using UnityEngine;
using UnityEngine.UI;

namespace VoxGame
{
    public enum BannerStyle { Power, Mega, Counter, Combo, Call, Big, Huge, Info }

    // Крупные надписи удара (≤ MaxBanners) и всплывающие числа урона (≤ MaxNumbers).
    // Пулы собраны заранее; при переполнении вытесняется самая старая. Строки заготовлены — в кадре нет новых строк.
    // Анимация на реальном времени: выскакивание 0 → 1.15 → 1, лёгкий наклон, выцветание.
    public class FxBanners
    {
        public const int MaxBanners = 5;
        public const int MaxNumbers = 12;
        public const int ComboMin = 3;           // с какой серии показывать «СЕРИЯ ×N»
        public const int ComboTextMax = 30;
        public const int NumberTextMax = 199;

        // Раскладка (доли экрана и опорные пиксели холста). Голова соперника занимает верх кадра под полосами HUD,
        // поэтому надписи ниже лица: слово удара — на уровне груди, центральные — у пояса; лицо остаётся открытым.
        public const float CenterYPort = 0.40f, CenterYLand = 0.34f;   // центральные надписи (в ландшафте выше подсказки жестов)
        public const float WordYPort = 0.55f, WordYLand = 0.45f;       // слово удара (по горизонтали — у точки удара)
        public const float NumberSide = 0.13f;   // число урона сбоку от точки удара, доля ширины
        public const float StackStep = 92f;      // сдвиг вверх старых центральных надписей при новой
        public const float WorldDrift = 40f;     // всплытие слова удара за жизнь
        public const float NumberRise = 110f;    // всплытие числа за жизнь
        public const float NumberLife = 0.85f;
        public const float PopIn = 0.08f, PopSettle = 0.10f, PopPeak = 1.15f, FadeOut = 0.25f;
        public const float TiltMax = 7f;         // градусы
        public const float BannerWidth = 600f;   // шире холста портрета: длинное слово не переносится по буквам
        public const float WorldXMin = 0.36f, WorldXMax = 0.64f;   // слово удара не уходит за край
        public const float WorldXMinLand = 0.30f, WorldXMaxLand = 0.70f;

        // ---- Слова (свои) --------------------------------------------------

        public const string Finish    = "ДОБЕЙ!";
        public const string Crushed   = "СОКРУШИЛ!";
        public const string Knockdown = "НОКДАУН!";
        public const string Knockout  = "В НОКАУТ!";
        public const string Counter   = "КОНТРА!";
        public const string Fracture  = "ХРУСТ!";
        public const string ToJaw     = "В ЧЕЛЮСТЬ!";
        public const string ToTeeth   = "ПО ЗУБАМ!";
        public const string ToNose    = "В НОС!";
        public const string ToEye     = "В ГЛАЗ!";
        public const string Miss      = "УКЛОН!";
        public const string Blocked   = "В ЗАЩИТУ";
        public const string Champion  = "ЧЕМПИОН!";
        public const string BeltWon   = "ПОЯС ТВОЙ!";
        public const string Victory   = "ПОБЕДА!";

        public static readonly string[] HookWords  = { "ХРЯСЬ!", "БАХ!", "ВЛЕПИЛ!", "ТРЕСК!" };
        public static readonly string[] UpperWords = { "СНИЗУ!", "ПОДБРОСИЛ!", "В ПОДБОРОДОК!" };
        public static readonly string[] MegaWords  = { "ГРОМ!", "РАЗНОС!", "НАПОВАЛ!" };

        static readonly string[] ComboStr = BuildCombo();
        static readonly string[] NumberStr = BuildNumbers();

        static string[] BuildCombo()
        {
            var a = new string[ComboTextMax + 1];
            for (int i = 0; i <= ComboTextMax; i++) a[i] = "СЕРИЯ ×" + i;
            return a;
        }

        static string[] BuildNumbers()
        {
            var a = new string[NumberTextMax + 1];
            for (int i = 0; i <= NumberTextMax; i++) a[i] = i.ToString();
            return a;
        }

