# Last Tide

A roguelite sailing, trading and broadside-combat game drawn as an ink-and-parchment sea chart. Godot 4.7 (.NET / C#). Design: [`docs/GDD.md`](docs/GDD.md). Build status: [`docs/PROGRESS.md`](docs/PROGRESS.md).

## Run it

```bash
godot --path ~/last-tide                       # play (1600×900 window, fixed size)
godot --path ~/last-tide -- --sail=3            # start with full sail set
godot --path ~/last-tide -- --wind=45,8         # pin the wind: from compass 45° (NE) at 8 m/s, no wander
godot --path ~/last-tide -- --hull=frigate      # any id from src/Sim/Hulls.cs
godot -e --path ~/last-tide                     # open in the Godot editor
```

Controls: `W` / `S` set or take in sail one step (furled → ⅓ → ⅔ → full) · `A` / `D` helm (the rudder centres when released) · `Q` / `E` hold to aim and release to fire a broadside · right mouse button raises the spyglass toward the cursor · hover over any ship in sight (your own too) for her card · `1`–`4` crew orders, `C` crew panel · `L` lantern · `F` dock inside a harbour ring, dig at a matched X or salvage a wreck (furled) · `M` chart (only with a cartographer aboard; hire one at a tavern) · wheel zoom · `Esc` pause menu (Resume / Save and quit to the title / Quit) · `F11` or `Alt+Enter` fullscreen.

With no arguments the game opens on the title (New voyage · Resume voyage · The logbook · Quit); every debug argument below skips it and starts a run directly, without touching the profile or suspend save.

Reading the screen: the compass rose top-left carries the **wind** as a bold blue arrow (it points where the wind blows to; feathers mark where it comes from) and the **ship** as a thin sepia needle (red when she is in irons). The masthead fly on the ship shows the apparent wind. Bottom-left: sail gauge, speed in knots, point of sail. The wave strokes on the sea run with the wind and thicken with it.

## Test it

```bash
dotnet test tests/Sim.Tests                 # sim rules, headless: polar, hulls, wind, physics, tacking, replay determinism, save/load, clock
godot --path . -- --selftest                # real input path: W/S/A/D, wheel, Esc/Q, HUD strings, grounding, render check
godot --path . -- --sail=3 --wind=45,8 --screenshot=/tmp/shot.png --frames=240   # render check (look at the PNG)
godot --path . -- --sail=3 --fps --frames=600                                    # prints measured FPS
godot --path . -- --playtest=6,3 --seed=21      # 3 whole voyages to day 6 through the real game layer, autopilot at the keys, ×8 speed
```

Run `godot` and `dotnet` outside the sandbox; the self-test and screenshots need the real display.

Debug arguments (after `--`; any of them skips the title): `--seed=N` `--hull=id` `--preset=calm|rough|tempest` `--sail=0..3` `--wind=FROM,SPEED` (pinned) `--zoom=Z` `--quiet` (no fleet, no director) `--time=S` (fast-forward) `--region=key` `--at=X,Y` (start at a point in metres, off the chart too) `--storm` `--monster=type` `--sparring` `--reveal` `--chart` (brings a green cartographer: the chart opens only with one aboard) `--cartographer[=0..2]` (start with one aboard, green by default) `--dock` / `--dock=cove` with `--page=0..3` `--treasure` (a matched bottle map at its dig ring) `--hover` (the pointer on the nearest ship in sight) `--deliver` (sign the home board and sail to the first contract's port; add `--dock` to arrive) `--title[=voyage|logbook]` `--logbook` (the recap) `--pause` `--hints` (onboarding cards, scratch profile) `--screenshot=path --frames=N` `--fps` `--selftest` `--playtest=DAYS[,VOYAGES]` (autopilot voyages through the title, keys, port, pause, save and resume; fails on any error) `--uiscale=S` (any screen at a UI scale) `--crew` (crew panel open) `--pause=options` `--fleet=N` (N ships of real traffic near her) `--off=layer,…` (hide layers, for perf bisecting) `--laststand[=hull|water]` `--damage=HULL,WATER` `--notice=KEY` (HUD states for screenshots) `--shipsheet[=level[:ids]]` (hull grid / point-of-sail sweep) `--fxtest` `--sinktest[=player]` `--loadout=a,b,c,d,e` (wear cosmetics) `--novsync` `--sound`. A malformed value is ignored with a warning. Debug runs use a scratch profile and settings (`user://debug/`).

## Balance and release tooling

```bash
cd scratch/balance && dotnet run -c Release -- rough 200 1000 40    # autopilot voyages: preset, runs, first seed, day cap → out/*.jsonl + a summary
cd scratch/tune && dotnet run -c Release -- pilot 1000 20            # one traced voyage (decisions, per-day state, time by mode)
cd scratch/tune && dotnet run -c Release -- profile 3000             # sim tick cost by phase
godot --headless --path . --export-release "Linux" build/linux/LastTide.x86_64      # needs LastTide.sln (present)
godot --headless --path . --export-release "Windows" build/windows/LastTide.exe
```

`docs/STEAM.md` lists what Steam needs from Nolan; `docs/STYLE-BIBLE.md` is the art contract awaiting his approval.

## Layout

| Path | What |
|---|---|
| `src/Sim/` | All rules in plain C#, no Godot types, fixed 30 Hz, seeded, deterministic. `World` (run, clock, tick, command log, hash, save), `Ship` (custom rigid body: polar thrust, drag, keel, rudder, collisions), `WindField` (wandering wind + spatial field + gusts), `Polar` and `Hulls` (data tables), `Island`, `MapGen` + `Landforms` + `Coastlines` (the landform map generator), `Whirlpools` and `Ice` (sea hazards), `Pilot` (helm helpers), `Tuning` (every v1 tuning value), `Rng`, `Noise`, `Vec2`, `Angles`. |
| `tests/Sim.Tests/` | xUnit tests for the sim. |
| `game/` | Godot layer: `Main` (input → `ShipInput`, tick, interpolation, camera, args), `ShipView` (procedural ink ship + sails), `SeaLayer` (parchment sea shader), `IslandView`, `WakeView`, `Hud` (rose, gauges, labels), `Text` (string table), `Ink` (palette, scale). |
| `assets/text/en.csv` | Every player-facing string. |
| `assets/shaders/sea.gdshader` | The sea: paper, hand-inked wave strokes, gust patches, portolan rhumb lines, coastal watercolour, shallows, surf, land wash. The wind it draws is the sim's own `World.WindAt`, sampled by `SeaLayer` on a world-locked grid (self-test checked). |
| `assets/fonts/` | IM Fell English (SIL OFL, licence beside it). |
| `docs/` | GDD (design + decisions log §19), PROGRESS (status, evidence, bugs), LICENCES, AI-ASSETS. |
| `scratch/` | Throwaway: tuning sweep harness (`scratch/tune`), screenshots. Not part of the game. |
| `tools/art/` | The art pipeline: `gen.sh` (Codex image_gen with the style-bible template), `key.sh` (chroma → alpha), cutters/atlas builders, `README.md`. |
| `docs/audit/` | The 2026-09 audit: `SUMMARY.md` and one report per area (every finding, its evidence and fix). |

Coordinates: metres, +X east, +Y south, angles clockwise from +X (Godot's convention); compass = math angle + 90°. 1 m = 4 px at zoom 1. The sim ticks in `_PhysicsProcess` (30/s); rendering interpolates between the last two ticks.

## Toolchain

Godot 4.7.2 .NET at `~/.local/opt/godot-4.7.2-mono` (launcher `~/.local/bin/godot` sets `DOTNET_ROOT`), .NET 8 SDK at `~/.dotnet`. Notes: `~/.claude/workspace/tools/languages.md` → "Godot 4.7.2 .NET".
