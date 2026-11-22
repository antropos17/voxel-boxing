/* CPU-профиль (CDP Profiler) обычного боя и тяжёлых травм: самые дорогие функции по собственному времени.
   node tests/perf-cpuprof.js [секунд боя]  — системный Chrome с аппаратной графикой, как tests/perf.js gpu */
const fs = require('fs'), path = require('path');
const { chromium, THREE, PAGE, ROOT } = require('./lib');
const SYS = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const SECS = +process.argv[2] || 12;
(async () => {
  const browser = await chromium.launch({ executablePath: SYS, headless: true, args: ['--no-sandbox', '--ignore-gpu-blocklist', '--enable-gpu', '--use-angle=d3d11', '--disable-gpu-vsync', '--disable-frame-rate-limit'] });
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 3, hasTouch: true, isMobile: true });
  const page = await ctx.newPage();
  await page.route('**/three.min.js', r => r.fulfill({ path: process.env.MIN ? THREE : THREE.replace('three.min.js', 'three.js'), contentType: 'application/javascript' }));/* имена функций three видны только в несжатой сборке */
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  await page.goto(PAGE); await page.evaluate(() => { localStorage.clear(); localStorage.setItem('vb_cheat', '{"god":true,"stam":true,"immortal":true,"noKD":true,"passive":true}') }); await page.reload();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 120000 });
  await page.click('#goStart'); await page.waitForTimeout(1500);
  const cdp = await ctx.newCDPSession(page);
  await cdp.send('Profiler.enable'); await cdp.send('Profiler.setSamplingInterval', { interval: 100 }); await cdp.send('Profiler.start');
  const t0 = Date.now(); let i = 0; const acts = ['flay', 'eye', 'jawoff', 'gut', 'teeth', 'ribs', 'nose', 'skull', 'cheek'];
  while (Date.now() - t0 < SECS * 1000) { i++; const hs = await page.evaluate(() => __g.headScreen()); const d = [[90, 0], [-90, 0], [0, -80]][i % 3];
    await page.mouse.move(hs[0] - d[0] / 2, hs[1] - d[1] / 2); await page.mouse.down(); await page.mouse.move(hs[0] + d[0] / 2, hs[1] + d[1] / 2, { steps: 2 }); await page.mouse.up(); await page.waitForTimeout(250);
    if (i % 4 === 0) await page.evaluate(a => __g.adm(a), acts[(i / 4) % acts.length]) }
  const { profile } = await cdp.send('Profiler.stop');
  const self = {}, byId = {}; let total = 0;
  profile.nodes.forEach(n => byId[n.id] = n);
  const dt = {}; for (let k = 0; k < profile.samples.length; k++) { dt[profile.samples[k]] = (dt[profile.samples[k]] || 0) + (profile.timeDeltas[k] || 0) }
  profile.nodes.forEach(n => { const f = n.callFrame, key = (f.functionName || '(anon)') + (f.url.includes('test.html') ? ':' + (f.lineNumber + 1) : f.url.includes('three') ? ' [three]' : ''); const t = (dt[n.id] || 0) / 1000; self[key] = (self[key] || 0) + t; if (!/\(idle\)|\(program\)/.test(f.functionName)) total += t });
  const rows = Object.entries(self).filter(e => !/\(idle\)/.test(e[0])).sort((a, b) => b[1] - a[1]).slice(0, 30);
  console.log('всего занято ' + total.toFixed(0) + ' мс за ' + SECS + ' с; кадров ' + await page.evaluate(() => __prof.n.render));
  rows.forEach(r => console.log(r[1].toFixed(0).padStart(7) + ' мс ' + (100 * r[1] / total).toFixed(1).padStart(5) + '%  ' + r[0]));
  console.log('chunkTimes', JSON.stringify(await page.evaluate(() => __g.chunkTimes())));
  fs.writeFileSync(path.join(ROOT, 'shots', 'perf-cpuprof.json'), JSON.stringify(rows));
  await browser.close();
})().catch(e => { console.error('FAIL', e.message.slice(0, 400)); process.exit(1) });
