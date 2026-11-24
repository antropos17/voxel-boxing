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
                return new SaveData();
            }
        }

        // --- Сохранение ---

        /// <summary>Записать SaveData в JSON. Создаёт папку при необходимости.</summary>
        public static void Save(SaveData d, string path = null) {
            string p = ResolvePath(path);
            string dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            d.version = 1;
            string json = JsonUtility.ToJson(d, prettyPrint: false);
            File.WriteAllText(p, json);
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

            d.playerLevel = Mathf.Max(1, d.playerLevel);
            d.coins = Mathf.Max(0, d.coins);
            d.xp = Mathf.Max(0, d.xp);
            d.wins = Mathf.Max(0, d.wins);
            d.losses = Mathf.Max(0, d.losses);
            d.beltCount = Mathf.Max(0, d.beltCount);
            d.ladderIndex = Mathf.Max(0, d.ladderIndex);
        }
    }
}
