namespace PHYSXR.Ghost
{
    // Implementation-neutral execution-level representation of a GhostAction.
    // Deliberately a separate type from PHYSXR.Core.Enums.GhostAction so a
    // future Unity/Animator/NavMesh integration can evolve independently of
    // the frozen decision vocabulary - this is not a decision, just what the
    // execution boundary was told to represent.
    public enum GhostExecutionState
    {
        Following,
        Warning,
        Investigating,
        Protecting,
        Retreating,

        // Versioned extension paired with GhostAction.ATTACK.
        Attacking
    }
}
