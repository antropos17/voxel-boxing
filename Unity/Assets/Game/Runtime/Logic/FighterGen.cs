using UnityEngine;

namespace VoxGame
{
    // Процедурная генерация бойца из зерна. Один и тот же seed → побитово тот же боец.
    public static class FighterGen
    {
        // Тон кожи анатомической головы (AnatMat «кожа», sRGB). Голова у всех соперников одна, поэтому тело
        // всегда этого тона, а различимость дают фигура, трусы, перчатки, накладки головы и отметины.
        public static readonly Color32 HeadSkin = new Color32(222, 168, 128, 255);

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

        // 32 прозвища, без повторов
        static readonly string[] Nicknames =
        {
            "Кулак",   "Молот",   "Утёс",    "Гром",    "Монстр",
            "Гранит",  "Коршун",  "Тигр",    "Медведь", "Волк",
            "Лом",     "Топор",   "Буря",    "Скала",   "Призрак",
            "Гвоздь",  "Таран",   "Смерч",   "Ягуар",   "Дракон",
            "Шторм",   "Крюк",    "Пушка",   "Стальной","Вихрь",
            "Лёд",     "Огонь",   "Кабан",   "Зубр",    "Сокол",
            "Рыцарь",  "Вулкан",
        };

        // ---- Палитры -----------------------------------------------------

        // Трусы: основной цвет, пояс, лампасы. Подобраны попарно различимыми.
        static readonly Color32[] TrunksMain =
        {
            new Color32(200,  28,  36, 255),  // алый
            new Color32( 22,  70, 170, 255),  // синий
            new Color32(236, 236, 236, 255),  // белый
            new Color32( 40,  40,  46, 255),  // чёрный
            new Color32( 20, 140,  80, 255),  // изумрудный
            new Color32(110,  40, 160, 255),  // фиолетовый
            new Color32(235, 110,  20, 255),  // оранжевый
            new Color32( 20, 165, 170, 255),  // бирюзовый
            new Color32(210,  30, 120, 255),  // малиновый
            new Color32(220, 170,  30, 255),  // золотой
            new Color32(105, 118,  66, 255),  // хаки
            new Color32(110, 180, 240, 255),  // небесный
            new Color32(120,  20,  36, 255),  // бордовый
            new Color32(170, 175, 185, 255),  // серебристый
        };
        static readonly Color32[] TrunksBelt =
        {
            new Color32(245, 245, 245, 255),
            new Color32(240, 200,  40, 255),
            new Color32( 30,  90, 200, 255),
            new Color32(214, 170,  40, 255),
            new Color32(250, 250, 250, 255),
            new Color32(250, 210,  60, 255),
            new Color32( 25,  25,  30, 255),
            new Color32(250, 250, 250, 255),
            new Color32( 30,  30,  36, 255),
            new Color32(150,  20,  20, 255),
            new Color32( 40,  40,  30, 255),
            new Color32(250, 250, 250, 255),
            new Color32(220, 190, 120, 255),
            new Color32(200,  30,  40, 255),
        };
        static readonly Color32[] TrunksStripe =
        {
            new Color32(245, 245, 245, 255),
            new Color32(240, 200,  40, 255),
            new Color32( 30,  90, 200, 255),
            new Color32(214, 170,  40, 255),
            new Color32( 20,  20,  20, 255),
            new Color32(250, 250, 250, 255),
            new Color32( 25,  25,  30, 255),
            new Color32(230,  60,  60, 255),
            new Color32(250, 250, 250, 255),
            new Color32(150,  20,  20, 255),
            new Color32(200, 190, 120, 255),
            new Color32( 20,  50, 120, 255),
            new Color32(220, 190, 120, 255),
            new Color32( 30,  30,  36, 255),
        };

        // Перчатки: основной цвет и манжета
        static readonly Color32[] GloveMain =
        {
            new Color32(210,  30,  30, 255),
            new Color32( 30,  60, 180, 255),
            new Color32( 34,  34,  38, 255),
            new Color32(236, 236, 230, 255),
            new Color32(220, 180,  20, 255),
            new Color32( 20, 130,  70, 255),
            new Color32( 34,  34,  38, 255),
            new Color32(120,  40, 170, 255),
            new Color32(230, 110,  20, 255),
            new Color32( 20, 160, 180, 255),
        };
        static readonly Color32[] GloveCuff =
        {
            new Color32(245, 245, 245, 255),
            new Color32(245, 245, 245, 255),
            new Color32(200,  30,  30, 255),
            new Color32( 30,  70, 190, 255),
            new Color32( 30,  30,  34, 255),
            new Color32(245, 245, 245, 255),
            new Color32(220, 180,  40, 255),
            new Color32(240, 200,  50, 255),
            new Color32( 30,  30,  34, 255),
            new Color32(245, 245, 245, 255),
        };

