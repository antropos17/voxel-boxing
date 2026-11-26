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

        public const int Pelvis = 0, Chest = 1, Sh0 = 2, Sh1 = 3, Knee0 = 4, Knee1 = 5, Foot0 = 6, Foot1 = 7, Head = 8, Hand0 = 9, Hand1 = 10,
                         El0 = 11, El1 = 12, N = 13;
        static readonly float[] Rad = { 0.14f, 0.15f, 0.10f, 0.10f, 0.11f, 0.11f, 0.08f, 0.08f, 0.11f, 0.09f, 0.09f, 0.05f, 0.05f };   // кисть — перчатка
        // объёмы рук и корпуса для запрета взаимопроникновения (м): корпус — капсула таз–грудь, плечо, предплечье, голень
        public const float TorsoR = 0.13f, UpperR = 0.05f, ForeR = 0.045f, ShinR = 0.055f;
        public const float ShCone = 115f;    // плечо: конус вокруг оси «наружу-вниз» от корпуса, град (предел в отчёте 120 — запас на перенос на модель)
        public const float ElbowMin = 36f;   // локоть: внутренний угол не меньше (сгиб ≤ 144°), град

        public readonly Vector3[] x = new Vector3[N], p = new Vector3[N], v = new Vector3[N];
        readonly float[] clusterD = new float[15];
        readonly bool[] wall = new bool[N];
        public const float VMax = 5f;   // м/с: связи не разгоняют узлы сверх этого (канаты, угол ринга)
        float shin0, shin1, neckD;
        readonly float[] upL = new float[2], foL = new float[2], nearL = new float[2];   // плечо, предплечье (до центра кулака), плечо–кулак не ближе
        // колено — шарнир в позе (FoeBody.ApplyFall): сгиб от покоя 0…KneeMax, без переразгибания
        public const float KneeMax = 140f;
        public static bool HandCollide = true;   // замеры: отключить
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
            { _rq = Frame(out Vector3 c); for (int i = 0; i < 6; i++) rel[i] = Quaternion.Inverse(_rq) * (x[i] - c); }
            shin0 = (x[Foot0] - x[Knee0]).magnitude; shin1 = (x[Foot1] - x[Knee1]).magnitude;
            neckD = (x[Head] - x[Chest]).magnitude;
            for (int k = 0; k < 2; k++)
            {
                upL[k] = (x[El0 + k] - x[Sh0 + k]).magnitude; foL[k] = (x[Hand0 + k] - x[El0 + k]).magnitude;
                float c = Mathf.Cos(ElbowMin * Mathf.Deg2Rad);
                nearL[k] = Mathf.Sqrt(upL[k] * upL[k] + foL[k] * foL[k] - 2f * upL[k] * foL[k] * c);
            }
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
            v[El0] = v[El1] = f * (1.1f * s) + Vector3.down * 0.2f;
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
                for (int ai = 0; ai < 4; ai++)   // руки лёгкие: длины, выталкивание из тела и настил сходятся за несколько проходов
                {
                    Arm(0); Arm(1);
                    if (HandCollide) { Limb(0); Limb(1); }
                    for (int i = Hand0; i < N; i++) Bounds(i);
                }
                Arm(0); Arm(1);
                for (int i = 0; i < N; i++) Bounds(i);
                Rigid(); for (int i = 0; i < 6; i++) Bounds(i);
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
            float sv = time > 1.9f ? 4f * SleepV : time > 1.6f ? 2.5f * SleepV : SleepV;
            _still = v2 < sv * sv * N && vmax < 3f * sv ? _still + h : 0f;
            if (_still >= SleepT && time > 0.3f && contacts >= 3) { asleep = true; for (int i = 0; i < N; i++) v[i] = Vector3.zero; }
        }

        // Расстояние a–b: равно d (или не больше d при maxOnly), доли сдвига wa и wb
        void Dist(int a, int b, float d, float wa, float wb, bool maxOnly)
        {
            Vector3 e = x[b] - x[a]; float l = e.magnitude;
            if (l < 1e-6f || (maxOnly && l <= d)) return;
            Vector3 c = e * ((l - d) / l);
            x[a] += c * wa; x[b] -= c * wb;
        }

        // Угол сгиба в [−90°, 270°): сгиб за 180° (голень вверх-вперёд) не перескакивает в переразгибание
        public static float Wrap(float deg) { deg = Mathf.Repeat(deg + 90f, 360f) - 90f; return deg; }

        // Рука из двух звеньев: длины постоянны, локоть сгибается не больше 180° − ElbowMin, плечо в конусе ShCone
        void Arm(int k)
        {
            int sh = Sh0 + k, el = El0 + k, hd = Hand0 + k;
            Dist(sh, el, upL[k], 0.1f, 0.9f, false);
            Dist(el, hd, foL[k], 0.5f, 0.5f, false);
            Vector3 e = x[hd] - x[sh]; float l = e.magnitude;
            if (l < nearL[k] && l > 1e-5f) x[hd] = x[sh] + e * (nearL[k] / l);
            Vector3 up = (x[Chest] - x[Pelvis]).normalized, rt = x[Sh0] - x[Sh1]; rt = (rt - up * Vector3.Dot(rt, up)).normalized;
            Vector3 ax = ((k == 0 ? rt : -rt) - up).normalized, u = x[el] - x[sh];
            float ul = u.magnitude, ang = Vector3.Angle(ax, u);
            if (ang > ShCone && ul > 1e-5f) x[el] = x[sh] + Vector3.Slerp(ax, u / ul, ShCone / ang).normalized * ul;
        }

        // Рука не входит в корпус, голову, голени и другую руку (капсулы и шары; двигается рука — она лёгкая)
        const float LM = 0.008f;   // запас рук в физике: модель повторяет узлы с ошибкой до ~1 см
        void Limb(int k)
        {
            int sh = Sh0 + k, el = El0 + k, hd = Hand0 + k, o = 1 - k;
            float rh = Rad[hd];
            PushOut(Head, x[sh], x[el], Rad[Head] + UpperR + LM);   // голова не входит в плечо у самого сустава (рука туда не уйдёт)
            PushOut(el, x[Pelvis], x[Chest], TorsoR + UpperR + LM, Pelvis, Chest);   // локоть снаружи корпуса: плечо не проходит сквозь грудь
            PushOut(el, x[Head], x[Head], Rad[Head] + UpperR + LM, Head, Head);
            PushOut(hd, x[Pelvis], x[Chest], TorsoR + rh + LM, Pelvis, Chest);
            PushOut(hd, x[Head], x[Head], Rad[Head] + rh + LM, Head, Head);
            PushOut(hd, x[Knee0], x[Foot0], ShinR + rh + LM); PushOut(hd, x[Knee1], x[Foot1], ShinR + rh + LM);
            PushOut(hd, x[Hand0 + o], x[Hand0 + o], Rad[Hand0 + o] + rh + LM);
            PushOut(hd, x[Sh0 + o], x[El0 + o], UpperR + rh + LM, El0 + o, El0 + o);   // кулак не входит в чужое плечо
            PushOut(hd, x[El0 + o], x[Hand0 + o], ForeR + rh + LM, El0 + o, Hand0 + o);   // и в чужое предплечье
            SegPush(el, hd, x[Pelvis], x[Chest], TorsoR + ForeR + LM, false, Pelvis, Chest);
            SegPush(el, hd, x[Head], x[Head], Rad[Head] + ForeR + LM, false, Head, Head);
            SegPush(el, hd, x[Knee0], x[Foot0], ShinR + ForeR + LM, false); SegPush(el, hd, x[Knee1], x[Foot1], ShinR + ForeR + LM, false);
            SegPush(el, hd, x[El0 + o], x[Hand0 + o], 2f * ForeR + LM, false);
            SegPush(el, hd, x[Sh0 + o], x[El0 + o], UpperR + ForeR + LM, false);
            SegPush(sh, el, x[Head], x[Head], Rad[Head] + UpperR + LM, true, Head, Head);
            if (k == 0) SegPush(sh, el, x[Sh1], x[El1], 2f * UpperR + LM, true);   // плечо о плечо (поперёк груди)
            SegPush(sh, el, x[Knee0], x[Foot0], ShinR + UpperR + LM, true); SegPush(sh, el, x[Knee1], x[Foot1], ShinR + UpperR + LM, true);
        }

        // Отрезок i–j не ближе d к отрезку a–b: сдвиг по ближайшим точкам (lockI — двигается только j: плечевой узел — часть корпуса)
        void SegPush(int i, int j, Vector3 a, Vector3 b, float d, bool lockI, int na = -1, int nb = -1)
        {
            Closest(x[i], x[j], a, b, out float s, out float t);
            Vector3 c1 = Vector3.Lerp(x[i], x[j], s), c2 = Vector3.Lerp(a, b, t), e = c1 - c2; float l = e.magnitude;
            if (l >= d) return;
            e = l > 1e-5f ? e / l : Vector3.up;
            if (Lift(e, d - l, t, na, nb)) return;
            e = Out(e);
            Vector3 corr = e * (d - l);
            if (lockI) { x[j] += corr / Mathf.Max(s, 0.3f); return; }
            float kk = 1f / Mathf.Max(0.5f, (1f - s) * (1f - s) + s * s);
            x[i] += corr * ((1f - s) * kk); x[j] += corr * (s * kk);
        }

        // Нормаль выталкивания: вниз (в настил) нельзя — рука под телом уходит вбок, а не упирается в пол
        static Vector3 Out(Vector3 e)
        {
            if (e.y >= -0.3f) return e;
            Vector3 hz = new Vector3(e.x, 0f, e.z);
            return hz.sqrMagnitude > 1e-4f ? hz.normalized : Vector3.right;
        }

        // Ближайшие точки отрезков p1–q1 и p2–q2: параметры s, t
        public static void Closest(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float t)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            if (a < 1e-9f && e < 1e-9f) { s = t = 0f; return; }
            if (a < 1e-9f) { s = 0f; t = Mathf.Clamp01(f / e); return; }
            float c = Vector3.Dot(d1, r);
            if (e < 1e-9f) { t = 0f; s = Mathf.Clamp01(-c / a); return; }
            float bb = Vector3.Dot(d1, d2), den = a * e - bb * bb;
            s = den > 1e-9f ? Mathf.Clamp01((bb * f - c * e) / den) : 0f;
            t = (bb * s + f) / e;
            if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); } else if (t > 1f) { t = 1f; s = Mathf.Clamp01((bb - c) / a); }
        }

        void PushOut(int i, Vector3 a, Vector3 b, float d, int na = -1, int nb = -1)
        {
            Vector3 ab = b - a; float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(x[i] - a, ab) / ab.sqrMagnitude) : 0f;
            Vector3 c = a + ab * t, e = x[i] - c; float l = e.magnitude;
            if (l >= d) return;
            e = l > 1e-5f ? e / l : Vector3.up;
            if (Lift(e, d - l, t, na, nb)) return;
            x[i] += Out(e) * (d - l);
        }

        // Рука под корпусом или головой (выталкивать вниз, в настил): тело ложится на руку — корпус (узлы na–nb) поднимается
        bool Lift(Vector3 e, float depth, float t, int na, int nb)
        {
            if (na < 0 || e.y >= -0.3f) return false;
            if (na == Pelvis)
            {
                // корпус твёрдый: рука под ним поднимает его не выше, чем рука под корпусом помещается (иначе плечо тянет
                // руку вверх вслед за корпусом и тело «всплывает» на метры); выше — рука уходит вбок
                float cap = floorY + TorsoR + 0.13f;
                if (Mathf.Max(x[Pelvis].y, x[Chest].y) >= cap) return false;
                x[Pelvis].y = Mathf.Min(x[Pelvis].y + depth * (1f - t) * 1.5f, Mathf.Max(x[Pelvis].y, cap));
                x[Chest].y = Mathf.Min(x[Chest].y + depth * t * 1.5f, Mathf.Max(x[Chest].y, cap));
                return true;
            }
            if (na == nb) x[na].y += depth;
            else { x[na].y += depth * (1f - t) * 1.5f; x[nb].y += depth * t * 1.5f; }
            return true;
        }

        // Голова не дальше NeckCone от оси корпуса (таз → грудь)
        // корпус (таз, грудь, плечи, колени) — твёрдое тело: узлы на свои места в осях таз->грудь, плечо->плечо.
        // Рисованное плечо стоит на корпусе модели; без этого узел плеча уходит от него на см, и развод рук в физике
        // не совпадает с моделью (PHYSICS.md, проникновение рук)
        readonly Vector3[] rel = new Vector3[6];
        Quaternion Frame(out Vector3 c)
        {
            c = Vector3.zero; for (int i = 0; i < 6; i++) c += x[i] / 6f;
            Vector3 up = (x[Chest] - x[Pelvis]).normalized, rt = (x[Sh0] - x[Sh1]).normalized;
            return Quaternion.LookRotation(Vector3.Cross(rt, up), up);
        }
        Quaternion _rq = Quaternion.identity;
        // поворот — лучшее совпадение формы по всем шести узлам (вращательная часть деформации, Müller 2016), не по четырём:
        // упор колен в настил поворачивает корпус, а не теряется
        void Rigid()
        {
            Vector3 c = Vector3.zero; for (int i = 0; i < 6; i++) c += x[i] / 6f;
            Vector3 a0 = Vector3.zero, a1 = Vector3.zero, a2 = Vector3.zero;
            for (int i = 0; i < 6; i++) { Vector3 d = x[i] - c; a0 += d * rel[i].x; a1 += d * rel[i].y; a2 += d * rel[i].z; }
            Quaternion q = _rq;
            for (int k = 0; k < 8; k++)
            {
                Vector3 r0 = q * Vector3.right, r1 = q * Vector3.up, r2 = q * Vector3.forward;
                Vector3 w = (Vector3.Cross(r0, a0) + Vector3.Cross(r1, a1) + Vector3.Cross(r2, a2))
                          / (Mathf.Abs(Vector3.Dot(r0, a0) + Vector3.Dot(r1, a1) + Vector3.Dot(r2, a2)) + 1e-9f);
                float wl = w.magnitude; if (wl < 1e-6f) break;
                q = Quaternion.AngleAxis(wl * Mathf.Rad2Deg, w / wl) * q; q.Normalize();
            }
            _rq = q;
            for (int i = 0; i < 6; i++) x[i] = c + q * rel[i];
        }

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
