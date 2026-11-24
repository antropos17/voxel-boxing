using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    /// Этап B, часть 8: ветки подтёков и пятна вокруг ран не трогают случайные числа и ограничены; упрощённый шейдер включается ключевым словом.
    public class AnatomyB8Tests
    {
        static readonly float3 Brow = new float3(2.6f, 4.4f, 14), Nose = new float3(0, 0.8f, 16), Back = new float3(0, 0, -1);

        static (byte[] mat, int frac, int wounds, int kidSeq, int kids, int pool, int spread, int blood) Fight(bool branches)
        {
            bool was = AnatDamage.Branches; AnatDamage.Branches = branches;
            var a = AnatomyBuilder.Build(); var d = new AnatDamage(a);
            try
            {
                for (int k = 0; k < 3; k++) { d.Hit(Brow, Back, 3.4f, true); for (int f = 0; f < 45; f++) d.Step(1 / 30f); }
                for (int k = 0; k < 3; k++) { d.Hit(Nose, Back, 3.0f, true); for (int f = 0; f < 45; f++) d.Step(1 / 30f); }
                for (int f = 0; f < 900; f++) d.Step(1 / 30f);
                int nb = 0; foreach (var s in d.st.pool.AsArray()) if (s.blood > 0) nb++;
                return (a.head.mat.ToArray(), d.fractures.Count, d.wounds.Count, d.kidSeq, d.kids, d.poolCells, d.spreadCells, nb);
            }
            finally { d.Dispose(); a.Dispose(); AnatDamage.Branches = was; }
        }

        /// С ветками и пятнами разрушение то же (rng не сдвинут), крови больше, все счётчики в своих пределах.
        [Test]
        public void BranchesKeepScenarioAndBounds()
        {
            var off = Fight(false); var on = Fight(true);
            Assert.AreEqual(off.mat, on.mat, "материалы головы после ударов");
            Assert.AreEqual(off.frac, on.frac); Assert.AreEqual(off.wounds, on.wounds);
            Assert.AreEqual(0, off.kidSeq); Assert.Greater(on.kidSeq, 0, "ветки появились"); Assert.Greater(on.pool, 0, "пятно вокруг раны");
            Assert.Greater(on.blood, off.blood, "ячеек с кровью больше");
            Assert.LessOrEqual(on.kids, AnatDamage.MaxKids); Assert.LessOrEqual(on.kidSeq, AnatDamage.MaxKidsAll); Assert.LessOrEqual(on.spread, AnatDamage.MaxSpread);
        }

        /// Упрощённый шейдер — глобальное ключевое слово ANAT_LITE.
        [Test]
        public void LiteKeyword()
        {
            bool was = AnatPalette.Lite;
            try
            {
                AnatPalette.SetLite(true); Assert.IsTrue(Shader.IsKeywordEnabled("ANAT_LITE"));
                AnatPalette.SetLite(false); Assert.IsFalse(Shader.IsKeywordEnabled("ANAT_LITE"));
            }
            finally { AnatPalette.SetLite(was); }
        }

        /// Номер мышцы для шейдера: у головы есть мышцы, у каждой мышцы направление волокон задано (единичный вектор).
        [Test]
        public void MuscleFibers()
        {
            var a = AnatomyBuilder.Build();
            try
            {
                int mus = 0; foreach (var p in a.head.part) if (AnatPart.IsMuscle(p)) mus++;
                Assert.Greater(mus, 1000);
                for (int p = 32; p < 64; p++) { var f = AnatGeo.Fiber(p, new float3(1, 0, 0)); Assert.AreEqual(1f, math.length(f), 1e-3f, AnatPart.Name(p)); }
            }
            finally { a.Dispose(); }
        }
    }
}
