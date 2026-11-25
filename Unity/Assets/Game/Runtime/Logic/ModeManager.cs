using UnityEngine;

namespace VoxGame {

    public enum GameMode { Career, QuickFight, Trial, Training }

    /// <summary>
    /// Правила режимов и расчёт награды.
    /// Trial: 3 боя подряд, HP +5 между боями, монеты ×2, мега запрещена.
    /// Training: слабый соперник, бой не идёт в счёт побед и поражений.
    /// </summary>
    public class ModeManager {

        public GameMode CurrentMode { get; private set; }
        public int TrialFightsLeft { get; private set; }

        // --- Инициализация режима ---

        public void StartMode(GameMode mode) {
            CurrentMode = mode;
            TrialFightsLeft = mode == GameMode.Trial ? 3 : 0;
        }

        /// <summary>
        /// Вызвать после победы в бою.
        /// Возвращает true, если впереди ещё бои в текущем режиме (только Trial и Career).
        /// </summary>
        public bool OnFightWon(PlayerProfile p) {
            switch (CurrentMode) {
                case GameMode.Career:
                    p.ladderIndex++;
                    p.wins++;
                    return p.ladderIndex < 12;

                case GameMode.Trial:
                    p.wins++;
                    TrialFightsLeft--;
                    return TrialFightsLeft > 0;

                case GameMode.Training:
                    return false;

                default: // QuickFight
                    p.wins++;
                    return false;
            }
        }

        /// <summary>Вызвать после поражения.</summary>
        public void OnFightLost(PlayerProfile p) {
            if (CurrentMode == GameMode.Training) return;
            p.losses++;
            if (CurrentMode == GameMode.Trial) TrialFightsLeft = 0;
        }

        /// <summary>В режиме Trial мега запрещена.</summary>
        public bool MegaAllowed => CurrentMode != GameMode.Trial;

        // --- Следующий соперник ---

        /// <param name="fightIndex">
        /// Для QuickFight — выбранный индекс (0..ladderIndex).
        /// -1 = брать следующего по лестнице.
        /// </param>
        public FighterDef GetNextFighter(PlayerProfile p, RosterDef r, int fightIndex = -1) {
            int maxIdx = Mathf.Min(p.ladderIndex, r.Fighters.Length - 1);
            if (CurrentMode == GameMode.QuickFight && fightIndex >= 0)
                return r.Get(Mathf.Clamp(fightIndex, 0, maxIdx));
            return r.Get(Mathf.Max(0, maxIdx));
        }

        // --- Награда за бой ---

        // База за победу растёт с уровнем соперника: 100 за первую ступень, 210 за двенадцатую.
        public const int WinBase = 90, WinPerLevel = 10;
        public static int WinCoins(int foeLevel) => WinBase + WinPerLevel * Mathf.Max(1, foeLevel);

        // Точность боя: попадания / брошенные удары; -1 — ударов не было (тогда считается по истории).
        public static float Accuracy(int hitsThrown, int hitsLanded) =>
            hitsThrown > 0 ? Mathf.Clamp01((float)hitsLanded / hitsThrown) : -1f;

        /// <summary>
        /// Монеты за победу: база по уровню (WinCoins) + нокдауны×20 + точность%×0.5 + KO +50.
        /// Trial: ×2. Поражение: 0.
        /// accuracy — доля попаданий из всех брошенных ударов (0..1); меньше нуля — по history
        /// (история представления хранит только попадания, поэтому точность передаётся отдельно).
        /// </summary>
        public int ComputeReward(HitResult[] history, GameMode mode, int foeLevel = 1, float accuracy = -1f) {
            if (history == null || history.Length == 0) {
                int b = WinCoins(foeLevel);
                return mode == GameMode.Trial ? b * 2 : b;
            }

            int kds = 0, hits = 0;
            bool wasKO = false;
            foreach (var r in history) {
                if (r.isKnockdown) kds++;
                if (r.hit) hits++;
                if (r.isKO) wasKO = true;
            }

            float acc = accuracy >= 0f ? Mathf.Clamp01(accuracy) * 100f : (float)hits / history.Length * 100f;
            int reward = WinCoins(foeLevel)
                + kds * 20
                + (int)(acc * 0.5f)
                + (wasKO ? 50 : 0);

            if (mode == GameMode.Trial) reward *= 2;
            return reward;
        }

        /// <summary>HP, восстанавливаемые между боями в данном режиме.</summary>
        public float GetHpRestore(GameMode mode) {
            return mode == GameMode.Trial ? 5f : 45f;
        }
    }
}
