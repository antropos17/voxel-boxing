// GameApp.cs — корень игры: единственный Update. Владеет UIRoot, экранами, GameFlow (профиль, сохранение),
// фоном меню (пустой ринг с толпой, камера облетает), звуком кнопок и FightDirector на время боя.
// Всё через явные Init и Tick(realDt): редактор вне Play Mode может прогнать игру вызовами Tick и Press.
using System;
using UnityEngine;

namespace VoxGame {

    public class GameApp : MonoBehaviour {

        // ------------------------------------------------------------------ Настройки (подгонять по снимкам)

        public const uint DefaultSeed = 0x5EED2027u;
        const float OrbitRadius = 4.3f;       // м от центра ринга: за помостом (2.6), перед первым рядом зрителей (4.95)
        const float OrbitHeight = 2.2f;       // м над настилом: выше верхнего каната (1.36)
        const float OrbitLookY  = 0.55f;      // точка взгляда над центром ринга
        const float OrbitSpeed  = 5f;         // град/с
        const float MenuFovPortrait = 68f, MenuFovLandscape = 46f;
        const float MaxFrameDt = 0.1f;        // длинный кадр (загрузка, сворачивание) не прыгает дальше 0.1 с
        const int   MaxHits = 512;            // как история ударов FightDirector
        static readonly Vector3 LightForward = new Vector3(0.30f, -0.75f, -0.60f);

        // Сколько реальных секунд держать экран загрузки (бой строится на втором тике). Сквозному проходу можно 0.
        public float LoadingHold = 0.6f;

        // ------------------------------------------------------------------ Публичное

        public GameFlow Flow { get; private set; }
        public UIRoot UI { get; private set; }
        public FightDirector Fight { get; private set; }
        public Camera Cam => _cam;

        public bool Initialized => Flow != null;
        public AppState State => Flow != null ? Flow.State : AppState.Boot;
        public ScreenId ActiveScreen => UI != null ? UI.ActiveScreen : ScreenId.MainMenu;

        public MainMenuScreen MainMenu { get; private set; }
        public ModeSelectScreen ModeSelect { get; private set; }
        public RosterScreen Roster { get; private set; }
        public PlayerScreen Player { get; private set; }
        public ShopScreen Shop { get; private set; }
        public SettingsScreen Settings { get; private set; }
        public PauseScreen Pause { get; private set; }
        public RewardScreen Reward { get; private set; }
        public LoadingScreen Loading { get; private set; }
        public ResultScreen Result => Fight != null ? Fight.Result : null;   // экран итогов строит FightDirector

        public InputReader FightInput => Fight != null ? Fight.GetComponent<InputReader>() : null;   // синтетические удары
        public SoundPlayer UiSound => _uiSound;
        public ShopResult LastShopResult { get; private set; } = ShopResult.Unavailable;
        public PreviewStudio Studio => _studio;   // портреты бойцов и перчатки для меню (кэш с пределом)
        public bool Vibration => Flow != null && Flow.Profile.vibrationEnabled;

        // ------------------------------------------------------------------ Поля

        Camera _cam;
        int _viewW, _viewH;
        bool _bootTried;

        Transform _menuScene;
        RingBuilder _menuRing;
        CrowdView _menuCrowd;
        float _orbit;
        SoundPlayer _uiSound;
        PreviewStudio _studio;

        GameObject _fightSlot, _resultSlot;   // пустые экраны на место уничтоженных HUD и итогов
        readonly HitResult[] _hits = new HitResult[MaxHits];
        int _hitCount;
        float _fightTime;
        bool _decidedSent, _endedSent, _resultShown;

        int _loadTicks;
        float _loadT;
        bool _built;

        // ------------------------------------------------------------------ Инициализация

        // Запуск со сцены (Play Mode и сборка): размер окна — Screen, сохранение — persistentDataPath.
        public void Init(Camera cam) => Init(cam, 0, 0, null, DefaultSeed);

