using System;
using System.Collections;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Level;
using PHYSXR.Game.Player;
using UnityEngine;

namespace PHYSXR.Game.Dev
{
    // Development-only automated play-through of the M2 loop inside Unity.
    // Enabled only by the "-physxr-m2-selftest" command-line argument (or the
    // PHYS-XR > M2 > Run Route Self-Test editor menu); otherwise it is never
    // added. AppBootstrap adds it to the persistent [Systems] object so it
    // survives the restart reloads it triggers.
    //
    // It swaps the player's input for ScriptedPlayerInput and walks the real
    // CharacterController through the doorways, aims the real camera and
    // presses interact through PlayerController -> PlayerInteractor, so the
    // same code path as the E key is exercised. Each check logs
    // "M2SELFTEST PASS|FAIL <id>"; a player build exits with the failure count.
    //
    // It is evidence for the scripted route only. Manual Editor checks with
    // a person at the keyboard are still required.
    public class M2RouteSelfTest : MonoBehaviour
    {
        public const string EditorSessionKey = "PHYSXR.M2RouteSelfTest";

        private const float WalkTimeLimitSeconds = 150f;
        private const float WaypointTolerance = 0.3f;

        private int passed;
        private int failed;

        private IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            Debug.Log("M2SELFTEST BEGIN");

            BlackthornGreybox house = null;
            yield return WaitForHouse(null, h => house = h);
            if (house == null)
            {
                Finish();
                yield break;
            }

            var input = new ScriptedPlayerInput();
            house.Player.SetInput(input);
            GameFlowController flow = house.FlowController;
            PlayerInteractor interactor = house.Player.Interactor;
            InteractionResult lastResult = default;
            int interactions = 0;
            interactor.Interacted += (_, r) => { lastResult = r; interactions++; };

            // 1. Start location and initial state.
            CheckCleanStart("C01_StartState", house, expectedRun: 1);
            Color darkAmbient = RenderSettings.ambientLight;

            float walkStart = Time.time;

            // 6. Exit refuses before power, through real targeting.
            yield return WalkTo(house, input, BlackthornGreybox.ExitStandPosition);
            yield return AimAt(house, input, BlackthornGreybox.ExitDoorPosition);
            Check("C06a_ExitTargeted", interactor.RefreshTarget() == house.Exit, "target=" + Name(interactor.CurrentTarget));
            yield return Press(input, () => input.PressInteractNextFrame());
            Check("C06b_ExitRefusedBeforePower", interactions == 1 && !lastResult.Accepted
                && lastResult.Refusal == InteractionRefusal.ExitLocked, lastResult.ToString());
            Check("C06c_ExitRefusalLeavesState", flow.Flow.Stage == ObjectiveStage.FindFuse && flow.Flow.RunState == RunState.Playing,
                flow.Flow.Stage + "/" + flow.Flow.RunState);

            // 3. Fuse box rejects use before pickup (same API the interactor calls).
            InteractionResult early = house.FuseBox.Interact();
            Check("C03_FuseBoxRefusedBeforeFuse", !early.Accepted && early.Refusal == InteractionRefusal.FuseRequired
                && flow.Flow.Stage == ObjectiveStage.FindFuse && !house.Power.IsPowered && !house.Power.IsRestoring, early.ToString());

            // 2. Walk to the kitchen and pick up the fuse.
            foreach (Vector3 point in BlackthornGreybox.RouteToFuse)
                yield return WalkTo(house, input, point);
            yield return AimAt(house, input, BlackthornGreybox.FusePosition);
            Check("C02a_FuseTargeted", interactor.RefreshTarget() == house.Fuse, "target=" + Name(interactor.CurrentTarget));
            yield return Press(input, () => input.PressInteractNextFrame());
            Check("C02b_FuseCollected", lastResult.Accepted && flow.Flow.HasFuse && !house.Fuse.gameObject.activeSelf, lastResult.ToString());

            // 11. Repeated pickup cannot duplicate the fuse.
            int before = interactions;
            yield return Press(input, () => input.PressInteractNextFrame());
            InteractionResult again = house.Fuse.Interact();
            Check("C11a_RepeatPickupRefused", interactions == before && !again.Accepted
                && again.Refusal == InteractionRefusal.FuseAlreadyCollected && flow.Flow.StageHistory.Count == 2, again.ToString());

            // 9. Pause and resume through the pause intent.
            yield return Press(input, () => input.PressPauseNextFrame());
            Check("C09a_Paused", flow.Flow.RunState == RunState.Paused && Mathf.Approximately(Time.timeScale, 0f), "timeScale=" + Time.timeScale);
            Vector3 pausedAt = house.Player.transform.position;
            input.Move = Vector2.up;
            for (int i = 0; i < 10; i++)
                yield return null;
            input.Stop();
            InteractionResult whilePaused = interactor.TryInteract();
            Check("C09b_PauseBlocksGameplay", (house.Player.transform.position - pausedAt).sqrMagnitude < 1e-6f
                && whilePaused.Refusal == InteractionRefusal.GameplayInactive, whilePaused.ToString());
            yield return Press(input, () => input.PressPauseNextFrame());
            Check("C09c_Resumed", flow.Flow.RunState == RunState.Playing && Mathf.Approximately(Time.timeScale, 1f), "timeScale=" + Time.timeScale);

