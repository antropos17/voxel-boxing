using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    /// Отсечение скрытого (этап A2): рисуется только то, к чему можно заглянуть снаружи. Каждый тест — на своей свежей голове.
    public class AnatomyVisTests
    {
        AnatHead a;
        [SetUp] public void Make() { a = AnatomyBuilder.Build(); }
        [TearDown] public void Free() { a?.Dispose(); a = null; }

        static bool Seen(AnatGrid g, int i) => g.vis[i] <= AnatVis.Lim && (AnatVis.Open(g.mat[i]) || AnatMat.Clear(g.mat[i]));

        int SeenCount(AnatGrid g) { int n = 0; for (int i = 0; i < g.Count; i++) if (Seen(g, i)) n++; return n; }

        /// Открытые грани тела: сплошная ячейка рядом с видимой открытой или прозрачной (за краем решётки тела — не видно).
        static int Exposed(AnatGrid g)
        {
            int n = 0;
            for (int z = 0; z < g.dims.z; z++) for (int y = 0; y < g.dims.y; y++) for (int x = 0; x < g.dims.x; x++)
            {
                int i = g.Index(x, y, z); byte m = g.mat[i];
                if (AnatVis.Open(m) || AnatMat.Clear(m)) continue;
                for (int d = 0; d < 6; d++)
                {
                    int3 q = new int3(x, y, z) + (d == 0 ? new int3(1, 0, 0) : d == 1 ? new int3(-1, 0, 0) : d == 2 ? new int3(0, 1, 0) : d == 3 ? new int3(0, -1, 0) : d == 4 ? new int3(0, 0, 1) : new int3(0, 0, -1));
                    if (math.any(q < 0) || math.any(q >= g.dims)) continue;
                    if (Seen(g, g.Index(q.x, q.y, q.z))) n++;
                }
            }
            return n;
        }

        /// Видимые ячейки воздуха головы внутри области sdf < -inset (убранная ударом ткань — «пусто» — не в счёт).
        int SeenIn(System.Func<float3, float> sdf, float inset)
        {
            var h = a.head; int n = 0;
            for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
            {
                int i = h.Index(x, y, z); if (!Seen(h, i)) continue;
                if (h.mat[i] == AnatMat.Air && sdf(h.Local(x, y, z)) < -inset) n++;
            }
            return n;
        }

        static float MaxSinus(float3 p) => AnatGeo.MaxSinus(math.abs(p.x), p.y, p.z);
        static float FrontalSinus(float3 p) => AnatGeo.FrontalSinus(math.abs(p.x), p.y, p.z);
        static float MouthCore(float3 p) => AnatGeo.MouthCav(p.x, p.y, p.z);

        /// Убрать ячейки головы в цилиндре p0→p1 радиуса r, кроме занятых телами и keep (как удар).
        NativeList<int> Bore(float3 p0, float3 p1, float r, System.Func<byte, bool> keep)
        {
            var h = a.head; var L = new NativeList<int>(1024, Allocator.Persistent);
            // удар останавливается на первой ячейке keep вдоль оси (кость, мозг): за ней ничего не убирается
            float3 ax = math.normalize(p1 - p0); float stop = float.MaxValue;
            for (int z = 0; z <= h.dims.z - 1; z++) for (int y = 0; y <= h.dims.y - 1; y++) for (int x = 0; x <= h.dims.x - 1; x++)
            {
                int i = h.Index(x, y, z); float3 q = h.Local(x, y, z);
                if (keep(h.mat[i]) && AnatGeo.SegDist(q, p0, p1) <= r) stop = math.min(stop, math.dot(q - p0, ax));
            }
            for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
            {
                int i = h.Index(x, y, z); byte m = h.mat[i];
                if (m == AnatMat.Empty || m == AnatMat.Covered || keep(m)) continue;
                float3 q = h.Local(x, y, z);
                if (AnatGeo.SegDist(q, p0, p1) > r || math.dot(q - p0, ax) >= stop) continue;
                h.mat[i] = AnatMat.Empty; L.Add(i);
            }
            return L;
        }

        /// Число ячеек, где видимость после обновления по месту отличается от полного пересчёта.
        int DiffVsFull()
        {
            var inc = new List<byte[]>(); foreach (var g in a.All) inc.Add(g.vis.ToArray());
            a.vis.Full(); int k = 0, diff = 0;
            foreach (var g in a.All) { var s = inc[k++]; for (int i = 0; i < s.Length; i++) if ((s[i] <= AnatVis.Lim) != (g.vis[i] <= AnatVis.Lim)) diff++; }
            return diff;
        }

        static bool Bone(byte m) => m == AnatMat.Compact || m == AnatMat.Spongy || m == AnatMat.Periost;

        /// Целая голова: челюсть и зубы не видны вовсе, пазухи и глубина рта скрыты, глаза видны.
        [Test]
        public void IntactHidesInside()
        {
            Assert.That(Exposed(a.mand), Is.EqualTo(0), "нижняя челюсть");
            int teeth = 0; foreach (var t in a.teeth) teeth += Exposed(t);
            Assert.That(teeth, Is.EqualTo(0), "зубы");
            Assert.That(SeenIn(MaxSinus, 0.2f), Is.EqualTo(0), "гайморовы пазухи");
            Assert.That(SeenIn(FrontalSinus, 0.2f), Is.EqualTo(0), "лобные пазухи");
            Assert.That(SeenIn(MouthCore, 0.5f), Is.EqualTo(0), "глубина рта");
            Assert.That(SeenCount(a.eyeL), Is.GreaterThan(0), "глаз");
        }

        /// Передняя камера глаза видна целиком: сквозь роговицу и влагу видна радужка.
        [Test]
        public void EyeChamberSeenThrough()
        {
            foreach (var e in new[] { a.eyeL, a.eyeR })
            {
                int aq = 0, hidden = 0;
                for (int i = 0; i < e.Count; i++) if (e.mat[i] == AnatMat.Aqueous) { aq++; if (e.vis[i] > AnatVis.Lim) hidden++; }
                Assert.That(aq, Is.GreaterThan(0), e.name);
                Assert.That(hidden, Is.EqualTo(0), e.name + ": скрыто " + hidden + " из " + aq);
            }
        }

        /// Пробой щеки открывает зубы и стенку рта; обновление по месту совпадает с полным пересчётом.
        [Test]
        public void CheekBreachRevealsMouth()
        {
            int before = SeenIn(MouthCore, 0.5f);
            var cells = Bore(new float3(7.2f, -5.0f, 5.0f), new float3(2.0f, -5.0f, 5.6f), 0.7f, m => false);
            int flips = a.vis.Opened(cells.AsArray()); cells.Dispose();
            Assert.That(flips, Is.GreaterThan(0));
            int teeth = 0; foreach (var t in a.teeth) teeth += Exposed(t);
            int mouth = SeenIn(MouthCore, 0.5f);
            Debug.Log("ANAT_TEST cheek breach: flips " + flips + ", teeth faces seen " + teeth + ", mouth cells seen " + before + " → " + mouth);
            Assert.That(teeth, Is.GreaterThan(0), "зубы");
            Assert.That(mouth, Is.GreaterThan(before), "полость рта");
            Assert.That(DiffVsFull(), Is.EqualTo(0));
        }

        /// Удар до кости над гайморовой пазухой (кость цела): пазуха остаётся скрытой.
        [Test]
        public void HitShortOfSinusKeepsItHidden()
        {
            var cells = Bore(new float3(3.3f, -0.6f, 13f), new float3(2.4f, -0.4f, 2f), 0.6f, Bone);
            Assert.That(cells.Length, Is.GreaterThan(100));
            a.vis.Opened(cells.AsArray()); cells.Dispose();
            Assert.That(SeenIn(MaxSinus, 0.2f), Is.EqualTo(0), "гайморова пазуха");
            Assert.That(DiffVsFull(), Is.EqualTo(0));
        }
    }
}
