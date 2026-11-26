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

        public const float ArmCell   = 0.8f;   // шаг решётки руки, см
        public const float GloveCell = 0.55f;  // шаг решётки перчатки, см

        // Тон кожи анатомической головы (AnatMat: «кожа», sRGB). Внешность головы не настраивается,
        // поэтому тело всегда этого тона, иначе шея и плечи разного цвета.
        public static readonly Color32 HeadSkin = FighterGen.HeadSkin;

        // Цвет татуировок
        public static readonly Color32 Ink = new Color32(38, 46, 70, 255);

        // Номера цветов перчатки
        public const byte GMain = 1, GCuff = 2, GPalm = 3, GLace = 4, GStud = 5;

        // ---- Формы ----------------------------------------------------------------------------

        // Плечо: начало в плечевом суставе, ось +Y к локтю.
        public static float UpperArm(float x, float y, float z)
        {
            float d = VoxModel.Cap(x, y, z, 0, 0, 0, 0, 29, 0, 4.5f, 3.8f);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 15, z - 1.3f, 4.1f, 7.5f, 4.3f), 2);
            return VoxModel.Smin(d, VoxModel.Ell(x, y - 14, z + 1.5f, 4.2f, 8.5f, 4.0f), 2);
        }

        // Предплечье: начало в локте, ось +Y к запястью.
        public static float ForeArm(float x, float y, float z)
        {
            float d = VoxModel.Cap(x, y, z, 0, 0, 0, 0, 27, 0, 4.2f, 2.9f);
            return VoxModel.Smin(d, VoxModel.Ell(x - 0.6f, y - 7, z - 0.6f, 3.9f, 7, 3.5f), 2);
        }

        // Перчатка: центр в кулаке, +Z к костяшкам, +Y — тыльная сторона, большой палец со стороны thumb (±1 по X).
        // k — общий масштаб, kx — отдельно ширина (формы из каталога перчаток).
        public static float Glove(float x, float y, float z, int thumb, float k, float kx, bool studs)
        {
            x /= k * kx; y /= k; z /= k;
            float d = VoxModel.Ell(x, y, z - 1.0f, 6.4f, 5.6f, 7.6f);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 0.6f, z - 5.4f, 6.2f, 4.9f, 4.0f), 2.5f);
            d = VoxModel.Smin(d, VoxModel.Cap(x, y, z, thumb * 5.2f, -1.5f, -2.8f, thumb * 4.6f, -2.0f, 4.4f, 2.4f), 1.6f);
            d = VoxModel.Smin(d, VoxModel.Cap(x, y, z, 0, -0.4f, -11.5f, 0, -0.4f, -4.5f, 4.8f), 2.2f);
            if (studs)
                for (int i = 0; i < 4; i++)
                    d = Mathf.Min(d, VoxModel.Ell(x - (-4.2f + 2.8f * i), y - 2.6f, z - 7.6f, 1.1f, 1.1f, 1.5f));
            return d * k * Mathf.Min(1f, kx);
        }

        static byte GlovePaint(float x, float y, float z, float k, float kx, bool studs)
        {
            x /= k * kx; y /= k; z /= k;
            if (z < -6.8f) return GCuff;                                   // манжета
            if (studs && z > 7.2f && y > 1.2f && y < 4.2f) {               // шипы на костяшках
                float fx = Mathf.Repeat(x + 4.2f + 1.4f, 2.8f) - 1.4f;
                if (Mathf.Abs(x) < 5.6f && fx * fx + (y - 2.6f) * (y - 2.6f) < 1.3f) return GStud;
            }
            if (y > 3.0f && Mathf.Abs(x) < 1.3f && z < -1.5f) return GLace;  // шнуровка на тыльной стороне
            if (y < -3.6f && z < 4f) return GPalm;                         // ладонь темнее
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
                (x, y, z) => band ? ArmBandPaint(x, y, z) : (byte)1);
            return m.BuildMesh("upperArm", new[] { default, skin, Ink }, mn * 0.01f, ArmCell * 0.01f);
        }

        // Браслет: широкая полоса с зубцами вниз к локтю и тонкая линия под ней
        static byte ArmBandPaint(float x, float y, float z)
        {
            float a = Mathf.Atan2(z, x);
            float tooth = Mathf.Abs(Mathf.Repeat(a * 6f / Mathf.PI, 2f) - 1f);   // 0..1, шесть зубцов по кругу
            if (y > 11.6f && y < 13.4f + 1.9f * tooth) return 2;
            if (y > 9.8f && y < 10.7f) return 2;
            return 1;
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
                    if (!wraps || y < 14.2f) return 1;
                    // витки бинта наискось: два тона
                    float a = Mathf.Atan2(z, x) / Mathf.PI;
                    return Mathf.Repeat(y * 0.55f + a, 1f) < 0.5f ? (byte)2 : (byte)3;
                });
            Color32 wd = Color.Lerp(wrapColor, Color.black, 0.18f);
            return m.BuildMesh("foreArm", new[] { default, skin, wrapColor, wd }, mn * 0.01f, ArmCell * 0.01f);
        }

        public static Mesh GloveMesh(int thumb, Color a, Color b, float k = 1f, float kx = 1f, bool studs = false)
        {
            var mn = new Vector3(-8.25f * k * Mathf.Max(1f, kx), -6.6f * k, -17.05f * k);
            var mx = new Vector3(8.25f * k * Mathf.Max(1f, kx), 6.6f * k, 11f * k);
            var m = VoxModel.FromSdf(mn, mx, GloveCell,
                (x, y, z) => Glove(x, y, z, thumb, k, kx, studs),
                (x, y, z) => GlovePaint(x, y, z, k, kx, studs));
            Color32 main = a, cuff = b;
            Color32 palm = Color.Lerp(a, Color.black, 0.3f);
            var pal = new[] { default, main, cuff, palm, new Color32(236, 230, 214, 255), new Color32(196, 198, 204, 255) };
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
                                       Vector3 k, Vector3 pole, Vector3 back, float scale = 1f, float nearM = 0.12f)
        {
            float l1 = UpperLen * scale, l2 = ForeLen * scale, go = GloveOff * scale;
            k = k.normalized;
            Vector3 w = gt - k * go;
            Vector3 u = w - S; float d = u.magnitude, reach = (l1 + l2) * 0.985f, near = nearM * scale;   // nearM: плечо–запястье не ближе (сгиб локтя)
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
