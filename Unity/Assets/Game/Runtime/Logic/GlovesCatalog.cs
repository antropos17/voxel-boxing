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
        /// <summary>Описание для магазина: чем перчатка отличается от остальных.</summary>
        public string description;
        public float dmgMult;
        public float speedMult;
        public float staminaCostMult;
        public float megaGainMult;
        public float incomingDmgMult;
        public int price;
        /// <summary>Условие открытия: "" — сразу, "ladder>=N" — после N побед.</summary>
        public string unlockCondition;
        /// <summary>Форма для представления: номер GloveShape (0 классика, 1 тяжёлая, 2 скоростная, 3 раструб с молнией,
        /// 4 пластины, 5 шипы, 6 чемпионская со звездой, 7 бинты). Полный вид — GloveLooks по id.</summary>
        public int shapeTag;
        public Color colorA;
        public Color colorB;
    }

    public static class GlovesCatalog {

        public static readonly GloveEntry[] All = {
            new GloveEntry {
                id = "classic", displayName = "Классика",
                description = "Надёжная пара на каждый бой: без сильных и слабых сторон.",
                dmgMult = 1.00f, speedMult = 1.00f, staminaCostMult = 1.00f,
                megaGainMult = 1.00f, incomingDmgMult = 1.00f,
                price = 0, unlockCondition = "", shapeTag = 0,
                colorA = new Color(0.87f, 0.13f, 0.13f),
                colorB = Color.white,
            },
            new GloveEntry {
                id = "heavy", displayName = "Тяжёлые",
                description = "Плотная набивка: удар тяжелее, но замах медленнее и сил уходит больше.",
                dmgMult = 1.15f, speedMult = 0.90f, staminaCostMult = 1.10f,
                megaGainMult = 1.00f, incomingDmgMult = 1.00f,
                price = 250, unlockCondition = "", shapeTag = 1,
                colorA = new Color(0.29f, 0.18f, 0.10f),
                colorB = new Color(0.78f, 0.63f, 0.25f),
            },
            new GloveEntry {
                id = "speed", displayName = "Скорострел",
                description = "Лёгкие и узкие: быстрый замах и экономия сил ценой силы удара.",
                dmgMult = 0.90f, speedMult = 1.20f, staminaCostMult = 0.85f,
                megaGainMult = 1.00f, incomingDmgMult = 1.00f,
                price = 250, unlockCondition = "", shapeTag = 2,
                colorA = new Color(0.00f, 0.80f, 1.00f),
                colorB = new Color(0.00f, 0.27f, 0.67f),
            },
            new GloveEntry {
                id = "mega", displayName = "Мега-тяг",
                description = "Заряженные: шкала мега-удара копится в полтора раза быстрее.",
                dmgMult = 1.00f, speedMult = 1.00f, staminaCostMult = 1.00f,
                megaGainMult = 1.50f, incomingDmgMult = 1.00f,
                price = 400, unlockCondition = "ladder>=3", shapeTag = 3,
                colorA = new Color(0.53f, 0.00f, 0.80f),
                colorB = new Color(1.00f, 0.67f, 0.00f),
            },
            new GloveEntry {
                id = "armor", displayName = "Броня",
                description = "Стальные пластины: пропускаешь меньше урона, но руки тяжелее.",
                dmgMult = 0.95f, speedMult = 0.95f, staminaCostMult = 1.05f,
                megaGainMult = 1.00f, incomingDmgMult = 0.80f,
                price = 500, unlockCondition = "ladder>=5", shapeTag = 4,
                colorA = new Color(0.24f, 0.26f, 0.29f),
                colorB = new Color(0.10f, 0.11f, 0.13f),
            },
            new GloveEntry {
                id = "spike", displayName = "Шипастые",
                description = "Шипы на костяшках: удар сильнее и мега-удар заряжается быстрее.",
                dmgMult = 1.10f, speedMult = 1.00f, staminaCostMult = 1.00f,
                megaGainMult = 1.10f, incomingDmgMult = 1.00f,
                price = 600, unlockCondition = "ladder>=7", shapeTag = 5,
                colorA = new Color(0.07f, 0.07f, 0.07f),
                colorB = new Color(1.00f, 0.27f, 0.00f),
            },
            new GloveEntry {
                id = "champion", displayName = "Чемпионские",
                description = "Золото чемпиона: всё понемногу лучше. Только для прошедших всю лестницу.",
                dmgMult = 1.10f, speedMult = 1.05f, staminaCostMult = 0.95f,
                megaGainMult = 1.25f, incomingDmgMult = 0.90f,
                price = 1500, unlockCondition = "ladder>=12", shapeTag = 6,
                colorA = new Color(1.00f, 0.76f, 0.15f),
                colorB = new Color(0.80f, 0.08f, 0.10f),
            },
            new GloveEntry {
                id = "wraps", displayName = "Бинты",
                description = "Голые кулаки в бинтах: самый быстрый замах и мало усталости, но защиты почти нет.",
                dmgMult = 0.90f, speedMult = 1.25f, staminaCostMult = 0.80f,
                megaGainMult = 1.10f, incomingDmgMult = 1.20f,
                price = 150, unlockCondition = "", shapeTag = 7,
                colorA = new Color(0.95f, 0.92f, 0.84f),
                colorB = new Color(0.78f, 0.74f, 0.64f),
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
