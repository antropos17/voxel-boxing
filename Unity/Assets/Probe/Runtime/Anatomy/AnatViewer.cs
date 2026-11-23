using System.Collections;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VoxProbe
{
    /// Интерактивный просмотр головы в сборке: VoxelAnat.exe -anatView (окно 540×1170).
    /// ЛКМ — орбита, колесо — зум, СКМ или Shift+ЛКМ — сдвиг, F или двойной клик — вид по умолчанию;
    /// 1–4 — разрезы, 0 — целая, [ ] — плоскость на воксель; ПКМ или Ctrl+ЛКМ — удар, Q/W/E — сила, R — восстановить;
    /// ↑↓ — кивок, ←→ — поворот, Shift+←→ — наклон к плечу, пробел — толчок (изгиб шеи, этап A3);
    /// H — подсказка, Esc — выход. -anatViewSelfTest [-anatShots папка] — пройти шаги сам и снять кадр после каждого.
    public class AnatViewer : MonoBehaviour
    {
        public Material anatMaterial;
        public bool selfTest; public string shotsDir;
        static readonly Vector3 Center = new Vector3(0, 1.6f, 0);
        static readonly float3 Ctr = new float3(0, 1.5f, 1.0f);   // см головы

        AnatView v; Camera cam; Light sun;
        // камера: цель и текущее (плавно догоняет цель)
        float yaw, pitch, dist; Vector3 pivot;
        float tYaw, tPitch, tDist; Vector3 tPivot;
        static readonly Key[] Digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 }, Pads = { Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4 };
        const float MinDist = 0.06f, MaxDist = 1.2f;
        bool dragOrbit, dragPan; float lastClick = -1; Vector2 lastClickPos;
        int strength = 1; bool hud = true;
        // подсказка: строка пересобирается 4 раза в секунду, GUIStyle и GUIContent — один раз
        readonly StringBuilder sb = new StringBuilder(512);
        GUIContent text = new GUIContent(""); GUIStyle style; Texture2D bg;
        float fpsAcc; int fpsN; float fps; float hudT;
        bool freezeSkin; int pushN;   // самопроверка держит позу и отлёт неподвижными; номер толчка пробелом
        // толчки пробелом по кругу: (точка удара, направление) в см головы — в лицо, в левую и правую скулу, апперкот
        static readonly (float3 at, float3 dir)[] Pushes =
        {
            (new float3(0, 3, 10), new float3(0, 0, -1)), (new float3(-6.5f, -1, 5), new float3(1, 0, -0.3f)),
            (new float3(6.5f, -1, 5), new float3(-1, 0, -0.3f)), (new float3(0, -8.5f, 7), new float3(0, 1, -0.2f)),
        };

        void Awake()
        {
            Application.targetFrameRate = 120; QualitySettings.vSyncCount = 1;
            if (!Application.isEditor) Screen.SetResolution(540, 1170, FullScreenMode.Windowed);
        }

        void Start()
        {
            cam = Camera.main; sun = FindFirstObjectByType<Light>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
            cam.nearClipPlane = 0.005f; cam.farClipPlane = 10; cam.fieldOfView = 30;
            v = new AnatView(null, Center, anatMaterial);
            AnatSkin.Reset(); AnatSkin.on = true; AnatSkin.SetGlobals(v.root);
            Default(true);
            Rebuild();
            if (selfTest) StartCoroutine(SelfTest());
        }

        // ракурсы: (рыскание, тангаж, расстояние, точка взгляда в см головы)
        void View(float y, float p, float d, float3 at, bool snap)
        {
            tYaw = y; tPitch = p; tDist = d; tPivot = v.ToWorld(at);
            if (snap) { yaw = tYaw; pitch = tPitch; dist = tDist; pivot = tPivot; Place(); }
        }
        void Default(bool snap) => View(-35, 6, 0.75f, Ctr, snap);
        void CutView(int c, bool snap)
        {
            switch (c)
            {
                case 1: View(90, 5, 0.75f, Ctr, snap); break;
                case 2: View(-12, 6, 0.72f, new float3(0, 1, 6), snap); break;
                case 3: View(0, 72, 0.72f, new float3(0, 6, 0), snap); break;
                case 4: View(0, 52, 0.55f, new float3(0, -5.3f, 4), snap); break;
                default: Default(snap); break;
            }
        }

        void Place()
        {
            cam.transform.position = pivot + Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward * dist;
            cam.transform.LookAt(pivot);
            if (sun != null) sun.transform.rotation = cam.transform.rotation * Quaternion.Euler(18, -28, 0);   // свет из-за плеча камеры
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            fpsAcc += dt; fpsN++;
            if (fpsAcc >= 0.25f) { fps = fpsN / fpsAcc; fpsAcc = 0; fpsN = 0; }
            if (!selfTest) Controls(dt);
            if (!freezeSkin) AnatSkin.Step(dt);
            AnatSkin.SetGlobals(v.root);
            float k = 1 - math.exp(-dt * 14f);
            yaw = math.lerp(yaw, tYaw, k); pitch = math.lerp(pitch, tPitch, k); dist = math.exp(math.lerp(math.log(dist), math.log(tDist), k));
            pivot = Vector3.Lerp(pivot, tPivot, k);
            Place();
            hudT -= dt; if (hudT <= 0) { hudT = 0.25f; Rebuild(); }
        }

        void OnApplicationFocus(bool f) { if (!f) { dragOrbit = dragPan = false; } }

        void Controls(float dt)
        {
            var kb = Keyboard.current; var ms = Mouse.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Application.Quit(); return; }
            if (!Application.isFocused || ms == null || kb == null) { dragOrbit = dragPan = false; return; }
            bool shift = kb.shiftKey.isPressed, ctrl = kb.ctrlKey.isPressed;
            Vector2 pos = ms.position.ReadValue(), d = ms.delta.ReadValue();
            bool inside = pos.x >= 0 && pos.y >= 0 && pos.x < Screen.width && pos.y < Screen.height;

            // клавиши
            for (int c = 0; c <= 4; c++)
                if (kb[Digits[c]].wasPressedThisFrame || kb[Pads[c]].wasPressedThisFrame) { v.SetCut(c); CutView(c, false); Rebuild(); }
            if (kb.leftBracketKey.wasPressedThisFrame) { v.Shift(-0.2f); Rebuild(); }
            if (kb.rightBracketKey.wasPressedThisFrame) { v.Shift(0.2f); Rebuild(); }
            if (kb.qKey.wasPressedThisFrame) { strength = 0; Rebuild(); }
            if (kb.wKey.wasPressedThisFrame) { strength = 1; Rebuild(); }
            if (kb.eKey.wasPressedThisFrame) { strength = 2; Rebuild(); }
            if (kb.rKey.wasPressedThisFrame) { v.Restore(); AnatSkin.Reset(); Rebuild(); }
            // поза: стрелки держать, 60°/с; пробел — толчок
            float rate = math.radians(60f) * dt;
            float3 dp = 0;
            if (kb.upArrowKey.isPressed) dp.x -= rate;
            if (kb.downArrowKey.isPressed) dp.x += rate;
            if (kb.leftArrowKey.isPressed) { if (shift) dp.z += rate; else dp.y += rate; }
            if (kb.rightArrowKey.isPressed) { if (shift) dp.z -= rate; else dp.y -= rate; }
            if (math.any(dp != 0)) AnatSkin.Turn(dp);
            if (kb.spaceKey.wasPressedThisFrame) { var pu = Pushes[pushN++ % Pushes.Length]; AnatSkin.Push(pu.at, pu.dir, strength); }
            if (kb.hKey.wasPressedThisFrame) hud = !hud;
            if (kb.fKey.wasPressedThisFrame) Default(false);

            // удар: ПКМ или Ctrl+ЛКМ
            if (inside && (ms.rightButton.wasPressedThisFrame || (ctrl && ms.leftButton.wasPressedThisFrame))) { Strike(pos); return; }

            // ЛКМ: двойной клик — вид по умолчанию, иначе орбита (с Shift — сдвиг)
            if (inside && ms.leftButton.wasPressedThisFrame)
            {
                if (Time.unscaledTime - lastClick < 0.3f && (pos - lastClickPos).sqrMagnitude < 36) { Default(false); lastClick = -1; }
                else { lastClick = Time.unscaledTime; lastClickPos = pos; }
                if (shift) dragPan = true; else dragOrbit = true;
            }
            if (inside && ms.middleButton.wasPressedThisFrame) dragPan = true;
            if (!ms.leftButton.isPressed && !ms.middleButton.isPressed) dragOrbit = dragPan = false;
            if (dragOrbit && !shift) { tYaw += d.x * 0.25f; tPitch = math.clamp(tPitch - d.y * 0.25f, -85f, 85f); }
            else if (dragPan || dragOrbit)
            {
                float s = 2 * tDist * math.tan(math.radians(cam.fieldOfView * 0.5f)) / Screen.height;
                tPivot -= cam.transform.right * d.x * s + cam.transform.up * d.y * s;
            }
            float wheel = ms.scroll.ReadValue().y;
            if (inside && wheel != 0) tDist = math.clamp(tDist * math.exp(-math.sign(wheel) * math.min(math.abs(wheel), 360f) / 120f * 0.15f), MinDist, MaxDist);
        }

        /// Удар по лучу из камеры через точку экрана.
        float3 lastPick;
        bool Strike(Vector2 screen)
        {
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0));
            if (!v.Pick(ray, out var hit, out _, out _)) { v.lastWhat = "мимо"; Rebuild(); return false; }
            AnatSkin.Push(hit, v.pickDir, strength);
            v.Hit(hit, v.pickDir, strength);
            lastPick = hit;
            Rebuild();
            return true;
        }

        void Rebuild()
        {
            if (v == null) return;
            sb.Clear();
            sb.Append("ЛКМ — вращать · колесо — ближе/дальше\nСКМ или Shift+ЛКМ — сдвиг · F, двойной клик — вид\n");
            sb.Append("ПКМ или Ctrl+ЛКМ — удар · Q/W/E — сила · R — восстановить\n");
            sb.Append("1–4 — разрезы, 0 — целая · [ ] — плоскость · H — скрыть · Esc — выход\n");
            sb.Append("↑↓ — кивок · ←→ — поворот · Shift+←→ — наклон · пробел — толчок\n");
            sb.Append("Сила: ").Append(AnatView.Strengths[strength].name).Append(" · разрез: ").Append(AnatView.CutName[v.cut]);
            if (v.cut > 0) sb.Append(" (").Append(v.shift[v.cut] >= 0 ? "+" : "").Append(v.shift[v.cut].ToString("0.0")).Append(" см)");
            float3 pd = math.degrees(AnatSkin.Total);
            sb.Append('\n').Append("Поза: кивок ").Append(pd.x.ToString("0")).Append("°, поворот ").Append(pd.y.ToString("0")).Append("°, наклон ").Append(pd.z.ToString("0")).Append('°');
            sb.Append('\n').Append("Кадров/с: ").Append(fps.ToString("0")).Append(" · треугольников: ").Append(v.Triangles.ToString("N0"));
            sb.Append('\n').Append("Чанков: ").Append(v.Chunks).Append(" (с сеткой ").Append(v.ChunksDrawn).Append(')');
            sb.Append('\n').Append("Перестройка: ").Append(v.lastWhat).Append(" — ").Append(v.lastMs.ToString("0.0")).Append(" мс");
            if (v.lastRemoved > 0) sb.Append(", убрано ").Append(v.lastRemoved).Append(" вокс.");
            text.text = sb.ToString();
        }

        void OnGUI()
        {
            if (!hud || v == null) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
                style.normal.textColor = new Color(0.92f, 0.93f, 0.95f);
                bg = new Texture2D(1, 1); bg.SetPixel(0, 0, new Color(0, 0, 0, 0.55f)); bg.Apply();
            }
            style.fontSize = Mathf.Max(11, Mathf.RoundToInt(Screen.height / 92f));
            float w = Mathf.Min(Screen.width - 16, style.fontSize * 37f);
            float h = style.CalcHeight(text, w - 12);
            GUI.DrawTexture(new Rect(8, 8, w, h + 10), bg);
            GUI.Label(new Rect(14, 13, w - 12, h), text, style);
        }

        // ── самопроверка: повороты, каждый разрез, три удара; кадр окна (с подсказкой) после каждого шага ──
        IEnumerator SelfTest()
        {
            string dir = shotsDir ?? System.IO.Path.Combine(Application.persistentDataPath, "anatomy-view");
            System.IO.Directory.CreateDirectory(dir);
            var log = new StringBuilder("{\n");
            for (int i = 0; i < 20; i++) yield return null;   // окно встаёт в размер, шейдеры прогреваются
            log.Append("  \"window\": \"").Append(Screen.width).Append('x').Append(Screen.height).Append("\",\n");
            log.Append("  \"intactTris\": ").Append(v.Triangles).Append(", \"chunks\": ").Append(v.Chunks).Append(",\n");
            int n = 0;
            IEnumerator Shot(string name)
            {
                Rebuild();
                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForEndOfFrame();
                var t = ScreenCapture.CaptureScreenshotAsTexture();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, (++n).ToString("00") + "-" + name + ".png"), t.EncodeToPNG());
                Destroy(t);
                log.Append("  \"").Append(name).Append("\": { \"tris\": ").Append(v.Triangles).Append(", \"ms\": ").Append(v.lastMs.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                   .Append(", \"removed\": ").Append(v.lastRemoved).Append(", \"fps\": ").Append(fps.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append(" },\n");
            }
            yield return Shot("default");
            View(60, 6, 0.75f, Ctr, true); yield return Shot("orbit-60");
            View(160, 28, 0.80f, Ctr, true); yield return Shot("orbit-back-top");
            View(-20, 4, 0.08f, new float3(-3.2f, 2.3f, 6.2f), true); yield return Shot("zoom-eye");
            for (int c = 1; c <= 4; c++) { v.SetCut(c); CutView(c, true); yield return Shot("cut-" + c); }
            v.SetCut(1); CutView(1, true); v.Shift(0.2f); v.Shift(0.2f); yield return Shot("cut-1-shift+0.4");
            v.SetCut(0); Default(true); yield return Shot("cut-0");
            // изгиб шеи (A3): кивок, наклон, поворот на 60°, удар в повёрнутой голове, отлёт; поза держится неподвижно
            freezeSkin = true;
            View(90, 4, 0.85f, new float3(0, -3, 0), true);
            AnatSkin.pose = math.radians(new float3(30, 0, 0)); yield return Shot("pose-nod-forward-30");
            AnatSkin.pose = math.radians(new float3(-30, 0, 0)); yield return Shot("pose-nod-back-30");
            View(0, 4, 0.85f, new float3(0, -3, 0), true);
            AnatSkin.pose = math.radians(new float3(0, 0, 30)); yield return Shot("pose-tilt-30");
            AnatSkin.pose = math.radians(new float3(0, 60, 0)); yield return Shot("pose-turn-60");
            View(-35, 6, 0.85f, new float3(0, -3, 0), true); yield return Shot("pose-turn-60-3q");
            // предельные позы и подбородок снизу-спереди при повороте 60°
            View(90, 4, 0.85f, new float3(0, -3, 0), true);
            AnatSkin.pose = math.radians(new float3(45, 0, 0)); yield return Shot("pose-nod-forward-45");
            View(0, 4, 0.85f, new float3(0, -3, 0), true);
            AnatSkin.pose = math.radians(new float3(0, 80, 0)); yield return Shot("pose-turn-80");
            AnatSkin.pose = math.radians(new float3(0, 0, 40)); yield return Shot("pose-tilt-40");
            AnatSkin.pose = math.radians(new float3(0, 60, 0));
            View(35, -12, 0.45f, new float3(0, -9, 3), true); yield return Shot("pose-turn-60-chin-below");   // ниже подбородка, но выше среза шеи
            // удар в скулу повёрнутой головы: прицел — изогнутая точка скулы; камера смотрит на неё по её нормали
            float3 cheekRest = new float3(-4.5f, -2.5f, 6), cheekPosed = AnatSkin.Skin(cheekRest, out var cm);
            float3 look = math.mul(cm, math.normalize(new float3(-0.6f, 0, 1)));
            strength = 1; View(math.degrees(math.atan2(look.x, look.z)), 0, 0.40f, cheekPosed, true); yield return null;
            bool hp = Strike(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            float3 pickRest = lastPick;
            AnatSkin.recoil = 0; AnatSkin.recoilV = 0;
            yield return Shot("pose-turn-60-hit");
            View(-35, 6, 0.85f, new float3(0, -3, 0), true); yield return Shot("pose-turn-60-hit-wide");
            log.Append("  \"poseHit\": { \"hit\": ").Append(hp ? "true" : "false").Append(", \"restPoint\": \"").Append(pickRest.ToString()).Append("\", \"aimRest\": \"").Append(cheekRest.ToString()).Append("\" },\n");
            v.Restore();
            // отлёт: тяжёлый удар в лицо, кадр на пике (0,12 с) и после затухания (ещё 1,4 с)
            AnatSkin.Reset(); View(90, 4, 0.85f, new float3(0, -3, 0), true);
            AnatSkin.Push(new float3(0, 3, 10), new float3(0, 0, -1), 2);
            for (int i = 0; i < 7; i++) AnatSkin.Step(1 / 60f); yield return Shot("recoil-peak");
            log.Append("  \"recoilPeakDeg\": \"").Append(math.degrees(AnatSkin.recoil).ToString()).Append("\",\n");
            for (int i = 0; i < 84; i++) AnatSkin.Step(1 / 60f); yield return Shot("recoil-settled");
            log.Append("  \"recoilSettledDeg\": \"").Append(math.degrees(AnatSkin.Total).ToString()).Append("\",\n");
            // цена изгиба: время кадра без вертикальной синхронизации, изгиб выкл/вкл (поза — поворот 60°)
            AnatSkin.Reset(); AnatSkin.pose = math.radians(new float3(0, 60, 0)); Default(true);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            int bn = 0;
            foreach (bool sk in new[] { false, true, false, true })
            {
                AnatSkin.on = sk;
                for (int i = 0; i < 30; i++) yield return null;
                float acc = 0; for (int i = 0; i < 240; i++) { yield return null; acc += Time.unscaledDeltaTime; }
                log.Append("  \"frameMsSkin").Append(sk ? "On" : "Off").Append(++bn).Append("\": ").Append((acc / 240 * 1000).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
            }
            AnatSkin.on = true; AnatSkin.Reset(); QualitySettings.vSyncCount = 1; Application.targetFrameRate = 120;
            freezeSkin = false;
            // три удара по лучу из камеры в центр экрана: щека (лёгкий), лоб (средний), свод (тяжёлый)
            Vector2 mid = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            strength = 0; View(-40, 0, 0.45f, new float3(-4.5f, -2.5f, 6), true); yield return null; bool h1 = Strike(mid); yield return Shot("hit-light-cheek");
            strength = 1; View(-10, 10, 0.45f, new float3(-2.5f, 7.5f, 8.5f), true); yield return null; bool h2 = Strike(mid); yield return Shot("hit-medium-forehead");
            strength = 2; View(-30, 50, 0.45f, new float3(-3f, 12f, 2f), true); yield return null; bool h3 = Strike(mid); yield return Shot("hit-heavy-vault");
            View(-30, 50, 0.55f, new float3(-3f, 10f, 2f), true); yield return Shot("hits-overview");
            v.SetCut(1); CutView(1, true); yield return Shot("hits-cut-1");
            v.Restore(); Default(true); yield return Shot("restored");
            log.Append("  \"hits\": [").Append(h1 ? "true" : "false").Append(", ").Append(h2 ? "true" : "false").Append(", ").Append(h3 ? "true" : "false").Append("],\n");
            log.Append("  \"diffAfterRestore\": ").Append(v.DiffFromPristine()).Append(",\n  \"ok\": true\n}\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "selftest.json"), log.ToString());
            Debug.Log("ANAT_VIEW_SELFTEST_OK " + dir);
            Application.Quit();
        }

        void OnDestroy() { v?.Dispose(); if (bg != null) Destroy(bg); }
    }
}
