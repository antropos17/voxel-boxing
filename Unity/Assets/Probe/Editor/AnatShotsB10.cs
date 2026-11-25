using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxProbe.Editor
{
    /// Этап B, часть 10: снимки парами «0- без правок части 10 | 1- с ними» → Unity/Shots/anatomy-b10/ и парный замер кадра удара.
    /// Правки: кровь ветвями (AnatDamage.Rivulets), русла крови по коже и слои мышц на культе (шейдер, _AnatB10Off), вес отломков без перехлёста (AnatBones.Wide).
    /// Запуск: -executeMethod VoxProbe.Editor.AnatShotsB10.Run / .Bench
    public static class AnatShotsB10
    {
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        static Camera cam; static RenderTexture rt; static AnatView v; static string dir; static Material mat;
        static readonly StringBuilder sb = new StringBuilder();

        public static void Set(bool on)
        {
            AnatBones.Wide = on; AnatDamage.Rivulets = on; Shader.SetGlobalFloat("_AnatB10Off", on ? 0 : 1);
        }

        static void Scene()
        {
            mat = AnatShots.EnsureMaterial();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("cam"); cam = camGo.AddComponent<Camera>();
            var ad = camGo.AddComponent<UniversalAdditionalCameraData>(); ad.renderShadows = false; ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f); cam.nearClipPlane = 0.01f; cam.farClipPlane = 5; cam.fieldOfView = 30;
            var lightGo = new GameObject("sun"); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(35, 150, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            rt = new RenderTexture(900, 1100, 24) { name = "shot" }; cam.targetTexture = rt;
            AnatSkin.Reset(); AnatSkin.Off();
        }

        public static void Run()
        {
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "anatomy-b10")); Directory.CreateDirectory(dir);
            Scene(); sb.Clear(); bool lite = AnatPalette.Lite; AnatPalette.SetLite(false);
            try
            {
                for (int on = 0; on < 2; on++)
                {
                    Set(on == 1); string P = on + "-";
                    // кровь: бой части 5/8 (рассечение брови и нос), 40 и 70 с
                    New(); float t = 0.5f;
                    foreach (var p in new[] { new float3(-3.6f, -0.2f, 0), new float3(-3.0f, -2.2f, 0), new float3(-4.0f, 0.3f, 0), new float3(-3.4f, -4.6f, 0), new float3(-2.2f, 6.6f, 0), new float3(-3.3f, -1.0f, 0) })
                    { Ticks(t); v.Hit(new float3(p.x, p.y, 16), new float3(0.15f, 0, -1), 1); t += 2; }
                    t = 16;
                    foreach (var p in new[] { new float3(-2.6f, 4.0f, 0), new float3(-2.6f, 4.0f, 0), new float3(0.0f, 0.8f, 0), new float3(-2.0f, 4.2f, 0) })
                    { Ticks(t); v.Hit(new float3(p.x, p.y, 16), new float3(0.1f, -0.1f, -1), 1); t += 1.5f; }
                    var mid = new float3(0, -1, 1);
                    Ticks(40); Shot(P + "blood40-front", 0, 3, 0.5f, mid); Shot(P + "blood40-3q", 35, 8, 0.5f, mid);
                    Ticks(70); Shot(P + "blood70-front", 0, 3, 0.5f, mid); Shot(P + "blood70-3q", 35, 8, 0.5f, mid); Shot(P + "blood70-fight", 0, 5, 0.85f, new float3(0, -2, 0));
                    Log(P + "blood", "веток за бой " + v.sim.kidSeq + ", растекание " + v.sim.spreadCells + ", пятна " + v.sim.poolCells);
                    // Ле Фор II и III — удары из части 9
                    var chL = (new float3(3.0f, -2.2f, 14), new float3(-0.15f, 0, -1)); var chR = (new float3(-3.0f, -2.2f, 14), new float3(0.15f, 0, -1)); var mo = (new float3(0, -4.6f, 14), new float3(0, 0, -1));
                    var lf = new[] { chL, chR, chL, chR, chL, chR, mo, chL, chR, chL, mo };
                    var mid9 = new float3(0, 0.3f, 1);
                    for (int lv = 2; lv <= 3; lv++)
                    {
                        New(); t = 0.5f; int n = lv == 2 ? 7 : 11;
                        for (int k = 0; k < n; k++) { Ticks(t); v.Hit(lf[k].Item1, math.normalize(lf[k].Item2), 1); t += 1.2f; }
                        Ticks(t + 1.5f);
                        string nm = P + "lefort" + lv;
                        if (lv == 2) { Shot(nm + "-front", 0, 3, 0.45f, mid9); Shot(nm + "-front-close", 0, 3, 0.25f, new float3(0, 2, 1)); }
                        else { Shot(nm + "-3q", -35, 8, 0.45f, mid9); Shot(nm + "-3q-close", -35, 8, 0.25f, new float3(-1, 3, 1)); }
                        Shot(nm + "-fight", lv == 2 ? 0 : 40, lv == 2 ? 5 : 10, 0.85f, new float3(0, -2, 0));
                        Log(nm, string.Join("; ", v.bones.log));
                    }
                    // культя: сценарий seed 77 до культи
                    New(); var st = AnatBench.Stages(v); var rnd = new Unity.Mathematics.Random(77); int hits = 0;
                    foreach (var s in st)
                    {
                        for (int k = 0; k < s.max && !s.done(v); k++) { if (AnatBench.Strike(v, s.where, ref rnd)) hits++; for (int f = 0; f < 3; f++) v.Tick(1 / 30f, 12); }
                        v.Settle(); for (int f = 0; f < 60; f++) v.Tick(1 / 30f, 64); v.Settle();
                    }
                    Shot(P + "stump-3q", -40, 15, 0.85f, new float3(0, -4, 0)); Shot(P + "stump-top", -15, 70, 0.4f, new float3(0, -10.5f, 0)); Shot(P + "stump-close", -25, 55, 0.25f, new float3(0, -10.5f, -1));
                    Log(P + "stump", "ударов " + hits + ", треуг. " + v.Triangles + ", куски " + v.PieceTriangles + "; поверхность среза: " + Census());
                }
            }
            finally { Set(true); AnatPalette.SetLite(lite); v?.Dispose(); v = null; rt.Release(); }
            File.WriteAllText(Path.Combine(dir, "cases.txt"), sb.ToString());
            Debug.Log("ANAT_B10 shots done\n" + sb);
        }

        /// Открытые ячейки на срезе шеи по материалам (сколько видно мышцы, жира, кости, размозжённой ткани, сгустка).
        static string Census()
        {
            var h = v.a.head; var cnt = new Dictionary<string, int>(); int all = 0;
            for (int z = 1; z < h.dims.z - 1; z++) for (int y = 1; y < h.dims.y - 1; y++) for (int x = 1; x < h.dims.x - 1; x++)
            {
                int i = h.Index(x, y, z); byte m = h.mat[i]; if (!AnatMat.Solid(m) || h.Local(x, y, z).y > AnatDamage.StumpY + 3) continue;
                if (h.mat[i + h.dims.x] != 0) continue;   // открыта сверху
                string k = AnatMat.Table[m].name.Split(' ')[0]; cnt.TryGetValue(k, out int c); cnt[k] = c + 1; all++;
            }
            var l = new List<KeyValuePair<string, int>>(cnt); l.Sort((a, b) => b.Value.CompareTo(a.Value));
            var o = new StringBuilder(); foreach (var kv in l) o.Append(kv.Key).Append(' ').Append((100f * kv.Value / all).ToString("0", Inv)).Append("%, ");
            return o.ToString();
        }

        /// Парный замер кадра удара «без | с» правками части 10 в редакторе: сценарий seed 77 (как AnatShotsB2.Run), прогоны чередуются
        /// 0, 1, 0, 1 …; кадр удара — удар + первый Tick(1/30, 12). Результат — ProbeResults/b10/bench.json.
        public static void Bench() => Bench(3);
        public static void Bench(int rounds)
        {
            var res = new List<double>[2] { new List<double>(), new List<double>() }; var hitOnly = new List<double>[2] { new List<double>(), new List<double>() };
            var fp = new string[2]; var o = new StringBuilder("{\n  \"runs\": [\n");
            mat = AnatShots.EnsureMaterial(); AnatSkin.Reset(); AnatSkin.Off();
            try
            {
                for (int r = 0; r < rounds * 2; r++)
                {
                    int on = r % 2; Set(on == 1);
                    var w = new AnatView(null, new Vector3(0, 1.6f, 0), mat);
                    var st = AnatBench.Stages(w); var rnd = new Unity.Mathematics.Random(77); var sw = new System.Diagnostics.Stopwatch(); int hits = 0; var tris = new List<int>();
                    var mine = new List<double>();
                    foreach (var s in st)
                    {
                        for (int k = 0; k < s.max && !s.done(w); k++)
                        {
                            sw.Restart(); bool ok = AnatBench.Strike(w, s.where, ref rnd); double hm = sw.Elapsed.TotalMilliseconds;
                            sw.Restart(); w.Tick(1 / 30f, 12); double tm = sw.Elapsed.TotalMilliseconds;
                            if (ok) { hits++; res[on].Add(hm + tm); hitOnly[on].Add(hm); mine.Add(hm + tm); }
                            for (int f = 0; f < 2; f++) w.Tick(1 / 30f, 12);
                        }
                        w.Settle(); for (int f = 0; f < 60; f++) w.Tick(1 / 30f, 64); w.Settle(); tris.Add(w.Triangles);
                    }
                    string f0 = hits + " / " + string.Join(" / ", tris) + " / " + w.PieceTriangles; fp[on] = fp[on] ?? f0;
                    mine.Sort();
                    o.Append("    {\"on\": ").Append(on).Append(", \"fingerprint\": \"").Append(f0).Append("\", \"mean\": ").Append(Avg(mine).ToString("0.###", Inv))
                     .Append(", \"median\": ").Append(mine[mine.Count / 2].ToString("0.###", Inv)).Append(", \"worst\": ").Append(mine[mine.Count - 1].ToString("0.###", Inv)).Append("},\n");
                    if (fp[on] != f0) Debug.LogError("ANAT_B10 fingerprint differs: " + fp[on] + " vs " + f0);
                    w.Dispose();
                }
            }
            finally { Set(true); }
            o.Length -= 2; o.Append("\n  ],\n");
            for (int on = 0; on < 2; on++)
            {
                var a = res[on]; a.Sort(); var b = hitOnly[on]; b.Sort();
                o.Append("  \"").Append(on == 0 ? "off" : "on").Append("\": {\"n\": ").Append(a.Count).Append(", \"mean\": ").Append(Avg(a).ToString("0.###", Inv))
                 .Append(", \"median\": ").Append(a[a.Count / 2].ToString("0.###", Inv)).Append(", \"p90\": ").Append(a[a.Count * 9 / 10].ToString("0.###", Inv))
                 .Append(", \"worst\": ").Append(a[a.Count - 1].ToString("0.###", Inv)).Append(", \"hitMean\": ").Append(Avg(b).ToString("0.###", Inv))
                 .Append(", \"fingerprint\": \"").Append(fp[on]).Append("\"},\n");
            }
            o.Length -= 2; o.Append("\n}\n");
            string pd = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ProbeResults", "b10")); Directory.CreateDirectory(pd);
            File.WriteAllText(Path.Combine(pd, "bench.json"), o.ToString());
            Debug.Log("ANAT_B10 bench\n" + o);
        }

        static double Avg(List<double> a) { double s = 0; foreach (var x in a) s += x; return a.Count > 0 ? s / a.Count : 0; }

        static void New() { v?.Dispose(); v = new AnatView(null, new Vector3(0, 1.6f, 0), mat); }
        static void Ticks(float until) { while (v.sim.time < until) v.Tick(1 / 30f, 64); v.Settle(); }
        static void Log(string k, string s) => sb.Append(k).Append(": ").Append(s).Append('\n');

        static void Shot(string name, float yaw, float pitch, float dist, float3 at)
        {
            Vector3 piv = v.ToWorld(at);
            cam.transform.position = piv + Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward * dist; cam.transform.LookAt(piv);
            Shader.SetGlobalFloat("_AnatTime", v.sim.time);
            cam.Render();
            RenderTexture.active = rt;
            var tx = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tx.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tx.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tx.EncodeToPNG());
            Object.DestroyImmediate(tx); RenderTexture.active = null;
        }
    }
}
