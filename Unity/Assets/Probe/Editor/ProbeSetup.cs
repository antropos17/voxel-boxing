using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxProbe.Editor
{
    /// Пакетный режим: Unity.exe -batchmode -quit -projectPath Unity -executeMethod VoxProbe.Editor.ProbeSetup.Setup
    /// Создаёт URP-ассет, материалы, сцену и настройки игрока. Повторный запуск безопасен.
    public static class ProbeSetup
    {
        const string Dir = "Assets/Probe/Generated";
        const string ScenePath = "Assets/Probe/Probe.unity";

        public static void Setup()
        {
            Directory.CreateDirectory(Dir);
            // URP: без теней, без постобработки, без MSAA и HDR
            var rd = Load<UniversalRendererData>(Dir + "/ProbeRenderer.asset");
            if (rd == null) { rd = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(rd, Dir + "/ProbeRenderer.asset"); }
            var rp = Load<UniversalRenderPipelineAsset>(Dir + "/ProbeURP.asset");
            if (rp == null) { rp = UniversalRenderPipelineAsset.Create(rd); AssetDatabase.CreateAsset(rp, Dir + "/ProbeURP.asset"); }
            var so = new SerializedObject(rp);
            Set(so, "m_MainLightShadowsSupported", false);
            Set(so, "m_AdditionalLightShadowsSupported", false);
            SetInt(so, "m_AdditionalLightsRenderingMode", 0);
            SetInt(so, "m_MSAA", 1);
            Set(so, "m_SupportsHDR", false);
            Set(so, "m_RequireDepthTexture", false);
            Set(so, "m_RequireOpaqueTexture", false);
            Set(so, "m_UseSRPBatcher", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = rp;
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = rp; QualitySettings.shadows = UnityEngine.ShadowQuality.Disable; QualitySettings.vSyncCount = 0; QualitySettings.antiAliasing = 0; }

            var vox = Mat(Dir + "/Voxel.mat", "VoxProbe/Voxel", m => m.SetFloat("_Ambient", 0.38f));
            var bl = Mat(Dir + "/Blood.mat", "VoxProbe/Instanced", m => { m.enableInstancing = true; m.SetColor("_Color", new Color(0.45f, 0.02f, 0.03f)); m.SetFloat("_Vary", 0.45f); });
            var fl = Mat(Dir + "/Floor.mat", "VoxProbe/Instanced", m => { m.SetColor("_Color", new Color(0.22f, 0.22f, 0.24f)); m.SetFloat("_Vary", 0f); m.SetFloat("_Ambient", 0.6f); });

            // сцена: камера, лампа, объект пробы
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.allowMSAA = false; cam.allowHDR = false;
            var ad = camGo.AddComponent<UniversalAdditionalCameraData>();
            ad.renderShadows = false; ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None;
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.shadows = LightShadows.None; light.intensity = 1.0f;
            lightGo.transform.rotation = Quaternion.Euler(28, 150, 0);
            var probe = new GameObject("Probe").AddComponent<ProbeRunner>();
            probe.voxMaterial = vox; probe.bloodMaterial = bl; probe.floorMaterial = fl;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            // игрок
            PlayerSettings.companyName = "VoxelBox";
            PlayerSettings.productName = "VoxelProbe";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.voxelbox.probe");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 480; PlayerSettings.defaultScreenHeight = 1040;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SplashScreen.show = false;
            // обе системы ввода: проба (ProbeRunner) читает старый Input, просмотр головы (AnatViewer, -anatView) — пакет Input System
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps.Length > 0)
            {
                var pso = new SerializedObject(ps[0]);
                var p = pso.FindProperty("activeInputHandler");
                if (p != null) { p.intValue = 2; pso.ApplyModifiedPropertiesWithoutUndo(); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("PROBE_SETUP_OK");
        }

        static T Load<T>(string p) where T : Object => AssetDatabase.LoadAssetAtPath<T>(p);
        static void Set(SerializedObject so, string n, bool v) { var p = so.FindProperty(n); if (p != null) p.boolValue = v; else Debug.LogWarning("нет поля " + n); }
        static void SetInt(SerializedObject so, string n, int v) { var p = so.FindProperty(n); if (p != null) p.intValue = v; else Debug.LogWarning("нет поля " + n); }

        static Material Mat(string path, string shader, System.Action<Material> init)
        {
            var m = Load<Material>(path);
            var sh = Shader.Find(shader);
            if (sh == null) throw new System.Exception("нет шейдера " + shader);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh; init(m); EditorUtility.SetDirty(m);
            return m;
        }

        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Win/VoxelProbe.exe");
        public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/Android/VoxelProbe.apk");

        static void Build(BuildTarget target, string path)
        {
            Setup();
            EditorUserBuildSettings.buildAppBundle = false;
            var opt = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path, target = target, options = BuildOptions.None };
            var r = BuildPipeline.BuildPlayer(opt);
            Debug.Log("PROBE_BUILD " + r.summary.result + " " + r.summary.totalSize + " bytes " + r.summary.totalTime + " -> " + path);
            if (r.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
