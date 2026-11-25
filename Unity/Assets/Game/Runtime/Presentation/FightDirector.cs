using System;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using VoxProbe;

namespace VoxGame
{
    // Главный MonoBehaviour боя: строит сцену, тикает все системы, читает ввод.
    // Никакой логики в Awake/Start — только через Init().
    //
    // Оси боя (мир): соперник стоит в начале координат лицом в +Z, игрок смотрит на него из точки _eye в сторону −Z.
    // Правая сторона экрана — мир −X. Анатомическая голова стоит без поворота: AnatView.Pick переводит луч в оси головы
    // только сдвигом, поэтому наклон головы вместе с корпусом учитывается здесь (луч поворачивается в оси головы).
    public class FightDirector : MonoBehaviour
    {
        // ---- Публичные свойства --------------------------------------------

        public FightState  State      => _sim?.State;
        public FighterDef  Foe        => _foeDef;
        public bool        Finished   { get; private set; }
        public bool        PlayerWon  { get; private set; }
        public AnatView    Head       => _v;
        public FightHUD    Hud        => _hud;
        public RefereeLogic Referee   => _referee;
        public int         HitsThrown => _hitsTotal;    // ударов игрока
        public int         HitsLanded => _hitsLanded;   // из них попало (по логике боя)
        public int         HeadHits   { get; private set; }   // ударов, дошедших до головы (Pick попал)
        public int         FoeAttackSide => _strikeCmd.side;

        // Замер кадра удара: весь Tick в кадре, где был v.Hit, и его части (мс).
        public float lastHitFrameMs, maxHitFrameMs;
        public float lastPickMs, lastHitMs, lastAnatTickMs, lastLayerMs;
        public int   hitFrameSeq; // растёт на каждый Tick с ударом по голове
        // Последний удар по голове (для повторения тех же ударов на отдельной голове в замере) и игровое время кадра.
        public Ray    lastHeadRay;
        public float3 lastHitPoint, lastHitDir;
        public int    lastHitStrength;
        public float  lastSdt;

        // Точки расширения для FxDirector (W5).
        public event Action<HitResult> OnFoeHit;
        public event Action            OnPlayerHit;
        public event Action            OnKnockdown;
        public event Action            OnKnockout;

        // ---- Постановка сцены ----------------------------------------------

        // Глаза игрока: на высоте головы соперника (её начало — уровень ушей), в EyeDist от его оси.
        const float EyeDist = 1.20f;
        Vector3 _eye = new Vector3(0f, 1.62f, EyeDist);
        const float PortPitch = 14.5f, LandPitch = 9.5f;                    // наклон взгляда вниз, градусы
        // Красный угол ринга виден за соперником справа: в 3 м от игрока, 9.5° от оси взгляда;
        // диагональ ринга повёрнута к оси взгляда на 12°.
        const float CornerDist = 3.0f, CornerOff = 9.5f, CornerDiag = 12f;
        static readonly Vector3 LightForward = new Vector3(0.30f, -0.75f, -0.60f);

        // ---- Поля ----------------------------------------------------------

        FighterDef   _foeDef;
        PlayerProfile _profile;
        Camera        _cam;
        UIRoot        _uiRoot;

        RingBuilder   _ring;
        FoeBody       _foeBody;
        AnatView      _v;
        PlayerGloves  _playerGloves;
        FightSim      _sim;
        FightAI       _ai;
        FightStyle    _aiStyle;
        RefereeLogic  _referee;
        InputReader   _inputReader;
        FightHUD      _hud;
        ResultScreen  _result;

        Material      _anatMat;
        Material      _voxMat;
        Transform     _sceneRoot;

        // Таймеры эффектов
        float _freezeTimer, _slowTimer, _slowSpeed = 0.28f;

        // Удар соперника
        bool      _strikePending;
        float     _strikeTimer;
        AICommand _strikeCmd;
        FightPhase _prevAIPhase;

        // Камера
        int   _viewW, _viewH;       // размер окна вывода; 0 — брать Screen
        float _camShakeAmt;
        float _camShakeDecay = 10f;
        float _camFollow;           // 0 — взгляд по стойке, 1 — за головой упавшего соперника
        float _camDodge;            // сглаженный уход камеры при уклоне

        // Статистика
        float _fightTime;
        int   _hitsTotal, _hitsLanded;
        bool  _resultShown;
        int   _restartCount;
        bool  _blockFromButton;
        uint  _aimSeq;              // разброс точки прицеливания

