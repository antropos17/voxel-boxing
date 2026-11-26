using UnityEngine;

namespace VoxGame
{
    // Руки игрока от первого лица: две перчатки с предплечьями в осях камеры (камера смотрит в свой +Z).
    // Рука 0 — левая (на экране слева), рука 1 — правая.
    // Рука из двух звеньев: плечевой сустав под нижним краем кадра → локоть → запястье (двухзвенная обратная
    // кинематика, локоть смотрит вниз и чуть наружу). В покое, в ударе, в блоке и в поднятой руке рука входит в кадр
    // снизу (или из нижнего угла), видно короткое предплечье с перчаткой и, при вытянутой руке, часть плеча.
    // НЕ MonoBehaviour — создаётся и управляется из FightDirector.
    public class PlayerGloves
    {
        const float Scale   = 0.86f;   // руки игрока чуть мельче рук соперника: иначе в портрете две перчатки не помещаются
        const float ReturnT = 0.16f;   // возврат руки после удара, с

        // Поза покоя: доли полуэкрана от центра (nx — к своему краю, ny — вверх, минус — вниз) и глубина, м.
        // Портрет: кнопки HUD по всей ширине низа (~13% высоты), перчатки с манжетами над ними.
        const float RestNXP = 0.60f, RestNYP = -0.40f, RestZP = 0.60f;
        // Ландшафт: кнопки только по нижним углам, перчатки ближе к середине.
        const float RestNXL = 0.40f, RestNYL = -0.52f, RestZL = 0.52f;
        // Блок: перчатки выше и ближе
        const float BlockNXP = 0.62f, BlockNXL = 0.21f, BlockNY = -0.36f, BlockZ = 0.40f;

        // Плечевой сустав: точка под нижним краем кадра на глубине AnchorZ (в ударе уходит до AnchorPunchZ),
        // по горизонтали — доля полуширины кадра на этой глубине (1 — нижний угол своей стороны).
        const float AnchorZ = 0.22f, AnchorPunchZ = 0.50f;
        const float AnchorGap = 0.07f;   // м за нижней плоскостью кадра: больше толщины предплечья, среза не видно
        const float RestAUP = 0.85f, RestAUL = 0.55f, BlockAU = 0.45f;
        const float JabAU = 0.85f, HookAU = 1.0f, UpperAU = 0.50f, MegaAU = 0.80f;
        const float ForeFollow = 0.55f;  // насколько ось перчатки следует за предплечьем в ударе (остальное — костяшки)
        const float RestFollow = 0.25f;  // то же в стойке: предплечье там почти вертикально
        const float EdgeX = 0.90f;       // в размахе хука центр перчатки (и локоть в кадре) не дальше этой доли полуширины
        // Локоть: куда сгибается (оси камеры: наружу на PoleOut, вниз), и самый прямой угол в локте, градусы.
        // Если сустав под кадром дальше, чем достаёт рука, плечо удлиняется ровно настолько, чтобы изгиб остался.
        const float PoleOut = 0.45f, ElbowMinDeg = 145f;
        const float UpperOver = 0.04f;   // запас длины плеча за суставом, м: срез уходит под кадр
        // Вытянутая рука (удар, поднятая рука): сустав от запястья по лучу ReachDir (x — наружу) на ReachLen, м
        // (при длинах звеньев это угол в локте около 105°), локоть гнётся к ReachPole (наружу и вниз). В ландшафте по бокам
        // много места: сустав и локоть уходят дальше наружу (…L), иначе рука идёт снизу почти прямой.
        static readonly Vector3 ReachDirP = new Vector3(0.40f, -0.88f, -0.25f), ReachPoleP = new Vector3(0.35f, -1f, -0.1f);
        static readonly Vector3 ReachDirL = new Vector3(0.62f, -0.80f, -0.20f), ReachPoleL = new Vector3(0.55f, -1f, -0.1f);
        Vector3 _reachDir = ReachDirP, _reachPole = ReachPoleP;
        const float SideLowNY = -0.55f;   // сустав за боковым краем — не выше этой доли полувысоты кадра (минус — вниз)
        const float SideLowNYP = -0.85f;  // в портрете рука входит снизу или из самого нижнего угла, не с середины края
        float _sideLowNY = SideLowNYP;
        // Досягаемость: голова соперника в 1.3 м, а рука игрока — 0.6 м. Цель удара и точка хвата рефери переносятся
        // по лучу из камеры ближе (на экране — та же точка, перчатка крупнее и закрывает место удара), иначе плечо
        // тянулось жердью через полкадра. Плечо удлиняется не больше UpperMaxStretch, при растяжении — и толще.
        const float PunchMaxZP = 0.74f, PunchMaxZL = 0.80f, VictoryMaxZ = 0.45f;
        const float UpperMaxStretch = 1.35f;
        float _punchMaxZ = PunchMaxZP;
        const float ReachLen = 0.48f, ReachElbowDeg = 110f;   // в вытянутой руке локоть не прямее этого угла