        public static string ComboText(int n) => ComboStr[Mathf.Clamp(n, 0, ComboTextMax)];
        public static string NumberText(int n) => NumberStr[Mathf.Clamp(n, 0, NumberTextMax)];

        // ---- Слоты ---------------------------------------------------------

        class Slot
        {
            public RectTransform rt;
            public Text text;
            public bool alive;
            public int mode;              // Center, Word или Number
            public float age, life, size, rise, tilt;
            public float y, yLand;        // своя высота центральной надписи (доля экрана), 0 — по умолчанию
            public Vector3 world;         // точка удара (слово и число)
            public Vector2 jitter;
            public float stack, stackTarget;
            public Color color;
            public uint seq;
        }

        readonly Slot[] _banners = new Slot[MaxBanners];
        readonly Slot[] _numbers = new Slot[MaxNumbers];
        const int Center = 0, Word = 1, Number = 2;

        readonly Camera _cam;
        float _aspect = 0.4615f;   // ширина / высота кадра (из SetView: снимки рисуются не в размер Screen)
        RectTransform _root;
        uint _seq;
        uint _rng = 0x68E31DA4u;

        public int ActiveBanners { get { int n = 0; for (int i = 0; i < MaxBanners; i++) if (_banners[i].alive) n++; return n; } }
        public int ActiveNumbers { get { int n = 0; for (int i = 0; i < MaxNumbers; i++) if (_numbers[i].alive) n++; return n; } }

        // Текст живой надписи с самым поздним появлением (для тестов и снимков); null — нет.
        public string LastBannerText
        {
            get
            {
                Slot best = null;
                for (int i = 0; i < MaxBanners; i++)
                    if (_banners[i].alive && (best == null || _banners[i].seq > best.seq)) best = _banners[i];
                return best?.text.text;
            }
        }

        // parent — растянутый на весь холст слой; cam — для проекции точки удара (null — всё по центру).
        public FxBanners(Transform parent, Camera cam)
        {
            _cam = cam;
            var go = new GameObject("FxBanners", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _root = go.GetComponent<RectTransform>();
            UIKit.SetAnchors(_root, 0f, 0f, 1f, 1f);
            _root.offsetMin = _root.offsetMax = Vector2.zero;

            for (int i = 0; i < MaxNumbers; i++) _numbers[i] = MakeSlot("Num" + i, 40, 220f, 80f, 2f, 3f);
            for (int i = 0; i < MaxBanners; i++) _banners[i] = MakeSlot("Banner" + i, 60, BannerWidth, 150f, 3f, 6f);
        }

        Slot MakeSlot(string name, int fontSize, float w, float h, float outline, float shadow)
        {
            var t = UIKit.Label(_root, name, fontSize, UIKit.TextMain, semiBold: true);
            var o = t.GetComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.95f);
            o.effectDistance = new Vector2(outline, -outline);
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0.25f, 0f, 0f, 0.75f);
            sh.effectDistance = new Vector2(shadow * 0.6f, -shadow);
            var rt = t.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            t.gameObject.SetActive(false);
            return new Slot { rt = rt, text = t };
        }

        // ---- Показ ---------------------------------------------------------

        // Крупная надпись по центру; старые центральные уходят вверх.
        public void ShowCenter(string word, BannerStyle style) => ShowCenter(word, style, 0f);

        // life > 0 — своё время жизни (надпись к сцене, например к поясу), иначе по стилю;
        // yPort / yLand > 0 — своя высота в портрете и ландшафте (доля экрана), иначе общая для центральных.
        public void ShowCenter(string word, BannerStyle style, float life, float yPort = 0f, float yLand = 0f)
        {
            // старые центральные уходят вверх; надписи со своей высотой (к сцене) стопку не двигают и в ней не едут
            if (yPort <= 0f)
                for (int i = 0; i < MaxBanners; i++)
                    if (_banners[i].alive && _banners[i].mode == Center && _banners[i].y <= 0f) _banners[i].stackTarget += StackStep;
            var s = Take(_banners);
            Setup(s, word, style);
            if (life > 0f) s.life = life;
            s.mode = Center;
            s.y = yPort;
            s.yLand = yLand;
            s.rise = 0f;
            Place(s, 0f);
        }

