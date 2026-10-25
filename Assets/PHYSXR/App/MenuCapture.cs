using System.Collections;
using System.IO;
using PHYSXR.Game.Level;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PHYSXR.App
{
    // Development-only screenshots of the welcome screen, for reviewing the
    // layout at different window sizes. Enabled only by
    //   -physxr-menu-capture <folder>
    // (with -screen-width/-screen-height -screen-fullscreen 0 for the size).
    // It photographs a mid-reveal frame, the finished menu in each sensor
    // mode, a focused control and the Settings and Controls panels, then
    // quits. It never saves settings; the USB-serial shot uses whatever port
    // the command line or settings name.
    //
    // With -physxr-menu-capture-begin it then presses Begin in Virtual-only
    // mode, photographs the fade and the start of play, returns to the menu,
    // and logs how many menu-music sources are playing at each step (to show
    // the soundscape never doubles up).
    public class MenuCapture : MonoBehaviour
    {
        private string folder;
        private string size;

        private IEnumerator Start()
        {
            folder = Argument(MainMenu.MenuCaptureArgument) ?? Path.Combine(Application.persistentDataPath, "menu_captures");
            Directory.CreateDirectory(folder);
            MainMenu menu = GetComponent<MainMenu>();
            yield return null;
            size = Screen.width + "x" + Screen.height;

            while (menu.RevealTime < 2.3f)
                yield return null;
            yield return Shot("reveal");

            while (menu.Revealing)
                yield return null;
            yield return Wait(1.2f);

            int original = menu.CurrentModeIndex;
            string[] names = { "virtual", "simulated", "usb", "udp" };
            foreach (int mode in new[] { 0, 1, 3, 2 })
            {
                menu.SetModeIndex(mode);
                yield return Wait(mode == 2 ? 6f : 1f);
                yield return Shot("mode_" + names[mode]);
            }

            menu.FocusForCapture("Sensors");
            yield return Wait(0.6f);
            yield return Shot("focus");
            menu.OpenForCapture(1);
            yield return Wait(0.4f);
            yield return Shot("settings");
            menu.OpenForCapture(2);
            yield return Wait(0.4f);
            yield return Shot("controls");
            menu.OpenForCapture(0);

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), MainMenu.MenuCaptureArgument + "-begin") >= 0)
                yield return BeginRoundTrip(menu);

            menu.SetModeIndex(original);
            Debug.Log("[MenuCapture] done: " + folder);
            Application.Quit(0);
        }

        private IEnumerator BeginRoundTrip(MainMenu menu)
        {
            menu.SetModeIndex(0);
            yield return Wait(0.5f);
            LogAudio("before Begin");
            menu.BeginForCapture();
            yield return Wait(0.8f);
            yield return Shot("begin_fade");

            float deadline = Time.realtimeSinceStartup + 40f;
            BlackthornGreybox house = null;
            while ((house == null || !house.FlowController.RunStarted) && Time.realtimeSinceStartup < deadline)
            {
                house = FindAnyObjectByType<BlackthornGreybox>();
                yield return null;
            }
            Debug.Log("[MenuCapture] game started: " + (house != null) + " scene=" + SceneManager.GetActiveScene().name);
            yield return Wait(0.4f);
            LogAudio("game +0.4 s");
            yield return Shot("game_fade_in");
            yield return Wait(3f);
            LogAudio("game +3.4 s");
            yield return Shot("game_start");

            AppBootstrap boot = GetComponent<AppBootstrap>();
            boot.ReturnToMenu();
            yield return Wait(0.5f);
            while (menu.Revealing)
                yield return null;
            yield return Wait(4.5f);
            LogAudio("back in menu +5 s");
            yield return Shot("returned");

            boot.ReturnToMenu();
            yield return Wait(6f);
            LogAudio("second return +6 s");
        }

        private static void LogAudio(string when)
        {
            int playing = 0, theme = 0, listeners = 0;
            float themeVolume = 0f;
            foreach (AudioSource s in FindObjectsByType<AudioSource>())
            {
                if (!s.isPlaying)
                    continue;
                playing++;
                if (s.clip != null && s.clip.name == "PHYSXR_MenuTheme")
                {
                    theme++;
                    themeVolume = s.volume;
                }
            }
            foreach (AudioListener l in FindObjectsByType<AudioListener>())
                if (l.isActiveAndEnabled)
                    listeners++;
            Debug.Log("[MenuCapture] audio " + when + ": playing sources " + playing + ", menu theme playing " + theme
                      + " (volume " + themeVolume.ToString("F2") + "), active listeners " + listeners + ", master " + AudioListener.volume.ToString("F2"));
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(folder, "menu_" + size + "_" + name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[MenuCapture] " + path);
            yield return null;
            yield return null;
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;
        }

        private static string Argument(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }
    }
}
