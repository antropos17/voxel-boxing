using UnityEngine;

namespace VoxGame
{
    // Описание удара игрока для эффектов. Заполняет FightDirector из PunchInput и HitResult.
    public struct HitFx
    {
        public PunchType kind;      // Jab / Hook / Upper / Mega
        public PunchType punch;     // вид удара без меги (Jab / Hook / Upper): название на плашке и в «МЕГА-ХУК!»
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
        public const float JabShake = 0.012f, PowerShake = 0.045f, MegaShake = 0.11f, FractureShake = 0.06f;
        public const float BlockShake = 0.025f, KnockdownShake = 0.08f, KnockoutShake = 0.12f, HurtShake = 0.06f;
        public const float PowerFovK = 0.9f, MegaFovK = 2.4f, FractureFovK = 1.3f, FinisherFovK = 3.0f, KnockoutFovK = 1.6f;

        // ---- Удар: кольца, кровь, надписи (src/e_fx.js impactFx, src/g_game.js hit) ----

        public const float StrJab = 0.3f, StrPowerMax = 1.6f, StrMega = 2.6f;   // сила для колец, как в вебе
        public const float WebFaceDist = 0.7f;      // м: лицо соперника от камеры веба; масштаб вида = расстояние / это
        public const float ViewKMin = 1f, ViewKMax = 2.5f;
        public const float WhiteFlash = 0.15f;      // ×FlashMax 0,7 ≈ 0,1: в линейном цвете Unity это та же яркость, что 0,36 #white веба
        public const int BloodJab = 120, BloodPower = 130, BloodPowerPerPw = 10, BloodMega = 280, BloodFracture = 30;
        public const int BloodHeadBurst = 100, BloodCounter = 8, ChunksPower = 3, ChunksMega = 10, ChunksFracture = 6;
        public const int FireMega = 40, SweatJab = 4;
        public const float SpdJab = 0.9f, SpdPower = 1.2f, SpdMega = 1.5f;

        // Без эффектов: частицы, кольца, накладки и надписи не строятся; время (стоп-кадр, слоу-мо) и камера — как с ними.
        public static bool Visuals = true;

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
        public Vector3 LastHitPoint { get; private set; }   // точка последнего удара игрока, мир (замеры)

        public FxParticles Particles { get; private set; }
        public FxBanners   Banners   { get; private set; }
        public FxScreen    Overlay   { get; private set; }
        public FxRings     Rings     { get; private set; }
        public FxPops      Pops      { get; private set; }

        float _clock;                 // реальное время с начала
        float _freeze, _slow;
        float _lastStop = -100f;
        float _shake, _fovK, _kick;
        float _roll, _rollVel;
        float _lastFinisherBanner = -100f;
        uint  _rng = 0x9E3779B9u;

        // Соперник (из SyncFoe)
        Vector3    _foeHead;
        bool       _foeStunned;

        Transform _fxRoot;
        Camera    _cam;
        float     _aspect = 0.4615f;
        Vector3   _linesAt;

        // ---- Построение ----------------------------------------------------

