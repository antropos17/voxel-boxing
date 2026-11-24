using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxProbe
{
    /// Отрисовка одной решётки анатомической головы: чанки 32³ — по объекту с MeshRenderer.
    /// Корень тела: позиция pos + rot·org (см → м), поворот rot, масштаб step; вершины — в ячейках чанка.
    public unsafe class AnatBody
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
        readonly Bounds bnd;                       // границы сетки чанка (ячейки) с запасом 15 см на изгиб шеи (AnatSkin)
        NativeArray<VS> nopool; NativeArray<int> noslot;
        static MaterialPropertyBlock mpb;
        static readonly int ChunkOrgId = Shader.PropertyToID("_ChunkOrg"), BodyParId = Shader.PropertyToID("_BodyPar");
        // перестройка части чанков (джобами)
        JobHandle job; Mesh.MeshDataArray jobData; NativeArray<int3> jobOrg; NativeArray<long> jobSt, jobPh; readonly int[] jobIdx; int jobN; bool inFlight;
        readonly Mesh[][] jobMeshes;               // массивы сеток по числу чанков в партии (заводятся один раз, без выделений в кадре)
        readonly AnatSpare spare;                  // у куска: откуда брать и куда возвращать объекты чанков
        public int rebuilt; public double rebuildMs;
        // часть 7 (только голова): прошлая сетка чанков по слоям и коробка изменений с прошлой перестройки (ячейки решётки) —
        // слои дальше AnatMesher.PrevM от коробки берутся из прошлой сетки. Без коробки (MarkDirty) — чанк целиком.
        NativeArray<UnsafeList<AVert>> prevV; NativeArray<int> prevSeg, prevNav; NativeArray<byte> prevSt;
        readonly bool[] prevOk, dfull; readonly int3[] dmn, dmx; NativeArray<int> jobChunk; NativeArray<int4> jobRmn, jobRmx;

        public int ChunkCount => nch.x * nch.y * nch.z;
        public int3 ChunkCoord(int i) => new int3(i % nch.x, (i / nch.x) % nch.y, i / (nch.x * nch.y));

        public AnatBody(AnatGrid g, Transform parent, Material material, int bodyId, AnatSpare spare = null)
        {
            this.g = g; isHead = g.name == "head"; this.spare = spare;
            if (isHead) style |= AnatMesher.StyleBend;   // голова гнётся в шее (AnatSkin); челюсть, глаза и зубы — жёстко
            bnd = new Bounds(new Vector3(16, 16, 16), Vector3.one * (40 + 2 * 15f / g.step));
            mpb ??= new MaterialPropertyBlock();
            root = spare != null && spare.roots.Count > 0 ? spare.roots.Pop() : new GameObject(g.name).transform;
            root.SetParent(parent, false);
            root.localPosition = (Vector3)(g.World(g.org) * 0.01f);
            root.localRotation = new quaternion(g.rot);
            root.localScale = Vector3.one * (g.step * 0.01f);
            nch = (g.dims + AnatMesher.CS - 1) / AnatMesher.CS;
            int n = ChunkCount;
            rs = new MeshRenderer[n]; meshes = new Mesh[n]; quads = new int[n]; naive = new int[n]; dirty = new bool[n];
            jobIdx = new int[n]; jobMeshes = new Mesh[n + 1][];
            prevOk = new bool[n]; dfull = new bool[n]; dmn = new int3[n]; dmx = new int3[n];
            for (int i = 0; i < n; i++) { dfull[i] = true; dmn[i] = int.MaxValue; dmx[i] = int.MinValue; }
            for (int i = 0; i < n; i++)
            {
                var go = spare != null && spare.chunks.Count > 0 ? spare.chunks.Pop() : null;
                MeshRenderer r;
                if (go != null) { meshes[i] = go.GetComponent<MeshFilter>().sharedMesh; r = rs[i] = go.GetComponent<MeshRenderer>(); }
                else
                {
                    go = new GameObject("c" + i);
                    meshes[i] = new Mesh { name = g.name + i };
                    meshes[i].MarkDynamic();
                    go.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                    r = rs[i] = go.AddComponent<MeshRenderer>();
                    r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                go.transform.SetParent(root, false);
                int3 c = ChunkCoord(i) * AnatMesher.CS;
                go.transform.localPosition = (Vector3)(float3)c;
                r.sharedMaterial = material;
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
            if (st != null) return new StateView { pool = st.pool.AsArray(), slot = st.slot, nch = st.nch, bs = st.bs, on = true };
            if (!nopool.IsCreated) { nopool = new NativeArray<VS>(1, Allocator.Persistent); noslot = new NativeArray<int>(1, Allocator.Persistent); }
            return new StateView { pool = nopool, slot = noslot, nch = 1, bs = 1, on = false };
        }

        public void MarkDirty(int chunk) { dfull[chunk] = true; if (!dirty[chunk]) { dirty[chunk] = true; dirtyCount++; } }
        /// Часть 7: чанк грязный из-за ячеек решётки в [mn, mx] (изменились материал, состояние или видимость).
        public void MarkDirtyCells(int chunk, int3 mn, int3 mx)
        {
            int3 o = ChunkCoord(chunk) * AnatMesher.CS; dmn[chunk] = math.min(dmn[chunk], mn - o); dmx[chunk] = math.max(dmx[chunk], mx - o);
            if (!dirty[chunk]) { dirty[chunk] = true; dirtyCount++; }
        }

        VisView Vis() => cull ? VisView.Of(g, isHead) : VisView.Off(g.mat);

        /// Забрать из решётки ячейки, сменившие видимость (AnatGrid.Touch), и пометить чанки с отступом сетки (2 ячейки + угол).
        public void SyncVis()
        {
            foreach (int c in g.tch) { if (g.tbox.TryGetValue(c, out var b)) MarkDirtyCells(c, b.Item1, b.Item2); else MarkDirty(c); }
            g.tch.Clear(); g.tbox.Clear();
            if (math.any(g.tmn > g.tmx)) return;
            int3 c0 = math.max(g.tmn - 3, 0) / AnatMesher.CS, c1 = math.min(g.tmx + 3, g.dims - 1) / AnatMesher.CS;
            for (int z = c0.z; z <= c1.z; z++) for (int y = c0.y; y <= c1.y; y++) for (int x = c0.x; x <= c1.x; x++) MarkDirtyCells(x + nch.x * (y + nch.y * z), g.tmn, g.tmx);
            g.tmn = int.MaxValue; g.tmx = int.MinValue;
        }

        /// Поставить в работу до max грязных чанков (джобами, все потоки). Готовые сетки применяются в Complete().
        public void Schedule(NativeArray<VertexAttributeDescriptor> layout, int max)
        {
            if (inFlight || dirtyCount == 0) return;
            int n = 0;
            for (int i = 0; i < dirty.Length && n < max; i++) if (dirty[i]) { jobIdx[n++] = i; dirty[i] = false; dirtyCount--; }
            jobN = n;
            jobOrg = new NativeArray<int3>(n, Allocator.TempJob); jobSt = new NativeArray<long>(n * 4, Allocator.TempJob); jobPh = new NativeArray<long>(n * AnatMesher.PhN, Allocator.TempJob);
            for (int k = 0; k < n; k++) jobOrg[k] = ChunkCoord(jobIdx[k]) * AnatMesher.CS;
            jobData = Mesh.AllocateWritableMeshData(n);
            var cj = new AnatChunkJob { mat = g.mat, origins = jobOrg, layout = layout, dims = g.dims, sv = View(), vv = Vis(), style = style, grid = new float4(g.org, g.step), dscale = 0.01f / g.step, meshes = jobData, stats = jobSt, ph = jobPh };
            jobChunk = new NativeArray<int>(n, Allocator.TempJob); jobRmn = new NativeArray<int4>(n, Allocator.TempJob); jobRmx = new NativeArray<int4>(n, Allocator.TempJob);
            if (isHead)
            {
                int nc = ChunkCount;
                if (!prevV.IsCreated)
                {
                    prevV = new NativeArray<UnsafeList<AVert>>(nc, Allocator.Persistent);
                    for (int i = 0; i < nc; i++) prevV[i] = new UnsafeList<AVert>(0, Allocator.Persistent);
                    prevSeg = new NativeArray<int>(nc * (AnatMesher.SegN + 1), Allocator.Persistent); prevNav = new NativeArray<int>(nc * (AnatMesher.SegN + 1), Allocator.Persistent);
                    prevSt = new NativeArray<byte>(nc, Allocator.Persistent);
                }
                for (int k = 0; k < n; k++)
                {
                    int i = jobIdx[k]; jobChunk[k] = i;
                    jobRmn[k] = new int4(dmn[i], prevOk[i] && !dfull[i] ? 1 : 0); jobRmx[k] = new int4(dmx[i], 0);
                    prevOk[i] = true;
                }
                cj.pv = (UnsafeList<AVert>*)prevV.GetUnsafePtr(); cj.pseg = (int*)prevSeg.GetUnsafePtr(); cj.pnav = (int*)prevNav.GetUnsafePtr(); cj.pst = (byte*)prevSt.GetUnsafePtr();
            }
            for (int k = 0; k < n; k++) { int i = jobIdx[k]; dfull[i] = false; dmn[i] = int.MaxValue; dmx[i] = int.MinValue; }
            cj.chunk = jobChunk; cj.rmn = jobRmn; cj.rmx = jobRmx;
            job = cj.Schedule(n, 1);
            inFlight = true;
        }

        public void Complete()
        {
            if (!inFlight) return;
            double w0 = AnatProf.Now(); job.Complete(); inFlight = false; double w1 = AnatProf.Now(); AnatProf.meshWait += w1 - w0;
            var ms = jobMeshes[jobN] ??= new Mesh[jobN];
            for (int k = 0; k < ms.Length; k++) ms[k] = meshes[jobIdx[k]];
            Mesh.ApplyAndDisposeWritableMeshData(jobData, ms, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            double sum = 0, freq = System.Diagnostics.Stopwatch.Frequency;
            for (int k = 0; k < ms.Length; k++)
            {
                int i = jobIdx[k];
                ms[k].bounds = bnd;
                quads[i] = (int)jobSt[k * 4 + 1]; naive[i] = (int)jobSt[k * 4];
                rs[i].enabled = quads[i] > 0;
                double cm = (jobSt[k * 4 + 3] - jobSt[k * 4 + 2]) * 1000.0 / freq; sum += cm;
                if (cm > AnatProf.chunkMax) { AnatProf.chunkMax = cm; for (int q = 0; q < AnatMesher.PhN; q++) AnatProf.chunkPh[q] = q < 4 ? jobPh[k * AnatMesher.PhN + q] * 1000.0 / freq : jobPh[k * AnatMesher.PhN + q]; }
            }
            rebuilt += ms.Length; rebuildMs += sum;
            jobOrg.Dispose(); jobSt.Dispose(); jobPh.Dispose(); jobChunk.Dispose(); jobRmn.Dispose(); jobRmx.Dispose();
            AnatProf.meshApply += AnatProf.Now() - w1;
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
            var e = new NativeArray<int>(0, Allocator.TempJob); var e4 = new NativeArray<int4>(0, Allocator.TempJob);
            new AnatChunkJob { mat = g.mat, origins = orgs, layout = layout, dims = g.dims, sv = View(), vv = Vis(), style = style, grid = new float4(g.org, g.step), dscale = 0.01f / g.step, meshes = data, stats = st, chunk = e, rmn = e4, rmx = e4 }.Schedule(n, 1).Complete();
            e.Dispose(); e4.Dispose();
            Mesh.ApplyAndDisposeWritableMeshData(data, meshes, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            for (int i = 0; i < n; i++)
            {
                meshes[i].bounds = bnd;
                quads[i] = (int)st[i * 4 + 1]; naive[i] = (int)st[i * 4];
                rs[i].enabled = quads[i] > 0;
                dirty[i] = false;
            }
            dirtyCount = 0; g.tmn = int.MaxValue; g.tmx = int.MinValue; g.tch.Clear(); g.tbox.Clear();
            for (int i = 0; i < n; i++) { prevOk[i] = false; dfull[i] = false; dmn[i] = int.MaxValue; dmx[i] = int.MinValue; }   // часть 7: прошлые сетки по слоям не заведены — следующая перестройка целиком
            orgs.Dispose(); st.Dispose();
        }

        public void Destroy()
        {
            if (inFlight) Complete();
            if (nopool.IsCreated) { nopool.Dispose(); noslot.Dispose(); }
            if (prevV.IsCreated) { for (int i = 0; i < prevV.Length; i++) prevV[i].Dispose(); prevV.Dispose(); prevSeg.Dispose(); prevNav.Dispose(); prevSt.Dispose(); }
            if (spare != null && root != null)
            {
                // кусок: объекты чанков с сетками и корень — в запас (во время игры без создания и DestroyImmediate)
                for (int i = 0; i < rs.Length; i++) { rs[i].enabled = false; rs[i].transform.SetParent(spare.hold, false); spare.chunks.Push(rs[i].gameObject); }
                root.SetParent(spare.hold, false); spare.roots.Push(root);
                return;
            }
            foreach (var m in meshes) if (m != null) Object.DestroyImmediate(m);
            if (root != null) Object.DestroyImmediate(root.gameObject);
        }
    }

    /// Запас объектов для тел кусков: объекты чанков (MeshFilter + MeshRenderer + своя сетка) и корни. Лежат под неактивным hold
    /// (удаляются вместе с корнем просмотра). Размер ограничен пиком одновременно живых кусков (AnatPieces.MaxPieces).
    public class AnatSpare
    {
        public readonly Transform hold;
        public readonly System.Collections.Generic.Stack<GameObject> chunks = new System.Collections.Generic.Stack<GameObject>();
        public readonly System.Collections.Generic.Stack<Transform> roots = new System.Collections.Generic.Stack<Transform>();
        public AnatSpare(Transform parent) { hold = new GameObject("spare").transform; hold.SetParent(parent, false); hold.gameObject.SetActive(false); }
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
