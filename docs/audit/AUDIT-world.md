# AUDIT-world — the world as a hand-coloured chart come alive (sea, islands, ports, fog, weather, life)

Agent: `world` · branch `world` · copy `~/.cache/lt-audit/world/` · merged `game-core` (7113d7b) at 39c84c2.

Owned: `game/SeaLayer.cs` + `assets/shaders/sea.gdshader`, `game/ChartView.cs` (ChartView, ChartChunk, MarksView),
`game/FogView.cs` + `fog.gdshader`, `game/WeatherView.cs` + `weather.gdshader`, ambient-life views,
`assets/art/{land,ports,decor,paper,life}`.
New files (mine): `game/CoastField.cs` (per-map coast distance field + region tables), `game/InkMesh.cs` (batched
anti-aliased ink strokes + stamp meshes), `game/LifeView.cs` (gulls, dolphins, fish, whale, weed, smoke, region
lettering), `game/WorldArt.cs` (generated atlas regions), `assets/shaders/{world.gdshaderinc, ink, storm, sprites,
stamp_mul}.gdshader`, `tools/art/{cut.py, atlas.py, paper.py, noise.py}`, `docs/ai-assets/world.md`.
Shared-file edits (small, local): `game/Main.cs` (build the CoastField once per voyage, pass it to sea/chart/marks/
weather, add LifeView, bind the reveal texture to the sea), `game/Main.SelfTest.cs` (7 checks added, one merge
conflict resolved keeping both sides). No sim changes, no saved-state changes, no new strings, no balance changes.

Status: DONE (last commit on branch `world`, see git log).

## Numbers
| Measure (`vrun.sh … --seed=7 --sail=3 --fps --frames=600`) | before | after |
|---|---|---|
| Draw calls at sea | **219** | **100** |
| Draw calls `--fleet=40` | **265** | **121** |
| Process time avg (CPU) | 0.29 ms | 0.76 ms (wind-grid sampling, fog bloom upload, life); spikes >5 ms: 1 (startup) |
| iGPU render time (relative only, never FPS) | 9.9 ms | 12.7–15 ms at 1600×900, almost all the sea pass (`--off=sea` → 2 ms) |

Chart chunks: all 30 build at the start, on-screen ones at once then one a frame (4–11 ms each, the first 38 ms with
JIT); the coast field builds once per voyage in ~230 ms (while the title or a fresh voyage opens).

## Findings (bugs in my files, fixed with evidence)
| ID | Sev | Where | What was wrong | Evidence | Fix | Status |
|---|---|---|---|---|---|---|
| W-01 | major | game/SeaLayer.cs:29 (old), sea.gdshader `gust()` | The sea's gust patches were meant to be the sim's gust field but were sampled on the render clock (`renderTime`: 0 at every view build, stops in menus) while `WindField.Gust` runs on `World.Time`: after a resume, `--time` or any menu the drawn gusts were a different noise slice from the sim's. | `sea.Update(..., renderTime, ...)` vs `Wind.Gust(p, Time)`; `--time=80` starts the shader at t = 0 against sim t = 80. | The sea draws the sim's own `World.WindAt` / `ConditionsAt`, sampled on a world-locked grid (SeaLayer.UpdateGrid): equal by construction, and it now also shows doldrums, regional wind factors and the storm swirl. Self-test "the sea's strokes follow the sim's own wind (grid vs WindAt: 0.1°/0.03 m/s …)". | fixed |
| W-02 | minor | sea.gdshader `strokes()` (old) | Stroke drift was `time × wind_speed × 0.35`: any change of the local wind speed rescaled the whole accumulated offset, so the sea jumped (at t = 500 s a 0.5 m/s gust slid every stroke ~87 m). | Code. | Drift integrated per frame (`drift += dir·speed·0.35·dt`). | fixed |
| W-03 | minor | sea.gdshader `strokes()` (old) | Strokes lived in a frame rotated with the wind about the world origin: a veering wind (σ ≈ 2°/s) swept the pattern sideways at r·ω (≈ 70 m/s 2 km from the origin), the sea "swam". | Code: `p = (dot(w, wind_dir) - drift, dot(w, across))`. | World-aligned cells; each stroke turns about its own centre. | fixed |
| W-04 | minor | sea/weather.gdshader `hash2` (old) | `fract(sin(dot(p,…))·43758)` collapses at large arguments on GL: the ash specks became a regular dot lattice. | `gallery/before/volcanic.png`, lower right. | Integer PCG3D hashes (`world.gdshaderinc`); world-locked flakes that thin with the zoom. | fixed |
| W-05 | minor | game/WeatherView.cs:36 (old) | Night jumped at both twilight boundaries: `Night ? 1 : Dusk·0.5` = 0.5 at 21:59 → 1 at 22:00, and 1 at 05:59 → 0.5 at 06:00. | Code + `World.HourOfDay`. | Continuous twilight: golden (evening) / rose (morning) light next to the day, the blue hour next to the night (`SmoothStep(0.45, 1, Dusk)`). | fixed |
| W-06 | minor | game/ChartView.cs:117 (old DrawPort) | The harbour ring was 24 dashes, each a 7.5° straight chord (~60 px at zoom 1.25): on the open sea they read as stray straight lines. | `gallery/before/sea-start.png`; `--off=` bisect with only the chart on. | Faint dotted ring in the chart + `HarbourGlow`: a slowly turning dashed ring that strengthens as she closes and is full inside (self-test). | fixed |
| W-07 | minor | game/ChartView.cs:189 (old MarksView) | Redraw trigger was a sum of counts (`coves·1000 + pins·7 + …`): same-count changes (a pin removed and another dropped; one wreck salvaged as another is revealed) never redrew. | Code. | Content hash (positions, notes, ids, palette). | fixed |
| W-08 | polish | game/FogView.cs | Newly charted cells switched on at once: "the chart inks itself in" had no motion. | — | Per-cell bloom 0.6–1.6 s, a watercolour tide line, graphite hatching just beyond the known edge (self-test). | fixed |
| W-09 | minor | game/SeaLayer.cs (my first grid) | Resampling 640 cells every 6th frame cost 2.1 ms avg / 8 ms max spikes. | Temporary timer: `GRID avg 2.08 ms max 7.95 ms`. | Toroidal grid: only cells entering the window are sampled, 4 rows refreshed a frame; `--fps` now shows 1 process spike >5 ms (startup). | fixed |
| W-10 | polish | game/WeatherView.cs | The title's backdrop is a fresh world held still at 06:00, the darkest instant of the dawn twilight: the title sat over night (base code: half-indigo). | `gallery/before/title.png`; my first gallery run. | A world that has never ticked and is held still is drawn by day. | fixed |

