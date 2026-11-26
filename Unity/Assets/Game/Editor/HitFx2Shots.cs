// HitFx2Shots.cs — замер и снимки правки эффектов удара (Unity/HIT-FX-2.md): разлёт крови вокруг точки удара
// и плашка удара (перекрытие перчатками, контраст, safe-area, полосы HUD и счётчик комбо).
// Бой — настоящий FightDirector, удары — InputReader.Inject (как HitFxShots). Только чтение состояния боя.
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static partial class ShotsTaker
    {
        public static string HitFx2Log = "";

        // Итог одного удара (тесты читают поля).
        public struct HitFx2Res
        {
            public PunchType punch;
            public bool land, hit;
            public int peakFrame, peakVis, peakIn;          // первый кадр максимума видимых; видимых и в круге на 0,15 с
            public float peakInFrac;                        // доля в круге 0,2·W вокруг точки удара в первом кадре максимума
            public float in15Frac, in33Frac;                // то же через 0,15 и 0,33 с
            public int after05Alive; public float after05HighFrac, after05BehindFrac;   // через 0,5 с: выше точки на 0,25·H
            public bool plateSeen; public float hudBottom, plateGloveFrac, plateContrastMin, plateHudFrac, plateComboFrac;
            public float sizeMax, sizeMed, streakMax; public int sizeN;
            public int dropN, dropSep; public float dropFrac;   // доработка 4: капли (стороны на экране ≤1,6:1), видимые отдельно от полос
            public float plateOvMax, plateOv15, plateJumpMax; public int plateFrames, plateOvMaxF;   // плашка по всем кадрам видимости: перекрытие руками, скачок (доля ширины)   // доработка 3: толщина частиц на 0,15 с, доли ширины
            public Color bloodCol, gloveCol; public float bloodLum, bloodGloveContrast;   // средние цвета пикселей (sRGB) и яркость, контраст
            public Vector2 hitN; public float bloodPx15, plateDist, ringOverText, plateGlovePx; public Rect glovePxN; public bool plateInSafe; public Rect plateN, safeN, gloveN0, gloveN1;
        }

        public static readonly List<HitFx2Res> HitFx2Results = new List<HitFx2Res>();

        const int HF2PortW = 1080, HF2PortH = 2400;
        const float HF2Radius = 0.2f, HF2High = 0.25f;
        // Пик крови: все капли удара живут ~0,3 с и больше, число видимых держится с кадра удара (полка), поэтому «кадр максимума»
        // неоднозначен; снимок и главная цифра — на 0,15 с (9 кадров), когда облако раскрылось. Первый кадр максимума — peakFrame.
        const int PeakAt = 9;
        const int T27 = 16;

        // prefix: "" — снимки после правки, "before-" — только пара хука до правки. shots=false — только числа (тесты).
        public static void HitFx2(string prefix = "", bool shots = true)
        {
            var log = new StringBuilder();
            HitFx2Results.Clear();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "hit-fx-2"));
            if (shots) Directory.CreateDirectory(dir);
            bool before = prefix.Length > 0;
            var kinds = new[] { (PunchType.Jab, -1, "jab"), (PunchType.Hook, 1, "hook"), (PunchType.Upper, 1, "upper") };
            foreach (bool land in new[] { false, true })
                foreach (var (t, side, name) in kinds)
                {
                    bool shoot = shots && (before ? (!land && t == PunchType.Hook) : (!land || t == PunchType.Hook));
                    string tag = prefix + name;
                    var r = HitFx2One(t, side, land, shoot ? dir : null, tag, land ? "-land" : "");
                    HitFx2Results.Add(r);
                    log.AppendLine("HITFX2 " + (land ? "land " : "port ") + name + " hit=" + r.hit
                                   + " peakFrame=" + r.peakFrame + " inFrac=" + F(r.peakInFrac) + " vis15=" + r.peakVis + " in15n=" + r.peakIn
                                   + " in15=" + F(r.in15Frac) + " in33=" + F(r.in33Frac) + " bloodPx15=" + r.bloodPx15.ToString("0") + " after05 alive=" + r.after05Alive + " high=" + F(r.after05HighFrac)
                                   + " behind=" + F(r.after05BehindFrac)
                                   + " | plate seen=" + r.plateSeen + " glove=" + F(r.plateGloveFrac) + " contrastMin=" + F(r.plateContrastMin)
                                   + " hit=(" + F(r.hitN.x) + "," + F(r.hitN.y) + ") dist=" + F(r.plateDist) + " glovePx=" + F(r.plateGlovePx) + " gpx=" + RectS(r.glovePxN) + " ringOverText=" + F(r.ringOverText) + " inSafe=" + r.plateInSafe + " hud=" + F(r.plateHudFrac) + " hudBottom=" + F(r.hudBottom) + " combo=" + F(r.plateComboFrac)
                                   + " plate=" + RectS(r.plateN) + " safe=" + RectS(r.safeN) + " g0=" + RectS(r.gloveN0) + " g1=" + RectS(r.gloveN1));
                }
            HitFx2Log = log.ToString();
            Debug.Log(HitFx2Log);
        }

        static string RectS(Rect r) => "(" + F(r.xMin) + "," + F(r.yMin) + "," + F(r.xMax) + "," + F(r.yMax) + ")";

        static HitFx2Res HitFx2One(PunchType t, int side, bool land, string dir, string tag, string suf)
        {
            int w = land ? HF2PortH : HF2PortW, h = land ? HF2PortW : HF2PortH;
            var res = new HitFx2Res { punch = t, land = land, plateContrastMin = -1f };
            var d = NewFight(out var cam, out var ui, 4f, 3f);
            d.SetViewSize(w, h);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;   // проекция с пропорциями снимка
            try
            {
                var run = new Run { d = d };
                run.Ticks(90);
                run.WaitOpen(150);
                // серия к апперкоту, как в HitFxShots: на снимке плашки виден счётчик комбо
                if (t == PunchType.Upper) { PunchAndWaitHit(run, PunchType.Hook, 1, 1.3f); run.Ticks(8); }
                var fx = d.Fx;
                uint born = fx.Particles.Born;
                res.hit = PunchAndWaitHit(run, t, side, t == PunchType.Jab ? 1.1f : 1.35f);
                Vector3 hp = fx.LastHitPoint;
                var buf = new Vector3[FxParticles.MaxParticles];
                string tmpPeak = Path.Combine(Path.GetTempPath(), "vox-hitfx2-peak.png");
                int maxVis = -1;
                bool plateDone = false;
                Vector2 prevC = new Vector2(-1f, -1f);
                for (int f = 0; f <= 56; f++)
                {
                    if (f > 0) run.Tick();
                    BloodStats(cam, fx, born, hp, buf, out int alive, out int vis, out int inside, out int high, out int behind);
                    if (vis > maxVis) { maxVis = vis; res.peakFrame = f; res.peakInFrac = vis > 0 ? (float)inside / vis : 0f; }
                    if (f == PeakAt)
                    {
                        res.peakVis = vis; res.peakIn = inside; res.in15Frac = vis > 0 ? (float)inside / vis : 0f;
                        res.bloodPx15 = ParticlePx(cam, ui, w, h, fx, ref res);
                        SizeStats(cam, fx, born, w, ref res);
                        if (dir != null) HF2Shot(cam, ui, w, h, tmpPeak);
                    }
                    // 0,27 с: перчатка ушла с лица, облако крови открыто (дополнительный снимок, портрет)
                    if (dir != null && !land && f == T27) HF2Shot(cam, ui, w, h, Path.Combine(dir, tag + "-t27.png"));
                    if (f == 20) res.in33Frac = vis > 0 ? (float)inside / vis : 0f;
                    if (f == 30)
                    {
                        res.after05Alive = alive;
                        res.after05HighFrac = alive > 0 ? (float)high / alive : 0f;
                        res.after05BehindFrac = alive > 0 ? (float)behind / alive : 0f;
                        if (dir != null && !land && t == PunchType.Jab) HF2Shot(cam, ui, w, h, Path.Combine(dir, tag + "-after05.png"));
                    }
                    if (f > 0 && fx.Pops.LastPopRect != null && fx.Pops.LastPopShown && fx.Pops.LastPopAge < FxPops.PopLife)
                        PlateFrame(d, cam, ui, w, h, f, ref res, ref prevC);
                    // плашка: первый кадр полной видимости (появление закончилось)
                    if (!plateDone && fx.Pops.LastPopRect != null && fx.Pops.LastPopAge >= FxPops.PopIn * FxPops.PopLife)
                    {
                        plateDone = true;
                        PlateStats(d, cam, ui, w, h, ref res, dir != null ? Path.Combine(dir, tag + "-plate" + suf + ".png") : null);
                    }
                }
                if (dir != null && File.Exists(tmpPeak)) File.Copy(tmpPeak, Path.Combine(dir, tag + "-peak" + suf + ".png"), true);
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release(); Object.DestroyImmediate(rt);
                EndFight(d);
            }
            return res;
        }

        // Капли и морось этого удара: видимые (перед камерой, в кадре, дальше ближней отсечки частиц), в круге 0,2·W
        // вокруг экранной точки удара; выше точки удара больше чем на 0,25·H (в том числе за верхним краем); позади камеры.
        static void BloodStats(Camera cam, FxDirector fx, uint born, Vector3 hp, Vector3[] buf,
                               out int alive, out int vis, out int inside, out int high, out int behind)
        {
            alive = fx.Particles.CopyBlood(born, buf);
            vis = inside = high = behind = 0;
            Vector3 c = cam.WorldToViewportPoint(hp);
            float aspect = (float)cam.pixelWidth / cam.pixelHeight;
            for (int i = 0; i < alive; i++)
            {
                Vector3 v = cam.WorldToViewportPoint(buf[i]);
                if (v.z <= cam.nearClipPlane) { behind++; continue; }
                if (v.y - c.y > HF2High) high++;
                if (v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue;
                if (Vector3.Distance(buf[i], cam.transform.position) <= FxParticles.Near0) continue;
                vis++;
                float dx = v.x - c.x, dy = (v.y - c.y) / aspect;   // в долях ширины
                if (dx * dx + dy * dy <= HF2Radius * HF2Radius) inside++;
            }
        }

        // Снимок без сброса targetTexture (камера остаётся с пропорциями снимка).
        static void HF2Shot(Camera cam, UIRoot ui, int w, int h, string path)
        {
            HF2Begin(cam, ui, w, h);
            var px = HF2Render(cam, w, h);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            ui.RestoreAfterShot();
        }

        static void HF2Begin(Camera cam, UIRoot ui, int w, int h)
        {
            ui.PrepareForShot(w, h);
            ui.FitToCamera(cam, w, h);
            // холст в World Space не масштабируется CanvasScaler: задать масштаб, как у ScreenSpace на телефоне w×h
            // (опорное 540×1170 / 1170×540, match 0,5 — среднее геометрическое по ширине и высоте)
            bool l = w > h;
            float s = Mathf.Sqrt(w / (l ? 1170f : 540f) * (h / (l ? 540f : 1170f)));
            var crt = (RectTransform)ui.transform;
            crt.sizeDelta = new Vector2(w / s, h / s);
            crt.localScale *= s;
            // искусственная safe-area UIRoot (47/34 сверху/снизу, 47/21 по бокам/снизу) — в тех же долях, что на 540×1170
            var fit = ui.GetComponentInChildren<SafeAreaFitter>(true);
            if (fit != null)
                fit.Apply(l ? new Rect(47f * s, 21f * s, w - 94f * s, h - 21f * s) : new Rect(0f, 34f * s, w, h - 81f * s), new Vector2(w, h));
            Canvas.ForceUpdateCanvases();
        }

        static Color[] HF2Render(Camera cam, int w, int h)
        {
            var rt = cam.targetTexture;
            RenderTexture.active = rt;
            cam.Render();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            var px = tex.GetPixels();
            Object.DestroyImmediate(tex);
            RenderTexture.active = null;
            return px;
        }

        // Плашка на полной видимости: перекрытие перчатками, полосами HUD и счётчиком комбо (доля площади плашки),
        // целиком ли внутри safe-area, наименьший контраст текста к фону под плашкой (фон снят без плашки, плашка наложена по альфе).
        static void PlateStats(FightDirector d, Camera cam, UIRoot ui, int w, int h, ref HitFx2Res res, string path)
        {
            var pop = d.Fx.Pops.LastPopRect;
            HF2Begin(cam, ui, w, h);
            res.plateSeen = true;
            res.plateN = UiRectN(cam, pop);
            var sa = ui.transform.Find("SafeArea") as RectTransform;
            res.safeN = sa != null ? UiRectN(cam, sa) : new Rect(0f, 0f, 1f, 1f);
            res.plateInSafe = Inside(res.plateN, res.safeN, 0.001f);
            // расстояние от центра плашки до экранной точки удара, в долях меньшей стороны кадра (портрет — ширины, ландшафт — высоты)
            {
                Vector3 hp = cam.WorldToViewportPoint(d.Fx.LastHitPoint);
                float dx = (res.plateN.center.x - hp.x) * w, dy = (res.plateN.center.y - hp.y) * h;
                res.plateDist = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Min(w, h);
                res.hitN = new Vector2(hp.x, hp.y);
            }
            res.gloveN0 = GloveRectN(cam, 0); res.gloveN1 = GloveRectN(cam, 1);
            float a = Area(res.plateN);
            float g = Area(Inter(res.plateN, res.gloveN0)) + Area(Inter(res.plateN, res.gloveN1))
                    - Area(Inter(Inter(res.plateN, res.gloveN0), res.gloveN1));
            res.plateGloveFrac = a > 0f ? g / a : 0f;
            float hud = 0f;
            foreach (var fld in new[] { "_foeHpBg", "_plrHpBg", "_stamBg", "_megaBg" })
            {
                var f = typeof(FightHUD).GetField(fld, BindingFlags.NonPublic | BindingFlags.Instance);
                var r = f?.GetValue(d.Hud) as RectTransform;
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                Rect hr = UiRectN(cam, r);
                hud += Area(Inter(res.plateN, hr));
                res.hudBottom = res.hudBottom == 0f ? hr.yMin : Mathf.Min(res.hudBottom, hr.yMin);
            }
            res.plateHudFrac = a > 0f ? hud / a : 0f;
            var combo = d.Fx.Pops.ComboRect;
            if (combo != null && combo.gameObject.activeInHierarchy)
            {
                var cb = d.Fx.Pops.ComboRect.GetComponent<Text>();
                Rect cr = UiRectN(cam, combo);
                // видимая часть счётчика — по ширине текста от левого края (pivot слева)
                float tw = cb != null ? cb.preferredWidth / combo.rect.width : 1f;
                cr.width *= Mathf.Clamp01(tw);
                res.plateComboFrac = a > 0f ? Area(Inter(res.plateN, cr)) / a : 0f;
            }

            // перчатки по пикселям: кадр с перчатками и без них (плашка спрятана), доля пикселей плашки, где видна перчатка
            {
                pop.gameObject.SetActive(false);
                var on = HF2Render(cam, w, h);
                var gs = new[] { cam.transform.Find("playerArms/playerGlove0"), cam.transform.Find("playerArms/playerGlove1") };
                foreach (var gt in gs) if (gt != null) gt.gameObject.SetActive(false);
                var off = HF2Render(cam, w, h);
                foreach (var gt in gs) if (gt != null) gt.gameObject.SetActive(true);
                pop.gameObject.SetActive(true);
                res.plateGlovePx = DiffFrac(on, off, w, h, res.plateN);
                res.glovePxN = DiffBox(on, off, w, h);
            }
            // кольцо над текстом: пиксели плашки с кольцами и без них (доля изменившихся)
            {
                var on = HF2Render(cam, w, h);
                d.Fx.Rings.SetDrawn(false);
                var off = HF2Render(cam, w, h);
                d.Fx.Rings.SetDrawn(true);
                res.ringOverText = DiffFrac(on, off, w, h, res.plateN);
            }
            // фон без плашки
            pop.gameObject.SetActive(false);
            var bg = HF2Render(cam, w, h);
            pop.gameObject.SetActive(true);
            var img = pop.GetComponent<Image>();
            var txt = pop.GetComponentInChildren<Text>();
            Color fill = img.color, tc = txt.color;
            float lt = Lum(tc.linear);
            float minC = 99f;
            int x0 = Mathf.Clamp(Mathf.RoundToInt(res.plateN.xMin * w), 0, w - 1), x1 = Mathf.Clamp(Mathf.RoundToInt(res.plateN.xMax * w), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(res.plateN.yMin * h), 0, h - 1), y1 = Mathf.Clamp(Mathf.RoundToInt(res.plateN.yMax * h), 0, h - 1);
            Color fl = fill.linear;
            for (int y = y0; y <= y1; y += 2)
            for (int x = x0; x <= x1; x += 2)
            {
                Color b = bg[x + y * w].linear;
                Color o = fl * fill.a + b * (1f - fill.a);
                float lb = Lum(o);
                float c = (Mathf.Max(lt, lb) + 0.05f) / (Mathf.Min(lt, lb) + 0.05f);
                if (c < minC) minC = c;
            }
            res.plateContrastMin = minC;
            if (path != null)
            {
                var px = HF2Render(cam, w, h);
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.SetPixels(px); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            ui.RestoreAfterShot();
        }

        // Пиксели кадра, которые закрывают частицы: снимок с частицами и без них, разница больше 0,06 по любому каналу.
        // Заодно средний цвет пикселей частиц и пикселей перчаток игрока на том же кадре (перчатки — разница с кадром без них).
        static float ParticlePx(Camera cam, UIRoot ui, int w, int h, FxDirector fx, ref HitFx2Res res)
        {
            HF2Begin(cam, ui, w, h);
            var on = HF2Render(cam, w, h);
            fx.Particles.SetDrawn(false);
            var off = HF2Render(cam, w, h);
            fx.Particles.SetDrawn(true);
            var gs = new[] { cam.transform.Find("playerArms/playerGlove0"), cam.transform.Find("playerArms/playerGlove1") };
            foreach (var gt in gs) if (gt != null) gt.gameObject.SetActive(false);
            var ng = HF2Render(cam, w, h);
            foreach (var gt in gs) if (gt != null) gt.gameObject.SetActive(true);
            ui.RestoreAfterShot();
            Color sb = Color.clear, sg = Color.clear; int nb = 0, ngl = 0;
            for (int i = 0; i < on.Length; i++)
            {
                bool part = Differs(on[i], off[i]);
                if (part) { sb += on[i]; nb++; }
                else if (Differs(on[i], ng[i])) { sg += on[i]; ngl++; }
            }
            res.bloodCol = nb > 0 ? sb / nb : Color.black; res.gloveCol = ngl > 0 ? sg / ngl : Color.black;
            float lb = Lum(res.bloodCol.linear), lg = Lum(res.gloveCol.linear);
            res.bloodLum = lb;
            res.bloodGloveContrast = (Mathf.Max(lb, lg) + 0.05f) / (Mathf.Min(lb, lg) + 0.05f);
            return DiffFrac(on, off, w, h, new Rect(0f, 0f, 1f, 1f)) * w * h;
        }

        static bool Differs(Color p, Color q) => Mathf.Abs(p.r - q.r) > 0.06f || Mathf.Abs(p.g - q.g) > 0.06f || Mathf.Abs(p.b - q.b) > 0.06f;

        // Руки игрока: перчатки (с манжетой) и предплечья
        static readonly string[] ArmNames = { "playerArms/playerGlove0", "playerArms/playerGlove1", "playerArms/playerForeArm0", "playerArms/playerForeArm1" };

        // Кадр видимости плашки: доля её пикселей, где видны перчатка, манжета или предплечье (кадр с руками и без, плашка спрятана);
        // смещение центра плашки от прошлого кадра в долях ширины.
        static void PlateFrame(FightDirector d, Camera cam, UIRoot ui, int w, int h, int f, ref HitFx2Res res, ref Vector2 prevC)
        {
            var pop = d.Fx.Pops.LastPopRect;
            HF2Begin(cam, ui, w, h);
            Rect pr = UiRectN(cam, pop);
            pop.gameObject.SetActive(false);
            var on = HF2Render(cam, w, h);
            var arms = new Transform[ArmNames.Length];
            for (int i = 0; i < arms.Length; i++) { arms[i] = cam.transform.Find(ArmNames[i]); if (arms[i] != null) arms[i].gameObject.SetActive(false); }
            var off = HF2Render(cam, w, h);
            foreach (var a in arms) if (a != null) a.gameObject.SetActive(true);
            pop.gameObject.SetActive(true);
            ui.RestoreAfterShot();
            float ov = DiffFrac(on, off, w, h, pr);
            if (ov > res.plateOvMax) { res.plateOvMax = ov; res.plateOvMaxF = f; }
            if (f == PeakAt) res.plateOv15 = ov;
            res.plateFrames++;
            Vector2 c = new Vector2(pr.center.x * w, pr.center.y * h);
            if (prevC.x >= 0f) res.plateJumpMax = Mathf.Max(res.plateJumpMax, (c - prevC).magnitude / w);
            prevC = c;
        }

        // Экранная толщина частиц крови этого удара (наибольшая сторона куба, у штриха — поперёк движения), доли ширины кадра.
        static void SizeStats(Camera cam, FxDirector fx, uint born, int w, ref HitFx2Res res)
        {
            int m = FxParticles.MaxParticles;
            var pos = new Vector3[m]; var th = new float[m]; var st = new bool[m];
            var ax = new Vector3[m];
            int n = fx.Particles.CopyBloodDims(born, pos, th, st, ax);
            var l = new List<float>(n);
            var drops = new List<Vector2>(); var strk = new List<Vector2>();
            float asp0 = cam.aspect;
            Vector3 hv = cam.WorldToViewportPoint(fx.LastHitPoint);
            Vector2 hpx = new Vector2(hv.x * w, hv.y * w / asp0);
            float tv = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), asp = cam.aspect;
            res.streakMax = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 v = cam.WorldToViewportPoint(pos[i]);
                if (v.z <= 0.01f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue;
                float f = th[i] / (v.z * tv * asp);   // доля ширины
                l.Add(f);
                if (st[i]) res.streakMax = Mathf.Max(res.streakMax, f);
                // соотношение сторон на экране: длина штриха (концы оси) к толщине
                float tp = f * w, lp = 0f;
                if (st[i])
                {
                    Vector3 a = cam.WorldToViewportPoint(pos[i] - ax[i] * 0.5f), b = cam.WorldToViewportPoint(pos[i] + ax[i] * 0.5f);
                    lp = new Vector2((b.x - a.x) * w, (b.y - a.y) * w / asp0).magnitude;
                }
                Vector2 sp = new Vector2(v.x * w, v.y * w / asp0);
                if (Mathf.Max(lp, tp) <= 1.6f * tp) drops.Add(sp); else strk.Add(sp);
            }
            l.Sort();
            res.sizeN = l.Count;
            res.sizeMax = l.Count > 0 ? l[l.Count - 1] : 0f;
            res.sizeMed = l.Count > 0 ? l[l.Count / 2] : 0f;
            res.dropN = drops.Count;
            res.dropFrac = l.Count > 0 ? (float)drops.Count / l.Count : 0f;
            // отдельно видны: вне выпуклой оболочки полос или дальше от точки удара, чем 80-й перцентиль полос
            var ds = new List<float>(strk.Count);
            foreach (var q in strk) ds.Add((q - hpx).magnitude);
            ds.Sort();
            float p80 = ds.Count > 0 ? ds[Mathf.Min(ds.Count - 1, (int)(ds.Count * 0.8f))] : 0f;
            var hull = Hull(strk);
            res.dropSep = 0;
            foreach (var q in drops) if ((q - hpx).magnitude > p80 || !InHull(hull, q)) res.dropSep++;
        }

        // Выпуклая оболочка (монотонная цепь), против часовой
        static List<Vector2> Hull(List<Vector2> pts)
        {
            var p = new List<Vector2>(pts);
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (p.Count < 3) return p;
            var hl = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hl.Count;
                for (int k = 0; k < p.Count; k++)
                {
                    var q = pass == 0 ? p[k] : p[p.Count - 1 - k];
                    while (hl.Count >= start + 2 && Cross(hl[hl.Count - 2], hl[hl.Count - 1], q) <= 0f) hl.RemoveAt(hl.Count - 1);
                    hl.Add(q);
                }
                hl.RemoveAt(hl.Count - 1);
            }
            return hl;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        static bool InHull(List<Vector2> h, Vector2 q)
        {
            if (h.Count < 3) return false;
            for (int i = 0; i < h.Count; i++) if (Cross(h[i], h[(i + 1) % h.Count], q) < 0f) return false;
            return true;
        }

        static Rect DiffBox(Color[] a, Color[] b, int w, int h)
        {
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x += 2)
            {
                Color p = a[x + y * w], q = b[x + y * w];
                if (Mathf.Abs(p.r - q.r) > 0.06f || Mathf.Abs(p.g - q.g) > 0.06f || Mathf.Abs(p.b - q.b) > 0.06f)
                { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            }
            return x1 < 0 ? Rect.zero : Rect.MinMaxRect((float)x0 / w, (float)y0 / h, (float)x1 / w, (float)y1 / h);
        }

        static float DiffFrac(Color[] a, Color[] b, int w, int h, Rect n)
        {
            int x0 = Mathf.Clamp((int)(n.xMin * w), 0, w - 1), x1 = Mathf.Clamp((int)(n.xMax * w), 0, w - 1);
            int y0 = Mathf.Clamp((int)(n.yMin * h), 0, h - 1), y1 = Mathf.Clamp((int)(n.yMax * h), 0, h - 1);
            int all = 0, diff = 0;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Color p = a[x + y * w], q = b[x + y * w];
                all++;
                if (Mathf.Abs(p.r - q.r) > 0.06f || Mathf.Abs(p.g - q.g) > 0.06f || Mathf.Abs(p.b - q.b) > 0.06f) diff++;
            }
            return all > 0 ? (float)diff / all : 0f;
        }

        static float Lum(Color l) => 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b;

        // Прямоугольник элемента холста в долях кадра (холст в World Space на время снимка).
        static readonly Vector3[] _hf2c = new Vector3[4];
        static Rect UiRectN(Camera cam, RectTransform r)
        {
            r.GetWorldCorners(_hf2c);
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 v = cam.WorldToViewportPoint(_hf2c[i]);
                x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        // Экранный прямоугольник перчатки игрока s: углы ориентированного короба сетки (localBounds) в кадре.
        public static Rect GloveRectN(Camera cam, int s)
        {
            var g = cam.transform.Find("playerArms/playerGlove" + s) ?? cam.transform.Find("playerGlove" + s);
            if (g == null || !g.gameObject.activeInHierarchy) return Rect.zero;
            var mf = g.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return Rect.zero;
            Bounds b = mf.sharedMesh.bounds;
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 v = cam.WorldToViewportPoint(g.TransformPoint(p));
                if (v.z <= 0.01f) continue;
                x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
            }
            return x1 > x0 ? Rect.MinMaxRect(x0, y0, x1, y1) : Rect.zero;
        }

        static Rect Inter(Rect a, Rect b)
        {
            float x0 = Mathf.Max(a.xMin, b.xMin), y0 = Mathf.Max(a.yMin, b.yMin), x1 = Mathf.Min(a.xMax, b.xMax), y1 = Mathf.Min(a.yMax, b.yMax);
            return x1 > x0 && y1 > y0 ? Rect.MinMaxRect(x0, y0, x1, y1) : Rect.zero;
        }

        static float Area(Rect r) => r.width * r.height;
        static bool Inside(Rect a, Rect o, float eps) =>
            a.xMin >= o.xMin - eps && a.yMin >= o.yMin - eps && a.xMax <= o.xMax + eps && a.yMax <= o.yMax + eps;
    }
}