        // История ударов для ComputeReward
        readonly HitResult[] _hitHistory = new HitResult[512];
        int _hitCount;

        // Режим игры (для расчёта награды)
        ModeManager _modeManager;

        // Замер кадра удара
        readonly Stopwatch _tickSw = new Stopwatch();
        bool _hitThisFrame;
        double _pickMs, _hitMs;

        // HUD данные (кэш без аллокаций)
        HudData _hudData;

        // ---- Инициализация -------------------------------------------------

        // Строит сцену, головы и соперника. Вызывается создателем после AddComponent.
        // foeHpMult / foeStunMult — запас здоровья и стойкость соперника (1 — по уровню; меньше — быстрее нокдаун и нокаут).
        public void Init(FighterDef foe, PlayerProfile profile, Camera cam, UIRoot ui,
                         ModeManager modeManager = null, float foeHpMult = 1f, float foeStunMult = 1f)
        {
            _foeDef      = foe;
            _profile     = profile;
            _cam         = cam;
            _uiRoot      = ui;
            _modeManager = modeManager;

            // Материалы
            var anatShader = Shader.Find("VoxProbe/Anatomy");
            _anatMat = new Material(anatShader != null ? anatShader
                                                       : Shader.Find("Hidden/InternalErrorShader"));
            var voxShader = Shader.Find("VoxGame/VertexColor");
            _voxMat = new Material(voxShader != null ? voxShader
                                                     : Shader.Find("Hidden/InternalErrorShader"));

            // Корень сцены
            var sg = new GameObject("FightScene");
            sg.transform.SetParent(transform, false);
            _sceneRoot = sg.transform;

            // Соперник (тело + крепление головы); глаза игрока — на высоте его головы в стойке
            _foeBody = new FoeBody(_sceneRoot, foe, _voxMat);
            _eye.y = _foeBody.headMount.position.y;

            // Ринг: красный угол — за соперником справа от него на экране
            float off = CornerOff * Mathf.Deg2Rad, diag = CornerDiag * Mathf.Deg2Rad;
            var corner = new Vector3(_eye.x - Mathf.Sin(off) * CornerDist, 0f, _eye.z - Mathf.Cos(off) * CornerDist);
            float cornerR = RingBuilder.RopeHalf * 1.41421356f;
            var ringCenter = corner + new Vector3(Mathf.Sin(diag), 0f, Mathf.Cos(diag)) * cornerR;
            _ring = new RingBuilder();
            _ring.Build(_sceneRoot, ringCenter, 45f + CornerDiag, LightForward);

            // Анатомия головы (строится здесь как «экран загрузки»)
            AnatSkin.on = true;
            AnatSkin.Reset();
            _v = new AnatView(_sceneRoot, _foeBody.headMount.position, _anatMat);

            // Руки игрока
            var glove = GlovesCatalog.Get(profile.equippedGloveId);
            _playerGloves = new PlayerGloves(cam.transform, GloveLook.FromEntry(glove), _voxMat);

            // Параметры боя из профиля
            var pcs = profile.GetCombatStats();
            var prms = new FightParams
            {
                dmgMult         = pcs.dmgMult,
                speedMult       = pcs.speedMult,
                staminaCostMult = pcs.staminaCostMult,
                megaGainMult    = pcs.megaGainMult,
                defenseMult     = pcs.incomingDmgMult,
            };

            // Симулятор и ИИ
            _sim = new FightSim(foe.level, foe.seed, prms);
            ApplyFoeTuning(foeHpMult, foeStunMult);
            int styleIdx = Mathf.Clamp(foe.styleIdx, 0, FightAI.Styles.Length - 1);
            _aiStyle = FightAI.Styles[styleIdx];
            _ai = new FightAI(_aiStyle, foe.level, foe.seed);
            _referee = new RefereeLogic();

            // Ввод
            _inputReader = GetComponent<InputReader>() ?? gameObject.AddComponent<InputReader>();

            // UI: FightHUD и ResultScreen сами регистрируются в UIRoot через Init
            var hudGO = new GameObject("FightHUD");
            _hud = hudGO.AddComponent<FightHUD>();
            _hud.Init(_uiRoot);
            _hud.OnBlock       += () => _blockFromButton = !_blockFromButton;
            _hud.OnDodgeLeft   += () => DoDodge(-1);
            _hud.OnDodgeRight  += () => DoDodge(1);

            var resultGO = new GameObject("ResultScreen");
            _result = resultGO.AddComponent<ResultScreen>();
            _result.Init(_uiRoot);
            _result.OnRematch += Restart;

            UpdateCamera(0f);

            // Строки HUD кэшируем один раз
            _hudData.foeName    = foe.name;
            _hudData.playerName = profile.playerName;
            _hudData.foeHpMax   = _sim.State.hpMax;
            _hudData.playerHpMax = 100f;

            _uiRoot.ShowScreen(ScreenId.Fight);
            _hud.ShowBanner("БОЙ!", 1.5f);
        }

