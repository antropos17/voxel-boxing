using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static VoxProbe.AnatGeo;

namespace VoxProbe
{
    /// Сосуд — отрезок трубки. band: 0 глубокий (весь объём трубки), 1 под кожей (слой сразу под кожей вдоль пути), 2 в твёрдой оболочке.
    public struct Vessel { public float3 a, b; public float r; public byte part, mat, band; }

    /// Решётка одного тела: материал, часть (кость, мышца, сосуд; бит 7 — шов), зона. Координаты в см в рамке головы:
    /// точка ячейки = pos + rot·(org + (i + 0,5)·step).
    public class AnatGrid
    {
        public string name;
        public NativeArray<byte> mat, part, zone;
        public int3 dims;
        public float step;
        public float3 org, pos;
        public float3x3 rot = float3x3.identity;
        public int Count => dims.x * dims.y * dims.z;

        public AnatGrid(string name, int3 dims, float step, float3 org)
        {
            this.name = name; this.dims = dims; this.step = step; this.org = org;
            int n = Count;
            mat = new NativeArray<byte>(n, Allocator.Persistent);
            part = new NativeArray<byte>(n, Allocator.Persistent);
            zone = new NativeArray<byte>(n, Allocator.Persistent);
        }
        public int Index(int x, int y, int z) => x + dims.x * (y + dims.y * z);
        public float3 Local(int x, int y, int z) => org + (new float3(x, y, z) + 0.5f) * step;
        public float3 World(float3 local) => pos + math.mul(rot, local);
        /// Ячейка по точке в рамке головы (−1, если вне решётки).
        public int Lookup(float3 p)
        {
            float3 l = math.mul(math.transpose(rot), p - pos);
            int3 c = (int3)math.floor((l - org) / step);
            if (math.any(c < 0) || math.any(c >= dims)) return -1;
            return Index(c.x, c.y, c.z);
        }
        public void Dispose() { if (mat.IsCreated) { mat.Dispose(); part.Dispose(); zone.Dispose(); } }
    }

    /// Анатомическая голова: голова (кожа, мышцы, череп, мозг, полости), нижняя челюсть, два глаза, 32 зуба.
    public class AnatHead
    {
        public AnatGrid head, mand, eyeL, eyeR;
        public AnatGrid[] teeth;
        public ToothDef[] toothDefs;
        public readonly Dictionary<string, double> ms = new Dictionary<string, double>();
        public IEnumerable<AnatGrid> All { get { yield return head; yield return mand; yield return eyeL; yield return eyeR; foreach (var t in teeth) yield return t; } }

        /// Материал и часть в точке рамки головы (порядок: зубы, глаза, челюсть, голова). Для срезов и тестов.
        public byte Sample(float3 p, out byte part, out AnatGrid from)
        {
            for (int i = 0; i < teeth.Length; i++)
            {
                var d = p - toothDefs[i].c;
                if (math.lengthsq(d) > 9f) continue;
                int k = teeth[i].Lookup(p);
                if (k >= 0 && teeth[i].mat[k] != 0) { part = teeth[i].part[k]; from = teeth[i]; return teeth[i].mat[k]; }
            }
            foreach (var g in new[] { eyeL, eyeR, mand })
            {
                int k = g.Lookup(p);
                if (k >= 0 && g.mat[k] != 0) { part = g.part[k]; from = g; return g.mat[k]; }
            }
            int h = head.Lookup(p);
            if (h >= 0) { var m = head.mat[h]; if (m != AnatMat.Covered) { part = head.part[h]; from = head; return m; } }
            part = 0; from = null; return 0;
        }

        public void Dispose() { foreach (var g in All) g?.Dispose(); }
    }

    public static class AnatomyBuilder
    {
        // нижняя челюсть: решётка выровнена с решёткой головы (тот же шаг, сдвиг на целое число ячеек)
        public static readonly int3 MandOff = new int3(17, 5, 51), MandDims = new int3(61, 57, 52);

        public static Vessel[] Vessels()
        {
            var v = new List<Vessel>();
            void Add(float3 a, float3 b, float r, byte part, byte mat, byte band) => v.Add(new Vessel { a = a, b = b, r = r, part = part, mat = mat, band = band });
            // поверхностная височная артерия: от козелка вверх, лобная и теменная ветви
            Add(new float3(6.6f, -0.6f, 1.3f), new float3(6.6f, 4.5f, 1.6f), 0.55f, AnatPart.SupTemporalA, AnatMat.Artery, 1);
            Add(new float3(6.6f, 4.5f, 1.6f), new float3(5.8f, 6.8f, 4.5f), 0.5f, AnatPart.SupTemporalA, AnatMat.Artery, 1);
            Add(new float3(6.6f, 4.5f, 1.6f), new float3(6.8f, 8.0f, -0.5f), 0.5f, AnatPart.SupTemporalA, AnatMat.Artery, 1);
            // лицевая артерия: от края челюсти у жевательной мышцы к углу рта и к углу глаза
            Add(new float3(3.9f, -9.0f, 4.6f), new float3(2.6f, -5.6f, 7.6f), 0.55f, AnatPart.FacialA, AnatMat.Artery, 1);
            Add(new float3(2.6f, -5.6f, 7.6f), new float3(1.4f, -1.5f, 8.6f), 0.5f, AnatPart.FacialA, AnatMat.Artery, 1);
            Add(new float3(1.4f, -1.5f, 8.6f), new float3(1.2f, 1.2f, 8.2f), 0.45f, AnatPart.FacialA, AnatMat.Artery, 1);
            // вены кожи головы (теменная и затылочная), там же, под кожей
            Add(new float3(2.5f, 12.0f, 3.0f), new float3(4.5f, 10.0f, -4.0f), 0.5f, AnatPart.ScalpV, AnatMat.Vein, 1);
            Add(new float3(3.0f, 6.0f, -9.0f), new float3(5.5f, 0.5f, -7.5f), 0.5f, AnatPart.ScalpV, AnatMat.Vein, 1);
            // сонная артерия и яремная вена
            Add(new float3(1.9f, -11.95f, 0.6f), new float3(2.0f, -4.0f, 0.4f), 0.3f, AnatPart.CarotidA, AnatMat.Artery, 0);
            Add(new float3(2.0f, -4.0f, 0.4f), new float3(1.8f, -1.2f, 0.6f), 0.28f, AnatPart.CarotidA, AnatMat.Artery, 0);
            Add(new float3(2.3f, -11.95f, -0.2f), new float3(2.6f, -3.0f, -1.2f), 0.35f, AnatPart.JugularV, AnatMat.Vein, 0);
            // средняя менингеальная артерия: от остистого отверстия вверх по внутренней стороне виска
            Add(new float3(3.8f, 0.8f, 0.8f), new float3(5.4f, 2.0f, 1.2f), 0.3f, AnatPart.MiddleMeningealA, AnatMat.Artery, 2);
            Add(new float3(5.4f, 2.0f, 1.2f), new float3(5.8f, 4.0f, 1.8f), 0.3f, AnatPart.MiddleMeningealA, AnatMat.Artery, 2);
            Add(new float3(5.8f, 4.0f, 1.8f), new float3(5.8f, 7.0f, 2.6f), 0.3f, AnatPart.MiddleMeningealA, AnatMat.Artery, 2);
            Add(new float3(5.4f, 2.0f, 1.2f), new float3(5.6f, 4.0f, -1.5f), 0.3f, AnatPart.MiddleMeningealA, AnatMat.Artery, 2);
            return v.ToArray();
        }

