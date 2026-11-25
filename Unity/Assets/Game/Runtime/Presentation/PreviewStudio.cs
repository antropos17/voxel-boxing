using System.Collections.Generic;
using UnityEngine;
using VoxProbe;

namespace VoxGame
{
    // Картинки для меню: поясной портрет бойца (экран лестницы) и перчатка (магазин), отрисованные в RenderTexture
    // вне боя. Портрет — тело FoeBody с накладками головы (причёска, борода, убор) и простой воксельной головой
    // в тон кожи: анатомическую голову здесь не строим, она дорогая. Студия стоит далеко под сценой, камера
    // выключена и рисует только по запросу. Кэш ограничен: портретов MaxPortraits (вытесняется давний),
    // перчаток — не больше каталога. Не MonoBehaviour; владелец — GameApp, освобождение — Dispose.
    public class PreviewStudio
    {
        public const int MaxPortraits = 4;
        public const int PortraitW = 320, PortraitH = 400;
        public const int GloveW = 240, GloveH = 288;   // как рамка превью в магазине (без растяжения)
        static readonly Vector3 At = new Vector3(0f, -300f, 0f);   // под сценой: ни ринга, ни зала в кадре
        static readonly Color Back = new Color(0.115f, 0.115f, 0.135f, 1f);

        readonly Transform _root;
        readonly Camera _cam;
        readonly Material _mat;
        readonly Mesh[] _headMesh = new Mesh[FighterGen.SkinToneCount];   // простая голова по тону кожи

        struct Slot { public int key; public RenderTexture rt; public uint used; }
        readonly Slot[] _portraits = new Slot[MaxPortraits];
        uint _clock;
        readonly Dictionary<string, RenderTexture> _gloves = new Dictionary<string, RenderTexture>();

        public int PortraitCount { get { int n = 0; foreach (var s in _portraits) if (s.rt != null) n++; return n; } }
        public int GloveCount => _gloves.Count;
        public int Renders { get; private set; }   // сколько раз рисовали (кэш работает — число не растёт)

        public PreviewStudio(Transform parent)
        {
            _root = new GameObject("PreviewStudio").transform;
            _root.SetParent(parent, false);
            _root.position = At;
            _mat = new Material(GameUtil.FindShader("VoxGame/VertexColor", GameUtil.VoxMaterial)) { name = "Preview" };

            var camGO = new GameObject("PreviewCam");
            camGO.transform.SetParent(_root, false);
            _cam = camGO.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Back;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 6f;
            _cam.enabled = false;
            for (int i = 0; i < MaxPortraits; i++) _portraits[i].key = -1;
        }

        // ---- Портрет бойца ----------------------------------------------------------------------

        // key — номер в лестнице (или любой устойчивый ключ бойца).
        public Texture Portrait(int key, in FighterDef def)
        {
            int free = -1, old = 0;
            for (int i = 0; i < MaxPortraits; i++)
            {
                if (_portraits[i].rt != null && _portraits[i].key == key) { _portraits[i].used = ++_clock; return _portraits[i].rt; }
                if (_portraits[i].rt == null) { if (free < 0) free = i; }
                else if (_portraits[i].used < _portraits[old].used || _portraits[old].rt == null) old = i;
            }
            int s = free >= 0 ? free : old;
            if (_portraits[s].rt == null) _portraits[s].rt = NewRt(PortraitW, PortraitH, "Portrait");
            _portraits[s].key = key;
            _portraits[s].used = ++_clock;
            RenderPortrait(def, _portraits[s].rt);
            return _portraits[s].rt;
        }

        void RenderPortrait(in FighterDef def, RenderTexture rt)
        {
            // накладки головы садятся по позе черепа из AnatSkin; без анатомической головы — поза покоя
            bool skinOn = AnatSkin.on;
            AnatSkin.on = false;
            FoeBody body = null;
            try
            {
                body = new FoeBody(_root, def, _mat);
                var cmd = new AICommand { newPhase = FightPhase.Idle };
                for (int i = 0; i < 30; i++) body.Tick(cmd, 1f / 60f, Vector3.zero);
                body.gear.SyncToSkull();
                int tone = Mathf.Clamp(def.colors.skinTone, 0, _headMesh.Length - 1);
                if (_headMesh[tone] == null) _headMesh[tone] = BuildHead(def.colors.skin.a > 0f ? (Color32)def.colors.skin : BoxerParts.HeadSkin);
                var head = VoxModel.Spawn("portraitHead", body.headMount, _headMesh[tone], _mat);
                float ms = body.headMount.lossyScale.x;
                head.localScale = Vector3.one / (ms > 1e-4f ? ms : 1f);

                // от пояса до макушки, чуть сбоку и сверху (как на листе бойцов)
                Vector3 look = body.headMount.position + Vector3.down * 0.18f;
                Vector3 eye = look + new Vector3(-0.32f, 0.12f, 2.12f);
                _cam.fieldOfView = 26f;
                _cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
                Draw(rt);
            }
            finally
            {
                body?.Dispose();
                AnatSkin.on = skinOn;
            }
        }