        float _foeHpMult = 1f, _foeStunMult = 1f;

        void ApplyFoeTuning(float hpMult, float stunMult)
        {
            _foeHpMult = Mathf.Max(0.05f, hpMult); _foeStunMult = Mathf.Max(0.05f, stunMult);
            var st = _sim.State;
            st.hpMax *= _foeHpMult; st.hp = st.hpMax; st.stunMax *= _foeStunMult;
        }

        // Размер окна вывода, если он не равен Screen (снимки в текстуру). 0 — вернуться к Screen.
        public void SetViewSize(int w, int h)
        {
            _viewW = w; _viewH = h;
            _uiRoot?.SetViewSize(w, h);
            if (_cam != null) { UpdateCamera(0f); _playerGloves.Tick(0f); }
        }

        // ---- Tick / Update -------------------------------------------------

        void Update() => Tick(Time.unscaledDeltaTime);

        // Тик боя. realDt — реальное время кадра (Time.unscaledDeltaTime).
        public void Tick(float realDt)
        {
            if (_sim == null) return;

            // Замер полного кадра удара
            _hitThisFrame = false;
            _pickMs = _hitMs = 0;
            _tickSw.Restart();

            // Масштабированное игровое время
            float sdt;
            if (_freezeTimer > 0f) { _freezeTimer -= realDt; sdt = 0f; }
            else if (_slowTimer  > 0f) { _slowTimer  -= realDt; sdt = realDt * _slowSpeed; }
            else sdt = realDt;
            lastSdt = sdt;

            // Счётчик времени боя
            if (!Finished) _fightTime += sdt;

            // Ввод игрока (луч идёт в голову в её положении прошлого кадра)
            if (!Finished) ProcessInput();

            // ИИ: TickAI продвигает игровое время и восстановление; настоящий ИИ задаёт фазу
            _sim.TickAI(sdt);
            AICommand cmd = _ai.Tick(sdt, _sim.State);
            _sim.State.phase = cmd.newPhase;

            // Удар соперника
            ProcessFoeStrike(realDt, cmd);
            _prevAIPhase = cmd.newPhase;

            // Тело соперника; бьющая перчатка летит в лицо игроку, а при блоке — в его перчатки
            Vector3 strikeAt = _sim.State.playerBlocking
                ? _eye + new Vector3(0f, -0.13f, -0.36f)
                : _eye + new Vector3(-cmd.side * 0.04f, -0.06f, -0.16f);
            _foeBody.Tick(cmd, sdt, strikeAt);

            // Голова: стоит на креплении тела. Порядок кадра головы: AnatSkin.Step → v.Tick → AnatSkin.SetGlobals
            double t0 = _tickSw.Elapsed.TotalMilliseconds;
            _v.root.SetPositionAndRotation(_foeBody.headMount.position, _foeBody.headMount.rotation);
            AnatSkin.Step(sdt);
            _v.Tick(sdt);
            AnatSkin.SetGlobals(_v.root);
            double anatMs = _tickSw.Elapsed.TotalMilliseconds - t0;

            // Рефери (реальное время)
            _referee.Tick(realDt);
            while (_referee.TryDequeueEvent(out RefereeEvent re))
                HandleRefereeEvent(re);

            // Результаты после поднятия руки
            if (!_resultShown && _referee.State == RefereeState.RaiseHand)
                ShowResult();

            // Камера и руки игрока (руки — в осях камеры, поэтому после неё)
            UpdateCamera(realDt);
            _playerGloves.Tick(sdt);

            // HUD
            FillHudData();
            _hud.Refresh(_hudData);
            _hud.Tick(sdt, realDt);
            _uiRoot.Tick(sdt, realDt);
            _result.Tick(sdt, realDt);

            // Замер кадра удара
            _tickSw.Stop();
            if (_hitThisFrame)
            {
                lastHitFrameMs = (float)_tickSw.Elapsed.TotalMilliseconds;
                if (lastHitFrameMs > maxHitFrameMs) maxHitFrameMs = lastHitFrameMs;
                lastPickMs     = (float)_pickMs;
                lastHitMs      = (float)_hitMs;
                lastAnatTickMs = (float)anatMs;
                lastLayerMs    = lastHitFrameMs - lastPickMs - lastHitMs - lastAnatTickMs;
                hitFrameSeq++;
            }
        }