        // Волосы: естественные и крашеные (крашеные — только для ирокеза)
        static readonly Color32[] HairNatural =
        {
            new Color32( 26,  22,  24, 255),  // чёрные
            new Color32( 62,  40,  28, 255),  // тёмно-русые
            new Color32(112,  64,  34, 255),  // каштановые
            new Color32(176,  82,  32, 255),  // рыжие
            new Color32(210, 176, 108, 255),  // светлые
            new Color32(150, 148, 146, 255),  // седые
        };
        static readonly Color32[] HairDyed =
        {
            new Color32( 40,  90, 210, 255),  // синие
            new Color32( 60, 180,  60, 255),  // зелёные
            new Color32(220,  40,  90, 255),  // розовые
            new Color32(240, 220,  60, 255),  // жёлтые
        };

        // Головные уборы: основной цвет и отделка
        static readonly Color32[] WearMain =
        {
            new Color32(200,  30,  36, 255),
            new Color32( 28,  70, 180, 255),
            new Color32( 36,  36,  40, 255),
            new Color32(236, 236, 232, 255),
            new Color32( 30, 140,  80, 255),
            new Color32(230, 120,  20, 255),
            new Color32(110,  40, 160, 255),
            new Color32(230, 190,  30, 255),
        };
        static readonly Color32[] WearTrim =
        {
            new Color32(245, 245, 245, 255),
            new Color32(245, 245, 245, 255),
            new Color32(220,  40,  40, 255),
            new Color32( 30,  70, 190, 255),
            new Color32(245, 245, 245, 255),
            new Color32( 30,  30,  34, 255),
            new Color32(240, 200,  50, 255),
            new Color32( 30,  30,  34, 255),
        };

        // Бинты
        static readonly Color32[] WrapColors =
        {
            new Color32(240, 238, 232, 255),
            new Color32(200,  36,  40, 255),
            new Color32( 34,  34,  38, 255),
            new Color32( 30,  80, 190, 255),
            new Color32(230, 190,  30, 255),
        };

        // ---- Телосложения ------------------------------------------------
        // Индекс — BodyType: Lean, Athlete, Heavy, Stocky, Long.

        static readonly float[] TypeScaleA  = { 0.95f, 1.00f, 1.05f, 0.90f, 1.05f };
        static readonly float[] TypeScaleB  = { 1.00f, 1.05f, 1.10f, 0.95f, 1.10f };
        static readonly float[] TypeShoulder = { 0.92f, 1.08f, 1.12f, 1.10f, 0.90f };
        static readonly float[] TypeNeck    = { 0.85f, 1.00f, 1.18f, 1.14f, 0.88f };
        static readonly float[] TypeChest   = { 0.88f, 1.06f, 1.16f, 1.10f, 0.90f };
        static readonly float[] TypeArm     = { 0.87f, 1.06f, 1.22f, 1.15f, 0.88f };
        static readonly float[] TypeWaist   = { 0.88f, 0.90f, 1.28f, 1.08f, 0.88f };
        static readonly float[] TypeLen     = { 1.00f, 1.00f, 1.01f, 0.93f, 1.08f };
        static readonly float[] TypeMassLo  = { 62f, 76f, 100f, 84f, 70f };
        static readonly float[] TypeMassHi  = { 70f, 86f, 110f, 94f, 78f };

        // Под повязкой — любая причёска, кроме ирокеза
        static readonly HairStyle[] BandHair = { HairStyle.Bald, HairStyle.Buzz, HairStyle.FullHair, HairStyle.Ponytail };

        // Названия стилей боя для визитки
        static readonly string[] StyleNames =
        {
            "Кирпич", "Шкаф", "Молот", "Бетон", "Утюг", "Гранит", "Бульдозер", "Тихий"
        };

        // Число вариантов для лестницы и тестов
        public static int TrunksPaletteCount => TrunksMain.Length;
        public static int GlovePaletteCount  => GloveMain.Length;
        public static int NicknameCount      => Nicknames.Length;
        public const int BodyTypeCount = 5;
        public const int TopCount      = 8;   // 0–4 причёски без убора, 5 повязка, 6 шлем, 7 шапочка
        public const int BeardCount    = 6;

