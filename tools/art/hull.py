# uv run -q --with pillow --with numpy python tools/art/hull.py <raw.png> <out.png> <length_m> <beam_m>
# Plan-view hull sprite: chroma green (or an already-transparent PNG) -> alpha, keep the hull (largest blob and
# anything attached), trim, bow to the right, longest side scaled for crisp zoom 2.5 (4 px/m, capped 512..1024).
# Also writes <out>-rim.png: the painted outer band (alpha = weight) the game recolours for cosmetic hull paint.
import sys, subprocess, tempfile, os
import numpy as np
from PIL import Image, ImageFilter

raw, out, L, B = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4])
im = Image.open(raw)
if im.mode != 'RGBA' or np.asarray(im)[..., 3].min() == 255:
    tmp = tempfile.mktemp(suffix='.png', dir=os.path.dirname(os.path.abspath(raw)))
    subprocess.run(['uv', 'run', '-q', '--with', 'pillow', 'python',
                    os.path.expanduser('~/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py'),
                    '--input', raw, '--out', tmp, '--auto-key', 'border', '--soft-matte',
                    '--transparent-threshold', '12', '--opaque-threshold', '220', '--despill', '--force'], check=True)
    im = Image.open(tmp).convert('RGBA'); os.remove(tmp)
a = np.asarray(im).astype(np.float32)
alpha = a[..., 3]
# Largest connected blob (4-neighbour flood on a coarse mask) keeps stray specks out.
mask = alpha > 40
h, w = mask.shape
lab = np.zeros((h, w), np.int32); cur = 0; sizes = {}
for y0 in range(0, h, 2):
    for x0 in range(0, w, 2):
        if mask[y0, x0] and lab[y0, x0] == 0:
            cur += 1; stack = [(y0, x0)]; lab[y0, x0] = cur; n = 0
            while stack:
                y, x = stack.pop(); n += 1
                for yy, xx in ((y+1, x), (y-1, x), (y, x+1), (y, x-1)):
                    if 0 <= yy < h and 0 <= xx < w and mask[yy, xx] and lab[yy, xx] == 0:
                        lab[yy, xx] = cur; stack.append((yy, xx))
            sizes[cur] = n
keep = max(sizes, key=sizes.get)
blob = lab == keep
# grow the blob by a few px so the soft matte edge stays
from PIL import Image as I2
grown = np.asarray(I2.fromarray((blob * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(7))) > 0
a[..., 3] = np.where(grown, alpha, 0)
ys, xs = np.nonzero(a[..., 3] > 8)
pad = 3
y1, y2, x1, x2 = max(0, ys.min() - pad), min(h, ys.max() + pad + 1), max(0, xs.min() - pad), min(w, xs.max() + pad + 1)
a = a[y1:y2, x1:x2]
img = Image.fromarray(a.clip(0, 255).astype(np.uint8), 'RGBA')
if img.height > img.width:
    img = img.rotate(90, expand=True)
target = int(max(512, min(1024, L * 4 * 2.5 * 1.15)))
s = target / img.width
img = img.resize((target, max(1, round(img.height * s))), Image.LANCZOS)
img.save(out, optimize=True)
# Rim: the painted band inside the outline = pixels whose distance to the transparent edge is under ~9% of the beam.
al = np.asarray(img)[..., 3] > 128
rim_px = max(3, round(img.height * 0.085))
er = I2.fromarray((al * 255).astype(np.uint8))
inner = er
for _ in range(rim_px):
    inner = inner.filter(ImageFilter.MinFilter(3))
inner = np.asarray(inner.filter(ImageFilter.GaussianBlur(1.2))).astype(np.float32) / 255
rim = np.clip(np.asarray(img)[..., 3] / 255.0 - inner, 0, 1)
# ...and only where the art is darker than the honey deck (the deck never takes the paint).
px = np.asarray(img).astype(np.float32) / 255
lum = px[..., 0] * 0.299 + px[..., 1] * 0.587 + px[..., 2] * 0.114
t = np.clip((lum - 0.56) / 0.10, 0, 1)
rim *= 1 - t * t * (3 - 2 * t)
rgba = np.zeros((img.height, img.width, 4), np.uint8); rgba[..., :3] = 255; rgba[..., 3] = (rim * 255).astype(np.uint8)
Image.fromarray(rgba, 'RGBA').save(out.replace('.png', '-rim.png'), optimize=True)
print(f'{out} {img.size} aspect {img.width / img.height:.2f} (sim {L / B:.2f}) rim {rim_px}px')
