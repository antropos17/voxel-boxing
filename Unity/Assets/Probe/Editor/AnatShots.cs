using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxProbe.Editor
{
    /// Unity.exe -batchmode -quit -projectPath Unity -executeMethod VoxProbe.Editor.AnatShots.Run
    /// Срезы (AnatTools.Slices), счёт треугольников по режимам, замер сборки сетки, снимки снаружи и разрезы сеткой.
    public static class AnatShots
    {
        const string MatPath = "Assets/Probe/Generated/Anatomy.mat";
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        static string F(double v) => v.ToString("0.###", Inv);

        public static Material EnsureMaterial()
        {
            Directory.CreateDirectory("Assets/Probe/Generated");
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            var sh = Shader.Find("VoxProbe/Anatomy");
            if (sh == null) throw new System.Exception("нет шейдера VoxProbe/Anatomy");
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, MatPath); }
            m.shader = sh; m.SetFloat("_Ambient", 0.42f);
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssets();
            return m;
        }

        public static void Run()
        {
            AnatTools.Slices();
            Directory.CreateDirectory(AnatTools.OutDir);
            var mat = EnsureMaterial();
            AnatPalette.Apply();
            var a = AnatomyBuilder.Build();
            var layout = new NativeArray<VertexAttributeDescriptor>(AnatMesher.Layout, Allocator.Persistent);
            var sb = new StringBuilder("{\n");
            Bench(a, layout, sb);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("anat").transform;
            var bodies = new List<AnatBody>();
            int id = 0;
            foreach (var g in a.All) { var b = new AnatBody(g, root, mat, id++); b.BuildAll(layout); bodies.Add(b); }
            int tris = 0, tHead = 0, tMand = 0, tEyes = 0, tTeeth = 0;
            foreach (var b in bodies)
            {
                int t = b.Triangles; tris += t;
                if (b.g == a.head) tHead += t; else if (b.g == a.mand) tMand += t; else if (b.g == a.eyeL || b.g == a.eyeR) tEyes += t; else tTeeth += t;
            }
            sb.Append("  \"renderTris\": ").Append(tris).Append(",\n");
            sb.Append("  \"trisHead\": ").Append(tHead).Append(", \"trisMandible\": ").Append(tMand).Append(", \"trisEyes\": ").Append(tEyes).Append(", \"trisTeeth\": ").Append(tTeeth).Append(",\n");
            // голова: грани наружу (ячейка пустоты, связанная с краем решётки) и грани внутренних полостей (пазухи, нос, рот, глотка)
            {
                var h = a.head; var outside = new bool[h.Count]; var q = new System.Collections.Generic.Queue<int>();
                for (int z = 0; z < h.dims.z; z++) for (int y = 0; y < h.dims.y; y++) for (int x = 0; x < h.dims.x; x++)
                    if ((x == 0 || y == 0 || z == 0 || x == h.dims.x - 1 || y == h.dims.y - 1 || z == h.dims.z - 1) && !AnatMat.Cull(h.mat[h.Index(x, y, z)])) { int i = h.Index(x, y, z); outside[i] = true; q.Enqueue(i); }
                int W = h.dims.x, H = h.dims.y, D = h.dims.z;
                while (q.Count > 0)
                {
                    int i = q.Dequeue(); int x = i % W, y = (i / W) % H, z = i / (W * H);
                    for (int d = 0; d < 6; d++)
                    {
                        int xx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), yy = y + (d == 2 ? 1 : d == 3 ? -1 : 0), zz = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
                        if (xx < 0 || yy < 0 || zz < 0 || xx >= W || yy >= H || zz >= D) continue;
                        int j = h.Index(xx, yy, zz); if (outside[j] || AnatMat.Cull(h.mat[j])) continue;
                        outside[j] = true; q.Enqueue(j);
                    }
                }
                long fOut = 0, fIn = 0;
                for (int z = 0; z < D; z++) for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    int i = h.Index(x, y, z); byte m = h.mat[i]; if (!AnatMat.Cull(m) || m == AnatMat.Covered) continue;
                    for (int d = 0; d < 6; d++)
                    {
                        int xx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), yy = y + (d == 2 ? 1 : d == 3 ? -1 : 0), zz = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
                        bool inb = !(xx < 0 || yy < 0 || zz < 0 || xx >= W || yy >= H || zz >= D);
                        if (inb && AnatMat.Cull(h.mat[h.Index(xx, yy, zz)])) continue;
                        if (!inb || outside[h.Index(xx, yy, zz)]) fOut++; else fIn++;
                    }
                }
                sb.Append("  \"headFacesOutside\": ").Append(fOut).Append(", \"headFacesInnerCavities\": ").Append(fIn).Append(",\n");
            }

            var camGo = new GameObject("cam");
            var cam = camGo.AddComponent<Camera>();
            var ad = camGo.AddComponent<UniversalAdditionalCameraData>();
            ad.renderShadows = false; ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 5;
            var lightGo = new GameObject("sun");
            var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(30, 160, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            var rt = new RenderTexture(1024, 1280, 24) { name = "shot" };
            cam.targetTexture = rt;

            Vector3 C(float x, float y, float z) => new Vector3(x, y, z) * 0.01f;
            void Shot(string name, Vector3 eye, Vector3 at, float fov)
            {
                cam.fieldOfView = fov;
                cam.transform.position = eye; cam.transform.LookAt(at);
                cam.Render();
                RenderTexture.active = rt;
                var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); t.Apply();
                File.WriteAllBytes(Path.Combine(AnatTools.OutDir, "shot-" + name + ".png"), t.EncodeToPNG());
                Object.DestroyImmediate(t);
                RenderTexture.active = null;
            }
            var ctr = C(0, 1.5f, 1.0f);
            Shot("front", C(0, 2.5f, 62), ctr, 27);
            Shot("three-quarter", C(-38, 6, 48), ctr, 27);
            Shot("profile", C(-62, 2, 1), ctr, 27);
            Shot("back", C(20, 25, -55), ctr, 27);
            Shot("eye", C(-3.2f, 2.6f, 20), C(-3.2f, 2.3f, 6.2f), 22);
            Shot("mouth", C(0, -4.5f, 24), C(0, -5.2f, 8), 24);

            // разрезы: убрать половину (ячейки с центром по ту сторону плоскости) и перестроить сетку
            void Cut(string name, System.Func<float3, bool> remove, Vector3 eye, Vector3 at, float fov)
            {
                var saved = new List<byte[]>();
                foreach (var b in bodies)
                {
                    var g = b.g; saved.Add(g.mat.ToArray());
                    for (int z = 0; z < g.dims.z; z++) for (int y = 0; y < g.dims.y; y++) for (int x = 0; x < g.dims.x; x++)
                        if (remove(g.World(g.Local(x, y, z)))) g.mat[g.Index(x, y, z)] = 0;
                    b.BuildAll(layout);
                }
                Shot(name, eye, at, fov);
                for (int i = 0; i < bodies.Count; i++) { bodies[i].g.mat.CopyFrom(saved[i]); bodies[i].BuildAll(layout); }
            }
            lightGo.transform.rotation = Quaternion.Euler(25, 120, 0);
            Cut("cut-sagittal", p => p.x > 0.35f, C(62, 2, 2), ctr, 27);
            Cut("cut-sagittal-3q", p => p.x > 0.35f, C(45, 12, 40), ctr, 29);
            lightGo.transform.rotation = Quaternion.Euler(30, 160, 0);
            Cut("cut-coronal-orbits", p => p.z > 6.0f, C(0, 3, 60), C(0, 1.0f, 6), 27);
            Cut("cut-coronal-temple", p => p.z > 1.2f, C(-15, 5, 58), C(0, 1.0f, 1.2f), 28);
            lightGo.transform.rotation = Quaternion.Euler(70, 160, 0);
            Cut("cut-axial-brain", p => p.y > 6.0f, C(0, 60, 14), C(0, 6, 0), 28);
            Cut("cut-axial-teeth", p => p.y > -5.25f, C(0, 34, 22), C(0, -5.3f, 4), 26);

            foreach (var b in bodies) b.Destroy();
            layout.Dispose(); a.Dispose(); rt.Release();
            sb.Append("  \"ok\": true\n}\n");
            File.WriteAllText(Path.Combine(AnatTools.OutDir, "mesh.json"), sb.ToString());
            Debug.Log("ANAT_SHOTS_OK");
        }

        /// Треугольники по режимам и стоимость сборки сетки (по чанку на главном потоке и всех чанков джобами).
        static void Bench(AnatHead a, NativeArray<VertexAttributeDescriptor> layout, StringBuilder sb)
        {
            var res = new NativeArray<int>(2, Allocator.TempJob);
            long naive = 0, smooth = 0, greedy = 0; int chunks = 0;
            foreach (var g in a.All)
            {
                var nch = (g.dims + AnatMesher.CS - 1) / AnatMesher.CS;
                chunks += nch.x * nch.y * nch.z;
                new AnatCountJob { mat = g.mat, dims = g.dims, nch = nch, mode = AnatMesher.ModeNaive, result = res }.Run(); naive += res[1];
                new AnatCountJob { mat = g.mat, dims = g.dims, nch = nch, mode = AnatMesher.ModeRender, result = res }.Run(); smooth += res[1];
                new AnatCountJob { mat = g.mat, dims = g.dims, nch = nch, mode = AnatMesher.ModeGreedyAO, result = res }.Run(); greedy += res[1];
            }
            res.Dispose();
            sb.Append("  \"chunks\": ").Append(chunks).Append(",\n");
            sb.Append("  \"trisNaive\": ").Append(naive * 2).Append(",\n  \"trisSmoothMerged\": ").Append(smooth * 2).Append(",\n  \"trisGreedyAONoSmooth\": ").Append(greedy * 2).Append(",\n");
            sb.Append("  \"vertsSmoothMerged\": ").Append(smooth * 4).Append(",\n  \"meshBytesSmooth\": ").Append(smooth * 4 * 12 + smooth * 6 * 2).Append(",\n");

            // голова: каждый чанк отдельно на главном потоке (лучший из 3 после прогрева) и все чанки джобами
            var h = a.head;
            var nh = (h.dims + AnatMesher.CS - 1) / AnatMesher.CS;
            int n = nh.x * nh.y * nh.z;
            var org = new NativeArray<int3>(1, Allocator.TempJob);
            var st = new NativeArray<long>(4, Allocator.TempJob);
            double sum = 0, max = 0;
            var sw = new System.Diagnostics.Stopwatch();
            for (int c = 0; c < n; c++)
            {
                org[0] = new int3(c % nh.x, (c / nh.x) % nh.y, c / (nh.x * nh.y)) * AnatMesher.CS;
                double best = 1e9;
                for (int rep = 0; rep < 4; rep++)
                {
                    var md = Mesh.AllocateWritableMeshData(1);
                    sw.Restart();
                    new AnatChunkJob { mat = h.mat, origins = org, layout = layout, dims = h.dims, meshes = md, stats = st }.Run(1);
                    double ms = sw.Elapsed.TotalMilliseconds;
                    md.Dispose();
                    if (rep > 0) best = math.min(best, ms);
                }
                sum += best; max = math.max(max, best);
            }
            var orgs = new NativeArray<int3>(n, Allocator.TempJob);
            var sts = new NativeArray<long>(n * 4, Allocator.TempJob);
            for (int c = 0; c < n; c++) orgs[c] = new int3(c % nh.x, (c / nh.x) % nh.y, c / (nh.x * nh.y)) * AnatMesher.CS;
            double bestAll = 1e9;
            for (int rep = 0; rep < 4; rep++)
            {
                var md = Mesh.AllocateWritableMeshData(n);
                sw.Restart();
                new AnatChunkJob { mat = h.mat, origins = orgs, layout = layout, dims = h.dims, meshes = md, stats = sts }.Schedule(n, 1).Complete();
                double ms = sw.Elapsed.TotalMilliseconds;
                md.Dispose();
                if (rep > 0) bestAll = math.min(bestAll, ms);
            }
            sb.Append("  \"editorHeadChunkMsMean\": ").Append(F(sum / n)).Append(",\n  \"editorHeadChunkMsMax\": ").Append(F(max)).Append(",\n");
            sb.Append("  \"editorHeadRebuildOneThreadMs\": ").Append(F(sum)).Append(",\n  \"editorHeadRebuildParallelMs\": ").Append(F(bestAll)).Append(",\n");
            sb.Append("  \"workers\": ").Append(Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount).Append(",\n");
            long gridBytes = 0; foreach (var g in a.All) gridBytes += g.Count * 3L;
            sb.Append("  \"gridBytes\": ").Append(gridBytes).Append(",\n");
            org.Dispose(); st.Dispose(); orgs.Dispose(); sts.Dispose();
        }
    }
}
