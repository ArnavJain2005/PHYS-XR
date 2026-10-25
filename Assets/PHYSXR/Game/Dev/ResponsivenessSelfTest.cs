using System;
using System.Collections;
using System.Collections.Generic;
using PHYSXR.App;
using PHYSXR.Communication;
using PHYSXR.Core.Enums;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Level;
using PHYSXR.Ghost;
using UnityEngine;

namespace PHYSXR.Game.Dev
{
    // Development-only automated check that The Hollow visibly responds to
    // the physical station and to the player in the game: the real house,
    // decision pipeline, NavMesh ghost and lantern, driven through
    // ScriptedPlayerInput.
    //
    //   Q01-Q09  sensor response (needs "-physxr-condition SIMULATED"; the
    //            in-game station stands in for the ESP32 with the confidence
    //            the real station reports, LOW): NEAR -> WARN and the ghost
    //            advances to StalkDistance but never into the threat zone,
    //            a shake -> INVESTIGATE[X3] counted once, its search stays
    //            out of the threat zone, VERY_NEAR -> WARN[X2] with no
    //            FOLLOW gap after a held NEAR, a silent station -> neutral
    //            FOLLOW. Q20: that NEAR -> VERY_NEAR rise is visible (one
    //            escalation, tighter stalk, still outside the zone). Q21: the
    //            station comes back and the next NEAR reacts at once. Q23: a
    //            shake's search is a visible walk (>= 1 m). Q22: a second
    //            shake during it moves the search to another spot.
    //   Q10-Q19  in-game proximity (any condition; the station, if any, is
    //            silent): the threat zone -> WARN[E6], no attack while
    //            paused or out of reach, too close -> ATTACK[E6] after the
    //            telegraph, the lantern interrupts it, no immediate repeat,
    //            a landed E6 strike kills, and a restart is clean.
    //
    // Enabled only by "-physxr-responsiveness-selftest". Each check logs
    // "SELFTEST PASS|FAIL <id>"; a player build exits with the failure count.
    public class ResponsivenessSelfTest : MonoBehaviour
    {
        private static readonly Vector3 CorridorStart = new Vector3(-4f, 0.05f, -0.6f);

        private int passed;
        private int failed;
        private readonly List<string> failures = new List<string>();

        private IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            Debug.Log("SELFTEST BEGIN responsiveness");

            BlackthornGreybox house = null;
            yield return WaitForHouse(null, h => house = h);
            if (house == null) { Finish(); yield break; }

            var input = new ScriptedPlayerInput();
            house.Player.SetInput(input);
            GameFlowController flow = house.FlowController;
            var sensors = flow.Services?.Sensors as SensorService;
            SimulatedSensorStation station = sensors?.Simulator;
            if (station != null)
            {
                station.SetConfidence(ConfidenceLevel.LOW);
                station.SetPresence(PresenceLevel.NONE);
            }

            // Wake The Hollow the way a player does (the first-sighting
            // lunge, E5, is a feint), then wait for it to settle.
            house.Player.Respawn(CorridorStart, 0f);
            yield return WalkTo(house, input, new Vector3(-4f, 0f, 2.2f));
            for (float t = 0f; t < 5f && house.Ghost.Phase == GhostPhase.NORMAL; t += Time.deltaTime)
            {
                Aim(house, input, house.Ghost.ChestPosition);
                yield return null;
            }
            input.Stop();
            yield return WaitUntil(() => house.Ghost.Phase == GhostPhase.NORMAL, 6f);
            Check("Q00_GhostAwake", !house.Ghost.Dormant && house.Ghost.HasNavMesh && !flow.Flow.IsDead,
                "dormant=" + house.Ghost.Dormant + " dead=" + flow.Flow.IsDead);

            if (station != null)
                yield return SensorResponse(house, input, sensors, station);
            else
                Debug.Log("SELFTEST SKIP Q01-Q09, Q20-Q23 sensor response | needs -physxr-condition SIMULATED");

            yield return ProximityAttack(house, input);
            Finish();
        }

