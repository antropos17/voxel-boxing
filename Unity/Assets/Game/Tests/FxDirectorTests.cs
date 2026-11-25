// FxDirectorTests.cs — стоп-кадр с антиспамом, ход времени, пределы пулов эффектов, затухание камеры.
using NUnit.Framework;
using UnityEngine;

namespace VoxGame.Tests
{
    [TestFixture]
    public class FxDirectorTests
    {
        const float Dt = 1f / 60f;

        static void Run(FxDirector fx, float seconds)
        {
            int n = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < n; i++) fx.Tick(Dt);
        }

        static HitFx Hit(PunchType kind, bool mega = false, float power = 1f)
        {
            return new HitFx
            {
                kind = kind, side = 1, power = power, mega = mega, zone = -1, damage = 12f, combo = 1,
                point = new Vector3(0f, 1.6f, 0.1f), dir = new Vector3(0f, 0f, -1f),
            };
        }

        // ---- Время ---------------------------------------------------------

        [Test]
        public void HitStop_FirstStop_IsFull()
        {
            var fx = new FxDirector();
            fx.HitStop(0.08f, 0.20f);
            Assert.AreEqual(0.08f, fx.FreezeLeft, 1e-5f);
            Assert.AreEqual(0.20f, fx.SlowLeft, 1e-5f);
        }

        [Test]
        public void HitStop_AntiSpam_FrequentHitsGetShorterStops()
        {
            var fx = new FxDirector();
            fx.HitStop(0.08f, 0.20f);
            Run(fx, 0.4f);                                   // стоп и слоу-мо прошли
            Assert.AreEqual(0f, fx.FreezeLeft);
            Assert.AreEqual(0f, fx.SlowLeft);

            fx.HitStop(0.08f, 0.20f);                        // через 0.4 с: половина
            float q = Mathf.Clamp(fx.Clock / FxDirector.StopQWindow, FxDirector.StopQMin, 1f);
            Assert.Less(fx.FreezeLeft, 0.08f);
            Assert.AreEqual(0.08f * q, fx.FreezeLeft, 1e-4f);
            Assert.AreEqual(0.20f * q, fx.SlowLeft, 1e-4f);

            Run(fx, 0.3f);
            fx.HitStop(0.08f, 0f);                           // сразу ещё раз: самый короткий
            Run(fx, 0.2f);
            fx.HitStop(0.08f, 0f);
            Assert.AreEqual(0.08f * 0.25f, fx.FreezeLeft, 1e-4f);

            Run(fx, 1f);                                     // пауза дольше окна — снова полный
            fx.HitStop(0.08f, 0f);
            Assert.AreEqual(0.08f, fx.FreezeLeft, 1e-5f);
        }

        [Test]
        public void HitStop_Full_BypassesAntiSpam()
        {
            var fx = new FxDirector();
            fx.HitStop(0.02f, 0f);
            Run(fx, 0.05f);
            fx.HitStop(FxDirector.KnockoutFreeze, FxDirector.KnockoutSlow, true);
            Assert.AreEqual(FxDirector.KnockoutFreeze, fx.FreezeLeft, 1e-5f);
            Assert.AreEqual(FxDirector.KnockoutSlow, fx.SlowLeft, 1e-5f);
        }

        [Test]
        public void TimeScale_ZeroInFreeze_SlowAfter_ThenNormal()
        {
            var fx = new FxDirector();
            Assert.AreEqual(1f, fx.Tick(Dt));
            fx.HitStop(0.1f, 0.5f, true);

            int frozen = 0;
            while (fx.FreezeLeft > 0f)
            {
                Assert.AreEqual(0f, fx.Tick(Dt), "в стоп-кадре мир стоит");
                Assert.AreEqual(0f, fx.TimeScale);
                frozen++;
                Assert.Less(frozen, 20);
            }
            Assert.GreaterOrEqual(frozen, 5);

            Assert.AreEqual(FxDirector.SlowScale, fx.Tick(Dt), 1e-6f);
            Assert.AreEqual(0.28f, fx.TimeScale, 1e-6f);
            Run(fx, 0.6f);
            Assert.AreEqual(1f, fx.Tick(Dt));
        }