            // 4/5. Install the fuse and watch the power come on.
            foreach (Vector3 point in BlackthornGreybox.RouteToFuseBox)
                yield return WalkTo(house, input, point);
            yield return AimAt(house, input, BlackthornGreybox.FuseBoxPosition);
            Check("C04a_FuseBoxTargeted", interactor.RefreshTarget() == house.FuseBox, "target=" + Name(interactor.CurrentTarget));
            yield return Press(input, () => input.PressInteractNextFrame());
            Check("C04b_FuseInstalled", lastResult.Accepted && flow.Flow.FuseInstalled, lastResult.ToString());

            float waitUntil = Time.time + 5f;
            while (!flow.Flow.PowerRestored && Time.time < waitUntil)
                yield return null;
            yield return null;
            Check("C05a_PowerRestored", flow.Flow.PowerRestored && house.Power.IsPowered, "stage=" + flow.Flow.Stage);
            Check("C05b_LightingChanged",
                house.Power.CountEnabled(house.Power.MainLights) == house.Power.MainLights.Count
                && house.Power.CountEnabled(house.Power.EmergencyLights) == 0
                && RenderSettings.ambientLight != darkAmbient,
                "main=" + house.Power.CountEnabled(house.Power.MainLights) + "/" + house.Power.MainLights.Count
                + " emergency=" + house.Power.CountEnabled(house.Power.EmergencyLights)
                + " ambient=" + RenderSettings.ambientLight);
            Check("C05c_ExitIndicatorUnlocked", house.Exit.ShowsUnlocked, "indicator");

            // 11. Repeated installation cannot retrigger power.
            before = interactions;
            yield return Press(input, () => input.PressInteractNextFrame());
            InteractionResult again2 = house.FuseBox.Interact();
            Check("C11b_RepeatInstallRefused", interactions == before + 1 && !lastResult.Accepted
                && again2.Refusal == InteractionRefusal.FuseAlreadyInstalled && !house.Power.IsRestoring
                && flow.Flow.StageHistory.Count == 4, lastResult + " / " + again2);

            // 7. Walk back to the front door and leave.
            foreach (Vector3 point in BlackthornGreybox.RouteToExit)
                yield return WalkTo(house, input, point);
            yield return AimAt(house, input, BlackthornGreybox.ExitDoorPosition);
            yield return Press(input, () => input.PressInteractNextFrame());
            Check("C07_ExitSucceedsAfterPower", lastResult.Accepted && flow.Flow.RunState == RunState.Succeeded, lastResult.ToString());

            // 12. Walk-only route time (scaled time, so the paused frames above do not count).
            float walkSeconds = Time.time - walkStart;
            Check("C12_RouteUnder150s", walkSeconds < WalkTimeLimitSeconds,
                "scripted route " + walkSeconds.ToString("F1") + " s; RunSeconds=" + flow.RunSeconds.ToString("F1"));

            // 8. Success disables ordinary gameplay.
            Vector3 endAt = house.Player.transform.position;
            input.Move = Vector2.up;
            input.LookDegrees = new Vector2(5f, 0f);
            for (int i = 0; i < 10; i++)
                yield return null;
            input.Stop();
            InteractionResult afterEnd = interactor.TryInteract();
            bool pauseIgnored = !flow.TogglePause();
            Check("C08_SuccessDisablesGameplay", (house.Player.transform.position - endAt).sqrMagnitude < 1e-6f
                && afterEnd.Refusal == InteractionRefusal.GameplayInactive && interactor.CurrentTarget == null
                && pauseIgnored && flow.Flow.RunState == RunState.Succeeded, afterEnd.ToString());

            // 10. Restart from the success screen.
            BlackthornGreybox old = house;
            flow.RestartRun();
            yield return WaitForHouse(old, h => house = h);
            if (house == null)
            {
                Finish();
                yield break;
            }
            CheckCleanStart("C10a_RestartAfterSuccess", house, expectedRun: 2);

            // 10. Restart mid-run from the pause menu, after collecting the fuse.
            input = new ScriptedPlayerInput();
            house.Player.SetInput(input);
            house.Player.Respawn(BlackthornGreybox.RouteToFuse[BlackthornGreybox.RouteToFuse.Length - 1], 0f);
            yield return null;
            yield return AimAt(house, input, BlackthornGreybox.FusePosition);
            house.Player.Interactor.TryInteract();
            house.FlowController.Pause();
            Check("C10b_MidRunSetup", house.FlowController.Flow.HasFuse && house.FlowController.Flow.RunState == RunState.Paused,
                house.FlowController.Flow.Stage + "/" + house.FlowController.Flow.RunState);
            old = house;
            house.FlowController.RestartRun();
            yield return WaitForHouse(old, h => house = h);
            if (house != null)
                CheckCleanStart("C10c_RestartWhilePausedMidRun", house, expectedRun: 3);

