const { chromium, LAUNCH, THREE, PAGE } = require('./lib');
(async () => {
  const browser = await chromium.launch(LAUNCH);
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1, hasTouch: true, isMobile: true });
  const page = await ctx.newPage();
  page.on('pageerror', e => console.log('pageerror: ' + e.message));
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  await page.goto(PAGE);
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 60000 });
  const meas = (sel) => page.evaluate((sel) => new Promise(res => {
    const t0 = performance.now(); let longest = 0, lastT = t0;
    const obs = new PerformanceObserver(l => l.getEntries().forEach(e => { if (e.duration > longest) longest = e.duration }));
    try { obs.observe({ entryTypes: ['longtask'] }) } catch (e) { }
    document.querySelector(sel).click();
    (function poll() { if (__g.errs()[2] === 'fight') { setTimeout(() => res({ total: Math.round(performance.now() - t0), longestTask: Math.round(longest) }), 300) } else setTimeout(poll, 4) })();
  }), sel);
  console.log('first bell', JSON.stringify(await meas('#goStart')));
  await page.waitForTimeout(800);
  await page.evaluate(() => __g.adm('burst'));
  await page.waitForFunction(() => { const b = document.getElementById('goNext'); return b && b.offsetParent !== null }, null, { timeout: 30000 });
  console.log('next fight', JSON.stringify(await meas('#goNext')));
  await page.waitForTimeout(800);
  await page.evaluate(() => __g.adm('jawoff'));
  await page.waitForFunction(() => { const b = document.getElementById('goNext'); return b && b.offsetParent !== null }, null, { timeout: 30000 });
  console.log('next fight', JSON.stringify(await meas('#goNext')));
  console.log(JSON.stringify(await page.evaluate(() => __g.errs())));
  await browser.close();
})().catch(e => { console.error('FAIL', e.message.slice(0, 400)); process.exit(1); });
