// HudData.cs — снимок состояния боя для FightHUD.
// FightDirector заполняет это каждый кадр; FightHUD читает только HudData, не FightState.
namespace VoxGame {

    public struct HudData {
        public float playerHp;         // 0..playerHpMax
        public float playerHpMax;      // обычно 100
        public float foeHp;            // 0..foeHpMax
        public float foeHpMax;
        public float playerStamina;    // 0..100
        public float megaMeter;        // 0..100
        public bool  megaReady;
        public float fightTime;        // секунды с начала боя (растёт вверх)
        public int   foeKnockdowns;    // 0..2
        public string foeName;
        public string playerName;
        public FightPhase foePhase;    // для подсказки телеграфа (Windup)
        public int    foeAttackSide;   // -1 / +1 (сторона атаки соперника)
        public float  telegraphAlpha;  // 0..1 (прозрачность «!» по прогрессу замаха)
        public bool   playerBlocking;
        public int    combo;           // текущее комбо игрока (0 = нет)
        public float  foeStun01;       // 0..1: оглушение соперника (полное — нокдаун)
        public float  staminaHitCost;  // цена удара в единицах выносливости (деления зарядов); 0 — по умолчанию
    }
}