        // The ghost at the far end of the corridor, the player at its start
        // facing it, the attack cooldown spent.
        private IEnumerator Separate(BlackthornGreybox house, ScriptedPlayerInput input)
        {
            GameFlowController flow = house.FlowController;
            input.Stop();
            house.Player.Respawn(CorridorStart, 0f);
            house.Ghost.Manifest(house.Anchors.FirstSighting, false, false);
            yield return WaitUntil(() => house.Ghost.Phase == GhostPhase.NORMAL && house.Ghost.CooldownMs <= 0
                && flow.GhostRuntime.LastAction == GhostAction.FOLLOW, 10f);
            yield return Wait(1f);
        }

        private IEnumerator SensorResponse(BlackthornGreybox house, ScriptedPlayerInput input, SensorService sensors,
            SimulatedSensorStation station)
        {
            GameFlowController flow = house.FlowController;
            GhostMotionController ghost = house.Ghost;
            yield return Separate(house, input);
            yield return WaitUntil(() => sensors.Status == PhysicalSourceStatus.FRESH, 2f);
            yield return Wait(0.6f);

            // ToF NEAR: WARN (R4, passed through as INNER), and the ghost
            // advances from the far end of the corridor to stalk the player.
            ghost.Manifest(house.Anchors.FirstSighting, false, false);
            yield return null;
            yield return null;
            float before = ghost.StraightDistanceToPlayer;
            float closest = before;
            bool warned = false;
            bool attacked = false;
            string warnRule = string.Empty;
            station.SetPresence(PresenceLevel.NEAR);
            for (float t = 0f; t < 5f; t += Time.deltaTime)
            {
                Aim(house, input, ghost.ChestPosition);
                if (flow.GhostRuntime.LastAction == GhostAction.WARN && !warned)
                {
                    warned = true;
                    warnRule = flow.GhostRuntime.LastRuleId + "/" + InnerRule(flow);
                }
                if (ghost.Phase != GhostPhase.NORMAL || flow.GhostRuntime.LastAction == GhostAction.ATTACK)
                    attacked = true;
                closest = Mathf.Min(closest, ghost.StraightDistanceToPlayer);
                yield return null;
            }
            float after = ghost.StraightDistanceToPlayer;
            Check("Q01_ToFNearWarns", warned && warnRule == "INNER/R4" && ghost.Behaviour == GhostExecutionState.Warning,
                "rule=" + warnRule + " behaviour=" + ghost.Behaviour);
            Check("Q02_WarnAdvancesVisibly", before - after >= 1.5f && after <= GhostMotionController.StalkDistance + 0.6f && !flow.Flow.IsDead,
                "distance " + before.ToString("F1") + " -> " + after.ToString("F1") + " m");
            Check("Q03_SensorAloneNeverAttacks", !attacked && closest >= EncounterDirector.ThreatZoneEnter - 0.1f && !flow.Flow.IsDead,
                "attacked=" + attacked + " closest=" + closest.ToString("F2") + " m");

            // IMU shake near the station: INVESTIGATE[X3], once per shake.
            Vector3 at = ghost.transform.position;
            int accepted = 0;
            station.TriggerDisturbance(sensors.NowMs);
            bool investigated = false;
            string investigateRule = string.Empty;
            float nearest = ghost.StraightDistanceToPlayer;
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                nearest = Mathf.Min(nearest, ghost.StraightDistanceToPlayer);
                accepted += sensors.FrameAcceptedEvents.Count;
                if (!investigated && flow.GhostRuntime.LastAction == GhostAction.INVESTIGATE)
                {
                    investigated = true;
                    investigateRule = flow.GhostRuntime.LastRuleId;
                }
                yield return null;
            }
            float moved = Flat(ghost.transform.position - at);
            Check("Q04_ShakeInvestigates", investigated && investigateRule == "X3", "rule=" + investigateRule);
            Check("Q05_InvestigateExecuted", ghost.Behaviour == GhostExecutionState.Investigating && ghost.Phase == GhostPhase.NORMAL,
                "behaviour=" + ghost.Behaviour + " moved=" + moved.ToString("F2") + " m");
            Check("Q06_ShakeCountedOnce", accepted == 1, "accepted events=" + accepted);
            Check("Q23_SearchIsAWalk", moved >= 1f, "moved=" + moved.ToString("F2") + " m to "
                + ghost.InvestigateTarget.x.ToString("F1") + "," + ghost.InvestigateTarget.z.ToString("F1"));

