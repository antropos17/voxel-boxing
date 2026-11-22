using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;

namespace VoxProbe
{
    /// Проверки строения анатомической головы: лучи снаружи внутрь по зонам (HEAD-SPEC, раздел 3), зубы в кости, глаз в глазнице.
    /// Ожидания взяты из таблицы раздела 3; допуск — шаг решётки (2 мм) в обе стороны, слой тоньше шага в модели не тоньше 2 мм.
    public static class AnatChecks
    {
        public struct Run { public byte mat; public float mm; }

        /// Слой ожидания: допустимые материалы, толщина по таблице (мм, нижняя и верхняя граница). hi ≤ 0 — толщина не проверяется.
        public struct Layer { public string name; public byte[] mats; public float lo, hi; }

        public struct Ray { public string zone; public float3 from, dir; public Layer[] layers; }

        static Layer L(string n, float lo, float hi, params byte[] m) => new Layer { name = n, mats = m, lo = lo, hi = hi };
        static readonly byte[] Bone = { AnatMat.Compact, AnatMat.Spongy };
        static Layer Sk(float lo, float hi) => L("кожа", lo, hi, AnatMat.Skin, AnatMat.Stubble);
        static Layer Bn(float lo, float hi) => L("кость", lo, hi, Bone);

        /// Лучи (начало снаружи, направление внутрь; см, система головы). Зона — строка таблицы раздела 3.
        public static readonly Ray[] Rays =
        {
            new Ray { zone = "лоб", from = new float3(1.6f, 6.4f, 14), dir = new float3(0, 0, -1), layers = new[] {
                Sk(1.2f, 1.5f), L("жир", 4, 7, AnatMat.Fat), L("апоневроз", 1, 2, AnatMat.Fascia, AnatMat.Muscle), L("рыхлый слой", 1, 3, AnatMat.Loose),
                L("надкостница", .5f, .5f, AnatMat.Periost), Bn(7, 10), L("твёрдая оболочка", .5f, 2, AnatMat.Dura), L("ликвор", 2, 3, AnatMat.Csf),
                L("кора", 2, 4, AnatMat.Grey), L("белое вещество", 0, 0, AnatMat.White) } },
            new Ray { zone = "свод", from = new float3(1.4f, 20, -1.5f), dir = new float3(0, -1, 0), layers = new[] {
                Sk(1.2f, 1.5f), L("жир", 4, 7, AnatMat.Fat), L("апоневроз", 1, 2, AnatMat.Fascia), L("рыхлый слой", 1, 3, AnatMat.Loose),
                L("надкостница", .5f, .5f, AnatMat.Periost), Bn(7, 10), L("твёрдая оболочка", .5f, 2, AnatMat.Dura), L("ликвор", 2, 3, AnatMat.Csf),
                L("кора", 2, 4, AnatMat.Grey) } },
            new Ray { zone = "висок", from = new float3(12, 2.6f, 2.4f), dir = new float3(-1, 0, 0), layers = new[] {
                Sk(1.4f, 1.4f), L("жир и фасции", 3, 6, AnatMat.Fat, AnatMat.Fascia, AnatMat.Loose), L("височная мышца", 8, 15, AnatMat.Muscle, AnatMat.Tendon),
                L("надкостница", .5f, .5f, AnatMat.Periost), Bn(1.4f, 2.3f), L("твёрдая оболочка", .5f, 2, AnatMat.Dura, AnatMat.Artery) } },
            new Ray { zone = "бровь", from = new float3(2.6f, 4.4f, 14), dir = new float3(0, 0, -1), layers = new[] {
                Sk(1.0f, 1.3f), L("жир", 1, 3, AnatMat.Fat), L("круговая мышца глаза", 1.6f, 1.6f, AnatMat.Muscle), L("надкостница", .5f, .5f, AnatMat.Periost),
                Bn(4, 0) } },
            new Ray { zone = "нос", from = new float3(0.3f, 1.4f, 14), dir = new float3(0, 0, -1), layers = new[] {
                Sk(1.2f, 2), L("жир", 1, 3, AnatMat.Fat), L("надкостница", 0, 0, AnatMat.Periost), Bn(1.5f, 3), L("слизистая", 0, 0, AnatMat.Mucosa) } },
            new Ray { zone = "скула", from = new float3(4.4f, -0.9f, 14), dir = new float3(0, 0, -1), layers = new[] {
                Sk(1.5f, 2), L("жир", 5, 10, AnatMat.Fat), L("фасция", 1, 2, AnatMat.Fascia), L("скуловые мышцы", 2, 4, AnatMat.Muscle),
                L("надкостница", 0, 0, AnatMat.Periost), Bn(4, 8) } },
            new Ray { zone = "подбородок", from = new float3(0.6f, -8.0f, 14), dir = new float3(0, 0, -1), layers = new[] {
                Sk(1.5f, 2), L("жир", 5, 10, AnatMat.Fat), L("подбородочная мышца", 3, 5, AnatMat.Muscle, AnatMat.Fascia), L("надкостница", 0, 0, AnatMat.Periost),
                Bn(6, 12) } },
            new Ray { zone = "ухо", from = new float3(12, 2.7f, -0.6f), dir = new float3(-1, 0, 0), layers = new[] {
                Sk(1, 2), L("хрящ", 2, 4, AnatMat.Cartilage), Sk(1, 2) } },
            new Ray { zone = "затылок", from = new float3(1.2f, 2.5f, -16), dir = new float3(0, 0, 1), layers = new[] {
                Sk(1.5f, 1.5f), L("жир", 4, 7, AnatMat.Fat), L("апоневроз и затылочная мышца", 2, 4, AnatMat.Fascia, AnatMat.Muscle, AnatMat.Loose),
                L("надкостница", 0, 0, AnatMat.Periost), Bn(8, 10) } },
        };

