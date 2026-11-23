using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    /// Мягкие ткани и кровь (этап B, docs/HEAD-SPEC.md, разделы 4 и 7). Каждый тест — на своей свежей голове.
    [Ignore("этап B не закончен")]
    public class AnatomyDamageTests
    {
        AnatHead a; AnatDamage d;
        [SetUp] public void Make() { a = AnatomyBuilder.Build(); d = new AnatDamage(a); }
        [TearDown] public void Free() { d?.Dispose(); a?.Dispose(); d = null; a = null; }

        static readonly float3 Brow = new float3(2.6f, 4.4f, 14), Fore = new float3(1.6f, 6.4f, 14), Back = new float3(0, 0, -1);

        void Run(float s) { for (float t = 0; t < s; t += 1 / 30f) d.Step(1 / 30f); }

        static List<byte> Stack(List<AnatChecks.Run> runs)
        {
            var L = new List<byte>();
            foreach (var r in runs) if (L.Count == 0 || L[L.Count - 1] != r.mat) L.Add(r.mat);
            return L;
        }

        /// Слои открываются по порядку: материал, открытый в центре удара, идёт по исходному порядку слоёв и не перескакивает назад;
        /// серия мощных ударов в одно место доходит до надкостницы или кости.
        [TestCase("лоб")]
        [TestCase("скула")]
        public void LayersOpenInOrder(string zone)
        {
            var ray = AnatChecks.Rays.First(r => r.zone == zone);
            var stack = Stack(AnatChecks.Trace(a, ray.from, ray.dir));
            int last = 0, deepest = 0; var seq = new List<string>();
            for (int k = 0; k < 40; k++)
            {
                d.Hit(ray.from, ray.dir, 3.6f, true); Run(0.5f);
                var now = AnatChecks.Trace(a, ray.from, ray.dir);
                if (now.Count == 0) continue;
                int idx = stack.IndexOf(now[0].mat);
                if (idx < 0) continue;                    // размозжённая ткань и т. п. — не слой исходного стека
                if (seq.Count == 0 || seq[seq.Count - 1] != AnatMat.Table[now[0].mat].name) seq.Add(AnatMat.Table[now[0].mat].name);
                Assert.That(idx, Is.GreaterThanOrEqualTo(last), "слой открылся раньше верхнего: " + string.Join(" → ", seq));
                last = idx; deepest = math.max(deepest, idx);
            }
            int bone = stack.FindIndex(m => m == AnatMat.Periost || m == AnatMat.Compact);
            Assert.That(deepest, Is.GreaterThanOrEqualTo(bone), "не дошли до надкостницы: " + string.Join(" → ", seq));
        }

        int HitsToSplit(float3 p, float3 dir, float E, int max = 30)
        {
            for (int k = 1; k <= max; k++) { var r = d.Hit(p, dir, E, true); Run(0.3f); if (r.split > 0) return k; }
            return max + 1;
        }

        /// Рассечение зависит от расстояния до кости: без учёта кости бровь держит больше ударов; бровь (кость под 6 мм)
        /// рвётся раньше мягкой щеки (кость глубже 2 см).
        [Test]
        public void SplitsWhereBoneIsClose()
        {
            int brow = HitsToSplit(Brow, Back, 2.6f);
            Free(); Make(); d.boneCrush = false;
            int browNoBone = HitsToSplit(Brow, Back, 2.6f);
            Free(); Make();
            int cheek = HitsToSplit(new float3(4.6f, -3.6f, 14), math.normalize(new float3(-0.3f, 0, -1)), 2.6f);
            Debug.Log("ANAT_TEST split: brow " + brow + ", brow without bone " + browNoBone + ", cheek " + cheek);
            Assert.That(brow, Is.LessThan(browNoBone), "кость под бровью должна ускорять рассечение");
            Assert.That(brow, Is.LessThan(cheek), "бровь должна рваться раньше щеки");
        }

        /// Направление разрыва: у брови горизонтально, у рта (губа о зубы) вертикально.
        [Test]
        public void TearDirectionByZone()
        {
            Assert.That(HitsToSplit(Brow, Back, 3.6f), Is.LessThanOrEqualTo(30));
            Assert.That(math.abs(d.lastTearDir.x), Is.GreaterThan(0.85f), "бровь: " + d.lastTearDir);
            Free(); Make();
            Assert.That(HitsToSplit(new float3(0.4f, -4.9f, 14), Back, 3.6f), Is.LessThanOrEqualTo(30));
            Assert.That(d.lastTearZone, Is.EqualTo(AnatZone.Mouth));
            Assert.That(math.abs(d.lastTearDir.y), Is.GreaterThan(math.abs(d.lastTearDir.x)), "рот: " + d.lastTearDir);
        }

        /// Отёк века закрывает глаз: после серии ударов по глазу щель перед шаром заполнена отёчной кожей.
        [Test]
        public void EyelidSwellingClosesEye()
        {
            for (int k = 0; k < 12 && !d.eyeClosed[0]; k++) { d.Hit(new float3(3.6f, 2.4f, 14), math.normalize(new float3(-0.2f, 0, -1)), 3.0f, true); Run(1.5f); }
            Run(5);
            Assert.IsTrue(d.eyeClosed[0], "отёк века " + d.LidSwell(0));
            Assert.IsFalse(d.eyeClosed[1], "правый глаз не били");
            byte first = 0;
            for (float z = 14; z > 4; z -= 0.05f) { int i = a.head.Lookup(new float3(AnatGeo.EX, AnatGeo.EY, z)); if (i >= 0 && AnatMat.Cull(a.head.mat[i])) { first = a.head.mat[i]; break; } }
            Assert.That(first, Is.EqualTo(AnatMat.Skin), "спереди по центру глаза первой должна быть кожа века, а не глаз");
        }

        /// Кровь из раны стекает вниз под действием тяжести.
        [Test]
        public void BloodRunsDown()
        {
            for (int k = 0; k < 30 && d.wounds.Count == 0; k++) { d.Hit(Brow, Back, 3.6f, true); Run(0.3f); }
            Assert.That(d.wounds.Count, Is.GreaterThan(0));
            var w = d.wounds[0]; float wy = a.head.Local(w.x, w.y, w.z).y;
            Run(8);
            double below = 0, above = 0; var h = a.head;
            for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
            {
                var v = d.Get(x, y, z); if (v.blood == 0) continue;
                float dy = h.Local(x, y, z).y - wy;
                if (dy < -1) below += v.blood; else if (dy > 1) above += v.blood;
            }
            Debug.Log("ANAT_TEST blood below " + below + " above " + above);
            Assert.That(below, Is.GreaterThan(above * 3 + 500));
        }

        /// Пределы: раны ≤ 44, подтёки ≤ 70, капли ≤ 160 при любом числе ударов.
        [Test]
        public void LimitsHold()
        {
            var r = new System.Random(3);
            for (int k = 0; k < 220; k++)
            {
                var p = new float3((float)(r.NextDouble() * 12 - 6), (float)(r.NextDouble() * 16 - 9), 14);
                d.Hit(p, math.normalize(new float3((float)(r.NextDouble() - 0.5), (float)(r.NextDouble() - 0.5), -2)), 1 + (float)r.NextDouble() * 3.6f, r.NextDouble() < 0.7);
                for (int s = 0; s < 6; s++) { d.Step(1 / 30f); Assert.That(d.wounds.Count, Is.LessThanOrEqualTo(AnatDamage.MaxWounds)); Assert.That(d.drips.Count, Is.LessThanOrEqualTo(AnatDamage.MaxDrips)); Assert.That(d.drops.Count, Is.LessThanOrEqualTo(AnatDamage.MaxDrops)); }
            }
            Debug.Log("ANAT_TEST limits: peaks wounds " + d.peakWounds + " drips " + d.peakDrips + " drops " + d.peakDrops + ", removed " + d.removed + ", state chunks " + d.st.Chunks);
        }

        /// Синяк меняет цвет по «дням»: красно-розовый → тёмно-фиолетовый → синий → зеленоватый → жёлтый.
        [Test]
        public void BruiseColorStages()
        {
            Color c0 = AnatDamage.BruiseColor(0.3f), c1 = AnatDamage.BruiseColor(2.2f), c2 = AnatDamage.BruiseColor(4.6f), c3 = AnatDamage.BruiseColor(7.6f), c4 = AnatDamage.BruiseColor(11);
            Assert.That(c0.r > c0.g && c0.r > c0.b, "красно-розовый " + c0);
            Assert.That(c1.b > c1.g && c1.r > c1.g && c1.r < 0.5f, "тёмно-фиолетовый " + c1);
            Assert.That(c2.b > c2.r && c2.b > c2.g, "синий " + c2);
            Assert.That(c3.g > c3.r && c3.g > c3.b, "зеленоватый " + c3);
            Assert.That(c4.r > c4.b && c4.g > c4.b && c4.r > 0.85f, "жёлтый " + c4);
        }

        /// 30% общей травмы головы добавляется к каждой зоне.
        [Test]
        public void ZoneTraumaShares30Percent()
        {
            for (int k = 0; k < 4; k++) d.Hit(new float3(0.2f, 1.0f, 14), Back, 2, true);
            Assert.That(d.trauma[AnatZone.Nose], Is.EqualTo(8).Within(1e-3), "удары в нос должны попадать в зону носа");
            Assert.That(d.Eff(AnatZone.Brow), Is.EqualTo(0.3f * d.traumaAll).Within(1e-3));
            Assert.That(d.Eff(AnatZone.Nose), Is.EqualTo(d.traumaAll).Within(1e-3));
        }

        /// Вскрытая поверхностная височная артерия даёт пульсирующую струю капель.
        [Test]
        public void TempleArteryJets()
        {
            int k = 0;
            while (k < 40 && !d.wounds.Any(w => w.kind == AnatDamage.WJet)) { d.Hit(new float3(12, 2.6f, 2.0f), new float3(-1, 0, 0), 3.8f, true); Run(1); k++; }
            Assert.That(d.wounds.Any(w => w.kind == AnatDamage.WJet), "артерия не вскрылась за " + k + " ударов");
            int peak = 0, beats = 0, prev = 0;
            for (float t = 0; t < 3; t += 1 / 60f) { d.Step(1 / 60f); if (d.drops.Count > prev + 2) beats++; prev = d.drops.Count; peak = math.max(peak, d.drops.Count); }
            Debug.Log("ANAT_TEST temple: hits " + k + ", drops peak " + peak + ", spurts " + beats);
            Assert.That(peak, Is.GreaterThan(3));
            Assert.That(beats, Is.InRange(3, 8), "≈1,7 выброса в секунду за 3 с");
        }

        /// Нос: кровь из ноздрей стекает на верхнюю губу.
        [Test]
        public void NoseBleedsOntoLip()
        {
            for (int k = 0; k < 3; k++) { d.Hit(new float3(0.2f, 1.0f, 14), Back, 3.0f, true); Run(0.3f); }
            Assert.That(d.wounds.Any(w => w.kind == AnatDamage.WNose), "нет кровотечения из носа");
            Run(6);
            double lip = 0; var h = a.head;
            for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
            {
                float3 p = h.Local(x, y, z); if (p.y > -3.4f || p.y < -5.6f || math.abs(p.x) > 2.2f) continue;
                lip += d.Get(x, y, z).blood;
            }
            Assert.That(lip, Is.GreaterThan(2000), "кровь на губе");
        }
    }
}
