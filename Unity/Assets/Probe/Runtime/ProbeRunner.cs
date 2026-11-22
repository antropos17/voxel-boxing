using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace VoxProbe
{
    /// Проба: одна разрушаемая голова. Режим замера — 60 с ударов по сценарию, затем итог на экране, в JSON и в логе.
    /// Аргументы: -probeAuto (замер сразу), -probeQuit (выйти после), -probeWorkers N, -probeRes 1080x2340 (рисовать в текстуру этого размера),
    /// -probeSeconds S, -probeNoHud. На Android замер стартует сам.
    public class ProbeRunner : MonoBehaviour
    {
        public Material voxMaterial, bloodMaterial, floorMaterial;

        const float VS = HeadBuilder.S * 0.01f; // метры на воксель
        const int MaxPieces = 24, MinPieceVox = 8, AnchorY = 4;
        static readonly Vector3 HeadPos = new Vector3(0, 1.6f, 0);

        // состояние
        Camera cam;
        RenderTexture rt;
        VoxBody head;
        NativeArray<byte> pristine;
        readonly List<VoxBody> bodies = new List<VoxBody>();
        MeshScheduler mesher;
        Blood blood;
        float floorY;
        Unity.Mathematics.Random rng;

        // куски
        class Piece { public GameObject go; public Rigidbody rb; public BoxCollider bc; public VoxBody body; public float born; public bool active; }
        readonly List<Piece> pieces = new List<Piece>();
        int piecesSpawned, piecesRecycled, fragmentsToDrops;

        // буферы операций
        NativeArray<int> carveRes, islandRes, stamp, queue;
        NativeArray<float3> seeds;
        NativeList<int> compVox; NativeList<int2> comps; NativeList<int3> compMin, compMax;
        int gen;
        struct Pending { public int3 mn, mx; public float3 dir, at; }
        readonly List<Pending> islandQueue = new List<Pending>(16);

        // режим
        bool auto, quitAfter, noHud, running, done;
        int workers = -1;
        float seconds = 60;
        double runStart, lastT;
        int hitIndex;
        bool burst1, burst2, resetDone;

        // замеры
        readonly Stat frameMs = new Stat(200000), cpuMainMs = new Stat(200000), gpuMs = new Stat(200000), workMs = new Stat(200000);
        readonly Stat gcAlloc = new Stat(200000), carveMs = new Stat(1000), islandMs = new Stat(1000), extractMs = new Stat(1000);
        readonly FrameTiming[] ft = new FrameTiming[1];
        ProfilerRecorder gcRec, totalRec, sysRec, gcUsedRec, gfxRec;
        readonly Stopwatch sw = new Stopwatch(), opSw = new Stopwatch();
        long allocBase;
        int hits, heavyHits, bursts, framesAllocating;
        readonly StringBuilder sb = new StringBuilder(8192);
        string hud = "", summary = "";
        float hudT;
        Dictionary<string, string> pre = new Dictionary<string, string>();
        int startTris, startVerts, peakTris, peakPieces, peakDrops, preResetTris;

        void Awake()
        {
            useGUILayout = false;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-probeAuto") auto = true;
                else if (a == "-probeQuit") quitAfter = true;
                else if (a == "-probeNoHud") noHud = true;
                else if (a == "-probeWorkers" && i + 1 < args.Length) workers = int.Parse(args[++i]);
                else if (a == "-probeSeconds" && i + 1 < args.Length) seconds = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                else if (a == "-probeRes" && i + 1 < args.Length)
                {
                    var p = args[++i].Split('x');
                    rt = new RenderTexture(int.Parse(p[0]), int.Parse(p[1]), 24) { name = "probe" };
                }
            }
            if (Application.platform == RuntimePlatform.Android) auto = true;
            if (workers > 0) JobsUtility.JobWorkerCount = workers;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Application.isMobilePlatform ? 120 : 1000;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Shader.SetGlobalVectorArray("_VoxPalette", PaletteLinear());
            cam = Camera.main;
            if (rt != null) cam.targetTexture = rt;

            mesher = new MeshScheduler();
            carveRes = new NativeArray<int>(8, Allocator.Persistent);
            islandRes = new NativeArray<int>(2, Allocator.Persistent);
            seeds = new NativeArray<float3>(32, Allocator.Persistent);
            compVox = new NativeList<int>(4096, Allocator.Persistent);
            comps = new NativeList<int2>(64, Allocator.Persistent);
            compMin = new NativeList<int3>(64, Allocator.Persistent);
            compMax = new NativeList<int3>(64, Allocator.Persistent);
            rng = new Unity.Mathematics.Random(12345);

            // голова
            sw.Restart();
            var grid = HeadBuilder.Build(out int3 dims);
            pre["headBuildMs"] = F(sw.Elapsed.TotalMilliseconds);
            pristine = new NativeArray<byte>(grid, Allocator.Persistent);
            stamp = new NativeArray<int>(grid.Length, Allocator.Persistent);
            queue = new NativeArray<int>(grid.Length, Allocator.Persistent);
            var hr = new GameObject("head").transform;
            hr.position = HeadPos + (Vector3)(HeadBuilder.Min * 0.01f);
            head = new VoxBody(hr, voxMaterial, VS) { visibleOnApply = true };
            head.SetGrid(grid, dims);
            bodies.Add(head);

            floorY = HeadPos.y - 0.14f;
            blood = new Blood(bloodMaterial, floorY);
            MakeFloor();
            PlaceCamera();

            gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            totalRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            sysRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            gcUsedRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
            gfxRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");

            Prepass("start");
            mesher.Schedule(bodies, Now);
            mesher.Flush(Now); // первая сборка до замера
            startTris = head.Triangles; startVerts = head.Vertices;
            mesher.chunkMs.Clear(); mesher.batchMs.Clear(); mesher.applyMs.Clear(); mesher.batches = 0; mesher.chunksBuilt = 0;
            lastT = Now;
            if (auto) armAt = Now + 2.0; // 2 с прогрева: первый кадр собирает шейдеры, в замер не идёт
            else summary = "Свободный режим: касание — удар. Клавиша B — разрыв, R — сброс, M — замер.";
        }

        double armAt;
        int ev, prevEv, gcBase;
        long monoBase;
        readonly float[] worstMs = new float[12], worstT = new float[12];
        readonly int[] worstEv = new int[12];
        const int EvLight = 1, EvHeavy = 2, EvBurst = 4, EvIslands = 8, EvPiece = 16, EvReset = 32, EvMesh = 64;

        static double Now => Time.realtimeSinceStartupAsDouble;
        static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        Vector4[] PaletteLinear()
        {
            var a = new Vector4[16];
            for (int i = 0; i < VoxMat.Palette.Length; i++) { Color c = VoxMat.Palette[i]; a[i] = QualitySettings.activeColorSpace == ColorSpace.Linear ? (Vector4)c.linear : (Vector4)c; }
            return a;
        }

        void PlaceCamera()
        {
            float aspect = rt != null ? rt.width / (float)rt.height : cam.aspect;
            cam.fieldOfView = 40;
            float half = math.tan(math.radians(cam.fieldOfView * 0.5f)) * aspect;
            float width = head.dims.x * VS * 1.04f;
            float dist = width * 0.5f / half;
            var center = HeadPos + new Vector3(0, 0.014f, 0);
            cam.transform.position = center + new Vector3(0, 0, dist + 0.1f);
            cam.transform.LookAt(center);
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 10;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
        }

        void MakeFloor()
        {
            var go = new GameObject("floor");
            go.transform.position = new Vector3(0, floorY, 0);
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-2, 0, -2), new Vector3(-2, 0, 2), new Vector3(2, 0, 2), new Vector3(2, 0, -2) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, -0.5f, 0); bc.size = new Vector3(4, 1, 4);
        }

        // ---------- предварительные замеры (вне 60 с) ----------
        void Prepass(string tag)
        {
            var grid = head.vox;
            int3 nch = head.nch;
            var res = new NativeArray<int>(2, Allocator.TempJob);
            new CountJob { vox = grid, dims = head.dims, nch = nch, mode = GreedyMesher.ModeRender, result = res }.Run();
            int naive = res[0], ao = res[1];
            new CountJob { vox = grid, dims = head.dims, nch = nch, mode = GreedyMesher.ModeNoAO, result = res }.Run();
            int noao = res[1];
            res.Dispose();
            int solid = 0; for (int i = 0; i < grid.Length; i++) if (grid[i] != 0) solid++;
            pre[tag + ".voxels"] = solid.ToString();
            pre[tag + ".trisNaive"] = (naive * 2).ToString();
            pre[tag + ".trisGreedyAO"] = (ao * 2).ToString();
            pre[tag + ".trisGreedyNoAO"] = (noao * 2).ToString();
            pre[tag + ".vertsNaive"] = (naive * 4).ToString();
            pre[tag + ".vertsGreedyAO"] = (ao * 4).ToString();

            // стоимость сетки одного чанка: каждый чанк отдельно на главном потоке (Run), прогрев + 3 повтора, берём минимум
            var layout = new NativeArray<UnityEngine.Rendering.VertexAttributeDescriptor>(GreedyMesher.Layout, Allocator.TempJob);
            var org = new NativeArray<int3>(1, Allocator.TempJob);
            var st = new NativeArray<long>(4, Allocator.TempJob);
            double sum = 0, max = 0; int cnt = 0;
            for (int c = 0; c < head.ChunkCount; c++)
            {
                org[0] = head.ChunkCoord(c) * GreedyMesher.CS;
                double best = 1e9;
                for (int rep = 0; rep < 4; rep++)
                {
                    var md = Mesh.AllocateWritableMeshData(1);
                    opSw.Restart();
                    new ChunkMeshJob { vox = grid, origins = org, layout = layout, dims = head.dims, meshes = md, stats = st }.Run(1);
                    double ms = opSw.Elapsed.TotalMilliseconds;
                    md.Dispose();
                    if (rep > 0 && ms < best) best = ms;
                }
                sum += best; cnt++; if (best > max) max = best;
            }
            pre[tag + ".chunkMsMean"] = F(sum / cnt);
            pre[tag + ".chunkMsMax"] = F(max);
            pre[tag + ".chunkMsSumOneThread"] = F(sum);

            // все чанки разом джобами (все потоки), до завершения
            var orgs = new NativeArray<int3>(head.ChunkCount, Allocator.TempJob);
            var sts = new NativeArray<long>(head.ChunkCount * 4, Allocator.TempJob);
            for (int c = 0; c < head.ChunkCount; c++) orgs[c] = head.ChunkCoord(c) * GreedyMesher.CS;
            double bestAll = 1e9;
            for (int rep = 0; rep < 4; rep++)
            {
                var md = Mesh.AllocateWritableMeshData(head.ChunkCount);
                opSw.Restart();
                new ChunkMeshJob { vox = grid, origins = orgs, layout = layout, dims = head.dims, meshes = md, stats = sts }.Schedule(head.ChunkCount, 1).Complete();
                double ms = opSw.Elapsed.TotalMilliseconds;
                md.Dispose();
                if (rep > 0 && ms < bestAll) bestAll = ms;
            }
            pre[tag + ".fullRebuildParallelMs"] = F(bestAll);
            // поиск кусков по всей решётке
            double bestIsl = 1e9;
            for (int rep = 0; rep < 3; rep++)
            {
                opSw.Restart();
                RunIslands(int3.zero, head.dims - 1);
                bestIsl = math.min(bestIsl, opSw.Elapsed.TotalMilliseconds);
            }
            pre[tag + ".islandsFullGridMs"] = F(bestIsl);
            pre[tag + ".islandsFullGridFound"] = comps.Length.ToString();
            orgs.Dispose(); sts.Dispose(); layout.Dispose(); org.Dispose(); st.Dispose();
        }

        // ---------- удар ----------
        public enum Kind { Light, Heavy, Burst }

        /// Луч из камеры через точку экрана (пиксели). true — попали в голову.
        bool Aim(Vector2 screen, out float3 cell, out float3 dirCells)
        {
            cell = default; dirCells = default;
            Vector2 s = screen;
            if (rt != null) s = new Vector2(screen.x / Screen.width * rt.width, screen.y / Screen.height * rt.height);
            var ray = cam.ScreenPointToRay(s);
            var root = head.root;
            float3 o = (float3)root.InverseTransformPoint(ray.origin) / VS;
            float3 d = math.normalize((float3)root.InverseTransformDirection(ray.direction));
            if (!VoxRay.Cast(head.vox, head.dims, o, d, out int3 c, out float t)) return false;
            cell = o + d * t; dirCells = d;
            return true;
        }

        void Hit(Vector2 screen, Kind kind)
        {
            if (!Aim(screen, out float3 at, out float3 dir)) return;
            float r, r2; int nseed, drops;
            if (kind == Kind.Light) { r = 3f; r2 = 0; nseed = 0; drops = 40; }
            else if (kind == Kind.Heavy) { r = 3.5f; r2 = 8f; nseed = 7; drops = 150; heavyHits++; }
            else { r = 12f; r2 = 22f; nseed = 18; drops = 900; bursts++; }
            ev |= kind == Kind.Light ? EvLight : kind == Kind.Heavy ? EvHeavy : EvBurst;
            float3 c = at + dir * (r * 0.5f);
            for (int s = 0; s < nseed; s++) seeds[s] = c + rng.NextFloat3Direction() * rng.NextFloat(r, r2);
            opSw.Restart();
            new CarveJob { vox = head.vox, dims = head.dims, c = c, r = r, r2 = r2, seeds = seeds, seedCount = nseed, salt = (uint)hits * 7919u + 1, result = carveRes }.Run();
            carveMs.Add((float)opSw.Elapsed.TotalMilliseconds);
            hits++;
            var mn = new int3(carveRes[1], carveRes[2], carveRes[3]);
            var mx = new int3(carveRes[4], carveRes[5], carveRes[6]);
            head.MarkDirty(mn, mx);
            // поиск кусков — в следующем кадре, в окрестности удара с запасом (разрыв — по всей решётке)
            var p = new Pending { dir = dir, at = c };
            if (kind == Kind.Burst) { p.mn = int3.zero; p.mx = head.dims - 1; }
            else { p.mn = math.max(mn - 6, 0); p.mx = math.min(mx + 6, head.dims - 1); }
            islandQueue.Add(p);
            float3 w = CellToWorld(c);
            float3 wd = -(float3)cam.transform.forward;
            for (int k = 0; k < drops; k++)
            {
                float3 v = (wd * rng.NextFloat(0.3f, 1.6f) + rng.NextFloat3Direction() * rng.NextFloat(0.2f, 1.2f)) * (kind == Kind.Burst ? 1.6f : 1f);
                blood.Spawn(w + rng.NextFloat3Direction() * r * VS * 0.6f, v, rng.NextFloat(0.6f, 1.8f) * VS * 1.5f, rng.NextFloat(2.5f, 6f));
            }
        }

        float3 CellToWorld(float3 c) => (float3)head.root.position + c * VS;

        void RunIslands(int3 mn, int3 mx)
        {
            gen++;
            new IslandJob { vox = head.vox, dims = head.dims, mn = mn, mx = mx, anchorY = AnchorY, stamp = stamp, queue = queue, gen = gen, compVox = compVox, comps = comps, compMin = compMin, compMax = compMax, result = islandRes }.Run();
        }

        void ProcessIslands()
        {
            for (int q = 0; q < islandQueue.Count; q++)
            {
                var p = islandQueue[q];
                opSw.Restart();
                RunIslands(p.mn, p.mx);
                islandMs.Add((float)opSw.Elapsed.TotalMilliseconds);
                opSw.Restart();
                if (comps.Length > 0) ev |= EvIslands;
                for (int k = 0; k < comps.Length; k++) Detach(k, p);
                if (comps.Length > 0) extractMs.Add((float)opSw.Elapsed.TotalMilliseconds);
            }
            islandQueue.Clear();
        }

        void Detach(int k, Pending hit)
        {
            int2 cr = comps[k];
            int3 mn = compMin[k], mx = compMax[k];
            head.MarkDirty(mn, mx);
            float3 centre = ((float3)(mn + mx) * 0.5f + 0.5f);
            Piece pc = cr.y >= MinPieceVox ? TakePiece() : null;
            if (pc == null)
            {
                // мелочь — в капли
                int W = head.dims.x, WH = head.dims.x * head.dims.y;
                for (int j = 0; j < cr.y; j++)
                {
                    int i = compVox[cr.x + j];
                    if ((j & 1) == 0)
                    {
                        float3 cc = new float3(i % W, (i / W) % head.dims.y, i / WH) + 0.5f;
                        blood.Spawn(CellToWorld(cc), (hit.dir * -1f + rng.NextFloat3Direction()) * 0.8f, VS * 1.4f, rng.NextFloat(2f, 4f));
                    }
                    head.vox[i] = 0;
                }
                fragmentsToDrops++;
                return;
            }
            int3 pd = mx - mn + 1;
            var grid = new NativeArray<byte>(pd.x * pd.y * pd.z, Allocator.Persistent);
            new ExtractJob { src = head.vox, sdims = head.dims, ids = compVox.AsArray(), start = cr.x, count = cr.y, dst = grid, mn = mn, pd = pd }.Run();
            pc.body.SetGrid(grid, pd);
            var t = pc.go.transform;
            float3 wp = CellToWorld(mn);
            pc.rb.position = wp; pc.rb.rotation = Quaternion.identity;
            t.SetPositionAndRotation(wp, Quaternion.identity);
            pc.bc.center = (float3)pd * VS * 0.5f; pc.bc.size = (float3)pd * VS;
            pc.rb.mass = math.max(0.001f, cr.y * VS * VS * VS * 1000f);
            float3 away = math.normalizesafe(centre - hit.at);
            float3 v = away * rng.NextFloat(0.4f, 1.4f) - hit.dir * rng.NextFloat(0.2f, 1.0f) + new float3(0, rng.NextFloat(0.2f, 1.2f), 0);
            pc.go.SetActive(true);
            pc.rb.linearVelocity = v;
            pc.rb.angularVelocity = rng.NextFloat3Direction() * rng.NextFloat(2, 12);
            pc.born = Time.time; pc.active = true;
            if (!bodies.Contains(pc.body)) bodies.Add(pc.body);
            piecesSpawned++; ev |= EvPiece;
        }

        Piece TakePiece()
        {
            int act = 0; Piece oldest = null;
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                if (!p.active) { if (!p.body.busy) return p; continue; }
                act++;
                if (!p.body.busy && (oldest == null || p.born < oldest.born)) oldest = p;
            }
            if (act < MaxPieces && pieces.Count < MaxPieces + 4)
            {
                var go = new GameObject("piece");
                var p = new Piece { go = go, rb = go.AddComponent<Rigidbody>(), bc = go.AddComponent<BoxCollider>() };
                p.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                p.body = new VoxBody(go.transform, voxMaterial, VS) { visibleOnApply = true };
                pieces.Add(p);
                return p;
            }
            if (oldest != null) { piecesRecycled++; oldest.active = false; oldest.body.Hide(); }
            return oldest;
        }

        void ResetHead()
        {
            ev |= EvReset;
            pristine.CopyTo(head.vox);
            head.MarkAllDirty();
            for (int i = 0; i < pieces.Count; i++) if (pieces[i].active && !pieces[i].body.busy) { pieces[i].active = false; pieces[i].go.SetActive(false); }
        }

        // ---------- кадр ----------
        void StartRun()
        {
            running = true; done = false;
            runStart = Now; hitIndex = 0; burst1 = burst2 = resetDone = false;
            frameMs.Clear(); cpuMainMs.Clear(); gpuMs.Clear(); workMs.Clear(); gcAlloc.Clear(); carveMs.Clear(); islandMs.Clear(); extractMs.Clear();
            mesher.chunkMs.Clear(); mesher.batchMs.Clear(); mesher.applyMs.Clear(); mesher.batches = 0; mesher.chunksBuilt = 0;
            hits = heavyHits = bursts = framesAllocating = piecesSpawned = piecesRecycled = fragmentsToDrops = 0; blood.spawned = 0;
            peakTris = peakPieces = peakDrops = 0;
            startTris = head.Triangles; startVerts = head.Vertices;
            pre["memStart.totalUsedMB"] = F(totalRec.LastValue / 1048576.0);
            pre["memStart.systemUsedMB"] = F(sysRec.LastValue / 1048576.0);
            pre["memStart.monoUsedMB"] = F(Profiler.GetMonoUsedSizeLong() / 1048576.0);
            pre["memStart.unityAllocatedMB"] = F(Profiler.GetTotalAllocatedMemoryLong() / 1048576.0);
            monoBase = Profiler.GetMonoUsedSizeLong(); gcBase = System.GC.CollectionCount(0);
            for (int i = 0; i < worstMs.Length; i++) { worstMs[i] = 0; worstT[i] = 0; worstEv[i] = 0; }
            prevEv = ev = 0;
        }

        void Update()
        {
            double now = Now;
            float dt = (float)(now - lastT); lastT = now;
            sw.Restart();
            FrameTimingManager.CaptureFrameTimings();
            if (running)
            {
                frameMs.Add(dt * 1000f);
                if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
                {
                    if (ft[0].cpuMainThreadFrameTime > 0) cpuMainMs.Add((float)ft[0].cpuMainThreadFrameTime);
                    if (ft[0].gpuFrameTime > 0) gpuMs.Add((float)ft[0].gpuFrameTime);
                }
                // выделения: счётчик профайлера, если он есть в сборке; иначе прирост управляемой кучи (сборка мусора его обнуляет — тогда кадр не считаем)
                long fa;
                if (gcRec.Valid) fa = gcRec.LastValue;
                else { long mu = Profiler.GetMonoUsedSizeLong(); fa = math.max(0, mu - monoBase); monoBase = mu; }
                gcAlloc.Add(fa); if (fa > 0) framesAllocating++;
                float fms = dt * 1000f; int wi = 0;
                for (int i = 1; i < worstMs.Length; i++) if (worstMs[i] < worstMs[wi]) wi = i;
                if (fms > worstMs[wi]) { worstMs[wi] = fms; worstT[wi] = (float)(now - runStart); worstEv[wi] = prevEv; }
            }
            prevEv = ev; ev = 0;
            if (armAt > 0 && now >= armAt) { armAt = 0; StartRun(); }

            int built = mesher.chunksBuilt;
            mesher.Poll(now);
            ProcessIslands();
            if (running) Scenario((float)(now - runStart));
            else if (!done || !auto) FreeInput();
            else if (Input.GetMouseButtonDown(0) || Input.touchCount > 0) { done = false; auto = false; summary = ""; }
            if (mesher.chunksBuilt != built) ev |= EvMesh;
            mesher.Schedule(bodies, now);
            blood.Schedule(math.min(dt, 0.05f));
            sw.Stop(); // время скрипта: Update + LateUpdate, без рендера
        }

        void LateUpdate()
        {
            sw.Start();
            int built = mesher.chunksBuilt;
            mesher.Poll(Now);
            if (mesher.chunksBuilt != built) ev |= EvMesh;
            blood.Render();
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                if (p.active && p.go.transform.position.y < floorY - 2f) { p.active = false; p.go.SetActive(false); }
            }
            if (running)
            {
                int tris = Tris();
                if (tris > peakTris) peakTris = tris;
                int ap = 0; for (int i = 0; i < pieces.Count; i++) if (pieces[i].active) ap++;
                if (ap > peakPieces) peakPieces = ap;
                if (blood.Alive > peakDrops) peakDrops = blood.Alive;
                workMs.Add((float)sw.Elapsed.TotalMilliseconds);
            }
            if (!noHud && Time.unscaledTime > hudT)
            {
                hudT = Time.unscaledTime + 0.5f;
                float fms = frameMs.n > 0 ? frameMs.v[math.min(frameMs.n, frameMs.v.Length) - 1] : 0;
                hud = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} к/с  {1:0.0} мс  тр {2}  куски {3}  капли {4}{5}",
                    1000f / math.max(fms, 0.01f), fms, Tris(), ActivePieces(), blood.Alive, running ? "  " + (seconds - (Now - runStart)).ToString("0") + " с" : "");
            }
        }

        int ActivePieces() { int n = 0; for (int i = 0; i < pieces.Count; i++) if (pieces[i].active) n++; return n; }

        int Tris()
        {
            int t = head.Triangles;
            for (int i = 0; i < pieces.Count; i++) if (pieces[i].active) t += pieces[i].body.Triangles;
            return t + blood.Alive * 12;
        }

        int Verts()
        {
            int t = head.Vertices;
            for (int i = 0; i < pieces.Count; i++) if (pieces[i].active) t += pieces[i].body.Vertices;
            return t + blood.Alive * 24;
        }

        Vector2 HeadScreen(float u, float v)
        {
            float w = rt != null ? Screen.width : Screen.width, h = Screen.height;
            return new Vector2(w * u, h * v);
        }

        void Scenario(float t)
        {
            if (!burst1 && t >= 24f) { burst1 = true; preBurst = head.Triangles; Hit(HeadScreen(0.5f, 0.55f), Kind.Burst); return; }
            if (!resetDone && t >= 34f) { resetDone = true; preResetTris = head.Triangles; pre["beforeReset.trisGreedyAO"] = head.Triangles.ToString(); pre["beforeReset.trisNaive"] = head.NaiveTriangles.ToString(); ResetHead(); return; }
            if (!burst2 && t >= 50f) { burst2 = true; Hit(HeadScreen(0.5f, 0.5f), Kind.Burst); return; }
            float next = 0.5f + hitIndex * 0.35f;
            if (t >= next && t < seconds - 0.5f)
            {
                hitIndex++;
                var k = hitIndex % 5 == 4 ? Kind.Heavy : Kind.Light;
                Hit(HeadScreen(rng.NextFloat(0.25f, 0.75f), rng.NextFloat(0.32f, 0.72f)), k);
            }
            if (t >= seconds) Finish();
        }

        int preBurst;

        void FreeInput()
        {
            if (Input.GetMouseButtonDown(0)) Hit(Input.mousePosition, Kind.Heavy);
            if (Input.GetKeyDown(KeyCode.B)) Hit(HeadScreen(0.5f, 0.55f), Kind.Burst);
            if (Input.GetKeyDown(KeyCode.R)) ResetHead();
            if (Input.GetKeyDown(KeyCode.M)) armAt = Now + 2.0;
            if (Input.touchCount >= 3 && Input.GetTouch(2).phase == TouchPhase.Began) ResetHead();
        }

        void Finish()
        {
            running = false; done = true;
            mesher.Flush(Now);
            int endTris = Tris(), endVerts = Verts(), endHeadTris = head.Triangles, endHeadNaive = head.NaiveTriangles;
            pre["memEnd.totalUsedMB"] = F(totalRec.LastValue / 1048576.0);
            pre["memEnd.systemUsedMB"] = F(sysRec.LastValue / 1048576.0);
            pre["memEnd.gcUsedMB"] = F(gcUsedRec.LastValue / 1048576.0);
            pre["memEnd.gfxUsedMB"] = gfxRec.Valid ? F(gfxRec.LastValue / 1048576.0) : "n/a";
            pre["memEnd.monoUsedMB"] = F(Profiler.GetMonoUsedSizeLong() / 1048576.0);
            pre["memEnd.unityAllocatedMB"] = F(Profiler.GetTotalAllocatedMemoryLong() / 1048576.0);
            Prepass("end");
            Build(endTris, endVerts, endHeadTris, endHeadNaive);
            string dir = Application.persistentDataPath;
            string name = "probe-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-w" + JobsUtility.JobWorkerCount + ".json";
            string path = System.IO.Path.Combine(dir, name);
            System.IO.File.WriteAllText(path, json);
            Debug.Log("PROBE_FILE " + path);
            Debug.Log("PROBE_JSON " + json);
            if (quitAfter) Application.Quit();
        }

        string json;

        void Build(int endTris, int endVerts, int endHeadTris, int endHeadNaive)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            sb.Clear();
            sb.Append("{\n");
            void K(string k, string v, bool q = false) { sb.Append("  \"").Append(k).Append("\": ").Append(q ? "\"" + v + "\"" : v).Append(",\n"); }
            K("device", SystemInfo.deviceModel, true);
            K("cpu", SystemInfo.processorType, true);
            K("cores", SystemInfo.processorCount.ToString());
            K("gpu", SystemInfo.graphicsDeviceName, true);
            K("graphicsApi", SystemInfo.graphicsDeviceType.ToString(), true);
            K("platform", Application.platform.ToString(), true);
            K("jobWorkers", JobsUtility.JobWorkerCount.ToString());
            K("jobWorkersMax", JobsUtility.JobWorkerMaximumCount.ToString());
            K("renderSize", (rt != null ? rt.width + "x" + rt.height : Screen.width + "x" + Screen.height), true);
            K("targetFrameRate", Application.targetFrameRate.ToString());
            K("seconds", F(seconds));
            K("grid", head.dims.x + "x" + head.dims.y + "x" + head.dims.z, true);
            K("chunkSize", GreedyMesher.CS.ToString());
            K("chunks", head.ChunkCount.ToString());
            K("frames", frameMs.n.ToString());
            K("frameMsMean", F(frameMs.Mean)); K("frameMsP95", F(frameMs.Pct(0.95f))); K("frameMsP99", F(frameMs.Pct(0.99f))); K("frameMsMax", F(frameMs.max));
            K("cpuMainMsMean", F(cpuMainMs.Mean)); K("cpuMainMsP95", F(cpuMainMs.Pct(0.95f))); K("cpuMainMsP99", F(cpuMainMs.Pct(0.99f))); K("cpuMainMsMax", F(cpuMainMs.max)); K("cpuMainSamples", cpuMainMs.n.ToString());
            K("gpuMsMean", F(gpuMs.Mean)); K("gpuMsP95", F(gpuMs.Pct(0.95f))); K("gpuMsP99", F(gpuMs.Pct(0.99f))); K("gpuMsMax", F(gpuMs.max)); K("gpuSamples", gpuMs.n.ToString());
            K("scriptMsMean", F(workMs.Mean)); K("scriptMsP99", F(workMs.Pct(0.99f))); K("scriptMsMax", F(workMs.max));
            K("trisStart", startTris.ToString()); K("vertsStart", startVerts.ToString());
            K("trisEnd", endTris.ToString()); K("vertsEnd", endVerts.ToString());
            K("trisPeak", peakTris.ToString());
            K("headTrisEndGreedy", endHeadTris.ToString()); K("headTrisEndNaive", endHeadNaive.ToString());
            K("headTrisBeforeBurst1", preBurst.ToString()); K("headTrisBeforeReset", preResetTris.ToString());
            K("chunkJobMsMean", F(mesher.chunkMs.Mean)); K("chunkJobMsP99", F(mesher.chunkMs.Pct(0.99f))); K("chunkJobMsMax", F(mesher.chunkMs.max)); K("chunksBuilt", mesher.chunkMs.n.ToString());
            K("batchLatencyMsMean", F(mesher.batchMs.Mean)); K("batchLatencyMsMax", F(mesher.batchMs.max)); K("batches", mesher.batchMs.n.ToString());
            K("meshApplyMsMean", F(mesher.applyMs.Mean)); K("meshApplyMsMax", F(mesher.applyMs.max));
            K("carveMsMean", F(carveMs.Mean)); K("carveMsMax", F(carveMs.max));
            K("islandMsMean", F(islandMs.Mean)); K("islandMsMax", F(islandMs.max)); K("islandSearches", islandMs.n.ToString());
            K("extractMsMean", F(extractMs.Mean)); K("extractMsMax", F(extractMs.max));
            K("hits", hits.ToString()); K("heavyHits", heavyHits.ToString()); K("bursts", bursts.ToString());
            K("piecesSpawned", piecesSpawned.ToString()); K("piecesRecycled", piecesRecycled.ToString()); K("fragmentsToDrops", fragmentsToDrops.ToString()); K("piecesPeak", peakPieces.ToString());
            K("dropsSpawned", blood.spawned.ToString()); K("dropsPeak", peakDrops.ToString());
            K("gcSource", gcRec.Valid ? "ProfilerRecorder GC Allocated In Frame" : "Profiler.GetMonoUsedSizeLong delta", true);
            K("gcCollectionsInRun", (System.GC.CollectionCount(0) - gcBase).ToString());
            var wsb = new StringBuilder("[");
            var order = new int[worstMs.Length]; for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (x, y) => worstMs[y].CompareTo(worstMs[x]));
            foreach (int i in order) { if (worstMs[i] <= 0) continue; if (wsb.Length > 1) wsb.Append(", "); wsb.Append("{\"ms\": ").Append(F(worstMs[i])).Append(", \"t\": ").Append(F(worstT[i])).Append(", \"ev\": \"").Append(EvName(worstEv[i])).Append("\"}"); }
            wsb.Append("]");
            K("worstFrames", wsb.ToString());
            K("gcBytesPerFrameMean", F(gcAlloc.Mean)); K("gcBytesPerFrameMax", F(gcAlloc.max)); K("gcFramesAllocating", framesAllocating.ToString());
            K("gcCollections", System.GC.CollectionCount(0).ToString());
            foreach (var kv in pre) K(kv.Key, kv.Value, !char.IsDigit(kv.Value[0]) && kv.Value[0] != '-');
            sb.Length -= 2; sb.Append("\n}\n");
            json = sb.ToString();
            summary = string.Format(c,
                "ИТОГ {0} с, {1} кадров, потоков джобов {2}\nкадр: ср {3:0.00} / p95 {4:0.00} / p99 {5:0.00} / макс {6:0.0} мс\nЦП гл. поток ср {7:0.00} мс, ГП ср {8:0.00} мс\nтреугольники: начало {9}, конец {10} (пик {11})\nсетка чанка: ср {12:0.000} / макс {13:0.000} мс; партия ср {14:0.00} мс\nпоиск кусков: ср {15:0.000} / макс {16:0.000} мс\nвыделения: ср {17:0} Б/кадр, кадров с выделениями {18}\nпамять: {19} МБ\nкуски {20}, капли {21}\n\nкасание — свободный режим",
                F(seconds), frameMs.n, JobsUtility.JobWorkerCount, frameMs.Mean, frameMs.Pct(0.95f), frameMs.Pct(0.99f), frameMs.max,
                cpuMainMs.Mean, gpuMs.Mean, startTris, endTris, peakTris, mesher.chunkMs.Mean, mesher.chunkMs.max, mesher.batchMs.Mean,
                islandMs.Mean, islandMs.max, gcAlloc.Mean, framesAllocating, F(totalRec.LastValue / 1048576.0), piecesSpawned, blood.spawned);
        }

        static string EvName(int e)
        {
            if (e == 0) return "-";
            var s = new StringBuilder();
            if ((e & EvLight) != 0) s.Append("light ");
            if ((e & EvHeavy) != 0) s.Append("heavy ");
            if ((e & EvBurst) != 0) s.Append("burst ");
            if ((e & EvIslands) != 0) s.Append("islands ");
            if ((e & EvPiece) != 0) s.Append("piece ");
            if ((e & EvReset) != 0) s.Append("reset ");
            if ((e & EvMesh) != 0) s.Append("mesh ");
            return s.ToString().Trim();
        }

        GUIStyle small, big;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (rt != null) GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), rt, ScaleMode.ScaleToFit, false);
            if (noHud) return;
            if (small == null)
            {
                small = new GUIStyle(GUI.skin.label) { fontSize = math.max(12, Screen.height / 70) };
                big = new GUIStyle(GUI.skin.label) { fontSize = math.max(14, Screen.height / 48), wordWrap = true };
                small.normal.textColor = Color.white; big.normal.textColor = Color.white;
            }
            GUI.Label(new Rect(8, 8, Screen.width - 16, 40), hud, small);
            if (summary.Length > 0)
            {
                GUI.color = new Color(0, 0, 0, 0.7f);
                GUI.DrawTexture(new Rect(0, Screen.height * 0.08f, Screen.width, Screen.height * 0.5f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(16, Screen.height * 0.09f, Screen.width - 32, Screen.height * 0.5f), summary, big);
            }
        }

        void OnDestroy()
        {
            mesher?.Dispose();
            blood?.Dispose();
            foreach (var b in bodies) b.Dispose();
            foreach (var p in pieces) if (!bodies.Contains(p.body)) p.body.Dispose();
            if (pristine.IsCreated) pristine.Dispose();
            carveRes.Dispose(); islandRes.Dispose(); stamp.Dispose(); queue.Dispose(); seeds.Dispose();
            compVox.Dispose(); comps.Dispose(); compMin.Dispose(); compMax.Dispose();
            gcRec.Dispose(); totalRec.Dispose(); sysRec.Dispose(); gcUsedRec.Dispose(); gfxRec.Dispose();
        }
    }
}
