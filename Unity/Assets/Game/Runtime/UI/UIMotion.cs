// UIMotion.cs — единая система движения интерфейса (UI-MOTION.md).
// Все длительности и кривые — здесь. Твины лежат в массиве фиксированного размера: в кадре нет выделений памяти.
// Тикает UIRoot.Tick (реальное время). Вне Play Mode (тесты EditMode, проход GameWalk, снимки) движение мгновенное,
// если не включён ForceAnimate — так проверки видят конечное состояние экрана сразу.
// Ввод анимация не держит: кнопка экрана нажимается с первого кадра появления, уходящий экран лучи не ловит.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VoxGame {

    public enum Ease { Linear, OutCubic, InCubic, OutQuint, OutBack, InOutSine }

    public static class UIMotion {

        // ------------------------------------------------------------------ Длительности, с (120–350 мс)

        public const float PressIn     = 0.12f;   // кнопка вниз
        public const float PressOut    = 0.22f;   // кнопка обратно (с перелётом)
        public const float ScreenIn    = 0.28f;   // экран появляется
        public const float ScreenOut   = 0.16f;   // экран уходит (быстрее появления)
        public const float ItemIn      = 0.26f;   // элемент каскада
        public const float StaggerStep = 0.035f;  // шаг каскада
        public const int   StaggerMax  = 10;      // дальше каскад не растягивается (последние идут вместе)
        public const float CountUp     = 0.35f;   // подсчёт цифр
        public const float PopIn       = 0.20f;   // всплытие надписи/отметки

        // ------------------------------------------------------------------ Амплитуды

        public const float ScreenRise  = 28f;     // опорных px: экран всплывает снизу
        public const float ScreenScale = 0.975f;
        public const float ItemScale   = 0.94f;
        public const float PressScale  = 0.94f;

        // ------------------------------------------------------------------ Кривые

        public static float Eval(Ease e, float t) {
            t = Mathf.Clamp01(t);
            switch (e) {
                case Ease.OutCubic:  { float u = 1f - t; return 1f - u * u * u; }
                case Ease.InCubic:   return t * t * t;
                case Ease.OutQuint:  { float u = 1f - t; return 1f - u * u * u * u * u; }
                case Ease.OutBack:   { const float c1 = 1.70158f, c3 = c1 + 1f; float u = t - 1f; return 1f + c3 * u * u * u + c1 * u * u; }
                case Ease.InOutSine: return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
                default:             return t;
            }
        }

        // ------------------------------------------------------------------ Режимы

        // «Меньше движения» (настройки): без тряски, вспышек, перелётов и каскада; экраны только проявляются.
        public static bool Reduced;
        // Анимировать и вне Play Mode (тесты движения, серии снимков).
        public static bool ForceAnimate;
        public static bool Animate => ForceAnimate || Application.isPlaying;

        // ------------------------------------------------------------------ Твины

        enum Kind : byte { None, Alpha, Scale, OffsetY, Count, BarH }

        struct Tween {
            public Kind kind;
            public Object target;          // CanvasGroup / RectTransform / Text
            public float from, to, t, dur, delay;
            public Ease ease;
            public bool deactivate;        // по окончании выключить объект (уход экрана)
            public string prefix, suffix;  // подсчёт: строка собирается только при смене числа
            public int shown;
        }

        public const int Capacity = 160;
        static readonly Tween[] _tw = new Tween[Capacity];
        static int _n;

        public static int Active => _n;

        static int Find(Object target, Kind k) {
            for (int i = 0; i < _n; i++) if (_tw[i].kind == k && _tw[i].target == target) return i;
            return -1;
        }

        static void Add(Kind k, Object target, float from, float to, float dur, float delay, Ease ease, bool deactivate = false,
                        string prefix = null, string suffix = null) {
            if (target == null) return;
            int i = Find(target, k);
            if (i < 0) {
                if (_n >= Capacity) { var tmp = new Tween { kind = k, target = target, to = to, prefix = prefix, suffix = suffix, deactivate = deactivate }; Apply(ref tmp, 1f); return; }
                i = _n++;
            }
            _tw[i] = new Tween { kind = k, target = target, from = from, to = to, t = 0f, dur = Mathf.Max(0.0001f, dur), delay = delay,
                                 ease = ease, deactivate = deactivate, prefix = prefix, suffix = suffix, shown = int.MinValue };
            if (!Animate) { Apply(ref _tw[i], 1f); RemoveAt(i); }
            else Apply(ref _tw[i], 0f);   // с задержкой тоже: элемент ждёт своей очереди в начальном виде
        }

        static void RemoveAt(int i) { _n--; _tw[i] = _tw[_n]; _tw[_n] = default; }

        static void Apply(ref Tween w, float k) {
            float v = Mathf.LerpUnclamped(w.from, w.to, Eval(w.ease, k));
            switch (w.kind) {
                case Kind.Alpha: {
                    var g = w.target as CanvasGroup; if (g == null) return;
                    g.alpha = v;
                    if (k >= 1f && w.deactivate) { g.gameObject.SetActive(false); g.alpha = 1f; g.blocksRaycasts = true; }
                    break;
                }
                case Kind.Scale: {
                    var rt = w.target as RectTransform; if (rt == null) return;
                    rt.localScale = new Vector3(v, v, 1f);
                    break;
                }
                case Kind.OffsetY: {
                    var rt = w.target as RectTransform; if (rt == null) return;
                    rt.offsetMin = new Vector2(0f, v); rt.offsetMax = new Vector2(0f, v);
                    break;
                }
                case Kind.BarH: {
                    var rt = w.target as RectTransform; if (rt == null) return;
                    var am = rt.anchorMax; am.x = Mathf.Clamp01(v); rt.anchorMax = am;
                    break;
                }
                case Kind.Count: {
                    var t = w.target as Text; if (t == null) return;
                    int n = Mathf.RoundToInt(v);
                    if (n != w.shown) { w.shown = n; t.text = w.prefix + n + w.suffix; }
                    break;
                }
            }
        }

        // UIRoot.Tick: продвинуть все твины на реальное время udt.
        public static void Tick(float udt) {
            for (int i = 0; i < _n; ) {
                ref var w = ref _tw[i];
                if (w.target == null) { RemoveAt(i); continue; }
                float step = udt;
                if (w.delay > 0f) {
                    w.delay -= udt;
                    if (w.delay > 0f) { i++; continue; }
                    step = -w.delay; w.delay = 0f;   // остаток шага — в сам твин
                }
                w.t += step;
                float k = w.t / w.dur;
                Apply(ref w, k);
                if (k >= 1f) RemoveAt(i); else i++;
            }
        }

        // Довести всё до конца (смена ориентации, снимок, выключение).
        public static void CompleteAll() {
            while (_n > 0) { Apply(ref _tw[_n - 1], 1f); RemoveAt(_n - 1); }
        }

        public static void Complete(Object target) {
            if (target == null) return;
            for (int i = 0; i < _n; ) if (_tw[i].target == target) { Apply(ref _tw[i], 1f); RemoveAt(i); } else i++;
        }

        public static bool IsAnimating(Object target) {
            for (int i = 0; i < _n; i++) if (_tw[i].target == target) return true;
            return false;
        }

        // ------------------------------------------------------------------ Готовые движения

        public static CanvasGroup Group(GameObject go) {
            var g = go.GetComponent<CanvasGroup>();
            if (g == null) g = go.AddComponent<CanvasGroup>();
            return g;
        }

        public static void Fade(CanvasGroup g, float from, float to, float dur, Ease e, float delay = 0f, bool deactivate = false) =>
            Add(Kind.Alpha, g, from, to, dur, delay, e, deactivate);

        public static void Scale(RectTransform rt, float from, float to, float dur, Ease e, float delay = 0f) =>
            Add(Kind.Scale, rt, from, to, dur, delay, e);

        // Полоса UIKit.Bar: заливка растёт от from до to (доли).
        public static void Bar(RectTransform fill, float from, float to, float dur = CountUp, float delay = 0f) =>
            Add(Kind.BarH, fill, from, to, dur, delay, Ease.OutCubic);

        // Печать: элемент падает из крупного в свой размер с перелётом (заголовки итогов, отметки).
        public static void Stamp(RectTransform rt, float delay = 0f) {
            if (Reduced) { Complete(rt); rt.localScale = Vector3.one; return; }
            Add(Kind.Scale, rt, 1.45f, 1f, PopIn + 0.1f, delay, Ease.OutBack);
        }

        // Подсчёт числа в надписи: prefix + n + suffix; конечная строка — та же, что записал бы экран без анимации.
        public static void Count(Text t, int from, int to, string prefix, string suffix, float dur = CountUp, float delay = 0f) {
            if (!Animate || from == to) { Complete(t); t.text = prefix + to + suffix; return; }
            Add(Kind.Count, t, from, to, dur, delay, Ease.OutCubic, false, prefix, suffix);
        }

        // Появление экрана: проявление, подъём снизу и лёгкое приближение; затем каскад содержимого.
        // Кнопки нажимаются сразу: blocksRaycasts и interactable не трогаются.
        public static void ScreenEnter(GameObject screen) {
            var g = Group(screen);
            g.blocksRaycasts = true; g.interactable = true;
            var rt = screen.transform as RectTransform;
            Fade(g, 0f, 1f, ScreenIn, Ease.OutCubic);
            if (rt == null) return;
            if (Reduced) { Complete(rt); rt.localScale = Vector3.one; return; }
            Add(Kind.OffsetY, rt, -ScreenRise, 0f, ScreenIn, 0f, Ease.OutQuint);
            Scale(rt, ScreenScale, 1f, ScreenIn, Ease.OutCubic);
            Cascade(rt);
        }

        // Уход экрана: гаснет и выключается; нажатия сквозь него уже не идут.
        public static void ScreenExit(GameObject screen) {
            var g = Group(screen);
            if (!Animate || !screen.activeSelf) { Complete(g); screen.SetActive(false); return; }
            g.blocksRaycasts = false;
            var rt = screen.transform as RectTransform;
            if (rt != null) { Complete(rt); if (!Reduced) Scale(rt, 1f, 0.985f, ScreenOut, Ease.InCubic); }
            Fade(g, g.alpha, 0f, ScreenOut, Ease.InCubic, 0f, deactivate: true);
        }

        // Каскад: прямые дети экрана (и строки списков) сверху вниз проявляются с шагом StaggerStep.
        static readonly System.Collections.Generic.List<RectTransform> _items = new System.Collections.Generic.List<RectTransform>(48);
        static readonly Vector3[] _corners = new Vector3[4];

        static void Cascade(RectTransform root) {
            if (!Animate) return;
            _items.Clear();
            for (int i = 0; i < root.childCount; i++) {
                var c = root.GetChild(i) as RectTransform;
                if (c == null || !c.gameObject.activeSelf || c.GetComponent<NoCascade>() != null) continue;
                var sr = c.GetComponentInChildren<ScrollRect>(false);
                if (sr != null && sr.content != null) {
                    for (int j = 0; j < sr.content.childCount && _items.Count < 40; j++) {
                        var r = sr.content.GetChild(j) as RectTransform;
                        if (r != null && r.gameObject.activeSelf) _items.Add(r);
                    }
                    continue;
                }
                _items.Add(c);
            }
            // сверху вниз, слева направо (по верхнему левому углу)
            _items.Sort((a, b) => {
                a.GetWorldCorners(_corners); float ay = _corners[1].y, ax = _corners[1].x;
                b.GetWorldCorners(_corners); float by = _corners[1].y, bx = _corners[1].x;
                return Mathf.Abs(ay - by) > 1e-4f ? by.CompareTo(ay) : ax.CompareTo(bx);
            });
            for (int i = 0; i < _items.Count; i++) {
                float d = Mathf.Min(i, StaggerMax) * StaggerStep;
                var it = _items[i];
                Fade(Group(it.gameObject), 0f, 1f, ItemIn, Ease.OutCubic, d);
                if (Find(it, Kind.Scale) < 0) Scale(it, ItemScale, 1f, ItemIn, Ease.OutBack, d);   // свою печать экран ставит сам
            }
            _items.Clear();
        }
    }

    // Метка: элемент не участвует в каскаде (фон экрана, затемнения, всплывающие окна).
    public class NoCascade : MonoBehaviour { }

    // Отклик кнопки на палец: сжатие при касании, возврат с лёгким перелётом. onClick кнопки не задерживается.
    public class UIPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler {
        bool _down;
        public void OnPointerDown(PointerEventData e) {
            var b = GetComponent<Selectable>();
            if (b != null && !b.IsInteractable()) return;
            _down = true;
            if (!UIMotion.Reduced) UIMotion.Scale((RectTransform)transform, transform.localScale.x, UIMotion.PressScale, UIMotion.PressIn, Ease.OutCubic);
        }
        public void OnPointerUp(PointerEventData e) => Release();
        public void OnPointerExit(PointerEventData e) { if (_down) Release(); }
        void Release() {
            _down = false;
            if (!UIMotion.Reduced) UIMotion.Scale((RectTransform)transform, transform.localScale.x, 1f, UIMotion.PressOut, Ease.OutBack);
            else transform.localScale = Vector3.one;
        }
        void OnDisable() { _down = false; UIMotion.Complete(transform); transform.localScale = Vector3.one; }
    }
}
