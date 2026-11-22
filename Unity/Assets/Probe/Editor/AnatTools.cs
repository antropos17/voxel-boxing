using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace VoxProbe.Editor
{
    /// Пакетный режим: Unity.exe -batchmode -quit -projectPath Unity -executeMethod VoxProbe.Editor.AnatTools.Slices
    /// Срезы анатомической головы и сводка по материалам в Unity/Shots/anatomy/.
    public static class AnatTools
    {
        public static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "anatomy"));

        public static readonly (string name, string title, float3 o, float3 u, float3 v, float w, float h)[] Cuts =
        {
            ("sag-mid", "срединный, вид справа, лицо справа", new float3(0, 1.5f, 0.2f), new float3(0, 0, 1), new float3(0, 1, 0), 24.5f, 27f),
            ("sag-para", "в 3 мм от средней линии, вид справа", new float3(0.3f, 1.5f, 0.2f), new float3(0, 0, 1), new float3(0, 1, 0), 24.5f, 27f),
            ("cor-orbits", "через глазницы (z = 6,0), вид спереди", new float3(0, 0.8f, 6.0f), new float3(1, 0, 0), new float3(0, 1, 0), 19f, 23f),
            ("cor-temple", "через висок и сустав (z = 1,2), вид спереди", new float3(0, 1.0f, 1.2f), new float3(1, 0, 0), new float3(0, 1, 0), 19f, 25f),
            ("ax-teeth-up", "горизонтальный через верхние зубы (y = −4,7), вид сверху", new float3(0, -4.7f, 3.2f), new float3(-1, 0, 0), new float3(0, 0, 1), 16f, 15f),
            ("ax-teeth-low", "горизонтальный через нижние зубы (y = −5,8), вид сверху", new float3(0, -5.8f, 3.2f), new float3(-1, 0, 0), new float3(0, 0, 1), 16f, 15f),
            ("sag-teeth", "через клык и премоляры (x = 1,9), вид справа", new float3(1.9f, -4.6f, 4.6f), new float3(0, 0, 1), new float3(0, 1, 0), 11f, 10f),
            ("ax-brain", "горизонтальный через мозг (y = 6,0), вид сверху", new float3(0, 6.0f, -0.4f), new float3(-1, 0, 0), new float3(0, 0, 1), 18f, 23f),
            ("ax-orbits", "горизонтальный через глаза (y = 2,3), вид сверху", new float3(0, 2.3f, 0.2f), new float3(-1, 0, 0), new float3(0, 0, 1), 18f, 23f),
            ("ax-maxsinus", "горизонтальный через гайморовы пазухи (y = −1,0)", new float3(0, -1.0f, 0.2f), new float3(-1, 0, 0), new float3(0, 0, 1), 18f, 23f),
            ("sag-orbit", "через глазницу (x = 3,2), вид справа", new float3(3.2f, 1.5f, 0.2f), new float3(0, 0, 1), new float3(0, 1, 0), 24.5f, 27f),
        };

        public static void Slices()
        {
            Directory.CreateDirectory(OutDir);
            AnatomyBuilder.Build().Dispose(); // первая сборка компилирует Burst — в замер не идёт
            var a = AnatomyBuilder.Build();
            var sb = new StringBuilder();
            sb.Append("{\n  \"buildMs\": {");
            bool first = true;
            foreach (var kv in a.ms) { sb.Append(first ? "" : ", ").Append('"').Append(kv.Key).Append("\": ").Append(kv.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)); first = false; }
            sb.Append("},\n");
            foreach (var c in Cuts)
            {
                var r = AnatSlice.Render(a, c.o, c.u, c.v, c.w, c.h, 0.025f);
                File.WriteAllBytes(Path.Combine(OutDir, "slice-" + c.name + ".png"), AnatSlice.Png(r));
            }
            AppendStats(sb, a);
            sb.Append("}\n");
            File.WriteAllText(Path.Combine(OutDir, "stats.json"), sb.ToString());
            a.Dispose();
            Debug.Log("ANAT_SLICES_OK " + OutDir);
        }

        /// Свободный срез: -anatCut ox,oy,oz ux,uy,uz vx,vy,vz w h name
        public static void Cut()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-anatCut");
            float3 P(string s) { var p = s.Split(','); return new float3(F(p[0]), F(p[1]), F(p[2])); }
            var o = P(args[i + 1]); var u = P(args[i + 2]); var v = P(args[i + 3]);
            float w = F(args[i + 4]), h = F(args[i + 5]); string name = args[i + 6];
            Directory.CreateDirectory(OutDir);
            var a = AnatomyBuilder.Build();
            var r = AnatSlice.Render(a, o, u, v, w, h, 0.025f);
            File.WriteAllBytes(Path.Combine(OutDir, "slice-" + name + ".png"), AnatSlice.Png(r));
            a.Dispose();
            Debug.Log("ANAT_CUT_OK " + name);
        }

        /// Проверки строения в текстовый отчёт Shots/anatomy/checks.txt (лучи по зонам, корни в кости, глаз в глазнице).
        public static void Checks()
        {
            Directory.CreateDirectory(OutDir);
            var a = AnatomyBuilder.Build();
            File.WriteAllText(Path.Combine(OutDir, "checks.txt"), ChecksText(a));
            a.Dispose();
            Debug.Log("ANAT_CHECKS_OK");
        }

        const string NL = "\n";
        public static string ChecksText(AnatHead a)
        {
            var sb = new StringBuilder();
            foreach (var r in AnatChecks.Rays)
            {
                var res = AnatChecks.Check(a, r);
                sb.Append("== ").Append(r.zone).Append(res.ok ? " — ок" : " — РАСХОЖДЕНИЕ").Append(NL).Append(res.report).Append("  луч: ").Append(res.got).Append(NL).Append(NL);
            }
            var miss = new System.Collections.Generic.Dictionary<string, int>();
            var roots = AnatChecks.RootsInBone(a, out int withRoots, miss);
            sb.Append("== корни в кости: зубов с корнем ").Append(withRoots).Append(" из ").Append(a.teeth.Length).Append(NL);
            for (int i = 0; i < roots.Length; i++) sb.Append("  ").Append(a.toothDefs[i].fdi).Append(": ").Append((roots[i] * 100).ToString("0")).Append("%").Append(i % 8 == 7 ? NL : "");
            sb.Append("  вместо кости встретилось:"); foreach (var kv in miss) sb.Append(' ').Append(kv.Key).Append(" ").Append(kv.Value).Append(';'); sb.Append(NL);
            foreach (var e in new[] { a.eyeL, a.eyeR })
            {
                var r = AnatChecks.EyeInOrbit(a, e);
                sb.Append("== ").Append(e.name).Append(": ячеек глаза под Covered ").Append((r.wrapped * 100).ToString("0.0")).Append("%, кость глазницы от центра ")
                  .Append(r.minWall.ToString("0.00")).Append("–").Append(r.maxWall.ToString("0.00")).Append(" см, открытых направлений ").Append(r.openDirs).Append(" из 26").Append(NL);
            }
            return sb.ToString();
        }

        static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        public static void AppendStats(StringBuilder sb, AnatHead a)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int occupied = 0; long cells = 0;
            var perMat = new long[AnatMat.Count];
            foreach (var g in a.All)
            {
                cells += g.Count;
                for (int i = 0; i < g.Count; i++) { byte m = g.mat[i]; if (m < AnatMat.Count) perMat[m]++; if (AnatMat.Solid(m)) occupied++; }
            }
            sb.Append("  \"cells\": ").Append(cells).Append(",\n  \"occupied\": ").Append(occupied).Append(",\n");
            sb.Append("  \"headCells\": ").Append(a.head.Count).Append(",\n");
            int usedMats = 0;
            sb.Append("  \"materials\": {");
            bool first = true;
            for (int m = 1; m < AnatMat.Count; m++)
            {
                if (perMat[m] == 0) continue;
                usedMats++;
                sb.Append(first ? "" : ", ").Append('"').Append(AnatMat.Table[m].name).Append("\": ").Append(perMat[m]); first = false;
            }
            sb.Append("},\n  \"materialsUsed\": ").Append(usedMats).Append(",\n");
            var vol = SinusVolumes(a);
            sb.Append("  \"sinusMl\": {");
            first = true;
            foreach (var kv in vol) { sb.Append(first ? "" : ", ").Append('"').Append(kv.Key).Append("\": ").Append(kv.Value.ToString("0.00", inv)); first = false; }
            sb.Append("},\n");
        }

        /// Объём воздуха в пазухах по областям (мл = см³).
        public static System.Collections.Generic.Dictionary<string, double> SinusVolumes(AnatHead a)
        {
            var g = a.head; double cv = g.step * g.step * g.step;
            var d = new System.Collections.Generic.Dictionary<string, double> { ["frontal"] = 0, ["maxL"] = 0, ["maxR"] = 0, ["sphenoid"] = 0, ["ethmoid"] = 0, ["nasal"] = 0, ["other"] = 0 };
            for (int z = 0; z < g.dims.z; z++)
            for (int y = 0; y < g.dims.y; y++)
            for (int x = 0; x < g.dims.x; x++)
            {
                int i = g.Index(x, y, z);
                if (g.mat[i] != AnatMat.Air) continue;
                float3 p = g.Local(x, y, z); float ax = math.abs(p.x);
                string k;
                if (AnatGeo.FrontalSinus(ax, p.y, p.z) < 0) k = "frontal";
                else if (AnatGeo.MaxSinus(ax, p.y, p.z) < 0) k = p.x >= 0 ? "maxL" : "maxR";
                else if (AnatGeo.SphSinus(p.x, p.y, p.z) < 0) k = "sphenoid";
                else if (AnatGeo.EthmoidBox(ax, p.y, p.z, out _)) k = "ethmoid";
                else if (AnatGeo.Nasal(ax, p.y, p.z) < 0 || AnatGeo.Vestibule(ax, p.y, p.z) < 0) k = "nasal";
                else k = "other";
                d[k] += cv;
            }
            return d;
        }
    }
}
