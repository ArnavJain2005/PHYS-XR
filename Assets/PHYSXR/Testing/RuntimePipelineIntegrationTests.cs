using System;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Decision;
using PHYSXR.Ghost;
using PHYSXR.Telemetry;

namespace PHYSXR.Testing
{
    // Runtime-pipeline integration tests (RT-01..RT-12). Unlike
    // EncounterDecisionLayerTests, these do not call the decision layer
    // directly: every case drives a real WorldStateManager snapshot through
    // GhostRuntimePipeline (built via CreateRuntimeChain, as GhostRuntime
    // builds it) -> GhostActionExecutor -> GhostBehaviourController, and asserts
    // on the returned GhostExecutionState AND on the telemetry the pipeline
    // emitted. Software-only: no Unity, ESP32, UDP or Quest involved.
    public static class RuntimePipelineIntegrationTests
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

            Run("RT-01_Beat_PresenceNone_PipelineReturnsWarning", RT01);
            Run("RT-02_Beat_NearHighNoDisturbance_PipelineReturnsWarning", RT02);
            Run("RT-03_NoBeat_SamePhysicalConditions_KeepsExistingFollow", RT03);
            Run("RT-04_Telemetry_RecordsFinalWarnAndRuleWReason", RT04);
            Run("RT-05_GenuineProtect_BeatsScriptedWarning", RT05);
            Run("RT-06_GenuineRetreat_BeatsScriptedWarning", RT06);
            Run("RT-07_InterruptedPhase_ProducesRetreatViaE0", RT07);
            Run("RT-08_ActionReasonExecutionStateTelemetry_AreConsistent", RT08);
            Run("RT-09_IndependentInstances_SameInput_SameDecision", RT09);
            Run("RT-10_NoFlags_ChainEqualsStandaloneR1toR8_OverInputGrid", RT10);
            Run("RT-11_BeatEnd_ReturnsToInnerDecision_InnerHistoryStaysCoherent", RT11);
            Run("RT-12_EventActiveAlone_IsNotAWarningBeat", RT12);

            Console.WriteLine("RuntimePipelineIntegrationTests: " + passed + " passed, " + failed + " failed");
            return failed;
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void AssertEq<T>(T expected, T actual, string context)
        {
            if (!Equals(expected, actual))
                throw new Exception(context + ": expected " + expected + " but got " + actual);
        }

        // Every tick must leave action, reason, execution state, controller
        // state and telemetry telling the same story.
        private static void AssertConsistent(RuntimeTestHarness h, GhostExecutionState returned)
        {
            TelemetryRecord decision = h.Last(TelemetryEventType.DecisionMade);
            TelemetryRecord executed = h.Last(TelemetryEventType.GhostActionExecuted);
            TelemetryRecord changed = h.Last(TelemetryEventType.GhostExecutionStateChanged);

            AssertTrue(decision != null && executed != null && changed != null, "expected Decision/Executed/StateChanged records");
            AssertTrue(decision.ghostAction.HasValue, "DecisionMade must carry the final action");
            AssertEq(decision.ghostAction, executed.ghostAction, "DecisionMade.action vs GhostActionExecuted.action");
            AssertEq(h.Executor.LastAction, decision.ghostAction, "action handed to the executor vs DecisionMade.action");
            AssertEq(new GhostActionExecutor().Execute(decision.ghostAction.Value), returned, "returned state vs executor mapping of the final action");
            AssertEq(returned, executed.ghostExecutionState, "returned state vs GhostActionExecuted.state");
            AssertEq(returned, changed.ghostExecutionState, "returned state vs GhostExecutionStateChanged.state");
            AssertEq(returned, h.Controller.CurrentState, "returned state vs controller state");
            AssertTrue(!string.IsNullOrEmpty(decision.reason), "DecisionMade must carry a non-empty reason");
        }

