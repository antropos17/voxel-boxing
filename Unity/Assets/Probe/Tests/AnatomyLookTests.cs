using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    /// Внешность соперника (Unity/FACES.md): тон кожи, воксельные причёски, волосы уходят вместе с кожей под ними.
    public class AnatomyLookTests
    {
        static int Count(AnatGrid g, byte m) { int n = 0; foreach (var x in g.mat) if (x == m) n++; return n; }

        /// Каждая причёска строится: лысый — без волос, ёжик реже короткой, ирокез и объёмные — толще; брови на месте;
        /// без висящих ячеек; остальные ткани те же, что у вида по умолчанию.
        [Test]
        public void EachHairBuilds()
        {
            var a0 = AnatomyBuilder.Build();
            var n = new int[AnatHair.Count];
            try
            {
                int brow0 = Count(a0.head, AnatMat.Brow);
                for (byte s = 0; s < AnatHair.Count; s++)
                {
                    var a = AnatomyBuilder.Build(new AnatLook { hair = s, skin = AnatLook.Tones[s % AnatLook.Tones.Length], hairCol = new Color32(200, 40, 40, 255) });
                    try
                    {
                        n[s] = Count(a.head, AnatMat.Hair);
                        Assert.AreEqual(brow0, Count(a.head, AnatMat.Brow), "брови, " + AnatHair.Names[s]);
                        var cut = new List<int>(); AnatLooks.Settle(a.head, int3.zero, a.head.dims - 1, cut);
                        Assert.AreEqual(0, cut.Count, "висящие волосы после сборки, " + AnatHair.Names[s]);
                        int diff = 0;
                        for (int i = 0; i < a.head.mat.Length; i++)
                        {
                            byte x = a0.head.mat[i], y = a.head.mat[i];
                            if (x != y && !((x == AnatMat.Hair || x == 0) && (y == AnatMat.Hair || y == 0))) diff++;
                        }
                        Assert.AreEqual(0, diff, "кроме волос ничего не меняется, " + AnatHair.Names[s]);
                        Assert.AreEqual(a0.mand.mat.ToArray(), a.mand.mat.ToArray(), "челюсть, " + AnatHair.Names[s]);
                    }
                    finally { a.Dispose(); }
                }
                Debug.Log("ANAT_TEST hair cells: " + string.Join(", ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, n.Length), k => AnatHair.Names[k] + " " + n[k])));
                Assert.AreEqual(Count(a0.head, AnatMat.Hair), n[AnatHair.Short], "короткая = прежняя");
                Assert.AreEqual(0, n[AnatHair.Bald], "лысый");
                Assert.That(n[AnatHair.Buzz], Is.InRange(n[AnatHair.Short] / 3, n[AnatHair.Short]), "ёжик");
                Assert.Greater(n[AnatHair.Mohawk], n[AnatHair.Short] / 3, "ирокез");
                Assert.Greater(n[AnatHair.Full], n[AnatHair.Short] * 3, "объёмные");
            }
            finally { a0.Dispose(); }
        }

        /// Каждый тон перекрашивает кожу, щетину и губу; подкожные ткани, кровь, кость и срезы не зависят от тона;
        /// тона идут от светлого к тёмному.
        [Test]
        public void EachToneColorsSkinOnly()
        {
            float prev = 1e9f;
            foreach (var t in AnatLook.Tones)
            {
                var look = new AnatLook { skin = t };
                Assert.AreEqual(t, AnatLooks.Surf(AnatMat.Skin, look)); Assert.AreEqual(t, AnatLooks.Surf(AnatMat.Stubble, look));
                for (int m = 0; m < AnatMat.Count; m++)
                    if (m != AnatMat.Skin && m != AnatMat.Stubble && m != AnatMat.Lip) Assert.AreEqual(AnatMat.Table[m].surf, AnatLooks.Surf(m, look), AnatMat.Table[m].name);
                float lum = t.r * 0.3f + t.g * 0.59f + t.b * 0.11f; Assert.Less(lum, prev - 15, "тона заметно разные и темнеют"); prev = lum;
            }
            AnatPalette.Apply(new AnatLook { skin = AnatLook.Tones[4] }); AnatPalette.Apply();   // строится без ошибок, затем прежняя палитра
        }

        /// Вид по умолчанию — прежний: те же ячейки, что Build(), цвета таблицы, тон [1] — тон таблицы.
        [Test]
        public void DefaultIsOld()
        {
            Assert.IsTrue(default(AnatLook).IsDefault);
            for (int m = 0; m < AnatMat.Count; m++) Assert.AreEqual(AnatMat.Table[m].surf, AnatLooks.Surf(m, default));
            Assert.AreEqual(AnatMat.Table[AnatMat.Skin].surf, AnatLook.Tones[1]);
            var a = AnatomyBuilder.Build(); var b = AnatomyBuilder.Build(default(AnatLook)); var c = AnatomyBuilder.Build(new AnatLook { skin = AnatLook.Tones[4], hairCol = new Color32(9, 9, 9, 255) });
            try
            {
                foreach (var g in new[] { b, c })
                {
                    Assert.AreEqual(a.head.mat.ToArray(), g.head.mat.ToArray()); Assert.AreEqual(a.head.part.ToArray(), g.head.part.ToArray());
                    Assert.AreEqual(a.head.zone.ToArray(), g.head.zone.ToArray());
                }
            }
            finally { a.Dispose(); b.Dispose(); c.Dispose(); }
        }

        /// Висящие волосы у свода (в 8 см от точки, куда целят удары): ячейка волос, от которой до ближайшей ткани (не волосы,
        /// не пусто) дальше её высоты над кожей (Body) плюс одна ячейка, — висит в воздухе над вырванной кожей.
        static int Hanging(AnatGrid h, int slack = 1)
        {
            int bad = 0; float3 aim = new float3(0, 9, -1);
            for (int z = 1; z < h.dims.z - 1; z++) for (int y = 1; y < h.dims.y - 1; y++) for (int x = 1; x < h.dims.x - 1; x++)
            {
                int i = h.Index(x, y, z); if (h.mat[i] != AnatMat.Hair) continue;
                float3 p = h.Local(x, y, z); if (math.distance(p, aim) > 8f) continue;
                int need = (int)math.ceil(math.max(0, AnatGeo.Body(p.x, p.y, p.z)) / h.step) + slack;
                bool near = false;
                for (int dz = -need; dz <= need && !near; dz++) for (int dy = -need; dy <= need && !near; dy++) for (int dx = -need; dx <= need && !near; dx++)
                {
                    int3 q = new int3(x + dx, y + dy, z + dz); if (math.any(q < 0) || math.any(q >= h.dims)) continue;
                    byte m = h.mat[h.Index(q.x, q.y, q.z)];
                    near = m != AnatMat.Empty && m != AnatMat.Air && m != AnatMat.Hair && m != AnatMat.Brow && m != AnatMat.Lash;
                }
                if (!near) bad++;
            }
            return bad;
        }

        static (int before, int after, int dropped, int hair, int strict) Beat(byte style, bool fall)
        {
            bool was = AnatView.HairFall; AnatView.HairFall = fall;
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null, new AnatLook { hair = style });
            try
            {
                int before = Hanging(v.a.head);
                float3[] from = { new float3(0, 40, 2), new float3(1.5f, 40, -2), new float3(-1.5f, 40, 4), new float3(0, 30, -30), new float3(25, 30, 0) };
                for (int k = 0; k < 10; k++)
                {
                    var o = from[k % from.Length]; var to = new float3(0, 9, -1);
                    if (!v.Pick(new Ray(v.ToWorld(o), (Vector3)math.normalize(to - o)), out var h, out _, out _)) continue;
                    v.Hit(h, v.pickDir, k % 3 == 2 ? 2 : 1);
                    for (int f = 0; f < 3; f++) v.Tick(1 / 30f);
                }
                v.Settle(true);
                return (before, Hanging(v.a.head), v.hairDropped, Count(v.a.head, AnatMat.Hair), Hanging(v.a.head, 0));
            }
            finally { v.Dispose(); AnatView.HairFall = was; }
        }

        /// Удары по волосистой части: волосы над вырванной кожей уходят, новых висящих ячеек нет; без HairFall — висят.
        [Test]
        public void HitHairLeavesNoHanging()
        {
            foreach (var s in new[] { AnatHair.Full, AnatHair.Mohawk, AnatHair.Short })
            {
                var on = Beat(s, true);
                Debug.Log("ANAT_TEST hair hit " + AnatHair.Names[s] + ": hanging before " + on.before + " after " + on.after + " dropped " + on.dropped + " hair " + on.hair + " strict " + on.strict);
                Assert.AreEqual(0, on.before, "висящие волосы после сборки, " + AnatHair.Names[s]);
                Assert.AreEqual(0, on.after, "висящие волосы после ударов, " + AnatHair.Names[s]);
                if (s == AnatHair.Full)
                {
                    Assert.Greater(on.dropped, 0, "волосы ушли вместе с кожей");
                    var off = Beat(s, false);
                    Debug.Log("ANAT_TEST hair hit без HairFall: hanging " + off.after + " strict " + off.strict);
                    Assert.Greater(off.strict, on.strict, "без HairFall волос далеко от ткани больше");
                }
            }
        }
    }
}