        // Представление: частицы в сцене, накладки и надписи поверх холста UIRoot.
        // sceneRoot — корень сцены боя; canvasRoot — transform UIRoot (сам Canvas); cam — камера боя.
        public void Build(Transform sceneRoot, Transform canvasRoot, Camera cam)
        {
            Dispose();
            _cam = cam;
            if (!Visuals) return;
            Particles = new FxParticles(sceneRoot);
            Rings = new FxRings(sceneRoot);

            var go = new GameObject("Fx", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            _fxRoot = go.transform;
            var rt = (RectTransform)_fxRoot;
            UIKit.SetAnchors(rt, 0f, 0f, 1f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _fxRoot.SetAsLastSibling();   // поверх экранов

            Overlay = new FxScreen(_fxRoot);      // накладки ниже надписей
            Pops = new FxPops(_fxRoot, cam);
            Banners = new FxBanners(_fxRoot, cam);
        }

        // Показать или спрятать накладки и надписи (экраны поверх боя: пауза, настройки, итоги).
        public void SetOverlayVisible(bool on)
        {
            if (_fxRoot != null && _fxRoot.gameObject.activeSelf != on) _fxRoot.gameObject.SetActive(on);
        }

        // Пропорции кадра (ширина / высота): надписи и линии скорости переставляются сразу.
        public void SetView(float aspect)
        {
            _aspect = aspect;
            Banners?.SetView(aspect);
            Pops?.SetView(aspect);
            if (Overlay != null && Overlay.LinesAlpha > 0f) Overlay.MoveLines(Project(_linesAt));
        }

        Vector2 Project(Vector3 at) =>
            _cam != null ? FxBanners.ProjectView(_cam, _aspect, at, new Vector2(0.5f, 0.6f)) : new Vector2(0.5f, 0.6f);

        // Масштаб вида: камера Unity дальше от лица, чем в вебе; кольца и кровь крупнее во столько же раз.
        float ViewK(Vector3 at) =>
            _cam != null ? Mathf.Clamp(Vector3.Distance(_cam.transform.position, at) / WebFaceDist, ViewKMin, ViewKMax) : 2f;

        Vector3 ToCam(Vector3 at, Vector3 dir) =>
            _cam != null ? (_cam.transform.position - at).normalized : (dir.sqrMagnitude > 1e-6f ? -dir.normalized : Vector3.back);

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
                if (_cam != null) Particles.SetCamera(_cam.transform.position);
                Particles.Tick(dt);
            }
            Rings?.Tick(dt, realDt, _cam != null ? _cam.transform : null);
            Banners?.Tick(realDt);
            if (Pops != null) { RecordArms(); SyncAvoid(); Pops.Tick(realDt, realDt * TimeScale); }
            Overlay?.Tick(realDt);
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
            LastHitPoint = h.point;

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

            if (!Visuals) return;
            float str = h.mega ? StrMega : power ? Mathf.Min(StrPowerMax, pw * (h.counter ? 1.25f : 1f)) : StrJab;
            float k = ViewK(h.point);
            Vector3 toCam = ToCam(h.point, h.dir);

            // Кольца ударной волны: джеб — одно, силовой — два, мега — три; вспышка и линии скорости — с силы 1
            Rings?.Impact(h.point, str, h.mega ? 3 : power ? 2 : 1, k);
            if (Overlay != null)
            {
                if (str >= 1f)
                {
                    _linesAt = h.point;
                    Overlay.Lines(Project(h.point), Mathf.Min(1f, str / 1.3f));
                    Overlay.Flash(WhiteFlash);
                }
                if (h.mega) { Overlay.MegaGlow(); Overlay.Splat(3); }
                if (h.headBurst) { Overlay.Flash(0.5f); Overlay.Splat(4); }
                else if (h.counter) Overlay.Flash(0.15f);
            }

            // Кровь: капли-штрихи и морось из точки удара назад к камере и вбок по удару; кубики мяса на силовых
            if (Particles != null)
            {
                int blood = h.mega ? BloodMega : power ? BloodPower + Mathf.RoundToInt(BloodPowerPerPw * (pw - 1f)) : BloodJab;
                if (h.fracture) blood += BloodFracture;
                if (h.headBurst) blood += BloodHeadBurst;
                if (h.counter) blood += BloodCounter;
                int bk = h.mega || h.headBurst ? FxParticles.BurstAll : h.punch == PunchType.Upper ? FxParticles.BurstUpper
                       : h.punch == PunchType.Hook ? FxParticles.BurstHook : FxParticles.BurstJab;
                Particles.BloodBurst(h.point, h.dir, toCam, blood, bk, h.mega ? SpdMega : power ? SpdPower : SpdJab, k);
                int chunks = (h.mega ? ChunksMega : power ? ChunksPower : 0) + (h.fracture ? ChunksFracture : 0);
                if (chunks > 0) Particles.Chunks(h.point, h.dir, toCam, chunks, 1f, k);
                if (h.mega) Particles.Fire(h.point, h.dir, toCam, FireMega, k);
                else if (!power) Particles.Sweat(h.point, h.dir, SweatJab);
            }

            // Надписи: мега — крупное название и число урона сбоку; обычный удар — плашка «ХУК −12» под подбородком
            int dmg = Mathf.RoundToInt(h.damage);
            if (h.mega)
            {
                Banners?.ShowCenter(FxBanners.MegaNames[Mathf.Clamp((int)h.punch, 0, FxBanners.MegaNames.Length - 1)], BannerStyle.Mega);
                if (h.damage >= 0.5f) Banners?.ShowNumber(dmg, h.point, 2);
            }
            else if (Pops != null)
            {
                Pops.ShowPop(FxPops.PopText((int)h.punch, h.counter, dmg), h.point, power || h.counter);
            }
            if (h.fracture && !h.mega) Banners?.ShowCenter(FxBanners.Fracture, BannerStyle.Power);
            Pops?.OnCombo(h.combo);
        }

