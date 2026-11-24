using UnityEngine;

namespace VoxGame
{
    // Внешний вид головы — причёска, борода, аксессуар — одним enum
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

    // Физический аксессуар, который AccessoryBuilder строит как VoxBody поверх головы
    public enum AccessoryType { None, Bandana, Earguards, Mouthguard, Helmet }

    public struct BodyColors
    {
        public Color skin;              // оттенок кожи тела
        public Color trunks;            // цвет трусов
        public Color accent;            // отделка (лампасы, логотип)
        public Color gloveA, gloveB;    // основной и акцентный цвет перчаток
    }

    public struct BodyBuild
    {
        public float scale;         // 0.90–1.10, шаг 0.05 (5 значений)
        public float shoulderMult;  // 0.85–1.15, множитель ширины плеч
        public float neckMult;      // 0.80–1.20, множитель толщины шеи
        public float mass;          // 60–110 кг, влияет на анимационный вес
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
        public HeadStyle   headStyle;
        public AccessoryType accessory; // для AccessoryBuilder.Build()
        public BodyBuild   build;
        public FighterStats stats;
        public string      cardText;    // визитка для экрана выбора

        // Совместимость с контрактом: scale как прямое поле
        public float scale => build.scale;
    }
}
