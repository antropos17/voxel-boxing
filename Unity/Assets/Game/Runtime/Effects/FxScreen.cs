using UnityEngine;
using UnityEngine.UI;

namespace VoxGame
{
    // Полноэкранные накладки uGUI поверх боя: белая вспышка удара, красная виньетка пропущенного удара,
    // тёмная виньетка и «кинополосы» на нокауте и добивании, брызги крови на «объективе» (≤ MaxSplats).
    // Текстуры виньетки и пятна рисуются один раз при построении. Всё затухает на реальном времени.
    public class FxScreen
    {
        public const int   MaxSplats     = 6;
        public const float SplatLife     = 3.5f;   // с, как в вебе
        public const float SplatHold     = 1.8f;   // с полной яркости, затем выцветание
        public const float SplatAlpha    = 0.85f;
        public const float SplatMin      = 120f, SplatMax = 230f;   // опорные пиксели холста
        public const float FlashMax      = 0.70f;
        public const float FlashDecay    = 11f;    // exp(-k·t): полноэкранная вспышка — мгновение, а не дымка
        public const float BurstHold     = 0.09f;  // с: звезда удара держится (стоп-кадр), затем гаснет
        public const float BurstFade     = 0.16f;
        public const float BurstGrow     = 0.35f;  // насколько вырастает за жизнь
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
        RawImage _hurt, _dark, _burst;
        readonly SplatSlot[] _splats = new SplatSlot[MaxSplats];
        Texture2D _vigTex, _splatTex, _burstTex;

        float _flashA, _hurtA;
        float _burstAge = 99f, _burstSize;
        Color _burstColor;
        float _cine, _cineStrength, _bars;
        uint _seq;
        uint _rng = 0xB5297A4Du;

        public float FlashAlpha => _flashA;
        public float BurstAlpha => BurstA();
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
            _burstTex = MakeBurst();

            // снизу вверх: тёмная виньетка, полосы, красная виньетка, пятна, вспышка
            _dark = Raw("Dark", _vigTex, Color.black);
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
            _burst = Raw("Burst", _burstTex, Color.white);
            _burst.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _flash = Img("Flash", Color.white);
            Apply();
        }

        // ---- События -------------------------------------------------------

        // Белая вспышка, strength 0..1.
        public void Flash(float strength) => _flashA = Mathf.Max(_flashA, FlashMax * Mathf.Clamp01(strength));

        // Звезда удара в точке попадания: vp — доли экрана, size — опорные px, color — цвет (альфа — сила).
        public void Burst(Vector2 vp, float size, Color color)
        {
            _burstAge = 0f;
            _burstSize = size;
            _burstColor = color;
            var rt = _burst.rectTransform;
            rt.anchorMin = rt.anchorMax = vp;
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, Rand01() * 45f);
        }

        // Переставить звезду (смена пропорций кадра).
        public void MoveBurst(Vector2 vp)
        {
            var rt = _burst.rectTransform;
            rt.anchorMin = rt.anchorMax = vp;
        }

        float BurstA()
        {
            if (_burstAge >= BurstHold + BurstFade) return 0f;
            return _burstColor.a * (_burstAge < BurstHold ? 1f : 1f - (_burstAge - BurstHold) / BurstFade);
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
                rt.localRotation = Quaternion.Euler(0f, 0f, Rand01() * 360f);
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
            if (_burstAge < 99f) _burstAge += udt;

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
            float ba = BurstA();
            if (ba > 0.002f)
            {
                float k = 0.75f + BurstGrow * Mathf.Clamp01(_burstAge / (BurstHold + BurstFade));
                _burst.rectTransform.sizeDelta = new Vector2(_burstSize * k, _burstSize * k);
                var bc = _burstColor; bc.a = ba; _burst.color = bc;
                if (!_burst.gameObject.activeSelf) _burst.gameObject.SetActive(true);
            }
            else if (_burst.gameObject.activeSelf) _burst.gameObject.SetActive(false);
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
            _flashA = _hurtA = 0f; _cine = 0f; _bars = 0f; _cineStrength = 0f; _burstAge = 99f;
            for (int i = 0; i < MaxSplats; i++) { _splats[i].alive = false; _splats[i].img.gameObject.SetActive(false); }
            Apply();
        }

        public void Dispose()
        {
            if (_root != null) GameUtil.Destroy(_root.gameObject);
            if (_vigTex != null) GameUtil.Destroy(_vigTex);
            if (_splatTex != null) GameUtil.Destroy(_splatTex);
            if (_burstTex != null) GameUtil.Destroy(_burstTex);
            _root = null; _vigTex = null; _splatTex = null; _burstTex = null;
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
        static Texture2D MakeVignette()
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

        // Звезда удара: яркое ядро и неровные острые лучи (рисованная, с жёстким краем — под воксели).
        static Texture2D MakeBurst()
        {
            const int N = TexSize * 2;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "FxBurst", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            const int Rays = 10;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Atan2(v, u);
                // длинные и короткие лучи через один, острые концы
                float t = Mathf.Repeat(a / (2f * Mathf.PI) * Rays, Rays);
                int ray = Mathf.FloorToInt(t);
                float ph = t - ray;
                float len = (ray % 2 == 0 ? 0.96f : 0.66f) * (0.9f + 0.1f * Mathf.Sin(ray * 2.4f));
                float tip = 1f - Mathf.Abs(ph - 0.5f) * 2f;               // 1 — ось луча, 0 — между лучами
                float edge = Mathf.Lerp(0.36f, len, tip * tip);
                float alpha = r < edge ? 1f : 0f;
                float core = Mathf.Clamp01(1f - r / 0.34f);
                // ядро белое, лучи тёплые; середина полупрозрачна, чтобы под ней читалось лицо
                byte g = (byte)(205 + 50 * core), b = (byte)(90 + 165 * core);
                float ca = alpha * (r < 0.22f ? 0.35f : 1f);
                px[x + y * N] = new Color32(255, g, b, (byte)(ca * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // Пятно крови: неровная клякса из нескольких кругов и мелкие капли вокруг.
        static Texture2D MakeSplat()
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) { name = "FxSplat", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[TexSize * TexSize];
            // круги: x, y, r (доли текстуры)
            float[] blobs =
            {
                0.50f, 0.50f, 0.22f,  0.38f, 0.56f, 0.14f,  0.62f, 0.44f, 0.15f,  0.52f, 0.66f, 0.10f,
                0.44f, 0.36f, 0.11f,  0.70f, 0.62f, 0.07f,  0.22f, 0.70f, 0.045f, 0.80f, 0.30f, 0.05f,
                0.26f, 0.28f, 0.035f, 0.84f, 0.76f, 0.03f,  0.15f, 0.48f, 0.03f,  0.58f, 0.86f, 0.035f,
            };
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float u = (x + 0.5f) / TexSize, v = (y + 0.5f) / TexSize;
                float a = 0f, core = 0f;
                for (int b = 0; b < blobs.Length; b += 3)
                {
                    float dx = u - blobs[b], dy = v - blobs[b + 1], r = blobs[b + 2];
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    a = Mathf.Max(a, Mathf.Clamp01((r - d) / 0.025f + 0.5f));
                    core = Mathf.Max(core, Mathf.Clamp01(1f - d / r));
                }
                // середина темнее края: густая кровь
                byte shade = (byte)(200 + 55 * (1f - core));
                px[x + y * TexSize] = new Color32(shade, shade, shade, (byte)(a * 255f));
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
