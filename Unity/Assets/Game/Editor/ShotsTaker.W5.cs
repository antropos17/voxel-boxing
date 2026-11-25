// ShotsTaker.W5.cs — лист портретов меню: 12 бойцов лестницы так, как их рисует PreviewStudio (тело, накладки
// головы и простая воксельная голова). Без Play Mode.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class ShotsTaker
    {
        [MenuItem("VoxGame/Portraits Sheet W5")]
        public static void PortraitsSheet()
        {
            var dir = ShotsDir();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var roster = new RosterDef(42u);
            const int Cols = 4, Rows = 3;
            const int CellW = PreviewStudio.PortraitW + 16, PicH = PreviewStudio.PortraitH, CellH = PicH + 96;
            const int W = Cols * CellW, H = Rows * CellH;

            // свет как в меню (ключевой сверху-спереди), студия — как в игре
            var lightGO = new GameObject("KeyLight");
            lightGO.transform.rotation = Quaternion.LookRotation(new Vector3(0.30f, -0.75f, -0.60f));
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.00f, 0.96f, 0.88f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.None;
            var host = new GameObject("Host");
            var studio = new PreviewStudio(host.transform);

            // кэш студии держит 4 портрета: каждый копируем в свою текстуру листа сразу
            var pics = new RenderTexture[Cols * Rows];
            for (int i = 0; i < pics.Length; i++)
            {
                var src = studio.Portrait(i, roster.Get(i));
                pics[i] = new RenderTexture(PreviewStudio.PortraitW, PreviewStudio.PortraitH, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, pics[i]);
            }
            int cached = studio.PortraitCount, renders = studio.Renders;
            studio.Portrait(11, roster.Get(11));   // из кэша: число отрисовок не растёт
            bool cacheHit = studio.Renders == renders;
            studio.Dispose();

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

            var cgo = new GameObject("Sheet", typeof(RectTransform), typeof(Canvas));
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
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f); prt.pivot = new Vector2(0.5f, 1f);
                prt.sizeDelta = new Vector2(PreviewStudio.PortraitW, PicH);
                prt.anchoredPosition = new Vector2(0f, -4f);

                var name = UIKit.Label(cell, "Name", 26, UIKit.TextMain, true, TextAnchor.MiddleLeft);
                name.text = (i + 1) + ". " + f.name + " «" + f.nickname + "»";
                Bottom(name.rectTransform, 44f, 36f);
                var style = UIKit.Label(cell, "Style", 22, UIKit.Accent, false, TextAnchor.MiddleLeft);
                style.text = StyleOf(f) + " · ур. " + f.level;
                Bottom(style.rectTransform, 6f, 34f);
            }
            foreach (var t in cgo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UiLayer;

            Canvas.ForceUpdateCanvases();
            Shot(cam, null, W, H, Path.Combine(dir, "w5-portraits.png"));
            foreach (var p in pics) { p.Release(); Object.DestroyImmediate(p); }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            LastLog = "GAME_W5_PORTRAITS cached=" + cached + "/" + PreviewStudio.MaxPortraits + " renders=" + renders +
                      " cacheHit=" + cacheHit;
            Debug.Log(LastLog);
        }

        // ------------------------------------------------------------------ Пределы и утечки

        // Не меньше 2000 кадров боя с ударами каждые 20 кадров: максимумы счётчиков против пределов.
        public static void LimitsProbe(int frames = 2100)
        {
            var d = NewFight(out _, out _, 60f, 60f);   // соперник не падает раньше конца замера
            var run = new Run { d = d };
            int mPart = 0, mBan = 0, mNum = 0, mVoice = 0, mSplat = 0, punches = 0, ticks = 0;
            for (int f = 0; f < frames && !d.Finished; f++)
            {
                if (f % 20 == 0) { run.Punch((PunchType)(f / 20 % 3), f % 40 == 0 ? -1 : 1, 1.3f); punches++; }
                run.Tick();
                ticks++;
                mPart = Mathf.Max(mPart, d.Fx.Particles.AliveCount);
                mBan = Mathf.Max(mBan, d.Fx.Banners.ActiveBanners);
                mNum = Mathf.Max(mNum, d.Fx.Banners.ActiveNumbers);
                mVoice = Mathf.Max(mVoice, d.Sound.ActiveVoices);
                mSplat = Mathf.Max(mSplat, d.Fx.Overlay != null ? d.Fx.Overlay.ActiveSplats : 0);
            }
            LastLog = "GAME_LIMITS frames=" + ticks + " punches=" + punches + " headHits=" + d.HeadHits
                      + " particles=" + mPart + "/" + FxParticles.MaxParticles
                      + " banners=" + mBan + "/" + FxBanners.MaxBanners
                      + " numbers=" + mNum + "/" + FxBanners.MaxNumbers
                      + " voices=" + mVoice + "/" + SoundPlayer.MaxVoices
                      + " spectators=" + d.Crowd.Count + "/" + CrowdView.MaxCrowd
                      + " splats=" + mSplat + "/" + FxScreen.MaxSplats;
            EndFight(d);
            Debug.Log(LastLog);
        }

        // 10 циклов «меню → быстрый бой → удары → пауза → в меню» через GameApp: объекты до и после не растут.
        public static void LeakProbe(int cycles = 10)
        {
            string save = Path.Combine(Path.GetTempPath(), "voxgame-leak", "save.json");
            Directory.CreateDirectory(Path.GetDirectoryName(save));
            if (File.Exists(save)) File.Delete(save);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGO.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 40f;
            var app = new GameObject("GameRoot").AddComponent<GameApp>();
            app.Init(cam, PortW, PortH, save);
            string before = "", after = "";
            try
            {
                for (int c = 0; c <= cycles; c++)
                {
                    LeakCycle(app);
                    if (c == 0) before = Census();   // первый цикл — прогрев (кэши, статические текстуры)
                }
                after = Census();
            }
            finally
            {
                Object.DestroyImmediate(app.gameObject);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            LastLog = "GAME_LEAK cycles=" + cycles + " before[" + before + "] after[" + after + "] errors=" + app.ErrorCount;
            Debug.Log(LastLog);
        }

        static void LeakCycle(GameApp app)
        {
            void P(string id) { if (!app.Press(id)) throw new System.Exception("GAME_LEAK press " + id + " в " + app.Flow.State); app.Tick(DT); }
            P("menu.play"); P("mode.quick");
            for (int i = 0; i < 400 && app.Flow.State != AppState.Fight; i++) app.Tick(DT);
            if (app.Flow.State != AppState.Fight) throw new System.Exception("GAME_LEAK бой не начался: " + app.Flow.State);
            for (int i = 0; i < 120; i++)
            {
                if (i % 20 == 0) app.FightInput.Inject(new PunchInput { type = (PunchType)(i / 20 % 3), side = i % 40 == 0 ? -1 : 1, pw = 1.3f, speed = 1.2f });
                app.Tick(DT);
            }
            P("fight.pause"); P("pause.menu"); P("pause.menu");
            for (int i = 0; i < 30; i++) app.Tick(DT);
            if (app.Flow.State != AppState.MainMenu) throw new System.Exception("GAME_LEAK не в меню: " + app.Flow.State);
        }

        static string Census()
        {
            System.GC.Collect();
            return "go=" + Resources.FindObjectsOfTypeAll<GameObject>().Length
                 + " mesh=" + Resources.FindObjectsOfTypeAll<Mesh>().Length
                 + " mat=" + Resources.FindObjectsOfTypeAll<Material>().Length
                 + " tex=" + Resources.FindObjectsOfTypeAll<Texture>().Length;
        }
    }
}