        // viewW/viewH — размер окна вывода, если он не Screen (снимки в текстуру; 0 — Screen).
        // savePath — файл сохранения (сквозной проход пишет во временный); seed — случайные соперники.
        public void Init(Camera cam, int viewW, int viewH, string savePath = null, uint seed = DefaultSeed) {
            if (Flow != null) return;   // повторный Init ничего не делает
            _cam = cam;
            _viewW = viewW; _viewH = viewH;
            Flow = new GameFlow(savePath, seed);

            var uiGO = new GameObject("UIRoot");
            uiGO.transform.SetParent(transform, false);
            UI = uiGO.AddComponent<UIRoot>();
            UI.Init(cam);
            if (viewW > 0 && viewH > 0) UI.SetViewSize(viewW, viewH);   // до регистрации: экраны сразу в нужной ориентации

            _fightSlot = new GameObject("FightSlot", typeof(RectTransform));
            UI.Register(ScreenId.Fight, _fightSlot);
            _resultSlot = new GameObject("ResultSlot", typeof(RectTransform));
            UI.Register(ScreenId.Result, _resultSlot);

            BuildScreens();
            BuildMenuScene();
            _studio = new PreviewStudio(transform);
            Roster.OnSelect = ShowPortrait;
            Shop.OnGloveSelected = ShowGlovePreview;

            _uiSound = new SoundPlayer();
            _uiSound.Init(transform, seed);
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            UIKit.OnButtonClick = OnUiClick;
            ApplyAudio();

            Flow.OnStateChanged += OnState;
            Flow.Boot();
        }

        T NewScreen<T>(string name) where T : MonoBehaviour {
            var go = new GameObject(name, typeof(RectTransform));
            return go.AddComponent<T>();
        }

        void BuildScreens() {
            var f = Flow;
            MainMenu = NewScreen<MainMenuScreen>("MainMenu");
            MainMenu.Init(UI, () => f.Play(), () => f.OpenPlayer(), () => f.OpenShop(), () => f.OpenSettings());

            ModeSelect = NewScreen<ModeSelectScreen>("ModeSelect");
            ModeSelect.Init(UI, () => f.StartCareer(), () => f.StartQuick(), () => f.StartTraining(), () => f.StartTrial(),
                            () => f.Back());

            Roster = NewScreen<RosterScreen>("Roster");
            Roster.Init(UI, i => f.PickFighter(i), () => f.Back());

            Player = NewScreen<PlayerScreen>("Player");
            Player.Init(UI, () => f.Back(), () => f.OpenShop());

            Shop = NewScreen<ShopScreen>("Shop");
            Shop.Init(UI, id => LastShopResult = f.BuyGlove(id), id => LastShopResult = f.EquipGlove(id),
                      t => LastShopResult = f.BuyUpgrade(t), () => f.Back());

            Settings = NewScreen<SettingsScreen>("Settings");
            Settings.Init(UI,
                on => { f.SetSoundEnabled(on); ApplyAudio(); },
                (k, v) => { f.SetVolume(k, v); ApplyAudio(); },
                on => f.SetVibration(on),
                () => { if (f.ResetProgress()) Settings.SetData(f.Profile, f.CanResetProgress); },
                () => f.Back());

            Pause = NewScreen<PauseScreen>("Pause");
            Pause.Init(UI, () => f.Resume(), RestartFromPause, () => f.OpenSettings(), () => f.QuitToMenu());

            Reward = NewScreen<RewardScreen>("Reward");
            Reward.Init(UI, () => f.Continue(), () => f.OpenShop(), () => f.Rematch(), () => f.QuitToMenu());

            Loading = NewScreen<LoadingScreen>("Loading");
            Loading.Init(UI);
        }

        // Фон меню: пустой ринг и зал со зрителями, как в бою.
        void BuildMenuScene() {
            _menuScene = new GameObject("MenuScene").transform;
            _menuScene.SetParent(transform, false);
            _menuRing = new RingBuilder();
            _menuRing.Build(_menuScene, Vector3.zero, 0f, LightForward);
            _menuCrowd = new CrowdView();
            _menuCrowd.Build(_menuScene, CrowdStands.Default(Vector3.zero, 0f));
            _menuCrowd.SetMood(CrowdMood.Idle);
            UpdateOrbit(0f);
        }

        // ------------------------------------------------------------------ Tick

