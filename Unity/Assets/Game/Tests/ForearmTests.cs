using NUnit.Framework;
using UnityEngine;

namespace VoxGame.Tests
{
    // Поле зрения рук игрока (PlayerGloves.ViewFix, FOREARM.md): правка только вида.
    // Перчатка в покое и в пике каждого удара стоит в той же точке (оси камеры), что без правки; рука на экране
    // мельче только в портрете (масштаб k, k, 1), в ландшафте — как была.
    public class ForearmTests
    {
        static readonly PunchType[] Kinds = { PunchType.Jab, PunchType.Hook, PunchType.Upper, PunchType.Mega };

        static Vector3[] Poses(bool fix, bool portrait, out Vector3 rigScale)
        {
            bool was = PlayerGloves.ViewFix;
            PlayerGloves.ViewFix = fix;
            var root = new GameObject("cam");
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var res = new Vector3[2 + Kinds.Length * 2];
            try
            {
                var g = new PlayerGloves(root.transform, GloveLook.FromEntry(GlovesCatalog.All[0]), mat);
                int w = portrait ? 540 : 1170, h = portrait ? 1170 : 540;
                g.SetView(GameUtil.CalcFov(w, h), w / (float)h);
                var rig = root.transform.Find("playerArms");
                rigScale = rig.localScale;
                for (int i = 0; i < 40; i++) g.Tick(1f / 60f);
                res[0] = rig.Find("playerGlove0").position; res[1] = rig.Find("playerGlove1").position;
                int n = 2;
                foreach (var kind in Kinds)
                    foreach (int side in new[] { -1, 1 })
                    {
                        int s = side < 0 ? 0 : 1;
                        g.Punch(side, kind, 0.2f, new Vector3(side * 0.04f, -0.03f, 1.25f));
                        for (int i = 0; i < 20 && g.Extension(s) < 1f; i++) g.Tick(1f / 60f);
                        res[n++] = rig.Find("playerGlove" + s).position;
                        for (int i = 0; i < 40; i++) g.Tick(1f / 60f);
                    }
                g.Dispose();
            }
            finally
            {
                PlayerGloves.ViewFix = was;
                Object.DestroyImmediate(root); Object.DestroyImmediate(mat);
            }
            return res;
        }

        [Test]
        public void GloveSpotsUnchanged([Values(true, false)] bool portrait)
        {
            var a = Poses(false, portrait, out var s0);
            var b = Poses(true, portrait, out var s1);
            Assert.AreEqual(Vector3.one, s0, "без правки масштаб рук 1");
            if (portrait)
            {
                Assert.That(s1.x, Is.InRange(0.5f, 0.7f), "портрет: руки мельче");
                Assert.AreEqual(s1.x, s1.y, 1e-6f, "без искажения на экране");
                Assert.AreEqual(1f, s1.z, 1e-6f, "глубина та же");
            }
            else Assert.AreEqual(Vector3.one, s1, "ландшафт: как было");
            for (int i = 0; i < a.Length; i++)
                Assert.Less((a[i] - b[i]).magnitude, 0.002f, "перчатка на том же месте, поза " + i + ": " + a[i] + " / " + b[i]);
        }
    }
}
