// RewardScreen.cs — награда после итогов: монеты с разбивкой, опыт, новый уровень, пояс, следующий соперник.
// Главная кнопка: победа — ДАЛЕЕ (следующий соперник или меню), поражение — ЕЩЁ РАЗ. Ниже В МАГАЗИН и В МЕНЮ.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class RewardScreen : MonoBehaviour, ILayoutHandler {

        public const ScreenId Id = ScreenId.Reward;
        public const int MaxLines = 6;

        public Action OnNext, OnShop, OnRematch, OnMenu;

        Text _header, _total, _xp, _levelUp, _extra, _next;
        RectTransform _line, _xpBg, _xpFill, _linesBox;
        readonly Text[] _lineL = new Text[MaxLines], _lineR = new Text[MaxLines];
        Button _mainBtn, _shopBtn, _menuBtn;
        bool _won = true, _land;
        int _n = MaxLines;   // строк разбивки: рамка под них, без пустого хвоста
        float _pulse;

        public string TotalText => _totalFinal;   // конечная строка (на экране монеты ещё могут набегать)
        string _totalFinal = "";
        public string NextFoeText => _next.text;
        public string MainCaption => _mainBtn.GetComponentInChildren<Text>().text;
        public bool Won => _won;

        public void Init(UIRoot root, Action onNext, Action onShop, Action onRematch, Action onMenu) {
            OnNext = onNext; OnShop = onShop; OnRematch = onRematch; OnMenu = onMenu;
            BuildUI();
            root.Register(Id, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);
            MenuKit.Backdrop(transform, UIKit.BgDark);

            _header = UIKit.Label(transform, "Header", UIKit.FontXl, UIKit.Accent, semiBold: true, TextAnchor.MiddleLeft);
            _line = UIKit.Panel(transform, "Line", UIKit.AccentDim);
            _total = UIKit.Label(transform, "Total", 58, UIKit.MegaGold, semiBold: true);
            var sh = _total.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(3f, -3f);

            _linesBox = UIKit.Panel(transform, "Lines", UIKit.BgPanel);
            for (int i = 0; i < MaxLines; i++) {
                _lineL[i] = UIKit.Label(_linesBox, "L" + i, UIKit.FontSm, UIKit.TextMain, semiBold: false, TextAnchor.MiddleLeft);
                _lineR[i] = UIKit.Label(_linesBox, "R" + i, UIKit.FontSm, UIKit.MegaGold, semiBold: true, TextAnchor.MiddleRight);
            }

            _xp = UIKit.Label(transform, "Xp", UIKit.FontMd, UIKit.TextMain, semiBold: true, TextAnchor.MiddleLeft);
            (_xpBg, _xpFill) = UIKit.Bar(transform, "XpBar", new Color32(40, 34, 30, 220), UIKit.Accent);
            _levelUp = UIKit.Label(transform, "LevelUp", UIKit.FontLg, UIKit.Accent, semiBold: true);
            _extra = UIKit.Label(transform, "Extra", UIKit.FontMd, UIKit.MegaGold, semiBold: true);
            _next = UIKit.Label(transform, "Next", UIKit.FontSm, UIKit.TextMain, semiBold: false);

            _mainBtn = MenuKit.Primary(transform, "BtnMain", "ДАЛЕЕ", UIKit.FontLg);
            _mainBtn.onClick.AddListener(PressMain);
            _shopBtn = MenuKit.Secondary(transform, "BtnShop", "В МАГАЗИН", UIKit.FontMd);
            _shopBtn.onClick.AddListener(PressShop);
            _menuBtn = MenuKit.Secondary(transform, "BtnMenu", "В МЕНЮ", UIKit.FontMd);
            _menuBtn.onClick.AddListener(PressMenu);

            Layout(false);
        }

        // ------------------------------------------------------------------ Данные

        public void Show(RewardInfo r, PlayerProfile p) {
            if (r == null) return;
            _won = r.won;
            _header.text = r.won ? "НАГРАДА" : "УТЕШИТЕЛЬНЫЙ ПРИЗ";
            _header.color = r.won ? UIKit.Accent : UIKit.TextMain;
            _totalFinal = "+" + MenuKit.Coins(r.coins);
            UIMotion.Count(_total, 0, r.coins, "+", " " + MenuKit.Plural(r.coins, "монета", "монеты", "монет"), UIMotion.CountUp, 0.08f);
            UIMotion.Stamp(_total.rectTransform, 0.04f);

            int n = Mathf.Min(MaxLines, r.lines.Count);
            for (int i = 0; i < MaxLines; i++) {
                bool on = i < n;
                _lineL[i].gameObject.SetActive(on);
                _lineR[i].gameObject.SetActive(on);
                if (!on) continue;
                _lineL[i].text = r.lines[i].label;
                int v = r.lines[i].value;
                if (v >= 0) UIMotion.Count(_lineR[i], 0, v, "+", "", UIMotion.CountUp, 0.14f + i * UIMotion.StaggerStep);
                else { UIMotion.Complete(_lineR[i]); _lineR[i].text = "−" + (-v); }
            }

            bool max = p.level >= PlayerProfile.MaxPlayerLevel;
            _xp.text = "Опыт +" + r.xp + (max ? "" : "   (" + p.xp + " / " + PlayerProfile.XpForNextLevel(p.level) + ")");
            float xpK = max ? 1f : (float)p.xp / Mathf.Max(1, PlayerProfile.XpForNextLevel(p.level));
            UIKit.SetBarH(_xpFill, xpK);
            UIMotion.Bar(_xpFill, 0f, Mathf.Clamp01(xpK), UIMotion.CountUp, 0.2f);
            _levelUp.gameObject.SetActive(r.levelsGained > 0);
            _levelUp.text = "НОВЫЙ УРОВЕНЬ: " + r.newLevel + "!";
            if (r.levelsGained > 0) UIMotion.Stamp(_levelUp.rectTransform, 0.3f);

            string extra = "";
            if (r.belt) extra = "ПОЯС ЛЕСТНИЦЫ — ТВОЙ!";
            else if (r.mode == GameMode.Trial && r.trialDone) extra = "ИСПЫТАНИЕ ПРОЙДЕНО!";
            else if (r.mode == GameMode.Trial && r.won) extra = "Испытание: бой " + r.trialFight + " из " + r.trialTotal + " позади";
            else if (r.mode == GameMode.Trial) extra = "Испытание прервано";
            else if (r.mode == GameMode.Training) extra = "Тренировка окончена";
            else if (r.ladderAdvanced) extra = "Ступень " + r.ladderIndex + " из " + RosterDef.Count + " взята";
            _extra.text = extra;
            _extra.gameObject.SetActive(extra.Length > 0);

            if (r.mode == GameMode.Career)
                _next.text = string.IsNullOrEmpty(r.nextFoe) ? "Лестница пройдена" : "Следующий соперник: " + r.nextFoe;
            else
                _next.text = "";
            _next.gameObject.SetActive(_next.text.Length > 0);

            MenuKit.SetCaption(_mainBtn, r.won ? MainCaptionFor(r) : "ЕЩЁ РАЗ");
            _n = Mathf.Max(1, n);
            Layout(_land);
        }

        // Куда ведёт «дальше»: следующий бой лестницы или испытания, иначе в меню.
        static string MainCaptionFor(RewardInfo r) {
            if (r.mode == GameMode.Career && !string.IsNullOrEmpty(r.nextFoe)) return "К ЛЕСТНИЦЕ";
            if (r.mode == GameMode.Trial && !r.trialDone) return "СЛЕДУЮЩИЙ БОЙ";
            return "ДАЛЕЕ";
        }

        // ------------------------------------------------------------------ Нажатия

        // Главная кнопка: при победе — дальше, при поражении — реванш.
        public void PressMain() {
            if (_won) OnNext?.Invoke(); else OnRematch?.Invoke();
        }

        public void PressNext()    => OnNext?.Invoke();
        public void PressMenu()    => OnMenu?.Invoke();
        public void PressShop()    => OnShop?.Invoke();
        public void PressRematch() => OnRematch?.Invoke();

        // ------------------------------------------------------------------ Раскладка

        public void OnLayout(bool landscape) => Layout(landscape);

        public void Layout(bool landscape) {
            _land = landscape;
            var main = _mainBtn.GetComponent<RectTransform>();
            var shop = _shopBtn.GetComponent<RectTransform>();
            var menu = _menuBtn.GetComponent<RectTransform>();
            // строки разбивки делят рамку поровну; рамка ровно под их число
            for (int i = 0; i < MaxLines; i++) {
                float y1 = 1f - (i + 0.5f) / (_n + 1f), y0 = y1 - 1f / (_n + 1f);
                UIKit.SetAnchors(_lineL[i].rectTransform, 0f, y0, 0.74f, y1);
                _lineL[i].rectTransform.offsetMin = new Vector2(16f, 0f); _lineL[i].rectTransform.offsetMax = Vector2.zero;
                UIKit.SetAnchors(_lineR[i].rectTransform, 0.74f, y0, 1f, y1);
                _lineR[i].rectTransform.offsetMin = Vector2.zero; _lineR[i].rectTransform.offsetMax = new Vector2(-16f, 0f);
            }
            float y;
            if (landscape) {
                MenuKit.Place(_header.rectTransform, 0.02f, 0.865f, 0.50f, 0.995f);
                MenuKit.Place(_line,                 0.02f, 0.855f, 0.98f, 0.860f);
                // левая колонка: сумма и разбивка по центру высоты
                float box = 0.09f * (_n + 1f), stack = 0.145f + 0.02f + box;
                y = 0.85f - Mathf.Max(0f, 0.82f - stack) * 0.5f;
                MenuKit.Place(_total.rectTransform,  0.02f, y - 0.145f, 0.50f, y); y -= 0.165f;
                MenuKit.Place(_linesBox,             0.02f, y - box, 0.50f, y);
                MenuKit.Place(_xp.rectTransform,     0.53f, 0.770f, 0.98f, 0.850f);
                MenuKit.Place(_xpBg,                 0.53f, 0.735f, 0.98f, 0.765f);
                MenuKit.Place(_levelUp.rectTransform, 0.53f, 0.640f, 0.98f, 0.725f);
                MenuKit.Place(_extra.rectTransform,  0.53f, 0.565f, 0.98f, 0.640f);
                MenuKit.Place(_next.rectTransform,   0.53f, 0.450f, 0.98f, 0.565f);
                MenuKit.Place(main, 0.53f, 0.235f, 0.98f, 0.440f);
                MenuKit.Place(shop, 0.53f, 0.020f, 0.75f, 0.215f);
                MenuKit.Place(menu, 0.76f, 0.020f, 0.98f, 0.215f);
            } else {
                MenuKit.Place(_header.rectTransform, 0.05f, 0.920f, 0.95f, 0.995f);
                MenuKit.Place(_line,                 0.05f, 0.912f, 0.95f, 0.916f);
                MenuKit.Place(_total.rectTransform,  0.03f, 0.825f, 0.97f, 0.905f);
                // от разбивки до строки соперника — блок по центру между суммой и кнопками
                float box = 0.045f * (_n + 1f);
                float stack = box + 0.02f + 0.045f + 0.02f + 0.02f
                              + (_levelUp.gameObject.activeSelf ? 0.06f : 0f) + (_extra.gameObject.activeSelf ? 0.055f : 0f)
                              + (_next.gameObject.activeSelf ? 0.07f : 0f);
                y = 0.815f - Mathf.Max(0f, 0.535f - stack) * 0.5f;
                MenuKit.Place(_linesBox,             0.03f, y - box, 0.97f, y); y -= box + 0.02f;
                MenuKit.Place(_xp.rectTransform,     0.05f, y - 0.045f, 0.95f, y); y -= 0.045f;
                MenuKit.Place(_xpBg,                 0.05f, y - 0.02f, 0.95f, y); y -= 0.04f;
                if (_levelUp.gameObject.activeSelf) { MenuKit.Place(_levelUp.rectTransform, 0.03f, y - 0.06f, 0.97f, y); y -= 0.06f; }
                if (_extra.gameObject.activeSelf) { MenuKit.Place(_extra.rectTransform, 0.03f, y - 0.055f, 0.97f, y); y -= 0.055f; }
                MenuKit.Place(_next.rectTransform,   0.03f, y - 0.07f, 0.97f, y);
                MenuKit.Place(main, 0.05f, 0.140f, 0.95f, 0.260f);
                MenuKit.Place(shop, 0.05f, 0.012f, 0.49f, 0.128f);
                MenuKit.Place(menu, 0.51f, 0.012f, 0.95f, 0.128f);
            }
        }

        // Пульс надписи нового уровня.
        public void Tick(float dt, float udt) {
            if (!_levelUp.gameObject.activeInHierarchy) return;
            _pulse += udt;
            if (_pulse > 1000f) _pulse -= 1000f;
            float k = 1f + 0.06f * Mathf.Sin(_pulse * 5f);
            _levelUp.rectTransform.localScale = new Vector3(k, k, 1f);
        }
    }
}
