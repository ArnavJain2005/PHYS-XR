using System;
using System.Collections.Generic;
using System.Text;
using PHYSXR.Core.Enums;

namespace PHYSXR.Communication
{
    // Validates incoming semantic packets before anything downstream trusts
    // them. Contains no Ghost decision logic and never selects a
    // GhostAction - it only ever accepts or rejects a packet.
    //
    // Sequence tracking lives here (not in PhysicalStateManager) so that
    // "old sequence number can never overwrite newer state" is enforced at
    // the communication boundary, independent of the decision system.
    //
    // BOOT IDENTITY (fix P3): sequence numbers restart at 1 when the ESP32
    // reboots. Ordering is therefore tracked per boot: a packet with a boot
    // ID not seen before starts fresh sequence tracking (and is flagged
    // isNewBoot), while a packet from an earlier boot is rejected OLD_BOOT.
    // Packets in the legacy format carry no boot field and all count as boot
    // 0, so the original single-sequence behaviour is unchanged for them.
    //
    // Contract v1 fields (spec section E) are optional on the wire: when "v"
    // is present it must be a supported version, and when "dev" is present
    // and a device ID is configured, it must match. The USB-serial link sets
    // RequireVersion and RequireDeviceId, making both mandatory there.
    public class PacketValidator
    {
        public const int MaxDatagramBytes = 512;
        public const int SupportedVersion = 1;

        private readonly string expectedDeviceId;
        private readonly HashSet<long> earlierBoots = new HashSet<long>();

        private long lastAcceptedSequenceNumber = long.MinValue;
        private bool hasAcceptedAny;
        private long currentBoot;

        public PacketValidator()
            : this(null)
        {
        }

        // expectedDeviceId: when set, a packet whose "dev" differs is rejected
        // FOREIGN_DEVICE, so other stations on the network are ignored.
        public PacketValidator(string expectedDeviceId)
        {
            this.expectedDeviceId = string.IsNullOrEmpty(expectedDeviceId) ? null : expectedDeviceId;
        }

        public long CurrentBoot => currentBoot;

        // When true, a line without "v" is rejected VERSION_UNSUPPORTED. The
        // USB-serial link sets it: the firmware always sends v=1, so a
        // versionless line there means old or foreign firmware. Off by default
        // so legacy-format packets and their tests behave as before.
        public bool RequireVersion { get; set; }

        // When true, a line without "dev" is rejected FOREIGN_DEVICE. Set with
        // RequireVersion on the USB-serial link: the firmware always sends its
        // device ID, so a line without one is not from the station. Off by
        // default for the same legacy reason.
        public bool RequireDeviceId { get; set; }

        public ValidationResult Validate(byte[] rawData)
        {
            if (rawData == null || rawData.Length == 0)
                return ValidationResult.Reject(PacketRejectCode.BAD_SIZE, "empty packet");

            if (rawData.Length > MaxDatagramBytes)
                return ValidationResult.Reject(PacketRejectCode.BAD_SIZE, "packet larger than " + MaxDatagramBytes + " bytes");

            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(rawData);
            }
            catch (Exception)
            {
                return ValidationResult.Reject(PacketRejectCode.MALFORMED, "undecodable packet bytes");
            }

            return Validate(text);
        }

