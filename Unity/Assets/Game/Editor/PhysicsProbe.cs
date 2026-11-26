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
                for (int f = 0; f <= 20; f++)
                {
                    if (!onlyHook8 && (name == "weak" || name == "strong") && f == 9) PShot(d, cam, ui, name + "-peak");
                    if ((name != "weak" && name != "strong") && (f == 0 || f == 3 || f == 8 || f == 20) && (!onlyHook8 || f == 8))
                        PShot(d, cam, ui, name + "-f" + f);
                    log.AppendLine("SHOT " + name + " f" + f + " head=" + F((b.headMount.position - h0).magnitude * 100f) + "cm lean=" + F(b.spring.lean) + " twist=" + F(b.spring.twist));
                    run.Tick();
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
            var log = new StringBuilder();
            for (int f = 0; f < fights; f++)
            {
                uint seed = (uint)(seed0 + f);
                var d = NewFight(out var cam, out var ui, 0.6f, 0.5f, 0, false, def => { def.seed = seed * 7919u + 3u; return def; });
                var run = new Run { d = d };
                run.Ticks(60);
                var b = d.FoeFigure;
                var parts = FoeParts(b);
                var combo = new[] { PunchType.Hook, PunchType.Upper, PunchType.Jab, PunchType.Hook, PunchType.Jab };
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
                        bool shotRest = false;
                        for (int i = 0; i < (ko ? 300 : 900); i++)
                        {
                            run.Tick(); frames++;
                            float low = LowestOverFloor(b, parts, out string w);
                            if (low < minLow) { minLow = low; minWhat = w; }
                            float jh = (b.headMount.position - prevH).magnitude, jt = (b.Torso.position - prevT).magnitude;
                            float j = Mathf.Max(jh, jt);
                            if (d.State.phase == FightPhase.Rise || (stillAt >= 0 && d.State.phase == FightPhase.Idle)) maxJumpRise = Mathf.Max(maxJumpRise, j);
                            else maxJump = Mathf.Max(maxJump, j);
                            if (stillAt < 0 && i > 5 && j < 0.05f / 60f) stillAt = i;
                            if (shots && f == 0 && downs == 1 && i == 0) PShot(d, cam, ui, "ko-f0");
                            if (shots && f == 0 && downs == 1 && i == 10) PShot(d, cam, ui, "ko-f10");
                            if (shots && f == 0 && downs == 1 && i == 25) PShot(d, cam, ui, "ko-f25");
                            if (shots && f == 0 && downs == 1 && !shotRest && i == 150) { PShot(d, cam, ui, "ko-rest"); PShot(d, cam, ui, "ko-rest-land", true); shotRest = true; }
                            prevH = b.headMount.position; prevT = b.Torso.position;
                            if (!ko && d.State.phase == FightPhase.Idle && i > 60) { if (shots && f == 0 && downs == 1) PShot(d, cam, ui, "ko-getup"); break; }
                        }
                        Vector3 head1 = b.root.InverseTransformPoint(b.headMount.position);
                        Vector3 fall = head1 - head0; fall.y = 0f; Vector3 hd = lastDir; hd.y = 0f;
                        log.AppendLine("KO fight=" + f + " n=" + downs + (ko ? " KO" : " down") + " last=" + lastType + " frames=" + frames
                                       + " lowest=" + F(minLow * 100f) + "cm(" + minWhat + ") fallAngle=" + F(Vector3.Angle(fall, hd))
                                       + " fall=" + V(fall) + " still@" + stillAt + " jump=" + F(maxJump * 100f) + "cm jumpRise=" + F(maxJumpRise * 100f) + "cm");
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
                int frame = 0, peakP = 0, peakD = 0, under = 0, hang = 0, settled = 0, total = 0;
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
                        peakP = Mathf.Max(peakP, v.pieces.list.Count); peakD = Mathf.Max(peakD, v.sim.drops.Count);
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
                    if (p.asleep) settled++;
                    if (total <= 30) log.AppendLine("PIECE " + p.what + " cells=" + p.cells + " y=" + F(y * 100f) + "cm asleep=" + p.asleep + " age=" + F(p.age));
                }
                if (shots && f == 0) PShot(d, cam, ui, "pieces-rest");
                log.AppendLine("PIECES fight=" + f + " made=" + v.pieces.made + " alive=" + total + " peak=" + peakP + "/" + AnatPieces.MaxPieces
                               + " under=" + under + " hang=" + hang + " asleep=" + settled + " dropsPeak=" + peakD + "/" + AnatDamage.MaxDrops);
                EndFight(d);
            }
            PhysLog = log.ToString();
        }

        static Vector3 PieceWorld(AnatView v, AnatPiece p) => p.body != null && p.body.root != null
            ? p.body.root.parent.TransformPoint((Vector3)p.x * 0.01f) : v.root.TransformPoint((Vector3)p.x * 0.01f);
    }
}
