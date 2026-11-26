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

            p.BloodBurst(Vector3.up, Vector3.forward, 2000, 2f);
            Assert.LessOrEqual(p.AliveCount, FxParticles.MaxParticles);
            Assert.LessOrEqual(FxParticles.MaxParticles, 640);
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
                for (int i = 0; i < 8; i++) b.ShowCenter(FxBanners.MegaNames[i % FxBanners.MegaNames.Length], BannerStyle.Power);
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
            Assert.AreSame(FxPops.ComboText(4), FxPops.ComboText(4));
            Assert.AreEqual("КОМБО ×4", FxPops.ComboText(4));
            Assert.AreSame(FxBanners.NumberText(37), FxBanners.NumberText(37));
            Assert.AreEqual("−37", FxBanners.NumberText(37));
            Assert.AreEqual("ХУК −12", FxPops.PopText((int)PunchType.Hook, false, 12));
            Assert.AreEqual("ВСТРЕЧНЫЙ АППЕРКОТ −7", FxPops.PopText((int)PunchType.Upper, true, 7));
            Assert.AreSame(FxPops.PopText(0, false, 3), FxPops.PopText(0, false, 3));
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

        // ---- Удар: кольца, кровь, надписи ---------------------------------

        static FxDirector BuildFx(out GameObject scene, out GameObject canvas, out GameObject camGo)
        {
            scene = new GameObject("FxScene");
            canvas = new GameObject("FxCanvas", typeof(RectTransform));
            camGo = new GameObject("FxCam");
            var cam = camGo.AddComponent<Camera>();
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 1.62f, 1.4f), Quaternion.Euler(4f, 180f, 0f));
            var fx = new FxDirector();
            fx.Build(scene.transform, canvas.transform, cam);
            fx.SetView(0.4615f);
            return fx;
        }

        static void Free(FxDirector fx, GameObject a, GameObject b, GameObject c)
        {
            fx.Dispose();
            Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c);
        }

        // Серия ударов подряд (каждый кадр, мега через раз): пулы частиц, колец и надписей не выходят за пределы,
        // а после прогрева удар и кадр эффектов не выделяют память.
        [Test]
        public void HitSeries_PoolsCapped_NoAllocation()
        {
            var fx = BuildFx(out var a, out var b, out var c);
            try
            {
                for (int i = 0; i < 120; i++)
                {
                    var h = Hit(i % 3 == 0 ? PunchType.Jab : i % 3 == 1 ? PunchType.Hook : PunchType.Upper, mega: i % 2 == 0, power: 1.4f);
                    h.punch = i % 3 == 0 ? PunchType.Jab : PunchType.Hook;
                    h.fracture = i % 4 == 0; h.headBurst = i % 9 == 0; h.counter = i % 5 == 0; h.combo = i;
                    fx.OnPlayerHit(h);
                    fx.OnFracture(i % 7);
                    fx.SyncInjuries(i / 10, i > 50, i / 60);
                    fx.Tick(Dt);
                    Assert.LessOrEqual(fx.Particles.AliveCount, FxParticles.MaxParticles);
                    Assert.LessOrEqual(fx.Particles.DrawnCount, FxParticles.MaxParticles);
                    Assert.LessOrEqual(fx.Rings.Active, FxRings.MaxRings);
                    Assert.LessOrEqual(fx.Pops.ActivePops, 1, "на экране одна плашка удара");
                    Assert.LessOrEqual(fx.Pops.ActiveChips, FxPops.MaxChips);
                    Assert.LessOrEqual(fx.Banners.ActiveBanners, FxBanners.MaxBanners);
                }
                Assert.AreEqual(FxParticles.MaxParticles, fx.Particles.AliveCount, "серия мег заполнила пул");

                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 60; i++)
                {
                    var h = Hit(i % 2 == 0 ? PunchType.Hook : PunchType.Jab, mega: i % 6 == 0, power: 1.3f);
                    h.punch = i % 2 == 0 ? PunchType.Hook : PunchType.Jab;
                    h.combo = i; h.damage = i;
                    fx.OnPlayerHit(h);
                    fx.Tick(Dt);
                }
                long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual(0, bytes, "удар и кадр эффектов без выделений памяти");

                Run(fx, 6f);
                Assert.AreEqual(0, fx.Particles.AliveCount);
                Assert.AreEqual(0, fx.Rings.Active);
                Assert.AreEqual(0, fx.Pops.ActivePops);
                Assert.IsFalse(fx.Pops.ComboVisible);
            }
            finally { Free(fx, a, b, c); }
        }

        // Сила удара: джеб — одно кольцо, без линий скорости и вспышки; апперкот — два кольца, линии; мега — три, виньетка.
        [Test]
        public void Impact_ScalesWithPower()
        {
            var fx = BuildFx(out var a, out var b, out var c);
            try
            {
                var jab = Hit(PunchType.Jab, power: 0.4f); jab.punch = PunchType.Jab; jab.damage = 3f;
                fx.OnPlayerHit(jab);
                Assert.AreEqual(2, fx.Rings.Active, "кольцо и диск");
                Assert.AreEqual(0f, fx.Overlay.LinesAlpha);
                Assert.AreEqual(0f, fx.Overlay.FlashAlpha);
                Assert.AreEqual("ДЖЕБ −3", fx.Pops.LastPopText);
                int jabBlood = fx.Particles.Count(FxKind.Blood) + fx.Particles.Count(FxKind.Mist);
                fx.Reset();

                var up = Hit(PunchType.Upper, power: 1.3f); up.punch = PunchType.Upper; up.damage = 11f; up.combo = 3;
                fx.OnPlayerHit(up);
                Assert.AreEqual(3, fx.Rings.Active);
                Assert.Greater(fx.Overlay.LinesAlpha, 0.3f);
                Assert.Greater(fx.Overlay.FlashAlpha, 0.05f);   // белая вспышка есть (FxDirector.WhiteFlash)
                Assert.AreEqual("АППЕРКОТ −11", fx.Pops.LastPopText);
                fx.Tick(Dt);
                Assert.AreEqual("КОМБО ×3", fx.Pops.ComboShown);
                int upBlood = fx.Particles.Count(FxKind.Blood) + fx.Particles.Count(FxKind.Mist);
                Assert.Greater(upBlood, jabBlood * 2);
                fx.Reset();

                var mega = Hit(PunchType.Mega, mega: true, power: 1.3f); mega.punch = PunchType.Hook; mega.damage = 30f;
                fx.OnPlayerHit(mega);
                Assert.AreEqual(4, fx.Rings.Active);
                Assert.AreEqual("МЕГА-ХУК!", fx.Banners.LastBannerText);
                Assert.Greater(fx.Particles.Count(FxKind.Fire), 0);
                Assert.Greater(fx.Particles.Count(FxKind.Blood) + fx.Particles.Count(FxKind.Mist), upBlood * 2);
                fx.Tick(0.15f);
                Assert.Greater(fx.Overlay.MegaAlpha, 0.15f);   // виньетка мега ещё горит (пик FxScreen.MegaA0)
            }
            finally { Free(fx, a, b, c); }
        }

        // Без эффектов (Visuals = false) представление не строится, а время и камера — те же, что с эффектами.
        [Test]
        public void VisualsOff_SameTimeAndCamera()
        {
            var on = BuildFx(out var a, out var b, out var c);
            bool was = FxDirector.Visuals;
            FxDirector.Visuals = false;
            var off = BuildFx(out var a2, out var b2, out var c2);
            FxDirector.Visuals = was;
            try
            {
                Assert.IsNull(off.Particles);
                Assert.IsNull(off.Rings);
                for (int i = 0; i < 90; i++)
                {
                    if (i % 15 == 0)
                    {
                        var h = Hit(i % 30 == 0 ? PunchType.Hook : PunchType.Jab, mega: i == 60, power: 1.2f);
                        on.OnPlayerHit(h); off.OnPlayerHit(h);
                    }
                    if (i == 40) { on.OnKnockdown(); off.OnKnockdown(); }
                    Assert.AreEqual(on.Tick(Dt), off.Tick(Dt));
                    Assert.AreEqual(on.FovKick, off.FovKick);
                    Assert.AreEqual(on.CamRoll, off.CamRoll);
                    Assert.AreEqual(on.CamOffset, off.CamOffset);
                }
            }
            finally { Free(on, a, b, c); Free(off, a2, b2, c2); }
        }

        // Бой на фиксированном seed с эффектами и без: исход, урон и разрушение головы побитно те же.
        [Test]
        public void FightOutcome_SameWithAndWithoutEffects()
        {
            string with = VoxGame.Editor.ShotsTaker.HitFxOutcome(true, 24);
            string without = VoxGame.Editor.ShotsTaker.HitFxOutcome(false, 24);
            StringAssert.Contains("landed=", with);
            Assert.AreEqual(with, without);
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

        // HIT-FX-2: брызги летят от точки удара по направлению удара, не уходят вверх и к объективу, тяжесть роняет их.
        [Test]
        public void BloodBurst_FollowsPunch_FallsDown()
        {
            var at = new Vector3(0f, 1.6f, 0f);
            var toCam = Vector3.back;   // камера на −Z, смотрит на +Z: экран вправо — +X
            var buf = new Vector3[FxParticles.MaxParticles];
            foreach (int kind in new[] { FxParticles.BurstJab, FxParticles.BurstHook, FxParticles.BurstUpper })
            {
                var p = new FxParticles(null);
                Vector3 dir = kind == FxParticles.BurstHook ? Vector3.left : kind == FxParticles.BurstUpper ? Vector3.up : Vector3.forward;
                p.BloodBurst(at, dir, toCam, 60, kind, FxDirector.SpdPower, 2f);
                for (int i = 0; i < 6; i++) p.Tick(Dt);   // 0,1 с
                int n = p.CopyBlood(0u, buf);
                Vector3 m = Vector3.zero; float back = 0f;
                for (int i = 0; i < n; i++) { m += buf[i] - at; back = Mathf.Min(back, buf[i].z - at.z); }
                m /= Mathf.Max(1, n);
                if (kind == FxParticles.BurstHook) Assert.Less(m.x, -0.03f, "хук: облако уходит вбок по удару");
                if (kind == FxParticles.BurstUpper) Assert.Greater(m.y, 0.03f, "апперкот: облако уходит вверх");
                if (kind == FxParticles.BurstJab) Assert.Less(m.z, -0.01f, "джеб: облако к камере");
                Assert.Greater(back, -FxParticles.ToCamMax * 0.1f - FxParticles.SpawnLead - 0.02f, "к камере не быстрее ToCamMax");
                for (int i = 0; i < 24; i++) p.Tick(Dt);  // 0,5 с
                n = p.CopyBlood(0u, buf);
                int high = 0;
                for (int i = 0; i < n; i++) if (buf[i].y > at.y + 0.2f) high++;
                Assert.LessOrEqual(high, n / 10, "через 0,5 с капли не выше точки удара больше чем на 20 см (вид " + kind + ")");
            }
        }

        // HIT-FX-2, приёмка на настоящем бою (портрет 1080×2400 и ландшафт 2400×1080): кровь у точки удара на 0,15 с,
        // через 0,5 с не выше; плашка не под перчатками, контрастная, внутри safe-area, не на полосах HUD и счётчике комбо.
        [Test]
        public void HitFx2_Criteria()
        {
            VoxGame.Editor.ShotsTaker.HitFx2("", false);
            var rs = VoxGame.Editor.ShotsTaker.HitFx2Results;
            Assert.AreEqual(6, rs.Count);
            foreach (var r in rs)
            {
                string w = (r.land ? "ландшафт " : "портрет ") + r.punch;
                Assert.IsTrue(r.hit, w + ": удар дошёл");
                Assert.GreaterOrEqual(r.in15Frac, 0.6f, w + ": кровь в круге 0,2·W на 0,15 с");
                Assert.LessOrEqual(r.after05HighFrac + r.after05BehindFrac, 0.1f, w + ": через 0,5 с выше точки или за камерой");
                Assert.IsTrue(r.plateSeen, w + ": плашка видна");
                Assert.LessOrEqual(r.plateGlovePx, 0.05f, w + ": плашка под перчаткой (по пикселям перчаток)");
                Assert.LessOrEqual(r.plateDist, 0.22f, w + ": плашка дальше 0,22 меньшей стороны от точки удара");
                Assert.AreEqual(0f, r.ringOverText, 1e-4f, w + ": кольцо поверх текста плашки");
                // доработка 2: крови на 0,15 с больше; до неё (a8d7ad5) в портрете видимых 12 / 34 / 34, пикселей 93 / 5455 / 18766
                if (!r.land && r.punch == PunchType.Jab)  { Assert.GreaterOrEqual(r.peakVis, 30, w); Assert.GreaterOrEqual(r.bloodPx15, 279f, w); }
                if (!r.land && r.punch == PunchType.Hook) { Assert.GreaterOrEqual(r.peakVis, 85, w); Assert.GreaterOrEqual(r.bloodPx15, 16365f, w); }
                if (!r.land && r.punch == PunchType.Upper) { Assert.GreaterOrEqual(r.peakVis, 34, w); Assert.GreaterOrEqual(r.bloodPx15, 18766f, w); }
                Assert.GreaterOrEqual(r.plateContrastMin, 4.5f, w + ": контраст плашки");
                Assert.IsTrue(r.plateInSafe, w + ": плашка в safe-area");
                Assert.AreEqual(0f, r.plateHudFrac, 1e-4f, w + ": плашка на полосах HUD");
                Assert.AreEqual(0f, r.plateComboFrac, 1e-4f, w + ": плашка на счётчике комбо");
            }
        }
    }
}
