using UnityEngine;

namespace VoxGame
{
    // Рефери: воксельный человек в полосатой рубашке с бабочкой, тёмных брюках, с простой воксельной головой.
    // Управляется RefereeLogic: состояние, счёт, фаза позы 0..1. События логики не читает (их разбирает директор).
    // НЕ MonoBehaviour: Build → SetScene → Tick(dt, udt) → Dispose. Анимация идёт по реальному времени (udt).
    //
    // Оси боя: соперник в начале координат лицом в +Z, игрок смотрит на него из +Z в сторону −Z.
    // Рука рефери 0 — левая (−X его осей), 1 — правая (+X). Позы рук — углы суставов, переходы — интерполяция углов;
    // при хвате руки победителя кисть доводится до точки двухзвенным решением (локоть по треугольнику).
    public class RefereeView
    {
        // ---- Размеры, м ------------------------------------------------------------------------
        const float HipY = 0.94f;                      // центр таза над настилом
        const float HipJointX = 0.095f, HipJointY = -0.04f;
        const float ThighLen = 0.42f, ShinLen = 0.42f; // + 6 см от лодыжки до подошвы
        const float UpperLen = 0.29f, ForeLen = 0.26f;
        const float HandReach = 0.075f;                // запястье → середина хвата
        const float GripPalm = 0.035f;                 // середина хвата смещена к ладони
        static readonly Vector3 ShoulderLocal = new Vector3(0.18f, 0.40f, 0f);   // в осях торса, x — по знаку руки
        static readonly Vector3 HeadLocal = new Vector3(0f, 0.55f, 0.01f);

        // ---- Постановка (подгонять по снимкам) -------------------------------------------------
        // Место в покое — смещение от корня соперника: сбоку за ним, на экране справа (мир −X), не закрывая его.
        // Портрет узкий (по горизонтали ±16.5°), поэтому ближе к оси взгляда; ландшафт — дальше в сторону.
        public static readonly Vector3 IdlePortrait  = new Vector3(-0.52f, 0f, -1.10f);
        public static readonly Vector3 IdleLandscape = new Vector3(-0.80f, 0f, -0.70f);
        // У упавшего соперника: сбоку и позади, лицом между соперником и игроком. Камера в нокдауне смотрит вниз
        // на упавшего, а верх кадра занят полосами HUD, поэтому рефери не вплотную: иначе видны только брюки.
        public static readonly Vector3 CountSpot = new Vector3(-0.38f, 0f, -0.85f);
        const float CountSquat = 38f;   // при счёте рефери приседает к упавшему: голова ниже полос HUD; угол бедра, градусы
        // Упал игрок: рефери шагает к камере (в осях камеры по горизонтали: вправо, вперёд)
        const float PlayerDownRight = 0.38f, PlayerDownFwd = 0.70f;
        // Победил соперник: рефери справа от него на экране и поднимает его руку 1 (мир −X). Оси корня соперника.
        static readonly Vector3 FoeWinStand    = new Vector3(-0.60f, 0f, 0.10f);
        static readonly Vector3 FoeWristGuard  = new Vector3(-0.12f, 1.36f, 0.20f);
        static readonly Vector3 FoeWristRaised = new Vector3(-0.30f, 1.95f, 0.05f);
        // Победил игрок: рефери справа от камеры, берёт левую перчатку игрока за запястье (оси камеры).
        // Левую — чтобы предплечье игрока уходило за левый край, а не поперёк рефери.
        static readonly Vector3 PlayerWristRest   = new Vector3(-0.15f, -0.30f, 0.56f);
        // Поднятое запястье — у середины и выше глаз: перчатка вверху кадра (HUD в развязке убран).
        // Рефери стоит сбоку справа (в портрете виден по пояс у края кадра), его рука почти прямая, не поперёк лица.
        static readonly Vector3 PlayerWristRaised = new Vector3(-0.04f, 0.24f, 0.92f);
        const float PlayerWinRight = 0.30f, PlayerWinFwd = 1.30f;
        // Пояс выносят перед камерой: рефери отпускает руку и отходит в сторону, чтобы не стоять за поясом.
        const float BeltStepAside = 0.55f;

        // Какой рукой победителя машет рефери: у игрока сторона как в PlayerGloves.Punch (−1 — левая),
        // у соперника — номер руки FoeBody (1 — на экране справа, мир −X).
        public const int PlayerGripSide = -1;
        public const int FoeGripArm = 1;

        const float WalkSpeed  = 1.1f;    // м/с, возврат на место
        const float StrideLen  = 0.30f;   // м на полшага
        const float PoseRate   = 10f;     // скорость сглаживания углов
        const float StrikeRate = 26f;     // взмах счёта
        const int   CountArm   = 1;       // считает правой: она со стороны упавшего, взмах виден на фоне зала

        // ---- Цвета ------------------------------------------------------------------------------
        static readonly Color32 CSkin    = new Color32(206, 150, 112, 255);
        static readonly Color32 CWhite   = new Color32(234, 234, 230, 255);
        static readonly Color32 CBlack   = new Color32(30, 30, 34, 255);
        static readonly Color32 CBow     = new Color32(14, 14, 16, 255);
        static readonly Color32 CPants   = new Color32(38, 40, 50, 255);
        static readonly Color32 CBelt    = new Color32(16, 16, 18, 255);
        static readonly Color32 CSilver  = new Color32(184, 186, 194, 255);
        static readonly Color32 CShoe    = new Color32(20, 20, 22, 255);
        static readonly Color32 CSole    = new Color32(70, 70, 74, 255);
        static readonly Color32 CHair    = new Color32(74, 72, 76, 255);
        static readonly Color32 CEye     = new Color32(28, 24, 24, 255);
        static readonly Color32 CMouth   = new Color32(132, 70, 64, 255);

