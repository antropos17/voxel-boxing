// VictorySceneTests.cs — постановщик развязки без рефери и без вида: порядок фаз, пояс, сигналы, сброс.
using System.Collections.Generic;
using NUnit.Framework;

namespace VoxGame.Tests
{
    [TestFixture]
    public class VictorySceneTests
    {
        const float DT = 1f / 60f;

        // Прогоняет развязку до готовности (или до предела кадров) и возвращает фазы в порядке появления.
        static List<VictoryState> Run(VictoryScene v, int maxFrames = 3000)
        {
            var seen = new List<VictoryState> { v.State };
            for (int i = 0; i < maxFrames && v.Running; i++)
            {
                v.Tick(DT);
                if (seen[seen.Count - 1] != v.State) seen.Add(v.State);
            }
            return seen;
        }

        [Test]
        public void Phases_GoInOrder_WithoutBelt()
        {
            var v = new VictoryScene();
            v.Begin(true, false);
            var seen = Run(v);
            CollectionAssert.AreEqual(new[]
            {
                VictoryState.Stop, VictoryState.Walk, VictoryState.Raise, VictoryState.Cheer, VictoryState.Ready
            }, seen);
            Assert.IsTrue(v.IsDone);
            Assert.IsFalse(v.Running);
        }

        [Test]
        public void Phases_IncludeBelt_ForTitleWin()
        {
            var v = new VictoryScene();
            v.Begin(true, true);
            Assert.IsTrue(v.ShowsBelt);
            var seen = Run(v);
            CollectionAssert.AreEqual(new[]
            {
                VictoryState.Stop, VictoryState.Walk, VictoryState.Raise, VictoryState.Cheer, VictoryState.Belt,
                VictoryState.Ready
            }, seen);
        }

        [Test]
        public void Belt_OnlyWithFlag_AndOnlyForPlayerWin()
        {
            int belts = 0;
            var v = new VictoryScene { OnBelt = () => belts++ };

            v.Begin(true, false);
            CollectionAssert.DoesNotContain(Run(v), VictoryState.Belt);
            v.Reset();

            v.Begin(false, true);   // проигрыш игрока: пояса нет даже с флагом
            Assert.IsFalse(v.ShowsBelt);
            CollectionAssert.DoesNotContain(Run(v), VictoryState.Belt);
            v.Reset();

            Assert.AreEqual(0, belts);
            v.Begin(true, true);
            Run(v);
            Assert.AreEqual(1, belts);
        }

        [Test]
        public void Signals_FireOncePerFinale()
        {
            int stop = 0, walk = 0, raised = 0, ready = 0;
            var moods = new List<CrowdMood>();
            var v = new VictoryScene
            {
                OnFightStopped = () => stop++,
                OnWalk = () => walk++,
                OnHandRaised = () => raised++,
                OnReady = () => ready++,
                SetCrowdMood = m => moods.Add(m),
            };
            v.Begin(true, false);
            v.Begin(true, false);   // повторный вызов во время развязки ничего не делает
            Run(v);
            for (int i = 0; i < 60; i++) v.Tick(DT);   // после готовности сигналы не повторяются

            Assert.AreEqual(1, stop);
            Assert.AreEqual(1, walk);
            Assert.AreEqual(1, raised);
            Assert.AreEqual(1, ready);
            CollectionAssert.AreEqual(new[] { CrowdMood.Cheer }, moods);
        }

        [Test]
        public void Duration_MatchesPhaseSum()
        {
            var v = new VictoryScene();
            v.Begin(true, true);
            Run(v);
            float want = VictoryScene.StopDur + VictoryScene.WalkDur + VictoryScene.RaiseDur
                         + VictoryScene.CheerDur + VictoryScene.BeltDur;
            // остаток кадра при смене фазы не переносится: на каждую из шести фаз — не больше кадра сверху
            Assert.GreaterOrEqual(v.Elapsed, want - 1e-3f);
            Assert.LessOrEqual(v.Elapsed, want + 6f * DT + 1e-3f);
        }

        [Test]
        public void Reset_ReturnsToIdle_AndAllowsNewFinale()
        {
            var v = new VictoryScene();
            v.Begin(true, true);
            for (int i = 0; i < 200; i++) v.Tick(DT);
            Assert.IsTrue(v.Running);

            v.Reset();
            Assert.AreEqual(VictoryState.Idle, v.State);
            Assert.IsFalse(v.Running);
            Assert.IsFalse(v.IsDone);
            Assert.AreEqual(0f, v.Elapsed);
            v.Tick(DT);   // в покое Tick ничего не двигает
            Assert.AreEqual(VictoryState.Idle, v.State);

            v.Begin(false, false);
            Assert.AreEqual(VictoryState.Stop, v.State);
            Assert.IsFalse(v.PlayerWon);
            Run(v);
            Assert.IsTrue(v.IsDone);
        }

        [Test]
        public void TitleFight_IsLastOfLadder()
        {
            Assert.IsTrue(VictoryScene.IsTitleFight(RosterDef.Count - 1, RosterDef.Count));
            Assert.IsFalse(VictoryScene.IsTitleFight(RosterDef.Count - 2, RosterDef.Count));
            Assert.IsFalse(VictoryScene.IsTitleFight(0, 0));   // вне лестницы
        }
    }
}
