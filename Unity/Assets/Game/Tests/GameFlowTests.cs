// GameFlowTests.cs — машина состояний приложения: переходы, награды, лестница, магазин, сохранение. Без сцены и Play Mode.
// Победа ведёт сразу к награде, поражение — к экрану итогов.
using System.IO;
using NUnit.Framework;

namespace VoxGame.Tests
{
    [TestFixture]
    public class GameFlowTests
    {
        string _dir, _path;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "voxflow_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "save.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        GameFlow NewFlow(uint seed = 7u)
        {
            var f = new GameFlow(_path, seed);
            f.Boot();
            return f;
        }

        // Победа с нокаутом и одним нокдауном; все удары попали.
        static FightStats WinStats()
        {
            var h = new HitResult[6];
            for (int i = 0; i < h.Length; i++) h[i] = new HitResult { hit = true };
            h[3].isKnockdown = true;
            h[5].isKO = true;
            return new FightStats { time = 40f, knockdowns = 1, hitsThrown = 6, hitsLanded = 6, history = h };
        }

        static FightStats LossStats(int kds = 0) =>
            new FightStats { time = 30f, knockdowns = kds, hitsThrown = 3, hitsLanded = 1, history = new HitResult[0] };

        // Довести бой до конца: загрузка → бой → развязка → награда (победа) или итоги (поражение).
        static void PlayOut(GameFlow f, bool won)
        {
            Assert.AreEqual(AppState.Loading, f.State);
            Assert.IsTrue(f.FightLoaded());
            Assert.IsTrue(f.FightDecided());
            Assert.AreEqual(AppState.Victory, f.State);
            Assert.IsTrue(f.FightEnded(won, won ? WinStats() : LossStats(1)));
            Assert.AreEqual(won ? AppState.Reward : AppState.Result, f.State);
        }

        // ------------------------------------------------------------------ Переходы

        [Test]
        public void Boot_GoesToMainMenu_Once()
        {
            var f = new GameFlow(_path, 1u);
            Assert.AreEqual(AppState.Boot, f.State);
            Assert.IsTrue(f.Boot());
            Assert.AreEqual(AppState.MainMenu, f.State);
            Assert.IsFalse(f.Boot());
        }

        [Test]
        public void ValidTransitions_MenuToReward()
        {
            var f = NewFlow();
            Assert.IsTrue(f.Play());                Assert.AreEqual(AppState.ModeSelect, f.State);
            Assert.IsTrue(f.Back());                Assert.AreEqual(AppState.MainMenu, f.State);
            Assert.IsTrue(f.Play());
            Assert.IsTrue(f.StartCareer());         Assert.AreEqual(AppState.Roster, f.State);
            Assert.IsTrue(f.Back());                Assert.AreEqual(AppState.ModeSelect, f.State);
            Assert.IsTrue(f.StartCareer());
            Assert.IsTrue(f.PickFighter(0));        Assert.AreEqual(AppState.Loading, f.State);
            Assert.IsTrue(f.FightLoaded());         Assert.AreEqual(AppState.Fight, f.State);
            Assert.IsTrue(f.Pause());               Assert.AreEqual(AppState.Pause, f.State);
            Assert.IsTrue(f.OpenSettings());        Assert.AreEqual(AppState.Settings, f.State);
            Assert.IsTrue(f.Back());                Assert.AreEqual(AppState.Pause, f.State);
            Assert.IsTrue(f.Resume());              Assert.AreEqual(AppState.Fight, f.State);
            Assert.IsTrue(f.FightDecided());        Assert.AreEqual(AppState.Victory, f.State);
            Assert.IsTrue(f.FightEnded(true, WinStats())); Assert.AreEqual(AppState.Reward, f.State);
            Assert.IsTrue(f.Continue());            Assert.AreEqual(AppState.Roster, f.State);
        }

