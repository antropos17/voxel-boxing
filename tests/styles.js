/* Стили соперников и правило уклона.
   node tests/styles.js [секунд на соперника]
   Воспроизводимость: Math.random в странице заменён сеяным генератором (__seed) с самой загрузки, requestAnimationFrame заглушён —
   настоящих кадров нет, игра идёт только шагами __g.run по 16,7 мс. Одинаковый прогон даёт одинаковую таблицу.
   1) каждый из 8 соперников бьёт N игровых секунд (по умолчанию 120) против игрока без урона; игрок раз в 1,5 с мажет джебом или бьёт в перчатку (повод для контры)
      и раз в 2 с уклоняется в случайную сторону (так видно, сколько финтов поймали на уклон).
      Печатает: замахи, одиночные, серии, финты (в скобках: на сколько из них игрок уклонился), контры, средний замах, доля хуков,
      доля атак, начатых перчаткой справа на экране (это левая рука соперника: он стоит к нам лицом; у левши Утюга около 80%).
   2) уклон: правильная сторона -> open; неправильная при прямом -> половина урона; при хуке -> полный урон; уклон на финт -> удар доходит. */
const { chromium, LAUNCH, THREE, PAGE } = require('./lib');
const SEC = +process.argv[2] || 120;
(async () => {
  const browser = await chromium.launch(LAUNCH);
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1, hasTouch: true, isMobile: true });
  const page = await ctx.newPage();
  const errs = [];
  page.on('pageerror', e => errs.push('pageerror: ' + e.message + ' ' + (e.stack || '').split('\n').slice(1, 3).join(' ')));
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  await page.addInitScript(() => { const o = Math.random; window.__seed = n => { let a = n >>> 0; Math.random = () => { a = (a + 0x6D2B79F5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296 } }; window.__unseed = () => { Math.random = o }; window.__seed(1); window.requestAnimationFrame = () => 0 });
  await page.goto(PAGE);
  await page.evaluate(() => localStorage.setItem('vb_cheat', JSON.stringify({ god: true, nostop: true })));
  await page.reload();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 60000, polling: 200 });
  let fail = 0; const bad = m => { fail++; console.log('FAIL', m) };

  /* all eight fights in one synchronous evaluate, started straight from the menu: no real frames with an unseeded Math.random run in between */
  const rows = await page.evaluate((sec) => { const rows = []; for (let lv = 1; lv <= 8; lv++) {
      __seed(1000 + lv); const g = __g; g.CHEAT.god = true; g.CHEAT.passive = false; g.G.level = lv; g.startFight(false); g.G.tipT = 0; g.G.anim = 0; g.P.mega = 0; g.P.megaReady = false;
      let n = 0;
      g.run(Math.round(sec * 60), i => {
        const F = g.F; F.hp = F.max; F.stun = 0; F.liver = 0; g.P.hp = 100;
        if (g.G.mode !== 'fight') return false;
        if (i % 120 === 100) g.dodge(Math.random() < .5 ? -1 : 1);
        if (i % 90 === 45) {
          if ((n++) % 2) { const p = g.glovePx(i % 180 < 90 ? 0 : 1); g.impactAt(p[0], p[1], 'jab', -1, 1) }
          else g.punch('jab', 12, 300, -1, 1, 0, 0);
        }
      });
      const F = g.F, L = F.log;
      __unseed();
      rows.push({ lv, name: F.name, style: F.sty.s, hn: F.hn, mode: g.G.mode, ...L, avgW: L.wind ? Math.round(L.wt / L.wind * 1000) : 0 });
    } return rows }, SEC);
  console.log('бой | соперник  | стиль               | замахи | одиноч | серии | финты (поймал) | контры | замах мс | хуки % | 1-я справа % | подсказка');
  for (const r of rows) console.log([String(r.lv).padStart(3), r.name.padEnd(9), r.style.padEnd(19), String(r.wind).padStart(6), String(r.one).padStart(6), String(r.ser).padStart(5),
    (r.feint + ' (' + r.bait + ')').padStart(14), String(r.ctr).padStart(6), String(r.avgW).padStart(8), String(Math.round(r.hook / Math.max(1, r.wind) * 100)).padStart(6),
    String(Math.round(r.right / Math.max(1, r.lead) * 100)).padStart(12), String(r.hn).padStart(9)].join(' | '));
  const by = n => rows.find(r => r.name === n);
  const K = by('Кирпич'), Sh = by('Шкаф'), M = by('Молот'), B = by('Бетон'), U = by('Утюг'), Ti = by('Тихий'), Bu = by('Бульдозер');
  if (rows.some(r => r.mode !== 'fight')) bad('бой закончился раньше времени');
  if (K.feint || K.ser || K.ctr) bad('у Кирпича есть финты/серии/контры');
  if (!(K.avgW > M.avgW && Sh.avgW > M.avgW)) bad('замах Кирпича и Шкафа не длиннее, чем у Молота');
  if (!(M.ser > M.one)) bad('у Молота серий не больше, чем одиночных');
  if (!(B.ctr >= 5 && B.ctr > 3 * (M.ctr + K.ctr + Sh.ctr + U.ctr))) bad('Бетон не контратакует заметно чаще других');
  if (!(U.feint >= 5 && U.right > U.lead * .65)) bad('Утюг: мало финтов или не левша (первая рука справа на экране < 65%)');
  if (!(Ti.feint > 0 && Ti.ctr > 0 && Ti.ser > 0)) bad('Тихий смешивает не всё');
  if (!(Bu.wind > K.wind * 1.6)) bad('Бульдозер не напористее Кирпича');
  for (const r of rows) if (!(r.lv === 1 || r.hn < rows[r.lv - 2].hn || r.hn === 0)) bad('подсказка не слабеет к бою ' + r.lv);

  /* dodge rule: forced swings on fight 1 with the AI held passive, player takes real damage */
  const dz = await page.evaluate(() => {
    __seed(7); const g = __g; g.CHEAT.god = false; g.CHEAT.passive = true; g.G.level = 1; g.startFight(false); g.G.tipT = 0; g.G.anim = 0;
    const out = [];
    function trial(hand, kind, how) {
      const F = g.F, P = g.P; P.hp = 100; P.dodge = 0; P.dodgeCd = 0; P.block = false; F.slowMul = 1; F.groggy = 0; F.hp = F.max; F.stun = 0; F.moveT = 99;
      g.setF('idle'); F.next = 99; g.run(30); g.swing(hand, kind, .6);
      let side = 0, px = null, hint = null;
      g.run(200, () => { if (F.st === 'windup' && F.t > .4) { side = F.side; px = g.glovePx(hand < 0 ? 0 : 1); hint = document.getElementById('bL').classList.contains('hint') ? -1 : document.getElementById('bR').classList.contains('hint') ? 1 : 0; return false } });
      if (how !== 'none') g.dodge(how === 'right' ? -side : side);
      g.run(200, () => (F.st === 'windup' || F.st === 'strike') ? undefined : false);
      const st = F.st; g.run(5);
      out.push({ hand, kind, how, side, px: px && Math.round(px[0]), hint, st, dmg: +(100 - P.hp).toFixed(2), full: +(F.dmg * (kind === 'hook' ? 1.25 : 1)).toFixed(2) });
    }
    for (const hand of [-1, 1]) for (const kind of ['straight', 'hook']) for (const how of ['none', 'right', 'wrong']) trial(hand, kind, how);
    /* feint: Утюг with every windup a feint; dodge during the fake -> the real blow lands */
    g.CHEAT.passive = false; g.G.level = 5; g.startFight(false); g.G.tipT = 0; g.G.anim = 0;
    const F = g.F, P = g.P; F.feintP = 1; F.counterP = 0; F.guardP = 0; F.next = .2; P.hp = 100; P.dodge = 0; P.dodgeCd = 0;
    let dodged = false, hintOnFeint = false, hpAt = 0, res = null;
    g.run(900, () => {
      if (!dodged && F.st === 'windup' && F.feint && F.t > .05) { if (document.getElementById('bL').classList.contains('hint') || document.getElementById('bR').classList.contains('hint')) hintOnFeint = true; g.dodge(1); dodged = true; hpAt = P.hp }
      if (dodged && (F.st === 'recover' || F.st === 'open')) { res = { st: F.st, dmg: +(hpAt - P.hp).toFixed(2), bait: F.log.bait, hintOnFeint }; return false }
    });
    g.CHEAT.god = true;
    __unseed();
    return { out, feint: res };
  });
  console.log('\nуклон: рука  удар      уклон  сторона  x перчатки  подсказка  итог     урон / полный');
  for (const o of dz.out) {
    console.log('       ' + String(o.hand).padStart(3) + '  ' + o.kind.padEnd(8) + '  ' + o.how.padEnd(5) + '  ' + String(o.side).padStart(6) + '  ' + String(o.px).padStart(10) + '  ' + String(o.hint).padStart(9) + '  ' + o.st.padEnd(7) + '  ' + o.dmg + ' / ' + o.full);
    if (o.side !== o.hand || (o.side < 0) !== (o.px < 195)) bad('сторона удара не совпала с проекцией перчатки ' + JSON.stringify(o));
    if (o.hint !== -o.side) bad('подсвечена не та кнопка ' + JSON.stringify(o));
    const near = (a, b) => Math.abs(a - b) < .05;
    if (o.how === 'none' && !(o.st === 'recover' && near(o.dmg, o.full))) bad('без уклона не полный урон ' + JSON.stringify(o));
    if (o.how === 'right' && !(o.st === 'open' && o.dmg === 0)) bad('правильный уклон не раскрыл соперника ' + JSON.stringify(o));
    if (o.how === 'wrong' && o.kind === 'straight' && !(o.st === 'recover' && near(o.dmg, o.full / 2))) bad('неправильный уклон от прямого: не половина ' + JSON.stringify(o));
    if (o.how === 'wrong' && o.kind === 'hook' && !(o.st === 'recover' && near(o.dmg, o.full))) bad('неправильный уклон от хука: не полный урон ' + JSON.stringify(o));
  }
  console.log('финт: ' + JSON.stringify(dz.feint));
  if (!dz.feint || dz.feint.st !== 'recover' || !(dz.feint.dmg > 0) || dz.feint.bait < 1 || dz.feint.hintOnFeint) bad('уклон на финт не наказан ' + JSON.stringify(dz.feint));
  const e = await page.evaluate(() => __g.errs());
  if (e[0]) bad('ошибки кадра: ' + e[1]);
  console.log('errors:', errs.length ? errs.join('\n') : 'none');
  console.log(fail ? 'styles: FAIL ' + fail : 'styles: OK');
  await browser.close();
  process.exit(fail || errs.length ? 1 : 0);
})().catch(e => { console.error('FAIL', e.message.slice(0, 400)); process.exit(1); });
