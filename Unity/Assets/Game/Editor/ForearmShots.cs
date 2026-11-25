// ForearmShots.cs — рука игрока от первого лица «без | с» поля зрения рук (PlayerGloves.ViewFix false / true).
// Бой — настоящий FightDirector, удары и блок — через InputReader.Inject и кнопку HUD, как в Spring Shots.
// Итог: Unity/Shots/forearm/<момент>-p.png (слева «без», справа «с»), <момент>-l.png (сверху «без», снизу «с»), sheet.png;
// доля кадра под рукой — в LastLog (FOREARM_AREA): вся рука (плечо + предплечье + перчатка) и только плечо с предплечьем.
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
        public static string ForearmLog = "";
        static readonly string[] ForearmMoments = { "rest", "jab", "hook", "upper", "block" };
        const int P16W = 540, P16H = 960;   // телефон 9:16 — только замер площади

        // Подбор: только площадь «с» (без снимков), портрет 9:19,5, все моменты в одну строку
        public static string ForearmSweep()
        {
            var log = new StringBuilder();
            string tmp = Path.Combine(Path.GetTempPath(), "vox-forearm-sweep");
            Directory.CreateDirectory(tmp);
            _sweep = true;
            try { ForearmFight(tmp, "on", log); } finally { _sweep = false; }
            return log.ToString();
        }
        static bool _sweep;

        [MenuItem("VoxGame/Forearm Shots")]
        public static void ForearmShots()
        {
            bool was = PlayerGloves.ViewFix;
            var log = new StringBuilder();
            string tmp = Path.Combine(Path.GetTempPath(), "vox-forearm-shots");
            Directory.CreateDirectory(tmp);
            try
            {
                foreach (bool on in new[] { false, true })
                {
                    PlayerGloves.ViewFix = on;
                    ForearmFight(tmp, on ? "on" : "off", log);
                }
            }
            finally { PlayerGloves.ViewFix = was; }

            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "forearm"));
            Directory.CreateDirectory(dir);
            foreach (var m in ForearmMoments)
            {
                Combine(Path.Combine(tmp, m + "-off-p.png"), Path.Combine(tmp, m + "-on-p.png"), Path.Combine(dir, m + "-p.png"), true);
                Combine(Path.Combine(tmp, m + "-off-l.png"), Path.Combine(tmp, m + "-on-l.png"), Path.Combine(dir, m + "-l.png"), false);
            }
            ForearmSheet(tmp, Path.Combine(dir, "sheet.png"));
            ForearmLog = log.ToString();
            Debug.Log(ForearmLog);
        }

        static void ForearmFight(string tmp, string tag, StringBuilder log)
        {
            var d = NewFight(out var cam, out var ui, 6f, 4f);
            var run = new Run { d = d };
            run.Ticks(90);
            ForearmMoment(d, cam, ui, tmp, "rest", tag, log);

            ForearmPunch(run, PunchType.Jab, 1);
            ForearmMoment(d, cam, ui, tmp, "jab", tag, log);
            ForearmPunch(run, PunchType.Hook, -1);
            ForearmMoment(d, cam, ui, tmp, "hook", tag, log);
            ForearmPunch(run, PunchType.Upper, 1);
            ForearmMoment(d, cam, ui, tmp, "upper", tag, log);

            // блок кнопкой HUD: перчатки дошли до позы блока
            run.Ticks(40);
            run.defend = false;
            d.Hud.PressBlock();
            run.Ticks(20);
            ForearmMoment(d, cam, ui, tmp, "block", tag, log);
            d.Hud.PressBlock();
            EndFight(d);
        }

        // Удар и ожидание пика: вылет руки дошёл до 1
        static void ForearmPunch(Run run, PunchType kind, int side)
        {
            run.Ticks(40);
            run.WaitOpen(200);
            run.Punch(kind, side);
            int s = side < 0 ? 0 : 1;
            for (int i = 0; i < 30; i++) { run.Tick(); if (run.d.Gloves.Extension(s) >= 1f) break; }
        }

        static void ForearmMoment(FightDirector d, Camera cam, UIRoot ui, string tmp, string moment, string tag, StringBuilder log)
        {
            if (_sweep)
            {
                ArmArea(cam, d, PortW, PortH, out float a0, out float r0);
                log.Append(moment + " " + (a0 * 100f).ToString("0.0") + "/" + (r0 * 100f).ToString("0.0") + "  ");
                return;
            }
            Pair(d, cam, ui, tmp, moment + "-" + tag);
            var sb = new StringBuilder("FOREARM_AREA " + tag + " " + moment);
            foreach (var v in new[] { (PortW, PortH, "9:19.5"), (P16W, P16H, "9:16"), (LandW, LandH, "land") })
            {
                d.SetViewSize(v.Item1, v.Item2);
                ArmArea(cam, d, v.Item1, v.Item2, out float all, out float arm);
                sb.Append(" " + v.Item3 + " all=" + (all * 100f).ToString("0.0") + "% arm=" + (arm * 100f).ToString("0.0") + "% k="
                          + d.Gloves.ArmScreenK.ToString("0.00"));
            }
            d.SetViewSize(PortW, PortH);
            log.AppendLine(sb.ToString());
        }

        // Доля кадра под рукой: кадр камеры с руками, без плеча и предплечья, без рук целиком — пиксели, которые различаются.
        static void ArmArea(Camera cam, FightDirector d, int w, int h, out float all, out float arm)
        {
            var parts = new List<GameObject>();
            foreach (var t in cam.GetComponentsInChildren<Transform>(false))
                if (t.name.StartsWith("playerUpperArm") || t.name.StartsWith("playerForeArm")) parts.Add(t.gameObject);
            var full = Grab(cam, w, h);
            foreach (var p in parts) p.SetActive(false);
            var noArm = Grab(cam, w, h);
            d.Gloves.SetVisible(false);
            var none = Grab(cam, w, h);
            d.Gloves.SetVisible(true);
            foreach (var p in parts) p.SetActive(true);
            int na = 0, nr = 0;
            for (int i = 0; i < full.Length; i++)
            {
                if (!Same(full[i], none[i])) na++;
                if (!Same(full[i], noArm[i])) nr++;
            }
            all = na / (float)full.Length; arm = nr / (float)full.Length;
        }

        static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        static Color32[] Grab(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; RenderTexture.active = rt;
            cam.Render();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            cam.targetTexture = null; RenderTexture.active = null;
            rt.Release(); Object.DestroyImmediate(rt);
            return px;
        }

        // Сводный лист: сверху портреты «без | с» по моментам, снизу ландшафты «без» над «с»; всё в половину размера.
        static void ForearmSheet(string tmp, string outPath)
        {
            const int gap = 10, half = 2;
            int pw = PortW / half, ph = PortH / half, lw = LandW / half, lh = LandH / half;
            int n = ForearmMoments.Length;
            int w1 = n * (2 * pw + gap) - gap, w2 = n * (lw + gap) - gap;
            int W = Mathf.Max(w1, w2), H = ph + gap + 2 * lh + gap;
            var o = new Texture2D(W, H, TextureFormat.RGB24, false);
            var fill = new Color32[W * H];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(230, 230, 230, 255);
            o.SetPixels32(fill);
            for (int m = 0; m < n; m++)
            {
                string mm = ForearmMoments[m];
                Blit(o, Path.Combine(tmp, mm + "-off-p.png"), m * (2 * pw + gap), H - ph, pw, ph);
                Blit(o, Path.Combine(tmp, mm + "-on-p.png"), m * (2 * pw + gap) + pw, H - ph, pw, ph);
                Blit(o, Path.Combine(tmp, mm + "-off-l.png"), m * (lw + gap), lh, lw, lh);
                Blit(o, Path.Combine(tmp, mm + "-on-l.png"), m * (lw + gap), 0, lw, lh);
            }
            o.Apply();
            File.WriteAllBytes(outPath, o.EncodeToPNG());
            Object.DestroyImmediate(o);
        }

        static void Blit(Texture2D o, string path, int x0, int y0, int w, int h)
        {
            if (!File.Exists(path)) return;
            var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(path));
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = t.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
            o.SetPixels(x0, y0, w, h, px);
            Object.DestroyImmediate(t);
        }
    }
}
