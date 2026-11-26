// ArmsBodyShots.cs — руки игрока и соперника, тело соперника (ARMS-BODY.md): снимки, треугольники по частям, доля белой
// манжеты в пике джеба, точки удара, время сборки бойца и кадра боя. Снимки — Unity/Shots/arms-body/ (prefix "before-"
// для «до»), числа — LastLog и Unity/Logs/arms-<tag>.txt. Эффекты удара на снимках выключены (FxDirector.Visuals),
// чтобы кольца не закрывали перчатку; в замере времени включены.
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class ShotsTaker
    {
        public static string ArmsLog = "";
        const int BPW = 1080, BPH = 2400;

        public static void ArmsBodyBefore() => ArmsBody("before-", "before");
        public static void ArmsBodyAfter()  => ArmsBody("", "after");
        public static void ArmsBenchBefore() => ArmsBench("before");
        public static void ArmsBenchAfter()  => ArmsBench("after");

        static string ArmsDir()
        {
            var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "arms-body"));
            Directory.CreateDirectory(d);
            return d;
        }

        static void ArmsSave(string tag, StringBuilder log)
        {
            ArmsLog = log.ToString();
            var p = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "arms-" + tag + ".txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, ArmsLog);
            Debug.Log(ArmsLog);
        }

        public static void ArmsBody(string prefix, string tag)
        {
            var dir = ArmsDir();
            var log = new StringBuilder();
            bool was = FxDirector.Visuals;
            FxDirector.Visuals = false;
            try
            {
                var d = NewFight(out var cam, out var ui, 6f, 4f);
                var run = new Run { d = d };
                run.Ticks(90);
                ArmsTris(d, cam, log);
                BigShots(d, cam, ui, dir, prefix + "guard", true);

                foreach (var (kind, side, name) in new[] { (PunchType.Jab, 1, "jab"), (PunchType.Hook, -1, "hook"), (PunchType.Upper, 1, "upper") })
                {
                    int hits = d.HeadHits;
                    d.SetViewSize(BPW, BPH);
                    ForearmPunch(run, kind, side);
                    int s = side < 0 ? 0 : 1;
                    var g = PG(cam, s).position;
                    var vp = cam.WorldToViewportPoint(g);
                    var hp = cam.WorldToViewportPoint((Vector3)d.lastHitPoint);
                    log.AppendLine("ARMS_HIT " + name + " hit=" + (d.HeadHits > hits) + " glove=" + V3(g) + " gloveVP=" + V2(vp)
                                   + " hitPt=" + V3((Vector3)d.lastHitPoint) + " hitVP=" + V2(hp));
                    if (name == "jab") WhiteCuff(d, cam, s, log);
                    BigShots(d, cam, ui, dir, prefix + name, name == "jab");
                }
                EndFight(d);

                // крупные планы
                d = NewFight(out cam, out ui, 6f, 4f);
                run = new Run { d = d };
                run.Ticks(90);
                ui.gameObject.SetActive(false);
                Closeups(d, cam, dir, prefix);
                d.SetViewSize(LandW, LandH);
                run.Ticks(40);
                GloveCloseup(d, cam, Path.Combine(dir, prefix + "player-glove.png"));
                EndFight(d);

                // три бойца ростера в стойке (только «после»)
                if (prefix == "")
                {
                    int n = 0;
                    foreach (int fi in new[] { 2, 5, 9 })
                    {
                        d = NewFight(out cam, out ui, 6f, 4f, fi);
                        run = new Run { d = d };
                        run.Ticks(90);
                        d.SetViewSize(BPW, BPH);
                        Shot(cam, ui, BPW, BPH, Path.Combine(dir, "roster-" + (++n) + ".png"));
                        log.AppendLine("ARMS_ROSTER " + n + " idx=" + fi);
                        EndFight(d);
                    }
                }
            }
            finally { FxDirector.Visuals = was; }
            ArmsSave(tag, log);
        }

        static Transform PG(Camera cam, int s) => FindDeep(cam.transform, "playerGlove" + s);

        static string V3(Vector3 v) => "(" + v.x.ToString("0.0000", Inv) + "," + v.y.ToString("0.0000", Inv) + "," + v.z.ToString("0.0000", Inv) + ")";
        static string V2(Vector3 v) => "(" + v.x.ToString("0.0000", Inv) + "," + v.y.ToString("0.0000", Inv) + ")";

        static void BigShots(FightDirector d, Camera cam, UIRoot ui, string dir, string name, bool land)
        {
            d.SetViewSize(BPW, BPH);
            Shot(cam, ui, BPW, BPH, Path.Combine(dir, name + ".png"));
            if (land)
            {
                d.SetViewSize(2400, 1080);
                Shot(cam, ui, 2400, 1080, Path.Combine(dir, name + "-land.png"));
            }
            d.SetViewSize(BPW, BPH);
        }

        static int Tris(Transform t)
        {
            var mf = t != null ? t.GetComponent<MeshFilter>() : null;
            return mf != null && mf.sharedMesh != null ? (int)(mf.sharedMesh.GetIndexCount(0) / 3) : 0;
        }

        static Transform FindDeep(Transform r, string name)
        {
            foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        // Треугольники по частям и всего в кадре (все включённые MeshRenderer сцены; толпа рисуется инстансами и не входит).
        static void ArmsTris(FightDirector d, Camera c, StringBuilder log)
        {
            var cam = c.transform;
            var fr = d.FoeFigure.root;
            int pu = Tris(FindDeep(cam, "playerUpperArm0")), pf = Tris(FindDeep(cam, "playerForeArm0")), pg = Tris(FindDeep(cam, "playerGlove0"));
            int fu = Tris(FindDeep(fr, "upperArm0")), ff = Tris(FindDeep(fr, "foreArm0")), fg = Tris(FindDeep(fr, "glove0"));
            int ft = Tris(FindDeep(fr, "torso")), fl = Tris(FindDeep(fr, "legs"));
            long all = 0;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (mr.enabled && mr.gameObject.activeInHierarchy) all += Tris(mr.transform);
            log.AppendLine("ARMS_TRIS playerUpper=" + pu + " playerFore=" + pf + " playerGlove=" + pg + " playerArm=" + (pu + pf + pg)
                           + " foeUpper=" + fu + " foeFore=" + ff + " foeGlove=" + fg + " foeArm=" + (fu + ff + fg)
                           + " foeTorso=" + ft + " foeLegs=" + fl + " head=" + d.Head.Triangles + " frameMeshRenderers=" + all);
        }

        // Пик джеба, портрет 1080×2400: маска перчатки (пиксели, которые меняются, если спрятать бьющую перчатку),
        // её экранный прямоугольник; «белый» — все каналы ≥ 200 и разброс ≤ 30 (строго белый — ≥ 230 и ≤ 18).
        static void WhiteCuff(FightDirector d, Camera cam, int s, StringBuilder log)
        {
            d.SetViewSize(BPW, BPH);
            var g = PG(cam, s).gameObject;
            var full = Grab(cam, BPW, BPH);
            g.SetActive(false);
            var none = Grab(cam, BPW, BPH);
            g.SetActive(true);
            int x0 = BPW, y0 = BPH, x1 = -1, y1 = -1, mask = 0, white = 0, strict = 0;
            for (int y = 0; y < BPH; y++)
            for (int x = 0; x < BPW; x++)
            {
                int i = x + y * BPW;
                if (Same(full[i], none[i])) continue;
                mask++;
                if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                var c = full[i];
                int mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b)), mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (mn >= 200 && mx - mn <= 30) white++;
                if (mn >= 230 && mx - mn <= 18) strict++;
            }
            int rect = x1 >= x0 ? (x1 - x0 + 1) * (y1 - y0 + 1) : 1;
            log.AppendLine("ARMS_WHITE at=" + x0 + "," + y0 + " rect=" + (x1 - x0 + 1) + "x" + (y1 - y0 + 1) + " mask=" + mask + " white=" + white
                           + " whiteOfRect=" + (100f * white / rect).ToString("0.0", Inv) + "% whiteOfMask="
                           + (100f * white / Mathf.Max(1, mask)).ToString("0.0", Inv) + "% strictOfRect=" + (100f * strict / rect).ToString("0.0", Inv) + "%");
        }

        static void CamShot(Camera cam, Vector3 pos, Vector3 look, float fov, int w, int h, string path)
        {
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            cam.fieldOfView = fov;
            Shot(cam, null, w, h, path);
            cam.transform.SetPositionAndRotation(p0, r0);
            cam.fieldOfView = f0;
        }

        static void Closeups(FightDirector d, Camera cam, string dir, string prefix)
        {
            var fr = d.FoeFigure.root;
            d.Gloves.SetVisible(false);
            Vector3 chest = fr.TransformPoint(new Vector3(0f, 1.18f, 0f));
            CamShot(cam, chest + fr.forward * 1.45f + fr.up * 0.04f, chest, 40f, BPW, BPH, Path.Combine(dir, prefix + "foe-torso.png"));
            var el = FindDeep(fr, "foreArm0");
            Vector3 e = el.position;
            CamShot(cam, e + fr.right * 0.75f + fr.forward * 0.25f + fr.up * 0.02f, e + fr.up * 0.02f, 40f, BPW, BPH,
                    Path.Combine(dir, prefix + "foe-arm.png"));
            d.Gloves.SetVisible(true);
        }

        // Левая перчатка игрока сбоку-спереди: кулак, большой палец, манжета, предплечье (ландшафт: масштаб рук 1).
        static void GloveCloseup(FightDirector d, Camera cam, string path)
        {
            var g = PG(cam, 0);
            var ct = cam.transform;
            Vector3 c = g.position - g.forward * 0.06f;
            Vector3 pos = c + g.forward * 0.34f - ct.right * 0.36f + ct.up * 0.16f;
            var c2 = new GameObject("GloveCam").AddComponent<Camera>();
            c2.CopyFrom(cam);
            c2.nearClipPlane = 0.02f;
            c2.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(c - pos, ct.up));
            c2.fieldOfView = 42f;
            Shot(c2, null, BPW, BPH, path);
            Object.DestroyImmediate(c2.gameObject);
        }

        // ---- Время: сборка бойца (FoeBody), рук игрока (PlayerGloves), кадр боя и кадр удара ----

        static double Med(List<double> v) { if (v.Count == 0) return 0; v.Sort(); return v[v.Count / 2]; }

        // Только сборка: FoeBody по всему ростеру, разбивка на заполнение и сетки VoxModel и остальное
        public static void ArmsBuildProfile()
        {
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var parent = new GameObject("bench");
            var roster = new RosterDef(42u);
            var sw = new Stopwatch(); var all = new List<double>();
            new FoeBody(parent.transform, roster.Get(0), mat, true).Dispose();
            VoxModel.FillMs = VoxModel.MeshMs = 0;
            var sb = new StringBuilder();
            for (int i = 0; i < RosterDef.Count; i++)
            {
                double f0 = VoxModel.FillMs, m0 = VoxModel.MeshMs;
                sw.Restart();
                var b = new FoeBody(parent.transform, roster.Get(i), mat, true);
                sw.Stop();
                all.Add(sw.Elapsed.TotalMilliseconds);
                sb.Append(" " + i + ":" + F(sw.Elapsed.TotalMilliseconds) + "/fill" + F(VoxModel.FillMs - f0) + "/mesh" + F(VoxModel.MeshMs - m0));
                b.Dispose();
            }
            Object.DestroyImmediate(parent); Object.DestroyImmediate(mat);
            ArmsLog = "ARMS_BUILDPROF med=" + F(Med(all)) + sb;
            Debug.Log(ArmsLog);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "arms-buildprof.txt")), ArmsLog);
        }

        public static void ArmsBench(string tag)
        {
            var log = new StringBuilder();
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var parent = new GameObject("bench");
            var foe = new List<double>(); var ply = new List<double>();
            var sw = new Stopwatch();
            var roster = new RosterDef(42u);
            for (int rep = 0; rep < 3; rep++)
                for (int i = 0; i < RosterDef.Count; i++)
                {
                    sw.Restart();
                    var b = new FoeBody(parent.transform, roster.Get(i), mat, true);
                    sw.Stop();
                    if (rep > 0) foe.Add(sw.Elapsed.TotalMilliseconds);
                    b.Dispose();
                }
            for (int rep = 0; rep < 9; rep++)
            {
                var cam = new GameObject("c");
                sw.Restart();
                var g = new PlayerGloves(cam.transform, GloveLook.FromEntry(GlovesCatalog.All[rep % GlovesCatalog.All.Length]), mat);
                sw.Stop();
                if (rep > 0) ply.Add(sw.Elapsed.TotalMilliseconds);
                g.Dispose(); Object.DestroyImmediate(cam);
            }
            Object.DestroyImmediate(parent); Object.DestroyImmediate(mat);
            log.AppendLine("ARMS_BUILD foeBody med=" + F(Med(foe)) + " max=" + F(foe[foe.Count - 1]) + " n=" + foe.Count
                           + " | playerGloves med=" + F(Med(ply)) + " max=" + F(ply[ply.Count - 1]) + " n=" + ply.Count);

            // бой как HitFxBench (эффекты включены): кадр удара — lastHitFrameMs, кадр боя — d.Tick без удара
            for (int r = 0; r < 2; r++)
            {
                var frame = new List<double>(); var hit = new List<double>(); var rend = new List<double>();
                var d = NewFight(out var bcam, out _, 6f, 4f);
                d.SetViewSize(BPW, BPH);
                var rt = new RenderTexture(BPW, BPH, 24, RenderTextureFormat.ARGB32);
                var px = new Texture2D(1, 1, TextureFormat.RGB24, false);
                var run = new Run { d = d };
                run.Ticks(90);
                var combo = new[] { PunchType.Hook, PunchType.Jab, PunchType.Upper, PunchType.Jab };
                int got = 0, tries = 0;
                var tsw = new Stopwatch();
                while (got < 40 + WarmHits && tries < 160 && !d.Finished)
                {
                    tries++;
                    run.WaitOpen(150);
                    int seq = d.hitFrameSeq;
                    run.Punch(combo[tries % combo.Length], tries % 2 == 0 ? 1 : -1);
                    run.Tick();
                    if (d.hitFrameSeq != seq) { if (got >= WarmHits) hit.Add(d.lastHitFrameMs); got++; }
                    for (int i = 0; i < 20; i++)
                    {
                        int s2 = d.hitFrameSeq;
                        tsw.Restart(); d.Tick(DT); tsw.Stop();
                        if (d.hitFrameSeq == s2 && got > WarmHits) frame.Add(tsw.Elapsed.TotalMilliseconds);
                        if (i % 5 == 0 && got > WarmHits)
                        {
                            // отрисовка кадра 1080×2400 с ожиданием GPU (чтение пикселя)
                            bcam.targetTexture = rt; RenderTexture.active = rt;
                            tsw.Restart(); bcam.Render(); px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); tsw.Stop();
                            bcam.targetTexture = null; RenderTexture.active = null;
                            rend.Add(tsw.Elapsed.TotalMilliseconds);
                        }
                    }
                }
                log.AppendLine("ARMS_BENCH run=" + r + " hitFrame " + Dist(hit) + " | fightFrame " + Dist(frame) + " | render " + Dist(rend));
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(px);
                EndFight(d);
            }
            ArmsSave("bench-" + tag, log);
        }
    }
}
