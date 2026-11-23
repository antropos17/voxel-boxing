using UnityEngine;

namespace VoxProbe
{
    /// Материалы анатомической головы (docs/HEAD-SPEC.md, раздел 2). Один байт на воксель — индекс в этой таблице.
    /// Класс: 0 пусто, 1 мягкое, 2 кость, 3 жидкость, 4 твёрдое (зуб), 5 волос, 6 воздух (не рисуется, не связывает куски).
    public static class AnatMat
    {
        public const byte Empty = 0, Skin = 1, Lip = 2, Mucosa = 3, Gum = 4, Tongue = 5, Fat = 6, Fascia = 7, Loose = 8, Parotid = 9,
            Muscle = 10, Tendon = 11, Periost = 12, Compact = 13, Spongy = 14, Cartilage = 15, Dura = 16, Csf = 17, Grey = 18, White = 19,
            CerebGrey = 20, Stem = 21, VenSinus = 22, Artery = 23, Vein = 24, Sclera = 25, Cornea = 26, Iris = 27, Pupil = 28, Lens = 29,
            Vitreous = 30, Nerve = 31, OrbFat = 32, Enamel = 33, Dentin = 34, Pulp = 35, Hair = 36, Brow = 37, Lash = 38, Stubble = 39,
            Air = 40, Clot = 41, Crushed = 42, Blood = 43, Covered = 44, Aqueous = 45, Shade = 46, Count = 47;
        /// Covered — ячейка занята другим телом (глаз, зуб, нижняя челюсть): для отсечения граней она сплошная, сама не рисуется.
        /// Aqueous — водянистая влага передней камеры глаза. Она и роговица прозрачны: сетка их не рисует, грани за ними видны (Clear).
        /// Shade — не материал ячейки: «тень полости», которой сетка закрывает видимый воздух там, где он переходит в скрытый (AnatVis).

        public const byte ClsEmpty = 0, ClsSoft = 1, ClsBone = 2, ClsLiquid = 3, ClsHard = 4, ClsHair = 5, ClsAir = 6;

        public struct Def
        {
            public string name;
            public Color32 surf, cut;    // цвет поверхности и цвет на срезе (sRGB)
            public float vary;           // разброс цвета 0..1
            public float strength;       // относительная прочность (JS: кожа 8,5; мышца 12; кость 38)
            public float wet;            // влажность (блеск) 0..1
            public float bleed;          // кровоточивость при вскрытии 0..1
            public byte cls;
        }

        static Def D(string n, int r, int g, int b, int cr, int cg, int cb, float vary, float str, float wet, float bleed, byte cls) =>
            new Def { name = n, surf = new Color32((byte)r, (byte)g, (byte)b, 255), cut = new Color32((byte)cr, (byte)cg, (byte)cb, 255), vary = vary, strength = str, wet = wet, bleed = bleed, cls = cls };

