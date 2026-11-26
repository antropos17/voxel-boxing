// UiMotionShots.Hud.cs — серия кадров шкал боя с движением (UIMotion.ForceAnimate) и замер времени HUD.
// HudSeries(): выезд шкал, удар (до, пик, след, покой), мало здоровья, накал меги и «МЕГА ГОТОВ», нокдаун со счётом, уход.
// HudPerf(): время FightHUD.Refresh+Tick+UIMotion.Tick и выделения памяти HUD в установившемся бою.
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

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

        static void S(string name, bool land = false)
        {
            if (land) Snap(2400, 1080, name + "-l"); else Snap(1080, 2400, name + "-p");
            _app.SetViewSize(1080, 2400);
        }

        static string F(float v) => v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ Замер

        // Установившийся бой: N кадров с ударами каждые 20 кадров. Время — только HUD (Refresh+Tick) и UIMotion.Tick;
        // выделения — счётчик GC.GetAllocatedBytesForCurrentThread вокруг этих вызовов.
        public static void HudPerf(int frames = 600)
        {
            _log = new System.Text.StringBuilder();
            UIMotion.ForceAnimate = true;
            try
            {
                ToFight();
                Ticks(120);
                var hud = _app.Fight.Hud;
                var d = new HudData();
                var sw = new Stopwatch();
                double total = 0, worst = 0; long alloc = 0;
                var st = _app.Fight.State;
                d.playerHpMax = 100f; d.foeHpMax = st.hpMax; d.foeName = "Кирпич"; d.playerName = "Игрок"; d.staminaHitCost = 12f;
                d.playerHp = 100f; d.foeHp = st.hpMax; d.playerStamina = 100f; d.foeKnockdowns = 0;
                // прогрев: первые кадры создают строки таблиц и пулы
                for (int i = 0; i < 120; i++) Step(hud, ref d, i, null);
                for (int i = 0; i < frames; i++)
                {
                    long a0 = GC.GetAllocatedBytesForCurrentThread();
                    sw.Restart();
                    Step(hud, ref d, i, null);
                    sw.Stop();
                    alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                    double ms = sw.Elapsed.TotalMilliseconds;
                    total += ms; if (ms > worst) worst = ms;
                }
                _log.AppendLine("UI_HUD_PERF frames=" + frames + " avgMs=" + total / frames + " worstMs=" + worst + " allocBytes=" + alloc
                                + " floaters=" + hud.LiveFloaters + " sparks=" + hud.LiveSparks);
            }
            finally
            {
                UIMotion.ForceAnimate = false;
                Close();
            }
        }

        // Кадр HUD с синтетическими данными: урон каждые 20 кадров, выносливость тратится и копится, мега растёт.
        static void Step(FightHUD hud, ref HudData d, int i, object _)
        {
            if (i % 20 == 0) { d.foeHp = Mathf.Max(5f, d.foeHp - 7f); d.playerStamina = Mathf.Max(0f, d.playerStamina - 12f); }
            if (i % 50 == 25) d.playerHp = Mathf.Max(10f, d.playerHp - 9f);
            if (d.foeHp <= 6f) d.foeHp = d.foeHpMax;
            if (d.playerHp <= 12f) d.playerHp = 100f;
            d.playerStamina = Mathf.Min(100f, d.playerStamina + 0.3f);
            d.megaMeter = (i * 0.25f) % 100f;
            d.foeStun01 = (i % 300) / 300f;
            d.fightTime = 30f + i / 60f;
            hud.Refresh(d);
            hud.Tick(DT, DT);
            UIMotion.Tick(DT);
        }
    }
}
