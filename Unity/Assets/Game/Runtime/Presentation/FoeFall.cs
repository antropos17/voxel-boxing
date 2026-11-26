using UnityEngine;

namespace VoxGame
{
    // Падение соперника в нокдауне и нокауте (PHYSICS.md, направление 3): тело из 11 узлов-шаров (мир, метры).
    // Таз, грудь, плечи и колени — одно жёсткое звено (бёдра у модели — часть торса), голени, голова на шее с пределом
    // наклона, кисти на руках предельной длины. Удар задаёт направление: голова и грудь уходят по удару, таз проседает,
    // колени подламываются — тело складывается по коленям и шее, а не падает доской. Настил — пол с трением и малым
    // отскоком, канаты — стенки квадрата ринга. Шаг фиксированный H (позиционные связи), засыпает, когда все узлы стоят.
    // Без аллокаций в кадре: массивы создаются один раз. НЕ MonoBehaviour.
    public class FoeFall
    {
        public static bool Enabled = true;
        public const float H = 1f / 120f;
        public const int MaxSteps = 12, Iter = 6;
        public const float SleepV = 0.1f, SleepT = 0.25f;    // все узлы медленнее SleepV (м/с, ~1,7 мм за кадр) SleepT с — тело лежит
        public const float NeckCone = 55f;                   // наклон головы от оси корпуса, град

        public const int Pelvis = 0, Chest = 1, Sh0 = 2, Sh1 = 3, Knee0 = 4, Knee1 = 5, Foot0 = 6, Foot1 = 7, Head = 8, Hand0 = 9, Hand1 = 10, N = 11;
        static readonly float[] Rad = { 0.14f, 0.15f, 0.10f, 0.10f, 0.11f, 0.11f, 0.08f, 0.08f, 0.11f, 0.07f, 0.07f };

        public readonly Vector3[] x = new Vector3[N], p = new Vector3[N], v = new Vector3[N];
        readonly float[] clusterD = new float[15];
        readonly bool[] wall = new bool[N];
        public const float VMax = 5f;   // м/с: связи не разгоняют узлы сверх этого (канаты, угол ринга)
        float shin0, shin1, neckD, arm0, arm1;
        float _acc, _still;
        public bool active, asleep;
        public float time, floorY;
        // ринг: центр, оси (единичные, в плоскости пола), полуширина по канатам
        Vector3 _ringC, _ringX = Vector3.right, _ringZ = Vector3.forward; float _ringHalf = 100f;
        public int steps;

        static readonly int[] PA = { 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 3, 3, 4 };
        static readonly int[] PB = { 1, 2, 3, 4, 5, 2, 3, 4, 5, 3, 4, 5, 4, 5, 5 };

        public static float Radius(int i) => Rad[i];

        public void SetArena(Vector3 center, float yawDeg, float half)
        {
            _ringC = center;
            Quaternion q = Quaternion.Euler(0f, yawDeg, 0f);
            _ringX = q * Vector3.right; _ringZ = q * Vector3.forward; _ringHalf = half;
        }

        // Начать падение: pos — узлы в текущей позе (мир), dir — направление добивающего удара (мир), j — импульс удара,
        // ko — нокаут (сильнее и без подъёма).
        public void Begin(Vector3[] pos, Vector3 dir, float j, bool ko, float floor)
        {
            floorY = floor;
            for (int i = 0; i < N; i++) { x[i] = pos[i]; v[i] = Vector3.zero; }
            for (int k = 0; k < 15; k++) clusterD[k] = (x[PA[k]] - x[PB[k]]).magnitude;
            shin0 = (x[Foot0] - x[Knee0]).magnitude; shin1 = (x[Foot1] - x[Knee1]).magnitude;
            neckD = (x[Head] - x[Chest]).magnitude;
            arm0 = Mathf.Max((x[Hand0] - x[Sh0]).magnitude, 0.5f); arm1 = Mathf.Max((x[Hand1] - x[Sh1]).magnitude, 0.5f);
            Vector3 f = new Vector3(dir.x, 0f, dir.z);
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.back;
            float s = Mathf.Clamp(0.75f + 0.12f * j, 0.8f, 1.6f) * (ko ? 1.25f : 1f);
            // верх уходит по удару, таз проседает и чуть отъезжает, колени подламываются навстречу, голова — хлыстом
            v[Head] = f * (2.0f * s) + Vector3.down * 0.3f;
            v[Chest] = f * (1.5f * s) + Vector3.down * 0.4f;
            v[Sh0] = v[Sh1] = f * (1.4f * s) + Vector3.down * 0.4f;
            v[Pelvis] = f * (1.3f * s) + Vector3.down * 0.6f;
            v[Knee0] = v[Knee1] = f * (0.5f * s) + Vector3.down * 0.7f;
            v[Hand0] = v[Hand1] = f * (0.9f * s) + Vector3.up * 0.6f;
            _acc = 0f; _still = 0f; time = 0f; active = true; asleep = false; steps = 0;
        }

        public void Stop() { active = false; asleep = false; }

        public void Step(float dt)
        {
            if (!active || !(dt > 0f)) return;
            time += dt;
            if (asleep) return;
            _acc += dt;
            int n = 0;
            while (_acc >= H && n < MaxSteps) { Sub(H); _acc -= H; n++; if (asleep) { _acc = 0f; break; } }
            if (n >= MaxSteps && _acc >= H) _acc = 0f;
        }

