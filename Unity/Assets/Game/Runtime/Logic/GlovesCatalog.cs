using UnityEngine;

namespace VoxGame {

    /// <summary>
    /// Запись каталога перчаток. Все множители применяются к базовым параметрам игрока.
    /// speedMult — коэффициент скорости замаха; используется как делитель времени:
    /// windupTime = base / speedMult, поэтому &gt; 1 = быстрее, &lt; 1 = медленнее.
    /// </summary>
    public struct GloveEntry {
        public string id;
        public string displayName;
        public float dmgMult;
        public float speedMult;
        public float staminaCostMult;
        public float megaGainMult;
        public float incomingDmgMult;
        public int price;
        /// <summary>Условие открытия: "" — сразу, "ladder>=N" — после N побед.</summary>
        public string unlockCondition;
        /// <summary>Признак формы для представления: 0=round, 1=bulky, 2=compact, 3=studded.</summary>
        public int shapeTag;
        public Color colorA;
        public Color colorB;
    }

    public static class GlovesCatalog {

        public static readonly GloveEntry[] All = {
            new GloveEntry {
                id = "classic", displayName = "Классика",
                dmgMult = 1.00f, speedMult = 1.00f, staminaCostMult = 1.00f,
                megaGainMult = 1.00f, incomingDmgMult = 1.00f,
                price = 0, unlockCondition = "", shapeTag = 0,
                colorA = new Color(0.87f, 0.13f, 0.13f),
                colorB = Color.white,
            },
            new GloveEntry {
                id = "heavy", displayName = "Тяжёлые",
                dmgMult = 1.15f, speedMult = 0.90f, staminaCostMult = 1.10f,
                megaGainMult = 1.00f, incomingDmgMult = 1.00f,
                price = 250, unlockCondition = "", shapeTag = 1,
                colorA = new Color(0.29f, 0.18f, 0.10f),
                colorB = new Color(0.78f, 0.63f, 0.25f),
            },
            new GloveEntry {
                id = "speed", displayName = "Скорострел",
                dmgMult = 0.90f, speedMult = 1.20f, staminaCostMult = 0.85f,
                megaGainMult = 1.00f, incomingDmgMult = 1.00f,
                price = 250, unlockCondition = "", shapeTag = 2,
                colorA = new Color(0.00f, 0.80f, 1.00f),
                colorB = new Color(0.00f, 0.27f, 0.67f),
            },
            new GloveEntry {
                id = "mega", displayName = "Мега-тяг",
                dmgMult = 1.00f, speedMult = 1.00f, staminaCostMult = 1.00f,
                megaGainMult = 1.50f, incomingDmgMult = 1.00f,
                price = 400, unlockCondition = "ladder>=3", shapeTag = 0,
                colorA = new Color(0.53f, 0.00f, 0.80f),
                colorB = new Color(1.00f, 0.67f, 0.00f),
            },
            new GloveEntry {
                id = "armor", displayName = "Броня",
                dmgMult = 0.95f, speedMult = 0.95f, staminaCostMult = 1.05f,
                megaGainMult = 1.00f, incomingDmgMult = 0.80f,
                price = 500, unlockCondition = "ladder>=5", shapeTag = 1,
                colorA = new Color(0.27f, 0.27f, 0.27f),
                colorB = new Color(0.67f, 0.67f, 0.67f),
            },
            new GloveEntry {
                id = "spike", displayName = "Шипастые",
                dmgMult = 1.10f, speedMult = 1.00f, staminaCostMult = 1.00f,
                megaGainMult = 1.10f, incomingDmgMult = 1.00f,
                price = 600, unlockCondition = "ladder>=7", shapeTag = 3,
                colorA = new Color(0.07f, 0.07f, 0.07f),
                colorB = new Color(1.00f, 0.27f, 0.00f),
            },
        };

        /// <summary>
        /// Вернуть запись по id. Если не найдено — возвращает classic (index 0).
        /// </summary>
        public static GloveEntry Get(string id) {
            if (id != null) {
                foreach (var g in All) {
                    if (g.id == id) return g;
                }
            }
            return All[0];
        }
    }
}
