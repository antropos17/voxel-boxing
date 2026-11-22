"""Пересоздаёт набор звуков одной командой.

    X:/Claude/tools/earshot-venv/Scripts/python -I tools/sounds/make.py [событие ...] [--jobs 4] [--ffmpeg PATH]

Каждое событие из spec.py: слои рендерятся earshot (`sounds.render(id, seed)`; одно и то же зерно всегда даёт те же
отсчёты), режутся, фильтруются и сводятся в моно 48 кГц; из n+2 кандидатов берутся n лучших по аудиту earshot
(щелчки, перегруз, обрезанный хвост) и атаке. Пишет:
  assets/sounds/src/<событие>_<k>.wav   исходники без потерь (16 бит, моно, 48 кГц) — для Unity
  assets/sounds/web/<событие>_<k>.webm/.mp3   сжатые копии для веба (build.js копирует их в dist/sounds/)
  assets/sounds/manifest.json            событие -> файлы, громкость, разброс, приоритет, голоса
  assets/sounds/report.json              замеры каждого файла
Без аргументов пересобирает всё; с именами — только их (манифест дописывается).
"""
import json, os, subprocess, sys, argparse
from concurrent.futures import ProcessPoolExecutor
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, 'assets', 'sounds')
SR = 48000
sys.path.insert(0, HERE)
os.environ.setdefault('HF_HUB_OFFLINE', '1')   # earshot make моделей не грузит; на всякий случай без сети


def db(x):
    return 10 ** (x / 20)


def lead_trim(y, rel=-40):
    pk = np.abs(y).max()
    if pk <= 0:
        return y
    i = int(np.argmax(np.abs(y) > pk * db(rel)))
    return y[max(0, i - int(.001 * SR)):]


def fade(y, ms_in=1.0, ms_out=10.0):
    y = y.copy()
    a, b = min(len(y), int(ms_in * SR / 1000)), min(len(y), int(ms_out * SR / 1000))
    if a:
        y[:a] *= np.linspace(0, 1, a)
    if b:
        y[-b:] *= np.linspace(1, 0, b) ** 2
    return y


def filt(y, lp=None, hp=None, loop=False):
    from scipy.signal import butter, sosfiltfilt
    if not lp and not hp:
        return y
    n = len(y)
    x = np.concatenate([y, y, y]) if loop else y      # петля: фильтр по кругу, шов остаётся бесшовным
    if lp:
        x = sosfiltfilt(butter(4, lp, 'low', fs=SR, output='sos'), x)
    if hp:
        x = sosfiltfilt(butter(2, hp, 'high', fs=SR, output='sos'), x)
    return x[n:2 * n] if loop else x


def pitch(y, st):
    if not st:
        return y
    import soxr
    return soxr.resample(y, SR, SR / 2 ** (st / 12))     # быстрее/медленнее вместе с высотой, как плёнка


_cache = {}


def render(sid, seed, axes):
    from earshot import sounds
    k = (sid, seed, json.dumps(axes or {}, sort_keys=True))
    if k not in _cache:
        y, m = sounds.render(sid, seed=seed, **(axes or {}))
        y = np.asarray(y, dtype=np.float64)
        _cache[k] = (y.mean(axis=1) if y.ndim == 2 else y, m)
    return _cache[k]


def layer(L, cand, loop):
    from earshot.synth import master
    y, m = render(L['id'], L.get('seed', 0) + cand, L.get('axes'))
    c = L.get('crop')
    if c:
        if 'peak' in c:
            p = int(np.argmax(np.abs(y)))
            a = max(0, p - int(c['peak'] * SR / 1000))
        else:
            s = c['start']
            s = s[cand % len(s)] if isinstance(s, list) else s
            a = int(s * SR / 1000)
        y = fade(y[a:a + int(c['len'] * SR / 1000)], 1, min(60, c['len'] * .25))
    elif not loop:
        y = lead_trim(y)
    y = filt(pitch(y, L.get('pitch', 0)), L.get('lp'), L.get('hp'), loop)
    if not loop and 'crop' not in L:
        y = lead_trim(y)
    lu = master.loudness(y)
    y = y * db(-20 - lu + L.get('gain', 0))                # каждый слой к -20 LUFS, потом его gain
    at = int(L.get('at', 0) * SR / 1000)
    return np.concatenate([np.zeros(at), y]), m.get('member')


def candidate(ev, cand):
    from earshot.synth import master
    loop = ev.get('loop', False)
    parts = [layer(L, cand, loop) for L in ev['layers']]
    n = max(len(p[0]) for p in parts)
    y = np.zeros(n)
    for p, _ in parts:
        y[:len(p)] += p
    if not loop:
        y = lead_trim(y, -36)
        y = y[:int(ev['max'] * SR)]
        pk = np.abs(y).max()                               # хвост ниже -60 дБ от пика отрезаем
        live = np.nonzero(np.abs(y) > pk * db(-60))[0]
        y = fade(y[:live[-1] + 1 if len(live) else len(y)], 1, min(40, len(y) / SR * 1000 * .2))
    # в динамике телефона ниже ~300 Гц почти ничего нет: тяжёлый удар из одного низа там тише джеба. Если у события
    # задан phone (LUFS после фильтра 300 Гц), добавляем к звуку его же верх параллельно, пока не дотянем
    if ev.get('phone') is not None and not loop:
        from scipy.signal import butter, sosfilt
        sos = butter(4, 300, 'high', fs=SR, output='sos')
        top = sosfilt(sos, y)
        for _ in range(8):
            gap = (ev['phone'] - ev['lufs']) - (master.loudness(sosfilt(sos, y)) - master.loudness(y))
            if gap <= .3:
                break
            y = y + top * (db(min(gap, 9)) - 1)
    # уровень по-earshot: momentary max = цель, истинный пик не выше -1 дБFS; у коротких ударов пик не режется
    # лимитером, а мягко подгибается (master._fold), чтобы атака осталась яркой; ноты интерфейса не подгибаются
    y = master.finish(y, peak_db=-1.0, lufs=ev['lufs'], loop=loop, lift=0.0 if loop else 6.0,
                      tonal=ev['group'] == 'интерфейс', fade_in=0.0005)
    return y, [p[1] for p in parts]


