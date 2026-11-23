using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static VoxProbe.AnatBench;

namespace VoxProbe.Editor
{
    /// Этап B, часть 2: разрушение головы до культи через просмотр (AnatView) — тяжёлые удары по стадиям: челюсть, лицо, свод,
    /// половина головы, культя. Снимки после каждой стадии и замер → Unity/Shots/anatomy-b/destroy/ (+ destroy.json).
    /// Запуск: unity command script eval 'VoxProbe.Editor.AnatShotsB2.Run();'
    public static class AnatShotsB2
    {
        public const string OutDir = "Shots/anatomy-b/destroy";
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        static string F(double v) => v.ToString("0.##", Inv);
        static void Comma(StringBuilder sb) { if (sb.Length > 1 && sb[sb.Length - 2] == ',') sb.Length -= 2; }

        static Camera cam; static RenderTexture rt; static AnatView v;

        static void Shot(string name, float yaw, float pitch, float dist, float3 at)
        {
            Vector3 piv = v.ToWorld(at);
            cam.transform.position = piv + Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward * dist; cam.transform.LookAt(piv);
            Shader.SetGlobalFloat("_AnatTime", v.sim.time);
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); t.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), t.EncodeToPNG());
            Object.DestroyImmediate(t); RenderTexture.active = null;
        }

        /// Только свод: тяжёлые удары в темя до вскрытия и снимки мозга в проломе крупно.
        public static void Vault(int hits = 12)
        {
            Scene();
            var rnd = new Unity.Mathematics.Random(3);
            System.Func<float3, bool> top = p => p.y > 8f && math.abs(p.x) < 3.5f && p.z > -4 && p.z < 1;
            for (int k = 0; k < hits; k++) { Strike(v, top, ref rnd); v.Tick(1 / 30f, 12); }
            v.Settle(); for (int f = 0; f < 60; f++) v.Tick(1 / 30f, 64); v.Settle();
            Shot("v-top", -10, 75, 0.32f, new float3(0, 9, -1.5f));
            Shot("v-close", -25, 55, 0.16f, new float3(0, 9.5f, -1.5f));
            Shot("v-side", -70, 30, 0.3f, new float3(0, 9, -1.5f));
            File.WriteAllText(Path.Combine(OutDir, "vault.txt"), "brainOpen " + BrainOpen(v) + " fractures " + v.sim.fractures.Count + " rips " + v.sim.ripCount + " brainHits " + v.sim.brainHits + " pieces " + v.pieces.list.Count + "\n" + string.Join("\n", v.sim.events));
            v.Dispose(); v = null; rt.Release();
        }

        static void Scene()
        {
            Directory.CreateDirectory(OutDir);
            var mat = AnatShots.EnsureMaterial();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("cam"); cam = camGo.AddComponent<Camera>();
            var ad = camGo.AddComponent<UniversalAdditionalCameraData>(); ad.renderShadows = false; ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f); cam.nearClipPlane = 0.01f; cam.farClipPlane = 5; cam.fieldOfView = 30;
            var lightGo = new GameObject("sun"); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(35, 150, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            rt = new RenderTexture(900, 1100, 24) { name = "shot" }; cam.targetTexture = rt;
            AnatSkin.Reset(); AnatSkin.Off();
            v = new AnatView(null, new Vector3(0, 1.6f, 0), mat);
        }

        public static void Run()
        {
            Scene();
            var sb = new StringBuilder("{\n  \"stages\": [\n");
            int tris0 = v.Triangles;
            Shot("0-intact", -35, 8, 0.8f, new float3(0, -2, 0));
            var stages = Stages(v); var rnd = new Unity.Mathematics.Random(77);
            double worst = 0, hitMax = 0; int hitsAll = 0;
            foreach (var st in stages)
            {
                int k = 0, miss = 0;
                for (; k < st.max && !st.done(v); k++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    bool ok = Strike(v, st.where, ref rnd);
                    double hm = sw.Elapsed.TotalMilliseconds; hitMax = math.max(hitMax, hm);
                    if (!ok) miss++; else hitsAll++;
                    for (int f = 0; f < 3; f++) { sw.Restart(); v.Tick(1 / 30f, 12); worst = math.max(worst, sw.Elapsed.TotalMilliseconds + (f == 0 ? hm : 0)); }
                }
                v.Settle(); for (int f = 0; f < 60; f++) v.Tick(1 / 30f, 64); v.Settle();
                sb.Append("    {\"stage\": \"").Append(st.name).Append("\", \"hits\": ").Append(k).Append(", \"miss\": ").Append(miss).Append(", \"done\": ").Append(st.done(v) ? "true" : "false")
                  .Append(", \"tris\": ").Append(v.Triangles).Append(", \"pieces\": ").Append(v.pieces.list.Count).Append(", \"pieceTris\": ").Append(v.PieceTriangles)
                  .Append(", \"made\": ").Append(v.pieces.made).Append(", \"trimmed\": ").Append(v.pieces.trimmed).Append(", \"bodiesOff\": ").Append(v.detachedBodies)
                  .Append(", \"fractures\": ").Append(v.sim.fractures.Count).Append(", \"rips\": ").Append(v.sim.ripCount).Append(", \"brainHits\": ").Append(v.sim.brainHits).Append("},\n");
                Shot(st.name + "-front", 0, 5, 0.85f, new float3(0, -4, 0));
                Shot(st.name + "-3q", -40, 15, 0.85f, new float3(0, -4, 0));
                if (st.name == "3-vault") { Shot(st.name + "-brain-top", -20, 60, 0.5f, new float3(0, 8, 0)); Shot(st.name + "-brain-close", -30, 50, 0.3f, new float3(0, 8.5f, 0)); }
                if (st.name == "5-stump") { Shot(st.name + "-top", -15, 70, 0.4f, new float3(0, -10.5f, 0)); Shot(st.name + "-close", -25, 55, 0.25f, new float3(0, -10.5f, -1)); Shot(st.name + "-floor", -30, 25, 1.0f, new float3(0, -20, 0)); }
            }
            sb.Length -= 2; sb.Append("\n  ],\n");
            sb.Append("  \"trisIntact\": ").Append(tris0).Append(", \"hits\": ").Append(hitsAll).Append(", \"hitMsMax\": ").Append(F(hitMax)).Append(", \"worstFrameMs\": ").Append(F(worst))
              .Append(", \"islandMsMax\": ").Append(F(v.islMsMax)).Append(", \"islandRequests\": ").Append(v.isl.requests).Append(", \"islandFull\": ").Append(v.isl.fullPasses)
              .Append(", \"piecesPeak\": ").Append(v.pieces.peak).Append(", \"crumbs\": ").Append(v.crumbs).Append(",\n");
            sb.Append("  \"left\": [");
            { int k = 0; foreach (var g in v.a.All) { if (g != v.a.head && !v.gone[k]) { int n = Solid(g); if (n > 0) sb.Append("\"").Append(g.name).Append(" ").Append(n).Append(" bad ").Append(F(v.SupportBad(g))).Append("\", "); } k++; } }
            Comma(sb); sb.Append("],\n  \"pieceList\": [");
            foreach (var p in v.pieces.list) sb.Append("\"").Append(p.what).Append(" ").Append(p.cells).Append(" y ").Append(F(p.x.y)).Append(p.asleep ? " sleep" : "").Append(p.ready ? "" : " notready").Append(" age ").Append(F(p.age)).Append("\", ");
            Comma(sb); sb.Append("],\n");
            sb.Append("  \"events\": [\n"); int ne = 0; foreach (var e in v.sim.events) { if (ne++ > 80) break; sb.Append("    \"").Append(e.Replace("\"", "'")).Append("\",\n"); } sb.Length -= 2; sb.Append("\n  ]\n}\n");
            File.WriteAllText(Path.Combine(OutDir, "destroy.json"), sb.ToString());
            v.Dispose(); v = null; rt.Release();
            Debug.Log("ANAT_B2_OK");
        }
    }
}