        /// Сосуды внутри слоёв не считаются отдельным слоем (артерия в оболочке, вена в жире).
        static bool Skip(byte m) => m == AnatMat.Artery || m == AnatMat.Vein;

        /// Слои вдоль луча. Вход ищется шагом 0,1 мм (волосы и пустота снаружи пропускаются), дальше луч идёт по нормали к поверхности
        /// (−∇HeadSdf) обходом ячеек решётки головы (DDA): он проходит каждую ячейку, которую пересекает, и не перескакивает слой
        /// в один воксель через ребро. Ячейки, занятые другим телом (Covered), просматриваются точками через AnatHead.Sample.
        public static List<Run> Trace(AnatHead a, float3 from, float3 dir, float maxCm = 9, bool alongNormal = true)
        {
            var runs = new List<Run>();
            float3 p0 = from; bool found = false;
            for (float t = 0; t < 30; t += 0.01f)
            {
                var p = from + dir * t;
                byte m = a.Sample(p, out _, out _);
                if (m == AnatMat.Empty || m == AnatMat.Air || m == AnatMat.Hair || m == AnatMat.Brow || m == AnatMat.Lash) continue;
                found = true; p0 = p;
                if (alongNormal)
                {
                    const float e = 0.05f;
                    float3 gr = new float3(AnatGeo.HeadSdf(p.x + e, p.y, p.z) - AnatGeo.HeadSdf(p.x - e, p.y, p.z),
                        AnatGeo.HeadSdf(p.x, p.y + e, p.z) - AnatGeo.HeadSdf(p.x, p.y - e, p.z), AnatGeo.HeadSdf(p.x, p.y, p.z + e) - AnatGeo.HeadSdf(p.x, p.y, p.z - e));
                    if (math.lengthsq(gr) > 1e-8f) dir = -math.normalize(gr);
                }
                p0 = p - dir * 0.3f;
                break;
            }
            if (!found) return runs;
            var g = a.head;
            void Add(byte m, float cm)
            {
                if (runs.Count == 0 && (m == AnatMat.Empty || m == AnatMat.Air || m == AnatMat.Hair || m == AnatMat.Brow || m == AnatMat.Lash)) return;
                if (runs.Count > 0 && runs[runs.Count - 1].mat == m) { var r = runs[runs.Count - 1]; r.mm += cm * 10; runs[runs.Count - 1] = r; }
                else runs.Add(new Run { mat = m, mm = cm * 10 });
            }
            float3 gp = (p0 - g.org) / g.step;
            int3 c = (int3)math.floor(gp);
            int3 st = new int3(dir.x > 0 ? 1 : -1, dir.y > 0 ? 1 : -1, dir.z > 0 ? 1 : -1);
            float3 inv = 1f / math.max(math.abs(dir), 1e-9f) * g.step;
            float3 next = new float3(
                (dir.x > 0 ? (c.x + 1 - gp.x) : (gp.x - c.x)) * inv.x,
                (dir.y > 0 ? (c.y + 1 - gp.y) : (gp.y - c.y)) * inv.y,
                (dir.z > 0 ? (c.z + 1 - gp.z) : (gp.z - c.z)) * inv.z);
            float tcur = 0, total = 0.3f + maxCm;
            while (tcur < total)
            {
                int ax = next.x < next.y ? (next.x < next.z ? 0 : 2) : (next.y < next.z ? 1 : 2);
                float tn = math.min(next[ax], total);
                bool inside = math.all(c >= 0) && math.all(c < g.dims);
                byte m = inside ? g.mat[g.Index(c.x, c.y, c.z)] : AnatMat.Empty;
                if (m == AnatMat.Covered)
                {
                    for (float t = tcur; t < tn; t += 0.01f) Add(a.Sample(p0 + dir * (t + 0.005f), out _, out _), math.min(0.01f, tn - t));
                }
                else if (tn > tcur) Add(m, tn - tcur);
                tcur = tn;
                c[ax] += st[ax]; next[ax] += inv[ax];
            }
            return runs;
        }

