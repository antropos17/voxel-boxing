// SpringShots.cs — снимки пружинности и рук парами «без | с» (FoeSpring.Enabled false / true).
// Бой — настоящий FightDirector, удары и блок — через InputReader.Inject и кнопки HUD, как в FightShots W1.
// Итог: Unity/Shots/spring/<момент>-p.png (слева «без», справа «с») и <момент>-l.png (сверху «без», снизу «с»), лог — LastLog.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VoxProbe;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class ShotsTaker
    {
        public static string SpringLog = "";
        public static bool SpringOnlyOn;   // подбор: снимать только «с» (без склейки пар)
        static readonly string[] SpringMoments = { "stance", "hit", "hit-0.2s", "punch", "block", "victory" };

        [MenuItem("VoxGame/Spring Shots")]
        public static void SpringShots()
        {
            bool was = FoeSpring.Enabled;
            var log = new StringBuilder();
            string tmp = Path.Combine(Path.GetTempPath(), "vox-spring-shots");
            Directory.CreateDirectory(tmp);
            try
            {
                foreach (bool on in SpringOnlyOn ? new[] { true } : new[] { false, true })
                {
                    FoeSpring.Enabled = on;
                    string tag = on ? "on" : "off";
                    SpringFight(tmp, tag, log);
                    SpringVictory(tmp, tag, log);
                }
            }
            finally { FoeSpring.Enabled = was; }

            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "spring"));
            Directory.CreateDirectory(dir);
            foreach (var m in SpringMoments)
            {
                Combine(Path.Combine(tmp, m + "-off-p.png"), Path.Combine(tmp, m + "-on-p.png"), Path.Combine(dir, m + "-p.png"), true);
                Combine(Path.Combine(tmp, m + "-off-l.png"), Path.Combine(tmp, m + "-on-l.png"), Path.Combine(dir, m + "-l.png"), false);
            }
            SpringLog = log.ToString();
            Debug.Log(SpringLog);
        }

        static void SpringPair(FightDirector d, Camera cam, UIRoot ui, string tmp, string moment, string tag, StringBuilder log)
        {
            Pair(d, cam, ui, tmp, moment + "-" + tag);
            var b = d.FoeFigure;
            log.AppendLine("SPRING " + tag + " " + moment + " phase=" + d.State.phase
                           + " head=" + F(b.spring.head.magnitude * 100f) + "cm root=" + F(b.spring.root.magnitude * 100f)
                           + "cm lean=" + F(b.spring.lean) + " twist=" + F(b.spring.twist)
                           + " neck=" + F(Unity.Mathematics.math.length(AnatSkin.recoil) * Mathf.Rad2Deg) + "deg"
                           + " glove=" + F(b.spring.glove[0].magnitude * 100f) + "/" + F(b.spring.glove[1].magnitude * 100f) + "cm");
        }

        static void SpringFight(string tmp, string tag, StringBuilder log)
        {
            var d = NewFight(out var cam, out var ui, 4f, 3f);
            var run = new Run { d = d };
            run.Ticks(90);
            SpringPair(d, cam, ui, tmp, "stance", tag, log);

            // силовой хук левой: момент удара (через 5 кадров — пик отдачи головы) и через 0,2 с после него
            run.WaitOpen(150);
            int seq = d.hitFrameSeq;
            run.Punch(PunchType.Hook, -1, 1.35f);
            run.Tick();
            log.AppendLine("SPRING " + tag + " hookLanded=" + (d.hitFrameSeq != seq));
            run.Ticks(4);
            SpringPair(d, cam, ui, tmp, "hit", tag, log);
            run.Ticks(12);
            SpringPair(d, cam, ui, tmp, "hit-0.2s", tag, log);

            // прямой правой: перчатка в крайней точке
            run.Ticks(30);
            run.WaitOpen(150);
            run.Punch(PunchType.Jab, 1);
            run.Ticks(3);
            SpringPair(d, cam, ui, tmp, "punch", tag, log);

            // блок кнопкой HUD: снимок в ударе соперника по перчаткам
            run.Ticks(30);
            run.defend = false;
            d.Hud.PressBlock();
            int w = 0;
            while (d.State.phase != FightPhase.Strike && w < 900 && !d.Finished) { run.Tick(); w++; }
            run.Ticks(3);
            SpringPair(d, cam, ui, tmp, "block", tag, log);
            d.Hud.PressBlock();
            run.defend = true;
            EndFight(d);
        }

        static void SpringVictory(string tmp, string tag, StringBuilder log)
        {
            var d = NewFight(out var cam, out var ui, 0.45f, 0.5f);
            var run = new Run { d = d };
            run.Ticks(60);
            var combo = new[] { PunchType.Hook, PunchType.Upper, PunchType.Jab, PunchType.Hook };
            int punches = 0;
            while (!d.Finished && punches < 400)
            {
                run.WaitOpen(200);
                if (d.State.phase == FightPhase.Down || d.State.phase == FightPhase.Rise) { run.Tick(); continue; }
                run.Punch(combo[punches % combo.Length], punches % 2 == 0 ? 1 : -1, 1.35f);
                punches++;
                run.Ticks(18);
            }
            int w = 0;
            while (w < 1500 && !(d.Victory.State == VictoryState.Cheer && d.Victory.Progress >= 0.6f)) { run.Tick(); w++; }
            log.AppendLine("SPRING " + tag + " victory punches=" + punches + " won=" + d.PlayerWon + " state=" + d.Victory.State
                           + " gripping=" + d.RefereeFigure.IsGripping);
            SpringPair(d, cam, ui, tmp, "victory", tag, log);
            EndFight(d);
        }

        // Парный замер кадра удара: бои «без» и «с» по очереди (без, с, без, с), в каждом 3 удара на прогрев (JIT, Burst)
        // и hits ударов в зачёт; кадр удара — FightDirector.lastHitFrameMs. Вклад пружин — отдельно: толчок и шаг тела.
        public static void SpringBench(int hits = 40)
        {
            bool was = FoeSpring.Enabled;
            var log = new StringBuilder();
            var all = new Dictionary<bool, List<double>> { { false, new List<double>() }, { true, new List<double>() } };
            var anat = new Dictionary<bool, List<double>> { { false, new List<double>() }, { true, new List<double>() } };
            var pieces = new Dictionary<bool, List<double>> { { false, new List<double>() }, { true, new List<double>() } };
            try
            {
                foreach (bool on in new[] { false, true, false, true })
                {
                    FoeSpring.Enabled = on; AnatPieces.Tethers = on;
                    var d = NewFight(out var cam, out var ui, 6f, 4f);
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
                        if (d.hitFrameSeq != seq)
                        {
                            if (got >= WarmHits) { all[on].Add(d.lastHitFrameMs); anat[on].Add(d.lastAnatTickMs + d.lastHitMs + d.lastPickMs); }
                            got++;
                        }
                        for (int i = 0; i < 20; i++) { run.Tick(); pieces[on].Add(d.Head.pieceMs); }
                    }
                    log.AppendLine("SPRING_BENCH run on=" + on + " hits=" + (got - WarmHits) + " finished=" + d.Finished);
                    EndFight(d);
                }
                // вклад пружин: толчок и тик тела (с пружинами минус без) на одном теле, 2000 повторов
                var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
                var root = new GameObject("bench");
                var body = new FoeBody(root.transform, FighterGen.Generate(42u, 1), mat);
                var cmd = new AICommand { newPhase = FightPhase.Guard, side = -1 };
                double[] ms = new double[2];
                for (int pass = 0; pass < 3; pass++)
                    foreach (bool on in new[] { false, true })
                    {
                        FoeSpring.Enabled = on;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        for (int i = 0; i < 2000; i++)
                        {
                            body.AddHit(body.headMount.position + body.root.forward * 0.1f, -body.root.forward, i % 3, 1.2f);
                            body.Tick(cmd, 1f / 60f, Vector3.forward);
                        }
                        if (pass > 0) ms[on ? 1 : 0] += sw.Elapsed.TotalMilliseconds / 4000.0;
                    }
                body.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(mat);
                log.AppendLine("SPRING_BENCH body_tick_plus_hit_ms off=" + ms[0].ToString("0.0000") + " on=" + ms[1].ToString("0.0000")
                               + " spring_contrib_ms=" + (ms[1] - ms[0]).ToString("0.0000"));
            }
            finally { FoeSpring.Enabled = was; AnatPieces.Tethers = true; }
            foreach (bool on in new[] { false, true })
                log.AppendLine("SPRING_BENCH hitFrame on=" + on + " " + Dist(all[on]) + " | anat(pick+hit+tick) " + Dist(anat[on])
                               + " | piecesStep " + Dist(pieces[on]));
            SpringLog = log.ToString();
            Debug.Log(SpringLog);
        }

        static string Dist(List<double> x)
        {
            if (x.Count == 0) return "n=0";
            var s = new List<double>(x); s.Sort();
            double sum = 0; foreach (var v in s) sum += v;
            return "n=" + s.Count + " avg=" + (sum / s.Count).ToString("0.000") + " med=" + s[s.Count / 2].ToString("0.000")
                 + " p90=" + s[(int)(s.Count * 0.9)].ToString("0.000") + " max=" + s[s.Count - 1].ToString("0.000");
        }

        // Два снимка в один: портрет — рядом (слева a), ландшафт — друг под другом (сверху a); между ними светлая полоса.
        static void Combine(string a, string b, string outPath, bool sideBySide)
        {
            if (!File.Exists(a) || !File.Exists(b)) return;
            var ta = new Texture2D(2, 2); ta.LoadImage(File.ReadAllBytes(a));
            var tb = new Texture2D(2, 2); tb.LoadImage(File.ReadAllBytes(b));
            const int gap = 8;
            int w = sideBySide ? ta.width + gap + tb.width : ta.width, h = sideBySide ? ta.height : ta.height + gap + tb.height;
            var o = new Texture2D(w, h, TextureFormat.RGB24, false);
            var fill = new Color32[w * h];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(230, 230, 230, 255);
            o.SetPixels32(fill);
            if (sideBySide) { o.SetPixels(0, 0, ta.width, ta.height, ta.GetPixels()); o.SetPixels(ta.width + gap, 0, tb.width, tb.height, tb.GetPixels()); }
            else { o.SetPixels(0, tb.height + gap, ta.width, ta.height, ta.GetPixels()); o.SetPixels(0, 0, tb.width, tb.height, tb.GetPixels()); }
            o.Apply();
            File.WriteAllBytes(outPath, o.EncodeToPNG());
            Object.DestroyImmediate(ta); Object.DestroyImmediate(tb); Object.DestroyImmediate(o);
        }
    }
}
