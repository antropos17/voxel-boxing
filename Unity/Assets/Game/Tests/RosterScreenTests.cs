// RosterScreenTests.cs — состояния карточек лестницы и доступность боя без рендера.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VoxGame.Tests
{
    [TestFixture]
    public class RosterScreenTests
    {
        readonly List<GameObject> _made = new List<GameObject>();
        HashSet<EventSystem> _esBefore;
        RosterScreen _rs;
        int _fought;

        [SetUp]
        public void SetUp()
        {
            _esBefore = new HashSet<EventSystem>(Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            var camGO = new GameObject("RosterTestCam"); _made.Add(camGO);
            var cam = camGO.AddComponent<Camera>();
            cam.enabled = false;
            var uiGO = new GameObject("RosterTestUI"); _made.Add(uiGO);
            var ui = uiGO.AddComponent<UIRoot>();
            ui.Init(cam);
            var rsGO = new GameObject("RosterTestScreen"); _made.Add(rsGO);
            _rs = rsGO.AddComponent<RosterScreen>();
            _fought = -1;
            _rs.Init(ui, i => _fought = i);
            _rs.SetData(new RosterDef(42u), new PlayerProfile { ladderIndex = 2 });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _made) if (go != null) Object.DestroyImmediate(go);
            _made.Clear();
            // убрать только EventSystem, созданный UIRoot для теста
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!_esBefore.Contains(es)) Object.DestroyImmediate(es.gameObject);
        }

        [Test]
        public void States_FollowLadderIndex()
        {
            Assert.AreEqual(RosterDef.Count, _rs.Count);
            Assert.AreEqual(FighterCardState.Defeated, _rs.StateOf(0));
            Assert.AreEqual(FighterCardState.Defeated, _rs.StateOf(1));
            Assert.AreEqual(FighterCardState.Next, _rs.StateOf(2));
            for (int i = 3; i < RosterDef.Count; i++)
                Assert.AreEqual(FighterCardState.Locked, _rs.StateOf(i), "боец " + i);
        }

        [Test]
        public void Available_DefeatedAndNext_NotLockedOrOutOfRange()
        {
            Assert.IsTrue(_rs.IsAvailable(0));
            Assert.IsTrue(_rs.IsAvailable(1));
            Assert.IsTrue(_rs.IsAvailable(2));
            Assert.IsFalse(_rs.IsAvailable(3));
            Assert.IsFalse(_rs.IsAvailable(RosterDef.Count - 1));
            Assert.IsFalse(_rs.IsAvailable(-1));
            Assert.IsFalse(_rs.IsAvailable(RosterDef.Count));
        }

        [Test]
        public void Selection_StartsAtNext_AndFightOnlyWhenAvailable()
        {
            Assert.AreEqual(2, _rs.Selected);
            _rs.PressFight();
            Assert.AreEqual(2, _fought);

            _fought = -1;
            _rs.Select(5);   // закрытый
            _rs.PressFight();
            Assert.AreEqual(-1, _fought);

            _rs.Select(0);   // реванш с побеждённым
            _rs.PressFight();
            Assert.AreEqual(0, _fought);
        }

        [Test]
        public void LadderDone_AllDefeated()
        {
            _rs.SetData(new RosterDef(42u), new PlayerProfile { ladderIndex = RosterDef.Count });
            for (int i = 0; i < RosterDef.Count; i++)
            {
                Assert.AreEqual(FighterCardState.Defeated, _rs.StateOf(i));
                Assert.IsTrue(_rs.IsAvailable(i));
            }
            Assert.AreEqual(RosterDef.Count - 1, _rs.Selected);
        }
    }
}
