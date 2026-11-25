// GameApp.cs — корень игры: единственный Update. Владеет UIRoot, экранами, GameFlow (профиль, сохранение),
// фоном меню (пустой ринг с толпой, камера облетает) и FightDirector на время боя.
// Всё через явные Init(Camera) и Tick(realDt): редактор вне Play Mode может прогнать игру вызовами Tick.
using UnityEngine;

namespace VoxGame {

    public class GameApp : MonoBehaviour {

        // ------------------------------------------------------------------ Настройки (подгонять по снимкам)

        const float OrbitRadius = 4.3f;       // м от центра ринга: за помостом (2.6), перед первым рядом зрителей (4.95)
        const float OrbitHeight = 2.2f;       // м над настилом: выше верхнего каната (1.36)
        const float OrbitLookY  = 0.55f;      // точка взгляда над центром ринга
        const float OrbitSpeed  = 5f;         // град/с
        const float MenuFovPortrait = 68f, MenuFovLandscape = 46f;
        const int   MaxHits = 512;            // как история ударов FightDirector
        static readonly Vector3 LightForward = new Vector3(0.30f, -0.75f, -0.60f);

        // Сколько реальных секунд держать экран загрузки (бой строится на втором тике). Снимкам можно поставить 0.
        public float LoadingHold = 0.6f;

        // ------------------------------------------------------------------ Публичное

        public GameFlow Flow { get; private set; }
        public UIRoot UI { get; private set; }
        public FightDirector Fight { get; private set; }
        public Camera Cam => _cam;

        public MainMenuScreen MainMenu { get; private set; }
        public ModeSelectScreen ModeSelect { get; private set; }
        public RosterScreen Roster { get; private set; }
        public PlayerScreen Player { get; private set; }
        public ShopScreen Shop { get; private set; }
        public SettingsScreen Settings { get; private set; }
        public PauseScreen Pause { get; private set; }
        public RewardScreen Reward { get; private set; }
        public LoadingScreen Loading { get; private set; }
        public ResultScreen Result => _result;           // экран итогов текущего боя (его строит FightDirector)

        public float CrowdVolume => Flow != null ? Flow.Settings.CrowdVolume : 1f;   // для звука толпы
        public bool  Vibration   => Flow != null && Flow.Settings.vibration;

        // ------------------------------------------------------------------ Поля

        Camera _cam;
        int _viewW, _viewH;

        Transform _menuScene;
        RingBuilder _menuRing;
        CrowdView _menuCrowd;
        float _orbit;

        GameObject _fightSlot, _resultSlot;   // пустые экраны на место уничтоженных HUD и итогов
        ResultScreen _result;
        readonly HitResult[] _hits = new HitResult[MaxHits];
        int _hitCount;
        float _fightTime;
        bool _decidedSent, _endedSent;

        int _loadTicks;
        float _loadT;
        bool _built;

        // ------------------------------------------------------------------ Инициализация

        // savePath / seed — для тестов и снимков; по умолчанию сохранение в persistentDataPath.
        public void Init(Camera cam, string savePath = null, uint seed = 0x5EED2027u) {
            _cam = cam;
            Flow = new GameFlow(savePath, seed);

            var uiGO = new GameObject("UIRoot");
            uiGO.transform.SetParent(transform, false);
            UI = uiGO.AddComponent<UIRoot>();
            UI.Init(cam);

            _fightSlot = new GameObject("FightSlot", typeof(RectTransform));
            UI.Register(ScreenId.Fight, _fightSlot);
            _resultSlot = new GameObject("ResultSlot", typeof(RectTransform));
            UI.Register(ScreenId.Result, _resultSlot);

            BuildScreens();
            BuildMenuScene();
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
            ModeSelect.Init(UI, () => f.StartCareer(), () => f.StartQuick(), () => f.StartTrial(), () => f.Back());

            Roster = NewScreen<RosterScreen>("Roster");
            Roster.Init(UI, i => f.PickFighter(i), () => f.Back());

            Player = NewScreen<PlayerScreen>("Player");
            Player.Init(UI, () => f.Back(), () => f.OpenShop());

            Shop = NewScreen<ShopScreen>("Shop");
            Shop.Init(UI, id => f.BuyGlove(id), id => f.EquipGlove(id), t => f.BuyUpgrade(t), () => f.Back());

            Settings = NewScreen<SettingsScreen>("Settings");
            Settings.Init(UI,
                v => { f.SetSoundLevel(v); ApplyAudio(); },
                v => { f.SetCrowdLevel(v); ApplyAudio(); },
                on => f.SetVibration(on),
                () => { if (f.ResetProgress()) Settings.SetData(f.Settings, f.CanResetProgress); },
                () => f.Back());

            Pause = NewScreen<PauseScreen>("Pause");
            Pause.Init(UI, () => f.Resume(), () => f.OpenSettings(), () => f.QuitToMenu());

            Reward = NewScreen<RewardScreen>("Reward");
            Reward.Init(UI, () => f.Continue(), () => f.OpenShop(), () => f.Rematch());

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

        // Запуск в Play Mode и в сборке: сцена Game.unity несёт пустой объект GameRoot — на нём создаётся игра.
        // Не Awake/Start: вызывается движком после загрузки сцены; в других сценах (нет GameRoot) ничего не делает.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootScene() {
            var root = GameObject.Find("GameRoot");
            if (root == null || root.GetComponent<FightDirector>() != null) return;
            var app = root.GetComponent<GameApp>();
            if (app != null && app.Flow != null) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (app == null) app = root.AddComponent<GameApp>();
            app.Init(cam);
        }

