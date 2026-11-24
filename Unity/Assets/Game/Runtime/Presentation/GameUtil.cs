using UnityEngine;

namespace VoxGame
{
    // Общие вспомогательные функции уровня представления.
    public static class GameUtil
    {
        // Уничтожение объекта: DestroyImmediate вне Play Mode, иначе Destroy.
        public static void Destroy(Object obj)
        {
            if (obj == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { Object.DestroyImmediate(obj); return; }
#endif
            Object.Destroy(obj);
        }

        // Вертикальный FOV по ширине и высоте экрана.
        // Портрет: 2*atan(tan(12°)/aspect), clamp [36, 60]; ландшафт: 32°.
        public static float CalcFov(float screenWidth, float screenHeight)
        {
            if (screenHeight <= 0f || screenWidth <= 0f) return 46f;
            if (screenWidth > screenHeight) return 32f;
            float aspect = screenWidth / screenHeight;
            float fov = 2f * Mathf.Atan(Mathf.Tan(12f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
            return Mathf.Clamp(fov, 36f, 60f);
        }

        // Порог свайпа в пикселях по правилам web-fight: clamp(min(w,h)*0.065, 22, 40).
        public static float SwipeThreshold(float screenWidth, float screenHeight)
        {
            float mn = Mathf.Min(screenWidth, screenHeight);
            return Mathf.Clamp(mn * 0.065f, 22f, 40f);
        }
    }
}
