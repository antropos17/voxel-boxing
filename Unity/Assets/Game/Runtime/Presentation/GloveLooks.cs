using UnityEngine;

namespace VoxGame
{
    // Форма перчатки игрока. Номер формы = GloveEntry.shapeTag.
    public enum GloveShape { Classic, Heavy, Speed, Charged, Armored, Spiked, Champion, Wraps }

    // Вид перчаток: основной цвет, манжета, отделка (шнуровка, пластины, шипы, звезда, лента) и форма.
    public struct GloveLook
    {
        public string     id;
        public GloveShape shape;
        public Color      main, cuff, trim;

        // Вид записи каталога; для id без описания в GloveLooks — по цветам и shapeTag самой записи.
        public static GloveLook FromEntry(GloveEntry e)
        {
            if (GloveLooks.Has(e.id)) return GloveLooks.Get(e.id);
            return new GloveLook
            {
                id = e.id, shape = (GloveShape)Mathf.Clamp(e.shapeTag, 0, (int)GloveShape.Wraps),
                main = e.colorA, cuff = e.colorB, trim = e.colorB,
            };
        }
    }

    // Виды перчаток каталога и построение их воксельных сеток.
    // Оси перчатки (см): центр в кулаке, +Z к костяшкам, +Y — тыльная сторона, большой палец со стороны thumb (±1 по X).
    // Запястье — на BoxerParts.GloveOff позади центра (z = −8.5 см).
    public static class GloveLooks
    {
        // Рука игрока из двух звеньев (до масштаба PlayerGloves): предплечье запястье → локоть и плечо локоть → плечевой
        // сустав. Плечо PlayerGloves растягивает по длине до сустава под кадром, предплечье — нет.
        public const float ForeLenCm  = 30f;
        public const float UpperLenCm = 40f;

        static readonly GloveLook[] Looks =
        {
            Look("classic",  GloveShape.Classic,  new Color(0.87f, 0.13f, 0.13f), new Color(0.96f, 0.95f, 0.92f), new Color(1.00f, 0.98f, 0.90f)),
            Look("heavy",    GloveShape.Heavy,    new Color(0.29f, 0.18f, 0.10f), new Color(0.78f, 0.63f, 0.25f), new Color(0.13f, 0.08f, 0.05f)),
            Look("speed",    GloveShape.Speed,    new Color(0.00f, 0.80f, 1.00f), new Color(0.00f, 0.27f, 0.67f), new Color(0.95f, 0.97f, 1.00f)),
            Look("mega",     GloveShape.Charged,  new Color(0.53f, 0.00f, 0.80f), new Color(1.00f, 0.67f, 0.00f), new Color(1.00f, 0.95f, 0.35f)),
            Look("armor",    GloveShape.Armored,  new Color(0.24f, 0.26f, 0.29f), new Color(0.10f, 0.11f, 0.13f), new Color(0.80f, 0.82f, 0.86f)),
            Look("spike",    GloveShape.Spiked,   new Color(0.07f, 0.07f, 0.07f), new Color(1.00f, 0.27f, 0.00f), new Color(0.82f, 0.84f, 0.88f)),
            Look("champion", GloveShape.Champion, new Color(1.00f, 0.76f, 0.15f), new Color(0.80f, 0.08f, 0.10f), new Color(1.00f, 1.00f, 1.00f)),
            Look("wraps",    GloveShape.Wraps,    new Color(0.95f, 0.92f, 0.84f), new Color(0.78f, 0.74f, 0.64f), new Color(0.85f, 0.12f, 0.12f)),
        };

        static GloveLook Look(string id, GloveShape sh, Color main, Color cuff, Color trim) =>
            new GloveLook { id = id, shape = sh, main = main, cuff = cuff, trim = trim };

        public static int Count => Looks.Length;
        public static GloveLook At(int i) => Looks[i];

        public static bool Has(string id)
        {
            if (id == null) return false;
            for (int i = 0; i < Looks.Length; i++) if (Looks[i].id == id) return true;
            return false;
        }

        // Вид по id каталога; неизвестный id — классика.
        public static GloveLook Get(string id)
        {
            if (id != null)
                for (int i = 0; i < Looks.Length; i++) if (Looks[i].id == id) return Looks[i];
            return Looks[0];
        }

        // ---- Витрина ----------------------------------------------------------------------------

