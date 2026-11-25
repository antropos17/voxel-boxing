# Unity-проба: одна разрушаемая голова

Шаг 2 из `docs/ADR-001-platform.md`: держит ли Unity 6 + Burst + сетки с объединением граней нужную скорость. Только проба: боя, соперника целиком, интерфейса, звука нет. Итоги — в `PROBE-REPORT.md`.

Unity `6000.3.21f1`, URP 17.3, пакеты: Burst, Collections, Mathematics, Test Framework.

## Что внутри

| Файл | Что делает |
|---|---|
| `Assets/Probe/Runtime/HeadBuilder.cs` | Голова из SDF веб-версии (`headSDF`), слои по глубине (фаска 3-4-5, как `chamfer`). Решётка 57×81×71, воксель 3,3 мм. Три Burst-джоба. |
| `Assets/Probe/Runtime/VoxMat.cs` | Палитра: 1 байт на воксель = индекс материала, 0 — пусто. |
| `Assets/Probe/Runtime/GreedyMesher.cs` | Binary greedy meshing: столбцы занятости — 64-битные маски с полем из соседних чанков, грани — сдвиг и AND-NOT, объединение по битовым строкам среза. Вершина 8 байт (half4). Джоб пишет прямо в `Mesh.MeshData`. |
| `Assets/Probe/Runtime/VoxBody.cs` | Тело из чанков 32³ и планировщик перестройки: грязные чанки уходят партией джобов, результат забирается, когда готов. |
| `Assets/Probe/Runtime/VoxOps.cs` | Удар (шар + раскол на осколки по Вороному), поиск оторванных кусков в окрестности удара, перенос куска в свою решётку, луч по решётке. |
| `Assets/Probe/Runtime/Blood.cs` | Капли: до 3000, обновление в Burst, `Graphics.RenderMeshInstanced`. |
| `Assets/Probe/Runtime/ProbeRunner.cs` | Сцена, сценарий замера на 60 с, итог на экране, в JSON и в логе. |
| `Assets/Probe/Shaders/*.shader` | Свет в вершинах от одной лампы, цвет из палитры. |
| `Assets/Probe/Editor/ProbeSetup.cs` | Пакетная настройка проекта (URP, материалы, сцена, игрок) и сборки. |
| `Assets/Probe/Tests/ProbeTests.cs` | EditMode-тесты. |

## Команды (Git Bash)

```bash
U="X:/Programs/Unity/6000.3.21f1/Editor/Unity.exe"; P="X:/Projects 2027/Voxel Boxing - Fight 2027/Unity"
# настройка (URP, материалы, сцена) — один раз после клонирования
"$U" -batchmode -quit -projectPath "$P" -executeMethod VoxProbe.Editor.ProbeSetup.Setup -logFile setup.log
# EditMode-тесты, отчёт в tests.xml
"$U" -batchmode -projectPath "$P" -runTests -testPlatform EditMode -testResults tests.xml -logFile tests.log
# сборка Windows (IL2CPP, release) -> Unity/Builds/Win/VoxelProbe.exe
"$U" -batchmode -quit -projectPath "$P" -buildTarget Win64 -executeMethod VoxProbe.Editor.ProbeSetup.BuildWindows -logFile win.log
# сборка Android (IL2CPP, ARM64, release, APK) -> Unity/Builds/Android/VoxelProbe.apk
"$U" -batchmode -quit -projectPath "$P" -buildTarget Android -executeMethod VoxProbe.Editor.ProbeSetup.BuildAndroid -logFile android.log
```

Пакетный запуск не пройдёт, если этот проект открыт в редакторе.

## Два способа работы: пакетный запуск и открытый редактор через Unity CLI

С 23.11.2026 в проекте стоит пакет `com.unity.pipeline` (`Packages/manifest.json`). Через него Unity CLI (`unity`, версия 1.0.1) управляет открытым редактором этого проекта.

**Когда какой:**