        // Простая голова в тон кожи (см, начало — уровень ушей, +Z — лицо): череп, челюсть, нос, уши, глаза, брови, рот.
        static Mesh BuildHead(Color32 skin)
        {
            var mn = new Vector3(-11f, -15f, -12f);
            var mx = new Vector3(11f, 14f, 13.5f);
            var m = VoxModel.FromSdf(mn, mx, 0.6f,
                (x, y, z) =>
                {
                    float d = VoxModel.Ell(x, y - 1.5f, z + 0.5f, 8.6f, 11f, 10f);
                    d = VoxModel.Smin(d, VoxModel.Ell(x, y + 7f, z - 2f, 6.6f, 6.5f, 7.5f), 2f);
                    d = Mathf.Min(d, VoxModel.Ell(x, y + 1.5f, z - 10f, 1.3f, 2.6f, 1.9f));
                    float ax = Mathf.Abs(x);
                    d = Mathf.Min(d, VoxModel.Ell(ax - 8.7f, y, z + 0.5f, 1.3f, 3f, 2f));
                    return d;
                },
                (x, y, z) =>
                {
                    float ax = Mathf.Abs(x);
                    if (z > 5f && ax > 1.8f && ax < 4.6f && y > 1.4f && y < 3.0f) return 2;   // глаза
                    if (z > 5f && ax > 1.4f && ax < 5.0f && y > 4.0f && y < 5.0f) return 3;   // брови
                    if (z > 6f && ax < 2.6f && y > -6.4f && y < -5.3f) return 4;              // рот
                    return 1;
                });
            var pal = new Color32[] { default, skin, new Color32(40, 30, 28, 255),
                                      new Color32(88, 60, 44, 255), new Color32(150, 78, 70, 255) };
            return m.BuildMesh("portraitHead", pal, mn * 0.01f, 0.006f);
        }

        // ---- Перчатка -------------------------------------------------------------------------------

        public Texture Glove(string id)
        {
            if (_gloves.TryGetValue(id, out var rt) && rt != null) return rt;
            rt = NewRt(GloveW, GloveH, "Glove_" + id);
            Transform g = null;
            try
            {
                // перчатка в три четверти: костяшки к зрителю и вбок, тыльная сторона сверху (как на листе перчаток)
                g = GloveLooks.SpawnShowcase(id, _root, _mat, -1);
                g.localPosition = Vector3.zero;
                g.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                // смотреть в середину перчатки (с манжетой), а не в начало её узла: иначе край срезан
                var rs = g.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds(_root.position, Vector3.one * 0.2f);
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                Vector3 at = b.center;
                Vector3 eye = at + new Vector3(0.42f, 0.30f, 0.50f).normalized * (b.extents.magnitude * 4.4f);   // рамка уже по ширине: дистанция по горизонтальному углу
                _cam.fieldOfView = 30f;
                _cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));
                Draw(rt);
            }
            finally
            {
                GloveLooks.DestroyShowcase(g);
            }
            _gloves[id] = rt;
            return rt;
        }

        // ---- Общее ----------------------------------------------------------------------------------

        static RenderTexture NewRt(int w, int h, string name)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = name };
            rt.Create();
            return rt;
        }

        void Draw(RenderTexture rt)
        {
            _cam.targetTexture = rt;
            _cam.Render();
            _cam.targetTexture = null;
            Renders++;
        }

        public void Dispose()
        {
            for (int i = 0; i < MaxPortraits; i++)
            {
                if (_portraits[i].rt != null) { _portraits[i].rt.Release(); GameUtil.Destroy(_portraits[i].rt); }
                _portraits[i] = new Slot { key = -1 };
            }
            foreach (var kv in _gloves) if (kv.Value != null) { kv.Value.Release(); GameUtil.Destroy(kv.Value); }
            _gloves.Clear();
            for (int i = 0; i < _headMesh.Length; i++) { GameUtil.Destroy(_headMesh[i]); _headMesh[i] = null; }
            GameUtil.Destroy(_mat);
            if (_root != null) GameUtil.Destroy(_root.gameObject);
        }
    }
}
