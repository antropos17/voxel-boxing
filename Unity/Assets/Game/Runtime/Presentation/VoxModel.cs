using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxProbe;

namespace VoxGame
{
    // Вспомогательный строитель: заполнять байтовую решётку примитивами (индексами палитры)
    // и строить из неё VoxBody. Индекс 0 — пусто, 1–15 — позиции в _VoxPalette материала.
    // Координаты всех примитивов — в пространстве вокселей (0..SX/SY/SZ).
    public class VoxModel
    {
        public readonly int SX, SY, SZ;
        readonly byte[] cells;

        public VoxModel(int sx, int sy, int sz)
        {
            SX = sx; SY = sy; SZ = sz;
            cells = new byte[sx * sy * sz];
        }

        // ---- Примитивы --------------------------------------------------

        public void Clear() => System.Array.Clear(cells, 0, cells.Length);

        public void Set(int x, int y, int z, byte mat)
        {
            if ((uint)x >= (uint)SX || (uint)y >= (uint)SY || (uint)z >= (uint)SZ) return;
            cells[x + SX * (y + SY * z)] = mat;
        }

        public void FillBox(int x0, int y0, int z0, int x1, int y1, int z1, byte mat)
        {
            x0 = Mathf.Max(x0, 0); y0 = Mathf.Max(y0, 0); z0 = Mathf.Max(z0, 0);
            x1 = Mathf.Min(x1, SX); y1 = Mathf.Min(y1, SY); z1 = Mathf.Min(z1, SZ);
            for (int z = z0; z < z1; z++)
            for (int y = y0; y < y1; y++)
            {
                int row = SX * (y + SY * z);
                for (int x = x0; x < x1; x++)
                    cells[x + row] = mat;
            }
        }

