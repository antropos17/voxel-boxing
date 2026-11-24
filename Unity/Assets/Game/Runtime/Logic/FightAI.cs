using System;
using UnityEngine;

namespace VoxGame
{
    // Параметры стиля соперника. 8 предустановленных стилей — FightAI.Styles[0..7].
    public struct FightStyle
    {
        public string name;
        public float w;   // множитель времени замаха
        public float p;   // множитель паузы между атаками
        public float d;   // множитель урона (используется FightSim)
        public float c;   // шанс начать серию
        public int   n;   // доп. ударов в серии
        public float fe;  // шанс финта
        public float co;  // шанс контры
        public float hk;  // шанс хука (иначе прямой)
        public float g;   // бонус к вероятности guard
        public float hn;  // сила подсказки уклона (1=чёткая, 0=нет), используется HUD
        public float bs;  // стоимость блока (выносливость), используется FightSim
        public float bc;  // стоимость блока (доля HP), используется FightSim
    }

    // Команда ИИ: описывает состояние соперника в каждом кадре.
    // FightDirector передаёт команду FoeBody (для анимации IK).
    // FightSim применяет newPhase → state.phase.
    public struct AICommand
    {
        public FightPhase newPhase;  // текущая фаза соперника
        public int        side;       // -1 = лево, +1 = право
        public bool       isHook;     // true = хук, false = прямой
        public bool       isFeint;    // идёт ложный замах (первая половина финта)
        public bool       isCounter;  // контратака
    }

    // Детерминированный ИИ соперника.
    // Управляет машиной состояний (Idle→Guard→Windup→Strike→Recover→...)
    // и возвращает AICommand каждый кадр через Tick().
    // FightSim обязан:
    //   1) применить cmd.newPhase → state.phase до следующего вызова Tick;
    //   2) вызвать TriggerCounter() когда удар игрока промахнулся или пробил блок.
    public class FightAI
    {
        // 8 стилей (индекс соответствует STY[0..7] из web-fight.md §6)
        public static readonly FightStyle[] Styles = new FightStyle[8]
        {
            // 0  Кирпич — медленный одиночка, без хитростей, чёткий телеграф
            new FightStyle { name="Кирпич",    w=1.25f, p=1.25f, d=1.00f, c=0f,   n=0, fe=0f,   co=0f,   hk=.15f, g=0f, hn=1.00f, bs=18f, bc=.20f },
            // 1  Шкаф — очень медленный, очень больно, чуть скрытнее
            new FightStyle { name="Шкаф",      w=1.45f, p=1.35f, d=1.55f, c=0f,   n=0, fe=0f,   co=0f,   hk=.25f, g=0f, hn=.85f,  bs=34f, bc=.30f },
            // 2  Молот — серийник, почти без пауз, без хитростей
            new FightStyle { name="Молот",     w=1.00f, p=1.00f, d=0.80f, c=.75f, n=2, fe=0f,   co=0f,   hk=.30f, g=0f, hn=.70f,  bs=14f, bc=.20f },
            // 3  Бетон — контратакующий, иногда серия
            new FightStyle { name="Бетон",     w=1.10f, p=1.10f, d=1.05f, c=.15f, n=1, fe=0f,   co=.75f, hk=.30f, g=0f, hn=.55f,  bs=18f, bc=.20f },
            // 4  Утюг — финтует, иногда серия
            new FightStyle { name="Утюг",      w=1.00f, p=1.00f, d=1.00f, c=.20f, n=1, fe=.45f, co=0f,   hk=.25f, g=0f, hn=.40f,  bs=18f, bc=.20f },
            // 5  Гранит — хуки, финты, контры, нечёткий телеграф
            new FightStyle { name="Гранит",    w=1.15f, p=1.00f, d=1.20f, c=.25f, n=1, fe=.10f, co=.25f, hk=.65f, g=0f, hn=.25f,  bs=26f, bc=.25f },
            // 6  Бульдозер — агрессивный серийник, слабый телеграф
            new FightStyle { name="Бульдозер", w=0.90f, p=0.50f, d=0.95f, c=.55f, n=2, fe=.10f, co=.35f, hk=.35f, g=0f, hn=.10f,  bs=18f, bc=.20f },
            // 7  Тихий — финты, контры, серии, телеграфа нет
            new FightStyle { name="Тихий",     w=1.00f, p=0.85f, d=1.10f, c=.45f, n=2, fe=.30f, co=.50f, hk=.40f, g=0f, hn=0f,    bs=22f, bc=.22f },
        };

        // ── Константы ──────────────────────────────────────────────────────────