        void Sub(float h)
        {
            steps++;
            float damp = Mathf.Exp(-0.6f * h);
            for (int i = 0; i < N; i++)
            {
                v[i].y -= 9.81f * h;
                v[i] *= damp;
                p[i] = x[i]; wall[i] = false;
                x[i] += v[i] * h;
            }
            for (int it = 0; it < Iter; it++)
            {
                for (int k = 0; k < 15; k++) Dist(PA[k], PB[k], clusterD[k], 0.5f, 0.5f, false);
                Dist(Knee0, Foot0, shin0, 0.5f, 0.5f, false); Dist(Knee1, Foot1, shin1, 0.5f, 0.5f, false);
                Dist(Chest, Head, neckD, 0.3f, 0.7f, false);
                Cone();
                Dist(Sh0, Hand0, arm0, 0.1f, 0.9f, true); Dist(Sh1, Hand1, arm1, 0.1f, 0.9f, true);
                for (int i = 0; i < N; i++) Bounds(i);
            }
            // сдвиг узла за подшаг не больше VMax·h: связи у канатов и в углу не выбрасывают тело (между p и x — внутри ринга и над настилом)
            float dmax = VMax * h;
            for (int i = 0; i < N; i++) { Vector3 dd = x[i] - p[i]; float dl = dd.magnitude; if (dl > dmax) x[i] = p[i] + dd * (dmax / dl); }
            float vmax = 0f, v2 = 0f; int contacts = 0;
            for (int i = 0; i < N; i++) if (wall[i] || x[i].y <= floorY + Rad[i] + 1e-3f) contacts++;   // настил или канаты
            // тело легло (3+ узла на настиле): общее вязкое трение — не раскачивается и засыпает
            float rest = contacts >= 3 || time > 1.6f ? Mathf.Exp(-(5f + 20f * Mathf.Max(0f, time - 0.8f)) * h) : 1f;   // позже 0,8 с гасится сильнее
            for (int i = 0; i < N; i++)
            {
                Vector3 nv = (x[i] - p[i]) / h;
                bool floor = x[i].y <= floorY + Rad[i] + 1e-3f;
                // канаты гасят скорость: у стенки узел не отскакивает и скользит с трением
                if (wall[i]) nv *= Mathf.Exp(-25f * h);
                if (nv.sqrMagnitude > VMax * VMax) nv *= VMax / nv.magnitude;
                if (floor)
                {
                    // малый отскок при ударе о настил, трение скольжения
                    if (i >= Head && v[i].y < -0.8f) nv.y = -v[i].y * 0.22f;   // голова и кисти — с отскоком
                    nv.x *= Mathf.Exp(-9f * h); nv.z *= Mathf.Exp(-9f * h);
                }
                v[i] = nv * rest;
                vmax = Mathf.Max(vmax, nv.magnitude); v2 += nv.sqrMagnitude;
            }
            // лежит: средняя скорость узлов < SleepV и ни один не быстрее 3·SleepV (голова на пределе шеи дрожит на мм);
            // после 1,6 с порог в 2,5 раза мягче — остаточная дрожь связей не держит тело «живым»
            float sv = time > 1.6f ? 2.5f * SleepV : SleepV;
            _still = v2 < sv * sv * N && vmax < 3f * sv ? _still + h : 0f;
            if (_still >= SleepT && time > 0.3f) { asleep = true; for (int i = 0; i < N; i++) v[i] = Vector3.zero; }
        }

        // Расстояние a–b: равно d (или не больше d при maxOnly), доли сдвига wa и wb
        void Dist(int a, int b, float d, float wa, float wb, bool maxOnly)
        {
            Vector3 e = x[b] - x[a]; float l = e.magnitude;
            if (l < 1e-6f || (maxOnly && l <= d)) return;
            Vector3 c = e * ((l - d) / l);
            x[a] += c * wa; x[b] -= c * wb;
        }

        // Голова не дальше NeckCone от оси корпуса (таз → грудь)
        void Cone()
        {
            Vector3 ax = (x[Chest] - x[Pelvis]).normalized, nk = x[Head] - x[Chest];
            float l = nk.magnitude; if (l < 1e-6f) return;
            float ang = Vector3.Angle(ax, nk);
            if (ang <= NeckCone) return;
            Vector3 dirN = Vector3.Slerp(ax, nk / l, NeckCone / ang).normalized;
            x[Head] = x[Chest] + dirN * l;
        }

        void Bounds(int i)
        {
            float r = Rad[i];
            if (x[i].y < floorY + r) x[i].y = floorY + r;
            Vector3 d = x[i] - _ringC;
            float lim = _ringHalf - r - 0.04f, dx = Vector3.Dot(d, _ringX), dz = Vector3.Dot(d, _ringZ);
            if (dx > lim) { x[i] -= _ringX * (dx - lim); wall[i] = true; } else if (dx < -lim) { x[i] -= _ringX * (dx + lim); wall[i] = true; }
            if (dz > lim) { x[i] -= _ringZ * (dz - lim); wall[i] = true; } else if (dz < -lim) { x[i] -= _ringZ * (dz + lim); wall[i] = true; }
        }

        // Тест: всё внутри ринга (с допуском)
        public bool InRing(int i, float tol = 0.01f)
        {
            Vector3 d = x[i] - _ringC;
            return Mathf.Abs(Vector3.Dot(d, _ringX)) <= _ringHalf + tol && Mathf.Abs(Vector3.Dot(d, _ringZ)) <= _ringHalf + tol;
        }

        public float MaxSpeed { get { float m = 0f; for (int i = 0; i < N; i++) m = Mathf.Max(m, v[i].magnitude); return m; } }
    }
}
