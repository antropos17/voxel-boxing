// ResultScreen.cs — экран итогов боя: победа/поражение, статистика, кнопки действий.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class ResultScreen : MonoBehaviour, ILayoutHandler {

        // ------------------------------------------------------------------ Элементы

        Text _headerLbl;
        Text _timeLbl, _kdLbl, _accLbl, _rewardLbl;

        Button _nextBtn, _rematchBtn, _menuBtn;

        // ------------------------------------------------------------------ Публичные колбэки

        public Action OnNext;
        public Action OnRematch;
        public Action OnMenu;

        // ------------------------------------------------------------------ Инициализация

        public void Init(UIRoot root) {
            root.Register(ScreenId.Result, gameObject);
            BuildUI();
        }

        void BuildUI() {
            // Убедиться что есть RectTransform (аналогично FightHUD)
            var selfRt = gameObject.GetComponent<RectTransform>();
            if (selfRt == null) selfRt = gameObject.AddComponent<RectTransform>();
            UIKit.SetAnchors(selfRt, 0f, 0f, 1f, 1f);
            selfRt.offsetMin = selfRt.offsetMax = Vector2.zero;

            // Тёмный фон-панель
            var bg = UIKit.Panel(transform, "Bg", UIKit.BgDark);
            UIKit.SetAnchors(bg, 0f, 0f, 1f, 1f);
            bg.offsetMin = bg.offsetMax = Vector2.zero;

            // Декоративная горизонтальная линия под заголовком
            var line = UIKit.Panel(transform, "Line", UIKit.AccentDim);
            UIKit.SetAnchors(line, 0.05f, 0.69f, 0.95f, 0.695f);
            line.offsetMin = line.offsetMax = Vector2.zero;

            // Заголовок «ПОБЕДА!» / «ПОРАЖЕНИЕ»
            _headerLbl = UIKit.Label(transform, "Header", UIKit.FontLg + 20,
                                     UIKit.Accent, semiBold: true);
            UIKit.SetAnchors(_headerLbl.rectTransform, 0.05f, 0.71f, 0.95f, 0.92f);
            _headerLbl.text = "ПОБЕДА!";

            // Строки статистики — равномерно между 0.36 и 0.69
            _timeLbl   = StatLine("StatTime",   "Время: —",       0.605f, 0.675f);
            _kdLbl     = StatLine("StatKD",     "Нокдауны: 0",    0.525f, 0.600f);
            _accLbl    = StatLine("StatAcc",    "Точность: 0%",   0.445f, 0.520f);
            _rewardLbl = StatLine("StatRew",    "Награда: 0",     0.365f, 0.440f);

            // --- Кнопки ---

            // ДАЛЕЕ (основная — видна только при победе)
            _nextBtn = UIKit.Btn(transform, "BtnNext", "ДАЛЕЕ",
                UIKit.FontMd, UIKit.Accent, UIKit.BgDark);
            var nextRT = _nextBtn.GetComponent<RectTransform>();
            UIKit.SetAnchors(nextRT, 0.10f, 0.195f, 0.90f, 0.308f);
            nextRT.offsetMin = nextRT.offsetMax = Vector2.zero;
            _nextBtn.onClick.AddListener(() => OnNext?.Invoke());

            // РЕВАНШ
            _rematchBtn = UIKit.Btn(transform, "BtnRematch", "РЕВАНШ",
                UIKit.FontMd, UIKit.BgPanel, UIKit.TextMain);
            var remRT = _rematchBtn.GetComponent<RectTransform>();
            UIKit.SetAnchors(remRT, 0.10f, 0.093f, 0.90f, 0.188f);
            remRT.offsetMin = remRT.offsetMax = Vector2.zero;
            _rematchBtn.onClick.AddListener(() => OnRematch?.Invoke());

            // В МЕНЮ
            _menuBtn = UIKit.Btn(transform, "BtnMenu", "В МЕНЮ",
                UIKit.FontSm, UIKit.BgPanelLt, UIKit.TextSub);
            var menuRT = _menuBtn.GetComponent<RectTransform>();
            UIKit.SetAnchors(menuRT, 0.25f, 0.015f, 0.75f, 0.085f);
            menuRT.offsetMin = menuRT.offsetMax = Vector2.zero;
            _menuBtn.onClick.AddListener(() => OnMenu?.Invoke());
        }

        Text StatLine(string name, string defaultText, float yMin, float yMax) {
            var lbl = UIKit.Label(transform, name, UIKit.FontMd, UIKit.TextMain,
                                  semiBold: false, TextAnchor.MiddleCenter);
            UIKit.SetAnchors(lbl.rectTransform, 0.05f, yMin, 0.95f, yMax);
            lbl.text = defaultText;
            return lbl;
        }

        // ------------------------------------------------------------------ Показ результата

        // victory   — победа или поражение
        // time      — секунды боя
        // knockdowns — нокдауны соперника
        // accuracy  — 0..1 (отношение попавших ударов к нанесённым)
        // reward    — монеты
        public void Show(bool victory, float time, int knockdowns, float accuracy, int reward) {
            _headerLbl.text  = victory ? "ПОБЕДА!" : "ПОРАЖЕНИЕ";
            _headerLbl.color = victory ? UIKit.Accent : UIKit.HpRed;

            int tMin = (int)(time / 60f);
            int tSec = (int)(time % 60f);
            _timeLbl.text   = "Время: " + tMin + ":" + (tSec < 10 ? "0" : "") + tSec;
            _kdLbl.text     = "Нокдауны: " + knockdowns;
            _accLbl.text    = "Точность: " + Mathf.RoundToInt(accuracy * 100f) + "%";
            _rewardLbl.text = "Награда: " + reward + " монет";

            // Кнопка «ДАЛЕЕ» доступна только при победе
            _nextBtn.gameObject.SetActive(victory);
            _rematchBtn.gameObject.SetActive(true);
            _menuBtn.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------ Программные нажатия (GameWalk)

        public void PressNext()    => OnNext?.Invoke();
        public void PressRematch() => OnRematch?.Invoke();
        public void PressMenu()    => OnMenu?.Invoke();

        // ------------------------------------------------------------------ ILayoutHandler

        public void OnLayout(bool landscape) {
            if (!landscape) return;
            // В ландшафте safe-area высота ~519px; каждая кнопка должна быть ≥96px (≥0.185 from height).
            var nextRT  = _nextBtn.GetComponent<RectTransform>();
            var remRT   = _rematchBtn.GetComponent<RectTransform>();
            var menuRT  = _menuBtn.GetComponent<RectTransform>();
            UIKit.SetAnchors(nextRT,  0.10f, 0.60f, 0.90f, 0.80f); // height=0.20×519≈104px
            UIKit.SetAnchors(remRT,   0.10f, 0.38f, 0.90f, 0.57f); // height=0.19×519≈99px
            UIKit.SetAnchors(menuRT,  0.25f, 0.15f, 0.75f, 0.33f); // height=0.18×519≈93px
            nextRT.offsetMin  = nextRT.offsetMax  = Vector2.zero;
            remRT.offsetMin   = remRT.offsetMax   = Vector2.zero;
            menuRT.offsetMin  = menuRT.offsetMax  = Vector2.zero;
        }

        // ------------------------------------------------------------------ Tick (на будущее)

        public void Tick(float dt, float udt) {
            // Нет состояния, зависящего от времени.
        }
    }
}
