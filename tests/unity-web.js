// Проверка Unity-сборки игры в браузере: загрузка, меню -> бой -> удары до развязки, замер кадров, памяти, размера загрузки.
// node tests/unity-web.js [папка сборки | URL] [--mobile] [--headless] [--out папка] [--hits N] [--maxSec S]
// По умолчанию: Unity/Builds/Web через свой сервер, настоящий Chrome с окном (GPU), портрет 390x844 x2, мышь.
// --mode quick|training|ladder|trial — режим боя (по умолчанию quick).
// --mobile: эмуляция телефона (UA Android, касания, плотность 3 -> в шаблоне не выше 2): на телефоне игра включает ANAT_LITE.
// Меню проходит через GameApp.Press (SendMessage), удары — настоящим вводом (мышь или касания) по холсту.
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require(require.resolve('playwright', { paths: [process.cwd(), path.join(__dirname, '..'), path.join(__dirname, '../../../..')] }));

const args = process.argv.slice(2);
const opt = (k, d) => { const i = args.indexOf(k); return i >= 0 ? args[i + 1] : d; };
const flag = k => args.includes(k);
const src = args[0] && !args[0].startsWith('--') ? args[0] : 'Unity/Builds/Web';
const MOBILE = flag('--mobile'), HEADLESS = flag('--headless');
const OUT = opt('--out', 'Unity/Shots/web' + (MOBILE ? '-mobile' : ''));
const HITS = +opt('--hits', 30), MAXSEC = +opt('--maxSec', 300);
fs.mkdirSync(OUT, { recursive: true });