- **Открытый редактор + CLI** — для частых шагов: тесты, снимки, замеры в редакторе, `script eval` своих методов. Нет холодного старта.
- **Пакетный запуск** — сборки Windows и Android, чистые замеры времени, CI. Перед ним свой редактор закрыть (`unity close`), иначе Unity не пустит второй процесс на проект.
- Правка C# → перекомпиляция → снимок одинаково долгие обоими способами (~22 с): основное время — сама перекомпиляция и перезагрузка домена.

Замер 23.11.2026 (три повтора каждого шага, машина загружена другими сессиями; разброс между повторами до 10%):

| Шаг | Пакетный, с | CLI, открытый редактор, с | Во сколько раз |
|---|---|---|---|
| правка цвета в `AnatMat.cs` → перекомпиляция → снимок головы 3/4 (`AnatShotsA2.One`) | 21,4 (22,7 / 20,4 / 21,2) | 22,0 (21,7 / 22,0 / 22,4) | 1,0 |
| то же без правки кода (только снимок) | ~21 | 5,6 | ~3,8 |
| EditMode-тесты строения и видимости (13 прошли) | 39,1 (40,8 / 38,0 / 38,6) | 11,4 (12,4 / 10,9 / 10,9) | 3,4 |
| полный набор снимков A2 (`AnatShotsA2.Run`) | 26,9 (28,4 / 24,6 / 27,7) | 10,0 (10,2 / 10,5 / 9,3) | 2,7 |

Первое открытие редактора (сразу после установки пакета, импорт пакетов): 149 с до `ready`; повторное — 35 с. Память: редактор 2,1–2,8 ГБ плюс 2–5 процессов импорта ассетов по ~0,85 ГБ, пока они живы.

Результат одинаковый: снимок `One` и все 18 снимков A2 совпали попиксельно, треугольники во всех состояниях те же, тесты — 13 прошли, 11 этапа B пропущены.

### Команды (Git Bash)

```bash
export UNITY_NO_CONSENT_PROMPT=1 UNITY_NON_INTERACTIVE=1 UNITY_NO_AGENT_HINTS=1
P="X:/Projects 2027/Voxel Boxing - Fight 2027/Unity"
# открыть свой редактор и дождаться готовности (всегда с полным путём: на машине бывает открыт чужой проект)
unity open "$P"
unity status --until-ready --project-path "$P" --format json
# перекомпиляция после правки C#; затем ОБЯЗАТЕЛЬНО дождаться готовности — recompile возвращается до конца перезагрузки домена
unity recompile --project-path "$P" --timeout 300 --format json
unity status --until-ready --project-path "$P" --format json
# свой метод в живом редакторе
unity command script eval --code 'VoxProbe.Editor.AnatShotsA2.Run(); return "ok";' --timeout 900 --project-path "$P" --caller plugin --skill unity-cli --format json
# EditMode-тесты: фильтр — подстрока имени (не регулярка); результат по каждому тесту в JSON
J=$(unity command test run --filter "VoxProbe.Tests.Anatomy" --detach --project-path "$P" --caller plugin --skill unity-cli --format json | python -c "import json,sys;print(json.load(sys.stdin)['data']['jobId'])")
unity job wait "$J" --timeout 900 --project-path "$P" --format json > Logs/tests-cli.json
# закрыть свой редактор перед пакетным запуском или сборкой
unity close "$P"
```

Пакетные команды — выше, в разделе «Команды»; снимки и тесты анатомии пакетно:

```bash
"$U" -batchmode -quit -projectPath "$P" -executeMethod VoxProbe.Editor.AnatShotsA2.Run -logFile Logs/a2.log
"$U" -batchmode -projectPath "$P" -runTests -testPlatform EditMode -testFilter "VoxProbe.Tests.AnatomyTests|VoxProbe.Tests.AnatomyVisTests" -testResults Logs/tests.xml -logFile Logs/tests.log
```

### Причуды

