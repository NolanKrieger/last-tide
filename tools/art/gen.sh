#!/bin/bash
# tools/art/gen.sh <out-raw.png> "<subject>" [chroma|full]
# Generates ONE image with Codex's built-in image_gen using the style-bible template (docs/STYLE-BIBLE.md).
#   chroma (default): the subject alone on flat #00FF00 → key it with tools/art/key.sh afterwards.
#   full:             a full-bleed image (textures, paper, key art) with no chroma background.
# ~1–2 min per image. Run at most 3 at once per agent (&, then wait). Always keeps stdin closed (codex eats it).
set -u
OUT="$(realpath -m "$1")"; SUBJ="$2"; MODE="${3:-chroma}"
mkdir -p "$(dirname "$OUT")"
STYLE="Style: an antique treasure-map storybook illustration, black India-ink linework with light watercolour washes; hand-drawn, slightly wobbly confident pen lines, cross-hatching for shade, no gradients, no photo-realism, no 3D render look, no text, no letters, no numbers, no signature, no border, no drop shadow."
if [ "$MODE" = chroma ]; then
  BG="The background must be one perfectly flat, uniform chroma green (#00FF00) with no texture, no shadow and nothing else on it. Keep the whole subject inside the frame with a generous green margin on every side. The subject itself must contain no pure green."
else
  BG="Fill the entire square edge to edge."
fi
PROMPT="Use your built-in image generation tool (image_gen) to create exactly ONE image, then copy the generated PNG file to $OUT (do not resize, crop or re-encode it). Reply with only the saved path. $STYLE $BG Subject: $SUBJ Square 1024x1024."
codex exec --skip-git-repo-check -C "$(dirname "$OUT")" "$PROMPT" < /dev/null > "$OUT.log" 2>&1
if [ -s "$OUT" ]; then echo "ok   $OUT"; else echo "FAIL $OUT (log: $OUT.log)"; exit 1; fi
