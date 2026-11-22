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