- Сразу после `unity recompile` следующий вызов может получить код 12 (`EDITOR_UNREACHABLE`): идёт перезагрузка домена. Без `status --until-ready` между ними `eval` либо падает, либо может отработать на старом коде.
- Фильтр `test run` в редакторе — подстрока, `A|B` не работает (в пакетном `-testFilter` работает).
- Первый открытый редактор 23.11.2026 закрылся сам через ~2 мин после готовности (в журнале штатный выход: сохранение раскладки, «Pipeline Server stopped», без ошибок); причина не найдена. Второй открытый работал стабильно весь замер.
- Тесты этапа B (`[Ignore]`) фильтр `VoxProbe.Tests.Anatomy` тоже захватывает — они пропускаются.

## Игровой слой: как запустить игру

Код — `Assets/Game/` (сборки `VoxGame`, `VoxGame.Editor`, `VoxGame.Tests`), анатомическую голову берёт из `Assets/Probe/` без правок. Сцена — `Assets/Game/Game.unity`: открыть и нажать Play (меню → режим → лестница → бой → награда → магазин). Единственный `Update` — у `GameApp`, остальное тикает из него.

Пункты меню `VoxGame` в редакторе:

- `Setup Game Scene` — пересоздать сцену, добавить её первой в Build Settings, создать материалы `Resources/Shaders/*.mat` (держат шейдеры в сборке плеера; отдельно — `Ensure Shader Materials`);
- `Game Walk W4` — сквозной проход без Play Mode: 36 шагов через `GameApp.Press` и синтетические удары, снимки всех экранов; итоговая строка `GAME_WALK ok …`;
- `Fight Shots W1` — снимки боя и замер кадра удара (`GAME_HIT_FRAME_MS`, `GAME_HIT_BREAKDOWN`, `GAME_LAYER_PARTS`);
- `Referee Shots W2`, `Roster Sheet W2`, `Roster Screen Shots W2`, `Fx Shots W3`, `Gloves Sheet W3`, `Portraits Sheet W5` — снимки по темам.

Снимки — `Shots/game/` (портрет 540×1170 `-p`, ландшафт 1170×540 `-l`). Сохранение прохода и замеров — во временной папке, не в профиле игрока.

```bash
U="X:/Programs/Unity/6000.3.21f1/Editor/Unity.exe"
P="X:/Projects 2027/Voxel Boxing - Fight 2027/Unity"
# в открытом редакторе: проход, лист портретов, пределы, утечки, распределения памяти
unity command script eval --timeout 590 --code 'VoxGame.Editor.GameWalk.Walk(); return VoxGame.Editor.GameWalk.LastLog;' --project-path "$P" --caller plugin --skill unity-cli --format json
unity command script eval --timeout 300 --code 'VoxGame.Editor.ShotsTaker.PortraitsSheet(); return VoxGame.Editor.ShotsTaker.LastLog;' --project-path "$P" --caller plugin --skill unity-cli --format json
unity command script eval --timeout 590 --code 'VoxGame.Editor.ShotsTaker.LimitsProbe(); return VoxGame.Editor.ShotsTaker.LastLog;' --project-path "$P" --caller plugin --skill unity-cli --format json
unity command script eval --timeout 590 --code 'VoxGame.Editor.ShotsTaker.LeakProbe(); return VoxGame.Editor.ShotsTaker.LastLog;' --project-path "$P" --caller plugin --skill unity-cli --format json
unity command script eval --timeout 590 --code 'VoxGame.Editor.ShotsTaker.AllocProbe(); return VoxGame.Editor.ShotsTaker.LastLog;' --project-path "$P" --caller plugin --skill unity-cli --format json
# тесты игрового слоя и анатомии пакетно (редактор закрыт)
"$U" -batchmode -nographics -projectPath "$P" -runTests -testPlatform EditMode -testFilter "VoxGame.Tests" -testResults "$P/Logs/game-tests.xml" -logFile "$P/Logs/game-tests.log"
"$U" -batchmode -nographics -projectPath "$P" -runTests -testPlatform EditMode -testFilter "VoxProbe.Tests.Anatomy" -testResults "$P/Logs/anat-tests.xml" -logFile "$P/Logs/anat-tests.log"
```

Шрифт интерфейса — Inter (SIL OFL 1.1), файлы и лицензия в `Assets/Game/Resources/Fonts/`; запись в `CREDITS.md` в корне.

## Игра в браузере (Web)

