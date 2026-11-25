using UnityEngine;
using UnityEngine.Rendering;

namespace VoxGame
{
    public enum CrowdMood { Idle, Tense, Cheer }

    // Геометрия трибун в осях ринга (те же оси, что у RingBuilder: центр настила в начале координат,
    // настил на высоте 0, трибуны с четырёх сторон, сторона k повёрнута на k·90°, ряды уходят по +z стороны).
    public struct CrowdStands
    {
        public Vector3 center;      // центр ринга в осях parent
        public float   yawDeg;      // поворот ринга вокруг вертикали
        public float   floorY;      // пол зала относительно настила
        public int     steps;       // ступеней у трибуны
        public float   stepRise;    // подъём одной ступени
        public float   firstDist;   // первый ряд: расстояние от центра ринга
        public float   rowStep;     // шаг рядов вглубь
        public int     rows;        // рядов; ряды за последней ступенью стоят на ней в полный рост
        public float   halfW0;      // полуширина первого ряда
        public float   halfWStep;   // прирост полуширины на ряд
        public float   seatStep;    // шаг мест в ряду
        public float   fill;        // доля занятых мест (0..1), сверх MaxCrowd не будет
        public uint    seed;

        // Совпадает с трибунами RingBuilder (три ступени по 0.6 м, ряды через 1.1 м от 4.95 м),
        // плюс стоячий ряд на верхней ступени.
        public static CrowdStands Default(Vector3 center, float yawDeg) => new CrowdStands
        {
            center = center, yawDeg = yawDeg,
            floorY = -1.0f, steps = 3, stepRise = 0.6f,
            firstDist = 4.95f, rowStep = 1.1f, rows = 4,
            halfW0 = 4.6f, halfWStep = 1.1f, seatStep = 0.55f,
            fill = 0.9f, seed = 0x9E3779B9u,
        };
    }

    // Зрители вокруг ринга. НЕ MonoBehaviour: Build / Tick / Dispose зовёт владелец (FightDirector).
    //
    // Как рисуется. Инстансинг (Graphics.DrawMeshInstanced / RenderMeshInstanced) отвергнут: шейдер
    // VoxGame/VertexColor собран без multi_compile_instancing, а Graphics.Draw* вне Play Mode при ручном
    // Camera.Render в RenderTexture (снимки ShotsTaker) в кадр попадает ненадёжно. Поэтому по одной
    // объединённой сетке на сторону (4 обычных MeshRenderer — их видит любая камера в любом режиме),
    // а анимация — сдвигом вершин на CPU в заранее выделенных массивах: у каждого зрителя свой отрезок
    // вершин, к нему прибавляется подскок и наклон к рингу, руки плавно переходят из позы «вниз» в позу
    // «вверх» (обе позы лежат в массивах базы и альтернативы). В кадре нет new: SetVertices из готового массива.
    public class CrowdView
    {
        public const int MaxCrowd = 300;

        // ---- Настройки вида (подгонять по снимкам) -------------------------------------------------

        const float Ambient  = 0.34f;   // как у зала в RingBuilder
        const float FogStart = 5.5f;
        const float FogEnd   = 21f;

        const float MoodRate      = 3.0f;   // скорость перехода настроения, 1/с
        const float BurstDecay    = 2.4f;   // затухание всплеска, 1/с

        const float IdleBob       = 0.012f; // покачивание вверх-вниз, м
        const float IdleSway      = 0.035f; // покачивание вперёд-назад (наклон на метр роста)
        const float TenseLean     = 0.20f;  // наклон к рингу при нокдауне (м на метр роста)
        const float TenseBob      = 0.008f;
        const float TenseArms     = 0.35f;  // часть зрителей приподнимает руки
        const float CheerWaveJump = 0.17f;  // подскок на гребне волны, м
        const float CheerHop      = 0.05f;  // частые подпрыгивания вне гребня, м
        const float CheerHopRate  = 9.0f;   // рад/с
        const float CheerArms     = 0.55f;  // руки подняты у всех хотя бы на столько
        const float CheerLean     = 0.06f;
        const float WaveSpeed     = 2.2f;   // волна вокруг ринга, рад/с
        const float BurstJump     = 0.13f;
        const float BurstHopRate  = 14f;

