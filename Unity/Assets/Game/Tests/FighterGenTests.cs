using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VoxGame;

namespace VoxGame.Tests
{
    [TestFixture]
    public class FighterGenTests
    {
        // ---- Детерминизм -------------------------------------------------

        [Test]
        public void Generate_SameSeed_BitIdentical()
        {
            var a = FighterGen.Generate(12345u, 3);
            var b = FighterGen.Generate(12345u, 3);

            Assert.AreEqual(a.seed,       b.seed,       "seed");
            Assert.AreEqual(a.name,       b.name,       "name");
            Assert.AreEqual(a.nickname,   b.nickname,   "nickname");
            Assert.AreEqual(a.styleIdx,   b.styleIdx,   "styleIdx");
            Assert.AreEqual(a.headStyle,  b.headStyle,  "headStyle");
            Assert.AreEqual(a.build.scale, b.build.scale, "scale");
            Assert.AreEqual(a.colors.trunks, b.colors.trunks, "trunks");
            Assert.AreEqual(a.colors.skin,   b.colors.skin,   "skin");
            Assert.AreEqual(a.stats.health,  b.stats.health,  "health");
        }

        [Test]
        public void Roster_SameSeed_SameRoster()
        {
            var r1 = new RosterDef(999u);
            var r2 = new RosterDef(999u);
            for (int i = 0; i < 12; i++)
            {
                Assert.AreEqual(r1.Get(i).name,     r2.Get(i).name,     $"name[{i}]");
                Assert.AreEqual(r1.Get(i).styleIdx, r2.Get(i).styleIdx, $"styleIdx[{i}]");
            }
        }

        // ---- Различимость соседей в лестнице ----------------------------

        [Test]
        public void Roster_AdjacentFighters_NeverTripleMatch()
        {
            var roster = new RosterDef(999u);
            for (int i = 0; i < 11; i++)
            {
                var a = roster.Get(i);
                var b = roster.Get(i + 1);
                bool tripleMatch = a.headStyle == b.headStyle
                    && a.colors.trunks == b.colors.trunks
                    && a.styleIdx == b.styleIdx;
                Assert.IsFalse(tripleMatch,
                    $"Соседи {i} и {i + 1} неразличимы: headStyle={a.headStyle}, style={a.styleIdx}");
            }
        }

        // ---- Рост характеристик по уровню --------------------------------

        [Test]
        public void Stats_GrowFromLevel1ToLevel12()
        {
            // Те же атрибуты внешности, разный уровень → разные характеристики
            var low  = FighterGen.Generate(555u, 1);
            var high = FighterGen.Generate(555u, 12);

            Assert.Less(low.stats.health,     high.stats.health,
                "HP не растёт с ур.1 до ур.12");
            Assert.Less(low.stats.damage,     high.stats.damage,
                "Урон не растёт с ур.1 до ур.12");
            Assert.Less(low.stats.resilience, high.stats.resilience,
                "Стойкость не растёт с ур.1 до ур.12");
        }

        [Test]
        public void Roster_HpMaxGrowsAlongLadder()
        {
            // HP уровня i+1 должен быть ≥ HP уровня i
            var roster = new RosterDef(42u);
            for (int i = 0; i < 11; i++)
            {
                var a = roster.Get(i);
                var b = roster.Get(i + 1);
                Assert.LessOrEqual(a.stats.health, b.stats.health,
                    $"stats.health не растёт между ур.{a.level} и ур.{b.level}");
            }
        }

        // ---- Уникальность имён в лестнице --------------------------------

        [Test]
        public void Roster_AllNamesUnique()
        {
            var roster = new RosterDef(777u);
            var seen   = new HashSet<string>();
            for (int i = 0; i < 12; i++)
            {
                string name = roster.Get(i).name;
                Assert.IsTrue(seen.Add(name),
                    $"Имя «{name}» повторяется (позиция {i})");
            }
        }

        // ---- Структура лестницы ------------------------------------------

        [Test]
        public void Roster_Has12FightersWithCorrectLevels()
        {
            var roster = new RosterDef(100u);
            Assert.AreEqual(12, roster.Fighters.Length);
            for (int i = 0; i < 12; i++)
                Assert.AreEqual(i + 1, roster.Get(i).level, $"level[{i}]");
        }

        [Test]
        public void Roster_Next_WrapsAround()
        {
            var roster = new RosterDef(1u);
            var last   = roster.Next(11);  // после 12-го → снова первый
            Assert.AreEqual(roster.Get(0).seed, last.seed);
        }

        // ---- Быстрый бой -------------------------------------------------

        [Test]
        public void GenerateRandom_IsDeterministic()
        {
            var a = FighterGen.GenerateRandom(888u, 5);
            var b = FighterGen.GenerateRandom(888u, 5);
            Assert.AreEqual(a.name,     b.name);
            Assert.AreEqual(a.styleIdx, b.styleIdx);
        }

        [Test]
        public void GenerateQuickFight_IsDeterministic()
        {
            var a = RosterDef.GenerateQuickFight(321u, 6);
            var b = RosterDef.GenerateQuickFight(321u, 6);
            Assert.AreEqual(a.name,  b.name);
            Assert.AreEqual(a.level, b.level);
        }

