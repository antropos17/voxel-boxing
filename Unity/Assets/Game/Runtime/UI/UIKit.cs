// UIKit.cs — фабрика uGUI-элементов, палитра, кегли. Ни один метод не MonoBehaviour.
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    // Общие строительные блоки UI: панель, надпись, кнопка, полоса-индикатор, якоря.
    // Единая палитра со шкалами боя (UI-MOTION.md): тёмный «чернильный» фон, кость, золото, кровь, синий соперника.
    // Кнопки с фаской (блик сверху, тёмная губа снизу) и чернильной рамкой, карточки с золотой кромкой, заголовки-печати.
    // Рамка из четырёх полос (UIKit.Frame): цвет меняется целиком.
    public class UIBorder : MonoBehaviour {
        public Image[] sides;
        public Color Color {
            get => sides[0].color;
            set { for (int i = 0; i < sides.Length; i++) sides[i].color = value; }
        }
    }

    public static class UIKit {

        // ------------------------------------------------------------------ Палитра

        public static readonly Color BgDark    = new Color32(14,  12,  18,  255);
        public static readonly Color BgPanel   = new Color32(24,  19,  30,  240);
        public static readonly Color BgPanelLt = new Color32(46,  38,  56,  235);
        public static readonly Color Accent     = new Color32(244, 182,  46, 255);   // золото шкал
        public static readonly Color AccentDim  = new Color32(128,  86,  18, 240);
        public static readonly Color TextMain   = new Color32(245, 239, 226, 255);
        public static readonly Color TextSub    = new Color32(176, 167, 152, 255);
        public static readonly Color Bone       = new Color32(236, 226, 200, 255);
        public static readonly Color Blood      = new Color32(200,  35,  28, 255);
        public static readonly Color Ink        = new Color32(8,    6,  12, 255);
        public static readonly Color FoeBlue    = new Color32(59,  130, 240, 255);
        public static readonly Color HpGreen    = new Color32(68,  200,  85, 255);
        public static readonly Color HpRed      = new Color32(220,  50,  50, 255);
        public static readonly Color RedText    = new Color32(255, 120, 102, 255);   // красная надпись на тёмном: контраст ≥ 4,5
        public static readonly Color StamBlue   = new Color32(65,  145, 230, 255);
        public static readonly Color MegaGold   = new Color32(255, 200,  38, 255);

        // ------------------------------------------------------------------ Кегли

        public const int FontXl = 46;
        public const int FontLg = 36;
        public const int FontMd = 26;
        public const int FontSm = 20;
        public const int FontXs = 16;

        // ------------------------------------------------------------------ Шрифты

        static Font _semi, _regular;

        public static Font SemiBold {
            get {
                if (_semi == null) _semi = Resources.Load<Font>("Fonts/Inter-SemiBold");
                return _semi;
            }
        }

        public static Font Regular {
            get {
                if (_regular == null) _regular = Resources.Load<Font>("Fonts/Inter-Regular");
                return _regular;
            }
        }

        // ------------------------------------------------------------------ Общий обработчик нажатий

        // Вызывается каждой кнопкой из Btn до её собственного обработчика (звук Ui). Ставит GameApp.
        public static System.Action OnButtonClick;

        public static void Click() => OnButtonClick?.Invoke();

        // ------------------------------------------------------------------ Фабрика

        // Панель: RectTransform + Image (сплошной цвет). Панель цвета BgPanel — карточка: чернильная рамка и золотая кромка сверху.
        public static RectTransform Panel(Transform parent, string name, Color color) {
            var rt = Plain(parent, name, color);
            if (color == BgPanel) Card(rt);
            return rt;
        }

        public static RectTransform Plain(Transform parent, string name, Color color) {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        // Полоса-украшение внутри rt: доли по x, по y — от края в пикселях (h > 0 — сверху, h < 0 — снизу).
        public static RectTransform Strip(RectTransform rt, string name, Color c, float x0, float x1, float h) {
            var s = Plain(rt, name, c);
            if (h > 0f) { SetAnchors(s, x0, 1f, x1, 1f); s.offsetMin = new Vector2(0f, -h); s.offsetMax = Vector2.zero; }
            else        { SetAnchors(s, x0, 0f, x1, 0f); s.offsetMin = Vector2.zero; s.offsetMax = new Vector2(0f, -h); }
            return s;
        }

        // Карточка: рамка цвета чернил и золотая кромка сверху (короткая золотая и длинная тусклая — как подпись шкалы).
        public static void Card(RectTransform rt) {
            Frame(rt, new Color(0f, 0f, 0f, 0.85f), 2f);
            Strip(rt, "EdgeDim", new Color(Accent.r, Accent.g, Accent.b, 0.22f), 0f, 1f, 2f);
            Strip(rt, "Edge", Accent, 0f, 0.18f, 4f);
        }

        // Рамка внутри края: четыре тонкие полосы-дети (Outline на подложке подкрашивает её целиком — не годится).
        public static UIBorder Frame(RectTransform rt, Color c, float px) {
            var b = rt.gameObject.AddComponent<UIBorder>();
            b.sides = new Image[4];
            for (int i = 0; i < 4; i++) {
                var s = Plain(rt, "Border" + i, c);
                if (i == 0) { SetAnchors(s, 0f, 1f, 1f, 1f); s.offsetMin = new Vector2(0f, -px); s.offsetMax = Vector2.zero; }
                else if (i == 1) { SetAnchors(s, 0f, 0f, 1f, 0f); s.offsetMin = Vector2.zero; s.offsetMax = new Vector2(0f, px); }
                else if (i == 2) { SetAnchors(s, 0f, 0f, 0f, 1f); s.offsetMin = Vector2.zero; s.offsetMax = new Vector2(px, 0f); }
                else { SetAnchors(s, 1f, 0f, 1f, 1f); s.offsetMin = new Vector2(-px, 0f); s.offsetMax = Vector2.zero; }
                b.sides[i] = s.GetComponent<Image>();
            }
            return b;
        }

        // Надпись: Text с Inter-SemiBold (или Inter-Regular) + мягкая Outline.
        public static Text Label(Transform parent, string name,
                                 int fontSize, Color color,
                                 bool semiBold = true,
                                 TextAnchor align = TextAnchor.MiddleCenter) {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font          = semiBold ? SemiBold : Regular;
            t.fontSize      = fontSize;
            t.color         = color;
            t.alignment     = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow   = VerticalWrapMode.Overflow;
            t.supportRichText    = false;
            t.raycastTarget      = false;
            var o = go.AddComponent<Outline>();
            o.effectColor    = new Color(0f, 0f, 0f, 0.55f);
            o.effectDistance = new Vector2(1.5f, -1.5f);
            // крупный золотой заголовок — «печать»: жёсткая чернильная тень
            if (color == Accent && fontSize >= FontLg) {
                var sh = go.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.9f); sh.effectDistance = new Vector2(3f, -4f);
            }
            return t;
        }

        // Кнопка: высота ≥ 88 px опорных (пальцевой минимум) — задаёт раскладка экрана.
        // Якоря не устанавливаются — задаются после вызова.
        public static Button Btn(Transform parent, string name, string caption,
                                 int fontSize = FontMd,
                                 Color? bgColor = null, Color? textColor = null) {
            var rt = Plain(parent, name, bgColor ?? BgPanel);
            rt.sizeDelta = new Vector2(200f, 96f);
            rt.gameObject.GetComponent<Image>().raycastTarget = true;
            // фаска: чернильная рамка, блик сверху, тёмная губа снизу (поверх любого цвета лица кнопки)
            Frame(rt, new Color(Ink.r, Ink.g, Ink.b, 0.95f), 3f);
            Strip(rt, "Hi", new Color(1f, 1f, 1f, 0.13f), 0f, 1f, 6f);
            Strip(rt, "Lip", new Color(0f, 0f, 0f, 0.34f), 0f, 1f, -8f);

            var lbl = Label(rt, "Lbl", fontSize, textColor ?? TextMain, semiBold: true);
            SetAnchors(lbl.rectTransform, 0, 0, 1, 1);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;
            lbl.text = caption;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(Click);
            rt.gameObject.AddComponent<UIPress>();   // сжатие под пальцем (UIMotion), нажатие не задерживает
            var c = btn.colors;
            c.normalColor      = Color.white;
            c.highlightedColor = new Color(1.3f, 1.3f, 1.1f, 1f);
            c.pressedColor     = new Color(0.75f, 0.75f, 0.75f, 1f);
            btn.colors = c;
            return btn;
        }

        // Полоса-индикатор с anchor-заливкой (не требует спрайта).
        // Вызывай SetBarH / SetBarV для обновления уровня.
        public static (RectTransform bg, RectTransform fill) Bar(
                Transform parent, string name,
                Color bgColor, Color fillColor,
                bool vertical = false) {

            var bg = Panel(parent, name, bgColor);

            var fillRT = Panel(bg, name + "_F", fillColor);
            // Якоря fill: левый/нижний = 0, правый/верхний = 1 (по умолчанию полный)
            SetAnchors(fillRT, 0f, 0f, 1f, 1f);
            fillRT.offsetMin = new Vector2(1f, 1f);
            fillRT.offsetMax = new Vector2(-1f, -1f);

            if (vertical) {
                // Начинаем пустой снизу — anchorMax.y = 0
                var am = fillRT.anchorMax;
                am.y = 0f;
                fillRT.anchorMax = am;
            } else {
                // Начинаем полным — anchorMax.x = 1
            }

            return (bg, fillRT);
        }

        // Обновить горизонтальную полосу (0..1).
        public static void SetBarH(RectTransform fill, float ratio) {
            var am = fill.anchorMax;
            am.x = Mathf.Clamp01(ratio);
            fill.anchorMax = am;
        }

        // Обновить вертикальную полосу снизу вверх (0..1).
        public static void SetBarV(RectTransform fill, float ratio) {
            var am = fill.anchorMax;
            am.y = Mathf.Clamp01(ratio);
            fill.anchorMax = am;
        }

        // ------------------------------------------------------------------ Якоря / утилиты

        public static void SetAnchors(RectTransform rt,
                                      float xMin, float yMin,
                                      float xMax, float yMax) {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
        }

        // Прибить к точечному якорю с фиксированным размером (пикселей в опорной резолюции).
        public static void SetCorner(RectTransform rt,
                                     float anchorX, float anchorY,
                                     float pivotX,  float pivotY,
                                     float w, float h,
                                     float offsetX = 0f, float offsetY = 0f) {
            rt.anchorMin = rt.anchorMax = new Vector2(anchorX, anchorY);
            rt.pivot      = new Vector2(pivotX, pivotY);
            rt.sizeDelta  = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(offsetX, offsetY);
        }

        // Уничтожение (DestroyImmediate вне Play Mode в редакторе).
        public static void Destroy(Object obj) {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(obj);
            else
#endif
                Object.Destroy(obj);
        }
    }
}
