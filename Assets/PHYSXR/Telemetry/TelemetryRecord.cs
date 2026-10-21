using PHYSXR.Core.Enums;
using PHYSXR.Ghost;

namespace PHYSXR.Telemetry
{
    // A single observational telemetry entry. Plain data contract - same
    // convention as PHYSXR.Core.Data (public fields, no behaviour) - so it
    // stays a passive record, never a place decision logic could hide.
    //
    // Deliberately does NOT carry a full WorldState/PhysicalState snapshot:
    // only the small set of already-computed values (GhostAction,
    // GhostExecutionState, decision reason, an optional identifier and a
    // short free-text detail) needed to correlate a pipeline stage without
    // duplicating the source contracts.
    public class TelemetryRecord
    {
        public long timestamp;
        public TelemetryEventType eventType;

        // Relevant only for decision/action/state-change events; null otherwise.
        public GhostAction? ghostAction;
        public GhostExecutionState? ghostExecutionState;

        // Decision explainability (e.g. RuleBasedDecisionProvider.LastDecisionReason).
        public string reason;

        // An identifier already available upstream where relevant
        // (e.g. a PhysicalEvent.eventId) - not a new identity scheme.
        public long? relatedId;

        // Short free-text context (e.g. "presence=NEAR"), not a WorldState dump.
        public string detail;

        // Additive game-telemetry fields (spec section G). All optional; the
        // original pipeline-stage records leave them null.
        //
        // type:         the record type written to the JSON Lines file
        //               (DECISION, BEHAVIOUR, PKT_RX, OBJECTIVE, ...).
        // decisionId:   "dec", increasing per emitted decision; carried into
        //               the BEHAVIOUR/ATTACK/DEFENSE records it caused ("beh").
        // ruleId:       the rule that produced the decision (E3, W, INNER, ...).
        // causeEventId: "cause_evt", the accepted physical event behind it.
        public string type;
        public long? decisionId;
        public string ruleId;
        public long? causeEventId;
    }
}