        /// Таблица материалов. Тон кожи, волос и радужки подставляет внешность (Look), здесь — LOOKS[0] веб-версии.
        public static readonly Def[] Table =
        {
            D("пусто",                0,0,0, 0,0,0, 0, 0, 0, 0, ClsEmpty),
            D("кожа (эпидермис+дерма)", 222,168,128, 196,110,104, .05f, 8.5f, .15f, .6f, ClsSoft),
            D("губа",                 204,128,106, 170,70,74, .05f, 7f, .45f, .8f, ClsSoft),
            D("слизистая",            196,92,98, 180,70,80, .08f, 6f, .9f, .9f, ClsSoft),
            D("десна",                208,112,114, 190,90,96, .06f, 8f, .8f, .7f, ClsSoft),
            D("язык",                 198,98,102, 170,60,66, .08f, 10f, .85f, .8f, ClsSoft),
            D("жир",                  232,200,124, 238,196,96, .14f, 6f, .5f, .2f, ClsSoft),
            D("фасция, апоневроз",    214,210,200, 222,218,206, .05f, 14f, .7f, .1f, ClsSoft),
            D("рыхлая клетчатка",     226,214,180, 230,218,186, .06f, 3f, .5f, .2f, ClsSoft),
            D("околоушная железа",    214,170,130, 206,160,120, .14f, 7f, .4f, .4f, ClsSoft),
            D("мышца",                150,36,40, 140,30,36, .10f, 12f, .6f, .7f, ClsSoft),
            D("сухожилие",            226,222,210, 230,226,214, .04f, 22f, .6f, .1f, ClsSoft),
            D("надкостница",          220,190,170, 214,180,160, .05f, 16f, .5f, .3f, ClsSoft),
            D("компактная кость",     236,226,200, 240,230,204, .04f, 44f, .25f, .05f, ClsBone),
            D("губчатая кость",       200,120,110, 196,96,90, .30f, 26f, .5f, .6f, ClsBone),
            D("хрящ",                 216,226,230, 214,226,234, .04f, 14f, .45f, .05f, ClsSoft),
            D("твёрдая оболочка",     214,206,196, 206,198,190, .05f, 13f, .7f, .3f, ClsSoft),
            D("ликвор",               200,214,226, 190,206,222, .02f, .5f, 1f, 0f, ClsLiquid),
            D("серое вещество",       170,120,124, 168,118,122, .06f, 15f, .7f, .5f, ClsSoft),
            D("белое вещество",       236,224,206, 240,228,210, .04f, 18f, .6f, .3f, ClsSoft),
            D("мозжечок (кора)",      160,104,110, 158,100,108, .06f, 15f, .7f, .5f, ClsSoft),
            D("ствол, спинной мозг",  226,212,196, 230,214,198, .04f, 18f, .6f, .4f, ClsSoft),
            D("венозный синус",       70,20,40, 60,10,30, .05f, 4f, .9f, 1f, ClsSoft),
            D("артерия",              190,30,40, 170,20,30, .05f, 9f, .8f, 1f, ClsSoft),
            D("вена",                 80,30,60, 70,24,52, .05f, 6f, .8f, .9f, ClsSoft),
            D("склера",               236,232,226, 240,236,230, .03f, 14f, .8f, .2f, ClsSoft),
            D("роговица",             220,230,236, 224,232,238, .02f, 11f, 1f, 0f, ClsSoft),
            D("радужка",              86,58,36, 86,58,36, .12f, 6f, .7f, .3f, ClsSoft),
            D("зрачок",               14,10,12, 14,10,12, 0, 6f, 1f, 0f, ClsSoft),
            D("хрусталик",            230,226,190, 232,226,188, .03f, 10f, .8f, 0f, ClsSoft),
            D("стекловидное тело",    214,224,220, 210,222,218, .02f, 2f, 1f, 0f, ClsLiquid),
            D("нерв",                 236,230,214, 240,232,214, .04f, 12f, .6f, .2f, ClsSoft),
            D("жир глазницы",         240,210,130, 242,206,112, .12f, 5f, .6f, .3f, ClsSoft),
            D("эмаль",                240,236,222, 244,240,228, .03f, 40f, .5f, 0f, ClsHard),
            D("дентин",               232,214,170, 232,212,164, .04f, 28f, .3f, 0f, ClsHard),
            D("пульпа",               210,110,120, 200,96,108, .05f, 6f, .7f, .6f, ClsSoft),
            D("волосы",               46,31,23, 46,31,23, .25f, 4f, .1f, 0f, ClsHair),
            D("бровь",                39,26,20, 39,26,20, .25f, 4f, .1f, 0f, ClsHair),
            D("ресница",              32,24,22, 32,24,22, .1f, 2f, .1f, 0f, ClsHair),
            D("кожа со щетиной",      222,168,128, 196,110,104, .05f, 8.5f, .1f, .6f, ClsSoft),
            D("воздух",               0,0,0, 0,0,0, 0, 0, 0, 0, ClsAir),
            D("свернувшаяся кровь",   74,22,20, 70,20,18, .10f, 3f, .5f, 0f, ClsSoft),
            D("размозжённая ткань",   150,30,34, 150,30,34, .25f, 2f, .9f, .7f, ClsSoft),
            D("кровь",                110,8,12, 110,8,12, .10f, .5f, 1f, 0f, ClsLiquid),
            D("занято другим телом",  0,0,0, 0,0,0, 0, 0, 0, 0, ClsAir),
            D("водянистая влага",     214,224,220, 210,222,218, .02f, 1f, 1f, 0f, ClsLiquid),
            D("тень полости",         34,16,18, 34,16,18, .05f, 0, .3f, 0f, ClsAir),
        };