## Reported, not mine
| ID | Sev | Where | What | Evidence | Status |
|---|---|---|---|---|---|
| R-01 | major | game/Audio.cs:182 (base) | Combat sounds were read from `world.Events` once per render frame, but each tick clears `Events`: with ≥ 2 ticks in a frame, cannon/hit/splash sounds were lost. | `DIAG fire ticks=4 frames=3` + "FAIL a broadside booms (sail_down)" on a slow frame. | fixed in game-core (`Audio.Consume()` per tick), merged here |
| R-02 | minor | game/Main.SelfTest.cs:279 (base) | "the spyglass sees 700 m" aimed while the camera was still settling: 7.2° off at 20 fps (limit 10°). | `DIAG spy dir=-1.446 want=-1.571 … fps=20`. | fixed in game-core (GC-30), merged here |

## Contract note (for the lead)
The brief says "the sea shader mirrors the sim's noise hash for wind — keep that contract". Its purpose (the water
shows exactly the wind the sim blows) is kept and made exact: instead of re-deriving only the gust noise on the GPU
(which also missed regional wind factors, doldrums and storms, and ran on the wrong clock, W-01), `SeaLayer` samples
`World.WindAt` / `ConditionsAt` themselves on a world-locked grid and the shader reads that; the self-test compares
the two at the same instant (0.1° / 0.03 m/s). The mirrored `vnoise` stays in `world.gdshaderinc` for decoration.
Updated the one README row that described the old mechanism; `src/Sim/Noise.cs`'s comment ("the sea shader computes
the same field") is now historical — left alone (sim file, not mine).

## Proposals (other owners)
| ID | Sev | Where | What | Evidence |
|---|---|---|---|---|
| P-01 | polish | game/Hud.cs (hud) | The HUD's region toast ("Mangrove Maze" under the Threat bar) now duplicates the region name the chart letters across the sea on entry. Suggest dropping the toast or making it the small caption while the sea carries the big lettering; either way one of the two. | `gallery/world-2/sea-sailing.png` |

## What the world looks like now
- **Paper**: a seamless procedural rag-paper data texture (`tools/art/paper.py`: tone, fibres, foxing, tooth; seam
  difference 6.26 vs 6.32 between neighbours) sampled world-locked at two rotated scales so it never repeats.