        // ------------------------------------------------------------------ Tick

        void Update() { Tick(Time.deltaTime); }

        public void Tick(float realDt) {
            if (Flow == null) return;
            float udt = realDt;

            switch (Flow.State) {
                case AppState.Loading:
                    TickLoading(udt);
                    break;
                case AppState.Fight:
                case AppState.Victory:
                case AppState.Result:
                    TickFight(udt);
                    break;
            }

            if (Fight == null) {
                UpdateOrbit(udt);
                _menuCrowd.Tick(udt, udt);
            }

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
        }

        // Экран загрузки виден хотя бы кадр, на следующем тике строится бой, после выдержки — в бой.
        void TickLoading(float udt) {
            _loadT += udt;
            _loadTicks++;
            if (!_built && _loadTicks >= 2) BuildFight();
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
            // FightDirector сам показывает экран итогов, когда развязка закончилась
            if (!_endedSent && UI.ActiveScreen == ScreenId.Result
                && (Flow.State == AppState.Fight || Flow.State == AppState.Victory)) {
                _endedSent = true;
                Flow.FightEnded(Fight.Referee.PlayerWon, CollectStats());
            }
        }

        // ------------------------------------------------------------------ Переходы

        void OnState(AppState prev, AppState next) {
            var p = Flow.Profile;
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
                    UI.ShowScreen(ScreenId.FighterSelect);
                    break;
                case AppState.Loading:
                    DisposeFight();
                    Loading.Show(Flow.Foe, ModeLine());
                    UI.ShowScreen(LoadingScreen.Id);
                    _loadT = 0f; _loadTicks = 0; _built = false;
                    break;
                case AppState.Fight:
                    UI.ShowScreen(ScreenId.Fight);
                    break;
                case AppState.Pause:
                    Pause.Show(Flow.Foe.name);
                    UI.ShowScreen(ScreenId.Pause);
                    break;
                case AppState.Victory:
                    break;   // развязку ведёт FightDirector, HUD остаётся
                case AppState.Result:
                    PatchResult();
                    UI.ShowScreen(ScreenId.Result);
                    break;
                case AppState.Reward:
                    DisposeFight();
                    Reward.Show(Flow.LastReward, p);
                    UI.ShowScreen(RewardScreen.Id);
                    break;
                case AppState.Shop:
                    DisposeFight();
                    Shop.SetData(p);
                    Shop.SelectGlove(p.equippedGloveId);
                    UI.ShowScreen(ScreenId.Shop);
                    break;
                case AppState.Settings:
                    Settings.SetData(Flow.Settings, Flow.CanResetProgress);
                    UI.ShowScreen(ScreenId.Settings);
                    break;
                case AppState.Player:
                    Player.SetData(p);
                    UI.ShowScreen(ScreenId.Player);
                    break;
            }
        }