def score(r, ev):
    s = 10 * len(r['warn']) + max(0, r['onset_ms'] - 10) * (0 if ev.get('loop') or ev['group'] in ('толпа', 'интерфейс') or ev['max'] > 1.2 else 1)
    return s + abs(r['lufs'] - ev['lufs'])


def build_event(args):
    name, ev, ffmpeg = args
    from measure import measure
    os.makedirs(os.path.join(OUT, 'src'), exist_ok=True)
    os.makedirs(os.path.join(OUT, 'web'), exist_ok=True)
    tries = ev['n'] + (0 if ev.get('loop') else 2)
    cands = []
    for c in range(tries):
        y, mem = candidate(ev, c)
        r = measure(y, SR)
        if ev.get('loop'):   # у петли края не нулевые нарочно; вместо этого скачок на шве против обычного шага
            r['warn'] = [w for w in r['warn'] if w not in ('edge', 'truncated')]
            d = np.abs(np.diff(y))
            r['seam'] = round(float(abs(y[0] - y[-1]) / (np.percentile(d, 99) + 1e-12)), 2)
        cands.append((score(r, ev), c, y, r, mem))
    best = sorted(cands, key=lambda t: (t[0], t[1]))[:ev['n']]
    best.sort(key=lambda t: t[1])
    import soundfile as sf
    files, rep = [], []
    for k, (sc, c, y, r, mem) in enumerate(best, 1):
        base = f'{name}_{k}'
        wav = os.path.join(OUT, 'src', base + '.wav')
        sf.write(wav, y.astype(np.float32), SR, subtype='PCM_16')
        for ext, args_ in (('webm', ['-c:a', 'libopus', '-b:a', '40k' if ev.get('loop') else '48k', '-vbr', 'on', '-application', 'audio']),
                           ('mp3', ['-c:a', 'libmp3lame', '-b:a', '64k' if ev.get('loop') else '80k'])):
            dst = os.path.join(OUT, 'web', base + '.' + ext)
            subprocess.run([ffmpeg, '-y', '-v', 'error', '-i', wav, '-ac', '1', '-map_metadata', '-1'] + args_ + [dst], check=True)
        files.append(base)
        rep.append(dict(file=base, cand=c, **r, members=[m and f"{m['n']}/{m['of']}" for m in mem]))
    man = dict(files=files, gain=0, pv=ev.get('pv', 1), vv=ev.get('vv', .1), pri=ev.get('pri', 3), voices=ev.get('voices', 2),
               loop=bool(ev.get('loop')), group=ev['group'], desc=ev['desc'], words=ev['words'],
               layers=[{k: v for k, v in L.items()} for L in ev['layers']], lufs=ev['lufs'])
    return name, man, rep


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('names', nargs='*')
    ap.add_argument('--jobs', type=int, default=4)
    ap.add_argument('--ffmpeg', default=os.environ.get('FFMPEG') or os.path.join(os.environ.get('LOCALAPPDATA', ''),
                    'Programs', 'HyperFrames', 'resources', 'app.asar.unpacked', 'vendor', 'ffmpeg', 'win32-x64', 'ffmpeg.exe'))
    a = ap.parse_args()
    from spec import EVENTS
    names = a.names or list(EVENTS)
    mp, rp = os.path.join(OUT, 'manifest.json'), os.path.join(OUT, 'report.json')
    man = json.load(open(mp, encoding='utf-8')) if a.names and os.path.exists(mp) else {'version': 1, 'events': {}}
    rep = json.load(open(rp, encoding='utf-8')) if a.names and os.path.exists(rp) else {}
    if not a.names:   # полная пересборка: старые файлы долой
        for d in ('src', 'web'):
            p = os.path.join(OUT, d)
            if os.path.isdir(p):
                for f in os.listdir(p):
                    os.remove(os.path.join(p, f))
    with ProcessPoolExecutor(a.jobs) as ex:
        for name, m, r in ex.map(build_event, [(n, EVENTS[n], a.ffmpeg) for n in names]):
            man['events'][name] = m
            rep[name] = r
            print(f"{name:12s} " + '  '.join(f"{x['dur']:.2f}s {x['lufs']:+.0f}/{x['phone']:+.0f}LU on{x['onset_ms']:.0f}ms c{x['centroid']} {','.join(x['warn']) or 'ok'}" for x in r), flush=True)
    man['events'] = {k: man['events'][k] for k in EVENTS if k in man['events']}
    json.dump(man, open(mp, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    json.dump(rep, open(rp, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    tot = sum(os.path.getsize(os.path.join(OUT, 'web', f)) for f in os.listdir(os.path.join(OUT, 'web')))
    print(f"web: {tot / 1024:.0f} KB, events {len(man['events'])}, files {sum(len(e['files']) for e in man['events'].values())}")


if __name__ == '__main__':
    main()
