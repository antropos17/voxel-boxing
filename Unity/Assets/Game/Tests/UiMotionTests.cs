// UiMotionTests.cs — шкалы боя и движение интерфейса (Unity/UI-MOTION.md): след урона догоняет значение,
// предел летящих цифр и искр, кнопки нажимаются во время перехода, «меньше движения» гасит тряску и вспышки,
// подсчёт цифр кончается той же строкой, что без анимации.
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame.Tests
{
    [TestFixture]
    public class UiMotionTests
    {
        const float DT = 1f / 60f;
        GameObject _camGo, _rootGo;
        UIRoot _root;

        [SetUp]
        public void SetUp()
        {
            UIMotion.CompleteAll();
            UIMotion.ForceAnimate = true;
            UIMotion.Reduced = false;
            _camGo = new GameObject("cam");
            var cam = _camGo.AddComponent<Camera>();
            _rootGo = new GameObject("UIRoot");
            _root = _rootGo.AddComponent<UIRoot>();
            _root.Init(cam);
        }

        [TearDown]
        public void TearDown()
        {
            UIMotion.CompleteAll();
            UIMotion.ForceAnimate = false;
            UIMotion.Reduced = false;
            if (_rootGo != null) Object.DestroyImmediate(_rootGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            foreach (var es in Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
        }

        FightHUD NewHud(out HudData d)
        {
            var go = new GameObject("FightHUD");
            var hud = go.AddComponent<FightHUD>();
            hud.Init(_root);
            _root.ShowScreen(ScreenId.Fight);
            d = new HudData { playerHp = 100f, playerHpMax = 100f, foeHp = 120f, foeHpMax = 120f, playerStamina = 100f,
                              foeName = "Кирпич", playerName = "Игрок", staminaHitCost = 12f };
            hud.Refresh(d);
            Run(hud, ref d, 1f);   // выезд шкал закончился
            return hud;
        }

        static void Run(FightHUD hud, ref HudData d, float seconds)
        {
            for (float t = 0f; t < seconds; t += DT) { hud.Refresh(d); hud.Tick(DT, DT); UIMotion.Tick(DT); }
        }

        // След урона: сразу после удара стоит на прежнем значении, через ~0.3 с начинает догонять и за секунду догоняет.
        [Test]
        public void DamageTrail_CatchesUpWithValue()
        {
            var hud = NewHud(out var d);
            Assert.AreEqual(1f, hud.FoeValue, 1e-4f);
            d.foeHp = 84f;   // −36 (30 %)
            hud.Refresh(d); hud.Tick(DT, DT);
            Assert.AreEqual(0.7f, hud.FoeValue, 1e-3f);
            Assert.AreEqual(1f, hud.FoeTrail, 1e-3f, "след стоит на прежнем значении");
            Run(hud, ref d, FightHUD.TrailHold - 0.05f);
            Assert.Greater(hud.FoeTrail, 0.95f, "в пределах удержания след ещё не двинулся");
            Run(hud, ref d, 0.2f);
            Assert.Less(hud.FoeTrail, 0.999f, "след пошёл к значению");
            Assert.Greater(hud.FoeTrail, hud.FoeValue, "но ещё не догнал");
            Run(hud, ref d, 0.6f);
            Assert.AreEqual(hud.FoeValue, hud.FoeTrail, 1e-4f, "след догнал значение");
            // восстановление (лечение) — без следа
            d.foeHp = 120f; hud.Refresh(d);
            Assert.AreEqual(hud.FoeValue, hud.FoeTrail, 1e-4f);
        }

        // Предел: летящих цифр не больше MaxFloaters, искр — не больше MaxSparks, при любом потоке ударов.
        [Test]
        public void Floaters_And_Sparks_AreCapped()
        {
            var hud = NewHud(out var d);
            int maxF = 0, maxS = 0;
            d.megaReady = true; d.megaMeter = 100f; d.foeStun01 = 0.95f;
            for (int i = 0; i < 240; i++)
            {
                // каждые 2 кадра удар по сопернику и через раз — по игроку (окно сложения цифр не спасает: стороны чередуются)
                if (i % 2 == 0) d.foeHp = d.foeHp > 10f ? d.foeHp - 9f : 120f;
                if (i % 3 == 0) d.playerHp = d.playerHp > 12f ? d.playerHp - 7f : 25f;
                d.playerStamina = i % 4 == 0 ? 0f : 100f;
                hud.Refresh(d); hud.Tick(DT, DT); UIMotion.Tick(DT);
                maxF = Mathf.Max(maxF, hud.LiveFloaters);
                maxS = Mathf.Max(maxS, hud.LiveSparks);
            }
            Assert.LessOrEqual(maxF, FightHUD.MaxFloaters);
            Assert.LessOrEqual(maxS, FightHUD.MaxSparks);
            Assert.Greater(maxF, 0, "цифры вообще появлялись");
            Assert.Greater(maxS, 0, "искры вообще появлялись");
            int floats = 0;
            foreach (var t in hud.GetComponentsInChildren<Text>(true)) if (t.name.StartsWith("F") && t.transform.parent.name == "Fx") floats++;
            Assert.AreEqual(FightHUD.MaxFloaters, floats, "пул цифр не растёт");
        }

        // Переход экранов: у нового экрана кнопка нажимается с первого кадра (ещё прозрачный), старый лучей не ловит и гаснет.
        [Test]
        public void Buttons_ActiveDuringTransition()
        {
            var a = NewScreen(ScreenId.MainMenu, out var btnA);
            var b = NewScreen(ScreenId.Settings, out var btnB);
            _root.ShowScreen(ScreenId.MainMenu);
            for (int i = 0; i < 30; i++) UIMotion.Tick(DT);
            int pressed = 0;
            btnB.onClick.AddListener(() => pressed++);

            _root.ShowScreen(ScreenId.Settings);
            var gb = b.GetComponent<CanvasGroup>();
            var ga = a.GetComponent<CanvasGroup>();
            Assert.Less(gb.alpha, 0.5f, "новый экран ещё проявляется");
            Assert.IsTrue(UIMotion.IsAnimating(gb), "переход идёт");
            Assert.IsTrue(gb.blocksRaycasts && gb.interactable, "новый экран принимает касания сразу");
            Assert.IsTrue(btnB.IsInteractable() && btnB.isActiveAndEnabled, "кнопка активна");
            btnB.onClick.Invoke();
            Assert.AreEqual(1, pressed, "нажатие прошло без ожидания конца перехода");
            Assert.IsTrue(a.activeSelf, "старый экран ещё гаснет");
            Assert.IsFalse(ga.blocksRaycasts, "уходящий экран касания не ловит");
            UIMotion.Tick(0.05f);
            Assert.Greater(gb.alpha, 0f);
            for (int i = 0; i < 30; i++) UIMotion.Tick(DT);
            Assert.IsFalse(a.activeSelf, "старый экран выключен");
            Assert.AreEqual(1f, gb.alpha, 1e-4f);
            Assert.AreEqual(Vector3.one, b.transform.localScale);
            Assert.AreEqual(Vector2.zero, ((RectTransform)b.transform).offsetMin);
            Assert.AreEqual(1f, btnB.transform.localScale.x, 1e-4f);
        }

        GameObject NewScreen(ScreenId id, out Button btn)
        {
            var go = new GameObject(id.ToString(), typeof(RectTransform));
            MenuKit.Root(go);
            MenuKit.Backdrop(go.transform, UIKit.BgDark);
            UIKit.Label(go.transform, "Title", UIKit.FontLg, UIKit.Accent).text = "ЗАГОЛОВОК";
            btn = MenuKit.Primary(go.transform, "Btn", "ДАЛЕЕ", UIKit.FontLg);
            MenuKit.Place(btn.GetComponent<RectTransform>(), 0.1f, 0.1f, 0.9f, 0.2f);
            _root.Register(id, go);
            return go;
        }

        // «Меньше движения»: удар не трясёт панель и не даёт вспышки; без настройки — трясёт и вспыхивает.
        [Test]
        public void ReducedMotion_DisablesShakeAndFlash()
        {
            float jolt, flash;
            Hit(false, out jolt, out flash);
            Assert.Greater(jolt, 0.5f, "обычно сильный удар трясёт панель");
            Assert.Greater(flash, 0.1f, "и даёт вспышку");
            TearDown(); SetUp();
            Hit(true, out jolt, out flash);
            Assert.AreEqual(0f, jolt, 1e-4f, "без тряски");
            Assert.AreEqual(0f, flash, 1e-4f, "без вспышки");
        }

        void Hit(bool reduced, out float maxJolt, out float maxFlash)
        {
            UIMotion.Reduced = reduced;
            var hud = NewHud(out var d);
            maxJolt = 0f; maxFlash = 0f;
            d.foeHp -= 30f; d.playerHp -= 25f; d.playerHp = Mathf.Max(d.playerHp, 50f);
            for (int i = 0; i < 40; i++)
            {
                hud.Refresh(d); hud.Tick(DT, DT); UIMotion.Tick(DT);
                maxJolt = Mathf.Max(maxJolt, hud.PanelJolt);
                maxFlash = Mathf.Max(maxFlash, hud.FlashAlpha);
            }
        }

        // Подсчёт: в движении строка набегает, в конце — ровно та, что без анимации; вне Play Mode — сразу конечная.
        [Test]
        public void CountUp_EndsWithFinalString()
        {
            var go = new GameObject("t", typeof(RectTransform));
            go.transform.SetParent(_rootGo.transform, false);
            var t = UIKit.Label(go.transform, "L", 20, Color.white);
            UIMotion.Count(t, 0, 180, "Награда: ", " монет");
            Assert.AreEqual("Награда: 0 монет", t.text);
            UIMotion.Tick(UIMotion.CountUp * 0.5f);
            Assert.AreNotEqual("Награда: 180 монет", t.text);
            UIMotion.Tick(UIMotion.CountUp);
            Assert.AreEqual("Награда: 180 монет", t.text);
            UIMotion.ForceAnimate = false;
            UIMotion.Count(t, 0, 7, "Нокдауны: ", "");
            Assert.AreEqual("Нокдауны: 7", t.text, "в тестах и проходе — без ожидания");
        }

        // Все длительности в рамках 120–350 мс.
        [Test]
        public void Durations_WithinBudget()
        {
            foreach (var v in new[] { UIMotion.PressIn, UIMotion.PressOut, UIMotion.ScreenIn, UIMotion.ScreenOut, UIMotion.ItemIn,
                                      UIMotion.CountUp, UIMotion.PopIn, FightHUD.IntroTime, FightHUD.JoltTime, FightHUD.TrailHold, FightHUD.TrailCatch })
            {
                Assert.GreaterOrEqual(v, 0.12f - 1e-4f);
                Assert.LessOrEqual(v, 0.35f + 1e-4f);
            }
            Assert.AreEqual(1f, UIMotion.Eval(Ease.OutBack, 1f), 1e-4f);
            Assert.AreEqual(0f, UIMotion.Eval(Ease.OutCubic, 0f), 1e-4f);
        }
    }
}
