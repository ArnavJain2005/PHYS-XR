using System;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Core.Interfaces;

namespace PHYSXR.Decision
{
    // Game-level arbitration in front of the frozen R1-R8 provider.
    //
    // R1-R8 (RuleBasedDecisionProvider) are NOT modified. The inner provider
    // is evaluated on every call so its DecisionHistory/hysteresis stays
    // coherent; this layer only decides whether its result is replaced.
    //
    // Precedence, first match wins:
    //   G0  gameplayTerminal           -> inner result, unchanged (no beat may override).
    //       This is decision-level defence in depth only. The terminal state is
    //       actually ENFORCED one level down: GhostRuntimePipeline's terminal
    //       gate stops the decide/execute/apply sequence from running at all.
    //   E0  ghost phase INTERRUPTED    -> RETREAT (lantern interruption)
    //   S   inner result PROTECT/RETREAT -> inner result (genuine safety response)
    //   W   mandatoryWarningBeat       -> WARN
    //   --  otherwise                  -> inner result
    //
    // ATTACK-related rules (E1 attack continues, E3 attack starts, E4 close
    // pressure) and the PROTECT cap (E2) slot in after E0 / after W in a later
    // milestone; this class implements only what the warning-beat decision needs.
    //
    // Deterministic: no clock, no randomness, no state of its own beyond the
    // last reason string.
    public class EncounterDecisionLayer : IExplainableDecisionProvider
    {
        private readonly RuleBasedDecisionProvider inner;

        public string LastDecisionReason { get; private set; } = string.Empty;
        public string LastRuleId { get; private set; } = string.Empty;

        public EncounterDecisionLayer(RuleBasedDecisionProvider inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public RuleBasedDecisionProvider Inner => inner;

        // Checkpoint restart: clears the inner provider's temporal context
        // (hysteresis history) and this layer's last reason/rule. Calibration
        // and the R1-R8 rules are untouched.
        public void Reset()
        {
            inner.Reset();
            LastDecisionReason = string.Empty;
            LastRuleId = string.Empty;
        }

        public GhostAction Decide(WorldState state)
        {
            GhostAction innerAction = inner.Decide(state);
            string innerReason = inner.LastDecisionReason;

            VirtualState virtualWorld = state?.virtualWorld;
            GhostState ghost = state?.ghost;

            if (virtualWorld != null && virtualWorld.gameplayTerminal)
                return Result(innerAction, "G0", "TERMINAL: gameplay terminal state, no game-level override | " + innerReason);

            if (ghost != null && ghost.phase == GhostPhase.INTERRUPTED)
                return Result(GhostAction.RETREAT, "E0", "RETREAT[E0]: ghost interrupted by defensive pulse");

            if (innerAction == GhostAction.PROTECT || innerAction == GhostAction.RETREAT)
                return Result(innerAction, "S", innerReason);

            if (virtualWorld != null && virtualWorld.mandatoryWarningBeat)
                return Result(GhostAction.WARN, "W", "WARN[W]: mandatory scripted warning beat (inner candidate " + innerAction + ")");

            return Result(innerAction, "INNER", innerReason);
        }

        private GhostAction Result(GhostAction action, string ruleId, string reason)
        {
            LastRuleId = ruleId;
            LastDecisionReason = reason;
            return action;
        }
    }
}
