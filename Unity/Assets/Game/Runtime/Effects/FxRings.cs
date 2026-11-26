using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGame
{
    // Кольца ударной волны в точке попадания, как impactFx веб-версии (src/e_fx.js): кольцо (толщина 12 % радиуса)
    // и короткий залитый диск, оба развёрнуты к камере, тёплый белый (0xfff6dc), сложение со сценой, поверх всего.
    // Кольцо растёт от R0 до R1 с замедлением (1 − (1 − k)²) и гаснет линейно; размер, яркость, длительность
    // и число колец — по силе удара. Пул MaxRings слотов, один меш, без аллокаций в кадре.
    public class FxRings
    {
        public const int MaxRings = 10;
        public const int Segs = 32;
        public const float Inner = 0.88f;               // внутренний радиус кольца, доля внешнего (веб: RingGeometry(.88, 1))
        public const float RingDur0 = 0.15f, RingDurK = 0.10f;          // длительность кольца: 0,15 + 0,1·str, с
        public const float RingR0 = 0.02f, RingR1 = 0.06f, RingR1K = 0.11f;   // радиус, м (веб, у лица в 0,7 м)
        public const float RingA0 = 0.45f, RingAK = 0.30f;              // яркость 0,45 + 0,3·min(1, str)
        public const float DiscDur = 0.08f, DiscR0 = 0.015f, DiscR0K = 0.02f, DiscR1 = 0.035f, DiscR1K = 0.04f, DiscA = 0.45f;   // диск — по реальному времени: лицо не закрыто дольше 0,08 с
        public const float NextDelay = 0.045f, NextScale = 0.72f;     // второе и третье кольцо: позже и меньше
        static readonly Color32 Warm = new Color32(255, 246, 220, 255);

        struct Ring
        {
            public bool on;
            public float delay, t, dur, r0, r1, a, inner;
            public Vector3 at;
        }

        readonly Ring[] _r = new Ring[MaxRings];
        int _next;

        readonly Vector3[] _v;
        readonly Color32[] _c;
        Mesh _mesh;
        Material _mat;
        GameObject _go;
        bool _empty = true;

        // Показать или спрятать кольца (замер: кольцо над текстом плашки).
        public void SetDrawn(bool on) { if (_go != null) _go.SetActive(on); }
        public int Active { get { int n = 0; for (int i = 0; i < MaxRings; i++) if (_r[i].on) n++; return n; } }

        // parent == null — только логика (тесты).
        public FxRings(Transform parent)
        {
            _v = new Vector3[MaxRings * Segs * 2];
            _c = new Color32[MaxRings * Segs * 2];
            if (parent == null) return;
            var idx = new int[MaxRings * Segs * 6];
            for (int r = 0; r < MaxRings; r++)
            for (int s = 0; s < Segs; s++)
            {
                int b = r * Segs * 2, s1 = (s + 1) % Segs, t = (r * Segs + s) * 6;
                idx[t] = b + s * 2; idx[t + 1] = b + s * 2 + 1; idx[t + 2] = b + s1 * 2 + 1;
                idx[t + 3] = b + s * 2; idx[t + 4] = b + s1 * 2 + 1; idx[t + 5] = b + s1 * 2;
            }
            _mesh = new Mesh { name = "FxRings" };
            _mesh.MarkDynamic();
            _mesh.vertices = _v; _mesh.colors32 = _c;
            _mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(40f, 40f, 40f));

            var sh = GameUtil.FindShader("VoxGame/FxAdditive", GameUtil.FxAddMaterial);
            _mat = new Material(sh) { name = "FxRings" };
            _mat.renderQueue = 3000;   // в очереди прозрачных вместе с холстом: холст ближе к камере и рисуется после — кольцо под плашкой и HUD
            _go = new GameObject("FxRings");
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // Удар силой str (веб: джеб 0,3, силовой pw·mult до 1,6, мега 2,6); count колец; k — масштаб вида.
        public void Impact(Vector3 at, float str, int count, float k)
        {
            float dur = RingDur0 + RingDurK * str, r1 = (RingR1 + RingR1K * str) * k, a = RingA0 + RingAK * Mathf.Min(1f, str);
            for (int n = 0; n < count; n++)
            {
                float sc = Mathf.Pow(NextScale, n);
                Add(at, n * NextDelay, dur, RingR0 * k, r1 * sc, a * (n == 0 ? 1f : 0.8f), Inner);
            }
            Add(at, 0f, DiscDur, (DiscR0 + DiscR0K * str) * k, (DiscR1 + DiscR1K * str) * k, DiscA, 0f);
        }

        void Add(Vector3 at, float delay, float dur, float r0, float r1, float a, float inner)
        {
            // свободный слот, иначе ближайший к концу (по кругу)
            int i = -1;
            for (int k = 0; k < MaxRings; k++) if (!_r[k].on) { i = k; break; }
            if (i < 0) { i = _next; _next = (_next + 1) % MaxRings; }
            _r[i] = new Ring { on = true, delay = delay, t = 0f, dur = dur, r0 = r0, r1 = r1, a = a, inner = inner, at = at };
        }

        // dt — время колец (в стоп-кадре идёт медленно, как в вебе: sdt || dt·0,3), realDt — время диска;
        // cam — для разворота к камере.
        public void Tick(float dt, float realDt, Transform cam)
        {
            for (int i = 0; i < MaxRings; i++)
            {
                if (!_r[i].on) continue;
                if (_r[i].delay > 0f) { _r[i].delay -= dt; continue; }
                _r[i].t += _r[i].inner > 0f ? dt : realDt;
                if (_r[i].t >= _r[i].dur) _r[i].on = false;
            }
            Rebuild(cam);
        }

        public void Clear()
        {
            for (int i = 0; i < MaxRings; i++) _r[i].on = false;
            Rebuild(null);
        }

        void Rebuild(Transform cam)
        {
            if (_mesh == null) return;
            bool any = false;
            Vector3 right = cam != null ? cam.right : Vector3.right, up = cam != null ? cam.up : Vector3.up;
            Matrix4x4 w2l = _go.transform.worldToLocalMatrix;
            for (int i = 0; i < MaxRings; i++)
            {
                int b = i * Segs * 2;
                var r = _r[i];
                if (!r.on || r.delay > 0f)
                {
                    for (int s = 0; s < Segs * 2; s++) _v[b + s] = Vector3.zero;
                    continue;
                }
                any = true;
                float k = Mathf.Clamp01(r.t / r.dur);
                float rad = r.r0 + (r.r1 - r.r0) * (1f - (1f - k) * (1f - k));
                var col = Warm; col.a = (byte)Mathf.Clamp(255f * (1f - k) * r.a, 0f, 255f);
                Vector3 c = w2l.MultiplyPoint3x4(r.at);
                Vector3 ru = w2l.MultiplyVector(right), uu = w2l.MultiplyVector(up);
                for (int s = 0; s < Segs; s++)
                {
                    float ang = s * (2f * Mathf.PI / Segs);
                    Vector3 d = ru * Mathf.Cos(ang) + uu * Mathf.Sin(ang);
                    _v[b + s * 2] = c + d * (rad * r.inner);
                    _v[b + s * 2 + 1] = c + d * rad;
                    _c[b + s * 2] = _c[b + s * 2 + 1] = col;
                }
            }
            if (!any && _empty) return;
            _mesh.SetVertices(_v, 0, _v.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.SetColors(_c, 0, _c.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _empty = !any;
        }

        public void Dispose()
        {
            if (_go != null) GameUtil.Destroy(_go);
            if (_mesh != null) GameUtil.Destroy(_mesh);
            if (_mat != null) GameUtil.Destroy(_mat);
            _go = null; _mesh = null; _mat = null;
        }
    }
}
