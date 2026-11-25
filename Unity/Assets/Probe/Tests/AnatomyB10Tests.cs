using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    /// Этап B, часть 10: огрехи переломов у брови и глаза (перехлёст кожи, край коробки), кровь ветвями, мышцы на культе.
    public class AnatomyB10Tests
    {
        // удары из снимков части 9 (AnatShotsB9): щёки по очереди и рот — Ле Фор II за 7 ударов, III за 11
        static readonly float3 ChL = new float3(3.0f, -2.2f, 14), ChR = new float3(-3.0f, -2.2f, 14), Mo = new float3(0, -4.6f, 14);
        static readonly float3 DL = math.normalize(new float3(-0.15f, 0, -1)), DR = math.normalize(new float3(0.15f, 0, -1)), DM = new float3(0, 0, -1);

        static AnatView LeFort(int n)
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            float3[] p = { ChL, ChR, ChL, ChR, ChL, ChR, Mo, ChL, ChR, ChL, Mo }; float3[] d = { DL, DR, DL, DR, DL, DR, DM, DL, DR, DL, DM };
            float t = 0.5f;
            for (int k = 0; k < n; k++) { while (v.sim.time < t) v.Tick(1 / 30f, 64); v.Hit(p[k], d[k], 1); t += 1.2f; }
            while (v.sim.time < t + 1.5f) v.Tick(1 / 30f, 64); v.Settle();
            return v;
        }

        /// Наибольшее растяжение смещения по коже лица (|∂D/∂x| по осям, D — сдвиг отломков, см/см) и наибольший сдвиг кожи
        /// у глазного яблока относительно самого глаза, см. Растяжение ≥ 1 — кожа перехлёстывается (полосы, тёмные пятна).
        static void Measure(AnatView v, out float stretch, out float eye, out string at, out float nose)
        {
            var h = v.a.head; stretch = 0; eye = 0; at = ""; nose = 0;
            float3 eL = new float3(AnatGeo.EX, AnatGeo.EY, AnatGeo.EZ), eR = new float3(-AnatGeo.EX, AnatGeo.EY, AnatGeo.EZ);
            float3 sL = v.bones.eyeShift[0] + (v.bones.Level(AnatBones.Maxilla) >= 3 ? v.bones.upperShift * 0.6f : 0);
            float3 sR = v.bones.eyeShift[1] + (v.bones.Level(AnatBones.Maxilla) >= 3 ? v.bones.upperShift * 0.6f : 0);
            for (int z = 1; z < h.dims.z - 1; z += 2) for (int y = 1; y < h.dims.y - 1; y += 2) for (int x = 1; x < h.dims.x - 1; x += 2)
            {
                int i = x + h.dims.x * (y + h.dims.y * z); if (h.mat[i] != AnatMat.Skin) continue;   // кожа: во рту шаг между верхними и нижними зубами — настоящий
                if (h.mat[i + 1] != AnatMat.Empty && h.mat[i - 1] != AnatMat.Empty && h.mat[i + h.dims.x] != AnatMat.Empty && h.mat[i - h.dims.x] != AnatMat.Empty
                    && h.mat[i + h.dims.x * h.dims.y] != AnatMat.Empty && h.mat[i - h.dims.x * h.dims.y] != AnatMat.Empty) continue;
                float3 p = h.org + (new float3(x, y, z) + 0.5f) * h.step; if (p.z < 2) continue;
                // вдоль поверхности: нормаль — к пустым соседям; поперёк кожи (кость → воздух) перехлёста не бывает
                int sx = h.dims.x, sy = h.dims.x * h.dims.y;
                float3 nn = new float3((h.mat[i + 1] == AnatMat.Empty ? 1 : 0) - (h.mat[i - 1] == AnatMat.Empty ? 1 : 0),
                    (h.mat[i + sx] == AnatMat.Empty ? 1 : 0) - (h.mat[i - sx] == AnatMat.Empty ? 1 : 0), (h.mat[i + sy] == AnatMat.Empty ? 1 : 0) - (h.mat[i - sy] == AnatMat.Empty ? 1 : 0));
                if (math.lengthsq(nn) < 0.5f) continue; nn = math.normalize(nn);
                float3 ta = math.abs(nn.x) < 0.6f ? new float3(1, 0, 0) : new float3(0, 1, 0), t1 = math.normalize(math.cross(nn, ta)), t2 = math.cross(nn, t1);
                // касательный якобиан смещения: det(I + J) — во сколько раз меняется площадь кожи; ≤ 0 — кожа выворачивается (перехлёст),
                // чистый сдвиг (кривой нос) det не меняет. stretch = 1 − min det: 0 — без сжатия, ≥ 1 — перехлёст
                float3 g1 = (v.bones.DispAt(0, p + t1 * 0.4f) - v.bones.DispAt(0, p - t1 * 0.4f)) / 0.8f, g2 = (v.bones.DispAt(0, p + t2 * 0.4f) - v.bones.DispAt(0, p - t2 * 0.4f)) / 0.8f;
                float det = (1 + math.dot(t1, g1)) * (1 + math.dot(t2, g2)) - math.dot(t1, g2) * math.dot(t2, g1);
                if (math.abs(p.x) < 1.2f) { nose = math.max(nose, 1 - det); continue; }   // нос: стык двух отломков (нос вбок, лицо назад) — отдельно
                if (1 - det > stretch) { stretch = 1 - det; at = p.ToString(); }
                float dl = math.distance(p, eL), dr = math.distance(p, eR);
                if (math.min(dl, dr) < AnatGeo.ER + 0.3f) eye = math.max(eye, math.length(v.bones.DispAt(0, p) - (dl < dr ? sL : sR)));
            }
        }

        /// Ле Фор II и III: кожа не перехлёстывается (сжатие площади < 0,7) и не отходит от глаза больше чем на 3 мм.
        /// В части 9 (Wide = false) — перехлёст у брови и угла глаза: коробка отломка резала вес, переход 1 → 0 за ~1 см при сдвиге 1,3–1,7 см.
        [Test]
        public void FractureNoFold()
        {
            bool was = AnatBones.Wide;
            try
            {
                foreach (int n in new[] { 7, 11 })
                {
                    float[] st = new float[2], ey = new float[2], ns = new float[2]; string[] at = new string[2];
                    for (int w = 0; w < 2; w++)
                    {
                        AnatBones.Wide = w == 1; var v = LeFort(n);
                        try { Assert.GreaterOrEqual(v.bones.Level(AnatBones.Maxilla), n == 7 ? 2 : 3, string.Join("; ", v.bones.log)); Measure(v, out st[w], out ey[w], out at[w], out ns[w]); }
                        finally { v.Dispose(); }
                    }
                    Debug.Log("ANAT_TEST b10 fold: Ле Фор " + (n == 7 ? "II" : "III") + " растяжение часть 9 " + st[0].ToString("0.00") + " (" + at[0] + ") → " + st[1].ToString("0.00") + " (" + at[1] + "), кожа у глаза " + ey[0].ToString("0.00") + " → " + ey[1].ToString("0.00") + " см, нос (|x| < 1,2) " + ns[0].ToString("0.00") + " → " + ns[1].ToString("0.00"));
                    Assert.Less(st[1], 0.7f, "кожа не перехлёстывается (площадь не меньше 0,3 от прежней)"); Assert.Less(ey[1], 0.3f, "кожа у глаза идёт с глазом");
                    Assert.Greater(st[0], st[1], "в части 9 растяжение было больше");
                }
            }
            finally { AnatBones.Wide = was; }
        }

        static readonly float3 Brow = new float3(2.6f, 4.4f, 14), Nose = new float3(0, 0.8f, 16), Back = new float3(0, 0, -1);

        /// Бой части 8 (бровь и нос), 30 с крови. runs — наибольшее число отдельных потёков на одной высоте под носом
        /// (кровь на лицевой поверхности, разрыв ≥ 2 ячеек), wide — средняя ширина потёка, ячеек.
        static (byte[] mat, int frac, int runs, float wide, int kids, int kidSeq, int spread) Bleed(bool riv)
        {
            bool was = AnatDamage.Rivulets; AnatDamage.Rivulets = riv;
            var a = AnatomyBuilder.Build(); var d = new AnatDamage(a);
            try
            {
                for (int k = 0; k < 3; k++) { d.Hit(Brow, Back, 3.4f, true); for (int f = 0; f < 45; f++) d.Step(1 / 30f); }
                for (int k = 0; k < 3; k++) { d.Hit(Nose, Back, 3.0f, true); for (int f = 0; f < 45; f++) d.Step(1 / 30f); }
                for (int f = 0; f < 900; f++) d.Step(1 / 30f);
                var h = a.head; int best = 0, cells = 0, nr = 0;
                int y0 = (int)((-1.5f - h.org.y) / h.step), y1 = (int)((-9f - h.org.y) / h.step);
                for (int y = y0; y > y1; y--)
                {
                    int runs = 0, gap = 2;
                    for (int x = 0; x < h.dims.x; x++)
                    {
                        bool on = false;
                        for (int z = h.dims.z - 1; z > h.dims.z / 2 && !on; z--) if (d.Get(x, y, z).blood > 40) on = true;
                        if (on) { if (gap >= 2) { runs++; nr++; } cells++; gap = 0; } else gap++;
                    }
                    best = math.max(best, runs);
                }
                return (a.head.mat.ToArray(), d.fractures.Count, best, nr > 0 ? cells / (float)nr : 0, d.kids, d.kidSeq, d.spreadCells);
            }
            finally { d.Dispose(); a.Dispose(); AnatDamage.Rivulets = was; }
        }

        /// Кровь ветвями: разрушение то же (rng не сдвинут), отдельных потёков на одной высоте больше и они уже, пределы держатся.
        [Test]
        public void BloodBranches()
        {
            var off = Bleed(false); var on = Bleed(true);
            Debug.Log("ANAT_TEST b10 blood: потёков на высоте часть 8 " + off.runs + " → " + on.runs + ", ширина " + off.wide.ToString("0.0") + " → " + on.wide.ToString("0.0")
                + " яч., веток за бой " + off.kidSeq + " → " + on.kidSeq + ", растекание " + off.spread + " → " + on.spread);
            Assert.AreEqual(off.mat, on.mat, "материалы головы после ударов"); Assert.AreEqual(off.frac, on.frac);
            Assert.Greater(on.kidSeq, off.kidSeq * 3 / 2, "веток больше"); Assert.GreaterOrEqual(on.runs, off.runs, "отдельных потёков не меньше");
            Assert.LessOrEqual(on.kids, AnatDamage.MaxKids); Assert.LessOrEqual(on.kidSeq, AnatDamage.MaxKidsAll); Assert.LessOrEqual(on.spread, AnatDamage.MaxSpread);
        }

        // ---------- отрисовка: русла крови и слои мышц видны на картинке ----------

        static Color32[] Render(AnatView v, float yaw, float pitch, float dist, float3 at, bool off, bool lite = false)
        {
            bool wasLite = AnatPalette.Lite; AnatPalette.SetLite(lite); Shader.SetGlobalFloat("_AnatB10Off", off ? 1 : 0);
            var cg = new GameObject("t-cam"); var cam = cg.AddComponent<Camera>(); var lg = new GameObject("t-sun"); var l = lg.AddComponent<Light>();
            var rt = new RenderTexture(300, 360, 24);
            try
            {
                l.type = LightType.Directional; l.intensity = 1.1f; l.shadows = LightShadows.None; lg.transform.rotation = Quaternion.Euler(35, 150, 0);
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.nearClipPlane = 0.01f; cam.farClipPlane = 5; cam.fieldOfView = 30; cam.targetTexture = rt;
                Vector3 piv = v.ToWorld(at); cam.transform.position = piv + Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward * dist; cam.transform.LookAt(piv);
                Shader.SetGlobalFloat("_AnatTime", v.sim.time); cam.Render();
                RenderTexture.active = rt; var tx = new Texture2D(300, 360, TextureFormat.RGBA32, false); tx.ReadPixels(new Rect(0, 0, 300, 360), 0, 0); tx.Apply();
                var px = tx.GetPixels32(); Object.DestroyImmediate(tx); RenderTexture.active = null; return px;
            }
            finally { Shader.SetGlobalFloat("_AnatB10Off", 0); AnatPalette.SetLite(wasLite); cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(cg); Object.DestroyImmediate(lg); }
        }

        static bool Red(Color32 c) => c.r > 60 && c.r > c.g * 1.7f && c.r > c.b * 1.5f;

        /// Смен «кровь / не кровь» на строку в нижней половине кадра (лицо под глазами).
        static float Edges(Color32[] px)
        {
            int n = 0, rows = 0;
            for (int y = 40; y < 180; y += 2) { rows++; for (int x = 1; x < 300; x++) if (Red(px[y * 300 + x]) != Red(px[y * 300 + x - 1])) n++; }
            return n / (float)rows;
        }

        /// Русла: тот же бой (бровь, нос), 40 с; лист крови на коже распадается на ручьи — смен «кровь / не кровь» на строку больше, и в ANAT_LITE.
        [Test]
        public void BloodRivuletsRender()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), new Material(Shader.Find("VoxProbe/Anatomy")));
            try
            {
                float t = 0.5f;
                foreach (var p in new[] { new float3(-3.6f, -0.2f, 0), new float3(-3.0f, -2.2f, 0), new float3(-4.0f, 0.3f, 0), new float3(-3.4f, -4.6f, 0), new float3(-2.2f, 6.6f, 0), new float3(-3.3f, -1.0f, 0) })
                { while (v.sim.time < t) v.Tick(1 / 30f, 64); v.Hit(new float3(p.x, p.y, 16), new float3(0.15f, 0, -1), 1); t += 2; }
                t = 16;
                foreach (var p in new[] { new float3(-2.6f, 4.0f, 0), new float3(-2.6f, 4.0f, 0), new float3(0.0f, 0.8f, 0), new float3(-2.0f, 4.2f, 0) })
                { while (v.sim.time < t) v.Tick(1 / 30f, 64); v.Hit(new float3(p.x, p.y, 16), new float3(0.1f, -0.1f, -1), 1); t += 1.5f; }
                while (v.sim.time < 40) v.Tick(1 / 30f, 64); v.Settle();
                var at = new float3(0, -1, 1);
                float off = Edges(Render(v, 0, 3, 0.5f, at, true)), on = Edges(Render(v, 0, 3, 0.5f, at, false));
                float offL = Edges(Render(v, 0, 3, 0.5f, at, true, true)), onL = Edges(Render(v, 0, 3, 0.5f, at, false, true));
                Debug.Log("ANAT_TEST b10 rivulets: смен кровь/кожа на строку " + off.ToString("0.0") + " → " + on.ToString("0.0") + ", ANAT_LITE " + offL.ToString("0.0") + " → " + onL.ToString("0.0"));
                Assert.Greater(on, off * 1.5f, "русла"); Assert.Greater(onL, offL * 1.3f, "русла в ANAT_LITE");
            }
            finally { v.Dispose(); }
        }

        static bool Fascia(Color32 c) => c.r > 110 && c.g > c.r * 0.84f && c.b > c.r * 0.62f && c.b < c.r * 0.92f;

        /// Слои мышц: сценарий seed 77 до культи, срез сверху; светлых прослоек между мышцами больше (и в ANAT_LITE), разрушение то же.
        [Test]
        public void StumpMuscleLayersRender()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), new Material(Shader.Find("VoxProbe/Anatomy")));
            try
            {
                var st = AnatBench.Stages(v); var rnd = new Unity.Mathematics.Random(77); int hits = 0;
                foreach (var s in st)
                {
                    for (int k = 0; k < s.max && !s.done(v); k++) { if (AnatBench.Strike(v, s.where, ref rnd)) hits++; for (int f = 0; f < 3; f++) v.Tick(1 / 30f, 12); }
                    v.Settle(); for (int f = 0; f < 60; f++) v.Tick(1 / 30f, 64); v.Settle();
                }
                Assert.AreEqual(78, hits); Assert.AreEqual(51928, v.Triangles);
                var at = new float3(0, -10.5f, -1);
                float Share(Color32[] px) { int a = 0, f = 0; foreach (var c in px) { if (c.r + c.g + c.b < 30) continue; a++; if (Fascia(c)) f++; } return f / (float)a; }
                float off = Share(Render(v, -25, 55, 0.25f, at, true)), on = Share(Render(v, -25, 55, 0.25f, at, false));
                float offL = Share(Render(v, -25, 55, 0.25f, at, true, true)), onL = Share(Render(v, -25, 55, 0.25f, at, false, true));
                Debug.Log("ANAT_TEST b10 muscles: доля прослоек на срезе " + (off * 100).ToString("0.0") + " → " + (on * 100).ToString("0.0") + " %, ANAT_LITE " + (offL * 100).ToString("0.0") + " → " + (onL * 100).ToString("0.0") + " %");
                Assert.Greater(on, off * 1.3f, "прослоек больше"); Assert.Greater(onL, offL * 1.5f, "и в ANAT_LITE"); Assert.Greater(on, 0.03f);
            }
            finally { v.Dispose(); }
        }
    }
}
