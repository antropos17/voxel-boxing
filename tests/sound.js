/* Звуки из файлов: страница по http (свой сервер на 127.0.0.1), все файлы манифеста скачаны и декодированы, каждое
   событие sfx.* запускает источник из файла (не синтез), голосов не больше предела при серии из 50 ударов,
   и без файлов (запрос звуков оборван) — тихий откат на синтез без ошибок. */
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium, LAUNCH, THREE, ROOT } = require('./lib');
const DIST = path.join(ROOT, 'dist');
const TYPES = { '.html': 'text/html', '.json': 'application/json', '.webm': 'audio/webm', '.mp3': 'audio/mpeg', '.js': 'application/javascript' };
/* считаем источники: сколько создано, сколько сейчас звучит, сколько осцилляторов (синтез) */
const COUNT = () => {
  const B = AudioBufferSourceNode.prototype, O = AudioContext.prototype, st = B.start, sp = B.stop, osc = O.createOscillator;
  window.__au = { src: 0, osc: 0, live: [], peak: 0 };
  B.start = function (when) {
    const a = window.__au, t = this.context.currentTime, end = this.loop ? 1e9 : Math.max(t, when || 0) + this.buffer.duration / this.playbackRate.value;
    this.__rec = { end }; a.live.push(this.__rec); a.src++;
    a.live = a.live.filter(r => r.end > t); a.peak = Math.max(a.peak, a.live.length);
    return st.apply(this, arguments);
  };
  /* остановленный голос считаем ушедшим сразу: его 60 мс затухания не новый голос */
  B.stop = function (when) { if (this.__rec) this.__rec.end = Math.min(this.__rec.end, this.context.currentTime); return sp.apply(this, arguments) };
  O.createOscillator = function () { window.__au.osc++; return osc.apply(this, arguments) };
};
/* все sfx с типичными аргументами; у каждого должно быть событие в манифесте */
const CALLS = [['jab', [true]], ['jab', [false]], ['jab', [true, true]], ['heavy', [false, 1, true, 'hook']], ['heavy', [true, 1.6, false, 'hook']],
  ['heavy', [false, 1, true, 'upper']], ['heavy', [false, 1, false, 'upper']], ['heavy', [false, 1, true, 'over']], ['heavy', [false, 1, false, 'over']],
  ['heavy', [false, 1, true, 'flurry']], ['heavy', [true]], ['block', []], ['guardBreak', []],
  ...['nose', 'jaw', 'cheekL', 'orbR', 'skull', 'ribL', 'abs', 'arm', undefined].map(z => ['crack', [z]]),
  ['whoosh', []], ['miss', []], ['dodge', []], ['swing', []], ['bell', [1]], ['bell', [3]], ['hurt', [false, 1]], ['hurt', [true, 1.25]], ['hurt', [false, .5]],
  ['step', []], ['tick', []], ['count', []], ['tooth', []], ['tooth', [true]], ['toothFloor', [.7]], ['thud', []], ['drop', []], ['clack', []],
  ['squelch', []], ['gut', []], ['tear', []], ['flay', []], ['jawOff', []], ['eye', []], ['burst', []], ['drip', [.8]], ['ready', []], ['charge', []],
  ['mega', []], ['breath', []], ['cough', []], ['crowd', ['kd']], ['crowd', ['ko']], ['crowd', ['boo']], ['ui', []], ['combo', [5]],
  ['win', []], ['lose', []], ['star', []], ['coin', []]];

