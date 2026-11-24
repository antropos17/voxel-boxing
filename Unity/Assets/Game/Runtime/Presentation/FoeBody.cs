using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using VoxProbe;

namespace VoxGame
{
    // Параметры внешности соперника. Директор заполняет из FighterDef.
    public struct FoeLook
    {
        public Color skin, trunks, accent, gloveA, gloveB;
        public float shoulderWidth; // множитель ширины плеч: 0.85–1.15
        public float neckThickness; // множитель толщины шеи: 0.85–1.15
        public float scale;         // масштаб корня: 0.90–1.10

        public static FoeLook FromDef(FighterDef def) => new FoeLook
        {
            skin          = def.colors.skin,
            trunks        = def.colors.trunks,
            accent        = def.colors.accent,
            gloveA        = def.colors.gloveA,
            gloveB        = def.colors.gloveB,
            shoulderWidth = def.build.shoulderMult,
            neckThickness = def.build.neckMult,
            scale         = def.scale,
        };
    }

    // Воксельное тело соперника: шея, торс, плечи, руки, перчатки.
    // НЕ MonoBehaviour — создаётся и управляется из FightDirector.
    public class FoeBody
    {
        // Корень соперника (масштаб = look.scale). FightDirector управляет позицией корня.
        public readonly Transform root;
        // Точка крепления AnatView.root (+0.046Y +0.026Z от основания анатомической головы).
        public readonly Transform headMount;

        // ---- Константы --------------------------------------------------
        const float VS_T = 0.0055f; // торс: 5.5 мм
        const float VS_N = 0.0065f; // шея:  6.5 мм
        const float VS_A = 0.0085f; // рука: 8.5 мм
        const float VS_G = 0.0052f; // перч: 5.2 мм

        // Высоты ключевых точек (метры, от root)
        const float Y_TORSO   = 0.86f;  // нижний срез торса (верх шорт)
        const float Y_NECK    = 1.38f;  // низ шеи
        const float Y_SHOULDER = 1.37f; // высота плечевого сустава
        const float UPPER_LEN = 0.26f;  // плечо → локоть
        const float FORE_LEN  = 0.26f;  // локоть → запястье

        // Индексы палитры тела
        const byte B_SKIN   = 1; // кожа
        const byte B_SHADOW = 2; // тень кожи
        const byte B_TRUNK  = 3; // трусы
        const byte B_ACCENT = 4; // акцент

        // Индексы палитры перчаток
        const byte G_MAIN = 1;
        const byte G_SEC  = 2;

        // ---- Тела -------------------------------------------------------
        VoxBody torsoBody, neckBody;
        readonly VoxBody[] shoulderBodies  = new VoxBody[2];
        readonly VoxBody[] upperArmBodies  = new VoxBody[2];
        readonly VoxBody[] foreArmBodies   = new VoxBody[2];
        readonly VoxBody[] gloveBodies     = new VoxBody[2];
        Material bodyMat, gloveMat;

        // ---- Иерархия IK-суставов (дочерние root) -----------------------
        readonly Transform[] upperArmRoot  = new Transform[2];
        readonly Transform[] foreArmRoot   = new Transform[2];
        readonly Transform[] gloveRoot     = new Transform[2];
        // Позиции плеч в локальных координатах root
        readonly Vector3[] shoulderLocal   = new Vector3[2];
        float shoulderHalfW;

        // ---- Пружинное состояние (без аллокаций в Tick) -----------------
        float swayY, swayYVel;
        float impX, impXVel;
        readonly Vector3[] glovePos = new Vector3[2];
        readonly Vector3[] gloveVel = new Vector3[2];

        // ---- Поза -------------------------------------------------------
        enum Pose { Guard, WindupL, WindupR, StrikeL, StrikeR, Stagger, Down, Rise, Victory, Defeat }
        static readonly Vector3[,] PoseTgt = new Vector3[10, 2]
        {
            { new Vector3(-0.14f, 1.32f, 0.14f), new Vector3( 0.14f, 1.32f, 0.14f) }, // Guard
            { new Vector3(-0.28f, 1.26f,-0.08f), new Vector3( 0.14f, 1.32f, 0.14f) }, // WindupL
            { new Vector3(-0.14f, 1.32f, 0.14f), new Vector3( 0.28f, 1.26f,-0.08f) }, // WindupR
            { new Vector3( 0.04f, 1.38f, 0.56f), new Vector3( 0.14f, 1.32f, 0.14f) }, // StrikeL
            { new Vector3(-0.14f, 1.32f, 0.14f), new Vector3(-0.04f, 1.38f, 0.56f) }, // StrikeR
            { new Vector3(-0.22f, 1.08f, 0.05f), new Vector3( 0.22f, 1.08f, 0.05f) }, // Stagger
            { new Vector3(-0.28f, 0.28f, 0.10f), new Vector3( 0.28f, 0.28f, 0.10f) }, // Down
            { new Vector3(-0.18f, 1.14f, 0.10f), new Vector3( 0.18f, 1.14f, 0.10f) }, // Rise
            { new Vector3(-0.30f, 1.80f, 0.00f), new Vector3( 0.30f, 1.80f, 0.00f) }, // Victory
            { new Vector3(-0.20f, 0.78f, 0.00f), new Vector3( 0.20f, 0.78f, 0.00f) }, // Defeat
        };
        Pose currentPose = Pose.Guard;

