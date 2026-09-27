# Art pipeline (Codex image_gen → keyed PNG → assets/art)

1. `tools/art/gen.sh scratch/art-raw/<name>.png "<subject>" [chroma|full]` — one image, ~1 min, the style-bible
   template (docs/STYLE-BIBLE.md) is built in. At most 3 concurrent per agent; stdin stays closed.
   Several small subjects (icons, stamps) can share one image: ask for "N separate items arranged in a loose grid,
   well apart, none touching" and cut them apart by connected alpha components after keying.
2. `tools/art/key.sh scratch/art-raw/<name>.png assets/art/<set>/<name>.png [max-side=512]` — chroma → alpha,
   trim, scale. Check the printed green-fringe count is ~0.
3. `uv run -q --with pillow python tools/art/sheet.py assets/art/<set> scratch/<set>-sheet.png` — judge on parchment.
4. In game: `Art.Tex("<set>/<name>")` (null when absent → keep a procedural fallback). Textures import with mipmaps
   (project.godot `[importer_defaults]`); set `TextureFilter = LinearWithMipmaps` on nodes that draw them scaled down.
5. Log every shipped asset in `docs/AI-ASSETS.md` (Steam disclosure): asset, path, tool, prompt, date.
