// WebStats.cs — замер кадров в браузерной сборке: строка WEB_STAT в консоль браузера раз в 5 с боя.
// Только Web-плеер (UNITY_WEBGL && !UNITY_EDITOR): в редакторе, Android и Windows не компилируется, поведение не меняет.
// Кадр — Time.unscaledDeltaTime (время между началами кадров, вместе с отрисовкой и ожиданием vsync браузера).
// Кадр удара — следующий кадр после Tick с ударом по голове (его dt включает весь кадр удара); tick — время FightDirector.Tick на ударе.
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine;

namespace VoxGame
{
    static class WebStats
    {
        static int _seq = -1, _frames, _hits, _slow; static bool _hitPending;
        static double _sum; static float _worst, _hitWorst, _tickWorst, _tickSum, _next; static string _state;

        public static void Frame(FightDirector f, string state)
        {
            if (state != _state) { _state = state; Debug.Log("WEB_STATE " + state); }   // для скрипта проверки: где сейчас игра
            bool fighting = state == "Fight";
            float ms = Time.unscaledDeltaTime * 1000f;
            if (!fighting || f == null) { if (_frames > 0) Log("end"); _seq = -1; return; }
            if (_seq < 0) { _seq = f.hitFrameSeq; _next = Time.realtimeSinceStartup + 5f; return; }
            _frames++; _sum += ms; if (ms > _worst) _worst = ms; if (ms > 33.4f) _slow++;
            if (_hitPending) { _hitPending = false; if (ms > _hitWorst) _hitWorst = ms; }
            if (f.hitFrameSeq != _seq) {
                _seq = f.hitFrameSeq; _hits++; _hitPending = true;
                _tickSum += f.lastHitFrameMs; if (f.lastHitFrameMs > _tickWorst) _tickWorst = f.lastHitFrameMs;
            }
            if (Time.realtimeSinceStartup >= _next) { _next = Time.realtimeSinceStartup + 5f; Log("run"); }
        }

        static void Log(string tag)
        {
            Debug.Log("WEB_STAT " + tag + " frames=" + _frames + " avgMs=" + (_sum / Mathf.Max(1, _frames)).ToString("F1") + " worstMs=" + _worst.ToString("F1")
                + " slow33=" + _slow + " hits=" + _hits + " hitFrameWorstMs=" + _hitWorst.ToString("F1")
                + " hitTickAvgMs=" + (_tickSum / Mathf.Max(1, _hits)).ToString("F1") + " hitTickWorstMs=" + _tickWorst.ToString("F1")
                + " monoMB=" + (UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20) + " allocMB=" + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20));
            if (tag == "end") { _frames = _hits = _slow = 0; _sum = 0; _worst = _hitWorst = _tickWorst = _tickSum = 0; _hitPending = false; }
        }
    }
}
#endif
