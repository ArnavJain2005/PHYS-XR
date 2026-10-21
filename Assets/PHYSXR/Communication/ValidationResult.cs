namespace PHYSXR.Communication
{
    // Outcome of PacketValidator.Validate(). Deliberately not an exception:
    // rejection is an expected, routine outcome on this boundary (malformed
    // data, stale sequence numbers, dropped/corrupted UDP payloads), not an
    // exceptional one.
    public class ValidationResult
    {
        public bool isValid;
        public SemanticPacket packet;
        public string failureReason;

        // Additive: machine-readable reject code for telemetry.
        public PacketRejectCode code;

        // Additive: true when this accepted packet is the first from a new
        // boot after packets from another boot (an ESP32 reset, fix P3).
        public bool isNewBoot;

        // Additive: sequence numbers skipped since the last accepted packet of
        // the same boot (packet loss), 0 if none.
        public long sequenceGap;

        public static ValidationResult Reject(string reason)
        {
            return Reject(PacketRejectCode.INVALID_FIELD, reason);
        }

        public static ValidationResult Reject(PacketRejectCode code, string reason)
        {
            return new ValidationResult
            {
                isValid = false,
                packet = null,
                failureReason = reason,
                code = code
            };
        }

        public static ValidationResult Accept(SemanticPacket packet)
        {
            return new ValidationResult
            {
                isValid = true,
                packet = packet,
                failureReason = null,
                code = PacketRejectCode.None
            };
        }
    }
}
