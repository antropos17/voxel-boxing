using System.Collections.Generic;
using UnityEngine;

namespace VoxGame
{
    // Ринг и зал: помост с настилом, четыре ряда канатов, угловые стойки с подушками, пустые трибуны (зрителей
    // ставит CrowdView), ключевой свет. Всё строится кодом один раз, в бою не меняется.
    // Оси ринга: центр настила в начале координат, настил на высоте 0, пол зала на −PlatH.
    // Красный угол — (+RopeHalf, −RopeHalf), синий — напротив.
    // НЕ MonoBehaviour — создаётся и управляется из FightDirector.
    public class RingBuilder
    {
        public const float RopeHalf = 2.1f;    // канаты: квадрат 4.2 м
        const float PlatHalf = 2.6f;           // помост шире канатов на фартук
        const float PlatH    = 1.0f;           // высота помоста над полом зала
        const float RopeT    = 0.036f;         // толщина каната
        static readonly float[] RopeY = { 0.46f, 0.76f, 1.06f, 1.36f };

        static readonly Color32 CCanvas   = new Color32( 62, 104, 168, 255);
        static readonly Color32 CApronTop = new Color32( 46,  80, 134, 255);
        static readonly Color32 CMark     = new Color32(196, 208, 226, 255);
        static readonly Color32 CSkirt    = new Color32( 22,  32,  64, 255);
        static readonly Color32 CTrim     = new Color32(214, 214, 208, 255);
        static readonly Color32 CPost     = new Color32(150, 154, 165, 255);
        static readonly Color32 CRed      = new Color32(204,  44,  44, 255);
        static readonly Color32 CBlue     = new Color32( 44,  78, 190, 255);
        static readonly Color32 CWhite    = new Color32(226, 226, 220, 255);
        static readonly Color32 CHall     = new Color32( 34,  34,  42, 255);
        static readonly Color32 CStand    = new Color32( 28,  28,  36, 255);
        static readonly Color32 CWall     = new Color32( 20,  20,  28, 255);
        static readonly Color32[] RopeCol = { CRed, CBlue, CWhite, CRed };   // снизу вверх

        Transform _root;
        readonly List<Object> _owned = new List<Object>();

        // Построить ринг: center и yawDeg — положение и поворот ринга в осях parent.
        // lightForward — направление ключевого света в осях parent (свет в осях боя, а не ринга).
        public void Build(Transform parent, Vector3 center, float yawDeg, Vector3 lightForward)
        {
            _root = new GameObject("Ring").transform;
            _root.SetParent(parent, false);
            _root.localPosition = center;
            _root.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

            var shader = GameUtil.FindShader("VoxGame/VertexColor", GameUtil.VoxMaterial);
            var ringMat = NewMat(shader, 0.42f, 0f, 1000f, 2000f);
            var hallMat = NewMat(shader, 0.34f, 0f, 5.5f, 21f);

            var mb = new MeshBuilder();
            AddCanvas(mb); AddSkirt(mb); AddPosts(mb); AddRopes(mb);
            Spawn("ring", mb.Build("ring"), ringMat);

            mb = new MeshBuilder();
            AddHall(mb);
            Spawn("hall", mb.Build("hall"), hallMat);
            // ряды ламп над трибунами убраны (25.11.2026): в тёмном небе они читались белыми «чёрточками»
            // и пересекали таймер и кнопку паузы; свет сцены даёт KeyLight, лампы её не освещали

            var lightGO = new GameObject("KeyLight");
            lightGO.transform.SetParent(parent, false);
            lightGO.transform.localRotation = Quaternion.LookRotation(lightForward.normalized);
            var light = lightGO.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.color     = new Color(1.00f, 0.96f, 0.88f);
            light.intensity = 1.15f;
            light.shadows   = LightShadows.None;
            _owned.Add(lightGO);
        }

