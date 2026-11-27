// EmotionShots2.cs — вторая итерация мимики (Unity/EMOTIONS.md, «Вторая итерация»): беглые выражения с боевой камеры
// (портрет 1080×1920), замер «до/после» по пикселям области лица и по времени удержания; листы сравнения.
// Итог: Unity/Shots/emotions-2/, лог — EmotionsLog.
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoxProbe;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class ShotsTaker
    {
        static string Emo2Dir(string sub = null)
        {
            var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "emotions-2"));
            if (sub != null) d = Path.Combine(d, sub);
            Directory.CreateDirectory(d);
            return d;
        }

        // точки лица мягкой головы (как AnatomyFaceTests.Sample, без глаз и челюсти)
        static List<float3> FacePts(AnatHead a)
        {
            var pts = new List<float3>(); var g = a.head;
            for (int z = 1; z < g.dims.z - 1; z++)
                for (int y = 1; y < g.dims.y - 1; y++)
                    for (int x = 1; x < g.dims.x - 1; x++)
                    {
                        int i = g.Index(x, y, z); byte m = g.mat[i];
                        if (m != AnatMat.Skin && m != AnatMat.Lip && m != AnatMat.Brow && m != AnatMat.Lash && m != AnatMat.Stubble) continue;
                        var p = g.Local(x, y, z); if (p.z < 3f) continue;
                        if (g.mat[i + 1] != 0 && g.mat[i - 1] != 0 && g.mat[g.Index(x, y + 1, z)] != 0 && g.mat[g.Index(x, y - 1, z)] != 0 && g.mat[g.Index(x, y, z + 1)] != 0) continue;
                        if (((x + y + z) % 3) == 0) pts.Add(p);
                    }
            return pts;
        }

        static float FaceRms(List<float3> pts, float[] c0, float[] c1)
        {
            double s = 0;
            foreach (var p in pts) { float d = math.distance(AnatFace.Deform(p, AnatFace.RoleSoft, c0), AnatFace.Deform(p, AnatFace.RoleSoft, c1)); s += d * d; }
            return (float)math.sqrt(s / math.max(1, pts.Count));
        }

        static readonly string[] FleetKeys = { "windup", "strike", "jab", "dodged", "block" };
        static readonly string[] FleetNames = { "замах", "выпад", "джеб", "увернулся", "в блок" };
        static void Fire(FoeFace ff, int k)
        {
            switch (k)
            {
                case 0: ff.OnWindup(); break;
                case 1: ff.OnStrike(); break;
                case 2: ff.OnHit(0, false, (int)AnatZone.Cheek, false, false, 1); break;
                case 3: ff.OnDodged(); break;
                default: ff.OnFoeLanded(true, false); break;
            }
        }

        // Лицо бойца: прогрев 2 с покоя, затем событие k и кадры после него (k < 0 — без события). Детерминировано.
        static FoeFace FleetFace(FighterDef def, int k, int frames, List<float3> pts, out float[] c0, List<float> curve)
        {
            var ff = new FoeFace(def); ff.face.blinks = false;
            var st = new FightState(); st.hpMax = 100; st.hp = 100;
            for (int i = 0; i < 120; i++) ff.Tick(DT, st);
            c0 = (float[])ff.face.ch.Clone();
            if (k >= 0) Fire(ff, k);
            for (int i = 0; i < frames; i++) { ff.Tick(DT, st); curve?.Add(FaceRms(pts, c0, ff.face.ch)); }
            return ff;
        }

        public static string FleetLog = "";

        /// Беглые выражения с боевой камеры, портрет 1080×1920: покой бойца и пик каждого события (кадр с наибольшим сдвигом лица),
        /// средняя разница пикселей области лица «пик − покой» (0..255 по RGB), время на ≥ 70 % амплитуды. tag — «before» или «after».
        public static void EmotionFleeting(string tag)
        {
            const int W = 1080, H = 1920, CW = 300, CHt = 360;
            int[] foes = { 4, 7, 8, 6 };
            ShaderUtil.allowAsyncCompilation = false;
            var log = new StringBuilder();
            var dir = Emo2Dir("fleeting-" + tag);
            int SW = (FleetKeys.Length + 1) * CW, SH = foes.Length * CHt;
            var sheetPx = new Color32[SW * SH];   // текстуры — после NewScene (она выгружает «ничейные»)
            foreach (int fi in foes)
            {
                var d = NewFight(out var cam, out _, 6f, 6f, fi);
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                var run = new Run { d = d };
                run.Ticks(60); run.WaitOpen(200); run.Ticks(30);
                d.SetViewSize(W, H);
                var v = d.Head; var def = new RosterDef(42u).Get(fi);
                var pts = FacePts(v.a);
                // область лица на экране: проекция рамки лица (±6 см, от подбородка до бровей, перед)
                cam.targetTexture = rt;   // проекция — в размере кадра 1080×1920
                var m = v.root.localToWorldMatrix; float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
                foreach (var cx in new[] { -6f, 6f }) foreach (var cy in new[] { -11.5f, 5.5f }) foreach (var cz in new[] { 4f, 9f })
                {
                    var s = cam.WorldToScreenPoint(m.MultiplyPoint(new Vector3(cx, cy, cz) * 0.01f));
                    x0 = math.min(x0, s.x); x1 = math.max(x1, s.x); y0 = math.min(y0, s.y); y1 = math.max(y1, s.y);
                }
                cam.targetTexture = null;
                int rx0 = math.clamp((int)x0, 0, W - 1), rx1 = math.clamp((int)x1, 1, W), ry0 = math.clamp((int)y0, 0, H - 1), ry1 = math.clamp((int)y1, 1, H);
                Color32[] Render(FoeFace ff)
                {
                    ff.Apply(v); d.FoeFigure.gear.SetJaw(ff.JawAngle, ff.JawSide);
                    cam.targetTexture = rt; RenderTexture.active = rt; cam.Render();
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                    cam.targetTexture = null; RenderTexture.active = null;
                    return tex.GetPixels32();
                }
                void Crop(Color32[] px, int col, int row)
                {
                    int cxm = (rx0 + rx1) / 2, cym = (ry0 + ry1) / 2;
                    int ox = col * CW, oy = (foes.Length - 1 - row) * CHt;
                    for (int yy = 0; yy < CHt; yy++) for (int xx = 0; xx < CW; xx++)
                    {
                        int sx = math.clamp(cxm - CW / 2 + xx, 0, W - 1), sy = math.clamp(cym - CHt / 2 + yy, 0, H - 1);
                        sheetPx[(oy + yy) * SW + ox + xx] = px[sy * W + sx];
                    }
                }
                var basePx = Render(FleetFace(def, -1, 0, pts, out _, null));
                var baseCopy = (Color32[])basePx.Clone();
                int row0 = System.Array.IndexOf(foes, fi);
                Crop(baseCopy, 0, row0);
                File.WriteAllBytes(Path.Combine(dir, "foe" + (fi + 1) + "-rest.png"), tex.EncodeToPNG());
                var tmp = new FoeFace(def);
                log.AppendLine("FLEET " + tag + " foe=" + (fi + 1) + " «" + def.nickname + "» " + tmp.Temper.name + " faceRect=" + (rx1 - rx0) + "x" + (ry1 - ry0) + "px");
                for (int k = 0; k < FleetKeys.Length; k++)
                {
                    var curve = new List<float>();
                    var probe = FleetFace(def, k, 90, pts, out _, curve);
                    int pk = 0; for (int i = 1; i < curve.Count; i++) if (curve[i] > curve[pk]) pk = i;
                    float amp = curve[pk]; int hold = 0; foreach (var c in curve) if (c >= 0.7f * amp && amp > 0) hold++;
                    var ff = FleetFace(def, k, pk + 1, pts, out _, null);
                    var px = Render(ff);
                    double sum = 0; int n = 0;
                    for (int yy = ry0; yy < ry1; yy++) for (int xx = rx0; xx < rx1; xx++)
                    {
                        var a0 = baseCopy[yy * W + xx]; var a1 = px[yy * W + xx];
                        sum += (math.abs(a0.r - a1.r) + math.abs(a0.g - a1.g) + math.abs(a0.b - a1.b)) / 3.0; n++;
                    }
                    Crop(px, k + 1, row0);
                    File.WriteAllBytes(Path.Combine(dir, "foe" + (fi + 1) + "-" + FleetKeys[k] + ".png"), tex.EncodeToPNG());
                    log.AppendLine("FLEET " + tag + " foe=" + (fi + 1) + " " + FleetKeys[k] + " event=" + ff.lastEvent + " expr=" + (ff.face.ev >= 0 ? AnatEmo.Keys[ff.face.ev] : "-")
                        + " peakFrame=" + pk + " ampMm=" + F(amp * 10) + " hold70=" + F(hold * DT) + "s pxDiff=" + (sum / math.max(1, n)).ToString("0.000", Inv));
                }
                d.SetViewSize(PortW, PortH);
                Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
                EndFight(d);
            }
            var sheet = new Texture2D(SW, SH, TextureFormat.RGB24, false);
            sheet.SetPixels32(sheetPx); sheet.Apply();
            File.WriteAllBytes(Path.Combine(Emo2Dir(), "fleeting-" + tag + ".png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            AnatFace.Off();
            FleetLog = log.ToString().TrimEnd();
            File.WriteAllText(Path.Combine(Emo2Dir(), "fleeting-" + tag + ".txt"), FleetLog);
            Debug.Log(FleetLog);
        }
            // Клетки листа на диск: ряд клеток CW×CH → PNG (собирается в листы «до | после» функцией Emo2Compose).
        static void SaveStrip(List<Color32[]> cells, int cw, int ch, int cols, string path)
        {
            int rows = (cells.Count + cols - 1) / cols;
            var t = new Texture2D(cols * cw, rows * ch, TextureFormat.RGB24, false);
            var px = new Color32[cols * cw * rows * ch];
            for (int k = 0; k < cells.Count; k++)
            {
                int ox = (k % cols) * cw, oy = (rows - 1 - k / cols) * ch;
                for (int y = 0; y < ch; y++) System.Array.Copy(cells[k], y * cw, px, (oy + y) * cols * cw + ox, cw);
            }
            t.SetPixels32(px); t.Apply(); File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t);
        }

        static Color32[] RigCell(PortraitRig rig, int cw, int ch)
        {
            var rt = new RenderTexture(cw, ch, 24, RenderTextureFormat.ARGB32);
            var t = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            rig.Render(rt);
            RenderTexture.active = rt; t.ReadPixels(new Rect(0, 0, cw, ch), 0, 0); t.Apply(); RenderTexture.active = null;
            var px = t.GetPixels32(); Object.DestroyImmediate(t); rt.Release(); Object.DestroyImmediate(rt);
            return px;
        }

        /// Сосредоточенное и злость: анфас и три четверти, боец 9 крупно → emotions-2/fva-<tag>.png (4 клетки в ряд).
        public static void EmotionFocusAnger(string tag)
        {
            const int CW = 400, CH = 440;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var roster = new RosterDef(GameFlow.RosterSeed);
            var rig = new PortraitRig(Vector3.zero);
            rig.SetFighter(EmoFighter(roster, 8));
            var face = new AnatFace(Neutral, 7) { blinks = false };
            var cells = new List<Color32[]>();
            foreach (var yaw in new[] { 0f, 35f })
                foreach (var e in new[] { AnatEmo.Focus, AnatEmo.Anger })
                { Pose(rig, face, e); EmoCam(rig, yaw, 3f, 0.5f); cells.Add(RigCell(rig, CW, CH)); }
            SaveStrip(cells, CW, CH, 4, Path.Combine(Emo2Dir("parts"), "fva-" + tag + ".png"));
            AnatFace.Off(); rig.Dispose();
        }

        /// Бородатые бойцы лестницы с самым открытым ртом (удивление, челюсть на пределе канала 1): профиль и три четверти
        /// → emotions-2/parts/beard-<tag>.png (ряд на бойца, 2 клетки). Имена — в лог.
        public static string BeardLog = "";
        public static void EmotionBeard(string tag)
        {
            const int CW = 360, CH = 400;
            ShaderUtil.allowAsyncCompilation = false;   // новый вариант шейдера бороды иначе пропускает первый кадр
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var roster = new RosterDef(GameFlow.RosterSeed);
            var rig = new PortraitRig(Vector3.zero);
            var cells = new List<Color32[]>(); var log = new StringBuilder();
            var face = new AnatFace(Neutral, 7) { blinks = false };
            for (int i = 0; i < RosterDef.Count; i++)
            {
                var f = roster.Get(i); if (f.head.beard == BeardStyle.None) continue;
                f.head.wear = HeadWear.None; f.head.plaster = false; f.head.earring = false;
                rig.SetFighter(f);
                foreach (var r in rig.Body.root.GetComponentsInChildren<Renderer>())   // руки и перчатки закрывают профиль
                    if (r.name.StartsWith("glove") || r.name.StartsWith("foreArm") || r.name.StartsWith("upperArm")) r.enabled = false;
                face.Clear(); face.Play(AnatEmo.Surprise, 1f, 10f, 9); face.Snap();
                var c = (float[])face.ch.Clone(); c[AnatFace.Jaw] = 1f;
                AnatFace.Send(c, rig.View.root, rig.View.a, rig.View.gone);
                rig.Body.gear.SetJaw(c[AnatFace.Jaw] * AnatFace.JawRad, c[AnatFace.JawSide] * AnatFace.JawSideCm);
                foreach (var yaw in new[] { 80f, 40f }) { EmoCam(rig, yaw, 3f, 0.55f); cells.Add(RigCell(rig, CW, CH)); }
                log.AppendLine("BEARD " + tag + " fighter=" + (i + 1) + " " + f.name + " «" + f.nickname + "» beard=" + f.head.beard);
            }
            SaveStrip(cells, CW, CH, 2, Path.Combine(Emo2Dir("parts"), "beard-" + tag + ".png"));
            AnatFace.Off(); rig.Dispose();
            BeardLog = log.ToString().TrimEnd();
        }
            // Попарная разница выражений той же метрикой, что AnatomyFaceTests.EachExpressionDistinct (мм RMS): строки — сосредоточенное и злость.
        public static string EmoPairs()
        {
            var a = AnatomyBuilder.Build();
            var pts = new List<float3>(); var roles = new List<int>(); var g = a.head;
            for (int z = 1; z < g.dims.z - 1; z++) for (int y = 1; y < g.dims.y - 1; y++) for (int x = 1; x < g.dims.x - 1; x++)
            {
                int i = g.Index(x, y, z); byte m = g.mat[i];
                if (m != AnatMat.Skin && m != AnatMat.Lip && m != AnatMat.Brow && m != AnatMat.Lash && m != AnatMat.Stubble) continue;
                var p = g.Local(x, y, z); if (p.z < 3f) continue;
                if (g.mat[i + 1] != 0 && g.mat[i - 1] != 0 && g.mat[g.Index(x, y + 1, z)] != 0 && g.mat[g.Index(x, y - 1, z)] != 0 && g.mat[g.Index(x, y, z + 1)] != 0) continue;
                if (((x + y + z) & 1) == 0) { pts.Add(p); roles.Add(AnatFace.RoleSoft); }
            }
            foreach (var (gr, role) in new[] { (a.eyeL, AnatFace.RoleEyeL), (a.eyeR, AnatFace.RoleEyeR), (a.mand, AnatFace.RoleJaw) })
                for (int i = 0; i < gr.mat.Length; i += 7)
                    if (gr.mat[i] != 0) { int x = i % gr.dims.x, y = (i / gr.dims.x) % gr.dims.y, z = i / (gr.dims.x * gr.dims.y); pts.Add(gr.World(gr.Local(x, y, z))); roles.Add(role); }
            var rest = new float3[pts.Count]; var po = new float3[AnatEmo.Count][];
            var z0 = new float[AnatFace.NC]; for (int i = 0; i < pts.Count; i++) rest[i] = AnatFace.Deform(pts[i], roles[i], z0);
            for (int e = 0; e < AnatEmo.Count; e++)
            {
                var f = new AnatFace(Neutral) { blinks = false }; f.Play(e, 1f, 10f, 9); f.Snap();
                po[e] = new float3[pts.Count]; for (int i = 0; i < pts.Count; i++) po[e][i] = AnatFace.Deform(pts[i], roles[i], f.ch);
            }
            var sb = new StringBuilder(); float minAll = 99; string minPair = "";
            for (int e1 = 0; e1 < AnatEmo.Count; e1++)
            {
                if (e1 == AnatEmo.Focus || e1 == AnatEmo.Anger) sb.Append(AnatEmo.Keys[e1] + ":");
                for (int e2 = 0; e2 < AnatEmo.Count; e2++)
                {
                    if (e2 == e1) continue;
                    double s = 0; int n = 0;
                    for (int i = 0; i < pts.Count; i++) { float d = math.distance(po[e1][i], po[e2][i]); if (math.distance(po[e1][i], rest[i]) + math.distance(po[e2][i], rest[i]) > 0.02f) { s += d * d; n++; } }
                    float r = (float)math.sqrt(s / math.max(n, 1)) * 10;
                    if (e1 == AnatEmo.Focus || e1 == AnatEmo.Anger) sb.Append(" " + AnatEmo.Keys[e2] + "=" + r.ToString("0.00", Inv));
                    if (e2 > e1 && r < minAll) { minAll = r; minPair = AnatEmo.Keys[e1] + "/" + AnatEmo.Keys[e2]; }
                }
                if (e1 == AnatEmo.Focus || e1 == AnatEmo.Anger) sb.AppendLine();
            }
            sb.Append("min pair " + minPair + " " + minAll.ToString("0.00", Inv));
            a.Dispose();
            return sb.ToString();
        }
            /// Шов бороды при самом открытом рте: для каждого бородатого бойца лестницы — расхождение соседних вершин бороды
        /// (изменение длины рёбер меша, мм) при челюсти на пределе (канал 1) и со сдвигом вбок; «до» — та же сетка с весами старой
        /// разрезки (выше шва губ 0, ниже 1); «кожа» — насколько вершина бороды у кожи (≤ 5 мм от неё) отходит от своей точки кожи
        /// при полной мимике (удивление + челюсть 1), мм: плюс — просвет.
        public static string BeardSeamCheck()
        {
            var sb = new StringBuilder();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var roster = new RosterDef(GameFlow.RosterSeed);
            var rig = new PortraitRig(Vector3.zero);
            var face = new AnatFace(Neutral, 7) { blinks = false }; face.Play(AnatEmo.Surprise, 1f, 10f, 9); face.Snap();
            float[] ch = (float[])face.ch.Clone(); ch[AnatFace.Jaw] = 1f;
            float3 pv = AnatFace.JawPivot;
            float3 Rot(float3 p, float a, float side) { float c = math.cos(a), s = math.sin(a); var v = p - pv; return pv + new float3(v.x, v.y * c - v.z * s, v.y * s + v.z * c) + new float3(side, 0, 0); }
            float worstNew = 0, worstOld = 0, worstGap = 0;
            for (int i = 0; i < RosterDef.Count; i++)
            {
                var f = roster.Get(i); if (f.head.beard == BeardStyle.None) continue;
                rig.SetFighter(f);
                MeshFilter mf = null; foreach (var m in rig.Body.root.GetComponentsInChildren<MeshFilter>(true)) if (m.name == "beard") mf = m;
                if (mf == null) { sb.AppendLine("SEAM fighter=" + (i + 1) + " нет меша бороды"); continue; }
                var mesh = mf.sharedMesh; var vs = mesh.vertices; var uv = new List<Vector2>(); mesh.GetUVs(1, uv); var tri = mesh.triangles;
                float mNew = 0, mOld = 0, mGap = 0, mBack = 0, mGapOld = 0, seamOld = 0; int gapN = 0, gapNOld = 0, near = 0, back = 0; float3 worstAt = 0;
                foreach (var side in new[] { 0f, 0.5f * AnatFace.JawSideCm })
                {
                    float a = AnatFace.JawRad;
                    var pn = new float3[vs.Length]; var po = new float3[vs.Length];
                    for (int k = 0; k < vs.Length; k++)
                    {
                        float3 p = (float3)(vs[k] * 100f); float w = uv[k].x, wo = p.y < AnatFace.YL ? 1f : 0f;
                        pn[k] = Rot(p, a * w, side * w); po[k] = Rot(p, a * wo, side * wo);
                        if (math.abs(p.y - AnatFace.YL) < 0.16f) seamOld = math.max(seamOld, math.distance(Rot(p, a, side), p));   // старый разрез: две копии вершины
                    }
                    for (int t = 0; t < tri.Length; t += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            int i0 = tri[t + e], i1 = tri[t + (e + 1) % 3];
                            float l0 = math.distance((float3)(vs[i0] * 100f), (float3)(vs[i1] * 100f));
                            mNew = math.max(mNew, math.abs(math.distance(pn[i0], pn[i1]) - l0));
                            mOld = math.max(mOld, math.abs(math.distance(po[i0], po[i1]) - l0));
                        }
                    if (side == 0f)
                        for (int k = 0; k < vs.Length; k++)
                        {
                            float3 p = (float3)(vs[k] * 100f), sk = HeadGear.BeardAnchor(vs[k] * 100f);
                            float d0 = math.distance(p, sk); if (d0 > 0.5f) continue; near++;
                            float3 s1 = AnatFace.Deform(sk, AnatFace.RoleSoft, ch);
                            float gap = math.distance(pn[k], s1) - d0;
                            if (sk.z < 3.2f) { back++; mBack = math.max(mBack, gap); continue; }   // кожа за z = 3 мимикой не двигается
                            if (gap > mGap) { mGap = gap; worstAt = sk; } if (gap > 0.05f) gapN++;
                            float gapO = math.distance(po[k], s1) - d0; mGapOld = math.max(mGapOld, gapO); if (gapO > 0.05f) gapNOld++;
                        }
                }
                // на старой разрезке вершины шва — два разных меша; ребро через шов здесь заменяет пару совпадающих вершин
                sb.AppendLine("SEAM fighter=" + (i + 1) + " «" + f.nickname + "» " + f.head.beard + " verts=" + vs.Length + " edgeDiv new=" + (mNew * 10).ToString("0.000", Inv)
                    + "mm old=" + (mOld * 10).ToString("0.0", Inv) + "mm | skin gap max=" + (mGap * 10).ToString("0.00", Inv) + "mm, >0.5mm " + gapN + "/" + (near - back) + " (до: max=" + (mGapOld * 10).ToString("0.00", Inv) + "mm, >0.5mm " + gapNOld + ") worst at " + worstAt + " | шов разреза до=" + (seamOld * 10).ToString("0.0", Inv) + "mm | у неподвижной кожи (z<3.2) " + back + " max=" + (mBack * 10).ToString("0.00", Inv) + "mm");
                worstNew = math.max(worstNew, mNew); worstOld = math.max(worstOld, mOld); worstGap = math.max(worstGap, mGap);
            }
            sb.Append("SEAM worst edgeDiv new=" + (worstNew * 10).ToString("0.000", Inv) + "mm old=" + (worstOld * 10).ToString("0.0", Inv) + "mm skinGap=" + (worstGap * 10).ToString("0.00", Inv) + "mm");
            AnatFace.Off(); rig.Dispose();
            return sb.ToString();
        }
    }
}
