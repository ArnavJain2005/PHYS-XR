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
    // T15, T17, T18, plus the surge RETREAT, the first-sighting lunge (E5),
    // the readable modal, sprint and jump, and the ghost's solid body).
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
        private bool sawE5;
        private bool sawWindup;
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

            Check("S01_StartState", house.Ghost.Dormant && !house.Ghost.BodySolid && !flow.Flow.EncounterCommitted && flow.World != null
                && flow.GhostRuntime.IsInitialized && house.HasNavMesh,
                "dormant=" + house.Ghost.Dormant + " solid=" + house.Ghost.BodySolid + " nav=" + house.HasNavMesh);
            CheckSingletons("S02_SingleCameraListenerEventSystem");

            yield return ReadableModal(house, input);
            yield return SprintAndJump(house, input);

            // First sighting: walk into the corridor trigger. The Hollow
            // appears at the far end of the corridor, in view.
            // Seeing it starts a real attack through the decision layer (E5),
            // possibly while still walking, so watch from the start.
            // Before the encounter is committed a landed strike is a feint.
            int feints = 0;
            Action onFeint = () => feints++;
            flow.StrikeFeinted += onFeint;
            sawE5 = false;
            sawWindup = false;
            Coroutine watch = StartCoroutine(WatchSighting(house));

            house.Player.Respawn(new Vector3(-4f, 0.05f, -0.6f), 0f);
            yield return WalkTo(house, input, new Vector3(-4f, 0f, 2.2f));
            Check("S03_FirstSightingManifests", !house.Ghost.Dormant && house.Ghost.HasNavMesh,
                "dormant=" + house.Ghost.Dormant + " onNav=" + house.Ghost.HasNavMesh);

            for (float t = 0f; t < 5f && !sawWindup; t += Time.deltaTime)
            {
                Aim(house, input, house.Ghost.ChestPosition);
                yield return null;
            }
            StopCoroutine(watch);
            Check("S04a_SightingDetected", flow.SightingSeen && house.Ghost.BodySolid, "seen=" + flow.SightingSeen + " solid=" + house.Ghost.BodySolid);
            Check("S04b_SightingAttack_E5Windup", sawE5 && sawWindup,
                "E5=" + sawE5 + " windup=" + sawWindup + " last=" + flow.GhostRuntime.LastAction + " [" + flow.GhostRuntime.LastRuleId + "]");
            yield return WaitUntil(() => house.Ghost.Phase == GhostPhase.NORMAL, 6f);
            Check("S04c_SightingAttackNotLethal", house.Ghost.Phase == GhostPhase.NORMAL && !flow.Flow.IsDead && flow.Session.Deaths == 0,
                "phase=" + house.Ghost.Phase + " dead=" + flow.Flow.IsDead + " feints=" + feints);
            int repeats = 0;
            for (float t = 0f; t < 4f; t += Time.deltaTime)
            {
                Aim(house, input, house.Ghost.ChestPosition);
                if (house.Ghost.Phase == GhostPhase.WINDUP) repeats++;
                yield return null;
            }
            flow.StrikeFeinted -= onFeint;
            Check("S04d_SightingAttackOnce", repeats == 0 && !flow.Director.FirstSightingAttackPending(flow.GameClockMs) && flow.Director.FirstSightingUsed,
                "windup frames after=" + repeats);
            input.Stop();

            // Objective, then the surge. Stand at the fuse box, as a player
            // would: R2 RETREAT needs the player outside the threat zone, and
            // the lunge left The Hollow beside them in the corridor.
            house.Player.Respawn(new Vector3(6.7f, 0.05f, 12f), 90f);
            yield return Wait(0.3f);
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
            Check("S10_DeathClosesModalAndBlocksInput", !flow.ReadableOpen && !flow.Flow.AcceptsGameplayInput, "modal=" + flow.ReadableOpen);

            // The ghost is halted where it struck, still visible and solid:
            // walk the body straight at it and it must stop at its surface.
            yield return GhostBlocks(house);

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
                    && !house.Power.IsPowered && house.Ghost.Dormant && !house.Ghost.BodySolid && !house.FlowController.SightingSeen
                    && house.FlowController.Flow.Stage == ObjectiveStage.FindFuse,
                    "attempt=" + house.FlowController.Session.Attempt);

            Finish();
        }

        // Records, every frame, whether the first-sighting attack was decided
        // by E5 and whether the ghost reached WINDUP.
        private IEnumerator WatchSighting(BlackthornGreybox house)
        {
            GameFlowController flow = house.FlowController;
            while (true)
            {
                if (flow.GhostRuntime.LastAction == GhostAction.ATTACK && flow.GhostRuntime.LastRuleId == "E5") sawE5 = true;
                if (house.Ghost.Phase == GhostPhase.WINDUP) sawWindup = true;
                yield return null;
            }
        }

        // Bug fix 2: a readable is a modal. E opens it through the real
        // interaction ray, the same press does not also close it, look/move/
        // sprint/jump/lantern are blocked while it is open, E closes it, and
        // control returns.
        private IEnumerator ReadableModal(BlackthornGreybox house, ScriptedPlayerInput input)
        {
            GameFlowController flow = house.FlowController;
            ReadableNote radio = null;
            foreach (ReadableNote n in FindObjectsByType<ReadableNote>())
                if (n.name == "Radio") radio = n;
            if (radio == null)
            {
                Check("M01_RadioFound", false, "no Radio readable");
                yield break;
            }

            house.Player.Respawn(new Vector3(1.4f, 0.05f, 3.5f), 270f);
            for (int i = 0; i < 40; i++)
            {
                Aim(house, input, radio.transform.position);
                yield return null;
            }
            input.Stop();
            yield return null;

            input.PressInteractNextFrame();
            yield return null;
            yield return null;
            yield return null;
            Check("M01_EOpensReadable", flow.ReadableOpen && flow.OpenNote == radio,
                "open=" + flow.ReadableOpen + " target=" + (house.Player.Interactor.CurrentTarget != null ? house.Player.Interactor.CurrentTarget.name : "none"));

            Vector3 at = house.Player.transform.position;
            float yaw = house.Player.transform.eulerAngles.y;
            int jumps = house.Player.Motor.Jumps;
            input.Move = new Vector2(0f, 1f);
            input.LookDegrees = new Vector2(8f, 3f);
            input.SprintHeld = true;
            input.PressJumpNextFrame();
            input.PressDefendNextFrame();
            for (int i = 0; i < 20; i++)
                yield return null;
            Vector3 moved = house.Player.transform.position - at;
            moved.y = 0f;
            Check("M02_ModalBlocksControls", flow.ReadableOpen && moved.magnitude < 0.02f
                && Mathf.Abs(Mathf.DeltaAngle(yaw, house.Player.transform.eulerAngles.y)) < 0.01f
                && house.Player.Motor.Jumps == jumps && house.Lantern.Uses == 0,
                "moved=" + moved.magnitude.ToString("F3") + " jumps=" + (house.Player.Motor.Jumps - jumps) + " lantern=" + house.Lantern.Uses);
            input.Stop();

            input.PressInteractNextFrame();
            yield return null;
            yield return null;
            yield return null;
            Check("M03_EClosesReadable", !flow.ReadableOpen, "open=" + flow.ReadableOpen);

            input.Move = new Vector2(0f, -1f);
            for (int i = 0; i < 20; i++)
                yield return null;
            input.Stop();
            moved = house.Player.transform.position - at;
            moved.y = 0f;
            Check("M04_ControlsRestored", !flow.ReadableOpen && moved.magnitude > 0.2f, "moved=" + moved.magnitude.ToString("F2"));

            // Esc closes the readable instead of pausing; pause closes it too.
            radio.Interact();
            yield return null;
            input.PressPauseNextFrame();
            yield return null;
            yield return null;
            bool escClosed = !flow.ReadableOpen && flow.Flow.RunState == RunState.Playing;
            radio.Interact();
            flow.TogglePause();
            yield return null;
            bool pauseClosed = !flow.ReadableOpen && flow.Flow.RunState == RunState.Paused;
            flow.TogglePause();
            yield return null;
            Check("M05_EscAndPauseClose", escClosed && pauseClosed && flow.Flow.RunState == RunState.Playing,
                "esc=" + escClosed + " pause=" + pauseClosed);
        }

        // Bug fix 4: Shift sprints (faster than walking), Space jumps from the
        // ground only, never again in mid-air, and neither works while paused.
        private IEnumerator SprintAndJump(BlackthornGreybox house, ScriptedPlayerInput input)
        {
            GameFlowController flow = house.FlowController;
            Transform body = house.Player.transform;

            house.Player.Respawn(new Vector3(-1.5f, 0.05f, -5.5f), 90f);
            yield return Wait(0.3f);
            Vector3 a = body.position;
            input.Move = new Vector2(0f, 1f);
            yield return Wait(0.5f);
            Vector3 b = body.position;
            input.SprintHeld = true;
            yield return Wait(0.5f);
            Vector3 c = body.position;
            bool sprinting = house.Player.Motor.Sprinting;
            input.Stop();
            float walk = Flat(b - a) / 0.5f;
            float sprint = Flat(c - b) / 0.5f;
            Check("J01_SprintFaster", sprinting && sprint > walk * 1.4f && house.Player.Motor.Stamina.Stamina01 < 1f,
                "walk=" + walk.ToString("F2") + " sprint=" + sprint.ToString("F2") + " m/s stamina=" + house.Player.Motor.Stamina.Stamina01.ToString("F2"));

            yield return Wait(0.3f);
            int jumps = house.Player.Motor.Jumps;
            float ground = body.position.y;
            float peak = ground;
            input.PressJumpNextFrame();
            bool pressedInAir = false;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                peak = Mathf.Max(peak, body.position.y);
                if (!pressedInAir && t > 0.15f && !house.Player.Motor.Grounded)
                {
                    pressedInAir = true;
                    input.PressJumpNextFrame();
                }
                yield return null;
            }
            Check("J02_JumpOnceNoAirJump", house.Player.Motor.Jumps == jumps + 1 && peak - ground > 0.6f && pressedInAir
                && house.Player.Motor.Grounded && Mathf.Abs(body.position.y - ground) < 0.05f,
                "jumps=" + (house.Player.Motor.Jumps - jumps) + " rise=" + (peak - ground).ToString("F2") + " airPress=" + pressedInAir);

            flow.TogglePause();
            yield return null;
            input.PressJumpNextFrame();
            input.Move = new Vector2(0f, 1f);
            input.SprintHeld = true;
            Vector3 pausedAt = body.position;
            for (int i = 0; i < 10; i++)
                yield return null;
            Check("J03_PausedNoJumpOrSprint", house.Player.Motor.Jumps == jumps + 1 && (body.position - pausedAt).sqrMagnitude < 1e-6f,
                "jumps=" + (house.Player.Motor.Jumps - jumps));
            input.Stop();
            flow.TogglePause();
            yield return null;
        }

        // Bug fix 1: the player's CharacterController cannot pass through the
        // ghost's capsule.
        private IEnumerator GhostBlocks(BlackthornGreybox house)
        {
            GhostMotionController ghost = house.Ghost;
            Transform body = house.Player.transform;
            CharacterController cc = body.GetComponent<CharacterController>();
            Vector3 g = ghost.transform.position;
            Vector3 away = body.position - g;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
                away = Vector3.back;
            Vector3 start = g + away.normalized * 2.5f;
            start.y = body.position.y;
            float yaw = Mathf.Atan2(-away.x, -away.z) * Mathf.Rad2Deg;
            house.Player.Respawn(start, yaw);
            yield return new WaitForFixedUpdate();
            yield return null;

            float closest = float.MaxValue;
            for (int i = 0; i < 90; i++)
            {
                house.Player.Motor.Move(new Vector2(0f, 1f), 1f / 60f);
                Vector3 d = body.position - ghost.transform.position;
                d.y = 0f;
                closest = Mathf.Min(closest, d.magnitude);
                yield return null;
            }
            float contact = GhostMotionController.BodyRadius + cc.radius;
            float travelled = Flat(body.position - start);
            Check("B01_GhostBodyBlocksPlayer", ghost.BodySolid && ghost.Body.gameObject.layer == GhostMotionController.BodyLayer
                && !ghost.Body.isTrigger && closest >= contact - 0.06f && travelled > 1.2f,
                "closest=" + closest.ToString("F2") + " contact=" + contact.ToString("F2") + " travelled=" + travelled.ToString("F2"));
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
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
