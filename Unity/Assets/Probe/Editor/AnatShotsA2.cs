using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxProbe.Editor
{
    /// Этап A2: внешний вид и отсечение скрытого.
    /// Band — опыты со ступеньками на коже (вид поверхности AnatMesher.Style*), Run — снимки до/после, пары, замер по состояниям.
    /// Unity.exe -batchmode -quit -projectPath Unity -executeMethod VoxProbe.Editor.AnatShotsA2.Run   → Unity/Shots/anatomy-a2/
    public static class AnatShotsA2
    {
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots"));
        static string Out => Path.Combine(Root, "anatomy-a2");
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        static string F(double v) => v.ToString("0.###", Inv);
        static Vector3 C(float x, float y, float z) => new Vector3(x, y, z) * 0.01f;
        static readonly Vector3 Ctr = new Vector3(0, 1.5f, 1.0f) * 0.01f;

        class Rig
        {
            public AnatHead a; public List<AnatBody> bodies = new List<AnatBody>();
            public Camera cam; public RenderTexture rt; public Light light;
            public NativeArray<VertexAttributeDescriptor> layout;

            public Rig(int style)
            {
                var mat = AnatShots.EnsureMaterial();
                AnatPalette.Apply();
                a = AnatomyBuilder.Build();
                layout = new NativeArray<VertexAttributeDescriptor>(AnatMesher.Layout, Allocator.Persistent);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("anat").transform;
                int id = 0;
                foreach (var g in a.All) { var b = new AnatBody(g, root, mat, id++) { style = style }; bodies.Add(b); }
                Build();
                var camGo = new GameObject("cam");
                cam = camGo.AddComponent<Camera>();
                var ad = camGo.AddComponent<UniversalAdditionalCameraData>();
                ad.renderShadows = false; ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
                cam.nearClipPlane = 0.01f; cam.farClipPlane = 5;
                var lightGo = new GameObject("sun");
                light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.None;
                lightGo.transform.rotation = Quaternion.Euler(30, 160, 0);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
                rt = new RenderTexture(1024, 1280, 24) { name = "shot" };
                cam.targetTexture = rt;
            }

            public void Build() { foreach (var b in bodies) b.BuildAll(layout); }
            public void Style(int s) { foreach (var b in bodies) b.style = s; Build(); }
            public void Cull(bool on) { foreach (var b in bodies) b.cull = on; Build(); }

            public void Shot(string path, Vector3 eye, Vector3 at, float fov)
            {
                cam.fieldOfView = fov; cam.transform.position = eye; cam.transform.LookAt(at);
                cam.Render();
                RenderTexture.active = rt;
                var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); t.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t); RenderTexture.active = null;
            }

            /// Те же ракурсы, что у этапа A (AnatShots.Run).
            public void Views(string dir, string prefix, bool all = true)
            {
                Shot(Path.Combine(dir, prefix + "front.png"), C(0, 2.5f, 62), Ctr, 27);
                Shot(Path.Combine(dir, prefix + "three-quarter.png"), C(-38, 6, 48), Ctr, 27);
                Shot(Path.Combine(dir, prefix + "profile.png"), C(-62, 2, 1), Ctr, 27);
                if (!all) { Shot(Path.Combine(dir, prefix + "cheek.png"), C(-16, 1, 30), C(-3.5f, 1.5f, 6), 20); return; }
                Shot(Path.Combine(dir, prefix + "back.png"), C(20, 25, -55), Ctr, 27);
                Shot(Path.Combine(dir, prefix + "eye.png"), C(-3.2f, 2.6f, 20), C(-3.2f, 2.3f, 6.2f), 22);
                Shot(Path.Combine(dir, prefix + "mouth.png"), C(0, -4.5f, 24), C(0, -5.2f, 8), 24);
            }

            public (int head, int mand, int eyes, int teeth, int all) Tris()
            {
                int h = 0, m = 0, e = 0, t = 0;
                foreach (var b in bodies) { int n = b.Triangles; if (b.g == a.head) h += n; else if (b.g == a.mand) m += n; else if (b.g == a.eyeL || b.g == a.eyeR) e += n; else t += n; }
                return (h, m, e, t, h + m + e + t);
            }

            /// Время сетки головы: каждый чанк с поверхностью на главном потоке (лучший из 3 после прогрева), и все чанки джобами.
            public (double mean, double max, double one, double par, int n) HeadMeshMs()
            {
                var hb = bodies[0]; var h = hb.g;
                var nopool = new NativeArray<VS>(1, Allocator.TempJob); var noslot = new NativeArray<int>(1, Allocator.TempJob);
                var sv = new StateView { pool = nopool, slot = noslot, nch = 1 };
                var vv = hb.cull ? VisView.Of(h, true) : VisView.Off(h.mat);
                var org = new NativeArray<int3>(1, Allocator.TempJob); var st = new NativeArray<long>(4, Allocator.TempJob);
                var sw = new System.Diagnostics.Stopwatch();
                double sum = 0, max = 0; int cnt = 0;
                for (int c = 0; c < hb.ChunkCount; c++)
                {
                    if (hb.naive[c] == 0 && hb.quads[c] == 0) continue;
                    org[0] = hb.ChunkCoord(c) * AnatMesher.CS;
                    double best = 1e9;
                    for (int rep = 0; rep < 4; rep++)
                    {
                        var md = Mesh.AllocateWritableMeshData(1);
                        sw.Restart();
                        new AnatChunkJob { mat = h.mat, origins = org, layout = layout, dims = h.dims, sv = sv, vv = vv, style = hb.style, dscale = 0.05f, meshes = md, stats = st }.Run(1);
                        double ms = sw.Elapsed.TotalMilliseconds; md.Dispose();
                        if (rep > 0) best = math.min(best, ms);
                    }
                    sum += best; max = math.max(max, best); cnt++;
                }
                int n = hb.ChunkCount;
                var orgs = new NativeArray<int3>(n, Allocator.TempJob); var sts = new NativeArray<long>(n * 4, Allocator.TempJob);
                for (int c = 0; c < n; c++) orgs[c] = hb.ChunkCoord(c) * AnatMesher.CS;
                double par = 1e9;
                for (int rep = 0; rep < 4; rep++)
                {
                    var md = Mesh.AllocateWritableMeshData(n);
                    sw.Restart();
                    new AnatChunkJob { mat = h.mat, origins = orgs, layout = layout, dims = h.dims, sv = sv, vv = vv, style = hb.style, dscale = 0.05f, meshes = md, stats = sts }.Schedule(n, 1).Complete();
                    double ms = sw.Elapsed.TotalMilliseconds; md.Dispose();
                    if (rep > 0) par = math.min(par, ms);
                }
                org.Dispose(); st.Dispose(); orgs.Dispose(); sts.Dispose(); nopool.Dispose(); noslot.Dispose();
                return (sum / math.max(1, cnt), max, sum, par, cnt);
            }

            public void Dispose() { foreach (var b in bodies) b.Destroy(); layout.Dispose(); a.Dispose(); rt.Release(); }
        }

        /// Опыты со ступеньками: 0 — как на этапе A; 1 — без затенения углов; 2 — нормаль поля; 3 — 1+2; 6 — нормаль и сдвиг угла по полю; 7 — всё.
        public static readonly int[] BandStyles = { 0, 1, 2, 3, 6, 7 };

        public static void Diag()
        {
            var a = AnatomyBuilder.Build(); var e = a.eyeL; var sb = new StringBuilder();
            foreach (var yz in new[] { new float2(2.7f, -0.6f), new float2(0.5f, -1f), new float2(-1.5f, -1f), new float2(2.5f, -2.5f), new float2(0.5f, 0.5f) })
            {
                float bx = 0; for (float x = 12; x > 0; x -= 0.02f) if (AnatGeo.Body(x, yz.x, yz.y) < 0) { bx = x; break; }
                Debug.Log("ANAT_EARX y " + yz.x + " z " + yz.y + " body surface x " + bx);
            }
            foreach (var ray in AnatChecks.Rays) { var res = AnatChecks.Check(a, ray); Debug.Log("ANAT_RAY " + ray.zone + " " + res.ok + " :: " + res.got + " :: " + res.report); }
            foreach (float dx in new[] { 0f, 0.4f })
            {
                sb.Append("dx ").Append(dx).Append(": ");
                for (float z = 9; z > 6; z -= 0.05f)
                {
                    var p = new float3(AnatGeo.EX + dx, AnatGeo.EY, z); byte m = a.Sample(p, out _, out var g);
                    int ei = e.Lookup(p); int hi = a.head.Lookup(p);
                    sb.Append(F(z)).Append(':').Append(m).Append('/').Append(g == null ? "-" : g.name).Append(" ev").Append(ei >= 0 ? e.vis[ei] : -1).Append(" hm").Append(a.head.mat[hi]).Append(" hv").Append(a.head.vis[hi]).Append("; ");
                }
                sb.Append(" || ");
            }
            Debug.Log("ANAT_DIAG " + sb); a.Dispose();
        }

        public static void Band()
        {
            var r = new Rig(0);
            var sb = new StringBuilder("{\n");
            string dir = Path.Combine(Out, "band");
            foreach (int s in BandStyles)
            {
                r.Style(s);
                r.Views(dir, "s" + s + "-", false);
                var t = r.Tris(); var ms = r.HeadMeshMs();
                sb.Append("  \"style").Append(s).Append("\": { \"trisHead\": ").Append(t.head).Append(", \"trisAll\": ").Append(t.all)
                  .Append(", \"headChunkMsMean\": ").Append(F(ms.mean)).Append(", \"headChunkMsMax\": ").Append(F(ms.max))
                  .Append(", \"headOneThreadMs\": ").Append(F(ms.one)).Append(", \"headParallelMs\": ").Append(F(ms.par)).Append(", \"chunks\": ").Append(ms.n).Append(" },\n");
            }
            foreach (var v in new[] { "front", "three-quarter", "profile", "cheek" })
            {
                var tiles = new List<string>(); foreach (int s in BandStyles) tiles.Add(Path.Combine(dir, "s" + s + "-" + v + ".png"));
                Grid(tiles, 3, Path.Combine(dir, "compare-" + v + ".png"));
            }
            sb.Append("  \"order\": \"0 1 2 / 3 6 7\"\n}\n");
            File.WriteAllText(Path.Combine(dir, "band.json"), sb.ToString());
            r.Dispose();
            Debug.Log("ANAT_A2_BAND_OK");
        }

        /// Картинки в сетку по cols в ряд (уменьшены вдвое).
        static void Grid(List<string> files, int cols, string path)
        {
            var imgs = new List<Texture2D>();
            foreach (var f in files) { var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(f)); imgs.Add(t); }
            int w = imgs[0].width / 2, h = imgs[0].height / 2, rows = (imgs.Count + cols - 1) / cols;
            var o = new Texture2D(w * cols, h * rows, TextureFormat.RGB24, false);
            for (int k = 0; k < imgs.Count; k++)
            {
                int cx = k % cols, cy = rows - 1 - k / cols;
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) o.SetPixel(cx * w + x, cy * h + y, imgs[k].GetPixel(x * 2, y * 2));
            }
            o.Apply(); File.WriteAllBytes(path, o.EncodeToPNG());
            foreach (var t in imgs) Object.DestroyImmediate(t); Object.DestroyImmediate(o);
        }

        /// Пара «до | после» в одну картинку.
        static void Pair(string before, string after, string path)
        {
            if (!File.Exists(before) || !File.Exists(after)) return;
            var a = new Texture2D(2, 2); a.LoadImage(File.ReadAllBytes(before));
            var b = new Texture2D(2, 2); b.LoadImage(File.ReadAllBytes(after));
            var o = new Texture2D(a.width + b.width + 8, math.max(a.height, b.height), TextureFormat.RGB24, false);
            var fill = new Color32[o.width * o.height]; for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(230, 230, 230, 255); o.SetPixels32(fill);
            o.SetPixels(0, 0, a.width, a.height, a.GetPixels()); o.SetPixels(a.width + 8, 0, b.width, b.height, b.GetPixels());
            o.Apply(); File.WriteAllBytes(path, o.EncodeToPNG());
            Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(o);
        }

        // ---------- состояния для замера ----------
        /// Убрать ячейки головы в цилиндре (ось p0→p1, радиус r), кроме занятых телами; keep — что не трогать. Возвращает убранные.
        public static NativeList<int> Bore(AnatGrid h, float3 p0, float3 p1, float r, System.Func<byte, bool> keep)
        {
            var L = new NativeList<int>(1024, Allocator.Persistent);
            float3 lo = math.min(p0, p1) - r, hi = math.max(p0, p1) + r;
            int3 c0 = math.max((int3)math.floor((lo - h.org) / h.step), 0), c1 = math.min((int3)math.floor((hi - h.org) / h.step), h.dims - 1);
            // удар останавливается на первой ячейке keep вдоль оси (кость, мозг): за ней ничего не убирается
            float3 ax = math.normalize(p1 - p0); float stop = float.MaxValue;
            for (int z = c0.z; z <= c1.z; z++) for (int y = c0.y; y <= c1.y; y++) for (int x = c0.x; x <= c1.x; x++)
            {
                int i = h.Index(x, y, z); float3 q = h.Local(x, y, z);
                if (keep(h.mat[i]) && AnatGeo.SegDist(q, p0, p1) <= r) stop = math.min(stop, math.dot(q - p0, ax));
            }
            for (int z = c0.z; z <= c1.z; z++) for (int y = c0.y; y <= c1.y; y++) for (int x = c0.x; x <= c1.x; x++)
            {
                int i = h.Index(x, y, z); byte m = h.mat[i];
                if (m == AnatMat.Empty || m == AnatMat.Covered || keep(m)) continue;
                float3 q = h.Local(x, y, z);
                if (AnatGeo.SegDist(q, p0, p1) > r || math.dot(q - p0, ax) >= stop) continue;
                h.mat[i] = AnatMat.Empty; L.Add(i);
            }
            return L;
        }

        public static bool Bone(byte m) => m == AnatMat.Compact || m == AnatMat.Spongy || m == AnatMat.Periost;
        public static bool BrainM(byte m) => m == AnatMat.Grey || m == AnatMat.White || m == AnatMat.CerebGrey || m == AnatMat.Stem || m == AnatMat.Csf;

        /// Пробой щеки в рот (сбоку на уровне зубов), удар до кости над гайморовой пазухой, вскрытый свод, половина головы.
        public static readonly float3 CheekA = new float3(7.2f, -5.0f, 5.0f), CheekB = new float3(2.0f, -5.0f, 5.6f);
        public static readonly float3 SinusA = new float3(3.3f, -0.6f, 13f), SinusB = new float3(2.4f, -0.4f, 2f);
        public static readonly float3 SkullA = new float3(1.2f, 15f, 1.0f), SkullB = new float3(1.2f, 6.5f, 1.0f);

        public static void Run()
        {
            Directory.CreateDirectory(Out);
            var r = new Rig(AnatMesher.StyleDefault);
            var a = r.a; var sb = new StringBuilder("{\n");
            sb.Append("  \"style\": ").Append(AnatMesher.StyleDefault).Append(", \"visLim\": ").Append(AnatVis.Lim).Append(",\n");
            sb.Append("  \"buildMs\": ").Append(F(a.ms["total"])).Append(", \"visFullMs\": ").Append(F(a.ms["visibility"])).Append(",\n");

            // снимки и пары с этапом A
            r.Views(Out, "shot-");
            foreach (var v in new[] { "front", "three-quarter", "profile", "back", "eye", "mouth" })
                Pair(Path.Combine(Root, "anatomy", "shot-" + v + ".png"), Path.Combine(Out, "shot-" + v + ".png"), Path.Combine(Out, "pair-" + v + ".png"));

            void Row(string name, bool cullToo = true)
            {
                var on = r.Tris();
                sb.Append("  \"").Append(name).Append("\": { \"head\": ").Append(on.head).Append(", \"mandible\": ").Append(on.mand).Append(", \"eyes\": ").Append(on.eyes)
                  .Append(", \"teeth\": ").Append(on.teeth).Append(", \"all\": ").Append(on.all);
                if (cullToo)
                {
                    r.Cull(false); var off = r.Tris(); r.Cull(true);
                    sb.Append(", \"noCull\": { \"head\": ").Append(off.head).Append(", \"mandible\": ").Append(off.mand).Append(", \"eyes\": ").Append(off.eyes).Append(", \"teeth\": ").Append(off.teeth).Append(", \"all\": ").Append(off.all).Append(" }");
                }
                sb.Append(" },\n");
            }
            Row("intact");
            var ms = r.HeadMeshMs();
            sb.Append("  \"headMesh\": { \"chunkMsMean\": ").Append(F(ms.mean)).Append(", \"chunkMsMax\": ").Append(F(ms.max)).Append(", \"oneThreadMs\": ").Append(F(ms.one)).Append(", \"parallelMs\": ").Append(F(ms.par)).Append(", \"chunks\": ").Append(ms.n).Append(" },\n");
            r.Cull(false); var ms0 = r.HeadMeshMs(); r.Cull(true);
            sb.Append("  \"headMeshNoCull\": { \"chunkMsMean\": ").Append(F(ms0.mean)).Append(", \"chunkMsMax\": ").Append(F(ms0.max)).Append(", \"oneThreadMs\": ").Append(F(ms0.one)).Append(", \"parallelMs\": ").Append(F(ms0.par)).Append(" },\n");

            // удар, не достающий пазухи: мягкие ткани до кости
            Damage("sinusHitNoReach", Bore(a.head, SinusA, SinusB, 0.6f, Bone), r, sb, "shot-sinus-hit", C(16, 0, 40), C(3, -0.5f, 8));
            // пробой щеки в рот
            Damage("cheekBreach", Bore(a.head, CheekA, CheekB, 0.7f, m => false), r, sb, "shot-cheek-breach", C(42, -6, 22), C(4, -5, 4));
            Row("afterCheekBreach");
            // вскрытый свод: кожа, кость, оболочка — до мозга
            Damage("skullOpening", Bore(a.head, SkullA, SkullB, 2.2f, BrainM), r, sb, "shot-skull-open", C(-18, 50, 20), C(0, 8, 0));
            Row("afterSkullOpening");
            // половина головы (всё с x > 0,35 во всех телах), видимость заново
            foreach (var b in r.bodies)
            {
                var g = b.g;
                for (int z = 0; z < g.dims.z; z++) for (int y = 0; y < g.dims.y; y++) for (int x = 0; x < g.dims.x; x++)
                    if (g.World(g.Local(x, y, z)).x > 0.35f) g.mat[g.Index(x, y, z)] = 0;
            }
            a.vis.Full(); r.Build();
            sb.Append("  \"halfVisFullMs\": ").Append(F(a.vis.lastMs + a.vis.bodiesMs)).Append(",\n");
            r.light.transform.rotation = Quaternion.Euler(25, 120, 0);
            r.Shot(Path.Combine(Out, "shot-half.png"), C(62, 2, 2), Ctr, 27);
            r.Shot(Path.Combine(Out, "shot-half-3q.png"), C(45, 12, 40), Ctr, 29);
            Row("halfRemoved");
            sb.Append("  \"ok\": true\n}\n");
            File.WriteAllText(Path.Combine(Out, "measure.json"), sb.ToString());
            r.Dispose();
            Debug.Log("ANAT_A2_OK");
        }

        /// Удаление ячеек как при ударе: видимость от них (по месту), перестройка помеченных чанков; сверка с полным пересчётом; снимок.
        static void Damage(string name, NativeList<int> cells, Rig r, StringBuilder sb, string shot, Vector3 eye, Vector3 at)
        {
            var a = r.a;
            int flips = a.vis.Opened(cells.AsArray());
            double visMs = a.vis.lastMs;
            int dirty = 0; foreach (var b in r.bodies) { b.SyncVis(); dirty += b.dirtyCount; }
            // и сами удалённые ячейки — их чанки
            var h = a.head; var hb = r.bodies[0];
            foreach (int i in cells) { int x = i % h.dims.x, y = (i / h.dims.x) % h.dims.y, z = i / (h.dims.x * h.dims.y); h.Touch(new int3(x, y, z), new int3(x, y, z)); }
            hb.SyncVis();
            dirty = 0; foreach (var b in r.bodies) dirty += b.dirtyCount;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var b in r.bodies) b.RebuildDirty(r.layout);
            double meshMs = sw.Elapsed.TotalMilliseconds;
            // сверка: тот же результат, что полный пересчёт
            var inc = new List<byte[]>(); foreach (var g in a.All) inc.Add(g.vis.ToArray());
            a.vis.Full(); int k = 0, diff = 0;
            foreach (var g in a.All) { var f = g.vis; var s = inc[k++]; for (int i = 0; i < s.Length; i++) if ((s[i] <= AnatVis.Lim) != (f[i] <= AnatVis.Lim)) diff++; }
            r.Build();
            sb.Append("  \"").Append(name).Append("\": { \"removed\": ").Append(cells.Length).Append(", \"visFlips\": ").Append(flips).Append(", \"visUpdateMs\": ").Append(F(visMs))
              .Append(", \"dirtyChunks\": ").Append(dirty).Append(", \"rebuildMs\": ").Append(F(meshMs)).Append(", \"incrementalVsFullDiff\": ").Append(diff).Append(" },\n");
            r.Shot(Path.Combine(Out, shot + ".png"), eye, at, 26);
            cells.Dispose();
        }
    }
}