        const float TwoPi = 6.2831853f;

        // ---- Раскладка вершин зрителя ---------------------------------------------------------------
        // 5 коробок (торс, голова, волосы/шапка, левая и правая рука) × 5 граней (без задней) × 4 вершины.
        const int BoxV      = 20;
        const int BoxI      = 30;
        const int VertsPer  = 5 * BoxV;
        const int IdxPer    = 5 * BoxI;
        const int ArmV0     = 3 * BoxV;   // с этой вершины идут руки

        static readonly Color32[] Cloth =
        {
            new Color32(178,  46,  46, 255), new Color32( 46,  74, 170, 255), new Color32(196, 196, 190, 255),
            new Color32( 38,  38,  44, 255), new Color32(112, 112, 118, 255), new Color32(196, 160,  54, 255),
            new Color32( 52, 118,  72, 255), new Color32(200, 112,  40, 255), new Color32(104,  62, 134, 255),
            new Color32( 40, 118, 128, 255), new Color32(112,  78,  52, 255), new Color32( 36,  46,  86, 255),
        };
        static readonly Color32[] Skin =
        {
            new Color32(232, 192, 162, 255), new Color32(214, 168, 132, 255), new Color32(188, 138, 102, 255),
            new Color32(152, 106,  74, 255), new Color32(114,  78,  54, 255), new Color32( 88,  60,  42, 255),
        };
        static readonly Color32[] Hair =
        {
            new Color32( 30,  26,  24, 255), new Color32( 62,  42,  28, 255), new Color32(102,  72,  44, 255),
            new Color32(198, 166, 102, 255), new Color32(152, 150, 146, 255), new Color32(150,  72,  40, 255),
        };

        // Грани коробки в долях полуразмера: +x, −x, +y, −y, −z (к рингу). Задняя (+z) не видна из ринга.
        static readonly Vector3[] FaceN =
        {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, -1, 0), new Vector3(0, 0, -1),
        };
        static readonly Vector3[] FaceC =
        {
            new Vector3( 1, -1, -1), new Vector3( 1,  1, -1), new Vector3( 1,  1,  1), new Vector3( 1, -1,  1),
            new Vector3(-1, -1,  1), new Vector3(-1,  1,  1), new Vector3(-1,  1, -1), new Vector3(-1, -1, -1),
            new Vector3(-1,  1, -1), new Vector3(-1,  1,  1), new Vector3( 1,  1,  1), new Vector3( 1,  1, -1),
            new Vector3(-1, -1,  1), new Vector3(-1, -1, -1), new Vector3( 1, -1, -1), new Vector3( 1, -1,  1),
            new Vector3(-1, -1, -1), new Vector3(-1,  1, -1), new Vector3( 1,  1, -1), new Vector3( 1, -1, -1),
        };

        // ---- Состояние --------------------------------------------------------------------------------

        Transform _root;
        Material  _mat;
        readonly Mesh[]      _mesh  = new Mesh[4];
        readonly Vector3[][] _base  = new Vector3[4][];   // поза «руки вниз»
        readonly Vector3[][] _alt   = new Vector3[4][];   // поза «руки вверх» (отличается только у рук)
        readonly Vector3[][] _work  = new Vector3[4][];
        readonly int[]       _first = new int[5];         // зрители стороны s: [_first[s], _first[s+1])

        // по зрителю
        readonly int[]   _vStart = new int[MaxCrowd];
        readonly float[] _seatY  = new float[MaxCrowd];
        readonly float[] _phase  = new float[MaxCrowd];
        readonly float[] _freq   = new float[MaxCrowd];
        readonly float[] _waveA  = new float[MaxCrowd];
        readonly float[] _excite = new float[MaxCrowd];

