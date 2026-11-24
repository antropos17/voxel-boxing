using UnityEngine;

namespace VoxGame
{
    // Процедурная генерация бойца из зерна. Один и тот же seed → побитово тот же боец.
    public static class FighterGen
    {
        // ---- Таблицы имён ------------------------------------------------

        // 50 мужских русских имён (из contracts.md, с заменой двух повторов)
        static readonly string[] Names =
        {
            "Артём",    "Дмитрий",   "Иван",      "Николай",  "Алексей",
            "Сергей",   "Михаил",    "Андрей",    "Павел",    "Роман",
            "Владимир", "Евгений",   "Максим",    "Тимур",    "Руслан",
            "Борис",    "Виктор",    "Григорий",  "Денис",    "Илья",
            "Кирилл",   "Леонид",    "Марк",      "Антон",    "Олег",
            "Пётр",     "Семён",     "Степан",    "Фёдор",    "Юрий",
            "Ярослав",  "Захар",     "Игорь",     "Константин","Лев",
            "Матвей",   "Никита",    "Тарас",     "Вадим",    "Геннадий",
            "Данила",   "Эдуард",    "Савелий",   "Зиновий",  "Климент",
            "Митя",     "Нестор",    "Трофим",    "Ермолай",  "Ростислав",
        };

        // 32 прозвища
        static readonly string[] Nicknames =
        {
            "Кулак",   "Молот",   "Утёс",    "Гром",    "Монстр",
            "Гранит",  "Коршун",  "Тигр",    "Медведь", "Волк",
            "Лом",     "Топор",   "Буря",    "Скала",   "Призрак",
            "Гвоздь",  "Таран",   "Смерч",   "Ягуар",   "Дракон",
            "Шторм",   "Крюк",    "Пушка",   "Стальной","Вихрь",
            "Лёд",     "Огонь",   "Кабан",   "Зубр",    "Коршун",
            "Рыцарь",  "Вулкан",
        };

        // ---- Цветовые палитры --------------------------------------------

        // 8 оттенков кожи
        static readonly Color32[] SkinTones =
        {
            new Color32(255, 220, 177, 255),  // светлая
            new Color32(241, 194, 125, 255),  // бронзовая
            new Color32(224, 172, 105, 255),  // тёмная бронза
            new Color32(198, 134,  66, 255),  // карамельная
            new Color32(141,  85,  36, 255),  // коричневая
            new Color32( 80,  47,  21, 255),  // тёмно-коричневая
            new Color32(255, 205, 148, 255),  // персиковая
            new Color32(240, 180, 120, 255),  // золотистая
        };

        // 10 цветов трусов
        static readonly Color32[] TrunksColors =
        {
            new Color32(220,  20,  20, 255),  // красный
            new Color32(  0,  80, 180, 255),  // синий
            new Color32( 20, 160,  20, 255),  // зелёный
            new Color32(200, 160,   0, 255),  // золотой
            new Color32(160,   0, 160, 255),  // фиолетовый
            new Color32(  0, 160, 160, 255),  // бирюзовый
            new Color32(220, 100,   0, 255),  // оранжевый
            new Color32(  0,   0,   0, 255),  // чёрный
            new Color32(230, 230, 230, 255),  // белый
            new Color32(220,  20, 120, 255),  // малиновый
        };

        // 8 акцентных цветов (лампасы, отделка)
        static readonly Color32[] AccentColors =
        {
            new Color32(255, 215,   0, 255),  // золото
            new Color32(192, 192, 192, 255),  // серебро
            new Color32(255,   0,   0, 255),  // красный
            new Color32(  0, 120, 255, 255),  // синий
            new Color32(  0, 200, 100, 255),  // зелёный
            new Color32(200,   0, 200, 255),  // фиолетовый
            new Color32(255, 140,   0, 255),  // оранжевый
            new Color32(255, 255, 255, 255),  // белый
        };

        // 8 цветов перчаток
        static readonly Color32[] GloveColors =
        {
            new Color32(220,  30,  30, 255),
            new Color32( 30,  60, 180, 255),
            new Color32(  0,   0,   0, 255),
            new Color32(220, 180,   0, 255),
            new Color32(180,   0,   0, 255),
            new Color32(  0, 100, 180, 255),
            new Color32( 60,  60,  60, 255),
            new Color32(220,  80,   0, 255),
        };

        // ---- Дискретные шкалы --------------------------------------------

        // Масштаб корня бойца: 5 значений 0.90..1.10 шаг 0.05
        static readonly float[] ScaleValues    = { 0.90f, 0.95f, 1.00f, 1.05f, 1.10f };
        // Ширина плеч: 7 значений 0.85..1.15
        static readonly float[] ShoulderValues = { 0.85f, 0.90f, 0.95f, 1.00f, 1.05f, 1.10f, 1.15f };
        // Толщина шеи: 5 значений 0.80..1.20
        static readonly float[] NeckValues     = { 0.80f, 0.90f, 1.00f, 1.10f, 1.20f };

        // Названия стилей боя для визитки
        static readonly string[] StyleNames =
        {
            "Кирпич", "Шкаф", "Молот", "Бетон", "Утюг", "Гранит", "Бульдозер", "Тихий"
        };

        // ---- Публичный API -----------------------------------------------

