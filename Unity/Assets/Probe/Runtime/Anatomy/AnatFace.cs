using Unity.Mathematics;
using UnityEngine;
using static VoxProbe.AnatGeo;

namespace VoxProbe
{
    /// Выражения лица соперника (Unity/EMOTIONS.md). Номер — строка таблицы AnatFace.Poses.
    public static class AnatEmo
    {
        public const int Calm = 0, Focus = 1, Anger = 2, Rage = 3, Smirk = 4, Surprise = 5, Pain = 6, Dazed = 7, Fear = 8, Tired = 9,
            Triumph = 10, Defeat = 11, Contempt = 12, Count = 13;
        public static readonly string[] Names = { "спокойное", "сосредоточенное", "злость", "оскал", "ухмылка", "удивление", "боль", "оглушён",
            "страх", "усталость", "торжество", "поражение", "презрение" };
        public static readonly string[] Keys = { "calm", "focus", "anger", "rage", "smirk", "surprise", "pain", "dazed", "fear", "tired",
            "triumph", "defeat", "contempt" };
    }

    /// Характер бойца для мимики: базовый настрой и склонности. Задаётся рядом с внешностью (AnatLook), игровой слой
    /// выбирает предустановку по бойцу (FighterGen.TemperOf).
    public struct AnatTemper
    {
        public string name;
        public int mood;          // выражение покоя (AnatEmo)
        public float moodW;       // его сила 0..1
        public float smirk, anger, fear, show;   // склонности 0..1: ухмыляться, злиться, бояться, показывать торжество
        public float gain;        // общий размах мимики (0,6 — сдержанный, 1,3 — яркий)
        public float blink;       // морганий в секунду
        public float brow;        // постоянный сдвиг бровей (−1 насуплен … +1 приподняты)
        public int side;          // сторона кривой ухмылки: +1 — +x головы, −1 — −x

        public static readonly AnatTemper[] Presets =
        {
            new AnatTemper { name = "хладнокровный", mood = AnatEmo.Focus,    moodW = 0.45f, smirk = 0.3f, anger = 0.2f, fear = 0.15f, show = 0.2f, gain = 0.75f, blink = 0.18f, brow = -0.1f, side = 1 },
            new AnatTemper { name = "задира",        mood = AnatEmo.Smirk,    moodW = 0.55f, smirk = 1.0f, anger = 0.4f, fear = 0.3f,  show = 0.8f, gain = 1.1f,  blink = 0.3f,  brow = 0.15f, side = 1 },
            new AnatTemper { name = "злой",          mood = AnatEmo.Anger,    moodW = 0.6f,  smirk = 0.2f, anger = 1.0f, fear = 0.2f,  show = 0.5f, gain = 1.1f,  blink = 0.25f, brow = -0.4f, side = -1 },
            new AnatTemper { name = "шоумен",        mood = AnatEmo.Smirk,    moodW = 0.35f, smirk = 0.7f, anger = 0.3f, fear = 0.4f,  show = 1.0f, gain = 1.3f,  blink = 0.35f, brow = 0.3f,  side = -1 },
            new AnatTemper { name = "нервный",       mood = AnatEmo.Fear,     moodW = 0.3f,  smirk = 0.2f, anger = 0.3f, fear = 1.0f,  show = 0.4f, gain = 1.15f, blink = 0.6f,  brow = 0.35f, side = 1 },
            new AnatTemper { name = "угрюмый",       mood = AnatEmo.Focus,    moodW = 0.7f,  smirk = 0.1f, anger = 0.6f, fear = 0.3f,  show = 0.2f, gain = 0.9f,  blink = 0.2f,  brow = -0.6f, side = 1 },
            new AnatTemper { name = "зверь",         mood = AnatEmo.Anger,    moodW = 0.45f, smirk = 0.3f, anger = 0.9f, fear = 0.1f,  show = 0.7f, gain = 1.25f, blink = 0.15f, brow = -0.3f, side = 1 },
            new AnatTemper { name = "каменный",      mood = AnatEmo.Calm,     moodW = 0.8f,  smirk = 0.15f, anger = 0.2f, fear = 0.1f, show = 0.15f, gain = 0.6f, blink = 0.12f, brow = 0f,    side = 1 },
        };
    }

