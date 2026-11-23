using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxProbe.Editor
{
    /// Этап B: сценарий «серия ударов в бровь, скулу, нос, челюсть», снимки и срезы по ходу боя, отдельно — струя из височной артерии.
    /// Unity.exe -batchmode -quit -projectPath Unity -executeMethod VoxProbe.Editor.AnatShotsB.Run   → Unity/Shots/anatomy-b/
    public static class AnatShotsB
    {
        public const string OutDir = "Shots/anatomy-b";
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        static string F(double v) => v.ToString("0.###", Inv);

        class Rig
        {
            public AnatHead a; public AnatDamage d; public List<AnatBody> bodies = new List<AnatBody>(); public AnatBody head;
            public AnatBloodFx fx; public Camera cam; public RenderTexture rt; public Transform root; public Light light;
            public NativeArray<VertexAttributeDescriptor> layout;
            public int rebuilt; public double rebuildMs, rebuildMsMax; public int rebuildCalls;

            public void Sync()
            {
                for (int i = 0; i < d.dirty.Length; i++) if (d.dirty[i]) { d.dirty[i] = false; head.MarkDirty(i); }
                if (head.dirtyCount == 0) return;
                int r0 = head.rebuilt; var sw = System.Diagnostics.Stopwatch.StartNew();
                head.RebuildDirty(layout);
                double ms = sw.Elapsed.TotalMilliseconds; rebuilt += head.rebuilt - r0; rebuildMs += ms; rebuildMsMax = math.max(rebuildMsMax, ms); rebuildCalls++;
            }

            public void Shot(string name, Vector3 eye, Vector3 at, float fov)
            {
                Sync();
                fx.Update(d.drops, d.time);
                Shader.SetGlobalFloat("_AnatTime", d.time);
                cam.fieldOfView = fov; cam.transform.position = eye; cam.transform.LookAt(at);
                cam.Render();
                RenderTexture.active = rt;
                var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); t.Apply();
                File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), t.EncodeToPNG());
                Object.DestroyImmediate(t); RenderTexture.active = null;
            }

            public void Slice(string name, float3 o, float3 u, float3 v, float w, float h)
            {
                var r = AnatSlice.Render(a, o, u, v, w, h, 0.025f, d.st);
                File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), AnatSlice.Png(r));
            }

            public void Dispose() { foreach (var b in bodies) b.Destroy(); fx.Destroy(); d.Dispose(); a.Dispose(); }
        }

        static Rig Make(Material mat, NativeArray<VertexAttributeDescriptor> layout, Camera cam, RenderTexture rt)
        {
            var g = new Rig { layout = layout, cam = cam, rt = rt };
            g.a = AnatomyBuilder.Build(); g.d = new AnatDamage(g.a);
            g.root = new GameObject("anat").transform;
            int id = 0;
            foreach (var gr in g.a.All) { var b = new AnatBody(gr, g.root, mat, id++); if (gr == g.a.head) { b.st = g.d.st; g.head = b; } b.BuildAll(layout); g.bodies.Add(b); }
            g.fx = new AnatBloodFx(g.root, mat, layout);
            return g;
        }

        static Vector3 C(float x, float y, float z) => new Vector3(x, y, z) * 0.01f;

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            var mat = AnatShots.EnsureMaterial();
            AnatPalette.Apply();
            var layout = new NativeArray<VertexAttributeDescriptor>(AnatMesher.Layout, Allocator.Persistent);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>();
            var ad = camGo.AddComponent<UniversalAdditionalCameraData>(); ad.renderShadows = false; ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f); cam.nearClipPlane = 0.01f; cam.farClipPlane = 5;
            var lightGo = new GameObject("sun"); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(30, 160, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            var rt = new RenderTexture(1024, 1280, 24) { name = "shot" }; cam.targetTexture = rt;

            var sb = new StringBuilder("{\n");
            var g = Make(mat, layout, cam, rt);
            var d = g.d;
            var blows = AnatScenario.Blows();
            var log = new StringBuilder();
            var ctr = C(0, 1.5f, 1.0f);
            void Shots(string tag)
            {
                g.Shot(tag + "-front", C(0, 2.5f, 62), ctr, 27);
                g.Shot(tag + "-3q-left", C(38, 6, 48), ctr, 27);
                g.Shot(tag + "-brow-eye", C(9, 5, 24), C(2.8f, 3.0f, 9), 24);
                g.Shot(tag + "-nose-mouth", C(-4, -1, 26), C(0, -2.5f, 9), 26);
                g.Shot(tag + "-jaw-right", C(-40, -8, 26), C(-4, -5.5f, 4), 26);
            }
            void Slices(string tag)
            {
                g.Slice(tag + "-slice-brow-sag", new float3(2.6f, 3.5f, 9), new float3(0, 0, 1), new float3(0, 1, 0), 10, 9);
                g.Slice(tag + "-slice-cheek-sag", new float3(4.4f, -0.5f, 8), new float3(0, 0, 1), new float3(0, 1, 0), 10, 9);
                g.Slice(tag + "-slice-nose-ax", new float3(0, 1.0f, 8), new float3(1, 0, 0), new float3(0, 0, -1), 9, 9);
                g.Slice(tag + "-slice-jaw-cor", new float3(-4, -6.5f, 4.5f), new float3(1, 0, 0), new float3(0, 1, 0), 9, 8);
            }
            Shots("t00");
            const float dt = 1 / 60f; int bi = 0; float nextSync = 0;
            var snapT = new[] { 10.5f, 42f, 70f, 110f, AnatScenario.End };
            int si = 0;
            double sumHit = 0; int nHit = 0; int rebuildBefore;
            var perHit = new StringBuilder();
            while (d.time < AnatScenario.End)
            {
                while (bi < blows.Count && d.time >= blows[bi].t)
                {
                    var b = blows[bi++];
                    var hi = d.Hit(b.p, b.dir, b.E, b.power);
                    rebuildBefore = g.rebuilt; var sw = System.Diagnostics.Stopwatch.StartNew(); g.Sync(); double rms = sw.Elapsed.TotalMilliseconds;
                    sumHit += hi.ms; nHit++;
                    perHit.Append("    {\"t\": ").Append(F(d.time)).Append(", \"what\": \"").Append(b.what).Append("\", \"zone\": \"").Append(AnatZone.Names[hi.zone])
                        .Append("\", \"cells\": ").Append(hi.cells).Append(", \"removed\": ").Append(hi.removed).Append(", \"zoneTrauma\": ").Append(F(hi.eff))
                        .Append(", \"hitMs\": ").Append(F(hi.ms)).Append(", \"chunksRebuilt\": ").Append(g.rebuilt - rebuildBefore).Append(", \"rebuildMs\": ").Append(F(rms)).Append("},\n");
                }
                d.Step(dt);
                if (d.time >= nextSync) { nextSync = d.time + 0.1f; g.Sync(); }     // перестройка не чаще 10 раз в секунду
                if (si < snapT.Length && d.time >= snapT[si]) { string tag = "t" + ((int)math.round(snapT[si])).ToString("000"); Shots(tag); if (si == 1 || si == snapT.Length - 1) Slices(tag); si++; }
            }
            int tris = 0; foreach (var b in g.bodies) tris += b.Triangles;
            sb.Append("  \"hits\": ").Append(nHit).Append(", \"hitMsMean\": ").Append(F(sumHit / nHit)).Append(", \"hitMsMax\": ").Append(F(d.hitMsMax)).Append(",\n");
            sb.Append("  \"stepMsMean\": ").Append(F(d.stepMs / (AnatScenario.End / dt))).Append(", \"stepMsMax\": ").Append(F(d.stepMsMax)).Append(",\n");
            sb.Append("  \"cellsRemoved\": ").Append(d.removed).Append(", \"peakWounds\": ").Append(d.peakWounds).Append(", \"peakDrips\": ").Append(d.peakDrips).Append(", \"peakDrops\": ").Append(d.peakDrops).Append(",\n");
            sb.Append("  \"stateChunks\": ").Append(d.st.Chunks).Append(", \"stateBytes\": ").Append(d.st.Bytes).Append(", \"headChunks\": ").Append(d.dirty.Length).Append(",\n");
            sb.Append("  \"chunkRebuilds\": ").Append(g.rebuilt).Append(", \"rebuildCalls\": ").Append(g.rebuildCalls).Append(", \"rebuildMsTotal\": ").Append(F(g.rebuildMs)).Append(", \"rebuildMsMaxCall\": ").Append(F(g.rebuildMsMax))
              .Append(", \"chunkMsMean\": ").Append(F(g.head.rebuildMs / math.max(1, g.head.rebuilt))).Append(",\n");
            sb.Append("  \"trisAfter\": ").Append(tris).Append(", \"eyeClosedL\": ").Append(d.eyeClosed[0] ? "true" : "false").Append(", \"eyeClosedR\": ").Append(d.eyeClosed[1] ? "true" : "false").Append(",\n");
            sb.Append("  \"zoneTrauma\": {");
            for (int z = 1; z < AnatZone.Count; z++) if (d.trauma[z] > 0) sb.Append(" \"").Append(AnatZone.Names[z]).Append("\": [").Append(F(d.trauma[z])).Append(", ").Append(F(d.Eff((byte)z))).Append("],");
            sb.Length--; sb.Append(" },\n");
            sb.Append("  \"perHit\": [\n").Append(perHit.ToString().TrimEnd(',', '\n')).Append("\n  ],\n");
            sb.Append("  \"events\": [\n"); foreach (var e in d.events) sb.Append("    \"").Append(e.Replace("\"", "'")).Append("\",\n"); sb.Length -= 2; sb.Append("\n  ],\n");
            // порядок вскрытия слоёв по зонам (первое появление материала среди убранных ячеек)
            sb.Append("  \"exposureOrder\": {\n");
            var byZone = new Dictionary<byte, List<string>>();
            foreach (var e in d.exposed) { if (!byZone.TryGetValue(e.zone, out var l)) byZone[e.zone] = l = new List<string>(); if (!l.Contains(e.mat)) l.Add(e.mat); }
            foreach (var kv in byZone) sb.Append("    \"").Append(AnatZone.Names[kv.Key]).Append("\": \"").Append(string.Join(" → ", kv.Value)).Append("\",\n");
            sb.Length -= 2; sb.Append("\n  },\n");
            g.Dispose();

            // струя из височной артерии: удары в висок, пока не вскроется поверхностная височная артерия
            var g2 = Make(mat, layout, cam, rt); var d2 = g2.d; int th = 0;
            while (th < 40 && !d2.events.Exists(e => e.Contains("височная артерия"))) { d2.Hit(new float3(12, 2.6f, 2.0f), new float3(-1, 0, 0), 3.8f, true); th++; for (int k = 0; k < 90; k++) d2.Step(dt); }
            sb.Append("  \"templeHitsToArtery\": ").Append(th).Append(", \"templeArteryOpen\": ").Append(d2.events.Exists(e => e.Contains("височная артерия")) ? "true" : "false").Append(",\n");
            float t0 = d2.time; int peak = 0; float peakT = 0;
            while (d2.time < t0 + 3) { d2.Step(dt); if (d2.drops.Count > peak) { peak = d2.drops.Count; peakT = d2.time; } }
            // снимок на пике выброса следующего удара сердца
            float tb = d2.time; while (d2.time < tb + 1.2f) { int before = d2.drops.Count; d2.Step(dt); if (d2.drops.Count >= 6 && d2.drops.Count > before) { for (int k = 0; k < 4; k++) d2.Step(dt); break; } }
            g2.Shot("temple-jet", C(-10, 12, 45), C(9, 2.5f, 1.5f), 30);
            g2.Shot("temple-jet-front", C(30, 4, 40), C(9, 2.5f, 1.5f), 30);
            for (int k = 0; k < 600; k++) d2.Step(dt);
            g2.Shot("temple-10s-later", C(30, 4, 40), C(9, 1.0f, 1.5f), 30);
            sb.Append("  \"templeDropsPeak\": ").Append(peak).Append(",\n");
            sb.Append("  \"templeEvents\": [\n"); foreach (var e in d2.events) sb.Append("    \"").Append(e.Replace("\"", "'")).Append("\",\n"); sb.Length -= 2; sb.Append("\n  ],\n");
            g2.Dispose();

            layout.Dispose(); rt.Release();
            sb.Append("  \"ok\": true\n}\n");
            File.WriteAllText(Path.Combine(OutDir, "scenario.json"), sb.ToString());
            Debug.Log("ANAT_B_OK");
        }
    }
}
