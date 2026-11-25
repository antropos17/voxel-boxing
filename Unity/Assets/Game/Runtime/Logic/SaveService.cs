using System;
using System.IO;
using UnityEngine;

namespace VoxGame {

    /// <summary>
    /// JSON-сохранение через JsonUtility.
    /// DefaultPath: null = Application.persistentDataPath/save.json.
    /// Для тестов задать DefaultPath = Path.Combine(tmpDir, "save.json") перед вызовом.
    /// </summary>
    public static class SaveService {

        // 1 — первый формат; 2 — громкости masterVolume/sfxVolume/crowdVolume.
        public const int CurrentVersion = 2;

        /// <summary>
        /// Переопределение пути. null — использовать Application.persistentDataPath.
        /// В тестах подставить временную папку.
        /// </summary>
        public static string DefaultPath;

        static string ResolvePath(string path) =>
            path
            ?? DefaultPath
            ?? (Application.persistentDataPath + "/save.json");

        // --- Загрузка ---

        /// <summary>
        /// Загрузить SaveData из JSON. Файл не найден или битый → new SaveData() с дефолтами.
        /// Битый файл откладывается рядом (save.json.bad), чтобы следующее сохранение не стёрло его без следа.
        /// </summary>
        public static SaveData Load(string path = null) {
            string p = ResolvePath(path);
            if (!File.Exists(p)) return new SaveData();
            try {
                string json = File.ReadAllText(p);
                if (string.IsNullOrWhiteSpace(json)) return new SaveData();
                var d = JsonUtility.FromJson<SaveData>(json);
                if (d == null) return new SaveData();
                Migrate(d);
                return d;
            } catch {
                KeepBroken(p);
                return new SaveData();
            }
        }

        static void KeepBroken(string p) {
            try { File.Copy(p, p + ".bad", true); } catch { }
        }

        // --- Сохранение ---

        /// <summary>
        /// Записать SaveData в JSON. Создаёт папку при необходимости.
        /// Пишет во временный файл и затем подменяет: обрыв записи не портит прежнее сохранение.
        /// </summary>
        public static void Save(SaveData d, string path = null) {
            string p = ResolvePath(path);
            string dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            d.version = CurrentVersion;
            string json = JsonUtility.ToJson(d, prettyPrint: false);
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, json);
            File.Copy(tmp, p, true);
            File.Delete(tmp);
        }

        // --- Миграция ---

        // Привести данные из старых сохранений к актуальному формату.
        static void Migrate(SaveData d) {
            // Classic всегда куплен; boughtGloveIds мог быть null в очень старом файле
            if (d.boughtGloveIds == null || d.boughtGloveIds.Length == 0)
                d.boughtGloveIds = new string[] { "classic" };

            if (string.IsNullOrEmpty(d.equippedGloveId))
                d.equippedGloveId = "classic";

            if (d.upgradeLevels == null || d.upgradeLevels.Length != 5)
                d.upgradeLevels = new int[5];

            if (string.IsNullOrEmpty(d.playerName))
                d.playerName = "Игрок";

            // версия 1 громкостей не знала: полная громкость (вкл/выкл — по soundEnabled)
            if (d.version < 2) {
                d.masterVolume = 1f;
                d.sfxVolume = 1f;
                d.crowdVolume = 1f;
            }
            d.masterVolume = Volume01(d.masterVolume);
            d.sfxVolume = Volume01(d.sfxVolume);
            d.crowdVolume = Volume01(d.crowdVolume);

            d.playerLevel = Mathf.Max(1, d.playerLevel);
            d.coins = Mathf.Max(0, d.coins);
            d.xp = Mathf.Max(0, d.xp);
            d.wins = Mathf.Max(0, d.wins);
            d.losses = Mathf.Max(0, d.losses);
            d.beltCount = Mathf.Max(0, d.beltCount);
            d.ladderIndex = Mathf.Max(0, d.ladderIndex);
        }

        // Громкость 0..1; мусор (NaN, бесконечность) — полная.
        public static float Volume01(float v) {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 1f;
            return Mathf.Clamp01(v);
        }
    }
}
