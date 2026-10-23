using System;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Core.Interfaces;
using PHYSXR.Decision;
using PHYSXR.Telemetry;

namespace PHYSXR.Ghost
{
    // Plain C#, Unity-independent orchestration extracted from
    // GhostRuntime.Tick() so the decide -> execute -> apply sequence (and
    // the optional telemetry emitted alongside it) stays directly testable
    // outside Unity, the same way every other stage of this pipeline
    // already is. GhostRuntime itself stays a thin MonoBehaviour wrapper:
    // it fetches WorldState from WorldStateManager and hands it here.
    //
    // DECISION CHAIN (runtime composition, see GhostRuntime):
    //   WorldState -> RuleBasedDecisionProvider (R1-R8, frozen)
    //              -> EncounterDecisionLayer (G0/E0/S/W arbitration)
    //              -> GhostAction -> GhostActionExecutor -> GhostExecutionState
    //              -> GhostBehaviourController
    // The pipeline depends only on IExplainableDecisionProvider, so it runs
    // whichever provider it is given and reports THAT provider's reason: with
    // the encounter layer in front, telemetry carries the final (outer)
    // action and reason, never the inner provider's suppressed candidate.
    //
    // TERMINAL GATE: once the run is terminal (WorldState.virtualWorld
    // .gameplayTerminal observed true, or EngageTerminalGate() called by the
    // gameplay session) the gate LATCHES and every following Run() is a no-op
    // for the ghost: no decision, no Execute(), no ApplyState(). The latch
    // survives the flag later reading false and any warning beat; only
    // RestartFromCheckpoint() clears it. The five-action decision contract is
    // unchanged - termination is not a GhostAction, it is an execution gate.
    //
    // TelemetrySink is optional (may be left null) and purely observational:
    // - it is never required for Run() to work correctly;
    // - a failing sink can never alter the resulting GhostAction/
    //   GhostExecutionState, because emitting a record is wrapped so any
    //   exception it throws is swallowed rather than propagated;
    // - no core component (RuleBasedDecisionProvider, GhostActionExecutor,
    //   GhostBehaviourController) was given an ITelemetrySink dependency to
    //   make this possible - this class only reads their already-public
    //   outputs (GhostAction, LastDecisionReason, GhostExecutionState).
    //
    // CHANGE-ONLY TELEMETRY (ChangeOnlyTelemetry = true, used by the game):
    // the default mode writes four stage records on every Run(), which at
    // frame rate is a constant allocation and an unbounded log. In change-only
    // mode the pipeline writes one DECISION record only when the action, the
    // rule ID or the reason (ignoring its parenthesised numbers) changes,
    // numbers it with an increasing decision ID, and carries the rule ID and
    // the causing physical event ID. The decision itself is identical in both
    // modes; only what is recorded differs.
    //
    // EVENT ATTRIBUTION (change-only mode). An accepted physical event reaches
    // the rules as WorldState's 3 s disturbance pulse, which carries its ID
    // (causeEventId). Only R3 and R5 read the pulse, so a decision is
    // attributed to the event (cause_evt) only when the encounter layer passed
    // the inner result through ("INNER") and that result came from R3 or R5,
    // or from the inner dwell retaining the action this same event already
    // caused. Decisions made while the pulse merely happens to be active
    // (PROTECT, a surge RETREAT, an encounter WARN) are not attributed.
    //
    // Each event the decision layer sees also gets exactly one
    // PHYS_EVENT_OUTCOME record:
    //   ACTED          the event produced a decision (written at that decision)
    //   OUTRANKED      R3's or R5's conditions held, but another rule's
    //                  decision was selected (a higher-priority rule, an
    //                  encounter rule, or the dwell)
    //   NOT_QUALIFIED  neither R3's nor R5's conditions held (e.g. the player
    //                  is in the threat zone, or confidence is LOW)
    // OUTRANKED and NOT_QUALIFIED describe the first decision that saw the
    // event, and are written when its pulse ends, the run turns terminal or
    // the run restarts, so an event that acts later in its pulse is ACTED.
    // Records carry the selected action, the winning rule and the inner rule.
    // Telemetry only: none of this is read by any decision.
    public class GhostRuntimePipeline
    {
        private readonly IExplainableDecisionProvider decisionProvider;
        private readonly IGhostActionExecutor actionExecutor;
        private readonly IGhostBehaviourController behaviourController;

