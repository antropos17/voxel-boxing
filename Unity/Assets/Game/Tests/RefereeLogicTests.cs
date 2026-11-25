using System.Collections.Generic;
using NUnit.Framework;
using VoxGame;

namespace VoxGame.Tests
{
    [TestFixture]
    public class RefereeLogicTests
    {
        // --- вспомогательные методы ---

        static List<RefereeEvent> DrainEvents(RefereeLogic r)
        {
            var list = new List<RefereeEvent>();
            while (r.TryDequeueEvent(out var e)) list.Add(e);
            return list;
        }

        // --- тесты ---

        [Test]
        public void InitialState_IsIdle()
        {
            var r = new RefereeLogic();
            Assert.AreEqual(RefereeState.Idle, r.State);
            Assert.AreEqual(0, r.Count);
            Assert.AreEqual(0f, r.Phase, 0.001f);
        }

        [Test]
        public void OnKnockdown_EntersStepIn()
        {
            var r = new RefereeLogic();
            r.OnKnockdown(-1);
            Assert.AreEqual(RefereeState.StepIn, r.State);
        }

        [Test]
        public void StepIn_PhaseAdvancesTo1_ThenEntersCounting()
        {
            var r = new RefereeLogic();
            r.OnKnockdown(-1);

            r.Tick(RefereeLogic.STEP_IN_DUR * 0.5f);
            Assert.AreEqual(RefereeState.StepIn, r.State);
            Assert.AreEqual(0.5f, r.Phase, 0.001f);

            r.Tick(RefereeLogic.STEP_IN_DUR * 0.5f);
            Assert.AreEqual(RefereeState.Counting, r.State);
            Assert.AreEqual(0, r.Count);
        }

        [Test]
        public void Counting_PhaseAtHalfStep()
        {
            var r = new RefereeLogic();
            r.OnKnockdown(-1);
            r.Tick(RefereeLogic.STEP_IN_DUR); // → Counting

            r.Tick(RefereeLogic.COUNT_STEP * 0.5f);
            Assert.AreEqual(RefereeState.Counting, r.State);
            Assert.AreEqual(0.5f, r.Phase, 0.001f);
            Assert.AreEqual(0, r.Count); // шаг не завершён
        }

        [Test]
        public void Counting_EachStep_EmitsCountTickEvent()
        {
            var r = new RefereeLogic();
            r.OnKnockdown(-1);
            r.Tick(RefereeLogic.STEP_IN_DUR); // → Counting

            for (int expected = 1; expected <= 5; expected++)
            {
                r.Tick(RefereeLogic.COUNT_STEP);
                var evts = DrainEvents(r);
                Assert.AreEqual(1, evts.Count, $"счёт {expected}: одно событие");
                Assert.AreEqual(RefereeEventType.CountTick, evts[0].Type);
                Assert.AreEqual(expected, evts[0].Count);
                Assert.AreEqual(expected, r.Count);
                Assert.AreEqual(RefereeState.Counting, r.State);
            }
        }

        [Test]
        public void FullCountToTen_TenTicksThenFightStoppedThenHandRaised()
        {
            var r = new RefereeLogic();
            r.OnKnockdown(-1); // riseAt=-1, не встанет
            r.Tick(RefereeLogic.STEP_IN_DUR); // → Counting

            // счёт 1..9 — только CountTick
            for (int i = 1; i <= 9; i++)
            {
                r.Tick(RefereeLogic.COUNT_STEP);
                var evts = DrainEvents(r);
                Assert.AreEqual(1, evts.Count, $"счёт {i}");
                Assert.AreEqual(RefereeEventType.CountTick, evts[0].Type);
                Assert.AreEqual(i, evts[0].Count);
                Assert.AreEqual(RefereeState.Counting, r.State);
            }

            // счёт 10 — CountTick(10) + FightStopped в одном тике
            r.Tick(RefereeLogic.COUNT_STEP);
            var e10 = DrainEvents(r);
            Assert.AreEqual(2, e10.Count, "на счёте 10: CountTick + FightStopped");
            Assert.AreEqual(RefereeEventType.CountTick,    e10[0].Type);
            Assert.AreEqual(10,                            e10[0].Count);
            Assert.AreEqual(RefereeEventType.FightStopped, e10[1].Type);
            Assert.AreEqual(RefereeState.StopFight, r.State);
            Assert.AreEqual(10, r.Count);

            // StopFight → WalkToWinner
            r.Tick(RefereeLogic.STOP_DUR);
            Assert.AreEqual(RefereeState.WalkToWinner, r.State);

            // WalkToWinner → RaiseHand
            r.Tick(RefereeLogic.WALK_DUR);
            Assert.AreEqual(RefereeState.RaiseHand, r.State);

            // RaiseHand → Done + HandRaised
            r.Tick(RefereeLogic.RAISE_DUR);
            Assert.AreEqual(RefereeState.Done, r.State);
            Assert.AreEqual(1f, r.Phase, 0.001f);

            var final = DrainEvents(r);
            Assert.AreEqual(1, final.Count);
            Assert.AreEqual(RefereeEventType.HandRaised, final[0].Type);
            Assert.IsTrue(r.PlayerWon);
        }

