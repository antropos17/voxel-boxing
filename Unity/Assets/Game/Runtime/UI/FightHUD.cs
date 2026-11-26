// FightHUD.cs — HUD боя (UI-MOTION.md). Основа — макет шкал Сеймура (docs/mockups/hud-bars, варианты В–Е).
// Не имеет Update(). Работает через Refresh(HudData) + Tick(dt,udt) из FightDirector. Не зависит от FightState.
// Слева — игрок (кость), справа — соперник (синий), посередине — время. Под здоровьем игрока — заряды выносливости
// (один кубик — один удар) и мега-шкала с накалом; под здоровьем соперника — оглушение и отметки нокдаунов.
// Движение: след урона (снятое на миг светлеет и догоняет), толчок и тряска панели по силе удара, летящие цифры урона
// (подряд складываются), пульс при малом здоровье, выезд шкал в начале боя и уход в конце, счёт рефери, «МЕГА ГОТОВ».
// Все пулы — фиксированного размера: в установившемся бою HUD не выделяет память. UIMotion.Reduced гасит тряску и вспышки.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public partial class FightHUD : MonoBehaviour, ILayoutHandler {

        // ------------------------------------------------------------------ Пределы и тайминги

        public const int   MaxFloaters = 6;      // летящих цифр одновременно
        public const int   MaxSparks   = 40;     // искр, углей и золотой пыли одновременно
        public const int   MaxPips     = 12;     // делений выносливости
        public const int   KdPips      = 3;      // третий нокдаун заканчивает бой
        public const float TrailHold   = 0.32f;  // след стоит, с
        public const float TrailCatch  = 0.30f;  // и догоняет значение за, с
        public const float TrailFlash  = 0.10f;  // снятый кусок первые мгновения белый
        public const float JoltTime    = 0.32f;  // толчок панели
        public const float FloatLife   = 0.95f;  // цифра урона живёт
        public const float FloatMerge  = 0.70f;  // удар в это окно складывается с прежней цифрой
        public const float LowHp       = 0.30f;  // ниже — пульс и угли на шкале игрока
        public const float IntroTime   = 0.35f;  // выезд шкал
        public const float IntroStagger = 0.07f;
        public const float OutroDelay  = 0.80f;  // после нокаута шкалы ещё видны
        public const float CountLife   = 0.75f;  // цифра счёта рефери

        // ------------------------------------------------------------------ Палитра шкал (макет)

        static readonly Color Ink    = new Color32(245, 239, 226, 255);
        static readonly Color Bone   = new Color32(236, 226, 200, 255);
        static readonly Color Blood  = new Color32(200,  35,  28, 255);
        static readonly Color Hot    = new Color32(255,  74,  58, 255);
        static readonly Color Gold   = new Color32(244, 182,  46, 255);
        static readonly Color FoeBlue = new Color32(59, 130, 240, 255);
        static readonly Color PlateC = new Color(0.05f, 0.04f, 0.07f, 0.74f);
        static readonly Color Well   = new Color32(16, 12, 20, 255);   // непрозрачная «яма» шкалы: рамка под ней не просвечивает
        static readonly Color PipOff = new Color(0.16f, 0.13f, 0.10f, 0.85f);

        // ------------------------------------------------------------------ Шкала здоровья

        sealed class HpBar {
            public RectTransform panel, frame, fill, trail, flash;
            public Image fillImg, trailImg, flashImg, frameImg;
            public Text name, num;
            public CanvasGroup group;
            public bool right;           // заливка прижата вправо (соперник)
            public Color fillColor, trailColor;
            public float value = 1f, shown = -1f, trailV = 1f, trailShown = -1f, hold, flashT = 9f, sinceHit = 9f;
            public float joltT = 9f, joltAmp, joltSign = 1f;
            public int numCache = -1;
            public RectTransform[] cracks; public float[] crackT; public bool shattered;   // FightHUD.Cracks.cs
        }

        HpBar _plr, _foe;
        RectTransform _foeHpBg, _plrHpBg, _stamBg, _megaBg;   // имена полей читают инструменты снимков (HitFx2Shots)
        RectTransform _megaFill, _stunBg, _stunFill, _mid, _fx;
        CanvasGroup _midGroup;
        Image _megaFillImg, _stunFillImg;
        readonly Image[] _pips = new Image[MaxPips];
        readonly Image[] _kd = new Image[KdPips];
        readonly float[] _kdT = { 9f, 9f, 9f };
        readonly float[] _pipShine = new float[MaxPips];
        int _pipCount = -1, _pipsFull = -1;
        float _pipWeakT;

        Text _megaReadyLbl, _timerLbl, _countLbl, _countCap, _bannerLbl;
        Text _telegraphL, _telegraphR;
        Image _telBgL, _telBgR;
        RawImage _vignette;
        RectTransform _hintBg;
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

        // ------------------------------------------------------------------ Летящие цифры и искры (пулы)

        struct Floater { public Text t; public RectTransform rt; public float age, sum, x0, y0, drift; public int side; public bool alive; }
        readonly Floater[] _fl = new Floater[MaxFloaters];

        struct Spark { public Image img; public RectTransform rt; public Vector2 p, v; public float age, life, grav; public Color c0, c1; public bool alive; }
        readonly Spark[] _sp = new Spark[MaxSparks];
        uint _rng = 0x9E3779B9u;

        public int LiveFloaters { get { int n = 0; for (int i = 0; i < MaxFloaters; i++) if (_fl[i].alive) n++; return n; } }
        public int LiveSparks   { get { int n = 0; for (int i = 0; i < MaxSparks; i++) if (_sp[i].alive) n++; return n; } }

        // ------------------------------------------------------------------ Кэш (анти-аллокация)

        int  _cacheTMin = -1, _cacheTSec = -1;
        int  _cacheKd = -1;
        bool _cacheMegaRdy;
        float _megaShown = -1f, _stunShown = -1f, _mega, _stun;
        float _megaRdyT = 9f, _emberAcc, _fireAcc, _sparkAcc, _beat;
        float _plrHpRatio = 1f;
        bool _needIntro = true, _outro;
        float _introT = 9f, _outroT;
        float _bannerTimer, _countTimer, _countAge;
        bool _landscape;

        // Готовые строки: таймер «M:SS», счёт рефери, числа здоровья и урона — в кадре боя строки не создаются.
        const int TimerMax = 600, CountMax = 10, NumMax = 200, DmgMax = 199;
        static readonly string[] TimerStr = BuildTimer(), CountStr = BuildList("", CountMax), NumStr = BuildList("", NumMax), DmgStr = BuildList("−", DmgMax);
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
            _vignette.color = new Color(0.75f, 0f, 0f, 0f);
            _vignette.raycastTarget = false;
            Place(vg.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);

            // Панели шкал: игрок слева, соперник справа
            _plr = MakeHpBar("PlrPanel", "PlrHp", false, Bone, Blood);
            _foe = MakeHpBar("FoePanel", "FoeHp", true, FoeBlue, Ink);
            _plrHpBg = _plr.frame; _foeHpBg = _foe.frame;

            // Заряды выносливости: ряд кубиков под здоровьем игрока
            _stamBg = new GameObject("Stam", typeof(RectTransform)).GetComponent<RectTransform>();
            _stamBg.SetParent(_plr.panel, false);
            for (int i = 0; i < MaxPips; i++) {
                var p = UIKit.Panel(_stamBg, "P" + i, Gold);
                _pips[i] = p.GetComponent<Image>();
            }

            // Мега-шкала: тонкая золотая полоса с накалом
            _megaBg = UIKit.Panel(_plr.panel, "Mega", Well);
            _megaFill = UIKit.Panel(_megaBg, "Mega_F", UIKit.MegaGold);
            Stretch(_megaFill, 1f, 1f, -1f, -1f);
            _megaFillImg = _megaFill.GetComponent<Image>();

            // Оглушение соперника: тонкая полоса под его здоровьем; у края — отметки нокдаунов
            _stunBg = UIKit.Panel(_foe.panel, "Stun", Well);
            _stunFill = UIKit.Panel(_stunBg, "Stun_F", Gold);
            Stretch(_stunFill, 1f, 1f, -1f, -1f);
            _stunFillImg = _stunFill.GetComponent<Image>();
            for (int i = 0; i < KdPips; i++) {
                var k = UIKit.Panel(_foe.panel, "Kd" + i, PipOff);
                var o = k.gameObject.AddComponent<Outline>();
                o.effectColor = Ink; o.effectDistance = new Vector2(1.5f, -1.5f);
                _kd[i] = k.GetComponent<Image>();
            }

            _megaReadyLbl = UIKit.Label(transform, "MegaRdy", UIKit.FontSm, UIKit.MegaGold, semiBold: true);
            _megaReadyLbl.text = "МЕГА ГОТОВ · свайп!";
            var mo = _megaReadyLbl.GetComponent<Outline>();
            mo.effectColor = new Color(0.25f, 0.05f, 0f, 0.95f); mo.effectDistance = new Vector2(2f, -2f);
            _megaReadyLbl.gameObject.SetActive(false);

            // Середина: время
            _mid = new GameObject("Mid", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            _mid.SetParent(transform, false);
            _midGroup = _mid.GetComponent<CanvasGroup>(); _midGroup.blocksRaycasts = false;
            _timerLbl = UIKit.Label(_mid, "Timer", UIKit.FontMd + 2, Ink, semiBold: true);
            Stretch(_timerLbl.rectTransform, 0f, 0f, 0f, 0f);
            _timerLbl.text = "0:00";
            var to = _timerLbl.GetComponent<Outline>();
            to.effectColor = new Color(0f, 0f, 0f, 0.9f); to.effectDistance = new Vector2(2f, -2f);

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
            _dodgeL = ControlBtn("BtnDgL", "◄", 44, UIKit.TextMain);
            _dodgeL.onClick.AddListener(() => OnDodgeLeft?.Invoke());
            _dodgeR = ControlBtn("BtnDgR", "►", 44, UIKit.TextMain);
            _dodgeR.onClick.AddListener(() => OnDodgeRight?.Invoke());
            // пауза — своя плашка: чернила, рамка кости, знак из двух брусков (видна на любом фоне ринга)
            _pauseBtn = UIKit.Btn(transform, "BtnPause", "", UIKit.FontSm, new Color(0.03f, 0.02f, 0.05f, 0.86f), UIKit.TextMain);
            _pauseBtn.GetComponent<UIBorder>().Color = new Color(Bone.r, Bone.g, Bone.b, 0.8f);
            for (int i = 0; i < 2; i++) {
                var bar = UIKit.Plain(_pauseBtn.transform, "Bar" + i, Bone);
                UIKit.SetAnchors(bar, 0.5f, 0.28f, 0.5f, 0.72f);
                bar.sizeDelta = new Vector2(9f, 0f); bar.anchoredPosition = new Vector2(i == 0 ? -9f : 9f, 0f);
            }
            _pauseBtn.onClick.AddListener(() => OnPause?.Invoke());

            // Слой искр и цифр урона: поверх шкал, лучи не ловит
            _fx = new GameObject("Fx", typeof(RectTransform)).GetComponent<RectTransform>();
            _fx.SetParent(transform, false);
            Place(_fx, 0f, 0f, 1f, 1f);
            for (int i = 0; i < MaxSparks; i++) {
                var s = UIKit.Panel(_fx, "S" + i, Gold);
                s.sizeDelta = new Vector2(6f, 6f);
                UIKit.SetAnchors(s, 0.5f, 0.5f, 0.5f, 0.5f);
                s.gameObject.SetActive(false);
                _sp[i].rt = s; _sp[i].img = s.GetComponent<Image>();
            }
            BuildShards();
            for (int i = 0; i < MaxFloaters; i++) {
                var t = UIKit.Label(_fx, "F" + i, 30, Bone, semiBold: true);
                var o = t.GetComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.95f); o.effectDistance = new Vector2(2.5f, -2.5f);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                var r = t.rectTransform;
                UIKit.SetAnchors(r, 0.5f, 0.5f, 0.5f, 0.5f);
                r.sizeDelta = new Vector2(140f, 60f);
                t.gameObject.SetActive(false);
                _fl[i].t = t; _fl[i].rt = r;
            }

            // Счёт рефери: крупная цифра и подпись «НОКДАУН»
            _countCap = UIKit.Label(transform, "CountCap", UIKit.FontLg, Hot, semiBold: true);
            _countCap.text = "НОКДАУН";
            var cco = _countCap.GetComponent<Outline>();
            cco.effectColor = new Color(0f, 0f, 0f, 0.95f); cco.effectDistance = new Vector2(2f, -2f);
            _countCap.gameObject.SetActive(false);
            _countLbl = UIKit.Label(transform, "Count", 132, UIKit.TextMain, semiBold: true);
            var co = _countLbl.GetComponent<Outline>();
            co.effectColor = new Color(0f, 0f, 0f, 0.95f); co.effectDistance = new Vector2(4f, -4f);
            var cs = _countLbl.gameObject.AddComponent<Shadow>();
            cs.effectColor = new Color(0.45f, 0f, 0f, 0.85f); cs.effectDistance = new Vector2(5f, -8f);
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

        HpBar MakeHpBar(string panelName, string frameName, bool right, Color fill, Color trail) {
            var b = new HpBar { right = right, fillColor = fill, trailColor = trail };
            b.panel = UIKit.Panel(transform, panelName, PlateC);
            b.group = b.panel.gameObject.AddComponent<CanvasGroup>();
            b.group.blocksRaycasts = false;
            b.name = UIKit.Label(b.panel, "Name", UIKit.FontSm, Ink, semiBold: true, right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            b.name.horizontalOverflow = HorizontalWrapMode.Overflow;
            b.num = UIKit.Label(b.panel, "Num", UIKit.FontMd + 2, right ? FoeBlue : Bone, semiBold: true, right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            var no = b.num.GetComponent<Outline>();
            no.effectColor = new Color(0f, 0f, 0f, 0.9f); no.effectDistance = new Vector2(2f, -2f);
            b.frame = UIKit.Panel(b.panel, frameName, Ink);          // светлая рамка 2 px
            b.frameImg = b.frame.GetComponent<Image>();
            var well = UIKit.Panel(b.frame, "Well", Well);
            Stretch(well, 2f, 2f, -2f, -2f);
            b.trail = UIKit.Panel(well, "Trail", trail);
            b.fill = UIKit.Panel(well, frameName + "_F", fill);
            b.flash = UIKit.Panel(well, "Flash", new Color(1f, 1f, 1f, 0f));
            b.trailImg = b.trail.GetComponent<Image>(); b.fillImg = b.fill.GetComponent<Image>(); b.flashImg = b.flash.GetComponent<Image>();
            SetSpan(b.fill, 1f, right); SetSpan(b.trail, 1f, right); SetSpan(b.flash, 1f, right);
            BuildCracks(b);
            return b;
        }

        Button ControlBtn(string name, string caption, int fontSize, Color textColor) {
            var b = UIKit.Btn(transform, name, caption, fontSize, new Color32(16, 14, 22, 235), textColor);
            b.GetComponent<UIBorder>().Color = new Color(1f, 1f, 1f, 0.42f);   // светлая рамка по краю подложки
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

        // Заливка доли v: у игрока от левого края, у соперника — от правого.
        static void SetSpan(RectTransform rt, float v, bool right) {
            v = Mathf.Clamp01(v);
            rt.anchorMin = new Vector2(right ? 1f - v : 0f, 0f);
            rt.anchorMax = new Vector2(right ? 1f : v, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // Кусок между a и b (доли от «своего» края).
        static void SetSeg(RectTransform rt, float a, float b, bool right) {
            float lo = Mathf.Clamp01(Mathf.Min(a, b)), hi = Mathf.Clamp01(Mathf.Max(a, b));
            rt.anchorMin = new Vector2(right ? 1f - hi : lo, 0f);
            rt.anchorMax = new Vector2(right ? 1f - lo : hi, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------ Обновление

        // Вызывается FightDirector каждый кадр. Строки не создаются: только готовые из таблиц.
        public void Refresh(HudData d) {
            float foeK = d.foeHpMax > 0f ? Mathf.Clamp01(d.foeHp / d.foeHpMax) : 0f;
            float plrK = d.playerHpMax > 0f ? Mathf.Clamp01(d.playerHp / d.playerHpMax) : 1f;
            _plrHpRatio = plrK;

            // шкалы выезжают, когда бой на экране (во время загрузки HUD скрыт, хотя бой уже тикает)
            var hg = GetComponent<CanvasGroup>();
            if (_needIntro && isActiveAndEnabled && (hg == null || hg.blocksRaycasts)) {
                _needIntro = false; _outro = false;
                ResetCracks();
                _foe.value = _foe.trailV = foeK; _plr.value = _plr.trailV = plrK;
                _cacheKd = d.foeKnockdowns; ApplyKd(d.foeKnockdowns);
                _cacheMegaRdy = d.megaReady && _megaOn; _megaReadyLbl.gameObject.SetActive(_cacheMegaRdy);
                _introT = UIMotion.Animate ? 0f : 9f;
            }

            Track(_foe, foeK, d.foeHpMax, d.foeHp, 1);
            Track(_plr, plrK, d.playerHpMax, d.playerHp, 0);

            _mega = Mathf.Clamp01(d.megaMeter * 0.01f);
            _stun = Mathf.Clamp01(d.foeStun01);
            _cine = Mathf.Clamp01(d.cineBars);

            // Заряды выносливости: кубик — один удар; потраченный рассыпается золотом, восстановленный вспыхивает
            float cost = d.staminaHitCost > 0.5f ? d.staminaHitCost : 12f;
            int count = Mathf.Clamp(Mathf.FloorToInt(100f / cost + 0.001f), 1, MaxPips);
            if (count != _pipCount) { _pipCount = count; _pipsFull = -1; LayoutPips(); }
            int full = Mathf.Clamp(Mathf.FloorToInt(d.playerStamina / cost + 0.001f), 0, count);
            float part = Mathf.Clamp01(d.playerStamina / cost - full);
            if (_pipsFull >= 0 && full != _pipsFull) {
                if (full < _pipsFull) for (int i = full; i < _pipsFull; i++) GoldDust(i);
                else for (int i = _pipsFull; i < full; i++) _pipShine[i] = 0f;
            }
            if (full == 0 && _pipsFull > 0) _pipWeakT = 0f;
            _pipsFull = full;
            for (int i = 0; i < count; i++) {
                Color c = i < full ? Gold : i == full ? Color.Lerp(PipOff, new Color(0.55f, 0.40f, 0.12f, 0.95f), part) : PipOff;
                _pips[i].color = c;   // яркость вспышки дописывает Tick
            }

            // «МЕГА ГОТОВ»: печать и золотой взрыв
            bool rdy = d.megaReady && _megaOn;
            if (rdy != _cacheMegaRdy) {
                _cacheMegaRdy = rdy;
                _megaReadyLbl.gameObject.SetActive(rdy);
                if (rdy) { _megaRdyT = 0f; MegaBurst(); }
            }

            // Имена (не меняются, но защищаем от повторной записи)
            if (!ReferenceEquals(_foe.name.text, d.foeName) && _foe.name.text != d.foeName) _foe.name.text = d.foeName ?? "";
            if (!ReferenceEquals(_plr.name.text, d.playerName) && _plr.name.text != d.playerName) _plr.name.text = d.playerName ?? "";

            // Нокдауны: отметка ставится печатью
            if (d.foeKnockdowns != _cacheKd) {
                bool up = d.foeKnockdowns > _cacheKd && _cacheKd >= 0;
                _cacheKd = d.foeKnockdowns;
                ApplyKd(d.foeKnockdowns);
                if (up) { int k = Mathf.Clamp(d.foeKnockdowns - 1, 0, KdPips - 1); _kdT[k] = 0f; KdBurst(k); }
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
                _cacheTMin = tMin; _cacheTSec = tSec;
                _timerLbl.text = TimerStr[Mathf.Clamp(tMin * 60 + tSec, 0, TimerMax - 1)];
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

            // Конец боя: нокаут или игрок на полу — шкалы уезжают
            bool over = d.foePhase == FightPhase.KO || d.playerHp <= 0f;
            if (over && !_outro) {
                _outro = true; _outroT = -OutroDelay;
                if (d.foePhase == FightPhase.KO) Shatter(_foe);
                if (d.playerHp <= 0f) Shatter(_plr);
            }
        }

        // Значение шкалы и реакция на урон: след, вспышка, толчок по силе удара, цифра.
        void Track(HpBar b, float k, float max, float hp, int side) {
            float prev = b.value;
            b.value = k;
            if (_needIntro) { b.trailV = k; prev = k; }   // бой ещё не на экране: без реакций
            if (k < prev - 0.0005f) {
                float dmg = (prev - k) * Mathf.Max(1f, max);
                b.hold = TrailHold; b.flashT = 0f; b.sinceHit = 0f;
                float s = Mathf.Clamp01(dmg / 18f);                 // джеб ~4, хук ~10, мега 25+
                if (!UIMotion.Reduced) { b.joltT = 0f; b.joltAmp = 2f + 9f * s; b.joltSign = -b.joltSign; }
                SpawnFloater(side, dmg, b, k, prev);
                if (s > 0.45f && !UIMotion.Reduced) ChunkSparks(b, k, prev, s);
            } else if (k > b.trailV) b.trailV = k;   // лечение/восстановление: след не нужен
            int n = Mathf.Clamp(Mathf.CeilToInt(hp - 0.001f), 0, NumMax);
            if (n != b.numCache) { b.numCache = n; b.num.text = NumStr[n]; }
        }

        void ApplyKd(int kd) {
            for (int i = 0; i < KdPips; i++) _kd[i].color = i < kd ? Blood : PipOff;
        }

        // ------------------------------------------------------------------ Tick: движение (реальное время)

        const int BannerFont = 76;
        static readonly Color BlockOffColor = new Color32(16, 14, 22, 235);
        static readonly Color BlockOnColor  = new Color32(170, 95, 20, 245);

        public void Tick(float dt, float udt) {
            float rdt = udt;                     // таймеры баннера, счёта и подсказки — по настоящему времени
            if (!UIMotion.Animate) udt = 9f;     // движение вне Play Mode — сразу конечное состояние
            bool calm = UIMotion.Reduced;

            // выезд в начале и уход в конце: панели сверху, середина следом
            float introK = 1f;
            if (_introT < 9f) { _introT += udt; if (_introT > IntroTime + IntroStagger * 2f + 0.1f) _introT = 9f; }
            if (_outro) _outroT += udt;
            PlacePanel(_plr, 0, ref introK);
            float fk = 1f; PlacePanel(_foe, 1, ref fk);
            {
                float e = Enter(2), o = Exit(2);
                float a = e * (1f - o);
                if (a != _midGroup.alpha) _midGroup.alpha = a;
                var mp = new Vector2(0f, (1f - e) * 60f + o * 60f);
                if (mp != _mid.anchoredPosition) _mid.anchoredPosition = mp;
            }

            StepBar(_plr, udt, calm, introK);
            StepBar(_foe, udt, calm, fk);
            StepCracks(_plr, udt); StepCracks(_foe, udt);

            // мега-шкала: накал (цвет к белому золоту) и угли, растущие с зарядом
            float megaV = _mega * introK;
            if (Mathf.Abs(megaV - _megaShown) > 0.0005f) { _megaShown = megaV; SetSpan(_megaFill, megaV, false); }
            if (_megaOn) {
                float heat = _cacheMegaRdy ? 0.75f + 0.25f * Mathf.Sin(Time.realtimeSinceStartup * 9f) : _mega * _mega;
                _megaFillImg.color = Color.Lerp(UIKit.Accent, new Color(1f, 0.97f, 0.75f, 1f), calm ? Mathf.Min(heat, 0.5f) : heat);
                if (_mega > 0.25f && !_outro) {
                    _emberAcc += udt * (2f + 14f * _mega * _mega);
                    while (_emberAcc >= 1f) { _emberAcc -= 1f; Ember(); }
                }
            }
            if (_megaReadyLbl.gameObject.activeSelf) {
                _megaRdyT += udt;
                var rt = _megaReadyLbl.rectTransform;
                float sc = 1f;
                if (!calm) sc = _megaRdyT < 0.3f ? Mathf.LerpUnclamped(1.7f, 1f, UIMotion.Eval(Ease.OutBack, _megaRdyT / 0.3f))
                                                : 1f + 0.04f * Mathf.Sin(_megaRdyT * 7f);
                rt.localScale = new Vector3(sc, sc, 1f);
                var c = UIKit.MegaGold; c.a = _megaRdyT < 0.3f || calm ? 1f : 0.8f + 0.2f * Mathf.Sin(_megaRdyT * 7f);
                _megaReadyLbl.color = c;
            }

            // оглушение: от золота к алому; у края — искрит («нокдаун близко»)
            float stunV = _stun * fk;
            if (Mathf.Abs(stunV - _stunShown) > 0.0005f) { _stunShown = stunV; SetSpan(_stunFill, stunV, true); }
            _stunFillImg.color = Color.Lerp(Gold, Hot, Mathf.InverseLerp(0.5f, 1f, _stun));
            if (_stun > 0.8f && !_outro) {
                _sparkAcc += udt * 10f * (_stun - 0.7f) / 0.3f;
                while (_sparkAcc >= 1f) { _sparkAcc -= 1f; StunSpark(); }
            }

            // мало здоровья: сердцебиение панели игрока и угли с края шкалы («твоя шкала горит»)
            if (_plrHpRatio < LowHp && _plrHpRatio > 0f && !_outro) {
                _beat += udt;
                float beatT = _beat % 0.9f;
                float pulse = beatT < 0.09f ? beatT / 0.09f : beatT < 0.18f ? 1f - (beatT - 0.09f) / 0.09f
                            : beatT < 0.27f ? 0.6f * (beatT - 0.18f) / 0.09f : beatT < 0.38f ? 0.6f * (1f - (beatT - 0.27f) / 0.11f) : 0f;
                _plr.fillImg.color = Color.Lerp(Blood, Hot, calm ? 0.5f : pulse);
                _plr.frameImg.color = calm ? Hot : Color.Lerp(Ink, Hot, pulse);
                if (!calm) { float s = 1f + 0.05f * pulse; _plr.frame.localScale = new Vector3(s, s, 1f); }
                _fireAcc += udt * 9f;
                while (_fireAcc >= 1f) { _fireAcc -= 1f; Fire(); }
            } else if (_beat > 0f) {
                _beat = 0f;
                _plr.fillImg.color = _plr.fillColor; _plr.frameImg.color = Ink; _plr.frame.localScale = Vector3.one;
            }

            // вспышка восстановленных кубиков, тряска пустого ряда
            for (int i = 0; i < _pipCount; i++) {
                if (_pipShine[i] >= 0.3f) continue;
                _pipShine[i] += udt;
                if (!calm) _pips[i].color = Color.Lerp(Color.white, _pips[i].color, Mathf.Clamp01(_pipShine[i] / 0.3f));
            }
            if (_pipWeakT < 0.32f) {
                _pipWeakT += udt;
                float x = calm ? 0f : 3f * Mathf.Sin(_pipWeakT * 60f) * (1f - _pipWeakT / 0.32f);
                _stamBg.anchoredPosition = new Vector2(x, 0f);
                if (_pipWeakT >= 0.32f) _stamBg.anchoredPosition = Vector2.zero;
            }

            // отметки нокдаунов: печать 2 → 1
            for (int i = 0; i < KdPips; i++) {
                if (_kdT[i] >= 0.5f) continue;
                _kdT[i] += udt;
                float s = calm ? 1f : Mathf.LerpUnclamped(2.2f, 1f, UIMotion.Eval(Ease.OutBack, _kdT[i] / 0.5f));
                _kd[i].rectTransform.localScale = new Vector3(s, s, 1f);
            }

            // виньетка по краям: появляется при hp < 55%, вспыхивает от сильного удара по игроку
            float vign = _plrHpRatio < 0.55f ? (0.55f - _plrHpRatio) / 0.55f * 0.85f : 0f;
            if (!calm && _plr.sinceHit < 0.3f) vign = Mathf.Max(vign, 0.55f * (1f - _plr.sinceHit / 0.3f) * Mathf.Clamp01(_plr.joltAmp / 8f));
            _vignette.color = new Color(0.75f, 0f, 0f, vign);

            StepFloaters(udt);
            StepSparks(udt);
            StepShards(udt);

            // счёт рефери: цифра падает крупной и гаснет; последние счёты — алые
            if (_countTimer > 0f) {
                _countTimer -= rdt; _countAge += udt;
                float s = calm ? 1f : Mathf.LerpUnclamped(1.6f, 1f, UIMotion.Eval(Ease.OutBack, _countAge / 0.22f));
                _countLbl.rectTransform.localScale = new Vector3(s, s, 1f);
                var c = _countLbl.color; c.a = Mathf.Clamp01(_countTimer / 0.18f); _countLbl.color = c;
                var cc = _countCap.color; cc.a = c.a; _countCap.color = cc;
                if (_countTimer <= 0f) { _countLbl.gameObject.SetActive(false); _countCap.gameObject.SetActive(false); }
            }
            if (_bannerTimer > 0f) {
                _bannerTimer -= rdt;
                float age = _bannerAge += udt;
                float s = calm ? 1f : age < 0.18f ? Mathf.Lerp(1.7f, 1f, UIMotion.Eval(Ease.OutCubic, age / 0.18f)) : 1f;
                _bannerLbl.rectTransform.localScale = new Vector3(s, s, 1f);
                var c = _bannerLbl.color; c.a = Mathf.Min(Mathf.Clamp01(age / 0.12f), Mathf.Clamp01(_bannerTimer / 0.3f)); _bannerLbl.color = c;
                if (_bannerTimer <= 0f) _bannerLbl.gameObject.SetActive(false);
            }
            if (_hintTimer > 0f) {
                _hintTimer -= rdt;
                if (_hintTimer <= 0f) _hintBg.gameObject.SetActive(false);
            }
        }
        float _bannerAge;

        float Enter(int i) {
            if (_introT >= 9f) return 1f;
            return UIMotion.Eval(Ease.OutQuint, (_introT - i * IntroStagger) / IntroTime);
        }

        // Уход: в конце боя — насовсем; при кинополосах (нокдаун) — под полосы и обратно, когда они уезжают.
        float _cine;
        float Exit(int i) {
            float c = _cine * _cine * (3f - 2f * _cine);
            if (!_outro || _outroT <= 0f) return c;
            return Mathf.Max(c, UIMotion.Eval(Ease.InCubic, (_outroT - i * IntroStagger * 0.5f) / (IntroTime * 0.8f)));
        }

        // Панель шкалы: выезд сверху, толчок от удара (сдвиг по силе с затуханием и лёгкий наклон масштаба).
        void PlacePanel(HpBar b, int i, ref float introK) {
            float e = Enter(i), o = Exit(i);
            introK = Mathf.Clamp01(e);
            b.group.alpha = Mathf.Clamp01(e * 1.4f) * (1f - o);
            float x = 0f, y = (1f - e) * 70f + o * 80f, sc = 1f;
            if (b.joltT < JoltTime) {
                float u = b.joltT / JoltTime, damp = (1f - u) * (1f - u);
                x = b.joltAmp * b.joltSign * Mathf.Sin(u * 21f) * damp;
                y += b.joltAmp * 0.35f * Mathf.Cos(u * 17f) * damp;
                sc = 1f + 0.012f * b.joltAmp * damp;
            }
            var pos = new Vector2(x, y);
            if (pos != b.panel.anchoredPosition) b.panel.anchoredPosition = pos;
            if (sc != b.panel.localScale.x) b.panel.localScale = new Vector3(sc, sc, 1f);
        }

        void StepBar(HpBar b, float udt, bool calm, float introK) {
            b.joltT += udt; b.sinceHit += udt;
            if (b.trailV > b.value) {
                if (b.hold > 0f) b.hold -= udt;
                else {
                    float gap = b.trailV - b.value;
                    b.trailV = Mathf.Max(b.value, b.trailV - Mathf.Max(gap, 0.05f) * udt / TrailCatch * 1.6f);
                    if (b.trailV - b.value < 0.002f) b.trailV = b.value;
                }
            } else b.trailV = b.value;
            float v = b.value * introK, tv = b.trailV * introK;
            if (Mathf.Abs(v - b.shown) > 0.0005f) { b.shown = v; SetSpan(b.fill, v, b.right); }
            if (Mathf.Abs(tv - b.trailShown) > 0.0005f) { b.trailShown = tv; SetSpan(b.trail, tv, b.right); }
            // снятый кусок: на миг белый, затем цвет следа; вся заливка вспыхивает от удара
            b.flashT += udt;
            float wk = calm ? 0f : 1f - Mathf.Clamp01(b.flashT / TrailFlash);
            b.trailImg.color = Color.Lerp(b.trailColor, Color.white, wk);
            if (wk > 0f) { SetSeg(b.flash, v, Mathf.Max(tv, v), b.right); b.flashImg.color = new Color(1f, 1f, 1f, 0.75f * wk); }
            else if (b.flashImg.color.a > 0f) b.flashImg.color = new Color(1f, 1f, 1f, 0f);
        }

        // ------------------------------------------------------------------ Летящие цифры

        // Точка шкалы (доля от своего края) в координатах слоя Fx.
        Vector2 BarPoint(HpBar b, float f, float dy = 0f) {
            var r = b.frame.rect;
            float x = b.right ? Mathf.Lerp(r.xMax, r.xMin, f) : Mathf.Lerp(r.xMin, r.xMax, f);
            Vector3 w = b.frame.TransformPoint(new Vector3(x, r.center.y + dy, 0f));
            Vector3 l = _fx.InverseTransformPoint(w);
            return new Vector2(l.x, l.y);
        }

        // Урон всплывает под шкалой у снятого куска и падает; удары подряд складываются (−12, −24).
        void SpawnFloater(int side, float dmg, HpBar b, float k, float prev) {
            int slot = -1;
            for (int i = 0; i < MaxFloaters; i++)
                if (_fl[i].alive && _fl[i].side == side && _fl[i].age < FloatMerge) { slot = i; break; }
            if (slot < 0) {
                float oldest = -1f;
                for (int i = 0; i < MaxFloaters; i++) {
                    if (!_fl[i].alive) { slot = i; break; }
                    if (_fl[i].age > oldest) { oldest = _fl[i].age; slot = i; }   // предел: старая цифра уступает место
                }
                _fl[slot].sum = 0f;
            }
            ref var f = ref _fl[slot];
            f.sum += dmg; f.age = 0f; f.side = side; f.alive = true;
            var p = BarPoint(b, (k + prev) * 0.5f);
            // под панелью, у снятого куска (не на отметках и числе)
            Vector3 pb = _fx.InverseTransformPoint(b.panel.TransformPoint(new Vector3(0f, b.panel.rect.yMin, 0f)));
            f.x0 = p.x; f.y0 = pb.y - 22f;
            f.drift = (side == 1 ? -1f : 1f) * (6f + 10f * Rand01());
            int n = Mathf.Clamp(Mathf.RoundToInt(f.sum), 1, DmgMax);
            f.t.text = DmgStr[n];
            f.t.fontSize = Mathf.RoundToInt(Mathf.Lerp(26f, 44f, Mathf.Clamp01(f.sum / 30f)));
            f.t.color = side == 1 ? new Color(1f, 0.93f, 0.66f, 1f) : Hot;
            if (!f.t.gameObject.activeSelf) f.t.gameObject.SetActive(true);
        }

        void StepFloaters(float udt) {
            for (int i = 0; i < MaxFloaters; i++) {
                ref var f = ref _fl[i];
                if (!f.alive) continue;
                f.age += udt;
                if (f.age >= FloatLife) { f.alive = false; f.t.gameObject.SetActive(false); continue; }
                float u = f.age / FloatLife;
                float pop = UIMotion.Reduced ? 1f : f.age < 0.16f ? Mathf.LerpUnclamped(1.6f, 1f, UIMotion.Eval(Ease.OutBack, f.age / 0.16f)) : 1f;
                f.rt.localScale = new Vector3(pop, pop, 1f);
                f.rt.anchoredPosition = new Vector2(f.x0 + f.drift * u, f.y0 - 34f * UIMotion.Eval(Ease.OutCubic, u));
                var c = f.t.color; c.a = u < 0.65f ? 1f : 1f - (u - 0.65f) / 0.35f; f.t.color = c;
            }
        }

        // ------------------------------------------------------------------ Искры (пиксельные квадраты)

        float Rand01() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / 16777216f; }

        void Emit(Vector2 p, Vector2 v, float life, float size, float grav, Color c0, Color c1) {
            int slot = -1; float oldest = -1f;
            for (int i = 0; i < MaxSparks; i++) {
                if (!_sp[i].alive) { slot = i; break; }
                float a = _sp[i].age / _sp[i].life;
                if (a > oldest) { oldest = a; slot = i; }   // предел: гаснущая искра уступает место
            }
            ref var s = ref _sp[slot];
            s.p = p; s.v = v; s.age = 0f; s.life = life; s.grav = grav; s.c0 = c0; s.c1 = c1; s.alive = true;
            s.rt.sizeDelta = new Vector2(size, size);
            s.rt.anchoredPosition = p;
            s.img.color = c0;
            if (!s.rt.gameObject.activeSelf) s.rt.gameObject.SetActive(true);
        }

        void StepSparks(float udt) {
            for (int i = 0; i < MaxSparks; i++) {
                ref var s = ref _sp[i];
                if (!s.alive) continue;
                s.age += udt;
                if (s.age >= s.life) { s.alive = false; s.rt.gameObject.SetActive(false); continue; }
                s.v.y -= s.grav * udt;
                s.p += s.v * udt;
                s.rt.anchoredPosition = s.p;
                float u = s.age / s.life;
                var c = Color.Lerp(s.c0, s.c1, u); c.a *= 1f - u * u;
                s.img.color = c;
            }
        }

        static readonly Color EmberHot = new Color(1f, 0.92f, 0.55f, 1f), EmberCool = new Color(0.85f, 0.18f, 0.05f, 0.9f);

        void Ember() {
            var r = _megaBg.rect;
            float f = Rand01() * _mega;
            Vector3 w = _megaBg.TransformPoint(new Vector3(Mathf.Lerp(r.xMin, r.xMax, f), r.yMax, 0f));
            Vector3 l = _fx.InverseTransformPoint(w);
            Emit(new Vector2(l.x, l.y), new Vector2((Rand01() - 0.5f) * 30f, 40f + 70f * Rand01()), 0.45f + 0.4f * Rand01(),
                 3f + 3f * Rand01(), -20f, EmberHot, EmberCool);
        }

        void Fire() {
            var p = BarPoint(_plr, _plr.value * Rand01(), _plr.frame.rect.height * 0.3f);
            Emit(p, new Vector2((Rand01() - 0.5f) * 24f, 50f + 60f * Rand01()), 0.35f + 0.3f * Rand01(), 4f + 4f * Rand01(), -30f,
                 new Color(1f, 0.85f, 0.3f, 1f), new Color(0.7f, 0.05f, 0.02f, 0.8f));
        }

        void StunSpark() {
            var r = _stunBg.rect;
            Vector3 w = _stunBg.TransformPoint(new Vector3(Mathf.Lerp(r.xMax, r.xMin, _stun * Rand01()), r.center.y, 0f));
            Vector3 l = _fx.InverseTransformPoint(w);
            float a = Rand01() * Mathf.PI * 2f;
            Emit(new Vector2(l.x, l.y), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (60f + 60f * Rand01()), 0.22f, 3f, 0f, Color.white, Gold);
        }

        void GoldDust(int pip) {
            if (pip < 0 || pip >= _pipCount) return;
            var rt = _pips[pip].rectTransform; var r = rt.rect;
            Vector3 l = _fx.InverseTransformPoint(rt.TransformPoint(r.center));
            for (int i = 0; i < 3; i++)
                Emit(new Vector2(l.x, l.y), new Vector2((Rand01() - 0.5f) * 80f, 20f + 50f * Rand01()), 0.4f + 0.2f * Rand01(), 3f, 220f, Gold, new Color(0.6f, 0.35f, 0.05f, 1f));
        }

        void ChunkSparks(HpBar b, float k, float prev, float s) {
            int n = 3 + Mathf.RoundToInt(5f * s);
            for (int i = 0; i < n; i++) {
                var p = BarPoint(b, Mathf.Lerp(k, prev, Rand01()));
                float dir = b.right ? 1f : -1f;
                Emit(p, new Vector2(dir * (-40f + 120f * Rand01()), 40f + 120f * Rand01()), 0.35f + 0.25f * Rand01(), 4f + 3f * s, 380f,
                     Color.white, b.right ? FoeBlue : Blood);
            }
        }

        void MegaBurst() {
            if (UIMotion.Reduced) return;
            var r = _megaBg.rect;
            for (int i = 0; i < 14; i++) {
                Vector3 l = _fx.InverseTransformPoint(_megaBg.TransformPoint(new Vector3(Mathf.Lerp(r.xMin, r.xMax, Rand01()), r.center.y, 0f)));
                float a = Mathf.PI * (0.1f + 0.8f * Rand01());
                Emit(new Vector2(l.x, l.y), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (90f + 110f * Rand01()), 0.5f + 0.3f * Rand01(), 5f, 160f, Color.white, UIKit.MegaGold);
            }
        }

        void KdBurst(int k) {
            if (UIMotion.Reduced) return;
            var rt = _kd[k].rectTransform;
            Vector3 l = _fx.InverseTransformPoint(rt.TransformPoint(rt.rect.center));
            for (int i = 0; i < 8; i++) {
                float a = i * Mathf.PI * 0.25f;
                Emit(new Vector2(l.x, l.y), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 110f, 0.35f, 5f, 0f, Hot, Blood);
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

        // «Заново» из паузы: погасить счёт рефери, баннер, подсказку, цифры и искры; шкалы выедут заново.
        public void ResetTransient() {
            _countTimer = _bannerTimer = _hintTimer = 0f;
            _countLbl.gameObject.SetActive(false); _countCap.gameObject.SetActive(false);
            _bannerLbl.gameObject.SetActive(false);
            _hintBg.gameObject.SetActive(false);
            _cacheKd = -1; _cacheMegaRdy = false; _megaReadyLbl.gameObject.SetActive(false);
            for (int i = 0; i < MaxFloaters; i++) { _fl[i].alive = false; _fl[i].t.gameObject.SetActive(false); }
            for (int i = 0; i < MaxSparks; i++) { _sp[i].alive = false; _sp[i].rt.gameObject.SetActive(false); }
            _plr.joltT = _foe.joltT = 9f; _plr.hold = _foe.hold = 0f;
            _needIntro = true; _outro = false;
            ResetCracks();
        }

        // Показать крупный счёт рефери на CountLife с (для нокдаун-отсчёта).
        public void ShowCount(int n) {
            int c = Mathf.Clamp(n, 0, CountMax);
            _countLbl.text = CountStr[c];
            _countLbl.color = c >= 8 ? Hot : UIKit.TextMain;
            _countLbl.gameObject.SetActive(true);
            _countCap.gameObject.SetActive(true);
            _countTimer = CountLife; _countAge = 0f;
            if (!UIMotion.Animate) { _countLbl.rectTransform.localScale = Vector3.one; }
        }

        // Показать баннер по центру на seconds секунд.
        public void ShowBanner(string text, float seconds) {
            _bannerLbl.text = text;
            _bannerLbl.gameObject.SetActive(true);
            var c = _bannerLbl.color; c.a = UIMotion.Animate ? 0f : 1f; _bannerLbl.color = c;
            _bannerTimer = seconds; _bannerAge = 0f;
        }

        // Для тестов и снимков: доли шкал и след.
        public float FoeTrail => _foe.trailV;
        public float FoeValue => _foe.value;
        public float PlayerTrail => _plr.trailV;
        public float PlayerValue => _plr.value;
        public float PanelJolt => Mathf.Abs(_foe.panel.anchoredPosition.x) + Mathf.Abs(_plr.panel.anchoredPosition.x)
                                  + Mathf.Abs(_foe.panel.localScale.x - 1f) + Mathf.Abs(_plr.panel.localScale.x - 1f);
        public float FlashAlpha => Mathf.Max(_foe.flashImg.color.a, _plr.flashImg.color.a);
        public bool  Outro => _outro;
        public bool  CountVisible => _countLbl.gameObject.activeSelf;
        public float IntroProgress => _introT >= 9f ? 1f : Mathf.Clamp01(_introT / (IntroTime + IntroStagger * 2f));

        // Программное нажатие кнопок — для GameWalk (сквозной проход).
        public void PressBlock()      => OnBlock?.Invoke();
        public void PressDodgeLeft()  => OnDodgeLeft?.Invoke();
        public void PressDodgeRight() => OnDodgeRight?.Invoke();
        public void PressPause()      => OnPause?.Invoke();

        // ------------------------------------------------------------------ Layout

        // ILayoutHandler: раскладка под ориентацию (доли safe-area). UIRoot зовёт при регистрации и при смене.
        // Шкалы — только в верхней полосе (выше FxPops.PopTop*: плашка удара под ними), голова соперника ниже.
        public void OnLayout(bool landscape) {
            _landscape = landscape;
            if (landscape) {
                Place(_plr.panel, 0.020f, 0.862f, 0.375f, 0.995f);
                Place(_foe.panel, 0.580f, 0.862f, 0.918f, 0.995f);
                Place(_mid,       0.395f, 0.900f, 0.560f, 0.995f);
                Place(_megaReadyLbl, 0.020f, 0.835f, 0.375f, 0.862f);
                _megaReadyLbl.alignment = TextAnchor.MiddleLeft;
                Place(_pauseBtn, 0.930f, 0.855f, 0.995f, 0.995f);   // угол справа сверху, ≥ 88 px на 1080
                Place(_telBgL, 0.150f, 0.440f, 0.230f, 0.660f);
                Place(_telBgR, 0.770f, 0.440f, 0.850f, 0.660f);
                Place(_hintBg,     0.190f, 0.025f, 0.810f, 0.230f);   // ниже «СЕРИЯ ×N» (центр 0.30)
                Place(_countCap,   0.300f, 0.730f, 0.700f, 0.820f);
                Place(_countLbl,   0.350f, 0.400f, 0.650f, 0.740f);
                Place(_bannerLbl,  0.200f, 0.280f, 0.800f, 0.440f);
                // Кнопки под большими пальцами по углам, середина низа свободна; «БЛОК» над «◄»,
                // чтобы перчатки игрока (от ~0.25 ширины) их не перекрывали. Высота ≥ 100 опорных px.
                Place(_dodgeL,   0.008f, 0.030f, 0.122f, 0.270f);
                Place(_blockBtn, 0.008f, 0.295f, 0.122f, 0.500f);
                Place(_dodgeR,   0.878f, 0.030f, 0.992f, 0.270f);
            } else {
                Place(_plr.panel, 0.020f, 0.872f, 0.425f, 0.995f);
                Place(_foe.panel, 0.575f, 0.872f, 0.980f, 0.995f);
                Place(_mid,       0.425f, 0.945f, 0.575f, 0.995f);
                Place(_megaReadyLbl, 0.020f, 0.835f, 0.600f, 0.870f);
                _megaReadyLbl.alignment = TextAnchor.MiddleLeft;
                // пауза — под временем, между панелями (≥ 44 опорных px, на 1080 — от 88 px)
                Place(_pauseBtn, 0.445f, 0.880f, 0.555f, 0.940f);
                Place(_telBgL, 0.025f, 0.560f, 0.185f, 0.660f);
                Place(_telBgR, 0.815f, 0.560f, 0.975f, 0.660f);
                Place(_hintBg,     0.030f, 0.125f, 0.970f, 0.235f);
                Place(_countCap,   0.150f, 0.735f, 0.850f, 0.785f);
                Place(_countLbl,   0.200f, 0.540f, 0.800f, 0.745f);
                Place(_bannerLbl,  0.040f, 0.390f, 0.960f, 0.470f);
                // кнопки раздельно по низу (высота ~105 опорных px): между ними видно соперника
                Place(_dodgeL,   0.025f, 0.012f, 0.235f, 0.108f);
                Place(_blockBtn, 0.330f, 0.012f, 0.670f, 0.108f);
                Place(_dodgeR,   0.765f, 0.012f, 0.975f, 0.108f);
            }
            LayoutPanel(_plr, landscape);
            LayoutPanel(_foe, landscape);
            LayoutPips();
            _plr.shown = _plr.trailShown = _foe.shown = _foe.trailShown = _megaShown = _stunShown = -1f;   // заливки — заново
        }

        // Внутри панели: имя сверху, толстая шкала с числом, под ней — заряды/мега (игрок) или оглушение и нокдауны (соперник).
        void LayoutPanel(HpBar b, bool land) {
            int nameFont = land ? UIKit.FontSm : UIKit.FontSm, numFont = land ? UIKit.FontMd : UIKit.FontMd + 2;
            b.name.fontSize = nameFont; b.num.fontSize = numFont;
            if (!b.right) {
                Rel(b.name.rectTransform, 0.035f, 0.66f, 0.97f, 0.98f);
                Rel(b.frame, 0.030f, 0.37f, 0.80f, 0.64f);
                Rel(b.num.rectTransform, 0.82f, 0.33f, 0.995f, 0.68f);
                Rel(_stamBg, 0.030f, 0.17f, 0.80f, 0.31f);
                Rel(_megaBg, 0.030f, 0.05f, 0.80f, 0.12f);
            } else {
                Rel(b.name.rectTransform, 0.03f, 0.66f, 0.965f, 0.98f);
                Rel(b.frame, 0.20f, 0.37f, 0.97f, 0.64f);
                Rel(b.num.rectTransform, 0.005f, 0.33f, 0.18f, 0.68f);
                Rel(_stunBg, 0.20f, 0.17f, 0.97f, 0.27f);
                // три квадратные отметки нокдаунов — слева под числом
                float sz = land ? 12f : 15f;
                for (int i = 0; i < KdPips; i++) {
                    var k = _kd[i].rectTransform;
                    k.anchorMin = k.anchorMax = new Vector2(0.02f, 0.18f);
                    k.pivot = new Vector2(0f, 0.5f);
                    k.sizeDelta = new Vector2(sz, sz);
                    k.anchoredPosition = new Vector2(i * (sz + 6f), 0f);
                }
            }
        }

        // Кубики выносливости: _pipCount делений с зазором.
        void LayoutPips() {
            int n = Mathf.Max(1, _pipCount < 0 ? 8 : _pipCount);
            for (int i = 0; i < MaxPips; i++) {
                bool on = i < n;
                if (_pips[i].gameObject.activeSelf != on) _pips[i].gameObject.SetActive(on);
                if (!on) continue;
                var rt = _pips[i].rectTransform;
                UIKit.SetAnchors(rt, (float)i / n, 0f, (float)(i + 1) / n, 1f);
                rt.offsetMin = new Vector2(0f, 0f); rt.offsetMax = new Vector2(-3f, 0f);
            }
        }

        static void Rel(RectTransform rt, float x0, float y0, float x1, float y1) {
            UIKit.SetAnchors(rt, x0, y0, x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
