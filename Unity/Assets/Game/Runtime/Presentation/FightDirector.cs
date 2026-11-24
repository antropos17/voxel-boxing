using System;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using VoxProbe;

namespace VoxGame
{
    // Главный MonoBehaviour боя: строит сцену, тикает все системы, читает ввод.
    // Никакой логики в Awake/Start — только через Init().
    public class FightDirector : MonoBehaviour
    {
        // ---- Публичные свойства --------------------------------------------

        public FightState  State      => _sim?.State;
        public FighterDef  Foe        => _foeDef;
        public bool        Finished   { get; private set; }
        public bool        PlayerWon  { get; private set; }

        // Замер кадра удара (Stopwatch вокруг всего Tick в кадре, где был v.Hit).
        public float lastHitFrameMs;
        public float maxHitFrameMs;

        // Точки расширения для FxDirector (W5).
        public event Action<HitResult> OnFoeHit;
        public event Action            OnPlayerHit;
        public event Action            OnKnockdown;
        public event Action            OnKnockout;

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
        Vector3 _camBasePos;
        Vector3 _headTarget;
        float   _camShakeAmt;
        float   _camShakeDecay = 10f;

        // Статистика
        float _fightTime;
        int   _hitsTotal, _hitsLanded;
        bool  _resultShown;
        int   _restartCount;
        bool  _blockFromButton;

        // Замер кадра удара
        readonly Stopwatch _tickSw = new Stopwatch();
        bool _hitThisFrame;

        // HUD данные (кэш без аллокаций)
        HudData _hudData;

        // ---- Инициализация -------------------------------------------------

        // Строит сцену, головы и соперника. Вызывается создателем после AddComponent.
        public void Init(FighterDef foe, PlayerProfile profile, Camera cam, UIRoot ui)
        {
            _foeDef  = foe;
            _profile = profile;
            _cam     = cam;
            _uiRoot  = ui;

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

            // Ринг
            _ring = new RingBuilder();
            _ring.Build(_sceneRoot);

            // Соперник (тело + крепление головы)
            _foeBody = new FoeBody(_sceneRoot, foe, _voxMat);
            _foeBody.root.position = new Vector3(0f, 0f, 0.5f);
            _foeBody.root.rotation = Quaternion.Euler(0f, 180f, 0f);

            // Анатомия головы (строится здесь как "экран загрузки")
            AnatSkin.on = true;
            AnatSkin.Reset();
            _v = new AnatView(null, _foeBody.headMount.position, _anatMat);

            // Перчатки игрока
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

            // Камера
            _camBasePos  = new Vector3(0f, 1.65f, -0.55f);
            _headTarget  = _foeBody.headMount.position;
            cam.transform.position = _camBasePos;
            cam.transform.LookAt(_headTarget);
            cam.fieldOfView = GameUtil.CalcFov(Screen.width, Screen.height);

            // Строки HUD кэшируем один раз
            _hudData.foeName    = foe.name;
            _hudData.playerName = profile.playerName;
            _hudData.foeHpMax   = _sim.State.hpMax;
            _hudData.playerHpMax = 100f;

            _uiRoot.ShowScreen(ScreenId.Fight);
            _hud.ShowBanner("БОЙ!", 1.5f);
        }

        // ---- Tick / Update -------------------------------------------------

        void Update() => Tick(Time.unscaledDeltaTime);

        // Тик боя. realDt — реальное время кадра (Time.unscaledDeltaTime).
        public void Tick(float realDt)
        {
            if (_sim == null) return;

            // Замер полного кадра удара
            _hitThisFrame = false;
            _tickSw.Restart();

            // Масштабированное игровое время
            float sdt;
            if (_freezeTimer > 0f) { _freezeTimer -= realDt; sdt = 0f; }
            else if (_slowTimer  > 0f) { _slowTimer  -= realDt; sdt = realDt * _slowSpeed; }
            else sdt = realDt;

            // Счётчик времени боя
            if (!Finished) _fightTime += sdt;

            // Порядок кадра головы: AnatSkin.Step(realDt) → v.Tick(sdt) → AnatSkin.SetGlobals(root)
            AnatSkin.Step(realDt);

            // Ввод игрока
            if (!Finished) ProcessInput(sdt);

            // ИИ: TickAI продвигает _gameTime и TickRecovery; реальный ИИ переопределяет фазу
            _sim.TickAI(sdt);
            AICommand cmd = _ai.Tick(sdt, _sim.State);
            _sim.State.phase = cmd.newPhase;

            // Удар соперника
            ProcessFoeStrike(realDt, cmd);
            _prevAIPhase = cmd.newPhase;

            // Анимация тел
            _foeBody.Tick(cmd, sdt);
            _playerGloves.Tick(sdt);

            // Анатомия головы
            _v.Tick(sdt);
            AnatSkin.SetGlobals(_v.root);

            // Рефери (реальное время)
            _referee.Tick(realDt);
            while (_referee.TryDequeueEvent(out RefereeEvent re))
                HandleRefereeEvent(re);

            // Результаты после поднятия руки
            if (!_resultShown && _referee.State == RefereeState.RaiseHand)
                ShowResult();

            // Камера
            UpdateCamera(realDt);

            // HUD
            FillHudData(sdt);
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
            }
        }