            // A second shake while the search runs: same INVESTIGATE, but the
            // search visibly moves on to another spot, in the frame it lands.
            int researches = ghost.Researches;
            Vector3 firstSpot = ghost.InvestigateTarget;
            long firstEvent = flow.GhostRuntime.LastCausedEventId;
            station.TriggerDisturbance(sensors.NowMs);
            yield return WaitUntil(() => ghost.Researches > researches, 1.5f);
            Check("Q22_SecondShakeMovesSearch", ghost.Researches == researches + 1
                && flow.GhostRuntime.LastCausedEventId != firstEvent && flow.GhostRuntime.LastAction == GhostAction.INVESTIGATE
                && Flat(ghost.InvestigateTarget - firstSpot) > 0.5f && ghost.Behaviour == GhostExecutionState.Investigating,
                "researches +" + (ghost.Researches - researches) + " evt " + firstEvent + " -> " + flow.GhostRuntime.LastCausedEventId
                + " spot " + firstSpot.x.ToString("F1") + "," + firstSpot.z.ToString("F1")
                + " -> " + ghost.InvestigateTarget.x.ToString("F1") + "," + ghost.InvestigateTarget.z.ToString("F1"));

            for (float t = 0f; t < 4f && flow.World.DisturbancePulseActive; t += Time.deltaTime)
            {
                nearest = Mathf.Min(nearest, ghost.StraightDistanceToPlayer);
                yield return null;
            }
            yield return Wait(0.5f);
            Check("Q09_SearchStaysOutOfThreatZone", nearest >= EncounterDirector.ThreatZoneEnter - 0.1f && !flow.Flow.IsDead
                && flow.GhostRuntime.LastAction == GhostAction.WARN,
                "closest=" + nearest.ToString("F2") + " m, then " + flow.GhostRuntime.LastAction + " [" + flow.GhostRuntime.LastRuleId + "/" + InnerRule(flow) + "]");

            // ToF VERY_NEAR (single sensor, LOW) from a held NEAR: WARN[X2],
            // with no FOLLOW in between, and the running WARN visibly presses
            // in (escalation cue, tighter stalk) without entering the zone.
            // The search (Q22/Q23) walks the ghost away to search another
            // spot. Q20 is about escalating a held NEAR stalk, so the R4 WARN
            // first brings it back to StalkDistance (timed for the log).
            float backFrom = ghost.StraightDistanceToPlayer;
            float backTime = 0f;
            for (; backTime < 10f && ghost.StraightDistanceToPlayer > GhostMotionController.StalkDistance + 0.6f; backTime += Time.deltaTime)
            {
                Aim(house, input, ghost.ChestPosition);
                yield return null;
            }
            Debug.Log("SELFTEST INFO Q20 precondition | NEAR stalk regained from " + backFrom.ToString("F1") + " m in "
                + backTime.ToString("F1") + " s, " + flow.GhostRuntime.LastAction + " [" + flow.GhostRuntime.LastRuleId + "/" + InnerRule(flow) + "]");

