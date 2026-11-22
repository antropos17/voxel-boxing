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
  await page.click('#goStart'); await page.waitForTimeout(800);
  await page.evaluate(() => { __g.G.tipT = 0; __g.F.moveT = 99; __g.F.dzT = 0; __g.F.next = 99; });
  await page.waitForTimeout(600);
  console.log('idle  [yaw pitch roll | skull dx dz | chest dx dz | pelvis dx dz | knee dy] cm');
  console.log(JSON.stringify(await page.evaluate(() => __g.bodyLog())));
  await page.screenshot({ path: 'shots/y0.png' });
  // sample in-page at fixed sim time: record every frame for 1.2 s after a hook-like impulse to the face
  const run = async (tag, js) => { const r = await page.evaluate((js) => new Promise(res => { const out = []; let t = 0, last = performance.now(); window.__TS=.35; eval(js); function f(now) { t += Math.min(.05, (now - last) / 1000)*.35; last = now; out.push([+t.toFixed(2)].concat(__g.bodyLog())); if (t < 1.1) requestAnimationFrame(f); else {window.__TS=1;res(out)} } requestAnimationFrame(f) }), js);
    console.log(tag); r.filter((e, i) => i % Math.ceil(r.length/16) === 0).forEach(e => console.log(' ' + JSON.stringify(e))); };
  await run('HOOK to the face J=3.8 (from his left)', "var v=new __g.T.Vector3();__g.HEADV.group.localToWorld(v.set(.03,-.01,.09));__g.rdHit(v,new __g.T.Vector3(-.94,0,-.34),3.8,true)");
  await page.waitForTimeout(800);
  await run('BODY hook J=3.8', "var v=new __g.T.Vector3();__g.TORSOV.group.localToWorld(v.set(.08,.15,.1));__g.rdHit(v,new __g.T.Vector3(-.5,0,-.86),3.8,false)");
  await page.waitForTimeout(800);
  await page.evaluate(() => { window.__TS = .15; const v = new __g.T.Vector3(); __g.HEADV.group.localToWorld(v.set(.03, -.01, .09)); __g.rdHit(v, new __g.T.Vector3(-.94, 0, -.34), 7, true); __g.pgl(false); });
  for (let k = 1; k <= 4; k++) { await page.waitForTimeout(500); await page.screenshot({ path: 'shots/y' + k + '.png' }); }
  console.log('errors:', errs.length ? errs.join('\n') : 'none');
  await browser.close();
})().catch(e => { console.error('FAIL', e.message.slice(0, 300)); process.exit(1); });