        // Одна перчатка по id под parent (экран магазина, лист перчаток). Оси как у перчатки (см. выше), размер 1:1
        // (в бою руки игрока в масштабе 0.86). Удалять через DestroyShowcase: сетка своя у каждой витрины.
        public static Transform SpawnShowcase(string gloveId, Transform parent, Material mat, int thumb = -1)
        {
            var look = Get(gloveId);
            return VoxModel.Spawn("glove_" + look.id, parent, BuildGloveMesh(look, thumb), mat);
        }

        public static void DestroyShowcase(Transform t)
        {
            if (t == null) return;
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null) GameUtil.Destroy(mf.sharedMesh);
            GameUtil.Destroy(t.gameObject);
        }

        // ---- Сетки ------------------------------------------------------------------------------

        const byte CMain = 1, CCuff = 2, CPalm = 3, CTrim = 4, CExtra = 5, CSkin = 6;

        public static Mesh BuildGloveMesh(GloveLook look, int thumb)
        {
            var mn = new Vector3(-11.5f, -8.5f, -15.5f);
            var mx = new Vector3(11.5f, 10f, 13.5f);
            var sh = look.shape;
            int th = thumb < 0 ? -1 : 1;
            var m = VoxModel.FromSdf(mn, mx, BoxerParts.GloveCell,
                (x, y, z) => Sdf(sh, th, x, y, z),
                (x, y, z) => PaintAt(sh, th, x, y, z));
            Color extra = sh == GloveShape.Armored ? new Color(0.12f, 0.12f, 0.14f)
                        : sh == GloveShape.Spiked  ? look.trim
                        : Color.Lerp(look.trim, Color.black, 0.35f);
            var pal = new Color32[]
            {
                default, look.main, look.cuff, Color.Lerp(look.main, Color.black, 0.3f), look.trim, extra, BoxerParts.HeadSkin,
            };
            var mesh = m.BuildMesh("glove_" + look.id, pal, mn * 0.01f, BoxerParts.GloveCell * 0.01f);
            Sil.Add(mesh, SilPoints((x, y, z) => Sdf(sh, th, x, y, z), mn, mx));
            return mesh;
        }

        // Силуэт перчатки и предплечья для места плашки удара (FxDirector): точки у поверхности SDF с шагом SilStepCm в координатах сетки (м).
        // Сетка на ЦП не читается, поэтому точки берутся из той же SDF при постройке.
        public const float SilStepCm = 1.5f;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Mesh, Vector3[]> Sil =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Mesh, Vector3[]>();
        public static Vector3[] Silhouette(Mesh m) => m != null && Sil.TryGetValue(m, out var p) ? p : null;

        static Vector3[] SilPoints(VoxModel.Sdf sdf, Vector3 mn, Vector3 mx)
        {
            var l = new System.Collections.Generic.List<Vector3>(2048);
            for (float x = mn.x + SilStepCm * 0.5f; x < mx.x; x += SilStepCm)
            for (float y = mn.y + SilStepCm * 0.5f; y < mx.y; y += SilStepCm)
            for (float z = mn.z + SilStepCm * 0.5f; z < mx.z; z += SilStepCm)
            {
                if (sdf(x, y, z) >= 0f) continue;
                float h = SilStepCm;   // только точки у поверхности: силуэт тот же, точек в разы меньше
                if (sdf(x + h, y, z) < 0f && sdf(x - h, y, z) < 0f && sdf(x, y + h, z) < 0f
                    && sdf(x, y - h, z) < 0f && sdf(x, y, z + h) < 0f && sdf(x, y, z - h) < 0f) continue;
                l.Add(new Vector3(x, y, z) * 0.01f);
            }
            return l.ToArray();
        }

        // Предплечье игрока: запястье в начале координат, локоть на −ForeLenCm по Y (с округлым локтевым суставом).
        // У бинтов предплечье замотано от запястья.
        public static Mesh BuildForeArmMesh(GloveLook look)
        {
            bool wraps = look.shape == GloveShape.Wraps;
            var mn = new Vector3(-6f, -ForeLenCm - 5.6f, -6f);
            var mx = new Vector3(6f, 3.6f, 6f);
            VoxModel.Sdf sdf = (x, y, z) =>
            {
                float d = VoxModel.Cap(x, y, z, 0, 0, 0, 0, -ForeLenCm, 0, 2.9f, 4.3f);
                d = VoxModel.Smin(d, VoxModel.Ell(x - 0.6f, y + ForeLenCm * 0.7f, z - 0.6f, 3.9f, 7f, 3.5f), 2f);
                return wraps && y > -12f ? d - 0.35f : d;
            };
            var m = VoxModel.FromSdf(mn, mx, BoxerParts.ArmCell, sdf,
                (x, y, z) =>
                {
                    if (!wraps || y < -12f) return 1;
                    if (y < -10.8f) return 4;   // лента по краю бинта
                    float a = Mathf.Atan2(z, x) / Mathf.PI;
                    return Mathf.Repeat(y * 0.55f + a, 1f) < 0.55f ? (byte)2 : (byte)3;
                });
            var pal = new Color32[] { default, BoxerParts.HeadSkin, look.main, look.cuff, look.trim };
            var mesh = m.BuildMesh("playerForeArm", pal, mn * 0.01f, BoxerParts.ArmCell * 0.01f);
            Sil.Add(mesh, SilPoints(sdf, mn, mx));
            return mesh;
        }

