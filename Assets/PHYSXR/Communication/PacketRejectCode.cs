namespace PHYSXR.Communication
{
    // Machine-readable reason a datagram was not accepted (spec section E,
    // "Validation order"). Telemetry logs one of these for every rejected
    // packet. ValidationResult.failureReason keeps the human-readable text.
    public enum PacketRejectCode
    {
        None = 0,
        BAD_SIZE,
        MALFORMED,
        VERSION_UNSUPPORTED,
        FOREIGN_DEVICE,
        INVALID_FIELD,
        OUT_OF_ORDER,
        OLD_BOOT,
        STALE_PACKET
    }

    // What happened to the event a packet carried, if any (spec section E,
    // step 9). Only ACCEPTED events start a disturbance pulse.
    public enum EventAcceptance
    {
        NoEvent = 0,
        ACCEPTED,
        DUPLICATE,
        OLD_EVENT,
        EVENT_TOO_OLD,
        RESYNCING,
        BLACKOUT,
        MASKED
    }
}
