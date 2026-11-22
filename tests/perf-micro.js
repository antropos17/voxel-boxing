/* Микробенчмарки «минимум из N» (устойчивы к загрузке машины) и проверка мешинга против эталона v19.
   node tests/perf-micro.js [замедление CPU, по умолчанию 1] — после каждого состояния печатает времена; meshCheck должен давать nbad 0 */
const { chromium, THREE, PAGE } = require('./lib');
const SYS = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe', RATE = +process.argv[2] || 1;
(async () => {
  const browser = await chromium.launch({ executablePath: SYS, headless: true, args: ['--no-sandbox', '--ignore-gpu-blocklist', '--enable-gpu', '--use-angle=d3d11', '--disable-gpu-vsync', '--disable-frame-rate-limit'] });
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 3, hasTouch: true, isMobile: true });
  const page = await ctx.newPage(); const errs = []; page.on('pageerror', e => errs.push(e.message));
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  await page.goto(PAGE); await page.evaluate(() => { localStorage.clear(); localStorage.setItem('vb_cheat', '{"god":true,"stam":true,"immortal":true,"noKD":true,"passive":true}') }); await page.reload();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 120000 });
  await page.click('#goStart'); await page.waitForTimeout(1500);
  const cdp = await ctx.newCDPSession(page); await cdp.send('Emulation.setCPUThrottlingRate', { rate: RATE });
  const show = async tag => { const r = await page.evaluate(() => __g.microBench(5)); console.log(`== ${tag} (CPU ×${RATE})`, JSON.stringify(r)); console.log('   meshCheck', JSON.stringify(await page.evaluate(() => __g.meshCheck())));
    console.log('   meshAB v19/сейчас/во сколько раз', JSON.stringify(await page.evaluate(() => __g.meshAB(7)))) };
  await show('стойка');
  for (const a of ['flay', 'skull', 'gut', 'eye', 'jaw', 'nose', 'cheek', 'ribs']) { await page.evaluate(a => __g.adm(a), a); await page.waitForTimeout(400) }
  for (let i = 0; i < 20; i++) await page.evaluate(i => __g.impactAt(195 + (i % 5 - 2) * 18, 330 + (i % 4) * 25, i % 2 ? 'hook' : 'jab', i % 2 ? 1 : -1, 1.3), i);
  await page.waitForTimeout(1500);
  await show('после травм');
  await page.evaluate(() => __g.adm('burst')); await page.waitForTimeout(1500);
  await show('разрыв головы');
  console.log('ошибки:', errs.length ? errs.join('\n') : 'нет');
  await browser.close();
})().catch(e => { console.error('FAIL', e.message.slice(0, 400)); process.exit(1) });
