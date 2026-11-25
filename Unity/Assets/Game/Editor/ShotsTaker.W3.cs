// ShotsTaker.W3.cs — снимки третьей вехи: эффекты удара, пропущенный удар, добивание, развязка с поясом
// и поражением, лист перчаток; замер аллокаций в кадре боя.
// Всё без Play Mode: настоящий FightDirector, удары только через InputReader.Inject, защита — кнопками HUD.
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class ShotsTaker
    {
        // Удар и ожидание кадра попадания по голове; false — удар не дошёл (промах или соперник лёг).
        static bool PunchAndWaitHit(Run run, PunchType t, int side, float pw, int maxTicks = 20)
        {
            int seq = run.d.hitFrameSeq;
            run.Punch(t, side, pw);
            for (int i = 0; i < maxTicks && run.d.hitFrameSeq == seq; i++) run.Tick();
            return run.d.hitFrameSeq != seq;
        }

        [MenuItem("VoxGame/Fx Shots W3")]
        public static void FxShots()
        {
            var dir = ShotsDir();
            var log = new StringBuilder();

            // ---- эффекты удара: крепкий соперник, чтобы серия не валила его
            {
                var d = NewFight(out var cam, out var ui, 4f, 3f);
                var run = new Run { d = d };
                run.Ticks(90);

                // джеб: скромные искры и брызги, небольшое число урона
                run.WaitOpen(150);
                bool ok = PunchAndWaitHit(run, PunchType.Jab, -1, 1.1f);
                run.Ticks(6);   // перчатка уже идёт назад: искры и брызги видны у лица
                log.AppendLine("W3_FX_JAB hit=" + ok + " banner=" + d.Fx.Banners.LastBannerText + " parts=" + d.Fx.Particles.AliveCount
                               + " timeScale=" + F(d.Fx.TimeScale) + " fovKick=" + F(d.Fx.FovKick));
                Pair(d, cam, ui, dir, "w3-fx-jab");
                run.Ticks(60);

                // силовой хук: снимок в стоп-кадре — вспышка, брызги, крупное слово, наезд
                run.WaitOpen(150);
                ok = PunchAndWaitHit(run, PunchType.Hook, -1, 1.35f);
                run.Ticks(3);
                log.AppendLine("W3_FX_POWER hit=" + ok + " banner=" + d.Fx.Banners.LastBannerText + " parts=" + d.Fx.Particles.AliveCount
                               + " freeze=" + F(d.Fx.FreezeLeft) + " slow=" + F(d.Fx.SlowLeft) + " flash=" + F(d.Fx.Overlay.FlashAlpha)
                               + " fovKick=" + F(d.Fx.FovKick));
                Pair(d, cam, ui, dir, "w3-fx-power");
                run.Ticks(60);

                // мега: набрать шкалу ударами (джебы и хуки), затем свайп в переносицу
                int n = 0;
                while (!d.State.megaReady && n < 60 && !d.Finished)
                {
                    run.WaitOpen(150);
                    run.Punch(n % 3 == 2 ? PunchType.Jab : PunchType.Hook, n % 2 == 0 ? -1 : 1, 1.3f);
                    run.Ticks(20);
                    n++;
                }
                run.Ticks(90);
                run.WaitOpen(150);
                bool megaReady = d.State.megaReady;
                ok = PunchAndWaitHit(run, PunchType.Hook, 1, 1.35f);
                run.Ticks(5);
                log.AppendLine("W3_FX_MEGA ready=" + megaReady + " fillPunches=" + n + " hit=" + ok + " banner=" + d.Fx.Banners.LastBannerText
                               + " parts=" + d.Fx.Particles.AliveCount + " splats=" + d.Fx.Overlay.ActiveSplats + " flash=" + F(d.Fx.Overlay.FlashAlpha)
                               + " fovKick=" + F(d.Fx.FovKick) + " timeScale=" + F(d.Fx.TimeScale));
                Pair(d, cam, ui, dir, "w3-fx-mega");
                run.Ticks(120);

                // пропущенный удар: игрок не защищается, снимок сразу после попадания
                run.defend = false;
                float hp0 = d.State.playerHp;
                int w = 0;
                while (d.State.playerHp >= hp0 && w < 900 && !d.Finished) { run.Tick(); w++; }
                run.Ticks(3);
                log.AppendLine("W3_FX_TAKEN waited=" + w + " playerHp=" + F(hp0) + "->" + F(d.State.playerHp) + " hurt=" + F(d.Fx.Overlay.HurtAlpha)
                               + " roll=" + F(d.Fx.CamRoll) + " offset=" + d.Fx.CamOffset.ToString("F3"));
                Pair(d, cam, ui, dir, "w3-fx-taken");
                EndFight(d);
            }

            // ---- добивание: стойкий к нокдаунам соперник, бить до окна добивания, затем до нокаута
            {
                var d = NewFight(out var cam, out var ui, 1f, 20f);
                var run = new Run { d = d };
                run.Ticks(90);
                var combo = new[] { PunchType.Hook, PunchType.Upper, PunchType.Jab, PunchType.Hook };
                int punches = 0;
                while (!d.Fx.FinisherReady && !d.Finished && punches < 200)
                {
                    run.WaitOpen(150);
                    // мегу не тратим: она могла бы снять остаток здоровья одним ударом до окна
                    var t = d.State.megaReady ? PunchType.Jab : combo[punches % combo.Length];
                    run.Punch(t, punches % 2 == 0 ? -1 : 1, 1.2f);
                    punches++;
                    for (int i = 0; i < 18 && !d.Fx.FinisherReady; i++) run.Tick();
                }
                run.Ticks(10);
                log.AppendLine("W3_FX_FINISHER_READY ready=" + d.Fx.FinisherReady + " punches=" + punches + " hp=" + F(d.State.hp) + "/" + F(d.State.hpMax)
                               + " kd=" + d.State.knockdowns + " banner=" + d.Fx.Banners.LastBannerText + " finished=" + d.Finished);
                Pair(d, cam, ui, dir, "w3-fx-finisher-ready");

                punches = 0;
                while (!d.Finished && punches < 200)
                {
                    run.WaitOpen(150);
                    var t = d.State.megaReady ? PunchType.Jab : combo[punches % combo.Length];
                    run.Punch(t, punches % 2 == 0 ? 1 : -1, 1.35f);
                    punches++;
                    for (int i = 0; i < 18 && !d.Finished; i++) run.Tick();
                }
                run.Ticks(16);
                log.AppendLine("W3_FX_FINISHER finished=" + d.Finished + " won=" + d.PlayerWon + " punches=" + punches + " banner=" + d.Fx.Banners.LastBannerText
                               + " bars=" + F(d.Fx.Overlay.Bars) + " slow=" + F(d.Fx.SlowLeft) + " timeScale=" + F(d.Fx.TimeScale)
                               + " fovKick=" + F(d.Fx.FovKick) + " flash=" + F(d.Fx.Overlay.FlashAlpha));
                Pair(d, cam, ui, dir, "w3-fx-finisher");
                EndFight(d);
            }

            // ---- поражение: сильный соперник, игрок не защищается; рефери поднимает руку соперника
            {
                var d = NewFight(out var cam, out var ui, 4f, 4f, RosterDef.Count - 1);
                var run = new Run { d = d, defend = false };
                int w = 0;
                while (!d.Finished && w < 60 * 240) { run.Tick(); w++; }
                int w2 = 0;
                while (w2 < 900 && !(d.Victory.State == VictoryState.Cheer && d.Victory.Progress >= 0.6f)) { run.Tick(); w2++; }
                log.AppendLine("W3_DEFEAT finished=" + d.Finished + " won=" + d.PlayerWon + " fightTicks=" + w + " victory=" + d.Victory.State
                               + " ref=" + d.Referee.State + " gripping=" + d.RefereeFigure.IsGripping + " screen=" + ui.ActiveScreen);
                Pair(d, cam, ui, dir, "w3-defeat-raise");
                EndFight(d);
            }

            LastLog = log.ToString().TrimEnd();
            Debug.Log(LastLog);
            Debug.Log("GAME_W3_FX_OK " + dir);
        }

        // ------------------------------------------------------------------ Аллокации в кадре боя

        // 300 кадров боя с ударами после прогрева: байты на кадр во всём Tick и без вызовов анатомии.
        public static void AllocProbe()
        {
            var d = NewFight(out _, out _, 4f, 3f);
            var run = new Run { d = d };
            run.Ticks(120);
            // прогрев: все виды ударов и звуков загружены
            for (int i = 0; i < 6; i++) { run.WaitOpen(120); run.Punch((PunchType)(i % 3), i % 2 == 0 ? -1 : 1, 1.3f); run.Ticks(20); }

            FightDirector.AllocRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            FightDirector.MeasureAlloc = true;
            d.allocTotal = d.allocAnat = 0;
            const int Frames = 300;
            int hits0 = d.HeadHits;
            for (int f = 0; f < Frames; f++)
            {
                if (f % 20 == 0) run.Punch((PunchType)(f / 20 % 3), f % 40 == 0 ? -1 : 1, 1.3f);
                run.Tick();
            }
            FightDirector.MeasureAlloc = false;
            FightDirector.AllocRec.Dispose();
            long layer = d.allocTotal - d.allocAnat;
            LastLog = "GAME_LAYER_ALLOC bytesPerFrame=" + F((double)layer / Frames) + " layerBytes=" + layer
                      + " anatBytes=" + d.allocAnat + " totalBytes=" + d.allocTotal + " frames=" + Frames + " headHits=" + (d.HeadHits - hits0);
            EndFight(d);
            Debug.Log(LastLog);
        }

        // ------------------------------------------------------------------ Лист перчаток

        [MenuItem("VoxGame/Gloves Sheet W3")]
        public static void GlovesSheet()
        {
            var dir = ShotsDir();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int count = GlovesCatalog.All.Length;
            const int Cols = 4, CellW = 400, CellH = 520, PicH = 340;
            int rows = (count + Cols - 1) / Cols;
            int W = Cols * CellW, H = rows * CellH;

            // перчатка в три четверти: костяшки к зрителю и вбок, тыльная сторона сверху
            var rig = new GameObject("GloveRig").transform;
            rig.position = new Vector3(0f, -40f, 0f);
            var mat = new Material(Shader.Find("VoxGame/VertexColor"));
            var lightGO = new GameObject("GloveLight");
            lightGO.transform.SetParent(rig, false);
            lightGO.transform.rotation = Quaternion.LookRotation(new Vector3(-0.35f, -0.7f, 0.6f));
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f; light.shadows = LightShadows.None;
            light.color = new Color(1f, 0.97f, 0.92f);
            var gcGO = new GameObject("GloveCam");
            gcGO.transform.SetParent(rig, false);
            var gcam = gcGO.AddComponent<Camera>();
            gcam.clearFlags = CameraClearFlags.SolidColor;
            gcam.backgroundColor = new Color(0.10f, 0.10f, 0.125f, 1f);
            gcam.nearClipPlane = 0.05f; gcam.farClipPlane = 5f; gcam.fieldOfView = 30f; gcam.enabled = false;
            var gcd = gcGO.AddComponent<UniversalAdditionalCameraData>();
            gcd.renderShadows = false;
            Vector3 eye = rig.position + new Vector3(0.42f, 0.30f, 0.50f) * 0.8f, at = rig.position + new Vector3(0f, 0f, -0.03f);
            gcam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));

            var pics = new RenderTexture[count];
            for (int i = 0; i < count; i++)
            {
                var g = GloveLooks.SpawnShowcase(GlovesCatalog.All[i].id, rig, mat, -1);
                g.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                pics[i] = new RenderTexture(CellW, PicH, 24, RenderTextureFormat.ARGB32);
                gcam.targetTexture = pics[i];
                gcam.Render();
                gcam.targetTexture = null;
                GloveLooks.DestroyShowcase(g);
            }
            Object.DestroyImmediate(rig.gameObject);
            Object.DestroyImmediate(mat);

            const int UiLayer = 5;
            var at2 = new Vector3(0f, 100f, 0f);
            var camGO = new GameObject("SheetCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.065f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = H * 0.001f * 0.5f;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 5f;
            cam.cullingMask = 1 << UiLayer;
            cam.transform.SetPositionAndRotation(at2 - Vector3.forward, Quaternion.identity);
            camGO.AddComponent<UniversalAdditionalCameraData>().renderShadows = false;

            var cgo = new GameObject("Sheet", typeof(RectTransform), typeof(Canvas));
            cgo.layer = UiLayer;
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var crt = cgo.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(W, H);
            crt.localScale = Vector3.one * 0.001f;
            crt.position = at2;

            for (int i = 0; i < count; i++)
            {
                var e = GlovesCatalog.All[i];
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

                var name = UIKit.Label(cell, "Name", 32, UIKit.TextMain, true, TextAnchor.MiddleLeft);
                name.text = e.displayName + (e.price > 0 ? "  · " + e.price : "");
                Bottom(name.rectTransform, 118f, 42f);
                var stats = UIKit.Label(cell, "Stats", 24, UIKit.Accent, false, TextAnchor.UpperLeft);
                stats.text = GloveStats(e);
                Bottom(stats.rectTransform, 8f, 106f);
            }
            foreach (var t in cgo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UiLayer;

            Canvas.ForceUpdateCanvases();
            Shot(cam, null, W, H, Path.Combine(dir, "w3-gloves-sheet.png"));
            foreach (var p in pics) { p.Release(); Object.DestroyImmediate(p); }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("GAME_W3_GLOVES_OK " + dir);
        }

        // 2–3 главных отличия перчатки от классики: самые большие отклонения множителей.
        static string GloveStats(in GloveEntry e)
        {
            var names = new[] { "удар", "скорость", "расход сил", "мега", "получаемый урон" };
            // для расхода сил и получаемого урона меньше — лучше
            var vals = new[] { e.dmgMult, e.speedMult, e.staminaCostMult, e.megaGainMult, e.incomingDmgMult };
            var idx = new[] { 0, 1, 2, 3, 4 };
            Array.Sort(idx, (a, b) => Mathf.Abs(vals[b] - 1f).CompareTo(Mathf.Abs(vals[a] - 1f)));
            var sb = new StringBuilder();
            int shown = 0;
            foreach (int k in idx)
            {
                float dv = vals[k] - 1f;
                if (Mathf.Abs(dv) < 0.01f || shown == 3) continue;
                if (shown > 0) sb.Append('\n');
                sb.Append(names[k]).Append(' ').Append(dv > 0 ? "+" : "−").Append(Mathf.RoundToInt(Mathf.Abs(dv) * 100f)).Append('%');
                shown++;
            }
            return shown == 0 ? "без сильных и слабых сторон" : sb.ToString();
        }
    }
}
