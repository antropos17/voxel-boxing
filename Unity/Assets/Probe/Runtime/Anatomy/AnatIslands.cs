using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxProbe
{
    /// Поиск оторванных кусков головы (этап B, часть 2). Работает по снимку решётки одним Burst-джобом на рабочем потоке:
    /// запрос в кадре удара, результат — в следующем кадре (AnatView.Tick), главный поток не ждёт.
    /// Обычный поиск — от ячеек, убранных с прошлого запроса (их соседей): обход из каждой затравки ограничен Cap ячейками;
    /// дошёл до якоря (низ шеи, y < AnchorRows) или до предела — кусок держится, иначе это оторванный остров.
    /// Редкий полный обход (после разрыва): заливка от якоря по всей решётке, всё недостигнутое — острова; ловит большие куски
    /// (свод, половина головы, голова целиком), которые ограниченный обход принял бы за держащиеся.
    /// Связь: сплошное (не пусто, не воздух) и не жидкость — ликвор и кровь куски не держат. Covered (место челюсти, глаз, зубов) связывает.
    public class AnatIslands : System.IDisposable
    {
        public const int Cap = 60000, AnchorRows = 2;
        public readonly AnatGrid g;
        NativeArray<byte> snap, link;
        public NativeArray<byte> mark;          // после джоба: 1 — держится, 2 — остров, 0 — не обходилось
        public NativeList<int> cells;           // ячейки островов подряд
        public NativeList<int2> comps;          // (начало в cells, число)
        NativeList<int> seeds, jobSeeds, queue;
        JobHandle job;
        public bool busy, pendingFull;
        bool doneFull;
        public double lastMs, lastSchedMs; public int requests, fullPasses;
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        public AnatIslands(AnatGrid g)
        {
            this.g = g; int n = g.Count;
            snap = new NativeArray<byte>(n, Allocator.Persistent); mark = new NativeArray<byte>(n, Allocator.Persistent);
            link = new NativeArray<byte>(64, Allocator.Persistent);
            for (int m = 0; m < AnatMat.Count; m++) link[m] = (byte)(m != AnatMat.Empty && m != AnatMat.Air && AnatMat.Class((byte)m) != AnatMat.ClsLiquid ? 1 : 0);
            link[AnatMat.Covered] = 1;
            cells = new NativeList<int>(4096, Allocator.Persistent); comps = new NativeList<int2>(64, Allocator.Persistent);
            seeds = new NativeList<int>(4096, Allocator.Persistent); jobSeeds = new NativeList<int>(4096, Allocator.Persistent); queue = new NativeList<int>(Cap + 64, Allocator.Persistent);
        }

        public static bool Links(byte m) => m != AnatMat.Empty && m != AnatMat.Air && (m == AnatMat.Covered || AnatMat.Class(m) != AnatMat.ClsLiquid);

        /// Поставить поиск: затравки — убранные ячейки (их соседи), full — полный обход от якоря. Если джоб ещё идёт, запрос копится.
        public void Request(NativeList<int> removed, bool full)
        {
            for (int k = 0; k < removed.Length; k++) seeds.Add(removed[k]);
            if (full) pendingFull = true;
        }

        public bool HasWork => seeds.Length > 0 || pendingFull;

        /// Запустить джоб по снимку текущей решётки (снимок — копия 1,9 МБ, ~0,2 мс).
        public void Schedule()
        {
            if (busy || !HasWork) return;
            sw.Restart();
            snap.CopyFrom(g.mat);
            doneFull = pendingFull;
            jobSeeds.Clear(); jobSeeds.AddRange(seeds.AsArray()); seeds.Clear();
            job = new IslandJob { snap = snap, link = link, mark = mark, cells = cells, comps = comps, seeds = jobSeeds.AsArray(), queue = queue,
                dims = g.dims, full = pendingFull, cap = Cap, anchorRows = AnchorRows }.Schedule();
            busy = true; pendingFull = false; requests++; if (doneFull) fullPasses++;
            lastSchedMs = sw.Elapsed.TotalMilliseconds;
        }

        /// Готов ли результат (не ждёт). После true — читать comps/cells/mark, затем Clear().
        public bool Ready()
        {
            if (!busy || !job.IsCompleted) return false;
            job.Complete(); busy = false;
            lastMs = sw.Elapsed.TotalMilliseconds;
            return true;
        }

        /// Дождаться (для тестов и снимков).
        public void Wait() { if (busy) { job.Complete(); busy = false; lastMs = sw.Elapsed.TotalMilliseconds; } }

        public void Reset() { if (busy) { job.Complete(); busy = false; } seeds.Clear(); cells.Clear(); comps.Clear(); pendingFull = false; }

        public void Dispose()
        {
            if (busy) job.Complete();
            snap.Dispose(); mark.Dispose(); link.Dispose(); cells.Dispose(); comps.Dispose(); seeds.Dispose(); jobSeeds.Dispose(); queue.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct IslandJob : IJob
        {
            [ReadOnly] public NativeArray<byte> snap, link;
            public NativeArray<byte> mark;
            public NativeList<int> cells, queue;
            public NativeList<int2> comps;
            [ReadOnly] public NativeArray<int> seeds;
            public int3 dims; public bool full; public int cap, anchorRows;

            bool L(int i) => link[snap[i]] != 0;

            public void Execute()
            {
                int W = dims.x, H = dims.y, D = dims.z, WH = W * H, n = WH * D;
                cells.Clear(); comps.Clear();
                for (int i = 0; i < n; i++) mark[i] = 0;
                if (full)
                {
                    // заливка от якоря: всё, что достижимо от низа шеи, держится
                    queue.Clear();
                    for (int z = 0; z < D; z++) for (int y = 0; y < anchorRows; y++) for (int x = 0; x < W; x++)
                    { int i = x + W * (y + H * z); if (L(i)) { mark[i] = 1; queue.Add(i); } }
                    for (int h = 0; h < queue.Length; h++) Spread(queue[h], 1, W, H, D, WH);
                    // недостигнутое сплошное — острова (каждый связный кусок отдельно)
                    for (int i = 0; i < n; i++) if (mark[i] == 0 && L(i)) Collect(i, W, H, D, WH, int.MaxValue);
                    // висящая жидкость: ликвор и кровь без держащегося соседа и с пустым соседом — последний набор (число со знаком минус)
                    int o0 = cells.Length;
                    for (int z = 1; z < D - 1; z++) for (int y = 1; y < H - 1; y++) for (int x = 1; x < W - 1; x++)
                    {
                        int i = x + W * (y + H * z); byte m = snap[i]; if (m == 0 || L(i) || m == AnatMat.Air) continue;
                        bool held = mark[i + 1] == 1 || mark[i - 1] == 1 || mark[i + W] == 1 || mark[i - W] == 1 || mark[i + WH] == 1 || mark[i - WH] == 1;
                        bool open = snap[i + 1] == 0 || snap[i - 1] == 0 || snap[i + W] == 0 || snap[i - W] == 0 || snap[i + WH] == 0 || snap[i - WH] == 0;
                        if (!held && open) cells.Add(i);
                    }
                    if (cells.Length > o0) comps.Add(new int2(o0, -(cells.Length - o0)));
                    queue.Clear();
                    return;
                }
                for (int s = 0; s < seeds.Length; s++)
                {
                    int c = seeds[s];
                    int x0 = c % W, y0 = (c / W) % H, z0 = c / WH;
                    for (int d = 0; d < 6; d++)
                    {
                        int x = x0 + (d == 0 ? 1 : d == 1 ? -1 : 0), y = y0 + (d == 2 ? 1 : d == 3 ? -1 : 0), z = z0 + (d == 4 ? 1 : d == 5 ? -1 : 0);
                        if (x < 0 || y < 0 || z < 0 || x >= W || y >= H || z >= D) continue;
                        int i = x + W * (y + H * z);
                        if (mark[i] == 0 && L(i)) Collect(i, W, H, D, WH, cap);
                    }
                }
            }

            /// Обход из ячейки start: если не дошёл до якоря и уложился в limit — остров (mark 2, в cells и comps), иначе mark 1.
            void Collect(int start, int W, int H, int D, int WH, int limit)
            {
                int c0 = cells.Length;
                cells.Add(start); mark[start] = 3;   // 3 — в текущем обходе
                bool anchored = (start / W) % H < anchorRows && !full;
                for (int h = c0; h < cells.Length && !anchored; h++)
                {
                    if (cells.Length - c0 > limit) { anchored = true; break; }
                    int c = cells[h];
                    int x = c % W, y = (c / W) % H, z = c / WH;
                    for (int d = 0; d < 6; d++)
                    {
                        int xx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), yy = y + (d == 2 ? 1 : d == 3 ? -1 : 0), zz = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
                        if (xx < 0 || yy < 0 || zz < 0 || xx >= W || yy >= H || zz >= D) continue;
                        int i = xx + W * (yy + H * zz);
                        if (!L(i)) continue;
                        byte mk = mark[i];
                        if (mk == 1) { anchored = true; break; }   // дошёл до держащегося
                        if (mk != 0) continue;
                        if (!full && yy < anchorRows) { anchored = true; break; }
                        mark[i] = 3; cells.Add(i);
                    }
                }
                if (anchored)
                {
                    // держится: пометить пройденное (продолжать не нужно — связь с держащимся уже есть)
                    for (int k = c0; k < cells.Length; k++) mark[cells[k]] = 1;
                    cells.Length = c0;
                    return;
                }
                for (int k = c0; k < cells.Length; k++) mark[cells[k]] = 2;
                comps.Add(new int2(c0, cells.Length - c0));
            }

            void Spread(int c, byte v, int W, int H, int D, int WH)
            {
                int x = c % W, y = (c / W) % H, z = c / WH;
                for (int d = 0; d < 6; d++)
                {
                    int xx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), yy = y + (d == 2 ? 1 : d == 3 ? -1 : 0), zz = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
                    if (xx < 0 || yy < 0 || zz < 0 || xx >= W || yy >= H || zz >= D) continue;
                    int i = xx + W * (yy + H * zz);
                    if (mark[i] != 0 || !L(i)) continue;
                    mark[i] = v; queue.Add(i);
                }
            }
        }
    }
}