        // Метки травм: новый перелом в зоне zone (AnatZone), выбитые зубы, оторванная челюсть, выбитые глаза.
        public void OnFracture(int zone)
        {
            if (Pops == null || zone < 0 || zone >= FxPops.ZoneFracture.Length) return;
            Pops.SetChip(zone, FxPops.ZoneFracture[zone], true);
        }

        int  _teethShown, _eyesShown;
        bool _jawShown;
        const int ChipTeeth = 10, ChipJaw = 11, ChipEyes = 12;

        public void SyncInjuries(int teethOut, bool jawOff, int eyesOut)
        {
            if (Pops == null) return;
            if (teethOut != _teethShown) { _teethShown = teethOut; if (teethOut > 0) Pops.SetChip(ChipTeeth, FxPops.TeethText(teethOut), teethOut > 3); }
            if (jawOff && !_jawShown) { _jawShown = true; Pops.SetChip(ChipJaw, FxPops.JawOff, true); }
            if (eyesOut != _eyesShown) { _eyesShown = eyesOut; if (eyesOut > 0) Pops.SetChip(ChipEyes, eyesOut > 1 ? FxPops.EyesOut : FxPops.EyeOut, true); }
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
            if (hasPoint)
            {
                Particles?.Sparks(at, Vector3.up, 6, 0.8f);
                Rings?.Impact(at, 0.4f, 1, ViewK(at));   // веб: impactFx(точка, 0,25…0,6) и на блоке
            }
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
            Particles?.BloodBurst(_foeHead, Vector3.down, ToCam(_foeHead, Vector3.down), 30, FxParticles.BurstAll, SpdPower, ViewK(_foeHead));
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
            Rings?.Impact(_foeHead, StrMega, 3, ViewK(_foeHead));
            Overlay?.Cinema(KnockoutSlow + 1.2f, 1f);
            Particles?.BloodBurst(_foeHead, Vector3.down, ToCam(_foeHead, Vector3.down), 60, FxParticles.BurstAll, SpdMega, ViewK(_foeHead));
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
            Rings?.Impact(_foeHead, StrMega, 3, ViewK(_foeHead));
            if (Overlay != null)
            {
                Overlay.Flash(1f);
                Overlay.Cinema(FinisherSlow + 1.6f, 1f);
                Overlay.Splat(5);
            }
            if (Particles != null)
            {
                var c = ToCam(_foeHead, Vector3.down);
                float k = ViewK(_foeHead);
                Particles.BloodBurst(_foeHead, Vector3.down, c, 120, FxParticles.BurstAll, SpdMega, k);
                Particles.Fire(_foeHead, Vector3.down, c, FireMega, k);
            }
            FinisherReady = false;
        }

        // Новый бой / реванш: всё в покой.
        public void Reset()
        {
            _freeze = _slow = 0f; _lastStop = -100f;
            _shake = _fovK = _kick = _roll = _rollVel = 0f;
            TimeScale = 1f; CamOffset = Vector3.zero; CamRoll = 0f; FovKick = 0f;
            FinisherReady = false; _foeStunned = false;
            _teethShown = _eyesShown = 0; _jawShown = false;
            _lastFinisherBanner = -100f;
            Particles?.Clear();
            Rings?.Clear();
            Pops?.Clear();
            Banners?.Clear();
            Overlay?.Clear();
        }

