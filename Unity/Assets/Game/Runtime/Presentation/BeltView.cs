using UnityEngine;

namespace VoxGame
{
    // Воксельный чемпионский пояс перед камерой игрока: цветной ремень с золотым кантом, боковые пластины,
    // большая золотая пряжка с камнем. Появляется сверху в кадр, покачивается, пряжка вспыхивает, по ней бегут блики.
    // НЕ MonoBehaviour: Build → SetView → Show/Hide → Tick(dt, udt) → Dispose. Анимация — по реальному времени.
    // Пояс живёт в осях камеры (камера смотрит в свой +Z), лицевая сторона пояса — к камере (−Z его осей).
    public class BeltView
    {
        // ---- Настройки (подгонять по снимкам) ------------------------------------------------------
        public const float EnterDur = 0.75f;     // выход в кадр, с
        public const float ExitDur  = 0.40f;     // уход вниз, с
        const float DropFrom   = 0.42f;          // откуда опускается: выше точки покоя, м
        const float RestY      = -0.03f;         // точка покоя в осях камеры (по высоте), м
        const float BuckleFill = 0.75f;          // доля ширины кадра под пряжкой (портрет)
        const float MinDist = 0.55f, MaxDist = 0.90f;
        const float Scale      = 1f;             // общий масштаб пояса
        const float FlashEvery = 1.1f, FlashLen = 0.35f;   // блики на пряжке
        const float BaseGlow   = 0.10f, FlashGlow = 0.45f;

        // ---- Форма (см) ---------------------------------------------------------------------------
        const float Curve = 0.0065f;             // изгиб ремня назад от камеры: z = Curve·x²
        const float StrapHalfLen = 52f, StrapHalfH = 5f, StrapHalfT = 0.7f;
        static readonly float[] PlateX = { -31f, -19f, 19f, 31f };

        static readonly Color32 CGold     = new Color32(236, 190, 64, 255);
        static readonly Color32 CGoldMid  = new Color32(206, 158, 44, 255);
        static readonly Color32 CGoldDark = new Color32(162, 118, 28, 255);
        static readonly Color32 CGem      = new Color32(214, 28, 58, 255);
        static readonly Color32 CGemHi    = new Color32(255, 170, 190, 255);
        static readonly Color32 CSilver   = new Color32(220, 222, 230, 255);
        static readonly Color32 CPanel    = new Color32(24, 40, 104, 255);
        static readonly Color32 CStitch   = new Color32(250, 214, 110, 255);
        static readonly Color32 CSpark    = new Color32(255, 248, 214, 255);

        // Точки бликов на пряжке (м, оси пояса)
        static readonly Vector3[] SparkAt =
        {
            new Vector3(-0.10f, 0.07f, -0.035f), new Vector3(0.11f, -0.06f, -0.035f),
            new Vector3(0.0f, 0.115f, -0.025f), new Vector3(-0.015f, 0.012f, -0.055f),
        };

        enum Mode { Hidden, Enter, Shown, Exit }

        Transform _root, _spark;
        Material _buckleMat, _sparkMat;
        Mesh[] _meshes;
        Mode _mode = Mode.Hidden;
        float _t, _life;
        Vector3 _rest = new Vector3(0f, RestY, 0.65f);

        public Transform Root => _root;
        public bool Visible => _mode != Mode.Hidden;
        // Опустился и стоит в кадре
        public bool Settled => _mode == Mode.Shown;

        // ---- Построение ---------------------------------------------------------------------------

        // cameraParent — камера игрока; strap — цвет ремня.
        public void Build(Transform cameraParent, Material voxMat, Color strap)
        {
            _root = new GameObject("ChampionBelt").transform;
            _root.SetParent(cameraParent, false);
            _root.localScale = Vector3.one * Scale;

            _buckleMat = new Material(voxMat);
            _buckleMat.SetFloat("_Glow", BaseGlow);
            _sparkMat = new Material(voxMat);
            _sparkMat.SetFloat("_Glow", 1f);

            var strapMesh = BuildStrap(strap);
            var buckleMesh = BuildBuckle();
            var sparkMesh = BuildSpark();
            _meshes = new[] { strapMesh, buckleMesh, sparkMesh };

            VoxModel.Spawn("strap", _root, strapMesh, voxMat);
            VoxModel.Spawn("buckle", _root, buckleMesh, _buckleMat);
            _spark = VoxModel.Spawn("spark", _root, sparkMesh, _sparkMat);
            _spark.localScale = Vector3.zero;

            _root.gameObject.SetActive(false);
        }

