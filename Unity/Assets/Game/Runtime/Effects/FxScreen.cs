using UnityEngine;
using UnityEngine.UI;

namespace VoxGame
{
    // Полноэкранные накладки uGUI поверх боя: белая вспышка удара, линии скорости из точки удара, оранжевая виньетка меги,
    // красная виньетка пропущенного удара, тёмная виньетка и «кинополосы» на нокауте и добивании, брызги крови на «объективе»
    // (≤ MaxSplats).
    // Текстуры виньетки и пятна рисуются один раз при построении. Всё затухает на реальном времени.
    public class FxScreen
    {
        public const int   MaxSplats     = 6;
        public const float SplatLife     = 3.5f;   // с, как в вебе
        public const float SplatHold     = 1.8f;   // с полной яркости, затем выцветание
        public const float SplatAlpha    = 0.85f;
        public const float SplatMin      = 70f, SplatMax = 130f;    // опорные пиксели холста (веб: 9 клеток по 4–10 css-px)
        public const float FlashMax      = 0.70f;
        public const float FlashDecay    = 11f;    // exp(-k·t): полноэкранная вспышка — мгновение, а не дымка
        // Линии скорости (веб #lines): лучи по 1° через 6,5° из точки удара, середина пустая, к краю плотнее;
        // яркость 0,7 → 0 и рост ×1 → ×1,12 за 0,22 с. Радиус маски — доля диагонали холста.
        public const float LinesLife     = 0.22f, LinesA0 = 0.5f, LinesGrow = 0.12f;
        public const float LinesStep     = 6.5f, LinesWidth = 1f, LinesHole = 0.15f, LinesFull = 0.45f;
        public const int   LinesTex      = 256;
        public const float LinesDiagMin  = 1290f;   // диагональ опорного холста 540×1170, пока у холста нет размера
        // Оранжевая виньетка меги (веб #megafx): 0 → 1 за 20 % жизни, затем гаснет, 0,8 с
        public const float MegaLife      = 0.8f, MegaA0 = 0.25f;   // веб 0,6 в sRGB; в линейном цвете Unity та же яркость при ~0,25
        public const float HurtMax       = 0.60f;
        public const float HurtDecay     = 3.2f;
        public const float BarsFrac      = 0.10f;  // высота каждой полосы, доля экрана
        public const float BarsIn        = 5f;     // скорость выезда (доля в секунду ×)
        public const float BarsOut       = 2.5f;
        public const float DarkVignetteMax = 0.8f;
        public const int   TexSize       = 64;

        static readonly Color SplatColor = new Color(0.50f, 0.02f, 0.03f, 1f);
        static readonly Color HurtColor  = new Color(0.85f, 0.04f, 0.02f, 1f);

        class SplatSlot
        {
            public RawImage img;
            public bool alive;
            public float age;
            public float size;
            public uint seq;
        }

        RectTransform _root;
        Image _flash, _barTop, _barBot;
        RawImage _hurt, _dark, _lines, _mega;
        readonly SplatSlot[] _splats = new SplatSlot[MaxSplats];
        Texture2D _vigTex, _splatTex, _linesTex;

        float _flashA, _hurtA;
        float _linesAge = 99f, _linesK, _megaAge = 99f;
        float _cine, _cineStrength, _bars;
        uint _seq;
        uint _rng = 0xB5297A4Du;

        public float FlashAlpha => _flashA;
        public float LinesAlpha => LinesA();
        public float MegaAlpha  => MegaA();
        public float HurtAlpha  => _hurtA;
        public float Bars       => _bars;
        public int ActiveSplats { get { int n = 0; for (int i = 0; i < MaxSplats; i++) if (_splats[i].alive) n++; return n; } }

        public FxScreen(Transform parent)
        {
            var go = new GameObject("FxScreen", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _root = go.GetComponent<RectTransform>();
            Stretch(_root);

            _vigTex = MakeVignette();
            _splatTex = MakeSplat();
            _linesTex = MakeLines();

            // снизу вверх: тёмная виньетка, полосы, красная виньетка, пятна, вспышка
            _dark = Raw("Dark", _vigTex, Color.black);
            _mega = Raw("Mega", _vigTex, new Color(1f, 0.43f, 0.08f, 0f));
            _barTop = Img("BarTop", Color.black);
            _barBot = Img("BarBot", Color.black);
            _hurt = Raw("Hurt", _vigTex, HurtColor);
            for (int i = 0; i < MaxSplats; i++)
            {
                var img = Raw("Splat" + i, _splatTex, SplatColor);
                img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                img.gameObject.SetActive(false);
                _splats[i] = new SplatSlot { img = img };
            }
            _lines = Raw("Lines", _linesTex, Color.white);
            _lines.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _flash = Img("Flash", Color.white);
            Apply();
        }

        // ---- События -------------------------------------------------------

        // Белая вспышка, strength 0..1.
        public void Flash(float strength) { if (UIMotion.Reduced) return; _flashA = Mathf.Max(_flashA, FlashMax * Mathf.Clamp01(strength)); }   // «меньше движения»: без вспышек

        // Линии скорости из точки удара: vp — доли экрана, strength 0..1 (веб: только силовой удар и мега).
        public void Lines(Vector2 vp, float strength)
        {
            _linesAge = 0f;
            _linesK = Mathf.Clamp01(strength);
            MoveLines(vp);
            _lines.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Rand01() * LinesStep);
        }

