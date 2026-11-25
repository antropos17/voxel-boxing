using NUnit.Framework;
using UnityEngine;
using VoxGame;
using VoxGame.Editor;

namespace VoxGame.Tests
{
    [TestFixture]
    public class SoundPlayerTests
    {
        // --- таблица голосов ---

        [Test]
        public void Voice21_EvictsLowestPriority()
        {
            var t = new VoiceTable(SoundPlayer.MaxVoices);
            int lowSlot = -1;
            for (int i = 0; i < SoundPlayer.MaxVoices; i++)
            {
                int pri = i == 7 ? 1 : 4;
                int s = t.Allocate(i, pri, 3, out int ev);
                Assert.AreEqual(-1, ev);
                if (i == 7) lowSlot = s;
            }
            Assert.AreEqual(SoundPlayer.MaxVoices, t.ActiveCount);

            int slot = t.Allocate(100, 5, 3, out int evicted);
            Assert.AreEqual(lowSlot, slot);
            Assert.AreEqual(lowSlot, evicted);
            Assert.AreEqual(100, t.EventOf(slot));
            Assert.AreEqual(SoundPlayer.MaxVoices, t.ActiveCount);
        }

        [Test]
        public void Voice21_EqualPriority_EvictsOldest()
        {
            var t = new VoiceTable(SoundPlayer.MaxVoices);
            int first = t.Allocate(0, 3, 3, out _);
            for (int i = 1; i < SoundPlayer.MaxVoices; i++) t.Allocate(i, 3, 3, out _);

            int slot = t.Allocate(50, 3, 3, out int evicted);
            Assert.AreEqual(first, slot);
            Assert.AreEqual(first, evicted);
        }

        [Test]
        public void Voice21_LowerThanAll_IsDropped()
        {
            var t = new VoiceTable(SoundPlayer.MaxVoices);
            for (int i = 0; i < SoundPlayer.MaxVoices; i++) t.Allocate(i, 5, 3, out _);

            int slot = t.Allocate(50, 2, 3, out int evicted);
            Assert.AreEqual(-1, slot);
            Assert.AreEqual(-1, evicted);
            Assert.AreEqual(0, t.CountOf(50));
        }

        [Test]
        public void PerEventLimit_EvictsOldestOfSameEvent()
        {
            var t = new VoiceTable(SoundPlayer.MaxVoices);
            int a = t.Allocate(1, 3, 3, out _);
            t.Allocate(2, 0, 3, out _);           // чужое событие с меньшим приоритетом не трогается
            t.Allocate(1, 3, 3, out _);
            t.Allocate(1, 3, 3, out _);
            Assert.AreEqual(3, t.CountOf(1));

            int slot = t.Allocate(1, 3, 3, out int evicted);
            Assert.AreEqual(a, slot);
            Assert.AreEqual(a, evicted);
            Assert.AreEqual(3, t.CountOf(1));
            Assert.AreEqual(1, t.CountOf(2));
            Assert.AreEqual(4, t.ActiveCount);
        }

        [Test]
        public void Release_FreesSlot()
        {
            var t = new VoiceTable(4);
            int s = t.Allocate(1, 3, 2, out _);
            t.Release(s);
            Assert.AreEqual(0, t.ActiveCount);
            Assert.AreEqual(s, t.Allocate(2, 3, 2, out int ev));
            Assert.AreEqual(-1, ev);
        }

        // --- вариант и разброс ---

        [Test]
        public void PickVariant_NeverRepeatsPrevious_AndCoversAll()
        {
            var rng = new SoundRng(12345);
            var seen = new bool[4];
            int last = -1;
            for (int i = 0; i < 2000; i++)
            {
                int k = SoundPlayer.PickVariant(4, last, rng);
                Assert.That(k, Is.InRange(0, 3));
                Assert.AreNotEqual(last, k);
                seen[k] = true;
                last = k;
            }
            for (int i = 0; i < 4; i++) Assert.IsTrue(seen[i], "вариант " + i + " ни разу не выпал");
        }