        // ---- Поле зрения рук (как viewmodel FOV в шутерах) ----------------------------------------------------------------
        // В портрете горизонтальное поле камеры ~33° (CalcFov держит tan = 0,296), в ландшафте ~90°: одна и та же рука в метрах
        // в портрете в 3,4 раза шире на экране. Руки ставятся в виртуальном кадре с горизонтальным tan не меньше ArmTanHMin
        // (вертикальный — в той же пропорции) и рисуются через родителя _rig с масштабом (k, k, 1), k = tanH / tanH':
        // точка p виртуального кадра видна там же, где её показала бы камера с полем шире, а глубина прежняя.
        // Позы в долях экрана и точки попадания (луч из камеры) не меняются; в ландшафте k = 1.
        public static bool  ViewFix = true;        // false — как до правки (сверка «без | с»)
        public static float ArmTanHMin = 0.50f;    // tan половины горизонтального поля рук (≈ 53° на весь кадр; в портрете k ≈ 0,59)
        float _k = 1f;                             // масштаб рук на экране (1 — как камера)
        // ---- Поза руки в портрете (PoseFix) ----
        // С полем рук перчатка в ударе стоит в центре кадра, а рука от неё шла прямой палкой до нижнего края (полэкрана).
        // В портрете ближе всего боковой край: в ударе локоть уводится за него — предплечье от запястья наружу (SideFore…),
        // плечо от локтя вниз-назад за край (SideUpper…), оси кадра рук, x — наружу. Длины звеньев прежние, перчатка на месте.
        public static bool PoseFix = true;         // false — вид fd78630 (только поле рук)
        public static Vector3 SideForeJab = new Vector3(1f, -0.60f, -0.40f), SideForeHook = new Vector3(1f, 0.05f, -0.35f),
                              SideForeUpper = new Vector3(1f, -0.55f, -0.25f), SideUpper = new Vector3(0.4f, -1f, -0.4f);
        // Перчатка в портрете: доля прежнего размера на экране (поле рук делает её мельче в k раз)
        public static float GloveScreenK = 0.68f;
        public static float SideFollow = 0.2f;     // ось перчатки в такой позе меньше следует за предплечьем: костяшки к цели
        float _gs = 1f;                            // масштаб перчатки поверх Scale
        bool  _pose;                               // PoseFix и поле рук действует (портрет)
        readonly Transform _rig;

        readonly Transform _cam;
        readonly Transform[] _upper = new Transform[2], _fore = new Transform[2], _glove = new Transform[2];
        readonly MeshFilter[] _foreMf = new MeshFilter[2], _gloveMf = new MeshFilter[2];
        Mesh _foreMesh, _upperMesh;
        readonly Mesh[] _gloveMesh = new Mesh[2];
        GloveLook _look;

        // Кадр
        float _tanV = 0.64f, _tanH = 0.30f, _nLen = 1.19f;
        float _restAU = RestAUP;

        // Положение в стойке (оси камеры) — зависит от поля зрения и пропорций экрана
        readonly Vector3[] _rest = new Vector3[2], _block = new Vector3[2];
        readonly Vector3[] _base = new Vector3[2];   // сглаженное положение без удара
        bool  _baseSet;
        float _blockW;

        // Удар
        readonly float[] _ext = new float[2], _timer = new float[2], _outT = new float[2];
        readonly Vector3[] _goal = new Vector3[2], _tgt = new Vector3[2];   // _tgt — цель в осях камеры, _goal — в кадре рук
        readonly PunchType[] _kind = new PunchType[2];

        bool  _blocking;
        int   _dodgeSide;
        float _dodgeTimer;

        // Поднятая рука победителя: запястье в руке рефери (оси камеры)
        const float VictoryIn = 0.18f;  // переход в позу и обратно, с
        // Поднятая рука входит в кадр из нижнего угла своей стороны и с глубины VictoryAnchorZ: предплечье идёт
        // наискось вверх, а не столбом из середины низа. Вторая рука в это время опущена за нижний край.
        const float VictoryAU = 1.0f, VictoryAnchorZ = 0.55f, VictoryDropNY = -1.7f;
        // Поднятая рука: сустав сбоку вне кадра, локоть уходит за боковой край — в кадре предплечье и перчатка,
        // а не плечо жердью от нижнего угла через весь кадр (сустав условный, его не видно).
        static readonly Vector3 VictoryShoulder = new Vector3(0.30f, -0.30f, 0.15f);
        const float VictoryElbowDeg = 150f;
        // ---- Пружинность и вытянутая рука по образцу веб-версии (FoeSpring.Enabled) ----------------------------------------
        // В ударе и в победе плечо — настоящее: ниже и сбоку от глаза (оси камеры, x — наружу), с ударом уходит вперёд и внутрь
        // (веб: ±0,2 / −0,3, вперёд на 0,22). В портрете плечи уже: рука входит снизу, а не с бокового края.
        // Звенья не тянутся, запястье не дальше ReachK длины руки — локоть всегда заметно согнут.
        public static Vector3 ShoulderP = new Vector3(0.10f, -0.40f, -0.02f), ShoulderL = new Vector3(0.13f, -0.40f, -0.02f);
        public static Vector3 ShoulderPunch = new Vector3(-0.03f, 0.02f, 0.20f), ShoulderVictory = new Vector3(-0.02f, 0.10f, 0.12f);
        public static float ReachK = 0.9f;
        // Полюс локтя по виду удара (веб POLE): прямой — вниз и наружу за кулаком, хук — вверх и наружу, апперкот — вниз
        public static Vector3 PoleJab = new Vector3(0.6f, -0.9f, 0f), PoleHook = new Vector3(1f, 0.6f, -0.1f),
                                PoleUpper = new Vector3(0.3f, -1f, -0.4f), PoleVictory = new Vector3(0.7f, -1f, -0.3f);
        // Перчатка при попадании сплющивается и качается (веб: k 620, c 24), шаг фиксированный
        const float SqK = 620f, SqC = 24f, SqMin = -0.18f, SqMax = 0.34f, SqH = 1f / 240f;
        static readonly float[] SqKick = { 5.5f, 9f, 13f };   // джеб, силовой, мега
        readonly float[] _sq = new float[2], _sqv = new float[2];
        // Отдача перчатки после попадания (PHYSICS.md): пружина вдоль пути удара назад к плечу, тот же шаг SqH.
        // Пик ~3 кадра, назад за ~8 кадров; время между ударами не меняется (перезарядка в FightDirector).
        public static bool Recoil = true;
        public const float RcK = 1200f, RcC = 55f, RcMax = 0.07f;
        static readonly float[] RcKick = { 4.0f, 4.6f, 5.4f };   // м/с: джеб, силовой, мега
        readonly float[] _rc = new float[2], _rcv = new float[2];
        readonly Vector3[] _rcDir = new Vector3[2];