        // Эллипсоид: центр и радиусы в пространстве вокселей.
        public void FillEllipsoid(float cx, float cy, float cz, float rx, float ry, float rz, byte mat)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx - 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry - 1));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(cz - rz - 1));
            int x1 = Mathf.Min(SX, Mathf.CeilToInt(cx + rx + 1));
            int y1 = Mathf.Min(SY, Mathf.CeilToInt(cy + ry + 1));
            int z1 = Mathf.Min(SZ, Mathf.CeilToInt(cz + rz + 1));
            float irx2 = 1f / (rx * rx), iry2 = 1f / (ry * ry), irz2 = 1f / (rz * rz);
            for (int z = z0; z < z1; z++)
            for (int y = y0; y < y1; y++)
            {
                float dy = y - cy, dz = z - cz;
                float rem = 1f - dy * dy * iry2 - dz * dz * irz2;
                if (rem < 0) continue;
                float rx2i = rem / irx2; // max dx² allowed
                float xLo = cx - Mathf.Sqrt(rx2i), xHi = cx + Mathf.Sqrt(rx2i);
                int xa = Mathf.Max(x0, Mathf.CeilToInt(xLo));
                int xb = Mathf.Min(x1, Mathf.FloorToInt(xHi) + 1);
                int row = SX * (y + SY * z);
                for (int x = xa; x < xb; x++)
                    cells[x + row] = mat;
            }
        }

        // Капсула: концы A/B и радиус в пространстве вокселей.
        public void FillCapsule(float ax, float ay, float az, float bx, float by, float bz,
                                 float r, byte mat)
        {
            float lx = bx - ax, ly = by - ay, lz = bz - az;
            float lenSq = lx * lx + ly * ly + lz * lz;
            float r2 = r * r;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, bx) - r - 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, by) - r - 1));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(az, bz) - r - 1));
            int x1 = Mathf.Min(SX, Mathf.CeilToInt(Mathf.Max(ax, bx) + r + 1));
            int y1 = Mathf.Min(SY, Mathf.CeilToInt(Mathf.Max(ay, by) + r + 1));
            int z1 = Mathf.Min(SZ, Mathf.CeilToInt(Mathf.Max(az, bz) + r + 1));
            float invLen = lenSq > 1e-9f ? 1f / lenSq : 0f;
            for (int z = z0; z < z1; z++)
            for (int y = y0; y < y1; y++)
            {
                int row = SX * (y + SY * z);
                for (int x = x0; x < x1; x++)
                {
                    float dx = x - ax, dy = y - ay, dz = z - az;
                    float t = Mathf.Clamp01((dx * lx + dy * ly + dz * lz) * invLen);
                    float qx = dx - t * lx, qy = dy - t * ly, qz = dz - t * lz;
                    if (qx * qx + qy * qy + qz * qz <= r2)
                        cells[x + row] = mat;
                }
            }
        }

        // Скруглённый бокс: центр, полуразмеры и радиус скругления в пространстве вокселей.
        public void FillRoundBox(float cx, float cy, float cz,
                                  float hx, float hy, float hz, float r, byte mat)
        {
            float r2 = r * r;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - hx - r - 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - hy - r - 1));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(cz - hz - r - 1));
            int x1 = Mathf.Min(SX, Mathf.CeilToInt(cx + hx + r + 1));
            int y1 = Mathf.Min(SY, Mathf.CeilToInt(cy + hy + r + 1));
            int z1 = Mathf.Min(SZ, Mathf.CeilToInt(cz + hz + r + 1));
            for (int z = z0; z < z1; z++)
            for (int y = y0; y < y1; y++)
            {
                float dy = Mathf.Max(Mathf.Abs(y - cy) - hy, 0f);
                float dz = Mathf.Max(Mathf.Abs(z - cz) - hz, 0f);
                float edge = dy * dy + dz * dz;
                if (edge > r2) continue;
                int row = SX * (y + SY * z);
                for (int x = x0; x < x1; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x - cx) - hx, 0f);
                    if (dx * dx + edge <= r2)
                        cells[x + row] = mat;
                }
            }
        }

        // ---- Палитра ----------------------------------------------------

        // Задать _VoxPalette[16] на материал из массива Color (sRGB).
        public static void SetPalette(Material mat, Color[] palette)
        {
            var v = new Vector4[16];
            int n = Mathf.Min(palette.Length, 16);
            for (int i = 0; i < n; i++)
                v[i] = new Vector4(palette[i].r, palette[i].g, palette[i].b, 1f);
            mat.SetVectorArray("_VoxPalette", v);
        }

        // ---- Сборка VoxBody ---------------------------------------------

        // Передать решётку в VoxBody и синхронно построить меш.
        // После вызова этот VoxModel можно менять — VoxBody владеет своей копией NativeArray.
        // parent — Transform, к которому прикрепляются чанки (обычно смещённый дочерний GO).
        public VoxBody ToVoxBody(Transform parent, Material mat, float voxelSize)
        {
            var body = new VoxBody(parent, mat, voxelSize);
            int total = SX * SY * SZ;
            var na = new NativeArray<byte>(total, Allocator.Persistent,
                                           NativeArrayOptions.UninitializedMemory);
            na.CopyFrom(cells);
            body.SetGrid(na, new int3(SX, SY, SZ));
            body.visibleOnApply = true;
            // Синхронная сборка меша через MeshScheduler
            var sched = new MeshScheduler();
            var list = new List<VoxBody>(1) { body };
            sched.Schedule(list, 0.0);
            sched.Flush(0.0);
            sched.Dispose();
            return body;
        }

        // Версия для нескольких тел в одном проходе (экономит пересоздание планировщика).
        public static void BuildAll(List<(VoxBody body, VoxModel model)> pairs)
        {
            var sched = new MeshScheduler();
            var bodies = new List<VoxBody>(pairs.Count);
            foreach (var (body, model) in pairs)
            {
                int total = model.SX * model.SY * model.SZ;
                var na = new NativeArray<byte>(total, Allocator.Persistent,
                                               NativeArrayOptions.UninitializedMemory);
                na.CopyFrom(model.cells);
                body.SetGrid(na, new int3(model.SX, model.SY, model.SZ));
                body.visibleOnApply = true;
                bodies.Add(body);
            }
            sched.Schedule(bodies, 0.0);
            sched.Flush(0.0);
            sched.Dispose();
        }
    }
}
