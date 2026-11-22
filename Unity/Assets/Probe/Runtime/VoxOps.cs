using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxProbe
{
    /// Удар: шар радиуса r удаляет воксели. Если задан r2 — область r..r2 колется на куски:
    /// тонкая оболочка на r2 отрезает её от головы, трещины между ближайшими затравками (Вороной) делят на осколки.
    /// Мягкие ткани на краю раны становятся мясом/кровью — вскрываются слои под кожей.
    [BurstCompile(CompileSynchronously = true)]
    public struct CarveJob : IJob
    {
        public NativeArray<byte> vox;
        public int3 dims;
        public float3 c;          // центр в ячейках
        public float r, r2;       // ячейки
        [ReadOnly] public NativeArray<float3> seeds;
        public int seedCount;
        public uint salt;
        public NativeArray<int> result; // [0] удалено, [1..6] bbox изменений

        public void Execute()
        {
            float R = math.max(r, r2) + 1.5f;
            int3 mn = math.max((int3)math.floor(c - R), 0), mx = math.min((int3)math.ceil(c + R), dims - 1);
            int removed = 0;
            int W = dims.x, H = dims.y;
            for (int z = mn.z; z <= mx.z; z++)
            for (int y = mn.y; y <= mx.y; y++)
            for (int x = mn.x; x <= mx.x; x++)
            {
                int i = x + W * (y + H * z);
                byte m = vox[i]; if (m == 0) continue;
                float3 p = new float3(x, y, z) + 0.5f;
                float d = math.distance(p, c);
                bool cut = false;
                if (d < r) cut = true;
                else if (r2 > 0 && d < r2)
                {
                    if (d >= r2 - 1f) cut = true;
                    else
                    {
                        float d1 = 1e9f, d2 = 1e9f;
                        for (int s = 0; s < seedCount; s++)
                        {
                            float e = math.distancesq(p, seeds[s]);
                            if (e < d1) { d2 = d1; d1 = e; } else if (e < d2) d2 = e;
                        }
                        if (math.sqrt(d2) - math.sqrt(d1) < 0.9f) cut = true;
                    }
                }
                if (cut) { vox[i] = 0; removed++; continue; }
                float rim = (r2 > 0 ? r2 : r) + 1.4f;
                if (d < rim && VoxMat.Soft(m))
                {
                    uint h = math.hash(new uint4((uint)x, (uint)y, (uint)z, salt));
                    vox[i] = (h & 3) == 0 ? VoxMat.Blood : VoxMat.Meat;
                }
            }
            result[0] = removed;
            result[1] = mn.x; result[2] = mn.y; result[3] = mn.z; result[4] = mx.x; result[5] = mx.y; result[6] = mx.z;
        }
    }

    /// Поиск оторванных кусков в области [mn, mx]. Связано с головой то, что достаёт до «якоря» (низ шеи, y < anchorY)
    /// или до края области, если край не совпадает с краем решётки (за краем считаем, что голова цела).
    /// Остальные связные области — оторванные куски: их ячейки в compVox, диапазоны в comps, рамки в compMin/compMax.
    [BurstCompile(CompileSynchronously = true)]
    public struct IslandJob : IJob
    {
        [ReadOnly] public NativeArray<byte> vox;
        public int3 dims, mn, mx;
        public int anchorY;
        public NativeArray<int> stamp; // размер решётки, переиспользуется (метка = gen)
        public NativeArray<int> queue; // размер решётки
        public int gen;
        public NativeList<int> compVox;
        public NativeList<int2> comps;
        public NativeList<int3> compMin, compMax;
        public NativeArray<int> result; // [0] ячеек посещено

        public void Execute()
        {
            compVox.Clear(); comps.Clear(); compMin.Clear(); compMax.Clear();
            int W = dims.x, H = dims.y, WH = W * H;
            int head = 0, tail = 0, visited = 0;
            bool openX0 = mn.x > 0, openX1 = mx.x < dims.x - 1, openY0 = mn.y > 0, openY1 = mx.y < dims.y - 1, openZ0 = mn.z > 0, openZ1 = mx.z < dims.z - 1;
            for (int z = mn.z; z <= mx.z; z++)
            for (int y = mn.y; y <= mx.y; y++)
            for (int x = mn.x; x <= mx.x; x++)
            {
                int i = x + W * (y + H * z);
                if (vox[i] == 0) continue;
                bool seed = y < anchorY || (x == mn.x && openX0) || (x == mx.x && openX1) || (y == mn.y && openY0) || (y == mx.y && openY1) || (z == mn.z && openZ0) || (z == mx.z && openZ1);
                if (seed && stamp[i] != gen) { stamp[i] = gen; queue[tail++] = i; }
            }
            visited += Flood(ref head, ref tail, W, WH, false, out _, out _);
            for (int z = mn.z; z <= mx.z; z++)
            for (int y = mn.y; y <= mx.y; y++)
            for (int x = mn.x; x <= mx.x; x++)
            {
                int i = x + W * (y + H * z);
                if (vox[i] == 0 || stamp[i] == gen) continue;
                head = 0; tail = 0;
                stamp[i] = gen; queue[tail++] = i;
                int start = compVox.Length;
                int cnt = Flood(ref head, ref tail, W, WH, true, out int3 bmn, out int3 bmx);
                visited += cnt;
                comps.Add(new int2(start, cnt));
                compMin.Add(bmn); compMax.Add(bmx);
            }
            result[0] = visited;
        }

        int Flood(ref int head, ref int tail, int W, int WH, bool collect, out int3 bmn, out int3 bmx)
        {
            bmn = new int3(int.MaxValue); bmx = new int3(int.MinValue);
            int n = 0;
            while (head < tail)
            {
                int i = queue[head++]; n++;
                int x = i % W, y = (i / W) % dims.y, z = i / WH;
                if (collect) { compVox.Add(i); var p = new int3(x, y, z); bmn = math.min(bmn, p); bmx = math.max(bmx, p); }
                if (x > mn.x) Push(i - 1, ref tail);
                if (x < mx.x) Push(i + 1, ref tail);
                if (y > mn.y) Push(i - W, ref tail);
                if (y < mx.y) Push(i + W, ref tail);
                if (z > mn.z) Push(i - WH, ref tail);
                if (z < mx.z) Push(i + WH, ref tail);
            }
            return n;
        }

        void Push(int j, ref int tail)
        {
            if (vox[j] == 0 || stamp[j] == gen) return;
            stamp[j] = gen; queue[tail++] = j;
        }
    }

    /// Переносит связную область из головы в решётку куска (рамка mn, размер pd) и стирает её в голове.
    [BurstCompile(CompileSynchronously = true)]
    public struct ExtractJob : IJob
    {
        public NativeArray<byte> src;
        public int3 sdims;
        [ReadOnly] public NativeArray<int> ids;
        public int start, count;
        public NativeArray<byte> dst;
        public int3 mn, pd;
        public void Execute()
        {
            int W = sdims.x, WH = sdims.x * sdims.y;
            for (int k = 0; k < count; k++)
            {
                int i = ids[start + k];
                int x = i % W - mn.x, y = (i / W) % sdims.y - mn.y, z = i / WH - mn.z;
                dst[x + pd.x * (y + pd.y * z)] = src[i];
                src[i] = 0;
            }
        }
    }

    public static class VoxRay
    {
        /// Луч в координатах решётки (ячейки). Первая занятая ячейка или -1. Перенос raycast() из веба.
        public static bool Cast(NativeArray<byte> vox, int3 dims, float3 o, float3 d, out int3 cell, out float t)
        {
            cell = default; t = 0;
            float t0 = 0, t1 = 1e9f;
            for (int k = 0; k < 3; k++)
            {
                if (math.abs(d[k]) < 1e-9f) { if (o[k] < 0 || o[k] > dims[k]) return false; }
                else
                {
                    float a = -o[k] / d[k], b = (dims[k] - o[k]) / d[k];
                    if (a > b) { float tt = a; a = b; b = tt; }
                    t0 = math.max(t0, a); t1 = math.min(t1, b);
                    if (t0 > t1) return false;
                }
            }
            float tc = t0 + 1e-4f;
            int3 c = (int3)math.floor(o + d * tc);
            int3 st = new int3(d.x > 0 ? 1 : -1, d.y > 0 ? 1 : -1, d.z > 0 ? 1 : -1);
            float3 td = new float3(d.x != 0 ? math.abs(1 / d.x) : 1e9f, d.y != 0 ? math.abs(1 / d.y) : 1e9f, d.z != 0 ? math.abs(1 / d.z) : 1e9f);
            float3 tm = new float3(
                d.x != 0 ? ((d.x > 0 ? c.x + 1 : c.x) - o.x) / d.x : 1e9f,
                d.y != 0 ? ((d.y > 0 ? c.y + 1 : c.y) - o.y) / d.y : 1e9f,
                d.z != 0 ? ((d.z > 0 ? c.z + 1 : c.z) - o.z) / d.z : 1e9f);
            for (int n = 0; n < dims.x + dims.y + dims.z + 8; n++)
            {
                if (math.all(c >= 0) && math.all(c < dims) && vox[c.x + dims.x * (c.y + dims.y * c.z)] != 0) { cell = c; t = tc; return true; }
                if (tm.x < tm.y && tm.x < tm.z) { c.x += st.x; tc = tm.x; tm.x += td.x; }
                else if (tm.y < tm.z) { c.y += st.y; tc = tm.y; tm.y += td.y; }
                else { c.z += st.z; tc = tm.z; tm.z += td.z; }
                if (tc > t1 + 1e-3f) break;
            }
            return false;
        }
    }
}
