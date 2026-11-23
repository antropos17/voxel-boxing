using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxProbe
{
    /// Отрисовка одной решётки анатомической головы: чанки 32³ — по объекту с MeshRenderer.
    /// Корень тела: позиция pos + rot·org (см → м), поворот rot, масштаб step; вершины — в ячейках чанка.
    public class AnatBody
    {
        public readonly AnatGrid g;
        public readonly Transform root;
        public readonly int3 nch;
        public readonly MeshRenderer[] rs;
        public readonly Mesh[] meshes;
        public readonly int[] quads, naive;
        public readonly bool[] dirty;
        public int dirtyCount;
        public AnatState st;                       // состояния вокселей (этап B), у тела без повреждений — null
        public bool cull = true;                   // отсекать невидимое (AnatVis); false — рисовать всё, как на этапе A
        public int style = AnatMesher.StyleDefault; // вид поверхности (AnatMesher.Style*)
        readonly bool isHead;
        NativeArray<VS> nopool; NativeArray<int> noslot;
        static MaterialPropertyBlock mpb;
        static readonly int ChunkOrgId = Shader.PropertyToID("_ChunkOrg"), BodyParId = Shader.PropertyToID("_BodyPar");
        // перестройка части чанков (джобами)
        JobHandle job; Mesh.MeshDataArray jobData; NativeArray<int3> jobOrg; NativeArray<long> jobSt; int[] jobIdx; bool inFlight;
        public int rebuilt; public double rebuildMs;

        public int ChunkCount => nch.x * nch.y * nch.z;
        public int3 ChunkCoord(int i) => new int3(i % nch.x, (i / nch.x) % nch.y, i / (nch.x * nch.y));

        public AnatBody(AnatGrid g, Transform parent, Material material, int bodyId)
        {
            this.g = g; isHead = g.name == "head";
            mpb ??= new MaterialPropertyBlock();
            root = new GameObject(g.name).transform;
            root.SetParent(parent, false);
            root.localPosition = (Vector3)(g.World(g.org) * 0.01f);
            root.localRotation = new quaternion(g.rot);
            root.localScale = Vector3.one * (g.step * 0.01f);
            nch = (g.dims + AnatMesher.CS - 1) / AnatMesher.CS;
            int n = ChunkCount;
            rs = new MeshRenderer[n]; meshes = new Mesh[n]; quads = new int[n]; naive = new int[n]; dirty = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("c" + i);
                go.transform.SetParent(root, false);
                int3 c = ChunkCoord(i) * AnatMesher.CS;
                go.transform.localPosition = (Vector3)(float3)c;
                meshes[i] = new Mesh { name = g.name + i };
                meshes[i].MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var r = rs[i] = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mpb.SetVector(ChunkOrgId, new Vector4(c.x, c.y, c.z, bodyId));
                float3 ec = -g.org / g.step;   // центр тела (глаз — ось взгляда) в ячейках: шейдер рисует радужку и зрачок кругами
                mpb.SetVector(BodyParId, new Vector4(g.step, ec.x, ec.y, ec.z));
                r.SetPropertyBlock(mpb);
                r.enabled = false;
                dirty[i] = true;
            }
            dirtyCount = n;
        }

        StateView View()
        {
            if (st != null) return new StateView { pool = st.pool.AsArray(), slot = st.slot, nch = st.nch, on = true };
            if (!nopool.IsCreated) { nopool = new NativeArray<VS>(1, Allocator.Persistent); noslot = new NativeArray<int>(1, Allocator.Persistent); }
            return new StateView { pool = nopool, slot = noslot, nch = 1, on = false };
        }

        public void MarkDirty(int chunk) { if (!dirty[chunk]) { dirty[chunk] = true; dirtyCount++; } }

        VisView Vis() => cull ? VisView.Of(g, isHead) : VisView.Off(g.mat);

        /// Забрать из решётки ячейки, сменившие видимость (AnatGrid.Touch), и пометить чанки с отступом сетки (2 ячейки + угол).
        public void SyncVis()
        {
            foreach (int c in g.tch) MarkDirty(c);
            g.tch.Clear();
            if (math.any(g.tmn > g.tmx)) return;
            int3 c0 = math.max(g.tmn - 3, 0) / AnatMesher.CS, c1 = math.min(g.tmx + 3, g.dims - 1) / AnatMesher.CS;
            for (int z = c0.z; z <= c1.z; z++) for (int y = c0.y; y <= c1.y; y++) for (int x = c0.x; x <= c1.x; x++) MarkDirty(x + nch.x * (y + nch.y * z));
            g.tmn = int.MaxValue; g.tmx = int.MinValue;
        }

        /// Поставить в работу до max грязных чанков (джобами, все потоки). Готовые сетки применяются в Complete().
        public void Schedule(NativeArray<VertexAttributeDescriptor> layout, int max)
        {
            if (inFlight || dirtyCount == 0) return;
            var list = new System.Collections.Generic.List<int>();
            for (int i = 0; i < dirty.Length && list.Count < max; i++) if (dirty[i]) { list.Add(i); dirty[i] = false; dirtyCount--; }
            int n = list.Count; jobIdx = list.ToArray();
            jobOrg = new NativeArray<int3>(n, Allocator.TempJob); jobSt = new NativeArray<long>(n * 4, Allocator.TempJob);
            for (int k = 0; k < n; k++) jobOrg[k] = ChunkCoord(jobIdx[k]) * AnatMesher.CS;
            jobData = Mesh.AllocateWritableMeshData(n);
            job = new AnatChunkJob { mat = g.mat, origins = jobOrg, layout = layout, dims = g.dims, sv = View(), vv = Vis(), style = style, grid = new float4(g.org, g.step), dscale = 0.01f / g.step, meshes = jobData, stats = jobSt }.Schedule(n, 1);
            inFlight = true;
        }

        public void Complete()
        {
            if (!inFlight) return;
            job.Complete(); inFlight = false;
            var ms = new Mesh[jobIdx.Length];
            for (int k = 0; k < ms.Length; k++) ms[k] = meshes[jobIdx[k]];
            Mesh.ApplyAndDisposeWritableMeshData(jobData, ms, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            double sum = 0, freq = System.Diagnostics.Stopwatch.Frequency;
            for (int k = 0; k < ms.Length; k++)
            {
                int i = jobIdx[k];
                ms[k].bounds = new Bounds(new Vector3(16, 16, 16), new Vector3(40, 40, 40));
                quads[i] = (int)jobSt[k * 4 + 1]; naive[i] = (int)jobSt[k * 4];
                rs[i].enabled = quads[i] > 0;
                sum += (jobSt[k * 4 + 3] - jobSt[k * 4 + 2]) * 1000.0 / freq;
            }
            rebuilt += ms.Length; rebuildMs += sum;
            jobOrg.Dispose(); jobSt.Dispose();
        }

        /// Перестроить все грязные чанки сразу (для снимков и тестов).
        public void RebuildDirty(NativeArray<VertexAttributeDescriptor> layout) { while (dirtyCount > 0) { Schedule(layout, 64); Complete(); } }

        public int Triangles { get { int t = 0; for (int i = 0; i < quads.Length; i++) t += quads[i] * 2; return t; } }

        /// Собрать все чанки (джобами, все потоки) и дождаться.
        public void BuildAll(NativeArray<VertexAttributeDescriptor> layout)
        {
            int n = ChunkCount;
            var orgs = new NativeArray<int3>(n, Allocator.TempJob);
            var st = new NativeArray<long>(n * 4, Allocator.TempJob);
            for (int i = 0; i < n; i++) orgs[i] = ChunkCoord(i) * AnatMesher.CS;
            var data = Mesh.AllocateWritableMeshData(n);
            new AnatChunkJob { mat = g.mat, origins = orgs, layout = layout, dims = g.dims, sv = View(), vv = Vis(), style = style, grid = new float4(g.org, g.step), dscale = 0.01f / g.step, meshes = data, stats = st }.Schedule(n, 1).Complete();
            Mesh.ApplyAndDisposeWritableMeshData(data, meshes, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            for (int i = 0; i < n; i++)
            {
                meshes[i].bounds = new Bounds(new Vector3(16, 16, 16), new Vector3(40, 40, 40));
                quads[i] = (int)st[i * 4 + 1]; naive[i] = (int)st[i * 4];
                rs[i].enabled = quads[i] > 0;
                dirty[i] = false;
            }
            dirtyCount = 0; g.tmn = int.MaxValue; g.tmx = int.MinValue; g.tch.Clear();
            orgs.Dispose(); st.Dispose();
        }

        public void Destroy()
        {
            if (inFlight) Complete();
            if (nopool.IsCreated) { nopool.Dispose(); noslot.Dispose(); }
            foreach (var m in meshes) if (m != null) Object.DestroyImmediate(m);
            if (root != null) Object.DestroyImmediate(root.gameObject);
        }
    }

    public static class AnatPalette
    {
        /// Палитра в шейдер: _AnatPal (цвет поверхности, линейный; a — разброс), _AnatCut (цвет на срезе), _AnatPar (x — влажность, y — кожа:
        /// на ней видны покраснение, синяк, ссадина), _AnatDmg — цвета повреждений, _BruiseDay — секунд боя на «день» синяка.
        public static void Apply()
        {
            var pal = new Vector4[64]; var cut = new Vector4[64]; var par = new Vector4[64];
            bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int i = 0; i < AnatMat.Count; i++)
            {
                var d = AnatMat.Table[i];
                Color s = d.surf, c = d.cut;
                if (lin) { s = s.linear; c = c.linear; }
                pal[i] = new Vector4(s.r, s.g, s.b, d.vary);
                cut[i] = new Vector4(c.r, c.g, c.b, d.vary);
                par[i] = new Vector4(d.wet, AnatDamage.SkinLike((byte)i) ? 1 : i == AnatMat.Brow ? 0.4f : 0, d.bleed, d.cls);
            }
            var dmg = new Vector4[12];
            for (int i = 0; i < AnatDamage.DmgColors.Length; i++) { Color c = AnatDamage.DmgColors[i]; if (lin) c = c.linear; dmg[i] = c; }
            Shader.SetGlobalVectorArray("_AnatDmg", dmg);
            Shader.SetGlobalFloat("_BruiseDay", AnatDamage.BruiseDay);
            Shader.SetGlobalFloat("_AnatTime", 0);
            Shader.SetGlobalVectorArray("_AnatPal", pal);
            Shader.SetGlobalVectorArray("_AnatCut", cut);
            Shader.SetGlobalVectorArray("_AnatPar", par);
        }
    }
}