        // Поле зрения камеры (вертикальное, градусы) и пропорции: пряжка занимает около BuckleFill ширины кадра.
        public void SetView(float vFovDeg, float aspect)
        {
            float vHalf = vFovDeg * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * Mathf.Max(0.1f, aspect));
            float z = Mathf.Clamp(0.15f * Scale / BuckleFill / Mathf.Tan(hHalf), MinDist, MaxDist);
            _rest = new Vector3(0f, RestY, z);
        }

        public void Show()
        {
            if (_root == null || _mode == Mode.Enter || _mode == Mode.Shown) return;
            _root.gameObject.SetActive(true);
            _mode = Mode.Enter;
            _t = 0f;
            Pose();
        }

        public void Hide()
        {
            if (_root == null || _mode == Mode.Hidden || _mode == Mode.Exit) return;
            _mode = Mode.Exit;
            _t = 0f;
        }

        // ---- Tick --------------------------------------------------------------------------------

        public void Tick(float dt, float udt)
        {
            if (_root == null || _mode == Mode.Hidden) return;
            udt = Mathf.Max(0f, udt);
            _t += udt;
            _life += udt;
            if (_mode == Mode.Enter && _t >= EnterDur) { _mode = Mode.Shown; _t = EnterDur; }
            if (_mode == Mode.Exit && _t >= ExitDur)
            {
                _mode = Mode.Hidden;
                _root.gameObject.SetActive(false);
                return;
            }
            Pose();
        }

        void Pose()
        {
            Vector3 pos = _rest;
            float pitch = 0f, roll = 0f, yaw = 0f, settle = 1f;
            if (_mode == Mode.Enter)
            {
                float k = Mathf.Clamp01(_t / EnterDur);
                float e = OutBack(k);
                pos += Vector3.up * DropFrom * (1f - e);
                pitch = 55f * (1f - e);
                roll = -14f * (1f - e);
                settle = k;
            }
            else if (_mode == Mode.Exit)
            {
                float k = Mathf.Clamp01(_t / ExitDur);
                pos += Vector3.down * 0.55f * k * k;
                pitch = -30f * k * k;
            }

            // покачивание в кадре
            yaw += Mathf.Sin(_life * 1.7f) * 5f * settle;
            roll += Mathf.Sin(_life * 1.1f) * 2.5f * settle;
            pos.y += Mathf.Sin(_life * 2.3f) * 0.006f * settle;
            _root.localPosition = pos;
            _root.localRotation = Quaternion.Euler(pitch, yaw, roll);

            // вспышки пряжки: короткий импульс раз в FlashEvery, сильная вспышка при посадке
            float ft = Mathf.Repeat(_life, FlashEvery) / FlashLen;
            float pulse = ft < 1f ? Mathf.Sin(Mathf.PI * ft) : 0f;
            float land = _mode == Mode.Shown ? Mathf.Exp(-(_t - EnterDur) * 5f) : 0f;
            _buckleMat.SetFloat("_Glow", Mathf.Clamp01(BaseGlow + FlashGlow * Mathf.Max(pulse, land) + 0.04f * Mathf.Sin(_life * 4f)));

            int idx = Mathf.FloorToInt(_life / FlashEvery) % SparkAt.Length;
            _spark.localPosition = SparkAt[idx];
            _spark.localRotation = Quaternion.Euler(0f, 0f, _life * 90f);
            _spark.localScale = Vector3.one * (pulse * (_mode == Mode.Exit ? 0f : 1f));
        }

