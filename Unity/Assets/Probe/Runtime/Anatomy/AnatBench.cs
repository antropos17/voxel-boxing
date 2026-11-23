using System.Collections;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

namespace VoxProbe
{
    /// Сценарий разрушения головы до культи (этап B), общий для редактора (AnatShotsB2) и игрока (-anatBench).
    /// Стадии и удары перенесены из Editor/AnatShotsB2.cs без изменений: те же условия, те же случайные числа.
    public static class AnatBench
    {
        public struct Stage { public string name; public System.Func<float3, bool> where; public int max; public System.Func<AnatView, bool> done; }

        /// Твёрдых ячеек головы в области (каждая вторая по осям — для условий стадий).
        public static int Count(AnatView v, System.Func<float3, bool> where)
        {
            var h = v.a.head; int n = 0;
            for (int z = 0; z < h.dims.z; z += 2) for (int y = 0; y < h.dims.y; y += 2) for (int x = 0; x < h.dims.x; x += 2)
            { byte m = h.mat[h.Index(x, y, z)]; if (AnatMat.Cull(m) && where(h.Local(x, y, z))) n++; }
            return n;
        }

        /// Открытого мозга (кора и белое вещество с пустым соседом) в своде.
        public static int BrainOpen(AnatView v)
        {
            var h = v.a.head; int n = 0;
            for (int z = 1; z < h.dims.z - 1; z++) for (int y = 1; y < h.dims.y - 1; y++) for (int x = 1; x < h.dims.x - 1; x++)
            {
                int i = h.Index(x, y, z); float3 lp = h.Local(x, y, z); if (!AnatDamage.Brainy(h.mat[i]) || lp.y < 7 || lp.z > 2 || lp.z < -5) continue;
                if (h.mat[i + 1] == 0 || h.mat[i - 1] == 0 || h.mat[i + h.dims.x] == 0 || h.mat[i - h.dims.x] == 0 || h.mat[i + h.dims.x * h.dims.y] == 0 || h.mat[i - h.dims.x * h.dims.y] == 0) n++;
            }
            return n;
        }

        public static Stage[] Stages(AnatView v)
        {
            int face0 = Count(v, Face), left0 = Count(v, Left), top0 = Count(v, Above), mand0 = Solid(v.a.mand);
            return new[]
            {
                new Stage { name = "1-jaw", max = 40, where = p => p.y < -3.5f && p.y > -10 && p.z > 1, done = w => Solid(w.a.mand) < mand0 * 0.15f },
                new Stage { name = "2-face", max = 80, where = Face, done = w => Count(w, Face) < face0 * 0.12f },
                new Stage { name = "3-vault", max = 40, where = p => p.y > 7.5f && math.abs(p.x) < 4 && p.z > -5 && p.z < 2, done = w => BrainOpen(w) > 600 },
                new Stage { name = "4-half", max = 120, where = Left, done = w => Count(w, Left) < left0 * 0.1f },
                new Stage { name = "5-stump", max = 300, where = Above, done = w => Count(w, Above) < top0 * 0.003f },
            };
        }
        public static int Solid(AnatGrid g) { int n = 0; for (int i = 0; i < g.Count; i++) if (AnatMat.Solid(g.mat[i])) n++; return n; }
        public static bool Face(float3 p) => p.z > 4.5f && math.abs(p.x) < 5.5f && p.y > -6 && p.y < 6;
        public static bool Left(float3 p) => p.x > 0.6f && p.y > AnatDamage.StumpY + 1.5f;
        public static bool Above(float3 p) => p.y > AnatDamage.StumpY + 1.5f;

        /// Тяжёлый удар по случайной открытой ячейке головы в области where: луч снаружи вдоль направления от центра головы.
        public static bool Strike(AnatView v, System.Func<float3, bool> where, ref Unity.Mathematics.Random rnd, int str = 2)
        {
            var h = v.a.head;
            for (int t = 0; t < 4000; t++)
            {
                int3 c = rnd.NextInt3(1, h.dims - 1); int i = h.Index(c.x, c.y, c.z); byte m = h.mat[i];
                if (!AnatMat.Cull(m) || m == AnatMat.Covered) continue;
                float3 p = h.Local(c.x, c.y, c.z); if (!where(p) || AnatDamage.Protected(p)) continue;
                if (!(h.mat[i + 1] == 0 || h.mat[i - 1] == 0 || h.mat[i + h.dims.x] == 0 || h.mat[i - h.dims.x] == 0 || h.mat[i + h.dims.x * h.dims.y] == 0 || h.mat[i - h.dims.x * h.dims.y] == 0)) continue;
                float3 o = math.normalizesafe(p - new float3(0, 1, 0), new float3(0, 0, 1));
                var ray = new Ray(v.ToWorld(p + o * 25), -(Vector3)o);
                if (!v.Pick(ray, out var hp, out _, out _)) continue;
                v.Hit(hp, v.pickDir, str); return true;
            }
            return false;
        }
    }

