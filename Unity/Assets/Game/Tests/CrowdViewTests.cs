// CrowdViewTests.cs — предел зрителей и смена настроения без рендера.
using NUnit.Framework;
using UnityEngine;

namespace VoxGame.Tests
{
    [TestFixture]
    public class CrowdViewTests
    {
        GameObject _parent;
        CrowdView _crowd;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("CrowdTestParent");
            _crowd = new CrowdView();
        }

        [TearDown]
        public void TearDown()
        {
            _crowd.Dispose();
            Object.DestroyImmediate(_parent);
        }

        [Test]
        public void DefaultStands_WithinLimit()
        {
            _crowd.Build(_parent.transform, CrowdStands.Default(Vector3.zero, 45f));
            Assert.Greater(_crowd.Count, 0);
            Assert.LessOrEqual(_crowd.Count, CrowdView.MaxCrowd);
            Assert.LessOrEqual(CrowdView.MaxCrowd, 300);
        }

        [Test]
        public void HugeStands_ClampedToLimit()
        {
            var st = CrowdStands.Default(Vector3.zero, 0f);
            st.rows = 40; st.fill = 1f; st.halfW0 = 12f;
            _crowd.Build(_parent.transform, st);
            Assert.AreEqual(CrowdView.MaxCrowd, _crowd.Count);
        }

        [Test]
        public void Rebuild_DoesNotAccumulate()
        {
            var st = CrowdStands.Default(Vector3.zero, 45f);
            _crowd.Build(_parent.transform, st);
            int first = _crowd.Count;
            _crowd.Dispose();
            _crowd.Build(_parent.transform, st);
            Assert.AreEqual(first, _crowd.Count);
        }

        [Test]
        public void Mood_And_Tick_Work()
        {
            _crowd.Build(_parent.transform, CrowdStands.Default(Vector3.zero, 45f));
            Assert.AreEqual(CrowdMood.Idle, _crowd.Mood);
            _crowd.SetMood(CrowdMood.Cheer);
            _crowd.Burst(2f);
            for (int i = 0; i < 30; i++) _crowd.Tick(1f / 60f, 1f / 60f);
            Assert.AreEqual(CrowdMood.Cheer, _crowd.Mood);
        }
    }
}