Сборка `Game.unity` под Web (модуль WebGL Build Support для 6000.3.21f1 установлен). Настройки Web ставит сам метод сборки (`Assets/Game/Editor/WebBuild.cs`, только для Web): Brotli + Decompression Fallback, stripping High, `RuntimeSpeedLTO`, Wasm 2023, куча 256/2048 МБ, шаблон `Assets/WebGLTemplates/VoxFull`. Сборка с нуля ~50 мин (компоновка wasm с LTO).

```bash
"$U" -batchmode -quit -projectPath "$P" -buildTarget WebGL -executeMethod VoxGame.Editor.WebBuild.BuildWebGL -logFile Logs/web.log   # Builds/Web/
rm -rf ../web/unity && cp -r Builds/Web ../web/unity     # выкладка: pages.yml копирует web/unity -> https://antropos17.github.io/voxel-boxing/unity/
node ../tests/unity-web.js Unity/Builds/Web               # из корня репозитория: Chrome с окном, меню -> бой -> удары, цифры в Unity/Shots/web/result.json
node ../tests/unity-web.js Unity/Builds/Web --mobile --mode training   # эмуляция телефона (касания, ANAT_LITE)
```

Скрипт ищет Playwright в `node_modules` репозитория (в worktree — `NODE_PATH=<основная папка>/node_modules`), браузер — установленный Chrome (`channel: 'chrome'`). Цифры замера и ограничения — `docs/STATUS.md`, «Unity: игра в браузере».

## Просмотр головы

Интерактивный режим той же сборки: `-anatView`. Без флага сборка, как раньше, делает замерный облёт.

```bash
# из корня репозитория; окно 540×1170, размер можно менять мышью
start "" "Unity/Builds/AnatWin/VoxelAnat.exe" -anatView
# самопроверка: повороты, разрезы, три удара, кадр окна после каждого шага -> Unity/Shots/anatomy-view/
"Unity/Builds/AnatWin/VoxelAnat.exe" -anatViewSelfTest -anatShots "$(pwd -W)/Unity/Shots/anatomy-view"
```

| Клавиша | Что делает |
|---|---|
| ЛКМ, тянуть | вращать вокруг головы (через полюс не переворачивается) |
| колесо | ближе / дальше: от глаза крупно до всей головы |
| СКМ или Shift + ЛКМ, тянуть | сдвинуть точку взгляда |
| F или двойной клик | вид по умолчанию |
| 1 / 2 / 3 / 4 | разрез: сагиттальный (0,6 см от средней линии), фронтальный через глазницы, горизонтальный через мозг, через челюсти и зубы; камера поворачивается к срезу |
| 0 | целая голова (удары остаются) |
| [ и ] | сдвинуть плоскость текущего разреза на воксель (0,2 см) |
| ПКМ или Ctrl + ЛКМ | удар по лучу из камеры в точку под курсором |
| Q / W / E | сила удара: лёгкий (r 0,6 см, глубина 1 см, останавливается на кости и зубах), средний (1 / 2,2 см, на мозге), тяжёлый (1,6 / 4 см, насквозь) |
| R | восстановить голову (удары, разрез и сдвиги сброшены) |
| H | спрятать подсказку |
| Esc | выход |

Подсказка в левом верхнем углу: кадры в секунду, треугольники, чанки (всего и с сеткой), что перестраивалось последним и за сколько мс.

Как устроено: `Runtime/Anatomy/AnatView.cs` — ядро без ввода (разрез — настоящее снятие половины ячеек во всех телах и полный пересчёт видимости и сеток; удар — капсула вдоль луча, как `Bore` в `AnatShotsA2`, видимость по месту через `AnatVis.Opened`, перестраиваются только помеченные чанки). `AnatViewer.cs` — камера, ввод (Input System), подсказка (IMGUI, строка пересобирается 4 раза в секунду), самопроверка. Запускается из `AnatRunner` по флагу. Оторванные куски не падают: их поиска в анатомической голове пока нет, удар только убирает ячейки. Синяков, отёка и крови в просмотре нет (этап B).

