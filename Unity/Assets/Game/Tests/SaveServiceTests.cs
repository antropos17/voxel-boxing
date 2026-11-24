using NUnit.Framework;
using System.IO;
using VoxGame;

namespace VoxGame.Tests {

    [TestFixture]
    public class SaveServiceTests {

        string _tempDir;

        [SetUp]
        public void SetUp() {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "VoxGameSaveTest_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempDir);
            SaveService.DefaultPath = Path.Combine(_tempDir, "save.json");
        }

        [TearDown]
        public void TearDown() {
            SaveService.DefaultPath = null;
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        // ==================== Базовые сохранение/загрузка ====================

        [Test]
        public void SaveAndLoad_RoundTrip_AllFields() {
            var d = new SaveData {
                playerName      = "Тест",
                playerLevel     = 5,
                xp              = 350,
                coins           = 1200,
                wins            = 7,
                losses          = 2,
                beltCount       = 1,
                ladderIndex     = 3,
                soundEnabled    = false,
                vibrationEnabled= true,
                equippedGloveId = "heavy",
                boughtGloveIds  = new string[] { "classic", "heavy" },
                upgradeLevels   = new int[] { 2, 1, 0, 3, 1 },
            };

            SaveService.Save(d);
            var loaded = SaveService.Load();

            Assert.AreEqual("Тест",    loaded.playerName);
            Assert.AreEqual(5,         loaded.playerLevel);
            Assert.AreEqual(350,       loaded.xp);
            Assert.AreEqual(1200,      loaded.coins);
            Assert.AreEqual(7,         loaded.wins);
            Assert.AreEqual(2,         loaded.losses);
            Assert.AreEqual(1,         loaded.beltCount);
            Assert.AreEqual(3,         loaded.ladderIndex);
            Assert.AreEqual(false,     loaded.soundEnabled);
            Assert.AreEqual(true,      loaded.vibrationEnabled);
            Assert.AreEqual("heavy",   loaded.equippedGloveId);
            Assert.AreEqual(2, loaded.boughtGloveIds.Length);
            Assert.AreEqual(new int[] { 2, 1, 0, 3, 1 }, loaded.upgradeLevels);
        }

        [Test]
        public void SaveData_HasVersion() {
            var d = new SaveData();
            SaveService.Save(d);
            var loaded = SaveService.Load();
            Assert.Greater(loaded.version, 0);
        }

        // ==================== Защита от отсутствия файла ====================

        [Test]
        public void Load_MissingFile_ReturnsDefault() {
            // Файл не создан — Load должен вернуть дефолт
            var d = SaveService.Load();
            Assert.IsNotNull(d);
            Assert.AreEqual(1, d.playerLevel);
            Assert.AreEqual(0, d.coins);
            Assert.AreEqual("classic", d.equippedGloveId);
        }

        // ==================== Защита от битого файла ====================

        [Test]
        public void Load_CorruptJson_ReturnsDefault() {
            File.WriteAllText(SaveService.DefaultPath, "{ БИТЫЙ JSON !!!");
            var d = SaveService.Load();
            Assert.IsNotNull(d);
            Assert.AreEqual(1, d.playerLevel);
        }

        [Test]
        public void Load_EmptyFile_ReturnsDefault() {
            File.WriteAllText(SaveService.DefaultPath, "");
            var d = SaveService.Load();
            Assert.IsNotNull(d);
            Assert.AreEqual(1, d.playerLevel);
        }

        [Test]
        public void Load_RandomBinaryData_ReturnsDefault() {
            File.WriteAllBytes(SaveService.DefaultPath, new byte[] { 0xFF, 0xFE, 0x00, 0x01 });
            var d = SaveService.Load();
            Assert.IsNotNull(d);
        }

        // ==================== Путь задаётся параметром ====================

        [Test]
        public void ExplicitPath_SavesAndLoadsToGivenFile() {
            string customPath = Path.Combine(_tempDir, "custom_slot.json");
            var d = new SaveData { coins = 999, playerName = "Слот2" };
            SaveService.Save(d, customPath);
            Assert.IsTrue(File.Exists(customPath));

            var loaded = SaveService.Load(customPath);
            Assert.AreEqual(999, loaded.coins);
            Assert.AreEqual("Слот2", loaded.playerName);
        }

        [Test]
        public void ExplicitPath_DoesNotAffectDefaultPath() {
            string other = Path.Combine(_tempDir, "other.json");
            SaveService.Save(new SaveData { coins = 777 }, other);

            // Дефолтный путь пустой → вернёт дефолтный профиль
            var d = SaveService.Load(); // DefaultPath
            Assert.AreEqual(0, d.coins);
        }

        // ==================== Миграция: classic всегда куплен ====================

        [Test]
        public void Migration_EnsuresClassicInBoughtGloves() {
            // Старый формат — boughtGloveIds не задан (null в JSON)
            var raw = "{\"version\":1,\"playerName\":\"Старый\",\"playerLevel\":1,\"coins\":50}";
            File.WriteAllText(SaveService.DefaultPath, raw);
            var d = SaveService.Load();
            Assert.IsNotNull(d.boughtGloveIds);
            Assert.IsTrue(System.Array.IndexOf(d.boughtGloveIds, "classic") >= 0);
        }

        // ==================== Интеграция с PlayerProfile ====================

        [Test]
        public void ProfileSaveTo_LoadFrom_RoundTrip() {
            var profile = new PlayerProfile {
                playerName = "Борец",
                level = 3,
                coins = 450,
                wins = 5,
                losses = 1,
                ladderIndex = 4,
            };
            profile.AddXp(300);
            profile.BuyUpgrade(UpgradeType.Power);

            var saveData = new SaveData();
            profile.SaveTo(saveData);
            SaveService.Save(saveData);

            var loaded = SaveService.Load();
            var profile2 = new PlayerProfile();
            profile2.LoadFrom(loaded);

            Assert.AreEqual(profile.playerName,  profile2.playerName);
            Assert.AreEqual(profile.level,       profile2.level);
            Assert.AreEqual(profile.coins,       profile2.coins);
            Assert.AreEqual(profile.wins,        profile2.wins);
            Assert.AreEqual(profile.ladderIndex, profile2.ladderIndex);
            Assert.AreEqual(profile.upgradeLevels[(int)UpgradeType.Power],
                            profile2.upgradeLevels[(int)UpgradeType.Power]);
        }
    }
}