        [Test]
        public void MenuBranches_PlayerSettingsShop_ReturnToMenu()
        {
            var f = NewFlow();
            Assert.IsTrue(f.OpenPlayer());   Assert.AreEqual(AppState.Player, f.State);
            Assert.IsTrue(f.Back());         Assert.AreEqual(AppState.MainMenu, f.State);
            Assert.IsTrue(f.OpenSettings()); Assert.AreEqual(AppState.Settings, f.State);
            Assert.IsTrue(f.Back());         Assert.AreEqual(AppState.MainMenu, f.State);
            Assert.IsTrue(f.OpenShop());     Assert.AreEqual(AppState.Shop, f.State);
            Assert.IsTrue(f.Back());         Assert.AreEqual(AppState.MainMenu, f.State);
            Assert.IsTrue(f.OpenPlayer());
            Assert.IsTrue(f.OpenShop());     Assert.AreEqual(AppState.Shop, f.State);
            Assert.IsTrue(f.Back());         Assert.AreEqual(AppState.Player, f.State);
        }

        [Test]
        public void InvalidCommands_AreIgnored()
        {
            var f = new GameFlow(_path, 3u);
            Assert.IsFalse(f.Play(), "до загрузки меню играть нельзя");
            Assert.AreEqual(AppState.Boot, f.State);
            f.Boot();

            int changes = 0;
            f.OnStateChanged += (a, b) => changes++;
            Assert.IsFalse(f.FightLoaded());
            Assert.IsFalse(f.Resume());
            Assert.IsFalse(f.Pause());
            Assert.IsFalse(f.Continue());
            Assert.IsFalse(f.Rematch());
            Assert.IsFalse(f.StartCareer());
            Assert.IsFalse(f.PickFighter(0));
            Assert.IsFalse(f.FightEnded(true, WinStats()));
            Assert.IsFalse(f.QuitToMenu());
            Assert.IsFalse(f.Back());
            Assert.AreEqual(ShopResult.Unavailable, f.BuyUpgrade(UpgradeType.Power));
            Assert.AreEqual(AppState.MainMenu, f.State);
            Assert.AreEqual(0, changes);

            // закрытый боец лестницы и выход за пределы
            f.Play(); f.StartCareer();
            Assert.IsFalse(f.PickFighter(5));
            Assert.IsFalse(f.PickFighter(-1));
            Assert.IsFalse(f.PickFighter(RosterDef.Count));
            Assert.AreEqual(AppState.Roster, f.State);

            // в бою нельзя в магазин и нельзя дважды решить исход
            f.PickFighter(0); f.FightLoaded();
            Assert.IsFalse(f.OpenShop());
            Assert.IsFalse(f.Continue());
            f.FightDecided();
            Assert.IsFalse(f.FightDecided());
            Assert.IsFalse(f.Pause(), "в развязке паузы нет");
        }

        // ------------------------------------------------------------------ Награды и лестница

        [Test]
        public void Win_AdvancesLadder_GrantsCoins_AndSaves()
        {
            var f = NewFlow();
            f.Play(); f.StartCareer(); f.PickFighter(0);
            PlayOut(f, true);

            var stats = WinStats();
            int expected = f.Modes.ComputeReward(stats.history, GameMode.Career);
            Assert.AreEqual(1, f.Profile.ladderIndex);
            Assert.AreEqual(1, f.Profile.wins);
            Assert.AreEqual(expected, f.Profile.coins);
            Assert.AreEqual(expected, f.LastReward.coins);
            Assert.IsTrue(f.LastReward.ladderAdvanced);
            Assert.AreEqual(System.Math.Max(10, expected / 2), f.LastReward.xp);

            // разбивка сходится с итогом
            int sum = 0;
            foreach (var l in f.LastReward.lines) sum += l.value;
            Assert.AreEqual(expected, sum);

            // следующий соперник назван
            var next = f.Roster.Get(1);
            StringAssert.Contains(next.name, f.LastReward.nextFoe);

            var d = SaveService.Load(_path);
            Assert.AreEqual(1, d.ladderIndex);
            Assert.AreEqual(expected, d.coins);
            Assert.AreEqual(1, d.wins);
        }