    /// Мимика: смесь выражений (вес каждого — пружина с критическим затуханием, без скачков), событие с приоритетом и длительностью,
    /// фон — моргание, дыхание ртом, движение взгляда. Итог — 24 канала (брови, веки, взгляд, челюсть, губы), они идут в шейдер
    /// Anatomy (_Face) и сдвигают вершины головы, челюсти, глаз и нижних зубов от позы покоя. Решётку и модель повреждений мимика
    /// не трогает: выбитое не возвращается, на уцелевшем мимика остаётся. Та же деформация на C# — Deform (для тестов).
    public class AnatFace
    {
        public static bool Enabled = true;   // выключатель всей мимики: без него шейдер и мешер работают как раньше

        // каналы: L — сторона +x головы (левая сторона человека), R — −x
        public const int BrowInL = 0, BrowInR = 1, BrowOutL = 2, BrowOutR = 3, Knit = 4, LidUpL = 5, LidUpR = 6, LidLoL = 7, LidLoR = 8,
            GazeX = 9, GazeY = 10, Diverge = 11, Jaw = 12, JawSide = 13, CornerL = 14, CornerR = 15, Wide = 16, LipUp = 17, LipDown = 18,
            Press = 19, Nose = 20, CheekL = 21, CheekR = 22, NC = 24;

