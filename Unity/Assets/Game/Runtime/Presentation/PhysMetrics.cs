using System.Collections.Generic;
using UnityEngine;

namespace VoxGame
{
    // Меры отклика на удар по записи смещений (по кадру): пик, направление в пике, время возврата, переходы через ноль.
    // Только для тестов и замеров (PHYSICS.md); в кадре боя не вызывается.
    public static class PhysMetrics
    {
        public static int Peak(List<Vector3> d, out float mag)
        {
            int at = 0; mag = 0f;
            for (int i = 0; i < d.Count; i++) { float m = d[i].magnitude; if (m > mag) { mag = m; at = i; } }
            return at;
        }

        public static int Peak(List<float> d, out float mag)
        {
            int at = 0; mag = 0f;
            for (int i = 0; i < d.Count; i++) { float m = Mathf.Abs(d[i]); if (m > mag) { mag = m; at = i; } }
            return at;
        }

        // Угол (град) между смещением в пике и направлением удара
        public static float PeakAngle(List<Vector3> d, Vector3 dir)
        {
            int at = Peak(d, out float m);
            return m > 1e-7f ? Vector3.Angle(d[at], dir) : 180f;
        }

        // Номер кадра, с которого отклонение навсегда меньше frac пика (−1 — не вернулось за запись)
        public static int Settle(List<Vector3> d, float frac = 0.05f)
        {
            Peak(d, out float m);
            for (int i = d.Count - 1; i >= 0; i--) if (d[i].magnitude >= frac * m) return i + 1 < d.Count ? i + 1 : -1;
            return 0;
        }

        // Переходы через ноль после пика: проекция на направление пика, гистерезис hyst пика (дрожание мельче не считается)
        public static int ZeroCross(List<Vector3> d, float hyst = 0.02f)
        {
            int at = Peak(d, out float m);
            if (m < 1e-7f) return 0;
            Vector3 u = d[at] / m; float h = hyst * m; int sg = 1, n = 0;
            for (int i = at + 1; i < d.Count; i++)
            {
                float p = Vector3.Dot(d[i], u);
                if (sg > 0 && p < -h) { sg = -1; n++; }
                else if (sg < 0 && p > h) { sg = 1; n++; }
            }
            return n;
        }
    }
}
