// ModeSelectScreen.cs — выбор режима: карточки ЛЕСТНИЦА / БЫСТРЫЙ БОЙ / ТРЕНИРОВКА / ИСПЫТАНИЕ, у каждой описание и строка прогресса.
// Портрет — столбец карточек, ландшафт — сетка 2×2.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class ModeSelectScreen : MonoBehaviour, ILayoutHandler {

        public Action OnLadder, OnQuick, OnTraining, OnTrial, OnBack;

        class Card {
            public Button btn;
            public RectTransform rt, stripe;
            public Text title, desc, info;
        }

        Text _header;
        RectTransform _line;
        Card _ladder, _quick, _training, _trial;
        Button _backBtn;

        public void Init(UIRoot root, Action onLadder, Action onQuick, Action onTraining, Action onTrial, Action onBack) {
            OnLadder = onLadder; OnQuick = onQuick; OnTraining = onTraining; OnTrial = onTrial; OnBack = onBack;
            BuildUI();
            root.Register(ScreenId.ModeSelect, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);
            MenuKit.Backdrop(transform, new Color(UIKit.BgDark.r, UIKit.BgDark.g, UIKit.BgDark.b, 0.86f));

            _header = UIKit.Label(transform, "Header", UIKit.FontXl, UIKit.Accent, semiBold: true, TextAnchor.MiddleLeft);
            _header.text = "РЕЖИМ";
            _line = UIKit.Panel(transform, "Line", UIKit.AccentDim);

            _ladder   = MakeCard("CardLadder", "ЛЕСТНИЦА", PressLadder);
            _quick    = MakeCard("CardQuick", "БЫСТРЫЙ БОЙ", PressQuick);
            _training = MakeCard("CardTraining", "ТРЕНИРОВКА", PressTraining);
            _trial    = MakeCard("CardTrial", "ИСПЫТАНИЕ", PressTrial);

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
            _ladder.desc.text = RosterDef.Count + " соперников по порядку. Пройди всех — получи пояс.";
            _ladder.info.text = beaten >= RosterDef.Count
                ? "Лестница пройдена · поясов: " + p.beltCount
                : "Побеждено " + beaten + " из " + RosterDef.Count + " · поясов: " + p.beltCount;

            _quick.desc.text = "Случайный соперник по твоей силе. Без лестницы, просто бой.";
            _quick.info.text = "Уровень соперника: " + Mathf.Clamp(p.ladderIndex + 1, 1, RosterDef.Count);

            _training.desc.text = "Слабый соперник почти не бьёт. Отработай удары и уклоны.";
            _training.info.text = "Не идёт в счёт побед · +" + MenuKit.Coins(GameFlow.TrainingCoins);

            _trial.desc.text = TrialRules();
            _trial.info.text = "За прохождение +" + MenuKit.Coins(GameFlow.TrialBonus);
        }

        public static string TrialRules() {
            var m = new ModeManager();
            m.StartMode(GameMode.Trial);
            int fights = m.TrialFightsLeft;
            int mult = Mathf.Max(1, m.ComputeReward(null, GameMode.Trial) / Mathf.Max(1, m.ComputeReward(null, GameMode.Career)));
            string s = fights + " " + MenuKit.Plural(fights, "бой", "боя", "боёв") + " подряд, монеты ×" + mult;
            s += m.MegaAllowed ? "." : ", мега-удар запрещён.";
            s += " Между боями здоровье +" + Mathf.RoundToInt(m.GetHpRestore(GameMode.Trial)) + ".";
            return s;
        }

        public string CareerInfo => _ladder.info.text;
        public string TrialText => _trial.desc.text;

        // ------------------------------------------------------------------ Нажатия

        public void PressLadder()   => OnLadder?.Invoke();
        public void PressQuick()    => OnQuick?.Invoke();
        public void PressTraining() => OnTraining?.Invoke();
        public void PressTrial()    => OnTrial?.Invoke();
        public void PressBack()     => OnBack?.Invoke();

        // ------------------------------------------------------------------ Раскладка

        public void OnLayout(bool landscape) => Layout(landscape);

        public void Layout(bool landscape) {
            if (landscape) {
                MenuKit.Place(_header.rectTransform, 0.03f, 0.885f, 0.97f, 0.995f);
                MenuKit.Place(_line,                 0.03f, 0.876f, 0.97f, 0.881f);
                PlaceCard(_ladder,   0.02f, 0.550f, 0.49f, 0.860f, true);
                PlaceCard(_quick,    0.51f, 0.550f, 0.98f, 0.860f, true);
                PlaceCard(_training, 0.02f, 0.230f, 0.49f, 0.535f, true);
                PlaceCard(_trial,    0.51f, 0.230f, 0.98f, 0.535f, true);
                MenuKit.Place(_backBtn.GetComponent<RectTransform>(), 0.02f, 0.02f, 0.24f, 0.21f);
            } else {
                MenuKit.Place(_header.rectTransform, 0.05f, 0.935f, 0.95f, 0.998f);
                MenuKit.Place(_line,                 0.05f, 0.929f, 0.95f, 0.933f);
                PlaceCard(_ladder,   0.03f, 0.730f, 0.97f, 0.915f, false);
                PlaceCard(_quick,    0.03f, 0.534f, 0.97f, 0.719f, false);
                PlaceCard(_training, 0.03f, 0.338f, 0.97f, 0.523f, false);
                PlaceCard(_trial,    0.03f, 0.142f, 0.97f, 0.327f, false);
                MenuKit.Place(_backBtn.GetComponent<RectTransform>(), 0.03f, 0.012f, 0.40f, 0.125f);
            }
        }

        static void PlaceCard(Card c, float x0, float y0, float x1, float y1, bool landscape) {
            MenuKit.Place(c.rt, x0, y0, x1, y1);
            if (landscape) {
                Inset(c.title.rectTransform, 0.70f, 0.97f);
                Inset(c.desc.rectTransform,  0.27f, 0.70f);
                Inset(c.info.rectTransform,  0.04f, 0.27f);
            } else {
                Inset(c.title.rectTransform, 0.66f, 0.96f);
                Inset(c.desc.rectTransform,  0.26f, 0.66f);
                Inset(c.info.rectTransform,  0.04f, 0.26f);
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