const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon', '.css': 'text/css' };
function serve(dir) {
  return new Promise(res => {
    const s = http.createServer((q, r) => {
      let p = decodeURIComponent(q.url.split('?')[0]); if (p.endsWith('/')) p += 'index.html';
      const f = path.join(dir, p);
      if (!f.startsWith(path.resolve(dir)) || !fs.existsSync(f)) { r.writeHead(404); return r.end(); }
      r.writeHead(200, { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(r);
    }).listen(0, '127.0.0.1', () => res(s));
  });
}
const sleep = ms => new Promise(r => setTimeout(r, ms));
const pct = (a, p) => { if (!a.length) return 0; const b = [...a].sort((x, y) => x - y); return b[Math.min(b.length - 1, Math.floor(p * b.length))]; };

(async () => {
  let url = src, server = null;
  if (!/^https?:/.test(src)) { server = await serve(path.resolve(src)); url = `http://127.0.0.1:${server.address().port}/`; }
  const browser = await chromium.launch({ channel: 'chrome', headless: HEADLESS, args: ['--disable-renderer-backgrounding', '--disable-background-timer-throttling', '--enable-precise-memory-info'] });
  const ctx = await browser.newContext(MOBILE
    ? { viewport: { width: 390, height: 844 }, deviceScaleFactor: 3, isMobile: true, hasTouch: true, userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36' }
    : { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 });
  const page = await ctx.newPage();
  const cdp = await ctx.newCDPSession(page);
  const logs = [], errors = [], net = {}; let state = 'none'; const stats = [];
  page.on('console', m => {
    const t = m.text(); logs.push(m.type() + ': ' + t);
    if (m.type() === 'error') errors.push(t);
    const s = /WEB_STATE (\w+)/.exec(t); if (s) { state = s[1]; console.log(`[${((Date.now() - t0) / 1000).toFixed(1)}s] state ${state}`); }
    if (t.includes('WEB_STAT')) { stats.push(t); console.log(t.slice(t.indexOf('WEB_STAT'))); }
  });
  page.on('pageerror', e => { errors.push('pageerror: ' + e.message); });
  page.on('response', async r => { try { const b = await r.body(); net[r.url().replace(url, '')] = b.length; } catch (e) { } });
  const t0 = Date.now();
  await page.goto(url);
  await page.waitForFunction(() => window.unityInstance, null, { timeout: 180000 });
  const ready = await page.evaluate(() => ({ nav: performance.timeOrigin, start: window.vbStart, ready: window.vbReady }));
  for (let i = 0; i < 300 && state !== 'MainMenu'; i++) await sleep(200);
  const tMenu = Date.now() - t0;
  console.log(`ready ${(ready.ready).toFixed(0)} ms after navigation, menu after ${tMenu} ms`);

  // рамка кадров (rAF) и память кучи wasm, с отметками
  await page.evaluate(() => {
    window.vbF = []; window.vbMem = 0; let last = performance.now();
    const loop = t => { window.vbF.push(t - last); last = t; requestAnimationFrame(loop); }; requestAnimationFrame(loop);
    setInterval(() => { try { const m = window.unityInstance.Module; const h = (m.HEAPU8 || m.HEAP8).length; if (h > window.vbMem) window.vbMem = h; } catch (e) { } }, 250);
  });
  const press = id => page.evaluate(id => window.unityInstance.SendMessage('GameRoot', 'Press', id), id);
  await sleep(1500);
  await page.screenshot({ path: path.join(OUT, '01-menu-portrait.png') });

  // меню -> быстрый бой
  const route = { MainMenu: 'menu.play', ModeSelect: 'mode.' + opt('--mode', 'quick'), Roster: 'roster.fight' };
  for (let i = 0; i < 60 && state !== 'Fight'; i++) { if (route[state]) await press(route[state]); await sleep(500); }
  if (state !== 'Fight') { console.log('FAIL: бой не начался, состояние ' + state); }
  await sleep(2500);
  await page.evaluate(() => { window.vbF = []; });
  await page.screenshot({ path: path.join(OUT, '02-fight-start.png') });

  const vp = page.viewportSize();
  async function touchSwipe(x0, y0, x1, y1, ms) {
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: x0, y: y0 }] });
    const n = 3; for (let k = 1; k <= n; k++) { await sleep(ms / n); await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: x0 + (x1 - x0) * k / n, y: y0 + (y1 - y0) * k / n }] }); }
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  }
  async function swipe(x0, y0, x1, y1, ms) {
    if (MOBILE) return touchSwipe(x0, y0, x1, y1, ms);
    await page.mouse.move(x0, y0); await page.mouse.down();
    const n = 3; for (let k = 1; k <= n; k++) { await sleep(ms / n); await page.mouse.move(x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n); }
    await page.mouse.up();
  }
  // удары: джебы (тап), хуки (быстрый свайп вбок), апперкоты (быстрый свайп вверх) — тяжёлые
  const W = vp.width, H = vp.height; let n = 0, shot3 = false; const tf = Date.now();
  while ((Date.now() - tf) / 1000 < MAXSEC) {
    if (state === 'Fight') {
      const k = n % 5;
      if (k === 0 || k === 3) await swipe(W * (k ? 0.35 : 0.65), H * 0.45, W * (k ? 0.36 : 0.64), H * 0.45, 30);
      else if (k === 1) await swipe(W * 0.15, H * 0.5, W * 0.85, H * 0.42, 60);
      else if (k === 2) await swipe(W * 0.5, H * 0.75, W * 0.52, H * 0.3, 60);
      else await swipe(W * 0.85, H * 0.5, W * 0.15, H * 0.42, 60);
      n++;
      if (n === HITS && !shot3) { shot3 = true; await sleep(400); await page.screenshot({ path: path.join(OUT, '03-head-damaged.png') }); }
      await sleep(420);
    } else if (state === 'Pause') { await press('pause.resume'); await sleep(300); }
    else if (state === 'Loading') await sleep(300);
    else break;   // Victory / Result / Reward — развязка
  }
  const fightSec = (Date.now() - tf) / 1000;
  console.log(`punches ${n}, fight ${fightSec.toFixed(0)} s, end state ${state}`);
  await sleep(2500);
  await page.screenshot({ path: path.join(OUT, '04-outcome.png') });
  for (let i = 0; i < 20 && state !== 'Result' && state !== 'Reward'; i++) await sleep(500);
  await page.screenshot({ path: path.join(OUT, '05-result.png') });

  const fr = await page.evaluate(() => ({ f: window.vbF, mem: window.vbMem, js: performance.memory ? performance.memory.usedJSHeapSize : 0 }));
  // ландшафт: главное меню и бой в ландшафте
  await page.setViewportSize({ width: 844, height: 390 });
  for (let i = 0; i < 20 && state !== 'MainMenu'; i++) { await press({ Result: 'result.menu', Reward: 'reward.menu', Victory: 'result.menu' }[state] || 'reward.menu'); await sleep(500); }
  await sleep(1000); await page.screenshot({ path: path.join(OUT, '06-menu-landscape.png') });
  for (let i = 0; i < 60 && state !== 'Fight'; i++) { if (route[state]) await press(route[state]); await sleep(500); }
  await sleep(2500);
  for (let i = 0; i < 12 && state === 'Fight'; i++) { await swipe(844 * 0.5, 390 * 0.8, 844 * 0.52, 390 * 0.2, 50); await sleep(420); }
  await sleep(400); await page.screenshot({ path: path.join(OUT, '07-fight-landscape.png') });
  const mem2 = await page.evaluate(() => window.vbMem);

  const f = fr.f.slice(1), sum = f.reduce((a, b) => a + b, 0);
  const files = Object.entries(net).filter(([k]) => k.startsWith('Build/'));
  const res = {
    url, mobile: MOBILE, headless: HEADLESS, ua: await page.evaluate(() => navigator.userAgent),
    gpu: await page.evaluate(() => { const c = document.createElement('canvas').getContext('webgl2'); const d = c && c.getExtension('WEBGL_debug_renderer_info'); return d ? c.getParameter(d.UNMASKED_RENDERER_WEBGL) : 'unknown'; }),
    download: Object.fromEntries(files), downloadTotal: files.reduce((a, [, v]) => a + v, 0),
    readyMs: Math.round(ready.ready), menuMs: tMenu, punches: n, fightSec: Math.round(fightSec), endState: state,
    raf: { frames: f.length, avgMs: +(sum / Math.max(1, f.length)).toFixed(2), p50: +pct(f, 0.5).toFixed(1), p99: +pct(f, 0.99).toFixed(1), worst: +Math.max(0, ...f).toFixed(1), over33: f.filter(x => x > 33.4).length, over50: f.filter(x => x > 50).length },
    wasmHeapPeakMB: Math.round(Math.max(fr.mem, mem2) / 1048576), jsHeapMB: Math.round(fr.js / 1048576),
    unityStats: stats, errors,
  };
  fs.writeFileSync(path.join(OUT, 'result.json'), JSON.stringify(res, null, 1));
  fs.writeFileSync(path.join(OUT, 'console.log'), logs.join('\n'));
  console.log(JSON.stringify({ ...res, unityStats: stats.length, errors: errors.length }, null, 1));
  await browser.close(); if (server) server.close();
})().catch(e => { console.error('FAIL', e); process.exit(1); });
