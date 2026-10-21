using PHYSXR.Core.Enums;

namespace PHYSXR.Core.Data
{
    public class GhostState
    {
        public GhostAction currentState;

        // Additive game-level extension: INTERRUPTED while the lantern pulse
        // is stunning the ghost (and during the retreat that follows).
        public GhostPhase phase;

        // Additive encounter fields, reported by the ghost's motion controller
        // (spec section D). R1-R8 never read them.
        //
        // phaseElapsedMs:   time spent in the current phase.
        // attackCooldownMs: remaining attack cooldown; 0 means an attack may start.
        public long phaseElapsedMs;
        public long attackCooldownMs;
    }
}
