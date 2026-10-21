using System;
using System.Collections;
using System.Collections.Generic;
using PHYSXR.Core.Enums;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Level;
using PHYSXR.Ghost;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PHYSXR.Game.Dev
{
    // Development-only automated check of the encounter inside Unity: the real
    // house, the real decision pipeline, the real NavMesh ghost and the real
    // lantern, driven through ScriptedPlayerInput (Technical Spec tests T14,
    // T15, T17, T18, plus the surge RETREAT and the beat WARN).
    //
    // Enabled only by "-physxr-selftest" (or PHYS-XR > Run Encounter
    // Self-Test). Each check logs "SELFTEST PASS|FAIL <id>"; a player build
    // exits with the failure count. It shortens the walk by teleporting the
    // player between the beats, so it is not a timing test of the route (the
    // M2 route self-test is).
    public class EncounterSelfTest : MonoBehaviour
    {
        public const string EditorSessionKey = "PHYSXR.EncounterSelfTest";

        private int passed;
        private int failed;
        private readonly List<string> failures = new List<string>();

        private IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            Debug.Log("SELFTEST BEGIN encounter");

            BlackthornGreybox house = null;
            yield return WaitForHouse(null, h => house = h);
            if (house == null) { Finish(); yield break; }

            var input = new ScriptedPlayerInput();
            house.Player.SetInput(input);
            GameFlowController flow = house.FlowController;

            Check("S01_StartState", house.Ghost.Dormant && !flow.Flow.EncounterCommitted && flow.World != null
                && flow.GhostRuntime.IsInitialized && house.HasNavMesh,
                "dormant=" + house.Ghost.Dormant + " nav=" + house.HasNavMesh);
            CheckSingletons("S02_SingleCameraListenerEventSystem");

            // First sighting: walk into the corridor trigger.
            house.Player.Respawn(new Vector3(-4f, 0.05f, -0.6f), 0f);
            yield return WalkTo(house, input, new Vector3(-4f, 0f, 2.2f));
            yield return Wait(0.3f);
            Check("S03_FirstSightingManifests", !house.Ghost.Dormant && house.Ghost.HasNavMesh,
                "dormant=" + house.Ghost.Dormant + " onNav=" + house.Ghost.HasNavMesh);
            Check("S04_BeatGivesWarn", flow.GhostRuntime.LastAction == GhostAction.WARN && flow.GhostRuntime.LastRuleId == "W",
                flow.GhostRuntime.LastAction + " [" + flow.GhostRuntime.LastRuleId + "] " + flow.GhostRuntime.LastReason);

            // Objective, then the surge.
            InteractionResult pickup = house.Fuse.Interact();
            InteractionResult install = house.FuseBox.Interact();
            Check("S05_FuseInstalled", pickup.Accepted && install.Accepted, pickup + " / " + install);
            bool sawRetreat = false;
            for (float t = 0f; t < 4f && !sawRetreat; t += Time.deltaTime)
            {
                if (flow.GhostRuntime.LastAction == GhostAction.RETREAT && flow.LastVirtualState.threatLevel >= 0.79f)
                    sawRetreat = true;
                yield return null;
            }
            yield return WaitUntil(() => flow.Flow.PowerRestored, 5f);
            Check("S06_SurgeRetreat", sawRetreat, "R2 RETREAT seen during the surge");
            Check("S07_CheckpointSaved", flow.Session.HasCheckpoint && flow.Flow.ExitUnlocked, "checkpoint=" + flow.Session.HasCheckpoint);

            // Commit the encounter through the real dining-room trigger.
            house.Player.Respawn(new Vector3(4f, 0.05f, 9.3f), 180f);
            yield return WalkTo(house, input, new Vector3(4f, 0f, 6.6f));
            yield return Wait(0.6f);
            Check("S08_EncounterCommitted", flow.Flow.EncounterCommitted && !house.Ghost.Dormant,
                "committed=" + flow.Flow.EncounterCommitted);

            // T15: a pulse that misses (aimed away from the ghost) starts the
            // 3 s cooldown and interrupts nothing.
            Turn(house, input, 180f);
            yield return null;
            input.Stop();
            input.PressDefendNextFrame();
            yield return null;
            yield return null;
            bool missed = house.Lantern.LastResult != "hit" && !house.Lantern.Ready && house.Ghost.Phase != GhostPhase.INTERRUPTED;
            yield return Wait(2.6f);
            bool stillCooling = !house.Lantern.Ready;
            yield return Wait(0.6f);
            Check("T15_MissCooldown3s", missed && stillCooling && house.Lantern.Ready && house.Lantern.Hits == 0,
                "result=" + house.Lantern.LastResult);

            // Stand in the entry hall facing the ghost; wait for the telegraph and windup.
            house.Player.Respawn(new Vector3(1.6f, 0.05f, -5.6f), 0f);
            float warnSince = -1f;
            float warnHeld = 0f;
            bool windup = false;
            for (float t = 0f; t < 25f && !windup; t += Time.deltaTime)
            {
                Aim(house, input, house.Ghost.ChestPosition);
                if (flow.GhostRuntime.LastAction == GhostAction.WARN) { if (warnSince < 0f) warnSince = Time.time; }
                else if (flow.GhostRuntime.LastAction != GhostAction.ATTACK) warnSince = -1f;
                if (house.Ghost.Phase == GhostPhase.WINDUP)
                {
                    windup = true;
                    warnHeld = warnSince > 0f ? Time.time - warnSince : 0f;
                }
                yield return null;
            }
            Check("S09_AttackTelegraphed", windup && warnHeld >= 1.4f, "windup=" + windup + " warned=" + warnHeld.ToString("F2") + "s");

            // T14: lantern during the windup.
            input.PressDefendNextFrame();
            float hitTime = -1f;
            for (int i = 0; i < 6 && hitTime < 0f; i++)
            {
                Aim(house, input, house.Ghost.ChestPosition);
                yield return null;
                if (house.Ghost.Phase == GhostPhase.INTERRUPTED)
                    hitTime = Time.time;
            }
            yield return null;
            Check("T14a_LanternInterruptsWindup", hitTime > 0f && house.Lantern.Hits == 1, "phase=" + house.Ghost.Phase + " result=" + house.Lantern.LastResult);
            Check("T14b_E0Retreat", flow.GhostRuntime.LastAction == GhostAction.RETREAT && flow.GhostRuntime.LastRuleId == "E0",
                flow.GhostRuntime.LastAction + " [" + flow.GhostRuntime.LastRuleId + "]");

            bool struck = false;

            // Next attack no sooner than 11.5 s after the hit.
            float nextWindup = -1f;
            for (float t = 0f; t < 30f && nextWindup < 0f; t += Time.deltaTime)
            {
                Aim(house, input, house.Ghost.ChestPosition);
                if (house.Ghost.Phase == GhostPhase.STRIKE) struck = true;
                if (house.Ghost.Phase == GhostPhase.WINDUP) nextWindup = Time.time;
                if (flow.Flow.IsDead) break;
                yield return null;
            }
            Check("T14c_NextAttackAfter11_5s", nextWindup > 0f && nextWindup - hitTime >= 11.4f && !struck,
                "gap=" + (nextWindup - hitTime).ToString("F2") + "s");

            // T17: let the strike land.
            yield return WaitUntil(() => flow.Flow.IsDead, 12f);
            Check("T17a_StrikeKills", flow.Flow.IsDead && flow.Session.Deaths == 1 && flow.GhostRuntime.IsHalted,
                "dead=" + flow.Flow.IsDead + " halted=" + flow.GhostRuntime.IsHalted);
            yield return Wait(1.0f);

            BlackthornGreybox old = house;
            flow.RestartFromCheckpoint();
            yield return WaitForHouse(old, h => house = h);
            if (house == null) { Finish(); yield break; }
            flow = house.FlowController;
            Vector3 p = house.Player.transform.position;
            bool graceAtStart = flow.LastVirtualState.attackGraceActive || flow.GameClockMs < 200;
            Check("T17b_CheckpointState", flow.StartedFromCheckpoint && flow.Session.Attempt == 2 && house.Power.IsPowered
                && flow.Flow.ExitUnlocked && !flow.Flow.EncounterCommitted && house.Lantern.Ready && !house.Ghost.Dormant
                && (p - GameFlowController.CheckpointPosition).sqrMagnitude < 0.25f,
                "attempt=" + flow.Session.Attempt + " powered=" + house.Power.IsPowered + " pos=" + p);
            bool attackedEarly = false;
            house.Player.SetInput(input = new ScriptedPlayerInput());
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                if (flow.GhostRuntime.LastAction == GhostAction.ATTACK) attackedEarly = true;
                yield return null;
            }
            Check("T17c_GraceAndNoEarlyAttack", graceAtStart && !attackedEarly, "grace=" + graceAtStart);

            // T18: ten restarts in a row - no duplicates, no material/mesh growth.
            long memoryBefore = GC.GetTotalMemory(true);
            int materialsBefore = Resources.FindObjectsOfTypeAll<Material>().Length;
            for (int i = 0; i < 10; i++)
            {
                old = house;
                house.FlowController.RestartFromCheckpoint();
                yield return WaitForHouse(old, h => house = h);
                if (house == null) { Finish(); yield break; }
                yield return Wait(0.3f);
            }
            yield return Wait(0.5f);
            long memoryAfter = GC.GetTotalMemory(true);
            int materialsAfter = Resources.FindObjectsOfTypeAll<Material>().Length;
            CheckSingletons("T18a_NoDuplicatesAfter10Restarts");
            Check("T18b_NoMaterialLeak", materialsAfter <= materialsBefore + 2, "materials " + materialsBefore + " -> " + materialsAfter);
            Check("T18c_ManagedMemoryFlat", memoryAfter <= memoryBefore * 1.15 + 2 * 1024 * 1024,
                "mono " + (memoryBefore / 1048576.0).ToString("F1") + " -> " + (memoryAfter / 1048576.0).ToString("F1") + " MB");
            Check("T18d_AttemptCount", house.FlowController.Session.Attempt == 12, "attempt=" + house.FlowController.Session.Attempt);

            // T20: a full restart is clean.
            old = house;
            house.FlowController.RestartRun();
            yield return WaitForHouse(old, h => house = h);
            if (house != null)
                Check("T20_FullRestartClean", house.FlowController.Session.Attempt == 1 && !house.FlowController.Session.HasCheckpoint
                    && !house.Power.IsPowered && house.Ghost.Dormant && house.FlowController.Flow.Stage == ObjectiveStage.FindFuse,
                    "attempt=" + house.FlowController.Session.Attempt);

            Finish();
        }

        private void CheckSingletons(string id)
        {
            int cameras = 0;
            foreach (Camera c in FindObjectsByType<Camera>())
                if (c.isActiveAndEnabled) cameras++;
            int listeners = 0;
            foreach (AudioListener l in FindObjectsByType<AudioListener>())
                if (l.isActiveAndEnabled) listeners++;
            int eventSystems = FindObjectsByType<EventSystem>().Length;
            int flows = FindObjectsByType<GameFlowController>().Length;
            int ghosts = FindObjectsByType<GhostMotionController>().Length;
            Check(id, cameras == 1 && listeners == 1 && eventSystems == 1 && flows == 1 && ghosts == 1,
                "cameras=" + cameras + " listeners=" + listeners + " eventSystems=" + eventSystems + " flows=" + flows + " ghosts=" + ghosts);
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

        private static void Turn(BlackthornGreybox house, ScriptedPlayerInput input, float degrees)
        {
            input.LookDegrees = new Vector2(degrees, 0f);
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
