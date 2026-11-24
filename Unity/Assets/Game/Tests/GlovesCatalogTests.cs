using NUnit.Framework;
using System.Collections.Generic;
using VoxGame;

namespace VoxGame.Tests {

    [TestFixture]
    public class GlovesCatalogTests {

        [Test]
        public void AllCount_AtLeast6() {
            Assert.GreaterOrEqual(GlovesCatalog.All.Length, 6);
        }

        [Test]
        public void AllIds_AreUnique() {
            var seen = new HashSet<string>();
            foreach (var g in GlovesCatalog.All) {
                Assert.IsFalse(seen.Contains(g.id), $"Дублирующийся id: {g.id}");
                seen.Add(g.id);
            }
        }

        [Test]
        public void ClassicGlove_IsFree() {
            var classic = GlovesCatalog.Get("classic");
            Assert.AreEqual("classic", classic.id);
            Assert.AreEqual(0, classic.price);
        }

        [Test]
        public void GetById_ReturnsCorrectEntry() {
            var heavy = GlovesCatalog.Get("heavy");
            Assert.AreEqual("heavy", heavy.id);
            Assert.Greater(heavy.dmgMult, 1f); // тяжёлые бьют сильнее
        }

        [Test]
        public void GetUnknownId_FallsBackToClassic() {
            var fallback = GlovesCatalog.Get("несуществующие");
            Assert.AreEqual("classic", fallback.id);
        }

        [Test]
        public void GetNull_FallsBackToClassic() {
            var fallback = GlovesCatalog.Get(null);
            Assert.AreEqual("classic", fallback.id);
        }

        /// <summary>Любые две перчатки различаются хотя бы по одному числовому свойству.</summary>
        [Test]
        public void PairwiseDifferentStats() {
            var all = GlovesCatalog.All;
            for (int i = 0; i < all.Length; i++) {
                for (int j = i + 1; j < all.Length; j++) {
                    var a = all[i];
                    var b = all[j];
                    bool differs =
                        a.dmgMult          != b.dmgMult          ||
                        a.speedMult        != b.speedMult        ||
                        a.staminaCostMult  != b.staminaCostMult  ||
                        a.megaGainMult     != b.megaGainMult     ||
                        a.incomingDmgMult  != b.incomingDmgMult  ||
                        a.price            != b.price;
                    Assert.IsTrue(differs,
                        $"Перчатки '{a.id}' и '{b.id}' имеют одинаковые характеристики");
                }
            }
        }

        [Test]
        public void AllDisplayNames_NotEmpty() {
            foreach (var g in GlovesCatalog.All)
                Assert.IsFalse(string.IsNullOrEmpty(g.displayName), $"id={g.id}: пустое displayName");
        }

        [Test]
        public void AllPrices_NonNegative() {
            foreach (var g in GlovesCatalog.All)
                Assert.GreaterOrEqual(g.price, 0, $"id={g.id}: отрицательная цена");
        }
    }
}
