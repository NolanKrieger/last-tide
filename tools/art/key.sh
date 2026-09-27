#!/bin/bash
# tools/art/key.sh <raw-chroma.png> <final.png> [max-side-px=512] [pad-px=6]
# Chroma green → alpha (soft matte + despill), trimmed to the subject plus padding, longest side scaled to max.
set -eu
IN="$(realpath "$1")"; OUT="$(realpath -m "$2")"; MAX="${3:-512}"; PAD="${4:-6}"
mkdir -p "$(dirname "$OUT")"
TMP="$(mktemp --suffix=.png)"
uv run -q --with pillow python "$HOME/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py" \
  --input "$IN" --out "$TMP" --auto-key border --soft-matte --transparent-threshold 12 --opaque-threshold 220 --despill --force
uv run -q --with pillow python - "$TMP" "$OUT" "$MAX" "$PAD" <<'PY'
import sys
from PIL import Image
src, out, mx, pad = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4])
im = Image.open(src).convert('RGBA')
a = im.getchannel('A').point(lambda v: 255 if v > 8 else 0)
bb = a.getbbox()
if bb is None: sys.exit('keyed image is empty: ' + src)
im = im.crop((max(0, bb[0]-pad), max(0, bb[1]-pad), min(im.width, bb[2]+pad), min(im.height, bb[3]+pad)))
s = min(1.0, mx / max(im.size))
if s < 1.0: im = im.resize((max(1, round(im.width*s)), max(1, round(im.height*s))), Image.LANCZOS)
g = sum(1 for p in im.getdata() if p[3] > 200 and p[1] > 200 and p[0] < 90 and p[2] < 90)
im.save(out, optimize=True)
print(f'keyed {out} {im.size} green-fringe px={g}')
PY
rm -f "$TMP"
