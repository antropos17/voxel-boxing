// FightHUD.cs — HUD боя.
// Не имеет Update(). Работает через Tick(dt,udt) + Refresh(HudData) из FightDirector.
// Не зависит от FightState — принимает только HudData.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class FightHUD : MonoBehaviour, ILayoutHandler {

        // ------------------------------------------------------------------ Элементы

        RectTransform _foeHpBg,  _foeHpFill;
        RectTransform _plrHpBg,  _plrHpFill;
        RectTransform _stamBg,   _stamFill;
        RectTransform _megaBg,   _megaFill;

        Text _foeNameLbl, _plrNameLbl;
        Text _megaReadyLbl;
        Text _timerLbl;
        Text _kdLbl;
        Text _telegraphL, _telegraphR;
        Image _telBgL, _telBgR;   // плашки подсказки замаха (в них «!»)
        Text _countLbl;    // крупный счёт рефери
        Text _bannerLbl;   // центральный баннер
        RawImage _vignette;
        RectTransform _hintBg;   // подсказка жестов на тёмной подложке
        Text _hintLbl;
        float _hintTimer;
        Image _blockImg;
        bool _cacheBlock;
        static Texture2D s_vigTex;   // радиальная виньетка: одна на всё время работы (не копится между боями)

        Button _blockBtn, _dodgeL, _dodgeR, _pauseBtn;

        // ------------------------------------------------------------------ Публичные события

        public Action OnBlock;
        public Action OnDodgeLeft;
        public Action OnDodgeRight;
        public Action OnPause;

        // ------------------------------------------------------------------ Кэш (анти-аллокация)

        int   _cacheFoeHp  = -1;
        int   _cachePlrHp  = -1;
        int   _cacheStam   = -1;
        int   _cacheMega   = -1;
        int   _cacheTMin   = -1, _cacheTSec = -1;
        int   _cacheKd     = -1;
        bool  _cacheMegaRdy;

        // Готовые строки таймера «M:SS» (0:00…9:59), счёта рефери и нокдаунов: в кадре боя строки не создаются.
        // Серию показывает FxBanners («СЕРИЯ ×N») — в HUD её нет, иначе она дублировалась и садилась на рефери.
        const int TimerMax = 600, CountMax = 10, KdMax = 9;
        static readonly string[] TimerStr = BuildTimer(), CountStr = BuildList("", CountMax), KdStr = BuildList("KD×", KdMax);
        static string[] BuildTimer() {
            var a = new string[TimerMax];
            for (int i = 0; i < TimerMax; i++) a[i] = (i / 60) + ":" + (i % 60 / 10) + (i % 10);
            return a;
        }
        static string[] BuildList(string prefix, int max) {
            var a = new string[max + 1];
            for (int i = 0; i <= max; i++) a[i] = prefix + i;
            return a;
        }

        // ------------------------------------------------------------------ Таймеры баннеров

        float _bannerTimer;
        float _countTimer;

        // ------------------------------------------------------------------ Инициализация

        // Вызывается FightDirector после AddComponent<FightHUD>.
        public void Init(UIRoot root) {
            BuildUI();
            // регистрация после сборки: UIRoot сразу раскладывает экран под текущую ориентацию
            root.Register(ScreenId.Fight, gameObject);
        }

        void BuildUI() {
            var rt = gameObject.GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            UIKit.SetAnchors(rt, 0f, 0f, 1f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            // Красная виньетка при низком HP: первая — под полосами и кнопками, центр прозрачный
            if (s_vigTex == null) { s_vigTex = FxScreen.MakeVignette(); s_vigTex.hideFlags = HideFlags.DontSave; }
            var vg = new GameObject("Vignette", typeof(RectTransform), typeof(RawImage));
            vg.transform.SetParent(transform, false);
            _vignette = vg.GetComponent<RawImage>();
            _vignette.texture = s_vigTex;
            _vignette.color = new Color(0.75f, 0f, 0f, 0f); // начально невидима
            _vignette.raycastTarget = false;
            Place(vg.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);

            // Полосы: соперник, игрок, выносливость, тонкая мега-шкала
            (_foeHpBg, _foeHpFill) = UIKit.Bar(transform, "FoeHp", new Color32(45, 18, 18, 200), UIKit.HpRed);
            _foeNameLbl = UIKit.Label(_foeHpBg, "FoeName", UIKit.FontSm, UIKit.TextMain,
                                      semiBold: true, TextAnchor.MiddleLeft);
            Stretch(_foeNameLbl.rectTransform, 8f, 0f, -4f, 0f);

            (_plrHpBg, _plrHpFill) = UIKit.Bar(transform, "PlrHp", new Color32(18, 45, 18, 200), UIKit.HpGreen);
            _plrNameLbl = UIKit.Label(_plrHpBg, "PlrName", UIKit.FontXs, UIKit.TextMain,
                                      semiBold: true, TextAnchor.MiddleRight);
            Stretch(_plrNameLbl.rectTransform, 4f, 0f, -8f, 0f);

            (_stamBg, _stamFill) = UIKit.Bar(transform, "Stam", new Color32(18, 18, 55, 180), UIKit.StamBlue);
            (_megaBg, _megaFill) = UIKit.Bar(transform, "Mega", new Color32(50, 30, 10, 200), UIKit.MegaGold);

            _megaReadyLbl = UIKit.Label(transform, "MegaRdy", UIKit.FontSm, UIKit.MegaGold, semiBold: true);
            _megaReadyLbl.text = "МЕГА ГОТОВ · свайп!";
            _megaReadyLbl.gameObject.SetActive(false);

            _timerLbl = UIKit.Label(transform, "Timer", UIKit.FontMd, UIKit.TextMain, semiBold: true);
            _timerLbl.text = "0:00";

            _kdLbl = UIKit.Label(transform, "KD", UIKit.FontSm, UIKit.Accent, semiBold: true);
            _kdLbl.text = "";

            // Подсказка замаха соперника: красная плашка с крупным «!» с той стороны, откуда идёт удар
            _telegraphL = TelegraphBadge("TelL", out _telBgL);
            _telegraphR = TelegraphBadge("TelR", out _telBgR);

            // Подсказка жестов: тёмная подложка, белый текст; над кнопками, гаснет сама
            _hintBg = UIKit.Panel(transform, "Hint", new Color(0.05f, 0.04f, 0.07f, 0.78f));
            _hintLbl = UIKit.Label(_hintBg, "HintText", UIKit.FontSm, UIKit.TextMain, semiBold: true);
            Stretch(_hintLbl.rectTransform, 10f, 6f, -10f, -6f);
            _hintBg.gameObject.SetActive(false);

            // Кнопки управления на плотной тёмной подложке со светлой рамкой: читаются поверх трусов, перчаток и ринга
            _blockBtn = ControlBtn("BtnBlock", "БЛОК", 30, UIKit.Accent);
            _blockBtn.onClick.AddListener(() => OnBlock?.Invoke());
            _blockImg = _blockBtn.GetComponent<Image>();
            _dodgeL = ControlBtn("BtnDgL", "\u25C4", 44, UIKit.TextMain);
            _dodgeL.onClick.AddListener(() => OnDodgeLeft?.Invoke());
            _dodgeR = ControlBtn("BtnDgR", "\u25BA", 44, UIKit.TextMain);
            _dodgeR.onClick.AddListener(() => OnDodgeRight?.Invoke());
            _pauseBtn = UIKit.Btn(transform, "BtnPause", "II",
                UIKit.FontSm, new Color32(22, 24, 44, 110), UIKit.TextMain);
            _pauseBtn.onClick.AddListener(() => OnPause?.Invoke());

            // Крупная цифра отсчёта рефери
            _countLbl = UIKit.Label(transform, "Count", 132, UIKit.TextMain, semiBold: true);
            _countLbl.gameObject.SetActive(false);

            // Баннер «БОЙ!»: главный сигнал старта — крупно, золотом, с тёмной обводкой и тенью (читается на коже)
            _bannerLbl = UIKit.Label(transform, "Banner", BannerFont, UIKit.MegaGold, semiBold: true);
            var bo = _bannerLbl.GetComponent<Outline>();
            bo.effectColor = new Color(0f, 0f, 0f, 0.95f);
            bo.effectDistance = new Vector2(3f, -3f);
            var bs = _bannerLbl.gameObject.AddComponent<Shadow>();
            bs.effectColor = new Color(0.25f, 0f, 0f, 0.75f);
            bs.effectDistance = new Vector2(3f, -6f);
            _bannerLbl.gameObject.SetActive(false);

            OnLayout(false);
        }

        Button ControlBtn(string name, string caption, int fontSize, Color textColor) {
            var b = UIKit.Btn(transform, name, caption, fontSize, new Color32(16, 14, 22, 235), textColor);
            var frame = b.gameObject.AddComponent<Outline>();          // рамка по краю подложки
            frame.effectColor    = new Color(1f, 1f, 1f, 0.42f);
            frame.effectDistance = new Vector2(2f, -2f);
            var lbl = b.GetComponentInChildren<Text>();
            var o = lbl.GetComponent<Outline>();
            o.effectColor    = new Color(0f, 0f, 0f, 0.9f);
            o.effectDistance = new Vector2(2f, -2f);
            return b;
        }

        Text TelegraphBadge(string name, out Image bg) {
            var rt = UIKit.Panel(transform, name, new Color(0.86f, 0.12f, 0.12f, 0.5f));
            bg = rt.GetComponent<Image>();
            var t = UIKit.Label(rt, "Mark", 92, UIKit.TextMain, semiBold: true);
            Stretch(t.rectTransform, 0f, 0f, 0f, 0f);
            t.text = "!";
            rt.gameObject.SetActive(false);
            return t;
        }

        // Якоря в долях экрана, без отступов.
        static void Place(RectTransform rt, float x0, float y0, float x1, float y1) {
            UIKit.SetAnchors(rt, x0, y0, x1, y1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void Place(Component c, float x0, float y0, float x1, float y1) =>
            Place(c.GetComponent<RectTransform>(), x0, y0, x1, y1);

        static void Stretch(RectTransform rt, float l, float b, float r, float t) {
            UIKit.SetAnchors(rt, 0f, 0f, 1f, 1f);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(r, t);
        }

        // ------------------------------------------------------------------ Обновление

        // Вызывается FightDirector каждый кадр. Строки не аллоцируются, если значения не изменились.
        public void Refresh(HudData d) {

            // HP соперника (полоса убывает слева)
            UIKit.SetBarH(_foeHpFill, d.foeHpMax > 0f ? d.foeHp / d.foeHpMax : 0f);

            // HP игрока
            float plrRatio = d.playerHpMax > 0f ? d.playerHp / d.playerHpMax : 1f;
            UIKit.SetBarH(_plrHpFill, plrRatio);

            // Выносливость
            UIKit.SetBarH(_stamFill, d.playerStamina * 0.01f);

            // Мега-шкала (горизонтальная)
            UIKit.SetBarH(_megaFill, d.megaMeter * 0.01f);

            // «МЕГА ГОТОВ»
            if (d.megaReady != _cacheMegaRdy) {
                _cacheMegaRdy = d.megaReady;
                _megaReadyLbl.gameObject.SetActive(d.megaReady && _megaOn);
            }

            // Имена (не меняются, но защищаем от повторной записи)
            if (_foeNameLbl.text != d.foeName) _foeNameLbl.text = d.foeName ?? "";
            if (_plrNameLbl.text != d.playerName) _plrNameLbl.text = d.playerName ?? "";

            // Нокдауны
            if (d.foeKnockdowns != _cacheKd) {
                _cacheKd = d.foeKnockdowns;
                _kdLbl.text = d.foeKnockdowns > 0 ? KdStr[Mathf.Min(d.foeKnockdowns, KdMax)] : "";
            }

            // Блок включён (кнопка-переключатель или удержание низа): кнопка подсвечена
            if (d.playerBlocking != _cacheBlock) {
                _cacheBlock = d.playerBlocking;
                _blockImg.color = d.playerBlocking ? BlockOnColor : BlockOffColor;
            }

            // Таймер M:SS (только при смене секунды)
            int tMin = (int)(d.fightTime / 60f);
            int tSec = (int)(d.fightTime % 60f);
            if (tMin != _cacheTMin || tSec != _cacheTSec) {
                _cacheTMin = tMin;
                _cacheTSec = tSec;
                WriteTimer(tMin, tSec);
            }

            // Телеграф: плашка «!» при замахе соперника; даже слабая подсказка стиля читается (не прозрачнее 0.55)
            bool telActive = d.foePhase == FightPhase.Windup && d.telegraphAlpha > 0.05f;
            bool showL = telActive && d.foeAttackSide < 0, showR = telActive && d.foeAttackSide > 0;
            if (_telBgL.gameObject.activeSelf != showL) _telBgL.gameObject.SetActive(showL);
            if (_telBgR.gameObject.activeSelf != showR) _telBgR.gameObject.SetActive(showR);
            if (telActive) {
                float a = Mathf.Max(0.55f, d.telegraphAlpha);
                var bc = new Color(0.86f, 0.12f, 0.12f, 0.8f * a);
                var tc = new Color(1f, 1f, 1f, a);
                if (showL) { _telBgL.color = bc; _telegraphL.color = tc; }
                if (showR) { _telBgR.color = bc; _telegraphR.color = tc; }
            }

            // Виньетка по краям: появляется при hp < 55%
            float vign = plrRatio < 0.55f ? (0.55f - plrRatio) / 0.55f * 0.85f : 0f;
            _vignette.color = new Color(0.75f, 0f, 0f, vign);
        }

        const int BannerFont = 76;
        static readonly Color BlockOffColor = new Color32(16, 14, 22, 235);
        static readonly Color BlockOnColor  = new Color32(170, 95, 20, 245);

        // Tick обновляет таймеры баннеров (udt — реальное время).
        public void Tick(float dt, float udt) {
            if (_bannerTimer > 0f) {
                _bannerTimer -= udt;
                if (_bannerTimer <= 0f) _bannerLbl.gameObject.SetActive(false);
            }
            if (_countTimer > 0f) {
                _countTimer -= udt;
                if (_countTimer <= 0f) _countLbl.gameObject.SetActive(false);
            }
            if (_hintTimer > 0f) {
                _hintTimer -= udt;
                if (_hintTimer <= 0f) _hintBg.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ Публичные методы

        // Подсказка жестов на seconds секунд (реальное время).
        public void ShowHint(string text, float seconds) {
            _hintLbl.text = text;
            _hintBg.gameObject.SetActive(true);
            _hintTimer = seconds;
        }

        public void HideHint() {
            if (_hintTimer <= 0f) return;
            _hintTimer = 0f;
            _hintBg.gameObject.SetActive(false);
        }

        public bool HintVisible => _hintBg.gameObject.activeSelf;

        // Мега-шкала и «МЕГА ГОТОВ» скрыты в режиме без мега-удара (испытание).
        public void SetMegaVisible(bool on) {
            _megaBg.gameObject.SetActive(on);
            if (!on) { _megaReadyLbl.gameObject.SetActive(false); _cacheMegaRdy = false; }
            _megaOn = on;
        }
        bool _megaOn = true;

        // «Заново» из паузы: погасить счёт рефери, баннер и подсказку, сбросить кэши.
        public void ResetTransient() {
            _countTimer = _bannerTimer = _hintTimer = 0f;
            _countLbl.gameObject.SetActive(false);
            _bannerLbl.gameObject.SetActive(false);
            _hintBg.gameObject.SetActive(false);
            _cacheKd = -1; _cacheMegaRdy = false; _megaReadyLbl.gameObject.SetActive(false);
        }

        // Показать крупный счёт рефери на 0.75 с (для нокдаун-отсчёта).
        public void ShowCount(int n) {
            _countLbl.text = CountStr[Mathf.Clamp(n, 0, CountMax)];
            _countLbl.gameObject.SetActive(true);
            _countTimer = 0.75f;
        }

        // Показать баннер по центру на seconds секунд.
        public void ShowBanner(string text, float seconds) {
            _bannerLbl.text = text;
            _bannerLbl.gameObject.SetActive(true);
            _bannerTimer = seconds;
        }

        // Программное нажатие кнопок — для GameWalk (сквозной проход).
        public void PressBlock()      => OnBlock?.Invoke();
        public void PressDodgeLeft()  => OnDodgeLeft?.Invoke();
        public void PressDodgeRight() => OnDodgeRight?.Invoke();
        public void PressPause()      => OnPause?.Invoke();

        // ------------------------------------------------------------------ Layout

        // ILayoutHandler: раскладка под ориентацию (доли safe-area). UIRoot зовёт при регистрации и при смене.
        public void OnLayout(bool landscape) {
            if (landscape) {
                // Полосы по краям сверху, середина над головой соперника свободна
                Place(_foeHpBg,  0.030f, 0.900f, 0.400f, 0.962f);
                Place(_plrHpBg,  0.585f, 0.900f, 0.900f, 0.962f);
                Place(_stamBg,   0.585f, 0.868f, 0.900f, 0.890f);
                Place(_megaBg,   0.585f, 0.838f, 0.900f, 0.860f);
                Place(_timerLbl, 0.440f, 0.895f, 0.560f, 0.985f);
                Place(_megaReadyLbl, 0.585f, 0.760f, 0.900f, 0.832f);
                _megaReadyLbl.alignment = TextAnchor.MiddleRight;
                Place(_kdLbl,    0.030f, 0.825f, 0.250f, 0.895f);
                _kdLbl.alignment = TextAnchor.MiddleLeft;
                UIKit.SetCorner(_pauseBtn.GetComponent<RectTransform>(), 1f, 1f, 1f, 1f, 92f, 92f, -8f, -8f);
                Place(_telBgL, 0.150f, 0.440f, 0.230f, 0.660f);
                Place(_telBgR, 0.770f, 0.440f, 0.850f, 0.660f);
                Place(_hintBg,     0.190f, 0.035f, 0.810f, 0.255f);
                Place(_countLbl,   0.350f, 0.440f, 0.650f, 0.880f);
                Place(_bannerLbl,  0.200f, 0.280f, 0.800f, 0.440f);
                // Кнопки под большими пальцами по углам, середина низа свободна; «БЛОК» над «◄»,
                // чтобы перчатки игрока (от ~0.25 ширины) их не перекрывали. Высота ≥ 100 опорных px.
                Place(_dodgeL,   0.008f, 0.030f, 0.122f, 0.270f);
                Place(_blockBtn, 0.008f, 0.295f, 0.122f, 0.500f);
                Place(_dodgeR,   0.878f, 0.030f, 0.992f, 0.270f);
            } else {
                Place(_timerLbl, 0.360f, 0.957f, 0.640f, 0.998f);
                Place(_foeHpBg,  0.035f, 0.922f, 0.800f, 0.953f);
                Place(_plrHpBg,  0.035f, 0.886f, 0.800f, 0.917f);
                Place(_stamBg,   0.035f, 0.869f, 0.800f, 0.881f);
                Place(_megaBg,   0.035f, 0.852f, 0.800f, 0.864f);
                Place(_megaReadyLbl, 0.035f, 0.812f, 0.600f, 0.848f);
                _megaReadyLbl.alignment = TextAnchor.MiddleLeft;
                Place(_kdLbl,    0.600f, 0.812f, 0.800f, 0.848f);
                _kdLbl.alignment = TextAnchor.MiddleRight;
                UIKit.SetCorner(_pauseBtn.GetComponent<RectTransform>(), 1f, 1f, 1f, 1f, 92f, 92f, -8f, -8f);
                Place(_telBgL, 0.025f, 0.560f, 0.185f, 0.660f);
                Place(_telBgR, 0.815f, 0.560f, 0.975f, 0.660f);
                Place(_hintBg,     0.030f, 0.125f, 0.970f, 0.235f);
                Place(_countLbl,   0.200f, 0.560f, 0.800f, 0.800f);
                Place(_bannerLbl,  0.040f, 0.390f, 0.960f, 0.470f);
                // кнопки раздельно по низу (высота ~105 опорных px): между ними видно соперника
                Place(_dodgeL,   0.025f, 0.012f, 0.235f, 0.108f);
                Place(_blockBtn, 0.330f, 0.012f, 0.670f, 0.108f);
                Place(_dodgeR,   0.765f, 0.012f, 0.975f, 0.108f);
            }
        }

        // ------------------------------------------------------------------ Вспомогательное

        // Формат M:SS из готовых строк — без аллокаций.
        void WriteTimer(int m, int s) {
            _timerLbl.text = TimerStr[Mathf.Clamp(m * 60 + s, 0, TimerMax - 1)];
        }
    }
}
