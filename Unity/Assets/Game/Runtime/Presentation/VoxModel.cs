using System.Collections.Generic;
using UnityEngine;

namespace VoxGame
{
    // Статическая воксельная модель игрового слоя (тело соперника, руки, перчатки).
    // Решётка байтов (0 — пусто, 1..N — номер цвета в палитре) заполняется по функции расстояния в сантиметрах,
    // затем один раз строится обычная сетка Unity: грань на каждую открытую сторону ячейки, цвет в вершинах,
    // нормаль — смесь нормали грани и сглаженной нормали поверхности (объём читается, грани вокселей видны).
    // Такие тела не разрушаются, поэтому решётка после сборки сетки не хранится.
    public class VoxModel
    {
        public delegate float Sdf(float x, float y, float z);   // расстояние до поверхности, см (< 0 — внутри)
        public delegate byte Paint(float x, float y, float z);  // номер цвета ячейки у поверхности

        public readonly int SX, SY, SZ;
        readonly byte[] cells;

        public VoxModel(int sx, int sy, int sz)
        {
            SX = sx; SY = sy; SZ = sz;
            cells = new byte[sx * sy * sz];
        }

        // Модель по рамке в сантиметрах: mn..mx, шаг cell. Ячейка (i,j,k) имеет центр mn + (i+0.5, j+0.5, k+0.5)·cell.
        public static VoxModel FromSdf(Vector3 mn, Vector3 mx, float cell, Sdf sdf, Paint paint)
        {
            var m = new VoxModel(Mathf.CeilToInt((mx.x - mn.x) / cell), Mathf.CeilToInt((mx.y - mn.y) / cell),
                                 Mathf.CeilToInt((mx.z - mn.z) / cell));
            m.Fill(mn, cell, sdf, paint);
            return m;
        }

        // Заполнить по функции расстояния. Вдоль строки ячейки, далёкие от поверхности, пропускаются без вычисления
        // (функции расстояния здесь приближённые, поэтому шаг пропуска берётся с запасом).
        void Fill(Vector3 mn, float cell, Sdf sdf, Paint paint)
        {
            float inv = 0.55f / cell;
            for (int z = 0; z < SZ; z++)
            for (int y = 0; y < SY; y++)
            {
                float py = mn.y + (y + 0.5f) * cell, pz = mn.z + (z + 0.5f) * cell;
                int row = SX * (y + SY * z);
                for (int x = 0; x < SX;)
                {
                    float px = mn.x + (x + 0.5f) * cell;
                    float d = sdf(px, py, pz);
                    int skip = Mathf.Max(1, (int)(Mathf.Abs(d) * inv));
                    if (d <= 0f)
                    {
                        // глубоко внутри цвет не нужен: грани есть только у ячеек рядом с пустотой
                        byte c = skip > 1 ? (byte)1 : paint(px, py, pz);
                        int x1 = Mathf.Min(SX, x + skip);
                        for (int i = x; i < x1; i++) cells[row + i] = c == 0 ? (byte)1 : c;
                    }
                    x += skip;
                }
            }
        }

        bool Solid(int x, int y, int z) =>
            (uint)x < (uint)SX && (uint)y < (uint)SY && (uint)z < (uint)SZ && cells[x + SX * (y + SY * z)] != 0;