        // Переставить центр линий (смена пропорций кадра).
        public void MoveLines(Vector2 vp)
        {
            var rt = _lines.rectTransform;
            rt.anchorMin = rt.anchorMax = vp;
            rt.anchoredPosition = Vector2.zero;
        }

        float LinesA() => _linesAge >= LinesLife ? 0f : LinesA0 * _linesK * (1f - _linesAge / LinesLife);

        // Оранжевая виньетка меги.
        public void MegaGlow() => _megaAge = 0f;

        float MegaA()
        {
            if (_megaAge >= MegaLife) return 0f;
            float k = _megaAge / MegaLife;
            return MegaA0 * (k < 0.2f ? k / 0.2f : 1f - (k - 0.2f) / 0.8f);
        }

        // Красная виньетка пропущенного удара.
        public void Hurt(float strength) => _hurtA = Mathf.Max(_hurtA, HurtMax * Mathf.Clamp01(strength));

        // Кинополосы и тёмная виньетка на seconds.
        public void Cinema(float seconds, float strength)
        {
            _cine = Mathf.Max(_cine, seconds);
            _cineStrength = Mathf.Max(_cineStrength, Mathf.Clamp01(strength));
        }

        // n пятен крови на объективе; при переполнении вытесняется самое старое.
        public void Splat(int n)
        {
            for (int k = 0; k < n; k++)
            {
                SplatSlot s = null;
                for (int i = 0; i < MaxSplats; i++)
                {
                    if (!_splats[i].alive) { s = _splats[i]; break; }
                    if (s == null || _splats[i].seq < s.seq) s = _splats[i];
                }
                s.alive = true; s.age = 0f; s.seq = ++_seq;
                s.size = Mathf.Lerp(SplatMin, SplatMax, Rand01());
                var rt = s.img.rectTransform;
                // не на кнопках и полосах HUD
                var a = new Vector2(0.08f + 0.84f * Rand01(), 0.24f + 0.56f * Rand01());
                rt.anchorMin = rt.anchorMax = a;
                rt.anchoredPosition = Vector2.zero;
                rt.localRotation = Quaternion.Euler(0f, 0f, 90f * Mathf.Floor(Rand01() * 4f));   // клетки остаются по сетке экрана
                s.img.gameObject.SetActive(true);
                s.img.rectTransform.SetAsLastSibling();
                _flash.rectTransform.SetAsLastSibling();
            }
        }

        // ---- Тик -----------------------------------------------------------

        public void Tick(float udt)
        {
            _flashA *= Mathf.Exp(-FlashDecay * udt);
            _hurtA  *= Mathf.Exp(-HurtDecay * udt);
            if (_flashA < 0.004f) _flashA = 0f;
            if (_hurtA  < 0.004f) _hurtA  = 0f;
            if (_linesAge < 99f) _linesAge += udt;
            if (_megaAge < 99f) _megaAge += udt;

            if (_cine > 0f)
            {
                _cine = Mathf.Max(0f, _cine - udt);
                _bars = Mathf.Min(1f, _bars + BarsIn * udt);
            }
            else
            {
                _bars = Mathf.Max(0f, _bars - BarsOut * udt);
                if (_bars <= 0f) _cineStrength = 0f;
            }

            for (int i = 0; i < MaxSplats; i++)
            {
                var s = _splats[i];
                if (!s.alive) continue;
                s.age += udt;
                if (s.age >= SplatLife) { s.alive = false; s.img.gameObject.SetActive(false); }
            }
            Apply();
        }