        // NEAR + HIGH + no disturbance, outside the threat zone: R8 FOLLOW.
        private static RuntimeTestHarness NearHigh(bool beat)
        {
            return new RuntimeTestHarness()
                .SetPhysical(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false)
                .Player(inThreatZone: false)
                .Virtual(threat: 0.2f, beat: beat);
        }

        private static void RT01()
        {
            var h = new RuntimeTestHarness()
                .SetPhysical(PresenceLevel.NONE, ConfidenceLevel.HIGH, false)
                .Player(inThreatZone: false)
                .Virtual(threat: 0.2f, beat: true);

            GhostExecutionState state = h.Tick();

            AssertEq(GhostExecutionState.Warning, state, "pipeline result for beat + presence NONE");
            AssertEq(GhostExecutionState.Warning, h.Controller.CurrentState, "controller state");
            AssertConsistent(h, state);
        }

        private static void RT02()
        {
            var h = NearHigh(beat: true);

            GhostExecutionState state = h.Tick();

            AssertEq(GhostExecutionState.Warning, state, "pipeline result for beat + NEAR/HIGH/no disturbance");
            AssertConsistent(h, state);
        }

        private static void RT03()
        {
            var h = NearHigh(beat: false);
            GhostExecutionState state = h.Tick();

            // Independent, plain R1-R8 provider on the identical WorldState.
            var reference = new RuleBasedDecisionProvider();
            GhostAction expected = reference.Decide(NearHigh(beat: false).World.GetCurrentWorldState());

            AssertEq(GhostAction.FOLLOW, expected, "R1-R8 on its own for this input");
            AssertEq(GhostExecutionState.Following, state, "pipeline result without a beat");
            AssertEq(reference.LastDecisionReason, h.Last(TelemetryEventType.DecisionMade).reason, "telemetry reason is the unchanged R-rule reason");
            AssertConsistent(h, state);
        }

        private static void RT04()
        {
            var h = NearHigh(beat: true);
            GhostExecutionState state = h.Tick();

            TelemetryRecord decision = h.Last(TelemetryEventType.DecisionMade);
            AssertEq(GhostAction.WARN, decision.ghostAction, "telemetry DecisionMade action");
            AssertTrue(decision.reason.StartsWith("WARN[W]"), "telemetry reason must identify rule W, was: " + decision.reason);
            AssertTrue(decision.reason.IndexOf("mandatory", StringComparison.OrdinalIgnoreCase) >= 0, "reason must name the mandatory warning beat: " + decision.reason);

            // The inner provider's own reason (FOLLOW, R8) must NOT be what telemetry shows.
            AssertTrue(h.RuleBased.LastDecisionReason.StartsWith("FOLLOW"), "inner provider still evaluated its own FOLLOW candidate");
            AssertTrue(decision.reason != h.RuleBased.LastDecisionReason, "telemetry must carry the outer reason, not the inner one");

            AssertEq(GhostAction.WARN, h.Last(TelemetryEventType.GhostActionExecuted).ghostAction, "executed action");
            AssertEq(GhostExecutionState.Warning, h.Last(TelemetryEventType.GhostExecutionStateChanged).ghostExecutionState, "state-changed state");
            AssertConsistent(h, state);
        }

        private static RuntimeTestHarness ProtectConditions(bool beat)
        {
            return new RuntimeTestHarness()
                .SetPhysical(PresenceLevel.VERY_NEAR, ConfidenceLevel.HIGH, false)
                .Player(inThreatZone: true, distanceToGhost: 0.5f)
                .Virtual(threat: 0.2f, beat: beat);
        }

        private static RuntimeTestHarness RetreatConditions(bool beat)
        {
            return new RuntimeTestHarness()
                .SetPhysical(PresenceLevel.NONE, ConfidenceLevel.HIGH, false)
                .Player(inThreatZone: false)
                .Virtual(threat: 0.8f, beat: beat);
        }