        // Сцена Game.unity несёт GameRoot с этим компонентом: первый кадр Play Mode или сборки создаёт игру
        // (единственное исключение из правила «Init вызывает создатель»). Вне Play Mode Update не идёт.
        void Update() {
            if (Flow == null) {
                if (_bootTried) return;
                _bootTried = true;
                var cam = Camera.main;
                if (cam == null) { Debug.LogWarning("VoxGame: в сцене нет камеры MainCamera — игра не запущена"); return; }
                // ориентация плеера: поворот разрешён в обе стороны (портрет и ландшафт), даже если в настройках портрет
                Screen.autorotateToPortrait = Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = true;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.orientation = ScreenOrientation.AutoRotation;
                Init(cam);
            }
            // «Назад» на Android (и Escape): в бою — пауза, в меню — шаг назад
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) BackPressed();
            Tick(Time.deltaTime);
        }

        // Игра свернута (звонок, «Домой», шторка): бой встаёт на паузу, прогресс пишется на диск.
        // В редакторе потеря фокуса окна игры — не сворачивание: там не трогаем.
        void OnApplicationPause(bool paused) {
            if (paused && !Application.isEditor) Suspend();
        }

        void OnApplicationFocus(bool focus) {
            if (!focus && !Application.isEditor) Suspend();
        }

        public void Suspend() {
            if (Flow == null) return;
            if (Flow.State == AppState.Fight) Flow.Pause();
            Flow.Save();
        }

        public void BackPressed() {
            if (Flow == null) return;
            if (Flow.State == AppState.Fight) { if (Flow.Pause()) _uiSound?.Play(SoundId.Ui); }
            else Flow.Back();
        }

        public void Tick(float realDt) {
            if (Flow == null) return;
            float udt = Mathf.Clamp(realDt, 0f, MaxFrameDt);

            // Подсистемы под отдельной защитой: исключение в одной не останавливает остальные и цикл кадра.
            try {
                switch (Flow.State) {
                    case AppState.Loading:
                        TickLoading(udt);
                        break;
                    case AppState.Fight:
                    case AppState.Victory:
                    case AppState.Result:
                        TickFight(udt);
                        break;
                    // Pause и Settings из паузы: бой не тикает — стоит
                }
            } catch (Exception e) { Report("бой", e); }

            try {
                if (Fight == null) {
                    UpdateOrbit(udt);
                    _menuCrowd.Tick(udt, udt);
                }
            } catch (Exception e) { Report("фон меню", e); }
            try { _uiSound.Tick(udt); } catch (Exception e) { Report("звук", e); }

            try {
                UI.Tick(udt, udt);
                MainMenu.Tick(udt, udt);
                ModeSelect.Tick(udt, udt);
                Roster.Tick(udt, udt);
                Player.Tick(udt, udt);
                Shop.Tick(udt, udt);
                Settings.Tick(udt, udt);
                Pause.Tick(udt, udt);
                Reward.Tick(udt, udt);
                Loading.Tick(udt, udt);
            } catch (Exception e) { Report("интерфейс", e); }
        }

        // Ошибка подсистемы пишется в лог один раз на место (не каждый кадр); число ошибок — для проверок.
        public int ErrorCount { get; private set; }
        string _lastErr;
        void Report(string where, Exception e) {
            ErrorCount++;
            string key = where + e.GetType().Name + e.Message;
            if (key == _lastErr) return;
            _lastErr = key;
            Debug.LogError("VoxGame: сбой (" + where + "): " + e);
        }

        // Экран загрузки виден хотя бы кадр, на следующем тике строится бой (голова — здесь, не в бою),
        // после выдержки — в бой.
        void TickLoading(float udt) {
            _loadT += udt;
            _loadTicks++;
            if (!_built && _loadTicks >= 2) {
                try { BuildFight(); }
                catch (Exception e) {
                    // бой не построился: убрать недостроенное и вернуться в меню, а не застрять в пустом бою
                    Report("постройка боя", e);
                    try { DisposeFight(); } catch (Exception e2) { Report("уборка боя", e2); }
                    Flow.AbortLoading();
                    return;
                }
            }
            if (_built && _loadT >= LoadingHold) Flow.FightLoaded();
        }

