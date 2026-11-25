using NUnit.Framework;
using VoxGame;

namespace VoxGame.Tests {

    [TestFixture]
    public class EconomyTests {

        PlayerProfile NewProfile(int coins = 1000) {
            return new PlayerProfile { coins = coins };
        }

        // ==================== BuyUpgrade ====================

        [Test]
        public void BuyUpgrade_Ok_DeductsCoins() {
            var p = NewProfile(500);
            var result = p.BuyUpgrade(UpgradeType.Power);
            Assert.AreEqual(BuyResult.Ok, result);
            Assert.AreEqual(400, p.coins); // cost[0] = 100
            Assert.AreEqual(1, p.upgradeLevels[(int)UpgradeType.Power]);
        }

        [Test]
        public void BuyUpgrade_NotEnoughCoins_NoChange() {
            var p = NewProfile(50);
            int coinsBefore = p.coins;
            var result = p.BuyUpgrade(UpgradeType.Power);
            Assert.AreEqual(BuyResult.NotEnoughCoins, result);
            Assert.AreEqual(coinsBefore, p.coins, "монеты не должны изменяться при отказе");
            Assert.AreEqual(0, p.upgradeLevels[(int)UpgradeType.Power]);
        }

        [Test]
        public void BuyUpgrade_MaxLevel_NoChange() {
            var p = NewProfile(5000);
            // Поднять до MaxUpgradeLevel
            for (int i = 0; i < PlayerProfile.MaxUpgradeLevel; i++) {
                Assert.AreEqual(BuyResult.Ok, p.BuyUpgrade(UpgradeType.Speed));
            }
            int coinsAtMax = p.coins;
            var result = p.BuyUpgrade(UpgradeType.Speed);
            Assert.AreEqual(BuyResult.MaxLevel, result);
            Assert.AreEqual(coinsAtMax, p.coins, "монеты не должны изменяться при MaxLevel");
            Assert.AreEqual(PlayerProfile.MaxUpgradeLevel, p.upgradeLevels[(int)UpgradeType.Speed]);
        }

        [Test]
        public void BuyUpgrade_CostsAreIncreasing() {
            // 100 -> 300 -> 700
            var p = NewProfile(5000);
            int c0 = p.coins;
            p.BuyUpgrade(UpgradeType.Stamina); int cost0 = c0 - p.coins;
            int c1 = p.coins;
            p.BuyUpgrade(UpgradeType.Stamina); int cost1 = c1 - p.coins;
            int c2 = p.coins;
            p.BuyUpgrade(UpgradeType.Stamina); int cost2 = c2 - p.coins;
            Assert.AreEqual(100, cost0);
            Assert.AreEqual(300, cost1);
            Assert.AreEqual(700, cost2);
        }

        [Test]
        public void BuyUpgrade_CoinsNeverNegative() {
            var p = NewProfile(0);
            for (int i = 0; i < 20; i++)
                p.BuyUpgrade(UpgradeType.Defense);
            Assert.GreaterOrEqual(p.coins, 0);
        }

        // ==================== BuyGlove ====================

        [Test]
        public void BuyGlove_Ok_DeductsCoins() {
            var p = NewProfile(500);
            var result = p.BuyGlove("heavy"); // price=250
            Assert.AreEqual(BuyResult.Ok, result);
            Assert.AreEqual(250, p.coins);
            Assert.IsTrue(p.OwnsGlove("heavy"));
        }

        [Test]
        public void BuyGlove_NotEnoughCoins_NoChange() {
            var p = NewProfile(100);
            var result = p.BuyGlove("heavy"); // price=250
            Assert.AreEqual(BuyResult.NotEnoughCoins, result);
            Assert.AreEqual(100, p.coins);
            Assert.IsFalse(p.OwnsGlove("heavy"));
        }

        [Test]
        public void BuyGlove_AlreadyOwned_NoChange() {
            var p = NewProfile(1000);
            p.BuyGlove("heavy");
            int coinsBefore = p.coins;
            var result = p.BuyGlove("heavy");
            Assert.AreEqual(BuyResult.AlreadyOwned, result);
            Assert.AreEqual(coinsBefore, p.coins);
        }

        [Test]
        public void BuyGlove_Unknown_NotInCatalog() {
            var p = NewProfile(1000);
            var result = p.BuyGlove("несуществующие");
            Assert.AreEqual(BuyResult.NotInCatalog, result);
        }

        [Test]
        public void ClassicGlove_AlwaysOwned() {
            var p = new PlayerProfile();
            Assert.IsTrue(p.OwnsGlove("classic"));
        }

        // ==================== GetCombatStats ====================

        [Test]
        public void GetCombatStats_Default_AllOnes() {
            var p = new PlayerProfile();
            var stats = p.GetCombatStats();
            Assert.AreEqual(1f, stats.dmgMult, 0.001f);
            Assert.AreEqual(1f, stats.speedMult, 0.001f);
            Assert.AreEqual(1f, stats.staminaCostMult, 0.001f);
            Assert.AreEqual(1f, stats.megaGainMult, 0.001f);
            Assert.AreEqual(1f, stats.incomingDmgMult, 0.001f);
        }

