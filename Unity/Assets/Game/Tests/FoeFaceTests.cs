using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using NUnit.Framework;
using VoxProbe;

namespace VoxGame.Tests
{
    // Мимика соперника в бою (Unity/EMOTIONS.md): событие боя → ожидаемое выражение, настрой по здоровью, характеры бойцов лестницы.
    public class FoeFaceTests
    {
        static FighterDef Ladder(int i) => new RosterDef(GameFlow.RosterSeed).Get(i);

        static FightState Hp(float frac) { var st = new FightState(); st.hpMax = 100f; st.hp = 100f * frac; return st; }

        static int After(FoeFace ff, float sec, float hp = 1f)
        {
            var st = Hp(hp);
            for (float t = 0; t < sec; t += 1f / 60f) ff.Tick(1f / 60f, st);
            return ff.face.Dominant;
        }

        static FoeFace Face(int presetIdx)
        {
            // боец с нужным характером: ищем в лестнице, иначе подменяем прозвище
            var f = Ladder(0);
            var ff = new FoeFace(f);
            ff.face.temper = AnatTemper.Presets[presetIdx];
            ff.face.blinks = false;
            ff.face.Snap();
            return ff;
        }

        [Test]
        public void HitGivesPainThenDazed()
        {
            var ff = Face(0);
            ff.OnHit(1, false, (int)AnatZone.Cheek, false, false, 1);
            Assert.AreEqual(AnatEmo.Pain, After(ff, 0.2f), "силовой удар — боль");
            ff = Face(0);
            ff.OnHit(2, true, (int)AnatZone.Jaw, true, false, -1);
            Assert.AreEqual(AnatEmo.Pain, After(ff, 0.2f), "мега — боль");
            Assert.AreEqual(AnatEmo.Dazed, After(ff, 1.0f), "затем оглушён");
            ff = Face(0);
            ff.OnHit(0, false, (int)AnatZone.Skull, false, false, 1);
            ff.Tick(1f / 60f, Hp(1f)); ff.Tick(1f / 60f, Hp(1f));
            float jab = ff.face.w[AnatEmo.Pain];
            var ff2 = Face(0); ff2.OnHit(1, false, (int)AnatZone.Orbit, false, false, 1);
            After(ff2, 0.2f); After(ff, 0.2f - 2f / 60f);
            Assert.Greater(ff2.face.w[AnatEmo.Pain], ff.face.w[AnatEmo.Pain] + 0.15f, "сила удара: джеб слабее силового в глаз");
            Assert.Greater(ff2.face.ch[AnatFace.LidUpL], ff2.face.ch[AnatFace.LidUpR], "место: глаз со стороны удара зажмурен сильнее");
            Assert.Greater(jab, 0f);
        }

        [Test]
        public void TauntsByTemper()
        {
            var bully = Face(1); bully.OnPlayerMiss();
            Assert.AreEqual(AnatEmo.Smirk, After(bully, 0.4f), "задира ухмыляется на промах");
            var show = Face(3); show.OnFoeLanded(false, true);
            Assert.AreEqual(AnatEmo.Triumph, After(show, 0.4f), "пробил блок — торжество");
            var angry = Face(2); angry.OnWindup();
            Assert.AreEqual(AnatEmo.Rage, After(angry, 0.4f), "злой (покой — злость) в замахе скалится");
            angry.OnStrike();
            Assert.AreEqual(AnatEmo.Rage, After(angry, 0.2f), "выпад — оскал");
            var cold = Face(0); cold.OnWindup();
            Assert.AreEqual(AnatEmo.Anger, After(cold, 0.4f), "хладнокровный (покой — сосредоточенное) в замахе злится");
            var nerv = Face(4); nerv.OnDodged();
            Assert.AreEqual(AnatEmo.Surprise, After(nerv, 0.3f), "нервный удивлён уклону");
        }

        [Test]
        public void KnockdownRiseKoFinale()
        {
            var ff = Face(6);
            ff.OnKnockdown();
            Assert.AreEqual(AnatEmo.Dazed, After(ff, 5f), "нокдаун — оглушён, держится");
            ff.OnPlayerMiss();
            Assert.AreEqual(AnatEmo.Dazed, After(ff, 0.5f), "мелочь не перебивает нокдаун");
            ff.OnRise();
            Assert.AreEqual(AnatEmo.Tired, After(ff, 0.6f), "подъём — усталость");
            Assert.AreEqual(AnatEmo.Anger, After(ff, 1.5f), "затем злость (зверь)");
            ff.OnKnockout();
            Assert.AreEqual(AnatEmo.Dazed, After(ff, 1f), "нокаут");
            ff.OnFinale(false);
            Assert.AreEqual(AnatEmo.Defeat, After(ff, 1f), "поражение");
            var w = Face(3); w.OnFinale(true);
            Assert.AreEqual(AnatEmo.Triumph, After(w, 1f), "победа соперника — торжество");
        }

