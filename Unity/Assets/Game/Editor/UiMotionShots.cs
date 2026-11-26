// UiMotionShots.cs — снимки и проверки интерфейса (UI-MOTION.md).
// Screens(tag)  — все экраны в портрете 1080×2400 и ландшафте 2400×1080 → Shots/ui-motion/<tag>-<экран>-p|l.png.
// LayoutAudit() — шесть размеров экрана: кнопки и тексты внутри safe-area, тексты не обрезаны, кнопки ≥ 88 px при 1080,
//                 шкалы боя не заходят на голову соперника и в полосу плашки удара.
// Запуск в открытом редакторе: unity command script eval --code 'VoxGame.Editor.UiMotionShots.Screens("before"); return VoxGame.Editor.UiMotionShots.LastLog;'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class UiMotionShots
    {
        const float DT = 1f / 60f;
        public static string LastLog = "";
        static GameApp _app;
        static StringBuilder _log;
        static string _dir;

        static readonly Vector2Int[] Main = { new Vector2Int(1080, 2400), new Vector2Int(2400, 1080) };
        static readonly Vector2Int[] Sizes = {
            new Vector2Int(1080, 2400), new Vector2Int(1170, 2532), new Vector2Int(720, 1280),
            new Vector2Int(2400, 1080), new Vector2Int(1920, 1080), new Vector2Int(2048, 1536) };

        static string Dir()
        {
            var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "ui-motion"));
            Directory.CreateDirectory(d);
            return d;
        }

        // ------------------------------------------------------------------ Обход экранов

        // Проходит все экраны и на каждом зовёт visit(имя экрана). Бой — настоящий, с синтетическими ударами.
        static void Tour(Action<string> visit)
        {
            NewApp();
            Ticks(40); visit("menu");
            Press("menu.settings"); Ticks(30); visit("settings"); Press("settings.back");
            Press("menu.player"); Ticks(30); visit("player"); Press("player.back");
            Press("menu.shop"); Ticks(30); visit("shop"); Press("shop.back");
            Press("menu.play"); Ticks(30); visit("mode");
            Press("mode.ladder"); Press("roster.select:0"); Ticks(30); visit("roster");
            Press("roster.fight", false); _app.Tick(DT); visit("loading");
            int w = 0;
            while (_app.State != AppState.Fight && w < 900) { _app.Tick(DT); w++; }
            if (_app.State != AppState.Fight) throw new Exception("UI_MOTION_FAIL бой не начался: " + _app.State);
            Ticks(120);
            for (int i = 0; i < 3; i++) { Hit(i % 2 == 0 ? PunchType.Hook : PunchType.Jab, i % 2 == 0 ? -1 : 1); Ticks(40); }
            Ticks(60); visit("fight");
            Press("fight.pause"); Ticks(30); visit("pause"); Press("pause.resume"); Ticks(5);
            _app.Result.Show(true, 83f, 2, 0.64f, 180);
            _app.UI.ShowScreen(ScreenId.Result); Ticks(45); visit("result");
            _app.Reward.Show(FakeReward(), _app.Flow.Profile);
            _app.UI.ShowScreen(ScreenId.Reward); Ticks(75); visit("reward");
        }

        static RewardInfo FakeReward()
        {
            var r = new RewardInfo { won = true, mode = GameMode.Career, coins = 180, xp = 60, levelsGained = 1, newLevel = 2,
                                     ladderAdvanced = true, ladderIndex = 1, nextFoe = "Сергей «Кирпич»" };
            r.lines.Add(new RewardLine("Победа", 120));
            r.lines.Add(new RewardLine("Нокдауны ×2", 40));
            r.lines.Add(new RewardLine("Точность", 20));
            return r;
        }

        public static void Screens(string tag)
        {
            _log = new StringBuilder();
            _dir = Dir();
            try
            {
                Tour(name =>
                {
                    foreach (var s in Main) Snap(s.x, s.y, tag + "-" + name + (s.x > s.y ? "-l" : "-p"));
                });
            }
            finally { Close(); }
        }

        // ------------------------------------------------------------------ Проверка вёрстки

        public static int LayoutProblems;
        static string _headInfo = "";

        public static void LayoutAudit()
        {
            _log = new StringBuilder();
            LayoutProblems = 0;
            try
            {
                Tour(name => { foreach (var s in Sizes) Audit(name, s.x, s.y); });
            }
            finally { Close(); }
            _log.AppendLine("UI_LAYOUT problems=" + LayoutProblems);
            LastLog = _log.ToString().TrimEnd();
        }

        static void Audit(string screen, int w, int h)
        {
            _app.SetViewSize(w, h);
            var ui = _app.UI;
            ui.PrepareForShot(w, h);
            ui.FitToCamera(_app.Cam, w, h);
            Canvas.ForceUpdateCanvases();
            var root = ui.GetComponent<RectTransform>();
            var safeRt = (RectTransform)root.Find("SafeArea");
            var c = new Vector3[4];
            Rect safe = PxRect(root, safeRt, c, w, h);
            float minBtn = 88f * Mathf.Min(w, h) / 1080f;
            var sb = new StringBuilder();
            int buttons = 0, texts = 0;
            foreach (var b in root.GetComponentsInChildren<Button>(false))
            {
                if (!b.isActiveAndEnabled) continue;
                var r = PxRect(root, (RectTransform)b.transform, c, w, h);
                var mask = b.GetComponentInParent<RectMask2D>();
                if (mask != null)
                {
                    var m = PxRect(root, mask.rectTransform, c, w, h);
                    if (!r.Overlaps(m)) continue;
                    if (b.GetComponentInParent<ScrollRect>() != null && (r.yMin < m.yMin - 1f || r.yMax > m.yMax + 1f)) continue;   // частично прокручена
                }
                buttons++;
                string label = Label(b);
                if (Mathf.Min(r.width, r.height) < minBtn - 0.5f) sb.Append(" small[" + label + " " + Mathf.RoundToInt(r.width) + "x" + Mathf.RoundToInt(r.height) + "<" + Mathf.RoundToInt(minBtn) + "]");
                if (!Inside(r, safe, 1.5f)) sb.Append(" unsafe[" + label + "]");
            }
            foreach (var t in root.GetComponentsInChildren<Text>(false))
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.color.a < 0.05f) continue;
                if (t.GetComponentInParent<CanvasGroup>() is CanvasGroup g && g.alpha < 0.05f) continue;
                texts++;
                var rr = t.rectTransform.rect;
                bool wrap = t.horizontalOverflow == HorizontalWrapMode.Wrap;
                if (t.verticalOverflow == VerticalWrapMode.Truncate && t.preferredHeight > rr.height + 1f)
                    sb.Append(" cutV[" + Short(t.text) + "]");
                else if (!wrap && t.preferredWidth > rr.width + 2f && t.GetComponentInParent<Button>() != null)
                    sb.Append(" wide[" + Short(t.text) + "]");
                if (wrap && t.preferredHeight > rr.height * 1.6f + 4f && t.GetComponentInParent<Button>() != null)
                    sb.Append(" wrapV[" + Short(t.text) + "]");
                var tr = PxRect(root, t.rectTransform, c, w, h);
                // видимая часть надписи: ширина строки по выравниванию, высота — по тексту
                float k = tr.width / Mathf.Max(1f, rr.width);
                float pw = Mathf.Min(rr.width, t.preferredWidth) * k, ph = Mathf.Min(rr.height, t.preferredHeight) * k;
                float cx = tr.center.x, cy = tr.center.y;
                int ax = (int)t.alignment % 3, ay = (int)t.alignment / 3;
                float x0 = ax == 0 ? tr.xMin : ax == 2 ? tr.xMax - pw : cx - pw * 0.5f;
                float y0 = ay == 0 ? tr.yMin : ay == 2 ? tr.yMax - ph : cy - ph * 0.5f;
                var vis = new Rect(x0, y0, pw, ph);
                if (t.GetComponentInParent<RectMask2D>() is RectMask2D tm && !vis.Overlaps(PxRect(root, tm.rectTransform, c, w, h))) continue;
                if (!Inside(vis, safe, 2f)) sb.Append(" unsafeT[" + Short(t.text) + "]");
            }
            if (screen == "fight" && _app.Fight != null) AuditHud(sb, root, c, w, h);
            string issues = sb.ToString();
            if (issues.Length > 0) LayoutProblems++;
            _log.AppendLine("UI_LAYOUT " + screen + " " + w + "x" + h + " buttons=" + buttons + " texts=" + texts + (issues.Length > 0 ? issues : " ok") + _headInfo);
            _headInfo = "";
            ui.RestoreAfterShot();
        }

        // Шкалы боя: не на голове соперника и выше полосы плашки удара (FxPops.PopTop*).
        static void AuditHud(StringBuilder sb, RectTransform root, Vector3[] c, int w, int h)
        {
            Rect head = HeadRectPx(w, h);
            _headInfo = " face=" + Mathf.RoundToInt(head.xMin) + ".." + Mathf.RoundToInt(head.xMax) + "x" + Mathf.RoundToInt(head.yMin) + ".." + Mathf.RoundToInt(head.yMax);
            bool land = w > h;
            float popTop = (1f - (land ? FxPops.PopTopL : FxPops.PopTopP)) * h;   // y вниз
            float popSide = land ? FxPops.PopSideL * w : 0f;
            foreach (var name in new[] { "FoeHp", "PlrHp", "Stam", "Mega" })
            {
                var t = FindDeep(_app.Fight.Hud.transform, name);
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                var r = PxRect(root, (RectTransform)t, c, w, h);
                if (head.width > 0f && r.Overlaps(head)) sb.Append(" onFace[" + name + "]");
                bool inPopBand = r.yMax > popTop + 1f && r.xMax > popSide && r.xMin < w - popSide;
                if (inPopBand) sb.Append(" onPlate[" + name + "]");
            }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        // Голова соперника на снимке (пиксели, y вниз): углы общих границ её сеток.
        static Rect HeadRectPx(int w, int h)
        {
            var v = _app.Fight.Head;
            if (v == null) return Rect.zero;
            bool any = false; var bb = new Bounds();
            foreach (var r in v.root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r.transform.parent == null || r.transform.parent.name != "head") continue;   // только чанки головы, не глаза и не капли
                // границы чанка — весь чанк, а не воксели: короб считаем по вершинам сетки
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var vs = mf.sharedMesh.vertices; var m = r.transform.localToWorldMatrix;
                for (int k = 0; k < vs.Length; k += 3)
                {
                    var p = m.MultiplyPoint3x4(vs[k]);
                    if (!any) { bb = new Bounds(p, Vector3.zero); any = true; } else bb.Encapsulate(p);
                }
            }
            if (!any) return Rect.zero;
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            var cam = _app.Cam;
            float aspect = cam.aspect; cam.aspect = (float)w / h;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = bb.center + Vector3.Scale(bb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 vp = cam.WorldToViewportPoint(p);
                x0 = Mathf.Min(x0, vp.x); x1 = Mathf.Max(x1, vp.x); y0 = Mathf.Min(y0, vp.y); y1 = Mathf.Max(y1, vp.y);
            }
            cam.ResetAspect();
            // лицо — средние 70 % ширины и верхние 85 % высоты общего короба (уши и шея — не лицо)
            float cx = (x0 + x1) * 0.5f, hw = (x1 - x0) * 0.35f;
            return Rect.MinMaxRect((cx - hw) * w, (1f - y1) * h, (cx + hw) * w, (1f - (y0 + (y1 - y0) * 0.15f)) * h);
        }

        // ------------------------------------------------------------------ Помощники

        static void NewApp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.035f, 1f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            var camData = camGO.AddComponent<UniversalAdditionalCameraData>();
            camData.renderShadows = false;
            camData.antialiasing = AntialiasingMode.None;
            string saveDir = Path.Combine(Path.GetTempPath(), "voxgame-uimotion");
            Directory.CreateDirectory(saveDir);
            string savePath = Path.Combine(saveDir, "save.json");
            if (File.Exists(savePath)) File.Delete(savePath);
            var go = new GameObject("GameRoot");
            _app = go.AddComponent<GameApp>();
            _app.Init(cam, 1080, 2400, savePath);
        }

        static void Close()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            _app = null;
            LastLog = _log.ToString().TrimEnd();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void Ticks(int n) { for (int i = 0; i < n; i++) _app.Tick(DT); }

        static void Press(string id, bool tick = true)
        {
            if (!_app.Press(id)) throw new Exception("UI_MOTION_FAIL press " + id + " в состоянии " + _app.State);
            if (tick) _app.Tick(DT);
        }

        static void Hit(PunchType t, int side, float pw = 1.3f) =>
            _app.FightInput.Inject(new PunchInput { type = t, side = side, pw = pw, speed = 1.2f });

        static void Snap(int w, int h, string name)
        {
            _app.SetViewSize(w, h);
            var ui = _app.UI;
            ui.PrepareForShot(w, h);
            ui.FitToCamera(_app.Cam, w, h);
            Canvas.ForceUpdateCanvases();
            Render(_app.Cam, ui, w, h, Path.Combine(_dir, name + ".png"));
            _log.AppendLine("UI_SHOT " + name);
        }

        // Как ShotsTaker.Shot, но текстура кадра уничтожается сразу: серия больших снимков (2400 px) не копит память видеокарты.
        static void Render(Camera cam, UIRoot ui, int w, int h, string path)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt;
                RenderTexture.active = rt;
                cam.Render();
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(tex);
                ui.RestoreAfterShot();
            }
        }

        static Rect PxRect(RectTransform root, RectTransform rt, Vector3[] c, int w, int h)
        {
            rt.GetWorldCorners(c);
            Vector2 a = root.InverseTransformPoint(c[0]), b = root.InverseTransformPoint(c[2]);
            float x0 = Mathf.Min(a.x, b.x) + w * 0.5f, x1 = Mathf.Max(a.x, b.x) + w * 0.5f;
            float y0 = h * 0.5f - Mathf.Max(a.y, b.y), y1 = h * 0.5f - Mathf.Min(a.y, b.y);
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        static bool Inside(Rect a, Rect o, float eps) =>
            a.xMin >= o.xMin - eps && a.yMin >= o.yMin - eps && a.xMax <= o.xMax + eps && a.yMax <= o.yMax + eps;

        static string Label(Button b)
        {
            var t = b.GetComponentInChildren<Text>();
            return t != null && !string.IsNullOrEmpty(t.text) ? Short(t.text) : b.name;
        }

        static string Short(string s)
        {
            s = s.Replace('\n', ' ');
            return s.Length > 20 ? s.Substring(0, 20) + "…" : s;
        }
    }
}