function serve() {
  return new Promise(ok => {
    const srv = http.createServer((q, r) => {
      const u = decodeURIComponent(q.url.split('?')[0]), f = path.join(DIST, u === '/' ? 'test.html' : u);
      if (!f.startsWith(DIST) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { r.writeHead(404); r.end(); return }
      r.writeHead(200, { 'Content-Type': TYPES[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(r);
    }).listen(0, '127.0.0.1', () => ok(srv));
  });
}
async function open(browser, base, block) {
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true });
  const page = await ctx.newPage(), errs = [];
  page.on('pageerror', e => errs.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error' && !/fonts\.g|ERR_FAILED|status of 404/.test(m.text())) errs.push('console: ' + m.text()) });
  await page.addInitScript(COUNT);
  await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
  await page.route('**/fonts.googleapis.com/**', r => r.abort());
  if (block) await page.route('**/sounds/**', r => r.abort());
  await page.goto(base + '/test.html');
  await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 90000 });
  return { page, errs };
}
(async () => {
  const srv = await serve(), base = 'http://127.0.0.1:' + srv.address().port, fails = [];
  const browser = await chromium.launch({ ...LAUNCH, args: [...LAUNCH.args, '--autoplay-policy=no-user-gesture-required'] });
  const man = JSON.parse(fs.readFileSync(path.join(DIST, 'sounds', 'manifest.json'), 'utf8')), total = Object.values(man.events).reduce((n, e) => n + e.files.length, 0);
  const { page, errs } = await open(browser, base, false);
  await page.click('#goStart');
  const t0 = Date.now();
  await page.waitForFunction(n => { const S = __g.snd.SND; return Object.keys(S.buf).length + Object.keys(S.bad).length >= n }, total, { timeout: 60000 }).catch(() => {});
  const load = await page.evaluate(() => { const S = __g.snd.SND; return { ext: S.ext, buf: Object.keys(S.buf).length, bad: Object.keys(S.bad), raw: Object.keys(S.raw).length, ac: __g.snd.ac() && __g.snd.ac().state } });
  console.log('loaded', JSON.stringify(load), 'of', total, 'in', Date.now() - t0, 'ms');
  if (load.buf !== total) fails.push('decoded ' + load.buf + ' of ' + total + (load.bad.length ? ', bad: ' + load.bad.join(' ') : ''));
  /* запасной формат для Safari: каждый mp3 тоже декодируется */
  const mp3 = await page.evaluate(async fs => { const ac = __g.snd.ac(), bad = []; for (const f of fs) { try { const b = await (await fetch('sounds/' + f + '.mp3')).arrayBuffer(); await ac.decodeAudioData(b) } catch (e) { bad.push(f) } } return bad }, Object.values(man.events).flatMap(e => e.files));
  console.log('mp3 decoded', total - mp3.length, 'of', total);
  if (mp3.length) fails.push('mp3 not decoded: ' + mp3.join(' '));
  /* фон зала включается, пока идёт бой */
  await page.waitForFunction(() => __g.G.mode === 'fight', null, { timeout: 30000 });
  await page.waitForTimeout(1200);
  if (!(await page.evaluate(() => !!__g.snd.SND.hall))) fails.push('hall loop did not start in fight');
  await page.evaluate(() => { __g.G.paused = true });
  /* каждое событие манифеста играет файлом; каждая функция sfx запускает файл, а не синтез */
  const ev = await page.evaluate(evs => evs.filter(e => e !== 'hall').map(e => { __g.snd.SND.v.splice(0).forEach(x => x.s.stop()); const a = window.__au.src, ok = __g.snd.play(e); return [e, ok && window.__au.src > a] }), Object.keys(man.events));
  ev.filter(x => !x[1]).forEach(x => fails.push('event ' + x[0] + ' did not start a source'));
  const calls = await page.evaluate(cs => cs.map(([n, a]) => {
    const S = __g.snd.SND; S.v.splice(0).forEach(x => x.s.stop()); S.dripT = -9; const p = S.n.play, o = window.__au.osc; __g.snd.sfx[n].apply(null, a);
    return [n + '(' + a.map(String).join(',') + ')', S.n.play > p, window.__au.osc - o];
  }), CALLS);
  calls.filter(c => !c[1] || c[2]).forEach(c => fails.push('sfx.' + c[0] + ': ' + (c[1] ? 'also synth' : 'no file played')));
  const names = await page.evaluate(() => Object.keys(__g.snd.sfx));
  names.filter(n => !CALLS.some(c => c[0] === n)).forEach(n => fails.push('sfx.' + n + ' is not covered by the test'));
  console.log('events', ev.length, 'calls', calls.length, 'sfx functions', names.length);
  /* серия из 50 ударов вперемешку с кровью и хрустом: голосов не больше предела, у события не больше своего */
  await page.waitForTimeout(5000);
  const burst = await page.evaluate(async () => {
    const s = __g.snd, S = s.SND, man = S.man.events, ty = ['hook', 'upper', 'over', 'flurry'];
    S.v.splice(0).forEach(x => x.s.stop()); window.__au.live = window.__au.live.filter(r => r.end > 1e8); window.__au.peak = 0; let worst = 0, perEv = {}, bad = [];
    for (let i = 0; i < 50; i++) {
      s.sfx.heavy(i % 2 === 0, 1 + (i % 5) * .2, i % 3 !== 0, ty[i % 4]); s.sfx.squelch(); if (i % 3 === 0) s.sfx.crack('nose'); if (i % 4 === 0) s.sfx.tooth(); s.sfx.drip(.5);
      worst = Math.max(worst, S.v.length);
      const c = {}; S.v.forEach(x => { c[x.ev] = (c[x.ev] || 0) + 1 }); for (const k in c) { perEv[k] = Math.max(perEv[k] || 0, c[k]); if (c[k] > (man[k].voices || 2)) bad.push(k + ':' + c[k]) }
      await new Promise(r => setTimeout(r, 25));
    }
    return { worst, peak: window.__au.peak, perEv, bad, n: S.n, max: s.MAX };
  });
  console.log('burst', JSON.stringify(burst));
  if (burst.worst > burst.max) fails.push('voices ' + burst.worst + ' > ' + burst.max);
  if (burst.peak > burst.max + 1) fails.push('sounding sources ' + burst.peak + ' > ' + (burst.max + 1) + ' (limit + hall)');
  if (burst.bad.length) fails.push('per-event limit broken: ' + burst.bad.join(' '));
  errs.forEach(e => fails.push(e));
  await page.context().close();
  /* без файлов: запрос звуков оборван — синтез, ошибок нет */
  const nf = await open(browser, base, true);
  await nf.page.click('#goStart'); await nf.page.waitForTimeout(1500);
  const fb = await nf.page.evaluate(() => { const o = window.__au.osc; __g.snd.sfx.jab(); __g.snd.sfx.heavy(true, 1.4, true, 'over'); __g.snd.sfx.bell(1); return { man: !!__g.snd.SND.man, osc: window.__au.osc - o, mode: __g.G.mode } });
  console.log('no files', JSON.stringify(fb));
  if (fb.man || fb.osc < 3) fails.push('fallback to synth did not happen: ' + JSON.stringify(fb));
  nf.errs.forEach(e => fails.push('no files: ' + e));
  await browser.close(); srv.close();
  console.log(fails.length ? 'FAIL\n' + fails.join('\n') : 'sound ok');
  process.exit(fails.length ? 1 : 0);
})().catch(e => { console.error('FAIL', e); process.exit(1) });
