using UnityEngine;

namespace VoxGame
{
    // Общие части боксёра для соперника и для рук игрока: формы (функции расстояния из веб-версии, см),
    // сборка сеток и двухзвенная рука (плечо → локоть → запястье → перчатка).
    public static class BoxerParts
    {
        public const float UpperLen = 0.30f;   // плечо → локоть, м
        public const float ForeLen  = 0.27f;   // локоть → запястье, м
        public const float GloveOff = 0.085f;  // запястье → центр кулака, м

        public const float ArmCell   = 0.65f;  // шаг решётки руки, см (до ARMS-BODY.md 0.8)
        public const float GloveCell = 0.45f;  // шаг решётки перчатки, см (до ARMS-BODY.md 0.55)

        // Тон кожи анатомической головы (AnatMat: «кожа», sRGB). Внешность головы не настраивается,
        // поэтому тело всегда этого тона, иначе шея и плечи разного цвета.
        public static readonly Color32 HeadSkin = FighterGen.HeadSkin;

        // Цвет татуировок
        public static readonly Color32 Ink = new Color32(38, 46, 70, 255);

        // Номера цветов перчатки
        public const byte GMain = 1, GCuff = 2, GPalm = 3, GLace = 4, GStud = 5, GEdge = 6, GSeam = 7, GFold = 8;
        // Номера цветов руки: кожа, тень в бороздах, свет на выпуклостях (тон вокселями)
        public const byte ASkin = 1, AShade = 4, ALight = 5;
        public const float ShadeK = 0.86f, LightK = 1.07f;

        public static Color32 Tone(Color32 c, float k) =>
            new Color32((byte)Mathf.Min(255f, c.r * k), (byte)Mathf.Min(255f, c.g * k), (byte)Mathf.Min(255f, c.b * k), 255);

        public static float Bump(float v, float c, float w) => Mathf.Max(0f, 1f - Mathf.Abs(v - c) / w);

        // ---- Формы ----------------------------------------------------------------------------