        // ---- Конструктор ------------------------------------------------

        public FoeBody(Transform parent, FighterDef def, Material voxMat)
        {
            var look = FoeLook.FromDef(def);
            shoulderHalfW = 0.195f * look.shoulderWidth;

            var rootGO = new GameObject("FoeBody");
            rootGO.transform.SetParent(parent, false);
            root = rootGO.transform;
            root.localScale = Vector3.one * look.scale;

            bodyMat  = new Material(voxMat);
            gloveMat = new Material(voxMat);
            SetPalettes(look);

            BuildAllParts(look);

            // Позиции плеч (локальные, от root) — наружный сустав плеча
            shoulderLocal[0] = new Vector3(-(shoulderHalfW + 0.17f), Y_SHOULDER, 0f);
            shoulderLocal[1] = new Vector3(+(shoulderHalfW + 0.17f), Y_SHOULDER, 0f);

            // Начальное положение перчаток из позы Guard
            for (int i = 0; i < 2; i++)
                glovePos[i] = PoseTgt[(int)Pose.Guard, i];

            // headMount: локальная позиция = низ шеи + высота шеи + смещение PIV
            float neckH = 22 * VS_N; // 0.143 м
            var hmGO = new GameObject("headMount");
            hmGO.transform.SetParent(root, false);
            hmGO.transform.localPosition = new Vector3(0f, Y_NECK + neckH + 0.046f, 0.026f);
            headMount = hmGO.transform;
        }

        // ---- Построение тела --------------------------------------------

