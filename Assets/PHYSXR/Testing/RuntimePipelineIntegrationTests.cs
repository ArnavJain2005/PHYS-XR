using System;
using System.Collections.Generic;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Decision;
using PHYSXR.Ghost;
using PHYSXR.PhysicalWorld;
using PHYSXR.Telemetry;
using PHYSXR.WorldStateSystem;

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
            Run("PRI1-S1_SafeKnock_InvestigatesOnce_AttributedActed_ThenFollows", Pri1S1);
            Run("PRI1-S2_SameKnockInDanger_HigherPriorityWins_ExplainedNotAttributed", Pri1S2);
            Run("PRI1-S3_InterruptedInvestigation_ReevaluatesFromCurrentState", Pri1S3);
            Run("PRI1-DET_SameSequence_SameRecords_TelemetryDoesNotChangeDecisions", Pri1Det);

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
        // ------------------------------------------------------------ Priority 1
        //
        // PRI1-*: the game's own composition - gated WorldStateManager on a
        // game clock, GhostRuntimePipeline.CreateRuntimeChain(clock, ...) with
        // change-only telemetry, the real executor and controller - driven
        // frame by frame in GameFlowController's order. Each accepted physical
        // event is handed in as WorldStateManager.NotifyAcceptedEvent does in
        // the game (deduplication happens upstream; SENS-T04 covers it). The
        // raw per-line disturbance flag is held true throughout to show the
        // gated world ignores it.
        private sealed class GameRig
        {
            public long Now = 10000;
            public readonly PhysicalStateManager Physical = new PhysicalStateManager();
            public readonly WorldStateManager World;
            public readonly GhostBehaviourController Controller = new GhostBehaviourController();
            public readonly InMemoryTelemetrySink Sink = new InMemoryTelemetrySink();
            public readonly GhostRuntimePipeline Pipeline;
            public readonly List<GhostAction> Actions = new List<GhostAction>();
            public readonly List<GhostExecutionState> States = new List<GhostExecutionState>();

            public PresenceLevel Presence = PresenceLevel.NONE;
            public ConfidenceLevel Confidence = ConfidenceLevel.HIGH;
            public float Distance = 8f;
            public bool Zone, Band, Committed, Beat;
            public float Threat = 0.2f;
            public GhostPhase Phase = GhostPhase.NORMAL;

            public GameRig(bool telemetry = true)
            {
                World = new WorldStateManager(Physical, () => Now);
                World.UpdateSourceStatus(PhysicalSourceStatus.FRESH);
                Pipeline = GhostRuntimePipeline.CreateRuntimeChain(() => Now, new GhostActionExecutor(), Controller);
                Pipeline.ChangeOnlyTelemetry = true;
                Pipeline.TelemetrySink = telemetry ? Sink : null;
            }

            public void Knock(long id)
            {
                World.NotifyAcceptedEvent(new PhysicalEvent { eventType = "DISTURBANCE", eventId = id, boot = 1 });
            }

            public void Frames(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Now += 50;
                    Physical.UpdateState(new PhysicalState { presence = Presence, confidence = Confidence, disturbance = true, timestamp = Now });
                    World.UpdatePlayerState(new PlayerState { distanceToGhost = Distance, inThreatZone = Zone, inWarnBand = Band });
                    World.UpdateVirtualState(new VirtualState
                    {
                        threatLevel = Threat, encounterCommitted = Committed, mandatoryWarningBeat = Beat, eventActive = Beat
                    });
                    World.UpdateGhostState(new GhostState { phase = Phase, currentState = Pipeline.LastAction ?? GhostAction.FOLLOW });
                    GhostExecutionState state = Pipeline.Run(World.GetCurrentWorldState(), Now);
                    Actions.Add(Pipeline.LastAction.Value);
                    States.Add(state);
                }
            }

            public List<TelemetryRecord> OfType(string type)
            {
                var list = new List<TelemetryRecord>();
                foreach (TelemetryRecord r in Sink.Records)
                    if (r.type == type)
                        list.Add(r);
                return list;
            }

            public TelemetryRecord SingleOutcome(long eventId)
            {
                TelemetryRecord found = null;
                foreach (TelemetryRecord r in OfType("PHYS_EVENT_OUTCOME"))
                {
                    if (r.relatedId != eventId)
                        continue;
                    if (found != null)
                        throw new Exception("more than one PHYS_EVENT_OUTCOME for evt=" + eventId);
                    found = r;
                }
                if (found == null)
                    throw new Exception("no PHYS_EVENT_OUTCOME for evt=" + eventId);
                return found;
            }

            // Controller states in order, consecutive repeats collapsed.
            public string StateSequence()
            {
                var parts = new List<string>();
                foreach (GhostExecutionState s in States)
                    if (parts.Count == 0 || parts[parts.Count - 1] != s.ToString())
                        parts.Add(s.ToString());
                return string.Join(">", parts);
            }

            public int Episodes(GhostAction action)
            {
                int episodes = 0;
                for (int i = 0; i < Actions.Count; i++)
                    if (Actions[i] == action && (i == 0 || Actions[i - 1] != action))
                        episodes++;
                return episodes;
            }

            public string Trace()
            {
                var b = new System.Text.StringBuilder();
                foreach (TelemetryRecord r in Sink.Records)
                    b.Append(r.timestamp).Append('|').Append(r.type).Append('|').Append(r.ghostAction).Append('|').Append(r.ruleId)
                        .Append('|').Append(r.causeEventId).Append('|').Append(r.reason).Append('|').Append(r.detail).Append('\n');
                return b.ToString();
            }
        }

        private static void AssertOutcome(TelemetryRecord outcome, string expected, GhostAction action, string rule, string inner, string context)
        {
            AssertTrue(outcome.detail.Contains("outcome=" + expected + " "), context + ": outcome " + expected + " expected, got [" + outcome.detail + "]");
            AssertEq(action, outcome.ghostAction.Value, context + ": outcome action");
            AssertEq(rule, outcome.ruleId, context + ": winning rule");
            AssertTrue(outcome.detail.Contains("inner=" + inner + " "), context + ": inner rule " + inner + " expected, got [" + outcome.detail + "]");
        }

        private static void AssertNoDecisionAttributed(GameRig rig, string context)
        {
            foreach (TelemetryRecord d in rig.OfType("DECISION"))
                AssertTrue(d.causeEventId == null, context + ": DECISION " + d.ghostAction + " [" + d.ruleId + "] must not carry cause_evt=" + d.causeEventId);
        }

        // Scenario 1: a fresh, confident knock while the player is safe.
        private static void Pri1S1()
        {
            var rig = new GameRig { Presence = PresenceLevel.NEAR };
            rig.Frames(20);
            rig.Knock(1);
            rig.Frames(140); // 7 s: the 3 s pulse, the dwell, then calm

            AssertEq(1, rig.Episodes(GhostAction.INVESTIGATE), "exactly one INVESTIGATE episode for one knock (raw flag held true)");
            AssertEq("Following>Investigating>Following", rig.StateSequence(), "the decision reaches the controller and it returns to FOLLOW");
            AssertEq(GhostAction.FOLLOW, rig.Actions[rig.Actions.Count - 1], "ends in FOLLOW");

            List<TelemetryRecord> decisions = rig.OfType("DECISION");
            TelemetryRecord investigate = decisions.Find(d => d.ghostAction == GhostAction.INVESTIGATE);
            AssertTrue(investigate != null && investigate.causeEventId == 1, "the INVESTIGATE decision is attributed to evt 1");
            foreach (TelemetryRecord d in decisions)
                if (d.ghostAction != GhostAction.INVESTIGATE)
                    AssertTrue(d.causeEventId == null, "only the INVESTIGATE decision is attributed, not " + d.ghostAction);

            AssertOutcome(rig.SingleOutcome(1), GhostRuntimePipeline.OutcomeActed, GhostAction.INVESTIGATE, "INNER", "R3", "S1");
            AssertEq(1, rig.OfType("PHYS_EVENT_OUTCOME").Count, "one outcome record in total");

            // R5: the same knock with nobody near the station also acts, as WARN.
            var r5 = new GameRig();
            r5.Frames(20);
            r5.Knock(2);
            r5.Frames(140);
            AssertOutcome(r5.SingleOutcome(2), GhostRuntimePipeline.OutcomeActed, GhostAction.WARN, "INNER", "R5", "S1/R5");
            AssertEq(1, r5.Episodes(GhostAction.WARN), "one WARN episode");
        }

        // Scenario 2: the same knock while the player is in danger, or while a
        // more urgent rule applies. The knock never becomes INVESTIGATE there,
        // no decision is credited to it, and its outcome names the winning rule.
        private static void Pri1S2()
        {
            void Case(string name, Action<GameRig> setUp, GhostAction expectedAction, string rule, string outcome, string inner, string states)
            {
                var rig = new GameRig { Presence = PresenceLevel.NEAR };
                setUp(rig);
                rig.Frames(20);
                rig.Knock(7);
                rig.Frames(80); // past the 3 s pulse, so the outcome is written

                AssertEq(0, rig.Episodes(GhostAction.INVESTIGATE), name + ": no INVESTIGATE");
                AssertEq(expectedAction, rig.Actions[rig.Actions.Count - 1], name + ": selected action");
                AssertEq(states, rig.StateSequence(), name + ": controller");
                AssertNoDecisionAttributed(rig, name);
                AssertOutcome(rig.SingleOutcome(7), outcome, expectedAction, rule, inner, name);
            }

            // Player inside the ghost's threat zone: R3 and R5 do not apply there.
            Case("pre-commit, threat zone", r => { r.Distance = 3f; r.Zone = true; r.Band = true; },
                GhostAction.FOLLOW, "INNER", GhostRuntimePipeline.OutcomeNotQualified, "R8", "Following");
            Case("committed encounter, threat zone", r => { r.Distance = 3f; r.Zone = true; r.Band = true; r.Committed = true; },
                GhostAction.WARN, "E4", GhostRuntimePipeline.OutcomeNotQualified, "R8", "Warning");
            Case("person VERY_NEAR, threat zone", r => { r.Presence = PresenceLevel.VERY_NEAR; r.Distance = 3f; r.Zone = true; r.Band = true; },
                GhostAction.PROTECT, "S", GhostRuntimePipeline.OutcomeNotQualified, "R1", "Protecting");

            // Player outside the zone, so R3 qualifies, but a more urgent rule wins.
            Case("attack wind-up in progress", r => { r.Phase = GhostPhase.WINDUP; },
                GhostAction.ATTACK, "E1", GhostRuntimePipeline.OutcomeOutranked, "R3", "Attacking");
            Case("power surge (critical threat)", r => { r.Threat = 0.8f; },
                GhostAction.RETREAT, "S", GhostRuntimePipeline.OutcomeOutranked, "R2", "Retreating");
            Case("scripted warning beat", r => { r.Beat = true; },
                GhostAction.WARN, "W", GhostRuntimePipeline.OutcomeOutranked, "R3", "Warning");

            // Danger escalates during the knock's pulse: PROTECT starts while the
            // pulse is still active. That new decision is PROTECT's, not the knock's.
            var escalate = new GameRig { Presence = PresenceLevel.NEAR, Distance = 3f, Zone = true, Band = true };
            escalate.Frames(20);
            escalate.Knock(10);
            escalate.Frames(10);
            escalate.Presence = PresenceLevel.VERY_NEAR;
            escalate.Frames(70);
            AssertEq("Following>Protecting", escalate.StateSequence(), "escalate: PROTECT during the pulse");
            AssertTrue(escalate.OfType("DECISION").Exists(d => d.ghostAction == GhostAction.PROTECT), "escalate: a PROTECT decision was logged in the pulse");
            AssertNoDecisionAttributed(escalate, "escalate");
            AssertOutcome(escalate.SingleOutcome(10), GhostRuntimePipeline.OutcomeNotQualified, GhostAction.FOLLOW, "INNER", "R8", "escalate");

            // Context still decides: in the zone first, out of it within the
            // pulse - the event then acts, and its single outcome is ACTED.
            var late = new GameRig { Presence = PresenceLevel.NEAR, Distance = 3f, Zone = true, Band = true };
            late.Frames(20);
            late.Knock(8);
            late.Frames(10);
            late.Distance = 8f; late.Zone = false; late.Band = false;
            late.Frames(100);
            AssertEq(1, late.Episodes(GhostAction.INVESTIGATE), "acts once the player leaves the zone within the pulse");
            AssertOutcome(late.SingleOutcome(8), GhostRuntimePipeline.OutcomeActed, GhostAction.INVESTIGATE, "INNER", "R3", "late");

            // The run turning terminal mid-pulse still writes the pending outcome.
            var terminal = new GameRig { Presence = PresenceLevel.NEAR, Distance = 3f, Zone = true, Band = true };
            terminal.Frames(20);
            terminal.Knock(9);
            terminal.Frames(10);
            terminal.Pipeline.EngageTerminalGate();
            AssertOutcome(terminal.SingleOutcome(9), GhostRuntimePipeline.OutcomeNotQualified, GhostAction.FOLLOW, "INNER", "R8", "terminal");
        }

        // Scenario 3: an investigation interrupted by a higher-priority
        // condition. When it clears, the ghost re-decides from the current
        // world; the knock is never re-credited after its pulse.
        private static void Pri1S3()
        {
            // a) PROTECT outlasts the pulse: straight back to FOLLOW.
            var a = new GameRig { Presence = PresenceLevel.NEAR };
            a.Frames(10); a.Knock(1); a.Frames(20);
            a.Presence = PresenceLevel.VERY_NEAR; a.Distance = 3f; a.Zone = true; a.Frames(80);
            a.Presence = PresenceLevel.NEAR; a.Distance = 8f; a.Zone = false; a.Frames(80);
            AssertEq("Following>Investigating>Protecting>Following", a.StateSequence(), "a: interrupted, then re-evaluated");
            AssertEq(1, a.Episodes(GhostAction.INVESTIGATE), "a: the expired knock is not investigated again");
            TelemetryRecord protect = a.OfType("DECISION").Find(d => d.ghostAction == GhostAction.PROTECT);
            AssertTrue(protect != null && protect.causeEventId == null, "a: PROTECT is not credited to the knock");
            AssertOutcome(a.SingleOutcome(1), GhostRuntimePipeline.OutcomeActed, GhostAction.INVESTIGATE, "INNER", "R3", "a");

            // b) PROTECT ends while the knock is still fresh: R3 applies again
            //    to the same live event (credited), then the dwell holds it
            //    after the pulse (not credited), then FOLLOW.
            var b = new GameRig { Presence = PresenceLevel.NEAR };
            b.Frames(10); b.Knock(1); b.Frames(20);
            b.Presence = PresenceLevel.VERY_NEAR; b.Distance = 3f; b.Zone = true; b.Frames(20);
            b.Presence = PresenceLevel.NEAR; b.Distance = 8f; b.Zone = false; b.Frames(100);
            AssertEq("Following>Investigating>Protecting>Investigating>Following", b.StateSequence(), "b: re-evaluated from the current world");
            foreach (TelemetryRecord d in b.OfType("DECISION"))
            {
                bool r3 = d.ghostAction == GhostAction.INVESTIGATE && d.reason.StartsWith("INVESTIGATE:");
                AssertTrue(r3 ? d.causeEventId == 1 : d.causeEventId == null,
                    "b: only R3 INVESTIGATE decisions are credited; " + d.ghostAction + " \"" + d.reason + "\" cause=" + d.causeEventId);
            }
            AssertOutcome(b.SingleOutcome(1), GhostRuntimePipeline.OutcomeActed, GhostAction.INVESTIGATE, "INNER", "R3", "b");

            // c) The power surge interrupts it: RETREAT, then FOLLOW.
            var c = new GameRig { Presence = PresenceLevel.NEAR };
            c.Frames(10); c.Knock(1); c.Frames(20);
            c.Threat = 0.8f; c.Frames(60);
            c.Threat = 0.2f; c.Frames(80);
            AssertEq("Following>Investigating>Retreating>Following", c.StateSequence(), "c: surge interrupts, then re-evaluated");
            TelemetryRecord retreat = c.OfType("DECISION").Find(d => d.ghostAction == GhostAction.RETREAT);
            AssertTrue(retreat != null && retreat.causeEventId == null, "c: RETREAT is not credited to the knock");
            AssertEq(1, c.OfType("PHYS_EVENT_OUTCOME").Count, "c: one outcome record");
        }

        // The same input sequence gives the same records, and telemetry (on or
        // off) never changes a decision.
        private static void Pri1Det()
        {
            void Script(GameRig r)
            {
                r.Presence = PresenceLevel.NEAR;
                r.Frames(10); r.Knock(1); r.Frames(20);
                r.Presence = PresenceLevel.VERY_NEAR; r.Distance = 3f; r.Zone = true; r.Band = true; r.Frames(30);
                r.Knock(2); r.Frames(30);
                r.Presence = PresenceLevel.NONE; r.Distance = 8f; r.Zone = false; r.Band = false; r.Frames(40);
                r.Knock(3); r.Threat = 0.8f; r.Frames(40);
                r.Threat = 0.2f; r.Committed = true; r.Band = true; r.Distance = 5f; r.Frames(80);
            }

            var first = new GameRig();
            var second = new GameRig();
            var silent = new GameRig(telemetry: false);
            Script(first); Script(second); Script(silent);

            AssertEq(first.Trace(), second.Trace(), "identical records for identical input");
            AssertEq(string.Join(",", first.Actions), string.Join(",", silent.Actions), "telemetry does not change decisions");
            AssertEq(3, first.OfType("PHYS_EVENT_OUTCOME").Count, "one outcome per event");
        }
    }
}