        // Плечо: начало в плечевом суставе, ось +Y к локтю.
        // +Z — бицепс (к подсказке back), −Z — трицепс и локтевой отросток.
        public static float UpperArm(float x, float y, float z)
        {
            float d = VoxModel.Cap(x, y, z, 0, 0, 0, 0, 29, 0, 4.4f, 3.5f);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 4.0f, z, 5.0f, 6.6f, 4.9f), 1.6f);                // дельта — шапка плеча
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 16.5f, z + 1.6f, 3.8f, 7.2f, 3.9f), 1.1f);        // бицепс
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 13.5f, z - 1.8f, 4.0f, 8.2f, 3.7f), 1.1f);        // трицепс
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 29.0f, z - 2.0f, 2.1f, 2.4f, 1.7f), 1.0f);        // локтевой отросток
            d = VoxModel.Smin(d, VoxModel.Ell(Mathf.Abs(x) - 3.0f, y - 28.3f, z, 1.3f, 1.6f, 1.4f), 0.9f);   // надмыщелки
            d += 0.30f * Bump(y, 8.6f, 1.3f) * Bump(z, 0f, 4f);                                    // борозда под дельтой
            return d;
        }

        public static byte UpperArmPaint(float x, float y, float z, bool band)
        {
            if (band)
            {
                // браслет: широкая полоса с зубцами вниз к локтю и тонкая линия под ней
                float a = Mathf.Atan2(z, x);
                float tooth = Mathf.Abs(Mathf.Repeat(a * 6f / Mathf.PI, 2f) - 1f);
                if (y > 11.6f && y < 13.4f + 1.9f * tooth) return 2;
                if (y > 9.8f && y < 10.7f) return 2;
            }
            if (Mathf.Abs(y - 8.6f) < 0.9f || (y > 25.5f && y < 27.2f && z > 0.5f)) return AShade;  // борозда под дельтой, локтевой сгиб
            if (y > 13f && y < 20f && z > 2.6f) return ALight;                                           // верх бицепса
            if (y < 5f && Mathf.Abs(x) < 3f && z * z + x * x > 16f) return ALight;                       // шапка дельты
            return ASkin;
        }

        // Предплечье: начало в локте, ось +Y к запястью.
        // +Z — тыльная сторона (разгибатели), −Z — сгибатели; запястье уплощено.
        public static float ForeArm(float x, float y, float z)
        {
            float d = VoxModel.Cap(x, y, z, 0, 0, 0, 0, 27, 0, 4.0f, 2.6f);
            d = VoxModel.Smin(d, VoxModel.Ell(x - 1.0f, y - 7.5f, z - 0.9f, 3.5f, 7.5f, 3.0f), 1.3f);   // плечелучевая и разгибатели
            d = VoxModel.Smin(d, VoxModel.Ell(x + 0.9f, y - 8.5f, z + 1.0f, 3.3f, 8.0f, 3.0f), 1.3f);   // сгибатели
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 25.8f, z, 3.1f, 2.2f, 2.2f), 1.0f);               // запястье
            d = VoxModel.Smin(d, VoxModel.Ell(x, y + 0.6f, z + 2.2f, 1.9f, 2.0f, 1.5f), 0.9f);          // локтевой отросток
            d += 0.28f * Bump(y, 3.0f, 1.1f) * Mathf.Clamp01(z / 2f);                                // локтевой сгиб
            d += 0.18f * Bump(x, -0.4f, 0.8f) * Bump(y, 15f, 9f) * Mathf.Clamp01(-z / 2f);              // борозда по локтевой кости
            return d;
        }

        public static byte ForeArmPaint(float x, float y, float z)
        {
            if ((y > 2f && y < 4.2f && z > 0.8f) || (Mathf.Abs(x + 0.4f) < 0.7f && y > 9f && y < 21f && z < -1.5f)) return AShade;
            if (y > 3.5f && y < 13f && x > 1.2f && z > 1.0f) return ALight;                            // верх плечелучевой
            if (y > 24.6f && Mathf.Abs(z) < 1.2f && Mathf.Abs(x) > 2.4f) return AShade;              // складка запястья
            return ASkin;
        }

        // Перчатка: центр в кулаке, +Z к костяшкам, +Y — тыльная сторона, большой палец со стороны thumb (±1 по X).
        // k — общий масштаб, kx — отдельно ширина (формы из каталога перчаток).
        public static float Glove(float x, float y, float z, int thumb, float k, float kx, bool studs)
        {
            x /= k * kx; y /= k; z /= k;
            float d = Fist(x, y, z, thumb);
            d = VoxModel.Smin(d, CuffShape(x, y, z, -15.2f, 4.7f, 4.9f), 2.2f);
            if (studs)
                for (int i = 0; i < 4; i++)
                    d = Mathf.Min(d, VoxModel.Ell(x - (-4.2f + 2.8f * i), y - 2.6f, z - 7.6f, 1.1f, 1.1f, 1.5f));
            return d * k * Mathf.Min(1f, kx);
        }

        // Кулак перчатки (оси перчатки, см): объём с подвёрнутыми пальцами снизу спереди, большой палец отдельным валиком
        // со складкой (малое сглаживание), костяшки — ряд мягких бугров. Общий для соперника и игрока.
        public static float Fist(float x, float y, float z, int thumb)
        {
            float d = VoxModel.Ell(x, y, z - 1.0f, 6.4f, 5.6f, 7.6f);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 0.6f, z - 5.4f, 6.2f, 4.9f, 4.0f), 2.5f);
            d = VoxModel.Smin(d, VoxModel.Cap(x, y, z, -4.0f, -2.9f, 5.9f, 4.0f, -2.9f, 5.9f, 2.7f), 1.4f);    // подвёрнутые пальцы
            for (int i = 0; i < 4; i++)
                d = VoxModel.Smin(d, VoxModel.Ell(x - (-3.6f + 2.4f * i), y - 2.6f, z - 8.0f, 1.5f, 1.4f, 1.3f), 0.9f);   // костяшки
            float th = VoxModel.Cap(x, y, z, thumb * 5.4f, -1.4f, -2.6f, thumb * 4.3f, -2.4f, 4.6f, 2.3f, 2.0f);
            th = Mathf.Min(th, VoxModel.Ell(x - thumb * 4.1f, y + 2.5f, z - 4.9f, 2.1f, 1.9f, 2.0f));          // подушка пальца
            d = VoxModel.Smin(d, th, 0.55f);
            d += 0.22f * Bump(z, 3.6f, 0.7f) * Mathf.Clamp01((-y - 2.0f) / 1.5f);                       // складка пальцы|ладонь
            return d;
        }

        // Манжета от кисти назад до zEnd: плоский срез, кант на конце, складка посередине, внутри открыта (тёмная подкладка),
        // чтобы к камере смотрело кольцо, а не белый диск.
        public static float CuffShape(float x, float y, float z, float zEnd, float r0, float r1)
        {
            float c = VoxModel.Cap(x, y, z, 0, -0.4f, -4.5f, 0, -0.4f, zEnd, r0, r1);
            float zf = -4.5f + (zEnd + 4.5f) * 0.45f;
            c += 0.32f * Bump(z, zf, 0.6f);                     // складка
            c -= 0.32f * Bump(z, zEnd + 0.55f, 0.6f);           // кант
            c = Mathf.Max(c, zEnd - z);
            float rr = r1 - 0.95f, ry = y + 0.4f;
            float hole = Mathf.Max(Mathf.Sqrt(x * x + ry * ry) - rr, z - (zEnd + 1.8f));
            return Mathf.Max(c, -hole);
        }

        // Отделка манжеты: кант и подкладка, складка, полоса цвета перчатки («логотип»), шнуровка крест-накрест
        // по тыльной стороне с люверсами. c — базовый номер цвета, cuff — номер цвета манжеты; остальное — номера палитры.
        // r1 — радиус манжеты у среза (как в CuffShape): внутренность отверстия — подкладка.
        public static byte CuffDetail(byte c, byte cuff, float x, float y, float z, float zEnd, float r1, byte edge, byte fold, byte logo, byte lace)
        {
            if (z < zEnd + 1.05f) return edge;
            if (z < zEnd + 2.6f && x * x + (y + 0.4f) * (y + 0.4f) < (r1 - 0.5f) * (r1 - 0.5f)) return edge;
            if (c != cuff) return c;
            float zf = -4.5f + (zEnd + 4.5f) * 0.45f;
            if (Mathf.Abs(z - zf) < 0.32f) return fold;
            if (z > zEnd + 1.9f && z < zEnd + 3.3f) return logo;
            if (y > 2.4f && z > zf + 0.4f && z < -5.3f)
            {
                float ax = Mathf.Abs(x);
                float ph = Mathf.Abs(Mathf.Repeat(z * 0.62f, 1f) - 0.5f) * 2f;      // 0..1 вдоль ряда
                if (ax < 2.3f && Mathf.Abs(ax - 2.0f * ph) < 0.42f) return lace;   // крест-накрест
                if (ax > 2.0f && ax < 2.7f && ph > 0.8f) return edge;               // люверсы
            }
            return c;
        }

        static byte GlovePaint(float x, float y, float z, float k, float kx, bool studs, int thumb)
        {
            x /= k * kx; y /= k; z /= k;
            if (z < -6.8f) return CuffDetail(GCuff, GCuff, x, y, z, -15.2f, 4.9f, GEdge, GFold, GMain, GLace);   // манжета
            if (Mathf.Abs(y + 0.7f) < 0.33f && Mathf.Abs(x) > 4.6f && z < 6.5f) return GSeam;           // боковой шов
            if (Mathf.Abs(x - thumb * 3.0f) < 0.35f && y < -0.6f && z > -2.5f && z < 4.8f) return GSeam;  // шов у большого пальца
            if (studs && z > 7.2f && y > 1.2f && y < 4.2f) {               // шипы на костяшках
                float fx = Mathf.Repeat(x + 4.2f + 1.4f, 2.8f) - 1.4f;
                if (Mathf.Abs(x) < 5.6f && fx * fx + (y - 2.6f) * (y - 2.6f) < 1.3f) return GStud;
            }
            if (y > 3.0f && Mathf.Abs(x) < 1.3f && z < -1.5f) return GLace;  // шнуровка на тыльной стороне
            if (y < -3.6f && z < 4f) return GPalm;                         // ладонь темнее
            if (y < -2.1f && z > 3.0f && z < 4.2f) return GPalm;           // складка пальцы|ладонь
            return GMain;
        }

        // ---- Сетки ----------------------------------------------------------------------------

        // Плечо. thick — толщина (поперёк оси руки), band — татуировка-браслет с зубцами.
        public static Mesh UpperArmMesh(Color32 skin, float thick = 1f, bool band = false)
        {
            float k = Mathf.Max(0.5f, thick);
            var mn = new Vector3(-6.4f * k, -5.6f, -6.4f * k); var mx = new Vector3(6.4f * k, 34.4f, 6.4f * k);
            var m = VoxModel.FromSdf(mn, mx, ArmCell,
                (x, y, z) => UpperArm(x / k, y, z / k) * Mathf.Min(1f, k),
                (x, y, z) => UpperArmPaint(x / k, y, z / k, band), true);
            return m.BuildMesh("upperArm", new[] { default, skin, Ink, default, Tone(skin, ShadeK), Tone(skin, LightK) },
                               mn * 0.01f, ArmCell * 0.01f);
        }

        // Предплечье. thick — толщина, wraps — бинты у запястья (видны между локтем и манжетой перчатки).
        public static Mesh ForeArmMesh(Color32 skin, float thick = 1f, bool wraps = false, Color32 wrapColor = default)
        {
            float k = Mathf.Max(0.5f, thick);
            var mn = new Vector3(-5.6f * k, -4.8f, -5.6f * k); var mx = new Vector3(5.6f * k, 30.4f, 5.6f * k);
            var m = VoxModel.FromSdf(mn, mx, ArmCell,
                (x, y, z) =>
                {
                    float d = ForeArm(x / k, y, z / k) * Mathf.Min(1f, k);
                    return wraps && y > 14.2f ? d - 0.35f : d;   // бинт чуть толще кожи
                },
                (x, y, z) =>
                {
                    if (!wraps || y < 14.2f) return ForeArmPaint(x / k, y, z / k);
                    // витки бинта наискось: два тона
                    float a = Mathf.Atan2(z, x) / Mathf.PI;
                    return Mathf.Repeat(y * 0.55f + a, 1f) < 0.5f ? (byte)2 : (byte)3;
                }, true);
            Color32 wd = Color.Lerp(wrapColor, Color.black, 0.18f);
            return m.BuildMesh("foreArm", new[] { default, skin, wrapColor, wd, Tone(skin, ShadeK), Tone(skin, LightK) },
                               mn * 0.01f, ArmCell * 0.01f);
        }

        public static Mesh GloveMesh(int thumb, Color a, Color b, float k = 1f, float kx = 1f, bool studs = false)
        {
            var mn = new Vector3(-8.25f * k * Mathf.Max(1f, kx), -6.6f * k, -17.05f * k);
            var mx = new Vector3(8.25f * k * Mathf.Max(1f, kx), 6.6f * k, 11f * k);
            var m = VoxModel.FromSdf(mn, mx, GloveCell,
                (x, y, z) => Glove(x, y, z, thumb, k, kx, studs),
                (x, y, z) => GlovePaint(x, y, z, k, kx, studs, thumb), true);
            Color32 main = a, cuff = b;
            Color32 palm = Color.Lerp(a, Color.black, 0.3f);
            var pal = new[] { default, main, cuff, palm, new Color32(236, 230, 214, 255), new Color32(196, 198, 204, 255),
                              (Color32)Color.Lerp(b, new Color(0.08f, 0.07f, 0.07f), 0.7f), (Color32)Color.Lerp(a, Color.black, 0.4f),
                              (Color32)Color.Lerp(b, Color.black, 0.24f) };
            return m.BuildMesh("glove", pal, mn * 0.01f, GloveCell * 0.01f);
        }

        // ---- Рука ------------------------------------------------------------------------------

        // h, сделанный перпендикулярным единичной оси ax; не вырождается.
        static Vector3 PerpTo(Vector3 h, Vector3 ax)
        {
            Vector3 o = h - ax * Vector3.Dot(h, ax);
            if (o.sqrMagnitude < 1e-4f)
            {
                o = Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.forward;
                o -= ax * Vector3.Dot(o, ax);
            }
            return o.normalized;
        }

        // Звено от A к B: ось +Y вдоль звена, +Z — по подсказке hint.
        static void SegTo(Transform t, Vector3 a, Vector3 b, Vector3 hint)
        {
            Vector3 by = b - a; float len = by.magnitude;
            if (len < 1e-5f) return;
            by /= len;
            t.SetPositionAndRotation(a, Quaternion.LookRotation(PerpTo(hint, by), by));
        }

        // Одна рука. S — плечо, gt — желаемый центр перчатки, k — желаемое направление костяшек,
        // pole — куда смотрит локоть, back — куда смотрит тыльная сторона кисти. Запястье на GloveOff позади перчатки,
        // вылет ограничен длиной руки (звенья не растягиваются), перчатка идёт по оси предплечья с небольшим
        // доворотом к k. Все точки — в мировых осях. upper может быть null (у игрока плечо не рисуется).
        // Возвращает настоящий центр перчатки.
        public static Vector3 SolveArm(Transform upper, Transform fore, Transform glove, Vector3 S, Vector3 gt,
                                       Vector3 k, Vector3 pole, Vector3 back, float scale = 1f)
        {
            float l1 = UpperLen * scale, l2 = ForeLen * scale, go = GloveOff * scale;
            k = k.normalized;
            Vector3 w = gt - k * go;
            Vector3 u = w - S; float d = u.magnitude, reach = (l1 + l2) * 0.985f, near = 0.12f * scale;
            if (d > reach) w = S + u * (reach / d);
            else if (d < near && d > 1e-5f) w = S + u * (near / d);

            // локоть: треугольник со сторонами l1, l2 и расстоянием плечо–запястье, в сторону pole
            u = w - S; d = u.magnitude;
            Vector3 e;
            if (d < 1e-5f) e = S + Vector3.down * l1;
            else
            {
                u /= d;
                float dd = Mathf.Min(d, (l1 + l2) * 0.999f);
                float a = (l1 * l1 - l2 * l2 + dd * dd) / (2f * dd);
                float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
                Vector3 pp = pole - u * Vector3.Dot(pole, u);
                pp = pp.sqrMagnitude < 1e-10f ? Vector3.down : pp.normalized;
                e = S + u * (a * d / dd) + pp * h;
            }

            Vector3 ax = (w - e).normalized;
            Vector3 ga = (ax * 0.84f + k * 0.16f).normalized;
            Vector3 c = w + ga * go;
            if (upper != null) SegTo(upper, S, e, back);
            SegTo(fore, e, w, PerpTo(back, ax));
            glove.SetPositionAndRotation(c, Quaternion.LookRotation(ga, PerpTo(back, ga)));
            return c;
        }
    }
}
