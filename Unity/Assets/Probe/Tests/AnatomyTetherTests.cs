using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    /// Висящие куски (AnatPieces.Tethers, SPRING-PHYSICS.md): мягкий островок висит на упругой нити, рвётся и падает;
    /// при любом кадре без NaN; висящих не больше MaxTethered; без флага кусок сразу падает, как раньше.
    public class AnatomyTetherTests
    {
        AnatView v; AnatPieces ps; Unity.Collections.NativeList<int> cells; bool was;

        [SetUp]
        public void SetUp()
        {
            was = AnatPieces.Tethers;
            AnatSkin.Reset(); AnatSkin.on = false;
            v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            ps = new AnatPieces(null, null);
            cells = new Unity.Collections.NativeList<int>(128, Unity.Collections.Allocator.Persistent);
            var h = v.a.head;
            for (int i = 0; i < h.Count && cells.Length < 100; i++) if (h.mat[i] == AnatMat.Skin) cells.Add(i);
        }

        [TearDown]
        public void TearDown() { AnatPieces.Tethers = was; ps.Clear(); cells.Dispose(); v.Dispose(); }

        AnatPiece Make() => ps.Make(v.a.head, null, cells.AsArray(), 0, cells.Length, new float3(0, 60, 140), "кусок");

        [Test]
        public void AnatomyPieceHangsThenFalls([Values(1f / 30f, 1f / 60f, 1f / 120f)] float dt)
        {
            AnatPieces.Tethers = true;
            var p = Make();
            Assert.IsTrue(p.teth, "мягкий кусок висит");
            float maxD = 0;
            for (float t = 0; t < 0.5f; t += dt)
            {
                ps.Step(dt);
                Assert.IsFalse(math.any(math.isnan(p.x)), "NaN");
                if (p.teth) maxD = math.max(maxD, math.length(p.x - p.anchor));
            }
            Assert.IsTrue(p.teth, "через 0,5 с ещё висит (срок ≥ 0,6 с)");
            Assert.LessOrEqual(maxD, p.tethLen + AnatPieces.TearOver + 0.5f, "на нити, не улетел");
            Assert.Greater(p.x.y, AnatPieces.Floor + 5f, "не на полу");
            for (float t = 0; t < 3f; t += dt) ps.Step(dt);
            Assert.IsFalse(p.teth, "нить порвалась");
            Assert.Less(p.x.y, AnatPieces.Floor + 6f, "упал на пол");
        }

        [Test]
        public void AnatomyTetheredLimited()
        {
            AnatPieces.Tethers = true;
            for (int k = 0; k < 12; k++) Make();
            int n = 0; foreach (var q in ps.list) if (q.teth) n++;
            Assert.AreEqual(AnatPieces.MaxTethered, n);
            ps.Step(1 / 60f);
            Assert.LessOrEqual(ps.tethered, AnatPieces.MaxTethered);
        }

        [Test]
        public void AnatomyNoTetherWhenOff()
        {
            AnatPieces.Tethers = false;
            var p = Make();
            Assert.IsFalse(p.teth);
            for (int i = 0; i < 60; i++) ps.Step(1 / 60f);
            Assert.Less(p.x.y, AnatPieces.Floor + 6f, "упал за 1 с");
        }
    }
}