        // ---- Ввод игрока ---------------------------------------------------

        void ProcessInput(float sdt)
        {
            var st = _sim.State;

            // Блок из жеста удержания низа или кнопки HUD
            st.playerBlocking = _inputReader.IsBlocking || _blockFromButton;

            // Перчатки отражают состояние блока
            _playerGloves.SetBlock(st.playerBlocking);

            while (_inputReader.Dequeue(out PunchInput p))
            {
                if (st.phase == FightPhase.KO) break;

                // Мега: любой свайп при полной шкале
                p.isMega    = st.megaReady && p.type != PunchType.Jab;
                p.isFlurry  = false;

                int strength = p.type == PunchType.Jab ? 0 : (p.isMega ? 2 : 1);
                Vector3 target = ComputePunchTarget(p.type, p.side);
                Ray ray = new Ray(_cam.transform.position, target - _cam.transform.position);

                bool hitPick = _v.Pick(ray, out float3 hitPt, out byte mat, out AnatGrid grid);
                int removed = 0;
                if (hitPick)
                {
                    AnatSkin.Push(hitPt, _v.pickDir, strength);
                    removed = _v.Hit(hitPt, _v.pickDir, strength);
                    if (removed > 0) _hitThisFrame = true;
                }

                bool counter = st.phase == FightPhase.Windup;
                bool open    = st.phase == FightPhase.Open;

                var result = _sim.ProcessHit(
                    removed, (int)_v.sim.lastZone, _v.sim.traumaAll,
                    _v.detachedBodies, strength, p.isMega, p.pw, counter, open);

                _hitsTotal++;
                if (result.hit) _hitsLanded++;

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
                _playerGloves.Punch(p.side, p.type, gloveDur);

                // Один удар за тик
                break;
            }
        }

        // Вычислить мировую точку прицеливания по типу и стороне удара.
        Vector3 ComputePunchTarget(PunchType type, int side)
        {
            Vector3 c = _headTarget;
            switch (type)
            {
                case PunchType.Hook:
                    // side=-1 → лев. скула (мир +X), side=+1 → пр. скула (мир -X)
                    return c + new Vector3(-side * 0.065f, 0f, 0f);
                case PunchType.Upper:
                    return c + new Vector3(-side * 0.03f, -0.09f, 0.02f);
                case PunchType.Mega:
                    return c + new Vector3(0f, 0.07f, -0.01f);
                default: // Jab
                    return c;
            }
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

            float dmgBase = (8f + 1.4f * (_foeDef.level - 1)) * _aiStyle.d;
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
                    _sim.State.phase = FightPhase.Guard;
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
            int reward = win ? 100 + _sim.State.knockdowns * 50 : 20;
            _result.Show(win, _fightTime, _sim.State.knockdowns, accuracy, reward);
            _uiRoot.ShowScreen(ScreenId.Result);
        }

        // ---- Камера --------------------------------------------------------

        void UpdateCamera(float realDt)
        {
            if (_camShakeAmt > 0.0001f)
            {
                float s = _camShakeAmt;
                Vector3 off = new Vector3(
                    UnityEngine.Random.Range(-s, s),
                    UnityEngine.Random.Range(-s, s),
                    0f);
                _cam.transform.position = _camBasePos + off;
                _camShakeAmt = Mathf.Max(0f, _camShakeAmt - _camShakeDecay * realDt);
            }
            else
            {
                _cam.transform.position = _camBasePos;
                _camShakeAmt = 0f;
            }
            _cam.transform.LookAt(_headTarget);
            _cam.fieldOfView = GameUtil.CalcFov(Screen.width, Screen.height);
        }

        // ---- HUD -----------------------------------------------------------

        void FillHudData(float sdt)
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
            _hudData.combo          = st.comboCount;
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
            _sim.CarryMegaMeter(prevMega);
            _ai  = new FightAI(_aiStyle, _foeDef.level, _foeDef.seed);
            _referee.Reset();

            Finished      = false;
            PlayerWon     = false;
            _resultShown  = false;
            _fightTime    = 0f;
            _hitsTotal    = 0;
            _hitsLanded   = 0;
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
            if (_sceneRoot != null) GameUtil.Destroy(_sceneRoot.gameObject);
            if (_anatMat   != null) GameUtil.Destroy(_anatMat);
            if (_voxMat    != null) GameUtil.Destroy(_voxMat);
            _ring         = null;
            _foeBody      = null;
            _v            = null;
            _playerGloves = null;
        }

        void OnDestroy() => Dispose();
    }
}
