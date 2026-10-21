using System;

namespace PHYSXR.Ghost
{
    // Plain C# state holder / application boundary for GhostExecutionState.
    // This is intentionally only that - a validated place to store "what
    // behaviour state is the Ghost currently in" - and nothing more. It
    // does not know about WorldState, GhostAction, IDecisionProvider,
    // RuleBasedDecisionProvider or IGhostActionExecutor, contains no
    // decision logic, and has no UnityEngine/XR/Animator/NavMesh/audio/
    // VFX/movement/networking/sensor dependency. Concrete Unity behaviour
    // is deliberately not implemented here yet.
    //
    // Halt()/Reset() exist for terminal gameplay states (player dead, ending
    // playing, run complete): while halted, ApplyState() validates its
    // argument exactly as before but does NOT change CurrentState. Reset()
    // is the checkpoint-restart path back to the initial Following state.
    public class GhostBehaviourController : IGhostBehaviourController
    {
        public GhostExecutionState CurrentState { get; private set; } = GhostExecutionState.Following;

        public bool IsHalted { get; private set; }

        public void ApplyState(GhostExecutionState state)
        {
            if (!Enum.IsDefined(typeof(GhostExecutionState), state))
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unhandled GhostExecutionState value");

            if (IsHalted)
                return;

            CurrentState = state;
        }

        public void Halt()
        {
            IsHalted = true;
        }

        public void Reset()
        {
            IsHalted = false;
            CurrentState = GhostExecutionState.Following;
        }
    }
}
