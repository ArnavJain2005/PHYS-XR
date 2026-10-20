using PHYSXR.Game.Dev;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Level;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PHYSXR.App
{
    // Lives on the persistent [Systems] object in the Bootstrap scene (build
    // index 0). It survives game-scene reloads, owns the GameSession, hands it
    // to each freshly loaded game scene, then loads Game_BlackthornHouse.
    //
    // M2 scope: session + scene flow only. The UDP receiver, communication
    // pipeline, event dedup memory and telemetry sink also belong here per
    // the spec, but are wired in M5-M7; M2 runs with no sensor station.
    public class AppBootstrap : MonoBehaviour
    {
        public const string SelfTestArgument = "-physxr-m2-selftest";

        private static bool exists;
        private bool isPrimary;

        // Keeps the duplicate guard correct when Enter Play Mode skips domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            exists = false;
        }

        public GameSession Session { get; } = new GameSession();

        private void Awake()
        {
            // Bootstrap is only loaded once, but guard against a second copy
            // (for example, if someone loads the Bootstrap scene again).
            if (exists)
            {
                Destroy(gameObject);
                return;
            }

            exists = true;
            isPrimary = true;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (SelfTestRequested())
                gameObject.AddComponent<M2RouteSelfTest>();
        }

        private void Start()
        {
            SceneManager.LoadScene(GameFlowController.GameSceneName);
        }

        private void OnDestroy()
        {
            if (!isPrimary)
                return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            exists = false;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != GameFlowController.GameSceneName)
                return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                BlackthornGreybox house = root.GetComponent<BlackthornGreybox>();
                if (house != null)
                {
                    house.FlowController.AttachSession(Session);
                    return;
                }
            }

            Debug.LogError("[AppBootstrap] " + scene.name + " has no BlackthornGreybox root; session not attached.");
        }

        private static bool SelfTestRequested()
        {
            foreach (string arg in System.Environment.GetCommandLineArgs())
                if (arg == SelfTestArgument)
                    return true;
#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool(M2RouteSelfTest.EditorSessionKey, false))
            {
                UnityEditor.SessionState.SetBool(M2RouteSelfTest.EditorSessionKey, false);
                return true;
            }
#endif
            return false;
        }
    }
}
