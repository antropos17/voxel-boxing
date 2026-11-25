using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxGame {

    public enum UpgradeType { Power, Speed, Stamina, Defense, MegaCharge }

    public enum BuyResult { Ok, NotEnoughCoins, MaxLevel, NotInCatalog, AlreadyOwned }

    // Шины громкости из настроек: общая, эффекты боя и кнопки, толпа.
    public enum VolumeKind { Master, Sfx, Crowd }

    /// <summary>Итоговые боевые параметры = улучшения × перчатки.</summary>
    public struct PlayerCombatStats {
        /// <summary>Множитель урона исходящих ударов.</summary>
        public float dmgMult;
        /// <summary>Коэффициент скорости замаха; windupTime = base / speedMult (&gt;1 = быстрее).</summary>
        public float speedMult;
        /// <summary>Множитель расхода выносливости на удар.</summary>
        public float staminaCostMult;
        /// <summary>Множитель набора меги.</summary>
        public float megaGainMult;
        /// <summary>Множитель входящего урона (< 1 = больше защиты).</summary>
        public float incomingDmgMult;
    }

    /// <summary>Профиль игрока. megaMeter не здесь — он в FightState.</summary>
    public class PlayerProfile {

        public string playerName = "Игрок";
        public int level = 1;
        public int xp;
        public int coins;
        public int wins;
        public int losses;
        public int beltCount;
        public int ladderIndex;
        public int[] upgradeLevels = new int[5]; // индекс = UpgradeType, макс. 3
        public string equippedGloveId = "classic";
        public List<string> boughtGloveIds = new List<string> { "classic" };
        public bool soundEnabled = true;
        public bool vibrationEnabled = true;
        public float masterVolume = 1f;   // 0..1
        public float sfxVolume = 1f;
        public float crowdVolume = 1f;

        public float GetVolume(VolumeKind k) =>
            k == VolumeKind.Master ? masterVolume : k == VolumeKind.Sfx ? sfxVolume : crowdVolume;

        public void SetVolume(VolumeKind k, float v) {
            v = SaveService.Volume01(v);
            if (k == VolumeKind.Master) masterVolume = v;
            else if (k == VolumeKind.Sfx) sfxVolume = v;
            else crowdVolume = v;
        }

        // Уровни и стоимости улучшений
        public static readonly int[] UpgradeCosts = { 100, 300, 700 };
        public const int MaxUpgradeLevel = 3;
        public const int MaxPlayerLevel = 20;

        // Множители от улучшений [уровень 0..3]
        static readonly float[] DmgBonus     = { 1.00f, 1.10f, 1.20f, 1.35f };
        // speedMult — коэффициент скорости (делитель времени): выше = быстрее замах
        static readonly float[] SpeedBonus   = { 1.00f, 1.10f, 1.20f, 1.30f };
        static readonly float[] StaminaBonus = { 1.00f, 0.87f, 0.77f, 0.67f }; // cost multiplier
        static readonly float[] DefenseBonus = { 1.00f, 0.85f, 0.75f, 0.60f }; // incoming dmg
        static readonly float[] MegaBonus    = { 1.00f, 1.20f, 1.40f, 1.60f };

        // --- Итоговые множители ---

        /// <summary>Итоговые боевые параметры = улучшения × оснащённые перчатки.</summary>
        public PlayerCombatStats GetCombatStats() {
            var glove = GlovesCatalog.Get(equippedGloveId);
            return new PlayerCombatStats {
                dmgMult         = GetDmgMult()      * glove.dmgMult,
                speedMult       = GetSpeedMult()    * glove.speedMult,
                staminaCostMult = GetStaminaMult()  * glove.staminaCostMult,
                megaGainMult    = GetMegaGainMult() * glove.megaGainMult,
                incomingDmgMult = GetDefenseMult()  * glove.incomingDmgMult,
            };
        }

        // Контрактные методы — только вклад улучшений (без перчаток).
        public float GetDmgMult()       => DmgBonus    [upgradeLevels[(int)UpgradeType.Power]];
        public float GetSpeedMult()     => SpeedBonus  [upgradeLevels[(int)UpgradeType.Speed]];
        public float GetStaminaMult()   => StaminaBonus[upgradeLevels[(int)UpgradeType.Stamina]];
        public float GetDefenseMult()   => DefenseBonus[upgradeLevels[(int)UpgradeType.Defense]];
        public float GetMegaGainMult()  => MegaBonus   [upgradeLevels[(int)UpgradeType.MegaCharge]];

        // --- Покупка улучшений ---

        /// <summary>
        /// Купить следующий уровень улучшения.
        /// Снимает монеты с profile.coins. Не бросает исключений.
        /// </summary>
        public BuyResult BuyUpgrade(UpgradeType t) {
            int idx = (int)t;
            int curLevel = upgradeLevels[idx];
            if (curLevel >= MaxUpgradeLevel) return BuyResult.MaxLevel;
            int cost = UpgradeCosts[curLevel];
            if (coins < cost) return BuyResult.NotEnoughCoins;
            coins -= cost;
            upgradeLevels[idx] = curLevel + 1;
            return BuyResult.Ok;
        }

        // --- Перчатки ---

        public bool OwnsGlove(string id) => boughtGloveIds.Contains(id);

        /// <summary>Купить перчатку. Снимает монеты. Не бросает исключений.</summary>
        public BuyResult BuyGlove(string id) {
            int idx = Array.FindIndex(GlovesCatalog.All, g => g.id == id);
            if (idx < 0) return BuyResult.NotInCatalog;
            if (OwnsGlove(id)) return BuyResult.AlreadyOwned;
            int price = GlovesCatalog.All[idx].price;
            if (coins < price) return BuyResult.NotEnoughCoins;
            coins -= price;
            boughtGloveIds.Add(id);
            return BuyResult.Ok;
        }

        /// <summary>Надеть перчатку. Возвращает false если не куплена.</summary>
        public bool EquipGlove(string id) {
            if (!OwnsGlove(id)) return false;
            equippedGloveId = id;
            return true;
        }

        // --- Опыт и уровни ---

        /// <summary>XP, необходимый для перехода с level на level+1.</summary>
        public static int XpForNextLevel(int level) => 200 * level;

        /// <summary>
        /// Добавить XP и обработать повышение уровня.
        /// Возвращает количество уровней, на которые поднялись.
        /// </summary>
        public int AddXp(int amount) {
            xp += amount;
            int levelsGained = 0;
            while (level < MaxPlayerLevel) {
                int needed = XpForNextLevel(level);
                if (xp < needed) break;
                xp -= needed;
                level++;
                levelsGained++;
            }
            return levelsGained;
        }

        // --- Сериализация ---

        /// <summary>Восстановить профиль из SaveData с клампингом и дефолтами.</summary>
        public void LoadFrom(SaveData d) {
            playerName       = string.IsNullOrEmpty(d.playerName) ? "Игрок" : d.playerName;
            level            = Mathf.Max(1, d.playerLevel);
            xp               = Mathf.Max(0, d.xp);
            coins            = Mathf.Max(0, d.coins);
            wins             = Mathf.Max(0, d.wins);
            losses           = Mathf.Max(0, d.losses);
            beltCount        = Mathf.Max(0, d.beltCount);
            ladderIndex      = Mathf.Max(0, d.ladderIndex);
            soundEnabled     = d.soundEnabled;
            vibrationEnabled = d.vibrationEnabled;
            masterVolume     = SaveService.Volume01(d.masterVolume);
            sfxVolume        = SaveService.Volume01(d.sfxVolume);
            crowdVolume      = SaveService.Volume01(d.crowdVolume);
            equippedGloveId  = string.IsNullOrEmpty(d.equippedGloveId) ? "classic" : d.equippedGloveId;

            if (d.upgradeLevels != null && d.upgradeLevels.Length == 5) {
                for (int i = 0; i < 5; i++)
                    upgradeLevels[i] = Mathf.Clamp(d.upgradeLevels[i], 0, MaxUpgradeLevel);
            } else {
                Array.Clear(upgradeLevels, 0, 5);
            }

            boughtGloveIds.Clear();
            if (d.boughtGloveIds != null) {
                foreach (var id in d.boughtGloveIds) {
                    if (!string.IsNullOrEmpty(id) && !boughtGloveIds.Contains(id))
                        boughtGloveIds.Add(id);
                }
            }
            if (!boughtGloveIds.Contains("classic"))
                boughtGloveIds.Add("classic");
        }

        /// <summary>Скопировать профиль в SaveData для сохранения.</summary>
        public void SaveTo(SaveData d) {
            d.playerName      = playerName;
            d.playerLevel     = level;
            d.xp              = xp;
            d.coins           = coins;
            d.wins            = wins;
            d.losses          = losses;
            d.beltCount       = beltCount;
            d.ladderIndex     = ladderIndex;
            d.soundEnabled    = soundEnabled;
            d.vibrationEnabled = vibrationEnabled;
            d.masterVolume    = masterVolume;
            d.sfxVolume       = sfxVolume;
            d.crowdVolume     = crowdVolume;
            d.equippedGloveId = equippedGloveId;
            d.upgradeLevels   = (int[])upgradeLevels.Clone();
            d.boughtGloveIds  = boughtGloveIds.ToArray();
        }
    }
}