        [Test]
        public void RiseAtN_EmitsSayBox_ThenResumes_ThenIdle()
        {
            var r = new RefereeLogic();
            r.OnKnockdown(5); // встанет на счёте 5

            r.Tick(RefereeLogic.STEP_IN_DUR); // → Counting

            // счёт 1..4 без перехода
            for (int i = 1; i <= 4; i++)
            {
                r.Tick(RefereeLogic.COUNT_STEP);
                var evts = DrainEvents(r);
                Assert.AreEqual(RefereeEventType.CountTick, evts[0].Type, $"счёт {i}");
                Assert.AreEqual(RefereeState.Counting, r.State);
            }

            // счёт 5 → CountTick(5) + SayBox, переход в Resume
            r.Tick(RefereeLogic.COUNT_STEP);
            var e5 = DrainEvents(r);
            Assert.AreEqual(2, e5.Count, "на счёте riseAt: CountTick + SayBox");
            Assert.AreEqual(RefereeEventType.CountTick, e5[0].Type);
            Assert.AreEqual(5,                          e5[0].Count);
            Assert.AreEqual(RefereeEventType.SayBox,    e5[1].Type);
            Assert.AreEqual(RefereeState.Resume, r.State);
            Assert.AreEqual(5, r.Count);

            // Resume → Idle после RESUME_DUR
            r.Tick(RefereeLogic.RESUME_DUR);
            Assert.AreEqual(RefereeState.Idle, r.State);
            Assert.AreEqual(0f, r.Phase, 0.001f);
        }

        [Test]
        public void Knockout_SkipsCounting_GoesDirectToStopFight()
        {
            var r = new RefereeLogic();
            r.OnKnockout(true); // игрок победил

            Assert.AreEqual(RefereeState.StepIn, r.State);

            r.Tick(RefereeLogic.STEP_IN_DUR); // → StopFight (минуем Counting)

            var evts = DrainEvents(r);
            Assert.AreEqual(1, evts.Count, "только FightStopped");
            Assert.AreEqual(RefereeEventType.FightStopped, evts[0].Type);
            Assert.AreEqual(RefereeState.StopFight, r.State);
            Assert.AreEqual(0, r.Count, "при нокауте счёт не ведётся");
        }

        [Test]
        public void Knockout_LeadsToHandRaised()
        {
            var r = new RefereeLogic();
            r.OnKnockout(true);

            r.Tick(RefereeLogic.STEP_IN_DUR);
            DrainEvents(r); // FightStopped

            r.Tick(RefereeLogic.STOP_DUR);
            Assert.AreEqual(RefereeState.WalkToWinner, r.State);

            r.Tick(RefereeLogic.WALK_DUR);
            Assert.AreEqual(RefereeState.RaiseHand, r.State);

            r.Tick(RefereeLogic.RAISE_DUR);
            Assert.AreEqual(RefereeState.Done, r.State);

            var final = DrainEvents(r);
            Assert.AreEqual(1, final.Count);
            Assert.AreEqual(RefereeEventType.HandRaised, final[0].Type);
            Assert.IsTrue(r.PlayerWon);
        }