        public static AnatHead Build()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = new AnatHead();
            var head = r.head = new AnatGrid("head", Dims, S, Org);
            int n = head.Count;
            var occ = new NativeArray<byte>(n, Allocator.TempJob);
            var dmask = new NativeArray<byte>(n, Allocator.TempJob);
            var dist = new NativeArray<ushort>(n, Allocator.TempJob);

            r.toothDefs = MakeTeeth();
            var teeth = new NativeArray<ToothDef>(r.toothDefs, Allocator.TempJob);
            MakeMandArch(out var archA, out var thickA);
            var arch = new NativeArray<float4>(archA, Allocator.TempJob);
            var thick = new NativeArray<float>(thickA, Allocator.TempJob);
            var anchors = new NativeArray<float>(Anchors, Allocator.TempJob);
            var vessels = new NativeArray<Vessel>(Vessels(), Allocator.TempJob);

            var h = new OccJob { occ = occ, dmask = dmask, dims = head.dims, org = head.org, s = S }.Schedule(head.dims.z, 1);
            h = new HeadBuilder.ChamferJob { vox = dmask, dist = dist, dims = head.dims }.Schedule(h);
            h.Complete(); r.ms["head.occ+chamfer"] = sw.Elapsed.TotalMilliseconds; sw.Restart();
            new ClassJob { occ = occ, dist = dist, mat = head.mat, part = head.part, zone = head.zone, dims = head.dims, org = head.org, s = S,
                teeth = teeth, arch = arch, thick = thick, anchors = anchors, vessels = vessels }.Schedule(head.dims.z, 1).Complete();
            r.ms["head.classify"] = sw.Elapsed.TotalMilliseconds; sw.Restart();
            var src = new NativeArray<byte>(head.mat, Allocator.TempJob);
            new SealJob { src = src, mat = head.mat, part = head.part, dims = head.dims }.Schedule(head.dims.z, 1).Complete();
            src.Dispose();
            new BoneJob { mat = head.mat, part = head.part, dims = head.dims }.Schedule(head.dims.z, 1).Complete();
            new WrapJob { mat = head.mat, part = head.part, dims = head.dims }.Schedule(head.dims.z, 1).Complete();
            new HairJob { mat = head.mat, part = head.part, zone = head.zone, dims = head.dims, org = head.org, s = S }.Schedule(head.dims.z, 1).Complete();
            r.ms["head.bone+hair"] = sw.Elapsed.TotalMilliseconds; sw.Restart();

            // нижняя челюсть
            var mand = r.mand = new AnatGrid("mandible", MandDims, S, Org + (float3)MandOff * S);
            int mn = mand.Count;
            var mmask = new NativeArray<byte>(mn, Allocator.TempJob);
            var mdist = new NativeArray<ushort>(mn, Allocator.TempJob);
            var mh = new MandMaskJob { mask = mmask, hdist = dist, hdims = head.dims, off = MandOff, dims = mand.dims, org = mand.org, s = S, arch = arch, thick = thick }.Schedule(mand.dims.z, 1);
            mh = new HeadBuilder.ChamferJob { vox = mmask, dist = mdist, dims = mand.dims }.Schedule(mh);
            mh = new MandClassJob { mask = mmask, dist = mdist, mat = mand.mat, part = mand.part, zone = mand.zone, dims = mand.dims, org = mand.org, s = S, teeth = teeth }.Schedule(mand.dims.z, 1, mh);
            mh.Complete();
            new BoneJob { mat = mand.mat, part = mand.part, dims = mand.dims }.Schedule(mand.dims.z, 1).Complete();
            r.ms["mandible"] = sw.Elapsed.TotalMilliseconds; sw.Restart();

            // глаза: шаг 1 мм, центр шара в (±3,2; 2,3; 6,2)
            r.eyeL = MakeEye("eyeL", +1); r.eyeR = MakeEye("eyeR", -1);
            r.ms["eyes"] = sw.Elapsed.TotalMilliseconds; sw.Restart();
            // зубы: шаг 1 мм в осях зуба
            r.teeth = new AnatGrid[32];
            for (int i = 0; i < 32; i++) r.teeth[i] = MakeTooth(r.toothDefs[i]);
            r.ms["teeth"] = sw.Elapsed.TotalMilliseconds;

            occ.Dispose(); dmask.Dispose(); dist.Dispose(); mmask.Dispose(); mdist.Dispose();
            teeth.Dispose(); arch.Dispose(); thick.Dispose(); anchors.Dispose(); vessels.Dispose();
            double total = 0; foreach (var kv in r.ms) total += kv.Value;
            r.ms["total"] = total;
            return r;
        }

