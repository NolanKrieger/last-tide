# uv run -q --with pillow --with numpy --with scipy python tools/art/ui_cut.py <raw.png> <outdir> <prefix> [max=256] [dilate=14] [minarea=2500] [ink]
# Keys a batched Codex sheet (chroma green/magenta, or already RGBA), splits it into its separate items by connected
# alpha components (dilated so an item's loose parts stay together), orders them in reading order and writes
# <outdir>/<prefix>-NN.png, each trimmed + padded and scaled so its longest side is <= max.
# "ink" turns white/cream into transparency and keeps only the linework as alpha (for engravings tinted in-engine).
import sys, os, subprocess, tempfile
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

raw, outdir, prefix = sys.argv[1], sys.argv[2], sys.argv[3]
mx = int(sys.argv[4]) if len(sys.argv) > 4 else 256
dil = int(sys.argv[5]) if len(sys.argv) > 5 else 14
minarea = int(sys.argv[6]) if len(sys.argv) > 6 else 2500
ink = len(sys.argv) > 7 and sys.argv[7] == 'ink'
os.makedirs(outdir, exist_ok=True)

im = Image.open(raw)
if im.mode != 'RGBA' or im.getpixel((2, 2))[3] == 255:
    tmp = tempfile.mktemp(suffix='.png')
    subprocess.run(['uv', 'run', '-q', '--with', 'pillow', 'python',
                    os.path.expanduser('~/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py'),
                    '--input', raw, '--out', tmp, '--auto-key', 'border', '--soft-matte',
                    '--transparent-threshold', '12', '--opaque-threshold', '220', '--despill', '--force'], check=True)
    im = Image.open(tmp).convert('RGBA')
    os.remove(tmp)
else:
    # Codex keyed it itself: pull a 2 px fringe in and soften, which removes the coloured halo it leaves.
    im = im.convert('RGBA')
    a = im.getchannel('A').filter(ImageFilter.MinFilter(5)).filter(ImageFilter.GaussianBlur(0.8))
    im.putalpha(a)

arr = np.array(im)
if ink:
    rgb = arr[..., :3].astype(np.float32)
    lum = (0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]) / 255.0
    a = arr[..., 3].astype(np.float32) / 255.0
    inkness = np.clip((0.82 - lum) / 0.62, 0, 1)
    arr[..., 3] = (a * inkness * 255).astype(np.uint8)
    arr[..., 0] = arr[..., 1] = arr[..., 2] = 20
mask = arr[..., 3] > 24
big = ndimage.binary_dilation(mask, iterations=dil)
lab, n = ndimage.label(big)
items = []
for i in range(1, n + 1):
    ys, xs = np.nonzero((lab == i) & mask)
    if len(xs) < minarea: continue
    items.append((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1, xs.mean(), ys.mean()))
# Reading order: rows by centre y (a new row when the centre jumps by more than half an item), then x.
items.sort(key=lambda t: t[5])
rows, cur = [], []
for t in items:
    if cur and t[5] - np.mean([c[5] for c in cur]) > 0.5 * np.median([c[3] - c[1] for c in items]):
        rows.append(cur); cur = []
    cur.append(t)
if cur: rows.append(cur)
ordered = [t for r in rows for t in sorted(r, key=lambda t: t[4])]
out = Image.fromarray(arr)
for k, (x0, y0, x1, y1, _, _) in enumerate(ordered):
    pad = 6
    crop = out.crop((max(0, x0 - pad), max(0, y0 - pad), min(out.width, x1 + pad), min(out.height, y1 + pad)))
    # Drop pixels that belong to a neighbouring item inside this bounding box.
    sub = lab[max(0, y0 - pad):min(out.height, y1 + pad), max(0, x0 - pad):min(out.width, x1 + pad)]
    keep = ndimage.binary_dilation(sub == lab[int((y0 + y1) / 2), int((x0 + x1) / 2)] if lab[int((y0 + y1) / 2), int((x0 + x1) / 2)] else sub > 0, iterations=1)
    ids = np.unique(sub[(sub > 0)])
    if len(ids) > 1:
        # keep the label that owns most of this item's mask
        best = max(ids, key=lambda j: np.sum(sub == j))
        c = np.array(crop); c[..., 3] = np.where(sub == best, c[..., 3], 0); crop = Image.fromarray(c)
    s = min(1.0, mx / max(crop.size))
    if s < 1.0: crop = crop.resize((max(1, round(crop.width * s)), max(1, round(crop.height * s))), Image.LANCZOS)
    path = os.path.join(outdir, f'{prefix}-{k:02d}.png')
    crop.save(path, optimize=True)
    print(path, crop.size)
print(len(ordered), 'items')