        // Куда отдача может унести руку s (мир, м): во время удара — наибольший сдвиг по линии к плечу, иначе ноль.
        // По нему плашка удара обходит руку и после отдачи (FxDirector.ArmMask).
        public Vector3 RecoilReach(int s) => Recoil && _ext[s] > 0f ? _rig.TransformVector(_rcDir[s] * RcMax) : Vector3.zero;
        readonly bool[] _landed = new bool[2];
        float _sqAcc;
        Vector3 _shP = ShoulderP;

        int     _vicSide = -1;
        bool    _vicOn;
        float   _vicW;
        Vector3 _vicWrist;
        bool    _visible = true;

        static float Sign(int s) => s == 0 ? -1f : 1f;

        public PlayerGloves(Transform cameraParent, GloveLook look, Material mat)
        {
            _cam = cameraParent;
            _rig = new GameObject("playerArms").transform;
            _rig.SetParent(_cam, false);
            BuildMeshes(look);
            _upperMesh = GloveLooks.BuildUpperArmMesh();
            for (int s = 0; s < 2; s++)
            {
                _upper[s] = VoxModel.Spawn("playerUpperArm" + s, _rig, _upperMesh, mat);
                _upper[s].localScale = Vector3.one * Scale;
                _fore[s]  = VoxModel.Spawn("playerForeArm" + s, _rig, _foreMesh, mat);
                _glove[s] = VoxModel.Spawn("playerGlove" + s, _rig, _gloveMesh[s], mat);
                _foreMf[s]  = _fore[s].GetComponent<MeshFilter>();
                _gloveMf[s] = _glove[s].GetComponent<MeshFilter>();
                _fore[s].localScale = _glove[s].localScale = Vector3.one * Scale;
            }
            SetView(60f, 0.46f);
            Tick(0f);
        }

        public GloveLook Look => _look;

        // Сменить вид перчаток (магазин, переигровка): сетки перестраиваются, поза не меняется.
        public void SetLook(GloveLook look)
        {
            DestroyMeshes();
            BuildMeshes(look);
            for (int s = 0; s < 2; s++)
            {
                _foreMf[s].sharedMesh  = _foreMesh;
                _gloveMf[s].sharedMesh = _gloveMesh[s];
            }
        }

        public void SetGloves(string gloveId) => SetLook(GloveLook.FromEntry(GlovesCatalog.Get(gloveId)));

        void BuildMeshes(GloveLook look)
        {
            _look = look;
            _foreMesh = GloveLooks.BuildForeArmMesh(look);
            _gloveMesh[0] = GloveLooks.BuildGloveMesh(look, +1);   // левая: большой палец сверху при тыльной стороне наружу
            _gloveMesh[1] = GloveLooks.BuildGloveMesh(look, -1);
        }

        void DestroyMeshes()
        {
            GameUtil.Destroy(_foreMesh);
            GameUtil.Destroy(_gloveMesh[0]);
            GameUtil.Destroy(_gloveMesh[1]);
            _foreMesh = null; _gloveMesh[0] = _gloveMesh[1] = null;
        }

        // Поле зрения камеры (вертикальное, градусы) и отношение ширины к высоте: перчатки стоят по бокам у нижнего края.
        public void SetView(float vFovDeg, float aspect)
        {
            _tanV = Mathf.Tan(vFovDeg * 0.5f * Mathf.Deg2Rad);
            _tanH = _tanV * aspect;
            // поле рук: дальше всё в виртуальном кадре (_tanH, _tanV шире настоящих в 1/k раз)
            _k = ViewFix ? Mathf.Min(1f, _tanH / ArmTanHMin) : 1f;
            _tanV /= _k; _tanH /= _k;
            _pose = PoseFix && _k < 0.999f;
            _gs = _pose ? Mathf.Max(1f, GloveScreenK / _k) : 1f;
            if (!FoeSpring.Enabled)
                for (int s = 0; s < 2; s++) if (_glove[s] != null) _glove[s].localScale = Vector3.one * (Scale * _gs);
            var rs = new Vector3(_k, _k, 1f);
            if (_rig.localScale != rs) _rig.localScale = rs;
            _nLen = Mathf.Sqrt(1f + _tanV * _tanV);
            bool port = aspect < 1f;
            _restAU = port ? RestAUP : RestAUL;
            _reachDir = port ? ReachDirP : ReachDirL;
            _reachPole = port ? ReachPoleP : ReachPoleL;
            _sideLowNY = port ? SideLowNYP : SideLowNY;
            _punchMaxZ = port ? PunchMaxZP : PunchMaxZL;
            _shP = port ? ShoulderP : ShoulderL;
            float rz = port ? RestZP : RestZL, rnx = port ? RestNXP : RestNXL, rny = port ? RestNYP : RestNYL;
            float bnx = port ? BlockNXP : BlockNXL;
            for (int s = 0; s < 2; s++)
            {
                float sg = Sign(s);
                Vector3 rest  = new Vector3(sg * rnx * rz * _tanH, rny * rz * _tanV, rz);
                Vector3 block = new Vector3(sg * bnx * BlockZ * _tanH, BlockNY * BlockZ * _tanV, BlockZ);
                // при повороте экрана перчатки переезжают сразу, а не плывут через полэкрана
                _base[s] = _baseSet ? _base[s] + (_blocking ? block - _block[s] : rest - _rest[s]) : rest;
                _rest[s]  = rest;
                _block[s] = block;
            }
            _baseSet = true;
        }