        [Test]
        public void Events_UseWebTable()
        {
            var fx = new FxDirector();
            fx.OnPlayerHit(Hit(PunchType.Jab, power: 0.4f));
            Assert.AreEqual(0.03f, fx.FreezeLeft, 1e-5f);
            Assert.AreEqual(0f, fx.SlowLeft);

            fx = new FxDirector();
            fx.OnPlayerHit(Hit(PunchType.Mega, mega: true));
            Assert.AreEqual(0.16f, fx.FreezeLeft, 1e-5f);
            Assert.AreEqual(0.60f, fx.SlowLeft, 1e-5f);

            fx = new FxDirector();
            fx.OnPlayerHit(Hit(PunchType.Hook, power: 1f));
            Assert.AreEqual(0.08f, fx.FreezeLeft, 1e-5f);
            Assert.AreEqual(0.20f, fx.SlowLeft, 1e-5f);

            fx = new FxDirector();
            fx.OnKnockdown();
            Assert.AreEqual(0f, fx.FreezeLeft);
            Assert.AreEqual(0.90f, fx.SlowLeft, 1e-5f);

            fx = new FxDirector();
            fx.OnKnockout();
            Assert.AreEqual(0.20f, fx.FreezeLeft, 1e-5f);
            Assert.AreEqual(1.10f, fx.SlowLeft, 1e-5f);
        }

        // ---- Камера --------------------------------------------------------

        [Test]
        public void Shake_Fov_Roll_Decay()
        {
            var fx = new FxDirector();
            fx.OnPlayerHit(Hit(PunchType.Mega, mega: true));
            fx.OnPlayerHit(Hit(PunchType.Hook, power: 1.3f));
            fx.OnFoeHit(1f, false);
            fx.Tick(Dt);
            Assert.Greater(fx.ShakeAmp, 0.05f);
            Assert.Greater(fx.FovKick, 5f);
            Assert.Greater(fx.CamOffset.magnitude, 0f);

            Run(fx, 0.3f);
            float mid = fx.ShakeAmp;
            Assert.Less(mid, FxDirector.MegaShake * 0.2f);

            Run(fx, 2.5f);
            Assert.AreEqual(0f, fx.ShakeAmp);
            Assert.Less(fx.FovKick, 0.01f);
            Assert.Less(Mathf.Abs(fx.CamRoll), 0.05f);
            Assert.Less(fx.CamOffset.magnitude, 1e-3f);
        }

        // ---- Добивание -----------------------------------------------------

        [Test]
        public void FinisherWindow_Rules()
        {
            Assert.IsTrue(FxDirector.IsFinisherWindow(0.15f, 0f, 0, FightPhase.Guard));
            Assert.IsTrue(FxDirector.IsFinisherWindow(0.30f, 0.8f, 0, FightPhase.Idle));
            Assert.IsTrue(FxDirector.IsFinisherWindow(0.30f, 0f, 0, FightPhase.Stagger));
            Assert.IsTrue(FxDirector.IsFinisherWindow(0.40f, 0f, 2, FightPhase.Guard));
            Assert.IsFalse(FxDirector.IsFinisherWindow(0.60f, 0.9f, 1, FightPhase.Stagger));
            Assert.IsFalse(FxDirector.IsFinisherWindow(0.40f, 0f, 1, FightPhase.Guard));
            Assert.IsFalse(FxDirector.IsFinisherWindow(0.10f, 0f, 0, FightPhase.Down));
            Assert.IsFalse(FxDirector.IsFinisherWindow(0.10f, 0f, 0, FightPhase.KO));
        }

