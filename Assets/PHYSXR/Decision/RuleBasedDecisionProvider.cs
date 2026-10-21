using System;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Core.Interfaces;

namespace PHYSXR.Decision
{
    // Deterministic MVP decision provider (architecture ARCH-05). Implements
    // the approved first-match-wins rule set R1-R8. Consumes ONLY WorldState:
    // no sensor access, no networking, no Unity API dependency, no
    // randomness, no timers.
    //
    // The source architecture explicitly leaves exact numeric thresholds
    // unfrozen (implementation/calibration parameters, not architecture
    // requirements). ElevatedThreatThreshold/CriticalThreatThreshold are
    // therefore constructor parameters with sensible temporary defaults,
    // not hardcoded magic numbers, so they can be recalibrated later
    // without touching the rule logic. MinimumActionDwellMs (hysteresis)
    // follows the same pattern.
    //
    // HYSTERESIS: R1-R8 (SelectCandidate below) are completely unchanged -
    // same conditions, same order, same GhostAction outputs, same reason
    // strings. A single post-processing step (ApplyHysteresisAndRecord)
    // decides whether the selected candidate is actually returned, or
    // whether the previous action is retained a little longer to avoid
    // rapid de-escalation flicker. See that method for the exact policy.
    //
    // DECISION CLOCK (fix P1): hysteresis originally measured dwell with
    // WorldState.physical.timestamp. That timestamp is the sensor's, so it
    // stops advancing whenever no packets arrive (virtual-only mode, a sensor
    // dropout), and the provider then froze in its last action forever. An
    // optional monotonic clock can now be injected; when present, every
    // temporal-context timestamp comes from it instead. Without one, the
    // provider behaves exactly as before, so the existing suites still apply.
    public class RuleBasedDecisionProvider : IDecisionProvider
    {
        // Calibration parameters - NOT frozen architecture values.
        public float ElevatedThreatThreshold { get; }
        public float CriticalThreatThreshold { get; }

        // Hysteresis calibration parameter - NOT a frozen architecture
        // value. A minimum time (measured via WorldState.physical.timestamp,
        // never wall-clock) that must elapse in the current action before
        // certain de-escalating transitions are allowed through. 0
        // effectively disables hysteresis (every candidate is applied
        // immediately, reproducing pre-hysteresis behaviour).
        public long MinimumActionDwellMs { get; }

        // Explainability (architecture §8.4): read-only reason for the most
        // recently returned decision. Decide() never reads this field, so
        // it cannot influence the decision - it is written only after the
        // action has already been selected. When hysteresis suppresses a
        // candidate, this holds a structured "HYSTERESIS: ..." explanation
        // instead of the suppressed candidate's genuine rule reason.
        public string LastDecisionReason { get; private set; }
            = "FOLLOW: no active threat or event requiring intervention";

        // Deterministic temporal-context foundation (owned, not injected -
        // there is only ever one per provider instance). Populated by
        // ApplyHysteresisAndRecord with the FINAL action every call (never
        // a suppressed candidate), and read by ApplyHysteresisAndRecord to
        // decide whether to gate the next candidate.
        //
        // Exposed read-only so tests can inspect it, but it is not part
        // of IDecisionProvider and is not meant to be read by anything
        // outside the decision layer.
        private readonly DecisionHistory temporalContext = new DecisionHistory();
        public DecisionHistory TemporalContext => temporalContext;

        // Optional monotonic decision clock in milliseconds (P1). Null means
        // the legacy time source, WorldState.physical.timestamp.
        private readonly Func<long> decisionClock;

        public bool HasDecisionClock => decisionClock != null;

        public RuleBasedDecisionProvider(
            float elevatedThreatThreshold = 0.4f,
            float criticalThreatThreshold = 0.7f,
            long minimumActionDwellMs = 1500L,
            Func<long> decisionClock = null)
        {
            ElevatedThreatThreshold = elevatedThreatThreshold;
            CriticalThreatThreshold = criticalThreatThreshold;
            MinimumActionDwellMs = minimumActionDwellMs;
            this.decisionClock = decisionClock;
        }

