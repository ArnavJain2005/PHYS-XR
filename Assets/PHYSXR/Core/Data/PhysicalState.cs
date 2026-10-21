using PHYSXR.Core.Enums;

namespace PHYSXR.Core.Data
{
    public class PhysicalState
    {
        public PresenceLevel presence;
        public ConfidenceLevel confidence;
        public bool disturbance;
        public long timestamp;

        // Additive, telemetry only: the ID of the accepted physical event whose
        // disturbance pulse is active in this snapshot (0 = none). Lets a
        // decision record name the event that caused it (cause_evt). R1-R8
        // never read it.
        public long causeEventId;
    }
}