        void TickFight(float udt) {
            if (Fight == null) return;
            Fight.Tick(udt);
            if (!Fight.Finished) _fightTime += Fight.lastSdt;

            if (Flow.State == AppState.Fight && Fight.Finished && !_decidedSent) {
                _decidedSent = true;
                Flow.FightDecided();
            }
            // развязка закончилась (FightDirector показал итоги): начислить; победа уводит к награде
            if (_resultShown && !_endedSent && (Flow.State == AppState.Fight || Flow.State == AppState.Victory)) {
                _endedSent = true;
                Flow.FightEnded(Fight.Referee.PlayerWon, CollectStats());
            }
        }

        // Вызывается изнутри FightDirector.Tick: только флаг, переход — после Tick.
        void OnFightResultShown() => _resultShown = true;

        // Щелчок паузы — звуком меню: звук боя на паузе глушится в том же нажатии.
        void OnHudPause() { if (Flow.Pause()) _uiSound.Play(SoundId.Ui); }

        // ------------------------------------------------------------------ Переходы

        // Переход уже случился в GameFlow: исключение здесь не должно оставить игрока на старом экране.
        void OnState(AppState prev, AppState next) {
            try { ApplyState(prev, next); }
            catch (Exception e) {
                Report("переход " + prev + " → " + next, e);
                try { UI.ShowScreen(ScreenOf(next)); } catch (Exception e2) { Report("экран", e2); }
            }
        }

        static ScreenId ScreenOf(AppState s) {
            switch (s) {
                case AppState.ModeSelect: return ScreenId.ModeSelect;
                case AppState.Roster:     return ScreenId.FighterSelect;
                case AppState.Loading:    return ScreenId.Loading;
                case AppState.Fight:      return ScreenId.Fight;
                case AppState.Pause:      return ScreenId.Pause;
                case AppState.Result:     return ScreenId.Result;
                case AppState.Reward:     return ScreenId.Reward;
                case AppState.Shop:       return ScreenId.Shop;
                case AppState.Settings:   return ScreenId.Settings;
                case AppState.Player:     return ScreenId.Player;
                default:                  return ScreenId.MainMenu;
            }
        }

        void ApplyState(AppState prev, AppState next) {
            var p = Flow.Profile;
            // надписи и вспышки удара лежат поверх всех экранов: на паузе, в настройках и в итогах их не видно
            if (Fight != null && Fight.Fx != null) Fight.Fx.SetOverlayVisible(next == AppState.Fight || next == AppState.Victory);
            switch (next) {
                case AppState.MainMenu:
                    DisposeFight();
                    MainMenu.SetProfile(p);
                    UI.ShowScreen(ScreenId.MainMenu);
                    break;
                case AppState.ModeSelect:
                    ModeSelect.SetData(p);
                    UI.ShowScreen(ScreenId.ModeSelect);
                    break;
                case AppState.Roster:
                    DisposeFight();
                    Roster.SetData(Flow.Roster, p);
                    ShowPortrait(Roster.Selected);
                    UI.ShowScreen(ScreenId.FighterSelect);
                    break;
                case AppState.Loading:
                    DisposeFight();
                    Loading.Show(Flow.Foe, ModeLine());
                    UI.ShowScreen(ScreenId.Loading);
                    _loadT = 0f; _loadTicks = 0; _built = false;
                    break;
                case AppState.Fight:
                    UI.ShowScreen(ScreenId.Fight);
                    DrainInput();   // нажатия на экране паузы не должны стать ударами
                    break;
                case AppState.Pause:
                    Pause.Show(Flow.Foe.name, Flow.Mode != GameMode.Training);
                    UI.ShowScreen(ScreenId.Pause);
                    break;
                case AppState.Victory:
                    break;   // развязку ведёт FightDirector
                case AppState.Result:
                    PatchResult();
                    UI.ShowScreen(ScreenId.Result);
                    break;
                case AppState.Reward:
                    DisposeFight();
                    Reward.Show(Flow.LastReward, p);
                    UI.ShowScreen(ScreenId.Reward);
                    if (Flow.LastReward != null && Flow.LastReward.coins > 0) _uiSound.Play(SoundId.Coin);
                    break;
                case AppState.Shop:
                    DisposeFight();
                    Shop.SetData(p);
                    Shop.SelectGlove(p.equippedGloveId);
                    ShowGlovePreview(Shop.SelectedGloveId);
                    UI.ShowScreen(ScreenId.Shop);
                    break;
                case AppState.Settings:
                    Settings.SetData(p, Flow.CanResetProgress);
                    UI.ShowScreen(ScreenId.Settings);
                    break;
                case AppState.Player:
                    Player.SetData(p);
                    UI.ShowScreen(ScreenId.Player);
                    break;
            }
            ApplyAudio();
        }

