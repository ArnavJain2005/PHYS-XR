using System;
using System.Globalization;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Core.Interfaces;

namespace PHYSXR.Decision
{
    // Game-level arbitration in front of the frozen R1-R8 provider
    // (Technical Spec section D).
    //
    // R1-R8 (RuleBasedDecisionProvider) are NOT modified. The inner provider
    // is evaluated on every call so its DecisionHistory/hysteresis stays
    // coherent; this layer only decides whether its result is replaced.
    //
    // Precedence, first match wins:
    //   G0  gameplayTerminal             -> inner result, unchanged (no beat may override).
    //       This is decision-level defence in depth only. The terminal state is
    //       actually ENFORCED one level down: GhostRuntimePipeline's terminal
    //       gate stops the decide/execute/apply sequence from running at all.
    //   E0  ghost phase INTERRUPTED      -> RETREAT (lantern interruption)
    //   E1  ghost phase WINDUP/STRIKE/RECOVER -> ATTACK (an attack in progress
    //       runs its bounded window to the end)
    //   E2  inner PROTECT has run for ProtectCapMs, or its refractory is
    //       active -> the inner result is treated as FOLLOW from here on
    //   S   (effective) inner PROTECT/RETREAT -> inner result (genuine safety response)
    //   W   mandatoryWarningBeat         -> WARN
    //   E5  first-sighting attack: the game reports the player's first clear
    //       view of the ghost (firstSightingAttack), no restart grace, attack
    //       cooldown 0 -> ATTACK. A one-shot scripted beat: the game raises
    //       the flag once per run and clears it when the attack starts.
    //   E3  attack starts: encounterCommitted, no restart grace, distance <=
    //       AttackDistance, previous decision WARN for >= WarnTelegraphMs,
    //       attack cooldown 0, inner result not PROTECT -> ATTACK
    //   E4  close pressure: encounterCommitted, player in the warn band, inner
    //       result FOLLOW or WARN -> WARN
    //   --  otherwise                    -> (effective) inner result
    //
    // Then one outer dwell: a close-pressure WARN (E4) is not dropped to
    // FOLLOW until it has been held for OuterDwellMs, matching the inner
    // hysteresis, so the warn-band edge cannot make WARN and FOLLOW flicker.
    // A scripted beat (W) still ends the moment the beat ends (RT-11), and
    // WARN from the inner rules already has the inner hysteresis.
    //
    // Decisions on deferred questions (recorded here and in the docs):
    //  - A mandatory warning beat does NOT stop an attack already in progress
    //    (E1 outranks W), but no NEW attack can start during a beat (W
    //    outranks E3). The beat itself is a WARN, so it counts as the
    //    telegraph E3 requires.
    //  - No sensor value appears in E1-E5. Physical input can only PREVENT an
    //    attack (R1 PROTECT through S), never cause one.
    //  - The first-sighting attack (E5) ranks below the warning beat, so the
    //    game ends the manifest beat when it raises the sighting flag.
    //
    // Deterministic: given the WorldState sequence and the injected clock,
    // the output is fixed. Without a clock, WorldState.physical.timestamp is
    // used, like the inner provider's legacy mode.
    public class EncounterDecisionLayer : IExplainableDecisionProvider, IRuleIdentifiedDecisionProvider
    {
        public const long DefaultProtectCapMs = 6000L;
        public const long DefaultProtectRefractoryMs = 10000L;
        public const long DefaultWarnTelegraphMs = 1500L;
        public const long DefaultOuterDwellMs = 1500L;
        public const float DefaultAttackDistance = 2.0f;

        private const string ReasonInterrupted = "RETREAT[E0]: ghost interrupted by defensive pulse";
        private const string ReasonAttackContinues = "ATTACK[E1]: attack in progress";
        private const string ReasonFirstSighting = "ATTACK[E5]: first sighting of the player";
        private const string ReasonClosePressure = "WARN[E4]: committed encounter, player inside the warn band";
        private const string ReasonOuterDwell = "HYSTERESIS[outer]: retaining WARN; candidate FOLLOW suppressed until minimum dwell elapsed";

        private static readonly string[] BeatReasons = BuildBeatReasons();
        private static readonly string[] CapReasons = BuildCapReasons();

