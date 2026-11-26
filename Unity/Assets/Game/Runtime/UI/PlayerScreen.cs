// PlayerScreen.cs — профиль бойца: имя, уровень и полоса опыта, победы/поражения, пояса, надетые перчатки, пять улучшений.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class PlayerScreen : MonoBehaviour, ILayoutHandler {

        public Action OnBack, OnShop;

        Text _header, _name, _level, _xpText, _record;
        RectTransform _line, _xpBg, _xpFill;
        RectTransform _glovePanel, _swA, _swB;
        Text _gloveTitle;
        GloveStatsView _stats;
        RectTransform _upPanel;
        Text _upTitle;
        readonly Text[] _upName = new Text[5], _upEffect = new Text[5];
        readonly Image[][] _upPips = new Image[5][];
        Button _backBtn, _shopBtn;

        public string RecordText => _record.text;
        public string LevelText => _level.text;

        public void Init(UIRoot root, Action onBack, Action onShop) {
            OnBack = onBack; OnShop = onShop;
            BuildUI();
            root.Register(ScreenId.Player, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);
            MenuKit.Backdrop(transform, UIKit.BgDark);

            _header = UIKit.Label(transform, "Header", UIKit.FontXl, UIKit.Accent, semiBold: true, TextAnchor.MiddleLeft);
            _header.text = "БОЕЦ";
            _line = MenuKit.Rule(transform);

            _name = UIKit.Label(transform, "Name", UIKit.FontXl, UIKit.TextMain, semiBold: true, TextAnchor.MiddleLeft);
            _level = UIKit.Label(transform, "Level", UIKit.FontMd, UIKit.Accent, semiBold: true, TextAnchor.MiddleLeft);
            (_xpBg, _xpFill) = UIKit.Bar(transform, "Xp", new Color32(40, 34, 30, 220), UIKit.Accent);
            _xpText = UIKit.Label(transform, "XpText", UIKit.FontXs + 2, UIKit.TextSub, semiBold: false, TextAnchor.MiddleRight);
            _record = UIKit.Label(transform, "Record", UIKit.FontSm, UIKit.TextMain, semiBold: false, TextAnchor.MiddleLeft);

            _glovePanel = UIKit.Panel(transform, "Gloves", UIKit.BgPanel);
            _swA = UIKit.Panel(_glovePanel, "SwA", Color.white);
            _swB = UIKit.Panel(_glovePanel, "SwB", Color.white);
            _gloveTitle = UIKit.Label(_glovePanel, "Title", UIKit.FontMd, UIKit.TextMain, semiBold: true, TextAnchor.MiddleLeft);
            _stats = new GloveStatsView(_glovePanel);

            _upPanel = UIKit.Panel(transform, "Upgrades", UIKit.BgPanel);
            _upTitle = UIKit.Label(_upPanel, "Title", UIKit.FontMd, UIKit.TextMain, semiBold: true, TextAnchor.MiddleLeft);
            _upTitle.text = "Улучшения";
            for (int i = 0; i < 5; i++) {
                var t = ShopScreen.UpgradeOrder[i];
                _upName[i] = UIKit.Label(_upPanel, "N" + i, UIKit.FontSm, UIKit.TextMain, semiBold: true, TextAnchor.MiddleLeft);
                _upName[i].text = ShopScreen.UpgradeName(t);
                _upPips[i] = MenuKit.Pips(_upPanel, "P" + i, PlayerProfile.MaxUpgradeLevel);
                _upEffect[i] = UIKit.Label(_upPanel, "E" + i, UIKit.FontXs + 1, UIKit.TextSub, semiBold: false, TextAnchor.MiddleRight);
            }

            _backBtn = MenuKit.Secondary(transform, "BtnBack", "НАЗАД", UIKit.FontMd);
            _backBtn.onClick.AddListener(PressBack);
            _shopBtn = MenuKit.Primary(transform, "BtnShop", "В МАГАЗИН", UIKit.FontMd);
            _shopBtn.onClick.AddListener(PressShop);

            SetData(new PlayerProfile());
            Layout(false);
        }

        public void SetData(PlayerProfile p) {
            _name.text = p.playerName;
            bool max = p.level >= PlayerProfile.MaxPlayerLevel;
            int need = PlayerProfile.XpForNextLevel(p.level);
            _level.text = "Уровень " + p.level + (max ? " (максимум)" : "");
            UIKit.SetBarH(_xpFill, max ? 1f : (float)p.xp / Mathf.Max(1, need));
            _xpText.text = max ? "опыт набран" : "опыт " + p.xp + " / " + need;
            _record.text = "Победы: " + p.wins + "   Поражения: " + p.losses + "   Пояса: " + p.beltCount;

            var g = GlovesCatalog.Get(p.equippedGloveId);
            _gloveTitle.text = "Перчатки: " + g.displayName;
            _swA.GetComponent<Image>().color = g.colorA;
            _swB.GetComponent<Image>().color = g.colorB;
            _stats.Set(g);

            for (int i = 0; i < 5; i++) {
                var t = ShopScreen.UpgradeOrder[i];
                int lv = p.upgradeLevels[(int)t];
                MenuKit.SetPips(_upPips[i], lv);
                _upEffect[i].text = lv > 0 ? ShopScreen.UpgradeEffect(t, GameFlow.UpgradeMult(t, lv)) : "нет";
            }
        }

        public void PressBack() => OnBack?.Invoke();
        public void PressShop() => OnShop?.Invoke();

        // ------------------------------------------------------------------ Раскладка

        public void OnLayout(bool landscape) => Layout(landscape);

        public void Layout(bool landscape) {
            if (landscape) {
                MenuKit.Place(_header.rectTransform, 0.02f, 0.885f, 0.48f, 0.995f);
                MenuKit.Place(_line,                 0.02f, 0.876f, 0.98f, 0.881f);
                MenuKit.Place(_name.rectTransform,   0.02f, 0.765f, 0.48f, 0.865f);
                MenuKit.Place(_level.rectTransform,  0.02f, 0.700f, 0.30f, 0.765f);
                MenuKit.Place(_xpText.rectTransform, 0.28f, 0.700f, 0.48f, 0.765f);
                MenuKit.Place(_xpBg, 0.02f, 0.660f, 0.48f, 0.695f);
                MenuKit.Place(_record.rectTransform, 0.02f, 0.585f, 0.48f, 0.650f);
                MenuKit.Place(_glovePanel,           0.02f, 0.030f, 0.48f, 0.570f);
                GloveInner(0.80f);
                MenuKit.Place(_upPanel,              0.52f, 0.245f, 0.98f, 0.865f);
                UpInner();
                MenuKit.Place(_backBtn.GetComponent<RectTransform>(), 0.52f, 0.030f, 0.70f, 0.225f);
                MenuKit.Place(_shopBtn.GetComponent<RectTransform>(), 0.72f, 0.030f, 0.98f, 0.225f);
            } else {
                MenuKit.Place(_header.rectTransform, 0.05f, 0.935f, 0.95f, 0.998f);
                MenuKit.Place(_line,                 0.05f, 0.929f, 0.95f, 0.933f);
                MenuKit.Place(_name.rectTransform,   0.05f, 0.865f, 0.95f, 0.922f);
                MenuKit.Place(_level.rectTransform,  0.05f, 0.825f, 0.55f, 0.865f);
                MenuKit.Place(_xpText.rectTransform, 0.45f, 0.825f, 0.95f, 0.865f);
                MenuKit.Place(_xpBg, 0.05f, 0.800f, 0.95f, 0.822f);
                MenuKit.Place(_record.rectTransform, 0.05f, 0.755f, 0.95f, 0.795f);
                MenuKit.Place(_glovePanel,           0.03f, 0.480f, 0.97f, 0.745f);
                GloveInner(0.80f);
                MenuKit.Place(_upPanel,              0.03f, 0.140f, 0.97f, 0.468f);
                UpInner();
                MenuKit.Place(_backBtn.GetComponent<RectTransform>(), 0.03f, 0.012f, 0.40f, 0.125f);
                MenuKit.Place(_shopBtn.GetComponent<RectTransform>(), 0.43f, 0.012f, 0.97f, 0.125f);
            }
        }

        // Шапка панели перчаток (цвета и название) выше titleY, полосы свойств — ниже.
        void GloveInner(float titleY) {
            MenuKit.Place(_swA, 0.03f, titleY + 0.03f, 0.09f, 0.97f);
            MenuKit.Place(_swB, 0.09f, titleY + 0.03f, 0.12f, 0.97f);
            MenuKit.Place(_gloveTitle.rectTransform, 0.15f, titleY, 0.97f, 1f);
            MenuKit.Place(_stats.Root, 0.04f, 0.03f, 0.96f, titleY - 0.02f);
        }

        void UpInner() {
            MenuKit.Place(_upTitle.rectTransform, 0.04f, 0.84f, 0.96f, 1f);
            for (int i = 0; i < 5; i++) {
                float y1 = 0.84f - i * 0.165f, y0 = y1 - 0.165f;
                MenuKit.Place(_upName[i].rectTransform, 0.04f, y0, 0.40f, y1);
                MenuKit.Place(MenuKit.PipsRect(_upPips[i]), 0.38f, y0 + 0.035f, 0.55f, y1 - 0.035f);
                MenuKit.Place(_upEffect[i].rectTransform, 0.56f, y0, 0.96f, y1);
            }
        }

        public void Tick(float dt, float udt) { }
    }
}
