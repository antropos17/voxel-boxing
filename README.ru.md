# Воксель Бокс

Мобильный воксельный бокс от первого лица. Игра — один файл `dist/index.html`, его можно открыть в браузере или выложить на любой статический хостинг (например, GitHub Pages).

## Быстрый старт

```bash
npm install
npx playwright install chromium   # один раз, нужно только для тестов
npm run build
npm test
```

Поиграть с телефона: `npm run serve`, затем открыть `http://<IP компьютера>:5173` в той же Wi-Fi сети.

## Продолжить в Claude Code

```bash
cd voxel-box
git init && git add -A && git commit -m "v18"
claude
```

Claude Code сам прочитает `CLAUDE.md`. Подробности — в `docs/ARCHITECTURE.md` и `docs/STATUS.md`.

## Структура

- `src/` — исходники (куски одного скрипта, склеиваются `build.js`)
- `dist/index.html` — готовая игра
- `tests/` — тесты на Playwright
- `docs/` — архитектура и текущее состояние