        string ModeLine() {
            switch (Flow.Mode) {
                case GameMode.Career:
                    int step = Flow.FoeLadderIndex + 1;
                    string s = "Карьера · ступень " + step + " из " + RosterDef.Count;
                    return step == RosterDef.Count ? s + " · бой за пояс" : s;
                case GameMode.Trial:
                    return "Испытание · бой " + (Flow.TrialIndex + 1) + " из " + Flow.TrialTotal;
                default:
                    return "Быстрый бой";
            }
        }

        // ------------------------------------------------------------------ Бой

        void BuildFight() {
            _built = true;
            var go = new GameObject("Fight");
            go.transform.SetParent(transform, false);
            Fight = go.AddComponent<FightDirector>();
            Fight.enabled = false;   // бой тикает только из GameApp.Tick, собственный Update не нужен
            // перчатки и улучшения применяются из профиля (PlayerProfile.GetCombatStats внутри FightDirector.Init)
            Fight.Init(Flow.Foe, Flow.Profile, _cam, UI, Flow.Modes, 1f, 1f, Flow.FightLadderIndex, Flow.FightLadderCount);
            if (_viewW > 0 && _viewH > 0) Fight.SetViewSize(_viewW, _viewH);

            Fight.Hud.OnPause = () => Flow.Pause();
            Fight.OnFoeHit += CollectHit;
            _hitCount = 0;
            _fightTime = 0f;
            _decidedSent = _endedSent = false;

            // экран итогов строит FightDirector; кнопки ведём через GameFlow
            _result = FindResultScreen();
            if (_result != null) {
                _result.OnNext = () => Flow.Continue();
                _result.OnRematch = () => Flow.Rematch();
                _result.OnMenu = () => Flow.QuitToMenu();
            }

            _menuScene.gameObject.SetActive(false);
            UI.ShowScreen(LoadingScreen.Id);   // FightDirector.Init показал HUD — загрузка остаётся до FightLoaded
        }

        ResultScreen FindResultScreen() {
            var all = UI.GetComponentsInChildren<ResultScreen>(true);
            // новейший — последний ребёнок safe-слоя (прежний может ещё ждать отложенного Destroy)
            ResultScreen best = null;
            int bestIdx = -1;
            foreach (var r in all) {
                if (r == null) continue;
                int idx = r.transform.GetSiblingIndex();
                if (idx > bestIdx) { bestIdx = idx; best = r; }
            }
            return best;
        }

        void CollectHit(HitResult r) {
            if (r.hit && _hitCount < MaxHits) _hits[_hitCount++] = r;
        }

        FightStats CollectStats() {
            var hist = new HitResult[_hitCount];
            System.Array.Copy(_hits, hist, _hitCount);
            return new FightStats {
                time = _fightTime,
                knockdowns = Fight.State != null ? Fight.State.knockdowns : 0,
                hitsThrown = Fight.HitsThrown,
                hitsLanded = Fight.HitsLanded,
                history = hist,
            };
        }

        // Итоги после поражения тоже ведут к награде (утешительной), а строка награды — по начислению GameFlow.
        void PatchResult() {
            if (_result == null || Flow.LastReward == null) return;
            var next = _result.transform.Find("BtnNext");
            if (next != null) next.gameObject.SetActive(true);
            var rew = _result.transform.Find("StatRew");
            var t = rew != null ? rew.GetComponent<UnityEngine.UI.Text>() : null;
            if (t != null) t.text = "Награда: " + MenuKit.Coins(Flow.LastReward.coins);
        }

        void DisposeFight() {
            if (Fight == null) return;
            Fight.OnFoeHit -= CollectHit;
            var go = Fight.gameObject;
            Fight.Dispose();
            GameUtil.Destroy(go);
            Fight = null;
            _result = null;
            // UIRoot держит ссылки на уничтоженные HUD и итоги — заменить пустышками
            if (UI != null) {
                UI.Register(ScreenId.Fight, _fightSlot);
                UI.Register(ScreenId.Result, _resultSlot);
            }
            if (_menuScene != null) _menuScene.gameObject.SetActive(true);
            UpdateOrbit(0f);
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
            UI.SetViewSize(w, h);
            if (Fight != null) Fight.SetViewSize(w, h);
            else UpdateOrbit(0f);
        }

        void ApplyAudio() {
            AudioListener.volume = Flow.Settings.SoundVolume;
        }

        // ------------------------------------------------------------------ Освобождение

        public void Dispose() {
            if (Flow != null) Flow.OnStateChanged -= OnState;
            DisposeFight();
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
