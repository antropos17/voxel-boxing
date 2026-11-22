const { chromium, LAUNCH, THREE, PAGE } = require('./lib');
const SEED = +process.argv[2] || 1, SECS = +process.argv[3] || 110, CHEAT = process.argv[4] || '{"god":true}';
let s = SEED; const rnd = () => (s = (s * 16807) % 2147483647) / 2147483647;
(async () => {
  const browser = await chromium.launch(LAUNCH);
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1, hasTouch: true, isMobile: true });
  const page = await ctx.newPage();
  const errs = [];
  page.on('pageerror', e => { const m = 'pageerror: ' + e.message + ' | ' + (e.stack || '').split('\n').slice(1, 4).join(' <- '); if (errs.indexOf(m) < 0) errs.push(m) });
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  await page.goto(PAGE);
  await page.evaluate((c) => localStorage.setItem('vb_cheat', c), CHEAT);
  await page.reload();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 60000 });
  await page.click('#goStart'); await page.waitForTimeout(400);
  await page.evaluate(() => { window.__fc = 0; (function f() { window.__fc++; requestAnimationFrame(f) })(); window.__alive = () => { const a = __prof.n.render; return a } });
  const acts = ['nose', 'jaw', 'cheek', 'orb', 'teeth', 'jawoff', 'guard', 'eye', 'skull', 'flay', 'burst', 'gut', 'arm', 'ribs', 'smile', 'kd', 'heal', 'fresh', 'next'];
  const t0 = Date.now(); let n = 0, lastR = 0, stuck = 0, log = [];
  while (Date.now() - t0 < SECS * 1000) {
    n++;
    const st = await page.evaluate(() => ({ mode: __g.G.mode, st: __g.F.st, hs: __g.headScreen(), r: __prof.n.render, end: !document.getElementById('end').hidden && document.getElementById('game').classList.contains('sheeted'), adm: !document.getElementById('adm').hidden }));
    if (st.r === lastR) { stuck++; if (stuck > 6) { errs.push('RENDER LOOP STOPPED at action ' + n + ' ' + JSON.stringify(log.slice(-6))); break } } else stuck = 0; lastR = st.r;
    if (st.adm) { await page.click('#admClose').catch(() => { }); continue }
    if (st.mode !== 'fight' && st.mode !== 'ko' && st.mode !== 'lost') { const b = await page.$('#goNext'); if (b && await b.isVisible()) { await b.click().catch(() => { }); log.push('goNext'); await page.waitForTimeout(500); continue } await page.waitForTimeout(300); continue }
    const r = rnd(); const hs = st.hs; let a;
    if (r < .3) { a = 'tap'; await page.mouse.click(hs[0] + (rnd() - .5) * 120, hs[1] + (rnd() - .3) * 260) }
    else if (r < .72) { const d = [[90, 0], [-90, 0], [0, -90], [0, 90]][(rnd() * 4) | 0], x = hs[0] + (rnd() - .5) * 110, y = hs[1] + (rnd() - .3) * 240; a = 'swipe' + d; await page.mouse.move(x - d[0] / 2, y - d[1] / 2); await page.mouse.down(); await page.mouse.move(x + d[0] / 2, y + d[1] / 2, { steps: 2 }); await page.mouse.up() }
    else if (r < .78) { a = 'block'; await page.dispatchEvent('#bB', 'pointerdown'); await page.waitForTimeout(250); await page.dispatchEvent('#bB', 'pointerup') }
    else if (r < .83) { a = 'dodge'; await page.dispatchEvent(rnd() < .5 ? '#bL' : '#bR', 'pointerdown') }
    else if (r < .88) { a = 'mega'; await page.evaluate(() => __g.megaAdd(100)) }
    else if (st.mode === 'fight') { a = acts[(rnd() * acts.length) | 0]; await page.click('#admBtn').catch(() => { }); await page.click(`#adm [data-a="${a}"]`, { timeout: 2000 }).catch(() => { }) }
    log.push(a); await page.waitForTimeout(120 + rnd() * 200);
    if (n % 60 === 0) console.log(n, Math.round((Date.now() - t0) / 1000) + 's', JSON.stringify(await page.evaluate(() => { const i = __prof.report()._info; return { lvl: __g.G.level, mode: __g.G.mode, pieces: i.pieces, ropes: i.ropes, tris: i.tris, calls: i.calls, geo: i.geo, wounds: __g.WOUNDS.length, drips: __g.DRIPS.length, heapMB: performance.memory ? Math.round(performance.memory.usedJSHeapSize / 1e6) : 0 } })));
  }
  console.log('frame errs', JSON.stringify(await page.evaluate(() => __g.errs())));
  console.log('actions', n, 'errors:', errs.length ? '\n' + errs.join('\n') : 'none');
  await browser.close();
})().catch(e => { console.error('FAIL', e.message.slice(0, 400)); process.exit(1); });
