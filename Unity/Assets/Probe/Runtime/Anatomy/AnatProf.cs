namespace VoxProbe
{
    /// Разбивка времени удара по частям (часть 4): Begin() в начале удара, Lap(k) — время с прошлой отметки уходит в слот k.
    /// Один Stopwatch на всё, без выделений. Читает автозамер (AnatBenchRunner) после каждого удара.
    public static class AnatProf
    {
        public const int Pick = 0, Locate = 1, Spot = 2, Apply = 3, Lids = 4, Fail = 5, Smear = 6, Macer = 7, Break = 8, Rip = 9, DmgVis = 10,
            BoreStop = 11, BoreCut = 12, BoreCsf = 13, BoreVis = 14, Rest = 15, RestVis = 16, Mesh = 17, N = 18;
        public static readonly string[] Names = { "pick", "locate", "spot", "apply", "lids", "fail", "smear", "macer", "break", "rip", "dmgVis",
            "boreStop", "boreCut", "boreCsf", "boreVis", "rest", "restVis", "mesh" };
        public static readonly double[] t = new double[N];
        public static int removes, takes;
        public static double meshWait, meshApply;   // AnatBody.Complete: ожидание джоба сеток и применение (копится, сбрасывает Begin)
        public static double chunkMax;               // часть 6: самый долгий чанк (время джоба) с последнего сброса
        static readonly System.Diagnostics.Stopwatch clk = System.Diagnostics.Stopwatch.StartNew();
        public static double Now() => clk.Elapsed.TotalMilliseconds;
        static readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        static double last;

        public static void Begin() { System.Array.Clear(t, 0, N); removes = takes = 0; meshWait = meshApply = 0; sw.Restart(); last = 0; }
        public static void Lap(int k) { double e = sw.Elapsed.TotalMilliseconds; t[k] += e - last; last = e; }

        /// То же для AnatView.Tick: TBegin() в начале, TLap(k) — в слот tk[k].
        public const int TStep = 0, TDrops = 1, TDetach = 2, TIslWait = 3, TIslStep = 4, TRequest = 5, TPieces = 6, TFlush = 7, TN = 8;
        public static readonly string[] TNames = { "step", "drops", "detach", "islWait", "islStep", "request", "pieces", "flush" };
        public static readonly double[] tk = new double[TN];
        static readonly System.Diagnostics.Stopwatch tsw = new System.Diagnostics.Stopwatch();
        static double tlast;
        public static void TBegin() { System.Array.Clear(tk, 0, TN); tsw.Restart(); tlast = 0; }
        public static void TLap(int k) { double e = tsw.Elapsed.TotalMilliseconds; tk[k] += e - tlast; tlast = e; }
    }
}
