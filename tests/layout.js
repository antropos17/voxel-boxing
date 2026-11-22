const { chromium, LAUNCH, THREE, PAGE } = require('./lib');
(async () => {
  const browser = await chromium.launch(LAUNCH);
  const errs = [];
  for (const [w, h, tag] of [[844, 390, 'land']]) {
    const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: 1, hasTouch: true, isMobile: true });
    const page = await ctx.newPage();
    page.on('pageerror', e => errs.push(tag + ' pageerror: ' + e.message));
    await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
    await page.route('**/fonts.googleapis.com/**', r => r.abort());
    await page.goto(PAGE);
    await page.evaluate(() => localStorage.setItem('vb_cheat', JSON.stringify({ passive: true })));
    await page.reload();
    await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 60000 });
    await page.screenshot({ path: 'shots/z-' + tag + '0.png' });
    await page.click('#goStart'); await page.waitForTimeout(900);
    for (const k of ['nose', 'jaw', 'cheek', 'orb', 'guard', 'arm', 'ribs', 'gut']) { await page.click('#admBtn'); await page.click(`#adm [data-a="${k}"]`); await page.waitForTimeout(150); }
    await page.waitForTimeout(600);
    // near-miss tap: 30 px outside the head silhouette on the side
    const hs = await page.evaluate(() => __g.headScreen());
    const before = await page.evaluate(() => __g.G.stats.landed);
    const edge = await page.evaluate(([x, y]) => { let k = 0; while (k < 300 && __g.aim(x + k, y, true).kind !== 'none') k += 4; return k }, hs);
    await page.mouse.click(hs[0] + edge + 22, hs[1]); await page.waitForTimeout(500);
    console.log(tag, 'edge', edge, 'near-miss tap landed:', (await page.evaluate(() => __g.G.stats.landed)) - before, 'SWIPE', await page.evaluate(() => 0));
    await page.screenshot({ path: 'shots/z-' + tag + '1.png' });
    await page.click('#admBtn'); await page.waitForTimeout(200); await page.screenshot({ path: 'shots/z-' + tag + '2.png' });
    await ctx.close();
  }
  console.log('errors:', errs.length ? errs.join('\n') : 'none');
  await browser.close();
})().catch(e => { console.error('FAIL', e.message.slice(0, 300)); process.exit(1); });
