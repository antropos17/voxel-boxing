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