        /// Структуры тоньше шага решётки, утолщённые до одного вокселя (HEAD-SPEC, раздел 1). [название, настоящая толщина, в модели]
        public static readonly string[,] Thickened =
        {
            { "кожа (эпидермис + дерма)", "1,0–2,0 мм", "1 воксель головы, 2 мм" },
            { "надкостница", "≈0,5 мм", "2 мм" },
            { "апоневроз и фасции", "1–2 мм", "2 мм" },
            { "твёрдая оболочка, серп, намёт", "0,5–2 мм", "2 мм" },
            { "стенки глазницы (дно, внутренняя)", "0,2–1 мм", "2 мм" },
            { "стенки пазух, решётчатые перегородки", "0,2–1 мм", "2 мм" },
            { "чешуя височной кости", "1,4–2,3 мм", "2 мм (только компакта)" },
            { "носовые кости, хрящи носа", "0,8–3 мм", "2 мм" },
            { "слизистая носа и рта", "0,3–3 мм", "2 мм" },
            { "средняя менингеальная и поверхностная височная артерии", "1–2 мм", "2 мм" },
            { "ресницы", "≈0,1 мм", "ряд вокселей 2 мм" },
            { "склера", "0,5–1 мм", "1 воксель глаза, 1 мм" },
            { "роговица", "0,5 мм", "1 мм" },
            { "эмаль у шейки зуба", "0,1–0,5 мм", "1 воксель зуба, 1 мм" },
            { "межпозвонковый диск", "4–6 мм", "2 мм" },
        };

        public static byte Class(byte m) => m < Count ? Table[m].cls : ClsEmpty;
        public static bool Solid(byte m) => m != Empty && m != Air && m != Covered;
        /// Сплошная для отсечения граней (Covered закрывает грани соседей).
        public static bool Cull(byte m) => m != Empty && m != Air;
        /// Прозрачна для сетки: роговица и водянистая влага (не рисуются и не закрывают грани за собой).
        public static bool Clear(byte m) => m == Cornea || m == Aqueous;
    }

    /// Номера частей (второй байт вокселя): кость, мышца или сосуд. Бит 7 у кости — шов.
    public static class AnatPart
    {
        public const byte None = 0,
            Frontal = 1, ParietalL = 2, ParietalR = 3, TemporalL = 4, TemporalR = 5, Occipital = 6, Sphenoid = 7, Ethmoid = 8,
            NasalL = 9, NasalR = 10, ZygomaticL = 11, ZygomaticR = 12, MaxillaL = 13, MaxillaR = 14, Palatine = 15, Vomer = 16,
            Mandible = 17, Vertebra = 18, Hyoid = 19,
            Frontalis = 32, Occipitalis = 33, OrbOculi = 34, OrbOris = 35, ZygMajor = 36, ZygMinor = 37, Buccinator = 38, Mentalis = 39,
            Masseter = 40, Temporalis = 41, Platysma = 42, MedPterygoid = 43, LatPterygoid = 44, RectusSup = 45, RectusInf = 46,
            RectusMed = 47, RectusLat = 48, ObliqueSup = 49, ObliqueInf = 50, NeckDeep = 51, Scm = 52, SoftPalate = 53, TongueM = 54,
            Trapezius = 55, Strap = 56, Esophagus = 57, LarynxM = 58,
            SupTemporalA = 64, FacialA = 65, ScalpV = 66, NasalMucosaV = 67, MiddleMeningealA = 68, SagittalSinus = 69, CarotidA = 70,
            JugularV = 71, InfAlveolarA = 72, TransverseSinus = 73,
            Suture = 0x80;