        // ---- Ввод игрока ---------------------------------------------------

        void ProcessInput()
        {
            var st = _sim.State;

            // Блок из жеста удержания низа или кнопки HUD
            st.playerBlocking = _inputReader.IsBlocking || _blockFromButton;
            _playerGloves.SetBlock(st.playerBlocking);

            while (_inputReader.Dequeue(out PunchInput p))
            {
                if (st.phase == FightPhase.KO) break;
                // лежачего и встающего не бьют
                if (st.phase == FightPhase.Down || st.phase == FightPhase.Rise) continue;

                // Мега: любой свайп при полной шкале
                p.isMega    = st.megaReady && p.type != PunchType.Jab;
                p.isFlurry  = false;

                int strength = p.type == PunchType.Jab ? 0 : (p.isMega ? 2 : 1);
                PunchLine(p.type, p.side, out Vector3 from, out Vector3 aim);

                // Луч в осях покоя головы: AnatView.Pick учитывает только сдвиг корня, поворот снимаем сами.
                // Pick шагает по 0,4 мм от входа в рамку головы (при изгибе шеи рамка шире на 15 см), поэтому луч
                // начинается за PickLead до точки прицеливания: та же первая ячейка, а шагов в разы меньше.
                Vector3 hp = _v.root.position;
                Quaternion hr = _v.root.rotation, inv = Quaternion.Inverse(hr);
                Vector3 dirW = (aim - from).normalized;
                Vector3 start = (aim - from).sqrMagnitude > PickLead * PickLead ? aim - dirW * PickLead : from;
                var ray = new Ray(hp + inv * (start - hp), inv * dirW);

                double t0 = _tickSw.Elapsed.TotalMilliseconds;
                bool hitPick = _v.Pick(ray, out float3 hitPt, out byte mat, out AnatGrid grid);
                double t1 = _tickSw.Elapsed.TotalMilliseconds;
                _pickMs += t1 - t0;
                int removed = 0;
                Vector3 contact = aim;
                if (hitPick)
                {
                    AnatSkin.Push(hitPt, _v.pickDir, strength);
                    removed = _v.Hit(hitPt, _v.pickDir, strength);
                    _hitMs += _tickSw.Elapsed.TotalMilliseconds - t1;
                    _hitThisFrame = true;
                    lastHeadRay = new Ray(ray.origin - hp, ray.direction);
                    lastHitPoint = hitPt; lastHitDir = _v.pickDir; lastHitStrength = strength;
                    HeadHits++;
                    contact = hp + hr * ((Vector3)hitPt * 0.01f);
                    _foeBody.AddImpulse(hr * (Vector3)_v.pickDir * (strength == 0 ? 0.25f : strength == 1 ? 0.6f : 1f));
                }

                bool counter = st.phase == FightPhase.Windup;
                bool open    = st.phase == FightPhase.Open;

                // Сила попадания для логики боя. Лёгкий и средний удар анатомия чаще не пробивает (синяк, отёк без
                // убранных ячеек), а пробой мегой убирает тысячи ячеек, поэтому основа — база по силе удара (урон как
                // в веб-версии), а убранные ячейки дают ограниченную добавку.
                float bse = ImpactBase[strength];
                int impact = hitPick ? Mathf.RoundToInt(bse * p.pw + Mathf.Min(removed, bse) * 0.5f) : 0;
                var result = _sim.ProcessHit(
                    impact, (int)_v.sim.lastZone, _v.sim.traumaAll,
                    _v.detachedBodies, strength, p.isMega, p.pw, counter, open);

                _hitsTotal++;
                if (result.hit)
                {
                    _hitsLanded++;
                    if (_hitCount < _hitHistory.Length) _hitHistory[_hitCount++] = result;
                }

                if (!result.hit)
                {
                    _ai.TriggerCounter();
                }
                else
                {
                    if (result.isKO)
                    {
                        _hud.ShowBanner("В НОКАУТ!", 3f);
                        _referee.OnKnockout(true);
                        PlayerWon = true;
                        Finished  = true;
                        TriggerHitStop(0f, 1.0f, 0.22f);
                        OnFoeHit?.Invoke(result);
                        OnKnockout?.Invoke();
                    }
                    else if (result.isKnockdown)
                    {
                        _hud.ShowBanner("НОКДАУН!", 2f);
                        int riseAt = 4 + (int)(_sim.State.knockdowns % 3);
                        _referee.OnKnockdown(riseAt);
                        TriggerHitStop(4f, 0.5f);
                        OnFoeHit?.Invoke(result);
                        OnKnockdown?.Invoke();
                    }
                    else
                    {
                        float shakeAmt = p.isMega ? 0.025f
                            : p.type == PunchType.Jab ? 0.008f : 0.015f;
                        _camShakeAmt = Mathf.Max(_camShakeAmt, shakeAmt);
                        if (p.isMega) TriggerHitStop(0f, 0.4f);
                        OnFoeHit?.Invoke(result);
                    }
                }

                float gloveDur = p.type == PunchType.Jab ? 0.085f : (p.isMega ? 0.4f : 0.2f);
                _playerGloves.Punch(p.side, p.isMega ? PunchType.Mega : p.type, gloveDur, contact);

                // Один удар за тик
                break;
            }
        }

