// ShotsTaker.W2.cs — снимки второй вехи: рефери и развязка, лист бойцов лестницы, экран лестницы.
// Всё без Play Mode: настоящий FightDirector, удары только через InputReader.Inject; портреты бойцов — FoeBody
// с одной общей анатомической головой (голова строится долго и у всех одинаковая).
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using VoxProbe;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class ShotsTaker
    {
        // Строки отчёта последнего сценария (для script eval в открытом редакторе: Debug.Log туда не возвращается)
        public static string LastLog = "";

        // Диагностика постановки рефери: виды сбоку и сверху в Unity/Logs/diag (не в снимки)
        public static bool RefDiag;

        static void DiagRef(FightDirector d, Camera cam, string tag)
        {
            if (!RefDiag) return;
            var ddir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "diag"));
            Directory.CreateDirectory(ddir);
            var go = new GameObject("DiagCam");
            var dc = go.AddComponent<Camera>();
            dc.clearFlags = CameraClearFlags.SolidColor; dc.backgroundColor = new Color(0.25f, 0.25f, 0.3f, 1f);
            dc.nearClipPlane = 0.05f; dc.farClipPlane = 60f; dc.fieldOfView = 45f;
            Vector3 mid = d.RefereeFigure.Root.position + new Vector3(0f, 1.0f, 0f);
            void V(string n, Vector3 off)
            {
                var pos = mid + off;
                dc.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(mid - pos, Vector3.up));
                Shot(dc, null, 700, 700, Path.Combine(ddir, tag + "-" + n + ".png"));
            }
            V("side", new Vector3(-3.5f, 0.3f, 0.4f));
            V("front", new Vector3(0f, 0.3f, 3.5f));
            d.RefereeFigure.Root.gameObject.SetActive(false);
            Shot(cam, null, PortW, PortH, Path.Combine(ddir, tag + "-noref.png"));
            d.RefereeFigure.Root.gameObject.SetActive(true);
            Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------------ Рефери и развязка

        [MenuItem("VoxGame/Referee Shots W2")]
        public static void RefereeShots()
        {
            var dir = ShotsDir();
            // ослабленный соперник с поясом после победы: нокдаун и нокаут за разумное число ударов
            var d = NewFight(out var cam, out var ui, 0.45f, 0.5f, 0, forceBelt: true);
            var run = new Run { d = d };
            var log = new StringBuilder();

            run.Ticks(90);
            Pair(d, cam, ui, dir, "w2-ref-idle");
            log.AppendLine("W2_REF_IDLE ref=" + d.Referee.State + " crowd=" + d.Crowd.Count + " mood=" + d.Crowd.Mood);

            // ---- нокдаун: бить, пока соперник не упадёт; снимок на счёте 2, рука рефери вверху перед взмахом
            var combo = new[] { PunchType.Hook, PunchType.Upper, PunchType.Jab, PunchType.Hook };
            int punches = 0;
            while (d.State.knockdowns == 0 && !d.Finished && punches < 150)
            {
                run.WaitOpen(120);
                run.Punch(combo[punches % combo.Length], punches % 2 == 0 ? -1 : 1);
                punches++;
                run.Ticks(18);
            }
            {
                int w = 0;
                while (w < 600 && !d.Finished
                       && !(d.Referee.State == RefereeState.Counting && d.Referee.Count >= 2 && d.Referee.Phase >= 0.64f))
                { run.Tick(); w++; }
                log.AppendLine("W2_REF_COUNT punches=" + punches + " kd=" + d.State.knockdowns + " count=" + d.Referee.Count
                               + " phase=" + F(d.Referee.Phase) + " ref=" + d.Referee.State + " mood=" + d.Crowd.Mood);
                Pair(d, cam, ui, dir, "w2-ref-count");
                DiagRef(d, cam, "count");
            }

            // ---- нокаут: соперник встаёт, добить
            punches = 0;
            while (!d.Finished && punches < 300)
            {
                run.WaitOpen(200);
                if (d.State.phase == FightPhase.Down || d.State.phase == FightPhase.Rise) { run.Tick(); continue; }
                run.Punch(combo[punches % combo.Length], punches % 2 == 0 ? 1 : -1, 1.35f);
                punches++;
                run.Ticks(18);
            }
            {
                int w = 0;
                // скрещенные над головой руки: второй мах (фаза ~0.75), к нему кончается слоу-мо нокаута
                while (w < 600 && !(d.Referee.State == RefereeState.StopFight && d.Referee.Phase >= 0.74f)) { run.Tick(); w++; }
                log.AppendLine("W2_REF_STOP finished=" + d.Finished + " won=" + d.PlayerWon + " punches=" + punches
                               + " ref=" + d.Referee.State + " phase=" + F(d.Referee.Phase) + " victory=" + d.Victory.State);
                Pair(d, cam, ui, dir, "w2-ref-stop");
                DiagRef(d, cam, "stop");
            }

            // ---- рука победителя поднята, зал ликует
            {
                int w = 0;
                while (w < 900 && !(d.Victory.State == VictoryState.Cheer && d.Victory.Progress >= 0.6f)) { run.Tick(); w++; }
                var g = d.RefereeFigure.GripPoint();
                log.AppendLine("W2_REF_RAISE victory=" + d.Victory.State + " ref=" + d.Referee.State + " gripping=" + d.RefereeFigure.IsGripping
                               + " grip(cam)=" + cam.transform.InverseTransformPoint(g).ToString("F3") + " mood=" + d.Crowd.Mood);
                Pair(d, cam, ui, dir, "w2-ref-raise");
                Pair(d, cam, ui, dir, "w3-victory-raise");
                DiagRef(d, cam, "raise");
            }

            // ---- пояс
            {
                int w = 0;
                while (w < 900 && !(d.Victory.State == VictoryState.Belt && d.Belt.Settled)) { run.Tick(); w++; }
                run.Ticks(20);
                log.AppendLine("W2_BELT victory=" + d.Victory.State + " shows=" + d.Victory.ShowsBelt + " settled=" + d.Belt.Settled);
                Pair(d, cam, ui, dir, "w2-belt");
                Pair(d, cam, ui, dir, "w3-victory-belt");
            }

            // ---- экран результата после развязки
            {
                int w = 0;
                while (w < 900 && !d.Victory.IsDone) { run.Tick(); w++; }
                run.Ticks(10);
                log.AppendLine("W2_RESULT done=" + d.Victory.IsDone + " screen=" + ui.ActiveScreen + " elapsed=" + F(d.Victory.Elapsed));
                Pair(d, cam, ui, dir, "w2-result");
            }

            // ---- реванш возвращает всё в исходное
            d.Restart();
            run.Ticks(30);
            log.AppendLine("W2_RESTART victory=" + d.Victory.State + " ref=" + d.Referee.State + " mood=" + d.Crowd.Mood
                           + " belt=" + d.Belt.Visible + " screen=" + ui.ActiveScreen + " finished=" + d.Finished);

            EndFight(d);
            LastLog = log.ToString().TrimEnd();
            Debug.Log(LastLog);
            Debug.Log("GAME_W2_REF_OK " + dir);
        }

        // ------------------------------------------------------------------ Портреты бойцов

        // Поясной портрет бойца в стойке: тело FoeBody, общая анатомическая голова, накладки HeadGear, тёмный фон.
        sealed class PortraitRig
        {
            public readonly Camera cam;
            readonly Transform _root;
            readonly Material _anatMat, _voxMat;
            readonly AnatView _v;
            FoeBody _body;

            public PortraitRig(Vector3 at)
            {
                _root = new GameObject("PortraitRig").transform;
                _root.position = at;
                _anatMat = new Material(Shader.Find("VoxProbe/Anatomy"));
                _voxMat = new Material(Shader.Find("VoxGame/VertexColor"));

                var lightGO = new GameObject("PortraitLight");
                lightGO.transform.SetParent(_root, false);
                lightGO.transform.rotation = Quaternion.LookRotation(new Vector3(0.30f, -0.75f, -0.60f));
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1.00f, 0.96f, 0.88f);
                light.intensity = 1.15f;
                light.shadows = LightShadows.None;

                var camGO = new GameObject("PortraitCam");
                camGO.transform.SetParent(_root, false);
                cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.115f, 0.115f, 0.135f, 1f);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 10f;
                cam.fieldOfView = 26f;
                cam.enabled = false;
                var cd = camGO.AddComponent<UniversalAdditionalCameraData>();
                cd.renderShadows = false;
                cd.antialiasing = AntialiasingMode.None;

                AnatSkin.on = true;
                AnatSkin.Reset();
                _v = new AnatView(_root, at + Vector3.up * 1.6f, _anatMat);
            }

            public void SetFighter(in FighterDef def)
            {
                _body?.Dispose();
                AnatSkin.Reset();
                _body = new FoeBody(_root, def, _voxMat);
                var cmd = new AICommand { newPhase = FightPhase.Idle };
                for (int i = 0; i < 45; i++)
                {
                    _body.Tick(cmd, DT, Vector3.zero);
                    _v.root.SetPositionAndRotation(_body.headMount.position, _body.headMount.rotation);
                    AnatSkin.Step(DT);
                    _v.Tick(DT);
                    AnatSkin.SetGlobals(_v.root);
                    _body.gear.SyncToSkull();
                }
                // по росту бойца, чуть сбоку и сверху: от пояса до макушки
                Vector3 look = _body.headMount.position + Vector3.down * 0.18f;
                Vector3 eye = look + new Vector3(-0.32f, 0.12f, 2.12f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            }

            public void Render(RenderTexture rt)
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
            }

            public void Dispose()
            {
                _body?.Dispose();
                _v.Dispose();
                AnatSkin.Reset();
                Object.DestroyImmediate(_anatMat);
                Object.DestroyImmediate(_voxMat);
                Object.DestroyImmediate(_root.gameObject);
            }
        }

        // Стиль из визитки «"прозвище" · стиль · HP N · Ур.L»
        static string StyleOf(in FighterDef f)
        {
            if (string.IsNullOrEmpty(f.cardText)) return "";
            var parts = f.cardText.Split('·');
            return parts.Length > 1 ? parts[1].Trim() : "";
        }

        // ------------------------------------------------------------------ Лист бойцов и кадры начала боя

        [MenuItem("VoxGame/Roster Sheet W2")]
        public static void RosterSheet()
        {
            var dir = ShotsDir();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var roster = new RosterDef(42u);
            const int Cols = 4, Rows = 3, CellW = 400, CellH = 500, PicH = 410;
            const int W = Cols * CellW, H = Rows * CellH;

            // портреты 12 бойцов — каждый в свою текстуру
            var rig = new PortraitRig(Vector3.zero);
            var pics = new RenderTexture[Cols * Rows];
            for (int i = 0; i < pics.Length; i++)
            {
                rig.SetFighter(roster.Get(i));
                pics[i] = new RenderTexture(CellW, PicH, 24, RenderTextureFormat.ARGB32);
                rig.Render(pics[i]);
            }
            rig.Dispose();

            // лист: холст в мире перед ортографической камерой, ячейки — портрет и подпись
            const int UiLayer = 5;
            var at = new Vector3(0f, 100f, 0f);
            var camGO = new GameObject("SheetCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.06f, 0.075f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = H * 0.001f * 0.5f;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 5f;
            cam.cullingMask = 1 << UiLayer;
            cam.transform.SetPositionAndRotation(at - Vector3.forward, Quaternion.identity);
            var cd = camGO.AddComponent<UniversalAdditionalCameraData>();
            cd.renderShadows = false;

            var cgo = new GameObject("Sheet", typeof(RectTransform), typeof(Canvas));
            cgo.layer = UiLayer;
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var crt = cgo.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(W, H);
            crt.localScale = Vector3.one * 0.001f;
            crt.position = at;

            for (int i = 0; i < pics.Length; i++)
            {
                var f = roster.Get(i);
                int cx = i % Cols, cy = i / Cols;
                var cell = UIKit.Panel(cgo.transform, "Cell" + i, new Color(0.10f, 0.10f, 0.125f, 1f));
                cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f);
                cell.pivot = new Vector2(0f, 1f);
                cell.sizeDelta = new Vector2(CellW - 8, CellH - 8);
                cell.anchoredPosition = new Vector2(cx * CellW + 4, -(cy * CellH + 4));

                var pic = new GameObject("Pic", typeof(RectTransform), typeof(RawImage));
                pic.transform.SetParent(cell, false);
                pic.GetComponent<RawImage>().texture = pics[i];
                var prt = pic.GetComponent<RectTransform>();
                prt.anchorMin = new Vector2(0f, 1f); prt.anchorMax = new Vector2(1f, 1f); prt.pivot = new Vector2(0.5f, 1f);
                prt.sizeDelta = new Vector2(0f, PicH - 8);
                prt.anchoredPosition = Vector2.zero;

                var name = UIKit.Label(cell, "Name", 30, UIKit.TextMain, true, TextAnchor.MiddleLeft);
                name.text = (i + 1) + ". " + f.name + " «" + f.nickname + "»";
                Bottom(name.rectTransform, 46f, 40f);
                var style = UIKit.Label(cell, "Style", 24, UIKit.Accent, false, TextAnchor.MiddleLeft);
                style.text = StyleOf(f) + " · ур. " + f.level;
                Bottom(style.rectTransform, 6f, 38f);
            }
            foreach (var t in cgo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UiLayer;

            Canvas.ForceUpdateCanvases();
            Shot(cam, null, W, H, Path.Combine(dir, "w2-roster-sheet.png"));
            foreach (var p in pics) { p.Release(); Object.DestroyImmediate(p); }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // кадры начала боя от первого лица с HUD для первых восьми
            for (int i = 0; i < 8; i++)
            {
                var d = NewFight(out var fcam, out var fui, 1f, 1f, i);
                for (int k = 0; k < 90; k++) d.Tick(DT);
                d.SetViewSize(PortW, PortH);
                Shot(fcam, fui, PortW, PortH, Path.Combine(dir, "w2-foe-" + (i + 1).ToString("00") + "-p.png"));
                EndFight(d);
            }
            Debug.Log("GAME_W2_SHEET_OK " + dir);
        }

        // Полоса у нижнего края ячейки: bottom — отступ снизу, h — высота (опорные px)
        static void Bottom(RectTransform rt, float bottom, float h)
        {
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(14f, bottom);
            rt.offsetMax = new Vector2(-10f, bottom + h);
        }

        // ------------------------------------------------------------------ Экран лестницы

        [MenuItem("VoxGame/Roster Screen Shots W2")]
        public static void RosterScreenShots()
        {
            var dir = ShotsDir();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var roster = new RosterDef(42u);
            var profile = new PlayerProfile { ladderIndex = 2 };   // двое побеждены, третий — следующий

            // портрет выбранного бойца
            var rig = new PortraitRig(new Vector3(0f, -50f, 0f));
            rig.SetFighter(roster.Get(profile.ladderIndex));
            var portrait = new RenderTexture(420, 520, 24, RenderTextureFormat.ARGB32);
            rig.Render(portrait);
            rig.Dispose();

            var camGO = new GameObject("UiCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 5f;
            cam.transform.position = new Vector3(0f, 200f, 0f);
            var cd = camGO.AddComponent<UniversalAdditionalCameraData>();
            cd.renderShadows = false;

            var uiGO = new GameObject("UIRoot");
            var ui = uiGO.AddComponent<UIRoot>();
            ui.Init(cam);
            var rsGO = new GameObject("RosterScreen");
            var rs = rsGO.AddComponent<RosterScreen>();
            int fought = -1;
            rs.Init(ui, i => fought = i, null);
            rs.SetData(roster, profile);
            rs.Select(profile.ladderIndex);
            rs.SetPortrait(portrait);
            ui.ShowScreen(ScreenId.FighterSelect);

            void Take(int w, int h, string tag)
            {
                cam.fieldOfView = w > h ? 50f : GameUtil.CalcFov(w, h);
                ui.SetViewSize(w, h);
                ui.PrepareForShot(w, h);
                ui.FitToCamera(cam, w, h);
                Canvas.ForceUpdateCanvases();
                rs.Tick(DT, DT);   // прокрутка к выбранному и обрезка портрета — после раскладки
                Canvas.ForceUpdateCanvases();
                rs.Tick(DT, DT);
                ui.RestoreAfterShot();
                Shot(cam, ui, w, h, Path.Combine(dir, tag));
            }
            Take(PortW, PortH, "w2-roster-p.png");
            Take(LandW, LandH, "w2-roster-l.png");

            rs.PressFight();
            LastLog = "W2_ROSTER selected=" + rs.Selected + " state=" + rs.StateOf(rs.Selected) + " fight=" + fought
                      + " avail0=" + rs.IsAvailable(0) + " avail3=" + rs.IsAvailable(3);
            Debug.Log(LastLog);
            portrait.Release();
            Object.DestroyImmediate(portrait);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("GAME_W2_ROSTER_OK " + dir);
        }
    }
}