### Сборка анатомической головы и автозамер (этап B)

```bash
# закрыть свой редактор (unity close "$P"), затем:
"$U" -batchmode -quit -projectPath "$P" -buildTarget Win64 -executeMethod VoxProbe.Editor.AnatSetup.BuildWindowsKeepScene -logFile Logs/win.log      # Builds/AnatWin/VoxelAnat.exe
"$U" -batchmode -quit -projectPath "$P" -buildTarget Android -executeMethod VoxProbe.Editor.AnatSetup.BuildAndroidKeepScene -logFile Logs/android.log # Builds/AnatAndroid/VoxelAnat.apk
# автозамер разрушения до культи (seed 77), результат в JSON
Unity/Builds/AnatWin/VoxelAnat.exe -anatBench -screen-width 540 -screen-height 1170 -screen-fullscreen 0 -anatBenchOut "$(pwd -W)/Unity/ProbeResults/b3/bench-win-1.json"
```

`…KeepScene` не вызывают `Setup()` и не пересохраняют `Anatomy.unity` (старые `BuildWindows` / `BuildAndroid` вызывают). Аргументы автозамера: `-anatBenchOut файл`, `-anatBenchSeed N`, `-anatBenchNoQuit`; без `-anatBenchOut` файл идёт в `persistentDataPath`. На телефоне сборка сразу открывает просмотр: палец — вращать, тап — удар, щипок — масштаб, два пальца — сдвиг, тап двумя — сила, тап тремя — восстановить; первая строка подсказки — кадры/с, худший кадр, время удара. Результаты и памятка: `ANATOMY-B.md`, «Часть 3, сборка и замер».

## Замер

Windows, кадр телефона 1080×2340 (рисуется в текстуру этого размера, окно 480×1040 показывает её уменьшенной):

```bash
Unity/Builds/Win/VoxelProbe.exe -probeAuto -probeQuit -probeRes 1080x2340 -screen-width 480 -screen-height 1040 -screen-fullscreen 0 -logFile win-run.log
# то же с одним рабочим потоком джобов
Unity/Builds/Win/VoxelProbe.exe -probeAuto -probeQuit -probeWorkers 1 -probeRes 1080x2340 -screen-width 480 -screen-height 1040 -screen-fullscreen 0 -logFile win-run1.log
```

Аргументы: `-probeAuto` — замер сразу, `-probeQuit` — выйти после него, `-probeWorkers N` — рабочих потоков джобов, `-probeRes WxH` — размер кадра, `-probeSeconds S` — длина прогона, `-probeNoHud` — без надписей.

Сценарий (60 с): удар каждые 0,35 с — лёгкий (шар 1 см), каждый пятый тяжёлый (воронка 1,2 см и раскол на осколки до 2,6 см); на 24-й и 50-й секунде «разрыв головы» (выбивает шар 7 см, поиск кусков по всей решётке); на 34-й секунде голова восстанавливается и перестраивается целиком. Удары идут лучом из камеры, как касание.

Результат: файл `probe-<дата>-w<потоки>.json` в `Application.persistentDataPath` (Windows: `%USERPROFILE%\AppData\LocalLow\VoxelBox\VoxelProbe\`, Android: `/sdcard/Android/data/com.voxelbox.probe/files/`), строки `PROBE_FILE` и `PROBE_JSON` в логе, итог на экране.

На Android замер стартует сам при запуске. После итога касание переводит в свободный режим: касание — тяжёлый удар, три пальца — восстановить голову. На ПК: клик — удар, B — разрыв, R — восстановить, M — новый замер.

## Телефон

1. Включить на телефоне «Для разработчиков» → «Отладка по USB», подключить к ПК, разрешить отладку.
2. `"X:/Programs/Unity/6000.3.21f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe" install -r Unity/Builds/Android/VoxelProbe.apk` (или скопировать APK на телефон и открыть его, разрешив установку из неизвестных источников).
3. Открыть VoxelProbe, держать телефон 70 с не трогая. Сфотографировать итог на экране; JSON забрать командой `adb pull /sdcard/Android/data/com.voxelbox.probe/files/ .`
