using UnityEngine;

namespace VoxGame
{
    // Описание удара игрока для эффектов. Заполняет FightDirector из PunchInput и HitResult.
    public struct HitFx
    {
        public PunchType kind;      // Jab / Hook / Upper / Mega
        public int       side;      // рука: -1 левая, +1 правая (крен камеры на хуке)
        public float     power;     // 0..1+: 1 — обычный силовой (pw×множитель), джеб ≈ 0.3–0.5
        public bool      mega;
        public bool      counter;   // встречный (соперник был в замахе)
        public bool      fracture;  // новый перелом
        public bool      headBurst; // накопленная травма головы за порогом разрыва
        public int       zone;      // AnatZone или -1
        public float     damage;    // снято HP (всплывающее число), 0 — без числа
        public int       combo;     // текущая серия
        public Vector3   point;     // точка попадания, мир
        public Vector3   dir;       // направление удара, мир
    }

    // Единая точка «событие боя → набор эффектов». Чистый класс без MonoBehaviour.
    // Время: стоп-кадр и слоу-мо только через HitStop; всё затухает на реальном времени.
    // Игровое время кадра = realDt × TimeScale (TimeScale считает Tick).
    // Представление (частицы, надписи, накладки) подключается через Build; без него класс работает как чистая логика (тесты).
    public class FxDirector
    {
        // ---- Время ---------------------------------------------------------

        public const float SlowScale   = 0.28f;   // ход времени в слоу-мо
        public const float StopQWindow = 0.8f;    // антиспам: за сколько секунд после стопа он снова полный
        public const float StopQMin    = 0.25f;   // антиспам: самый короткий стоп при частых ударах
        public const float ParticleMinScale = 0.3f; // ход времени частиц не медленнее этого (в стоп-кадре)

        // Стоп-кадр / слоу-мо по событиям (секунды, таблица веба)
        public const float JabFreeze = 0.03f;
        public const float PowerFreeze = 0.08f, PowerSlow = 0.20f, PowerSlowPerPw = 0.12f;
        public const float MegaFreeze = 0.16f, MegaSlow = 0.60f;
        public const float FractureFreeze = 0.09f, FractureSlow = 0.50f;
        public const float BlockFreeze = 0.03f;
        public const float DodgeSlow = 0.25f;
        public const float KnockdownSlow = 0.90f;
        public const float KnockoutFreeze = 0.20f, KnockoutSlow = 1.10f;
        public const float FinisherFreeze = 0.20f, FinisherSlow = 1.10f;

        // ---- Камера --------------------------------------------------------

        public const float ShakeScale   = 0.6f;   // web-значения тряски (м) × этот множитель
        public const float ShakeDecay   = 9f;     // exp(-k·t)
        public const float FovPerK      = 6f;     // градусов наезда на единицу fovK
        public const float FovDecay     = 6.5f;
        public const float FovKickMax   = 18f;    // предел наезда, градусы
        public const float KickDist     = 0.08f;  // м толчка вперёд на единицу kick
        public const float KickDecay    = 7f;
        public const float RollSpring   = 95f, RollDamp = 9f;   // пружина крена (как P.cv/P.cr веба)
        public const float RollHook     = 40f;    // импульс крена на хуке, град/с
        public const float RollHurt     = 60f;    // импульс крена от пропущенного удара 
        public const float RollShake    = 1.6f;   // дрожь крена при тряске, градусов на 0.1 м тряски

        // Тряска (м, до ShakeScale) и наезд (fovK) по событиям
        public const float BurstJab = 120f, BurstPower = 190f, BurstMega = 340f, BurstFinisher = 460f;   // опорные px
        public const float JabShake = 0.012f, PowerShake = 0.045f, MegaShake = 0.11f, FractureShake = 0.06f;
        public const float BlockShake = 0.025f, KnockdownShake = 0.08f, KnockoutShake = 0.12f, HurtShake = 0.06f;
        public const float PowerFovK = 0.9f, MegaFovK = 2.4f, FractureFovK = 1.3f, FinisherFovK = 3.0f, KnockoutFovK = 1.6f;

        // ---- Добивание -----------------------------------------------------

        public const float FinisherHp       = 0.20f;  // доля здоровья: всегда окно добивания
        public const float FinisherHpStun   = 0.35f;  // с этой доли — если сильно оглушён
        public const float FinisherStun     = 0.65f;  // доля оглушения для окна
        public const float FinisherHpAfterKd = 0.45f; // после второго нокдауна
        public const float FinisherBannerGap = 4f;    // не чаще, с: «ДОБЕЙ!»

