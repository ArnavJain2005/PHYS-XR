namespace PHYSXR.Core.Enums
{
    // Execution phase reported back by the ghost's motion controller.
    // Additive game-level extension; R1-R8 never read it.
    public enum GhostPhase
    {
        NORMAL,
        WINDUP,
        STRIKE,
        RECOVER,
        INTERRUPTED
    }
}
