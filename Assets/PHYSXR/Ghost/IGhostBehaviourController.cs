namespace PHYSXR.Ghost
{
    // Minimal Unity-facing behaviour boundary, mirroring the
    // IDecisionProvider/IGhostActionExecutor plug-in shape. Consumes a
    // GhostExecutionState already produced elsewhere - it does not decide
    // or execute concrete behaviour, only holds/applies the current one.
    //
    // Halt/Reset (additive, terminal-state support): once Halt() has been
    // called the controller refuses every ApplyState() until Reset(), so no
    // movement/attack state can be applied while the run is terminal even if
    // a caller bypasses the pipeline. CurrentState is left as it was (the
    // ghost stays where it is); presentation code must treat IsHalted as
    // "do not drive ordinary ghost behaviour".
    public interface IGhostBehaviourController
    {
        void ApplyState(GhostExecutionState state);

        GhostExecutionState CurrentState { get; }

        bool IsHalted { get; }

        void Halt();

        void Reset();
    }
}