        // ---- Команды -----------------------------------------------------------------------------

        // Удар. side: −1 — левая, +1 — правая; duration — время до начала возврата, с;
        // worldTarget — точка на голове соперника, куда приходят костяшки.
        public void Punch(int side, PunchType kind, float duration, Vector3 worldTarget, bool landed = true)
        {
            int s = side < 0 ? 0 : 1;
            float sg = Sign(s);
            Vector3 t = _cam.InverseTransformPoint(worldTarget);
            switch (kind)
            {
                case PunchType.Hook:  t += new Vector3(sg * 0.075f, 0.005f, -0.035f); break;   // сбоку от скулы
                case PunchType.Upper: t += new Vector3(0f, -0.075f, -0.04f); break;            // под подбородком
                default:              t += new Vector3(0f, 0f, -0.08f); break;                 // перед лицом
            }
            _tgt[s] = t;
            _goal[s] = Goal(s);
            _landed[s] = landed;
            _kind[s]  = kind;
            _timer[s] = Mathf.Max(duration, 0.08f);
            _outT[s]  = Mathf.Max(_timer[s] * 0.5f, 0.045f);
        }

        public void SetBlock(bool blocking) => _blocking = blocking;

        // Рефери поднимает руку игрока: side (−1 левая, +1 правая), worldWrist — точка хвата (мир), куда встаёт запястье;
        // перчатка над ним костяшками вверх. Звать каждый кадр, пока рефери держит руку.
        public void RaiseVictory(int side, Vector3 worldWrist)
        {
            _vicSide  = side < 0 ? 0 : 1;
            Vector3 cw = _cam.InverseTransformPoint(worldWrist);
            _vicWrist = ToArm(FoeSpring.Enabled ? ReachSphere(cw, VicShoulderCam(_vicSide), ArmReach()) : Reach(cw, VictoryMaxZ));
            _vicOn    = true;
        }

        // Куда приходит перчатка (кадр рук): цель удара, перенесённая по лучу из камеры до досягаемой точки.
        // Считается по текущему кадру: при повороте экрана посреди удара перчатка остаётся на цели.
        Vector3 Goal(int s)
        {
            // досягаемость — в осях камеры по настоящему плечу: глубина и место перчатки те же, что без поля рук
            return ToArm(FoeSpring.Enabled ? ReachSphere(_tgt[s], ShoulderCam(s, 1f), ArmReach() + BoxerParts.GloveOff * Scale * 0.7f)
                                           : Reach(_tgt[s], _punchMaxZ));
        }

        // Точка в осях камеры → виртуальный кадр рук (на экране то же место, глубина та же)
        Vector3 ToArm(Vector3 p) => new Vector3(p.x / _k, p.y / _k, p.z);

        public float Extension(int s) => _ext[s];   // вылет руки 0..1 (снимки)
        public float ArmScreenK => _k;

        float ArmReach() => (GloveLooks.UpperLenCm + GloveLooks.ForeLenCm) * 0.01f * Scale * ReachK;

        // Настоящее плечо (оси камеры) при вылете e (0 — стойка, 1 — удар)
        Vector3 ShoulderCam(int s, float e) =>
            new Vector3(Sign(s) * (_shP.x + ShoulderPunch.x * e), _shP.y + ShoulderPunch.y * e, _shP.z + ShoulderPunch.z * e);

        Vector3 VicShoulderCam(int s) =>
            new Vector3(Sign(s) * (_shP.x + ShoulderVictory.x), _shP.y + ShoulderVictory.y, _shP.z + ShoulderVictory.z);

        // То же в кадре рук: на экране сустав там же, где настоящий (за нижним краем). Если рука до него не достаёт,
        // Place подтягивает его к запястью по той же линии — рука уходит за край в ту же сторону.
        Vector3 RealShoulder(int s, float e) => ToArm(ShoulderCam(s, e));
        Vector3 VicRealShoulder(int s) => ToArm(VicShoulderCam(s));

        // Точка на луче из камеры через t (на экране то же место), не дальше r от плеча sh: самая дальняя такая точка луча.
        // Луч мимо сферы — ближайшая к плечу точка луча, подтянутая к плечу.
        static Vector3 ReachSphere(Vector3 t, Vector3 sh, float r)
        {
            float tl = t.magnitude;
            if (tl < 1e-5f) return t;
            Vector3 u = t / tl;
            float b = Vector3.Dot(u, sh), disc = b * b - sh.sqrMagnitude + r * r;
            if (disc < 0f) { Vector3 c = u * Mathf.Max(0f, b); return sh + (c - sh).normalized * r; }
            float far = b + Mathf.Sqrt(disc);
            return tl <= far ? t : u * far;
        }

