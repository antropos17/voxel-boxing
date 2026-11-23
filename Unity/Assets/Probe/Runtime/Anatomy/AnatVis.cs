using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxProbe
{
    /// Видимость (этап A2): сетка рисует только грани, к которым можно заглянуть снаружи.
    /// Голова: глубина видимого воздуха vis — от края решётки через открытые ячейки (пусто — шаг 0, воздух полости — шаг 1).
    /// Видима открытая ячейка с vis ≤ Lim: ноздри, щель рта, слуховой проход видны на Lim ячеек вглубь, закрытые полости
    /// (пазухи, нос и глотка за преддверием, рот за губами) — нет. Сетка считает невидимую открытую ячейку сплошной: грани к ней
    /// не рисуются, а где она граничит с видимой, рисуется «тень полости» (AnatMat.Shade) — сквозь проём не виден фон.
    /// Отдельные тела (челюсть, глаза, зубы): их открытая ячейка видима, если в той же точке видима ячейка головы
    /// или ячейка головы под телом (Covered) граничит с видимой; vis тела — 0 (видима) или Hidden.
    /// Удаление ткани только открывает: vis убывает, обновление идёт от удалённых ячеек (Opened) одним Burst-джобом.
    /// Если ячейка закрылась (отёк века), голова считается заново (Full).
    public class AnatVis
    {
        public const int Lim = 8;
        public const byte Hidden = Lim + 1;
        readonly AnatHead a;
        readonly AnatGrid h;
        public double lastMs, bodiesMs;
        public int lastFlips;

        public AnatVis(AnatHead a)
        {
            this.a = a; h = a.head;
            foreach (var g in a.All) if (!g.vis.IsCreated) g.vis = new NativeArray<byte>(g.Count, Allocator.Persistent);
            foreach (var g in a.All) { g.hasClear = false; for (int i = 0; i < g.Count && !g.hasClear; i++) g.hasClear = AnatMat.Clear(g.mat[i]); }
        }

        /// Всё заново: голова и все тела.
        public void Full()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var box = new NativeArray<int>(7, Allocator.TempJob);
            var none = new NativeArray<int>(0, Allocator.TempJob);
            var fl = new NativeList<int>(0, Allocator.TempJob);
            new FloodJob { mat = h.mat, vis = h.vis, opened = none, box = box, dims = h.dims, full = true, flipped = fl }.Run();
            lastFlips = box[6];
            none.Dispose(); box.Dispose(); fl.Dispose();
            lastMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            foreach (var g in a.All) if (g != h) BodyVis(g);
            bodiesMs = sw.Elapsed.TotalMilliseconds;
            foreach (var g in a.All) g.Touch(0, g.dims - 1);
        }

        /// Ячейки головы стали пустыми (удалена ткань): видимость обновляется от них. Ячейки, сменившие видимость,
        /// и тела рядом помечаются для перестройки сетки (AnatGrid.Touch). Возвращает число сменивших видимость ячеек.
        public int Opened(NativeArray<int> cells)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var box = new NativeArray<int>(7, Allocator.TempJob);
            var fl = new NativeList<int>(1024, Allocator.TempJob);
            new FloodJob { mat = h.mat, vis = h.vis, opened = cells, box = box, dims = h.dims, full = false, flipped = fl }.Run();
            int flips = box[6];
            if (flips > 0)
            {
                int3 mn = new int3(box[0], box[1], box[2]), mx = new int3(box[3], box[4], box[5]);
                // перестраиваются чанки самих ячеек, сменивших видимость, а не вся их коробка (полость рта тянется через полголовы)
                int W = h.dims.x, H = h.dims.y;
                for (int k = 0; k < fl.Length; k++) { int i = fl[k]; h.TouchCell(new int3(i % W, (i / W) % H, i / (W * H))); }
                float3 lo = h.Local(mn.x, mn.y, mn.z) - 2 * h.step, hi = h.Local(mx.x, mx.y, mx.z) + 2 * h.step;   // ячейка тела зависит от ячейки головы и её соседей: до 1,5 шага
                // тела рядом: все джобы разом (пересчёт в коробке → растекание по прозрачным → сравнение со старым), одно ожидание;
                // перестраиваются только тела, у которых видимость сменилась, и только сменившаяся область
                var gs = new System.Collections.Generic.List<AnatGrid>();
                var olds = new System.Collections.Generic.List<NativeArray<byte>>();
                var boxes = new System.Collections.Generic.List<NativeArray<int>>();
                var hs = new NativeList<JobHandle>(8, Allocator.Temp);
                foreach (var g in a.All)
                {
                    if (g == h || !Overlap(g, lo, hi)) continue;
                    var old = new NativeArray<byte>(g.vis, Allocator.TempJob);
                    var gb = new NativeArray<int>(7, Allocator.TempJob);
                    var jh = BodyVisJobFor(g, true, lo, hi).Schedule(g.dims.z, 4);
                    if (g.hasClear) jh = new ClearSpreadJob { mat = g.mat, vis = g.vis, dims = g.dims }.Schedule(jh);
                    jh = new DiffJob { a = old, b = g.vis, dims = g.dims, box = gb }.Schedule(jh);
                    hs.Add(jh); gs.Add(g); olds.Add(old); boxes.Add(gb);
                }
                JobHandle.CombineDependencies(hs.AsArray()).Complete();
                for (int k = 0; k < gs.Count; k++)
                {
                    var gb = boxes[k];
                    if (gb[6] > 0) gs[k].Touch(new int3(gb[0], gb[1], gb[2]), new int3(gb[3], gb[4], gb[5]));
                    gb.Dispose(); olds[k].Dispose();
                }
                hs.Dispose();
            }
            box.Dispose(); fl.Dispose();
            lastMs = sw.Elapsed.TotalMilliseconds; lastFlips = flips;
            return flips;
        }

        /// Видимость тела по полю головы (Burst, по слоям z).
        void BodyVis(AnatGrid g)
        {
            BodyVisJobFor(g, false, 0, 0).Schedule(g.dims.z, 1).Complete();
            if (g.hasClear) new ClearSpreadJob { mat = g.mat, vis = g.vis, dims = g.dims }.Run();
        }

        BodyVisJob BodyVisJobFor(AnatGrid g, bool boxed, float3 lo, float3 hi) =>
            new BodyVisJob { boxed = boxed, lo = lo, hi = hi, hmat = h.mat, hvis = h.vis, hdims = h.dims, horg = h.org, hstep = h.step, vis = g.vis, mat = g.mat, dims = g.dims, org = g.org, step = g.step, pos = g.pos, rot = g.rot };

        /// Коробка ячеек, у которых видимость (≤ Lim) различается в a и b, и их число.
        [BurstCompile(CompileSynchronously = true)]
        struct DiffJob : IJob
        {
            [ReadOnly] public NativeArray<byte> a, b;
            public NativeArray<int> box;
            public int3 dims;
            public void Execute()
            {
                int3 lo = int.MaxValue, hi = int.MinValue; int n = 0, W = dims.x, H = dims.y;
                for (int i = 0; i < a.Length; i++)
                {
                    if ((a[i] <= Lim) == (b[i] <= Lim)) continue;
                    var c = new int3(i % W, (i / W) % H, i / (W * H)); lo = math.min(lo, c); hi = math.max(hi, c); n++;
                }
                box[0] = lo.x; box[1] = lo.y; box[2] = lo.z; box[3] = hi.x; box[4] = hi.y; box[5] = hi.z; box[6] = n;
            }
        }

        /// Свет проходит сквозь прозрачные ткани тела (роговица, водянистая влага): видимость растекается по ним внутри тела.
        /// Сетка тела мельче сетки головы, поэтому без этого глубина передней камеры глубже одной ячейки головы считалась скрытой
        /// и радужка за ней не рисовалась. Пустые ячейки тела видимость не передают (иначе пролом открыл бы корни зубов в кости).
        [BurstCompile(CompileSynchronously = true)]
        struct ClearSpreadJob : IJob
        {
            [ReadOnly] public NativeArray<byte> mat;
            public NativeArray<byte> vis;
            public int3 dims;
            public void Execute()
            {
                int W = dims.x, H = dims.y, n = W * H * dims.z;
                var st = new NativeList<int>(1024, Allocator.Temp);
                for (int i = 0; i < n; i++) if (vis[i] == 0 && (Open(mat[i]) || AnatMat.Clear(mat[i]))) st.Add(i);
                while (st.Length > 0)
                {
                    int i = st[st.Length - 1]; st.RemoveAt(st.Length - 1);
                    int x = i % W, y = (i / W) % H, z = i / (W * H);
                    for (int dd = 0; dd < 6; dd++)
                    {
                        int3 q = new int3(x, y, z) + Dir(dd);
                        if (math.any(q < 0) || math.any(q >= dims)) continue;
                        int j = q.x + W * (q.y + H * q.z);
                        if (vis[j] == 0 || !AnatMat.Clear(mat[j])) continue;
                        vis[j] = 0; st.Add(j);
                    }
                }
            }
        }

        /// Пересекается ли коробка тела (в рамке головы) с [lo, hi].
        static bool Overlap(AnatGrid g, float3 lo, float3 hi)
        {
            float3 bmin = float.MaxValue, bmax = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                float3 c = g.org + new float3((k & 1) != 0 ? g.dims.x : 0, (k & 2) != 0 ? g.dims.y : 0, (k & 4) != 0 ? g.dims.z : 0) * g.step;
                float3 w = g.World(c); bmin = math.min(bmin, w); bmax = math.max(bmax, w);
            }
            return math.all(bmin <= hi) && math.all(bmax >= lo);
        }

        public static bool Open(byte m) => m == AnatMat.Empty || m == AnatMat.Air;

        /// Прогрев: один раз запустить все джобы обновления (в редакторе Burst компилирует джоб при первом запуске — для честного замера).
        public void Warm()
        {
            var save = new System.Collections.Generic.List<byte[]>(); foreach (var g in a.All) save.Add(g.vis.ToArray());
            int W = h.dims.x, H = h.dims.y, D = h.dims.z, cell = -1;
            for (int i = 0; i < h.Count && cell < 0; i++) if (h.mat[i] == AnatMat.Empty) cell = i;   // открытая ячейка: ничего не меняется
            var one = new NativeArray<int>(1, Allocator.TempJob); one[0] = cell;
            Opened(one); one.Dispose();
            var eye = a.eyeL; var old = new NativeArray<byte>(eye.vis, Allocator.TempJob); var gb = new NativeArray<int>(7, Allocator.TempJob);
            var jh = BodyVisJobFor(eye, true, h.org, h.org + (float3)h.dims * h.step).Schedule(eye.dims.z, 4);
            jh = new ClearSpreadJob { mat = eye.mat, vis = eye.vis, dims = eye.dims }.Schedule(jh);
            new DiffJob { a = old, b = eye.vis, dims = eye.dims, box = gb }.Schedule(jh).Complete();
            old.Dispose(); gb.Dispose();
            int k = 0; foreach (var g in a.All) g.vis.CopyFrom(save[k++]);
            foreach (var g in a.All) { g.tch.Clear(); g.tmn = int.MaxValue; g.tmx = int.MinValue; }
        }

        /// Поле глубины видимого воздуха головы. full — заново от края решётки; иначе — от opened (ячеек, ставших открытыми):
        /// их значение — минимум соседей плюс шаг; дальше уровни 0..Lim обходятся по очереди (шаги 0 и 1, как у BFS 0-1).
        /// box: мин. и макс. координаты ячеек, ставших видимыми, и их число.
        [BurstCompile(CompileSynchronously = true)]
        struct FloodJob : IJob
        {
            [ReadOnly] public NativeArray<byte> mat;
            public NativeArray<byte> vis;
            [ReadOnly] public NativeArray<int> opened;
            public NativeArray<int> box;
            public NativeList<int> flipped;               // ячейки, ставшие видимыми (только при full = false)
            public int3 dims; public bool full;
            int3 lo, hi; int flips;

            void Set(int i, int v)
            {
                if (vis[i] > Lim && v <= Lim)
                {
                    int x = i % dims.x, y = (i / dims.x) % dims.y, z = i / (dims.x * dims.y);
                    lo = math.min(lo, new int3(x, y, z)); hi = math.max(hi, new int3(x, y, z)); flips++;
                    if (!full) flipped.Add(i);
                }
                vis[i] = (byte)v;
            }

            public void Execute()
            {
                int W = dims.x, H = dims.y, D = dims.z, n = W * H * D;
                lo = int.MaxValue; hi = int.MinValue; flips = 0;
                var seeds = new NativeList<int>(4096, Allocator.Temp);
                if (full)
                {
                    for (int i = 0; i < n; i++) vis[i] = Hidden;
                    for (int z = 0; z < D; z++) for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                    {
                        if (x != 0 && y != 0 && z != 0 && x != W - 1 && y != H - 1 && z != D - 1) continue;
                        int i = x + W * (y + H * z); byte m = mat[i];
                        if (!Open(m)) continue;
                        int v = m == AnatMat.Air ? 1 : 0; Set(i, v); seeds.Add((v << 21) | i);
                    }
                }
                else
                {
                    for (int k = 0; k < opened.Length; k++)
                    {
                        int i = opened[k]; byte m = mat[i]; if (!Open(m)) continue;
                        int x = i % W, y = (i / W) % H, z = i / (W * H);
                        int best = (x == 0 || y == 0 || z == 0 || x == W - 1 || y == H - 1 || z == D - 1) ? 0 : Hidden;
                        for (int dd = 0; dd < 6; dd++)
                        {
                            int3 q = new int3(x, y, z) + Dir(dd);
                            if (math.any(q < 0) || math.any(q >= dims)) continue;
                            int j = q.x + W * (q.y + H * q.z);
                            if (Open(mat[j])) best = math.min(best, vis[j]);
                        }
                        int v = best + (m == AnatMat.Air ? 1 : 0);
                        if (v < vis[i]) { Set(i, v); seeds.Add((v << 21) | i); }
                    }
                }
                seeds.Sort();
                var cur = new NativeList<int>(4096, Allocator.Temp);
                var nxt = new NativeList<int>(4096, Allocator.Temp);
                int si = 0;
                for (int k = 0; k <= Lim; k++)
                {
                    while (si < seeds.Length && (seeds[si] >> 21) == k) { cur.Add(seeds[si] & 0x1FFFFF); si++; }
                    while (cur.Length > 0)
                    {
                        int i = cur[cur.Length - 1]; cur.RemoveAt(cur.Length - 1);
                        if (vis[i] != k) continue;
                        int x = i % W, y = (i / W) % H, z = i / (W * H);
                        for (int dd = 0; dd < 6; dd++)
                        {
                            int3 q = new int3(x, y, z) + Dir(dd);
                            if (math.any(q < 0) || math.any(q >= dims)) continue;
                            int j = q.x + W * (q.y + H * q.z);
                            byte m = mat[j]; if (!Open(m)) continue;
                            int v = k + (m == AnatMat.Air ? 1 : 0);
                            if (v >= vis[j]) continue;
                            Set(j, v);
                            if (v == k) cur.Add(j); else nxt.Add(j);
                        }
                    }
                    var t = cur; cur = nxt; nxt = t;
                }
                box[0] = lo.x; box[1] = lo.y; box[2] = lo.z; box[3] = hi.x; box[4] = hi.y; box[5] = hi.z; box[6] = flips;
            }
        }

        static int3 Dir(int d) => d == 0 ? new int3(1, 0, 0) : d == 1 ? new int3(-1, 0, 0) : d == 2 ? new int3(0, 1, 0) : d == 3 ? new int3(0, -1, 0) : d == 4 ? new int3(0, 0, 1) : new int3(0, 0, -1);

        /// Видимость открытых ячеек тела: ячейка головы в той же точке видима, либо она Covered и граничит с видимой. Вне головы — видима.
        [BurstCompile(CompileSynchronously = true)]
        struct BodyVisJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> hmat, hvis, mat;
            [NativeDisableParallelForRestriction] public NativeArray<byte> vis;
            public int3 hdims, dims; public float3 horg, org, pos; public float hstep, step; public float3x3 rot;
            public bool boxed; public float3 lo, hi;   // boxed — пересчитать только ячейки в коробке [lo, hi] рамки головы

            public void Execute(int z)
            {
                for (int y = 0; y < dims.y; y++)
                for (int x = 0; x < dims.x; x++)
                {
                    int i = x + dims.x * (y + dims.y * z);
                    byte m = mat[i];
                    if (!(Open(m) || AnatMat.Clear(m))) { vis[i] = Hidden; continue; }
                    float3 p = pos + math.mul(rot, org + (new float3(x, y, z) + 0.5f) * step);
                    if (boxed && (math.any(p < lo) || math.any(p > hi))) continue;
                    int3 c = (int3)math.floor((p - horg) / hstep);
                    vis[i] = HeadSees(c) ? (byte)0 : Hidden;
                }
            }
            bool HeadSees(int3 c)
            {
                if (math.any(c < 0) || math.any(c >= hdims)) return true;
                int i = c.x + hdims.x * (c.y + hdims.y * c.z);
                byte m = hmat[i];
                if (Open(m)) return hvis[i] <= Lim;
                if (m != AnatMat.Covered) return false;
                for (int d = 0; d < 6; d++)
                {
                    int3 q = c + Dir(d);
                    if (math.any(q < 0) || math.any(q >= hdims)) return true;
                    int j = q.x + hdims.x * (q.y + hdims.y * q.z);
                    if (Open(hmat[j]) && hvis[j] <= Lim) return true;
                }
                return false;
            }
        }
    }

    /// Видимость для сетки чанка: vis решётки (≤ Lim — видима), что за краем решётки (голова — видимо, тело — нет),
    /// рисовать ли «тень полости» (только голова).
    public struct VisView
    {
        [ReadOnly] public NativeArray<byte> vis, mat;
        public bool on, outVisible, shade;
        public static VisView Of(AnatGrid g, bool isHead) => g.vis.IsCreated ? new VisView { vis = g.vis, mat = g.mat, on = true, outVisible = isHead, shade = isHead } : Off(g.mat);
        /// Без отсечения (все открытые ячейки видимы); массив — любой созданный, чтобы джоб принял поля.
        public static VisView Off(NativeArray<byte> any) => new VisView { vis = any, mat = any };
    }
}
