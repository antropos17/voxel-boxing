/* Повторяемые снимки для сравнения «до/после»: засеянный Math.random, кадры шагом ровно 16.7 мс без rAF, без бюджета мешинга.
   TEST_HTML=путь — снять другую сборку (например, старую версию файла из git, собранную в отдельной папке).
   node tests/perf-shots.js <метка>            -> shots/vis-<метка>-<состояние>.png
   node tests/perf-shots.js cmp <метка1> <метка2> -> доля отличающихся пикселей по каждому состоянию */
const fs = require('fs'), path = require('path');
const { chromium, LAUNCH, THREE, PAGE, ROOT } = require('./lib');
const STATES = ['stance', 'wounds', 'face', 'knockdown', 'burst'];
async function shoot(tag) {
  const browser = await chromium.launch(LAUNCH);
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, hasTouch: true, isMobile: true });
  const page = await ctx.newPage(); const errs = []; page.on('pageerror', e => errs.push(e.message));
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  await page.addInitScript(() => { window.__MANUAL = true; let s = 777; Math.random = () => (s = (s * 16807) % 2147483647) / 2147483647 });
  await page.goto(process.env.TEST_HTML ? require('url').pathToFileURL(process.env.TEST_HTML).href : PAGE); await page.evaluate(() => { localStorage.clear(); localStorage.setItem('vb_cheat', '{"god":true,"stam":true,"immortal":true,"passive":true,"nostop":true}') }); await page.reload();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 120000, polling: 100 });
  await page.evaluate(() => { __g.manual(); __g.stepN(90) });
  const cnt = {}; const snap = async nm => { await page.waitForTimeout(150); await page.screenshot({ path: path.join(ROOT, 'shots', `vis-${tag}-${nm}.png`) });
    const c = await page.evaluate(() => __g.perfCats()); cnt[nm] = c; console.log(nm.padEnd(10), 'tris', c._total.tris, 'calls', c._total.calls, Object.entries(c).filter(e => e[0] !== '_total').sort((a, b) => b[1].tris - a[1].tris).slice(0, 6).map(e => e[0] + ' ' + e[1].tris + '/' + e[1].calls).join(', ')) };
  await snap('stance');
  await page.evaluate(() => { const hs = __g.headScreen(); for (let i = 0; i < 14; i++) { __g.impactAt(hs[0] + (i % 5 - 2) * 14, hs[1] + (i % 4 - 1.5) * 16, ['jab', 'hook', 'upper', 'over'][i % 4], i % 2 ? 1 : -1, 1.25); __g.stepN(6) }
    ['flay', 'eye', 'gut', 'nose'].forEach(a => { __g.adm(a); __g.stepN(20) }); __g.stepN(60) });
  await snap('wounds');
  await page.evaluate(() => { __g.pgl(false); __g.farm(false); const v = new __g.T.Vector3(); __g.RIG.headG.getWorldPosition(v); __g.setInspect({ pos: [v.x + .1, v.y + .1, v.z + .55], look: [v.x, v.y + .03, v.z], fov: 34 }); __g.stepN(2) });
  await snap('face');
  await page.evaluate(() => { __g.setInspect(null); __g.pgl(true); __g.farm(true); __g.adm('kd'); __g.stepN(110) });
  await snap('knockdown');
  await page.evaluate(() => { __g.stepN(200); __g.adm('burst'); __g.stepN(50) });
  await snap('burst');
  fs.writeFileSync(path.join(ROOT, 'shots', `vis-${tag}.json`), JSON.stringify(cnt));
  const st = await page.evaluate(() => __g.stepN(0));
  console.log(tag, 'готово', JSON.stringify(st), 'ошибки:', errs.length ? errs.join('\n') : 'нет');
  await browser.close();
}
async function cmp(a, b) {
  const browser = await chromium.launch(LAUNCH); const page = await browser.newPage();
  for (const s of STATES) {
    const A = fs.readFileSync(path.join(ROOT, 'shots', `vis-${a}-${s}.png`)).toString('base64'), B = fs.readFileSync(path.join(ROOT, 'shots', `vis-${b}-${s}.png`)).toString('base64');
    const r = await page.evaluate(async ([A, B]) => { const ld = src => new Promise(r => { const i = new Image(); i.onload = () => r(i); i.src = 'data:image/png;base64,' + src });
      const [ia, ib] = await Promise.all([ld(A), ld(B)]); const w = ia.width, h = ia.height, c = document.createElement('canvas'); c.width = w; c.height = h; const x = c.getContext('2d');
      x.drawImage(ia, 0, 0); const da = x.getImageData(0, 0, w, h).data; x.drawImage(ib, 0, 0); const db = x.getImageData(0, 0, w, h).data;
      let n = 0, big = 0, mx = 0; for (let i = 0; i < da.length; i += 4) { const d = Math.max(Math.abs(da[i] - db[i]), Math.abs(da[i + 1] - db[i + 1]), Math.abs(da[i + 2] - db[i + 2])); if (d) n++; if (d > 24) big++; if (d > mx) mx = d }
      return { diffPct: +(100 * n / (w * h)).toFixed(3), bigPct: +(100 * big / (w * h)).toFixed(3), maxDiff: mx } }, [A, B]);
    console.log(s.padEnd(10), JSON.stringify(r));
  }
  await browser.close();
}
(process.argv[2] === 'cmp' ? cmp(process.argv[3], process.argv[4]) : shoot(process.argv[2] || 'cur')).catch(e => { console.error('FAIL', e.message.slice(0, 400)); process.exit(1) });
