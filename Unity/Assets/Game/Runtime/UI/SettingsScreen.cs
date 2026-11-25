// SettingsScreen.cs — настройки: громкость звуков и толпы ступенями, вибрация, сброс прогресса с подтверждением.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class SettingsScreen : MonoBehaviour, ILayoutHandler {

        public Action<int> OnSoundLevel, OnCrowdLevel;
        public Action<bool> OnVibration;
        public Action OnReset, OnBack;

        class StepRow {
            public RectTransform rt;
            public Text label;
            public Button minus, plus;
            public Image[] pips;
        }

        Text _header;
        RectTransform _line;
        StepRow _sound, _crowd;
        RectTransform _vibRow, _resetRow;
        Text _vibLabel;
        Button _vibBtn, _resetBtn, _backBtn;
        RectTransform _confirm;
        Text _confirmText;
        Button _yesBtn, _noBtn;

        int _soundLevel, _crowdLevel;
        bool _vibration, _canReset;

        public int SoundLevel => _soundLevel;
        public int CrowdLevel => _crowdLevel;
        public bool Vibration => _vibration;
        public bool ConfirmShown => _confirm.gameObject.activeSelf;

        public void Init(UIRoot root, Action<int> onSound, Action<int> onCrowd, Action<bool> onVibration,
                         Action onReset, Action onBack) {
            OnSoundLevel = onSound; OnCrowdLevel = onCrowd; OnVibration = onVibration; OnReset = onReset; OnBack = onBack;
            BuildUI();
            root.Register(ScreenId.Settings, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);
            MenuKit.Backdrop(transform, new Color(UIKit.BgDark.r, UIKit.BgDark.g, UIKit.BgDark.b, 0.95f));

            _header = UIKit.Label(transform, "Header", UIKit.FontXl, UIKit.Accent, semiBold: true, TextAnchor.MiddleLeft);
            _header.text = "НАСТРОЙКИ";
            _line = UIKit.Panel(transform, "Line", UIKit.AccentDim);

            _sound = MakeStepRow("Sound", "Звуки", () => StepSound(-1), () => StepSound(1));
            _crowd = MakeStepRow("Crowd", "Толпа", () => StepCrowd(-1), () => StepCrowd(1));

            _vibRow = UIKit.Panel(transform, "Vibration", UIKit.BgPanel);
            _vibLabel = UIKit.Label(_vibRow, "Label", UIKit.FontMd, UIKit.TextMain, semiBold: true, TextAnchor.MiddleLeft);
            _vibLabel.text = "Вибрация";
            MenuKit.Place(_vibLabel.rectTransform, 0.05f, 0f, 0.50f, 1f);
            _vibBtn = MenuKit.Secondary(_vibRow, "Toggle", "ВКЛ", UIKit.FontMd);
            _vibBtn.onClick.AddListener(PressVibration);
            UIKit.SetAnchors(_vibBtn.GetComponent<RectTransform>(), 0.52f, 0.12f, 0.97f, 0.88f);
            _vibBtn.GetComponent<RectTransform>().offsetMin = _vibBtn.GetComponent<RectTransform>().offsetMax = Vector2.zero;

            _resetRow = new GameObject("ResetRow", typeof(RectTransform)).GetComponent<RectTransform>();
            _resetRow.SetParent(transform, false);
            _resetBtn = MenuKit.Secondary(_resetRow, "BtnReset", "СБРОСИТЬ ПРОГРЕСС", UIKit.FontSm);
            _resetBtn.GetComponentInChildren<Text>().color = UIKit.HpRed;
            _resetBtn.onClick.AddListener(PressReset);
            MenuKit.Place(_resetBtn.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);

            _backBtn = MenuKit.Secondary(transform, "BtnBack", "НАЗАД", UIKit.FontMd);
            _backBtn.onClick.AddListener(PressBack);

            // Подтверждение сброса: поверх всего экрана
            _confirm = UIKit.Panel(transform, "Confirm", new Color(0.03f, 0.02f, 0.04f, 0.96f));
            _confirm.GetComponent<Image>().raycastTarget = true;   // не пропускать нажатия к строкам под ним
            _confirmText = UIKit.Label(_confirm, "Text", UIKit.FontMd, UIKit.TextMain, semiBold: true);
            _confirmText.text = "Сбросить весь прогресс?\nМонеты, перчатки, улучшения и лестница пропадут навсегда.";
            _yesBtn = UIKit.Btn(_confirm, "BtnYes", "ДА, СБРОСИТЬ", UIKit.FontMd, UIKit.HpRed, UIKit.TextMain);
            _yesBtn.onClick.AddListener(PressResetConfirm);
            _noBtn = MenuKit.Secondary(_confirm, "BtnNo", "ОТМЕНА", UIKit.FontMd);
            _noBtn.onClick.AddListener(PressResetCancel);
            _confirm.gameObject.SetActive(false);

            SetData(new GameSettings(), true);
            Layout(false);
        }

        StepRow MakeStepRow(string name, string caption, UnityEngine.Events.UnityAction minus, UnityEngine.Events.UnityAction plus) {
            var r = new StepRow();
            r.rt = UIKit.Panel(transform, name, UIKit.BgPanel);
            r.label = UIKit.Label(r.rt, "Label", UIKit.FontMd, UIKit.TextMain, semiBold: true, TextAnchor.MiddleLeft);
            r.label.text = caption;
            r.minus = MenuKit.Secondary(r.rt, "Minus", "−", UIKit.FontXl);
            r.minus.onClick.AddListener(minus);
            r.plus = MenuKit.Secondary(r.rt, "Plus", "+", UIKit.FontXl);
            r.plus.onClick.AddListener(plus);
            r.pips = MenuKit.Pips(r.rt, "Pips", GameSettings.MaxLevel);
            MenuKit.Place(r.label.rectTransform, 0.04f, 0f, 0.30f, 1f);
            MenuKit.Place(r.minus.GetComponent<RectTransform>(), 0.30f, 0.10f, 0.49f, 0.90f);
            MenuKit.Place(MenuKit.PipsRect(r.pips), 0.51f, 0.32f, 0.79f, 0.68f);
            MenuKit.Place(r.plus.GetComponent<RectTransform>(), 0.80f, 0.10f, 0.99f, 0.90f);
            return r;
        }

        // canReset — сброс доступен (из настроек главного меню; из паузы боя — нет).
        public void SetData(GameSettings s, bool canReset) {
            _soundLevel = s.soundLevel;
            _crowdLevel = s.crowdLevel;
            _vibration = s.vibration;
            _canReset = canReset;
            _resetRow.gameObject.SetActive(canReset);
            _confirm.gameObject.SetActive(false);
            Refresh();
        }

        void Refresh() {
            MenuKit.SetPips(_sound.pips, _soundLevel);
            MenuKit.SetPips(_crowd.pips, _crowdLevel);
            MenuKit.SetCaption(_vibBtn, _vibration ? "ВКЛ" : "ВЫКЛ");
            MenuKit.SetToggle(_vibBtn, _vibration);
        }

        void StepSound(int d) {
            int v = Mathf.Clamp(_soundLevel + d, 0, GameSettings.MaxLevel);
            if (v == _soundLevel) return;
            _soundLevel = v;
            Refresh();
            OnSoundLevel?.Invoke(v);
        }

        void StepCrowd(int d) {
            int v = Mathf.Clamp(_crowdLevel + d, 0, GameSettings.MaxLevel);
            if (v == _crowdLevel) return;
            _crowdLevel = v;
            Refresh();
            OnCrowdLevel?.Invoke(v);
        }

        // ------------------------------------------------------------------ Нажатия

        public void PressSoundMinus() => StepSound(-1);
        public void PressSoundPlus()  => StepSound(1);
        public void PressCrowdMinus() => StepCrowd(-1);
        public void PressCrowdPlus()  => StepCrowd(1);

        public void PressVibration() {
            _vibration = !_vibration;
            Refresh();
            OnVibration?.Invoke(_vibration);
        }

        // Первое нажатие только спрашивает подтверждение.
        public void PressReset() {
            if (!_canReset) return;
            _confirm.gameObject.SetActive(true);
        }

        public void PressResetConfirm() {
            if (!_confirm.gameObject.activeSelf) return;
            _confirm.gameObject.SetActive(false);
            OnReset?.Invoke();
        }

        public void PressResetCancel() => _confirm.gameObject.SetActive(false);

        public void PressBack() {
            _confirm.gameObject.SetActive(false);
            OnBack?.Invoke();
        }

        // ------------------------------------------------------------------ Раскладка

        public void OnLayout(bool landscape) => Layout(landscape);

        public void Layout(bool landscape) {
            if (landscape) {
                MenuKit.Place(_header.rectTransform, 0.03f, 0.885f, 0.97f, 0.995f);
                MenuKit.Place(_line,                 0.03f, 0.876f, 0.97f, 0.881f);
                MenuKit.Place(_sound.rt,  0.02f, 0.560f, 0.49f, 0.840f);
                MenuKit.Place(_crowd.rt,  0.02f, 0.250f, 0.49f, 0.530f);
                MenuKit.Place(_vibRow,    0.51f, 0.560f, 0.98f, 0.840f);
                MenuKit.Place(_resetRow,  0.51f, 0.250f, 0.98f, 0.530f);
                MenuKit.Place(_backBtn.GetComponent<RectTransform>(), 0.02f, 0.02f, 0.24f, 0.21f);
                MenuKit.Place(_confirm, 0.10f, 0.08f, 0.90f, 0.92f);
                MenuKit.Place(_confirmText.rectTransform, 0.05f, 0.45f, 0.95f, 0.95f);
                MenuKit.Place(_yesBtn.GetComponent<RectTransform>(), 0.05f, 0.06f, 0.48f, 0.36f);
                MenuKit.Place(_noBtn.GetComponent<RectTransform>(),  0.52f, 0.06f, 0.95f, 0.36f);
            } else {
                MenuKit.Place(_header.rectTransform, 0.05f, 0.935f, 0.95f, 0.998f);
                MenuKit.Place(_line,                 0.05f, 0.929f, 0.95f, 0.933f);
                MenuKit.Place(_sound.rt,  0.03f, 0.770f, 0.97f, 0.900f);
                MenuKit.Place(_crowd.rt,  0.03f, 0.625f, 0.97f, 0.755f);
                MenuKit.Place(_vibRow,    0.03f, 0.480f, 0.97f, 0.610f);
                MenuKit.Place(_resetRow,  0.03f, 0.300f, 0.97f, 0.400f);
                MenuKit.Place(_backBtn.GetComponent<RectTransform>(), 0.03f, 0.012f, 0.40f, 0.125f);
                MenuKit.Place(_confirm, 0.03f, 0.25f, 0.97f, 0.70f);
                MenuKit.Place(_confirmText.rectTransform, 0.05f, 0.42f, 0.95f, 0.95f);
                MenuKit.Place(_yesBtn.GetComponent<RectTransform>(), 0.05f, 0.05f, 0.48f, 0.34f);
                MenuKit.Place(_noBtn.GetComponent<RectTransform>(),  0.52f, 0.05f, 0.95f, 0.34f);
            }
        }

        public void Tick(float dt, float udt) { }
    }
}
