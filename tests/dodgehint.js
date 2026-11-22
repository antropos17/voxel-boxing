/* Подсказка уклона в две стадии, в игровом времени (шаги __g.run, без настоящих таймеров).
   node tests/dodgehint.js
   Для замахов 0.75 / 0.5 / 0.34 / 0.22 с, при 60 и при 20 кадрах в секунду, обе руки:
   - нажатие в первый кадр полной заливки (класс go) -> соперник раскрыт (open);
   - при длинном замахе нажатие в первый кадр «готовься» (класс hint без go) -> полный урон и подпись «Рано»;
   - на финте ни рамки, ни заливки.
   Math.random сеян с загрузки, requestAnimationFrame заглушён: прогон воспроизводим. */
const { chromium, LAUNCH, THREE, PAGE } = require('./lib');
(async () => {
  const browser = await chromium.launch(LAUNCH);
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1, hasTouch: true, isMobile: true });
  const page = await ctx.newPage();
  const errs = [];
  page.on('pageerror', e => errs.push('pageerror: ' + e.message + ' ' + (e.stack || '').split('\n').slice(1, 3).join(' ')));
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  await page.addInitScript(() => { let a = 1; Math.random = () => { a = (a + 0x6D2B79F5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296 }; window.requestAnimationFrame = () => 0 });
  await page.goto(PAGE);
  await page.evaluate(() => localStorage.setItem('vb_cheat', JSON.stringify({ nostop: true })));
  await page.reload();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 60000, polling: 200 });
  const res = await page.evaluate(() => {
    const g = __g, bL = document.getElementById('bL'), bR = document.getElementById('bR'), pops = document.getElementById('pops');
    const stage = () => { const b = bL.classList.contains('hint') ? bL : bR.classList.contains('hint') ? bR : null; return b ? (b.classList.contains('go') ? 'go' : 'ready') + (b === bL ? 'L' : 'R') : '' };
    const said = w => [...pops.children].some(e => e.textContent === w);
    g.CHEAT.god = false; g.CHEAT.passive = true; g.G.level = 1; g.startFight(false); g.G.tipT = 0; g.G.anim = 0;
    const out = [];
    function trial(dur, ms, hand, when) {
      const F = g.F, P = g.P; P.hp = 100; P.dodge = 0; P.dodgeCd = 0; P.block = false; F.slowMul = 1; F.groggy = 0; F.hp = F.max; F.stun = 0; F.moveT = 99;
      g.setF('idle'); F.next = 99; g.run(20, null, ms); pops.innerHTML = '';
      g.swing(hand, 'straight', dur);
      let pressed = null, firstReady = null, firstGo = null, t = 0, end = null;
      g.run(400, () => {
        const s = stage();
        if (s.startsWith('ready') && firstReady === null) firstReady = +t.toFixed(3);
        if (s.startsWith('go') && firstGo === null) firstGo = +t.toFixed(3);
        if (!pressed && s.startsWith(when)) { pressed = { s, t: +t.toFixed(3), left: +(F.st === 'windup' ? F.dur - F.t + .13 : .13 - F.t).toFixed(3) }; g.dodge(s.endsWith('L') ? -1 : 1) }
        if (F.st !== 'windup' && F.st !== 'strike') { end = F.st; return false }
        t += ms / 1000;
      }, ms);
      out.push({ dur, fps: Math.round(1000 / ms), hand, when, pressed, firstReady, firstGo, st: end, dmg: +(100 - P.hp).toFixed(2), full: +F.dmg.toFixed(2), early: said('Рано') });
    }
    for (const ms of [16.7, 50]) for (const dur of [.75, .5, .34, .22]) for (const hand of [-1, 1]) {
      trial(dur, ms, hand, 'go');
      if (dur >= .5) trial(dur, ms, hand, 'ready');
    }
    /* feint: Утюг, every windup a feint */
    g.CHEAT.passive = false; g.CHEAT.god = true; g.G.level = 5; g.startFight(false); g.G.tipT = 0; g.G.anim = 0;
    const F = g.F; F.feintP = 1; F.counterP = 0; F.guardP = 0; F.next = .2;
    let feintFrames = 0, feintLit = 0, realLit = false, phase = 0;
    g.run(600, () => {
      if (F.st === 'windup' && F.feint) { phase = 1; feintFrames++; if (stage()) feintLit++ }
      else if (phase === 1 && F.st === 'windup') { if (stage()) realLit = true; if (F.t > .05) return false }
    });
    return { out, feint: { feintFrames, feintLit, realLit }, errs: g.errs() };
  });
  let fail = 0; const bad = m => { fail++; console.log('FAIL', m) };
  console.log('замах  к/с  рука  нажал    в стадии  остаток до удара  рамка с  заливка с  итог     урон / полный  «Рано»');
  for (const o of res.out) {
    console.log([String(o.dur).padEnd(5), String(o.fps).padStart(3), String(o.hand).padStart(4), o.when.padEnd(7), (o.pressed ? o.pressed.s : '-').padEnd(8),
      String(o.pressed ? o.pressed.left : '-').padStart(16), String(o.firstReady).padStart(7), String(o.firstGo).padStart(9), String(o.st).padEnd(7), (o.dmg + ' / ' + o.full).padStart(13), String(o.early).padStart(6)].join('  '));
    if (!o.pressed) { bad('стадия ' + o.when + ' не появилась ' + JSON.stringify(o)); continue }
    if ((o.pressed.s.endsWith('L') ? -1 : 1) !== -o.hand) bad('подсвечена не та сторона ' + JSON.stringify(o));
    if (o.when === 'go' && !(o.st === 'open' && o.dmg === 0 && !o.early)) bad('нажатие в первый кадр заливки не спасло ' + JSON.stringify(o));
    if (o.when === 'ready' && !(o.st === 'recover' && Math.abs(o.dmg - o.full) < .05 && o.early)) bad('раннее нажатие: не полный урон или нет «Рано» ' + JSON.stringify(o));
    if (o.firstReady === null || o.firstReady > .05) bad('рамка не с начала замаха ' + JSON.stringify(o));
  }
  console.log('финт: ' + JSON.stringify(res.feint));
  if (!(res.feint.feintFrames > 3 && res.feint.feintLit === 0 && res.feint.realLit)) bad('подсказка на финте или нет её на настоящем замахе');
  if (res.errs[0]) bad('ошибки кадра: ' + res.errs[1]);
  console.log('errors:', errs.length ? errs.join('\n') : 'none');
  console.log(fail ? 'dodgehint: FAIL ' + fail : 'dodgehint: OK');
  await browser.close();
  process.exit(fail || errs.length ? 1 : 0);
})().catch(e => { console.error('FAIL', e.message.slice(0, 400)); process.exit(1); });