        // Точка (оси камеры) по лучу из камеры не дальше maxZ: на экране то же место, рука до неё достаёт.
        static Vector3 Reach(Vector3 t, float maxZ) => t.z > maxZ ? t * (maxZ / t.z) : t;

        // Отпустить руку сразу (переигровка).
        public void ClearVictory()
        {
            _vicOn = false; _vicW = 0f; _vicSide = -1;
        }

        // Показать или спрятать обе руки (во время пояса перед камерой).
        public void SetVisible(bool on)
        {
            if (on == _visible) return;
            _visible = on;
            for (int s = 0; s < 2; s++)
            {
                _upper[s].gameObject.SetActive(on);
                _fore[s].gameObject.SetActive(on);
                _glove[s].gameObject.SetActive(on);
            }
        }

        // Уклон: side < 0 — влево, > 0 — вправо.
        public void SetDodge(int side)
        {
            _dodgeSide  = side;
            _dodgeTimer = side != 0 ? 0.3f : 0f;
        }

        // ---- Tick --------------------------------------------------------------------------------

        public void Tick(float dt)
        {
            if (_dodgeTimer > 0f) { _dodgeTimer -= dt; if (_dodgeTimer <= 0f) _dodgeSide = 0; }
            float kb = 1f - Mathf.Exp(-14f * dt);
            _blockW = Mathf.Lerp(_blockW, _blocking ? 1f : 0f, kb);

            if (FoeSpring.Enabled) SquashStep(dt);
            for (int s = 0; s < 2; s++)
            {
                float sg = Sign(s);

                // вылет и возврат
                if (_timer[s] > 0f)
                {
                    _timer[s] -= dt;
                    float e0 = _ext[s];
                    _ext[s] = Mathf.Min(1f, _ext[s] + dt / _outT[s]);
                    // касание: перчатка сплющивается и пружинит
                    if (FoeSpring.Enabled && _landed[s] && e0 < 1f && _ext[s] >= 1f)
                    {
                        int ki = _kind[s] == PunchType.Mega ? 2 : _kind[s] == PunchType.Jab ? 0 : 1;
                        _sqv[s] += SqKick[ki];
                        if (Recoil) _rcv[s] += RcKick[ki];
                    }
                }
                else if (_ext[s] > 0f) _ext[s] = Mathf.Max(0f, _ext[s] - dt / ReturnT);

                Vector3 bt = _blocking ? _block[s] : _rest[s];
                if (_dodgeSide != 0) bt += new Vector3(_dodgeSide * 0.05f, -0.015f, 0f);
                _base[s] = Vector3.Lerp(_base[s], bt, kb);

                float e = 1f - (1f - _ext[s]) * (1f - _ext[s]);
                float arc = Mathf.Sin(Mathf.PI * _ext[s]);

                // стойка: костяшки вверх и вперёд, тыльная сторона наружу и к игроку
                Vector3 pos  = _base[s];
                Vector3 kn   = new Vector3(-sg * 0.14f, 0.78f, 0.61f);
                Vector3 back = new Vector3(sg * 0.8f, 0.2f, -0.55f);
                float au = sg * Mathf.Lerp(_restAU, BlockAU, _blockW), az = AnchorZ;

                if (e > 0f)
                {
                    _goal[s] = Goal(s);
                    Vector3 pk, pb, bow;
                    float pu;
                    switch (_kind[s])
                    {
                        case PunchType.Hook:
                            pk = new Vector3(-sg * 0.9f, 0.05f, 0.45f); pb = new Vector3(0f, 0.75f, -0.65f);
                            bow = new Vector3(sg * 0.17f, 0.02f, -0.04f); pu = HookAU; break;
                        case PunchType.Upper:
                            pk = new Vector3(0f, 0.85f, 0.52f); pb = new Vector3(sg * 0.25f, -0.45f, 0.85f);
                            bow = new Vector3(0f, -0.16f, 0f); pu = UpperAU; break;
                        case PunchType.Mega:
                            pk = new Vector3(0f, 0.05f, 1f); pb = new Vector3(sg * 0.3f, 1f, 0f);
                            bow = new Vector3(sg * 0.08f, 0.06f, -0.10f); pu = MegaAU; break;
                        default:
                            pk = new Vector3(0f, 0.05f, 1f); pb = new Vector3(sg * 0.3f, 1f, 0f);
                            bow = Vector3.zero; pu = JabAU; break;
                    }
                    if (FoeSpring.Enabled) bow *= 0.5f;   // плечо уже ведёт дугу: прибавка по дуге меньше
                    pos  = Vector3.Lerp(pos, _goal[s], e) + bow * arc;
                    // отдача после попадания: назад по линии удара к плечу
                    _rcDir[s] = (RealShoulder(s, 1f) - _goal[s]).normalized;
                    if (_rc[s] != 0f) pos += _rcDir[s] * _rc[s];
                    kn   = Vector3.Lerp(kn, pk, e);
                    back = Vector3.Lerp(back, pb, e);
                    au   = Mathf.Lerp(au, sg * pu, e);
                    az   = Mathf.Lerp(AnchorZ, AnchorPunchZ, e);
                    // размах хука не уводит перчатку за боковой край кадра
                    float lim = Mathf.Max(0.05f, pos.z) * _tanH * EdgeX;
                    pos.x = Mathf.Clamp(pos.x, -lim, lim);
                }

                float lockW = 0f;
                if (s == _vicSide)
                {
                    _vicW = Mathf.MoveTowards(_vicW, _vicOn ? 1f : 0f, dt / VictoryIn);
                    if (_vicW > 0f)
                    {
                        VictoryPose(sg, _vicW, ref pos, ref kn, ref back, ref au);
                        az = Mathf.Lerp(az, VictoryAnchorZ, _vicW);
                        lockW = _vicW;
                    }
                }
                else if (_vicSide >= 0 && _vicW > 0f)
                    pos.y = Mathf.Lerp(pos.y, VictoryDropNY * pos.z * _tanV, _vicW);

                float reachW = Mathf.Max(e, lockW);
                Vector3 kp = _kind[s] == PunchType.Hook ? PoleHook : _kind[s] == PunchType.Upper ? PoleUpper : PoleJab;
                Place(s, pos, kn, back, au, az, lockW, Mathf.Lerp(RestFollow, ForeFollow, reachW), reachW, e, kp, _kind[s]);
                if (FoeSpring.Enabled) SquashApply(s);
            }
        }

