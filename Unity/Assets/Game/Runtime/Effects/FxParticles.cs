using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGame
{
    public enum FxKind : byte { Blood, Spark, Sweat, Star }

    // Пул простых частиц: кровь, искры удара, пот, звёздочки оглушения.
    // Кольцевой буфер: новая частица занимает самый старый слот. Физика — в массивах, без аллокаций в кадре.
    // Отрисовка — один динамический меш из кубиков с цветом в вершинах (шейдер слоя VoxGame/VertexColor),
    // пересобираемый в заранее выделенные массивы: обычный MeshRenderer надёжно попадает в Camera.Render вне Play Mode.
    // Шейдер непрозрачный, поэтому частица «гаснет» уменьшением размера.
    public class FxParticles
    {
        public const int   MaxParticles = 200;
        public const float Gravity      = 7.6f;    // м/с², как в вебе
        public const float FloorY       = 0f;      // настил ринга
        public const float Glow         = 0.7f;    // самосвечение материала: искры и кровь читаются в тени
        public const float StarRate     = 7f;      // звёздочек в секунду над оглушённым
        public const float StarRadius   = 0.15f;   // м, орбита вокруг головы
        public const float StarSpeed    = 0.65f;   // м/с по орбите
        public const float BloodSize    = 0.017f;  // м, средний размер капли
        public const float SparkSize    = 0.011f;  // м, средний размер искры
        public const float SpawnLead    = 0.03f;
        public const float BloodStretch = 1.2f, SparkStretch = 2.2f;   // вытяжка по скорости, долей размера   // м: рождение перед поверхностью, со стороны удара

        const int VertsPer = 24, IdxPer = 36;

        // Частицы
        readonly Vector3[] _pos = new Vector3[MaxParticles];
        readonly Vector3[] _vel = new Vector3[MaxParticles];
        readonly Vector3[] _orbit = new Vector3[MaxParticles];   // центр орбиты звёздочки
        readonly float[]   _life = new float[MaxParticles];       // осталось, с (≤0 — свободна)
        readonly float[]   _life0 = new float[MaxParticles];
        readonly float[]   _size = new float[MaxParticles];
        readonly float[]   _spin = new float[MaxParticles];       // угол, рад
        readonly float[]   _spinV = new float[MaxParticles];
        readonly FxKind[]  _kind = new FxKind[MaxParticles];
        readonly Color32[] _col = new Color32[MaxParticles];
        readonly uint[]    _seq = new uint[MaxParticles];         // порядковый номер рождения
        int  _next;
        uint _born;
        int  _alive;
        float _starAcc;
        uint _rng = 0x2545F491u;

        // Меш
        readonly Vector3[] _mv;
        readonly Vector3[] _mn;
        readonly Color32[] _mc;
        Mesh       _mesh;
        Material   _mat;
        GameObject _go;
        bool       _meshEmpty = true;

        public int  AliveCount => _alive;
        public uint Born => _born;
        public Mesh Mesh => _mesh;

        // parent == null — только логика (тесты), без меша.
        public FxParticles(Transform parent)
        {
            _mv = new Vector3[MaxParticles * VertsPer];
            _mn = new Vector3[MaxParticles * VertsPer];
            _mc = new Color32[MaxParticles * VertsPer];
            if (parent == null) return;

            var idx = new int[MaxParticles * IdxPer];
            for (int p = 0; p < MaxParticles; p++)
            for (int f = 0; f < 6; f++)
            {
                int b = p * VertsPer + f * 4, t = p * IdxPer + f * 6;
                idx[t] = b; idx[t + 1] = b + 1; idx[t + 2] = b + 2;
                idx[t + 3] = b; idx[t + 4] = b + 2; idx[t + 5] = b + 3;
            }
            _mesh = new Mesh { name = "FxParticles" };
            _mesh.MarkDynamic();
            _mesh.indexFormat = IndexFormat.UInt16;
            _mesh.vertices = _mv; _mesh.normals = _mn; _mesh.colors32 = _mc;
            _mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(40f, 40f, 40f));

            var shader = GameUtil.FindShader("VoxGame/VertexColor", GameUtil.VoxMaterial);
            _mat = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader")) { name = "FxParticles" };
            _mat.SetFloat("_Glow", Glow);

            _go = new GameObject("FxParticles");
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ---- Рождение ------------------------------------------------------

        // Одна частица. Занимает самый старый слот (живую вытесняет).
        public void Spawn(FxKind kind, Vector3 pos, Vector3 vel, float size, float life, Color32 col)
        {
            int i = _next;
            _next = (_next + 1) % MaxParticles;
            if (_life[i] <= 0f) _alive++;
            _kind[i] = kind; _pos[i] = pos; _vel[i] = vel; _size[i] = size;
            _life[i] = _life0[i] = Mathf.Max(0.01f, life); _col[i] = col;
            _spin[i] = Rand01() * 6.2832f; _spinV[i] = (Rand01() * 2f - 1f) * 14f;
            _orbit[i] = pos;
            _seq[i] = _born++;
        }

        // Кровь: капли из точки удара по направлению удара с широким разлётом в стороны и вверх; часть — обратно
        // к камере. Рождаются чуть перед поверхностью (со стороны удара), чтобы не начинать внутри головы.
        public void BloodBurst(Vector3 at, Vector3 dir, int n, float speed)
        {
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 from = at - d * SpawnLead;
            for (int k = 0; k < n; k++)
            {
                Vector3 r = RandSphere();
                Vector3 main = Rand01() < 0.3f ? -d * 0.5f : d * 0.45f;
                Vector3 v = (main + r * 1.0f + Vector3.up * 0.4f).normalized * speed * (0.35f + 0.65f * Rand01());
                byte rr = (byte)(130 + Rand01() * 80), gg = (byte)(8 + Rand01() * 22);
                Spawn(FxKind.Blood, from + r * 0.012f, v, BloodSize * (0.6f + 0.8f * Rand01()), 0.7f + 0.7f * Rand01(),
                      new Color32(rr, gg, (byte)(gg + 4), 255));
            }
        }

        // Искры удара: быстрые, короткие, вытянуты по скорости.
        public void Sparks(Vector3 at, Vector3 dir, int n, float speed)
        {
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            for (int k = 0; k < n; k++)
            {
                Vector3 v = (RandSphere() - d * 0.2f).normalized * speed * (1.2f + 1.6f * Rand01());
                byte g = (byte)(200 + Rand01() * 55);
                Spawn(FxKind.Spark, at - d * SpawnLead, v, SparkSize * (0.6f + 0.8f * Rand01()), 0.14f + 0.18f * Rand01(),
                      new Color32(255, g, (byte)(g - 90), 255));
            }
        }

        // Пот: светлые брызги в стороны и вверх.
        public void Sweat(Vector3 at, Vector3 dir, int n)
        {
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            for (int k = 0; k < n; k++)
            {
                Vector3 v = (RandSphere() + d * 0.5f + Vector3.up * 0.6f).normalized * (0.9f + 1.4f * Rand01());
                Spawn(FxKind.Sweat, at + RandSphere() * 0.03f, v, 0.004f + 0.004f * Rand01(), 0.45f + 0.3f * Rand01(),
                      new Color32(215, 232, 245, 255));
            }
        }

        // Звёздочки по орбите над головой; dt — игровое время (в стоп-кадре не рождаются).
        public void EmitStars(Vector3 head, float dt)
        {
            _starAcc += dt * StarRate;
            while (_starAcc >= 1f)
            {
                _starAcc -= 1f;
                float a = Rand01() * 6.2832f;
                Vector3 c = head + new Vector3(0f, 0.13f, 0f);
                Vector3 off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * StarRadius;
                Vector3 tan = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * StarSpeed;
                Spawn(FxKind.Star, c + off, tan + Vector3.up * 0.04f, 0.018f + 0.008f * Rand01(), 0.9f,
                      new Color32(255, 220, 60, 255));
                int i = (_next + MaxParticles - 1) % MaxParticles;
                _orbit[i] = c;
            }
        }

        // ---- Тик -----------------------------------------------------------

        public void Tick(float dt)
        {
            if (dt > 0f)
            {
                float k2 = (StarSpeed / StarRadius) * (StarSpeed / StarRadius);
                for (int i = 0; i < MaxParticles; i++)
                {
                    if (_life[i] <= 0f) continue;
                    _life[i] -= dt;
                    if (_life[i] <= 0f) { _alive--; continue; }

                    Vector3 v = _vel[i];
                    switch (_kind[i])
                    {
                        case FxKind.Star:
                            // орбита: притяжение к центру в горизонтали, без тяжести
                            Vector3 c = _orbit[i] - _pos[i];
                            v.x += c.x * k2 * dt; v.z += c.z * k2 * dt;
                            break;
                        case FxKind.Spark:
                            v.y -= Gravity * 0.5f * dt;
                            v *= Mathf.Exp(-3f * dt);
                            break;
                        case FxKind.Sweat:
                            v.y -= Gravity * 0.8f * dt;
                            v *= Mathf.Exp(-1.5f * dt);
                            break;
                        default:
                            v.y -= Gravity * dt;
                            v *= Mathf.Exp(-0.6f * dt);
                            break;
                    }
                    Vector3 p = _pos[i] + v * dt;

                    // Настил: кровь ложится и быстро гаснет, остальное отскакивает
                    if (p.y < FloorY + _size[i] * 0.5f && v.y < 0f)
                    {
                        p.y = FloorY + _size[i] * 0.5f;
                        if (_kind[i] == FxKind.Blood) { v = Vector3.zero; _life[i] = Mathf.Min(_life[i], 0.35f); }
                        else { v.y *= -0.32f; v.x *= 0.6f; v.z *= 0.6f; }
                    }
                    _pos[i] = p; _vel[i] = v;
                    _spin[i] += _spinV[i] * dt;
                }
            }
            RebuildMesh();
        }

        // Наименьший номер рождения среди живых (самая старая живая частица); uint.MaxValue — пусто.
        public uint OldestAliveSeq()
        {
            uint m = uint.MaxValue;
            for (int i = 0; i < MaxParticles; i++)
                if (_life[i] > 0f && _seq[i] < m) m = _seq[i];
            return m;
        }

        public bool IsAlive(uint seq)
        {
            for (int i = 0; i < MaxParticles; i++)
                if (_life[i] > 0f && _seq[i] == seq) return true;
            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < MaxParticles; i++) _life[i] = 0f;
            _alive = 0; _starAcc = 0f;
            RebuildMesh();
        }

        public void Dispose()
        {
            if (_go != null) GameUtil.Destroy(_go);
            if (_mesh != null) GameUtil.Destroy(_mesh);
            if (_mat != null) GameUtil.Destroy(_mat);
            _go = null; _mesh = null; _mat = null;
        }

        // ---- Меш -----------------------------------------------------------

        // Кубик на частицу: кровь и искры вытянуты по скорости, пот и звёздочки вращаются.
        void RebuildMesh()
        {
            if (_mesh == null) return;
            if (_alive == 0 && _meshEmpty) return;
            // частицы живут в мире, меш — в осях своего объекта
            Matrix4x4 w2l = _go.transform.worldToLocalMatrix;

            for (int i = 0; i < MaxParticles; i++)
            {
                int b = i * VertsPer;
                if (_life[i] <= 0f)
                {
                    for (int k = 0; k < VertsPer; k++) _mv[b + k] = Vector3.zero;
                    continue;
                }
                float t = _life[i] / _life0[i];
                // гаснет уменьшением в последней трети жизни
                float s = _size[i] * Mathf.Clamp01(t * 3f);
                if (_kind[i] == FxKind.Star) s *= 0.8f + 0.2f * Mathf.Sin(_spin[i] * 2f);

                Vector3 u, v, w;
                float len = s;
                Vector3 vel = _vel[i];
                float sp = vel.magnitude;
                if ((_kind[i] == FxKind.Blood || _kind[i] == FxKind.Spark) && sp > 0.05f)
                {
                    w = vel / sp;
                    Vector3 a = Mathf.Abs(w.y) < 0.9f ? Vector3.up : Vector3.right;
                    u = Vector3.Cross(a, w).normalized;
                    v = Vector3.Cross(w, u);
                    len = s * (1f + (_kind[i] == FxKind.Spark ? Mathf.Min(sp * 1.6f, SparkStretch) : Mathf.Min(sp * 0.5f, BloodStretch)));
                }
                else
                {
                    float ca = Mathf.Cos(_spin[i]), sa = Mathf.Sin(_spin[i]);
                    u = new Vector3(ca, sa, 0f); v = new Vector3(-sa * 0.8f, ca * 0.8f, 0.6f).normalized;
                    w = Vector3.Cross(u, v);
                }
                WriteCube(b, w2l.MultiplyPoint3x4(_pos[i]), w2l.MultiplyVector(u * (s * 0.5f)),
                          w2l.MultiplyVector(v * (s * 0.5f)), w2l.MultiplyVector(w * (len * 0.5f)), _col[i]);
            }

            _mesh.SetVertices(_mv, 0, _mv.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.SetNormals(_mn, 0, _mn.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.SetColors(_mc, 0, _mc.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _meshEmpty = _alive == 0;
        }

        // Шесть граней по полуосям hu, hv, hw; обход по часовой снаружи (как у VoxModel).
        void WriteCube(int b, Vector3 c, Vector3 hu, Vector3 hv, Vector3 hw, Color32 col)
        {
            Face(b,      c, hu,  hv,  hw, col);   // +u
            Face(b + 4,  c, -hu, hw,  hv, col);   // -u
            Face(b + 8,  c, hv,  hw,  hu, col);   // +v
            Face(b + 12, c, -hv, hu,  hw, col);   // -v
            Face(b + 16, c, hw,  hu,  hv, col);   // +w
            Face(b + 20, c, -hw, hv,  hu, col);   // -w
        }

        // Грань с нормалью вдоль n (полуось), стороны a, b.
        void Face(int i, Vector3 c, Vector3 n, Vector3 a, Vector3 b, Color32 col)
        {
            Vector3 o = c + n;
            Vector3 nn = n.normalized;
            // лицевая сторона Unity: cross(v1−v0, v2−v0) = −4·cross(a, b) должен смотреть по n
            if (Vector3.Dot(Vector3.Cross(a, b), n) > 0f) { Vector3 t = a; a = b; b = t; }
            _mv[i]     = o - a - b;
            _mv[i + 1] = o - a + b;
            _mv[i + 2] = o + a + b;
            _mv[i + 3] = o + a - b;
            _mn[i] = _mn[i + 1] = _mn[i + 2] = _mn[i + 3] = nn;
            _mc[i] = _mc[i + 1] = _mc[i + 2] = _mc[i + 3] = col;
        }

        // ---- Случайные числа -----------------------------------------------

        float Rand01()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        Vector3 RandSphere()
        {
            for (int k = 0; k < 8; k++)
            {
                var p = new Vector3(Rand01() * 2f - 1f, Rand01() * 2f - 1f, Rand01() * 2f - 1f);
                float m = p.sqrMagnitude;
                if (m > 1e-4f && m <= 1f) return p;
            }
            return Vector3.up;
        }
    }
}
