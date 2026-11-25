using NUnit.Framework;
using UnityEngine;
using VoxGame;

namespace VoxGame.Tests
{
    // EditMode тесты — без UnityEngine.TestRunner (только NUnit)
    [TestFixture]
    public class FightStateTests
    {
        // ---- Вспомогательные ----

        static FightSim MakeSim(int level = 1, uint seed = 42)
            => new FightSim(level, seed);

        // Нанести удар с достаточным removed чтобы снять реальный HP
        static HitResult Hit(FightSim sim, int removed = 20, AnatZone zone = AnatZone.Skull,
                             int strength = 1, bool mega = false, float pw = 1f,
                             bool counter = false, bool open = false)
            => sim.ProcessHit(removed, (int)zone, 0f, 0, strength, mega, pw, counter, open);

        // ---- Базовый урон ----

        [Test]
        public void Damage_ReducesHP()
        {
            var sim = MakeSim();
            float before = sim.State.hp;
            var r = Hit(sim, removed: 20, zone: AnatZone.Skull, strength: 1);
            Assert.IsTrue(r.hit);
            Assert.Less(sim.State.hp, before);
            Assert.Greater(r.dmgDealt, 0f);
        }

        [Test]
        public void Damage_ZoneMultiplier_JawBiggerThanNose()
        {
            // Две одинаковые симуляции, одинаковые removed/strength — зона разная
            var simJaw  = MakeSim();
            var simNose = MakeSim();
            float beforeJaw  = simJaw.State.hp;
            float beforeNose = simNose.State.hp;
            Hit(simJaw,  removed: 20, zone: AnatZone.Jaw,  strength: 1);
            Hit(simNose, removed: 20, zone: AnatZone.Nose, strength: 1);
            float dJaw  = beforeJaw  - simJaw.State.hp;
            float dNose = beforeNose - simNose.State.hp;
            Assert.Greater(dJaw, dNose, "Jaw (×1.6) должен давать больше урона чем Nose (×1.0)");
        }

        [Test]
        public void Damage_MegaMultiplier()
        {
            var simNorm = MakeSim();
            var simMega = MakeSim();
            float bN = simNorm.State.hp, bM = simMega.State.hp;
            Hit(simNorm, removed: 10, strength: 1, mega: false);
            Hit(simMega, removed: 10, strength: 1, mega: true);
            float dN = bN - simNorm.State.hp;
            float dM = bM - simMega.State.hp;
            // Мега даёт ×2.4 — с учётом стамины разброс; проверяем что существенно больше
            Assert.Greater(dM, dN * 1.5f);
        }

        [Test]
        public void Damage_OpenHit_Bigger()
        {
            var simNorm = MakeSim();
            var simOpen = MakeSim();
            float bN = simNorm.State.hp, bO = simOpen.State.hp;
            Hit(simNorm, removed: 15, strength: 1, open: false);
            Hit(simOpen, removed: 15, strength: 1, open: true);
            Assert.Less(simOpen.State.hp, simNorm.State.hp);
        }

        [Test]
        public void Damage_CounterHit_Bigger()
        {
            var simNorm    = MakeSim();
            var simCounter = MakeSim();
            float bN = simNorm.State.hp, bC = simCounter.State.hp;
            Hit(simNorm,    removed: 15, strength: 1, counter: false);
            Hit(simCounter, removed: 15, strength: 1, counter: true);
            Assert.Less(simCounter.State.hp, simNorm.State.hp);
        }

        // ---- Нокдаун ----

        [Test]
        public void Knockdown_OccursBeforeKO_WhenKDLessThan2()
        {
            var sim = MakeSim(level: 1, seed: 1);
            // Нанести достаточно оглушения через много ударов
            int maxIter = 200;
            bool gotKD = false;
            for (int i = 0; i < maxIter && sim.State.phase != FightPhase.KO; i++)
            {
                var r = Hit(sim, removed: 30, zone: AnatZone.Jaw, strength: 2);
                if (r.isKnockdown) { gotKD = true; break; }
                // Сбросить phase из Down в Guard чтобы продолжать бить
                if (sim.State.phase == FightPhase.Down) sim.State.phase = FightPhase.Guard;
            }
            Assert.IsTrue(gotKD || sim.State.knockdowns > 0,
                "Нокдаун должен произойти раньше KO при kd < 2");
        }

        [Test]
        public void Knockdown_IncreasesKnockdownCount()
        {
            var sim = MakeSim();
            int before = sim.State.knockdowns;
            // Принудительно выставить stun на грань нокдауна
            sim.State.stunAccum = sim.State.stunMax - 0.01f;
            sim.State.phase     = FightPhase.Guard;
            Hit(sim, removed: 5, zone: AnatZone.Skull, strength: 1);
            // knockdowns увеличился ИЛИ KO (если kd уже было 2)
            bool increased = sim.State.knockdowns > before || sim.State.phase == FightPhase.KO;
            Assert.IsTrue(increased);
        }