        CrowdMood _mood = CrowdMood.Idle;
        float _wIdle = 1f, _wTense, _wCheer;
        float _burst, _time;
        uint  _rng;

        public int Count { get; private set; }
        public CrowdMood Mood => _mood;
        public Transform Root => _root;

        // ---- Построение --------------------------------------------------------------------------------

        public void Build(Transform parent, CrowdStands st)
        {
            Dispose();
            _rng = st.seed != 0 ? st.seed : 0x9E3779B9u;

            _root = new GameObject("Crowd").transform;
            _root.SetParent(parent, false);
            _root.localPosition = st.center;
            _root.localRotation = Quaternion.Euler(0f, st.yawDeg, 0f);

            var shader = Shader.Find("VoxGame/VertexColor");
            _mat = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader")) { name = "Crowd" };
            _mat.SetFloat("_Ambient", Ambient);
            _mat.SetFloat("_Glow", 0f);
            _mat.SetFloat("_FogStart", FogStart);
            _mat.SetFloat("_FogEnd", FogEnd);

            // Места: сначала все, потом ровно K из них выборкой без возврата (равномерно по всем сторонам).
            int total = 0;
            for (int side = 0; side < 4; side++)
                for (int k = 0; k < st.rows; k++)
                    total += RowSeats(st, k, out _, out _, out _, out _);
            int need = Mathf.Min(MaxCrowd, Mathf.RoundToInt(total * Mathf.Clamp01(st.fill)));

            var sx = new float[MaxCrowd]; var sz = new float[MaxCrowd]; var sy = new float[MaxCrowd];
            var stand = new bool[MaxCrowd];
            int n = 0, left = total;
            for (int side = 0; side < 4; side++)
            {
                _first[side] = n;
                for (int k = 0; k < st.rows; k++)
                {
                    int cnt = RowSeats(st, k, out float x0, out float dist, out float seatY, out bool standing);
                    for (int j = 0; j < cnt; j++, left--)
                    {
                        if (need <= 0 || Rnd() * left >= need) continue;
                        need--;
                        sx[n] = x0 + j * st.seatStep + (Rnd() - 0.5f) * 0.12f;
                        sz[n] = dist + (Rnd() - 0.5f) * 0.10f;
                        sy[n] = seatY;
                        stand[n] = standing;
                        n++;
                    }
                }
            }
            _first[4] = n;
            Count = n;

            for (int side = 0; side < 4; side++)
                BuildSide(side, sx, sy, sz, stand);
        }

        // Мест в ряду k и их геометрия в осях стороны.
        static int RowSeats(in CrowdStands st, int k, out float x0, out float dist, out float seatY, out bool standing)
        {
            dist = st.firstDist + st.rowStep * k;
            int step = Mathf.Min(k, st.steps - 1);
            seatY = st.floorY + st.stepRise * (step + 1);
            standing = k >= st.steps;
            // не заходить за диагональ угла, где начинается трибуна соседней стороны
            float w = Mathf.Min(st.halfW0 + st.halfWStep * k, dist - 0.45f);
            float stagger = (k & 1) == 1 ? st.seatStep * 0.5f : 0f;
            x0 = -w + stagger;
            if (w <= 0f || st.seatStep <= 0f) return 0;
            return (int)((2f * w - stagger) / st.seatStep) + 1;
        }

