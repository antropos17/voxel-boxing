using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    /// Изгиб шеи (этап A3, AnatSkin): поза покоя — тождество, низ шеи — якорь, обратный изгиб, луч удара в позе.
    public class AnatomySkinTests
    {
        [TearDown] public void Off() { AnatSkin.Reset(); AnatSkin.on = false; }

        static float3 Deg(float x, float y, float z) => math.radians(new float3(x, y, z));

        [Test]
        public void RestPoseIsIdentityAndBaseIsAnchored()
        {
            var rnd = new Unity.Mathematics.Random(7);
            for (int i = 0; i < 2000; i++)
            {
                float3 p = rnd.NextFloat3(new float3(-9, -20, -11), new float3(9, 14, 12));
                Assert.Less(math.distance(AnatSkin.Skin(p, float3.zero, out _), p), 1e-5f);
            }
            // самая сильная поза: всё ниже T1 (y < −16) неподвижно
            float3 r = Deg(40, 80, 40);
            for (int i = 0; i < 2000; i++)
            {
                float3 p = rnd.NextFloat3(new float3(-9, -20, -11), new float3(9, -16f, 12));
                Assert.Less(math.distance(AnatSkin.Skin(p, r, out _), p), 1e-4f, "якорь сдвинулся: " + p);
            }
        }

        [Test]
        public void ChinAndFaceMoveWithSkull()
        {
            // при повороте на 60° подбородок, нос и затылок поворачиваются на тот же суммарный угол, что и свод
            float3 r = Deg(0, 60, 0);
            foreach (var p in new[] { new float3(0, -8.5f, 8), new float3(0, 0, 11), new float3(0, 12, 0), new float3(0, -2, -9) })
            {
                AnatSkin.Skin(p, r, out var M);
                float ang = math.degrees(math.acos(math.clamp((M.c0.x + M.c1.y + M.c2.z - 1) * 0.5f, -1, 1)));
                Assert.AreEqual(60f, ang, 3f, "точка " + p + " повернулась на " + ang + "°");
            }
        }

        [Test]
        public void UnskinInvertsSkin()
        {
            // у ткани и в 5 мм над кожей обратный изгиб всегда даёт прообраз; внутри тела — ту же точку, кроме складок
            // (кивок вперёд — подбородок входит в горло, поворот — скрутка у угла челюсти): их доля ≤ 2 %
            var rnd = new Unity.Mathematics.Random(11);
            var log = new System.Text.StringBuilder();
            foreach (var r in new[] { Deg(30, 0, 0), Deg(-35, 0, 0), Deg(0, 60, 0), Deg(0, 0, 35), Deg(30, 60, 25), Deg(-30, -75, -35) })
            {
                int near = 0, diverge = 0, inside = 0, other = 0;
                for (int i = 0; i < 3000; i++)
                {
                    float3 p = rnd.NextFloat3(new float3(-9, -20, -11), new float3(9, 14, 12));
                    float sd = AnatGeo.HeadSdf(p.x, p.y, p.z);
                    if (sd > 0.5f) continue;   // воздух дальше 5 мм от кожи луч проходит насквозь, точность там не нужна
                    float3 q = AnatSkin.Skin(p, r, out _);
                    float3 back = AnatSkin.Unskin(q, r);
                    near++; if (math.distance(AnatSkin.Skin(back, r, out _), q) > 0.05f) diverge++;
                    if (sd < -0.3f && p.y > -19.5f) { inside++; if (math.distance(back, p) > 0.05f) other++; }
                }
                log.Append(math.degrees(r)).Append(": не сошлось ").Append(diverge).Append("/").Append(near).Append(", складка ").Append(other).Append("/").Append(inside).Append(" | ");
                Assert.LessOrEqual(diverge, near * 0.02f, "поза " + math.degrees(r) + ": не сошлось " + diverge + " из " + near);
                Assert.LessOrEqual(other, inside * 0.03f, "поза " + math.degrees(r) + ": другой прообраз у " + other + " из " + inside);
            }
            Debug.Log("SKIN_FOLD " + log);
        }

        [Test]
        public void StrikeInTurnedPoseHitsSameCellAsAtRest()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            try
            {
                AnatSkin.Reset(); AnatSkin.on = true;
                // в покое: луч в скулу по нормали
                float3 n = math.normalize(new float3(-0.6f, 0, 1)), aim = new float3(-4.5f, -2.5f, 6);
                var rest = new Ray(v.ToWorld(aim + n * 30), -(Vector3)n);
                Assert.IsTrue(v.Pick(rest, out var h0, out byte m0, out _));
                // поворот на 60° и кивок: тот же луч, повёрнутый вместе с головой у точки удара
                AnatSkin.pose = Deg(15, 60, 10);
                float3 aimP = AnatSkin.Skin(aim, out var M), nP = math.mul(M, n);
                var bent = new Ray(v.ToWorld(aimP + nP * 30), -(Vector3)nP);
                Assert.IsTrue(v.Pick(bent, out var h1, out byte m1, out _), "в позе луч не попал");
                Assert.AreEqual(m0, m1, "в позе первая ткань другая");
                Assert.Less(math.distance(h0, h1), 0.25f, "точка удара в позе ушла от точки в покое: " + h0 + " → " + h1);
                Assert.Greater(math.dot(v.pickDir, -n), 0.98f, "направление удара в покое не совпало");
                // удар убирает ту же ячейку
                int c0 = v.a.head.Lookup(h0 - n * 0.05f);
                Assert.Greater(v.Hit(h1, v.pickDir, 0), 0);
                Assert.AreEqual(AnatMat.Empty, v.a.head.mat[c0]);
            }
            finally { v.Dispose(); }
        }

        [Test]
        public void RecoilSwingsBackAndSettles()
        {
            AnatSkin.Reset();
            AnatSkin.Push(new float3(0, 3, 10), new float3(0, 0, -1), 2);   // тяжёлый в лицо — голова назад (кивок < 0)
            float peak = 0;
            for (int i = 0; i < 30; i++) { AnatSkin.Step(1 / 60f); peak = math.min(peak, AnatSkin.recoil.x); }
            Assert.Less(math.degrees(peak), -15f, "отлёт слабый");
            Assert.Greater(math.degrees(peak), -41f, "отлёт сильнее предела");
            for (int i = 0; i < 240; i++) AnatSkin.Step(1 / 60f);
            Assert.Less(math.length(AnatSkin.recoil), math.radians(0.5f), "не успокоилась за 4 с");
        }
    }
}
