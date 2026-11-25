namespace VoxGame
{
    // Лестница карьеры: 12 бойцов, уровни 1–12.
    // Гарантии: уникальные имена и прозвища; у всех 12 разные палитры трусов и разные пары
    // «верх головы (причёска или убор) + борода»; соседи различаются телосложением и перчатками.
    public class RosterDef
    {
        public const int Count = 12;

        // Стиль боя по ступени: в начале простые (Кирпич, Молот), к поясу хитрые (Гранит, Тихий); соседи не повторяются.
        // Индексы FightAI.Styles: 0 Кирпич, 1 Шкаф, 2 Молот, 3 Бетон, 4 Утюг, 5 Гранит, 6 Бульдозер, 7 Тихий.
        public static readonly int[] LadderStyles = { 0, 2, 4, 3, 1, 2, 6, 4, 5, 1, 7, 5 };

        public FighterDef[] Fighters { get; private set; }

        public RosterDef(uint seed)
        {
            Fighters = new FighterDef[Count];

            // Уникальные имена через перемешивание пула
            var names = new string[Count];
            FighterGen.FillUniqueNames(FighterGen.Hash(seed, 0xABCDu), names);

            // Перестановки признаков: первые 12 элементов каждой не повторяются
            int[] trunks = FighterGen.Perm(FighterGen.TrunksPaletteCount, FighterGen.Hash(seed, 0x7121u));
            int[] gloves = FighterGen.Perm(FighterGen.GlovePaletteCount,  FighterGen.Hash(seed, 0x6107u));
            int[] nicks  = FighterGen.Perm(FighterGen.NicknameCount,      FighterGen.Hash(seed, 0x4E1Cu));
            int[] tops   = Chain(FighterGen.TopCount,      FighterGen.Hash(seed, 0x70F5u));
            int[] bodies = Chain(FighterGen.BodyTypeCount, FighterGen.Hash(seed, 0xB0D1u));
            int[] beards = FighterGen.Perm(FighterGen.BeardCount, FighterGen.Hash(seed, 0xBEA4u));

            var usedHead = new bool[FighterGen.TopCount * 8];
            for (int i = 0; i < Count; i++)
            {
                // борода по кругу; если пара «верх + борода» уже была — следующая борода
                int top = tops[i], beard = beards[i % beards.Length];
                while (usedHead[top * 8 + beard]) beard = (beard + 1) % FighterGen.BeardCount;
                usedHead[top * 8 + beard] = true;

                var picks = new FighterGen.LookPicks
                {
                    trunks = trunks[i],
                    gloves = gloves[i % gloves.Length],
                    body   = bodies[i],
                    top    = top,
                    beard  = beard,
                    nick   = nicks[i],
                    style  = LadderStyles[i],
                };
                uint s = FighterGen.Hash(seed, (uint)(i + 100));
                Fighters[i] = FighterGen.Generate(s, i + 1, picks);
                ApplyName(ref Fighters[i], names[i]);
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

        // Цепочка перестановок 0..n−1 длиной не меньше Count: каждое значение встречается не чаще ceil(Count/n) раз,
        // соседние не совпадают (на стыке перестановок совпадение убирается обменом со следующим).
        static int[] Chain(int n, uint seed)
        {
            int blocks = (Count + n - 1) / n + 1;
            var a = new int[blocks * n];
            for (int b = 0; b < blocks; b++)
            {
                int[] p = FighterGen.Perm(n, FighterGen.Hash(seed, (uint)b));
                System.Array.Copy(p, 0, a, b * n, n);
            }
            for (int i = 1; i < a.Length - 1; i++)
                if (a[i] == a[i - 1]) { int t = a[i]; a[i] = a[i + 1]; a[i + 1] = t; }
            return a;
        }

        // Заменить имя и пересобрать cardText (nickname и остальное не меняются)
        static void ApplyName(ref FighterDef f, string name)
        {
            f.name     = name;
            f.cardText = FighterGen.CardText(f);
        }
    }
}
