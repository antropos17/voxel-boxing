using UnityEngine;

namespace VoxGame
{
    // Детерминированный ГПСЧ (xorshift32)
    public struct Rng
    {
        uint _s;
        public Rng(uint seed) { _s = seed == 0 ? 1u : seed; }

        public uint Next()
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return _s;
        }

        // [0, 1)
        public float NextFloat() => (Next() & 0x7FFFFFFFu) / (float)0x80000000u;

        // [min, max)
        public float Range(float min, float max) => min + NextFloat() * (max - min);
    }

    // Очередь событий боя без аллокаций в кадре
    public enum FightEventType
    {
        None,
        HitLanded,
        HitBlocked,
        HitMiss,
        Knockdown,
        KO,
        PlayerHurt,
        PlayerDodge,
        MegaReady,
        MegaUsed,
        ComboBreak,
    }

    public struct FightEvent
    {
        public FightEventType type;
        public HitResult      hitResult;  // заполнен при HitLanded/KO/Knockdown
        public float          time;       // игровое время
    }

    public class FightSim
    {
        // Кольцевой буфер событий — без new[] в кадре
        const int EVENT_BUF = 32;
        readonly FightEvent[] _events = new FightEvent[EVENT_BUF];
        int _evHead, _evCount;

        public FightState State  { get; private set; }
        Rng               _rng;
        float             _gameTime;

        // Простой встроенный ИИ-таймер (полноценный — в FightAI.cs, M3)
        float _aiTimer;
        const float AI_PHASE_MIN = 0.9f;
        const float AI_PHASE_MAX = 2.4f;

        public FightSim(int level, uint seed, FightParams? prms = null)
        {
            State = new FightState(level);
            if (prms.HasValue) State.fightParams = prms.Value;
            _rng = new Rng(seed);
            _aiTimer = _rng.Range(AI_PHASE_MIN, AI_PHASE_MAX);
        }

        // Скопировать мега-шкалу из предыдущего боя
        public void CarryMegaMeter(float prevMegaMeter)
        {
            State.megaMeter = Mathf.Clamp(prevMegaMeter, 0f, 100f);
            State.megaReady = State.megaMeter >= 100f;
        }

        // Обработать удар игрока.
        // removed — из AnatView.Hit(); zoneIdx — v.sim.lastZone; traumaAll — v.sim.traumaAll
        public HitResult ProcessHit(int removed, int zoneIdx, float traumaAll,
                                    int detachedBodies, int strength, bool isMega,
                                    float pw, bool counterHit, bool openHit)
        {
            // Игрок промахнулся или соперник в KO/Down — не обрабатываем
            if (removed <= 0 || State.phase == FightPhase.KO)
            {
                if (removed <= 0) State.SpendPunchStamina();   // мимо — выносливость всё равно уходит
                var miss = new HitResult { hit = false };
                _PushEvent(new FightEvent { type = FightEventType.HitMiss, time = _gameTime });
                return miss;
            }

            var zone = (AnatZone)Mathf.Clamp(zoneIdx, 0, (int)AnatZone.Count - 1);

            // Заполнить контекст удара в HitResult перед TakeHit
            // (TakeHit читает counterHit/openHit из result)
            var r = new HitResult
            {
                hit            = true,
                removed        = removed,
                zoneIdx        = zoneIdx,
                traumaAll      = traumaAll,
                detachedBodies = detachedBodies,
                strength       = strength,
                isMega         = isMega,
                pw             = pw,
                counterHit     = counterHit,
                openHit        = openHit,
            };

            // --- Расчёт урона через FightState ---
            // Бонус ×1.5 контр/встречного удара применяется внутри TakeHit → _UpdateMegaOnHit.
            var fromState = State.TakeHit(removed, zone, strength, isMega, pw, counterHit, openHit);
            r.dmgDealt = fromState.dmgDealt;

            // Пороги gore
            r.headBurst = traumaAll > 42f;
            r.fracture  = traumaAll > 14f; // упрощённо: bone → fracture

            // --- Комбо ---
            if (_gameTime - State.lastHitTime < 1.5f)
                State.comboCount++;
            else
                State.comboCount = 1;
            State.lastHitTime = _gameTime;
            State.idleTimer   = 0f;

            // --- Нокдаун / KO ---
            bool ko = State.CheckKO(isMega);
            if (ko)
            {
                r.isKO    = true;
                State.phase = FightPhase.KO;
                _PushEvent(new FightEvent { type = FightEventType.KO, hitResult = r, time = _gameTime });
            }
            else if (State.stunAccum >= State.stunMax && State.knockdowns < 2)
            {
                r.isKnockdown = true;
                State.OnKnockdown();
                _PushEvent(new FightEvent { type = FightEventType.Knockdown, hitResult = r, time = _gameTime });
            }
            else
            {
                _PushEvent(new FightEvent { type = FightEventType.HitLanded, hitResult = r, time = _gameTime });
            }

            // Если мега был использован — сброс шкалы
            if (isMega)
            {
                State.megaMeter = 0f;
                State.megaReady = false;
                _PushEvent(new FightEvent { type = FightEventType.MegaUsed, time = _gameTime });
            }
            else if (State.megaReady)
            {
                // Уведомить представление что мега готов (если ещё не уведомляли)
                // Генерируем событие только при первом переходе — проверяется снаружи
            }

            return r;
        }

        // ИИ-ход — возвращает команду для FoeBody.
        // Полноценный ИИ через FightAI (M3) подключается в FightDirector;
        // здесь минимальный детерминированный вариант для тестов и W1.
        public AICommand TickAI(float sdt)
        {
            _gameTime += sdt;
            State.TickRecovery(sdt);

            _aiTimer -= sdt;
            if (_aiTimer > 0f)
                return new AICommand { newPhase = FightPhase.Guard };

            _aiTimer = _rng.Range(AI_PHASE_MIN, AI_PHASE_MAX);
            bool useHook = _rng.NextFloat() < 0.3f;
            int  side    = (_rng.Next() & 1) == 0 ? -1 : 1;

            if (State.phase == FightPhase.Down || State.phase == FightPhase.KO)
                return new AICommand { newPhase = State.phase };

            return new AICommand
            {
                newPhase = FightPhase.Windup,
                side     = side,
                isHook   = useHook,
            };
        }

        // Восстановление (вызывается FightDirector для не-AI части)
        public void TickRecovery(float dt)
        {
            State.TickRecovery(dt);
        }

        // --- События ---

        // Число событий в очереди
        public int EventCount => _evCount;

        // Прочитать событие (индекс 0 = старейшее). Не удаляет.
        // _evHead указывает на следующую свободную ячейку.
        // Старейшее находится по (_evHead - _evCount + EVENT_BUF) % EVENT_BUF.
        public FightEvent PeekEvent(int i)
        {
            int tail = (_evHead - _evCount + EVENT_BUF) % EVENT_BUF;
            return _events[(tail + i) % EVENT_BUF];
        }

        // Удалить прочитанные события (от хвоста очереди, т.е. старейшие)
        public void ClearEvents(int count)
        {
            _evCount = Mathf.Max(0, _evCount - Mathf.Min(count, _evCount));
        }

        // Забрать все события в массив (не аллоцирует если буфер не переполнен)
        public int DrainEvents(FightEvent[] dest, int maxCount)
        {
            int n = Mathf.Min(_evCount, maxCount);
            for (int i = 0; i < n; i++)
                dest[i] = PeekEvent(i);
            _evCount = Mathf.Max(0, _evCount - n);
            return n;
        }

        void _PushEvent(FightEvent e)
        {
            _events[_evHead] = e;
            _evHead = (_evHead + 1) % EVENT_BUF;
            if (_evCount < EVENT_BUF) _evCount++;
            // иначе вытесняем старейшее (кольцевой буфер)
        }
    }
}
