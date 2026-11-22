/* Замер производительности (docs/PERF.md). Ничего в игре не меняет, только смотрит через тестовый хук.
   node tests/perf.js [counters|gpu|cpu|load|mem|all] [--hw] [--sw]
     counters — что видеокарта получает на кадр: треугольники и вызовы по объектам, программы, буферы, буфер кадра (не зависит от железа)
     gpu      — время кадра на настоящей видеокарте ПК (системный Chrome, без swiftshader), от разрешения и числа треугольников
     cpu      — время JS кадра при замедлении процессора ×1, ×4, ×6 (CDP), долгие кадры с разбивкой по функциям
     load     — загрузка страницы, первый «В бой», сборка соперника между боями, при ×4
     mem      — память за 5 боёв подряд
   GPU_LUID=0,85962 — выбрать видеокарту (LUID из chrome://gpu), например встроенную вместо дискретной.
   --hw: системный Chrome с аппаратным ускорением (по умолчанию для gpu и cpu), --sw: swiftshader из tests/lib.js.
   Результаты: вывод в консоль и shots/perf-<режим>.json. */
const fs = require('fs'), path = require('path');
const { chromium, LAUNCH, THREE, PAGE, ROOT } = require('./lib');
const MODE = process.argv[2] || 'counters', FLAGS = process.argv.slice(3);
const SYSCHROME = process.env.CHROME_PATH || ['C:/Program Files/Google/Chrome/Application/chrome.exe', '/usr/bin/google-chrome', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'].find(p => fs.existsSync(p));
/* аппаратный запуск: свой GPU-процесс через ANGLE/D3D11 (Windows) без ограничения частоты кадров */
const HWLAUNCH = { executablePath: SYSCHROME, headless: true, args: ['--no-sandbox', '--ignore-gpu-blocklist', '--enable-gpu', '--use-angle=' + (process.platform === 'win32' ? 'd3d11' : 'default'), ...(process.env.GPU_LUID ? ['--use-adapter-luid=' + process.env.GPU_LUID] : []), '--disable-gpu-vsync', '--disable-frame-rate-limit', '--enable-unsafe-webgpu'] };
const CHEATS = { god: true, stam: true, immortal: true, noKD: true, passive: true, thru: true };
const out = {};
const log = (...a) => console.log(...a);
const wait = ms => new Promise(r => setTimeout(r, ms));

async function open(hw, o = {}) {
  if (hw && !SYSCHROME) throw new Error('нет системного Chrome для аппаратного запуска (CHROME_PATH)');
  const browser = await chromium.launch(hw ? HWLAUNCH : LAUNCH);
  const ctx = await browser.newContext({ viewport: o.vp || { width: 390, height: 844 }, deviceScaleFactor: o.dsf || 3, hasTouch: true, isMobile: true });
  const page = await ctx.newPage();
  const errs = [];
  page.on('pageerror', e => errs.push(e.message));
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  await page.addInitScript(() => { window.__lt = []; try { new PerformanceObserver(l => l.getEntries().forEach(e => __lt.push([Math.round(e.startTime), Math.round(e.duration)]))).observe({ entryTypes: ['longtask'] }) } catch (e) { } });
  const cdp = await ctx.newCDPSession(page);
  if (o.cpu) await cdp.send('Emulation.setCPUThrottlingRate', { rate: o.cpu });
  await page.goto(PAGE);
  await page.evaluate(c => { localStorage.clear(); localStorage.setItem('vb_cheat', JSON.stringify(c)) }, o.cheats || CHEATS);
  const t0 = Date.now(); await page.reload();
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 120000 });
  const loadMs = Date.now() - t0;
  return { browser, page, cdp, errs, loadMs };
}
async function start(page) { await page.click('#goStart'); await page.waitForFunction(() => __g.G.mode === 'fight', null, { timeout: 60000 }); await page.evaluate(() => { __g.G.tipT = 0 }); await wait(1200) }
async function swipes(page, n, pause = 300) {
  for (let i = 0; i < n; i++) { const hs = await page.evaluate(() => __g.headScreen()); const d = i % 3 === 2 ? [0, -80] : [i % 2 ? 90 : -90, 0], ty = i % 2 ? -30 : 10;
    await page.mouse.move(hs[0] - d[0] / 2, hs[1] + ty - d[1] / 2); await page.mouse.down(); await page.mouse.move(hs[0] + d[0] / 2, hs[1] + ty + d[1] / 2, { steps: 2 }); await page.mouse.up(); await wait(pause) }
}
const adm = (page, a) => page.evaluate(a => __g.adm(a), a);
/* состояния боя: стойка → серия тяжёлых ударов → нокдаун с рэгдоллом → разрыв головы */
const STATES = [
  ['stance', async p => { }],
  ['heavy', async p => { await swipes(p, 14); for (const a of ['flay', 'eye', 'teeth', 'gut', 'ribs', 'jaw', 'nose']) { await adm(p, a); await wait(250) } await wait(300) }],
  ['ragdoll', async p => { await adm(p, 'kd'); await wait(700) }],
  ['burst', async p => { await adm(p, 'burst'); await wait(500) }],
];

