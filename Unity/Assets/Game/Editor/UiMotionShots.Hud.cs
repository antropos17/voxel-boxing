// UiMotionShots.Hud.cs — серия кадров шкал боя с движением (UIMotion.ForceAnimate) и замер времени HUD.
// HudSeries(): выезд шкал, удар (до, пик, след, покой), мало здоровья, накал меги и «МЕГА ГОТОВ», нокдаун со счётом, уход.
// HudPerf(): время FightHUD.Refresh+Tick+UIMotion.Tick и выделения памяти HUD в установившемся бою.
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class UiMotionShots
    {
        static void ToFight()
        {
            NewApp();
            Ticks(10);
            Press("menu.play"); Press("mode.ladder"); Press("roster.select:0");
            Press("roster.fight", false);
            int w = 0;
            while (_app.State != AppState.Fight && w < 900) { _app.Tick(DT); w++; }
            if (_app.State != AppState.Fight) throw new Exception("UI_MOTION_FAIL бой не начался: " + _app.State);
        }

        static int TicksUntil(Func<bool> cond, int max)
        {
            int i = 0;
            for (; i < max && !cond(); i++) _app.Tick(DT);
            return i;
        }

        public static void HudSeries()
        {
            _log = new System.Text.StringBuilder();
            _dir = Dir();
            UIMotion.ForceAnimate = true;
            try
            {
                ToFight();
                var hud = _app.Fight.Hud;
                _app.Tick(DT); _app.Tick(DT); _app.Tick(DT);
                S("hud-01-intro");
                Ticks(40); S("hud-02-rest");

                // удар: до, пик (кадр попадания), след урона, покой
                var st = _app.Fight.State;
                TicksUntil(() => st.phase == FightPhase.Idle || st.phase == FightPhase.Guard || st.phase == FightPhase.Open, 300);
                S("hud-03-hit-before");
                float v0 = hud.FoeValue;
                Hit(PunchType.Hook, -1, 1.4f);
                int n = TicksUntil(() => hud.FoeValue < v0 - 0.001f, 120);
                _app.Tick(DT);
                S("hud-04-hit-peak"); S("hud-04-hit-peak", true);
                _log.AppendLine("UI_HUD hit after " + n + " ticks: value=" + F(hud.FoeValue) + " trail=" + F(hud.FoeTrail) + " jolt=" + F(hud.PanelJolt));
                Ticks(14); S("hud-05-hit-trail");
                _log.AppendLine("UI_HUD trail +0.25s: value=" + F(hud.FoeValue) + " trail=" + F(hud.FoeTrail));
                Ticks(70); S("hud-06-hit-rest");
                _log.AppendLine("UI_HUD rest +1.4s: value=" + F(hud.FoeValue) + " trail=" + F(hud.FoeTrail));

                // серия ударов: цифры складываются
                for (int i = 0; i < 3; i++) { Hit(i % 2 == 0 ? PunchType.Jab : PunchType.Hook, i % 2 == 0 ? 1 : -1, 1.3f); Ticks(10); }
                S("hud-07-combo");

                // накал меги и «МЕГА ГОТОВ»
                st.megaMeter = 70f; Ticks(30); S("hud-08-mega-heat");
                st.megaMeter = 100f; st.megaReady = true; Ticks(6); S("hud-09-mega-ready"); S("hud-09-mega-ready", true);
                Ticks(40);

                // мало здоровья: пульс и угли
                st.playerHp = 22f; Ticks(20); S("hud-10-low-hp"); S("hud-10-low-hp", true);

                // оглушение у края — искрит; затем нокдаун со счётом рефери
                st.stunAccum = st.stunMax * 0.9f; Ticks(12); S("hud-11-stun-near");
                int kd0 = st.knockdowns;
                for (int i = 0; i < 6 && st.knockdowns == kd0; i++)
                {
                    TicksUntil(() => st.phase == FightPhase.Idle || st.phase == FightPhase.Guard || st.phase == FightPhase.Open || st.phase == FightPhase.Stagger, 200);
                    st.stunAccum = st.stunMax * 0.98f;
                    Hit(PunchType.Hook, 1, 1.5f); Ticks(20);
                }
                int c = TicksUntil(() => hud.CountVisible, 600);
                Ticks(4);
                S("hud-12-kd-count"); S("hud-12-kd-count", true);
                _log.AppendLine("UI_HUD knockdowns=" + st.knockdowns + " count after " + c + " ticks");
                Ticks(30); S("hud-13-kd-count2");
            }
            finally
            {
                UIMotion.ForceAnimate = false;
                Close();
            }
        }

        // Трещины на 75/50/25 % и разлёт шкалы соперника на нокауте (живой бой, движение включено).
        public static void CrackSeries()
        {
            _log = new System.Text.StringBuilder();
            _dir = Dir();
            UIMotion.ForceAnimate = true;
            try
            {
                ToFight();
                var hud = _app.Fight.Hud; var st = _app.Fight.State;
                Ticks(50);
                float[] at = { 0.74f, 0.49f, 0.24f };
                string[] nm = { "hud-20-crack-75", "hud-21-crack-50", "hud-22-crack-25" };
                for (int i = 0; i < 3; i++)
                {
                    st.hp = st.hpMax * at[i]; Ticks(3);
                    S(nm[i]); if (i == 2) S(nm[i], true);
                    _log.AppendLine("UI_HUD crack hp=" + F(at[i]) + " cracks=" + hud.CracksShown(true));
                    Ticks(20);
                }
                // добить: здоровье на донышке, сильный удар — нокаут
                int n = 0;
                for (int i = 0; i < 12 && !hud.Shattered(true); i++)
                {
                    TicksUntil(() => st.phase == FightPhase.Idle || st.phase == FightPhase.Guard || st.phase == FightPhase.Open || st.phase == FightPhase.Stagger || st.phase == FightPhase.Down, 200);
                    st.hp = Mathf.Min(st.hp, 2f);
                    Hit(PunchType.Hook, -1, 1.6f);
                    n = TicksUntil(() => hud.Shattered(true), 60);
                }
                _log.AppendLine("UI_HUD ko shattered=" + hud.Shattered(true) + " phase=" + st.phase + " shards=" + hud.LiveShards);
                _app.Tick(DT); S("hud-23-shatter-a");
                Ticks(5); S("hud-24-shatter-b"); S("hud-24-shatter-b", true);
                Ticks(12); S("hud-25-shatter-c");
                Ticks(30); S("hud-26-shatter-d");
                _log.AppendLine("UI_HUD ko +0.8s shards=" + hud.LiveShards);
            }
            finally
            {
                UIMotion.ForceAnimate = false;
                Close();
                LastLog = _log.ToString().TrimEnd();
            }
        }

        static void S(string name, bool land = false)
        {
            if (land) Snap(2400, 1080, name + "-l"); else Snap(1080, 2400, name + "-p");
            _app.SetViewSize(1080, 2400);
        }

        static string F(float v) => v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ Замер

        // Установившийся бой без сцены боя: только холст и HUD, синтетические данные — урон каждые 20 кадров,
        // выносливость тратится и копится, мега растёт. Время — Refresh+Tick HUD и UIMotion.Tick;
        // выделения — GC.GetAllocatedBytesForCurrentThread вокруг этих вызовов (после прогрева).
        public static void HudPerf(int frames = 1200)
        {
            _log = new System.Text.StringBuilder();
            UIMotion.ForceAnimate = true;
            var camGo = new GameObject("PerfCam"); var cam = camGo.AddComponent<Camera>();
            var rootGo = new GameObject("PerfUI"); var root = rootGo.AddComponent<UIRoot>(); root.Init(cam);
            try
            {
                var hud = new GameObject("FightHUD").AddComponent<FightHUD>();
                hud.Init(root);
                root.ShowScreen(ScreenId.Fight);
                _log.AppendLine(Measure("new", frames, d => hud.Refresh(d), () => hud.Tick(DT, DT)));
                _log.AppendLine("UI_HUD_PERF new floaters=" + hud.LiveFloaters + " sparks=" + hud.LiveSparks);
                _log.AppendLine(MeasureKo(hud));
                if (ExtraPerf != null) _log.AppendLine(ExtraPerf(root, frames));
            }
            finally
            {
                UIMotion.ForceAnimate = false;
                UIMotion.CompleteAll();
                Object.DestroyImmediate(rootGo); Object.DestroyImmediate(camGo);
                LastLog = _log.ToString().TrimEnd();
            }
        }

        // Для замера «до»: временный файл со старым HUD подставляет сюда свой прогон.
        public static Func<UIRoot, int, string> ExtraPerf;

        public static string Measure(string tag, int frames, Action<HudData> refresh, Action tick)
        {
            var d = new HudData { playerHpMax = 100f, foeHpMax = 200f, foeName = "Кирпич", playerName = "Игрок", staminaHitCost = 12f,
                                  playerHp = 100f, foeHp = 200f, playerStamina = 100f };
            for (int i = 0; i < 240; i++) Step(ref d, i, refresh, tick);   // прогрев: пулы, таблицы строк, JIT
            var sw = new Stopwatch();
            double total = 0, worst = 0; long alloc = 0; var all = new double[frames];
            for (int i = 0; i < frames; i++)
            {
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                Step(ref d, 240 + i, refresh, tick);
                Canvas.ForceUpdateCanvases();
                sw.Stop();
                alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                double ms = sw.Elapsed.TotalMilliseconds;
                total += ms; if (ms > worst) worst = ms; all[i] = ms;
            }
            Array.Sort(all);
            return "UI_HUD_PERF " + tag + " frames=" + frames + " avgMs=" + (total / frames).ToString("0.0000") + " medianMs=" + all[frames / 2].ToString("0.0000")
                   + " p95Ms=" + all[frames * 95 / 100].ToString("0.0000") + " worstMs=" + worst.ToString("0.000")
                   + " allocBytes=" + alloc + " (время включает перестройку холста Canvas.ForceUpdateCanvases)";
        }

        // Нокаут: шкала с тремя трещинами разлетается. Первый нокаут — прогрев (путь кода, пулы), затем «заново»;
        // замер — второй нокаут: 90 кадров с кадра разлёта, пик кадра и выделения.
        static string MeasureKo(FightHUD hud)
        {
            var d = new HudData { playerHpMax = 100f, foeHpMax = 200f, foeName = "Кирпич", playerName = "Игрок", staminaHitCost = 12f,
                                  playerHp = 60f, foeHp = 30f, playerStamina = 50f };
            var sw = new Stopwatch();
            double total = 0, worst = 0, first = 0; long alloc = 0; int cracks = 0, shards = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                hud.ResetTransient(); d.foeHp = 30f; d.foePhase = FightPhase.Idle;
                for (int i = 0; i < 120; i++) { hud.Refresh(d); hud.Tick(DT, DT); UIMotion.Tick(DT); }
                cracks = hud.CracksShown(true);
                d.foeHp = 0f; d.foePhase = FightPhase.KO;
                for (int i = 0; i < 90; i++)
                {
                    long a0 = GC.GetAllocatedBytesForCurrentThread();
                    sw.Restart();
                    hud.Refresh(d); hud.Tick(DT, DT); UIMotion.Tick(DT);
                    Canvas.ForceUpdateCanvases();
                    sw.Stop();
                    long da = GC.GetAllocatedBytesForCurrentThread() - a0;
                    if (pass == 0) continue;
                    alloc += da;
                    double ms = sw.Elapsed.TotalMilliseconds;
                    if (i == 0) { first = ms; shards = hud.LiveShards; }
                    total += ms; if (ms > worst) worst = ms;
                }
            }
            return "UI_HUD_PERF ko cracks=" + cracks + " shards=" + shards + " frames=90 avgMs=" + (total / 90).ToString("0.0000")
                   + " shatterFrameMs=" + first.ToString("0.000") + " peakMs=" + worst.ToString("0.000") + " allocBytes=" + alloc;
        }

        // Кадр HUD с синтетическими данными: урон каждые 20 кадров, выносливость тратится и копится, мега растёт.
        static void Step(ref HudData d, int i, Action<HudData> refresh, Action tick)
        {
            if (i % 20 == 0) { d.foeHp = Mathf.Max(5f, d.foeHp - 7f); d.playerStamina = Mathf.Max(0f, d.playerStamina - 12f); }
            if (i % 50 == 25) d.playerHp = Mathf.Max(10f, d.playerHp - 9f);
            if (d.foeHp <= 6f) d.foeHp = d.foeHpMax;
            if (d.playerHp <= 12f) d.playerHp = 100f;
            d.playerStamina = Mathf.Min(100f, d.playerStamina + 0.3f);
            d.megaMeter = (i * 0.25f) % 100f;
            d.foeStun01 = (i % 300) / 300f;
            d.fightTime = 30f + i / 60f;
            refresh(d);
            tick();
            UIMotion.Tick(DT);
        }
    }
}
