# AUDIT — art-ships (visual agent: everything that floats)

Owner files: `game/ShipView.cs` (+ new `game/ShipView.Rig.cs`), `game/FleetView.cs`, `game/WakeView.cs`,
`game/MonsterView.cs`, `game/EffectsView.cs`; new helpers `game/InkBatch.cs` (one-draw-call triangle batcher; was InkMesh),
`game/PaintLayer.cs` (a z-ordered child drawing layer; was InkLayer), `game/ShipArt.cs` (hull art + runtime paint), `game/FxAtlas.cs`
(generated UV table); art `assets/art/{ships,monsters,fx}`; pipeline `tools/art/{hull.py,sprite.py,fxatlas.py}`.
AI asset log: `docs/ai-assets/art-ships.md`. Status: DONE (2026-09-26).

Screenshots referenced below live in this copy under `scratch/shots/` (git-ignored). BEFORE = the lead's gallery
`~/.cache/lt-audit/gallery/before/*.png` plus my per-hull captures in `scratch/shots/before/`.

## Baseline (before)
- Draw calls (runner, frame 600): `--seed=7 --sail=3 --fps --frames=600` → **219**; `--fleet=40` → **265**.
- Ships were thin procedural polygons with one Bézier per sail (`before/sloop-z25.png`, `before/frigate-z25.png`);
  monsters were mostly invisible (`gallery/before/{serpent,siren,weed,croc,ghost}.png`); smoke = grey circles.

## Changes (visual)

