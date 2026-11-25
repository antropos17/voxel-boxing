using UnityEngine;

namespace VoxGame
{
    public enum FightPhase { Idle, Guard, Windup, Strike, Recover, Open, Stagger, Down, Rise, KO }
    public enum AnatZone  { Skull = 0, Cheek = 1, Jaw = 2, Mouth = 3, Nose = 4, Orbit = 5, Ear = 6, Count = 7 }

    // Множители зон (HEAD-SPEC §8, нельзя менять)
    // Skull=1.2, Cheek=1.15, Jaw=1.6, Mouth=1.5, Nose=1.0, Orbit=0.9, Ear=1.25
    public static class ZoneMults
    {
        public static readonly float[] Values = { 1.2f, 1.15f, 1.6f, 1.5f, 1.0f, 0.9f, 1.25f };
    }

    // Параметры перчаток/улучшений — приходят снаружи, FightState от них не зависит
    public struct FightParams
    {
        public float dmgMult;           // перчатки + Power upgrade
        public float staminaCostMult;   // перчатки + Stamina upgrade (обратный)
        public float megaGainMult;      // перчатки + MegaCharge upgrade
        public float defenseMult;       // входящий урон по игроку
        public float speedMult;         // не используется FightState, нужен FightDirector

        public static FightParams Default => new FightParams
        {
            dmgMult         = 1f,
            staminaCostMult = 1f,
            megaGainMult    = 1f,
            defenseMult     = 1f,
            speedMult       = 1f,
        };
    }

    public class FightState
    {
        // ---- Соперник ----
        public float      hp;
        public float      hpMax;
        public float      stunAccum;
        public float      stunMax;
        public int        knockdowns;
        public float      groggy;        // секунды после нокдауна
        public float      megaMeter;     // 0–100, НЕ сбрасывается между боями
        public bool       megaReady;     // true при megaMeter >= 100
        public FightPhase phase;

        // ---- Игрок ----
        public float playerHp;           // 0–100
        public float playerStamina;      // 0–100
        public bool  playerBlocking;
        public float playerDodge;        // убывает до 0 за DODGE_DUR сек
        public int   playerDodgeSide;    // -1 / +1

        // ---- Параметры боя (множители перчаток/улучшений) ----
        public FightParams fightParams;

        // ---- Комбо-счётчик игрока (для мега-шкалы) ----
        public int   comboCount;
        public float lastHitTime;        // игровое время последнего попадания
        public float idleTimer;          // время без удара (для штрафа мега)

        // Константы
        public const float STAMINA_REGEN_FIGHT = 19f;   // /с в бою
        public const float STAMINA_REGEN_BLOCK = 9f;    // /с в блоке
        public const float STAMINA_HIT_COST    = 12f;   // стоимость удара
        public const float DODGE_DUR           = 0.42f;
        public const float GROGGY_DUR          = 3.5f;
        public const float IDLE_MEGA_PENALTY   = 4f;    // /с при простое > 2.5 с
        public const float IDLE_THRESHOLD      = 2.5f;

        public FightState() { }

        public FightState(int level)
        {
            hpMax     = 220f + 50f * (level - 1);
            hp        = hpMax;
            stunMax   = 28f + level * 2.5f;
            stunAccum = 0f;
            knockdowns = 0;
            groggy    = 0f;
            // megaMeter не сбрасывается — если создаётся новый FightState между боями,
            // вызывающий должен скопировать megaMeter из предыдущего.
            megaMeter = 0f;
            megaReady = false;
            phase     = FightPhase.Idle;

            playerHp       = 100f;
            playerStamina  = 100f;
            playerBlocking = false;
            playerDodge    = 0f;
            playerDodgeSide = 0;
            fightParams    = FightParams.Default;
            comboCount     = 0;
            lastHitTime    = 0f;
            idleTimer      = 0f;
        }

