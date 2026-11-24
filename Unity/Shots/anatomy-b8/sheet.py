# Листы снимков части 8: python sheet.py → пары pair-*.png (до | после) и full-lite.png (полный | упрощённый шейдер) в этой папке.
# Кадры: before/ (HEAD до части 8), after/, after-lite/ — AnatShotsB5.Run(tag, root: "Shots/anatomy-b8", more: true[, lite: true]).
import os
from PIL import Image, ImageDraw, ImageFont

D = os.path.dirname(os.path.abspath(__file__))
PAIRS = {"1-swell": ["f15-side", "f15-swell", "f70-side", "f70-swell"], "2-blood": ["f40-3q", "f40-blood", "f70-front", "f70-blood"],
         "3-stump": ["5-stump-3q", "5-stump-top", "5-stump-close"], "0-fight": ["f15-front", "f40-3q", "f70-3q"]}
LITE = ["f40-3q", "f70-bruise", "3-vault-brain-close", "2-face-crack0", "5-stump-close"]
try: FONT = ImageFont.truetype("arial.ttf", 22)
except OSError: FONT = ImageFont.load_default()

def tile(path, w, label):
    im = Image.open(path).convert("RGB"); im = im.resize((w, int(im.height * w / im.width)))
    ImageDraw.Draw(im).text((8, 6), label, fill=(255, 255, 255), font=FONT)
    return im

def grid(ims, cols):
    w, h = ims[0].width, max(i.height for i in ims); rows = (len(ims) + cols - 1) // cols
    out = Image.new("RGB", (w * cols, h * rows), (20, 20, 24))
    for k, im in enumerate(ims): out.paste(im, ((k % cols) * w, (k // cols) * h))
    return out

for name, shots in PAIRS.items():
    ims = []
    for n in shots: ims += [tile(os.path.join(D, "before", n + ".png"), 450, "до " + n), tile(os.path.join(D, "after", n + ".png"), 450, "после " + n)]
    grid(ims, 2).save(os.path.join(D, "pair-" + name + ".png"))
ims = []
for n in LITE: ims += [tile(os.path.join(D, "after", n + ".png"), 450, "полный " + n), tile(os.path.join(D, "after-lite", n + ".png"), 450, "упрощ. " + n)]
grid(ims, 2).save(os.path.join(D, "full-lite.png"))