async function counters() {
  const hw = FLAGS.includes('--hw');
  const { browser, page, errs } = await open(hw);
  await start(page);
  out.res0 = await page.evaluate(() => __g.perfRes());
  log('ресурсы после старта', JSON.stringify(out.res0));
  out.states = {};
  for (const [nm, fn] of STATES) {
    await fn(page);
    const r = await page.evaluate(() => ({ cats: __g.perfCats(), info: { calls: __g.renderer.info.render.calls, tris: __g.renderer.info.render.triangles }, mem: __g.perfMem(), res: __g.perfRes() }));
    out.states[nm] = r;
    log(`\n== ${nm}: three ${r.info.tris} tris / ${r.info.calls} calls; наш подсчёт ${r.cats._total.tris} / ${r.cats._total.calls}; программ ${r.res.programs}, буферов ${r.res.buffers} (${r.res.bufferMB} МБ), куски ${r.mem.pieces}, верёвки ${r.mem.ropes}, раны ${r.mem.wounds}, кубики ${r.mem.inst}`);
    for (const [k, v] of Object.entries(r.cats).filter(e => e[0] !== '_total').sort((a, b) => b[1].tris - a[1].tris)) log(`   ${k.padEnd(10)} ${String(v.tris).padStart(7)} tris ${String(v.calls).padStart(4)} calls`);
    await page.screenshot({ path: path.join(ROOT, 'shots', `perf-${nm}.png`) });
  }
  log('ошибки:', errs.length ? errs.join('\n') : 'нет');
  await browser.close();
}

async function gpu() {
  const hw = !FLAGS.includes('--sw');
  const { browser, page, errs } = await open(hw);
  const res = await page.evaluate(() => __g.perfRes());
  log('GPU:', res.gpu, '| буфер кадра', res.dbuf, 'PR', res.pr, 'PRMAX', res.prmax, hw ? '(аппаратно)' : '(swiftshader — время GPU недостоверно)');
  out.gpu = res.gpu; out.hw = hw;
  await start(page); await page.evaluate(() => __g.noAdapt(2));
  out.states = {};
  const VARS = [['полный PR2', {}], ['PR 3 (родное)', { pr: 3 }], ['PR 1', { pr: 1 }], ['PR 0.5', { pr: .5 }], ['без головы и торса', { hide: ['head', 'torso'] }], ['без соперника', { hide: ['head', 'torso', 'legs', 'arms'] }], ['без соперника PR 0.5', { hide: ['head', 'torso', 'legs', 'arms'], pr: .5 }]];
  for (const [nm, fn] of STATES) {
    await fn(page);
    const rows = {};
    for (const [vn, o] of VARS) { await page.evaluate(o => __g.gpuBench(30, o), o); rows[vn] = await page.evaluate(o => __g.gpuBench(80, o), o) }
    out.states[nm] = rows;
    log(`\n== ${nm}`);
    for (const [vn, r] of Object.entries(rows)) log(`   ${vn.padEnd(22)} кадр ${String(r.med).padStart(6)} мс (p90 ${r.p90}), из них JS отправки ${r.submit} мс; ${r.tris} tris ${r.calls} calls, буфер ${r.dbuf}`);
  }
  log('ошибки:', errs.length ? errs.join('\n') : 'нет');
  await browser.close();
}

