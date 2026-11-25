// HitFxShots.cs — эффекты удара по образцу веб-версии (Unity/HIT-FX.md): снимки «без | с» (FxDirector.Visuals false / true),
// лист «веб-эталон | Unity стало», парный замер кадра удара и прогон боя для проверки, что эффекты не меняют исход.
// Бой — настоящий FightDirector, удары — InputReader.Inject, как в FightShots W1 и SpringShots.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class ShotsTaker
    {
        public static string HitFxLog = "";
        static readonly string[] HitFxMoments = { "jab", "upper", "mega", "knockdown" };

        [MenuItem("VoxGame/Hit Fx Shots")]
        public static void HitFxShots() => HitFxShots(null);

        // webRef — снимок веб-версии для листа сравнения (shots/m2-fight.png основной папки); null — без листа.
        public static void HitFxShots(string webRef)
        {
            bool was = FxDirector.Visuals;
            var log = new StringBuilder();
            string tmp = Path.Combine(Path.GetTempPath(), "vox-hitfx-shots");
            Directory.CreateDirectory(tmp);
            try
            {
                foreach (bool on in new[] { false, true })
                {
                    FxDirector.Visuals = on;
                    HitFxFight(tmp, on ? "on" : "off", log);
                    HitFxKnockdown(tmp, on ? "on" : "off", log);
                }
            }
            finally { FxDirector.Visuals = was; }

            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "hit-fx"));
            Directory.CreateDirectory(dir);
            foreach (var m in HitFxMoments)
            {
                Combine(Path.Combine(tmp, m + "-off-p.png"), Path.Combine(tmp, m + "-on-p.png"), Path.Combine(dir, m + "-p.png"), true);
                Combine(Path.Combine(tmp, m + "-off-l.png"), Path.Combine(tmp, m + "-on-l.png"), Path.Combine(dir, m + "-l.png"), false);
            }
            if (webRef != null && File.Exists(webRef))
            {
                SideBySideH(webRef, Path.Combine(tmp, "upper-on-p.png"), Path.Combine(tmp, "mega-on-p.png"), Path.Combine(dir, "web-vs-unity.png"));
                log.AppendLine("HITFX sheet=web-vs-unity.png ref=" + webRef);
            }
            HitFxLog = log.ToString();
            Debug.Log(HitFxLog);
        }

        static void HitFxPair(FightDirector d, Camera cam, UIRoot ui, string tmp, string moment, string tag, StringBuilder log)
        {
            Pair(d, cam, ui, tmp, moment + "-" + tag);
            var fx = d.Fx;
            log.AppendLine("HITFX " + tag + " " + moment + " phase=" + d.State.phase + " hp=" + F(d.State.hp) + " landed=" + d.HitsLanded
                           + (fx.Particles != null
                               ? " parts=" + fx.Particles.AliveCount + " blood=" + fx.Particles.Count(FxKind.Blood) + " rings=" + fx.Rings.Active
                                 + " pop=" + fx.Pops.LastPopText + " banner=" + fx.Banners.LastBannerText + " combo=" + fx.Pops.ComboShown
                                 + " chips=" + fx.Pops.ActiveChips + " lines=" + F(fx.Overlay.LinesAlpha)
                               : "")
                           + " timeScale=" + F(fx.TimeScale) + " fovKick=" + F(fx.FovKick));
        }

        // Джеб, апперкот, мега — крепкий соперник (как W3), кадр в пик эффекта: кольца раскрыты, брызги летят.
        static void HitFxFight(string tmp, string tag, StringBuilder log)
        {
            var d = NewFight(out var cam, out var ui, 4f, 3f);
            var run = new Run { d = d };
            run.Ticks(90);

            run.WaitOpen(150);
            PunchAndWaitHit(run, PunchType.Jab, -1, 1.1f);
            run.Ticks(3);
            HitFxPair(d, cam, ui, tmp, "jab", tag, log);
            run.Ticks(60);

            // серия до апперкота: счётчик комбо на снимке
            run.WaitOpen(150);
            PunchAndWaitHit(run, PunchType.Hook, 1, 1.3f);
            run.Ticks(8);
            PunchAndWaitHit(run, PunchType.Upper, 1, 1.4f);
            run.Ticks(9);
            HitFxPair(d, cam, ui, tmp, "upper", tag, log);
            run.Ticks(60);

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
            PunchAndWaitHit(run, PunchType.Hook, 1, 1.35f);
            run.Ticks(10);
            HitFxPair(d, cam, ui, tmp, "mega", tag, log);
            EndFight(d);
        }

        // Нокдаун: слабый соперник, удары до первого нокдауна, кадр сразу после удара, который уронил.
        static void HitFxKnockdown(string tmp, string tag, StringBuilder log)
        {
            var d = NewFight(out var cam, out var ui, 0.6f, 0.5f);
            var run = new Run { d = d };
            run.Ticks(60);
            var combo = new[] { PunchType.Hook, PunchType.Upper, PunchType.Jab, PunchType.Hook };
            int punches = 0;
            while (d.State.knockdowns == 0 && !d.Finished && punches < 200)
            {
                run.WaitOpen(150);
                var t = d.State.megaReady ? PunchType.Jab : combo[punches % combo.Length];
                PunchAndWaitHit(run, t, punches % 2 == 0 ? 1 : -1, 1.35f);
                punches++;
                if (d.State.knockdowns > 0) break;
                run.Ticks(14);
            }
            run.Ticks(9);
            log.AppendLine("HITFX " + tag + " knockdown punches=" + punches + " kd=" + d.State.knockdowns);
            HitFxPair(d, cam, ui, tmp, "knockdown", tag, log);
            EndFight(d);
        }

        // Прогон боя для теста «эффекты не меняют исход»: тот же сценарий ударов, итог — строка состояния боя.
        public static string HitFxOutcome(bool visuals, int punches)
        {
            bool was = FxDirector.Visuals;
            FxDirector.Visuals = visuals;
            try
            {
                var d = NewFight(out _, out _, 1f, 1f);
                var run = new Run { d = d };
                run.Ticks(60);
                var combo = new[] { PunchType.Hook, PunchType.Jab, PunchType.Upper, PunchType.Jab, PunchType.Hook };
                for (int i = 0; i < punches && !d.Finished; i++)
                {
                    run.WaitOpen(150);
                    run.Punch(combo[i % combo.Length], i % 2 == 0 ? 1 : -1, 1.3f);
                    run.Ticks(16);
                }
                run.Ticks(120);
                var s = d.State;
                var v = d.Head;
                string r = "hp=" + s.hp.ToString("R") + " playerHp=" + s.playerHp.ToString("R") + " phase=" + s.phase + " kd=" + s.knockdowns
                         + " landed=" + d.HitsLanded + " headHits=" + d.HeadHits + " removed=" + v.sim.removed + " fractures=" + v.sim.fractures.Count
                         + " detached=" + v.detachedBodies + " mega=" + s.megaMeter.ToString("R") + " finished=" + d.Finished + " won=" + d.PlayerWon;
                EndFight(d);
                return r;
            }
            finally { FxDirector.Visuals = was; }
        }

        // Парный замер кадра удара «без | с» эффектами: бои по очереди (без, с, без, с), 3 удара на прогрев, hits в зачёт.
        // Кадр удара — lastHitFrameMs; вклад эффектов — lastFxMs (Tick эффектов + OnPlayerHit) в кадре удара
        // и время Tick эффектов в 20 кадрах после удара (частицы летят).
        public static void HitFxBench(int hits = 40)
        {
            bool was = FxDirector.Visuals;
            var log = new StringBuilder();
            var frame = new Dictionary<bool, List<double>> { { false, new List<double>() }, { true, new List<double>() } };
            var fxHit = new Dictionary<bool, List<double>> { { false, new List<double>() }, { true, new List<double>() } };
            var fxAfter = new Dictionary<bool, List<double>> { { false, new List<double>() }, { true, new List<double>() } };
            var parts = new Dictionary<bool, int> { { false, 0 }, { true, 0 } };
            try
            {
                foreach (bool on in new[] { false, true, false, true })
                {
                    FxDirector.Visuals = on;
                    var d = NewFight(out _, out _, 6f, 4f);
                    var run = new Run { d = d };
                    run.Ticks(90);
                    var combo = new[] { PunchType.Hook, PunchType.Jab, PunchType.Upper, PunchType.Jab };
                    int got = 0, tries = 0;
                    while (got < hits + WarmHits && tries < hits * 4 && !d.Finished)
                    {
                        tries++;
                        run.WaitOpen(150);
                        int seq = d.hitFrameSeq;
                        run.Punch(combo[tries % combo.Length], tries % 2 == 0 ? 1 : -1);
                        run.Tick();
                        bool count = d.hitFrameSeq != seq && got >= WarmHits;
                        if (count) { frame[on].Add(d.lastHitFrameMs); fxHit[on].Add(d.lastFxMs); }
                        if (d.hitFrameSeq != seq) got++;
                        for (int i = 0; i < 20; i++)
                        {
                            run.Tick();
                            if (count) fxAfter[on].Add(d.lastFxTickMs);
                        }
                        if (on && d.Fx.Particles != null) parts[on] = Mathf.Max(parts[on], d.Fx.Particles.AliveCount);
                    }
                    log.AppendLine("HITFX_BENCH run on=" + on + " hits=" + (got - WarmHits) + " finished=" + d.Finished);
                    EndFight(d);
                }
            }
            finally { FxDirector.Visuals = was; }
            foreach (bool on in new[] { false, true })
                log.AppendLine("HITFX_BENCH on=" + on + " hitFrame " + Dist(frame[on]) + " | fxInHitFrame " + Dist(fxHit[on])
                               + " | fxTickAfter " + Dist(fxAfter[on]));
            log.AppendLine("HITFX_BENCH peakParticles=" + parts[true] + " max=" + FxParticles.MaxParticles);
            HitFxLog = log.ToString();
            Debug.Log(HitFxLog);
        }

        // Лист: веб-эталон слева, два кадра Unity справа, всё приведено к высоте портрета Unity.
        static void SideBySideH(string web, string a, string b, string outPath)
        {
            var tw = Load(web); var ta = Load(a); var tb = Load(b);
            if (tw == null || ta == null || tb == null) return;
            int h = ta.height, ww = Mathf.RoundToInt(tw.width * (float)h / tw.height);
            const int gap = 12;
            int w = ww + gap + ta.width + gap + tb.width;
            var o = new Texture2D(w, h, TextureFormat.RGB24, false);
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.9f, 0.9f, 0.9f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < ww; x++)
                px[x + y * w] = tw.GetPixelBilinear((x + 0.5f) / ww, (y + 0.5f) / h);
            Blit(px, w, ta, ww + gap);
            Blit(px, w, tb, ww + gap + ta.width + gap);
            o.SetPixels(px); o.Apply();
            File.WriteAllBytes(outPath, o.EncodeToPNG());
            Object.DestroyImmediate(tw); Object.DestroyImmediate(ta); Object.DestroyImmediate(tb); Object.DestroyImmediate(o);
        }

        static Texture2D Load(string path)
        {
            if (!File.Exists(path)) return null;
            var t = new Texture2D(2, 2);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        static void Blit(Color[] px, int w, Texture2D t, int x0)
        {
            var src = t.GetPixels();
            for (int y = 0; y < t.height; y++)
            for (int x = 0; x < t.width; x++)
                px[x0 + x + y * w] = src[x + y * t.width];
        }
    }
}