        // ---- Контракт полей ----------------------------------------------

        [Test]
        public void Generate_ScaleInValidRange()
        {
            for (uint s = 0u; s < 50u; s++)
            {
                var f = FighterGen.Generate(s * 1000u + 1u, 1);
                Assert.GreaterOrEqual(f.scale, 0.90f, $"seed={s}: scale ниже 0.90");
                Assert.LessOrEqual   (f.scale, 1.10f, $"seed={s}: scale выше 1.10");
            }
        }

        // ---- Внешность: детерминизм ----------------------------------------

        [Test]
        public void Generate_SameSeed_SameLook()
        {
            for (uint s = 1u; s < 40u; s++)
            {
                var a = FighterGen.Generate(s * 7919u, 4);
                var b = FighterGen.Generate(s * 7919u, 4);
                Assert.AreEqual(a.build,    b.build,    $"build, seed {s}");
                Assert.AreEqual(a.head,     b.head,     $"head, seed {s}");
                Assert.AreEqual(a.colors,   b.colors,   $"colors, seed {s}");
                Assert.AreEqual(a.mark,     b.mark,     $"mark, seed {s}");
                Assert.AreEqual(a.markSide, b.markSide, $"markSide, seed {s}");
                Assert.AreEqual(a.cardText, b.cardText, $"cardText, seed {s}");
            }
        }

        [Test]
        public void Roster_SameSeed_SameLook()
        {
            var r1 = new RosterDef(2024u);
            var r2 = new RosterDef(2024u);
            for (int i = 0; i < RosterDef.Count; i++)
            {
                Assert.AreEqual(r1.Get(i).build,    r2.Get(i).build,    $"build[{i}]");
                Assert.AreEqual(r1.Get(i).head,     r2.Get(i).head,     $"head[{i}]");
                Assert.AreEqual(r1.Get(i).colors,   r2.Get(i).colors,   $"colors[{i}]");
                Assert.AreEqual(r1.Get(i).mark,     r2.Get(i).mark,     $"mark[{i}]");
                Assert.AreEqual(r1.Get(i).nickname, r2.Get(i).nickname, $"nickname[{i}]");
            }
        }

        // ---- Лестница: попарная различимость 12 бойцов -----------------------

        static readonly uint[] RosterSeeds = { 0u, 1u, 7u, 42u, 100u, 777u, 999u, 2024u, 31337u, 0xDEADBEEFu, 123456789u, 4000000000u };

        [Test]
        public void Roster_TrunksPalettesUnique()
        {
            foreach (uint seed in RosterSeeds)
            {
                var r = new RosterDef(seed);
                var seen = new HashSet<int>();
                for (int i = 0; i < RosterDef.Count; i++)
                    Assert.IsTrue(seen.Add(r.Get(i).colors.trunksPalette),
                        $"seed {seed}: палитра трусов {r.Get(i).colors.trunksPalette} повторяется (позиция {i})");
            }
        }

        [Test]
        public void Roster_HeadLooksUnique()
        {
            foreach (uint seed in RosterSeeds)
            {
                var r = new RosterDef(seed);
                var seen = new HashSet<int>();
                for (int i = 0; i < RosterDef.Count; i++)
                {
                    var h = r.Get(i).head;
                    Assert.IsTrue(seen.Add(FighterGen.HeadKey(h)),
                        $"seed {seed}: верх головы {FighterGen.TopOf(h)} + борода {h.beard} повторяется (позиция {i})");
                }
            }
        }

        [Test]
        public void Roster_NamesNicknamesAndIdentityUnique()
        {
            foreach (uint seed in RosterSeeds)
            {
                var r = new RosterDef(seed);
                var names = new HashSet<string>();
                var nicks = new HashSet<string>();
                var ids   = new HashSet<string>();
                for (int i = 0; i < RosterDef.Count; i++)
                {
                    var f = r.Get(i);
                    Assert.IsFalse(string.IsNullOrEmpty(f.name),     $"seed {seed}: пустое имя [{i}]");
                    Assert.IsFalse(string.IsNullOrEmpty(f.nickname), $"seed {seed}: пустое прозвище [{i}]");
                    Assert.IsTrue(names.Add(f.name),     $"seed {seed}: имя «{f.name}» повторяется");
                    Assert.IsTrue(nicks.Add(f.nickname), $"seed {seed}: прозвище «{f.nickname}» повторяется");
                    Assert.IsTrue(ids.Add(f.name + "|" + f.nickname + "|" + f.styleIdx),
                        $"seed {seed}: имя+прозвище+стиль повторяются [{i}]");
                }
            }
        }