        void Apply()
        {
            SetAlpha(_flash, _flashA);
            SetAlpha(_hurt, _hurtA);
            SetAlpha(_mega, MegaA());
            float la = LinesA();
            SetAlpha(_lines, la);
            if (la > 0.002f)
            {
                // квадрат со стороной в две диагонали холста: из любой точки удара лучи доходят до углов
                var root = _root.rect;
                float side = 2f * Mathf.Max(LinesDiagMin, Mathf.Sqrt(root.width * root.width + root.height * root.height));
                float g = 1f + LinesGrow * Mathf.Clamp01(_linesAge / LinesLife);
                _lines.rectTransform.sizeDelta = new Vector2(side * g, side * g);
            }
            float b = Smooth(_bars) * _cineStrength;
            SetAlpha(_dark, DarkVignetteMax * b);
            _barTop.gameObject.SetActive(b > 0f);
            _barBot.gameObject.SetActive(b > 0f);
            UIKit.SetAnchors(_barTop.rectTransform, 0f, 1f - BarsFrac * b, 1f, 1f);
            UIKit.SetAnchors(_barBot.rectTransform, 0f, 0f, 1f, BarsFrac * b);

            for (int i = 0; i < MaxSplats; i++)
            {
                var s = _splats[i];
                if (!s.alive) continue;
                float a = s.age < SplatHold ? SplatAlpha : SplatAlpha * (1f - (s.age - SplatHold) / (SplatLife - SplatHold));
                float pop = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(s.age / 0.07f));
                s.img.rectTransform.sizeDelta = new Vector2(s.size * pop, s.size * pop);
                var c = SplatColor; c.a = Mathf.Max(0f, a);
                s.img.color = c;
            }
        }

        public void Clear()
        {
            _flashA = _hurtA = 0f; _cine = 0f; _bars = 0f; _cineStrength = 0f; _linesAge = 99f; _megaAge = 99f;
            for (int i = 0; i < MaxSplats; i++) { _splats[i].alive = false; _splats[i].img.gameObject.SetActive(false); }
            Apply();
        }

        public void Dispose()
        {
            if (_root != null) GameUtil.Destroy(_root.gameObject);
            if (_vigTex != null) GameUtil.Destroy(_vigTex);
            if (_splatTex != null) GameUtil.Destroy(_splatTex);
            if (_linesTex != null) GameUtil.Destroy(_linesTex);
            _root = null; _vigTex = null; _splatTex = null; _linesTex = null;
        }

        // ---- Построение ----------------------------------------------------

        static void Stretch(RectTransform rt)
        {
            UIKit.SetAnchors(rt, 0f, 0f, 1f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        Image Img(string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, false);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            Stretch(img.rectTransform);
            return img;
        }

        RawImage Raw(string name, Texture tex, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(_root, false);
            var img = go.GetComponent<RawImage>();
            img.texture = tex;
            img.color = c;
            img.raycastTarget = false;
            Stretch(img.rectTransform);
            return img;
        }

        static void SetAlpha(Graphic g, float a)
        {
            bool on = a > 0.002f;
            if (g.gameObject.activeSelf != on) g.gameObject.SetActive(on);
            if (!on) return;
            var c = g.color; c.a = a; g.color = c;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        // Виньетка: прозрачный центр, плотные края (растягивается по экрану — эллипс).
        internal static Texture2D MakeVignette()
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) { name = "FxVignette", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float u = (x + 0.5f) / TexSize * 2f - 1f, v = (y + 0.5f) / TexSize * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v) / 1.4142f;
                float a = Mathf.Clamp01((r - 0.38f) / 0.62f);
                a = a * a * (3f - 2f * a);
                px[x + y * TexSize] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // Линии скорости: тонкие лучи из центра (1° через 6,5°), середина прозрачна, к краю плотнее (как маска веба).
        // Радиус 1 — половина стороны текстуры (диагональ холста).
        static Texture2D MakeLines()
        {
            const int N = LinesTex;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "FxLines", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float deg = Mathf.Repeat(Mathf.Atan2(v, u) * Mathf.Rad2Deg, LinesStep);
                float line = deg < LinesWidth ? 1f : deg < LinesWidth + 0.5f ? 0.4f : 0f;
                float mask = Mathf.Clamp01((r - LinesHole) / (LinesFull - LinesHole));
                px[x + y * N] = new Color32(255, 255, 255, (byte)(255f * line * 0.9f * mask * mask));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // Пятно крови, как screenSplat веба: клетки 9×9 вокруг середины (16 квадратиков, два оттенка крови), жёсткий край.
        static Texture2D MakeSplat()
        {
            const int G = 9, C = 7, N = G * C;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                { name = "FxSplat", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            var px = new Color32[N * N];
            // клетки: x, y по сетке 9×9 (0..8) и оттенок (1 — светлая), раскиданы около середины, как rnd(−4, 4)·u веба
            int[] cells =
            {
                4, 4, 0,  3, 4, 1,  5, 4, 0,  4, 3, 1,  4, 5, 0,  2, 5, 0,  6, 3, 1,  3, 6, 0,
                5, 6, 1,  1, 3, 0,  7, 5, 0,  4, 1, 1,  6, 7, 0,  2, 2, 1,  8, 4, 0,  4, 8, 0,
                3, 3, 0,  5, 5, 1,
            };
            for (int k = 0; k < cells.Length; k += 3)
            {
                byte shade = (byte)(cells[k + 2] == 1 ? 255 : 190);
                for (int y = 0; y < C; y++)
                for (int x = 0; x < C; x++)
                    px[cells[k] * C + x + (cells[k + 1] * C + y) * N] = new Color32(shade, shade, shade, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        float Rand01()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }
    }
}