        public ValidationResult Validate(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return ValidationResult.Reject(PacketRejectCode.BAD_SIZE, "empty packet");

            var fields = ProvisionalTextPacketFormat.ParseFields(rawText);
            if (fields == null)
                return ValidationResult.Reject(PacketRejectCode.MALFORMED, "malformed packet");

            int version = 0;
            if (fields.TryGetValue("v", out var versionText))
            {
                if (!int.TryParse(versionText, out version) || version != SupportedVersion)
                    return ValidationResult.Reject(PacketRejectCode.VERSION_UNSUPPORTED, "unsupported packet version '" + versionText + "'");
            }
            else if (RequireVersion)
            {
                return ValidationResult.Reject(PacketRejectCode.VERSION_UNSUPPORTED, "missing protocol version (v)");
            }

            fields.TryGetValue("dev", out var deviceId);
            if (RequireDeviceId && string.IsNullOrEmpty(deviceId))
                return ValidationResult.Reject(PacketRejectCode.FOREIGN_DEVICE, "missing device id (dev)");
            if (expectedDeviceId != null && !string.IsNullOrEmpty(deviceId) && deviceId != expectedDeviceId)
                return ValidationResult.Reject(PacketRejectCode.FOREIGN_DEVICE, "packet from foreign device '" + deviceId + "'");

            long boot = 0;
            if (fields.TryGetValue("boot", out var bootText) && (!long.TryParse(bootText, out boot) || boot < 0))
                return ValidationResult.Reject("invalid boot identity");

            if (!fields.TryGetValue("seq", out var seqText) || !long.TryParse(seqText, out long sequenceNumber))
                return ValidationResult.Reject("missing or invalid sequence number");

            if (!fields.TryGetValue("ts", out var tsText) || !long.TryParse(tsText, out long timestamp) || timestamp <= 0)
                return ValidationResult.Reject("missing or invalid timestamp");

            if (!fields.TryGetValue("presence", out var presenceText) || !IsDefinedEnumName<PresenceLevel>(presenceText))
                return ValidationResult.Reject("invalid presence value");

            if (!fields.TryGetValue("confidence", out var confidenceText) || !IsDefinedEnumName<ConfidenceLevel>(confidenceText))
                return ValidationResult.Reject("invalid confidence value");

            if (!fields.TryGetValue("disturbance", out var disturbanceText) || !bool.TryParse(disturbanceText, out bool disturbance))
                return ValidationResult.Reject("missing or invalid disturbance flag");

            long eventTimestamp = 0;
            if (fields.TryGetValue("tEvent", out var tEventText) && tEventText.Length > 0
                && (!long.TryParse(tEventText, out eventTimestamp) || eventTimestamp < 0))
                return ValidationResult.Reject("invalid event timestamp");

            bool isNewBoot = false;
            if (hasAcceptedAny && boot != currentBoot)
            {
                if (earlierBoots.Contains(boot))
                    return ValidationResult.Reject(PacketRejectCode.OLD_BOOT, "packet from an earlier boot");
                isNewBoot = true;
            }

            if (hasAcceptedAny && !isNewBoot && sequenceNumber <= lastAcceptedSequenceNumber)
                return ValidationResult.Reject(PacketRejectCode.OUT_OF_ORDER, "stale or duplicate sequence number");

            fields.TryGetValue("eventType", out var eventType);
            long eventId = 0;
            if (fields.TryGetValue("eventId", out var eventIdText) && (!long.TryParse(eventIdText, out eventId) || eventId < 0))
                return ValidationResult.Reject("invalid event id");
            bool hasEvent = eventId != 0;

            var packet = new SemanticPacket
            {
                sequenceNumber = sequenceNumber,
                timestamp = timestamp,
                presence = presenceText,
                confidence = confidenceText,
                disturbance = disturbance,
                eventType = eventType ?? string.Empty,
                eventId = eventId,
                hasEvent = hasEvent,
                version = version,
                deviceId = deviceId ?? string.Empty,
                boot = boot,
                eventTimestamp = eventTimestamp
            };

            long gap = hasAcceptedAny && !isNewBoot ? Math.Max(0L, sequenceNumber - lastAcceptedSequenceNumber - 1) : 0L;

            if (isNewBoot)
                earlierBoots.Add(currentBoot);

            currentBoot = boot;
            lastAcceptedSequenceNumber = sequenceNumber;
            hasAcceptedAny = true;

            ValidationResult result = ValidationResult.Accept(packet);
            result.isNewBoot = isNewBoot;
            result.sequenceGap = gap;
            return result;
        }

        public long LastAcceptedSequenceNumber => lastAcceptedSequenceNumber;

        // Exact, case-sensitive member names only. Enum.TryParse alone would
        // also accept numeric text ("presence=7"), including undefined values.
        private static bool IsDefinedEnumName<TEnum>(string value) where TEnum : struct, Enum
        {
            return !string.IsNullOrEmpty(value) && Enum.IsDefined(typeof(TEnum), value);
        }
    }
}
