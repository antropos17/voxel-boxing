using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static VoxProbe.AnatGeo;

namespace VoxProbe
{
    /// Причёска анатомической головы (Unity/FACES.md). Short — прежний слой в одну ячейку поверх кожи (вид по умолчанию).
    public static class AnatHair
    {
        public const byte Short = 0, Bald = 1, Buzz = 2, Mohawk = 3, Full = 4, Count = 5;
        public static readonly string[] Names = { "короткая", "лысый", "ёжик", "ирокез", "объёмные" };
    }

    /// Внешность соперника: тон кожи, причёска, цвет волос. default(AnatLook) — прежний вид побитно (цвета таблицы AnatMat,
    /// короткие волосы): a = 0 у цвета — «как в таблице».
    public struct AnatLook
    {
        public byte hair;
        public Color32 skin, hairCol, browCol;   // browCol.a = 0 — брови в цвет волос (темнее)

        /// Тона кожи от светлого до тёмного; [1] — тон таблицы AnatMat (прежний).
        public static readonly Color32[] Tones =
        {
            new Color32(240, 204, 178, 255),   // светлый
            new Color32(222, 168, 128, 255),   // прежний
            new Color32(188, 132,  90, 255),   // смуглый
            new Color32(140,  92,  60, 255),   // коричневый
            new Color32( 86,  56,  40, 255),   // тёмный
        };
        public static readonly string[] ToneNames = { "светлый", "прежний", "смуглый", "коричневый", "тёмный" };

        public bool IsDefault => hair == AnatHair.Short && skin.a == 0 && hairCol.a == 0 && browCol.a == 0;
    }

    /// Цвета внешности в палитру шейдера и воксельные причёски.
    public static class AnatLooks
    {
        /// Поверхность материала с учётом внешности: кожа, щетина, губа — от тона; волосы, брови — от цвета волос.
        /// Подкожные ткани, кровь, кости и цвета на срезе не меняются.
        public static Color32 Surf(int m, in AnatLook look)
        {
            var d = AnatMat.Table[m].surf;
            if (look.skin.a != 0)
            {
                var s0 = AnatMat.Table[AnatMat.Skin].surf; var t = look.skin;
                if (m == AnatMat.Skin || m == AnatMat.Stubble) return t;
                if (m == AnatMat.Lip) return new Color32(Sc(d.r, t.r, s0.r), Sc(d.g, t.g, s0.g), Sc(d.b, t.b, s0.b), 255);
            }
            if (look.hairCol.a != 0)
            {
                var c = look.hairCol;
                if (m == AnatMat.Hair) return c;
                if (m == AnatMat.Brow) return look.browCol.a != 0 ? look.browCol : new Color32((byte)(c.r * 0.8f), (byte)(c.g * 0.8f), (byte)(c.b * 0.8f), 255);
            }
            return d;
        }
        static byte Sc(byte v, byte t, byte s0) => (byte)math.min(255, v * t / math.max(1, (int)s0));

        /// Сколько ячеек над кожей может занять причёска (запас для поиска висящих волос после удара); 0 — поиск не нужен.
        /// Короткая (вид по умолчанию) — 0: слой в одну ячейку повреждения снимают вместе с кожей, а seed 77 остаётся побитно прежним.
        public static int Layers(byte hair) => hair == AnatHair.Mohawk ? 12 : hair == AnatHair.Full ? 9 : hair == AnatHair.Buzz ? 1 : 0;

        /// Причёски, кроме короткой (её строит HairJob, как раньше): слой волос над кожей толщиной по стилю, затем снять
        /// ячейки без опоры (Settle). Решётка головы кончается в 2 см над макушкой и в 0,6 см за затылком — толще не будет.
        public static int LastGrowCut;   // снято без опоры при последней сборке (для отчёта)
        public static void Grow(AnatGrid h, byte hair)
        {
            if (hair == AnatHair.Short || hair == AnatHair.Bald) return;
            new GrowJob { mat = h.mat, zone = h.zone, dims = h.dims, org = h.org, s = h.step, style = hair }.Schedule(h.dims.z, 1).Complete();
            var cut = new List<int>();
            Settle(h, int3.zero, h.dims - 1, cut);
            foreach (var i in cut) { h.mat[i] = AnatMat.Empty; h.zone[i] = 0; }
            LastGrowCut = cut.Count;
        }