        private GhostAction? lastEmittedAction;
        private string lastEmittedRule;
        private string lastEmittedReason;
        private bool terminalRecorded;

        public const string OutcomeActed = "ACTED";
        public const string OutcomeOutranked = "OUTRANKED";
        public const string OutcomeNotQualified = "NOT_QUALIFIED";

        // Event attribution state (see EVENT ATTRIBUTION above).
        private long trackedEventId;
        private bool trackedResolved;
        private GhostAction? trackedActedAction;
        private TelemetryRecord pendingOutcome;
        private long lastRunTimestamp;

        public ITelemetrySink TelemetrySink { get; set; }

        public bool ChangeOnlyTelemetry { get; set; }

        // Increases by one per DECISION record in change-only mode ("dec").
        public long CurrentDecisionId { get; private set; }

        // Outputs of the most recent decision, for presentation and the dev overlay.
        public GhostAction? LastAction { get; private set; }

        public string LastReason => decisionProvider.LastDecisionReason;

        public string LastRuleId => (decisionProvider as IRuleIdentifiedDecisionProvider)?.LastRuleId ?? string.Empty;

        public IExplainableDecisionProvider DecisionProvider => decisionProvider;

        // True once the terminal gate has latched. Only
        // RestartFromCheckpoint() clears it.
        public bool IsTerminalGateEngaged { get; private set; }

        // Runtime constructor: any explainable provider, normally
        // EncounterDecisionLayer wrapping RuleBasedDecisionProvider.
        public GhostRuntimePipeline(
            IExplainableDecisionProvider decisionProvider,
            IGhostActionExecutor actionExecutor,
            IGhostBehaviourController behaviourController)
        {
            this.decisionProvider = decisionProvider;
            this.actionExecutor = actionExecutor;
            this.behaviourController = behaviourController;
        }

        // R1-R8-only constructor, kept so the existing suites that build the
        // pipeline directly around RuleBasedDecisionProvider run unchanged.
        // It does NOT add the encounter layer: use the constructor above (as
        // GhostRuntime does) for the full chain.
        public GhostRuntimePipeline(
            RuleBasedDecisionProvider decisionProvider,
            IGhostActionExecutor actionExecutor,
            IGhostBehaviourController behaviourController)
            : this(new RuleBasedExplainableAdapter(decisionProvider), actionExecutor, behaviourController)
        {
        }

        // The runtime's decision chain in ONE place: R1-R8 provider wrapped by
        // the encounter layer. GhostRuntime and the integration tests both
        // build their pipeline through this, so what the tests exercise is the
        // composition the game actually runs.
        public static GhostRuntimePipeline CreateRuntimeChain(
            RuleBasedDecisionProvider ruleBasedProvider,
            IGhostActionExecutor actionExecutor,
            IGhostBehaviourController behaviourController)
        {
            return new GhostRuntimePipeline(
                new EncounterDecisionLayer(ruleBasedProvider),
                actionExecutor,
                behaviourController);
        }

        // The same chain with the monotonic decision clock (fix P1) injected
        // into both layers. This is what the game runs.
        public static GhostRuntimePipeline CreateRuntimeChain(
            Func<long> decisionClock,
            IGhostActionExecutor actionExecutor,
            IGhostBehaviourController behaviourController)
        {
            if (decisionClock == null)
                throw new ArgumentNullException(nameof(decisionClock));

            var ruleBased = new RuleBasedDecisionProvider(decisionClock: decisionClock);
            return new GhostRuntimePipeline(
                new EncounterDecisionLayer(ruleBased, decisionClock),
                actionExecutor,
                behaviourController);
        }

