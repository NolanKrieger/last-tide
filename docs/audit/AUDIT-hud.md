# AUDIT-hud — in-voyage HUD and onboarding hints (visual agent `hud`)

Owner files: `game/Hud.cs`, `game/Hints.cs`, `assets/art/hud/`. Branch `hud` in `~/.cache/lt-audit/hud`.
Status: **done** (2026-09-26). Branch `hud`, last commit listed at the end. Merged in: game-core, world, and the lead's integration branch twice (ui-screens, sim-combat, sim-trade, art-ships).

## Findings and changes

| ID | Sev | Where | What was wrong | Evidence | Fix | Status |
|---|---|---|---|---|---|---|
| HUD-01 | major | `Hud.cs` EdgeMarkers (old l.433–463) | Off-screen ship markers were printed on top of the wind text, gauge labels and key line, and on each other ("Brigantine"/"Sloop" collided). | BEFORE `gallery/before/fleet.png` | Markers are badges on the screen rim along the bearing from the ship, pushed out of every HUD plate (keep-out rects), crowded badges merge into one with a count, labels placed toward the screen centre and dropped if they would touch a plate or another label. | fixed |
| HUD-02 | major | `Hud.cs` EdgeMarkers (old l.447) | Markers showed every hostile within `VisionRadius + 300` m for everyone: a free tier-3 Lookout, while the Lookout officer's warning range (`World.WarningRange`, GDD §7 "edge-of-screen warnings 100/200/300 m beyond vision") had no reader anywhere in `game/`; beasts never got a marker. | `grep -rn WarningRange game/` → no hits on the baseline; old code `if (d > World.VisionRadius + 300) continue;` | A marker for hostiles/beasts in sight (`PlayerSees`) that are off screen, plus lookout reports (thin ring, "· lookout") within `VisionRadius + WarningRange`; the current beast gets its own badge with its emblem. | fixed |
| HUD-03 | minor | `Hud.cs` notices/toast (old l.255–260, 327–338) | Notice and region-toast timers counted frames (`1/60` per frame), so they lasted 2 s at 144 Hz and 10 s at 30 fps, and kept running while paused or docked: a notice raised on docking (`NOTICE_DESERTED`, `NOTICE_OFFICERS_LEFT`, enqueued in `World.Port` Dock) faded behind the port panel before the player could read it. | code; BEFORE `gallery/before/port-market.png` shows the notice slot under the panel | Real frame delta; timers hold while paused or docked; notices wait in a queue until the HUD is uncovered. | fixed |
| HUD-04 | minor | `en.csv` HUD_DOCK/HUD_DIG/HUD_DIG_FURL/HUD_SALVAGE/HUD_SALVAGE_FURL; hints | Prompts said "F · …" and "(S)" whatever the bindings were (keys are rebindable since M11); hint texts hardcoded W/S/A/D/Q/E/L/M/F/3/4. | `en.csv` rows 124, 320–326, 464–476 on the baseline | Prompt words no longer carry a key; the HUD draws a key cap from the live binding (`Hud.KeyLabel`, wired in `Main.BuildViews`); hint texts carry `[Action]` tokens drawn as caps of the bound key. | fixed |
| HUD-05 | minor | `Hints.cs` table | The "dock" note fired at t = 0 of every voyage (she starts inside her home harbour ring) and pre-empted "Make sail"; it also explained a ring the player had not sailed into. | BEFORE `gallery/before/hints.png`: dock card while furled at 4 s | "dock" fires in the first harbour ring she enters after having been out of one for a second (`World.HarborHere`, not the HUD's prompt), so "first_sail" comes first and the note meets a ring she sailed into. | fixed |
| HUD-06 | minor | `en.csv` HINT_night, HINT_leak | "L lights the lantern" — the lantern starts lit (`World.Lantern = true`), so L douses it. "4 puts hands on the pumps, 3 on repairs" — Balanced (4) pumps only with hands left after sails/guns/repair (0 on the starting sloop); Repair & pump (3) is the pump order. | `World.Weather.cs:6`, `Ship.Stations()` | Texts corrected (night: lit by default, L douses to slip past; leak: 3 sends hands to pumps and leaks, 4 back to balanced). | fixed |
| HUD-07 | polish | `Hud.cs` (old l.68–72) | "Aground!" was lettered over the Threat tier name. | BEFORE `gallery/before/night.png` | Aground is a red chip on the conditions line; the mangrove block is a red ribbon in the notice slot. | fixed |
| HUD-08 | polish | `Hud.cs` EdgeMarkers (old l.458) | A marker's hull name was centred 22 px inward, i.e. over its own chevron. | BEFORE `gallery/before/sparring.png` ("Sloop") | See HUD-01. | fixed |
| HUD-09 | minor | `Hud.cs` prompt (old l.311) — R-04 from the sim-trade auditor | While salvaging a wreck that lies inside an unmatched dig ring the prompt said "Digging · 15 s": it inferred salvage from `WreckHere != null && DigSiteHere == null`, but the sim salvages whenever no *matched* site is here. | self-test check "and the prompt counts the salvage down" FAILS with the old inference ("Digging · 15 s"), passes with the fix | `World.Salvaging => Digging && digWreck` (one read-only line added in `src/Sim/World.Exploration.cs`, outside my files, at the lead's request); the label, icon and countdown key on it. | fixed |
| HUD-10 | major (perf) | `Hud.cs` Refresh | Reading the pointer with `GetLocalMousePosition()` every frame is a blocking display-server query: 2.0 ms a frame in the runner (the whole old HUD refresh cost 0.05 ms). | `--fixed-fps 60` profile: refresh 2.03 ms → 0.05 ms after the change | The pointer comes from mouse events (`_Input`), as Main already does for the spyglass. | fixed |
| HUD-12 | minor | `Hud.cs` / `Main.cs` (lead's R-3) | Notice and toast timers counted render frames (`1.0 / 60` per `Refresh`): 2.1 s at 144 Hz, 10 s at 30 fps. | code | `Hud.Refresh(world, paused, delta, covered)`: every HUD timer and animation runs on the frame delta and holds while paused, docked or covered by a full screen; a notice leaves `World.Notices` only when it is inked (so Main's quill cue and game-core's GC-25 "notices wait for the sea" both hold). One-line call-site edit in `Main.ProcessFrame`. | fixed |
| HUD-13 | minor | `Hints.cs` | Hint cards sat at one fixed spot under the Threat bar whatever they explained, over the sea's middle; after the redesign the fixed slot would also have covered the key legend. | BEFORE `gallery/before/hints.png` | Each note is pinned beside the instrument it explains (rose, helm, water gauge, guns, Threat, prompt), found by searching around it for a spot clear of every plate, the notice slot and the screen edge (narrower wraps allowed), fixed once per note; an ink leader runs from the card's nearest point to the instrument (to the rose's ring for the rose) and is dropped if it would cross another plate. Keys are caps of the live bindings (`[[Action]]` via `Text.Raw`). | fixed |
| HUD-11 | minor (perf) | `Hud.cs` | The old HUD allocated ~15 formatted strings and a LINQ join every frame and cost 69 draw calls at sea (89 with 40 ships): every tick mark, arc and label was its own call. | perf table below | Strings are formatted only when their inputs change; text widths, key labels and marker labels are cached; everything but text and the rose art is drawn from one atlas in batched rects. | fixed |

| HUD-14 | minor | `Hud.cs` EdgeMarkers `Clear` | A badge pushed out of one plate could land on the next (notice ribbon ↔ day plate, legend ↔ prompt) and ping-pong: at 125/150 % UI scale badges sat on the Threat bar and on the dock prompt. | captures `scratch/scales/ui125-fleet.png`, `ui150-fleet.png` (before this fix, overwritten); self-test check "a badge between two plates clears both" | Candidates = the point, its projections onto every plate edge and their own projections; the nearest one inside the track that no plate covers wins. | fixed |
| HUD-15 | minor | `Hud.cs` EdgeMarkers labels (also art-ships' R2) | Labels were kept off plates and other labels but not off other badges ("Frigate · 390 m" ran over a neighbour's badge at 125 %). art-ships' R2 (labels stacked on each other in `fleet.png`) was the baseline HUD, fixed by HUD-01. | `scratch/scales/ui125-fleet.png` before/after | Every badge (its own too, once a label is clamped to the screen edge) is an obstacle; a label tries four sides and is dropped when none is clear; crowded badges merge into one with a count. | fixed |
| HUD-16 | polish | `Hud.cs` region toast (world's P-01) | The HUD's big region toast under the Threat bar duplicated the region name the world branch now letters across the sea. | `gallery/world-2/sea-sailing.png` | Toast removed; the region chip on the conditions line inks bold with a self-drawing underline for a few seconds instead (`Hud.RegionCalled`, self-test "entering a region inks its name on the conditions line"). | fixed |
| HUD-17 | polish | `Hud.cs` | A hostile hidden under a HUD plate (e.g. under the purse) had no marker: markers only covered off-screen ships. | self-test "a hostile hidden under a HUD plate is marked beside it" | Ships and beasts under a plate get a badge at the plate's edge. | fixed |
| INC-01 | major (process) | my `scratch/cap.sh` (not committed) | On 09-23 my capture script passed a **relative** `VRUN_USERDATA` (`scratch/shots/rN/.ud-…`). Godot ignores a relative `XDG_DATA_HOME` and falls back to `~/.local/share`, so those capture runs used Nolan's real `~/.local/share/godot/app_userdata/Last Tide/` (the lead's 09-23 incident, at least in part). Batches affected: `scratch/shots/r1…r13`, `r01`, `r2b`, `r3b`, and one `gallery-after` hints shot (09-23 13:40–16:20). What they could write there: Godot logs for every run; `debug/profile.json` from the `--hints` runs; and **`profile.json` from the `--logbook` recap capture in `r8` (pre-game-core Main recorded that run: a Rough Seas "best" of about 0 days)**. No run used `--pause`/`--dock`, so `suspend.json` was not written; settings were not saved. The self-test, perf and gallery runs used absolute paths. | `scratch/cap.sh` history; `vrun.sh` now absolutizes and refuses real dirs | cap.sh now `realpath`s its output dir; the runner's guard covers the rest. I did not open or change Nolan's folder. | fixed (disclosed) |

## Harmonised with ui-screens' theme (2026-09-26)

The HUD now speaks the screens' language while still drawing from its one atlas: `tools/art/hud_atlas.py` copies the kit's
pieces in (paper crop, `rim.png` at half size, `key.png`/`key-hot.png`, the gold `corner.png`, `ribbon.png`, the station,
gold, crate, anchor and hourglass icons, goods cells 0–2), and `Hud.cs` uses them:
- plates = the screens' paper tiled + their deckled rim with the printed double rule (edges tiled, never stretched); gold
  corner flourishes on the large plates (day cartouche, last stand) as on the screens' sheets;
- key caps = their key cap (9-sliced) everywhere: legend, helm, batteries, crew, prompts, hint notes;
- notices ride their swallow-tail ribbon (rose-tinted for danger); the tier banner is their ribbon with the tier in
  display small caps and "The sea grows meaner" lettered above it;
- gold, hold, stores (provisions/munitions/timber = the market's goods icons) and crew stations use the same pictures as
  the port and crew screens; secondary text uses `Parchment.Muted` (72 % ink) like the screens.

## Reported (outside my files)

| ID | Sev | Where | What | Evidence | Proposed fix |
|---|---|---|---|---|---|
| R-01 | critical — **fixed by game-core (GC-1)**, verified after the merge (self-test "GC-1 a harbour rescue docks her and opens the port") | `World.Combat.cs` rescue + `Main.cs` | A harbour rescue ("By a hair", GDD §8) docks her inside the sim (`Execute(new PortCommand(PortAction.Dock))`), but nothing opens the port panel: `Main.Paused` is true while `world.IsDocked`, so the sim stops for good with no panel to cast off from (F does nothing, Esc only toggles the pause menu). Every successful last-stand rescue soft-locks the voyage. | `scratch/shots/r01/rescue-200.png` and `rescue-900.png` (`--fixed-fps 60 -- --seed=7 --damage=0.5,97,6`): the frames are pixel-identical (`ImageChops.difference(...).getbbox() is None`), no panel, clock stopped | `Main.ProcessFrame`: `if (mode == Mode.Run && world.IsDocked && !portScreen.IsOpen && !logbook.IsOpen) { portScreen.Open(); audio.Bell(); if (suspendEnabled) profile.WriteSuspend(world.SaveJson()); }` |
| R-02 | minor — **fixed by game-core** (`audio.Consume()` after each tick in `_PhysicsProcess`) | `Audio.cs` `Update` | One-shots are read from `world.Events` once per render frame, but `World.Tick` clears `Events` at its start: when two physics ticks run in one render frame (frame rate under 30, or any hitch) the first tick's cannon/hit/splash/sink/coin sounds are lost. Makes the self-test's "a broadside booms" flaky under load (seen 2× in 8 runs here). | `World.cs:101` `Events.Clear()`; `Audio.cs:182` loop in `Update`; self-test logs `scratch/selftest-r1.log` (earlier run) | Consume events in `_PhysicsProcess` right after each tick (as `EffectsView.Consume` does), e.g. `audio.Consume()` next to `effects.Consume()`. |
| R-03 | observation | `Settings.Apply` (UI scale) | UI scale is `Window.ContentScaleFactor`, which scales the whole root viewport: at 150% the sea is zoomed 1.5× too, not just the HUD. The HUD lays out correctly at every scale (self-test check), but the option also changes the camera's field of view. | `scratch/shots/r3/ui150.png` vs `sea-sailing` | If only the HUD should scale: keep `ContentScaleFactor` 1 and scale the CanvasLayers (`CanvasLayer.Transform`/`Scale`) instead; the HUD reads its size from its canvas, so it would follow. |
| R-06 (lead / sim-combat) | major | Lookout warnings unused | Same finding as HUD-02, already fixed: no Lookout → markers only for what she can see; with one → reports out to sight + `WarningRange` (thin ring, "· lookout"); self-test "beyond sight, with no lookout aboard, she is not marked" / "a lookout reports her from beyond sight". | — |
| R-05 | minor | `Main.SelfTest.cs` (timing) | Two pre-existing timing flakes surfaced once the HUD checks shifted the timeline: the spyglass check aimed through a camera still easing after a teleport; the first-sail hint check depended on which notes had fired earlier in the test (and on a random voyage's raiders). | runs logged in `scratch/selftest*.log` | Fixed in the self-test (camera pinned after the teleport; the other notes marked seen before that check). |

## Merge with game-core (914e37b)

- Conflicts: `en.csv` — the prompt rows keep no key in their words (the HUD draws the bound key as a cap beside them; game-core's `[[Dock]] · …` would have printed the key twice), hint rows take game-core's `[[Action]]` tokens with the hud wording (HUD-06); `Main.cs` — kept `--playtest` and game-core's `--uiscale`, plus the hud capture args `--laststand[=water]`, `--notice=KEY`, `--damage=HULL,WATER[,LEAKS]`.
- Contracts followed: key names come from the bindings (`Text.Get` resolves `[[Action]]`; the notes use `Text.Raw` and draw caps); self-test input only through `Push`/`Key`/`Tap`; no `GetTree().Quit` added.
- game-core's `Main.Audit.cs` read the old HUD's private `Label` fields by reflection (`notice`, `card`); those checks now read `Hud.NoticeShown`, `Hints.NoteShown` and `Hud.DockPromptKey` (GC-11 now asserts the prompt's key cap is "G" after rebinding Dock to G).

## Performance (draw calls at frame 600 with `--fixed-fps 60`, so each tree's frame is the same sim state)

`scratch/perf.sh` runs `--seed=7 --sail=3 --fps --frames=600` (+ `--fleet=40`), each with and without `--off=hud`.

| Tree | at sea | HUD off | HUD's share | fleet 40 | HUD off | HUD's share |
|---|---|---|---|---|---|---|
| baseline (a878227) | 237 | 168 | **69** | 265 | 176 | **89** |
| hud after the redesign (pre-merge) | 180 | 168 | **12** | 191 | 176 | **15** |
| hud final (all merges, world's new sea) | 30 | 17 | **13** | 32 | 17 | **15** |

HUD refresh ≈ 0.05 ms a frame (was 2 ms before HUD-10); no garbage in steady state (strings formatted on change).

## Before / after screenshots

BEFORE = `~/.cache/lt-audit/gallery/before/<name>.png`; AFTER = `~/.cache/lt-audit/hud/scratch/gallery-after/<name>.png`
(1600×900; `hints`, `laststand`, `tier`, `damage` taken with `--fixed-fps 60` so the moment is exact); scale and fullscreen
sweep in `~/.cache/lt-audit/hud/scratch/scales/`.

| Item | Before | After |
|---|---|---|
| Compass rose (art, wind arrow with 5-kn feathers, heading needle, no-go wedge, in irons) | `sea-sailing`, `storm` | `sea-sailing`, `storm`, `ghost` (in irons: red wedge + needle) |
| Wind / heading / region-weather-monster chips | `night`, `kraken` | `night`, `kraken`, `siren` |
| Day + watch dial, Threat bar and tier | `sea-sailing` | `sea-sailing`, `tier` (banner + pulse), `night` (moon) |
| Purse (ship, hull, gold, hold, crew, stores) | `sea-sailing` | `sea-sailing` |
| Helm (4 sails, canvas set, speed, point of sail, keys) | `sea-sailing` | `sea-sailing`, `hints` |
| Ship card (hull/water tubes, leaks, batteries + reload, shot, crew stations) | `sparring` | `sparring`, `damage` |
| Notices / ribbons | `treasure` | `treasure`, `storm`, `tier` |
| Prompts with key caps | `treasure`, `hints` | `treasure`, `hints`, `sea-start` |
| Key legend | every sea shot | every sea shot; `pause` (comes back while paused) |
| Edge markers | `fleet`, `sparring` | `fleet`, `sparring`, `siren` (beast badge), `scales/ui125-fleet` |
| Last stand | — (self-test only) | `laststand`, `scales/ui150-stand`, `scales/fs-stand` |
| Hint notes | `hints` | `hints`, `scales/ui125-hints` |
| SUNK / recap background | `recap` | `recap` (instruments fade under SUNK) |
| Fullscreen 1920×1080 | — | `scales/fs-sea`, `fs-fleet`, `fs-stand`, `fs-ui75-stand`, `fs-ui125-dmg`, `fs-ui150-tier` |
| UI scale 75 / 125 / 150 % | — | `scales/ui75-fleet`, `ui125-fleet`, `ui125-hints`, `ui150-fleet`, `ui150-stand`, `ui150-dmg` |

## Tests run

- `dotnet test tests/Sim.Tests`: **171/171** on the final tree (119/119 before the integration merges).
- `--selftest`: **PASS, 198 `ok`** on the final tree (baseline 106). HUD/hint checks added by this branch: markers (in sight
  off screen, beyond sight without/with a lookout, under a plate, between two plates), last-stand refuge, prompt key follows
  the binding, salvage prompt, notes letter the bound key and sit clear of the plates, notices wait in port and are read
  after casting off, no plate overlap at 150 % UI scale, legend fades and returns (pause, pointer), region chip; plus the
  broadside, spyglass and first-sail timing fixes.
