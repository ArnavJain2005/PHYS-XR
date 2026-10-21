namespace PHYSXR.Core.Enums
{
    public enum GhostAction
    {
        FOLLOW,
        WARN,
        INVESTIGATE,
        PROTECT,
        RETREAT,

        // Versioned extension (GDD 9.2, spec section D). Never produced by
        // R1-R8: only EncounterDecisionLayer rules E1/E3 return it, inside the
        // committed final encounter, and no sensor value appears in their
        // conditions, so a raw reading can never trigger an attack.
        ATTACK
    }
}
