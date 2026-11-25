using UnityEngine;
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

        static readonly Color Plate = new Color(14f / 255f, 8f / 255f, 12f / 255f, 0.72f);
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
            public Color color;
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
        RectTransform _root, _comboRt;
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

        public FxPops(Transform parent, Camera cam)
        {
            _cam = cam;
            var go = new GameObject("FxPops", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _root = go.GetComponent<RectTransform>();
            UIKit.SetAnchors(_root, 0f, 0f, 1f, 1f);
            _root.offsetMin = _root.offsetMax = Vector2.zero;

            for (int i = 0; i < MaxPops; i++)
            {
                var rt = NewRect("Pop" + i, _root);
                var plate = rt.gameObject.AddComponent<Image>();
                plate.color = Plate; plate.raycastTarget = false;
                var t = UIKit.Label(rt, "Text", PopFontPower, Gold, semiBold: true);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                Hard(t, 2f);
                Stretch(t.rectTransform);
                rt.gameObject.SetActive(false);
                _pops[i] = new Pop { rt = rt, plate = plate, text = t };
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
            s.alive = true; s.age = 0f; s.seq = ++_seq; s.world = at;
            if (!ReferenceEquals(s.text.text, text)) s.text.text = text;
            s.text.fontSize = gold ? PopFontPower : PopFontJab;
            s.color = gold ? Gold : Bone;
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
            p.x = Mathf.Clamp(p.x, Land ? PopXMinLand : PopXMin, Land ? PopXMaxLand : PopXMax);
            p.y = Mathf.Clamp(p.y, 0.2f, 0.8f);
            s.rt.anchorMin = s.rt.anchorMax = p;
            // всплывает от +8 px (появление) до −46 css-px, как margin-top в @keyframes pop
            float rise = k < PopIn ? -11f * (1f - k / PopIn) : PopRise * Mathf.Clamp01((k - PopIn) / (1f - PopIn));
            s.rt.anchoredPosition = new Vector2(0f, rise);
            var c = s.color; c.a = a; s.text.color = c;
            var pc = Plate; pc.a *= a; s.plate.color = pc;
        }

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
            _root = null;
        }
    }
}
