using UnityEngine;

namespace VoxGame
{
    // Звуковые события боя. Порядок совпадает с SoundBank.Defs (проверяется тестом).
    public enum SoundId
    {
        JabHead,        // джеб в голову
        HookHead,       // хук в голову
        UpperHead,      // апперкот в челюсть
        OverHead,       // самый тяжёлый обычный удар (силовой прямой)
        Mega,           // мега-удар
        Flurry,         // удар мега-серии
        Swing,          // свист замаха сильного удара
        Miss,           // промах игрока
        Whoosh,         // удар соперника летит в лицо
        Block,          // блок: перчатка о перчатку
        Dodge,          // уклон
        GuardBreak,     // пробитый блок
        Hurt,           // пропущенный прямой
        HurtHook,       // пропущенный хук
        Graze,          // вскользь
        CrackNose,      // хруст носа
        CrackCheek,     // хруст скулы / глазницы
        CrackJaw,       // перелом челюсти
        CrackSkull,     // череп
        Thud,           // падение тела на ринг
        RefCount,       // счёт рефери (хлопок по настилу)
        Bell,           // гонг
        Hall,           // гул зала, петля
        CrowdKnockdown, // зал на нокдауне
        CrowdKnockout,  // зал на нокауте: рёв и аплодисменты
        Win,            // победа
        Lose,           // поражение
        Coin,           // монета
        Ui,             // кнопка
        FoeWarn,        // сигнал замаха соперника
        Ready,          // мега-удар готов
        Charge,         // замах мега-удара
        Combo,          // серия (высоту поднимать через pitchMul)
        Star,           // звезда на экране итога
    }

    public enum SoundBus { Sfx, Crowd }

    // Описание события: числа перенесены из assets/sounds/manifest.json.
    public sealed class SoundDef
    {
        public readonly SoundId id;
        public readonly string[] files;   // имена ресурсов в Resources/Audio (без расширения)
        public readonly float gainDb;     // поправка громкости, дБ
        public readonly float pitchVar;   // разброс высоты, ± полутона
        public readonly float volVar;     // разброс громкости: vol *= 1 - rnd*volVar
        public readonly int priority;     // 0..7, больше — важнее
        public readonly int maxVoices;    // предел одновременных голосов события
        public readonly bool loop;
        public readonly SoundBus bus;

        public SoundDef(SoundId id, string[] files, float pitchVar, float volVar, int priority, int maxVoices,
                        SoundBus bus = SoundBus.Sfx, bool loop = false, float gainDb = 0f)
        {
            this.id = id;
            this.files = files;
            this.pitchVar = pitchVar;
            this.volVar = volVar;
            this.priority = priority;
            this.maxVoices = maxVoices;
            this.bus = bus;
            this.loop = loop;
            this.gainDb = gainDb;
        }

        public float GainLinear => Mathf.Pow(10f, gainDb / 20f);
    }

    // Таблица событий и ленивая загрузка клипов из Resources/Audio с кэшем.
    public static class SoundBank
    {
        public const string ResourceDir = "Audio/";

        static string[] F(string ev, int n)
        {
            var r = new string[n];
            for (int i = 0; i < n; i++) r[i] = ev + "_" + (i + 1);
            return r;
        }