        public void Dispose()
        {
            Particles?.Dispose(); Particles = null;
            Rings?.Dispose();     Rings = null;
            Pops?.Dispose();      Pops = null;
            Banners?.Dispose();   Banners = null;
            Overlay?.Dispose();   Overlay = null;
            if (_fxRoot != null) GameUtil.Destroy(_fxRoot.gameObject);
            _fxRoot = null;
        }

        // ---- Вспомогательные -----------------------------------------------

        void AddShake(float s) { if (UIMotion.Reduced) return; if (s > _shake) _shake = s; }   // «меньше движения» (UI-MOTION.md): без тряски камеры

        // Перчатки игрока (PlayerGloves: камера/playerArms/playerGlove0..1) → экранные прямоугольники для места плашки удара.
        readonly MeshFilter[] _gloveMf = new MeshFilter[2];
        void SyncAvoid()
        {
            if (_cam == null) { Pops.SetAvoid(Rect.zero, Rect.zero); if (Pops.NeedAvoid) { Pops.BeginMask(); Pops.EndMask(); } return; }
            Pops.SetAvoid(GloveRect(0), GloveRect(1));
            if (Pops.NeedAvoid) ArmMask();
        }

        // Руки игрока для места плашки (доработка 4): перчатки с манжетой и предплечья. Плашка выбирает место один раз и
        // стоит всё время показа, поэтому обходит не только руки сейчас, но и их позы за ArmHistN кадров: рука после удара
        // возвращается тем же путём, которым шла к удару.
        // Обратно рука идёт не всегда тем же путём: у джеба в портрете она приходит сбоку, а уходит вниз, в стойку.
        // Поэтому ещё и путь возврата: позы между нынешней и позой ArmHistN−1 кадров назад (до удара, рука в стойке), ArmSweepN шагов.
        public const int ArmHistN = 21, ArmHistRecent = 9, ArmHistStep = 4, ArmHistStride = 4, ArmSweepN = 6;   // история 0,35 с; недавние 0,15 с — каждая 4-я поза, каждая 4-я точка
        static readonly string[] ArmRigNames = { "playerArms/playerGlove0", "playerArms/playerGlove1", "playerArms/playerForeArm0", "playerArms/playerForeArm1" };
        readonly MeshFilter[] _armMf = new MeshFilter[4];
        // Поза руки игрока в отдаче после касания (PlayerGloves.RecoilGhost): g — половина и вся отдача, part — как ArmRigNames
        public delegate bool GhostFn(int g, int part, out Matrix4x4 world);
        public GhostFn ArmGhost;
        readonly Matrix4x4[] _armHist = new Matrix4x4[ArmHistN * 4];   // поза части в осях камеры; [кадр·4 + часть]
        readonly bool[] _armHistOn = new bool[ArmHistN * 4];
        int _armHead;

        void RecordArms()
        {
            if (_cam == null) return;
            _armHead = (_armHead + 1) % ArmHistN;
            Matrix4x4 c = _cam.transform.worldToLocalMatrix;
            for (int s = 0; s < 4; s++)
            {
                if (_armMf[s] == null)
                {
                    var t = _cam.transform.Find(ArmRigNames[s]) ?? (s < 2 ? _cam.transform.Find(GloveNames[s]) : null);
                    if (t != null) _armMf[s] = t.GetComponent<MeshFilter>();
                }
                var mf = _armMf[s];
                bool on = mf != null && mf.gameObject.activeInHierarchy && mf.sharedMesh != null;
                _armHistOn[_armHead * 4 + s] = on;
                if (on) _armHist[_armHead * 4 + s] = c * mf.transform.localToWorldMatrix;
            }
        }

