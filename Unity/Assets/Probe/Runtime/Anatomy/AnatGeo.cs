using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxProbe
{
    /// Описание зуба: шейка c (центр на линии эмаль-цемент), оси U (вдоль дуги), V (наружу, к губе/щеке), W (к верхушке корня).
    public struct ToothDef
    {
        public float3 c, U, V, W;
        public float mw, bl, hc, hr;   // ширина вдоль дуги, толщина, высота коронки, длина корня (см)
        public int type, roots, upper, fdi;
    }

    /// Геометрия анатомической головы, сантиметры, рамка веб-головы: начало у слухового прохода на средней линии, +y вверх, +z к лицу.
    /// Правая сторона человека — x &lt; 0. Всё, что ниже, — Burst-совместимые функции; таблицы передаются массивами.
    public static class AnatGeo
    {
        public const float S = 0.2f;   // шаг решётки головы, см (2,0 мм)
        public static readonly float3 Org = new float3(-9.5f, -11.95f, -11.2f);
        public static readonly int3 Dims = new int3(95, 134, 117);
        public const float EX = 3.2f, EY = 2.3f, EZ = 6.2f, ER = 1.2f, LR = 1.5f;

        // ---------- примитивы ----------
        public static float SdEll(float px, float py, float pz, float rx, float ry, float rz)
        {
            float a = px / rx, b = py / ry, c = pz / rz, k0 = math.sqrt(a * a + b * b + c * c);
            a /= rx; b /= ry; c /= rz;
            float k1 = math.sqrt(a * a + b * b + c * c);
            return k1 < 1e-9f ? -math.min(rx, math.min(ry, rz)) : k0 * (k0 - 1) / k1;
        }
        public static float SdEll2(float px, float py, float rx, float ry)
        {
            float a = px / rx, b = py / ry, k0 = math.sqrt(a * a + b * b);
            a /= rx; b /= ry;
            float k1 = math.sqrt(a * a + b * b);
            return k1 < 1e-9f ? -math.min(rx, ry) : k0 * (k0 - 1) / k1;
        }
        public static float SdCap(float3 p, float3 a, float3 b, float r, float r2)
        {
            float3 pa = p - a, ba = b - a;
            float h = math.saturate(math.dot(pa, ba) / math.dot(ba, ba));
            return math.length(pa - ba * h) - (r + (r2 - r) * h);
        }
        public static float SdBox(float3 p, float3 c, float3 h) { float3 q = math.abs(p - c) - h; return math.length(math.max(q, 0)) + math.min(math.cmax(q), 0); }
        public static float Smin(float a, float b, float k) { float h = k - math.abs(a - b); return h > 0 ? math.min(a, b) - h * h * 0.25f / k : math.min(a, b); }
        public static float Smax(float a, float b, float k) { float h = k - math.abs(a - b); return h > 0 ? math.max(a, b) + h * h * 0.25f / k : math.max(a, b); }
        public static float Gs(float dx, float dy, float dz, float r) => math.exp(-(dx * dx + dy * dy + dz * dz) / (r * r));
        public static bool InEll(float x, float y, float z, float a, float b, float c) { x /= a; y /= b; z /= c; return x * x + y * y + z * z < 1; }
        public static float Sstep(float e0, float e1, float x) { float t = math.saturate((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
        public static float SegDist(float3 p, float3 a, float3 b) { float3 pa = p - a, ba = b - a; float h = math.saturate(math.dot(pa, ba) / math.dot(ba, ba)); return math.length(pa - ba * h); }
        public static float SegDist2(float2 p, float2 a, float2 b) { float2 pa = p - a, ba = b - a; float h = math.saturate(math.dot(pa, ba) / math.dot(ba, ba)); return math.length(pa - ba * h); }

        static bool Almond(float ax, float y)
        {
            float u = (ax - EX) / 1.45f; if (u <= -1 || u >= 1) return false;
            float w = math.pow(1 - u * u, 0.7f), dy = y - EY;
            return dy < 0.5f * w + 0.07f * u && dy > -0.33f * w + 0.05f * u;
        }
        public static bool LashZone(float ax, float y)
        {
            float u = (ax - EX) / 1.45f; if (u <= -1.02f || u >= 1.02f) return false;
            float w = math.pow(math.max(0, 1 - u * u), 0.7f), top = 0.5f * w + 0.07f * u, dy = y - EY;
            return dy >= top - 0.05f && dy < top + 0.36f;
        }

        /// Наружная форма — headSDF веб-версии (d_models.js) без уха; шея — столб до низа решётки (в вебе под ней туловище,
        /// здесь шар шеи замыкался под челюстью бугром), низ решётки закрыт кожей (OccJob).
        public static float Body(float x, float y, float z)
        {
            float ax = math.abs(x), d, e;
            d = SdEll(x, y - 3.4f, z + 1.2f, 7.5f, 9.4f, 9.4f);
            d = Smin(d, SdEll(x, y - 6.0f, z - 3.0f, 6.3f, 5.0f, 5.6f), 2.0f);
            d = Smin(d, SdEll(x, y - 3.95f, z - 6.45f, 5.5f, 1.25f, 2.25f), 1.0f);
            d = Smin(d, SdEll(ax - 4.4f, y - 0.9f, z - 5.6f, 2.3f, 1.5f, 2.2f), 1.2f);
            d = Smin(d, SdEll(ax - 6.3f, y - 0.7f, z - 2.0f, 1.2f, 0.95f, 3.0f), 1.0f);
            d = Smin(d, SdEll(x, y + 1.6f, z - 5.6f, 4.1f, 3.6f, 3.4f), 1.6f);
            d = Smin(d, SdEll(x, y + 4.3f, z - 6.9f, 3.2f, 2.0f, 2.4f), 1.2f);
            d = Smin(d, SdEll(x, y + 6.9f, z - 3.6f, 5.2f, 2.6f, 5.0f), 1.8f);
            d = Smin(d, SdEll(x, y + 8.6f, z - 7.1f, 2.5f, 2.0f, 1.7f), 1.4f);
            d = Smin(d, SdEll(ax - 4.9f, y + 6.4f, z - 0.2f, 1.3f, 1.8f, 1.9f), 1.4f);
            d = Smin(d, SdCap(new float3(ax, y, z), new float3(4.9f, -6.0f, 0.2f), new float3(5.0f, -0.8f, 0.6f), 1.2f, 1.2f), 1.2f);
            d = Smin(d, SdEll(x, math.max(y + 7.4f, 0), z + 2.4f, 4.8f, 4.2f, 4.7f), 2.2f);
            e = SdCap(new float3(x, y, z), new float3(0, 3.0f, 8.3f), new float3(0, -1.4f, 10.3f), 0.55f, 0.72f);
            e = Smin(e, SdEll(x, y + 1.75f, z - 10.15f, 1.1f, 0.95f, 0.95f), 0.8f);
            e = Smin(e, SdEll(ax - 1.3f, y + 2.25f, z - 8.95f, 0.82f, 0.72f, 0.95f), 0.6f);
            d = Smin(d, e, 0.9f);
            d = Smin(d, SdEll(x, y + 4.45f, z - 8.55f, 2.45f, 0.72f, 1.0f), 0.7f);
            d = Smin(d, SdEll(x, y + 5.95f, z - 8.35f, 2.25f, 0.75f, 0.95f), 0.7f);
            return d;
        }

        /// Ушная раковина — пластина толщиной 6–8 мм (кожа, хрящ, кожа): контур-эллипс в плоскости y–z, задний край отведён
        /// от головы, у входа в слуховой проход — чаша раковины, по краю завиток толще.
        public static float EarSdf(float ax, float y, float z)
        {
            float u = y - 0.55f, v = z + 1.05f;
            float o = SdEll2(v, u, 1.85f, 3.05f);
            if (o > 0.6f) return o;
            float back = math.saturate(0.5f - 0.5f * v / 1.85f);
            // от поверхности головы (один шаг Ньютона по x от 7,3): спереди пластина прирастает, задний край отходит на 1,6 см
            float xs = 7.3f - Body(7.3f, y, z);
            float xm = xs + 0.25f + 1.6f * back * back;
            float bowl = math.saturate(1 - math.length(new float2((u + 0.35f) / 1.25f, (v - 0.2f) / 0.85f)));
            xm -= 0.35f * bowl;
            float t = 0.3f + 0.12f * Sstep(-0.55f, -0.05f, o);
            return math.max(math.abs(ax - xm) - t, o);
        }

        public static float HeadSdf(float x, float y, float z)
        {
            float ax = math.abs(x);
            float d = Smin(Body(x, y, z), EarSdf(ax, y, z), 0.35f);
            d = Smax(d, -SdEll(ax - EX, y - EY, z - 7.15f, 1.8f, 1.4f, 1.55f), 0.6f);
            d = Smax(d, -SdEll(x, y + 5.25f, z - 5.3f, 2.55f, 0.85f, 3.2f), 0.4f);
            d = Smax(d, -SdEll(ax - 0.56f, y + 2.72f, z - 9.35f, 0.3f, 0.24f, 0.55f), 0.15f);
            float ex = ax - EX, ey = y - EY, ez = z - EZ, er = math.sqrt(ex * ex + ey * ey + ez * ez), lid = er - LR;
            if (ez > 0.2f && Almond(ax, y)) lid = math.max(lid, 0.25f);
            d = Smin(d, lid, 0.3f);
            return math.min(d, er - ER);
        }

        public static float MouthCav(float x, float y, float z) => SdEll(x, y + 5.25f, z - 5.3f, 2.55f, 0.85f, 3.2f);
        public static bool InMouthZone(float x, float y, float z) => SdEll(x, y + 5.25f, z - 5.3f, 3.0f, 1.25f, 3.65f) < 0;
        public static bool LipZone(float ax, float y, float z) { if (z < 7.9f || y > -4.55f || y < -6.3f) return false; float u = (y + 5.25f) / 1.05f; return ax < 2.3f - 0.9f * u * u; }
        public static bool BrowZone(float ax, float y, float z)
        {
            if (ax < 0.75f || ax > 5.5f || z < 5.4f) return false;
            float t = (ax - 0.75f) / 4.75f, c = 3.45f + 0.42f * math.sin(math.PI * math.min(1, t * 1.2f)) - 0.15f * t, h = 0.42f - 0.17f * t;
            return y > c - h && y < c + h;
        }
        public static bool HairZone(float x, float y, float z)
        {
            float ax = math.abs(x);
            if (ax > 6.6f && y < 4.1f && y > -3.9f && z > -3.4f && z < 2.1f) return false;
            float q = (ax / 5.5f) * (ax / 5.5f), h;
            if (z >= 4.4f) h = 9.2f + 1.5f * q;
            else if (z >= 1.4f) h = 9.2f + 1.5f * q - (4.4f - z) * (8.3f + 1.5f * q) / 3.0f;
            else if (z >= -2.6f) h = 0.9f;
            else h = -3.2f - 0.8f * (1 - math.min(1, ax / 6));
            return y > h;
        }
        /// Плотность щетины 0..1 — гладкое поле по форме зоны beardZone() веб-версии (щёки, челюсть, шея, над губой, без центра
        /// подбородка у губы), с переходом ~0,8 см вместо прямых краёв. Ячейка — щетина, если её хеш меньше плотности.
        public static float StubbleDensity(float x, float y, float z)
        {
            float ax = math.abs(x);
            float d = Sstep(-2.0f, -2.9f, y) * Sstep(0.3f, 1.3f, z) * Sstep(7.6f, 6.8f, ax);
            // гладкий центр подбородка под губой
            float ex = Sstep(3.4f, 2.4f, ax) * Sstep(6.7f, 7.7f, z) * Sstep(-7.4f, -6.4f, y) * Sstep(-3.1f, -4.1f, y);
            d *= 1 - ex;
            // усы над губой
            float m = Sstep(3.0f, 2.1f, ax) * Sstep(-4.4f, -3.8f, y) * Sstep(-2.6f, -3.2f, y) * Sstep(7.8f, 8.5f, z);
            return math.max(d, m);
        }
        public static float CellHash(int3 c) { uint h = (uint)(c.x * 73856093 ^ c.y * 19349663 ^ c.z * 83492791); h ^= h >> 15; h *= 0x85EBCA77u; h ^= h >> 13; return (h & 0xFFFF) / 65535f; }

        /// Ячейка раковины вне головы (хрящ или мочка под кожей).
        public static bool EarZone(float ax, float y, float z) => ax > 6.3f && EarSdf(ax, y, z) < 0.05f && Body(ax, y, z) > 0.1f;

        /// Язык: тело и корень (в вебе — один эллипсоид у дна рта).
        public static bool InTongue(float x, float y, float z)
        {
            if (y > -4.05f) return false;
            float d = Smin(SdEll(x, y + 5.55f, z - 5.0f, 1.85f, 0.72f, 2.5f), SdEll(x, y + 6.2f, z - 2.7f, 1.6f, 1.2f, 1.4f), 0.6f);
            return d < 0;
        }

        // ---------- полость черепа ----------
        /// Дно полости черепа: передняя ямка над глазницами (решётчатая пластинка ниже), средняя ямка у виска, турецкое седло, за ним задняя ямка.
        public static float FloorF(float ax, float z)
        {
            float ant = 3.55f - 0.45f * math.exp(-(ax / 0.8f) * (ax / 0.8f));
            float midLat = 0.6f + 0.3f * math.max(0, z - 1.0f);
            float mid = math.lerp(1.9f, midLat, Sstep(1.0f, 2.4f, ax));
            float f = math.lerp(0.3f, mid, Sstep(-2.9f, -1.9f, z));
            return math.lerp(f, ant, Sstep(2.2f, 3.0f, z));
        }
        public static float PostFossa(float x, float y, float z) => SdEll(x, y + 0.9f, z + 5.9f, 5.0f, 2.7f, 4.0f);
        public static float StemCap(float x, float y, float z) => SdCap(new float3(x, y, z), new float3(0, 1.4f, -1.2f), new float3(0, -3.7f, -2.6f), 1.0f, 0.6f);
        /// &lt; 0 — внутри разрешённой для мозга области (отдельно от свода, который задан толщиной покровов).
        public static float BaseLimit(float x, float y, float z)
        {
            float ax = math.abs(x);
            return math.min(math.min(FloorF(ax, z) - y, PostFossa(x, y, z)), StemCap(x, y, z));
        }
        public static float BaseThick(float ax, float y, float z)
        {
            float w0 = Gs(ax, y - 3.5f, z - 5.5f, 2.2f), w1 = Gs(ax - 3.5f, y - 0.5f, z - 0.8f, 1.6f), w2 = Gs(ax, y - 1.4f, z - 2.4f, 1.0f),
                  w3 = Gs(ax - 3.6f, y, z + 1.0f, 1.2f), w4 = Gs(ax, y + 2.5f, z + 5.0f, 2.0f);
            float sw = w0 + w1 + w2 + w3 + w4 + 1e-4f;
            return (0.2f * w0 + 0.35f * w1 + 0.3f * w2 + 1.4f * w3 + 0.8f * w4 + 0.4f * 1e-4f) / sw;
        }
        /// Намёт мозжечка: над ним затылочные доли, под ним мозжечок.
        public static float Tent(float ax, float z) => 0.3f + math.saturate((z + 9f) / 6f) * 1.3f + 0.4f * math.max(0, 1 - (ax / 4f) * (ax / 4f));
        /// Поле извилин: |f| мало — борозда.
        public static float Gyri(float x, float y, float z) =>
            math.sin(3.0f * x + 1.6f * math.sin(2.0f * y + 0.7f * z)) + math.sin(2.8f * y + 1.5f * math.sin(1.9f * z)) + math.sin(2.9f * z + 1.4f * math.sin(1.5f * x + 0.9f * y));

        /// Расстояние до ближайшей борозды (см): |f| / |grad f|.
        public static float SulcusDist(float x, float y, float z)
        {
            const float h = 0.05f;
            float f = Gyri(x, y, z);
            float gx = Gyri(x + h, y, z) - Gyri(x - h, y, z), gy = Gyri(x, y + h, z) - Gyri(x, y - h, z), gz = Gyri(x, y, z + h) - Gyri(x, y, z - h);
            float g = math.sqrt(gx * gx + gy * gy + gz * gz) / (2 * h);
            return math.abs(f) / math.max(g, 0.5f);
        }

        // ---------- основание черепа ----------
        /// Скат, пирамиды височных костей, сосцевидные отростки: массивная кость основания (меньше 0 — внутри).
        public static float BaseBone(float ax, float y, float z)
        {
            float3 q = new float3(ax, y, z);
            float clivus = SdCap(new float3(ax * 0.75f, y, z), new float3(0, 0.9f, 1.3f), new float3(0, -3.0f, -1.2f), 0.7f, 0.55f);
            float petrous = SdCap(q, new float3(1.5f, 0.5f, 0.2f), new float3(4.7f, 0.2f, -2.2f), 0.55f, 0.85f);
            return math.min(clivus, math.min(petrous, Mastoid(ax, y, z)));
        }
        public static float Mastoid(float ax, float y, float z) => SdEll(ax - 5.3f, y + 1.1f, z + 2.7f, 0.85f, 1.25f, 1.0f);
        /// Наружный слуховой проход.
        public static float EarCanal(float ax, float y, float z) => SdCap(new float3(ax, y, z), new float3(7.6f, 0.25f, -0.75f), new float3(5.7f, 0.1f, -1.0f), 0.32f, 0.28f);

        // ---------- глазница ----------
        public static float Orbit(float ax, float y, float z)
        {
            float e1 = SdEll(ax - 3.1f, y - 2.25f, z - 5.8f, 1.75f, 1.75f, 1.9f);
            float co = SdCap(new float3(ax, y, z), new float3(2.9f, 2.1f, 5.0f), new float3(1.55f, 1.85f, 2.75f), 1.5f, 0.25f);
            return Smin(e1, co, 0.4f);
        }
        public static readonly float3 Apex = new float3(1.55f, 1.85f, 2.75f);
        public static float3 Globe(float3 n) => new float3(EX, EY, EZ) + math.normalize(n) * ER;

        /// Глазодвигательные мышцы и нерв (координаты с ax). Возвращает номер мышцы или 0; tendon — сухожилие верхней косой за блоком.
        public static byte EyeMuscle(float3 q, out bool tendon, out bool nerve)
        {
            tendon = false; nerve = false;
            float3 A = Apex;
            if (SdCap(q, Globe(new float3(-0.12f, 0, -1)), A, 0.2f, 0.17f) < 0) { nerve = true; return 0; }
            if (SdCap(q, A, new float3(0.45f, 2.25f, 1.9f), 0.17f, 0.17f) < 0) { nerve = true; return 0; }
            if (SdCap(q, A + new float3(0, 0.25f, 0), Globe(new float3(0, 1, 0.3f)), 0.15f, 0.2f) < 0) return AnatPart.RectusSup;
            if (SdCap(q, A + new float3(0, -0.25f, 0), Globe(new float3(0, -1, 0.3f)), 0.15f, 0.2f) < 0) return AnatPart.RectusInf;
            if (SdCap(q, A + new float3(-0.2f, 0, 0), Globe(new float3(-1, 0, 0.3f)), 0.15f, 0.2f) < 0) return AnatPart.RectusMed;
            if (SdCap(q, A + new float3(0.25f, 0, 0), Globe(new float3(1, 0, 0.3f)), 0.15f, 0.2f) < 0) return AnatPart.RectusLat;
            float3 T = new float3(2.1f, 3.65f, 7.0f);
            if (SdCap(q, A + new float3(-0.1f, 0.3f, 0), T, 0.13f, 0.15f) < 0) return AnatPart.ObliqueSup;
            if (SdCap(q, T, Globe(new float3(0.4f, 0.8f, -0.4f)), 0.1f, 0.12f) < 0) { tendon = true; return AnatPart.ObliqueSup; }
            if (SdCap(q, new float3(2.0f, 0.75f, 6.9f), Globe(new float3(0.7f, -0.6f, -0.4f)), 0.16f, 0.18f) < 0) return AnatPart.ObliqueInf;
            return 0;
        }

        // ---------- пазухи и полости ----------
        public static float FrontalSinus(float ax, float y, float z) => SdEll(ax - 0.85f, y - 4.45f, z - 7.1f, 1.05f, 1.15f, 0.8f);
        public static float MaxSinus(float ax, float y, float z) => SdEll(ax - 2.4f, y + 0.95f, z - 5.1f, 1.45f, 1.6f, 2.2f);
        public static float SphSinus(float x, float y, float z) => SdEll(x, y - 0.75f, z - 3.1f, 1.35f, 1.05f, 1.15f);
        public static float NasalHalf(float y) => math.lerp(1.05f, 0.30f, Sstep(-2.9f, 2.5f, y));
        public static float NasalRoof(float z) => math.lerp(-0.4f, 2.5f, Sstep(2.9f, 5.0f, z));
        /// Носовая полость (две половины, перегородка по средней линии вне полости). Приближённое расстояние.
        public static float Nasal(float ax, float y, float z)
        {
            float d = math.max(math.max(0.1f - ax, ax - NasalHalf(y)), math.max(-2.95f - y, y - NasalRoof(z)));
            return math.max(d, math.max(2.9f - z, z - 10.2f));
        }
        public static float Concha(float ax, float y, float z)
        {
            float a = SdEll(ax - 0.85f, y + 2.1f, z - 5.4f, 0.35f, 0.3f, 1.8f);
            float b = SdEll(ax - 0.62f, y + 0.6f, z - 5.4f, 0.3f, 0.28f, 1.5f);
            float c = SdEll(ax - 0.45f, y - 0.9f, z - 4.9f, 0.22f, 0.2f, 0.9f);
            return math.min(a, math.min(b, c));
        }
        public static float Vestibule(float ax, float y, float z) => SdCap(new float3(ax, y, z), new float3(0.56f, -2.72f, 9.35f), new float3(0.5f, -2.3f, 8.3f), 0.28f, 0.28f);
        public static bool EthmoidBox(float ax, float y, float z, out bool edge)
        {
            float x0 = NasalHalf(y) + 0.2f;
            edge = false;
            if (ax < x0 || ax > 1.45f || y < 0.7f || y > 3.2f || z < 3.9f || z > 7.3f) return false;
            edge = ax < x0 + 0.1f || ax > 1.25f || y < 0.9f || y > 3.0f || z < 4.1f || z > 7.1f;
            return true;
        }
        public static float Pharynx(float x, float y, float z)
        {
            float naso = SdBox(new float3(x, y, z), new float3(0, -1.6f, 2.2f), new float3(1.1f, 1.3f, 0.8f));
            float oro = math.max(SdEll2(x, z - 0.9f, 1.1f, 0.5f), y + 2.8f);
            float fauces = SdBox(new float3(x, y, z), new float3(0, -5.0f, 1.9f), new float3(0.9f, 0.55f, 0.75f));
            return math.min(math.min(naso, oro), fauces);
        }

        // ---------- зубные дуги ----------
        public const float UpA = 1.2f, UpZ0 = 7.95f, LoA = 1.15f, LoZ0 = 7.35f;
        /// x дуги на глубине z (цепная линия z = z0 − a(ch(x/a) − 1)).
        public static float ArchX(float a, float z0, float z) => a * math.log(1 + math.max(0, z0 - z) / a + math.sqrt(math.square(1 + math.max(0, z0 - z) / a) - 1));

        // ---------- нижняя челюсть ----------
        public static readonly float3 Condyle = new float3(4.95f, -0.6f, 0.25f);

        /// Ветвь, мыщелок, венечный отросток (ax).
        public static float Ramus(float ax, float y, float z)
        {
            float xp = 4.35f + 0.045f * (y + 7.8f);
            float poly = math.max(math.max(-7.85f - 0.08f * (z - 0.7f) - y, 0.7f - 0.07f * (y + 7.8f) - z), math.max(z - 2.45f, y + 2.3f));
            float r = math.max(math.abs(ax - xp) - 0.33f, poly);
            float3 p = new float3(ax, y, z);
            r = Smin(r, SdCap(p, new float3(4.6f, -3.0f, 0.35f), new float3(4.9f, -0.75f, 0.2f), 0.38f, 0.32f), 0.2f);
            r = Smin(r, SdEll(ax - Condyle.x, y - Condyle.y, z - Condyle.z, 0.95f, 0.42f, 0.5f), 0.15f);
            r = Smin(r, SdCap(p, new float3(4.35f, -3.2f, 2.1f), new float3(4.3f, -1.35f, 2.5f), 0.3f, 0.18f), 0.2f);
            return r;
        }

        /// Тело нижней челюсти вдоль нижней дуги: arch — точки (x, z, верх, низ) по одной стороне, thick — полутолщина.
        public static float MandBody(float ax, float y, float z, NativeArray<float4> arch, NativeArray<float> thick)
        {
            float best = 1e9f, top = 0, bot = 0, t = 0;
            float2 p = new float2(ax, z);
            for (int i = 0; i + 1 < arch.Length; i++)
            {
                float2 a = arch[i].xy, b = arch[i + 1].xy, ba = b - a, pa = p - a;
                float h = math.saturate(math.dot(pa, ba) / math.dot(ba, ba));
                float d = math.length(pa - ba * h);
                if (d < best) { best = d; top = math.lerp(arch[i].z, arch[i + 1].z, h); bot = math.lerp(arch[i].w, arch[i + 1].w, h); t = math.lerp(thick[i], thick[i + 1], h); }
            }
            // полоса вдоль дуги плюс подбородок через среднюю линию
            float band = best - t;
            if (ax < 0.6f) band = math.min(band, math.abs(z - arch[0].y) - t);
            return math.max(band, math.max(bot - y, y - top));
        }
        public static float Mandible(float ax, float y, float z, NativeArray<float4> arch, NativeArray<float> thick)
        {
            if (y < -11f || y > 0.5f || z < -0.9f || z > 9.2f || ax > 6.2f) return 9f;
            return Smin(MandBody(ax, y, z, arch, thick), Ramus(ax, y, z), 0.5f);
        }
        public static float AlvCanal(float ax, float y, float z)
        {
            float3 p = new float3(ax, y, z);
            return math.min(SdCap(p, new float3(4.35f, -5.0f, 1.2f), new float3(2.9f, -7.4f, 3.4f), 0.12f, 0.12f),
                            SdCap(p, new float3(2.9f, -7.4f, 3.4f), new float3(2.15f, -7.5f, 6.0f), 0.12f, 0.12f));
        }

        // ---------- зубы ----------
        /// Расстояние до поверхности зуба в его осях (q.x вдоль дуги, q.y наружу, q.z к верхушке; коронка при q.z &lt; 0).
        public static float ToothSdf(in ToothDef t, float3 q)
        {
            float hw = t.mw * 0.5f, hb = t.bl * 0.5f;
            float dc = 9f;
            if (q.z < 0.05f)
            {
                float s = math.saturate(-q.z / t.hc), a, b;
                if (t.type <= 1) { a = hw * (0.82f + 0.18f * math.sin(math.PI * math.min(1, s * 1.4f))); b = hb * (1 - 0.72f * s * s); }
                else if (t.type == 2) { a = hw * (0.85f + 0.15f * math.sin(math.PI * s)) * (1 - 0.45f * s * s * s); b = hb * (1 - 0.5f * s * s); }
                else { float k = 0.84f + 0.16f * math.sin(math.PI * s); a = hw * k; b = hb * k; }
                float top = -t.hc;
                if (t.type == 2) top = -t.hc * (1 - 0.35f * math.min(1, math.abs(q.x) / hw));
                dc = math.max(SdEll2(q.x, q.y, math.max(a, 0.03f), math.max(b, 0.03f)), math.max(top - q.z, q.z - 0.05f));
                if (t.type >= 3 && s > 0.86f && (math.abs(q.x) < 0.05f || (t.type >= 5 && math.abs(q.y) < 0.05f))) dc = math.max(dc, 0.01f); // борозды жевательной поверхности
            }
            float dr = 9f;
            if (q.z > -0.05f)
            {
                float u = math.saturate(q.z / t.hr), taper = 1 - 0.82f * u;
                if (t.roots == 1)
                    dr = math.max(SdEll2(q.x, q.y, hw * 0.78f * taper + 0.02f, hb * 0.82f * taper + 0.02f), math.max(-0.05f - q.z, q.z - t.hr));
                else
                {
                    float spread = 1 + 0.4f * u, trunk = 0.25f;
                    float dt = u < trunk ? math.max(SdEll2(q.x, q.y, hw * 0.8f, hb * 0.8f), -0.05f - q.z) : 9f;
                    dr = dt;
                    for (int k = 0; k < t.roots; k++)
                    {
                        float2 o;
                        if (t.roots == 2) o = t.upper == 1 ? new float2(0, (k == 0 ? 0.22f : -0.22f) * t.bl) : new float2((k == 0 ? -0.25f : 0.25f) * t.mw, 0);
                        else o = k == 0 ? new float2(-0.22f * t.mw, 0.2f * t.bl) : k == 1 ? new float2(0.22f * t.mw, 0.2f * t.bl) : new float2(0, -0.25f * t.bl);
                        float rr = math.min(hw, hb) * 0.5f * taper + 0.03f;
                        float d = math.max(math.length(new float2(q.x, q.y) - o * spread) - rr, math.max(-0.05f - q.z, q.z - t.hr));
                        dr = math.min(dr, d);
                    }
                }
            }
            return math.min(dc, dr);
        }
        public static float3 ToothLocal(in ToothDef t, float3 p) { float3 d = p - t.c; return new float3(math.dot(d, t.U), math.dot(d, t.V), math.dot(d, t.W)); }

        /// Ближайший зуб дуги: расстояние до поверхности, локальные координаты и номер.
        public static float TeethSdf(NativeArray<ToothDef> teeth, int from, int count, float3 p, out float3 q, out int idx)
        {
            float best = 9f; q = 0; idx = -1;
            for (int i = from; i < from + count; i++)
            {
                var t = teeth[i];
                float3 d = p - t.c;
                if (math.lengthsq(d) > math.square(t.hr + t.hc + 0.6f)) continue;
                float3 lq = new float3(math.dot(d, t.U), math.dot(d, t.V), math.dot(d, t.W));
                float s = ToothSdf(t, lq);
                if (s < best) { best = s; q = lq; idx = i; }
            }
            return best;
        }

        /// Все 32 зуба. Ширины по Wheeler ×0,85, коронки и корни верхних укорочены под веб-голову (между носом и ртом ниже реального).
        public static ToothDef[] MakeTeeth()
        {
            float[] upW = { .72f, .55f, .64f, .60f, .55f, .85f, .77f, .72f }, upB = { .62f, .55f, .70f, .80f, .78f, .95f, .92f, .85f };
            float[] upC = { 1.0f, .85f, .95f, .80f, .75f, .70f, .65f, .60f }, upR = { .95f, .95f, 1.15f, 1.0f, 1.0f, .95f, .9f, .8f };
            int[] upN = { 1, 1, 1, 2, 1, 3, 3, 3 };
            float[] loW = { .43f, .47f, .60f, .60f, .60f, .94f, .89f, .85f }, loB = { .52f, .55f, .65f, .68f, .70f, .90f, .86f, .82f };
            float[] loC = { .85f, .85f, .95f, .80f, .75f, .70f, .65f, .60f }, loR = { 1.15f, 1.2f, 1.4f, 1.3f, 1.3f, 1.3f, 1.2f, 1.1f };
            int[] loN = { 1, 1, 1, 1, 1, 2, 2, 2 };
            var list = new ToothDef[32];
            int n = 0;
            for (int jaw = 0; jaw < 2; jaw++)
            {
                bool up = jaw == 0;
                float a = up ? UpA : LoA, z0 = up ? UpZ0 : LoZ0, occ = up ? -5.24f : -5.28f;
                float[] W = up ? upW : loW, B = up ? upB : loB, C = up ? upC : loC, R = up ? upR : loR; int[] N = up ? upN : loN;
                for (int side = 0; side < 2; side++)
                {
                    float sx = side == 0 ? 1f : -1f;   // +x — левая сторона человека
                    float s = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        float sm = s + W[k] * 0.5f; s += W[k];
                        float tt = math.log(sm / a + math.sqrt(sm * sm / (a * a) + 1));
                        float x = a * tt, z = z0 - a * (math.cosh(tt) - 1);
                        float2 tan = math.normalize(new float2(1, -math.sinh(tt)));
                        var U = new float3(tan.x * sx, 0, tan.y);
                        var outw = new float3(math.sinh(tt) * sx, 0, 1); outw = math.normalize(outw);
                        float tilt = k <= 2 ? 0.25f : k <= 4 ? 0.12f : 0.05f;
                        var W0 = math.normalize(new float3(0, up ? 1 : -1, 0) - outw * tilt * (up ? 1 : 0.6f));
                        // ортонормировать: U в плоскости, перпендикулярной W
                        U = math.normalize(U - W0 * math.dot(U, W0));
                        var V = math.normalize(math.cross(W0, U));
                        if (math.dot(V, outw) < 0) { U = -U; V = -V; }
                        float cy = up ? occ + C[k] : occ - C[k];
                        var c = new float3(x * sx, cy, z) - W0 * 0f;
                        int fdi = (up ? (side == 1 ? 10 : 20) : (side == 1 ? 40 : 30)) + k + 1;
                        list[n++] = new ToothDef { c = c, U = U, V = V, W = W0, mw = W[k], bl = B[k], hc = C[k], hr = R[k], type = k, roots = N[k], upper = up ? 1 : 0, fdi = fdi };
                    }
                }
            }
            return list;
        }

        /// Ось нижней дуги для тела челюсти: (x, z, верх, низ) и полутолщина. Цепная линия через центры нижних зубов, затем к углу.
        public static void MakeMandArch(out float4[] pts, out float[] thick)
        {
            var p = new System.Collections.Generic.List<float4>(); var t = new System.Collections.Generic.List<float>();
            float a = LoA, z0 = LoZ0, total = 5.38f;
            for (int i = 0; i <= 14; i++)
            {
                float s = total * i / 14f, tt = math.log(s / a + math.sqrt(s * s / (a * a) + 1));
                float x = a * tt, z = z0 - a * (math.cosh(tt) - 1);
                float u = s / total;
                p.Add(new float4(x, z, math.lerp(-6.25f, -6.15f, u), math.lerp(-9.75f, -8.6f, math.sqrt(u))));
                t.Add(math.lerp(0.5f, 0.65f, math.saturate(s / 4.5f)));
            }
            p.Add(new float4(3.4f, 2.1f, -5.6f, -8.35f)); t.Add(0.6f);
            p.Add(new float4(4.25f, 1.3f, -5.9f, -8.1f)); t.Add(0.5f);
            p.Add(new float4(4.5f, 0.8f, -6.6f, -7.95f)); t.Add(0.45f);
            pts = p.ToArray(); thick = t.ToArray();
        }

        // ---------- слои покровов по зонам ----------
        public const int AnchorStride = 14;
        /// Опорные точки толщин (раздел 3): ax, y, z, σ, кожа, жир, фасция, рыхлый слой, мышца, надкостница, кость (мм), мышца, вид кости (0 нет, 1 кость, 2 хрящ, 3 тонкая компакта).
        public static readonly float[] Anchors =
        {
            0f,   6.8f,  8.2f, 2.0f,  1.4f, 5f,  0f,  2f, 1.5f, .5f, 8.5f, AnatPart.Frontalis, 1,
            3.2f, 6.5f,  7.4f, 1.8f,  1.4f, 5f,  0f,  2f, 1.5f, .5f, 8f,   AnatPart.Frontalis, 1,
            0f,   12.6f, -1f,  3.5f,  1.5f, 5f,  1.5f,2f, 0f,   .5f, 7f,   0, 1,
            5.8f, 9.0f,  -1.5f,3.0f,  1.5f, 5f,  1.5f,2f, 0f,   .5f, 7f,   0, 1,
            6.3f, 3.0f,  2.0f, 1.6f,  1.4f, 4.5f,1f,  0f, 12f,  .5f, 2f,   AnatPart.Temporalis, 3,
            6.5f, 4.6f,  -0.6f,1.6f,  1.4f, 4.5f,1f,  0f, 8f,   .5f, 2f,   AnatPart.Temporalis, 3,
            0f,   2.5f,  -10.2f,2.5f, 1.5f, 5.5f,0f,  0f, 3f,   .5f, 9.5f, AnatPart.Occipitalis, 1,
            4.5f, 3.0f,  -8.8f,2.2f,  1.5f, 5f,  1.5f,0f, 1.5f, .5f, 8f,   AnatPart.Occipitalis, 1,
            2.4f, 4.0f,  8.0f, 1.2f,  1.2f, 2f,  0f,  0f, 1.6f, .5f, 8f,   AnatPart.OrbOculi, 1,
            0.8f, 3.8f,  8.6f, 1.0f,  1.3f, 2f,  0f,  0f, 1.6f, .5f, 6f,   AnatPart.OrbOculi, 1,
            4.4f, 0.9f,  6.6f, 1.4f,  1.8f, 6f,  1.5f,0f, 3f,   .5f, 7f,   AnatPart.ZygMajor, 1,
            2.3f, -0.2f, 7.9f, 1.0f,  1.6f, 4f,  1f,  0f, 3f,   .5f, 2f,   AnatPart.ZygMinor, 1,
            3.8f, -4.5f, 5.8f, 1.6f,  1.8f, 8f,  1.5f,0f, 4f,   0f,  0f,   AnatPart.Buccinator, 0,
            5.2f, -5.0f, 1.0f, 1.8f,  1.8f, 5f,  1f,  0f, 11f,  0f,  0f,   AnatPart.Masseter, 0,
            0f,   -9.0f, 8.2f, 1.4f,  1.8f, 4f,  0f,  0f, 3f,   0f,  0f,   AnatPart.Mentalis, 0,
            0f,   1.6f,  9.2f, 0.8f,  1.6f, 1.5f,0f,  0f, 0f,   0f,  2f,   0, 1,
            0f,   -1.2f, 10.0f,0.9f,  1.8f, 1.5f,0f,  0f, 0f,   0f,  1.5f, 0, 2,
            0f,   -11f,  2.0f, 2.5f,  2f,   6f,  0f,  0f, 2f,   0f,  0f,   AnatPart.Platysma, 0,
            0f,   -8.0f, -7.0f,2.5f,  2f,   6f,  1.5f,0f, 0f,   0f,  0f,   0, 0,
            5.6f, -1.0f, -2.6f,1.4f,  1.5f, 4f,  1f,  0f, 2f,   .5f, 8f,   AnatPart.Scm, 1,
            6.8f, 5.5f,  -3.5f,2.0f,  1.5f, 5f,  1.5f,2f, 0f,   .5f, 6f,   0, 1,
            1.0f, -3.6f, 9.0f, 1.0f,  1.8f, 3f,  0f,  0f, 4f,   0f,  0f,   AnatPart.OrbOris, 0,
        };
        public const int AnchorCols = 13;

        /// Мышца по месту для клетки мышечного слоя.
        public static byte MuscleAt(float ax, float y, float z, byte dom)
        {
            float3 p = new float3(ax, y, z);
            if (z > 6.4f && (ax / 2.7f) * (ax / 2.7f) + ((y + 5.25f) / 1.7f) * ((y + 5.25f) / 1.7f) < 1) return AnatPart.OrbOris;
            if (SegDist(p, new float3(4.9f, 0.1f, 6.2f), new float3(2.3f, -5.0f, 7.9f)) < 0.45f) return AnatPart.ZygMajor;
            if (SegDist(p, new float3(3.7f, 0.6f, 7.3f), new float3(1.3f, -4.1f, 8.8f)) < 0.35f) return AnatPart.ZygMinor;
            if (z > 5.5f && y > -0.4f && math.length(new float2(ax - EX, y - EY)) < 2.4f) return AnatPart.OrbOculi;
            if (y > 4.6f && z > 4f) return AnatPart.Frontalis;
            if (dom != 0) return dom;
            if (y < -8f) return AnatPart.Platysma;
            return AnatPart.NeckDeep;
        }

        /// Глубокие мышцы (ниже слоёв покровов).
        public static byte DeepMuscle(float ax, float y, float z)
        {
            float3 p = new float3(ax, y, z);
            if (SdCap(p, new float3(1.7f, -1.0f, 2.6f), new float3(4.2f, -7.4f, 0.9f), 0.55f, 0.5f) < 0) return AnatPart.MedPterygoid;
            if (SdCap(p, new float3(2.0f, -0.6f, 2.8f), new float3(4.6f, -1.0f, 0.7f), 0.5f, 0.45f) < 0) return AnatPart.LatPterygoid;
            if (ax > 3.4f && y > -1.6f && y < 7f && z > -1.5f && z < 4.8f) return AnatPart.Temporalis;
            if (ax > 3.6f && y < -1f && z > -0.5f) return AnatPart.Masseter;
            if (ax > 3.8f && y < -1f && z < -0.5f) return AnatPart.Scm;
            return AnatPart.NeckDeep;
        }

        /// Направление волокон мышцы в точке (рамка головы, x со знаком).
        public static float3 Fiber(int part, float3 p)
        {
            float sx = p.x < 0 ? -1 : 1; float3 a = new float3(math.abs(p.x), p.y, p.z);
            float3 d;
            switch (part)
            {
                case AnatPart.Temporalis: d = new float3(4.5f, -1.4f, 2.4f) - a; break;
                case AnatPart.Masseter: d = new float3(-0.05f, -1, -0.25f); break;
                case AnatPart.Frontalis: case AnatPart.Occipitalis: case AnatPart.Platysma: case AnatPart.NeckDeep: d = new float3(0, 1, 0.05f); break;
                case AnatPart.Mentalis: d = new float3(0, 1, 0.3f); break;
                case AnatPart.OrbOculi: { float3 r = a - new float3(EX, EY, EZ); d = new float3(-r.y, r.x, 0); break; }
                case AnatPart.OrbOris: { float3 r = a - new float3(0, -5.25f, 8.6f); d = new float3(-r.y, r.x * 0.6f, 0); break; }
                case AnatPart.ZygMajor: d = new float3(2.3f, -5.0f, 7.9f) - new float3(4.9f, 0.1f, 6.2f); break;
                case AnatPart.ZygMinor: d = new float3(1.3f, -4.1f, 8.8f) - new float3(3.7f, 0.6f, 7.3f); break;
                case AnatPart.Buccinator: d = new float3(-0.3f, 0, 1); break;
                case AnatPart.MedPterygoid: d = new float3(4.2f, -7.4f, 0.9f) - new float3(1.7f, -1.0f, 2.6f); break;
                case AnatPart.LatPterygoid: d = new float3(4.6f, -1.0f, 0.7f) - new float3(2.0f, -0.6f, 2.8f); break;
                case AnatPart.Scm: d = new float3(-0.4f, -1, 0.5f); break;
                case AnatPart.RectusSup: case AnatPart.RectusInf: case AnatPart.RectusMed: case AnatPart.RectusLat: case AnatPart.ObliqueSup:
                    d = new float3(EX, EY, EZ) - Apex; break;
                case AnatPart.ObliqueInf: d = new float3(1.9f, 0.85f, -1.5f); break;
                default: d = new float3(0, 1, 0); break;
            }
            d = math.normalizesafe(d, new float3(0, 1, 0));
            d.x *= sx;
            return d;
        }

        /// Кость по месту (для швов). Порядок важен: лицевые кости раньше свода.
        public static byte BoneAt(float x, float y, float z)
        {
            float ax = math.abs(x); bool L = x >= 0;
            if (y < -3.6f && ax < 2.6f && z < 0.3f) return AnatPart.Vertebra;
            if (z > 7.55f && ax < 1.3f && y > 0.6f && y < 3.4f && !(y > 3.0f && ax > 0.6f)) return L ? AnatPart.NasalL : AnatPart.NasalR;
            if (ax < 0.16f && y < 0.4f && y > -3.0f && z < 7.0f && z > 2.8f) return AnatPart.Vomer;
            if (ax < 1.3f && y > 0.4f && y < 3.4f && z > 4.1f && z < 7.6f) return AnatPart.Ethmoid;
            if (y < -2.9f && y > -3.6f && ax < 2.2f && z > 2.8f && z < 4.1f) return AnatPart.Palatine;
            float wav = 0.12f * math.sin(y * 9f + ax * 5f) + 0.08f * math.sin(z * 11f);
            if (ax > 2.9f && y > -1.6f && y < 2.8f && z > 2.4f + wav && z < 7.8f && !(ax < 3.6f && y < 0f)) return L ? AnatPart.ZygomaticL : AnatPart.ZygomaticR;
            if (z > 2.8f && y < 1.6f + wav && ax < 4.0f && y > -5.0f) return L ? AnatPart.MaxillaL : AnatPart.MaxillaR;
            if ((ax < 2.2f && y > -1.6f && y < 2.4f && z > 0.8f && z < 4.4f) || (ax > 3.4f && z > 2.0f + wav && z < 4.3f && y > -0.6f && y < 3.8f)) return AnatPart.Sphenoid;
            float zcor = 1.0f + (12.5f - y) * 0.2f + wav;
            if (z > zcor && y > 1.6f) return AnatPart.Frontal;
            float zlam = -6.8f + 0.18f * (10.5f - y) + wav;
            if (z < zlam || (y < -0.8f && z < -2.6f)) return AnatPart.Occipital;
            float ysq = 4.2f - 0.08f * (z - 0.5f) * (z - 0.5f) + wav;
            if ((ax > 3.4f && y < ysq) || (ax > 2.0f && y < 1.4f && z < 1.2f && z > -3.2f)) return L ? AnatPart.TemporalL : AnatPart.TemporalR;
            return L ? AnatPart.ParietalL : AnatPart.ParietalR;
        }

        public static byte ZoneAt(float x, float y, float z)
        {
            float ax = math.abs(x);
            if (ax > 6.75f && y > -3.6f && y < 4.3f && z < 1.9f) return AnatZone.Ear;
            if (y < -9.0f && z < 4.5f) return AnatZone.Neck;
            if (ax > 5.4f && y > 0.8f && y < 6.8f && z > -2.5f && z < 4.2f) return AnatZone.Temple;
            if (z < 0.6f) { if (y < -3f) return AnatZone.Neck; if (z < -4f && y < 7.5f) return AnatZone.Occiput; return AnatZone.Scalp; }
            if (y > 4.7f) return z > 3.4f ? AnatZone.Fore : AnatZone.Scalp;
            if (y > 3.05f && ax < 5.7f) return AnatZone.Brow;
            if (ax < 1.8f && y > -3.35f && y <= 3.05f && z > 7.3f) return AnatZone.Nose;
            if (y > 0.4f && ax < 5.6f) return AnatZone.Eye;
            if (ax < 2.9f && y <= -3.2f && y > -7.0f && z > 6.0f) return AnatZone.Mouth;
            if (y > -0.4f && ax >= 2.6f) return AnatZone.CheekBone;
            if (y <= -6.2f || (y <= -3.6f && ax >= 4.2f)) return AnatZone.Jaw;
            return AnatZone.Cheek;
        }
    }
}
