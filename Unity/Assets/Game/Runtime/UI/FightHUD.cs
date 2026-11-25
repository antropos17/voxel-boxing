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
        Text _comboLbl;
        Text _countLbl;    // крупный счёт рефери
        Text _bannerLbl;   // центральный баннер
        Image _vignette;

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
        int   _cacheCombo  = -1;
        bool  _cacheMegaRdy;

        static readonly char[] _timeBuf = new char[4]; // "M:SS"

        // ------------------------------------------------------------------ Таймеры баннеров

        float _bannerTimer;
        float _countTimer;

        // ------------------------------------------------------------------ Инициализация

        // Вызывается FightDirector после AddComponent<FightHUD>.
        public void Init(UIRoot root) {
            root.Register(ScreenId.Fight, gameObject);
            BuildUI();
        }

        void BuildUI() {
            var rt = gameObject.GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            UIKit.SetAnchors(rt, 0f, 0f, 1f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            BuildFoeHp();
            BuildPlayerHp();
            BuildStamina();
            BuildMega();
            BuildTimer();
            BuildKDCounter();
            BuildTelegraph();
            BuildCombo();
            BuildButtons();
            BuildCountLabel();
            BuildBanner();
            BuildVignette();
        }

        void BuildFoeHp() {
            (_foeHpBg, _foeHpFill) = UIKit.Bar(transform, "FoeHp",
                new Color32(45, 18, 18, 210), UIKit.HpRed);
            UIKit.SetAnchors(_foeHpBg, 0.06f, 0.885f, 0.73f, 0.945f);
            _foeHpBg.offsetMin = new Vector2(6f, 0f);
            _foeHpBg.offsetMax = new Vector2(-6f, 0f);

            _foeNameLbl = UIKit.Label(_foeHpBg, "FoeName", UIKit.FontSm, UIKit.TextMain,
                                      semiBold: true, TextAnchor.MiddleLeft);
            UIKit.SetAnchors(_foeNameLbl.rectTransform, 0f, 0f, 1f, 1f);
            _foeNameLbl.rectTransform.offsetMin = new Vector2(6f, 0f);
            _foeNameLbl.rectTransform.offsetMax = Vector2.zero;
        }

        void BuildPlayerHp() {
            (_plrHpBg, _plrHpFill) = UIKit.Bar(transform, "PlrHp",
                new Color32(18, 45, 18, 210), UIKit.HpGreen);
            UIKit.SetAnchors(_plrHpBg, 0.06f, 0.825f, 0.73f, 0.883f);
            _plrHpBg.offsetMin = new Vector2(6f, 0f);
            _plrHpBg.offsetMax = new Vector2(-6f, 0f);

            _plrNameLbl = UIKit.Label(_plrHpBg, "PlrName", UIKit.FontXs, UIKit.TextSub,
                                      semiBold: false, TextAnchor.MiddleRight);
            UIKit.SetAnchors(_plrNameLbl.rectTransform, 0f, 0f, 1f, 1f);
            _plrNameLbl.rectTransform.offsetMin = Vector2.zero;
            _plrNameLbl.rectTransform.offsetMax = new Vector2(-6f, 0f);
        }

        void BuildStamina() {
            (_stamBg, _stamFill) = UIKit.Bar(transform, "Stam",
                new Color32(18, 18, 55, 180), UIKit.StamBlue);
            UIKit.SetAnchors(_stamBg, 0.06f, 0.800f, 0.73f, 0.822f);
            _stamBg.offsetMin = new Vector2(6f, 0f);
            _stamBg.offsetMax = new Vector2(-6f, 0f);
        }

        void BuildMega() {
            // Вертикальная мега-шкала справа, заполнение снизу вверх.
            // xMax≤0.76 чтобы не заходить под кнопку паузы (x≈0.80–1.00, y≈0.91–1.00).
            (_megaBg, _megaFill) = UIKit.Bar(transform, "Mega",
                new Color32(50, 30, 10, 200), UIKit.MegaGold, vertical: true);
            UIKit.SetAnchors(_megaBg, 0.60f, 0.70f, 0.76f, 0.945f);
            _megaBg.offsetMin = _megaBg.offsetMax = Vector2.zero;

            _megaReadyLbl = UIKit.Label(transform, "MegaRdy", UIKit.FontSm, UIKit.MegaGold,
                                        semiBold: true);
            // x=0.42–0.76 чтобы не перекрывать кнопку паузы справа
            UIKit.SetAnchors(_megaReadyLbl.rectTransform, 0.42f, 0.90f, 0.76f, 0.975f);
            _megaReadyLbl.text = "МЕГА ГОТОВ";
            _megaReadyLbl.gameObject.SetActive(false);
        }

        void BuildTimer() {
            _timerLbl = UIKit.Label(transform, "Timer", UIKit.FontMd, UIKit.TextMain, semiBold: true);
            UIKit.SetAnchors(_timerLbl.rectTransform, 0.35f, 0.885f, 0.65f, 0.975f);
            _timerLbl.text = "0:00";
        }

        void BuildKDCounter() {
            // Слева от полосы HP игрока (x=0.00–0.25), вне мега-шкалы (x=0.60–0.76).
            // Оранжевый (#E68C28) на тёмном фоне — достаточный контраст.
            _kdLbl = UIKit.Label(transform, "KD", UIKit.FontSm, UIKit.Accent, semiBold: true);
            UIKit.SetAnchors(_kdLbl.rectTransform, 0.00f, 0.820f, 0.25f, 0.882f);
            _kdLbl.text = "";
        }

        void BuildTelegraph() {
            _telegraphL = UIKit.Label(transform, "TelL", UIKit.FontLg, UIKit.HpRed, semiBold: true);
            UIKit.SetAnchors(_telegraphL.rectTransform, 0.00f, 0.44f, 0.13f, 0.56f);
            _telegraphL.text = "!";
            _telegraphL.gameObject.SetActive(false);

            _telegraphR = UIKit.Label(transform, "TelR", UIKit.FontLg, UIKit.HpRed, semiBold: true);
            UIKit.SetAnchors(_telegraphR.rectTransform, 0.87f, 0.44f, 1.00f, 0.56f);
            _telegraphR.text = "!";
            _telegraphR.gameObject.SetActive(false);
        }

        void BuildCombo() {
            _comboLbl = UIKit.Label(transform, "Combo", UIKit.FontLg, UIKit.Accent, semiBold: true);
            UIKit.SetAnchors(_comboLbl.rectTransform, 0.28f, 0.55f, 0.72f, 0.68f);
            _comboLbl.text = "";
            _comboLbl.gameObject.SetActive(false);
        }

        void BuildButtons() {
            // --- БЛОК ---
            _blockBtn = UIKit.Btn(transform, "BtnBlock", "БЛОК",
                UIKit.FontMd, new Color32(60, 40, 10, 210), UIKit.Accent);
            UIKit.SetAnchors(_blockBtn.GetComponent<RectTransform>(), 0.30f, 0.02f, 0.70f, 0.11f);
            _blockBtn.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            _blockBtn.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            _blockBtn.onClick.AddListener(() => OnBlock?.Invoke());

            // --- Уклон влево ---
            _dodgeL = UIKit.Btn(transform, "BtnDgL", "◄",
                UIKit.FontMd, new Color32(28, 28, 50, 190), UIKit.TextSub);
            UIKit.SetAnchors(_dodgeL.GetComponent<RectTransform>(), 0.00f, 0.02f, 0.26f, 0.11f);
            _dodgeL.GetComponent<RectTransform>().offsetMin = new Vector2(8f, 0f);
            _dodgeL.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            _dodgeL.onClick.AddListener(() => OnDodgeLeft?.Invoke());

            // --- Уклон вправо ---
            _dodgeR = UIKit.Btn(transform, "BtnDgR", "►",
                UIKit.FontMd, new Color32(28, 28, 50, 190), UIKit.TextSub);
            UIKit.SetAnchors(_dodgeR.GetComponent<RectTransform>(), 0.74f, 0.02f, 1.00f, 0.11f);
            _dodgeR.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            _dodgeR.GetComponent<RectTransform>().offsetMax = new Vector2(-8f, 0f);
            _dodgeR.onClick.AddListener(() => OnDodgeRight?.Invoke());

            // --- Пауза (угол, фиксированный размер 100×100) ---
            _pauseBtn = UIKit.Btn(transform, "BtnPause", "II",
                UIKit.FontSm, new Color32(28, 24, 40, 180), UIKit.TextSub);
            UIKit.SetCorner(_pauseBtn.GetComponent<RectTransform>(),
                anchorX: 1f, anchorY: 1f, pivotX: 1f, pivotY: 1f,
                w: 100f, h: 100f, offsetX: -8f, offsetY: -8f);
            _pauseBtn.onClick.AddListener(() => OnPause?.Invoke());
        }

        void BuildCountLabel() {
            // Крупная цифра отсчёта рефери в центре.
            _countLbl = UIKit.Label(transform, "Count", 92, UIKit.TextMain, semiBold: true);
            UIKit.SetAnchors(_countLbl.rectTransform, 0.30f, 0.34f, 0.70f, 0.60f);
            _countLbl.gameObject.SetActive(false);
        }

        void BuildBanner() {
            // Баннер: «НОКДАУН», «НОКАУТ!», «МЕГА!», «РАЗБИТА!» и т.п.
            _bannerLbl = UIKit.Label(transform, "Banner", UIKit.FontLg + 10, UIKit.Accent, semiBold: true);
            UIKit.SetAnchors(_bannerLbl.rectTransform, 0.08f, 0.42f, 0.92f, 0.58f);
            _bannerLbl.gameObject.SetActive(false);
        }

        void BuildVignette() {
            // Полупрозрачный красный оверлей при низком HP игрока.
            var go = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            _vignette = go.GetComponent<Image>();
            _vignette.color = new Color(0.55f, 0f, 0f, 0f); // начально невидима
            _vignette.raycastTarget = false;
            UIKit.SetAnchors(go.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
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

            // Мега-шкала (вертикальная)
            UIKit.SetBarV(_megaFill, d.megaMeter * 0.01f);

            // «МЕГА ГОТОВ»
            if (d.megaReady != _cacheMegaRdy) {
                _cacheMegaRdy = d.megaReady;
                _megaReadyLbl.gameObject.SetActive(d.megaReady);
            }

            // Имена (не меняются, но защищаем от повторной записи)
            if (_foeNameLbl.text != d.foeName) _foeNameLbl.text = d.foeName ?? "";
            if (_plrNameLbl.text != d.playerName) _plrNameLbl.text = d.playerName ?? "";

            // Нокдауны
            if (d.foeKnockdowns != _cacheKd) {
                _cacheKd = d.foeKnockdowns;
                _kdLbl.text = d.foeKnockdowns > 0 ? "KD×" + d.foeKnockdowns : "";
            }

            // Таймер M:SS (только при смене секунды)
            int tMin = (int)(d.fightTime / 60f);
            int tSec = (int)(d.fightTime % 60f);
            if (tMin != _cacheTMin || tSec != _cacheTSec) {
                _cacheTMin = tMin;
                _cacheTSec = tSec;
                WriteTimer(tMin, tSec);
            }

            // Комбо ×N (при combo ≥ 2)
            if (d.combo != _cacheCombo) {
                _cacheCombo = d.combo;
                bool show = d.combo >= 2;
                _comboLbl.gameObject.SetActive(show);
                if (show) _comboLbl.text = "×" + d.combo;
            }

            // Телеграф: «!» при замахе соперника
            bool telActive = d.foePhase == FightPhase.Windup && d.telegraphAlpha > 0.05f;
            if (telActive) {
                bool showL = d.foeAttackSide < 0;
                bool showR = d.foeAttackSide > 0;
                Color c = UIKit.HpRed;
                c.a = d.telegraphAlpha;
                _telegraphL.gameObject.SetActive(showL);
                _telegraphR.gameObject.SetActive(showR);
                if (showL) _telegraphL.color = c;
                if (showR) _telegraphR.color = c;
            } else {
                _telegraphL.gameObject.SetActive(false);
                _telegraphR.gameObject.SetActive(false);
            }

            // Виньетка: появляется при hp < 55%
            float vign = plrRatio < 0.55f ? (0.55f - plrRatio) / 0.55f * 0.65f : 0f;
            _vignette.color = new Color(0.55f, 0f, 0f, vign);
        }

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
        }

        // ------------------------------------------------------------------ Публичные методы

        // Показать крупный счёт рефери на 0.75 с (для нокдаун-отсчёта).
        public void ShowCount(int n) {
            _countLbl.text = n.ToString();
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

        // ILayoutHandler: вызывается при смене ориентации.
        public void OnLayout(bool landscape) {
            if (landscape) {
                // Уклоны по бокам в ландшафте
                UIKit.SetAnchors(_dodgeL.GetComponent<RectTransform>(), 0.00f, 0.10f, 0.11f, 0.40f);
                UIKit.SetAnchors(_dodgeR.GetComponent<RectTransform>(), 0.89f, 0.10f, 1.00f, 0.40f);
                // БЛОК: высота 0.20×519≈104px ≥96px (минимальная цель пальца)
                UIKit.SetAnchors(_blockBtn.GetComponent<RectTransform>(), 0.35f, 0.02f, 0.65f, 0.22f);
            } else {
                // Портрет: кнопки внизу
                UIKit.SetAnchors(_dodgeL.GetComponent<RectTransform>(), 0.00f, 0.02f, 0.26f, 0.11f);
                UIKit.SetAnchors(_dodgeR.GetComponent<RectTransform>(), 0.74f, 0.02f, 1.00f, 0.11f);
                UIKit.SetAnchors(_blockBtn.GetComponent<RectTransform>(), 0.30f, 0.02f, 0.70f, 0.11f);
            }
        }

        // ------------------------------------------------------------------ Вспомогательное

        // Формат M:SS через char-буфер — не аллоцирует при каждом вызове.
        void WriteTimer(int m, int s) {
            _timeBuf[0] = (char)('0' + m);
            _timeBuf[1] = ':';
            _timeBuf[2] = (char)('0' + s / 10);
            _timeBuf[3] = (char)('0' + s % 10);
            _timerLbl.text = new string(_timeBuf, 0, 4); // аллокация только при смене секунды
        }
    }
}
