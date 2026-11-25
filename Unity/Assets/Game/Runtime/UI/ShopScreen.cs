// ShopScreen.cs — магазин: вкладки ПЕРЧАТКИ (каталог, свойства, купить/надеть, место под 3D-превью) и УЛУЧШЕНИЯ (пять веток).
// Покупки идут через колбэки-функции: магазин сам не меняет профиль, а показывает итог («Не хватает монет»).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class ShopScreen : MonoBehaviour, ILayoutHandler {

        public enum Tab { Gloves, Upgrades }

        const int TileCols = 4;   // плиток перчаток в ряду

        // ------------------------------------------------------------------ Колбэки

        public Func<string, ShopResult> OnBuyGlove;
        public Func<string, ShopResult> OnEquipGlove;
        public Func<UpgradeType, ShopResult> OnBuyUpgrade;
        public Action OnBack;
        public Action<string> OnGloveSelected;   // выбор сменился — можно отрисовать превью и отдать в SetPreview

        // ------------------------------------------------------------------ Тексты

        public static readonly UpgradeType[] UpgradeOrder = {
            UpgradeType.Power, UpgradeType.Speed, UpgradeType.Stamina, UpgradeType.Defense, UpgradeType.MegaCharge
        };

        public static string UpgradeName(UpgradeType t) {
            switch (t) {
                case UpgradeType.Power:   return "Сила";
                case UpgradeType.Speed:   return "Скорость";
                case UpgradeType.Stamina: return "Выносливость";
                case UpgradeType.Defense: return "Стойкость";
                default:                  return "Мега";
            }
        }

        // Эффект множителя улучшения словами: «урон +20%».
        public static string UpgradeEffect(UpgradeType t, float mult) {
            switch (t) {
                case UpgradeType.Power:   return "урон " + GloveStatsView.Percent(mult);
                case UpgradeType.Speed:   return "скорость замаха " + GloveStatsView.Percent(mult);
                case UpgradeType.Stamina: return "расход сил " + GloveStatsView.Percent(mult);
                case UpgradeType.Defense: return "входящий урон " + GloveStatsView.Percent(mult);
                default:                  return "набор меги " + GloveStatsView.Percent(mult);
            }
        }

        public static string GloveDescription(in GloveEntry g) =>
            string.IsNullOrEmpty(g.description) ? "Свойства — на полосах ниже." : g.description;

        public static string UnlockText(int ladder) =>
            "Откроется после " + ladder + " " + MenuKit.Plural(ladder, "победы", "побед", "побед") + " в карьере";

        // ------------------------------------------------------------------ Элементы

        class GloveTile {
            public Button btn;
            public Image bg;
            public RectTransform stripA, stripB;
            public Text name, status;
        }

        class UpgradeRow {
            public UpgradeType type;
            public RectTransform rt;
            public Text name, effect;
            public Image[] pips;
            public Button btn;
        }

        Text _header, _coins;
        RectTransform _line;
        Button _tabGloves, _tabUpgrades, _backBtn, _buyBtn, _equipBtn;

        RectTransform _glovesRoot, _upgradesRoot;
        readonly List<GloveTile> _tiles = new List<GloveTile>();
        RectTransform _card, _previewBox, _swatchA, _swatchB;
        RawImage _preview;
        Text _gName, _gDesc, _gStatus;
        GloveStatsView _stats;
        readonly List<UpgradeRow> _rows = new List<UpgradeRow>();

        RectTransform _toastBox;
        Text _toast;
        float _toastTimer;

        readonly Dictionary<string, Texture> _previews = new Dictionary<string, Texture>();
        PlayerProfile _profile;
        int _selected;
        Tab _tab;

        public Tab CurrentTab => _tab;
        public string SelectedGloveId => GlovesCatalog.All[_selected].id;
        public string ToastText => _toast.gameObject.activeSelf ? _toast.text : "";
        public string CoinsText => _coins.text;

        // ------------------------------------------------------------------ Инициализация

        public void Init(UIRoot root, Func<string, ShopResult> onBuyGlove, Func<string, ShopResult> onEquipGlove,
                         Func<UpgradeType, ShopResult> onBuyUpgrade, Action onBack) {
            OnBuyGlove = onBuyGlove; OnEquipGlove = onEquipGlove; OnBuyUpgrade = onBuyUpgrade; OnBack = onBack;
            BuildUI();
            root.Register(ScreenId.Shop, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);
            MenuKit.Backdrop(transform, UIKit.BgDark);

            _header = UIKit.Label(transform, "Header", UIKit.FontXl, UIKit.Accent, semiBold: true, TextAnchor.MiddleLeft);
            _header.text = "МАГАЗИН";
            _coins = UIKit.Label(transform, "Coins", UIKit.FontMd, UIKit.MegaGold, semiBold: true, TextAnchor.MiddleRight);
            _line = UIKit.Panel(transform, "Line", UIKit.AccentDim);

            _tabGloves = MenuKit.Secondary(transform, "TabGloves", "ПЕРЧАТКИ", UIKit.FontMd);
            _tabGloves.onClick.AddListener(() => ShowTab(Tab.Gloves));
            _tabUpgrades = MenuKit.Secondary(transform, "TabUpgrades", "УЛУЧШЕНИЯ", UIKit.FontMd);
            _tabUpgrades.onClick.AddListener(() => ShowTab(Tab.Upgrades));

            BuildGloves();
            BuildUpgrades();

            _backBtn = MenuKit.Secondary(transform, "BtnBack", "НАЗАД", UIKit.FontMd);
            _backBtn.onClick.AddListener(PressBack);
            _buyBtn = MenuKit.Primary(transform, "BtnBuy", "КУПИТЬ", UIKit.FontMd);
            _buyBtn.onClick.AddListener(() => PressBuy());
            _equipBtn = MenuKit.Primary(transform, "BtnEquip", "НАДЕТЬ", UIKit.FontMd);
            _equipBtn.onClick.AddListener(() => PressEquip());

            // Надпись-отказ поверх содержимого
            _toastBox = UIKit.Panel(transform, "Toast", new Color(0.05f, 0.03f, 0.04f, 0.92f));
            _toast = UIKit.Label(_toastBox, "Lbl", UIKit.FontMd, UIKit.HpRed, semiBold: true);
            MenuKit.Place(_toast.rectTransform, 0f, 0f, 1f, 1f);
            _toastBox.gameObject.SetActive(false);
            _toast.gameObject.SetActive(false);

            SetData(new PlayerProfile());
            ShowTab(Tab.Gloves);
            Layout(false);
        }

        void BuildGloves() {
            _glovesRoot = new GameObject("Gloves", typeof(RectTransform)).GetComponent<RectTransform>();
            _glovesRoot.SetParent(transform, false);
            MenuKit.Place(_glovesRoot, 0f, 0f, 1f, 1f);

            for (int i = 0; i < GlovesCatalog.All.Length; i++) {
                var g = GlovesCatalog.All[i];
                var t = new GloveTile();
                t.btn = UIKit.Btn(_glovesRoot, "Tile" + i, "", UIKit.FontSm, UIKit.BgPanel, UIKit.TextMain);
                t.btn.GetComponentInChildren<Text>().gameObject.SetActive(false);
                t.bg = t.btn.GetComponent<Image>();
                var frame = t.btn.gameObject.AddComponent<Outline>();
                frame.effectColor = new Color(1f, 1f, 1f, 0.18f);
                frame.effectDistance = new Vector2(2f, -2f);
                var rt = t.btn.GetComponent<RectTransform>();
                t.stripA = UIKit.Panel(rt, "A", g.colorA);
                t.stripB = UIKit.Panel(rt, "B", g.colorB);
                UIKit.SetAnchors(t.stripA, 0f, 0.70f, 0.70f, 1f);
                t.stripA.offsetMin = new Vector2(6f, 0f); t.stripA.offsetMax = new Vector2(0f, -6f);
                UIKit.SetAnchors(t.stripB, 0.70f, 0.70f, 1f, 1f);
                t.stripB.offsetMin = new Vector2(0f, 0f); t.stripB.offsetMax = new Vector2(-6f, -6f);
                t.name = UIKit.Label(rt, "Name", UIKit.FontXs + 1, UIKit.TextMain, semiBold: true);
                t.name.text = g.displayName;
                MenuKit.Place(t.name.rectTransform, 0.03f, 0.36f, 0.97f, 0.70f);
                t.status = UIKit.Label(rt, "Status", UIKit.FontXs, UIKit.TextSub, semiBold: false);
                MenuKit.Place(t.status.rectTransform, 0.03f, 0.04f, 0.97f, 0.36f);
                int idx = i;
                t.btn.onClick.AddListener(() => SelectGlove(idx));
                _tiles.Add(t);
            }

            _card = UIKit.Panel(_glovesRoot, "Card", UIKit.BgPanel);
            var cf = _card.gameObject.AddComponent<Outline>();
            cf.effectColor = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.6f);
            cf.effectDistance = new Vector2(2f, -2f);

            // Место под 3D-превью: пока текстуры нет — две плашки цветов перчатки (кулак и манжета)
            _previewBox = UIKit.Panel(_card, "PreviewBox", new Color32(10, 9, 14, 255));
            _swatchA = UIKit.Panel(_previewBox, "SwA", Color.white);
            UIKit.SetAnchors(_swatchA, 0.22f, 0.38f, 0.78f, 0.86f);
            _swatchA.offsetMin = _swatchA.offsetMax = Vector2.zero;
            _swatchB = UIKit.Panel(_previewBox, "SwB", Color.white);
            UIKit.SetAnchors(_swatchB, 0.30f, 0.14f, 0.70f, 0.38f);
            _swatchB.offsetMin = _swatchB.offsetMax = Vector2.zero;
            var pgo = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            pgo.transform.SetParent(_previewBox, false);
            _preview = pgo.GetComponent<RawImage>();
            _preview.raycastTarget = false;
            MenuKit.Place(_preview.rectTransform, 0f, 0f, 1f, 1f);
            _preview.gameObject.SetActive(false);

            _gName = UIKit.Label(_card, "Name", UIKit.FontLg, UIKit.TextMain, semiBold: true, TextAnchor.MiddleLeft);
            _gDesc = UIKit.Label(_card, "Desc", UIKit.FontSm, UIKit.TextSub, semiBold: false, TextAnchor.UpperLeft);
            _gStatus = UIKit.Label(_card, "Status", UIKit.FontSm, UIKit.MegaGold, semiBold: true, TextAnchor.MiddleLeft);
            _stats = new GloveStatsView(_card);
        }

        void BuildUpgrades() {
            _upgradesRoot = new GameObject("Upgrades", typeof(RectTransform)).GetComponent<RectTransform>();
            _upgradesRoot.SetParent(transform, false);
            MenuKit.Place(_upgradesRoot, 0f, 0f, 1f, 1f);

            foreach (var t in UpgradeOrder) {
                var r = new UpgradeRow { type = t };
                r.rt = UIKit.Panel(_upgradesRoot, "Up" + t, UIKit.BgPanel);
                r.name = UIKit.Label(r.rt, "Name", UIKit.FontMd, UIKit.TextMain, semiBold: true, TextAnchor.MiddleLeft);
                r.name.text = UpgradeName(t);
                r.pips = MenuKit.Pips(r.rt, "Pips", PlayerProfile.MaxUpgradeLevel);
                r.effect = UIKit.Label(r.rt, "Effect", UIKit.FontXs + 2, UIKit.TextSub, semiBold: false, TextAnchor.MiddleLeft);
                r.btn = MenuKit.Primary(r.rt, "Btn", "УЛУЧШИТЬ", UIKit.FontSm);
                var type = t;
                r.btn.onClick.AddListener(() => PressUpgrade(type));
                _rows.Add(r);
            }
        }

        // ------------------------------------------------------------------ Данные

        public void SetData(PlayerProfile p) {
            _profile = p ?? new PlayerProfile();
            _coins.text = MenuKit.Coins(_profile.coins);
            for (int i = 0; i < _tiles.Count; i++) RefreshTile(i);
            RefreshCard();
            foreach (var r in _rows) RefreshRow(r);
        }

        // Выбрать перчатку по индексу каталога (на входе в магазин — надетую).
        public void SelectGlove(int index) {
            int n = GlovesCatalog.All.Length;
            index = Mathf.Clamp(index, 0, n - 1);
            bool changed = index != _selected;
            _selected = index;
            for (int i = 0; i < _tiles.Count; i++) RefreshTile(i);
            RefreshCard();
            if (changed) OnGloveSelected?.Invoke(SelectedGloveId);
        }

        public void SelectGlove(string id) {
            int idx = Array.FindIndex(GlovesCatalog.All, g => g.id == id);
            if (idx >= 0) SelectGlove(idx);
        }

        // Готовая картинка перчатки (RenderTexture от сборщика); null — снова плашки цветов.
        public void SetPreview(string id, Texture tex) {
            if (tex == null) _previews.Remove(id); else _previews[id] = tex;
            RefreshPreview();
        }

        void RefreshTile(int i) {
            var g = GlovesCatalog.All[i];
            var t = _tiles[i];
            bool sel = i == _selected;
            t.bg.color = sel ? UIKit.AccentDim : UIKit.BgPanel;
            if (_profile.equippedGloveId == g.id) { t.status.text = "надето"; t.status.color = UIKit.HpGreen; }
            else if (_profile.OwnsGlove(g.id)) { t.status.text = "куплено"; t.status.color = UIKit.HpGreen; }
            else if (!GameFlow.IsGloveUnlocked(g, _profile)) { t.status.text = "закрыто"; t.status.color = UIKit.TextSub; }
            else { t.status.text = MenuKit.Coins(g.price); t.status.color = UIKit.MegaGold; }
        }

        void RefreshCard() {
            var g = GlovesCatalog.All[_selected];
            _gName.text = g.displayName;
            _gDesc.text = GloveDescription(g);
            _stats.Set(g);
            _swatchA.GetComponent<Image>().color = g.colorA;
            _swatchB.GetComponent<Image>().color = g.colorB;
            RefreshPreview();

            bool owned = _profile.OwnsGlove(g.id);
            bool equipped = _profile.equippedGloveId == g.id;
            bool unlocked = GameFlow.IsGloveUnlocked(g, _profile);
            if (equipped) { _gStatus.text = "НАДЕТО"; _gStatus.color = UIKit.HpGreen; }
            else if (owned) { _gStatus.text = "КУПЛЕНО"; _gStatus.color = UIKit.HpGreen; }
            else if (!unlocked) { _gStatus.text = UnlockText(GameFlow.GloveUnlockLadder(g)); _gStatus.color = UIKit.TextSub; }
            else { _gStatus.text = "Цена: " + MenuKit.Coins(g.price); _gStatus.color = UIKit.MegaGold; }

            bool gloves = _tab == Tab.Gloves;
            _buyBtn.gameObject.SetActive(gloves && !owned);
            _equipBtn.gameObject.SetActive(gloves && owned);
            if (!owned) {
                MenuKit.SetCaption(_buyBtn, unlocked ? "КУПИТЬ · " + g.price : "ЗАКРЫТО");
                MenuKit.SetActionState(_buyBtn, unlocked);
            } else {
                MenuKit.SetCaption(_equipBtn, equipped ? "НАДЕТО" : "НАДЕТЬ");
                MenuKit.SetActionState(_equipBtn, !equipped);
            }
        }

        void RefreshPreview() {
            bool has = _previews.TryGetValue(SelectedGloveId, out var tex) && tex != null;
            _preview.texture = has ? tex : null;
            _preview.gameObject.SetActive(has);
            _swatchA.gameObject.SetActive(!has);
            _swatchB.gameObject.SetActive(!has);
        }

        void RefreshRow(UpgradeRow r) {
            int lv = _profile.upgradeLevels[(int)r.type];
            MenuKit.SetPips(r.pips, lv);
            if (lv >= PlayerProfile.MaxUpgradeLevel) {
                r.effect.text = "Максимум: " + UpgradeEffect(r.type, GameFlow.UpgradeMult(r.type, lv));
                MenuKit.SetCaption(r.btn, "МАКС.");
                MenuKit.SetActionState(r.btn, false);
            } else {
                r.effect.text = "Дальше: " + UpgradeEffect(r.type, GameFlow.UpgradeMult(r.type, lv + 1));
                MenuKit.SetCaption(r.btn, "УЛУЧШИТЬ\n" + MenuKit.Coins(PlayerProfile.UpgradeCosts[lv]));
                MenuKit.SetActionState(r.btn, true);
            }
        }

        // ------------------------------------------------------------------ Вкладки и нажатия (публичны для сквозного прохода)

        public void ShowTab(Tab tab) {
            _tab = tab;
            bool gloves = tab == Tab.Gloves;
            _glovesRoot.gameObject.SetActive(gloves);
            _upgradesRoot.gameObject.SetActive(!gloves);
            MenuKit.SetToggle(_tabGloves, gloves);
            MenuKit.SetToggle(_tabUpgrades, !gloves);
            RefreshCard();
        }

        public void PressTab(int tab) => ShowTab(tab == 0 ? Tab.Gloves : Tab.Upgrades);

        public ShopResult PressBuy() {
            if (OnBuyGlove == null) return ShopResult.Unavailable;
            var r = OnBuyGlove(SelectedGloveId);
            AfterAction(r);
            return r;
        }

        public ShopResult PressEquip() {
            if (OnEquipGlove == null) return ShopResult.Unavailable;
            var r = OnEquipGlove(SelectedGloveId);
            AfterAction(r);
            return r;
        }

        public ShopResult PressUpgrade(UpgradeType t) {
            if (OnBuyUpgrade == null) return ShopResult.Unavailable;
            var r = OnBuyUpgrade(t);
            AfterAction(r);
            return r;
        }

        public void PressBack() => OnBack?.Invoke();

        // Обновить карточки; отказ — короткой надписью.
        void AfterAction(ShopResult r) {
            SetData(_profile);
            string msg = null;
            switch (r) {
                case ShopResult.NotEnoughCoins: msg = "Не хватает монет"; break;
                case ShopResult.Locked:         msg = "Пока закрыто"; break;
                case ShopResult.MaxLevel:       msg = "Уже максимум"; break;
            }
            if (msg != null) {
                MenuKit.ShowToast(_toast, msg, ref _toastTimer);
                _toastBox.gameObject.SetActive(true);
            }
        }

        // ------------------------------------------------------------------ Раскладка

        public void OnLayout(bool landscape) => Layout(landscape);

        public void Layout(bool landscape) {
            var back = _backBtn.GetComponent<RectTransform>();
            var buy = _buyBtn.GetComponent<RectTransform>();
            var equip = _equipBtn.GetComponent<RectTransform>();
            if (landscape) {
                MenuKit.Place(_header.rectTransform, 0.02f, 0.80f, 0.30f, 0.995f);
                MenuKit.Place(_tabGloves.GetComponent<RectTransform>(),   0.31f, 0.80f, 0.52f, 0.995f);
                MenuKit.Place(_tabUpgrades.GetComponent<RectTransform>(), 0.53f, 0.80f, 0.74f, 0.995f);
                MenuKit.Place(_coins.rectTransform,  0.75f, 0.80f, 0.98f, 0.995f);
                MenuKit.Place(_line,                 0.02f, 0.785f, 0.98f, 0.790f);
                MenuKit.Place(back,  0.02f, 0.015f, 0.22f, 0.205f);
                MenuKit.Place(buy,   0.62f, 0.015f, 0.98f, 0.205f);
                MenuKit.Place(equip, 0.62f, 0.015f, 0.98f, 0.205f);
                MenuKit.Place(_toastBox, 0.24f, 0.030f, 0.60f, 0.190f);

                // перчатки: плитки сеткой слева, карточка справа
                PlaceTiles(0.02f, 0.235f, 0.46f, 0.765f);
                MenuKit.Place(_card, 0.48f, 0.235f, 0.98f, 0.765f);
                MenuKit.Place(_previewBox,          0.02f, 0.05f, 0.30f, 0.95f);
                MenuKit.Place(_gName.rectTransform, 0.33f, 0.80f, 0.98f, 0.97f);
                MenuKit.Place(_gDesc.rectTransform, 0.33f, 0.58f, 0.98f, 0.80f);
                MenuKit.Place(_gStatus.rectTransform, 0.33f, 0.48f, 0.98f, 0.58f);
                MenuKit.Place(_stats.Root,          0.33f, 0.03f, 0.97f, 0.47f);

                // улучшения: пять столбцов
                for (int i = 0; i < _rows.Count; i++) {
                    var r = _rows[i];
                    float x0 = 0.02f + i * 0.194f;
                    MenuKit.Place(r.rt, x0, 0.235f, x0 + 0.184f, 0.765f);
                    r.name.fontSize = UIKit.FontSm;
                    r.name.alignment = TextAnchor.MiddleCenter;
                    r.effect.alignment = TextAnchor.MiddleCenter;
                    MenuKit.Place(r.name.rectTransform, 0.04f, 0.82f, 0.96f, 0.98f);
                    MenuKit.Place(MenuKit.PipsRect(r.pips), 0.14f, 0.68f, 0.90f, 0.80f);
                    MenuKit.Place(r.effect.rectTransform, 0.05f, 0.42f, 0.95f, 0.66f);
                    MenuKit.Place(r.btn.GetComponent<RectTransform>(), 0.06f, 0.03f, 0.94f, 0.40f);
                }
            } else {
                MenuKit.Place(_header.rectTransform, 0.05f, 0.935f, 0.60f, 0.998f);
                MenuKit.Place(_coins.rectTransform,  0.50f, 0.935f, 0.95f, 0.998f);
                MenuKit.Place(_line,                 0.05f, 0.929f, 0.95f, 0.933f);
                MenuKit.Place(_tabGloves.GetComponent<RectTransform>(),   0.03f, 0.828f, 0.495f, 0.920f);
                MenuKit.Place(_tabUpgrades.GetComponent<RectTransform>(), 0.505f, 0.828f, 0.97f, 0.920f);
                MenuKit.Place(back,  0.03f, 0.012f, 0.33f, 0.125f);
                MenuKit.Place(buy,   0.36f, 0.012f, 0.97f, 0.125f);
                MenuKit.Place(equip, 0.36f, 0.012f, 0.97f, 0.125f);
                MenuKit.Place(_toastBox, 0.08f, 0.33f, 0.92f, 0.40f);

                PlaceTiles(0.03f, 0.622f, 0.97f, 0.815f);
                MenuKit.Place(_card, 0.03f, 0.140f, 0.97f, 0.610f);
                MenuKit.Place(_previewBox,          0.03f, 0.52f, 0.40f, 0.97f);
                MenuKit.Place(_gName.rectTransform, 0.43f, 0.84f, 0.97f, 0.97f);
                MenuKit.Place(_gDesc.rectTransform, 0.43f, 0.62f, 0.97f, 0.84f);
                MenuKit.Place(_gStatus.rectTransform, 0.43f, 0.52f, 0.97f, 0.62f);
                MenuKit.Place(_stats.Root,          0.05f, 0.03f, 0.95f, 0.49f);

                for (int i = 0; i < _rows.Count; i++) {
                    var r = _rows[i];
                    float y1 = 0.815f - i * 0.136f;
                    MenuKit.Place(r.rt, 0.03f, y1 - 0.128f, 0.97f, y1);
                    r.name.fontSize = UIKit.FontMd;
                    r.name.alignment = TextAnchor.MiddleLeft;
                    r.effect.alignment = TextAnchor.MiddleLeft;
                    MenuKit.Place(r.name.rectTransform, 0.04f, 0.62f, 0.60f, 0.95f);
                    MenuKit.Place(MenuKit.PipsRect(r.pips), 0.04f, 0.40f, 0.36f, 0.60f);
                    MenuKit.Place(r.effect.rectTransform, 0.04f, 0.04f, 0.62f, 0.38f);
                    MenuKit.Place(r.btn.GetComponent<RectTransform>(), 0.63f, 0.10f, 0.97f, 0.90f);
                }
            }
        }

        // Плитки перчаток сеткой TileCols столбцов в прямоугольнике (доли safe-слоя).
        void PlaceTiles(float x0, float y0, float x1, float y1) {
            int rows = (_tiles.Count + TileCols - 1) / TileCols;
            float cw = (x1 - x0) / TileCols, rh = (y1 - y0) / Mathf.Max(1, rows);
            for (int i = 0; i < _tiles.Count; i++) {
                int col = i % TileCols, row = i / TileCols;
                var rt = _tiles[i].btn.GetComponent<RectTransform>();
                UIKit.SetAnchors(rt, x0 + col * cw, y1 - (row + 1) * rh, x0 + (col + 1) * cw, y1 - row * rh);
                rt.offsetMin = new Vector2(3f, 3f);
                rt.offsetMax = new Vector2(-3f, -3f);
            }
        }

        // ------------------------------------------------------------------ Tick

        public void Tick(float dt, float udt) {
            if (_toastTimer <= 0f) return;
            MenuKit.TickToast(_toast, ref _toastTimer, udt);
            if (_toastTimer <= 0f) _toastBox.gameObject.SetActive(false);
        }
    }
}
