// ShotsTaker.cs — утилита снимков игрового слоя.
// Shot(...) — статический метод рендера одного кадра в PNG.
// FightShots() — сквозной сценарий W1: 5 пар портрет/ландшафт без Play Mode.
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VoxProbe;

namespace VoxGame.Editor
{
    public static class ShotsTaker
    {
        // ------------------------------------------------------------------ Статический метод снимка

        /// Рендер одного кадра через RenderTexture → PNG.
        /// Canvas переводится в World Space если вне Play Mode (Screen Space не работает в batch).
        /// Вызывающий обязан освободить ресурсы сам если создавал объекты.
        public static void Shot(Camera cam, UIRoot ui, int w, int h, string path)
        {
            ui.PrepareForShot(w, h);

            // В редакторе / пакетном режиме Screen Space Camera не рисует в RenderTexture.
            // Переключаем Canvas в World Space перед камерой по frustum.
            if (!Application.isPlaying)
                ui.FitToCamera(cam, w, h);

            Canvas.ForceUpdateCanvases();

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture    = rt;
            RenderTexture.active = rt;
            cam.Render();

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, tex.EncodeToPNG());

            Object.DestroyImmediate(tex);
            cam.targetTexture    = null;
            RenderTexture.active = null;
            rt.Release();

            ui.RestoreAfterShot();
        }

        // ------------------------------------------------------------------ Сценарий снимков W1

        /// Вне Play Mode: создаёт бой на чистой логике, прогоняет тики с синтетическими
        /// жестами, снимает пять пар портрет/ландшафт в Unity/Shots/game/,
        /// выводит в лог GAME_HIT_FRAME_MS.
        [MenuItem("VoxGame/Fight Shots W1")]
        public static void FightShots()
        {
            var shotsDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Shots", "game"));
            Directory.CreateDirectory(shotsDir);

            // --- Новая пустая сцена ---
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Определяем бойца заранее (нужен для FoeBody) ---
            var roster = new RosterDef(42u);
            var def    = roster.Get(0); // боец уровня 1

            // --- Шейдеры и материалы для воксельных тел ---
            var voxShader  = Shader.Find("VoxGame/VertexColor");
            var voxMat     = new Material(voxShader != null ? voxShader : Shader.Find("Hidden/InternalErrorShader"));
            var anatShader = Shader.Find("VoxProbe/Anatomy");
            var anatMat    = new Material(anatShader != null ? anatShader : Shader.Find("Hidden/InternalErrorShader"));

            // --- Соперник: воксельное тело ---
            FoeBody foeBody = null;
            try
            {
                foeBody = new FoeBody(null, def, voxMat);
                foeBody.root.position = new Vector3(0f, 0f, 0.5f);
                foeBody.root.rotation = Quaternion.Euler(0f, 180f, 0f);
            }
            catch { /* продолжаем без тела */ }

            // --- Соперник: анатомическая голова ---
            try
            {
                AnatSkin.on = true;
                AnatSkin.Reset();
                var headPos = foeBody != null ? foeBody.headMount.position : new Vector3(0f, 1.57f, 0.47f);
                var v = new AnatView(null, headPos, anatMat);
                // Несколько тиков для инициализации меша
                for (int i = 0; i < 3; i++) { AnatSkin.Step(0.016f); v.Tick(0.016f, 999); AnatSkin.SetGlobals(v.root); }
            }
            catch { /* продолжаем без головы */ }

            // --- Камера (смотрит на лицо соперника, как FightDirector) ---
            var camGo = new GameObject("ShotCam");
            var cam   = camGo.AddComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 1f);
            cam.nearClipPlane   = 0.05f;
            cam.farClipPlane    = 20f;
            cam.fieldOfView     = 60f;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderShadows = false;
            camData.antialiasing  = AntialiasingMode.None;
            // Позиция как в FightDirector: чуть выше головы соперника, смотрим на headMount
            camGo.transform.position = new Vector3(0f, 1.65f, -0.55f);
            var lookTarget = foeBody != null ? foeBody.headMount.position : new Vector3(0f, 1.57f, 0.47f);
            camGo.transform.LookAt(lookTarget);

            // --- Перчатки игрока (вид от первого лица) ---
            try
            {
                var gloveEntry = GlovesCatalog.Get("classic");
                var _ = new PlayerGloves(camGo.transform, GloveLook.FromEntry(gloveEntry), voxMat);
            }
            catch { /* продолжаем без перчаток */ }

            // --- Свет ---
            var lightGo = new GameObject("Sun");
            var light   = lightGo.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.shadows   = LightShadows.None;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

            // --- Ринг (геометрия в фоне, нужен шейдер VoxGame/VertexColor) ---
            try
            {
                var ring = new RingBuilder();
                ring.Build(null);
            }
            catch { /* продолжаем без геометрии если шейдер не найден */ }

            // --- UI ---
            var uiGo   = new GameObject("UIRoot");
            var uiRoot = uiGo.AddComponent<UIRoot>();
            uiRoot.Init(cam);

            var hudGo = new GameObject("FightHUD");
            var hud   = hudGo.AddComponent<FightHUD>();
            hud.Init(uiRoot);

            var resultGo = new GameObject("ResultScreen");
            var result   = resultGo.AddComponent<ResultScreen>();
            result.Init(uiRoot);

            uiRoot.ShowScreen(ScreenId.Fight);

