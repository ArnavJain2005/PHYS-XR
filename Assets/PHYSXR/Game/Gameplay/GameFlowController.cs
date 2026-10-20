using System;
using PHYSXR.Game.Flow;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PHYSXR.Game.Gameplay
{
    // Unity-side owner of one run in Game_BlackthornHouse. It wraps the plain
    // C# GameFlow (all objective/run transitions live there) and applies the
    // Unity consequences: power restoration, time scale, cursor lock, run
    // timer, player-facing messages and scene reload for restart.
    //
    // Interactables and the pause menu call the methods below; nothing here
    // reads a device. M3 will add the WorldState feed and GhostRuntime.Tick()
    // here; in M2 there is no ghost, and the existing ghost pipeline,
    // GameplaySession and decision layers are untouched.
    public class GameFlowController : MonoBehaviour
    {
        public const string GameSceneName = "Game_BlackthornHouse";

        private PowerSystem powerSystem;
        private GameSession session;
        private bool restarting;

        public GameFlow Flow { get; private set; } = new GameFlow();

        public GameSession Session => session;

        // True once Start() has begun this scene's run on the session.
        public bool RunStarted { get; private set; }

        // Seconds of unpaused play since the run started; frozen on success.
        public float RunSeconds { get; private set; }

        public string LastMessage { get; private set; } = string.Empty;

        public float LastMessageTime { get; private set; } = -100f;

        public event Action<string> MessageRaised;

        public void Initialize(PowerSystem powerSystem)
        {
            this.powerSystem = powerSystem;
            powerSystem.PowerRestored += OnPowerRestored;
            powerSystem.ApplyUnpowered();
            Flow.RunStateChanged += OnRunStateChanged;
        }

        // Called by AppBootstrap when the scene loads. If the game scene is
        // played directly in the Editor (no Bootstrap), Start() creates a
        // local session instead and says so.
        public void AttachSession(GameSession gameSession)
        {
            session = gameSession;
        }

        private void Awake()
        {
            Time.timeScale = 1f;
        }

        private void Start()
        {
            if (session == null)
            {
                Debug.LogWarning("[GameFlowController] No Bootstrap session attached (scene played directly); using a local session.");
                session = new GameSession();
            }

            session.BeginNewRun();
            RunStarted = true;
            ApplyCursor(Flow.RunState);
            Debug.Log("[GameFlow] RUN_START run=" + session.RunNumber + " attempt=" + session.Attempt);
        }

        private void Update()
        {
            if (Flow.RunState == RunState.Playing)
                RunSeconds += Time.deltaTime;
        }

        private void OnDestroy()
        {
            if (powerSystem != null)
                powerSystem.PowerRestored -= OnPowerRestored;
        }

        public InteractionResult CollectFuse()
        {
            return Report(Flow.TryCollectFuse(), "FUSE_PICKUP");
        }

        public InteractionResult InstallFuse()
        {
            InteractionResult result = Report(Flow.TryInstallFuse(), "FUSE_INSTALLED");
            if (result.Accepted)
                powerSystem.RestorePower();
            return result;
        }

        public InteractionResult UseExit()
        {
            return Report(Flow.TryUseExit(), "EXIT_USED");
        }

        public bool Pause()
        {
            return Flow.Pause();
        }

        public bool Resume()
        {
            return Flow.Resume();
        }

        public bool TogglePause()
        {
            return Flow.TogglePause();
        }

        // Full restart from the beginning: reload the game scene, which
        // rebuilds the house, player, objective and lighting from scratch.
        // This is not a checkpoint restore (that arrives with M4).
        public void RestartRun()
        {
            if (restarting)
                return;

            restarting = true;
            Debug.Log("[GameFlow] RUN_RESTART from stage=" + Flow.Stage + " state=" + Flow.RunState);
            Time.timeScale = 1f;
            SceneManager.LoadScene(GameSceneName);
        }

        public void QuitGame()
        {
            Debug.Log("[GameFlow] QUIT");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnPowerRestored()
        {
            Report(Flow.ConfirmPowerRestored(), "POWER_RESTORED");
        }

        private void OnRunStateChanged(RunState state)
        {
            Time.timeScale = state == RunState.Paused ? 0f : 1f;
            ApplyCursor(state);

            if (state == RunState.Succeeded)
                Debug.Log("[GameFlow] RUN_END outcome=success duration_s=" + RunSeconds.ToString("F1"));
            else
                Debug.Log("[GameFlow] " + (state == RunState.Paused ? "PAUSE" : "RESUME"));
        }

        private InteractionResult Report(InteractionResult result, string objectiveRecord)
        {
            if (result.Accepted)
                Debug.Log("[GameFlow] OBJECTIVE " + objectiveRecord + " stage=" + Flow.Stage + " t=" + RunSeconds.ToString("F1"));

            if (!string.IsNullOrEmpty(result.Message))
            {
                LastMessage = result.Message;
                LastMessageTime = Time.unscaledTime;
                MessageRaised?.Invoke(result.Message);
            }
            return result;
        }

        private static void ApplyCursor(RunState state)
        {
            bool playing = state == RunState.Playing;
            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !playing;
        }
    }
}
