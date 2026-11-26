// RosterScreen.cs — экран лестницы соперников: список карточек, подробная карточка выбранного, «В БОЙ» / «НАЗАД».
// Без Update и без логики в Awake/Start: Init → SetData → Tick(dt, udt) от владельца.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class RosterScreen : MonoBehaviour, ILayoutHandler {

        const float Gap = 10f;        // зазор между строками, опорные px
        const float SideInset = 6f;   // отступ строк от краёв списка (место под рамку выделения)
        const float PulseRate = 4f;   // пульс рамки выделения, рад/с

        // ------------------------------------------------------------------ Колбэки

        public Action<int> OnFight;    // индекс бойца в RosterDef.Fighters
        public Action OnBack;
        public Action<int> OnSelect;   // выбор сменился — можно перерисовать портрет и отдать его в SetPortrait

        // ------------------------------------------------------------------ Элементы

        Text _header, _progress;
        RectTransform _line;
        RectTransform _list, _viewport, _content;
        ScrollRect _scroll;
        FighterCard _detail;
        Button _fightBtn, _backBtn;
        Image _fightImg;
        Text _fightLbl;
        Outline _fightOutline;

        readonly List<FighterCard> _cards = new List<FighterCard>();
        RosterDef _roster;
        PlayerProfile _profile;
        int _selected = -1;
        bool _landscape, _revealPending;
        float _pulseT;

        public int Selected => _selected;
        public int Count => _cards.Count;

        // ------------------------------------------------------------------ Инициализация

        public void Init(UIRoot root, Action<int> onFight = null, Action onBack = null) {
            if (onFight != null) OnFight = onFight;
            if (onBack != null) OnBack = onBack;
            BuildUI();
            // регистрация после сборки: UIRoot сразу раскладывает экран под текущую ориентацию
            root.Register(ScreenId.FighterSelect, gameObject);
        }

        void BuildUI() {
            var selfRt = gameObject.GetComponent<RectTransform>();
            if (selfRt == null) selfRt = gameObject.AddComponent<RectTransform>();
            UIKit.SetAnchors(selfRt, 0f, 0f, 1f, 1f);
            selfRt.offsetMin = selfRt.offsetMax = Vector2.zero;

            // фон шире safe-area: закрывает экран до краёв, под вырезом и полосой жестов
            var bg = UIKit.Panel(transform, "Bg", UIKit.BgDark);
            UIKit.SetAnchors(bg, 0f, 0f, 1f, 1f);
            bg.offsetMin = new Vector2(-400f, -400f);
            bg.offsetMax = new Vector2(400f, 400f);

            _header = UIKit.Label(transform, "Header", UIKit.FontXl, UIKit.Accent, semiBold: true, TextAnchor.MiddleLeft);
            _header.text = "ЛЕСТНИЦА";
            _line = MenuKit.Rule(transform);
            _progress = UIKit.Label(transform, "Progress", UIKit.FontSm, UIKit.TextSub, semiBold: false, TextAnchor.MiddleRight);

            // Прокручиваемый список: Viewport с RectMask2D (без трафарета), строки раскладываются вручную —
            // без LayoutGroup, чтобы раскладка не зависела от прохода перестройки холста вне Play Mode.
            _list = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
            _list.SetParent(transform, false);
            _scroll = _list.gameObject.AddComponent<ScrollRect>();

            _viewport = UIKit.Panel(_list, "Viewport", new Color(0f, 0f, 0f, 0f));
            _viewport.GetComponent<Image>().raycastTarget = true;   // ловит перетаскивание в зазорах между строками
            _viewport.gameObject.AddComponent<RectMask2D>();
            Place(_viewport, 0f, 0f, 1f, 1f);

            _content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            _content.SetParent(_viewport, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(0f, 0f);
            _content.anchoredPosition = Vector2.zero;

            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.135f;
            _scroll.scrollSensitivity = 40f;

            _detail = FighterCard.CreateDetail(transform);

            _fightBtn = UIKit.Btn(transform, "BtnFight", "В БОЙ", UIKit.FontXl, UIKit.Accent, UIKit.BgDark);
            _fightBtn.onClick.AddListener(PressFight);
            _fightImg = _fightBtn.GetComponent<Image>();
            _fightLbl = _fightBtn.GetComponentInChildren<Text>();
            _fightOutline = _fightBtn.GetComponentInChildren<Text>().GetComponent<Outline>();
            _fightOutline.enabled = false;   // тёмная надпись на светлой кнопке: обводка её только мажет
            var fc = _fightBtn.colors;
            fc.disabledColor = Color.white;  // закрытое состояние красим сами
            _fightBtn.colors = fc;

            _backBtn = UIKit.Btn(transform, "BtnBack", "НАЗАД", UIKit.FontMd, UIKit.BgPanel, UIKit.TextMain);
            _backBtn.onClick.AddListener(PressBack);

            Layout(false);
            RefreshFightButton();
        }

        // ------------------------------------------------------------------ Данные

        // Лестница и прогресс игрока: бойцы до ladderIndex побеждены, ladderIndex — следующий, дальше закрыты.
        // Выбор встаёт на следующего (или на последнего, если лестница пройдена).
        public void SetData(RosterDef roster, PlayerProfile profile) {
            _roster = roster;
            _profile = profile;
            int n = roster != null && roster.Fighters != null ? roster.Fighters.Length : 0;

            if (_cards.Count != n) {
                foreach (var c in _cards) c.Destroy();
                _cards.Clear();
                for (int i = 0; i < n; i++) {
                    var card = FighterCard.CreateRow(_content, i, Select);
                    float top = Gap + i * (FighterCard.RowHeight + Gap);
                    var rt = card.Root;
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.offsetMin = new Vector2(SideInset, -(top + FighterCard.RowHeight));
                    rt.offsetMax = new Vector2(-SideInset, -top);
                    _cards.Add(card);
                }
                _content.sizeDelta = new Vector2(0f, Gap + n * (FighterCard.RowHeight + Gap));
                _content.anchoredPosition = Vector2.zero;
            }

            int defeated = 0;
            for (int i = 0; i < n; i++) {
                var st = StateOf(i);
                if (st == FighterCardState.Defeated) defeated++;
                _cards[i].Set(roster.Fighters[i], i, i + 1, n, st);
            }
            _progress.text = n > 0 && defeated >= n ? "Лестница пройдена!" : "Побеждено: " + defeated + " из " + n;

            _selected = -1;
            if (n > 0) Select(Mathf.Min(profile != null ? Mathf.Max(0, profile.ladderIndex) : 0, n - 1));
            else RefreshFightButton();
        }

        public FighterCardState StateOf(int index) {
            int ladder = _profile != null ? _profile.ladderIndex : 0;
            if (index < ladder) return FighterCardState.Defeated;
            return index == ladder ? FighterCardState.Next : FighterCardState.Locked;
        }

        // Драться можно со следующим и с уже побеждёнными (реванш).
        public bool IsAvailable(int index) =>
            index >= 0 && index < _cards.Count && StateOf(index) != FighterCardState.Locked;

        // ------------------------------------------------------------------ Выбор и кнопки (публичны для сквозного прохода)

        public void Select(int index) {
            int n = _cards.Count;
            if (n == 0) return;
            index = Mathf.Clamp(index, 0, n - 1);
            bool changed = index != _selected;
            _selected = index;

            for (int i = 0; i < n; i++) _cards[i].SetSelected(i == index);
            _detail.Set(_roster.Fighters[index], index, index + 1, n, StateOf(index));
            RefreshFightButton();
            _revealPending = true;
            Reveal();
            if (changed) OnSelect?.Invoke(index);
        }

        public void PressFight() {
            if (IsAvailable(_selected)) OnFight?.Invoke(_selected);
        }

        public void PressBack() => OnBack?.Invoke();

        // Портрет выбранного бойца (RenderTexture, отрендеренная сборщиком); null — заглушка.
        public void SetPortrait(Texture tex) => _detail.SetPortrait(tex);

        void RefreshFightButton() {
            bool ok = IsAvailable(_selected);
            _fightBtn.interactable = ok;
            _fightImg.color = ok ? UIKit.Accent : UIKit.BgPanelLt;
            _fightLbl.color = ok ? UIKit.BgDark : UIKit.TextSub;
            _fightLbl.text  = ok ? "В БОЙ" : "ЗАКРЫТ";
            _fightOutline.enabled = !ok;
        }

        // Прокрутить так, чтобы выбранная строка была видна целиком. Пока у списка нет размера — отложить до Tick.
        void Reveal() {
            if (!_revealPending || _selected < 0) return;
            float vh = _viewport.rect.height;
            if (vh <= 0f) return;
            _revealPending = false;

            float total = _content.sizeDelta.y;
            float top = Gap * 0.5f + _selected * (FighterCard.RowHeight + Gap);
            float bottom = top + FighterCard.RowHeight + Gap;
            float pos = _content.anchoredPosition.y;
            if (top < pos) pos = top;
            else if (bottom > pos + vh) pos = bottom - vh;
            pos = Mathf.Clamp(pos, 0f, Mathf.Max(0f, total - vh));
            _scroll.StopMovement();
            _content.anchoredPosition = new Vector2(0f, pos);
        }

        // ------------------------------------------------------------------ Раскладка

        public void OnLayout(bool landscape) => Layout(landscape);

        // Портрет: заголовок, список в один столбец, подробная карточка, кнопки внизу.
        // Ландшафт: список слева, подробная карточка справа, под ней кнопки.
        public void Layout(bool landscape) {
            _landscape = landscape;
            if (landscape) {
                Place(_header.rectTransform,   0.03f, 0.885f, 0.60f, 0.995f);
                Place(_progress.rectTransform, 0.60f, 0.885f, 0.97f, 0.995f);
                Place(_line,                   0.03f, 0.876f, 0.97f, 0.881f);
                Place(_list,                   0.02f, 0.020f, 0.49f, 0.866f);
                Place(_detail.Root,            0.51f, 0.290f, 0.98f, 0.862f);
                Place(_backBtn.GetComponent<RectTransform>(),  0.51f, 0.025f, 0.66f, 0.255f);
                Place(_fightBtn.GetComponent<RectTransform>(), 0.68f, 0.025f, 0.98f, 0.255f);
            } else {
                Place(_header.rectTransform,   0.05f, 0.935f, 0.95f, 0.998f);
                Place(_progress.rectTransform, 0.05f, 0.935f, 0.95f, 0.998f);
                Place(_line,                   0.05f, 0.929f, 0.95f, 0.933f);
                Place(_list,                   0.03f, 0.385f, 0.97f, 0.925f);
                Place(_detail.Root,            0.03f, 0.172f, 0.97f, 0.375f);
                Place(_backBtn.GetComponent<RectTransform>(),  0.03f, 0.012f, 0.33f, 0.152f);
                Place(_fightBtn.GetComponent<RectTransform>(), 0.36f, 0.012f, 0.97f, 0.152f);
            }
            _detail.Layout(landscape);
            _revealPending = true;
            Reveal();
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1) {
            UIKit.SetAnchors(rt, x0, y0, x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------ Tick

        public void Tick(float dt, float udt) {
            if (!gameObject.activeInHierarchy) return;
            _pulseT += udt;
            if (_pulseT > 1000f) _pulseT -= 1000f;
            float k = 0.5f + 0.5f * Mathf.Sin(_pulseT * PulseRate);
            if (_selected >= 0 && _selected < _cards.Count) _cards[_selected].SetPulse(k);
            _detail.FitPortrait();
            if (_revealPending) Reveal();
        }
    }
}