        // Плечо игрока: локоть в начале координат, плечевой сустав на −UpperLenCm по Y. Одно на все виды перчаток.
        public static Mesh BuildUpperArmMesh()
        {
            var mn = new Vector3(-6.4f, -UpperLenCm - 6f, -6.4f);
            var mx = new Vector3(6.4f, 5.2f, 6.4f);
            var m = VoxModel.FromSdf(mn, mx, BoxerParts.ArmCell,
                (x, y, z) =>
                {
                    float d = VoxModel.Cap(x, y, z, 0, 0, 0, 0, -UpperLenCm, 0, 4.4f, 4.9f);
                    return VoxModel.Smin(d, VoxModel.Ell(x, y + UpperLenCm * 0.5f, z - 1.3f, 4.3f, 8.5f, 4.5f), 2f);
                },
                (x, y, z) => 1);
            var pal = new Color32[] { default, BoxerParts.HeadSkin };
            return m.BuildMesh("playerUpperArm", pal, mn * 0.01f, BoxerParts.ArmCell * 0.01f);
        }

        // ---- Формы (см) -------------------------------------------------------------------------

        static float Sdf(GloveShape sh, int th, float x, float y, float z)
        {
            switch (sh)
            {
                case GloveShape.Heavy:
                {
                    // крупнее, валик над костяшками делает перед квадратным, толстая манжета
                    float d = Mitt(x, y, z, th, 1.16f, 1.06f, 1f, 1f);
                    d = VoxModel.Smin(d, VoxModel.Cap(x, y, z, -5.6f, 2.5f, 8.6f, 5.6f, 2.5f, 8.6f, 3.6f), 1.8f);
                    return VoxModel.Smin(d, Cuff(x, y, z, -13.5f, 5.9f, 6.1f), 2.2f);
                }
                case GloveShape.Speed:
                {
                    // узкая вытянутая, гребень по тыльной стороне, короткая манжета
                    float d = Mitt(x, y, z, th, 0.96f, 0.80f, 0.88f, 1.12f);
                    d = VoxModel.Smin(d, VoxModel.Cap(x, y, z, 0, 4.4f, -5f, 0, 4.7f, 6.8f, 0.95f), 0.8f);
                    return VoxModel.Smin(d, Cuff(x, y, z, -10f, 3.9f, 3.7f), 1.8f);
                }
                case GloveShape.Charged:
                {
                    // манжета-раструб
                    float d = Mitt(x, y, z, th, 1.02f, 1f, 1f, 1f);
                    return VoxModel.Smin(d, Cuff(x, y, z, -13f, 4.5f, 6.9f), 2.0f);
                }
                case GloveShape.Armored:
                {
                    float d = Mitt(x, y, z, th, 1.04f, 1f, 1f, 1f);
                    d = VoxModel.Smin(d, Cuff(x, y, z, -12.5f, 5.0f, 5.2f), 2.2f);
                    return Mathf.Min(d, Plates(x, y, z));
                }
                case GloveShape.Spiked:
                {
                    float d = Mitt(x, y, z, th, 1f, 1f, 1f, 1f);
                    d = VoxModel.Smin(d, Cuff(x, y, z, -12.5f, 4.8f, 4.8f), 2.2f);
                    return Mathf.Min(d, Spikes(x, y, z));
                }
                case GloveShape.Champion:
                {
                    float d = Mitt(x, y, z, th, 1.06f, 1f, 1f, 1f);
                    d = VoxModel.Smin(d, Cuff(x, y, z, -13f, 5.0f, 5.0f), 2.2f);
                    return Mathf.Min(d, Ring(x, y, z));
                }
                case GloveShape.Wraps:
                {
                    // голый кулак в бинтах: меньше любой перчатки
                    const float k = 1.1f;
                    float X = x / k, Y = y / k, Z = z / k;
                    float d = VoxModel.Ell(X, Y, Z - 0.6f, 4.5f, 3.7f, 4.4f);
                    d = VoxModel.Smin(d, VoxModel.Cap(X, Y, Z, -3.3f, 1.0f, 3.9f, 3.3f, 1.0f, 3.9f, 1.8f), 1.2f);
                    d = VoxModel.Smin(d, VoxModel.Cap(X, Y, Z, -3.1f, -1.7f, 3.4f, 3.1f, -1.7f, 3.4f, 1.6f), 1.0f);
                    d = VoxModel.Smin(d, VoxModel.Cap(X, Y, Z, th * 4.0f, -1.0f, -1.6f, th * 2.0f, -2.2f, 3.0f, 1.35f), 1.0f);
                    d = VoxModel.Smin(d, Mathf.Max(VoxModel.Cap(X, Y, Z, 0, -0.2f, -3f, 0, -0.2f, -10.5f, 3.0f, 3.1f),
                                                   -10.5f - Z), 1.5f);
                    return (d - 0.25f) * k;
                }
                default:
                {
                    float d = Mitt(x, y, z, th, 1f, 1f, 1f, 1f);
                    return VoxModel.Smin(d, Cuff(x, y, z, -12.5f, 4.8f, 4.8f), 2.2f);
                }
            }
        }