        // ---- Объекты ----------------------------------------------------------------------------
        Transform _root, _hips, _torso, _head;
        readonly Transform[] _thigh = new Transform[2], _shin = new Transform[2], _shoe = new Transform[2];
        readonly Transform[] _upper = new Transform[2], _fore = new Transform[2], _hand = new Transform[2];
        readonly GameObject[] _handOpen = new GameObject[2], _handGrip = new GameObject[2];
        readonly bool[] _gripShown = new bool[2];
        Mesh[] _meshes;

        RefereeLogic _logic;
        Transform _foeRoot, _foeHead, _cam;
        float _floorY;
        bool _landscape;

        // ---- Состояние --------------------------------------------------------------------------
        RefereeState _prev = RefereeState.Idle;
        Vector3 _pos, _from;
        float _yaw;
        float _t, _gait, _walkAmt;
        int _gripArm = 1;
        float _gripW;
        Vector3 _gripPoint;
        float _headYaw, _headPitch;

        // Углы рук (текущие и целевые), градусы: подъём вперёд, отведение в сторону, поворот плеча, сгиб локтя
        readonly float[] _raise = new float[2], _abd = new float[2], _twist = new float[2], _bend = new float[2];
        readonly float[] _tRaise = new float[2], _tAbd = new float[2], _tTwist = new float[2], _tBend = new float[2];
        readonly float[] _armRate = new float[2];
        float _lean, _tLean, _roll, _tRoll, _spin, _tSpin, _squat, _tSquat;

        static float Sign(int i) => i == 0 ? -1f : 1f;

        public Transform Root => _root;
        public RefereeState State => _logic != null ? _logic.State : RefereeState.Idle;
        public bool WinnerIsPlayer => _logic == null || _logic.PlayerWon;

        // Рефери держит руку победителя: директор каждый кадр ведёт запястье победителя в GripPoint().
        public bool IsGripping => _logic != null && _gripW > 0.5f && !_letGo &&
                                  (_logic.State == RefereeState.RaiseHand || _logic.State == RefereeState.Done);

        // Отпустить руку победителя (вручают пояс): рука рефери опускается; снова хватает только в следующей развязке.
        public void LetGo() => _letGo = true;
        bool _letGo;

        // Мировая точка хвата: середина кисти рефери со стороны ладони — сюда ставится запястье победителя.
        public Vector3 GripPoint() => _gripPoint;

        // ---- Построение --------------------------------------------------------------------------

        public void Build(Transform parent, Material voxMat, RefereeLogic logic)
        {
            _logic = logic;
            _floorY = parent != null ? parent.position.y : 0f;

            _root = new GameObject("Referee").transform;
            _root.SetParent(parent, false);

            var head = BuildHead();
            var torso = BuildTorso();
            var hips = BuildHips();
            var upper = BuildUpperArm();
            var fore = BuildForeArm();
            var thigh = BuildLeg(ThighLen * 100f, 7.2f, 5.6f, "refThigh");
            var shin = BuildLeg(ShinLen * 100f, 5.6f, 4.6f, "refShin");
            var shoe = BuildShoe();
            var open = new[] { BuildHand(-1f, false), BuildHand(1f, false) };
            var grip = new[] { BuildHand(-1f, true), BuildHand(1f, true) };
            _meshes = new[] { head, torso, hips, upper, fore, thigh, shin, shoe, open[0], open[1], grip[0], grip[1] };

            _hips = VoxModel.Spawn("refHips", _root, hips, voxMat);
            _torso = VoxModel.Spawn("refTorso", _root, torso, voxMat);
            _head = VoxModel.Spawn("refHead", _torso, head, voxMat);
            _head.localPosition = HeadLocal;

            for (int i = 0; i < 2; i++)
            {
                _thigh[i] = VoxModel.Spawn("refThigh" + i, _hips, thigh, voxMat);
                _thigh[i].localPosition = new Vector3(Sign(i) * HipJointX, HipJointY, 0f);
                _shin[i] = VoxModel.Spawn("refShin" + i, _thigh[i], shin, voxMat);
                _shin[i].localPosition = new Vector3(0f, -ThighLen, 0f);
                _shoe[i] = VoxModel.Spawn("refShoe" + i, _shin[i], shoe, voxMat);
                _shoe[i].localPosition = new Vector3(0f, -ShinLen, 0f);

                _upper[i] = VoxModel.Spawn("refUpperArm" + i, _root, upper, voxMat);
                _fore[i] = VoxModel.Spawn("refForeArm" + i, _root, fore, voxMat);
                _hand[i] = new GameObject("refHand" + i).transform;
                _hand[i].SetParent(_root, false);
                _handOpen[i] = VoxModel.Spawn("open", _hand[i], open[i], voxMat).gameObject;
                _handGrip[i] = VoxModel.Spawn("grip", _hand[i], grip[i], voxMat).gameObject;
                _handGrip[i].SetActive(false);
            }
        }

        // Соперник (корень и крепление головы) и камера игрока. Вызывать после Build и до первого Tick.
        public void SetScene(Transform foeRoot, Transform foeHead, Transform playerCam)
        {
            _foeRoot = foeRoot; _foeHead = foeHead; _cam = playerCam;
            Snap();
        }

