# Игровой слой — план
Дата: 25.11.2026. Автор: архитектор game-layer.

## Цель
Добавить в Unity 6 URP игровой слой (`Unity/Assets/Game/`) поверх готовой анатомической
головы. Всё строится кодом; ручной работы в редакторе нет. Выпуск: App Store / Google Play / Steam.

---

## Устройство

### Сборки
- **VoxGame** (`Assets/Game/Runtime/`) — логика (чистый C#, без MB) + представление (тонкие MB). References: VoxProbe (autoRef=true), Unity.InputSystem, Unity.Mathematics, UnityEngine.UI.
- **VoxGame.Editor** (`Assets/Game/Editor/`) — GameSetup, ShotsTaker, GameWalk (сквозной проход). References: VoxGame, VoxProbe.Editor.
- **VoxGame.Tests** (`Assets/Game/Tests/`) — EditMode-тесты. References: VoxGame, UnityEngine/Editor.TestRunner.

### Модули (неизменяемые Anat* — только вызов через публичный API)

| Модуль | Пакет | Тип |
|---|---|---|
| FightState, FightSim, PunchInput, HitResult | Logic/ | чистый C# |
| FighterDef, FighterGen, RosterDef | Logic/ | чистый C# |
| FightAI | Logic/ | чистый C# |
| PlayerProfile, GlovesCatalog, EconomyState, SaveService | Logic/ | чистый C# |
| ModeManager | Logic/ | чистый C# |
| FightDirector, InputReader | Presentation/ | MB |
| FoeBody, PlayerGloves, RingBuilder | Presentation/ | MB |
| RefereeView, CrowdView, BeltPresenter | Presentation/ | MB |
| FxDirector, FxParticlePool, BannerQueue | Effects/ | MB |
| SoundPlayer | Audio/ | MB |
| UIRoot, FightHUD, ResultScreen, экраны меню | UI/ | MB |
| GameSetup, ShotsTaker, GameWalk | Editor/ | Editor |

### Поток данных в кадре
```
InputReader → FightDirector.OnPunch()
  → AnatView.Pick(ray) → AnatSkin.Push → AnatView.Hit()
  → FightSim.ProcessHit(removed, zone, v.sim.*) → HitResult
  → FxDirector.OnHit(result), SoundPlayer.Play()
  → FightHUD.Refresh(state)
каждый кадр:
  AnatSkin.Step(dt_unscaled) → v.Tick(sdt) → AnatSkin.SetGlobals(v.root)
  sdt = dt * (freeze>0 ? 0 : slow>0 ? 0.28f : cine>0 ? 0.22f : 1f)
  таймеры эффектов/камеры — Time.unscaledDeltaTime
```

### UI-стек
uGUI, Canvas режим Screen Space Camera (рисуется в `Camera.Render()→RenderTexture`).
Reference Resolution: 540×1170, Match=0.5. SafeAreaFitter через `Screen.safeArea`.
Текст: `UnityEngine.UI.Text` со шрифтом Inter (SIL OFL 1.1, `Assets/Game/Resources/Fonts/`, кириллица есть);
TMP не используем — ему нужен импорт Essential Resources, в пакетном режиме это лишний риск.
Снимки: отдельная `ShotCamera` → `RenderTexture(W,H,24)` → `Render()` → PNG.
Портрет 540×1170, ландшафт 1170×540 (перед ландшафтным снимком: обновить `CanvasScaler.referenceResolution` и вызвать `SafeAreaFitter.Refresh()`).

---

## Вехи

### M1 — Удар в ринге
Агенты параллельно (файлы не пересекаются):
- **A1**: FightState.cs, FightSim.cs, PunchInput.cs, HitResult.cs
- **A2**: FighterDef.cs, FighterGen.cs, RosterDef.cs
- **A3**: PlayerProfile.cs (заглушка), GlovesCatalog.cs
- **A4**: FightDirector.cs, InputReader.cs
- **A5**: FoeBody.cs, PlayerGloves.cs, RingBuilder.cs
- **A6**: GameSetup.cs (сцена Game.unity + EditorBuildSettings + manifest.json + ugui)

Проверяем: тап → Pick/Hit → HP убывает → нокдаун (kd<2) → KO (kd≥2 или HP≤0).
Тесты: TakeDamage снижает hp; KD и KO — раздельные ветки; FighterGen детерминирован; GlovesCatalog ≥6 записей.

### M2 — HUD + снимки
- **A4**: UIRoot.cs, FightHUD.cs, ResultScreen.cs
- **A6**: ShotsTaker.cs (540×1170, 1170×540)

Проверяем: HUD показывает HP/стамину/мега; снимки без исключений в batchmode.
Тесты: ShotsTaker создаёт оба PNG; CanvasScaler обновляется перед ландшафтным снимком.

### M3 — 8 соперников + AI
- **A1**: FightAI.cs (8 стилей: Кирпич/Шкаф/Молот/Бетон/Утюг/Гранит/Бульдозер/Тихий)
- **A5**: AccessoryBuilder.cs (повязки/ушники поверх AnatView.root), FoeBody — 4 варианта масштаба
- **A4**: RosterScreen.cs

Различимость (голова неизменна, _AnatPal — глобал):
тело/шея/плечи цвет кожи → VoxBody+VoxProbe/Voxel; трусы цвет; аксессуар над головой; масштаб 0.90–1.10; имя+стиль.
Тесты: 8 seeds → 8 уникальных наборов тела/цвета; LadderProgression; AIStyle параметры.

### M4 — Рефери + развязка
- **A5**: RefereeView.cs (VoxBody без MB; 4 позы через Transform-иерархию), CrowdView.cs (300 InstancedMesh, волна через Matrix4x4[] каждый кадр)
- **A4**: BeltPresenter.cs

Проверяем: рефери считает 1–10 по WaitForSecondsRealtime(0.8f); толпа аплодирует.
Тесты: RefereeView не MonoBehaviour; CrowdView ≤300 инстансов; BeltPresenter появляется после KO.

### M5 — Эффекты + звук
- **A7**: FxDirector.cs, FxParticlePool.cs (InstancedMesh кольцевой буфер ≤200), BannerQueue.cs (пул 5 надписей)
- **A8**: SoundPlayer.cs (пул 20 AudioSource, priority-вытеснение), AudioSetup.cs (WAV из assets/sounds/src/ → Unity/Assets/Game/Audio/)

Лимиты частиц: джеб 0, пауэр 20, мега 40, разрыв 220 (кольцевой буфер вытесняет старейшие).
Антиспам STOPQ кулдаун 0.8 с. full=true обходит STOPQ.
Тесты: AudioSource пул вытесняет 21-й голос; STOPQ снижает freeze при частых ударах; ParticlePool ≤200.

### M6 — Меню + экономика + сохранение
- **A1**: EconomyState.cs, SaveService.cs (JSON → persistentDataPath/save.json v1), ModeManager.cs
- **A3**: PlayerProfile.cs (complete), applyUpgrade
- **A4**: MainMenuScreen, ModeSelectScreen, FighterSelectScreen, ShopScreen, PlayerScreen, SettingsScreen, PauseScreen

Проверяем: полный поток меню→бой→результат→магазин; Save/Load; P.megaMeter не сбрасывается между боями.
Тесты: BuyUpgrade уменьшает coins; превышение max level 3 → исключение; Save→Load идентично.

---

## Пределы накопления (игровой слой)
- Частицы FxParticlePool: ≤200 (InstancedMesh, кольцевой буфер)
- Баннеры BannerQueue: ≤5 (пул вытесняет старейший)
- AudioSource: ≤20 (priority-вытеснение)
- Зрители CrowdView: ≤300 (InstancedMesh, Matrix4x4[], не меняется)
- Аксессуары: 1 на соперника (меняется между боями)
Пределы анатомии не трогать: раны ≤44, подтёки ≤70, куски ≤24.

---

## Риски

| Риск | Снятие |
|---|---|
| _AnatPal глобал → головы идентичны по тону | Только тело+аксессуары; запрос к Probe-сессии |
| AnatomyBuilder.Build() 300–500 мс (ПК), 1–3 с (телефон) | Строить в загрузочном экране (пул 2 AnatView); никогда не в бою |
| Tick вне удара до 22 мс (открытая задача Probe) | FightDirector не вызывает v.Hit() дважды за кадр; тяжёлый Update в FoeBody — последний |
| Canvas в пакетном снимке | Всё представление тикается явным `Tick(dt)` без корутин; запасной вариант — World Space Canvas перед камерой |
| Matrix4x4[] толпы каждый кадр | CrowdView выделяет массив один раз, обновляет in-place; замер на M4 |

---

## Точки расширения анатомии (запрос к Probe-сессии)
1. `toneIndex` в AnatomyBuilder.Build() — цвет кожи/этнос
2. `hairStyle` / `stubbleDensity` как параметры Build()
3. Возврат HitInfo из AnatView.Hit() (сейчас только int removed)
4. Асинхронный AnatomyBuilder.Build() (сейчас блокирует главный поток)
5. Callback/событие при detach вместо поллинга v.detachedBodies