        string ModeLine() {
            switch (Flow.Mode) {
                case GameMode.Career:
                    int step = Flow.FoeLadderIndex + 1;
                    string s = "Лестница · ступень " + step + " из " + RosterDef.Count;
                    return step == RosterDef.Count ? s + " · бой за пояс" : s;
                case GameMode.Trial:
                    return "Испытание · бой " + (Flow.TrialIndex + 1) + " из " + Flow.TrialTotal;
                case GameMode.Training:
                    return "Тренировка · соперник бьёт слабо";
                default:
                    return "Быстрый бой";
            }
        }

        // ------------------------------------------------------------------ Картинки меню

        // Портрет выбранного бойца лестницы; закрытый — заглушка «?» (лицо следующих соперников не раскрываем).
        void ShowPortrait(int index) {
            if (_studio == null || Roster == null) return;
            try {
                if (index < 0 || index >= RosterDef.Count || !Roster.IsAvailable(index)) { Roster.SetPortrait(null); return; }
                Roster.SetPortrait(_studio.Portrait(index, Flow.Roster.Get(index)));
            } catch (Exception e) { Report("портрет", e); Roster.SetPortrait(null); }
        }

        void ShowGlovePreview(string id) {
            if (_studio == null || Shop == null || string.IsNullOrEmpty(id)) return;
            try { Shop.SetPreview(id, _studio.Glove(id)); }
            catch (Exception e) { Report("превью перчатки", e); }
        }

        // ------------------------------------------------------------------ Бой

        void BuildFight() {
            var go = new GameObject("Fight");
            go.transform.SetParent(transform, false);
            Fight = go.AddComponent<FightDirector>();
            Fight.enabled = false;   // бой тикает только из GameApp.Tick, собственный Update не нужен
            Fight.FoeDamageMult = Flow.FoeDamageMult;
            Fight.PlayerStartHp = Flow.StartPlayerHp;
            var prof = Flow.Profile;
            Fight.ShowControlsHint = Flow.Mode == GameMode.Training || prof.wins + prof.losses < HintFights;
            // перчатки и улучшения применяются из профиля (PlayerProfile.GetCombatStats внутри FightDirector.Init);
            // пояс выносят только в первом титульном бою (повтор двенадцатой ступени — обычный бой)
            Fight.Init(Flow.Foe, Flow.Profile, _cam, UI, Flow.Modes, 1f, 1f,
                       Flow.IsTitleFight ? Flow.FightLadderIndex : -1, Flow.FightLadderCount);
            if (_viewW > 0 && _viewH > 0) Fight.SetViewSize(_viewW, _viewH);

            Fight.Hud.OnPause += OnHudPause;
            Fight.OnFoeHit += CollectHit;
            Fight.OnResultShown = OnFightResultShown;
            ResetFightCounters();

            // экран итогов строит FightDirector; кнопки ведём через GameFlow (замена его собственного «реванша»)
            var r = Fight.Result;
            if (r != null) {
                r.OnNext = () => Flow.Continue();
                r.OnRematch = () => Flow.Rematch();
                r.OnMenu = () => Flow.QuitToMenu();
            }

            _menuScene.gameObject.SetActive(false);
            UI.ShowScreen(ScreenId.Loading);   // FightDirector.Init показал HUD — загрузка остаётся до FightLoaded
            ApplyAudio();
            _built = true;   // только после успешной постройки: иначе загрузка пустила бы в бой без HUD
        }

        const int HintFights = 2;   // подсказка жестов в первых боях (и всегда на тренировке)

        void ResetFightCounters() {
            _hitCount = 0;
            _fightTime = 0f;
            _decidedSent = _endedSent = _resultShown = false;
        }

        // «Заново» на экране паузы: тот же соперник с начала, голова восстанавливается без пересборки.
        void RestartFromPause() {
            if (Fight == null || !Flow.RestartFight()) return;
            Fight.FoeDamageMult = Flow.FoeDamageMult;
            Fight.Restart();
            ResetFightCounters();
            DrainInput();
        }

