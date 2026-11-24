using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace VoxGame
{
    // Читает касания/мышь → очередь PunchInput.
    // Inject() добавляет синтетические жесты для тестов.
    // Не реагирует на касания над кнопками UI.
    // IsBlocking = true когда палец удерживается в нижней зоне экрана.
    public class InputReader : MonoBehaviour
    {
        // ---- Очередь --------------------------------------------------------

        const int Q_MAX = 16;
        readonly PunchInput[] _queue = new PunchInput[Q_MAX];
        int _qHead, _qCount;

        // Добавить синтетический жест (тесты / GameWalk).
        public void Inject(PunchInput p)
        {
            if (_qCount >= Q_MAX) return;
            _queue[(_qHead + _qCount) % Q_MAX] = p;
            _qCount++;
        }

        // Получить следующий PunchInput. false если очередь пуста.
        public bool Dequeue(out PunchInput p)
        {
            if (_qCount == 0) { p = default; return false; }
            p = _queue[_qHead];
            _qHead = (_qHead + 1) % Q_MAX;
            _qCount--;
            return true;
        }

        // Удержание нижней части экрана — блок.
        public bool IsBlocking { get; private set; }

        // ---- Трекинг касаний ------------------------------------------------

        const int TRACK_MAX = 5;
        struct TouchState
        {
            public int     fingerId;
            public Vector2 start;
            public float   startTime;
            public bool    isBlock;    // удержание нижней зоны
            public bool    isActive;
        }
        readonly TouchState[] _touches = new TouchState[TRACK_MAX];
        readonly List<RaycastResult> _raycastBuf = new List<RaycastResult>(8);
        PointerEventData _pd;

        // ---- Включение/выключение -------------------------------------------

        void OnEnable()
        {
            if (!EnhancedTouchSupport.enabled)
                EnhancedTouchSupport.Enable();
        }

        // ---- Update ---------------------------------------------------------

        void Update()
        {
            ProcessTouches();
            ProcessMouseFallback();
            RefreshBlockState();
        }

        void ProcessTouches()
        {
            var touches = ETouch.activeTouches;
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                switch (t.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        OnTouchBegan(t.touchId, t.screenPosition);
                        break;
                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        OnTouchEnded(t.touchId, t.screenPosition, (float)(t.time - t.startTime));
                        break;
                }
            }
        }

        void ProcessMouseFallback()
        {
            // Мышь только в редакторе и при отсутствии касаний.
            if (ETouch.activeTouches.Count > 0) return;
            var mouse = Mouse.current;
            if (mouse == null) return;
            const int MOUSE_ID = -1;
            if (mouse.leftButton.wasPressedThisFrame)
                OnTouchBegan(MOUSE_ID, mouse.position.ReadValue());
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                float dur = FindElapsed(MOUSE_ID);
                OnTouchEnded(MOUSE_ID, mouse.position.ReadValue(), dur);
            }
        }

        // ---- Обработка касания ----------------------------------------------

        void OnTouchBegan(int id, Vector2 pos)
        {
            if (IsOverUI(pos)) return;
            int slot = FindFreeSlot();
            if (slot < 0) return;
            _touches[slot] = new TouchState
            {
                fingerId  = id,
                start     = pos,
                startTime = Time.unscaledTime,
                isBlock   = IsBottomZone(pos),
                isActive  = true,
            };
        }

        void OnTouchEnded(int id, Vector2 endPos, float duration)
        {
            int slot = FindSlot(id);
            if (slot < 0) return;
            var st = _touches[slot];
            _touches[slot] = default;
            if (st.isBlock) return; // удержание нижней зоны — не удар
            ClassifyGesture(st.start, endPos, duration);
        }

        void ClassifyGesture(Vector2 start, Vector2 end, float duration)
        {
            Vector2 delta = end - start;
            float dist  = delta.magnitude;
            float swipe = GameUtil.SwipeThreshold(Screen.width, Screen.height);
            // speed = dist / max(16, dur_ms) px/ms
            float speed = dist / Mathf.Max(16f, duration * 1000f);
            float pw    = PunchInput.CalcPw(speed);

            PunchInput p;
            if (dist < swipe)
            {
                // Тап → джеб; сторона от середины экрана
                p = new PunchInput
                {
                    type    = PunchType.Jab,
                    side    = start.x < Screen.width * 0.5f ? -1 : 1,
                    pw      = 1f,
                    screenStart = start,
                    screenEnd   = end,
                };
            }
            else
            {
                float ax = Mathf.Abs(delta.x), ay = Mathf.Abs(delta.y);
                PunchType type;
                int side;
                if (ax >= ay)
                {
                    // Горизонтальный → хук; dx>0 → side=-1 (лев. скула соперника)
                    type = PunchType.Hook;
                    side = delta.x > 0 ? -1 : 1;
                }
                else if (delta.y > 0)
                {
                    // Вверх (Unity Y растёт вверх) → апперкот; сторона от начала x
                    type = PunchType.Upper;
                    side = start.x < Screen.width * 0.5f ? -1 : 1;
                }
                else
                {
                    // Вниз → оверхенд (Mega); сторона от начала x
                    type = PunchType.Mega;
                    side = start.x < Screen.width * 0.5f ? -1 : 1;
                }
                p = new PunchInput
                {
                    type    = type,
                    side    = side,
                    speed   = speed,
                    pw      = pw,
                    screenStart = start,
                    screenEnd   = end,
                };
            }
            Inject(p);
        }

        // ---- Вспомогательные ------------------------------------------------

        void RefreshBlockState()
        {
            IsBlocking = false;
            for (int i = 0; i < TRACK_MAX; i++)
                if (_touches[i].isActive && _touches[i].isBlock)
                {
                    IsBlocking = true;
                    return;
                }
        }

        bool IsBottomZone(Vector2 pos) => pos.y < Screen.height * 0.20f;

        bool IsOverUI(Vector2 pos)
        {
            if (EventSystem.current == null) return false;
            if (_pd == null) _pd = new PointerEventData(EventSystem.current);
            _pd.position = pos;
            EventSystem.current.RaycastAll(_pd, _raycastBuf);
            bool over = _raycastBuf.Count > 0;
            _raycastBuf.Clear();
            return over;
        }

        int FindFreeSlot()
        {
            for (int i = 0; i < TRACK_MAX; i++)
                if (!_touches[i].isActive) return i;
            return -1;
        }

        int FindSlot(int id)
        {
            for (int i = 0; i < TRACK_MAX; i++)
                if (_touches[i].isActive && _touches[i].fingerId == id) return i;
            return -1;
        }

        float FindElapsed(int id)
        {
            int s = FindSlot(id);
            return s >= 0 ? Time.unscaledTime - _touches[s].startTime : 0f;
        }
    }
}
