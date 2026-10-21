namespace PHYSXR.Core.Data
{
    public class PlayerState
    {
        public float distanceToGhost;
        public bool movement;
        public bool inThreatZone;

        // Additive encounter field (spec section D, "Warn band"): true once the
        // player is within 6.0 m of the ghost, until they are beyond 7.5 m.
        // Read only by EncounterDecisionLayer rule E4, never by R1-R8.
        public bool inWarnBand;
    }
}
