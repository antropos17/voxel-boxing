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
        public string outPath; public bool quit = true, asyncMesh, vsync; public uint seed = 77;
        AnatView v; Camera cam;
        static string F(double d) => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        float[] hitMs = new float[1024], tickMs = new float[4096], frameMs = new float[8192], worstMs = new float[1024];   // worst: удар + первый Tick
        int nHit, nTick, nFrame, nWorst;
        float[] mwMs = new float[1024], maMs = new float[1024]; int nMw, nMa;   // сетки прошлого кадра, забранные в ударе: ожидание джоба и применение
        // часть 4: разбивка каждого удара по частям (AnatProf), убрано ячеек Remove / Take
        readonly System.Collections.Generic.List<double[]> prof = new System.Collections.Generic.List<double[]>();
        readonly System.Collections.Generic.List<int2> profN = new System.Collections.Generic.List<int2>();
        readonly System.Collections.Generic.List<double[]> tprof = new System.Collections.Generic.List<double[]>();   // первый Tick после удара
        float[] twMs = new float[1024], taMs = new float[1024], tcMs = new float[1024]; int nTw, nTa, nTc;   // часть 6: первый Tick — ожидание сеток, применение, самый долгий чанк
        float[][] phMs = { new float[1024], new float[1024], new float[1024], new float[1024], new float[1024], new float[1024] }; int[] nPh = new int[AnatMesher.PhN];   // часть 7: стадии самого долгого чанка первого Tick

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
                else if (args[i] == "-anatBenchAsyncMesh") asyncMesh = true;   // часть 4: сетки на рабочих потоках между кадрами (AnatView.asyncMesh)
                else if (args[i] == "-anatBenchVsync") { QualitySettings.vSyncCount = 1; Application.targetFrameRate = 60; vsync = true; }   // часть 6: 60 кадров/с с вертикальной синхронизацией
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
            sw.Restart(); v = new AnatView(null, new Vector3(0, 1.6f, 0), anatMaterial); double buildMs = sw.Elapsed.TotalMilliseconds; v.asyncMesh = asyncMesh;
            View(); yield return null; yield return null;
            long mono1 = Profiler.GetMonoUsedSizeLong(), tot1 = Profiler.GetTotalAllocatedMemoryLong();
            int gc0 = System.GC.CollectionCount(0);
            var sb = new StringBuilder("{\n  \"stages\": [\n");
            int tris0 = v.Triangles; var stages = Stages(v); var rnd = new Unity.Mathematics.Random(seed);
            int hitsAll = 0; double t0 = Time.realtimeSinceStartupAsDouble; AnatMeshProf.Prof.Data = default;
            foreach (var st in stages)
            {
                int k = 0, miss = 0;
                for (; k < st.max && !st.done(v); k++)
                {
                    // кадр 0: удар и первый Tick (как в редакторе); кадры 1, 2: ещё по Tick. Между вызовами — кадр Unity.
                    AnatProf.Begin(); sw.Restart(); bool ok = AnatBench.Strike(v, st.where, ref rnd); double hm = sw.Elapsed.TotalMilliseconds;
                    if (!ok) miss++; else { hitsAll++; Add(ref hitMs, ref nHit, (float)hm); var pr = new double[AnatProf.N + AnatProf.XN]; System.Array.Copy(AnatProf.t, pr, AnatProf.N); System.Array.Copy(AnatProf.x, 0, pr, AnatProf.N, AnatProf.XN); prof.Add(pr); profN.Add(new int2(AnatProf.removes, AnatProf.takes)); Add(ref mwMs, ref nMw, (float)AnatProf.meshWait); Add(ref maMs, ref nMa, (float)AnatProf.meshApply); }
                    for (int f = 0; f < 3; f++)
                    {
                        double w0 = AnatProf.meshWait, a0 = AnatProf.meshApply; AnatProf.chunkMax = 0; System.Array.Clear(AnatProf.chunkPh, 0, AnatMesher.PhN);
                        sw.Restart(); v.Tick(1 / 30f, 12); double tm = sw.Elapsed.TotalMilliseconds;
                        if (f == 0 && ok) { Add(ref twMs, ref nTw, (float)(AnatProf.meshWait - w0)); Add(ref taMs, ref nTa, (float)(AnatProf.meshApply - a0)); Add(ref tcMs, ref nTc, (float)AnatProf.chunkMax); for (int q = 0; q < AnatMesher.PhN; q++) Add(ref phMs[q], ref nPh[q], (float)AnatProf.chunkPh[q]); }
                        Add(ref tickMs, ref nTick, (float)tm); if (f == 0) { Add(ref worstMs, ref nWorst, (float)(tm + hm)); if (ok) tprof.Add((double[])AnatProf.tk.Clone()); }
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
            sb.Append("  \"platform\": \"").Append(Application.platform).Append("\", \"seed\": ").Append(seed).Append(", \"asyncMesh\": ").Append(v.asyncMesh ? "true" : "false").Append(", \"vsync\": ").Append(vsync ? "true" : "false").Append(", \"cpu\": \"").Append(SystemInfo.processorType.Replace("\"", "'")).Append("\", \"gpu\": \"").Append(SystemInfo.graphicsDeviceName.Replace("\"", "'")).Append("\", \"res\": \"").Append(Screen.width).Append('x').Append(Screen.height).Append("\",\n");
            sb.Append("  \"hits\": ").Append(hitsAll).Append(", \"trisIntact\": ").Append(tris0).Append(", \"trisStump\": ").Append(v.Triangles).Append(", \"pieceTrisStump\": ").Append(v.PieceTriangles).Append(",\n");
            sb.Append("  \"hitMsMax\": ").Append(F(Max(hitMs, nHit))).Append(", \"hitMsMedian\": ").Append(F(Med(hitMs, nHit))).Append(",\n");
            sb.Append("  \"worstFrameMsEditorStyle\": ").Append(F(Max(worstMs, nWorst))).Append(", \"medianFrameMsEditorStyle\": ").Append(F(Med(worstMs, nWorst))).Append(", \"tickMsMax\": ").Append(F(Max(tickMs, nTick))).Append(", \"tickMsMedian\": ").Append(F(Med(tickMs, nTick))).Append(",\n");
            sb.Append("  \"wallFrameMsMax\": ").Append(F(Max(frameMs, nFrame))).Append(", \"wallFrameMsMedian\": ").Append(F(Med(frameMs, nFrame))).Append(", \"wallFrames\": ").Append(nFrame).Append(",\n");
            sb.Append("  \"islandMsMax\": ").Append(F(v.islMsMax)).Append(", \"pieceMsMax\": ").Append(F(v.pieceMsMax)).Append(", \"piecesPeak\": ").Append(v.pieces.peak).Append(", \"islandRequests\": ").Append(v.isl.requests).Append(", \"islandFull\": ").Append(v.isl.fullPasses).Append(",\n");
            sb.Append("  \"buildMs\": ").Append(F(buildMs)).Append(", \"runSeconds\": ").Append(F(Time.realtimeSinceStartupAsDouble - t0)).Append(", \"gcCollections\": ").Append(System.GC.CollectionCount(0) - gc0).Append(",\n");
            sb.Append("  \"memMB\": { \"monoStart\": ").Append(F(mono1 / 1048576.0)).Append(", \"monoEnd\": ").Append(F(mono2 / 1048576.0)).Append(", \"totalStart\": ").Append(F(tot1 / 1048576.0)).Append(", \"totalEnd\": ").Append(F(tot2 / 1048576.0))
              .Append(", \"monoBeforeView\": ").Append(F(mono0 / 1048576.0)).Append(", \"totalBeforeView\": ").Append(F(tot0 / 1048576.0)).Append(", \"gcHeapEnd\": ").Append(F(System.GC.GetTotalMemory(false) / 1048576.0)).Append(" }\n}\n");
            // разбивка: по каждой части худшее, медиана, сумма за прогон; пять худших ударов целиком
            sb.Length -= 3; sb.Append(",\n  \"hitParts\": {");
            int nh = prof.Count; var col = new float[math.max(1, nh)];
            for (int k = 0; k < AnatProf.N + AnatProf.XN; k++)
            {
                double sum = 0; for (int j = 0; j < nh; j++) { col[j] = (float)prof[j][k]; sum += prof[j][k]; }
                sb.Append(k == 0 ? "\n" : ",\n").Append("    \"").Append(PName(k)).Append("\": { \"max\": ").Append(F(Max(col, nh))).Append(", \"median\": ").Append(F(Med(col, nh))).Append(", \"sum\": ").Append(F(sum)).Append(" }");
            }
            sb.Append(",\n    \"meshWaitInHit\": { \"max\": ").Append(F(Max(mwMs, nMw))).Append(", \"median\": ").Append(F(Med(mwMs, nMw))).Append(" }, \"meshApplyInHit\": { \"max\": ").Append(F(Max(maMs, nMa))).Append(", \"median\": ").Append(F(Med(maMs, nMa))).Append(" }");
            sb.Append("\n  },\n  \"tickParts\": {");
            int nt = tprof.Count; var tcol = new float[math.max(1, nt)];
            for (int k = 0; k < AnatProf.TN; k++)
            {
                for (int j = 0; j < nt; j++) tcol[j] = (float)tprof[j][k];
                sb.Append(k == 0 ? "\n" : ",\n").Append("    \"").Append(AnatProf.TNames[k]).Append("\": { \"max\": ").Append(F(Max(tcol, nt))).Append(", \"median\": ").Append(F(Med(tcol, nt))).Append(" }");
            }
            sb.Append(",\n    \"meshWait\": { \"max\": ").Append(F(Max(twMs, nTw))).Append(", \"median\": ").Append(F(Med(twMs, nTw))).Append(" }, \"meshApply\": { \"max\": ").Append(F(Max(taMs, nTa))).Append(", \"median\": ").Append(F(Med(taMs, nTa)))
              .Append(" }, \"chunkMax\": { \"max\": ").Append(F(Max(tcMs, nTc))).Append(", \"median\": ").Append(F(Med(tcMs, nTc))).Append(" }");
            string[] phN = { "chFill", "chState", "chInit", "chFaces", "chVerts", "chVcalls" };
            for (int q = 0; q < AnatMesher.PhN; q++) sb.Append(", \"").Append(phN[q]).Append("\": { \"max\": ").Append(F(Max(phMs[q], nPh[q]))).Append(", \"median\": ").Append(F(Med(phMs[q], nPh[q]))).Append(" }");
            var mp = AnatMeshProf.Prof.Data; double tf = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            sb.Append("\n  },\n  \"meshProf\": { \"chunks\": ").Append(mp.chunks).Append(", \"fillMs\": ").Append(F(mp.fill * tf)).Append(", \"stateMs\": ").Append(F(mp.state * tf)).Append(", \"initMs\": ").Append(F(mp.init * tf))
              .Append(", \"facesMs\": ").Append(F(mp.faces * tf)).Append(", \"writeMs\": ").Append(F(mp.write * tf)).Append(", \"corners\": ").Append(mp.nCorner).Append(", \"grads\": ").Append(mp.nGrad).Append(", \"fields\": ").Append(mp.nField)
              .Append(", \"states\": ").Append(mp.nState).Append(", \"verts\": ").Append(mp.nVert);
            sb.Append(" },\n  \"worstFrames\": [");
            var ford = new int[nt]; for (int j = 0; j < nt; j++) ford[j] = j;
            var comb = new float[math.max(1, nt)]; for (int j = 0; j < nt; j++) { double s2 = hitMs[j]; for (int k = 0; k < AnatProf.TN; k++) s2 += tprof[j][k]; comb[j] = (float)s2; }
            System.Array.Sort(ford, (x, y) => comb[y].CompareTo(comb[x]));
            for (int q = 0; q < math.min(5, nt); q++)
            {
                int j = ford[q]; sb.Append(q == 0 ? "\n" : ",\n").Append("    { \"hit\": ").Append(j).Append(", \"ms\": ").Append(F(comb[j])).Append(", \"hitMs\": ").Append(F(hitMs[j]));
                for (int k = 0; k < AnatProf.TN; k++) if (tprof[j][k] >= 0.05) sb.Append(", \"").Append(AnatProf.TNames[k]).Append("\": ").Append(F(tprof[j][k]));
                sb.Append(" }");
            }
            sb.Append("\n  ],\n  \"worstHits\": [");
            var order = new int[nh]; for (int j = 0; j < nh; j++) order[j] = j;
            System.Array.Sort(order, (x, y) => hitMs[y].CompareTo(hitMs[x]));
            for (int q = 0; q < math.min(5, nh); q++)
            {
                int j = order[q]; sb.Append(q == 0 ? "\n" : ",\n").Append("    { \"hit\": ").Append(j).Append(", \"ms\": ").Append(F(hitMs[j])).Append(", \"removes\": ").Append(profN[j].x).Append(", \"takes\": ").Append(profN[j].y);
                for (int k = 0; k < AnatProf.N + AnatProf.XN; k++) if (prof[j][k] >= 0.05) sb.Append(", \"").Append(PName(k)).Append("\": ").Append(F(prof[j][k]));
                sb.Append(" }");
            }
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("ANAT_BENCH_FILE " + outPath); Debug.Log("ANAT_BENCH_JSON " + sb.ToString().Replace("\n", " "));
            v.Dispose(); v = null;
            if (quit) Application.Quit();
        }

        static AnatBench.Stage[] Stages(AnatView w) => AnatBench.Stages(w);
        static string PName(int k) => k < AnatProf.N ? AnatProf.Names[k] : AnatProf.XNames[k - AnatProf.N];
    }
}
