# Progress

Living status for Last Tide. A new session must be able to resume from this file alone.
Roadmap and exit criteria: `docs/GDD.md` §18. Decisions: GDD §19 (the "M1 build" block holds the tuning choices).

## Status by milestone

| M | Milestone | State |
|---|---|---|
| M0 | Project skeleton | **Done** 2026-09-23 |
| M1 | Sailing feel | **Built** 2026-09-23. Gate lifted by Nolan the same day: finish the whole game, then iterate on feel. |
| M2 | World gen, regions, ports, chart reveal | **Done** 2026-09-23 |
| M3 | Ports and economy | **Done** 2026-09-23 |
| M4 | Combat | **Done** 2026-09-23 |
| M5 | AI ships, reputation, director, Threat | **Done** 2026-09-23 |
| M6 | Weather and day/night | **Done** 2026-09-23 |
| M7 | Monsters | **Done** 2026-09-23 |
| M8 | Progression: hulls, parts, crew, officers | **Done** 2026-09-23 |
| M9 | Exploration: coves, black market, bottle maps, digs, wrecks, achievements | **Done** 2026-09-23 |
| M10 | Run wrapper: title, presets, cosmetics, name, logbook, bests, suspend save, hints | **Done** 2026-09-23 |
| M11 | Art, audio, Steam, balance, options, perf, exports | **Done** 2026-09-26: art pass, full audit (178 fixes) and visual overhaul — see "Audit and visual overhaul" below. Open: the next balance round and Nolan's playtests. |

## M0 — Skeleton (done 2026-09-23)