        // ---- Состояние -----------------------------------------------------

        public float TimeScale  { get; private set; } = 1f;
        public Vector3 CamOffset { get; private set; }   // в осях камеры: x вправо, y вверх, z вперёд
        public float CamRoll    { get; private set; }    // градусы вокруг оси взгляда
        public float FovKick    { get; private set; }    // градусы: вычесть из fieldOfView
        public bool  FinisherReady { get; private set; }
        public float FreezeLeft => _freeze;
        public float SlowLeft   => _slow;
        public float ShakeAmp   => _shake;
        public float Clock      => _clock;

        public FxParticles Particles { get; private set; }
        public FxBanners   Banners   { get; private set; }
        public FxScreen    Overlay   { get; private set; }

        float _clock;                 // реальное время с начала
        float _freeze, _slow;
        float _lastStop = -100f;
        float _shake, _fovK, _kick;
        float _roll, _rollVel;
        float _lastFinisherBanner = -100f;
        int   _lastComboShown;
        uint  _rng = 0x9E3779B9u;

        // Соперник (из SyncFoe)
        Vector3    _foeHead;
        bool       _foeStunned;

        Transform _fxRoot;
        Camera    _cam;
        float     _aspect = 0.4615f;
        Vector3   _burstAt;
        bool      _burstOn;

        // ---- Построение ----------------------------------------------------

