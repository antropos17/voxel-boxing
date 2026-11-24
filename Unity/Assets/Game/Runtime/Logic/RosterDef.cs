namespace VoxGame
{
    // Лестница карьеры: 12 бойцов, уровни 1–12.
    // Гарантии: уникальные имена; соседи не совпадают одновременно по (headStyle, trunks, style).
    public class RosterDef
    {
        public FighterDef[] Fighters { get; private set; }

        public RosterDef(uint seed)
        {
            Fighters = new FighterDef[12];

            // Уникальные имена через перемешивание пула
            var names = new string[12];
            FighterGen.FillUniqueNames(FighterGen.Hash(seed, 0xABCDu), names);

            // Генерируем бойцов
            for (int i = 0; i < 12; i++)
            {
                uint s = FighterGen.Hash(seed, (uint)(i + 100));
                Fighters[i] = FighterGen.Generate(s, i + 1);
                ApplyName(ref Fighters[i], names[i]);
            }

            // Гарантируем различимость соседей (до 200 попыток на бойца)
            for (int i = 1; i < 12; i++)
            {
                uint attempt = 0;
                while (attempt < 200u && TripleMatch(Fighters[i - 1], Fighters[i]))
                {
                    attempt++;
                    uint s2 = FighterGen.Hash(seed ^ (attempt * 0xDEADBEEFu), (uint)(i + 100 + attempt * 13u));
                    var regen = FighterGen.Generate(s2, i + 1);
                    ApplyName(ref regen, names[i]);  // имя остаётся из пула
                    Fighters[i] = regen;
                }
            }
        }

        public FighterDef Get(int index) => Fighters[index];

        // Следующий соперник по кругу
        public FighterDef Next(int currentIndex) => Fighters[(currentIndex + 1) % Fighters.Length];

        // Случайный боец для быстрого боя (не из лестницы, независимый seed)
        public static FighterDef GenerateQuickFight(uint seed, int level = 6)
        {
            uint s = FighterGen.Hash(seed, 0xFEEDu);
            return FighterGen.Generate(s, level);
        }

        // --- приватные вспомогательные ------------------------------------

        // Тройное совпадение = неразличимые соседи
        static bool TripleMatch(in FighterDef a, in FighterDef b)
        {
            return a.headStyle == b.headStyle
                && a.colors.trunks == b.colors.trunks
                && a.styleIdx == b.styleIdx;
        }

        // Заменить имя и пересобрать cardText (nickname и остальное не меняются)
        static void ApplyName(ref FighterDef f, string name)
        {
            f.name     = name;
            f.cardText = $"«{f.nickname}» · {StyleName(f.styleIdx)} · HP {220 + 50 * (f.level - 1)} · Ур.{f.level}";
        }

        static readonly string[] StyleNames =
            { "Кирпич", "Шкаф", "Молот", "Бетон", "Утюг", "Гранит", "Бульдозер", "Тихий" };

        static string StyleName(int idx) => StyleNames[idx];
    }
}
