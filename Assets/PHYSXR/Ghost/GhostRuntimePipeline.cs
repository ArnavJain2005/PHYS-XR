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
    public class GhostRuntimePipeline
    {
        private readonly IExplainableDecisionProvider decisionProvider;
        private readonly IGhostActionExecutor actionExecutor;
        private readonly IGhostBehaviourController behaviourController;

        public ITelemetrySink TelemetrySink { get; set; }

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

        public GhostExecutionState Run(WorldState worldState, long timestamp)
        {
            if (!IsTerminalGateEngaged && worldState?.virtualWorld != null && worldState.virtualWorld.gameplayTerminal)
                EngageTerminalGate();

            if (IsTerminalGateEngaged)
            {
                Emit(TelemetryEventType.WorldStateEvaluated, timestamp,
                    detail: "TERMINAL GATE: ghost decision, execution and behaviour suppressed");
                // Not a new command: the controller's last state, unchanged.
                return behaviourController.CurrentState;
            }

            Emit(TelemetryEventType.WorldStateEvaluated, timestamp);

            GhostAction action = decisionProvider.Decide(worldState);
            Emit(TelemetryEventType.DecisionMade, timestamp, ghostAction: action, reason: decisionProvider.LastDecisionReason);

            GhostExecutionState executionState = actionExecutor.Execute(action);
            Emit(TelemetryEventType.GhostActionExecuted, timestamp, ghostAction: action, ghostExecutionState: executionState);

            behaviourController.ApplyState(executionState);
            Emit(TelemetryEventType.GhostExecutionStateChanged, timestamp, ghostExecutionState: executionState);

            return executionState;
        }

        // Latches the terminal gate and halts the behaviour controller.
        // Idempotent. Called by Run() when it sees gameplayTerminal, and by
        // GameplaySession.EnterTerminal().
        public void EngageTerminalGate()
        {
            if (IsTerminalGateEngaged)
                return;

            IsTerminalGateEngaged = true;
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
            IsTerminalGateEngaged = false;
            behaviourController.Reset();
            decisionProvider.Reset();
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

            public string LastDecisionReason => provider.LastDecisionReason;

            public GhostAction Decide(WorldState worldState) => provider.Decide(worldState);

            public void Reset() => provider.Reset();
        }
    }
}
