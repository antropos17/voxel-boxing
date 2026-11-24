using System.Collections.Generic;
using UnityEngine;

namespace VoxGame
{
    // Статический ринг ~5×5 м. Всё строится кодом, без ресурсов.
    // НЕ MonoBehaviour — создаётся и управляется из FightDirector.
    public class RingBuilder
    {
        // ---- Параметры ринга --------------------------------------------
        const float HALF      = 2.1f;   // полуразмер ринга (внешний холст)
        const float INNER     = 1.95f;  // полуразмер внутренней области (боксёры)
        const float APRON     = 0.4f;   // ширина фартука за канатами
        const float POST_H    = 1.5f;   // высота угловых стоек
        const float POST_W    = 0.08f;  // ширина стойки
        const float ROPE_R    = 0.012f; // поперечный размер каната (отображается как плоская полоса)
        const float ROPE_T    = 0.025f; // толщина каната (полоса)
        static readonly float[] RopeY = { 0.50f, 0.78f, 1.06f, 1.34f };

        // Цвета
        static readonly Color32 C_CANVAS  = new Color32(255, 255, 255, 255);
        static readonly Color32 C_APRON   = new Color32(220, 220, 220, 255);
        static readonly Color32 C_POST    = new Color32(230, 200, 100, 255);
        static readonly Color32 C_ROPE_A  = new Color32(220,  40,  40, 255); // 2 нижних
        static readonly Color32 C_ROPE_B  = new Color32( 40,  40, 220, 255); // 2 верхних
        static readonly Color32 C_FLOOR   = new Color32( 55,  55,  60, 255);
        static readonly Color32 C_STEPS   = new Color32(100,  90,  80, 255);
        static readonly Color32 C_CUSHION = new Color32(220,  40,  40, 255);

        readonly Transform _root;
        Shader _vertColShader;
        readonly List<GameObject> _gos = new List<GameObject>();
        Light _keyLight, _fillLight;

        // ---- Конструктор ------------------------------------------------

        public RingBuilder()
        {
            var go = new GameObject("Ring");
            _root = go.transform;
        }

        // Построить всю геометрию и добавить источники света.
        public void Build(Transform parent)
        {
            _root.SetParent(parent, false);
            _vertColShader = Shader.Find("VoxGame/VertexColor");

            var mb = new MeshBuilder();

            // Пол ринга (внутри канатов)
            mb.AddQuad(
                new Vector3(-INNER, 0f, -INNER), new Vector3( INNER, 0f, -INNER),
                new Vector3( INNER, 0f,  INNER), new Vector3(-INNER, 0f,  INNER), C_CANVAS);

            // Фартук вокруг ринга (от INNER до HALF на каждой стороне)
            AddApron(mb);

            // Пол зала (за фартуком, до 7 м)
            AddHallFloor(mb);

            Spawn("floor", mb.Build());

            // Угловые стойки (отдельный меш для каждой)
            var mbPosts = new MeshBuilder();
            AddPosts(mbPosts);
            Spawn("posts", mbPosts.Build());

            // Канаты
            var mbRopes = new MeshBuilder();
            AddRopes(mbRopes);
            Spawn("ropes", mbRopes.Build());

            // Огни
            SetupLights();
        }

        // ---- Геометрия --------------------------------------------------