        void BuildSide(int side, float[] sx, float[] sy, float[] sz, bool[] stand)
        {
            int i0 = _first[side], i1 = _first[side + 1], cnt = i1 - i0;
            var sideT = new GameObject("Side" + side).transform;
            sideT.SetParent(_root, false);
            sideT.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
            if (cnt == 0) return;

            var bv = new Vector3[cnt * VertsPer];
            var av = new Vector3[cnt * VertsPer];
            var nv = new Vector3[cnt * VertsPer];
            var cv = new Color32[cnt * VertsPer];
            var tv = new int[cnt * IdxPer];
            var sideQ = Quaternion.Euler(0f, side * 90f, 0f);

            for (int i = i0; i < i1; i++)
            {
                int v0 = (i - i0) * VertsPer;
                _vStart[i] = v0;
                _seatY[i]  = sy[i];
                _phase[i]  = Rnd() * TwoPi;
                _freq[i]   = 0.35f + 0.45f * Rnd();
                _excite[i] = 0.35f + 0.65f * Rnd();
                var ringPos = sideQ * new Vector3(sx[i], 0f, sz[i]);
                _waveA[i]  = Mathf.Atan2(ringPos.x, ringPos.z);

                // внешность из зерна
                float kc = 0.78f + 0.32f * Rnd();
                Color32 cloth = Mul(Cloth[Pick(Cloth.Length)], kc);
                Color32 skin  = Mul(Skin[Pick(Skin.Length)], 0.9f + 0.15f * Rnd());
                Color32 hair  = Mul(Hair[Pick(Hair.Length)], 0.85f + 0.3f * Rnd());
                Color32 hat   = Mul(Cloth[Pick(Cloth.Length)], 0.9f + 0.2f * Rnd());
                Color32 arms  = Rnd() < 0.5f ? skin : Mul(cloth, 0.92f);
                float r = Rnd();
                int hairStyle = r < 0.15f ? 0 : r < 0.60f ? 1 : r < 0.85f ? 2 : 3;   // лысый, коротко, копна, шапка

                // фигура: сидит (бёдра на ступени) или стоит в полный рост
                float jh = Rnd() * 0.10f;
                float tw = 0.18f + 0.05f * Rnd();               // полуширина торса
                float th = 0.25f + jh * 0.5f;                   // полувысота торса сидящего
                float hip = stand[i] ? sy[i] + 0.45f : sy[i];
                float shoulder = hip + 2f * th;
                const float hw = 0.095f, hh = 0.105f, hz = 0.095f;
                float hy = shoulder + 0.02f + hh;

                // поворот к центру ринга (не полностью: ряд смотрит в целом вперёд)
                float yaw = Mathf.Atan2(sx[i], sz[i]) * 0.6f;
                var q = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
                var p = new Vector3(sx[i], 0f, sz[i]);

                int v = v0;
                var torsoC = new Vector3(0f, (sy[i] + shoulder) * 0.5f, 0f);
                var torsoH = new Vector3(tw, (shoulder - sy[i]) * 0.5f, 0.13f);
                v = AddBox(bv, av, nv, cv, v, p, q, torsoC, torsoH, torsoC, torsoH, cloth);
                var headC = new Vector3(0f, hy, 0f);
                var headH = new Vector3(hw, hh, hz);
                v = AddBox(bv, av, nv, cv, v, p, q, headC, headH, headC, headH, skin);

                Vector3 hairC, hairH; Color32 hairCol;
                switch (hairStyle)
                {
                    case 0:  hairC = headC; hairH = headH * 0.9f; hairCol = skin; break;   // спрятано в голове
                    case 1:  hairC = new Vector3(0f, hy + hh - 0.025f, 0.02f);
                             hairH = new Vector3(hw + 0.008f, 0.04f, hz - 0.012f); hairCol = hair; break;
                    case 2:  hairC = new Vector3(0f, hy + 0.03f, 0.03f);
                             hairH = new Vector3(hw + 0.012f, hh, hz - 0.02f); hairCol = hair; break;
                    default: hairC = new Vector3(0f, hy + hh - 0.01f, 0f);
                             hairH = new Vector3(hw + 0.014f, 0.045f, hz + 0.014f); hairCol = hat; break;
                }
                v = AddBox(bv, av, nv, cv, v, p, q, hairC, hairH, hairC, hairH, hairCol);

                for (int s = -1; s <= 1; s += 2)
                {
                    var downC = new Vector3(s * (tw + 0.05f), shoulder - 0.21f, -0.01f);
                    var downH = new Vector3(0.05f, 0.20f, 0.055f);
                    var upC   = new Vector3(s * (tw + 0.03f), shoulder + 0.23f, 0f);
                    var upH   = new Vector3(0.05f, 0.23f, 0.05f);
                    v = AddBox(bv, av, nv, cv, v, p, q, downC, downH, upC, upH, arms);
                }

                int t0 = (i - i0) * IdxPer;
                for (int f = 0; f < 25; f++)
                {
                    int b = v0 + f * 4, t = t0 + f * 6;
                    tv[t] = b; tv[t + 1] = b + 1; tv[t + 2] = b + 2;
                    tv[t + 3] = b; tv[t + 4] = b + 2; tv[t + 5] = b + 3;
                }
            }

            var mesh = new Mesh { name = "crowd" + side };
            mesh.indexFormat = bv.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.MarkDynamic();
            mesh.SetVertices(bv);
            mesh.SetNormals(nv);
            mesh.SetColors(cv);
            mesh.SetTriangles(tv, 0);
            // рамка с запасом на подскоки, наклон и поднятые руки — пересчитывать её в кадре не нужно
            var bounds = mesh.bounds;
            for (int k = 0; k < av.Length; k++) bounds.Encapsulate(av[k]);
            bounds.Expand(new Vector3(0.5f, 0.9f, 0.6f));
            mesh.bounds = bounds;

            _mesh[side] = mesh;
            _base[side] = bv;
            _alt[side]  = av;
            _work[side] = (Vector3[])bv.Clone();
            VoxModel.Spawn("crowd", sideT, mesh, _mat);
        }

