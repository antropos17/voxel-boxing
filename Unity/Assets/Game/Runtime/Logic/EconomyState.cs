using System;
using UnityEngine;

namespace VoxGame {

    /// <summary>Данные сохранения. Все поля по умолчанию — корректный стартовый профиль.</summary>
    [Serializable]
    public class SaveData {
        public int version = SaveService.CurrentVersion;
        public string playerName = "Игрок";
        public int playerLevel = 1;
        public int xp;
        public int coins;
        public int wins;
        public int losses;
        public int beltCount;
        public int ladderIndex;
        public int[] upgradeLevels = new int[5];
        public string equippedGloveId = "classic";
        public string[] boughtGloveIds = new string[] { "classic" };
        public bool soundEnabled = true;
        public bool vibrationEnabled = true;
        // с версии 2: громкости 0..1 (в старых файлах полей нет — SaveService.Migrate ставит 1)
        public float masterVolume = 1f;
        public float sfxVolume = 1f;
        public float crowdVolume = 1f;
    }

    /// <summary>Результат боя для экрана итогов.</summary>
    public struct FightReward {
        public int coins;
        public int xp;
        public int levelsGained;
        public bool LeveledUp => levelsGained > 0;
    }

    /// <summary>
    /// Применение наград к профилю игрока.
    /// Расчёт монет — через ModeManager.ComputeReward.
    /// XP = max(10, coins / 2). Поражение: 0 монет, 10 XP.
    /// </summary>
    public static class EconomyState {

        /// <summary>
        /// Начислить coins и xp профилю, обработать повышение уровня.
        /// </summary>
        public static FightReward Apply(PlayerProfile profile, int coins, int xp) {
            profile.coins += coins;
            int levelsGained = profile.AddXp(xp);
            return new FightReward {
                coins = coins,
                xp = xp,
                levelsGained = levelsGained,
            };
        }

        /// <summary>
        /// Вычислить и применить награду за бой. Won=false → 0 монет.
        /// </summary>
        public static FightReward ApplyFightResult(
                PlayerProfile profile, HitResult[] history, bool won, ModeManager mgr) {
            int coins = won ? mgr.ComputeReward(history, mgr.CurrentMode) : 0;
            int xp = won ? Mathf.Max(10, coins / 2) : 10;
            return Apply(profile, coins, xp);
        }
    }
}