        static void AddApron(MeshBuilder mb)
        {
            // Четыре полосы фартука (+Z, -Z, +X, -X)
            mb.AddQuad(
                new Vector3(-HALF, 0f, -HALF), new Vector3(-INNER, 0f, -HALF),
                new Vector3(-INNER, 0f,  HALF), new Vector3(-HALF, 0f,  HALF), C_APRON); // -X
            mb.AddQuad(
                new Vector3( INNER, 0f, -HALF), new Vector3(HALF, 0f, -HALF),
                new Vector3( HALF, 0f,  HALF), new Vector3(INNER, 0f,  HALF), C_APRON); // +X
            mb.AddQuad(
                new Vector3(-INNER, 0f, -HALF), new Vector3(INNER, 0f, -HALF),
                new Vector3( INNER, 0f, -INNER), new Vector3(-INNER, 0f, -INNER), C_APRON); // -Z
            mb.AddQuad(
                new Vector3(-INNER, 0f, INNER), new Vector3(INNER, 0f, INNER),
                new Vector3( INNER, 0f,  HALF), new Vector3(-INNER, 0f,  HALF), C_APRON); // +Z
            // Угловые квадраты (4 угла)
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                float x0 = sx > 0 ? INNER : -HALF, x1 = sx > 0 ? HALF : -INNER;
                float z0 = sz > 0 ? INNER : -HALF, z1 = sz > 0 ? HALF : -INNER;
                if (x0 > x1) { float t = x0; x0 = x1; x1 = t; }
                if (z0 > z1) { float t = z0; z0 = z1; z1 = t; }
                mb.AddQuad(
                    new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0),
                    new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), C_APRON);
            }
        }

        static void AddHallFloor(MeshBuilder mb)
        {
            const float HALL = 7f;
            // Четыре полосы и 4 угла зала (y = -0.02, чуть ниже ринга)
            float y = -0.02f;
            // -X
            mb.AddQuad(new Vector3(-HALL, y, -HALL), new Vector3(-HALF, y, -HALL),
                        new Vector3(-HALF, y,  HALL), new Vector3(-HALL, y,  HALL), C_FLOOR);
            // +X
            mb.AddQuad(new Vector3( HALF, y, -HALL), new Vector3(HALL, y, -HALL),
                        new Vector3( HALL, y,  HALL), new Vector3(HALF, y,  HALL), C_FLOOR);
            // -Z
            mb.AddQuad(new Vector3(-HALF, y, -HALL), new Vector3(HALF, y, -HALL),
                        new Vector3( HALF, y, -HALF), new Vector3(-HALF, y, -HALF), C_FLOOR);
            // +Z
            mb.AddQuad(new Vector3(-HALF, y,  HALF), new Vector3(HALF, y, HALF),
                        new Vector3( HALF, y,  HALL), new Vector3(-HALF, y, HALL), C_FLOOR);
            // Углы
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                float x0 = sx > 0 ? HALF : -HALL, x1 = sx > 0 ? HALL : -HALF;
                float z0 = sz > 0 ? HALF : -HALL, z1 = sz > 0 ? HALL : -HALF;
                mb.AddQuad(new Vector3(x0, y, z0), new Vector3(x1, y, z0),
                            new Vector3(x1, y, z1), new Vector3(x0, y, z1), C_FLOOR);
            }

            // Небольшой скос перед рингом (ступени не делаем, только маркировка)
            mb.AddBox(new Vector3(-HALF, -0.1f, -HALF - 0.3f),
                       new Vector3( HALF,  0.0f, -HALF), C_STEPS);
        }

        static void AddPosts(MeshBuilder mb)
        {
            // 4 угловые стойки
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                float x = sx * HALF, z = sz * HALF;
                float h2 = POST_W * 0.5f;
                // Вертикальная стойка
                mb.AddBox(new Vector3(x - h2, 0f,       z - h2),
                           new Vector3(x + h2, POST_H,  z + h2), C_POST);
                // Подушка на вершине стойки (чуть шире и цветная)
                mb.AddBox(new Vector3(x - h2 - 0.02f, POST_H,          z - h2 - 0.02f),
                           new Vector3(x + h2 + 0.02f, POST_H + 0.07f, z + h2 + 0.02f), C_CUSHION);
            }
        }

        static void AddRopes(MeshBuilder mb)
        {
            // 4 каната (горизонтальные полосы по периметру)
            for (int ri = 0; ri < RopeY.Length; ri++)
            {
                float y    = RopeY[ri];
                Color32 c  = ri < 2 ? C_ROPE_A : C_ROPE_B;
                float h2   = ROPE_T * 0.5f;

                // Одна петля каната вокруг ринга (по 4 стороны)
                float post = HALF; // от стойки до стойки
                // -Z сторона
                mb.AddBox(new Vector3(-post, y - h2, -post - ROPE_T), new Vector3(post, y + h2, -post), c);
                // +Z сторона
                mb.AddBox(new Vector3(-post, y - h2,  post), new Vector3(post, y + h2,  post + ROPE_T), c);
                // -X сторона
                mb.AddBox(new Vector3(-post - ROPE_T, y - h2, -post), new Vector3(-post, y + h2, post), c);
                // +X сторона
                mb.AddBox(new Vector3( post, y - h2, -post), new Vector3( post + ROPE_T, y + h2, post), c);
            }
        }

        // ---- Освещение --------------------------------------------------

        void SetupLights()
        {
            // Ключевой свет: тёплый, сверху/спереди
            var kGO = new GameObject("KeyLight");
            kGO.transform.SetParent(_root, false);
            kGO.transform.position = new Vector3(-1.5f, 4.5f, -4.0f);
            kGO.transform.LookAt(Vector3.zero);
            _keyLight = kGO.AddComponent<Light>();
            _keyLight.type      = LightType.Directional;
            _keyLight.color     = new Color(1.00f, 0.95f, 0.82f);
            _keyLight.intensity = 1.1f;
            _keyLight.shadows   = LightShadows.None;
            _gos.Add(kGO);

            // Контровой свет: прохладный, сзади/сверху
            var fGO = new GameObject("FillLight");
            fGO.transform.SetParent(_root, false);
            fGO.transform.position = new Vector3(1.5f, 3.0f, 4.5f);
            fGO.transform.LookAt(Vector3.zero);
            _fillLight = fGO.AddComponent<Light>();
            _fillLight.type      = LightType.Directional;
            _fillLight.color     = new Color(0.72f, 0.82f, 1.00f);
            _fillLight.intensity = 0.35f;
            _fillLight.shadows   = LightShadows.None;
            _gos.Add(fGO);
        }

        // ---- Вспомогательные методы -------------------------------------

        void Spawn(string name, Mesh mesh)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(_root, false);
            var mf  = go.AddComponent<MeshFilter>();
            var mr  = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = new Material(_vertColShader != null
                ? _vertColShader
                : Shader.Find("Hidden/InternalErrorShader"));
            mr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows       = false;
            _gos.Add(go);
        }

        // ---- Tick (ринг статичен — no-op) --------------------------------

        public void Tick(float dt, float udt) { }

        // ---- Dispose ----------------------------------------------------

        public void Dispose()
        {
            foreach (var go in _gos)
                DestroyObj(go);
            _gos.Clear();
            if (_root != null) DestroyObj(_root.gameObject);
        }

        static void DestroyObj(UnityEngine.Object o)
        {
            if (o == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(o); return; }
#endif
            UnityEngine.Object.Destroy(o);
        }

        // ================================================================
        // Внутренний строитель меша (вершины+цвета+треугольники)
        // ================================================================
        sealed class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>(256);
            readonly List<Color32> _c = new List<Color32>(256);
            readonly List<int>     _t = new List<int>(512);

            // Добавить четырёхугольник (a→b→c, a→c→d)
            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col)
            {
                int base0 = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                _c.Add(col); _c.Add(col); _c.Add(col); _c.Add(col);
                _t.Add(base0); _t.Add(base0 + 1); _t.Add(base0 + 2);
                _t.Add(base0); _t.Add(base0 + 2); _t.Add(base0 + 3);
            }

            // Добавить параллелепипед по min/max (6 граней, 2 треугольника каждая)
            public void AddBox(Vector3 min, Vector3 max, Color32 col)
            {
                // Порядок граней: нижняя, верхняя, -Z, +Z, -X, +X
                float x0 = min.x, y0 = min.y, z0 = min.z;
                float x1 = max.x, y1 = max.y, z1 = max.z;

                AddQuad(new Vector3(x0,y0,z0),new Vector3(x1,y0,z0),new Vector3(x1,y0,z1),new Vector3(x0,y0,z1),col); // низ
                AddQuad(new Vector3(x0,y1,z1),new Vector3(x1,y1,z1),new Vector3(x1,y1,z0),new Vector3(x0,y1,z0),col); // верх
                AddQuad(new Vector3(x0,y1,z0),new Vector3(x1,y1,z0),new Vector3(x1,y0,z0),new Vector3(x0,y0,z0),col); // -Z
                AddQuad(new Vector3(x0,y0,z1),new Vector3(x1,y0,z1),new Vector3(x1,y1,z1),new Vector3(x0,y1,z1),col); // +Z
                AddQuad(new Vector3(x0,y0,z0),new Vector3(x0,y0,z1),new Vector3(x0,y1,z1),new Vector3(x0,y1,z0),col); // -X
                AddQuad(new Vector3(x1,y0,z1),new Vector3(x1,y0,z0),new Vector3(x1,y1,z0),new Vector3(x1,y1,z1),col); // +X
            }

            // Собрать Mesh
            public Mesh Build()
            {
                var mesh = new Mesh { name = "RingMesh" };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(_v);
                mesh.SetColors(_c);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