        [Test]
        public void GetCombatStats_PowerUpgrade_IncreasesDmg() {
            var p = new PlayerProfile { coins = 500 };
            p.BuyUpgrade(UpgradeType.Power);
            var stats = p.GetCombatStats();
            Assert.Greater(stats.dmgMult, 1f);
        }

        [Test]
        public void GetCombatStats_ArmorGloves_ReducesIncoming() {
            var p = new PlayerProfile { coins = 2000 };
            // Принудительно выдать перчатки без покупки для теста
            p.boughtGloveIds.Add("armor");
            p.EquipGlove("armor");
            var stats = p.GetCombatStats();
            Assert.Less(stats.incomingDmgMult, 1f);
        }

        // ==================== AddXp / LevelUp ====================

        [Test]
        public void AddXp_LevelUp_IncrementsLevel() {
            var p = new PlayerProfile { level = 1, xp = 0 };
            int gained = p.AddXp(PlayerProfile.XpForNextLevel(1));
            Assert.AreEqual(1, gained);
            Assert.AreEqual(2, p.level);
        }

        [Test]
        public void AddXp_NotEnough_NoLevelUp() {
            var p = new PlayerProfile { level = 1, xp = 0 };
            int gained = p.AddXp(PlayerProfile.XpForNextLevel(1) - 1);
            Assert.AreEqual(0, gained);
            Assert.AreEqual(1, p.level);
        }

        [Test]
        public void AddXp_MultiLevel_InOneBatch() {
            var p = new PlayerProfile { level = 1 };
            int xpNeeded = PlayerProfile.XpForNextLevel(1) + PlayerProfile.XpForNextLevel(2);
            int gained = p.AddXp(xpNeeded);
            Assert.AreEqual(2, gained);
            Assert.AreEqual(3, p.level);
        }

        [Test]
        public void AddXp_AtMaxLevel_DoesNotExceed() {
            var p = new PlayerProfile { level = PlayerProfile.MaxPlayerLevel };
            p.AddXp(999999);
            Assert.AreEqual(PlayerProfile.MaxPlayerLevel, p.level);
        }

        // ==================== ModeManager.ComputeReward ====================

        [Test]
        public void ComputeReward_BaseCoins_NoHistory() {
            var mgr = new ModeManager();
            mgr.StartMode(GameMode.Career);
            int reward = mgr.ComputeReward(null, GameMode.Career);
            Assert.AreEqual(100, reward);
        }

        [Test]
        public void ComputeReward_KnockdownBonus() {
            var mgr = new ModeManager();
            var history = new HitResult[] {
                new HitResult { hit = true, isKnockdown = true },
                new HitResult { hit = true, isKnockdown = true },
            };
            int reward = mgr.ComputeReward(history, GameMode.Career);
            // 100 + 2*20 + 100%*0.5 = 100+40+50 = 190
            Assert.AreEqual(190, reward);
        }

        [Test]
        public void ComputeReward_KOBonus() {
            var mgr = new ModeManager();
            var history = new HitResult[] {
                new HitResult { hit = true, isKO = true },
            };
            int reward = mgr.ComputeReward(history, GameMode.Career);
            // 100 + 0 + 100%*0.5 + 50 = 200
            Assert.AreEqual(200, reward);
        }

        [Test]
        public void ComputeReward_Trial_Doubled() {
            var mgr = new ModeManager();
            int reward = mgr.ComputeReward(null, GameMode.Trial);
            Assert.AreEqual(200, reward); // 100 * 2
        }

        [Test]
        public void ComputeReward_ZeroAccuracy() {
            var mgr = new ModeManager();
            var history = new HitResult[] {
                new HitResult { hit = false },
                new HitResult { hit = false },
            };
            int reward = mgr.ComputeReward(history, GameMode.Career);
            Assert.AreEqual(100, reward); // base, 0% accuracy, no kd, no ko
        }

        // ==================== EconomyState.Apply ====================

        [Test]
        public void EconomyState_Apply_AddsCoinAndXp() {
            var p = new PlayerProfile { coins = 0, xp = 0, level = 1 };
            var reward = EconomyState.Apply(p, 100, 10);
            Assert.AreEqual(100, p.coins);
            Assert.AreEqual(100, reward.coins);
            Assert.AreEqual(10, reward.xp);
        }

        [Test]
        public void EconomyState_Apply_LevelUp_Reported() {
            var p = new PlayerProfile { level = 1, xp = 0 };
            int xp = PlayerProfile.XpForNextLevel(1);
            var reward = EconomyState.Apply(p, 0, xp);
            Assert.AreEqual(1, reward.levelsGained);
            Assert.IsTrue(reward.LeveledUp);
        }

        // ==================== GetHpRestore ====================

        [Test]
        public void GetHpRestore_Career_Returns45() {
            var mgr = new ModeManager();
            Assert.AreEqual(45f, mgr.GetHpRestore(GameMode.Career));
        }

        [Test]
        public void GetHpRestore_Trial_Returns5() {
            var mgr = new ModeManager();
            Assert.AreEqual(5f, mgr.GetHpRestore(GameMode.Trial));
        }
    }
}