        // Применить удар по сопернику. counterHit/openHit — контекст из FightSim.
        // Возвращает частичный HitResult (dmgDealt); остальные поля заполняет FightSim.
        public HitResult TakeHit(int removed, AnatZone zone, int strength, bool isMega, float pw,
                                  bool counterHit = false, bool openHit = false)
        {
            bool enoughStamina = playerStamina >= STAMINA_HIT_COST * fightParams.staminaCostMult;

            // Расход выносливости
            if (enoughStamina)
                playerStamina = Mathf.Max(0f, playerStamina - STAMINA_HIT_COST * fightParams.staminaCostMult);

            // Расчёт урона HP
            float dmg = removed * 0.18f * ZoneMults.Values[(int)zone];
            dmg *= strength == 2 ? 1.5f : strength == 1 ? 1.2f : 1.0f;
            if (openHit)       dmg *= 1.6f;
            if (counterHit)    dmg *= 1.3f;
            if (isMega)        dmg *= 2.4f;
            if (!enoughStamina) dmg *= 0.6f;
            dmg *= fightParams.dmgMult;

            hp = Mathf.Max(0f, hp - dmg);

            // Оглушение (1 ячейка = 0.08 stun; сила масштабирует)
            float stunDmg = removed * 0.08f * (strength == 2 ? 1.5f : strength == 1 ? 1.2f : 1f);
            if (isMega) stunDmg *= 2f;
            stunAccum += stunDmg;

            // Мега-шкала
            _UpdateMegaOnHit(strength, isMega, counterHit, openHit);

            return new HitResult { hit = true, dmgDealt = dmg };
        }

        // Восстановление за кадр
        public void TickRecovery(float dt)
        {
            // Выносливость
            float regen = playerBlocking ? STAMINA_REGEN_BLOCK : STAMINA_REGEN_FIGHT;
            playerStamina = Mathf.Min(100f, playerStamina + regen * dt);

            // Уклон убывает
            if (playerDodge > 0f)
            {
                playerDodge = Mathf.Max(0f, playerDodge - dt / DODGE_DUR);
                if (playerDodge <= 0f) playerDodgeSide = 0;
            }

            // Groggy убывает
            if (groggy > 0f) groggy = Mathf.Max(0f, groggy - dt);

            // Мега idle штраф
            idleTimer += dt;
            if (idleTimer > IDLE_THRESHOLD && !megaReady)
                megaMeter = Mathf.Max(0f, megaMeter - IDLE_MEGA_PENALTY * dt);

            // Мега статус
            megaReady = megaMeter >= 100f;
        }

        // Нокдаун: stun >= stunMax И kd < 2
        public void OnKnockdown()
        {
            knockdowns++;
            stunAccum = 0f;
            groggy    = GROGGY_DUR;
            phase     = FightPhase.Down;
        }

        // KO: hp<=0 ИЛИ (stun>=stunMax И kd>=2) ИЛИ (mega И hp < 45% И stun >= 60% stunMax)
        public bool CheckKO(bool isMega)
        {
            if (hp <= 0f) return true;
            if (stunAccum >= stunMax && knockdowns >= 2) return true;
            if (isMega && hp < hpMax * 0.45f && stunAccum >= stunMax * 0.6f) return true;
            return false;
        }

        // Мега-шкала при попадании игрока.
        // counterHit/openHit: множитель ×1.5 применяется к рассчитанному gain, не ко всему meter.
        void _UpdateMegaOnHit(int strength, bool isMega, bool counterHit, bool openHit)
        {
            if (megaReady) return; // не растёт когда заполнена
            idleTimer = 0f;        // сбросить таймер простоя

            float gain = strength == 0 ? 5f : 11f; // джеб +5, пауэр +11
            // комбо-бонус +(combo-1) до 6
            int comboBon = Mathf.Min(comboCount, 6);
            if (comboBon > 0) gain += comboBon;
            // контр/встречный: ×1.5 на текущий gain (contracts.md «контр/встречный: ×1.5»)
            if (counterHit || openHit) gain *= 1.5f;

            megaMeter = Mathf.Min(100f, megaMeter + gain * fightParams.megaGainMult);
            megaReady = megaMeter >= 100f;
        }

        // Получил удар сам игрок — мега −18
        public void OnPlayerHurt()
        {
            megaMeter = Mathf.Max(0f, megaMeter - 18f);
            megaReady = megaMeter >= 100f;
        }

        // Удар игрока заблокирован соперником — мега +2
        public void OnHitBlocked()
        {
            if (megaReady) return;
            megaMeter = Mathf.Min(100f, megaMeter + 2f * fightParams.megaGainMult);
            megaReady = megaMeter >= 100f;
        }
    }
}
