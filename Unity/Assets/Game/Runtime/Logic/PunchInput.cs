using UnityEngine;

namespace VoxGame
{
    public enum PunchType { Jab, Hook, Upper, Mega }

    public struct PunchInput
    {
        public PunchType type;
        public int       side;        // -1 лево, +1 право
        public float     speed;       // px/ms свайпа, 0 для джеба
        public float     pw;          // clamp(0.95 + speed*0.22, 1, 1.35)
        public bool      isMega;      // megaReady && swipe
        public bool      isFlurry;
        public Vector2   screenStart;
        public Vector2   screenEnd;

        // Вычислить pw из скорости свайпа
        public static float CalcPw(float speedPxMs)
            => Mathf.Clamp(0.95f + speedPxMs * 0.22f, 1f, 1.35f);
    }
}
