using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxProbe.Editor
{
    /// Этап B, часть 9: снимки переломов §5 → Unity/Shots/anatomy-b9/. Каждый случай дважды — без переломов (AnatBones.On = false, «0-»)
    /// и с ними («1-»): те же удары, то же разрушение, разница только в переломах. Случаи: нос сбоку, скула Л, Ле Фор II и III,
    /// вдавленный перелом лба; бой (нос, скула, челюсть) с расстояния боя, он же в ANAT_LITE.
    /// Запуск: unity command script eval 'VoxProbe.Editor.AnatShotsB9.Run(); return "ok";' или -executeMethod VoxProbe.Editor.AnatShotsB9.Run
    public static class AnatShotsB9
    {
        static Camera cam; static RenderTexture rt; static AnatView v; static string dir; static Material mat;
        static readonly StringBuilder sb = new StringBuilder();

        struct B { public float3 p, d; public int s; public B(float3 p, float3 d, int s = 1) { this.p = p; this.d = math.normalize(d); this.s = s; } }

        public static void Run()
        {
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "anatomy-b9")); Directory.CreateDirectory(dir);
            mat = AnatShots.EnsureMaterial();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("cam"); cam = camGo.AddComponent<Camera>();
            var ad = camGo.AddComponent<UniversalAdditionalCameraData>(); ad.renderShadows = false; ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f); cam.nearClipPlane = 0.01f; cam.farClipPlane = 5; cam.fieldOfView = 30;
            var lightGo = new GameObject("sun"); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(35, 150, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            rt = new RenderTexture(900, 1100, 24) { name = "shot" }; cam.targetTexture = rt;
            AnatSkin.Reset(); AnatSkin.Off();
            bool was = AnatBones.On, lite = AnatPalette.Lite; sb.Clear();
            try
            {
                var side = new float3(0.4f, 0, -1); var mid = new float3(0, 0.3f, 1);
                Case("nose", new[] { new B(new float3(-2.0f, 1.6f, 14), side) }, (n) => { Shot(n + "-front", 0, 3, 0.42f, mid); Shot(n + "-3q", -35, 8, 0.42f, mid); Shot(n + "-top", 0, 55, 0.4f, mid); });
                var zyg = new B(new float3(4.4f, -0.9f, 14), new float3(-0.35f, 0, -1));
                Case("zygoma", new[] { zyg, zyg, zyg, zyg }, (n) => { Shot(n + "-front", 0, 3, 0.42f, mid); Shot(n + "-3q", 35, 8, 0.42f, mid); Shot(n + "-below", 20, -30, 0.42f, mid); });
                var chL = new B(new float3(3.0f, -2.2f, 14), new float3(-0.15f, 0, -1)); var chR = new B(new float3(-3.0f, -2.2f, 14), new float3(0.15f, 0, -1));
                var mo = new B(new float3(0, -4.6f, 14), new float3(0, 0, -1));
                var lf2 = new[] { chL, chR, chL, chR, chL, chR, mo };
                Case("lefort2", lf2, (n) => { Shot(n + "-front", 0, 3, 0.45f, mid); Shot(n + "-3q", -35, 8, 0.45f, mid); Shot(n + "-side", -85, 0, 0.45f, mid); });
                var lf3 = new[] { chL, chR, chL, chR, chL, chR, mo, chL, chR, chL, mo };
                Case("lefort3", lf3, (n) => { Shot(n + "-front", 0, 3, 0.45f, mid); Shot(n + "-3q", -35, 8, 0.45f, mid); Shot(n + "-side", -85, 0, 0.45f, mid); });
                var fo = new B(new float3(-0.5f, 7.0f, 14), new float3(0, -0.15f, -1));
                Case("frontal", new[] { fo, fo, fo, fo, fo, fo }, (n) => { Shot(n + "-front", 0, 3, 0.42f, new float3(0, 4, 1)); Shot(n + "-3q", -35, 8, 0.42f, new float3(0, 4, 1)); Shot(n + "-side", -80, 0, 0.42f, new float3(0, 4, 1)); });
                var fight = new[] { new B(new float3(-2.0f, 1.6f, 14), side), zyg, new B(new float3(-9, -7.4f, 4.5f), new float3(1, 0.05f, -0.3f)), zyg, new B(new float3(-9, -7.4f, 4.5f), new float3(1, 0.05f, -0.3f)), zyg, zyg };
                Case("fight", fight, (n) => { Shot(n + "-front", 0, 5, 0.85f, new float3(0, -2, 0)); Shot(n + "-3q", 40, 10, 0.85f, new float3(0, -2, 0)); });
                AnatPalette.SetLite(true);
                Case("fight-lite", fight, (n) => { Shot(n + "-front", 0, 5, 0.85f, new float3(0, -2, 0)); Shot(n + "-3q", 40, 10, 0.85f, new float3(0, -2, 0)); Shot(n + "-close", 0, 3, 0.42f, mid); });
            }
            finally { AnatBones.On = was; AnatPalette.SetLite(lite); v?.Dispose(); v = null; }
            File.WriteAllText(Path.Combine(dir, "cases.txt"), sb.ToString());
            Debug.Log("ANAT_B9 shots done\n" + sb);
        }

        static void Case(string name, B[] blows, System.Action<string> shots)
        {
            for (int on = 0; on < 2; on++)
            {
                AnatBones.On = on == 1;
                v?.Dispose(); v = new AnatView(null, new Vector3(0, 1.6f, 0), mat);
                float t = 0.5f;
                foreach (var b in blows) { Ticks(t); v.Hit(b.p, b.d, b.s); t += 1.2f; }
                Ticks(t + 1.5f);
                shots(on + "-" + name);
                sb.Append(on + "-" + name).Append(": ").Append(blows.Length).Append(" ударов, треуг. ").Append(v.Triangles).Append(", переломы: ").Append(string.Join("; ", v.bones.log))
                  .Append(" | травма зон лоб/нос/скула/щека/рот ").Append(v.sim.Eff(AnatZone.Fore).ToString("0.0")).Append('/').Append(v.sim.Eff(AnatZone.Nose).ToString("0.0")).Append('/')
                  .Append(v.sim.Eff(AnatZone.CheekBone).ToString("0.0")).Append('/').Append(v.sim.Eff(AnatZone.Cheek).ToString("0.0")).Append('/').Append(v.sim.Eff(AnatZone.Mouth).ToString("0.0")).Append('\n');
            }
        }

        static void Ticks(float until) { while (v.sim.time < until) v.Tick(1 / 30f, 64); v.Settle(); }

        static void Shot(string name, float yaw, float pitch, float dist, float3 at)
        {
            Vector3 piv = v.ToWorld(at);
            cam.transform.position = piv + Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward * dist; cam.transform.LookAt(piv);
            Shader.SetGlobalFloat("_AnatTime", v.sim.time);
            cam.Render();
            RenderTexture.active = rt;
            var tx = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tx.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tx.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tx.EncodeToPNG());
            Object.DestroyImmediate(tx); RenderTexture.active = null;
        }
    }
}