        public GhostExecutionState Run(WorldState worldState, long timestamp)
        {
            if (!IsTerminalGateEngaged && worldState?.virtualWorld != null && worldState.virtualWorld.gameplayTerminal)
                EngageTerminalGate();

            if (IsTerminalGateEngaged)
            {
                if (!ChangeOnlyTelemetry || !terminalRecorded)
                    Emit(TelemetryEventType.WorldStateEvaluated, timestamp,
                        detail: "TERMINAL GATE: ghost decision, execution and behaviour suppressed");
                terminalRecorded = true;
                // Not a new command: the controller's last state, unchanged.
                return behaviourController.CurrentState;
            }

            if (!ChangeOnlyTelemetry)
                Emit(TelemetryEventType.WorldStateEvaluated, timestamp);

            GhostAction action = decisionProvider.Decide(worldState);
            LastAction = action;
            lastRunTimestamp = timestamp;
            if (ChangeOnlyTelemetry)
            {
                long eventId = worldState?.physical?.causeEventId ?? 0L;
                if (eventId != trackedEventId)
                {
                    FlushPendingOutcome(timestamp);
                    trackedEventId = eventId;
                    trackedResolved = false;
                    trackedActedAction = null;
                }

                bool caused = IsCausedByTrackedEvent(action);
                EmitDecisionIfChanged(timestamp, action, caused ? trackedEventId : 0L);
                TrackEventOutcome(timestamp, action, caused);
            }
            else
                Emit(TelemetryEventType.DecisionMade, timestamp, ghostAction: action, reason: decisionProvider.LastDecisionReason);

            GhostExecutionState executionState = actionExecutor.Execute(action);
            if (!ChangeOnlyTelemetry)
                Emit(TelemetryEventType.GhostActionExecuted, timestamp, ghostAction: action, ghostExecutionState: executionState);

            behaviourController.ApplyState(executionState);
            if (!ChangeOnlyTelemetry)
                Emit(TelemetryEventType.GhostExecutionStateChanged, timestamp, ghostExecutionState: executionState);

            return executionState;
        }

        private void EmitDecisionIfChanged(long timestamp, GhostAction action, long cause)
        {
            string rule = LastRuleId;
            string reason = decisionProvider.LastDecisionReason;

            if (lastEmittedAction == action && rule == lastEmittedRule && SameReasonIgnoringNumbers(reason, lastEmittedReason))
                return;

            GhostAction? previous = lastEmittedAction;
            lastEmittedAction = action;
            lastEmittedRule = rule;
            lastEmittedReason = reason;
            CurrentDecisionId++;

            if (TelemetrySink == null)
                return;

            try
            {
                TelemetrySink.Record(new TelemetryRecord
                {
                    timestamp = timestamp,
                    eventType = TelemetryEventType.DecisionMade,
                    type = "DECISION",
                    ghostAction = action,
                    reason = reason,
                    ruleId = rule,
                    decisionId = CurrentDecisionId,
                    causeEventId = cause != 0L ? cause : (long?)null,
                    relatedId = cause != 0L ? cause : (long?)null,
                    detail = previous.HasValue ? "prev=" + previous.Value : "prev=NONE"
                });
            }
            catch
            {
                // Telemetry must never affect the decision pipeline.
            }
        }

        // The inner R1-R8 provider behind the pipeline's provider, if any.
        private RuleBasedDecisionProvider InnerRules =>
            (decisionProvider as EncounterDecisionLayer)?.Inner ?? (decisionProvider as RuleBasedExplainableAdapter)?.Provider;

        // True when the encounter layer (if present) passed the inner result through.
        private bool InnerResultSelected =>
            decisionProvider is RuleBasedExplainableAdapter || LastRuleId == "INNER";

        private bool IsCausedByTrackedEvent(GhostAction action)
        {
            RuleBasedDecisionProvider inner = InnerRules;
            if (trackedEventId == 0L || inner == null || !InnerResultSelected)
                return false;

            string rule = inner.LastRuleId;
            if (rule == "R3" || rule == "R5")
                return true;

            // The dwell holding the action this event already caused.
            return rule == RuleBasedDecisionProvider.HysteresisRuleId && trackedActedAction == action;
        }

        private void TrackEventOutcome(long timestamp, GhostAction action, bool caused)
        {
            if (trackedEventId == 0L || trackedResolved)
                return;

            if (caused)
            {
                trackedActedAction = action;
                trackedResolved = true;
                pendingOutcome = null;
                EmitOutcome(BuildOutcome(timestamp, action, OutcomeActed));
                return;
            }

            if (pendingOutcome == null)
            {
                RuleBasedDecisionProvider inner = InnerRules;
                bool eligible = inner != null && inner.LastDisturbanceRuleEligible;
                pendingOutcome = BuildOutcome(timestamp, action, eligible ? OutcomeOutranked : OutcomeNotQualified);
            }
        }

        // Writes the outcome of an event that never acted. Called when its
        // pulse ends (or another event replaces it), at the terminal gate and
        // at a checkpoint restart.
        private void FlushPendingOutcome(long timestamp)
        {
            if (pendingOutcome != null && !trackedResolved)
            {
                pendingOutcome.timestamp = timestamp;
                EmitOutcome(pendingOutcome);
            }

            trackedResolved = true;
            pendingOutcome = null;
        }