        // выражение → каналы (обе стороны, если не задано иначе)
        public static readonly float[][] Poses = new float[AnatEmo.Count][];
        static void P(int e, params (int ch, float v)[] kv) { var a = new float[NC]; foreach (var (c, v) in kv) a[c] = v; Poses[e] = a; }
        static AnatFace()
        {
            P(AnatEmo.Calm, (LidUpL, 0.12f), (LidUpR, 0.12f), (CornerL, 0.05f), (CornerR, 0.05f));
            P(AnatEmo.Focus, (BrowInL, -0.65f), (BrowInR, -0.65f), (BrowOutL, -0.15f), (BrowOutR, -0.15f), (Knit, 0.8f), (LidUpL, 0.25f), (LidUpR, 0.25f),
                (LidLoL, 0.55f), (LidLoR, 0.55f), (GazeY, 0.12f), (Press, 0.8f), (CornerL, -0.3f), (CornerR, -0.3f));
            P(AnatEmo.Anger, (BrowInL, -1f), (BrowInR, -1f), (BrowOutL, 0.25f), (BrowOutR, 0.25f), (Knit, 1f), (LidUpL, -0.2f), (LidUpR, -0.2f),
                (LidLoL, 0.55f), (LidLoR, 0.55f), (Nose, 0.5f), (Press, 0.7f), (CornerL, -0.6f), (CornerR, -0.6f), (LipUp, 0.2f), (CheekL, 0.2f), (CheekR, 0.2f));
            P(AnatEmo.Rage, (BrowInL, -1f), (BrowInR, -1f), (BrowOutL, 0.35f), (BrowOutR, 0.35f), (Knit, 1f), (LidUpL, -0.4f), (LidUpR, -0.4f),
                (LidLoL, 0.4f), (LidLoR, 0.4f), (Nose, 1f), (LipUp, 1f), (LipDown, 0.9f), (Jaw, 0.3f), (Wide, 0.8f), (CornerL, -0.25f), (CornerR, -0.25f),
                (CheekL, 0.6f), (CheekR, 0.6f));
            P(AnatEmo.Smirk, (BrowOutL, 0.7f), (BrowInL, 0.25f), (BrowInR, -0.25f), (LidUpL, 0.2f), (LidUpR, 0.35f), (LidLoR, 0.35f),
                (CornerL, 1f), (CornerR, -0.1f), (CheekL, 0.7f), (Wide, 0.25f), (LipUp, 0.2f), (GazeX, -0.15f));
            P(AnatEmo.Surprise, (BrowInL, 1f), (BrowInR, 1f), (BrowOutL, 0.9f), (BrowOutR, 0.9f), (LidUpL, -1f), (LidUpR, -1f), (Jaw, 0.6f),
                (Wide, -0.35f), (LipDown, 0.2f), (LipUp, 0.1f));
            P(AnatEmo.Pain, (BrowInL, 0.7f), (BrowInR, 0.7f), (BrowOutL, -0.6f), (BrowOutR, -0.6f), (Knit, 1f), (LidUpL, 1f), (LidUpR, 1f),
                (LidLoL, 1f), (LidLoR, 1f), (Nose, 0.9f), (CheekL, 0.9f), (CheekR, 0.9f), (LipUp, 0.9f), (LipDown, 0.9f), (Wide, 1f),
                (CornerL, -0.4f), (CornerR, -0.4f));
            P(AnatEmo.Dazed, (BrowInL, 0.3f), (BrowInR, 0.15f), (BrowOutL, -0.3f), (BrowOutR, -0.1f), (LidUpL, 0.5f), (LidUpR, 0.35f),
                (Diverge, 0.9f), (GazeY, -0.35f), (Jaw, 0.45f), (JawSide, 0.35f), (CornerL, -0.35f), (CornerR, -0.2f), (LipDown, 0.4f), (Wide, -0.1f));
            P(AnatEmo.Fear, (BrowInL, 1f), (BrowInR, 1f), (BrowOutL, 0.2f), (BrowOutR, 0.2f), (Knit, 0.7f), (LidUpL, -0.85f), (LidUpR, -0.85f),
                (LidLoL, 0.3f), (LidLoR, 0.3f), (Jaw, 0.35f), (Wide, 1f), (LipDown, 0.75f), (CornerL, -1f), (CornerR, -1f));
            P(AnatEmo.Tired, (BrowInL, 0.3f), (BrowInR, 0.3f), (BrowOutL, -0.45f), (BrowOutR, -0.45f), (LidUpL, 0.6f), (LidUpR, 0.6f),
                (GazeY, -0.3f), (Jaw, 0.4f), (CornerL, -0.4f), (CornerR, -0.4f), (LipDown, 0.25f));
            P(AnatEmo.Triumph, (BrowInL, 0.45f), (BrowInR, 0.45f), (BrowOutL, 0.65f), (BrowOutR, 0.65f), (LidUpL, 0.1f), (LidUpR, 0.1f),
                (LidLoL, 0.45f), (LidLoR, 0.45f), (CornerL, 1f), (CornerR, 1f), (Wide, 0.7f), (CheekL, 1f), (CheekR, 1f), (LipUp, 0.6f),
                (Jaw, 0.4f), (LipDown, 0.5f), (GazeY, 0.15f));
            P(AnatEmo.Defeat, (BrowInL, 0.9f), (BrowInR, 0.9f), (BrowOutL, -0.5f), (BrowOutR, -0.5f), (Knit, 0.5f), (LidUpL, 0.5f), (LidUpR, 0.5f),
                (GazeY, -0.7f), (CornerL, -0.9f), (CornerR, -0.9f), (Press, 0.4f), (Jaw, 0.05f));
            P(AnatEmo.Contempt, (CornerR, 0.95f), (CornerL, -0.4f), (LidUpL, 0.45f), (LidUpR, 0.45f), (BrowOutR, 0.5f), (BrowInL, -0.45f), (LipUp, 0.25f),
                (Nose, 0.5f), (Press, 0.5f), (CheekR, 0.5f), (GazeY, -0.1f), (GazeX, 0.2f));
        }

        // размеры движений, см и радианы (ими же пользуется шейдер Anatomy, функция Face)
        public const float BrowUpCm = 0.8f, BrowDownCm = 0.6f, KnitXCm = 0.25f, KnitYCm = 0.18f, LidClose = 0.62f, LidWide = 0.07f, LidLow = 0.2f,
            GazeYaw = 0.32f, GazePitch = 0.22f, JawRad = 0.3f, JawSideCm = 0.35f, CornerUpCm = 0.65f, CornerDownCm = 0.55f, WideCm = 0.42f,
            LipUpCm = 0.6f, LipDownCm = 0.55f, PressCm = 0.15f, NoseCm = 0.22f, CheekCm = 0.32f, YL = -5.25f;
        public static readonly float3 JawPivot = new float3(0, -0.6f, 0.25f);   // ось открывания — через суставные головки (AnatGeo.Condyle)

