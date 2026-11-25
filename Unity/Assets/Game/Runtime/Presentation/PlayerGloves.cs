using UnityEngine;

namespace VoxGame
{
    // Руки игрока от первого лица: две перчатки с предплечьями в осях камеры (камера смотрит в свой +Z).
    // Рука 0 — левая (на экране слева), рука 1 — правая.
    // Предплечье всегда идёт от точки-«локтя» под нижним краем кадра к запястью: в покое, в ударе, в блоке
    // и в поднятой руке оно входит в кадр снизу (или из нижнего угла), а его конец всегда за кадром.
    // НЕ MonoBehaviour — создаётся и управляется из FightDirector.
    public class PlayerGloves
    {
        const float Scale   = 0.86f;   // руки игрока чуть мельче рук соперника: иначе в портрете две перчатки не помещаются
        const float ReturnT = 0.16f;   // возврат руки после удара, с

        // Поза покоя: доли полуэкрана от центра (nx — к своему краю, ny — вверх, минус — вниз) и глубина, м.
        // Портрет: кнопки HUD по всей ширине низа (~13% высоты), перчатки с манжетами над ними.
        const float RestNXP = 0.60f, RestNYP = -0.40f, RestZP = 0.60f;
        // Ландшафт: кнопки только по нижним углам, перчатки ближе к середине.
        const float RestNXL = 0.40f, RestNYL = -0.52f, RestZL = 0.52f;
        // Блок: перчатки выше и ближе
        const float BlockNXP = 0.62f, BlockNXL = 0.21f, BlockNY = -0.36f, BlockZ = 0.40f;

        // «Локоть»: точка под нижним краем кадра на глубине AnchorZ (в ударе уходит до AnchorPunchZ),
        // по горизонтали — доля полуширины кадра на этой глубине (1 — нижний угол своей стороны).
        const float AnchorZ = 0.22f, AnchorPunchZ = 0.50f;
        const float AnchorGap = 0.07f;   // м за нижней плоскостью кадра: больше толщины предплечья, среза не видно
        const float RestAUP = 0.85f, RestAUL = 0.55f, BlockAU = 0.45f;
        const float JabAU = 0.85f, HookAU = 1.0f, UpperAU = 0.50f, MegaAU = 0.80f;
        const float ForeFollow = 0.55f;  // насколько ось перчатки следует за предплечьем (остальное — направление костяшек)
        const float EdgeX = 0.90f;       // в размахе хука центр перчатки не дальше этой доли полуширины кадра
        const float ForeOver = 0.05f;    // запас длины предплечья за «локтем», м

        readonly Transform _cam;
        readonly Transform[] _fore = new Transform[2], _glove = new Transform[2];
        readonly MeshFilter[] _foreMf = new MeshFilter[2], _gloveMf = new MeshFilter[2];
        Mesh _foreMesh;
        readonly Mesh[] _gloveMesh = new Mesh[2];
        GloveLook _look;

        // Кадр
        float _tanV = 0.64f, _tanH = 0.30f, _nLen = 1.19f;
        float _restAU = RestAUP;

        // Положение в стойке (оси камеры) — зависит от поля зрения и пропорций экрана
        readonly Vector3[] _rest = new Vector3[2], _block = new Vector3[2];
        readonly Vector3[] _base = new Vector3[2];   // сглаженное положение без удара
        bool  _baseSet;
        float _blockW;

        // Удар
        readonly float[] _ext = new float[2], _timer = new float[2], _outT = new float[2];
        readonly Vector3[] _goal = new Vector3[2];
        readonly PunchType[] _kind = new PunchType[2];

        bool  _blocking;
        int   _dodgeSide;
        float _dodgeTimer;

        // Поднятая рука победителя: запястье в руке рефери (оси камеры)
        const float VictoryIn = 0.18f;  // переход в позу и обратно, с
        // Поднятая рука входит в кадр из нижнего угла своей стороны и с глубины VictoryAnchorZ: предплечье идёт
        // наискось вверх, а не столбом из середины низа. Вторая рука в это время опущена за нижний край.
        const float VictoryAU = 1.0f, VictoryAnchorZ = 0.55f, VictoryDropNY = -1.7f;
        int     _vicSide = -1;
        bool    _vicOn;
        float   _vicW;
        Vector3 _vicWrist;
        bool    _visible = true;

        static float Sign(int s) => s == 0 ? -1f : 1f;

        public PlayerGloves(Transform cameraParent, GloveLook look, Material mat)
        {
            _cam = cameraParent;
            BuildMeshes(look);
            for (int s = 0; s < 2; s++)
            {
                _fore[s]  = VoxModel.Spawn("playerForeArm" + s, _cam, _foreMesh, mat);
                _glove[s] = VoxModel.Spawn("playerGlove" + s, _cam, _gloveMesh[s], mat);
                _foreMf[s]  = _fore[s].GetComponent<MeshFilter>();
                _gloveMf[s] = _glove[s].GetComponent<MeshFilter>();
                _fore[s].localScale = _glove[s].localScale = Vector3.one * Scale;
            }
            SetView(60f, 0.46f);
            Tick(0f);
        }

