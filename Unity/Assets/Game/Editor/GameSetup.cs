// GameSetup.cs — создаёт сцену Game.unity (камера, свет, GameRoot с GameApp) и добавляет её первой в Build Settings.
// Запуск: VoxGame → Setup Game Scene (или batch -executeMethod VoxGame.Editor.GameSetup.Setup)
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxGame.Editor
{
    public static class GameSetup
    {
        public const string ScenePath = "Assets/Game/Game.unity";

        [MenuItem("VoxGame/Setup Game Scene")]
        public static void Setup()
        {
            EnsureUgui();
            EnsureSceneDirectory();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Камера + AudioListener ---
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam   = camGo.AddComponent<Camera>();
            cam.allowMSAA        = false;
            cam.allowHDR         = false;
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = new Color(0.08f, 0.08f, 0.12f, 1f);
            cam.nearClipPlane    = 0.05f;
            cam.farClipPlane     = 20f;
            cam.fieldOfView      = 60f;

            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderShadows      = false;
            camData.renderPostProcessing = false;
            camData.antialiasing       = AntialiasingMode.None;

            camGo.AddComponent<AudioListener>(); // только одна AudioListener на сцену

            // --- Направленный свет (без теней) ---
            var lightGo = new GameObject("Sun");
            var light   = lightGo.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.shadows   = LightShadows.None;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

            RenderSettings.ambientMode  = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.25f, 0.30f);

            // --- GameRoot + GameApp ---
            // GameApp — единственный Update игры: первый кадр Play Mode или сборки сам вызывает Init(Camera.main).
            var gameRoot = new GameObject("GameRoot");
            gameRoot.AddComponent<GameApp>();

            // --- Проверка шейдеров и материалов ---
            CheckShaders();

            // --- Сохранить сцену ---
            EditorSceneManager.SaveScene(scene, ScenePath);

            // --- Добавить ПЕРВОЙ в Build Settings (не удаляя остальные) ---
            AddSceneToBuildFirst(ScenePath);

            Debug.Log("GAME_SETUP_OK " + ScenePath);
        }

        // Проверяет/добавляет "com.unity.ugui" в manifest.json.
        static void EnsureUgui()
        {
            var manifestPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));
            if (!File.Exists(manifestPath)) return;

            var text = File.ReadAllText(manifestPath);
            if (text.Contains("\"com.unity.ugui\"")) return;

            const string marker = "\"dependencies\": {";
            int idx = text.IndexOf(marker);
            if (idx < 0) return;

            text = text.Insert(idx + marker.Length,
                "\n    \"com.unity.ugui\": \"2.0.0\",");
            File.WriteAllText(manifestPath, text);
            AssetDatabase.Refresh();
            Debug.Log("VoxGame: добавлен com.unity.ugui 2.0.0 в manifest.json");
        }

        // Гарантирует наличие папки Assets/Game/.
        static void EnsureSceneDirectory()
        {
            var dir = Path.Combine(Application.dataPath, "Game");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        // Проверяет наличие шейдеров, нужных игровому слою.
        static void CheckShaders()
        {
            if (Shader.Find("VoxGame/VertexColor") == null)
                Debug.LogWarning(
                    "VoxGame: шейдер VoxGame/VertexColor не найден. " +
                    "Убедись, что VoxGameVertexColor.shader находится в Assets/Game/.");
            EnsureShaderMaterials();
        }

        // Материалы в Resources держат шейдеры в сборке плеера: Shader.Find сам по себе не включает шейдер,
        // и в плеере вернул бы null. GameUtil.FindShader берёт шейдер из этих материалов, если Find не нашёл.
        [MenuItem("VoxGame/Ensure Shader Materials")]
        public static void EnsureShaderMaterials()
        {
            const string dir = "Assets/Game/Resources/Shaders";
            if (!AssetDatabase.IsValidFolder("Assets/Game/Resources")) AssetDatabase.CreateFolder("Assets/Game", "Resources");
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Game/Resources", "Shaders");
            EnsureMat(dir, GameUtil.VoxMaterial, "VoxGame/VertexColor");
            EnsureMat(dir, GameUtil.AnatMaterial, "VoxProbe/Anatomy");
            EnsureMat(dir, GameUtil.FxAddMaterial, "VoxGame/FxAdditive");
            AssetDatabase.SaveAssets();
        }

        static void EnsureMat(string dir, string name, string shaderName)
        {
            string path = dir + "/" + name + ".mat";
            var sh = Shader.Find(shaderName);
            if (sh == null) { Debug.LogWarning("VoxGame: шейдер " + shaderName + " не найден"); return; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) AssetDatabase.CreateAsset(new Material(sh) { name = name }, path);
            else if (m.shader != sh) { m.shader = sh; EditorUtility.SetDirty(m); }
        }

        // Добавляет сцену первой в Build Settings, сохраняя все существующие.
        static void AddSceneToBuildFirst(string scenePath)
        {
            var current = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            current.RemoveAll(s => s.path == scenePath); // убрать дубликат если был
            current.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = current.ToArray();
        }
    }
}