        [Test]
        public void LowHpMood()
        {
            var ff = Face(0);
            Assert.AreEqual(AnatEmo.Tired, After(ff, 1f, 0.15f), "мало здоровья — усталость");
            Assert.Greater(ff.face.fatigue, 0.8f);
            var nerv = Face(4);
            Assert.AreEqual(AnatEmo.Fear, After(nerv, 1f, 0.06f), "нервный при почти нуле — страх");
            // дыхание ртом: челюсть ходит
            float mn = 9, mx = -9; var st = Hp(0.15f);
            for (int i = 0; i < 120; i++) { ff.Tick(1f / 60f, st); mn = System.Math.Min(mn, ff.face.ch[AnatFace.Jaw]); mx = System.Math.Max(mx, ff.face.ch[AnatFace.Jaw]); }
            Assert.Greater(mx - mn, 0.15f, "дыхание ртом");
        }

        [Test]
        public void LadderTempersVaried()
        {
            var r = new RosterDef(GameFlow.RosterSeed);
            var names = new HashSet<string>(); var moods = new HashSet<int>();
            for (int i = 0; i < RosterDef.Count; i++) { var t = FighterGen.TemperOf(r.Get(i)); names.Add(t.name); moods.Add(t.mood); Assert.Greater(t.gain, 0.4f); }
            Assert.GreaterOrEqual(names.Count, 6, "характеров в лестнице");
            Assert.GreaterOrEqual(moods.Count, 3, "разных настроев покоя");
            var a = FighterGen.TemperOf(r.Get(0)); var b = FighterGen.TemperOf(r.Get(0));
            Assert.AreEqual(a.gain, b.gain, "детерминированно");
        }
    
        // ---- вторая итерация (EMOTIONS.md) ----

        static List<float3> FacePts(AnatHead a)
        {
            var pts = new List<float3>(); var g = a.head;
            for (int z = 1; z < g.dims.z - 1; z++) for (int y = 1; y < g.dims.y - 1; y++) for (int x = 1; x < g.dims.x - 1; x++)
            {
                int i = g.Index(x, y, z); byte m = g.mat[i];
                if (m != AnatMat.Skin && m != AnatMat.Lip && m != AnatMat.Brow && m != AnatMat.Lash) continue;
                var p = g.Local(x, y, z); if (p.z < 3f) continue;
                if (g.mat[i + 1] != 0 && g.mat[i - 1] != 0 && g.mat[g.Index(x, y + 1, z)] != 0 && g.mat[g.Index(x, y - 1, z)] != 0 && g.mat[g.Index(x, y, z + 1)] != 0) continue;
                if ((x + y + z) % 3 == 0) pts.Add(p);
            }
            return pts;
        }

        static float Rms(List<float3> pts, float[] c0, float[] c1)
        {
            double s = 0;
            foreach (var p in pts) { float d = math.distance(AnatFace.Deform(p, AnatFace.RoleSoft, c0), AnatFace.Deform(p, AnatFace.RoleSoft, c1)); s += d * d; }
            return (float)math.sqrt(s / math.max(1, pts.Count));
        }

        /// Беглые выражения (замах, выпад, джеб, увернулся, удар в блок) у всех 8 характеров: лицо уходит от покоя бойца
        /// не меньше чем на 0,8 мм RMS (раньше замах у «сосредоточенных» и «злых» — 0,3–0,5 мм: то же выражение, что покой)
        /// и держится на ≥ 70 % своей амплитуды не меньше 0,15 с.
        [Test]
        public void FleetingReadableAndHeld()
        {
            var a = AnatomyBuilder.Build();
            try
            {
                var pts = FacePts(a); var st = Hp(1f); float minAmp = 99, minHold = 99; string wa = "", wh = ""; var bad = new List<string>();
                for (int t = 0; t < AnatTemper.Presets.Length; t++)
                    for (int k = 0; k < 5; k++)
                    {
                        var ff = Face(t);
                        for (int i = 0; i < 120; i++) ff.Tick(1f / 60f, st);
                        var c0 = (float[])ff.face.ch.Clone();
                        if (k == 0) ff.OnWindup(); else if (k == 1) ff.OnStrike(); else if (k == 2) ff.OnHit(0, false, (int)AnatZone.Cheek, false, false, 1);
                        else if (k == 3) ff.OnDodged(); else ff.OnFoeLanded(true, false);
                        var curve = new List<float>();
                        for (int i = 0; i < 90; i++) { ff.Tick(1f / 60f, st); curve.Add(Rms(pts, c0, ff.face.ch)); }
                        float amp = 0; foreach (var c in curve) amp = math.max(amp, c);
                        int hold = 0; foreach (var c in curve) if (c >= 0.7f * amp) hold++;
                        string tag = AnatTemper.Presets[t].name + " / " + ff.lastEvent;
                        if (amp < minAmp) { minAmp = amp; wa = tag; }
                        if (hold / 60f < minHold) { minHold = hold / 60f; wh = tag; }
                        if (amp < 0.08f || hold / 60f < 0.15f) bad.Add(tag + " " + (amp * 10).ToString("0.00") + " мм " + (hold / 60f).ToString("0.00") + " с");
                    }
                Debug.Log("FOE_FACE fleeting: наименьшая амплитуда " + (minAmp * 10).ToString("0.00") + " мм (" + wa + "), наименьшее удержание " + minHold.ToString("0.00") + " с (" + wh + ")" + (bad.Count > 0 ? "; мимо: " + string.Join("; ", bad) : ""));
                Assert.IsEmpty(bad, "амплитуда ≥ 0,8 мм и удержание ≥ 0,15 с");
            }
            finally { a.Dispose(); }
        }

