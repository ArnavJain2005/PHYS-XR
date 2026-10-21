using System;
using PHYSXR.Core.Enums;

namespace PHYSXR.Ghost
{
    // Deterministic GhostAction -> GhostExecutionState translation. This
    // class is completely stateless: it holds no fields, stores no "current
    // action", and Execute() is a pure function of its argument. It contains
    // no decision logic - the action has already been chosen by
    // RuleBasedDecisionProvider before it ever reaches this class.
    //
    // No UnityEngine/MonoBehaviour/Animator/NavMesh/XR/networking/telemetry/
    // timer/randomness/sensor dependency is used here by design.
    public class GhostActionExecutor : IGhostActionExecutor
    {
        public GhostExecutionState Execute(GhostAction action)
        {
            switch (action)
            {
                case GhostAction.FOLLOW:
                    return GhostExecutionState.Following;
                case GhostAction.WARN:
                    return GhostExecutionState.Warning;
                case GhostAction.INVESTIGATE:
                    return GhostExecutionState.Investigating;
                case GhostAction.PROTECT:
                    return GhostExecutionState.Protecting;
                case GhostAction.RETREAT:
                    return GhostExecutionState.Retreating;
                case GhostAction.ATTACK:
                    return GhostExecutionState.Attacking;
                default:
                    // No silent fallthrough: an unrecognized GhostAction
                    // value (e.g. an invalid cast) must fail loudly rather
                    // than resolve to an unintended default execution state.
                    throw new ArgumentOutOfRangeException(nameof(action), action, "Unhandled GhostAction value");
            }
        }
    }
}
