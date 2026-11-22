const { chromium, LAUNCH, THREE, PAGE } = require('./lib');
(async () => {
  const browser = await chromium.launch(LAUNCH);
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, hasTouch: true, isMobile: true });
  const page = await ctx.newPage();
  const errs = [];
  page.on('pageerror', e => errs.push('pageerror: ' + e.message));
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  await page.goto(PAGE);await page.evaluate(()=>localStorage.clear());await page.reload();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 60000 });
  await page.click('#goStart');
  await page.evaluate(() => { window.__lab=[]; new MutationObserver(m=>m.forEach(r=>r.addedNodes.forEach(n=>__lab.push(n.className+':'+n.textContent)))).observe(document.getElementById('pops'),{childList:true});
    new MutationObserver(()=>__lab.push('BANNER:'+document.getElementById('banner').textContent)).observe(document.getElementById('banner'),{childList:true,characterData:true,subtree:true}); });
  // play a real fight for ~25 s: mix of taps, swipes, block on windup
  const t0 = Date.now(); let n = 0, ft = [];
  while (Date.now() - t0 < 30000) {
    const st = await page.evaluate(() => [__g.G.mode, __g.F.st, __g.headScreen()]);
    if (st[0] !== 'fight') break;
    const hs = st[2];
    if (st[1] === 'windup') { await page.dispatchEvent('#bB', 'pointerdown'); await page.waitForTimeout(400); await page.dispatchEvent('#bB', 'pointerup'); continue; }
    n++;
    if (n % 3) await page.mouse.click(hs[0] + (n % 5 - 2) * 12, hs[1] + (n % 4) * 15);
    else { const d = [[90, 0], [-90, 0], [0, -90], [0, 90]][(n / 3 | 0) % 4]; await page.mouse.move(hs[0] - d[0] / 2, hs[1] - d[1] / 2 + 10); await page.mouse.down(); await page.mouse.move(hs[0] + d[0] / 2, hs[1] + d[1] / 2 + 10, { steps: 2 }); await page.mouse.up(); }
    await page.waitForTimeout(260);
    if (n === 20) await page.screenshot({ path: 'shots/j1-fight.png' });
  }
  await page.screenshot({ path: 'shots/j2-end.png' });
  const r = await page.evaluate(() => ({ mode: __g.G.mode, st: __g.F.st, hp: __g.F.hp | 0, php: __g.P.hp | 0, mega: __g.P.mega | 0, rd: __g.P.megaReady, stats: __g.G.stats, lab: __lab.slice(0, 60) }));
  console.log(JSON.stringify(r));
  console.log('frame errs', JSON.stringify(await page.evaluate(() => __g.errs())));
  console.log('errors:', errs.length ? errs.join('\n') : 'none');
  await browser.close();
})().catch(e => { console.error('FAIL', e); process.exit(1); });