        void BuildAllParts(FoeLook look)
        {
            var builds = new List<(VoxBody, VoxModel)>(10);

            // Торс: 92×100×46, VS=5.5мм
            {
                int sx = 92, sy = 100, sz = 46;
                var pRoot = Part("torso", root,
                    new Vector3(-sx * 0.5f * VS_T, Y_TORSO, -sz * 0.5f * VS_T));
                torsoBody = new VoxBody(pRoot, bodyMat, VS_T);
                var m = new VoxModel(sx, sy, sz);
                float cx = sx * .5f, cy = sy * .5f, cz = sz * .5f;
                m.FillRoundBox(cx, cy + 2f, cz, 40f, 46f, 18f, 6f, B_SKIN);
                m.FillEllipsoid(cx - 36f, cy + 42f, cz, 12f, 10f, 10f, B_SKIN);
                m.FillEllipsoid(cx + 36f, cy + 42f, cz, 12f, 10f, 10f, B_SKIN);
                m.FillBox(0, 0, 0, sx, 18, sz, B_TRUNK);
                m.FillBox(0, 16, 0, sx, 20, sz, B_ACCENT);
                builds.Add((torsoBody, m));
            }

            // Шея: 18×22×18, VS=6.5мм
            {
                int sx = 18, sy = 22, sz = 18;
                float nt = look.neckThickness;
                var pRoot = Part("neck", root,
                    new Vector3(-sx * 0.5f * VS_N, Y_NECK, -sz * 0.5f * VS_N));
                neckBody = new VoxBody(pRoot, bodyMat, VS_N);
                var m = new VoxModel(sx, sy, sz);
                m.FillCapsule(sx * .5f, 3f, sz * .5f, sx * .5f, 19f, sz * .5f, 6f * nt, B_SKIN);
                builds.Add((neckBody, m));
            }

            // Плечи: 42×14×14, VS=8.5мм; ориентация вдоль +Y → ±X через поворот
            for (int s = 0; s < 2; s++)
            {
                int gx = 42, gy = 14, gz = 14;
                float sign = s == 0 ? 1f : -1f; // s=0 Left(+Z=−90→-X), s=1 Right(−90→+X)
                var pRoot = Part($"shoulder{s}", root,
                    new Vector3(0f, Y_SHOULDER, -gz * 0.5f * VS_A));
                // Поворот: +Y → ±X
                // s=0 Left: Euler(0,0,90) → Y→-X; s=1 Right: Euler(0,0,-90) → Y→+X
                pRoot.localRotation = Quaternion.Euler(0f, 0f, sign * 90f);
                shoulderBodies[s] = new VoxBody(pRoot, bodyMat, VS_A);
                var m = new VoxModel(gx, gy, gz);
                m.FillCapsule(gx * .5f, 3f, gz * .5f, gx * .5f - 4f, gz * .5f, gz * .5f, 5f, B_SKIN);
                builds.Add((shoulderBodies[s], m));
            }

            // Плечевые кости + предплечья + перчатки: 14×32×14, VS=8.5мм
            // (Позиция/поворот обновляются в Tick через IK)
            for (int s = 0; s < 2; s++)
            {
                // Плечевая кость
                {
                    int gx = 14, gy = 32, gz = 14;
                    var pRoot = Part($"upperArm{s}", root, Vector3.zero);
                    upperArmRoot[s] = pRoot;
                    // Смещение mesh в локальных: от (0,0,0) вдоль +Y (поворот joint вдоль +Y→вниз)
                    var mesh = Part($"upperArmMesh{s}", pRoot,
                        new Vector3(-gx * 0.5f * VS_A, 0f, -gz * 0.5f * VS_A));
                    upperArmBodies[s] = new VoxBody(mesh, bodyMat, VS_A);
                    var m = new VoxModel(gx, gy, gz);
                    m.FillCapsule(gx * .5f, 4f, gz * .5f, gx * .5f, 28f, gz * .5f, 5f, B_SKIN);
                    builds.Add((upperArmBodies[s], m));
                }

                // Предплечье
                {
                    int gx = 14, gy = 32, gz = 14;
                    var pRoot = Part($"foreArm{s}", root, Vector3.zero);
                    foreArmRoot[s] = pRoot;
                    var mesh = Part($"foreArmMesh{s}", pRoot,
                        new Vector3(-gx * 0.5f * VS_A, 0f, -gz * 0.5f * VS_A));
                    foreArmBodies[s] = new VoxBody(mesh, bodyMat, VS_A);
                    var m = new VoxModel(gx, gy, gz);
                    m.FillCapsule(gx * .5f, 4f, gz * .5f, gx * .5f, 28f, gz * .5f, 4.5f, B_SKIN);
                    builds.Add((foreArmBodies[s], m));
                }

                // Перчатка: 30×24×52, VS=5.2мм
                {
                    int gx = 30, gy = 24, gz = 52;
                    var pRoot = Part($"glove{s}", root, Vector3.zero);
                    gloveRoot[s] = pRoot;
                    // Mesh: X/Y центрированы, Z начинается от pRoot (вперёд)
                    var mesh = Part($"gloveMesh{s}", pRoot,
                        new Vector3(-gx * 0.5f * VS_G, -gy * 0.5f * VS_G, 0f));
                    gloveBodies[s] = new VoxBody(mesh, gloveMat, VS_G);
                    var m = new VoxModel(gx, gy, gz);
                    m.FillRoundBox(gx * .5f, gy * .5f, gz * .5f + 4f, 11f, 9f, 16f, 4f, G_MAIN);
                    m.FillRoundBox(gx * .5f, gy * .5f, 10f, 9f, 7.5f, 8f, 3f, G_SEC);
                    builds.Add((gloveBodies[s], m));
                }
            }

            VoxModel.BuildAll(builds);
        }