        static byte PaintAt(GloveShape sh, int th, float x, float y, float z)
        {
            switch (sh)
            {
                case GloveShape.Heavy:
                    if (z < -8.0f) return z > -10.8f && z < -9.4f ? CTrim : CCuff;       // кольцо на манжете
                    if (y > 3.6f && Mathf.Abs(x) < 2.6f && z > -3.5f && z < 1.5f) return CTrim;   // нашивка
                    return y < -4.2f && z < 4.6f ? CPalm : CMain;
                case GloveShape.Speed:
                    if (z < -6.0f) return CCuff;
                    if (y > 4.1f && Mathf.Abs(x) < 1.5f) return CTrim;                  // гребень
                    if (Mathf.Abs(x) > 2.6f && z > -5f && z < 8f && Mathf.Repeat(z - 0.9f * y, 4.4f) < 1.2f) return CTrim;
                    return y < -3.2f && z < 4f ? CPalm : CMain;
                case GloveShape.Charged:
                    if (z < -7.0f) return z < -11.6f ? CTrim : CCuff;                   // светлый край раструба
                    if (y > 2.6f && z > -5.5f && z < 7.5f)
                    {
                        // молния по тыльной стороне
                        float xc = 1.7f * (Mathf.Abs(Mathf.Repeat(z / 2.6f, 2f) - 1f) * 2f - 1f);
                        if (Mathf.Abs(x - xc) < 1.0f) return CTrim;
                    }
                    return y < -3.6f && z < 4f ? CPalm : CMain;
                case GloveShape.Armored:
                    if (Plates(x, y, z) < 0.45f)
                    {
                        // заклёпки по краям пластин
                        float ax = Mathf.Abs(x);
                        if (ax > 3.4f && ax < 4.5f && PlateCenterDist(z) < 0.5f) return CExtra;
                        return CTrim;
                    }
                    if (z < -7.0f) return CCuff;
                    return y < -3.7f && z < 4.2f ? CPalm : CMain;
                case GloveShape.Spiked:
                    if (Spikes(x, y, z) < 0.5f) return CExtra;
                    if (z < -6.8f) return CCuff;
                    return y < -3.6f && z < 4f ? CPalm : CMain;
                case GloveShape.Champion:
                    if (Ring(x, y, z) < 0.5f) return CTrim;
                    if (z < -7.2f) return CCuff;
                    if (y > 2.5f && Star5(x, z - 0.6f, 3.8f, 0.42f) < 0f) return CTrim;   // звезда на тыльной стороне
                    return y < -3.8f && z < 4.2f ? CPalm : CMain;
                case GloveShape.Wraps:
                {
                    const float k = 1.1f;
                    float Y = y / k, Z = z / k;
                    if (Z > 2.6f && Y < -0.5f) return CSkin;                            // пальцы спереди голые
                    if (Z > -7.6f && Z < -6.5f) return CTrim;                            // лента на запястье
                    float a = Mathf.Atan2(y, x) / Mathf.PI;
                    return Mathf.Repeat(Z * 0.42f + a, 1f) < 0.55f ? CMain : CCuff;
                }
                default:
                    if (z < -6.8f) return CCuff;
                    if (y > 3.0f && Mathf.Abs(x) < 1.3f && z < -1.5f) return CTrim;      // шнуровка
                    return y < -3.6f && z < 4f ? CPalm : CMain;
            }
        }

