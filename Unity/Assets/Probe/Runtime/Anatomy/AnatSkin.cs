using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe
{
    /// Этап A3: изгиб шеи — 8 суставов (череп–C1, C1–C2, C2–C3 … C7–T1) с весами, поза только в вершинном шейдере
    /// (Anatomy.shader, _SkinOn). Решётки, видимость, сетки и поиск кусков остаются в позе покоя; шейдер переводит
    /// вершину мир → см головы → изгиб → мир. Тот же изгиб на CPU (Skin) и обратный ему (Unskin) — для луча удара:
    /// точка луча в позе переводится в точку покоя, удар и разрез считаются там.
    /// Вес сустава j — smoothstep по высоте YEff (AnatBend): всё выше границы жёсткой головы идёт с черепом одним телом;
    /// повороты применяются от верхнего сустава к нижнему вокруг осей покоя. Ниже T1 (y < −15,8) — неподвижный якорь.
    public static class AnatSkin
    {
        public const int N = 8;
        public static readonly float[] Y = { -3.7f, -4.6f, -6.5f, -8.2f, -9.9f, -11.6f, -13.3f, -15.0f };   // высота сустава, см
        public const float PivZ = -1.0f;                                                                  // оси — через тела позвонков
        public const float YBot = AnatBend.YBot, YRef = AnatBend.YRef;   // граница жёсткой головы и низ зоны изгиба — AnatBend
        public static readonly float[] Blend = { 0.5f, 1.0f, 0.8f, 0.8f, 0.8f, 0.8f, 0.8f, 0.8f };          // полуширина перехода, см
        // доли суставов в кивке (ось X), повороте (Y) и наклоне к плечу (Z); сумма каждой строки — 1
        public static readonly float[] FNod = { 0.30f, 0.05f, 0.65f / 6, 0.65f / 6, 0.65f / 6, 0.65f / 6, 0.65f / 6, 0.65f / 6 };
        public static readonly float[] FTurn = { 0.05f, 0.50f, 0.075f, 0.075f, 0.075f, 0.075f, 0.075f, 0.075f };
        public static readonly float[] FTilt = { 0.10f, 0.05f, 0.85f / 6, 0.85f / 6, 0.85f / 6, 0.85f / 6, 0.85f / 6, 0.85f / 6 };
        public static readonly float3 Limit = math.radians(new float3(45f, 80f, 40f));   // кивок вперёд/назад, поворот, наклон

        public static bool on;              // включён ли изгиб (просмотр); вне просмотра поза покоя
        public static float3 pose;          // радианы: x — кивок (+ вперёд, подбородок вниз), y — поворот, z — наклон к плечу
        public static float3 recoil, recoilV;   // отлёт от удара: пружина с затуханием поверх позы
        public const float K = 60f, C = 5.4f, MaxRecoil = 0.7f;
        public static readonly float[] PushGain = { 0.08f, 0.18f, 0.30f };   // рад/с на см плеча удара, по силе удара
        public static readonly float3 PushPivot = new float3(0, -9f, -1f);

        public static float3 Total => pose + recoil;
        public static float3 JointRot(int j, float3 r) => new float3(r.x * FNod[j], r.y * FTurn[j], r.z * FTilt[j]);
        public static float YEff(float3 p) => AnatBend.YEff(p);
        public static float Weight(int j, float3 p) => math.smoothstep(Y[j] - Blend[j], Y[j] + Blend[j], YEff(p));

        public static void Reset() { pose = 0; recoil = 0; recoilV = 0; }

        /// Изгиб точки покоя p (см головы) в позу; M — суммарный поворот в этой точке (без учёта изменения весов).
        public static float3 Skin(float3 p, out float3x3 M) => Skin(p, Total, out M);
        public static float3 Skin(float3 p, float3 r, out float3x3 M)
        {
            float3 q = p; M = float3x3.identity;
            for (int j = 0; j < N; j++)
            {
                float3 rv = JointRot(j, r) * Weight(j, p);
                float a = math.length(rv);
                if (a < 1e-7f) continue;
                var R = float3x3.AxisAngle(rv / a, a);
                float3 piv = new float3(0, Y[j], PivZ);
                q = piv + math.mul(R, q - piv); M = math.mul(R, M);
            }
            return q;
        }

        /// Обратный изгиб: точка в позе → точка покоя. Ньютон с приближённым якобианом M (поворот в точке).
        public static float3 Unskin(float3 q) => Unskin(q, Total);
        public static float3 Unskin(float3 q, float3 r)
        {
            // два начала: сама точка (шея у якоря) и точка, повёрнутая обратно целиком (голова); берётся лучшее
            float3 a = Solve(q, r, q, out float ea);
            if (ea < 1e-8f) return a;
            float3 b = Solve(q, r, Skin(q, -r, out _), out float eb);
            return eb < ea ? b : a;
        }

        static float3 Solve(float3 q, float3 r, float3 p, out float err)
        {
            // Ньютон с приближённым якобианом и откатом шага: у складок (кивок, ткани сминаются) шаг делится пополам
            float3 e = q - Skin(p, r, out var M);
            err = math.lengthsq(e);
            for (int it = 0; it < 32 && err > 1e-10f; it++)
            {
                // численный якобиан изгиба (веса меняются по точке — поворот M его не заменяет при скрутке)
                float3 s0 = q - e;
                var Jm = new float3x3((Skin(p + new float3(0.01f, 0, 0), r, out _) - s0) * 100f, (Skin(p + new float3(0, 0.01f, 0), r, out _) - s0) * 100f,
                    (Skin(p + new float3(0, 0, 0.01f), r, out _) - s0) * 100f);
                float det = math.determinant(Jm);
                float3 d = math.abs(det) > 1e-3f ? math.mul(math.inverse(Jm), e) : math.mul(math.transpose(M), e);
                for (int k = 0; k < 8; k++, d *= 0.5f)
                {
                    float3 pn = p + d, en = q - Skin(pn, r, out var Mn);
                    float ern = math.lengthsq(en);
                    if (ern < err || k == 7) { p = pn; e = en; err = ern; M = Mn; break; }
                }
            }
            return p;
        }

        /// Отлёт: удар в точку hit (покой) по направлению dir (покой) даёт угловую скорость (плечо × направление).
        public static void Push(float3 hit, float3 dir, int strength)
        {
            recoilV += math.cross(hit - PushPivot, math.normalize(dir)) * PushGain[math.clamp(strength, 0, 2)];
        }

        public static void Step(float dt)
        {
            // шаг считается целиком (подшаги по 4 мс): раньше dt обрезался до 0,1 с, и Step(1,4) оставлял голову у пика отлёта
            if (!(dt > 0)) return;
            int n = (int)math.min(math.ceil(dt / 0.004f), 2500);
            float h = dt / math.max(n, 1);
            for (int i = 0; i < n; i++)
            {
                recoilV += (-K * recoil - C * recoilV) * h;
                recoil += recoilV * h;
                float m = math.length(recoil);
                if (m > MaxRecoil) { recoil *= MaxRecoil / m; recoilV -= math.dot(recoilV, recoil / MaxRecoil) * recoil / MaxRecoil; }
            }
        }

        public static void Turn(float3 d) => pose = math.clamp(pose + d, new float3(-Limit.x * 0.9f, -Limit.y, -Limit.z), Limit);

        static readonly int W2HId = Shader.PropertyToID("_AnatW2H"), H2WId = Shader.PropertyToID("_AnatH2W"), JId = Shader.PropertyToID("_SkinJ"),
            PId = Shader.PropertyToID("_SkinP"), SId = Shader.PropertyToID("_SkinS"), TId = Shader.PropertyToID("_SkinT"), OnId = Shader.PropertyToID("_SkinOn");
        static readonly Vector4[] J = new Vector4[N], P = new Vector4[N], TV = new Vector4[7];

        /// Параметры шейдера: мир ↔ см головы (корень головы root), поворот каждого сустава, точки и веса.
        public static void SetGlobals(Transform root)
        {
            var w2h = Matrix4x4.Scale(Vector3.one * 100f) * root.worldToLocalMatrix;
            Shader.SetGlobalMatrix(W2HId, w2h); Shader.SetGlobalMatrix(H2WId, w2h.inverse);
            float3 r = Total;
            for (int j = 0; j < N; j++) { float3 v = JointRot(j, r); J[j] = new Vector4(v.x, v.y, v.z, 0); P[j] = new Vector4(0, Y[j], PivZ, Blend[j]); }
            for (int i = 0; i < 28; i++) TV[i >> 2][i & 3] = AnatBend.T[math.min(i, AnatBend.T.Length - 1)];
            Shader.SetGlobalVector(SId, new Vector4(YBot, YRef, AnatBend.TZ0, AnatBend.T.Length - 1));
            Shader.SetGlobalVectorArray(JId, J); Shader.SetGlobalVectorArray(PId, P); Shader.SetGlobalVectorArray(TId, TV);
            Shader.SetGlobalFloat(OnId, on ? 1f : 0f);
        }

        public static void Off() { on = false; Shader.SetGlobalFloat(OnId, 0f); }
    }

    /// Граница жёсткой головы и высота YEff для весов суставов (AnatSkin). Отдельный класс без управляемых полей:
    /// InBend вызывает мешер из джоба Burst, а статический конструктор AnatSkin (Shader.PropertyToID) Burst не компилирует.
    public static class AnatBend
    {
        // Жёсткая голова. Всё выше границы YTop(x, z) идёт с черепом как одно тело: лицо, нижняя челюсть, зубы, глаза, затылок.
        // Граница лежит на 0,6 см ниже нижнего края челюсти (спереди) и на 1 см ниже основания черепа (сзади и по бокам);
        // у позвоночника (|x| < 2–3,5 см, z −8…1) она равна YRef — там суставы стоят на своих позвонках.
        // Ниже границы высота сжимается: YTop → YRef, YBot → YBot (линейно), и веса суставов считаются по этой высоте YEff.
        public const float YBot = -15.8f, YRef = -3.2f;   // YRef = AnatSkin.Y[0] + Blend[0]: выше неё все веса — 1
        public const float TZ0 = -12f;                     // T[i] — граница при z = TZ0 + i, см
        public static readonly float[] T =
        {
            -4.6f, -4.6f, -4.6f, -4.6f, -4.6f, -4.6f, -4.6f, -4.6f, -4.6f, -4.8f, -5.4f, -6.6f,    // z −12 … −1: затылок и сосцевидный отросток
            -8.5f, -8.9f, -9.1f, -9.3f, -9.3f, -9.5f, -9.7f, -10.3f, -10.3f, -10.3f, -10.3f, -10.3f, -10.3f, -10.3f,   // z 0 … 13: под нижней челюстью
        };
        public static float SpineMask(float3 p) => (1 - math.smoothstep(2f, 3.5f, math.abs(p.x))) * math.smoothstep(-8f, -6.5f, p.z) * (1 - math.smoothstep(0f, 1f, p.z));
        public static float YTop(float3 p)
        {
            float zf = math.clamp(p.z - TZ0, 0, T.Length - 1); int i = math.min((int)zf, T.Length - 2);
            return math.lerp(math.lerp(T[i], T[i + 1], zf - i), YRef, SpineMask(p));
        }
        public static float YEff(float3 p)
        {
            float yt = YTop(p);
            return p.y >= yt ? p.y - yt + YRef : YBot + (p.y - YBot) * (YRef - YBot) / (yt - YBot);
        }
        /// Точка в зоне изгиба (с запасом): там грани не склеиваются в длинные прямоугольники (AnatMesher.StyleBend).
        public static bool InBend(float3 p) { float ye = YEff(p); return ye < YRef + 0.4f && ye > YBot - 0.4f; }
        /// Лицо, которое двигает мимика (AnatFace.Deform): брови, веки, щёки, губы, подбородок.
        public static bool InFace(float3 p) => p.z > 3.0f && p.y > -10.6f && p.y < 6.8f && math.abs(p.x) < 6.6f;
    }
}