        void DrainInput() {
            var inp = FightInput;
            if (inp == null) return;
            while (inp.Dequeue(out _)) { }
        }

        void CollectHit(HitResult r) {
            if (r.hit && _hitCount < MaxHits) _hits[_hitCount++] = r;
        }

        FightStats CollectStats() {
            var hist = new HitResult[_hitCount];
            Array.Copy(_hits, hist, _hitCount);
            return new FightStats {
                time = _fightTime,
                knockdowns = Fight.State != null ? Fight.State.knockdowns : 0,
                hitsThrown = Fight.HitsThrown,
                hitsLanded = Fight.HitsLanded,
                history = hist,
                playerHpLeft = Fight.State != null ? Fight.State.playerHp : 0f,
            };
        }

        // Итоги поражения: строка награды — по начислению GameFlow (утешительный приз и опыт).
        void PatchResult() {
            var r = Result;
            var info = Flow.LastReward;
            if (r == null || info == null) return;
            string s = info.coins > 0 ? "Утешительный приз: +" + MenuKit.Coins(info.coins) : "Без монет";
            r.SetRewardLine(s + "\nопыт +" + info.xp);   // две строки: перенос не рвёт «опыт +10»
        }

        void DisposeFight() {
            if (Fight == null) return;
            Fight.OnFoeHit -= CollectHit;
            Fight.OnResultShown = null;
            var go = Fight.gameObject;
            Fight.Dispose();
            GameUtil.Destroy(go);
            Fight = null;
            // UIRoot держит ссылки на уничтоженные HUD и итоги — заменить пустышками
            if (UI != null) {
                UI.Register(ScreenId.Fight, _fightSlot);
                UI.Register(ScreenId.Result, _resultSlot);
            }
            if (_menuScene != null) _menuScene.gameObject.SetActive(true);
            UpdateOrbit(0f);
        }

        // ------------------------------------------------------------------ Звук

        // Громкости и «звук выкл» из профиля; на паузе (и в настройках из паузы) звук боя молчит.
        void ApplyAudio() {
            if (Flow == null) return;
            var p = Flow.Profile;
            if (_uiSound != null) {
                _uiSound.SetVolumes(p.sfxVolume, p.crowdVolume, p.masterVolume);
                if (_uiSound.Muted == p.soundEnabled) _uiSound.Mute(!p.soundEnabled);
            }
            var fs = Fight != null ? Fight.Sound : null;
            if (fs != null) {
                fs.SetVolumes(p.sfxVolume, p.crowdVolume, p.masterVolume);
                bool mute = !p.soundEnabled || Flow.State == AppState.Pause || Flow.State == AppState.Settings;
                if (fs.Muted != mute) fs.Mute(mute);
            }
        }

        // Общий обработчик кнопок (UIKit.OnButtonClick). Кнопки боя озвучивает FightDirector.
        void OnUiClick() {
            if (Flow == null || _uiSound == null) return;
            var s = _clickState != AppState.Boot ? _clickState : Flow.State;
            if (s == AppState.Fight || s == AppState.Victory) return;
            _uiSound.Play(SoundId.Ui);
        }

        // ------------------------------------------------------------------ Программные нажатия (сквозной проход)

        // Эквивалент нажатия кнопки текущего экрана. id — «экран.кнопка[:аргумент]», см. список в ответе писателя
        // и в switch ниже. Возвращает false, если такой кнопки на текущем экране нет или она неактивна.
        public bool Press(string id) {
            if (Flow == null || string.IsNullOrEmpty(id)) return false;
            string arg = null;
            int colon = id.IndexOf(':');
            if (colon >= 0) { arg = id.Substring(colon + 1); id = id.Substring(0, colon); }
            // щелчок звучит как у настоящей кнопки: по состоянию до нажатия (UIKit.Click — первый слушатель кнопки)
            var before = Flow.State;
            bool ok = Dispatch(id, arg);
            if (ok) { _clickState = before; UIKit.Click(); _clickState = AppState.Boot; }
            return ok;
        }

        AppState _clickState = AppState.Boot;   // Boot — брать текущее состояние

