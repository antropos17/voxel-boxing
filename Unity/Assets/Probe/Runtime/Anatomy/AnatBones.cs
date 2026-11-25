using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe
{
    /// Этап B, часть 9: переломы костей по HEAD-SPEC §5. Нагрузка на кость копится от ударов по зонам (с передачей соседним
    /// костям), кость ломается при своём пороге (порядок §5: нос раньше всех, затылок позже всех), следующий уровень — при пороге × Grow.
    /// Отломок — связный кусок кости: набор костей (граница идёт по швам между частями решётки) или кость, отрезанная анатомической
    /// линией (Ле Фор I, дно глазницы — тонкое место, вдавление свода — шов или край вдавления). Смещение отломка вместе с мягкими
    /// тканями над ним — только вид: вес отломка в 3D-текстуре тела (тексель = 2×2×2 ячейки, канал = отломок), вершинный шейдер
    /// сдвигает вершины на вес × смещение. Решётка, rng и разрушение не меняются, поэтому сценарий seed 77 побитно прежний.
    /// Маска отломка строится джобом (Burst) после удара и забирается в Tick следующего кадра — в кадре удара полной перестройки нет.
    public class AnatBones
    {
        public const byte Nose = 0, OrbitL = 1, OrbitR = 2, TempL = 3, TempR = 4, JawAngle = 5, ZygL = 6, ZygR = 7, Maxilla = 8,
            ParL = 9, ParR = 10, JawBody = 11, Frontal = 12, Occipital = 13, Count = 14;
        public static readonly string[] Names = { "носовые кости", "дно глазницы Л", "дно глазницы П", "чешуя височной Л", "чешуя височной П",
            "угол и мыщелок челюсти", "скуловая Л", "скуловая П", "верхняя челюсть", "теменная Л", "теменная П", "тело челюсти", "лобная", "затылочная" };
        /// Место кости в таблице §5 (0 — ломается первой).
        public static readonly int[] Rank = { 0, 1, 1, 2, 2, 3, 4, 4, 5, 6, 6, 7, 8, 9 };
        /// Порог первого уровня в единицах нагрузки (E мощного удара; лёгкий — половина). Нижние границы §5 (100, 400, 400, 600, 850, 970,
        /// 2000, 2500, 3000, 3500 Н) сжаты под шкалу травмы зоны (каша 7, мясо до кости 14, пролом 24): лицо ломается до каши, свод — до пролома.
        public static readonly float[] Thr = { 2f, 5f, 5f, 5.5f, 5.5f, 6.5f, 8f, 8f, 9f, 14f, 14f, 15f, 18f, 23f };
        public const float Grow = 1.35f;     // порог следующего уровня растёт (§5)
        public const int MaxLevel = 3;       // у верхней челюсти уровни — Ле Фор I, II, III
        public const int MaxActive = 8;      // отломков со смещением у головы (две текстуры по 4 канала); у челюсти — 2
        public const float LeFort1Y = -2.4f; // Ле Фор I: горизонтальная линия над дном носа и верхушками корней
        public const int Dmax = 8;           // мягкие ткани идут за отломком до 8 текселей (3,2 см) от кости
        public static bool On = true;        // выключатель для замера «до / после» в одной сборке

        public class Brk { public byte bone, level, side; public float t; public float3 c, n, d; public int body, slot; }

        public readonly AnatHead a;
        public readonly float[] load = new float[Count];
        public readonly Brk[] rec = new Brk[Count];
        public readonly List<Brk> list = new List<Brk>();
        public readonly List<string> log = new List<string>();
        public int headSlots, jawSlots, noSlot;
        /// Тела: 0 — голова (2 текстуры), 1 — нижняя челюсть (1 текстура). Данные RGBA32, тексель = 2×2×2 ячейки.
        public readonly int3[] tdims = new int3[2];
        public readonly NativeArray<byte>[] data = new NativeArray<byte>[3];   // голова A, голова B, челюсть
        public readonly Texture3D[] tex = new Texture3D[3];
        public readonly float3[] eyeShift = new float3[2];
        public float3 upperShift; public float3[] lowerShift = new float3[0];
        JobHandle job; bool pending; int dueFrame; public bool dirty;
        /// Исходная анатомия для масок (по байту на ячейку: номер кости 0–31, бит 5 — кость, бит 6 — хрящ носа, бит 7 — не пусто):
        /// маска считается по целой голове, а не по текущей решётке — джоб не читает то, что меняет удар, итог не зависит от того, когда он идёт.
        readonly NativeArray<byte>[] code = new NativeArray<byte>[2];
        readonly List<NativeArray<byte>> temps = new List<NativeArray<byte>>(); readonly List<NativeArray<int>> tempsI = new List<NativeArray<int>>();

        public AnatBones(AnatHead a)
        {
            this.a = a;
            tdims[0] = (a.head.dims + 1) / 2; tdims[1] = (a.mand.dims + 1) / 2;
            for (int k = 0; k < 3; k++) { int3 td = tdims[k < 2 ? 0 : 1]; data[k] = new NativeArray<byte>(td.x * td.y * td.z * 4, Allocator.Persistent); }
            lowerShift = new float3[a.teeth.Length];
            for (int b = 0; b < 2; b++) { var g = b == 0 ? a.head : a.mand; code[b] = new NativeArray<byte>(g.Count, Allocator.Persistent); new CodeJob { mat = g.mat, part = g.part, zone = g.zone, code = code[b] }.Run(g.Count); }
            // в решётке головы место нижней челюсти занято (Covered): для масок головы это чужая кость — щека над челюстью не идёт с Ле Фором
            var h = a.head; var m = a.mand; int3 off = AnatomyBuilder.MandOff; var hc = code[0];
            for (int z = 0; z < m.dims.z; z++) for (int y = 0; y < m.dims.y; y++) for (int x = 0; x < m.dims.x; x++)
            {
                int i = m.Index(x, y, z); if (!Hard(m.mat[i])) continue;
                int3 q = new int3(x, y, z) + off; if (math.any(q < 0) || math.any(q >= h.dims)) continue;
                int j = h.Index(q.x, q.y, q.z); if (h.mat[j] == AnatMat.Covered) hc[j] = (byte)(128 | 32 | AnatPart.Mandible);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct CodeJob : IJobFor
        {
            [ReadOnly] public NativeArray<byte> mat, part, zone; public NativeArray<byte> code;
            public void Execute(int i)
            {
                byte m = mat[i]; int c = (part[i] & 0x7F) < 32 ? part[i] & 31 : 0;
                if (Hard(m)) c |= 32; else if (m == AnatMat.Cartilage && zone[i] == AnatZone.Nose) c |= 64;
                if (m != AnatMat.Empty) c |= 128;
                code[i] = (byte)c;
            }
        }

        public void Reset()
        {
            Complete();
            System.Array.Clear(load, 0, Count); System.Array.Clear(rec, 0, Count); list.Clear(); log.Clear();
            headSlots = jawSlots = noSlot = 0; System.Array.Clear(sent, 0, Count); eyeShift[0] = eyeShift[1] = 0; upperShift = 0; System.Array.Clear(lowerShift, 0, lowerShift.Length);
            for (int k = 0; k < 3; k++) { var d = data[k]; unsafe { Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemClear(Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(d), d.Length); } }
            dirty = true;
        }

        /// Куда идёт нагрузка от удара в зону (сторона s: 0 — Л, x ≥ 0; side — удар сбоку). Передача соседним костям — как в вебе
        /// (рот → челюсть ×0,55, глазница ×0,8, скула ×0,85).
        public static void Targets(byte zone, int s, bool side, float3 p, List<(byte b, float f)> L)
        {
            L.Clear();
            byte orb = s == 0 ? OrbitL : OrbitR, tmp = s == 0 ? TempL : TempR, zyg = s == 0 ? ZygL : ZygR, par = s == 0 ? ParL : ParR;
            switch (zone)
            {
                case AnatZone.Nose: L.Add((Nose, 1f)); L.Add((Maxilla, 0.4f)); break;
                case AnatZone.Eye: L.Add((orb, 0.8f)); L.Add((zyg, 0.3f)); L.Add((Nose, 0.2f)); break;
                case AnatZone.Brow: L.Add((Frontal, 0.6f)); L.Add((orb, 0.5f)); L.Add((zyg, 0.3f)); break;
                case AnatZone.CheekBone: L.Add((zyg, 0.85f)); L.Add((orb, 0.5f)); L.Add((Maxilla, 0.3f)); break;
                case AnatZone.Cheek:
                    if (math.abs(p.x) > 2.6f && p.y > -1.6f) { L.Add((zyg, 0.85f)); L.Add((orb, 0.4f)); L.Add((Maxilla, 0.4f)); break; }   // щека над скуловой костью — как скула
                    L.Add((Maxilla, 0.6f)); L.Add((zyg, 0.4f)); L.Add((side ? JawAngle : JawBody, 0.3f)); break;
                case AnatZone.Mouth: L.Add((Maxilla, 0.5f)); L.Add((JawBody, 0.55f)); break;
                case AnatZone.Jaw: if (side) { L.Add((JawAngle, 1f)); L.Add((JawBody, 0.4f)); } else { L.Add((JawBody, 1f)); L.Add((JawAngle, 0.3f)); } break;
                case AnatZone.Temple: L.Add((tmp, 1f)); L.Add((zyg, 0.3f)); L.Add((par, 0.3f)); break;
                case AnatZone.Fore: L.Add((Frontal, 1f)); L.Add((Nose, 0.1f)); break;
                case AnatZone.Scalp:
                    if (p.z > 3f) { L.Add((Frontal, 1f)); L.Add((par, 0.3f)); }
                    else if (p.z < -5f) { L.Add((Occipital, 1f)); L.Add((par, 0.3f)); }
                    else L.Add((par, 1f));
                    break;
                case AnatZone.Occiput: L.Add((Occipital, 1f)); L.Add((par, 0.3f)); break;
                case AnatZone.Ear: L.Add((tmp, 0.6f)); L.Add((JawAngle, 0.4f)); break;
            }
        }

        public static float Threshold(byte b, int level) => Thr[b] * math.pow(Grow, level);
        public byte Level(byte b) => rec[b] == null ? (byte)0 : rec[b].level;

        static readonly List<(byte b, float f)> TL = new List<(byte, float)>();
        /// Удар (точка контакта p, наружная нормаль n, направление dir): нагрузка на кости, переломы и новые уровни. Не больше одного
        /// нового уровня у кости за удар. Возвращает число новых переломов и уровней.
        public int Hit(byte zone, float3 p, float3 n, float3 dir, float E, bool power, float time, int frame)
        {
            if (!On || zone == 0) return 0;
            int s = p.x >= 0 ? 0 : 1; bool side = math.abs(dir.x) > 0.55f;
            Targets(zone, s, side, p, TL);
            float add = E * (power ? 1f : 0.5f); int got = 0;
            foreach (var (b, f) in TL)
            {
                load[b] += add * f;
                int lv = Level(b); if (lv >= MaxLevel || load[b] < Threshold(b, lv)) continue;
                if (Break(b, (byte)(lv + 1), s, p, n, dir, time)) got++;
            }
            if (got > 0) Schedule(frame);
            return got;
        }

        bool Break(byte b, byte lv, int s, float3 p, float3 n, float3 dir, float time)
        {
            var r = rec[b];
            if (r == null)
            {
                bool jaw = b == JawAngle || b == JawBody;
                r = new Brk { bone = b, side = (byte)(b == Nose || b == Maxilla || b == Frontal || b == Occipital ? 0 : s), c = p, n = n, t = time, body = jaw ? 1 : 0, slot = -1 };
                if (jaw) { if (jawSlots < 2) r.slot = jawSlots++; }
                else if (headSlots < MaxActive) r.slot = headSlots++;
                if (r.slot < 0) noSlot++;
                rec[b] = r; list.Add(r);
            }
            r.level = lv; r.d = Disp(r, dir);
            if (b == Maxilla) upperShift = r.d;
            if (b == OrbitL || b == OrbitR) eyeShift[b == OrbitL ? 0 : 1] = new float3(0, -0.22f, -0.25f) * Mul(lv) * ShowEye;
            if (b == JawAngle || b == JawBody) for (int t = 0; t < a.teeth.Length; t++)
                if (a.toothDefs[t].upper == 0 && Member(b, r.side, lv, a.toothDefs[t].c, AnatPart.Mandible, AnatMat.Compact, 0, r.c)) lowerShift[t] = r.d;
            log.Add(time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " с: перелом — " + Label(r) + (r.slot < 0 ? " (без смещения: предел отломков)" : ""));
            dirty = true;
            return true;
        }

        public static string Label(Brk r) => r.bone == Maxilla ? "верхняя челюсть, Ле Фор " + (r.level == 1 ? "I" : r.level == 2 ? "II" : "III")
            : Names[r.bone] + (r.level > 1 ? ", уровень " + r.level : "");

        static float Mul(int lv) => lv <= 1 ? 1f : lv == 2 ? 1.5f : 1.9f;

        /// Усиление смещения ради читаемости с расстояния боя (замечание Руслана 25.11.2026): лицо ×Show, свод ×ShowVault.
        public const float Show = 2.6f, ShowVault = 1.5f, ShowEye = 1.2f;   // глаз и дно глазницы — меньше: сильнее глаз уходит из век и открывает тёмную глазницу

        /// Смещение отломка, см в рамке головы.
        static float3 Disp(Brk r, float3 dir)
        {
            bool vault = r.bone == Frontal || r.bone == Occipital || r.bone == ParL || r.bone == ParR || r.bone == TempL || r.bone == TempR;
            return Disp0(r, dir) * (vault ? ShowVault : r.bone == OrbitL || r.bone == OrbitR ? ShowEye : Show);
        }

        static float3 Disp0(Brk r, float3 dir)
        {
            float m = Mul(r.level), sx = r.side == 0 ? 1 : -1;
            switch (r.bone)
            {
                case Nose:
                    // удар сбоку — нос уходит вбок по удару, прямой — вдавлен
                    return (math.abs(dir.x) > 0.25f ? new float3(math.sign(dir.x) * 0.45f, -0.05f, -0.15f) : new float3(0, -0.08f, -0.4f)) * m;
                case OrbitL: case OrbitR: return new float3(0, -0.3f, -0.08f) * m;
                case ZygL: case ZygR: return new float3(-sx * 0.3f, -0.12f, -0.3f) * m;
                case Maxilla: return r.level == 1 ? new float3(0, -0.15f, -0.3f) : r.level == 2 ? new float3(0, -0.2f, -0.45f) : new float3(0, -0.25f, -0.6f);
                case JawAngle: return new float3((dir.x >= 0 ? 1 : -1) * 0.35f, -0.25f, -0.05f) * m;
                case JawBody: return new float3(sx * 0.15f, -0.2f, -0.15f) * m;
                case Frontal: return -r.n * 0.8f * m;   // вдавление на толщину кости (7–10 мм)
                default: return -r.n * (r.bone == Occipital ? 0.5f : r.bone == ParL || r.bone == ParR ? 0.6f : 0.5f) * m;   // чешуя височной, теменная
            }
        }

        static bool Hard(byte m) => m == AnatMat.Compact || m == AnatMat.Spongy;

        /// Ячейка (p — см головы, part, mat, zone) — в отломке кости bone? c — точка удара (центр вдавления).
        public static bool Member(byte bone, int side, int level, float3 p, byte part, byte mat, byte zone, float3 c)
        {
            bool hard = Hard(mat), cart = mat == AnatMat.Cartilage && zone == AnatZone.Nose;
            int pt = part & 0x7F; float ax = math.abs(p.x); bool L = side == 0;
            bool nasal = pt == AnatPart.NasalL || pt == AnatPart.NasalR;
            switch (bone)
            {
                case Nose: return (hard && nasal) || cart;
                case OrbitL: case OrbitR:
                    return hard && pt == (L ? AnatPart.MaxillaL : AnatPart.MaxillaR) && math.abs(ax - AnatGeo.EX) < 1.15f
                        && p.y > AnatGeo.EY - AnatGeo.ER - 0.9f && p.y < AnatGeo.EY - AnatGeo.ER + 0.35f && p.z > AnatGeo.EZ - 2.6f && p.z < AnatGeo.EZ + 0.4f;
                case TempL: case TempR: return hard && pt == (L ? AnatPart.TemporalL : AnatPart.TemporalR) && p.y > 0.8f && math.distance(p, c) < 2.6f;
                case ZygL: case ZygR: return hard && pt == (L ? AnatPart.ZygomaticL : AnatPart.ZygomaticR);
                case Maxilla:
                {
                    bool mx = pt == AnatPart.MaxillaL || pt == AnatPart.MaxillaR || pt == AnatPart.Palatine || pt == AnatPart.Vomer;
                    if (level <= 1) return hard && mx && p.y < LeFort1Y;
                    if (level == 2) return (hard && (mx || nasal)) || cart;
                    return (hard && (mx || nasal || pt == AnatPart.ZygomaticL || pt == AnatPart.ZygomaticR || pt == AnatPart.Ethmoid)) || cart;
                }
                case ParL: case ParR: return hard && pt == (L ? AnatPart.ParietalL : AnatPart.ParietalR) && math.distance(p, c) < 3.0f;
                case Frontal: return hard && pt == AnatPart.Frontal && math.distance(p, c) < 2.2f;
                case Occipital: return hard && pt == AnatPart.Occipital && math.distance(p, c) < 3.0f;
                case JawAngle:
                {
                    if (!hard || pt != AnatPart.Mandible) return false;
                    bool onHit = (p.x >= 0) == L;
                    if (onHit && ax > 2.8f && p.z < 2.9f + 0.25f * (p.y + 7f)) return false;   // ветвь стороны удара за линией угла
                    if (!onHit && ax > 3.6f && p.y > -2.4f) return false;                     // мыщелок другой стороны выше шейки
                    return true;
                }
                case JawBody: return hard && pt == AnatPart.Mandible && (L ? p.x > 0 : p.x < 0);   // по средней линии (симфиз)
            }
            return false;
        }

        /// Коробка отломка с мягкими тканями вокруг, см головы.
        public static void Box(Brk r, out float3 lo, out float3 hi)
        {
            float sx = r.side == 0 ? 1 : -1; float3 e;
            switch (r.bone)
            {
                case Nose: lo = new float3(-2.5f, -4.5f, 4f); hi = new float3(2.5f, 5f, 13f); return;
                case OrbitL: case OrbitR: e = new float3(sx * AnatGeo.EX, AnatGeo.EY - AnatGeo.ER, AnatGeo.EZ - 1); lo = e - new float3(3, 3, 4); hi = e + new float3(3, 3, 4); return;
                case ZygL: case ZygR: lo = new float3(sx > 0 ? 1.5f : -9.5f, -4f, -1f); hi = new float3(sx > 0 ? 9.5f : -1.5f, 5f, 11f); return;
                case Maxilla: lo = new float3(-6.5f, -7f, -1f); hi = new float3(6.5f, r.level >= 3 ? 5.5f : 4f, 13f); return;
                case JawAngle: case JawBody: lo = -100; hi = 100; return;
                default: float R = r.bone == Frontal ? 2.2f : r.bone == TempL || r.bone == TempR ? 2.6f : 3.0f; lo = r.c - (R + 2.5f); hi = r.c + (R + 2.5f); return;
            }
        }

        void Schedule(int frame)
        {
            foreach (var r in list)
            {
                if (r.slot < 0 || !Changed(r)) continue;
                var g = r.body == 0 ? a.head : a.mand; int k = r.body == 0 ? (r.slot < 4 ? 0 : 1) : 2, ch = r.slot & 3;
                Box(r, out float3 lo, out float3 hi);
                int3 clo = math.max((int3)math.floor((lo - g.org) / g.step), 0), chi = math.min((int3)math.floor((hi - g.org) / g.step), g.dims - 1);
                int3 tlo = clo / 2, thi = chi / 2, bn = thi - tlo + 1; int nb = bn.x * bn.y * bn.z;
                var val = new NativeArray<byte>(nb, Allocator.TempJob); var tmp = new NativeArray<byte>(nb, Allocator.TempJob); var cls = new NativeArray<byte>(nb, Allocator.TempJob); var q = new NativeArray<int>(nb, Allocator.TempJob);
                temps.Add(val); temps.Add(tmp); temps.Add(cls); tempsI.Add(q);
                job = new FragJob { code = code[r.body], dims = g.dims, org = g.org, step = g.step, pos = g.pos, rot = g.rot,
                    tlo = tlo, thi = thi, td = tdims[r.body == 0 ? 0 : 1], bone = r.bone, side = r.side, level = r.level, c = r.c, ch = ch,
                    tex = data[k], val = val, tmp = tmp, cls = cls, q = q }.Schedule(job);
                sent[r.bone] = r.level;
            }
            pending = true; dueFrame = frame + 1;
        }
        readonly byte[] sent = new byte[Count];
        bool Changed(Brk r) => sent[r.bone] != r.level && (r.bone == Maxilla || sent[r.bone] == 0);   // маска меняется только у верхней челюсти (Ле Фор)

        /// Забрать готовые маски в кадре frame (с кадра после удара); true — текстуры обновились.
        public bool Pending => pending;
        public bool Due(int frame) { if (!pending || frame < dueFrame) return false; Complete(); return true; }

        public void Complete()
        {
            if (!pending) return;
            job.Complete(); pending = false;
            foreach (var t in temps) t.Dispose(); foreach (var t in tempsI) t.Dispose(); temps.Clear(); tempsI.Clear();
            dirty = true;
        }

        /// Вес отломков в точке тела body (ячейки решётки, как в шейдере: трилинейно по текселям), 8 каналов (у челюсти 4).
        public float4x2 WeightAt(int body, float3 cells)
        {
            int3 td = tdims[body]; float3 u = cells * 0.5f - 0.5f; int3 i0 = (int3)math.floor(u); float3 f = u - i0;
            float4x2 w = default;
            for (int c = 0; c < 8; c++)
            {
                int3 o = new int3(c & 1, (c >> 1) & 1, c >> 2), q = math.clamp(i0 + o, 0, td - 1);
                float k = (o.x == 1 ? f.x : 1 - f.x) * (o.y == 1 ? f.y : 1 - f.y) * (o.z == 1 ? f.z : 1 - f.z);
                int ti = (q.x + td.x * (q.y + td.y * q.z)) * 4;
                var A = data[body == 0 ? 0 : 2]; w.c0 += k * new float4(A[ti], A[ti + 1], A[ti + 2], A[ti + 3]) / 255f;
                if (body == 0) { var B = data[1]; w.c1 += k * new float4(B[ti], B[ti + 1], B[ti + 2], B[ti + 3]) / 255f; }
            }
            return w;
        }

        /// Смещение в точке p (см головы) по телу body — то же, что сдвигает вершину в шейдере, см.
        public float3 DispAt(int body, float3 p)
        {
            var g = body == 0 ? a.head : a.mand; var w = WeightAt(body, (math.mul(math.transpose(g.rot), p - g.pos) - g.org) / g.step);
            float3 d = 0;
            foreach (var r in list) if (r.slot >= 0 && r.body == body) d += r.d * (r.slot < 4 ? w.c0[r.slot & 3] : w.c1[r.slot & 3]);
            return d;
        }

        public void Dispose()
        {
            Complete();
            for (int k = 0; k < 3; k++) { if (data[k].IsCreated) data[k].Dispose(); if (tex[k] != null) Object.DestroyImmediate(tex[k]); }
            for (int b = 0; b < 2; b++) if (code[b].IsCreated) code[b].Dispose();
        }

        /// Маска одного отломка в канал ch текстуры: тексель кости — доля ячеек отломка среди костных; мягкие ткани берут значение
        /// ближайшей кости (волна от всех костей коробки, не дальше Dmax), пустые тексели у поверхности — максимум соседей.
        [BurstCompile(CompileSynchronously = true)]
        struct FragJob : IJob
        {
            [ReadOnly] public NativeArray<byte> code;
            public int3 dims, tlo, thi, td; public float3 org, pos, c; public float3x3 rot; public float step;
            public byte bone; public int side, level, ch;
            public NativeArray<byte> tex, val, tmp, cls; public NativeArray<int> q;
            public void Execute()
            {
                for (int i = 0; i < tex.Length; i += 4) tex[i + ch] = 0;
                int3 bn = thi - tlo + 1; int qh = 0, qt = 0;
                for (int tz = 0; tz < bn.z; tz++) for (int ty = 0; ty < bn.y; ty++) for (int tx = 0; tx < bn.x; tx++)
                {
                    int fb = 0, ob = 0, sf = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        int x = (tlo.x + tx) * 2 + (k & 1), y = (tlo.y + ty) * 2 + ((k >> 1) & 1), z = (tlo.z + tz) * 2 + (k >> 2);
                        if (x >= dims.x || y >= dims.y || z >= dims.z) continue;
                        int cd = code[x + dims.x * (y + dims.y * z)]; if (cd < 128) continue;
                        bool hard = (cd & 32) != 0, cart = (cd & 64) != 0;
                        float3 p = pos + math.mul(rot, org + (new float3(x, y, z) + 0.5f) * step);
                        if (Member(bone, side, level, p, (byte)(cd & 31), hard ? AnatMat.Compact : cart ? AnatMat.Cartilage : AnatMat.Fat, cart ? AnatZone.Nose : (byte)0, c)) fb++;
                        else if (hard) ob++; else sf++;
                    }
                    int b = tx + bn.x * (ty + bn.y * tz);
                    if (fb + ob > 0) { val[b] = (byte)(255 * fb / (fb + ob)); cls[b] = 1; q[qt++] = b; }
                    else { val[b] = 0; cls[b] = (byte)(sf > 0 ? 2 : 0); }
                }
                // волна по мягким тканям от кости: значение ближайшей кости; cls 1 — задано (у мягких 10 + шаг)
                while (qh < qt)
                {
                    int b = q[qh++]; int bx = b % bn.x, by = (b / bn.x) % bn.y, bz = b / (bn.x * bn.y);
                    int dist = cls[b] == 1 ? 0 : cls[b] - 10; if (dist >= Dmax) continue;
                    for (int d = 0; d < 6; d++)
                    {
                        int nx = bx + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = by + (d == 2 ? 1 : d == 3 ? -1 : 0), nz = bz + (d == 4 ? 1 : d == 5 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nz < 0 || nx >= bn.x || ny >= bn.y || nz >= bn.z) continue;
                        int j = nx + bn.x * (ny + bn.y * nz); if (cls[j] != 2) continue;
                        cls[j] = (byte)(10 + dist + 1); val[j] = val[b]; q[qt++] = j;
                    }
                }
                // мягкие ткани сглаживаются (3 прохода по соседям): кожа над краем отломка — склон, а не ступенька; кость остаётся резкой
                for (int pass = 0; pass < 3; pass++)
                {
                    for (int b = 0; b < val.Length; b++)
                    {
                        tmp[b] = val[b]; if (cls[b] < 10 && cls[b] != 2) continue;
                        int bx = b % bn.x, by = (b / bn.x) % bn.y, bz = b / (bn.x * bn.y), sum = val[b], cnt = 1;
                        for (int d = 0; d < 6; d++)
                        {
                            int nx = bx + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = by + (d == 2 ? 1 : d == 3 ? -1 : 0), nz = bz + (d == 4 ? 1 : d == 5 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nz < 0 || nx >= bn.x || ny >= bn.y || nz >= bn.z) continue;
                            int j = nx + bn.x * (ny + bn.y * nz); if (cls[j] >= 10 || cls[j] == 2) { sum += val[j]; cnt++; }
                        }
                        tmp[b] = (byte)((sum + cnt / 2) / cnt);
                    }
                    for (int b = 0; b < val.Length; b++) val[b] = tmp[b];
                }
                // запись в канал; пустые тексели у поверхности — максимум заданных соседей (иначе выборка у кожи тянет вес к нулю)
                for (int tz = 0; tz < bn.z; tz++) for (int ty = 0; ty < bn.y; ty++) for (int tx = 0; tx < bn.x; tx++)
                {
                    int b = tx + bn.x * (ty + bn.y * tz); byte v = val[b];
                    if (cls[b] == 0 || cls[b] == 2)
                    {
                        v = 0;
                        for (int d = 0; d < 6; d++)
                        {
                            int nx = tx + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = ty + (d == 2 ? 1 : d == 3 ? -1 : 0), nz = tz + (d == 4 ? 1 : d == 5 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nz < 0 || nx >= bn.x || ny >= bn.y || nz >= bn.z) continue;
                            int j = nx + bn.x * (ny + bn.y * nz); if (cls[j] == 1 || cls[j] >= 10) v = (byte)math.max((int)v, (int)val[j]);
                        }
                    }
                    int3 t = tlo + new int3(tx, ty, tz); tex[(t.x + td.x * (t.y + td.y * t.z)) * 4 + ch] = v;
                }
            }
        }
    }
}
