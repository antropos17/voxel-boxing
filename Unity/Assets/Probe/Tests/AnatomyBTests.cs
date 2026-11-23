using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    /// Этап B через просмотр (AnatView): поза, видимость, пределы при ударах мышью.
    public class AnatomyBTests
    {
        static float3 Deg(float a, float b, float c) => math.radians(new float3(a, b, c));

        struct Sum { public long red, bruise, swell, blood, dmg; public int removed, wounds; }
        static Sum Take(AnatView v)
        {
            var s = new Sum { removed = v.sim.removed, wounds = v.sim.wounds.Count }; var h = v.a.head;
            for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
            {
                var q = v.sim.Get(x, y, z); s.red += q.red; s.bruise += q.bruise; s.swell += q.swell; s.blood += q.blood; s.dmg += q.dmg;
            }
            return s;
        }

        /// Серия средних ударов в скулу в позе (кивок 15°, поворот 60°, наклон 10°) даёт ту же картину, что в покое:
        /// те же убранные ячейки, раны, покраснение, синяк и отёк (луч в позе переводится в покой).
        [Test]
        public void HitsInPoseMatchRest()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            try
            {
                float3 n = math.normalize(new float3(-0.6f, 0, 1)), aim = new float3(-4.5f, -2.5f, 6);
                Sum[] res = new Sum[2];
                for (int pass = 0; pass < 2; pass++)
                {
                    AnatSkin.Reset(); AnatSkin.on = true; v.Restore();
                    if (pass == 1) AnatSkin.pose = Deg(15, 60, 10);
                    float3 aimP = AnatSkin.Skin(aim, out var M), nP = math.mul(M, n);
                    for (int k = 0; k < 6; k++)
                    {
                        Assert.IsTrue(v.Pick(new Ray(v.ToWorld(aimP + nP * 30), -(Vector3)nP), out var h, out _, out _), "луч не попал, проход " + pass);
                        v.Hit(h, v.pickDir, 1);
                    }
                    res[pass] = Take(v);
                }
                Debug.Log("ANAT_TEST pose: rest removed " + res[0].removed + " red " + res[0].red + " bruise " + res[0].bruise + " swell " + res[0].swell
                    + "; pose removed " + res[1].removed + " red " + res[1].red + " bruise " + res[1].bruise + " swell " + res[1].swell);
                Assert.Greater(res[0].bruise, 0);
                Assert.That(res[1].removed, Is.EqualTo(res[0].removed).Within(math.max(3, res[0].removed / 10)), "убрано");
                Assert.That((double)res[1].red, Is.EqualTo(res[0].red).Within(res[0].red * 0.1), "покраснение");
                Assert.That((double)res[1].bruise, Is.EqualTo(res[0].bruise).Within(res[0].bruise * 0.1), "синяк");
                Assert.That((double)res[1].swell, Is.EqualTo(res[0].swell).Within(res[0].swell * 0.1 + 1), "отёк");
            }
            finally { AnatSkin.Reset(); AnatSkin.on = false; v.Dispose(); }
        }

        /// Тяжесть в позе: при наклоне головы к плечу на 40° кровь из рассечённой брови уходит в рамке покоя вбок, а не только вниз.
        [Test]
        public void BloodFollowsPoseGravity()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            try
            {
                float[] side = new float[2];
                for (int pass = 0; pass < 2; pass++)
                {
                    AnatSkin.Reset(); AnatSkin.on = true; v.Restore();
                    if (pass == 1) AnatSkin.pose = Deg(0, 0, 40);
                    for (int k = 0; k < 20 && v.sim.wounds.Count == 0; k++) { v.Hit(new float3(2.6f, 4.4f, 14), new float3(0, 0, -1), 1); v.Tick(0.3f, 4096); }
                    Assert.Greater(v.sim.wounds.Count, 0, "бровь не рассечена");
                    var w = v.sim.wounds[0]; float3 wp = v.a.head.Local(w.x, w.y, w.z);
                    var h = v.a.head; var b0 = new byte[h.Count];
                    for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++) b0[h.Index(x, y, z)] = v.sim.Get(x, y, z).blood;
                    for (int t = 0; t < 240; t++) v.Tick(1 / 30f, 64);
                    double sx = 0, sw = 0;   // центр прибавившейся крови относительно раны
                    for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
                    {
                        int add = v.sim.Get(x, y, z).blood - b0[h.Index(x, y, z)]; if (add <= 0) continue;
                        float3 p = h.Local(x, y, z); if (math.distance(p, wp) > 6) continue;
                        sx += (p.x - wp.x) * add; sw += add;
                    }
                    side[pass] = (float)(sx / math.max(1, sw));
                }
                float3 g = AnatView.Down();
                Debug.Log("ANAT_TEST pose gravity: blood x-offset rest " + side[0] + " tilted " + side[1] + ", down " + g);
                Assert.Greater(math.abs(side[1] - side[0]), 0.3f, "наклон не изменил, куда стекает кровь");
                Assert.AreEqual(math.sign(g.x), math.sign(side[1] - side[0]), "кровь ушла не в ту сторону");
            }
            finally { AnatSkin.Reset(); AnatSkin.on = false; v.Dispose(); }
        }

        /// После серии ударов всех сил видимость по месту (AnatVis.Opened) совпадает с полным пересчётом.
        [Test]
        public void DamageKeepsVisibilityConsistent()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            try
            {
                var r = new System.Random(5);
                for (int k = 0; k < 24; k++)
                {
                    var p = new float3((float)(r.NextDouble() * 10 - 5), (float)(r.NextDouble() * 14 - 6), 14);
                    v.Hit(p, new float3(0, 0, -1), k % 3); v.Tick(0.2f, 64);
                }
                var h = v.a.head; var inc = h.vis.ToArray();
                v.a.vis.Full();
                int diff = 0; for (int i = 0; i < inc.Length; i++) if (inc[i] != h.vis[i]) diff++;
                Assert.AreEqual(0, diff, "ячеек с другой видимостью");
            }
            finally { v.Dispose(); }
        }

        /// Пределы в просмотре: 150 ударов всех сил с ходом времени — раны, подтёки и капли не выходят за пределы.
        [Test]
        public void ViewLimitsHold()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            try
            {
                var r = new System.Random(9);
                for (int k = 0; k < 150; k++)
                {
                    var p = new float3((float)(r.NextDouble() * 12 - 6), (float)(r.NextDouble() * 18 - 9), 14);
                    v.Hit(p, math.normalize(new float3((float)(r.NextDouble() - 0.5), (float)(r.NextDouble() - 0.5), -2)), r.Next(3));
                    for (int s = 0; s < 4; s++)
                    {
                        v.Tick(1 / 30f, 12);
                        Assert.LessOrEqual(v.sim.wounds.Count, AnatDamage.MaxWounds); Assert.LessOrEqual(v.sim.drips.Count, AnatDamage.MaxDrips); Assert.LessOrEqual(v.sim.drops.Count, AnatDamage.MaxDrops);
                    }
                }
                Debug.Log("ANAT_TEST view limits: peaks " + v.sim.peakWounds + "/" + v.sim.peakDrips + "/" + v.sim.peakDrops);
            }
            finally { v.Dispose(); }
        }

        // ---------- часть 2: куски, культя ----------

        static int Cnt(AnatGrid h, System.Func<float3, byte, bool> f)
        {
            int n = 0;
            for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
            { byte m = h.mat[h.Index(x, y, z)]; if (m != AnatMat.Empty && f(h.Local(x, y, z), m)) n++; }
            return n;
        }

        /// Целая голова: полный поиск кусков не делает ни одного куска и не отрывает ни челюсть, ни глаза, ни зубы.
        [Test]
        public void IntactHeadHoldsTogether()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            try
            {
                v.Settle(true);
                Assert.AreEqual(0, v.pieces.list.Count, "кусков у целой головы");
                for (int k = 0; k < v.gone.Length; k++) Assert.IsFalse(v.gone[k], "оторвалось тело " + k);
                Debug.Log("ANAT_TEST intact: crumbs " + v.crumbs + " orphans " + v.orphans + " island ms " + v.isl.lastMs.ToString("0.0"));
            }
            finally { v.Dispose(); }
        }

        /// Срез свода плоскостью: всё выше — один кусок, он падает и засыпает на полу; в голове над срезом ничего не висит.
        [Test]
        public void CutVaultBecomesPiece()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            try
            {
                var h = v.a.head;
                int above = Cnt(h, (p, m) => p.y >= 8.5f && AnatIslands.Links(m) && m != AnatMat.Covered);
                for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
                {
                    int i = h.Index(x, y, z); float py = h.Local(x, y, z).y;
                    if (py > 8 && py < 8.5f && h.mat[i] != AnatMat.Empty) { v.sim.Take(i); v.sim.cutLog.Add(i); }
                }
                v.sim.Flush();
                v.Settle(true);
                Assert.GreaterOrEqual(v.pieces.list.Count, 1, "кусков");
                AnatPiece big = null; foreach (var pc in v.pieces.list) if (big == null || pc.cells > big.cells) big = pc;
                int left = Cnt(h, (p, m) => p.y >= 8.5f && AnatIslands.Links(m));
                Assert.Greater(big.cells, above * 0.8f, "в куске мало ячеек");
                Assert.Less(left, above * 0.02f, "над срезом осталось в голове");
                float y0 = big.x.y;
                for (int f = 0; f < 600 && !big.asleep; f++) v.Tick(1 / 60f, 64);
                Assert.IsTrue(big.asleep, "кусок не уснул"); Assert.Less(big.x.y, y0 - 20, "кусок не упал");
                Assert.Greater(big.x.y, AnatPieces.Floor - 2, "кусок провалился под пол");
                Debug.Log("ANAT_TEST cut vault: above " + above + " piece " + big.cells + " left " + left + " fell to " + big.x.y.ToString("0.0"));
            }
            finally { v.Dispose(); }
        }

        /// Предел кусков: при 40 новых кусках остаётся MaxPieces, убираются самые старые.
        [Test]
        public void PiecesTrimOldest()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            var ps = new AnatPieces(null, null);
            var cells = new Unity.Collections.NativeList<int>(128, Unity.Collections.Allocator.Persistent);
            try
            {
                var h = v.a.head;
                for (int i = 0; i < h.Count && cells.Length < 100; i++) if (h.mat[i] == AnatMat.Skin) cells.Add(i);
                for (int k = 0; k < 40; k++) ps.Make(h, null, cells.AsArray(), 0, cells.Length, 0, "t");
                Assert.AreEqual(AnatPieces.MaxPieces, ps.list.Count);
                Assert.AreEqual(40 - AnatPieces.MaxPieces, ps.trimmed);
                Assert.AreEqual(ps.list[1].id - 1, ps.list[0].id, "порядок");
                Assert.AreEqual(100 + 40 - AnatPieces.MaxPieces, ps.list[0].id, "убраны не самые старые");
            }
            finally { ps.Clear(); cells.Dispose(); v.Dispose(); }
        }

        /// Серия тяжёлых ударов: куски и ячейки кусков в пределах, культя ниже StumpAt не тронута,
        /// на срезе культи есть кость, артерия, вена, мышца, кожа и дыхательные пути (воздух или хрящ).
        [Test]
        public void HeavyHitsKeepPieceLimitsAndStump()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            try
            {
                var h = v.a.head;
                var keep = new System.Collections.Generic.List<int>(); var was = new System.Collections.Generic.List<byte>();
                for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
                { int i = h.Index(x, y, z); if (h.mat[i] != AnatMat.Empty && AnatDamage.Protected(h.Local(x, y, z) + new float3(0, 0.6f, 0))) { keep.Add(i); was.Add(h.mat[i]); } }
                var r = new System.Random(11);
                for (int k = 0; k < 60; k++)
                {
                    var p = new float3((float)(r.NextDouble() * 12 - 6), (float)(r.NextDouble() * 16 - 4), 14);
                    v.Hit(p, math.normalize(new float3((float)(r.NextDouble() - 0.5), (float)(r.NextDouble() - 0.5), -2)), 2);
                    for (int s = 0; s < 3; s++)
                    {
                        v.Tick(1 / 30f, 12);
                        Assert.LessOrEqual(v.pieces.list.Count, AnatPieces.MaxPieces);
                        Assert.IsTrue(v.pieces.list.Count <= 1 || v.pieces.Cells <= AnatPieces.MaxCells, "ячеек в кусках");
                    }
                }
                v.Settle();
                int changed = 0; for (int j = 0; j < keep.Count; j++) if (h.mat[keep[j]] != was[j]) changed++;
                Assert.AreEqual(0, changed, "культя изменилась");
                var seen = new System.Collections.Generic.HashSet<byte>();
                for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
                {
                    float3 p = h.Local(x, y, z); float s0 = AnatDamage.StumpAt(p.x, p.z);
                    if (p.y < s0 && p.y > s0 - 0.5f) seen.Add(h.mat[h.Index(x, y, z)]);
                }
                Assert.IsTrue(seen.Contains(AnatMat.Compact) || seen.Contains(AnatMat.Spongy), "кость на срезе");
                Assert.IsTrue(seen.Contains(AnatMat.Artery), "артерия на срезе"); Assert.IsTrue(seen.Contains(AnatMat.Vein), "вена на срезе");
                Assert.IsTrue(seen.Contains(AnatMat.Muscle), "мышца на срезе"); Assert.IsTrue(seen.Contains(AnatMat.Skin), "кожа на срезе");
                Assert.IsTrue(seen.Contains(AnatMat.Air) || seen.Contains(AnatMat.Cartilage), "дыхательные пути на срезе");
                Debug.Log("ANAT_TEST heavy: pieces made " + v.pieces.made + " peak " + v.pieces.peak + " trimmed " + v.pieces.trimmed + " crumbs " + v.crumbs + " rips " + v.sim.ripCount + " fractures " + v.sim.fractures.Count);
            }
            finally { v.Dispose(); }
        }

        /// Часть 3: журнал событий ограничен — сверх MaxEvents записи не добавляются, только счёт потерянных.
        [Test]
        public void EventsCapped()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            try
            {
                var d = v.sim; int before = d.events.Count;
                for (int k = 0; k < 1000; k++) if (d.CanLog()) d.events.Add("x");
                Assert.AreEqual(AnatDamage.MaxEvents, d.events.Count);
                Assert.AreEqual(1000 - (AnatDamage.MaxEvents - before), d.eventsLost);
                var r = new System.Random(5);
                for (int k = 0; k < 10; k++) v.Hit(new float3((float)(r.NextDouble() * 8 - 4), (float)(r.NextDouble() * 10 - 2), 14), new float3(0, 0, -1), 2);
                Assert.AreEqual(AnatDamage.MaxEvents, d.events.Count, "удары не дописывают сверх предела");
            }
            finally { v.Dispose(); }
        }

        /// Часть 3: мелкий кусок держит блок состояний по своему размеру, а не чанк 32³; состояния переносятся на свои места.
        [Test]
        public void SmallPieceStateIsSmall()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            var ps = new AnatPieces(null, null);
            var cells = new Unity.Collections.NativeList<int>(128, Unity.Collections.Allocator.Persistent);
            try
            {
                var h = v.a.head;
                for (int i = 0; i < h.Count && cells.Length < 100; i++) if (h.mat[i] == AnatMat.Skin) cells.Add(i);
                for (int k = 0; k < cells.Length; k++) { int i = cells[k]; v.sim.st.Set(i % h.dims.x, (i / h.dims.x) % h.dims.y, i / (h.dims.x * h.dims.y), new VS { blood = (byte)(10 + k) }); }
                var p = ps.Make(h, v.sim.st, cells.AsArray(), 0, cells.Length, 0, "t");
                Assert.AreEqual(cells.Length, p.cells);
                Assert.Less(p.st.Bytes, AnatState.CV * 12 / 8, "блок состояний мелкого куска");
                for (int k = 0; k < cells.Length; k++)
                {
                    int i = cells[k]; int3 l = new int3(i % h.dims.x, (i / h.dims.x) % h.dims.y, i / (h.dims.x * h.dims.y)) - p.lo + 1;
                    Assert.AreEqual(10 + k, p.st.Get(l.x, l.y, l.z).blood, "состояние ячейки " + k);
                }
                Debug.Log("ANAT_TEST small piece: dims " + p.g.dims + " state bytes " + p.st.Bytes);
            }
            finally { ps.Clear(); cells.Dispose(); v.Dispose(); }
        }

        /// Часть 3: одинаковые удары и кадры дают одинаковый итог (поиск кусков разбирается в назначенном кадре, а не
        /// когда джоб успел): те же убранные ячейки, куски и их размеры, та же решётка головы.
        [Test]
        public void SameHitsSameResult()
        {
            string Run()
            {
                var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
                try
                {
                    var r = new System.Random(21); var h = v.a.head;
                    for (int k = 0; k < 30; k++)
                    {
                        var p = new float3((float)(r.NextDouble() * 12 - 6), (float)(r.NextDouble() * 16 - 4), 14);
                        v.Hit(p, math.normalize(new float3((float)(r.NextDouble() - 0.5), (float)(r.NextDouble() - 0.5), -2)), 2);
                        for (int s = 0; s < 3; s++) v.Tick(1 / 30f, 12);
                    }
                    long hash = 17; for (int i = 0; i < h.Count; i++) hash = hash * 31 + h.mat[i];
                    var sb = new System.Text.StringBuilder();
                    sb.Append("removed ").Append(v.sim.removed).Append(" made ").Append(v.pieces.made).Append(" crumbs ").Append(v.crumbs).Append(" off ").Append(v.detachedBodies).Append(" hash ").Append(hash).Append(" pieces");
                    foreach (var p in v.pieces.list) sb.Append(' ').Append(p.cells);
                    return sb.ToString();
                }
                finally { v.Dispose(); }
            }
            string a = Run(), b = Run();
            Debug.Log("ANAT_TEST same: " + a);
            Assert.AreEqual(a, b);
        }
    }
}
