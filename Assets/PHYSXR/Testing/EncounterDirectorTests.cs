using System;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Decision;
using PHYSXR.Game.Flow;

namespace PHYSXR.Testing
{
    // DIR suite: the game-side WorldState inputs (EncounterDirector), and that
    // fed through the real decision chain they produce the beats the spec
    // describes (R7 WARN near the ghost after the fuse, R2 RETREAT for the
    // surge, the beat WARN, no attack during the restart grace).
    public static class EncounterDirectorTests
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

            Run("DIR-01_Bands_HaveHysteresis", DIR01);
            Run("DIR-02_Threat_BaselineNearAfterFuseAndSurge", DIR02);
            Run("DIR-03_Beat_Lasts4s_SetsEventAndWarningBeat", DIR03);
            Run("DIR-04_RestartGrace_3s", DIR04);
            Run("DIR-05_Chain_NearAfterFuse_R7Warn_SurgeRetreat", DIR05);
            Run("DIR-06_Chain_GraceBlocksAttackThenAllows", DIR06);

            Console.WriteLine("EncounterDirectorTests: " + passed + " passed, " + failed + " failed");
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

        private static void DIR01()
        {
            var d = new EncounterDirector();
            var p = new PlayerState();
            var v = new VirtualState();
            d.Update(0, 4.0f, 0f, false, false, p, v);
            AssertTrue(!p.inThreatZone && p.inWarnBand, "4.0 m: band yes, zone no");
            d.Update(0, 3.5f, 0f, false, false, p, v);
            AssertTrue(p.inThreatZone, "3.5 m enters the zone");
            d.Update(0, 4.4f, 0f, false, false, p, v);
            AssertTrue(p.inThreatZone, "4.4 m stays in (exit > 4.5)");
            d.Update(0, 4.6f, 0f, false, false, p, v);
            AssertTrue(!p.inThreatZone, "4.6 m leaves");
            d.Update(0, 7.4f, 0f, false, false, p, v);
            AssertTrue(p.inWarnBand, "7.4 m still in band");
            d.Update(0, 7.6f, 0.3f, false, false, p, v);
            AssertTrue(!p.inWarnBand && p.movement, "7.6 m out; moving");
        }

        private static void DIR02()
        {
            var d = new EncounterDirector();
            var p = new PlayerState();
            var v = new VirtualState();
            d.Update(0, 5f, 0f, false, false, p, v);
            AssertEq(0.2f, v.threatLevel, "no fuse: baseline even when close");
            d.Update(0, 5f, 0f, true, false, p, v);
            AssertEq(0.5f, v.threatLevel, "fuse + within 6 m");
            d.Update(0, 7f, 0f, true, false, p, v);
            AssertEq(0.5f, v.threatLevel, "7 m: hysteresis keeps 0.5");
            d.Update(0, 8f, 0f, true, false, p, v);
            AssertEq(0.2f, v.threatLevel, "beyond 7.5 m");
            d.BeginSurge(1000);
            d.Update(3999, 20f, 0f, true, false, p, v);
            AssertEq(0.8f, v.threatLevel, "surge for 3 s");
            d.Update(4000, 20f, 0f, true, false, p, v);
            AssertEq(0.2f, v.threatLevel, "surge over");
        }

        private static void DIR03()
        {
            var d = new EncounterDirector();
            var p = new PlayerState();
            var v = new VirtualState();
            d.BeginBeat(500, "FIRST_SIGHTING");
            d.Update(4499, 10f, 0f, false, false, p, v);
            AssertTrue(v.eventActive && v.mandatoryWarningBeat, "beat active at 3.999 s");
            d.Update(4500, 10f, 0f, false, false, p, v);
            AssertTrue(!v.eventActive && !v.mandatoryWarningBeat, "beat over at 4 s");
            AssertEq("FIRST_SIGHTING", d.LastBeat, "beat id");
        }

        private static void DIR04()
        {
            var d = new EncounterDirector();
            var p = new PlayerState();
            var v = new VirtualState();
            d.BeginRestartGrace(0);
            d.Update(2999, 1f, 0f, true, true, p, v);
            AssertTrue(v.attackGraceActive && v.encounterCommitted, "grace and commit passed through");
            d.Update(3000, 1f, 0f, true, true, p, v);
            AssertTrue(!v.attackGraceActive, "grace over");
        }

        private static WorldState Snapshot(PlayerState p, VirtualState v, long cooldown = 0)
        {
            return new WorldState
            {
                physical = new PhysicalState { presence = PresenceLevel.NONE, confidence = ConfidenceLevel.LOW },
                player = new PlayerState { distanceToGhost = p.distanceToGhost, inThreatZone = p.inThreatZone, inWarnBand = p.inWarnBand, movement = p.movement },
                virtualWorld = new VirtualState
                {
                    threatLevel = v.threatLevel, eventActive = v.eventActive, mandatoryWarningBeat = v.mandatoryWarningBeat,
                    encounterCommitted = v.encounterCommitted, attackGraceActive = v.attackGraceActive
                },
                ghost = new GhostState { attackCooldownMs = cooldown }
            };
        }

        private static void DIR05()
        {
            long now = 0;
            var layer = new EncounterDecisionLayer(new RuleBasedDecisionProvider(decisionClock: () => now), () => now);
            var d = new EncounterDirector();
            var p = new PlayerState();
            var v = new VirtualState();

            d.Update(now, 5f, 1f, true, false, p, v);
            AssertEq(GhostAction.WARN, layer.Decide(Snapshot(p, v)), "fuse carried, ghost at 5 m: R7 WARN");
            AssertTrue(layer.Inner.LastDecisionReason.Contains("elevated virtual threat"), layer.Inner.LastDecisionReason);

            d.BeginSurge(now);
            now += 100;
            d.Update(now, 5f, 1f, true, false, p, v);
            AssertEq(GhostAction.RETREAT, layer.Decide(Snapshot(p, v)), "surge: R2 RETREAT");
        }

        private static void DIR06()
        {
            long now = 0;
            var layer = new EncounterDecisionLayer(new RuleBasedDecisionProvider(decisionClock: () => now), () => now);
            var d = new EncounterDirector();
            var p = new PlayerState();
            var v = new VirtualState();
            d.BeginRestartGrace(0);

            bool attackedDuringGrace = false;
            long firstAttack = -1;
            for (int i = 0; i < 80; i++)
            {
                now += 100;
                d.Update(now, 1.8f, 0f, true, true, p, v);
                GhostAction a = layer.Decide(Snapshot(p, v));
                if (a == GhostAction.ATTACK)
                {
                    if (now < 3000) attackedDuringGrace = true;
                    if (firstAttack < 0) firstAttack = now;
                }
            }
            AssertTrue(!attackedDuringGrace, "no attack in the first 3 s after a restart");
            AssertTrue(firstAttack >= 3000, "attack possible after the grace (first at " + firstAttack + " ms)");
        }
    }
}
