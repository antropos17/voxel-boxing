// ReviewFixesTests.cs — правила, исправленные после проверки игрового слоя: награда по уровню и точности,
// повтор ступени, титульный бой, здоровье и мега в испытании, удары соперника после нокдауна, перезарядка
// по скорости, выносливость на промахе, остановка рефери, надетая перчатка, стили лестницы, срыв загрузки.
using System.IO;
using NUnit.Framework;

namespace VoxGame.Tests
{
    [TestFixture]
    public class ReviewFixesTests
    {
        string _dir, _path;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "voxfix_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "save.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        GameFlow NewFlow()
        {
            var f = new GameFlow(_path, 7u);
            f.Boot();
            return f;
        }

        static HitResult[] Hits(int n)
        {
            var h = new HitResult[n];
            for (int i = 0; i < n; i++) h[i] = new HitResult { hit = true };
            return h;
        }

        static void WinFight(GameFlow f, FightStats s)
        {
            Assert.IsTrue(f.FightLoaded());
            Assert.IsTrue(f.FightDecided());
            Assert.IsTrue(f.FightEnded(true, s));
        }

        // ---- Награда ----

        [Test]
        public void Accuracy_FromThrownAndLanded()
        {
            Assert.AreEqual(0.5f, ModeManager.Accuracy(10, 5), 1e-5f);
            Assert.AreEqual(-1f, ModeManager.Accuracy(0, 0));
        }

        [Test]
        public void Reward_UsesPassedAccuracy_NotHistory()
        {
            var m = new ModeManager();
            var h = Hits(4);   // история — только попадания: по ней точность всегда 100%
            int full = m.ComputeReward(h, GameMode.QuickFight, 1, 1f);
            int half = m.ComputeReward(h, GameMode.QuickFight, 1, 0.5f);
            Assert.AreEqual(25, full - half);   // 100%×0.5 − 50%×0.5
        }

        [Test]
        public void Reward_GrowsWithFoeLevel()
        {
            var m = new ModeManager();
            Assert.AreEqual(100, ModeManager.WinCoins(1));
            Assert.Greater(m.ComputeReward(Hits(3), GameMode.QuickFight, 12, 1f),
                           m.ComputeReward(Hits(3), GameMode.QuickFight, 1, 1f));
        }

        [Test]
        public void Career_RepeatOfBeatenStep_PaysLess()
        {
            var f = NewFlow();
            var s = new FightStats { hitsThrown = 4, hitsLanded = 4, history = Hits(4) };
            f.Play(); f.StartCareer(); f.PickFighter(0);
            WinFight(f, s);
            int first = f.LastReward.coins;
            Assert.AreEqual(1, f.Profile.ladderIndex);
            f.Continue();
            Assert.AreEqual(AppState.Roster, f.State);
            Assert.IsTrue(f.PickFighter(0));
            WinFight(f, s);
            int repeat = f.LastReward.coins;
            Assert.Less(repeat, first);
            Assert.AreEqual(UnityEngine.Mathf.RoundToInt(first * GameFlow.RepeatCoinsMult), repeat);
            Assert.AreEqual(1, f.Profile.ladderIndex);
        }

        [Test]
        public void TitleFight_OnlyFirstTimeAtLastStep()
        {
            var f = NewFlow();
            f.Profile.ladderIndex = RosterDef.Count - 1;
            f.Play(); f.StartCareer();
            Assert.IsTrue(f.PickFighter(RosterDef.Count - 1));
            Assert.IsTrue(f.IsTitleFight);
            WinFight(f, new FightStats { history = Hits(2), hitsThrown = 2, hitsLanded = 2 });
            Assert.AreEqual(RosterDef.Count, f.Profile.ladderIndex);
            f.Continue();
            Assert.IsTrue(f.PickFighter(RosterDef.Count - 1));
            Assert.IsFalse(f.IsTitleFight);   // повтор после пояса — обычный бой
        }

        // ---- Испытание ----

        [Test]
        public void Trial_CarriesHp_PlusRestore_NoMega()
        {
            var f = NewFlow();
            f.Play();
            Assert.IsTrue(f.StartTrial());
            Assert.IsFalse(f.Modes.MegaAllowed);
            Assert.AreEqual(100f, f.StartPlayerHp);
            WinFight(f, new FightStats { history = Hits(2), hitsThrown = 2, hitsLanded = 2, playerHpLeft = 40f });
            Assert.IsTrue(f.Continue());
            Assert.AreEqual(AppState.Loading, f.State);
            Assert.AreEqual(40f + f.Modes.GetHpRestore(GameMode.Trial), f.StartPlayerHp, 1e-4f);
        }