        [Test]
        public void Loss_GivesConsolation_KeepsLadder_RematchSameFoe()
        {
            var f = NewFlow();
            f.Play(); f.StartCareer(); f.PickFighter(0);
            string foe = f.Foe.name;
            PlayOut(f, false);

            Assert.AreEqual(0, f.Profile.ladderIndex);
            Assert.AreEqual(1, f.Profile.losses);
            Assert.AreEqual(GameFlow.ConsolationBase + GameFlow.ConsolationPerKd, f.Profile.coins);
            Assert.IsFalse(f.LastReward.won);
            Assert.AreEqual(GameFlow.LossXp, f.LastReward.xp);

            // «ЕЩЁ РАЗ» прямо с итогов — тот же соперник
            Assert.AreEqual(AppState.Result, f.State);
            Assert.IsTrue(f.Rematch());
            Assert.AreEqual(AppState.Loading, f.State);
            Assert.AreEqual(foe, f.Foe.name);
            Assert.AreEqual(0, f.FoeLadderIndex);

            // «В МЕНЮ» с итогов
            PlayOut(f, false);
            Assert.IsTrue(f.QuitToMenu());
            Assert.AreEqual(AppState.MainMenu, f.State);
            Assert.AreEqual(2, f.Profile.losses);
        }

        [Test]
        public void ReplayOfDefeatedFoe_DoesNotSkipLadder()
        {
            var f = NewFlow();
            f.Play(); f.StartCareer(); f.PickFighter(0);
            PlayOut(f, true);
            f.Continue();
            Assert.AreEqual(AppState.Roster, f.State);

            Assert.IsTrue(f.PickFighter(0), "побеждённого можно вызвать снова");
            PlayOut(f, true);
            Assert.AreEqual(1, f.Profile.ladderIndex);
            Assert.AreEqual(2, f.Profile.wins);
            Assert.IsFalse(f.LastReward.ladderAdvanced);
        }

        [Test]
        public void TitleFight_GivesBelt()
        {
            var f = NewFlow();
            f.Profile.ladderIndex = RosterDef.Count - 1;
            f.Play(); f.StartCareer();
            Assert.IsTrue(f.PickFighter(RosterDef.Count - 1));
            Assert.AreEqual(RosterDef.Count - 1, f.FightLadderIndex);
            Assert.AreEqual(RosterDef.Count, f.FightLadderCount);
            PlayOut(f, true);
            Assert.AreEqual(1, f.Profile.beltCount);
            Assert.IsTrue(f.LastReward.belt);
            Assert.AreEqual(RosterDef.Count, f.Profile.ladderIndex);
            Assert.AreEqual("", f.LastReward.nextFoe);
        }

        [Test]
        public void Surrender_FromPause_CountsAsLoss()
        {
            var f = NewFlow();
            f.Play(); f.StartQuick(); f.FightLoaded(); f.Pause();
            Assert.IsTrue(f.QuitToMenu());
            Assert.AreEqual(AppState.MainMenu, f.State);
            Assert.AreEqual(1, f.Profile.losses);
            Assert.AreEqual(1, SaveService.Load(_path).losses);
        }

        [Test]
        public void Trial_ThreeWins_ThenMenu_WithDifferentFoes()
        {
            var f = NewFlow();
            f.Play();
            Assert.IsTrue(f.StartTrial());
            Assert.AreEqual(GameMode.Trial, f.Mode);
            Assert.AreEqual(-1, f.FightLadderIndex);
            var seen = new System.Collections.Generic.HashSet<uint>();
            for (int k = 0; k < f.TrialTotal; k++)
            {
                Assert.AreEqual(k, f.TrialIndex);
                Assert.IsTrue(seen.Add(f.Foe.seed), "соперники испытания разные");
                PlayOut(f, true);
                f.Continue();
            }
            Assert.AreEqual(AppState.MainMenu, f.State);
            Assert.AreEqual(3, f.Profile.wins);
            Assert.AreEqual(0, f.Profile.ladderIndex, "испытание лестницу не двигает");
        }

        // ------------------------------------------------------------------ Магазин

