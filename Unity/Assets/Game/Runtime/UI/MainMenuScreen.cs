// MainMenuScreen.cs — главное меню поверх 3D-ринга: название, строка профиля, ИГРАТЬ / БОЕЦ / МАГАЗИН / НАСТРОЙКИ.
// Здесь же MenuKit и GloveStatsView — общие приёмы экранов меню (раскладка долями, кнопки, полосы свойств перчаток).
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class MainMenuScreen : MonoBehaviour, ILayoutHandler {

        public Action OnPlay, OnPlayer, OnShop, OnSettings;

        Text _title, _sub, _profile;
        RectTransform _topShade, _bottomShade, _line;
        Button _playBtn, _playerBtn, _shopBtn, _settingsBtn;

        public void Init(UIRoot root, Action onPlay, Action onPlayer, Action onShop, Action onSettings) {
            OnPlay = onPlay; OnPlayer = onPlayer; OnShop = onShop; OnSettings = onSettings;
            BuildUI();
            root.Register(ScreenId.MainMenu, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);

            // Полупрозрачные затемнения сверху и снизу: ринг за ними виден, текст и кнопки читаются
            _topShade = UIKit.Panel(transform, "TopShade", new Color(UIKit.BgDark.r, UIKit.BgDark.g, UIKit.BgDark.b, 0.62f));
            _bottomShade = UIKit.Panel(transform, "BottomShade", new Color(UIKit.BgDark.r, UIKit.BgDark.g, UIKit.BgDark.b, 0.70f));
            _topShade.gameObject.AddComponent<NoCascade>(); _bottomShade.gameObject.AddComponent<NoCascade>();

            _title = UIKit.Label(transform, "Title", 64, UIKit.Accent, semiBold: true);
            _title.text = "ВОКСЕЛЬ БОКС";
            var sh = _title.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(4f, -4f);

            _sub = UIKit.Label(transform, "Sub", UIKit.FontSm, UIKit.TextSub, semiBold: false);
            _sub.text = "Бокс от первого лица";
            _line = UIKit.Panel(transform, "Line", UIKit.AccentDim);

            _profile = UIKit.Label(transform, "Profile", UIKit.FontMd, UIKit.TextMain, semiBold: true);

            _playBtn = MenuKit.Primary(transform, "BtnPlay", "ИГРАТЬ", UIKit.FontXl + 6);
            _playBtn.onClick.AddListener(PressPlay);
            _playerBtn = MenuKit.Secondary(transform, "BtnPlayer", "БОЕЦ", UIKit.FontMd);
            _playerBtn.onClick.AddListener(PressPlayer);
            _shopBtn = MenuKit.Secondary(transform, "BtnShop", "МАГАЗИН", UIKit.FontMd);
            _shopBtn.onClick.AddListener(PressShop);
            _settingsBtn = MenuKit.Secondary(transform, "BtnSettings", "НАСТРОЙКИ", UIKit.FontMd);
            _settingsBtn.onClick.AddListener(PressSettings);

            Layout(false);
        }

        // Строка профиля: имя, уровень, монеты.
        public void SetProfile(PlayerProfile p) {
            if (p == null) return;
            _profile.text = p.playerName + "  ·  Ур. " + p.level + "  ·  " + MenuKit.Coins(p.coins);
        }

        public string ProfileText => _profile.text;

        // ------------------------------------------------------------------ Нажатия

        public void PressPlay()     => OnPlay?.Invoke();
        public void PressPlayer()   => OnPlayer?.Invoke();
        public void PressShop()     => OnShop?.Invoke();
        public void PressSettings() => OnSettings?.Invoke();

        // ------------------------------------------------------------------ Раскладка

        public void OnLayout(bool landscape) => Layout(landscape);

        public void Layout(bool landscape) {
            _title.fontSize = landscape ? 64 : 56;
            if (landscape) {
                // Слева название и профиль, справа столбец кнопок; ринг виден посередине
                MenuKit.Bleed(_topShade, 0f, 0.62f, 1f, 1f, top: true);
                MenuKit.Bleed(_bottomShade, 0.58f, 0f, 1f, 0.62f, top: false);
                MenuKit.Place(_title.rectTransform,   0.02f, 0.80f, 0.56f, 0.99f);
                MenuKit.Place(_sub.rectTransform,     0.02f, 0.72f, 0.56f, 0.80f);
                MenuKit.Place(_line,                  0.04f, 0.705f, 0.54f, 0.712f);
                MenuKit.Place(_profile.rectTransform, 0.02f, 0.62f, 0.56f, 0.70f);
                MenuKit.Place(_playBtn.GetComponent<RectTransform>(),     0.60f, 0.70f, 0.98f, 0.97f);
                MenuKit.Place(_playerBtn.GetComponent<RectTransform>(),   0.60f, 0.48f, 0.98f, 0.67f);
                MenuKit.Place(_shopBtn.GetComponent<RectTransform>(),     0.60f, 0.26f, 0.98f, 0.45f);
                MenuKit.Place(_settingsBtn.GetComponent<RectTransform>(), 0.60f, 0.03f, 0.98f, 0.23f);
            } else {
                MenuKit.Bleed(_topShade, 0f, 0.74f, 1f, 1f, top: true);
                MenuKit.Bleed(_bottomShade, 0f, 0f, 1f, 0.45f, top: false);
                MenuKit.Place(_title.rectTransform,   0.03f, 0.865f, 0.97f, 0.975f);
                MenuKit.Place(_sub.rectTransform,     0.05f, 0.828f, 0.95f, 0.865f);
                MenuKit.Place(_line,                  0.10f, 0.820f, 0.90f, 0.824f);
                MenuKit.Place(_profile.rectTransform, 0.03f, 0.760f, 0.97f, 0.815f);
                MenuKit.Place(_playBtn.GetComponent<RectTransform>(),     0.06f, 0.300f, 0.94f, 0.425f);
                MenuKit.Place(_playerBtn.GetComponent<RectTransform>(),   0.06f, 0.165f, 0.49f, 0.285f);
                MenuKit.Place(_shopBtn.GetComponent<RectTransform>(),     0.51f, 0.165f, 0.94f, 0.285f);
                MenuKit.Place(_settingsBtn.GetComponent<RectTransform>(), 0.06f, 0.030f, 0.94f, 0.150f);
            }
        }

        public void Tick(float dt, float udt) { }
    }

    // ====================================================================== Общие приёмы экранов меню

    public static class MenuKit {

        // Растянуть корень экрана на весь safe-слой.
        public static RectTransform Root(GameObject go) {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            UIKit.SetAnchors(rt, 0f, 0f, 1f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        // Сплошной фон шире safe-area: закрывает экран до краёв, под вырезом и полосой жестов.
        public static RectTransform Backdrop(Transform parent, Color color) {
            var bg = UIKit.Panel(parent, "Bg", color);
            UIKit.SetAnchors(bg, 0f, 0f, 1f, 1f);
            bg.offsetMin = new Vector2(-400f, -400f);
            bg.offsetMax = new Vector2(400f, 400f);
            bg.gameObject.AddComponent<NoCascade>();   // фон стоит, пока содержимое всплывает
            return bg;
        }

        // Затемнение, выходящее за safe-area к ближнему краю экрана.
        public static void Bleed(RectTransform rt, float x0, float y0, float x1, float y1, bool top) {
            UIKit.SetAnchors(rt, x0, y0, x1, y1);
            float l = x0 <= 0f ? -400f : 0f, r = x1 >= 1f ? 400f : 0f;
            rt.offsetMin = new Vector2(l, top ? 0f : -400f);
            rt.offsetMax = new Vector2(r, top ? 400f : 0f);
        }

        public static void Place(RectTransform rt, float x0, float y0, float x1, float y1) {
            UIKit.SetAnchors(rt, x0, y0, x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // Главная кнопка: оранжевая, тёмная надпись без обводки.
        public static Button Primary(Transform parent, string name, string caption, int fontSize) {
            var b = UIKit.Btn(parent, name, caption, fontSize, UIKit.Accent, UIKit.BgDark);
            b.GetComponentInChildren<Outline>().enabled = false;
            var c = b.colors;
            c.disabledColor = Color.white;   // закрытое состояние красим сами
            b.colors = c;
            return b;
        }

        // Обычная кнопка: тёмная подложка со светлой рамкой.
        public static Button Secondary(Transform parent, string name, string caption, int fontSize) {
            var b = UIKit.Btn(parent, name, caption, fontSize, UIKit.BgPanel, UIKit.TextMain);
            var frame = b.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(1f, 1f, 1f, 0.22f);
            frame.effectDistance = new Vector2(2f, -2f);
            var c = b.colors;
            c.disabledColor = Color.white;
            b.colors = c;
            return b;
        }

        public static void SetCaption(Button b, string caption) => b.GetComponentInChildren<Text>().text = caption;

        // Кнопка-действие: доступна (оранжевая) или закрыта (серая с серой надписью).
        public static void SetActionState(Button b, bool enabled) {
            b.interactable = enabled;
            b.GetComponent<Image>().color = enabled ? UIKit.Accent : UIKit.BgPanelLt;
            var lbl = b.GetComponentInChildren<Text>();
            lbl.color = enabled ? UIKit.BgDark : UIKit.TextSub;
            lbl.GetComponent<Outline>().enabled = !enabled;
        }

        // Выбранная вкладка/сегмент — оранжевая рамка и светлая подложка.
        public static void SetToggle(Button b, bool on) {
            b.GetComponent<Image>().color = on ? UIKit.AccentDim : UIKit.BgPanel;
            b.GetComponentInChildren<Text>().color = on ? UIKit.TextMain : UIKit.TextSub;
        }

        public static string Coins(int n) => n + " " + Plural(n, "монета", "монеты", "монет");

        public static string Plural(int n, string one, string few, string many) {
            int a = Mathf.Abs(n) % 100, b = a % 10;
            if (a >= 11 && a <= 14) return many;
            if (b == 1) return one;
            if (b >= 2 && b <= 4) return few;
            return many;
        }

        // Ряд квадратных отметок уровня (воксельные точки): заполнено level из max.
        public static Image[] Pips(Transform parent, string name, int max) {
            var holder = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            holder.SetParent(parent, false);
            var pips = new Image[max];
            for (int i = 0; i < max; i++) {
                var p = UIKit.Panel(holder, "P" + i, UIKit.BgPanelLt);
                // квадраты одинаковой высоты, по горизонтали — доли ширины
                float w = 1f / max;
                UIKit.SetAnchors(p, i * w, 0f, (i + 1) * w, 1f);
                p.offsetMin = new Vector2(2f, 2f);
                p.offsetMax = new Vector2(-6f, -2f);
                pips[i] = p.GetComponent<Image>();
            }
            return pips;
        }

        public static RectTransform PipsRect(Image[] pips) => (RectTransform)pips[0].transform.parent;

        public static void SetPips(Image[] pips, int level) {
            for (int i = 0; i < pips.Length; i++)
                pips[i].color = i < level ? UIKit.Accent : UIKit.BgPanelLt;
        }

        // Короткая надпись-отказ: висит Hold секунд и гаснет.
        public const float ToastHold = 1.6f, ToastFade = 0.4f;

        public static void TickToast(Text t, ref float timer, float udt) {
            if (timer <= 0f) return;
            timer -= udt;
            if (timer <= 0f) { t.gameObject.SetActive(false); return; }
            var c = t.color;
            c.a = Mathf.Clamp01(timer / ToastFade);
            t.color = c;
        }

        public static void ShowToast(Text t, string msg, ref float timer) {
            t.text = msg;
            var c = t.color; c.a = 1f; t.color = c;
            t.gameObject.SetActive(true);
            timer = ToastHold + ToastFade;
        }
    }

    // ====================================================================== Пять свойств перчаток полосами

    public class GloveStatsView {

        public const int Rows = 5;
        static readonly string[] Names = { "Урон", "Скорость", "Выносливость", "Мега", "Защита" };
        static readonly Color[] Colors = { UIKit.HpRed, UIKit.StamBlue, UIKit.HpGreen, UIKit.MegaGold, new Color32(170, 175, 190, 255) };

        public RectTransform Root { get; }
        readonly Text[] _names = new Text[Rows], _values = new Text[Rows];
        readonly RectTransform[] _bars = new RectTransform[Rows], _fills = new RectTransform[Rows];

        public GloveStatsView(Transform parent, int fontSize = UIKit.FontSm) {
            Root = new GameObject("GloveStats", typeof(RectTransform)).GetComponent<RectTransform>();
            Root.SetParent(parent, false);
            for (int i = 0; i < Rows; i++) {
                _names[i] = UIKit.Label(Root, "N" + i, fontSize, UIKit.TextSub, semiBold: false, TextAnchor.MiddleLeft);
                _names[i].text = Names[i];
                (_bars[i], _fills[i]) = UIKit.Bar(Root, "B" + i, new Color32(40, 34, 30, 220), Colors[i]);
                _values[i] = UIKit.Label(Root, "V" + i, fontSize, UIKit.TextMain, semiBold: true, TextAnchor.MiddleRight);
                float y1 = 1f - (float)i / Rows, y0 = 1f - (float)(i + 1) / Rows;
                MenuKit.Place(_names[i].rectTransform, 0f, y0, 0.42f, y1);
                UIKit.SetAnchors(_bars[i], 0.42f, y0, 0.80f, y1);
                _bars[i].offsetMin = new Vector2(0f, 7f);
                _bars[i].offsetMax = new Vector2(0f, -7f);
                MenuKit.Place(_values[i].rectTransform, 0.80f, y0, 1f, y1);
            }
        }

        // Полоса: половина — как у классики; значение — прибавка в процентах.
        public void Set(in GloveEntry g) {
            Row(0, g.dmgMult);
            Row(1, g.speedMult);
            Row(2, 2f - g.staminaCostMult);
            Row(3, g.megaGainMult);
            Row(4, 2f - g.incomingDmgMult);
        }

        public static float Ratio(float m) => Mathf.Clamp01(0.5f + (m - 1f) * 2f);

        public static string Percent(float m) {
            int p = Mathf.RoundToInt((m - 1f) * 100f);
            if (p == 0) return "0%";
            return (p > 0 ? "+" : "−") + Mathf.Abs(p) + "%";
        }

        void Row(int i, float m) {
            UIKit.SetBarH(_fills[i], Ratio(m));
            _values[i].text = Percent(m);
            _values[i].color = m > 1.001f ? UIKit.HpGreen : m < 0.999f ? UIKit.HpRed : UIKit.TextMain;
        }
    }
}
