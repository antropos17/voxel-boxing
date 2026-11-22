/* Читаемость боя: сколько кадра вокруг соперника закрывают летящие частицы крови сразу после тяжёлых ударов.
   node tests/readable.js [метка]   — снимки shots/read-<метка>-*.png, итог в консоль.
   Доля считается в прямоугольнике вокруг головы и корпуса: «кровь» — красные пиксели (r>55, r>2.2g, r>2.2b).
   «закрыто» — доля пикселей, которые меняются, если в том же кадре спрятать кубики (кровь, крошки, искры, огонь), минус то же до удара;
   время на паузе через __TS=0. «proj» — расчётная экранная площадь красных кубиков, «px» — длина самого большого. */
const { chromium, LAUNCH, THREE, PAGE } = require('./lib');
const TAG = process.argv[2] || 'run';
const VIEWS = [['port', 390, 844], ['land', 844, 390]];
const EVENTS = [['upper', 'hit:upper:2'], ['hook', 'hit:hook:2'], ['kd', 'adm:kd']];
for (let k = 0; k < 8; k++) EVENTS.push(k % 2 ? ['mhook' + k, 'hit:hook:3'] : ['mupper' + k, 'hit:upper:3']);
EVENTS.push(['gut', 'adm:gut'], ['burst', 'adm:burst'], ['mupper9', 'hit:upper:3']);
const DEL = [3, 6, 10, 15, 25, 40, 60]; /* кадры после удара: 0.05 … 1.0 с */
(async () => {
  const browser = await chromium.launch(LAUNCH);
  const out = {};
  let fail = 0;
  for (const [vn, W, H] of VIEWS) {
    const ctx = await browser.newContext({ viewport: { width: W, height: H }, deviceScaleFactor: 1, hasTouch: true, isMobile: true });
    const page = await ctx.newPage();
    const errs = [];
    page.on('pageerror', e => errs.push(e.message));
    /* детерминизм: одинаковый Math.random и игровое время по кадрам (16.7 мс на кадр), ожидания тоже в кадрах */
    await page.addInitScript(() => { let a = 12345; Math.random = () => { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; };
      const raf = window.requestAnimationFrame.bind(window); let ft = 1000; window.__fr = 0; window.requestAnimationFrame = cb => raf(() => { window.__fr++; ft += 1000 / 60; cb(ft); }); });
    const frames = async n => { const t = await page.evaluate(() => window.__fr); await page.waitForFunction(x => window.__fr >= x, t + n, { timeout: 120000 }); };
    await page.route('**/three.min.js', r => r.fulfill({ path: THREE, contentType: 'application/javascript' }));
    await page.route('**/fonts.googleapis.com/**', r => r.abort());
    await page.goto(PAGE);
    await page.evaluate(() => { localStorage.setItem('vb_cheat', JSON.stringify({ immortal: true, noKD: true, passive: true, god: true, stam: true, thru: true })); });
    await page.reload();
    await page.waitForFunction(() => !document.getElementById('goStart').disabled, null, { timeout: 60000 });
    await page.click('#goStart'); await frames(24);
    await page.evaluate(() => { __g.G.tipT = 0; __g.F.moveT = 99; __g.F.dzT = 0;
      let s = __g.RIG.root; while (s.parent) s = s.parent; s.traverse(o => { if (o.geometry === __g.cgeo) window.__cubes = o; }); });
    const res = [];
    for (const [en, ev] of EVENTS) {
      await page.evaluate(() => { window.__TS = 1; __g.G.paused = false; });
      await frames(42);
      let t0 = 0, mx = 0, mxAll = 0, big = 0, ar = 0, base = 0;
      for (const d of [0, ...DEL]) {
        if (d) { await frames(d - t0); t0 = d; }
        const r = await page.evaluate(async () => {
          window.__TS = 0; await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
          const h = __g.headScreen(), c = document.querySelector('canvas'), W = c.clientWidth, H = c.clientHeight;
          const x0 = Math.max(0, h[0] - W * .3) | 0, x1 = Math.min(W, h[0] + W * .3) | 0, y0 = Math.max(0, h[1] - H * .14) | 0, y1 = Math.min(H, h[1] + H * .38) | 0;
          /* экранные размеры частиц: длина самой большой и суммарная площадь в прямоугольнике */
          const g = __g.cgeo, P = g.attributes.iPos.array, S = g.attributes.iS.array, C = g.attributes.iC.array, cam = __g.camera, f = H / 2 / Math.tan(cam.fov * Math.PI / 360), v = new __g.T.Vector3();
          let big = 0, area = 0;
          for (let i = 0; i < g.instanceCount; i++) { if (!(C[i * 4] > 2 * C[i * 4 + 1])) continue; v.set(P[i * 3], P[i * 3 + 1], P[i * 3 + 2]); const dd = v.distanceTo(cam.position); v.project(cam); if (v.z > 1 || v.z < -1) continue;
            const sx = (v.x * .5 + .5) * W, sy = (-v.y * .5 + .5) * H, L = Math.max(S[i * 3], S[i * 3 + 1], S[i * 3 + 2]) / dd * f, A = S[i * 3] * S[i * 3 + 2] / dd / dd * f * f;
            if (L > big) big = L; if (sx > x0 && sx < x1 && sy > y0 && sy < y1) area += A; }
          const rect = x1 - x0 > 20 && y1 - y0 > 20 ? [x0, y0, x1, y1] : [W * .2 | 0, H * .2 | 0, W * .8 | 0, H * .7 | 0];
          /* тот же кадр дважды, синхронно: с кубиками и без; сравниваем пиксели */
          let sc = __g.RIG.root; while (sc.parent) sc = sc.parent;
          const k = c.width / W, cx = rect[0] * k | 0, cy = rect[1] * k | 0, cw = (rect[2] - rect[0]) * k | 0, ch = (rect[3] - rect[1]) * k | 0;
          const grab = () => { __g.renderer.render(sc, cam); const t = document.createElement('canvas'); t.width = cw; t.height = ch; t.getContext('2d').drawImage(c, cx, cy, cw, ch, 0, 0, cw, ch); return t.getContext('2d').getImageData(0, 0, cw, ch).data; };
          const A = grab(); window.__cubes.visible = false; const B = grab(); window.__cubes.visible = true; grab();
          const red = (d, i) => d[i] > 55 && d[i] > 2.2 * d[i + 1] && d[i] > 2.2 * d[i + 2];
          let ra = 0, rp = 0; const n = A.length / 4;
          for (let i = 0; i < A.length; i += 4) { if (red(A, i)) ra++; if (Math.abs(A[i] - B[i]) + Math.abs(A[i + 1] - B[i + 1]) + Math.abs(A[i + 2] - B[i + 2]) > 60) rp++; }
          return { big, area: area / ((x1 - x0) * (y1 - y0) || 1), cnt: [ra / n, rp / n] };
        });
        if (d === 6 || d === 10) await page.screenshot({ path: `shots/read-${TAG}-${vn}-${en}-${d}.png` });
        const cnt = r.cnt;
        if (!d) { base = cnt[1]; await page.evaluate(() => { window.__TS = 1; });
      const a = ev.split(':');
        if (a[0] === 'adm') await page.evaluate(x => __g.adm(x), a[1]);
        else { /* настоящий свайп, как в gore.js: игрок делает выпад, соперник близко */
          const h = await page.evaluate(() => __g.headScreen()), d = a[1] === 'hook' ? [90, 0] : [0, -80], tx = h[0], ty = h[1] - 30;
          if (a[2] === '3') await page.evaluate(() => __g.megaAdd(100));
          await page.mouse.move(tx - d[0] / 2, ty - d[1] / 2); await page.mouse.down(); await page.mouse.move(tx + d[0] / 2, ty + d[1] / 2, { steps: 2 }); await page.mouse.up();
          await frames(11); }
          continue; }
        if (cnt[1] - base > mx) mx = cnt[1] - base; big = Math.max(big, r.big); ar = Math.max(ar, r.area); if (cnt[0] > mxAll) mxAll = cnt[0];
        await page.evaluate(() => { window.__TS = 1; });
      }
      res.push([en, +(mx * 100).toFixed(1), +(mxAll * 100).toFixed(1), Math.round(big), +(ar * 100).toFixed(1)]);
    }
    /* подписи: каждая разновидность поверх соперника */
    await page.evaluate(() => { window.__TS = 1; const h = __g.headScreen(); __g.impactAt(h[0], h[1], 'upper', -1, 3); });
    await page.waitForTimeout(120);
    await page.evaluate(() => { window.__TS = 0; document.querySelectorAll('.pop').forEach(e => e.remove()); const h = __g.headScreen(), P = document.getElementById('pops');
      [['Апперкот −6', 'gold', 0], ['−24', 'mega', 1], ['!', 'warn', 2], ['Блок', 'pj', 3], ['Мимо', 'miss', 4], ['Блок пробит', '', 5]].forEach(([t, c, k]) => {
        const e = document.createElement('div'); e.className = 'pop' + (c ? ' ' + c : ''); e.textContent = t; e.style.left = (h[0] + (k % 2 ? 60 : -60)) + 'px'; e.style.top = (h[1] - 70 + k * 42) + 'px'; e.style.animationDelay = '-.35s'; e.style.animationPlayState = 'paused'; P.appendChild(e); }); });
    await page.waitForTimeout(150);
    await page.screenshot({ path: `shots/read-${TAG}-${vn}-pops.png` });
    out[vn] = res;
    const pm = Math.max(...res.map(r => r[1])), pb = Math.max(...res.map(r => r[3])), mg = res.filter(r => r[0][0] === 'm'), mm = +(mg.reduce((a, r) => a + r[1], 0) / mg.length).toFixed(1);
    console.log(vn, W + 'x' + H, 'max covered%', pm, 'mega mean covered%', mm, 'max red particle px', pb, '\n [event, covered%, red%, biggest red px, red proj area%]', JSON.stringify(res), errs.length ? 'ERR ' + errs.join(' | ') : 'no errors');
    if (errs.length) fail = 1;
    await ctx.close();
  }
  await browser.close();
  process.exit(fail);
})().catch(e => { console.error('FAIL', e.message.slice(0, 300)); process.exit(1); });
