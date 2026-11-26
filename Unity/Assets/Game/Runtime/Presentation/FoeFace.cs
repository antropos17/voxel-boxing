using Unity.Mathematics;
using VoxProbe;

namespace VoxGame
{
    // Мимика соперника в бою (Unity/EMOTIONS.md): события боя → выражения AnatFace с приоритетом и длительностью,
    // настрой по здоровью (усталость, страх), характер бойца (FighterGen.TemperOf). Своя случайность — бой и анатомию не трогает.
    // Приоритеты: 1 — замах и мелочи, 2 — насмешка, злость, торжество за удар, 3 — пропущенный удар, 4 — тяжёлый удар,
    // 5 — нокдаун, 6 — нокаут, 7 — развязка (победа или поражение).
    public sealed class FoeFace
    {
        public readonly AnatFace face;
        public AnatTemper Temper => face.temper;
        public string lastEvent = "";          // для тестов и лога
        int nextE = -1, nextPrio; float nextDur, nextGain;
        uint rng;
        public static bool Enabled = true;     // выключатель для замеров «без»

        public FoeFace(in FighterDef f)
        {
            face = new AnatFace(FighterGen.TemperOf(f), f.seed ^ 0xFACEu);
            rng = (f.seed ^ 0x5EEDu) * 2654435761u + 1u;
        }

        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

        void Play(string tag, int e, float gain, float dur, int prio, int side = 0, bool hold = false)
        {
            if (prio <= 2) gain *= 1f - 0.5f * math.saturate(face.fatigue + face.fear);   // избитый и уставший меньше дразнится и злится
            if (face.Play(e, gain, dur, prio, side, hold)) { lastEvent = tag; nextE = -1; }
        }
        void Then(int e, float gain, float dur, int prio) { nextE = e; nextGain = gain; nextDur = dur; nextPrio = prio; }

        // Пропустил удар: сила (0 джеб, 1 силовой, 2 мега), зона AnatZone, сторона удара по x головы (+1/−1).
        public void OnHit(int strength, bool mega, int zone, bool fracture, bool counter, int side)
        {
            var t = face.temper;
            bool jaw = zone == (int)AnatZone.Jaw || zone == (int)AnatZone.Mouth, eye = zone == (int)AnatZone.Orbit || zone == (int)AnatZone.Nose;
            if (mega || strength >= 2 || fracture)
            {
                Play("hit-heavy", AnatEmo.Pain, 1f, 0.55f, 4, side);
                Then(AnatEmo.Dazed, 1f, 1.5f, 3);
            }
            else if (strength == 1)
            {
                Play("hit", AnatEmo.Pain, eye ? 1f : 0.85f, eye ? 0.75f : 0.6f, 3, side);
                if (jaw || counter) Then(AnatEmo.Dazed, 0.8f, 0.9f, 2);
                else Then(t.fear > t.anger ? AnatEmo.Fear : t.anger >= 0.5f ? AnatEmo.Anger : AnatEmo.Focus, 0.8f, 1.0f, 2);
            }
            else
            {
                Play("jab", AnatEmo.Pain, eye ? 0.75f : 0.5f, 0.35f, 2, side);
                if (t.anger >= 0.6f) Then(AnatEmo.Anger, 0.7f, 0.8f, 1);
                else if (t.smirk >= 0.7f && Rand() < 0.5f) Then(AnatEmo.Smirk, 0.7f, 0.8f, 1);
            }
        }

        // Игрок промахнулся или удар ушёл в блок: насмешка по характеру.
        public void OnPlayerMiss()
        {
            var t = face.temper; float r = Rand();
            if (r < t.smirk) Play("miss-smirk", AnatEmo.Smirk, 1f, 1.3f, 2);
            else if (r < t.smirk + t.anger * 0.5f) Play("miss-contempt", AnatEmo.Contempt, 1f, 1.2f, 2);
            else Play("miss-focus", AnatEmo.Focus, 0.9f, 0.8f, 1);
        }

        // Удар соперника: попал (blocked — в блок, broken — пробил блок) или игрок увернулся.
        public void OnFoeLanded(bool blocked, bool broken)
        {
            var t = face.temper;
            if (blocked) { Play("landed-block", t.anger >= 0.5f ? AnatEmo.Anger : AnatEmo.Focus, 0.7f, 0.6f, 1); return; }
            if (broken || Rand() < t.show) Play("landed-triumph", AnatEmo.Triumph, 1f, 1.1f, 2);
            else Play("landed-smirk", AnatEmo.Smirk, 1f, 1.1f, 2);
        }
        public void OnDodged()
        {
            var t = face.temper;
            if (t.fear > t.anger) Play("dodged-surprise", AnatEmo.Surprise, 1f, 0.7f, 2);
            else Play("dodged-anger", AnatEmo.Anger, 1f, 0.9f, 2);
        }

        // Замах и выпад
        public void OnWindup() { var t = face.temper; Play("windup", t.anger >= 0.5f ? AnatEmo.Anger : AnatEmo.Focus, 0.9f, 0.9f, 1); }
        public void OnStrike() { var t = face.temper; Play("strike", t.anger >= 0.5f ? AnatEmo.Rage : AnatEmo.Focus, 1f, 0.4f, 1); }

        public void OnKnockdown() => Play("knockdown", AnatEmo.Dazed, 1f, 0f, 5, 0, true);
        public void OnRise()
        {
            Play("rise", AnatEmo.Tired, 1f, 1.4f, 5);
            var t = face.temper;
            Then(t.fear > 0.6f ? AnatEmo.Fear : AnatEmo.Anger, 0.9f, 1.2f, 3);
        }
        public void OnKnockout() => Play("ko", AnatEmo.Dazed, 1f, 0f, 6, 0, true);
        // Развязка: foeWon — соперник победил (торжество), иначе поражение
        public void OnFinale(bool foeWon) => Play(foeWon ? "won" : "lost", foeWon ? AnatEmo.Triumph : AnatEmo.Defeat, 1f, 0f, 7, 0, true);

        // Кадр: настрой по здоровью и фазе, очередь «потом», шаг пружин.
        public void Tick(float dt, FightState st)
        {
            if (!Enabled) return;
            float hp = st.hpMax > 0f ? st.hp / st.hpMax : 1f;
            face.fatigue = math.saturate((0.55f - hp) / 0.45f) + (st.groggy > 0f ? 0.3f : 0f);
            face.fear = math.saturate((0.3f - hp) / 0.25f);
            bool idle = face.ev < 0 || (face.evLeft <= 0 && !face.hold);
            if (nextE >= 0 && idle) { int e = nextE; nextE = -1; Play("then-" + AnatEmo.Keys[e], e, nextGain, nextDur, nextPrio); }
            face.Step(dt);
        }

        public float JawAngle => Enabled && !face.jawGone ? math.saturate(face.ch[AnatFace.Jaw]) * AnatFace.JawRad : 0f;
        public float JawSide => Enabled && !face.jawGone ? face.ch[AnatFace.JawSide] * AnatFace.JawSideCm : 0f;

        public void Apply(AnatView v)
        {
            if (!Enabled) { AnatFace.Off(); return; }
            face.Apply(v.root, v.a, v.gone);
        }
    }
}