        // Clears the temporal context (and therefore all hysteresis state,
        // which lives entirely inside DecisionHistory - see
        // ApplyHysteresisAndRecord) back to a fresh/neutral state. Does
        // not affect ElevatedThreatThreshold/CriticalThreatThreshold/
        // MinimumActionDwellMs or LastDecisionReason. Never called
        // automatically; a runtime restart (new provider instance)
        // naturally starts clean without needing this.
        public void Reset()
        {
            temporalContext.Reset();
        }

        public GhostAction Decide(WorldState state)
        {
            (GhostAction candidateAction, string candidateReason) = SelectCandidate(state);
            return ApplyHysteresisAndRecord(candidateAction, candidateReason, state);
        }

        // R1-R8, unchanged: same conditions, same first-match-wins order,
        // same GhostAction values, same reason strings as before
        // hysteresis existed. Only the shape changed (a candidate is
        // returned rather than immediately resolved/recorded), so this
        // method can be unit-tested and unaffected by hysteresis policy.
        private (GhostAction, string) SelectCandidate(WorldState state)
        {
            PhysicalState physical = state?.physical;
            PlayerState player = state?.player;
            VirtualState virtualWorld = state?.virtualWorld;

            // PlayerState.movement, GhostState.currentState and
            // PhysicalState.timestamp are deliberately not read here per the
            // approved MVP rule policy - they remain valid WorldState fields
            // for future revisions.
            PresenceLevel presence = physical?.presence ?? PresenceLevel.NONE;
            ConfidenceLevel confidence = physical?.confidence ?? ConfidenceLevel.LOW;
            bool disturbance = physical?.disturbance ?? false;

            bool inThreatZone = player?.inThreatZone ?? false;

            float threatLevel = virtualWorld?.threatLevel ?? 0f;
            bool eventActive = virtualWorld?.eventActive ?? false;

            // R1 - CRITICAL PLAYER RISK
            if (inThreatZone && presence == PresenceLevel.VERY_NEAR && confidence == ConfidenceLevel.HIGH)
                return (GhostAction.PROTECT, "PROTECT: player at risk + high-confidence VERY_NEAR physical presence");

            // R2 - CRITICAL VIRTUAL/GENERAL THREAT
            if (threatLevel >= CriticalThreatThreshold && !inThreatZone)
                return (GhostAction.RETREAT, "RETREAT: sufficiently high threat without active player-risk condition");

            // R3 - QUALIFIED PHYSICAL EVENT
            if ((presence == PresenceLevel.NEAR || presence == PresenceLevel.VERY_NEAR)
                && disturbance
                && confidence == ConfidenceLevel.HIGH
                && !inThreatZone)
                return (GhostAction.INVESTIGATE, "INVESTIGATE: qualified physical disturbance detected");

            // R4 - UNCERTAIN PHYSICAL CONDITION
            if (presence == PresenceLevel.NEAR && confidence == ConfidenceLevel.LOW && !inThreatZone)
                return (GhostAction.WARN, "WARN: physical condition detected with low confidence");

            // R5 - QUALIFIED DISTURBANCE WITHOUT PROXIMITY
            if (disturbance && presence == PresenceLevel.NONE && !inThreatZone)
                return (GhostAction.WARN, "WARN: disturbance detected without proximity confirmation");

            // R6 - VIRTUAL EVENT (MVP: never INVESTIGATE for a purely virtual event)
            if (eventActive && presence == PresenceLevel.NONE && !inThreatZone && threatLevel < CriticalThreatThreshold)
                return (GhostAction.WARN, "WARN: virtual event detected");

            // R7 - ELEVATED VIRTUAL THREAT
            if (threatLevel >= ElevatedThreatThreshold
                && threatLevel < CriticalThreatThreshold
                && !inThreatZone
                && presence == PresenceLevel.NONE)
                return (GhostAction.WARN, "WARN: elevated virtual threat");

            // R8 - NORMAL
            return (GhostAction.FOLLOW, "FOLLOW: no active threat or event requiring intervention");
        }