        bool Dispatch(string id, string arg) {
            var s = Flow.State;
            switch (id) {
                // главное меню
                case "menu.play":     if (s != AppState.MainMenu) return false; MainMenu.PressPlay(); return true;
                case "menu.player":   if (s != AppState.MainMenu) return false; MainMenu.PressPlayer(); return true;
                case "menu.shop":     if (s != AppState.MainMenu) return false; MainMenu.PressShop(); return true;
                case "menu.settings": if (s != AppState.MainMenu) return false; MainMenu.PressSettings(); return true;

                // выбор режима
                case "mode.ladder":   if (s != AppState.ModeSelect) return false; ModeSelect.PressLadder(); return true;
                case "mode.quick":    if (s != AppState.ModeSelect) return false; ModeSelect.PressQuick(); return true;
                case "mode.training": if (s != AppState.ModeSelect) return false; ModeSelect.PressTraining(); return true;
                case "mode.trial":    if (s != AppState.ModeSelect) return false; ModeSelect.PressTrial(); return true;
                case "mode.back":     if (s != AppState.ModeSelect) return false; ModeSelect.PressBack(); return true;

                // лестница
                case "roster.select":
                    if (s != AppState.Roster || !int.TryParse(arg, out int idx) || idx < 0 || idx >= Roster.Count) return false;
                    Roster.Select(idx); return true;
                case "roster.fight":
                    if (s != AppState.Roster || !Roster.IsAvailable(Roster.Selected)) return false;
                    Roster.PressFight(); return true;
                case "roster.back":   if (s != AppState.Roster) return false; Roster.PressBack(); return true;

                // бой (HUD)
                case "fight.pause":       if (s != AppState.Fight || Fight == null) return false; Fight.Hud.PressPause(); return true;
                case "fight.block":       if (s != AppState.Fight || Fight == null) return false; Fight.Hud.PressBlock(); return true;
                case "fight.dodge-left":  if (s != AppState.Fight || Fight == null) return false; Fight.Hud.PressDodgeLeft(); return true;
                case "fight.dodge-right": if (s != AppState.Fight || Fight == null) return false; Fight.Hud.PressDodgeRight(); return true;

                // пауза; «В МЕНЮ» — два нажатия
                case "pause.resume":   if (s != AppState.Pause) return false; Pause.PressResume(); return true;
                case "pause.restart":  if (s != AppState.Pause) return false; Pause.PressRestart(); return true;
                case "pause.settings": if (s != AppState.Pause) return false; Pause.PressSettings(); return true;
                case "pause.menu":     if (s != AppState.Pause) return false; Pause.PressMenu(); return true;

                // итоги поражения
                case "result.again": if (s != AppState.Result || Result == null) return false; Result.PressRematch(); return true;
                case "result.menu":  if (s != AppState.Result || Result == null) return false; Result.PressMenu(); return true;
                case "result.next":  if (s != AppState.Result || Result == null) return false; Result.PressNext(); return true;

                // награда
                case "reward.next":  if (s != AppState.Reward) return false; Reward.PressMain(); return true;
                case "reward.shop":  if (s != AppState.Reward) return false; Reward.PressShop(); return true;
                case "reward.menu":  if (s != AppState.Reward) return false; Reward.PressMenu(); return true;

                // магазин
                case "shop.gloves":   if (s != AppState.Shop) return false; Shop.ShowTab(ShopScreen.Tab.Gloves); return true;
                case "shop.upgrades": if (s != AppState.Shop) return false; Shop.ShowTab(ShopScreen.Tab.Upgrades); return true;
                case "shop.glove": {
                    if (s != AppState.Shop || Shop.CurrentTab != ShopScreen.Tab.Gloves) return false;
                    int gi = Array.FindIndex(GlovesCatalog.All, g => g.id == arg);
                    if (gi < 0) return false;
                    Shop.SelectGlove(gi); return true;
                }
                case "shop.buy":
                    if (s != AppState.Shop || !Shop.BuyEnabled) return false;
                    Shop.PressBuy(); return true;
                case "shop.equip":
                    if (s != AppState.Shop || !Shop.EquipEnabled) return false;
                    Shop.PressEquip(); return true;
                case "shop.upgrade": {
                    if (s != AppState.Shop || Shop.CurrentTab != ShopScreen.Tab.Upgrades) return false;
                    if (!TryUpgrade(arg, out UpgradeType t) || !Shop.UpgradeEnabled(t)) return false;
                    Shop.PressUpgrade(t); return true;
                }
                case "shop.back":     if (s != AppState.Shop) return false; Shop.PressBack(); return true;

                // экран бойца
                case "player.back":   if (s != AppState.Player) return false; Player.PressBack(); return true;
                case "player.shop":   if (s != AppState.Player) return false; Player.PressShop(); return true;

                // настройки
                case "settings.sound":     if (s != AppState.Settings) return false; Settings.PressSound(); return true;
                case "settings.vibration": if (s != AppState.Settings) return false; Settings.PressVibration(); return true;
                case "settings.master-":   if (s != AppState.Settings) return false; Settings.PressVolume(VolumeKind.Master, -1); return true;
                case "settings.master+":   if (s != AppState.Settings) return false; Settings.PressVolume(VolumeKind.Master, 1); return true;
                case "settings.sfx-":      if (s != AppState.Settings) return false; Settings.PressVolume(VolumeKind.Sfx, -1); return true;
                case "settings.sfx+":      if (s != AppState.Settings) return false; Settings.PressVolume(VolumeKind.Sfx, 1); return true;
                case "settings.crowd-":    if (s != AppState.Settings) return false; Settings.PressVolume(VolumeKind.Crowd, -1); return true;
                case "settings.crowd+":    if (s != AppState.Settings) return false; Settings.PressVolume(VolumeKind.Crowd, 1); return true;
                case "settings.reset":     if (s != AppState.Settings || !Settings.ResetVisible) return false; Settings.PressReset(); return true;
                case "settings.reset-yes": if (s != AppState.Settings || !Settings.ConfirmShown) return false; Settings.PressResetConfirm(); return true;
                case "settings.reset-no":  if (s != AppState.Settings || !Settings.ConfirmShown) return false; Settings.PressResetCancel(); return true;
                case "settings.back":      if (s != AppState.Settings) return false; Settings.PressBack(); return true;
            }
            return false;
        }