        const float PickLead = 0.05f;                       // м: начало луча Pick перед точкой прицеливания
        static readonly float[] ImpactBase = { 10f, 28f, 28f }; // джеб, силовой, мега — в «ячейках» FightState.TakeHit

        // Линия удара: откуда летит перчатка и в какую точку головы целит (мир).
        // Точки заданы в осях головы (см от уровня ушей: +Y вверх, +Z — лицо); левая рука игрока бьёт в правую
        // от соперника половину лица (мир +X). Каждый удар чуть смещён, чтобы серия не шла в одну ячейку.
        void PunchLine(PunchType type, int side, out Vector3 from, out Vector3 aim)
        {
            _aimSeq = _aimSeq * 1664525u + 1013904223u;
            float jx = ((_aimSeq >> 8) & 0xFF) / 255f - 0.5f, jy = ((_aimSeq >> 16) & 0xFF) / 255f - 0.5f;
            float s = -side;   // сторона руки в мире: левая рука игрока — +X
            Vector3 local, start;
            switch (type)
            {
                case PunchType.Hook:   // сбоку в скулу или челюсть
                    local = new Vector3(s * 5.6f, -3.2f + jy * 4.5f, 5.0f + jx * 2f);
                    start = _eye + new Vector3(s * 0.34f, -0.06f, -0.38f);
                    break;
                case PunchType.Upper:  // снизу в подбородок
                    local = new Vector3(s * 1.4f + jx * 2f, -9.4f, 6.4f + jy * 1.5f);
                    start = _eye + new Vector3(s * 0.07f, -0.46f, -0.45f);
                    break;
                case PunchType.Mega:   // прямой в переносицу
                    local = new Vector3(jx * 2f, 0.5f + jy * 2f, 9f);
                    start = _eye + new Vector3(s * 0.04f, -0.04f, 0f);
                    break;
                default:               // джеб: нос, губы, глаз со стороны руки
                    local = new Vector3(s * 1.6f + jx * 3.2f, -2.6f + jy * 5.5f, 9f);
                    start = _eye + new Vector3(s * 0.05f, -0.05f, 0f);
                    break;
            }
            from = start;
            aim = _v.root.TransformPoint(local * 0.01f);
        }

        void DoDodge(int side)
        {
            _sim.State.playerDodge     = 1f;
            _sim.State.playerDodgeSide = side;
            _playerGloves.SetDodge(side);
        }

        // ---- Удар соперника ------------------------------------------------

