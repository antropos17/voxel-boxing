using UnityEngine;

namespace VoxGame
{
    // Воксельное тело соперника по пояс и ниже: торс с трусами, плечи, предплечья, перчатки.
    // Соперник стоит в начале координат корня лицом в +Z. Голова (AnatView) телу не принадлежит:
    // директор каждый кадр ставит её в headMount. Накладки головы (HeadGear) — дочерние к headMount.
    // Фигура, трусы, перчатки, бинты и отметины берутся из FighterDef; тон кожи у всех один — как у головы.
    // НЕ MonoBehaviour — создаётся и управляется из FightDirector.
    public class FoeBody
    {
        public readonly Transform root;       // корень соперника (масштаб тела = FighterDef.scale)
        public readonly Transform headMount;  // сюда ставится начало координат головы (уровень ушей)
        public readonly HeadGear  gear;       // причёска, борода, головной убор и мелочи головы

        // ---- Размеры (метры, от корня при масштабе 1) -------------------------------------------
        const float WaistY  = 0.98f;   // пояс: начало координат формы торса
        const float HipDrop = 0.10f;   // ось наклона корпуса ниже пояса
        const float HeadY   = 0.64f;   // от пояса до начала координат головы (шаблон): низ шеи головы (−19.95 см) уходит в трапеции
        const float HeadZ   = 0.02f;
        const float NeckLen = 0.1995f; // от начала координат головы до низа её шеи
        const float ShoulderX = 0.195f, ShoulderY = 0.415f;
        const float TorsoCell = 0.7f;  // см
        const float StretchFrom = 10f; // см от пояса: выше корпус растягивается по длине (torsoLen), трусы — нет

        // ---- Пропорции под анатомическую голову (подгоняются по снимкам) -----------------------------
        // Голова не масштабируется (≈19 см в ширину с ушами, ≈24 см от подбородка до макушки), поэтому ширины тела
        // задаются в долях ширины головы и не зависят от масштаба корня; масштаб меняет только рост и толщину.
        const float HeadWidthCm   = 19f;
        const float ShoulderMinK  = 2.20f, ShoulderMaxK = 2.50f;   // ширина по дельтам / ширина головы
        const float HipMinK       = 1.42f, HipMaxK      = 1.85f;   // ширина трусов / ширина головы
        const float NeckMinK      = 1.02f, NeckMaxK     = 1.25f;   // основание шеи и трапеции к шаблону
        const float TorsoLenBase  = 0.93f;  // шаблон корпуса длинноват: плечи ~22 см ниже ушей, как у человека
        const float DeltX = 18.6f, DeltR = 5.8f, HipR = 17.2f;     // полуширины шаблона: центр и радиус дельты, таз трусов

        // ---- Голова и перчатки в стойке ------------------------------------------------------------
        const float HeadPitchStance = 4f;    // лёгкий наклон головы вперёд в стойке, град (крен в стойке — 0)
        const float HeadYawFollow   = 0.3f;  // доля скрутки корпуса, которую повторяет голова
        const float StaggerRoll = 9f, DownRoll = 8f;
        const float StrikeSpeed = 13f;       // предел скорости перчатки в ударе, м/с (в осях корня)
        const float RefHeadY = 1.64f;        // высота головы, под которую заданы позы замаха, раскрытия и подъёма
        // центр перчатки от начала координат головы (м): x — наружу от средней линии, y — от уровня ушей, z — вперёд.
        // Перчатка стоит костяшками вверх-вперёд и выступает над центром на ~6 см: верх у скулы, лицо открыто.
        // Перчатки вынесены вперёд лица: у ушей кисть уходила в голову, а локти — в стороны на высоту плеч.
        static readonly Vector3 IdleGloveOff  = new Vector3(0.165f, -0.085f, 0.17f);
        static readonly Vector3 GuardGloveOff = new Vector3(0.155f, -0.070f, 0.19f);

        // Номера цветов торса
        const byte CSkin = 1, CTrunks = 2, CBelt = 3, CTrunksDark = 4, CStripe = 5, CInk = 6, CScar = 7, CBodyHair = 8;
        // Номера цветов голеней
        const byte LSkin = 1, LBoot = 2, LSole = 3, LSock = 4;
        const float KneeY = -44f, SoleY = -WaistY * 100f;   // колено и настил от пояса, см

