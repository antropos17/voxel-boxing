using NUnit.Framework;
using UnityEngine;
using VoxGame.Editor;
using VoxProbe;

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

        // Нокдаун и нокаут (направление 3): без NaN, не под настилом (> 2 см), в ринге, лежит за ≤ 2,5 с, ложится по удару
        // (≥ 90 % в пределах 45°), подъём без рывка (≤ 5 % роста за кадр), стойка после подъёма как до падения.
        [Test]
        public void FallStaysInRingAndFollowsHit([Values(false, true)] bool ko)
        {
            bool was = FoeFall.Enabled; FoeFall.Enabled = true;
            try
            {
                var st = ShotsTaker.FallStats(4, 5, 1f / 60f, ko);
                string s = st.Line();
                Assert.AreEqual(0, st.nan, s); Assert.AreEqual(0, st.under, s); Assert.AreEqual(0, st.outRing, s);
                Assert.AreEqual(0, st.slow, s);
                Assert.LessOrEqual(st.dirBad, st.runs / 10, s);
                Assert.LessOrEqual(st.worstJump, 0.05f, s);
                Assert.Less(st.worstStance, 0.01f, s);
            }
            finally { FoeFall.Enabled = was; }
        }

        // Суставы при падении (дополнение к направлению 3), счёт по кадрам: ни один сустав не выходит за предел больше чем на 3°,
        // колено и локоть не переразгибаются (> 5°), ноги лёжа не «книжкой» (бедро–голень ≥ 35°, бедро–корпус ≥ 30°), голени не
        // в одном объёме. Взаимопроникновение капсул критерию (≤ 1,5 / 0,5 см) пока не отвечает — здесь только защита от ухудшения.
        [Test]
        public void FallKeepsJointLimits([Values(false, true)] bool ko)
        {
            bool was = FoeFall.Enabled; FoeFall.Enabled = true;
            try
            {
                var l = ShotsTaker.FallStats(4, 5, 1f / 60f, ko).limb;
                string s = l.Line() + l.By();
                Assert.Greater(l.frames, 0, s);
                Assert.AreEqual(0, l.framesOver, s); Assert.AreEqual(0, l.framesHyper, s); Assert.AreEqual(0, l.legsSame, s);
                Assert.GreaterOrEqual(l.minKneeRest, 35f, s); Assert.GreaterOrEqual(l.minHipRest, 30f, s);
                Assert.LessOrEqual(l.worstPenRest, 0.07f, s); Assert.LessOrEqual(l.worstPen, 0.22f, s);
            }
            finally { FoeFall.Enabled = was; }
        }

        // Куски (направление 4): в бою мегой и силовыми куски падают на настил ринга, через 3 с не под ним и не в воздухе,
        // ≥ 90 % уснули за ≤ 3 с; пределы кусков, капель и подтёков не превышены.
        [Test]
        public void PiecesLieOnRingFloor()
        {
            ShotsTaker.PhysPieces(2, 1, false);
            int alive = 0, asleep = 0;
            foreach (var l in ShotsTaker.PhysLog.Split((char)10))
            {
                if (!l.StartsWith("PIECES")) continue;
                StringAssert.Contains("under=0 hang=0", l);
                alive += Num(l, "alive="); asleep += Num(l, "asleep=");
                Assert.LessOrEqual(Num(l, " peak="), AnatPieces.MaxPieces, l);
                Assert.LessOrEqual(Num(l, "dripsPeak="), AnatDamage.MaxDrips, l);
                Assert.LessOrEqual(Num(l, "dropsPeak="), AnatDamage.MaxDrops, l);
            }
            Assert.Greater(alive, 0, "куски есть");
            Assert.GreaterOrEqual(asleep, alive * 0.9f, ShotsTaker.PhysLog);
        }

        static int Num(string l, string k) { int i = l.IndexOf(k) + k.Length, j = i; while (j < l.Length && char.IsDigit(l[j])) j++; return int.Parse(l.Substring(i, j - i)); }

        // Падение не зависит от частоты кадров: те же удары при 30 и 120 к/с — те же итоги
        [Test]
        public void FallFrameRateIndependent([Values(1f / 30f, 1f / 120f)] float dt)
        {
            var st = ShotsTaker.FallStats(3, 4, dt);
            string s = st.Line();
            Assert.AreEqual(0, st.nan + st.under + st.outRing + st.slow, s);
            Assert.LessOrEqual(st.dirBad, 1, s);
            Assert.LessOrEqual(st.worstJump, 0.05f, s);
        }
    }
}
