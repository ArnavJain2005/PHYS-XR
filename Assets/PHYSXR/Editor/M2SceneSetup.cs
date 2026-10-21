using System;
using System.Linq;
using PHYSXR.App;
using PHYSXR.Game.Dev;
using PHYSXR.Game.Level;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PHYSXR.EditorTools
{
    // Editor-only tooling for the M2 desktop greybox.
    //
    //  - CreateScenes: (re)creates the two shipping scenes with Unity's own
    //    scene API (no hand-written YAML) and sets the build list to exactly
    //    Bootstrap (0) + Game_BlackthornHouse (1). SampleScene stays in the
    //    project as a developer scene but leaves the build. Idempotent.
    //  - ApplyBuildScenes: only resets the build list.
    //  - RunRouteSelfTest: plays from Bootstrap with M2RouteSelfTest enabled.
    //  - BuildWindows: batch-mode Windows build ("-buildPath <exe>" optional).
    //
    // Batch use:
    //   Unity -batchmode -quit -projectPath <proj> -executeMethod PHYSXR.EditorTools.M2SceneSetup.CreateScenes
    //   Unity -batchmode -quit -projectPath <proj> -executeMethod PHYSXR.EditorTools.M2SceneSetup.BuildWindows
    public static class M2SceneSetup
    {
        public const string BootstrapScenePath = "Assets/PHYSXR/Scenes/Bootstrap.unity";
        public const string GameScenePath = "Assets/PHYSXR/Scenes/Game_BlackthornHouse.unity";
        private const string DefaultBuildPath = "Builds/M2_Windows/PHYS-XR.exe";

        [MenuItem("PHYS-XR/M2/Create Or Repair Scenes")]
        public static void CreateScenes()
        {
            Scene bootstrap = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var systems = new GameObject("[Systems]");
            systems.AddComponent<AppBootstrap>();
            EditorSceneManager.SaveScene(bootstrap, BootstrapScenePath);

            Scene game = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PhysxrArtSetup.ApplyLighting();
            var house = new GameObject("BlackthornHouse");
            house.AddComponent<BlackthornGreybox>();
            EditorSceneManager.SaveScene(game, GameScenePath);

            ApplyBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("M2SceneSetup: created " + BootstrapScenePath + " and " + GameScenePath);
        }

        [MenuItem("PHYS-XR/M2/Apply Build Scenes")]
        public static void ApplyBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScenePath, true),
                new EditorBuildSettingsScene(GameScenePath, true)
            };
            Debug.Log("M2SceneSetup: build scenes = " + string.Join(", ", EditorBuildSettings.scenes.Select(s => s.path)));
        }

        [MenuItem("PHYS-XR/M2/Run Route Self-Test (Play Mode)")]
        public static void RunRouteSelfTest()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            SessionState.SetBool(M2RouteSelfTest.EditorSessionKey, true);
            EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("PHYS-XR/Run Encounter Self-Test (Play Mode)")]
        public static void RunEncounterSelfTest()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            SessionState.SetBool(EncounterSelfTest.EditorSessionKey, true);
            EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        public static void BuildWindows()
        {
            string buildPath = ArgumentAfter("-buildPath") ?? DefaultBuildPath;
            ApplyBuildScenes();

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = buildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log("M2SceneSetup: build " + report.summary.result + " -> " + buildPath
                + " (" + report.summary.totalErrors + " errors, " + report.summary.totalWarnings + " warnings)");
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static string ArgumentAfter(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }
    }
}