        // ---- Объекты -----------------------------------------------------------------------------
        readonly Transform torsoJoint;
        readonly Transform[] upper = new Transform[2], fore = new Transform[2], glove = new Transform[2];
        readonly Vector3[] shoulderLocal = new Vector3[2];   // в осях torsoJoint
        readonly Mesh[] meshes;
        readonly float scale;

        // ---- Фигура (множители формы торса) --------------------------------------------------------
        readonly float shoulderK, waistK, chestK, armK, neckK, lenK, belly;
        readonly int pattern;
        readonly BodyMark mark;
        readonly float markSide;

        // ---- Состояние анимации ------------------------------------------------------------------
        readonly Vector3[] glovePos = new Vector3[2];   // центры перчаток в осях корня (до масштаба)
        float lean, twist, drop, lunge, anim;
        float impLean, impLeanV, impTwist, impTwistV;
        float headPitch = HeadPitchStance, headYaw, headRoll;   // поза головы в осях корня, град
        float headDy;   // поправка поз, заданных абсолютной высотой, к настоящей высоте головы
        FightPhase phase = FightPhase.Idle;

        // Рука в руке рефери (победа соперника): номер руки и точка запястья (мир); -1 — не держат
        int holdArm = -1;
        Vector3 holdWrist;
        float holdW;

        // Рука 0 — на экране слева (мир +X, правая рука соперника), рука 1 — справа.
        static float ArmSign(int s) => s == 0 ? 1f : -1f;

        // Поле множителя: 0 (не задано) читается как 1
        static float Or1(float v) => v > 0f ? v : 1f;

        public FoeBody(Transform parent, FighterDef def, Material mat)
        {
            var b = def.build;
            scale = Mathf.Clamp(def.scale <= 0f ? 1f : def.scale, 0.9f, 1.1f);
            belly     = Mathf.InverseLerp(1.10f, 1.30f, Or1(b.waistMult));
            chestK    = Mathf.Lerp(0.90f, 1.16f, Mathf.InverseLerp(0.85f, 1.20f, Or1(b.chestMult)));
            armK      = Mathf.Lerp(0.88f, 1.22f, Mathf.InverseLerp(0.85f, 1.25f, Or1(b.armMult)));
            // ширина по дельтам — 2.2–2.5 ширины головы при любом масштабе корня
            float shW = 0.5f * HeadWidthCm * Mathf.Lerp(ShoulderMinK, ShoulderMaxK, Mathf.InverseLerp(0.85f, 1.15f, Or1(b.shoulderMult)));
            shoulderK = shW / ((DeltX + DeltR * armK) * scale);
            float hipW = 0.5f * HeadWidthCm * Mathf.Lerp(HipMinK, HipMaxK, Mathf.InverseLerp(0.85f, 1.30f, Or1(b.waistMult)));
            waistK    = hipW / (HipR * scale);
            // шея головы своя и не меняется; основание шеи и трапеции тела — в мире одной толщины при любом масштабе
            // и ширине плеч (в шаблоне оно сужено на shoulderK), только толще шеи головы, не тоньше
            neckK     = 0.9f * Mathf.Lerp(NeckMinK, NeckMaxK, Mathf.InverseLerp(0.80f, 1.20f, Or1(b.neckMult))) / (shoulderK * scale);
            lenK      = Mathf.Clamp(Or1(b.torsoLen), 0.92f, 1.10f) * TorsoLenBase;
            pattern   = def.colors.trunksPattern;
            mark      = def.mark;
            markSide  = def.markSide < 0 ? -1f : 1f;

            root = new GameObject("FoeBody").transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * scale;

            torsoJoint = new GameObject("torsoJoint").transform;
            torsoJoint.SetParent(root, false);
            torsoJoint.localPosition = new Vector3(0f, WaistY - HipDrop, 0f);

            headMount = new GameObject("headMount").transform;
            headMount.SetParent(torsoJoint, false);
            headMount.localPosition = HeadLocal();
            headDy = WaistY - HipDrop + HeadLocal().y - RefHeadY;

            Color32 skin = BoxerParts.HeadSkin;
            var torsoMesh = BuildTorso(skin, def);
            var t = VoxModel.Spawn("torso", torsoJoint, torsoMesh, mat);
            t.localPosition = new Vector3(0f, HipDrop, 0f);
            var legsMesh = BuildLegs(skin, def);
            var lg = VoxModel.Spawn("legs", torsoJoint, legsMesh, mat);
            lg.localPosition = new Vector3(0f, HipDrop, 0f);

            // татуировка-браслет — только на одном плече (рука 0 — сторона +X)
            int bandArm = mark == BodyMark.ArmBand ? (markSide > 0f ? 0 : 1) : -1;
            var upperPlain = BoxerParts.UpperArmMesh(skin, armK);
            var upperBand  = bandArm >= 0 ? BoxerParts.UpperArmMesh(skin, armK, true) : null;
            var foreMesh   = BoxerParts.ForeArmMesh(skin, Mathf.Lerp(1f, armK, 0.8f), def.colors.handWraps, def.colors.wraps);
            var gloveMeshes = new[]
            {
                BoxerParts.GloveMesh(-1, def.colors.gloveA, def.colors.gloveB),
                BoxerParts.GloveMesh(+1, def.colors.gloveA, def.colors.gloveB),
            };
            meshes = upperBand != null
                ? new[] { torsoMesh, legsMesh, upperPlain, upperBand, foreMesh, gloveMeshes[0], gloveMeshes[1] }
                : new[] { torsoMesh, legsMesh, upperPlain, foreMesh, gloveMeshes[0], gloveMeshes[1] };

            float shY = HipDrop + StretchY(ShoulderY * 100f) * 0.01f;
            for (int s = 0; s < 2; s++)
            {
                upper[s] = VoxModel.Spawn("upperArm" + s, root, s == bandArm ? upperBand : upperPlain, mat);
                fore[s]  = VoxModel.Spawn("foreArm" + s, root, foreMesh, mat);
                glove[s] = VoxModel.Spawn("glove" + s, root, gloveMeshes[s], mat);
                shoulderLocal[s] = new Vector3(ArmSign(s) * ShoulderX * shoulderK, shY, 0f);
                glovePos[s] = AtHead(s, IdleGloveOff, 0f);
            }
            Pose(Vector3.zero);

            gear = new HeadGear();
            gear.Build(headMount, def, mat);
        }

