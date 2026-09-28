# Progress

Living status for Last Tide. A new session must be able to resume from this file alone.
Roadmap and exit criteria: `docs/GDD.md` §18. Decisions: GDD §19 (the "M1 build" block holds the tuning choices).

## Status by milestone

| M | Milestone | State |
|---|---|---|
| M0 | Project skeleton | **Done** 2026-09-23 |
| M1 | Sailing feel | **Built** 2026-09-23. Gate lifted by Nolan the same day: finish the whole game, then iterate on feel. |
| M2 | World gen, regions, ports, chart reveal | **Done** 2026-09-23. 2026-09-27: v6 world — landform islands, 18 × 13.5 km chart, 12 regions, no edge (see "World v6" below). |
| M3 | Ports and economy | **Done** 2026-09-23 |
| M4 | Combat | **Done** 2026-09-23 |
| M5 | AI ships, reputation, director, Threat | **Done** 2026-09-23 |
| M6 | Weather and day/night | **Done** 2026-09-23 |
| M7 | Monsters | **Done** 2026-09-23 |
| M8 | Progression: hulls, parts, crew, officers | **Done** 2026-09-23 |
| M9 | Exploration: coves, black market, bottle maps, digs, wrecks, achievements | **Done** 2026-09-23 |
| M10 | Run wrapper: title, presets, cosmetics, name, logbook, bests, suspend save, hints | **Done** 2026-09-23 |
| M11 | Art, audio, Steam, balance, options, perf, exports | **Done** 2026-09-26: art pass, full audit (178 fixes) and visual overhaul — see "Audit and visual overhaul" below. 2026-09-27: harbour office (contracts, survey fees, Crown bounties, smuggling). Water follows the hull; pumps removed. Open: the next balance round and Nolan's playtests. |

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

## Harbour office (2026-09-27)

Nolan asked for more ways to make money. The lean start rarely grew, and he picked four from a brainstorm (passengers and
taking prizes were declined). All four live in a fourth port tab, the **Harbour office**. Rules and numbers: GDD §6
"Harbour office" and §19.

- **Sim:** `src/Sim/Contracts.cs` holds the kinds table (freight, dispatches, contraband), `Contract` and `Receipt`.
  `src/Sim/World.Office.cs` covers:
  - the per-day board, drawn per slot from (seed, port, day, slot)
  - signing and giving up (logged `PortAction.SignContract` / `AbandonContract`) and deadlines
  - delivery on docking, the survey fee on a first visit, and Crown bounties (earned when a Brethren ship sinks, paid
    at Crown ports)
  - customs searches on docking and Crown patrol searches at sea
  
  Contract crates count in `Player.SlotsUsed`. Contracts, signed slots, bounties owed and patrols that searched are
  saved, hashed and replay. `Autopilot` signs freight and dispatches bound where it is going, and when exploring takes
  a paid passage to an unvisited market no farther than its own pick. It doesn't smuggle.
- **Game:**
  - `PortScreen`: the office page (board, contracts in hand with Give up, bounties, the arrival account) and a
    "+N gold on arrival" line under the purse. Q/E page through four tabs.
  - `ChartScreen`: red double rings and "due Day N" on contract ports, a key entry, and hover lines.
  - `Hud`: notices can carry values (`"KEY|arg"`).
  - `--deliver` debug arg.
  - Strings in `en.csv`.

Evidence (2026-09-27):
- `dotnet build`: 0 errors, 0 warnings. `dotnet test tests/Sim.Tests`: **242/242**, including 18 new `OfficeTests`.
- `--selftest`: **PASS, 204 ok**. New checks: office strings, office tab by click, Sign and Give up by click, E wraps.
  `--playtest=3,1 --seed=21`: PASS, 0 errors.
- Screenshots reviewed: the office board, an arrival at 100% and 150% UI scale (`--seed=21 --deliver --dock
  --page=3`), and the chart's delivery rings.
- Balance, Rough · 200 voyages · same seeds (`docs/BALANCE.md`):
  - voyages that grew past 200 gold: 36 → **91**
  - voyages that bought a bigger hull: 15 → **30**
  - median day survived: 16.0 → 16.3

## Ship card (2026-09-27)

