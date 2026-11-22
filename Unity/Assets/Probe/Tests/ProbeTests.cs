using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    public class ProbeTests
    {
        static readonly int3[] N = { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };

        static bool Solid(NativeArray<byte> v, int3 d, int3 p) => math.all(p >= 0) && math.all(p < d) && v[p.x + d.x * (p.y + d.y * p.z)] != 0;

        /// Открытые грани перебором: ключ (направление, ячейка) → материал.
        static Dictionary<long, byte> NaiveFaces(NativeArray<byte> v, int3 d)
        {
            var f = new Dictionary<long, byte>();
            for (int z = 0; z < d.z; z++) for (int y = 0; y < d.y; y++) for (int x = 0; x < d.x; x++)
            {
                var p = new int3(x, y, z); byte m = v[x + d.x * (y + d.y * z)]; if (m == 0) continue;
                for (int k = 0; k < 6; k++) if (!Solid(v, d, p + N[k])) f[Key(k, p)] = m;
            }
            return f;
        }

        static long Key(int dir, int3 p) => ((long)dir << 48) | ((long)p.x << 32) | ((long)p.y << 16) | (long)p.z;

        /// Прямоугольники всех чанков → грани. Проверяет, что ни одна грань не покрыта дважды.
        static Dictionary<long, byte> GreedyFaces(NativeArray<byte> v, int3 d, int mode, out int quadCount)
        {
            var f = new Dictionary<long, byte>();
            var quads = new NativeList<Quad>(4096, Allocator.Temp);
            int3 nch = (d + GreedyMesher.CS - 1) / GreedyMesher.CS;
            quadCount = 0;
            for (int cz = 0; cz < nch.z; cz++) for (int cy = 0; cy < nch.y; cy++) for (int cx = 0; cx < nch.x; cx++)
            {
                quads.Clear();
                int3 org = new int3(cx, cy, cz) * GreedyMesher.CS;
                GreedyMesher.BuildQuads(v, d, org, mode, ref quads);
                quadCount += quads.Length;
                for (int i = 0; i < quads.Length; i++)
                {
                    var q = quads[i]; int A = q.dir >> 1;
                    for (int bb = 0; bb < q.h; bb++) for (int aa = 0; aa < q.w; aa++)
                    {
                        int a = q.a + aa, b = q.b + bb;
                        int3 p = A == 0 ? new int3(q.d, a, b) : A == 1 ? new int3(a, q.d, b) : new int3(a, b, q.d);
                        long k = Key(q.dir, org + p);
                        Assert.IsFalse(f.ContainsKey(k), "грань покрыта дважды");
                        f[k] = q.mat;
                    }
                }
            }
            quads.Dispose();
            return f;
        }

        static void AssertSame(Dictionary<long, byte> a, Dictionary<long, byte> b)
        {
            Assert.AreEqual(a.Count, b.Count, "разное число граней");
            foreach (var kv in a)
            {
                Assert.IsTrue(b.TryGetValue(kv.Key, out byte m), "грань не покрыта");
                Assert.AreEqual(kv.Value, m, "другой материал");
            }
        }

        [Test]
        public void Greedy_CoversSameSurfaceAsNaive_Head()
        {
            var v = HeadBuilder.Build(out int3 d);
            try
            {
                var naive = NaiveFaces(v, d);
                AssertSame(naive, GreedyFaces(v, d, GreedyMesher.ModeRender, out int qa));
                AssertSame(naive, GreedyFaces(v, d, GreedyMesher.ModeNoAO, out int qb));
                Assert.Less(qa, naive.Count, "объединение не уменьшило число граней");
                Assert.LessOrEqual(qb, qa);
                Debug.Log($"head: naive faces {naive.Count}, greedy AO {qa}, greedy noAO {qb}");
            }
            finally { v.Dispose(); }
        }

        [Test]
        public void Greedy_CoversSameSurfaceAsNaive_RandomNoise()
        {
            var d = new int3(70, 40, 35);
            var v = new NativeArray<byte>(d.x * d.y * d.z, Allocator.Temp);
            var r = new Unity.Mathematics.Random(7);
            for (int i = 0; i < v.Length; i++) v[i] = (byte)(r.NextFloat() < 0.55f ? r.NextInt(1, 4) : 0);
            AssertSame(NaiveFaces(v, d), GreedyFaces(v, d, GreedyMesher.ModeRender, out _));
            AssertSame(NaiveFaces(v, d), GreedyFaces(v, d, GreedyMesher.ModeNoAO, out _));
            v.Dispose();
        }

        [Test]
        public void Carve_OnChunkBorder_RebuildsBothChunks()
        {
            var d = new int3(64, 32, 32);
            var v = new NativeArray<byte>(d.x * d.y * d.z, Allocator.Persistent);
            for (int z = 8; z < 24; z++) for (int y = 8; y < 24; y++) for (int x = 20; x < 44; x++) v[x + d.x * (y + d.y * z)] = VoxMat.Muscle;
            var go = new GameObject("t");
            var body = new VoxBody(go.transform, null, 0.01f);
            try
            {
                body.SetGrid(v, d);
                Assert.AreEqual(2, body.ChunkCount);
                for (int i = 0; i < 2; i++) body.dirty[i] = false;
                body.dirtyCount = 0;
                int before0 = Faces(body, 0), before1 = Faces(body, 1);
                // ячейка x=32 — первая в чанке 1, её сосед x=31 — в чанке 0
                var res = new NativeArray<int>(8, Allocator.TempJob);
                var seeds = new NativeArray<float3>(1, Allocator.TempJob);
                new CarveJob { vox = body.vox, dims = d, c = new float3(32.5f, 16.5f, 16.5f), r = 0.6f, seeds = seeds, result = res }.Run();
                Assert.AreEqual(1, res[0]);
                body.MarkDirty(new int3(res[1], res[2], res[3]), new int3(res[4], res[5], res[6]));
                res.Dispose(); seeds.Dispose();
                Assert.IsTrue(body.dirty[0], "чанок 0 не помечен");
                Assert.IsTrue(body.dirty[1], "чанок 1 не помечен");
                Assert.AreEqual(before0 + 1, Faces(body, 0), "в чанке 0 должна появиться грань +x у x=31");
                Assert.AreEqual(before1 + 5, Faces(body, 1), "в чанке 1 — пять граней вокруг ямы");
            }
            finally { body.Dispose(); Object.DestroyImmediate(go); }
        }

        static int Faces(VoxBody b, int chunk)
        {
            var q = new NativeList<Quad>(256, Allocator.Temp);
            int n = GreedyMesher.BuildQuads(b.vox, b.dims, b.ChunkCoord(chunk) * GreedyMesher.CS, GreedyMesher.ModeRender, ref q);
            q.Dispose();
            return n;
        }

        [Test]
        public void Islands_FindsCutPiece_KeepsAnchored()
        {
            var d = new int3(24, 48, 24);
            int n = d.x * d.y * d.z;
            var v = new NativeArray<byte>(n, Allocator.Persistent);
            void Box(int3 a, int3 b) { for (int z = a.z; z <= b.z; z++) for (int y = a.y; y <= b.y; y++) for (int x = a.x; x <= b.x; x++) v[x + d.x * (y + d.y * z)] = VoxMat.Bone; }
            Box(new int3(8, 0, 8), new int3(14, 30, 14));   // столб от якоря
            Box(new int3(9, 34, 9), new int3(13, 40, 13));  // отрезанная голова: 5*7*5 = 175
            Box(new int3(14, 20, 10), new int3(20, 22, 12)); // выступ, связанный со столбом
            var stamp = new NativeArray<int>(n, Allocator.Persistent);
            var queue = new NativeArray<int>(n, Allocator.Persistent);
            var cv = new NativeList<int>(Allocator.Persistent); var cs = new NativeList<int2>(Allocator.Persistent);
            var cmn = new NativeList<int3>(Allocator.Persistent); var cmx = new NativeList<int3>(Allocator.Persistent);
            var res = new NativeArray<int>(2, Allocator.Persistent);
            try
            {
                // вся решётка
                new IslandJob { vox = v, dims = d, mn = int3.zero, mx = d - 1, anchorY = 2, stamp = stamp, queue = queue, gen = 1, compVox = cv, comps = cs, compMin = cmn, compMax = cmx, result = res }.Run();
                Assert.AreEqual(1, cs.Length);
                Assert.AreEqual(175, cs[0].y);
                Assert.AreEqual(new int3(9, 34, 9), cmn[0]); Assert.AreEqual(new int3(13, 40, 13), cmx[0]);
                // только окрестность: верх столба и кусок. Столб касается нижнего края области — считается связанным.
                new IslandJob { vox = v, dims = d, mn = new int3(4, 24, 4), mx = new int3(20, 44, 20), anchorY = 2, stamp = stamp, queue = queue, gen = 2, compVox = cv, comps = cs, compMin = cmn, compMax = cmx, result = res }.Run();
                Assert.AreEqual(1, cs.Length);
                Assert.AreEqual(175, cs[0].y);
                // перемычка: кусок прирос к столбу — отрываться нечему
                Box(new int3(11, 31, 11), new int3(11, 33, 11));
                new IslandJob { vox = v, dims = d, mn = int3.zero, mx = d - 1, anchorY = 2, stamp = stamp, queue = queue, gen = 3, compVox = cv, comps = cs, compMin = cmn, compMax = cmx, result = res }.Run();
                Assert.AreEqual(0, cs.Length);
                // перерезали перемычку — снова один кусок
                v[11 + d.x * (32 + d.y * 11)] = 0;
                new IslandJob { vox = v, dims = d, mn = new int3(6, 26, 6), mx = new int3(16, 42, 16), anchorY = 2, stamp = stamp, queue = queue, gen = 4, compVox = cv, comps = cs, compMin = cmn, compMax = cmx, result = res }.Run();
                Assert.AreEqual(1, cs.Length);
                Assert.AreEqual(176, cs[0].y);
            }
            finally { v.Dispose(); stamp.Dispose(); queue.Dispose(); cv.Dispose(); cs.Dispose(); cmn.Dispose(); cmx.Dispose(); res.Dispose(); }
        }
    }
}
