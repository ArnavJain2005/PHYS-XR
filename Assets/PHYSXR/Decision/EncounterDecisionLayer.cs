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
    //       AttackDistance, the encounter telegraph (see below) running for
    //       >= WarnTelegraphMs, attack cooldown 0, inner result not PROTECT
    //       -> ATTACK
    //   E4  close pressure: encounterCommitted, player in the warn band, inner
    //       result FOLLOW or WARN -> WARN
    //   --  otherwise                    -> (effective) inner result
    //
    // Optional, set by the game (null and false by default here, so the rules
    // above are exactly as specified when they are not used):
    //
    //   SensorResponse (SensorResponseProfile): X3 INVESTIGATE for a qualified
    //       shake near the station at LOW confidence (held for the pulse
    //       unless R1-R3 take over), X1/X2 for a confirmed
    //       VERY_NEAR. Applied to the inner result before E2, so X1 is capped by
    //       E2 and passed by S like any PROTECT; G0, E0 and E1 still outrank it,
    //       and W, E5, E3, E6 and E4 apply after it as they do to an inner result.
    //   ProximityAttack: E6, the in-game proximity rule before the encounter is
    //       committed (E3/E4 already cover the committed encounter), on player
    //       and ghost positions only, never on a sensor:
    //         E6  ATTACK: not committed, no restart grace, distance <=
    //             AttackDistance, the encounter telegraph running for >=
    //             WarnTelegraphMs, attack cooldown 0 (ranked with E3, after E5)
    //         E6  WARN:   not committed, player in the threat zone (3.5 m, exit
    //             4.5 m), inner result FOLLOW or WARN (ranked with E4) - the
    //             telegraph. An INVESTIGATE (R3/X3) or PROTECT is never
    //             replaced, so physical input can still only delay an attack.
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
    //    outranks E3).
    //  - The telegraph E3 and E6 require is encounter-specific: it starts
    //    when this layer first decides an encounter warning (E4, E6 WARN, or
    //    the DWELL that holds one) and ends at any other decision. A WARN
    //    from the inner rules (R4-R7), the sensor response (X2) or a scripted
    //    beat (W) neither starts nor extends it, so physical input cannot
    //    shorten or skip the warning before an attack.
    //  - No sensor value appears in E1-E6. Physical input never starts an
    //    attack, and R1 PROTECT (through S) prevents one.
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
        private const string ReasonSensorProtect = "PROTECT[X1]: single-sensor VERY_NEAR in threat zone (sensor response; confidence LOW)";
        private const string ReasonSensorWarnLow = "WARN[X2]: single-sensor VERY_NEAR, not corroborated (sensor response; confidence LOW)";
        private const string ReasonSensorWarnHigh = "WARN[X2]: VERY_NEAR outside the threat zone, no rule matched (sensor response; confidence HIGH)";
        private const string ReasonSensorInvestigate = "INVESTIGATE[X3]: qualified physical disturbance near the station (sensor response; confidence LOW)";
        private const string ReasonProximityWarn = "WARN[E6]: player inside the ghost's threat zone (in-game proximity telegraph)";

        private static readonly string[] BeatReasons = BuildBeatReasons();
        private static readonly string[] CapReasons = BuildCapReasons();

        private readonly RuleBasedDecisionProvider inner;
        private readonly Func<long> clock;

        private GhostAction? previousAction;
        private long previousActionEnteredAt;

        // Encounter telegraph for E3/E6: when the current unbroken run of
        // encounter warnings (E4, E6 WARN, DWELL) began, or null when none is
        // running. Kept separate from previousActionEnteredAt, which any WARN
        // advances, so an unrelated WARN cannot pre-charge an attack.
        private long? telegraphStartedAt;
        private long? protectStartedAt;
        private long protectRefractoryUntil = long.MinValue;

        // Sensor-response state (unused while SensorResponse is null).
        private long presenceSince = long.MinValue;
        private long sensorHoldUntil = long.MinValue;
        private GhostAction sensorAction = GhostAction.FOLLOW;
        private bool sensorHighConfidence;
        private bool investigateLatched;

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

        // X1-X3 (see SensorResponseProfile). Null, the default here, keeps
        // the layer's behaviour exactly as specified above.
        public SensorResponseProfile SensorResponse { get; set; }

        // E6, the in-game proximity telegraph and attack before the encounter
        // is committed. False, the default here, keeps the layer as specified.
        public bool ProximityAttack { get; set; }

        // The inner provider's rule for the most recent call (R1-R8, or HYST
        // when its dwell retained the previous action). Telemetry only; this
        // layer's own LastRuleId ("INNER", "S", "E4", ...) is unchanged.
        public string InnerRuleId => inner.LastRuleId;

        // The action this layer returned last, or null before the first call.
        public GhostAction? PreviousAction => previousAction;

        // How long the encounter telegraph has been running at `now`, or -1
        // when no encounter warning is running.
        public long TelegraphElapsedMs(long now) => telegraphStartedAt.HasValue ? now - telegraphStartedAt.Value : -1L;

        private static bool IsEncounterWarning(GhostAction action, string ruleId) =>
            action == GhostAction.WARN && (ruleId == "E4" || ruleId == "E6" || ruleId == "DWELL");

        // Checkpoint restart: clears the inner provider's temporal context
        // (hysteresis history), this layer's own timers (telegraph, PROTECT
        // cap and refractory) and the last reason/rule. Calibration and the
        // R1-R8 rules are untouched.
        public void Reset()
        {
            inner.Reset();
            previousAction = null;
            previousActionEnteredAt = 0L;
            telegraphStartedAt = null;
            protectStartedAt = null;
            protectRefractoryUntil = long.MinValue;
            presenceSince = long.MinValue;
            sensorHoldUntil = long.MinValue;
            sensorAction = GhostAction.FOLLOW;
            sensorHighConfidence = false;
            investigateLatched = false;
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

            // Evaluated every call (before E0/E1 return) so the ConfirmMs
            // timer tracks the physical state continuously.
            GhostAction sensorResult = GhostAction.FOLLOW;
            string sensorRule = null;
            string sensorReason = null;
            bool sensorOverride = SensorResponse != null
                && ApplySensorResponse(state, now, innerAction, out sensorResult, out sensorRule, out sensorReason);

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

            if (sensorOverride)
            {
                effective = sensorResult;
                effectiveRule = sensorRule;
                effectiveReason = sensorReason;
            }

            if (effective == GhostAction.PROTECT)
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
                && TelegraphElapsedMs(now) >= WarnTelegraphMs)
            {
                Set(GhostAction.ATTACK, "E3", string.Format(CultureInfo.InvariantCulture,
                        "ATTACK[E3]: committed, warned {0:F1}s, distance {1:F1}m",
                        TelegraphElapsedMs(now) / 1000.0, player.distanceToGhost),
                    out action, out ruleId, out reason);
                return;
            }

            bool attackGrace = virtualWorld != null && virtualWorld.attackGraceActive;

            if (ProximityAttack && !committed && !attackGrace && player != null && ghost != null
                && player.distanceToGhost <= AttackDistance
                && ghost.attackCooldownMs <= 0L
                && TelegraphElapsedMs(now) >= WarnTelegraphMs)
            {
                Set(GhostAction.ATTACK, "E6", string.Format(CultureInfo.InvariantCulture,
                        "ATTACK[E6]: player too close to the ghost, warned {0:F1}s, distance {1:F1}m (in-game proximity)",
                        TelegraphElapsedMs(now) / 1000.0, player.distanceToGhost),
                    out action, out ruleId, out reason);
                return;
            }

            if (committed && player != null && player.inWarnBand
                && (effective == GhostAction.FOLLOW || effective == GhostAction.WARN))
            {
                Set(GhostAction.WARN, "E4", ReasonClosePressure, out action, out ruleId, out reason);
                return;
            }

            if (ProximityAttack && !committed && player != null && player.inThreatZone
                && (effective == GhostAction.FOLLOW || effective == GhostAction.WARN))
            {
                Set(GhostAction.WARN, "E6", ReasonProximityWarn, out action, out ruleId, out reason);
                return;
            }

            Set(effective, effectiveRule, effectiveReason, out action, out ruleId, out reason);
        }

        // X1-X3 (SensorResponseProfile). Returns true when a sensor-response
        // rule replaces the inner result this call. X3 only replaces R4 WARN or
        // R8 FOLLOW; X1/X2 only a plain R8 FOLLOW. The inner dwell holding a
        // WARN over an R8 candidate counts as R8 for both. Any other inner
        // result (an established rule, or the inner hysteresis retaining a
        // safety action or an INVESTIGATE) always wins and also ends an X1/X2
        // hold.
        private bool ApplySensorResponse(WorldState state, long now, GhostAction innerAction,
            out GhostAction action, out string ruleId, out string reason)
        {
            action = GhostAction.FOLLOW;
            ruleId = null;
            reason = null;

            PhysicalState physical = state?.physical;
            bool veryNear = physical != null && physical.presence == PresenceLevel.VERY_NEAR;
            bool singleSensor = veryNear && physical.confidence == ConfidenceLevel.LOW;

            // ConfirmMs debounces the ONSET of presence. A hand already held
            // at NEAR that moves closer has been present all along, so the
            // VERY_NEAR it reaches is confirmed at once; restarting the timer
            // there dropped a held R4 WARN to FOLLOW for ConfirmMs (SR-25).
            bool present = physical != null && physical.presence != PresenceLevel.NONE;
            if (!present)
                presenceSince = long.MinValue;
            else if (presenceSince == long.MinValue)
                presenceSince = now;

            string innerRule = inner.LastRuleId;

            // The inner dwell holding a WARN while its candidate is the idle
            // R8 FOLLOW: nothing established applies any more, the dwell only
            // stops a flicker to FOLLOW. For X2/X3 that is the idle case, so
            // they start at once instead of after up to MinimumActionDwellMs
            // (live COM6 run 23 Oct 20:39: NEAR -> VERY_NEAR reached X2 625 ms
            // late; SR-28, SR-29). A dwell holding PROTECT or RETREAT is
            // never this case (SR-30).
            bool heldWarnOverIdle = innerRule == RuleBasedDecisionProvider.HysteresisRuleId
                && innerAction == GhostAction.WARN && inner.LastCandidateRuleId == "R8";

            // X3: the disturbance pulse exists only for an accepted, fresh,
            // unmasked event while Playing (WorldStateManager); it is the
            // LOW-confidence counterpart of R3, which needs HIGH. Once started
            // it is latched for the pulse: shaking the station swings the ToF,
            // and neither a flicker to NONE (R5) nor the inner hysteresis may
            // end the search. A further shake restarts the pulse and so keeps
            // the search going. R1-R3 (and a PROTECT or RETREAT the inner
            // hysteresis is holding) always win, and the latch ends with the
            // pulse, so stale, masked or paused input still ends it.
            bool pulse = physical != null && physical.disturbance;
            if (!pulse)
                investigateLatched = false;

            bool innerUrgent = innerRule == "R1" || innerRule == "R2" || innerRule == "R3"
                || innerAction == GhostAction.PROTECT || innerAction == GhostAction.RETREAT;
            if (innerUrgent)
                investigateLatched = false;

            // Player at risk in threat zone with single-sensor VERY_NEAR is X1 PROTECT.
            // Safety PROTECT outranks the lower-priority search.
            bool inZone = state?.player != null && state.player.inThreatZone;
            bool x1Protect = singleSensor && inZone && SensorResponse.ProtectInThreatZone
                && (now - presenceSince >= SensorResponse.ConfirmMs);
            if (x1Protect)
                investigateLatched = false;

            if (!x1Protect && !investigateLatched && SensorResponse.InvestigateDisturbance && pulse
                && physical.confidence == ConfidenceLevel.LOW
                && (physical.presence == PresenceLevel.NEAR || physical.presence == PresenceLevel.VERY_NEAR)
                && ((innerAction == GhostAction.WARN && innerRule == "R4") || (innerAction == GhostAction.FOLLOW && innerRule == "R8")
                    || heldWarnOverIdle))
            {
                investigateLatched = true;
            }

            if (investigateLatched)
            {
                sensorHoldUntil = long.MinValue;
                action = GhostAction.INVESTIGATE;
                ruleId = "X3";
                reason = ReasonSensorInvestigate;
                return true;
            }

            bool innerIdle = (innerAction == GhostAction.FOLLOW && innerRule == "R8") || heldWarnOverIdle;
            if (!innerIdle)
            {
                sensorHoldUntil = long.MinValue;
                return false;
            }

            if (veryNear && now - presenceSince >= SensorResponse.ConfirmMs)
            {
                // HIGH confidence in the threat zone is R1, so X1 is only ever
                // the single-sensor (LOW) case.
                sensorAction = singleSensor && inZone && SensorResponse.ProtectInThreatZone ? GhostAction.PROTECT : GhostAction.WARN;
                sensorHighConfidence = !singleSensor;
                sensorHoldUntil = now + SensorResponse.HoldMs;
            }
            else if (sensorHoldUntil == long.MinValue || now >= sensorHoldUntil)
            {
                return false;
            }

            action = sensorAction;
            if (action == GhostAction.PROTECT)
            {
                ruleId = "X1";
                reason = ReasonSensorProtect;
            }
            else
            {
                ruleId = "X2";
                reason = sensorHighConfidence ? ReasonSensorWarnHigh : ReasonSensorWarnLow;
            }
            return true;
        }

        private void ApplyOuterDwell(long now, ref GhostAction action, ref string ruleId, ref string reason)
        {
            if (action != GhostAction.FOLLOW || previousAction != GhostAction.WARN)
                return;

            if (LastRuleId != "E4" && LastRuleId != "E6" && LastRuleId != "DWELL")
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

            if (IsEncounterWarning(action, ruleId))
            {
                if (!telegraphStartedAt.HasValue)
                    telegraphStartedAt = now;
            }
            else
            {
                telegraphStartedAt = null;
            }

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