        void ProcessFoeStrike(float realDt, AICommand cmd)
        {
            // Начало фазы Strike
            if (cmd.newPhase == FightPhase.Strike && _prevAIPhase != FightPhase.Strike)
            {
                _strikePending = true;
                _strikeTimer   = 0f;
                _strikeCmd     = cmd;
            }

            if (!_strikePending) return;
            _strikeTimer += realDt;
            if (_strikeTimer < 0.13f) return;
            _strikePending = false;

            var st = _sim.State;
            if (st.phase == FightPhase.KO || Finished) return;

            // Уклон в правильную сторону нейтрализует удар
            bool dodgeOk = st.playerDodge > 0.04f && st.playerDodgeSide != 0
                           && st.playerDodgeSide == -_strikeCmd.side;
            if (dodgeOk)
            {
                st.phase = FightPhase.Open;
                _ai.TriggerCounter();
                return;
            }

            float dmgBase = (9f + 1.6f * (_foeDef.level - 1)) * _aiStyle.d;
            if (_strikeCmd.isHook)    dmgBase *= 1.25f;
            if (_strikeCmd.isCounter) dmgBase *= 1.2f;
            dmgBase *= st.fightParams.defenseMult;

            if (st.playerBlocking)
            {
                st.playerStamina -= _aiStyle.bs;
                if (st.playerStamina < 0f)
                {
                    st.playerHp      -= dmgBase * 0.75f;
                    st.playerStamina  = 0f;
                }
                else
                {
                    st.playerHp -= dmgBase * _aiStyle.bc;
                }
            }
            else
            {
                st.playerHp -= dmgBase;
            }

            st.playerHp = Mathf.Max(0f, st.playerHp);
            st.OnPlayerHurt();
            _camShakeAmt = Mathf.Max(_camShakeAmt, 0.018f);
            OnPlayerHit?.Invoke();

            if (st.playerHp <= 0f && !Finished)
            {
                Finished  = true;
                PlayerWon = false;
                _referee.OnKnockout(false);
            }
        }

        // ---- Рефери --------------------------------------------------------

        void HandleRefereeEvent(RefereeEvent re)
        {
            switch (re.Type)
            {
                case RefereeEventType.CountTick:
                    _hud.ShowCount(re.Count);
                    break;
                case RefereeEventType.SayBox:
                    // Rise, не Guard: FightAI.SyncExternal(Rise) выдерживает RISE_DUR=1.9с перед EnterIdle.
                    _sim.State.phase = FightPhase.Rise;
                    _sim.State.groggy = Mathf.Max(0f, _sim.State.groggy * 0.5f);
                    _hud.ShowBanner("БОЙ!", 1.5f);
                    break;
                case RefereeEventType.FightStopped:
                    Finished = true;
                    break;
                case RefereeEventType.HandRaised:
                    ShowResult();
                    break;
            }
        }

        void ShowResult()
        {
            if (_resultShown) return;
            _resultShown = true;
            bool win = _referee.PlayerWon;
            float accuracy = _hitsTotal > 0 ? (float)_hitsLanded / _hitsTotal : 0f;

            int reward;
            if (win && _modeManager != null)
            {
                // Правильный расчёт через ModeManager: base 100 + KD×20 + точность%×0.5 + KO +50; Trial ×2
                var hist = new HitResult[_hitCount];
                System.Array.Copy(_hitHistory, hist, _hitCount);
                reward = _modeManager.ComputeReward(hist, _modeManager.CurrentMode);
            }
            else
            {
                reward = win ? 100 : 0;
            }

            _result.Show(win, _fightTime, _sim.State.knockdowns, accuracy, reward);
            _uiRoot.ShowScreen(ScreenId.Result);
        }

        // ---- Камера --------------------------------------------------------

        void UpdateCamera(float realDt)
        {
            float w = _viewW > 0 ? _viewW : Screen.width, h = _viewH > 0 ? _viewH : Screen.height;
            bool land = w > h;
            _cam.fieldOfView = GameUtil.CalcFov(w, h);
            _playerGloves.SetView(_cam.fieldOfView, h > 0f ? w / h : 0.46f);

            Vector3 eye = _eye;
            if (_camShakeAmt > 0.0001f)
            {
                float s = _camShakeAmt;
                eye += new Vector3(UnityEngine.Random.Range(-s, s), UnityEngine.Random.Range(-s, s), 0f);
                _camShakeAmt = Mathf.Max(0f, _camShakeAmt - _camShakeDecay * realDt);
            }
            else _camShakeAmt = 0f;

            // Уклон: игрок уходит в сторону (вправо на экране — мир −X)
            var st = _sim.State;
            float dodgeT = st.playerDodge > 0f ? -st.playerDodgeSide * 0.14f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(st.playerDodge)) : 0f;
            _camDodge = Mathf.Lerp(_camDodge, dodgeT, 1f - Mathf.Exp(-18f * realDt));
            eye.x += _camDodge;

