namespace PHYSXR.Decision
{
    // How EncounterDecisionLayer makes the physical station's semantic state
    // visible in the ghost's behaviour where the frozen R1-R8 rules leave a
    // gap (the game sets one by default; -physxr-standard-rules removes it).
    //
    // With only the ToF valid (the ultrasonic is invalid on the current
    // station) confidence is always LOW, so R1 (PROTECT) and R3 (INVESTIGATE)
    // can never fire: R4 WARN for NEAR and R5 WARN for a shake with nothing in
    // range are all that remains, VERY_NEAR matches no rule at all, and a
    // shake while something is near changes nothing. The profile adds:
    //
    //   X3  accepted disturbance (the WorldState pulse), confidence LOW,
    //       presence NEAR or VERY_NEAR, inner result R4 WARN or R8 FOLLOW
    //                                          -> INVESTIGATE  (InvestigateDisturbance)
    //       then held for the rest of the pulse, whatever the ToF reads
    //       (shaking swings it), unless R1-R3 take over
    //   X1  VERY_NEAR, confidence LOW, player in the threat zone, inner R8
    //                                          -> PROTECT      (ProtectInThreatZone)
    //   X2  VERY_NEAR otherwise, inner R8      -> WARN
    //
    // X3 is the LOW-confidence form of R3 and is deliberately the LESS
    // aggressive outcome: INVESTIGATE sends the ghost searching and breaks the
    // WARN telegraph an attack needs, so a shake can delay an attack but never
    // cause one. X1/X2 react only once presence has lasted ConfirmMs and are
    // held for HoldMs so the ghost cannot flicker. X1's PROTECT goes through
    // the layer's E2 cap (6 s) and refractory (10 s) like any other PROTECT.
    //
    // Confidence is never raised and missing ultrasonic data is never
    // substituted. Freshness is unchanged: WorldStateManager hands the layer a
    // neutral NONE/LOW with no pulse whenever the source is not FRESH, is
    // masked (virtual-only) or the run is not Playing, so X1-X3 cannot act on
    // stale, invalid, paused or virtual-only input.
    public sealed class SensorResponseProfile
    {
        public const long DefaultConfirmMs = 250L;
        public const long DefaultHoldMs = 1500L;

        public SensorResponseProfile(long confirmMs = DefaultConfirmMs, long holdMs = DefaultHoldMs,
            bool protectInThreatZone = false, bool investigateDisturbance = true)
        {
            ConfirmMs = confirmMs < 0 ? 0 : confirmMs;
            HoldMs = holdMs < 0 ? 0 : holdMs;
            ProtectInThreatZone = protectInThreatZone;
            InvestigateDisturbance = investigateDisturbance;
        }

        // Presence (NEAR or VERY_NEAR, without a NONE in between) must have
        // lasted this long before X1/X2 react to VERY_NEAR (debounce on top
        // of the firmware's 30/35 cm hysteresis). NEAR -> VERY_NEAR is
        // therefore immediate once the NEAR itself is older than this.
        public long ConfirmMs { get; }

        // Minimum time an X1/X2 reaction is held after it last applied.
        public long HoldMs { get; }

        // X1 on (-physxr-exhibition). Off: VERY_NEAR in the zone is X2 WARN.
        public bool ProtectInThreatZone { get; }

        // X3 on.
        public bool InvestigateDisturbance { get; }

        public override string ToString()
        {
            return "sensor-response(confirm=" + ConfirmMs + "ms,hold=" + HoldMs + "ms,protectInZone=" + ProtectInThreatZone
                + ",investigateDisturbance=" + InvestigateDisturbance + ")";
        }
    }
}
