// FacesShots.cs — разные лица соперников (Unity/FACES.md): лист бойцов лестницы анфас, два контрастных бойца анфас, в три
// четверти и в бою от первого лица (портрет телефона), голова после ударов по волосам; замер кадра удара «лысый | объёмные».
// Итог: Unity/Shots/faces/*.png, лог — FacesLog.
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoxProbe;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class ShotsTaker
    {
        public static string FacesLog = "";

        static string FacesDir()
        {
            var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "faces"));
            Directory.CreateDirectory(d);
            return d;
        }

        static string LookText(in FighterDef f)
        {
            var lk = FighterGen.AnatLookOf(f);
            return AnatLook.ToneNames[f.colors.skinTone] + " + " + AnatHair.Names[lk.hair] + " #" + ColorUtility.ToHtmlStringRGB(lk.hairCol)
                 + " (" + f.head.hair + (f.head.wear != HeadWear.None ? ", " + f.head.wear : "") + (f.head.beard != BeardStyle.None ? ", борода " + f.head.beard : "") + ")";
        }

        // Камера рига: голова крупно; yaw — поворот вокруг головы (0 — анфас), pitch — сверху.
        static void HeadCam(PortraitRig rig, float yaw, float pitch, float dist = 0.62f)
        {
            Vector3 at = rig.Body.headMount.position + Vector3.up * 0.02f;
            Vector3 dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;   // pitch > 0 — камера выше головы
            Vector3 eye = at + dir * dist;
            rig.cam.fieldOfView = 26f;
            rig.cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));
        }

        static void SavePng(RenderTexture rt, string path)
        {
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(tex);
        }

        static void RigShot(PortraitRig rig, string path, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rig.Render(rt); SavePng(rt, path); rt.Release(); Object.DestroyImmediate(rt);
        }

        // Пара для крупных снимков: самый светлый и самый тёмный бойцы лестницы с разными причёсками.
        static (int a, int b) Contrast(RosterDef r)
        {
            int a = 0, b = 0;
            for (int i = 0; i < RosterDef.Count; i++)
            {
                var f = r.Get(i);
                if (f.colors.skinTone < r.Get(a).colors.skinTone || f.colors.skinTone == r.Get(a).colors.skinTone && f.head.wear == HeadWear.None && r.Get(a).head.wear != HeadWear.None) a = i;
                if (f.colors.skinTone > r.Get(b).colors.skinTone || f.colors.skinTone == r.Get(b).colors.skinTone && f.head.wear == HeadWear.None && r.Get(b).head.wear != HeadWear.None) b = i;
            }
            return (a, b);
        }

        [MenuItem("VoxGame/Faces Shots")]
        public static void FacesShots()
        {
            var log = new StringBuilder();
            var dir = FacesDir();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var roster = new RosterDef(GameFlow.RosterSeed);
            for (int i = 0; i < RosterDef.Count; i++) { var f = roster.Get(i); log.AppendLine("FACE " + (i + 1) + " " + f.name + " «" + f.nickname + "»: " + LookText(f)); }

            // 1) лист анфас: 4 × 3, по строкам слева направо в порядке лестницы
            const int Cols = 4, Rows = 3, CW = 360, CH = 420;
            var sheet = new Texture2D(Cols * CW, Rows * CH, TextureFormat.RGB24, false);
            var rig = new PortraitRig(Vector3.zero);
            var cell = new RenderTexture(CW, CH, 24, RenderTextureFormat.ARGB32);
            var px = new Texture2D(CW, CH, TextureFormat.RGB24, false);
            for (int i = 0; i < RosterDef.Count; i++)
            {
                rig.SetFighter(roster.Get(i));
                HeadCam(rig, 0f, 4f, 0.78f);
                rig.Render(cell);
                RenderTexture.active = cell; px.ReadPixels(new Rect(0, 0, CW, CH), 0, 0); px.Apply(); RenderTexture.active = null;
                int cx = i % Cols, cy = Rows - 1 - i / Cols;
                sheet.SetPixels(cx * CW, cy * CH, CW, CH, px.GetPixels());
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, "sheet-front.png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet); Object.DestroyImmediate(px); cell.Release(); Object.DestroyImmediate(cell);

            // 2) два контрастных бойца: анфас и три четверти
            var (ca, cb) = Contrast(roster);
            foreach (var (k, tag) in new[] { (ca, "a"), (cb, "b") })
            {
                rig.SetFighter(roster.Get(k));
                HeadCam(rig, 0f, 4f); RigShot(rig, Path.Combine(dir, tag + "-front.png"), 540, 640);
                HeadCam(rig, 38f, 10f); RigShot(rig, Path.Combine(dir, tag + "-34.png"), 540, 640);
                log.AppendLine("FACE contrast " + tag + " = " + (k + 1) + " " + roster.Get(k).name + ": " + LookText(roster.Get(k)));
            }

            // 3) удары по волосам: боец с объёмными волосами, тяжёлые и средние удары сверху-спереди по своду
            {
                var f = roster.Get(ca);
                f.head.hair = HairStyle.FullHair; f.head.wear = HeadWear.None;
                rig.SetFighter(f);
                var v = rig.View; int before = Count(v, AnatMat.Hair);
                float3[] from = { new float3(0, 40, 12), new float3(4, 40, 8), new float3(-4, 40, 10), new float3(0, 40, 4) };
                int hits = 0;
                for (int k = 0; k < 8; k++)
                {
                    var o = from[k % from.Length]; var to = new float3(k % 2 == 0 ? 1.5f : -1.5f, 9, 2);
                    if (!v.Pick(new Ray(v.ToWorld(o), (Vector3)math.normalize(to - o)), out var h, out _, out _)) continue;
                    v.Hit(h, v.pickDir, k % 2 == 0 ? 2 : 1); hits++;
                    for (int t = 0; t < 4; t++) v.Tick(1 / 30f);
                }
                v.Settle(true); v.MeshDone();
                log.AppendLine("FACE hair hits=" + hits + " hair " + before + " -> " + Count(v, AnatMat.Hair) + " dropped(no support)=" + v.hairDropped);
                HeadCam(rig, 25f, 50f, 0.7f); RigShot(rig, Path.Combine(dir, "hair-hit.png"), 540, 640);
                HeadCam(rig, 0f, 4f); RigShot(rig, Path.Combine(dir, "hair-hit-front.png"), 540, 640);
            }
            rig.Dispose();

            // 4) боевой вид от первого лица, портрет телефона
            foreach (var (k, tag) in new[] { (ca, "a"), (cb, "b") })
            {
                var d = NewFight(out var cam, out var ui, 4f, 3f, k);
                var run = new Run { d = d };
                run.Ticks(90);
                d.SetViewSize(PortW, PortH);
                Shot(cam, ui, PortW, PortH, Path.Combine(dir, tag + "-fight-p.png"));
                EndFight(d);
            }

            FacesLog = log.ToString().TrimEnd();
            Debug.Log(FacesLog);
        }

        static int Count(AnatView v, byte m) { int n = 0; foreach (var x in v.a.head.mat) if (x == m) n++; return n; }

        // Замер кадра удара «лысый | объёмные» тем же боем, что SpringBench: 1-й соперник лестницы, причёска подменена, без убора.
        public static void FacesBench(int hits = 40)
        {
            var log = new StringBuilder();
            var styles = new[] { HairStyle.Bald, HairStyle.FullHair, HairStyle.Bald, HairStyle.FullHair };
            var all = new Dictionary<HairStyle, List<double>> { { HairStyle.Bald, new List<double>() }, { HairStyle.FullHair, new List<double>() } };
            var anat = new Dictionary<HairStyle, List<double>> { { HairStyle.Bald, new List<double>() }, { HairStyle.FullHair, new List<double>() } };
            var hitMs = new Dictionary<HairStyle, List<double>> { { HairStyle.Bald, new List<double>() }, { HairStyle.FullHair, new List<double>() } };
            foreach (var st in styles)
            {
                var d = NewFight(out var cam, out var ui, 6f, 4f, 0, false, f => { f.head.hair = st; f.head.wear = HeadWear.None; return f; });
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
                        if (got >= WarmHits) { all[st].Add(d.lastHitFrameMs); anat[st].Add(d.lastAnatTickMs + d.lastHitMs + d.lastPickMs); hitMs[st].Add(d.lastHitMs); }
                        got++;
                    }
                    for (int i = 0; i < 20; i++) run.Tick();
                }
                log.AppendLine("FACES_BENCH run hair=" + st + " hits=" + (got - WarmHits) + " finished=" + d.Finished + " hairCells=" + Count(d.Head, AnatMat.Hair) + " dropped=" + d.Head.hairDropped);
                EndFight(d);
            }
            foreach (var st in new[] { HairStyle.Bald, HairStyle.FullHair })
                log.AppendLine("FACES_BENCH hitFrame hair=" + st + " " + Dist(all[st]) + " | anat(pick+hit+tick) " + Dist(anat[st]) + " | hit " + Dist(hitMs[st]));
            FacesLog = log.ToString().TrimEnd();
            Debug.Log(FacesLog);
        }
    }
}
