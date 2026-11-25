// ResultScreen.cs — экран итогов боя: победа/поражение, статистика, кнопки действий.
// В общем потоке игры его видно после поражения: «ЕЩЁ РАЗ» и «В МЕНЮ» (победа ведёт сразу к награде).
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class ResultScreen : MonoBehaviour, ILayoutHandler {

        // ------------------------------------------------------------------ Элементы

        Text _headerLbl;
        RectTransform _line;
        Text _timeLbl, _kdLbl, _accLbl, _rewardLbl;

        Button _nextBtn, _rematchBtn, _menuBtn;

        // ------------------------------------------------------------------ Публичные колбэки

        public Action OnNext;
        public Action OnRematch;
        public Action OnMenu;

        // ------------------------------------------------------------------ Инициализация

        public void Init(UIRoot root) {
            BuildUI();
            root.Register(ScreenId.Result, gameObject);
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
            // фон шире safe-area: закрывает экран до краёв, под вырезом и полосой жестов
            bg.offsetMin = new Vector2(-400f, -400f);
            bg.offsetMax = new Vector2(400f, 400f);

            // Декоративная горизонтальная линия под заголовком
            _line = UIKit.Panel(transform, "Line", UIKit.AccentDim);

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
            // тёмная надпись на светлой кнопке: тёмная обводка её только мажет
            _nextBtn.GetComponentInChildren<Outline>().enabled = false;

            // ЕЩЁ РАЗ
            _rematchBtn = UIKit.Btn(transform, "BtnRematch", "ЕЩЁ РАЗ",
                UIKit.FontMd, UIKit.BgPanel, UIKit.TextMain);
            var remRT = _rematchBtn.GetComponent<RectTransform>();
            UIKit.SetAnchors(remRT, 0.10f, 0.093f, 0.90f, 0.188f);
            remRT.offsetMin = remRT.offsetMax = Vector2.zero;
            _rematchBtn.onClick.AddListener(() => OnRematch?.Invoke());

            // В МЕНЮ
            _menuBtn = UIKit.Btn(transform, "BtnMenu", "В МЕНЮ",
                UIKit.FontMd, UIKit.BgPanelLt, UIKit.TextMain);
            var menuRT = _menuBtn.GetComponent<RectTransform>();
            UIKit.SetAnchors(menuRT, 0.25f, 0.015f, 0.75f, 0.085f);
            menuRT.offsetMin = menuRT.offsetMax = Vector2.zero;
            _menuBtn.onClick.AddListener(() => OnMenu?.Invoke());

            OnLayout(false);
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
            // после поражения главная кнопка — «ЕЩЁ РАЗ»: оранжевая
            _rematchBtn.GetComponent<Image>().color = victory ? UIKit.BgPanel : UIKit.Accent;
            var rl = _rematchBtn.GetComponentInChildren<Text>();
            rl.color = victory ? UIKit.TextMain : UIKit.BgDark;
            rl.GetComponent<Outline>().enabled = victory;
        }

        // Строка награды по начислению GameFlow (утешительный приз и опыт).
        public void SetRewardLine(string text) => _rewardLbl.text = text;

        public string HeaderText => _headerLbl.text;
        public string RewardText => _rewardLbl.text;

        // ------------------------------------------------------------------ Программные нажатия (GameWalk)

        public void PressNext()    => OnNext?.Invoke();
        public void PressRematch() => OnRematch?.Invoke();
        public void PressMenu()    => OnMenu?.Invoke();

        // ------------------------------------------------------------------ ILayoutHandler

        public void OnLayout(bool landscape) {
            if (landscape) {
                // Ландшафт: заголовок сверху, статистика слева, кнопки справа (каждая не ниже ~96 px)
                Place(_headerLbl.rectTransform, 0.05f, 0.745f, 0.95f, 0.965f);
                Place(_line,                    0.05f, 0.725f, 0.95f, 0.735f);
                Place(_timeLbl.rectTransform,   0.05f, 0.560f, 0.52f, 0.690f);
                Place(_kdLbl.rectTransform,     0.05f, 0.420f, 0.52f, 0.550f);
                Place(_accLbl.rectTransform,    0.05f, 0.280f, 0.52f, 0.410f);
                Place(_rewardLbl.rectTransform, 0.05f, 0.140f, 0.52f, 0.270f);
                Place(_nextBtn.GetComponent<RectTransform>(),    0.56f, 0.490f, 0.94f, 0.690f);
                Place(_rematchBtn.GetComponent<RectTransform>(), 0.56f, 0.275f, 0.94f, 0.465f);
                Place(_menuBtn.GetComponent<RectTransform>(),    0.56f, 0.070f, 0.94f, 0.250f);
            } else {
                Place(_headerLbl.rectTransform, 0.05f, 0.710f, 0.95f, 0.920f);
                Place(_line,                    0.05f, 0.690f, 0.95f, 0.695f);
                Place(_timeLbl.rectTransform,   0.05f, 0.605f, 0.95f, 0.675f);
                Place(_kdLbl.rectTransform,     0.05f, 0.525f, 0.95f, 0.600f);
                Place(_accLbl.rectTransform,    0.05f, 0.445f, 0.95f, 0.520f);
                Place(_rewardLbl.rectTransform, 0.05f, 0.365f, 0.95f, 0.440f);
                Place(_nextBtn.GetComponent<RectTransform>(),    0.10f, 0.225f, 0.90f, 0.330f);
                Place(_rematchBtn.GetComponent<RectTransform>(), 0.10f, 0.115f, 0.90f, 0.215f);
                Place(_menuBtn.GetComponent<RectTransform>(),    0.10f, 0.012f, 0.90f, 0.105f);
            }
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1) {
            UIKit.SetAnchors(rt, x0, y0, x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------ Tick (на будущее)

        public void Tick(float dt, float udt) {
            // Нет состояния, зависящего от времени.
        }
    }
}