        // Принудительный выбор признаков (−1 — по зерну). Лестница через это делает бойцов попарно различимыми.
        public struct LookPicks
        {
            public int trunks, gloves, body, top, beard, nick, style;
            public static readonly LookPicks Free = new LookPicks { trunks = -1, gloves = -1, body = -1, top = -1, beard = -1, nick = -1, style = -1 };
        }

        // ---- Публичный API -----------------------------------------------

        /// <summary>
        /// Детерминированная генерация бойца из зерна и уровня.
        /// Один и тот же seed + level → побитово тот же FighterDef.
        /// </summary>
        public static FighterDef Generate(uint seed, int level) => Generate(seed, level, LookPicks.Free);

        /// <summary>Генерация произвольного бойца для быстрого боя.</summary>
        public static FighterDef GenerateRandom(uint seed, int level = 5)
        {
            return Generate(seed, level);
        }

        // Верх головы как одно число: причёска без убора (0–4) или убор (5–7).
        public static int TopOf(in HeadLook h) => h.wear != HeadWear.None ? 4 + (int)h.wear : (int)h.hair;

        // Ключ различимости головы: верх + борода
        public static int HeadKey(in HeadLook h) => TopOf(h) * 8 + (int)h.beard;

        public static FighterDef Generate(uint seed, int level, LookPicks p)
        {
            // Каждый атрибут хеширует seed с уникальной солью
            int nameIdx    = (int)(Hash(seed,  7) % (uint)Names.Length);
            int nickIdx    = p.nick >= 0 ? p.nick % Nicknames.Length : (int)(Hash(seed, 8) % (uint)Nicknames.Length);
            int styleIdx   = p.style >= 0 ? p.style % 8 : (int)(Hash(seed,  0) % 8);
            int trunksIdx  = p.trunks >= 0 ? p.trunks % TrunksMain.Length : (int)(Hash(seed, 3) % (uint)TrunksMain.Length);
            int gloveIdx   = p.gloves >= 0 ? p.gloves % GloveMain.Length : (int)(Hash(seed, 5) % (uint)GloveMain.Length);
            int typeIdx    = p.body >= 0 ? p.body % BodyTypeCount : (int)(Hash(seed, 13) % BodyTypeCount);
            int top        = p.top >= 0 ? p.top % TopCount : (int)(Hash(seed, 9) % TopCount);
            int beard      = p.beard >= 0 ? p.beard % BeardCount : (int)(Hash(seed, 14) % BeardCount);
            bool wrapsOn   = Hash(seed, 24) % 3u != 0u;

            var colors = new BodyColors
            {
                skin          = (Color)HeadSkin,
                trunks        = (Color)TrunksMain[trunksIdx],
                accent        = (Color)TrunksBelt[trunksIdx],
                stripe        = (Color)TrunksStripe[trunksIdx],
                trunksPalette = trunksIdx,
                trunksPattern = (int)(Hash(seed, 26) % 4u),
                gloveA        = (Color)GloveMain[gloveIdx],
                gloveB        = (Color)GloveCuff[gloveIdx],
                glovePalette  = gloveIdx,
                handWraps     = wrapsOn,
                wraps         = (Color)WrapColors[Hash(seed, 25) % (uint)WrapColors.Length],
            };

            var build = MakeBuild(seed, (BodyType)typeIdx);
            var head  = MakeHead(seed, top, (BeardStyle)beard);

            var mark     = (BodyMark)(Hash(seed, 22) % 5u);
            int markSide = (Hash(seed, 23) & 1u) == 0u ? 1 : -1;

            var headStyle = SummaryStyle(head);
            var accType   = HeadStyleToAccessoryType(headStyle);
            var stats     = ComputeStats(level, styleIdx);

            var f = new FighterDef
            {
                seed      = seed,
                name      = Names[nameIdx],
                nickname  = Nicknames[nickIdx],
                styleIdx  = styleIdx,
                level     = level,
                colors    = colors,
                headStyle = headStyle,
                accessory = accType,
                build     = build,
                head      = head,
                mark      = mark,
                markSide  = markSide,
                stats     = stats,
            };
            f.cardText = CardText(f);
            return f;
        }

        // Визитка для экрана выбора
        internal static string CardText(in FighterDef f) =>
            $"«{f.nickname}» · {StyleNames[f.styleIdx]} · HP {220 + 50 * (f.level - 1)} · Ур.{f.level}";

        // ---- Внутренние вспомогательные ----------------------------------

