// PauseScreen.cs — пауза поверх замершего боя: ПРОДОЛЖИТЬ / ЗАНОВО / НАСТРОЙКИ / В МЕНЮ (выход — со вторым нажатием).
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class PauseScreen : MonoBehaviour, ILayoutHandler {

        public Action OnResume, OnRestart, OnSettings, OnMenu;

        Text _title, _foe, _hint;
        RectTransform _panel;
        Button _resumeBtn, _restartBtn, _settingsBtn, _menuBtn;
        bool _armed;

        public bool MenuArmed => _armed;

        public void Init(UIRoot root, Action onResume, Action onRestart, Action onSettings, Action onMenu) {
            OnResume = onResume; OnRestart = onRestart; OnSettings = onSettings; OnMenu = onMenu;
            BuildUI();
            root.Register(ScreenId.Pause, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);
            // сцена боя видна сквозь затемнение
            var bg = MenuKit.Backdrop(transform, new Color(UIKit.BgDark.r, UIKit.BgDark.g, UIKit.BgDark.b, 0.72f));
            bg.GetComponent<Image>().raycastTarget = true;

            _panel = UIKit.Panel(transform, "Panel", UIKit.BgPanel);
            _panel.GetComponent<UIBorder>().Color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.6f);

            _title = UIKit.Label(_panel, "Title", UIKit.FontXl + 10, UIKit.Accent, semiBold: true);
            _title.text = "ПАУЗА";
            _foe = UIKit.Label(_panel, "Foe", UIKit.FontSm, UIKit.TextSub, semiBold: false);

            _resumeBtn = MenuKit.Primary(_panel, "BtnResume", "ПРОДОЛЖИТЬ", UIKit.FontLg);
            _resumeBtn.onClick.AddListener(PressResume);
            _restartBtn = MenuKit.Secondary(_panel, "BtnRestart", "ЗАНОВО", UIKit.FontMd);
            _restartBtn.onClick.AddListener(PressRestart);
            _settingsBtn = MenuKit.Secondary(_panel, "BtnSettings", "НАСТРОЙКИ", UIKit.FontMd);
            _settingsBtn.onClick.AddListener(PressSettings);
            _menuBtn = MenuKit.Secondary(_panel, "BtnMenu", "В МЕНЮ", UIKit.FontMd);
            _menuBtn.GetComponentInChildren<Text>().color = new Color(1f, 0.47f, 0.40f, 1f);   // светлее HpRed: читается на сером
            _menuBtn.onClick.AddListener(PressMenu);
            _hint = UIKit.Label(_panel, "Hint", UIKit.FontXs + 1, UIKit.TextSub, semiBold: false);

            Layout(false);
            Show("", true);
        }

        // Вызывать при каждом входе в паузу: сбрасывает подтверждение выхода.
        // countsAsLoss — выход в меню засчитывается поражением (на тренировке — нет).
        public void Show(string foeName, bool countsAsLoss) {
            _armed = false;
            _foe.text = string.IsNullOrEmpty(foeName) ? "" : "Соперник: " + foeName;
            MenuKit.SetCaption(_menuBtn, "В МЕНЮ");
            _hint.text = countsAsLoss ? "Выход в меню засчитается как поражение" : "Тренировка: выход без последствий";
        }

        public void PressResume()   => OnResume?.Invoke();
        public void PressRestart()  => OnRestart?.Invoke();
        public void PressSettings() => OnSettings?.Invoke();

        // Первое нажатие — переспросить, второе — выйти.
        public void PressMenu() {
            if (!_armed) {
                _armed = true;
                MenuKit.SetCaption(_menuBtn, "ТОЧНО В МЕНЮ?");
                _hint.text = "Нажми ещё раз, чтобы выйти из боя";
                return;
            }
            _armed = false;
            OnMenu?.Invoke();
        }

        public void OnLayout(bool landscape) => Layout(landscape);

        public void Layout(bool landscape) {
            var resume = _resumeBtn.GetComponent<RectTransform>();
            var restart = _restartBtn.GetComponent<RectTransform>();
            var settings = _settingsBtn.GetComponent<RectTransform>();
            var menu = _menuBtn.GetComponent<RectTransform>();
            if (landscape) {
                MenuKit.Place(_panel, 0.20f, 0.04f, 0.80f, 0.96f);
                MenuKit.Place(_title.rectTransform, 0.05f, 0.84f, 0.95f, 0.98f);
                MenuKit.Place(_foe.rectTransform,   0.05f, 0.77f, 0.95f, 0.84f);
                MenuKit.Place(resume,   0.06f, 0.555f, 0.94f, 0.755f);
                MenuKit.Place(restart,  0.06f, 0.335f, 0.49f, 0.535f);
                MenuKit.Place(settings, 0.51f, 0.335f, 0.94f, 0.535f);
                MenuKit.Place(menu,     0.06f, 0.115f, 0.94f, 0.315f);
                MenuKit.Place(_hint.rectTransform, 0.05f, 0.01f, 0.95f, 0.105f);
            } else {
                MenuKit.Place(_panel, 0.06f, 0.18f, 0.94f, 0.82f);
                MenuKit.Place(_title.rectTransform, 0.05f, 0.84f, 0.95f, 0.97f);
                MenuKit.Place(_foe.rectTransform,   0.05f, 0.77f, 0.95f, 0.84f);
                MenuKit.Place(resume,   0.08f, 0.600f, 0.92f, 0.760f);
                MenuKit.Place(restart,  0.08f, 0.420f, 0.49f, 0.575f);
                MenuKit.Place(settings, 0.51f, 0.420f, 0.92f, 0.575f);
                MenuKit.Place(menu,     0.08f, 0.240f, 0.92f, 0.395f);
                MenuKit.Place(_hint.rectTransform, 0.05f, 0.04f, 0.95f, 0.21f);
            }
        }

        public void Tick(float dt, float udt) { }
    }
}