Nolan: "i should be able to hover over ships and see information about them." Details are in GDD §15 and §19.

- `game/ShipCard.cs` picks the ship under the pointer and draws the red ring and card, on canvas layer 11.
- The pick tests the hull's capsule on screen with 8 px of slack, so small ships at low zoom are still easy to hit.
- The card is lettered again only when what it shows changes.
- `World.NameOf` names every AI ship from the seed and its id.
- `Main` feeds the card the pointer each frame, and clears the pointer when the mouse leaves the window.
- `--hover` points at the nearest ship on the screen (for screenshots).

Evidence: `dotnet test` **243/243**. `--selftest` **PASS, 207 ok, twice in a row**. The three new checks, through real mouse
motion: a ship's card shows name, stance and bounty; your own ship's card; no card on open water. One earlier run
failed "holding A turns her to port" once; that check runs before any hover code, and it passed on both reruns.
Screenshots reviewed: `--sparring --hover --zoom=0.6` and `--fleet=10 --hover --zoom=0.5`.

## Merchants fight back; fullscreen keys (2026-09-27)

Nolan: "merchant ships should be able to defend themselves but also still be trying to get away", and he could not make
the window fullscreen. Details are in GDD §8 and §19.

- `Seamanship.FightingRetreat` (`src/Sim/Ai.cs`): the flee course (`FleeHeading`, split out of `Flee`), bent up to
  `YawLimitDeg` 50° to bring a loaded side abeam of an attacker in gun range; fires when `ClearShot` bears.
  `MerchantCaptain` uses it whenever a threat is in sight. Merchants carry `World.MerchantGuns` = 4 (was 2).
- `project.godot`: `window/size/resizable=true` (it was false, so KWin would not maximise or fullscreen it).
  `Main._Input`: F11 or Alt+Enter toggles `Settings.Fullscreen`, saves, applies, and refreshes Options if open.

Evidence: `dotnet test` **245/245** (two new: a merchant answers a raider alongside, hits it, keeps fleeing, and still
never attacks on sight; she holds course and fire for a pursuer dead astern, yaws for one just abaft the beam, fires the
side that bears, and stops yawing for a side that is empty). `--selftest` **PASS** with two new checks at the end: F11
goes fullscreen and saves it; Alt+Enter goes back to a window. Not run: the balance batch (`scratch/balance` is no
longer on disk; the autopilot never attacks merchants, so its numbers are only touched through raiders taking fire).
No screenshot of a merchant firing.

## UI/UX pass (2026-09-27)

Nolan: "audit the game and make ui and ux improvements". A 30-screen gallery (every page, 100 % and 150 % UI scale)
was reviewed at full size; what it found and what changed:

- **Chart** (`game/ChartScreen.cs`):
  - It opened on the whole sheet with the charted sea (11 % at the start) crammed into one corner. Now it opens framed
    on what she has charted, her position and her deliveries (zoom 1–3.5), and Space centres it on the ship.
  - Port names overlapped islands, glyphs and region names, and ran into the neatline. Names now try 8 places and take
    the one that covers least: lettering, glyphs, the ship and pins first, then land, then region names. The "due Day N"
    labels are placed the same way.
  - Region names are measured at the size they are drawn, and keep clear of port names and of each other.
  - Sea monsters no longer sit cut off at the neatline. The scale bar says ⅛, not 0.125.
  - At 150 % the subtitle ran under the cartouche. The cause was a stale minimum size in `UiCartouche`, which now
    re-measures itself when resized; this also affected the port and title plates.
  - The one-line help squeezed into the bottom-right corner is now a "Using the chart" list opposite the key, built
    from the bindings.
- **HUD:**
  - The Threat tier ("Flat Calm") sat uncaptioned beside the clock and read as a weather report in a 20-knot breeze.
    It now has a "Threat" caption.
  - Edge markers step out from under a hint note.
  - The treasure ring's "dig here" was lettered under the ring, beneath the HUD prompt; it now sits beside the ring.
- **Port:**
  - The market has a **max** button (as much as purse, hold and stock allow, or everything held).
  - Buying a new hull, giving up a contract and dismissing an officer ask **Confirm?** once (red, 4 s) before acting.
  - At 125–150 % the header and footer step in from the corner scrollwork.
  - Hull descriptions and the good's group line wrap instead of ending in "…".