        [BurstCompile(CompileSynchronously = true)]
        struct GrowJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<byte> mat, zone;
            public int3 dims; public float3 org; public float s; public byte style;
            public void Execute(int z)
            {
                int W = dims.x, H = dims.y, D = dims.z;
                if (z == 0 || z == D - 1) return;
                float Z = org.z + (z + 0.5f) * s;
                for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    int i = x + W * (y + H * z);
                    if (mat[i] != 0) continue;
                    float X = org.x + (x + 0.5f) * s, Y = org.y + (y + 0.5f) * s;
                    bool on;
                    if (style == AnatHair.Buzz)
                    {
                        if (!HairZone(X, Y + 0.1f, Z) || CellHash(new int3(x, y, z)) > 0.6f) continue;
                        bool skin = false;
                        for (int d = 0; d < 6 && !skin; d++)
                        {
                            int j = i + (d == 0 ? 1 : d == 1 ? -1 : d == 2 ? W : d == 3 ? -W : d == 4 ? W * H : -W * H);
                            skin = mat[j] == AnatMat.Skin;
                        }
                        on = skin;
                    }
                    else
                    {
                        float b = Body(X, Y, Z); if (b <= -0.5f || b > 3.2f) continue;   // кожа решётки местами ниже нуля поля Body — без зазора
                        // корень на коже под ячейкой (по нормали): зона и толщина — по нему, столбик волос однороден, без навесов
                        const float e = 0.05f;
                        float3 n = math.normalizesafe(new float3(Body(X + e, Y, Z) - Body(X - e, Y, Z), Body(X, Y + e, Z) - Body(X, Y - e, Z), Body(X, Y, Z + e) - Body(X, Y, Z - e)));
                        float3 q = new float3(X, Y, Z) - n * b;
                        if (style == AnatHair.Mohawk) on = math.abs(q.x) < 1.4f && HairZone(q.x, q.y + 0.3f, q.z) && b < MohawkTop(q.z);
                        else
                        {
                            float th = math.lerp(1.6f, 0.9f, Sstep(3f, 7f, q.z)) * (0.85f + 0.3f * CellHash((int3)math.floor(q / 1.2f)));
                            on = HairZone(q.x, q.y + 0.4f, q.z) && b < th;
                        }
                    }
                    if (!on) continue;
                    mat[i] = AnatMat.Hair; zone[i] = ZoneAt(X, Y, Z);
                }
            }
        }

        /// Гребень ирокеза: ниже у лба и на затылке, зубцами (как HeadGear игрового слоя).
        static float MohawkTop(float z)
        {
            float h = 3.0f * math.lerp(0.55f, 1f, Sstep(8f, 3f, z)) * math.lerp(0.6f, 1f, Sstep(-11f, -5f, z));
            return h * (0.78f + 0.22f * math.abs(math.sin(z * 0.9f)));
        }

        /// Опора волос: ячейка волос держится, если рядом (6 соседей) твёрдая ткань не-волос или удержанная ячейка волос заметно ближе
        /// к коже (высота над кожей — Body, квант 0,02 см, ближе хотя бы на Step квантов). Ячейки обходятся по возрастанию высоты,
        /// поэтому волосы над вырванной кожей уходят целиком, а не висят. Коробка [mn, mx] в ячейках головы; снятые — в cut.
        public const int Step = 3;
        public static void Settle(AnatGrid h, int3 mn, int3 mx, List<int> cut)
        {
            mn = math.max(mn, 1); mx = math.min(mx, h.dims - 2);
            if (math.any(mn > mx)) return;
            var sup = new NativeArray<byte>(AnatMat.Count, Allocator.TempJob);
            for (int m = 0; m < AnatMat.Count; m++)
                sup[m] = (byte)(m != AnatMat.Hair && m != AnatMat.Brow && m != AnatMat.Lash && AnatIslands.Links((byte)m) ? 1 : 0);
            var bd = mx - mn + 1;
            var lay = new NativeArray<byte>(bd.x * bd.y * bd.z, Allocator.TempJob);
            var outl = new NativeList<int>(256, Allocator.TempJob);
            new SettleJob { mat = h.mat, sup = sup, lay = lay, cut = outl, dims = h.dims, org = h.org, s = h.step, mn = mn, bd = bd }.Run();
            for (int k = 0; k < outl.Length; k++) cut.Add(outl[k]);
            sup.Dispose(); lay.Dispose(); outl.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct SettleJob : IJob
        {
            [ReadOnly] public NativeArray<byte> mat, sup;
            public NativeArray<byte> lay;      // по коробке: 0 — не волосы, 1 + высота (квант 0,02 см) — волосы, 255 — снята
            public NativeList<int> cut;
            public int3 dims, mn, bd; public float3 org; public float s;
            static int Q(float b) => 1 + (int)math.round(math.clamp(b, 0, 3.2f) / 0.02f);
            public void Execute()
            {
                int W = dims.x, H = dims.y, n = 0;
                var cnt = new NativeArray<int>(163, Allocator.Temp);
                for (int z = 0; z < bd.z; z++) for (int y = 0; y < bd.y; y++) for (int x = 0; x < bd.x; x++)
                {
                    int3 c = mn + new int3(x, y, z); int i = c.x + W * (c.y + H * c.z);
                    if (mat[i] != AnatMat.Hair) continue;
                    float3 p = org + ((float3)c + 0.5f) * s;
                    int v = Q(Body(p.x, p.y, p.z));
                    lay[x + bd.x * (y + bd.y * z)] = (byte)v; cnt[v + 1]++; n++;
                }
                for (int v = 1; v < 163; v++) cnt[v] += cnt[v - 1];
                var ord = new NativeArray<int>(math.max(1, n), Allocator.Temp);
                for (int b = 0; b < lay.Length; b++) { int v = lay[b]; if (v != 0) ord[cnt[v]++] = b; }
                for (int k = 0; k < n; k++)
                {
                    int b = ord[k], v = lay[b];
                    int x = b % bd.x, y = (b / bd.x) % bd.y, z = b / (bd.x * bd.y);
                    int3 c = mn + new int3(x, y, z); int i = c.x + W * (c.y + H * c.z);
                    bool ok = false;
                    for (int d = 0; d < 6 && !ok; d++)
                    {
                        int3 o = d == 0 ? new int3(1, 0, 0) : d == 1 ? new int3(-1, 0, 0) : d == 2 ? new int3(0, 1, 0) : d == 3 ? new int3(0, -1, 0) : d == 4 ? new int3(0, 0, 1) : new int3(0, 0, -1);
                        int3 q = c + o; int j = q.x + W * (q.y + H * q.z);
                        byte m = mat[j];
                        if (m != AnatMat.Hair) { ok = sup[m] != 0; continue; }
                        int3 qb = q - mn;
                        if (math.any(qb < 0) || math.any(qb >= bd))   // за коробкой — не трогали: держит, если ближе к коже
                        { float3 pq = org + ((float3)q + 0.5f) * s; ok = Q(Body(pq.x, pq.y, pq.z)) <= v - Step; continue; }
                        int l = lay[qb.x + bd.x * (qb.y + bd.y * qb.z)];
                        ok = l != 255 && l <= v - Step;
                    }
                    if (!ok) { lay[b] = 255; cut.Add(i); }
                }
                cnt.Dispose(); ord.Dispose();
            }
        }
    }
}
