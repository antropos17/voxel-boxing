using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxProbe
{
    /// Оторванные куски (этап B, часть 2): своя маленькая решётка с материалами, частями, зонами и состояниями (кровь, синяк),
    /// своё тело с сеткой (AnatBody, без изгиба шеи — номер тела ≥ 100) и простая физика твёрдого тела в рамке корня головы (см):
    /// тяжесть, пол, отскок с потерей, трение, засыпание. Куски не сталкиваются друг с другом и с головой.
    /// Пределы: не больше MaxPieces кусков и MaxCells ячеек во всех кусках; лишние — самые старые — убираются.
    public class AnatPiece
    {
        public AnatGrid g; public AnatState st; public AnatBody body;
        public float3 com;                 // центр масс в рамке решётки куска (см)
        public float3 x, v, w;             // положение центра масс в рамке корня (см), скорость (см/с), угловая скорость (рад/с)
        public quaternion q = quaternion.identity;
        public float age, still; public bool asleep, ready; public int cells; public string what; public int id;
        public float3 ext;                 // полуразмер решётки, см
        public int3 lo; public float3 sum;  // сборка по частям: угол рамки в ячейках источника, сумма центров ячеек
        // висит на упругой нити от места отрыва (покой головы, см): длина, срок до разрыва
        public bool teth; public float3 anchor; public float tethLen, tethLife;
    }

    public class AnatPieces
    {
        public const int MaxPieces = 24, MaxCells = 900000;
        public const float Floor = -26f;   // пол, см рамки головы (низ шеи на −20)
        // Висящие куски (по образцу веб-версии, p.teth): мягкий островок сначала висит на упругой нити от места отрыва
        // (нить идёт за головой и изгибом шеи), качается и рвётся по растяжению или по сроку. Флаг выключает целиком.
        public static bool Tethers = true;
        public const int MaxTethered = 6, TetherMaxCells = 6000;
        public const float TetherK = 900f, TetherC = 8f, TearOver = 8f, TetherVMax = 400f;   // 1/с², 1/с, см сверх длины, см/с
        public int tethered;   // висят сейчас
        public readonly List<AnatPiece> list = new List<AnatPiece>();
        readonly Transform root; readonly Material material;
        readonly AnatSpare spare;          // объекты и сетки чанков убранных кусков: новый кусок берёт их, а не создаёт
        public int made, trimmed, crumbs, peak; int nextId = 100;
        public int Cells { get { int c = 0; foreach (var p in list) c += p.cells; return c; } }

        public AnatPieces(Transform root, Material material) { this.root = root; this.material = material; if (root != null) spare = new AnatSpare(root); }

        /// Новый кусок из ячеек src (индексы решётки src). Позиция — как у головы в текущей позе (жёстко с черепом у точки центра).
        public AnatPiece Make(AnatGrid src, AnatState srcSt, NativeArray<int> cells, int from, int count, float3 vel, string what)
        {
            int3 mn = int.MaxValue, mx = int.MinValue;
            for (int k = 0; k < count; k++)
            {
                int i = cells[from + k]; int3 c = Cell(src, i);
                mn = math.min(mn, c); mx = math.max(mx, c);
            }
            var p = Begin(src, mn, mx);
            Fill(p, src, srcSt, cells, from, count);
            return End(p, src, vel, what);
        }

        /// Кусок по частям (большой остров разбирается по кадрам): Begin — решётка по рамке ячеек [mn, mx] источника,
        /// Fill — перенести часть ячеек (с частями, зонами и состояниями), End — тело с сеткой и физика (null — ячеек нет).
        public AnatPiece Begin(AnatGrid src, int3 mn, int3 mx)
        {
            int3 dims = mx - mn + 3;
            var g = new AnatGrid("piece", dims, src.step, src.org + (float3)(mn - 1) * src.step);
            return new AnatPiece { g = g, st = new AnatState(g, 1), lo = mn };
        }

        public void Fill(AnatPiece p, AnatGrid src, AnatState srcSt, NativeArray<int> cells, int from, int count)
        {
            var g = p.g; var st = p.st;
            for (int k = 0; k < count; k++)
            {
                int i = cells[from + k]; byte m = src.mat[i];
                if (m == AnatMat.Empty || m == AnatMat.Air || m == AnatMat.Covered) continue;
                int3 c = Cell(src, i), l = c - p.lo + 1; int j = g.Index(l.x, l.y, l.z);
                g.mat[j] = m; g.part[j] = src.part[i]; g.zone[j] = src.zone[i];
                if (srcSt != null) { var v = srcSt.Get(c.x, c.y, c.z); if (!v.Zero) st.Set(l.x, l.y, l.z, v); }
                p.sum += g.Local(l.x, l.y, l.z); p.cells++;
            }
        }

        public AnatPiece End(AnatPiece p, AnatGrid src, float3 vel, string what)
        {
            if (p.cells == 0) { Drop(p); return null; }
            var g = p.g;
            p.what = what; p.id = nextId++; p.v = vel; p.ext = (float3)g.dims * g.step * 0.5f;
            p.com = p.sum / p.cells;
            // в позе: кусок стартует там, где его видно (поворот черепа у центра масс)
            float3 rest = math.mul(src.rot, p.com) + src.pos;
            if (AnatSkin.on) { float3 posed = AnatSkin.Skin(rest, out float3x3 M); p.x = posed; p.q = new quaternion(M); }
            else p.x = rest;
            var rnd = new Unity.Mathematics.Random((uint)(p.id * 7919 + 13));
            p.w = rnd.NextFloat3(-6, 6) / math.max(1, math.length(p.ext) * 0.5f);
            if (Tethers && what == "кусок" && p.cells <= TetherMaxCells && Tethered() < MaxTethered)
            {
                p.teth = true; p.anchor = rest;
                p.tethLen = math.clamp(math.length(p.ext) * 0.6f, 0.8f, 3f);
                p.tethLife = math.lerp(0.6f, 1.6f, rnd.NextFloat());
            }
            if (root != null)
            {
                p.body = new AnatBody(g, root, material, p.id, spare) { cull = false, st = p.st };
                Place(p);
            }
            list.Add(p); made++;
            Trim(); peak = math.max(peak, list.Count);
            return p;
        }

        /// Бросить недостроенный кусок (Begin без End).
        public void Drop(AnatPiece p) { p.st.Dispose(); p.g.Dispose(); }

        static int3 Cell(AnatGrid g, int i) => new int3(i % g.dims.x, (i / g.dims.x) % g.dims.y, i / (g.dims.x * g.dims.y));

        /// Убрать лишние: сверх MaxPieces кусков или MaxCells ячеек — сначала самые старые.
        void Trim()
        {
            while (list.Count > MaxPieces || (list.Count > 1 && Cells > MaxCells)) { Kill(list[0]); list.RemoveAt(0); trimmed++; }
        }

        int Tethered() { int n = 0; foreach (var q in list) if (q.teth) n++; return n; }

        void Kill(AnatPiece p) { p.body?.Destroy(); p.st.Dispose(); p.g.Dispose(); }

        public void Clear() { foreach (var p in list) Kill(p); list.Clear(); }

        /// Физика: тяжесть, пол с отскоком и трением, засыпание. Сетка куска строится по бюджету в AnatView; пока её нет, кусок висит на месте.
        public void Step(float dt)
        {
            if (dt <= 0) return;
            int sub = math.max(1, (int)math.ceil(dt / 0.008f)); float h = dt / sub;
            tethered = 0;
            foreach (var p in list)
            {
                if (!p.ready && p.body != null) { p.ready = p.body.dirtyCount == 0; if (!p.ready) continue; }
                p.age += dt;   // с момента, когда сетка готова и кусок полетел
                if (p.asleep) continue;
                // точка крепления нити в позе этого кадра (голова и шея двигаются)
                float3 anc = 0;
                if (p.teth)
                {
                    if (!Tethers || p.age > p.tethLife) p.teth = false;
                    else { anc = AnatSkin.on ? AnatSkin.Skin(p.anchor, out _) : p.anchor; tethered++; }
                }
                for (int s = 0; s < sub; s++)
                {
                    p.v.y -= 981f * h;
                    if (p.teth)
                    {
                        // нить тянет только при натяжении; затухание вдоль нити; разрыв при перетяжке
                        float3 d = p.x - anc; float L = math.length(d);
                        if (L > p.tethLen && L > 1e-4f)
                        {
                            float3 u = d / L; float over = L - p.tethLen;
                            if (over > TearOver) { p.teth = false; tethered--; }
                            else
                            {
                                p.v += (-TetherK * over - TetherC * math.dot(p.v, u)) * u * h;
                                p.v *= math.exp(-1.5f * h);
                                p.w *= math.exp(-3f * h);
                                float vl = math.length(p.v); if (vl > TetherVMax) p.v *= TetherVMax / vl;
                            }
                        }
                    }
                    p.x += p.v * h;
                    float a = math.length(p.w);
                    if (a > 1e-5f) p.q = math.normalize(math.mul(quaternion.AxisAngle(p.w / a, a * h), p.q));
                    // пол: самая низкая из 8 вершин решётки куска (вокруг центра масс)
                    float3x3 R = new float3x3(p.q); float low = 0; float3 lowArm = 0;
                    float3 c0 = p.g.org - p.com, c1 = p.g.org + (float3)p.g.dims * p.g.step - p.com;
                    for (int k = 0; k < 8; k++)
                    {
                        float3 arm = math.mul(R, new float3((k & 1) != 0 ? c1.x : c0.x, (k & 2) != 0 ? c1.y : c0.y, (k & 4) != 0 ? c1.z : c0.z));
                        float y = p.x.y + arm.y - Floor; if (y < low) { low = y; lowArm = arm; }
                    }
                    if (low < 0)
                    {
                        p.x.y -= low * 0.8f;   // выталкивание (оболочка решётки шире самого куска — оставить немного утопленным)
                        float3 vc = p.v + math.cross(p.w, lowArm);
                        if (vc.y < 0) { p.v.y = -p.v.y * 0.25f; p.v.xz *= 0.6f; p.w *= 0.6f; p.w += math.cross(lowArm, new float3(0, -vc.y, 0)) / math.max(1, math.lengthsq(lowArm)) * 0.3f; }
                        else { p.v.xz *= math.exp(-6 * h); p.w *= math.exp(-6 * h); }
                    }
                }
                bool slow = !p.teth && math.lengthsq(p.v) < 25 && math.lengthsq(p.w) < 0.04f;
                p.still = slow ? p.still + dt : 0;
                if (p.still > 0.6f || p.age > 12) { p.asleep = true; p.v = 0; p.w = 0; }
                Place(p);
            }
        }

        /// Корень тела куска: точка решётки r ↦ x + q·(r − com).
        public static void Place(AnatPiece p)
        {
            var R = new float3x3(p.q);
            p.g.rot = R; p.g.pos = p.x - math.mul(R, p.com);
            if (p.body == null) return;
            p.body.root.localPosition = (Vector3)(p.g.World(p.g.org) * 0.01f);
            p.body.root.localRotation = p.q;
        }
    }
}
