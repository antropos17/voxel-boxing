// GameFlowShopSaveTests.cs — магазин до предела, награда и рост уровня, сохранение: точный круг, старый формат,
// битый файл, сбой записи. Чистая логика, без сцены.
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VoxGame.Tests
{
    [TestFixture]
    public class GameFlowShopSaveTests
    {
        string _dir, _path;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "voxshop_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "save.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        GameFlow NewFlow(uint seed = 11u)
        {
            var f = new GameFlow(_path, seed);
            f.Boot();
            return f;
        }

        static FightStats Win()
        {
            var h = new HitResult[4];
            for (int i = 0; i < h.Length; i++) h[i] = new HitResult { hit = true };
            h[3].isKO = true;
            return new FightStats { time = 30f, knockdowns = 0, hitsThrown = 4, hitsLanded = 4, history = h };
        }

        static void WinFight(GameFlow f)
        {
            f.FightLoaded();
            f.FightDecided();
            Assert.IsTrue(f.FightEnded(true, Win()));
        }

        // ------------------------------------------------------------------ Магазин

        [Test]
        public void Upgrades_ToLimit_WithGrowingCost()
        {
            var f = NewFlow();
            int total = 0;
            foreach (var c in PlayerProfile.UpgradeCosts) total += c;
            f.Profile.coins = total + 5;
            f.OpenShop();

            int prev = 0;
            for (int lv = 0; lv < PlayerProfile.MaxUpgradeLevel; lv++)
            {
                int cost = f.UpgradeCost(UpgradeType.Speed);
                Assert.Greater(cost, prev, "цена растёт с уровнем");
                prev = cost;
                Assert.AreEqual(ShopResult.Ok, f.BuyUpgrade(UpgradeType.Speed));
                Assert.AreEqual(lv + 1, f.Profile.upgradeLevels[(int)UpgradeType.Speed]);
            }
            Assert.AreEqual(5, f.Profile.coins);
            Assert.AreEqual(-1, f.UpgradeCost(UpgradeType.Speed));
            Assert.AreEqual(ShopResult.MaxLevel, f.BuyUpgrade(UpgradeType.Speed));
            Assert.AreEqual(5, f.Profile.coins, "на пределе монеты не списываются");

            // улучшение реально попадает в боевые параметры
            Assert.Greater(f.Profile.GetCombatStats().speedMult, 1.2f);
            var g = new GameFlow(_path, 1u);
            Assert.AreEqual(PlayerProfile.MaxUpgradeLevel, g.Profile.upgradeLevels[(int)UpgradeType.Speed]);
        }

        [Test]
        public void BuyAndEquip_ChangesCombatStats_AndPersists()
        {
            var f = NewFlow();
            f.Profile.coins = 249;
            f.OpenShop();
            Assert.AreEqual(ShopResult.NotEnoughCoins, f.BuyGlove("heavy"));
            Assert.AreEqual(249, f.Profile.coins);
            Assert.IsFalse(f.Profile.OwnsGlove("heavy"));

            f.Profile.coins = 250;
            float dmgBefore = f.Profile.GetCombatStats().dmgMult;
            Assert.AreEqual(ShopResult.Ok, f.BuyGlove("heavy"));
            Assert.AreEqual(0, f.Profile.coins);
            Assert.AreEqual("classic", f.Profile.equippedGloveId, "покупка не надевает сама");
            Assert.AreEqual(ShopResult.Ok, f.EquipGlove("heavy"));
            Assert.AreNotEqual(dmgBefore, f.Profile.GetCombatStats().dmgMult, "перчатки меняют параметры боя");

            var g = new GameFlow(_path, 3u);
            Assert.AreEqual("heavy", g.Profile.equippedGloveId);
            Assert.AreEqual(f.Profile.GetCombatStats().dmgMult, g.Profile.GetCombatStats().dmgMult, 1e-6f);

            // все 8 перчаток каталога различимы по id и цене
            Assert.AreEqual(8, GlovesCatalog.All.Length);
        }

        [Test]
        public void Shop_NotOpen_RejectsEverything()
        {
            var f = NewFlow();
            f.Profile.coins = 10000;
            Assert.AreEqual(ShopResult.Unavailable, f.BuyGlove("heavy"));
            Assert.AreEqual(ShopResult.Unavailable, f.EquipGlove("classic"));
            Assert.AreEqual(ShopResult.Unavailable, f.BuyUpgrade(UpgradeType.Power));
            Assert.AreEqual(10000, f.Profile.coins);
        }

        // ------------------------------------------------------------------ Награда и уровень

        [Test]
        public void Reward_LevelsUp_WhenXpCrossesThreshold()
        {
            var f = NewFlow();
            f.Profile.xp = PlayerProfile.XpForNextLevel(1) - 5;
            f.Play(); f.StartQuick();
            WinFight(f);
            Assert.AreEqual(AppState.Reward, f.State);
            var r = f.LastReward;
            Assert.IsTrue(r.won);
            Assert.GreaterOrEqual(r.xp, 10);
            Assert.AreEqual(1, r.levelsGained);
            Assert.AreEqual(2, r.newLevel);
            Assert.AreEqual(2, f.Profile.level);
            Assert.AreEqual(r.coins, f.Profile.coins);
            Assert.AreEqual(2, SaveService.Load(_path).playerLevel, "награда сохранена сразу");
        }

        // ------------------------------------------------------------------ Сохранение

        [Test]
        public void SaveLoad_RoundTrip_Identical()
        {
            var f = NewFlow();
            var p = f.Profile;
            p.playerName = "Проверка";
            p.coins = 1234; p.xp = 77; p.level = 4; p.wins = 9; p.losses = 3; p.beltCount = 1; p.ladderIndex = 6;
            p.upgradeLevels = new[] { 3, 2, 1, 0, 2 };
            p.boughtGloveIds.Add("speed");
            p.equippedGloveId = "speed";
            p.soundEnabled = false; p.vibrationEnabled = false;
            p.masterVolume = 0.6f; p.sfxVolume = 0.8f; p.crowdVolume = 0.2f;
            f.Save();

            var d1 = new SaveData();
            p.SaveTo(d1);
            var g = new GameFlow(_path, 5u);
            var d2 = new SaveData();
            g.Profile.SaveTo(d2);
            Assert.AreEqual(JsonUtility.ToJson(d1), JsonUtility.ToJson(d2));
            Assert.AreEqual(SaveService.CurrentVersion, SaveService.Load(_path).version);
        }

        [Test]
        public void OldSave_WithoutVolumes_LoadsFullVolume()
        {
            // формат версии 1: полей громкости нет
            File.WriteAllText(_path,
                "{\"version\":1,\"playerName\":\"Старый\",\"playerLevel\":3,\"coins\":420,\"ladderIndex\":2," +
                "\"soundEnabled\":false,\"vibrationEnabled\":true,\"equippedGloveId\":\"classic\"," +
                "\"boughtGloveIds\":[\"classic\"],\"upgradeLevels\":[1,0,0,0,0]}");
            var f = NewFlow();
            Assert.AreEqual(420, f.Profile.coins);
            Assert.AreEqual(3, f.Profile.level);
            Assert.AreEqual(2, f.Profile.ladderIndex);
            Assert.IsFalse(f.Profile.soundEnabled, "вкл/выкл звука из старого файла сохранён");
            Assert.AreEqual(1f, f.Profile.masterVolume, 1e-6f);
            Assert.AreEqual(1f, f.Profile.sfxVolume, 1e-6f);
            Assert.AreEqual(1f, f.Profile.crowdVolume, 1e-6f);

            f.Save();
            var d = SaveService.Load(_path);
            Assert.AreEqual(SaveService.CurrentVersion, d.version);
            Assert.AreEqual(1f, d.masterVolume, 1e-6f);
            Assert.AreEqual(420, d.coins);
        }

        [Test]
        public void Volumes_GarbageInFile_AreClamped()
        {
            File.WriteAllText(_path,
                "{\"version\":2,\"playerLevel\":1,\"masterVolume\":7.5,\"sfxVolume\":-3,\"crowdVolume\":0.5}");
            var f = NewFlow();
            Assert.AreEqual(1f, f.Profile.masterVolume, 1e-6f);
            Assert.AreEqual(0f, f.Profile.sfxVolume, 1e-6f);
            Assert.AreEqual(0.5f, f.Profile.crowdVolume, 1e-6f);
        }

        [Test]
        public void CorruptSave_DoesNotThrow_KeepsCopy_AndNextSaveWorks()
        {
            File.WriteAllText(_path, "{ это не JSON ]]]");
            GameFlow f = null;
            Assert.DoesNotThrow(() => f = NewFlow());
            Assert.AreEqual(AppState.MainMenu, f.State);
            Assert.AreEqual(0, f.Profile.coins);
            Assert.AreEqual(1, f.Profile.level);
            Assert.IsTrue(File.Exists(_path + ".bad"), "битый файл отложен рядом");

            f.Profile.coins = 50;
            f.Save();
            Assert.IsFalse(f.LastSaveFailed);
            Assert.AreEqual(50, SaveService.Load(_path).coins);
        }

        [Test]
        public void SaveFailure_DoesNotThrow()
        {
            // путь сохранения указывает на папку — запись невозможна
            string dirAsFile = Path.Combine(_dir, "folder");
            Directory.CreateDirectory(dirAsFile);
            var f = new GameFlow(dirAsFile, 1u);
            f.Boot();
            f.Profile.coins = 300;
            f.OpenShop();
            ShopResult r = ShopResult.Unavailable;
            Assert.DoesNotThrow(() => r = f.BuyGlove("heavy"));
            Assert.AreEqual(ShopResult.Ok, r, "покупка в памяти прошла");
            Assert.IsTrue(f.LastSaveFailed);
        }

        // ------------------------------------------------------------------ Ступени громкости на экране настроек

        [Test]
        public void VolumeSteps_RoundTrip()
        {
            for (int lv = 0; lv <= SettingsScreen.Steps; lv++)
                Assert.AreEqual(lv, SettingsScreen.ToLevel(SettingsScreen.ToVolume(lv)));
            Assert.AreEqual(SettingsScreen.Steps, SettingsScreen.ToLevel(1f));
            Assert.AreEqual(0, SettingsScreen.ToLevel(0f));
        }
    }
}