        static float OutBack(float k)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float m = k - 1f;
            return 1f + c3 * m * m * m + c1 * m * m;
        }

        // ---- Формы -------------------------------------------------------------------------------

        static float RBox(float x, float y, float z, float bx, float by, float bz, float r)
        {
            float qx = Mathf.Abs(x) - bx + r, qy = Mathf.Abs(y) - by + r, qz = Mathf.Abs(z) - bz + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f), oz = Mathf.Max(qz, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy + oz * oz) + Mathf.Min(Mathf.Max(qx, Mathf.Max(qy, qz)), 0f) - r;
        }

        static float StrapZ(float x) => Curve * x * x;

        // Пластина на ремне в точке xc: u — вдоль ремня, w — по нормали (лицо к −w).
        static void PlateAxes(float x, float z, float xc, out float u, out float w)
        {
            float th = Mathf.Atan(2f * Curve * xc), c = Mathf.Cos(th), s = Mathf.Sin(th);
            float cx = xc + s * 1.0f, cz = StrapZ(xc) - c * 1.0f;
            float px = x - cx, pz = z - cz;
            u = px * c + pz * s;
            w = -px * s + pz * c;
        }

        static float StrapShape(float x, float y, float z)
        {
            float d = Mathf.Max(Mathf.Max(Mathf.Abs(y) - StrapHalfH, Mathf.Abs(z - StrapZ(x)) - StrapHalfT),
                                Mathf.Abs(x) - StrapHalfLen);
            for (int i = 0; i < PlateX.Length; i++)
            {
                PlateAxes(x, z, PlateX[i], out float u, out float w);
                d = Mathf.Min(d, RBox(u, y, w, 3.6f, 4.6f, 0.7f, 0.6f));
                d = Mathf.Min(d, VoxModel.Ell(u, y, w + 0.9f, 1.2f, 1.2f, 0.7f));
            }
            return d;
        }

        static byte StrapPaint(float x, float y, float z)
        {
            for (int i = 0; i < PlateX.Length; i++)
            {
                PlateAxes(x, z, PlateX[i], out float u, out float w);
                if (VoxModel.Ell(u, y, w + 0.9f, 1.2f, 1.2f, 0.7f) < 0.4f) return 5;
                if (RBox(u, y, w, 3.6f, 4.6f, 0.7f, 0.6f) < 0.45f)
                    return Mathf.Abs(u) < 2.3f && Mathf.Abs(y) < 3.2f && w < -0.3f ? (byte)4 : (byte)3;
            }
            float ay = Mathf.Abs(y);
            if (ay > 3.5f && ay < 4.2f) return 6;
            return ay > 4.3f ? (byte)2 : (byte)1;
        }

        Mesh BuildStrap(Color strap)
        {
            var mn = new Vector3(-53.5f, -5.6f, -3f); var mx = new Vector3(53.5f, 5.6f, StrapZ(53.5f) + 1.5f);
            const float cell = 0.6f;
            var m = VoxModel.FromSdf(mn, mx, cell, StrapShape, StrapPaint);
            Color32 main = strap, dark = Color.Lerp(strap, Color.black, 0.35f);
            return m.BuildMesh("beltStrap", new[] { default, main, dark, CGold, CPanel, CGem, CStitch },
                               mn * 0.01f, cell * 0.01f, 0.4f, new[] { 1f, 0.4f, 0.4f, 0.6f, 0.6f, 0.6f, 0.4f });
        }

        static float Rim(float x, float y, float z) =>
            Mathf.Max(RBox(x, y, z + 2.1f, 13.6f, 9.6f, 0.6f, 3.2f), -RBox(x, y, z + 2.1f, 11.4f, 7.6f, 1.2f, 2.2f));

        static float Gem(float x, float y, float z) => VoxModel.Ell(x, y, z + 2.6f, 3.8f, 3.8f, 2.0f);

        static float Studs(float x, float y, float z) =>
            VoxModel.Ell(Mathf.Abs(x) - 8.5f, Mathf.Abs(y) - 5.2f, z + 2.3f, 0.9f, 0.9f, 0.7f);

        static float BuckleShape(float x, float y, float z)
        {
            float ax = Mathf.Abs(x);
            float d = RBox(x, y, z + 0.4f, 15f, 11f, 1.5f, 4f);
            d = Mathf.Min(d, VoxModel.Ell(x, y - 11.5f, z + 0.5f, 6f, 3.4f, 1.3f));    // гребень сверху
            d = Mathf.Min(d, VoxModel.Ell(x, y + 11.2f, z + 0.5f, 5f, 2.6f, 1.3f));    // снизу
            d = Mathf.Min(d, VoxModel.Ell(ax - 15.5f, y, z + 0.4f, 3.6f, 7f, 1.3f));   // крылья
            d = Mathf.Min(d, Rim(x, y, z));
            d = Mathf.Min(d, VoxModel.Ell(x, y, z + 2.0f, 5f, 5f, 0.9f));              // оправа камня
            d = Mathf.Min(d, Gem(x, y, z));
            return Mathf.Min(d, Studs(x, y, z));
        }

        static byte BucklePaint(float x, float y, float z)
        {
            if (Gem(x, y, z) < 0.4f) return x < -0.6f && y > 0.8f && z < -3.4f ? (byte)5 : (byte)4;
            if (Studs(x, y, z) < 0.4f) return 6;
            if (Rim(x, y, z) < 0.4f) return 1;
            if (VoxModel.Ell(x, y, z + 2.0f, 5f, 5f, 0.9f) < 0.4f) return 1;
            // поле внутри обода: лучи от камня
            if (Mathf.Abs(x) < 11.6f && Mathf.Abs(y) < 7.8f && z < -1.6f)
                return Mathf.Repeat(Mathf.Atan2(y, x) * 12f / (2f * Mathf.PI), 1f) < 0.5f ? (byte)3 : (byte)2;
            return 1;
        }

        Mesh BuildBuckle()
        {
            var mn = new Vector3(-20f, -15.5f, -5.2f); var mx = new Vector3(20f, 15.5f, 2.5f);
            const float cell = 0.5f;
            var m = VoxModel.FromSdf(mn, mx, cell, BuckleShape, BucklePaint);
            return m.BuildMesh("beltBuckle", new[] { default, CGold, CGoldDark, CGoldMid, CGem, CGemHi, CSilver },
                               mn * 0.01f, cell * 0.01f, 0.45f, new[] { 1f, 0.6f, 0.6f, 0.6f, 0.4f, 0.4f, 0.5f });
        }

        // Блик: четырёхлучевая звёздочка
        static float SparkShape(float x, float y, float z)
        {
            float d = VoxModel.Ell(x, y, z, 0.35f, 3.2f, 0.35f);
            d = Mathf.Min(d, VoxModel.Ell(x, y, z, 3.2f, 0.35f, 0.35f));
            return Mathf.Min(d, VoxModel.Ell(x, y, z, 1.0f, 1.0f, 0.5f));
        }

        Mesh BuildSpark()
        {
            var mn = new Vector3(-3.5f, -3.5f, -0.75f); var mx = new Vector3(3.5f, 3.5f, 0.75f);
            const float cell = 0.25f;
            var m = VoxModel.FromSdf(mn, mx, cell, SparkShape, (x, y, z) => 1);
            return m.BuildMesh("beltSpark", new[] { default, CSpark }, mn * 0.01f, cell * 0.01f, 0f);
        }

        // ---- Dispose ------------------------------------------------------------------------------

        public void Dispose()
        {
            if (_meshes != null) foreach (var m in _meshes) GameUtil.Destroy(m);
            _meshes = null;
            if (_buckleMat != null) GameUtil.Destroy(_buckleMat);
            if (_sparkMat != null) GameUtil.Destroy(_sparkMat);
            _buckleMat = _sparkMat = null;
            if (_root != null) GameUtil.Destroy(_root.gameObject);
            _root = null;
            _mode = Mode.Hidden;
        }
    }
}