        // Слово удара: по горизонтали у точки удара (мир), по высоте — на уровне груди соперника.
        public void ShowAt(string word, Vector3 world, BannerStyle style)
        {
            // на экране одно слово удара: прежнее гаснет сразу, иначе два слова ложатся друг на друга
            for (int i = 0; i < MaxBanners; i++)
                if (_banners[i].alive && _banners[i].mode == Word)
                {
                    _banners[i].alive = false;
                    _banners[i].text.gameObject.SetActive(false);
                }
            var s = Take(_banners);
            Setup(s, word, style);
            s.mode = Word;
            s.world = world;
            s.rise = WorldDrift;
            Place(s, 0f);
        }

        // Пропорции кадра; живые надписи сразу переставляются (поворот экрана, снимки портрет/ландшафт).
        public void SetView(float aspect)
        {
            if (Mathf.Abs(aspect - _aspect) < 1e-4f) return;
            _aspect = aspect;
            for (int i = 0; i < MaxBanners; i++) if (_banners[i].alive) Place(_banners[i], _banners[i].age);
            for (int i = 0; i < MaxNumbers; i++) if (_numbers[i].alive) Place(_numbers[i], _numbers[i].age);
        }

        // Всплывающее число урона. tier: 0 — джеб, 1 — силовой, 2 — мега.
        public void ShowNumber(int value, Vector3 world, int tier)
        {
            var s = Take(_numbers);
            string str = NumberText(value);
            if (!ReferenceEquals(s.text.text, str)) s.text.text = str;
            s.text.fontSize = tier >= 2 ? 50 : tier == 1 ? 40 : 30;
            s.color = tier >= 2 ? new Color(1f, 0.32f, 0.18f) : tier == 1 ? (Color)UIKit.MegaGold : UIKit.TextMain;
            s.life = NumberLife;
            s.size = 1f;
            s.tilt = 0f;
            s.mode = Number;
            s.world = world;
            // небольшой разброс, чтобы серия чисел не ложилась в одну точку
            s.jitter = new Vector2(NumberSide + (Rand01() - 0.5f) * 0.06f, (Rand01() - 0.5f) * 0.03f);
            s.stack = s.stackTarget = 0f;
            s.rise = NumberRise;
            Place(s, 0f);
        }

        Slot Take(Slot[] pool)
        {
            Slot best = null;
            for (int i = 0; i < pool.Length; i++)
            {
                if (!pool[i].alive) { best = pool[i]; break; }
                if (best == null || pool[i].seq < best.seq) best = pool[i];
            }
            best.alive = true;
            best.age = 0f;
            best.seq = ++_seq;
            best.stack = best.stackTarget = 0f;
            best.y = best.yLand = 0f;
            best.text.gameObject.SetActive(true);
            best.rt.SetAsLastSibling();
            return best;
        }

        void Setup(Slot s, string word, BannerStyle style)
        {
            if (!ReferenceEquals(s.text.text, word)) s.text.text = word;
            int font; float life; Color c;
            switch (style)
            {
                case BannerStyle.Mega:    font = 72; life = 1.10f; c = UIKit.MegaGold; break;
                case BannerStyle.Counter: font = 62; life = 0.90f; c = UIKit.Accent; break;
                case BannerStyle.Combo:   font = 40; life = 0.80f; c = UIKit.Accent; break;
                case BannerStyle.Call:    font = 76; life = 1.30f; c = new Color(1f, 0.27f, 0.20f); break;
                case BannerStyle.Big:     font = 76; life = 1.60f; c = UIKit.TextMain; break;
                case BannerStyle.Huge:    font = 80; life = 2.20f; c = UIKit.MegaGold; break;
                case BannerStyle.Info:    font = 30; life = 0.60f; c = UIKit.TextSub; break;
                default:                  font = 52; life = 0.75f; c = UIKit.TextMain; break;
            }
            s.text.fontSize = font;
            s.life = life;
            s.color = c;
            s.size = 1f;
            s.tilt = (Rand01() * 2f - 1f) * TiltMax * (style == BannerStyle.Info || style == BannerStyle.Combo ? 0.4f : 1f);
        }

