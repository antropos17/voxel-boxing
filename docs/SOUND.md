# Звук

Звуки боя сделаны программой [earshot](https://github.com/Leonxlnx/earshot) 1.0.0 и играются из файлов через WebAudio. Старый синтез из `src/e_fx.js` остался запасным вариантом: он играет, когда файлы не загрузились (file://, нет сети, ошибка декодирования). Происхождение и условия использования описаны в `assets/sounds/CREDITS.md`.

**Никто этот набор не слушал.** Всё ниже проверено только замерами: длительность, пик, LUFS, спектр, атака, аудит earshot. Как он звучит, нужно проверить на слух (список в конце).

## earshot: что это и как он устроен

- Python-пакет (MIT, Leon Lin). Исходники в `X:\Claude\tools\earshot-src` (коммит 7a7f7c5), установка в venv `X:\Claude\tools\earshot-venv` (Python 3.10). Ставился без дополнения `listen`, без MCP и без ключей: `pip install -e X:\Claude\tools\earshot-src` с `PIP_CACHE_DIR=X:\Claude\tools\pip-cache`. Зависимости: numpy, scipy, soundfile, soxr, numba.
- Сеть. Перед установкой прочитан код. Наружу ходят только необязательные части: `llm.py` (Gemini, нужен ключ и флаг `--yes-upload`), скачивание моделей для `listen` и `doctor --fetch`. `make` и `render` моделей не грузят и в сеть не ходят. `make.py` на всякий случай ставит `HF_HUB_OFFLINE=1`. Исполняемых файлов earshot не скачивает: `subprocess` вызывает только ffmpeg и ffprobe.
- Каталог. 922 звука (`earshot sounds`), имена вида `тип/вариант`: `hit/punch`, `punch/body`, `whoosh/heavy`, `crowd/cheer`… Звуки бывают трёх видов:
  - выученные (`learned`, `textures`, `drums`, `notes`) — модель из нескольких килобайт чисел, снятых с одной записи CC0; earshot синтезирует по ней новый дубль;
  - банк (`bank`) — рецепты, подогнанные к записям CC0;
  - процедурные (`action`, `motion`, `figure`, `ui`, `object`) — чистый код.
- `earshot make "слова" -o файл --n N --seed S [--sound ID]` подбирает звук по английскому описанию (или берёт указанный `--sound`), рендерит N вариантов и пишет wav или flac. Для ogg, mp3 и webm нужен ffmpeg. `earshot build` собирает набор по описанию, но для подстройки ему нужен `listen` (модели), поэтому здесь не использовался.
- Из Python: `earshot.sounds.render(id, seed=…, **оси)` возвращает стерео 48 кГц и `meta`. У выученных `meta.member` — номер исходной записи (n из of). Зерно выбирает запись и дубль, одно и то же зерно всегда даёт те же отсчёты, на этом держится воспроизводимость набора. Оси (`axes`) у каждого звука свои: длина, высота, яркость и т. п.
- Уровень: `earshot.synth.master.finish(y, peak_db, lufs, loop, lift, tonal, fade_in)`. Громкость считается по momentary-max LUFS, истинный пик ограничен −1 дБFS. Короткие пики не режутся лимитером, а мягко подгибаются (`_fold`), поэтому атака удара остаётся яркой. Громкость мерит `master.loudness`.
- Аудит: `earshot.forge.sfx.check(y, sr)` находит щелчки, перегруз, обрезанный хвост (`truncated`), звон (`ring`), восходящий глайд и ненулевые края (`edge`).

Сильные стороны: много выученных бытовых звуков с честным происхождением (CC0), детерминизм по зерну, хорошая функция мастеринга, аудит в комплекте, работа офлайн, скорость (весь набор собирается за ~2 мин в 6 процессах).

Слабые стороны и чего он не умеет:
- Нет речи, криков, кряхтения и стонов: боксёр не может ухнуть или застонать. Дыхание и кашель есть (`breath/pant`, `cough/person`), но это бытовые записи.
- Нет настоящего боксёрского гонга и шума арены. Гонг собран из `bell/desk` и `elevator/ding` с понижением, зал — из `cafe/busy` (ресторан) через фильтр. Для толпы на нокдауне есть `crowd/cheer` и `applause/audience`, но они про зрителей в зале, не про стадион.
- Готового удара по лицу почти нет: `punch/body` и `hit/punch` либо слишком «киношные», либо одни низы. Удары пришлось собирать из 3–6 слоёв: шлепок, тело, хлопок багажника, мешок, хруст яблока.
- Выученные дубли разные по длине и громкости, хвосты часто длинные. Поэтому `make.py` режет каждый слой по пику, приводит его к −20 LUFS и только потом сводит.
- Тяжёлые удары earshot почти целиком в низах (центр спектра ~50 Гц). В динамике телефона их не слышно, они кажутся тише джеба. Отсюда «телефонная» цель (ниже).

## Как устроен конвейер

Набор описан в `tools/sounds/spec.py` (`EVENTS`), собирается `tools/sounds/make.py`, замеры в `tools/sounds/measure.py`.

Пересобрать всё (≈2 мин, удаляет старые файлы):

```
X:/Claude/tools/earshot-venv/Scripts/python -I tools/sounds/make.py --jobs 6
```

Только часть событий (манифест дописывается): `… make.py jab_head bell`. ffmpeg по умолчанию берётся из HyperFrames (`%LOCALAPPDATA%\Programs\HyperFrames\…\ffmpeg.exe`), другой путь задаётся через `--ffmpeg` или `FFMPEG`. После пересборки нужен `npm run build`.

Событие состоит из слоёв `L(id, gain, at, crop, pitch, lp, hp, seed, axes)`. Для каждого из n+2 кандидатов (зерно слоя + номер кандидата) `make.py` делает следующее:

1. Рендерит слой и обрезает его (по пику или от заданного места). Меняет высоту через soxr (как плёнка). Фильтрует: у петли фильтр идёт по кругу, чтобы шов остался целым. Приводит слой к −20 LUFS + gain.
2. Сводит слои в моно и убирает тишину в начале (−36 дБ). Ограничивает длину `max`, отрезает хвост ниже −60 дБ от пика.
3. Если у события задан `phone`, параллельно добавляет его же верх (выше 300 Гц), пока громкость выше 300 Гц не дойдёт до цели.
4. Выравнивает уровень: `master.finish(lufs=цель, peak −1 дБ)`.
5. Мерит кандидата (`measure.py`: длительность, пик, LUFS, «телефон», атака, центр спектра, доля низов и верхов, аудит).
6. Из кандидатов берёт n лучших. Оценка: 10 × число предупреждений + штраф за атаку длиннее 10 мс + отклонение от целевой громкости. У петли вместо краёв проверяется шов.

Выход:

- `assets/sounds/src/<событие>_<k>.wav` — 16 бит, моно, 48 кГц, без потерь (исходники, в том числе для Unity), 12 МБ;
- `assets/sounds/web/<событие>_<k>.webm` (Opus 48 кбит/с, у петли 40) и `.mp3` (80 кбит/с, у петли 64);
- `assets/sounds/manifest.json` — событие → файлы, `gain`, `pv` (разброс высоты, полутоны), `vv` (разброс громкости), `pri` (приоритет), `voices` (предел голосов), `loop`, плюс описание, запрос и слои;
- `assets/sounds/report.json` — замеры каждого файла.

`build.js` копирует в `dist/sounds/` нужные по манифесту webm и mp3 и пишет туда облегчённый манифест. Если файла нет, сборка падает. `dist/sounds/` в git не идёт.

Размер: webm 0,81 МБ, mp3 1,25 МБ, вместе 2,06 МБ. Клиент качает один формат: webm, если браузер играет Opus, иначе mp3 (Safari). Это 0,8–1,25 МБ.

### Громкость и «телефон»

Колонка «LUFS» — momentary-max по всему спектру. «Телефон» — то же после фильтра 300 Гц 4-го порядка, грубая модель динамика телефона.

Цели:

- удары по нарастанию веса: джеб −15/−16, хук −13/−13,5, апперкот −12,5/−13, оверхенд −11,5/−12, мега −10; на телефоне −16…−12;
- по игроку −12, касание −17;
- хруст костей −12…−14;
- ткани −15…−19, капли −28;
- интерфейс −16…−21;
- фон зала −30.

Атака у всех ударов и хруста ≤ 10 мс (от −30 дБ до пика).

## Как играет игра (`src/e_fx.js`)

- `sndLoad()` запускается через 300 мс после `load`. Под file:// или без `fetch` ничего не делает, тогда работает синтез.
  - Загружает манифест, затем файлы, по 4 запроса параллельно.
  - Кнопка «В бой» загрузки не ждёт.
  - До первого касания (`audioInit`) файлы лежат сырыми. После касания они декодируются асинхронно (`decodeAudioData`) и кадр не занимают.
  - Если webm не декодировался, берётся mp3.
- `snd(событие, громкость, скорость, задержка)` выбирает случайный вариант, не повторяя последний, с разбросом высоты и громкости по манифесту.
  - У каждого события свой предел `voices`: при переполнении глушится самый старый голос этого события.
  - Общий предел `SNDMAX` = 20: глушится голос с меньшим приоритетом, а если все старше, новый звук не играет.
  - Заглушённый голос гаснет за 60 мс, без щелчка.
  - Если файла нет, функция возвращает `false`, и `sfx.*` играет старый синтез.
- Громкость следует за силой удара. `sfx.heavy(w, s, head, type)` даёт громкость `.72 + .32·(s−1)` (от 0,6 до 1,15) и чуть понижает тон у сильных ударов. Удар по игроку слабее 60 % от полного играет как `graze`. Капли — не чаще раза в 70 мс, громкость зависит от размера.
- Фон зала (`hall`, петля 20 с) плавно включается, пока идёт бой, нокдаун или поражение, и выключается в меню.
- iOS: контекст будится на `pointerdown`, `touchend` и `keydown` (фаза capture) и при возврате на вкладку (`visibilitychange`).
- Для тестов есть `window.__g.snd` (только в `dist/test.html`).

### События и откат на синтез

Имена и сигнатуры `sfx.*` прежние. Новые функции: `guardBreak`, `crack(зона)`, `toothFloor`, `flay`, `jawOff`, `eye`, `burst`, `drip`, `breath`, `cough`, `crowd(kd|ko|boo)`, `ui`, `combo(n)`, `win`, `lose`, `star`, `coin`.

Без файлов:

- старые функции (jab, heavy, block, whoosh, miss, dodge, swing, bell, hurt, step, tick, count, tooth, thud, drop, clack, squelch, gut, tear, ready, charge, mega) играют прежний синтез;
- новые `crack`, `flay`, `jawOff`, `eye`, `burst` и `guardBreak` откатываются на подходящий старый синтез (блок, хруст, разрыв, хлюп);
- у `toothFloor`, `drip`, `breath`, `cough`, `crowd`, `ui`, `combo`, `win`, `lose`, `star` и `coin` синтеза не было, они молчат. Раньше здесь тоже было тихо.

Новые точки вызова: промах (`miss`), уклон (`dodge`), пробитый блок, хруст по зоне перелома, выбитый и сколотый зуб, зуб на полу, кровь на полу, срезанная кожа, оторванная челюсть, взрыв головы, толпа на нокдауне и нокауте, свист при поражении, вдох после подъёма, кашель на счёте 2, кнопки меню, комбо от 3 (на нечётных), победа и поражение. `star` и `coin` есть в наборе, но игра их пока не вызывает: окно итогов (`showEnd`) по условию задачи не трогалось.

## Набор

58 событий, 163 файла.

Остались предупреждения аудита:

- `over_head_1`, `crack_arm_1` — `truncated`: хвост обрезан ограничением длины;
- `charge_1`, `charge_2` — `truncated`, нарочно: это замах, обрезанный перед ударом;
- `tooth_floor_2…4` — `ring`: звон кубика по полу, так и задумано.

**удары**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `jab_head` | джеб в голову: лёгкий хлёсткий шлепок перчатки | «a sharp slap of a hand on skin; a punch landing on a body» → hit/slap + punch/body + thump/book | 4 | 0.29–0.35 | -15…-15 | -17…-16 | 3/3 |
| `jab_body` | джеб в корпус: глухой шлепок | «a punch landing on a body» → punch/body + hit/slap + thump/book | 4 | 0.36–0.38 | -16…-16 | -19…-17 | 3/3 |
| `hook_head` | хук в голову: тяжёлый удар с хлопком кожи | «a punch landing on a body, a fast heavy thwack» → punch/body + hit/slap + hit/punch + car_trunk/slam + apple/bite | 4 | 0.35–0.50 | -13…-13 | -15…-14 | 4/3 |
| `hook_body` | хук в корпус: глухой тяжёлый удар по рёбрам | «a punch landing on a body» → punch/body + hit/punch + bag/drop + hit/slap | 4 | 0.32–0.45 | -14…-14 | -16…-16 | 4/3 |
| `upper_head` | апперкот в челюсть: удар снизу с щелчком зубов | «a heavy powerful punch» → hit/punch + punch/body + hit/slap + kick/punch + car_trunk/slam + slapstick/crack | 4 | 0.47–0.49 | -13…-12 | -15…-14 | 4/3 |
| `upper_body` | апперкот в корпус | «a punch landing on a body; a short heavy thud» → punch/body + kick/punch + hit/drop + bag/drop | 3 | 0.48–0.49 | -13…-13 | -16…-15 | 4/3 |
| `over_head` | оверхенд в голову: самый тяжёлый обычный удар | «a heavy powerful punch with a sharp crack and a deep boom» → hit/crit + punch/body + car_trunk/slam + hit/slap | 4 | 0.54–0.59 | -12…-12 | -15…-14 | 4/3 |
| `over_body` | оверхенд в корпус | «a heavy powerful punch» → hit/crit + punch/body + bag/drop | 3 | 0.54–0.57 | -12…-12 | -16…-15 | 4/3 |
| `mega` | мега-удар: удар с низким кинематографичным гулом и хрустом | «a heavy powerful punch; a deep cinematic boom impact» → hit/crit + impact/punch + punch/body + hit/slap + car_trunk/slam + bag/drop + break/rock | 3 | 1.35–1.36 | -11…-10 | -16…-14 | 6/2 |
| `flurry` | удар мега-серии: быстрый тяжёлый | «a punch landing on a body, a fast heavy thwack» → hit/punch + punch/body + hit/slap + car_trunk/slam + cut_hit/punch | 4 | 0.35–0.45 | -12…-12 | -15…-15 | 5/3 |

**ткани**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `wet` | мокрый слой поверх удара по окровавленному лицу | «a footstep in thick wet mud; a small splash» → step/mud + splash/small | 4 | 0.35–0.40 | -19…-19 | -19…-19 | 2/3 |

**воздух**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `swing` | свист замаха сильного удара | «a stick swung fast through the air» → whoosh/stick + swipe/hand | 4 | 0.39–0.40 | -20…-20 | -26…-21 | 2/2 |
| `miss` | промах: кулак рассёк воздух | «a hand swept quickly through the air» → sword/swing + swipe/hand | 3 | 0.42–0.44 | -19…-19 | -20…-20 | 2/2 |
| `dodge` | уклон: рывок корпусом, шорох | «a wide breathy whoosh of air» → whoosh/airy + swipe/hand | 3 | 0.49–0.49 | -21…-21 | -22…-21 | 2/1 |
| `whoosh` | удар соперника летит в лицо: тяжёлый свист | «a flat wooden plank swung through the air» → whoosh/stick + whoosh/plank | 3 | 0.35–0.45 | -18…-18 | -23…-21 | 3/2 |

**удары**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `block` | блок: перчатка о перчатку, кожаный хлопок | «a small leather pouch grabbed; a heavy book closed, short and low» → hit/slap + thump/book + leather/grab | 4 | 0.27–0.29 | -16…-16 | -19…-18 | 3/3 |

**по игроку**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `hurt` | пропущенный прямой: удар в лицо изнутри, глухо | «a heavy powerful punch; a deep cinematic boom impact» → hit/crit + punch/body + car_trunk/slam + impact/punch | 3 | 0.59–0.69 | -12…-12 | -16…-14 | 5/2 |
| `hurt_hook` | пропущенный хук: сбоку, со звоном в голове | «a heavy powerful punch with a sharp crack» → hit/crit + punch/body + car_trunk/slam + hit/slap + tick/glass | 3 | 0.61–0.83 | -12…-12 | -15…-14 | 5/2 |
| `graze` | вскользь: кожа чиркнула по лицу | «a sharp slap of a hand on skin» → hit/slap + swipe/hand | 3 | 0.34–0.35 | -17…-17 | -19…-17 | 4/2 |
| `guard_break` | пробитый блок: удар сквозь перчатки | «a book slammed shut; a punch landing on a body» → hit/punch + hit/slap + leather/grab + book/shut | 2 | 0.37–0.50 | -13…-13 | -17…-15 | 5/1 |

**кость**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `crack_nose` | хруст носа: хрящ сминается | «an egg cracked on the rim of a bowl; a crunchy bite into an apple» → egg/crack + apple/bite + hit/slap | 3 | 0.29–0.29 | -14…-14 | -14…-14 | 5/2 |
| `crack_cheek` | хруст скулы: сухой щелчок кости | «a slapstick snapped shut; knuckles cracking» → slapstick/crack + knuckles/crack + punch/body | 3 | 0.35–0.39 | -14…-14 | -15…-14 | 5/2 |
| `crack_jaw` | перелом челюсти: тяжёлый хруст | «a rock cracking and crumbling» → break/rock + knuckles/crack + slapstick/crack | 3 | 0.43–0.43 | -13…-13 | -16…-16 | 5/2 |
| `crack_skull` | череп: низкий раскалывающийся хруст | «a rock cracking and crumbling; a block of ice cracking» → break/rock + ice/crack + hit/crit | 3 | 0.79–0.79 | -12…-12 | -15…-15 | 6/2 |
| `crack_rib` | рёбра: глухой хруст под мясом | «dry twigs snapped by hand; a punch landing on a body» → twigs/snap + punch/body + break/rock | 3 | 0.37–0.44 | -14…-14 | -18…-16 | 5/2 |
| `crack_arm` | перелом руки: щелчок кости | «dry twigs snapped; knuckles cracking» → twigs/snap + knuckles/crack + egg/crack | 3 | 0.24–0.35 | -15…-14 | -15…-14 | 5/2 |

**ткани**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `squelch` | мокрый шлепок ткани, плоть | «a footstep in thick wet mud» → step/mud + splash/small + pop/mouth | 4 | 0.45–0.45 | -17…-17 | -17…-17 | 2/3 |
| `gut` | кишки и мозг вываливаются: тяжёлый мокрый звук | «a footstep splashing in shallow water; something dipped into water» → step/puddle + water/plop + step/mud | 3 | 0.58–0.59 | -15…-15 | -16…-15 | 4/2 |
| `tear` | рассечение и разрыв ткани | «a piece of cloth torn apart» → cloth/tear + velcro/rip + step/mud | 3 | 0.55–0.64 | -15…-15 | -15…-15 | 4/2 |
| `flay` | сдирание кожи до кости | «a sheet of paper torn off; a piece of cloth torn apart» → paper/rip + cloth/tear + step/mud | 3 | 0.50–0.51 | -16…-16 | -16…-16 | 4/2 |
| `jaw_off` | отрыв челюсти: хруст и разрыв | «a rock cracking; a piece of cloth torn apart; a splash» → break/rock + cloth/tear + step/puddle | 2 | 0.90–0.99 | -12…-12 | -14…-14 | 6/1 |
| `eye` | выпадение глаза: влажный хлопок | «a soft pop made with the lips; a bubble popping» → pop/mouth + step/mud + pop/bubble | 3 | 0.40–0.40 | -16…-16 | -16…-16 | 4/1 |
| `burst` | разрыв головы | «a rock cracking and crumbling; a big splash; an egg cracked» → break/rock + hit/crit + splash/big + egg/crack + step/puddle | 2 | 1.19–1.20 | -11…-10 | -13…-13 | 7/1 |

**зубы**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `tooth` | зуб выбит: костяной щелчок | «a light tap on a glass; knuckles cracking» → knuckles/crack + tick/glass | 4 | 0.27–0.28 | -18…-18 | -18…-18 | 3/2 |
| `tooth_chip` | скол зуба | «a light tap on a glass» → tick/glass + slapstick/crack | 3 | 0.20–0.20 | -21…-21 | -21…-21 | 2/2 |
| `tooth_floor` | зуб стучит об пол | «a light tap on a glass; dice thrown» → dice/throw + slapstick/crack | 4 | 0.14–0.14 | -25…-25 | -25…-25 | 1/3 |
| `clack` | щелчок зубов, вылет капы | «a slapstick snapped shut» → slapstick/crack + pen/cap | 3 | 0.17–0.17 | -18…-18 | -18…-18 | 3/2 |

**ткани**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `drip` | капля крови падает на ринг | «a water drop falling into water, a drip» → drip/drop + drip/plink | 4 | 0.24–0.25 | -28…-28 | -28…-28 | 0/3 |

**тело**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `drop` | оторванный кусок шлёпается на пол | «a short heavy thud landing hard; a footstep in thick wet mud» → hit/drop + step/mud | 3 | 0.28–0.29 | -18…-18 | -21…-20 | 2/2 |
| `thud` | падение тела на ринг | «a person falling down onto a tiled floor» → person/fall + punch/body + kick/deep | 3 | 1.20–1.20 | -12…-12 | -15…-14 | 6/1 |

**бой**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `bell` | гонг: удар колокола ринга | «a reception desk bell rung once, a bright ding» → bell/desk + elevator/ding | 2 | 1.60–1.60 | -14…-14 | -14…-14 | 7/3 |
| `count` | отсчёт нокдауна: рефери хлопает по настилу | «a sharp slap of a hand on skin; a heavy book closed» → hit/slap + thump/book | 3 | 0.17–0.20 | -16…-16 | -19…-19 | 5/1 |

**тело**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `step` | шаг соперника по рингу | «a footstep on an old wooden floor» → step/floorboard + land/wood | 4 | 0.25–0.28 | -26…-26 | -31…-31 | 1/2 |
| `breath` | тяжёлое дыхание соперника | «someone panting out of breath after running» → breath/pant | 3 | 1.50–1.50 | -22…-22 | -22…-22 | 1/1 |
| `cough` | хрип и кашель после нокдауна | «a sharp gasp; a person coughing» → cough/person + gasp/breath | 2 | 1.04–1.30 | -20…-20 | -21…-21 | 1/1 |

**толпа**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `crowd_kd` | толпа на нокдауне: вскрик и гул | «a crowd cheering and whooping» → crowd/cheer + gasp/breath | 2 | 2.49–2.50 | -19…-19 | -19…-19 | 4/1 |
| `crowd_ko` | толпа на нокауте: рёв и аплодисменты | «a crowd cheering and whooping; a small audience applauding» → crowd/cheer + applause/audience | 2 | 4.47–4.50 | -17…-17 | -17…-17 | 6/1 |
| `crowd_boo` | толпа освистывает поражение | «a crowd booing» → crowd/boo | 1 | 3.48–3.48 | -20…-20 | -20…-20 | 5/1 |
| `hall` | фон зала: гул голосов, без звона посуды | «a busy restaurant with murmuring voices» → cafe/busy | 1 | 20.00–20.00 | -30…-30 | -31…-31 | 0/1 петля |

**интерфейс**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `ui` | кнопка | «a soft rounded pop; a computer mouse button clicked» → ui_pop/soft + click/mouse | 2 | 0.16–0.17 | -20…-20 | -20…-20 | 3/1 |
| `tick` | сигнал замаха соперника «!» | «a small metallic tick» → tick/wood + tick/metal | 2 | 0.15–0.15 | -22…-22 | -22…-22 | 4/1 |
| `ready` | мега-удар готов | «a quick rising arpeggio over two octaves; a fast sparkling run of four high bell notes» → power_up/rise + gem/sparkle | 1 | 1.18–1.18 | -17…-17 | -18…-18 | 5/1 |

**удары**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `charge` | замах мега-удара: воздух втягивается | «air sucked in backwards, then a heavy hit» → impact/suck + whoosh/heavy | 2 | 0.43–0.43 | -16…-16 | -20…-17 | 5/1 |

**интерфейс**

| событие | что | запрос earshot → слои | n | длит., с | LUFS | телефон | приор./голоса |
|---|---|---|---|---|---|---|---|
| `combo` | комбо (высота растёт с длиной серии) | «two very quick notes leaping up a fifth» → menu/select | 1 | 0.39–0.39 | -21…-21 | -21…-21 | 2/1 |
| `win` | победа | «a rising six-note major fanfare» → win/fanfare | 1 | 2.85–2.85 | -16…-16 | -16…-16 | 7/1 |
| `lose` | поражение | «three falling notes of a minor chord» → lose/descend | 1 | 3.32–3.32 | -17…-17 | -20…-20 | 7/1 |
| `star` | звезда на экране итога | «a fast sparkling run of four high bell notes» → gem/sparkle | 1 | 0.97–0.97 | -19…-19 | -19…-19 | 3/2 |
| `coin` | монета / счётчик зубов | «two quick high bell notes» → coin/bright | 1 | 0.65–0.65 | -19…-19 | -19…-19 | 3/2 |

## Проверки

- `node tests/sound.js` поднимает свой http-сервер на 127.0.0.1 и проверяет:
  - все 163 файла скачаны и декодированы (webm), все 163 mp3 тоже декодируются;
  - фон зала включается в бою;
  - каждое событие манифеста и каждая функция `sfx` (39 функций, 62 вызова) запускают источник из файла, без синтеза;
  - серия из 50 ударов вперемешку с кровью и хрустом держит общий предел 20 голосов и предел каждого события;
  - с оборванными запросами к `sounds/` игра тихо откатывается на синтез, без ошибок.
- `npm test`, `tests/gore.js`, `tests/knockdown.js`, `tests/layout.js`, `tests/styles.js` проходят.
- Тесты не проверяют, как это звучит, и не проверяют реальный телефон и iOS Safari.

## Что послушать первым

1. `over_head` и `mega` — самые тяжёлые удары: есть ли вес на телефоне, не хрипят ли.
2. `jab_head` звучит чаще всех: не надоедает ли, хватает ли 4 вариантов.
3. `crack_nose`, `crack_skull` — хруст: похож ли на кость, а не на ветку.
4. `squelch`, `tear` — мокрое и разрыв: не слишком ли мерзко или, наоборот, мультяшно.
5. `bell` — гонг из настольного звонка: похож ли на ринг.
6. `hall` — фон зала из записи ресторана: не мешает ли, не слышен ли шов петли (20 с).
7. `thud` — падение соперника.
