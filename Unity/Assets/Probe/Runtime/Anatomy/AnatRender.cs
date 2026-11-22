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
        static MaterialPropertyBlock mpb;
        static readonly int ChunkOrgId = Shader.PropertyToID("_ChunkOrg");

        public int ChunkCount => nch.x * nch.y * nch.z;
        public int3 ChunkCoord(int i) => new int3(i % nch.x, (i / nch.x) % nch.y, i / (nch.x * nch.y));

        public AnatBody(AnatGrid g, Transform parent, Material material, int bodyId)
        {
            this.g = g;
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
                r.SetPropertyBlock(mpb);
                r.enabled = false;
                dirty[i] = true;
            }
            dirtyCount = n;
        }

        public int Triangles { get { int t = 0; for (int i = 0; i < quads.Length; i++) t += quads[i] * 2; return t; } }

        /// Собрать все чанки (джобами, все потоки) и дождаться.
        public void BuildAll(NativeArray<VertexAttributeDescriptor> layout)
        {
            int n = ChunkCount;
            var orgs = new NativeArray<int3>(n, Allocator.TempJob);
            var st = new NativeArray<long>(n * 4, Allocator.TempJob);
            for (int i = 0; i < n; i++) orgs[i] = ChunkCoord(i) * AnatMesher.CS;
            var data = Mesh.AllocateWritableMeshData(n);
            new AnatChunkJob { mat = g.mat, origins = orgs, layout = layout, dims = g.dims, meshes = data, stats = st }.Schedule(n, 1).Complete();
            Mesh.ApplyAndDisposeWritableMeshData(data, meshes, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            for (int i = 0; i < n; i++)
            {
                meshes[i].bounds = new Bounds(new Vector3(16, 16, 16), new Vector3(34, 34, 34));
                quads[i] = (int)st[i * 4 + 1]; naive[i] = (int)st[i * 4];
                rs[i].enabled = quads[i] > 0;
                dirty[i] = false;
            }
            dirtyCount = 0;
            orgs.Dispose(); st.Dispose();
        }

        public void Destroy()
        {
            foreach (var m in meshes) if (m != null) Object.DestroyImmediate(m);
            if (root != null) Object.DestroyImmediate(root.gameObject);
        }
    }

    public static class AnatPalette
    {
        /// Палитра в шейдер: _AnatPal (цвет поверхности, линейный; a — разброс), _AnatCut (цвет на срезе), _AnatPar (x — влажность).
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
                par[i] = new Vector4(d.wet, d.strength, d.bleed, d.cls);
            }
            Shader.SetGlobalVectorArray("_AnatPal", pal);
            Shader.SetGlobalVectorArray("_AnatCut", cut);
            Shader.SetGlobalVectorArray("_AnatPar", par);
        }
    }
}
