# Листы снимков части 5: python sheet.py → sheet-before.png, sheet-after.png и пары pair-*.png (до | после) в этой папке.
import os, sys
from PIL import Image, ImageDraw, ImageFont

D = os.path.dirname(os.path.abspath(__file__))
SHEET = ["f15-front", "f15-3q", "f70-front", "f70-3q", "f40-blood", "1-jaw-front", "2-face-3q", "2-face-crack0",
         "3-vault-crack0", "3-vault-brain-top", "3-vault-brain-close", "4-half-3q", "5-stump-top", "5-stump-close", "5-stump-3q"]
PAIRS = {"1-bruise-swell": ["f15-3q", "f15-bruise", "f70-3q", "f70-bruise"], "2-blood": ["f40-blood", "f40-3q", "1-jaw-front"],
         "3-cracks": ["2-face-crack0", "3-vault-crack0", "4-half-crack0"], "4-brain": ["3-vault-brain-close", "3-vault-brain-top"],
         "5-stump": ["5-stump-close", "5-stump-top"]}
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

for tag in ("before", "after"):
    src = os.path.join(D, tag)
    if not os.path.isdir(src): continue
    grid([tile(os.path.join(src, n + ".png"), 360, tag + " " + n) for n in SHEET], 5).save(os.path.join(D, "sheet-" + tag + ".png"))
if os.path.isdir(os.path.join(D, "after")):
    for name, shots in PAIRS.items():
        ims = []
        for n in shots: ims += [tile(os.path.join(D, "before", n + ".png"), 450, "до " + n), tile(os.path.join(D, "after", n + ".png"), 450, "после " + n)]
        grid(ims, 2).save(os.path.join(D, "pair-" + name + ".png"))
