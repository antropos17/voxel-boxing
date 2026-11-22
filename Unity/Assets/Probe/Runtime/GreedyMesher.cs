using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxProbe
{
    /// Прямоугольник объединённых граней в плоскости среза.
    /// dir: 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z. Плоские оси: X → (a=y, b=z), Y → (a=x, b=z), Z → (a=x, b=y).
    public struct Quad
    {
        public byte dir, d, a, b, w, h, mat, ao; // ao: 4 угла по 2 бита (c0,c1,c2,c3)
    }

    /// Вершина 8 байт: half4 (x, y, z в пределах чанка 0..32; w = материал*32 + грань*4 + затенение угла).
    public struct VoxVertex
    {
        public half x, y, z, w;
    }

    /// Binary greedy meshing: столбцы занятости — 64-битные маски (32 ячейки + по одной с каждой стороны из соседних чанков),
    /// открытые грани — сдвиг и AND-NOT по маске, объединение — по битовым строкам среза (tzcnt), ключ = материал + затенение 4 углов.
    /// Грани с неравным затенением углов не объединяются (иначе градиент растянется на весь прямоугольник).
    public static class GreedyMesher
    {
        public const int CS = 32, P = CS + 2;
        public const int ModeRender = 0, ModeNoAO = 1;

        public static readonly VertexAttributeDescriptor[] Layout = { new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float16, 4) };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Bit(ulong v, int i) => (int)((v >> i) & 1ul);

        /// Строит прямоугольники для чанка с началом org. Читает соседние чанки на одну ячейку (отсечение и затенение через границу).
        /// Возвращает число открытых граней (наивная сетка: квадрат на каждую открытую сторону).
        public static int BuildQuads(NativeArray<byte> vox, int3 dims, int3 org, int mode, ref NativeList<Quad> quads)
        {
            var cols = new NativeArray<ulong>(3 * P * P, Allocator.Temp, NativeArrayOptions.ClearMemory);
            int W = dims.x, H = dims.y, D = dims.z;
            // столбцы занятости в координатах с полем: индекс = коорд + 1
            int x0 = math.max(org.x - 1, 0), x1 = math.min(org.x + CS + 1, W);
            int y0 = math.max(org.y - 1, 0), y1 = math.min(org.y + CS + 1, H);
            int z0 = math.max(org.z - 1, 0), z1 = math.min(org.z + CS + 1, D);
            for (int gz = z0; gz < z1; gz++)
            for (int gy = y0; gy < y1; gy++)
            {
                int i = x0 + W * (gy + H * gz);
                int pz = gz - org.z + 1, py = gy - org.y + 1;
                for (int gx = x0; gx < x1; gx++, i++)
                {
                    if (vox[i] == 0) continue;
                    int px = gx - org.x + 1;
                    cols[0 * P * P + pz * P + py] |= 1ul << px;
                    cols[1 * P * P + pz * P + px] |= 1ul << py;
                    cols[2 * P * P + py * P + px] |= 1ul << pz;
                }
            }

            var keys = new NativeArray<uint>(CS * CS * CS, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var masks = new NativeArray<uint>(CS * CS, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int naive = 0;
            for (int dir = 0; dir < 6; dir++)
            {
                int A = dir >> 1; bool pos = (dir & 1) == 0;
                int cb = A * P * P;
                for (int k = 0; k < CS * CS; k++) masks[k] = 0;
                for (int b = 0; b < CS; b++)
                for (int a = 0; a < CS; a++)
                {
                    ulong col = cols[cb + (b + 1) * P + (a + 1)];
                    if (col == 0) continue;
                    ulong f = pos ? col & ~(col >> 1) : col & ~(col << 1);
                    uint inner = (uint)((f >> 1) & 0xFFFFFFFFul);
                    if (inner == 0) continue;
                    naive += math.countbits(inner);
                    while (inner != 0)
                    {
                        int d = math.tzcnt(inner); inner &= inner - 1;
                        int gx, gy, gz;
                        if (A == 0) { gx = d; gy = a; gz = b; } else if (A == 1) { gx = a; gy = d; gz = b; } else { gx = a; gy = b; gz = d; }
                        byte mat = vox[(org.x + gx) + W * ((org.y + gy) + H * (org.z + gz))];
                        uint key = mat;
                        if (mode == ModeRender)
                        {
                            int L = d + 1 + (pos ? 1 : -1), pa = a + 1, pb = b + 1;
                            int a0 = Ao(cols, cb, L, pa, pb, -1, -1), a1 = Ao(cols, cb, L, pa, pb, -1, 1), a2 = Ao(cols, cb, L, pa, pb, 1, 1), a3 = Ao(cols, cb, L, pa, pb, 1, -1);
                            key |= (uint)(a0 | (a1 << 2) | (a2 << 4) | (a3 << 6)) << 8;
                        }
                        keys[d * CS * CS + b * CS + a] = key;
                        masks[d * CS + b] |= 1u << a;
                    }
                }
                for (int d = 0; d < CS; d++)
                {
                    int kb = d * CS * CS, mb = d * CS;
                    for (int v = 0; v < CS; v++)
                    {
                        uint m = masks[mb + v];
                        while (m != 0)
                        {
                            int u = math.tzcnt(m);
                            uint key = keys[kb + v * CS + u];
                            uint ao = key >> 8;
                            bool merge = mode == ModeNoAO || ao == 0 || ao == 0x55 || ao == 0xAA || ao == 0xFF;
                            int w = 1;
                            if (merge) while (u + w < CS && ((m >> (u + w)) & 1u) != 0 && keys[kb + v * CS + u + w] == key) w++;
                            uint run = (w == 32 ? 0xFFFFFFFFu : ((1u << w) - 1u)) << u;
                            int h = 1;
                            if (merge)
                                while (v + h < CS)
                                {
                                    uint mm = masks[mb + v + h];
                                    if ((mm & run) != run) break;
                                    bool ok = true;
                                    int rb = kb + (v + h) * CS;
                                    for (int q = u; q < u + w; q++) if (keys[rb + q] != key) { ok = false; break; }
                                    if (!ok) break;
                                    masks[mb + v + h] = mm & ~run;
                                    h++;
                                }
                            m &= ~run;
                            quads.Add(new Quad { dir = (byte)dir, d = (byte)d, a = (byte)u, b = (byte)v, w = (byte)w, h = (byte)h, mat = (byte)(key & 0xFF), ao = (byte)ao });
                        }
                    }
                }
            }
            cols.Dispose(); keys.Dispose(); masks.Dispose();
            return naive;
        }

        /// Затенение угла 0..3 (3 — светло) по трём соседям в слое воздуха.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Ao(NativeArray<ulong> cols, int cb, int L, int pa, int pb, int sa, int sb)
        {
            int s1 = Bit(cols[cb + pb * P + (pa + sa)], L);
            int s2 = Bit(cols[cb + (pb + sb) * P + pa], L);
            int c = Bit(cols[cb + (pb + sb) * P + (pa + sa)], L);
            return s1 == 1 && s2 == 1 ? 0 : 3 - (s1 + s2 + c);
        }

        /// Вершины прямоугольников. Порядок обхода — по часовой, если смотреть снаружи (лицевая сторона Unity).
        public static void EmitVertices(ref NativeList<Quad> quads, NativeArray<VoxVertex> verts)
        {
            for (int qi = 0; qi < quads.Length; qi++)
            {
                var q = quads[qi];
                int A = q.dir >> 1; bool pos = (q.dir & 1) == 0;
                int pd = q.d + (pos ? 1 : 0);
                int a0 = q.a, a1 = q.a + q.w, b0 = q.b, b1 = q.b + q.h;
                // c0=(a0,b0) c1=(a0,b1) c2=(a1,b1) c3=(a1,b0); для +x, -y, +z обход разворачивается
                bool flip = (A == 0 && pos) || (A == 1 && !pos) || (A == 2 && pos);
                int ca0 = a0, cb0 = b0, ca1 = a0, cb1 = b1, ca2 = a1, cb2 = b1, ca3 = a1, cb3 = b0;
                int ao0 = q.ao & 3, ao1 = (q.ao >> 2) & 3, ao2 = (q.ao >> 4) & 3, ao3 = (q.ao >> 6) & 3;
                if (flip) { ca1 = a1; cb1 = b0; ca3 = a0; cb3 = b1; int t = ao1; ao1 = ao3; ao3 = t; }
                int wb = q.mat * 32 + q.dir * 4;
                int o = qi * 4;
                verts[o + 0] = V(A, pd, ca0, cb0, wb + ao0);
                verts[o + 1] = V(A, pd, ca1, cb1, wb + ao1);
                verts[o + 2] = V(A, pd, ca2, cb2, wb + ao2);
                verts[o + 3] = V(A, pd, ca3, cb3, wb + ao3);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static VoxVertex V(int A, int d, int a, int b, int w)
        {
            int x, y, z;
            if (A == 0) { x = d; y = a; z = b; } else if (A == 1) { x = a; y = d; z = b; } else { x = a; y = b; z = d; }
            return new VoxVertex { x = (half)(float)x, y = (half)(float)y, z = (half)(float)z, w = (half)(float)w };
        }
    }

    /// Строит сетки для списка чанков одного тела прямо в Mesh.MeshData (без копии на главном потоке).
    /// stats[i] = (открытые грани, прямоугольники, тики начала, тики конца) для замера стоимости чанка.
    [BurstCompile(CompileSynchronously = true)]
    public struct ChunkMeshJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> vox;
        [ReadOnly] public NativeArray<int3> origins;
        [ReadOnly] public NativeArray<VertexAttributeDescriptor> layout;
        public int3 dims;
        public Mesh.MeshDataArray meshes;
        [NativeDisableParallelForRestriction] public NativeArray<long> stats;

        public void Execute(int i)
        {
            long t0 = ProfilerUnsafeUtility.Timestamp;
            var quads = new NativeList<Quad>(1024, Allocator.Temp);
            int naive = GreedyMesher.BuildQuads(vox, dims, origins[i], GreedyMesher.ModeRender, ref quads);
            int nq = quads.Length, nv = nq * 4, ni = nq * 6;
            var md = meshes[i];
            md.SetVertexBufferParams(nv, layout);
            var verts = md.GetVertexData<VoxVertex>();
            GreedyMesher.EmitVertices(ref quads, verts);
            bool big = nv > 65535;
            md.SetIndexBufferParams(ni, big ? IndexFormat.UInt32 : IndexFormat.UInt16);
            if (big)
            {
                var idx = md.GetIndexData<uint>();
                for (int q = 0; q < nq; q++) { uint b = (uint)(q * 4); int o = q * 6; idx[o] = b; idx[o + 1] = b + 1; idx[o + 2] = b + 2; idx[o + 3] = b; idx[o + 4] = b + 2; idx[o + 5] = b + 3; }
            }
            else
            {
                var idx = md.GetIndexData<ushort>();
                for (int q = 0; q < nq; q++) { int b = q * 4, o = q * 6; idx[o] = (ushort)b; idx[o + 1] = (ushort)(b + 1); idx[o + 2] = (ushort)(b + 2); idx[o + 3] = (ushort)b; idx[o + 4] = (ushort)(b + 2); idx[o + 5] = (ushort)(b + 3); }
            }
            md.subMeshCount = 1;
            md.SetSubMesh(0, new SubMeshDescriptor(0, ni) { vertexCount = nv, bounds = new Bounds(new Vector3(16, 16, 16), new Vector3(32, 32, 32)) },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            long t1 = ProfilerUnsafeUtility.Timestamp;
            quads.Dispose();
            stats[i * 4] = naive; stats[i * 4 + 1] = nq; stats[i * 4 + 2] = t0; stats[i * 4 + 3] = t1;
        }
    }

    /// Только счёт граней и прямоугольников (для отчёта: наивно / с затенением / без затенения).
    [BurstCompile(CompileSynchronously = true)]
    public struct CountJob : IJob
    {
        [ReadOnly] public NativeArray<byte> vox;
        public int3 dims; public int3 nch; public int mode;
        public NativeArray<int> result; // [0] открытые грани, [1] прямоугольники
        public void Execute()
        {
            var quads = new NativeList<Quad>(4096, Allocator.Temp);
            int naive = 0, nq = 0;
            for (int z = 0; z < nch.z; z++)
            for (int y = 0; y < nch.y; y++)
            for (int x = 0; x < nch.x; x++)
            {
                quads.Clear();
                naive += GreedyMesher.BuildQuads(vox, dims, new int3(x, y, z) * GreedyMesher.CS, mode, ref quads);
                nq += quads.Length;
            }
            result[0] = naive; result[1] = nq;
        }
    }
}