        Material NewMat(Shader shader, float ambient, float glow, float fogStart, float fogEnd)
        {
            var m = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
            m.SetFloat("_Ambient", ambient);
            m.SetFloat("_Glow", glow);
            m.SetFloat("_FogStart", fogStart);
            m.SetFloat("_FogEnd", fogEnd);
            _owned.Add(m);
            return m;
        }

        void Spawn(string name, Mesh mesh, Material mat)
        {
            _owned.Add(mesh);
            VoxModel.Spawn(name, _root, mesh, mat);
        }

        // ---- Помост ------------------------------------------------------------------------------

        // Настил клетками 10 см: пятно света в центре, круг разметки, фартук за канатами темнее.
        static void AddCanvas(MeshBuilder mb)
        {
            const int N = 52; float cell = 2f * PlatHalf / N;
            for (int iz = 0; iz < N; iz++)
            for (int ix = 0; ix < N; ix++)
            {
                float x0 = -PlatHalf + ix * cell, z0 = -PlatHalf + iz * cell;
                float cx = x0 + cell * 0.5f, cz = z0 + cell * 0.5f, r = Mathf.Sqrt(cx * cx + cz * cz);
                bool apron = Mathf.Abs(cx) > RopeHalf || Mathf.Abs(cz) > RopeHalf;
                Color32 c = apron ? CApronTop : CCanvas;
                if (!apron && (Mathf.Abs(r - 1.0f) < 0.05f || Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz)) > RopeHalf - 0.1f)) c = CMark;
                float k = Mathf.Lerp(0.58f, 1f, Mathf.Exp(-r * r / 5.5f)) * (((ix + iz) & 1) == 0 ? 1f : 0.965f);
                mb.Quad(new Vector3(x0, 0, z0), new Vector3(x0, 0, z0 + cell), new Vector3(x0 + cell, 0, z0 + cell),
                        new Vector3(x0 + cell, 0, z0), Vector3.up, Mul(c, k));
            }
        }

        // Юбка помоста: тёмные стенки со светлым кантом.
        static void AddSkirt(MeshBuilder mb)
        {
            float h = PlatHalf;
            mb.Box(new Vector3(0, -0.04f, 0), new Vector3(h + 0.012f, 0.035f, h + 0.012f), 0f, CTrim, top: false);
            mb.Box(new Vector3(0, -0.5f * PlatH - 0.04f, 0), new Vector3(h, 0.5f * PlatH - 0.04f, h), 0f, CSkirt, top: false);
        }

        // Стойки по углам и подушки на них, повёрнутые к центру ринга.
        static void AddPosts(MeshBuilder mb)
        {
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? 1f : -1f, sz = (i & 2) == 0 ? -1f : 1f;
                float px = sx * (RopeHalf + 0.17f), pz = sz * (RopeHalf + 0.17f);
                mb.Box(new Vector3(px, 0.26f, pz), new Vector3(0.055f, 1.26f, 0.055f), 0f, CPost);
                mb.Box(new Vector3(px, 1.545f, pz), new Vector3(0.07f, 0.025f, 0.07f), 0f, CTrim);
                Color32 pad = i == 0 ? CRed : i == 3 ? CBlue : CWhite;   // i=0: (+,−) красный, i=3: (−,+) синий
                float yaw = Mathf.Atan2(sx, sz) * Mathf.Rad2Deg;         // толщина подушки вдоль диагонали
                var pc = new Vector3(sx * (RopeHalf + 0.03f), 0.91f, sz * (RopeHalf + 0.03f));
                mb.Box(pc, new Vector3(0.15f, 0.56f, 0.075f), yaw, pad);
                mb.Box(pc + new Vector3(0, 0.585f, 0), new Vector3(0.155f, 0.025f, 0.08f), yaw, CTrim);
                mb.Box(pc - new Vector3(0, 0.585f, 0), new Vector3(0.155f, 0.025f, 0.08f), yaw, CTrim);
            }
        }

        // Четыре ряда канатов по периметру и по две стяжки на сторону.
        static void AddRopes(MeshBuilder mb)
        {
            float half = RopeHalf, len = RopeHalf - 0.02f, t = RopeT * 0.5f;
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2; float s = (side & 1) == 0 ? -1f : 1f;
                for (int r = 0; r < RopeY.Length; r++)
                {
                    var c = alongX ? new Vector3(0, RopeY[r], s * half) : new Vector3(s * half, RopeY[r], 0);
                    var e = alongX ? new Vector3(len, t, t) : new Vector3(t, t, len);
                    mb.Box(c, e, 0f, RopeCol[r]);
                }
                for (int k = -1; k <= 1; k += 2)
                {
                    float y0 = RopeY[0], y1 = RopeY[RopeY.Length - 1];
                    var c = alongX ? new Vector3(k * 0.75f, (y0 + y1) * 0.5f, s * half) : new Vector3(s * half, (y0 + y1) * 0.5f, k * 0.75f);
                    mb.Box(c, new Vector3(0.012f, (y1 - y0) * 0.5f, 0.012f), 0f, CTrim);
                }
            }
        }

        // ---- Зал ---------------------------------------------------------------------------------

        static void AddHall(MeshBuilder mb)
        {
            const float R = 15f;
            mb.Quad(new Vector3(-R, -PlatH, -R), new Vector3(-R, -PlatH, R), new Vector3(R, -PlatH, R),
                    new Vector3(R, -PlatH, -R), Vector3.up, CHall);
            for (int side = 0; side < 4; side++)
            {
                float yaw = side * 90f;
                // три ступени трибуны и стена за ними
                for (int k = 0; k < 3; k++)
                    mb.Box(Rot(yaw, new Vector3(0, -PlatH + 0.3f * (k + 1), 5.15f + 1.1f * k + 2.2f)),
                           new Vector3(11.5f, 0.3f * (k + 1), 2.75f), yaw, CStand);
                mb.Box(Rot(yaw, new Vector3(0, 2.0f, 13.2f)), new Vector3(14f, 3.2f, 0.3f), yaw, CWall);
            }
        }

        static Vector3 Rot(float yaw, Vector3 v) => Quaternion.Euler(0f, yaw, 0f) * v;

        static Color32 Mul(Color32 c, float k) =>
            new Color32((byte)Mathf.Min(255f, c.r * k), (byte)Mathf.Min(255f, c.g * k), (byte)Mathf.Min(255f, c.b * k), 255);

        // ---- Tick (ринг статичен) ----------------------------------------------------------------

        public void Tick(float dt, float udt) { }

        public void Dispose()
        {
            foreach (var o in _owned) GameUtil.Destroy(o);
            _owned.Clear();
            if (_root != null) GameUtil.Destroy(_root.gameObject);
        }

        // ---- Строитель сетки: грани с нормалями и цветом в вершинах ---------------------------------
        sealed class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>(4096), _n = new List<Vector3>(4096);
            readonly List<Color32> _c = new List<Color32>(4096);
            readonly List<int> _t = new List<int>(6144);

            // Четырёхугольник a→b→c→d по часовой стрелке, если смотреть со стороны нормали.
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color32 col)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int k = 0; k < 4; k++) { _n.Add(n); _c.Add(col); }
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
                _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }

            // Коробка: центр, полуразмеры, поворот вокруг вертикали (градусы).
            public void Box(Vector3 center, Vector3 half, float yawDeg, Color32 col, bool top = true)
            {
                var q = Quaternion.Euler(0f, yawDeg, 0f);
                Vector3 P(float x, float y, float z) => center + q * new Vector3(x * half.x, y * half.y, z * half.z);
                Vector3 N(float x, float y, float z) => q * new Vector3(x, y, z);
                Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), N(1, 0, 0), col);
                Quad(P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), N(-1, 0, 0), col);
                if (top) Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), N(0, 1, 0), col);
                Quad(P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), N(0, -1, 0), col);
                Quad(P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), N(0, 0, 1), col);
                Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), N(0, 0, -1), col);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32
                                                    : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetColors(_c);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
