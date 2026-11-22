using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxProbe
{
    /// Голова из SDF, перенесённая из веб-версии (src/d_models.js: headSDF, слои по глубине).
    /// Сантиметры, начало у слухового прохода, +z к камере. Решётка 57×81×71, воксель 3,3 мм — как в вебе.
    public static class HeadBuilder
    {
        public const float S = 0.33f;
        public static readonly float3 Min = new float3(-9.4f, -11.95f, -11.2f);
        public static readonly float3 Max = new float3(9.4f, 14.7f, 12.1f);
        public static int3 Dims => new int3((int)math.ceil((Max.x - Min.x) / S), (int)math.ceil((Max.y - Min.y) / S), (int)math.ceil((Max.z - Min.z) / S));

        /// Строит голову в новый массив (Allocator.Persistent). Три Burst-джоба: SDF, фаска-дистанция, слои.
        public static NativeArray<byte> Build(out int3 dims)
        {
            dims = Dims;
            int n = dims.x * dims.y * dims.z;
            var vox = new NativeArray<byte>(n, Allocator.Persistent);
            var dist = new NativeArray<ushort>(n, Allocator.TempJob);
            var fill = new FillJob { vox = vox, dims = dims, s = S, org = Min };
            var h = fill.Schedule(dims.z, 1);
            h = new ChamferJob { vox = vox, dist = dist, dims = dims }.Schedule(h);
            h = new LayerJob { vox = vox, dist = dist, dims = dims, s = S, org = Min }.Schedule(dims.z, 1, h);
            h.Complete();
            dist.Dispose();
            return vox;
        }

        const float EX = 3.2f, EY = 2.3f, EZ = 6.2f, ER = 1.2f, LR = 1.5f;

        static float SdEll(float px, float py, float pz, float rx, float ry, float rz)
        {
            float a = px / rx, b = py / ry, c = pz / rz, k0 = math.sqrt(a * a + b * b + c * c);
            a /= rx; b /= ry; c /= rz;
            float k1 = math.sqrt(a * a + b * b + c * c);
            return k1 < 1e-9f ? -math.min(rx, math.min(ry, rz)) : k0 * (k0 - 1) / k1;
        }
        static float SdCap(float px, float py, float pz, float ax, float ay, float az, float bx, float by, float bz, float r, float r2)
        {
            float pax = px - ax, pay = py - ay, paz = pz - az, bax = bx - ax, bay = by - ay, baz = bz - az;
            float h = math.saturate((pax * bax + pay * bay + paz * baz) / (bax * bax + bay * bay + baz * baz));
            pax -= bax * h; pay -= bay * h; paz -= baz * h;
            return math.sqrt(pax * pax + pay * pay + paz * paz) - (r + (r2 - r) * h);
        }
        static float Smin(float a, float b, float k) { float h = k - math.abs(a - b); return h > 0 ? math.min(a, b) - h * h * 0.25f / k : math.min(a, b); }
        static float Smax(float a, float b, float k) { float h = k - math.abs(a - b); return h > 0 ? math.max(a, b) + h * h * 0.25f / k : math.max(a, b); }
        static float Gs(float dx, float dy, float dz, float r) => math.exp(-(dx * dx + dy * dy + dz * dz) / (r * r));
        static bool InEll(float x, float y, float z, float a, float b, float c) { x /= a; y /= b; z /= c; return x * x + y * y + z * z < 1; }

        static bool Almond(float ax, float y)
        {
            float u = (ax - EX) / 1.45f; if (u <= -1 || u >= 1) return false;
            float w = math.pow(1 - u * u, 0.7f), dy = y - EY;
            return dy < 0.5f * w + 0.07f * u && dy > -0.33f * w + 0.05f * u;
        }

        public static float Sdf(float x, float y, float z)
        {
            float ax = math.abs(x), d, e;
            d = SdEll(x, y - 3.4f, z + 1.2f, 7.5f, 9.4f, 9.4f);
            d = Smin(d, SdEll(x, y - 6.0f, z - 3.0f, 6.3f, 5.0f, 5.6f), 2.0f);
            d = Smin(d, SdEll(x, y - 3.95f, z - 6.45f, 5.5f, 1.25f, 2.25f), 1.0f);
            d = Smin(d, SdEll(ax - 4.4f, y - 0.9f, z - 5.6f, 2.3f, 1.5f, 2.2f), 1.2f);
            d = Smin(d, SdEll(ax - 6.3f, y - 0.7f, z - 2.0f, 1.2f, 0.95f, 3.0f), 1.0f);
            d = Smin(d, SdEll(x, y + 1.6f, z - 5.6f, 4.1f, 3.6f, 3.4f), 1.6f);
            d = Smin(d, SdEll(x, y + 4.3f, z - 6.9f, 3.2f, 2.0f, 2.4f), 1.2f);
            d = Smin(d, SdEll(x, y + 6.9f, z - 3.6f, 5.2f, 2.6f, 5.0f), 1.8f);
            d = Smin(d, SdEll(x, y + 8.6f, z - 7.1f, 2.5f, 2.0f, 1.7f), 1.4f);
            d = Smin(d, SdEll(ax - 4.9f, y + 6.4f, z - 0.2f, 1.3f, 1.8f, 1.9f), 1.4f);
            d = Smin(d, SdCap(ax, y, z, 4.9f, -6.0f, 0.2f, 5.0f, -0.8f, 0.6f, 1.2f, 1.2f), 1.2f);
            d = Smin(d, SdEll(x, y + 7.4f, z + 2.4f, 5.0f, 4.2f, 4.8f), 2.2f);
            e = SdCap(x, y, z, 0, 3.0f, 8.3f, 0, -1.4f, 10.3f, 0.55f, 0.72f);
            e = Smin(e, SdEll(x, y + 1.75f, z - 10.15f, 1.1f, 0.95f, 0.95f), 0.8f);
            e = Smin(e, SdEll(ax - 1.3f, y + 2.25f, z - 8.95f, 0.82f, 0.72f, 0.95f), 0.6f);
            d = Smin(d, e, 0.9f);
            d = Smin(d, SdEll(x, y + 4.45f, z - 8.55f, 2.45f, 0.72f, 1.0f), 0.7f);
            d = Smin(d, SdEll(x, y + 5.95f, z - 8.35f, 2.25f, 0.75f, 0.95f), 0.7f);
            e = SdEll(ax - 7.65f, y - 0.6f, z + 1.0f, 0.95f, 3.1f, 2.0f);
            e = Smax(e, -SdEll(ax - 8.35f, y - 0.2f, z + 0.7f, 0.55f, 1.5f, 1.05f), 0.3f);
            d = Smin(d, e, 0.5f);
            d = Smax(d, -SdEll(ax - EX, y - EY, z - 7.15f, 1.8f, 1.4f, 1.55f), 0.6f);
            d = Smax(d, -SdEll(x, y + 5.25f, z - 5.3f, 2.55f, 0.85f, 3.2f), 0.4f);
            d = Smax(d, -SdEll(ax - 0.56f, y + 2.72f, z - 9.35f, 0.3f, 0.24f, 0.55f), 0.15f);
            float ex = ax - EX, ey = y - EY, ez = z - EZ, er = math.sqrt(ex * ex + ey * ey + ez * ez), lid = er - LR;
            if (ez > 0.2f && Almond(ax, y)) lid = math.max(lid, 0.25f);
            d = Smin(d, lid, 0.3f);
            return math.min(d, er - ER);
        }

        /// Зубы: в вебе это отдельные сетки, здесь — воксели решётки (две дуги).
        static bool Tooth(float x, float y, float z)
        {
            float ax = math.abs(x); if (ax > 2.75f) return false;
            float u = ax / 2.75f, zf = 8.25f - 3.0f * u * u;
            if (y > -5.24f && y < -4.18f && z <= zf && z > zf - 0.72f) return true;
            zf -= 0.22f;
            return y <= -5.26f && y > -6.28f && z <= zf && z > zf - 0.72f;
        }
        static float SoftT(float x, float y, float z) { float ax = math.abs(x); return 0.62f + 1.0f * Gs(ax - 4.0f, y + 2.6f, z - 5.0f, 2.0f) + 0.8f * Gs(ax - 5.0f, y + 5.0f, z - 1.5f, 1.8f) + 0.35f * Gs(x, y + 9.0f, z - 7.5f, 1.5f) + 0.35f * Gs(ax - 6.6f, y - 3.0f, z - 2.0f, 1.5f) - 0.15f * Gs(x, y - 2.4f, z - 8.6f, 1.0f) - 0.12f * Gs(ax - 4.6f, y - 0.9f, z - 7.2f, 1.2f); }
        static float FatT(float x, float y, float z) { float ax = math.abs(x); return 0.33f + 0.5f * Gs(ax - 4.0f, y + 2.4f, z - 5.4f, 1.8f); }
        static bool NoBone(float ax, float y, float z) => (ax > 6.85f && y > -3.2f && y < 4.1f && z < 1.6f) || (z > 8.55f && y < 1.8f && y > -3.3f && ax < 2.3f) || (z > 7.9f && y < -3.4f && y > -6.9f && ax < 2.7f);
        static bool LipZone(float ax, float y, float z) { if (z < 7.9f || y > -4.55f || y < -6.3f) return false; float u = (y + 5.25f) / 1.05f; return ax < 2.3f - 0.9f * u * u; }
        static bool InTongue(float x, float y, float z) => InEll(x, y + 5.95f, z - 5.5f, 1.95f, 0.5f, 2.4f);

        [BurstCompile(CompileSynchronously = true)]
        struct FillJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<byte> vox;
            public int3 dims; public float s; public float3 org;
            public void Execute(int z)
            {
                float Z = org.z + (z + 0.5f) * s;
                for (int y = 0; y < dims.y; y++)
                {
                    float Y = org.y + (y + 0.5f) * s;
                    int i = dims.x * (y + dims.y * z);
                    for (int x = 0; x < dims.x; x++, i++)
                    {
                        float X = org.x + (x + 0.5f) * s;
                        byte t = 0;
                        if (Tooth(X, Y, Z)) t = 2;
                        else if (Sdf(X, Y, Z) <= 0) t = 1;
                        vox[i] = t;
                    }
                }
            }
        }

        /// Фаска 3-4-5 до ближайшей пустой ячейки (за решёткой — пусто), как chamfer() в вебе.
        [BurstCompile(CompileSynchronously = true)]
        public struct ChamferJob : IJob
        {
            [ReadOnly] public NativeArray<byte> vox;
            public NativeArray<ushort> dist;
            public int3 dims;
            public void Execute()
            {
                int W = dims.x, H = dims.y, D = dims.z, n = W * H * D;
                for (int i = 0; i < n; i++) dist[i] = (ushort)(vox[i] != 0 ? 60000 : 0);
                for (int pass = 0; pass < 2; pass++)
                {
                    bool fw = pass == 0;
                    for (int zi = 0; zi < D; zi++)
                    for (int yi = 0; yi < H; yi++)
                    for (int xi = 0; xi < W; xi++)
                    {
                        int x = fw ? xi : W - 1 - xi, y = fw ? yi : H - 1 - yi, z = fw ? zi : D - 1 - zi;
                        int i = x + W * (y + H * z);
                        int m = dist[i]; if (m == 0) continue;
                        for (int dz = -1; dz <= 1; dz++)
                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0) continue;
                            bool before = dz < 0 || (dz == 0 && dy < 0) || (dz == 0 && dy == 0 && dx < 0);
                            if (before != fw) continue;
                            int w = math.abs(dx) + math.abs(dy) + math.abs(dz); w = w == 1 ? 3 : w == 2 ? 4 : 5;
                            int xx = x + dx, yy = y + dy, zz = z + dz;
                            int v = (xx < 0 || yy < 0 || zz < 0 || xx >= W || yy >= H || zz >= D) ? 0 : dist[xx + W * (yy + H * zz)];
                            v += w; if (v < m) m = v;
                        }
                        dist[i] = (ushort)m;
                    }
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct LayerJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<byte> vox;
            [ReadOnly] public NativeArray<ushort> dist;
            public int3 dims; public float s; public float3 org;
            public void Execute(int z)
            {
                float Z = org.z + (z + 0.5f) * s;
                for (int y = 0; y < dims.y; y++)
                {
                    float Y = org.y + (y + 0.5f) * s;
                    int i = dims.x * (y + dims.y * z);
                    for (int x = 0; x < dims.x; x++, i++)
                    {
                        byte t0 = vox[i]; if (t0 == 0) continue;
                        if (t0 == 2) { vox[i] = VoxMat.Tooth; continue; }
                        float X = org.x + (x + 0.5f) * s, ax = math.abs(X), dep = dist[i] / 3f * s;
                        bool surf = dist[i] <= 4;
                        float ex = ax - EX, ey = Y - EY, ez = Z - EZ, er = math.sqrt(ex * ex + ey * ey + ez * ez);
                        byte t;
                        if (er < ER)
                        {
                            if (surf && ez > 0) { float r2 = ex * ex + ey * ey; t = r2 < 0.055f ? VoxMat.Pupil : r2 < 0.34f ? VoxMat.Iris : VoxMat.Eye; }
                            else t = surf ? VoxMat.Eye : VoxMat.EyeIn;
                        }
                        else if (InTongue(X, Y, Z)) t = VoxMat.Muscle;
                        else if (surf) t = LipZone(ax, Y, Z) ? VoxMat.Lip : VoxMat.Skin;
                        else if (NoBone(ax, Y, Z)) t = (ax > 6.7f || (Z > 8.4f && Y > -3.3f)) ? VoxMat.Cart : VoxMat.Muscle;
                        else
                        {
                            float soft = SoftT(X, Y, Z);
                            t = dep <= s + FatT(X, Y, Z) ? VoxMat.Fat : dep <= soft ? VoxMat.Muscle : dep <= soft + 0.72f ? VoxMat.Bone : VoxMat.Brain;
                        }
                        vox[i] = t;
                    }
                }
            }
        }
    }
}
