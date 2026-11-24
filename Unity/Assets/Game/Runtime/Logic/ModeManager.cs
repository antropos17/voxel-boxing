using UnityEngine;

namespace VoxGame {

    public enum GameMode { Career, QuickFight, Trial }

    /// <summary>
    /// Правила режимов и расчёт награды.
    /// Trial: 3 боя подряд, HP +5 между боями, монеты ×2, мега запрещена.
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

                default: // QuickFight
                    p.wins++;
                    return false;
            }
        }

        /// <summary>Вызвать после поражения.</summary>
        public void OnFightLost(PlayerProfile p) {
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

        /// <summary>
        /// Монеты за победу: base 100 + нокдауны×20 + точность%×0.5 + KO +50.
        /// Trial: ×2. Поражение: 0.
        /// </summary>
        public int ComputeReward(HitResult[] history, GameMode mode) {
            if (history == null || history.Length == 0) {
                return mode == GameMode.Trial ? 200 : 100;
            }

            int kds = 0, hits = 0;
            bool wasKO = false;
            foreach (var r in history) {
                if (r.isKnockdown) kds++;
                if (r.hit) hits++;
                if (r.isKO) wasKO = true;
            }

            float accuracy = (float)hits / history.Length * 100f;
            int reward = 100
                + kds * 20
                + (int)(accuracy * 0.5f)
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
