const { chromium, LAUNCH, THREE, PAGE } = require('./lib');
const mode = process.argv[2] || 'kd';
(async () => {
  const browser = await chromium.launch(LAUNCH);
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1, hasTouch: true, isMobile: true });
  const page = await ctx.newPage();
  const errs = [];
  page.on('pageerror', e => errs.push('pageerror: ' + e.message + ' ' + (e.stack || '').split('\n').slice(1, 3).join(' ')));
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  await page.goto(PAGE);
  await page.evaluate(() => localStorage.setItem('vb_cheat', JSON.stringify({ passive: true, god: true })));
  await page.reload();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 60000 });
  await page.click('#goStart'); await page.waitForTimeout(300);
  await page.evaluate(() => { __g.G.tipT = 0; __g.F.moveT = 99; __g.F.dzT = 0; __g.pgl(false); document.getElementById('hud').style.display='none'; });
  await page.waitForTimeout(400);
  const cam = (p, l) => page.evaluate(([p, l]) => __g.setInspect({ pos: p, look: l, fov: 50 }), [p, l]);
  await cam([1.7, 1.3, -0.3], [0, 0.75, -1.1]);
  await page.waitForTimeout(200); await page.screenshot({ path: 'shots/s0.png' });
  if (mode === 'hook') await page.evaluate(() => { window.__hook = 1; });
  await page.evaluate((mode) => { __g.F.lastJ = 4.5; }, mode);
  await page.click('#admBtn'); await page.click('#adm [data-a="kd"]');
  await page.evaluate(() => __g.pgl(false));
  for (let k = 1; k <= 5; k++) { await page.waitForTimeout(k < 3 ? 250 : 500); await page.screenshot({ path: 'shots/s' + k + '.png' }); }
  await cam([0.2, 2.6, -0.2], [0.05, 0, -1.3]); await page.waitForTimeout(200); await page.screenshot({ path: 'shots/s6.png' });
  await cam([1.7, 1.3, -0.3], [0, 0.75, -1.1]);
  await page.waitForFunction(() => __g.F.st === 'rise', null, { timeout: 120000 });
  for (let k = 7; k <= 12; k++) { await page.waitForTimeout(280); await page.screenshot({ path: 'shots/s' + k + '.png' }); }
  await page.waitForFunction(() => __g.F.st !== 'rise', null, { timeout: 60000 });
  await page.waitForTimeout(300); await page.screenshot({ path: 'shots/s13.png' });
  console.log('errors:', errs.length ? errs.join('\n') : 'none');
  await browser.close();
})().catch(e => { console.error('FAIL', e.message.slice(0, 300)); process.exit(1); });