        // Коробка в позе a (база) и b (альтернатива): углы соответствуют, поэтому позы можно смешивать по вершинам.
        static int AddBox(Vector3[] bv, Vector3[] av, Vector3[] nv, Color32[] cv, int v, Vector3 p, Quaternion q,
                          Vector3 ca, Vector3 ha, Vector3 cb, Vector3 hb, Color32 col)
        {
            for (int f = 0; f < 5; f++)
            {
                var n = q * FaceN[f];
                // верх светлее, низ темнее — объём читается и в тумане
                float k = f == 2 ? 1.06f : f == 3 ? 0.8f : 1f;
                var c = Mul(col, k);
                for (int j = 0; j < 4; j++)
                {
                    var corner = FaceC[f * 4 + j];
                    bv[v] = p + q * (ca + Vector3.Scale(corner, ha));
                    av[v] = p + q * (cb + Vector3.Scale(corner, hb));
                    nv[v] = n;
                    cv[v] = c;
                    v++;
                }
            }
            return v;
        }

        // ---- Управление ---------------------------------------------------------------------------------

        // Настроение толпы; переход плавный (MoodRate).
        public void SetMood(CrowdMood mood) => _mood = mood;

        // Короткий всплеск после сильного удара: strength 0..1 (больше 1 обрезается), повторный берёт максимум.
        public void Burst(float strength) => _burst = Mathf.Max(_burst, Mathf.Clamp01(strength));

        // dt — игровое время: в стоп-кадре зал замирает вместе с боем, в слоу-мо замедляется.
        int _half;

