const { chromium, LAUNCH, THREE, PAGE } = require('./lib');
(async () => {
  const browser = await chromium.launch(LAUNCH);
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1, hasTouch: true, isMobile: true });
  const page = await ctx.newPage();
  const errs = [];
  page.on('pageerror', e => errs.push('pageerror: ' + e.message + ' ' + (e.stack || '').split('\n').slice(1, 3).join(' ')));
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  await page.goto(PAGE);
  await page.evaluate(() => localStorage.setItem('vb_cheat', JSON.stringify({ immortal: true, noKD: true, passive: true, god: true, stam: true, thru: true })));
  await page.reload();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 60000 });
  await page.click('#goStart'); await page.waitForTimeout(300);
  await page.evaluate(() => { __g.G.tipT = 0; __g.F.moveT = 99; __g.F.dzT = 0; });
  const close = async (n) => { await page.evaluate(() => { __g.pgl(false); __g.farm(false); const v = new __g.T.Vector3(); __g.RIG.headG.getWorldPosition(v); __g.setInspect({ pos: [v.x + .1, v.y + .1, v.z + .55], look: [v.x, v.y + .03, v.z], fov: 34 }) }); await page.waitForTimeout(350); await page.screenshot({ path: 'shots/x' + n + '.png' }); await page.evaluate(() => { __g.setInspect(null); __g.pgl(true); __g.farm(true) }); };
  let shot = 0, t0 = Date.now();
  for (let n = 0; n < 16; n++) {
    const hs = await page.evaluate(() => __g.headScreen());
    // alternate: hooks to the forehead / upper face, overhands on top
    const tgt = [[-8, -40], [8, -42], [0, -36], [-4, -46]][n % 4];
    const d = n % 3 === 2 ? [0, 80] : [n % 2 ? 90 : -90, 0];
    await page.mouse.move(hs[0] + tgt[0] - d[0] / 2, hs[1] + tgt[1] - d[1] / 2); await page.mouse.down(); await page.mouse.move(hs[0] + tgt[0] + d[0] / 2, hs[1] + tgt[1] + d[1] / 2, { steps: 2 }); await page.mouse.up();
    await page.waitForTimeout(420);
    if (n === 3 || n === 7 || n === 11 || n === 15) { await page.waitForTimeout(1200); await close(shot++); console.log(n + 1, JSON.stringify(await page.evaluate(() => ({ tr: Object.fromEntries(Object.entries(__g.F.trauma).map(([k, v]) => [k, Math.round(v)])), br: __g.F.broken, pieces: __g.pieces.length, burst: __g.F.burst, landed: __g.G.stats.landed, brain: __g.G.stats.brain })))); }
  }
  console.log('secs', (Date.now() - t0) / 1000);
  await page.click('#admBtn'); await page.click('#adm [data-a="fresh"]'); await page.waitForTimeout(800);
  await page.click('#admBtn'); await page.click('#adm [data-a="flay"]'); await page.waitForTimeout(1200); await close(4);
  await page.click('#admBtn'); await page.click('#adm [data-a="burst"]'); await page.waitForTimeout(1500); await close(5);
  await page.evaluate(() => { __g.pgl(false); __g.setInspect({ pos: [0.5, 2.0, -0.2], look: [0, 1.5, -1], fov: 40 }) }); await page.waitForTimeout(300); await page.screenshot({ path: 'shots/x6.png' });
  console.log('errors:', errs.length ? errs.join('\n') : 'none');
  await browser.close();
})().catch(e => { console.error('FAIL', e.message.slice(0, 300)); process.exit(1); });
