// RewardScreen.cs — награда после итогов: монеты с разбивкой, опыт, новый уровень, пояс, следующий соперник.
// Победа: ДАЛЕЕ / В МАГАЗИН. Поражение: РЕВАНШ / В МАГАЗИН / ДАЛЕЕ.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class RewardScreen : MonoBehaviour, ILayoutHandler {

        // Своего значения в ScreenId нет (UIRoot.cs чужой): номер вне перечня, словарь UIRoot его принимает.
        public const ScreenId Id = (ScreenId)100;
        public const int MaxLines = 6;

        public Action OnNext, OnShop, OnRematch;

        Text _header, _total, _xp, _levelUp, _extra, _next;
        RectTransform _line, _xpBg, _xpFill, _linesBox;
        readonly Text[] _lineL = new Text[MaxLines], _lineR = new Text[MaxLines];
        Button _mainBtn, _shopBtn, _nextBtn;
        bool _won = true, _landscape;
        float _pulse;

        public string TotalText => _total.text;
        public string NextFoeText => _next.text;
        public bool Won => _won;

        public void Init(UIRoot root, Action onNext, Action onShop, Action onRematch) {
            OnNext = onNext; OnShop = onShop; OnRematch = onRematch;
            BuildUI();
            root.Register(Id, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);
            MenuKit.Backdrop(transform, UIKit.BgDark);

            _header = UIKit.Label(transform, "Header", UIKit.FontXl, UIKit.Accent, semiBold: true);
            _line = UIKit.Panel(transform, "Line", UIKit.AccentDim);
            _total = UIKit.Label(transform, "Total", 58, UIKit.MegaGold, semiBold: true);
            var sh = _total.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(3f, -3f);

            _linesBox = UIKit.Panel(transform, "Lines", UIKit.BgPanel);
            for (int i = 0; i < MaxLines; i++) {
                _lineL[i] = UIKit.Label(_linesBox, "L" + i, UIKit.FontSm, UIKit.TextMain, semiBold: false, TextAnchor.MiddleLeft);
                _lineR[i] = UIKit.Label(_linesBox, "R" + i, UIKit.FontSm, UIKit.MegaGold, semiBold: true, TextAnchor.MiddleRight);
                float y1 = 1f - 0.04f - i * (0.92f / MaxLines), y0 = y1 - 0.92f / MaxLines;
                UIKit.SetAnchors(_lineL[i].rectTransform, 0f, y0, 0.74f, y1);
                _lineL[i].rectTransform.offsetMin = new Vector2(16f, 0f); _lineL[i].rectTransform.offsetMax = Vector2.zero;
                UIKit.SetAnchors(_lineR[i].rectTransform, 0.74f, y0, 1f, y1);
                _lineR[i].rectTransform.offsetMin = Vector2.zero; _lineR[i].rectTransform.offsetMax = new Vector2(-16f, 0f);
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
            _nextBtn = MenuKit.Secondary(transform, "BtnNext", "ДАЛЕЕ", UIKit.FontMd);
            _nextBtn.onClick.AddListener(PressNext);

            Layout(false);
        }

        // ------------------------------------------------------------------ Данные

        public void Show(RewardInfo r, PlayerProfile p) {
            if (r == null) return;
            _won = r.won;
            _header.text = r.won ? "НАГРАДА" : "УТЕШИТЕЛЬНЫЙ ПРИЗ";
            _header.color = r.won ? UIKit.Accent : UIKit.TextMain;
            _total.text = "+" + MenuKit.Coins(r.coins);

            int n = Mathf.Min(MaxLines, r.lines.Count);
            for (int i = 0; i < MaxLines; i++) {
                bool on = i < n;
                _lineL[i].gameObject.SetActive(on);
                _lineR[i].gameObject.SetActive(on);
                if (!on) continue;
                _lineL[i].text = r.lines[i].label;
                _lineR[i].text = "+" + r.lines[i].value;
            }

            bool max = p.level >= PlayerProfile.MaxPlayerLevel;
            _xp.text = "Опыт +" + r.xp + (max ? "" : "   (" + p.xp + " / " + PlayerProfile.XpForNextLevel(p.level) + ")");
            UIKit.SetBarH(_xpFill, max ? 1f : (float)p.xp / Mathf.Max(1, PlayerProfile.XpForNextLevel(p.level)));
            _levelUp.gameObject.SetActive(r.levelsGained > 0);
            _levelUp.text = "НОВЫЙ УРОВЕНЬ: " + r.newLevel + "!";

            string extra = "";
            if (r.belt) extra = "ПОЯС ЛЕСТНИЦЫ — ТВОЙ!";
            else if (r.mode == GameMode.Trial && r.trialDone) extra = "ИСПЫТАНИЕ ПРОЙДЕНО!";
            else if (r.mode == GameMode.Trial && r.won) extra = "Испытание: бой " + r.trialFight + " из " + r.trialTotal + " позади";
            else if (r.mode == GameMode.Trial) extra = "Испытание прервано";
            else if (r.ladderAdvanced) extra = "Ступень " + r.ladderIndex + " из " + RosterDef.Count + " взята";
            _extra.text = extra;
            _extra.gameObject.SetActive(extra.Length > 0);

            if (r.mode == GameMode.Career)
                _next.text = string.IsNullOrEmpty(r.nextFoe) ? "Лестница пройдена" : "Следующий соперник: " + r.nextFoe;
            else
                _next.text = "";
            _next.gameObject.SetActive(_next.text.Length > 0);

            MenuKit.SetCaption(_mainBtn, r.won ? "ДАЛЕЕ" : "РЕВАНШ");
            _nextBtn.gameObject.SetActive(!r.won);
            Layout(_landscape);   // раскладка нижних кнопок зависит от их числа
        }

        // ------------------------------------------------------------------ Нажатия

        // Главная кнопка: при победе — дальше, при поражении — реванш.
        public void PressMain() {
            if (_won) OnNext?.Invoke(); else OnRematch?.Invoke();
        }

        public void PressNext()    => OnNext?.Invoke();
        public void PressShop()    => OnShop?.Invoke();
        public void PressRematch() => OnRematch?.Invoke();

        // ------------------------------------------------------------------ Раскладка

        public void OnLayout(bool landscape) => Layout(landscape);

        public void Layout(bool landscape) {
            _landscape = landscape;
            var main = _mainBtn.GetComponent<RectTransform>();
            var shop = _shopBtn.GetComponent<RectTransform>();
            var next = _nextBtn.GetComponent<RectTransform>();
            if (landscape) {
                MenuKit.Place(_header.rectTransform, 0.02f, 0.865f, 0.50f, 0.995f);
                MenuKit.Place(_line,                 0.02f, 0.855f, 0.98f, 0.860f);
                MenuKit.Place(_total.rectTransform,  0.02f, 0.705f, 0.50f, 0.850f);
                MenuKit.Place(_linesBox,             0.02f, 0.030f, 0.50f, 0.690f);
                MenuKit.Place(_xp.rectTransform,     0.53f, 0.770f, 0.98f, 0.850f);
                MenuKit.Place(_xpBg,                 0.53f, 0.735f, 0.98f, 0.765f);
                MenuKit.Place(_levelUp.rectTransform, 0.53f, 0.640f, 0.98f, 0.725f);
                MenuKit.Place(_extra.rectTransform,  0.53f, 0.565f, 0.98f, 0.640f);
                MenuKit.Place(_next.rectTransform,   0.53f, 0.450f, 0.98f, 0.565f);
                MenuKit.Place(main, 0.53f, 0.235f, 0.98f, 0.440f);
                MenuKit.Place(shop, 0.53f, 0.020f, 0.75f, 0.215f);
                MenuKit.Place(next, 0.76f, 0.020f, 0.98f, 0.215f);
            } else {
                MenuKit.Place(_header.rectTransform, 0.03f, 0.920f, 0.97f, 0.995f);
                MenuKit.Place(_line,                 0.05f, 0.912f, 0.95f, 0.916f);
                MenuKit.Place(_total.rectTransform,  0.03f, 0.825f, 0.97f, 0.905f);
                MenuKit.Place(_linesBox,             0.03f, 0.545f, 0.97f, 0.815f);
                MenuKit.Place(_xp.rectTransform,     0.05f, 0.495f, 0.95f, 0.540f);
                MenuKit.Place(_xpBg,                 0.05f, 0.470f, 0.95f, 0.490f);
                MenuKit.Place(_levelUp.rectTransform, 0.03f, 0.410f, 0.97f, 0.465f);
                MenuKit.Place(_extra.rectTransform,  0.03f, 0.360f, 0.97f, 0.410f);
                MenuKit.Place(_next.rectTransform,   0.03f, 0.270f, 0.97f, 0.355f);
                MenuKit.Place(main, 0.05f, 0.140f, 0.95f, 0.260f);
                if (_nextBtn.gameObject.activeSelf) {
                    MenuKit.Place(shop, 0.05f, 0.012f, 0.49f, 0.128f);
                    MenuKit.Place(next, 0.51f, 0.012f, 0.95f, 0.128f);
                } else {
                    MenuKit.Place(shop, 0.05f, 0.012f, 0.95f, 0.128f);
                }
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