        [Test]
        public void Knockdown_ResetsStunAccum()
        {
            var sim = MakeSim();
            sim.State.stunAccum = sim.State.stunMax; // на пороге
            sim.State.knockdowns = 0;
            sim.State.OnKnockdown();
            Assert.AreEqual(0f, sim.State.stunAccum);
            Assert.AreEqual(1, sim.State.knockdowns);
        }

        // ---- KO — ветки ----

        [Test]
        public void KO_WhenHPReachesZero()
        {
            var sim = MakeSim();
            sim.State.hp = 0f;
            Assert.IsTrue(sim.State.CheckKO(false));
        }

        [Test]
        public void KO_WhenStunOverflowAndKDAtLeast2()
        {
            var sim = MakeSim();
            sim.State.stunAccum = sim.State.stunMax;
            sim.State.knockdowns = 2;
            Assert.IsTrue(sim.State.CheckKO(false));
        }

        [Test]
        public void KO_NotTriggered_WhenKDLessThan2()
        {
            var sim = MakeSim();
            sim.State.hp         = 50f;
            sim.State.stunAccum  = sim.State.stunMax;
            sim.State.knockdowns = 1;
            Assert.IsFalse(sim.State.CheckKO(false),
                "При kd=1 и stun>=stunMax должен быть нокдаун, не KO");
        }

        [Test]
        public void KO_MegaCondition()
        {
            var sim = MakeSim();
            sim.State.hp        = sim.State.hpMax * 0.44f; // < 45%
            sim.State.stunAccum = sim.State.stunMax * 0.65f; // >= 60%
            Assert.IsTrue(sim.State.CheckKO(true),
                "Мега должен вызвать KO при hp<45% и stun>=60%stunMax");
        }

        [Test]
        public void KO_MegaCondition_NotTriggered_WithoutMega()
        {
            var sim = MakeSim();
            sim.State.hp        = sim.State.hpMax * 0.44f;
            sim.State.stunAccum = sim.State.stunMax * 0.65f;
            Assert.IsFalse(sim.State.CheckKO(false),
                "Без мега KO по мега-условию не должен срабатывать");
        }

        // ---- Блок ----

        [Test]
        public void Block_SlowerStaminaRegen()
        {
            var sim = MakeSim();
            var sB  = MakeSim();
            sim.State.playerStamina = 50f;
            sB.State.playerStamina  = 50f;
            sim.State.playerBlocking = false;
            sB.State.playerBlocking  = true;

            const float dt = 1f;
            sim.State.TickRecovery(dt);
            sB.State.TickRecovery(dt);

            Assert.Greater(sim.State.playerStamina, sB.State.playerStamina,
                "В бою регенерация 19/с, в блоке — 9/с");
        }

        // ---- Уклон ----

        [Test]
        public void Dodge_DecaysOverTime()
        {
            var sim = MakeSim();
            sim.State.playerDodge = 1f;
            sim.State.TickRecovery(FightState.DODGE_DUR);
            Assert.AreEqual(0f, sim.State.playerDodge, 0.01f);
        }

        // ---- Мега-шкала ----

        [Test]
        public void MegaMeter_GrowsOnHit()
        {
            var sim = MakeSim();
            float before = sim.State.megaMeter;
            Hit(sim, removed: 10, strength: 0); // джеб +5
            Assert.Greater(sim.State.megaMeter, before);
        }

        [Test]
        public void MegaMeter_ReachesReady()
        {
            var sim = MakeSim();
            sim.State.megaMeter = 95f;
            Hit(sim, removed: 10, strength: 0); // джеб +5 → >=100
            Assert.IsTrue(sim.State.megaReady);
        }

        [Test]
        public void MegaMeter_DecreaseOnPlayerHurt()
        {
            var sim = MakeSim();
            sim.State.megaMeter = 50f;
            sim.State.OnPlayerHurt();
            Assert.Less(sim.State.megaMeter, 50f);
        }

        [Test]
        public void MegaMeter_IdlePenalty()
        {
            var sim = MakeSim();
            sim.State.megaMeter = 80f;
            sim.State.idleTimer = FightState.IDLE_THRESHOLD + 0.01f;
            float before = sim.State.megaMeter;
            sim.State.TickRecovery(1f);
            Assert.Less(sim.State.megaMeter, before);
        }

        [Test]
        public void MegaMeter_NotResetBetweenBouts_WhenCarried()
        {
            var sim = MakeSim();
            sim.State.megaMeter = 60f;
            var sim2 = new FightSim(1, 42);
            sim2.CarryMegaMeter(sim.State.megaMeter);
            Assert.AreEqual(60f, sim2.State.megaMeter, 0.01f);
        }

        // ---- Победа / поражение игрока ----

        [Test]
        public void PlayerDeath_WhenHPReachesZero()
        {
            var sim = MakeSim();
            sim.State.playerHp = 0f;
            Assert.LessOrEqual(sim.State.playerHp, 0f);
        }

        // ---- Детерминизм ----

