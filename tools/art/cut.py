# uv run -q --with pillow --with numpy python tools/art/cut.py <raw.png> <outdir> <name1,name2,...> [max=256] [merge=18]
# Cuts a multi-item sheet (Codex image_gen, "N separate items in a loose grid") into one keyed PNG per item.
# Chroma-green sheets are keyed with the same remover as key.sh; sheets that already carry alpha keep it.
# Items are found as connected alpha components (dilated by <merge> px so grass tufts and splashes stay with
# their subject), ordered in reading order (rows, then left to right) and saved as <outdir>/<name>.png,
# trimmed, longest side scaled to <max>. A name of "-" skips that item.
import os, subprocess, sys, tempfile
import numpy as np
from PIL import Image, ImageFilter

raw, outdir, names = sys.argv[1], sys.argv[2], sys.argv[3].split(',')
mx = int(sys.argv[4]) if len(sys.argv) > 4 else 256
merge = int(sys.argv[5]) if len(sys.argv) > 5 else 18
os.makedirs(outdir, exist_ok=True)

im = Image.open(raw)
if im.mode == 'RGBA' and im.getpixel((2, 2))[3] == 0:
    rgba = im.convert('RGBA')
else:
    tmp = tempfile.mktemp(suffix='.png')
    subprocess.run(['python3', os.path.expanduser('~/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py'),
                    '--input', raw, '--out', tmp, '--auto-key', 'border', '--soft-matte', '--transparent-threshold', '12',
                    '--opaque-threshold', '220', '--despill', '--force'], check=True, stdout=subprocess.DEVNULL)
    rgba = Image.open(tmp).convert('RGBA')
    os.remove(tmp)

a = np.array(rgba.getchannel('A'))
mask = Image.fromarray(((a > 24) * 255).astype(np.uint8))
if merge > 0:
    mask = mask.filter(ImageFilter.MaxFilter(2 * (merge // 2) + 1))
# Label at quarter resolution (fast enough in pure Python), then map back.
s = 4
small = np.array(mask.resize((mask.width // s, mask.height // s), Image.NEAREST)) > 0
lab = np.zeros(small.shape, np.int32)
n = 0
for y0 in range(small.shape[0]):
    for x0 in range(small.shape[1]):
        if small[y0, x0] and lab[y0, x0] == 0:
            n += 1
            stack = [(y0, x0)]
            lab[y0, x0] = n
            while stack:
                y, x = stack.pop()
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < small.shape[0] and 0 <= xx < small.shape[1] and small[yy, xx] and lab[yy, xx] == 0:
                        lab[yy, xx] = n
                        stack.append((yy, xx))
comps = []
for k in range(1, n + 1):
    ys, xs = np.nonzero(lab == k)
    if len(ys) * s * s < 900:   # specks
        continue
    comps.append((k, xs.min() * s, ys.min() * s, (xs.max() + 1) * s, (ys.max() + 1) * s))
# Reading order: group into rows by vertical overlap of centres.
comps.sort(key=lambda c: (c[2] + c[4]) / 2)
rows, cur = [], []
for c in comps:
    cy = (c[2] + c[4]) / 2
    if cur and cy - (cur[0][2] + cur[0][4]) / 2 > (cur[0][4] - cur[0][2]) * 0.6:
        rows.append(cur); cur = []
    cur.append(c)
if cur: rows.append(cur)
ordered = [c for r in rows for c in sorted(r, key=lambda c: c[1])]
print(f'{raw}: {len(ordered)} items for {len(names)} names')
full = np.array(Image.fromarray(lab.astype(np.int32)).resize(rgba.size, Image.NEAREST))
arr = np.array(rgba)
for c, name in zip(ordered, names):
    if name == '-':
        continue
    k, x0, y0, x1, y1 = c
    pad = 6
    x0, y0 = max(0, x0 - pad), max(0, y0 - pad)
    x1, y1 = min(rgba.width, x1 + pad), min(rgba.height, y1 + pad)
    piece = arr[y0:y1, x0:x1].copy()
    other = full[y0:y1, x0:x1]
    piece[(other != k) & (other != 0), 3] = 0          # never carry a neighbour's pixels
    img = Image.fromarray(piece)
    bb = img.getchannel('A').point(lambda v: 255 if v > 8 else 0).getbbox()
    img = img.crop(bb)
    sc = min(1.0, mx / max(img.size))
    if sc < 1.0:
        img = img.resize((max(1, round(img.width * sc)), max(1, round(img.height * sc))), Image.LANCZOS)
    g = int(((np.array(img)[..., 3] > 200) & (np.array(img)[..., 1] > 200) & (np.array(img)[..., 0] < 90) & (np.array(img)[..., 2] < 90)).sum())
    img.save(os.path.join(outdir, name + '.png'), optimize=True)
    print(f'  {name:18s} {img.size} green-fringe={g}')