            Finish();
        }

        private void CheckCleanStart(string id, BlackthornGreybox house, int expectedRun)
        {
            GameFlowController flow = house.FlowController;
            Vector3 p = house.Player.transform.position;
            Vector2 offset = new Vector2(p.x - BlackthornGreybox.StartPosition.x, p.z - BlackthornGreybox.StartPosition.z);
            bool ok = offset.magnitude < 0.2f
                && flow.Flow.RunState == RunState.Playing
                && flow.Flow.Stage == ObjectiveStage.FindFuse
                && flow.Flow.StageHistory.Count == 1
                && !house.Power.IsPowered && !house.Power.IsRestoring
                && house.Power.CountEnabled(house.Power.MainLights) == 0
                && house.Fuse.gameObject.activeSelf && house.Fuse.IsAvailable
                && !flow.Flow.ExitUnlocked && !house.Exit.ShowsUnlocked
                && Mathf.Approximately(Time.timeScale, 1f)
                && flow.Session.RunNumber == expectedRun && flow.Session.Attempt == 1;
            Check(id, ok, "pos=" + p + " stage=" + flow.Flow.Stage + " state=" + flow.Flow.RunState
                + " powered=" + house.Power.IsPowered + " run=" + flow.Session.RunNumber + " attempt=" + flow.Session.Attempt
                + " timeScale=" + Time.timeScale);
        }

        // Waits for a BlackthornGreybox other than `previous` whose run has started.
        private IEnumerator WaitForHouse(BlackthornGreybox previous, Action<BlackthornGreybox> found)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                BlackthornGreybox house = FindAnyObjectByType<BlackthornGreybox>();
                if (house != null && house != previous && house.FlowController.RunStarted)
                {
                    yield return null;
                    found(house);
                    yield break;
                }
                yield return null;
            }
            Check("WaitForHouse", false, "game scene did not load within 20 s");
            found(null);
        }

        private IEnumerator WalkTo(BlackthornGreybox house, ScriptedPlayerInput input, Vector3 target)
        {
            Transform body = house.Player.transform;
            float deadline = Time.time + 15f;
            while (Time.time < deadline)
            {
                Vector3 to = target - body.position;
                to.y = 0f;
                if (to.magnitude < WaypointTolerance)
                {
                    input.Stop();
                    yield break;
                }

                float yawError = Mathf.DeltaAngle(body.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                float turn = Mathf.Clamp(yawError, -8f, 8f);
                float level = house.Player.Motor.Pitch;
                input.LookDegrees = new Vector2(turn, Mathf.Clamp(level, -8f, 8f));
                float forward = Mathf.Abs(yawError) < 25f ? Mathf.Clamp01(to.magnitude / 0.6f + 0.3f) : 0f;
                input.Move = new Vector2(0f, forward);
                yield return null;
            }
            input.Stop();
            Check("WalkTo_" + target, false, "stuck at " + body.position);
        }

        private IEnumerator AimAt(BlackthornGreybox house, ScriptedPlayerInput input, Vector3 point)
        {
            Transform body = house.Player.transform;
            Transform head = house.Player.Head;
            float deadline = Time.time + 3f;
            while (Time.time < deadline)
            {
                Vector3 to = point - head.position;
                float yawError = Mathf.DeltaAngle(body.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                float desiredPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                float pitchError = house.Player.Motor.Pitch - desiredPitch;
                if (Mathf.Abs(yawError) < 0.5f && Mathf.Abs(pitchError) < 0.5f)
                    break;
                input.Move = Vector2.zero;
                input.LookDegrees = new Vector2(Mathf.Clamp(yawError, -6f, 6f), Mathf.Clamp(pitchError, -6f, 6f));
                yield return null;
            }
            input.Stop();
            yield return null;
        }

        private static IEnumerator Press(ScriptedPlayerInput input, Action schedule)
        {
            schedule();
            yield return null;
            yield return null;
        }

        private void Check(string id, bool condition, string detail)
        {
            if (condition)
            {
                passed++;
                Debug.Log("M2SELFTEST PASS " + id + " | " + detail);
            }
            else
            {
                failed++;
                Debug.LogError("M2SELFTEST FAIL " + id + " | " + detail);
            }
        }

        private void Finish()
        {
            Debug.Log("M2SELFTEST END passed=" + passed + " failed=" + failed);
#if !UNITY_EDITOR
            Application.Quit(failed);
#endif
            enabled = false;
        }

        private static string Name(Component c)
        {
            return c != null ? c.name : "none";
        }
    }
}