| ID | Area | What changed | Before → After |
|---|---|---|---|
| V1 | Hulls | 14 plan-view ink-and-watercolour hulls (Codex image_gen, bow +X, no rig), keyed/trimmed/scaled by `tools/art/hull.py` to ≥512 px long (up to 713 px for the man-o'-war) → crisp at zoom 2.5, mipmapped (`LinearWithMipmaps`) for zoom 0.3. Drawn at the sim's Length × (blend of Beam and art aspect). Soft blue water wash under each hull. Procedural hull kept as fallback. | `before/frigate-z25.png` → `hulls-z25-sheet.png`, `hulls-z125-sheet.png`, `hulls-z04-sheet.png` |
| V2 | Hull paint | Each hull ships a `-rim.png` mask of its painted outer band; `ShipArt.Painted` recolours only that band (luminance-preserving) for cosmetic hull colours (black/green/red) and faction colours at sea (Crown: red-umber, Brethren: dark teal, traders: plain). Deck, planks and ink survive. | `scratch/tintcheck.png`, `m2/cosm-crop.png` |
| V3 | Hit flash | Red flash now overlays the hull art (tinted texture pass), water in the hold darkens the hull blue, and she lists while foundering. | `e2/fx-z25-crop.png` |
| V4 | Sails | New sail layer (`ShipView.Rig.cs`): per-hull rig plans (square tiers course/topsail/topgallant, gaff main+topsail, jibs on stays, lateen yards, spanker, spritsail). Sail level reads by how many sails are set: ⅓ = topsails / reefed main + one jib, ⅔ = + courses / full main + jibs, full = + topgallants / gaff topsail. Square yards brace round, booms/gaffs/lateens sheet out to leeward, all eased toward the **apparent** wind so they swing across on a tack; canvas luffs (flutters) near/inside irons and backs slightly head-to-wind; cloth is a 3-row watercolour band (shade at the spar, light mid, pooled edge) with seams, a reef band, a heavy belly ink line, furled rolls with gaskets; belly-and-settle snap on every sail change; torn sails show rips; sail cosmetics (ochre/indigo/stripes) apply. Sails swell slightly when a ship is small on screen so a far ship still reads. | `m6/sweep-sloop.png`, `m6/sweep-frig.png`, `m6/brig-crops.png` (8 headings round the wind) |
| V5 | Flags | Masthead fly (apparent wind) kept as a waving streamer; faction ensigns drawn in-engine from `Ink.Faction` (Crown: paper cross on red, traders: house flag, Brethren: skull and bones), streaming with the apparent wind; player's cosmetic flag is a swallowtail; figureheads (serpent/siren/kraken) gilded and sized to read. | `m2/cosm-crop.png` |
| V6 | Damage/juice | Shot holes + splintered planks accumulate with hull damage; bow wave curls off the stem with speed. | `r4/sheet3-z055.png` |
| V7 | Sinking | Ships that sink go down in view: list, settle by the stern with churn at the waterline, darken, fade (7.5 s; hers 2.3 s so it fits before the logbook); then a pale stain, a whirl, bubbles and a wreckage field (planks, spar with torn canvas, grating, rope, crate, hat) that drifts and fades. Ships that slip away fade out instead of popping. | `e4/sink-timeline.png`, `psink-zoom2.png` |
| V8 | Wakes | Wake = pale foam slick (patchy) + Kelvin-arm ink wavelets that fan out and fade; every ship in sight now has one (fleet wakes were missing); cosmetic wake ink applies to the arms. One draw call for all wakes. | `hulls-z25-sheet.png` |
| V9 | Effects | Broadside ripples gun by gun (0.05 s, like the sim): orange starburst muzzle flash + an ink-wash smoke blot per gun (8 generated puffs, atlas) that drifts downwind and fades; balls with a streak; misses throw a white crown, rings and droplets; hits burst wood splinters + dust; beasts bleed ink; rams splinter + ring; volcanic rocks boil and steam; storm spray whips off the bow downwind; collect glints gold/white. | `e2/fx-timeline.png`, `e2/fx-z25-crop.png`, `m3/erupt2.png` |
| V10 | Flotsam | Barrels, crates and chests are generated sprites bobbing in a foam ring; chests glint gold; they settle and fade as they sink. | `psink-zoom.png` (debris), `e1/*` |
| V11 | Monsters | All six beasts are generated "here be monsters" marginalia bent along live spines (`InkBatch.Strip`): serpent swims, arches through the surface in humps with splash crescents and lunges when it bites; kraken jets mantle-first under the water with arms streaming, then grips with four arms that rise from boiling water and curl over the rail (each shootable arm withers when cut); crocodile basks on the bank watching her, swims as a shadow with eyes and a V-wake, tail sways, jaws snap on the bite; weed-kraken is a tangled mass under the stern with fronds that creep up her sides and curl over the rails; ghost ship is a translucent wavering wreck above the night/fog wash with pale ghost-lights that blaze gold when she flares solid; siren sings on her rock with ribbons of song + notes to the ship and a dotted earshot circle (250 m). Every shootable part shows its hit zone (dotted chart circle, sim radius) and an HP dial. Eruptions: dashed red zone with growing rock shadows, then 6 generated lava bombs splash down with steam. | before `gallery/before/{serpent,kraken,ghost,croc,weed,siren,volcanic}.png` → after `m3/serp.png`, `m5/kraken.png`, `m5/ghost-pair.png`, `m3/croc.png`, `m4/weed-z25.png`, `m6/siren.png`, `m3/erupt1.png`, `m3/erupt2.png` |
| V12 | Night | Every ship shows a warm stern lantern at night, drawn above the night wash (the player's only while her lantern is lit: doused = dark, as the stealth rule says). | `m5/nightfleet.png` |

Merged `game-core` (7113d7b) into this branch at da07697 as the lead asked (its self-test helpers, GC-5 event guard);
the one conflict (the spyglass check, fixed by both of us) takes game-core's version.

Debug/review args added (view-only, parsed inside my views; they never touch sim rules in a real run):
`--shipsheet[=level[:id,id@N,...]]` (hull grid / point-of-sail sweep), `--fxtest` (broadside/splash/hit/ram on cue),
`--sinktest` / `--sinktest=player`, `--loadout=key,key,...` (wear cosmetics without unlocking them).

## Findings (bugs)

| ID | Sev | File:line | What was wrong | Evidence | Fix | Status |
|---|---|---|---|---|---|---|
| B1 | major | `src/Sim/World.cs:93` (consumed in `game/EffectsView.cs` `Consume`, `game/FleetView.cs` `Sync`, `game/Main.cs:497`, `game/Audio.cs:182`) | `World.Tick` returns early once `RunOver` (and while docked) **before** `Events.Clear()`, so the final tick's events stay in `world.Events`. Views that read them per physics tick re-fired them every tick until the logbook opened: her own sinking effect stacked ~75× into an opaque blue disc; the hull flash re-triggered each tick. Audio (reads per frame, gated by min-gap) replays e.g. the sink/hit sounds between the loss and the logbook. Baseline EffectsView did the same (75 whirls + 1,500 splinters). | `--sinktest=player` capture `scratch/shots/psink/g4.png` before the fix: disc pixel (69,96,117) ≈ the stain colour fully opaque; after: `psink-zoom2.png` | My views consume a tick's events once (keyed on `world.Ticks`: `EffectsView.Consume`, `FleetView.Sync`); `ShipView.Flash()` is ignored while sinking; a sunk ship records no more wake. The same bug was found independently by game-core (GC-5), which guards `Main._PhysicsProcess` (effects/audio/flash only when a tick ran); merged into this branch (da07697) — both guards agree. Still proposed for the sim owner: clear `Events` on the early return in `World.Tick`. | fixed |
| B2 | minor | `game/Main.SelfTest.cs:278` | "the spyglass sees 700 m along the cone" is flaky: the check teleports the ship then aims the cursor at a point 700 m ahead while the camera is still easing, so by the time the input lands the bearing is off by ~10° (dir −1.396 vs −1.571 rad) and the cone misses. Fails on `main` too under load (2 of 4 baseline runs here). | debug print in the failing run: `DBG spy on=True dir=-1.396 want=-1.571`; baseline worktree `scratch/wt-main` runs 1 and 3 FAIL | Settle the camera (`camPos = ship`) + one frame before aiming. | fixed |
| B3 | minor | `game/FleetView.cs:38–42` (old) | A ship removed from `world.Others` (sunk or slipped away) vanished instantly: no sinking seen for enemies. | before: sink event → ship gone next frame | Views of removed ships are kept, sunk ones go down (V7), escaped ones fade over 0.6 s. | fixed |
| B4 | polish | `game/FleetView.cs` (old) | Only the player's ship had a wake. | before gallery `sparring.png` | All visible ships record wakes (V8). | fixed |
| B5 | minor | self-test flakiness under load | "the first-voyage hint shows and is remembered (trade)" and "a broadside booms (sail_down)" also failed once each on the **baseline** worktree while other agents loaded the machine (load avg 13–18). Not mine; reported. | `scratch/wt-main` runs 2 and 4 | — | reported |

## Reported to other owners (not fixed here)
| ID | Owner | What | Evidence |
|---|---|---|---|
| R1 | sim (`World.cs`) | Clear `Events` on `World.Tick`'s early return (RunOver / docked) so no consumer can replay stale events (B1 root cause). | B1 |
| R2 | hud (`Hud.cs` EdgeMarkers) | Off-screen hostile chevrons with hull names stack on top of each other at the screen edge and over the sail gauge when several ships lie on the same bearing ("Sloop"/"Brigantine" at top-left, "Brigantine"/"Schooner" over the sail gauge). | `scratch/shots/m7/fleet40.png` (x≈20, y≈200 and y≈705–740) |
| R3 | tests (`AiTests.ThePopulatedSeaTradesAndKeepsItsBudget`) | Wall-clock assertion (240 s of sim < 12 s) fails when the box is loaded (it failed once here while two test runs overlapped; passes alone in 8 s). Consider a looser bound or measuring ticks, not seconds. | this run's first `dotnet test` (1 failed / 118 passed), `--filter AiTests` alone 9/9 |

Saved state: untouched (no change to `World.Save.cs`, profile or settings formats). No sim rule changed; the debug
args above are view-side only and ignored in real voyages.

## Performance (draw calls; runner, frame 600 — never FPS from the runner)
| Scene | Before (baseline) | After, my branch alone | After, merged with `merged` (world's new sea) |
|---|---|---|---|
| `--seed=7 --sail=3 --fps --frames=600` (sea, one ship) | 219 | 201 | **81** |
| `--seed=7 --sail=3 --fleet=40 --fps --frames=600` | 265 | 222 | **103** |
| `--shipsheet=3 --zoom=0.55` (15 hulls on screen at once) | — | 227 | **129** |
| `--fleet=40 --zoom=0.3` (widest zoom) | — | 236 | — |

How: every ship is 2–3 texture draws (water wash, hull, wet/flash overlay only when needed) + ONE triangle batch
(`InkBatch`: rig, sails, flags, damage, bow wave); all wakes are one batch; effects ≤ 4 batches (water ink, water
sprites, smoke sprites from one atlas, top ink); monsters a handful. `_Draw` paths reuse static buffers (no per-frame
allocation once grown). Hull paint variants are built once per (hull, colour) and cached (worst case ~60 small textures).

Design notes: sails are procedural rather than sprites on purpose — every frame they brace, sheet, belly, luff and
swing across on a tack from the sim's apparent wind, which fixed sprites cannot do. Hull PNGs are up to 713 px long
(man-o'-war: 62 m → ~744 px on screen at zoom 2.5 fullscreen), i.e. the "big key art" ≤ 1024 allowance; every other
sprite is ≤ 512 px.

## Merge with the integration branch (2026-09-26)
Merged `../merge merged` (68a43a5: sim-trade, game-core, sim-combat, world, ui-screens, icon) at 70011b9. Two class-name
clashes with world's new chart code (same namespace): my `InkMesh` → **`InkBatch`** (`game/InkBatch.cs`), my `InkLayer`
→ **`PaintLayer`** (`game/PaintLayer.cs`); my `tools/art/atlas.py` → `tools/art/fxatlas.py`. world's classes are
untouched. B1 now has one fix per consumer: Main (game-core GC-5) consumes effects/audio/flash only after a tick ran,
so `EffectsView.Consume` dropped its own guard; `FleetView.Sync` (called every physics tick by Main) keeps its guard.
Checked over the new sea at zoom 0.4 / 1.1–1.25 / 2.5, night, storm, fog + ghost, kraken, sparring
(`scratch/shots/new1/*.png`): hulls, faction ensigns, sails, wakes and monsters all read on the lighter paper; the
water wash under the hulls stays subtle; the ghost and night lanterns sit above the new weather wash and under the HUD.
No change needed.

## Tests run
- `dotnet build LastTide.csproj`: 0 errors.
- `dotnet test tests/Sim.Tests` on the merged tree: **171/171** passed (2026-09-26). Earlier, on my branch alone:
  119/119 with test parallelism off; the default parallel run failed only the wall-clock assertion R3 while the box
  was at load ~20–26 (passes alone).
- Self-test (`vrun.sh … -- --selftest`) on the merged tree: **SELFTEST PASS, 178 `ok`** (`scratch/selftest-merged.log`).
  My branch before the merge: PASS 141 ok (after game-core), PASS 106 ok (before it).
- Screens verified in the runner at 1600×900 and `--fullscreen` 1920×1080 (`scratch/shots/fs/*.png`): `--sparring`,
  `--monster=` in each region, `--fleet=40`, `--hull=<id>` for all 14 hulls at `--zoom=0.4/1.25/2.5`
  (`scratch/shots/hulls-z04|z125|z25-sheet.png`), BEFORE/AFTER pairs `scratch/pairs-ships.png`, `scratch/pairs-monsters.png`.