        public void Tick(float dt, float udt)
        {
            if (_root == null || dt <= 0f) return;
            _time += dt;
            if (_time > 10000f) _time -= 10000f;   // не терять точность синусов в долгой сессии

            float k = 1f - Mathf.Exp(-MoodRate * dt);
            _wIdle  += ((_mood == CrowdMood.Idle  ? 1f : 0f) - _wIdle)  * k;
            _wTense += ((_mood == CrowdMood.Tense ? 1f : 0f) - _wTense) * k;
            _wCheer += ((_mood == CrowdMood.Cheer ? 1f : 0f) - _wCheer) * k;
            _burst  *= Mathf.Exp(-BurstDecay * dt);
            if (_burst < 0.002f) _burst = 0f;

            // за кадр — две трибуны из четырёх через одну: зал движется с половинной частотой, затраты вдвое меньше
            float t = _time;
            _half = 1 - _half;
            for (int side = _half; side < 4; side += 2)
            {
                var mesh = _mesh[side];
                if (mesh == null) continue;
                var bv = _base[side]; var av = _alt[side]; var wv = _work[side];

                for (int i = _first[side]; i < _first[side + 1]; i++)
                {
                    float ph = _phase[i], f = _freq[i], ex = _excite[i];
                    float jy = 0f, lean = 0f, arm = 0f;

                    if (_wIdle > 0.001f)
                    {
                        jy   += _wIdle * IdleBob * Mathf.Sin(t * f * TwoPi + ph);
                        lean += _wIdle * IdleSway * Mathf.Sin(t * f * 1.3f + ph * 1.7f);
                    }
                    if (_wTense > 0.001f)
                    {
                        lean += _wTense * TenseLean * (0.6f + 0.4f * ex);
                        jy   += _wTense * TenseBob * Mathf.Sin(t * f * TwoPi * 2.3f + ph);
                        arm  += _wTense * TenseArms * ex * ex;
                    }
                    if (_wCheer > 0.001f)
                    {
                        float w = Mathf.Cos(_waveA[i] - t * WaveSpeed);
                        w = w > 0f ? w * w * w * w : 0f;
                        float hop = Mathf.Abs(Mathf.Sin(t * CheerHopRate * (0.8f + 0.4f * f) + ph));
                        jy   += _wCheer * (CheerWaveJump * w + CheerHop * hop * ex);
                        arm  += _wCheer * (CheerArms + (1f - CheerArms) * w);
                        lean += _wCheer * CheerLean;
                    }
                    if (_burst > 0f)
                    {
                        float b = _burst * ex;
                        jy += BurstJump * b * Mathf.Abs(Mathf.Sin(t * BurstHopRate + ph));
                        if (b > arm) arm = b;
                    }
                    if (arm > 1f) arm = 1f;

                    float seat = _seatY[i];
                    int v0 = _vStart[i], va = v0 + ArmV0, v1 = v0 + VertsPer;
                    for (int v = v0; v < va; v++)
                    {
                        var b = bv[v];
                        wv[v] = new Vector3(b.x, b.y + jy, b.z - lean * (b.y - seat));
                    }
                    for (int v = va; v < v1; v++)
                    {
                        var b = bv[v]; var a = av[v];
                        float x = b.x + (a.x - b.x) * arm, y = b.y + (a.y - b.y) * arm, z = b.z + (a.z - b.z) * arm;
                        wv[v] = new Vector3(x, y + jy, z - lean * (y - seat));
                    }
                }
                mesh.SetVertices(wv, 0, wv.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
        }

        public void Dispose()
        {
            for (int s = 0; s < 4; s++)
            {
                if (_mesh[s] != null) GameUtil.Destroy(_mesh[s]);
                _mesh[s] = null; _base[s] = _alt[s] = _work[s] = null;
            }
            if (_mat != null) GameUtil.Destroy(_mat);
            _mat = null;
            if (_root != null) GameUtil.Destroy(_root.gameObject);
            _root = null;
            Count = 0;
            _mood = CrowdMood.Idle;
            _wIdle = 1f; _wTense = _wCheer = _burst = _time = 0f;
        }

        // ---- Вспомогательное ------------------------------------------------------------------------------

        float Rnd()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        int Pick(int len) => Mathf.Min(len - 1, (int)(Rnd() * len));

        static Color32 Mul(Color32 c, float k) =>
            new Color32((byte)Mathf.Min(255f, c.r * k), (byte)Mathf.Min(255f, c.g * k), (byte)Mathf.Min(255f, c.b * k), 255);
    }
}
