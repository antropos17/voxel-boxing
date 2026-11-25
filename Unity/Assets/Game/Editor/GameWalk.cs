// GameWalk.cs — сквозной проход игры вне Play Mode через настоящий GameApp: кнопки — GameApp.Press,
// удары — InputReader.Inject, сохранение — во временный файл. На каждом шаге проверка состояния
// (исключение при несовпадении) и строка лога; попутно снимки каждого экрана в портрете и ландшафте
// и проверка кнопок (не меньше 88 px) и надписей (не обрезаны) на снимке.
// Запуск: VoxGame → Game Walk W4 (или script eval: VoxGame.Editor.GameWalk.Walk()).
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VoxGame.Editor
{
    public static class GameWalk
    {
        const float DT = 1f / 60f;
        const int PortW = 540, PortH = 1170, LandW = 1170, LandH = 540;
        const float MinButtonPx = 88f;
        const string BoughtGlove = "wraps";          // самые дешёвые перчатки (150)
        const string PricyGlove = "heavy";           // 250: после покупок не хватает монет
        const UpgradeType BoughtUpgrade = UpgradeType.Power;

        public static string LastLog = "";
        public static string LastUiReport = "";

        static GameApp _app;
        static int _steps;
        static StringBuilder _log, _ui;
        static string _dir;
        static int _uiProblems;

        // ------------------------------------------------------------------ Проход

        [MenuItem("VoxGame/Game Walk W4")]
        public static void Walk()
        {
            _steps = 0;
            _uiProblems = 0;
            _log = new StringBuilder();
            _ui = new StringBuilder();
            _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Shots", "game"));
            Directory.CreateDirectory(_dir);
            string saveDir = Path.Combine(Path.GetTempPath(), "voxgame-walk");
            Directory.CreateDirectory(saveDir);
            string savePath = Path.Combine(saveDir, "save.json");
            if (File.Exists(savePath)) File.Delete(savePath);

            try
            {
                Run(savePath);
            }
            finally
            {
                if (_app != null) Object.DestroyImmediate(_app.gameObject);
                _app = null;
                LastLog = _log.ToString().TrimEnd();
                LastUiReport = _ui.ToString().TrimEnd();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static void Run(string savePath)
        {
            NewApp(savePath);
            var f = _app.Flow;
            Check("boot", f.State == AppState.MainMenu && _app.ActiveScreen == ScreenId.MainMenu && f.Profile.coins == 0,
                  "state=" + f.State + " coins=" + f.Profile.coins);
            Ticks(30);
            Pair("w4-menu");

            // ---- меню → режим → лестница
            Press("menu.play");
            Check("mode", f.State == AppState.ModeSelect && _app.ActiveScreen == ScreenId.ModeSelect, "state=" + f.State);
            Pair("w4-mode");

            Press("mode.ladder");
            Check("roster", f.State == AppState.Roster && _app.ActiveScreen == ScreenId.FighterSelect && f.Mode == GameMode.Career,
                  "state=" + f.State + " mode=" + f.Mode);
            Check("roster-locked", !_app.Press("roster.select:1") || !_app.Press("roster.fight"),
                  "второй соперник закрыт до первой победы");
            Press("roster.select:0");
            Pair("w4-roster");

            // ---- загрузка → бой 1
            Press("roster.fight", tick: false);
            Check("loading", f.State == AppState.Loading && f.FoeLadderIndex == 0, "state=" + f.State + " foe=" + f.Foe.name);
            _app.Tick(DT);
            Pair("w4-loading");
            string foe1 = f.Foe.name;
            WaitState(AppState.Fight, 600, "fight1-start");
            Check("fight1", _app.Fight != null && _app.ActiveScreen == ScreenId.Fight && _app.Fight.Gloves.Look.id == "classic",
                  "foe=" + foe1 + " glove=" + _app.Fight.Gloves.Look.id + " hp=" + F(_app.Fight.State.hpMax));

            var bot = new Bot();
            bot.Fight(90, true, false);
            for (int i = 0; i < 3; i++) { bot.WaitOpen(150); bot.Punch(i % 2 == 0 ? PunchType.Hook : PunchType.Jab, i % 2 == 0 ? -1 : 1); bot.Fight(14, true, false); }
            Pair("w4-fight");

            // ---- бой до победы
            int ticks = bot.Fight(60 * 240, true, true);
            Check("fight1-won", _app.Fight != null && _app.Fight.Finished && _app.Fight.PlayerWon && f.State == AppState.Victory,
                  "ticks=" + ticks + " punches=" + bot.punches + " dodges=" + bot.dodges + " kd=" + _app.Fight.State.knockdowns
                  + " playerHp=" + F(_app.Fight.State.playerHp) + " state=" + f.State);

            // ---- развязка: рефери поднимает руку, толпа ликует
            {
                int w = 0;
                while (w < 900 && f.State == AppState.Victory
                       && !(_app.Fight.Victory.State == VictoryState.Cheer && _app.Fight.Victory.Progress >= 0.6f)) { _app.Tick(DT); w++; }
                var d = _app.Fight;
                Check("victory-raise", d.Victory.State == VictoryState.Cheer && d.RefereeFigure.IsGripping && d.Crowd.Mood == CrowdMood.Cheer
                                       && _app.ActiveScreen == ScreenId.Fight,
                      "victory=" + d.Victory.State + " gripping=" + d.RefereeFigure.IsGripping + " mood=" + d.Crowd.Mood
                      + " belt=" + d.Victory.ShowsBelt + " (пояс — только в титульном бою) screen=" + _app.ActiveScreen);
                Pair("w4-victory");
            }

            // ---- награда
            WaitState(AppState.Reward, 60 * 30, "reward");
            var rw = f.LastReward;
            int coins1 = f.Profile.coins;
            Check("reward", rw != null && rw.won && rw.coins > 0 && rw.ladderAdvanced && f.Profile.ladderIndex == 1 && coins1 == rw.coins
                            && f.Profile.wins == 1 && _app.ActiveScreen == ScreenId.Reward && File.Exists(savePath) && !f.LastSaveFailed,
                  "coins=" + coins1 + " xp=" + rw?.xp + " level=" + f.Profile.level + " ladder=" + f.Profile.ladderIndex
                  + " next=" + rw?.nextFoe + " saves=" + f.SaveCount);
            Pair("w4-reward");

            // ---- магазин: купить перчатки, надеть, купить улучшение
            Press("reward.shop");
            Check("shop", f.State == AppState.Shop && _app.ActiveScreen == ScreenId.Shop, "state=" + f.State);
            Press("shop.glove:" + BoughtGlove);
            Press("shop.buy");
            Check("shop-buy", _app.LastShopResult == ShopResult.Ok && f.Profile.OwnsGlove(BoughtGlove)
                              && f.Profile.coins == coins1 - GlovesCatalog.Get(BoughtGlove).price,
                  "result=" + _app.LastShopResult + " coins=" + f.Profile.coins);
            Press("shop.equip");
            Check("shop-equip", _app.LastShopResult == ShopResult.Ok && f.Profile.equippedGloveId == BoughtGlove,
                  "equipped=" + f.Profile.equippedGloveId);
            Check("shop-equip-again", !_app.Press("shop.equip"), "надетые не надеваются второй раз");
            Press("shop.upgrades");
            // первой победы (200) на перчатки и улучшение не хватает: кнопка улучшения неактивна
            if (f.Profile.coins < PlayerProfile.UpgradeCosts[0])
            {
                Check("shop-upgrade-short", !_app.Press("shop.upgrade:power") && f.Profile.upgradeLevels[(int)BoughtUpgrade] == 0,
                      "coins=" + f.Profile.coins + " cost=" + PlayerProfile.UpgradeCosts[0] + " (кнопка неактивна)");
                Pair("w4-shop-upgrades-short");

                // повторный бой с уже побеждённым соперником: монеты есть, лестница не двигается
                Press("shop.back");
                Check("rematch-roster", f.State == AppState.Roster && f.IsFighterAvailable(0), "state=" + f.State);
                Press("roster.select:0");
                Press("roster.fight", tick: false);
                int c1 = f.Profile.coins;
                WaitState(AppState.Fight, 600, "rematch-start");
                bot = new Bot();
                ticks = bot.Fight(60 * 240, true, true);
                WaitState(AppState.Reward, 60 * 40, "rematch-reward");
                Check("rematch-won", f.LastReward.won && !f.LastReward.ladderAdvanced && f.Profile.ladderIndex == 1 && f.Profile.wins == 2
                                     && f.Profile.coins == c1 + f.LastReward.coins,
                      "ticks=" + ticks + " coins=" + f.Profile.coins + " (+" + f.LastReward.coins + ") ladder=" + f.Profile.ladderIndex);
                Press("reward.shop");
                Press("shop.upgrades");
            }
            int c0 = f.Profile.coins;
            Press("shop.upgrade:power");
            Check("shop-upgrade", _app.LastShopResult == ShopResult.Ok && f.Profile.upgradeLevels[(int)BoughtUpgrade] == 1
                                  && f.Profile.coins == c0 - PlayerProfile.UpgradeCosts[0],
                  "result=" + _app.LastShopResult + " power=" + f.Profile.upgradeLevels[(int)BoughtUpgrade] + " coins=" + f.Profile.coins);
            Pair("w4-shop-upgrades");
            Press("shop.gloves");
            Press("shop.glove:" + PricyGlove);
            Check("shop-short", f.Profile.coins < GlovesCatalog.Get(PricyGlove).price && !_app.Press("shop.buy")
                                && !f.Profile.OwnsGlove(PricyGlove),
                  "coins=" + f.Profile.coins + " price=" + GlovesCatalog.Get(PricyGlove).price + " (кнопка неактивна)");
            Pair("w4-shop-gloves");

            // ---- назад к лестнице, в меню, экран бойца
            Press("shop.back");
            Check("shop-back", f.State == AppState.Roster, "state=" + f.State);
            Press("roster.back");
            Press("mode.back");
            Check("to-menu", f.State == AppState.MainMenu && _app.ActiveScreen == ScreenId.MainMenu, "state=" + f.State);
            Press("menu.player");
            Check("player", f.State == AppState.Player && _app.ActiveScreen == ScreenId.Player, "state=" + f.State);
            Pair("w4-player");
            Press("player.back");

            // ---- бой 2: другой соперник, купленные перчатки и улучшение
            Press("menu.play");
            Press("mode.ladder");
            Press("roster.select:1");
            Press("roster.fight", tick: false);
            Check("loading2", f.State == AppState.Loading && f.FoeLadderIndex == 1 && f.Foe.name != foe1,
                  "foe=" + f.Foe.name + " prev=" + foe1);
            WaitState(AppState.Fight, 600, "fight2-start");
            {
                var d = _app.Fight;
                var expect = f.Profile.GetCombatStats();
                var fp = d.State.fightParams;
                Check("fight2-gear", d.Gloves.Look.id == BoughtGlove && d.Foe.name != foe1
                                     && Mathf.Approximately(fp.dmgMult, expect.dmgMult) && Mathf.Approximately(fp.speedMult, expect.speedMult)
                                     && Mathf.Approximately(fp.staminaCostMult, expect.staminaCostMult)
                                     && !Mathf.Approximately(fp.dmgMult, 1f),
                      "glove=" + d.Gloves.Look.id + " foe=" + d.Foe.name + " dmg=" + F(fp.dmgMult) + " speed=" + F(fp.speedMult)
                      + " stamina=" + F(fp.staminaCostMult) + " (ждали dmg=" + F(expect.dmgMult) + ")");
            }
            bot = new Bot();
            bot.Fight(90, true, false);
            for (int i = 0; i < 2; i++) { bot.WaitOpen(150); bot.Punch(i % 2 == 0 ? PunchType.Jab : PunchType.Hook, 1); bot.Fight(14, true, false); }
            Pair("w4-fight2");

            // ---- пауза: бой стоит; «заново»; продолжить
            Press("fight.pause");
            Check("pause", f.State == AppState.Pause && _app.ActiveScreen == ScreenId.Pause, "state=" + f.State);
            {
                var d = _app.Fight;
                float hp = d.State.hp, php = d.State.playerHp;
                var ph = d.State.phase;
                d.lastSdt = -1f;
                Ticks(120);
                Check("pause-frozen", d.lastSdt == -1f && d.State.hp == hp && d.State.playerHp == php && d.State.phase == ph
                                      && (d.Sound == null || d.Sound.Muted),
                      "lastSdt=" + F(d.lastSdt) + " hp=" + F(d.State.hp) + " muted=" + (d.Sound != null && d.Sound.Muted));
            }
            Pair("w4-pause");
            Press("pause.restart");
            Check("restart", f.State == AppState.Fight && f.RestartCount == 1 && _app.Fight.State.hp == _app.Fight.State.hpMax
                             && _app.Fight.State.playerHp >= 99.9f && _app.Fight.Gloves.Look.id == BoughtGlove,
                  "hp=" + F(_app.Fight.State.hp) + "/" + F(_app.Fight.State.hpMax) + " playerHp=" + F(_app.Fight.State.playerHp));
            Press("fight.pause");
            Press("pause.resume");
            Check("resume", f.State == AppState.Fight && _app.ActiveScreen == ScreenId.Fight && !_app.Fight.Sound.Muted,
                  "state=" + f.State);

            // ---- намеренное поражение: игрок не защищается и не бьёт
            int wins = f.Profile.wins, losses = f.Profile.losses, coinsBefore = f.Profile.coins;
            ticks = bot.Fight(60 * 300, false, false);
            Check("fight2-lost", _app.Fight != null && _app.Fight.Finished && !_app.Fight.PlayerWon,
                  "ticks=" + ticks + " playerHp=" + F(_app.Fight.State.playerHp) + " state=" + f.State);
            WaitState(AppState.Result, 60 * 30, "result");
            Check("result", _app.ActiveScreen == ScreenId.Result && f.LastReward != null && !f.LastReward.won
                            && f.Profile.losses == losses + 1 && f.Profile.wins == wins && f.Profile.ladderIndex == 1
                            && f.Profile.coins == coinsBefore + f.LastReward.coins,
                  "coins=" + f.Profile.coins + " (+" + f.LastReward.coins + ") losses=" + f.Profile.losses);
            Ticks(20);
            Pair("w4-result-lose");
            Press("result.menu");
            Check("menu-after-loss", f.State == AppState.MainMenu && _app.Fight == null, "state=" + f.State);

            // ---- перезагрузка сохранения с диска и сверка
            var p = f.Profile;
            string want = Snapshot(p);
            Object.DestroyImmediate(_app.gameObject);
            _app = null;
            NewApp(savePath);
            f = _app.Flow;
            string got = Snapshot(f.Profile);
            Check("reload", want == got && f.State == AppState.MainMenu, "\n  want " + want + "\n  got  " + got);

            // ---- настройки: громкость, сброс прогресса с подтверждением
            Press("menu.settings");
            Check("settings", f.State == AppState.Settings && _app.ActiveScreen == ScreenId.Settings && _app.Settings.ResetVisible,
                  "state=" + f.State);
            Press("settings.sfx-");
            Check("settings-volume", f.Profile.sfxVolume < 1f, "sfx=" + F(f.Profile.sfxVolume));
            Pair("w4-settings");
            // переключатель «выключено»: вид, сохранение, обратно
            Press("settings.vibration");
            Check("settings-switch-off", !f.Profile.vibrationEnabled && !_app.Settings.SwitchShownOn(false)
                                         && _app.Settings.SwitchCaption(false) == "ВЫКЛ" && !SaveService.Load(savePath).vibrationEnabled,
                  "vibration=" + f.Profile.vibrationEnabled + " caption=" + _app.Settings.SwitchCaption(false));
            Pair("w4-settings-off");
            Press("settings.vibration");
            Press("settings.reset");
            Check("settings-confirm", _app.Settings.ConfirmShown, "подтверждение показано");
            Pair("w4-settings-confirm");
            Press("settings.reset-yes");
            Check("reset", f.Profile.coins == 0 && f.Profile.ladderIndex == 0 && f.Profile.equippedGloveId == "classic"
                           && f.Profile.sfxVolume < 1f,
                  "coins=" + f.Profile.coins + " ladder=" + f.Profile.ladderIndex + " sfx=" + F(f.Profile.sfxVolume));
            Press("settings.back");
            Check("end", f.State == AppState.MainMenu, "state=" + f.State);

            string end = "GAME_WALK ok steps=" + _steps + " coins=" + Field(want, "coins") + " level=" + Field(want, "level")
                         + " gloves=" + Field(want, "gloves") + " equipped=" + Field(want, "equipped") + " upgrades=" + Field(want, "upgrades")
                         + " ladder=" + Field(want, "ladder") + " uiProblems=" + _uiProblems;
            _log.AppendLine(end);
            Debug.Log(end);
        }

        // ------------------------------------------------------------------ Помощники

        static void NewApp(string savePath)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.035f, 1f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            var camData = camGO.AddComponent<UniversalAdditionalCameraData>();
            camData.renderShadows = false;
            camData.antialiasing = AntialiasingMode.None;

            var go = new GameObject("GameRoot");
            _app = go.AddComponent<GameApp>();
            _app.Init(cam, PortW, PortH, savePath);
        }

        static void Ticks(int n) { for (int i = 0; i < n; i++) _app.Tick(DT); }

        static void Press(string id, bool tick = true)
        {
            var s0 = _app.State;
            if (!_app.Press(id)) throw new Exception("GAME_WALK_FAIL press " + id + " отказано в состоянии " + s0 + " экран " + _app.ActiveScreen);
            if (tick) _app.Tick(DT);
            _log.AppendLine("GAME_WALK_PRESS " + id + " " + s0 + " -> " + _app.State);
        }

        static void Check(string name, bool ok, string detail)
        {
            _steps++;
            string line = "GAME_WALK_STEP " + _steps + " " + name + (ok ? " ok " : " FAIL ") + detail;
            _log.AppendLine(line);
            Debug.Log(line);
            if (!ok) throw new Exception("GAME_WALK_FAIL " + line);
        }

        static void WaitState(AppState s, int maxTicks, string name)
        {
            int w = 0;
            while (_app.State != s && w < maxTicks) { _app.Tick(DT); w++; }
            if (_app.State != s) throw new Exception("GAME_WALK_FAIL " + name + ": ждали " + s + ", а " + _app.State + " после " + w + " тиков");
        }

        static string Snapshot(PlayerProfile p)
        {
            var g = new System.Collections.Generic.List<string>(p.boughtGloveIds);
            g.Sort(StringComparer.Ordinal);
            return "coins=" + p.coins + " level=" + p.level + " xp=" + p.xp + " wins=" + p.wins + " losses=" + p.losses
                   + " ladder=" + p.ladderIndex + " belts=" + p.beltCount + " gloves=" + string.Join(",", g) + " equipped=" + p.equippedGloveId
                   + " upgrades=" + string.Join(",", p.upgradeLevels);
        }

        static string Field(string snap, string key)
        {
            foreach (var part in snap.Split(' '))
                if (part.StartsWith(key + "=", StringComparison.Ordinal)) return part.Substring(key.Length + 1);
            return "?";
        }

        static string F(float v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ Снимки и проверка интерфейса

        static void Pair(string tag)
        {
            Snap(PortW, PortH, tag + "-p");
            Snap(LandW, LandH, tag + "-l");
            _app.SetViewSize(PortW, PortH);
        }

        static void Snap(int w, int h, string name)
        {
            _app.SetViewSize(w, h);
            var ui = _app.UI;
            ui.PrepareForShot(w, h);
            ui.FitToCamera(_app.Cam, w, h);
            Canvas.ForceUpdateCanvases();
            Audit(name, w, h);
            ShotsTaker.Shot(_app.Cam, ui, w, h, Path.Combine(_dir, name + ".png"));
        }

        // Кнопки не меньше 88 px и не у самого края; надписи помещаются в свои рамки.
        // Холст на снимке — w×h единиц (World Space, FitToCamera), единица = пиксель снимка.
        static void Audit(string name, int w, int h)
        {
            var root = _app.UI.GetComponent<RectTransform>();
            var sb = new StringBuilder();
            int buttons = 0, texts = 0;
            var corners = new Vector3[4];
            foreach (var b in root.GetComponentsInChildren<Button>(false))
            {
                if (!b.isActiveAndEnabled) continue;
                buttons++;
                var r = PxRect(root, (RectTransform)b.transform, corners, w, h);
                // в прокручиваемом списке кнопка за краем маски скрыта, а не вылезла за экран
                var mask = b.GetComponentInParent<RectMask2D>();
                if (mask != null)
                {
                    var m = PxRect(root, mask.rectTransform, corners, w, h);
                    if (!r.Overlaps(m)) continue;
                    r = Rect.MinMaxRect(Mathf.Max(r.xMin, m.xMin), Mathf.Max(r.yMin, m.yMin), Mathf.Min(r.xMax, m.xMax), Mathf.Min(r.yMax, m.yMax));
                    if (b.GetComponentInParent<ScrollRect>() != null && (r.width < MinButtonPx || r.height < MinButtonPx)) continue;   // частично прокручена
                }
                string label = Label(b);
                if (r.width < MinButtonPx - 0.5f || r.height < MinButtonPx - 0.5f)
                    sb.Append(" small[" + label + " " + Mathf.RoundToInt(r.width) + "x" + Mathf.RoundToInt(r.height) + "]");
                if (r.xMin < -0.5f || r.yMin < -0.5f || r.xMax > w + 0.5f || r.yMax > h + 0.5f)
                    sb.Append(" offscreen[" + label + "]");
            }
            foreach (var t in root.GetComponentsInChildren<Text>(false))
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.color.a < 0.05f) continue;
                texts++;
                var rr = t.rectTransform.rect;
                float ph = t.preferredHeight;
                bool wrap = t.horizontalOverflow == HorizontalWrapMode.Wrap;
                float pw = wrap ? 0f : t.preferredWidth;
                if (t.verticalOverflow == VerticalWrapMode.Truncate && ph > rr.height + 1f)
                    sb.Append(" cutV[" + Short(t.text) + " " + Mathf.RoundToInt(ph) + ">" + Mathf.RoundToInt(rr.height) + "]");
                else if (!wrap && pw > rr.width + 2f && t.GetComponentInParent<Button>() != null)
                    sb.Append(" wide[" + Short(t.text) + " " + Mathf.RoundToInt(pw) + ">" + Mathf.RoundToInt(rr.width) + "]");
                var tr = PxRect(root, t.rectTransform, corners, w, h);
                if (tr.xMax < 0 || tr.xMin > w || tr.yMax < 0 || tr.yMin > h) continue;
                // по центру выровненный текст занимает только ширину строки, а не всю рамку
                bool center = t.alignment == TextAnchor.MiddleCenter || t.alignment == TextAnchor.UpperCenter || t.alignment == TextAnchor.LowerCenter;
                if (center && !wrap && t.preferredWidth < rr.width)
                {
                    float k = tr.width / Mathf.Max(1f, rr.width), half = t.preferredWidth * k * 0.5f, cx = tr.center.x;
                    tr = Rect.MinMaxRect(cx - half, tr.yMin, cx + half, tr.yMax);
                }
                if (tr.xMin < -1f || tr.xMax > w + 1f) sb.Append(" edge[" + Short(t.text) + "]");
            }
            string issues = sb.ToString();
            if (issues.Length > 0) _uiProblems++;
            string line = "GAME_WALK_UI " + name + " screen=" + _app.ActiveScreen + " buttons=" + buttons + " texts=" + texts
                          + (issues.Length > 0 ? issues : " ok");
            _ui.AppendLine(line);
            _log.AppendLine(line);
        }

        static Rect PxRect(RectTransform root, RectTransform rt, Vector3[] c, int w, int h)
        {
            rt.GetWorldCorners(c);
            Vector2 a = root.InverseTransformPoint(c[0]), b = root.InverseTransformPoint(c[2]);
            // начало координат холста — его центр (pivot 0.5), ось y вверх
            float x0 = Mathf.Min(a.x, b.x) + w * 0.5f, x1 = Mathf.Max(a.x, b.x) + w * 0.5f;
            float y0 = h * 0.5f - Mathf.Max(a.y, b.y), y1 = h * 0.5f - Mathf.Min(a.y, b.y);
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        static string Label(Button b)
        {
            var t = b.GetComponentInChildren<Text>();
            return t != null && !string.IsNullOrEmpty(t.text) ? Short(t.text) : b.name;
        }

        static string Short(string s)
        {
            s = s.Replace('\n', ' ');
            return s.Length > 24 ? s.Substring(0, 24) + "…" : s;
        }

        // ------------------------------------------------------------------ Бот боя

        sealed class Bot
        {
            public int punches, dodges;
            bool _dodged;
            int _cool;
            static readonly PunchType[] Combo = { PunchType.Hook, PunchType.Upper, PunchType.Jab, PunchType.Hook };

            public void Punch(PunchType t, int side, float pw = 1.25f)
            {
                _app.FightInput.Inject(new PunchInput { type = t, side = side, pw = pw, speed = 1.2f });
                punches++;
            }

            static bool Open(FightPhase ph) =>
                ph == FightPhase.Idle || ph == FightPhase.Guard || ph == FightPhase.Recover || ph == FightPhase.Open || ph == FightPhase.Stagger;

            public void WaitOpen(int maxTicks)
            {
                for (int i = 0; i < maxTicks && _app.State == AppState.Fight && !_app.Fight.Finished; i++)
                {
                    if (Open(_app.Fight.State.phase)) return;
                    Step(true);
                }
            }

            // Тики боя: защита — кнопками уклона HUD (через Press), атака — серией ударов в открытые фазы.
            // Возвращает число тиков; останавливается, когда исход решён.
            public int Fight(int maxTicks, bool defend, bool attack)
            {
                int i = 0;
                for (; i < maxTicks && _app.State == AppState.Fight && !_app.Fight.Finished; i++)
                {
                    if (attack && _cool <= 0 && Open(_app.Fight.State.phase))
                    {
                        Punch(Combo[punches % Combo.Length], punches % 2 == 0 ? -1 : 1, 1.3f);
                        _cool = 18;
                    }
                    _cool--;
                    Step(defend);
                }
                return i;
            }

            void Step(bool defend)
            {
                var d = _app.Fight;
                if (defend && d.State.phase == FightPhase.Strike && !_dodged)
                {
                    _app.Press(d.FoeAttackSide > 0 ? "fight.dodge-left" : "fight.dodge-right");
                    _dodged = true;
                    dodges++;
                }
                else if (d.State.phase != FightPhase.Strike) _dodged = false;
                _app.Tick(DT);
            }
        }
    }
}