        // Поднятая рука (оси камеры): запястье в точке хвата, костяшки вверх; плечевой сустав под нижним углом кадра,
        // локоть сгибается вниз-наружу: плечо идёт из угла, предплечье — вверх к перчатке. w — вес позы 0..1.
        void VictoryPose(float sg, float w, ref Vector3 pos, ref Vector3 kn, ref Vector3 back, ref float au)
        {
            Vector3 up = ToArm(_cam.InverseTransformDirection(Vector3.up)).normalized;
            Vector3 vk = (up * 0.92f + new Vector3(-sg * 0.12f, 0f, 0.2f)).normalized;
            Vector3 gt = _vicWrist + vk * (BoxerParts.GloveOff * Scale);
            float vu = sg * VictoryAU;
            pos  = Vector3.Lerp(pos, gt, w);
            kn   = Vector3.Lerp(kn, vk, w).normalized;
            back = Vector3.Lerp(back, new Vector3(sg * 0.3f, 0f, -1f), w);
            au   = Mathf.Lerp(au, vu, w);
        }

        // Поставить руку (оси камеры): pos — центр перчатки, kn — направление костяшек, back — тыльная сторона,
        // au/az — плечевой сустав под кадром, lockW — насколько запястье прижато к точке хвата рефери,
        // follow — насколько ось перчатки следует за предплечьем; reachW — вес вытянутой руки (удар, поднятая рука):
        // сустав уезжает вглубь по нижнему краю кадра к ближайшей к запястью точке, чтобы плечо не тянулось жердью.
        void Place(int s, Vector3 pos, Vector3 kn, Vector3 back, float au, float az, float lockW, float follow, float reachW,
                   float ext = 0f, Vector3 kindPole = default, PunchType kind = PunchType.Jab)
        {
            float go = BoxerParts.GloveOff * Scale * _gs;
            Vector3 k = kn.sqrMagnitude > 1e-8f ? kn.normalized : Vector3.forward;
            Vector3 pole = new Vector3(Sign(s) * PoleOut, -1f, 0f);
            bool sp = FoeSpring.Enabled;
            // первый проход: локоть для запястья по костяшкам, от него ось перчатки; второй — по настоящему запястью
            Vector3 w = pos - k * go;
            Vector3 rp = sp ? kindPole : _reachPole;
            if (reachW > 0f) pole = Vector3.Lerp(pole, new Vector3(Sign(s) * rp.x, rp.y, rp.z), reachW);
            // с пружинностью звенья вытянутой руки не тянутся (179° — предел только для вырожденного треугольника)
            float maxDeg = Mathf.Lerp(ElbowMinDeg, sp ? 179f : ReachElbowDeg, reachW);
            if (lockW > 0f)
            {
                maxDeg = Mathf.Lerp(maxDeg, sp ? 179f : VictoryElbowDeg, lockW);
                Vector3 vp = sp ? new Vector3(Sign(s) * PoleVictory.x, PoleVictory.y, PoleVictory.z) : new Vector3(Sign(s), -0.9f, -0.2f);
                pole = Vector3.Lerp(pole, vp, lockW);   // локоть вниз-наружу, к углу кадра
            }
            Vector3 sh0 = ArmShoulder(s, au, az, w, reachW, lockW, ext);
            Vector3 el = Elbow(w, sh0, pole, maxDeg);
            float pw = _pose ? reachW * (1f - lockW) : 0f;   // только удар, не поднятая рука
            if (pw > 0f) SideArm(s, w, ref el, ref sh0, pw, kind);
            if (pw > 0f) follow = Mathf.Lerp(follow, SideFollow, pw);
            Vector3 ga = Dir(Dir(w - el, k) * follow + k * (1f - follow), k);
            w = pos - ga * go;
            if (lockW > 0f) { w = Vector3.Lerp(w, _vicWrist, lockW); pos = w + ga * go; }
            Vector3 sh = ArmShoulder(s, au, az, w, reachW, lockW, ext);
            // сустав дальше, чем достаёт рука с предельно вытянутым плечом, — подтянуть его к запястью
            float reach = GloveLooks.UpperLenCm * 0.01f * Scale * UpperMaxStretch + GloveLooks.ForeLenCm * 0.01f * Scale - 0.01f;
            if (sp) reach = Mathf.Lerp(reach, (GloveLooks.UpperLenCm + GloveLooks.ForeLenCm) * 0.01f * Scale * 0.985f, Mathf.Max(reachW, lockW));
            Vector3 ws = sh - w;
            if (ws.sqrMagnitude > reach * reach) sh = w + ws.normalized * reach;
            el = Elbow(w, sh, pole, maxDeg);
            if (pw > 0f) SideArm(s, w, ref el, ref sh, pw, kind);
            Vector3 fa = Dir(w - el, ga), ua = Dir(el - sh, fa);

            _glove[s].localPosition = pos;
            _glove[s].localRotation = Quaternion.LookRotation(ga, Perp(back, ga));
            // предплечье: сетка от запястья по −Y до локтя; длина обычно ровно ForeLenCm, иначе — подогнать
            _fore[s].localPosition = w;
            _fore[s].localRotation = Quaternion.LookRotation(Perp(back, fa), fa);
            SetLenScale(_fore[s], (w - el).magnitude / (GloveLooks.ForeLenCm * 0.01f));
            // плечо: сетка от локтя по −Y до сустава под кадром и чуть дальше
            _upper[s].localPosition = el;
            _upper[s].localRotation = Quaternion.LookRotation(Perp(back, ua), ua);
            SetLenScale(_upper[s], ((el - sh).magnitude + UpperOver) / (GloveLooks.UpperLenCm * 0.01f));
        }

