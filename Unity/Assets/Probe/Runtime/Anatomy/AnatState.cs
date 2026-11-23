using Unity.Collections;
using Unity.Mathematics;

namespace VoxProbe
{
    /// Состояние вокселя (этап B), 12 байт. Времена — в единицах 2 с от начала боя (0..510 с).
    /// red — покраснение, rt — время последнего удара (и начала отёка); swell — отёк в 0,05 мм (цель, растёт в шейдере за секунды);
    /// bruise, bt — синяк и его «возраст»; abr — ссадина; blood, blt — кровь и её возраст; dmg — накопленный урон ×10;
    /// dx, dy, dz — смещение ячейки в 0,1 мм (края раны, позже отломки), читает построитель сетки.
    public struct VS
    {
        public byte red, rt, swell, bruise, bt, abr, blood, blt, dmg;
        public sbyte dx, dy, dz;
        public bool Zero => red == 0 && swell == 0 && bruise == 0 && abr == 0 && blood == 0 && dx == 0 && dy == 0 && dz == 0;
    }

    /// Разреженное хранилище: массив состояний 32³ заводится только для чанка, в котором что-то случилось.
    public class AnatState : System.IDisposable
    {
        public const int CS = AnatMesher.CS, CV = CS * CS * CS;
        public readonly AnatGrid g;
        public readonly int3 nch;
        public NativeArray<int> slot;     // номер чанка → начало в pool или −1
        public NativeList<VS> pool;
        public int Chunks { get; private set; }
        public long Bytes => (long)pool.Capacity * 12 + slot.Length * 4;

        public AnatState(AnatGrid g, int reserveChunks = 4)
        {
            this.g = g;
            nch = (g.dims + CS - 1) / CS;
            slot = new NativeArray<int>(nch.x * nch.y * nch.z, Allocator.Persistent);
            for (int i = 0; i < slot.Length; i++) slot[i] = -1;
            pool = new NativeList<VS>(CV * reserveChunks, Allocator.Persistent);
        }

        int Chunk(int x, int y, int z) => (x / CS) + nch.x * ((y / CS) + nch.y * (z / CS));
        static int Local(int x, int y, int z) => (x % CS) + CS * ((y % CS) + CS * (z % CS));

        /// Индекс в pool или −1 (если create — заводит чанк).
        public int Idx(int x, int y, int z, bool create)
        {
            int c = Chunk(x, y, z), s = slot[c];
            if (s < 0)
            {
                if (!create) return -1;
                s = pool.Length; pool.Resize(s + CV, NativeArrayOptions.ClearMemory); slot[c] = s; Chunks++;
            }
            return s + Local(x, y, z);
        }

        public VS Get(int x, int y, int z) { int k = Idx(x, y, z, false); return k < 0 ? default : pool[k]; }
        public void Set(int x, int y, int z, VS v) { pool[Idx(x, y, z, true)] = v; }

        public void Dispose() { if (slot.IsCreated) slot.Dispose(); if (pool.IsCreated) pool.Dispose(); }
    }

    /// Вид состояния для построителя сетки (в джобе). on = false — у тела состояний нет.
    public struct StateView
    {
        [ReadOnly] public NativeArray<VS> pool;
        [ReadOnly] public NativeArray<int> slot;
        public int3 nch;
        public bool on;
    }
}