        // Представление: частицы в сцене, накладки и надписи поверх холста UIRoot.
        // sceneRoot — корень сцены боя; canvasRoot — transform UIRoot (сам Canvas); cam — камера боя.
        public void Build(Transform sceneRoot, Transform canvasRoot, Camera cam)
        {
            Dispose();
            _cam = cam;
            Particles = new FxParticles(sceneRoot);

            var go = new GameObject("Fx", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            _fxRoot = go.transform;
            var rt = (RectTransform)_fxRoot;
            UIKit.SetAnchors(rt, 0f, 0f, 1f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _fxRoot.SetAsLastSibling();   // поверх экранов

            Overlay = new FxScreen(_fxRoot);      // накладки ниже надписей
            Banners = new FxBanners(_fxRoot, cam);
        }

        // Показать или спрятать накладки и надписи (экраны поверх боя: пауза, настройки, итоги).
        public void SetOverlayVisible(bool on)
        {
            if (_fxRoot != null && _fxRoot.gameObject.activeSelf != on) _fxRoot.gameObject.SetActive(on);
        }

        // Пропорции кадра (ширина / высота): надписи и звезда удара переставляются сразу.
        public void SetView(float aspect)
        {
            _aspect = aspect;
            Banners?.SetView(aspect);
            if (_burstOn && Overlay != null && _cam != null)
                Overlay.MoveBurst(FxBanners.ProjectView(_cam, _aspect, _burstAt, new Vector2(0.5f, 0.6f)));
        }

        // Звезда удара в точке мира; size — опорные px.
        void Burst(Vector3 at, float size, Color c)
        {
            if (Overlay == null) return;
            _burstAt = at; _burstOn = true;
            var vp = _cam != null ? FxBanners.ProjectView(_cam, _aspect, at, new Vector2(0.5f, 0.6f)) : new Vector2(0.5f, 0.6f);
            Overlay.Burst(vp, size, c);
        }

        // ---- Tick ----------------------------------------------------------

        // Реальное время кадра → TimeScale этого кадра. Тикает и представление.
        public float Tick(float realDt)
        {
            if (realDt < 0f) realDt = 0f;
            _clock += realDt;

            // Стоп-кадр, затем слоу-мо (слоу-мо ждёт конца стоп-кадра, как в вебе)
            if (_freeze > 0f) { _freeze = Mathf.Max(0f, _freeze - realDt); TimeScale = 0f; }
            else if (_slow > 0f) { _slow = Mathf.Max(0f, _slow - realDt); TimeScale = SlowScale; }
            else TimeScale = 1f;

            // Камера: всё на реальном времени
            _shake *= Mathf.Exp(-ShakeDecay * realDt);
            _fovK  *= Mathf.Exp(-FovDecay * realDt);
            _kick  *= Mathf.Exp(-KickDecay * realDt);
            if (_shake < 1e-4f) _shake = 0f;
            if (_fovK  < 1e-3f) _fovK  = 0f;
            if (_kick  < 1e-3f) _kick  = 0f;

            // Пружина крена: полунеявный Эйлер, шаг ограничен ради устойчивости
            float h = Mathf.Min(realDt, 1f / 30f);
            _rollVel += (-RollSpring * _roll - RollDamp * _rollVel) * h;
            _roll    += _rollVel * h;
            if (Mathf.Abs(_roll) < 1e-3f && Mathf.Abs(_rollVel) < 1e-2f) { _roll = 0f; _rollVel = 0f; }

            float s = _shake * ShakeScale;
            if (s > 0f)
            {
                CamOffset = new Vector3((Rand01() * 2f - 1f) * s, (Rand01() * 2f - 1f) * s, _kick * KickDist);
                CamRoll   = _roll + (Rand01() * 2f - 1f) * s * RollShake * 10f;
            }
            else
            {
                CamOffset = new Vector3(0f, 0f, _kick * KickDist);
                CamRoll   = _roll;
            }
            FovKick = Mathf.Min(FovKickMax, _fovK * FovPerK);

            // частицы в стоп-кадре не замирают: брызги разлетаются медленно уже в кадре удара
            float dt = realDt * Mathf.Max(TimeScale, ParticleMinScale);
            if (Particles != null)
            {
                // звёздочки над головой оглушённого
                if (_foeStunned) Particles.EmitStars(_foeHead, dt);
                Particles.Tick(dt);
            }
            Banners?.Tick(realDt);
            Overlay?.Tick(realDt);
            if (_burstOn && Overlay != null && Overlay.BurstAlpha <= 0f) _burstOn = false;
            return TimeScale;
        }

        // Состояние соперника на этот кадр: окно добивания и звёздочки.
        // headPos — центр головы (мир); stunFrac = stunAccum / stunMax.
        public void SyncFoe(float hpFrac, float stunFrac, int knockdowns, FightPhase phase, Vector3 headPos)
        {
            _foeHead = headPos;
            _foeStunned = phase == FightPhase.Stagger || (stunFrac >= FinisherStun && phase != FightPhase.Down && phase != FightPhase.KO);

            bool ready = IsFinisherWindow(hpFrac, stunFrac, knockdowns, phase);
            // подсказка повторяется, пока окно открыто
            if (ready && _clock - _lastFinisherBanner >= FinisherBannerGap)
            {
                _lastFinisherBanner = _clock;
                Banners?.ShowCenter(FxBanners.Finish, BannerStyle.Call);
            }
            FinisherReady = ready;
        }

        // Окно добивания: мало здоровья, или оглушён при среднем, или после второго нокдауна.
        public static bool IsFinisherWindow(float hpFrac, float stunFrac, int knockdowns, FightPhase phase)
        {
            if (phase == FightPhase.Down || phase == FightPhase.Rise || phase == FightPhase.KO) return false;
            if (hpFrac <= 0f) return false;
            if (hpFrac <= FinisherHp) return true;
            bool stunned = phase == FightPhase.Stagger || stunFrac >= FinisherStun;
            if (hpFrac <= FinisherHpStun && stunned) return true;
            if (knockdowns >= 2 && hpFrac <= FinisherHpAfterKd) return true;
            return false;
        }

        // ---- Стоп-кадр -----------------------------------------------------

        // Единственный способ остановить или замедлить время. full — без антиспама (нокдаун, нокаут, добивание).
        public void HitStop(float freeze, float slow, bool full = false)
        {
            if (!full)
            {
                float q = Mathf.Clamp((_clock - _lastStop) / StopQWindow, StopQMin, 1f);
                freeze *= q; slow *= q;
            }
            _lastStop = _clock;
            if (freeze > _freeze) _freeze = freeze;
            if (slow > _slow) _slow = slow;
        }

        // ---- События боя ---------------------------------------------------

        // Удар игрока дошёл до соперника.
        public void OnPlayerHit(in HitFx h)
        {
            bool power = h.kind == PunchType.Hook || h.kind == PunchType.Upper;
            float pw = Mathf.Max(1f, h.power);

            // Время
            if (h.mega) HitStop(MegaFreeze, MegaSlow);
            else if (h.fracture) HitStop(FractureFreeze, FractureSlow);
            else if (power) HitStop(PowerFreeze, PowerSlow + PowerSlowPerPw * (pw - 1f));
            else HitStop(JabFreeze, 0f);

            // Камера
            float shake = h.mega ? MegaShake : h.fracture ? FractureShake : power ? PowerShake * pw : JabShake;
            if (h.counter) shake *= 1.3f;
            AddShake(shake);
            if (h.mega) AddFov(MegaFovK);
            else if (h.fracture) AddFov(FractureFovK);
            else if (power) AddFov(PowerFovK * pw);
            _kick = Mathf.Max(_kick, h.mega ? 1f : power ? 0.6f : 0.25f);
            if (h.kind == PunchType.Hook) _rollVel += h.side * RollHook * pw;
            else if (h.kind == PunchType.Upper) _rollVel += h.side * RollHook * 0.4f;

            // Частицы
            if (Particles != null)
            {
                int blood = h.mega ? 60 : h.fracture ? 30 : power ? 18 : 7;
                int sparks = h.mega ? 22 : power ? 10 : 5;
                if (h.headBurst) blood += 40;
                if (h.counter) blood += 6;
                Particles.BloodBurst(h.point, h.dir, blood, h.mega ? 3.2f : power ? 2.4f : 1.6f);
                Particles.Sparks(h.point, h.dir, sparks, h.mega ? 1.4f : 1f);
                if (power || h.mega) Particles.Sweat(h.point, h.dir, h.mega ? 10 : 5);
            }

            // Накладки: звезда удара по силе (джеб — маленькая белая, мега — крупная оранжевая), вспышка и кровь
            // на объективе — только на самых сильных
            if (Overlay != null)
            {
                float k = h.counter ? 1.2f : 1f;
                if (h.mega) Burst(h.point, BurstMega * k, new Color(1f, 0.78f, 0.30f, 1f));
                else if (power) Burst(h.point, BurstPower * pw * k, new Color(1f, 0.93f, 0.55f, 0.9f));
                else Burst(h.point, BurstJab * k, new Color(1f, 1f, 1f, 0.7f));
                if (h.headBurst) { Overlay.Flash(0.5f); Overlay.Splat(4); }
                else if (h.mega) Overlay.Splat(2);
                else if (h.counter) Overlay.Flash(0.15f);
            }

            // Надписи
            if (Banners != null)
            {
                if (h.damage >= 0.5f)
                    Banners.ShowNumber(Mathf.RoundToInt(h.damage), h.point, h.mega ? 2 : power ? 1 : 0);

                string word = PickWord(h, power);
                if (word != null)
                {
                    var style = h.mega ? BannerStyle.Mega : h.counter ? BannerStyle.Counter : BannerStyle.Power;
                    if (h.counter && !h.mega) Banners.ShowCenter(word, style);
                    else Banners.ShowAt(word, h.point, style);
                }

                // серия — только на заметных отметках (3, 5, 10, 15…): счёт каждого удара уже показывает HUD
                if (h.combo >= FxBanners.ComboMin && h.combo != _lastComboShown && (h.combo == FxBanners.ComboMin || h.combo % 5 == 0))
                    Banners.ShowCenter(FxBanners.ComboText(h.combo), BannerStyle.Combo);
            }
            _lastComboShown = h.combo;
        }

        // Удар соперника по игроку. power 0..1+, blocked — пришёлся в перчатки.
        public void OnFoeHit(float power, bool blocked)
        {
            float p = Mathf.Max(0.3f, power);
            float side = Rand01() < 0.5f ? -1f : 1f;
            if (blocked)
            {
                HitStop(BlockFreeze, 0f);
                AddShake(BlockShake * p);
                _rollVel += side * RollHurt * 0.25f * p;
                Overlay?.Hurt(0.25f * p);
                return;
            }
            AddShake(HurtShake * p);
            _rollVel += side * RollHurt * p;
            if (Overlay != null)
            {
                Overlay.Hurt(Mathf.Min(1f, 0.6f + 0.4f * p));
                Overlay.Splat(p >= 0.75f ? 4 : 2);
            }
        }

        // Удар игрока пришёлся в защиту соперника. at — точка в мире (искры), если известна.
        public void OnBlock() => OnBlock(Vector3.zero, false);
        public void OnBlock(Vector3 at) => OnBlock(at, true);

        void OnBlock(Vector3 at, bool hasPoint)
        {
            HitStop(BlockFreeze, 0f);
            AddShake(BlockShake);
            if (hasPoint) Particles?.Sparks(at, Vector3.up, 6, 0.8f);
            Banners?.ShowCenter(FxBanners.Blocked, BannerStyle.Info);
        }

        // Без надписи «УКЛОН!»: на снимках эффектов уклоняется бот-защитник, а не игрок.
        public bool QuietDodge;

        // Игрок ушёл от удара соперника: короткое замедление, окно контратаки.
        public void OnDodge()
        {
            HitStop(0f, DodgeSlow);
            if (!QuietDodge) Banners?.ShowCenter(FxBanners.Miss, BannerStyle.Info);
        }

        public void OnKnockdown()
        {
            HitStop(0f, KnockdownSlow, true);
            AddShake(KnockdownShake);
            AddFov(PowerFovK);
            Banners?.ShowCenter(FxBanners.Knockdown, BannerStyle.Big);
            Overlay?.Cinema(KnockdownSlow + 0.4f, 0.5f);
            if (Particles != null) Particles.BloodBurst(_foeHead, Vector3.down, 18, 1.6f);
            FinisherReady = false;
        }

        // Нокаут. Если в этот момент было окно добивания — это добивание.
        public void OnKnockout()
        {
            if (FinisherReady) { OnFinisher(); return; }
            HitStop(KnockoutFreeze, KnockoutSlow, true);
            AddShake(KnockoutShake);
            AddFov(KnockoutFovK);
            Banners?.ShowCenter(FxBanners.Knockout, BannerStyle.Huge);
            Burst(_foeHead, BurstMega, new Color(1f, 0.85f, 0.4f, 1f));
            Overlay?.Cinema(KnockoutSlow + 1.2f, 1f);
            if (Particles != null) Particles.BloodBurst(_foeHead, Vector3.down, 36, 2.6f);
            FinisherReady = false;
        }

        // Добивающий удар: длинное слоу-мо, сильный наезд, белая вспышка, крупная надпись.
        public void OnFinisher()
        {
            HitStop(FinisherFreeze, FinisherSlow, true);
            AddShake(KnockoutShake);
            AddFov(FinisherFovK);
            _kick = Mathf.Max(_kick, 1.4f);
            Banners?.ShowCenter(FxBanners.Crushed, BannerStyle.Huge);
            Burst(_foeHead, BurstFinisher, new Color(1f, 0.70f, 0.25f, 1f));
            if (Overlay != null)
            {
                Overlay.Flash(1f);
                Overlay.Cinema(FinisherSlow + 1.6f, 1f);
                Overlay.Splat(5);
            }
            if (Particles != null)
            {
                Particles.BloodBurst(_foeHead, Vector3.down, 60, 3.4f);
                Particles.Sparks(_foeHead, Vector3.down, 20, 1.6f);
            }
            FinisherReady = false;
        }

        // Новый бой / реванш: всё в покой.
        public void Reset()
        {
            _freeze = _slow = 0f; _lastStop = -100f;
            _shake = _fovK = _kick = _roll = _rollVel = 0f;
            TimeScale = 1f; CamOffset = Vector3.zero; CamRoll = 0f; FovKick = 0f;
            FinisherReady = false; _foeStunned = false; _lastComboShown = 0; _burstOn = false;
            _lastFinisherBanner = -100f;
            Particles?.Clear();
            Banners?.Clear();
            Overlay?.Clear();
        }

        public void Dispose()
        {
            Particles?.Dispose(); Particles = null;
            Banners?.Dispose();   Banners = null;
            Overlay?.Dispose();   Overlay = null;
            if (_fxRoot != null) GameUtil.Destroy(_fxRoot.gameObject);
            _fxRoot = null;
        }

        // ---- Вспомогательные -----------------------------------------------

        void AddShake(float s) { if (s > _shake) _shake = s; }
        void AddFov(float k) { if (k > _fovK) _fovK = k; }

        // Слово удара: перелом и зона важнее, силовой — из набора по виду, джеб без слова.
        string PickWord(in HitFx h, bool power)
        {
            if (h.mega) return FxBanners.MegaWords[RandIdx(FxBanners.MegaWords.Length)];
            if (h.counter) return FxBanners.Counter;
            if (!power) return null;
            if (h.fracture) return FxBanners.Fracture;
            switch ((AnatZone)h.zone)
            {
                case AnatZone.Jaw:   return FxBanners.ToJaw;
                case AnatZone.Mouth: return FxBanners.ToTeeth;
                case AnatZone.Nose:  return FxBanners.ToNose;
                case AnatZone.Orbit: return FxBanners.ToEye;
            }
            var set = h.kind == PunchType.Upper ? FxBanners.UpperWords : FxBanners.HookWords;
            return set[RandIdx(set.Length)];
        }

        float Rand01()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        int RandIdx(int n) => Mathf.Min(n - 1, (int)(Rand01() * n));
    }
}
