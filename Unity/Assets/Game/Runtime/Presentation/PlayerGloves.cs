using UnityEngine;

namespace VoxGame
{
    public enum GloveShape { Normal, Heavy, Narrow, Spiked }

    public struct GloveLook
    {
        public Color      colorA, colorB;
        public GloveShape shape;

        // Смаппировать запись каталога (shapeTag 0-3 → GloveShape 0-3)
        public static GloveLook FromEntry(GloveEntry e) => new GloveLook
        {
            colorA = e.colorA,
            colorB = e.colorB,
            shape  = (GloveShape)Mathf.Clamp(e.shapeTag, 0, (int)GloveShape.Spiked),
        };
    }

    // Руки игрока от первого лица: две перчатки с предплечьями в осях камеры (камера смотрит в свой +Z).
    // Рука 0 — левая (на экране слева), рука 1 — правая.
    // НЕ MonoBehaviour — создаётся и управляется из FightDirector.
    public class PlayerGloves
    {
        const float Scale    = 0.86f;   // руки игрока чуть мельче рук соперника: иначе в портрете две перчатки не помещаются
        const float RestZ    = 0.55f;   // глубина перчаток в стойке, м от камеры
        const float ReturnT  = 0.16f;   // возврат руки после удара, с

        readonly Transform _cam;
        readonly Transform[] _fore = new Transform[2], _glove = new Transform[2];
        readonly Mesh[] _meshes;

        // Положение в стойке (оси камеры) — зависит от поля зрения и пропорций экрана
        readonly Vector3[] _rest = new Vector3[2], _block = new Vector3[2];
        readonly Vector3[] _base = new Vector3[2];   // сглаженное положение без удара
        bool _baseSet;

        // Удар
        readonly float[] _ext = new float[2], _timer = new float[2], _outT = new float[2];
        readonly Vector3[] _goal = new Vector3[2], _lunge = new Vector3[2];
        const float LungeMin  = 0.55f;  // наименьший вынос плеча в ударе, м
        const float LungeKeep = 0.40f;  // расстояние плечо → цель в конце удара (рука 0.49 м, остаётся сгиб в локте)

        static Vector3 Shoulder(float sg) => new Vector3(sg * 0.17f, -0.30f, 0.10f);
        readonly PunchType[] _kind = new PunchType[2];

        bool  _blocking;
        int   _dodgeSide;
        float _dodgeTimer;

        // Поднятая рука победителя: запястье в руке рефери (оси камеры)
        const float VictoryIn = 0.18f;  // переход в позу и обратно, с
        const float VictoryStretch = 3.2f; // предплечье поднятой руки длиннее: локоть за краем кадра, без обрубка посреди экрана
        int     _vicSide = -1;
        bool    _vicOn;
        float   _vicW;
        Vector3 _vicWrist;
        bool    _visible = true;

        static float Sign(int s) => s == 0 ? -1f : 1f;

        public PlayerGloves(Transform cameraParent, GloveLook look, Material mat)
        {
            _cam = cameraParent;
            float k = look.shape == GloveShape.Heavy ? 1.12f : 1f;
            float kx = look.shape == GloveShape.Narrow ? 0.86f : 1f;
            bool studs = look.shape == GloveShape.Spiked;
            var fore = BoxerParts.ForeArmMesh(BoxerParts.HeadSkin);
            var gl = new[]
            {
                BoxerParts.GloveMesh(+1, look.colorA, look.colorB, k, kx, studs),
                BoxerParts.GloveMesh(-1, look.colorA, look.colorB, k, kx, studs),
            };
            _meshes = new[] { fore, gl[0], gl[1] };
            for (int s = 0; s < 2; s++)
            {
                _fore[s]  = VoxModel.Spawn("playerForeArm" + s, _cam, fore, mat);
                _glove[s] = VoxModel.Spawn("playerGlove" + s, _cam, gl[s], mat);
                _fore[s].localScale = _glove[s].localScale = Vector3.one * Scale;
            }
            SetView(60f, 0.46f);
            Tick(0f);
        }

        // Поле зрения камеры (вертикальное, градусы) и отношение ширины к высоте: перчатки стоят по бокам у нижнего края.
        public void SetView(float vFovDeg, float aspect)
        {
            float vHalf = vFovDeg * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * aspect);
            // в портрете кадр узкий: перчатки дальше и ближе к углам, иначе две перчатки занимают весь низ
            bool port = aspect < 1f;
            float z = port ? 0.64f : RestZ;
            // в портрете перчатки подняты над кнопками HUD (низ кадра ~13% занят кнопками)
            float xAng = port ? hHalf * 0.80f : Mathf.Min(hHalf * 0.57f, 22f * Mathf.Deg2Rad);
            float x = z * Mathf.Tan(xAng), y = -z * Mathf.Tan(vHalf * (port ? 0.55f : 0.84f));
            float bx = Mathf.Min(x, 0.085f), by = -0.40f * Mathf.Tan(vHalf * 0.42f);
            for (int s = 0; s < 2; s++)
            {
                // при повороте экрана перчатки переезжают сразу, а не плывут через полэкрана
                Vector3 rest = new Vector3(Sign(s) * x, y, z), block = new Vector3(Sign(s) * bx, by, 0.40f);
                _base[s] = _baseSet ? _base[s] + (_blocking ? block - _block[s] : rest - _rest[s]) : rest;
                _rest[s]  = rest;
                _block[s] = block;
            }
            _baseSet = true;
        }