        static Transform Part(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        void SetPalettes(FoeLook look)
        {
            var bp = new Color[16];
            bp[0] = Color.clear;
            bp[B_SKIN]   = look.skin;
            bp[B_SHADOW] = look.skin * 0.78f;
            bp[B_TRUNK]  = look.trunks;
            bp[B_ACCENT] = look.accent;
            VoxModel.SetPalette(bodyMat, bp);

            var gp = new Color[16];
            gp[0] = Color.clear;
            gp[G_MAIN] = look.gloveA;
            gp[G_SEC]  = look.gloveB;
            VoxModel.SetPalette(gloveMat, gp);
        }

        // ---- Tick -------------------------------------------------------

        public void Tick(AICommand cmd, float sdt)
        {
            currentPose = AIPose(cmd);

            // Пружинная анимация перчаток (без new)
            const float K = 55f, C = 11f;
            for (int i = 0; i < 2; i++)
                VecSpring(ref glovePos[i], ref gloveVel[i], PoseTgt[(int)currentPose, i], K, C, sdt);

            // Раскачка торса
            float swayTgt = Mathf.Sin(Time.time * 1.4f) *
                            (currentPose == Pose.Guard ? 2f : 0.5f);
            FloatSpring(ref swayY, ref swayYVel, swayTgt, 18f, 7f, sdt);
            FloatSpring(ref impX, ref impXVel, 0f, 10f, 9f, sdt);

            if (torsoBody?.root?.parent != null)
                torsoBody.root.parent.localRotation = Quaternion.Euler(impX * 14f, swayY, 0f);

            // IK рук
            SolveArms();
        }

        void SolveArms()
        {
            for (int s = 0; s < 2; s++)
            {
                Vector3 shoulder = root.TransformPoint(shoulderLocal[s]);
                Vector3 wristW   = root.TransformPoint(glovePos[s]);
                Vector3 pole     = root.up;
                Vector3 elbow    = Solve2BoneIK(shoulder, wristW, UPPER_LEN, FORE_LEN, pole);

                // Плечевая кость: от shoulder к elbow
                AimJoint(upperArmRoot[s], shoulder, elbow);

                // Предплечье: от elbow к wrist
                AimJoint(foreArmRoot[s], elbow, wristW);

                // Перчатка: позиция = запястье, ориентация = направление удара
                if (gloveRoot[s] != null)
                {
                    gloveRoot[s].position = wristW;
                    gloveRoot[s].rotation = root.rotation;
                }
            }
        }

        // Поворачивает joint так, чтобы +Y смотрел от from к to.
        static void AimJoint(Transform t, Vector3 from, Vector3 to)
        {
            if (t == null) return;
            t.position = from;
            Vector3 dir = to - from;
            if (dir.sqrMagnitude > 1e-9f)
                t.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        }

        static Vector3 Solve2BoneIK(Vector3 sh, Vector3 wr, float la, float lb, Vector3 pole)
        {
            float d = Vector3.Distance(sh, wr);
            d = Mathf.Clamp(d, Mathf.Abs(la - lb) + 0.001f, la + lb - 0.001f);
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            float a    = Mathf.Acos(cosA);
            Vector3 fw = (wr - sh).normalized;
            Vector3 cr = Vector3.Cross(pole - sh, fw);
            Vector3 bd = cr.sqrMagnitude > 1e-6f
                ? Vector3.Cross(fw, cr.normalized).normalized
                : Vector3.up;
            return sh + fw * (la * Mathf.Cos(a)) + bd * (la * Mathf.Sin(a));
        }

        static Pose AIPose(AICommand cmd) => cmd.newPhase switch
        {
            FightPhase.Windup  => cmd.side < 0 ? Pose.WindupL : Pose.WindupR,
            FightPhase.Strike  => cmd.side < 0 ? Pose.StrikeL : Pose.StrikeR,
            FightPhase.Stagger => Pose.Stagger,
            FightPhase.Down    => Pose.Down,
            FightPhase.Rise    => Pose.Rise,
            FightPhase.KO      => Pose.Defeat,
            _                  => Pose.Guard,
        };

        // ---- Импульс от удара (нагибает торс) ---------------------------

        public void AddImpulse(Vector3 worldImpulse)
        {
            Vector3 loc = root.InverseTransformDirection(worldImpulse);
            impXVel  += loc.z * 7f;
            swayYVel += loc.x * 4f;
        }

        // ---- Пружины (без аллокаций) ------------------------------------

        static void FloatSpring(ref float v, ref float vel, float tgt, float k, float c, float dt)
        {
            vel += (tgt - v) * k * dt;
            vel *= 1f - c * dt;
            v   += vel * dt;
        }

        static void VecSpring(ref Vector3 v, ref Vector3 vel, Vector3 tgt, float k, float c, float dt)
        {
            vel += (tgt - v) * k * dt;
            vel *= 1f - c * dt;
            v   += vel * dt;
        }

        // ---- Dispose ----------------------------------------------------

        public void Dispose()
        {
            torsoBody?.Dispose();
            neckBody?.Dispose();
            for (int i = 0; i < 2; i++)
            {
                shoulderBodies[i]?.Dispose();
                upperArmBodies[i]?.Dispose();
                foreArmBodies[i]?.Dispose();
                gloveBodies[i]?.Dispose();
            }
            DestroyObj(bodyMat);
            DestroyObj(gloveMat);
            if (root != null) DestroyObj(root.gameObject);
        }

        static void DestroyObj(UnityEngine.Object o)
        {
            if (o == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(o); return; }
#endif
            UnityEngine.Object.Destroy(o);
        }
    }
}