        // Пропорции экрана: место в покое для портрета и ландшафта.
        public void SetView(float aspect) => _landscape = aspect > 1f;

        // Сразу встать на место покоя в позе покоя (начало боя, переигровка).
        public void Snap()
        {
            if (_foeRoot == null || _root == null) return;
            _prev = State;
            _pos = _from = IdleSpot();
            _yaw = YawTo(_pos, Mid());
            _walkAmt = 0f; _gripW = 0f; _letGo = false; _tSquat = 0f;
            IdlePose();
            for (int i = 0; i < 2; i++)
            {
                _raise[i] = _tRaise[i]; _abd[i] = _tAbd[i]; _twist[i] = _tTwist[i]; _bend[i] = _tBend[i];
            }
            _lean = _tLean; _roll = _tRoll; _spin = _tSpin; _squat = _tSquat;
            Apply(0f, Mid());
        }

        // ---- Точки сцены (мир) ---------------------------------------------------------------------

        Vector3 FoeHead() => _foeHead != null ? _foeHead.position : _foeRoot.position + Vector3.up * 1.6f;

        Vector3 Floor(Vector3 p) { p.y = _floorY; return p; }

        void CamAxes(out Vector3 eye, out Vector3 f, out Vector3 r)
        {
            eye = _cam != null ? _cam.position : _foeRoot.position + new Vector3(0f, 1.6f, 1.2f);
            f = _cam != null ? Vector3.ProjectOnPlane(_cam.forward, Vector3.up) : Vector3.back;
            f = f.sqrMagnitude < 1e-6f ? Vector3.back : f.normalized;
            r = Vector3.Cross(Vector3.up, f);
        }

        Vector3 Cam() { CamAxes(out var e, out _, out _); return e; }

        // Середина между головой соперника и игроком — сюда смотрит корпус в покое
        Vector3 Mid() => (FoeHead() + Cam()) * 0.5f;

        Vector3 FoeSpot(Vector3 local) => Floor(_foeRoot.position + _foeRoot.rotation * local);

        Vector3 CamSpot(float right, float fwd)
        {
            CamAxes(out var e, out var f, out var r);
            return Floor(e + f * fwd + r * right);
        }

        Vector3 IdleSpot() => FoeSpot(_landscape ? IdleLandscape : IdlePortrait);

        // Куда смотрит/к кому идёт рефери у лежащего: соперник лежит при победе игрока, иначе лежит игрок
        Vector3 DownSpot() => WinnerIsPlayer ? FoeSpot(CountSpot) : CamSpot(PlayerDownRight, PlayerDownFwd);
        Vector3 DownTarget() => WinnerIsPlayer ? FoeHead() : Cam() + Vector3.down * 0.5f;

        Vector3 WinStand() => WinnerIsPlayer ? CamSpot(PlayerWinRight + (_letGo ? BeltStepAside : 0f), PlayerWinFwd)
                                             : FoeSpot(FoeWinStand);

        Vector3 GripStart() => WinnerIsPlayer
            ? (_cam != null ? _cam.TransformPoint(PlayerWristRest) : Cam())
            : _foeRoot.TransformPoint(FoeWristGuard);

        Vector3 GripEnd() => WinnerIsPlayer
            ? (_cam != null ? _cam.TransformPoint(PlayerWristRaised) : Cam())
            : _foeRoot.TransformPoint(FoeWristRaised);

        // Лицом к победителю и к залу: к игроку, если победил он, иначе к камере
        Vector3 WinFace()
        {
            if (!WinnerIsPlayer) return Cam();
            CamAxes(out var e, out var f, out _);
            return e + f * 0.25f;
        }

        static float YawTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0f;
            return d.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        // ---- Tick -------------------------------------------------------------------------------

