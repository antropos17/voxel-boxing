"""Инструментальные замеры звука (слушать некому): длительность, пик, громкость, атака, спектр, аудит earshot."""
import numpy as np

def mono(y):
    y = np.asarray(y, dtype=np.float64)
    return y.mean(axis=1) if y.ndim == 2 else y

def measure(y, sr=48000):
    from earshot.synth import master
    from earshot.forge import sfx
    m = mono(y)
    pk = float(np.abs(m).max()) or 1e-12
    on = int(np.argmax(np.abs(m) > pk * 10 ** (-30 / 20)))        # первая точка в 30 дБ от пика
    spec = np.abs(np.fft.rfft(m * np.hanning(len(m)))) ** 2
    f = np.fft.rfftfreq(len(m), 1 / sr)
    tot = spec.sum() or 1e-12
    cen = float((spec * f).sum() / tot)
    low = float(spec[f < 150].sum() / tot)
    high = float(spec[f > 3000].sum() / tot)
    a = sfx.check(m, sr)
    from scipy.signal import butter, sosfilt
    ph = sosfilt(butter(4, 300, 'high', fs=sr, output='sos'), m)   # что останется в динамике телефона
    return {'dur': round(len(m) / sr, 3), 'peak_db': round(20 * np.log10(pk), 1),
            'lufs': round(float(master.loudness(m)), 1), 'phone': round(float(master.loudness(ph)), 1), 'onset_ms': round(on / sr * 1000, 1),
            'peak_ms': round(int(np.argmax(np.abs(m))) / sr * 1000, 1), 'centroid': int(cen),
            'low': round(low, 2), 'high': round(high, 2),
            'warn': [w['code'] for w in a['warnings']]}