        [Test]
        public void PlayerLoss_PlayerWonIsFalse_AndReachesHandRaised()
        {
            var r = new RefereeLogic();
            r.OnKnockout(false); // игрок проиграл

            Assert.IsFalse(r.PlayerWon);

            r.Tick(RefereeLogic.STEP_IN_DUR);
            DrainEvents(r); // очистить FightStopped (возник при переходе StepIn→StopFight)
            r.Tick(RefereeLogic.STOP_DUR);
            r.Tick(RefereeLogic.WALK_DUR);
            r.Tick(RefereeLogic.RAISE_DUR);

            Assert.AreEqual(RefereeState.Done, r.State);
            Assert.IsFalse(r.PlayerWon, "победитель не изменился");

            Assert.IsTrue(r.TryDequeueEvent(out var e));
            Assert.AreEqual(RefereeEventType.HandRaised, e.Type);
        }

        [Test]
        public void HandRaised_EmittedExactlyOnce()
        {
            var r = new RefereeLogic();
            r.OnKnockout(true);
            r.Tick(RefereeLogic.STEP_IN_DUR);
            r.Tick(RefereeLogic.STOP_DUR);
            r.Tick(RefereeLogic.WALK_DUR);
            r.Tick(RefereeLogic.RAISE_DUR);

            int handRaisedCount = 0;
            while (r.TryDequeueEvent(out var e))
                if (e.Type == RefereeEventType.HandRaised) handRaisedCount++;

            // дополнительные тики в Done — событий нет
            r.Tick(1f);
            r.Tick(1f);
            while (r.TryDequeueEvent(out _)) handRaisedCount++;

            Assert.AreEqual(1, handRaisedCount, "HandRaised ровно один раз");
        }

        [Test]
        public void Reset_MidCount_ReturnsToIdle()
        {
            var r = new RefereeLogic();
            r.OnKnockdown(-1);
            r.Tick(RefereeLogic.STEP_IN_DUR); // → Counting

            r.Tick(RefereeLogic.COUNT_STEP); // Count=1
            r.Tick(RefereeLogic.COUNT_STEP); // Count=2
            r.Tick(RefereeLogic.COUNT_STEP); // Count=3
            Assert.AreEqual(3, r.Count);

            r.Reset();

            Assert.AreEqual(RefereeState.Idle, r.State);
            Assert.AreEqual(0, r.Count);
            Assert.AreEqual(0f, r.Phase, 0.001f);
            Assert.IsFalse(r.TryDequeueEvent(out _), "очередь очищена");
        }

        [Test]
        public void Reset_AfterKO_AllowsNewKnockdown()
        {
            var r = new RefereeLogic();
            r.OnKnockout(true);
            r.Tick(RefereeLogic.STEP_IN_DUR);
            r.Tick(RefereeLogic.STOP_DUR);
            r.Tick(RefereeLogic.WALK_DUR);
            r.Tick(RefereeLogic.RAISE_DUR);
            Assert.AreEqual(RefereeState.Done, r.State);

            r.Reset();

            r.OnKnockdown(4);
            Assert.AreEqual(RefereeState.StepIn, r.State);
            r.Tick(RefereeLogic.STEP_IN_DUR);
            Assert.AreEqual(RefereeState.Counting, r.State);
        }

        [Test]
        public void StopFight_PhaseAdvancesCorrectly()
        {
            var r = new RefereeLogic();
            r.OnKnockout(true);
            r.Tick(RefereeLogic.STEP_IN_DUR); // → StopFight
            DrainEvents(r);

            r.Tick(RefereeLogic.STOP_DUR * 0.5f);
            Assert.AreEqual(RefereeState.StopFight, r.State);
            Assert.AreEqual(0.5f, r.Phase, 0.001f);

            r.Tick(RefereeLogic.STOP_DUR * 0.5f);
            Assert.AreEqual(RefereeState.WalkToWinner, r.State);
        }

        [Test]
        public void LargeDeltaTime_HandledCorrectly_FullCountInOneTick()
        {
            var r = new RefereeLogic();
            r.OnKnockdown(-1);
            r.Tick(RefereeLogic.STEP_IN_DUR); // → Counting

            // 10 шагов за один крупный тик
            r.Tick(RefereeLogic.COUNT_STEP * 10f);

            // должны быть все CountTick(1..10) + FightStopped
            var evts = DrainEvents(r);
            Assert.AreEqual(11, evts.Count, "10 × CountTick + 1 × FightStopped");
            Assert.AreEqual(10, r.Count);
            Assert.AreEqual(RefereeState.StopFight, r.State);
        }
    }
}
