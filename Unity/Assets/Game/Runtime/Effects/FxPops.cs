using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace VoxGame
{
    // Надписи веб-версии (src/a_head.html, src/g_game.js):
    //  • плашка удара «ХУК −12» (.pop): заглавные, тёмная плашка rgba(14,8,12,.72), чёрная обводка; джеб — светлая и мельче,
    //    силовой и встречный — золотая; всплывает на 46 css-px за 0,85 с (появление 12 %, держится до 65 %, гаснет).
    //    Ставится под подбородком соперника по горизонтали точки удара: лицо не закрывает;
    //  • счётчик «КОМБО ×N» у кнопок (веб #cnum: с 2 ударов серии, крупнее с 5);
    //  • метки травм (веб #chips): «перелом · нос», «зубы −3», «челюсть оторвана»; видны последние MaxChips.
    // Строки собраны заранее, слоты — пул; в кадре удара нет новых строк и объектов.
    public class FxPops
    {
        public const int MaxPops = 4;
        public const int MaxChips = 4;
        public const int DmgMax = 199;
        public const float PopLife = 0.85f, PopIn = 0.12f, PopHold = 0.65f, PopRise = 64f;   // 46 css-px × 1,39 (390 → 540)
        public const int PopFontJab = 24, PopFontPower = 30;
        public const float PopXMin = 0.22f, PopXMax = 0.78f, PopXMinLand = 0.25f, PopXMaxLand = 0.75f;
        public const float PlatePadX = 10f, PlatePadY = 6f;
        public const float ComboShow = 1.5f;     // с: как окно серии FightSim (1,5 с игрового времени)
        public const int ComboMin = 2, ComboBig = 5;
        public const int ComboMax = 99;
        public static readonly Vector2 ComboPort = new Vector2(0.05f, 0.165f), ComboLand = new Vector2(0.6f, 0.08f);   // ландшафт: низ между рукой и кнопкой ►, слева кнопки
        public static readonly Vector2 ChipsPort = new Vector2(0.97f, 0.785f), ChipsLand = new Vector2(0.04f, 0.86f);
        public const float ChipH = 30f, ChipGap = 5f;

        // Плашка удара (правка HIT-FX-2): непрозрачная насыщенная заливка и рамка 3 px, рисуется поверх рук (ZTest Always).
        // Силовой и встречный — жёлтый текст на алой плашке с чёрной рамкой; джеб — белый текст на чёрной плашке с алой рамкой.
        static readonly Color PlatePower = new Color32(176, 18, 26, 255), PlateJab = new Color32(18, 12, 16, 255);
        static readonly Color FramePower = new Color32(0, 0, 0, 255), FrameJab = new Color32(225, 30, 36, 255);
        static readonly Color TextPower = new Color32(255, 232, 110, 255), TextJab = new Color32(255, 250, 240, 255);
        public const float FramePx = 3f;
        // Где плашка может стоять (доли кадра): ниже полос HUD, выше счётчика комбо и кнопок; в ландшафте — между кнопками.
        public const float PopTopP = 0.80f, PopBotP = 0.20f, PopTopL = 0.83f, PopBotL = 0.12f, PopSideL = 0.17f, PopMargin = 0.01f;
        static readonly Color Bone  = new Color32(236, 226, 200, 255);
        static readonly Color Gold  = new Color32(255, 196, 64, 255);
        static readonly Color BloodRed = new Color32(200, 35, 28, 255);

        // ---- Строки --------------------------------------------------------

        static readonly string[] Names = { "ДЖЕБ", "ХУК", "АППЕРКОТ" };
        static readonly string[,,] PopStr = BuildPops();   // [вид][встречный][урон]
        static readonly string[] ComboStr = BuildCombo();
        public static readonly string[] ZoneFracture =
            { "перелом · череп", "перелом · скула", "перелом · челюсть", "перелом · рот", "перелом · нос", "перелом · глазница", "перелом · ухо" };
        static readonly string[] TeethStr = BuildTeeth();
        public const string JawOff = "челюсть оторвана", EyeOut = "глаз выбит", EyesOut = "глаза выбиты";

        static string[,,] BuildPops()
        {
            var a = new string[Names.Length, 2, DmgMax + 1];
            for (int k = 0; k < Names.Length; k++)
            for (int c = 0; c < 2; c++)
            for (int d = 0; d <= DmgMax; d++)
                a[k, c, d] = (c == 1 ? "ВСТРЕЧНЫЙ " : "") + Names[k] + " −" + d;
            return a;
        }

        static string[] BuildCombo()
        {
            var a = new string[ComboMax + 1];
            for (int i = 0; i <= ComboMax; i++) a[i] = "КОМБО ×" + i;
            return a;
        }

        static string[] BuildTeeth()
        {
            var a = new string[33];
            for (int i = 0; i < a.Length; i++) a[i] = "зубы −" + i;
            return a;
        }

        // kind: 0 джеб, 1 хук, 2 апперкот.
        public static string PopText(int kind, bool counter, int dmg) =>
            PopStr[Mathf.Clamp(kind, 0, Names.Length - 1), counter ? 1 : 0, Mathf.Clamp(dmg, 0, DmgMax)];
        public static string ComboText(int n) => ComboStr[Mathf.Clamp(n, 0, ComboMax)];
        public static string TeethText(int n) => TeethStr[Mathf.Clamp(n, 0, TeethStr.Length - 1)];

        // ---- Слоты ---------------------------------------------------------

        class Pop
        {
            public RectTransform rt;
            public Image plate;
            public Text text;
            public bool alive;
            public float age;
            public Vector3 world;
            public Color color, fill, frame;
            public Outline outline;
            public Vector2 off;          // сдвиг от точки удара (доли кадра), выбран при появлении
            public int chosen;           // номер тика, в котором выбрано место (раз за кадр)
            public float riseK;          // доля подъёма: у верхней границы области плашка всплывает меньше
            public Rect g0, g1;          // перчатки, по которым выбрано место
            public uint seq;
        }

        class Chip
        {
            public RectTransform rt;
            public Image back;
            public Text text;
            public int key = -1;
            public uint seq;
        }

        readonly Pop[] _pops = new Pop[MaxPops];
        readonly Chip[] _chips = new Chip[MaxChips];
        readonly Camera _cam;
        RectTransform _root, _comboRt, _safe;
        Material _top;
        readonly Rect[] _avoid = new Rect[2];   // экранные прямоугольники перчаток игрока (доли кадра), задаёт FxDirector
        Text _combo;
        float _aspect = 0.4615f;
        uint _seq;
        int _comboN;
        float _comboLeft, _comboPulse;

        public int ActivePops { get { int n = 0; for (int i = 0; i < MaxPops; i++) if (_pops[i].alive) n++; return n; } }
        public int ActiveChips { get { int n = 0; for (int i = 0; i < MaxChips; i++) if (_chips[i].key >= 0) n++; return n; } }
        public bool ComboVisible => _combo.gameObject.activeSelf;
        public string ComboShown => _combo.gameObject.activeSelf ? _combo.text : null;

        // Текст самой поздней живой плашки (тесты, снимки); null — нет.
        public string LastPopText
        {
            get
            {
                Pop best = null;
                for (int i = 0; i < MaxPops; i++) if (_pops[i].alive && (best == null || _pops[i].seq > best.seq)) best = _pops[i];
                return best?.text.text;
            }
        }

        // Самая поздняя живая плашка (замер: прямоугольник, цвет плашки и текста); null — нет.
        public RectTransform LastPopRect
        {
            get
            {
                Pop best = null;
                for (int i = 0; i < MaxPops; i++) if (_pops[i].alive && (best == null || _pops[i].seq > best.seq)) best = _pops[i];
                return best?.rt;
            }
        }
        public float LastPopAge
        {
            get
            {
                Pop best = null;
                for (int i = 0; i < MaxPops; i++) if (_pops[i].alive && (best == null || _pops[i].seq > best.seq)) best = _pops[i];
                return best != null ? best.age : -1f;
            }
        }
        public RectTransform ComboRect => _comboRt;

        public FxPops(Transform parent, Camera cam)
        {
            _cam = cam;
            var go = new GameObject("FxPops", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _root = go.GetComponent<RectTransform>();
            UIKit.SetAnchors(_root, 0f, 0f, 1f, 1f);
            _root.offsetMin = _root.offsetMax = Vector2.zero;
            _safe = parent != null && parent.parent != null ? parent.parent.Find("SafeArea") as RectTransform : null;
            var ui = Shader.Find("UI/Default");
            if (ui != null)
            {
                _top = new Material(ui) { name = "FxPopsTop" };
                _top.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);   // поверх рук игрока при любой глубине
            }

            for (int i = 0; i < MaxPops; i++)
            {
                var rt = NewRect("Pop" + i, _root);
                var plate = rt.gameObject.AddComponent<Image>();
                plate.color = PlateJab; plate.raycastTarget = false;
                if (_top != null) plate.material = _top;
                var ol = plate.gameObject.AddComponent<Outline>();
                ol.effectDistance = new Vector2(FramePx, -FramePx); ol.useGraphicAlpha = true;
                var t = UIKit.Label(rt, "Text", PopFontPower, Gold, semiBold: true);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                Hard(t, 2f);
                if (_top != null) t.material = _top;
                Stretch(t.rectTransform);
                rt.gameObject.SetActive(false);
                _pops[i] = new Pop { rt = rt, plate = plate, text = t, outline = ol };
            }

            _combo = UIKit.Label(_root, "Combo", 40, UIKit.Accent, semiBold: true, align: TextAnchor.MiddleLeft);
            _combo.horizontalOverflow = HorizontalWrapMode.Overflow;
            Hard(_combo, 3f);
            _comboRt = _combo.rectTransform;
            _comboRt.pivot = new Vector2(0f, 0.5f);
            _comboRt.sizeDelta = new Vector2(320f, 56f);
            _combo.gameObject.SetActive(false);

            for (int i = 0; i < MaxChips; i++)
            {
                var rt = NewRect("Chip" + i, _root);
                var back = rt.gameObject.AddComponent<Image>();
                back.raycastTarget = false;
                var t = UIKit.Label(rt, "Text", 17, Color.white, semiBold: true);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.GetComponent<Outline>().enabled = false;
                Stretch(t.rectTransform);
                rt.gameObject.SetActive(false);
                _chips[i] = new Chip { rt = rt, back = back, text = t };
            }
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            UIKit.SetAnchors(rt, 0f, 0f, 1f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // Жёсткая чёрная обводка и тень вниз-вправо (как text-shadow веба).
        static void Hard(Text t, float d)
        {
            var o = t.GetComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 1f);
            o.effectDistance = new Vector2(d, -d);
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 1f);
            sh.effectDistance = new Vector2(d, -d * 1.5f);
        }

        // ---- Показ ---------------------------------------------------------

        // Плашка удара. at — точка под подбородком (мир), по горизонтали — точка удара. gold — силовой или встречный.
        public void ShowPop(string text, Vector3 at, bool gold)
        {
            Pop s = null;
            for (int i = 0; i < MaxPops; i++)
            {
                if (!_pops[i].alive) { s = _pops[i]; break; }
                if (s == null || _pops[i].seq < s.seq) s = _pops[i];
            }
            // на экране одна плашка удара: прежняя гаснет сразу, иначе серия ляжет стопкой на грудь
            for (int i = 0; i < MaxPops; i++) if (_pops[i].alive) { _pops[i].alive = false; _pops[i].rt.gameObject.SetActive(false); }
            s.alive = true; s.age = 0f; s.seq = ++_seq; s.world = at; s.chosen = -1;
            if (!ReferenceEquals(s.text.text, text)) s.text.text = text;
            s.text.fontSize = gold ? PopFontPower : PopFontJab;
            s.color = gold ? TextPower : TextJab;
            s.fill = gold ? PlatePower : PlateJab;
            s.frame = gold ? FramePower : FrameJab;
            s.off = Vector2.zero; s.g0 = s.g1 = Rect.zero;
            s.rt.sizeDelta = new Vector2(s.text.preferredWidth + PlatePadX * 2f, s.text.fontSize + PlatePadY * 2f);
            s.rt.gameObject.SetActive(true);
            s.rt.SetAsLastSibling();
            Place(s);
        }

        // Счётчик серии после удара игрока; серия < ComboMin прячет счётчик.
        public void OnCombo(int n)
        {
            if (n < ComboMin) { _comboLeft = 0f; return; }
            _comboN = n; _comboLeft = ComboShow; _comboPulse = 1f;
            string s = ComboText(n);
            if (!ReferenceEquals(_combo.text, s)) _combo.text = s;
            _combo.fontSize = n >= ComboBig ? 48 : 40;
            _combo.color = n >= ComboBig ? new Color(1f, 0.42f, 0.16f) : (Color)UIKit.Accent;
        }

        public void HideCombo() => _comboLeft = 0f;

        // Перчатки игрока на экране (доли кадра, Rect.zero — нет); плашка выбирает место, которое они не закрывают.
        public void SetAvoid(Rect glove0, Rect glove1) { _avoid[0] = glove0; _avoid[1] = glove1; }

        // Маска силуэта перчаток (MaskN×MaskN клеток на кадр) и её суммы по прямоугольникам; пока маски нет — короба SetAvoid.
        public const int MaskN = 96;
        public const float MaskFree = 0.04f, MaskW = 10f, MaskPad = 0.9f;   // бесплатная доля перекрытия маски, вес перекрытия, пятно точки (доля шага)
        readonly bool[] _mask = new bool[MaskN * MaskN];
        readonly int[] _sat = new int[(MaskN + 1) * (MaskN + 1)];
        bool _maskOn;
        int _tick, _maskTick = -1;   // маска и место — не чаще раза за кадр (ShowPop и Tick в одном кадре)
        public bool NeedAvoid { get { if (_maskTick == _tick) return false; for (int i = 0; i < MaxPops; i++) if (_pops[i].alive && _pops[i].age < PopIn * PopLife) return true; return false; } }
        public bool MaskStale => _maskTick != _tick;
        public void BeginMask() { System.Array.Clear(_mask, 0, _mask.Length); _maskOn = false; _maskTick = _tick; }
        public void MaskSplat(float x, float y, float hx, float hy)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((x - hx) * MaskN)), x1 = Mathf.Min(MaskN - 1, Mathf.FloorToInt((x + hx) * MaskN));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((y - hy) * MaskN)), y1 = Mathf.Min(MaskN - 1, Mathf.FloorToInt((y + hy) * MaskN));
            for (int j = y0; j <= y1; j++) for (int i = x0; i <= x1; i++) _mask[j * MaskN + i] = true;
            if (x1 >= x0 && y1 >= y0) _maskOn = true;
        }
        public void EndMask()
        {
            const int W = MaskN + 1;
            for (int j = 0; j < MaskN; j++)
            {
                int row = 0;
                for (int i = 0; i < MaskN; i++) { if (_mask[j * MaskN + i]) row++; _sat[(j + 1) * W + i + 1] = _sat[j * W + i + 1] + row; }
            }
        }
        // площадь маски внутри прямоугольника (доли кадра), по клеткам с центром внутри
        float MaskOv(Rect r)
        {
            const int W = MaskN + 1;
            int x0 = Mathf.Clamp(Mathf.RoundToInt(r.xMin * MaskN), 0, MaskN), x1 = Mathf.Clamp(Mathf.RoundToInt(r.xMax * MaskN), 0, MaskN);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(r.yMin * MaskN), 0, MaskN), y1 = Mathf.Clamp(Mathf.RoundToInt(r.yMax * MaskN), 0, MaskN);
            if (x1 <= x0 || y1 <= y0) return 0f;
            int n = _sat[y1 * W + x1] - _sat[y0 * W + x1] - _sat[y1 * W + x0] + _sat[y0 * W + x0];
            return n / (float)(MaskN * MaskN);
        }

        // Метка травмы с ключом key: тот же ключ обновляет метку, новый — добавляет (самая старая уходит).
        public void SetChip(int key, string text, bool bad)
        {
            Chip c = null;
            for (int i = 0; i < MaxChips; i++) if (_chips[i].key == key) { c = _chips[i]; break; }
            if (c == null)
                for (int i = 0; i < MaxChips; i++)
                {
                    if (_chips[i].key < 0) { c = _chips[i]; break; }
                    if (c == null || _chips[i].seq < c.seq) c = _chips[i];
                }
            if (c.key != key) c.seq = ++_seq;
            c.key = key;
            if (!ReferenceEquals(c.text.text, text)) c.text.text = text;
            c.back.color = bad ? BloodRed : Bone;
            c.text.color = bad ? Color.white : new Color32(32, 24, 32, 255);
            c.rt.sizeDelta = new Vector2(c.text.preferredWidth + 14f, ChipH);
            c.rt.gameObject.SetActive(true);
            LayoutChips();
        }

        public void SetView(float aspect)
        {
            if (Mathf.Abs(aspect - _aspect) < 1e-4f) return;
            _aspect = aspect;
            for (int i = 0; i < MaxPops; i++) if (_pops[i].alive) Place(_pops[i]);
            LayoutChips();
        }

        // ---- Тик -----------------------------------------------------------

        // udt — реальное время (надписи), gdt — игровое (окно серии).
        public void Tick(float udt, float gdt)
        {
            for (int i = 0; i < MaxPops; i++)
            {
                var s = _pops[i];
                if (!s.alive) continue;
                s.age += udt;
                if (s.age >= PopLife) { s.alive = false; s.rt.gameObject.SetActive(false); continue; }
                Place(s);
            }
            _tick++;

            _comboLeft = Mathf.Max(0f, _comboLeft - gdt);
            bool on = _comboLeft > 0f;
            if (_combo.gameObject.activeSelf != on) _combo.gameObject.SetActive(on);
            if (on)
            {
                _comboPulse = Mathf.Max(0f, _comboPulse - udt * 6f);
                float k = 1f + 0.25f * _comboPulse * _comboPulse;
                _comboRt.localScale = new Vector3(k, k, 1f);
                _comboRt.anchorMin = _comboRt.anchorMax = Land ? ComboLand : ComboPort;
                _comboRt.anchoredPosition = Vector2.zero;
                var c = _combo.color; c.a = Mathf.Clamp01(_comboLeft / 0.3f); _combo.color = c;
            }
        }

        bool Land => _aspect > 1f;

        void Place(Pop s)
        {
            float k = s.age / PopLife;
            float a = k < PopIn ? k / PopIn : k < PopHold ? 1f : 1f - (k - PopHold) / (1f - PopHold);
            Vector2 p = _cam != null ? FxBanners.ProjectView(_cam, _aspect, s.world, new Vector2(0.5f, 0.55f)) : new Vector2(0.5f, 0.55f);
            Vector2 root = RootSize();
            Vector2 half = new Vector2((s.rt.sizeDelta.x + 2f * FramePx) * 0.5f / root.x, (s.rt.sizeDelta.y + 2f * FramePx) * 0.5f / root.y);
            float riseN = PopRise / root.y;
            // место выбирается, пока плашка проявляется (0,1 с): перчатка в это время у лица; после — место не меняется
            if (k < PopIn && s.chosen != _tick)
            {
                s.chosen = _tick;
                s.g0 = _avoid[0]; s.g1 = _avoid[1];
                s.off = Choose(p, half, riseN, s.g0, s.g1, out s.riseK);
            }
            p = Fit(p + s.off, half, riseN * s.riseK);
            s.rt.anchorMin = s.rt.anchorMax = p;
            // всплывает от +8 px (появление) до −46 css-px, как margin-top в @keyframes pop
            float rise = k < PopIn ? -11f * (1f - k / PopIn) : PopRise * s.riseK * Mathf.Clamp01((k - PopIn) / (1f - PopIn));
            s.rt.anchoredPosition = new Vector2(0f, rise);
            var c = s.color; c.a = a; s.text.color = c;
            var pc = s.fill; pc.a *= a; s.plate.color = pc;
            s.outline.effectColor = s.frame;
        }

        // Размер холста в его единицах при пропорциях кадра _aspect, как его задаёт CanvasScaler UIRoot на телефоне
        // (опорное 540×1170 или 1170×540, match 0,5): не зависит от окна редактора и снимков.
        Vector2 RootSize()
        {
            float r = Land ? 1170f / 540f : 540f / 1170f, q = Mathf.Sqrt(_aspect / r);
            return Land ? new Vector2(1170f * q, 540f / q) : new Vector2(540f * q, 1170f / q);
        }

        // Допустимая область центра плашки: safe-area, ниже полос HUD, выше комбо и кнопок; в ландшафте — между кнопками.
        // Плашка всплывает на riseN, поэтому сверху запас на весь подъём, снизу — на опускание при появлении.
        Vector2 Fit(Vector2 c, Vector2 half, float riseN)
        {
            Vector2 smin = _safe != null ? _safe.anchorMin : Vector2.zero, smax = _safe != null ? _safe.anchorMax : Vector2.one;
            float x0 = Mathf.Max(smin.x, Land ? PopSideL : 0f) + PopMargin + half.x;
            float x1 = Mathf.Min(smax.x, Land ? 1f - PopSideL : 1f) - PopMargin - half.x;
            float y0 = Mathf.Max(smin.y, Land ? PopBotL : PopBotP) + PopMargin + half.y + riseN * (11f / PopRise);
            float y1 = Mathf.Min(smax.y, Land ? PopTopL : PopTopP) - PopMargin - half.y - riseN;
            if (x1 < x0) x0 = x1 = 0.5f * (x0 + x1);
            if (y1 < y0) y0 = y1 = 0.5f * (y0 + y1);
            return new Vector2(Mathf.Clamp(c.x, x0, x1), Mathf.Clamp(c.y, y0, y1));
        }

        public const int OffNX = 24, OffNY = 22;         // грубая сетка: ±24 шага по 0,04 и ±22 шага по 0,03 меньшей стороны
        public const float OffStepX = 0.04f, OffStepY = 0.03f;

        // Место плашки рядом с точкой удара (доработка 2): наименьшая цена = перекрытие перчатками и счётчиком комбо
        // (доля площади ×10) плюс расстояние центра до экранной точки удара в долях меньшей стороны кадра. Так плашка
        // встаёт вплотную к краю перчатки со свободной стороны. Перебор 49×45 мест и 9×9 уточнения (шаг вчетверо мельче), без выделений памяти.
        Rect _g0, _g1;
        static Vector3[] _offs;   // сдвиги грубой сетки в долях меньшей стороны (x, y, длина), по возрастанию длины
        static Vector3[] Offsets()
        {
            if (_offs != null) return _offs;
            var l = new System.Collections.Generic.List<Vector3>((2 * OffNX + 1) * (2 * OffNY + 1));
            for (int iy = -OffNY; iy <= OffNY; iy++)
            for (int ix = -OffNX; ix <= OffNX; ix++)
            {
                float x = ix * OffStepX, y = iy * OffStepY;
                l.Add(new Vector3(x, y, Mathf.Sqrt(x * x + y * y)));
            }
            l.Sort((a, b) => a.z.CompareTo(b.z));
            return _offs = l.ToArray();
        }
        Vector2 Choose(Vector2 p, Vector2 half, float riseN, Rect g0, Rect g1, out float riseK)
        {
            Rect combo = ComboRectN();
            _g0 = Inset(g0); _g1 = Inset(g1);   // запасной путь без маски: короб сетки шире силуэта перчатки (скруглённые края, манжета)
            float best = float.MaxValue; Vector2 bo = Vector2.zero;
            // границы области центра без запаса на подъём — один раз на выбор (Fit читает safe-area, это дорого)
            Vector2 lo = Fit(new Vector2(-2f, -2f), half, 0f), hi = Fit(new Vector2(2f, 2f), half, 0f);
            float top = hi.y;
            riseK = 1f;
            float kx = Land ? _aspect : 1f, ky = Land ? 1f : 1f / _aspect;   // доли кадра → доли меньшей стороны
            // шаг сетки — в долях меньшей стороны: в ландшафте по ширине мельче; второй проход уточняет лучший шаг вчетверо
            float sx = OffStepX / kx, sy = OffStepY / ky;   // шаги в долях кадра (для уточнения)
            // места по возрастанию расстояния: цена не меньше расстояния, поэтому дальние после лучшей цены не нужны
            var offs = Offsets();
            for (int i = 0; i < offs.Length; i++)
            {
                Vector3 o = offs[i];
                if (o.z >= best) break;
                Try(p, p + new Vector2(o.x / kx, o.y / ky), half, riseN, top, lo, hi, kx, ky, combo, ref best, ref bo, ref riseK);
            }
            for (float q = 0.25f; q > 0.05f; q *= 0.25f)   // уточнение: шаг 1/4, затем 1/16 грубого
            {
                Vector2 c0 = p + bo;
                for (int iy = -4; iy <= 4; iy++)
                for (int ix = -4; ix <= 4; ix++)
                    Try(p, c0 + new Vector2(ix * sx * q, iy * sy * q), half, riseN, top, lo, hi, kx, ky, combo, ref best, ref bo, ref riseK);
            }
            return bo;
        }

        void Try(Vector2 p, Vector2 want, Vector2 half, float riseN, float top, Vector2 lo, Vector2 hi, float kx, float ky, Rect combo,
                 ref float best, ref Vector2 bo, ref float riseK)
        {
            Vector2 c = new Vector2(Mathf.Clamp(want.x, lo.x, hi.x), Mathf.Clamp(want.y, lo.y, hi.y));
            float dx = (c.x - p.x) * kx, dy = (c.y - p.y) * ky;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            // два варианта: всплывает (сколько позволяет верх области) или стоит на месте — у края перчатки подъём
            // завёл бы плашку на неё
            for (int v = 0; v < 2; v++)
            {
                float rk = v == 0 ? Mathf.Clamp01((top - c.y) / riseN) : 0f;
                // плашка за время показа: от появления до конца удержания (всплывает примерно на 0,6 подъёма)
                Rect r = Rect.MinMaxRect(c.x - half.x, c.y - half.y, c.x + half.x, c.y + half.y + riseN * rk * 0.6f);
                float ov = (_maskOn ? MaskOv(r) : Ov(r, _g0) + Ov(r, _g1) - Ov(r, Inter(_g0, _g1))) + Ov(r, combo);
                // маска шире силуэта (пятно точки MaskPad шага): первые MaskFree площади не штрафуются
                float cost = MaskW * Mathf.Max(0f, ov / Mathf.Max(1e-6f, r.width * r.height) - (_maskOn ? MaskFree : 0f)) + dist + (v == 1 ? 0.001f : 0f);
                if (cost < best) { best = cost; bo = c - p; riseK = rk; }
            }
        }

        Rect ComboRectN()
        {
            Vector2 root = RootSize();
            Vector2 a = Land ? ComboLand : ComboPort;
            float w = _comboRt.sizeDelta.x / root.x, h = _comboRt.sizeDelta.y / root.y;
            return Rect.MinMaxRect(a.x, a.y - h * 0.5f, a.x + w, a.y + h * 0.5f);
        }

        // доля размера короба с каждой стороны: короб углов сетки → силуэт перчатки (по бокам скруглён сильнее, чем сверху и снизу)
        public const float GloveInsetX = 0.17f, GloveInsetY = 0.07f;
        static Rect Inset(Rect r)
        {
            if (r.width <= 0f) return r;
            float ix = r.width * GloveInsetX, iy = r.height * GloveInsetY;
            return Rect.MinMaxRect(r.xMin + ix, r.yMin + iy, r.xMax - ix, r.yMax - iy);
        }

        static Rect Inter(Rect a, Rect b)
        {
            float x0 = Mathf.Max(a.xMin, b.xMin), y0 = Mathf.Max(a.yMin, b.yMin), x1 = Mathf.Min(a.xMax, b.xMax), y1 = Mathf.Min(a.yMax, b.yMax);
            return x1 > x0 && y1 > y0 ? Rect.MinMaxRect(x0, y0, x1, y1) : Rect.zero;
        }

        static float Ov(Rect a, Rect b) { var r = Inter(a, b); return r.width * r.height; }

        void LayoutChips()
        {
            // по порядку появления, новые ниже; в портрете — справа под полосами HUD, в ландшафте — слева под полосой соперника
            uint last = 0;
            for (int order = 0; order < MaxChips; order++)
            {
                Chip next = null;
                for (int i = 0; i < MaxChips; i++)
                {
                    var c = _chips[i];
                    if (c.key >= 0 && c.seq > last && (next == null || c.seq < next.seq)) next = c;
                }
                if (next == null) break;
                last = next.seq;
                next.rt.anchorMin = next.rt.anchorMax = Land ? ChipsLand : ChipsPort;
                next.rt.pivot = new Vector2(Land ? 0f : 1f, 1f);
                next.rt.anchoredPosition = new Vector2(0f, -order * (ChipH + ChipGap));
            }
        }

        // ---- Сброс ---------------------------------------------------------

        public void Clear()
        {
            for (int i = 0; i < MaxPops; i++) { _pops[i].alive = false; _pops[i].rt.gameObject.SetActive(false); }
            for (int i = 0; i < MaxChips; i++) { _chips[i].key = -1; _chips[i].rt.gameObject.SetActive(false); }
            _comboLeft = 0f; _combo.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (_root != null) GameUtil.Destroy(_root.gameObject);
            if (_top != null) GameUtil.Destroy(_top);
            _root = null; _top = null;
        }
    }
}
