# Листы «0- без правок части 10 | 1- с ними» из снимков AnatShotsB10 и игровых (fight-*): python sheet.py
from PIL import Image, ImageDraw, ImageFont
import os
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 22)
rows = {'sheet-blood': ['blood40-front', 'blood40-3q', 'blood70-front', 'blood70-3q', 'blood70-fight'],
        'sheet-stump': ['stump-3q', 'stump-top', 'stump-close'],
        'sheet-lefort': ['lefort3-3q', 'lefort3-3q-close', 'lefort3-fight', 'lefort2-front', 'lefort2-front-close', 'lefort2-fight'],
        'sheet-game': ['game-p', 'game-l']}
for out, names in rows.items():
    ims = [(Image.open('0-' + n + '.png'), Image.open('1-' + n + '.png'), n) for n in names if os.path.exists('1-' + n + '.png')]
    if not ims: continue
    W = 450; Hs = [int(a.size[1] * W / a.size[0]) for a, _, _ in ims]
    S = Image.new('RGB', (W * 2, sum(Hs)), (30, 30, 34)); d = ImageDraw.Draw(S); y = 0
    for (a, b, n), H in zip(ims, Hs):
        S.paste(a.resize((W, H)), (0, y)); S.paste(b.resize((W, H)), (W, y))
        d.text((6, y + 4), '0 ' + n + ': без части 10', fill=(255, 255, 255), font=font); d.text((W + 6, y + 4), '1 ' + n + ': с частью 10', fill=(255, 255, 255), font=font)
        y += H
    S.save(out + '.png')
