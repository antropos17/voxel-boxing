// SettingsScreen.cs — настройки: звук вкл/выкл, громкости (общая, эффекты, толпа) ступенями, вибрация,
// сброс прогресса с подтверждением на самом экране. Значения живут в профиле и сохраняются сразу.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class SettingsScreen : MonoBehaviour, ILayoutHandler {

        public const int Steps = 5;   // ступеней громкости: 0, 20 … 100%
        const int RowFont = UIKit.FontSm + 4;

        public Action<bool> OnSound, OnVibration, OnMotion;
        public Action<VolumeKind, float> OnVolume;
        public Action OnReset, OnBack;

        class StepRow {
            public RectTransform rt;
            public Text label;
            public Button minus, plus;
            public Image[] pips;
        }

        class ToggleRow {
            public RectTransform rt;
            public Text label;
            public Button btn;
            public RectTransform knob;   // ползунок переключателя: справа — включено, слева — выключено
        }

        // Вид переключателя: включено — оранжевая подложка, ползунок справа, «ВКЛ» слева от него;
        // выключено — тёмная подложка, серый ползунок слева, серая «ВЫКЛ» справа.
        public static readonly Color SwitchOnBg = new Color32(150, 85, 22, 255), SwitchOffBg = new Color32(22, 19, 28, 255);
        public static readonly Color KnobOff = new Color32(95, 90, 100, 255);

        Text _header;
        RectTransform _line;
        ToggleRow _soundRow, _vibRow, _motionRow;
        readonly StepRow[] _vol = new StepRow[3];   // индекс = VolumeKind
        RectTransform _resetRow;
        Button _resetBtn, _backBtn;
        RectTransform _confirm;
        Text _confirmText;
        Button _yesBtn, _noBtn;

        bool _sound, _vibration, _motion, _canReset;
        readonly int[] _level = new int[3];

        public bool SoundOn => _sound;
        public bool Vibration => _vibration;
        public bool ReduceMotion => _motion;
        public int Level(VolumeKind k) => _level[(int)k];
        public bool ConfirmShown => _confirm.gameObject.activeSelf;
        public bool ResetVisible => _resetRow.gameObject.activeSelf;

        public static int ToLevel(float v) => Mathf.Clamp(Mathf.RoundToInt(v * Steps), 0, Steps);
        public static float ToVolume(int level) => (float)Mathf.Clamp(level, 0, Steps) / Steps;

        public void Init(UIRoot root, Action<bool> onSound, Action<VolumeKind, float> onVolume, Action<bool> onVibration,
                         Action onReset, Action onBack, Action<bool> onMotion = null) {
            OnSound = onSound; OnVolume = onVolume; OnVibration = onVibration; OnReset = onReset; OnBack = onBack; OnMotion = onMotion;
            BuildUI();
            root.Register(ScreenId.Settings, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);
            MenuKit.Backdrop(transform, new Color(UIKit.BgDark.r, UIKit.BgDark.g, UIKit.BgDark.b, 0.95f));

            _header = UIKit.Label(transform, "Header", UIKit.FontXl, UIKit.Accent, semiBold: true, TextAnchor.MiddleLeft);
            _header.text = "НАСТРОЙКИ";
            _line = MenuKit.Rule(transform);

            _soundRow = MakeToggleRow("Sound", "Звук", PressSound);
            _vol[(int)VolumeKind.Master] = MakeStepRow("Master", "Громкость", VolumeKind.Master);
            _vol[(int)VolumeKind.Sfx]    = MakeStepRow("Sfx", "Удары", VolumeKind.Sfx);
            _vol[(int)VolumeKind.Crowd]  = MakeStepRow("Crowd", "Толпа", VolumeKind.Crowd);
            _vibRow = MakeToggleRow("Vibration", "Вибрация", PressVibration);
            _motionRow = MakeToggleRow("Motion", "Меньше движения", PressMotion);

            _resetRow = new GameObject("ResetRow", typeof(RectTransform)).GetComponent<RectTransform>();
            _resetRow.SetParent(transform, false);
            _resetBtn = MenuKit.Secondary(_resetRow, "BtnReset", "СБРОСИТЬ ПРОГРЕСС", UIKit.FontMd);
            _resetBtn.GetComponentInChildren<Text>().color = new Color(1f, 0.47f, 0.40f, 1f);   // светлее HpRed: читается на сером
            _resetBtn.onClick.AddListener(PressReset);
            MenuKit.Place(_resetBtn.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);

            _backBtn = MenuKit.Secondary(transform, "BtnBack", "НАЗАД", UIKit.FontMd);
            _backBtn.onClick.AddListener(PressBack);

            // Подтверждение сброса: поверх всего экрана
            _confirm = UIKit.Panel(transform, "Confirm", new Color(0.03f, 0.02f, 0.04f, 0.96f));
            _confirm.GetComponent<Image>().raycastTarget = true;   // не пропускать нажатия к строкам под ним
            _confirm.GetComponent<Image>().color = Color.clear;
            // затемнение всего экрана (с запасом за края окна) и непрозрачная карточка: строки под окном не видны
            var dim = UIKit.Panel(_confirm, "Dim", new Color(0f, 0f, 0f, 0.85f));
            UIKit.SetAnchors(dim, -3f, -3f, 4f, 4f);
            dim.offsetMin = dim.offsetMax = Vector2.zero;
            var card = UIKit.Panel(_confirm, "Card", new Color32(30, 23, 36, 255));
            UIKit.SetAnchors(card, 0f, 0f, 1f, 1f);
            card.offsetMin = card.offsetMax = Vector2.zero;
            UIKit.Frame(card, UIKit.Accent, 2f);
            _confirmText = UIKit.Label(_confirm, "Text", UIKit.FontMd, UIKit.TextMain, semiBold: true);
            _confirmText.text = "Сбросить весь прогресс?\nМонеты, перчатки, улучшения и лестница пропадут навсегда.";
            _yesBtn = UIKit.Btn(_confirm, "BtnYes", "ДА, СБРОСИТЬ", UIKit.FontMd, UIKit.HpRed, UIKit.TextMain);
            _yesBtn.onClick.AddListener(PressResetConfirm);
            _noBtn = MenuKit.Secondary(_confirm, "BtnNo", "ОТМЕНА", UIKit.FontMd);
            _noBtn.onClick.AddListener(PressResetCancel);
            _confirm.gameObject.SetActive(false);

            SetData(new PlayerProfile(), true);
            Layout(false);
        }

        ToggleRow MakeToggleRow(string name, string caption, UnityEngine.Events.UnityAction onTap) {
            var r = new ToggleRow();
            r.rt = UIKit.Panel(transform, name, UIKit.BgPanel);
            r.label = UIKit.Label(r.rt, "Label", RowFont, UIKit.TextMain, semiBold: true, TextAnchor.MiddleLeft);
            r.label.text = caption;
            r.btn = MenuKit.Secondary(r.rt, "Toggle", "ВКЛ", UIKit.FontMd);
            r.btn.onClick.AddListener(onTap);
            r.knob = UIKit.Panel(r.btn.transform, "Knob", UIKit.Accent);
            r.knob.offsetMin = new Vector2(6f, 6f);
            r.knob.offsetMax = new Vector2(-6f, -6f);
            return r;
        }

        StepRow MakeStepRow(string name, string caption, VolumeKind kind) {
            var r = new StepRow();
            r.rt = UIKit.Panel(transform, name, UIKit.BgPanel);
            r.label = UIKit.Label(r.rt, "Label", RowFont, UIKit.TextMain, semiBold: true, TextAnchor.MiddleLeft);
            r.label.text = caption;
            r.minus = MenuKit.Secondary(r.rt, "Minus", "−", UIKit.FontXl);
            r.minus.onClick.AddListener(() => Step(kind, -1));
            r.plus = MenuKit.Secondary(r.rt, "Plus", "+", UIKit.FontXl);
            r.plus.onClick.AddListener(() => Step(kind, 1));
            r.pips = MenuKit.Pips(r.rt, "Pips", Steps);
            return r;
        }

        // canReset — сброс доступен (из настроек главного меню; из паузы боя — нет).
        public void SetData(PlayerProfile p, bool canReset) {
            _sound = p.soundEnabled;
            _vibration = p.vibrationEnabled;
            _motion = p.reduceMotion;
            for (int i = 0; i < 3; i++) _level[i] = ToLevel(p.GetVolume((VolumeKind)i));
            _canReset = canReset;
            _resetRow.gameObject.SetActive(canReset);
            _confirm.gameObject.SetActive(false);
            Refresh();
        }

        void Refresh() {
            SetSwitch(_soundRow, _sound);
            SetSwitch(_vibRow, _vibration);
            SetSwitch(_motionRow, _motion);
            for (int i = 0; i < 3; i++) {
                var r = _vol[i];
                MenuKit.SetPips(r.pips, _level[i]);
                // при выключенном звуке ступени видны, но приглушены
                float a = _sound ? 1f : 0.45f;
                r.label.color = new Color(UIKit.TextMain.r, UIKit.TextMain.g, UIKit.TextMain.b, a);
            }
        }

        static void SetSwitch(ToggleRow r, bool on) {
            MenuKit.SetCaption(r.btn, on ? "ВКЛ" : "ВЫКЛ");
            r.btn.GetComponent<Image>().color = on ? SwitchOnBg : SwitchOffBg;
            var lbl = r.btn.GetComponentInChildren<Text>();
            lbl.color = on ? UIKit.TextMain : UIKit.TextSub;
            // надпись — на свободной от ползунка стороне
            UIKit.SetAnchors(lbl.rectTransform, on ? 0f : 0.36f, 0f, on ? 0.64f : 1f, 1f);
            UIKit.SetAnchors(r.knob, on ? 0.66f : 0f, 0f, on ? 1f : 0.34f, 1f);
            r.knob.GetComponent<Image>().color = on ? UIKit.Accent : KnobOff;
        }

        // Для тестов и прохода: включён ли переключатель на экране (по ползунку) и его надпись.
        public bool SwitchShownOn(bool sound) => (sound ? _soundRow : _vibRow).knob.anchorMin.x > 0.5f;
        public string SwitchCaption(bool sound) => (sound ? _soundRow : _vibRow).btn.GetComponentInChildren<Text>().text;

        void Step(VolumeKind k, int d) {
            int i = (int)k;
            int v = Mathf.Clamp(_level[i] + d, 0, Steps);
            if (v == _level[i]) return;
            _level[i] = v;
            Refresh();
            OnVolume?.Invoke(k, ToVolume(v));
        }

        // ------------------------------------------------------------------ Нажатия

        public void PressVolume(VolumeKind k, int delta) => Step(k, delta);

        public void PressSound() {
            _sound = !_sound;
            Refresh();
            OnSound?.Invoke(_sound);
        }

        public void PressVibration() {
            _vibration = !_vibration;
            Refresh();
            OnVibration?.Invoke(_vibration);
        }

        // «Меньше движения»: без тряски, вспышек и каскадов (UIMotion.Reduced).
        public void PressMotion() {
            _motion = !_motion;
            Refresh();
            OnMotion?.Invoke(_motion);
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

        // Портрет: строки столбцом (подпись слева, управление справа).
        // Ландшафт: сетка 3×2, в каждой ячейке подпись сверху и управление под ней.
        public void Layout(bool landscape) {
            var back = _backBtn.GetComponent<RectTransform>();
            if (landscape) {
                MenuKit.Place(_header.rectTransform, 0.03f, 0.885f, 0.97f, 0.995f);
                MenuKit.Place(_line,                 0.03f, 0.876f, 0.97f, 0.881f);
                const float top0 = 0.560f, top1 = 0.865f, bot0 = 0.240f, bot1 = 0.545f;
                PlaceToggle(_soundRow, 0.020f, top0, 0.325f, top1, true);
                PlaceStep(_vol[0],     0.345f, top0, 0.655f, top1, true);
                PlaceStep(_vol[1],     0.675f, top0, 0.980f, top1, true);
                // нижний ряд — четыре ячейки: толпа, вибрация, меньше движения, сброс
                PlaceStep(_vol[2],     0.020f, bot0, 0.300f, bot1, true);
                PlaceToggle(_vibRow,   0.315f, bot0, 0.520f, bot1, true);
                PlaceToggle(_motionRow, 0.535f, bot0, 0.740f, bot1, true);
                MenuKit.Place(_resetRow, 0.755f, bot0 + 0.06f, 0.980f, bot1 - 0.06f);
                MenuKit.Place(back, 0.02f, 0.02f, 0.24f, 0.21f);
                MenuKit.Place(_confirm, 0.10f, 0.08f, 0.90f, 0.92f);
                MenuKit.Place(_confirmText.rectTransform, 0.05f, 0.45f, 0.95f, 0.95f);
                MenuKit.Place(_yesBtn.GetComponent<RectTransform>(), 0.05f, 0.06f, 0.48f, 0.36f);
                MenuKit.Place(_noBtn.GetComponent<RectTransform>(),  0.52f, 0.06f, 0.95f, 0.36f);
            } else {
                MenuKit.Place(_header.rectTransform, 0.05f, 0.935f, 0.95f, 0.998f);
                MenuKit.Place(_line,                 0.05f, 0.929f, 0.95f, 0.933f);
                // семь строк по 0.094 с зазором 0.010 сверху вниз
                float y = 0.918f;
                const float h = 0.094f, gap = 0.010f;
                PlaceToggle(_soundRow, 0.03f, y - h, 0.97f, y, false); y -= h + gap;
                PlaceStep(_vol[0],     0.03f, y - h, 0.97f, y, false); y -= h + gap;
                PlaceStep(_vol[1],     0.03f, y - h, 0.97f, y, false); y -= h + gap;
                PlaceStep(_vol[2],     0.03f, y - h, 0.97f, y, false); y -= h + gap;
                PlaceToggle(_vibRow,   0.03f, y - h, 0.97f, y, false); y -= h + gap;
                PlaceToggle(_motionRow, 0.03f, y - h, 0.97f, y, false); y -= h + gap;
                MenuKit.Place(_resetRow, 0.03f, y - h, 0.97f, y);
                MenuKit.Place(back, 0.03f, 0.012f, 0.40f, 0.125f);
                MenuKit.Place(_confirm, 0.03f, 0.25f, 0.97f, 0.70f);
                MenuKit.Place(_confirmText.rectTransform, 0.05f, 0.42f, 0.95f, 0.95f);
                MenuKit.Place(_yesBtn.GetComponent<RectTransform>(), 0.05f, 0.05f, 0.48f, 0.34f);
                MenuKit.Place(_noBtn.GetComponent<RectTransform>(),  0.52f, 0.05f, 0.95f, 0.34f);
            }
        }

        static void PlaceToggle(ToggleRow r, float x0, float y0, float x1, float y1, bool stacked) {
            MenuKit.Place(r.rt, x0, y0, x1, y1);
            if (stacked) {
                MenuKit.Place(r.label.rectTransform, 0.06f, 0.62f, 0.94f, 0.98f);
                MenuKit.Place(r.btn.GetComponent<RectTransform>(), 0.06f, 0.04f, 0.94f, 0.60f);
            } else {
                MenuKit.Place(r.label.rectTransform, 0.05f, 0f, 0.50f, 1f);
                MenuKit.Place(r.btn.GetComponent<RectTransform>(), 0.52f, 0.08f, 0.97f, 0.92f);
            }
        }

        static void PlaceStep(StepRow r, float x0, float y0, float x1, float y1, bool stacked) {
            MenuKit.Place(r.rt, x0, y0, x1, y1);
            if (stacked) {
                MenuKit.Place(r.label.rectTransform, 0.06f, 0.62f, 0.94f, 0.98f);
                MenuKit.Place(r.minus.GetComponent<RectTransform>(), 0.03f, 0.04f, 0.30f, 0.60f);
                MenuKit.Place(MenuKit.PipsRect(r.pips),              0.33f, 0.20f, 0.67f, 0.44f);
                MenuKit.Place(r.plus.GetComponent<RectTransform>(),  0.70f, 0.04f, 0.97f, 0.60f);
            } else {
                MenuKit.Place(r.label.rectTransform, 0.04f, 0f, 0.32f, 1f);
                MenuKit.Place(r.minus.GetComponent<RectTransform>(), 0.33f, 0.08f, 0.51f, 0.92f);
                MenuKit.Place(MenuKit.PipsRect(r.pips),              0.53f, 0.32f, 0.79f, 0.68f);
                MenuKit.Place(r.plus.GetComponent<RectTransform>(),  0.80f, 0.08f, 0.99f, 0.92f);
            }
        }

        public void Tick(float dt, float udt) { }
    }
}
