using System.Collections;
using System.Globalization;
using PHYSXR.Communication;
using PHYSXR.Core.Enums;
using PHYSXR.Game.Art;
using PHYSXR.Game.Audio;
using PHYSXR.Game.Dev;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Level;
using PHYSXR.Game.Settings;
using PHYSXR.Game.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PHYSXR.App
{
    // Lives on the persistent [Systems] object in the Bootstrap scene (build
    // index 0). It survives game-scene reloads and owns everything that must
    // outlive one attempt (spec section C):
    //
    //   GameSession       run/attempt counters, checkpoint, trial condition
    //   SensorService     USB-serial link / UDP receiver, validator/pipeline,
    //                     event-dedup memory, the packet simulator
    //   TelemetryService  the trial's JSON Lines file
    //   GameSettings      per-device player settings
    //   MainMenu          the start screen, with its own camera and listener
    //
    // Flow: the menu shows on launch -> Begin configures sensing and telemetry
    // for the chosen condition and loads Game_BlackthornHouse -> each loaded
    // game scene receives GameServices -> "Main menu" reloads Bootstrap, whose
    // duplicate [Systems] destroys itself, and this object shows the menu again.
    //
    // Command-line self-tests skip the menu:
    //   -physxr-m2-selftest   scripted objective route (encounter disabled)
    //   -physxr-selftest      scripted encounter checks (attack, lantern, death,
    //                         checkpoint, repeated restarts)
    //   -physxr-capture <dir> screenshot tour of the menu, house and ending
    //
    // Sensor link overrides (also remembered as the menu's settings):
    //   -physxr-condition VIRTUAL_ONLY|PHYSICAL_PLUS_VIRTUAL|SIMULATED   (self-tests)
    //   -physxr-transport serial|udp
    //   -physxr-serial-port AUTO|COM5
    //   -physxr-serial-baud 115200
    // Self-tests keep UDP unless -physxr-transport is given, so they never
    // open or probe COM ports.
    public class AppBootstrap : MonoBehaviour
    {
        public const string SelfTestArgument = "-physxr-m2-selftest";
        public const string EncounterSelfTestArgument = "-physxr-selftest";
        public const string ConditionArgument = "-physxr-condition";
        public const string TransportArgument = "-physxr-transport";
        public const string SerialPortArgument = "-physxr-serial-port";
        public const string SerialBaudArgument = "-physxr-serial-baud";
        private const float PerfInterval = 5f;

        private static bool exists;
        private bool isPrimary;

        private GameSettings settings;
        private SensorService sensors;
        private TelemetryService telemetry;
        private MainMenu menu;
        private Camera menuCamera;
        private AudioSource menuMusic;
        private AudioSource menuStorm;
        private bool trialActive;
        private bool encounterDisabled;
        private float perfStart;
        private int perfFrames;
        private float perfMinFps = float.MaxValue;

        // Keeps the duplicate guard correct when Enter Play Mode skips domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            exists = false;
        }

        public GameSession Session { get; } = new GameSession();

        public SensorService Sensors => sensors;

        public TelemetryService Telemetry => telemetry;

        public MainMenu Menu => menu;

        private void Awake()
        {
            // Bootstrap is reloaded for "Main menu"; the copy in that reload
            // finds the original and removes itself.
            if (exists)
            {
                Destroy(gameObject);
                return;
            }

            exists = true;
            isPrimary = true;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;

            settings = GameSettings.Load();
            ApplyLinkArguments(settings);
            settings.ApplyAudio();
            telemetry = new TelemetryService();
            sensors = gameObject.AddComponent<SensorService>();
            sensors.SetTelemetry(telemetry.Channel);

            UIKit.EnsureEventSystem(transform);
            BuildMenuCamera();
            menu = gameObject.AddComponent<MainMenu>();
            menu.Initialize(settings, sensors);
            menu.BeginRequested += BeginTrial;
            menu.QuitRequested += Quit;
            menu.SetFooter("PHYS-XR " + Application.version + "   -   telemetry: " + System.IO.Path.Combine(Application.persistentDataPath, "telemetry"));

            if (CaptureTour.RequestedFolder() != null)
            {
                // Dev capture: menu shot, then a VIRTUAL_ONLY trial.
                CaptureTour tour = gameObject.AddComponent<CaptureTour>();
                tour.MenuCaptured += () => BeginTrial(RunCondition.VIRTUAL_ONLY, "capture");
            }
            else if (SelfTestRequested(out bool encounterTest))
            {
                encounterDisabled = !encounterTest;
                if (encounterTest)
                    gameObject.AddComponent<EncounterSelfTest>();
                else
                    gameObject.AddComponent<M2RouteSelfTest>();
            }
        }

        private void Start()
        {
            if (encounterDisabled || GetComponent<EncounterSelfTest>() != null)
            {
                BeginTrial(ConditionFromArgs(RunCondition.VIRTUAL_ONLY), "selftest");
                return;
            }

            StartCoroutine(WarmUp());
            ShowMenu(true);
        }

        private void BuildMenuCamera()
        {
            var go = new GameObject("MenuCamera");
            go.transform.SetParent(transform, false);
            menuCamera = go.AddComponent<Camera>();
            menuCamera.clearFlags = CameraClearFlags.SolidColor;
            menuCamera.backgroundColor = Color.black;
            menuCamera.cullingMask = 0;
            go.AddComponent<AudioListener>();

            menuMusic = go.AddComponent<AudioSource>();
            menuMusic.loop = true;
            menuMusic.playOnAwake = false;
            menuMusic.clip = ProceduralAudio.Get(AudioCue.MenuMusic);
            menuMusic.volume = GameAudio.Level(AudioCue.MenuMusic);
            menuStorm = go.AddComponent<AudioSource>();
            menuStorm.loop = true;
            menuStorm.playOnAwake = false;
            menuStorm.clip = ProceduralAudio.Get(AudioCue.AmbienceStorm);
            menuStorm.volume = 0.25f;
        }

        // Generates the house's textures and sounds a little per frame while
        // the menu is up, so the first load does not stall.
        private IEnumerator WarmUp()
        {
            foreach (ProceduralTextures.Id id in System.Enum.GetValues(typeof(ProceduralTextures.Id)))
            {
                ProceduralTextures.Get(id);
                yield return null;
            }
            foreach (AudioCue cue in System.Enum.GetValues(typeof(AudioCue)))
            {
                for (int v = 0; v < ProceduralAudio.VariantCount(cue); v++)
                    ProceduralAudio.Get(cue, v);
                yield return null;
            }
        }

        private void ShowMenu(bool show)
        {
            menu.Show(show);
            menuCamera.gameObject.SetActive(show);
            if (show)
            {
                menuMusic.Play();
                menuStorm.Play();
            }
            else
            {
                menuMusic.Stop();
                menuStorm.Stop();
            }
        }

        private void BeginTrial(RunCondition condition, string trialId)
        {
            Session.Condition = condition;
            Session.TrialId = trialId;
            Session.RequestNewRun();
            settings.LastCondition = (int)condition;

            PhysicalTransport transport = trialId == "selftest" && !HasArgument(TransportArgument)
                ? PhysicalTransport.UDP
                : (PhysicalTransport)Mathf.Clamp(settings.Transport, 0, 1);
            SerialLinkSettings serial = menu.SerialSettings();
            string summary = string.Format(CultureInfo.InvariantCulture, "sens={0:F3};invert={1};vol={2:F2};bright={3:F2};transport={4};serial={5}",
                settings.MouseSensitivity, settings.InvertY, settings.MasterVolume, settings.Brightness,
                condition == RunCondition.SIMULATED ? "SIMULATED" : transport.ToString(), serial);
            // The trial file must be open before sensing is configured, or the
            // first SOURCE_STATUS record (condition, transport, port) is lost.
            telemetry.BeginTrial(trialId, condition.ToString(), summary);
            sensors.Configure(condition, transport, serial);
            trialActive = true;
            ResetPerf();

            ShowMenu(false);
            SceneManager.LoadScene(GameFlowController.GameSceneName);
        }

        public void ReturnToMenu()
        {
            telemetry.EndTrial();
            trialActive = false;
            SceneManager.LoadScene(0);
        }

        public void Quit()
        {
            telemetry.EndTrial();
            trialActive = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (!isPrimary)
                return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            telemetry?.Dispose();
            exists = false;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                telemetry?.Flush();
        }

        private void OnApplicationQuit()
        {
            telemetry?.EndTrial();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.buildIndex == 0)
            {
                ShowMenu(true);
                return;
            }

            if (scene.name != GameFlowController.GameSceneName)
                return;

            ShowMenu(false);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                BlackthornGreybox house = root.GetComponent<BlackthornGreybox>();
                if (house != null)
                {
                    house.FlowController.AttachServices(new GameServices
                    {
                        Session = Session,
                        Sensors = sensors,
                        Telemetry = telemetry.Channel,
                        Settings = settings,
                        ReturnToMenu = ReturnToMenu,
                        Quit = Quit,
                        EncounterDisabled = encounterDisabled
                    });
                    return;
                }
            }

            Debug.LogError("[AppBootstrap] " + scene.name + " has no BlackthornGreybox root; services not attached.");
        }

        private void Update()
        {
            if (!trialActive)
                return;

            telemetry.SetFrame(Session.Attempt, Time.frameCount);

            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
                perfMinFps = Mathf.Min(perfMinFps, 1f / dt);
            perfFrames++;
            float elapsed = Time.unscaledTime - perfStart;
            if (elapsed >= PerfInterval)
            {
                telemetry.Channel.Emit("PERF", string.Format(CultureInfo.InvariantCulture, "fps_mean={0:F1} fps_min={1:F1} mono_mb={2:F1}",
                    perfFrames / elapsed, perfMinFps, System.GC.GetTotalMemory(false) / 1048576.0));
                ResetPerf();
            }
        }

        private void ResetPerf()
        {
            perfStart = Time.unscaledTime;
            perfFrames = 0;
            perfMinFps = float.MaxValue;
        }

        private static bool SelfTestRequested(out bool encounterTest)
        {
            encounterTest = false;
            foreach (string arg in System.Environment.GetCommandLineArgs())
            {
                if (arg == SelfTestArgument)
                    return true;
                if (arg == EncounterSelfTestArgument)
                {
                    encounterTest = true;
                    return true;
                }
            }
#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool(M2RouteSelfTest.EditorSessionKey, false))
            {
                UnityEditor.SessionState.SetBool(M2RouteSelfTest.EditorSessionKey, false);
                return true;
            }
            if (UnityEditor.SessionState.GetBool(EncounterSelfTest.EditorSessionKey, false))
            {
                UnityEditor.SessionState.SetBool(EncounterSelfTest.EditorSessionKey, false);
                encounterTest = true;
                return true;
            }
#endif
            return false;
        }

        private static bool HasArgument(string name)
        {
            return System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;
        }

        private static string ArgumentValue(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }

        private static void ApplyLinkArguments(GameSettings s)
        {
            string transport = ArgumentValue(TransportArgument);
            if (transport != null)
                s.Transport = transport.Equals("udp", System.StringComparison.OrdinalIgnoreCase) ? (int)PhysicalTransport.UDP : (int)PhysicalTransport.USB_SERIAL;

            string port = ArgumentValue(SerialPortArgument);
            if (port != null && (port.Equals(GameSettings.AutoSerialPort, System.StringComparison.OrdinalIgnoreCase) || Win32SerialPorts.IsValidPortName(port)))
                s.SerialPort = port.ToUpperInvariant();
            else if (port != null)
                Debug.LogWarning("[AppBootstrap] ignoring " + SerialPortArgument + " '" + port + "' (expected AUTO or COMn)");

            string baud = ArgumentValue(SerialBaudArgument);
            if (baud != null && int.TryParse(baud, out int b) && b > 0)
                s.SerialBaud = b;
        }

        private static RunCondition ConditionFromArgs(RunCondition fallback)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == ConditionArgument && System.Enum.TryParse(args[i + 1], out RunCondition c))
                    return c;
            return fallback;
        }
    }
}
