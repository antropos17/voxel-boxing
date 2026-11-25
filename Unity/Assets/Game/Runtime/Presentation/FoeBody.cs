using UnityEngine;

namespace VoxGame
{
    // Воксельное тело соперника по пояс и ниже: торс с трусами, плечи, предплечья, перчатки.
    // Соперник стоит в начале координат корня лицом в +Z. Голова (AnatView) телу не принадлежит:
    // директор каждый кадр ставит её в headMount.
    // НЕ MonoBehaviour — создаётся и управляется из FightDirector.
    public class FoeBody
    {
        public readonly Transform root;       // корень соперника (масштаб тела = FighterDef.scale)
        public readonly Transform headMount;  // сюда ставится начало координат головы (уровень ушей)

        // ---- Размеры (метры, от корня при масштабе 1) -------------------------------------------
        const float WaistY  = 0.98f;   // пояс: начало координат формы торса
        const float HipDrop = 0.10f;   // ось наклона корпуса ниже пояса
        const float HeadY   = 0.66f;   // от пояса до начала координат головы: низ шеи головы (−19.95 см) уходит в трапеции
        const float HeadZ   = 0.02f;
        const float NeckLen = 0.1995f; // от начала координат головы до низа её шеи
        const float ShoulderX = 0.195f, ShoulderY = 0.415f;
        const float TorsoCell = 0.7f;  // см

        // Номера цветов торса
        const byte CSkin = 1, CTrunks = 2, CAccent = 3, CTrunksDark = 4;

        // ---- Объекты -----------------------------------------------------------------------------
        readonly Transform torsoJoint;
        readonly Transform[] upper = new Transform[2], fore = new Transform[2], glove = new Transform[2];
        readonly Vector3[] shoulderLocal = new Vector3[2];   // в осях torsoJoint
        readonly Mesh[] meshes;
        readonly float scale, shoulderK;

        // ---- Состояние анимации ------------------------------------------------------------------
        readonly Vector3[] glovePos = new Vector3[2];   // центры перчаток в осях корня (до масштаба)
        float lean, twist, drop, lunge, anim;
        float impLean, impLeanV, impTwist, impTwistV;
        FightPhase phase = FightPhase.Idle;

        // Рука 0 — на экране слева (мир +X, правая рука соперника), рука 1 — справа.
        static float ArmSign(int s) => s == 0 ? 1f : -1f;

        public FoeBody(Transform parent, FighterDef def, Material mat)
        {
            scale = Mathf.Clamp(def.scale <= 0f ? 1f : def.scale, 0.9f, 1.1f);
            // ширина плеч: 0.85–1.15 из генератора сжимается, чтобы плечи оставались 2.2–2.8 ширины головы
            shoulderK = 0.90f * Mathf.Lerp(0.94f, 1.06f, Mathf.InverseLerp(0.85f, 1.15f, def.build.shoulderMult));

            root = new GameObject("FoeBody").transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * scale;

            torsoJoint = new GameObject("torsoJoint").transform;
            torsoJoint.SetParent(root, false);
            torsoJoint.localPosition = new Vector3(0f, WaistY - HipDrop, 0f);

            headMount = new GameObject("headMount").transform;
            headMount.SetParent(torsoJoint, false);

            Color32 skin = BoxerParts.HeadSkin;
            var torsoMesh = BuildTorso(skin, def.colors.trunks, def.colors.accent);
            var t = VoxModel.Spawn("torso", torsoJoint, torsoMesh, mat);
            t.localPosition = new Vector3(0f, HipDrop, 0f);

            var upperMesh = BoxerParts.UpperArmMesh(skin);
            var foreMesh = BoxerParts.ForeArmMesh(skin);
            var gloveMeshes = new[]
            {
                BoxerParts.GloveMesh(-1, def.colors.gloveA, def.colors.gloveB),
                BoxerParts.GloveMesh(+1, def.colors.gloveA, def.colors.gloveB),
            };
            meshes = new[] { torsoMesh, upperMesh, foreMesh, gloveMeshes[0], gloveMeshes[1] };

            for (int s = 0; s < 2; s++)
            {
                upper[s] = VoxModel.Spawn("upperArm" + s, root, upperMesh, mat);
                fore[s]  = VoxModel.Spawn("foreArm" + s, root, foreMesh, mat);
                glove[s] = VoxModel.Spawn("glove" + s, root, gloveMeshes[s], mat);
                shoulderLocal[s] = new Vector3(ArmSign(s) * ShoulderX * shoulderK, HipDrop + ShoulderY, 0f);
                glovePos[s] = IdleGlove(s, 0f);
            }
            Pose(Vector3.zero);
        }

        // ---- Форма торса (см; начало — пояс, +Z — грудь) -------------------------------------------