        // ---- Тик -----------------------------------------------------------

        public void Tick(float udt)
        {
            for (int i = 0; i < MaxBanners; i++) Step(_banners[i], udt);
            for (int i = 0; i < MaxNumbers; i++) Step(_numbers[i], udt);
        }

        void Step(Slot s, float udt)
        {
            if (!s.alive) return;
            s.age += udt;
            if (s.age >= s.life)
            {
                s.alive = false;
                s.text.gameObject.SetActive(false);
                return;
            }
            s.stack = Mathf.Lerp(s.stack, s.stackTarget, 1f - Mathf.Exp(-14f * udt));
            Place(s, s.age);
        }

        void Place(Slot s, float age)
        {
            float k;
            if (age < PopIn) k = PopPeak * EaseOut(age / PopIn);
            else if (age < PopIn + PopSettle) k = Mathf.Lerp(PopPeak, 1f, (age - PopIn) / PopSettle);
            else k = 1f;
            float fadeT = Mathf.Clamp01((s.life - age) / FadeOut);
            if (fadeT < 1f) k *= 1f + 0.06f * (1f - fadeT);

            s.rt.anchorMin = s.rt.anchorMax = Anchor(s);
            s.rt.anchoredPosition = new Vector2(0f, s.stack + s.rise * (age / Mathf.Max(0.01f, s.life)));
            s.rt.localRotation = Quaternion.Euler(0f, 0f, s.tilt);
            s.rt.localScale = new Vector3(k * s.size, k * s.size, 1f);
            var c = s.color; c.a = fadeT;
            s.text.color = c;
        }

        static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }

        bool Land => _aspect > 1f;

        Vector2 Anchor(Slot s)
        {
            if (s.mode == Center)
            {
                float y = Land ? s.yLand : s.y;
                return new Vector2(0.5f, y > 0f ? y : Land ? CenterYLand : CenterYPort);
            }
            Vector2 p = Project(s.world, new Vector2(0.5f, 0.6f));
            if (s.mode == Word)
                return new Vector2(Mathf.Clamp(p.x, Land ? WorldXMinLand : WorldXMin, Land ? WorldXMaxLand : WorldXMax),
                                   Land ? WordYLand : WordYPort);
            p += new Vector2(s.jitter.x * (Land ? 0.5f : 1f), s.jitter.y);
            return new Vector2(Mathf.Clamp(p.x, 0.12f, 0.88f), Mathf.Clamp(p.y, 0.15f, 0.80f));
        }

        // Точка мира → доли кадра по положению камеры, её полю зрения и пропорциям из SetView.
        Vector2 Project(Vector3 world, Vector2 fallback)
        {
            if (_cam == null) return fallback;
            return ProjectView(_cam, _aspect, world, fallback);
        }

        public static Vector2 ProjectView(Camera cam, float aspect, Vector3 world, Vector2 fallback)
        {
            Vector3 p = cam.transform.InverseTransformPoint(world);
            if (p.z <= 0.01f) return fallback;
            float tv = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return new Vector2(0.5f + 0.5f * p.x / (p.z * tv * aspect), 0.5f + 0.5f * p.y / (p.z * tv));
        }

        float Rand01()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        // ---- Сброс ---------------------------------------------------------

        public void Clear()
        {
            for (int i = 0; i < MaxBanners; i++) { _banners[i].alive = false; _banners[i].text.gameObject.SetActive(false); }
            for (int i = 0; i < MaxNumbers; i++) { _numbers[i].alive = false; _numbers[i].text.gameObject.SetActive(false); }
        }

        public void Dispose()
        {
            if (_root != null) GameUtil.Destroy(_root.gameObject);
            _root = null;
        }
    }
}