        public void Tick(float dt, float udt)
        {
            if (_root == null || _logic == null || _foeRoot == null) return;
            udt = Mathf.Max(0f, udt);
            _t += udt;

            RefereeState st = _logic.State;
            float p = _logic.Phase;
            if (st != _prev)
            {
                _from = _pos;
                // рука хвата — та, чьё плечо ближе к поднятому запястью победителя (иначе рука идёт поперёк лица)
                if (st == RefereeState.WalkToWinner)
                {
                    Vector3 stand = WinStand(), g = GripEnd();
                    float yaw = YawTo(stand, WinFace());
                    Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                    float d0 = (stand + q * new Vector3(-ShoulderLocal.x, HipY + ShoulderLocal.y, 0f) - g).sqrMagnitude;
                    float d1 = (stand + q * new Vector3(ShoulderLocal.x, HipY + ShoulderLocal.y, 0f) - g).sqrMagnitude;
                    _gripArm = d0 < d1 ? 0 : 1;
                }
                if (st == RefereeState.Idle && (_prev == RefereeState.Done || _prev == RefereeState.RaiseHand))
                    _gripW = 0f;   // переигровка: руку отпускаем сразу
                if (st == RefereeState.Idle || st == RefereeState.WalkToWinner) _letGo = false;
                _prev = st;
            }

            // ---- куда идти и куда смотреть
            Vector3 dest, face, look;
            bool timed = false;
            switch (st)
            {
                case RefereeState.StepIn:
                    dest = DownSpot(); face = DownTarget(); look = DownTarget(); timed = true; break;
                case RefereeState.Counting:
                case RefereeState.StopFight:
                    dest = DownSpot(); face = DownTarget(); look = DownTarget(); break;
                case RefereeState.Resume:
                    dest = _pos; face = Mid(); look = FoeHead(); break;
                case RefereeState.WalkToWinner:
                    dest = WinStand(); face = WinFace(); look = GripStart(); timed = true; break;
                case RefereeState.RaiseHand:
                    dest = WinStand(); face = WinFace(); look = _gripPoint; break;
                case RefereeState.Done:
                    dest = WinStand(); face = WinFace(); look = Cam(); break;
                default:
                    dest = IdleSpot(); face = Mid(); look = FoeHead(); break;
            }

            Vector3 np = timed ? Vector3.Lerp(_from, dest, Smooth(p)) : Vector3.MoveTowards(_pos, dest, WalkSpeed * udt);
            np.y = _floorY;
            Vector3 step = np - _pos; step.y = 0f;
            float dist = step.magnitude;
            float speed = udt > 1e-5f ? dist / udt : 0f;
            _pos = np;
            _gait += dist / StrideLen * Mathf.PI;
            float ka = 1f - Mathf.Exp(-12f * udt);
            _walkAmt = Mathf.Lerp(_walkAmt, Mathf.Clamp01(speed / 0.7f), ka);

            // корпус: по ходу движения, а под конец перехода и на месте — к цели
            float wantYaw = speed > 0.15f && !(timed && p > 0.75f) ? Mathf.Atan2(step.x, step.z) * Mathf.Rad2Deg
                                                                   : YawTo(_pos, face);
            _yaw = Mathf.LerpAngle(_yaw, wantYaw, 1f - Mathf.Exp(-8f * udt));

            // ---- поза
            float gripT = 0f;
            Vector3 gripTarget = GripStart();
            for (int i = 0; i < 2; i++) _armRate[i] = PoseRate;
            _tSquat = 0f;
            switch (st)
            {
                case RefereeState.StepIn:
                    IdlePose();
                    if (p > 0.55f) CountArmPose(0.6f * Smooth((p - 0.55f) / 0.45f));   // рука уже идёт вверх
                    _tLean = 8f * Smooth(p);
                    _tSquat = Smooth((p - 0.5f) / 0.5f);
                    break;
                case RefereeState.Counting:
                    IdlePose();
                    CountArmPose(p);
                    _armRate[CountArm] = StrikeRate;
                    int other = 1 - CountArm;   // вторая рука на колене
                    SetArm(other, 28f, 12f, -30f, 52f);
                    _tLean = 26f; _tRoll = 0f; _tSpin = 0f; _tSquat = 1f;   // наклон и присед к упавшему
                    break;
                case RefereeState.Resume:
                {
                    // руки сведены перед собой — и разводятся в стороны: «бокс»
                    float k = Smooth((p - 0.3f) / 0.45f);
                    for (int i = 0; i < 2; i++)
                        SetArm(i, Mathf.Lerp(88f, 78f, k), Mathf.Lerp(-10f, 74f, k), -90f, Mathf.Lerp(18f, 6f, k));
                    _tLean = 6f; _tRoll = 0f; _tSpin = 0f;
                    break;
                }
                case RefereeState.StopFight:
                {
                    // два скрещивающих маха над головой
                    float w = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * 2f * p);
                    float up = Smooth(p / 0.2f);
                    for (int i = 0; i < 2; i++)
                        SetArm(i, Mathf.Lerp(40f, 120f, up), Mathf.Lerp(42f, -30f, w), -90f, 24f);   // кисти над головой, но ниже полос HUD
                    _tLean = -4f; _tRoll = 0f; _tSpin = 0f;
                    break;
                }
                case RefereeState.WalkToWinner:
                    IdlePose();
                    gripT = Smooth((p - 0.55f) / 0.45f);
                    break;
                case RefereeState.RaiseHand:
                case RefereeState.Done:
                {
                    IdlePose();
                    float k = st == RefereeState.Done ? 1f : Smooth(p);
                    Vector3 a = GripStart(), b = GripEnd();
                    gripTarget = Vector3.Lerp(a, b, k) + Vector3.up * (0.05f * Mathf.Sin(Mathf.PI * k));
                    if (st == RefereeState.Done) gripTarget += Vector3.up * (0.012f * Mathf.Sin(_t * 3.2f));
                    gripT = _letGo ? 0f : 1f;
                    SetArm(1 - _gripArm, 10f, 16f, -20f, 22f);   // свободная рука опущена вдоль тела
                    _tLean = -3f; _tRoll = 0f; _tSpin = 0f;
                    break;
                }
                default:
                    IdlePose();
                    break;
            }

            // взмахи рук при ходьбе (против ног), только у свободных рук
            if (_walkAmt > 0.01f && st != RefereeState.StopFight && st != RefereeState.Resume)
                for (int i = 0; i < 2; i++)
                {
                    if (st == RefereeState.Counting && i == CountArm) continue;
                    _tRaise[i] += Mathf.Sin(_gait + (i == 0 ? 0f : Mathf.PI)) * 16f * _walkAmt;
                }

