using NUnit.Framework;

namespace VoxGame.Tests
{
    // EditMode-тесты для FightAI.
    // Зависимости: FightAI, FightStyle, FightPhase, AICommand из VoxGame.
    // FightState — из FightState.cs (пишется L1-fight параллельно).
    [TestFixture]
    public class FightAITests
    {
        // Создаёт нейтральный FightState (level 1 по умолчанию)
        static FightState MakeState(int level = 1) => new FightState(level);

        // ── 1. Детерминизм ──────────────────────────────────────────────────────

        // Два ИИ с одним seed и одинаковыми входами дают одинаковые команды.
        [Test]
        public void Determinism_SameSeedSameCommands()
        {
            var sty      = FightAI.Styles[0]; // Кирпич: fe=0, co=0, c=0 — чистый ИИ
            const uint seed = 12345u;

            var ai1 = new FightAI(sty, 3, seed);
            var ai2 = new FightAI(sty, 3, seed);

            var state1 = MakeState(3);
            var state2 = MakeState(3);

            const float dt = 1f / 60f;
            for (int i = 0; i < 240; i++) // 4 секунды симуляции
            {
                var cmd1 = ai1.Tick(dt, state1);
                var cmd2 = ai2.Tick(dt, state2);

                // Оба ИИ следуют своим командам
                state1.phase = cmd1.newPhase;
                state2.phase = cmd2.newPhase;

                Assert.AreEqual(cmd1.newPhase, cmd2.newPhase,  $"phase: tick {i}");
                Assert.AreEqual(cmd1.side,     cmd2.side,      $"side: tick {i}");
                Assert.AreEqual(cmd1.isHook,   cmd2.isHook,    $"isHook: tick {i}");
                Assert.AreEqual(cmd1.isFeint,  cmd2.isFeint,   $"isFeint: tick {i}");
            }
        }

        // ── 2. Финт меняет руку ─────────────────────────────────────────────────

        // При финте настоящий замах идёт с руки, противоположной ложному замаху.
        [Test]
        public void Feint_ChangesAttackSide()
        {
            var sty = FightAI.Styles[4]; // Утюг: fe=0.45 — высокий шанс финта

            bool confirmed = false;
            for (uint seed = 1u; seed <= 2000u && !confirmed; seed++)
            {
                var ai    = new FightAI(sty, 1, seed);
                var state = MakeState(1);

                const float dt = 1f / 60f;
                int  feintSide = 0;
                bool inFeint   = false;

                for (int t = 0; t < 360; t++) // до 6 с симуляции
                {
                    var cmd = ai.Tick(dt, state);
                    state.phase = cmd.newPhase;

                    if (!inFeint && cmd.isFeint && cmd.newPhase == FightPhase.Windup)
                    {
                        // Начало ложного замаха
                        inFeint   = true;
                        feintSide = cmd.side;
                    }
                    else if (inFeint && !cmd.isFeint && cmd.newPhase == FightPhase.Windup)
                    {
                        // Настоящий замах после финта — рука должна смениться
                        Assert.AreNotEqual(feintSide, cmd.side,
                            $"seed={seed} tick={t}: настоящий замах должен быть с другой руки");
                        confirmed = true;
                        break;
                    }
                    else if (inFeint && cmd.newPhase != FightPhase.Windup)
                    {
                        // Финт прерван внешне — сброс, пробуем следующий seed
                        inFeint = false;
                        break;
                    }
                }
            }

            Assert.IsTrue(confirmed, "Не нашли финт за 2000 seeds: логика финта не работает");
        }

        // ── 3. Длительность замаха уменьшается с уровнем ───────────────────────

        // Соперник уровня 10 должен атаковать быстрее соперника уровня 1.
        [Test]
        public void WindupDuration_DecreasesWithLevel()
        {
            var sty = FightAI.Styles[0]; // Кирпич: fe=0, c=0 — нет финтов и серий
            const uint seed = 1u;

            float MeasureFirstWindup(int level)
            {
                var ai    = new FightAI(sty, level, seed);
                var state = MakeState(level);

                const float dt     = 1f / 600f; // 1.67 мс — высокое разрешение
                float windTime     = 0f;
                bool  entered      = false;

                for (int t = 0; t < 36000; t++) // до 60 с симуляции
                {
                    var cmd = ai.Tick(dt, state);
                    state.phase = cmd.newPhase;

                    if (!entered && cmd.newPhase == FightPhase.Windup)
                        entered = true;

                    if (entered)
                    {
                        windTime += dt;
                        if (cmd.newPhase != FightPhase.Windup)
                            break;
                    }
                }
                return windTime;
            }

            float t1  = MeasureFirstWindup(1);
            float t10 = MeasureFirstWindup(10);

            Assert.Greater(t1,  0f, "Уровень 1: замах должен иметь ненулевую длительность");
            Assert.Greater(t10, 0f, "Уровень 10: замах должен иметь ненулевую длительность");
            Assert.Less(t10, t1,
                $"Уровень 10 ({t10:F3} с) должен замахиваться короче, чем уровень 1 ({t1:F3} с)");
        }

        // ── 4. Окно Open завершается через ~0.95 с ─────────────────────────────

        // Когда соперник промахнулся (состояние Open 0.95 с), ИИ должен его покинуть.
        [Test]
        public void OpenWindow_ExitsAfterDuration()
        {
            var ai    = new FightAI(FightAI.Styles[0], 1, 1u);
            var state = MakeState(1);

            // Принудительно переводим в Open (внешний переход от FightSim)
            state.phase = FightPhase.Open;

            const float dt = 1f / 60f;

            // Первый тик — синхронизация; ИИ должен принять Open
            var cmd = ai.Tick(dt, state);
            Assert.AreEqual(FightPhase.Open, cmd.newPhase,
                "Сразу после Open: ИИ должен вернуть Open");

            // Прогоняем 1.5 с (> 0.95 с — гарантированный выход)
            for (int i = 0; i < 90; i++)
            {
                cmd = ai.Tick(dt, state);
                state.phase = cmd.newPhase;
            }

            Assert.AreNotEqual(FightPhase.Open, state.phase,
                "За 1.5 с ИИ обязан покинуть Open (длительность окна 0.95 с)");
        }
    }
}