        // ---- Форма торса (см; начало — пояс, +Z — грудь) -------------------------------------------
        // Формы заданы для шаблонной фигуры; Map переводит точку тела в осях шаблона: выше StretchFrom корпус
        // растянут по длине, по ширине талия переходит в плечи, грудная клетка глубже или мельче.

        float StretchY(float y) => y > StretchFrom ? StretchFrom + (y - StretchFrom) * lenK : y;

        static float Smooth(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        void Map(ref float x, ref float y, ref float z)
        {
            if (y > StretchFrom) y = StretchFrom + (y - StretchFrom) / lenK;
            x /= Mathf.Lerp(waistK, shoulderK, Smooth(8f, 36f, y));
            z /= Mathf.Lerp(1f, chestK, Smooth(14f, 30f, y));
        }

        // Живот, таз и (у тяжеловеса) брюшко — общая часть корпуса и трусов
        float Lower(float x, float y, float z)
        {
            float d = VoxModel.Smin(VoxModel.Ell(x, y - 12, z - 1.5f, 13.2f, 13.5f, 9.6f),
                                    VoxModel.Ell(x, y - 2, z, 14.4f, 7.5f, 10.4f), 4);
            if (belly > 0.01f)
                d = VoxModel.Smin(d, VoxModel.Ell(x, y - 13, z - 1.5f - 3.2f * belly, 12.6f + 1.5f * belly, 12.0f,
                                                  7.5f + 3.0f * belly), 6);
            return d;
        }

        // Корпус в осях шаблона
        float BodyM(float x, float y, float z)
        {
            float ax = Mathf.Abs(x), d;
            d = VoxModel.Ell(x, y - 29, z + 1, 15.0f, 16.5f, 10.8f);
            d = VoxModel.Smin(d, Lower(x, y, z), 5);
            d = VoxModel.Smin(d, VoxModel.Ell(ax - 7.4f, y - 33.8f, z - 6.0f, 8.8f, 5.8f, 3.0f), 2.2f);   // грудные
            d = VoxModel.Smin(d, VoxModel.Ell(ax - 10.5f, y - 27, z + 3.5f, 6.5f, 13, 7), 4);            // широчайшие
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 34, z + 4.5f, 15.5f, 13, 8.6f), 4);                 // спина
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 45.5f, z + 3.0f, 11.5f, 4.8f, 6.0f), 4);            // надплечья
            d = VoxModel.Smin(d, VoxModel.Ell(ax - 18.6f, y - 41.2f, z + 0.3f, 5.8f * armK, 6.8f, 5.9f * armK), 3);  // дельты
            d = VoxModel.Smin(d, VoxModel.Cap(ax, y, z, 2, 45, 3.5f, 16, 46.5f, 0, 1.8f), 2);            // ключицы
            // основание шеи под шею головы (у головы низ шеи — эллипс 8.6 × 5.8 см) и скаты трапеций
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 47.0f, z + 0.3f, 9.4f * neckK, 5.2f, 6.6f * neckK), 3);
            d = VoxModel.Smin(d, VoxModel.Cap(ax, y, z, 4.0f, 50.5f, -1.4f, 14.5f, 46.8f, -0.8f, 3.3f * neckK, 3.0f * neckK), 3);
            // пресс: три ряда выпуклостей на передней стенке живота (под брюшком не виден)
            if (z > 3f && y > 10.5f && y < 27.9f && ax < 6.6f && belly < 0.99f)
                d -= 0.6f * (1f - belly) * Mathf.Pow(Mathf.Sin(Mathf.PI * ax / 6.6f), 0.6f)
                          * Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * (y - 10.5f) / 5.8f)), 0.6f);
            return d;
        }

        // Трусы в осях шаблона: оболочка поверх низа корпуса с поясом, штанины до середины бедра.
        float TrunksM(float x, float y, float z)
        {
            float ax = Mathf.Abs(x);
            float d = Lower(x, y, z) - (y > 8.2f ? 1.5f : 1.0f);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y + 9, z, 17.2f, 12.5f, 11.8f), 4);
            d = VoxModel.Smin(d, VoxModel.Cap(ax, y, z, 9.0f, -8, 0, 10.6f, -26, 0.8f, 10.0f, 10.6f), 3);
            return Mathf.Max(d, Mathf.Max(y - 12.4f, -26f - y));
        }

        static float ThighsM(float x, float y, float z) =>
            VoxModel.Cap(Mathf.Abs(x), y, z, 9.0f, -8, 0, 10.2f, -44, 1.0f, 8.6f, 7.6f);

        float TorsoSdf(float x, float y, float z)
        {
            Map(ref x, ref y, ref z);
            return Mathf.Min(Mathf.Min(BodyM(x, y, z), TrunksM(x, y, z)), ThighsM(x, y, z));
        }

        byte TorsoPaint(float x, float y, float z)
        {
            Map(ref x, ref y, ref z);
            if (y > 12.4f || y < -26f || TrunksM(x, y, z) > 0.35f) return SkinPaint(x, y, z);
            if (y > 8.2f) return CBelt;                                       // пояс
            if (y > 7.4f) return CTrunksDark;                                 // шов под поясом
            float ax = Mathf.Abs(x), az = Mathf.Abs(z);
            switch (pattern)
            {
                case 1:   // двойной узкий лампас и кант
                    if (ax > 11.5f && az > 0.9f && az < 2.5f) return CStripe;
                    if (y < -23.5f) return CStripe;
                    break;
                case 2:   // без лампасов, широкий низ штанин
                    if (y < -20.5f) return CStripe;
                    break;
                case 3:   // штанины разного цвета, кант цвета пояса
                    if (y < -23.5f) return CBelt;
                    if (x > 0f) return CStripe;
                    break;
                default:  // широкий лампас и кант
                    if (ax > 11.5f && az < 3.2f) return CStripe;
                    if (y < -23.5f) return CStripe;
                    break;
            }
            return CTrunks;
        }

        // Кожа с отметиной (оси шаблона; +Z — грудь)
        byte SkinPaint(float x, float y, float z)
        {
            float sx = x * markSide;   // ось в сторону отметины
            switch (mark)
            {
                case BodyMark.ChestTattoo:
                    // две полосы наискось: от грудной мышцы к плечу
                    if (z > 0f && (Seg2(sx, y, 3.5f, 31f, 17f, 43.5f) < 1.1f || Seg2(sx, y, 6.5f, 27.6f, 18.8f, 39.4f) < 0.55f))
                        return CInk;
                    break;
                case BodyMark.Scar:
                    if (z > 2f)
                    {
                        // косой шов через живот и поперечные стежки каждые 2.4 см
                        float ax0 = -8f, ay0 = 15f, bx = 7f, by = 27f;
                        float dx = bx - ax0, dy = by - ay0, len = Mathf.Sqrt(dx * dx + dy * dy);
                        float ux = dx / len, uy = dy / len;
                        float px = sx - ax0, py = y - ay0;
                        float along = px * ux + py * uy, across = Mathf.Abs(px * -uy + py * ux);
                        if (along > 0f && along < len)
                        {
                            if (across < 0.55f) return CScar;
                            float st = Mathf.Abs(Mathf.Repeat(along + 1.2f, 2.4f) - 1.2f);
                            if (st < 0.38f && across < 1.4f) return CScar;
                        }
                    }
                    break;
                case BodyMark.ChestHair:
                    if (z > 3f)
                    {
                        float ex = x / 8.5f, ey = (y - 32f) / 7.5f;
                        float dens = Mathf.Max(0.62f * (1f - (ex * ex + ey * ey)),
                                               Mathf.Abs(x) < 1.3f && y > 12.5f && y < 27f ? 0.5f : 0f);
                        if (dens > 0f && CellHash(x, y, z) < dens) return CBodyHair;
                    }
                    break;
            }
            return CSkin;
        }

        // Расстояние от точки до отрезка на плоскости
        static float Seg2(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float h = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy));
            float qx = px - ax - dx * h, qy = py - ay - dy * h;
            return Mathf.Sqrt(qx * qx + qy * qy);
        }

        static float CellHash(float x, float y, float z)
        {
            uint h = (uint)(Mathf.FloorToInt(x / TorsoCell) * 73856093) ^ (uint)(Mathf.FloorToInt(y / TorsoCell) * 19349663)
                   ^ (uint)(Mathf.FloorToInt(z / TorsoCell) * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }

        Mesh BuildTorso(Color32 skin, in FighterDef def)
        {
            var mn = new Vector3(-30f, -36.4f, -18f);
            var mx = new Vector3(30f, StretchFrom + 46.5f * lenK, 20f);
            var m = VoxModel.FromSdf(mn, mx, TorsoCell, TorsoSdf, TorsoPaint);
            Color32 tr = def.colors.trunks, belt = def.colors.accent, stripe = def.colors.stripe;
            // чёрные трусы на тёмном фоне не читаются — приподнять до тёмно-серого
            if (tr.r + tr.g + tr.b < 90) tr = new Color32(44, 44, 52, 255);
            Color32 dark = Color.Lerp(tr, Color.black, 0.45f);
            Color32 scar = Color.Lerp(skin, new Color32(250, 214, 200, 255), 0.6f);
            Color32 bodyHair = Color.Lerp(def.head.hairColor, new Color(0.16f, 0.11f, 0.08f), 0.55f);
            var pal = new[] { default, skin, tr, belt, dark, stripe, BoxerParts.Ink, scar, bodyHair };
            return m.BuildMesh("torso", pal, mn * 0.01f, TorsoCell * 0.01f, 0.3f,
                               new[] { 1f, 1f, 0.35f, 0.5f, 0.5f, 0.35f, 1f, 1f, 1f });
        }

        // ---- Голени и боксёрки (см; начало — пояс, оси уже без шаблона) --------------------------------
        // Корпус кончается у колен; ниже — голени до настила: камера дальше, и фигура не должна висеть над рингом.
        // Колено — под концом бедра шаблона (10.2 см от средней линии, в мире × waistK).

        float KneeX => 10.2f * waistK;

        float LegsSdf(float x, float y, float z)
        {
            float ax = Mathf.Abs(x) - KneeX;
            float d = VoxModel.Cap(ax, y, z, 0f, KneeY + 3f, 1.0f, 0.8f, SoleY + 10f, -0.5f, 5.8f, 3.9f);   // голень
            d = VoxModel.Smin(d, VoxModel.Ell(ax - 0.3f, y - KneeY + 12f, z + 2.0f, 5.2f, 9.0f, 5.4f), 3f);  // икра
            // высокая боксёрка: голенище и стопа носком вперёд, подошва на настиле
            float b = VoxModel.Cap(ax, y, z, 0.8f, SoleY + 3f, 0.8f, 0.8f, SoleY + 21f, -0.3f, 5.0f, 4.6f);
            b = VoxModel.Smin(b, VoxModel.Ell(ax - 0.8f, y - SoleY - 4.2f, z - 4.5f, 5.0f, 4.4f, 12.0f), 2f);
            d = VoxModel.Smin(d, b, 1.5f);
            return Mathf.Max(d, SoleY - y);
        }

        static byte LegsPaint(float x, float y, float z)
        {
            if (y < SoleY + 1.6f) return LSole;
            if (y < SoleY + 21.5f) return LBoot;
            if (y < SoleY + 24.5f) return LSock;
            return LSkin;
        }

        Mesh BuildLegs(Color32 skin, in FighterDef def)
        {
            float kx = KneeX;
            var mn = new Vector3(-kx - 8f, SoleY - 0.4f, -9f);
            var mx = new Vector3(kx + 8f, -38f, 18f);
            var m = VoxModel.FromSdf(mn, mx, TorsoCell, LegsSdf, LegsPaint);
            // боксёрки — тёмный тон трусов, подошва и носки светлые
            Color32 boot = Color.Lerp(def.colors.trunks, new Color(0.08f, 0.08f, 0.1f), 0.45f);
            var pal = new[] { default, skin, boot, new Color32(226, 222, 214, 255), new Color32(238, 236, 230, 255) };
            return m.BuildMesh("legs", pal, mn * 0.01f, TorsoCell * 0.01f, 0.3f);
        }

        // ---- Позы (в осях корня, метры; z — к игроку) -----------------------------------------------

        // Начало координат головы в осях torsoJoint. Голова не масштабируется вместе с телом: к масштабу привязан низ
        // её шеи (он утоплен в трапеции), сама шея остаётся своей длины; низ шеи поднимается вместе с длиной корпуса.
        Vector3 HeadLocal()
        {
            float neckBase = StretchY((HeadY - NeckLen) * 100f) * 0.01f;
            return new Vector3(0f, HipDrop + neckBase + NeckLen / scale, HeadZ / scale);
        }

        // Центр перчатки у головы (в осях корня, до масштаба): off — сдвиг от начала координат головы в метрах мира,
        // x — наружу от средней линии. Перчатки идут за корпусом (наклон, отклик на удар), но не за поворотом головы.
        Vector3 AtHead(int s, Vector3 off, float bob)
        {
            Vector3 hl = HeadLocal() + new Vector3(ArmSign(s) * off.x, off.y + (s == 0 ? bob : -bob), off.z) / scale;
            return root.InverseTransformPoint(torsoJoint.TransformPoint(hl));
        }

        // Поза, заданная абсолютной высотой (под голову на RefHeadY), — к настоящей высоте головы
        Vector3 Abs(float x, float y, float z) => new Vector3(x, y + headDy, z);

        // Тик: cmd — команда ИИ, strikeTarget — куда летит бьющая перчатка (мир, у камеры игрока).
        public void Tick(AICommand cmd, float sdt, Vector3 strikeTarget)
        {
            phase = cmd.newPhase;
            anim += sdt;
            float bob = Mathf.Sin(anim * 5f) * 0.008f;
            Vector3 t0 = AtHead(0, IdleGloveOff, bob), t1 = AtHead(1, IdleGloveOff, bob);
            float leanT = 0f, twistT = Mathf.Sin(anim * 1.4f) * 1.5f, dropT = 0f, lungeT = 0f, rate = 12f;
            int hand = cmd.side < 0 ? 0 : 1;   // сторона удара на экране: слева — рука 0
            float hs = ArmSign(hand);

            switch (phase)
            {
                case FightPhase.Guard:
                    t0 = AtHead(0, GuardGloveOff, 0f); t1 = AtHead(1, GuardGloveOff, 0f); leanT = 3f; break;
                case FightPhase.Windup:
                {
                    Vector3 g = cmd.isHook ? Abs(hs * 0.36f, 1.50f, 0.12f) : Abs(hs * 0.22f, 1.55f, 0.13f);
                    if (hand == 0) t0 = g; else t1 = g;
                    twistT = hs * 16f; leanT = -3f; break;
                }
                case FightPhase.Strike:
                {
                    Vector3 g = root.InverseTransformPoint(strikeTarget);
                    if (hand == 0) t0 = g; else t1 = g;
                    twistT = -hs * 18f; leanT = 9f; lungeT = 0.16f; rate = 30f; break;
                }
                case FightPhase.Recover: leanT = 4f; lungeT = 0.05f; break;
                case FightPhase.Open:
                    t0 = Abs(0.27f, 1.24f, 0.26f); t1 = Abs(-0.26f, 1.20f, 0.25f); leanT = 12f; lungeT = 0.08f; break;
                case FightPhase.Stagger:
                    t0 = Abs(0.23f, 1.34f, 0.20f); t1 = Abs(-0.23f, 1.34f, 0.20f); leanT = -7f; break;
                case FightPhase.Down:
                    // на коленях, руки упёрты в настил
                    t0 = new Vector3(0.24f, 0.07f, 0.36f); t1 = new Vector3(-0.24f, 0.07f, 0.36f);
                    leanT = 30f; dropT = 0.60f; rate = 6f; break;
                case FightPhase.Rise:
                    t0 = Abs(0.20f, 1.22f, 0.24f); t1 = Abs(-0.20f, 1.22f, 0.24f);
                    leanT = 10f; dropT = 0f; rate = 2.2f; break;
                case FightPhase.KO:
                    // лицом в настил
                    t0 = new Vector3(0.30f, 0.07f, 0.62f); t1 = new Vector3(-0.30f, 0.07f, 0.62f);
                    leanT = 78f; dropT = 0.80f; rate = 5f; break;
            }

            float k = 1f - Mathf.Exp(-rate * sdt);
            holdW = Mathf.MoveTowards(holdW, holdArm >= 0 ? 1f : 0f, sdt / 0.2f);
            Vector3 g0 = glovePos[0], g1 = glovePos[1];
            glovePos[0] = Vector3.Lerp(g0, t0, k);
            glovePos[1] = Vector3.Lerp(g1, t1, k);
            // удар: с замаха перчатка срывалась на полметра за первый тик (почти телепорт). Скорость ограничена —
            // до цели она всё равно доходит раньше проверки удара (STRIKE_DUR 0.13 с)
            if (phase == FightPhase.Strike)
            {
                glovePos[0] = Vector3.MoveTowards(g0, glovePos[0], StrikeSpeed * sdt);
                glovePos[1] = Vector3.MoveTowards(g1, glovePos[1], StrikeSpeed * sdt);
            }
            float kb = 1f - Mathf.Exp(-Mathf.Min(rate, 10f) * sdt);
            lean = Mathf.Lerp(lean, leanT, kb);
            twist = Mathf.Lerp(twist, twistT, kb);
            drop = Mathf.Lerp(drop, dropT, kb);
            lunge = Mathf.Lerp(lunge, lungeT, k);

            // отклик корпуса на удар: пружина с затуханием, шаг не больше 1/60 с
            for (float left = Mathf.Min(sdt, 0.1f); left > 1e-5f; left -= 1f / 60f)
            {
                float h = Mathf.Min(left, 1f / 60f);
                impLeanV += (-90f * impLean - 11f * impLeanV) * h; impLean += impLeanV * h;
                impTwistV += (-90f * impTwist - 11f * impTwistV) * h; impTwist += impTwistV * h;
            }

            // голова: в стойке ровная (крен 0, лёгкий наклон вперёд), наклоны — в шатании и на настиле;
            // поверх — быстрый отклик на удар (кивок, поворот и крен от скрутки)
            float hp, hy, hr;
            switch (phase)
            {
                case FightPhase.Stagger: hp = -4f; hy = twist * 0.5f; hr = StaggerRoll * Mathf.Sin(anim * 2.3f); break;
                case FightPhase.Down:    hp = lean * 0.85f; hy = twist; hr = DownRoll; break;
                case FightPhase.KO:      hp = lean * 0.9f;  hy = twist; hr = DownRoll * 1.5f; break;
                default:                 hp = HeadPitchStance; hy = twist * HeadYawFollow; hr = 0f; break;
            }
            headPitch = Mathf.Lerp(headPitch, hp, kb);
            headYaw   = Mathf.Lerp(headYaw, hy, kb);
            headRoll  = Mathf.Lerp(headRoll, hr, kb);
            Pose(new Vector3(0f, -drop, lunge - drop * 0.25f));

            // накладки головы: полёт сброшенного убора и поза черепа (изгиб шеи прошлого кадра;
            // директор после AnatSkin.SetGlobals может вызвать gear.SyncToSkull() для точной позы этого кадра)
            gear.Tick(sdt);
        }

        // Расставить корпус и руки по текущему состоянию. offset — смещение корпуса в осях корня.
        void Pose(Vector3 offset)
        {
            torsoJoint.localPosition = new Vector3(0f, WaistY - HipDrop, 0f) + offset;
            float il = Mathf.Clamp(impLean, -14f, 14f), it = Mathf.Clamp(impTwist, -18f, 18f);
            torsoJoint.localRotation = Quaternion.Euler(lean + il, twist + it, 0f);
            // голова стоит на шее корпуса, но поворот у неё свой (в осях корня): корпус качается, голова ровная
            headMount.localPosition = HeadLocal();
            headMount.localRotation = Quaternion.Inverse(torsoJoint.localRotation)
                                    * Quaternion.Euler(headPitch + il * 0.5f, headYaw + it * 0.5f, headRoll - it * 0.4f);

            bool low = phase == FightPhase.Down || phase == FightPhase.KO;
            for (int s = 0; s < 2; s++)
            {
                float sg = ArmSign(s);
                Vector3 sh = torsoJoint.TransformPoint(shoulderLocal[s]);
                Vector3 gt = root.TransformPoint(glovePos[s]);
                Vector3 fwd = root.forward, up = root.up, right = root.right;
                // костяшки: в стойке вверх и вперёд, в ударе — по линии плечо → цель, на настиле — вниз
                Vector3 kn = phase == FightPhase.Strike ? (gt - sh).normalized
                           : low ? (fwd * 0.4f - up).normalized
                           : (up * 0.6f + fwd * 0.8f - right * sg * 0.15f).normalized;
                // локоть: в стойке — вниз и в сторону; при опущенных руках (раскрылся, шатается) — вниз и назад,
                // иначе локти торчат в стороны «руки в боки»
                float armLow = Mathf.InverseLerp(1.42f, 1.22f, glovePos[s].y);
                Vector3 pole = low ? right * sg * 0.9f - fwd * 0.4f
                                   : Vector3.Lerp(right * sg * 0.35f - up + fwd * 0.1f, right * sg * 0.25f - up - fwd * 0.7f, armLow);
                Vector3 back = right * sg * 0.85f + fwd * 0.5f;
                if (s == holdArm && holdW > 0f)
                {
                    // запястье в руке рефери, перчатка над ним костяшками вверх, локоть наружу и назад
                    gt = Vector3.Lerp(gt, holdWrist + up * BoxerParts.GloveOff * scale, holdW);
                    kn = Vector3.Lerp(kn, up, holdW).normalized;
                    pole = Vector3.Lerp(pole, right * sg * 0.8f - fwd * 0.4f, holdW);
                    back = Vector3.Lerp(back, fwd, holdW);
                }
                BoxerParts.SolveArm(upper[s], fore[s], glove[s], sh, gt, kn, pole, back, scale);
            }
        }

        // Рефери держит руку arm (0 — на экране слева, 1 — справа) за запястье в точке worldWrist (мир).
        // Звать каждый кадр, пока держит; поза применяется в следующем Tick.
        public void HoldWrist(int arm, Vector3 worldWrist)
        {
            holdArm = arm == 0 ? 0 : 1;
            holdWrist = worldWrist;
        }

        // Отпустить руку сразу (переигровка).
        public void ReleaseWrist()
        {
            holdArm = -1; holdW = 0f;
        }

        // Импульс от удара игрока (мир): корпус отклоняется назад и скручивается.
        public void AddImpulse(Vector3 worldImpulse)
        {
            Vector3 loc = root.InverseTransformDirection(worldImpulse);
            impLeanV  += loc.z * 60f;    // удар в лицо (−Z в осях соперника) — корпус назад
            impTwistV += loc.x * 80f;
        }

        // Сбросить головной убор (шлем, шапочку, повязку) на настил: при тяжёлом разрушении головы.
        // Возвращает false, если сбрасывать нечего или уже сброшено.
        public bool DropGear(Vector3 worldImpulse) => gear.Drop(worldImpulse, root.position.y);

        // Вернуть накладки головы на место (перезапуск боя)
        public void ResetGear() => gear.Restore();

        public void Dispose()
        {
            gear.Dispose();
            foreach (var m in meshes) GameUtil.Destroy(m);
            if (root != null) GameUtil.Destroy(root.gameObject);
        }
    }
}
