// PhysicsProbe.cs — замеры и снимки физики боя (Unity/PHYSICS.md): реакция тела на удар, руки, нокдаун, куски и капли.
// Запуск: unity command script eval --code 'VoxGame.Editor.ShotsTaker.PhysDirs(); return VoxGame.Editor.ShotsTaker.PhysLog;'
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxProbe;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class ShotsTaker
    {
        public static string PhysLog = "";
        public static string PhysTag = "";   // приставка имён снимков ("before-")
        static string PhysShots { get { var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "physics")); Directory.CreateDirectory(d); return d; } }
        static string V(Vector3 v) => "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")";
        const int PhysW = 1080, PhysH = 2400;

        static void PShot(FightDirector d, Camera cam, UIRoot ui, string name, bool land = false)
        {
            int w = land ? PhysH : PhysW, h = land ? PhysW : PhysH;
            d.SetViewSize(w, h);
            Shot(cam, ui, w, h, Path.Combine(PhysShots, PhysTag + name + ".png"));
            d.SetViewSize(PortW, PortH);
        }

        // Удар в голову: направление и точка попадания в осях корня соперника (точка — от начала координат головы, м)
        static bool LastHit(FightDirector d, out Vector3 dir, out Vector3 arm)
        {
            var b = d.FoeFigure; var hr = d.Head.root;
            Vector3 dw = hr.rotation * (Vector3)d.lastHitDir;
            Vector3 cw = hr.position + hr.rotation * ((Vector3)d.lastHitPoint * 0.01f);
            Quaternion inv = Quaternion.Inverse(b.root.rotation);
            dir = inv * dw; arm = inv * (cw - b.headMount.position);
            return true;
        }

        public static void PhysDirs()
        {
            var log = new StringBuilder();
            var d = NewFight(out var cam, out var ui, 8f, 8f);
            var run = new Run { d = d };
            run.Ticks(60);
            foreach (var t in new[] { PunchType.Jab, PunchType.Hook, PunchType.Upper })
                foreach (int side in new[] { -1, 1 })
                    for (int r = 0; r < 2; r++)
                    {
                        run.WaitOpen(300);
                        int seq = d.hitFrameSeq;
                        run.Punch(t, side, 1.25f);
                        run.Tick();
                        if (d.hitFrameSeq == seq) { log.AppendLine("DIR " + t + " " + side + " miss"); continue; }
                        LastHit(d, out var dir, out var arm);
                        log.AppendLine("DIR " + t + " " + side + " dir=" + V(dir) + " arm=" + V(arm) + " str=" + d.lastHitStrength + " phase=" + d.State.phase);
                        run.Ticks(50);
                    }
            EndFight(d);
            PhysLog = log.ToString();
        }

        // ---- Реакция тела: два одинаковых тела, одно получает удар; смещение = разница (качание стойки вычитается) ----

        // Направления и точки попадания из настоящего боя (PhysDirs), оси корня соперника
        public static readonly Vector3 JabDir = new Vector3(-0.02f, 0.01f, -1f).normalized, JabArm = new Vector3(0.02f, -0.03f, 0.09f);
        public static readonly Vector3 HookDir = new Vector3(-0.31f, 0f, -0.95f).normalized, HookArm = new Vector3(0.05f, -0.04f, 0.02f);
        public static readonly Vector3 UpperDir = new Vector3(-0.03f, 0.38f, -0.92f).normalized, UpperArm = new Vector3(0.02f, -0.10f, 0.05f);

        public sealed class React
        {
            public List<Vector3> head = new List<Vector3>(), chest = new List<Vector3>(), glove = new List<Vector3>();
            public float dt, headPeak, chestPeak, angle, settle, glovePeak, gloveSettle; public int headAt, chestAt, cross;
            public string Line(string n) => n + " head=" + F(headPeak * 100f) + "cm@" + headAt + " chest=" + F(chestPeak * 100f) + "cm@" + chestAt
                + " lag=" + (chestAt - headAt) + "f angle=" + F(angle) + " settle=" + F(settle) + "s cross=" + cross
                + " glove=" + F(glovePeak * 100f) + "cm back=" + F(gloveSettle) + "s";
        }

        public static React ReactRun(Vector3 dir, Vector3 arm, int strength, float pw, float dt, FightPhase phase = FightPhase.Idle, float secs = 2f)
        {
            var go = new GameObject("react");
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var def = FighterGen.Generate(42u, 1);
            var hit = new FoeBody(go.transform, def, mat); var calm = new FoeBody(go.transform, def, mat);
            var cmd = new AICommand { newPhase = phase, side = -1 };
            for (float t = 0f; t < 1f - 1e-4f; t += dt) { hit.Tick(cmd, dt, Vector3.forward); calm.Tick(cmd, dt, Vector3.forward); }
            Vector3 pt = hit.headMount.position + hit.root.rotation * arm;
            hit.AddHit(pt, hit.root.rotation * dir, strength, pw);
            var r = new React { dt = dt };
            Vector3 hc = new Vector3(0f, 0.03f, 0.02f), cc = new Vector3(0f, 0.45f, 0f);
            Quaternion inv = Quaternion.Inverse(hit.root.rotation);
            for (float t = 0f; t < secs - 1e-4f; t += dt)
            {
                hit.Tick(cmd, dt, Vector3.forward); calm.Tick(cmd, dt, Vector3.forward);
                r.head.Add(inv * (hit.headMount.TransformPoint(hc) - calm.headMount.TransformPoint(hc)));
                r.chest.Add(inv * (hit.Torso.TransformPoint(cc) - calm.Torso.TransformPoint(cc)));
                // перчатка относительно корпуса: насколько она отстала от своего места у корпуса
                Vector3 gh = hit.GloveWorld(0) - hit.Torso.TransformPoint(cc), gc = calm.GloveWorld(0) - calm.Torso.TransformPoint(cc);
                r.glove.Add(inv * (gh - gc));
            }
            r.headAt = PhysMetrics.Peak(r.head, out r.headPeak); r.chestAt = PhysMetrics.Peak(r.chest, out r.chestPeak);
            r.angle = PhysMetrics.PeakAngle(r.head, dir); r.cross = PhysMetrics.ZeroCross(r.head);
            int s = PhysMetrics.Settle(r.head); r.settle = s < 0 ? 99f : s * dt;
            PhysMetrics.Peak(r.glove, out r.glovePeak); int gs = PhysMetrics.Settle(r.glove, 0.1f); r.gloveSettle = gs < 0 ? 99f : gs * dt;
            hit.Dispose(); calm.Dispose(); Object.DestroyImmediate(go); Object.DestroyImmediate(mat);
            return r;
        }

        public static void PhysReact()
        {
            var log = new StringBuilder();
            foreach (float dt in new[] { 1f / 60f, 1f / 30f, 1f / 120f })
            {
                log.AppendLine("dt=1/" + Mathf.RoundToInt(1f / dt));
                var jab = ReactRun(JabDir, JabArm, 0, 1.25f, dt); log.AppendLine(jab.Line(" jab  "));
                var hook = ReactRun(HookDir, HookArm, 1, 1.25f, dt); log.AppendLine(hook.Line(" hook "));
                var up = ReactRun(UpperDir, UpperArm, 1, 1.25f, dt); log.AppendLine(up.Line(" upper"));
                var mega = ReactRun(JabDir, JabArm, 2, 1.25f, dt); log.AppendLine(mega.Line(" mega "));
                var blk = ReactRun(JabDir, JabArm, 1, 1.25f, dt, FightPhase.Guard); log.AppendLine(blk.Line(" guard"));
                log.AppendLine(" ratio hook/jab=" + F(hook.headPeak / jab.headPeak) + " mega/jab=" + F(mega.headPeak / jab.headPeak));
            }
            PhysLog = log.ToString();
        }

        // Раскадровка реакции в настоящем бою: кадры 0, 3, 8, 20 после попадания джеба, хука, апперкота; слабый и сильный на пике.
        public static void PhysReactShots(bool onlyHook8 = false)
        {
            var log = new StringBuilder();
            var d = NewFight(out var cam, out var ui, 30f, 30f);
            var run = new Run { d = d };
            run.Ticks(60);
            var kinds = new[] { (PunchType.Jab, "jab", 1.25f), (PunchType.Hook, "hook", 1.25f), (PunchType.Upper, "upper", 1.25f),
                                (PunchType.Jab, "weak", 0.8f), (PunchType.Hook, "strong", 1.6f) };
            foreach (var (t, name, pw) in kinds)
            {
                if (onlyHook8 && name != "hook") continue;
                run.Ticks(40);
                run.WaitOpen(300);
                // дождаться покоя стойки, без замаха
                int seq = d.hitFrameSeq;
                run.Punch(t, -1, pw);
                run.Tick();
                if (d.hitFrameSeq == seq) { log.AppendLine("SHOT " + name + " miss"); continue; }
                var b = d.FoeFigure; Vector3 h0 = b.headMount.position;
                while (d.lastSdt <= 0f) run.Tick();   // стоп-кадр удара
                for (int f = 0; f <= 20; f++)
                {
                    if (!onlyHook8 && (name == "weak" || name == "strong") && f == 9) PShot(d, cam, ui, name + "-peak");
                    if ((name != "weak" && name != "strong") && (f == 0 || f == 3 || f == 8 || f == 20) && (!onlyHook8 || f == 8))
                        PShot(d, cam, ui, name + "-f" + f);
                    log.AppendLine("SHOT " + name + " f" + f + " head=" + F((b.headMount.position - h0).magnitude * 100f) + "cm lean=" + F(b.spring.lean) + " twist=" + F(b.spring.twist));
                    // кадры — игрового времени (1/60 с): стоп-кадр и слоу-мо удара их растягивают
                    for (float gt = 0f; gt < 1f / 60f - 1e-5f; ) { run.Tick(); gt += d.lastSdt; }
                }
            }
            EndFight(d);
            PhysLog = log.ToString();
        }

        // Отдача перчатки игрока: два набора рук, попадание и промах (у промаха нет отдачи); смещение перчатки на экране
        // в долях ширины кадра 1080×2400, по кадрам после касания.
        public static string PlayerRecoil(PunchType kind, int side, float dt = 1f / 60f)
        {
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var ra = new GameObject("camA"); var rb = new GameObject("camB");
            var a = new PlayerGloves(ra.transform, GloveLook.FromEntry(GlovesCatalog.All[0]), mat);
            var b = new PlayerGloves(rb.transform, GloveLook.FromEntry(GlovesCatalog.All[0]), mat);
            float fov = GameUtil.CalcFov(PhysW, PhysH), asp = PhysW / (float)PhysH;
            a.SetView(fov, asp); b.SetView(fov, asp);
            float tv = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), th = tv * asp;
            for (int i = 0; i < 40; i++) { a.Tick(dt); b.Tick(dt); }
            int s = side < 0 ? 0 : 1;
            var tgt = new Vector3(side * 0.04f, -0.03f, 1.25f);
            a.Punch(side, kind, 0.2f, tgt, true); b.Punch(side, kind, 0.2f, tgt, false);
            var ga = ra.transform.Find("playerArms/playerGlove" + s); var gb = rb.transform.Find("playerArms/playerGlove" + s);
            int contact = -1, frames = 0; float peak = 0f, dz = 0f; var sb = new StringBuilder();
            for (int i = 0; i < 40; i++)
            {
                a.Tick(dt); b.Tick(dt);
                if (contact < 0 && a.Extension(s) >= 1f) contact = i;
                Vector3 pa = ga.position, pb = gb.position;   // корни камер в начале координат, оси мира
                Vector2 sa = new Vector2(pa.x / (pa.z * th) * 0.5f, pa.y / (pa.z * tv) * 0.5f * PhysH / PhysW);
                Vector2 sbp = new Vector2(pb.x / (pb.z * th) * 0.5f, pb.y / (pb.z * tv) * 0.5f * PhysH / PhysW);
                float d = (sa - sbp).magnitude;
                if (contact >= 0 && i > contact) { if (d >= 0.03f) frames++; if (d > peak) { peak = d; dz = pa.z - pb.z; } if (i - contact <= 8) sb.Append(F(d * 100f)).Append(' '); }
            }
            a.Dispose(); b.Dispose(); Object.DestroyImmediate(ra); Object.DestroyImmediate(rb); Object.DestroyImmediate(mat);
            return "RECOIL " + kind + " " + side + " peak=" + F(peak * 100f) + "%w frames>=3%=" + frames + " dz=" + F(dz * 100f) + "cm byFrame%: " + sb;
        }

        public static void PhysArms()
        {
            var log = new StringBuilder();
            foreach (var k in new[] { PunchType.Jab, PunchType.Hook, PunchType.Upper, PunchType.Mega })
                foreach (int side in new[] { -1, 1 }) log.AppendLine(PlayerRecoil(k, side));
            log.AppendLine(PlayerRecoil(PunchType.Jab, 1, 1f / 30f) + " (30)");
            log.AppendLine(PlayerRecoil(PunchType.Jab, 1, 1f / 120f) + " (120)");
            var r = ReactRun(HookDir, HookArm, 1, 1.25f, 1f / 60f); log.AppendLine(r.Line("foe hook"));
            var st = ReactRun(HookDir, HookArm, 1, 1.25f, 1f / 60f, FightPhase.Stagger); log.AppendLine(st.Line("foe stagger hook"));
            PhysLog = log.ToString();
        }

        // Падение одного тела без боя: dir — удар (оси корня), лог узлов в покое
        public static string FallDebug(Vector3 dir, bool ko, bool arena = false, Vector3 at = default, uint gen = 42u, int lvl = 1)
        {
            var go = new GameObject("fall");
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var b = new FoeBody(go.transform, FighterGen.Generate(gen, lvl), mat);
            if (arena) b.SetArena(RingCenter(), RingYaw, RingBuilder.RopeHalf);
            b.root.localPosition = at;
            const float dt = 1f / 60f; var sb = new StringBuilder();
            for (int i = 0; i < 30; i++) b.Tick(new AICommand { newPhase = FightPhase.Idle }, dt, Vector3.forward);
            b.AddHit(b.headMount.position + b.root.rotation * JabArm, b.root.rotation * dir, 1, 1.25f);
            var cmd = new AICommand { newPhase = ko ? FightPhase.KO : FightPhase.Down };
            for (int i = 0; i < 180; i++)
            {
                b.Tick(cmd, dt, Vector3.forward);
                if (i % 15 == 14 || i == 5)
                {
                    sb.Append("t=" + F((i + 1) * dt) + " asleep=" + b.fall.asleep + " v=" + F(b.fall.MaxSpeed));
                    for (int k = 0; k < FoeFall.N; k++) sb.Append(" " + k + V(b.fall.x[k]));
                    sb.AppendLine();
                }
            }
            var parts = FoeParts(b); float low = LowestOverFloor(b, parts, out string w);
            sb.AppendLine("lowest=" + F(low * 100f) + "cm " + w + " knee=" + V(b.root.Find("FoeBody") != null ? Vector3.zero : Vector3.zero));
            b.Dispose(); Object.DestroyImmediate(go); Object.DestroyImmediate(mat);
            return sb.ToString();
        }

        // Ринг как в FightDirector.Init: красный угол в 3 м за глазом (9,5°), диагональ 12°, канаты — квадрат 4,2 м
        public static Vector3 RingCenter()
        {
            float off = 9.5f * Mathf.Deg2Rad, diag = 12f * Mathf.Deg2Rad;
            var corner = new Vector3(-Mathf.Sin(off) * 3f, 0f, 1.40f - Mathf.Cos(off) * 3f);
            return corner + new Vector3(Mathf.Sin(diag), 0f, Mathf.Cos(diag)) * (RingBuilder.RopeHalf * 1.41421356f);
        }
        public const float RingYaw = 57f;

        public sealed class FallStat
        {
            public int runs, nan, under, outRing, slow, dirBad; public string meshWhat = ""; public float worstUnder, worstSettle, worstAngle, worstJump, worstStance, worstMesh;
            public string Line() => "FALL runs=" + runs + " NaN=" + nan + " under2cm=" + under + " (worst " + F(worstUnder * 100f) + "cm) outRing=" + outRing
                + " settle>2.5s=" + slow + " (worst " + F(worstSettle) + "s) dir>45=" + dirBad + " (" + F(100f * (runs - dirBad) / Mathf.Max(1, runs)) + "% ok, worst " + F(worstAngle)
                + ") riseJump=" + F(worstJump * 100f) + "%H stanceErr=" + F(worstStance * 100f) + "cm meshLowest=" + F(worstMesh * 100f) + "cm(" + meshWhat + ")";
        }

        // Нокдауны без боя: seeds × per падений с разными добивающими ударами (джеб, хуки, апперкоты, мега) и местом в ринге;
        // падение, счёт (лежит), подъём, стойка. dt — шаг кадра.
        public static FallStat FallStats(int seeds = 20, int per = 10, float dt = 1f / 60f, bool ko = false, StringBuilder log = null, bool near = false)
        {
            var st = new FallStat();
            var go = new GameObject("falls");
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            Vector3[] dirs = { JabDir, HookDir, new Vector3(-HookDir.x, HookDir.y, HookDir.z), UpperDir, new Vector3(0.45f, 0.1f, -0.89f), new Vector3(-0.45f, 0.1f, -0.89f) };
            for (int sd = 0; sd < seeds; sd++)
            {
                var b = new FoeBody(go.transform, FighterGen.Generate((uint)(1000 + sd * 31), 1 + sd % 8), mat);
                b.SetArena(RingCenter(), RingYaw, RingBuilder.RopeHalf);
                var parts = FoeParts(b);
                uint rng = (uint)(sd * 2654435761u + 7u);
                float Rnd() { rng = rng * 1664525u + 1013904223u; return (rng >> 8) / 16777216f; }
                var idle = new AICommand { newPhase = FightPhase.Idle };
                for (int i = 0; i < 30; i++) b.Tick(idle, dt, Vector3.forward);
                Vector3 stance = b.root.InverseTransformPoint(b.headMount.position);
                float height = stance.y + 0.12f;
                for (int k = 0; k < per; k++)
                {
                    st.runs++;
                    Vector3 dir = dirs[(int)(Rnd() * dirs.Length) % dirs.Length];
                    dir = (dir + new Vector3(Rnd() - 0.5f, (Rnd() - 0.5f) * 0.3f, 0f) * 0.4f).normalized;
                    int strength = Rnd() < 0.5f ? 1 : (Rnd() < 0.5f ? 0 : 2);
                    // корень соперника — как в бою (в начале координат, 1–1,2 м до канатов за спиной); near — сдвиг до 0,4 м к углу
                    b.root.localPosition = near ? new Vector3(Rnd() * 0.3f, 0f, -Rnd() * 0.3f) : Vector3.zero;
                    for (int i = 0; i < 10; i++) b.Tick(idle, dt, Vector3.forward);
                    b.AddHit(b.headMount.position + b.root.rotation * JabArm, b.root.rotation * dir, strength, 0.8f + Rnd());
                    Vector3 h0 = b.headMount.position;
                    var down = new AICommand { newPhase = ko ? FightPhase.KO : FightPhase.Down };
                    float settle = -1f; bool nan = false; float under = 0f, mesh = 0f; bool outR = false;
                    for (float t = 0f; t < 3.5f; t += dt)
                    {
                        b.Tick(down, dt, Vector3.forward);
                        for (int n = 0; n < FoeFall.N; n++)
                        {
                            Vector3 x = b.fall.x[n];
                            if (float.IsNaN(x.x + x.y + x.z)) nan = true;
                            under = Mathf.Max(under, b.fall.floorY - (x.y - FoeFall.Radius(n)));
                            if (!b.fall.InRing(n)) outR = true;
                        }
                        float lw = LowestOverFloor(b, parts, out string lwhat);
                        if (lw < mesh) { mesh = lw; if (lw < st.worstMesh) st.meshWhat = lwhat; }
                        if (settle < 0f && b.fall.asleep) settle = t + dt;
                    }
                    // направление падения — ось лежащего тела: таз -> голова (у канатов тело сползает, но лежит по удару)
                    Vector3 fall = b.fall.x[FoeFall.Head] - b.fall.x[FoeFall.Pelvis]; fall.y = 0f;
                    if (ko == false && fall.magnitude < 0.3f) fall = b.headMount.position - h0;
                    fall.y = 0f; Vector3 dw = b.root.rotation * dir; dw.y = 0f;
                    float ang = Vector3.Angle(fall, dw);
                    if (nan) st.nan++;
                    if (under > 0.02f) st.under++;
                    if (outR) st.outRing++;
                    if (settle < 0f || settle > 2.5f) st.slow++;
                    if (settle < 0f && log != null && st.slow < 6) { int mi = 0; for (int n = 1; n < FoeFall.N; n++) if (b.fall.v[n].magnitude > b.fall.v[mi].magnitude) mi = n; log.AppendLine(" awake seed=" + sd + " k=" + k + " node=" + mi + " v=" + V(b.fall.v[mi]) + " y=" + F((b.fall.x[mi].y - b.fall.floorY) * 100f)); }
                    if (ang > 45f) st.dirBad++;
                    st.worstUnder = Mathf.Max(st.worstUnder, under); st.worstSettle = Mathf.Max(st.worstSettle, settle < 0f ? 99f : settle);
                    st.worstAngle = Mathf.Max(st.worstAngle, ang); st.worstMesh = Mathf.Min(st.worstMesh, mesh);
                    if (log != null && (ang > 45f || under > 0.02f || outR || nan)) log.AppendLine(" bad seed=" + sd + " k=" + k + " dir=" + V(dir) + " ang=" + F(ang) + " under=" + F(under * 100f) + " out=" + outR + " fall=" + V(fall));
                    if (ko) { b.ResetFall(); continue; }
                    // подъём (Rise 1,9 с) и пауза стойки (1,1 с): скачок между кадрами, поза в конце
                    var rise = new AICommand { newPhase = FightPhase.Rise };
                    Vector3 ph = b.headMount.position, pt = b.Torso.position;
                    for (float t = 0f; t < 3f; t += dt)
                    {
                        b.Tick(t < 1.9f ? rise : idle, dt, Vector3.forward);
                        // рывок на 1/60 с: при 30 к/с кадр вдвое длиннее, та же скорость даёт вдвое больший сдвиг
                        float j = Mathf.Max((b.headMount.position - ph).magnitude, (b.Torso.position - pt).magnitude) / (dt * 60f);
                        if (j / height > st.worstJump && log != null && j / height > 0.05f) log.AppendLine(" jump seed=" + sd + " k=" + k + " t=" + F(t) + " j=" + F(j * 100f) + " settle=" + F(settle) + " w=" + F(b.FallWeight));
                        st.worstJump = Mathf.Max(st.worstJump, j / height);
                        ph = b.headMount.position; pt = b.Torso.position;
                        if (float.IsNaN(ph.x)) nan = true;
                    }
                    st.worstStance = Mathf.Max(st.worstStance, (b.root.InverseTransformPoint(b.headMount.position) - stance).magnitude);
                }
                b.Dispose();
            }
            Object.DestroyImmediate(go); Object.DestroyImmediate(mat);
            return st;
        }

        public static void PhysFalls()
        {
            var log = new StringBuilder();
            log.AppendLine(FallStats(20, 10, 1f / 60f, false, log).Line() + " (60)");
            log.AppendLine(FallStats(5, 4, 1f / 30f).Line() + " (30)");
            log.AppendLine(FallStats(5, 4, 1f / 120f).Line() + " (120)");
            log.AppendLine(FallStats(5, 4, 1f / 60f, true).Line() + " (KO)");
            log.AppendLine(FallStats(5, 4, 1f / 60f, false, null, true).Line() + " (near corner)");
            PhysLog = log.ToString();
        }

        // ---- Нокдаун и куски в настоящем бою --------------------------------------------------------------

        sealed class PartVerts { public Transform t; public Vector3[] v; }

        static List<PartVerts> FoeParts(FoeBody b)
        {
            var l = new List<PartVerts>();
            foreach (var mf in b.root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.gameObject.activeInHierarchy) continue;
                if (mf.transform.IsChildOf(b.headMount)) continue;
                l.Add(new PartVerts { t = mf.transform, v = mf.sharedMesh.vertices });
            }
            return l;
        }

        // Самая низкая точка тела над настилом (м): вершины сеток тела, голова — шаром 11 см вокруг её центра
        static float LowestOverFloor(FoeBody b, List<PartVerts> parts, out string what)
        {
            float floor = b.root.position.y, low = float.MaxValue; what = "";
            foreach (var p in parts)
            {
                var m = p.t.localToWorldMatrix;
                for (int i = 0; i < p.v.Length; i += 3) { float y = m.MultiplyPoint3x4(p.v[i]).y; if (y < low) { low = y; what = p.t.name; } }
            }
            float hy = b.headMount.TransformPoint(new Vector3(0f, 0.02f, 0.01f)).y - 0.11f;
            if (hy < low) { low = hy; what = "head"; }
            return low - floor;
        }

        // Прогон боя до нокдаунов: сколько раз, где тело ниже настила, как легло, скачки при подъёме.
        public static void PhysKO(int fights = 2, int seed0 = 1, bool shots = true)
        {
            var log = new StringBuilder(); var koSide = new System.Collections.Generic.HashSet<string>();
            for (int f = 0; f < fights; f++)
            {
                uint seed = (uint)(seed0 + f);
                var d = NewFight(out var cam, out var ui, 0.6f, 0.5f, 0, false, def => { def.seed = seed * 7919u + 3u; return def; });
                var run = new Run { d = d };
                run.Ticks(60);
                var b = d.FoeFigure;
                var parts = FoeParts(b);
                var combo = new[] { PunchType.Hook, PunchType.Upper, PunchType.Jab, PunchType.Hook, PunchType.Jab };
                log.AppendLine("KO fight=" + f + " stanceLowest=" + F(LowestOverFloor(b, parts, out string sw) * 100f) + "cm(" + sw + ")");
                int punches = 0, downs = 0;
                Vector3 lastDir = Vector3.zero; PunchType lastType = PunchType.Jab;
                while (!d.Finished && punches < 300 && downs < 4)
                {
                    run.WaitOpen(200);
                    var ph = d.State.phase;
                    if (ph == FightPhase.Down || ph == FightPhase.Rise) { run.Tick(); continue; }
                    var pt = combo[(punches + f) % combo.Length];
                    int seq = d.hitFrameSeq;
                    run.Punch(pt, punches % 2 == 0 ? 1 : -1, 1.35f);
                    punches++;
                    run.Tick();
                    if (d.hitFrameSeq != seq) { LastHit(d, out lastDir, out _); lastType = pt; }
                    if (d.State.phase == FightPhase.Down || d.State.phase == FightPhase.KO)
                    {
                        downs++;
                        bool ko = d.State.phase == FightPhase.KO;
                        Vector3 head0 = b.root.InverseTransformPoint(b.headMount.position);
                        float minLow = float.MaxValue; string minWhat = ""; float maxJump = 0f, maxJumpRise = 0f;
                        Vector3 prevH = b.headMount.position, prevT = b.Torso.position; int frames = 0, stillAt = -1;
                        bool shotRest = false; int jumpAt = -1; string jumpWhat = ""; Vector3 restAx = Vector3.zero; float restAt = -1f;
                        for (int i = 0; i < (ko ? 300 : 900); i++)
                        {
                            run.Tick(); frames++;
                            float low = LowestOverFloor(b, parts, out string w);
                            if (low < minLow) { minLow = low; minWhat = w; }
                            float jh = (b.headMount.position - prevH).magnitude, jt = (b.Torso.position - prevT).magnitude;
                            float j = Mathf.Max(jh, jt);
                            if (d.State.phase == FightPhase.Rise || (stillAt >= 0 && d.State.phase == FightPhase.Idle)) maxJumpRise = Mathf.Max(maxJumpRise, j);
                            else { if (j > maxJump) { jumpAt = i; jumpWhat = (jh > jt ? "head" : "torso") + " fw=" + F(b.FallWeight) + " ph=" + d.State.phase + " asleep=" + b.fall.asleep; } maxJump = Mathf.Max(maxJump, j); }
                            if (stillAt < 0 && i > 5 && j < 0.05f / 60f) stillAt = i;
                            if (shots && f == 0 && downs == 1 && i == 0) PShot(d, cam, ui, "ko-f0");
                            if (shots && f == 0 && downs == 1 && i == 10) PShot(d, cam, ui, "ko-f10");
                            if (shots && f == 0 && downs == 1 && i == 25) PShot(d, cam, ui, "ko-f25");
                            if (shots && f == 0 && downs == 1 && !shotRest && i == 150) { PShot(d, cam, ui, "ko-rest"); PShot(d, cam, ui, "ko-rest-land", true); shotRest = true; }
                            if (restAt < 0f && (b.fall.asleep || i == 150))
                            {
                                restAt = i / 60f; restAx = b.root.InverseTransformDirection(b.fall.x[FoeFall.Head] - b.fall.x[FoeFall.Pelvis]); restAx.y = 0f;
                                Vector3 ld = lastDir; ld.y = 0f; bool side = Mathf.Abs(ld.x) > 0.25f;
                                string nm = ld.x > 0f ? "ko-left-rest" : "ko-right-rest";
                                if (shots && side && !koSide.Contains(nm)) { koSide.Add(nm); PShot(d, cam, ui, nm); }
                            }
                            prevH = b.headMount.position; prevT = b.Torso.position;
                            if (!ko && d.State.phase == FightPhase.Idle && i > 60) { if (shots && f == 0 && downs == 1) PShot(d, cam, ui, "ko-getup"); break; }
                        }
                        Vector3 head1 = b.root.InverseTransformPoint(b.headMount.position);
                        Vector3 fall = head1 - head0; fall.y = 0f; Vector3 hd = lastDir; hd.y = 0f;
                        log.AppendLine("KO fight=" + f + " n=" + downs + (ko ? " KO" : " down") + " last=" + lastType + " frames=" + frames
                                       + " lowest=" + F(minLow * 100f) + "cm(" + minWhat + ") fallAngle=" + F(Vector3.Angle(fall, hd)) + " restAngle=" + F(Vector3.Angle(restAx, hd)) + " rest@" + F(restAt) + "s"
                                       + " fall=" + V(fall) + " still@" + stillAt + " jump=" + F(maxJump * 100f) + "cm@" + jumpAt + "(" + jumpWhat + ") jumpRise=" + F(maxJumpRise * 100f) + "cm");
                        if (ko) break;
                    }
                }
                log.AppendLine("KO fight=" + f + " punches=" + punches + " downs=" + downs + " finished=" + d.Finished);
                EndFight(d);
            }
            PhysLog = log.ToString();
        }

        // Куски и капли в бою мегой и силовыми: где лежат через 3 с, висят ли, сколько живых.
        public static void PhysPieces(int fights = 1, int seed0 = 1, bool shots = true)
        {
            var log = new StringBuilder();
            for (int f = 0; f < fights; f++)
            {
                uint seed = (uint)(seed0 + f);
                var d = NewFight(out var cam, out var ui, 30f, 30f, 0, false, def => { def.seed = seed * 7919u + 3u; return def; });
                var run = new Run { d = d };
                run.Ticks(60);
                var v = d.Head; var b = d.FoeFigure;
                var born = new Dictionary<AnatPiece, int>();
                int frame = 0, peakP = 0, peakD = 0, peakR = 0, peakW = 0, peakT = 0; bool dripShot = false; int under = 0, hang = 0, settled = 0, total = 0;
                bool flyShot = false;
                var combo = new[] { PunchType.Hook, PunchType.Upper, PunchType.Hook, PunchType.Jab };
                for (int k = 0; k < 60 && !d.Finished; k++)
                {
                    run.WaitOpen(200);
                    if (d.State.megaReady) run.Punch(PunchType.Hook, k % 2 == 0 ? 1 : -1, 1.6f);
                    else run.Punch(combo[k % combo.Length], k % 2 == 0 ? 1 : -1, 1.6f);
                    for (int i = 0; i < 24; i++)
                    {
                        run.Tick(); frame++;
                        foreach (var p in v.pieces.list) if (!born.ContainsKey(p)) born[p] = frame;
                        peakP = Mathf.Max(peakP, v.pieces.list.Count); peakD = Mathf.Max(peakD, v.sim.drops.Count); int dk = 0; foreach (var dr in v.sim.drips) if (!dr.kid) dk++; peakR = Mathf.Max(peakR, dk); peakW = Mathf.Max(peakW, v.sim.wounds.Count); peakT = Mathf.Max(peakT, v.pieces.tethered);   // подтёки без веток (kid): ветки в предел не входят
                        if (shots && f == 0 && !dripShot && v.sim.drops.Count >= 10) { PShot(d, cam, ui, "drips"); dripShot = true; }
                        if (shots && f == 0 && !flyShot && v.pieces.list.Count > 0)
                            foreach (var p in v.pieces.list) if (p.ready && !p.asleep && p.age > 0.05f && p.age < 0.25f) { PShot(d, cam, ui, "pieces-fly"); flyShot = true; break; }
                    }
                }
                run.Ticks(200); frame += 200;
                float floor = b.root.position.y;
                foreach (var p in v.pieces.list)
                {
                    total++;
                    Vector3 wp = PieceWorld(v, p);
                    float y = wp.y - floor;
                    if (y < -0.02f) under++;
                    if (p.asleep && y > 0.15f) hang++;
                    if (p.asleep && p.sleptAt <= 3f) settled++;
                    if (total <= 30) log.AppendLine("PIECE " + p.what + " cells=" + p.cells + " y=" + F(y * 100f) + "cm asleep=" + p.asleep + " age=" + F(p.age) + " slept@" + F(p.sleptAt) + " v=" + F(math.length(p.v)) + " w=" + F(math.length(p.w)));
                }
                if (shots && f == 0) PShot(d, cam, ui, "pieces-rest");
                log.AppendLine("PIECES fight=" + f + " made=" + v.pieces.made + " alive=" + total + " peak=" + peakP + "/" + AnatPieces.MaxPieces
                               + " under=" + under + " hang=" + hang + " asleep=" + settled + " dripsPeak=" + peakR + "/" + AnatDamage.MaxDrips + " woundsPeak=" + peakW + "/" + AnatDamage.MaxWounds + " tetheredPeak=" + peakT + "/" + AnatPieces.MaxTethered + " dropsPeak=" + peakD + "/" + AnatDamage.MaxDrops);
                EndFight(d);
            }
            PhysLog = log.ToString();
            System.IO.File.WriteAllText("Logs/phys-pieces.txt", PhysLog);
        }

        static Vector3 PieceWorld(AnatView v, AnatPiece p) => p.body != null && p.body.root != null
            ? p.body.root.parent.TransformPoint((Vector3)p.x * 0.01f) : v.root.TransformPoint((Vector3)p.x * 0.01f);
    }
}