    /// Автозамер в игроке: -anatBench [-anatBenchOut файл.json] [-anatBenchSeed N] [-anatBenchNoQuit].
    /// Сценарий как AnatShotsB2.Run (seed 77, после каждого удара Tick(1/30, 12) три раза), но по одному вызову на кадр Unity:
    /// камера и отрисовка работают. Удар и Tick меряются Stopwatch (как в редакторе), кадр — по Time.unscaledDeltaTime.
    public class AnatBenchRunner : MonoBehaviour
    {
        public Material anatMaterial;
        public string outPath; public bool quit = true; public uint seed = 77;
        AnatView v; Camera cam;
        static string F(double d) => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        float[] hitMs = new float[1024], tickMs = new float[4096], frameMs = new float[8192], worstMs = new float[1024];   // worst: удар + первый Tick
        int nHit, nTick, nFrame, nWorst;

        static float Med(float[] a, int n) { if (n == 0) return 0; var c = new float[n]; System.Array.Copy(a, c, n); System.Array.Sort(c); return n % 2 == 1 ? c[n / 2] : (c[n / 2 - 1] + c[n / 2]) * 0.5f; }
        static float Max(float[] a, int n) { float m = 0; for (int i = 0; i < n; i++) m = Mathf.Max(m, a[i]); return m; }
        static void Add(ref float[] a, ref int n, float x) { if (n == a.Length) System.Array.Resize(ref a, n * 2); a[n++] = x; }

        void Start()
        {
            Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
            cam = Camera.main;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f); cam.nearClipPlane = 0.01f; cam.farClipPlane = 5; cam.fieldOfView = 30;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-anatBenchOut" && i + 1 < args.Length) outPath = args[++i];
                else if (args[i] == "-anatBenchSeed" && i + 1 < args.Length) seed = uint.Parse(args[++i]);
                else if (args[i] == "-anatBenchNoQuit") quit = false;
            }
            if (string.IsNullOrEmpty(outPath)) outPath = Path.Combine(Application.persistentDataPath, "anat-bench-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            StartCoroutine(Run());
        }

        void View()
        {
            Vector3 piv = v.ToWorld(new float3(0, -2, 0));
            cam.transform.position = piv + Quaternion.Euler(-8, -35, 0) * Vector3.forward * 0.8f; cam.transform.LookAt(piv);
        }

