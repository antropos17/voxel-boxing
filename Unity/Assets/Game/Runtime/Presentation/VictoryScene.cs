using System;
using UnityEngine;

namespace VoxGame
{
    public enum VictoryState
    {
        Idle,    // развязки нет
        Stop,    // рефери подходит и останавливает бой
        Walk,    // идёт к победителю
        Raise,   // поднимает руку победителя
        Cheer,   // рука поднята, зал ликует
        Belt,    // чемпионский пояс (только титульный бой и победа игрока)
        Ready    // можно показывать экран результата
    }

    // Постановщик развязки боя: по нокауту ведёт последовательность на реальном времени и раздаёт сигналы.
    // НЕ MonoBehaviour и ничего не знает о UI. Фазы рефери (Stop/Walk/Raise) идут вровень с RefereeLogic,
    // если она передана: фаза кончается, когда логика рефери дошла до следующего состояния (с запасом SyncSlack
    // на случай, если логика встала). Без логики фазы идут по своим длительностям.
    public class VictoryScene
    {
        // ---- Длительности, с ------------------------------------------------------------------
        public const float StopDur   = RefereeLogic.STEP_IN_DUR + RefereeLogic.STOP_DUR;
        public const float WalkDur   = RefereeLogic.WALK_DUR;
        public const float RaiseDur  = RefereeLogic.RAISE_DUR;
        public const float CheerDur  = 1.5f;   // держит руку под рёв зала
        public const float BeltDur   = 2.8f;   // пояс в кадре до экрана результата
        public const float SyncSlack = 1.5f;   // наибольшее ожидание логики рефери сверх длительности фазы

        public VictoryState State    { get; private set; }
        public float        Progress { get; private set; }   // 0..1 внутри текущей фазы
        public float        Elapsed  { get; private set; }   // с начала развязки
        public bool         PlayerWon { get; private set; }
        public bool         ShowsBelt { get; private set; }
        public bool IsDone  => State == VictoryState.Ready;
        public bool Running => State != VictoryState.Idle && State != VictoryState.Ready;

        // Сигналы (по одному разу за развязку)
        public Action OnFightStopped;   // рефери остановил бой
        public Action OnWalk;           // пошёл к победителю
        public Action OnHandRaised;     // рука победителя поднята
        public Action<CrowdMood> SetCrowdMood;   // настроение зала: подключить CrowdView.SetMood
        public Action OnBelt;          // пояс вынесен в кадр
        public Action OnReady;          // готово к экрану результата

        readonly RefereeLogic _ref;
        readonly BeltView _belt;
        float _t;

        public VictoryScene(RefereeLogic referee = null, BeltView belt = null)
        {
            _ref = referee;
            _belt = belt;
        }

        // Титульный бой: последний соперник лестницы.
        public static bool IsTitleFight(int ladderIndex, int ladderCount) =>
            ladderCount > 0 && ladderIndex >= ladderCount - 1;

        // Начать развязку (в момент нокаута или остановки боя). showBelt — бой титульный или пояс нужен по флагу;
        // пояс выносится только при победе игрока.
        public void Begin(bool playerWon, bool showBelt)
        {
            if (Running) return;
            PlayerWon = playerWon;
            ShowsBelt = showBelt && playerWon;
            Elapsed = 0f;
            Enter(VictoryState.Stop);
            OnFightStopped?.Invoke();
        }

        // Вернуть в исходное (переигровка). Пояс уходит из кадра.
        public void Reset()
        {
            State = VictoryState.Idle;
            Progress = 0f;
            Elapsed = 0f;
            _t = 0f;
            _belt?.Hide();
        }

        public void Tick(float udt)
        {
            if (!Running) return;
            udt = Mathf.Max(0f, udt);
            _t += udt;
            Elapsed += udt;

            // при запоздалом Begin логика рефери может быть уже впереди — догоняем за один кадр
            for (int guard = 0; guard < 4 && Running; guard++)
            {
                VictoryState was = State;
                switch (State)
                {
                    case VictoryState.Stop:
                        Progress = Mathf.Clamp01(_t / StopDur);
                        if (PhaseOver(StopDur, RefereeState.WalkToWinner)) { Enter(VictoryState.Walk); OnWalk?.Invoke(); }
                        break;
                    case VictoryState.Walk:
                        Progress = Mathf.Clamp01(_t / WalkDur);
                        if (PhaseOver(WalkDur, RefereeState.RaiseHand)) Enter(VictoryState.Raise);
                        break;
                    case VictoryState.Raise:
                        Progress = Mathf.Clamp01(_t / RaiseDur);
                        if (PhaseOver(RaiseDur, RefereeState.Done))
                        {
                            Enter(VictoryState.Cheer);
                            OnHandRaised?.Invoke();
                            SetCrowdMood?.Invoke(CrowdMood.Cheer);
                        }
                        break;
                    case VictoryState.Cheer:
                        Progress = Mathf.Clamp01(_t / CheerDur);
                        if (_t >= CheerDur)
                        {
                            if (ShowsBelt)
                            {
                                Enter(VictoryState.Belt);
                                _belt?.Show();
                                OnBelt?.Invoke();
                            }
                            else Finish();
                        }
                        break;
                    case VictoryState.Belt:
                        Progress = Mathf.Clamp01(_t / BeltDur);
                        if (_t >= BeltDur) Finish();
                        break;
                }
                if (State == was) break;
            }
        }

        // Фаза рефери кончилась: логика дошла до next (или ждём не дольше SyncSlack), без логики — по времени.
        bool PhaseOver(float dur, RefereeState next)
        {
            if (_ref == null) return _t >= dur;
            return (int)_ref.State >= (int)next || _t >= dur + SyncSlack;
        }

        void Enter(VictoryState s)
        {
            State = s;
            _t = 0f;
            Progress = 0f;
        }

        void Finish()
        {
            Enter(VictoryState.Ready);
            Progress = 1f;
            OnReady?.Invoke();
        }
    }
}
