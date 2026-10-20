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
    }
}
