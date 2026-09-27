# Style bible

**Status (2026-09-23):** in force. Nolan asked for the game to "look amazing" and to keep making visual and UI
improvements, so the full art pass was cut from this bible: 14 plan-view hulls with paint masks, monsters as
marginalia, effects, island and port stamps, sea life, paper, the HUD and UI kit, 27 goods / 9 part / 17 achievement
icons, portraits, crests and the title key art — every file logged in `docs/AI-ASSETS.md`. Sails, ensigns and
wakes stay drawn in-engine (they must swing with the wind), which the probes showed reads better from overhead.
Nolan still has the final say: any set can be regenerated from the same template.

## The look in one line
An antique treasure-map storybook: the sea is parchment, everything on it is black India-ink linework with
light watercolour washes, hand-drawn and slightly wobbly, seen from **directly overhead** so it lies flat on
the chart. No gradients, no photo-realism, no perspective, no text baked into art, no drop shadows.

## Palette (from `game/Ink.cs`, already in the build)
| Ink | Hex-ish | Used for |
|---|---|---|
| Paper | #EEE2CA | the sea and every panel |
| Shade | #DACAAA | wells, hover, sketches |
| Black ink | #29211A | outlines, lettering |
| Soft ink | black at 50% | hachures, secondary text |
| Wind blue | #33526F | the wind arrow, pennants |
| Red | #9E291F | danger, hostile, X marks |
| Land wash | #CCB88F | islands |
| Hull umber | #8C6B47 · deck #C7AD80 · sail #F7F2E3 |

Colorblind palette swaps Crown/Brethren inks to Okabe–Ito vermilion/blue (Options); art never carries
faction colour itself — ensigns are drawn in-engine.

## What gets generated (Codex `image_gen`, 1024² PNG on chroma green → keyed to alpha)
| Set | Count | Notes |
|---|---|---|
| Hulls, plan view, bow to the right, no sails | 14 (one per `Hulls.All`) | scaled in-engine to `HullDef.Length` × 4 px/m |
| Sails | 4 rigs × 3 set states (⅓, ⅔, full) + furled | separate layer, mirrored in-engine for the other tack |
| Monsters as chart marginalia | 6 (serpent, kraken, ghost ship, crocodile, weed-kraken, siren rock) + eruption rocks | "here be monsters" drawings that come alive |
| Island stamps | 8 coast textures (palms, hills, mangrove, volcanic, ruins) | tiled/rotated over the procedural polygons |
| Chart ornaments | compass rose, cartouche, rule flourishes, port glyphs (crown, anchor, pennant, cove X) | UI |
| Flotsam | chest, barrel, cannonball, smoke blot, splash ring | effects |

Everything the build draws today is procedural ink (the placeholder art); each generated set replaces one
drawing routine behind the same call, so the game runs with or without any of it.

## The fixed prompt template
```
Use your built-in image generation tool to create ONE image and save it as <path>.
Style: an antique treasure-map storybook illustration, black India-ink linework with light watercolour
washes on plain white; hand-drawn, slightly wobbly lines, no gradients, no photo-realism, no text,
no border, no drop shadow. Background must be a flat solid chroma green (#00FF00).
Subject: <subject line from the table above, always "seen in strict orthographic PLAN VIEW from directly
overhead (bird's-eye map symbol, camera pointing straight down; no perspective, no horizon)">.
Square 1024x1024. Copy the generated file to that exact path and print the path.
```
Learned from the first probe (`scratch/art-probe/sloop-probe.png`): asking for "top-down" alone gave a
side view; the plan-view phrasing above is what the second round of probes uses.

## Pipeline
1. `codex exec --skip-git-repo-check -C <dir> "<prompt>"` per asset (≈1–2 min each; log kept beside the PNG).
2. Key the chroma green to alpha, trim, and normalise scale (hulls by length, sails by mast height).
3. Contact sheet → review → the accepted ones land in `assets/art/<set>/` with a row in `docs/AI-ASSETS.md`.
4. Rejects are regenerated with the template unchanged and only the subject line adjusted.

## Probes for approval (second round, plan view)
`scratch/art-probe2/`: `sloop-plan.png`, `sail-gaff.png`, `kraken-marginalia.png`, `island-stamp.png`.
Open decision for Nolan: approve this look, or change palette/line weight/subjects before ~60 assets are cut.