        public struct Result { public string zone; public bool ok; public string got, report; }

        /// Сверка с ожиданием по пучку из 9 лучей (сетка 3×3 через 1 мм поперёк направления): косой луч может задеть слой в один воксель
        /// уголком, поэтому толщина слоя — медиана по пучку, слой есть, если он есть хотя бы в 5 лучах из 9.
        /// Порядок: слои подряд, каждый — подряд идущие материалы из его набора (сосуды внутри слоя пропускаются).
        public static Result Check(AnatHead a, Ray r)
        {
            var d0 = math.normalize(r.dir);
            var u = math.normalize(math.cross(d0, math.abs(d0.y) > 0.9f ? new float3(1, 0, 0) : new float3(0, 1, 0)));
            var v = math.cross(d0, u);
            int nl = r.layers.Length;
            var th = new List<float>[nl]; for (int k = 0; k < nl; k++) th[k] = new List<float>();
            string got = null;
            for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
            {
                var runs = Trace(a, r.from + (u * i + v * j) * 0.1f, d0);
                if (i == 0 && j == 0) { var g = new StringBuilder(); foreach (var x in runs) g.Append(AnatMat.Table[x.mat].name).Append(' ').Append(x.mm.ToString("0.0")).Append(" · "); got = g.ToString(); }
                int ri = 0;
                for (int k = 0; k < nl; k++)
                {
                    var l = r.layers[k]; float mm = 0; bool any = false;
                    while (ri < runs.Count && (Skip(runs[ri].mat) || System.Array.IndexOf(l.mats, runs[ri].mat) >= 0))
                    { if (!Skip(runs[ri].mat)) { mm += runs[ri].mm; any = true; } ri++; }
                    if (any) th[k].Add(mm);
                    else if (!(l.lo <= 0 && l.hi <= 0)) break;
                }
            }
            var sb = new StringBuilder(); bool ok = true;
            for (int k = 0; k < nl; k++)
            {
                var l = r.layers[k];
                bool optional = l.lo <= 0 && l.hi <= 0;
                bool any = th[k].Count >= 5;
                th[k].Sort();
                float mm = th[k].Count > 0 ? th[k][th[k].Count / 2] : 0;
                // слой в один воксель косой луч пересекает на 0–2,8 мм (по нормали к ступеньке), отсюда нижний предел 1,0 мм
                float lo = math.max(1.0f, l.lo - 2.1f), hi = l.hi <= 0 ? 1e9f : math.max(l.hi, 2) + 2.1f;
                bool good = optional || (any && mm >= lo - 1e-3f && mm <= hi);
                if (l.lo > 0 && l.hi <= 0) good = any && mm >= l.lo - 0.1f;
                sb.Append(good ? "  ок  " : "  НЕТ ").Append(l.name).Append(": ").Append(any ? mm.ToString("0.0") + " мм" : "нет").Append(" [в ").Append(th[k].Count).Append(" из 9]")
                  .Append(" (по таблице ").Append(optional ? "есть" : l.hi <= 0 ? "≥ " + l.lo : l.lo + "–" + l.hi).Append(")").Append('\n');
                ok &= good;
                if (!any && !optional) break;
            }
            return new Result { zone = r.zone, ok = ok, got = got, report = sb.ToString() };
        }

