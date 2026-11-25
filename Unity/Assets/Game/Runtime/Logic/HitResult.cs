namespace VoxGame
{
    public struct HitResult
    {
        public bool  hit;             // попал или промах
        public int   removed;         // ячейки
        public int   zoneIdx;         // AnatZone (int чтобы не тащить enum)
        public float traumaAll;
        public int   detachedBodies;
        public int   strength;        // 0=джеб, 1=пауэр, 2=мега
        public bool  isKnockdown;
        public bool  isKO;
        public bool  isMega;
        public float pw;
        public bool  headBurst;       // traumaAll > 42
        public bool  fracture;        // новый перелом
        public float dmgDealt;        // HP снятые этим ударом
        public bool  counterHit;      // встречный удар
        public bool  openHit;         // foe был в Open
    }
}
