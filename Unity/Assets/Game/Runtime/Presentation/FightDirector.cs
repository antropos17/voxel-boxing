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
        public RefereeView RefereeFigure => _refView;
        public CrowdView   Crowd      => _crowd;
        public BeltView    Belt       => _belt;
        public VictoryScene Victory   => _victory;
        public PlayerGloves Gloves    => _playerGloves;
        public FoeBody     FoeFigure => _foeBody;
        public FoeFace     FoeMimic  => _face;     // мимика соперника (EMOTIONS.md)
        public bool        TitleFight => _titleFight;
        public int         HitsThrown => _hitsTotal;    // ударов игрока
        public int         HitsLanded => _hitsLanded;   // из них попало (по логике боя)
        public int         HeadHits   { get; private set; }   // ударов, дошедших до головы (Pick попал)
        public int         FoeAttackSide => _strikeCmd.side;

        public FxDirector  Fx         => _fx;
        public SoundPlayer Sound      => _sound;
        public ResultScreen Result    => _result;

        // Подключение к GameApp: урон соперника (тренировка — меньше 1; ставить до Init или между боями)
        // и сигнал «экран итогов показан» (вызывается внутри Tick — обработчик только ставит флаг).
        public float FoeDamageMult = 1f;
        public Action OnResultShown;
        // Здоровье игрока в начале боя (испытание переносит остаток) и подсказка жестов в начале боя.
        public float PlayerStartHp = 100f;
        public bool  ShowControlsHint;

        // Замер кадра удара: весь Tick в кадре, где был v.Hit, и его части (мс).
        public float lastHitFrameMs, maxHitFrameMs;
        public float lastPickMs, lastHitMs, lastAnatTickMs, lastLayerMs;
        // Части слоя в том же кадре: толпа, фигура рефери, накладки головы, эффекты, звук (мс)
        public float lastCrowdMs, lastRefMs, lastGearMs, lastFxMs, lastSoundMs;
        public float lastFxTickMs;   // время эффектов в последнем кадре (любом, не только кадре удара)
        // Замер аллокаций (включается снаружи): байты за Tick всего и в вызовах анатомии.
        // Счётчик — ProfilerRecorder «GC Allocated In Frame» (в Mono GC.GetAllocatedBytesForCurrentThread всегда 0).
        public static bool MeasureAlloc;
        public static Unity.Profiling.ProfilerRecorder AllocRec;
        static long AllocNow() => AllocRec.Valid ? AllocRec.CurrentValue : 0;
        public long allocTotal, allocAnat;
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
        // 1.40 м: в портрете голова ниже полос HUD, фигура видна до середины бедра над кнопками.
        const float EyeDist = 1.40f;
        Vector3 _eye = new Vector3(0f, 1.62f, EyeDist);
        const float PortPitch = 13f, LandPitch = 9.5f;                      // наклон взгляда вниз, градусы
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
        RefereeView   _refView;
        CrowdView     _crowd;
        BeltView      _belt;
        VictoryScene  _victory;
        FoeFace       _face;
        bool          _titleFight;
        static readonly Color BeltStrap = new Color(0.50f, 0.05f, 0.07f);   // тёмно-красная кожа ремня

        Material      _anatMat;
        Material      _voxMat;
        Transform     _sceneRoot;

        // Эффекты (стоп-кадр, слоу-мо, камера, частицы, надписи) и звук
        FxDirector    _fx;
        SoundPlayer   _sound;
        bool          _prevMegaReady;
        bool          _headBurstShown;   // разрыв головы показывается один раз за бой
        bool          _finaleNext;       // нокаут ударом игрока: развязка (HUD прочь, постановщик) — со следующего кадра,
                                         // чтобы не утяжелять и без того самый дорогой кадр удара
        float         _finale;           // 0..1: камера развязки (взгляд выше, без пола у камеры)

        // Удар соперника
        bool      _strikePending;
        float     _strikeTimer;
        AICommand _strikeCmd;
        FightPhase _prevAIPhase;

        // Камера
        int   _viewW, _viewH;       // размер окна вывода; 0 — брать Screen
        float _camFollow;          // 0 — взгляд по стойке, 1 — за головой упавшего соперника
        float _camDodge;            // сглаженный уход камеры при уклоне

        // Статистика
        float _fightTime;
        int   _hitsTotal, _hitsLanded;
        bool  _resultShown;
        int   _restartCount;
        bool  _blockFromButton;
        float _punchCd;             // перезарядка удара игрока, с реального времени
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
        // ladderIndex / ladderCount — место боя в лестнице: последний бой титульный, после победы выносят пояс.
        // forceBelt — пояс после победы в любом бою (сценарии снимков).
        public void Init(FighterDef foe, PlayerProfile profile, Camera cam, UIRoot ui,
                         ModeManager modeManager = null, float foeHpMult = 1f, float foeStunMult = 1f,
                         int ladderIndex = -1, int ladderCount = 0, bool forceBelt = false)
        {
            _titleFight = forceBelt || VictoryScene.IsTitleFight(ladderIndex, ladderCount);
            _foeDef      = foe;
            _profile     = profile;
            _cam         = cam;
            _uiRoot      = ui;
            _modeManager = modeManager;

            // Материалы
            _anatMat = new Material(GameUtil.FindShader("VoxProbe/Anatomy", GameUtil.AnatMaterial));
            _voxMat = new Material(GameUtil.FindShader("VoxGame/VertexColor", GameUtil.VoxMaterial));

            // Корень сцены
            var sg = new GameObject("FightScene");
            sg.transform.SetParent(transform, false);
            _sceneRoot = sg.transform;

            // Соперник (тело + крепление головы); глаза игрока — на высоте его головы в стойке
            _foeBody = new FoeBody(_sceneRoot, foe, _voxMat, true);
            _eye.y = _foeBody.headMount.position.y;

            // Ринг: красный угол — за соперником справа от него на экране
            float off = CornerOff * Mathf.Deg2Rad, diag = CornerDiag * Mathf.Deg2Rad;
            var corner = new Vector3(_eye.x - Mathf.Sin(off) * CornerDist, 0f, _eye.z - Mathf.Cos(off) * CornerDist);
            float cornerR = RingBuilder.RopeHalf * 1.41421356f;
            var ringCenter = corner + new Vector3(Mathf.Sin(diag), 0f, Mathf.Cos(diag)) * cornerR;
            _ring = new RingBuilder();
            _ring.Build(_sceneRoot, ringCenter, 45f + CornerDiag, LightForward);
            _foeBody.SetArena(ringCenter, 45f + CornerDiag, RingBuilder.RopeHalf);   // падение не выходит за канаты
            _crowd = new CrowdView();
            _crowd.Build(_sceneRoot, CrowdStands.Default(ringCenter, 45f + CornerDiag));

            // Анатомия головы (строится здесь как «экран загрузки»)
            AnatSkin.on = true;
            AnatSkin.Reset();
            _v = new AnatView(_sceneRoot, _foeBody.headMount.position, _anatMat, FighterGen.AnatLookOf(foe));   // тон кожи и причёска бойца
            // оторванные куски живут в рамке ринга: падают на настил, упираются в канаты, не следуют за головой
            var anatWorld = new GameObject("anatWorld").transform;
            anatWorld.SetParent(_sceneRoot, false);
            anatWorld.SetPositionAndRotation(new Vector3(ringCenter.x, _foeBody.root.position.y, ringCenter.z), Quaternion.Euler(0f, 45f + CornerDiag, 0f));
            _v.pieces.space = anatWorld; _v.pieces.spaceHalf = RingBuilder.RopeHalf * 100f - 4f;
            _face = new FoeFace(foe);   // мимика: характер бойца, события боя → выражения

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
                // режим без мега-удара (испытание): шкала не копится
                megaGainMult    = MegaAllowed ? pcs.megaGainMult : 0f,
                defenseMult     = pcs.incomingDmgMult,
            };

            // Симулятор и ИИ
            _sim = new FightSim(foe.level, foe.seed, prms);
            ApplyFoeTuning(foeHpMult, foeStunMult);
            _sim.State.playerHp = Mathf.Clamp(PlayerStartHp, 1f, 100f);
            int styleIdx = Mathf.Clamp(foe.styleIdx, 0, FightAI.Styles.Length - 1);
            _aiStyle = FightAI.Styles[styleIdx];
            _ai = new FightAI(_aiStyle, foe.level, foe.seed);
            _referee = new RefereeLogic();

            // Ввод
            _inputReader = GetComponent<InputReader>() ?? gameObject.AddComponent<InputReader>();
            _inputReader.Init();

            // UI: FightHUD и ResultScreen сами регистрируются в UIRoot через Init
            var hudGO = new GameObject("FightHUD");
            _hud = hudGO.AddComponent<FightHUD>();
            _hud.Init(_uiRoot);
            _hud.SetMegaVisible(MegaAllowed);
            _hud.OnBlock       += () => _blockFromButton = !_blockFromButton;
            _hud.OnDodgeLeft   += () => DoDodge(-1);
            _hud.OnDodgeRight  += () => DoDodge(1);

            var resultGO = new GameObject("ResultScreen");
            _result = resultGO.AddComponent<ResultScreen>();
            _result.Init(_uiRoot);
            _result.OnRematch += () => { _sound.Play(SoundId.Ui); Restart(); };

            // Эффекты поверх экранов (последний ребёнок холста) и звук; слушатель — на камере боя
            _fx = new FxDirector();
            _fx.Build(_sceneRoot, _uiRoot.transform, cam);
            _fx.ArmGhost = (int g, int part, out Matrix4x4 m) => { m = default; return _playerGloves != null && _playerGloves.RecoilGhost(g, part, out m); };   // плашка обходит руку и в отдаче
            _sound = new SoundPlayer();
            _sound.Init(transform);
            _sound.Mute(!profile.soundEnabled);
            _sound.StartLoop();
            _sound.Play(SoundId.Bell);
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            // щелчок кнопки паузы озвучивает GameApp звуком меню: звук боя на паузе глушится в том же нажатии

            // Пояс перед камерой и постановщик развязки
            _belt = new BeltView();
            _belt.Build(cam.transform, _voxMat, BeltStrap);
            _victory = new VictoryScene(_referee, _belt)
            {
                SetCrowdMood = m => _crowd.SetMood(m),
                OnHandRaised = OnHandRaised,
                OnBelt       = OnBeltShown,
                OnReady      = ShowResult,
            };

            UpdateCamera(0f);

            // Рефери: место в покое зависит от камеры, поэтому после неё
            _refView = new RefereeView();
            _refView.Build(_sceneRoot, _voxMat, _referee);
            _refView.SetView(_viewW > 0 && _viewH > 0 ? (float)_viewW / _viewH : (float)Screen.width / Mathf.Max(1, Screen.height));
            _refView.SetScene(_foeBody.root, _foeBody.headMount, cam.transform);

            // Строки HUD кэшируем один раз
            _hudData.foeName    = foe.name;
            _hudData.playerName = profile.playerName;
            _hudData.foeHpMax   = _sim.State.hpMax;
            _hudData.playerHpMax = 100f;

            _uiRoot.ShowScreen(ScreenId.Fight);
            _hud.ShowBanner("БОЙ!", 1.5f);
            if (ShowControlsHint) _hud.ShowHint(MegaAllowed ? ControlsHint : ControlsHintNoMega, HintSeconds);
        }

        // Подсказка жестов (первые бои и тренировка): гаснет через HintSeconds или после HintPunches ударов.
        public const string ControlsHint =
            "Тап — джеб · свайп вбок — хук\nвверх — апперкот · вниз — сверху\nдержи низ экрана — блок\nшкала полна: любой свайп — МЕГА";
        public const string ControlsHintNoMega =
            "Тап — джеб · свайп вбок — хук\nвверх — апперкот · вниз — сверху\nдержи низ экрана — блок";
        const float HintSeconds = 8f;
        const int   HintPunches = 5;

        bool MegaAllowed => _modeManager == null || _modeManager.MegaAllowed;

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
            if (_cam != null)
            {
                UpdateCamera(0f);
                // место рефери в покое зависит от ориентации: при повороте экрана он встаёт туда сразу
                if (_refView != null && _referee.State == RefereeState.Idle) _refView.Snap();
                if (_refView != null) PoseFigures(0f, 0f); else _playerGloves.Tick(0f);
            }
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

            long alloc0 = MeasureAlloc ? AllocNow() : 0;
            _allocAnatFrame = 0;

            // Масштабированное игровое время: стоп-кадр и слоу-мо — только в FxDirector
            var st0 = _sim.State;
            double tf = _tickSw.Elapsed.TotalMilliseconds;
            _fx.SyncFoe(st0.hpMax > 0f ? st0.hp / st0.hpMax : 0f, st0.stunMax > 0f ? st0.stunAccum / st0.stunMax : 0f,
                        st0.knockdowns, st0.phase, _foeBody.headMount.position);
            SyncInjuries();
            float sdt = realDt * _fx.Tick(realDt);
            _fxMs = _tickSw.Elapsed.TotalMilliseconds - tf;
            lastSdt = sdt;
            if (st0.megaReady && !_prevMegaReady && !Finished) _sound.Play(SoundId.Ready);
            _prevMegaReady = st0.megaReady;

            // Счётчик времени боя
            if (!Finished) _fightTime += sdt;
            if (_finaleNext) { _finaleNext = false; BeginFinale(true); }

            // Ввод игрока (луч идёт в голову в её положении прошлого кадра); касания читаются здесь, не в своём Update
            _inputReader.Tick();
            if (_punchCd > 0f) _punchCd -= realDt;
            if (!Finished) ProcessInput();
            if (ShowControlsHint && _hitsTotal >= HintPunches) _hud.HideHint();

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
            long a0 = MeasureAlloc ? AllocNow() : 0;
            _v.root.SetPositionAndRotation(_foeBody.headMount.position, _foeBody.headMount.rotation);
            AnatSkin.Step(sdt);
            _v.Tick(sdt);
            AnatSkin.SetGlobals(_v.root);
            _face.Tick(realDt, _sim.State); _face.Apply(_v);   // мимика — в реальном времени (стоп-кадр удара её не замораживает)
            if (MeasureAlloc) _allocAnatFrame += AllocNow() - a0;
            double t1g = _tickSw.Elapsed.TotalMilliseconds;
            double anatMs = t1g - t0;
            _foeBody.gear.SyncToSkull();   // накладки головы — по позе черепа этого кадра
            _foeBody.gear.SetJaw(_face.JawAngle, _face.JawSide);   // нижняя часть бороды — с челюстью мимики
            double gearMs = _tickSw.Elapsed.TotalMilliseconds - t1g;

            // Рефери (реальное время) и развязка
            _referee.Tick(realDt);
            while (_referee.TryDequeueEvent(out RefereeEvent re))
                HandleRefereeEvent(re);
            _victory.Tick(realDt);

            // Камера, затем фигуры в осях камеры и сцены
            UpdateCamera(realDt);
            PoseFigures(sdt, realDt);

            // Зал: dt — игровое время (в стоп-кадре замирает вместе с боем)
            double tc = _tickSw.Elapsed.TotalMilliseconds;
            _crowd.Tick(sdt, realDt);
            double crowdMs = _tickSw.Elapsed.TotalMilliseconds - tc;

            // HUD
            FillHudData();
            _hud.Refresh(_hudData);
            _hud.Tick(sdt, realDt);
            _uiRoot.Tick(sdt, realDt);
            _result.Tick(sdt, realDt);

            double ts = _tickSw.Elapsed.TotalMilliseconds;
            _sound.Tick(realDt);
            double soundMs = _tickSw.Elapsed.TotalMilliseconds - ts;

            if (MeasureAlloc)
            {
                allocTotal += AllocNow() - alloc0;
                allocAnat  += _allocAnatFrame;
            }

            // Замер кадра удара
            _tickSw.Stop();
            lastFxTickMs = (float)_fxMs;
            if (_hitThisFrame)
            {
                lastHitFrameMs = (float)_tickSw.Elapsed.TotalMilliseconds;
                if (lastHitFrameMs > maxHitFrameMs) maxHitFrameMs = lastHitFrameMs;
                lastPickMs     = (float)_pickMs;
                lastHitMs      = (float)_hitMs;
                lastAnatTickMs = (float)anatMs;
                lastLayerMs    = lastHitFrameMs - lastPickMs - lastHitMs - lastAnatTickMs;
                lastCrowdMs    = (float)crowdMs;
                lastRefMs      = (float)_refPoseMs;
                lastGearMs     = (float)gearMs;
                lastFxMs       = (float)_fxMs;
                lastSoundMs    = (float)soundMs;
                hitFrameSeq++;
            }
        }

        double _refPoseMs, _fxMs;
        long   _allocAnatFrame;

        // Рефери, руки игрока и пояс. Рука победителя идёт за рукой рефери, поэтому после него.
        // Из SetViewSize зовётся с нулевым временем: только расставить по новой камере.
        void PoseFigures(float sdt, float udt)
        {
            double t0 = _tickSw.Elapsed.TotalMilliseconds;
            _refView.Tick(sdt, udt);
            _refPoseMs = _tickSw.Elapsed.TotalMilliseconds - t0;
            if (_refView.IsGripping)
            {
                if (_referee.PlayerWon) _playerGloves.RaiseVictory(RefereeView.PlayerGripSide, _refView.GripPoint());
                else _foeBody.HoldWrist(RefereeView.FoeGripArm, _refView.GripPoint());
            }
            // руки игрока живут в реальном времени: в стоп-кадре перчатка доходит до лица (кадр удара), а удар нокаута
            // не висит в кадре всё слоу-мо
            _playerGloves.Tick(udt);
            _belt.Tick(sdt, udt);
        }

        // ---- Ввод игрока ---------------------------------------------------

        void ProcessInput()
        {
            var st = _sim.State;

            // Блок из жеста удержания низа или кнопки HUD
            st.playerBlocking = _inputReader.IsBlocking || _blockFromButton;
            _playerGloves.SetBlock(st.playerBlocking);

            // Перезарядка удара (темп зависит от «Скорости» и перчаток): ждёт один удар в очереди, лишние — прочь,
            // иначе набитые подряд тапы долетали бы секундами позже.
            if (_punchCd > 0f)
            {
                while (_inputReader.Count > 1) _inputReader.Dequeue(out _);
                return;
            }

            while (_inputReader.Dequeue(out PunchInput p))
            {
                if (st.phase == FightPhase.KO) break;
                // лежачего и встающего не бьют
                if (st.phase == FightPhase.Down || st.phase == FightPhase.Rise) continue;

                // Мега: любой свайп при полной шкале (в режиме без меги — никогда)
                p.isMega    = st.megaReady && MegaAllowed && p.type != PunchType.Jab;
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
                long a0 = MeasureAlloc ? AllocNow() : 0;
                int fractures0 = _v.sim.fractures.Count;
                bool hitPick = _v.Pick(ray, out float3 hitPt, out byte mat, out AnatGrid grid);
                double t1 = _tickSw.Elapsed.TotalMilliseconds;
                _pickMs += t1 - t0;
                int removed = 0;
                Vector3 contact = aim;
                Vector3 hitDirW = dirW;
                if (hitPick)
                {
                    AnatSkin.Push(hitPt, _v.pickDir, strength);
                    removed = _v.Hit(hitPt, _v.pickDir, strength);
                    _hitMs += _tickSw.Elapsed.TotalMilliseconds - t1;
                    hitDirW = hr * (Vector3)_v.pickDir;
                    _hitThisFrame = true;
                    lastHeadRay = new Ray(ray.origin - hp, ray.direction);
                    lastHitPoint = hitPt; lastHitDir = _v.pickDir; lastHitStrength = strength;
                    HeadHits++;
                    contact = hp + hr * ((Vector3)hitPt * 0.01f);
                    _foeBody.AddHit(contact, hitDirW, strength, p.pw);   // без пружинности — прежний AddImpulse
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
                if (MeasureAlloc) _allocAnatFrame += AllocNow() - a0;

                _hitsTotal++;
                if (result.hit)
                {
                    _hitsLanded++;
                    if (_hitCount < _hitHistory.Length) _hitHistory[_hitCount++] = result;
                }

                if (!result.hit)
                {
                    _face.OnPlayerMiss();
                    _ai.TriggerCounter();
                    _sound.Play(SoundId.Miss);
                    if (strength > 0) _sound.Play(SoundId.Swing, 0.8f);
                }
                else
                {
                    // Эффекты удара. В HitResult перелом и разрыв — признаки порога травмы (держатся до конца боя),
                    // поэтому для эффектов и звука берётся их появление: новый перелом в анатомии, первый разрыв головы.
                    bool fracture = _v.sim.fractures.Count > fractures0;
                    _face.OnHit(strength, p.isMega, (int)_v.sim.lastZone, fracture, counter, hitPick && hitPt.x < 0f ? -1 : 1);
                    bool burst = result.headBurst && !_headBurstShown;
                    if (burst) _headBurstShown = true;
                    var fx = new HitFx
                    {
                        kind = p.isMega ? PunchType.Mega : p.type, punch = p.type, side = p.side,
                        power = p.type == PunchType.Jab && !p.isMega ? 0.4f : p.pw,
                        mega = p.isMega, counter = counter, fracture = fracture, headBurst = burst,
                        zone = (int)_v.sim.lastZone, damage = result.dmgDealt, combo = st.comboCount,
                        point = contact, dir = hitDirW,
                    };
                    double tf = _tickSw.Elapsed.TotalMilliseconds;
                    _fx.OnPlayerHit(fx);
                    if (fracture) _fx.OnFracture(_v.sim.lastZone);
                    _fxMs += _tickSw.Elapsed.TotalMilliseconds - tf;
                    PlayHitSound(p, result, fracture, st.comboCount);

                    if (result.isKO)
                    {
                        _face.OnKnockout();
                        _referee.OnKnockout(true);
                        PlayerWon = true;
                        Finished  = true;
                        _crowd.SetMood(CrowdMood.Cheer);
                        _crowd.Burst(1f);
                        _finaleNext = true;
                        _fx.OnKnockout();
                        _sound.Play(SoundId.Thud); _sound.Play(SoundId.CrowdKnockout); _sound.Play(SoundId.Bell);
                        OnFoeHit?.Invoke(result);
                        OnKnockout?.Invoke();
                    }
                    else if (result.isKnockdown)
                    {
                        // начатый до нокдауна удар соперника не долетает
                        _strikePending = false;
                        var ks = _sim.State;
                        int riseAt = RefereeLogic.RiseCount(ks.knockdowns, ks.hpMax > 0f ? ks.hp / ks.hpMax : 0f);
                        _referee.OnKnockdown(riseAt);
                        _face.OnKnockdown();
                        _crowd.SetMood(CrowdMood.Tense);
                        _crowd.Burst(0.8f);
                        _fx.OnKnockdown();
                        _sound.Play(SoundId.Thud); _sound.Play(SoundId.CrowdKnockdown);
                        OnFoeHit?.Invoke(result);
                        OnKnockdown?.Invoke();
                    }
                    else
                    {
                        // зал взрывается на мегу, контру и силовой удар
                        if (p.isMega) _crowd.Burst(1f);
                        else if (counter) _crowd.Burst(0.7f);
                        else if (strength > 0) _crowd.Burst(0.4f);
                        OnFoeHit?.Invoke(result);
                    }
                }

                float speed = Mathf.Max(0.5f, st.fightParams.speedMult);
                float gloveDur = (p.type == PunchType.Jab ? 0.085f : (p.isMega ? 0.4f : 0.2f)) / speed;
                _playerGloves.Punch(p.side, p.isMega ? PunchType.Mega : p.type, gloveDur, contact, hitPick);
                _punchCd = FightState.PunchCooldown(p.type, p.isMega, speed);

                // Один удар за тик
                break;
            }
        }

        // Метки травм: выбитые зубы, оторванная челюсть и глаза — по оторванным телам анатомии (только чтение).
        // Порядок тел AnatView: голова, нижняя челюсть, левый и правый глаз, затем зубы.
        void SyncInjuries()
        {
            var gone = _v.gone;
            if (gone == null || gone.Length < 4) return;
            int teeth = 0;
            for (int i = 4; i < gone.Length; i++) if (gone[i]) teeth++;
            _fx.SyncInjuries(teeth, gone[1], (gone[2] ? 1 : 0) + (gone[3] ? 1 : 0));
        }

        // Звук попадания по голове: вид удара, громкость по силе, хруст перелома по зоне, серия.
        void PlayHitSound(in PunchInput p, in HitResult r, bool fracture, int combo)
        {
            float vol = Mathf.Clamp(0.72f + 0.32f * (p.pw - 1f), 0.6f, 1.15f);
            SoundId id;
            if (p.isMega) id = SoundId.Mega;
            else if (p.isFlurry) id = SoundId.Flurry;
            else switch (p.type)
            {
                case PunchType.Hook:  id = p.pw >= 1.3f ? SoundId.OverHead : SoundId.HookHead; break;   // самый быстрый хук — тяжёлый
                case PunchType.Upper: id = SoundId.UpperHead; break;
                default:              id = SoundId.JabHead; break;
            }
            _sound.Play(id, vol);
            if (fracture)
            {
                switch ((AnatZone)r.zoneIdx)
                {
                    case AnatZone.Nose:  _sound.Play(SoundId.CrackNose); break;
                    case AnatZone.Cheek:
                    case AnatZone.Orbit: _sound.Play(SoundId.CrackCheek); break;
                    case AnatZone.Jaw:
                    case AnatZone.Mouth: _sound.Play(SoundId.CrackJaw); break;
                    default:             _sound.Play(SoundId.CrackSkull); break;
                }
            }
            if (combo >= 3) _sound.Play(SoundId.Combo, 1f, 1f + 0.06f * combo);
        }

        // Развязка: постановщик ведёт рефери, пояс и зал; боевой HUD и кнопки убираются — чистый кадр.
        void BeginFinale(bool playerWon)
        {
            if (_victory.State == VictoryState.Idle) _victory.Begin(playerWon, _titleFight);
            _uiRoot.HideAll();
            if (!playerWon) _playerGloves.SetVisible(false);   // игрок проиграл: в кадре только победитель и рефери
        }

        // Рука победителя поднята: зал взрывается; при победе игрока — надпись над поднятой перчаткой (в ландшафте внизу).
        void OnHandRaised()
        {
            _crowd.Burst(1f);
            _face.OnFinale(!_referee.PlayerWon);
            _sound.Play(_referee.PlayerWon ? SoundId.Win : SoundId.Lose);
            if (_referee.PlayerWon) _fx.Banners?.ShowCenter(FxBanners.Victory, BannerStyle.Huge, 0f, 0.88f, 0.12f);
        }

        // Пояс в кадре: рефери отпускает руку и отходит, руки игрока убраны, за поясом затемнение, надпись.
        void OnBeltShown()
        {
            _playerGloves.SetVisible(false);
            _refView.LetGo();
            _sound.Play(SoundId.Win);
            // надписи над поясом (пряжка — в середине кадра), края кадра затемнены
            _fx.Banners?.ShowCenter(FxBanners.Champion, BannerStyle.Huge, VictoryScene.BeltDur, 0.76f, 0.86f);
            _fx.Banners?.ShowCenter(FxBanners.BeltWon, BannerStyle.Big, VictoryScene.BeltDur, 0.25f, 0.11f);
            _fx.Overlay?.Cinema(VictoryScene.BeltDur, 0.7f);
        }

        const float PickLead = 0.05f;                      // м: начало луча Pick перед точкой прицеливания
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
            // Замах: сигнал игроку
            if (cmd.newPhase == FightPhase.Windup && _prevAIPhase != FightPhase.Windup && !Finished)
            {
                _sound.Play(SoundId.FoeWarn);
                _face.OnWindup();
            }

            // Начало фазы Strike
            if (cmd.newPhase == FightPhase.Strike && _prevAIPhase != FightPhase.Strike)
            {
                _strikePending = true;
                _strikeTimer   = 0f;
                _strikeCmd     = cmd;
                if (!Finished) { _sound.Play(SoundId.Whoosh); _face.OnStrike(); }
            }

            if (!_strikePending) return;
            _strikeTimer += realDt;
            if (_strikeTimer < 0.13f) return;
            _strikePending = false;

            var st = _sim.State;
            if (!FightState.FoeCanStrike(st.phase) || Finished) return;

            // Уклон в правильную сторону нейтрализует удар
            bool dodgeOk = st.playerDodge > 0.04f && st.playerDodgeSide != 0
                           && st.playerDodgeSide == -_strikeCmd.side;
            if (dodgeOk)
            {
                st.phase = FightPhase.Open;
                _ai.TriggerCounter();
                _fx.OnDodge();
                _face.OnDodged();
                _sound.Play(SoundId.Dodge);
                return;
            }

            float dmgBase = (9f + 1.6f * (_foeDef.level - 1)) * _aiStyle.d;
            if (_strikeCmd.isHook)    dmgBase *= 1.25f;
            if (_strikeCmd.isCounter) dmgBase *= 1.2f;
            dmgBase *= st.fightParams.defenseMult * FoeDamageMult;

            bool blocked = false, broken = false;
            if (st.playerBlocking)
            {
                st.playerStamina -= _aiStyle.bs;
                if (st.playerStamina < 0f)
                {
                    st.playerHp      -= dmgBase * 0.75f;
                    st.playerStamina  = 0f;
                    broken = true;
                }
                else
                {
                    st.playerHp -= dmgBase * _aiStyle.bc;
                    blocked = true;
                }
            }
            else
            {
                st.playerHp -= dmgBase;
            }

            st.playerHp = Mathf.Max(0f, st.playerHp);
            st.OnPlayerHurt();
            float power = dmgBase / 15f;
            _fx.OnFoeHit(power, blocked);
            _face.OnFoeLanded(blocked, broken);
            if (blocked) { _sound.Play(SoundId.Block); _foeBody.StrikeBlocked(_strikeCmd.side); }   // перчатка отскакивает от блока
            else
            {
                if (broken) _sound.Play(SoundId.GuardBreak);
                _sound.Play(power < 0.6f ? SoundId.Graze : _strikeCmd.isHook ? SoundId.HurtHook : SoundId.Hurt);
            }
            OnPlayerHit?.Invoke();

            if (st.playerHp <= 0f && !Finished)
            {
                Finished  = true;
                PlayerWon = false;
                _face.OnFinale(true);
                _referee.OnKnockout(false);
                _crowd.SetMood(CrowdMood.Cheer);
                _crowd.Burst(1f);
                BeginFinale(false);
                _sound.Play(SoundId.Thud); _sound.Play(SoundId.CrowdKnockout); _sound.Play(SoundId.Bell);
            }
        }

        // ---- Рефери --------------------------------------------------------

        void HandleRefereeEvent(RefereeEvent re)
        {
            switch (re.Type)
            {
                case RefereeEventType.CountTick:
                    _hud.ShowCount(re.Count);
                    _sound.Play(SoundId.RefCount);
                    break;
                case RefereeEventType.SayBox:
                    // Rise, не Guard: FightAI.SyncExternal(Rise) выдерживает RISE_DUR=1.9с перед EnterIdle.
                    _sim.State.phase = FightPhase.Rise;
                    _face.OnRise();
                    _sim.State.groggy = Mathf.Max(0f, _sim.State.groggy * 0.5f);
                    _hud.ShowBanner("БОЙ!", 1.5f);
                    _crowd.SetMood(CrowdMood.Idle);
                    _sound.Play(SoundId.Bell);
                    break;
                case RefereeEventType.FightStopped:
                    // отсчёт до десяти: развязка начинается здесь (нокаут начал её раньше); соперник не встал — нокаут
                    if (!Finished)
                    {
                        PlayerWon = _referee.PlayerWon;
                        _sim.State.phase = FightPhase.KO;
                        _strikePending = false;
                        if (PlayerWon) _face.OnKnockout(); else _face.OnFinale(true);
                        _crowd.SetMood(CrowdMood.Cheer);
                        _crowd.Burst(1f);
                        _sound.Play(SoundId.Bell);
                        _sound.Play(SoundId.CrowdKnockout);
                        OnKnockout?.Invoke();
                    }
                    Finished = true;
                    BeginFinale(_referee.PlayerWon);
                    break;
            }
        }

        void ShowResult()
        {
            if (_resultShown) return;
            _resultShown = true;
            _fx.Reset();   // надписи, частицы и затемнение развязки не остаются поверх экрана итогов
            bool win = _referee.PlayerWon;
            float accuracy = _hitsTotal > 0 ? (float)_hitsLanded / _hitsTotal : 0f;

            int reward;
            if (win && _modeManager != null)
            {
                // Расчёт через ModeManager: база по уровню + KD×20 + точность%×0.5 + KO +50; Trial ×2
                var hist = new HitResult[_hitCount];
                System.Array.Copy(_hitHistory, hist, _hitCount);
                reward = _modeManager.ComputeReward(hist, _modeManager.CurrentMode, _foeDef.level,
                                                    ModeManager.Accuracy(_hitsTotal, _hitsLanded));
            }
            else
            {
                reward = win ? 100 : 0;
            }

            _result.Show(win, _fightTime, _sim.State.knockdowns, accuracy, reward);
            _uiRoot.ShowScreen(ScreenId.Result);
            _sound.StopLoop();
            if (reward > 0) _sound.Play(SoundId.Coin);
            OnResultShown?.Invoke();
        }

        // ---- Камера --------------------------------------------------------

        void UpdateCamera(float realDt)
        {
            float w = _viewW > 0 ? _viewW : Screen.width, h = _viewH > 0 ? _viewH : Screen.height;
            bool land = w > h;
            // наезд эффектов сужает поле зрения; руки и пояс в осях камеры ставятся по итоговому полю, чтобы не плыть по экрану
            float fov = GameUtil.CalcFov(w, h) - (_fx != null ? _fx.FovKick : 0f);
            _cam.fieldOfView = fov;
            float aspect = h > 0f ? w / h : 0.46f;
            _playerGloves.SetView(fov, aspect);
            _belt?.SetView(fov, aspect);
            _refView?.SetView(aspect);

            Vector3 eye = _eye;

            // Уклон: игрок уходит в сторону (вправо на экране — мир −X)
            var st = _sim.State;
            float dodgeT = st.playerDodge > 0f ? -st.playerDodgeSide * 0.14f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(st.playerDodge)) : 0f;
            _camDodge = Mathf.Lerp(_camDodge, dodgeT, 1f - Mathf.Exp(-18f * realDt));
            eye.x += _camDodge;

            // Взгляд: по стойке, а за упавшим соперником — вниз, к его голове; когда рефери пошёл к победителю — снова прямо
            // (в нокауте вниз — только пока рефери подходит: его взмахи над головой должны быть в кадре)
            bool low = st.phase == FightPhase.Down
                       || (st.phase == FightPhase.KO && (_victory == null || _victory.State == VictoryState.Idle
                                                         || _referee.State == RefereeState.StepIn));
            _camFollow = Mathf.Lerp(_camFollow, low ? 0.6f : 0f, 1f - Mathf.Exp(-3.5f * realDt));
            // Развязка: взгляд поднимается к поднятой руке и залу (упавший соперник уходит из кадра); при поражении
            // в портрете камера ещё и поворачивается к рефери, который стоит справа от победителя
            bool finale = _victory != null && _victory.State >= VictoryState.Walk && _victory.State != VictoryState.Ready;
            _finale = Mathf.MoveTowards(_finale, finale ? 1f : 0f, realDt / FinaleIn);
            float fk = Smooth01(_finale);
            float pitch = Mathf.Lerp(land ? LandPitch : PortPitch, land ? FinaleLandPitch : FinalePortPitch, fk);
            float yaw = !land && !_referee.PlayerWon ? FinaleLoseYawPort * fk : 0f;

            Quaternion stand = Quaternion.Euler(pitch, 180f + yaw, _camDodge * -25f);
            Vector3 look = Vector3.Lerp(eye + stand * Vector3.forward * 1.1f, _foeBody.headMount.position, _camFollow);
            Quaternion rot = Quaternion.LookRotation(look - eye, stand * Vector3.up);
            if (_fx != null)
            {
                eye += rot * _fx.CamOffset;
                rot *= Quaternion.Euler(0f, 0f, _fx.CamRoll);
            }
            _cam.transform.SetPositionAndRotation(eye, rot);
            _fx?.SetView(aspect);
        }

        const float FinaleIn = 0.9f;                                // с, подъём взгляда в развязке
        const float FinalePortPitch = 2f, FinaleLandPitch = 6f;     // градусы вниз
        const float FinaleLoseYawPort = 6f;                         // градусы вправо: победитель и рефери вместе
        static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

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
            _hudData.foeStun01      = st.stunMax > 0f ? st.stunAccum / st.stunMax : 0f;   // шкала оглушения (UI-MOTION.md)
            _hudData.staminaHitCost = FightState.STAMINA_HIT_COST * st.fightParams.staminaCostMult;
            _hudData.cineBars       = _fx != null && _fx.Overlay != null ? _fx.Overlay.Bars : 0f;
        }

        // ---- Перезапуск ----------------------------------------------------

        public void Restart()
        {
            if (_v == null) return;
            _v.Restore();
            AnatSkin.Reset();
            _face = new FoeFace(_foeDef);
            _foeBody.ResetGear();
            _foeBody.spring.Reset();
            _foeBody.ResetFall();
            // «Заново» — тот же бой с нуля: мега-шкала не переносится
            var prms = _sim.State.fightParams;
            _sim = new FightSim(_foeDef.level, _foeDef.seed + (uint)(_restartCount + 1), prms);
            ApplyFoeTuning(_foeHpMult, _foeStunMult);
            _sim.State.playerHp = Mathf.Clamp(PlayerStartHp, 1f, 100f);
            _ai  = new FightAI(_aiStyle, _foeDef.level, _foeDef.seed);
            _referee.Reset();
            _victory.Reset();
            _refView.Snap();
            _crowd.SetMood(CrowdMood.Idle);
            _playerGloves.ClearVictory();
            _playerGloves.SetVisible(true);
            // перчатки могли смениться в магазине между боями
            if (_playerGloves.Look.id != _profile.equippedGloveId) _playerGloves.SetGloves(_profile.equippedGloveId);
            _foeBody.ReleaseWrist();
            _fx.Reset();
            _sound.StopAll();
            _sound.StartLoop();
            _sound.Play(SoundId.Bell);
            _prevMegaReady = false;
            _headBurstShown = false;
            _finaleNext = false;
            _finale = 0f;

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
            _strikeCmd       = default;
            _prevAIPhase     = FightPhase.Idle;
            _camFollow       = 0f;
            _camDodge        = 0f;
            _punchCd         = 0f;
            _hud.ResetTransient();
            lastHitFrameMs   = 0f;
            maxHitFrameMs    = 0f;
            _restartCount++;

            _uiRoot.ShowScreen(ScreenId.Fight);
            _hud.ShowBanner("БОЙ!", 1.5f);
        }

        // ---- Dispose -------------------------------------------------------

        public void Dispose()
        {
            AnatFace.Off();
            AnatSkin.Reset(); AnatSkin.Off();   // изгиб шеи включён в Build: вернуть покой, иначе он переживёт бой (тесты анатомии после тестов игры)
            _fx?.Dispose();
            _sound?.Dispose();
            _fx           = null;
            _sound        = null;
            _refView?.Dispose();
            _crowd?.Dispose();
            _belt?.Dispose();
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
            _refView      = null;
            _crowd        = null;
            _belt         = null;
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