        // Маска рук в кадре: сейчас — все точки силуэта; в прошлых позах (каждая ArmHistStep-я) — каждая ArmHistStride-я
        // точка с пятном шире в ArmHistPad раз.
        public const float ArmHistPad = 1.3f;
        void ArmMask()
        {
            Pops.BeginMask();
            float tv = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            for (int ago = 0; ago < ArmHistRecent; ago += ArmHistStep)
            {
                int f = (_armHead - ago + ArmHistN) % ArmHistN;
                for (int s = 0; s < 4; s++)
                    if (_armHistOn[f * 4 + s]) ArmSplat(s, _armHist[f * 4 + s], ago == 0 ? 1 : ArmHistStride, ago / ArmHistStep, ago == 0 ? 1f : ArmHistPad, tv);
            }
            // отдача: рука в позах отдачи со сплющенной перчаткой (плашка стоит весь показ, рука туда ещё уйдёт)
            if (ArmGhost != null)
            {
                Matrix4x4 c = _cam.transform.worldToLocalMatrix;
                for (int g = 0; g < PlayerGloves.GhostN; g++)
                    for (int s = 0; s < 4; s++)
                        if (_armMf[s] != null && ArmGhost(g, s, out Matrix4x4 m)) ArmSplat(s, c * m, 1, g, ArmHistPad, tv);
            }
            int fo = (_armHead + 1) % ArmHistN;   // самая старая поза
            for (int s = 0; s < 4; s++)
            {
                if (!_armHistOn[_armHead * 4 + s] || !_armHistOn[fo * 4 + s]) continue;
                Matrix4x4 a = _armHist[_armHead * 4 + s], b = _armHist[fo * 4 + s];
                Vector3 pa = a.GetColumn(3), pb = b.GetColumn(3);
                Quaternion ra = a.rotation, rb = b.rotation; Vector3 sc = a.lossyScale;
                for (int k = 1; k <= ArmSweepN; k++)
                {
                    float t = k / (float)ArmSweepN;
                    ArmSplat(s, Matrix4x4.TRS(Vector3.Lerp(pa, pb, t), Quaternion.Slerp(ra, rb, t), sc), ArmHistStride, k, ArmHistPad, tv);
                }
            }
            Pops.EndMask();
        }

        void ArmSplat(int s, Matrix4x4 m, int stride, int phase, float padK, float tv)
        {
            var mf = _armMf[s];
            var pts = mf != null ? GloveLooks.Silhouette(mf.sharedMesh) : null;
            if (pts == null) return;
            float hw = FxPops.MaskPad * padK * GloveLooks.SilStepCm * 0.01f * mf.transform.lossyScale.x;   // полшага с запасом, м
            for (int i = phase % stride; i < pts.Length; i += stride)
            {
                Vector3 v = m.MultiplyPoint3x4(pts[i]);
                if (v.z <= 0.01f) continue;
                float fy = 0.5f / (v.z * tv), fx = fy / _aspect;
                Pops.MaskSplat(0.5f + v.x * fx, 0.5f + v.y * fy, hw * fx, hw * fy);
            }
        }

        static readonly string[] GloveNames = { "playerGlove0", "playerGlove1" };
        static readonly string[] GloveRigNames = { "playerArms/playerGlove0", "playerArms/playerGlove1" };   // руки в своей оснастке (поле зрения рук)
        Rect GloveRect(int s)
        {
            if (_gloveMf[s] == null)
            {
                var t = _cam.transform.Find(GloveRigNames[s]) ?? _cam.transform.Find(GloveNames[s]);
                if (t == null) return Rect.zero;
                _gloveMf[s] = t.GetComponent<MeshFilter>();
                if (_gloveMf[s] == null) return Rect.zero;
            }
            var mf = _gloveMf[s];
            if (!mf.gameObject.activeInHierarchy || mf.sharedMesh == null) return Rect.zero;
            // углы ориентированного короба сетки перчатки в кадре
            Bounds b = mf.sharedMesh.bounds;
            Transform tr = mf.transform;
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 e = new Vector3((i & 1) == 0 ? -b.extents.x : b.extents.x, (i & 2) == 0 ? -b.extents.y : b.extents.y,
                                        (i & 4) == 0 ? -b.extents.z : b.extents.z);
                Vector3 w = tr.TransformPoint(b.center + e);
                if (_cam.transform.InverseTransformPoint(w).z <= 0.01f) continue;
                Vector2 v = FxBanners.ProjectView(_cam, _aspect, w, Vector2.zero);
                x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
            }
            return x1 > x0 ? Rect.MinMaxRect(x0, y0, x1, y1) : Rect.zero;
        }
        void AddFov(float k) { if (k > _fovK) _fovK = k; }

        float Rand01()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        int RandIdx(int n) => Mathf.Min(n - 1, (int)(Rand01() * n));
    }
}