        public GloveLook Look => _look;

        // Сменить вид перчаток (магазин, переигровка): сетки перестраиваются, поза не меняется.
        public void SetLook(GloveLook look)
        {
            DestroyMeshes();
            BuildMeshes(look);
            for (int s = 0; s < 2; s++)
            {
                _foreMf[s].sharedMesh  = _foreMesh;
                _gloveMf[s].sharedMesh = _gloveMesh[s];
            }
        }

        public void SetGloves(string gloveId) => SetLook(GloveLook.FromEntry(GlovesCatalog.Get(gloveId)));

        void BuildMeshes(GloveLook look)
        {
            _look = look;
            _foreMesh = GloveLooks.BuildForeArmMesh(look);
            _gloveMesh[0] = GloveLooks.BuildGloveMesh(look, +1);   // левая: большой палец сверху при тыльной стороне наружу
            _gloveMesh[1] = GloveLooks.BuildGloveMesh(look, -1);
        }

        void DestroyMeshes()
        {
            GameUtil.Destroy(_foreMesh);
            GameUtil.Destroy(_gloveMesh[0]);
            GameUtil.Destroy(_gloveMesh[1]);
            _foreMesh = null; _gloveMesh[0] = _gloveMesh[1] = null;
        }

        // Поле зрения камеры (вертикальное, градусы) и отношение ширины к высоте: перчатки стоят по бокам у нижнего края.
        public void SetView(float vFovDeg, float aspect)
        {
            _tanV = Mathf.Tan(vFovDeg * 0.5f * Mathf.Deg2Rad);
            _tanH = _tanV * aspect;
            _nLen = Mathf.Sqrt(1f + _tanV * _tanV);
            bool port = aspect < 1f;
            _restAU = port ? RestAUP : RestAUL;
            float rz = port ? RestZP : RestZL, rnx = port ? RestNXP : RestNXL, rny = port ? RestNYP : RestNYL;
            float bnx = port ? BlockNXP : BlockNXL;
            for (int s = 0; s < 2; s++)
            {
                float sg = Sign(s);
                Vector3 rest  = new Vector3(sg * rnx * rz * _tanH, rny * rz * _tanV, rz);
                Vector3 block = new Vector3(sg * bnx * BlockZ * _tanH, BlockNY * BlockZ * _tanV, BlockZ);
                // при повороте экрана перчатки переезжают сразу, а не плывут через полэкрана
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
            _blockW = Mathf.Lerp(_blockW, _blocking ? 1f : 0f, kb);

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
                Vector3 pos  = _base[s];
                Vector3 kn   = new Vector3(-sg * 0.14f, 0.78f, 0.61f);
                Vector3 back = new Vector3(sg * 0.8f, 0.2f, -0.55f);
                float au = sg * Mathf.Lerp(_restAU, BlockAU, _blockW), az = AnchorZ;

                if (e > 0f)
                {
                    Vector3 pk, pb, bow;
                    float pu;
                    switch (_kind[s])
                    {
                        case PunchType.Hook:
                            pk = new Vector3(-sg * 0.9f, 0.05f, 0.45f); pb = new Vector3(0f, 0.75f, -0.65f);
                            bow = new Vector3(sg * 0.17f, 0.02f, -0.04f); pu = HookAU; break;
                        case PunchType.Upper:
                            pk = new Vector3(0f, 0.85f, 0.52f); pb = new Vector3(sg * 0.25f, -0.45f, 0.85f);
                            bow = new Vector3(0f, -0.16f, 0f); pu = UpperAU; break;
                        case PunchType.Mega:
                            pk = new Vector3(0f, 0.05f, 1f); pb = new Vector3(sg * 0.3f, 1f, 0f);
                            bow = new Vector3(sg * 0.08f, 0.06f, -0.10f); pu = MegaAU; break;
                        default:
                            pk = new Vector3(0f, 0.05f, 1f); pb = new Vector3(sg * 0.3f, 1f, 0f);
                            bow = Vector3.zero; pu = JabAU; break;
                    }
                    pos  = Vector3.Lerp(pos, _goal[s], e) + bow * arc;
                    kn   = Vector3.Lerp(kn, pk, e);
                    back = Vector3.Lerp(back, pb, e);
                    au   = Mathf.Lerp(au, sg * pu, e);
                    az   = Mathf.Lerp(AnchorZ, AnchorPunchZ, e);
                    // размах хука не уводит перчатку за боковой край кадра
                    float lim = Mathf.Max(0.05f, pos.z) * _tanH * EdgeX;
                    pos.x = Mathf.Clamp(pos.x, -lim, lim);
                }

                float lockW = 0f;
                if (s == _vicSide)
                {
                    _vicW = Mathf.MoveTowards(_vicW, _vicOn ? 1f : 0f, dt / VictoryIn);
                    if (_vicW > 0f)
                    {
                        VictoryPose(sg, _vicW, ref pos, ref kn, ref back, ref au);
                        az = Mathf.Lerp(az, VictoryAnchorZ, _vicW);
                        lockW = _vicW;
                    }
                }
                else if (_vicSide >= 0 && _vicW > 0f)
                    pos.y = Mathf.Lerp(pos.y, VictoryDropNY * pos.z * _tanV, _vicW);

                Place(s, pos, kn, back, au, az, lockW);
            }
        }