        [Test]
        public void PickVariant_SingleFile_ReturnsZero()
        {
            var rng = new SoundRng(7);
            Assert.AreEqual(0, SoundPlayer.PickVariant(1, 0, rng));
            Assert.AreEqual(0, SoundPlayer.PickVariant(1, -1, rng));
        }

        [Test]
        public void Pitch_StaysWithinSemitoneSpread()
        {
            var rng = new SoundRng(99);
            foreach (var def in SoundBank.Defs)
            {
                float lo = Mathf.Pow(2f, -def.pitchVar / 12f) - 1e-5f;
                float hi = Mathf.Pow(2f, def.pitchVar / 12f) + 1e-5f;
                for (int i = 0; i < 300; i++)
                {
                    float p = SoundPlayer.PitchFor(def.pitchVar, rng.NextSigned());
                    Assert.That(p, Is.InRange(lo, hi), def.id.ToString());
                }
            }
            Assert.AreEqual(1f, SoundPlayer.PitchFor(0f, 0.7f), 1e-6f);
            Assert.AreEqual(Mathf.Pow(2f, 1.5f / 12f), SoundPlayer.PitchFor(1.5f, 1f), 1e-5f);
        }

        [Test]
        public void Volume_StaysWithinSpread()
        {
            var rng = new SoundRng(5);
            for (int i = 0; i < 500; i++)
            {
                float v = SoundPlayer.VolumeFor(0.15f, rng.Next01());
                Assert.That(v, Is.InRange(0.85f, 1f));
            }
        }

        // --- банк ---

        [Test]
        public void Bank_OrderMatchesEnum_AndValuesSane()
        {
            Assert.AreEqual(System.Enum.GetValues(typeof(SoundId)).Length, SoundBank.Count);
            for (int i = 0; i < SoundBank.Count; i++)
            {
                var d = SoundBank.Defs[i];
                Assert.AreEqual((SoundId)i, d.id);
                Assert.Greater(d.files.Length, 0, d.id.ToString());
                Assert.That(d.priority, Is.InRange(0, 7));
                Assert.GreaterOrEqual(d.maxVoices, 1);
                Assert.LessOrEqual(d.maxVoices, SoundPlayer.MaxVoices);
            }
            Assert.IsTrue(SoundBank.Get(SoundId.Hall).loop);
        }

        [Test]
        public void Bank_AllFilesPresentInResources()
        {
            var missing = AudioSetup.FindMissing(out int total);
            Assert.Greater(total, 0);
            Assert.IsEmpty(missing, "нет файлов: " + string.Join(",", missing));
        }

        // --- проигрыватель вне Play Mode ---

        [Test]
        public void Player_OutsidePlayMode_DoesNotThrow_AndRespectsVoiceLimit()
        {
            var parent = new GameObject("SoundTestRoot");
            var p = new SoundPlayer();
            try
            {
                p.Init(parent.transform, 42);
                p.SetVolumes(0.8f, 0.6f, 1f);
                p.StartLoop();
                for (int i = 0; i < 60; i++)
                {
                    p.Play((SoundId)(i % SoundBank.Count), 1f, 1f);
                    Assert.LessOrEqual(p.ActiveVoices, SoundPlayer.MaxVoices);
                }
                for (int i = 0; i < 10; i++) p.Tick(1f / 60f);
                p.Mute(true);
                Assert.IsFalse(p.Play(SoundId.JabHead));
                p.Mute(false);
                p.StopLoop();
                for (int i = 0; i < 120; i++) p.Tick(1f / 60f);
                Assert.IsFalse(p.LoopPlaying);
            }
            finally
            {
                p.Dispose();
                Object.DestroyImmediate(parent);
            }
            Assert.IsFalse(p.IsReady);
        }
    }
}
