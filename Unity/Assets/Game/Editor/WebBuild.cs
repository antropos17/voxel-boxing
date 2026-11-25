// WebBuild.cs — пакетная сборка игры (Game.unity) под Web для GitHub Pages: Builds/Web/ (index.html + Build/).
// Запуск: batch -buildTarget WebGL -executeMethod VoxGame.Editor.WebBuild.BuildWebGL (как AnatSetup.Build*KeepScene: сцену не пересоздаёт).
// Настройки ставятся здесь же, только для Web (другие платформы не трогает); исходные и новые значения — в лог строкой WEB_SET.
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VoxGame.Editor
{
    public static class WebBuild
    {
        public const string OutPath = "Builds/Web";

        public static void BuildWebGL()
        {
            Debug.Log("WEB_SET before " + Describe());
            Apply();
            Debug.Log("WEB_SET after  " + Describe());
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { GameSetup.ScenePath }, locationPathName = OutPath, target = BuildTarget.WebGL, options = BuildOptions.None });
            Debug.Log("WEB_BUILD " + r.summary.result + " " + r.summary.totalSize + " bytes " + r.summary.totalTime + " -> " + OutPath);
            if (r.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
            foreach (var f in Directory.GetFiles(Path.Combine(OutPath, "Build"))) Debug.Log("WEB_FILE " + Path.GetFileName(f) + " " + new FileInfo(f).Length);
        }

        // GitHub Pages не шлёт Content-Encoding для .br, поэтому Brotli + распаковка в JS (Decompression Fallback, файлы .unityweb).
        // Скорость важнее размера (кадр удара): IL2CPP и wasm — на скорость; Wasm 2023 (SIMD, родные исключения) — Chrome 91+, Firefox 89+, Safari 16.4+.
        // Исключения: только явные — в коде есть try/catch (защита кадра, сохранение). Куча: старт под пик боя, рост геометрический.
        static void Apply()
        {
            var t = NamedBuildTarget.WebGL;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.wasm2023 = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.WebGL.maximumMemorySize = 2048;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.template = "PROJECT:VoxFull";
            PlayerSettings.SetManagedStrippingLevel(t, ManagedStrippingLevel.High);
            PlayerSettings.SetIl2CppCodeGeneration(t, Il2CppCodeGeneration.OptimizeSpeed);
            SetCodeOptimization("RuntimeSpeedLTO");
            AssetDatabase.SaveAssets();
        }

        // UnityEditor.WebGL.UserBuildSettings живёт в модуле Web и хранится в Library/ (не в репозитории) — ставим при каждой сборке.
        static System.Type UBS() => System.Type.GetType("UnityEditor.WebGL.UserBuildSettings, UnityEditor.WebGL.Extensions");
        static void SetCodeOptimization(string v)
        {
            var p = UBS()?.GetProperty("codeOptimization");
            if (p == null) { Debug.LogWarning("WEB_SET нет UserBuildSettings.codeOptimization (модуль Web?)"); return; }
            p.SetValue(null, System.Enum.Parse(p.PropertyType, v));
        }

        static string Describe()
        {
            var t = NamedBuildTarget.WebGL;
            return "compression=" + PlayerSettings.WebGL.compressionFormat + " fallback=" + PlayerSettings.WebGL.decompressionFallback + " stripEngine=" + PlayerSettings.stripEngineCode
                + " managedStrip=" + PlayerSettings.GetManagedStrippingLevel(t) + " il2cppGen=" + PlayerSettings.GetIl2CppCodeGeneration(t)
                + " codeOpt=" + UBS()?.GetProperty("codeOptimization")?.GetValue(null) + " api=" + PlayerSettings.GetApiCompatibilityLevel(t)
                + " exceptions=" + PlayerSettings.WebGL.exceptionSupport + " wasm2023=" + PlayerSettings.WebGL.wasm2023 + " dataCaching=" + PlayerSettings.WebGL.dataCaching + " symbols=" + PlayerSettings.WebGL.debugSymbolMode
                + " mem=" + PlayerSettings.WebGL.initialMemorySize + "/" + PlayerSettings.WebGL.maximumMemorySize + " growth=" + PlayerSettings.WebGL.memoryGrowthMode + " template=" + PlayerSettings.WebGL.template
                + " targetFps=" + Application.targetFrameRate + " vsync=" + QualitySettings.vSyncCount;
        }
    }
}