        public static string Name(int p)
        {
            switch (p & 0x7F)
            {
                case Frontal: return "лобная"; case ParietalL: return "теменная Л"; case ParietalR: return "теменная П";
                case TemporalL: return "височная Л"; case TemporalR: return "височная П"; case Occipital: return "затылочная";
                case Sphenoid: return "клиновидная"; case Ethmoid: return "решётчатая"; case NasalL: return "носовая Л"; case NasalR: return "носовая П";
                case ZygomaticL: return "скуловая Л"; case ZygomaticR: return "скуловая П"; case MaxillaL: return "верхняя челюсть Л";
                case MaxillaR: return "верхняя челюсть П"; case Palatine: return "нёбная"; case Vomer: return "сошник"; case Mandible: return "нижняя челюсть";
                case Vertebra: return "позвонок"; case Frontalis: return "лобная мышца"; case Occipitalis: return "затылочная мышца";
                case OrbOculi: return "круговая глаза"; case OrbOris: return "круговая рта"; case ZygMajor: return "большая скуловая";
                case ZygMinor: return "малая скуловая"; case Buccinator: return "щёчная"; case Mentalis: return "подбородочная";
                case Masseter: return "жевательная"; case Temporalis: return "височная мышца"; case Platysma: return "подкожная шеи";
                case MedPterygoid: return "медиальная крыловидная"; case LatPterygoid: return "латеральная крыловидная";
                case RectusSup: return "верхняя прямая"; case RectusInf: return "нижняя прямая"; case RectusMed: return "медиальная прямая";
                case RectusLat: return "латеральная прямая"; case ObliqueSup: return "верхняя косая"; case ObliqueInf: return "нижняя косая";
                case NeckDeep: return "мышцы шеи"; case Scm: return "грудино-ключично-сосцевидная"; case SoftPalate: return "мягкое нёбо";
                case TongueM: return "язык";
                case Trapezius: return "трапециевидная"; case Strap: return "подподъязычные"; case Esophagus: return "пищевод"; case LarynxM: return "мышцы гортани";
                case SupTemporalA: return "поверхностная височная артерия"; case FacialA: return "лицевая артерия"; case ScalpV: return "сосуды кожи головы";
                case NasalMucosaV: return "сосуды слизистой носа"; case MiddleMeningealA: return "средняя менингеальная артерия";
                case SagittalSinus: return "верхний сагиттальный синус"; case CarotidA: return "сонная артерия"; case JugularV: return "яремная вена";
                case InfAlveolarA: return "нижняя альвеолярная артерия"; case TransverseSinus: return "поперечный синус";
            }
            return "";
        }

        public static bool IsBone(int p) { p &= 0x7F; return p >= 1 && p < 32; }
        public static bool IsMuscle(int p) => p >= 32 && p < 64;
    }

    /// Зоны (как R_* в вебе, плюс висок и затылок): уязвимость кожи к рассечению ×100, склонность к отёку ×100, предел отёка (см), кровоточивость.
    public static class AnatZone
    {
        public const byte None = 0, Scalp = 1, Fore = 2, Brow = 3, Eye = 4, Nose = 5, CheekBone = 6, Cheek = 7, Mouth = 8, Jaw = 9, Ear = 10, Neck = 11,
            Temple = 12, Occiput = 13, Count = 14;
        public static readonly string[] Names = { "", "свод", "лоб", "бровь", "глаз", "нос", "скула", "щека", "рот", "челюсть", "ухо", "шея", "висок", "затылок" };
        public static readonly byte[] Vuln = { 0, 60, 90, 200, 130, 180, 180, 70, 190, 120, 100, 80, 110, 55 };
        public static readonly byte[] Swell = { 0, 70, 110, 160, 220, 120, 150, 110, 160, 80, 150, 60, 90, 60 };
        public static readonly float[] SwellCap = { 0, .5f, .9f, .75f, .85f, .55f, .75f, .65f, .6f, .5f, .7f, .35f, .6f, .45f };
        public static readonly float[] Bleed = { 0, 3f, 3f, 3.2f, 2.2f, 2.6f, 2f, 1.6f, 2.6f, 1.4f, 1.6f, 1f, 3f, 2.6f };
    }
}
