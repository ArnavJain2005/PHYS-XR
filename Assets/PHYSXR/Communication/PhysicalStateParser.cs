using System;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;

namespace PHYSXR.Communication
{
    // Converts an already-validated SemanticPacket into the existing
    // PHYSXR.Core.Data contracts. Does not re-validate (PacketValidator has
    // already confirmed the enum names and required fields) and does not
    // create any competing state model - it only ever produces the existing
    // PhysicalState / PhysicalEvent types.
    public class PhysicalStateParser
    {
        public bool TryParseState(SemanticPacket packet, out PhysicalState state)
        {
            state = null;

            if (packet == null)
                return false;

            if (!Enum.TryParse(packet.presence, out PresenceLevel presence))
                return false;

            if (!Enum.TryParse(packet.confidence, out ConfidenceLevel confidence))
                return false;

            state = new PhysicalState
            {
                presence = presence,
                confidence = confidence,
                disturbance = packet.disturbance,
                timestamp = packet.timestamp
            };

            return true;
        }

        public bool TryParseEvent(SemanticPacket packet, out PhysicalEvent physicalEvent)
        {
            physicalEvent = null;

            if (packet == null || !packet.hasEvent)
                return false;

            physicalEvent = new PhysicalEvent
            {
                eventType = packet.eventType,
                eventId = packet.eventId,
                timestamp = packet.timestamp,
                boot = packet.boot
            };

            return true;
        }
    }
}
