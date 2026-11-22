using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxProbe
{
    /// Тело из вокселей: плотная решётка (1 байт), чанки 32³ — по объекту с MeshRenderer на чанк.
    public class VoxBody
    {
        public NativeArray<byte> vox;
        public int3 dims, nch;
        public float voxelSize; // метры
        public Transform root;
        public bool[] dirty;
        public int dirtyCount;
        public MeshFilter[] filters;
        public MeshRenderer[] renderers;
        public Mesh[] meshes;
        public int[] quads, naive;    // по чанкам, из последней сборки
        public bool visibleOnApply;
        public NativeArray<byte> snapshot; // копия для джобов: решётку можно менять, пока строятся сетки
        public bool busy;                  // чанки тела в работе у джобов
        Material material;

        public int ChunkCount => nch.x * nch.y * nch.z;

        public VoxBody(Transform root, Material mat, float voxelSize)
        {
            this.root = root; material = mat; this.voxelSize = voxelSize;
        }

        /// Задать решётку (тело забирает массив). Объекты чанков переиспользуются.
        public void SetGrid(NativeArray<byte> grid, int3 dims)
        {
            if (vox.IsCreated) vox.Dispose();
            if (snapshot.IsCreated && snapshot.Length != grid.Length) snapshot.Dispose();
            vox = grid; this.dims = dims;
            nch = (dims + GreedyMesher.CS - 1) / GreedyMesher.CS;
            if (!snapshot.IsCreated) snapshot = new NativeArray<byte>(grid.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            int n = ChunkCount;
            if (filters == null || filters.Length < n)
            {
                int old = filters?.Length ?? 0;
                System.Array.Resize(ref filters, n); System.Array.Resize(ref renderers, n); System.Array.Resize(ref meshes, n);
                System.Array.Resize(ref dirty, n); System.Array.Resize(ref quads, n); System.Array.Resize(ref naive, n);
                for (int i = old; i < n; i++)
                {
                    var go = new GameObject("chunk");
                    go.transform.SetParent(root, false);
                    filters[i] = go.AddComponent<MeshFilter>();
                    renderers[i] = go.AddComponent<MeshRenderer>();
                    renderers[i].sharedMaterial = material;
                    renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                    renderers[i].receiveShadows = false;
                    renderers[i].lightProbeUsage = LightProbeUsage.Off;
                    renderers[i].reflectionProbeUsage = ReflectionProbeUsage.Off;
                    meshes[i] = new Mesh { name = "chunk" };
                    meshes[i].MarkDynamic();
                    filters[i].sharedMesh = meshes[i];
                }
            }
            for (int i = 0; i < filters.Length; i++)
            {
                bool used = i < n;
                renderers[i].gameObject.SetActive(used);
                renderers[i].enabled = false;
                quads[i] = naive[i] = 0;
                dirty[i] = used;
                if (used)
                {
                    int3 c = ChunkCoord(i);
                    var t = renderers[i].transform;
                    t.localPosition = (Vector3)(float3)(c * GreedyMesher.CS) * voxelSize;
                    t.localScale = Vector3.one * voxelSize;
                }
            }
            dirtyCount = n;
        }

        public int3 ChunkCoord(int i) => new int3(i % nch.x, (i / nch.x) % nch.y, i / (nch.x * nch.y));

        /// Изменились ячейки в [mn, mx]: перестроить все чанки, которые видят их (с запасом в одну ячейку — грани и затенение соседей).
        public void MarkDirty(int3 mn, int3 mx)
        {
            mn = math.max(mn - 1, 0); mx = math.min(mx + 1, dims - 1);
            int3 c0 = mn / GreedyMesher.CS, c1 = mx / GreedyMesher.CS;
            for (int z = c0.z; z <= c1.z; z++)
            for (int y = c0.y; y <= c1.y; y++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                int i = x + nch.x * (y + nch.y * z);
                if (!dirty[i]) { dirty[i] = true; dirtyCount++; }
            }
        }

        public void MarkAllDirty()
        {
            for (int i = 0; i < ChunkCount; i++) dirty[i] = true;
            dirtyCount = ChunkCount;
        }

        public int Triangles { get { int t = 0; for (int i = 0; i < ChunkCount; i++) t += quads[i] * 2; return t; } }
        public int NaiveTriangles { get { int t = 0; for (int i = 0; i < ChunkCount; i++) t += naive[i] * 2; return t; } }
        public int Vertices { get { int t = 0; for (int i = 0; i < ChunkCount; i++) t += quads[i] * 4; return t; } }

        public void Hide() { for (int i = 0; i < ChunkCount; i++) renderers[i].enabled = false; }

        public void Dispose()
        {
            if (vox.IsCreated) vox.Dispose();
            if (snapshot.IsCreated) snapshot.Dispose();
        }
    }

    /// Планирует перестройку грязных чанков джобами и забирает результат, когда он готов (кадр не ждёт).
    /// В работе одна партия; пока она идёт, новые изменения копятся и уйдут следующей.
    public class MeshScheduler
    {
        class Part
        {
            public VoxBody body;
            public int n;
            public int[] ids = new int[64];
            public NativeArray<int3> origins;
            public NativeArray<long> stats;
            public Mesh.MeshDataArray data;
        }

        readonly List<Part> parts = new List<Part>();
        readonly List<Part> pool = new List<Part>();
        readonly List<Mesh> meshList = new List<Mesh>(64);
        NativeArray<VertexAttributeDescriptor> layout;
        JobHandle handle;
        bool inFlight;
        double scheduledAt;

        // замеры
        public readonly Stat chunkMs = new Stat(20000), batchMs = new Stat(4000), applyMs = new Stat(4000);
        public int batches, chunksBuilt;
        public float tickToMs;
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        public MeshScheduler()
        {
            layout = new NativeArray<VertexAttributeDescriptor>(GreedyMesher.Layout, Allocator.Persistent);
            tickToMs = (float)(Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility.TimestampToNanosecondsConversionRatio.Numerator /
                               (double)Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility.TimestampToNanosecondsConversionRatio.Denominator * 1e-6);
        }

        public bool InFlight => inFlight;

        /// Если партия готова — применить. Никогда не ждёт.
        public void Poll(double now)
        {
            if (!inFlight || !handle.IsCompleted) return;
            handle.Complete();
            inFlight = false;
            batchMs.Add((float)((now - scheduledAt) * 1000.0));
            sw.Restart();
            for (int p = 0; p < parts.Count; p++)
            {
                var part = parts[p];
                meshList.Clear();
                for (int k = 0; k < part.n; k++) meshList.Add(part.body.meshes[part.ids[k]]);
                Mesh.ApplyAndDisposeWritableMeshData(part.data, meshList, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                for (int k = 0; k < part.n; k++)
                {
                    int id = part.ids[k];
                    var m = part.body.meshes[id];
                    m.bounds = new Bounds(new Vector3(16, 16, 16), new Vector3(32, 32, 32));
                    int nq = (int)part.stats[k * 4 + 1];
                    part.body.quads[id] = nq;
                    part.body.naive[id] = (int)part.stats[k * 4];
                    part.body.renderers[id].enabled = nq > 0 && part.body.visibleOnApply;
                    chunkMs.Add((part.stats[k * 4 + 3] - part.stats[k * 4 + 2]) * tickToMs);
                }
                chunksBuilt += part.n;
                part.body.busy = false;
                pool.Add(part);
            }
            parts.Clear();
            applyMs.Add((float)sw.Elapsed.TotalMilliseconds);
        }

        /// Отправить грязные чанки тел в работу (если партия не идёт).
        public void Schedule(List<VoxBody> bodies, double now)
        {
            if (inFlight) return;
            JobHandle all = default;
            for (int b = 0; b < bodies.Count; b++)
            {
                var body = bodies[b];
                if (body.dirtyCount == 0 || body.busy) continue;
                var part = pool.Count > 0 ? Take() : new Part();
                part.body = body;
                part.n = 0;
                int n = body.ChunkCount;
                if (part.ids.Length < n) part.ids = new int[n];
                for (int i = 0; i < n; i++) if (body.dirty[i]) { part.ids[part.n++] = i; body.dirty[i] = false; }
                body.dirtyCount = 0;
                if (!part.origins.IsCreated || part.origins.Length < part.n)
                {
                    if (part.origins.IsCreated) { part.origins.Dispose(); part.stats.Dispose(); }
                    int cap = math.max(part.n, 32);
                    part.origins = new NativeArray<int3>(cap, Allocator.Persistent);
                    part.stats = new NativeArray<long>(cap * 4, Allocator.Persistent);
                }
                for (int k = 0; k < part.n; k++) part.origins[k] = body.ChunkCoord(part.ids[k]) * GreedyMesher.CS;
                body.vox.CopyTo(body.snapshot);
                body.busy = true;
                part.data = Mesh.AllocateWritableMeshData(part.n);
                var job = new ChunkMeshJob { vox = body.snapshot, origins = part.origins, layout = layout, dims = body.dims, meshes = part.data, stats = part.stats };
                all = JobHandle.CombineDependencies(all, job.Schedule(part.n, 1));
                parts.Add(part);
            }
            if (parts.Count == 0) return;
            handle = all;
            inFlight = true;
            scheduledAt = now;
            JobHandle.ScheduleBatchedJobs();
        }

        Part Take() { var p = pool[pool.Count - 1]; pool.RemoveAt(pool.Count - 1); return p; }

        /// Дождаться и применить (только вне замера: старт, сброс в тестах).
        public void Flush(double now)
        {
            if (inFlight) { handle.Complete(); Poll(now); }
        }

        public void Dispose()
        {
            if (inFlight) { handle.Complete(); foreach (var p in parts) p.data.Dispose(); }
            foreach (var p in parts) DisposePart(p);
            foreach (var p in pool) DisposePart(p);
            if (layout.IsCreated) layout.Dispose();
        }

        static void DisposePart(Part p) { if (p.origins.IsCreated) { p.origins.Dispose(); p.stats.Dispose(); } }
    }

    /// Накопитель замеров без выделений памяти.
    public class Stat
    {
        public readonly float[] v;
        public int n;
        public double sum;
        public float max;
        public Stat(int cap) { v = new float[cap]; }
        public void Add(float x) { if (n < v.Length) v[n] = x; n++; sum += x; if (x > max) max = x; }
        public float Mean => n > 0 ? (float)(sum / n) : 0;
        public void Clear() { n = 0; sum = 0; max = 0; }
        public float Pct(float p)
        {
            int m = math.min(n, v.Length); if (m == 0) return 0;
            var c = new float[m]; System.Array.Copy(v, c, m); System.Array.Sort(c);
            return c[math.min(m - 1, (int)math.floor(p * (m - 1) + 0.5f))];
        }
    }
}
