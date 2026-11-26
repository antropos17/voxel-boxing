using NUnit.Framework;
using UnityEngine;
using VoxGame.Editor;

namespace VoxGame.Tests
{
    // Физика боя (PHYSICS.md): реакция тела на удар и руки. Направления и точки попадания — из настоящего боя (PhysDirs).
    public class PhysicsTests
    {
        bool _was;
        [SetUp] public void SetUp() { _was = FoeSpring.Enabled; FoeSpring.Enabled = true; }
        [TearDown] public void TearDown() { FoeSpring.Enabled = _was; }

        static ShotsTaker.React R(Vector3 dir, Vector3 arm, int str, float dt = 1f / 60f, FightPhase ph = FightPhase.Idle) =>
            ShotsTaker.ReactRun(dir, arm, str, 1.25f, dt, ph);

        // Джеб назад, хук вбок, апперкот вверх-назад: голова уходит по удару (≤ 25°), корпус в пике позже головы на 2–8 кадров,
        // возврат (< 5 % пика) за 0,35–0,9 с, после пика не больше 2 переходов через ноль.
        [Test]
        public void ReactionFollowsPunch([Values(0, 1, 2)] int kind)
        {
            var r = kind == 0 ? R(ShotsTaker.JabDir, ShotsTaker.JabArm, 0)
                  : kind == 1 ? R(ShotsTaker.HookDir, ShotsTaker.HookArm, 1) : R(ShotsTaker.UpperDir, ShotsTaker.UpperArm, 1);
            string s = r.Line("kind" + kind);
            Assert.LessOrEqual(r.angle, 25f, s);
            Assert.That(r.chestAt - r.headAt, Is.InRange(2, 8), s);
            Assert.That(r.settle, Is.InRange(0.35f, 0.9f), s);
            Assert.LessOrEqual(r.cross, 2, s);
        }

        // Сильный удар (силовой, мега) отклоняет голову не меньше чем в 1,8 раза дальше джеба; удар в защиту — меньше обычного.
        [Test]
        public void StrongBeatsWeak_GuardShort()
        {
            var jab = R(ShotsTaker.JabDir, ShotsTaker.JabArm, 0);
            var mega = R(ShotsTaker.JabDir, ShotsTaker.JabArm, 2);
            var hook = R(ShotsTaker.HookDir, ShotsTaker.HookArm, 1);
            var guard = R(ShotsTaker.HookDir, ShotsTaker.HookArm, 1, 1f / 60f, FightPhase.Guard);
            Assert.GreaterOrEqual(mega.headPeak / jab.headPeak, 1.8f, "мега/джеб");
            Assert.GreaterOrEqual(hook.headPeak / jab.headPeak, 1.8f, "силовой/джеб");
            Assert.Less(guard.headPeak, hook.headPeak * 0.6f, "блок — короткая отдача");
        }

        // Тот же удар при 30, 60, 120 к/с: пик головы в пределах 5 %, время пика то же (± кадр 1/30).
        [Test]
        public void ReactionFrameRateIndependent()
        {
            var a = R(ShotsTaker.HookDir, ShotsTaker.HookArm, 1, 1f / 60f);
            foreach (float dt in new[] { 1f / 30f, 1f / 120f })
            {
                var b = R(ShotsTaker.HookDir, ShotsTaker.HookArm, 1, dt);
                Assert.Less(Mathf.Abs(b.headPeak / a.headPeak - 1f), 0.05f, "пик при dt " + dt);
                Assert.Less(Mathf.Abs((b.headAt + 1) * dt - (a.headAt + 1) / 60f), 1f / 30f + 1e-4f, "время пика при dt " + dt);
            }
        }

        // Перчатки соперника отстают от корпуса при ударе по нему (≥ 1 см) и возвращаются за ≤ 0,8 с.
        [Test]
        public void FoeGlovesLagAndReturn()
        {
            var r = R(ShotsTaker.HookDir, ShotsTaker.HookArm, 1);
            Assert.Greater(r.glovePeak, 0.01f, r.Line("hook"));
            Assert.LessOrEqual(r.gloveSettle, 0.8f, r.Line("hook"));
        }

        // Отдача перчатки игрока после попадания: на экране ≥ 3 % ширины 2–6 кадров (60 к/с), к камере.
        [Test]
        public void PlayerGloveRecoil([Values(PunchType.Jab, PunchType.Hook, PunchType.Upper, PunchType.Mega)] PunchType k)
        {
            string s = ShotsTaker.PlayerRecoil(k, 1);
            int i = s.IndexOf("frames>=3%=") + 11;
            int n = int.Parse(s.Substring(i, s.IndexOf(' ', i) - i));
            Assert.That(n, Is.InRange(2, 6), s);
            Assert.IsTrue(s.Contains("dz=-"), "назад к камере: " + s);
        }
    }
}
