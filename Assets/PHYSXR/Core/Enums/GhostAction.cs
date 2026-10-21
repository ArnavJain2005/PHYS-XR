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
        // R1-R8: only EncounterDecisionLayer rules E1/E3/E5 return it (E3 in
        // the committed final encounter, E5 once at the first sighting), and
        // no sensor value appears in their conditions, so a raw reading can
        // never trigger an attack.
        ATTACK
    }
}
