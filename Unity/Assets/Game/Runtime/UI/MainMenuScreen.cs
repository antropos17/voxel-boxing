// MainMenuScreen.cs — главное меню поверх 3D-ринга: название, строка профиля, ИГРАТЬ / БОЕЦ / МАГАЗИН / НАСТРОЙКИ.
// Здесь же MenuKit и GloveStatsView — общие приёмы экранов меню (раскладка долями, кнопки, полосы свойств перчаток).
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class MainMenuScreen : MonoBehaviour, ILayoutHandler {

        public Action OnPlay, OnPlayer, OnShop, OnSettings;

        Text _title, _sub, _profile, _stampLbl;
        RectTransform _topShade, _bottomShade, _line, _stamp, _shine;
        Button _playBtn, _playerBtn, _shopBtn, _settingsBtn;
        float _idle;
        public const float PlayPulse = 0.025f;   // покой «ИГРАТЬ»: дыхание ±2,5 %, блик раз в 2,4 с
        public const float ShinePeriod = 2.4f;

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

            // заголовок в два яруса: «ВОКСЕЛЬ» костью, «БОКС» — печать на кровавой плашке, чуть наискось
            _title = UIKit.Label(transform, "Title", 64, UIKit.Bone, semiBold: true);
            _title.text = "ВОКСЕЛЬ";
            var sh = _title.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.9f);
            sh.effectDistance = new Vector2(5f, -6f);
            _stamp = UIKit.Plain(transform, "Stamp", UIKit.Blood);
            UIKit.Frame(_stamp, UIKit.Ink, 4f);
            UIKit.Strip(_stamp, "Hi", new Color(1f, 1f, 1f, 0.16f), 0f, 1f, 6f);
            _stampLbl = UIKit.Label(_stamp, "Lbl", 64, UIKit.TextMain, semiBold: true);
            _stampLbl.text = "БОКС";
            MenuKit.Place(_stampLbl.rectTransform, 0f, 0f, 1f, 1f);
            _stamp.localRotation = Quaternion.Euler(0f, 0f, 4f);

            _sub = UIKit.Label(transform, "Sub", UIKit.FontSm, UIKit.TextSub, semiBold: false);
            _sub.text = "Бокс от первого лица";
            _line = MenuKit.Rule(transform);

            _profile = UIKit.Label(transform, "Profile", UIKit.FontMd, UIKit.TextMain, semiBold: true);

            _playBtn = MenuKit.Primary(transform, "BtnPlay", "ИГРАТЬ", UIKit.FontXl + 10);
            _playBtn.onClick.AddListener(PressPlay);
            // блик пробегает по кнопке (маска по её краям)
            _playBtn.gameObject.AddComponent<RectMask2D>();
            _shine = UIKit.Plain(_playBtn.transform, "Shine", new Color(1f, 1f, 1f, 0.30f));
            UIKit.SetAnchors(_shine, 0f, -0.2f, 0f, 1.2f); _shine.sizeDelta = new Vector2(46f, 0f);
            _shine.localRotation = Quaternion.Euler(0f, 0f, -18f);
            _shine.SetSiblingIndex(1);
            _playerBtn = MenuKit.Secondary(transform, "BtnPlayer", "БОЕЦ", UIKit.FontLg);
            _playerBtn.onClick.AddListener(PressPlayer);
            _shopBtn = MenuKit.Secondary(transform, "BtnShop", "МАГАЗИН", UIKit.FontLg);
            _shopBtn.onClick.AddListener(PressShop);
            _settingsBtn = MenuKit.Secondary(transform, "BtnSettings", "НАСТРОЙКИ", UIKit.FontMd);
            _settingsBtn.onClick.AddListener(PressSettings);
            MenuKit.SetNotch(_playerBtn, UIKit.FoeBlue);
            MenuKit.SetNotch(_settingsBtn, UIKit.Bone);
            MenuKit.SubCaption(_playerBtn, "уровень и улучшения");
            MenuKit.SubCaption(_shopBtn, "перчатки");
            _settingsBtn.GetComponent<Image>().color = new Color(UIKit.BgPanel.r, UIKit.BgPanel.g, UIKit.BgPanel.b, 0.55f);

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
            _title.fontSize = landscape ? 78 : 84;
            _stampLbl.fontSize = landscape ? 64 : 70;
            if (landscape) {
                // Слева название и профиль, справа столбец кнопок; ринг виден посередине
                MenuKit.Bleed(_topShade, 0f, 0.62f, 1f, 1f, top: true);
                MenuKit.Bleed(_bottomShade, 0.58f, 0f, 1f, 0.62f, top: false);
                MenuKit.Place(_title.rectTransform,   0.02f, 0.80f, 0.40f, 0.99f);
                MenuKit.Place(_stamp,                 0.36f, 0.80f, 0.56f, 0.97f);
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
                MenuKit.Place(_title.rectTransform,   0.03f, 0.905f, 0.97f, 0.985f);
                MenuKit.Place(_stamp,                 0.25f, 0.840f, 0.75f, 0.905f);
                MenuKit.Place(_sub.rectTransform,     0.05f, 0.800f, 0.95f, 0.835f);
                MenuKit.Place(_line,                  0.10f, 0.792f, 0.90f, 0.795f);
                MenuKit.Place(_profile.rectTransform, 0.03f, 0.735f, 0.97f, 0.788f);
                MenuKit.Place(_playBtn.GetComponent<RectTransform>(),     0.06f, 0.300f, 0.94f, 0.425f);
                MenuKit.Place(_playerBtn.GetComponent<RectTransform>(),   0.06f, 0.165f, 0.49f, 0.285f);
                MenuKit.Place(_shopBtn.GetComponent<RectTransform>(),     0.51f, 0.165f, 0.94f, 0.285f);
                MenuKit.Place(_settingsBtn.GetComponent<RectTransform>(), 0.06f, 0.030f, 0.94f, 0.150f);
            }
        }

        // Покой «ИГРАТЬ»: лёгкое дыхание и блик. «Меньше движения» — без них.
        public void Tick(float dt, float udt) {
            if (_playBtn == null || !_playBtn.gameObject.activeInHierarchy) return;
            _idle += udt; if (_idle > 1000f) _idle -= 1000f;
            var prt = (RectTransform)_playBtn.transform;
            bool still = UIMotion.Reduced || !UIMotion.Animate || UIMotion.IsAnimating(prt);
            float k = still ? 1f : 1f + PlayPulse * Mathf.Sin(_idle * 3.1f);
            if (!UIMotion.IsAnimating(prt) && prt.localScale.x != k) prt.localScale = new Vector3(k, k, 1f);
            float ph = UIMotion.Animate ? (_idle % ShinePeriod) / 0.6f : 0.3f;   // проход блика 0,6 с, затем пауза; без анимации — стоит
            bool on = !UIMotion.Reduced && ph < 1f;
            if (_shine.gameObject.activeSelf != on) _shine.gameObject.SetActive(on);
            if (on) { float w = prt.rect.width; _shine.anchoredPosition = new Vector2(Mathf.Lerp(-60f, w + 60f, ph), 0f); }
        }

        public float PlayScale => _playBtn.transform.localScale.x;
        public bool ShineVisible => _shine.gameObject.activeSelf;
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
        // Поверх — фактура: крупные воксельные клетки, тёплый свет ринга сверху, тёмные края.
        public static RectTransform Backdrop(Transform parent, Color color) {
            var bg = UIKit.Panel(parent, "Bg", color);
            UIKit.SetAnchors(bg, 0f, 0f, 1f, 1f);
            bg.offsetMin = new Vector2(-400f, -400f);
            bg.offsetMax = new Vector2(400f, 400f);
            bg.gameObject.AddComponent<NoCascade>();   // фон стоит, пока содержимое всплывает
            var tex = new GameObject("Tex", typeof(RectTransform), typeof(RawImage));
            tex.transform.SetParent(bg, false);
            var ri = tex.GetComponent<RawImage>(); ri.texture = BackTex; ri.raycastTarget = false;
            ri.color = new Color(1f, 1f, 1f, color.a);
            Place((RectTransform)tex.transform, 0f, 0f, 1f, 1f);
            return bg;
        }

        static Texture2D _backTex;
        static Texture2D BackTex {
            get {
                if (_backTex != null) return _backTex;
                const int W = 48, H = 96;
                var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "MenuBack" };
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++) {
                        float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                        // тёплый свет сверху (кровь с золотом), тёмные края, клетки 2×2 текселя — «воксели»
                        float glow = Mathf.Clamp01(1f - Mathf.Sqrt((u - 0.5f) * (u - 0.5f) * 1.6f + (v - 1.02f) * (v - 1.02f) * 2.2f));
                        float edge = Mathf.Clamp01((Mathf.Max(Mathf.Abs(u - 0.5f) * 2f, Mathf.Abs(v - 0.5f) * 2f) - 0.55f) / 0.45f);
                        bool cell = ((x >> 1) + (y >> 1)) % 2 == 0;
                        Color c = Color.Lerp(new Color(0.55f, 0.08f, 0.06f), new Color(0.95f, 0.62f, 0.15f), glow * 0.35f);
                        float a = glow * 0.24f;
                        c = Color.Lerp(c, Color.black, edge);
                        a = Mathf.Max(a, edge * 0.55f);
                        if (cell) { c = Color.Lerp(c, Color.white, 0.10f); a += 0.035f; }
                        px[y * W + x] = new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
                    }
                t.SetPixels32(px); t.Apply(false, true);
                return _backTex = t;
            }
        }

        // Черта под заголовком: короткая золотая, капля крови и длинная тусклая (подпись шкалы боя).
        public static RectTransform Rule(Transform parent) {
            var rt = UIKit.Plain(parent, "Line", new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.25f));
            var g = UIKit.Plain(rt, "Gold", UIKit.Accent); UIKit.SetAnchors(g, 0f, 0f, 0.22f, 1f); g.offsetMin = new Vector2(0f, -3f); g.offsetMax = new Vector2(0f, 3f);
            var b = UIKit.Plain(rt, "Drop", UIKit.Blood); UIKit.SetAnchors(b, 0.225f, 0f, 0.245f, 1f); b.offsetMin = new Vector2(0f, -3f); b.offsetMax = new Vector2(0f, 3f);
            return rt;
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

        // Главная кнопка: золотая, чернильная надпись без обводки, тёмная губа — как плитка шкалы.
        public static Button Primary(Transform parent, string name, string caption, int fontSize) {
            var b = UIKit.Btn(parent, name, caption, fontSize, UIKit.Accent, UIKit.Ink);
            b.GetComponentInChildren<Text>().GetComponent<Outline>().enabled = false;
            var c = b.colors;
            c.disabledColor = Color.white;   // закрытое состояние красим сами
            b.colors = c;
            return b;
        }

        // Обычная кнопка: тёмная подложка, рамка кости и золотая метка слева (не серая плита).
        public static Button Secondary(Transform parent, string name, string caption, int fontSize) {
            var b = UIKit.Btn(parent, name, caption, fontSize, UIKit.BgPanel, UIKit.Bone);
            var rt = (RectTransform)b.transform;
            b.GetComponent<UIBorder>().Color = BoneFrame;
            var n = UIKit.Plain(rt, "Notch", UIKit.Accent);
            UIKit.SetAnchors(n, 0f, 0.18f, 0f, 0.82f); n.offsetMin = new Vector2(8f, 0f); n.offsetMax = new Vector2(16f, 0f);
            n.SetSiblingIndex(0);
            var c = b.colors;
            c.disabledColor = Color.white;
            b.colors = c;
            return b;
        }

        public static void SetCaption(Button b, string caption) => b.GetComponentInChildren<Text>().text = caption;

        // Подпись мельче под надписью кнопки: надпись уходит в верхние 60 %.
        public static Text SubCaption(Button b, string text) {
            var main = b.GetComponentInChildren<Text>();
            UIKit.SetAnchors(main.rectTransform, 0f, 0.36f, 1f, 1f);
            var t = UIKit.Label(b.transform, "Sub", UIKit.FontSm, UIKit.TextSub, semiBold: false);
            UIKit.SetAnchors(t.rectTransform, 0.05f, 0.08f, 0.95f, 0.44f);
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            t.text = text;
            return t;
        }

        static readonly Color BoneFrame = new Color(0.93f, 0.89f, 0.78f, 0.45f);

        // Обычная кнопка, ставшая главной (и обратно): метка и светлая рамка только у обычной.
        public static void SetSecondaryLook(Button b, bool secondary) {
            var n = b.transform.Find("Notch"); if (n != null) n.gameObject.SetActive(secondary);
            b.GetComponent<UIBorder>().Color = secondary ? BoneFrame : new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.95f);
        }

        // Метка обычной кнопки своим цветом (раздел: боец — синий, магазин — золото, настройки — кость).
        public static void SetNotch(Button b, Color c) {
            var n = b.transform.Find("Notch"); if (n != null) n.GetComponent<Image>().color = c;
        }

        // Кнопка-действие: доступна (оранжевая) или закрыта (серая с серой надписью).
        public static void SetActionState(Button b, bool enabled) {
            b.interactable = enabled;
            b.GetComponent<Image>().color = enabled ? UIKit.Accent : UIKit.BgPanelLt;
            var lbl = b.GetComponentInChildren<Text>();
            lbl.color = enabled ? UIKit.Ink : UIKit.TextSub;
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
