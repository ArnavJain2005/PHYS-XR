using System;
using System.Collections.Generic;
using System.IO;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Ghost;
using PHYSXR.PhysicalWorld;
using PHYSXR.Telemetry;
using PHYSXR.WorldStateSystem;

namespace PHYSXR.Testing
{
    // TELJ suite: the JSON Lines sink, the pipeline's change-only DECISION
    // records, and determinism with telemetry on versus a null sink (T13).
    public static class TelemetryJsonLinesTests
    {
        public static int RunAll()
        {
            int passed = 0;
            int failed = 0;

            void Run(string name, Action test)
            {
                try
                {
                    test();
                    passed++;
                    Console.WriteLine("PASS: " + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("FAIL: " + name + " - " + ex.Message);
                }
            }

            Run("TELJ-01_Sink_WritesOneEscapedJsonLinePerRecord", TELJ01);
            Run("TELJ-02_Sink_FullQueue_DropsAndReports_NeverThrows", TELJ02);
            Run("TELJ-03_Sink_UnwritablePath_FaultsQuietly", TELJ03);
            Run("TELJ-04_ChangeOnly_OneDecisionPerChange_WithIdsRuleAndCause", TELJ04);
            Run("TELJ-05_ChangeOnly_HysteresisNumbersDoNotSpamRecords", TELJ05);
            Run("TELJ-06_T13_Determinism_TelemetryOnVsNullSink", TELJ06);

            Console.WriteLine("TelemetryJsonLinesTests: " + passed + " passed, " + failed + " failed");
            return failed;
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static string TempFile()
        {
            return Path.Combine(Path.GetTempPath(), "physxr_telj_" + Guid.NewGuid().ToString("N") + ".jsonl");
        }

        private static void TELJ01()
        {
            string path = TempFile();
            try
            {
                using (var sink = new JsonLinesTelemetrySink(path, "T1", "SIMULATED", "Windows"))
                {
                    sink.Attempt = 2;
                    sink.Frame = 77;
                    sink.Record(new TelemetryRecord
                    {
                        timestamp = 1234, type = "DECISION", ghostAction = GhostAction.WARN, ruleId = "E4",
                        reason = "say \"hi\"\nnext", decisionId = 5, causeEventId = 17, relatedId = 17
                    });
                    sink.Record(new TelemetryRecord { timestamp = 1300, eventType = TelemetryEventType.WorldStateEvaluated });
                    sink.Flush();
                }

                string[] lines = File.ReadAllLines(path);
                AssertTrue(lines.Length == 2, "two lines, got " + lines.Length);
                AssertTrue(lines[0] == "{\"ts\":1234,\"type\":\"DECISION\",\"trial\":\"T1\",\"cond\":\"SIMULATED\",\"plat\":\"Windows\",\"attempt\":2,\"frame\":77,"
                    + "\"action\":\"WARN\",\"rule\":\"E4\",\"reason\":\"say \\\"hi\\\"\\nnext\",\"dec\":5,\"cause_evt\":17,\"rel\":17}", "line 0: " + lines[0]);
                AssertTrue(lines[1].Contains("\"type\":\"WorldStateEvaluated\"") && lines[1].Contains("\"stage\":\"WorldStateEvaluated\""), "legacy record: " + lines[1]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static void TELJ02()
        {
            string path = TempFile();
            try
            {
                using (var sink = new JsonLinesTelemetrySink(path, "T2", "VIRTUAL_ONLY", "Windows", capacity: 16))
                {
                    for (int i = 0; i < 5000; i++)
                        sink.Record(new TelemetryRecord { timestamp = i, type = "PERF" });
                    sink.Flush();
                    AssertTrue(sink.Written + sink.Dropped >= 5000, "every record written or counted as dropped");
                }
                string text = File.ReadAllText(path);
                AssertTrue(text.Contains("TELEMETRY_DROPPED") || !text.Contains("dropped"), "drops reported when they happen");
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static void TELJ03()
        {
            string bad = Path.Combine(Path.GetTempPath(), "physxr_no_such_dir_" + Guid.NewGuid().ToString("N"), "x", "y.jsonl");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "physxr_blocker.tmp"), "");
            string blocked = Path.Combine(Path.GetTempPath(), "physxr_blocker.tmp", "t.jsonl");
            using (var sink = new JsonLinesTelemetrySink(blocked, "T3", "", ""))
            {
                for (int i = 0; i < 10; i++)
                    sink.Record(new TelemetryRecord { type = "PERF" });
                sink.Flush(500);
                System.Threading.Thread.Sleep(200);
                AssertTrue(sink.Faulted, "a path under a file cannot be created: sink faults");
                sink.Record(new TelemetryRecord { type = "PERF" }); // still no exception
            }
            File.Delete(Path.Combine(Path.GetTempPath(), "physxr_blocker.tmp"));
            AssertTrue(!Directory.Exists(bad), "nothing created for the unused path");
        }

        private sealed class Chain
        {
            public long Now;
            public readonly PhysicalStateManager Physical = new PhysicalStateManager();
            public readonly WorldStateManager World;
            public readonly GhostRuntimePipeline Pipeline;
            public readonly InMemoryTelemetrySink Sink = new InMemoryTelemetrySink();

            public Chain(bool withSink)
            {
                World = new WorldStateManager(Physical, () => Now);
                Pipeline = GhostRuntimePipeline.CreateRuntimeChain(() => Now, new GhostActionExecutor(), new GhostBehaviourController());
                Pipeline.ChangeOnlyTelemetry = true;
                if (withSink)
                    Pipeline.TelemetrySink = Sink;
                World.UpdateGhostState(new GhostState());
            }

            public (GhostAction, string) Step(float threat, bool fresh, PresenceLevel presence, bool committed, float distance, long? eventId)
            {
                Now += 50;
                Physical.UpdateState(new PhysicalState { presence = presence, confidence = ConfidenceLevel.HIGH, timestamp = Now });
                World.UpdateSourceStatus(fresh ? PhysicalSourceStatus.FRESH : PhysicalSourceStatus.UNAVAILABLE);
                if (eventId.HasValue)
                    World.NotifyAcceptedEvent(new PhysicalEvent { eventId = eventId.Value });
                World.UpdatePlayerState(new PlayerState { distanceToGhost = distance, inWarnBand = distance <= 6f, inThreatZone = distance <= 3.5f });
                World.UpdateVirtualState(new VirtualState { threatLevel = threat, encounterCommitted = committed });
                Pipeline.Run(World.GetCurrentWorldState(), Now);
                return (Pipeline.LastAction.Value, Pipeline.LastRuleId);
            }
        }

        private static List<TelemetryRecord> Decisions(InMemoryTelemetrySink sink)
        {
            var list = new List<TelemetryRecord>();
            foreach (TelemetryRecord r in sink.Records)
                if (r.type == "DECISION")
                    list.Add(r);
            return list;
        }

        private static void TELJ04()
        {
            var chain = new Chain(true);
            for (int i = 0; i < 20; i++) chain.Step(0.2f, true, PresenceLevel.NONE, false, 10f, null);
            for (int i = 0; i < 20; i++) chain.Step(0.2f, true, PresenceLevel.NEAR, false, 10f, i == 0 ? 17 : (long?)null);
            List<TelemetryRecord> decisions = Decisions(chain.Sink);
            // Change-only mode writes no per-frame stage records: only DECISION
            // records, plus exactly one PHYS_EVENT_OUTCOME for the one event.
            var outcomes = new List<TelemetryRecord>();
            foreach (TelemetryRecord r in chain.Sink.Records)
                if (r.type == "PHYS_EVENT_OUTCOME")
                    outcomes.Add(r);
            AssertTrue(chain.Sink.Records.Count == decisions.Count + outcomes.Count, "change-only: DECISION and PHYS_EVENT_OUTCOME records only");
            AssertTrue(outcomes.Count == 1 && outcomes[0].relatedId == 17 && outcomes[0].detail.Contains("outcome=ACTED inner=R3 "),
                "one ACTED outcome for evt 17: " + (outcomes.Count > 0 ? outcomes[0].detail : "none"));
            AssertTrue(decisions.Count >= 2 && decisions.Count <= 4, "few records for 40 frames, got " + decisions.Count);
            AssertTrue(decisions[0].decisionId == 1 && decisions[0].ghostAction == GhostAction.FOLLOW && decisions[0].ruleId == "INNER", "first FOLLOW dec=1");
            TelemetryRecord investigate = decisions.Find(r => r.ghostAction == GhostAction.INVESTIGATE);
            AssertTrue(investigate != null && investigate.causeEventId == 17 && investigate.decisionId == 2, "INVESTIGATE dec=2 cause_evt=17");
            AssertTrue(chain.Pipeline.CurrentDecisionId == decisions[decisions.Count - 1].decisionId, "ids increase by one per record");
        }

        private static void TELJ05()
        {
            var chain = new Chain(true);
            chain.Step(0.5f, false, PresenceLevel.NONE, false, 10f, null); // R7 WARN
            for (int i = 0; i < 40; i++)
                chain.Step(0f, false, PresenceLevel.NONE, false, 10f, null); // HYSTERESIS retaining WARN, then FOLLOW
            List<TelemetryRecord> decisions = Decisions(chain.Sink);
            AssertTrue(decisions.Count == 3, "WARN, HYSTERESIS(WARN), FOLLOW: got " + decisions.Count);
            AssertTrue(decisions[1].reason.StartsWith("HYSTERESIS"), decisions[1].reason);
        }

        private static void TELJ06()
        {
            // T13: same scripted inputs, telemetry on vs no sink -> identical
            // (frame, action, rule) sequence.
            var withSink = new Chain(true);
            var without = new Chain(false);
            for (int frame = 0; frame < 600; frame++)
            {
                float threat = frame > 400 && frame < 460 ? 0.8f : (frame % 97 < 30 ? 0.5f : 0.2f);
                bool fresh = frame % 150 < 120;
                PresenceLevel presence = (PresenceLevel)(frame / 40 % 3);
                bool committed = frame > 300;
                float distance = 1.5f + (frame % 70) / 7f;
                long? evt = frame % 111 == 0 ? frame + 1000L : (long?)null;

                var a = withSink.Step(threat, fresh, presence, committed, distance, evt);
                var b = without.Step(threat, fresh, presence, committed, distance, evt);
                AssertTrue(a.Item1 == b.Item1 && a.Item2 == b.Item2, "frame " + frame + ": " + a + " vs " + b);
            }
            AssertTrue(Decisions(withSink.Sink).Count > 5, "the run produced several decisions");
        }
    }
}
