using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    /// Просмотр головы (AnatView, режим -anatView): луч удара из камеры, разрезы и восстановление.
    public class AnatomyViewTests
    {
        static readonly Vector3 Center = new Vector3(0, 1.6f, 0);
        static Vector3 Cm(AnatView v, float x, float y, float z) => v.ToWorld(new float3(x, y, z));

        [Test]
        public void RayFromCameraHitsExpectedSkinVoxel()
        {
            var v = new AnatView(null, Center, null);
            var go = new GameObject("cam"); var cam = go.AddComponent<Camera>(); cam.fieldOfView = 20; cam.nearClipPlane = 0.005f;   // как в просмотре: луч начинается у камеры, а не в 30 см перед ней
            try
            {
                // ракурс щеки из A2 (shot-cheek): камера слева-спереди, луч через центр кадра
                go.transform.position = Cm(v, -16, 1, 30); go.transform.LookAt(Cm(v, -3.5f, 1.5f, 6));
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                Assert.IsTrue(v.Pick(ray, out var hit, out byte m, out var from), "луч не попал в голову");
                Assert.AreEqual(AnatMat.Skin, m, "первая ткань по лучу — не кожа");
                Assert.AreSame(v.a.head, from);
                // ожидаемый воксель: частый шаг по лучу (0,005 см), первая сплошная ячейка любой решётки
                float3 o = v.ToHead(ray.origin), d = math.normalize((float3)ray.direction);
                int expect = -1;
                for (float t = 0; t < 80; t += 0.005f)
                {
                    float3 p = o + d * t;
                    if (!AnatMat.Solid(v.a.Sample(p, out _, out var g))) continue;
                    Assert.AreSame(v.a.head, g); expect = v.a.head.Lookup(p); break;
                }
                Assert.GreaterOrEqual(expect, 0);
                Assert.AreEqual(expect, v.a.head.Lookup(hit + d * 0.001f), "удар попал не в ту ячейку кожи");
                // и удар по ней убирает именно её
                int removed = v.Hit(hit, d, 0);
                Assert.Greater(removed, 0);
                Assert.AreEqual(AnatMat.Empty, v.a.head.mat[expect]);
            }
            finally { Object.DestroyImmediate(go); v.Dispose(); }
        }

        [Test]
        public void CutsThenWholeThenRestoreReturnOriginal()
        {
            var v = new AnatView(null, Center, null);
            try
            {
                int tris0 = v.Triangles;
                for (int c = 1; c <= 4; c++)
                {
                    v.SetCut(c);
                    Assert.Greater(v.DiffFromPristine(), 1000, "разрез " + c + " ничего не снял");
                    Assert.AreNotEqual(tris0, v.Triangles, "разрез " + c + ": сетка не перестроилась");
                }
                v.Shift(0.2f); v.Shift(-0.4f);
                v.SetCut(0);
                Assert.AreEqual(0, v.DiffFromPristine(), "0 не вернул целую голову");
                Assert.AreEqual(tris0, v.Triangles);
                // удар, разрез, R — всё как было
                var ray = new Ray(Cm(v, -30, 2, 30), (Cm(v, -4, 2, 6) - Cm(v, -30, 2, 30)).normalized);
                Assert.IsTrue(v.Pick(ray, out var hit, out _, out _));
                Assert.Greater(v.Hit(hit, (float3)ray.direction, 2), 0);
                v.SetCut(2);
                Assert.Greater(v.DiffFromPristine(), 0);
                v.Restore();
                Assert.AreEqual(0, v.cut);
                Assert.AreEqual(0, v.DiffFromPristine(), "R не вернул исходные ячейки");
                Assert.AreEqual(tris0, v.Triangles, "R не вернул исходную сетку");
            }
            finally { v.Dispose(); }
        }
    }
}
