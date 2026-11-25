// GameFlow.cs — машина состояний приложения: меню, выбор режима, лестница, загрузка, бой, итоги, награда, магазин.
// Чистая логика без MonoBehaviour: команды возвращают true, если переход допустим, иначе ничего не меняют.
// Победа ведёт сразу к награде, поражение — к экрану итогов («ЕЩЁ РАЗ» / «В МЕНЮ»).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGame {

    public enum AppState {
        Boot, MainMenu, ModeSelect, Roster, Loading,
        Fight, Pause, Victory, Result, Reward,
        Shop, Settings, Player
    }

    // Итог покупки или надевания в магазине (шире BuyResult: есть «закрыто» и «не куплено»).
    public enum ShopResult { Ok, NotEnoughCoins, MaxLevel, Locked, AlreadyOwned, NotOwned, AlreadyEquipped, Unavailable }

    // Итоги боя от представления.
    public struct FightStats {
        public bool won;
        public float time;          // секунды боя
        public int knockdowns;      // нокдаунов соперника
        public int hitsThrown;
        public int hitsLanded;
        public HitResult[] history; // попавшие удары игрока (для ModeManager.ComputeReward); null — пусто
    }

    public struct RewardLine {
        public string label;
        public int value;
        public RewardLine(string label, int value) { this.label = label; this.value = value; }
    }

    // Что показать на экране награды.
    public class RewardInfo {
        public bool won;
        public GameMode mode;
        public int coins;               // всего монет
        public int xp;
        public int levelsGained;
        public int newLevel;
        public bool belt;               // взят пояс лестницы
        public bool ladderAdvanced;
        public int ladderIndex;         // ступень после боя
        public int trialFight;          // номер боя испытания (1..), 0 — не испытание
        public int trialTotal;
        public bool trialDone;          // испытание пройдено целиком
        public string nextFoe;          // «Имя «Прозвище»» или пусто
        public readonly List<RewardLine> lines = new List<RewardLine>(8);
    }

    public class GameFlow {

        public const uint RosterSeed = 42u;         // та же лестница, что на принятых снимках
        public const int ConsolationBase = 20;      // утешительные монеты за поражение
        public const int ConsolationPerKd = 10;     // + за каждый нокдаун соперника
        public const int LossXp = 10;
        public const int BeltBonus = 500;           // за пояс лестницы
        public const int TrialBonus = 150;          // за пройденное испытание
        public const int TrainingCoins = 25;        // тренировка: немного монет, без фарма
        public const int TrainingXp = 15;
        public const float TrainingFoeDamage = 0.2f; // урон соперника на тренировке (доля обычного)

        // ------------------------------------------------------------------ Состояние

        public AppState State { get; private set; } = AppState.Boot;
        public AppState Previous { get; private set; } = AppState.Boot;
        public PlayerProfile Profile { get; private set; }
        public RosterDef Roster { get; }
        public ModeManager Modes { get; } = new ModeManager();
        public GameMode Mode => Modes.CurrentMode;

        public FighterDef Foe { get; private set; }
        public int FoeLadderIndex { get; private set; } = -1;   // место соперника в лестнице; -1 — вне карьеры
        public int FightSerial { get; private set; }            // растёт с каждым новым боем
        public int TrialIndex { get; private set; }             // 0-based номер боя испытания
        public int TrialTotal { get; private set; }
        public RewardInfo LastReward { get; private set; }
        public FightStats LastStats { get; private set; }
        public int SaveCount { get; private set; }               // сколько раз писали сохранение (тесты)
        public bool LastSaveFailed { get; private set; }         // запись не удалась (диск, права) — игра идёт дальше
        public int RestartCount { get; private set; }            // перезапусков боя из паузы
        public bool HasFoe { get; private set; }

        // Шапка боя для FightDirector: титульный ли бой и место в лестнице.
        public int FightLadderIndex => Mode == GameMode.Career ? FoeLadderIndex : -1;
        public int FightLadderCount => Mode == GameMode.Career ? RosterDef.Count : 0;

        // Урон соперника в бою (доля): на тренировке он бьёт слабо.
        public float FoeDamageMult => Mode == GameMode.Training ? TrainingFoeDamage : 1f;

        public event Action<AppState, AppState> OnStateChanged;   // (было, стало)

        readonly string _savePath;
        uint _rng;
        uint _trialSeed;
        AppState _shopReturn = AppState.MainMenu;
        bool _shopFromReward;
        AppState _settingsReturn = AppState.MainMenu;
        static readonly PlayerProfile _probe = new PlayerProfile();   // для расчёта эффекта улучшений

        // savePath: null — SaveService.DefaultPath или persistentDataPath. seed — случайные соперники.
        public GameFlow(string savePath = null, uint seed = 0x5EED2027u) {
            _savePath = savePath ?? SaveService.DefaultPath ?? (Application.persistentDataPath + "/save.json");
            _rng = seed == 0 ? 0x9E3779B9u : seed;
            Roster = new RosterDef(RosterSeed);

            Profile = new PlayerProfile();
            Profile.LoadFrom(SaveService.Load(_savePath));
        }

        public string SavePath => _savePath;

        // ------------------------------------------------------------------ Команды: меню

        public bool Boot() {
            if (State != AppState.Boot) return false;
            return Go(AppState.MainMenu);
        }

        public bool Play() {
            if (State != AppState.MainMenu) return false;
            return Go(AppState.ModeSelect);
        }

        public bool OpenPlayer() {
            if (State != AppState.MainMenu) return false;
            return Go(AppState.Player);
        }

        public bool OpenSettings() {
            if (State != AppState.MainMenu && State != AppState.Pause) return false;
            _settingsReturn = State;
            return Go(AppState.Settings);
        }

        public bool OpenShop() {
            if (State != AppState.MainMenu && State != AppState.Reward && State != AppState.Player) return false;
            _shopFromReward = State == AppState.Reward;
            _shopReturn = State;
            return Go(AppState.Shop);
        }

        public bool Back() {
            switch (State) {
                case AppState.ModeSelect: return Go(AppState.MainMenu);
                case AppState.Roster:     return Go(AppState.ModeSelect);
                case AppState.Player:     return Go(AppState.MainMenu);
                case AppState.Settings:   return Go(_settingsReturn);
                case AppState.Pause:      return Go(AppState.Fight);
                case AppState.Shop:
                    // магазин после награды: «назад» ведёт туда же, куда «дальше» с экрана награды
                    if (_shopFromReward) return ContinueAfterReward();
                    return Go(_shopReturn);
                default: return false;
            }
        }

        // ------------------------------------------------------------------ Команды: режимы и соперники

        public bool StartCareer() {
            if (State != AppState.ModeSelect) return false;
            Modes.StartMode(GameMode.Career);
            return Go(AppState.Roster);
        }

        public bool StartQuick() {
            if (State != AppState.ModeSelect) return false;
            Modes.StartMode(GameMode.QuickFight);
            SetFoe(RosterDef.GenerateQuickFight(NextRand(), QuickLevel()), -1);
            return BeginLoading();
        }

        public bool StartTrial() {
            if (State != AppState.ModeSelect) return false;
            BeginTrial(NextRand());
            return BeginLoading();
        }

        // Тренировка: соперник первой ступени, бьёт слабо; в счёт побед и поражений не идёт.
        public bool StartTraining() {
            if (State != AppState.ModeSelect) return false;
            Modes.StartMode(GameMode.Training);
            SetFoe(RosterDef.GenerateQuickFight(NextRand(), 1), -1);
            return BeginLoading();
        }

        // Драться можно со следующим по лестнице и с уже побеждёнными.
        public bool IsFighterAvailable(int index) =>
            index >= 0 && index < RosterDef.Count && index <= Profile.ladderIndex;

        public bool PickFighter(int index) {
            if (State != AppState.Roster || Mode != GameMode.Career) return false;
            if (!IsFighterAvailable(index)) return false;
            SetFoe(Roster.Get(index), index);
            return BeginLoading();
        }

        // ------------------------------------------------------------------ Команды: бой

        public bool FightLoaded() {
            if (State != AppState.Loading) return false;
            return Go(AppState.Fight);
        }

        public bool Pause() {
            if (State != AppState.Fight) return false;
            return Go(AppState.Pause);
        }

        public bool Resume() {
            if (State != AppState.Pause) return false;
            return Go(AppState.Fight);
        }

        // «Заново» из паузы: тот же соперник с начала, без начислений. Сцену перезапускает представление.
        public bool RestartFight() {
            if (State != AppState.Pause) return false;
            RestartCount++;
            return Go(AppState.Fight);
        }

        // Исход решён (нокаут или остановка): идёт развязка.
        public bool FightDecided() {
            if (State != AppState.Fight) return false;
            return Go(AppState.Victory);
        }

        // Развязка закончилась: награда начисляется и сохраняется сразу. Победа — экран награды,
        // поражение — экран итогов («ЕЩЁ РАЗ» / «В МЕНЮ»), утешительный приз уже начислен.
        public bool FightEnded(bool won, FightStats stats) {
            if (State != AppState.Fight && State != AppState.Victory) return false;
            stats.won = won;
            LastStats = stats;
            LastReward = won ? ApplyWin(stats) : ApplyLoss(stats);
            Save();
            return Go(won ? AppState.Reward : AppState.Result);
        }

        public bool Continue() {
            if (State == AppState.Result) return Go(AppState.Reward);
            if (State == AppState.Reward) return ContinueAfterReward();
            return false;
        }

        public bool Rematch() {
            if (State != AppState.Result && State != AppState.Reward) return false;
            if (!HasFoe) return false;
            // испытание после поражения начинается заново с теми же тремя соперниками
            if (Mode == GameMode.Trial && Modes.TrialFightsLeft <= 0) BeginTrial(_trialSeed);
            return BeginLoading();
        }

        // Из паузы — сдаться (поражение без награды); с итогов и награды — просто в меню.
        public bool QuitToMenu() {
            if (State == AppState.Pause) {
                Modes.OnFightLost(Profile);
                LastReward = null;
                Save();
                return Go(AppState.MainMenu);
            }
            if (State == AppState.Result || State == AppState.Reward) return Go(AppState.MainMenu);
            return false;
        }

        // ------------------------------------------------------------------ Магазин

        bool ShopOpen => State == AppState.Shop || State == AppState.Player;

        public static int GloveUnlockLadder(in GloveEntry g) {
            var c = g.unlockCondition;
            if (string.IsNullOrEmpty(c)) return 0;
            const string pre = "ladder>=";
            if (c.StartsWith(pre, StringComparison.Ordinal) && int.TryParse(c.Substring(pre.Length), out int n))
                return Mathf.Max(0, n);
            return 0;
        }

        public static bool IsGloveUnlocked(in GloveEntry g, PlayerProfile p) => p.ladderIndex >= GloveUnlockLadder(g);

        public ShopResult BuyGlove(string id) {
            if (!ShopOpen) return ShopResult.Unavailable;
            int idx = Array.FindIndex(GlovesCatalog.All, g => g.id == id);
            if (idx < 0) return ShopResult.Unavailable;
            if (Profile.OwnsGlove(id)) return ShopResult.AlreadyOwned;
            if (!IsGloveUnlocked(GlovesCatalog.All[idx], Profile)) return ShopResult.Locked;
            var r = Profile.BuyGlove(id);
            if (r == BuyResult.NotEnoughCoins) return ShopResult.NotEnoughCoins;
            if (r != BuyResult.Ok) return ShopResult.Unavailable;
            Save();
            return ShopResult.Ok;
        }

        public ShopResult EquipGlove(string id) {
            if (!ShopOpen) return ShopResult.Unavailable;
            if (!Profile.OwnsGlove(id)) return ShopResult.NotOwned;
            if (Profile.equippedGloveId == id) return ShopResult.AlreadyEquipped;
            Profile.EquipGlove(id);
            Save();
            return ShopResult.Ok;
        }

        public ShopResult BuyUpgrade(UpgradeType t) {
            if (!ShopOpen) return ShopResult.Unavailable;
            var r = Profile.BuyUpgrade(t);
            if (r == BuyResult.NotEnoughCoins) return ShopResult.NotEnoughCoins;
            if (r == BuyResult.MaxLevel) return ShopResult.MaxLevel;
            if (r != BuyResult.Ok) return ShopResult.Unavailable;
            Save();
            return ShopResult.Ok;
        }

        // Цена следующего уровня; -1 — максимум.
        public int UpgradeCost(UpgradeType t) {
            int lv = Profile.upgradeLevels[(int)t];
            return lv >= PlayerProfile.MaxUpgradeLevel ? -1 : PlayerProfile.UpgradeCosts[lv];
        }

        // Множитель улучшения на уровне level (без перчаток).
        public static float UpgradeMult(UpgradeType t, int level) {
            Array.Clear(_probe.upgradeLevels, 0, _probe.upgradeLevels.Length);
            _probe.upgradeLevels[(int)t] = Mathf.Clamp(level, 0, PlayerProfile.MaxUpgradeLevel);
            switch (t) {
                case UpgradeType.Power:   return _probe.GetDmgMult();
                case UpgradeType.Speed:   return _probe.GetSpeedMult();
                case UpgradeType.Stamina: return _probe.GetStaminaMult();
                case UpgradeType.Defense: return _probe.GetDefenseMult();
                default:                  return _probe.GetMegaGainMult();
            }
        }

        // ------------------------------------------------------------------ Настройки (в профиле, в том же сохранении)

        public bool SetSoundEnabled(bool on) {
            if (State != AppState.Settings) return false;
            Profile.soundEnabled = on;
            Save();
            return true;
        }

        public bool SetVolume(VolumeKind kind, float v) {
            if (State != AppState.Settings) return false;
            Profile.SetVolume(kind, v);
            Save();
            return true;
        }

        public bool SetVibration(bool on) {
            if (State != AppState.Settings) return false;
            Profile.vibrationEnabled = on;
            Save();
            return true;
        }

        // Сброс прогресса доступен только из настроек главного меню (не из паузы боя). Звук и вибрация остаются.
        public bool CanResetProgress => State == AppState.Settings && _settingsReturn == AppState.MainMenu;

        public bool ResetProgress() {
            if (!CanResetProgress) return false;
            var old = Profile;
            Profile = new PlayerProfile {
                soundEnabled = old.soundEnabled,
                vibrationEnabled = old.vibrationEnabled,
                masterVolume = old.masterVolume,
                sfxVolume = old.sfxVolume,
                crowdVolume = old.crowdVolume,
            };
            LastReward = null;
            HasFoe = false;
            FoeLadderIndex = -1;
            Save();
            return true;
        }

        // ------------------------------------------------------------------ Начисления

        RewardInfo ApplyWin(FightStats s) {
            var info = NewInfo(true);
            if (Mode == GameMode.Training) {
                // тренировка: малая награда вместо разбивки за бой
                info.lines.Add(new RewardLine("Тренировка", TrainingCoins));
                FinishInfo(info, EconomyState.Apply(Profile, TrainingCoins, TrainingXp));
                return info;
            }

            var hist = s.history ?? Array.Empty<HitResult>();
            int baseCoins = Modes.ComputeReward(hist, Mode);
            int xp = Mathf.Max(10, baseCoins / 2);
            FillWinLines(info.lines, hist, baseCoins);
            int coins = baseCoins;

            switch (Mode) {
                case GameMode.Career:
                    // повтор уже побеждённого соперника лестницу не двигает
                    if (FoeLadderIndex == Profile.ladderIndex && Profile.ladderIndex < RosterDef.Count) {
                        bool title = Profile.ladderIndex == RosterDef.Count - 1;
                        Modes.OnFightWon(Profile);
                        info.ladderAdvanced = true;
                        if (title) {
                            Profile.beltCount++;
                            info.belt = true;
                            coins += BeltBonus;
                            info.lines.Add(new RewardLine("Пояс лестницы", BeltBonus));
                        }
                    } else {
                        Profile.wins++;
                    }
                    break;
                case GameMode.Trial:
                    bool more = Modes.OnFightWon(Profile);
                    if (!more) {
                        info.trialDone = true;
                        coins += TrialBonus;
                        info.lines.Add(new RewardLine("Испытание пройдено", TrialBonus));
                    }
                    break;
                default:
                    Modes.OnFightWon(Profile);
                    break;
            }

            var r = EconomyState.Apply(Profile, coins, xp);
            FinishInfo(info, r);
            return info;
        }

        RewardInfo ApplyLoss(FightStats s) {
            var info = NewInfo(false);
            Modes.OnFightLost(Profile);
            if (Mode == GameMode.Training) {
                FinishInfo(info, EconomyState.Apply(Profile, 0, LossXp));
                return info;
            }
            int coins = ConsolationBase + Mathf.Max(0, s.knockdowns) * ConsolationPerKd;
            info.lines.Add(new RewardLine("Утешительный приз", ConsolationBase));
            if (s.knockdowns > 0)
                info.lines.Add(new RewardLine("Нокдауны соперника ×" + s.knockdowns, s.knockdowns * ConsolationPerKd));
            var r = EconomyState.Apply(Profile, coins, LossXp);
            FinishInfo(info, r);
            return info;
        }

        RewardInfo NewInfo(bool won) {
            return new RewardInfo {
                won = won,
                mode = Mode,
                trialFight = Mode == GameMode.Trial ? TrialIndex + 1 : 0,
                trialTotal = Mode == GameMode.Trial ? TrialTotal : 0,
            };
        }

        void FinishInfo(RewardInfo info, FightReward r) {
            info.coins = r.coins;
            info.xp = r.xp;
            info.levelsGained = r.levelsGained;
            info.newLevel = Profile.level;
            info.ladderIndex = Profile.ladderIndex;
            info.nextFoe = "";
            if (Mode == GameMode.Career && Profile.ladderIndex < RosterDef.Count) {
                var f = Roster.Get(Profile.ladderIndex);
                info.nextFoe = f.name + " «" + f.nickname + "»";
            }
        }

        // Разбивка по формуле ModeManager.ComputeReward; расхождение (множитель испытания) — отдельной строкой.
        static void FillWinLines(List<RewardLine> lines, HitResult[] hist, int total) {
            int kds = 0, hits = 0, sum = 0;
            bool ko = false;
            foreach (var h in hist) {
                if (h.isKnockdown) kds++;
                if (h.hit) hits++;
                if (h.isKO) ko = true;
            }
            lines.Add(new RewardLine("За победу", 100)); sum += 100;
            if (hist.Length > 0) {
                if (kds > 0) { lines.Add(new RewardLine("Нокдауны ×" + kds, kds * 20)); sum += kds * 20; }
                float acc = (float)hits / hist.Length * 100f;
                int accCoins = (int)(acc * 0.5f);
                if (accCoins > 0) { lines.Add(new RewardLine("Точность " + Mathf.RoundToInt(acc) + "%", accCoins)); sum += accCoins; }
                if (ko) { lines.Add(new RewardLine("Нокаут", 50)); sum += 50; }
            }
            if (total != sum) lines.Add(new RewardLine("Испытание ×2", total - sum));
        }

        // ------------------------------------------------------------------ Внутреннее

        bool ContinueAfterReward() {
            _shopFromReward = false;
            var info = LastReward;
            switch (Mode) {
                case GameMode.Career:
                    return Go(AppState.Roster);
                case GameMode.Trial:
                    if (info != null && info.won && Modes.TrialFightsLeft > 0) {
                        TrialIndex++;
                        SetFoe(TrialFoe(_trialSeed, TrialIndex), -1);
                        return BeginLoading();
                    }
                    return Go(AppState.MainMenu);
                default:
                    return Go(AppState.MainMenu);
            }
        }

        void BeginTrial(uint seed) {
            Modes.StartMode(GameMode.Trial);
            _trialSeed = seed;
            TrialIndex = 0;
            TrialTotal = Modes.TrialFightsLeft;
            SetFoe(TrialFoe(seed, 0), -1);
        }

        // Соперники испытания: сильнее текущей ступени, каждый следующий — на уровень выше.
        FighterDef TrialFoe(uint seed, int k) {
            int level = Mathf.Clamp(Profile.ladderIndex + 2 + k, 2, RosterDef.Count);
            return RosterDef.GenerateQuickFight(seed + (uint)k * 7919u, level);
        }

        int QuickLevel() => Mathf.Clamp(Profile.ladderIndex + 1, 1, RosterDef.Count);

        void SetFoe(FighterDef f, int ladderIndex) {
            Foe = f;
            FoeLadderIndex = ladderIndex;
            HasFoe = true;
        }

        bool BeginLoading() {
            FightSerial++;
            return Go(AppState.Loading);
        }

        bool Go(AppState next) {
            var prev = State;
            Previous = prev;
            State = next;
            OnStateChanged?.Invoke(prev, next);
            return true;
        }

        uint NextRand() {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return _rng;
        }

        // ------------------------------------------------------------------ Сохранение

        // Ошибка записи (диск, права) не роняет игру: прогресс остаётся в памяти до следующей записи.
        public void Save() {
            try {
                var d = new SaveData();
                Profile.SaveTo(d);
                SaveService.Save(d, _savePath);
                SaveCount++;
                LastSaveFailed = false;
            } catch (Exception e) {
                LastSaveFailed = true;
                Debug.LogWarning("VoxGame: сохранение не записано: " + e.Message);
            }
        }
    }
}