        static readonly Vector3[] FaceN =
        {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0),
            new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1),
        };

        // Четыре угла грани (в долях ячейки), обход по часовой стрелке, если смотреть снаружи.
        static readonly Vector3[,] FaceV =
        {
            { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) },
            { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) },
            { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) },
            { new Vector3(0, 0, 1), new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1) },
            { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) },
            { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) },
        };

        // Построить сетку. org — положение угла ячейки (0,0,0) в метрах в осях объекта, cellM — шаг в метрах.
        // faceBlend: 0 — гладкие нормали, 1 — нормали граней. aoScale — сила затенения складок по номеру цвета
        // (ткань гладкая: на светлых трусах ступенчатое затенение рисует полосы-горизонтали).
        public Mesh BuildMesh(string name, Color32[] palette, Vector3 org, float cellM, float faceBlend = 0.3f,
                              float[] aoScale = null)
        {
            var vs = new List<Vector3>(8192);
            var ns = new List<Vector3>(8192);
            var cs = new List<Color32>(8192);
            var ts = new List<int>(12288);

            for (int z = 0; z < SZ; z++)
            for (int y = 0; y < SY; y++)
            for (int x = 0; x < SX; x++)
            {
                byte m = cells[x + SX * (y + SY * z)];
                if (m == 0) continue;
                bool px = !Solid(x + 1, y, z), nx = !Solid(x - 1, y, z), py = !Solid(x, y + 1, z),
                     ny = !Solid(x, y - 1, z), pz = !Solid(x, y, z + 1), nz = !Solid(x, y, z - 1);
                if (!(px || nx || py || ny || pz || nz)) continue;

                // сглаженная нормаль и «вогнутость» по занятости окрестности 5×5×5
                Vector3 g = Vector3.zero; int filled = 0;
                for (int dz = -2; dz <= 2; dz++)
                for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    if (Solid(x + dx, y + dy, z + dz)) { g.x -= dx; g.y -= dy; g.z -= dz; filled++; }
                Vector3 sn = g.sqrMagnitude > 1e-6f ? g.normalized : Vector3.up;
                // плоская поверхность ≈ 0.6 занятых, складка больше, выступ меньше
                float aoK = aoScale != null && m < aoScale.Length ? aoScale[m] : 1f;
                float ao = Mathf.Clamp(1f - 1.5f * aoK * (filled / 125f - 0.6f), 0.58f, 1.08f);
                // лёгкий разброс тона по ячейке, как у кожи головы
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                float k = ao * (0.975f + 0.05f * ((h & 0xFFFF) / 65535f));
                Color32 pc = palette[Mathf.Min(m, palette.Length - 1)];
                var col = new Color32((byte)Mathf.Min(255f, pc.r * k), (byte)Mathf.Min(255f, pc.g * k),
                                      (byte)Mathf.Min(255f, pc.b * k), 255);

                var basePos = new Vector3(x, y, z);
                for (int f = 0; f < 6; f++)
                {
                    bool open = f == 0 ? px : f == 1 ? nx : f == 2 ? py : f == 3 ? ny : f == 4 ? pz : nz;
                    if (!open) continue;
                    Vector3 n = Vector3.Lerp(sn, FaceN[f], faceBlend).normalized;
                    int b = vs.Count;
                    for (int c = 0; c < 4; c++)
                    {
                        vs.Add(org + (basePos + FaceV[f, c]) * cellM);
                        ns.Add(n); cs.Add(col);
                    }
                    ts.Add(b); ts.Add(b + 1); ts.Add(b + 2);
                    ts.Add(b); ts.Add(b + 2); ts.Add(b + 3);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.indexFormat = vs.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32
                                                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vs);
            mesh.SetNormals(ns);
            mesh.SetColors(cs);
            mesh.SetTriangles(ts, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        // ---- Функции расстояния (как sdEll / sdCap / smin веб-версии, см) ----------------------------

        public static float Ell(float px, float py, float pz, float rx, float ry, float rz)
        {
            float a = px / rx, b = py / ry, c = pz / rz;
            float k0 = Mathf.Sqrt(a * a + b * b + c * c);
            a /= rx; b /= ry; c /= rz;
            float k1 = Mathf.Sqrt(a * a + b * b + c * c);
            return k1 < 1e-9f ? -Mathf.Min(rx, Mathf.Min(ry, rz)) : k0 * (k0 - 1f) / k1;
        }

        // Капсула от A к B, радиус r у A и r2 у B.
        public static float Cap(float px, float py, float pz, float ax, float ay, float az,
                                float bx, float by, float bz, float r, float r2)
        {
            float pax = px - ax, pay = py - ay, paz = pz - az, bax = bx - ax, bay = by - ay, baz = bz - az;
            float h = Mathf.Clamp01((pax * bax + pay * bay + paz * baz) / (bax * bax + bay * bay + baz * baz));
            pax -= bax * h; pay -= bay * h; paz -= baz * h;
            return Mathf.Sqrt(pax * pax + pay * pay + paz * paz) - (r + (r2 - r) * h);
        }

        public static float Cap(float px, float py, float pz, float ax, float ay, float az,
                                float bx, float by, float bz, float r) =>
            Cap(px, py, pz, ax, ay, az, bx, by, bz, r, r);

        // Плавное объединение.
        public static float Smin(float a, float b, float k)
        {
            float h = k - Mathf.Abs(a - b);
            return h > 0f ? Mathf.Min(a, b) - h * h * 0.25f / k : Mathf.Min(a, b);
        }

        // ---- Объект с сеткой ---------------------------------------------------------------------

        public static Transform Spawn(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }
    }
}