        [Test]
        public void Knockout_InFinisherWindow_IsFinisher()
        {
            var fx = new FxDirector();
            fx.SyncFoe(0.1f, 0.5f, 1, FightPhase.Guard, new Vector3(0f, 1.6f, 0f));
            Assert.IsTrue(fx.FinisherReady);
            fx.OnKnockout();
            Assert.IsFalse(fx.FinisherReady);
            Assert.AreEqual(FxDirector.FinisherFreeze, fx.FreezeLeft, 1e-5f);
            Assert.AreEqual(FxDirector.FinisherSlow, fx.SlowLeft, 1e-5f);
            fx.Tick(Dt);
            float expect = FxDirector.FinisherFovK * Mathf.Exp(-FxDirector.FovDecay * Dt) * FxDirector.FovPerK;
            Assert.AreEqual(Mathf.Min(FxDirector.FovKickMax, expect), fx.FovKick, 0.05f);   // сильный наезд
        }

        // ---- Частицы -------------------------------------------------------

        [Test]
        public void Particles_PoolCapped_EvictsOldest()
        {
            var p = new FxParticles(null);
            for (int i = 0; i < FxParticles.MaxParticles; i++)
                p.Spawn(FxKind.Spark, Vector3.up, Vector3.zero, 0.01f, 10f, new Color32(255, 255, 255, 255));
            Assert.AreEqual(FxParticles.MaxParticles, p.AliveCount);
            Assert.IsTrue(p.IsAlive(0));

            p.Spawn(FxKind.Spark, Vector3.up, Vector3.zero, 0.01f, 10f, new Color32(255, 255, 255, 255));
            Assert.AreEqual(FxParticles.MaxParticles, p.AliveCount);
            Assert.IsFalse(p.IsAlive(0), "вытеснена самая старая");
            Assert.IsTrue(p.IsAlive((uint)FxParticles.MaxParticles));
            Assert.AreEqual(1u, p.OldestAliveSeq());

            p.BloodBurst(Vector3.up, Vector3.forward, 500, 2f);
            Assert.LessOrEqual(p.AliveCount, 200);
            Assert.LessOrEqual(FxParticles.MaxParticles, 200);
        }

        [Test]
        public void Particles_Die_And_Mesh_Fixed_Size()
        {
            var parent = new GameObject("FxTestParent");
            var p = new FxParticles(parent.transform);
            try
            {
                p.BloodBurst(new Vector3(0f, 1.6f, 0f), Vector3.back, 40, 2f);
                p.Sparks(new Vector3(0f, 1.6f, 0f), Vector3.back, 10, 1f);
                p.EmitStars(new Vector3(0f, 1.6f, 0f), 1f);
                p.Tick(Dt);
                Assert.Greater(p.AliveCount, 0);
                Assert.AreEqual(FxParticles.MaxParticles * 24, p.Mesh.vertexCount);
                for (int i = 0; i < 300; i++) p.Tick(Dt);
                Assert.AreEqual(0, p.AliveCount);
                Assert.AreEqual(FxParticles.MaxParticles * 24, p.Mesh.vertexCount);
            }
            finally
            {
                p.Dispose();
                Object.DestroyImmediate(parent);
            }
        }

        // ---- Надписи и накладки --------------------------------------------