        // Локоть за боковым краем (PoseFix): направления звеньев поворачиваются к SideFore…/SideUpper с весом pw, длины те же.
        void SideArm(int s, Vector3 w, ref Vector3 el, ref Vector3 sh, float pw, PunchType kind)
        {
            float sg = Sign(s);
            Vector3 f = kind == PunchType.Hook ? SideForeHook : kind == PunchType.Upper ? SideForeUpper : SideForeJab;
            Vector3 d1 = new Vector3(sg * f.x, f.y, f.z).normalized, d2 = new Vector3(sg * SideUpper.x, SideUpper.y, SideUpper.z).normalized;
            float lf = (el - w).magnitude, lu = (sh - el).magnitude;
            Vector3 u0 = Dir(sh - el, d2);
            el = w + Vector3.Slerp(Dir(el - w, d1), d1, pw) * lf;
            sh = el + Vector3.Slerp(u0, d2, pw) * lu;
        }

        // Длина звена по Y; растянутое звено толще (как ближе к камере), чтобы не читалось жердью.
        void SetLenScale(Transform t, float sy)
        {
            if (Mathf.Abs(t.localScale.y - sy) <= 1e-4f) return;
            float th = Scale * Mathf.Max(1f, Mathf.Sqrt(sy / Scale));
            t.localScale = new Vector3(th, sy, th);
        }

        // Локоть по двухзвенной обратной кинематике (оси камеры): sh — плечевой сустав, w — запястье, pole — куда гнётся.
        // Предплечье не тянется; если сустав далеко (удар в дальнюю голову, рука у рефери), плечо удлиняется так,
        // чтобы угол в локте был не прямее ElbowMinDeg. Локоть в кадре не уходит за боковой край («третья рука»).
        Vector3 Elbow(Vector3 w, Vector3 sh, Vector3 pole, float maxDeg)
        {
            float l2 = GloveLooks.ForeLenCm * 0.01f * Scale;
            float l1 = GloveLooks.UpperLenCm * 0.01f * Scale;
            Vector3 u = w - sh;
            float d = u.magnitude;
            if (d < 1e-4f) return w + Vector3.down * l2;
            u /= d;
            // теорема косинусов: при угле в локте ElbowMinDeg плечо длиной lmin
            float c = Mathf.Cos(maxDeg * Mathf.Deg2Rad);
            float lmin = l2 * c + Mathf.Sqrt(Mathf.Max(0f, d * d - l2 * l2 * (1f - c * c)));
            l1 = Mathf.Min(Mathf.Max(l1, lmin), l1 * UpperMaxStretch);
            if (d < l1 - l2 + 0.02f) l1 = d + l2 - 0.02f;    // запястье у самого сустава: треугольник не вырождается
            float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 pp = pole - u * Vector3.Dot(pole, u);
            pp = pp.sqrMagnitude < 1e-8f ? Vector3.down : pp.normalized;
            Vector3 e = sh + u * a + pp * h;
            if (e.z > 0.05f && e.y + e.z * _tanV > 0f)
            {
                float lim = e.z * _tanH * EdgeX;
                e.x = Mathf.Clamp(e.x, -lim, lim);
            }
            return e;
        }

        // Плечевой сустав под нижним краем кадра: на глубине az, по горизонтали au долей полуширины, за нижней плоскостью
        // кадра на AnchorGap и не ближе к ней, чем запястье w (иначе плечо заходит в кадр своим срезом).
        Vector3 Anchor(float au, float az, Vector3 w)
        {
            float wIn = (w.y + w.z * _tanV) / _nLen;           // расстояние запястья от нижней плоскости, + внутри кадра
            float d = Mathf.Min(-AnchorGap, wIn - AnchorGap);
            return new Vector3(au * az * _tanH, d * _nLen - az * _tanV, az);
        }

