using System.Collections.Generic;
using NUnit.Framework;
using VoxProbe;

namespace VoxGame.Tests
{
    // Мимика соперника в бою (Unity/EMOTIONS.md): событие боя → ожидаемое выражение, настрой по здоровью, характеры бойцов лестницы.
    public class FoeFaceTests
    {
        static FighterDef Ladder(int i) => new RosterDef(GameFlow.RosterSeed).Get(i);

        static FightState Hp(float frac) { var st = new FightState(); st.hpMax = 100f; st.hp = 100f * frac; return st; }

        static int After(FoeFace ff, float sec, float hp = 1f)
        {
            var st = Hp(hp);
            for (float t = 0; t < sec; t += 1f / 60f) ff.Tick(1f / 60f, st);
            return ff.face.Dominant;
        }

        static FoeFace Face(int presetIdx)
        {
            // боец с нужным характером: ищем в лестнице, иначе подменяем прозвище
            var f = Ladder(0);
            var ff = new FoeFace(f);
            ff.face.temper = AnatTemper.Presets[presetIdx];
            ff.face.blinks = false;
            ff.face.Snap();
            return ff;
        }

        [Test]
        public void HitGivesPainThenDazed()
        {
            var ff = Face(0);
            ff.OnHit(1, false, (int)AnatZone.Cheek, false, false, 1);
            Assert.AreEqual(AnatEmo.Pain, After(ff, 0.2f), "силовой удар — боль");
            ff = Face(0);
            ff.OnHit(2, true, (int)AnatZone.Jaw, true, false, -1);
            Assert.AreEqual(AnatEmo.Pain, After(ff, 0.2f), "мега — боль");
            Assert.AreEqual(AnatEmo.Dazed, After(ff, 1.0f), "затем оглушён");
            ff = Face(0);
            ff.OnHit(0, false, (int)AnatZone.Skull, false, false, 1);
            ff.Tick(1f / 60f, Hp(1f)); ff.Tick(1f / 60f, Hp(1f));
            float jab = ff.face.w[AnatEmo.Pain];
            var ff2 = Face(0); ff2.OnHit(1, false, (int)AnatZone.Orbit, false, false, 1);
            After(ff2, 0.2f); After(ff, 0.2f - 2f / 60f);
            Assert.Greater(ff2.face.w[AnatEmo.Pain], ff.face.w[AnatEmo.Pain] + 0.15f, "сила удара: джеб слабее силового в глаз");
            Assert.Greater(ff2.face.ch[AnatFace.LidUpL], ff2.face.ch[AnatFace.LidUpR], "место: глаз со стороны удара зажмурен сильнее");
            Assert.Greater(jab, 0f);
        }

        [Test]
        public void TauntsByTemper()
        {
            var bully = Face(1); bully.OnPlayerMiss();
            Assert.AreEqual(AnatEmo.Smirk, After(bully, 0.4f), "задира ухмыляется на промах");
            var show = Face(3); show.OnFoeLanded(false, true);
            Assert.AreEqual(AnatEmo.Triumph, After(show, 0.4f), "пробил блок — торжество");
            var angry = Face(2); angry.OnWindup();
            Assert.AreEqual(AnatEmo.Anger, After(angry, 0.4f), "злой в замахе злится");
            angry.OnStrike();
            Assert.AreEqual(AnatEmo.Rage, After(angry, 0.2f), "выпад — оскал");
            var cold = Face(0); cold.OnWindup();
            Assert.AreEqual(AnatEmo.Focus, After(cold, 0.4f), "хладнокровный в замахе сосредоточен");
            var nerv = Face(4); nerv.OnDodged();
            Assert.AreEqual(AnatEmo.Surprise, After(nerv, 0.3f), "нервный удивлён уклону");
        }

        [Test]
        public void KnockdownRiseKoFinale()
        {
            var ff = Face(6);
            ff.OnKnockdown();
            Assert.AreEqual(AnatEmo.Dazed, After(ff, 5f), "нокдаун — оглушён, держится");
            ff.OnPlayerMiss();
            Assert.AreEqual(AnatEmo.Dazed, After(ff, 0.5f), "мелочь не перебивает нокдаун");
            ff.OnRise();
            Assert.AreEqual(AnatEmo.Tired, After(ff, 0.6f), "подъём — усталость");
            Assert.AreEqual(AnatEmo.Anger, After(ff, 1.5f), "затем злость (зверь)");
            ff.OnKnockout();
            Assert.AreEqual(AnatEmo.Dazed, After(ff, 1f), "нокаут");
            ff.OnFinale(false);
            Assert.AreEqual(AnatEmo.Defeat, After(ff, 1f), "поражение");
            var w = Face(3); w.OnFinale(true);
            Assert.AreEqual(AnatEmo.Triumph, After(w, 1f), "победа соперника — торжество");
        }

        [Test]
        public void LowHpMood()
        {
            var ff = Face(0);
            Assert.AreEqual(AnatEmo.Tired, After(ff, 1f, 0.15f), "мало здоровья — усталость");
            Assert.Greater(ff.face.fatigue, 0.8f);
            var nerv = Face(4);
            Assert.AreEqual(AnatEmo.Fear, After(nerv, 1f, 0.06f), "нервный при почти нуле — страх");
            // дыхание ртом: челюсть ходит
            float mn = 9, mx = -9; var st = Hp(0.15f);
            for (int i = 0; i < 120; i++) { ff.Tick(1f / 60f, st); mn = System.Math.Min(mn, ff.face.ch[AnatFace.Jaw]); mx = System.Math.Max(mx, ff.face.ch[AnatFace.Jaw]); }
            Assert.Greater(mx - mn, 0.15f, "дыхание ртом");
        }

        [Test]
        public void LadderTempersVaried()
        {
            var r = new RosterDef(GameFlow.RosterSeed);
            var names = new HashSet<string>(); var moods = new HashSet<int>();
            for (int i = 0; i < RosterDef.Count; i++) { var t = FighterGen.TemperOf(r.Get(i)); names.Add(t.name); moods.Add(t.mood); Assert.Greater(t.gain, 0.4f); }
            Assert.GreaterOrEqual(names.Count, 6, "характеров в лестнице");
            Assert.GreaterOrEqual(moods.Count, 3, "разных настроев покоя");
            var a = FighterGen.TemperOf(r.Get(0)); var b = FighterGen.TemperOf(r.Get(0));
            Assert.AreEqual(a.gain, b.gain, "детерминированно");
        }
    }
}
