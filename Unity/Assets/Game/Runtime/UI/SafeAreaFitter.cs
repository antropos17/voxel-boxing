// SafeAreaFitter.cs — подгоняет RectTransform по safe-area.
// Attach к контейнеру, который должен оставаться в пределах выреза нотча/закруглений.
using UnityEngine;

namespace VoxGame {

    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour {

        RectTransform _rt;

        // Явный вызов с конкретными значениями (для снимков — искусственная safe-area).
        // Параметры:
        //   safe   — прямоугольник в пикселях экрана (нижний левый угол + размеры)
        //   screen — полный размер экрана в пикселях
        public void Apply(Rect safe, Vector2 screen) {
            if (_rt == null) _rt = GetComponent<RectTransform>();
            if (screen.x <= 0f || screen.y <= 0f) return;

            _rt.anchorMin = new Vector2(safe.x / screen.x, safe.y / screen.y);
            _rt.anchorMax = new Vector2(
                (safe.x + safe.width)  / screen.x,
                (safe.y + safe.height) / screen.y);
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }

        // Обновление по реальным данным экрана (UIRoot.Init и UIRoot.Tick при смене safe-area или размера экрана).
        public void Refresh() {
            Apply(Screen.safeArea, new Vector2(Screen.width, Screen.height));
        }
    }
}