        // Плечевой сустав руки: без пружинности — как раньше; с ней вытянутая и поднятая рука идут от настоящего плеча.
        Vector3 ArmShoulder(int s, float au, float az, Vector3 w, float reachW, float lockW, float ext)
        {
            if (!FoeSpring.Enabled) return VicShoulder(s, Shoulder(s, au, az, w, reachW), lockW);
            Vector3 sh = Anchor(au, az, w);
            if (reachW > 0f) sh = Vector3.Lerp(sh, RealShoulder(s, ext), reachW);
            return lockW > 0f ? Vector3.Lerp(sh, VicRealShoulder(s), lockW) : sh;
        }

        // Сплющивание перчаток: пружина с фиксированным шагом SqH (реальное время, как вся рука игрока)
        void SquashStep(float dt)
        {
            if (!(dt > 0f)) return;
            _sqAcc = Mathf.Min(_sqAcc + dt, 0.1f);
            while (_sqAcc >= SqH)
            {
                _sqAcc -= SqH;
                for (int s = 0; s < 2; s++)
                {
                    _sqv[s] += (-SqK * _sq[s] - SqC * _sqv[s]) * SqH;
                    _sq[s] = Mathf.Clamp(_sq[s] + _sqv[s] * SqH, SqMin, SqMax);
                    _rcv[s] += (-RcK * _rc[s] - RcC * _rcv[s]) * SqH;
                    _rc[s] = Mathf.Clamp(_rc[s] + _rcv[s] * SqH, -RcMax * 0.3f, RcMax);
                }
            }
        }

        // Масштаб перчатки: вдоль оси (z) сжата, поперёк раздута; в покое — ровно Scale
        void SquashApply(int s)
        {
            float q = Mathf.Abs(_sq[s]) > 0.002f ? _sq[s] : 0f;
            float gs = Scale * _gs;
            var sc = new Vector3(gs * (1f + q * 0.45f), gs * (1f + q * 0.45f), gs * (1f - q));
            if ((_glove[s].localScale - sc).sqrMagnitude > 1e-10f) _glove[s].localScale = sc;
        }

        public float Squash(int s) => _sq[s];

        static Vector3 VicShoulder(int s, Vector3 sh, float lockW) =>
            lockW > 0f ? Vector3.Lerp(sh, new Vector3(Sign(s) * VictoryShoulder.x, VictoryShoulder.y, VictoryShoulder.z), lockW) : sh;

        Vector3 Shoulder(int s, float au, float az, Vector3 w, float reachW)
        {
            Vector3 sh = Anchor(au, az, w);
            return reachW > 0f ? Vector3.Lerp(sh, ReachAnchor(s, w), reachW) : sh;
        }

        // Сустав вытянутой руки: от запястья вниз-наружу-назад на ReachLen (локоть согнут заметно), затем дальше по
        // тому же лучу, пока сустав не выйдет за боковую или нижнюю плоскость кадра на AnchorGap. Рука входит снизу
        // или из нижней части своего края; плечо тянется, только если иначе сустав остался бы в кадре.
        Vector3 ReachAnchor(int s, Vector3 w)
        {
            float sg = Sign(s);
            Vector3 d = new Vector3(sg * _reachDir.x, _reachDir.y, _reachDir.z).normalized;
            float sideN = Mathf.Sqrt(1f + _tanH * _tanH);
            // запас до плоскости (+ внутри кадра): боковая своей стороны и нижняя; обе плоскости через камеру
            float mSideW = (-sg * w.x + w.z * _tanH) / sideN, mSideD = (-sg * d.x + d.z * _tanH) / sideN;
            float mBotW = (w.y + w.z * _tanV) / _nLen, mBotD = (d.y + d.z * _tanV) / _nLen;
            float needSide = mSideD < -1e-4f ? (mSideW + AnchorGap) / -mSideD : float.MaxValue;
            float needBot  = mBotD  < -1e-4f ? (mBotW  + AnchorGap) / -mBotD  : float.MaxValue;
            // через боковой край — только в его нижней части (ниже SideLowNY высоты кадра), иначе «третья рука» сбоку
            float k = d.y - _sideLowNY * _tanV * d.z;
            if (k < -1e-4f) needSide = Mathf.Max(needSide, (w.y - _sideLowNY * _tanV * w.z) / -k);
            float need = Mathf.Min(needSide, needBot);
            float r = need == float.MaxValue ? ReachLen : Mathf.Max(ReachLen, need);
            return w + d * r;
        }

        static Vector3 Dir(Vector3 v, Vector3 fallback) => v.sqrMagnitude > 1e-8f ? v.normalized : fallback;

        // h, сделанный перпендикулярным единичной оси ax; не вырождается.
        static Vector3 Perp(Vector3 h, Vector3 ax)
        {
            Vector3 o = h - ax * Vector3.Dot(h, ax);
            if (o.sqrMagnitude < 1e-4f)
            {
                o = Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.forward;
                o -= ax * Vector3.Dot(o, ax);
            }
            return o.normalized;
        }

        public void Dispose()
        {
            DestroyMeshes();
            GameUtil.Destroy(_upperMesh);
            _upperMesh = null;
            for (int i = 0; i < 2; i++)
            {
                if (_upper[i] != null) GameUtil.Destroy(_upper[i].gameObject);
                if (_fore[i] != null) GameUtil.Destroy(_fore[i].gameObject);
                if (_glove[i] != null) GameUtil.Destroy(_glove[i].gameObject);
            }
            if (_rig != null) GameUtil.Destroy(_rig.gameObject);
        }
    }
}