        private TelemetryRecord BuildOutcome(long timestamp, GhostAction action, string outcome)
        {
            RuleBasedDecisionProvider inner = InnerRules;
            return new TelemetryRecord
            {
                timestamp = timestamp,
                eventType = TelemetryEventType.PhysicalEventRegistered,
                type = "PHYS_EVENT_OUTCOME",
                ghostAction = action,
                reason = decisionProvider.LastDecisionReason,
                ruleId = LastRuleId,
                decisionId = CurrentDecisionId,
                relatedId = trackedEventId,
                detail = "evt=" + trackedEventId + " outcome=" + outcome
                    + " inner=" + (inner?.LastRuleId ?? string.Empty)
                    + " candidate=" + (inner?.LastCandidateRuleId ?? string.Empty)
            };
        }

        private void EmitOutcome(TelemetryRecord record)
        {
            if (TelemetrySink == null)
                return;

            try
            {
                TelemetrySink.Record(record);
            }
            catch
            {
                // Telemetry must never affect the decision pipeline.
            }
        }

        // Compares two reasons up to their first '(' so that a hysteresis
        // reason whose elapsed-time numbers change every frame is still "the
        // same reason". Allocation-free.
        private static bool SameReasonIgnoringNumbers(string a, string b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;

            int i = 0;
            while (true)
            {
                bool endA = i >= a.Length || a[i] == '(';
                bool endB = i >= b.Length || b[i] == '(';
                if (endA || endB)
                    return endA && endB;
                if (a[i] != b[i])
                    return false;
                i++;
            }
        }

        // Latches the terminal gate and halts the behaviour controller.
        // Idempotent. Called by Run() when it sees gameplayTerminal, and by
        // GameplaySession.EnterTerminal().
        public void EngageTerminalGate()
        {
            if (IsTerminalGateEngaged)
                return;

            IsTerminalGateEngaged = true;
            FlushPendingOutcome(lastRunTimestamp);
            behaviourController.Halt();
        }

        // Checkpoint restart: clears the gate, returns the behaviour
        // controller to its initial state and resets the decision provider's
        // transient state (hysteresis history). It does NOT touch WorldState;
        // use GameplaySession.RestartFromCheckpoint(), which also clears the
        // terminal/beat/phase flags - otherwise a stale gameplayTerminal=true
        // would simply re-latch the gate on the next Run().
        public void RestartFromCheckpoint()
        {
            FlushPendingOutcome(lastRunTimestamp);
            trackedEventId = 0L;
            trackedActedAction = null;
            IsTerminalGateEngaged = false;
            terminalRecorded = false;
            behaviourController.Reset();
            decisionProvider.Reset();
            LastAction = null;
            lastEmittedAction = null;
            lastEmittedRule = null;
            lastEmittedReason = null;
        }

        private void Emit(
            TelemetryEventType eventType,
            long timestamp,
            GhostAction? ghostAction = null,
            GhostExecutionState? ghostExecutionState = null,
            string reason = null,
            string detail = null)
        {
            if (TelemetrySink == null)
                return;

            try
            {
                TelemetrySink.Record(new TelemetryRecord
                {
                    timestamp = timestamp,
                    eventType = eventType,
                    ghostAction = ghostAction,
                    ghostExecutionState = ghostExecutionState,
                    reason = reason,
                    detail = detail
                });
            }
            catch
            {
                // Telemetry must never affect the Ghost decision/execution
                // pipeline - a failing sink is swallowed here, not propagated.
            }
        }

        // Presents the frozen RuleBasedDecisionProvider through the pipeline's
        // interface without editing it. Pure delegation, no logic.
        private sealed class RuleBasedExplainableAdapter : IExplainableDecisionProvider
        {
            private readonly RuleBasedDecisionProvider provider;

            public RuleBasedExplainableAdapter(RuleBasedDecisionProvider provider)
            {
                this.provider = provider;
            }

            public RuleBasedDecisionProvider Provider => provider;

            public string LastDecisionReason => provider.LastDecisionReason;

            public GhostAction Decide(WorldState worldState) => provider.Decide(worldState);

            public void Reset() => provider.Reset();
        }
    }
}
