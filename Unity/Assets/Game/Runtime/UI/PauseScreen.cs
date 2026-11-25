// PauseScreen.cs — пауза поверх замершего боя: ПРОДОЛЖИТЬ / НАСТРОЙКИ / СДАТЬСЯ (сдача — со вторым нажатием).
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class PauseScreen : MonoBehaviour, ILayoutHandler {

        public Action OnResume, OnSettings, OnSurrender;

        Text _title, _foe, _hint;
        RectTransform _panel;
        Button _resumeBtn, _settingsBtn, _surrenderBtn;
        bool _armed;

        public bool SurrenderArmed => _armed;

        public void Init(UIRoot root, Action onResume, Action onSettings, Action onSurrender) {
            OnResume = onResume; OnSettings = onSettings; OnSurrender = onSurrender;
            BuildUI();
            root.Register(ScreenId.Pause, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);
            // сцена боя видна сквозь затемнение
            var bg = MenuKit.Backdrop(transform, new Color(UIKit.BgDark.r, UIKit.BgDark.g, UIKit.BgDark.b, 0.72f));
            bg.GetComponent<Image>().raycastTarget = true;

            _panel = UIKit.Panel(transform, "Panel", UIKit.BgPanel);
            var frame = _panel.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.6f);
            frame.effectDistance = new Vector2(2f, -2f);

            _title = UIKit.Label(_panel, "Title", UIKit.FontXl + 10, UIKit.Accent, semiBold: true);
            _title.text = "ПАУЗА";
            _foe = UIKit.Label(_panel, "Foe", UIKit.FontSm, UIKit.TextSub, semiBold: false);

            _resumeBtn = MenuKit.Primary(_panel, "BtnResume", "ПРОДОЛЖИТЬ", UIKit.FontLg);
            _resumeBtn.onClick.AddListener(PressResume);
            _settingsBtn = MenuKit.Secondary(_panel, "BtnSettings", "НАСТРОЙКИ", UIKit.FontMd);
            _settingsBtn.onClick.AddListener(PressSettings);
            _surrenderBtn = MenuKit.Secondary(_panel, "BtnSurrender", "СДАТЬСЯ", UIKit.FontMd);
            _surrenderBtn.GetComponentInChildren<Text>().color = UIKit.HpRed;
            _surrenderBtn.onClick.AddListener(PressSurrender);
            _hint = UIKit.Label(_panel, "Hint", UIKit.FontXs + 1, UIKit.TextSub, semiBold: false);

            Layout(false);
            Show("");
        }

        // Вызывать при каждом входе в паузу: сбрасывает подтверждение сдачи.
        public void Show(string foeName) {
            _armed = false;
            _foe.text = string.IsNullOrEmpty(foeName) ? "" : "Соперник: " + foeName;
            MenuKit.SetCaption(_surrenderBtn, "СДАТЬСЯ");
            _hint.text = "Сдача засчитается как поражение";
        }

        public void PressResume()   => OnResume?.Invoke();
        public void PressSettings() => OnSettings?.Invoke();

        // Первое нажатие — переспросить, второе — сдаться.
        public void PressSurrender() {
            if (!_armed) {
                _armed = true;
                MenuKit.SetCaption(_surrenderBtn, "ТОЧНО СДАТЬСЯ?");
                _hint.text = "Нажми ещё раз, чтобы сдаться";
                return;
            }
            _armed = false;
            OnSurrender?.Invoke();
        }

        public void OnLayout(bool landscape) => Layout(landscape);

        public void Layout(bool landscape) {
            if (landscape) {
                MenuKit.Place(_panel, 0.22f, 0.04f, 0.78f, 0.96f);
                MenuKit.Place(_title.rectTransform, 0.05f, 0.80f, 0.95f, 0.98f);
                MenuKit.Place(_foe.rectTransform,   0.05f, 0.72f, 0.95f, 0.80f);
                MenuKit.Place(_resumeBtn.GetComponent<RectTransform>(),    0.08f, 0.47f, 0.92f, 0.70f);
                MenuKit.Place(_settingsBtn.GetComponent<RectTransform>(),  0.08f, 0.23f, 0.49f, 0.445f);
                MenuKit.Place(_surrenderBtn.GetComponent<RectTransform>(), 0.51f, 0.23f, 0.92f, 0.445f);
                MenuKit.Place(_hint.rectTransform,  0.05f, 0.03f, 0.95f, 0.20f);
            } else {
                MenuKit.Place(_panel, 0.06f, 0.22f, 0.94f, 0.78f);
                MenuKit.Place(_title.rectTransform, 0.05f, 0.82f, 0.95f, 0.97f);
                MenuKit.Place(_foe.rectTransform,   0.05f, 0.75f, 0.95f, 0.82f);
                MenuKit.Place(_resumeBtn.GetComponent<RectTransform>(),    0.08f, 0.54f, 0.92f, 0.72f);
                MenuKit.Place(_settingsBtn.GetComponent<RectTransform>(),  0.08f, 0.35f, 0.92f, 0.51f);
                MenuKit.Place(_surrenderBtn.GetComponent<RectTransform>(), 0.08f, 0.16f, 0.92f, 0.32f);
                MenuKit.Place(_hint.rectTransform,  0.05f, 0.03f, 0.95f, 0.14f);
            }
        }

        public void Tick(float dt, float udt) { }
    }
}
