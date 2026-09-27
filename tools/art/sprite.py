# uv run -q --with pillow --with numpy --with scipy python tools/art/sprite.py <raw.png> <out-prefix> [max-side=512] [name,name,...]
# Keys a generated sheet (chroma green, plain white, or an already-transparent PNG), splits it into its separate
# items (connected blobs, row-major order), trims and scales each so its longest side is max-side, and writes
# <out-prefix><name>.png (names given, else 0,1,2...). One item → <out-prefix>.png when no names are given.
import sys, os, subprocess, tempfile
import numpy as np
from PIL import Image
from scipy import ndimage

raw, prefix = sys.argv[1], sys.argv[2]
mx = int(sys.argv[3]) if len(sys.argv) > 3 else 512
names = sys.argv[4].split(',') if len(sys.argv) > 4 else None
im = Image.open(raw).convert('RGBA')
a = np.asarray(im).astype(np.float32)
border = np.concatenate([a[0, :, :3], a[-1, :, :3], a[:, 0, :3], a[:, -1, :3]])
if a[..., 3].min() < 250:
    pass                                                    # already transparent
elif (border[:, 1] > 200).mean() > 0.8 and (border[:, 0] < 90).mean() > 0.8:
    tmp = tempfile.mktemp(suffix='.png', dir=os.path.dirname(os.path.abspath(raw)))
    subprocess.run(['uv', 'run', '-q', '--with', 'pillow', 'python',
                    os.path.expanduser('~/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py'),
                    '--input', raw, '--out', tmp, '--auto-key', 'border', '--soft-matte',
                    '--transparent-threshold', '12', '--opaque-threshold', '220', '--despill', '--force'],
                   check=True, stdout=subprocess.DEVNULL)
    a = np.asarray(Image.open(tmp).convert('RGBA')).astype(np.float32); os.remove(tmp)
else:
    # Plain white paper: flood the near-white region connected to the border, soft edge by brightness.
    rgb = a[..., :3]
    bright = rgb.min(-1)
    sat = rgb.max(-1) - rgb.min(-1)
    white = (bright > 226) & (sat < 28)
    lab, _ = ndimage.label(white)
    edge_ids = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    bg = np.isin(lab, list(edge_ids))
    bg = ndimage.binary_dilation(bg, iterations=1) & (bright > 200) & (sat < 40) | bg
    alpha = np.where(bg, 0, 255).astype(np.float32)
    # soften: pixels next to background get alpha from their darkness
    ring = ndimage.binary_dilation(bg, iterations=2) & ~bg
    alpha[ring] = np.clip((255 - bright[ring]) * 255 / 60, 0, 255)
    a[..., 3] = alpha
mask = a[..., 3] > 40
grown = ndimage.binary_dilation(mask, iterations=6)
lab, n = ndimage.label(grown)
objs = ndimage.find_objects(lab)
sizes = ndimage.sum(mask, lab, range(1, n + 1))
keep = [i for i in range(n) if sizes[i] > sizes.max() * 0.004]
boxes = []
for i in keep:
    sl = objs[i]
    boxes.append((sl[0].start, sl[0].stop, sl[1].start, sl[1].stop, i + 1))
# row-major: sort by row band then x
h = a.shape[0]
boxes.sort(key=lambda b: (round(((b[0] + b[1]) / 2) / (h / 6)), b[2]))
if names and len(names) != len(boxes):
    print(f'warning: {len(boxes)} items found, {len(names)} names given')
for k, (y1, y2, x1, x2, lid) in enumerate(boxes):
    pad = 4
    y1, y2, x1, x2 = max(0, y1 - pad), min(a.shape[0], y2 + pad), max(0, x1 - pad), min(a.shape[1], x2 + pad)
    crop = a[y1:y2, x1:x2].copy()
    crop[..., 3] = np.where(lab[y1:y2, x1:x2] == lid, crop[..., 3], 0)
    img = Image.fromarray(crop.clip(0, 255).astype(np.uint8), 'RGBA')
    s = min(1.0, mx / max(img.size))
    if s < 1: img = img.resize((max(1, round(img.width * s)), max(1, round(img.height * s))), Image.LANCZOS)
    name = names[k] if names and k < len(names) else (str(k) if len(boxes) > 1 else '')
    out = prefix + name + '.png' if name else prefix + '.png'
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    img.save(out, optimize=True)
    print(f'{out} {img.size}')
