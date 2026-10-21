using System;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Decision;

namespace PHYSXR.Testing
{
    // ENC suite (Technical Spec section D, test T12): for each encounter rule
    // E0-E4 a fires case, a does-not-fire case and a priority case, plus the
    // outer dwell, Reset, decision-level versions of T11 (stability) and T14
    // (lantern interruption timing), and the invariant that no physical input
    // can cause an ATTACK. Runs on an injected clock, like the game.
    public static class EncounterRuleTests
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

            Run("ENC-E0a_Interrupted_Retreats", E0a);
            Run("ENC-E0b_NormalPhase_NoE0", E0b);
            Run("ENC-E0c_Interrupted_OutranksProtectAndBeat", E0c);
            Run("ENC-E1a_AttackPhases_HoldAttack", E1a);
            Run("ENC-E1b_NormalPhase_NoE1", E1b);
            Run("ENC-E1c_AttackInProgress_OutranksProtectAndBeat_NotTerminal", E1c);
            Run("ENC-E2a_ProtectCap_After6s_TreatedAsFollow", E2a);
            Run("ENC-E2b_ProtectUnder6s_StaysProtect", E2b);
            Run("ENC-E2c_Refractory10s_ThenProtectAgain", E2c);
            Run("ENC-E3a_CommittedWarnedClose_Attacks", E3a);
            Run("ENC-E3b_EachConditionMissing_NoAttack", E3b);
            Run("ENC-E3c_ProtectAndBeat_PreventAttack", E3c);
            Run("ENC-E3d_NoPhysicalInputCanCauseAttack", E3d);
            Run("ENC-E4a_CommittedInBand_Warns", E4a);
            Run("ENC-E4b_NotCommittedOrOutOfBand_NoE4", E4b);
            Run("ENC-E4c_YieldsToInvestigateAndRetreat", E4c);
            Run("ENC-DWELL_BandExit_WarnHeld1500ms", Dwell);
            Run("ENC-RESET_ClearsTelegraphAndCap", ResetTest);
            Run("ENC-T11_Oscillation_NoWarnShorterThanDwell", T11);
            Run("ENC-T14_InterruptDuringWindup_RetreatsAndNextAttackAfter11_5s", T14);
            Run("ENC-FLOW_AttackMiss_RecoverCooldownThenFreshTelegraph", AttackFlow);

