using System;
using UnityEngine;

namespace VoxGame
{
    // Свой ГПСЧ звука (xorshift32): не трогает UnityEngine.Random и детерминирован в тестах.
    public sealed class SoundRng
    {
        uint _s;

        public SoundRng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }

        public uint NextUInt()
        {
            uint x = _s;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _s = x;
            return x;
        }

        // [0, 1)
        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);

        // [-1, 1)
        public float NextSigned() => Next01() * 2f - 1f;

        // [0, n)
        public int Range(int n) => n <= 1 ? 0 : (int)(NextUInt() % (uint)n);
    }

    // Логическая таблица голосов без Unity-объектов: кто звучит, какого события, с каким приоритетом и возрастом.
    public sealed class VoiceTable
    {
        readonly bool[] _active;
        readonly int[] _ev;
        readonly int[] _pri;
        readonly long[] _seq;
        long _clock;

        public VoiceTable(int capacity)
        {
            _active = new bool[capacity];
            _ev = new int[capacity];
            _pri = new int[capacity];
            _seq = new long[capacity];
        }

        public int Capacity => _active.Length;
        public bool IsActive(int slot) => _active[slot];
        public int EventOf(int slot) => _ev[slot];
        public int PriorityOf(int slot) => _pri[slot];

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _active.Length; i++) if (_active[i]) n++;
                return n;
            }
        }

        public int CountOf(int ev)
        {
            int n = 0;
            for (int i = 0; i < _active.Length; i++) if (_active[i] && _ev[i] == ev) n++;
            return n;
        }

        // Занять слот под новый голос. Возвращает слот или -1, если звук не играет.
        // evicted — слот, чей голос надо заглушить (равен результату), или -1.
        // 1) у события уже maxPerEvent голосов — глушится самый старый голос этого события;
        // 2) есть свободный слот — он;
        // 3) таблица полна — глушится голос с наименьшим приоритетом (среди равных — самый старый);
        //    если он важнее нового, новый звук не играет.
        public int Allocate(int ev, int pri, int maxPerEvent, out int evicted)
        {
            evicted = -1;
            int same = 0, oldSame = -1, free = -1, low = -1;
            for (int i = 0; i < _active.Length; i++)
            {
                if (!_active[i]) { if (free < 0) free = i; continue; }
                if (_ev[i] == ev)
                {
                    same++;
                    if (oldSame < 0 || _seq[i] < _seq[oldSame]) oldSame = i;
                }
                if (low < 0 || _pri[i] < _pri[low] || (_pri[i] == _pri[low] && _seq[i] < _seq[low])) low = i;
            }

            int slot;
            if (same >= Mathf.Max(1, maxPerEvent)) { slot = oldSame; evicted = slot; }
            else if (free >= 0) slot = free;
            else
            {
                if (low < 0 || _pri[low] > pri) return -1;
                slot = low;
                evicted = slot;
            }

            _active[slot] = true;
            _ev[slot] = ev;
            _pri[slot] = pri;
            _seq[slot] = ++_clock;
            return slot;
        }

        public void Release(int slot) => _active[slot] = false;

        public void Clear()
        {
            for (int i = 0; i < _active.Length; i++) _active[i] = false;
        }
    }

    // Проигрыватель звука: чистый класс, владеет GameObject с пулом AudioSource.
    // Init(parent) → Play(...) по событиям → Tick(udt) каждый кадр → Dispose().
    public sealed class SoundPlayer
    {
        public const int MaxVoices = 20;        // предел одновременных голосов
        public const int TailSources = 4;       // запасные источники: заглушённый голос гаснет на них без щелчка
        public const float KillFade = 0.06f;    // с, гашение вытесненного голоса
        public const float LoopFadeIn = 1.5f;   // с, вход петли зала
        public const float LoopFadeOut = 0.9f;  // с, выход петли зала
        public const float MasterGain = 0.55f;  // общий запас громкости, как в веб-версии (20 голосов не клипуют)

        // --- чистые помощники (тестируются без Unity-объектов) ---

        // Случайный вариант, не повторяющий предыдущий (как snd() веб-версии).
        public static int PickVariant(int count, int last, SoundRng rng)
        {
            if (count <= 1) return 0;
            int k = rng.Range(count);
            if (k == last) k = (k + 1 + rng.Range(count - 1)) % count;
            return k;
        }

        // Разброс высоты: u ∈ [-1, 1], pitch = 2^(u·pv/12).
        public static float PitchFor(float semitones, float u) => Mathf.Pow(2f, u * semitones / 12f);

        // Разброс громкости: u ∈ [0, 1), vol = 1 − u·vv.
        public static float VolumeFor(float volVar, float u01) => 1f - u01 * volVar;

        // --- состояние ---

        GameObject _go;
        AudioSource[] _src;
        int[] _srcSlot;          // слот голоса, который звучит на источнике, или -1
        float[] _srcBase;        // громкость источника без шин и мастера
        SoundBus[] _srcBus;
        float[] _fadeT;          // оставшееся время гашения (>0 — источник гаснет)
        float[] _fadeK;          // текущий множитель гашения
        int[] _slotSrc;          // источник голоса по слоту
        readonly VoiceTable _voices = new VoiceTable(MaxVoices);
        readonly int[] _last = new int[SoundBank.Count];
        SoundRng _rng;

        AudioSource _loop;
        SoundId _loopId;
        float _loopLevel, _loopTarget;
        bool _loopOn;

        float _sfxVol = 1f, _crowdVol = 1f, _masterVol = 1f;
        bool _muted;

        public bool IsReady => _go != null;
        public bool Muted => _muted;
        public int ActiveVoices => _voices.ActiveCount;
        public bool LoopPlaying => _loopOn || _loopLevel > 0f;

        public void Init(Transform parent, uint seed = 0x5EED1234u)
        {
            Dispose();
            _rng = new SoundRng(seed);
            for (int i = 0; i < _last.Length; i++) _last[i] = -1;

            _go = new GameObject("SoundPlayer");
            if (parent != null) _go.transform.SetParent(parent, false);

            int n = MaxVoices + TailSources;
            _src = new AudioSource[n];
            _srcSlot = new int[n];
            _srcBase = new float[n];
            _srcBus = new SoundBus[n];
            _fadeT = new float[n];
            _fadeK = new float[n];
            _slotSrc = new int[MaxVoices];
            for (int i = 0; i < n; i++)
            {
                _src[i] = MakeSource();
                _srcSlot[i] = -1;
                _fadeK[i] = 1f;
            }
            for (int i = 0; i < MaxVoices; i++) _slotSrc[i] = -1;
            _voices.Clear();

            _loop = MakeSource();
            _loop.loop = true;
            _loopId = SoundId.Hall;
            _loopLevel = _loopTarget = 0f;
            _loopOn = false;

            // Все клипы и их звуковые данные грузятся здесь, а не при первом звуке: иначе первый Play каждого варианта
            // распаковывает файл в кадре боя (~0,6 мс; в импорте preloadAudioData выключен).
            for (int e = 0; e < SoundBank.Defs.Length; e++)
                for (int v = 0; v < SoundBank.Defs[e].files.Length; v++)
                {
                    var c = SoundBank.GetClip(SoundBank.Defs[e].id, v);
                    if (c != null && c.loadState != AudioDataLoadState.Loaded) c.LoadAudioData();
                }
        }

        AudioSource MakeSource()
        {
            var s = _go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 0f;
            s.dopplerLevel = 0f;
            return s;
        }

        // Общие громкости 0..1 (из настроек профиля).
        public void SetVolumes(float sfx, float crowd, float master)
        {
            _sfxVol = Mathf.Clamp01(sfx);
            _crowdVol = Mathf.Clamp01(crowd);
            _masterVol = Mathf.Clamp01(master);
            ApplyVolumes();
        }

        // Выключить звук целиком. Петля зала продолжает идти беззвучно, чтобы вернуться при включении.
        public void Mute(bool on)
        {
            _muted = on;
            if (on) StopAll();
            ApplyVolumes();
        }

        float BusGain(SoundBus bus)
        {
            if (_muted) return 0f;
            return MasterGain * _masterVol * (bus == SoundBus.Crowd ? _crowdVol : _sfxVol);
        }

        void ApplyVolumes()
        {
            if (_go == null) return;
            for (int i = 0; i < _src.Length; i++)
                if (_srcSlot[i] >= 0 || _fadeT[i] > 0f)
                    _src[i].volume = _srcBase[i] * _fadeK[i] * BusGain(_srcBus[i]);
            ApplyLoopVolume();
        }

        void ApplyLoopVolume()
        {
            var def = SoundBank.Get(_loopId);
            _loop.volume = def.GainLinear * _loopLevel * BusGain(def.bus);
        }

        // Сыграть событие. volumeMul — сила (например 0.6..1.15 по силе удара), pitchMul — общий сдвиг высоты.
        // Возвращает false, если звук не сыгран (выключен, нет файла, вытеснен более важными).
        public bool Play(SoundId id, float volumeMul = 1f, float pitchMul = 1f)
        {
            if (_go == null) return false;
            var def = SoundBank.Get(id);
            if (def.loop) { StartLoop(id); return true; }
            if (_muted) return false;

            int e = (int)id;
            int n = def.files.Length;
            int k = PickVariant(n, _last[e], _rng);
            AudioClip clip = null;
            for (int i = 0; i < n && clip == null; i++)
            {
                int v = (k + i) % n;
                clip = SoundBank.GetClip(id, v);
                if (clip != null) k = v;
            }
            if (clip == null) return false;

            int slot = _voices.Allocate(e, def.priority, def.maxVoices, out int evicted);
            if (slot < 0) return false;
            _last[e] = k;
            if (evicted >= 0 && _slotSrc[evicted] >= 0)
            {
                int vs = _slotSrc[evicted];
                _srcSlot[vs] = -1;
                _slotSrc[evicted] = -1;
                BeginFade(vs);
            }

            int s = FindIdleSource();
            _slotSrc[slot] = s;
            _srcSlot[s] = slot;
            _fadeT[s] = 0f;
            _fadeK[s] = 1f;
            _srcBus[s] = def.bus;
            _srcBase[s] = def.GainLinear * Mathf.Max(0f, volumeMul) * VolumeFor(def.volVar, _rng.Next01());

            var src = _src[s];
            src.clip = clip;
            src.pitch = Mathf.Clamp(pitchMul * PitchFor(def.pitchVar, _rng.NextSigned()), 0.1f, 3f);
            src.priority = 128 - def.priority * 12;   // у Unity меньше — важнее
            src.volume = _srcBase[s] * BusGain(def.bus);
            SafePlay(src);
            return true;
        }

        // Свободный источник; если все заняты — самый догоревший из гаснущих.
        int FindIdleSource()
        {
            int fading = -1;
            for (int i = 0; i < _src.Length; i++)
            {
                if (_srcSlot[i] >= 0) continue;
                if (_fadeT[i] <= 0f) return i;
                if (fading < 0 || _fadeT[i] < _fadeT[fading]) fading = i;
            }
            // Голосов ≤ MaxVoices, источников больше, поэтому гаснущий найдётся всегда
            SafeStop(_src[fading]);
            _fadeT[fading] = 0f;
            return fading;
        }

        void BeginFade(int s)
        {
            _fadeT[s] = KillFade;
            _fadeK[s] = 1f;
        }

        // Петля (гул зала) с плавным входом через Tick.
        public void StartLoop(SoundId id = SoundId.Hall)
        {
            if (_go == null) return;
            var clip = SoundBank.GetClip(id, 0);
            if (clip == null) return;
            if (_loop.clip != clip)
            {
                SafeStop(_loop);
                _loop.clip = clip;
                _loopLevel = 0f;
            }
            _loopId = id;
            _loopOn = true;
            _loopTarget = 1f;
            if (!SafeIsPlaying(_loop))
            {
                // Начало со случайного места, чтобы зал не начинался всегда одинаково
                try { _loop.timeSamples = (int)(_rng.Next01() * 0.9f * clip.samples); }
                catch (Exception) { }
                ApplyLoopVolume();
                SafePlay(_loop);
            }
        }

        public void StopLoop()
        {
            _loopOn = false;
            _loopTarget = 0f;
        }

        // Каждый кадр, реальное время.
        public void Tick(float udt)
        {
            if (_go == null) return;

            // Освободить отзвучавшие голоса
            for (int slot = 0; slot < MaxVoices; slot++)
            {
                if (!_voices.IsActive(slot)) continue;
                int s = _slotSrc[slot];
                if (s >= 0 && SafeIsPlaying(_src[s])) continue;
                _voices.Release(slot);
                if (s >= 0) _srcSlot[s] = -1;
                _slotSrc[slot] = -1;
            }

            // Гашение вытесненных
            for (int i = 0; i < _src.Length; i++)
            {
                if (_fadeT[i] <= 0f) continue;
                _fadeT[i] -= udt;
                if (_fadeT[i] <= 0f)
                {
                    _fadeT[i] = 0f;
                    _fadeK[i] = 1f;
                    SafeStop(_src[i]);
                    continue;
                }
                _fadeK[i] = _fadeT[i] / KillFade;
                _src[i].volume = _srcBase[i] * _fadeK[i] * BusGain(_srcBus[i]);
            }

            // Петля зала
            if (_loopLevel != _loopTarget)
            {
                float rate = _loopTarget > _loopLevel ? 1f / LoopFadeIn : 1f / LoopFadeOut;
                _loopLevel = Mathf.MoveTowards(_loopLevel, _loopTarget, rate * udt);
                ApplyLoopVolume();
                if (_loopLevel <= 0f && !_loopOn) SafeStop(_loop);
            }
        }

        // Заглушить все короткие звуки (петля не трогается).
        public void StopAll()
        {
            if (_go == null) return;
            for (int i = 0; i < _src.Length; i++)
            {
                SafeStop(_src[i]);
                _srcSlot[i] = -1;
                _fadeT[i] = 0f;
                _fadeK[i] = 1f;
            }
            for (int i = 0; i < MaxVoices; i++) _slotSrc[i] = -1;
            _voices.Clear();
        }

        public void Dispose()
        {
            if (_go == null) return;
            StopAll();
            SafeStop(_loop);
            GameUtil.Destroy(_go);
            _go = null;
            _src = null;
            _loop = null;
            _loopOn = false;
            _loopLevel = _loopTarget = 0f;
        }

        // Вне Play Mode и в пакетном режиме AudioSource может молчать или бросать — тихо игнорируем.
        static void SafePlay(AudioSource s)
        {
            try { s.Play(); } catch (Exception) { }
        }

        static void SafeStop(AudioSource s)
        {
            if (s == null) return;
            try { s.Stop(); } catch (Exception) { }
        }

        static bool SafeIsPlaying(AudioSource s)
        {
            if (s == null) return false;
            try { return s.isPlaying; } catch (Exception) { return false; }
        }
    }
}
