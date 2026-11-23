using System.Collections.Generic;
using Unity.Collections;
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
        AnatBloodFx drops;
        readonly AnatBody headBody; readonly int headK;
        readonly Material material;

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

        public AnatView(Transform parent, Vector3 center, Material material)
        {
            AnatPalette.Apply();
            this.material = material;
            a = AnatomyBuilder.Build();
            a.vis.Warm();
            layout = new NativeArray<VertexAttributeDescriptor>(AnatMesher.Layout, Allocator.Persistent);
            root = new GameObject("anat").transform;
            root.SetParent(parent, false); root.position = center;
            int id = 0;
            var gl = new List<AnatGrid>();
            foreach (var g in a.All) { bodies.Add(new AnatBody(g, root, material, id++)); gl.Add(g); pristine.Add(g.mat.ToArray()); dmg.Add(g.mat.ToArray()); }
            grids = gl.ToArray();
            headK = gl.IndexOf(a.head); headBody = bodies[headK];
            NewSim();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var b in bodies) b.BuildAll(layout);
            lastMs = sw.Elapsed.TotalMilliseconds;
        }

        public int Triangles { get { int t = 0; foreach (var b in bodies) t += b.Triangles; return t; } }
        public int Chunks { get { int c = 0; foreach (var b in bodies) c += b.ChunkCount; return c; } }
        public int ChunksDrawn { get { int c = 0; foreach (var b in bodies) for (int i = 0; i < b.ChunkCount; i++) if (b.quads[i] > 0) c++; return c; } }

        void NewSim()
        {
            sim?.Dispose();
            sim = new AnatDamage(a);
            var dm = dmg[headK];
            sim.onMat = (i, m) => dm[i] = m;
            headBody.st = sim.st;
            drops?.Destroy(); drops = new AnatBloodFx(root, material, layout);
            Shader.SetGlobalFloat("_AnatTime", 0);
        }

        /// Время боя: раны кровят, подтёки бегут, капли летят, отёк растёт, синяки меняют цвет; перестройка грязных чанков
        /// с бюджетом (не больше maxChunks за кадр).
        public void Tick(float dt, int maxChunks = 12)
        {
            sim.grav = Down();
            sim.Step(dt);
            Shader.SetGlobalFloat("_AnatTime", sim.time);
            drops.Update(sim.drops, sim.time);
            Flush(maxChunks);
        }

        /// Тяжесть в рамке покоя головы: мировой низ, повёрнутый обратно поворотом черепа (всё выше C1 движется с ним жёстко).
        /// Кровь на шее стекает по той же оси — у якоря это отступление меньше угла изгиба шеи.
        public static float3 Down()
        {
            if (!AnatSkin.on) return new float3(0, -1, 0);
            AnatSkin.Skin(new float3(0, 4, 0), out float3x3 M);
            return math.normalizesafe(math.mul(math.transpose(M), new float3(0, -1, 0)), new float3(0, -1, 0));
        }

        void Flush(int maxChunks)
        {
            for (int i = 0; i < sim.dirty.Length; i++) if (sim.dirty[i]) { sim.dirty[i] = false; headBody.MarkDirty(i); }
            foreach (var b in bodies) b.SyncVis();
            for (int guard = 0; guard < 64; guard++)
            {
                bool any = false;
                foreach (var b in bodies) if (b.dirtyCount > 0) { b.Schedule(layout, maxChunks); any = true; }
                foreach (var b in bodies) b.Complete();
                if (!any || maxChunks < 1000) break;
            }
        }

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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int before = sim.removed;
            sim.Hit(hit, dir, s.E, s.power);
            int removed = 0;
            if (s.bore)
            {
                byte entry = a.Sample(hit + dir * 0.05f, out _, out _);
                bool deep = Brain(entry);
                System.Func<byte, bool> keep = m => !deep && Brain(m);
                float3 p0 = hit - dir * (s.r * 0.6f), p1 = hit + dir * s.depth;
                float stop = float.MaxValue;
                foreach (var g in grids) Scan(g, p0, p1, s.r, (i, q) => { if (keep(g.mat[i])) stop = math.min(stop, math.dot(q - p0, dir)); });
                var h = a.head;
                for (int k = 0; k < grids.Length; k++)
                {
                    var g = grids[k]; var dm = dmg[k]; bool isHead = g == h;
                    int3 mn = int.MaxValue, mx = int.MinValue;
                    Scan(g, p0, p1, s.r, (i, q) =>
                    {
                        byte m = g.mat[i];
                        if (m == AnatMat.Empty || m == AnatMat.Covered || keep(m) || math.dot(q - p0, dir) >= stop) return;
                        int3 c = new int3(i % g.dims.x, (i / g.dims.x) % g.dims.y, i / (g.dims.x * g.dims.y));
                        if (isHead) sim.Cut(c.x, c.y, c.z);
                        else { g.mat[i] = AnatMat.Empty; dm[i] = AnatMat.Empty; removed++; mn = math.min(mn, c); mx = math.max(mx, c); }
                    });
                    if (!isHead && math.all(mn <= mx)) g.Touch(mn, mx);
                }
                // ликвор в проломе стекает: пустеют подоболочечное пространство и борозды до 1 см вокруг канала
                for (int pass = 0; pass < 6; pass++)
                    Scan(h, p0, p1 + dir * 1.0f, s.r + 1.0f, (i, q) =>
                    {
                        if (h.mat[i] != AnatMat.Csf) return;
                        int3 c = new int3(i % h.dims.x, (i / h.dims.x) % h.dims.y, i / (h.dims.x * h.dims.y));
                        if (NearOpen(h, c)) sim.Cut(c.x, c.y, c.z);
                    });
                sim.Flush();
            }
            removed += sim.removed - before;
            Flush(4096);
            lastMs = sw.Elapsed.TotalMilliseconds; lastWhat = "удар " + s.name; lastRemoved = removed;
            return removed;
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

        /// Ячейки решётки g в капсуле [p0, p1] радиуса r (p0, p1 — см головы); f(индекс, центр в см головы).
        static void Scan(AnatGrid g, float3 p0, float3 p1, float r, System.Action<int, float3> f)
        {
            var ir = math.transpose(g.rot);   // rot — поворот, обратный — транспонированный
            float3 l0 = math.mul(ir, p0 - g.pos), l1 = math.mul(ir, p1 - g.pos);
            float3 lo = math.min(l0, l1) - r, hi = math.max(l0, l1) + r;
            int3 c0 = math.max((int3)math.floor((lo - g.org) / g.step), 0), c1 = math.min((int3)math.floor((hi - g.org) / g.step), g.dims - 1);
            for (int z = c0.z; z <= c1.z; z++) for (int y = c0.y; y <= c1.y; y++) for (int x = c0.x; x <= c1.x; x++)
            {
                float3 q = g.World(g.Local(x, y, z));
                if (AnatGeo.SegDist(q, p0, p1) <= r) f(g.Index(x, y, z), q);
            }
        }

        /// Совпадает ли текущее состояние решёток с исходным (для тестов).
        public int DiffFromPristine()
        {
            int diff = 0;
            for (int k = 0; k < grids.Length; k++) { var g = grids[k]; var p = pristine[k]; for (int i = 0; i < p.Length; i++) if (g.mat[i] != p[i]) diff++; }
            return diff;
        }

        public void Dispose()
        {
            sim?.Dispose(); sim = null; drops?.Destroy();
            foreach (var b in bodies) b.Destroy();
            bodies.Clear();
            if (root != null) Object.DestroyImmediate(root.gameObject);
            a.Dispose();
            if (layout.IsCreated) layout.Dispose();
        }
    }
}
