using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe.Tests
{
    /// Этап B, часть 9: переломы §5 — порядок прочности, линии по швам и Ле Фору, смещение отломка, пределы, детерминизм.
    public class AnatomyB9Tests
    {
        static readonly float3 Back = new float3(0, 0, -1);

        /// Пороги идут в порядке таблицы §5; нос ломается одним мощным ударом, лоб — не раньше шестого.
        [Test]
        public void StrengthOrder()
        {
            for (int i = 0; i < AnatBones.Count; i++) for (int j = 0; j < AnatBones.Count; j++)
                if (AnatBones.Rank[i] < AnatBones.Rank[j]) Assert.Less(AnatBones.Thr[i], AnatBones.Thr[j], AnatBones.Names[i] + " < " + AnatBones.Names[j]);
            Assert.AreEqual(0, AnatBones.Rank[AnatBones.Nose]); Assert.AreEqual(9, AnatBones.Rank[AnatBones.Occipital]);
            var a = AnatomyBuilder.Build(); var b = new AnatBones(a);
            try
            {
                float3 n = new float3(0, 0, 1);
                b.Hit(AnatZone.Nose, new float3(0, 1, 9.5f), n, Back, 3.0f, true, 1, 0);
                Assert.AreEqual(1, b.Level(AnatBones.Nose), "нос — одним мощным ударом");
                b.Hit(AnatZone.CheekBone, new float3(4, 0, 7), n, Back, 3.0f, true, 2, 0);
                Assert.AreEqual(0, b.Level(AnatBones.ZygL), "скула — не с одного удара");
                int k = 0;
                while (b.Level(AnatBones.Frontal) == 0 && k < 20) { b.Hit(AnatZone.Fore, new float3(0, 7, 8), n, Back, 3.0f, true, 3 + k, 0); k++; }
                Assert.AreEqual(6, k, "лоб — на шестом мощном ударе");
                Assert.AreEqual(0, b.Level(AnatBones.ZygL), "лоб не грузит скулу");
            }
            finally { b.Dispose(); a.Dispose(); }
        }

        /// Граница отломка с соседней костью проходит по шву (части разные), у Ле Фора I — ещё по его линии, у глазницы — в тонком дне.
        [Test]
        public void LinesOnSuturesAndLeFort()
        {
            var a = AnatomyBuilder.Build();
            try
            {
                var h = a.head;
                void Check(byte bone, int side, int level, bool leFort1)
                {
                    int pairs = 0, same = 0, off = 0; float3 c = new float3(side == 0 ? 4 : -4, 0, 7);
                    for (int z = 1; z < h.dims.z - 1; z++) for (int y = 1; y < h.dims.y - 1; y++) for (int x = 1; x < h.dims.x - 1; x++)
                    {
                        int i = h.Index(x, y, z); byte m = h.mat[i]; if (m != AnatMat.Compact && m != AnatMat.Spongy) continue;
                        float3 p = h.Local(x, y, z); if (!AnatBones.Member(bone, side, level, p, h.part[i], m, h.zone[i], c)) continue;
                        for (int d = 0; d < 6; d++)
                        {
                            int3 q = new int3(x, y, z) + new int3(d == 0 ? 1 : d == 1 ? -1 : 0, d == 2 ? 1 : d == 3 ? -1 : 0, d == 4 ? 1 : d == 5 ? -1 : 0);
                            int j = h.Index(q.x, q.y, q.z); byte mj = h.mat[j]; if (mj != AnatMat.Compact && mj != AnatMat.Spongy) continue;
                            if (AnatBones.Member(bone, side, level, h.Local(q.x, q.y, q.z), h.part[j], mj, h.zone[j], c)) continue;
                            pairs++;
                            if ((h.part[i] & 0x7F) != (h.part[j] & 0x7F)) { Assert.AreNotEqual(0, h.part[i] & AnatPart.Suture, "клетка на стыке костей помечена швом"); continue; }
                            same++; if (!(leFort1 && math.abs(math.min(p.y, h.Local(q.x, q.y, q.z).y) - AnatBones.LeFort1Y) < 0.25f)) off++;
                        }
                    }
                    string nm = AnatBones.Names[bone] + " ур. " + level;
                    Debug.Log("ANAT_TEST b9 line " + nm + ": граница " + pairs + ", внутри кости " + same + ", вне линии " + off);
                    Assert.Greater(pairs, 20, nm + ": есть граница"); Assert.AreEqual(0, off, nm + ": граница только по швам" + (leFort1 ? " и линии Ле Фора I" : ""));
                    if (leFort1) Assert.Greater(same, 20, "Ле Фор I режет верхнюю челюсть поперёк");
                }
                Check(AnatBones.Nose, 0, 1, false);
                Check(AnatBones.ZygL, 0, 1, false);
                Check(AnatBones.Maxilla, 0, 1, true);
                Check(AnatBones.Maxilla, 0, 2, false);
                Check(AnatBones.Maxilla, 0, 3, false);
                // дно глазницы: отломок только из тонкой кости дна (Ле Фор и швы ни при чём — «взрывной» перелом по тонкому месту)
                int n = 0, thick = 0;
                for (int i = 0; i < h.Count; i++)
                {
                    byte m = h.mat[i]; if (m != AnatMat.Compact && m != AnatMat.Spongy) continue;
                    int x = i % h.dims.x, y = (i / h.dims.x) % h.dims.y, z = i / (h.dims.x * h.dims.y);
                    if (!AnatBones.Member(AnatBones.OrbitL, 0, 1, h.Local(x, y, z), h.part[i], m, h.zone[i], 0)) continue;
                    n++; if (m == AnatMat.Spongy) thick++;
                }
                Debug.Log("ANAT_TEST b9 orbit floor: " + n + " ячеек, губчатых " + thick);
                Assert.Greater(n, 30); Assert.Less(thick, n / 2, "дно глазницы — тонкая кость");
                int jb = 0; var g = a.mand; for (int i = 0; i < g.Count; i++) if ((g.part[i] & 0x7F) == AnatPart.Mandible && (g.mat[i] == AnatMat.Compact || g.mat[i] == AnatMat.Spongy)) jb++;
                Assert.Greater(jb, 1000, "кость нижней челюсти помечена частью «нижняя челюсть»");
            }
            finally { a.Dispose(); }
        }

        /// Мощный удар в нос сбоку: носовые кости и мягкие ткани над ними смещаются по удару, лоб не двигается; маска — со следующего кадра.
        [Test]
        public void FragmentShifts()
        {
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            try
            {
                var h = v.a.head; float3 dir = math.normalize(new float3(0.4f, 0, -1));
                v.Hit(new float3(-2.0f, 1.6f, 14), dir, 1); v.Tick(1 / 30f);
                Assert.AreEqual(1, v.bones.Level(AnatBones.Nose), string.Join("; ", v.bones.log));
                Assert.IsTrue(v.bones.Pending, "маска строится после кадра удара");
                v.Tick(1 / 30f); Assert.IsFalse(v.bones.Pending);
                float3 Surf(float3 o) { for (float t = 0; t < 12; t += 0.05f) { int i = h.Lookup(o + Back * t); if (i >= 0 && AnatMat.Cull(h.mat[i]) && h.mat[i] != AnatMat.Covered) return o + Back * t; } return o; }
                float3 dorsum = Surf(new float3(0, 2.2f, 16)), fore = Surf(new float3(0, 7.5f, 16)), chin = Surf(new float3(0, -8, 16));
                float3 dn = v.bones.DispAt(0, dorsum), df = v.bones.DispAt(0, fore), dc = v.bones.DispAt(0, chin);
                Debug.Log("ANAT_TEST b9 shift: спинка носа " + dorsum + " → " + dn + ", лоб " + df + ", подбородок " + dc + "; " + string.Join("; ", v.bones.log));
                Assert.Greater(dn.x, 0.25f, "кожа над носовыми костями ушла по удару (+x)");
                Assert.Less(math.length(df), 0.02f, "лоб на месте"); Assert.Less(math.length(dc), 0.02f, "подбородок на месте");
                // носовая кость — в отломке целиком
                int nb = 0, full = 0;
                for (int i = 0; i < h.Count; i++)
                {
                    if ((h.part[i] & 0x7F) != AnatPart.NasalL || h.mat[i] != AnatMat.Compact) continue;
                    int x = i % h.dims.x, y = (i / h.dims.x) % h.dims.y, z = i / (h.dims.x * h.dims.y);
                    nb++; if (v.bones.WeightAt(0, new float3(x, y, z) + 0.5f).c0[0] > 0.6f) full++;
                }
                Assert.Greater(full, nb * 0.8f, "носовая кость в отломке: " + full + " из " + nb);
            }
            finally { v.Dispose(); }
        }

        /// Пределы: отломков со смещением не больше MaxActive (голова) и 2 (челюсть), уровни ≤ 3, журнал ограничен числом костей и уровней.
        [Test]
        public void Bounded()
        {
            var a = AnatomyBuilder.Build(); var b = new AnatBones(a);
            try
            {
                for (int round = 0; round < 12; round++)
                    for (byte z = 1; z < AnatZone.Count; z++)
                        for (int s = 0; s < 2; s++)
                        {
                            float3 p = new float3(s == 0 ? 3 : -3, 0, round % 3 == 0 ? 6 : round % 3 == 1 ? 0 : -7);
                            b.Hit(z, p, new float3(0, 0, 1), round % 2 == 0 ? Back : new float3(s == 0 ? -1 : 1, 0, 0), 4.4f, true, round, round);
                        }
                b.Complete();
                Debug.Log("ANAT_TEST b9 bounded: переломов " + b.list.Count + ", слотов головы " + b.headSlots + ", челюсти " + b.jawSlots + ", без смещения " + b.noSlot + ", журнал " + b.log.Count);
                Assert.AreEqual(AnatBones.Count, b.list.Count, "сломаны все кости");
                Assert.AreEqual(AnatBones.MaxActive, b.headSlots); Assert.AreEqual(2, b.jawSlots); Assert.AreEqual(AnatBones.Count - 2 - AnatBones.MaxActive, b.noSlot);
                foreach (var r in b.list) Assert.LessOrEqual(r.level, AnatBones.MaxLevel);
                Assert.LessOrEqual(b.log.Count, AnatBones.Count * AnatBones.MaxLevel);
            }
            finally { b.Dispose(); a.Dispose(); }
        }

        static (string log, byte[] mat, byte[] frag, int tris) Run(bool on)
        {
            bool was = AnatBones.On; AnatBones.On = on;
            var v = new AnatView(null, new Vector3(0, 1.6f, 0), null);
            try
            {
                float3[] at = { new float3(-2.0f, 1.6f, 14), new float3(4.4f, -0.9f, 14), new float3(2.6f, 4.4f, 14), new float3(-9, -7.4f, 4.5f), new float3(0.2f, 1.0f, 14), new float3(4.4f, -0.9f, 14) };
                float3[] dr = { new float3(0.4f, 0, -1), new float3(-0.35f, 0, -1), new float3(-0.1f, -0.1f, -1), new float3(1, 0.05f, -0.3f), Back, new float3(-0.35f, 0, -1) };
                for (int r = 0; r < 2; r++) for (int k = 0; k < at.Length; k++) { v.Hit(at[k], math.normalize(dr[k]), 1); for (int f = 0; f < 20; f++) v.Tick(1 / 30f); }
                v.Settle();
                return (string.Join("; ", v.bones.log), v.a.head.mat.ToArray(), v.bones.data[0].ToArray(), v.Triangles);
            }
            finally { v.Dispose(); AnatBones.On = was; }
        }

        /// Два прогона одинаковы побитно (журнал переломов и маски); с переломами и без — та же решётка и те же треугольники.
        [Test]
        public void Deterministic()
        {
            var r1 = Run(true); var r2 = Run(true); var r0 = Run(false);
            Debug.Log("ANAT_TEST b9 determinism: " + r1.log + "; треугольников " + r1.tris + " / без переломов " + r0.tris);
            Assert.IsNotEmpty(r1.log); Assert.AreEqual(r1.log, r2.log); Assert.AreEqual(r1.frag, r2.frag, "маски отломков");
            Assert.AreEqual(r1.mat, r2.mat);
            Assert.IsEmpty(r0.log); Assert.AreEqual(r0.mat, r1.mat, "переломы §5 не меняют разрушение"); Assert.AreEqual(r0.tris, r1.tris);
        }
    }
}