        //                                   id                        файлы                    pv    vv     pri voices
        public static readonly SoundDef[] Defs =
        {
            new SoundDef(SoundId.JabHead,        F("jab_head", 4),    1.2f, 0.15f, 3, 3),
            new SoundDef(SoundId.HookHead,       F("hook_head", 4),   1.0f, 0.12f, 4, 3),
            new SoundDef(SoundId.UpperHead,      F("upper_head", 4),  1.0f, 0.12f, 4, 3),
            new SoundDef(SoundId.OverHead,       F("over_head", 4),   1.0f, 0.10f, 4, 3),
            new SoundDef(SoundId.Mega,           F("mega", 2),        0.6f, 0.06f, 6, 2),
            new SoundDef(SoundId.Flurry,         F("flurry", 2),      1.5f, 0.12f, 5, 3),
            new SoundDef(SoundId.Swing,          F("swing", 4),       1.5f, 0.15f, 2, 2),
            new SoundDef(SoundId.Miss,           F("miss", 3),        1.5f, 0.15f, 2, 2),
            new SoundDef(SoundId.Whoosh,         F("whoosh", 3),      1.5f, 0.12f, 3, 2),
            new SoundDef(SoundId.Block,          F("block", 4),       1.5f, 0.15f, 3, 3),
            new SoundDef(SoundId.Dodge,          F("dodge", 3),       1.5f, 0.15f, 2, 1),
            new SoundDef(SoundId.GuardBreak,     F("guard_break", 2), 1.0f, 0.10f, 5, 1),
            new SoundDef(SoundId.Hurt,           F("hurt", 3),        1.0f, 0.10f, 5, 2),
            new SoundDef(SoundId.HurtHook,       F("hurt_hook", 3),   1.0f, 0.10f, 5, 2),
            new SoundDef(SoundId.Graze,          F("graze", 3),       1.5f, 0.15f, 4, 2),
            new SoundDef(SoundId.CrackNose,      F("crack_nose", 3),  1.5f, 0.10f, 5, 2),
            new SoundDef(SoundId.CrackCheek,     F("crack_cheek", 3), 1.5f, 0.10f, 5, 2),
            new SoundDef(SoundId.CrackJaw,       F("crack_jaw", 3),   1.2f, 0.10f, 5, 2),
            new SoundDef(SoundId.CrackSkull,     F("crack_skull", 3), 1.0f, 0.08f, 6, 2),
            new SoundDef(SoundId.Thud,           F("thud", 2),        1.0f, 0.08f, 6, 1),
            new SoundDef(SoundId.RefCount,       F("count", 3),       1.0f, 0.10f, 5, 1),
            new SoundDef(SoundId.Bell,           F("bell", 1),        0.2f, 0.04f, 7, 3),
            new SoundDef(SoundId.Hall,           F("hall", 1),        0f,   0f,    0, 1, SoundBus.Crowd, loop: true),
            new SoundDef(SoundId.CrowdKnockdown, F("crowd_kd", 1),    0.3f, 0.10f, 4, 1, SoundBus.Crowd),
            new SoundDef(SoundId.CrowdKnockout,  F("crowd_ko", 1),    0.2f, 0.05f, 6, 1, SoundBus.Crowd),
            new SoundDef(SoundId.Win,            F("win", 1),         0f,   0f,    7, 1),
            new SoundDef(SoundId.Lose,           F("lose", 1),        0f,   0f,    7, 1),
            new SoundDef(SoundId.Coin,           F("coin", 1),        0f,   0f,    3, 2),
            new SoundDef(SoundId.Ui,             F("ui", 2),          0.5f, 0.05f, 3, 1),
            new SoundDef(SoundId.FoeWarn,        F("tick", 2),        0.5f, 0.05f, 4, 1),
            new SoundDef(SoundId.Ready,          F("ready", 1),       0f,   0f,    5, 1),
            new SoundDef(SoundId.Charge,         F("charge", 2),      0.5f, 0.05f, 5, 1),
            new SoundDef(SoundId.Combo,          F("combo", 1),       0f,   0f,    2, 1),
            new SoundDef(SoundId.Star,           F("star", 1),        0f,   0f,    3, 2),
        };

        public static int Count => Defs.Length;

        public static SoundDef Get(SoundId id) => Defs[(int)id];

        // Кэш: клип и признак «уже пытались загрузить» (чтобы отсутствующий файл не грузить каждый раз).
        static AudioClip[][] _clips;
        static bool[][] _tried;

        // Клип варианта события; null, если файла нет в Resources/Audio.
        public static AudioClip GetClip(SoundId id, int variant)
        {
            var def = Defs[(int)id];
            if (variant < 0 || variant >= def.files.Length) return null;
            if (_clips == null)
            {
                _clips = new AudioClip[Defs.Length][];
                _tried = new bool[Defs.Length][];
            }
            int e = (int)id;
            if (_clips[e] == null)
            {
                _clips[e] = new AudioClip[def.files.Length];
                _tried[e] = new bool[def.files.Length];
            }
            // Клип мог быть выгружен (смена сцены, DestroyImmediate) — тогда грузим заново
            if (!_tried[e][variant] || (_clips[e][variant] == null && !object.ReferenceEquals(_clips[e][variant], null)))
            {
                _tried[e][variant] = true;
                _clips[e][variant] = Resources.Load<AudioClip>(ResourceDir + def.files[variant]);
            }
            return _clips[e][variant];
        }

        // Сбросить кэш (после переимпорта или в тестах).
        public static void ClearCache()
        {
            _clips = null;
            _tried = null;
        }
    }
}