        [Test]
        public void Roster_BodyTypesVaried()
        {
            foreach (uint seed in RosterSeeds)
            {
                var r = new RosterDef(seed);
                var types = new HashSet<BodyType>();
                for (int i = 0; i < RosterDef.Count; i++)
                {
                    types.Add(r.Get(i).build.type);
                    if (i > 0)
                    {
                        Assert.AreNotEqual(r.Get(i - 1).build.type, r.Get(i).build.type,
                            $"seed {seed}: соседи {i - 1} и {i} одного телосложения");
                        Assert.AreNotEqual(r.Get(i - 1).colors.glovePalette, r.Get(i).colors.glovePalette,
                            $"seed {seed}: соседи {i - 1} и {i} в одинаковых перчатках");
                    }
                }
                Assert.AreEqual(FighterGen.BodyTypeCount, types.Count, $"seed {seed}: в лестнице не все телосложения");
            }
        }

        // ---- Внешность: диапазоны и сочетания -------------------------------

        [Test]
        public void Generate_BuildMultipliersInRange()
        {
            for (uint s = 0u; s < 200u; s++)
            {
                var b = FighterGen.Generate(s * 2654435761u + 9u, 1).build;
                Assert.That(b.shoulderMult, Is.InRange(0.85f, 1.15f), $"seed {s}: shoulderMult");
                Assert.That(b.neckMult,     Is.InRange(0.80f, 1.20f), $"seed {s}: neckMult");
                Assert.That(b.chestMult,    Is.InRange(0.85f, 1.20f), $"seed {s}: chestMult");
                Assert.That(b.armMult,      Is.InRange(0.85f, 1.25f), $"seed {s}: armMult");
                Assert.That(b.waistMult,    Is.InRange(0.85f, 1.30f), $"seed {s}: waistMult");
                Assert.That(b.torsoLen,     Is.InRange(0.92f, 1.10f), $"seed {s}: torsoLen");
                Assert.That(b.mass,         Is.InRange(60f, 110f),    $"seed {s}: mass");
            }
        }

        [Test]
        public void Generate_BodyTypesDiffer()
        {
            // тяжеловес шире и толще сухого, длинный выше коренастого
            var p = FighterGen.LookPicks.Free;
            p.body = (int)BodyType.Lean;    var lean   = FighterGen.Generate(5u, 1, p).build;
            p.body = (int)BodyType.Heavy;   var heavy  = FighterGen.Generate(5u, 1, p).build;
            p.body = (int)BodyType.Stocky;  var stocky = FighterGen.Generate(5u, 1, p).build;
            p.body = (int)BodyType.Long;    var tall   = FighterGen.Generate(5u, 1, p).build;
            Assert.Greater(heavy.shoulderMult, lean.shoulderMult);
            Assert.Greater(heavy.waistMult,    lean.waistMult + 0.2f);
            Assert.Greater(heavy.armMult,      lean.armMult + 0.2f);
            Assert.Greater(heavy.neckMult,     lean.neckMult + 0.2f);
            Assert.Greater(tall.torsoLen,      stocky.torsoLen + 0.1f);
            Assert.Greater(tall.scale,         stocky.scale);
        }

        [Test]
        public void Generate_HeadLookConsistent()
        {
            var counts = new int[FighterGen.TopCount];
            for (uint s = 0u; s < 400u; s++)
            {
                var f = FighterGen.Generate(s * 40503u + 17u, 3);
                var h = f.head;
                counts[FighterGen.TopOf(h)]++;
                if (h.wear == HeadWear.Helmet || h.wear == HeadWear.Beanie)
                    Assert.IsTrue(h.hair == HairStyle.Bald || h.hair == HairStyle.Buzz, $"seed {s}: под убором {h.hair}");
                if (h.wear != HeadWear.None)
                    Assert.AreNotEqual(HairStyle.Mohawk, h.hair, $"seed {s}: ирокез под убором");
                if (h.wear == HeadWear.Helmet)
                    Assert.IsFalse(h.earring, $"seed {s}: серьга под шлемом");
                if (h.pompom)
                    Assert.AreEqual(HeadWear.Beanie, h.wear, $"seed {s}: помпон без шапочки");
                Assert.IsTrue(h.side == 1 || h.side == -1, $"seed {s}: side");
                Assert.IsTrue(f.markSide == 1 || f.markSide == -1, $"seed {s}: markSide");
                Assert.AreEqual((Color)FighterGen.HeadSkin, f.colors.skin, $"seed {s}: тон кожи тела не как у головы");
            }
            for (int t = 0; t < counts.Length; t++)
                Assert.Greater(counts[t], 0, $"верх головы {t} не выпадает ни разу");
        }

        [Test]
        public void Generate_MarksVaried()
        {
            var seen = new HashSet<BodyMark>();
            for (uint s = 0u; s < 200u; s++) seen.Add(FighterGen.Generate(s * 977u + 5u, 1).mark);
            Assert.GreaterOrEqual(seen.Count, 4, "нательных отметин меньше четырёх вариантов");
        }

        [Test]
        public void Generate_StyleIdxInRange()
        {
            for (uint s = 0u; s < 50u; s++)
            {
                var f = FighterGen.Generate(s * 777u + 3u, 1);
                Assert.GreaterOrEqual(f.styleIdx, 0);
                Assert.Less          (f.styleIdx, 8);
            }
        }
    }
}