`LastTide.csproj` (Godot.NET.Sdk 4.7.2, net8.0, `Compile Remove="src/**;tests/**;scratch/**"`), `src/Sim/Sim.csproj` (plain C#), `tests/Sim.Tests` (xUnit), `project.godot` (GL Compatibility, 30 physics ticks/s, 1600×900 fixed-size window, `canvas_items`/`expand` stretch), `game/Main.tscn` + `Main.cs` with `--screenshot`, `--frames`, `--fps`, `--selftest`, `--seed`, `--hull`, `--sail`, `--wind`, `--zoom`. `.gdignore` in `src/`, `tests/`, `docs/`, `scratch/`.

Evidence: `dotnet build LastTide.csproj` 0 errors; `dotnet test tests/Sim.Tests` green; the window opens (screenshots below).

## M1 — Sailing feel (built 2026-09-23, gated on Nolan)

Exit criterion: Nolan playtests and says it feels right.

Delivered:
- **Sim** (`src/Sim`): `Vec2`/`Angles` (screen convention), `Rng` (xoshiro256**), `Noise` (seeded value noise, mirrored in the sea shader), `Tuning` (every v1 value), `Hulls` (14 rows from GDD §7), `Polar` (per-rig curves with the in-irons fade), `WindField` (OU wander + spatial field + gusts; `SetFixed` for tests), `Island` (polygon containment/closest point; `Blob` placeholder), `Ship` (custom rigid body: thrust = drag at polar target, quadratic+linear drag, keel, leeway, irons backwash, rudder authority vs headway, turn scrubs speed, capsule collision), `Pilot` (helm, close-hauled heading), `World` (tick, clock/watches, command log, FNV hash, replay, JSON save/load).
- **Godot** (`game/`): `Main` (input → `ShipInput`, 30 Hz tick, render interpolation, eased leading camera, wheel zoom, Esc pause / Q quit, CLI args), `SeaLayer` + `assets/shaders/sea.gdshader` (parchment, wind-aligned drifting ink strokes whose density follows wind speed, gust patches, rhumb grid), `ShipView` (procedural ink hull, gaff/jib/square/lateen sails from sail fraction and wind side, masthead fly = apparent wind), `WakeView`, `IslandView` (tan wash, coastline, hachure, dotted shallows), `Hud` (compass rose with bold wind arrow + heading needle, wind/heading lines, day + watch, sail gauge, speed, point of sail, hull, hints, paused notice, aground notice), `Text` + `assets/text/en.csv`.
- Tuning sweep harness `scratch/tune` (prints acceleration, coast, 180° turn, tack and beat numbers per tuning combo). Chosen values are in `Tuning.cs` and GDD §19.

Evidence (2026-09-23):
- `dotnet test tests/Sim.Tests`: **46/46** (polar table, hull table, wind wander/range/fixed/spatial, beam-reach speed = table within 1%, sail levels, close-hauled/running, wind scaling, irons sternway, sail-vs-turn, stopped ship can't turn, keel, turn drag, leeway, rudder centring, sail change time, grounding, close-hauled progress, tack through the wind, four-leg beat, helm overshoot, same-seed hash, command-log replay, save/load continuation, RNG state, noise, clock/watches, compass).
- `godot --path . -- --selftest`: **27/27 PASS** (strings; W/S levels + clamp + timing; D/A turn direction; rudder centring; wheel zoom in/out/limits + easing; camera follow; Esc pause/resume + ignored orders; grounding held outside the coast; frame has ink; HUD text).
- `--sail=3 --fps --frames=600`: **59.2 fps** (vsync-bound) at 1600×900 with the sea shader and one ship.
- Screenshots reviewed: `scratch/shot3.png` (zoom 1.25, sloop close-hauled), `scratch/shot4.png` (zoom 2.5, sails/pennant/wake), `scratch/shot5.png` (frigate square rig).
- Sailing numbers (sloop, 8 m/s wind): 0→76% top speed 4.2 s · 12→6 m/s coast 3.8 s · close-hauled tack 2.4 s through the eye, ≈7.7 s helm-to-settled, slowest 2.2 m/s · 4×20 s beat gains 250 m upwind.

What Nolan should try (the handoff message): raise sail with W, feel the pull on a reach vs close-hauled, tack through the wind with A/D, run downwind, furl and coast, zoom both ways, bump an island.

## M2 — World, regions, ports, chart (done 2026-09-23)

Exit criterion: maps validate (reachability, start-route guarantee) across 1,000 seeds in tests. **Met.**

Delivered: `Goods` (27), `RegionDef` (9 regions: island spacing/size/channel/roughness, port quotas, produce/consume tables, monster, cove and treasure weights), `Map`/`Port`/`TreasureSite`/`Wreck`, `NavGrid` (25 m cells, offset-polygon land, BFS sea distances), `RevealMask` (12.5 m bits, paint/region/save), `Names`, `MapGen` (jittered 3×3 warped Voronoi regions; dart-thrown islands with a coast-to-coast channel rule and per-region density caps; ports on coast vertices with harbour rings that must be wet; faction mix 44/39/17 + 3–5 coves weighted to Mangrove/Fog; goods from region tables repaired to ≥2 producers/≥3 consumers, luxuries stripped from consumers within 1.5 km of a producer, rare goods only at coves; validation by BFS + start route ≤ 600 m; 8–14 treasure sites weighted to Siren's Ruins; 3–6 Sargasso wrecks). World: ship starts in the start harbour, Trade Isles pre-inked and its ports discovered, vision paints the mask, coves discovered on sight, chart edge wall, Mangrove Maze closed to big hulls, ink pins. Godot: `ChartView` (30 chunks: hachured islands, harbour rings, faction marks crown/anchor/pennant + fort bar, lettered names), `MarksView` (coves, pins), `FogView` + `fog.gdshader` (parchment with ink-bleed edge from the mask texture), `ChartScreen` (M): whole map, region names, charted-sea wash shader, ports, ship + vision ring, pins with notes (click → note field, right-click removes), zoom/pan, ledger tooltip stub; HUD region toast and mangrove notice.

Evidence: `dotnet test` 54/54 incl. `AThousandSeedsValidate` (~45 s; 1.93 attempts/map, ~113 islands, ~37.7 ports); `--selftest` 34/34 (adds chart open/pause, pin add/remove, home port charted, chart close); screenshots `scratch/m2-start.png`, `m2-chart.png`, `m2-chart-all.png` reviewed.

## M3 — Ports and economy (done 2026-09-23)

Exit criterion: a scripted trader profits and prices react and recover (sim test). **Met.**

Delivered: `Market` per port (stock/target per good, price = base × role × √(target/stock) clamped 0.35–3×, 5% spread, unit-by-unit quotes so prices move while you trade, 15%/day drift + 1%/h noise, stock words), `Player` (gold, cargo with bulk, cost basis, crew, unpaid flag, reputation, ledger with day stamps and rumor flag, ports visited, ship name), `RunStats`, `World.Port` (Dock/CastOff/Buy/Sell/Repair/Hire/Rumor/BuyCannon/SellCannon as replayable `PortCommand`s; docking freezes the clock; ports closed at rep ≤ −50, coves always open; stations split + wages 3/2/4/2 and provisions ⌈crew/4⌉ charged at midnight; unpaid → 30% desert at next dock; no food → a hand a night; reputation decay 2/day; +1 rep per trading visit; rare goods only bought at coves), `World.Save` (full state incl. markets, ledger, stats, both logs). Godot: `Parchment` theme, `PortScreen` (header with standing and shut-out warning, Market with aligned columns/trend arrows/stock words/held + detail pane with quantity slider and marginal totals, Shipwright repair + cannons, Tavern hire + rumors, Cast off), HUD purse/hold/stores/crew, dock prompt, notice toasts; F docks, Esc casts off; `--dock` arg.

Evidence: `dotnet test` 63/63 (economy: price curve and clamps, ~5% drop per 10 units at a small port, drift back to target, scripted trader profits + price reaction + recovery, hold limits and bulk, upkeep/desertion/starvation, clock freeze and closed ports, tavern/cannons/repair, replay with port commands and save mid-dock); `--selftest` 46/46 (adds dock prompt, F docks, clock frozen, buy/sell/refused, tavern tab, Esc casts off); screenshots `scratch/m3-port2.png`, `m3-hud.png`.

## M4 — Combat (done 2026-09-23)

Exit criterion: scripted fight tests and a playtest. Tests met; the in-engine `--sparring` opponent stands in for the playtest until Nolan's round.

Delivered: `Ship` combat state (crew moved onto the ship; orders 1–4 split hands into guns/sails/repair/pumps; cannons split by side with the odd gun to starboard; reload 12 s + 2 s/grade scaled by manning; range 180 + 28/grade; damage 6 + 2/grade ×0.8–1.2; leaks at every 10% of hull lost with a copper save chance; casualties 25% per hit; water +1%/s per leak, pumps 0.2%/s per hand; carpenters plug a leak per 8 s each, then patch 1 HP/s at a plank per 10 HP up to 70%; last stand at hull 0 or water 100 with a 20 s glass + 3 s per pumping hand up to 35 s, water-only stands end below 80%; foundering caps speed at half and silences the guns), `World.Combat` (Fire with munitions for the player, ripple delays, ±3° spread, segment-vs-capsule hits, splashes at max range; ramming with positional separation, momentum exchange, bow-on ×2/×0.5, flank ×1.3, mass ratio, cooldown; flotsam chests and barrels that drift downwind, sink after 60 s and are collected within 12 m; reputation −10 on first blood, −30 on a sinking and +10 to the rival (traders lean Crown); rescue by reaching any open harbour while foundering docks her with 1 HP; RunOver + cause), `SparringCaptain` (closes to range, holds a beam reach, fires the bearing broadside), `ShipInput` extended (FirePort/FireStarboard/Order/ToggleLantern/Action). Godot: `FleetView` (other ships, interpolated, hidden beyond vision, faction ensigns), `EffectsView` (balls with trails, barrels/chests bobbing, smoke blots, splinters, splash rings, ram bursts, sinking whirl, glints; camera shake), firing arcs on Q/E hold, HUD hull/water/leak gauge, gun readiness discs with reload progress and shot count, crew-order line, LAST STAND overlay with the glass, SUNK overlay (logbook in M10); `--sparring`.

Evidence: `dotnet test` 77/77 (14 combat tests: broadside cost/hit/rep, manning reload, max-range splash, leaks per 10% + water, pumps/carpenters/patch/timber, no-timber, last stand → sink → run over, pumped stand recovery, harbour rescue, ramming by angle and mass, flotsam spill/drift/collect/expire, crew orders, sparring fight, save/load of damage); `--selftest` 56/56 (adds arcs, E fires two guns, target hit, HUD shot count, orders 3/1, last stand overlay show/clear); screenshot `scratch/m4-spar2.png`.

## M5 — AI ships, reputation, port closures, director, Threat bar, presets (done 2026-09-23)

Exit criterion: Threat curve plotted per preset in tests. **Met** (`ThreatCurvesMatchThePresets`).

Delivered: `Pathing` (A* on the nav grid with wind-aware move costs, string-pulled), `Director` (presets Calm/Rough/Tempest → k 0.10/0.15/0.22; Threat = 1 + k·days; tiers Flat Calm…LAST TIDE at 1/1.5/2/2.75/3.5/4.5/6/8/10; +10% enemy hull/damage per +1.0; credits 0.06/s × Threat, a card check every 10 s, six cards as drafted with Crown cards gated on Crown hostility, dearer cards weighted up with Threat, max 6 hunters), `AiState` + `Seamanship` (tacking through no-go zones with a 14 s tack hold, land avoidance on three bearings, path following, engage = close to 70% range then hold the target abeam and fire within ±22°, flee on the fastest point of sail), captains: `MerchantCaptain` (voyages between ports moving real stock: takes 30% of a producer's stock up to 30 units, adds it at the consumer; flees anything hostile within 450 m; 20–45 s in port), `PatrolCaptain` (Crown, circles home within 750 m, engages hostiles within sight, breaks off beyond 1.4 km), `RaiderCaptain` (Brethren, prowls lane waypoints within 1.5 km of its haven, engages prey, runs home under 30% hull), `HunterCaptain` (director spawns; gives up after 90 s beyond 2.5 km or when the player sits inside a fort's ring). `World.Fleet`: population at run start (merchants at 70% of non-pirate ports, patrols at 60% of colonies, a raider per haven), hostility rules (pirates hostile from day one; Crown at rep ≤ −20; merchants never attack), near ships (≤ 1.5 km) get full physics/combat while far ships slide along their lanes at polar speed, forts fire 3 balls every 10 s at hostile hulls inside their ring, spawns just beyond vision with Threat scaling. Save v2 now carries every AI field, flotsam, balls in flight, fort clocks and the director. Godot: `--preset`, `--quiet`, Threat bar with tier ticks and name, off-screen hostile chevrons with hull names.

Evidence: `dotnet test` 86/86 (lanes stay at sea across 20 port pairs; Threat/tier/scale values; director buys a card after earning credits and places it 80–470 m beyond vision; hunters close and fire; hostility matrix; merchants flee and patrols engage; forts fire on hostile players only; a populated sea (20–70 ships) trades within 4 min and runs 240 s of sim in < 12 s; save keeps the fleet and hashes equal); `--selftest` 58/58; screenshot `scratch/m5-sea.png` (merchant leaving the home harbour, Threat bar); FPS 55.4 with the full fleet (perf pass in M11).

## M6 — Weather and day/night (done 2026-09-23)

Exit criterion: playtest (Nolan's round comes after the build); verified by tests, self-test and screenshots.

Delivered: `Weather` (regional table: wind factor, fog chance, doldrums chance, storm rate, dawn fog, ash; slow noise fields for fog/doldrums/ash; storm cells 250–450 m that form 700–1100 m upwind of the player outside sight at a rate of one per ~3 min in Storm Reach at Threat 1, scaling with region and Threat, drift downwind at 0.4× wind, live 90–200 s, at most 3; inside: wind ×2.2–3 swirling around the eye with gusts, rain, swells that shove the hull, and a 3%/s × strength chance of torn sails at full canvas (−30% speed until a shipwright mends them for 25 gold)), `World.Weather` (`WindAt` = wandering field × region × doldrums × storm; a pinned wind bypasses all of it for tests; vision 500 by day, 250 lit / 150 doused at night with a dawn/dusk blend, fog down to 180, rain and ash −40%; the lantern (L) makes her a beacon at 650 m or a shadow at 200 m; hunters press 1.5× at night and search her last position when they cannot see her). Godot: `weather.gdshader` wash (dusk amber, indigo night with a warm lantern pool, fog haze thickening past sight, rain cast + slanting streaks, ash specks), `WeatherView` storm swirls, HUD weather line, `--time=`, `--storm`, `--region=` debug args.

Evidence: `dotnet test` 91/91 (vision by dawn/night/lantern/fog and detection ranges; fog banks foggy > 40% of samples vs Trade Isles < 10%; regional wind ratios and doldrums < 15%; storms form in Storm Reach, blow > 1.8× the base, drift, rain, cut vision, tear full sails and the shipwright mends them; night director rate; weather saves and hashes equal); `--selftest` 62/62 (adds L lantern toggle, storm rain on the wash and HUD, vision cut, clearing); screenshots `scratch/m6-night.png`, `m6-fog.png`, `m6-storm.png` reviewed.

## M7 — Monsters (done 2026-09-23)

Exit criterion: each beast beatable and escapable in a scripted test. **Met** (`MonsterTests`).

Delivered: `Monsters` (table of six with HP and achievements; targets = shootable parts; eruptions), `World.Monsters` (a roll once an in-game hour in a beast's region at 10% × Threat, doubled at night, a 60 s rest between encounters; beasts lie 200 m ahead of a moving ship). Reef Serpent (36 HP): dives, surfaces 22 m abeam (alternating sides) for 9 s, bites (8 dmg + 1 leak), dives 12–17 s; dies to broadsides while up; gives up outside the Shoals. Kraken: closes at 20 m/s, four tentacles (12 HP) at ±20° off each beam pin the ship (speed halved each tick), grind 1.5 HP/s and open a leak every 8 s; cutting three frees her (Unkrakened). Ghost Ship: only visible in fog or at night, holds station 120 m abeam on a parallel course (swaps sides every 35 s), fires two 4-dmg balls every 12 s, solid only during 3 s lantern flares every 6 s (50 HP; Lay the Ghost), fades 12 s after clear daylight. Giant Crocodile: waits on the nearest bank, lunges at 14 m/s within 90 m, bites 10 dmg and jams the rudder 5 s, slides back surfaced (40 HP; Handbags); gone outside the mangroves. Weed-Kraken: wraps the stern, halves speed, +3% water/s; the mass (50 HP) is shot or cut by carpenters at 5 HP/s each (Weeded Out); the Sargasso itself slows every hull to 60%. Siren: on the nearest rock, her song biases the rudder toward her (0.6 at 0 m → 0.36 at 250 m) within earshot; shoot the perch (50 HP; Deaf Ears) or sail clear. Volcanic eruptions every 30–70 s (÷√Threat) near the player: 5 s telegraphed 60 m zone, 20 dmg + a leak to anything inside. Achievements collect on the player and toast; the logbook cause names the beast. Godot: `MonsterView` marginalia (serpent humps, kraken arms with tentacle HP, ghost hull with lantern flares, crocodile, weed tangle, siren rock with song rings, eruption warning rings and rock splashes), HUD lines for beast/held fast/jammed/song, `--monster=`.

Evidence: `dotnet test` 98/98 (monster tests: roll cadence and regions, serpent bites/dies/escapes, kraken pins/smashes/freed, ghost fires/passes through/struck in flares, crocodile jam + siren pull/silenced, weed drag/cut + Sargasso drag, eruptions + save/hash); `--selftest` adds serpent named/beaten; screenshots `scratch/m7-kraken.png`, `m7-ghost.png`.

## M8 — Progression (done 2026-09-23)

Exit criterion: balance pass (the autopilot balance runs come with M11; the mechanics are tested here).

Delivered: `Parts` (9 parts × 5 grades; price = base × grade × (0.6 + hull cost/40000), cannons per gun; effects: sails +3% speed and +15% sail handling/grade, rigging −3° pointing/grade, planking +12% hull and −1.5% speed/grade, copper +2% speed and 12% leak save/grade, cannons 4→6→9→12→18→24-pdr, ram +25%/−12%, pumps +30%, hold +10%, lantern +10% night radius and spyglass reach), hull purchase (price = cost − trade-in; trade-in = 60% of hull + the parts that stay; cannons carry over up to the new slots with extras sold at 40 gold, lantern and pumps move, sails/rigging/planking/copper/hold/ram stay; crew beyond the berths paid off; cargo must fit; Galleon Captain / Ship of the Line), officers (one slot per type, 1/2/3 berths by hull; taverns offer one of each type at a tier rolled per port and day; prices 60/150/400, wages 4/8/15; Lookout +15/30/50% sight and 100/200/300 m warnings; Marines volley every 6 s within 120 m killing 1/2/3; Quartermaster 3/6/10% better prices, −10/20/30% wages, provisions last +15/30/50%; unpaid officers walk at the next dock), friendly standing (rep ≥ 20) 10% better prices, custom crew stations (`SetStations`, order Custom; riggers short of the hull's need cut sail handling, top speed to 80% and helm to 70%), the spyglass (RMB: 900 m × lantern grade along a 20° cone toward the cursor; inks the chart and reveals ships). Godot: shipwright tab with part rows (grade dots, effect, price) and hull rows (stats, trade-in, buy), tavern officer rows (offer, tier, price, wage, effect, aboard, hire/dismiss), crew panel (C) with orders and +/− per station, spyglass cone drawing, views rebind after a hull change.

Evidence: `dotnet test` 105/105 (parts prices/effects, hull carry-over and trade-in, officers and prices, marines volleys, custom stations and short riggers, saves and command-log replay); `--selftest` 74/74 (adds crew panel open/set/close, spyglass cone sees 700 m ahead and not astern, shipwright buys sails and a cutter, tavern hires a lookout); screenshot `scratch/m8-shipwright.png`.

## M9 — Exploration (done 2026-09-23)

Exit criterion: a bottle-map puzzle is solvable in a test. **Met** (`BottleMapsMatchWhenTheCoastIsSightedAndDigsTakeFifteenSecondsFurled`).

Delivered: `Exploration.cs` (`BlackMarketDef` table of six uniques — ghost-grey sails 650 (hunters spot you 30% closer), smuggler's keel 550 (points 5% closer), long nines 700 (+40 m range), bilge engine 500 (pumps ×1.5), hidden hold 600 (+15% cargo), false colours 450 (grudges fade 4/day) — each cove stocks 1–2 by seed + port id; `BottleMap` (treasure id, solved, sketch rotation); `CoveHint`), `World.Exploration` (black market buy via `PortAction.BuyUnique`; uniques live on the captain and reapply after a hull change; tavern bottle maps 60 gold via `PortAction.BuyMap`, also 30% of wreck salvages; a map matches when the ship comes within 150 m of the sketched island's bound circle → the X is pinned and inked; digs need the dig ring + Furled + F, take 15 s, and break on any sail or leaving the ring; reward 150–400 gold × (1 + 0.2·(Threat−1)) + 1–3 units of a rare good that fits + 20% chance of one of 15 cosmetics; X Marks at 5 digs; Sargasso wrecks salvage in 10 s for 5–12 units of Sargasso produce + 30–80 gold; 30% of rumors mark a "?" area (radius 400 m, centre within 250 m of an undiscovered cove) that clears once the cove is found; achievements ticked once a second: Nine Seas, Cartographer (80% inked), Smuggler's Welcome (3 coves), By a Hair, Last Tide, Fair Winds / Heavy Weather / Eye of the Storm by preset and day). `ShipInput.Action` (F at sea) drives digs through the tick. Save v2 carries uniques, maps, hints, cosmetics and a dig in progress; all of it is in the hash. Godot: shipwright tab grows a red **Black market** block at coves; tavern shows the bottle map on offer (region named) with a buy button; HUD prompt for dig/salvage ("furl first", countdown); `MarksView` draws the pinned X, the dashed dig ring and revealed wrecks; `ChartScreen` draws X's, "?" rumor circles, wrecks and the torn bottle-map sketches (island outline rotated by the map's own angle, X inside, matched/unmatched caption); `--treasure` and `--dock=cove` debug args.

Evidence: `dotnet test` 111/111 (6 exploration tests: black market stock/buy/effects/carry-over, map buy → match → furl rule → dig progress → interruption → reward, wreck salvage, cove rumor hints and clearing, achievements incl. Tempest Day 12, save/load/hash mid-dig); `--selftest` PASS (adds map matched beside its island, HUD dig prompt, F starts digging, countdown, interruption); screenshots `scratch/m9-cove-shipwright.png`, `m9-tavern.png`, `m9-dig.png`, `m9-chart.png` reviewed.

## M10 — Run wrapper (done 2026-09-23)

Exit criterion: full-run playtest (Nolan's; the flow is verified by the self-test and screenshots).

Delivered: `Profile` (`user://profile.json`, atomic temp+rename writes: one best per preset with ship, date and cause; achievements; unlocked cosmetics; hints seen; last preset/name/loadout; voyage count) and the suspend save (`user://suspend.json`: written on every dock and on Save & Quit, consumed on resume, deleted when the ship is lost); `TitleScreen` (home: New voyage / Resume voyage when a save exists / The logbook / Quit + a bests line; voyage page: three preset cards with growth, target and best, the five cosmetic slots cycling through plain + unlocked keys, ship's name with a random-name button; logbook page: bests per preset and the 17 achievements with descriptions and the cosmetic each unlocks); `LogbookScreen` recap as a ruled ship's-log page (Log of the <ship>, lost on day/watch/preset, cause, days to 0.01, gold, ships sunk, ports, leagues, best trade, treasures, coves, a red "personal best" line, achievements earned this voyage; New voyage / Return to the title); `PauseMenu` (Resume, Save and quit to the title, Quit to the desktop; Esc and Q keep their old meaning); `Hints` onboarding (13 once-per-profile cards under the HUD: furled at the start, the wind rose, in irons, tacking, the harbour ring, prices, first hostile, first leak, night and the lantern, the Threat bar, the chart, the spyglass, storms; never in debug runs); sim: `Cosmetics` slot/option tables, `Player.Loadout` (saved + hashed), `Names.Ship`, `Achievements.All` + the cosmetic each rewards, `World.DaysSurvived` is the score; ships render the loadout (hull colour, sail colour/stripes, stern flag, figurehead glyph, wake ink). `Main` was split into persistent screens + `BuildViews`/`FreeViews` so a new voyage rebuilds the world beneath them without a scene reload; a run with no CLI args opens the title over a fresh sea. Debug args: `--title[=voyage|logbook]`, `--logbook`, `--pause`, `--hints`.

Evidence: `dotnet test` 114/114 (+3: cosmetic/achievement tables, ship names + loadout save/hash, days-survived score with the clock frozen in port); `--selftest` PASS (adds: title holds the sim, Set sail starts the chosen preset/name, profile remembers, Esc opens the pause menu, Save & quit writes the save and returns, Resume restores the same hash and consumes it, docking autosaves, the first hint shows and is remembered, making sail retires it, the last stand ends the run, the logbook opens with the cause and a first best, the save is dropped, New voyage from the logbook, a blank name gets a random one); screenshots `scratch/m10-title.png`, `m10-voyage.png`, `m10-bests.png`, `m10-recap.png`, `m10-pause.png`, `m10-hint.png` reviewed.

## M11 — Release polish (in progress, 2026-09-23)

Done so far:
- **Options** (`game/Settings.cs` + `game/OptionsScreen.cs`, `user://settings.json`, from the title and the pause menu): key rebinding for all 14 keyboard actions through a press-to-capture button (one key holds one action; Esc keeps the old one; reset to defaults), fullscreen, window size (1280×720 … 2560×1440, centred), UI scale 75–150% (`Window.ContentScaleFactor`), master/ambience/effects volumes on real buses, the colorblind-safe palette (Okabe–Ito vermilion Crown / blue Brethren; chart chunks redraw), first-voyage hints on/off. `Main.OnKey` maps keys through the settings; the HUD key line is built from the bindings. Debug runs ignore the saved window/keys so screenshots stay comparable.
- **Audio** (`tools/audio/build.py` → `assets/audio/*.wav` + `manifest.json`, 32 sounds, 8.3 MB, every one synthesized with NumPy, no samples; `docs/LICENCES.md`). `game/Audio.cs` loads them at runtime (`AudioStreamWav.LoadFromFile`, loops crossfaded seamless), creates the Ambience/SFX buses, and drives eleven loops from the world (wind low/high by speed with pitch, waves by speed and storm, timber creak by speed and turn, rain in storms, harbour bustle and gulls by distance to a known port, pumps, digging, water rushing in with leaks, the siren's song by distance) and one-shots from sim events (cannon by distance, splinter hits, splashes, ram, sinking, coins, rescue bell, notice quill, rigging on sail changes, Threat-tier bell, monster cues, eruptions, beat-to-quarters drums on the first hostile in sight, thunder in storms, gusts) plus UI clicks and the dock bell. Menus duck the ambience. Debug runs and the self-test are muted (`--sound` unmutes, `--mute` forces silence).

Evidence: `dotnet test` **118/118** (adds `AutopilotTests` ×3, `AHarbourRescuesHerOnlyOnce`); `--selftest` PASS in the editor build, in `build/linux/LastTide.x86_64` and in `build/windows/LastTide.exe` under Proton 11 (adds: all loops loaded, wind and waves audible at sea, sail change → rigging, broadside → cannon, harbour bustle at the quay; rebinding W→K through the real key path and the key line, capture binds and unbinds, palette swap, UI scale, settings persist, Esc closes Options); screenshots `scratch/m11-options.png`, `m11-fleet40.png`, `m11-founder.png` reviewed; `--fps` 59.1 (1500 frames, vsync) and 57.5 with `--fleet=40`.

- **Juice**: hull flashes red on hits and rams (`ShipView.Flash`), sails belly-and-settle on every sail change (`SailSnap`), muzzle flashes on every broadside (`EffectsView` Flash), the HUD bounces gold changes, sail levels and new Threat tiers (`Hud.Pulse` tweens), and a foundering ship drowns the screen edges in indigo (`weather.gdshader` `founder`, ramping with the last stand and after the loss).
- **Perf pass** (`--fps` now reports tick/process/render/GPU times, spikes, GC and draw calls; `--off=` bisects layers; `--fleet=N` crowds the sea): the chart was 3,660 of 3,838 draw calls (10 ms render CPU → 54 fps). Batched multilines for hachures, shallows dots, harbour rings and the wake → **219 calls, 2.0 ms render CPU, 59.1 fps vsync-on**; **57.5 fps with 40 extra ships within 1.2 km** (tick 1.35 ms, 265 calls). Merchant lanes cached per port pair. Headless sim: 0.27 ms/tick populated (125× real time).
- **Autopilot + balance harness**: `src/Sim/Autopilot.cs` (see GDD §19 "Autopilot"), `scratch/balance` (parallel voyages per preset → `out/*.jsonl` + summary), `scratch/tune pilot <seed> <days>` traces one voyage. Tests: `AutopilotTests` (profitable start, runs to the cap, flees the strong / fights the weak).
- **Director tuning round 1** from the first batches (GDD §19): early hunters at half guns/hands, hunter cap 1+⌊Threat⌋, 180 s patience, 150 s quiet after a hunter leaves; harbour rescue once per port per voyage (an unlimited rescue let a broke 1-HP sloop sit out the day cap). Balance numbers and reading: `docs/BALANCE.md` (400 voyages across the three presets; medians near the Rough/Tempest targets, Calm's median under target, top quartiles at the caps via fort sheltering, the lean start rarely compounds).
- **Exports**: `export_presets.cfg` Linux + Windows; `LastTide.sln`; runtime files packed as-is. `build/linux/LastTide.x86_64` passes `--selftest`; `build/windows/LastTide.exe` passes `--selftest` under Proton 11 Experimental (`docs/STEAM.md` has the commands).
- **Steam seam**: `game/Platform.cs` (`NoPlatform` now, `SteamPlatform` behind `#if STEAM`), achievements mirrored from `Profile.RecordRun`; `docs/STEAM.md` = Nolan's checklist.
- **Style bible**: `docs/STYLE-BIBLE.md` + four plan-view probes in `scratch/art-probe2/` (sloop hull, gaff sail, kraken marginalia, island stamp) — **waiting on Nolan's approval before any mass generation**.

Still to do: balance iteration to the GDD §11 day targets (see BALANCE.md), bug list to zero, then the art pass after approval.

## Audit and visual overhaul (2026-09-23 → 2026-09-26)

Nolan: "do a full audit on the game and make sure there arent any bugs, keep making visual and ui improvements. i want
this game to look amazing." Eight agents (four auditors, four visual) worked in isolated copies; the lead verified
the auditors' regression tests fail on the untouched build, merged, reconciled overlaps and fixed what the merges
exposed. **Summary and every per-area report: `docs/audit/SUMMARY.md`, `docs/audit/AUDIT-*.md`.**

- **178 fixes.** Critical/major among them: same-port buy→sell loops printed gold; re-docking farmed standing; any chart
  pin made every save throw; the game froze docked after a harbour rescue or resuming a dock autosave; quitting
  mid-voyage lost the voyage; AI ships froze on coasts and in irons (the economy stalled) and fired broadsides that
  could not hit; resumed voyages lost their AI captains; wind jumped at region borders and storms flipped across a
  line; ~30 % of maps had a port no big hull could reach (map generator v5); cannonballs flew through islands; the
  Kraken was certain death for the lean sloop (retuned to GDD §12's "each hit frees a hold").
- **The look:** hand-coloured antique chart — rag paper, quill strokes drawn from the sim's own wind, rhumb lines,
  coastal watercolour, watercolour islands with stamped vegetation/volcanoes/ruins, illustrated harbour towns,
  blooming fog of war, storms with forked lightning, sea life, region lettering; 14 plan-view hulls with in-engine
  sails, ensigns and paint; monsters as living marginalia; ink-smoke broadsides and full sinkings; a chart-ornament
  HUD (compass rose, day dial, Threat scale, instruments, edge markers honouring the Lookout); parchment-ledger port
  screens with goods icons; a ship's-log recap with a wax seal; an antique chart screen; title key art; app icon and
  boot splash. IM Fell font family. Every generated image is logged in `docs/AI-ASSETS.md`.
- **Save compatibility:** the suspend save no longer carries the input/command logs (1.4 MB → 85 KB); older saves load
  on their own map (`MapVersion`); new fields default. The profile format is unchanged.

Evidence (merged tree, 2026-09-26):
- `dotnet build`: 0 errors, **0 warnings** (baseline 3). `dotnet test tests/Sim.Tests`: **224/224** (baseline 118).
- `--selftest`: **PASS, 198 ok** (baseline 106) in the editor build, in `build/linux/LastTide.x86_64` and in
  `build/windows/LastTide.exe` under Proton 11; no logged errors (the self-test now fails on any).
- Real GPU (RTX 2070 SUPER, 60 Hz vsync): **59.2 fps over 40 s with `--fleet=40`**, 50 draw calls (baseline 265),
  GPU 1.4 ms a frame; at sea 44 draw calls (baseline 219). Tick spikes are the first ~1.2 s of a voyage (one harbour
  lane field a tick) — `--fps` now prints them.
- Galleries: 28 standard screens before/after + zoomed ships/monsters, reviewed at full size.

## Next
- Balance iteration against GDD §11 with `scratch/balance` (see `docs/BALANCE.md`). The audit changed the baseline
  (AI ships no longer freeze, broadsides aim, the Kraken retune, shot stops at land): re-run the three presets first.
- Nolan's playtest of the whole loop (title → voyage → logbook), then iterate on look and feel.

## Blockers / needs Nolan
- **Look sign-off:** the art pass was cut from `docs/STYLE-BIBLE.md` on his "make it look amazing"; any set can be
  regenerated from the same template if he wants a change.
- **Design calls the audit raised (not changed; details in `docs/audit/`):**
  - Brethren standing decays toward 0 by 2 a day, so from day 4 pirates stop attacking on sight (§8's decay rule vs
    "hostile from day one"). Recommendation: Brethren decay toward their starting −25 (sim-trade P-06).
  - Friendly standing and the quartermaster pay on both sides of the spread, so a round trip between two friendly
    ports pays with no price difference (sim-trade P-01); four goods have no consuming region (P-02); wages can be
    cut 21–23 % by ordering all hands to the pumps before midnight (P-04); a wreck trades in at a sound hull's value
    (P-05); the quartermaster's provisions bonus vanishes for small crews (P-03).
  - Big AI hulls sail into the Mangrove Maze (the rule binds only the player; sim-combat P-04); a sunk Crown hunter
    pays the Crown's own bounty (P-06).
- **His real profile:** on 09-23 some audit capture runs wrote into `~/.local/share/godot/app_userdata/Last Tide/`
  (profile.json voyages 4 → 5 plus a 0-day Rough Seas best, a stray `debug/profile.json`, rotated logs). The runners
  now refuse real user folders. Reverting the file is his call.
- Git: still not under version control — `git init` + private GitHub repo? (never commit/push without his yes).
- Steam: Steamworks account/App ID/fee, the price point, store assets (`docs/STEAM.md`).

## Bug list
- (open, minor) The Options "UI scale" scales the whole window, sea included, not only the interface (hud R-03).
- (fixed 2026-09-26) 178 fixes from the audit — `docs/audit/SUMMARY.md` and the per-area reports.
- (fixed 2026-09-26, hardened) The unreproduced spontaneous pause / flaky self-test cascade: the self-test now tags its
  own input and ignores real keyboard/mouse events; every developer-run pause logs its source (audit GC-18).
- (fixed 2026-09-23) Exported builds could not find the string table or the audio: files read at runtime are now packed as-is (`importer="keep"`).
- (fixed 2026-09-23) The .NET exporter needs a solution file; `LastTide.sln` added.

## Decisions made while building (also in GDD §19)
- Rudder auto-centres on release; Esc pauses and Q quits from pause; fixed-size 1600×900 window until the options menu exists; default zoom 1.25 (range 0.3–2.5); camera lead 4 s × speed capped at 120 m.
- Tuning: QuadDrag 0.018, LinDrag 0.03, TurnDrag 0.25, helm authority full from 35% speed with a 0.5 floor under sail, TurnLag 0.4 s (from the sweep harness: tack ≈7 s, 180° ≈9 s).
- `Time` is derived from the tick count so day/watch boundaries are exact.
