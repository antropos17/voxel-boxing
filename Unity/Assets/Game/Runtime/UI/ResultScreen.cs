// ResultScreen.cs — экран итогов боя: победа/поражение, статистика, кнопки действий.
// В общем потоке игры его видно после поражения: «ЕЩЁ РАЗ» и «В МЕНЮ» (победа ведёт сразу к награде).
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class ResultScreen : MonoBehaviour, ILayoutHandler {

        // ------------------------------------------------------------------ Элементы

        // Иерархия: исход боя (печать на плашке) → цифры (три плитки) → награда (золотая полоса) → действие.
        Text _headerLbl;
        RectTransform _line, _plate, _rewardBar;
        Text _timeLbl, _kdLbl, _accLbl, _rewardLbl;
        readonly RectTransform[] _tiles = new RectTransform[3];

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

            // Тёмный фон с фактурой, шире safe-area
            MenuKit.Backdrop(transform, UIKit.BgDark);

            // Декоративная горизонтальная линия под заголовком
            _line = MenuKit.Rule(transform);

            // Заголовок «ПОБЕДА!» / «ПОРАЖЕНИЕ» — печать на плашке, чуть наискось
            _plate = UIKit.Plain(transform, "Plate", UIKit.Ink);
            UIKit.Frame(_plate, UIKit.Accent, 4f);
            UIKit.Strip(_plate, "Hi", new Color(1f, 1f, 1f, 0.10f), 0f, 1f, 8f);
            _plate.localRotation = Quaternion.Euler(0f, 0f, 2.5f);
            _headerLbl = UIKit.Label(_plate, "Header", UIKit.FontLg + 34,
                                     UIKit.Accent, semiBold: true);
            MenuKit.Place(_headerLbl.rectTransform, 0f, 0f, 1f, 1f);
            _headerLbl.text = "ПОБЕДА!";

            // Цифры боя — плитки: крупное значение, подпись под ним
            _timeLbl = Tile(0, "ВРЕМЯ", "—");
            _kdLbl   = Tile(1, "НОКДАУНЫ", "0");
            _accLbl  = Tile(2, "ТОЧНОСТЬ", "0%");
            // награда — золотая полоса
            _rewardBar = UIKit.Plain(transform, "RewardBar", new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.16f));
            UIKit.Frame(_rewardBar, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.6f), 2f);
            _rewardLbl = UIKit.Label(_rewardBar, "StatRew", UIKit.FontMd, UIKit.Accent, semiBold: true);
            MenuKit.Place(_rewardLbl.rectTransform, 0.03f, 0f, 0.97f, 1f);
            _rewardLbl.text = "Награда: 0";

            // --- Кнопки ---

            // ДАЛЕЕ (основная — видна только при победе)
            // кнопки — как в остальных экранах меню (MenuKit): главная оранжевая крупным кеглем, прочие с рамкой
            _nextBtn = MenuKit.Primary(transform, "BtnNext", "ДАЛЕЕ", UIKit.FontLg);
            var nextRT = _nextBtn.GetComponent<RectTransform>();
            UIKit.SetAnchors(nextRT, 0.10f, 0.195f, 0.90f, 0.308f);
            nextRT.offsetMin = nextRT.offsetMax = Vector2.zero;
            _nextBtn.onClick.AddListener(() => OnNext?.Invoke());

            // ЕЩЁ РАЗ
            _rematchBtn = MenuKit.Secondary(transform, "BtnRematch", "ЕЩЁ РАЗ", UIKit.FontMd);
            var remRT = _rematchBtn.GetComponent<RectTransform>();
            UIKit.SetAnchors(remRT, 0.10f, 0.093f, 0.90f, 0.188f);
            remRT.offsetMin = remRT.offsetMax = Vector2.zero;
            _rematchBtn.onClick.AddListener(() => OnRematch?.Invoke());

            // В МЕНЮ
            _menuBtn = MenuKit.Secondary(transform, "BtnMenu", "В МЕНЮ", UIKit.FontMd);
            var menuRT = _menuBtn.GetComponent<RectTransform>();
            UIKit.SetAnchors(menuRT, 0.25f, 0.015f, 0.75f, 0.085f);
            menuRT.offsetMin = menuRT.offsetMax = Vector2.zero;
            _menuBtn.onClick.AddListener(() => OnMenu?.Invoke());

            OnLayout(false);
        }

        Text Tile(int i, string caption, string value) {
            var t = UIKit.Panel(transform, "Tile" + i, UIKit.BgPanel);
            _tiles[i] = t;
            var v = UIKit.Label(t, "Value", 60, UIKit.TextMain, semiBold: true);
            MenuKit.Place(v.rectTransform, 0f, 0.34f, 1f, 0.92f);
            v.text = value;
            var c = UIKit.Label(t, "Cap", UIKit.FontSm, UIKit.TextSub, semiBold: false);
            MenuKit.Place(c.rectTransform, 0f, 0.06f, 1f, 0.36f);
            c.text = caption;
            return v;
        }

        // ------------------------------------------------------------------ Показ результата

        // victory   — победа или поражение
        // time      — секунды боя
        // knockdowns — нокдауны соперника
        // accuracy  — 0..1 (отношение попавших ударов к нанесённым)
        // reward    — монеты
        public void Show(bool victory, float time, int knockdowns, float accuracy, int reward) {
            _headerLbl.text  = victory ? "ПОБЕДА!" : "ПОРАЖЕНИЕ";
            // победа — золото на чернилах в золотой рамке; поражение — кость на крови
            _headerLbl.color = victory ? UIKit.Accent : UIKit.TextMain;
            _plate.GetComponent<Image>().color = victory ? UIKit.Ink : UIKit.Blood;
            _plate.GetComponent<UIBorder>().Color = victory ? UIKit.Accent : UIKit.Ink;

            int tMin = (int)(time / 60f);
            int tSec = (int)(time % 60f);
            _timeLbl.text   = tMin + ":" + (tSec < 10 ? "0" : "") + tSec;
            // цифры итогов набегают от нуля (UIMotion.Count; без анимации — сразу конечная строка)
            UIMotion.Count(_kdLbl, 0, knockdowns, "", "", UIMotion.CountUp, 0.10f);
            UIMotion.Count(_accLbl, 0, Mathf.RoundToInt(accuracy * 100f), "", "%", UIMotion.CountUp, 0.16f);
            UIMotion.Count(_rewardLbl, 0, reward, "Награда: ", " " + MenuKit.Plural(reward, "монета", "монеты", "монет"), UIMotion.CountUp, 0.22f);
            UIMotion.Stamp(_plate, 0.04f);

            // Кнопка «ДАЛЕЕ» доступна только при победе
            _nextBtn.gameObject.SetActive(victory);
            _rematchBtn.gameObject.SetActive(true);
            _menuBtn.gameObject.SetActive(true);
            // после поражения главная кнопка — «ЕЩЁ РАЗ»: оранжевая
            _rematchBtn.GetComponent<Image>().color = victory ? UIKit.BgPanel : UIKit.Accent;
            var rl = _rematchBtn.GetComponentInChildren<Text>();
            rl.color = victory ? UIKit.TextMain : UIKit.BgDark;
            rl.fontSize = victory ? UIKit.FontMd : UIKit.FontLg;
            rl.GetComponent<Outline>().enabled = victory;
            MenuKit.SetSecondaryLook(_rematchBtn, victory);
        }

        // Строка награды по начислению GameFlow (утешительный приз и опыт).
        public void SetRewardLine(string text) { UIMotion.Complete(_rewardLbl); _rewardLbl.text = text; }

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
                Place(_plate,                   0.10f, 0.760f, 0.90f, 0.960f);
                Place(_line,                    0.05f, 0.715f, 0.95f, 0.722f);
                Place(_tiles[0],                0.03f, 0.330f, 0.18f, 0.660f);
                Place(_tiles[1],                0.195f, 0.330f, 0.345f, 0.660f);
                Place(_tiles[2],                0.36f, 0.330f, 0.51f, 0.660f);
                Place(_rewardBar,               0.03f, 0.120f, 0.51f, 0.280f);
                Place(_nextBtn.GetComponent<RectTransform>(),    0.56f, 0.490f, 0.94f, 0.690f);
                Place(_rematchBtn.GetComponent<RectTransform>(), 0.56f, 0.275f, 0.94f, 0.465f);
                Place(_menuBtn.GetComponent<RectTransform>(),    0.56f, 0.070f, 0.94f, 0.250f);
            } else {
                Place(_plate,                   0.06f, 0.770f, 0.94f, 0.900f);
                Place(_line,                    0.05f, 0.718f, 0.95f, 0.721f);
                Place(_tiles[0],                0.04f, 0.520f, 0.33f, 0.680f);
                Place(_tiles[1],                0.355f, 0.520f, 0.645f, 0.680f);
                Place(_tiles[2],                0.67f, 0.520f, 0.96f, 0.680f);
                Place(_rewardBar,               0.04f, 0.380f, 0.96f, 0.480f);
                Place(_nextBtn.GetComponent<RectTransform>(),    0.05f, 0.225f, 0.95f, 0.330f);
                Place(_rematchBtn.GetComponent<RectTransform>(), 0.05f, 0.115f, 0.95f, 0.215f);
                Place(_menuBtn.GetComponent<RectTransform>(),    0.05f, 0.012f, 0.95f, 0.105f);
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