        const float STRIKE_DUR    = 0.13f;   // момент проверки удара от начала Strike
        const float OPEN_DUR      = 0.95f;   // окно Open после промаха соперника
        const float RISE_DUR      = 1.90f;   // время подъёма после нокдауна
        const float RISE_IDLE_ADD = 1.10f;   // доп. пауза сразу после Rise
        const float COUNTER_CD    = 1.20f;   // кулдаун повторной контры (с)
        const float GROGGY_WIND   = 1.30f;   // множитель замаха при groggy
        const float GROGGY_PACE   = 1.40f;   // множитель паузы при groggy

        // ── Конфигурация (задаётся один раз) ───────────────────────────────────

        readonly FightStyle _sty;
        readonly int        _level;

        // Базовые длительности (уровень × стиль), без groggy
        readonly float _windBase; // время замаха
        readonly float _paceBase; // пауза между атаками
        readonly float _guardP;   // вероятность перехода в Guard

        // ── ГПСЧ (xorshift32) ──────────────────────────────────────────────────

        uint _rng;

        // ── Внутренняя машина состояний ────────────────────────────────────────

        FightPhase _phase = FightPhase.Idle;
        float      _timer;

        int  _side;      // текущая атакующая рука
        bool _isHook;    // тип удара

        // Финт: _feintFirst=true во время первой (ложной) половины замаха
        bool _feintFirst;

        // Серия: кол-во оставшихся ударов после текущего
        int _seriesLeft;

        // Контра: ожидает возможности
        bool  _counterPending;
        float _counterCd; // кулдаун (секунды)

        // Флаги текущей команды (сбрасываются при смене состояния)
        bool _cmdFeint;
        bool _cmdCounter;

        // ── Конструктор ────────────────────────────────────────────────────────

        // seed=0 → детерминированное зерно на основе уровня
        public FightAI(FightStyle style, int level, uint seed = 0)
        {
            _sty   = style;
            _level = level;
            _rng   = seed != 0 ? seed : (uint)(level * 2654435761u + 1u);
            if (_rng == 0) _rng = 1u;

            _windBase = Mathf.Max(0.3f,  0.6f  - 0.045f * (level - 1)) * style.w;
            _paceBase = Mathf.Max(0.45f, 1.0f  - 0.09f  * (level - 1)) * style.p;
            _guardP   = Mathf.Clamp(0.22f + 0.05f * (level - 1) + style.g, 0f, 0.6f);

            // Начальное состояние — Idle
            _side  = 1;
            _timer = _paceBase;
        }

        // ── Публичный интерфейс ────────────────────────────────────────────────

        // Вызывается FightSim.ProcessHit когда удар игрока промахнулся
        // или пробил блок соперника. Точка расширения — FightSim обязан это вызывать.
        public void TriggerCounter()
        {
            _counterPending = true;
        }

        // Основной тик. sdt — масштабированное игровое время кадра.
        // FightSim обязан применить cmd.newPhase → state.phase до следующего вызова.
        public AICommand Tick(float sdt, FightState state)
        {
            if (state.phase == FightPhase.KO)
                return new AICommand { newPhase = FightPhase.KO, side = _side };

            // Принять внешний переход фазы (нокдаун, пошатывание, подъём, Open)
            if (state.phase != _phase)
                SyncExternal(state.phase);

            _timer -= sdt;
            if (_counterCd > 0f) _counterCd -= sdt;

            // Переход по истечении таймера
            if (_timer <= 0f)
                HandleExpiry(state);

            // Контратака в паузе (Idle/Guard/Recover + нет кулдауна + шанс co)
            if (_counterPending && CanCounter())
            {
                _counterPending = false;
                _counterCd = COUNTER_CD;
                EnterWindup(isCounter: true, isSeries: false, groggy: state.groggy > 0f);
            }

            return new AICommand
            {
                newPhase  = _phase,
                side      = _side,
                isHook    = _isHook,
                isFeint   = _cmdFeint,
                isCounter = _cmdCounter,
            };
        }

        // ── Переходы ───────────────────────────────────────────────────────────