        // ---------------- состояние ----------------
        public AnatTemper temper;
        public readonly float[] w = new float[AnatEmo.Count], wv = new float[AnatEmo.Count], wt = new float[AnatEmo.Count];
        public readonly float[] ch = new float[NC];
        public int ev = -1;                 // текущее событие (выражение) или −1
        public float evLeft, evGain; public int evPrio, evSide;
        public float fatigue, fear, time;   // 0..1: усталость (дыхание ртом), страх; время мимики, с
        public bool hold;                   // событие держится до следующего (нокдаун, нокаут, развязка)
        public float omega = 20f;           // жёсткость пружин весов, рад/с
        uint rng; float tirS, blinkT, blinkAt, breath, gazeT, gx, gy, gxT, gyT;
        public bool jawGone;                // нижняя челюсть оторвана: рот не двигается
        public bool blinks = true;          // моргание (снимки выражений — без него)

        public AnatFace(AnatTemper t, uint seed = 1)
        {
            temper = t; rng = seed * 747796405u + 2891336453u; if (rng == 0) rng = 1;
            if (temper.gain <= 0) temper.gain = 1; if (temper.blink <= 0) temper.blink = 0.25f;
            blinkAt = 1.5f + Rand() * 2f; gazeT = 0.5f;
            Snap();
        }

        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

        /// Событие: выражение e с силой gain на dur секунд; не перебивает событие с большим приоритетом, пока то не кончилось.
        public bool Play(int e, float gain = 1f, float dur = 1f, int prio = 1, int side = 0, bool holdIt = false)
        {
            if (ev >= 0 && (evLeft > 0 || hold) && prio < evPrio) return false;
            ev = e; evGain = gain; evLeft = dur; evPrio = prio; evSide = side; hold = holdIt;
            if (prio >= 3) wv[e] += 6f * gain;   // сильное событие (удар) — быстрый старт; скорость, не положение: скачка нет
            return true;
        }
        public void Clear() { ev = -1; evLeft = 0; evPrio = 0; hold = false; }

        /// Цели весов: событие поверх настроя (настрой бойца, усталость, страх).
        void Targets()
        {
            System.Array.Clear(wt, 0, wt.Length);
            bool on = ev >= 0 && (evLeft > 0 || hold);
            float eg = on ? math.saturate(evGain) : 0f;
            float rest = 1f - 0.9f * eg;
            wt[temper.mood] += temper.moodW * rest;
            wt[AnatEmo.Tired] += fatigue * 0.9f * rest;
            wt[AnatEmo.Fear] += fear * temper.fear * rest;
            if (on) wt[ev] += eg;
        }

        /// Шаг мимики: веса к целям (пружина), каналы, фон (моргание, дыхание, взгляд).
        public void Step(float dt)
        {
            if (!(dt > 0)) return;
            dt = math.min(dt, 0.1f); time += dt;
            if (ev >= 0 && evLeft > 0) { evLeft -= dt; if (evLeft <= 0 && !hold) { ev = -1; evPrio = 0; } }
            Targets();
            // критически затухающая пружина: x'' = ω²(цель − x) − 2ω x' — скорость непрерывна, кадр без скачка
            float om = omega, k = om * om, c = 2 * om;
            int n = (int)math.ceil(dt / 0.01f); float h = dt / n;
            for (int s = 0; s < n; s++)
                for (int i = 0; i < AnatEmo.Count; i++) { wv[i] += (k * (wt[i] - w[i]) - c * wv[i]) * h; w[i] += wv[i] * h; }
            Mix(dt);
        }

