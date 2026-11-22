const { chromium, LAUNCH, THREE, PAGE } = require('./lib');
(async () => {
  const browser = await chromium.launch(LAUNCH);
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1, hasTouch: true, isMobile: true });
  const page = await ctx.newPage();
  page.on('pageerror', e => console.log('pageerror: ' + e.message));
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  const t0 = Date.now();
  await page.goto(PAGE);
  await page.evaluate(() => localStorage.setItem('vb_cheat', '{"god":true}'));
  await page.reload();
  const t1 = Date.now();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 60000 });
  console.log('load ms', Date.now() - t1);
  const progs = () => page.evaluate(() => __g.progs());
  console.log('after load', JSON.stringify(await progs()));
  await page.click('#goStart'); await page.waitForTimeout(1500);
  console.log('fight', JSON.stringify(await progs()));
  for (const a of (process.argv[2] || 'nose,eye,jawoff,fresh,skull,burst,fresh,gut,guard,flay,fresh,eye').split(',')) {
    await page.evaluate((a) => { __g.adm(a) }, a); await page.waitForTimeout(1800);
    console.log(a, JSON.stringify(await progs()));
  }
  await browser.close();
})().catch(e => { console.error('FAIL', e.message.slice(0, 400)); process.exit(1); });
