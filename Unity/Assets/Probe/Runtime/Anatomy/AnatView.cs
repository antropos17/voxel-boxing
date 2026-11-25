using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxProbe
{
    /// Ядро интерактивного просмотра головы (без ввода): разрезы плоскостью, луч из камеры в воксель, удар, восстановление.
    /// Координаты головы — см (как в AnatGrid), мир — метры, корень головы в точке root.
    /// Состояния: base — исходная голова, dmg — голова с ударами без разреза, g.mat — dmg минус отрезанная половина.
    public class AnatView
    {
        public readonly AnatHead a;
        public readonly List<AnatBody> bodies = new List<AnatBody>();
        public readonly Transform root;
        readonly NativeArray<VertexAttributeDescriptor> layout;
        readonly List<byte[]> pristine = new List<byte[]>(), dmg = new List<byte[]>();
        readonly AnatGrid[] grids;

        public int cut;                      // 0 — целая голова, 1–4 — разрез
        public readonly float[] shift = new float[5];   // сдвиг плоскости каждого разреза, см
        public double lastMs;                // последняя перестройка (видимость + сетка), мс
        public string lastWhat = "сборка";
        public int lastRemoved;
        // этап B: повреждения мягких тканей и кровь на решётке головы (AnatDamage); капли в полёте
        public AnatDamage sim;
        public AnatBones bones;             // часть 9: переломы костей по §5 (смещение отломков — в шейдере)
        AnatBloodFx drops;
        readonly AnatBody headBody; readonly int headK;
        readonly Material material;
        // этап B, часть 2: поиск оторванных кусков (джоб, результат в следующем кадре) и сами куски
        public readonly AnatIslands isl;
        public readonly AnatPieces pieces;
        public readonly bool[] gone;          // отдельное тело (челюсть, глаз, зуб) оторвалось и стало куском
        float3 lastHit, lastDir; int lastStr;
        public int crumbs, detachedBodies;
        public double islMs, islMsMax, pieceMs, pieceMsMax;
        public int frameChunks;               // чанков, перестроенных в последнем кадре
        public double tSim, tBore, tRest, tMesh;   // последний удар: модель повреждений, пробой, разрывы и куски, сетки
        public int hitChunks = 0;             // чанков, перестраиваемых в самом Hit (часть 4: 0 — сетки строит Tick того же кадра по своему бюджету)
        // часть 3: итог зависит только от последовательности ударов и кадров. Поиск кусков, запущенный в кадре F, разбирается
        // в кадре F + IslDelay (джоб завершается принудительно, если ещё идёт); разбор — по кадрам, не больше IslBudget ячеек
        // за кадр (перенос в кусок и снятие с головы). Отдельное тело, сорванное ударом, отрывается в следующем кадре.
        public const int IslDelay = 2, IslBudget = 40000;
        public int frame;                     // номер кадра (Tick)
        int islAt, detachAt;
        int wk = -1, wph, wj; AnatPiece wp; float3 wcom;   // разбор: компонент, шаг (0 сбор, 1 перенос, 2 снятие), позиция
        NativeList<int> wtmp;
        readonly bool[] want;                 // тела к отрыву
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        readonly List<int> csf = new List<int>();
        NativeList<int> scanI; NativeList<float3> scanQ;   // Scan (Burst): индексы и центры ячеек капсулы

        /// Плоскости разрезов: удаляется всё, где dot(n, p) > d (p — см в рамке головы).
        public static readonly float3[] CutN = { float3.zero, new float3(1, 0, 0), new float3(0, 0, 1), new float3(0, 1, 0), new float3(0, 1, 0) };
        public static readonly float[] CutD = { 0, 0.6f, 6.0f, 6.0f, -5.25f };
        public static readonly string[] CutName = { "целая", "сагиттальный", "фронтальный через глазницы", "горизонтальный через мозг", "через челюсти и зубы" };

        /// Сила удара: E — в единицах веба (джеб ≈ 1, мощный 3,4–4,6) для модели повреждений; тяжёлый ещё пробивает канал r × depth
        /// (кость тоже — переломов до этапа C нет); в мозге канал идёт без остановки, иначе останавливается на мозге.
        public struct Strength { public string name; public float E; public bool power, bore; public float r, depth; }
        public static readonly Strength[] Strengths =
        {
            new Strength { name = "лёгкий",  E = 1.2f, power = false },
            new Strength { name = "средний", E = 3.0f, power = true },
            new Strength { name = "тяжёлый", E = 4.4f, power = true, bore = true, r = 1.4f, depth = 3.0f },
        };
        public static bool Brain(byte m) => m == AnatMat.Grey || m == AnatMat.White || m == AnatMat.CerebGrey || m == AnatMat.Stem || m == AnatMat.Csf;

        public AnatView(Transform parent, Vector3 center, Material material) : this(parent, center, material, default) { }

        /// look — внешность соперника (тон кожи, причёска, цвет волос; AnatLook.cs).
        public AnatView(Transform parent, Vector3 center, Material material, AnatLook look)
        {
            AnatPalette.Apply(look);
            if (AnatBones.B10Off) Shader.SetGlobalFloat("_AnatB10Off", 1);   // часть 10: снимки «без» через игровой слой
            this.material = material;
            a = AnatomyBuilder.Build(look);
            a.vis.Warm();
            layout = new NativeArray<VertexAttributeDescriptor>(AnatMesher.Layout, Allocator.Persistent);
            root = new GameObject("anat").transform;
            root.SetParent(parent, false); root.position = center;
            int id = 0;
            var gl = new List<AnatGrid>();
            foreach (var g in a.All) { bodies.Add(new AnatBody(g, root, material, id++)); gl.Add(g); pristine.Add(g.mat.ToArray()); dmg.Add(g.mat.ToArray()); }
            grids = gl.ToArray();
            headK = gl.IndexOf(a.head); headBody = bodies[headK];
            gone = new bool[grids.Length]; want = new bool[grids.Length];
            wtmp = new NativeList<int>(4096, Allocator.Persistent);
            scanI = new NativeList<int>(16384, Allocator.Persistent); scanQ = new NativeList<float3>(16384, Allocator.Persistent);
            isl = new AnatIslands(a.head);
            pieces = new AnatPieces(root, material);
            bones = new AnatBones(a);
            NewSim();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var b in bodies) b.BuildAll(layout);
            lastMs = sw.Elapsed.TotalMilliseconds;
        }

        public int Triangles { get { MeshDone(); int t = 0; foreach (var b in bodies) t += b.Triangles; return t; } }
        public int Chunks { get { int c = 0; foreach (var b in bodies) c += b.ChunkCount; return c; } }
        public int ChunksDrawn { get { MeshDone(); int c = 0; foreach (var b in bodies) for (int i = 0; i < b.ChunkCount; i++) if (b.quads[i] > 0) c++; return c; } }

        // часть 5: последние 16 переломов головы в шейдер (трещины на кости): центр и радиус в ячейках головы, ось удара
        static readonly int FracId = Shader.PropertyToID("_AnatFrac"), FracNId = Shader.PropertyToID("_AnatFracN"), FracBodyId = Shader.PropertyToID("_AnatFracBody");
        readonly Vector4[] fracBuf = new Vector4[32]; int fracSent = -1;
        void SendFractures()
        {
            int n = sim.fractures.Count, k0 = math.max(0, n - 16); var h = a.head;
            for (int k = k0; k < n; k++)
            {
                float3 c = (sim.fractures[k] - h.org) / h.step; float4 ax = sim.fracAxis[k];
                fracBuf[(k - k0) * 2] = new Vector4(c.x, c.y, c.z, ax.w / h.step); fracBuf[(k - k0) * 2 + 1] = new Vector4(ax.x, ax.y, ax.z, k * 1.7f);
            }
            Shader.SetGlobalVectorArray(FracId, fracBuf); Shader.SetGlobalFloat(FracNId, n - k0); Shader.SetGlobalFloat(FracBodyId, headK);
            fracSent = n;
        }

        // часть 9: отломки §5 в шейдер — текстуры веса (голова: 2, челюсть: 1), смещения в ячейках тела; сдвиг глаза и зубов целиком
        static MaterialPropertyBlock fmpb;
        static readonly int FragAId = Shader.PropertyToID("_AnatFragA"), FragBId = Shader.PropertyToID("_AnatFragB"), FragParId = Shader.PropertyToID("_FragPar"),
            FragDimId = Shader.PropertyToID("_FragDim"), FragDId = Shader.PropertyToID("_FragD"), ShiftId = Shader.PropertyToID("_BodyShift");
        readonly Vector4[] fragD = new Vector4[8];
        void SendBones()
        {
            bones.dirty = false; fmpb ??= new MaterialPropertyBlock();
            for (int body = 0; body < 2; body++)
            {
                var g = body == 0 ? a.head : a.mand; var B = bodies[System.Array.IndexOf(grids, g)];
                int k0 = body == 0 ? 0 : 2, nt = body == 0 ? 2 : 1; bool any = false;
                System.Array.Clear(fragD, 0, fragD.Length);
                foreach (var r in bones.list)
                {
                    if (r.body != body || r.slot < 0) continue; any = true;
                    float3 d = math.mul(math.transpose(g.rot), r.d) / g.step; fragD[r.slot] = new Vector4(d.x, d.y, d.z, r.t);
                }
                int3 td = bones.tdims[body];
                if (any)
                    for (int k = k0; k < k0 + nt; k++)
                    {
                        if (bones.tex[k] == null) bones.tex[k] = new Texture3D(td.x, td.y, td.z, TextureFormat.RGBA32, false) { name = g.name + "-frag" + k, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                        bones.tex[k].SetPixelData(bones.data[k], 0); bones.tex[k].Apply(false, false);
                    }
                foreach (var rr in B.rs)
                {
                    rr.GetPropertyBlock(fmpb);
                    fmpb.SetVector(FragParId, new Vector4(any ? 1 : 0, nt, 0, 0));
                    if (any) { fmpb.SetTexture(FragAId, bones.tex[k0]); fmpb.SetTexture(FragBId, bones.tex[body == 0 ? 1 : 2]); fmpb.SetVector(FragDimId, new Vector4(td.x, td.y, td.z, 0)); fmpb.SetVectorArray(FragDId, fragD); }
                    rr.SetPropertyBlock(fmpb);
                }
            }
            // Ле Фор III уносит скулы и края глазниц — глаз уходит вместе с ними наполовину, иначе выпирает из запавшего лица
            float3 lf = bones.Level(AnatBones.Maxilla) >= 3 ? bones.upperShift * 0.6f : 0;
            Shift(a.eyeL, bones.eyeShift[0] + lf); Shift(a.eyeR, bones.eyeShift[1] + lf);
            for (int t = 0; t < a.teeth.Length; t++) Shift(a.teeth[t], a.toothDefs[t].upper == 1 ? bones.upperShift : bones.lowerShift[t]);
        }
        void Shift(AnatGrid g, float3 dcm)
        {
            var B = bodies[System.Array.IndexOf(grids, g)]; float3 d = math.mul(math.transpose(g.rot), dcm) / g.step;
            foreach (var rr in B.rs) { rr.GetPropertyBlock(fmpb); fmpb.SetVector(ShiftId, new Vector4(d.x, d.y, d.z, 0)); rr.SetPropertyBlock(fmpb); }
        }

        void NewSim()
        {
            MeshDone();
            sim?.Dispose();
            sim = new AnatDamage(a);
            var dm = dmg[headK];
            sim.onMat = (i, m) => dm[i] = m;
            headBody.st = sim.st;
            drops?.Destroy(); drops = new AnatBloodFx(root, material, layout);
            Shader.SetGlobalFloat("_AnatTime", 0); fracSent = -1; SendFractures();
            bones.Reset(); SendBones();
            if (wp != null) { pieces.Drop(wp); wp = null; }
            wk = -1; if (want != null) System.Array.Clear(want, 0, want.Length);
            isl?.Reset(); pieces?.Clear(); crumbs = 0; detachedBodies = 0;
            if (gone != null) for (int k = 0; k < gone.Length; k++) { if (gone[k]) bodies[k].root.gameObject.SetActive(true); gone[k] = false; }
        }

        /// Время боя: раны кровят, подтёки бегут, капли летят, отёк растёт, синяки меняют цвет; перестройка грязных чанков
        /// с бюджетом (не больше maxChunks за кадр).
        public void Tick(float dt, int maxChunks = 12)
        {
            AnatProf.TBegin();
            MeshDone();   // сетки, поставленные прошлым кадром, — до любых правок решётки и состояний
            sim.grav = Down();
            sim.Step(dt);
            AnatProf.TLap(AnatProf.TStep);
            Shader.SetGlobalFloat("_AnatTime", sim.time);
            if (sim.fractures.Count != fracSent) SendFractures();
            bones.Due(frame); if (bones.dirty && !bones.Pending) SendBones();
            drops.Update(sim.drops, sim.time);
            AnatProf.TLap(AnatProf.TDrops);
            // отдельные тела, сорванные ударом прошлого кадра; куски: результат поиска в назначенном кадре, разбор по бюджету,
            // затем новый поиск, если накопились убранные ячейки
            if (frame >= detachAt) DetachWanted();
            AnatProf.TLap(AnatProf.TDetach);
            clock.Restart();
            bool work = IslandsDue(false); AnatProf.TLap(AnatProf.TIslWait); IslandStep(IslBudget); AnatProf.TLap(AnatProf.TIslStep);
            if (work) { islMs = clock.Elapsed.TotalMilliseconds; islMsMax = math.max(islMsMax, islMs); }
            Request(false);
            AnatProf.TLap(AnatProf.TRequest);
            clock.Restart();
            pieces.Step(dt);
            double pm = clock.Elapsed.TotalMilliseconds; pieceMs = pm; pieceMsMax = math.max(pieceMsMax, pm);
            AnatProf.TLap(AnatProf.TPieces);
            Flush(maxChunks);
            AnatProf.TLap(AnatProf.TFlush);
            frame++;
        }

        /// Передать убранные ячейки в поиск кусков и запустить его, если он свободен и прошлый результат разобран.
        void Request(bool full)
        {
            if (HairFall && DropHair() > 0) sim.Flush();   // волосы над убранной кожей (удар, пробой, оторванный кусок)
            if (sim.cutLog.Length > 0 || full) { isl.Request(sim.cutLog, full); sim.cutLog.Clear(); }
            if (isl.busy || wk >= 0) return;   // снимок и списки поиска общие: новый — только после разбора прошлого
            isl.Schedule(); if (isl.busy) islAt = frame + IslDelay;
        }

        /// Результат поиска: в назначенном кадре (now — сразу) дождаться джоба и начать разбор. true — разбор идёт.
        bool IslandsDue(bool now)
        {
            if (isl.busy && (now || frame >= islAt)) { isl.Wait(); wk = 0; wph = 0; wj = 0; }
            return wk >= 0;
        }

        /// Дождаться поиска кусков и разобрать его целиком (для снимков и тестов: в игре это делают следующие кадры).
        public void Settle(bool full = false)
        {
            MeshDone();
            bones.Complete(); if (bones.dirty) SendBones();
            DetachWanted();
            if (full) { IslandsDue(true); IslandStep(int.MaxValue); Request(true); }
            for (int k = 0; k < 8 && (isl.busy || isl.HasWork || sim.cutLog.Length > 0); k++) { Request(false); IslandsDue(true); IslandStep(int.MaxValue); }
            Flush(4096);
        }

        /// Разбор найденных островов, не больше budget ячеек: крошки (меньше MinPiece ячеек) рассыпаются, остальное — отдельные
        /// куски с физикой (кусок строится по частям, затем его ячейки снимаются с головы); в конце отдельное тело, под которым
        /// не осталось головы, отрывается целиком.
        void IslandStep(int budget)
        {
            if (wk < 0) return;
            var h = a.head; var cells = isl.cells.AsArray();
            while (wk < isl.comps.Length && budget > 0)
            {
                int2 c = isl.comps[wk];
                if (c.y < 0)
                {
                    int n = math.min(-c.y - wj, budget);
                    for (int j = wj; j < wj + n; j++) { int i = cells[c.x + j]; if (h.mat[i] != AnatMat.Empty && !AnatIslands.Links(h.mat[i])) sim.Take(i); }
                    wj += n; budget -= n;
                    if (wj >= -c.y) { orphans += -c.y; wk++; wj = 0; }
                    continue;
                }
                if (wph == 0)
                {
                    // сбор: связные ячейки острова (по текущей решётке — снимок мог устареть), рамка, центр масс
                    wtmp.Clear();
                    bool open = false; int3 mn = int.MaxValue, mx = int.MinValue; float3 com = 0;
                    for (int j = 0; j < c.y; j++)
                    {
                        int i = cells[c.x + j]; if (!AnatIslands.Links(h.mat[i])) continue;
                        wtmp.Add(i); if (!open) open = Touches(h, i);
                        int3 q = new int3(i % h.dims.x, (i / h.dims.x) % h.dims.y, i / (h.dims.x * h.dims.y));
                        mn = math.min(mn, q); mx = math.max(mx, q); com += h.Local(q.x, q.y, q.z);
                    }
                    budget -= c.y / 4 + 1;
                    if (wtmp.Length == 0) { wk++; continue; }
                    if (wtmp.Length < MinPiece)
                    {
                        // крошка: рассыпается, если её видно (в целой голове есть изолированные точки коры в ликворе — их не трогать)
                        if (open) { for (int j = 0; j < wtmp.Length; j++) sim.Take(wtmp[j]); crumbs++; }
                        wk++; continue;
                    }
                    wcom = com / wtmp.Length;
                    wp = pieces.Begin(h, mn, mx); wph = 1; wj = 0;
                    continue;
                }
                if (wph == 1)
                {
                    int n = math.min(wtmp.Length - wj, budget);
                    pieces.Fill(wp, h, sim.st, wtmp.AsArray(), wj, n); wj += n; budget -= n;
                    if (wj < wtmp.Length) continue;
                    var pc = pieces.End(wp, h, Fling(wcom, wtmp.Length), PieceName(wtmp.AsArray())); wp = null;
                    if (pc != null) Budget(pc);
                    wph = 2; wj = 0;
                    continue;
                }
                {
                    int n = math.min(wtmp.Length - wj, budget);
                    for (int j = wj; j < wj + n; j++) sim.Take(wtmp[j]);
                    wj += n; budget -= n;
                    if (wj >= wtmp.Length) { wk++; wph = 0; wj = 0; }
                }
            }
            if (wk >= isl.comps.Length)
            {
                // отдельные тела без опоры
                for (int guard = 0; guard < 4; guard++)
                {
                    bool any = false;
                    for (int k = 0; k < grids.Length; k++)
                    {
                        if (k == headK || gone[k]) continue;
                        if (Unsupported(grids[k])) { Detach(k); any = true; }
                    }
                    if (!any) break;
                }
                wk = -1;
            }
            sim.Flush();
        }

        public int orphans;               // убрано висящей жидкости (полный обход)
        public const int MinPiece = 60;   // меньше — крошка (≈ 0,5 см³): рассыпается, слота куска не занимает

        static bool Touches(AnatGrid g, int i)
        {
            int x = i % g.dims.x, y = (i / g.dims.x) % g.dims.y, z = i / (g.dims.x * g.dims.y);
            return NearOpen(g, new int3(x, y, z));
        }

        static string PieceName(NativeArray<int> cells) => "кусок";

        /// Скорость куска: от точки последнего удара наружу и по удару, сила — по удару; большие куски медленнее.
        float3 Fling(float3 com, int n)
        {
            float3 away = math.normalizesafe(com - lastHit, -lastDir);
            float sp = (lastStr == 2 ? 160 : lastStr == 1 ? 110 : 60) / math.max(1, math.pow(n / 2000f, 0.25f));
            return (away * 0.6f + lastDir * 0.4f) * sp + new float3(0, 40, 0);
        }

        /// Отдельное тело без опоры: больше 60% проверенных ячеек лежат над пустотой головы или в оторванном острове.
        bool Unsupported(AnatGrid g) => SupportBad(g) > 0.6f;
        /// Доля ячеек тела (выборка до 160) над пустотой головы или в оторванном острове.
        public float SupportBad(AnatGrid g)
        {
            var h = a.head; int n = 0, bad = 0, stepI = math.max(1, g.Count / 2048);
            for (int i = 0; i < g.Count && n < 160; i += stepI)
            {
                if (!AnatMat.Solid(g.mat[i])) continue;
                int j = h.Lookup(g.World(g.Local(i % g.dims.x, (i / g.dims.x) % g.dims.y, i / (g.dims.x * g.dims.y))));
                n++; if (j < 0 || h.mat[j] == AnatMat.Empty || isl.mark[j] == 2) bad++;
            }
            return n == 0 ? 0 : bad / (float)n;
        }

        /// Оторвать отдельное тело целиком: кусок из всех его ячеек, тело скрыто, его место в голове пустеет.
        void Detach(int k)
        {
            var g = grids[k]; var dm = dmg[k]; var h = a.head;
            var all = new NativeList<int>(g.Count, Allocator.Temp);
            for (int i = 0; i < g.Count; i++) if (g.mat[i] != AnatMat.Empty) all.Add(i);
            float3 com = 0; foreach (int i in all) com += g.World(g.Local(i % g.dims.x, (i / g.dims.x) % g.dims.y, i / (g.dims.x * g.dims.y)));
            if (all.Length > 0) { com /= all.Length; var pc = pieces.Make(g, null, all.AsArray(), 0, all.Length, Fling(com, all.Length), g.name); if (pc != null) Budget(pc); }
            foreach (int i in all)
            {
                int j = h.Lookup(g.World(g.Local(i % g.dims.x, (i / g.dims.x) % g.dims.y, i / (g.dims.x * g.dims.y))));
                if (j >= 0 && h.mat[j] == AnatMat.Covered) sim.Take(j);
                g.mat[i] = AnatMat.Empty; dm[i] = AnatMat.Empty;
            }
            all.Dispose();
            gone[k] = true; detachedBodies++;
            bodies[k].root.gameObject.SetActive(false);
        }

        /// Сорвать отдельное тело: в следующем кадре (кадр удара не ждёт полного прохода по решётке тела).
        void TryDetach(int k) { if (k >= 0 && k != headK && !gone[k]) { want[k] = true; detachAt = frame + 1; } }

        void DetachWanted()
        {
            bool any = false;
            for (int k = 0; k < want.Length; k++) if (want[k]) { want[k] = false; if (!gone[k]) { Detach(k); any = true; } }
            if (any) sim.Flush();
        }

        /// Небольшой кусок строится сразу (1–2 чанка), большой — по бюджету в следующих кадрах.
        void Budget(AnatPiece pc) { if (pc.body != null && pc.body.ChunkCount <= 2) { pc.body.BuildAll(layout); pc.ready = true; } }

        /// Тяжесть в рамке покоя головы: мировой низ, повёрнутый обратно поворотом черепа (всё выше C1 движется с ним жёстко).
        /// Кровь на шее стекает по той же оси — у якоря это отступление меньше угла изгиба шеи.
        public static float3 Down()
        {
            if (!AnatSkin.on) return new float3(0, -1, 0);
            AnatSkin.Skin(new float3(0, 4, 0), out float3x3 M);
            return math.normalizesafe(math.mul(math.transpose(M), new float3(0, -1, 0)), new float3(0, -1, 0));
        }

        /// Часть 4: сетки чанков, поставленные в Flush, считаются на рабочих потоках, пока кадр рисуется, и забираются здесь:
        /// в начале следующего Tick или Hit (до любых правок решётки и состояний, которые читает джоб) или перед чтением сеток.
        /// По умолчанию выключено: в автозамере без ограничения кадров удар тогда ждёт сетки прошлого кадра (см. ANATOMY-B.md, часть 4).
        public bool asyncMesh;
        public void MeshDone()
        {
            foreach (var b in bodies) b.Complete();
            foreach (var p in pieces.list) p.body?.Complete();
        }

        /// Перестройка грязных чанков: всего не больше maxChunks за вызов на все тела и куски (голова первой).
        void Flush(int maxChunks)
        {
            MeshDone();
            for (int i = 0; i < sim.dirty.Length; i++) if (sim.dirty[i]) { sim.dirty[i] = false; headBody.MarkDirtyCells(i, sim.dmn[i], sim.dmx[i]); }
            foreach (var b in bodies) b.SyncVis();
            frameChunks = 0;
            for (int guard = 0; guard < 64; guard++)
            {
                int left = maxChunks, keep = 0;
                foreach (var p in pieces.list) if (p.body != null && p.body.dirtyCount > 0) { keep = math.min(4, maxChunks / 3); break; }   // доля кусков, чтобы они не ждали голову
                foreach (var b in bodies) if (b.dirtyCount > 0 && left > keep) { int d0 = b.dirtyCount; b.Schedule(layout, left - keep); left -= d0 - b.dirtyCount; }
                foreach (var p in pieces.list) if (p.body != null && p.body.dirtyCount > 0 && left > 0) { int d0 = p.body.dirtyCount; p.body.Schedule(layout, left); left -= d0 - p.body.dirtyCount; }
                frameChunks += maxChunks - left;
                if (asyncMesh && maxChunks < 1000) { JobHandle.ScheduleBatchedJobs(); break; }   // запустить сразу (иначе Unity держит джобы до Complete); забирает MeshDone
                MeshDone();
                if (left == maxChunks || maxChunks < 1000) break;
            }
        }

        public int PieceTriangles { get { MeshDone(); int t = 0; foreach (var p in pieces.list) if (p.body != null) t += p.body.Triangles; return t; } }

        public float3 ToHead(Vector3 world) => (float3)(world - root.position) * 100f;
        public Vector3 ToWorld(float3 cm) => root.position + (Vector3)(cm * 0.01f);

        /// Включить разрез (0 — целая голова). Всё заново: видимость и сетки всех тел.
        public void SetCut(int c) { cut = c; Apply(c == 0 ? "целая голова" : "разрез " + c); }
        public void Shift(float dcm) { if (cut == 0) return; shift[cut] += dcm; Apply("плоскость " + cut + " " + (shift[cut] >= 0 ? "+" : "") + shift[cut].ToString("0.0") + " см"); }

        /// R: голова как была (удары и разрез сброшены, сдвиги плоскостей — тоже).
        public void Restore()
        {
            for (int k = 0; k < grids.Length; k++) System.Array.Copy(pristine[k], dmg[k], dmg[k].Length);
            for (int i = 0; i < shift.Length; i++) shift[i] = 0;
            cut = 0;
            NewSim(); Apply("восстановлено");
        }

        void Apply(string what)
        {
            MeshDone();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float3 n = CutN[cut]; float d = CutD[cut] + shift[cut];
            for (int k = 0; k < grids.Length; k++)
            {
                var g = grids[k]; var src = dmg[k];
                g.mat.CopyFrom(src);
                if (cut == 0) continue;
                for (int z = 0; z < g.dims.z; z++) for (int y = 0; y < g.dims.y; y++) for (int x = 0; x < g.dims.x; x++)
                {
                    int i = g.Index(x, y, z);
                    if (src[i] != 0 && math.dot(n, g.World(g.Local(x, y, z))) > d) g.mat[i] = 0;
                }
            }
            a.vis.Full();
            foreach (var b in bodies) b.BuildAll(layout);
            lastMs = sw.Elapsed.TotalMilliseconds; lastWhat = what; lastRemoved = 0;
        }

        /// Направление удара в осях покоя у последней точки Pick (при изгибе шеи отличается от направления луча).
        public float3 pickDir;

        /// Луч (мир) → первая сплошная ячейка (кожа, кость, зуб, глаз…). Точка входа — в см головы, в позе покоя.
        /// При изгибе шеи (AnatSkin.on) луч идёт в позе, каждая его точка переводится в покой обратным изгибом.
        public bool Pick(Ray ray, out float3 hit, out byte mat, out AnatGrid from)
        {
            hit = 0; mat = 0; from = null;
            float3 o = ToHead(ray.origin), dir = math.normalize((float3)ray.direction);
            pickDir = dir;
            bool bent = AnatSkin.on && math.any(math.abs(AnatSkin.Total) > 1e-5f);
            float3 Rest(float3 q) => bent ? AnatSkin.Unskin(q) : q;
            var h = a.head;
            // рамка решётки головы (в её собственных осях: pos и rot); при изгибе — с запасом на сдвиг
            var ir = math.transpose(h.rot);
            float3 ol = math.mul(ir, o - h.pos), dl = math.mul(ir, dir);
            float pad = bent ? 15f : 0f;
            float3 lo = h.org - pad, hi = h.org + (float3)h.dims * h.step + pad;
            float3 inv = 1f / dl, t0 = (lo - ol) * inv, t1 = (hi - ol) * inv;
            float tn = math.cmax(math.min(t0, t1)), tf = math.cmin(math.max(t0, t1));
            if (tf < math.max(tn, 0)) return false;
            const float st = 0.04f;
            for (float t = math.max(tn, 0); t <= tf; t += st)
            {
                float3 p = Rest(o + dir * t);
                byte m = a.Sample(p, out _, out var g);
                if (!AnatMat.Solid(m)) continue;
                // уточнить вход: половинным делением между прошлым и этим шагом
                float ta = t - st, tb = t;
                for (int it = 0; it < 6; it++) { float tm = (ta + tb) * 0.5f; if (AnatMat.Solid(a.Sample(Rest(o + dir * tm), out _, out _))) tb = tm; else ta = tm; }
                hit = Rest(o + dir * tb); mat = a.Sample(hit, out _, out from);
                if (!AnatMat.Solid(mat)) { hit = p; mat = m; from = g; }
                if (bent) pickDir = math.normalizesafe(Rest(o + dir * (tb + 0.1f)) - Rest(o + dir * (tb - 0.1f)), dir);
                return true;
            }
            return false;
        }

        /// Удар в точку hit (см головы, поза покоя) по направлению dir: полная картина повреждений (AnatDamage: покраснение, отёк,
        /// синяк, ссадина, рассечение, слои, размозжение, раны и кровь). Тяжёлый ещё пробивает канал вдоль удара во всех телах:
        /// до мозга, а если удар пришёлся в мозг — насквозь; в проломе стекает ликвор (открываются борозды).
        /// Удаления головы пишутся и в dmg (через sim.onMat), чтобы пережить смену разреза.
        public int Hit(float3 hit, float3 dir, int strength)
        {
            var s = Strengths[strength];
            dir = math.normalize(dir);
            AnatProf.Lap(AnatProf.Pick);
            MeshDone();
            var sw = clock; sw.Restart();
            int before = sim.removed;
            var hi = sim.Hit(hit, dir, s.E, s.power);
            bones.Hit(hi.zone, sim.lastP, sim.lastN, dir, s.E, s.power, sim.time, frame);
            tSim = sw.Elapsed.TotalMilliseconds;
            int removed = 0;
            if (s.bore)
            {
                byte entry = a.Sample(hit + dir * 0.05f, out _, out _);
                bool deep = Brain(entry);
                bool keep(byte m) => !deep && Brain(m);
                float3 p0 = hit - dir * (s.r * 0.6f), p1 = hit + dir * s.depth;
                float stop = float.MaxValue;
                foreach (var g in grids) { Scan(g, p0, p1, s.r); for (int j = 0; j < scanI.Length; j++) if (keep(g.mat[scanI[j]])) stop = math.min(stop, math.dot(scanQ[j] - p0, dir)); }
                AnatProf.Lap(AnatProf.BoreStop);
                var h = a.head;
                for (int k = 0; k < grids.Length; k++)
                {
                    var g = grids[k]; var dm = dmg[k]; bool isHead = g == h;
                    int3 mn = int.MaxValue, mx = int.MinValue;
                    Scan(g, p0, p1, s.r);
                    for (int j = 0; j < scanI.Length; j++)
                    {
                        int i = scanI[j]; float3 q = scanQ[j];
                        byte m = g.mat[i];
                        if (m == AnatMat.Empty || m == AnatMat.Covered || keep(m) || math.dot(q - p0, dir) >= stop) continue;
                        if (AnatDamage.Protected(q)) continue;   // культя шеи — якорь
                        int3 c = new int3(i % g.dims.x, (i / g.dims.x) % g.dims.y, i / (g.dims.x * g.dims.y));
                        if (isHead) sim.Cut(c.x, c.y, c.z);
                        else { g.mat[i] = AnatMat.Empty; dm[i] = AnatMat.Empty; removed++; mn = math.min(mn, c); mx = math.max(mx, c); }
                    }
                    if (!isHead && math.all(mn <= mx)) g.Touch(mn, mx);
                }
                AnatProf.Lap(AnatProf.BoreCut);
                // ликвор в проломе стекает: пустеют подоболочечное пространство и борозды до 1 см вокруг канала
                // (ячейки ликвора собираются одним проходом, дальше 6 волн только по ним)
                csf.Clear();
                Scan(h, p0, p1 + dir * 1.0f, s.r + 1.0f);
                for (int j = 0; j < scanI.Length; j++) { int i = scanI[j]; if (h.mat[i] == AnatMat.Csf && !AnatDamage.Protected(scanQ[j])) csf.Add(i); }
                // (обход с конца, как раньше с RemoveAt, но выжившие сдвигаются за один проход: тот же порядок, O(n))
                for (int pass = 0; pass < 6 && csf.Count > 0; pass++)
                {
                    int w = csf.Count;
                    for (int k = csf.Count - 1; k >= 0; k--)
                    {
                        int i = csf[k]; if (h.mat[i] != AnatMat.Csf) continue;
                        int3 c = new int3(i % h.dims.x, (i / h.dims.x) % h.dims.y, i / (h.dims.x * h.dims.y));
                        if (NearOpen(h, c)) { sim.Cut(c.x, c.y, c.z); continue; }
                        csf[--w] = i;
                    }
                    csf.RemoveRange(0, w);
                }
                AnatProf.Lap(AnatProf.BoreCsf);
                sim.Flush();
                AnatProf.Lap(AnatProf.BoreVis);
            }
            tBore = sw.Elapsed.TotalMilliseconds - tSim;
            lastHit = hit; lastDir = dir; lastStr = strength;
            bool full = false;
            // разрывы: в том же шаре и отдельные тела (челюсть, глаза, зубы); после разрыва — полный поиск кусков
            foreach (var rp in sim.rips)
            {
                full = true;
                for (int k = 0; k < grids.Length; k++)
                {
                    var g = grids[k]; if (g == a.head || gone[k]) continue; var dm = dmg[k];
                    int3 mn = int.MaxValue, mx = int.MinValue;
                    Scan(g, rp.xyz, rp.xyz, rp.w);
                    for (int j = 0; j < scanI.Length; j++)
                    {
                        int i = scanI[j]; if (g.mat[i] == AnatMat.Empty) continue;
                        g.mat[i] = AnatMat.Empty; dm[i] = AnatMat.Empty; removed++;
                        int3 c = new int3(i % g.dims.x, (i / g.dims.x) % g.dims.y, i / (g.dims.x * g.dims.y)); mn = math.min(mn, c); mx = math.max(mx, c);
                    }
                    if (math.all(mn <= mx)) g.Touch(mn, mx);
                }
            }
            sim.rips.Clear();
            // куски мозга на вылет: объёмный кусок с корой снаружи и белым веществом на изломе
            foreach (var e in sim.ejects) Brainout(e);
            sim.ejects.Clear();
            AnatProf.Lap(AnatProf.Rest);
            sim.Flush();
            AnatProf.Lap(AnatProf.RestVis);
            Request(full);
            // срыв челюсти и глаза (HEAD-SPEC, раздел 6): мощный удар в зону с травмой выше порога отрывает отдельное тело
            if (s.power)
            {
                var all = grids;
                byte z = sim.lastZone;
                if ((z == AnatZone.Jaw || z == AnatZone.Mouth) && sim.Eff(AnatZone.Jaw) > 46) TryDetach(System.Array.IndexOf(all, a.mand));
                if (sim.Eff(AnatZone.Eye) > 42)
                    for (int e = 0; e < 2; e++) { var eg = e == 0 ? a.eyeL : a.eyeR; if (math.distance(eg.World(eg.org + (float3)eg.dims * eg.step * 0.5f), hit) < 3.5f) TryDetach(System.Array.IndexOf(all, eg)); }
                if (sim.Eff(AnatZone.Mouth) > 30)
                    for (int t = 0; t < a.teeth.Length; t++) { var tg = a.teeth[t]; if (math.distance(tg.World(tg.org + (float3)tg.dims * tg.step * 0.5f), hit) < 1.6f) TryDetach(System.Array.IndexOf(all, tg)); }
            }
            removed += sim.removed - before;
            double t0 = sw.Elapsed.TotalMilliseconds; tRest = t0 - tSim - tBore;
            AnatProf.Lap(AnatProf.Rest);
            Flush(hitChunks);
            AnatProf.Lap(AnatProf.Mesh);
            tMesh = sw.Elapsed.TotalMilliseconds - t0;
            lastMs = sw.Elapsed.TotalMilliseconds; lastWhat = "удар " + s.name; lastRemoved = removed;
            return removed;
        }

        /// Кусок мозга: ячейки мозга в неровном шаре вокруг e.c становятся отдельным мягким телом и вылетают из пролома.
        void Brainout(AnatDamage.Eject e)
        {
            var h = a.head; var L = new NativeList<int>(512, Allocator.Temp);
            int3 lo = math.max((int3)math.floor((e.c - e.r * 1.3f - h.org) / h.step), 0), hi = math.min((int3)math.floor((e.c + e.r * 1.3f - h.org) / h.step), h.dims - 1);
            for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
            {
                int i = h.Index(x, y, z); if (!AnatDamage.Brainy(h.mat[i])) continue;
                float3 p = h.Local(x, y, z); float3 d = p - e.c; if (AnatDamage.Protected(p)) continue;
                float rn = e.r * (1 + 0.25f * math.sin(d.x * 4.1f + d.y * 2.3f) * math.sin(d.z * 3.7f - d.y * 1.9f));
                if (math.length(d) <= rn) L.Add(i);
            }
            if (L.Length >= MinPiece)
            {
                var pc = pieces.Make(h, sim.st, L.AsArray(), 0, L.Length, e.dir * 140 + new float3(0, 60, 0), "мозг");
                foreach (int i in L) { int x = i % h.dims.x, y = (i / h.dims.x) % h.dims.y, z = i / (h.dims.x * h.dims.y); sim.Cut(x, y, z); }
                if (pc != null) Budget(pc);
            }
            L.Dispose();
        }

        static bool NearOpen(AnatGrid g, int3 c)
        {
            for (int d = 0; d < 6; d++)
            {
                int3 n = c + new int3(d == 0 ? 1 : d == 1 ? -1 : 0, d == 2 ? 1 : d == 3 ? -1 : 0, d == 4 ? 1 : d == 5 ? -1 : 0);
                if (math.any(n < 0) || math.any(n >= g.dims)) continue;
                if (g.mat[g.Index(n.x, n.y, n.z)] == AnatMat.Empty) return true;
            }
            return false;
        }

        /// Ячейки решётки g в капсуле [p0, p1] радиуса r (p0, p1 — см головы) → scanI (индекс) и scanQ (центр в см головы).
        void Scan(AnatGrid g, float3 p0, float3 p1, float r)
        {
            scanI.Clear(); scanQ.Clear();
            var ir = math.transpose(g.rot);   // rot — поворот, обратный — транспонированный
            float3 l0 = math.mul(ir, p0 - g.pos), l1 = math.mul(ir, p1 - g.pos);
            float3 lo = math.min(l0, l1) - r, hi = math.max(l0, l1) + r;
            int3 c0 = math.max((int3)math.floor((lo - g.org) / g.step), 0), c1 = math.min((int3)math.floor((hi - g.org) / g.step), g.dims - 1);
            if (math.any(c0 > c1)) return;
            new ScanJob { c0 = c0, c1 = c1, dims = g.dims, org = g.org, step = g.step, pos = g.pos, rot = g.rot, p0 = p0, p1 = p1, r = r, outI = scanI, outQ = scanQ }.Run();
        }

        /// Часть 4: обход коробки капсулы в Burst (раньше — управляемый цикл). Порядок z, y, x и арифметика те же
        /// (Strict: без слияния умножения со сложением), поэтому список ячеек тот же.
        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        struct ScanJob : IJob
        {
            public int3 c0, c1, dims; public float3 org, pos, p0, p1; public float3x3 rot; public float step, r;
            public NativeList<int> outI; public NativeList<float3> outQ;
            public void Execute()
            {
                for (int z = c0.z; z <= c1.z; z++) for (int y = c0.y; y <= c1.y; y++) for (int x = c0.x; x <= c1.x; x++)
                {
                    float3 q = pos + math.mul(rot, org + (new float3(x, y, z) + 0.5f) * step);
                    if (AnatGeo.SegDist(q, p0, p1) <= r) { outI.Add(x + dims.x * (y + dims.y * z)); outQ.Add(q); }
                }
            }
        }

        /// Совпадает ли текущее состояние решёток с исходным (для тестов).
        /// Волосы без опоры после удара или отрыва куска уходят вместе с кожей под ними (AnatLooks.Settle), а не висят. Выключатель — для
        /// сравнения с прежним поведением (seed 77 в эталоне снят без него).
        public static bool HairFall = true;
        public int hairDropped;
        readonly List<int> hairCut = new List<int>();
        int DropHair()
        {
            int n = AnatLooks.Layers(a.look.hair); if (n == 0 || sim.cutLog.Length == 0) return 0;
            var h = a.head; int3 mn = int.MaxValue, mx = int.MinValue;
            for (int k = 0; k < sim.cutLog.Length; k++) { int i = sim.cutLog[k]; int3 c = new int3(i % h.dims.x, (i / h.dims.x) % h.dims.y, i / (h.dims.x * h.dims.y)); mn = math.min(mn, c); mx = math.max(mx, c); }
            hairCut.Clear();
            AnatLooks.Settle(h, mn - n - 1, mx + n + 1, hairCut);
            foreach (var i in hairCut) sim.Cut(i % h.dims.x, (i / h.dims.x) % h.dims.y, i / (h.dims.x * h.dims.y));
            hairDropped += hairCut.Count;
            return hairCut.Count;
        }

        public int DiffFromPristine()
        {
            int diff = 0;
            for (int k = 0; k < grids.Length; k++) { var g = grids[k]; var p = pristine[k]; for (int i = 0; i < p.Length; i++) if (g.mat[i] != p[i]) diff++; }
            return diff;
        }

        public void Dispose()
        {
            MeshDone();
            if (wp != null) { pieces.Drop(wp); wp = null; }
            pieces?.Clear(); isl?.Dispose(); if (wtmp.IsCreated) wtmp.Dispose(); if (scanI.IsCreated) scanI.Dispose(); if (scanQ.IsCreated) scanQ.Dispose();
            sim?.Dispose(); sim = null; drops?.Destroy(); bones?.Dispose();
            foreach (var b in bodies) b.Destroy();
            bodies.Clear();
            if (root != null) Object.DestroyImmediate(root.gameObject);
            a.Dispose();
            if (layout.IsCreated) layout.Dispose();
        }
    }
}