        // Поднятая рука (оси камеры): запястье в точке хвата, костяшки вверх; «локоть» под кадром прямо под запястьем,
        // поэтому предплечье идёт от нижнего края вверх. w — вес позы 0..1.
        void VictoryPose(float sg, float w, ref Vector3 pos, ref Vector3 kn, ref Vector3 back, ref float au)
        {
            Vector3 up = _cam.InverseTransformDirection(Vector3.up);
            Vector3 vk = (up * 0.92f + new Vector3(-sg * 0.12f, 0f, 0.2f)).normalized;
            Vector3 gt = _vicWrist + vk * (BoxerParts.GloveOff * Scale);
            float vu = sg * VictoryAU;
            pos  = Vector3.Lerp(pos, gt, w);
            kn   = Vector3.Lerp(kn, vk, w).normalized;
            back = Vector3.Lerp(back, new Vector3(sg * 0.3f, 0f, -1f), w);
            au   = Mathf.Lerp(au, vu, w);
        }

        // Поставить руку (оси камеры): pos — центр перчатки, kn — направление костяшек, back — тыльная сторона,
        // au/az — «локоть» под кадром, lockW — насколько запястье прижато к точке хвата рефери.
        void Place(int s, Vector3 pos, Vector3 kn, Vector3 back, float au, float az, float lockW)
        {
            float go = BoxerParts.GloveOff * Scale;
            Vector3 k = kn.sqrMagnitude > 1e-8f ? kn.normalized : Vector3.forward;
            Vector3 w0 = pos - k * go;
            Vector3 a = Dir(w0 - Anchor(au, az, w0), k);
            Vector3 ga = (a * ForeFollow + k * (1f - ForeFollow)).normalized;
            Vector3 w = pos - ga * go;
            if (lockW > 0f) { w = Vector3.Lerp(w, _vicWrist, lockW); pos = w + ga * go; }
            Vector3 el = Anchor(au, az, w);
            a = Dir(w - el, ga);

            _glove[s].localPosition = pos;
            _glove[s].localRotation = Quaternion.LookRotation(ga, Perp(back, ga));
            _fore[s].localPosition = w;
            _fore[s].localRotation = Quaternion.LookRotation(Perp(back, a), a);
            // сетка предплечья длиной ForeLenCm; если «локоть» дальше (удар в дальнюю голову) — тянем вдоль оси
            float need = (w - el).magnitude + ForeOver, have = GloveLooks.ForeLenCm * 0.01f * Scale;
            float sy = need > have ? Scale * need / have : Scale;
            if (_fore[s].localScale.y != sy) _fore[s].localScale = new Vector3(Scale, sy, Scale);
        }

        // «Локоть» под нижним краем кадра: на глубине az, по горизонтали au долей полуширины, за нижней плоскостью
        // кадра на AnchorGap и не ближе к ней, чем запястье w (иначе обратный конец предплечья заходит в кадр).
        Vector3 Anchor(float au, float az, Vector3 w)
        {
            float wIn = (w.y + w.z * _tanV) / _nLen;           // расстояние запястья от нижней плоскости, + внутри кадра
            float d = Mathf.Min(-AnchorGap, wIn - AnchorGap);
            return new Vector3(au * az * _tanH, d * _nLen - az * _tanV, az);
        }

        static Vector3 Dir(Vector3 v, Vector3 fallback) => v.sqrMagnitude > 1e-8f ? v.normalized : fallback;

        // h, сделанный перпендикулярным единичной оси ax; не вырождается.
        static Vector3 Perp(Vector3 h, Vector3 ax)
        {
            Vector3 o = h - ax * Vector3.Dot(h, ax);
            if (o.sqrMagnitude < 1e-4f)
            {
                o = Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.forward;
                o -= ax * Vector3.Dot(o, ax);
            }
            return o.normalized;
        }

        public void Dispose()
        {
            DestroyMeshes();
            for (int i = 0; i < 2; i++)
            {
                if (_fore[i] != null) GameUtil.Destroy(_fore[i].gameObject);
                if (_glove[i] != null) GameUtil.Destroy(_glove[i].gameObject);
            }
        }
    }
}
