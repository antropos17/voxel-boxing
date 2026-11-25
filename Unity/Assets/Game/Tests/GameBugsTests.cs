// GameBugsTests.cs — мелкие баги игрового слоя (Unity/GAME-BUGS.md): рывок перчатки соперника в начале удара,
// вид кнопок экрана поражения, переключатели настроек «ВКЛ / ВЫКЛ».
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame.Tests
{
    [TestFixture]
    public class GameBugsTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        static Transform Find(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        // Удар соперника: перчатка не срывается с замаха на полметра за кадр и всё же доходит до цели
        // раньше проверки удара (0.13 с).
        [Test]
        public void FoeStrike_GloveDoesNotTeleport()
        {
            _root = new GameObject("t");
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var def = FighterGen.Generate(42u, 1);
            var body = new FoeBody(_root.transform, def, mat);
            const float dt = 1f / 60f;
            Vector3 target = body.root.position + body.root.forward * 1.2f + Vector3.up * 1.55f;
            var g = Find(body.root, "glove0");
            Assert.NotNull(g);
            for (int i = 0; i < 40; i++) body.Tick(new AICommand { newPhase = FightPhase.Windup, side = -1 }, dt, target);
            Vector3 last = g.position;
            float maxStep = 0f, step = 0f;
            // до проверки удара (0.13 с ≈ 8 кадров): перчатка вытягивается до упора руки и встаёт
            for (int i = 0; i < 8; i++)
            {
                body.Tick(new AICommand { newPhase = FightPhase.Strike, side = -1 }, dt, target);
                step = (g.position - last).magnitude;
                maxStep = Mathf.Max(maxStep, step);
                last = g.position;
            }
            Assert.LessOrEqual(maxStep, 0.26f, "шаг перчатки за кадр");
            Assert.Less(step, 0.03f, "к проверке удара перчатка уже на месте, последний шаг " + step);
            body.Dispose();
            Object.DestroyImmediate(mat);
        }

        // Экран поражения: «ЕЩЁ РАЗ» — главная кнопка (оранжевая, крупный кегль), после победы — обычная.
        [Test]
        public void ResultScreen_RematchStyleFollowsOutcome()
        {
            _root = new GameObject("res", typeof(RectTransform));
            var rs = _root.AddComponent<ResultScreen>();
            typeof(ResultScreen).GetMethod("BuildUI", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rs, null);
            var btn = Find(_root.transform, "BtnRematch");
            var lbl = btn.GetComponentInChildren<Text>();

            rs.Show(false, 14f, 0, 0f, 20);
            Assert.AreEqual(UIKit.FontLg, lbl.fontSize);
            Assert.AreEqual(UIKit.Accent, btn.GetComponent<Image>().color);

            rs.Show(true, 30f, 1, 1f, 200);
            Assert.AreEqual(UIKit.FontMd, lbl.fontSize);
            Assert.AreEqual(UIKit.BgPanel, btn.GetComponent<Image>().color);
            Assert.AreEqual(UIKit.FontLg, Find(_root.transform, "BtnNext").GetComponentInChildren<Text>().fontSize);
        }

        // Настройки: «выключено» видно (ползунок слева, «ВЫКЛ», тёмная подложка), состояние из сохранения
        // восстанавливается на экране, нажатие переключает вид обратно.
        [Test]
        public void SettingsSwitch_OffStateVisible_AndRestoredFromSave()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "voxsw_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "save.json");
            try
            {
                var f = new GameFlow(path, 7u); f.Boot();
                f.OpenSettings();
                Assert.IsTrue(f.SetSoundEnabled(false));
                var g = new GameFlow(path, 8u);   // перезапуск игры: профиль из сохранения

                _root = new GameObject("set", typeof(RectTransform));
                var ss = _root.AddComponent<SettingsScreen>();
                typeof(SettingsScreen).GetMethod("BuildUI", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ss, null);
                ss.SetData(g.Profile, true);
                Assert.IsFalse(ss.SwitchShownOn(true));
                Assert.AreEqual("ВЫКЛ", ss.SwitchCaption(true));
                Assert.IsTrue(ss.SwitchShownOn(false), "вибрация включена");
                Assert.AreEqual("ВКЛ", ss.SwitchCaption(false));
                var soundBtn = Find(Find(_root.transform, "Sound"), "Toggle");
                Assert.AreEqual(SettingsScreen.SwitchOffBg, soundBtn.GetComponent<Image>().color);

                bool? sent = null;
                ss.OnSound = v => sent = v;
                ss.PressSound();
                Assert.IsTrue(ss.SwitchShownOn(true));
                Assert.AreEqual("ВКЛ", ss.SwitchCaption(true));
                Assert.AreEqual(SettingsScreen.SwitchOnBg, soundBtn.GetComponent<Image>().color);
                Assert.AreEqual(true, sent);
            }
            finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
        }
    }
}
