// EmotionShots.cs — мимика соперников (Unity/EMOTIONS.md): лист всех выражений одного бойца крупно, кадр на выражение,
// 3 бойца × 3 выражения (характеры), кадры боя (портрет 1080×2340 и ландшафт), мимика на повреждённой голове; замер кадра.
// Итог: Unity/Shots/emotions/*.png, лог — EmotionsLog.
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
        public static string EmotionsLog = "";

        static string EmoDir()
        {
            var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "emotions"));
            Directory.CreateDirectory(d);
            return d;
        }

        // боец для листа: светлый тон, без бороды и убора (накладки бороды не двигаются с челюстью)
        static FighterDef EmoFighter(RosterDef r, int i)
        {
            var f = r.Get(i); f.head.beard = BeardStyle.None; f.head.wear = HeadWear.None; f.head.plaster = false; f.head.earring = false; return f;
        }

        static readonly AnatTemper Neutral = new AnatTemper { name = "ровный", mood = AnatEmo.Calm, moodW = 0f, gain = 1f, blink = 0.25f, side = 1 };

        static void Pose(PortraitRig rig, AnatFace face, int e)
        {
            face.Clear(); if (e >= 0) face.Play(e, 1f, 10f, 9); face.Snap();
            face.Apply(rig.View.root, rig.View.a, rig.View.gone);
        }

        static void Blit(Texture2D sheet, RenderTexture rt, Texture2D px, int x, int y)
        {
            RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); px.Apply(); RenderTexture.active = null;
            sheet.SetPixels(x, y, rt.width, rt.height, px.GetPixels());
        }

        // голова крупно, центр — между глазами и ртом
        static void EmoCam(PortraitRig rig, float yaw, float pitch, float dist)
        {
            Vector3 at = rig.Body.headMount.position + rig.Body.headMount.rotation * new Vector3(0f, -0.018f, 0f);
            Vector3 dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            Vector3 eye = at + dir * dist;
            rig.cam.fieldOfView = 26f;
            rig.cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));
        }

        [MenuItem("VoxGame/Emotion Shots")]
        public static void EmotionShots()
        {
            var all = new StringBuilder();
            EmotionSheet(); all.AppendLine(EmotionsLog);
            EmotionTempers(); all.AppendLine(EmotionsLog);
            EmotionDamaged(); all.AppendLine(EmotionsLog);
            EmotionFight(); all.AppendLine(EmotionsLog);
            EmotionsLog = all.ToString().TrimEnd();
            Debug.Log(EmotionsLog);
        }

        static void StepFace(FoeFace ff, float sec) { var st = new FightState(); st.hpMax = 100; st.hp = 100; for (float t = 0; t < sec; t += DT) ff.Tick(DT, st); }

        // 3 бойца лестницы с разным характером × 3 случая: покой, игрок промахнулся, свой удар попал.
        public static void EmotionTempers()
        {
            var log = new StringBuilder();
            var dir = EmoDir();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var roster = new RosterDef(GameFlow.RosterSeed);
            int[] who = { 3, 7, 6 };
            const int CW = 360, CH = 400;
            var sheet = new Texture2D(3 * CW, who.Length * CH, TextureFormat.RGB24, false);
            var cell = new RenderTexture(CW, CH, 24, RenderTextureFormat.ARGB32);
            var px = new Texture2D(CW, CH, TextureFormat.RGB24, false);
            var rig = new PortraitRig(Vector3.zero);
            for (int r = 0; r < who.Length; r++)
            {
                var def = roster.Get(who[r]); def.head.wear = HeadWear.None; def.head.plaster = false;
                rig.SetFighter(def);
                for (int c = 0; c < 3; c++)
                {
                    var ff = new FoeFace(def); ff.face.blinks = false;
                    ff.face.Snap();
                    if (c == 1) ff.OnPlayerMiss(); else if (c == 2) ff.OnFoeLanded(false, false);
                    StepFace(ff, c == 0 ? 0.1f : 0.6f);
                    ff.Apply(rig.View); rig.Body.gear.SetJaw(ff.JawAngle, ff.JawSide);
                    EmoCam(rig, 0f, 3f, 0.6f);
                    rig.Render(cell);
                    Blit(sheet, cell, px, c * CW, (who.Length - 1 - r) * CH);
                    RigShot(rig, Path.Combine(dir, "temper-" + (who[r] + 1) + "-" + c + ".png"), 540, 600);
                    log.AppendLine("EMO temper " + (who[r] + 1) + " " + def.name + " «" + def.nickname + "» " + ff.Temper.name + " case" + c + " event=" + ff.lastEvent + " dominant=" + AnatEmo.Names[ff.face.Dominant]);
                }
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, "tempers.png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet); Object.DestroyImmediate(px); cell.Release(); Object.DestroyImmediate(cell);
            AnatFace.Off(); rig.Dispose();
            EmotionsLog = log.ToString().TrimEnd();
        }

        // Мимика на повреждённой голове: удары в рот, щёку и бровь, затем «спокойное | оскал | боль» — выбитое не возвращается.
        public static void EmotionDamaged()
        {
            var log = new StringBuilder();
            var dir = EmoDir();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var roster = new RosterDef(GameFlow.RosterSeed);
            var rig = new PortraitRig(Vector3.zero);
            rig.SetFighter(EmoFighter(roster, 8));
            var v = rig.View;
            float3[] to = { new float3(0.9f, -5.3f, 8.5f), new float3(-0.8f, -5.0f, 8.5f), new float3(3.0f, -1.0f, 7.0f), new float3(-2.6f, 3.6f, 7.5f), new float3(0.4f, -5.6f, 8.6f) };
            int[] str = { 2, 2, 1, 1, 2 };
            int hits = 0, removed = 0;
            for (int k = 0; k < to.Length; k++)
            {
                var o = to[k] + new float3(0, 0, 30);
                if (!v.Pick(new Ray(v.ToWorld(o), Vector3.back), out var h, out _, out _)) continue;
                removed += v.Hit(h, v.pickDir, str[k]); hits++;
                for (int t = 0; t < 6; t++) v.Tick(1 / 30f);
            }
            for (int t = 0; t < 30; t++) v.Tick(1 / 30f);
            v.Settle(true); v.MeshDone();
            int gone = 0; foreach (var g in v.gone) if (g) gone++;
            int tris0 = v.Triangles;
            var face = new AnatFace(Neutral, 3) { blinks = false };
            int[] es = { AnatEmo.Calm, AnatEmo.Rage, AnatEmo.Pain };
            const int CW = 400, CH = 440;
            var sheet = new Texture2D(es.Length * CW, CH, TextureFormat.RGB24, false);
            var cell = new RenderTexture(CW, CH, 24, RenderTextureFormat.ARGB32);
            var px = new Texture2D(CW, CH, TextureFormat.RGB24, false);
            for (int i = 0; i < es.Length; i++)
            {
                Pose(rig, face, es[i]);
                EmoCam(rig, 0f, 3f, 0.55f);
                rig.Render(cell);
                Blit(sheet, cell, px, i * CW, 0);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, "damaged.png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet); Object.DestroyImmediate(px); cell.Release(); Object.DestroyImmediate(cell);
            log.AppendLine("EMO damaged hits=" + hits + " removed=" + removed + " bodiesGone=" + gone + " tris before/after expressions " + tris0 + "/" + v.Triangles);
            AnatFace.Off(); rig.Dispose();
            EmotionsLog = log.ToString().TrimEnd();
        }

        // Бой от первого лица: боль в момент удара, насмешка после своего попадания, мало здоровья; портрет 1080×2340 и ландшафт.
        public static void EmotionFight(int foe = 8)
        {
            var log = new StringBuilder();
            var dir = EmoDir();
            const int PW = 1080, PH = 2340;
            // 1) удар: боль
            {
                var d = NewFight(out var cam, out var ui, 6f, 6f, foe);
                var run = new Run { d = d };
                run.Ticks(60);
                run.WaitOpen(200);
                int seq = d.hitFrameSeq;
                run.Punch(PunchType.Hook, 1, 1.3f);
                for (int i = 0; i < 20 && d.hitFrameSeq == seq; i++) run.Tick();
                run.Ticks(14);
                log.AppendLine("EMO fight-hit event=" + d.FoeMimic.lastEvent + " dominant=" + AnatEmo.Names[d.FoeMimic.face.Dominant] + " w=" + F(d.FoeMimic.face.w[d.FoeMimic.face.Dominant]));
                d.SetViewSize(PW, PH); Shot(cam, ui, PW, PH, Path.Combine(dir, "fight-hit.png"));
                d.SetViewSize(PH, PW); Shot(cam, ui, PH, PW, Path.Combine(dir, "fight-hit-land.png"));
                d.SetViewSize(PortW, PortH);
                EndFight(d);
            }
            // 2) свой удар попал по игроку: насмешка или торжество
            {
                var d = NewFight(out var cam, out var ui, 6f, 6f, foe);
                var run = new Run { d = d, defend = false };
                int t = 0;
                for (; t < 900 && !d.FoeMimic.lastEvent.StartsWith("landed"); t++) run.Tick();
                run.Ticks(14);
                log.AppendLine("EMO fight-taunt ticks=" + t + " event=" + d.FoeMimic.lastEvent + " dominant=" + AnatEmo.Names[d.FoeMimic.face.Dominant]);
                d.SetViewSize(PW, PH); Shot(cam, ui, PW, PH, Path.Combine(dir, "fight-taunt.png"));
                d.SetViewSize(PortW, PortH);
                EndFight(d);
            }
            // 3) мало здоровья: страх и усталость, дыхание ртом
            {
                var d = NewFight(out var cam, out var ui, 0.9f, 20f, foe);
                var run = new Run { d = d };
                run.Ticks(60);
                var combo = new[] { PunchType.Hook, PunchType.Upper, PunchType.Jab };
                for (int k = 0; k < 60 && !d.Finished && d.State.hp > d.State.hpMax * 0.22f; k++)
                {
                    run.WaitOpen(200);
                    run.Punch(combo[k % combo.Length], k % 2 == 0 ? 1 : -1, 1.0f);
                    run.Ticks(24);
                }
                d.FoeMimic.face.Clear();
                run.Ticks(50); for (int i = 0; i < 150 && d.FoeMimic.face.ev >= 0; i++) run.Tick(); run.Ticks(20);
                log.AppendLine("EMO fight-low-hp hp=" + F(d.State.hp / d.State.hpMax) + " finished=" + d.Finished + " fatigue=" + F(d.FoeMimic.face.fatigue) + " fear=" + F(d.FoeMimic.face.fear)
                    + " dominant=" + AnatEmo.Names[d.FoeMimic.face.Dominant] + " event=" + d.FoeMimic.lastEvent);
                d.SetViewSize(PW, PH); Shot(cam, ui, PW, PH, Path.Combine(dir, "fight-low-hp.png"));
                d.SetViewSize(PortW, PortH);
                EndFight(d);
            }
            EmotionsLog = log.ToString().TrimEnd();
        }

        public static void EmotionSheet(int who = 8, float dist = 0.6f, string outDir = null, string sheetName = "sheet.png", bool singles = true)
        {
            var log = new StringBuilder();
            var dir = outDir ?? EmoDir();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var roster = new RosterDef(GameFlow.RosterSeed);
            var rig = new PortraitRig(Vector3.zero);
            rig.SetFighter(EmoFighter(roster, who));
            var face = new AnatFace(Neutral, 7);
            const int Cols = 5, CW = 360, CH = 400; int rows = (AnatEmo.Count + Cols - 1) / Cols;
            var sheet = new Texture2D(Cols * CW, rows * CH, TextureFormat.RGB24, false);
            var bg = new Color[Cols * CW * rows * CH]; for (int i = 0; i < bg.Length; i++) bg[i] = new Color(0.115f, 0.115f, 0.135f); sheet.SetPixels(bg);
            var cell = new RenderTexture(CW, CH, 24, RenderTextureFormat.ARGB32);
            var px = new Texture2D(CW, CH, TextureFormat.RGB24, false);
            for (int e = 0; e < AnatEmo.Count; e++)
            {
                Pose(rig, face, e);
                EmoCam(rig, 0f, 3f, dist);
                rig.Render(cell);
                Blit(sheet, cell, px, (e % Cols) * CW, (rows - 1 - e / Cols) * CH);
                if (singles) RigShot(rig, Path.Combine(dir, "emo-" + AnatEmo.Keys[e] + ".png"), 540, 600);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, sheetName), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet); Object.DestroyImmediate(px); cell.Release(); Object.DestroyImmediate(cell);
            AnatFace.Off();
            rig.Dispose();
            log.AppendLine("EMO sheet fighter=" + (who + 1) + " " + roster.Get(who).name);
            EmotionsLog = log.ToString().TrimEnd();
            Debug.Log(EmotionsLog);
        }

        // Замер A/B «без мимики | с мимикой» с чередованием (как ARMS-BODY.md): 6 боёв в порядке без, с, с, без, без, с,
        // по 40 ударов; кадр удара — lastHitFrameMs, кадр боя — d.Tick без удара, отрисовка 1080×2400 с ожиданием GPU каждый 5-й кадр.
        // Отдельно — своё время мимики (FoeFace.Tick + Apply) и выделения памяти на него за 3000 кадров.
        public static void EmotionBench(int hits = 40)
        {
            bool wasA = AnatFace.Enabled, wasF = FoeFace.Enabled;
            var log = new StringBuilder();
            var hit = new Dictionary<bool, List<double>> { { false, new List<double>() }, { true, new List<double>() } };
            var frame = new Dictionary<bool, List<double>> { { false, new List<double>() }, { true, new List<double>() } };
            var rend = new Dictionary<bool, List<double>> { { false, new List<double>() }, { true, new List<double>() } };
            var tris = new Dictionary<bool, int> { { false, 0 }, { true, 0 } };
            try
            {
                int r = 0;
                foreach (bool on in new[] { false, true, true, false, false, true })
                {
                    AnatFace.Enabled = on; FoeFace.Enabled = on;
                    var h1 = new List<double>(); var f1 = new List<double>(); var r1 = new List<double>();
                    var d = NewFight(out var bcam, out _, 6f, 4f);
                    d.SetViewSize(1080, 2400);
                    var rt = new RenderTexture(1080, 2400, 24, RenderTextureFormat.ARGB32);
                    var px = new Texture2D(1, 1, TextureFormat.RGB24, false);
                    var run = new Run { d = d };
                    run.Ticks(90);
                    tris[on] = d.Head.Triangles;
                    var combo = new[] { PunchType.Hook, PunchType.Jab, PunchType.Upper, PunchType.Jab };
                    int got = 0, tries = 0;
                    var tsw = new System.Diagnostics.Stopwatch();
                    while (got < hits + WarmHits && tries < hits * 4 && !d.Finished)
                    {
                        tries++;
                        run.WaitOpen(150);
                        int seq = d.hitFrameSeq;
                        run.Punch(combo[tries % combo.Length], tries % 2 == 0 ? 1 : -1);
                        run.Tick();
                        if (d.hitFrameSeq != seq) { if (got >= WarmHits) h1.Add(d.lastHitFrameMs); got++; }
                        for (int i = 0; i < 20; i++)
                        {
                            int s2 = d.hitFrameSeq;
                            tsw.Restart(); d.Tick(DT); tsw.Stop();
                            if (d.hitFrameSeq == s2 && got > WarmHits) f1.Add(tsw.Elapsed.TotalMilliseconds);
                            if (i % 5 == 0 && got > WarmHits)
                            {
                                bcam.targetTexture = rt; RenderTexture.active = rt;
                                tsw.Restart(); bcam.Render(); px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); tsw.Stop();
                                bcam.targetTexture = null; RenderTexture.active = null;
                                r1.Add(tsw.Elapsed.TotalMilliseconds);
                            }
                        }
                    }
                    log.AppendLine("EMO_BENCH run=" + (r++) + " on=" + on + " hits=" + h1.Count + " hitFrame " + Dist(h1) + " | fightFrame " + Dist(f1) + " | render " + Dist(r1));
                    hit[on].AddRange(h1); frame[on].AddRange(f1); rend[on].AddRange(r1);
                    // своё время мимики в этом же бою (только «с»)
                    if (on && r == 2)
                    {
                        var ff = d.FoeMimic; var st = d.State;
                        for (int i = 0; i < 200; i++) { ff.Tick(DT, st); ff.Apply(d.Head); }
                        long b0 = System.GC.GetAllocatedBytesForCurrentThread();
                        tsw.Restart();
                        for (int i = 0; i < 3000; i++) { if (i % 90 == 0) ff.OnHit(i % 3, false, (int)AnatZone.Cheek, false, false, 1); ff.Tick(DT, st); ff.Apply(d.Head); }
                        tsw.Stop();
                        long b1 = System.GC.GetAllocatedBytesForCurrentThread();
                        // борода за челюстью (вторая итерация): SetJaw — один SetVector материала бороды
                        long g0 = System.GC.GetAllocatedBytesForCurrentThread(); var gsw = System.Diagnostics.Stopwatch.StartNew();
                        for (int i = 0; i < 3000; i++) d.FoeFigure.gear.SetJaw(0.3f * (i % 60) / 60f, 0.1f);
                        gsw.Stop(); long g1 = System.GC.GetAllocatedBytesForCurrentThread();
                        log.AppendLine("EMO_BENCH gear SetJaw " + (gsw.Elapsed.TotalMilliseconds / 3000).ToString("0.0000") + " ms/frame, alloc " + (g1 - g0) + " B / 3000 frames");
                        log.AppendLine("EMO_BENCH face Tick+Apply " + (tsw.Elapsed.TotalMilliseconds / 3000).ToString("0.0000") + " ms/frame, alloc " + (b1 - b0) + " B / 3000 frames");
                    }
                    rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(px);
                    EndFight(d);
                }
            }
            finally { AnatFace.Enabled = wasA; FoeFace.Enabled = wasF; AnatFace.Off(); }
            foreach (bool on in new[] { false, true })
                log.AppendLine("EMO_BENCH on=" + on + " headTris=" + tris[on] + " hitFrame " + Dist(hit[on]) + " | fightFrame " + Dist(frame[on]) + " | render " + Dist(rend[on]));
            EmotionsLog = log.ToString().TrimEnd();
            var p = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "emo-bench.txt"));
            File.WriteAllText(p, EmotionsLog);
            Debug.Log(EmotionsLog);
        }
    }
}
