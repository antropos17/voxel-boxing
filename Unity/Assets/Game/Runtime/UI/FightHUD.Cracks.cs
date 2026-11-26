// FightHUD.Cracks.cs — из варианта «Е» макета Сеймура: шкала трескается на 75, 50 и 25 %, на нокауте разлетается.
// Трещины и осколки собраны заранее (пулы фиксированного размера): в кадре память не выделяется.
// «Меньше движения»: трещины без вспышки и искр, разлёта нет — шкала уходит вместе с панелью.
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public partial class FightHUD {

        public const int   MaxShards  = 24;      // осколков одновременно (обе шкалы вместе)
        public const float ShardLife  = 1.10f;   // осколок летит, с
        public const float CrackPop   = 0.16f;   // трещина вспыхивает шире и садится
        public const int   ShardCols  = 12, ShardRows = 2;
        static readonly float[] CrackAt = { 0.75f, 0.50f, 0.25f };

        struct Shard { public Image img; public RectTransform rt; public Vector2 p, v; public float age, life, rot, spin; public Color c; public bool alive; }
        readonly Shard[] _sh = new Shard[MaxShards];

        // Три трещины поперёк шкалы (зигзаг из трёх отрезков: тёмная сердцевина, светлый край) — поверх заливки.
        void BuildCracks(HpBar b) {
            b.cracks = new RectTransform[CrackAt.Length];
            b.crackT = new float[CrackAt.Length];
            for (int i = 0; i < CrackAt.Length; i++) {
                var c = new GameObject("Crack" + i, typeof(RectTransform)).GetComponent<RectTransform>();
                c.SetParent(b.frame, false);
                float x = b.right ? 1f - CrackAt[i] : CrackAt[i];
                UIKit.SetAnchors(c, x, 0f, x, 1f);
                c.sizeDelta = new Vector2(16f, 0f); c.anchoredPosition = Vector2.zero;
                for (int k = 0; k < 3; k++) {
                    var s = UIKit.Plain(c, "Z" + k, new Color(0.02f, 0.01f, 0.03f, 0.95f));
                    UIKit.SetAnchors(s, 0.5f, k / 3f, 0.5f, (k + 1) / 3f);
                    s.sizeDelta = new Vector2(3f, 5f);
                    s.localRotation = Quaternion.Euler(0f, 0f, (k % 2 == 0 ? 26f : -26f) * (i % 2 == 0 ? 1f : -1f));
                    var o = s.gameObject.AddComponent<Outline>();   // светлый край трещины
                    o.effectColor = new Color(1f, 0.97f, 0.9f, 0.6f); o.effectDistance = new Vector2(1f, -1f);
                }
                c.gameObject.SetActive(false);
                b.cracks[i] = c;
            }
        }

        void BuildShards() {
            for (int i = 0; i < MaxShards; i++) {
                var s = UIKit.Plain(_fx, "Sh" + i, Color.white);
                UIKit.SetAnchors(s, 0.5f, 0.5f, 0.5f, 0.5f);
                s.gameObject.SetActive(false);
                _sh[i].rt = s; _sh[i].img = s.GetComponent<Image>();
            }
        }

        // Трещина появляется, когда здоровье опустилось до порога, и уходит при восстановлении.
        void StepCracks(HpBar b, float udt) {
            if (b.shattered) return;
            for (int i = 0; i < CrackAt.Length; i++) {
                bool on = !_needIntro && b.value <= CrackAt[i] + 1e-4f;
                var c = b.cracks[i];
                if (on != c.gameObject.activeSelf) {
                    c.gameObject.SetActive(on);
                    if (on) {
                        b.crackT[i] = 0f;
                        if (!UIMotion.Reduced) CrackSparks(b, CrackAt[i]);
                    }
                }
                if (on && b.crackT[i] < CrackPop) {
                    b.crackT[i] += udt;
                    float s = UIMotion.Reduced || !UIMotion.Animate ? 1f
                            : Mathf.LerpUnclamped(2.2f, 1f, UIMotion.Eval(Ease.OutBack, Mathf.Clamp01(b.crackT[i] / CrackPop)));
                    c.localScale = new Vector3(s, 1f, 1f);
                }
            }
        }

        void CrackSparks(HpBar b, float f) {
            var p = BarPoint(b, f);
            for (int i = 0; i < 5; i++) {
                float a = Mathf.PI * (0.15f + 0.7f * Rand01());
                Emit(p, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (70f + 90f * Rand01()), 0.3f + 0.15f * Rand01(), 3f + 2f * Rand01(), 260f,
                     Color.white, b.fillColor);
            }
        }

        // Нокаут: рамка шкалы раскалывается на ShardCols × ShardRows осколков, они разлетаются от середины вниз и падают.
        void Shatter(HpBar b) {
            if (b.shattered) return;
            b.shattered = true;
            if (UIMotion.Reduced) return;
            var r = b.frame.rect;
            float w = r.width / ShardCols, h = r.height / ShardRows;
            for (int k = 0; k < ShardCols * ShardRows; k++) {
                int cx = k % ShardCols, cy = k / ShardCols;
                float fm = (cx + 0.5f) / ShardCols;
                Color col = fm <= b.value + 0.02f ? b.fillColor : cy == 0 ? Ink : new Color(b.fillColor.r * 0.45f, b.fillColor.g * 0.45f, b.fillColor.b * 0.45f, 1f);
                var p = BarPoint(b, fm, (cy == 0 ? 0.25f : -0.25f) * r.height);
                float side = (fm - 0.5f) * (b.right ? -1f : 1f);
                // вниз и в стороны: на нокауте сверху выезжают кинополосы FxScreen — осколки падают на экран, а не под них
                var v = new Vector2(side * (260f + 220f * Rand01()) + (Rand01() - 0.5f) * 80f, -(80f + 200f * Rand01()) - cy * 60f);
                EmitShard(p, v, new Vector2(w * (0.7f + 0.5f * Rand01()), h * (0.8f + 0.4f * Rand01())), col);
            }
            for (int i = 0; i < 8; i++) {
                float a = Rand01() * Mathf.PI * 2f;
                Emit(BarPoint(b, Rand01()), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (120f + 120f * Rand01()), 0.4f, 4f, 200f, Color.white, Gold);
            }
            b.frame.gameObject.SetActive(false);
        }

        void EmitShard(Vector2 p, Vector2 v, Vector2 size, Color c) {
            int slot = -1; float oldest = -1f;
            for (int i = 0; i < MaxShards; i++) {
                if (!_sh[i].alive) { slot = i; break; }
                float a = _sh[i].age / _sh[i].life;
                if (a > oldest) { oldest = a; slot = i; }
            }
            ref var s = ref _sh[slot];
            s.p = p; s.v = v; s.age = 0f; s.life = ShardLife * (0.85f + 0.3f * Rand01());
            s.rot = (Rand01() - 0.5f) * 40f; s.spin = (Rand01() - 0.5f) * 900f; s.c = c; s.alive = true;
            s.rt.sizeDelta = size; s.rt.anchoredPosition = p; s.rt.localRotation = Quaternion.Euler(0f, 0f, s.rot);
            s.img.color = c;
            if (!s.rt.gameObject.activeSelf) s.rt.gameObject.SetActive(true);
        }

        void StepShards(float udt) {
            for (int i = 0; i < MaxShards; i++) {
                ref var s = ref _sh[i];
                if (!s.alive) continue;
                s.age += udt;
                if (s.age >= s.life) { s.alive = false; s.rt.gameObject.SetActive(false); continue; }
                s.v.y -= 1100f * udt;
                s.p += s.v * udt;
                s.rot += s.spin * udt;
                s.rt.anchoredPosition = s.p;
                s.rt.localRotation = Quaternion.Euler(0f, 0f, s.rot);
                float u = s.age / s.life;
                if (u > 0.6f) { var c = s.c; c.a *= 1f - (u - 0.6f) / 0.4f; s.img.color = c; }
            }
        }

        // Новый бой или «заново»: шкала целая, трещин и осколков нет.
        void ResetCracks() {
            for (int i = 0; i < MaxShards; i++) { _sh[i].alive = false; _sh[i].rt.gameObject.SetActive(false); }
            ResetCracks(_plr); ResetCracks(_foe);
            EndFinale();
        }

        static void ResetCracks(HpBar b) {
            b.shattered = false;
            if (!b.frame.gameObject.activeSelf) b.frame.gameObject.SetActive(true);
            for (int i = 0; i < b.cracks.Length; i++) { b.cracks[i].gameObject.SetActive(false); b.cracks[i].localScale = Vector3.one; b.crackT[i] = 9f; }
        }

        // Развязка прячет HUD целиком (UIRoot.HideAll), а осколки летят ещё ~1,1 с. Пока они живы, экран остаётся включённым,
        // но виден только слой Fx: шкалы, таймер и кнопки скрыты. Потом экран выключается сам; OnDisable всё возвращает.
        const float FinaleHold = ShardLife + 0.3f;
        bool _finaleOnly; float _finaleT;
        public bool FinaleOnly => _finaleOnly;
        public bool HoldForFinale() {
            if (UIMotion.Reduced || !gameObject.activeSelf) return false;
            if (!_finaleOnly) {
                for (int i = 0; i < transform.childCount; i++) { var c = transform.GetChild(i); if (c != _fx) c.gameObject.SetActive(false); }
                for (int i = 0; i < MaxFloaters; i++) { _fl[i].alive = false; _fl[i].t.gameObject.SetActive(false); }
                var g = GetComponent<CanvasGroup>(); if (g != null) g.blocksRaycasts = false;
                _finaleOnly = true; _finaleT = 0f;
            }
            return true;
        }
        void StepFinale(float udt) {
            if (!_finaleOnly) return;
            _finaleT += udt;
            if (_finaleT >= FinaleHold && LiveShards == 0) { EndFinale(); gameObject.SetActive(false); }
        }
        void OnDisable() => EndFinale();
        void EndFinale() {
            if (!_finaleOnly) return;
            _finaleOnly = false;
            for (int i = 0; i < transform.childCount; i++) transform.GetChild(i).gameObject.SetActive(true);
            var g = GetComponent<CanvasGroup>(); if (g != null) g.blocksRaycasts = true;
        }

        // Для тестов и снимков.
        public int  CracksShown(bool foe) { var b = foe ? _foe : _plr; int n = 0; for (int i = 0; i < b.cracks.Length; i++) if (b.cracks[i].gameObject.activeSelf) n++; return n; }
        public bool Shattered(bool foe) => (foe ? _foe : _plr).shattered;
        public int  LiveShards { get { int n = 0; for (int i = 0; i < MaxShards; i++) if (_sh[i].alive) n++; return n; } }
    }
}
