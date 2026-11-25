// FightFlowTests.cs — сквозные тесты боя на чистой логике без представления.
// FightSim + FightAI + синтетический ввод — без MonoBehaviour и Play Mode.
using NUnit.Framework;
using UnityEngine;

namespace VoxGame.Tests
{
    [TestFixture]
    public class FightFlowTests
    {
        const float DT = 1f / 60f;

        // ------------------------------------------------------------------ Вспомогательный симулятор

        struct FightOutcome
        {
            public bool playerWon;   // true = KO соперника
            public bool playerLost;  // true = HP игрока <= 0
            public int  ticks;
        }

        /// Прогоняет бой до победы, поражения или maxTicks.
        /// Игрок бьёт каждые punchEvery тиков (0 = не бьёт).
        /// ИИ наносит урон игроку при переходе Windup → Strike.
        static FightOutcome RunFight(
            FightSim sim, FightAI ai, FighterDef foe,
            int maxTicks, int punchEvery, int removedPerPunch)
        {
            // Базовый урон одного удара ИИ
            float baseDmg = (9f + 1.6f * (foe.level - 1)) * FightAI.Styles[foe.styleIdx].d;

            var prevPhase = sim.State.phase;

            for (int t = 0; t < maxTicks; t++)
            {
                var cmd = ai.Tick(DT, sim.State);

                bool wasWindup = prevPhase   == FightPhase.Windup;
                bool nowStrike = cmd.newPhase == FightPhase.Strike;

                // ИИ перешёл в Strike из Windup → засчитываем удар по игроку
                if (wasWindup && nowStrike && sim.State.phase != FightPhase.Down)
                    sim.State.playerHp = Mathf.Max(0f, sim.State.playerHp - baseDmg);

                sim.State.phase = cmd.newPhase;
                prevPhase       = cmd.newPhase;
                sim.TickRecovery(DT);

                if (sim.State.playerHp <= 0f)
                    return new FightOutcome { playerLost = true, ticks = t };
                if (sim.State.phase == FightPhase.KO)
                    return new FightOutcome { playerWon = true, ticks = t };

                // --- Удар игрока ---
                if (punchEvery > 0 && t % punchEvery == 0)
                {
                    var r = sim.ProcessHit(
                        removedPerPunch, 2, removedPerPunch * 0.5f,
                        0, 1, false, 1.1f, false, false);

                    if (r.isKO)
                        return new FightOutcome { playerWon = true, ticks = t };

                    if (r.isKnockdown)
                    {
                        // Соперник сбит: sim.State.phase уже = Down (установлено ProcessHit).
                        // Прокручиваем ~4 с счёта (AI остаётся в Down).
                        int countTicks = Mathf.RoundToInt(4f / DT);
                        for (int k = 0; k < countTicks; k++)
                        {
                            var c2 = ai.Tick(DT, sim.State);
                            // AI вернёт Down, пока внешний код не поставит Rise
                            sim.State.phase = c2.newPhase;
                            sim.TickRecovery(DT);
                        }
                        // Соперник встаёт
                        sim.State.phase = FightPhase.Rise;
                        prevPhase       = FightPhase.Rise;
                    }
                }
            }

            return new FightOutcome { ticks = maxTicks };
        }

        // ------------------------------------------------------------------ Тесты

        [Test]
        public void PlayerWins_WithStrongProfile()
        {
            // dmgMult=25 → большой урон → нокаут за несколько секунд
            var prms = new FightParams
            {
                dmgMult         = 25f,
                staminaCostMult = 1f,
                megaGainMult    = 1f,
                defenseMult     = 1f,
                speedMult       = 1f,
            };
            var def = FighterGen.Generate(42u, 1);
            var sim = new FightSim(def.level, def.seed, prms);
            var ai  = new FightAI(FightAI.Styles[def.styleIdx], def.level, def.seed);

            var outcome = RunFight(sim, ai, def,
                maxTicks: 600, punchEvery: 2, removedPerPunch: 20);

            Assert.IsTrue(outcome.playerWon,
                "Игрок с dmgMult=25 должен победить нокаутом");
            Assert.AreEqual(FightPhase.KO, sim.State.phase,
                "Фаза соперника должна быть KO");
            Assert.LessOrEqual(sim.State.hp, 0f,
                "HP соперника должно упасть до 0");
        }

        [Test]
        public void PlayerLoses_WhenNotPunching()
        {
            // ИИ атакует, игрок не бьёт → HP игрока падает до 0
            var def = FighterGen.Generate(42u, 1);
            var sim = new FightSim(def.level, def.seed, FightParams.Default);
            var ai  = new FightAI(FightAI.Styles[def.styleIdx], def.level, def.seed);

            // punchEvery=0 → игрок не бьёт
            var outcome = RunFight(sim, ai, def,
                maxTicks: 4800, punchEvery: 0, removedPerPunch: 0);

            Assert.IsTrue(outcome.playerLost,
                "Пассивный игрок должен проиграть (HP → 0)");
            Assert.LessOrEqual(sim.State.playerHp, 0f,
                "HP игрока должно упасть до нуля");
        }

        [Test]
        public void Determinism_SameSeed_SameOutcome()
        {
            uint seed = 777u;
            var def  = FighterGen.Generate(seed, 2);
            var prms = new FightParams
            {
                dmgMult         = 5f,
                staminaCostMult = 1f,
                megaGainMult    = 1f,
                defenseMult     = 1f,
                speedMult       = 1f,
            };

            var out1 = RunFight(
                new FightSim(def.level, seed, prms),
                new FightAI(FightAI.Styles[def.styleIdx], def.level, seed),
                def, 2400, 5, 12);

            var out2 = RunFight(
                new FightSim(def.level, seed, prms),
                new FightAI(FightAI.Styles[def.styleIdx], def.level, seed),
                def, 2400, 5, 12);

            Assert.AreEqual(out1.playerWon,  out2.playerWon,
                "Победитель детерминирован при одном seed");
            Assert.AreEqual(out1.playerLost, out2.playerLost,
                "Поражение детерминировано при одном seed");
            Assert.AreEqual(out1.ticks,      out2.ticks,
                "Число тиков до исхода детерминировано");
        }

        [Test]
        public void Fight_TerminatesInFiniteTime()
        {
            // Нормальные параметры, обычные удары — бой должен завершиться за 10 минут симуляции
            var def = FighterGen.Generate(1u, 1);
            var prms = new FightParams
            {
                dmgMult         = 3f,
                staminaCostMult = 1f,
                megaGainMult    = 1f,
                defenseMult     = 1f,
                speedMult       = 1f,
            };
            var sim = new FightSim(def.level, 1u, prms);
            var ai  = new FightAI(FightAI.Styles[def.styleIdx], def.level, 1u);

            var outcome = RunFight(sim, ai, def,
                maxTicks: 36000, punchEvery: 4, removedPerPunch: 10);

            Assert.IsTrue(outcome.playerWon || outcome.playerLost,
                "Бой должен завершиться за 36 000 тиков (10 мин × 60 FPS)");
        }
    }
}