            // Взгляд: по стойке, а за упавшим соперником — вниз, к его голове
            bool low = st.phase == FightPhase.Down || st.phase == FightPhase.KO;
            _camFollow = Mathf.Lerp(_camFollow, low ? 0.6f : 0f, 1f - Mathf.Exp(-3.5f * realDt));
            Quaternion stand = Quaternion.Euler(land ? LandPitch : PortPitch, 180f, _camDodge * -25f);
            Vector3 look = Vector3.Lerp(eye + stand * Vector3.forward * 1.1f, _foeBody.headMount.position, _camFollow);
            _cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, stand * Vector3.up));
        }

        // ---- HUD -----------------------------------------------------------

        void FillHudData()
        {
            var st = _sim.State;
            _hudData.playerHp       = st.playerHp;
            _hudData.foeHp          = st.hp;
            _hudData.playerStamina  = st.playerStamina;
            _hudData.megaMeter      = st.megaMeter;
            _hudData.megaReady      = st.megaReady;
            _hudData.fightTime      = _fightTime;
            _hudData.foeKnockdowns  = st.knockdowns;
            _hudData.foePhase       = st.phase;
            _hudData.foeAttackSide  = _strikeCmd.side;
            _hudData.telegraphAlpha = st.phase == FightPhase.Windup ? _aiStyle.hn : 0f;
            _hudData.playerBlocking = st.playerBlocking;
            // лежачему комбо не показываем: надпись висела бы над упавшим соперником
            _hudData.combo          = st.phase == FightPhase.Down || st.phase == FightPhase.KO ? 0 : st.comboCount;
        }

        // ---- Перезапуск ----------------------------------------------------

        public void Restart()
        {
            if (_v == null) return;
            _v.Restore();
            AnatSkin.Reset();
            float prevMega = _sim.State.megaMeter;
            var prms = _sim.State.fightParams;
            _sim = new FightSim(_foeDef.level, _foeDef.seed + (uint)(_restartCount + 1), prms);
            ApplyFoeTuning(_foeHpMult, _foeStunMult);
            _sim.CarryMegaMeter(prevMega);
            _ai  = new FightAI(_aiStyle, _foeDef.level, _foeDef.seed);
            _referee.Reset();

            Finished      = false;
            PlayerWon     = false;
            _resultShown  = false;
            _fightTime    = 0f;
            _hitsTotal    = 0;
            _hitsLanded   = 0;
            HeadHits      = 0;
            _hitCount     = 0;
            _blockFromButton = false;
            _strikePending   = false;
            _prevAIPhase     = FightPhase.Idle;
            lastHitFrameMs   = 0f;
            maxHitFrameMs    = 0f;
            _restartCount++;

            _uiRoot.ShowScreen(ScreenId.Fight);
            _hud.ShowBanner("БОЙ!", 1.5f);
        }

        // ---- Вспомогательные -----------------------------------------------

        void TriggerHitStop(float frames, float slowDur, float slowSpeed = 0.28f)
        {
            if (frames > 0f) _freezeTimer = Mathf.Max(_freezeTimer, frames / 60f);
            if (slowDur > 0f) { _slowTimer = Mathf.Max(_slowTimer, slowDur); _slowSpeed = slowSpeed; }
        }

        // ---- Dispose -------------------------------------------------------

        public void Dispose()
        {
            _ring?.Dispose();
            _foeBody?.Dispose();
            _v?.Dispose();
            _playerGloves?.Dispose();
            if (_hud       != null) GameUtil.Destroy(_hud.gameObject);
            if (_result    != null) GameUtil.Destroy(_result.gameObject);
            if (_sceneRoot != null) GameUtil.Destroy(_sceneRoot.gameObject);
            if (_anatMat   != null) GameUtil.Destroy(_anatMat);
            if (_voxMat    != null) GameUtil.Destroy(_voxMat);
            _ring         = null;
            _foeBody      = null;
            _v            = null;
            _playerGloves = null;
            _hud          = null;
            _result       = null;
            _sim          = null;
        }

        void OnDestroy() => Dispose();
    }
}