            int escalations = ghost.WarnEscalations;
            bool gap = false;
            float closestUrgent = ghost.StraightDistanceToPlayer;
            station.SetPresence(PresenceLevel.VERY_NEAR);
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                Aim(house, input, ghost.ChestPosition);
                if (flow.GhostRuntime.LastAction != GhostAction.WARN)
                    gap = true;
                closestUrgent = Mathf.Min(closestUrgent, ghost.StraightDistanceToPlayer);
                yield return null;
            }
            Check("Q07_VeryNearWarnsX2", flow.GhostRuntime.LastAction == GhostAction.WARN && flow.GhostRuntime.LastRuleId == "X2"
                && ghost.Phase == GhostPhase.NORMAL && !gap,
                flow.GhostRuntime.LastAction + " [" + flow.GhostRuntime.LastRuleId + "] gap=" + gap);
            Check("Q20_VeryNearEscalatesVisibly", ghost.WarnEscalations == escalations + 1 && ghost.WarnUrgent
                && ghost.Behaviour == GhostExecutionState.Warning
                && ghost.StraightDistanceToPlayer <= GhostMotionController.StalkDistanceUrgent + 0.6f
                && closestUrgent >= EncounterDirector.ThreatZoneEnter - 0.1f && !flow.Flow.IsDead,
                "escalations +" + (ghost.WarnEscalations - escalations) + " urgent=" + ghost.WarnUrgent
                + " distance=" + ghost.StraightDistanceToPlayer.ToString("F2") + " closest=" + closestUrgent.ToString("F2") + " m");

            // The station falls silent: stale data is never proximity.
            station.SetSending(false);
            yield return WaitUntil(() => sensors.Status != PhysicalSourceStatus.FRESH, 2f);
            yield return WaitUntil(() => flow.GhostRuntime.LastAction == GhostAction.FOLLOW, 5f);
            Check("Q08_SilentStationNeutral", sensors.Status != PhysicalSourceStatus.FRESH && flow.GhostRuntime.LastAction == GhostAction.FOLLOW,
                "status=" + sensors.Status + " " + flow.GhostRuntime.LastAction + " [" + flow.GhostRuntime.LastRuleId + "]");

            // The station comes back (no restart): the next NEAR reacts again.
            yield return WaitUntil(() => sensors.Status == PhysicalSourceStatus.UNAVAILABLE, 4f);
            station.SetPresence(PresenceLevel.NEAR);
            station.SetSending(true);
            float resumedAt = Time.time;
            yield return WaitUntil(() => flow.GhostRuntime.LastAction == GhostAction.WARN, 3f);
            float reactedIn = Time.time - resumedAt;
            Check("Q21_ReconnectReactsAgain", sensors.Status == PhysicalSourceStatus.FRESH && flow.GhostRuntime.LastAction == GhostAction.WARN
                && InnerRule(flow) == "R4" && reactedIn <= 0.5f,
                "status=" + sensors.Status + " " + flow.GhostRuntime.LastAction + " [" + flow.GhostRuntime.LastRuleId + "/" + InnerRule(flow)
                + "] after " + reactedIn.ToString("F2") + " s");

            // Leave the station silent again for the in-game proximity checks.
            station.SetPresence(PresenceLevel.NONE);
            station.SetSending(false);
            yield return WaitUntil(() => sensors.Status != PhysicalSourceStatus.FRESH && flow.GhostRuntime.LastAction == GhostAction.FOLLOW, 6f);
            input.Stop();
        }

        private IEnumerator ProximityAttack(BlackthornGreybox house, ScriptedPlayerInput input)
        {
            GameFlowController flow = house.FlowController;
            GhostMotionController ghost = house.Ghost;
            yield return Separate(house, input);
            Check("Q10_OutsideZoneNoProximity", flow.GhostRuntime.LastRuleId != "E6" && !flow.LastPlayerState.inThreatZone,
                flow.GhostRuntime.LastAction + " [" + flow.GhostRuntime.LastRuleId + "] distance=" + ghost.StraightDistanceToPlayer.ToString("F1"));

            // Into the threat zone, out of reach: the telegraph only.
            yield return Approach(house, input, 3.0f, 10f);
            yield return WaitUntil(() => flow.GhostRuntime.LastAction == GhostAction.WARN && flow.GhostRuntime.LastRuleId == "E6", 1.5f);
            float warnSince = Time.time;
            Check("Q11_ThreatZoneWarnsE6", flow.GhostRuntime.LastAction == GhostAction.WARN && flow.GhostRuntime.LastRuleId == "E6",
                flow.GhostRuntime.LastAction + " [" + flow.GhostRuntime.LastRuleId + "] distance=" + ghost.StraightDistanceToPlayer.ToString("F1"));

            flow.Pause();
            bool paused = flow.Flow.RunState == RunState.Paused;
            bool attackedWhilePaused = false;
            for (float until = Time.realtimeSinceStartup + 2f; Time.realtimeSinceStartup < until;)
            {
                if (ghost.Phase != GhostPhase.NORMAL || flow.GhostRuntime.LastAction == GhostAction.ATTACK)
                    attackedWhilePaused = true;
                yield return null;
            }
            flow.Resume();
            Check("Q12_PausedNoAttack", paused && !attackedWhilePaused, "paused=" + paused + " attacked=" + attackedWhilePaused);

            bool attackedOutOfReach = false;
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                Aim(house, input, ghost.ChestPosition);
                if (ghost.Phase != GhostPhase.NORMAL) attackedOutOfReach = true;
                yield return null;
            }
            Check("Q13_OutOfReachTelegraphOnly", !attackedOutOfReach && flow.GhostRuntime.LastAction == GhostAction.WARN,
                "attacked=" + attackedOutOfReach + " distance=" + ghost.StraightDistanceToPlayer.ToString("F1"));

            // Too close: the attack, after the telegraph.
            bool windup = false;
            float warned = 0f;
            for (float t = 0f; t < 6f && !windup; t += Time.deltaTime)
            {
                Step(house, input, 1.6f);
                if (ghost.Phase == GhostPhase.WINDUP)
                {
                    windup = true;
                    warned = Time.time - warnSince;
                }
                yield return null;
            }
            input.Stop();
            Check("Q14_TooCloseAttacksE6", windup && flow.AttackStartRule == "E6" && warned >= 1.4f,
                "windup=" + windup + " rule=" + flow.AttackStartRule + " warned=" + warned.ToString("F1") + "s");

            // Counterplay: the lantern during the windup.
            input.PressDefendNextFrame();
            bool interrupted = false;
            for (int i = 0; i < 6 && !interrupted; i++)
            {
                Aim(house, input, ghost.ChestPosition);
                yield return null;
                interrupted = ghost.Phase == GhostPhase.INTERRUPTED;
            }
            yield return null;
            Check("Q15_LanternInterruptsE6", interrupted && house.Lantern.Hits == 1 && !flow.Flow.IsDead,
                "phase=" + ghost.Phase + " result=" + house.Lantern.LastResult + " dead=" + flow.Flow.IsDead);
            Check("Q16_E0Retreat", flow.GhostRuntime.LastAction == GhostAction.RETREAT && flow.GhostRuntime.LastRuleId == "E0",
                flow.GhostRuntime.LastAction + " [" + flow.GhostRuntime.LastRuleId + "]");

            int windupFrames = 0;
            for (float t = 0f; t < 5f; t += Time.deltaTime)
            {
                Aim(house, input, ghost.ChestPosition);
                if (ghost.Phase == GhostPhase.WINDUP) windupFrames++;
                yield return null;
            }
            Check("Q17_NoImmediateRepeat", windupFrames == 0 && !flow.Flow.IsDead, "windup frames=" + windupFrames);

            // Stay too close without the lantern: the strike lands and kills.
            yield return WaitUntil(() => ghost.Phase == GhostPhase.NORMAL, 8f);
            yield return Separate(house, input);
            int windups = 0;
            bool struck = false;
            GhostPhase last = ghost.Phase;
            for (float t = 0f; t < 25f && !flow.Flow.IsDead; t += Time.deltaTime)
            {
                Step(house, input, 1.6f);
                if (ghost.Phase == GhostPhase.WINDUP && last != GhostPhase.WINDUP) windups++;
                if (ghost.Phase == GhostPhase.STRIKE) struck = true;
                last = ghost.Phase;
                yield return null;
            }
            input.Stop();
            Check("Q18_E6StrikeKills", flow.Flow.IsDead && struck && windups == 1 && flow.AttackStartRule == "E6"
                && flow.Session.Deaths == 1 && flow.GhostRuntime.IsHalted,
                "dead=" + flow.Flow.IsDead + " windups=" + windups + " rule=" + flow.AttackStartRule + " deaths=" + flow.Session.Deaths);

            BlackthornGreybox old = house;
            flow.RestartFromCheckpoint();
            yield return WaitForHouse(old, h => house = h);
            if (house == null) yield break;
            flow = house.FlowController;
            house.Player.SetInput(new ScriptedPlayerInput());
            bool attackedAfter = false;
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                if (flow.GhostRuntime.LastAction == GhostAction.ATTACK || house.Ghost.Phase != GhostPhase.NORMAL) attackedAfter = true;
                yield return null;
            }
            Check("Q19_RestartClean", !flow.Flow.IsDead && !attackedAfter && flow.Flow.RunState == RunState.Playing
                && flow.AttackStartRule == string.Empty,
                "dead=" + flow.Flow.IsDead + " attacked=" + attackedAfter + " state=" + flow.Flow.RunState);
        }

        private static string InnerRule(GameFlowController flow)
        {
            return (flow.GhostRuntime.Pipeline?.DecisionProvider as PHYSXR.Decision.EncounterDecisionLayer)?.Inner?.LastRuleId ?? string.Empty;
        }

        // Walks toward The Hollow until within the distance, then faces it.
        private IEnumerator Approach(BlackthornGreybox house, ScriptedPlayerInput input, float distance, float timeout)
        {
            for (float t = 0f; t < timeout && house.Ghost.StraightDistanceToPlayer > distance; t += Time.deltaTime)
            {
                Step(house, input, distance);
                yield return null;
            }
            input.Stop();
        }

        private static void Step(BlackthornGreybox house, ScriptedPlayerInput input, float distance)
        {
            Aim(house, input, house.Ghost.ChestPosition);
            if (house.Ghost.StraightDistanceToPlayer > distance)
                input.Move = new Vector2(0f, 1f);
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }

        private static void Aim(BlackthornGreybox house, ScriptedPlayerInput input, Vector3 point)
        {
            Transform body = house.Player.transform;
            Vector3 to = point - house.Player.Head.position;
            float yawError = Mathf.DeltaAngle(body.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
            float desiredPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            float pitchError = house.Player.Motor.Pitch - desiredPitch;
            input.Move = Vector2.zero;
            input.LookDegrees = new Vector2(Mathf.Clamp(yawError, -25f, 25f), Mathf.Clamp(pitchError, -25f, 25f));
        }

        private IEnumerator WalkTo(BlackthornGreybox house, ScriptedPlayerInput input, Vector3 target)
        {
            Transform body = house.Player.transform;
            float deadline = Time.time + 12f;
            while (Time.time < deadline)
            {
                Vector3 to = target - body.position;
                to.y = 0f;
                if (to.magnitude < 0.3f)
                    break;
                float yawError = Mathf.DeltaAngle(body.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                input.LookDegrees = new Vector2(Mathf.Clamp(yawError, -10f, 10f), 0f);
                input.Move = new Vector2(0f, Mathf.Abs(yawError) < 25f ? 1f : 0f);
                yield return null;
            }
            input.Stop();
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeout)
        {
            float until = Time.time + timeout;
            while (!condition() && Time.time < until)
                yield return null;
        }

        private IEnumerator WaitForHouse(BlackthornGreybox previous, Action<BlackthornGreybox> found)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                BlackthornGreybox house = FindAnyObjectByType<BlackthornGreybox>();
                if (house != null && house != previous && house.FlowController.RunStarted)
                {
                    yield return null;
                    yield return null;
                    found(house);
                    yield break;
                }
                yield return null;
            }
            Check("WaitForHouse", false, "game scene did not load within 30 s");
            found(null);
        }

        private void Check(string id, bool condition, string detail)
        {
            if (condition)
            {
                passed++;
                Debug.Log("SELFTEST PASS " + id + " | " + detail);
            }
            else
            {
                failed++;
                failures.Add(id);
                Debug.LogError("SELFTEST FAIL " + id + " | " + detail);
            }
        }

        private void Finish()
        {
            Debug.Log("SELFTEST END passed=" + passed + " failed=" + failed + (failures.Count > 0 ? " [" + string.Join(", ", failures) + "]" : string.Empty));
#if !UNITY_EDITOR
            Application.Quit(failed);
#endif
            enabled = false;
        }
    }
}