            for (int i = 0; i < 2; i++)
            {
                float k = 1f - Mathf.Exp(-_armRate[i] * udt);
                _raise[i] = Mathf.Lerp(_raise[i], _tRaise[i], k);
                _abd[i]   = Mathf.Lerp(_abd[i], _tAbd[i], k);
                _twist[i] = Mathf.Lerp(_twist[i], _tTwist[i], k);
                _bend[i]  = Mathf.Lerp(_bend[i], _tBend[i], k);
            }
            float kb = 1f - Mathf.Exp(-PoseRate * udt);
            _lean = Mathf.Lerp(_lean, _tLean, kb);
            _roll = Mathf.Lerp(_roll, _tRoll, kb);
            _spin = Mathf.Lerp(_spin, _tSpin, kb);
            _squat = Mathf.Lerp(_squat, _tSquat, kb);
            _gripW = gripT >= _gripW ? gripT : Mathf.Lerp(_gripW, gripT, 1f - Mathf.Exp(-8f * udt));

            Apply(udt, look, gripTarget);
        }

        // Поза покоя: руки свободно перед собой, лёгкое покачивание с ноги на ногу
        void IdlePose()
        {
            float sw = Mathf.Sin(_t * 1.3f);
            for (int i = 0; i < 2; i++)
                SetArm(i, 14f + Sign(i) * sw * 3f, 10f, -20f, 38f);
            _tLean = 3f;
            _tRoll = sw * 2f;
            _tSpin = Mathf.Sin(_t * 0.7f) * 4f;
        }

        // Счёт: рука поднимается (0..0.6), замирает вверху (0.6..0.78) и рубит вниз к моменту счёта (0.78..1).
        // Рука отведена в сторону упавшего; верх — на уровне головы: выше рука ушла бы под полосы HUD.
        // Ладонь при подъёме смотрит вперёд, внизу — в настил. p ≤ 0.6 — только подъём (так рука готовится на подходе).
        void CountArmPose(float p)
        {
            float up = p < 0.6f ? Smooth(p / 0.6f) : p < 0.78f ? 1f : 1f - Smooth((p - 0.78f) / 0.22f);
            int i = CountArm;
            SetArm(i, Mathf.Lerp(55f, 115f, up), Mathf.Lerp(30f, 58f, up), -90f, Mathf.Lerp(10f, 35f, up));
        }

        void SetArm(int i, float raise, float abd, float twist, float bend)
        {
            _tRaise[i] = raise; _tAbd[i] = abd; _tTwist[i] = twist; _tBend[i] = bend;
        }

        // ---- Расстановка частей --------------------------------------------------------------------

        void Apply(float udt, Vector3 look) => Apply(udt, look, GripStart());

