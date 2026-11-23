using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxProbe
{
    /// Вершина 20 байт: half4 (x, y, z в ячейках чанка со смещением угла; w = материал·8 + грань), snorm8×4 (нормаль, затенение угла 0..3 ×42),
    /// unorm8×4 c0 (покраснение, синяк, ссадина, кровь) и unorm8×4 c1 (время удара, возраст синяка, возраст крови, отёк) — среднее по ячейкам угла.
    public struct AVert
    {
        public half x, y, z, w;
        public sbyte nx, ny, nz, ao;
        public uint c0, c1;
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
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UNorm8, 4),
        };

        /// Рабочие массивы одного чанка.
        struct Ctx
        {
            public NativeArray<byte> occ, dm;
            public NativeArray<sbyte> lcs;
            public NativeArray<int> ls, lg;
            public bool st;                  // в окрестности чанка есть состояния
            public NativeArray<VS> ps;       // состояния в поле с отступом 2
            public NativeArray<uint2> lc;    // цвет угла (c0, c1)
            public NativeArray<float4> ld;   // смещение угла в ячейках; w = 1 — посчитано
            public float dscale;             // ячеек на 0,1 мм
            public int style;                // опыты со ступеньками: 1 — без затенения углов, 2 — нормаль поля, 4 — сдвиг угла по полю
            public NativeArray<float4> lf;   // поле угла: градиент (xyz) и значение (w); w < 0 — не считано
            public float4 grid;              // начало решётки (см) и шаг — для тени щетины
            public int3 corg;                // начало чанка в ячейках
        }

        public const int StyleNoAO = 1, StyleFieldNormal = 2, StyleFieldPos = 4, StyleStubble = 8;
        /// В зоне изгиба шеи (AnatBend.InBend) грани не склеиваются: шейдер гнёт вершины нелинейно, длинное ребро осталось бы прямым,
        /// а вершины соседних граней на нём ушли бы по дуге — Т-стыки дают щели.
        public const int StyleBend = 16;
        public const int StyleDefault = StyleNoAO | StyleFieldNormal | StyleStubble;   // вид поверхности по умолчанию (выбран по опытам этапа A2)
        static readonly float[] Gw = { 0.2494f, 0.8574f, 0.8574f, 0.2494f };   // exp(−t²/(2·0,9²)), t = ±1,5; ±0,5
        static readonly float[] Gd = { 0.4619f, 0.5293f, -0.5293f, -0.4619f };  // −t/σ²·w(t)

        static int3 Dir(int d) => d == 0 ? new int3(1, 0, 0) : d == 1 ? new int3(-1, 0, 0) : d == 2 ? new int3(0, 1, 0) : d == 3 ? new int3(0, -1, 0) : d == 4 ? new int3(0, 0, 1) : new int3(0, 0, -1);
        /// Оси плоскости грани: X → (a=y, b=z), Y → (a=x, b=z), Z → (a=x, b=y).
        static int3 AxA(int A) => A == 0 ? new int3(0, 1, 0) : new int3(1, 0, 0);
        static int3 AxB(int A) => A == 2 ? new int3(0, 1, 0) : new int3(0, 0, 1);

        /// Строит вершины чанка (по 4 на прямоугольник). Возвращает число открытых граней (наивная сетка).
        public static int Build(NativeArray<byte> mat, int3 dims, int3 org, int mode, ref NativeList<AVert> verts) =>
            Build(mat, dims, org, mode, ref verts, default, 0, VisView.Off(mat), 0);

        /// То же с состояниями вокселей (sv.on): цвет углов и смещение ячеек; dscale — ячеек на 0,1 мм.
        /// vv.on — видимость (AnatVis): невидимая открытая ячейка сплошная, у головы на границе с видимой — тень полости.
        /// Роговица и водянистая влага прозрачны всегда.
        public static int Build(NativeArray<byte> mat, int3 dims, int3 org, int mode, ref NativeList<AVert> verts, in StateView sv, float dscale, in VisView vv, int style, float4 grid = default)
        {
            var c = new Ctx(); c.style = mode == ModeRender ? style : 0;
            c.grid = grid; c.corg = org; if (grid.w <= 0) c.style &= ~StyleStubble;
            c.occ = new NativeArray<byte>(P * P * P, Allocator.Temp, NativeArrayOptions.ClearMemory);
            c.dm = new NativeArray<byte>(P * P * P, Allocator.Temp, NativeArrayOptions.ClearMemory);
            c.dscale = dscale;
            int W = dims.x, H = dims.y, D = dims.z;
            bool any = false;
            for (int pz = 0; pz < P; pz++)
            {
                int gz = org.z + pz - 2; bool inz = gz >= 0 && gz < D;
                for (int py = 0; py < P; py++)
                {
                    int gy = org.y + py - 2; bool iny = inz && gy >= 0 && gy < H;
                    int gi = W * (gy + H * gz), pi = P * (py + P * pz);
                    for (int px = 0; px < P; px++)
                    {
                        int gx = org.x + px - 2; bool inside = iny && gx >= 0 && gx < W;
                        if (!inside) { if (vv.on && !vv.outVisible) c.occ[pi + px] = 1; continue; }
                        byte m = mat[gi + gx];
                        bool core = px >= 2 && px <= CS + 1 && py >= 2 && py <= CS + 1 && pz >= 2 && pz <= CS + 1;
                        if (m == AnatMat.Empty || m == AnatMat.Air || AnatMat.Clear(m))
                        {
                            if (!vv.on || vv.vis[gi + gx] <= AnatVis.Lim) continue;
                            c.occ[pi + px] = 1;
                            if (vv.shade && Frontier(vv, dims, gx, gy, gz)) { c.dm[pi + px] = AnatMat.Shade; if (core) any = true; }
                            continue;
                        }
                        c.occ[pi + px] = 1;
                        if (m != AnatMat.Covered) { c.dm[pi + px] = m; if (core) any = true; }
                    }
                }
            }
            int naive = 0;
            if (!any) { c.occ.Dispose(); c.dm.Dispose(); return 0; }

            // состояния: только если у чанка или соседей (в пределах отступа) они заведены
            if (sv.on && mode == ModeRender)
            {
                int3 c0 = math.max(org - 2, 0) / CS, c1 = math.min(org + CS + 1, dims - 1) / CS;
                for (int z = c0.z; z <= c1.z && !c.st; z++) for (int y = c0.y; y <= c1.y && !c.st; y++) for (int x = c0.x; x <= c1.x; x++)
                    if (sv.slot[x + sv.nch.x * (y + sv.nch.y * z)] >= 0) { c.st = true; break; }
            }
            if (c.st)
            {
                c.ps = new NativeArray<VS>(P * P * P, Allocator.Temp, NativeArrayOptions.ClearMemory);
                for (int pz = 0; pz < P; pz++)
                {
                    int gz = org.z + pz - 2; if (gz < 0 || gz >= D) continue;
                    for (int py = 0; py < P; py++)
                    {
                        int gy = org.y + py - 2; if (gy < 0 || gy >= H) continue;
                        for (int px = 0; px < P; px++)
                        {
                            int gx = org.x + px - 2; if (gx < 0 || gx >= W) continue;
                            int s = sv.slot[gx / CS + sv.nch.x * (gy / CS + sv.nch.y * (gz / CS))];
                            if (s < 0) continue;
                            c.ps[px + P * (py + P * pz)] = sv.pool[s + (gx % CS) + sv.bs.x * ((gy % CS) + sv.bs.y * (gz % CS))];
                        }
                    }
                }
                c.lc = new NativeArray<uint2>(L * L * L, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                c.ld = new NativeArray<float4>(L * L * L, Allocator.Temp, NativeArrayOptions.ClearMemory);
            }

            // углы решётки: cs и 2S (целые −4..4)
            c.lcs = new NativeArray<sbyte>(L * L * L, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            c.ls = new NativeArray<int>(L * L * L, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            c.lg = new NativeArray<int>(L * L * L, Allocator.Temp, NativeArrayOptions.ClearMemory); // 0 — не считан
            for (int i = 0; i < c.lcs.Length; i++) c.lcs[i] = -1;
            if ((c.style & (StyleFieldNormal | StyleFieldPos)) != 0) { c.lf = new NativeArray<float4>(L * L * L, Allocator.Temp); for (int i = 0; i < c.lf.Length; i++) c.lf[i] = -1; }

            var keys = new NativeArray<int>(CS * CS, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int3 ext = math.clamp(dims - org, 0, CS);   // обход только по ячейкам решётки: у малых тел (зубы) чанк меньше 32³
            for (int dir = 0; dir < 6; dir++)
            {
                int A = dir >> 1; bool pos = (dir & 1) == 0;
                int3 n = Dir(dir), ea = AxA(A), eb = AxB(A);
                int nd = ext[A], na = math.csum(ext * ea), nbb = math.csum(ext * eb);
                for (int d = 0; d < nd; d++)
                {
                    for (int k = 0; k < CS * CS; k++) keys[k] = 0;
                    for (int b = 0; b < nbb; b++)
                    for (int a = 0; a < na; a++)
                    {
                        int3 cc = (A == 0 ? new int3(d, a, b) : A == 1 ? new int3(a, d, b) : new int3(a, b, d));
                        int pi = PI(cc + 2);
                        byte m = c.dm[pi];
                        if (m == 0) continue;
                        int3 nb = cc + 2 + n;
                        if (c.occ[PI(nb)] != 0) continue;
                        naive++;
                        if (mode == ModeNaive) continue;
                        // углы грани в решётке: плоскость pd, (a0|a1, b0|b1)
                        int pd = d + (pos ? 1 : 0);
                        int3 l00 = Lat(A, pd, a, b), l01 = Lat(A, pd, a, b + 1), l11 = Lat(A, pd, a + 1, b + 1), l10 = Lat(A, pd, a + 1, b);
                        int ao0 = Ao(c.occ, nb, ea, eb, -1, -1), ao1 = Ao(c.occ, nb, ea, eb, -1, 1), ao2 = Ao(c.occ, nb, ea, eb, 1, 1), ao3 = Ao(c.occ, nb, ea, eb, 1, -1);
                        bool merge;
                        if (mode == ModeGreedyAO) { int s = ao0 | (ao1 << 2) | (ao2 << 4) | (ao3 << 6); merge = s == 0 || s == 0x55 || s == 0xAA || s == 0xFF; keys[b * CS + a] = merge ? (m | (s << 8)) + 1 : -(m + 1); if (!merge) Emit(ref verts, ref c, A, pos, dir, pd, a, b, 1, 1, m, ao0, ao1, ao2, ao3, l00, l01, l11, l10, false); continue; }
                        merge = Flat(ref c, l00, n) && Flat(ref c, l01, n) && Flat(ref c, l11, n) && Flat(ref c, l10, n);
                        if (merge) keys[b * CS + a] = m + 1;
                        else Emit(ref verts, ref c, A, pos, dir, pd, a, b, 1, 1, m, ao0, ao1, ao2, ao3, l00, l01, l11, l10, true);
                    }
                    if (mode == ModeNaive) continue;
                    // жадное объединение одинаковых ключей
                    int pd2 = d + (pos ? 1 : 0);
                    for (int b = 0; b < nbb; b++)
                    for (int a = 0; a < na; a++)
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
                        Emit(ref verts, ref c, A, pos, dir, pd2, a, b, w, h, (byte)mm, aoS & 3, (aoS >> 2) & 3, (aoS >> 4) & 3, (aoS >> 6) & 3,
                            Lat(A, pd2, a, b), Lat(A, pd2, a, b + h), Lat(A, pd2, a + w, b + h), Lat(A, pd2, a + w, b), false);
                    }
                }
            }
            c.occ.Dispose(); c.dm.Dispose(); c.lcs.Dispose(); c.ls.Dispose(); c.lg.Dispose(); keys.Dispose();
            if (c.st) { c.ps.Dispose(); c.lc.Dispose(); c.ld.Dispose(); }
            if (c.lf.IsCreated) c.lf.Dispose();
            return naive;
        }

        /// Невидимая открытая ячейка граничит с видимой открытой.
        static bool Frontier(in VisView vv, int3 dims, int x, int y, int z)
        {
            for (int d = 0; d < 6; d++)
            {
                int3 q = new int3(x, y, z) + Dir(d);
                if (math.any(q < 0) || math.any(q >= dims)) return true;
                int j = q.x + dims.x * (q.y + dims.y * q.z);
                byte m = vv.mat[j];
                if ((m == AnatMat.Empty || m == AnatMat.Air) && vv.vis[j] <= AnatVis.Lim) return true;
            }
            return false;
        }

        static int PI(int3 p) => p.x + P * (p.y + P * p.z);
        static int LI(int3 l) => l.x + L * (l.y + L * l.z);
        static int3 Lat(int A, int d, int a, int b) => A == 0 ? new int3(d, a, b) : A == 1 ? new int3(a, d, b) : new int3(a, b, d);

        /// Угол решётки l (0..32): восемь ячеек вокруг — в поле с отступом 2 это (l.x+1..l.x+2, ...).
        static void Corner(ref Ctx c, int3 l, out int cs, out int3 s2)
        {
            int li = LI(l);
            if (c.lcs[li] >= 0) { cs = c.lcs[li]; int v = c.ls[li]; s2 = new int3((v & 15) - 8, ((v >> 4) & 15) - 8, ((v >> 8) & 15) - 8); return; }
            cs = 0; s2 = 0;
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
                if (c.occ[PI(l + 1 + new int3(dx, dy, dz))] != 0) { cs++; s2 += new int3(dx * 2 - 1, dy * 2 - 1, dz * 2 - 1); }
            c.lcs[li] = (sbyte)cs; c.ls[li] = (s2.x + 8) | ((s2.y + 8) << 4) | ((s2.z + 8) << 8);
        }

        static uint B8(float v) => (uint)math.clamp((int)math.round(v), 0, 255);

        /// Состояние угла: среднее по рисуемым ячейкам вокруг него; времена — с весом своей величины.
        static void CornerState(ref Ctx c, int3 l, out uint2 col, out float3 disp)
        {
            int li = LI(l);
            float4 cd = c.ld[li];
            if (cd.w != 0) { col = c.lc[li]; disp = cd.xyz; return; }
            float n = 0, red = 0, br = 0, ab = 0, bl = 0, sw = 0, wr = 0, tr = 0, tb = 0, tl = 0; float3 dd = 0;
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                int pi = PI(l + 1 + new int3(dx, dy, dz));
                if (c.dm[pi] == 0) continue;
                VS s = c.ps[pi]; n++;
                red += s.red; br += s.bruise; ab += s.abr; bl += s.blood; sw += s.swell;
                float w = s.red + s.swell; wr += w; tr += w * s.rt; tb += s.bruise * s.bt; tl += s.blood * s.blt;
                dd += new float3(s.dx, s.dy, s.dz);
            }
            if (n == 0) { col = 0; disp = 0; }
            else
            {
                float k = 1f / n;
                col = new uint2(B8(red * k) | (B8(br * k) << 8) | (B8(ab * k) << 16) | (B8(bl * k) << 24),
                                B8(wr > 0 ? tr / wr : 0) | (B8(br > 0 ? tb / br : 0) << 8) | (B8(bl > 0 ? tl / bl : 0) << 16) | (B8(sw * k) << 24));
                disp = dd * k * c.dscale;
            }
            c.lc[li] = col; c.ld[li] = new float4(disp, 1);
        }

        static bool Flat(ref Ctx c, int3 l, int3 n)
        {
            Corner(ref c, l, out int cs, out int3 s2);
            if (cs != 4 || !math.all(s2 == -4 * n)) return false;
            int3 g = Grad(ref c, l);
            if (!math.all(g * (1 - math.abs(n)) == 0)) return false;
            if (c.st) { CornerState(ref c, l, out uint2 col, out float3 disp); if (col.x != 0 || col.y != 0 || math.any(disp != 0)) return false; }
            if ((c.style & StyleFieldPos) != 0 && math.abs(Field(ref c, l).w - 0.5f) > 1e-4f) return false;
            if ((c.style & StyleBend) != 0 && AnatBend.InBend(c.grid.xyz + (float3)(c.corg + l) * c.grid.w)) return false;
            return true;
        }

        /// Градиент заполненности вокруг угла по 4×4×4 ячейкам (веса 1,2,2,1; смещения −3,−1,1,3) — для нормали.
        /// На плоскости шириной от двух ячеек в каждую сторону он строго вдоль нормали грани.
        static int3 Grad(ref Ctx c, int3 l)
        {
            int li = LI(l);
            int v = c.lg[li];
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
                        if (c.occ[PI(l + new int3(dx, dy, dz))] == 0) continue;
                        int wx = dx == 0 || dx == 3 ? 1 : 2, ox = dx * 2 - 3;
                        g += new int3(ox * wy * wz, oy * wx * wz, oz * wx * wy);
                    }
                }
            }
            c.lg[li] = (g.x + 512) | ((g.y + 512) << 10) | ((g.z + 512) << 20) | (1 << 30);
            return g;
        }

        /// Поле заполненности у угла: гауссово сглаживание (σ = 0,9 ячейки) по 4×4×4 ячейкам; w — значение 0..1 (поверхность — 0,5),
        /// xyz — минус градиент (смотрит наружу). На плоскости шириной от двух ячеек значение у угла ровно 0,5, градиент — вдоль нормали.
        static float4 Field(ref Ctx c, int3 l)
        {
            int li = LI(l);
            float4 v = c.lf[li];
            if (v.w >= 0) return v;
            float f = 0; float3 g = 0;
            for (int dz = 0; dz < 4; dz++)
            for (int dy = 0; dy < 4; dy++)
            for (int dx = 0; dx < 4; dx++)
            {
                if (c.occ[PI(l + new int3(dx, dy, dz))] == 0) continue;
                f += Gw[dx] * Gw[dy] * Gw[dz];
                g += new float3(Gd[dx] * Gw[dy] * Gw[dz], Gw[dx] * Gd[dy] * Gw[dz], Gw[dx] * Gw[dy] * Gd[dz]);
            }
            const float N = 1 / (2.2136f * 2.2136f * 2.2136f);
            v = new float4(g * N, f * N);
            c.lf[li] = v;
            return v;
        }

        static int Ao(NativeArray<byte> occ, int3 air, int3 ea, int3 eb, int sa, int sb)
        {
            int s1 = occ[PI(air + ea * sa)], s2 = occ[PI(air + eb * sb)], c = occ[PI(air + ea * sa + eb * sb)];
            return s1 != 0 && s2 != 0 ? 0 : 3 - (s1 + s2 + c);
        }

        static void Emit(ref NativeList<AVert> verts, ref Ctx c, int A, bool pos, int dir, int pd, int a, int b, int w, int h, byte m,
            int ao0, int ao1, int ao2, int ao3, int3 l0, int3 l1, int3 l2, int3 l3, bool smooth)
        {
            bool flip = (A == 0 && pos) || (A == 1 && !pos) || (A == 2 && pos);
            if (flip) { var t = l1; l1 = l3; l3 = t; int ta = ao1; ao1 = ao3; ao3 = ta; }
            float3 fn = Dir(dir);
            int wv = m * 8 + dir;
            verts.Add(V(ref c, l0, ao0, wv, fn, smooth));
            verts.Add(V(ref c, l1, ao1, wv, fn, smooth));
            verts.Add(V(ref c, l2, ao2, wv, fn, smooth));
            verts.Add(V(ref c, l3, ao3, wv, fn, smooth));
        }

        /// Тень щетины в канале затенения (когда AO выключено): 126 — без тени; по гладкому полю плотности в углу решётки,
        /// только на коже, с зерном ±3% по углу. Шейдер: яркость 0,72 + 0,28·ao, при полной плотности ≈ 0,84 (тон «кожи со щетиной»).
        static int Stubble(ref Ctx c, int3 l, int wv)
        {
            if ((c.style & StyleStubble) == 0) return 126;
            int m = wv >> 3; if (m != AnatMat.Skin && m != AnatMat.Stubble) return 126;
            float3 p = c.grid.xyz + (float3)(c.corg + l) * c.grid.w;
            float d = AnatGeo.StubbleDensity(p.x, p.y, p.z);
            if (d <= 0) return 126;
            float grain = (AnatGeo.CellHash(c.corg + l) - 0.5f) * 0.06f * d;
            return (int)math.clamp(126f * (1f - 0.57f * d + grain), 0, 126);
        }

        static AVert V(ref Ctx c, int3 l, int ao, int wv, float3 fn, bool smooth)
        {
            float3 p = l, nr = fn;
            uint2 col = 0;
            if (smooth)
            {
                Corner(ref c, l, out int cs, out int3 s2);
                float4 F = (c.style & (StyleFieldNormal | StyleFieldPos)) != 0 ? Field(ref c, l) : 0;
                float fl = math.length(F.xyz);
                if ((c.style & StyleFieldPos) != 0) { if (fl > 1e-4f) p += F.xyz / fl * math.clamp((F.w - 0.5f) / fl, -0.8f, 0.8f); }   // xyz смотрит наружу
                else if (cs > 0 && cs < 8)
                {
                    float3 S = (float3)s2 * 0.5f;
                    p += K * S * (1f / cs - 1f / (8 - cs));
                }
                if ((c.style & StyleFieldNormal) != 0) { if (fl > 1e-4f) nr = F.xyz / fl; }
                else
                {
                    float3 g = Grad(ref c, l);
                    float gl = math.length(g);
                    if (gl > 1f) nr = math.normalize(-g / gl * 0.85f + fn * 0.15f);
                }
                if (c.st) { CornerState(ref c, l, out col, out float3 disp); p += disp; }
            }
            // позиция в half: у 32 шаг half — 1/32 ячейки, у 0 — 1/2048; округляем до 1/32, чтобы общая вершина соседних чанков
            // (32+сдвиг в одном и 0+сдвиг в другом) записалась одинаково — иначе на стыках чанков щели в пиксель
            p = math.round(p * 32f) / 32f;
            return new AVert
            {
                x = (half)p.x, y = (half)p.y, z = (half)p.z, w = (half)(float)wv,
                nx = (sbyte)math.round(nr.x * 127), ny = (sbyte)math.round(nr.y * 127), nz = (sbyte)math.round(nr.z * 127), ao = (sbyte)((c.style & StyleNoAO) != 0 ? Stubble(ref c, l, wv) : ao * 42),
                c0 = col.x, c1 = col.y,
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
        public StateView sv;
        public VisView vv;
        public float dscale;
        public int style;
        public float4 grid;   // начало решётки и шаг (тень щетины), w = 0 — нет
        public Mesh.MeshDataArray meshes;
        [NativeDisableParallelForRestriction] public NativeArray<long> stats;

        public void Execute(int i)
        {
            long t0 = ProfilerUnsafeUtility.Timestamp;
            var verts = new NativeList<AVert>(4096, Allocator.Temp);
            int naive = AnatMesher.Build(mat, dims, origins[i], AnatMesher.ModeRender, ref verts, sv, dscale, vv, style, grid);
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
