using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxProbe
{
    /// Часть 7: предварительный отбор ячеек головы для удара (Burst). Отбор — только надмножество: геометрия считается с запасом,
    /// управляемый код потом проверяет каждую отобранную ячейку прежними выражениями (с синусами, Protected, случайными числами)
    /// в прежнем порядке z, y, x. Материал и «открыта наружу» (Exposed) — целые проверки, совпадают точно.
    public static class AnatScan
    {
        public const byte FSurf = 1, FSoft = 2, FCompact = 4, FCullC = 8, FFrac = 16, FDuraCsf = 32, FCull = 64;
        static NativeArray<byte> flags;
        /// Флаги материалов (как Surface, Soft, … в AnatDamage).
        public static NativeArray<byte> Flags(System.Func<byte, bool> surf, System.Func<byte, bool> soft)
        {
            if (flags.IsCreated) return flags;
            flags = new NativeArray<byte>(256, Allocator.Persistent);
            for (int m = 0; m < 256; m++)
            {
                byte b = (byte)m; int f = 0;
                if (surf(b)) f |= FSurf;
                if (soft(b)) f |= FSoft;
                if (b == AnatMat.Compact) f |= FCompact;
                if (AnatMat.Cull(b) && b != AnatMat.Covered) f |= FCullC;
                if (AnatMat.Cull(b) && b != AnatMat.Covered && !AnatDamage.Brainy(b)) f |= FFrac;
                if (b == AnatMat.Dura || b == AnatMat.Csf) f |= FDuraCsf;
                if (AnatMat.Cull(b)) f |= FCull;
                flags[m] = (byte)f;
            }
            return flags;
        }
        public static void Dispose() { if (flags.IsCreated) flags.Dispose(); }
    }

    /// Режимы: 0 — шар (|p − c|² ≤ r2 с запасом), материал из need, при exposed — открытая наружу;
    /// 1 — разрыв (|p − c| ≤ r с запасом), материал из need; 2 — перелом (кольцо вокруг оси l от B, см. AnatDamage.Fracture).
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    public struct AnatScanJob : IJob
    {
        [ReadOnly] public NativeArray<byte> mat, vis, flags;
        public bool hasVis, exposed;
        public int3 dims, lo, hi;
        public float3 org; public float S;
        public int mode; public byte need;
        public float3 c, l; public float r2, r, Rf;
        public NativeList<int> outI;

        bool OpenAt(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= dims.x || y >= dims.y || z >= dims.z) return true;
            int i = x + dims.x * (y + dims.y * z); byte m = mat[i];
            return m == AnatMat.Empty || (m == AnatMat.Air && (!hasVis || vis[i] <= AnatVis.Lim));
        }

        public void Execute()
        {
            outI.Clear();
            const float e = 1e-3f;
            float r2m = r2 * 1.0001f + 1e-4f, rm = r * 1.0001f + 1e-4f;
            float aLo = -2.6f - e, aHi = 3.5f + e, aRing = 1.6f + e, radHi = Rf * 1.25f + 0.2f + e, radLo = Rf * 0.79f - 0.17f - e;
            for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++)
            {
                int rowi = dims.x * (y + dims.y * z);
                for (int x = lo.x; x <= hi.x; x++)
                {
                    int i = rowi + x; byte m = mat[i];
                    if ((flags[m] & need) == 0) continue;
                    float3 p = org + (new float3(x, y, z) + 0.5f) * S, d = p - c;
                    if (mode == 0)
                    {
                        if (math.lengthsq(d) > r2m) continue;
                        if (exposed && !(OpenAt(x + 1, y, z) || OpenAt(x - 1, y, z) || OpenAt(x, y + 1, z) || OpenAt(x, y - 1, z) || OpenAt(x, y, z + 1) || OpenAt(x, y, z - 1))) continue;
                    }
                    else if (mode == 1) { if (math.length(d) > rm) continue; }
                    else
                    {
                        float al = math.dot(d, l); if (al < aLo || al > aHi) continue;
                        bool soft = m == AnatMat.Dura || m == AnatMat.Csf; if (al >= aRing && !soft) continue;
                        float rad = math.length(d - l * al); if (rad > radHi) continue;
                        if (!soft && rad < radLo) continue;   // кольцо не уже Rf·0,79 (1 − 0,14 − 0,07): внутри — только оболочка и ликвор
                    }
                    outI.Add(i);
                }
            }
        }
    }
}