        // Основа перчатки (как у соперника, BoxerParts.Glove): k — масштаб, kx/ky/kz — ширина, высота, длина.
        static float Mitt(float x, float y, float z, int th, float k, float kx, float ky, float kz)
        {
            float X = x / (k * kx), Y = y / (k * ky), Z = z / (k * kz);
            float d = VoxModel.Ell(X, Y, Z - 1.0f, 6.4f, 5.6f, 7.6f);
            d = VoxModel.Smin(d, VoxModel.Ell(X, Y - 0.6f, Z - 5.4f, 6.2f, 4.9f, 4.0f), 2.5f);
            d = VoxModel.Smin(d, VoxModel.Cap(X, Y, Z, th * 5.2f, -1.5f, -2.8f, th * 4.6f, -2.0f, 4.4f, 2.4f), 1.6f);
            return d * k * Mathf.Min(kx, Mathf.Min(ky, kz));
        }

        // Манжета от кисти (z = −4.5) назад до zEnd с плоским срезом; r0 у кисти, r1 у среза.
        static float Cuff(float x, float y, float z, float zEnd, float r0, float r1) =>
            Mathf.Max(VoxModel.Cap(x, y, z, 0, -0.4f, -4.5f, 0, -0.4f, zEnd, r0, r1), zEnd - z);

        // Броня: три изогнутые пластины поперёк тыльной стороны и щиток на костяшках (оболочка над основой).
        static readonly float[] PlateZ = { -3.4f, -0.1f, 3.2f };

        static float Plates(float x, float y, float z)
        {
            const float k = 1.04f;
            float X = x / k, Y = y / k, Z = z / k;
            float shell = VoxModel.Ell(X, Y, Z - 1.0f, 7.3f, 6.5f, 8.5f) * k;
            float d = float.MaxValue;
            for (int i = 0; i < PlateZ.Length; i++)
                d = Mathf.Min(d, Mathf.Max(shell, Mathf.Max(Mathf.Abs(z - PlateZ[i]) - 1.2f, 1.6f - y)));
            float front = VoxModel.Ell(X, Y - 0.6f, Z - 5.4f, 7.1f, 5.8f, 4.9f) * k;
            return Mathf.Min(d, Mathf.Max(front, Mathf.Max(7.4f - z, -1.2f - y)));
        }

        static float PlateCenterDist(float z)
        {
            float d = float.MaxValue;
            for (int i = 0; i < PlateZ.Length; i++) d = Mathf.Min(d, Mathf.Abs(z - PlateZ[i]));
            return d;
        }

        // Шипы-конусы: ряд на костяшках и ряд на тыльной стороне.
        static float Spikes(float x, float y, float z)
        {
            float d = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                float sx = -4.2f + 2.8f * i;
                d = Mathf.Min(d, VoxModel.Cap(x, y, z, sx, 2.4f, 8.4f, sx, 3.3f, 11.4f, 1.2f, 0.15f));
            }
            for (int i = 0; i < 3; i++)
            {
                float sx = -3.2f + 3.2f * i;
                d = Mathf.Min(d, VoxModel.Cap(x, y, z, sx, 5.0f, -0.6f, sx, 7.8f, -1.0f, 1.15f, 0.15f));
            }
            return d;
        }

        // Чемпионское кольцо-валик вокруг манжеты.
        static float Ring(float x, float y, float z)
        {
            float q = Mathf.Sqrt(x * x + (y + 0.4f) * (y + 0.4f)) - 5.0f, dz = z + 8.6f;
            return Mathf.Sqrt(q * q + dz * dz) - 1.0f;
        }

        // Пятиконечная звезда на плоскости (px, py), вершина по +py; r — радиус, rf — доля внутреннего радиуса.
        static float Star5(float px, float py, float r, float rf)
        {
            const float k1x = 0.809016994f, k1y = -0.587785252f, k2x = -k1x, k2y = k1y;
            px = Mathf.Abs(px);
            float t = 2f * Mathf.Max(k1x * px + k1y * py, 0f);
            px -= t * k1x; py -= t * k1y;
            t = 2f * Mathf.Max(k2x * px + k2y * py, 0f);
            px -= t * k2x; py -= t * k2y;
            px = Mathf.Abs(px);
            py -= r;
            float bax = rf * -k1y, bay = rf * k1x - 1f;
            float h = Mathf.Clamp((px * bax + py * bay) / (bax * bax + bay * bay), 0f, r);
            float ex = px - bax * h, ey = py - bay * h;
            return Mathf.Sqrt(ex * ex + ey * ey) * Mathf.Sign(py * bax - px * bay);
        }
    }
}
