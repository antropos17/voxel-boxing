using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VoxGame.Tests
{
    // Пружинность соперника (FoeSpring, FoeBody) и вытянутая рука игрока (PlayerGloves) — SPRING-PHYSICS.md.
    public class SpringTests
    {
        GameObject _root;
        bool _was;

        [SetUp] public void SetUp() { _was = FoeSpring.Enabled; FoeSpring.Enabled = true; }

        [TearDown]
        public void TearDown()
        {
            FoeSpring.Enabled = _was;
            if (_root != null) Object.DestroyImmediate(_root);
        }

        static readonly Vector3 Dir = new Vector3(-0.6f, 0.1f, -0.8f).normalized, Arm = new Vector3(0.06f, 0.14f, 0.09f);

        static void Run(FoeSpring s, float dt, float t) { for (float x = 0f; x < t - 1e-6f; x += dt) s.Step(dt); }

        // Удар мегой: через 1 с всё смещение меньше 1 мм и 0,1°.
        [Test]
        public void ReturnsToRestAfterHit()
        {
            var s = new FoeSpring();
            s.Kick(Dir, Arm, FoeSpring.Impulse(2, 1.3f));
            Run(s, 1f / 60f, 0.05f);
            Assert.Greater(s.head.magnitude, 0.005f, "отдача головы есть");
            Assert.Greater(s.root.magnitude, 0.01f, "отшаг есть");
            Run(s, 1f / 60f, 1f);
            Assert.Less(s.head.magnitude, 0.001f, "голова");
            Assert.Less(s.root.magnitude, 0.001f, "корень");
            Assert.Less(Mathf.Abs(s.lean) + Mathf.Abs(s.twist), 0.1f, "корпус");
            Assert.Less(s.glove[0].magnitude + s.glove[1].magnitude, 0.002f, "перчатки");
        }

        // Шаг фиксированный: в моменты, кратные 1/30 с, состояние одно и то же при кадре 1/30, 1/60, 1/120.
        [Test]
        public void SameTrajectoryAt30_60_120()
        {
            var a = new FoeSpring(); var b = new FoeSpring(); var c = new FoeSpring();
            foreach (var s in new[] { a, b, c }) s.Kick(Dir, Arm, FoeSpring.Impulse(1, 1.2f));
            for (int f = 0; f < 30; f++)   // 1 с
            {
                a.Step(1f / 30f);
                for (int i = 0; i < 2; i++) b.Step(1f / 60f);
                for (int i = 0; i < 4; i++) c.Step(1f / 120f);
                foreach (var s in new[] { b, c })
                {
                    Assert.Less((a.head - s.head).magnitude, 1e-4f, "голова, кадр " + f);
                    Assert.Less((a.root - s.root).magnitude, 1e-4f, "корень, кадр " + f);
                    Assert.Less(Mathf.Abs(a.lean - s.lean) + Mathf.Abs(a.twist - s.twist), 0.02f, "корпус, кадр " + f);
                }
            }
        }

        // Стоп-кадр: время стоит (dt = 0), удары сыплются — ничего не движется, скорость не растёт сверх предела;
        // потом время идёт — без NaN и взрыва, возврат к покою.
        [Test]
        public void StableInHitStopAndSlowMo()
        {
            var s = new FoeSpring();
            for (int i = 0; i < 1000; i++) { s.Kick(Dir, Arm, FoeSpring.JMax); s.Step(0f); }
            Assert.AreEqual(Vector3.zero, s.head, "в стоп-кадре смещения нет");
            Assert.LessOrEqual(s.headV.magnitude, FoeSpring.HeadVMax + 1e-4f);
            Assert.LessOrEqual(s.rootV.magnitude, FoeSpring.RootVMax + 1e-4f);
            Assert.LessOrEqual(Mathf.Abs(s.leanV), FoeSpring.BodyVMax + 1e-3f);
            for (int i = 0; i < 600; i++) s.Step(i % 2 == 0 ? 1f / 600f : 0.25f);   // слоу-мо вперемешку с долгими кадрами
            Run(s, 1f / 60f, 2f);
            Assert.IsFalse(float.IsNaN(s.Energy), "NaN");
            Assert.Less(s.Energy, 0.002f, "вернулся к покою");
        }

        // Предел амплитуды: мега в каждый кадр 3 с подряд — смещения в своих пределах.
        [Test]
        public void AmplitudeLimited()
        {
            var s = new FoeSpring();
            float mh = 0, mr = 0, ml = 0, mt = 0, mg = 0;
            for (int i = 0; i < 180; i++)
            {
                s.Kick(i % 2 == 0 ? Dir : -Dir, Arm, FoeSpring.JMax);
                s.Step(1f / 60f);
                mh = Mathf.Max(mh, s.head.magnitude); mr = Mathf.Max(mr, s.root.magnitude);
                ml = Mathf.Max(ml, Mathf.Abs(s.lean)); mt = Mathf.Max(mt, Mathf.Abs(s.twist));
                mg = Mathf.Max(mg, Mathf.Max(s.glove[0].magnitude, s.glove[1].magnitude));
            }
            Assert.LessOrEqual(mh, FoeSpring.HeadMax + 1e-5f);
            Assert.LessOrEqual(mr, FoeSpring.RootMax + 1e-5f);
            Assert.LessOrEqual(ml, FoeSpring.LeanMax + 1e-4f);
            Assert.LessOrEqual(mt, FoeSpring.TwistMax + 1e-4f);
            Assert.LessOrEqual(mg, FoeSpring.GloveMax + 1e-5f);
        }

        // 0 байт на удар: толчок и шаги после разогрева ничего не выделяют (и в теле соперника).
        [Test]
        public void ZeroAllocPerHit()
        {
            var s = new FoeSpring();
            s.Kick(Dir, Arm, 3f); s.Step(1f / 60f);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) { s.Kick(Dir, Arm, 3f); s.Step(1f / 60f); }
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - a0, "FoeSpring");

            _root = new GameObject("t");
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var body = new FoeBody(_root.transform, FighterGen.Generate(42u, 1), mat);
            Vector3 p = body.headMount.position + body.root.forward * 0.1f;
            body.AddHit(p, -body.root.forward, 1, 1.2f);
            a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) body.AddHit(p, -body.root.forward, i % 3, 1.2f);
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - a0, "FoeBody.AddHit");
            body.Dispose();
            Object.DestroyImmediate(mat);
        }

        // Тело соперника: голова после силового удара возвращается туда же, где она у такого же тела без удара (≤ 2 мм за 1,2 с);
        // в момент отдачи она смещена заметно (≥ 5 мм). Без пружинности удар голову не сдвигает.
        [Test]
        public void FoeHeadReturnsAfterHit()
        {
            _root = new GameObject("t");
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var def = FighterGen.Generate(42u, 1);
            var hit = new FoeBody(_root.transform, def, mat);
            var calm = new FoeBody(_root.transform, def, mat);
            var cmd = new AICommand { newPhase = FightPhase.Guard, side = -1 };
            const float dt = 1f / 60f;
            Vector3 tgt = Vector3.forward;
            for (int i = 0; i < 30; i++) { hit.Tick(cmd, dt, tgt); calm.Tick(cmd, dt, tgt); }
            hit.AddHit(hit.headMount.position + hit.root.forward * 0.1f + hit.root.right * 0.05f, -hit.root.forward, 1, 1.3f);
            float peak = 0f;
            for (int i = 0; i < 72; i++)
            {
                hit.Tick(cmd, dt, tgt); calm.Tick(cmd, dt, tgt);
                peak = Mathf.Max(peak, (hit.headMount.position - calm.headMount.position).magnitude);
            }
            float left = (hit.headMount.position - calm.headMount.position).magnitude;
            Assert.Greater(peak, 0.005f, "отдача головы");
            Assert.Less(left, 0.002f, "голова вернулась");

            FoeSpring.Enabled = false;
            var off = new FoeBody(_root.transform, def, mat);
            Vector3 h0 = off.headMount.position;
            off.AddHit(h0 + off.root.forward * 0.1f, -off.root.forward, 1, 1.3f);
            off.Tick(cmd, 0f, tgt);
            Assert.Less((off.headMount.position - h0).magnitude, 1e-5f, "без пружинности голова не сдвигается");
            hit.Dispose(); calm.Dispose(); off.Dispose();
            Object.DestroyImmediate(mat);
        }

        static Transform Child(Transform t, string name)
        {
            foreach (Transform c in t) if (c.name == name) return c;
            return null;
        }

        // Рука игрока в ударе (портрет и ландшафт): звенья не растянуты, локоть согнут (≤ 160°), плечо входит в кадр
        // через нижний край, а не через боковой.
        [Test]
        public void PlayerPunchArmBentFromBottom([Values(true, false)] bool portrait)
        {
            _root = new GameObject("cam");
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var g = new PlayerGloves(_root.transform, GloveLook.FromEntry(GlovesCatalog.All[0]), mat);
            float aspect = portrait ? 540f / 1170f : 1170f / 540f;
            float fov = GameUtil.CalcFov(portrait ? 540 : 1170, portrait ? 1170 : 540);
            g.SetView(fov, aspect);
            float tanV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), tanH = tanV * aspect;
            foreach (var kind in new[] { PunchType.Jab, PunchType.Hook, PunchType.Upper })
                foreach (int side in new[] { -1, 1 })
                {
                    g.Punch(side, kind, 0.2f, new Vector3(side * 0.05f, -0.02f, 1.25f));
                    for (int i = 0; i < 6; i++) g.Tick(1f / 60f);
                    int s = side < 0 ? 0 : 1;
                    var up = Child(_root.transform, "playerUpperArm" + s);
                    var fo = Child(_root.transform, "playerForeArm" + s);
                    string tag = kind + " " + side + (portrait ? " портрет" : " ландшафт");
                    // сетка плеча: локоть в начале, длина по Y = (локоть→сустав + запас 4 см) / UpperLen
                    float ul = GloveLooks.UpperLenCm * 0.01f;
                    Assert.LessOrEqual(up.localScale.y, (ul * 0.86f * 1.02f + 0.04f) / ul, "плечо не растянуто: " + tag);
                    Vector3 el = up.localPosition, wr = fo.localPosition, sh = el - up.up * (ul * up.localScale.y - 0.04f);
                    float ang = Vector3.Angle(sh - el, wr - el);
                    Assert.LessOrEqual(ang, 160f, "локоть согнут: " + tag);
                    // где плечо (от локтя к суставу) выходит из кадра
                    string exit = "в кадре";
                    for (int k = 0; k <= 50; k++)
                    {
                        Vector3 p = Vector3.Lerp(el, sh, k / 50f);
                        if (p.z <= 0.02f) { exit = "низ"; break; }
                        if (p.y < -p.z * tanV) { exit = "низ"; break; }
                        if (Mathf.Abs(p.x) > p.z * tanH) { exit = "бок y=" + (p.y / (p.z * tanV)).ToString("0.00"); break; }
                    }
                    if (exit.StartsWith("бок")) Assert.Less(float.Parse(exit.Substring(6), System.Globalization.CultureInfo.InvariantCulture), -0.7f, "рука входит снизу, а не с бокового края: " + tag + " " + exit);
                    for (int i = 0; i < 30; i++) g.Tick(1f / 60f);
                }
            g.Dispose();
            Object.DestroyImmediate(mat);
        }

        // Перчатка игрока при попадании сплющивается и возвращается к своему размеру; при промахе — нет.
        [Test]
        public void PlayerGloveSquashesOnContact()
        {
            _root = new GameObject("cam");
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var g = new PlayerGloves(_root.transform, GloveLook.FromEntry(GlovesCatalog.All[0]), mat);
            g.Punch(1, PunchType.Hook, 0.2f, new Vector3(0.05f, 0f, 1.25f), true);
            float mx = 0f;
            for (int i = 0; i < 20; i++) { g.Tick(1f / 60f); mx = Mathf.Max(mx, Mathf.Abs(g.Squash(1))); }
            Assert.Greater(mx, 0.03f, "сплющилась");
            for (int i = 0; i < 60; i++) g.Tick(1f / 60f);
            Assert.Less(Mathf.Abs(g.Squash(1)), 0.002f, "вернулась");
            g.Punch(-1, PunchType.Jab, 0.1f, new Vector3(-0.05f, 0f, 1.25f), false);
            mx = 0f;
            for (int i = 0; i < 20; i++) { g.Tick(1f / 60f); mx = Mathf.Max(mx, Mathf.Abs(g.Squash(0))); }
            Assert.Less(mx, 1e-4f, "промах — без сплющивания");
            g.Dispose();
            Object.DestroyImmediate(mat);
        }
    }
}
