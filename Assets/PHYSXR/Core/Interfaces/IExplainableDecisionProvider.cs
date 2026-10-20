using PHYSXR.Core.Enums;

namespace PHYSXR.Core.Interfaces
{
    // Decision selection plus decision-reason reporting, as one small
    // abstraction the runtime pipeline can depend on. It exists so the
    // pipeline can run EncounterDecisionLayer (which wraps the frozen
    // RuleBasedDecisionProvider) and still report the FINAL decision's reason
    // to telemetry, without RuleBasedDecisionProvider having to be edited.
    //
    // LastDecisionReason: why the most recent Decide() returned what it did.
    //   For a layered provider this is the OUTERMOST layer's reason.
    // Reset(): clears transient decision state (hysteresis history, last
    //   reason) for a checkpoint restart. Never changes calibration.
    public interface IExplainableDecisionProvider : IDecisionProvider
    {
        string LastDecisionReason { get; }

        void Reset();
    }
}
