using UnityEngine;

namespace VoxGame
{
    // Упругий отклик соперника на удар (по образцу веб-версии, src/g_game.js: F.hpp, F.tr, F.recoil, F.sx, F.glv).
    // Цепочка пружин в осях корня соперника (z — к игроку): удар толкает голову (сдвиг на шее и поворот), голова тянет за
    // собой корпус (наклон от бёдер: точка шеи neck), корпус — корень (отшаг ног). Поэтому корпус приходит в пик позже головы,
    // а ноги — позже корпуса. Перчатки — на инерции: корпус ускоряется, они отстают и догоняют (PHYSICS.md).
    // Шаг фиксированный H с накопителем: траектория одна и та же при любом кадре (1/30, 1/60, 1/120). Время — игровое:
    // в стоп-кадре (dt = 0) пружины стоят, повторные толчки зажаты по скорости и смещению — энергия не копится.
    // Без аллокаций: только поля. НЕ MonoBehaviour.
    public class FoeSpring
    {
        // Выключатель всего переноса: FoeBody и PlayerGloves ведут себя как до него (сверка, замер вклада).
        public static bool Enabled = true;

        public const float H = 1f / 120f;   // шаг, с
        public const int MaxSteps = 12;     // не больше шагов за кадр (0,1 с); остаток сверх — отбрасывается

        // Импульс удара J по силе (джеб, силовой, мега) — как в вебе (PJ ≈ 3–4, мега ×2), умножается на силу перчаток pw
        public static readonly float[] J = { 1.1f, 3.4f, 6.8f };
        public const float JMax = 9f;

        // Голова на шее (м): k, c (ζ 0,6), предел, прирост скорости на единицу J (м/с), предел скорости
        public const float HeadK = 150f, HeadC = 14.7f, HeadMax = 0.07f, HeadGain = 0.35f, HeadVMax = 1.6f;
        // Корпус: точка шеи от наклона (м), её тянет голова (NeckPull·сдвиг головы); пределы наклона и скрутки в градусах
        public const float NeckK = 160f, NeckC = 17.7f, NeckPull = 240f, NeckMax = 0.16f, NeckVMax = 1.4f, NeckLen = 0.55f;
        public const float LeanMax = 20f, TwistMax = 22f, BodyVMax = 220f;
        // Поворот головы от момента удара (град) и скрутка корпуса за ним: k, c как у головы и корпуса
        public const float YawGain = 260f, YawMax = 25f, YawVMax = 300f, TwistPull = 240f;
        // Отшаг корня (м): его тянет наклон корпуса (RootPull·точка шеи), только в плоскости пола
        public const float RootK = 50f, RootC = 12.7f, RootPull = 30f, RootMax = 0.08f, RootVMax = 1.2f;
        // Перчатки (м): пружина к месту у корпуса; инерция — ускорение корпуса тянет их назад (GloveLag);
        // блок (перчатки у лица) — толчок по удару (GloveBlock на единицу J)
        public const float GloveK = 170f, GloveC = 20f, GloveMax = 0.12f, GloveLag = 0.9f, GloveBlock = 0.22f, GloveVMax = 2f;
        // Удар в блок: голова и корпус получают такую долю толчка, отклик короче (жёстче)
        public const float BlockShare = 0.4f;

        public Vector3 head, headV;            // сдвиг головы на шее, м (оси корня)
        public Vector3 neck, neckV;            // точка шеи от наклона корпуса, м (оси корня)
        public float yaw, yawV, twist, twistV; // поворот головы и скрутка корпуса, град
        public float lean, roll, leanV;        // наклон корпуса вперёд/назад и вбок (из neck), град; leanV — скорость наклона
        public Vector3 root, rootV;            // отшаг корня, м (y = 0)
        public readonly Vector3[] glove = new Vector3[2], gloveV = new Vector3[2];
        float _acc;
        public int kicks;                      // счётчик толчков (тесты)

        public static float Impulse(int strength, float pw) =>
            Mathf.Min(JMax, J[Mathf.Clamp(strength, 0, 2)] * Mathf.Clamp(pw <= 0f ? 1f : pw, 0.5f, 1.6f));

        // Толчок удара: dir — направление удара, arm — точка попадания относительно шеи (оба в осях корня), j — импульс.
        // guard — соперник в защите: перчатки у лица принимают удар (короткая отдача головы, толчок перчаток).
        public void Kick(Vector3 dir, Vector3 arm, float j, bool guard = false)
        {
            if (!(j > 0f) || dir.sqrMagnitude < 1e-8f) return;
            dir.Normalize();
            j = Mathf.Min(j, JMax);
            kicks++;
            float jh = guard ? j * BlockShare : j;
            headV = Vector3.ClampMagnitude(headV + dir * (jh * HeadGain), HeadVMax);
            // момент относительно шеи: удар в висок поворачивает голову, корпус скручивается за ней
            float tq = Vector3.Cross(arm, dir).y * jh;
            yawV = Mathf.Clamp(yawV + tq * YawGain, -YawVMax, YawVMax);
            if (guard)
            {
                Vector3 gk = dir * (j * GloveBlock);
                for (int s = 0; s < 2; s++) gloveV[s] = Vector3.ClampMagnitude(gloveV[s] + gk, GloveVMax);
            }
        }

