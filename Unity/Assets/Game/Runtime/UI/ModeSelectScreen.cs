// ModeSelectScreen.cs — выбор режима: карточки КАРЬЕРА / БЫСТРЫЙ БОЙ / ИСПЫТАНИЕ, у каждой описание и строка прогресса.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class ModeSelectScreen : MonoBehaviour, ILayoutHandler {

        public Action OnCareer, OnQuick, OnTrial, OnBack;

        class Card {
            public Button btn;
            public RectTransform rt, stripe;
            public Text title, desc, info;
        }

        Text _header;
        RectTransform _line;
        Card _career, _quick, _trial;
        Button _backBtn;

        public void Init(UIRoot root, Action onCareer, Action onQuick, Action onTrial, Action onBack) {
            OnCareer = onCareer; OnQuick = onQuick; OnTrial = onTrial; OnBack = onBack;
            BuildUI();
            root.Register(ScreenId.ModeSelect, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);
            MenuKit.Backdrop(transform, new Color(UIKit.BgDark.r, UIKit.BgDark.g, UIKit.BgDark.b, 0.86f));

            _header = UIKit.Label(transform, "Header", UIKit.FontXl, UIKit.Accent, semiBold: true, TextAnchor.MiddleLeft);
            _header.text = "РЕЖИМ";
            _line = UIKit.Panel(transform, "Line", UIKit.AccentDim);

            _career = MakeCard("CardCareer", "КАРЬЕРА", PressCareer);
            _quick  = MakeCard("CardQuick", "БЫСТРЫЙ БОЙ", PressQuick);
            _trial  = MakeCard("CardTrial", "ИСПЫТАНИЕ", PressTrial);

            _backBtn = MenuKit.Secondary(transform, "BtnBack", "НАЗАД", UIKit.FontMd);
            _backBtn.onClick.AddListener(PressBack);

            SetData(new PlayerProfile());
            Layout(false);
        }

        Card MakeCard(string name, string title, UnityEngine.Events.UnityAction onTap) {
            var c = new Card();
            c.btn = UIKit.Btn(transform, name, "", UIKit.FontSm, UIKit.BgPanel, UIKit.TextMain);
            c.rt = c.btn.GetComponent<RectTransform>();
            var frame = c.btn.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.55f);
            frame.effectDistance = new Vector2(2f, -2f);
            c.btn.onClick.AddListener(onTap);
            // подпись кнопки из UIKit не нужна — у карточки свои надписи
            c.btn.GetComponentInChildren<Text>().gameObject.SetActive(false);

            c.stripe = UIKit.Panel(c.rt, "Stripe", UIKit.Accent);
            UIKit.SetAnchors(c.stripe, 0f, 0f, 0f, 1f);
            c.stripe.pivot = new Vector2(0f, 0.5f);
            c.stripe.sizeDelta = new Vector2(8f, 0f);
            c.stripe.anchoredPosition = Vector2.zero;

            c.title = UIKit.Label(c.rt, "Title", UIKit.FontLg, UIKit.Accent, semiBold: true, TextAnchor.MiddleLeft);
            c.title.text = title;
            c.desc = UIKit.Label(c.rt, "Desc", UIKit.FontSm, UIKit.TextMain, semiBold: false, TextAnchor.UpperLeft);
            c.info = UIKit.Label(c.rt, "Info", UIKit.FontXs + 2, UIKit.MegaGold, semiBold: true, TextAnchor.MiddleLeft);
            return c;
        }

        // Тексты карточек: прогресс игрока и правила испытания из ModeManager.
        public void SetData(PlayerProfile p) {
            int beaten = Mathf.Min(p.ladderIndex, RosterDef.Count);
            _career.desc.text = "Лестница из " + RosterDef.Count + " соперников. Пройди всех — получи пояс.";
            _career.info.text = beaten >= RosterDef.Count
                ? "Лестница пройдена · поясов: " + p.beltCount
                : "Побеждено " + beaten + " из " + RosterDef.Count + " · поясов: " + p.beltCount;

            _quick.desc.text = "Случайный соперник по твоей силе. Без лестницы, просто бой.";
            _quick.info.text = "Уровень соперника: " + Mathf.Clamp(p.ladderIndex + 1, 1, RosterDef.Count);

            _trial.desc.text = TrialRules();
            _trial.info.text = "За прохождение +" + GameFlow.TrialBonus + " монет";
        }

        public static string TrialRules() {
            var m = new ModeManager();
            m.StartMode(GameMode.Trial);
            int fights = m.TrialFightsLeft;
            int mult = Mathf.Max(1, m.ComputeReward(null, GameMode.Trial) / Mathf.Max(1, m.ComputeReward(null, GameMode.Career)));
            string s = fights + " " + MenuKit.Plural(fights, "бой", "боя", "боёв") + " подряд, монеты ×" + mult;
            s += m.MegaAllowed ? "." : ", мега-удар запрещён.";
            return s;
        }

        public string CareerInfo => _career.info.text;
        public string TrialText => _trial.desc.text;

        // ------------------------------------------------------------------ Нажатия

        public void PressCareer() => OnCareer?.Invoke();
        public void PressQuick()  => OnQuick?.Invoke();
        public void PressTrial()  => OnTrial?.Invoke();
        public void PressBack()   => OnBack?.Invoke();

        // ------------------------------------------------------------------ Раскладка

        public void OnLayout(bool landscape) => Layout(landscape);

        public void Layout(bool landscape) {
            if (landscape) {
                MenuKit.Place(_header.rectTransform, 0.03f, 0.885f, 0.97f, 0.995f);
                MenuKit.Place(_line,                 0.03f, 0.876f, 0.97f, 0.881f);
                PlaceCard(_career, 0.020f, 0.24f, 0.325f, 0.86f, true);
                PlaceCard(_quick,  0.347f, 0.24f, 0.653f, 0.86f, true);
                PlaceCard(_trial,  0.675f, 0.24f, 0.980f, 0.86f, true);
                MenuKit.Place(_backBtn.GetComponent<RectTransform>(), 0.02f, 0.02f, 0.24f, 0.21f);
            } else {
                MenuKit.Place(_header.rectTransform, 0.05f, 0.935f, 0.95f, 0.998f);
                MenuKit.Place(_line,                 0.05f, 0.929f, 0.95f, 0.933f);
                PlaceCard(_career, 0.03f, 0.665f, 0.97f, 0.905f, false);
                PlaceCard(_quick,  0.03f, 0.410f, 0.97f, 0.650f, false);
                PlaceCard(_trial,  0.03f, 0.155f, 0.97f, 0.395f, false);
                MenuKit.Place(_backBtn.GetComponent<RectTransform>(), 0.03f, 0.012f, 0.40f, 0.125f);
            }
        }

        static void PlaceCard(Card c, float x0, float y0, float x1, float y1, bool landscape) {
            MenuKit.Place(c.rt, x0, y0, x1, y1);
            if (landscape) {
                Inset(c.title.rectTransform, 0.78f, 0.97f);
                Inset(c.desc.rectTransform,  0.24f, 0.76f);
                Inset(c.info.rectTransform,  0.03f, 0.22f);
            } else {
                Inset(c.title.rectTransform, 0.66f, 0.95f);
                Inset(c.desc.rectTransform,  0.24f, 0.64f);
                Inset(c.info.rectTransform,  0.04f, 0.24f);
            }
        }

        // По вертикали — доли карточки, по горизонтали — отступ от полосы слева и от правого края.
        static void Inset(RectTransform rt, float y0, float y1) {
            UIKit.SetAnchors(rt, 0f, y0, 1f, y1);
            rt.offsetMin = new Vector2(24f, 0f);
            rt.offsetMax = new Vector2(-14f, 0f);
        }

        public void Tick(float dt, float udt) { }
    }
}
