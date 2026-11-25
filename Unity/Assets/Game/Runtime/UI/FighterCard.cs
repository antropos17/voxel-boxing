// FighterCard.cs — карточка бойца лестницы: строка списка или подробная карточка с портретом.
// Не MonoBehaviour: создаётся и раскладывается из RosterScreen.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public enum FighterCardState { Defeated, Next, Locked }

    public class FighterCard {

        public const float RowHeight = 140f;   // высота строки списка, опорные px

        static readonly string[] StatNames = { "сила", "скорость", "стойкость" };
        static readonly Color[]  StatCols  = { UIKit.HpRed, UIKit.StamBlue, UIKit.HpGreen };

        static readonly Color StepDefeated = new Color32(40, 92, 50, 255);
        static readonly Color ChipFrame    = new Color32(8, 8, 12, 255);
        static readonly Color PortraitBg   = new Color32(20, 18, 26, 255);

        public RectTransform Root { get; private set; }
        public int Index { get; private set; }
        public FighterCardState State { get; private set; }

        readonly bool _detail;

        RectTransform _selFrame;
        Image _selImg, _bg;
        RectTransform _stepRt;
        Image _stepBg;
        Text _step, _name, _sub, _info, _state;
        RectTransform _chipFrame;
        Image _chip;
        readonly Text[] _statLbl = new Text[3];
        readonly Text[] _statVal = new Text[3];
        readonly RectTransform[] _statBg = new RectTransform[3];
        readonly RectTransform[] _statFill = new RectTransform[3];

        // только у подробной карточки
        RectTransform _portraitFrame, _ribbon;
        RawImage _portrait;
        Text _portraitHint;
        Vector2 _fitSize;
        Texture _fitTex;

        FighterCard(bool detail) { _detail = detail; }

        // ------------------------------------------------------------------ Создание

        // Строка списка. Высота RowHeight, ширину и место задаёт владелец. Нажатие → onTap(index).
        public static FighterCard CreateRow(Transform parent, int index, Action<int> onTap) {
            var c = new FighterCard(false) { Index = index };
            c.BuildCommon(parent, "Card" + index);

            var btn = c._bg.gameObject.AddComponent<Button>();
            c._bg.raycastTarget = true;
            btn.targetGraphic = c._bg;
            var cb = btn.colors;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.1f, 1f);
            cb.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(() => onTap?.Invoke(c.Index));

            c.LayoutRow();
            return c;
        }

        // Подробная карточка с местом под портрет (RawImage).
        public static FighterCard CreateDetail(Transform parent) {
            var c = new FighterCard(true) { Index = -1 };
            c.BuildCommon(parent, "Detail");
            c.Layout(false);
            return c;
        }

        void BuildCommon(Transform parent, string name) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Root = go.GetComponent<RectTransform>();

            // рамка выделения позади фона (видна полосой по краю)
            _selFrame = UIKit.Panel(Root, "Sel", UIKit.Accent);
            _selImg = _selFrame.GetComponent<Image>();
            UIKit.SetAnchors(_selFrame, 0f, 0f, 1f, 1f);
            _selFrame.offsetMin = new Vector2(-3f, -3f);
            _selFrame.offsetMax = new Vector2(3f, 3f);
            _selFrame.gameObject.SetActive(_detail);

            var bgRt = UIKit.Panel(Root, "Bg", _detail ? UIKit.BgPanelLt : UIKit.BgPanel);
            _bg = bgRt.GetComponent<Image>();
            Place(bgRt, 0f, 0f, 1f, 1f);

            if (_detail) {
                _portraitFrame = UIKit.Panel(bgRt, "PortraitFrame", PortraitBg);
                _portraitHint = UIKit.Label(_portraitFrame, "Hint", 96, UIKit.BgPanelLt, semiBold: true);
                Place(_portraitHint.rectTransform, 0f, 0f, 1f, 1f);
                _portraitHint.text = "?";
                var pgo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage));
                pgo.transform.SetParent(_portraitFrame, false);
                _portrait = pgo.GetComponent<RawImage>();
                _portrait.raycastTarget = false;
                Place(_portrait.rectTransform, 0f, 0f, 1f, 1f, 3f);
                _portrait.enabled = false;
                // лента состояния поверх низа портрета
                _ribbon = UIKit.Panel(_portraitFrame, "Ribbon", new Color(0f, 0f, 0f, 0.6f));
                Place(_ribbon, 0f, 0f, 1f, 0.17f);
                _state = UIKit.Label(_ribbon, "State", UIKit.FontSm, UIKit.TextMain, semiBold: true);
                Place(_state.rectTransform, 0f, 0f, 1f, 1f, 2f);
            } else {
                var stepRt = UIKit.Panel(bgRt, "StepBg", UIKit.BgPanelLt);
                _stepBg = stepRt.GetComponent<Image>();
                _stepRt = stepRt;
                _step = UIKit.Label(stepRt, "Step", UIKit.FontLg, UIKit.TextMain, semiBold: true);
                Place(_step.rectTransform, 0f, 0f, 1f, 1f);
                _state = UIKit.Label(bgRt, "State", UIKit.FontXs, UIKit.TextMain, semiBold: true, TextAnchor.MiddleRight);
            }

            _name = UIKit.Label(bgRt, "Name", _detail ? UIKit.FontLg - 6 : UIKit.FontMd, UIKit.TextMain,
                                semiBold: true, TextAnchor.MiddleLeft);
            _sub = UIKit.Label(bgRt, "Sub", _detail ? UIKit.FontSm : UIKit.FontXs, UIKit.TextSub,
                               semiBold: false, TextAnchor.MiddleLeft);
            if (_detail)
                _info = UIKit.Label(bgRt, "Info", UIKit.FontXs, UIKit.TextSub, semiBold: false, TextAnchor.MiddleLeft);

            _chipFrame = UIKit.Panel(bgRt, "ChipFrame", ChipFrame);
            var chipRt = UIKit.Panel(_chipFrame, "Chip", Color.white);
            Place(chipRt, 0f, 0f, 1f, 1f, 2f);
            _chip = chipRt.GetComponent<Image>();

            for (int i = 0; i < 3; i++) {
                _statLbl[i] = UIKit.Label(bgRt, "StatLbl" + i, UIKit.FontXs, UIKit.TextSub,
                                          semiBold: false, TextAnchor.MiddleLeft);
                _statLbl[i].text = StatNames[i];
                (_statBg[i], _statFill[i]) = UIKit.Bar(bgRt, "Stat" + i, new Color32(10, 10, 14, 200), StatCols[i]);
                if (_detail)
                    _statVal[i] = UIKit.Label(bgRt, "StatVal" + i, UIKit.FontXs, UIKit.TextMain,
                                              semiBold: true, TextAnchor.MiddleRight);
            }
        }

        // ------------------------------------------------------------------ Раскладка

        // Строка: [ступень] имя ……… состояние / «прозвище» · стиль ……… чип / три полоски с подписями.
        void LayoutRow() {
            Place(_stepRt, 0f, 0f, 0.15f, 1f, 6f);
            Place(_name.rectTransform,  0.18f, 0.60f, 0.68f, 0.96f);
            Place(_state.rectTransform, 0.66f, 0.60f, 0.97f, 0.96f);
            Place(_sub.rectTransform,   0.18f, 0.38f, 0.88f, 0.62f);
            Place(_chipFrame,           0.90f, 0.41f, 0.97f, 0.59f);
            const float x0 = 0.18f, x1 = 0.97f, gap = 0.03f;
            float w = (x1 - x0 - 2f * gap) / 3f;
            for (int i = 0; i < 3; i++) {
                float a = x0 + i * (w + gap);
                Place(_statLbl[i].rectTransform, a, 0.19f, a + w, 0.38f);
                Place(_statBg[i], a, 0.07f, a + w, 0.17f);
            }
        }

        // Подробная карточка: портрет слева, сведения справа. В ландшафте карточка выше — портрет шире.
        public void Layout(bool landscape) {
            if (!_detail) return;
            float px = landscape ? 0.40f : 0.34f;
            Place(_portraitFrame, 0f, 0f, px, 1f, 8f);
            float ix = px + 0.03f;
            float X(float f) => ix + f * (0.97f - ix);
            Place(_name.rectTransform, X(0f), 0.76f, X(0.86f), 0.98f);
            Place(_chipFrame,          X(0.88f), 0.81f, X(1f), 0.93f);
            Place(_sub.rectTransform,  X(0f), 0.62f, X(1f), 0.77f);
            Place(_info.rectTransform, X(0f), 0.49f, X(1f), 0.62f);
            float rowH = 0.14f;
            for (int i = 0; i < 3; i++) {
                float y1 = 0.46f - i * rowH, y0 = y1 - rowH;
                Place(_statLbl[i].rectTransform, X(0f), y0, X(0.38f), y1);
                Place(_statBg[i], X(0.40f), y0 + rowH * 0.3f, X(0.84f), y1 - rowH * 0.3f);
                Place(_statVal[i].rectTransform, X(0.86f), y0, X(1f), y1);
            }
            _fitSize = Vector2.zero;   // пересчитать обрезку портрета
        }

        // ------------------------------------------------------------------ Данные

        // step — номер ступени с 1, total — ступеней всего.
        public void Set(in FighterDef f, int index, int step, int total, FighterCardState state) {
            Index = index;
            State = state;
            bool locked = state == FighterCardState.Locked;

            _name.text  = f.name;
            _name.color = locked ? UIKit.TextSub : UIKit.TextMain;
            string style = CardPart(f.cardText, 1);
            _sub.text = string.IsNullOrEmpty(style) ? "«" + f.nickname + "»" : "«" + f.nickname + "» · " + style;
            if (_info != null) {
                string hp = CardPart(f.cardText, 2);
                _info.text = "Ступень " + step + "/" + total + " · Ур. " + f.level + (hp.Length > 0 ? " · " + hp : "");
            }

            _chip.color = f.colors.trunks;

            int[] v = { f.stats.damage, f.stats.speed, f.stats.resilience };
            for (int i = 0; i < 3; i++) {
                UIKit.SetBarH(_statFill[i], v[i] / 10f);
                var c = StatCols[i];
                if (locked) c.a = 0.45f;
                _statFill[i].GetComponent<Image>().color = c;
                if (_statVal[i] != null) _statVal[i].text = v[i] + "/10";
            }

            switch (state) {
                case FighterCardState.Defeated:
                    _state.text = "✓ ПОБЕЖДЁН";   // ✓ есть в Inter, ✔ нет
                    _state.color = UIKit.HpGreen;
                    break;
                case FighterCardState.Next:
                    _state.text = "СЛЕДУЮЩИЙ";
                    _state.color = UIKit.Accent;
                    break;
                default:
                    _state.text = "ЗАКРЫТ";
                    _state.color = UIKit.TextSub;
                    break;
            }

            if (_step != null) {
                _step.text = step.ToString();
                _stepBg.color = state == FighterCardState.Next ? UIKit.Accent
                              : state == FighterCardState.Defeated ? StepDefeated : UIKit.BgPanelLt;
                _step.color = state == FighterCardState.Next ? UIKit.BgDark
                            : locked ? UIKit.TextSub : UIKit.TextMain;
                var o = _step.GetComponent<Outline>();
                if (o != null) o.enabled = state != FighterCardState.Next;   // тёмная цифра на светлом без обводки
            }
            if (!_detail) _bg.color = locked ? new Color(UIKit.BgPanel.r, UIKit.BgPanel.g, UIKit.BgPanel.b, 0.55f) : UIKit.BgPanel;
        }

        public void SetSelected(bool on) {
            if (!_detail) _selFrame.gameObject.SetActive(on);
        }

        // Пульс рамки выделения (0..1), зовётся из Tick владельца — без аллокаций.
        public void SetPulse(float k) {
            var c = UIKit.Accent;
            c.a = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(k));
            _selImg.color = c;
        }

        // Отрендеренный портрет бойца (RenderTexture или Texture2D); null — заглушка «?».
        public void SetPortrait(Texture tex) {
            if (!_detail) return;
            _portrait.texture = tex;
            _portrait.enabled = tex != null;
            _portraitHint.enabled = tex == null;
            _fitTex = null;
            FitPortrait();
        }

        // Обрезка портрета под рамку без растяжения: лишнее по ширине срезается поровну,
        // по высоте — снизу (голова остаётся в кадре). Пересчёт только при смене размера рамки или текстуры.
        public void FitPortrait() {
            if (!_detail || _portrait.texture == null) return;
            var size = _portrait.rectTransform.rect.size;
            var tex = _portrait.texture;
            if (size == _fitSize && tex == _fitTex) return;
            _fitSize = size; _fitTex = tex;
            if (size.x <= 0f || size.y <= 0f || tex.width <= 0 || tex.height <= 0) return;
            float frameA = size.x / size.y, texA = (float)tex.width / tex.height;
            if (texA > frameA) {
                float uw = frameA / texA;
                _portrait.uvRect = new Rect(0.5f - uw * 0.5f, 0f, uw, 1f);
            } else {
                float uh = texA / frameA;
                _portrait.uvRect = new Rect(0f, 1f - uh, 1f, uh);
            }
        }

        public void Destroy() {
            if (Root != null) UIKit.Destroy(Root.gameObject);
            Root = null;
        }

        // ------------------------------------------------------------------ Вспомогательное

        // Часть визитки «"прозвище" · стиль · HP N · Ур.L» по номеру (1 — стиль, 2 — HP).
        static string CardPart(string cardText, int idx) {
            if (string.IsNullOrEmpty(cardText)) return "";
            var parts = cardText.Split('·');
            return idx < parts.Length ? parts[idx].Trim() : "";
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1, float inset = 0f) {
            UIKit.SetAnchors(rt, x0, y0, x1, y1);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
