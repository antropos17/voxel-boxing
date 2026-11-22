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
  await page.evaluate(() => localStorage.setItem('vb_cheat', JSON.stringify({ passive: true, god: true, immortal: true, noKD: true })));
  await page.reload();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 60000 });
  await page.click('#goStart'); await page.waitForTimeout(900);
  await page.evaluate(() => { __g.G.tipT = 0; __g.F.moveT = 99; __g.F.dzT = 0; __g.F.next = 99; document.getElementById('hud').style.display = 'none'; });
  await page.waitForTimeout(500);
  const info = async (t) => console.log(t, JSON.stringify(await page.evaluate(() => { const r = __g.renderer.info.render; return { calls: r.calls, tris: r.triangles } })));
  let n = 0; const shot = async () => { await page.waitForTimeout(350); await page.screenshot({ path: 'shots/q' + (n++) + '.png' }); };
  await shot(); await info('standing');
  const views = [[[1.3, 1.5, -0.9], [0, 1.3, -0.95]], [[-0.9, 1.9, -0.3], [0, 1.3, -0.95]], [[0.0, 1.5, -2.2], [0, 1.3, -0.95]], [[0.3, 0.6, 0.1], [0, 1.2, -0.95]], [[0.0, 2.8, -0.9], [0, 1.0, -0.95]]];
  for (const v of views) { await page.evaluate(([p, l]) => __g.setInspect({ pos: p, look: l, fov: 45 }), v); await shot(); await info('view'); }
  await page.evaluate(() => __g.setInspect(null));
  // head turned hard + mouth open
  await page.evaluate(() => { const v = new __g.T.Vector3(); __g.HEADV.group.localToWorld(v.set(.03, -.01, .09)); __g.rdHit(v, new __g.T.Vector3(-.94, 0, -.34), 6, true); window.__TS = .1 });
  await page.waitForTimeout(900); await shot(); await info('head turned');
  await page.evaluate(() => { window.__TS = 1 }); await page.waitForTimeout(1500);
  await page.evaluate(() => localStorage.setItem('vb_cheat', '{}'));
  await page.click('#admBtn'); await page.click('#adm [data-a="kd"]'); await page.waitForTimeout(2500); await shot(); await info('down');
  await page.evaluate(() => __g.setInspect({ pos: [1.2, 1.2, -0.4], look: [0, 0.2, -1.4], fov: 50 })); await shot(); await info('down side');
  console.log('errors:', errs.length ? errs.join('\n') : 'none');
  await browser.close();
})().catch(e => { console.error('FAIL', e.message.slice(0, 300)); process.exit(1); });