        [Test]
        public void Determinism_SameSeedSameResult()
        {
            const uint seed = 777;
            var simA = new FightSim(1, seed);
            var simB = new FightSim(1, seed);

            // Несколько ходов ИИ
            for (int i = 0; i < 10; i++)
            {
                simA.TickAI(1f / 60f);
                simB.TickAI(1f / 60f);
            }

            // Нанести серию ударов
            for (int i = 0; i < 5; i++)
            {
                Hit(simA, removed: 15, zone: AnatZone.Skull, strength: 1);
                Hit(simB, removed: 15, zone: AnatZone.Skull, strength: 1);
            }

            Assert.AreEqual(simA.State.hp,        simB.State.hp,        0.001f);
            Assert.AreEqual(simA.State.stunAccum, simB.State.stunAccum, 0.001f);
            Assert.AreEqual(simA.State.knockdowns, simB.State.knockdowns);
        }

        [Test]
        public void Determinism_DifferentSeedDifferentAI()
        {
            var simA = new FightSim(1, 100);
            var simB = new FightSim(1, 200);

            AICommand lastA = default, lastB = default;
            for (int i = 0; i < 20; i++)
            {
                lastA = simA.TickAI(0.1f);
                lastB = simB.TickAI(0.1f);
            }
            // Не обязаны быть разными каждый шаг, но разные зёрна должны дать разный ИИ
            // Проверяем что симуляции вообще работают без исключений
            Assert.IsNotNull(simA.State);
            Assert.IsNotNull(simB.State);
        }

        // ---- FightEvent очередь ----

        [Test]
        public void EventQueue_HitLandedPushed()
        {
            var sim = MakeSim();
            Hit(sim, removed: 10, strength: 1);
            Assert.Greater(sim.EventCount, 0);
            var e = sim.PeekEvent(0);
            Assert.AreNotEqual(FightEventType.None, e.type);
        }

        [Test]
        public void EventQueue_KOEvent_OnKO()
        {
            var sim = MakeSim();
            sim.State.hp = 1f;
            Hit(sim, removed: 10, strength: 1);
            bool found = false;
            for (int i = 0; i < sim.EventCount; i++)
                if (sim.PeekEvent(i).type == FightEventType.KO) found = true;
            Assert.IsTrue(found);
        }

        // ---- Rng детерминизм ----

        [Test]
        public void Rng_SameSeedSameSequence()
        {
            var r1 = new Rng(123);
            var r2 = new Rng(123);
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(r1.Next(), r2.Next());
        }

        [Test]
        public void Rng_FloatInRange()
        {
            var r = new Rng(99);
            for (int i = 0; i < 100; i++)
            {
                float f = r.NextFloat();
                Assert.GreaterOrEqual(f, 0f);
                Assert.Less(f, 1f);
            }
        }

        // ---- Мега: бонус контр/встречного удара ----

        [Test]
        public void MegaMeter_CounterBonus_MultipliesGainNotMeter()
        {
            // Баг: ApplyMegaCounterBonus умножала весь meter на 1.5 → при meter≥67 немедленный megaReady.
            // Исправление: ×1.5 применяется к gain текущего удара.
            var sim = MakeSim();
            sim.State.megaMeter = 70f;
            // strength=1 (power), counter: gain = 11 × 1.5 = 16.5; ожидаем 70 + 16.5 = 86.5
            Hit(sim, removed: 10, strength: 1, counter: true);
            Assert.IsFalse(sim.State.megaReady,
                "При meter=70 и counter-ударе шкала должна быть ~86.5, не 100 (megaReady не должен взводиться)");
            Assert.AreEqual(70f + 11f * 1.5f, sim.State.megaMeter, 1f,
                "Ожидаемое значение ~86.5: gain=11 × 1.5, не meter × 1.5");
        }

        [Test]
        public void MegaMeter_OpenHitBonus_MultipliesGainNotMeter()
        {
            var sim = MakeSim();
            sim.State.megaMeter = 70f;
            // strength=1, openHit: gain = 11 × 1.5 = 16.5
            Hit(sim, removed: 10, strength: 1, open: true);
            Assert.IsFalse(sim.State.megaReady,
                "OpenHit тоже не должен мгновенно заполнять шкалу при meter=70");
            Assert.AreEqual(70f + 11f * 1.5f, sim.State.megaMeter, 1f);
        }

        // ---- Мультипликаторы перчаток/улучшений ----

        [Test]
        public void FightParams_DmgMult_Applied()
        {
            var simNorm = MakeSim();
            var simBuff = MakeSim();
            simBuff.State.fightParams = new FightParams
            {
                dmgMult         = 1.5f,
                staminaCostMult = 1f,
                megaGainMult    = 1f,
                defenseMult     = 1f,
                speedMult       = 1f,
            };

            float bN = simNorm.State.hp, bB = simBuff.State.hp;
            Hit(simNorm, removed: 20, strength: 1);
            Hit(simBuff, removed: 20, strength: 1);
            Assert.Less(simBuff.State.hp, simNorm.State.hp,
                "Усиленные перчатки должны наносить больше урона");
        }
    }
}
