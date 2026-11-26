using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    /// Мимика (Unity/EMOTIONS.md): выражения двигают лицо и отличаются друг от друга, переходы без скачков,
    /// разрушение мимика не восстанавливает, кадр без выделений памяти, шейдер действительно сдвигает лицо.
    public class AnatomyFaceTests
    {
        static readonly AnatTemper Flat = new AnatTemper { name = "ровный", mood = AnatEmo.Calm, moodW = 0f, gain = 1f, blink = 0.25f, side = 1 };

        struct Pt { public float3 p; public int role; }

        // Открытые ячейки лица (кожа, губы, брови, ресницы) — точки мягкой головы; плюс глаза и нижняя челюсть.
        static List<Pt> Sample(AnatHead a)
        {
            var pts = new List<Pt>(); var g = a.head;
            for (int z = 1; z < g.dims.z - 1; z++)
                for (int y = 1; y < g.dims.y - 1; y++)
                    for (int x = 1; x < g.dims.x - 1; x++)
                    {
                        int i = g.Index(x, y, z); byte m = g.mat[i];
                        if (m != AnatMat.Skin && m != AnatMat.Lip && m != AnatMat.Brow && m != AnatMat.Lash && m != AnatMat.Stubble) continue;
                        var p = g.Local(x, y, z); if (p.z < 3f) continue;
                        if (g.mat[i + 1] != 0 && g.mat[i - 1] != 0 && g.mat[g.Index(x, y + 1, z)] != 0 && g.mat[g.Index(x, y - 1, z)] != 0 && g.mat[g.Index(x, y, z + 1)] != 0) continue;
                        if (((x + y + z) & 1) == 0) pts.Add(new Pt { p = p, role = AnatFace.RoleSoft });
                    }
            foreach (var (gr, role) in new[] { (a.eyeL, AnatFace.RoleEyeL), (a.eyeR, AnatFace.RoleEyeR), (a.mand, AnatFace.RoleJaw) })
                for (int i = 0; i < gr.mat.Length; i += 7)
                    if (gr.mat[i] != 0) { int x = i % gr.dims.x, y = (i / gr.dims.x) % gr.dims.y, z = i / (gr.dims.x * gr.dims.y); pts.Add(new Pt { p = gr.World(gr.Local(x, y, z)), role = role }); }
            return pts;
        }

        static float3[] Pose(List<Pt> pts, float[] ch)
        {
            var r = new float3[pts.Count];
            for (int i = 0; i < pts.Count; i++) r[i] = AnatFace.Deform(pts[i].p, pts[i].role, ch);
            return r;
        }

        static float[] Channels(int e)
        {
            var f = new AnatFace(Flat) { blinks = false };
            if (e >= 0) f.Play(e, 1f, 10f, 9); f.Snap();
            return (float[])f.ch.Clone();
        }

        /// Каждое выражение сдвигает лицо измеримо (больше 200 из ~8300 точек лица — больше 1 мм, наибольший сдвиг больше 3 мм),
        /// и любые два выражения различаются (средний квадрат разницы — больше 0,8 мм по лицу).
        [Test]
        public void EachExpressionDistinct()
        {
            var a = AnatomyBuilder.Build();
            try
            {
                var pts = Sample(a);
                var rest = Pose(pts, new float[AnatFace.NC]);
                var poses = new float3[AnatEmo.Count][];
                for (int e = 0; e < AnatEmo.Count; e++)
                {
                    poses[e] = Pose(pts, Channels(e));
                    int moved = 0; float mx = 0;
                    for (int i = 0; i < pts.Count; i++) { float d = math.distance(poses[e][i], rest[i]); if (d > 0.1f) moved++; mx = math.max(mx, d); }
                    Debug.Log("ANAT_TEST face " + AnatEmo.Names[e] + ": точек сдвинуто > 1 мм " + moved + " из " + pts.Count + ", наибольший сдвиг " + (mx * 10).ToString("0.0") + " мм");
                    if (e != AnatEmo.Calm) { Assert.Greater(moved, 200, AnatEmo.Names[e]); Assert.Greater(mx, 0.3f, AnatEmo.Names[e]); }
                    else { Assert.Greater(moved, 0, "спокойное: веки чуть опущены"); Assert.Greater(mx, 0.08f, "спокойное"); }
                }
                float minD = 9; string pair = "";
                for (int e1 = 0; e1 < AnatEmo.Count; e1++)
                    for (int e2 = e1 + 1; e2 < AnatEmo.Count; e2++)
                    {
                        double s = 0; int n = 0;
                        for (int i = 0; i < pts.Count; i++) { float d = math.distance(poses[e1][i], poses[e2][i]); if (math.distance(poses[e1][i], rest[i]) + math.distance(poses[e2][i], rest[i]) > 0.02f) { s += d * d; n++; } }
                        float rms = (float)math.sqrt(s / math.max(n, 1));
                        if (rms < minD) { minD = rms; pair = AnatEmo.Names[e1] + " / " + AnatEmo.Names[e2]; }
                        Assert.Greater(rms, 0.08f, AnatEmo.Names[e1] + " / " + AnatEmo.Names[e2]);
                    }
                Debug.Log("ANAT_TEST face: самая близкая пара " + pair + ", разница " + (minD * 10).ToString("0.00") + " мм");
            }
            finally { a.Dispose(); }
        }

        /// Переходы плавные: события сменяют друг друга без предупреждения (удар посреди ухмылки, оскал, удивление, нокдаун, скачок усталости).
        /// Непрерывность: тот же сценарий с кадром вдвое короче даёт вдвое меньший шаг (скачок не зависел бы от кадра);
        /// скорость: за кадр 1/60 ни одна точка лица не сдвигается больше чем на 4 мм (самое большое выражение — около 21 мм).
        [Test]
        public void TransitionsSmooth()
        {
            var a = AnatomyBuilder.Build();
            try
            {
                var pts = Sample(a);
                float Run(int sub, out int at)
                {
                    var f = new AnatFace(AnatTemper.Presets[3], 5) { blinks = false };   // моргание — отдельно (BlinkContinuous)
                    var prev = Pose(pts, f.ch); float worst = 0; at = -1;
                    int[] evs = { AnatEmo.Smirk, AnatEmo.Pain, AnatEmo.Rage, AnatEmo.Surprise, AnatEmo.Dazed, AnatEmo.Triumph, AnatEmo.Fear, AnatEmo.Pain, AnatEmo.Calm };
                    int[] prio = { 2, 4, 1, 2, 5, 7, 2, 9, 9 };
                    for (int k = 0; k < 330 * sub; k++)
                    {
                        if (k % (35 * sub) == 5 * sub) { int j = (k / (35 * sub)) % evs.Length; f.Play(evs[j], 1f, 0.5f, prio[j]); }
                        if (k == 200 * sub) f.fatigue = 1;
                        f.Step(1 / (60f * sub));
                        var cur = Pose(pts, f.ch);
                        for (int i = 0; i < pts.Count; i++) { float d = math.distance(cur[i], prev[i]); if (d > worst) { worst = d; at = k / sub; } }
                        prev = cur;
                    }
                    return worst;
                }
                float w60 = Run(1, out int at60), w240 = Run(4, out int at240);
                Debug.Log("ANAT_TEST face: наибольший шаг точки за кадр 1/60 " + (w60 * 10).ToString("0.00") + " мм (кадр " + at60 + "), за 1/240 " + (w240 * 10).ToString("0.00") + " мм (кадр " + at240 + ")");
                Assert.Less(w60, 0.4f, "скорость");
                Assert.Less(w240, w60 * 0.35f, "кадр в 4 раза короче — шаг меньше в 3+ раза: без скачков");
            }
            finally { a.Dispose(); }
        }

        /// Моргание: веко закрывается и открывается за 0,16 с по синусу — непрерывно, полный цикл не короче 9 кадров 1/60 (шаг канала < 0,5).
        [Test]
        public void BlinkContinuous()
        {
            var f = new AnatFace(AnatTemper.Presets[4], 11);
            float prev = f.ch[AnatFace.LidUpL], worst = 0, top = 0;
            for (int k = 0; k < 600; k++) { f.Step(1 / 60f); float c = f.ch[AnatFace.LidUpL]; worst = math.max(worst, math.abs(c - prev)); top = math.max(top, c); prev = c; }
            Debug.Log("ANAT_TEST face blink: веко до " + top.ToString("0.00") + ", шаг за кадр до " + worst.ToString("0.00"));
            Assert.Greater(top, 0.9f, "моргнул"); Assert.Less(worst, 0.5f);
        }

        /// Приоритет и длительность: слабое событие не перебивает сильное, пока оно идёт; после него лицо возвращается к настрою.
        [Test]
        public void PriorityAndDuration()
        {
            var f = new AnatFace(AnatTemper.Presets[1], 9) { blinks = false };
            Assert.AreEqual(AnatEmo.Smirk, f.Dominant, "задира в покое ухмыляется");
            Assert.IsTrue(f.Play(AnatEmo.Pain, 1f, 0.6f, 4));
            for (int i = 0; i < 9; i++) f.Step(1 / 60f);
            Assert.AreEqual(AnatEmo.Pain, f.Dominant);
            Assert.IsFalse(f.Play(AnatEmo.Smirk, 1f, 1f, 2), "насмешка не перебивает боль");
            for (int i = 0; i < 90; i++) f.Step(1 / 60f);
            Assert.AreEqual(-1, f.ev, "событие кончилось");
            Assert.AreEqual(AnatEmo.Smirk, f.Dominant, "вернулся настрой");
        }

        /// Разрушение не восстанавливается: удары в рот и щёку убирают ячейки; смена выражений (в том числе раскрытый рот)
        /// не меняет решётку, число треугольников и не ставит чанки на перестройку; оторванная челюсть мимикой не двигается.
        /// Кадр мимики (Step + Apply) без выделений памяти.
        [Test]
        public void DamageStaysAndNoAlloc()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), new Material(Shader.Find("VoxProbe/Anatomy")));
            try
            {
                int removed = 0;
                foreach (var p in new[] { new float3(0.8f, -5.3f, 0), new float3(-0.6f, -5.2f, 0), new float3(2.8f, -1.2f, 0) })
                { if (v.Pick(new Ray(v.ToWorld(new float3(p.x, p.y, 30)), Vector3.back), out var h, out _, out _)) removed += v.Hit(h, v.pickDir, 2); for (int t = 0; t < 10; t++) v.Tick(1 / 30f, 64); }
                v.Settle(true); v.MeshDone();
                Assert.Greater(removed, 100);
                var mat0 = v.a.head.mat.ToArray(); int tris0 = v.Triangles;
                int Dirty() { int n = 0; foreach (var b in v.bodies) n += b.dirtyCount; return n; }
                int dirty0 = Dirty();
                var f = new AnatFace(AnatTemper.Presets[2], 3);
                for (int e = 0; e < AnatEmo.Count; e++) { f.Play(e, 1f, 1f, 9); for (int k = 0; k < 20; k++) { f.Step(1 / 60f); f.Apply(v.root, v.a, v.gone); } }
                Assert.AreEqual(dirty0, Dirty(), "мимика не ставит чанки на перестройку");
                Assert.AreEqual(mat0, v.a.head.mat.ToArray(), "решётка головы та же");
                Assert.AreEqual(tris0, v.Triangles, "треугольники те же");
                // кадр без выделений
                f.Play(AnatEmo.Rage, 1f, 5f, 9);
                for (int k = 0; k < 30; k++) { f.Step(1 / 60f); f.Apply(v.root, v.a, v.gone); }
                long b0 = System.GC.GetAllocatedBytesForCurrentThread();
                for (int k = 0; k < 600; k++) { f.Step(1 / 60f); f.Apply(v.root, v.a, v.gone); }
                long alloc = System.GC.GetAllocatedBytesForCurrentThread() - b0;
                Assert.AreEqual(0, alloc, "байт за 600 кадров");
                // челюсть оторвана — рот закрыт, нижняя часть не двигается
                var gone = (bool[])v.gone.Clone(); gone[1] = true;
                f.Play(AnatEmo.Surprise, 1f, 5f, 9); for (int k = 0; k < 30; k++) { f.Step(1 / 60f); f.Apply(v.root, v.a, gone); }
                Assert.AreEqual(0f, f.ch[AnatFace.Jaw]);
                Assert.AreEqual(AnatFace.RoleJaw, AnatFace.RoleOf(v.a, 1)); Assert.AreEqual(AnatFace.RoleRigid, AnatFace.RoleOf(v.a, 4 + System.Array.FindIndex(v.a.toothDefs, t => t.upper == 1)));
            }
            finally { AnatFace.Off(); v.Dispose(); }
        }

        /// Шейдер сдвигает лицо (то же, что Deform): кадры «спокойное» и «удивление» / «оскал» / «боль» различаются в области лица;
        /// с выключенной мимикой кадр совпадает со «спокойным» без век.
        [Test]
        public void ShaderMovesFace()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), new Material(Shader.Find("VoxProbe/Anatomy")));
            var cg = new GameObject("t-cam"); var cam = cg.AddComponent<Camera>(); var lg = new GameObject("t-sun"); var l = lg.AddComponent<Light>();
            var rt = new RenderTexture(240, 300, 24);
            try
            {
                l.type = LightType.Directional; l.intensity = 1.1f; l.shadows = LightShadows.None; lg.transform.rotation = Quaternion.Euler(35, 160, 0);
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.nearClipPlane = 0.01f; cam.farClipPlane = 5; cam.fieldOfView = 30; cam.targetTexture = rt;
                Vector3 piv = v.ToWorld(new float3(0, -1.5f, 5)); cam.transform.position = piv + Vector3.forward * 0.5f; cam.transform.LookAt(piv);
                v.MeshDone();
                Color32[] Shot(int e)
                {
                    if (e < -1) AnatFace.Off(); else AnatFace.Send(Channels(e), v.root, v.a, v.gone);
                    cam.Render(); RenderTexture.active = rt; var tx = new Texture2D(240, 300, TextureFormat.RGBA32, false); tx.ReadPixels(new Rect(0, 0, 240, 300), 0, 0); tx.Apply();
                    var px = tx.GetPixels32(); Object.DestroyImmediate(tx); RenderTexture.active = null; return px;
                }
                float Diff(Color32[] x, Color32[] y) { int n = 0; for (int i = 0; i < x.Length; i++) if (math.abs(x[i].r - y[i].r) + math.abs(x[i].g - y[i].g) + math.abs(x[i].b - y[i].b) > 40) n++; return n / (float)x.Length; }
                var off = Shot(-2); var zero = Shot(-1); var calm = Shot(AnatEmo.Calm);
                Assert.AreEqual(0f, Diff(off, zero), "нулевые каналы = без мимики");
                foreach (int e in new[] { AnatEmo.Surprise, AnatEmo.Rage, AnatEmo.Pain })
                {
                    float d = Diff(calm, Shot(e));
                    Debug.Log("ANAT_TEST face render " + AnatEmo.Names[e] + ": доля изменившихся пикселей " + (d * 100).ToString("0.0") + " %");
                    Assert.Greater(d, 0.01f, AnatEmo.Names[e]);
                }
            }
            finally { AnatFace.Off(); cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(cg); Object.DestroyImmediate(lg); v.Dispose(); }
        }
    }
}
