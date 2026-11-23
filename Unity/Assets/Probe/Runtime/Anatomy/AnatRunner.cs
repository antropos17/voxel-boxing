using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace VoxProbe
{
    /// Замер анатомической головы в игроке (этап A): сборка, полная перестройка сеток (все потоки и один поток), кадр при облёте камеры.
    /// Аргументы: -anatSeconds S (длина облёта, по умолчанию 12), -anatQuit, -anatRes 1080x2340 (рисовать в текстуру этого размера).
    /// Результат: anat-*.json в Application.persistentDataPath и строка ANAT_JSON в логе.
    public class AnatRunner : MonoBehaviour
    {
        public Material anatMaterial;
        static readonly Vector3 Center = new Vector3(0, 1.6f, 0);
        Camera cam; RenderTexture rt;
        AnatHead a; readonly List<AnatBody> bodies = new List<AnatBody>();
        NativeArray<VertexAttributeDescriptor> layout;
        float seconds = 12; bool quit; bool running; double t0;
        readonly List<float> frames = new List<float>(20000);
        readonly Dictionary<string, string> kv = new Dictionary<string, string>();
        ProfilerRecorder totalRec, gfxRec, mainRec;
        readonly List<float> mainMs = new List<float>(20000);

        static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        void Awake()
        {
            Application.targetFrameRate = 1000; QualitySettings.vSyncCount = 0;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-anatSeconds" && i + 1 < args.Length) seconds = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                else if (args[i] == "-anatQuit") quit = true;
                else if (args[i] == "-anatRes" && i + 1 < args.Length)
                {
                    var p = args[++i].Split('x');
                    rt = new RenderTexture(int.Parse(p[0]), int.Parse(p[1]), 24);
                }
            }
        }

        void Start()
        {
            cam = Camera.main;
            if (rt != null) cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 10; cam.fieldOfView = 30;
            AnatPalette.Apply();
            layout = new NativeArray<VertexAttributeDescriptor>(AnatMesher.Layout, Allocator.Persistent);
            totalRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            gfxRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            mainRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            AnatomyBuilder.Build().Dispose(); // прогрев (в игроке Burst уже скомпилирован, но первое выделение памяти дороже)
            kv["buildWarmMs"] = F(sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            a = AnatomyBuilder.Build();
            kv["buildMs"] = F(sw.Elapsed.TotalMilliseconds);
            foreach (var e in a.ms) kv["build." + e.Key + "Ms"] = F(e.Value);

            var root = new GameObject("anat").transform;
            root.position = Center;
            int id = 0;
            foreach (var g in a.All) bodies.Add(new AnatBody(g, root, anatMaterial, id++));
            int workers = JobsUtility.JobWorkerCount;
            kv["workers"] = workers.ToString();
            sw.Restart();
            foreach (var b in bodies) b.BuildAll(layout);
            kv["meshAllFirstMs"] = F(sw.Elapsed.TotalMilliseconds);
            double best = 1e9;
            for (int r = 0; r < 3; r++) { sw.Restart(); foreach (var b in bodies) b.BuildAll(layout); best = math.min(best, sw.Elapsed.TotalMilliseconds); }
            kv["meshAllParallelMs"] = F(best);
            JobsUtility.JobWorkerCount = 0;
            best = 1e9;
            for (int r = 0; r < 2; r++) { sw.Restart(); foreach (var b in bodies) b.BuildAll(layout); best = math.min(best, sw.Elapsed.TotalMilliseconds); }
            kv["meshAllOneThreadMs"] = F(best);
            JobsUtility.JobWorkerCount = workers;
            // один чанк головы с поверхностью (самый дорогой из головы — по числу граней)
            var h = bodies[0]; int top = 0;
            for (int i = 0; i < h.ChunkCount; i++) if (h.naive[i] > h.naive[top]) top = i;
            kv["headChunkMaxFaces"] = h.naive[top].ToString();

            int tris = 0, chunks = 0, drawn = 0; long verts = 0;
            foreach (var b in bodies) { tris += b.Triangles; chunks += b.ChunkCount; for (int i = 0; i < b.ChunkCount; i++) if (b.quads[i] > 0) { drawn++; verts += b.quads[i] * 4; } }
            kv["tris"] = tris.ToString(); kv["verts"] = verts.ToString(); kv["chunks"] = chunks.ToString(); kv["chunksDrawn"] = drawn.ToString();
            kv["meshBytes"] = (verts * 20 + verts / 4 * 6 * 2).ToString();
            foreach (var b in bodies) { string k = b.g == a.head ? "trisHead" : b.g == a.mand ? "trisMandible" : b.g == a.eyeL || b.g == a.eyeR ? "trisEyes" : "trisTeeth"; kv.TryGetValue(k, out var o); kv[k] = ((o == null ? 0 : int.Parse(o)) + b.Triangles).ToString(); }
            kv["visMs"] = F(a.ms["visibility"]);
            long grid = 0; foreach (var g in a.All) grid += g.Count * 3L;
            kv["gridBytes"] = grid.ToString();
            kv["bodies"] = bodies.Count.ToString();
            t0 = Time.realtimeSinceStartupAsDouble + 2.0; // 2 с прогрева шейдеров
        }

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float t = (float)(now - t0);
            // облёт: полный круг за 8 с, камера на 0,55 м, слегка покачивается по высоте
            float ang = t / 8f * math.PI * 2;
            cam.transform.position = Center + new Vector3(math.sin(ang) * 0.55f, 0.04f + 0.05f * math.sin(t * 0.7f), math.cos(ang) * 0.55f);
            cam.transform.LookAt(Center + new Vector3(0, 0.012f, 0));
            if (now < t0) return;
            if (!running) { running = true; frames.Clear(); mainMs.Clear(); return; }
            frames.Add(Time.unscaledDeltaTime * 1000f);
            if (mainRec.Valid && mainRec.Count > 0) mainMs.Add(mainRec.LastValue / 1e6f);
            if (t >= seconds) Finish();
        }

        void Finish()
        {
            enabled = false;
            frames.Sort(); mainMs.Sort();
            double sum = 0; foreach (var f in frames) sum += f;
            kv["frames"] = frames.Count.ToString();
            kv["frameMsMean"] = F(sum / frames.Count);
            kv["frameMsP95"] = F(frames[(int)(frames.Count * 0.95f)]);
            kv["frameMsP99"] = F(frames[(int)(frames.Count * 0.99f)]);
            kv["frameMsMax"] = F(frames[frames.Count - 1]);
            if (mainMs.Count > 0) { double s2 = 0; foreach (var f in mainMs) s2 += f; kv["mainThreadMsMean"] = F(s2 / mainMs.Count); }
            kv["memTotalUsedMB"] = F(totalRec.LastValue / 1048576.0);
            kv["memGfxUsedMB"] = gfxRec.Valid ? F(gfxRec.LastValue / 1048576.0) : "n/a";
            kv["memUnityAllocatedMB"] = F(Profiler.GetTotalAllocatedMemoryLong() / 1048576.0);
            kv["res"] = rt != null ? rt.width + "x" + rt.height : Screen.width + "x" + Screen.height;
            kv["device"] = SystemInfo.deviceModel + " / " + SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName;
            var sb = new StringBuilder("{\n");
            foreach (var e in kv) { bool num = double.TryParse(e.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _); sb.Append("  \"").Append(e.Key).Append("\": ").Append(num ? e.Value : "\"" + e.Value.Replace("\"", "'") + "\"").Append(",\n"); }
            sb.Append("  \"ok\": true\n}\n");
            string json = sb.ToString();
            string path = System.IO.Path.Combine(Application.persistentDataPath, "anat-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            System.IO.File.WriteAllText(path, json);
            Debug.Log("ANAT_FILE " + path);
            Debug.Log("ANAT_JSON " + json);
            if (quit) Application.Quit();
        }

        void OnDestroy()
        {
            foreach (var b in bodies) b.Destroy();
            a?.Dispose();
            if (layout.IsCreated) layout.Dispose();
            totalRec.Dispose(); gfxRec.Dispose(); mainRec.Dispose();
        }
    }
}
