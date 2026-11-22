using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace VoxProbe.Tests
{
    /// Строение анатомической головы (этап A, docs/HEAD-SPEC.md, разделы 1–3 и 10).
    public class AnatomyTests
    {
        static AnatHead head;

        [OneTimeSetUp] public void Build() => head = AnatomyBuilder.Build();
        [OneTimeTearDown] public void Free() { head?.Dispose(); head = null; }

        /// Зоны, где модель расходится с таблицей раздела 3 по известной причине (записано в Unity/ANATOMY-A.md).
        /// Тест ниже требует, чтобы расходились ровно они: исправление любой из них заметно, новое расхождение — тоже.
        static readonly string[] KnownDeviations = { "бровь", "ухо" };

        [Test]
        public void LayersMatchSpecTable()
        {
            var bad = new List<string>(); var report = new System.Text.StringBuilder();
            foreach (var r in AnatChecks.Rays)
            {
                var res = AnatChecks.Check(head, r);
                if (!res.ok) bad.Add(r.zone);
                report.Append(r.zone).Append(res.ok ? " ок\n" : " РАСХОЖДЕНИЕ\n").Append(res.report);
            }
            CollectionAssert.AreEquivalent(KnownDeviations, bad, report.ToString());
        }

        [Test]
        public void SinusesAreRealVoidsInRange()
        {
            var v = AnatTools_SinusVolumes();
            Assert.That(v["frontal"], Is.InRange(3.0, 8.0), "лобная, мл");
            Assert.That(v["maxL"], Is.InRange(10.0, 20.0), "верхнечелюстная левая, мл");
            Assert.That(v["maxR"], Is.InRange(10.0, 20.0), "верхнечелюстная правая, мл");
            Assert.That(v["sphenoid"], Is.InRange(3.0, 12.0), "клиновидная, мл");
            Assert.That(v["ethmoid"], Is.InRange(2.0, 8.0), "решётчатая, мл");
        }

        /// Тот же подсчёт, что в редакторном инструменте (он в сборке Editor, тесты её не видят).
        static Dictionary<string, double> AnatTools_SinusVolumes()
        {
            var g = head.head; double cv = g.step * g.step * g.step;
            var d = new Dictionary<string, double> { ["frontal"] = 0, ["maxL"] = 0, ["maxR"] = 0, ["sphenoid"] = 0, ["ethmoid"] = 0 };
            for (int z = 0; z < g.dims.z; z++) for (int y = 0; y < g.dims.y; y++) for (int x = 0; x < g.dims.x; x++)
            {
                if (g.mat[g.Index(x, y, z)] != AnatMat.Air) continue;
                var p = g.Local(x, y, z); float ax = math.abs(p.x);
                if (AnatGeo.FrontalSinus(ax, p.y, p.z) < 0) d["frontal"] += cv;
                else if (AnatGeo.MaxSinus(ax, p.y, p.z) < 0) d[p.x >= 0 ? "maxL" : "maxR"] += cv;
                else if (AnatGeo.SphSinus(p.x, p.y, p.z) < 0) d["sphenoid"] += cv;
                else if (AnatGeo.EthmoidBox(ax, p.y, p.z, out _)) d["ethmoid"] += cv;
            }
            return d;
        }

        [Test]
        public void ThirtyTwoTeethWithRootsInBone()
        {
            Assert.AreEqual(32, head.teeth.Length);
            var fdi = head.toothDefs.Select(t => t.fdi).OrderBy(x => x).ToArray();
            var want = new[] { 11, 12, 13, 14, 15, 16, 17, 18, 21, 22, 23, 24, 25, 26, 27, 28, 31, 32, 33, 34, 35, 36, 37, 38, 41, 42, 43, 44, 45, 46, 47, 48 };
            CollectionAssert.AreEqual(want, fdi);
            for (int k = 0; k < 32; k++)
            {
                var g = head.teeth[k]; int enamel = 0, dentin = 0, pulp = 0;
                for (int i = 0; i < g.Count; i++) { byte m = g.mat[i]; if (m == AnatMat.Enamel) enamel++; else if (m == AnatMat.Dentin) dentin++; else if (m == AnatMat.Pulp) pulp++; }
                Assert.That(enamel > 0 && dentin > 0 && pulp > 0, "зуб " + head.toothDefs[k].fdi + ": эмаль, дентин и пульпа");
            }
            var roots = AnatChecks.RootsInBone(head, out int withRoots);
            Assert.AreEqual(32, withRoots);
            for (int k = 0; k < 32; k++)
                Assert.That(roots[k], Is.GreaterThanOrEqualTo(0.75f), "корень зуба " + head.toothDefs[k].fdi + " в кости, доля");
        }

        [Test]
        public void EyesSitInOrbits()
        {
            foreach (var e in new[] { head.eyeL, head.eyeR })
            {
                var r = AnatChecks.EyeInOrbit(head, e);
                Assert.That(r.wrapped, Is.GreaterThan(0.9f), e.name + ": голова освободила место под глаз (Covered)");
                Assert.That(r.minWall, Is.GreaterThan(AnatGeo.ER), e.name + ": кость глазницы не заходит в глазное яблоко");
                Assert.That(r.maxWall, Is.LessThan(3.0f), e.name + ": стенки глазницы рядом");
                Assert.That(r.openDirs, Is.LessThanOrEqualTo(9), e.name + ": открыта только передняя сторона (9 из 26 направлений)");
            }
        }

        [Test]
        public void MandibleIsSeparateBodyWithJoint()
        {
            var g = head.mand; int bone = 0, cart = 0;
            for (int i = 0; i < g.Count; i++) { byte m = g.mat[i]; if (m == AnatMat.Compact || m == AnatMat.Spongy) bone++; else if (m == AnatMat.Cartilage) cart++; }
            Assert.That(bone, Is.GreaterThan(5000), "кость нижней челюсти");
            Assert.That(cart, Is.GreaterThan(10), "хрящ суставной головки");
            // голова уступает место челюсти: ячейки под телом челюсти в голове — Covered
            int covered = 0;
            for (int i = 0; i < head.head.Count; i++) if (head.head.mat[i] == AnatMat.Covered && head.head.part[i] == 2) covered++;
            Assert.That(covered, Is.GreaterThan(3000));
        }

        [Test]
        public void BrainHasAllParts()
        {
            var n = new int[AnatMat.Count];
            var g = head.head; for (int i = 0; i < g.Count; i++) n[g.mat[i]]++;
            foreach (var m in new[] { AnatMat.Dura, AnatMat.Csf, AnatMat.Grey, AnatMat.White, AnatMat.CerebGrey, AnatMat.Stem, AnatMat.VenSinus })
                Assert.That(n[m], Is.GreaterThan(100), AnatMat.Table[m].name);
            Assert.That(n[AnatMat.Grey], Is.GreaterThan(n[AnatMat.White] / 3), "кора не тоньше трети белого вещества по объёму");
        }

        /// Сетка со сглаживанием: каждая открытая грань покрыта ровно один раз (сумма площадей объединённых прямоугольников
        /// плюс одиночные сглаженные грани = число открытых граней), во всех телах.
        [Test]
        public void SmoothMeshCoversEveryOpenFaceOnce()
        {
            foreach (var g in head.All)
            {
                var nch = (g.dims + AnatMesher.CS - 1) / AnatMesher.CS;
                long naive = 0, area = 0;
                var verts = new NativeList<AVert>(16384, Allocator.Temp);
                for (int z = 0; z < nch.z; z++) for (int y = 0; y < nch.y; y++) for (int x = 0; x < nch.x; x++)
                {
                    verts.Clear();
                    naive += AnatMesher.Build(g.mat, g.dims, new int3(x, y, z) * AnatMesher.CS, AnatMesher.ModeRender, ref verts);
                    for (int q = 0; q < verts.Length; q += 4)
                    {
                        float3 a = P(verts[q]), b = P(verts[q + 1]), c = P(verts[q + 2]), d = P(verts[q + 3]);
                        bool lattice = IsInt(a) && IsInt(b) && IsInt(c) && IsInt(d);
                        if (!lattice) { area += 1; continue; }
                        float s = math.length(math.cross(b - a, d - a));
                        area += (long)math.round(s);
                    }
                }
                verts.Dispose();
                Assert.AreEqual(naive, area, g.name);
            }
        }

        static float3 P(AVert v) => new float3(v.x, v.y, v.z);
        static bool IsInt(float3 p) => math.all(math.abs(p - math.round(p)) < 1e-3f);
    }
}
