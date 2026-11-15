using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Glowrunner.EditorTools
{
    /// <summary>Creates the game scene (menu: Glowrunner ▸ Create Game Scene) and opens it when the project loads.</summary>
    [InitializeOnLoad]
    public static class GlowrunnerSetup
    {
        public const string Root = "Assets/Glowrunner";
        public const string ScenePath = Root + "/Scenes/Glowrunner.unity";
        const string MaterialPath = Root + "/Materials/GlowBase.mat";

        static GlowrunnerSetup()
        {
            if (Application.isBatchMode || SessionState.GetBool("Glowrunner.AutoOpened", false)) return;
            SessionState.SetBool("Glowrunner.AutoOpened", true);
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                var active = EditorSceneManager.GetActiveScene();
                if (string.IsNullOrEmpty(active.path) && !active.isDirty && File.Exists(ScenePath))
                    EditorSceneManager.OpenScene(ScenePath);
            };
        }

        [MenuItem("Glowrunner/Create Game Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard")) { name = "GlowBase" };
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(0.01f, 0.01f, 0.01f));
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Glowrunner");
            go.AddComponent<GameManager>().baseMaterial = mat;
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.productName = "Glowrunner";
            AssetDatabase.SaveAssets();
            Debug.Log("Glowrunner: scene created at " + ScenePath);
        }

        /// <summary>Builds a universal (Intel + Apple silicon) release app to Builds/macOS/Glowrunner.app.</summary>
        [MenuItem("Glowrunner/Build macOS App")]
        public static void BuildMacApp()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            if (!File.Exists(GlowrunnerIcon.IconPath)) GlowrunnerIcon.Generate();

            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
            PlayerSettings.productName = "Glowrunner";

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/macOS/Glowrunner.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"Glowrunner: macOS build {s.result} ({s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors) at {s.outputPath}");
            if (Application.isBatchMode && s.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
            if (!Application.isBatchMode && s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorUtility.RevealInFinder(s.outputPath);
        }
    }
}