        [Test]
        public void ShopPurchase_DeductsCoins_AndPersists()
        {
            var f = NewFlow();
            f.Profile.coins = 300;
            f.OpenShop();
            Assert.AreEqual(ShopResult.Ok, f.BuyGlove("heavy"));
            Assert.AreEqual(50, f.Profile.coins);
            Assert.AreEqual(ShopResult.AlreadyOwned, f.BuyGlove("heavy"));
            Assert.AreEqual(ShopResult.NotEnoughCoins, f.BuyGlove("speed"));
            Assert.AreEqual(50, f.Profile.coins, "отказ не списывает");
            Assert.AreEqual(ShopResult.Locked, f.BuyGlove("mega"), "закрыта до 3 побед в карьере");
            Assert.AreEqual(ShopResult.NotOwned, f.EquipGlove("speed"));
            Assert.AreEqual(ShopResult.Ok, f.EquipGlove("heavy"));
            Assert.AreEqual(ShopResult.AlreadyEquipped, f.EquipGlove("heavy"));
            Assert.AreEqual(ShopResult.NotEnoughCoins, f.BuyUpgrade(UpgradeType.Power));

            f.Profile.coins += 100;
            Assert.AreEqual(ShopResult.Ok, f.BuyUpgrade(UpgradeType.Power));
            Assert.AreEqual(50, f.Profile.coins);

            var reloaded = new GameFlow(_path, 9u);
            Assert.AreEqual(50, reloaded.Profile.coins);
            Assert.IsTrue(reloaded.Profile.OwnsGlove("heavy"));
            Assert.AreEqual("heavy", reloaded.Profile.equippedGloveId);
            Assert.AreEqual(1, reloaded.Profile.upgradeLevels[(int)UpgradeType.Power]);
        }

        [Test]
        public void UpgradeCostAndEffect_FollowProfileTables()
        {
            var f = NewFlow();
            Assert.AreEqual(PlayerProfile.UpgradeCosts[0], f.UpgradeCost(UpgradeType.Speed));
            f.Profile.upgradeLevels[(int)UpgradeType.Speed] = PlayerProfile.MaxUpgradeLevel;
            Assert.AreEqual(-1, f.UpgradeCost(UpgradeType.Speed));
            Assert.AreEqual(1f, GameFlow.UpgradeMult(UpgradeType.Power, 0), 1e-5f);
            Assert.Greater(GameFlow.UpgradeMult(UpgradeType.Power, 1), 1f);
            Assert.Less(GameFlow.UpgradeMult(UpgradeType.Defense, 2), 1f);
        }

        // ------------------------------------------------------------------ Полный круг

        [Test]
        public void FullLoop_MenuFightWinRewardShop_NextFightHasDifferentFoe()
        {
            var f = NewFlow();
            f.Play();
            f.StartCareer();
            Assert.IsTrue(f.PickFighter(0));
            var first = f.Foe;
            int serial = f.FightSerial;
            PlayOut(f, true);
            int coinsAfterWin = f.Profile.coins;
            Assert.Greater(coinsAfterWin, 0);

            Assert.AreEqual(AppState.Reward, f.State);    // победа → сразу награда
            Assert.IsTrue(f.OpenShop());                  // награда → магазин
            Assert.AreEqual(AppState.Shop, f.State);
            Assert.AreEqual(ShopResult.Ok, f.BuyUpgrade(UpgradeType.Power));
            Assert.AreEqual(coinsAfterWin - PlayerProfile.UpgradeCosts[0], f.Profile.coins);

            Assert.IsTrue(f.Back());                      // из магазина после награды — дальше по карьере
            Assert.AreEqual(AppState.Roster, f.State);
            Assert.IsTrue(f.PickFighter(f.Profile.ladderIndex));
            Assert.AreEqual(AppState.Loading, f.State);
            Assert.AreEqual(1, f.FoeLadderIndex);
            Assert.Greater(f.FightSerial, serial);
            Assert.AreNotEqual(first.name, f.Foe.name);
            Assert.AreNotEqual(first.seed, f.Foe.seed);

            Assert.IsTrue(f.FightLoaded());
            Assert.AreEqual(AppState.Fight, f.State);
        }

        [Test]
        public void QuickFight_NewFoeEachTime()
        {
            var f = NewFlow();
            f.Play(); f.StartQuick();
            uint a = f.Foe.seed;
            PlayOut(f, true);
            f.Continue();
            Assert.AreEqual(AppState.MainMenu, f.State);
            f.Play(); f.StartQuick();
            Assert.AreNotEqual(a, f.Foe.seed);
        }

        // ------------------------------------------------------------------ Настройки

