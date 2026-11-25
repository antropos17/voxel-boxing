using System.Collections.Generic;
using UnityEngine;

namespace VoxGame
{
    public enum RefereeState
    {
        Idle,          // рефери в углу — следит
        StepIn,        // подходит к упавшему
        Counting,      // отсчёт 1..10 (шаг 0.8 с)
        Resume,        // «Бокс!» — соперник встал
        StopFight,     // машет руками — бой остановлен
        WalkToWinner,  // идёт к победителю
        RaiseHand,     // поднимает руку и держит
        Done           // завершено
    }

    public enum RefereeEventType
    {
        CountTick,    // счёт увеличился (RefereeEvent.Count = текущий)
        SayBox,       // «Бокс!» — соперник поднялся
        FightStopped, // бой остановлен (нокаут / счёт 10 / поражение)
        HandRaised    // рука победителя поднята
    }

    public readonly struct RefereeEvent
    {
        public readonly RefereeEventType Type;
        public readonly int Count; // для CountTick: текущий счёт 1..10

        public RefereeEvent(RefereeEventType type, int count = 0)
        {
            Type  = type;
            Count = count;
        }
    }

    // Чистая машина состояний рефери (без MonoBehaviour).
    // Всё время — реальное (udt), без Time.timeScale.
    // RefereeView создаёт этот объект и опрашивает State/Count/Phase/TryDequeueEvent.
    public class RefereeLogic
    {
        // Длительности фаз (секунды реального времени)
        public const float STEP_IN_DUR = 0.60f;
        public const float COUNT_STEP  = 0.80f; // шаг отсчёта
        public const float RESUME_DUR  = 0.50f; // «Бокс!»
        public const float STOP_DUR    = 0.80f; // машет руками
        public const float WALK_DUR    = 0.80f; // идёт к победителю
        public const float RAISE_DUR   = 0.50f; // поднимает руку

        public RefereeState State     { get; private set; }
        public int          Count     { get; private set; }  // 0..10
        public float        Phase     { get; private set; }  // 0..1, нормир. фаза позы
        public bool         PlayerWon { get; private set; }  // победитель текущего боя

        // Выделен один раз, не растёт в бою
        private readonly Queue<RefereeEvent> _events = new Queue<RefereeEvent>(16);

        private float _timer;
        private int   _riseAt;      // счёт подъёма соперника; -1 = не встанет
        private bool  _isKnockout;  // true = нокаут (минуем Counting)

        public RefereeLogic() => Reset();

        // Основной тик. udt — реальное время кадра (Time.unscaledDeltaTime).
        public void Tick(float udt)
        {
            if (udt <= 0f) return;
            _timer += udt;

            switch (State)
            {
                case RefereeState.Idle:
                    Phase = 0f;
                    break;

                case RefereeState.StepIn:
                    Phase = Mathf.Clamp01(_timer / STEP_IN_DUR);
                    if (_timer >= STEP_IN_DUR)
                    {
                        if (_isKnockout) EnterStopFight();
                        else             EnterCounting();
                    }
                    break;

                case RefereeState.Counting:
                    // обрабатываем все полные шаги (на случай крупного dt)
                    // 1e-5f — допуск на накопленную ошибку float при последовательных вычитаниях
                    while (_timer + 1e-5f >= COUNT_STEP)
                    {
                        _timer -= COUNT_STEP;
                        Count++;
                        _events.Enqueue(new RefereeEvent(RefereeEventType.CountTick, Count));

                        if (_riseAt >= 1 && Count >= _riseAt) { EnterResume();    return; }
                        if (Count >= 10)                       { EnterStopFight(); return; }
                    }
                    Phase = Mathf.Clamp01(_timer / COUNT_STEP);
                    break;

                case RefereeState.Resume:
                    Phase = Mathf.Clamp01(_timer / RESUME_DUR);
                    if (_timer >= RESUME_DUR) { State = RefereeState.Idle; _timer = 0f; Phase = 0f; }
                    break;

                case RefereeState.StopFight:
                    Phase = Mathf.Clamp01(_timer / STOP_DUR);
                    if (_timer >= STOP_DUR) EnterWalkToWinner();
                    break;

                case RefereeState.WalkToWinner:
                    Phase = Mathf.Clamp01(_timer / WALK_DUR);
                    if (_timer >= WALK_DUR) EnterRaiseHand();
                    break;

                case RefereeState.RaiseHand:
                    Phase = Mathf.Clamp01(_timer / RAISE_DUR);
                    if (_timer >= RAISE_DUR)
                    {
                        Phase = 1f;
                        State = RefereeState.Done;
                        _events.Enqueue(new RefereeEvent(RefereeEventType.HandRaised));
                    }
                    break;

                case RefereeState.Done:
                    Phase = 1f;
                    break;
            }
        }

        // Счёт подъёма для OnKnockdown: обычно 5–6; на последнем допустимом нокдауне (второй) при здоровье соперника
        // ниже STOP_HP_FRAC он не встаёт — рефери досчитывает до 10 и останавливает бой.
        public const float STOP_HP_FRAC = 0.5f;
        public static int RiseCount(int knockdowns, float hpFrac) =>
            knockdowns >= 2 && hpFrac < STOP_HP_FRAC ? -1 : 4 + knockdowns % 3;

        // Нокдаун: соперник упал. riseAtCount — счёт подъёма (обычно 4..9),
        // или -1 если не встанет (отсчёт до 10).
        public void OnKnockdown(int riseAtCount)
        {
            _riseAt     = riseAtCount;
            _isKnockout = false;
            PlayerWon   = true;  // упал соперник — победа на стороне игрока
            EnterStepIn();
        }

        // Нокаут. playerWon = true — выиграл игрок; false — игрок проиграл.
        public void OnKnockout(bool playerWon)
        {
            _isKnockout = true;
            _riseAt     = -1;
            PlayerWon   = playerWon;
            EnterStepIn();
        }

        // Вернуть в начальное состояние (начало нового боя).
        public void Reset()
        {
            State       = RefereeState.Idle;
            Count       = 0;
            Phase       = 0f;
            _timer      = 0f;
            _riseAt     = -1;
            _isKnockout = false;
            PlayerWon   = true;
            _events.Clear();
        }

        // Получить следующее событие. false если очередь пуста.
        public bool TryDequeueEvent(out RefereeEvent evt)
        {
            if (_events.Count > 0) { evt = _events.Dequeue(); return true; }
            evt = default;
            return false;
        }

        // --- приватные переходы ---

        private void EnterStepIn()      { State = RefereeState.StepIn;       _timer = 0f; Phase = 0f; }
        private void EnterCounting()    { State = RefereeState.Counting;     _timer = 0f; Count = 0; Phase = 0f; }
        private void EnterWalkToWinner(){ State = RefereeState.WalkToWinner; _timer = 0f; Phase = 0f; }
        private void EnterRaiseHand()   { State = RefereeState.RaiseHand;    _timer = 0f; Phase = 0f; }

        private void EnterResume()
        {
            State  = RefereeState.Resume;
            _timer = 0f;
            Phase  = 0f;
            _events.Enqueue(new RefereeEvent(RefereeEventType.SayBox));
        }

        private void EnterStopFight()
        {
            State  = RefereeState.StopFight;
            _timer = 0f;
            Phase  = 0f;
            _events.Enqueue(new RefereeEvent(RefereeEventType.FightStopped));
        }
    }
}