        // ---- Команды -----------------------------------------------------------------------------

        // Удар. side: −1 — левая, +1 — правая; duration — время до начала возврата, с;
        // worldTarget — точка на голове соперника, куда приходят костяшки.
        public void Punch(int side, PunchType kind, float duration, Vector3 worldTarget)
        {
            int s = side < 0 ? 0 : 1;
            float sg = Sign(s);
            Vector3 t = _cam.InverseTransformPoint(worldTarget);
            switch (kind)
            {
                case PunchType.Hook:  t += new Vector3(sg * 0.075f, 0.005f, -0.035f); break;   // сбоку от скулы
                case PunchType.Upper: t += new Vector3(0f, -0.075f, -0.04f); break;            // под подбородком
                default:              t += new Vector3(0f, 0f, -0.08f); break;                 // перед лицом
            }
            _goal[s]  = t;
            // корпус идёт за ударом: плечо выносится к цели настолько, чтобы рука доставала до головы соперника
            Vector3 sh = Shoulder(sg), to = t - sh;
            _lunge[s] = to.normalized * Mathf.Max(LungeMin, to.magnitude - LungeKeep);
            _kind[s]  = kind;
            _timer[s] = Mathf.Max(duration, 0.08f);
            _outT[s]  = Mathf.Max(_timer[s] * 0.5f, 0.045f);
        }

        public void SetBlock(bool blocking) => _blocking = blocking;

        // Рефери поднимает руку игрока: side (−1 левая, +1 правая), worldWrist — точка хвата (мир), куда встаёт запястье;
        // перчатка над ним костяшками вверх. Звать каждый кадр, пока рефери держит руку.
        public void RaiseVictory(int side, Vector3 worldWrist)
        {
            _vicSide  = side < 0 ? 0 : 1;
            _vicWrist = _cam.InverseTransformPoint(worldWrist);
            _vicOn    = true;
        }

        // Отпустить руку сразу (переигровка).
        public void ClearVictory()
        {
            _vicOn = false; _vicW = 0f; _vicSide = -1;
        }

        // Показать или спрятать обе руки (во время пояса перед камерой).
        public void SetVisible(bool on)
        {
            if (on == _visible) return;
            _visible = on;
            for (int s = 0; s < 2; s++)
            {
                _fore[s].gameObject.SetActive(on);
                _glove[s].gameObject.SetActive(on);
            }
        }

        // Уклон: side < 0 — влево, > 0 — вправо.
        public void SetDodge(int side)
        {
            _dodgeSide  = side;
            _dodgeTimer = side != 0 ? 0.3f : 0f;
        }

        // ---- Tick --------------------------------------------------------------------------------