- **Ship card:** your own card lists your deliveries and their days, soonest first; red under a day left (GDD §15).
- **Text:**
  - "said to lie in the The Shoals" → a prose form of each region (`REGION_IN_*`).
  - Copper: "12% likelier to open no leak" → "12% fewer leaks from hits".
  - "1 officer berths" → "officers 1".
  - Officer hire buttons say "gold".
- **Title:** the voyage page's preview wake ran off the patch of sea onto bare paper; it now fades out inside it.

Evidence (2026-09-27):
- `dotnet build`: 0 errors, 0 warnings.
- `--selftest`: **PASS, 214 ok**. New checks: the chart opens framed with the ship on it (zoom 1.97); Space re-centres;
  − returns to the opening zoom; **max**; Give up asks once, then acts on a second click; the own card lists the
  delivery.
- `dotnet test`: **246/246**, run with the concurrent pumps rework in the tree too.
- Before/after screenshots reviewed: chart (start, delivery, full reveal, 150 %), sea, hints, treasure, market,
  shipwright, tavern, office (100 % and 150 %), voyage page.

## Water follows the hull; pumps removed (2026-09-27)

Nolan disliked the pumps and asked for the water to rise below 60% hull and fall above it, in proportion to the
distance from 60%. Details are in GDD §8 and §19.

- `Ship.DamageTick`: water changes by `FloodPerSecond` = `FloodRate` 10 × (`FloodLine` 0.6 − hull fraction) %/s, so
  1%/s per 10% of hull from the line. Leaks no longer add water; they still stop the patching until plugged.
- Pumps gone: the fourth station is **spare hands** (same wage, no effect); the Pumps part (`Part` has 8 entries now;
  a 9-entry save drops index 6) and the bilge engine unique (dropped from a save on load) are removed; order 3 is
  "Repair" (carpenters first); the pumps sound loop is no longer played.
- Last stand: a flat `StandGlass` 35 s (was 20 s + 3 s a pumping hand, up to 35). Carpenters keep patching through a
  water-only stand, so she can patch over the line and drain below 80% to end it.
- The Weed-Kraken's grip also stops her draining (else a sound hull cancelled its +3%/s).
- HUD: the hull tube marks the flood line at 60%; the water-stand hint says "carpenters first: patch her over the flood
  line"; the crew panel and ship card show "spare". `--laststand=water` now starts at 50% hull.

Evidence: `dotnet test` **246/246** (rewritten: the water follows the hull both ways and leaks add none; carpenters plug,
patch over the line and she drains; a water-only stand is patched out of; new: an older save drops its pumps grade and
bilge engine). `--selftest` **PASS**. Screenshots reviewed: HUD at 45% hull, a water last stand, crew panel. Not run:
a balance batch; expect big hulls to flood more than before (they used to have dozens of pumping hands) and a sloop
under 60% to flood more slowly than it did with several open leaks.

## World v6: landform islands, a 3× chart, twelve regions, no edge (2026-09-27)

Nolan asked for islands with real-world structure (sizes, shapes, clustered placement, lagoons, bays), then for no map
edge (monsters and whirlpools thicken further out), then for a much bigger map (he chose 3× each way, the nine regions
bigger plus new ones). Design and numbers: GDD §5 and §19 ("Landform islands", "Bigger chart", "Beyond the chart",
"Whirlpools and drift ice"). The cartographer (same day) is its own section in §19.

- **Generator v6** (`MapGen`, `Landforms`, `Coastlines`, `RegionLayout`): 12 regions on a 4 × 3 grid over 18 × 13.5 km;
  eleven landforms (great/high islands with coves or rias, groups, hotspot chains, atolls, almost-atolls, calderas,
  barrier islands, drowned ridges, mangrove deltas, cays, stacks) traced from a noise-roughened shape field with
  closing/opening and lake filling, so islands are simple polygons and lagoons open to the sea. Each region builds its
  signature landform, then fills round 3–5 island groups to its land share. Ports prefer sheltered harbours; big
  islands hold several; harbours, digs and wrecks lie in open sea. Coastlines are kept when only the ports re-roll.
  Older map versions are gone: a suspend save from before v6 is refused and set aside.