        /// Сразу к целям без перехода (снимки, начало боя).
        public void Snap()
        {
            Targets();
            for (int i = 0; i < AnatEmo.Count; i++) { w[i] = wt[i]; wv[i] = 0; }
            Mix(0);
        }

        void Mix(float dt)
        {
            System.Array.Clear(ch, 0, NC);
            float g = temper.gain;
            for (int e = 0; e < AnatEmo.Count; e++)
            {
                float we = w[e]; if (math.abs(we) < 1e-4f) continue;
                var p = Poses[e];
                bool flip = (e == AnatEmo.Smirk || e == AnatEmo.Contempt) && temper.side < 0 || (e == AnatEmo.Pain || e == AnatEmo.Dazed) && ev == e && evSide < 0;
                for (int i = 0; i < NC; i++) { int j = flip ? Mirror(i) : i; ch[j] += p[i] * we * g * (flip && (i == GazeX || i == JawSide) ? -1 : 1); }
            }
            // место удара: у боли глаз со стороны удара зажмурен сильнее, челюсть уходит от удара
            if (ev == AnatEmo.Pain && evSide != 0)
            {
                float wp = math.saturate(w[AnatEmo.Pain]);
                ch[evSide > 0 ? LidUpL : LidUpR] += 0.25f * wp; ch[evSide > 0 ? CheekL : CheekR] += 0.3f * wp; ch[JawSide] -= evSide * 0.5f * wp;
            }
            ch[BrowInL] += temper.brow * 0.4f; ch[BrowInR] += temper.brow * 0.4f; ch[BrowOutL] += temper.brow * 0.2f; ch[BrowOutR] += temper.brow * 0.2f;
            // фон: дыхание ртом при усталости (чаще и глубже с усталостью и страхом)
            float tir0 = math.saturate(w[AnatEmo.Tired] + fatigue * 0.5f + w[AnatEmo.Fear] * 0.3f);
            tirS = dt > 0 ? tirS + (tir0 - tirS) * (1 - math.exp(-dt * 3f)) : tir0; float tir = tirS;   // усталость меняется скачком (удар) — дыхание нарастает плавно
            breath += dt * (0.45f + 0.6f * tir) * 2 * math.PI;   // 0,45–1,05 вдоха в секунду
            if (breath > 2 * math.PI) breath -= 2 * math.PI;
            ch[Jaw] += tir * 0.35f * (0.5f - 0.5f * math.cos(breath)); ch[LipDown] += tir * 0.15f * (0.5f - 0.5f * math.cos(breath));
            // моргание: закрыть-открыть за 0,16 с, у оглушённого медленнее
            float dz = math.saturate(w[AnatEmo.Dazed]);
            if (dt > 0 && blinks)
            {
                blinkT += dt;
                if (blinkT >= blinkAt) { blinkT = -0.16f * (1 + 2 * dz); blinkAt = (0.4f + Rand() * 1.6f) / temper.blink; if (Rand() < 0.15f) blinkAt = 0.25f; }
            }
            if (blinkT < 0)
            {
                float ph = 1f + blinkT / (0.16f * (1 + 2 * dz));   // 0..1
                float b = math.sin(ph * math.PI);
                ch[LidUpL] = math.lerp(ch[LidUpL], 1.05f, b); ch[LidUpR] = math.lerp(ch[LidUpR], 1.05f, b);
            }
            // взгляд: мелкие перескоки; у оглушённого — плывёт
            if (dt > 0)
            {
                gazeT -= dt;
                if (gazeT <= 0) { gazeT = 0.5f + Rand() * 1.5f; gxT = (Rand() - 0.5f) * 0.3f; gyT = (Rand() - 0.5f) * 0.18f; }
                float a = 1 - math.exp(-dt * 25f);
                gx += (gxT - gx) * a; gy += (gyT - gy) * a;
            }
            ch[GazeX] += gx * (1 - dz) + dz * 0.35f * math.sin(time * 1.3f); ch[GazeY] += gy * (1 - dz) + dz * 0.25f * math.sin(time * 0.9f + 1f);
            for (int i = 0; i < NC; i++) ch[i] = math.clamp(ch[i], -1.2f, 1.2f);
            ch[Jaw] = math.saturate(ch[Jaw]); ch[Diverge] = math.saturate(ch[Diverge]);
            if (jawGone) { ch[Jaw] = 0; ch[JawSide] = 0; ch[LipDown] = 0; }
        }

