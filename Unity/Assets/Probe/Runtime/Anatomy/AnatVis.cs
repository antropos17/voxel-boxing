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
        readonly System.Diagnostics.Stopwatch openClock = new System.Diagnostics.Stopwatch();
        public int Opened(NativeArray<int> cells)
        {
            var sw = openClock; sw.Restart();
            var box = new NativeArray<int>(7, Allocator.TempJob);
            var fl = new NativeList<int>(1024, Allocator.TempJob);
            long xt = AnatProf.XT();
            new FloodJob { mat = h.mat, vis = h.vis, opened = cells, box = box, dims = h.dims, full = false, flipped = fl }.Run();
            int flips = box[6];
            AnatProf.XAdd(AnatProf.XVisFlood, xt); xt = AnatProf.XT();
            if (flips > 0)
            {
                int3 mn = new int3(box[0], box[1], box[2]), mx = new int3(box[3], box[4], box[5]);
                // перестраиваются чанки самих ячеек, сменивших видимость, а не вся их коробка (полость рта тянется через полголовы)
                // часть 7: чанки сменивших видимость ячеек (как AnatGrid.TouchCell для каждой) собираются в Burst, в набор — только разные
                var tc = new NativeList<int>(64, Allocator.TempJob); var tmn = new NativeList<int3>(64, Allocator.TempJob); var tmx = new NativeList<int3>(64, Allocator.TempJob);
                new TouchJob { flipped = fl, dims = h.dims, outC = tc, outMn = tmn, outMx = tmx }.Run();
                for (int k = 0; k < tc.Length; k++) h.TouchBox(tc[k], tmn[k], tmx[k]);
                tc.Dispose(); tmn.Dispose(); tmx.Dispose();
                AnatProf.XAdd(AnatProf.XVisTouch, xt); xt = AnatProf.XT();
                float3 lo = h.Local(mn.x, mn.y, mn.z) - 2 * h.step, hi = h.Local(mx.x, mx.y, mx.z) + 2 * h.step;   // ячейка тела зависит от ячейки головы и её соседей: до 1,5 шага
                // тела рядом: пересчёт в коробке → растекание по прозрачным → сравнение со старым (часть 7: на главном потоке);
                // перестраиваются только тела, у которых видимость сменилась, и только сменившаяся область
                var gs = new System.Collections.Generic.List<AnatGrid>();
                var olds = new System.Collections.Generic.List<NativeArray<byte>>();
                var boxes = new System.Collections.Generic.List<NativeArray<int>>();
                foreach (var g in a.All)
                {
                    if (g == h || !Overlap(g, lo, hi)) continue;
                    var gb = new NativeArray<int>(7, Allocator.TempJob);
                    // часть 7: только ячейки тела, чей центр может попасть в [lo, hi] (коробка с запасом 2 ячейки); за коробкой джоб и так
                    // ничего не менял (открытые пропускались, закрытые уже скрыты). Копия старой видимости и сравнение — в той же коробке,
                    // кроме тел с прозрачными тканями: растекание по ним идёт по всему телу.
                    var bj = BodyVisJobFor(g, true, lo, hi); CellBox(g, lo, hi, out bj.c0, out bj.c1);
                    int3 d0 = g.hasClear ? 0 : bj.c0, d1 = g.hasClear ? g.dims - 1 : bj.c1, de = math.max(0, d1 - d0 + 1);
                    var old = new NativeArray<byte>(math.max(1, de.x * de.y * de.z), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    // работа в коробке мала — на главном потоке: под нагрузкой ожидание вытесненного рабочего потока стоило до 6,5 мс
                    new CopyBoxJob { src = g.vis, dst = old, dims = g.dims, c0 = d0, c1 = d1 }.Run();
                    bj.Run(math.max(0, bj.c1.z - bj.c0.z + 1));
                    if (g.hasClear) new ClearSpreadJob { mat = g.mat, vis = g.vis, dims = g.dims }.Run();
                    new DiffJob { a = old, b = g.vis, dims = g.dims, box = gb, c0 = d0, c1 = d1 }.Run();
                    gs.Add(g); olds.Add(old); boxes.Add(gb);
                }
                for (int k = 0; k < gs.Count; k++)
                {
                    var gb = boxes[k];
                    if (gb[6] > 0) gs[k].Touch(new int3(gb[0], gb[1], gb[2]), new int3(gb[3], gb[4], gb[5]));
                    gb.Dispose(); olds[k].Dispose();
                }
                AnatProf.XAdd(AnatProf.XVisBodies, xt);
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
            new BodyVisJob { boxed = boxed, lo = lo, hi = hi, hmat = h.mat, hvis = h.vis, hdims = h.dims, horg = h.org, hstep = h.step, vis = g.vis, mat = g.mat, dims = g.dims, org = g.org, step = g.step, pos = g.pos, rot = g.rot, c0 = 0, c1 = g.dims - 1 };

        /// Часть 7: ячейки тела g, центр которых может лежать в [lo, hi] рамки головы (углы коробки в рамке тела, запас 2 ячейки).
        static void CellBox(AnatGrid g, float3 lo, float3 hi, out int3 c0, out int3 c1)
        {
            float3x3 inv = math.inverse(g.rot); float3 mn = float.MaxValue, mx = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                float3 w = new float3((k & 1) != 0 ? hi.x : lo.x, (k & 2) != 0 ? hi.y : lo.y, (k & 4) != 0 ? hi.z : lo.z);
                float3 q = (math.mul(inv, w - g.pos) - g.org) / g.step - 0.5f; mn = math.min(mn, q); mx = math.max(mx, q);
            }
            c0 = math.max(0, (int3)math.floor(mn) - 2); c1 = math.min(g.dims - 1, (int3)math.ceil(mx) + 2);
        }

        /// Часть 7: чанки, которые пометил бы AnatGrid.TouchCell для каждой ячейки flipped (окрестность ±3), — каждый один раз.
        [BurstCompile(CompileSynchronously = true)]
        struct TouchJob : IJob
        {
            [ReadOnly] public NativeList<int> flipped;
            public int3 dims;
            public NativeList<int> outC;
            public NativeList<int3> outMn, outMx;   // часть 7: коробка сменивших видимость ячеек по каждому чанку outC
            public void Execute()
            {
                int3 nch = (dims + AnatMesher.CS - 1) / AnatMesher.CS;
                var seen = new NativeArray<bool>(nch.x * nch.y * nch.z, Allocator.Temp);
                var pos = new NativeArray<int>(nch.x * nch.y * nch.z, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                int W = dims.x, H = dims.y;
                for (int k = 0; k < flipped.Length; k++)
                {
                    int i = flipped[k]; int3 c = new int3(i % W, (i / W) % H, i / (W * H));
                    int3 c0 = math.max(c - 3, 0) / AnatMesher.CS, c1 = math.min(c + 3, dims - 1) / AnatMesher.CS;
                    for (int z = c0.z; z <= c1.z; z++) for (int y = c0.y; y <= c1.y; y++) for (int x = c0.x; x <= c1.x; x++)
                    {
                        int q = x + nch.x * (y + nch.y * z);
                        if (seen[q]) { int p = pos[q]; outMn[p] = math.min(outMn[p], c); outMx[p] = math.max(outMx[p], c); continue; }
                        seen[q] = true; pos[q] = outC.Length; outC.Add(q); outMn.Add(c); outMx.Add(c);
                    }
                }
            }
        }

        /// Часть 7: ячейки коробки [c0, c1] решётки — подряд в dst (x, потом y, потом z).
        [BurstCompile(CompileSynchronously = true)]
        struct CopyBoxJob : IJob
        {
            [ReadOnly] public NativeArray<byte> src; public NativeArray<byte> dst; public int3 dims, c0, c1;
            public void Execute()
            {
                int k = 0, n = c1.x - c0.x + 1;
                if (n <= 0) return;
                for (int z = c0.z; z <= c1.z; z++) for (int y = c0.y; y <= c1.y; y++) { NativeArray<byte>.Copy(src, c0.x + dims.x * (y + dims.y * z), dst, k, n); k += n; }
            }
        }

        /// Коробка ячеек, у которых видимость (≤ Lim) различается в a (копия коробки [c0, c1], см. CopyBoxJob) и b (вся решётка), и их число.
        [BurstCompile(CompileSynchronously = true)]
        struct DiffJob : IJob
        {
            [ReadOnly] public NativeArray<byte> a, b;
            public NativeArray<int> box;
            public int3 dims, c0, c1;   // часть 7: сравниваются ячейки коробки [c0, c1] (минимум, максимум и число от порядка не зависят)
            public void Execute()
            {
                int3 lo = int.MaxValue, hi = int.MinValue; int n = 0, W = dims.x, H = dims.y;
                int k = 0;
                for (int z = c0.z; z <= c1.z; z++) for (int y = c0.y; y <= c1.y; y++)
                {
                    int r = W * (y + H * z);
                    for (int x = c0.x; x <= c1.x; x++, k++)
                    {
                        int i = r + x;
                        if ((a[k] <= Lim) == (b[i] <= Lim)) continue;
                        var c = new int3(x, y, z); lo = math.min(lo, c); hi = math.max(hi, c); n++;
                    }
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
            var eye = a.eyeL; var old = new NativeArray<byte>(eye.vis.Length, Allocator.TempJob); var gb = new NativeArray<int>(7, Allocator.TempJob);
            var jh = new CopyBoxJob { src = eye.vis, dst = old, dims = eye.dims, c0 = 0, c1 = eye.dims - 1 }.Schedule();
            jh = BodyVisJobFor(eye, true, h.org, h.org + (float3)h.dims * h.step).Schedule(eye.dims.z, 4, jh);
            jh = new ClearSpreadJob { mat = eye.mat, vis = eye.vis, dims = eye.dims }.Schedule(jh);
            new DiffJob { a = old, b = eye.vis, dims = eye.dims, box = gb, c0 = 0, c1 = eye.dims - 1 }.Schedule(jh).Complete();
            old.Dispose(); gb.Dispose();
            int k = 0; foreach (var g in a.All) g.vis.CopyFrom(save[k++]);
            foreach (var g in a.All) { g.tch.Clear(); g.tbox.Clear(); g.tmn = int.MaxValue; g.tmx = int.MinValue; }
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

            static int Div(int a, int b, double ib) { int q = (int)(a * ib); if (q * b > a) q--; else if ((q + 1) * b <= a) q++; return q; }

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
                double iW = 1.0 / W, iH = 1.0 / H;
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
                        // часть 7: координаты без целочисленного деления (частное через обратное в double с поправкой — точное),
                        // соседи — сдвигом индекса; порядок соседей прежний (+x, −x, +y, −y, +z, −z)
                        int yz = Div(i, W, iW), x = i - yz * W, z = Div(yz, H, iH), y = yz - z * H;
                        for (int dd = 0; dd < 6; dd++)
                        {
                            int j;
                            if (dd == 0) { if (x + 1 >= W) continue; j = i + 1; }
                            else if (dd == 1) { if (x == 0) continue; j = i - 1; }
                            else if (dd == 2) { if (y + 1 >= H) continue; j = i + W; }
                            else if (dd == 3) { if (y == 0) continue; j = i - W; }
                            else if (dd == 4) { if (z + 1 >= D) continue; j = i + W * H; }
                            else { if (z == 0) continue; j = i - W * H; }
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
            public int3 c0, c1;                          // часть 7: обходятся ячейки тела [c0, c1] (индекс джоба — слой c0.z + zi)

            public void Execute(int zi)
            {
                int z = c0.z + zi;
                for (int y = c0.y; y <= c1.y; y++)
                for (int x = c0.x; x <= c1.x; x++)
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
