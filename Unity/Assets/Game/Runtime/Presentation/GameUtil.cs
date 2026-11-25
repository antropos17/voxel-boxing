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

        // Вертикальный FOV по ширине и высоте экрана: соперник виден по пояс, за ним — ринг.
        // Портрет: по горизонтали 33° (2*atan(tan(16.5°)/aspect)), clamp [40, 66]; ландшафт: 50°.
        public static float CalcFov(float screenWidth, float screenHeight)
        {
            if (screenHeight <= 0f || screenWidth <= 0f) return 50f;
            if (screenWidth > screenHeight) return 50f;
            float aspect = screenWidth / screenHeight;
            float fov = 2f * Mathf.Atan(Mathf.Tan(16.5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
            return Mathf.Clamp(fov, 40f, 66f);
        }

        // Порог свайпа в пикселях по правилам web-fight: clamp(min(w,h)*0.065, 22, 40).
        // Веб-версия считала в CSS-пикселях: размеры экрана и порог здесь — в тех же единицах (см. PixelScale).
        public static float SwipeThreshold(float screenWidth, float screenHeight)
        {
            float mn = Mathf.Min(screenWidth, screenHeight);
            return Mathf.Clamp(mn * 0.065f, 22f, 40f);
        }

        // Физических пикселей на один CSS-пиксель (dp): Screen.dpi / 160; без dpi — по короткой стороне экрана
        // (телефон ~360 dp в ширину). Не меньше 1: на мониторе редактора пиксель и есть пиксель.
        public static float PixelScale(float dpi, float screenWidth, float screenHeight)
        {
            float s = dpi > 1f ? dpi / 160f : Mathf.Min(screenWidth, screenHeight) / 360f;
            return Mathf.Max(1f, s);
        }

        // Шейдеры игры в сборке плеера: Shader.Find находит только шейдеры, попавшие в сборку. Их туда тянут
        // материалы из Resources/Shaders (создаёт GameSetup); если Find не нашёл — берём шейдер у такого материала.
        public static Shader FindShader(string name, string resourceMaterial)
        {
            var s = Shader.Find(name);
            if (s != null) return s;
            var m = Resources.Load<Material>("Shaders/" + resourceMaterial);
            if (m != null && m.shader != null) return m.shader;
            return Shader.Find("Hidden/InternalErrorShader");
        }

        public const string VoxMaterial = "GameVertexColor", AnatMaterial = "GameAnatomy";
    }
}
