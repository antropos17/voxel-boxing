using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxProbe
{
    /// Вершина 12 байт: half4 (x, y, z в ячейках чанка со смещением угла; w = материал·8 + грань) и snorm8×4 (нормаль, затенение угла 0..3 ×42).
    public struct AVert
    {
        public half x, y, z, w;
        public sbyte nx, ny, nz, ao;
    }

    /// Сетка анатомического тела. Грань — между сплошной для отсечения ячейкой (всё, кроме пусто и воздуха) и несплошной.
    /// Сглаживание: угол сдвигается по восьми ячейкам вокруг него: off = k·S·(1/cs − 1/(8 − cs)), S — сумма смещений сплошных ячеек (±0,5),
    /// cs — их число. На плоскости S лежит вдоль нормали и смещение 0; выпуклое ребро скругляется, вогнутое заполняется.
    /// Нормаль угла — градиент заполненности по 4×4×4 ячейкам (Grad), смешанный с нормалью грани. Прямоугольники объединяются только из граней, у которых все четыре угла плоские
    /// (полупространство из 4 сплошных ячеек): тогда и все вершины на краю прямоугольника лежат точно на сетке, щелей нет.
    public static class AnatMesher
    {
        public const int CS = 32, P = CS + 4, L = CS + 1;
        public const int ModeRender = 0, ModeNaive = 1, ModeGreedyAO = 2;
        public const float K = 0.7f;

        public static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
        };

        static int3 Dir(int d) => d == 0 ? new int3(1, 0, 0) : d == 1 ? new int3(-1, 0, 0) : d == 2 ? new int3(0, 1, 0) : d == 3 ? new int3(0, -1, 0) : d == 4 ? new int3(0, 0, 1) : new int3(0, 0, -1);
        /// Оси плоскости грани: X → (a=y, b=z), Y → (a=x, b=z), Z → (a=x, b=y).
        static int3 AxA(int A) => A == 0 ? new int3(0, 1, 0) : new int3(1, 0, 0);
        static int3 AxB(int A) => A == 2 ? new int3(0, 1, 0) : new int3(0, 0, 1);

        /// Строит вершины чанка (по 4 на прямоугольник). Возвращает число открытых граней (наивная сетка).
        public static int Build(NativeArray<byte> mat, int3 dims, int3 org, int mode, ref NativeList<AVert> verts)
        {
            var occ = new NativeArray<byte>(P * P * P, Allocator.Temp, NativeArrayOptions.ClearMemory);
            var dm = new NativeArray<byte>(P * P * P, Allocator.Temp, NativeArrayOptions.ClearMemory);
            int W = dims.x, H = dims.y, D = dims.z;
            bool any = false;
            for (int pz = 0; pz < P; pz++)
            {
                int gz = org.z + pz - 2; if (gz < 0 || gz >= D) continue;
                for (int py = 0; py < P; py++)
                {
                    int gy = org.y + py - 2; if (gy < 0 || gy >= H) continue;
                    int gi = W * (gy + H * gz), pi = P * (py + P * pz);
                    for (int px = 0; px < P; px++)
                    {
                        int gx = org.x + px - 2; if (gx < 0 || gx >= W) continue;
                        byte m = mat[gi + gx];
                        if (m == AnatMat.Empty || m == AnatMat.Air) continue;
                        occ[pi + px] = 1;
                        if (m != AnatMat.Covered) { dm[pi + px] = m; if (px >= 2 && px <= CS + 1 && py >= 2 && py <= CS + 1 && pz >= 2 && pz <= CS + 1) any = true; }
                    }
                }
            }
            int naive = 0;
            if (!any) { occ.Dispose(); dm.Dispose(); return 0; }

            // углы решётки: cs и 2S (целые −4..4)
            var lcs = new NativeArray<sbyte>(L * L * L, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var ls = new NativeArray<int>(L * L * L, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var lg = new NativeArray<int>(L * L * L, Allocator.Temp, NativeArrayOptions.ClearMemory); // 0 — не считан
            for (int i = 0; i < lcs.Length; i++) lcs[i] = -1;

            var keys = new NativeArray<int>(CS * CS, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int dir = 0; dir < 6; dir++)
            {
                int A = dir >> 1; bool pos = (dir & 1) == 0;
                int3 n = Dir(dir), ea = AxA(A), eb = AxB(A);
                for (int d = 0; d < CS; d++)
                {
                    for (int k = 0; k < CS * CS; k++) keys[k] = 0;
                    for (int b = 0; b < CS; b++)
                    for (int a = 0; a < CS; a++)
                    {
                        int3 c = (A == 0 ? new int3(d, a, b) : A == 1 ? new int3(a, d, b) : new int3(a, b, d));
                        int pi = PI(c + 2);
                        byte m = dm[pi];
                        if (m == 0) continue;
                        int3 nb = c + 2 + n;
                        if (occ[PI(nb)] != 0) continue;
                        naive++;
                        if (mode == ModeNaive) continue;
                        // углы грани в решётке: плоскость pd, (a0|a1, b0|b1)
                        int pd = d + (pos ? 1 : 0);
                        int3 l00 = Lat(A, pd, a, b), l01 = Lat(A, pd, a, b + 1), l11 = Lat(A, pd, a + 1, b + 1), l10 = Lat(A, pd, a + 1, b);
                        int ao0 = Ao(occ, nb, ea, eb, -1, -1), ao1 = Ao(occ, nb, ea, eb, -1, 1), ao2 = Ao(occ, nb, ea, eb, 1, 1), ao3 = Ao(occ, nb, ea, eb, 1, -1);
                        bool merge;
                        if (mode == ModeGreedyAO) { int s = ao0 | (ao1 << 2) | (ao2 << 4) | (ao3 << 6); merge = s == 0 || s == 0x55 || s == 0xAA || s == 0xFF; keys[b * CS + a] = merge ? (m | (s << 8)) + 1 : -(m + 1); if (!merge) Emit(ref verts, A, pos, dir, pd, a, b, 1, 1, m, ao0, ao1, ao2, ao3, l00, l01, l11, l10, occ, lcs, ls, lg, false); continue; }
                        merge = Flat(occ, lcs, ls, lg, l00, n) && Flat(occ, lcs, ls, lg, l01, n) && Flat(occ, lcs, ls, lg, l11, n) && Flat(occ, lcs, ls, lg, l10, n);
                        if (merge) keys[b * CS + a] = m + 1;
                        else Emit(ref verts, A, pos, dir, pd, a, b, 1, 1, m, ao0, ao1, ao2, ao3, l00, l01, l11, l10, occ, lcs, ls, lg, true);
                    }
                    if (mode == ModeNaive) continue;
                    // жадное объединение одинаковых ключей
                    int pd2 = d + (pos ? 1 : 0);
                    for (int b = 0; b < CS; b++)
                    for (int a = 0; a < CS; a++)
                    {
                        int key = keys[b * CS + a];
                        if (key <= 0) continue;
                        int w = 1;
                        while (a + w < CS && keys[b * CS + a + w] == key) w++;
                        int h = 1;
                        while (b + h < CS)
                        {
                            bool ok = true;
                            for (int q = 0; q < w; q++) if (keys[(b + h) * CS + a + q] != key) { ok = false; break; }
                            if (!ok) break;
                            h++;
                        }
                        for (int hb = 0; hb < h; hb++) for (int q = 0; q < w; q++) keys[(b + hb) * CS + a + q] = 0;
                        int mm = (key - 1) & 0xFF, aoS = mode == ModeGreedyAO ? ((key - 1) >> 8) & 0xFF : 0xFF;
                        Emit(ref verts, A, pos, dir, pd2, a, b, w, h, (byte)mm, aoS & 3, (aoS >> 2) & 3, (aoS >> 4) & 3, (aoS >> 6) & 3,
                            Lat(A, pd2, a, b), Lat(A, pd2, a, b + h), Lat(A, pd2, a + w, b + h), Lat(A, pd2, a + w, b), occ, lcs, ls, lg, false);
                    }
                }
            }
            occ.Dispose(); dm.Dispose(); lcs.Dispose(); ls.Dispose(); lg.Dispose(); keys.Dispose();
            return naive;
        }

        static int PI(int3 p) => p.x + P * (p.y + P * p.z);
        static int LI(int3 l) => l.x + L * (l.y + L * l.z);
        static int3 Lat(int A, int d, int a, int b) => A == 0 ? new int3(d, a, b) : A == 1 ? new int3(a, d, b) : new int3(a, b, d);

        /// Угол решётки l (0..32): восемь ячеек вокруг — в поле с отступом 2 это (l.x+1..l.x+2, ...).
        static void Corner(NativeArray<byte> occ, NativeArray<sbyte> lcs, NativeArray<int> ls, int3 l, out int cs, out int3 s2)
        {
            int li = LI(l);
            if (lcs[li] >= 0) { cs = lcs[li]; int v = ls[li]; s2 = new int3((v & 15) - 8, ((v >> 4) & 15) - 8, ((v >> 8) & 15) - 8); return; }
            cs = 0; s2 = 0;
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
                if (occ[PI(l + 1 + new int3(dx, dy, dz))] != 0) { cs++; s2 += new int3(dx * 2 - 1, dy * 2 - 1, dz * 2 - 1); }
            lcs[li] = (sbyte)cs; ls[li] = (s2.x + 8) | ((s2.y + 8) << 4) | ((s2.z + 8) << 8);
        }

        static bool Flat(NativeArray<byte> occ, NativeArray<sbyte> lcs, NativeArray<int> ls, NativeArray<int> lg, int3 l, int3 n)
        {
            Corner(occ, lcs, ls, l, out int cs, out int3 s2);
            if (cs != 4 || !math.all(s2 == -4 * n)) return false;
            int3 g = Grad(occ, lg, l);
            return math.all(g * (1 - math.abs(n)) == 0);
        }

        /// Градиент заполненности вокруг угла по 4×4×4 ячейкам (веса 1,2,2,1; смещения −3,−1,1,3) — для нормали.
        /// На плоскости шириной от двух ячеек в каждую сторону он строго вдоль нормали грани.
        static int3 Grad(NativeArray<byte> occ, NativeArray<int> lg, int3 l)
        {
            int li = LI(l);
            int v = lg[li];
            if (v != 0) return new int3((v & 1023) - 512, ((v >> 10) & 1023) - 512, ((v >> 20) & 1023) - 512);
            int3 g = 0;
            for (int dz = 0; dz < 4; dz++)
            {
                int wz = dz == 0 || dz == 3 ? 1 : 2, oz = dz * 2 - 3;
                for (int dy = 0; dy < 4; dy++)
                {
                    int wy = dy == 0 || dy == 3 ? 1 : 2, oy = dy * 2 - 3;
                    for (int dx = 0; dx < 4; dx++)
                    {
                        if (occ[PI(l + new int3(dx, dy, dz))] == 0) continue;
                        int wx = dx == 0 || dx == 3 ? 1 : 2, ox = dx * 2 - 3;
                        g += new int3(ox * wy * wz, oy * wx * wz, oz * wx * wy);
                    }
                }
            }
            lg[li] = (g.x + 512) | ((g.y + 512) << 10) | ((g.z + 512) << 20) | (1 << 30);
            return g;
        }

        static int Ao(NativeArray<byte> occ, int3 air, int3 ea, int3 eb, int sa, int sb)
        {
            int s1 = occ[PI(air + ea * sa)], s2 = occ[PI(air + eb * sb)], c = occ[PI(air + ea * sa + eb * sb)];
            return s1 != 0 && s2 != 0 ? 0 : 3 - (s1 + s2 + c);
        }

        static void Emit(ref NativeList<AVert> verts, int A, bool pos, int dir, int pd, int a, int b, int w, int h, byte m,
            int ao0, int ao1, int ao2, int ao3, int3 l0, int3 l1, int3 l2, int3 l3,
            NativeArray<byte> occ, NativeArray<sbyte> lcs, NativeArray<int> ls, NativeArray<int> lg, bool smooth)
        {
            bool flip = (A == 0 && pos) || (A == 1 && !pos) || (A == 2 && pos);
            if (flip) { var t = l1; l1 = l3; l3 = t; int ta = ao1; ao1 = ao3; ao3 = ta; }
            float3 fn = Dir(dir);
            int wv = m * 8 + dir;
            verts.Add(V(l0, ao0, wv, fn, smooth, occ, lcs, ls, lg));
            verts.Add(V(l1, ao1, wv, fn, smooth, occ, lcs, ls, lg));
            verts.Add(V(l2, ao2, wv, fn, smooth, occ, lcs, ls, lg));
            verts.Add(V(l3, ao3, wv, fn, smooth, occ, lcs, ls, lg));
        }

        static AVert V(int3 l, int ao, int wv, float3 fn, bool smooth, NativeArray<byte> occ, NativeArray<sbyte> lcs, NativeArray<int> ls, NativeArray<int> lg)
        {
            float3 p = l, nr = fn;
            if (smooth)
            {
                Corner(occ, lcs, ls, l, out int cs, out int3 s2);
                if (cs > 0 && cs < 8)
                {
                    float3 S = (float3)s2 * 0.5f;
                    p += K * S * (1f / cs - 1f / (8 - cs));
                }
                float3 g = Grad(occ, lg, l);
                float gl = math.length(g);
                if (gl > 1f) nr = math.normalize(-g / gl * 0.85f + fn * 0.15f);
            }
            return new AVert
            {
                x = (half)p.x, y = (half)p.y, z = (half)p.z, w = (half)(float)wv,
                nx = (sbyte)math.round(nr.x * 127), ny = (sbyte)math.round(nr.y * 127), nz = (sbyte)math.round(nr.z * 127), ao = (sbyte)(ao * 42),
            };
        }
    }

    /// Сетки чанков анатомического тела в Mesh.MeshData. stats[i] = (открытые грани, прямоугольники, тики начала, тики конца).
    [BurstCompile(CompileSynchronously = true)]
    public struct AnatChunkJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> mat;
        [ReadOnly] public NativeArray<int3> origins;
        [ReadOnly] public NativeArray<VertexAttributeDescriptor> layout;
        public int3 dims;
        public Mesh.MeshDataArray meshes;
        [NativeDisableParallelForRestriction] public NativeArray<long> stats;

        public void Execute(int i)
        {
            long t0 = ProfilerUnsafeUtility.Timestamp;
            var verts = new NativeList<AVert>(4096, Allocator.Temp);
            int naive = AnatMesher.Build(mat, dims, origins[i], AnatMesher.ModeRender, ref verts);
            int nv = verts.Length, nq = nv / 4, ni = nq * 6;
            var md = meshes[i];
            md.SetVertexBufferParams(nv, layout);
            var dst = md.GetVertexData<AVert>();
            NativeArray<AVert>.Copy(verts.AsArray(), dst, nv);
            bool big = nv > 65535;
            md.SetIndexBufferParams(ni, big ? IndexFormat.UInt32 : IndexFormat.UInt16);
            if (big)
            {
                var idx = md.GetIndexData<uint>();
                for (int q = 0; q < nq; q++)
                {
                    int o = q * 6; uint b = (uint)(q * 4);
                    bool alt = dst[q * 4].ao + dst[q * 4 + 2].ao < dst[q * 4 + 1].ao + dst[q * 4 + 3].ao;
                    if (!alt) { idx[o] = b; idx[o + 1] = b + 1; idx[o + 2] = b + 2; idx[o + 3] = b; idx[o + 4] = b + 2; idx[o + 5] = b + 3; }
                    else { idx[o] = b + 1; idx[o + 1] = b + 2; idx[o + 2] = b + 3; idx[o + 3] = b + 1; idx[o + 4] = b + 3; idx[o + 5] = b; }
                }
            }
            else
            {
                var idx = md.GetIndexData<ushort>();
                for (int q = 0; q < nq; q++)
                {
                    int o = q * 6, b = q * 4;
                    bool alt = dst[b].ao + dst[b + 2].ao < dst[b + 1].ao + dst[b + 3].ao;
                    if (!alt) { idx[o] = (ushort)b; idx[o + 1] = (ushort)(b + 1); idx[o + 2] = (ushort)(b + 2); idx[o + 3] = (ushort)b; idx[o + 4] = (ushort)(b + 2); idx[o + 5] = (ushort)(b + 3); }
                    else { idx[o] = (ushort)(b + 1); idx[o + 1] = (ushort)(b + 2); idx[o + 2] = (ushort)(b + 3); idx[o + 3] = (ushort)(b + 1); idx[o + 4] = (ushort)(b + 3); idx[o + 5] = (ushort)b; }
                }
            }
            md.subMeshCount = 1;
            md.SetSubMesh(0, new SubMeshDescriptor(0, ni) { vertexCount = nv, bounds = new Bounds(new Vector3(16, 16, 16), new Vector3(34, 34, 34)) },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            long t1 = ProfilerUnsafeUtility.Timestamp;
            verts.Dispose();
            stats[i * 4] = naive; stats[i * 4 + 1] = nq; stats[i * 4 + 2] = t0; stats[i * 4 + 3] = t1;
        }
    }

    /// Счёт граней и прямоугольников по всем чанкам в заданном режиме (наивно / сглаживание с объединением плоских / объединение по затенению без сглаживания).
    [BurstCompile(CompileSynchronously = true)]
    public struct AnatCountJob : IJob
    {
        [ReadOnly] public NativeArray<byte> mat;
        public int3 dims, nch; public int mode;
        public NativeArray<int> result;
        public void Execute()
        {
            var verts = new NativeList<AVert>(16384, Allocator.Temp);
            int naive = 0, nq = 0;
            for (int z = 0; z < nch.z; z++)
            for (int y = 0; y < nch.y; y++)
            for (int x = 0; x < nch.x; x++)
            {
                verts.Clear();
                naive += AnatMesher.Build(mat, dims, new int3(x, y, z) * AnatMesher.CS, mode, ref verts);
                nq += verts.Length / 4;
            }
            result[0] = naive; result[1] = mode == AnatMesher.ModeNaive ? naive : nq;
        }
    }
}