        /// <summary>
        /// Детерминированная генерация бойца из зерна и уровня.
        /// Один и тот же seed + level → побитово тот же FighterDef.
        /// </summary>
        public static FighterDef Generate(uint seed, int level)
        {
            // Каждый атрибут хеширует seed с уникальной солью
            int nameIdx    = (int)(Hash(seed,  7) % (uint)Names.Length);
            int nickIdx    = (int)(Hash(seed,  8) % (uint)Nicknames.Length);
            int styleIdx   = (int)(Hash(seed,  0) % 8);
            int scaleIdx   = (int)(Hash(seed,  1) % (uint)ScaleValues.Length);
            int skinIdx    = (int)(Hash(seed,  2) % (uint)SkinTones.Length);
            int trunksIdx  = (int)(Hash(seed,  3) % (uint)TrunksColors.Length);
            int accentIdx  = (int)(Hash(seed,  4) % (uint)AccentColors.Length);
            int gloveAIdx  = (int)(Hash(seed,  5) % (uint)GloveColors.Length);
            int gloveBIdx  = (int)(Hash(seed,  6) % (uint)GloveColors.Length);
            int headIdx    = (int)(Hash(seed,  9) % 8);
            int shoulderI  = (int)(Hash(seed, 10) % (uint)ShoulderValues.Length);
            int neckI      = (int)(Hash(seed, 11) % (uint)NeckValues.Length);
            float mass     = 60f + (Hash(seed, 12) % 51u);  // 60..110 кг

            var colors = new BodyColors
            {
                skin   = (Color)SkinTones[skinIdx],
                trunks = (Color)TrunksColors[trunksIdx],
                accent = (Color)AccentColors[accentIdx],
                gloveA = (Color)GloveColors[gloveAIdx],
                gloveB = (Color)GloveColors[gloveBIdx],
            };

            var build = new BodyBuild
            {
                scale        = ScaleValues[scaleIdx],
                shoulderMult = ShoulderValues[shoulderI],
                neckMult     = NeckValues[neckI],
                mass         = mass,
            };

            var headStyle = (HeadStyle)headIdx;
            var accType   = HeadStyleToAccessoryType(headStyle);
            var stats     = ComputeStats(level, styleIdx);

            string nick      = Nicknames[nickIdx];
            string styleName = StyleNames[styleIdx];
            int    hpMax     = 220 + 50 * (level - 1);
            string cardText  = $"«{nick}» · {styleName} · HP {hpMax} · Ур.{level}";

            return new FighterDef
            {
                seed      = seed,
                name      = Names[nameIdx],
                nickname  = nick,
                styleIdx  = styleIdx,
                level     = level,
                colors    = colors,
                headStyle = headStyle,
                accessory = accType,
                build     = build,
                stats     = stats,
                cardText  = cardText,
            };
        }

        /// <summary>Генерация произвольного бойца для быстрого боя.</summary>
        public static FighterDef GenerateRandom(uint seed, int level = 5)
        {
            return Generate(seed, level);
        }

        // ---- Внутренние вспомогательные ----------------------------------

        // Перемешать первые count имён из пула (Fisher-Yates с seed).
        // Вызывает RosterDef для гарантии уникальности имён в лестнице.
        internal static void FillUniqueNames(uint seed, string[] dest)
        {
            int count = dest.Length;
            // Локальная копия пула
            var pool = new string[Names.Length];
            for (int i = 0; i < Names.Length; i++)
                pool[i] = Names[i];

            // Перемешиваем Фишером-Йейтсом
            uint s = seed;
            for (int i = pool.Length - 1; i > 0; i--)
            {
                s = Hash(s, (uint)i);
                int j = (int)(s % (uint)(i + 1));
                string tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
            }

            System.Array.Copy(pool, dest, count);
        }

        // Финш-хеш Murmur3 (детерминированный, без зависимостей)
        internal static uint Hash(uint seed, uint salt)
        {
            unchecked
            {
                uint h = seed ^ (salt * 2654435761u);
                h ^= h >> 16;
                h *= 0x45d9f3bu;
                h ^= h >> 16;
                return h;
            }
        }

        // HeadStyle → AccessoryType для AccessoryBuilder
        static AccessoryType HeadStyleToAccessoryType(HeadStyle h)
        {
            switch (h)
            {
                case HeadStyle.Bandana:    return AccessoryType.Bandana;
                case HeadStyle.Helmet:     return AccessoryType.Helmet;
                case HeadStyle.Mouthguard: return AccessoryType.Mouthguard;
                default:                   return AccessoryType.None;
            }
        }

        // Нормализованные характеристики 1–10 из уровня и стиля
        static FighterStats ComputeStats(int level, int styleIdx)
        {
            // HP: 220 (ур.1) .. 770 (ур.12) → диапазон 550, шаг ~61 на деление
            float hpRaw = 220f + 50f * (level - 1);
            int   hp    = Mathf.Clamp(1 + (int)((hpRaw - 220f) / 61f), 1, 10);

            // Урон: 9 (ур.1) .. 26.6 (ур.12) → диапазон ~17.6
            float dmgRaw = 9f + 1.6f * (level - 1);
            int   dmg    = Mathf.Clamp(1 + (int)((dmgRaw - 9f) / 1.96f), 1, 10);

            // Скорость: wind уменьшается с уровнем (меньше = быстрее)
            float wind = Mathf.Max(0.3f, 0.6f - 0.045f * (level - 1));
            int   spd  = Mathf.Clamp(1 + (int)((0.6f - wind) / 0.034f), 1, 10);

            // Стойкость: stunMax растёт с уровнем
            float stun = 28f + level * 2.5f;
            int   res  = Mathf.Clamp(1 + (int)((stun - 30.5f) / 3.4f), 1, 10);

            return new FighterStats { health = hp, damage = dmg, speed = spd, resilience = res };
        }
    }
}
