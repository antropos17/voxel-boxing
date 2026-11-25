using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace VoxGame
{
    // Читает касания/мышь → очередь PunchInput. Своего Update нет: FightDirector зовёт Tick() перед разбором ввода,
    // поэтому на паузе и вне боя касания не копятся. Init() включает EnhancedTouch (вызывает создатель).
    // Inject() добавляет синтетические жесты для тестов.
    // Не реагирует на касания над кнопками UI.
    // IsBlocking = true, когда палец держит нижнюю зону экрана дольше BlockHold и почти не сдвинулся;
    // касание из нижней зоны, ушедшее дальше порога свайпа, — удар (апперкот снизу), короткий тап там — джеб.
    // Порог свайпа и сила удара считаются в dp (как CSS-пиксели веб-версии), а не в физических пикселях.
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

        public int Count => _qCount;

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
            public bool    isBlock;    // началось в нижней зоне и пока не стало свайпом
            public bool    isActive;
        }
        readonly TouchState[] _touches = new TouchState[TRACK_MAX];
        readonly List<RaycastResult> _raycastBuf = new List<RaycastResult>(8);
        PointerEventData _pd;

        const float BlockHold = 0.12f;   // с: удержание нижней зоны дольше — блок; короче и без сдвига — джеб

        // ---- Включение -------------------------------------------------------

        public void Init()
        {
            if (Application.isPlaying && !EnhancedTouchSupport.enabled)
                EnhancedTouchSupport.Enable();
        }

        // ---- Tick (из FightDirector.Tick) -----------------------------------

        public void Tick()
        {
            // вне Play Mode (сквозной проход, снимки) устройств нет — только синтетическая очередь
            if (!Application.isPlaying) return;
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
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
                    case UnityEngine.InputSystem.TouchPhase.Moved:
                        OnTouchMoved(t.touchId, t.screenPosition);
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
            else if (mouse.leftButton.isPressed)
                OnTouchMoved(MOUSE_ID, mouse.position.ReadValue());
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

        // Палец из нижней зоны ушёл дальше порога свайпа — это удар, а не блок.
        void OnTouchMoved(int id, Vector2 pos)
        {
            int slot = FindSlot(id);
            if (slot < 0 || !_touches[slot].isBlock) return;
            if (IsSwipe(_touches[slot].start, pos)) _touches[slot].isBlock = false;
        }

        void OnTouchEnded(int id, Vector2 endPos, float duration)
        {
            int slot = FindSlot(id);
            if (slot < 0) return;
            var st = _touches[slot];
            _touches[slot] = default;
            // удержание нижней зоны без сдвига — блок, не удар; короткий тап там — обычный джеб
            if (st.isBlock && !IsSwipe(st.start, endPos) && Time.unscaledTime - st.startTime >= BlockHold) return;
            ClassifyGesture(st.start, endPos, duration);
        }

        static float Px() => GameUtil.PixelScale(Screen.dpi, Screen.width, Screen.height);

        static bool IsSwipe(Vector2 a, Vector2 b)
        {
            float px = Px();
            return (b - a).magnitude / px >= GameUtil.SwipeThreshold(Screen.width / px, Screen.height / px);
        }

        void ClassifyGesture(Vector2 start, Vector2 end, float duration)
        {
            // всё в dp: порог свайпа и скорость как в веб-версии (CSS-пиксели), на плотных экранах не завышены
            float px = Px();
            Vector2 delta = end - start;
            float dist  = delta.magnitude / px;
            float swipe = GameUtil.SwipeThreshold(Screen.width / px, Screen.height / px);
            // speed = dist / max(16, dur_ms) dp/ms
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
                if (_touches[i].isActive && _touches[i].isBlock && Time.unscaledTime - _touches[i].startTime >= BlockHold)
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