        private static void RT05()
        {
            var h = ProtectConditions(beat: true);
            GhostExecutionState state = h.Tick();
            var noBeat = ProtectConditions(beat: false);
            noBeat.Tick();

            AssertEq(GhostExecutionState.Protecting, state, "R1 PROTECT must beat the scripted warning");
            AssertEq(noBeat.Last(TelemetryEventType.DecisionMade).reason, h.Last(TelemetryEventType.DecisionMade).reason, "telemetry reason is the unchanged R1 reason");
            AssertTrue(h.Last(TelemetryEventType.DecisionMade).reason.StartsWith("PROTECT"), "reason must be the R1 PROTECT reason");
            AssertConsistent(h, state);
        }

        private static void RT06()
        {
            var h = RetreatConditions(beat: true);
            GhostExecutionState state = h.Tick();
            var noBeat = RetreatConditions(beat: false);
            noBeat.Tick();

            AssertEq(GhostExecutionState.Retreating, state, "R2 RETREAT must beat the scripted warning");
            AssertEq(noBeat.Last(TelemetryEventType.DecisionMade).reason, h.Last(TelemetryEventType.DecisionMade).reason, "telemetry reason is the unchanged R2 reason");
            AssertConsistent(h, state);
        }

        private static void RT07()
        {
            // Calm physical state, scripted beat active, ghost phase INTERRUPTED (lantern pulse).
            var h = new RuntimeTestHarness()
                .SetPhysical(PresenceLevel.NONE, ConfidenceLevel.HIGH, false)
                .Player(inThreatZone: false)
                .Virtual(threat: 0.2f, beat: true)
                .Phase(GhostPhase.INTERRUPTED);

            GhostExecutionState state = h.Tick();

            AssertEq(GhostExecutionState.Retreating, state, "interruption + beat must retreat, not warn");
            AssertTrue(h.Last(TelemetryEventType.DecisionMade).reason.StartsWith("RETREAT[E0]"), "reason must identify rule E0: " + h.Last(TelemetryEventType.DecisionMade).reason);
            AssertConsistent(h, state);

            // Interruption also wins with no beat at all.
            var calm = new RuntimeTestHarness()
                .SetPhysical(PresenceLevel.NONE, ConfidenceLevel.HIGH, false)
                .Player(inThreatZone: false)
                .Virtual(threat: 0.2f, beat: false)
                .Phase(GhostPhase.INTERRUPTED);
            AssertEq(GhostExecutionState.Retreating, calm.Tick(), "interruption without a beat");
        }

        private static void RT08()
        {
            // Consistency across every outer-layer branch, through the pipeline.
            var scenarios = new Func<RuntimeTestHarness>[]
            {
                () => NearHigh(beat: false),
                () => NearHigh(beat: true),
                () => ProtectConditions(beat: true),
                () => RetreatConditions(beat: true),
                () => new RuntimeTestHarness().SetPhysical(PresenceLevel.NEAR, ConfidenceLevel.HIGH, true).Player(false).Virtual(0.2f, beat: true),
                () => new RuntimeTestHarness().SetPhysical(PresenceLevel.NONE, ConfidenceLevel.HIGH, false).Player(false).Virtual(0.2f, beat: true).Phase(GhostPhase.INTERRUPTED),
            };

            foreach (var make in scenarios)
            {
                var h = make();
                GhostExecutionState state = h.Tick();
                AssertConsistent(h, state);
                AssertEq(1, h.Executor.Calls, "executor called exactly once per tick");
                AssertEq(1, h.Controller.ApplyCalls, "controller applied exactly once per tick");
            }
        }

        private static void RT09()
        {
            var a = NearHigh(beat: true);
            var b = NearHigh(beat: true);
            GhostExecutionState sa = a.Tick();
            GhostExecutionState sb = b.Tick();

            AssertEq(sa, sb, "execution state from independent instances");
            AssertEq(a.Last(TelemetryEventType.DecisionMade).ghostAction, b.Last(TelemetryEventType.DecisionMade).ghostAction, "action");
            AssertEq(a.Last(TelemetryEventType.DecisionMade).reason, b.Last(TelemetryEventType.DecisionMade).reason, "reason");
            AssertEq(a.Telemetry.Records.Count, b.Telemetry.Records.Count, "telemetry record count");
        }