            // Умеренный dmgMult: достаточно для заметных повреждений, но не мгновенный KO
            var prms = new FightParams
            {
                dmgMult         = 3f,
                staminaCostMult = 1f,
                megaGainMult    = 2f,
                defenseMult     = 1f,
                speedMult       = 1f,
            };
            var sim = new FightSim(def.level, def.seed, prms);
            var ai  = new FightAI(FightAI.Styles[def.styleIdx], def.level, def.seed);

            var sw         = new Stopwatch();
            var hitFrameMs = new List<double>();
            float gameTime = 0f;
            const float DT = 1f / 60f;

            // --- Вспомогательные функции ---

            void Tick()
            {
                var cmd = ai.Tick(DT, sim.State);
                sim.State.phase = cmd.newPhase;
                sim.TickRecovery(DT);
                gameTime += DT;
            }

            HitResult Punch(int removed, int zoneIdx = 2, int strength = 1, bool mega = false)
            {
                float pw = mega ? 1.35f : 1.1f;
                sw.Restart();
                var r = sim.ProcessHit(removed, zoneIdx, removed * 0.5f, 0, strength,
                                       mega, pw, false, false);
                hitFrameMs.Add(sw.Elapsed.TotalMilliseconds);
                return r;
            }

            HudData MakeHud(FightPhase foePhase = FightPhase.Idle,
                            float telegraphAlpha = 0f, int attackSide = 0) =>
                new HudData
                {
                    playerHp       = sim.State.playerHp,
                    playerHpMax    = 100f,
                    foeHp          = sim.State.hp,
                    foeHpMax       = sim.State.hpMax,
                    playerStamina  = sim.State.playerStamina,
                    megaMeter      = sim.State.megaMeter,
                    megaReady      = sim.State.megaReady,
                    fightTime      = gameTime,
                    foeKnockdowns  = sim.State.knockdowns,
                    foeName        = def.name,
                    playerName     = "Игрок",
                    foePhase       = foePhase,
                    foeAttackSide  = attackSide,
                    telegraphAlpha = telegraphAlpha,
                    playerBlocking = sim.State.playerBlocking,
                    combo          = sim.State.comboCount,
                };

            void TakeShots(string tag, HudData hudData)
            {
                hud.Refresh(hudData);
                Shot(cam, uiRoot, 540,  1170, Path.Combine(shotsDir, tag + "-p.png"));
                Shot(cam, uiRoot, 1170, 540,  Path.Combine(shotsDir, tag + "-l.png"));
            }

            // ---------------------------------------------------------------- w1-start
            // Начало боя: свежее состояние
            TakeShots("w1-start", MakeHud());

            // ---------------------------------------------------------------- w1-hits: ~10 ударов игрока
            // Небольшой removed (10–15 ячеек) чтобы показать повреждения без KO
            for (int i = 0; i < 10; i++)
            {
                Tick();
                Punch(10 + i, zoneIdx: i % 7, strength: 1);
            }
            TakeShots("w1-hits", MakeHud());

            // ---------------------------------------------------------------- w1-telegraph: соперник замахивается
            // Принудительно устанавливаем Windup; HUD покажет подсказку уклона
            var savedPhase = sim.State.phase;
            sim.State.phase = FightPhase.Windup;
            TakeShots("w1-telegraph",
                MakeHud(FightPhase.Windup, telegraphAlpha: 0.8f, attackSide: 1));
            sim.State.phase = savedPhase;

            // ---------------------------------------------------------------- w1-knockdown
            // Принудительно настраиваем состояние первого нокдауна для снимка.
            // Это надёжнее, чем ждать естественного накопления stun с малым removed.
            var savedKd    = sim.State.knockdowns;
            var savedHp    = sim.State.hp;
            var savedStun  = sim.State.stunAccum;
            sim.State.knockdowns = 1;
            sim.State.hp         = sim.State.hpMax * 0.5f; // 50 % HP для реализма
            sim.State.stunAccum  = 0f;
            sim.State.phase      = FightPhase.Down;
            hud.ShowCount(1);
            TakeShots("w1-knockdown", MakeHud(FightPhase.Down));
            // Восстановить после снимка
            sim.State.knockdowns = savedKd;
            sim.State.hp         = savedHp;
            sim.State.stunAccum  = savedStun;

            // ---------------------------------------------------------------- w1-result: нокаут и победа
            // Серия мега-ударов до KO (мега шкала заполнена принудительно)
            HitResult ko  = default;
            int attempts = 0;
            while (!ko.isKO && sim.State.phase != FightPhase.KO && attempts < 500)
            {
                attempts++;
                Tick();
                sim.State.megaMeter = 100f;
                sim.State.megaReady = true;
                ko = Punch(50, zoneIdx: 2, strength: 2, mega: true);
            }

            uiRoot.ShowScreen(ScreenId.Result);
            result.Show(true, gameTime, Mathf.Max(sim.State.knockdowns, savedKd), 0.75f, 250);
            Canvas.ForceUpdateCanvases();
            TakeShots("w1-result", MakeHud());

            // ---------------------------------------------------------------- Метрики
            hitFrameMs.Sort();
            double max = hitFrameMs.Count > 0 ? hitFrameMs[hitFrameMs.Count - 1] : 0.0;
            double avg = 0.0;
            foreach (var v in hitFrameMs) avg += v;
            avg = hitFrameMs.Count > 0 ? avg / hitFrameMs.Count : 0.0;

            UnityEngine.Debug.Log(
                $"GAME_HIT_FRAME_MS max={max:0.##} avg={avg:0.##} n={hitFrameMs.Count}");

            // --- Освободить сцену ---
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnityEngine.Debug.Log("GAME_SHOTS_OK " + shotsDir);
        }
    }
}
