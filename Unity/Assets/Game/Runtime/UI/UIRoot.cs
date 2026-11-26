// UIRoot.cs — корень UI: Canvas, CanvasScaler, EventSystem, стек экранов.
// Init(cam) вызывает GameSetup или FightDirector — ни логики в Awake/Start.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VoxGame {

    public enum ScreenId {
        MainMenu, ModeSelect, FighterSelect,
        Fight, Pause, Result,
        Shop, Player, Settings,
        Loading, Reward
    }

    // Интерфейс для экранов, которым нужно знать об изменении ориентации.
    public interface ILayoutHandler {
        void OnLayout(bool landscape);
    }

    // Единственный хозяин Canvas. Создаётся через Init, не через Awake.
    public class UIRoot : MonoBehaviour {

        Canvas        _canvas;
        CanvasScaler  _scaler;
        SafeAreaFitter _safeArea;
        RectTransform  _safeLayer;

        readonly Dictionary<ScreenId, GameObject> _screens = new();
        ScreenId _active;

        // Сохранение перед снимком
        RenderMode _savedMode;
        Camera     _savedCam;
        Vector2    _savedRes;
        float      _savedMatch;
        bool       _inShotMode;

        bool _prevLandscape;
        int  _viewW, _viewH;   // размер окна вывода, если он не равен Screen (снимки в текстуру); 0 — Screen

        bool Landscape => _viewW > 0 ? _viewW > _viewH : Screen.width > Screen.height;

        public ScreenId ActiveScreen => _active;
        public bool IsLandscape => Landscape;

        // ------------------------------------------------------------------ Инициализация

        // Вызывается создателем после AddComponent.
        public void Init(Camera renderCam) {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera  = renderCam;
            // холст у самой ближней плоскости: руки игрока (от 0.2 м) не перекрывают кнопки
            _canvas.planeDistance = renderCam.nearClipPlane + 0.03f;
            _canvas.sortingOrder  = 0;

            _scaler = gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution  = new Vector2(540f, 1170f);
            _scaler.matchWidthOrHeight   = 0.5f;

            var raycaster = gameObject.AddComponent<GraphicRaycaster>();
            _ = raycaster;

            // Промежуточный контейнер — ограничивается SafeAreaFitter
            var saGO = new GameObject("SafeArea", typeof(RectTransform));
            saGO.transform.SetParent(transform, false);
            _safeLayer = saGO.GetComponent<RectTransform>();
            UIKit.SetAnchors(_safeLayer, 0f, 0f, 1f, 1f);
            _safeLayer.offsetMin = Vector2.zero;
            _safeLayer.offsetMax = Vector2.zero;

            _safeArea = saGO.AddComponent<SafeAreaFitter>();
            _safeArea.Refresh();
            RememberScreen();

            EnsureEventSystem();

            _prevLandscape = Landscape;
        }

        // Размер окна вывода вместо Screen (0 — вернуться к Screen): по нему выбирается раскладка экранов.
        public void SetViewSize(int w, int h) {
            _viewW = w; _viewH = h;
            bool landscape = Landscape;
            if (landscape == _prevLandscape) return;
            _prevLandscape = landscape;
            BroadcastLayout(landscape);
        }

        static void EnsureEventSystem() {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }

        // ------------------------------------------------------------------ Экраны

        // Зарегистрировать экран как дочерний safe-area слоя.
        // Вызывается каждым экраном из Init(UIRoot).
        public void Register(ScreenId id, GameObject screenGO) {
            screenGO.transform.SetParent(_safeLayer, false);
            var rt = screenGO.GetComponent<RectTransform>();
            if (rt != null) {
                UIKit.SetAnchors(rt, 0f, 0f, 1f, 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            _screens[id] = screenGO;
            // экран уже собран: разложить под текущую ориентацию (иначе в ландшафте он остался бы портретным до первого поворота)
            foreach (var h in screenGO.GetComponentsInChildren<ILayoutHandler>(true))
                h.OnLayout(_prevLandscape);
            screenGO.SetActive(false);
        }

        // Показать один экран, остальные скрыть.
        // Уходящий экран гаснет за UIMotion.ScreenOut и лучей уже не ловит; новый появляется поверх (UIMotion.ScreenEnter),
        // его кнопки нажимаются сразу. Бой (HUD) появляется своим движением — шкалы выезжают в FightHUD.
        public void ShowScreen(ScreenId id) {
            foreach (var kv in _screens) {
                var go = kv.Value;
                if (kv.Key == id) {
                    var g = go.GetComponent<CanvasGroup>();
                    bool settled = go.activeSelf && (g == null || g.blocksRaycasts) && _active == id;
                    if (settled) continue;   // тот же экран ещё раз — без повторного появления
                    go.SetActive(true);
                    go.transform.SetAsLastSibling();
                    if (id == ScreenId.Fight) { var fg = UIMotion.Group(go); UIMotion.Complete(fg); fg.alpha = 1f; fg.blocksRaycasts = true; }
                    else UIMotion.ScreenEnter(go);
                } else if (go.activeSelf) UIMotion.ScreenExit(go);
            }
            _active = id;
        }

        public void HideAll() {
            foreach (var kv in _screens) {
                UIMotion.Complete(kv.Value.GetComponent<CanvasGroup>());
                kv.Value.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ Tick

        // Вызывается из GameApp.Tick (в старых сценах снимков — из FightDirector.Tick).
        // dt — игровое время, udt — реальное.
        public void Tick(float dt, float udt) {
            UIMotion.Tick(udt);
            // safe-area меняется и без смены ориентации: поворот на 180°, панель навигации, разделённый экран
            if (_viewW == 0 && !_inShotMode && ScreenChanged()) { RememberScreen(); _safeArea.Refresh(); }
            bool landscape = Landscape;
            if (landscape != _prevLandscape) {
                _prevLandscape = landscape;
                BroadcastLayout(landscape);
            }
        }

        Rect _lastSafe;
        int  _lastW, _lastH;
        bool ScreenChanged() => Screen.safeArea != _lastSafe || Screen.width != _lastW || Screen.height != _lastH;
        void RememberScreen() { _lastSafe = Screen.safeArea; _lastW = Screen.width; _lastH = Screen.height; }

        void BroadcastLayout(bool landscape) {
            UIMotion.CompleteAll();   // раскладка пишет якоря заново: движение доводится до конца, чтобы не спорить с ней
            foreach (var kv in _screens) {
                var handlers = kv.Value.GetComponentsInChildren<ILayoutHandler>(true);
                foreach (var h in handlers)
                    h.OnLayout(landscape);
            }
        }

        // ------------------------------------------------------------------ Снимки

        // Переключить Canvas в World Space, растянуть перед камерой по frustum.
        // Используется в пакетном режиме, если Screen Space Camera не работает в RenderTexture.
        public void FitToCamera(Camera cam, int w, int h) {
            _canvas.renderMode = RenderMode.WorldSpace;
            float dist  = cam.nearClipPlane + 0.03f;
            float camH  = 2f * dist * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float camW  = camH * ((float)w / h);
            var rt      = _canvas.GetComponent<RectTransform>();
            rt.sizeDelta    = new Vector2(w, h);
            rt.localScale   = Vector3.one * (camW / w);
            rt.position     = cam.transform.position + cam.transform.forward * dist;
            rt.rotation     = cam.transform.rotation;
            _inShotMode = true;
        }

        public void PrepareForShot(int w, int h) {
            _savedMode  = _canvas.renderMode;
            _savedCam   = _canvas.worldCamera;
            _savedRes   = _scaler.referenceResolution;
            _savedMatch = _scaler.matchWidthOrHeight;

            SetViewSize(w, h);
            bool landscape = w > h;
            _scaler.referenceResolution = landscape
                ? new Vector2(1170f, 540f)
                : new Vector2(540f, 1170f);
            _scaler.matchWidthOrHeight = 0.5f;

            // Искусственная safe-area: портрет — 47 сверху, 34 снизу; ландшафт — 47 по бокам, 21 снизу.
            Rect safe;
            if (!landscape)
                safe = new Rect(0f, 34f, w, h - 47f - 34f);
            else
                safe = new Rect(47f, 21f, w - 94f, h - 21f);
            _safeArea.Apply(safe, new Vector2(w, h));

            Canvas.ForceUpdateCanvases();
        }

        public void RestoreAfterShot() {
            if (_inShotMode) {
                _canvas.renderMode  = _savedMode;
                _canvas.worldCamera = _savedCam;
                _inShotMode = false;
            }
            _scaler.referenceResolution = _savedRes;
            _scaler.matchWidthOrHeight  = _savedMatch;
            _safeArea.Refresh();
        }
    }
}