        float Body(float x, float y, float z)
        {
            x /= shoulderK;
            float ax = Mathf.Abs(x), d;
            d = VoxModel.Ell(x, y - 29, z + 1, 15.0f, 16.5f, 10.8f);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 12, z - 1.5f, 13.2f, 13.5f, 9.6f), 5);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 2, z, 14.4f, 7.5f, 10.4f), 4);
            d = VoxModel.Smin(d, VoxModel.Ell(ax - 7.4f, y - 33.8f, z - 6.0f, 8.8f, 5.8f, 3.0f), 2.2f);   // грудные
            d = VoxModel.Smin(d, VoxModel.Ell(ax - 10.5f, y - 27, z + 3.5f, 6.5f, 13, 7), 4);            // широчайшие
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 34, z + 4.5f, 15.5f, 13, 8.6f), 4);                 // спина
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 45.5f, z + 3.0f, 11.5f, 4.8f, 6.0f), 4);            // надплечья
            d = VoxModel.Smin(d, VoxModel.Ell(ax - 18.6f, y - 41.2f, z + 0.3f, 5.8f, 6.8f, 5.9f), 3);    // дельты
            d = VoxModel.Smin(d, VoxModel.Cap(ax, y, z, 2, 45, 3.5f, 16, 46.5f, 0, 1.8f), 2);            // ключицы
            // основание шеи под шею головы (у головы низ шеи — эллипс 8.6 × 5.8 см) и скаты трапеций
            d = VoxModel.Smin(d, VoxModel.Ell(x, y - 47.0f, z + 0.3f, 9.4f, 5.2f, 6.6f), 3);
            d = VoxModel.Smin(d, VoxModel.Cap(ax, y, z, 4.0f, 50.5f, -1.4f, 14.5f, 46.8f, -0.8f, 3.3f, 3.0f), 3);
            // пресс: три ряда выпуклостей на передней стенке живота
            if (z > 3f && y > 10.5f && y < 27.9f && ax < 6.6f)
                d -= 0.6f * Mathf.Pow(Mathf.Sin(Mathf.PI * ax / 6.6f), 0.6f)
                          * Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * (y - 10.5f) / 5.8f)), 0.6f);
            return d;
        }

        // Трусы: оболочка поверх низа корпуса с поясом, штанины до середины бедра.
        float Trunks(float x, float y, float z)
        {
            x /= shoulderK;
            float ax = Mathf.Abs(x);
            float lower = VoxModel.Smin(VoxModel.Ell(x, y - 12, z - 1.5f, 13.2f, 13.5f, 9.6f),
                                        VoxModel.Ell(x, y - 2, z, 14.4f, 7.5f, 10.4f), 4);
            float d = lower - (y > 8.2f ? 1.5f : 1.0f);
            d = VoxModel.Smin(d, VoxModel.Ell(x, y + 9, z, 17.2f, 12.5f, 11.8f), 4);
            d = VoxModel.Smin(d, VoxModel.Cap(ax, y, z, 9.0f, -8, 0, 10.6f, -26, 0.8f, 10.0f, 10.6f), 3);
            return Mathf.Max(d, Mathf.Max(y - 12.4f, -26f - y));
        }

        static float Thighs(float x, float y, float z) =>
            VoxModel.Cap(Mathf.Abs(x), y, z, 9.0f, -8, 0, 10.2f, -44, 1.0f, 8.6f, 7.6f);

        Mesh BuildTorso(Color32 skin, Color trunks, Color accent)
        {
            var mn = new Vector3(-28f, -36.4f, -15.4f); var mx = new Vector3(28f, 54.6f, 14f);
            var m = VoxModel.FromSdf(mn, mx, TorsoCell,
                (x, y, z) => Mathf.Min(Mathf.Min(Body(x, y, z), Trunks(x, y, z)), Thighs(x / shoulderK, y, z)),
                (x, y, z) =>
                {
                    if (y > 12.4f || y < -26f || Trunks(x, y, z) > 0.35f) return CSkin;
                    if (y > 8.2f) return CAccent;                                         // пояс
                    if (y > 7.4f) return CTrunksDark;                                     // шов под поясом
                    if (Mathf.Abs(x / shoulderK) > 11.5f && Mathf.Abs(z) < 3.2f) return CAccent;   // лампасы
                    if (y < -23.5f) return CAccent;                                       // кант штанин
                    return CTrunks;
                });
            Color32 tr = trunks, ac = accent;
            // чёрные трусы на тёмном фоне не читаются — приподнять до тёмно-серого
            if (tr.r + tr.g + tr.b < 90) tr = new Color32(44, 44, 52, 255);
            Color32 dark = Color.Lerp(tr, Color.black, 0.45f);
            return m.BuildMesh("torso", new[] { default, skin, tr, ac, dark }, mn * 0.01f, TorsoCell * 0.01f, 0.3f,
                               new[] { 1f, 1f, 0.35f, 0.5f, 0.5f });
        }

        // ---- Позы (в осях корня, метры; z — к игроку) -----------------------------------------------

        Vector3 IdleGlove(int s, float bob) =>
            new Vector3(ArmSign(s) * 0.13f, 1.43f + (s == 0 ? bob : -bob), 0.27f);

        // Тик: cmd — команда ИИ, strikeTarget — куда летит бьющая перчатка (мир, у камеры игрока).
        public void Tick(AICommand cmd, float sdt, Vector3 strikeTarget)
        {
            phase = cmd.newPhase;
            anim += sdt;
            float bob = Mathf.Sin(anim * 5f) * 0.008f;
            Vector3 t0 = IdleGlove(0, bob), t1 = IdleGlove(1, bob);
            float leanT = 0f, twistT = Mathf.Sin(anim * 1.4f) * 1.5f, dropT = 0f, lungeT = 0f, rate = 12f;
            int hand = cmd.side < 0 ? 0 : 1;   // сторона удара на экране: слева — рука 0
            float hs = ArmSign(hand);

            switch (phase)
            {
                case FightPhase.Guard:
                    t0 = new Vector3(0.115f, 1.47f, 0.28f); t1 = new Vector3(-0.115f, 1.47f, 0.28f); leanT = 3f; break;
                case FightPhase.Windup:
                {
                    Vector3 g = cmd.isHook ? new Vector3(hs * 0.36f, 1.50f, 0.12f) : new Vector3(hs * 0.22f, 1.55f, 0.13f);
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
                    t0 = new Vector3(0.27f, 1.24f, 0.26f); t1 = new Vector3(-0.26f, 1.20f, 0.25f); leanT = 12f; lungeT = 0.08f; break;
                case FightPhase.Stagger:
                    t0 = new Vector3(0.23f, 1.34f, 0.20f); t1 = new Vector3(-0.23f, 1.34f, 0.20f); leanT = -7f; break;
                case FightPhase.Down:
                    // на коленях, руки упёрты в настил
                    t0 = new Vector3(0.24f, 0.07f, 0.36f); t1 = new Vector3(-0.24f, 0.07f, 0.36f);
                    leanT = 30f; dropT = 0.60f; rate = 6f; break;
                case FightPhase.Rise:
                    t0 = new Vector3(0.20f, 1.22f, 0.24f); t1 = new Vector3(-0.20f, 1.22f, 0.24f);
                    leanT = 10f; dropT = 0f; rate = 2.2f; break;
                case FightPhase.KO:
                    // лицом в настил
                    t0 = new Vector3(0.30f, 0.07f, 0.62f); t1 = new Vector3(-0.30f, 0.07f, 0.62f);
                    leanT = 78f; dropT = 0.80f; rate = 5f; break;
            }

            float k = 1f - Mathf.Exp(-rate * sdt);
            glovePos[0] = Vector3.Lerp(glovePos[0], t0, k);
            glovePos[1] = Vector3.Lerp(glovePos[1], t1, k);
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
            Pose(new Vector3(0f, -drop, lunge - drop * 0.25f));
        }

        // Расставить корпус и руки по текущему состоянию. offset — смещение корпуса в осях корня.
        void Pose(Vector3 offset)
        {
            torsoJoint.localPosition = new Vector3(0f, WaistY - HipDrop, 0f) + offset;
            torsoJoint.localRotation = Quaternion.Euler(lean + Mathf.Clamp(impLean, -14f, 14f),
                                                        twist + Mathf.Clamp(impTwist, -18f, 18f), 0f);
            // голова не масштабируется вместе с телом: к масштабу привязан низ её шеи (он утоплен в трапеции),
            // а сама шея остаётся своей длины
            headMount.localPosition = new Vector3(0f, HipDrop + HeadY - NeckLen + NeckLen / scale, HeadZ / scale);

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
                                   : Vector3.Lerp(right * sg * 0.75f - up - fwd * 0.2f, right * sg * 0.25f - up - fwd * 0.7f, armLow);
                Vector3 back = right * sg * 0.85f + fwd * 0.5f;
                BoxerParts.SolveArm(upper[s], fore[s], glove[s], sh, gt, kn, pole, back, scale);
            }
        }

        // Импульс от удара игрока (мир): корпус отклоняется назад и скручивается.
        public void AddImpulse(Vector3 worldImpulse)
        {
            Vector3 loc = root.InverseTransformDirection(worldImpulse);
            impLeanV  += loc.z * 60f;    // удар в лицо (−Z в осях соперника) — корпус назад
            impTwistV += loc.x * 80f;
        }

        public void Dispose()
        {
            foreach (var m in meshes) GameUtil.Destroy(m);
            if (root != null) GameUtil.Destroy(root.gameObject);
        }
    }
}
