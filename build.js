#!/usr/bin/env node
/* Склейка src/* в один HTML. dist/index.html — игра, dist/test.html — она же + тестовый хук (src/x_hook.js). */
const fs = require('fs'), path = require('path');
const SRC = path.join(__dirname, 'src'), OUT = path.join(__dirname, 'dist');
const ORDER = ['a_head.html', 'b_core.js', 'c_vox.js', 'd_models.js', 'e_fx.js', 'f_damage.js', 'f2_teeth.js', 'g_game.js', 'g2_gore.js', 'g3_ragdoll.js', 'h_admin.js', 'y_close.js', 'z_tail.html'];
const read = f => fs.readFileSync(path.join(SRC, f), 'utf8');
const join = list => list.map(read).join('');
fs.mkdirSync(OUT, { recursive: true });
const game = join(ORDER);
const hookAt = ORDER.indexOf('y_close.js');
const test = join(ORDER.slice(0, hookAt).concat(['x_hook.js'], ORDER.slice(hookAt)));
fs.writeFileSync(path.join(OUT, 'index.html'), game);
fs.writeFileSync(path.join(OUT, 'test.html'), test);
for (const [name, html] of [['index.html', game], ['test.html', test]]) {
  const parts = html.split('<script>');
  const js = parts[parts.length - 1].split('</script>')[0];
  try { new Function(js) } catch (e) { console.error('SYNTAX ERROR in ' + name + ': ' + e.message); process.exit(1) }
}
console.log('build ok: dist/index.html ' + (game.length / 1024).toFixed(1) + ' KB, dist/test.html ' + (test.length / 1024).toFixed(1) + ' KB');
