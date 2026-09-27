# uv run -q --with pillow python tools/art/sheet.py <dir-or-glob> <out.png> [cell=256]
# Contact sheet of PNGs on the game's parchment (#EEE2CA) so keyed art is judged on the paper it will sit on.
import sys, glob, os
from PIL import Image, ImageDraw
src, out = sys.argv[1], sys.argv[2]
cell = int(sys.argv[3]) if len(sys.argv) > 3 else 256
files = sorted(glob.glob(os.path.join(src, '*.png')) if os.path.isdir(src) else glob.glob(src))
cols = max(1, min(6, len(files))); rows = (len(files) + cols - 1) // cols
sheet = Image.new('RGB', (cols * cell, rows * (cell + 18)), (238, 226, 202))
d = ImageDraw.Draw(sheet)
for k, f in enumerate(files):
    im = Image.open(f).convert('RGBA'); im.thumbnail((cell - 12, cell - 12), Image.LANCZOS)
    x = (k % cols) * cell; y = (k // cols) * (cell + 18)
    sheet.paste(im, (x + (cell - im.width) // 2, y + (cell - im.height) // 2), im)
    d.text((x + 6, y + cell), os.path.basename(f)[:34], fill=(41, 33, 26))
sheet.save(out); print(out, sheet.size, len(files))
