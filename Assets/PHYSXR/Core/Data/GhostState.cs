using PHYSXR.Core.Enums;

namespace PHYSXR.Core.Data
{
    public class GhostState
    {
        public GhostAction currentState;

        // Additive game-level extension: INTERRUPTED while the lantern pulse
        // is stunning the ghost (and during the retreat that follows).
        public GhostPhase phase;
    }
}
