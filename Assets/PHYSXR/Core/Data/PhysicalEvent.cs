namespace PHYSXR.Core.Data
{
    public class PhysicalEvent
    {
        public string eventType;
        public long eventId;
        public long timestamp;

        // Additive (P4): the sensor station's boot identity. Event IDs restart
        // after an ESP32 reset, so dedup is keyed on (boot, eventId). 0 for
        // packets in the legacy format, which carry no boot field.
        public long boot;
    }
}
