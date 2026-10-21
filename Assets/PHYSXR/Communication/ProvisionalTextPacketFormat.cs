using System.Collections.Generic;

namespace PHYSXR.Communication
{
    // ------------------------------------------------------------------
    // PROVISIONAL WIRE FORMAT - NOT FINAL.
    //
    // No packet format exists anywhere else in this project. The ESP32
    // firmware contract has not been agreed upon yet, so this is a
    // deliberately minimal placeholder: a single UTF-8 text line of
    // "key=value" pairs separated by ';'. Example:
    //
    //   seq=42;ts=1690000000;presence=NEAR;confidence=HIGH;disturbance=false;eventType=;eventId=0
    //
    // Rationale for this specific shape:
    //   - trivial to produce from ESP32 firmware (sprintf, no JSON/binary
    //     library dependency required on the microcontroller)
    //   - trivial to hand-construct in tests without touching sockets
    //   - field names map 1:1 onto the existing semantic contracts, so
    //     nothing sensor-specific leaks into the wire format
    //
    // This decision is isolated entirely to this file. When the real ESP32
    // packet contract is finalized (binary struct, JSON, protobuf, etc.),
    // only ParseFields()/Encode() need to change - PacketValidator,
    // PhysicalStateParser, and everything downstream operate on
    // SemanticPacket and are unaffected.
    // ------------------------------------------------------------------
    public static class ProvisionalTextPacketFormat
    {
        public static Dictionary<string, string> ParseFields(string rawText)
        {
            if (string.IsNullOrEmpty(rawText))
                return null;

            var fields = new Dictionary<string, string>();
            var pairs = rawText.Split(';');

            foreach (var pair in pairs)
            {
                if (string.IsNullOrEmpty(pair))
                    continue;

                int separatorIndex = pair.IndexOf('=');
                if (separatorIndex <= 0)
                    return null;

                string key = pair.Substring(0, separatorIndex).Trim();
                string value = pair.Substring(separatorIndex + 1).Trim();

                if (key.Length == 0)
                    return null;

                fields[key] = value;
            }

            return fields.Count == 0 ? null : fields;
        }

        // Kept alongside the parser so tests (and any future simulated
        // ESP32 sender) can build valid provisional packets in one place
        // instead of hand-formatting strings throughout the codebase.
        public static string Encode(
            long sequenceNumber,
            long timestamp,
            string presence,
            string confidence,
            bool disturbance,
            string eventType,
            long eventId)
        {
            return string.Format(
                "seq={0};ts={1};presence={2};confidence={3};disturbance={4};eventType={5};eventId={6}",
                sequenceNumber, timestamp, presence, confidence, disturbance, eventType, eventId);
        }

        // Proposed contract v1 (Technical Spec section E, not yet verified
        // against firmware): the legacy keys plus v, dev, boot and tEvent.
        // Booleans are written lower-case, as firmware would print them.
        public static string EncodeV1(
            string deviceId,
            long boot,
            long sequenceNumber,
            long timestamp,
            string presence,
            string confidence,
            bool disturbance,
            string eventType,
            long eventId,
            long eventTimestamp)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "v=1;dev={0};boot={1};seq={2};ts={3};presence={4};confidence={5};disturbance={6};eventType={7};eventId={8};tEvent={9}",
                deviceId, boot, sequenceNumber, timestamp, presence, confidence, disturbance ? "true" : "false",
                eventType, eventId, eventTimestamp);
        }
    }
}