        void HandleExpiry(FightState state)
        {
            bool groggy = state.groggy > 0f;

            switch (_phase)
            {
                case FightPhase.Idle:
                    if (RngFloat() < _guardP)
                        EnterGuard();
                    else
                        EnterWindup(isCounter: false, isSeries: false, groggy);
                    break;

                case FightPhase.Guard:
                    EnterWindup(isCounter: false, isSeries: false, groggy);
                    break;

                case FightPhase.Windup:
                    if (_feintFirst)
                    {
                        // Ложный замах завершён — начинаем настоящий с другой руки
                        _feintFirst = false;
                        _cmdFeint   = false;
                        // Если игрок клюнул (начал уклон) — укороченный настоящий замах
                        bool baited = state.playerDodge > 0.04f;
                        _side  = -_side; // ключевое: рука меняется
                        _timer = WindTime(groggy) * (baited ? 0.38f : 0.55f);
                        // Фаза остаётся Windup — FightSim не видит изменений
                    }
                    else
                    {
                        EnterStrike();
                    }
                    break;

                case FightPhase.Strike:
                    EnterRecover();
                    break;

                case FightPhase.Recover:
                    if (_seriesLeft > 0)
                    {
                        _seriesLeft--;
                        EnterWindup(isCounter: false, isSeries: true, groggy);
                    }
                    else
                    {
                        EnterIdle(groggy);
                    }
                    break;

                case FightPhase.Open:
                case FightPhase.Stagger:
                    EnterIdle(groggy);
                    break;

                case FightPhase.Rise:
                    // После вставания — дополнительная пауза (groggy уже активен)
                    EnterIdle(groggy);
                    _timer += RISE_IDLE_ADD;
                    break;

                case FightPhase.Down:
                    // FightSim переведёт в Rise по окончании счёта — ждём
                    _timer = float.MaxValue;
                    break;
            }
        }

        void EnterIdle(bool groggy = false)
        {
            _phase      = FightPhase.Idle;
            _timer      = _paceBase * (groggy ? GROGGY_PACE : 1f);
            _cmdFeint   = false;
            _cmdCounter = false;
        }

        void EnterGuard()
        {
            _phase      = FightPhase.Guard;
            _timer      = 0.40f + RngFloat() * 0.50f; // 0.40–0.90 с
            _cmdFeint   = false;
            _cmdCounter = false;
        }

        void EnterWindup(bool isCounter, bool isSeries, bool groggy)
        {
            _cmdFeint   = false;
            _cmdCounter = isCounter;

            // Случайный выбор руки и типа удара
            _side   = (NextRng() & 1u) == 0u ? -1 : 1;
            _isHook = RngFloat() < _sty.hk;

            float wind = WindTime(groggy);
            if (isCounter) wind *= 0.6f;

            if (!isSeries)
            {
                // Финт?
                if (RngFloat() < _sty.fe)
                {
                    _feintFirst = true;
                    _cmdFeint   = true;
                    wind       *= 0.5f; // ложный замах короче
                }
                else
                {
                    _feintFirst = false;
                    // Начать серию? (только если ещё не в серии)
                    if (_seriesLeft == 0 && RngFloat() < _sty.c)
                        _seriesLeft = _sty.n;
                }
            }
            else
            {
                // Серийный удар — финт не делается, серия не пересчитывается
                _feintFirst = false;
            }

            _phase = FightPhase.Windup;
            _timer = wind;
        }

        void EnterStrike()
        {
            _phase = FightPhase.Strike;
            _timer = STRIKE_DUR;
        }

        void EnterRecover()
        {
            _phase = FightPhase.Recover;
            _timer = 0.24f + RngFloat() * 0.12f; // 0.24–0.36 с
        }

        // Принять внешний переход (нокдаун, пошатывание, подъём, Open и пр.)
        void SyncExternal(FightPhase external)
        {
            _phase      = external;
            _feintFirst = false;
            _seriesLeft = 0;
            _cmdFeint   = false;
            _cmdCounter = false;

            switch (external)
            {
                case FightPhase.Stagger: _timer = 0.40f + RngFloat() * 0.10f; break;
                case FightPhase.Down:    _timer = float.MaxValue;              break;
                case FightPhase.Rise:    _timer = RISE_DUR;                    break;
                case FightPhase.Open:    _timer = OPEN_DUR;                    break;
                default:                 _timer = 0f;                          break; // немедленный переход
            }
        }

        bool CanCounter()
        {
            return (_phase == FightPhase.Idle || _phase == FightPhase.Guard || _phase == FightPhase.Recover)
                && _counterCd <= 0f
                && RngFloat() < _sty.co;
        }

        float WindTime(bool groggy) => _windBase * (groggy ? GROGGY_WIND : 1f);

        // ── ГПСЧ xorshift32 ────────────────────────────────────────────────────

        uint NextRng()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            if (_rng == 0) _rng = 1u; // ноль убивает xorshift
            return _rng;
        }

        // Возвращает float в [0, 1)
        float RngFloat() => (NextRng() & 0x7FFFFFFFu) / (float)0x80000000u;
    }
}