        public void Tick(float dt)
        {
            if (_dodgeTimer > 0f) { _dodgeTimer -= dt; if (_dodgeTimer <= 0f) _dodgeSide = 0; }
            float kb = 1f - Mathf.Exp(-14f * dt);

            for (int s = 0; s < 2; s++)
            {
                float sg = Sign(s);

                // вылет и возврат
                if (_timer[s] > 0f)
                {
                    _timer[s] -= dt;
                    _ext[s] = Mathf.Min(1f, _ext[s] + dt / _outT[s]);
                }
                else if (_ext[s] > 0f) _ext[s] = Mathf.Max(0f, _ext[s] - dt / ReturnT);

                Vector3 bt = _blocking ? _block[s] : _rest[s];
                if (_dodgeSide != 0) bt += new Vector3(_dodgeSide * 0.05f, -0.015f, 0f);
                _base[s] = Vector3.Lerp(_base[s], bt, kb);

                float e = 1f - (1f - _ext[s]) * (1f - _ext[s]);
                float arc = Mathf.Sin(Mathf.PI * _ext[s]);

                // стойка: костяшки вверх и вперёд, тыльная сторона наружу и к игроку
                Vector3 pos = _base[s];
                Vector3 kn = new Vector3(-sg * 0.14f, 0.78f, 0.61f);
                Vector3 back = new Vector3(sg * 0.8f, 0.2f, -0.55f);
                Vector3 shoulder = Shoulder(sg);

                if (e > 0f)
                {
                    Vector3 pk, pb, bow;
                    switch (_kind[s])
                    {
                        case PunchType.Hook:
                            pk = new Vector3(-sg * 0.9f, 0.05f, 0.45f); pb = new Vector3(0f, 0.75f, -0.65f);
                            bow = new Vector3(sg * 0.17f, 0.02f, -0.04f); break;
                        case PunchType.Upper:
                            pk = new Vector3(0f, 0.85f, 0.52f); pb = new Vector3(sg * 0.25f, -0.45f, 0.85f);
                            bow = new Vector3(0f, -0.16f, 0f); break;
                        case PunchType.Mega:
                            pk = new Vector3(0f, 0.05f, 1f); pb = new Vector3(sg * 0.3f, 1f, 0f);
                            bow = new Vector3(sg * 0.08f, 0.06f, -0.10f); break;
                        default:
                            pk = new Vector3(0f, 0.05f, 1f); pb = new Vector3(sg * 0.3f, 1f, 0f);
                            bow = Vector3.zero; break;
                    }
                    pos = Vector3.Lerp(pos, _goal[s], e) + bow * arc;
                    kn = Vector3.Lerp(kn, pk, e);
                    back = Vector3.Lerp(back, pb, e);
                    // корпус идёт за ударом; плечо не рисуется, поэтому вынос с запасом: рука достаёт до головы
                    // соперника, а не застывает огромной перчаткой перед камерой
                    shoulder += _lunge[s] * e;
                }

                Vector3 pole = new Vector3(sg * 0.55f, -1f, -0.25f);
                if (s == _vicSide)
                {
                    _vicW = Mathf.MoveTowards(_vicW, _vicOn ? 1f : 0f, dt / VictoryIn);
                    if (_vicW > 0f) VictoryPose(sg, _vicW, ref pos, ref kn, ref back, ref shoulder, ref pole);
                }

                BoxerParts.SolveArm(null, _fore[s], _glove[s],
                    _cam.TransformPoint(shoulder), _cam.TransformPoint(pos),
                    _cam.TransformDirection(kn), _cam.TransformDirection(pole),
                    _cam.TransformDirection(back), Scale);
                StretchFore(s, s == _vicSide ? _vicW : 0f);
            }
        }

        // Предплечье тянется вдоль своей оси от запястья (k = 1 — обычная длина). Модель идёт от локтя по +Y.
        void StretchFore(int s, float w)
        {
            Transform f = _fore[s];
            if (w <= 0f)
            {
                if (f.localScale.y != Scale) f.localScale = Vector3.one * Scale;
                return;
            }
            float l2 = BoxerParts.ForeLen * Scale, k = 1f + (VictoryStretch - 1f) * w;
            Vector3 wrist = f.position + f.up * l2;
            f.position = wrist - f.up * (l2 * k);
            f.localScale = new Vector3(Scale, Scale * k, Scale);
        }

        // Поднятая рука (оси камеры): запястье в точке хвата, костяшки вверх; предплечье уходит вниз и наружу за край
        // кадра (плечо не рисуется, локоть должен быть вне кадра). w — вес позы 0..1.
        void VictoryPose(float sg, float w, ref Vector3 pos, ref Vector3 kn, ref Vector3 back, ref Vector3 shoulder, ref Vector3 pole)
        {
            Vector3 up = _cam.InverseTransformDirection(Vector3.up);
            Vector3 vk = (up * 0.92f + new Vector3(-sg * 0.12f, 0f, 0.2f)).normalized;
            Vector3 wrist = _vicWrist;
            float l1 = BoxerParts.UpperLen * Scale, l2 = BoxerParts.ForeLen * Scale;
            Vector3 elbow = wrist + new Vector3(sg * 0.55f, -0.55f, -0.62f).normalized * l2;
            Vector3 sh = elbow + new Vector3(sg * 0.25f, -0.85f, -0.45f).normalized * (l1 * 0.98f);
            Vector3 gt = wrist + vk * BoxerParts.GloveOff * Scale;
            pos      = Vector3.Lerp(pos, gt, w);
            kn       = Vector3.Lerp(kn, vk, w).normalized;
            back     = Vector3.Lerp(back, new Vector3(sg * 0.3f, 0f, -1f), w);
            shoulder = Vector3.Lerp(shoulder, sh, w);
            pole     = Vector3.Lerp(pole, elbow - (sh + wrist) * 0.5f, w);
        }

        public void Dispose()
        {
            foreach (var m in _meshes) GameUtil.Destroy(m);
            for (int i = 0; i < 2; i++)
            {
                if (_fore[i] != null) GameUtil.Destroy(_fore[i].gameObject);
                if (_glove[i] != null) GameUtil.Destroy(_glove[i].gameObject);
            }
        }
    }
}
