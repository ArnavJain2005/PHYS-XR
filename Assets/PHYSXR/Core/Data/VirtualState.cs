namespace PHYSXR.Core.Data
{
    public class VirtualState
    {
        public float threatLevel;
        public bool eventActive;

        // Additive game-level fields (versioned extension, not read by R1-R8).
        //
        // mandatoryWarningBeat: true only while a designer-authored warning
        // beat is playing (e.g. the pre-surge flicker warning). Distinct from
        // eventActive, which covers any virtual event and keeps its existing
        // R6 meaning. Set and cleared by the game flow, never by sensing.
        public bool mandatoryWarningBeat;

        // gameplayTerminal: true when the run is in a terminal state (player
        // dead, ending playing, run complete). No scripted beat may override
        // the ghost's behaviour in this state.
        public bool gameplayTerminal;

        // encounterCommitted: true once power is restored and the player has
        // crossed the encounter trigger (spec section D). Only then may an
        // attack (E3) or close pressure (E4) happen.
        public bool encounterCommitted;

        // attackGraceActive: true for a short time after a checkpoint restart,
        // during which no attack may start (E3).
        public bool attackGraceActive;
    }
}