        private readonly RuleBasedDecisionProvider inner;
        private readonly Func<long> clock;

        private GhostAction? previousAction;
        private long previousActionEnteredAt;
        private long? protectStartedAt;
        private long protectRefractoryUntil = long.MinValue;

        public string LastDecisionReason { get; private set; } = string.Empty;
        public string LastRuleId { get; private set; } = string.Empty;

        public long ProtectCapMs { get; }
        public long ProtectRefractoryMs { get; }
        public long WarnTelegraphMs { get; }
        public long OuterDwellMs { get; }
        public float AttackDistance { get; }

        public EncounterDecisionLayer(RuleBasedDecisionProvider inner)
            : this(inner, null)
        {
        }

        public EncounterDecisionLayer(
            RuleBasedDecisionProvider inner,
            Func<long> clock,
            long protectCapMs = DefaultProtectCapMs,
            long protectRefractoryMs = DefaultProtectRefractoryMs,
            long warnTelegraphMs = DefaultWarnTelegraphMs,
            long outerDwellMs = DefaultOuterDwellMs,
            float attackDistance = DefaultAttackDistance)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.clock = clock;
            ProtectCapMs = protectCapMs;
            ProtectRefractoryMs = protectRefractoryMs;
            WarnTelegraphMs = warnTelegraphMs;
            OuterDwellMs = outerDwellMs;
            AttackDistance = attackDistance;
        }

        public RuleBasedDecisionProvider Inner => inner;

        // The inner provider's rule for the most recent call (R1-R8, or HYST
        // when its dwell retained the previous action). Telemetry only; this
        // layer's own LastRuleId ("INNER", "S", "E4", ...) is unchanged.
        public string InnerRuleId => inner.LastRuleId;

        // The action this layer returned last, or null before the first call.
        public GhostAction? PreviousAction => previousAction;

        // Checkpoint restart: clears the inner provider's temporal context
        // (hysteresis history), this layer's own timers (telegraph, PROTECT
        // cap and refractory) and the last reason/rule. Calibration and the
        // R1-R8 rules are untouched.
        public void Reset()
        {
            inner.Reset();
            previousAction = null;
            previousActionEnteredAt = 0L;
            protectStartedAt = null;
            protectRefractoryUntil = long.MinValue;
            LastDecisionReason = string.Empty;
            LastRuleId = string.Empty;
        }

        public GhostAction Decide(WorldState state)
        {
            long now = clock != null ? clock() : state?.physical?.timestamp ?? 0L;

            GhostAction innerAction = inner.Decide(state);
            string innerReason = inner.LastDecisionReason;

            Arbitrate(state, now, innerAction, innerReason, out GhostAction action, out string ruleId, out string reason);
            ApplyOuterDwell(now, ref action, ref ruleId, ref reason);
            Record(now, action, ruleId, reason);
            return action;
        }