        static BodyBuild MakeBuild(uint seed, BodyType type)
        {
            int t = (int)type;
            float mass = Mathf.Lerp(TypeMassLo[t], TypeMassHi[t], (Hash(seed, 12) % 11u) / 10f);
            return new BodyBuild
            {
                type         = type,
                scale        = (Hash(seed, 1) & 1u) == 0u ? TypeScaleA[t] : TypeScaleB[t],
                shoulderMult = Mathf.Clamp(TypeShoulder[t] + Jit(seed, 10, 0.03f), 0.85f, 1.15f),
                neckMult     = Mathf.Clamp(TypeNeck[t]     + Jit(seed, 11, 0.04f), 0.80f, 1.20f),
                mass         = Mathf.Round(mass),
                chestMult    = Mathf.Clamp(TypeChest[t]    + Jit(seed, 27, 0.03f), 0.85f, 1.20f),
                armMult      = Mathf.Clamp(TypeArm[t]      + Jit(seed, 28, 0.03f), 0.85f, 1.25f),
                waistMult    = Mathf.Clamp(TypeWaist[t]    + Jit(seed, 29, 0.02f), 0.85f, 1.30f),
                torsoLen     = Mathf.Clamp(TypeLen[t]      + Jit(seed, 30, 0.01f), 0.92f, 1.10f),
            };
        }

        // Шаг разброса: −step, 0 или +step
        static float Jit(uint seed, uint salt, float step) => ((int)(Hash(seed, salt) % 3u) - 1) * step;

        static HeadLook MakeHead(uint seed, int top, BeardStyle beard)
        {
            var h = new HeadLook { beard = beard, side = (Hash(seed, 19) & 1u) == 0u ? 1 : -1 };
            if (top < 5)
            {
                h.hair = (HairStyle)top;
                h.wear = HeadWear.None;
            }
            else
            {
                h.wear = (HeadWear)(top - 4);
                uint r = Hash(seed, 15);
                if (h.wear == HeadWear.Headband)
                {
                    h.hair = BandHair[r % (uint)BandHair.Length];
                }
                else
                    h.hair = (r & 1u) == 0u ? HairStyle.Bald : HairStyle.Buzz;   // под шлемом и шапочкой — коротко
            }

            uint hc = Hash(seed, 16);
            if (h.hair == HairStyle.Mohawk && (hc & 1u) == 0u)
                h.hairColor = (Color)HairDyed[(hc >> 1) % (uint)HairDyed.Length];
            else
                h.hairColor = (Color)HairNatural[(hc >> 1) % (uint)HairNatural.Length];

            int wc = (int)(Hash(seed, 21) % (uint)WearMain.Length);
            h.wearColor = (Color)WearMain[wc];
            h.wearTrim  = (Color)WearTrim[wc];

            h.plaster = Hash(seed, 17) % 4u == 0u;
            h.earring = h.wear != HeadWear.Helmet && Hash(seed, 18) % 3u == 0u;
            h.pompom  = h.wear == HeadWear.Beanie && (Hash(seed, 20) & 1u) == 0u;
            return h;
        }

        // Сводка вида головы для старого поля headStyle
        static HeadStyle SummaryStyle(in HeadLook h)
        {
            if (h.wear == HeadWear.Helmet)   return HeadStyle.Helmet;
            if (h.wear == HeadWear.Headband) return HeadStyle.Bandana;
            if (h.beard == BeardStyle.Full || h.beard == BeardStyle.Goatee) return HeadStyle.Beard;
            switch (h.hair)
            {
                case HairStyle.Bald:   return HeadStyle.Bald;
                case HairStyle.Buzz:   return HeadStyle.Buzz;
                case HairStyle.Mohawk: return HeadStyle.Mohawk;
                default:               return HeadStyle.FullHair;
            }
        }

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

        // Перестановка чисел 0..n−1 (Fisher-Yates с seed)
        internal static int[] Perm(int n, uint seed)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = i;
            uint s = seed;
            for (int i = n - 1; i > 0; i--)
            {
                s = Hash(s, (uint)i + 0x51u);
                int j = (int)(s % (uint)(i + 1));
                int tmp = a[i]; a[i] = a[j]; a[j] = tmp;
            }
            return a;
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

            // Урон: 9 (ур.1) .. 26.6 (ур.12) → диапазон ~17.6, с множителем стиля (как бьёт в бою)
            float dmgRaw = (9f + 1.6f * (level - 1)) * FightAI.Styles[Mathf.Clamp(styleIdx, 0, FightAI.Styles.Length - 1)].d;
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
