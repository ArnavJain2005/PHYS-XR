using System;
using PHYSXR.Core.Enums;
using PHYSXR.Ghost;

namespace PHYSXR.Telemetry
{
    // Small convenience front for game code that writes typed records
    // (OBJECTIVE, FLOW, BEHAVIOUR, ATTACK, DEFENSE, ...) to whatever sink the
    // app supplied. A null sink makes every call a no-op, so gameplay code
    // never has to check, and an exception in the sink is swallowed: telemetry
    // can never change what the game does.
    public sealed class TelemetryChannel
    {
        public static readonly TelemetryChannel Null = new TelemetryChannel(null, () => 0L);

        private readonly ITelemetrySink sink;
        private readonly Func<long> clock;

        public TelemetryChannel(ITelemetrySink sink, Func<long> clock)
        {
            this.sink = sink;
            this.clock = clock ?? (() => 0L);
        }

        public ITelemetrySink Sink => sink;

        public bool Enabled => sink != null;

        public void Emit(string type, string detail = null, long? decisionId = null, long? relatedId = null,
            GhostAction? action = null, GhostExecutionState? executionState = null, string reason = null, string ruleId = null)
        {
            if (sink == null)
                return;

            try
            {
                sink.Record(new TelemetryRecord
                {
                    timestamp = clock(),
                    type = type,
                    detail = detail,
                    decisionId = decisionId,
                    relatedId = relatedId,
                    ghostAction = action,
                    ghostExecutionState = executionState,
                    reason = reason,
                    ruleId = ruleId
                });
            }
            catch
            {
                // Observational only.
            }
        }
    }
}
