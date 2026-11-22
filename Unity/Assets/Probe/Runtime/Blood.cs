using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxProbe
{
    /// Частицы крови: кольцевой буфер на Max штук, обновление и сборка матриц в Burst, отрисовка RenderMeshInstanced.
    public class Blood
    {
        public const int Max = 3000;
        public struct Drop { public float3 p, v; public float life, size; public int landed; }

        NativeArray<Drop> drops;
        NativeArray<float4x4> mats;
        NativeArray<int> count;
        int next;
        JobHandle handle;
        bool scheduled;
        Mesh cube;
        RenderParams rp;
        public float floorY;
        public int Alive { get; private set; }
        public int spawned;

        public Blood(Material mat, float floorY)
        {
            drops = new NativeArray<Drop>(Max, Allocator.Persistent);
            mats = new NativeArray<float4x4>(Max, Allocator.Persistent);
            count = new NativeArray<int>(1, Allocator.Persistent);
            cube = MakeCube();
            rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = new Bounds(Vector3.zero, Vector3.one * 100) };
            this.floorY = floorY;
        }

        public void Spawn(float3 p, float3 v, float size, float life)
        {
            if (scheduled) handle.Complete();
            drops[next] = new Drop { p = p, v = v, life = life, size = size };
            next = (next + 1) % Max;
            spawned++;
        }

        public void Schedule(float dt)
        {
            var up = new UpdateJob { drops = drops, dt = dt, floorY = floorY }.Schedule(Max, 256);
            handle = new PackJob { drops = drops, mats = mats, count = count }.Schedule(up);
            scheduled = true;
        }

        public void Render()
        {
            if (!scheduled) return;
            handle.Complete(); scheduled = false;
            int n = count[0]; Alive = n;
            var m = mats.Reinterpret<Matrix4x4>();
            for (int s = 0; s < n; s += 1023)
                Graphics.RenderMeshInstanced(rp, cube, 0, m, math.min(1023, n - s), s);
        }

        public void Clear() { if (scheduled) { handle.Complete(); scheduled = false; } for (int i = 0; i < Max; i++) drops[i] = default; Alive = 0; }

        public void Dispose() { if (scheduled) handle.Complete(); drops.Dispose(); mats.Dispose(); count.Dispose(); }

        [BurstCompile(CompileSynchronously = true)]
        struct UpdateJob : IJobParallelFor
        {
            public NativeArray<Drop> drops; public float dt, floorY;
            public void Execute(int i)
            {
                var d = drops[i];
                if (d.life <= 0) return;
                d.life -= dt;
                if (d.landed == 0)
                {
                    d.v.y -= 9.81f * dt;
                    d.v *= 1f - 0.6f * dt;
                    d.p += d.v * dt;
                    if (d.p.y < floorY + d.size * 0.5f) { d.p.y = floorY + d.size * 0.25f; d.v = 0; d.landed = 1; }
                }
                drops[i] = d;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct PackJob : IJob
        {
            [ReadOnly] public NativeArray<Drop> drops; public NativeArray<float4x4> mats; public NativeArray<int> count;
            public void Execute()
            {
                int c = 0;
                for (int i = 0; i < drops.Length; i++)
                {
                    var d = drops[i];
                    if (d.life <= 0) continue;
                    float s = d.size * math.saturate(d.life * 2f);
                    float3 sc = d.landed != 0 ? new float3(s * 1.6f, s * 0.3f, s * 1.6f) : new float3(s);
                    mats[c++] = float4x4.TRS(d.p, quaternion.identity, sc);
                }
                count[0] = c;
            }
        }

        static Mesh MakeCube()
        {
            var m = new Mesh { name = "drop" };
            var v = new Vector3[24]; var n = new Vector3[24]; var t = new int[36];
            Vector3[] N = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int f = 0; f < 6; f++)
            {
                Vector3 nn = N[f], u = Mathf.Abs(nn.y) > 0.5f ? Vector3.right : Vector3.up, w = Vector3.Cross(nn, u);
                for (int k = 0; k < 4; k++)
                {
                    float a = (k == 1 || k == 2) ? 0.5f : -0.5f, b = (k >= 2) ? 0.5f : -0.5f;
                    v[f * 4 + k] = nn * 0.5f + u * a + w * b; n[f * 4 + k] = nn;
                }
                // порядок по часовой, если смотреть снаружи: проверяем по нормали
                Vector3 p0 = v[f * 4], p1 = v[f * 4 + 1], p2 = v[f * 4 + 2];
                bool ok = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), nn) > 0;
                int o = f * 6, bI = f * 4;
                if (ok) { t[o] = bI; t[o + 1] = bI + 1; t[o + 2] = bI + 2; t[o + 3] = bI; t[o + 4] = bI + 2; t[o + 5] = bI + 3; }
                else { t[o] = bI; t[o + 1] = bI + 2; t[o + 2] = bI + 1; t[o + 3] = bI; t[o + 4] = bI + 3; t[o + 5] = bI + 2; }
            }
            m.vertices = v; m.normals = n; m.triangles = t;
            m.RecalculateBounds();
            return m;
        }
    }
}