            Console.WriteLine("EncounterRuleTests: " + passed + " passed, " + failed + " failed");
            return failed;
        }

        private sealed class Rig
        {
            public long Now;
            public readonly EncounterDecisionLayer Layer;

            public Rig()
            {
                Layer = new EncounterDecisionLayer(new RuleBasedDecisionProvider(decisionClock: () => Now), () => Now);
            }

            public GhostAction Step(WorldState w, long dtMs = 100)
            {
                Now += dtMs;
                return Layer.Decide(w);
            }
        }

        private static WorldState W(
            float distance = 10f, bool committed = false, bool band = false, bool threatZone = false,
            PresenceLevel presence = PresenceLevel.NONE, ConfidenceLevel confidence = ConfidenceLevel.LOW, bool disturbance = false,
            float threat = 0.2f, bool beat = false, bool terminal = false, bool grace = false,
            GhostPhase phase = GhostPhase.NORMAL, long cooldown = 0)
        {
            return new WorldState
            {
                physical = new PhysicalState { presence = presence, confidence = confidence, disturbance = disturbance, timestamp = 1 },
                player = new PlayerState { distanceToGhost = distance, inThreatZone = threatZone, inWarnBand = band, movement = true },
                virtualWorld = new VirtualState { threatLevel = threat, mandatoryWarningBeat = beat, gameplayTerminal = terminal, encounterCommitted = committed, attackGraceActive = grace },
                ghost = new GhostState { phase = phase, attackCooldownMs = cooldown }
            };
        }

        private static WorldState ProtectWorld(bool committed = false, float distance = 3f) =>
            W(distance: distance, committed: committed, band: true, threatZone: true, presence: PresenceLevel.VERY_NEAR, confidence: ConfidenceLevel.HIGH);

        private static void AssertEq<T>(T expected, T actual, string context)
        {
            if (!Equals(expected, actual))
                throw new Exception(context + ": expected " + expected + " but got " + actual);
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        // Holds an E4 WARN for holdMs at the given distance, inside the band.
        private static void Telegraph(Rig rig, long holdMs, float distance = 1.8f)
        {
            for (long t = 0; t < holdMs; t += 100)
                AssertEq(GhostAction.WARN, rig.Step(W(distance: distance + 3f, committed: true, band: true)), "telegraph WARN");
        }

        // ------------------------------------------------------------------ E0

        private static void E0a()
        {
            var rig = new Rig();
            AssertEq(GhostAction.RETREAT, rig.Step(W(phase: GhostPhase.INTERRUPTED)), "interrupted");
            AssertEq("E0", rig.Layer.LastRuleId, "rule");
        }

        private static void E0b()
        {
            var rig = new Rig();
            rig.Step(W());
            AssertTrue(rig.Layer.LastRuleId != "E0", "no E0 in NORMAL");
        }

        private static void E0c()
        {
            var rig = new Rig();
            WorldState w = ProtectWorld();
            w.ghost.phase = GhostPhase.INTERRUPTED;
            w.virtualWorld.mandatoryWarningBeat = true;
            AssertEq(GhostAction.RETREAT, rig.Step(w), "E0 over R1 PROTECT and beat");
        }

        // ------------------------------------------------------------------ E1

        private static void E1a()
        {
            foreach (GhostPhase phase in new[] { GhostPhase.WINDUP, GhostPhase.STRIKE, GhostPhase.RECOVER })
            {
                var rig = new Rig();
                AssertEq(GhostAction.ATTACK, rig.Step(W(committed: true, phase: phase)), phase.ToString());
                AssertEq("E1", rig.Layer.LastRuleId, phase + " rule");
            }
        }

        private static void E1b()
        {
            var rig = new Rig();
            AssertTrue(rig.Step(W(committed: true, distance: 1f, band: true)) != GhostAction.ATTACK, "NORMAL with no telegraph: no ATTACK");
        }

        private static void E1c()
        {
            var rig = new Rig();
            WorldState w = ProtectWorld(committed: true);
            w.ghost.phase = GhostPhase.WINDUP;
            w.virtualWorld.mandatoryWarningBeat = true;
            AssertEq(GhostAction.ATTACK, rig.Step(w), "attack in progress keeps its window");

            var terminal = new Rig();
            AssertTrue(terminal.Step(W(committed: true, phase: GhostPhase.STRIKE, terminal: true)) != GhostAction.ATTACK || terminal.Layer.LastRuleId == "G0",
                "terminal: G0 decides");
            AssertEq("G0", terminal.Layer.LastRuleId, "G0 outranks E1");
        }

        // ------------------------------------------------------------------ E2

        private static void E2a()
        {
            var rig = new Rig();
            AssertEq(GhostAction.PROTECT, rig.Step(ProtectWorld(), 0), "PROTECT starts at t=0");
            GhostAction last = GhostAction.PROTECT;
            for (int i = 0; i < 60; i++)
                last = rig.Step(ProtectWorld());
            AssertEq(GhostAction.FOLLOW, last, "6 s of PROTECT is capped");
            AssertEq("E2", rig.Layer.LastRuleId, "rule");
        }

        private static void E2b()
        {
            var rig = new Rig();
            rig.Step(ProtectWorld(), 0);
            GhostAction last = GhostAction.PROTECT;
            for (int i = 0; i < 59; i++)
                last = rig.Step(ProtectWorld());
            AssertEq(GhostAction.PROTECT, last, "5.9 s");
        }

        private static void E2c()
        {
            var rig = new Rig();
            rig.Step(ProtectWorld(), 0);
            for (int i = 0; i < 60; i++)
                rig.Step(ProtectWorld()); // cap at t=6.0 s, refractory to 16.0 s
            for (int i = 0; i < 20; i++)
                rig.Step(W()); // condition gone for 2 s
            AssertEq(GhostAction.FOLLOW, rig.Step(ProtectWorld()), "PROTECT condition again at 8.1 s: refractory");
            rig.Now = 16000;
            AssertEq(GhostAction.PROTECT, rig.Step(ProtectWorld()), "after the 10 s refractory PROTECT is allowed again");
        }

        // ------------------------------------------------------------------ E3

        private static void E3a()
        {
            var rig = new Rig();
            Telegraph(rig, 1500);
            AssertEq(GhostAction.ATTACK, rig.Step(W(distance: 1.8f, committed: true, band: true)), "E3");
            AssertEq("E3", rig.Layer.LastRuleId, "rule");
            AssertTrue(rig.Layer.LastDecisionReason.StartsWith("ATTACK[E3]: committed, warned 1.5s, distance 1.8m"), rig.Layer.LastDecisionReason);
        }

        private static void E3b()
        {
            var cases = new (string name, WorldState w, long telegraphMs)[]
            {
                ("not committed", W(distance: 1.8f, committed: false, band: true), 1500),
                ("restart grace", W(distance: 1.8f, committed: true, band: true, grace: true), 1500),
                ("distance 2.1 m", W(distance: 2.1f, committed: true, band: true), 1500),
                ("cooldown", W(distance: 1.8f, committed: true, band: true, cooldown: 2000), 1500),
                ("warned only 1.4 s", W(distance: 1.8f, committed: true, band: true), 1400),
            };
            foreach (var c in cases)
            {
                var rig = new Rig();
                Telegraph(rig, c.telegraphMs);
                AssertTrue(rig.Step(c.w) != GhostAction.ATTACK, "no attack when " + c.name);
            }
        }

        private static void E3c()
        {
            var rig = new Rig();
            Telegraph(rig, 2000);
            WorldState protect = ProtectWorld(committed: true, distance: 1.5f);
            AssertEq(GhostAction.PROTECT, rig.Step(protect), "R1 PROTECT prevents the attack");

            var beat = new Rig();
            Telegraph(beat, 2000);
            AssertEq(GhostAction.WARN, beat.Step(W(distance: 1.5f, committed: true, band: true, beat: true)), "no new attack during a beat");
            AssertEq("W", beat.Layer.LastRuleId, "beat rule");
        }

        private static void E3d()
        {
            // Outside the committed encounter, or beyond 2 m, every physical
            // combination over a long run produces no ATTACK.
            foreach (PresenceLevel presence in Enum.GetValues(typeof(PresenceLevel)))
            foreach (ConfidenceLevel confidence in Enum.GetValues(typeof(ConfidenceLevel)))
            foreach (bool disturbance in new[] { false, true })
            foreach (bool committed in new[] { false, true })
            {
                var rig = new Rig();
                for (int i = 0; i < 100; i++)
                {
                    float distance = committed ? 2.5f : 1.0f;
                    GhostAction a = rig.Step(W(distance: distance, committed: committed, band: true,
                        presence: presence, confidence: confidence, disturbance: disturbance));
                    AssertTrue(a != GhostAction.ATTACK, "ATTACK from " + presence + "/" + confidence + "/" + disturbance + " committed=" + committed);
                }
            }
        }

        // ------------------------------------------------------------------ E4

        private static void E4a()
        {
            var rig = new Rig();
            AssertEq(GhostAction.WARN, rig.Step(W(distance: 5f, committed: true, band: true)), "E4");
            AssertEq("E4", rig.Layer.LastRuleId, "rule");
        }

        private static void E4b()
        {
            AssertEq(GhostAction.FOLLOW, new Rig().Step(W(distance: 5f, committed: false, band: true)), "not committed");
            AssertEq(GhostAction.FOLLOW, new Rig().Step(W(distance: 7f, committed: true, band: false)), "outside band");
        }

        private static void E4c()
        {
            var investigate = new Rig();
            AssertEq(GhostAction.INVESTIGATE, investigate.Step(W(distance: 5f, committed: true, band: true,
                presence: PresenceLevel.NEAR, confidence: ConfidenceLevel.HIGH, disturbance: true)), "R3 INVESTIGATE wins over E4");

            var retreat = new Rig();
            AssertEq(GhostAction.RETREAT, retreat.Step(W(distance: 5f, committed: true, band: true, threat: 0.8f)), "R2 RETREAT wins over E4");
        }

        // --------------------------------------------------------- dwell/reset

        private static void Dwell()
        {
            var rig = new Rig();
            rig.Step(W(distance: 5f, committed: true, band: true));
            for (int i = 0; i < 13; i++)
                AssertEq(GhostAction.WARN, rig.Step(W(distance: 9f, committed: true, band: false)), "held at " + (i + 1) * 100 + " ms");
            AssertEq("DWELL", rig.Layer.LastRuleId, "dwell rule");
            rig.Step(W(distance: 9f, committed: true, band: false));
            AssertEq(GhostAction.FOLLOW, rig.Step(W(distance: 9f, committed: true, band: false)), "released after 1.5 s");
        }

        private static void ResetTest()
        {
            var rig = new Rig();
            Telegraph(rig, 2000);
            rig.Layer.Reset();
            AssertTrue(rig.Layer.PreviousAction == null && rig.Layer.LastRuleId == string.Empty, "cleared");
            AssertEq(GhostAction.WARN, rig.Step(W(distance: 1.8f, committed: true, band: true)), "no attack straight after Reset: telegraph restarts");
        }

        // ---------------------------------------------------------- scenarios

        private static void T11()
        {
            // Presence toggles NEAR(LOW)/NONE every 200 ms for 30 s while the
            // player oscillates between 5.5 m and 8 m in the committed encounter.
            var rig = new Rig();
            GhostAction previous = GhostAction.FOLLOW;
            long warnSince = 0;
            for (int i = 0; i < 300; i++)
            {
                bool near = (i / 2) % 2 == 0;
                float distance = (i / 7) % 2 == 0 ? 5.5f : 8f;
                bool band = distance <= 6f;
                GhostAction a = rig.Step(W(distance: distance, committed: true, band: band,
                    presence: near ? PresenceLevel.NEAR : PresenceLevel.NONE, confidence: ConfidenceLevel.LOW));

                if (a == GhostAction.WARN && previous != GhostAction.WARN)
                    warnSince = rig.Now;
                if (a == GhostAction.FOLLOW && previous == GhostAction.WARN)
                    AssertTrue(rig.Now - warnSince >= 1500, "WARN lasted only " + (rig.Now - warnSince) + " ms at t=" + rig.Now);
                AssertTrue(a != GhostAction.ATTACK, "no attack at distance >= 5.5 m");
                previous = a;
            }
        }

        private static void T14()
        {
            var rig = new Rig();
            Telegraph(rig, 1500);
            AssertEq(GhostAction.ATTACK, rig.Step(W(distance: 1.8f, committed: true, band: true)), "attack starts");
            AssertEq(GhostAction.ATTACK, rig.Step(W(distance: 1.8f, committed: true, band: true, phase: GhostPhase.WINDUP)), "windup");

            // Lantern hit: the motion controller reports INTERRUPTED the same frame.
            long hitAt = rig.Now;
            AssertEq(GhostAction.RETREAT, rig.Step(W(distance: 1.8f, committed: true, band: true, phase: GhostPhase.INTERRUPTED), 0), "E0 on the hit frame");

            // 5.5 s interrupted (stagger + retreat), then a 6 s cooldown counting down.
            long t;
            for (t = 100; t <= 5500; t += 100)
                AssertTrue(rig.Step(W(distance: 1.5f, committed: true, band: true, phase: GhostPhase.INTERRUPTED)) != GhostAction.ATTACK, "interrupted");
            long firstAttack = -1;
            for (t = 0; t < 10000 && firstAttack < 0; t += 100)
            {
                long cooldown = Math.Max(0, 6000 - t);
                if (rig.Step(W(distance: 1.5f, committed: true, band: true, cooldown: cooldown)) == GhostAction.ATTACK)
                    firstAttack = rig.Now;
            }
            AssertTrue(firstAttack > 0, "an attack eventually follows");
            AssertTrue(firstAttack - hitAt >= 11500, "next attack " + (firstAttack - hitAt) + " ms after the hit (>= 11.5 s)");
        }

        private static void AttackFlow()
        {
            var rig = new Rig();
            Telegraph(rig, 1500);
            AssertEq(GhostAction.ATTACK, rig.Step(W(distance: 1.8f, committed: true, band: true)), "E3");
            foreach (var (phase, ms) in new[] { (GhostPhase.WINDUP, 1200), (GhostPhase.STRIKE, 300), (GhostPhase.RECOVER, 2000) })
                for (int t = 0; t < ms; t += 100)
                    AssertEq(GhostAction.ATTACK, rig.Step(W(distance: 1.8f, committed: true, band: true, phase: phase)), phase + " (E1)");

            // Miss: back to NORMAL with a 6 s cooldown. The player stays close.
            AssertEq(GhostAction.WARN, rig.Step(W(distance: 1.8f, committed: true, band: true, cooldown: 6000)), "E4 WARN, not an instant re-attack");
            AssertEq(GhostAction.WARN, rig.Step(W(distance: 1.8f, committed: true, band: true, cooldown: 0)), "cooldown over but telegraph only 0.1 s old");
            Telegraph(rig, 1400);
            AssertEq(GhostAction.ATTACK, rig.Step(W(distance: 1.8f, committed: true, band: true)), "fresh 1.5 s telegraph, then attack");
        }
    }
}