        // ---------- глаз ----------
        public const float EyeStep = 0.1f;
        static AnatGrid MakeEye(string name, float side)
        {
            var g = new AnatGrid(name, new int3(26, 26, 30), EyeStep, new float3(-1.3f, -1.3f, -1.7f));
            g.pos = new float3(side * EX, EY, EZ);
            new EyeJob { mat = g.mat, part = g.part, zone = g.zone, dims = g.dims, org = g.org, s = EyeStep, side = side }.Schedule(g.dims.z, 1).Complete();
            return g;
        }

        [BurstCompile(CompileSynchronously = true)]
        struct EyeJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<byte> mat, part, zone;
            public int3 dims; public float3 org; public float s, side;
            public void Execute(int z)
            {
                for (int y = 0; y < dims.y; y++)
                for (int x = 0; x < dims.x; x++)
                {
                    int i = x + dims.x * (y + dims.y * z);
                    float3 p = org + (new float3(x, y, z) + 0.5f) * s;
                    float r = math.length(p), rc = math.length(p - new float3(0, 0, 0.48f));
                    byte m = 0;
                    bool cornea = p.z > 0.75f && rc < 0.78f;
                    if (r < ER || cornea)
                    {
                        float r2 = math.length(p.xy);
                        if (cornea && rc > 0.68f) m = AnatMat.Cornea;
                        else if (!cornea && r > ER - 0.1f) m = AnatMat.Sclera;
                        else if (p.z > 0.82f && p.z < 0.95f && r2 < 0.62f) m = r2 < 0.2f ? AnatMat.Pupil : AnatMat.Iris;
                        else if (p.z > 0.82f) m = AnatMat.Vitreous; // водянистая влага передней камеры (отдельного материала нет)
                        else if (AnatGeo.InEll(p.x, p.y, p.z - 0.6f, 0.45f, 0.45f, 0.2f)) m = AnatMat.Lens;
                        else m = AnatMat.Vitreous;
                    }
                    else if (AnatGeo.SdCap(p, new float3(-0.12f * side, 0, -1.05f), new float3(-0.2f * side, 0, -1.7f), 0.2f, 0.2f) < 0) m = AnatMat.Nerve;
                    mat[i] = m; part[i] = 0; zone[i] = m != 0 ? AnatZone.Eye : (byte)0;
                }
            }
        }

        // ---------- зуб ----------
        public const float ToothStep = 0.1f;
        static AnatGrid MakeTooth(ToothDef t)
        {
            float hw = t.mw * 0.5f + 0.15f, hb = t.bl * 0.5f + 0.15f;
            var lo = new float3(-hw, -hb, -t.hc - 0.1f);
            var hi = new float3(hw, hb, t.hr + 0.1f);
            var dims = (int3)math.ceil((hi - lo) / ToothStep);
            var g = new AnatGrid("tooth" + t.fdi, dims, ToothStep, lo);
            g.pos = t.c; g.rot = new float3x3(t.U, t.V, t.W);
            for (int z = 0; z < dims.z; z++)
            for (int y = 0; y < dims.y; y++)
            for (int x = 0; x < dims.x; x++)
            {
                float3 q = g.Local(x, y, z);
                float d = ToothSdf(t, q);
                byte m = 0;
                if (d < 0)
                {
                    float hw2 = t.mw * 0.5f, hb2 = t.bl * 0.5f;
                    bool pulp = q.z > -t.hc * 0.45f && q.z < t.hr * 0.25f && AnatGeo.SdEll2(q.x, q.y, hw2 * 0.36f, hb2 * 0.36f) < 0;
                    if (!pulp && q.z >= 0 && q.z < t.hr - 0.08f)
                    {
                        if (t.roots == 1) pulp = math.length(q.xy) < 0.07f;
                        else for (int k = 0; k < t.roots; k++)
                        {
                            float2 o; float u = math.saturate(q.z / t.hr), spread = 1 + 0.4f * u;
                            if (t.roots == 2) o = t.upper == 1 ? new float2(0, (k == 0 ? 0.22f : -0.22f) * t.bl) : new float2((k == 0 ? -0.25f : 0.25f) * t.mw, 0);
                            else o = k == 0 ? new float2(-0.22f * t.mw, 0.2f * t.bl) : k == 1 ? new float2(0.22f * t.mw, 0.2f * t.bl) : new float2(0, -0.25f * t.bl);
                            if (math.length(q.xy - o * spread) < 0.07f) pulp = true;
                        }
                    }
                    if (pulp) m = AnatMat.Pulp;
                    else if (q.z < 0 && d > -0.1f) m = AnatMat.Enamel;
                    else m = AnatMat.Dentin;
                }
                int i = g.Index(x, y, z);
                g.mat[i] = m; g.part[i] = (byte)t.fdi; g.zone[i] = m != 0 ? AnatZone.Mouth : (byte)0;
            }
            return g;
        }

        // ---------- голова ----------
        [BurstCompile(CompileSynchronously = true)]
        struct OccJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<byte> occ, dmask;
            public int3 dims; public float3 org; public float s;
            public void Execute(int z)
            {
                float Z = org.z + (z + 0.5f) * s;
                for (int y = 0; y < dims.y; y++)
                {
                    float Y = org.y + (y + 0.5f) * s;
                    int i = dims.x * (y + dims.y * z);
                    for (int x = 0; x < dims.x; x++, i++)
                    {
                        float X = org.x + (x + 0.5f) * s;
                        bool real = HeadSdf(X, Y, Z) <= 0 || InTongue(X, Y, Z);
                        occ[i] = (byte)(real ? 1 : 0);
                        // для глубины покровов рот заполнен: глубина считается от наружной кожи
                        dmask[i] = (byte)(real || MouthCav(X, Y, Z) < 0.3f ? 1 : 0);
                    }
                }
            }
        }

        public struct Layers { public float b0, b1, b2, b3, b4, b5, b6; public byte mus, kind; }

        /// Толщины покровов в точке: гауссовы веса опорных точек раздела 3. Порядок слоёв: кожа, жир, фасция, мышца, рыхлый слой, надкостница, кость.
        /// Тонкий слой, если он есть (больше 0,4 мм), утолщается до одного вокселя (2 мм); толщины округляются до целого числа вокселей.
        public static Layers Stack(NativeArray<float> an, float ax, float y, float z)
        {
            float sw = 0.002f, best = 0;
            float t0 = 1.6f * sw, t1 = 5f * sw, t2 = 1f * sw, t3 = 0, t4 = 2f * sw, t5 = .5f * sw, t6 = 6f * sw;
            int dom = -1;
            int na = an.Length / AnchorCols;
            for (int k = 0; k < na; k++)
            {
                int o = k * AnchorCols;
                float w = Gs(ax - an[o], y - an[o + 1], z - an[o + 2], an[o + 3]);
                if (w < 1e-4f) continue;
                sw += w;
                t0 += w * an[o + 4]; t1 += w * an[o + 5]; t2 += w * an[o + 6]; t3 += w * an[o + 7]; t4 += w * an[o + 8]; t5 += w * an[o + 9]; t6 += w * an[o + 10];
                if (w > best) { best = w; dom = k; }
            }
            t0 /= sw; t1 /= sw; t2 /= sw; t3 /= sw; t4 /= sw; t5 /= sw; t6 /= sw;
            var L = new Layers();
            L.mus = dom >= 0 ? (byte)an[dom * AnchorCols + 11] : (byte)0;
            L.kind = dom >= 0 ? (byte)an[dom * AnchorCols + 12] : (byte)1;
            // толщина в см, кратная шагу 0,2 см: центры ячеек лежат на глубинах 0,1; 0,3; …, и слой в n шагов получает ровно n ячеек по нормали
            // округляется граница (накопленная глубина), а не каждая толщина: у соседних ячеек границы расходятся не больше чем на шаг
            float Th(float t) => t < 0.4f ? 0 : math.max(t, 2f) * 0.1f;
            float fat = Th(t1), fas = Th(t2), mus = Th(t4), loose = Th(t3);
            float per = L.kind == 0 ? 0 : Th(t5), bone = L.kind == 0 ? 0 : Th(t6);
            if (L.kind == 3) bone = math.max(bone, 0.2f);
            float acc = 0.2f, prev = 0.2f;
            float B(float th) { if (th <= 0) return prev; acc += th; prev = math.max(prev + 0.2f, math.round(acc / 0.2f) * 0.2f); return prev; }
            L.b0 = 0.2f; L.b1 = B(fat); L.b2 = B(fas); L.b3 = B(mus); L.b4 = B(loose); L.b5 = B(per); L.b6 = B(bone);
            return L;
        }

        [BurstCompile(CompileSynchronously = true)]
        struct ClassJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> occ;
            [ReadOnly] public NativeArray<ushort> dist;
            [NativeDisableParallelForRestriction] public NativeArray<byte> mat, part, zone;
            [ReadOnly] public NativeArray<ToothDef> teeth;
            [ReadOnly] public NativeArray<float4> arch;
            [ReadOnly] public NativeArray<float> thick, anchors;
            [ReadOnly] public NativeArray<Vessel> vessels;
            public int3 dims; public float3 org; public float s;

            public void Execute(int z)
            {
                float Z = org.z + (z + 0.5f) * s;
                for (int y = 0; y < dims.y; y++)
                {
                    float Y = org.y + (y + 0.5f) * s;
                    int i = dims.x * (y + dims.y * z);
                    for (int x = 0; x < dims.x; x++, i++)
                    {
                        float X = org.x + (x + 0.5f) * s;
                        byte m = 0, p = 0, zn = 0;
                        if (occ[i] != 0)
                        {
                            float depth = math.max(0, (dist[i] / 3f - 0.5f) * s);
                            Cell(X, Y, Z, depth, new int3(x, y, z), out m, out p);
                            zn = m != 0 ? ZoneAt(X, Y, Z) : (byte)0;
                        }
                        mat[i] = m; part[i] = p; zone[i] = zn;
                    }
                }
            }

            void Cell(float x, float y, float z, float depth, int3 c, out byte m, out byte p)
            {
                m = 0; p = 0;
                float ax = math.abs(x);
                float3 q = new float3(ax, y, z), pp = new float3(x, y, z);
                float ex = ax - EX, ey = y - EY, ez = z - EZ, er = math.sqrt(ex * ex + ey * ey + ez * ez);
                if (er < ER) { m = AnatMat.Covered; p = 1; return; }                       // глазное яблоко — отдельное тело
                if (depth >= 0.4f && Mandible(ax, y, z, arch, thick) < 0) { m = AnatMat.Covered; p = 2; return; } // нижняя челюсть — отдельное тело
                float tsd = 9; int ti = -1; float3 tq = 0;
                if (y > -7.8f && y < -2.6f && ax < 3.8f && z > 2.2f && z < 9.4f) tsd = TeethSdf(teeth, 0, 32, pp, out tq, out ti);
                if (tsd < 0) { m = AnatMat.Covered; p = 3; return; }                       // зуб в лунке — отдельное тело
                if (InTongue(x, y, z)) { m = AnatMat.Tongue; p = AnatPart.TongueM; return; }

                var L = Stack(anchors, ax, y, z);
                // веки: кожа, круговая мышца, конъюнктива
                if (er < LR + 0.05f && ez > 0.1f && z > 5.2f)
                {
                    if (depth < 0.2f) m = LashZone(ax, y) ? AnatMat.Lash : AnatMat.Skin;
                    else if (er < ER + 0.13f) m = AnatMat.Mucosa;
                    else { m = AnatMat.Muscle; p = AnatPart.OrbOculi; }
                    return;
                }
                // наружная кожа
                if (depth < L.b0)
                {
                    m = LipZone(ax, y, z) ? AnatMat.Lip : BeardZone(x, y, z) ? AnatMat.Stubble : AnatMat.Skin;
                    return;
                }
                // со стороны рта: слизистая, десна, альвеолы, мышцы щеки и губ
                float md = MouthCav(x, y, z);
                bool oral = md < depth && InMouthZone(x, y, z);
                if (oral && md < 0.2f) { m = tsd < 0.35f ? AnatMat.Gum : AnatMat.Mucosa; return; }
                if (ti >= 0 && tsd < 0.6f)
                {
                    var t = teeth[ti];
                    if (tq.z < 0.2f) { if (tsd < 0.3f) { m = AnatMat.Gum; return; } }
                    else if (t.upper == 1) { m = tsd < 0.2f ? AnatMat.Compact : AnatMat.Spongy; p = x >= 0 ? AnatPart.MaxillaL : AnatPart.MaxillaR; return; }
                }
                if (oral && md < 0.9f && y < -3.6f && (ax > 1.9f || z > 7.2f))
                {
                    m = AnatMat.Muscle; p = z > 7.2f && ax < 2.6f ? AnatPart.OrbOris : AnatPart.Buccinator; return;
                }
                // ухо: хрящ под кожей
                float ec = EarCanal(ax, y, z);
                if (ec < 0.2f && depth >= 0.2f) { m = ec < 0 ? AnatMat.Air : AnatMat.Skin; return; }
                if (EarZone(ax, y, z)) { m = AnatMat.Cartilage; return; }
                // сосуды под кожей
                for (int k = 0; k < vessels.Length; k++)
                {
                    var v = vessels[k];
                    if (v.band == 1 && depth < L.b0 + 0.25f && SegDist(q, v.a, v.b) < v.r) { m = v.mat; p = v.part; return; }
                }
                // глазница: нерв, мышцы глаза, жир
                float orb = Orbit(ax, y, z);
                if (orb < 0.35f || (ax < 1.8f && y > 1.4f && y < 2.9f && z > 1.6f && z < 3.2f))
                {
                    byte em = EyeMuscle(q, out bool tendon, out bool nerve);
                    if (nerve) { m = AnatMat.Nerve; return; }
                    if (em != 0 && depth >= L.b1) { m = tendon ? AnatMat.Tendon : AnatMat.Muscle; p = em; return; }
                }
                if (orb < 0 && depth >= L.b1 && z < 7.9f) { m = AnatMat.OrbFat; return; }
                // носовая полость: ходы, слизистая, раковины, перегородка, преддверие
                // полость — глубже каркаса носа (кожа, жир, носовые кости или хрящи по слоям), кроме преддверия (ноздри)
                if (depth >= 0.2f && Vestibule(ax, y, z) < 0) { m = AnatMat.Air; return; }
                if (depth >= L.b6 && z > 2.9f && z < 10.4f && y > -3.15f && y < NasalRoof(z) + 0.2f && ax < NasalHalf(y) + 0.2f)
                {
                    float N = Nasal(ax, y, z);
                    if (N < 0)
                    {
                        float cc = Concha(ax, y, z);
                        if (cc < -0.12f) { m = AnatMat.Spongy; p = y < -1.4f ? (x >= 0 ? AnatPart.MaxillaL : AnatPart.MaxillaR) : AnatPart.Ethmoid; return; }
                        if (cc < 0 || N > -0.2f)
                        {
                            m = AnatMat.Mucosa;
                            if (ax < 0.45f && math.length(new float3(ax - 0.2f, y + 1.9f, z - 8.6f)) < 0.7f) p = AnatPart.NasalMucosaV;
                            return;
                        }
                        m = AnatMat.Air; return;
                    }
                    float other = math.max(math.max(ax - NasalHalf(y), -2.95f - y), math.max(y - NasalRoof(z), math.max(2.9f - z, z - 10.2f)));
                    if (ax < 0.1f && other < 0)
                    {
                        if (z > 6.9f && y < 2.2f) m = AnatMat.Cartilage;
                        else { m = AnatMat.Compact; p = y < 0.4f ? AnatPart.Vomer : AnatPart.Ethmoid; }
                        return;
                    }
                }
                // пазухи (стенка — кость, приоритет над мозгом); только глубже надкостницы — покровы над пазухой целы
                if (depth >= L.b5)
                {
                    if (EthmoidBox(ax, y, z, out bool edge))
                    {
                        bool wall = edge || c.y % 4 == 0 || c.z % 4 == 0; // ячейки 3×3 вокселя (6 мм) с перегородками в один воксель
                        if (wall) { m = AnatMat.Compact; p = AnatPart.Ethmoid; } else m = AnatMat.Air;
                        return;
                    }
                    float fs = FrontalSinus(ax, y, z), ms = MaxSinus(ax, y, z), ss = SphSinus(x, y, z);
                    float sn = math.min(fs, math.min(ms, ss));
                    if (sn < 0.05f)
                    {
                        bool septum = ss < 0 && ax < 0.1f;
                        if (sn < -0.2f && !septum) m = AnatMat.Air;
                        else { m = AnatMat.Compact; p = fs <= sn ? AnatPart.Frontal : ss <= sn ? AnatPart.Sphenoid : (x >= 0 ? AnatPart.MaxillaL : AnatPart.MaxillaR); }
                        return;
                    }
                }
                // стенки глазницы
                if (orb < 0.22f && depth >= L.b0 + 0.2f && z < 8.2f) { m = AnatMat.Compact; p = BoneAt(x, y, z); return; }
                // мягкое нёбо, глотка
                if (ax < 1.5f && z > 1.7f && z < 3.2f && y > -4.2f && y < -3.35f)
                {
                    m = (y > -3.55f || y < -4.0f) ? AnatMat.Mucosa : AnatMat.Muscle; p = m == AnatMat.Muscle ? AnatPart.SoftPalate : (byte)0; return;
                }
                float ph = Pharynx(x, y, z);
                if (ph < 0.2f && depth >= 0.6f) { m = ph < 0 ? AnatMat.Air : AnatMat.Mucosa; return; }
                // позвоночник и спинной мозг
                if (y < -3.6f && ax < 3.6f && z > -5.4f && z < 0.3f)
                {
                    byte sm = Spine(x, y, z, out byte sp);
                    if (sm != 0) { m = sm; p = sp; return; }
                }
                // полость черепа
                float Lb = BaseLimit(x, y, z);
                if (depth >= L.b6 && Lb < 0)
                {
                    float dB = math.min(depth - L.b6, -Lb);
                    Brain(x, y, z, dB, out m, out p);
                    return;
                }
                if (depth >= L.b6 && Lb < BaseThick(ax, y, z) && y > -4.2f) { m = AnatMat.Spongy; p = BoneAt(x, y, z); return; }
                // скат, пирамида, сосцевидный отросток (ячейки сосцевидного отростка — воздух)
                if (depth >= L.b4 && BaseBone(ax, y, z) < 0)
                {
                    float mst = Mastoid(ax, y, z);
                    if (mst < -0.25f && c.y % 3 != 0 && c.z % 3 != 0 && c.x % 3 != 0) { m = AnatMat.Air; return; }
                    m = AnatMat.Spongy; p = mst < 0 || ax > 1.4f ? (x >= 0 ? AnatPart.TemporalL : AnatPart.TemporalR) : AnatPart.Occipital; return;
                }
                // височно-нижнечелюстной сустав: суставной диск и ямка
                float cd = SdEll(ax - Condyle.x, y - Condyle.y, z - Condyle.z, 0.95f, 0.42f, 0.5f);
                if (cd >= 0 && cd < 0.45f && y > Condyle.y - 0.15f && depth >= 0.4f) { if (cd < 0.2f) m = AnatMat.Cartilage; else { m = AnatMat.Compact; p = x >= 0 ? AnatPart.TemporalL : AnatPart.TemporalR; } return; }
                // глубокие сосуды
                for (int k = 0; k < vessels.Length; k++)
                {
                    var v = vessels[k];
                    if (v.band == 0 && depth >= L.b1 && SdCap(q, v.a, v.b, v.r, v.r) < 0) { m = v.mat; p = v.part; return; }
                }
                // околоушная железа
                if (depth >= L.b1 && SdEll(ax - 5.6f, y + 3f, z - 1f, 0.9f, 1.8f, 1.3f) < 0) { m = AnatMat.Parotid; return; }
                // слои покровов
                if (depth < L.b1) { m = AnatMat.Fat; return; }
                if (depth < L.b2) { m = AnatMat.Fascia; return; }
                if (depth < L.b3) { m = AnatMat.Muscle; p = MuscleAt(ax, y, z, L.mus); return; }
                if (depth < L.b4) { m = AnatMat.Loose; return; }
                if (depth < L.b5) { m = AnatMat.Periost; return; }
                if (depth < L.b6)
                {
                    if (L.kind == 2) { m = AnatMat.Cartilage; return; }
                    m = L.kind == 3 ? AnatMat.Compact : AnatMat.Spongy; p = BoneAt(x, y, z); return;
                }
                // глубже: кости лица, нёбо, полость рта, скуловая дуга, глубокие мышцы
                bool faceBox = z > 3.2f + 0.9f * math.max(0, ax - 2.8f) && y > -3.2f && y < 3.6f && ax < 4.8f;
                if (faceBox) { m = AnatMat.Spongy; p = BoneAt(x, y, z); return; }
                float archIn = ArchX(UpA, UpZ0, z) - 0.25f;
                if (y > -3.55f && y < -2.95f && ax < archIn + 0.2f && z > 2.9f && z < 7.6f)
                {
                    if (y > -3.15f) m = AnatMat.Mucosa;            // дно носа
                    else if (y > -3.35f) { m = AnatMat.Compact; p = z < 4.0f ? AnatPart.Palatine : (x >= 0 ? AnatPart.MaxillaL : AnatPart.MaxillaR); }
                    else m = AnatMat.Mucosa;                         // нёбо со стороны рта
                    return;
                }
                if (y > -5.25f && y <= -3.55f && ax < archIn && z > 3.0f && z < 7.6f) { m = AnatMat.Air; return; } // собственно полость рта
                if (SdCap(q, new float3(4.9f, 0.6f, 4.4f), new float3(6.0f, 0.4f, 0.8f), 0.35f, 0.32f) < 0) { m = AnatMat.Compact; p = x >= 0 ? AnatPart.ZygomaticL : AnatPart.ZygomaticR; return; }
                m = AnatMat.Muscle; p = DeepMuscle(ax, y, z);
            }

            static byte Spine(float x, float y, float z, out byte p)
            {
                p = AnatPart.Vertebra;
                float ax = math.abs(x);
                // позвоночный канал: спинной мозг, ликвор, твёрдая оболочка
                float cn = SdEll2(x, z + 2.6f, 0.75f, 0.55f);
                if (cn < 0)
                {
                    p = 0;
                    if (y > -3.7f) return 0;
                    if (SdEll2(x, z + 2.6f, 0.45f, 0.35f) < 0) return AnatMat.Stem;
                    if (SdEll2(x, z + 2.6f, 0.6f, 0.45f) < 0) return AnatMat.Csf;
                    return AnatMat.Dura;
                }
                // уровни: C1 кольцо, C2 с зубом, C3–C5 тела и диски
                int lvl = -1; float y0 = 0, y1 = 0;
                if (y >= -4.6f && y < -3.8f) { lvl = 1; y0 = -4.6f; y1 = -3.8f; }
                else if (y >= -6.4f && y < -4.6f) { lvl = 2; y0 = -6.4f; y1 = -4.8f; }
                else if (y >= -8.3f && y < -6.4f) { lvl = 3; y0 = -8.1f; y1 = -6.6f; }
                else if (y >= -10f && y < -8.3f) { lvl = 4; y0 = -9.8f; y1 = -8.3f; }
                else if (y < -10f) { lvl = 5; y0 = -11.5f; y1 = -10f; }
                if (lvl < 0) return 0;
                bool inBand = y >= y0 && y < y1;
                float body = SdEll2(x, z + 1.0f, 0.85f, 0.75f);
                if (lvl == 1)
                {
                    if (SdEll2(x, z + 1.1f, 0.45f, 0.45f) < 0) return AnatMat.Compact;                    // зуб C2 внутри кольца атланта
                    if (body < 0 && body > -0.35f && z > -1.3f) return AnatMat.Compact;                     // передняя дуга
                }
                else if (body < 0) return inBand ? AnatMat.Spongy : AnatMat.Cartilage;                     // тело / диск
                if (lvl == 2 && y >= -4.8f && SdEll2(x, z + 1.1f, 0.45f, 0.45f) < 0) return AnatMat.Compact;
                float arch = SdEll2(x, z + 2.6f, 1.15f, 0.95f);
                if (arch < 0 && (z < -1.6f || ax > 0.6f)) return inBand ? AnatMat.Compact : AnatMat.Tendon;   // дуга / жёлтая связка
                float my = (y0 + y1) * 0.5f;
                if (ax < 0.25f && z < -3.4f && z > -4.8f && y < y1 - 0.2f && y > y0 - 0.3f && lvl >= 2) return AnatMat.Compact; // остистый
                float tw = lvl == 1 ? 3.3f : 2.3f;
                if (ax > 1.0f && ax < tw && z > -1.9f && z < -1.0f && math.abs(y - my) < 0.3f) return AnatMat.Compact;      // поперечные
                return 0;
            }

            void Brain(float x, float y, float z, float dB, out byte m, out byte p)
            {
                m = 0; p = 0;
                float ax = math.abs(x);
                float3 q = new float3(ax, y, z);
                if (dB < 0.2f)
                {
                    m = AnatMat.Dura;
                    if (ax < 0.3f && y > 6f && dB < 0.5f) { m = AnatMat.VenSinus; p = AnatPart.SagittalSinus; return; }
                    for (int k = 0; k < vessels.Length; k++)
                    {
                        var v = vessels[k];
                        if (v.band == 2 && SegDist(q, v.a, v.b) < v.r) { m = v.mat; p = v.part; return; }
                    }
                    return;
                }
                float tent = Tent(ax, z);
                bool post = PostFossa(x, y, z) < 0 && y < tent;
                if (!post && z < -4f && math.abs(y - tent) < 0.3f && dB < 0.6f && ax > 1.2f) { m = AnatMat.VenSinus; p = AnatPart.TransverseSinus; return; }
                if (ax < 0.3f && y > 6f && dB < 0.5f) { m = AnatMat.VenSinus; p = AnatPart.SagittalSinus; return; }
                if (dB < 0.4f) { m = AnatMat.Csf; return; }
                float stem = StemCap(x, y, z);
                if (stem < 0) { m = AnatMat.Stem; return; }
                if (math.abs(y - tent) < 0.1f && PostFossa(x, y, z) < 0.4f && stem > 0.35f && z < -1.5f) { m = AnatMat.Dura; return; }  // намёт
                if (ax < 0.1f && y > 4.5f && z > -9f && !post) { m = AnatMat.Dura; return; }                                         // серп
                // желудочки
                if (InEll(ax - 1.0f, y - 5.0f, z + 0.6f, 0.5f, 0.45f, 3.0f) || InEll(x, y - 3.3f, z + 0.2f, 0.15f, 0.6f, 1.0f) ||
                    InEll(x, y + 1.3f, z + 3.6f, 0.6f, 0.25f, 0.5f)) { m = AnatMat.Csf; return; }
                if (post)
                {
                    float dc = math.min(dB, tent - y);
                    float g = math.sin(11f * (y - 0.25f * z) + 2f * math.sin(3f * x));
                    m = (dc < 0.35f || (math.abs(g) < 0.55f && dc < 1.6f)) ? AnatMat.CerebGrey : AnatMat.White;
                    if (stem < 0.25f) m = AnatMat.Csf; // четвёртый желудочек и цистерна у ствола
                    return;
                }
                if (InEll(ax - 1.5f, y - 3.9f, z - 0.4f, 1.1f, 0.9f, 1.6f)) { m = AnatMat.Grey; return; }   // подкорковые ядра
                if (dB > 2.3f) { m = AnatMat.White; return; }
                float sd = SulcusDist(x, y, z);
                if (sd < 0.1f && dB < 1.9f) { m = AnatMat.Csf; return; }       // борозда
                if (dB < 0.75f) { m = AnatMat.Grey; return; }
                m = sd < 0.42f ? AnatMat.Grey : AnatMat.White;                    // кора по стенкам борозды, белое вещество в сердцевине извилины
            }
        }

        /// Надкостница везде, где кость касается мягких покровов или мышцы: ячейка кости с таким соседом становится надкостницей,
        /// если кость за ней с противоположной стороны продолжается (тонкую стенку в один воксель не трогаем).
        /// Нужна там, где округление толщин слоёв в соседних столбцах расходится на шаг и рыхлый слой касается кости напрямую.
        [BurstCompile(CompileSynchronously = true)]
        struct SealJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> src;
            [NativeDisableParallelForRestriction] public NativeArray<byte> mat, part;
            public int3 dims;
            static bool IsBone(byte m) => m == AnatMat.Compact || m == AnatMat.Spongy;
            static bool IsSoft(byte m) => m == AnatMat.Skin || m == AnatMat.Stubble || m == AnatMat.Fat || m == AnatMat.Fascia || m == AnatMat.Loose
                || m == AnatMat.Muscle || m == AnatMat.Tendon || m == AnatMat.Parotid;
            public void Execute(int z)
            {
                int W = dims.x, H = dims.y, D = dims.z;
                for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    if (z < 1 || z >= D - 1) continue;
                    int i = x + W * (y + H * z);
                    if (!IsBone(src[i])) continue;
                    for (int d = 0; d < 6; d++)
                    {
                        int o = d == 0 ? 1 : d == 1 ? -1 : d == 2 ? W : d == 3 ? -W : d == 4 ? W * H : -W * H;
                        if (IsSoft(src[i + o]) && IsBone(src[i - o])) { mat[i] = AnatMat.Periost; part[i] = 0; break; }
                    }
                }
            }
        }

        /// Кость: компактная, если рядом (6 соседей) не кость; иначе губчатая. Швы — кость граничит с другой костью.
        [BurstCompile(CompileSynchronously = true)]
        struct BoneJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<byte> mat, part;
            public int3 dims;
            static bool IsBone(byte m) => m == AnatMat.Compact || m == AnatMat.Spongy;
            public void Execute(int z)
            {
                int W = dims.x, H = dims.y, D = dims.z;
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = x + W * (y + H * z);
                    byte m = mat[i];
                    if (!IsBone(m)) continue;
                    bool edge = false, suture = false;
                    int pid = part[i] & 0x7F;
                    for (int d = 0; d < 6; d++)
                    {
                        int xx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), yy = y + (d == 2 ? 1 : d == 3 ? -1 : 0), zz = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
                        if (xx < 0 || yy < 0 || zz < 0 || xx >= W || yy >= H || zz >= D) { edge = true; continue; }
                        int j = xx + W * (yy + H * zz);
                        if (!IsBone(mat[j])) edge = true;
                        else { int pj = part[j] & 0x7F; if (pj != pid && pj != 0 && pid != 0) suture = true; }
                    }
                    if (edge && m == AnatMat.Spongy) mat[i] = AnatMat.Compact;
                    if (suture) part[i] = (byte)(pid | AnatPart.Suture);
                }
            }
        }

        /// Надкостница нижней челюсти — в решётке головы: мягкая ячейка рядом с челюстью становится надкостницей.
        [BurstCompile(CompileSynchronously = true)]
        struct WrapJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<byte> mat;
            [ReadOnly] public NativeArray<byte> part;
            public int3 dims;
            public void Execute(int z)
            {
                int W = dims.x, H = dims.y, D = dims.z;
                if (z == 0 || z == D - 1) return;
                for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    int i = x + W * (y + H * z);
                    byte m = mat[i];
                    if (m != AnatMat.Muscle && m != AnatMat.Fat && m != AnatMat.Fascia && m != AnatMat.Loose && m != AnatMat.Parotid) continue;
                    for (int d = 0; d < 6; d++)
                    {
                        int j = i + (d == 0 ? 1 : d == 1 ? -1 : d == 2 ? W : d == 3 ? -W : d == 4 ? W * H : -W * H);
                        if (mat[j] == AnatMat.Covered && part[j] == 2) { mat[i] = AnatMat.Periost; break; }
                    }
                }
            }
        }

        /// Волосы, брови: слой в одну ячейку поверх кожи в своих зонах.
        [BurstCompile(CompileSynchronously = true)]
        struct HairJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<byte> mat, part, zone;
            public int3 dims; public float3 org; public float s;
            public void Execute(int z)
            {
                int W = dims.x, H = dims.y, D = dims.z;
                if (z == 0 || z == D - 1) return;
                float Z = org.z + (z + 0.5f) * s;
                for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    int i = x + W * (y + H * z);
                    if (mat[i] != 0) continue;
                    float X = org.x + (x + 0.5f) * s, Y = org.y + (y + 0.5f) * s;
                    bool hair = HairZone(X, Y, Z), brow = !hair && BrowZone(math.abs(X), Y, Z);
                    if (!hair && !brow) continue;
                    bool skin = false;
                    for (int d = 0; d < 6 && !skin; d++)
                    {
                        int j = i + (d == 0 ? 1 : d == 1 ? -1 : d == 2 ? W : d == 3 ? -W : d == 4 ? W * H : -W * H);
                        skin = mat[j] == AnatMat.Skin;
                    }
                    if (!skin) continue;
                    mat[i] = hair ? AnatMat.Hair : AnatMat.Brow;
                    zone[i] = ZoneAt(X, Y, Z);
                }
            }
        }

        // ---------- нижняя челюсть ----------
        [BurstCompile(CompileSynchronously = true)]
        struct MandMaskJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<byte> mask;
            [ReadOnly] public NativeArray<ushort> hdist;
            [ReadOnly] public NativeArray<float4> arch;
            [ReadOnly] public NativeArray<float> thick;
            public int3 hdims, off, dims; public float3 org; public float s;
            public void Execute(int z)
            {
                float Z = org.z + (z + 0.5f) * s;
                for (int y = 0; y < dims.y; y++)
                {
                    float Y = org.y + (y + 0.5f) * s;
                    for (int x = 0; x < dims.x; x++)
                    {
                        float X = org.x + (x + 0.5f) * s;
                        int hi = (x + off.x) + hdims.x * ((y + off.y) + hdims.y * (z + off.z));
                        float depth = math.max(0, (hdist[hi] / 3f - 0.5f) * s);
                        mask[x + dims.x * (y + dims.y * z)] = (byte)(depth >= 0.4f && Mandible(math.abs(X), Y, Z, arch, thick) < 0 ? 1 : 0);
                    }
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct MandClassJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> mask;
            [ReadOnly] public NativeArray<ushort> dist;
            [ReadOnly] public NativeArray<ToothDef> teeth;
            [NativeDisableParallelForRestriction] public NativeArray<byte> mat, part, zone;
            public int3 dims; public float3 org; public float s;
            public void Execute(int z)
            {
                float Z = org.z + (z + 0.5f) * s;
                for (int y = 0; y < dims.y; y++)
                {
                    float Y = org.y + (y + 0.5f) * s;
                    for (int x = 0; x < dims.x; x++)
                    {
                        int i = x + dims.x * (y + dims.y * z);
                        byte m = 0, p = 0;
                        if (mask[i] != 0)
                        {
                            float X = org.x + (x + 0.5f) * s, ax = math.abs(X);
                            float3 pp = new float3(X, Y, Z);
                            float tsd = 9; float3 tq; int ti;
                            if (Y > -7.8f && ax < 3.8f && Z > 2.0f) tsd = TeethSdf(teeth, 16, 16, pp, out tq, out ti);
                            int dd = dist[i];
                            if (tsd < 0.05f) m = 0;                                                         // лунка (зуб — своё тело)
                            else if (AlvCanal(ax, Y, Z) < 0) { m = AnatMat.Artery; p = AnatPart.InfAlveolarA; } // нижнечелюстной канал
                            else if (dd <= 3)
                            {
                                bool joint = Y > Condyle.y - 0.1f && SdEll(ax - Condyle.x, Y - Condyle.y, Z - Condyle.z, 0.95f, 0.42f, 0.5f) < 0.05f;
                                m = joint ? AnatMat.Cartilage : AnatMat.Compact; p = joint ? (byte)0 : AnatPart.Mandible;
                            }
                            else { m = tsd < 0.25f ? AnatMat.Compact : AnatMat.Spongy; p = AnatPart.Mandible; } // кость; компакта у края — проход BoneJob
                        }
                        mat[i] = m; part[i] = p; zone[i] = m != 0 ? ZoneAt(org.x + (x + 0.5f) * s, Y, Z) : (byte)0;
                    }
                }
            }
        }
    }
}
