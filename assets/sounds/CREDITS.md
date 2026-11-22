# Происхождение звуков

Все файлы в `src/` и `web/` сделаны программой [earshot](https://github.com/Leonxlnx/earshot) 1.0.0 (коммит 7a7f7c5c70d4a763a1eca4b3666af7c77e313f97, лицензия MIT, © 2026 Leon Lin) командой `tools/sounds/make.py`, затем обрезаны, сведены и выровнены по громкости нашим кодом. Готовых записей из интернета в наборе нет: earshot хранит не звук, а числа, снятые с записей, и синтезирует по ним новые дубли.

## Условия earshot, дословно (README, раздел Licence)

> MIT for the code (see LICENSE). Everything earshot renders (`make`, `sfx`, `kit`, `forge`) belongs to whoever rendered it: free for any use, commercial too, without credit. The learned sounds, beds, drums and instrument notes come from recordings in the public domain (BigSoundBank, Kenney, VCSL, VSCO-2 CE, Karoryfer and CC0 uploads on Freesound, all CC0 or an explicit public-domain dedication), each recording credited in earshot/data/resyn/CREDITS.md, earshot/data/textures/CREDITS.md, earshot/data/drums/CREDITS.md and earshot/data/notes/CREDITS.md; CC0 asks for no credit, and the files keep it anyway. The bank's fitted sounds come from CC0 recordings (docs/CREDITS_FIT.md); a fit you make of your own recording carries that recording's licence.

Коротко: то, что отрендерил earshot, принадлежит тому, кто рендерил, и годится для любого использования, в том числе коммерческого, без указания авторства. Обязательных требований нет. Ниже авторы исходных записей CC0 — по доброй воле, как делает сам earshot.

Модели «слушания» (CLAP, AST) не ставились и не скачивались; ключи API не использовались.

## Какие звуки earshot использованы и откуда они

Слой — звук из каталога earshot; «события» — наши события из `manifest.json`, где он звучит.

### `applause/audience` — crowd_ko
- `applause/audience` (1): "Applause concert bar #5" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/applause-concert-bar-5-s2483.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `applause/audience` (2): "Applause Concert Bar #4" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/applause-concert-bar-4-s2482.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `applause/audience` (3): "Applause concert bar #3" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/applause-concert-bar-3-s2481.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `apple/bite` — hook_head, crack_nose
- `apple/bite` (1): "Eat a Rusk" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/eat-a-rusk-s0407.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `apple/bite` (2): "Eat a Rusk" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/eat-a-rusk-s0407.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `bag/drop` — hook_body, upper_body, over_body, mega
- `bag/drop` (1): "rpg-audio/dropLeather" by Kenney (kenney.nl), https://kenney.nl/assets/rpg-audio, CC0 1.0 (kenney.nl asset page and the pack License.txt)
- `bag/drop` (2): "Bag Drop Soft Surface 2" by BenjaminNelan, https://freesound.org/people/BenjaminNelan/sounds/584441, CC0 1.0 (public domain dedication)
- `bag/drop` (3): "Canvas bag on floor" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/canvas-bag-on-floor-s0576.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `bag/drop` (4): "Canvas bag on floor" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/canvas-bag-on-floor-s0576.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `bell/desk` — bell
- синтезирован кодом earshot (модуль `object`), записи не использовались

### `book/shut` — guard_break
- `book/shut` (1): "Book Close (by xavierrrangel)" by xavierrrangel, https://freesound.org/people/xavierrrangel/sounds/617410, CC0 1.0 (Openverse API license field "cc0" 1.0 for the Freesound page; preview MP3)
- `book/shut` (2): "Slamming book closed rapidly, loud.wav (by mht23SD)" by mht23SD, https://freesound.org/people/mht23SD/sounds/707336, CC0 1.0 (Openverse API license field "cc0" 1.0 for the Freesound page; preview MP3)
- `book/shut` (3): "Closing a Book" by Kodack, https://freesound.org/people/Kodack/sounds/256472, CC0 1.0 (public domain dedication)
- `book/shut` (4): "book_snap_shut.wav" by j1987, https://freesound.org/people/j1987/sounds/573198/, CC0 1.0 (public domain)
- `book/shut` (5): "Book slamming shut 1" by zmobie, https://freesound.org/people/zmobie/sounds/319809/, CC0 1.0 (public domain)

### `break/rock` — mega, crack_jaw, crack_skull, crack_rib, jaw_off, burst
- синтезирован кодом earshot (модуль `action`), записи не использовались

### `breath/pant` — breath
- `breath/pant` (1): "Breathless Man" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/breathless-man-s1103.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `breath/pant` (2): "Breathless Woman" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/breathless-woman-s1345.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `cafe/busy` — hall
- `restaurant` (cafe/busy): "Restaurant" by Joseph Sardin (BigSoundBank.com), https://bigsoundbank.com/restaurant-s0624.html, CC0 1.0 (public domain); the seconds 7.0 to 23.0 of the recording

### `car_trunk/slam` — hook_head, upper_head, over_head, mega, flurry, hurt, hurt_hook
- `car_trunk/slam` (1): "Car trunk #1" by Joseph Sardin, https://bigsoundbank.com/car-trunk-1-s1512.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `car_trunk/slam` (2): "Car trunk #2" by Joseph Sardin, https://bigsoundbank.com/car-trunk-2-s1525.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `click/mouse` — ui
- `click/mouse` (1): "left mouse click" by Gee-Jay, https://freesound.org/people/Gee-Jay/sounds/172503, CC0 1.0 (public domain dedication)
- `click/mouse` (2): "Raspberry Mouse, Single Click" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/raspberry-mouse-single-click-s1735.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `cloth/tear` — tear, flay, jaw_off
- `cloth/tear` (1): "Cloth" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/cloth-s0013.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `coin/bright` — coin
- синтезирован кодом earshot (модуль `figure`), записи не использовались

### `cough/person` — cough
- `cough/person` (1): "Man cough #5" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/man-cough-5-s2183.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `cough/person` (2): "Woman cough" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/woman-cough-s0654.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `cough/person` (3): "Woman cough" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/woman-cough-s0654.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `cough/person` (4): "Man cough #1" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/man-cough-s0488.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `crowd/boo` — crowd_boo
- `crowd/boo` (1): "Booing Crowd" by NeoSpica, https://freesound.org/people/NeoSpica/sounds/504621/, CC0 1.0 (Freesound page: "Creative Commons 0")
- `crowd/boo` (2): "crowd booing" by HowardV, https://freesound.org/people/HowardV/sounds/264378/, CC0 1.0 (Freesound page: "Creative Commons 0")

### `crowd/cheer` — crowd_kd, crowd_ko
- `crowd/cheer` (1): "Crowd Cheering" by SoundsExciting, https://freesound.org/people/SoundsExciting/sounds/365132/, CC0 1.0 (Freesound page: "Creative Commons 0")
- `crowd/cheer` (2): "Medium crowd cheering 02" by misjoc, https://freesound.org/people/misjoc/sounds/400586/, CC0 1.0 (Freesound page: "Creative Commons 0")
- `crowd/cheer` (3): "Short Crowd Cheer 2.flac" by qubodup, https://freesound.org/people/qubodup/sounds/182572/, CC0 1.0 (Freesound page: "Creative Commons 0")

### `cut_hit/punch` — flurry
- звук банка earshot, подогнан к записи CC0; авторы в docs/CREDITS_FIT.md репозитория earshot

### `dice/throw` — tooth_floor
- `dice/throw` (1): "Two dice on wooden table" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/two-dice-on-wooden-table-s0581.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `dice/throw` (2): "Two dice on wooden table" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/two-dice-on-wooden-table-s0581.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `drip/drop` — drip
- звук банка earshot, подогнан к записи CC0; авторы в docs/CREDITS_FIT.md репозитория earshot

### `drip/plink` — drip
- звук банка earshot, подогнан к записи CC0; авторы в docs/CREDITS_FIT.md репозитория earshot

### `egg/crack` — crack_nose, crack_arm, burst
- `egg/crack` (1): "egg crack1.wav" by Reitanna, https://freesound.org/people/Reitanna/sounds/244723/, CC0 1.0 (Freesound page: "Creative Commons 0")
- `egg/crack` (2): "egg crack20.wav" by Reitanna, https://freesound.org/people/Reitanna/sounds/244743/, CC0 1.0 (Freesound page: "Creative Commons 0")

### `elevator/ding` — bell
- `elevator/ding` (1): "Elevator Ding.wav" by XfiXy8, https://freesound.org/people/XfiXy8/sounds/467299/, CC0 1.0 (Freesound page: "Creative Commons 0")
- `elevator/ding` (2): "Elevator Ding" by Qvantor, https://freesound.org/people/Qvantor/sounds/879807/, CC0 1.0 (Freesound page: "Creative Commons 0")

### `gasp/breath` — cough, crowd_kd
- `gasp/breath` (1): "Male Gasp 1.wav" by jawbutch, https://freesound.org/people/jawbutch/sounds/344407/, CC0 1.0 (Freesound page: "Creative Commons 0")
- `gasp/breath` (2): "Male Gasp 4.wav" by jawbutch, https://freesound.org/people/jawbutch/sounds/344415/, CC0 1.0 (Freesound page: "Creative Commons 0")
- `gasp/breath` (3): "Gasp 4.wav" by jayfrosting, https://freesound.org/people/jayfrosting/sounds/333396/, CC0 1.0 (Freesound page: "Creative Commons 0")

### `gem/sparkle` — ready, star
- синтезирован кодом earshot (модуль `figure`), записи не использовались

### `hit/crit` — over_head, over_body, mega, hurt, hurt_hook, crack_skull, burst
- синтезирован кодом earshot (модуль `action`), записи не использовались

### `hit/drop` — upper_body, drop
- синтезирован кодом earshot (модуль `motion`), записи не использовались

### `hit/punch` — hook_head, hook_body, upper_head, flurry, guard_break
- синтезирован кодом earshot (модуль `action`), записи не использовались

### `hit/slap` — jab_head, jab_body, hook_head, hook_body, upper_head, over_head, mega, flurry, block, hurt_hook, graze, guard_break, crack_nose, count
- синтезирован кодом earshot (модуль `action`), записи не использовались

### `ice/crack` — crack_skull
- `ice/crack` (1): "Cracking ice #2" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/cracking-ice-2-s2206.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `ice/crack` (2): "Cracking ice #3" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/cracking-ice-3-s2207.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `impact/punch` — mega, hurt
- звук банка earshot, подогнан к записи CC0; авторы в docs/CREDITS_FIT.md репозитория earshot

### `impact/suck` — charge
- синтезирован кодом earshot (модуль `motion`), записи не использовались

### `kick/deep` — thud
- `kick/deep` (1): "kick_24_nodamp__kick__kick__k_nodamp_vl1_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)
- `kick/deep` (2): "kick_24_nodamp__kick__kick__k_nodamp_vl2_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)
- `kick/deep` (3): "kick_24_nodamp__kick__kick__k_nodamp_vl4_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)
- `kick/deep` (4): "kick_24_nodamp__kick__kick__k_nodamp_vl5_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)

### `kick/punch` — upper_head, upper_body
- `kick/soft` (1): "kick_24__kick__kick__k_vl1_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)
- `kick/soft` (2): "kick_24__kick__kick__k_vl2_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)
- `kick/soft` (3): "kick_24__kick__kick__k_vl4_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)
- `kick/soft` (4): "kick_24__kick__kick__k_vl6_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)
- `kick/soft` (5): "kick_24__kick__kick__k_vl8_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)
- `kick/soft` (6): "kick_24__kick__kick__k_vl9_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)
- `kick/soft` (7): "kick_24__kick__kick__k_vl11_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)
- `kick/soft` (8): "kick_24__kick__kick__k_vl13_rr1.flac" by Karoryfer Samples (Big Rusty Drums, github.com/sfzinstruments/karoryfer.big-rusty-drums), https://github.com/sfzinstruments/karoryfer.big-rusty-drums, CC0 1.0 (public domain dedication)

### `knuckles/crack` — crack_cheek, crack_jaw, crack_arm, tooth
- `knuckles/crack` (1): "Knuckle Cracking.wav" by suspensiondigital, https://freesound.org/people/suspensiondigital/sounds/389709/, CC0 1.0 (Freesound page: "Creative Commons 0")
- `knuckles/crack` (2): "knuckles cracking.wav" by Reitanna, https://freesound.org/people/Reitanna/sounds/217656/, CC0 1.0 (Freesound page: "Creative Commons 0")

### `land/wood` — step
- `land/wood` (1): "Jumping on wooden pontoon #1" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/jumping-on-wooden-pontoon-1-s1841.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `land/wood` (2): "Jumping on wooden pontoon #2" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/jumping-on-wooden-pontoon-2-s1842.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `leather/grab` — block, guard_break
- `leather/grab` (1): "rpg-audio/handleSmallLeather" by Kenney (kenney.nl), https://kenney.nl/assets/rpg-audio, CC0 1.0 (kenney.nl asset page and the pack License.txt)
- `leather/grab` (2): "rpg-audio/handleSmallLeather2" by Kenney (kenney.nl), https://kenney.nl/assets/rpg-audio, CC0 1.0 (kenney.nl asset page and the pack License.txt)

### `lose/descend` — lose
- синтезирован кодом earshot (модуль `figure`), записи не использовались

### `menu/select` — combo
- синтезирован кодом earshot (модуль `figure`), записи не использовались

### `paper/rip` — flay
- `paper/rip` (1): "Torn paper #2" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/torn-paper-2-s3239.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `paper/rip` (2): "Torn paper #4" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/torn-paper-4-s3241.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `paper/rip` (3): "Torn paper #3" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/torn-paper-3-s3240.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `pen/cap` — clack
- `pen/cap` (1): "Sharpie Marker Cap Open and Shut" by el_boss, https://freesound.org/people/el_boss/sounds/669920, CC0 1.0 (public domain dedication)

### `person/fall` — thud
- `person/fall` (1): "Body fall #4" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/body-fall-4-s2455.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `person/fall` (2): "Body fall #5" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/body-fall-5-s2456.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `pop/bubble` — eye
- синтезирован кодом earshot (модуль `motion`), записи не использовались

### `pop/mouth` — squelch, eye
- `pop/mouth` (1): "Plop Sound made with my mouth" by BlueSiren, https://freesound.org/people/BlueSiren/sounds/414384, CC0 1.0 (Freesound page, recorded in the owner's library licence.json)
- `pop/mouth` (2): "A soft mouth pop" by OZØNA, https://freesound.org/people/OZ%C3%98NA/sounds/721073, CC0 1.0 (public domain dedication)
- `pop/mouth` (3): "Tongue pop" by OZØNA, https://freesound.org/people/OZ%C3%98NA/sounds/721074, CC0 1.0 (Freesound page, recorded in the owner's library licence.json)

### `power_up/rise` — ready
- синтезирован кодом earshot (модуль `figure`), записи не использовались

### `punch/body` — jab_head, jab_body, hook_head, hook_body, upper_head, upper_body, over_head, over_body, mega, flurry, hurt, hurt_hook, crack_cheek, crack_rib, thud
- `punch/body` (1): "Punch #2" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/punches-s0444.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `punch/body` (2): "Punch #5" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/punch-5-s2460.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `punch/body` (3): "Punch #4" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/punch-4-s2459.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `punch/body` (4): "Punch #6" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/punch-6-s2461.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `punch/body` (5): "Punch #2" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/punches-s0444.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `punch/body` (6): "Fist Punch or kick" by rcroller, https://freesound.org/people/rcroller/sounds/424144/, CC0 1.0 (public domain)
- `punch/body` (7): "Face Punch, Body Impact, 4x" by Kinoton, https://freesound.org/people/Kinoton/sounds/503277/, CC0 1.0 (public domain)
- `punch/body` (8): "Face Punch, Body Impact, 4x" by Kinoton, https://freesound.org/people/Kinoton/sounds/503277/, CC0 1.0 (public domain)

### `slapstick/crack` — upper_head, crack_cheek, crack_jaw, tooth_chip, tooth_floor, clack
- `slapstick/crack` (1): "slapstick_rr2.wav" by Versilian Studios (VCSL, github.com/sgossner/VCSL), https://github.com/sgossner/VCSL, CC0 1.0 (public domain dedication)
- `slapstick/crack` (2): "slapstick_rr3.wav" by Versilian Studios (VCSL, github.com/sgossner/VCSL), https://github.com/sgossner/VCSL, CC0 1.0 (public domain dedication)
- `slapstick/crack` (3): "slapstick_rr1.wav" by Versilian Studios (VCSL, github.com/sgossner/VCSL), https://github.com/sgossner/VCSL, CC0 1.0 (public domain dedication)

### `splash/big` — burst
- синтезирован кодом earshot (модуль `action`), записи не использовались

### `splash/small` — wet, squelch
- синтезирован кодом earshot (модуль `action`), записи не использовались

### `step/floorboard` — step
- `step/floorboard` (1): "Man Footsteps on the Wooden Floor" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/man-footsteps-on-the-wooden-floor-s0165.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/floorboard` (2): "feet (slipper on floor)" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/feet-slipper-on-floor-s0373.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/floorboard` (3): "Man Footsteps on the Wooden Floor" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/man-footsteps-on-the-wooden-floor-s0165.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/floorboard` (4): "Man Footsteps on the Wooden Floor" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/man-footsteps-on-the-wooden-floor-s0165.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/floorboard` (5): "Man Footsteps on the Wooden Floor" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/man-footsteps-on-the-wooden-floor-s0165.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/floorboard` (6): "Man Footsteps on the Wooden Floor" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/man-footsteps-on-the-wooden-floor-s0165.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/floorboard` (7): "Man Footsteps on the Wooden Floor" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/man-footsteps-on-the-wooden-floor-s0165.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `step/mud` — wet, squelch, gut, tear, flay, eye, drop
- `step/mud` (1): "Steps in the Mud" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/steps-in-the-mud-s0495.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/mud` (2): "Steps in the Mud" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/steps-in-the-mud-s0495.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/mud` (3): "Steps in the Mud" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/steps-in-the-mud-s0495.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/mud` (4): "Steps in the Mud" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/steps-in-the-mud-s0495.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `step/puddle` — gut, jaw_off, burst
- `step/puddle` (1): "Shoes in the Water" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/shoes-in-the-water-s0690.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/puddle` (2): "Shoes in the Water" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/shoes-in-the-water-s0690.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/puddle` (3): "Shoes in the Water" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/shoes-in-the-water-s0690.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `step/puddle` (4): "Shoes in the Water" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/shoes-in-the-water-s0690.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `swipe/hand` — swing, miss, dodge, graze
- `swipe/hand` (1): "Swipe Whoosh" by qubodup, https://freesound.org/people/qubodup/sounds/60007, CC0 1.0 (public domain dedication)

### `sword/swing` — miss
- `sword/swing` (1): "Whoosh #2" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/whoosh-2-s0573.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `sword/swing` (2): "Whoosh #2" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/whoosh-2-s0573.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `sword/swing` (3): "Whoosh #2" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/whoosh-2-s0573.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `sword/swing` (4): "Whoosh #1" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/whoosh-1-s0572.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `sword/swing` (5): "Whoosh #1" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/whoosh-1-s0572.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `thump/book` — jab_head, jab_body, block, count
- синтезирован кодом earshot (модуль `motion`), записи не использовались

### `tick/glass` — hurt_hook, tooth, tooth_chip
- синтезирован кодом earshot (модуль `ui`), записи не использовались

### `tick/metal` — tick
- синтезирован кодом earshot (модуль `ui`), записи не использовались

### `tick/wood` — tick
- синтезирован кодом earshot (модуль `ui`), записи не использовались

### `twigs/snap` — crack_rib, crack_arm
- `twigs/snap` (1): "Broken twigs #1" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/broken-twigs-1-s1299.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `twigs/snap` (2): "Broken twigs #2" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/broken-twigs-2-s1300.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `twigs/snap` (3): "Broken twigs #1" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/broken-twigs-1-s1299.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `ui_pop/soft` — ui
- звук банка earshot, подогнан к записи CC0; авторы в docs/CREDITS_FIT.md репозитория earshot

### `velcro/rip` — tear
- `velcro/rip` (1): "Velcro tape" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/velcro-tape-s0629.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `water/plop` — gut
- `water/plop` (1): "Water Blop #1" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/water-blop-1-s3427.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `water/plop` (2): "Water Blop #2" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/water-blop-2-s3428.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `water/plop` (3): "Water Blop #3" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/water-blop-3-s3429.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `whoosh/airy` — dodge
- `whoosh/airy` (1): "whip03.wav (by snowflakes)" by snowflakes, https://freesound.org/people/snowflakes/sounds/72191, CC0 1.0 (Openverse API license field "cc0" 1.0 for the Freesound page; preview MP3)
- `whoosh/airy` (2): "Whoosh_Simple_ER_SFX_3.wav (by Alex_hears_things)" by Alex_hears_things, https://freesound.org/people/Alex_hears_things/sounds/521042, CC0 1.0 (Openverse API license field "cc0" 1.0 for the Freesound page; preview MP3)
- `whoosh/airy` (3): "whip02.wav (by snowflakes)" by snowflakes, https://freesound.org/people/snowflakes/sounds/72190, CC0 1.0 (Openverse API license field "cc0" 1.0 for the Freesound page; preview MP3)
- `whoosh/airy` (4): "Swing 1_5 (by Joao_Janz)" by Joao_Janz, https://freesound.org/people/Joao_Janz/sounds/485250, CC0 1.0 (Openverse API license field "cc0" 1.0 for the Freesound page; preview MP3)

### `whoosh/heavy` — charge
- `whoosh/heavy` (1): "Deep Whoosh #6" by Kinoton, https://freesound.org/people/Kinoton/sounds/558826, CC0 1.0 (public domain dedication)

### `whoosh/plank` — whoosh
- `whoosh/plank` (1): "Whoosh #6" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/whoosh-6-s1799.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `whoosh/plank` (2): "Whoosh #9" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/whoosh-9-s1802.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")

### `whoosh/stick` — swing, whoosh
- `whoosh/stick` (1): "SWOSH-01 44.1kHz.flac" by qubodup, https://freesound.org/people/qubodup/sounds/59988, CC0 1.0 (public domain dedication)
- `whoosh/stick` (2): "Whoosh #10" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/whoosh-10-s1798.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `whoosh/stick` (3): "Fast whoosh, bamboo swoosh through air, version 2" by zapsplat.com, https://freesound.org/people/zapsplat.com/sounds/719637, CC0 1.0 (public domain dedication)
- `whoosh/stick` (4): "Whoosh #5" by BigSoundBank (bigsoundbank.com), https://bigsoundbank.com/whoosh-5-s1797.html, CC0 1.0 (bigsoundbank.com/licenses.html; sound page: "CC0 (public domain): Free and royalty-free")
- `whoosh/stick` (5): "swosh-08.flac - 'stick cutting air'" by qubodup, https://freesound.org/people/qubodup/sounds/59995, CC0 1.0 (public domain dedication)

### `win/fanfare` — win
- синтезирован кодом earshot (модуль `figure`), записи не использовались
