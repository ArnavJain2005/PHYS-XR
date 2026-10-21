using System;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Decision;
using PHYSXR.PhysicalWorld;
using PHYSXR.WorldStateSystem;

namespace PHYSXR.Testing
{
    // Tests for the mandatory-warning-beat arbitration in
    // EncounterDecisionLayer (WB-01..12). Same dependency-light style as the
    // other *Tests classes. R1-R8 themselves are covered unchanged by
    // RuleBasedDecisionProviderTests / HysteresisTests.
    public static class EncounterDecisionLayerTests
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

            Run("WB-01_Beat_PresenceNone_ProducesWarn", WB01);
            Run("WB-02_Beat_NearHighNoDisturbance_ProducesWarn", WB02);
            Run("WB-03_NoBeat_NearHighNoDisturbance_KeepsExistingFollow", WB03);
            Run("WB-04a_Beat_GenuineProtect_ProtectWins", WB04a);
            Run("WB-04b_Beat_GenuineRetreat_RetreatWins", WB04b);
            Run("WB-05_Beat_VeryNearHighOutsideThreatZone_ProducesWarn", WB05);
            Run("WB-06_Beat_DefensiveInterruption_InterruptionWins", WB06);
            Run("WB-07_Beat_GameplayTerminal_NoWarnOverride", WB07);
            Run("WB-08_EventActiveWithoutBeat_NearHigh_KeepsExistingFollow", WB08);
            Run("WB-09_Beat_Investigate_BeatWarns_InnerStillEvaluated", WB09);
            Run("WB-10_BeatReasonNamesRuleAndInnerCandidate", WB10);
            Run("WB-11_WorldStateManagerCopiesNewFields", WB11);
            Run("WB-12_SameInputSameOutput_Deterministic", WB12);