        public static int Mirror(int i)
        {
            switch (i)
            {
                case BrowInL: return BrowInR; case BrowInR: return BrowInL; case BrowOutL: return BrowOutR; case BrowOutR: return BrowOutL;
                case LidUpL: return LidUpR; case LidUpR: return LidUpL; case LidLoL: return LidLoR; case LidLoR: return LidLoL;
                case CornerL: return CornerR; case CornerR: return CornerL; case CheekL: return CheekR; case CheekR: return CheekL;
                default: return i;
            }
        }

        /// Ведущее выражение сейчас (наибольший вес).
        public int Dominant { get { int b = 0; for (int i = 1; i < AnatEmo.Count; i++) if (w[i] > w[b]) b = i; return b; } }

        // ---------------- шейдер ----------------
        static readonly int FaceId = Shader.PropertyToID("_Face"), RoleId = Shader.PropertyToID("_FaceRole"), OnId = Shader.PropertyToID("_FaceOn"),
            MouthId = Shader.PropertyToID("_FaceMouth"), TxId = Shader.PropertyToID("_FaceTX"), W2HId = Shader.PropertyToID("_AnatW2H"), H2WId = Shader.PropertyToID("_AnatH2W");
        static readonly Vector4[] FaceV = new Vector4[NC / 4], TxV = new Vector4[16];
        static readonly float[] RoleV = new float[48];
        public const int RoleSoft = 0, RoleJaw = 1, RoleEyeL = 2, RoleEyeR = 3, RoleRigid = 4;

        /// Роль тела головы для шейдера: 0 — мягкая голова, 1 — нижняя челюсть и нижние зубы, 2/3 — глаз +x/−x, 4 — жёстко (верхние зубы).
        public static int RoleOf(AnatHead a, int body)
        {
            if (body == 0) return RoleSoft; if (body == 1) return RoleJaw; if (body == 2) return RoleEyeL; if (body == 3) return RoleEyeR;
            int t = body - 4; return t >= 0 && t < a.toothDefs.Length && a.toothDefs[t].upper == 0 ? RoleJaw : RoleRigid;
        }

        /// Каналы в шейдер. root — корень головы (мир ↔ см головы), a и gone — тела головы и оторванные (оторванное мимика не двигает).
        public void Apply(Transform root, AnatHead a, bool[] gone)
        {
            jawGone = gone != null && gone.Length > 1 && gone[1];
            if (jawGone) { ch[Jaw] = 0; ch[JawSide] = 0; ch[LipDown] = 0; }
            Send(ch, root, a, gone);
        }

        public static void Send(float[] c, Transform root, AnatHead a, bool[] gone)
        {
            if (!Enabled) { Shader.SetGlobalFloat(OnId, 0f); return; }
            for (int i = 0; i < NC; i++) FaceV[i >> 2][i & 3] = c[i];
            int nb = 4 + a.toothDefs.Length;
            for (int b = 0; b < RoleV.Length; b++) RoleV[b] = b < nb ? RoleOf(a, b) : RoleRigid;
            // зубы для рисунка рта: x шейки (см) и есть ли зуб (знак — верхний +, нижний −; 0 — выбит)
            for (int t = 0; t < 32; t++)
            {
                float v = 0, x = 0;
                if (t < a.toothDefs.Length) { x = a.toothDefs[t].c.x; bool here = gone == null || 4 + t >= gone.Length || !gone[4 + t]; v = (a.toothDefs[t].upper == 1 ? 1 : -1) * (here ? 1f : 0.5f); }
                TxV[t >> 1][(t & 1) * 2] = x; TxV[t >> 1][(t & 1) * 2 + 1] = v;
            }
            var w2h = Matrix4x4.Scale(Vector3.one * 100f) * root.worldToLocalMatrix;
            Shader.SetGlobalMatrix(W2HId, w2h); Shader.SetGlobalMatrix(H2WId, w2h.inverse);
            Shader.SetGlobalVectorArray(FaceId, FaceV); Shader.SetGlobalFloatArray(RoleId, RoleV); Shader.SetGlobalVectorArray(TxId, TxV);
            Shader.SetGlobalVector(MouthId, Mouth(c));
            Shader.SetGlobalFloat(OnId, 1f);
        }