function dist(ms) {
  const s = ms.slice().sort((a, b) => a - b), q = f => s[Math.min(s.length - 1, (s.length * f) | 0)] || 0, sum = s.reduce((a, b) => a + b, 0);
  return { n: s.length, avg: +(sum / Math.max(1, s.length)).toFixed(2), p50: q(.5), p90: q(.9), p99: q(.99), max: s[s.length - 1] || 0, over16: s.filter(v => v > 16.7).length, over33: s.filter(v => v > 33).length, over50: s.filter(v => v > 50).length };
}
async function cpu() {
  const hw = !FLAGS.includes('--sw');
  out.runs = {};
  for (const rate of [1, 4, 6]) {
    const { browser, page, cdp, errs } = await open(hw, { cpu: rate, dsf: 1 });
    await start(page);
    const run = {};
    /* стойка: 3 с без действий */
    await page.evaluate(() => __prof.startRec(16)); await wait(3000); run.stance = await page.evaluate(() => __prof.stopRec());
    /* бой: удары и тяжёлые травмы */
    await page.evaluate(() => { __prof.reset(); __prof.startRec(16) });
    await swipes(page, 16, 250);
    for (const a of ['flay', 'eye', 'jawoff', 'gut', 'teeth', 'ribs', 'kd']) { await adm(page, a); await wait(500) }
    await wait(1500);
    run.fight = await page.evaluate(() => __prof.stopRec());
    run.fightProf = await page.evaluate(() => { const r = __prof.report(); delete r._info; return r });
    await page.evaluate(() => { __prof.reset(); __prof.startRec(16) }); await adm(page, 'burst'); await wait(2500);
    run.burst = await page.evaluate(() => __prof.stopRec());
    run.burstProf = await page.evaluate(() => { const r = __prof.report(); delete r._info; return r });
    const sum = {};
    for (const k of ['stance', 'fight', 'burst']) { sum[k] = dist(run[k].ms); sum[k].slow = run[k].slow.sort((a, b) => b[0] - a[0]).slice(0, 8) }
    out.runs['x' + rate] = { sum, fightProf: run.fightProf, burstProf: run.burstProf, errs };
    log(`\n==== CPU ×${rate} ${hw ? '(аппаратная графика)' : '(swiftshader)'}`);
    for (const k of ['stance', 'fight', 'burst']) { const d = sum[k]; log(`  ${k.padEnd(7)} кадров ${d.n}: ср ${d.avg} p50 ${d.p50} p90 ${d.p90} p99 ${d.p99} max ${d.max} мс; >16.7: ${d.over16}, >33: ${d.over33}, >50: ${d.over50}`);
      d.slow.forEach(s => log(`      ${s[0]} мс [${s[1]}/${s[2]}] ${s[3]}`)) }
    const top = Object.entries(run.fightProf).filter(e => e[0][0] !== '_').sort((a, b) => b[1][0] - a[1][0]).slice(0, 12).map(e => `${e[0]} ${e[1][0]}мс/к max ${e[1][2]}`).join(' | ');
    log('  среднее на кадр в бою:', top);
    await browser.close();
  }
}

