using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxProbe
{
    /// Сценарий этапа B «серия ударов в бровь, скулу, нос, челюсть»: 4 круга по 4 удара через 2,5 с, затем бой идёт до 150 с
    /// (синяки меняют цвет, кровь густеет). Один и тот же в редакторе (снимки) и в игроке (замер).
    public static class AnatScenario
    {
        public struct Blow { public float t; public float3 p, dir; public float E; public bool power; public string what; }

        public static List<Blow> Blows(int seed = 7)
        {
            var r = new System.Random(seed);
            float J() => (float)(r.NextDouble() * 2 - 1) * 0.3f;
            var L = new List<Blow>();
            float t = 2;
            for (int round = 0; round < 4; round++)
            {
                L.Add(new Blow { t = t, p = new float3(2.6f + J(), 4.4f + J(), 14), dir = math.normalize(new float3(-0.1f, -0.1f, -1)), E = 3.6f, power = true, what = "бровь Л, прямой" }); t += 2.5f;
                L.Add(new Blow { t = t, p = new float3(4.4f + J(), -0.9f + J(), 14), dir = math.normalize(new float3(-0.35f, 0, -1)), E = 3.4f, power = true, what = "скула Л, хук" }); t += 2.5f;
                L.Add(new Blow { t = t, p = new float3(0.2f + J() * 0.3f, 1.0f + J(), 14), dir = new float3(0, 0, -1), E = round % 2 == 0 ? 1.2f : 3.0f, power = round % 2 == 1, what = round % 2 == 0 ? "нос, джеб" : "нос, прямой" }); t += 2.5f;
                L.Add(new Blow { t = t, p = new float3(-9, -7.4f + J(), 4.5f + J()), dir = math.normalize(new float3(1, 0.05f, -0.3f)), E = 3.8f, power = true, what = "челюсть П, хук" }); t += 2.5f;
            }
            return L;
        }

        public const float End = 150;
    }

    /// Капли крови в полёте: маленькие кубики в рамке головы (см), той же палитрой, что голова.
    public class AnatBloodFx
    {
        readonly Mesh mesh; readonly GameObject go;
        readonly NativeArray<VertexAttributeDescriptor> layout;
        public AnatBloodFx(Transform parent, Material mat, NativeArray<VertexAttributeDescriptor> layout)
        {
            this.layout = layout;
            go = new GameObject("blood-drops"); go.transform.SetParent(parent, false); go.transform.localScale = Vector3.one * 0.01f;
            mesh = new Mesh { name = "drops" }; mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock(); mpb.SetVector("_ChunkOrg", new Vector4(0, 0, 0, 99)); mpb.SetVector("_BodyPar", new Vector4(1, 0, 0, 0)); r.SetPropertyBlock(mpb);
        }

        public void Update(List<AnatDamage.Drop> drops, float time)
        {
            int n = drops.Count;
            var v = new NativeArray<AVert>(n * 24, Allocator.Temp); var idx = new NativeArray<ushort>(n * 36, Allocator.Temp);
            uint c0 = 255u << 24, c1 = (uint)math.clamp((int)math.round(time / 2), 0, 255) << 16;
            for (int k = 0; k < n; k++)
            {
                float3 p = drops[k].p; float s = 0.06f + 0.012f * drops[k].vol;
                for (int f = 0; f < 6; f++)
                {
                    int A = f >> 1; float sg = (f & 1) == 0 ? 1 : -1;
                    float3 nrm = 0; nrm[A] = sg; float3 ua = 0; ua[(A + 1) % 3] = 1; float3 ub = math.cross(nrm, ua);
                    for (int q = 0; q < 4; q++)
                    {
                        float3 cp = p + (nrm + ua * (q == 0 || q == 3 ? -1 : 1) + ub * (q < 2 ? -1 : 1)) * s;
                        v[k * 24 + f * 4 + q] = new AVert { x = (half)cp.x, y = (half)cp.y, z = (half)cp.z, w = (half)(float)(AnatMat.Blood * 8 + f), nx = (sbyte)(nrm.x * 127), ny = (sbyte)(nrm.y * 127), nz = (sbyte)(nrm.z * 127), ao = 126, c0 = c0, c1 = c1 };
                    }
                    int b = k * 24 + f * 4, o = k * 36 + f * 6;
                    idx[o] = (ushort)b; idx[o + 1] = (ushort)(b + 2); idx[o + 2] = (ushort)(b + 1); idx[o + 3] = (ushort)b; idx[o + 4] = (ushort)(b + 3); idx[o + 5] = (ushort)(b + 2);   // по часовой снаружи
                }
            }
            mesh.Clear();
            mesh.SetVertexBufferParams(n * 24, layout); mesh.SetVertexBufferData(v, 0, 0, n * 24);
            mesh.SetIndexBufferParams(n * 36, IndexFormat.UInt16); mesh.SetIndexBufferData(idx, 0, 0, n * 36);
            mesh.subMeshCount = 1; mesh.SetSubMesh(0, new SubMeshDescriptor(0, n * 36));
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200);
            v.Dispose(); idx.Dispose();
        }

        public void Destroy() { Object.DestroyImmediate(mesh); Object.DestroyImmediate(go); }
    }
}