        /// Корни зубов: доля ячеек корня (глубже 2,5 мм от шейки), у которых во всех шести направлениях первая ткань за лункой — кость
        /// (компакта, губчатая, надкостница, канал) или соседний зуб. Лунка — место самого зуба: в челюсти пусто, в голове Covered.
        /// miss — что встретилось вместо кости (название материала → число случаев).
        public static float[] RootsInBone(AnatHead a, out int withRoots, Dictionary<string, int> miss = null)
        {
            var res = new float[a.teeth.Length]; withRoots = 0;
            for (int k = 0; k < a.teeth.Length; k++)
            {
                var g = a.teeth[k];
                int root = 0, inBone = 0;
                for (int z = 0; z < g.dims.z; z++) for (int y = 0; y < g.dims.y; y++) for (int x = 0; x < g.dims.x; x++)
                {
                    byte m = g.mat[g.Index(x, y, z)];
                    if (m != AnatMat.Dentin && m != AnatMat.Pulp) continue;
                    var q = g.Local(x, y, z);
                    if (q.z < 0.25f) continue; // коронка и шейка (ось W — к верхушке корня)
                    root++;
                    var w = g.World(q);
                    bool all = true;
                    for (int d = 0; d < 6 && all; d++)
                    {
                        var dir = d == 0 ? new float3(1, 0, 0) : d == 1 ? new float3(-1, 0, 0) : d == 2 ? new float3(0, 1, 0) : d == 3 ? new float3(0, -1, 0) : d == 4 ? new float3(0, 0, 1) : new float3(0, 0, -1);
                        for (float t = 0.05f; t < 0.5f; t += 0.05f)
                        {
                            byte h = Host(a, w + dir * t, g);
                            if (h == 255 || h == AnatMat.Covered) continue; // свой зуб или лунка
                            bool bone = h == AnatMat.Compact || h == AnatMat.Spongy || h == AnatMat.Periost || h == AnatMat.Artery || h == AnatMat.Dentin;
                            if (!bone) { all = false; if (miss != null) { var nm = AnatMat.Table[h].name; miss.TryGetValue(nm, out int c); miss[nm] = c + 1; } }
                            break;
                        }
                    }
                    if (all) inBone++;
                }
                if (root > 0) withRoots++;
                res[k] = root == 0 ? 0 : inBone / (float)root;
            }
            return res;
        }

        /// Материал в точке: 255 — свой зуб, Dentin — чужой зуб, иначе нижняя челюсть (если не пусто), иначе голова.
        static byte Host(AnatHead a, float3 p, AnatGrid self)
        {
            int si = self.Lookup(p); if (si >= 0 && self.mat[si] != AnatMat.Empty) return 255;
            foreach (var t in a.teeth)
            {
                if (t == self) continue;
                int ti = t.Lookup(p); if (ti >= 0 && t.mat[ti] != AnatMat.Empty) return AnatMat.Dentin;
            }
            int mi = a.mand.Lookup(p); if (mi >= 0 && a.mand.mat[mi] != AnatMat.Empty) return a.mand.mat[mi];
            int hi = a.head.Lookup(p); return hi >= 0 ? a.head.mat[hi] : AnatMat.Empty;
        }

        /// Глаз в глазнице: доля ячеек глаза, вокруг которых в голове Covered (то есть голова освободила место) и рядом нет кожи/кости внутри шара;
        /// и расстояние от центра шара до ближайшей кости глазницы по 26 направлениям (см).
        public static (float wrapped, float minWall, float maxWall, int openDirs) EyeInOrbit(AnatHead a, AnatGrid eye)
        {
            float3 c = eye.pos; float mn = 99, mx = 0; int open = 0;
            for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                var d = math.normalize(new float3(dx, dy, dz));
                float hit = -1;
                for (float t = 1.0f; t < 3.5f; t += 0.02f)
                {
                    int hi = a.head.Lookup(c + d * t);
                    if (hi < 0) break;
                    byte m = a.head.mat[hi];
                    if (m == AnatMat.Compact || m == AnatMat.Spongy || m == AnatMat.Periost) { hit = t; break; }
                }
                if (hit < 0) open++; else { mn = math.min(mn, hit); mx = math.max(mx, hit); }
            }
            int cells = 0, covered = 0;
            for (int z = 0; z < eye.dims.z; z++) for (int y = 0; y < eye.dims.y; y++) for (int x = 0; x < eye.dims.x; x++)
            {
                int i = eye.Index(x, y, z); if (eye.mat[i] == AnatMat.Empty) continue;
                cells++;
                int hi = a.head.Lookup(eye.World(eye.Local(x, y, z)));
                if (hi >= 0 && a.head.mat[hi] == AnatMat.Covered) covered++;
            }
            return (cells == 0 ? 0 : covered / (float)cells, mn, mx, open);
        }
    }
}
