using UnityEngine;

namespace VoxGame
{
    // Внешний вид головы — причёска, борода, аксессуар — одним enum.
    // Сводка для старого контракта; настоящий вид головы — FighterDef.head (HeadLook).
    public enum HeadStyle
    {
        Bald       = 0,  // лысый
        Buzz       = 1,  // ёжик
        Mohawk     = 2,  // ирокез
        FullHair   = 3,  // шапка волос
        Bandana    = 4,  // повязка
        Helmet     = 5,  // шлем
        Mouthguard = 6,  // капа
        Beard      = 7   // борода
    }

    // Физический аксессуар старого контракта (AccessoryBuilder). Накладки головы теперь строит HeadGear.
    public enum AccessoryType { None, Bandana, Earguards, Mouthguard, Helmet }

    // Телосложение: пять заметно разных фигур
    public enum BodyType
    {
        Lean    = 0,  // сухой: узкий, тонкие руки, впалый живот
        Athlete = 1,  // атлет: широкие плечи, узкая талия
        Heavy   = 2,  // тяжеловес: крупный, живот, толстые руки и шея
        Stocky  = 3,  // коренастый: низкий короткий корпус, широкий
        Long    = 4,  // длинный: высокий, вытянутый узкий корпус
    }

    // Причёска поверх анатомической головы (у самой головы короткие тёмные волосы; «лысый» закрывает их кожей)
    public enum HairStyle { Bald = 0, Buzz = 1, Mohawk = 2, FullHair = 3, Ponytail = 4 }

    public enum BeardStyle
    {
        None      = 0,
        Mustache  = 1,  // усы
        Horseshoe = 2,  // усы подковой
        Goatee    = 3,  // усы и бородка на подбородке
        Chinstrap = 4,  // шкиперская: по линии челюсти, без усов
        Full      = 5,  // полная борода
    }

    // Головной убор (лицо открыто)
    public enum HeadWear { None = 0, Headband = 1, Helmet = 2, Beanie = 3 }

    // Нательная отметина из вокселей
    public enum BodyMark
    {
        None        = 0,
        ArmBand     = 1,  // татуировка-браслет на плече
        ChestTattoo = 2,  // татуировка-полосы от груди к плечу
        Scar        = 3,  // шрам на животе
        ChestHair   = 4,  // волосы на груди
    }

    public struct BodyColors
    {
        public Color skin;              // тон кожи тела и анатомической головы (FighterGen.SkinTones[skinTone])
        public int   skinTone;          // номер тона, 0 светлый … 4 тёмный (VoxProbe.AnatLook.Tones)
        public Color trunks;            // основной цвет трусов
        public Color accent;            // пояс трусов
        public Color stripe;            // лампасы и кант
        public int   trunksPalette;     // номер палитры трусов (для проверки различимости)
        public int   trunksPattern;     // 0 — широкий лампас, 1 — двойной, 2 — широкий низ, 3 — пополам
        public Color gloveA, gloveB;    // перчатки: основной цвет и манжета
        public int   glovePalette;
        public bool  handWraps;         // бинты на запястьях
        public Color wraps;
    }

    public struct BodyBuild
    {
        public BodyType type;
        public float scale;         // 0.90–1.10, масштаб корня бойца
        public float shoulderMult;  // 0.85–1.15, ширина плеч
        public float neckMult;      // 0.80–1.20, толщина шеи и трапеций
        public float mass;          // 60–110 кг, влияет на анимационный вес
        public float chestMult;     // 0.85–1.20, объём груди
        public float armMult;       // 0.85–1.25, толщина рук
        public float waistMult;     // 0.85–1.30, ширина талии (от 1.1 — живот)
        public float torsoLen;      // 0.92–1.10, длина корпуса
    }

    // Накладки головы для HeadGear
    public struct HeadLook
    {
        public HairStyle  hair;
        public BeardStyle beard;
        public HeadWear   wear;
        public bool  plaster;     // пластырь на брови
        public bool  earring;     // серьга-кольцо
        public bool  pompom;      // помпон шапочки
        public int   side;        // сторона пластыря и серьги: +1 — +X головы (на экране слева), −1 — −X
        public Color hairColor;   // волосы и борода
        public Color wearColor, wearTrim;
    }

    // Нормализованные характеристики 1–10 для экрана выбора бойца
    public struct FighterStats
    {
        public int health;      // живучесть
        public int damage;      // ударная мощь
        public int speed;       // скорость замаха
        public int resilience;  // стойкость к нокдауну
    }

    public struct FighterDef
    {
        public uint        seed;
        public string      name;        // имя
        public string      nickname;    // прозвище («Молот», «Утёс»)
        public int         styleIdx;    // 0–7, стиль боя
        public int         level;       // 1–N
        public BodyColors  colors;
        public HeadStyle   headStyle;   // сводка head для старого контракта
        public AccessoryType accessory; // сводка head для старого контракта
        public BodyBuild   build;
        public HeadLook    head;        // накладки головы
        public BodyMark    mark;        // нательная отметина
        public int         markSide;    // +1 — +X тела (на экране слева), −1 — −X
        public FighterStats stats;
        public string      cardText;    // визитка для экрана выбора

        // Совместимость с контрактом: scale как прямое поле
        public float scale => build.scale;
    }
}