        // The hysteresis policy, applied in exactly this order every call,
        // and the single choke point that records into DecisionHistory
        // and sets LastDecisionReason - both always reflect the FINAL
        // action, never the raw candidate, so a suppressed candidate can
        // never corrupt PreviousAction/TimeEnteredCurrentAction.
        //
        //   0. No previous action yet (first call ever, or first call
        //      after Reset()) -> apply the candidate immediately.
        //   1. Candidate is PROTECT or RETREAT -> apply immediately.
        //      A genuine safety escalation is NEVER gated.
        //   2. Previous action is PROTECT or RETREAT and the candidate is
        //      not (guaranteed by rule 1 not having matched) -> gated by
        //      MinimumActionDwellMs.
        //   3. Candidate is FOLLOW and the previous action is not FOLLOW
        //      -> gated by MinimumActionDwellMs.
        //   4. Otherwise -> apply immediately (FOLLOW->WARN/INVESTIGATE,
        //      WARN<->INVESTIGATE, same-action repeats, etc.).
        //
        // No global GhostAction ranking, no transition matrix, no
        // threshold deadbands, no separate safety-exit dwell parameter,
        // no repeated-event gating, and no additional persistent state
        // beyond what DecisionHistory already tracks.
        private GhostAction ApplyHysteresisAndRecord(GhostAction candidateAction, string candidateReason, WorldState state)
        {
            long timestamp = decisionClock != null ? decisionClock() : state?.physical?.timestamp ?? 0L;
            bool disturbanceActive = state?.physical?.disturbance ?? false;

            GhostAction? previousAction = temporalContext.PreviousAction;

            GhostAction finalAction;
            string finalReason;

            if (!previousAction.HasValue)
            {
                // Rule 0.
                finalAction = candidateAction;
                finalReason = candidateReason;
            }
            else if (IsSafetyAction(candidateAction))
            {
                // Rule 1.
                finalAction = candidateAction;
                finalReason = candidateReason;
            }
            else if (IsSafetyAction(previousAction.Value))
            {
                // Rule 2.
                (finalAction, finalReason) = ApplyDwellGate(candidateAction, candidateReason, previousAction.Value, timestamp);
            }
            else if (candidateAction == GhostAction.FOLLOW && previousAction.Value != GhostAction.FOLLOW)
            {
                // Rule 3.
                (finalAction, finalReason) = ApplyDwellGate(candidateAction, candidateReason, previousAction.Value, timestamp);
            }
            else
            {
                // Rule 4.
                finalAction = candidateAction;
                finalReason = candidateReason;
            }

            // DecisionHistory.Update() MUST receive the final action, not
            // the (possibly suppressed) candidate - otherwise a suppressed
            // candidate would corrupt PreviousAction/TimeEnteredCurrentAction
            // and future dwell calculations.
            temporalContext.Update(finalAction, timestamp, disturbanceActive);
            LastDecisionReason = finalReason;

            return finalAction;
        }

        // Shared dwell check for rules 2 and 3: retains the previous
        // action with a structured, explainable reason until
        // MinimumActionDwellMs has elapsed since the previous action was
        // entered (DecisionHistory.TimeInCurrentAction), then lets the
        // candidate through with its genuine reason unmodified. The
        // original reason that justified entering the retained action is
        // deliberately not persisted anywhere - DecisionHistory keeps its
        // existing four-field scope.
        private (GhostAction, string) ApplyDwellGate(GhostAction candidateAction, string candidateReason, GhostAction previousAction, long timestamp)
        {
            long elapsed = temporalContext.TimeInCurrentAction(timestamp);

            if (elapsed < MinimumActionDwellMs)
            {
                string reason = string.Format(
                    "HYSTERESIS: retaining {0}; candidate {1} suppressed until minimum dwell elapsed (elapsed={2}ms, required={3}ms).",
                    previousAction, candidateAction, elapsed, MinimumActionDwellMs);

                return (previousAction, reason);
            }

            return (candidateAction, candidateReason);
        }

        // PROTECT/RETREAT are, in the current frozen R1-R8 rule set,
        // producible only by R1 and R2 respectively - a structural 1:1
        // fact about today's rules, not an invented ranking over all five
        // GhostAction values.
        private static bool IsSafetyAction(GhostAction action)
        {
            return action == GhostAction.PROTECT || action == GhostAction.RETREAT;
        }
    }
}