async function load() {
  const hw = !FLAGS.includes('--sw'), rate = +(FLAGS.find(f => /^x\d/.test(f)) || 'x4').slice(1);
  const { browser, page, errs, loadMs } = await open(hw, { cpu: rate, dsf: 3, cheats: { god: true } });
  const lt = await page.evaluate(() => __lt.slice());
  const lp = await page.evaluate(() => { const r = __prof.report(); return Object.entries(r).filter(e => e[0][0] !== '_' && e[1][2] > 5).map(e => e[0] + ' ' + e[1][2] + 'мс ×' + e[1][1]).join(', ') });
  log(`==== загрузка при CPU ×${rate} ${hw ? '(аппаратно)' : '(swiftshader)'}: до «В бой» ${loadMs} мс; долгие задачи: ${lt.filter(e => e[1] > 50).map(e => e[1]).join(', ')}`);
  log('  функции дольше 5 мс:', lp);
  out.load = { rate, loadMs, longtasks: lt, fns: lp };
  const meas = async (sel) => page.evaluate(sel => new Promise(res => {
    const n0 = __lt.length, t0 = performance.now(); __prof.reset(); __prof.startRec(16);
    document.querySelector(sel).click();
    (function poll() { if (__g.G.mode === 'fight' && performance.now() - t0 > 50) { setTimeout(() => { const r = __prof.stopRec(); const pr = __prof.report(); res({ total: Math.round(performance.now() - t0), tasks: __lt.slice(n0).map(e => e[1]), firstFrames: r.ms.slice(0, 12), slow: r.slow.slice(0, 6), fns: Object.entries(pr).filter(e => e[0][0] !== '_' && e[1][2] > 5).map(e => e[0] + ' ' + e[1][2]).join(', ') }) }, 1500) } else setTimeout(poll, 4) })();
  }), sel);
  const first = await meas('#goStart');
  log(`  первый «В бой»: до гонга+1.5с ${first.total} мс, долгие задачи ${first.tasks.join(', ')}; первые кадры ${first.firstFrames.join(' ')}`);
  log('    долгие кадры:', JSON.stringify(first.slow), '\n    функции:', first.fns);
  out.first = first;
  out.next = [];
  for (const a of ['burst', 'jawoff']) {
    await wait(600); await adm(page, a);
    await page.waitForFunction(() => { const b = document.getElementById('goNext'); return b && b.offsetParent !== null }, null, { timeout: 60000 });
    const r = await meas('#goNext'); out.next.push(r);
    log(`  следующий бой после ${a}: ${r.total} мс, задачи ${r.tasks.join(', ')}; первые кадры ${r.firstFrames.join(' ')}\n    функции: ${r.fns}`);
  }
  log('ошибки:', errs.length ? errs.join('\n') : 'нет');
  await browser.close();
}

async function mem() {
  const hw = !FLAGS.includes('--sw');
  const { browser, page, cdp, errs } = await open(hw, { dsf: 1, cheats: { god: true, passive: true } });
  out.fights = [];
  for (let f = 1; f <= 5; f++) {
    if (f === 1) await start(page); else { await page.click('#goNext'); await page.waitForFunction(() => __g.G.mode === 'fight', null, { timeout: 60000 }); await wait(1200) }
    await swipes(page, 10, 200);
    for (const a of ['flay', 'gut', 'eye', 'jawoff']) { await adm(page, a); await wait(300) }
    const mid = await page.evaluate(() => __g.perfMem());
    await adm(page, 'burst');
    await page.waitForFunction(() => { const b = document.getElementById('goNext'); return b && b.offsetParent !== null }, null, { timeout: 60000 });
    await cdp.send('HeapProfiler.collectGarbage');
    const end = await page.evaluate(() => __g.perfMem()), res = await page.evaluate(() => __g.perfRes());
    out.fights.push({ f, mid, end, bufferMB: res.bufferMB, buffers: res.buffers });
    log(`бой ${f}: в бою куски ${mid.pieces} верёвки ${mid.ropes} геометрий ${mid.geometries}; после боя и GC: куча ${end.heapMB} МБ, воксели ${end.voxMB} МБ (${end.voxArrays} массивов), геометрий ${end.geometries}, текстур ${end.textures}, программ ${end.programs}, буферов в сцене ${res.buffers} (${res.bufferMB} МБ), куски ${end.pieces}`);
  }
  log('ошибки:', errs.length ? errs.join('\n') : 'нет');
  await browser.close();
}

(async () => {
  const modes = MODE === 'all' ? ['counters', 'gpu', 'cpu', 'load', 'mem'] : [MODE];
  for (const m of modes) { for (const k in out) delete out[k]; await ({ counters, gpu, cpu, load, mem })[m](); fs.writeFileSync(path.join(ROOT, 'shots', `perf-${m}.json`), JSON.stringify(out, null, 1)) }
})().catch(e => { console.error('FAIL', e.message.slice(0, 600)); process.exit(1) });
