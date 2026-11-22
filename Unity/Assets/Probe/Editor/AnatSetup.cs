using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxProbe.Editor
{
    /// Сцена замера анатомической головы и сборка игрока.
    /// Unity.exe -batchmode -quit -projectPath Unity -executeMethod VoxProbe.Editor.AnatSetup.BuildWindows
    /// Проба (Probe.unity, упрощённая голова) остаётся отдельным режимом и не меняется.
    public static class AnatSetup
    {
        public const string ScenePath = "Assets/Probe/Anatomy.unity";

        public static void Setup()
        {
            ProbeSetup.Setup(); // URP-ассет и настройки игрока — общие с пробой
            var mat = AnatShots.EnsureMaterial();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.allowMSAA = false; cam.allowHDR = false;
            var ad = camGo.AddComponent<UniversalAdditionalCameraData>();
            ad.renderShadows = false; ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None;
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.shadows = LightShadows.None; light.intensity = 1.0f;
            lightGo.transform.rotation = Quaternion.Euler(30, 160, 0);
            new GameObject("Anatomy").AddComponent<AnatRunner>().anatMaterial = mat;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("ANAT_SETUP_OK");
        }

        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/AnatWin/VoxelAnat.exe");
        public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/AnatAndroid/VoxelAnat.apk");

        static void Build(BuildTarget target, string path)
        {
            Setup();
            EditorUserBuildSettings.buildAppBundle = false;
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path, target = target, options = BuildOptions.None });
            Debug.Log("ANAT_BUILD " + r.summary.result + " " + r.summary.totalSize + " bytes " + r.summary.totalTime + " -> " + path);
            if (r.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
