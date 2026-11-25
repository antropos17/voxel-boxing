using UnityEngine;

namespace VoxGame
{
    // Упругий отклик соперника на удар (по образцу веб-версии, src/g_game.js: F.hpp, F.tr, F.recoil, F.sx, F.glv).
    // Четыре пружины в осях корня соперника (z — к игроку): сдвиг головы, наклон и скрутка корпуса, отшаг корня в плоскости
    // пола, добавка к позе перчаток. Удар даёт им скорость; пружины возвращают к нулю с затуханием.
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

        // Сдвиг головы: k, c, предел (м), прирост скорости на единицу J (м/с), предел скорости
        public const float HeadK = 260f, HeadC = 22f, HeadMax = 0.05f, HeadGain = 0.1f, HeadVMax = 0.8f;
        // Корпус (градусы): k, c, пределы наклона и скрутки, прирост от момента удара (град/с на Н·м условных), предел скорости
        public const float BodyK = 120f, BodyC = 13f, LeanMax = 20f, TwistMax = 22f, LeanGain = 120f, TwistGain = 480f, BodyVMax = 220f;
        // Отшаг корня (м): k, c, предел, прирост назад и вбок на единицу J
        public const float RootK = 80f, RootC = 11f, RootMax = 0.12f, RootGainZ = 0.35f, RootGainX = 0.25f, RootVMax = 2.5f;
        // Перчатки (м): k, c, предел, прирост вдоль удара и вниз на единицу J
        public const float GloveK = 170f, GloveC = 20f, GloveMax = 0.12f, GloveGain = 0.25f, GloveDown = 0.1f, GloveVMax = 2f;

        public Vector3 head, headV;            // сдвиг головы, м (оси корня)
        public float lean, leanV, twist, twistV;   // корпус, град
        public Vector3 root, rootV;            // отшаг корня, м (y = 0)
        public readonly Vector3[] glove = new Vector3[2], gloveV = new Vector3[2];
        float _acc;
        public int kicks;                      // счётчик толчков (тесты)

        public static float Impulse(int strength, float pw) =>
            Mathf.Min(JMax, J[Mathf.Clamp(strength, 0, 2)] * Mathf.Clamp(pw <= 0f ? 1f : pw, 0.5f, 1.6f));

        // Толчок удара: dir — направление удара, arm — точка попадания относительно шеи (оба в осях корня), j — импульс.
        public void Kick(Vector3 dir, Vector3 arm, float j)
        {
            if (!(j > 0f) || dir.sqrMagnitude < 1e-8f) return;
            dir.Normalize();
            j = Mathf.Min(j, JMax);
            kicks++;
            headV = Vector3.ClampMagnitude(headV + dir * (j * HeadGain), HeadVMax);
            // момент относительно шеи: удар в подбородок снизу запрокидывает, в висок — скручивает
            Vector3 tq = Vector3.Cross(arm, dir) * j;
            leanV  = Mathf.Clamp(leanV + tq.x * LeanGain, -BodyVMax, BodyVMax);
            twistV = Mathf.Clamp(twistV + tq.y * TwistGain, -BodyVMax, BodyVMax);
            Vector3 rv = rootV + new Vector3(dir.x * j * RootGainX, 0f, dir.z * j * RootGainZ);
            rootV = Vector3.ClampMagnitude(rv, RootVMax);
            Vector3 gk = (dir * GloveGain + Vector3.down * GloveDown) * j;
            for (int s = 0; s < 2; s++) gloveV[s] = Vector3.ClampMagnitude(gloveV[s] + gk, GloveVMax);
        }

        // Перчатка s идёт в удар: её пружина гасится, чтобы бьющая рука шла точно в цель.
        public void HoldGlove(int s) { glove[s] = Vector3.zero; gloveV[s] = Vector3.zero; }

        public void Reset()
        {
            head = headV = root = rootV = Vector3.zero;
            lean = leanV = twist = twistV = 0f;
            for (int s = 0; s < 2; s++) glove[s] = gloveV[s] = Vector3.zero;
            _acc = 0f;
        }

        // Продвинуть на dt игрового времени (в стоп-кадре dt = 0 — ничего не меняется).
        public void Step(float dt)
        {
            if (!(dt > 0f)) return;
            _acc += dt;
            int n = 0;
            while (_acc >= H && n < MaxSteps) { Sub(H); _acc -= H; n++; }
            if (n >= MaxSteps && _acc >= H) _acc = 0f;   // длинный кадр (пауза, загрузка): остаток не копится
        }

        void Sub(float h)
        {
            // полунеявный Эйлер: скорость, затем смещение по новой скорости (устойчив при ω·h < 2; здесь ω·h ≤ 0,14)
            headV += (-HeadK * head - HeadC * headV) * h; head = Vector3.ClampMagnitude(head + headV * h, HeadMax);
            leanV += (-BodyK * lean - BodyC * leanV) * h; lean = Mathf.Clamp(lean + leanV * h, -LeanMax, LeanMax);
            twistV += (-BodyK * twist - BodyC * twistV) * h; twist = Mathf.Clamp(twist + twistV * h, -TwistMax, TwistMax);
            rootV += (-RootK * root - RootC * rootV) * h; root = Vector3.ClampMagnitude(root + rootV * h, RootMax);
            for (int s = 0; s < 2; s++)
            {
                gloveV[s] += (-GloveK * glove[s] - GloveC * gloveV[s]) * h;
                glove[s] = Vector3.ClampMagnitude(glove[s] + gloveV[s] * h, GloveMax);
            }
        }

        // Общая мера отклонения (тесты, возврат к покою)
        public float Energy => head.magnitude + root.magnitude + glove[0].magnitude + glove[1].magnitude
                               + (Mathf.Abs(lean) + Mathf.Abs(twist)) * 0.001f;
    }
}