        [Test]
        public void Settings_PersistInSave_AndResetKeepsThem()
        {
            var f = NewFlow();
            f.Profile.coins = 999;
            f.Save();
            Assert.IsFalse(f.SetVolume(VolumeKind.Master, 0.4f), "вне экрана настроек не меняется");
            f.OpenSettings();
            Assert.IsTrue(f.SetVolume(VolumeKind.Master, 0.4f));
            Assert.IsTrue(f.SetVolume(VolumeKind.Sfx, 5f));
            Assert.IsTrue(f.SetVolume(VolumeKind.Crowd, 0.2f));
            Assert.IsTrue(f.SetSoundEnabled(false));
            Assert.IsTrue(f.SetVibration(false));
            Assert.AreEqual(1f, f.Profile.sfxVolume, 1e-5f, "громкость ограничена 0..1");

            var g = new GameFlow(_path, 2u);
            Assert.AreEqual(0.4f, g.Profile.masterVolume, 1e-5f);
            Assert.AreEqual(1f, g.Profile.sfxVolume, 1e-5f);
            Assert.AreEqual(0.2f, g.Profile.crowdVolume, 1e-5f);
            Assert.IsFalse(g.Profile.soundEnabled);
            Assert.IsFalse(g.Profile.vibrationEnabled);

            Assert.IsTrue(f.CanResetProgress);
            Assert.IsTrue(f.ResetProgress());
            Assert.AreEqual(0, f.Profile.coins);
            Assert.AreEqual(0, SaveService.Load(_path).coins);
            Assert.AreEqual(0.4f, f.Profile.masterVolume, 1e-5f, "сброс прогресса не трогает звук");
            Assert.IsFalse(f.Profile.soundEnabled);
        }

        [Test]
        public void ResetProgress_NotFromPause()
        {
            var f = NewFlow();
            f.Play(); f.StartQuick(); f.FightLoaded(); f.Pause();
            f.OpenSettings();
            Assert.IsFalse(f.CanResetProgress);
            Assert.IsFalse(f.ResetProgress());
            Assert.IsTrue(f.Back());
            Assert.AreEqual(AppState.Pause, f.State);
        }

        // ------------------------------------------------------------------ Пауза и тренировка

        [Test]
        public void RestartFromPause_SameFoe_NoCharges()
        {
            var f = NewFlow();
            Assert.IsFalse(f.RestartFight(), "вне паузы перезапуска нет");
            f.Play(); f.StartCareer(); f.PickFighter(0); f.FightLoaded();
            string foe = f.Foe.name;
            int saves = f.SaveCount;
            Assert.IsTrue(f.Pause());
            Assert.IsTrue(f.RestartFight());
            Assert.AreEqual(AppState.Fight, f.State);
            Assert.AreEqual(1, f.RestartCount);
            Assert.AreEqual(foe, f.Foe.name);
            Assert.AreEqual(0, f.Profile.losses);
            Assert.AreEqual(0, f.Profile.wins);
            Assert.AreEqual(saves, f.SaveCount, "перезапуск ничего не пишет");
        }

        [Test]
        public void Training_WeakFoe_NotCounted_SmallReward()
        {
            var f = NewFlow();
            f.Play();
            Assert.IsTrue(f.StartTraining());
            Assert.AreEqual(GameMode.Training, f.Mode);
            Assert.AreEqual(AppState.Loading, f.State);
            Assert.AreEqual(-1, f.FightLadderIndex);
            Assert.Less(f.FoeDamageMult, 1f);
            PlayOut(f, true);
            Assert.AreEqual(0, f.Profile.wins, "тренировка не идёт в счёт");
            Assert.AreEqual(0, f.Profile.ladderIndex);
            Assert.AreEqual(GameFlow.TrainingCoins, f.LastReward.coins);
            Assert.AreEqual(GameFlow.TrainingCoins, f.Profile.coins);
            Assert.AreEqual(GameFlow.TrainingXp, f.LastReward.xp);
            Assert.IsTrue(f.Continue());
            Assert.AreEqual(AppState.MainMenu, f.State);

            // поражение и выход из паузы тоже без поражений в счёте
            f.Play(); f.StartTraining();
            PlayOut(f, false);
            Assert.AreEqual(0, f.Profile.losses);
            Assert.AreEqual(0, f.LastReward.coins);
            Assert.IsTrue(f.QuitToMenu());
            f.Play(); f.StartTraining(); f.FightLoaded(); f.Pause();
            Assert.IsTrue(f.QuitToMenu());
            Assert.AreEqual(0, f.Profile.losses);

            // в обычном бою урон соперника полный
            f.Play(); f.StartQuick();
            Assert.AreEqual(1f, f.FoeDamageMult);
        }
    }
}
