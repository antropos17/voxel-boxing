// ShotsTaker.cs — утилита снимков игрового слоя.
// Shot(...) — рендер одного кадра в PNG.
// FightShots() — сквозной сценарий W1 без Play Mode: настоящий FightDirector, удары только через InputReader.Inject,
// нокдаун и нокаут — настоящими ударами; пять пар портрет/ландшафт и замер кадра удара.
// DiagShots() — диагностика: сцена со стороны, без UI.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VoxProbe;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class ShotsTaker
    {
        const float DT = 1f / 60f;
        const int PortW = 540, PortH = 1170, LandW = 1170, LandH = 540;
        const int WarmHits = 3;   // первые удары включают JIT и компиляцию Burst — в статистику не входят

        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        static string F(double v) => v.ToString("0.00", Inv);

        // ------------------------------------------------------------------ Снимок

        /// Рендер одного кадра через RenderTexture → PNG. ui == null — без интерфейса.
        /// Canvas переводится в World Space вне Play Mode (Screen Space в пакетном режиме не рисует в текстуру).
        public static void Shot(Camera cam, UIRoot ui, int w, int h, string path)
        {
            if (ui != null)
            {
                ui.PrepareForShot(w, h);
                if (!Application.isPlaying) ui.FitToCamera(cam, w, h);
                Canvas.ForceUpdateCanvases();
                // холст снимка другого размера: обрезку портрета лестницы пересчитать (в игре это делает Tick экрана)
                foreach (var rs in ui.GetComponentsInChildren<RosterScreen>(false)) rs.Tick(0f, 0f);
                Canvas.ForceUpdateCanvases();
            }

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture    = rt;
            RenderTexture.active = rt;
            cam.Render();

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, tex.EncodeToPNG());

            Object.DestroyImmediate(tex);
            cam.targetTexture    = null;
            RenderTexture.active = null;
            rt.Release();

            ui?.RestoreAfterShot();
        }

        // ------------------------------------------------------------------ Сцена

        static string ShotsDir()
        {
            var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "game"));
            Directory.CreateDirectory(d);
            return d;
        }

        static FightDirector NewFight(out Camera cam, out UIRoot ui, float hpMult = 1f, float stunMult = 1f,
                                      int foeIdx = 0, bool forceBelt = false, Func<FighterDef, FighterDef> tweak = null)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGO = new GameObject("ShotCam");
            cam = camGO.AddComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.035f, 1f);
            cam.nearClipPlane   = 0.05f;
            cam.farClipPlane    = 40f;
            var camData = camGO.AddComponent<UniversalAdditionalCameraData>();
            camData.renderShadows = false;
            camData.antialiasing  = AntialiasingMode.None;

            var uiGO = new GameObject("UIRoot");
            ui = uiGO.AddComponent<UIRoot>();
            ui.Init(cam);

            var def = new RosterDef(42u).Get(foeIdx);
            if (tweak != null) def = tweak(def);   // замеры и снимки внешности (FacesShots)
            var fdGO = new GameObject("FightDirector");
            var director = fdGO.AddComponent<FightDirector>();
            director.Init(def, new PlayerProfile(), cam, ui, null, hpMult, stunMult, foeIdx, RosterDef.Count, forceBelt);
            director.SetViewSize(PortW, PortH);
            director.Fx.QuietDodge = true;   // автоуклон бота не пишет «УКЛОН!» на снимках
            return director;
        }

        static void EndFight(FightDirector director)
        {
            var go = director.gameObject;
            director.Dispose();
            Object.DestroyImmediate(go);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // Пара снимков портрет/ландшафт одного и того же момента боя.
        static void Pair(FightDirector d, Camera cam, UIRoot ui, string dir, string tag)
        {
            d.SetViewSize(PortW, PortH);
            Shot(cam, ui, PortW, PortH, Path.Combine(dir, tag + "-p.png"));
            d.SetViewSize(LandW, LandH);
            Shot(cam, ui, LandW, LandH, Path.Combine(dir, tag + "-l.png"));
            d.SetViewSize(PortW, PortH);
        }

        // ------------------------------------------------------------------ Замер кадра удара

        struct HitFrame { public double ext, frame, pick, hit, tick, layer, tSim, tBore, tRest, tMesh, tickLastMs, crowd, refv, gear, fx, sound; }

        // Что повторить на отдельной голове: игровое время каждого кадра и удар в тех кадрах, где он был.
        struct Rec { public float sdt; public bool hit; public Ray ray; public float3 pt, dir; public int str; }

        sealed class Run
        {
            public FightDirector d;
            public readonly List<HitFrame> frames = new List<HitFrame>();
            public readonly List<Rec> recs = new List<Rec>();
            readonly Stopwatch sw = new Stopwatch();

            bool dodged;
            public bool defend = true;   // false — игрок не уклоняется (снимки пропущенного удара, поражение)

            public void Tick()
            {
                // защита игрока — настоящими кнопками HUD: уклон в сторону от летящего удара
                if (defend && d.State.phase == FightPhase.Strike && !dodged)
                {
                    if (d.FoeAttackSide > 0) d.Hud.PressDodgeLeft(); else d.Hud.PressDodgeRight();
                    dodged = true;
                }
                else if (d.State.phase != FightPhase.Strike) dodged = false;

                int seq = d.hitFrameSeq;
                sw.Restart();
                d.Tick(DT);
                double ext = sw.Elapsed.TotalMilliseconds;
                bool hit = d.hitFrameSeq != seq;
                recs.Add(new Rec { sdt = d.lastSdt, hit = hit, ray = d.lastHeadRay, pt = d.lastHitPoint, dir = d.lastHitDir, str = d.lastHitStrength });
                if (!hit) return;
                var v = d.Head;
                frames.Add(new HitFrame
                {
                    ext = ext, frame = d.lastHitFrameMs, pick = d.lastPickMs, hit = d.lastHitMs, tick = d.lastAnatTickMs,
                    layer = d.lastLayerMs, crowd = d.lastCrowdMs, refv = d.lastRefMs, gear = d.lastGearMs, fx = d.lastFxMs, sound = d.lastSoundMs, tSim = v.tSim, tBore = v.tBore, tRest = v.tRest, tMesh = v.tMesh, tickLastMs = v.lastMs,
                });
            }

            public void Ticks(int n) { for (int i = 0; i < n; i++) Tick(); }

            public void Punch(PunchType type, int side, float pw = 1.25f) =>
                d.GetComponent<InputReader>().Inject(new PunchInput { type = type, side = side, pw = pw, speed = 1.2f });

            // Ждать, пока соперник не в замахе/ударе и не на полу (только чтение состояния).
            public void WaitOpen(int maxTicks)
            {
                for (int i = 0; i < maxTicks && !d.Finished; i++)
                {
                    var ph = d.State.phase;
                    if (ph == FightPhase.Idle || ph == FightPhase.Guard || ph == FightPhase.Recover || ph == FightPhase.Open
                        || ph == FightPhase.Stagger) return;
                    Tick();
                }
            }
        }

        static string Stats(List<HitFrame> fs, int from, Func<HitFrame, double> sel, out double avg)
        {
            double mx = 0, sum = 0; int n = 0;
            for (int i = from; i < fs.Count; i++) { double x = sel(fs[i]); mx = Math.Max(mx, x); sum += x; n++; }
            avg = n > 0 ? sum / n : 0;
            return "max=" + F(mx) + " avg=" + F(avg) + " n=" + n;
        }

        // Те же вызовы анатомии на отдельной голове без слоя: AnatSkin.Step → (Pick → Push → Hit) → v.Tick → SetGlobals.
        static List<HitFrame> BareHead(List<Rec> recs, Vector3 at)
        {
            var mat = new Material(Shader.Find("VoxProbe/Anatomy"));
            AnatSkin.on = true;
            AnatSkin.Reset();
            var v = new AnatView(null, at, mat);
            var res = new List<HitFrame>();
            var sw = new Stopwatch();
            foreach (var r in recs)
            {
                sw.Restart();
                double pick = 0, hit = 0;
                if (r.hit)
                {
                    v.Pick(new Ray(v.root.position + r.ray.origin, r.ray.direction), out _, out _, out _);
                    pick = sw.Elapsed.TotalMilliseconds;
                    AnatSkin.Push(r.pt, r.dir, r.str);
                    v.Hit(r.pt, r.dir, r.str);
                    hit = sw.Elapsed.TotalMilliseconds - pick;
                }
                double t0 = sw.Elapsed.TotalMilliseconds;
                AnatSkin.Step(r.sdt);
                v.Tick(r.sdt);
                AnatSkin.SetGlobals(v.root);
                double tick = sw.Elapsed.TotalMilliseconds - t0;
                if (r.hit) res.Add(new HitFrame { frame = sw.Elapsed.TotalMilliseconds, pick = pick, hit = hit, tick = tick });
            }
            v.Dispose();
            Object.DestroyImmediate(mat);
            AnatSkin.Reset();
            return res;
        }

        // Настройки Burst и проверок редактора — только чтение.
        static string BurstInfo()
        {
            var o = BurstCompiler.Options;
            return "EnableBurstCompilation=" + o.EnableBurstCompilation
                 + " EnableBurstCompileSynchronously=" + o.EnableBurstCompileSynchronously
                 + " EnableBurstSafetyChecks=" + o.EnableBurstSafetyChecks
                 + " ForceEnableBurstSafetyChecks=" + o.ForceEnableBurstSafetyChecks
                 + " JobDebuggerEnabled=" + JobsUtility.JobDebuggerEnabled
                 + " NativeLeakDetection=" + Unity.Collections.NativeLeakDetection.Mode
                 + " batch=" + Application.isBatchMode;
        }

        // ------------------------------------------------------------------ Сценарий W1

        [MenuItem("VoxGame/Fight Shots W1")]
        public static void FightShots()
        {
            var dir = ShotsDir();
            // здоровье ×4 и стойкость ×3: серия для w1-hits (с мегами) не валит соперника, мега не добивает до нокдауна
            var d = NewFight(out var cam, out var ui, 4f, 3f);
            var run = new Run { d = d };
            var log = new StringBuilder();

            // Разминка: ~1.5 с боя, сетки головы построены
            run.Ticks(90);
            Pair(d, cam, ui, dir, "w1-start");

            // ---- w1-hits: серия разных ударов, между ударами идут тики
            // (силовой удар при полной шкале сам становится мегой — прямой в переносицу)
            var series = new (PunchType t, int side)[]
            {
                (PunchType.Hook, -1), (PunchType.Jab, -1), (PunchType.Hook, -1), (PunchType.Upper, 1), (PunchType.Jab, 1),
                (PunchType.Hook, -1), (PunchType.Hook, 1), (PunchType.Jab, -1), (PunchType.Upper, -1), (PunchType.Hook, -1),
                (PunchType.Jab, 1), (PunchType.Hook, -1), (PunchType.Upper, 1), (PunchType.Jab, -1),
            };
            int seriesMegas = 0;
            foreach (var (t, side) in series)
            {
                run.WaitOpen(120);
                if (d.State.megaReady && t != PunchType.Jab) seriesMegas++;
                run.Punch(t, side);
                run.Ticks(30);
            }
            if (d.State.megaReady)      // накопленную мегу — в переносицу
            {
                run.WaitOpen(120);
                seriesMegas++;
                run.Punch(PunchType.Hook, 1);
                run.Ticks(30);
            }
            run.Ticks(90);              // кровь стекает, отёк растёт
            run.WaitOpen(150);
            // кадр удара: хук сбоку — перчатка у скулы, лицо не закрыто
            bool hookOk = !d.State.megaReady;
            run.Punch(hookOk ? PunchType.Hook : PunchType.Jab, -1);
            run.Ticks(hookOk ? 6 : 3);  // перчатка в крайней точке
            var v = d.Head;
            string hitsLine = "GAME_HITS hits=" + d.HeadHits + " landed=" + d.HitsLanded + " removed=" + v.sim.removed
                              + " wounds=" + v.sim.wounds.Count + " drips=" + v.sim.drips.Count + " trauma=" + F(v.sim.traumaAll)
                              + " foeHp=" + F(d.State.hp) + "/" + F(d.State.hpMax) + " stun=" + F(d.State.stunAccum) + "/" + F(d.State.stunMax)
                              + " phase=" + d.State.phase + " seriesMegas=" + seriesMegas + " kd=" + d.State.knockdowns;
            Pair(d, cam, ui, dir, "w1-hits");

            // ---- w1-telegraph: естественный замах соперника
            {
                int w = 0;
                while (d.State.phase != FightPhase.Windup && w < 600 && !d.Finished) { run.Tick(); w++; }
                run.Ticks(4);
                log.AppendLine("GAME_TELEGRAPH phase=" + d.State.phase + " waited=" + w);
                Pair(d, cam, ui, dir, "w1-telegraph");
            }

            // ---- w1-knockdown: бить, пока соперник не упадёт; снимок на счёте рефери
            int punches = 0;
            var combo = new[] { PunchType.Hook, PunchType.Upper, PunchType.Jab, PunchType.Hook };
            while (d.State.knockdowns == 0 && !d.Finished && punches < 80)
            {
                run.WaitOpen(120);
                run.Punch(combo[punches % combo.Length], punches % 2 == 0 ? -1 : 1);
                punches++;
                run.Ticks(18);
            }
            {
                int w = 0;
                while (d.Referee.Count < 2 && w < 400 && !d.Finished) { run.Tick(); w++; }
                run.Ticks(2);
                log.AppendLine("GAME_KNOCKDOWN punches=" + punches + " kd=" + d.State.knockdowns + " refCount=" + d.Referee.Count
                               + " refState=" + d.Referee.State + " phase=" + d.State.phase);
                Pair(d, cam, ui, dir, "w1-knockdown");
            }

            // ---- w1-result: соперник встаёт, добить (мега — свайпом при полной шкале)
            int megas = 0;
            punches = 0;
            while (!d.Finished && punches < 200)
            {
                run.WaitOpen(200);
                if (d.State.phase == FightPhase.Down || d.State.phase == FightPhase.Rise) { run.Tick(); continue; }
                if (d.State.megaReady) megas++;
                run.Punch(combo[punches % combo.Length], punches % 2 == 0 ? 1 : -1, 1.35f);
                punches++;
                run.Ticks(18);
            }
            {
                int w = 0;
                while (!d.Victory.IsDone && w < 900) { run.Tick(); w++; }
                run.Ticks(30);
                log.AppendLine("GAME_RESULT finished=" + d.Finished + " won=" + d.PlayerWon + " punches=" + punches + " megaSwipes=" + megas
                               + " kd=" + d.State.knockdowns + " ref=" + d.Referee.State + " playerHp=" + F(d.State.playerHp));
                Pair(d, cam, ui, dir, "w1-result");
            }

            // ---- Замер
            var fs = run.frames;
            var warm = new StringBuilder("GAME_HIT_WARMUP");
            for (int i = 0; i < Math.Min(WarmHits, fs.Count); i++)
                warm.Append(" #").Append(i + 1).Append(" frame=").Append(F(fs[i].frame)).Append(" pick=").Append(F(fs[i].pick))
                    .Append(" hit=").Append(F(fs[i].hit)).Append(" tick=").Append(F(fs[i].tick)).Append(" layer=").Append(F(fs[i].layer));
            string frameLine = "GAME_HIT_FRAME_MS " + Stats(fs, WarmHits, f => f.frame, out _) + " (warmup excluded: " + WarmHits + ")";
            Stats(fs, WarmHits, f => f.pick, out double aPick);
            Stats(fs, WarmHits, f => f.hit, out double aHit);
            Stats(fs, WarmHits, f => f.tick, out double aTick);
            string layerStats = Stats(fs, WarmHits, f => f.layer, out double aLayer);
            Stats(fs, WarmHits, f => f.ext, out double aExt);
            Stats(fs, WarmHits, f => f.tSim, out double aSim);
            Stats(fs, WarmHits, f => f.tBore, out double aBore);
            Stats(fs, WarmHits, f => f.tRest, out double aRest);
            Stats(fs, WarmHits, f => f.tMesh, out double aMesh);
            Stats(fs, WarmHits, f => f.tickLastMs, out double aLast);
            string brk = "GAME_HIT_BREAKDOWN pick=" + F(aPick) + " hit=" + F(aHit) + " tick=" + F(aTick) + " layer=" + F(aLayer)
                         + " (layer " + layerStats + "; outer Stopwatch avg=" + F(aExt) + ")"
                         + " hitParts tSim=" + F(aSim) + " tBore=" + F(aBore) + " tRest=" + F(aRest) + " tMesh=" + F(aMesh) + " tick.lastMs=" + F(aLast);
            string parts = "GAME_LAYER_PARTS crowd " + Stats(fs, WarmHits, f => f.crowd, out _) + "; referee " + Stats(fs, WarmHits, f => f.refv, out _)
                           + "; gear " + Stats(fs, WarmHits, f => f.gear, out _)
                           + "; fx=" + Stats(fs, WarmHits, f => f.fx, out _) + "; sound=" + Stats(fs, WarmHits, f => f.sound, out _);
            // самый дорогой кадр слоя: из чего он состоит
            int wi = -1;
            for (int i = WarmHits; i < fs.Count; i++) if (wi < 0 || fs[i].layer > fs[wi].layer) wi = i;
            if (wi >= 0)
            {
                var w = fs[wi];
                parts += "\nGAME_LAYER_WORST #" + (wi + 1) + " layer=" + F(w.layer) + " crowd=" + F(w.crowd) + " referee=" + F(w.refv)
                       + " gear=" + F(w.gear) + " fx=" + F(w.fx) + " sound=" + F(w.sound)
                       + " other=" + F(w.layer - w.crowd - w.refv - w.gear - w.fx - w.sound);
            }

            Vector3 headAt = d.Head.root.position;
            var recs = run.recs;
            EndFight(d);

            // ---- Та же голова без слоя: те же удары и те же шаги времени
            var bare = BareHead(recs, headAt);
            string bareLine = "GAME_BARE_HEAD_MS " + Stats(bare, WarmHits, f => f.frame, out _);
            Stats(bare, WarmHits, f => f.pick, out double bPick);
            Stats(bare, WarmHits, f => f.hit, out double bHit);
            Stats(bare, WarmHits, f => f.tick, out double bTick);
            bareLine += " pick=" + F(bPick) + " hit=" + F(bHit) + " tick=" + F(bTick);

            LastLog = hitsLine + "\n" + log.ToString().TrimEnd() + "\n" + warm + "\n" + frameLine + "\n" + brk + "\n" + parts
                      + "\n" + bareLine + "\nGAME_BURST " + BurstInfo();
            Debug.Log(LastLog);
            Debug.Log("GAME_SHOTS_OK " + dir);
        }

        // ------------------------------------------------------------------ Диагностика (третье лицо, без UI)

        public static void DiagShots()
        {
            var dir = ShotsDir();
            var d = NewFight(out var cam, out _);
            for (int i = 0; i < 30; i++) d.Tick(DT);

            var sb = new StringBuilder();
            sb.AppendLine($"DIAG cam pos={cam.transform.position} fwd={cam.transform.forward} fov={cam.fieldOfView} head={d.Head.root.position}");
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (r.GetComponentInParent<Canvas>() != null || !r.enabled || r.transform.parent == null || r.transform.parent.name == "head") continue;
                var b = r.bounds;
                sb.AppendLine($"DIAG {r.transform.parent.name}/{r.name} min={b.min.ToString("F3")} max={b.max.ToString("F3")}");
            }
            Debug.Log(sb.ToString());

            var dGO = new GameObject("DiagCam");
            var dc = dGO.AddComponent<Camera>();
            dc.clearFlags = CameraClearFlags.SolidColor; dc.backgroundColor = new Color(0.25f, 0.25f, 0.3f, 1f);
            dc.nearClipPlane = 0.05f; dc.farClipPlane = 60f;
            Vector3 mid = d.Head.root.position + new Vector3(0f, -0.45f, 0f);
            void D(string name, Vector3 pos, Vector3 look, float fov)
            {
                dc.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
                dc.fieldOfView = fov;
                Shot(dc, null, 1000, 800, Path.Combine(dir, name + ".png"));
            }
            D("diag-side", mid + new Vector3(3.2f, 0.3f, 0f), mid, 40f);
            D("diag-34", mid + new Vector3(2.2f, 1.2f, 2.6f), mid, 40f);
            D("diag-front", mid + new Vector3(0f, 0.3f, 2.4f), mid, 40f);
            EndFight(d);
            Debug.Log("GAME_DIAG_OK");
        }
    }
}
