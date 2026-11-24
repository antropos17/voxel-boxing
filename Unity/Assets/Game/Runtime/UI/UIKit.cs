// UIKit.cs — фабрика uGUI-элементов, палитра, кегли. Ни один метод не MonoBehaviour.
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    // Общие строительные блоки UI: панель, надпись, кнопка, полоса-индикатор, якоря.
    // Единая палитра: тёмный фон (#0E0C12), тёплый оранжевый акцент (#E68C28).
    public static class UIKit {

        // ------------------------------------------------------------------ Палитра

        public static readonly Color BgDark    = new Color32(14,  12,  18,  255);
        public static readonly Color BgPanel   = new Color32(28,  24,  36,  220);
        public static readonly Color BgPanelLt = new Color32(42,  36,  54,  200);
        public static readonly Color Accent     = new Color32(230, 140,  40, 255);
        public static readonly Color AccentDim  = new Color32(170,  95,  20, 180);
        public static readonly Color TextMain   = new Color32(240, 236, 228, 255);
        public static readonly Color TextSub    = new Color32(155, 150, 142, 255);
        public static readonly Color HpGreen    = new Color32(68,  200,  85, 255);
        public static readonly Color HpRed      = new Color32(220,  50,  50, 255);
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

        // ------------------------------------------------------------------ Фабрика

        // Панель: RectTransform + Image (сплошной цвет).
        public static RectTransform Panel(Transform parent, string name, Color color) {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go.GetComponent<RectTransform>();
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
            return t;
        }

        // Кнопка: высота ≥ 96 px опорных (пальцевой минимум).
        // Якоря не устанавливаются — задаются после вызова.
        public static Button Btn(Transform parent, string name, string caption,
                                 int fontSize = FontMd,
                                 Color? bgColor = null, Color? textColor = null) {
            var rt = Panel(parent, name, bgColor ?? BgPanel);
            rt.sizeDelta = new Vector2(200f, 96f);
            rt.gameObject.GetComponent<Image>().raycastTarget = true;

            var lbl = Label(rt, "Lbl", fontSize, textColor ?? TextMain, semiBold: true);
            SetAnchors(lbl.rectTransform, 0, 0, 1, 1);
            lbl.rectTransform.offsetMin = Vector2.zero;
            lbl.rectTransform.offsetMax = Vector2.zero;
            lbl.text = caption;

            var btn = rt.gameObject.AddComponent<Button>();
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
