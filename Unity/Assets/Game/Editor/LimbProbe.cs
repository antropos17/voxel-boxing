// LimbProbe.cs — проверка конечностей в бою вне Play Mode: два боя (победа и поражение) через настоящий GameApp,
// на каждом тике — скачки суставов (рывок за тик), рука игрока у ближней плоскости камеры, перчатки соперника
// внутри головы, рефери вплотную к сопернику. Итог — строки LIMB_* в LastLog.
// Запуск: script eval: VoxGame.Editor.LimbProbe.Run()
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static class LimbProbe
    {
        const float DT = 1f / 60f;
        public static string LastLog = "";

        static readonly string[] Names =
        {
            "upperArm0", "upperArm1", "foreArm0", "foreArm1", "glove0", "glove1",
            "playerUpperArm0", "playerUpperArm1", "playerForeArm0", "playerForeArm1", "playerGlove0", "playerGlove1",
            "refUpperArm0", "refUpperArm1", "refForeArm0", "refForeArm1",
        };

        sealed class Track { public Transform t; public Vector3 last; public bool has; public float max, prevJ; public string at; public int spikes; public string spikeAt; }

        static GameApp _app;
        static StringBuilder _log;
        static readonly Dictionary<string, Track> _tr = new Dictionary<string, Track>();
        static FightDirector _seen;
        static float _nearMin = 9f; static string _nearAt;
        static float _headMin = 9f; static string _headAt;
        static float _refMin = 9f;  static string _refAt;
        static int _tick;

        public static void Run()
        {
            _log = new StringBuilder();
            _tr.Clear(); _seen = null; _tick = 0;
            _nearMin = _headMin = _refMin = 9f;
            string dir = Path.Combine(Path.GetTempPath(), "voxgame-limb");
            Directory.CreateDirectory(dir);
            string save = Path.Combine(dir, "save.json");
            if (File.Exists(save)) File.Delete(save);
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
                var cam = camGO.AddComponent<Camera>();
                cam.nearClipPlane = 0.05f; cam.farClipPlane = 40f;
                camGO.AddComponent<UniversalAdditionalCameraData>();
                _app = new GameObject("GameRoot").AddComponent<GameApp>();
                _app.Init(cam, 540, 1170, save);
                for (int i = 0; i < 30; i++) _app.Tick(DT);
                _app.Press("menu.play"); _app.Tick(DT);
                _app.Press("mode.ladder"); _app.Tick(DT);
                _app.Press("roster.select:0"); _app.Press("roster.fight");
                Fight(true, "win");
                Until(AppState.Reward, 60 * 30, "win");
                _app.Press("reward.next"); _app.Tick(DT);
                _app.Press("roster.select:1"); _app.Press("roster.fight");
                Fight(false, "lose");
                Until(AppState.Result, 60 * 30, "lose");
                Report();
            }
            finally
            {
                if (_app != null) Object.DestroyImmediate(_app.gameObject);
                _app = null;
                LastLog = _log.ToString().TrimEnd();
                Debug.Log(LastLog);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static void Until(AppState s, int max, string tag)
        {
            for (int i = 0; i < max && _app.State != s; i++) Step(tag);
            _log.AppendLine("LIMB_STATE " + tag + " -> " + _app.State);
        }

        static void Fight(bool win, string tag)
        {
            for (int i = 0; i < 600 && _app.State != AppState.Fight; i++) _app.Tick(DT);
            int cool = 0, n = 0; bool dodged = false;
            for (int i = 0; i < 60 * 300 && _app.State == AppState.Fight && !_app.Fight.Finished; i++)
            {
                var d = _app.Fight; var ph = d.State.phase;
                bool open = ph == FightPhase.Idle || ph == FightPhase.Guard || ph == FightPhase.Recover || ph == FightPhase.Open || ph == FightPhase.Stagger;
                if (win && cool <= 0 && open)
                {
                    var t = (n % 4) == 1 ? PunchType.Upper : (n % 2 == 0 ? PunchType.Hook : PunchType.Jab);
                    _app.FightInput.Inject(new PunchInput { type = t, side = n % 2 == 0 ? -1 : 1, pw = 1.3f, speed = 1.2f });
                    n++; cool = 18;
                }
                cool--;
                if (win && ph == FightPhase.Strike && !dodged) { _app.Press(d.FoeAttackSide > 0 ? "fight.dodge-left" : "fight.dodge-right"); dodged = true; }
                else if (ph != FightPhase.Strike) dodged = false;
                Step(tag);
            }
            _log.AppendLine("LIMB_FIGHT " + tag + " ticks=" + _tick + " won=" + (_app.Fight != null && _app.Fight.PlayerWon));
        }

        static void Step(string tag)
        {
            _app.Tick(DT); _tick++;
            var d = _app.Fight;
            if (d == null) return;
            if (d != _seen)
            {
                _seen = d; _tr.Clear();
                foreach (var t in d.GetComponentsInChildren<Transform>(true))
                    foreach (var nm in Names)
                        if (t.name == nm && !_tr.ContainsKey(nm)) _tr[nm] = new Track { t = t };
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    foreach (var nm in Names)
                        if (t.name == nm && !_tr.ContainsKey(nm)) _tr[nm] = new Track { t = t };
            }
            string ctx = tag + " t=" + _tick + " ph=" + d.State.phase + " app=" + _app.State + " vic=" + d.Victory.State;
            foreach (var kv in _tr)
            {
                var k = kv.Value; if (k.t == null || !k.t.gameObject.activeInHierarchy) { k.has = false; continue; }
                Vector3 p = k.t.position;
                if (k.has)
                {
                    float j = (p - k.last).magnitude; if (j > k.max) { k.max = j; k.at = ctx; }
                    // рывок: сустав сорвался с места (за тик в 4 раза быстрее прошлого и больше 5 см)
                    if (j > 0.05f && j > 4f * k.prevJ + 0.02f) { k.spikes++; if (k.spikes <= 3) k.spikeAt += " [" + j.ToString("0.000") + " " + ctx + "]"; }
                    k.prevJ = j;
                }
                else k.prevJ = 0f;
                k.last = p; k.has = true;
            }
            // рука игрока у ближней плоскости: ближайшая к камере вершина границ сетки
            var cam = _app.Cam;
            for (int s = 0; s < 2; s++)
                foreach (var nm in new[] { "playerGlove" + s, "playerForeArm" + s })
                    if (_tr.TryGetValue(nm, out var k) && k.t != null && k.t.gameObject.activeInHierarchy)
                    {
                        var r = k.t.GetComponent<Renderer>(); if (r == null) continue;
                        var b = r.bounds; float zmin = 9f;
                        for (int c = 0; c < 8; c++)
                        {
                            var v = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                            var cp = cam.transform.InverseTransformPoint(v);
                            // только то, что в кадре по горизонтали и вертикали
                            if (cp.z > 0f && Mathf.Abs(cp.x / cp.z) < 0.6f && Mathf.Abs(cp.y / cp.z) < 0.7f) zmin = Mathf.Min(zmin, cp.z);
                        }
                        if (zmin < _nearMin) { _nearMin = zmin; _nearAt = nm + " " + ctx; }
                    }
            // перчатки соперника и голова
            if (d.Head != null)
                for (int s = 0; s < 2; s++)
                    if (_tr.TryGetValue("glove" + s, out var g) && g.t != null)
                    {
                        float dist = (g.t.position - d.Head.root.position).magnitude;
                        if (dist < _headMin) { _headMin = dist; _headAt = "glove" + s + " " + ctx; }
                    }
            // рефери и соперник
            if (d.RefereeFigure != null && d.Head != null)
            {
                var a = d.RefereeFigure.Root.position; var h = d.Head.root.position;
                float dist = new Vector2(a.x - h.x, a.z - h.z).magnitude;
                if (dist < _refMin) { _refMin = dist; _refAt = ctx; }
            }
        }

        static void Report()
        {
            foreach (var kv in _tr)
                _log.AppendLine("LIMB_JUMP " + kv.Key + " max=" + kv.Value.max.ToString("0.000") + "m at " + kv.Value.at + " spikes=" + kv.Value.spikes + kv.Value.spikeAt);
            _log.AppendLine("LIMB_NEAR min_z=" + _nearMin.ToString("0.000") + " near=" + _app.Cam.nearClipPlane + " at " + _nearAt);
            _log.AppendLine("LIMB_HEAD glove-head min=" + _headMin.ToString("0.000") + " at " + _headAt);
            _log.AppendLine("LIMB_REF ref-foe min=" + _refMin.ToString("0.000") + " at " + _refAt);
        }
    }
}