        public static void Off() => Shader.SetGlobalFloat(OnId, 0f);

        /// Рот: x — раскрытие губ в середине, см; y, z — видимая высота верхних и нижних зубов, см.
        public static Vector4 Mouth(float[] c)
        {
            float jaw = math.saturate(c[Jaw]) * JawRad * 8.6f;   // плечо от оси до середины губ ≈ 8,6 см
            float sep = jaw + math.max(c[LipUp], 0) * LipUpCm + math.max(c[LipDown], 0) * LipDownCm;
            float up = math.clamp(math.max(c[LipUp], 0) * 0.75f + 0.12f, 0, 0.85f), lo = math.clamp(math.max(c[LipDown], 0) * 0.6f + 0.08f, 0, 0.65f);
            return new Vector4(sep, up, lo, 0);
        }

        // ---------------- деформация (та же, что в шейдере) ----------------
        static float S(float e0, float e1, float x) { float t = math.saturate((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
        static float3 RotX(float3 v, float a) { float c = math.cos(a), s = math.sin(a); return new float3(v.x, v.y * c - v.z * s, v.y * s + v.z * c); }
        static float3 RotY(float3 v, float a) { float c = math.cos(a), s = math.sin(a); return new float3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c); }

        /// Точка покоя h (см головы) тела с ролью role → точка с мимикой c (каналы). seam — шов губ (0 верх, 1 низ), mouth — маска рта.
        public static float3 Deform(float3 h, int role, float[] c) => Deform(h, role, c, out _, out _);
        public static float3 Deform(float3 h, int role, float[] c, out float seam, out float mouth)
        {
            seam = 0; mouth = 0;
            if (role == RoleRigid) return h;
            float jawA = math.saturate(c[Jaw]) * JawRad;
            if (role == RoleJaw) return JawPivot + RotX(h - JawPivot, jawA) + new float3(c[JawSide] * JawSideCm, 0, 0);
            if (role == RoleEyeL || role == RoleEyeR)
            {
                float sxe = role == RoleEyeL ? 1 : -1; float3 ce = new float3(sxe * EX, EY, EZ);
                float yaw = (c[GazeX] + sxe * c[Diverge] * 0.6f) * GazeYaw, pitch = -c[GazeY] * GazePitch;
                return ce + RotX(RotY(h - ce, yaw), pitch);
            }
            if (h.z < 3.0f) return h;
            float ax = math.abs(h.x), sx = h.x >= 0 ? 1 : -1; int sd = h.x >= 0 ? 0 : 1;
            float3 q = h;
            // веки: оболочка вокруг глаза поворачивается вокруг его центра (скользит по яблоку)
            float3 ce2 = new float3(sx * EX, EY, EZ), v = h - ce2; float er = math.length(v);
            if (er < 2.1f && v.z > -0.4f)
            {
                float u = (ax - EX) / 1.45f, wal = math.pow(math.max(0, 1 - u * u), 0.7f), mid = 0.085f * wal + 0.06f * u;
                float shell = S(2.05f, 1.6f, er) * S(-0.3f, 0.3f, v.z);
                float wU = shell * S(-0.2f, 0.15f, v.y - mid), wL = shell * S(0.2f, -0.15f, v.y - mid);
                float cu = c[LidUpL + sd] - c[GazeY] * 0.25f;
                float aU = (cu > 0 ? cu * LidClose : cu * LidWide) * wal, aL = -math.max(c[LidLoL + sd], 0) * LidLow * wal;
                float3 vr = RotX(v, aU * wU + aL * wL) * (1 + 0.06f * math.max(cu, 0) * wU);
                q = ce2 + vr;
            }
            float3 d = 0;
            // брови и лоб
            float t = math.saturate((ax - 0.75f) / 4.75f), yb = 3.45f + 0.42f * math.sin(math.PI * math.min(1, t * 1.2f)) - 0.15f * t, dyb = h.y - yb;
            float wb = math.exp(-dyb * dyb / (dyb > 0 ? 2.6f : 0.55f)) * (1 - S(5.6f, 6.8f, ax)) * S(4.5f, 6.0f, h.z);
            float up = math.lerp(c[BrowInL + sd], c[BrowOutL + sd], S(1.0f, 4.2f, ax));
            d.y += wb * up * (up > 0 ? BrowUpCm : BrowDownCm);
            float wk = wb * (1 - S(1.2f, 3.5f, ax));
            d.x -= sx * KnitXCm * c[Knit] * wk * S(0f, 0.6f, ax); d.y -= KnitYCm * c[Knit] * wk;
            // щёки, нос
            float wc = math.exp(-((ax - 2.9f) * (ax - 2.9f) + h.y * h.y) / 1.1f) * S(5.5f, 7.0f, h.z);
            float ck = c[CheekL + sd]; d.y += CheekCm * ck * wc; d.z += 0.1f * ck * wc;
            float wn = math.exp(-((ax - 0.9f) * (ax - 0.9f) * 2f + (h.y + 2.3f) * (h.y + 2.3f)) / 0.6f) * S(7.0f, 8.5f, h.z);
            d.y += NoseCm * c[Nose] * wn;
            // губы
            float dyl = h.y - YL, fm = S(6.0f, 7.6f, h.z), lipX = 1 - S(1.7f, 3.0f, ax), lipC = 1 - S(1.2f, 2.6f, ax);
            float wul = S(-0.13f, 0.13f, dyl) * (1 - S(0.7f, 1.9f, dyl)) * fm * lipC;
            float wll = S(0.13f, -0.13f, dyl) * (1 - S(-0.7f, -1.9f, dyl)) * fm * lipC;
            d.y += LipUpCm * math.max(c[LipUp], 0) * wul - LipDownCm * math.max(c[LipDown], 0) * wll;
            float cr = c[CornerL + sd], wcor = math.exp(-((ax - 2.2f) * (ax - 2.2f) * 1.2f + dyl * dyl * 1.6f) / 0.9f) * S(5.0f, 7.0f, h.z);
            d.y += cr * (cr > 0 ? CornerUpCm : CornerDownCm) * wcor; d.x += sx * 0.12f * math.max(cr, 0) * wcor; d.z -= 0.08f * math.max(cr, 0) * wcor;
            float wd = c[Wide], ww = math.exp(-((ax - 2.0f) * (ax - 2.0f) * 0.8f + dyl * dyl * 1.2f) / 1.4f) * fm * S(0f, 1.2f, ax);
            d.x += sx * WideCm * wd * ww * (wd < 0 ? 0.7f : 1f);
            float lb = lipX * fm * math.exp(-dyl * dyl / 0.8f);
            d.z += 0.25f * math.max(-wd, 0) * lb - PressCm * math.max(c[Press], 0) * lb;
            q += d;
            // нижняя челюсть: под швом губ (у рта — резко, к скулам — плавно) поворот вокруг оси суставов
            float sharp = 1 - S(1.8f, 3.4f, ax), wdt = math.lerp(1.4f, 0.13f, sharp), yc = YL - (1 - sharp) * 0.6f;
            float wJ = S(yc + wdt, yc - wdt, h.y) * S(-1.5f, 2.5f, h.z) * S(-11.0f, -8.8f, h.y);
            if (jawA > 0 || c[JawSide] != 0) q = JawPivot + RotX(q - JawPivot, jawA * wJ) + new float3(c[JawSide] * JawSideCm * wJ, 0, 0);
            seam = S(0.13f, -0.13f, dyl); mouth = lipX * fm;
            return q;
        }
    }
}
