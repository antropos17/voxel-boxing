/* Общая обвязка тестов: Playwright + локальный three.js вместо CDN + путь к dist/test.html.
   CHROME_PATH=/путь/к/chrome — взять системный Chrome вместо скачанного Playwright-ом Chromium. */
const path = require('path'), fs = require('fs'), url = require('url');
const { chromium } = require('playwright');
const ROOT = path.join(__dirname, '..');
process.chdir(ROOT);
fs.mkdirSync(path.join(ROOT, 'shots'), { recursive: true });
const LAUNCH = { args: ['--no-sandbox', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] };
if (process.env.CHROME_PATH) LAUNCH.executablePath = process.env.CHROME_PATH;
const THREE = path.join(path.dirname(require.resolve('three/package.json')), 'build', 'three.min.js');
const PAGE = url.pathToFileURL(path.join(ROOT, 'dist', 'test.html')).href;
if (!fs.existsSync(path.join(ROOT, 'dist', 'test.html'))) { console.error('Нет dist/test.html — сначала: npm run build'); process.exit(1) }
module.exports = { chromium, LAUNCH, THREE, PAGE, ROOT };