        IEnumerator Run()
        {
            AnatSkin.Reset(); AnatSkin.Off();
            for (int i = 0; i < 5; i++) yield return null;
            System.GC.Collect(); yield return null;
            long mono0 = Profiler.GetMonoUsedSizeLong(), tot0 = Profiler.GetTotalAllocatedMemoryLong();
            sw.Restart(); v = new AnatView(null, new Vector3(0, 1.6f, 0), anatMaterial); double buildMs = sw.Elapsed.TotalMilliseconds;
            View(); yield return null; yield return null;
            long mono1 = Profiler.GetMonoUsedSizeLong(), tot1 = Profiler.GetTotalAllocatedMemoryLong();
            int gc0 = System.GC.CollectionCount(0);
            var sb = new StringBuilder("{\n  \"stages\": [\n");
            int tris0 = v.Triangles; var stages = Stages(v); var rnd = new Unity.Mathematics.Random(seed);
            int hitsAll = 0; double t0 = Time.realtimeSinceStartupAsDouble;
            foreach (var st in stages)
            {
                int k = 0, miss = 0;
                for (; k < st.max && !st.done(v); k++)
                {
                    // кадр 0: удар и первый Tick (как в редакторе); кадры 1, 2: ещё по Tick. Между вызовами — кадр Unity.
                    sw.Restart(); bool ok = AnatBench.Strike(v, st.where, ref rnd); double hm = sw.Elapsed.TotalMilliseconds;
                    if (!ok) miss++; else { hitsAll++; Add(ref hitMs, ref nHit, (float)hm); }
                    for (int f = 0; f < 3; f++)
                    {
                        sw.Restart(); v.Tick(1 / 30f, 12); double tm = sw.Elapsed.TotalMilliseconds;
                        Add(ref tickMs, ref nTick, (float)tm); if (f == 0) Add(ref worstMs, ref nWorst, (float)(tm + hm));
                        Shader.SetGlobalFloat("_AnatTime", v.sim.time); View();
                        yield return null;
                        Add(ref frameMs, ref nFrame, Time.unscaledDeltaTime * 1000f);
                    }
                }
                v.Settle(); for (int f = 0; f < 60; f++) v.Tick(1 / 30f, 64); v.Settle();
                yield return null;
                sb.Append("    {\"stage\": \"").Append(st.name).Append("\", \"hits\": ").Append(k).Append(", \"miss\": ").Append(miss).Append(", \"done\": ").Append(st.done(v) ? "true" : "false")
                  .Append(", \"tris\": ").Append(v.Triangles).Append(", \"pieces\": ").Append(v.pieces.list.Count).Append(", \"pieceTris\": ").Append(v.PieceTriangles)
                  .Append(", \"made\": ").Append(v.pieces.made).Append(", \"trimmed\": ").Append(v.pieces.trimmed).Append(", \"bodiesOff\": ").Append(v.detachedBodies).Append("},\n");
            }
            sb.Length -= 2; sb.Append("\n  ],\n");
            long mono2 = Profiler.GetMonoUsedSizeLong(), tot2 = Profiler.GetTotalAllocatedMemoryLong();
            sb.Append("  \"platform\": \"").Append(Application.platform).Append("\", \"seed\": ").Append(seed).Append(", \"cpu\": \"").Append(SystemInfo.processorType.Replace("\"", "'")).Append("\", \"gpu\": \"").Append(SystemInfo.graphicsDeviceName.Replace("\"", "'")).Append("\", \"res\": \"").Append(Screen.width).Append('x').Append(Screen.height).Append("\",\n");
            sb.Append("  \"hits\": ").Append(hitsAll).Append(", \"trisIntact\": ").Append(tris0).Append(", \"trisStump\": ").Append(v.Triangles).Append(", \"pieceTrisStump\": ").Append(v.PieceTriangles).Append(",\n");
            sb.Append("  \"hitMsMax\": ").Append(F(Max(hitMs, nHit))).Append(", \"hitMsMedian\": ").Append(F(Med(hitMs, nHit))).Append(",\n");
            sb.Append("  \"worstFrameMsEditorStyle\": ").Append(F(Max(worstMs, nWorst))).Append(", \"medianFrameMsEditorStyle\": ").Append(F(Med(worstMs, nWorst))).Append(", \"tickMsMax\": ").Append(F(Max(tickMs, nTick))).Append(", \"tickMsMedian\": ").Append(F(Med(tickMs, nTick))).Append(",\n");
            sb.Append("  \"wallFrameMsMax\": ").Append(F(Max(frameMs, nFrame))).Append(", \"wallFrameMsMedian\": ").Append(F(Med(frameMs, nFrame))).Append(", \"wallFrames\": ").Append(nFrame).Append(",\n");
            sb.Append("  \"islandMsMax\": ").Append(F(v.islMsMax)).Append(", \"pieceMsMax\": ").Append(F(v.pieceMsMax)).Append(", \"piecesPeak\": ").Append(v.pieces.peak).Append(", \"islandRequests\": ").Append(v.isl.requests).Append(", \"islandFull\": ").Append(v.isl.fullPasses).Append(",\n");
            sb.Append("  \"buildMs\": ").Append(F(buildMs)).Append(", \"runSeconds\": ").Append(F(Time.realtimeSinceStartupAsDouble - t0)).Append(", \"gcCollections\": ").Append(System.GC.CollectionCount(0) - gc0).Append(",\n");
            sb.Append("  \"memMB\": { \"monoStart\": ").Append(F(mono1 / 1048576.0)).Append(", \"monoEnd\": ").Append(F(mono2 / 1048576.0)).Append(", \"totalStart\": ").Append(F(tot1 / 1048576.0)).Append(", \"totalEnd\": ").Append(F(tot2 / 1048576.0))
              .Append(", \"monoBeforeView\": ").Append(F(mono0 / 1048576.0)).Append(", \"totalBeforeView\": ").Append(F(tot0 / 1048576.0)).Append(", \"gcHeapEnd\": ").Append(F(System.GC.GetTotalMemory(false) / 1048576.0)).Append(" }\n}\n");
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("ANAT_BENCH_FILE " + outPath); Debug.Log("ANAT_BENCH_JSON " + sb.ToString().Replace("\n", " "));
            v.Dispose(); v = null;
            if (quit) Application.Quit();
        }

        static AnatBench.Stage[] Stages(AnatView w) => AnatBench.Stages(w);
    }
}