        private static void RT10()
        {
            // With no beat, no interruption and no terminal flag, the runtime
            // chain must be indistinguishable from plain R1-R8: same action and
            // same reason, over the full grid of presence x confidence x disturbance x
            // threat-zone x four threat levels.
            int checkedInputs = 0;
            foreach (PresenceLevel presence in Enum.GetValues(typeof(PresenceLevel)))
            foreach (ConfidenceLevel confidence in Enum.GetValues(typeof(ConfidenceLevel)))
            foreach (bool disturbance in new[] { false, true })
            foreach (bool zone in new[] { false, true })
            foreach (float threat in new[] { 0f, 0.5f, 0.7f, 0.9f })
            {
                var h = new RuntimeTestHarness().SetPhysical(presence, confidence, disturbance).Player(zone).Virtual(threat);
                GhostExecutionState state = h.Tick();

                var reference = new RuleBasedDecisionProvider();
                GhostAction expected = reference.Decide(h.World.GetCurrentWorldState());

                string ctx = presence + "/" + confidence + "/dist=" + disturbance + "/zone=" + zone + "/threat=" + threat;
                AssertEq(expected, h.Last(TelemetryEventType.DecisionMade).ghostAction, "action @ " + ctx);
                AssertEq(reference.LastDecisionReason, h.Last(TelemetryEventType.DecisionMade).reason, "reason @ " + ctx);
                AssertEq(new GhostActionExecutor().Execute(expected), state, "state @ " + ctx);
                checkedInputs++;
            }

            int expectedSize = Enum.GetValues(typeof(PresenceLevel)).Length * Enum.GetValues(typeof(ConfidenceLevel)).Length * 2 * 2 * 4;
            AssertEq(expectedSize, checkedInputs, "grid size");
            Console.WriteLine("      (RT-10 compared " + checkedInputs + " inputs)");
        }

        private static void RT11()
        {
            var h = NearHigh(beat: true);
            AssertEq(GhostExecutionState.Warning, h.Tick(), "beat active");

            // The inner provider was still evaluated during the beat.
            AssertEq(GhostAction.FOLLOW, h.RuleBased.TemporalContext.PreviousAction.Value, "inner history records its own FOLLOW during the beat");

            // Game flow ends the beat: the very next tick is the inner decision again.
            h.Virtual(threat: 0.2f, beat: false);
            GhostExecutionState after = h.Tick();
            AssertEq(GhostExecutionState.Following, after, "beat ended -> inner R8 FOLLOW immediately");
            AssertConsistent(h, after);
        }

        private static void RT12()
        {
            // eventActive keeps its existing R6 meaning and is not a warning beat.
            var h = new RuntimeTestHarness()
                .SetPhysical(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false)
                .Player(false)
                .Virtual(threat: 0.2f, eventActive: true, beat: false);
            AssertEq(GhostExecutionState.Following, h.Tick(), "eventActive with NEAR presence is not a warning (R6 needs presence NONE)");

            var r6 = new RuntimeTestHarness()
                .SetPhysical(PresenceLevel.NONE, ConfidenceLevel.HIGH, false)
                .Player(false)
                .Virtual(threat: 0.2f, eventActive: true, beat: false);
            GhostExecutionState state = r6.Tick();
            AssertEq(GhostExecutionState.Warning, state, "eventActive + presence NONE is the existing R6 WARN");
            AssertTrue(r6.Last(TelemetryEventType.DecisionMade).reason == "WARN: virtual event detected", "reason is the unchanged R6 string, not rule W: " + r6.Last(TelemetryEventType.DecisionMade).reason);
        }
    }
}