        // Перчатка s идёт в удар: её пружина гасится, чтобы бьющая рука шла точно в цель.
        public void HoldGlove(int s) { glove[s] = Vector3.zero; gloveV[s] = Vector3.zero; }

        // Бьющую перчатку соперника остановил блок игрока: отскок назад (к сопернику) на единицу силы
        public void Bounce(int s, float j)
        {
            gloveV[s] = Vector3.ClampMagnitude(gloveV[s] + new Vector3(0f, 0.15f, 1f).normalized * (-j * 1.2f), GloveVMax);
        }

        public void Reset()
        {
            head = headV = neck = neckV = root = rootV = Vector3.zero;
            yaw = yawV = twist = twistV = lean = roll = leanV = 0f;
            for (int s = 0; s < 2; s++) glove[s] = gloveV[s] = Vector3.zero;
            _acc = 0f;
        }

        // Продвинуть на dt игрового времени (в стоп-кадре dt = 0 — ничего не меняется). soft — руки обмякли (оглушение):
        // перчатки мягче держатся у корпуса и сильнее отстают.
        public void Step(float dt, bool soft = false)
        {
            if (!(dt > 0f)) return;
            _acc += dt;
            int n = 0;
            while (_acc >= H && n < MaxSteps) { Sub(H, soft); _acc -= H; n++; }
            if (n >= MaxSteps && _acc >= H) _acc = 0f;   // длинный кадр (пауза, загрузка): остаток не копится
            lean = Mathf.Clamp(Mathf.Atan2(neck.z, NeckLen) * Mathf.Rad2Deg, -LeanMax, LeanMax);
            roll = Mathf.Clamp(-Mathf.Atan2(neck.x, NeckLen) * Mathf.Rad2Deg, -LeanMax, LeanMax);
            leanV = neckV.z / NeckLen * Mathf.Rad2Deg;
        }

        void Sub(float h, bool soft)
        {
            // полунеявный Эйлер: скорость, затем смещение по новой скорости (устойчив при ω·h < 2; здесь ω·h ≤ 0,11)
            headV += (-HeadK * head - HeadC * headV) * h;
            headV = Vector3.ClampMagnitude(headV, HeadVMax); head = Vector3.ClampMagnitude(head + headV * h, HeadMax);
            Vector3 nv0 = neckV, rv0 = rootV;
            neckV += (-NeckK * neck - NeckC * neckV + NeckPull * head) * h;
            neckV = Vector3.ClampMagnitude(neckV, NeckVMax); neck = Vector3.ClampMagnitude(neck + neckV * h, NeckMax);
            yawV += (-HeadK * yaw - HeadC * yawV) * h; yaw = Mathf.Clamp(yaw + yawV * h, -YawMax, YawMax);
            twistV += (-NeckK * twist - NeckC * twistV + TwistPull * yaw) * h;
            twistV = Mathf.Clamp(twistV, -BodyVMax, BodyVMax); twist = Mathf.Clamp(twist + twistV * h, -TwistMax, TwistMax);
            Vector3 pull = new Vector3(neck.x, 0f, neck.z) * RootPull;
            rootV += (-RootK * root - RootC * rootV + pull) * h;
            rootV = Vector3.ClampMagnitude(rootV, RootVMax); root = Vector3.ClampMagnitude(root + rootV * h, RootMax);
            // инерция перчаток: ускорение корпуса у плеч (шея + корень) — перчатки остаются на месте и догоняют
            Vector3 acc = (neckV - nv0 + rootV - rv0) / h;
            float k = soft ? GloveK * 0.6f : GloveK, c = soft ? GloveC * 0.8f : GloveC;
            for (int s = 0; s < 2; s++)
            {
                gloveV[s] += (-k * glove[s] - c * gloveV[s] - GloveLag * acc) * h;
                gloveV[s] = Vector3.ClampMagnitude(gloveV[s], GloveVMax);
                glove[s] = Vector3.ClampMagnitude(glove[s] + gloveV[s] * h, GloveMax);
            }
        }

        // Общая мера отклонения (тесты, возврат к покою)
        public float Energy => head.magnitude + neck.magnitude + root.magnitude + glove[0].magnitude + glove[1].magnitude
                               + (Mathf.Abs(yaw) + Mathf.Abs(twist)) * 0.001f;
    }
}
