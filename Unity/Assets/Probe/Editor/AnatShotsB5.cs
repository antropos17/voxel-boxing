using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static VoxProbe.AnatBench;

namespace VoxProbe.Editor
{
    /// Этап B, часть 5: снимки вида повреждений «до / после» с одних ракурсов и стадий → Unity/Shots/anatomy-b5/<tag>/.
    /// 1) бой: средние удары в скулу, бровь, щёку (синяк, отёк), затем в бровь и нос (рассечение, кровь); снимки на 15, 40 и 70 с;
    /// 2) разрушение seed 77 как AnatShotsB2.Run (те же удары и тики): снимки стадий, трещины, мозг, культя, числа и время кадра.
    /// Запуск: unity command script eval 'VoxProbe.Editor.AnatShotsB5.Run("after"); return "ok";'
    public static class AnatShotsB5
    {
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        static string F(double v) => v.ToString("0.##", Inv);
        static Camera cam; static RenderTexture rt, rtBig; static AnatView v; static string dir;

        static void Scene()
        {
            var mat = AnatShots.EnsureMaterial();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("cam"); cam = camGo.AddComponent<Camera>();
            var ad = camGo.AddComponent<UniversalAdditionalCameraData>(); ad.renderShadows = false; ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f); cam.nearClipPlane = 0.01f; cam.farClipPlane = 5; cam.fieldOfView = 30;
            var lightGo = new GameObject("sun"); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(35, 150, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            rt = new RenderTexture(900, 1100, 24) { name = "shot" }; rtBig = new RenderTexture(1080, 2340, 24) { name = "frame" };
            cam.targetTexture = rt;
            AnatSkin.Reset(); AnatSkin.Off();
            v = new AnatView(null, new Vector3(0, 1.6f, 0), mat);
        }

        static void Aim(float yaw, float pitch, float dist, float3 at)
        {
            Vector3 piv = v.ToWorld(at);
            cam.transform.position = piv + Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward * dist; cam.transform.LookAt(piv);
        }

        static void Shot(string name, float yaw, float pitch, float dist, float3 at)
        {
            Aim(yaw, pitch, dist, at);
            Grab(name);
        }