        static bool TryUpgrade(string arg, out UpgradeType t) {
            switch (arg) {
                case "power":   t = UpgradeType.Power; return true;
                case "speed":   t = UpgradeType.Speed; return true;
                case "stamina": t = UpgradeType.Stamina; return true;
                case "defense": t = UpgradeType.Defense; return true;
                case "mega":    t = UpgradeType.MegaCharge; return true;
                default:        t = UpgradeType.Power; return false;
            }
        }

        // ------------------------------------------------------------------ Камера меню и вид

        void UpdateOrbit(float udt) {
            if (_cam == null) return;
            _orbit += OrbitSpeed * udt;
            if (_orbit >= 360f) _orbit -= 360f;
            float a = _orbit * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Sin(a) * OrbitRadius, OrbitHeight, Mathf.Cos(a) * OrbitRadius);
            var look = new Vector3(0f, OrbitLookY, 0f);
            _cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            float w = _viewW > 0 ? _viewW : Screen.width, h = _viewH > 0 ? _viewH : Screen.height;
            _cam.fieldOfView = w > h ? MenuFovLandscape : MenuFovPortrait;
        }

        // Размер окна вывода, если он не равен Screen (снимки в текстуру). 0 — вернуться к Screen.
        public void SetViewSize(int w, int h) {
            _viewW = w; _viewH = h;
            if (UI == null) return;
            UI.SetViewSize(w, h);
            if (Fight != null) Fight.SetViewSize(w, h);
            else UpdateOrbit(0f);
        }

        // ------------------------------------------------------------------ Освобождение

        public void Dispose() {
            if (Flow != null) Flow.OnStateChanged -= OnState;
            UIKit.OnButtonClick -= OnUiClick;
            DisposeFight();
            _uiSound?.Dispose();
            _uiSound = null;
            _studio?.Dispose();
            _studio = null;
            _menuCrowd?.Dispose();
            _menuRing?.Dispose();
            _menuCrowd = null;
            _menuRing = null;
            if (_menuScene != null) GameUtil.Destroy(_menuScene.gameObject);
            _menuScene = null;
        }

        void OnDestroy() => Dispose();
    }
}