- **Sea**: calligraphic quill strokes (tapered, heavier head, drawn tail → head, fading and reborn) whose density,
  length and speed follow the sim's wind per pixel (gusts darken, doldrums go glassy and sparse, storms swirl);
  whitecap crests in strong wind; swell strokes; bluer deep water; faint regional water tints; portolan rhumb lines
  (32 winds in ink / green / red) from five wind roses printed into the paper; a graduated neatline at the chart edge.
- **Coasts**: from a signed-distance field — a watercolour halo pooling at the shore, dotted shallows, a surf line
  washing in, a second waterline; the coast inked with hand wobble and a hachured fringe longer on the shaded shores.
- **Islands**: regional watercolour land with green washes, pooled edge, tide line and contour rings; 36 pictorial
  stamps scattered deterministically by region (palms and jungle; dunes; pines, crags and heather; mangroves and
  reeds; a volcano cone with rolling smoke, lava rocks and dead trees; columns, colonnades, obelisks, a stone head),
  a landmark at each island's heart.
- **Ports**: an illustrated town per port (size sets the count, faction the buildings: church and customs house for
  the Crown, windmill and warehouses for free ports, shacks, stilt huts and a watchtower for the Brethren; a lighthouse
  at large ports; a plan-view star fort for forts), a plank jetty, the faction flag in `Ink.Faction`, the name in small
  capitals on a paper ribbon with the faction mark, counter-scaled so it reads from zoom 0.3 to 2.5. Coves: a stilt
  hut and a lantern, the name in red italic, a red dashed ring once found; no gulls. `DrawFactionMark` keeps its
  signature (crown with pearls, anchor with flukes, swallow-tailed pennant, fort bar).
- **Fog of war**: blank paper; newly seen water blooms in; a ragged tide line; graphite sketching beyond the edge.
- **Marks**: a brush-stroke treasure X on a pale wash, a dashed dig ring, a dotted trail, "dig here"; rumour areas
  as a red dashed circle sprinkled with "?"; round-headed ink pins with italic notes; wrecks as broken hulls; all
  counter-scaled with the zoom.
- **Weather**: storm cells as a slate wash with log-spiral hatched arms turning the way the sim's storm wind turns;
  slanting rain driven along the wind; forked, inked lightning with a strike ring on every thunderclap (and silent
  distant strikes in cells in view); world-locked mist banks (a haze inside her sight, thick beyond); ash haze with
  drifting flakes and embers; a blotchy indigo night with a flickering lamp pool and star glints on the water;
  golden dusk, blue hour, rose dawn.
- **Life**: gulls wheeling over every harbour by day (with shadows), dolphins and fish leaping, a whale surfacing
  to blow and fluke in the Deep, sargassum mats in the Weed Sea, volcano smoke; region names lettered across the
  sea (the region she enters writes itself out on open, charted water clear of the chart's rings).

Before → after: before = `~/.cache/lt-audit/gallery/before/<name>.png`, after =
`~/.cache/lt-audit/gallery/world-2/<name>.png` (same 28 standard shots). Extra evidence in
`~/.cache/lt-audit/world/scratch/w/`: regions `r-*.png` (zoom 0.45, revealed), zoom `a4-z03.png` / `a2-z2.5.png`,
twilight `w-sheet2.png`, night `w-night.png`, lightning `x-strike*.png`, mist and weed `g-sheet.png`, gulls
`l-start.png`, whale `x-sheet.png`, smoke `x-volcano-crop.png`, marks `t-treasure*.png` / `m-marks.png`, cove
`c-cove.png`, chart edge `x-edge.png`, fog edge `f2-crop.png`, fullscreen 1920×1080 `fs-*.png`.

## Art (Codex image_gen; every sheet logged in `docs/ai-assets/world.md`)
7 sheets → 55 items, 0 green-fringe px after keying: land ×4 (36 stamps), ports ×2 (18), life (9), wind rose (1).
One atlas per set (one draw call per atlas): land 1024², ports / life / decor 1024×512. Procedural: paper, noise.

## Tests run
- `dotnet test tests/Sim.Tests`: **119/119** (sim untouched by me; 119 = base 118 + game-core's test).
- `--selftest`: **PASS, 148 ok** (base 106 + game-core's + my 7: the chart inks the chunks in view first; a
  thunderclap brings a lightning strike; crossing into the Shoals letters its name; newly charted water inks itself
  in; the sea's strokes follow the sim's own wind (grid vs WindAt 0.1°/0.03 m/s); the harbour ring stands out inside
  it; gulls wheel over the harbour by day).

Final evidence with the finished build: `gallery/world-2/` (28/28 captured) + `gallery/world-2-sheet.png`;
`scratch/w/final-fs.png` (1920×1080), `final-z25.png` (zoom 2.5), `final-z03.png` (zoom 0.3, revealed).
