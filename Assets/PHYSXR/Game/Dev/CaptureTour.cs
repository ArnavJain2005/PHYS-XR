using System;
using System.Collections;
using System.IO;
using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Level;
using UnityEngine;

namespace PHYSXR.Game.Dev
{
    // Development-only: a short scripted tour that saves screenshots of the
    // main menu and of fixed viewpoints in the house (before and after power,
    // the ghost, the encounter and the ending), then quits. Used to review the
    // lighting and art from a windowed player build without a person driving
    // it. Before each static room shot it also logs the frame times of the
    // next 90 frames ("CAPTURE_PERF name avg p95 max", milliseconds), a
    // cheap, repeatable view-by-view performance record. Enabled only by "-physxr-capture <folder>"; AppBootstrap starts the
    // trial after the menu shot.
    public class CaptureTour : MonoBehaviour
    {
        public const string Argument = "-physxr-capture";

        private string folder;

        public event Action MenuCaptured;

        public static string RequestedFolder()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == Argument)
                    return args[i + 1];
            return null;
        }

        private IEnumerator Start()
        {
            folder = RequestedFolder() ?? Path.Combine(Application.persistentDataPath, "captures");
            Directory.CreateDirectory(folder);
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot("00_menu");
            MenuCaptured?.Invoke();

            BlackthornGreybox house = null;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (house == null || !house.FlowController.RunStarted)
            {
                house = FindAnyObjectByType<BlackthornGreybox>();
                if (Time.realtimeSinceStartup > deadline) { Quit(); yield break; }
                yield return null;
            }

            var input = new ScriptedPlayerInput();
            house.Player.SetInput(input);
            GameFlowController flow = house.FlowController;
            yield return new WaitForSeconds(2f);

            yield return View(house, new Vector3(0f, 0.05f, -6.6f), 0f, 4f, "01_entry_hall_dark");
            yield return View(house, new Vector3(1.5f, 0.05f, -2f), 215f, 6f, "02_entry_hall_stairs_clock");
            flow.TriggerFirstSighting(house.Anchors.FirstSighting);
            yield return View(house, new Vector3(-4f, 0.05f, 1.2f), 0f, 4f, "03_corridor_first_sighting", 1.8f);
            // The same figure from further back: does it read at a distance?
            yield return View(house, new Vector3(-4f, 0.05f, -1.6f), 0f, 3f, "03b_corridor_distance", 0.8f);
            yield return View(house, new Vector3(-4.2f, 0.05f, 11.4f), 315f, 18f, "04_kitchen_fuse");
            house.Fuse.Interact();
            house.FuseBox.Interact();
            yield return new WaitForSeconds(3.5f);
            yield return View(house, new Vector3(3.6f, 0.05f, 10.5f), 75f, 6f, "05_utility_powered");
            yield return View(house, new Vector3(4f, 0.05f, 7.0f), 160f, 10f, "06_dining_powered");
            flow.TriggerEncounter("capture");
            yield return new WaitForSeconds(1.2f);
            yield return GhostView(house, 5f, 30f, "07a_encounter_mid");
            yield return GhostView(house, 3f, 80f, "07b_encounter_profile");
            Vector3 g = house.Ghost.transform.position;
            Vector3 stand = g + new Vector3(1.2f, 0.05f, -2.2f);
            Vector3 to = g - stand;
            yield return View(house, stand, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -6f, "07_encounter_close", 1.5f);
            yield return View(house, new Vector3(0.6f, 0.05f, -6.8f), 0f, 4f, "08_front_door_powered");

            flow.UseExit();
            yield return new WaitForSeconds(7.6f);
            yield return Shot("09_ending_reveal");
            yield return new WaitForSeconds(9f);
            yield return Shot("10_ending_subtitles");
            house.Ending.Skip();
            yield return new WaitForSeconds(1f);
            yield return Shot("11_end_card");
            Quit();
        }

        private IEnumerator View(BlackthornGreybox house, Vector3 position, float yaw, float pitchDown, string name, float settle = 0.6f)
        {
            house.Player.Respawn(position, yaw);
            house.Player.Motor.Look(new Vector2(0f, -pitchDown));
            yield return new WaitForSeconds(settle);
            yield return Shot(name);
        }

        // Stands `distance` from the ghost, `aroundDegrees` off its facing, and
        // looks at its chest.
        private IEnumerator GhostView(BlackthornGreybox house, float distance, float aroundDegrees, string name)
        {
            Transform ghost = house.Ghost.transform;
            Vector3 dir = Quaternion.Euler(0f, aroundDegrees, 0f) * ghost.forward;
            Vector3 stand = ghost.position + dir * distance;
            stand.y = 0.05f;
            Vector3 to = house.Ghost.ChestPosition - (stand + Vector3.up * 1.65f);
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitchDown = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            yield return View(house, stand, yaw, pitchDown, name, 0.5f);
        }

        private IEnumerator Shot(string name)
        {
            // Only the static room views: sampling before the timed ghost
            // beats (03, 07) or the ending would shift what they capture.
            if (name.StartsWith("01") || name.StartsWith("02") || name.StartsWith("04") || name.StartsWith("05")
                || name.StartsWith("06") || name.StartsWith("08"))
                yield return SampleFrames(name);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
            Debug.Log("CAPTURE " + name);
            yield return null;
            yield return null;
        }

        private static IEnumerator SampleFrames(string name)
        {
            const int frames = 90;
            var ms = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                ms[i] = Time.unscaledDeltaTime * 1000f;
            }
            float sum = 0f, max = 0f;
            foreach (float v in ms)
            {
                sum += v;
                max = Mathf.Max(max, v);
            }
            Array.Sort(ms);
            float p95 = ms[Mathf.Min(frames - 1, Mathf.CeilToInt(frames * 0.95f) - 1)];
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "CAPTURE_PERF {0} avg={1:F2} p95={2:F2} max={3:F2}", name, sum / frames, p95, max));
        }

        private static void Quit()
        {
            Debug.Log("CAPTURE END");
#if !UNITY_EDITOR
            Application.Quit(0);
#endif
        }
    }
}