        private void Arbitrate(WorldState state, long now, GhostAction innerAction, string innerReason,
            out GhostAction action, out string ruleId, out string reason)
        {
            VirtualState virtualWorld = state?.virtualWorld;
            GhostState ghost = state?.ghost;
            PlayerState player = state?.player;

            if (virtualWorld != null && virtualWorld.gameplayTerminal)
            {
                Set(innerAction, "G0", "TERMINAL: gameplay terminal state, no game-level override | " + innerReason,
                    out action, out ruleId, out reason);
                return;
            }

            GhostPhase phase = ghost?.phase ?? GhostPhase.NORMAL;

            if (phase == GhostPhase.INTERRUPTED)
            {
                Set(GhostAction.RETREAT, "E0", ReasonInterrupted, out action, out ruleId, out reason);
                return;
            }

            if (phase == GhostPhase.WINDUP || phase == GhostPhase.STRIKE || phase == GhostPhase.RECOVER)
            {
                Set(GhostAction.ATTACK, "E1", ReasonAttackContinues, out action, out ruleId, out reason);
                return;
            }

            GhostAction effective = innerAction;
            string effectiveRule = "INNER";
            string effectiveReason = innerReason;

            if (innerAction == GhostAction.PROTECT)
            {
                bool refractory = now < protectRefractoryUntil;
                bool capReached = protectStartedAt.HasValue && now - protectStartedAt.Value >= ProtectCapMs;
                if (capReached && !refractory)
                {
                    protectRefractoryUntil = now + ProtectRefractoryMs;
                    refractory = true;
                }

                if (refractory)
                {
                    effective = GhostAction.FOLLOW;
                    effectiveRule = "E2";
                    effectiveReason = CapReasons[capReached ? 1 : 0];
                }
            }

            if (effective == GhostAction.PROTECT || effective == GhostAction.RETREAT)
            {
                Set(effective, "S", effectiveReason, out action, out ruleId, out reason);
                return;
            }

            if (virtualWorld != null && virtualWorld.mandatoryWarningBeat)
            {
                Set(GhostAction.WARN, "W", BeatReasons[(int)innerAction], out action, out ruleId, out reason);
                return;
            }

            if (virtualWorld != null && virtualWorld.firstSightingAttack && !virtualWorld.attackGraceActive
                && ghost != null && ghost.attackCooldownMs <= 0L)
            {
                Set(GhostAction.ATTACK, "E5", ReasonFirstSighting, out action, out ruleId, out reason);
                return;
            }

            bool committed = virtualWorld != null && virtualWorld.encounterCommitted;

            if (committed && !virtualWorld.attackGraceActive && player != null && ghost != null
                && player.distanceToGhost <= AttackDistance
                && ghost.attackCooldownMs <= 0L
                && previousAction == GhostAction.WARN
                && now - previousActionEnteredAt >= WarnTelegraphMs)
            {
                Set(GhostAction.ATTACK, "E3", string.Format(CultureInfo.InvariantCulture,
                        "ATTACK[E3]: committed, warned {0:F1}s, distance {1:F1}m",
                        (now - previousActionEnteredAt) / 1000.0, player.distanceToGhost),
                    out action, out ruleId, out reason);
                return;
            }

            if (committed && player != null && player.inWarnBand
                && (effective == GhostAction.FOLLOW || effective == GhostAction.WARN))
            {
                Set(GhostAction.WARN, "E4", ReasonClosePressure, out action, out ruleId, out reason);
                return;
            }

            Set(effective, effectiveRule, effectiveReason, out action, out ruleId, out reason);
        }

        private void ApplyOuterDwell(long now, ref GhostAction action, ref string ruleId, ref string reason)
        {
            if (action != GhostAction.FOLLOW || previousAction != GhostAction.WARN)
                return;

            if (LastRuleId != "E4" && LastRuleId != "DWELL")
                return;

            if (now - previousActionEnteredAt >= OuterDwellMs)
                return;

            action = GhostAction.WARN;
            ruleId = "DWELL";
            reason = ReasonOuterDwell;
        }

        private void Record(long now, GhostAction action, string ruleId, string reason)
        {
            if (!previousAction.HasValue || previousAction.Value != action)
                previousActionEnteredAt = now;

            if (action == GhostAction.PROTECT)
            {
                if (!protectStartedAt.HasValue)
                    protectStartedAt = now;
            }
            else
            {
                protectStartedAt = null;
            }

            previousAction = action;
            LastRuleId = ruleId;
            LastDecisionReason = reason;
        }

        private static void Set(GhostAction value, string rule, string why,
            out GhostAction action, out string ruleId, out string reason)
        {
            action = value;
            ruleId = rule;
            reason = why;
        }

        // Precomputed so the per-frame warning beat does not allocate.
        private static string[] BuildBeatReasons()
        {
            var values = (GhostAction[])Enum.GetValues(typeof(GhostAction));
            var reasons = new string[values.Length];
            foreach (GhostAction value in values)
                reasons[(int)value] = "WARN[W]: mandatory scripted warning beat (inner candidate " + value + ")";
            return reasons;
        }

        private static string[] BuildCapReasons()
        {
            return new[]
            {
                "FOLLOW[E2]: PROTECT refractory active; inner PROTECT treated as FOLLOW",
                "FOLLOW[E2]: PROTECT cap reached; inner PROTECT treated as FOLLOW"
            };
        }
    }
}
