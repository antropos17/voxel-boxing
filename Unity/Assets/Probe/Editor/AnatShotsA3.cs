using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxProbe.Editor
{
    /// Этап A3: снимки позы шеи в редакторе (без сборки плеера) — для проверки изгиба и поиска дефектов.
    /// Вызов: unity command script eval 'VoxProbe.Editor.AnatShotsA3.Shot("имя", кивок, поворот, наклон, камера x,y,z, цель x,y,z, fov, cull); return "ok";'
    /// Пишет Logs/a3/имя.png. Камера и цель — в см головы.
    public static class AnatShotsA3
    {
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "a3"));
        static Rig rig;

        public static string Shot(string name, float nod, float turn, float tilt, float ex, float ey, float ez, float tx, float ty, float tz, float fov = 24, bool cull = true)
        {
            rig ??= new Rig();
            if (rig.cull != cull) rig.Cull(cull);
            AnatSkin.Reset(); AnatSkin.on = true; AnatSkin.pose = math.radians(new float3(nod, turn, tilt));
            AnatSkin.SetGlobals(rig.root);
            string path = Path.Combine(Dir, name + ".png");
            rig.Shot(path, new Vector3(ex, ey, ez) * 0.01f, new Vector3(tx, ty, tz) * 0.01f, fov);
            AnatSkin.Off();
            return path;
        }

        /// Сбросить сцену (после правки кода рига или решётки).
        public static void Free() { rig?.Dispose(); rig = null; }

        class Rig
        {
            public AnatHead a; public readonly List<AnatBody> bodies = new List<AnatBody>();
            public Transform root; public Camera cam; public RenderTexture rt; public bool cull = true;
            NativeArray<VertexAttributeDescriptor> layout;

            public Rig()
            {
                var mat = AnatShots.EnsureMaterial();
                AnatPalette.Apply();
                a = AnatomyBuilder.Build();
                layout = new NativeArray<VertexAttributeDescriptor>(AnatMesher.Layout, Allocator.Persistent);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                root = new GameObject("anat").transform;
                int id = 0;
                foreach (var g in a.All) bodies.Add(new AnatBody(g, root, mat, id++));
                foreach (var b in bodies) b.BuildAll(layout);
                var camGo = new GameObject("cam");
                cam = camGo.AddComponent<Camera>();
                var ad = camGo.AddComponent<UniversalAdditionalCameraData>();
                ad.renderShadows = false; ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
                cam.nearClipPlane = 0.01f; cam.farClipPlane = 5;
                var light = new GameObject("sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.None;
                light.transform.rotation = Quaternion.Euler(30, 160, 0);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
                rt = new RenderTexture(900, 900, 24) { name = "a3" };
                cam.targetTexture = rt;
            }

            public void Cull(bool on) { cull = on; foreach (var b in bodies) { b.cull = on; b.BuildAll(layout); } }

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

            public void Dispose()
            {
                foreach (var b in bodies) b.Destroy();
                if (layout.IsCreated) layout.Dispose();
                if (rt != null) Object.DestroyImmediate(rt);
                a.Dispose();
            }
        }
    }
}
