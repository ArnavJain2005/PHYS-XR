namespace PHYSXR.Core.Enums
{
    // Freshness of the physical (sensor) source, judged from Unity-side
    // arrival time (spec section E). Only FRESH lets physical state reach the
    // decision rules; STALE and UNAVAILABLE hand them a neutral state (P2).
    //
    //   FRESH        last accepted state line <= 500 ms ago
    //   STALE        500 ms .. 3 s ago
    //   UNAVAILABLE  > 3 s ago, or never connected
    public enum PhysicalSourceStatus
    {
        UNAVAILABLE = 0,
        STALE,
        FRESH
    }
}