        /// Борода одним мешем идёт за челюстью вершина за вершиной (HeadGear, шейдер _VOX_JAW): вес вершины — поле AnatFace.JawWeight,
        /// одинаковые точки покоя двигаются одинаково (шва нет: расхождение ≤ 0,5 мм), соседние вершины при челюсти на пределе
        /// расходятся плавно (ребро ячейки 3 мм меняется меньше чем на 1,2 см — без раскрытия клином, как у прежнего разреза: 2–3 см).
        [Test]
        public void BeardFollowsJawWithoutSeam()
        {
            var mat = new Material(Shader.Find("VoxGame/VertexColor"));
            var r = new RosterDef(GameFlow.RosterSeed); int bearded = 0;
            float3 pv = AnatFace.JawPivot; float ang = AnatFace.JawRad;
            float3 Rot(float3 p, float w) { float c = math.cos(ang * w), s = math.sin(ang * w); var v = p - pv; return pv + new float3(v.x, v.y * c - v.z * s, v.y * s + v.z * c) + new float3(0.5f * AnatFace.JawSideCm * w, 0, 0); }
            for (int i = 0; i < RosterDef.Count; i++)
            {
                var f = r.Get(i); if (f.head.beard == BeardStyle.None) continue;
                bearded++;
                var mount = new GameObject("mount").transform; var gear = new HeadGear();
                try
                {
                    gear.Build(mount, f, mat, true);
                    MeshFilter mf = null; int parts = 0;
                    foreach (var m in mount.GetComponentsInChildren<MeshFilter>(true)) if (m.name.StartsWith("beard")) { mf = m; parts++; }
                    Assert.AreEqual(1, parts, f.nickname + ": борода одним мешем");
                    Assert.IsTrue(mf.GetComponent<MeshRenderer>().sharedMaterial.IsKeywordEnabled("_VOX_JAW"), "вариант шейдера");
                    var mesh = mf.sharedMesh; var vs = mesh.vertices; var uv = new List<Vector2>(); mesh.GetUVs(1, uv);
                    Assert.AreEqual(vs.Length, uv.Count);
                    var at = new Dictionary<Vector3Int, float>(); float div = 0, edge = 0;
                    var pn = new float3[vs.Length];
                    for (int k = 0; k < vs.Length; k++)
                    {
                        var p = vs[k] * 100f; float w = uv[k].x;
                        Assert.That(w, Is.InRange(0f, 1f));
                        Assert.AreEqual(HeadGear.BeardJawWeight(p), w, 1e-4f, "вес вершины — поле кожи");
                        pn[k] = Rot(p, w);
                        var key = Vector3Int.RoundToInt(p * 1000f);
                        if (at.TryGetValue(key, out float w0)) div = math.max(div, math.distance(Rot(p, w0), pn[k]));
                        else at[key] = w;
                    }
                    var tri = mesh.triangles;
                    for (int t = 0; t < tri.Length; t += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            int i0 = tri[t + e], i1 = tri[t + (e + 1) % 3];
                            edge = math.max(edge, math.abs(math.distance(pn[i0], pn[i1]) - Vector3.Distance(vs[i0], vs[i1]) * 100f));
                        }
                    Debug.Log("FOE_FACE beard " + f.nickname + " " + f.head.beard + ": расхождение совпадающих вершин " + (div * 10).ToString("0.000") + " мм, изменение ребра до " + (edge * 10).ToString("0.0") + " мм");
                    Assert.LessOrEqual(div, 0.05f, f.nickname + ": шов");
                    Assert.Less(edge, 1.2f, f.nickname + ": разрыв");
                    gear.SetJaw(ang, 0.3f);
                }
                finally { gear.Dispose(); Object.DestroyImmediate(mount.gameObject); }
            }
            Object.DestroyImmediate(mat);
            Assert.GreaterOrEqual(bearded, 4, "бородатых в лестнице");
        }
    }
}
