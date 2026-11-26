using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGame
{
    public enum FxKind : byte { Blood, Spark, Sweat, Star, Mist, Chunk, Fire, Stain }

    // Пул простых частиц: кровь (капли-штрихи, мелкая морось, кубики мяса, пятна на настиле), искры, пот, огонь меги,
    // звёздочки оглушения. Кольцевой буфер: новая частица занимает самый старый слот. Физика — в массивах, без аллокаций в кадре.
    // Отрисовка — один динамический меш из кубиков с цветом в вершинах (шейдер слоя VoxGame/VertexColor): живые частицы
    // пишутся подряд в начало буфера вершин, рисуется только их диапазон индексов. Обычный MeshRenderer надёжно попадает
    // в Camera.Render вне Play Mode. Шейдер непрозрачный, поэтому частица «гаснет» уменьшением размера.
    // Кровь — как bloodBurst веб-версии (src/e_fx.js): две трети — капли, вытянутые по скорости в штрих (чем длиннее, тем
    // тоньше), треть — морось; размеры и скорости умножены на масштаб вида (камера Unity дальше от лица, чем в вебе).
    public class FxParticles
    {
        public const int   MaxParticles = 640;
        public const float Gravity      = 7.6f;    // м/с², как в вебе
        public const float FloorY       = 0f;      // настил ринга
        public const float Glow         = 0.55f;   // самосвечение материала: кровь читается в тени
        public const float StarRate     = 7f;      // звёздочек в секунду над оглушённым
        public const float StarRadius   = 0.15f;   // м, орбита вокруг головы
        public const float StarSpeed    = 0.65f;   // м/с по орбите
        public const float SparkSize    = 0.011f;  // м, средний размер искры
        public const float SpawnLead    = 0.03f;   // м: рождение перед поверхностью, со стороны удара
        // Кровь (веб: капля 2,2–5,5 мм, морось 1,2–2,2 мм, штрих до ×5,5 длины и не длиннее 2,6 % расстояния до камеры)
        public const float DropMin = 0.0022f, DropMax = 0.0055f, MistMin = 0.0012f, MistMax = 0.0022f;
        public const float StreakMax = 4.5f, StreakScreen = 0.026f, StreakK = 0.011f;
        public const float DropLifeMin = 1.6f, DropLifeMax = 2.4f, StainLife = 2.0f;
        public const float Near0 = 0.2f, Near1 = 0.6f;   // у объектива капля сжимается до нуля: не закрывает бой

        const int VertsPer = 24, IdxPer = 36;

        [StructLayout(LayoutKind.Sequential)]
        struct Vtx { public Vector3 p; public Vector3 n; public Color32 c; }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
        };

        static readonly Color32[] BloodCol =
        {
            new Color32(178, 12, 12, 255), new Color32(150, 10, 10, 255), new Color32(205, 26, 20, 255), new Color32(120, 8, 9, 255),
        };

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
        readonly Vtx[] _vb;
        Mesh       _mesh;
        Material   _mat;
        GameObject _go;
        int        _drawn;
        Vector3    _camPos;
        bool       _hasCam;

        public int  AliveCount => _alive;
        public uint Born => _born;
        public Mesh Mesh => _mesh;
        public int  DrawnCount => _drawn;

        // parent == null — только логика (тесты), без меша.
        public FxParticles(Transform parent)
        {
            _vb = new Vtx[MaxParticles * VertsPer];
            if (parent == null) return;

            var idx = new ushort[MaxParticles * IdxPer];
            for (int p = 0; p < MaxParticles; p++)
            for (int f = 0; f < 6; f++)
            {
                int b = p * VertsPer + f * 4, t = p * IdxPer + f * 6;
                idx[t] = (ushort)b; idx[t + 1] = (ushort)(b + 1); idx[t + 2] = (ushort)(b + 2);
                idx[t + 3] = (ushort)b; idx[t + 4] = (ushort)(b + 2); idx[t + 5] = (ushort)(b + 3);
            }
            _mesh = new Mesh { name = "FxParticles" };
            _mesh.MarkDynamic();
            _mesh.SetVertexBufferParams(MaxParticles * VertsPer, Layout);
            _mesh.SetIndexBufferParams(idx.Length, IndexFormat.UInt16);
            _mesh.SetIndexBufferData(idx, 0, 0, idx.Length, MeshUpdateFlags.DontValidateIndices);
            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0), Flags);
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

        const MeshUpdateFlags Flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
                                    | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        // Положение камеры: капли у объектива сжимаются (как NEAR0/NEAR1 веба), штрих ограничен по экрану.
        public void SetCamera(Vector3 camPos) { _camPos = camPos; _hasCam = true; }

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

        // Кровь (правка HIT-FX-2): из точки попадания на голове; основная масса летит в сторону удара — хук вбок по удару,
        // апперкот вверх, джеб веером к камере и в стороны, мега и разрыв — во все стороны. Две трети капель медленные
        // (облако у точки удара), треть — быстрые штрихи. Подъём ограничен, к камере — не быстрее ToCamMax: брызги не
        // долетают до объектива и не уходят за верхний край, тяжесть роняет их вниз. k — масштаб вида, spd — сила.
        public const int BurstJab = 0, BurstHook = 1, BurstUpper = 2, BurstAll = 3;
        public const float BloodSpeed = 1.25f;   // м/с: основная скорость при spd = 1, k = 1
        public const float ToCamMax = 0.9f;      // м/с: предел скорости к камере
        public const float UpMax = 2.2f;         // м/с: предел подъёма (апперкот)
        public const float DropK = 2.4f, BigDrop = 1.8f;   // размер капли ×масштаб вида ×DropK; каждая шестая ×BigDrop

        public void BloodBurst(Vector3 at, Vector3 dir, Vector3 toCam, int n, int kind, float spd, float k)
        {
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 c = toCam.sqrMagnitude > 1e-6f ? toCam.normalized : -d;
            Vector3 rx = Vector3.Cross(Vector3.up, c);
            rx = rx.sqrMagnitude > 1e-6f ? rx.normalized : Vector3.right;
            Vector3 cf = c - Vector3.Dot(c, Vector3.up) * Vector3.up;   // к камере по горизонтали
            cf = cf.sqrMagnitude > 1e-6f ? cf.normalized : c;
            float sx = Vector3.Dot(d, rx);
            Vector3 side = rx * (Mathf.Abs(sx) > 0.05f ? Mathf.Sign(sx) : 1f);
            Vector3 from = at + c * SpawnLead;
            float vk = BloodSpeed * spd * Mathf.Sqrt(k);
            for (int i = 0; i < n; i++)
            {
                var col = BloodCol[RandIdx(4)];
                bool mist = i % 3 == 2;
                float m = (i % 3 == 0) ? R(0.8f, 1.7f) : R(0.3f, 0.8f);   // треть быстрых, остальные — облако у точки
                Vector3 v;
                switch (kind)
                {
                    case BurstHook:
                        v = side * m + rx * R(-0.15f, 0.15f) + Vector3.up * R(-0.1f, 0.45f) * m + cf * R(-0.1f, 0.35f) * m;
                        break;
                    case BurstUpper:
                        v = Vector3.up * (0.15f + m) + rx * R(-0.3f, 0.3f) * m + cf * R(-0.1f, 0.3f) * m;
                        break;
                    case BurstJab:
                    {
                        float a = R(-1f, 1f);
                        v = (cf * 0.5f + rx * a + Vector3.up * R(-0.1f, 0.3f)) * m;
                        break;
                    }
                    default:
                        v = RandSphere() * (1.2f * m) + Vector3.up * 0.2f * m;
                        break;
                }
                v *= vk;
                float vc = Vector3.Dot(v, c);
                if (vc > ToCamMax) v -= c * (vc - ToCamMax);
                if (v.y > UpMax) v.y = UpMax;
                // размеры вразброс (доработка 2): каждая шестая капля крупная, морось тоже крупнее — всплеск читается на телефоне
                float big = i % 6 == 0 ? BigDrop : 1f;
                if (mist) Spawn(FxKind.Mist, from, v, R(MistMin, MistMax) * k * DropK, R(0.25f, 0.6f), col);
                else Spawn(FxKind.Blood, from + RandSphere() * 0.01f, v, R(DropMin, DropMax) * k * DropK * big, R(DropLifeMin, DropLifeMax), col);
            }
        }

        // Совместимость: кровь без камеры (тесты, нокаут): «к камере» — против удара.
        public void BloodBurst(Vector3 at, Vector3 dir, int n, float speed) =>
            BloodBurst(at, dir, -dir, n, BurstAll, speed * 0.6f, 1.5f);

        // Кубики мяса и кожи: крупнее капель, кувыркаются, отскакивают от настила.
        public void Chunks(Vector3 at, Vector3 dir, Vector3 toCam, int n, float spd, float k)
        {
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 c = toCam.sqrMagnitude > 1e-6f ? toCam.normalized : -d;
            Vector3 side = d - Vector3.Dot(d, c) * c;
            for (int i = 0; i < n; i++)
            {
                // подъём и полёт к камере ограничены, как у крови (HIT-FX-2): кубики не улетают за верхний край
                Vector3 v = side * R(0.6f, 2f) + RandSphere() * 0.8f + Vector3.up * R(0.2f, 1f) + c * R(0f, 0.6f);
                Color32 col = Rand01() < 0.6f ? new Color32((byte)R(140, 190), (byte)R(18, 40), (byte)R(20, 36), 255)
                                              : new Color32((byte)R(200, 230), (byte)R(120, 150), (byte)R(110, 130), 255);
                Spawn(FxKind.Chunk, at + c * SpawnLead + RandSphere() * 0.015f, v * spd * Mathf.Sqrt(k), R(0.006f, 0.011f) * k,
                      R(1.4f, 2.2f), col);
            }
        }

        // Огонь меги (веб, вид 7): оранжевые кубики, всплывают и гаснут.
        public void Fire(Vector3 at, Vector3 dir, Vector3 toCam, int n, float k)
        {
            Vector3 d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 c = toCam.sqrMagnitude > 1e-6f ? toCam.normalized : -d;
            Vector3 side = d - Vector3.Dot(d, c) * c;
            Vector3 rx = Vector3.Cross(Vector3.up, c);
            rx = rx.sqrMagnitude > 1e-6f ? rx.normalized : Vector3.right;
            for (int i = 0; i < n; i++)
            {
                Vector3 v = rx * R(-2.6f, 2.6f) + side * 2f + Vector3.up * R(-1f, 2.8f) + c * R(-0.5f, 2.4f);
                Spawn(FxKind.Fire, at + c * SpawnLead, v * Mathf.Sqrt(k), R(0.005f, 0.012f) * k, R(0.2f, 0.5f),
                      new Color32(255, (byte)R(110, 230), 30, 255));
            }
        }

        // Искры удара: быстрые, короткие, вытянуты по скорости (блок по перчаткам).
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

        // Пот: светлые брызги в стороны и вверх (веб: вместо крови, когда удар её не пустил).
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
                        case FxKind.Stain: continue;
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
                        case FxKind.Mist:
                            v.y -= Gravity * dt;
                            v *= 1f - Mathf.Min(1f, 2.5f * dt);
                            break;
                        case FxKind.Fire:
                            v.y += 2.4f * dt;
                            float l = 1f - Mathf.Min(1f, 3f * dt); v.x *= l; v.z *= l;
                            break;
                        default:   // капли и кубики
                            v.y -= Gravity * dt;
                            float h = 1f - Mathf.Min(1f, 0.6f * dt); v.x *= h; v.z *= h;
                            break;
                    }
                    Vector3 p = _pos[i] + v * dt;
                    if (_kind[i] == FxKind.Chunk) _spin[i] += _spinV[i] * dt;

                    // Настил: капля ложится пятном, морось и огонь гаснут, остальное отскакивает
                    if (p.y < FloorY + _size[i] * 0.5f && v.y < 0f)
                    {
                        p.y = FloorY + 0.001f;
                        switch (_kind[i])
                        {
                            case FxKind.Blood:
                                _kind[i] = FxKind.Stain; v = Vector3.zero;
                                _life[i] = _life0[i] = StainLife; _size[i] *= 1.8f;
                                break;
                            case FxKind.Mist: case FxKind.Fire:
                                _life[i] = 0f; _alive--; continue;
                            default:
                                p.y = FloorY + _size[i] * 0.5f;
                                if (Mathf.Abs(v.y) < 0.6f) { v = Vector3.zero; _spinV[i] = 0f; }
                                else { v.y *= -0.32f; v.x *= 0.6f; v.z *= 0.6f; _spinV[i] *= 0.5f; }
                                break;
                        }
                    }
                    _pos[i] = p; _vel[i] = v;
                    if (_kind[i] != FxKind.Chunk) _spin[i] += _spinV[i] * dt;
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

        // Живых частиц этого вида (тесты, снимки).
        public int Count(FxKind kind)
        {
            int n = 0;
            for (int i = 0; i < MaxParticles; i++) if (_life[i] > 0f && _kind[i] == kind) n++;
            return n;
        }

        // Положения живых капель и мороси, рождённых не раньше fromSeq (замер разлёта крови); вернёт число записанных.
        // Показать или спрятать меш частиц (замер: площадь крови на снимке).
        public void SetDrawn(bool on) { if (_go != null) _go.SetActive(on); }

        public int CopyBlood(uint fromSeq, Vector3[] dst)
        {
            int n = 0;
            for (int i = 0; i < MaxParticles && n < dst.Length; i++)
                if (_life[i] > 0f && _seq[i] >= fromSeq && (_kind[i] == FxKind.Blood || _kind[i] == FxKind.Mist)) dst[n++] = _pos[i];
            return n;
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

        // Кубик на частицу: капли — штрих по скорости (веб: длина до ×5,5, толщина s·√(s/l)), кубики мяса кувыркаются,
        // пятно на настиле — плоский квадрат, пот и звёздочки вращаются.
        void RebuildMesh()
        {
            if (_mesh == null) return;
            if (_alive == 0 && _drawn == 0) return;
            // частицы живут в мире, меш — в осях своего объекта
            Matrix4x4 w2l = _go.transform.worldToLocalMatrix;
            int j = 0;
            for (int i = 0; i < MaxParticles; i++)
            {
                if (_life[i] <= 0f) continue;
                float t = _life[i] / _life0[i];
                FxKind kind = _kind[i];
                float s = _size[i];
                // гаснет уменьшением в конце жизни (капли и пятна — за 0,3 с, остальное — в последней трети)
                if (kind == FxKind.Blood || kind == FxKind.Stain || kind == FxKind.Mist) s *= Mathf.Min(1f, _life[i] / 0.3f);
                else s *= Mathf.Clamp01(t * 3f);
                if (kind == FxKind.Star) s *= 0.8f + 0.2f * Mathf.Sin(_spin[i] * 2f);

                float cd = 99f;
                if (_hasCam)
                {
                    cd = Vector3.Distance(_pos[i], _camPos);
                    if (cd < Near1) { if (cd <= Near0) continue; s *= (cd - Near0) / (Near1 - Near0); }
                }
                if (s <= 1e-5f) continue;

                Vector3 u, v, w;
                float su = s, sv = s, sw = s;
                Vector3 vel = _vel[i];
                float sp = vel.magnitude;
                if ((kind == FxKind.Blood || kind == FxKind.Spark) && sp > 0.3f)
                {
                    w = vel / sp;
                    Vector3 a = Mathf.Abs(w.y) < 0.9f ? Vector3.up : Vector3.right;
                    u = Vector3.Cross(a, w).normalized;
                    v = Vector3.Cross(w, u);
                    if (kind == FxKind.Blood)
                    {
                        float l = Mathf.Max(s, Mathf.Min(s * (1f + Mathf.Min(StreakMax, sp * StreakK / s)), cd * StreakScreen));
                        sw = l; su = sv = s * Mathf.Sqrt(s / l);
                    }
                    else sw = s * (1f + Mathf.Min(sp * 1.6f, 2.2f));
                }
                else if (kind == FxKind.Stain)
                {
                    u = Vector3.right; v = Vector3.forward; w = Vector3.up;
                    sw = s * 0.12f;
                }
                else
                {
                    float ca = Mathf.Cos(_spin[i]), sa = Mathf.Sin(_spin[i]);
                    u = new Vector3(ca, sa, 0f); v = new Vector3(-sa * 0.8f, ca * 0.8f, 0.6f).normalized;
                    w = Vector3.Cross(u, v);
                }
                WriteCube(j * VertsPer, w2l.MultiplyPoint3x4(_pos[i]), w2l.MultiplyVector(u * (su * 0.5f)),
                          w2l.MultiplyVector(v * (sv * 0.5f)), w2l.MultiplyVector(w * (sw * 0.5f)), _col[i]);
                j++;
            }

            if (j > 0) _mesh.SetVertexBufferData(_vb, 0, 0, j * VertsPer, 0, Flags);
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, j * IdxPer), Flags);
            _drawn = j;
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
            _vb[i]     = new Vtx { p = o - a - b, n = nn, c = col };
            _vb[i + 1] = new Vtx { p = o - a + b, n = nn, c = col };
            _vb[i + 2] = new Vtx { p = o + a + b, n = nn, c = col };
            _vb[i + 3] = new Vtx { p = o + a - b, n = nn, c = col };
        }

        // ---- Случайные числа (свой генератор: порядок случайных чисел боя не трогается) ---------------

        float Rand01()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        float R(float a, float b) => a + (b - a) * Rand01();
        int RandIdx(int n) => Mathf.Min(n - 1, (int)(Rand01() * n));

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
