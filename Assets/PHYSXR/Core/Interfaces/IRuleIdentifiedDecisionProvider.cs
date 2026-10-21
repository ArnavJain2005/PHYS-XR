namespace PHYSXR.Core.Interfaces
{
    // Optional companion to IExplainableDecisionProvider: a short, stable ID
    // for the rule that produced the most recent decision (for example "E3",
    // "W" or "INNER"). Telemetry uses it to group decisions without parsing
    // reason strings. Providers that have no rule IDs simply do not implement it.
    public interface IRuleIdentifiedDecisionProvider
    {
        string LastRuleId { get; }
    }
}
