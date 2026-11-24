using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe
{
    /// Этап B: мягкие ткани и кровь (HEAD-SPEC, разделы 4 и 7) на решётке головы.
    /// Удар — как impact() в src/f_damage.js: пятно перчатки с затуханием к краю, перевес выступающих мест; только открытые ячейки
    /// (сосед — пусто или воздух), поэтому слои вскрываются по порядку. Кожа: покраснение, отёк, синяк, ссадина, урон;
    /// рвётся тем легче, чем ближе кость под ней (раздавливание о кость), и рвётся линией по зоне и волокнам мышцы под ней (tear()).
    /// Глубже кожи: урон, кровь. Травма зоны копится, 30% общей травмы головы добавляется к каждой зоне (goreHit()); пороги 7, 14, 24, 42.
    /// Кровь: раны (≤ 44) рождают подтёки (≤ 70), которые бегут по открытой поверхности вниз и оставляют кровь в ячейках;
    /// артерия у виска — пульсирующая струя капель (≤ 160), капли летят и садятся на поверхность; нос — поток из ноздрей.
    /// Все времена — секунды боя (time); в состояния пишутся в единицах 2 с.
    public class AnatDamage
    {
        public const float BruiseDay = 10f;          // секунд боя на «день» синяка: жёлтая стадия к ~110 с
        public const int MaxWounds = 44, MaxDrips = 70, MaxDrops = 160;
        public const float S = AnatGeo.S;

        /// Цвета повреждений, sRGB. 0 — множитель покраснения; 1..5 — множители синяка по дням 0, 2, 4,5, 7,5, 11 (цвет синяка / тон кожи LOOKS[0]);
        /// 6 — снятый эпидермис; 7 — точечная кровь ссадины; 8, 9 — свежая и загустевшая кровь.
        public static readonly Color[] DmgColors =
        {
            new Color(1.0f, 0.70f, 0.68f),
            new Color(0.76f, 0.41f, 0.60f), new Color(0.39f, 0.20f, 0.60f), new Color(0.31f, 0.38f, 0.84f), new Color(0.63f, 0.88f, 0.64f), new Color(0.94f, 1.0f, 0.72f),
            new Color(0.80f, 0.36f, 0.32f), new Color(0.45f, 0.05f, 0.06f),
            new Color(0.50f, 0.03f, 0.04f), new Color(0.23f, 0.07f, 0.05f),
        };
        static readonly float[] BruiseKeys = { 0, 2, 4.5f, 7.5f, 11 };

        /// Множитель цвета синяка в день day (как в шейдере).
        public static Color BruiseColor(float day)
        {
            for (int k = 0; k < 4; k++) if (day < BruiseKeys[k + 1]) return Color.Lerp(DmgColors[1 + k], DmgColors[2 + k], (day - BruiseKeys[k]) / (BruiseKeys[k + 1] - BruiseKeys[k]));
            return DmgColors[5];
        }

        public static bool SkinLike(byte m) => m == AnatMat.Skin || m == AnatMat.Lip || m == AnatMat.Stubble;
        static bool Surface(byte m) => SkinLike(m) || m == AnatMat.Hair || m == AnatMat.Brow;
        static bool Hard(byte m) => m == AnatMat.Compact || m == AnatMat.Spongy || m == AnatMat.Periost || m == AnatMat.Enamel || m == AnatMat.Dentin;
        /// Мягкое, что можно разрушить ударом на этапе B (без глаза, мозга и жидкостей).
        static bool Soft(byte m) => AnatMat.Class(m) == AnatMat.ClsSoft && m != AnatMat.Sclera && m != AnatMat.Cornea && m != AnatMat.Iris && m != AnatMat.Pupil
            && m != AnatMat.Lens && m != AnatMat.Grey && m != AnatMat.White && m != AnatMat.CerebGrey && m != AnatMat.Stem && m != AnatMat.Dura && m != AnatMat.VenSinus;

        public class Wound { public int x, y, z; public float rate, age, acc, tau, beat; public byte kind; public float3 n; public string src; }
        public const byte WSkin = 0, WJet = 1, WVein = 2, WNose = 3;
        public class Drip { public int x, y, z, px = -1, py = -1, pz = -1; public float vol, t, age; }
        public struct Drop { public float3 p, v; public float vol; }

        public struct HitInfo { public byte zone, entryMat; public int cells, removed, split; public float eff, ms; public bool macer; }

        public readonly AnatHead a;
        public readonly AnatGrid h;
        public readonly AnatState st;
        public readonly bool[] dirty;     // чанки головы, которые надо перестроить
        public float time;
        public float3 grav = new float3(0, -1, 0);   // тяжесть в рамке головы
        public readonly float[] trauma = new float[AnatZone.Count];
        public float traumaAll, noseAcc;
        public readonly List<Wound> wounds = new List<Wound>();
        public readonly List<Drip> drips = new List<Drip>();
        public readonly List<Drop> drops = new List<Drop>();
        public readonly bool[] eyeClosed = new bool[2];
        public readonly List<string> events = new List<string>();
        public const int MaxEvents = 128;           // предел журнала: дальше строки не собираются, только счёт eventsLost
        public int eventsLost;
        /// Можно ли записать событие (проверять до склейки строки).
        public bool CanLog() { if (events.Count < MaxEvents) return true; eventsLost++; return false; }
        readonly System.Diagnostics.Stopwatch hitClock = new System.Diagnostics.Stopwatch(), stepClock = new System.Diagnostics.Stopwatch();
        public readonly List<(float t, string mat, byte zone)> exposed = new List<(float, string, byte)>();
        readonly HashSet<int> vessels = new HashSet<int>();
        readonly bool[] macerDone = new bool[AnatZone.Count * 4];
        public readonly List<int>[] lids = { new List<int>(), new List<int>() };
        readonly int3[] nostril = new int3[2];
        readonly System.Random rng;
        float eyeT;
        public System.Action<int, byte> onMat;      // ячейка головы сменила материал (просмотр хранит копию для разрезов)
        void SetMat(int i, byte m) { h.mat[i] = m; onMat?.Invoke(i, m); }
        public bool boneCrush = true;               // учитывать раздавливание о кость (для проверки)
        public float3 lastTearDir; public byte lastTearZone;
        // счёт
        public int hits, removed, peakWounds, peakDrips, peakDrops;
        public double hitMs, hitMsMax, stepMs, stepMsMax, visMs, visMsMax;
        Unity.Collections.NativeList<int> opened;   // ячейки, ставшие пустыми с прошлого обновления видимости
        // ---------- часть 2: переломы, мозг, разрыв ----------
        /// Культя: ниже этой высоты (с неровным краем) разрыв не уходит — шея остаётся якорем (уровень C5: позвонок с каналом
        /// и спинным мозгом, гортань, пищевод, сонные артерии, яремные вены, мышцы, кожа).
        public const float StumpY = -10.4f;
        public static float StumpAt(float x, float z) => StumpY + 0.35f * math.sin(x * 1.7f + z * 0.9f) + 0.25f * math.sin(z * 2.3f - x * 0.6f);
        /// Часть 6: край культи — StumpY ± 0,6 (|0,35·sin| + |0,25·sin| ≤ 0,6); дальше 0,61 от StumpY ответ без синусов, тот же.
        public static bool Protected(float3 p) => p.y >= StumpY + 0.61f ? false : p.y < StumpY - 0.61f || p.y < StumpAt(p.x, p.z);
        public static bool Brainy(byte m) => m == AnatMat.Grey || m == AnatMat.White || m == AnatMat.CerebGrey || m == AnatMat.Stem;
        public Unity.Collections.NativeList<int> cutLog;   // убранные ячейки с прошлого запроса поиска кусков (забирает AnatView)
        public struct Eject { public float3 c, dir; public float r; }
        public readonly List<Eject> ejects = new List<Eject>();   // куски мозга на вылет (делает AnatView: это отдельные тела)
        public readonly List<float4> rips = new List<float4>();    // разрывы: центр и радиус (AnatView убирает и другие тела, полный поиск кусков)
        public readonly List<float3> fractures = new List<float3>();
        /// Часть 5, только вид: ось и радиус кольца каждого перелома (xyz — направление удара, w — Rf, см) — для трещин в шейдере.
        public readonly List<float4> fracAxis = new List<float4>();
        public int ripCount, brainHits; public byte lastZone;
        readonly HashSet<int> seenMat = new HashSet<int>();
        bool closed;                                 // ячейка закрылась (отёк века) — видимость заново

        public AnatDamage(AnatHead a, int seed = 1)
        {
            this.a = a; h = a.head; st = new AnatState(h); rng = new System.Random(seed);
            opened = new Unity.Collections.NativeList<int>(256, Unity.Collections.Allocator.Persistent);
            cutLog = new Unity.Collections.NativeList<int>(1024, Unity.Collections.Allocator.Persistent);
            dirty = new bool[st.nch.x * st.nch.y * st.nch.z];
            // веки: кожа зоны «глаз» в оболочке вокруг шара спереди
            for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
            {
                int i = h.Index(x, y, z); if (h.zone[i] != AnatZone.Eye || !Surface(h.mat[i])) continue;
                float3 p = h.Local(x, y, z);
                for (int s = 0; s < 2; s++)
                {
                    float3 c = new float3(s == 0 ? AnatGeo.EX : -AnatGeo.EX, AnatGeo.EY, AnatGeo.EZ);
                    float d = math.length(p - c);
                    if (d > AnatGeo.ER + 0.05f && d < AnatGeo.ER + 0.9f && p.z > AnatGeo.EZ - 0.2f && math.abs(p.y - AnatGeo.EY) < 1.3f) lids[s].Add(i);
                }
            }
            for (int s = 0; s < 2; s++) nostril[s] = Nearest(new float3(s == 0 ? 0.56f : -0.56f, -2.95f, 9.25f), 4, true);
        }

        // ---------- решётка ----------
        int3 Cell(int i) => new int3(i % h.dims.x, (i / h.dims.x) % h.dims.y, i / (h.dims.x * h.dims.y));
        bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < h.dims.x && y < h.dims.y && z < h.dims.z;
        byte M(int x, int y, int z) => In(x, y, z) ? h.mat[h.Index(x, y, z)] : AnatMat.Empty;
        static bool Open(byte m) => m == AnatMat.Empty || m == AnatMat.Air;
        /// Открыта наружу: сосед пуст (снаружи или убран) или это воздух полости, видимый снаружи (AnatVis). Закрытые пазухи и
        /// носовые ходы не в счёт — иначе удар в лицо рвёт слизистую пазухи раньше кожи над ней.
        bool OpenAt(int x, int y, int z)
        {
            if (!In(x, y, z)) return true;
            int i = h.Index(x, y, z); byte m = h.mat[i];
            return m == AnatMat.Empty || (m == AnatMat.Air && (!h.vis.IsCreated || h.vis[i] <= AnatVis.Lim));
        }
        bool Exposed(int x, int y, int z) =>
            OpenAt(x + 1, y, z) || OpenAt(x - 1, y, z) || OpenAt(x, y + 1, z) || OpenAt(x, y - 1, z) || OpenAt(x, y, z + 1) || OpenAt(x, y, z - 1);
        static byte T2(float t) => (byte)math.clamp((int)math.round(t / 2f), 0, 255);
        static byte Add8(int a, float b) => (byte)math.clamp((int)math.round(a + b), 0, 255);
        static byte Wt(byte t0, float w0, byte t1, float w1) => w0 + w1 <= 0 ? t1 : (byte)math.round((t0 * w0 + t1 * w1) / (w0 + w1));

        void Dirty(int x, int y, int z)
        {
            int cs = AnatState.CS;
            int x0 = math.max(0, x - 3) / cs, x1 = math.min(h.dims.x - 1, x + 3) / cs, y0 = math.max(0, y - 3) / cs, y1 = math.min(h.dims.y - 1, y + 3) / cs,
                z0 = math.max(0, z - 3) / cs, z1 = math.min(h.dims.z - 1, z + 3) / cs;
            for (int cz = z0; cz <= z1; cz++) for (int cy = y0; cy <= y1; cy++) for (int cx = x0; cx <= x1; cx++) dirty[cx + st.nch.x * (cy + st.nch.y * cz)] = true;
        }

        public VS Get(int x, int y, int z) => st.Get(x, y, z);
        void Put(int x, int y, int z, VS v) { st.Set(x, y, z, v); Dirty(x, y, z); }

        /// Нормаль наружу по заполненности 5×5×5 (как setNrm в вебе).
        float3 Normal(int x, int y, int z)
        {
            float3 s = 0;
            for (int c = -2; c <= 2; c++) for (int b = -2; b <= 2; b++) for (int a2 = -2; a2 <= 2; a2++)
                if (!AnatMat.Cull(M(x + a2, y + b, z + c))) s += new float3(a2, b, c);
            return math.normalizesafe(s, new float3(0, 0, 1));
        }

        /// Ближайшая открытая рисуемая ячейка в кубе r вокруг точки (lowY — среди равных брать нижнюю).
        int3 Nearest(float3 p, int r, bool lowY)
        {
            int3 c = (int3)math.floor((p - h.org) / S); int3 best = new int3(-1); float bd = 1e9f;
            for (int dz = -r; dz <= r; dz++) for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
            {
                int x = c.x + dx, y = c.y + dy, z = c.z + dz; byte m = M(x, y, z);
                if (!AnatMat.Cull(m) || m == AnatMat.Covered || !Exposed(x, y, z)) continue;
                float d = dx * dx + dy * dy + dz * dz + (lowY ? dy * 0.8f : 0);
                if (d < bd) { bd = d; best = new int3(x, y, z); }
            }
            return best;
        }

        /// Глубина до кости (или зуба) вдоль l от ячейки, см. Хрящ — как кость в 1,6 раза дальше, полость — опоры нет.
        float BoneDepth(int x, int y, int z, float3 l)
        {
            float3 p = h.Local(x, y, z);
            for (int k = 1; k <= 15; k++)
            {
                float3 q = p + l * (k * S);
                int i = h.Lookup(q); if (i < 0) return 3;
                byte m = h.mat[i];
                if (m == AnatMat.Covered) m = a.Sample(q, out _, out _);
                if (Hard(m)) return k * S;
                if (m == AnatMat.Cartilage) return k * S * 1.6f;
                if (m == AnatMat.Air && k <= 8) continue;   // узкая щель (преддверие рта): губа прижимается к зубам за ней
                if (Open(m) || AnatMat.Class(m) == AnatMat.ClsLiquid) return 3;
            }
            return 3;
        }

        // ---------- удар ----------
        static readonly List<(int i, int x, int y, int z, float p)> PL = new List<(int, int, int, int, float)>();
        static readonly List<int3> FAIL = new List<int3>();
        // часть 5, доработка: синяк шире пятна — квадрат расстояния до оси пятна у ячеек PL и кольцо кожи вокруг пятна
        static readonly List<float> PR = new List<float>();
        static readonly List<(int x, int y, int z, float r2)> RING = new List<(int, int, int, float)>();
        /// Предел ячеек, которые кровь смачивает растеканием вбок за бой (MaxSpread); счётчик — spreadCells.
        public const int MaxSpread = 12000;
        public int spreadCells;
        /// Выключатель доработки (синяк шире пятна и растекание крови) — для замера «до / после» в одном коде; по умолчанию включено.
        public static bool Wide = true;
        static float H3(int x, int y, int z) { uint q = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791); q ^= q >> 13; q *= 0x5bd1e995; q ^= q >> 15; return (q & 0xFFFF) / 65536f; }

        /// Удар в точку p (см, рамка головы) по направлению dir, сила E в единицах веба (джеб ≈ 1, мощный 3,4–4,6).
        public HitInfo Hit(float3 p, float3 dir, float E, bool power)
        {
            var sw = hitClock; sw.Restart();
            var r = new HitInfo();
            dir = math.normalize(dir);
            int3 hc = new int3(-1);
            // первая ткань вдоль луча — до выхода из решётки (точка удара может лежать далеко перед лицом, яма — глубоко)
            float th = 0;
            for (float t = -5; t < 40; t += 0.05f)
            {
                int i = h.Lookup(p + dir * t); if (i < 0) continue;
                if (AnatMat.Cull(h.mat[i])) { hc = Cell(i); th = t; break; }
            }
            if (hc.x < 0) return r;
            // кулак шире глазной щели и рта: если луч ушёл в щель на другое тело (глаз, зубы), контакт — первая ткань головы
            // в круге 0,9 см вокруг луча (веко, край глазницы, губа)
            if (h.mat[h.Index(hc.x, hc.y, hc.z)] == AnatMat.Covered)
            {
                float3 u = math.normalize(math.cross(dir, math.abs(dir.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0))), w = math.cross(dir, u);
                bool got = false;
                for (float t = th - 3; t < th && !got; t += 0.05f)
                    for (int k = 0; k < 12 && !got; k++)
                    {
                        float an = k * math.PI / 6; int j = h.Lookup(p + dir * t + (u * math.cos(an) + w * math.sin(an)) * 0.9f);
                        if (j >= 0 && AnatMat.Cull(h.mat[j]) && h.mat[j] != AnatMat.Covered) { hc = Cell(j); got = true; }
                    }
            }
            hits++;
            float3 n = Normal(hc.x, hc.y, hc.z);
            AnatProf.Lap(AnatProf.Locate);
            float3 l = math.normalize(-n * 0.7f + dir * 0.3f);          // ось пятна — внутрь
            float Rc = (power ? 2.4f : 1.7f) / S, R2 = Rc * Rc * 2.1f, hs = 1.4f / S;
            int R = (int)math.ceil(Rc * 1.5f + 2);
            PL.Clear(); FAIL.Clear(); PR.Clear(); RING.Clear(); float Ro2 = Rc * Rc * 2.9f;
            float best = 0; byte zone = h.zone[h.Index(hc.x, hc.y, hc.z)];
            for (int dz = -R; dz <= R; dz++) for (int dy = -R; dy <= R; dy++) for (int dx = -R; dx <= R; dx++)
            {
                int x = hc.x + dx, y = hc.y + dy, z = hc.z + dz; if (!In(x, y, z)) continue;
                int i = h.Index(x, y, z); byte m = h.mat[i];
                if (!AnatMat.Cull(m) || m == AnatMat.Covered) continue;
                float al = -(dx * l.x + dy * l.y + dz * l.z); if (al < -hs) continue;
                float px = dx + al * l.x, py = dy + al * l.y, pz = dz + al * l.z, pr2 = px * px + py * py + pz * pz;
                if (pr2 > R2) { if (pr2 <= Ro2 && al < hs && Surface(m) && Exposed(x, y, z)) RING.Add((x, y, z, pr2)); continue; }
                if (!Exposed(x, y, z)) continue;
                float pp = math.exp(-pr2 / (Rc * Rc) * 1.4f) * math.min(1.5f, 1 + al / hs); if (pp < 0.04f) { if (Surface(m)) RING.Add((x, y, z, pr2)); continue; }
                PL.Add((i, x, y, z, pp)); PR.Add(pr2);
                if (pp > best && h.zone[i] != 0) { best = pp; zone = h.zone[i]; }
            }
            AnatProf.Lap(AnatProf.Spot);
            r.zone = zone; lastZone = zone; r.cells = PL.Count; r.entryMat = h.mat[h.Index(hc.x, hc.y, hc.z)];
            // травма зоны: 30% общей травмы головы добавляется к каждой зоне
            float add = E * (power ? 1 : 0.5f);
            trauma[zone] += add; traumaAll += add;
            float eff = Eff(zone); r.eff = eff;
            if (zone == AnatZone.Nose) noseAcc += E * (power ? 1 : 0.6f);

            byte now = T2(time);
            float c = -math.dot(n, dir), sl = math.sqrt(math.max(0, 1 - c * c));
            float vu = math.sqrt(AnatZone.Vuln[zone] / 100f);
            foreach (var q in PL)
            {
                byte m = h.mat[q.i]; float Ep = E * q.p; byte zq = h.zone[q.i]; if (zq == 0) zq = zone;
                var v = Get(q.x, q.y, q.z);
                if (Surface(m))
                {
                    float cf = boneCrush ? 0.45f + 1.55f * math.exp(-BoneDepth(q.x, q.y, q.z, l) / 0.55f) : 0.6f;   // раздавливание о кость
                    float ra = Ep * 40;
                    v.rt = Wt(v.rt, v.red + v.swell, now, ra); v.red = Add8(v.red, ra);
                    int cap = (int)(AnatZone.SwellCap[zq] / 0.005f);
                    if (v.swell < cap) v.swell = (byte)math.min(cap, v.swell + Ep * 2.6f * AnatZone.Swell[zq] / 100f * 2f);
                    float ba = Ep * 21 * (0.6f + 0.4f * math.min(cf, 1.5f));
                    v.bt = Wt(v.bt, v.bruise, now, ba); v.bruise = Add8(v.bruise, ba);
                    if (Ep > 1) v.abr = Add8(v.abr, (Ep - 1) * 22 * (0.25f + sl) * (0.35f + v.bruise / 255f) * (power ? 1 : 0.55f));
                    float loss = Ep * 0.3f * math.sqrt(AnatZone.Vuln[zq] / 100f) * cf * (0.55f + v.bruise / 255f + v.abr / 400f);
                    v.dmg = Add8(v.dmg, loss * 10);
                    Put(q.x, q.y, q.z, v);
                    if (v.dmg >= Thr(m)) FAIL.Add(new int3(q.x, q.y, q.z));
                }
                else if (Soft(m))
                {
                    float k = m == AnatMat.Cartilage ? 0.32f : m == AnatMat.Periost ? 0.3f : 0.42f;
                    v.dmg = Add8(v.dmg, Ep * k * 10);
                    float ba = Ep * 45; v.blt = Wt(v.blt, v.blood, now, ba); v.blood = Add8(v.blood, ba);
                    Put(q.x, q.y, q.z, v);
                    if (v.dmg >= Thr(m)) FAIL.Add(new int3(q.x, q.y, q.z));
                }
                else if (AnatMat.Class(m) == AnatMat.ClsBone)
                {
                    float ba = Ep * 15; v.blt = Wt(v.blt, v.blood, now, ba); v.blood = Add8(v.blood, ba); Put(q.x, q.y, q.z, v);
                }
            }
            // часть 5, доработка: синяк шире пятна удара — плоский профиль до ~1,7 Rc с затуханием к краю (кожа, волосы)
            if (Wide)
            {
                float eb = E * (power ? 1 : 0.5f) * 11f, r0 = Rc * 0.9f, r1 = Rc * 1.7f;
                for (int k = 0; k < PL.Count; k++)
                {
                    var q = PL[k]; if (!Surface(h.mat[q.i])) continue;
                    float a = eb * (1 - math.smoothstep(r0, r1, math.sqrt(PR[k]))); if (a < 0.5f) continue;
                    var v = Get(q.x, q.y, q.z); v.bt = Wt(v.bt, v.bruise, now, a); v.bruise = Add8(v.bruise, a); Put(q.x, q.y, q.z, v);
                }
                foreach (var q in RING)
                {
                    float a = eb * (1 - math.smoothstep(r0, r1, math.sqrt(q.r2))) * (0.7f + 0.3f * H3(q.x, q.y, q.z)); if (a < 0.5f) continue;
                    var v = Get(q.x, q.y, q.z); v.bt = Wt(v.bt, v.bruise, now, a); v.bruise = Add8(v.bruise, a); Put(q.x, q.y, q.z, v);
                }
            }
            AnatProf.Lap(AnatProf.Apply);
            // веки — рыхлая клетчатка: отёк от любого удара в область глазницы (бровь, скула, сам глаз) собирается в них,
            // сильнее при ударе ближе к глазу (до 4 см от центра шара)
            float3 hp = h.Local(hc.x, hc.y, hc.z);
            for (int s = 0; s < 2; s++)
            {
                float dc = math.length(hp - new float3(s == 0 ? AnatGeo.EX : -AnatGeo.EX, AnatGeo.EY, AnatGeo.EZ));
                float f = math.saturate(1 - (dc - 1.6f) / 2.4f); if (f <= 0) continue;
                int cap = (int)(AnatZone.SwellCap[AnatZone.Eye] / 0.005f); float la = E * f * (power ? 6 : 3);
                foreach (int i in lids[s])
                {
                    if (!Surface(h.mat[i])) continue;
                    var c3 = Cell(i); var v = Get(c3.x, c3.y, c3.z);
                    v.rt = Wt(v.rt, v.red + v.swell, now, la); v.swell = (byte)math.min(cap, v.swell + la); Put(c3.x, c3.y, c3.z, v);
                }
            }
            AnatProf.Lap(AnatProf.Lids);
            foreach (var f in FAIL)
            {
                int i = h.Index(f.x, f.y, f.z); byte m = h.mat[i]; if (!AnatMat.Cull(m)) continue;
                byte zq = h.zone[i]; if (zq == 0) zq = zone;
                Remove(f.x, f.y, f.z);
                r.removed++;
                if (Surface(m)) { r.split++; r.removed += Tear(f.x, f.y, f.z, E, zq, n); AddWound(f.x, f.y, f.z, AnatZone.Bleed[zq], WSkin, 22, "рана: " + AnatZone.Names[zq]); }
                else if (rng.NextDouble() < 0.3) AddWound(f.x, f.y, f.z, AnatZone.Bleed[zq] * 0.5f, WSkin, 22, "рана глубже кожи: " + AnatZone.Names[zq]);
            }
            AnatProf.Lap(AnatProf.Fail);
            Smear(dir, n);
            AnatProf.Lap(AnatProf.Smear);
            r.macer = Macerate(zone, eff, h.Local(hc.x, hc.y, hc.z), E, power);
            AnatProf.Lap(AnatProf.Macer);
            if (power) Break(hc, n, l, dir, E, zone, eff);
            if (noseAcc > 3 && zone == AnatZone.Nose)
                for (int s = 0; s < 2; s++) if (nostril[s].x >= 0) AddWound(nostril[s].x, nostril[s].y, nostril[s].z, 2.2f + noseAcc * 0.25f, WNose, 45, "кровь из носа");
            AnatProf.Lap(AnatProf.Break);
            sw.Stop(); r.ms = (float)sw.Elapsed.TotalMilliseconds; hitMs += r.ms; hitMsMax = math.max(hitMsMax, r.ms);
            SyncVis();
            AnatProf.Lap(AnatProf.DmgVis);
            return r;
        }

        /// Порог разрушения ×10 (кожа головы с волосами — как кожа).
        static int Thr(byte m) => (int)math.min(255, 10 * (m == AnatMat.Hair || m == AnatMat.Brow ? AnatMat.Table[AnatMat.Skin].strength : AnatMat.Table[m].strength));

        public float Eff(byte zone) => trauma[zone] + (traumaAll - trauma[zone]) * 0.3f;

        /// Убрать ячейку снаружи (пробой в просмотре, поиск кусков): с кровью и сосудами, как при разрушении ударом.
        public void Cut(int x, int y, int z) { if (h.mat[h.Index(x, y, z)] != AnatMat.Empty) Remove(x, y, z); }
        public void Flush() => SyncVis();

        /// Убрать ячейку: открывается следующий слой; соседи в крови; открытый сосуд — источник.
        void Remove(int x, int y, int z)
        {
            int i = h.Index(x, y, z); byte m = h.mat[i], part = h.part[i];
            if (seenMat.Add(h.zone[i] * 64 + m)) exposed.Add((time, AnatMat.Table[m].name, h.zone[i]));   // первое появление материала в зоне
            cutLog.Add(i); AnatProf.removes++;
            SetMat(i, AnatMat.Empty); if (st.Idx(x, y, z, false) >= 0) st.Set(x, y, z, default);
            opened.Add(i);
            removed++;
            Dirty(x, y, z);
            byte now = T2(time);
            for (int d = 0; d < 8; d++)
            {
                int xx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), yy = y + (d == 2 ? 1 : d == 3 ? -1 : d == 6 ? -2 : 0), zz = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
                if (d == 7) { yy = y - 1; xx = x; zz = z; }
                byte nm = M(xx, yy, zz);
                if (nm == AnatMat.Artery || nm == AnatMat.Vein) OpenVessel(h.part[h.Index(xx, yy, zz)], xx, yy, zz);
                if (!AnatMat.Cull(nm) || nm == AnatMat.Covered) continue;
                var v = Get(xx, yy, zz); float ba = d == 3 || d == 7 ? 90 : d == 6 ? 60 : 55;
                v.blt = Wt(v.blt, v.blood, now, ba); v.blood = Add8(v.blood, ba); Put(xx, yy, zz, v);
            }
            if (m == AnatMat.Artery || m == AnatMat.Vein) OpenVessel(part, x, y, z);
        }

        void OpenVessel(byte part, int x, int y, int z)
        {
            int key = part * 2 + (h.Local(x, y, z).x < 0 ? 1 : 0);
            if (!vessels.Add(key)) return;
            string nm = AnatPart.Name(part);
            switch (part)
            {
                case AnatPart.SupTemporalA: AddWound(x, y, z, 7, WJet, 120, "струя: " + nm); break;
                case AnatPart.FacialA: case AnatPart.CarotidA: case AnatPart.InfAlveolarA: case AnatPart.MiddleMeningealA: AddWound(x, y, z, 5, WJet, 90, "струя: " + nm); break;
                case AnatPart.NasalMucosaV: AddWound(x, y, z, 4, WNose, 50, "кровь из носа: " + nm); break;
                default: AddWound(x, y, z, 4, WVein, 50, "вена: " + nm); break;
            }
            if (CanLog()) events.Add(F1(time) + " с: вскрыт сосуд — " + nm);
        }

        /// Разрыв кожи линией: у брови и скулы — вдоль костного края (горизонтально), иначе поперёк волокон мышцы под кожей
        /// (линии натяжения: у рта вертикально, на лбу горизонтально); без мышцы — как в вебе по зоне.
        int Tear(int x, int y, int z, float E, byte zone, float3 n)
        {
            float3 p = h.Local(x, y, z), t;
            float jit = (float)(rng.NextDouble() * 2 - 1);
            if (zone == AnatZone.Brow || zone == AnatZone.CheekBone) t = new float3(1, jit * 0.25f, 0);
            else
            {
                byte mus = 0;
                for (int k = 1; k <= 10 && mus == 0; k++) { int i = h.Lookup(p - n * (k * S)); if (i >= 0 && AnatPart.IsMuscle(h.part[i])) mus = h.part[i]; }
                if (mus != 0) { float3 f = AnatGeo.Fiber(mus, p); t = math.cross(n, f); if (math.lengthsq(t) < 0.04f) t = new float3(1, 0, 0); t += new float3(jit, jit, 0) * 0.15f; }
                else if (zone == AnatZone.Mouth) t = new float3(0.25f, 1, 0);
                else if (zone == AnatZone.Cheek || zone == AnatZone.Jaw) { float an = jit * 0.9f; t = new float3(math.cos(an), math.sin(an), 0); }
                else { float an = (float)(rng.NextDouble() * math.PI); t = new float3(math.cos(an), math.sin(an), 0); }
            }
            t -= n * math.dot(t, n); t = math.normalizesafe(t, new float3(1, 0, 0));
            lastTearDir = t; lastTearZone = zone;
            int steps = (int)math.round((1 + E * 0.35f + rng.NextDouble()) * 1.65f), rem = 0;
            var line = new List<float3> { p };
            for (int sg = -1; sg <= 1; sg += 2)
            {
                float3 cc = new float3(x, y, z) + 0.5f;
                for (int s = 0; s < steps; s++)
                {
                    cc += t * sg; int3 bc = new int3(-1); int bh = 1000;
                    for (int c3 = -1; c3 <= 1; c3++) for (int b = -1; b <= 1; b++) for (int a2 = -1; a2 <= 1; a2++)
                    {
                        int xx = (int)cc.x + a2, yy = (int)cc.y + b, zz = (int)cc.z + c3; byte m = M(xx, yy, zz);
                        if (!Surface(m) || !Exposed(xx, yy, zz)) continue;
                        var v = Get(xx, yy, zz); if (v.dmg < Thr(m) * 0.28f) continue;
                        int hp = Thr(m) - v.dmg; if (hp < bh) { bh = hp; bc = new int3(xx, yy, zz); }
                    }
                    if (bc.x < 0) break;
                    Remove(bc.x, bc.y, bc.z); rem++; cc = (float3)bc + 0.5f; line.Add(h.Local(bc.x, bc.y, bc.z));
                }
            }
            // края раны расходятся поперёк линии (кожа и жир в 4 ячейках), до 0,6 мм
            float3 perp = math.normalizesafe(math.cross(n, t));
            foreach (var lp in line)
            {
                int3 c0 = (int3)math.floor((lp - h.org) / S);
                for (int dz = -4; dz <= 4; dz++) for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++)
                {
                    int xx = c0.x + dx, yy = c0.y + dy, zz = c0.z + dz; byte m = M(xx, yy, zz);
                    if (!Surface(m) && m != AnatMat.Fat) continue;
                    float3 d = h.Local(xx, yy, zz) - lp; float dist = math.length(d) / S; if (dist > 4) continue;
                    float side = math.sign(math.dot(d, perp)); if (side == 0) continue;
                    var v = Get(xx, yy, zz); float g = 6 * (1 - dist / 4) * side;
                    v.dx = (sbyte)math.clamp(v.dx + perp.x * g, -60, 60); v.dy = (sbyte)math.clamp(v.dy + perp.y * g, -60, 60); v.dz = (sbyte)math.clamp(v.dz + perp.z * g, -60, 60);
                    Put(xx, yy, zz, v);
                }
            }
            if (CanLog()) events.Add(F1(time) + " с: рассечение — " + AnatZone.Names[zone] + ", " + (line.Count) + " ячеек кожи, направление (" + F1(t.x) + ", " + F1(t.y) + ", " + F1(t.z) + ")");
            return rem;
        }

        /// Кровь в пятне размазывается по направлению удара (касательная часть), часть уходит на перчатку.
        void Smear(float3 dir, float3 n)
        {
            float3 t = dir - n * math.dot(dir, n); if (math.length(t) < 0.2f) return;
            t = math.normalize(t);
            foreach (var q in PL)
            {
                if (q.p < 0.2f || !AnatMat.Cull(h.mat[q.i])) continue;
                var v = Get(q.x, q.y, q.z); if (v.blood < 40) continue;
                int3 tc = Nearest(h.Local(q.x, q.y, q.z) + t * (3 * S), 2, false); if (tc.x < 0) continue;
                float give = v.blood * 0.45f;
                var w = Get(tc.x, tc.y, tc.z); w.blt = Wt(w.blt, w.blood, v.blt, give); w.blood = Add8(w.blood, give * 0.75f); Put(tc.x, tc.y, tc.z, w);
                v.blood = Add8(v.blood, -give); Put(q.x, q.y, q.z, v);
            }
        }

        // ---------- размозжение ----------
        bool Macerate(byte zone, float eff, float3 c, float E, bool power)
        {
            bool did = false;
            if (eff > 7 && (power || rng.NextDouble() < 0.4)) { Pulp(c, power ? 1.5f : 0.9f, power ? 0.32f : 0.18f); did = true; Once(zone, 0, "каша (травма зоны > 7)"); }
            if (power && eff > 14) { Strip(c, 1.4f + math.min(1.4f, E * 0.17f)); did = true; Once(zone, 1, "мясо до кости (> 14)"); }
            if (power && eff > 24) { Chips(c, 0.9f + math.min(0.9f, E * 0.1f)); did = true; Once(zone, 2, "сколы кости (> 24), перелом"); }
            if (power && eff > 42) Once(zone, 3, "порог разрыва (> 42)");
            return did;
        }

        void Once(byte zone, int k, string what) { if (macerDone[zone * 4 + k]) return; macerDone[zone * 4 + k] = true; if (CanLog()) events.Add(F1(time) + " с: " + AnatZone.Names[zone] + " — " + what); }

        IEnumerable<int3> Ball(float3 c, float R)
        {
            int3 lo = math.max((int3)math.floor((c - R - h.org) / S), 0), hi = math.min((int3)math.ceil((c + R - h.org) / S), h.dims - 1);
            for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
                if (math.lengthsq(h.Local(x, y, z) - c) <= R * R) yield return new int3(x, y, z);
        }

        /// Каша: открытая мягкая ткань рвётся клочьями, остальное размозжено и пропитано кровью.
        void Pulp(float3 c, float R, float amt)
        {
            var L = new List<int3>();
            foreach (var q in Ball(c, R)) { byte m = M(q.x, q.y, q.z); if ((Surface(m) || Soft(m)) && Exposed(q.x, q.y, q.z)) L.Add(q); }
            byte now = T2(time);
            foreach (var q in L)
            {
                byte m = M(q.x, q.y, q.z); if (!AnatMat.Cull(m)) continue;
                double r = rng.NextDouble();
                // кожа в каше не исчезает целиком: отходит только уже надорванная (урон не меньше половины порога), остальная — ссадина
                bool sk = Surface(m);
                if (r < amt * (sk ? 1 : 0.8f) && (!sk || Get(q.x, q.y, q.z).dmg >= Thr(m) / 2)) { Remove(q.x, q.y, q.z); continue; }
                var v = Get(q.x, q.y, q.z);
                if (Surface(m)) v.abr = 255; else SetMat(h.Index(q.x, q.y, q.z), AnatMat.Crushed);
                v.blt = Wt(v.blt, v.blood, now, 200); v.blood = Add8(v.blood, 200); Put(q.x, q.y, q.z, v);
            }
        }

        /// До кости: всё мягкое покрытие, достижимое от поверхности без прохода сквозь кость, снимается; надкостница — частично.
        void Strip(float3 c, float R)
        {
            var seen = new HashSet<int>(); var q = new Queue<int3>();
            foreach (var p in Ball(c, R)) { byte m = M(p.x, p.y, p.z); if ((Surface(m) || Soft(m)) && Exposed(p.x, p.y, p.z)) { q.Enqueue(p); seen.Add(h.Index(p.x, p.y, p.z)); } }
            var L = new List<int3>();
            while (q.Count > 0)
            {
                var p = q.Dequeue(); L.Add(p);
                for (int d = 0; d < 6; d++)
                {
                    int3 nb = p + new int3(d == 0 ? 1 : d == 1 ? -1 : 0, d == 2 ? 1 : d == 3 ? -1 : 0, d == 4 ? 1 : d == 5 ? -1 : 0);
                    if (!In(nb.x, nb.y, nb.z)) continue; int i = h.Index(nb.x, nb.y, nb.z); byte m = h.mat[i];
                    if (seen.Contains(i) || !(Surface(m) || Soft(m)) || m == AnatMat.OrbFat || m == AnatMat.Nerve || m == AnatMat.Mucosa) continue;
                    if (AnatPart.IsMuscle(h.part[i]) && h.part[i] >= AnatPart.RectusSup && h.part[i] <= AnatPart.ObliqueInf) continue;
                    if (math.lengthsq(h.Local(nb.x, nb.y, nb.z) - c) > R * R) continue;
                    seen.Add(i); q.Enqueue(nb);
                }
            }
            foreach (var p in L) { byte m = M(p.x, p.y, p.z); if (m == AnatMat.Periost && rng.NextDouble() > 0.7) continue; Remove(p.x, p.y, p.z); }
        }

        /// Сколы: верхний слой открытой компакты выбит, видна губчатая кость (сами летящие куски — этап C).
        void Chips(float3 c, float R)
        {
            var L = new List<int3>();
            foreach (var p in Ball(c, R)) if (M(p.x, p.y, p.z) == AnatMat.Compact && Exposed(p.x, p.y, p.z)) L.Add(p);
            foreach (var p in L) if (rng.NextDouble() < 0.5) Remove(p.x, p.y, p.z);
        }

        // ---------- кровь ----------
        void AddWound(int x, int y, int z, float rate, byte kind, float tau, string src)
        {
            int3 c = Nearest(h.Local(x, y, z), 3, true); if (c.x < 0) return;
            foreach (var w in wounds)
                if (w.kind == kind && math.abs(w.x - c.x) + math.abs(w.y - c.y) + math.abs(w.z - c.z) < 10)
                { w.rate = math.min(9, w.rate * math.exp(-w.age / w.tau) + rate * 0.7f); w.age = 0; return; }
            if (wounds.Count >= MaxWounds)
            {
                int k = wounds.FindIndex(w => w.kind == WSkin); wounds.RemoveAt(k >= 0 ? k : 0);   // вытесняется старая мелкая рана
            }
            wounds.Add(new Wound { x = c.x, y = c.y, z = c.z, rate = rate, tau = tau, kind = kind, acc = (float)rng.NextDouble(), n = Normal(c.x, c.y, c.z), src = src });
            peakWounds = math.max(peakWounds, wounds.Count);
        }

        void StartDrip(int3 c, float vol)
        {
            if (drips.Count >= MaxDrips || c.x < 0) return;
            drips.Add(new Drip { x = c.x, y = c.y, z = c.z, vol = vol, t = (float)rng.NextDouble() });
            peakDrips = math.max(peakDrips, drips.Count);
        }

        public void Step(float dt)
        {
            var sw = stepClock; sw.Restart();
            time += dt;
            for (int k = wounds.Count - 1; k >= 0; k--)
            {
                var w = wounds[k]; w.age += dt; float r = w.rate * math.exp(-w.age / w.tau);
                if (r < 0.05f) { wounds.RemoveAt(k); continue; }
                byte wm = M(w.x, w.y, w.z);
                if (!AnatMat.Cull(wm) || wm == AnatMat.Covered || !Exposed(w.x, w.y, w.z))
                {
                    // ячейку раны убрали следующим ударом или она ушла вглубь — рана переходит на ближайшую открытую ткань
                    int3 c = Nearest(h.Local(w.x, w.y, w.z), 3, true);
                    if (c.x < 0) { wounds.RemoveAt(k); continue; }
                    w.x = c.x; w.y = c.y; w.z = c.z; w.n = Normal(c.x, c.y, c.z);
                }
                if (w.kind == WJet)
                {
                    // пульс ~100 в минуту: на каждый удар сердца — выброс капель; между ударами — подтекание
                    float b0 = w.beat; w.beat += dt * 1.7f;
                    if ((int)w.beat != (int)b0)
                    {
                        int nd = 3 + rng.Next(4); float sp = 55 + 60 * math.sqrt(math.saturate(r / 7));
                        float3 up = new float3(0, 1, 0), side = math.normalizesafe(math.cross(w.n, up), new float3(1, 0, 0));
                        for (int q = 0; q < nd && drops.Count < MaxDrops; q++)
                        {
                            float3 v = w.n * sp * (0.8f + 0.4f * (float)rng.NextDouble()) + up * sp * 0.35f + side * ((float)rng.NextDouble() * 2 - 1) * 18;
                            drops.Add(new Drop { p = h.Local(w.x, w.y, w.z) + w.n * 0.2f, v = v, vol = 6 + rng.Next(8) });
                        }
                        peakDrops = math.max(peakDrops, drops.Count);
                    }
                    w.acc += r * 0.3f * dt;
                }
                else w.acc += r * dt;
                while (w.acc >= 1) { w.acc -= 1; StartDrip(new int3(w.x, w.y, w.z), 16 + rng.Next(30)); }
            }
            byte now = T2(time);
            for (int k = drips.Count - 1; k >= 0; k--)
            {
                var d = drips[k]; d.age += dt; d.t += dt * 28 * math.exp(-d.age / 45f);     // ~5,6 см/с, густеет за минуту
                bool alive = true;
                while (d.t >= 1 && alive) { d.t -= 1; alive = StepDrip(d, now); }
                if (!alive || d.age > 90) drips.RemoveAt(k);
            }
            for (int k = drops.Count - 1; k >= 0; k--)
            {
                var d = drops[k]; float3 p0 = d.p;
                d.v += grav * 981f * dt; d.p += d.v * dt;
                // проверить путь по ячейкам: первая сплошная — садится
                bool done = false; int ns = (int)math.ceil(math.length(d.p - p0) / (S * 0.7f));
                for (int s = 1; s <= ns && !done; s++)
                {
                    float3 q = math.lerp(p0, d.p, s / (float)ns); int i = h.Lookup(q);
                    if (i < 0) { if (math.any(q < h.org - 2) || math.any(q > h.org + (float3)h.dims * S + 2)) done = true; continue; }
                    byte m = h.mat[i]; if (!AnatMat.Cull(m)) continue;
                    done = true; if (m == AnatMat.Covered) break;
                    int3 c = Nearest(math.lerp(p0, d.p, (s - 1) / (float)ns), 2, false); if (c.x < 0) break;
                    var v = Get(c.x, c.y, c.z); float ba = 60 + d.vol * 8; v.blt = Wt(v.blt, v.blood, now, ba); v.blood = Add8(v.blood, ba); Put(c.x, c.y, c.z, v);
                    StartDrip(c, d.vol * 1.5f);
                }
                if (done || math.length(d.p) > 60) drops.RemoveAt(k); else drops[k] = d;
            }
            eyeT += dt; if (eyeT >= 0.25f) { eyeT = 0; EyeCheck(); }
            SyncVis();
            sw.Stop(); stepMs += sw.Elapsed.TotalMilliseconds; stepMsMax = math.max(stepMsMax, sw.Elapsed.TotalMilliseconds);
        }

        bool StepDrip(Drip d, byte now)
        {
            byte m0 = M(d.x, d.y, d.z); if (!AnatMat.Cull(m0) || m0 == AnatMat.Covered) return false;
            var v = Get(d.x, d.y, d.z); float ba = 20 + d.vol * 3; v.blt = Wt(v.blt, v.blood, now, ba); v.blood = Add8(v.blood, ba); Put(d.x, d.y, d.z, v);
            if (Wide && spreadCells < MaxSpread) Spread(d, ba, now);
            if (--d.vol <= 0) return false;
            float best = 0.12f; int3 bc = new int3(-1);
            for (int c = -1; c <= 1; c++) for (int b = -1; b <= 1; b++) for (int a2 = -1; a2 <= 1; a2++)
            {
                if (a2 == 0 && b == 0 && c == 0) continue;
                int xx = d.x + a2, yy = d.y + b, zz = d.z + c; if (xx == d.px && yy == d.py && zz == d.pz) continue;
                byte m = M(xx, yy, zz); if (!AnatMat.Cull(m) || m == AnatMat.Covered || !Exposed(xx, yy, zz)) continue;
                float sc = (a2 * grav.x + b * grav.y + c * grav.z) / math.sqrt(a2 * a2 + b * b + c * c) + (float)rng.NextDouble() * 0.32f;
                if (sc > best) { best = sc; bc = new int3(xx, yy, zz); }
            }
            if (bc.x < 0)
            {
                // отрывается каплей (подбородок, кончик носа)
                if (drops.Count < MaxDrops) { drops.Add(new Drop { p = h.Local(d.x, d.y, d.z) + grav * 0.25f, v = grav * 15, vol = d.vol * 0.5f }); peakDrops = math.max(peakDrops, drops.Count); }
                return false;
            }
            d.px = d.x; d.py = d.y; d.pz = d.z; d.x = bc.x; d.y = bc.y; d.z = bc.z;
            return true;
        }

        /// Часть 5, доработка: кровь подтёка расходится вбок и вниз по открытой поверхности (радиус 1–3 ячейки по объёму потока),
        /// неровно — пропуски и вес по хешу координат (rng не трогается). Предел — MaxSpread новых ячеек за бой.
        void Spread(Drip d, float ba, byte now)
        {
            int rw = d.vol > 18 ? 3 : d.vol > 6 ? 2 : 1;
            for (int c = -rw; c <= rw; c++) for (int b = -rw; b <= rw; b++) for (int a2 = -rw; a2 <= rw; a2++)
            {
                if (a2 == 0 && b == 0 && c == 0) continue;
                int xx = d.x + a2, yy = d.y + b, zz = d.z + c; if (!In(xx, yy, zz)) continue;
                float len = math.sqrt(a2 * a2 + b * b + c * c), up = (a2 * grav.x + b * grav.y + c * grav.z) / len;
                if (up < -0.35f) continue;   // вверх по стене кровь не идёт
                float hh = H3(xx, yy, zz); if (hh < 0.15f) continue;
                byte m = M(xx, yy, zz); if (!AnatMat.Cull(m) || m == AnatMat.Covered || !Exposed(xx, yy, zz)) continue;
                float w = (1 - len / (rw + 1.5f)) * (0.5f + 0.5f * hh) * (up > 0.3f ? 1.1f : 0.85f);
                var u = Get(xx, yy, zz); if (u.blood == 0) { if (spreadCells >= MaxSpread) return; spreadCells++; }
                u.blt = Wt(u.blt, u.blood, now, ba * w); u.blood = Add8(u.blood, ba * w); Put(xx, yy, zz, u);
            }
        }

        // ---------- глаз ----------
        public float LidSwell(int s)
        {
            if (lids[s].Count == 0) return 0;
            double sum = 0; int n = 0;
            foreach (int i in lids[s])
            {
                if (!Surface(h.mat[i])) continue;
                int x = i % h.dims.x, y = (i / h.dims.x) % h.dims.y, z = i / (h.dims.x * h.dims.y);
                var v = Get(x, y, z); float grow = 1 - math.exp(-math.max(0, time - v.rt * 2f) / 4f);
                sum += v.swell * 0.005f * grow; n++;
            }
            return n == 0 ? 0 : (float)(sum / n);
        }

        void EyeCheck()
        {
            for (int s = 0; s < 2; s++)
            {
                if (eyeClosed[s]) continue;
                float sw = LidSwell(s); if (sw <= 0.36f) continue;
                eyeClosed[s] = true;
                // веки смыкаются: щель перед шаром заполняется отёчной кожей
                float3 gc = new float3(s == 0 ? AnatGeo.EX : -AnatGeo.EX, AnatGeo.EY, AnatGeo.EZ);
                float br = 0, rd = 0; int n = 0;
                foreach (int i in lids[s]) { int x = i % h.dims.x, y = (i / h.dims.x) % h.dims.y, z = i / (h.dims.x * h.dims.y); var v = Get(x, y, z); br += v.bruise; rd += v.red; n++; }
                br /= math.max(1, n); rd /= math.max(1, n);
                int filled = 0; byte now = T2(time);
                foreach (var q in Ball(gc, AnatGeo.ER + 0.36f))
                {
                    int i = h.Index(q.x, q.y, q.z); if (!Open(h.mat[i])) continue;
                    float3 p = h.Local(q.x, q.y, q.z); float d = math.length(p - gc);
                    if (d < AnatGeo.ER - 0.1f || p.z < AnatGeo.EZ || math.abs(p.x - gc.x) > 1.55f || math.abs(p.y - gc.y) > 0.95f) continue;
                    SetMat(i, AnatMat.Skin); h.part[i] = 0; h.zone[i] = AnatZone.Eye; closed = true;
                    Put(q.x, q.y, q.z, new VS { swell = (byte)math.min(255, sw / 0.005f * 0.5f), rt = now, bruise = (byte)br, bt = now, red = (byte)rd });
                    filled++;
                }
                if (CanLog()) events.Add(F1(time) + " с: " + (s == 0 ? "левый" : "правый") + " глаз закрыт отёком века (средний отёк " + F1(sw * 10) + " мм, " + filled + " ячеек)");
            }
        }

        static string F1(float v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// Видимость после изменений: от удалённых ячеек (в том же кадре), после закрытия — заново.
        void SyncVis()
        {
            if (a.vis == null || (opened.Length == 0 && !closed)) return;
            if (closed) a.vis.Full(); else a.vis.Opened(opened.AsArray());
            opened.Clear(); closed = false;
            visMs += a.vis.lastMs; visMsMax = math.max(visMsMax, a.vis.lastMs);
        }

        public void Dispose() { st.Dispose(); if (opened.IsCreated) opened.Dispose(); if (cutLog.IsCreated) cutLog.Dispose(); }

        // ---------- часть 2: переломы, мозг, разрыв ----------
        int3 CellOf(float3 p) => (int3)math.floor((p - h.org) / S);

        /// Убрать ячейку без крови (её забрал оторванный кусок).
        public void Take(int i)
        {
            if (h.mat[i] == AnatMat.Empty) return;
            int x = i % h.dims.x, y = (i / h.dims.x) % h.dims.y, z = i / (h.dims.x * h.dims.y);
            SetMat(i, AnatMat.Empty); if (st.Idx(x, y, z, false) >= 0) st.Set(x, y, z, default);
            opened.Add(i); removed++; Dirty(x, y, z); AnatProf.takes++;
        }

        /// Мощный удар по кости и в пролом: оболочка и мозг в проломе, перелом кольцом (травма зоны > 24), разрыв (> 42).
        void Break(int3 hc, float3 n, float3 l, float3 dir, float E, byte zone, float eff)
        {
            float3 hp = h.Local(hc.x, hc.y, hc.z); byte em = h.mat[h.Index(hc.x, hc.y, hc.z)];
            if (em == AnatMat.Dura || em == AnatMat.Csf) TearDura(hp, 1.0f + 0.1f * E);
            else if (Brainy(em))
            {
                // мягкий мозг: удар вырывает кусок с корой снаружи и белым веществом на изломе
                brainHits++; TearDura(hp, 1.2f);
                ejects.Add(new Eject { c = hp + dir * 0.4f, dir = math.normalizesafe(n - dir * 0.3f), r = 0.6f + 0.1f * E });
            }
            if (eff > 24) Fracture(hp, l, E, zone);
            // разрыв: травма зоны > 42 (как в вебе) или общая травма головы > 90 — дальше каждый мощный удар рвёт голову
            if (eff > 42 || traumaAll > 90) { float R = 1.6f + 0.3f * E + math.min(0.8f, traumaAll / 400f); AnatProf.Lap(AnatProf.Break); Rip(hp + dir * R * 0.4f, R); AnatProf.Lap(AnatProf.Rip); }
        }

        /// Перелом: кость под ударом раскалывается по кольцу (неровному) вместе с мягкими тканями над ним; оболочка под отломком
        /// рвётся, ликвор в бороздах под ним вытекает. Отломок со скальпом становится островом и отрывается (AnatIslands).
        void Fracture(float3 hp, float3 l, float E, byte zone)
        {
            float3 B = 0; bool got = false;
            for (int k = 0; k < 20 && !got; k++) { int i = h.Lookup(hp + l * (k * S)); if (i < 0) break; byte m = h.mat[i]; if (m == AnatMat.Compact || m == AnatMat.Spongy) { B = hp + l * (k * S); got = true; } }
            if (!got || Protected(B)) return;
            foreach (var f in fractures) if (math.distance(f, B) < 1.3f) return;   // здесь уже сломано — дальше работает разрыв
            fractures.Add(B);
            float Rf = 1.3f + 0.12f * E; fracAxis.Add(new float4(l, Rf));
            float3 u = math.normalize(math.cross(l, math.abs(l.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0))), w = math.cross(l, u);
            float ph = (float)rng.NextDouble() * 6.28f, ph2 = (float)rng.NextDouble() * 6.28f;
            int3 bc = CellOf(B); int Rc = (int)math.ceil((Rf * 1.25f + 3f) / S); int cut = 0;
            for (int dz = -Rc; dz <= Rc; dz++) for (int dy = -Rc; dy <= Rc; dy++) for (int dx = -Rc; dx <= Rc; dx++)
            {
                int x = bc.x + dx, y = bc.y + dy, z = bc.z + dz; if (!In(x, y, z)) continue;
                int i = h.Index(x, y, z); byte m = h.mat[i]; if (!AnatMat.Cull(m) || m == AnatMat.Covered || Brainy(m)) continue;
                // часть 4: дешёвый отсев по геометрии до Protected и тригонометрии (кольцо и отломок не шире Rf·1,21 + 0,17)
                float3 p = h.Local(x, y, z); float3 q = p - B; float al = math.dot(q, l); if (al < -2.6f || al > 3.5f) continue;
                bool soft = m == AnatMat.Dura || m == AnatMat.Csf; if (al >= 1.6f && !soft) continue;   // часть 6: кольцо — только при al < 1,6, ниже отломка — только оболочка и ликвор
                float3 rv = q - l * al; float rad = math.length(rv); if (rad > Rf * 1.25f + 0.2f || Protected(p)) continue;
                float ang = math.atan2(math.dot(rv, w), math.dot(rv, u));
                float R = Rf * (1 + 0.14f * math.sin(ang * 3 + ph) + 0.07f * math.sin(ang * 7 + ph2));
                bool ring = math.abs(rad - R) < 0.17f && al < 1.6f;
                bool under = rad < R && al > 0 && (m == AnatMat.Dura || m == AnatMat.Csf);
                if (ring || under) { Remove(x, y, z); cut++; }
            }
            if (CanLog()) events.Add(F1(time) + " с: перелом — " + AnatZone.Names[zone] + ", отломок ~" + F1(Rf * 2) + " см, трещина " + cut + " ячеек");
        }

        /// Оболочка и ликвор в шаре: оболочка рвётся, ликвор вытекает — открываются извилины коры с бороздами.
        void TearDura(float3 c, float R)
        {
            int3 lo = math.max(CellOf(c - R), 0), hi = math.min(CellOf(c + R), h.dims - 1);
            for (int pass = 0; pass < 3; pass++)
                for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
                {
                    byte m = h.mat[h.Index(x, y, z)]; if (m != AnatMat.Dura && m != AnatMat.Csf) continue;
                    float3 p = h.Local(x, y, z); if (math.lengthsq(p - c) > R * R || Protected(p)) continue;
                    if (m == AnatMat.Dura || Exposed(x, y, z)) Remove(x, y, z);
                }
        }

        /// Разрыв: всё в неровном шаре (кость тоже) уходит; культя ниже StumpAt не трогается. AnatView добавляет другие тела
        /// в том же шаре и полный поиск кусков.
        public void Rip(float3 c, float R)
        {
            rips.Add(new float4(c, R)); ripCount++;
            int3 lo = math.max(CellOf(c - R * 1.25f), 0), hi = math.min(CellOf(c + R * 1.25f), h.dims - 1);
            float ph = (float)rng.NextDouble() * 6.28f; int cut = 0;
            for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
            {
                int i = h.Index(x, y, z); byte m = h.mat[i]; if (!AnatMat.Cull(m)) continue;
                // часть 4: дешёвый отсев до Protected и синусов (неровный шар не шире R·1,28)
                float3 p = h.Local(x, y, z); float3 d = p - c; float r = math.length(d); if (r > R * 1.3f || Protected(p)) continue;
                // часть 6: неровный шар не уже R·0,72 (0,18 + 0,1) — ближе R·0,71 ячейка внутри без синусов
                if (r >= R * 0.71f)
                {
                    float Rn = R * (1 + 0.18f * math.sin(d.x * 2.1f + ph) * math.sin(d.y * 1.7f - ph) + 0.1f * math.sin(d.z * 3.3f + ph * 2));
                    if (r > Rn) continue;
                }
                // разрыв убирает без пропитки соседей кровью: иначе срез (культя) сплошь красный и слоёв не видно;
                // кровь — пятнами на краю и из вскрытых сосудов
                byte part = h.part[i];
                Take(i); cutLog.Add(i); cut++;
                if (seenMat.Add(h.zone[i] * 64 + m)) exposed.Add((time, AnatMat.Table[m].name, h.zone[i]));
                if (m == AnatMat.Artery || m == AnatMat.Vein) OpenVessel(part, x, y, z);
            }
            // край: кровь пятнами на трети открытых ячеек
            byte now = T2(time);
            for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
            {
                byte m = h.mat[h.Index(x, y, z)]; if (!AnatMat.Cull(m) || m == AnatMat.Covered) continue;
                float3 d = h.Local(x, y, z) - c; if (math.lengthsq(d) > R * R * 1.6f || rng.NextDouble() > 0.33 || !Exposed(x, y, z)) continue;
                var v = Get(x, y, z); float ba = 40 + 60 * (float)rng.NextDouble(); v.blt = Wt(v.blt, v.blood, now, ba); v.blood = Add8(v.blood, ba); Put(x, y, z, v);
            }
            if (CanLog()) events.Add(F1(time) + " с: разрыв — шар " + F1(R) + " см, " + cut + " ячеек");
        }
    }
}
