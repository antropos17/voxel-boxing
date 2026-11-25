using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
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

        [Test]
        public void AllDescriptions_NotEmpty() {
            foreach (var g in GlovesCatalog.All)
                Assert.IsFalse(string.IsNullOrEmpty(g.description), $"id={g.id}: пустое описание");
        }

        /// <summary>У каждой записи свой вид: форма и основной цвет не повторяются, shapeTag совпадает с формой.</summary>
        [Test]
        public void EveryEntry_HasDistinctLook() {
            var all = GlovesCatalog.All;
            for (int i = 0; i < all.Length; i++) {
                Assert.IsTrue(GloveLooks.Has(all[i].id), $"id={all[i].id}: нет вида в GloveLooks");
                var a = GloveLook.FromEntry(all[i]);
                Assert.AreEqual(all[i].shapeTag, (int)a.shape, $"id={all[i].id}: shapeTag не совпадает с формой");
                for (int j = i + 1; j < all.Length; j++) {
                    var b = GloveLook.FromEntry(all[j]);
                    Assert.AreNotEqual(a.shape, b.shape, $"'{a.id}' и '{b.id}' одной формы");
                    Assert.AreNotEqual(a.main, b.main, $"'{a.id}' и '{b.id}' одного цвета");
                }
            }
        }

        /// <summary>Сетки всех перчаток и предплечий строятся, не пустые и укладываются в 16-битные индексы.</summary>
        [Test]
        public void AllLooks_BuildMeshes() {
            foreach (var g in GlovesCatalog.All) {
                var look = GloveLook.FromEntry(g);
                var meshes = new[] {
                    GloveLooks.BuildGloveMesh(look, +1), GloveLooks.BuildGloveMesh(look, -1), GloveLooks.BuildForeArmMesh(look),
                };
                foreach (var m in meshes) {
                    Assert.Greater(m.vertexCount, 0, $"id={g.id}: пустая сетка {m.name}");
                    Assert.Less(m.vertexCount, 65000, $"id={g.id}: слишком большая сетка {m.name}");
                    Object.DestroyImmediate(m);
                }
            }
        }

        [Test]
        public void Showcase_SpawnsAndDestroys() {
            var parent = new GameObject("ShowcaseParent");
            var t = GloveLooks.SpawnShowcase("champion", parent.transform, null);
            Assert.IsNotNull(t);
            Assert.AreEqual(parent.transform, t.parent);
            GloveLooks.DestroyShowcase(t);
            Assert.AreEqual(0, parent.transform.childCount);
            Object.DestroyImmediate(parent);
        }
    }
}
