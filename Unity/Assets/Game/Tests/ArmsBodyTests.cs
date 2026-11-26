using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace VoxGame.Tests
{
    // Руки и тело (ARMS-BODY.md): правка только вида. Точки контакта перчаток игрока (оси камеры и проекция на экран)
    // в покое и в пике джеба, хука, апперкота; суставы, перчатки и крепление головы соперника; габариты торса и ног —
    // как до правки (ArmsBodyBaseline, снят на adba7a5) в пределах 1 %. Все бойцы ростера собираются с новыми руками.
    public class ArmsBodyTests
    {
        static readonly PunchType[] Kinds = { PunchType.Jab, PunchType.Hook, PunchType.Upper };

        static void Add(List<KeyValuePair<string, float>> o, string n, Vector3 v)
        {
            o.Add(new KeyValuePair<string, float>(n + ".x", v.x));
            o.Add(new KeyValuePair<string, float>(n + ".y", v.y));
            o.Add(new KeyValuePair<string, float>(n + ".z", v.z));
        }

        static void Glove(List<KeyValuePair<string, float>> o, string n, Transform g, float tanH, float tanV)
        {
            Vector3 p = g.position;
            Add(o, n, p);
            o.Add(new KeyValuePair<string, float>(n + ".vx", 0.5f + 0.5f * p.x / (p.z * tanH)));
            o.Add(new KeyValuePair<string, float>(n + ".vy", 0.5f + 0.5f * p.y / (p.z * tanV)));
        }

        // Все значения раскладки: имя → число (метры, доли экрана)
        public static List<KeyValuePair<string, float>> Layout()
        {
            var o = new List<KeyValuePair<string, float>>();
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            try
            {
                foreach (var (w, h, tag) in new[] { (1080, 2400, "p"), (2400, 1080, "l") })
                {
                    var root = new GameObject("cam");
                    var g = new PlayerGloves(root.transform, GloveLook.FromEntry(GlovesCatalog.All[0]), mat);
                    float fov = GameUtil.CalcFov(w, h), tanV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), tanH = tanV * w / h;
                    g.SetView(fov, w / (float)h);
                    var rig = root.transform.Find("playerArms");
                    for (int i = 0; i < 40; i++) g.Tick(1f / 60f);
                    Glove(o, tag + ".rest0", rig.Find("playerGlove0"), tanH, tanV);
                    Glove(o, tag + ".rest1", rig.Find("playerGlove1"), tanH, tanV);
                    foreach (var kind in Kinds)
                        foreach (int side in new[] { -1, 1 })
                        {
                            int s = side < 0 ? 0 : 1;
                            g.Punch(side, kind, 0.2f, new Vector3(side * 0.04f, -0.03f, 1.25f));
                            for (int i = 0; i < 20 && g.Extension(s) < 1f; i++) g.Tick(1f / 60f);
                            Glove(o, tag + "." + kind + s, rig.Find("playerGlove" + s), tanH, tanV);
                            for (int i = 0; i < 40; i++) g.Tick(1f / 60f);
                        }
                    g.Dispose();
                    Object.DestroyImmediate(root);
                }

                var roster = new RosterDef(42u);
                var parent = new GameObject("foes");
                for (int f = 0; f < RosterDef.Count; f++)
                {
                    var b = new FoeBody(parent.transform, roster.Get(f), mat, true);
                    string n = "foe" + f;
                    Add(o, n + ".head", b.headMount.position);
                    foreach (var part in new[] { "upperArm0", "upperArm1", "foreArm0", "foreArm1", "glove0", "glove1" })
                        Add(o, n + "." + part, b.root.Find(part).position);
                    foreach (var part in new[] { "torso", "legs" })
                    {
                        var t = Deep(b.root, part);
                        var bb = t.GetComponent<MeshFilter>().sharedMesh.bounds;
                        Add(o, n + "." + part + ".size", bb.size);
                        Add(o, n + "." + part + ".center", bb.center);
                    }
                    b.Dispose();
                }
                Object.DestroyImmediate(parent);
            }
            finally { Object.DestroyImmediate(mat); }
            return o;
        }

        static Transform Deep(Transform r, string name)
        {
            foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        // Допуск 1 %: точка — 1 % её значения (не меньше 0,5 мм), доля экрана — 0,01, центр габарита — 1 % размера по той же оси.
        [Test]
        public void LayoutUnchanged()
        {
            var now = new Dictionary<string, float>();
            foreach (var kv in Layout()) now[kv.Key] = kv.Value;
            var bl = new Dictionary<string, float>();
            foreach (var (k, v) in ArmsBodyBaseline.Values) bl[k] = v;
            Assert.AreEqual(bl.Count, now.Count, "число значений");
            var bad = new StringBuilder();
            int n = 0;
            foreach (var kv in bl)
            {
                Assert.IsTrue(now.ContainsKey(kv.Key), kv.Key);
                float a = now[kv.Key], b = kv.Value, tol;
                if (kv.Key.EndsWith(".vx") || kv.Key.EndsWith(".vy")) tol = 0.01f;
                else if (kv.Key.Contains(".center.")) tol = 0.01f * bl[kv.Key.Replace(".center.", ".size.")];
                else tol = Mathf.Max(0.0005f, 0.01f * Mathf.Abs(b));
                if (Mathf.Abs(a - b) > tol) { bad.AppendLine(kv.Key + " было " + b + " стало " + a); n++; }
            }
            Assert.AreEqual(0, n, bad.ToString());
        }

        // Все бойцы ростера собираются с новыми руками и телом; у каждой части есть сетка.
        [Test]
        public void RosterBuildsWithNewArms()
        {
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var parent = new GameObject("foes");
            var roster = new RosterDef(42u);
            try
            {
                for (int f = 0; f < RosterDef.Count; f++)
                {
                    var b = new FoeBody(parent.transform, roster.Get(f), mat, true);
                    foreach (var part in new[] { "torso", "legs", "upperArm0", "upperArm1", "foreArm0", "foreArm1", "glove0", "glove1" })
                    {
                        var t = Deep(b.root, part);
                        Assert.IsNotNull(t, f + ": " + part);
                        Assert.Greater(t.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0), 3000u, f + ": " + part);
                    }
                    b.Dispose();
                }
                for (int i = 0; i < GloveLooks.Count; i++)
                {
                    var look = GloveLooks.At(i);
                    var ms = new[] { GloveLooks.BuildGloveMesh(look, 1), GloveLooks.BuildGloveMesh(look, -1), GloveLooks.BuildForeArmMesh(look) };
                    foreach (var m in ms) { Assert.Greater(m.GetIndexCount(0), 3000u, look.id); Object.DestroyImmediate(m); }
                }
            }
            finally { Object.DestroyImmediate(parent); Object.DestroyImmediate(mat); }
        }

        [Test]
        public void LayoutDump()
        {
            var sb = new StringBuilder();
            foreach (var kv in Layout()) sb.AppendLine(kv.Key + "=" + kv.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            var p = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "arms-layout.txt"));
            File.WriteAllText(p, sb.ToString());
        }
    }
}