- **New regions:** Ice Reach (drift ice: `Ice`, `World.Ice`, `IceView`), Maelstrom Straits (whirlpools in the narrows
  and a great maelstrom: `MapGen.PlaceWhirlpools`, nav cores closed), Corsair Keys (Brethren-heavy, raiders ×1.8).
- **Beyond the chart:** no wall (`World.KeepOnTheChart` keeps only the Mangrove rule, on-chart only); outer
  whirlpools (`Whirlpools`) and faster, surer beasts (`World.OuterMonsterRoll`, `EdgePressure`); `WhirlpoolView`; the sea
  shader drops the neatline and darkens the water out there; the sea letters HERE BE MONSTERS.
- **Scale work:** island spatial index (`Map.IslandsAround`, used by hull collision and line of sight); merchant lanes
  from windowed sea-distance fields (`NavGrid.LocalDistances`, 3.6 km) instead of one full-chart field per harbour;
  `NavGrid.IsOpenSea`; the sim builds optimized in every configuration; SDF texel 8 m; chart chunks 18 × 15 with bounds
  grown to their islands; stamps count real island area; `--at=x,y` launch flag for screenshots of far waters.
- **Tests:** new `EdgeTests` (sails past the edge; whirlpools crowd in with distance and are pure functions of the
  seed; a whirlpool draws an idle hull into its eye, turns her and holes her; beasts rise within seconds 1.8 km out;
  the Maelstrom's whirlpools sit in the region, clear of harbours, cores closed to the nav grid), `IceTests` (ice only in
  the Ice Reach and off every shore; it drifts and replays exactly; a fast blow holes her, a crawl doesn't), new map
  tests (coastlines simple and wound alike; every region shows its signature; sizes span orders of magnitude; ports
  favour shelter), `AThousandSeedsValidate` now runs the thousand maps in parallel. Tests that leaned on the old
  map's geometry (fixed search windows, "the nearest port", starts inside the old chart) now search the new one.
  Old-map resume is replaced by a test that an older chart is refused.
- **Self-test:** two new checks through the real keys: W carries her past the chart's east edge, and the HUD then
  reads "Uncharted waters". "She strikes the island" now approaches a solid island's real shore.

Evidence (2026-09-27): `dotnet test` **267/267** (the 1000-seed sweep included); map generation ~1 s a map; sim tick 1.6 ms with 135
AI ships on the 151-port chart (headless, loaded machine). `--selftest`: all map/edge/cartographer checks ok; the one
failure in my run ("her own ship shows her card") was a camera race from the new edge check's teleport, fixed by the
hull session. Screenshots reviewed: whole-map renders of several seeds, the chart screen (whole sheet, and her marked
at its edge when off it), the great maelstrom, the drift ice, the open sea beyond the chart (HERE BE MONSTERS, fog
round her sight, whirlpools), the title. Not done: a balance batch on the 3× chart (runs will be longer in days;
GDD §11 targets need re-checking), and the chart screen's port names crowd at whole-sheet zoom.

## The cartographer (2026-09-27)

Nolan: "You should have to hire a cartographer to use the map, and you should only be able to see what you have
'discovered' while you have a cartographer aboard." Rules and numbers: GDD §19 "Cartographer".

- **Sim:** `OfficerType.Cartographer` with `Officers.CartographerReach` (1 / 1.25 / 1.5 × vision), `CartographerPrice`
  (40 / 120 / 320), `CartographerWage` (3 / 6 / 12), and per-type tables `PriceOf`/`WageOf`/`UsesSlot`
  (`src/Sim/Parts.cs`). `Player.SlottedOfficers` counts slot officers only; `HireOfficer` and the hull-change trim
  skip him. `World.HasCartographer`, `ChartRadius`, `ChartAround` (`World.Progression.cs`): the tick's vision paint,
  the spyglass fan, a matched bottle map's X and port discovery by sight all need him; tying up marks a port only
  with him (`World.Port.cs`); signing him on inks the harbour at once. The home tavern's cartographer is always
  green. Saves write `CartographerRule`; an older suspend save resumes with a green one. `Autopilot` signs one on
  at the first port where it can pay and keep 30 gold.
- **Game:** tavern card (listed first, own portrait `assets/art/people/cartographer.png`, slot-free Hire),
  `PortScreen.OfficerHireButton`; crew panel lists the officers and warns while none keeps the chart; M without
  him queues `NOTICE_NO_CARTOGRAPHER` once (`Main.OpenChart`) and the key legend reads "chart (no cartographer)";
  a `cartographer` hint (the chart hint waits for him). `FogView.Sight` + `fog.gdshader` `sight`: an analytic live
  circle of the current vision radius, cleared every frame, on or off the chart; `FogView.SeaTexture` (the mask with
  that circle stamped in, clamped onto the border) feeds the sea layer so it draws inside her sight. Debug:
  `--cartographer[=0..2]`; `--chart` brings a green one. The playtest driver expects the chart key to open only with
  him aboard.

Evidence (2026-09-27, with the other sessions' work in the same tree):
- `tests/Sim.Tests/CartographerTests.cs`, 9 tests, all pass: nothing inked or marked without him (sailing past six
  ports with the glass up, then tying up at a new one; ledger still kept); ink to 1 / 1.25 / 1.5 × vision and ports
  inside marked; the chart kept while he is ashore; no officer slot (lookout + cartographer on a 1-slot sloop, and he
  survives brigantine → sloop); wages and walking off unpaid; save/load and an older save; hire/dismiss replays from a
  save; the autopilot hires one at its first port. Updated to give their worlds a cartographer:
  `SailingInksTheChartAndTheChartSurvivesASave`, `ACoveFoundThroughTheSpyglassCounts`,
  `ACoveSightedThroughTheSpyglassCounts`, `TheSpyglassIsReplayedWithTheHelm` (now seed 4, aimed at the nearest
  uncharted water: seed 5's home lies 1.4 km inside the bigger Trade Isles), and 4 tavern offers in
  `OfficersHireIntoSlotsAndEarnTheirKeep`.
- Full `dotnet test`: 252 of 267 pass. The 15 failures are the v6 map's, not the cartographer's (AThousandSeedsValidate,
  GoodsMeetTheGlobalConstraints, "no open water in Deep" ×3, "no such point in Sargasso", fleet budget/moving, the
  543 KB suspend save (the 3× reveal mask), a storm count, the crocodile, two helm/merchant AI tests, the rescue-harbour autopilot
  test) plus `SailingInksTheChartAndTheChartSurvivesASave`: with a cartographer it inks, but 200 s from home now stays
  mostly inside the pre-charted Trade Isles (8.95% → 9.11%, under its old-map 4,000-cell bar).
- `--selftest`: 221 ok, 3 FAIL (two ship-card hover checks, "a lookout reports her from beyond sight"); the same three
  fail with the cartographer block skipped, so they come from other work in the tree. Every cartographer check passes
  through the real input path (M without him: chart shut, notice shown once; the fog's live sight and the sea inside
  it in uncharted water; the home tavern's Hire clicked; M then opens the chart; with the cutter's slot taken a
  clicked Hire still signs him on; GC-25 he walks off unpaid).
  `--playtest=2,1 --seed=21`: PASS, 0 errors, a pin dropped on the chart.
- Screenshots reviewed: the tavern with the cartographer card (`--dock --page=2`), the notice and the live circle in
  the uncharted Deep (`--region=deep --zoom=0.3 --notice=NOTICE_NO_CARTOGRAPHER`), the crew panel's warning, and the
  live circle 1.5 km off the chart (`--seed=5 --at=10500,300 --zoom=0.3`, frames 60 and 120). At zoom 0.5 after
  dawn the circle (500 m) is wider than the whole view (459 m to a corner), so no fog shows there: expected.
- Not measured: FPS. The machine sat at load average 264 from the parallel test runs (29.7 fps without a
  cartographer, 27.6 with a legendary one, both vsync-bound runs starved of CPU).

## Hull visual pass (2026-09-27)

Nolan: "make visual improvements to each new ship hull, they should look really good." View-only (`game/`), no rule
changes; details in GDD §19 "Hull liveries".

- **Liveries** (`ShipArt.Liveries`): every hull has its own paint, topsides and a stripe (sloop umber/cream, cutter
  revenue blue/white, schooner green, xebec vermilion/gold, brigantine oak/green, fluyt sea-green/red, brig ochre/black
  wale, barque black/white, corvette slate/ochre, frigate black/ochre, indiaman brown/buff, heavy frigate black/white,
  galleon crimson/gilt, man-o'-war black with two ochre gun decks). At sea the Crown runs a red strake and the Brethren
  tar their topsides with a sea-green stripe; a cosmetic hull colour repaints the player's topsides.
- **Topsides band:** the dressed texture is padded by 6% of the art's height and carries the hull side seen outside the
  rail (tumblehome), with the stripe, plank strakes, darkening toward the waterline and an ink edge, so the paint reads
  at play zoom (the rail alone was ~2 px). The rail is recoloured as before; the volume (rail highlight, deck shade
  under the bulwarks) is baked in. All built once per hull/livery from the art and the `-rim` masks.
- **Shadows and water:** a soft blurred silhouette per hull casts her shadow to the south-east (the gulls' light);
  every mast casts one sail shadow, spars and furled canvas cast too (`InkBatch.AppendShadow`). A blue contact wash
  and a white lip that grows with speed sit at her sides. `ShipView.Sunlight` (set by `WeatherView`) fades all of it at
  night, in fog and rain.
- **Rig:** sails shaded under the spar, lit across the belly by the sun, pooled at the edge, ink swelling at the belly
  and fining at the yardarms; square canvas nests as one stack per mast (upper yards abaft the lower, square to the
  yards, each upper sail shading the one below; before, they fanned out like blades); spars rounded by a lit line;
  shrouds sized to the hull with ratlines up close, stays between masts, braces to the rails; she heels to leeward with
  the pressure in her canvas and sways with the swell. The spritsail braces with the yards; the indiaman and
  man-o'-war (which carry jibs) no longer set one.
- **Shipwright:** the hull catalogue shows each hull in its livery (with the player's hull colour) and its shadow,
  drawn larger (small hulls were ~34 px).

Evidence (2026-09-27, in a private copy of the tree so the map rewrite in progress couldn't stall the runs; screenshots
under Xvfb):
- `dotnet build`: 0 errors, 0 warnings. No `src/Sim` or test changes. `dotnet test` on the same snapshot: 250/264; the
  14 failures are in map/save tests (old map versions, save size) touched by the world v6 rewrite then in progress.
- Screenshots reviewed: all 14 hulls before/after on a beam reach, 8-point sweeps (frigate, brigantine), faction mix,
  night, the frigate under way, the shipwright catalogue, the title preview.
- Draw calls with 40 fleet ships + the 14-hull sheet on screen: 87 → ~123 (one shadow draw per visible ship). Frame
  time was flat within noise under llvmpipe (7.8/7.6 → 7.7/7.0 fps, dominated by the sea shader); not measured on the
  desktop GPU.
- `--selftest` (Xvfb, own user folder): **PASS, 226 ok**, twice in a row, on the tree with world v6's fixes. The hover
  checks were flaky: the broadside block teleports her from beyond the chart's edge without snapping the camera, so it
  was still easing across the sea when the pointer aimed at her. It now snaps (`Main.SelfTest.cs`, GC-30 style). Two
  self-tests sharing `user://selftest/` clobber each other's suspend save: run one at a time.
- Before/after sheets: `scratch/hull-pass/` (all 14 hulls, the shipwright, a frigate's eight points of sail).

## Next
- Balance on the v6 world: the chart is 3× each way and ports ½–1 day apart, so re-run the autopilot batches for all
  three presets before tuning anything (GDD §11 day targets, upkeep, provisions, Threat per day).
- Chart screen at whole-sheet zoom: ~150 port names crowd; show names by zoom or importance.
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
- **Q quits from the pause menu** (GDD §19 "Pause", an M1 rule from before the menu existed). Q is also the port
  broadside key, so a player who pauses mid-fight and reaches for Q closes the game. The suspend save keeps the voyage.
  Recommendation: drop the Q shortcut and keep the menu's Quit. Not changed; it is a logged decision.
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