        static void Grab(string name)
        {
            Shader.SetGlobalFloat("_AnatTime", v.sim.time);
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); t.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), t.EncodeToPNG());
            Object.DestroyImmediate(t); RenderTexture.active = null;
        }

        /// Камера на точку c (см головы) снаружи вдоль направления от центра головы.
        static void ShotAt(string name, float3 c, float dist)
        {
            float3 o = math.normalizesafe(c - new float3(0, 1, 0), new float3(0, 0, 1));
            Vector3 piv = v.ToWorld(c);
            cam.transform.position = piv + (Vector3)o * dist; cam.transform.LookAt(piv, Vector3.up);
            Grab(name);
        }

        /// Время кадра: голова в портретном кадре телефона 1080×2340, 24 отрисовки, каждая с ожиданием GPU (чтение 1 пикселя). Медиана и худшее, мс.
        static string FrameMs()
        {
            Aim(-30, 10, 0.55f, new float3(0, -2, 0));
            cam.targetTexture = rtBig;
            var px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var l = new List<double>(); var sw = new System.Diagnostics.Stopwatch();
            for (int k = 0; k < 28; k++)
            {
                sw.Restart();
                cam.Render();
                RenderTexture.active = rtBig; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply(); RenderTexture.active = null;
                if (k >= 4) l.Add(sw.Elapsed.TotalMilliseconds);
            }
            Object.DestroyImmediate(px); cam.targetTexture = rt;
            l.Sort();
            return "{\"median\": " + F(l[l.Count / 2]) + ", \"worst\": " + F(l[l.Count - 1]) + "}";
        }

        static void Ticks(float until) { while (v.sim.time < until) v.Tick(1 / 30f, 64); v.Settle(); }

        static void Fight(StringBuilder sb)
        {
            // средние удары по правой стороне лица (x < 0 — к камере «3q»): скула, щека, челюсть, лоб, через 2 с
            float3[] bruise = { new float3(-3.6f, -0.2f, 0), new float3(-3.0f, -2.2f, 0), new float3(-4.0f, 0.3f, 0), new float3(-3.4f, -4.6f, 0), new float3(-2.2f, 6.6f, 0), new float3(-3.3f, -1.0f, 0) };
            float t = 0.5f;
            foreach (var p in bruise) { Ticks(t); v.Hit(new float3(p.x, p.y, 16), new float3(0.15f, 0, -1), 1); t += 2; }
            Ticks(15);
            Shot("f15-front", 0, 5, 0.85f, new float3(0, -2, 0));
            Shot("f15-3q", -40, 10, 0.85f, new float3(0, -2, 0));
            Shot("f15-bruise", -35, 5, 0.36f, new float3(-3.2f, -1f, 6));
            // ещё средние: бровь и нос (рассечение и кровь)
            float3[] cut = { new float3(-2.6f, 4.0f, 0), new float3(-2.6f, 4.0f, 0), new float3(0.0f, 0.8f, 0), new float3(-2.0f, 4.2f, 0) };
            t = 16;
            foreach (var p in cut) { Ticks(t); v.Hit(new float3(p.x, p.y, 16), new float3(0.1f, -0.1f, -1), 1); t += 1.5f; }
            Ticks(40);
            Shot("f40-front", 0, 5, 0.85f, new float3(0, -2, 0));
            Shot("f40-3q", -40, 10, 0.85f, new float3(0, -2, 0));
            Shot("f40-blood", -20, 0, 0.36f, new float3(-1.5f, 0, 6));
            Ticks(70);
            Shot("f70-front", 0, 5, 0.85f, new float3(0, -2, 0));
            Shot("f70-3q", -40, 10, 0.85f, new float3(0, -2, 0));
            Shot("f70-bruise", -35, 5, 0.36f, new float3(-3.2f, -1f, 6));
            sb.Append("  \"fight\": {\"tris\": ").Append(v.Triangles).Append(", \"wounds\": ").Append(v.sim.wounds.Count).Append(", \"drips\": ").Append(v.sim.drips.Count)
              .Append(", \"frameMs\": ").Append(FrameMs()).Append("},\n");
            v.Dispose(); v = null;
        }

        static List<double> Frames(int n)
        {
            var px = new Texture2D(1, 1, TextureFormat.RGBA32, false); var l = new List<double>(); var sw = new System.Diagnostics.Stopwatch();
            cam.targetTexture = rtBig;
            for (int k = 0; k < n + 3; k++)
            {
                sw.Restart(); cam.Render();
                RenderTexture.active = rtBig; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply(); RenderTexture.active = null;
                if (k >= 3) l.Add(sw.Elapsed.TotalMilliseconds);
            }
            Object.DestroyImmediate(px); cam.targetTexture = rt; return l;
        }

        /// Время кадра старого (временный AnatomyOld.shader = шейдер до части 5) и нового шейдера поочерёдно на одной сцене:
        /// целая голова, затем конец каждой стадии seed 77; 8 раундов по 20 кадров (порядок шейдеров чередуется) 1080×2340 на каждый шейдер. Медианы, мс.
        public static string CompareShaders()
        {
            var sb = new StringBuilder("{\n");
            Scene();
            var mat = AnatShots.EnsureMaterial(); var nw = mat.shader; var old = Shader.Find("VoxProbe/AnatomyOld");
            void Measure(string name)
            {
                Aim(-30, 10, 0.55f, new float3(0, -2, 0));
                var a = new List<double>(); var b = new List<double>();
                for (int r = 0; r < 8; r++) for (int o = 0; o < 2; o++) { bool isOld = (o == 0) == (r % 2 == 0); mat.shader = isOld ? old : nw; (isOld ? b : a).AddRange(Frames(20)); }   // порядок чередуется
                a.Sort(); b.Sort();
                sb.Append("  \"").Append(name).Append("\": {\"old\": ").Append(F(b[b.Count / 2])).Append(", \"new\": ").Append(F(a[a.Count / 2]))
                  .Append(", \"oldP90\": ").Append(F(b[b.Count * 9 / 10])).Append(", \"newP90\": ").Append(F(a[a.Count * 9 / 10])).Append("},\n");
            }
            Measure("0-intact");
            var stages = Stages(v); var rnd = new Unity.Mathematics.Random(77);
            foreach (var st in stages)
            {
                for (int k = 0; k < st.max && !st.done(v); k++) { Strike(v, st.where, ref rnd); for (int f = 0; f < 3; f++) v.Tick(1 / 30f, 12); }
                v.Settle(); for (int f = 0; f < 60; f++) v.Tick(1 / 30f, 64); v.Settle();
                Measure(st.name);
            }
            mat.shader = nw;
            sb.Length -= 2; sb.Append("\n}\n");
            v.Dispose(); v = null; rt.Release(); rtBig.Release();
            return sb.ToString();
        }

        public static void Run(string tag)
        {
            dir = Path.Combine("Shots/anatomy-b5", tag); Directory.CreateDirectory(dir);
            var sb = new StringBuilder("{\n");
            Scene();
            sb.Append("  \"intactFrameMs\": ").Append(FrameMs()).Append(",\n");
            Shot("0-intact-front", 0, 5, 0.85f, new float3(0, -2, 0));
            Fight(sb);
            v = new AnatView(null, new Vector3(0, 1.6f, 0), AnatShots.EnsureMaterial());
            sb.Append("  \"stages\": [\n");
            var stages = Stages(v); var rnd = new Unity.Mathematics.Random(77);
            int hitsAll = 0, fr0 = 0;
            foreach (var st in stages)
            {
                int k = 0;
                for (; k < st.max && !st.done(v); k++)
                {
                    if (Strike(v, st.where, ref rnd)) hitsAll++;
                    for (int f = 0; f < 3; f++) v.Tick(1 / 30f, 12);
                }
                v.Settle(); for (int f = 0; f < 60; f++) v.Tick(1 / 30f, 64); v.Settle();
                sb.Append("    {\"stage\": \"").Append(st.name).Append("\", \"hits\": ").Append(k).Append(", \"tris\": ").Append(v.Triangles)
                  .Append(", \"pieceTris\": ").Append(v.PieceTriangles).Append(", \"fractures\": ").Append(v.sim.fractures.Count)
                  .Append(", \"frameMs\": ").Append(FrameMs()).Append("},\n");
                Shot(st.name + "-front", 0, 5, 0.85f, new float3(0, -4, 0));
                Shot(st.name + "-3q", -40, 15, 0.85f, new float3(0, -4, 0));
                // трещины: крупно у переломов этой стадии
                for (int q = fr0; q < v.sim.fractures.Count && q < fr0 + 2; q++) ShotAt(st.name + "-crack" + (q - fr0), v.sim.fractures[q], 0.2f);
                fr0 = v.sim.fractures.Count;
                if (st.name == "3-vault") { Shot(st.name + "-brain-top", -20, 60, 0.5f, new float3(0, 8, 0)); Shot(st.name + "-brain-close", -30, 50, 0.3f, new float3(0, 8.5f, 0)); }
                if (st.name == "5-stump") { Shot(st.name + "-top", -15, 70, 0.4f, new float3(0, -10.5f, 0)); Shot(st.name + "-close", -25, 55, 0.25f, new float3(0, -10.5f, -1)); }
            }
            sb.Length -= 2; sb.Append("\n  ],\n");
            sb.Append("  \"hits\": ").Append(hitsAll).Append(", \"stumpTris\": ").Append(v.Triangles).Append(", \"pieceTris\": ").Append(v.PieceTriangles).Append(",\n");
            sb.Append("  \"fractureList\": [");
            foreach (var f in v.sim.fractures) sb.Append("[").Append(F(f.x)).Append(", ").Append(F(f.y)).Append(", ").Append(F(f.z)).Append("], ");
            if (v.sim.fractures.Count > 0) sb.Length -= 2;
            sb.Append("]\n}\n");
            File.WriteAllText(Path.Combine(dir, "b5.json"), sb.ToString());
            v.Dispose(); v = null; rt.Release(); rtBig.Release();
            Debug.Log("ANAT_B5_OK " + tag);
        }
    }
}