            Console.WriteLine("EncounterDecisionLayerTests: " + passed + " passed, " + failed + " failed");
            return failed;
        }

        private static long clock = 1000;

        private static WorldState World(
            PresenceLevel presence, ConfidenceLevel confidence, bool disturbance,
            bool inThreatZone = false, float threat = 0.2f, bool eventActive = false,
            bool beat = false, bool terminal = false, GhostPhase phase = GhostPhase.NORMAL)
        {
            clock += 100;
            return new WorldState
            {
                physical = new PhysicalState { presence = presence, confidence = confidence, disturbance = disturbance, timestamp = clock },
                player = new PlayerState { distanceToGhost = 5f, movement = true, inThreatZone = inThreatZone },
                virtualWorld = new VirtualState { threatLevel = threat, eventActive = eventActive, mandatoryWarningBeat = beat, gameplayTerminal = terminal },
                ghost = new GhostState { currentState = GhostAction.FOLLOW, phase = phase }
            };
        }

        private static EncounterDecisionLayer NewLayer() => new EncounterDecisionLayer(new RuleBasedDecisionProvider());

        private static void AssertEqual(GhostAction expected, GhostAction actual, string context)
        {
            if (expected != actual)
                throw new Exception(context + ": expected " + expected + " but got " + actual);
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void WB01()
        {
            var layer = NewLayer();
            var a = layer.Decide(World(PresenceLevel.NONE, ConfidenceLevel.HIGH, false, beat: true));
            AssertEqual(GhostAction.WARN, a, "beat + presence NONE");
            AssertTrue(layer.LastRuleId == "W", "rule should be W, was " + layer.LastRuleId);
        }

        private static void WB02()
        {
            var layer = NewLayer();
            var a = layer.Decide(World(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false, beat: true));
            AssertEqual(GhostAction.WARN, a, "beat + NEAR/HIGH/no disturbance");
            AssertEqual(GhostAction.FOLLOW, InnerOnly(World(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false, beat: true)),
                "inner R1-R8 alone (R8) for the same input");
        }

        private static void WB03()
        {
            var layer = NewLayer();
            var a = layer.Decide(World(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false));
            AssertEqual(GhostAction.FOLLOW, a, "no beat + NEAR/HIGH/no disturbance");
            AssertTrue(layer.LastRuleId == "INNER", "rule should be INNER, was " + layer.LastRuleId);
        }

        private static void WB04a()
        {
            var layer = NewLayer();
            var a = layer.Decide(World(PresenceLevel.VERY_NEAR, ConfidenceLevel.HIGH, false, inThreatZone: true, beat: true));
            AssertEqual(GhostAction.PROTECT, a, "beat + R1 PROTECT condition");
            AssertTrue(layer.LastRuleId == "S", "rule should be S, was " + layer.LastRuleId);
        }

        private static void WB04b()
        {
            var layer = NewLayer();
            var a = layer.Decide(World(PresenceLevel.NONE, ConfidenceLevel.HIGH, false, threat: 0.8f, beat: true));
            AssertEqual(GhostAction.RETREAT, a, "beat + R2 RETREAT condition");
            AssertTrue(layer.LastRuleId == "S", "rule should be S, was " + layer.LastRuleId);
        }

        private static void WB05()
        {
            var layer = NewLayer();
            var a = layer.Decide(World(PresenceLevel.VERY_NEAR, ConfidenceLevel.HIGH, false, inThreatZone: false, beat: true));
            AssertEqual(GhostAction.WARN, a, "beat + VERY_NEAR/HIGH outside threat zone, no disturbance");
        }

        private static void WB06()
        {
            var layer = NewLayer();
            var a = layer.Decide(World(PresenceLevel.NONE, ConfidenceLevel.HIGH, false, beat: true, phase: GhostPhase.INTERRUPTED));
            AssertEqual(GhostAction.RETREAT, a, "beat + defensive interruption");
            AssertTrue(layer.LastRuleId == "E0", "rule should be E0, was " + layer.LastRuleId);
        }

        private static void WB07()
        {
            var layer = NewLayer();
            var a = layer.Decide(World(PresenceLevel.NONE, ConfidenceLevel.HIGH, false, beat: true, terminal: true));
            AssertEqual(GhostAction.FOLLOW, a, "beat during terminal state must not produce WARN");
            AssertTrue(layer.LastRuleId == "G0", "rule should be G0, was " + layer.LastRuleId);
        }

        private static void WB08()
        {
            var layer = NewLayer();
            var a = layer.Decide(World(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false, eventActive: true));
            AssertEqual(GhostAction.FOLLOW, a, "eventActive alone is not a warning beat");
        }

        private static void WB09()
        {
            var layer = NewLayer();
            var a = layer.Decide(World(PresenceLevel.NEAR, ConfidenceLevel.HIGH, true, beat: true));
            AssertEqual(GhostAction.WARN, a, "beat + R3 INVESTIGATE candidate");
            AssertEqual(GhostAction.INVESTIGATE, layer.Inner.TemporalContext.PreviousAction.Value,
                "inner provider still evaluated and recorded its own result");
        }

        private static void WB10()
        {
            var layer = NewLayer();
            layer.Decide(World(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false, beat: true));
            AssertTrue(layer.LastDecisionReason.StartsWith("WARN[W]"), "reason should start with WARN[W]: " + layer.LastDecisionReason);
            AssertTrue(layer.LastDecisionReason.Contains("FOLLOW"), "reason should name inner candidate: " + layer.LastDecisionReason);
        }

        private static void WB11()
        {
            var manager = new WorldStateManager(new PhysicalStateManager());
            manager.UpdateVirtualState(new VirtualState { threatLevel = 0.2f, mandatoryWarningBeat = true, gameplayTerminal = true });
            manager.UpdateGhostState(new GhostState { currentState = GhostAction.WARN, phase = GhostPhase.INTERRUPTED });
            WorldState snapshot = manager.GetCurrentWorldState();
            AssertTrue(snapshot.virtualWorld.mandatoryWarningBeat, "mandatoryWarningBeat copied");
            AssertTrue(snapshot.virtualWorld.gameplayTerminal, "gameplayTerminal copied");
            AssertTrue(snapshot.ghost.phase == GhostPhase.INTERRUPTED, "phase copied");
        }

        private static void WB12()
        {
            var w = World(PresenceLevel.NEAR, ConfidenceLevel.HIGH, false, beat: true);
            var a1 = NewLayer().Decide(w);
            var a2 = NewLayer().Decide(w);
            AssertEqual(a1, a2, "two fresh layers, same input");
        }

        private static GhostAction InnerOnly(WorldState w) => new RuleBasedDecisionProvider().Decide(w);
    }
}