        [Test]
        public void Banners_And_Numbers_Capped()
        {
            var root = new GameObject("FxTestRoot", typeof(RectTransform));
            var b = new FxBanners(root.transform, null);
            try
            {
                for (int i = 0; i < 8; i++) b.ShowCenter(FxBanners.HookWords[i % FxBanners.HookWords.Length], BannerStyle.Power);
                b.ShowCenter(FxBanners.Crushed, BannerStyle.Huge);
                Assert.AreEqual(FxBanners.MaxBanners, b.ActiveBanners);
                Assert.LessOrEqual(FxBanners.MaxBanners, 5);
                Assert.AreEqual(FxBanners.Crushed, b.LastBannerText);

                for (int i = 0; i < 30; i++) b.ShowNumber(i, Vector3.zero, i % 3);
                Assert.AreEqual(FxBanners.MaxNumbers, b.ActiveNumbers);
                Assert.LessOrEqual(FxBanners.MaxNumbers, 12);

                for (int i = 0; i < 200; i++) b.Tick(Dt);
                Assert.AreEqual(0, b.ActiveBanners);
                Assert.AreEqual(0, b.ActiveNumbers);
            }
            finally
            {
                b.Dispose();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PrebuiltStrings_NoAllocationPerCall()
        {
            Assert.AreSame(FxBanners.ComboText(4), FxBanners.ComboText(4));
            Assert.AreEqual("СЕРИЯ ×4", FxBanners.ComboText(4));
            Assert.AreSame(FxBanners.NumberText(37), FxBanners.NumberText(37));
            Assert.AreEqual("37", FxBanners.NumberText(37));
            Assert.AreEqual(FxBanners.NumberText(FxBanners.NumberTextMax), FxBanners.NumberText(100000));
        }

        [Test]
        public void Screen_Splats_Capped_And_Fade()
        {
            var root = new GameObject("FxTestRoot", typeof(RectTransform));
            var s = new FxScreen(root.transform);
            try
            {
                s.Splat(10);
                Assert.AreEqual(FxScreen.MaxSplats, s.ActiveSplats);
                s.Flash(1f);
                s.Cinema(0.5f, 1f);
                for (int i = 0; i < 30; i++) s.Tick(Dt);
                Assert.Greater(s.Bars, 0.9f);
                Assert.Less(s.FlashAlpha, 0.05f);
                for (int i = 0; i < 300; i++) s.Tick(Dt);
                Assert.AreEqual(0, s.ActiveSplats);
                Assert.AreEqual(0f, s.Bars);
            }
            finally
            {
                s.Dispose();
                Object.DestroyImmediate(root);
            }
        }

        // ---- Сборка целиком ------------------------------------------------

        [Test]
        public void Build_FullFight_StaysWithinLimits()
        {
            var scene = new GameObject("FxScene");
            var canvas = new GameObject("FxCanvas", typeof(RectTransform));
            var camGo = new GameObject("FxCam");
            var cam = camGo.AddComponent<Camera>();
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 1.4f), Quaternion.Euler(10f, 180f, 0f));
            var fx = new FxDirector();
            try
            {
                fx.Build(scene.transform, canvas.transform, cam);
                for (int i = 0; i < 40; i++)
                {
                    var h = Hit(i % 3 == 0 ? PunchType.Jab : PunchType.Hook, mega: i % 10 == 9, power: 1.2f);
                    h.combo = i % 7; h.counter = i % 5 == 0; h.zone = i % 7;
                    fx.OnPlayerHit(h);
                    if (i % 4 == 0) fx.OnFoeHit(0.8f, i % 8 == 0);
                    fx.SyncFoe(1f - i / 40f, 0.7f, i / 15, FightPhase.Stagger, new Vector3(0f, 1.6f, 0f));
                    fx.Tick(Dt);
                    Assert.LessOrEqual(fx.Particles.AliveCount, FxParticles.MaxParticles);
                    Assert.LessOrEqual(fx.Banners.ActiveBanners, FxBanners.MaxBanners);
                    Assert.LessOrEqual(fx.Banners.ActiveNumbers, FxBanners.MaxNumbers);
                    Assert.LessOrEqual(fx.Overlay.ActiveSplats, FxScreen.MaxSplats);
                }
                fx.OnKnockout();
                Run(fx, 4f);
                fx.Reset();
                Assert.AreEqual(0, fx.Particles.AliveCount);
                Assert.AreEqual(1f, fx.Tick(Dt));
            }
            finally
            {
                fx.Dispose();
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(scene);
            }
        }
    }
}