        void Apply(float udt, Vector3 look, Vector3 gripTarget)
        {
            _root.SetPositionAndRotation(_pos, Quaternion.Euler(0f, _yaw, 0f));

            // ноги: простой шаг; бедро вперёд-назад, колено гнётся, пока нога идёт вперёд, стопа параллельна полу.
            // Присед: бедро вперёд на sq, голень назад на sq — стопа остаётся под тазом.
            float maxThigh = 0f, sq = _squat * CountSquat;
            for (int i = 0; i < 2; i++)
            {
                float ph = _gait + (i == 0 ? 0f : Mathf.PI);
                float th = Mathf.Sin(ph) * 22f * _walkAmt;
                float kn = 4f + Mathf.Max(0f, Mathf.Cos(ph)) * 42f * _walkAmt;
                _thigh[i].localRotation = Quaternion.Euler(-th - sq, 0f, 0f);
                _shin[i].localRotation = Quaternion.Euler(kn + 2f * sq, 0f, 0f);
                _shoe[i].localRotation = Quaternion.Euler(th - kn - sq, 0f, 0f);
                if (Mathf.Cos(ph) <= 0f) maxThigh = Mathf.Max(maxThigh, Mathf.Abs(th));
            }
            // опорная нога прямая: таз опускается, чтобы стопа не висела над настилом
            float drop = (ThighLen + ShinLen) * (1f - Mathf.Cos(maxThigh * Mathf.Deg2Rad))
                       + (ThighLen + ShinLen) * (1f - Mathf.Cos(sq * Mathf.Deg2Rad));
            float sway = Mathf.Sin(_t * 1.3f) * 0.012f * (1f - _walkAmt);
            var pelvis = new Vector3(sway, HipY - drop, 0f);
            _hips.localPosition = pelvis;
            _hips.localRotation = Quaternion.Euler(0f, _spin * 0.5f, _roll * 0.5f);
            _torso.localPosition = pelvis;
            _torso.localRotation = Quaternion.Euler(_lean, _spin, _roll);

            // голова: следит за целью, повороты ограничены
            Vector3 d = _torso.InverseTransformDirection(look - _head.position);
            float wantYaw = 0f, wantPitch = 0f;
            if (d.sqrMagnitude > 1e-6f)
            {
                d.Normalize();
                wantYaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -70f, 70f);
                wantPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg, -35f, 45f);
            }
            float kh = udt > 0f ? 1f - Mathf.Exp(-9f * udt) : 1f;
            _headYaw = Mathf.LerpAngle(_headYaw, wantYaw, kh);
            _headPitch = Mathf.Lerp(_headPitch, wantPitch, kh);
            _head.localRotation = Quaternion.Euler(_headPitch, _headYaw, 0f);

            // руки: углы суставов → точки; рука хвата доводится до цели с весом _gripW
            Quaternion qt = _torso.rotation;
            for (int i = 0; i < 2; i++)
            {
                float s = Sign(i);
                Vector3 S = _torso.TransformPoint(new Vector3(s * ShoulderLocal.x, ShoulderLocal.y, ShoulderLocal.z));
                Quaternion qu = qt * Quaternion.AngleAxis(-_raise[i], Vector3.right)
                                   * Quaternion.AngleAxis(s * _abd[i], Vector3.forward)
                                   * Quaternion.AngleAxis(s * _twist[i], Vector3.up);
                Vector3 E = S + qu * Vector3.down * UpperLen;
                Quaternion qf = qu * Quaternion.AngleAxis(-_bend[i], Vector3.right);
                Vector3 W = E + qf * Vector3.down * ForeLen;

                float w = i == _gripArm ? _gripW : 0f;
                if (w > 0.001f)
                {
                    SolveIK(S, gripTarget, qt * new Vector3(s * 0.7f, -1f, -0.5f), out Vector3 ei, out Vector3 wi);
                    E = Vector3.Lerp(E, ei, w);
                    W = Vector3.Lerp(W, wi, w);
                }

                Quaternion ru = SegRot(S, E, qu * Vector3.forward);
                Quaternion rf = SegRot(E, W, qf * Vector3.forward);
                _upper[i].SetPositionAndRotation(S, ru);
                _fore[i].SetPositionAndRotation(E, rf);
                _hand[i].SetPositionAndRotation(W, rf);

                bool grip = w > 0.6f;
                if (grip != _gripShown[i])
                {
                    _gripShown[i] = grip;
                    _handGrip[i].SetActive(grip);
                    _handOpen[i].SetActive(!grip);
                }
                if (i == _gripArm)
                    _gripPoint = W + rf * new Vector3(-s * GripPalm, -HandReach, 0f);
            }
        }

        // Локоть и запястье, чтобы середина кисти пришла в target; локоть смотрит в сторону pole.
        static void SolveIK(Vector3 S, Vector3 target, Vector3 pole, out Vector3 E, out Vector3 W)
        {
            Vector3 u = target - S;
            float dl = u.magnitude;
            u = dl > 1e-5f ? u / dl : Vector3.down;
            float d = Mathf.Clamp(dl - HandReach, 0.06f, (UpperLen + ForeLen) * 0.995f);
            W = S + u * d;
            float a = (UpperLen * UpperLen - ForeLen * ForeLen + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, UpperLen * UpperLen - a * a));
            Vector3 pp = pole - u * Vector3.Dot(pole, u);
            pp = pp.sqrMagnitude < 1e-8f ? Vector3.down : pp.normalized;
            E = S + u * a + pp * h;
        }

        // Звено от a к b: модель вытянута по −Y, +Z — по подсказке.
        static Quaternion SegRot(Vector3 a, Vector3 b, Vector3 fwdHint)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-10f) return Quaternion.identity;
            d.Normalize();
            Vector3 f = fwdHint - d * Vector3.Dot(fwdHint, d);
            if (f.sqrMagnitude < 1e-6f)
            {
                f = Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.forward;
                f -= d * Vector3.Dot(f, d);
            }
            return Quaternion.LookRotation(f.normalized, -d);
        }

        // ---- Формы (см) ---------------------------------------------------------------------------

        // Скруглённая коробка: полуразмеры b (со скруглением), радиус r.
        static float RBox(float x, float y, float z, float bx, float by, float bz, float r)
        {
            float qx = Mathf.Abs(x) - bx + r, qy = Mathf.Abs(y) - by + r, qz = Mathf.Abs(z) - bz + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f), oz = Mathf.Max(qz, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy + oz * oz) + Mathf.Min(Mathf.Max(qx, Mathf.Max(qy, qz)), 0f) - r;
        }

        static bool Stripe(float x) => Mathf.Repeat(x + 1.25f, 5f) < 2.5f;

        // Голова: начало — верх шеи, лицо в +Z. Простая, кубическая, с проседью; лоб открыт (волосы не дальше z 6.5).
        static float HeadShape(float x, float y, float z)
        {
            float ax = Mathf.Abs(x);
            float d = RBox(x, y - 11f, z - 0.5f, 7.6f, 9.8f, 8.6f, 3.0f);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 3.5f, z - 3f, 5.6f, 3.5f, 5.4f), 2f);       // подбородок
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 9.8f, z - 9.4f, 1.3f, 2.3f, 1.6f), 0.8f);   // нос
            d = VoxModel.Smin(d, VoxModel.Ell(ax - 7.9f, y - 10.5f, z + 0.2f, 1.3f, 2.4f, 1.7f), 0.8f);   // уши
            d = Mathf.Min(d, HairShape(x, y, z));
            return Mathf.Min(d, VoxModel.Cap(x, y, z, 0f, -2f, -0.5f, 0f, 4f, -0.5f, 4.8f));   // шея
        }

        static float HairShape(float x, float y, float z) =>
            Mathf.Max(Mathf.Max(VoxModel.Ell(x, y - 14.8f, z + 0.4f, 8.5f, 8.0f, 9.6f), Mathf.Min(13f - y, z + 3f)), z - 6.5f);

        static byte HeadPaint(float x, float y, float z)
        {
            float ax = Mathf.Abs(x);
            if (y > 6f && HairShape(x, y, z) < 0.5f) return 2;
            if (z > 6f)
            {
                if (Mathf.Abs(ax - 3.1f) < 0.95f && Mathf.Abs(y - 12.3f) < 0.75f) return 3;   // глаза
                if (Mathf.Abs(ax - 3.1f) < 1.7f && y > 13.9f && y < 14.9f) return 2;         // брови
                if (ax < 2.3f && Mathf.Abs(y - 5.6f) < 0.5f) return 4;                        // рот
            }
            return 1;
        }

        Mesh BuildHead()
        {
            var mn = new Vector3(-10f, -4f, -10.5f); var mx = new Vector3(10f, 24f, 12f);
            const float cell = 0.6f;
            var m = VoxModel.FromSdf(mn, mx, cell, HeadShape, HeadPaint);
            return m.BuildMesh("refHead", new[] { default, CSkin, CHair, CEye, CMouth }, mn * 0.01f, cell * 0.01f);
        }

        // Торс: начало — центр таза. Рубашка в вертикальную полоску, чёрный воротник, бабочка, шея.
        static float Bow(float x, float y, float z)
        {
            float d = VoxModel.Ell(x - 3.0f, y - 42.5f, z - 9.1f, 2.8f, 2.0f, 1.3f);
            d = Mathf.Min(d, VoxModel.Ell(x + 3.0f, y - 42.5f, z - 9.1f, 2.8f, 2.0f, 1.3f));
            return Mathf.Min(d, VoxModel.Ell(x, y - 42.5f, z - 9.6f, 1.1f, 1.3f, 1.2f));
        }

        static float Collar(float x, float y, float z) => VoxModel.Cap(x, y, z, 0f, 42f, 0.3f, 0f, 47.5f, 0.6f, 6.2f, 5.8f);

        static float TorsoShape(float x, float y, float z)
        {
            float ax = Mathf.Abs(x);
            float d = VoxModel.Ell(x, y - 24f, z, 15.5f, 22f, 10.5f);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 6f, z, 14.5f, 10f, 10.2f), 4f);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 37f, z + 0.5f, 17.5f, 9.5f, 10.8f), 5f);
            d = VoxModel.Smin(d, VoxModel.Ell(ax - 15.5f, y - 40f, z, 6.5f, 6f, 6.8f), 3f);
            d = Mathf.Max(d, -2f - y);
            float neck = VoxModel.Cap(x, y, z, 0f, 44f, 0.3f, 0f, 57f, 1.0f, 5.0f, 4.8f);
            d = Mathf.Min(d, VoxModel.Smin(Collar(x, y, z), neck, 1.5f));
            return Mathf.Min(d, Bow(x, y, z));
        }

        static byte TorsoPaint(float x, float y, float z)
        {
            if (Bow(x, y, z) < 0.45f) return 4;
            if (y > 47.8f) return 3;
            if (y > 43.5f && Collar(x, y, z) < 0.45f) return 2;
            if (y > 46f) return 3;
            return Stripe(x) ? (byte)2 : (byte)1;
        }

        Mesh BuildTorso()
        {
            var mn = new Vector3(-25f, -3f, -12.5f); var mx = new Vector3(25f, 58f, 12.5f);
            const float cell = 0.8f;
            var m = VoxModel.FromSdf(mn, mx, cell, TorsoShape, TorsoPaint);
            // ткань гладкая: затенение складок слабее, иначе на белых полосах рисуются ступени
            return m.BuildMesh("refTorso", new[] { default, CWhite, CBlack, CSkin, CBow }, mn * 0.01f, cell * 0.01f, 0.3f,
                               new[] { 1f, 0.4f, 0.4f, 1f, 0.5f });
        }

        // Таз в брюках с ремнём и пряжкой: начало — центр таза.
        static float HipsShape(float x, float y, float z) =>
            Mathf.Max(VoxModel.Ell(x, y + 5f, z, 15.5f, 12f, 11f), y - 4f);

        static byte HipsPaint(float x, float y, float z)
        {
            if (y > 0.5f)
                return Mathf.Abs(x) < 2.2f && y > 1f && y < 3.5f && z > 8f ? (byte)3 : (byte)2;
            return 1;
        }

        Mesh BuildHips()
        {
            var mn = new Vector3(-18f, -20f, -13f); var mx = new Vector3(18f, 5f, 13f);
            const float cell = 0.8f;
            var m = VoxModel.FromSdf(mn, mx, cell, HipsShape, HipsPaint);
            return m.BuildMesh("refHips", new[] { default, CPants, CBelt, CSilver }, mn * 0.01f, cell * 0.01f, 0.3f,
                               new[] { 1f, 0.5f, 0.5f, 0.5f });
        }

        // Плечо: начало в плечевом суставе, вдоль −Y. Короткий полосатый рукав, дальше кожа.
        static float Sleeve(float x, float y, float z) =>
            Mathf.Max(VoxModel.Cap(x, y, z, 0f, 1f, 0f, 0f, -14f, 0f, 5.6f, 5.0f), -14.5f - y);

        static float UpperShape(float x, float y, float z)
        {
            float d = VoxModel.Cap(x, y, z, 0f, 0f, 0f, 0f, -29f, 0f, 4.2f, 3.4f);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y + 1f, z, 5.4f, 5.2f, 5.4f), 2f);
            return Mathf.Min(d, Sleeve(x, y, z));
        }

        static byte UpperPaint(float x, float y, float z)
        {
            if (y > -14.6f && Sleeve(x, y, z) < 0.5f)
                return y < -13.4f ? (byte)3 : Stripe(x) ? (byte)3 : (byte)2;
            return 1;
        }

        Mesh BuildUpperArm()
        {
            var mn = new Vector3(-7f, -33f, -7f); var mx = new Vector3(7f, 7f, 7f);
            const float cell = 0.7f;
            var m = VoxModel.FromSdf(mn, mx, cell, UpperShape, UpperPaint);
            return m.BuildMesh("refUpperArm", new[] { default, CSkin, CWhite, CBlack }, mn * 0.01f, cell * 0.01f, 0.3f,
                               new[] { 1f, 1f, 0.4f, 0.4f });
        }

        static float ForeShape(float x, float y, float z) =>
            VoxModel.Smin(VoxModel.Cap(x, y, z, 0f, 0f, 0f, 0f, -26f, 0f, 3.5f, 2.6f),
                          VoxModel.Ell(x, y + 7f, z, 3.9f, 7f, 3.6f), 2f);

        Mesh BuildForeArm()
        {
            var mn = new Vector3(-5f, -30f, -5f); var mx = new Vector3(5f, 4.5f, 5f);
            const float cell = 0.6f;
            var m = VoxModel.FromSdf(mn, mx, cell, ForeShape, (x, y, z) => 1);
            return m.BuildMesh("refForeArm", new[] { default, CSkin }, mn * 0.01f, cell * 0.01f);
        }

        // Кисть: начало в запястье, пальцы вдоль −Y, ладонь к −s·X (внутрь у опущенной руки), большой палец к +Z.
        // grip — пальцы согнуты кольцом вокруг чужого запястья.
        static float HandShape(float x, float y, float z, float s, bool grip)
        {
            float px = x * s;   // в зеркальных осях ладонь всегда к −px
            float d = RBox(px, y + 5f, z, 1.5f, 4.6f, 4.0f, 1.2f);
            d = VoxModel.Smin(d, VoxModel.Cap(px, y, z, 0f, 1f, 0f, 0f, -1.5f, 0f, 2.6f), 1f);   // запястье
            for (int f = 0; f < 4; f++)
            {
                float fz = 2.7f - 1.8f * f;
                float len = f == 0 ? 6.4f : f == 1 ? 7.0f : f == 2 ? 6.6f : 5.2f;
                float fd;
                if (grip)
                {
                    fd = VoxModel.Cap(px, y, z, 0f, -9f, fz, -2.6f, -9f - len * 0.42f, fz, 0.95f);
                    fd = Mathf.Min(fd, VoxModel.Cap(px, y, z, -2.6f, -9f - len * 0.42f, fz, -4.6f, -9.6f, fz, 0.9f));
                }
                else fd = VoxModel.Cap(px, y, z, 0f, -9f, fz * 0.95f, -0.4f, -9f - len, fz * 1.05f, 0.95f, 0.8f);
                d = VoxModel.Smin(d, fd, 0.6f);
            }
            float th = grip ? VoxModel.Cap(px, y, z, -0.6f, -2.5f, 3.4f, -3.4f, -6.8f, 4.2f, 1.15f, 1.0f)
                            : VoxModel.Cap(px, y, z, -0.6f, -2.5f, 3.4f, -1.8f, -7.2f, 6.2f, 1.15f, 1.0f);
            return VoxModel.Smin(d, th, 0.8f);
        }

        Mesh BuildHand(float s, bool grip)
        {
            var mn = new Vector3(-7f, -18f, -5.5f); var mx = new Vector3(7f, 3.5f, 8.5f);
            const float cell = 0.5f;
            var m = VoxModel.FromSdf(mn, mx, cell, (x, y, z) => HandShape(x, y, z, s, grip), (x, y, z) => 1);
            return m.BuildMesh(grip ? "refHandGrip" : "refHand", new[] { default, CSkin }, mn * 0.01f, cell * 0.01f);
        }

        // Бедро и голень в брюках: начало в суставе, вдоль −Y.
        Mesh BuildLeg(float lenCm, float r0, float r1, string name)
        {
            var mn = new Vector3(-r0 - 1.5f, -lenCm - r1 - 1.5f, -r0 - 1.5f);
            var mx = new Vector3(r0 + 1.5f, r0 * 0.6f, r0 + 1.5f);
            const float cell = 0.8f;
            var m = VoxModel.FromSdf(mn, mx, cell,
                (x, y, z) => VoxModel.Cap(x, y, z, 0f, 0f, 0f, 0f, -lenCm, 0f, r0, r1), (x, y, z) => 1);
            return m.BuildMesh(name, new[] { default, CPants }, mn * 0.01f, cell * 0.01f, 0.3f, new[] { 1f, 0.5f });
        }

        // Ботинок: начало в лодыжке, носок в +Z, подошва на 6 см ниже.
        static float ShoeShape(float x, float y, float z)
        {
            float d = VoxModel.Ell(x, y + 3f, z - 4f, 4.6f, 3.6f, 12.5f);
            d = VoxModel.Smin(d, VoxModel.Cap(x, y, z, 0f, 1.5f, 0f, 0f, -3f, -0.5f, 4.4f), 2f);
            return Mathf.Max(d, -6f - y);
        }

        Mesh BuildShoe()
        {
            var mn = new Vector3(-6f, -6.5f, -10f); var mx = new Vector3(6f, 4f, 18f);
            const float cell = 0.6f;
            var m = VoxModel.FromSdf(mn, mx, cell, ShoeShape, (x, y, z) => y < -5f ? (byte)2 : (byte)1);
            return m.BuildMesh("refShoe", new[] { default, CShoe, CSole }, mn * 0.01f, cell * 0.01f);
        }

        // ---- Dispose ------------------------------------------------------------------------------

        public void Dispose()
        {
            if (_meshes != null) foreach (var m in _meshes) GameUtil.Destroy(m);
            _meshes = null;
            if (_root != null) GameUtil.Destroy(_root.gameObject);
            _root = null;
        }
    }
}