        [Test]
        public void Trial_StartHp_NotBelowMinimum()
        {
            var f = NewFlow();
            f.Play(); f.StartTrial();
            WinFight(f, new FightStats { history = Hits(1), hitsThrown = 1, hitsLanded = 1, playerHpLeft = 1f });
            f.Continue();
            Assert.AreEqual(GameFlow.MinStartHp, f.StartPlayerHp, 1e-4f);
        }

        [Test]
        public void NewFoe_ResetsStartHp()
        {
            var f = NewFlow();
            f.Play(); f.StartQuick();
            Assert.AreEqual(100f, f.StartPlayerHp);
            Assert.IsTrue(f.Modes.MegaAllowed);
        }

        // ---- Бой ----

        [Test]
        public void Foe_CannotStrike_WhenDownOrKo()
        {
            Assert.IsFalse(FightState.FoeCanStrike(FightPhase.Down));
            Assert.IsFalse(FightState.FoeCanStrike(FightPhase.Rise));
            Assert.IsFalse(FightState.FoeCanStrike(FightPhase.KO));
            Assert.IsTrue(FightState.FoeCanStrike(FightPhase.Idle));
        }

        [Test]
        public void PunchCooldown_ShorterWithSpeed()
        {
            float slow = FightState.PunchCooldown(PunchType.Jab, false, 1f);
            float fast = FightState.PunchCooldown(PunchType.Jab, false, 1.5f);
            Assert.Less(fast, slow);
            Assert.Greater(FightState.PunchCooldown(PunchType.Hook, true, 1f), FightState.PunchCooldown(PunchType.Hook, false, 1f));
        }

        [Test]
        public void Miss_SpendsStamina()
        {
            var sim = new FightSim(1, 42u);
            float before = sim.State.playerStamina;
            var r = sim.ProcessHit(0, 0, 0f, 0, 1, false, 1f, false, false);
            Assert.IsFalse(r.hit);
            Assert.Less(sim.State.playerStamina, before);
        }

        [Test]
        public void Referee_StopsAfterSecondKnockdown_WhenHurt()
        {
            Assert.AreEqual(-1, RefereeLogic.RiseCount(2, 0.3f));
            Assert.Greater(RefereeLogic.RiseCount(2, 0.8f), 0);
            Assert.Greater(RefereeLogic.RiseCount(1, 0.1f), 0);
        }

        // ---- Сохранение, лестница, загрузка ----

        [Test]
        public void LoadFrom_EquippedNotBought_FallsBackToClassic()
        {
            var p = new PlayerProfile();
            var d = new SaveData { equippedGloveId = "gold", boughtGloveIds = new[] { "classic" } };
            p.LoadFrom(d);
            Assert.AreEqual("classic", p.equippedGloveId);
            var d2 = new SaveData { equippedGloveId = "no_such_glove", boughtGloveIds = new[] { "classic", "no_such_glove" } };
            p.LoadFrom(d2);
            Assert.AreEqual("classic", p.equippedGloveId);
        }

        [Test]
        public void Ladder_StylesFixed_NoEqualNeighbours()
        {
            var r = new RosterDef(42u);
            Assert.AreEqual(0, r.Get(0).styleIdx);   // первая ступень — Кирпич (медленный, честный телеграф)
            for (int i = 0; i < RosterDef.Count; i++)
            {
                Assert.AreEqual(RosterDef.LadderStyles[i], r.Get(i).styleIdx);
                if (i > 0) Assert.AreNotEqual(r.Get(i - 1).styleIdx, r.Get(i).styleIdx);
            }
        }

        [Test]
        public void Stats_DamageFollowsStyle()
        {
            var heavy = FighterGen.Generate(5u, 12, new FighterGen.LookPicks { trunks = -1, gloves = -1, body = -1, top = -1, beard = -1, nick = -1, style = 1 });
            var light = FighterGen.Generate(5u, 12, new FighterGen.LookPicks { trunks = -1, gloves = -1, body = -1, top = -1, beard = -1, nick = -1, style = 2 });
            Assert.Greater(heavy.stats.damage, light.stats.damage);   // Шкаф бьёт больнее Молота
        }

        [Test]
        public void AbortLoading_ReturnsToMenu()
        {
            var f = NewFlow();
            f.Play(); f.StartQuick();
            Assert.AreEqual(AppState.Loading, f.State);
            Assert.IsTrue(f.AbortLoading());
            Assert.AreEqual(AppState.MainMenu, f.State);
            Assert.IsFalse(f.AbortLoading());
        }
    }
}
