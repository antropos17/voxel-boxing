# Листы «без переломов | с переломами» из снимков AnatShotsB9 (0-* и 1-*): python sheet.py
from PIL import Image, ImageDraw
import glob, os
rows = {'sheet-nose': ['nose-front', 'nose-3q', 'nose-top'], 'sheet-zygoma': ['zygoma-front', 'zygoma-3q', 'zygoma-below'],
        'sheet-lefort': ['lefort2-front', 'lefort2-side', 'lefort3-3q', 'lefort3-side'], 'sheet-frontal': ['frontal-front', 'frontal-3q', 'frontal-side'],
        'sheet-fight': ['fight-front', 'fight-3q', 'fight-lite-front', 'fight-lite-3q', 'fight-lite-close']}
for out, names in rows.items():
    ims = [(Image.open('0-' + n + '.png'), Image.open('1-' + n + '.png'), n) for n in names if os.path.exists('1-' + n + '.png')]
    w, h = ims[0][0].size; s = 0.5
    W, H = int(w * s), int(h * s)
    S = Image.new('RGB', (W * 2, H * len(ims)), (30, 30, 34)); d = ImageDraw.Draw(S)
    for r, (a, b, n) in enumerate(ims):
        S.paste(a.resize((W, H)), (0, r * H)); S.paste(b.resize((W, H)), (W, r * H))
        d.text((6, r * H + 4), n + ': без переломов', fill=(255, 255, 255)); d.text((W + 6, r * H + 4), n + ': с переломами §5', fill=(255, 255, 255))
    S.save(out + '.png')
