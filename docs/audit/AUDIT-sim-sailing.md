# AUDIT — sim-sailing (Last Tide)

Auditor: sim-sailing. Branch `sim-sailing` in `~/.cache/lt-audit/sim-sailing/`.
Scope: `src/Sim/` Ship, Pilot, Polar, Hulls, Wind, Weather, World.Weather, World, World.Save, MapGen, Map, Island,
NavGrid, Pathing, Reveal, Noise, Rng, Vec2, Angles, Tuning, Names, Regions + their tests. Regressions in
`tests/Sim.Tests/AuditSailingTests.cs`.

Status: **done** (2026-09-23). Baseline: `dotnet test` 118/118, self-test PASS (106 ok).

## Summary

- **Fixed on `sim-sailing`: 29** (+ S23/S30 already covered) — 6 major (S1 resumed fleets lose captains, S13 weather seams at region borders,
  S14 storm wind flips across a line, S17 hulls glued to coasts, S22 lantern widened daytime vision, S26 save grows
  with every helm tap), 20 minor, 3 polish. S21/S22/S23 came from sim-trade, S26 from game-core, S29/S30/S31
  from sim-combat, S32 from art-ships (lead's notes).
- **Ready on branch `sim-sailing-v4` (one commit on top): S27**, major — ~30% of maps have a harbour outside the
  Mangrove Maze that no big hull can reach; the fix re-rolls those maps, so the lead decides.
- **Reported outside my files: X1–X12** (AI steering into coasts, crew panel bypassing the logs, a wall-clock test,
  big hulls bought in the maze, …).
- **Touched outside my files (small, local):** `World.Port.cs` `Midnight()` guard (S10, 3 lines) and two crew
  actions (S29), `World.Progression.cs` `ApplyOfficers` (S22, 1 line), `game/Main.cs` `_PhysicsProcess` (S19,
  3 lines), `game/CrewPanel.cs` (S29, 2 lines);
  `EconomyTests.cs` only on `sim-sailing-v4`. Tests of mine changed: `WeatherTests` (S15), `DeterminismTests`
  (S26).
- **Saved state:** new optional `SaveData` fields `MapVersion`, `LastPaint`, `LastPos`, `VisionRadius`,
  `PlayerSpeedMult`, `PlayerLastHitBy`, `SirenPull`, `LastMonsterHit`, `MonsterHitTime`, `PricesLastVisit`,
  `Lanes`; `Log`/`Commands` no longer written (read past in old saves). Saves from the baseline build load on
  their own map with the baseline's own hash; `ASaveFromBeforeTheAuditStillLoads`,
  `AVoyageFromBeforeTheCoveRuleResumesOnItsOwnMap` guard it.

## Findings

### S1 — major — resumed voyages lose AI captains (ships drift dead in the water)
- **Where:** `src/Sim/World.Save.cs` `FromSave` (ship loop, was lines 345–348).
- **What was wrong:** each saved AI ship was spawned with a captain under its *temporary* spawn id
  (1, 2, 3 …), then re-keyed to its saved id with `Captains.Remove(nextShipId - 1)`. Saved ids have gaps as
  soon as any ship sinks, slips away or a hunter gives up, so a temporary id collides with an already-loaded
  ship's saved id: the spawn overwrote and the `Remove` deleted *that* ship's captain. After every resume a
  share of merchants, patrols, raiders and hunters had no captain at all — rudder centred, no trading, no
  chase — and the voyage diverged from the one that was saved.
- **Evidence:** probe (`scratch/probe firsttick 5 18000`): after one tick 18 of 40 AI ships differ between the
  running world and its reload (rudder 0, `TackHold` not ticking). Test
  `AuditSailingTests.AResumedFleetKeepsEveryCaptain` fails on the baseline with "ship 2 (Raider) has no
  captain after the load". The old `SaveAndLoadContinueIdentically` only ran 50 s, before any id gap exists.
- **Fix:** spawn with no captain, then register `CaptainFor(role)` under the saved id.
- **Status:** fixed. Saved state format unchanged.

### S2 — minor — resumed voyages re-lay merchant lanes with a different wind
- **Where:** `src/Sim/World.Save.cs` (the `routeCache` of `World.Fleet.cs` was never saved).
- **What was wrong:** lanes are A* paths cached per port pair with the wind of the moment they were first laid
  (`PlanVoyage`). A reload started with an empty cache, so the next voyage on a cached pair was re-laid with the
  current wind and took a different route from the running voyage (26/43, 31/46, 21/37 lanes change with the
  wind on seeds 5/6/7 — probe `pathwind`). The resumed world silently diverged from the saved one.
- **Evidence:** `AuditSailingTests.AResumedMerchantSailsTheLaneTheRunningWorldRemembers` (baseline: the loaded
  merchant gets a 3-point lane where the running world reuses its 5-point one).
- **Fix:** the cache is saved as `Lanes` (`[from, to, x0, y0, …]`, in cache order) and restored.
- **Status:** fixed. New optional save field; older saves load with an empty cache (old behaviour).

### S3 — minor — the siren's pull is lost on a save
- **Where:** `World.Save.cs`; `SirenPull` is computed in `MonsterTick` after the helm and bends the *next* tick's
  rudder (`World.Tick`), so it is carried state, but it was not saved.
- **Evidence:** `TheSirensPullSurvivesASave` (baseline: −0.527 before, 0 after the load; rudders then differ).
- **Fix:** saved/restored (`SirenPull`). **Status:** fixed.

### S4 — minor — the Sargasso's drag on the player is lost on a save
- **Where:** `World.Save.cs`; `Ship.SpeedMult` for the player is set in `MonsterTick`, after her `Step`, so the
  next step reads last tick's value. After a load it was 1 instead of 0.6 for one tick.
- **Evidence:** `TheSargassoDragSurvivesASave` (baseline: velocity 0.40 vs 0.41 after 1 s).
- **Fix:** saved/restored (`PlayerSpeedMult`). **Status:** fixed.

### S5 — minor — a resumed voyage's input log can leave a pulse standing in a replay
- **Where:** `World.Save.cs`: only `lastInput.Rudder` and `.SailDelta` were saved. The log records changes
  only; if the last logged input carried a pulse (lantern, fire, order, action), the reloaded world's first
  plain input compared equal to the truncated `lastInput` and was not logged, so a replay of the resumed voyage
  keeps that pulse on every tick (here: the lantern flips every tick).
- **Evidence:** `AReplayOfAResumedVoyageMatchesIt` (baseline: lantern true vs false).
- **Fix:** first saved the whole `ShipInput` as `LastInput`; superseded by S26 (the save carries no log; the
  resumed log opens with its first input).
- **Status:** fixed (by S26).

### S6 — minor — the chart inks differently after a save
- **Where:** `World.Save.cs`: `lastPaint` (where the vision circle was last inked; the next ink happens 6 m
  from there) was reset to the ship's position on load, so every later ink point shifted and the union of
  circles differed; port discovery and region entry run in the same block, so their timing shifted too.
- **Evidence:** `TheChartInksTheSameAfterASave` (baseline: 25,263 vs 25,192 cells inked 113 ticks after load).
- **Fix:** saved/restored (`LastPaint`). **Status:** fixed.

### S7 — polish — the market's trend arrows vanish after resuming in port
- **Where:** `World.Save.cs`: `Port.PricesLastVisit` (set at every dock, read by `PortScreen` for the ▲▼ arrows)
  was not saved. The suspend save is written on every dock, so every resume in port lost the arrows.
- **Evidence:** `TheMarketTrendArrowsSurviveASaveInPort` (baseline: null after the load).
- **Fix:** saved/restored per port (`PricesLastVisit`). **Status:** fixed.

### S8 — minor — the logbook's cause of sinking is wrong after a save
- **Where:** `World.Save.cs`: `LastMonsterHit`/`MonsterHitTime` (a beast or eruption within the last 60 s) and
  the player's `LastHitBy` (guns vs "the sea") were not saved, so a ship that foundered after a resume from
  damage taken before it was logged as "SUNK_SEA".
- **Evidence:** `TheCauseOfSinkingSurvivesASave` (baseline: `MonsterHitTime` −1 after the load; an eruption
  death and a gunnery death both come back as the sea).
- **Fix:** saved/restored (`LastMonsterHit`, `MonsterHitTime`, `PlayerLastHitBy`; an attacker that has since sunk
  is restored as a detached, sunk stand-in so the "guns" cause survives). **Status:** fixed.

### S9 — polish — vision radius shows 500 m after resuming until the first tick
- **Where:** `World.Save.cs`: `VisionRadius` was left at its 500 m default after a load (the sim recomputes it at
  the next tick, but a voyage resumed in port shows the day radius at night until cast off).
- **Fix:** saved/restored (`VisionRadius`, computed when absent). **Status:** fixed (no sim effect; view only).

### S10 — minor — upkeep falls due at dawn, not at midnight
- **Where:** `src/Sim/World.cs` `Tick` (called `Midnight()` when `Day` changed) + the guard in
  `src/Sim/World.Port.cs` `Midnight()` (outside my files; changed only as far as the fix needs).
- **What was wrong:** `Day` turns at 06:00 (the run starts at dawn), so the "midnight" upkeep — wages,
  provisions, starvation, reputation decay — was charged at dawn, 30 s after the in-game midnight that GDD §6
  (decided), §19 M3 and the code comment all name.
- **Evidence:** probe `upkeep`: "upkeep charged at t=120.00 s: day 2, hour 6.00". `UpkeepFallsDueAtMidnight`
  fails on the baseline (charged at hour 6).
- **Fix:** `World.MidnightsPassed` (midnight k falls at 120k − 30 s); `Tick` calls `Midnight()` when it changes;
  the guard keeps `lastUpkeepDay` = 1 + upkeeps charged, exactly the old meaning, so a save from before the fix
  neither double-charges nor skips (a save written between midnight and dawn is charged at the first tick).
- **Status:** fixed. Saved state format unchanged.

### S11 — minor — a cove sighted down the spyglass is never counted
- **Where:** `src/Sim/World.cs` `Tick`, spyglass block.
- **What was wrong:** the glass marks ports `Discovered` directly; the regular sighting code (which counts coves
  for `Stats.CovesFound`, Smuggler's Welcome and the logbook, and posts `NOTICE_COVE`) then skips them. Worse,
  with RMB held the glass block runs every 10 ticks and usually beats the regular block (which waits for 6 m of
  way), so even coves entering plain sight went uncounted while the glass was up.
- **Evidence:** `ACoveFoundThroughTheSpyglassCounts` (baseline: 2 coves discovered, `CovesFound` 0, no notice).
- **Fix:** one `Discover(port)` used by both paths. **Status:** fixed.

### S12 — minor — Calm and Tempest voyages do not replay
- **Where:** `src/Sim/World.cs` `Replay` always rebuilt a Rough Seas world with an empty loadout.
- **Evidence:** probe `replaypreset` / `ACalmOrTempestVoyageReplaysToTheSameState` (baseline: director credits
  8.01 vs 7.76, hashes differ; with the old 5-argument call).
- **Fix:** optional `preset` and `loadout` parameters (defaults keep the old call working).
- **Status:** fixed.

### S13 — major — weather has hard seams at every region border
- **Where:** `src/Sim/World.Weather.cs` `ConditionsAt`/`WindAt` took the wind factor, fog, doldrums and ash of the
  single region at the point; `src/Sim/Map.cs` gave no way to blend.
- **What was wrong:** crossing a border line switched the region's weather in one step: the wind speed jumped by
  up to 1,567% in 0.4 m (a Sargasso doldrums patch against the Trade Isles; 1.3× Deep against 0.7× Volcanic is
  common), fog went 0 → 1 (vision 500 → 180 m, the fog wash snapping) and doldrums 0.96 → 0 — a wall of calm.
  The HUD wind, the sea's stroke density and the ship's target speed all snapped with it. GDD §4 asks for a
  wind that "varies gently across the map".
- **Evidence:** probe `wxjumps` (51 borders crossed on seed 5: worst wind step 337%, fog 1.00, doldrums 0.96);
  `WeatherHasNoSeamsAtRegionBorders` (baseline: "wind speed jumps 1,567% … in 0.4 m").
- **Fix:** `Map.RegionWeights` — the same warped nearest-seed geometry as `RegionAt`, with each neighbour's weight
  falling smoothly (cubic, compact) from ½ on the border to 0 about 100 m inside. `ConditionsAt`/`WindAt` mix
  the regions' fog, doldrums, ash and wind factor by those weights (the noise fields are sampled once).
  `RegionAt` — which region you are in: monsters, the mangrove rule, region toasts — is unchanged, and deep inside
  a region the weather is exactly as before (the region-ratio test still sees 1.300000).
- **Note:** a 150 m band first tried flipped `AutopilotTests.TradesFromTheLeanStart` (seed 1000's first voyage
  meets a pirate a few hours earlier and trades 0 times in 4 days); the band chosen, 100 m (~8 s at a sloop's
  best speed), keeps it green. That test pins chaotic trajectories of three seeds; any weather change can flip it.
- **Status:** fixed. No saved state.

### S14 — major — a storm's wind flips up to 180° across a line inside every cell
- **Where:** `src/Sim/Weather.cs` `StormWind`.
- **What was wrong:** the swirl was mixed in by `LerpAngle`, which takes the short way round, so on the radial line
  where the swirl opposes the base wind the result flipped from one side to the other (up to 179° in 0.4 m); and
  at the eye the swirl direction itself (`atan2` of a vanishing vector) spun without limit, weighted at its
  heaviest (176° in 0.4 m). A ship crossing either got her wind thrown about in one tick.
- **Evidence:** probes `stormring` (worst 146–179° per 0.4 m on rings at 10–70% of the radius) and `wxjumps`;
  `StormWindTurnsWithoutJumps` (baseline: 176°).
- **Fix:** the swirl is mixed as a direction vector, and eased to nothing inside the eye (12% of the radius).
  Overlapping cells now add their pushes (`WindAt`) instead of the strongest one winning, which switched the wind
  abruptly where two cells are felt equally. Speeds are unchanged (the eye still blows hardest).
- **Status:** fixed. The strongest swirl is unchanged (0.6); only its blending and the eye differ.

### S15 — minor — storms appear and vanish at full strength
- **Where:** `src/Sim/Weather.cs` (`StormCell`, `StormAt`, `Tick`), `World.Weather.cs` `WindAt`.
- **What was wrong:** a cell blew at full strength from its first tick and stopped dead when its life ran out or
  its centre left the chart, so a ship inside saw the wind drop 27.8 → 7.7 m/s, swing 88° and the rain go
  0.68 → 0 in one tick; the chart swirl (`WeatherView`) meanwhile fades in over 10 s and out over 15 s, so the
  picture and the weather disagreed.
- **Evidence:** probe `wxjumps`; `AStormGathersAndBlowsOutGradually` (baseline: 51% wind jump in one tick).
- **Fix:** `StormCell.Fade` (Age/10 s, Life/15 s — the view's own spans) scales the felt strength everywhere
  (wind, rain, swell, torn sails, vision); a cell whose centre leaves the chart blows out over 15 s.
- **Test changed:** `WeatherTests.StormsFormUpwindDriftBlowHardAndTearSails` sampled the eye in the tick the cell
  formed; it now first checks the new cell has not gathered, lets it gather 10 s, then asserts as before.
- **Status:** fixed.

### S16 — minor — the mangroves' dawn fog switches on and off whole
- **Where:** `src/Sim/Weather.cs` `Fog`.
- **What was wrong:** the dawn-fog threshold applied from 05:00 to 08:00 exactly, so fog jumped 0 → 1.00 in one
  tick at 05:00 and vanished at 08:00.
- **Evidence:** probe `wxjumps`; `DawnFogComesAndGoesGradually` (baseline: 0.92 in one tick at hour 5.01).
- **Fix:** it rolls in over 05–06 and burns off over 07–08 (`FogFrom`). **Status:** fixed.

### S17 — major — grazing a coast glues a hull to it (AI ships stuck for minutes)
- **Where:** `src/Sim/Ship.cs` `Collide`; `src/Sim/Tuning.cs` (comment only).
- **What was wrong:** every tick a capsule circle touched land and was moving into it (true every tick while the
  sails drive her on, since thrust re-presses her), 40% of her speed along the coast was taken and her turn rate
  halved. A hull meeting a coast at a slant therefore stuck at a few cm/s (0.05–0.12 m/s) and turned at ~8% of
  her rate; nothing short of backing off freed her. The AI never backs off: over 20-minute voyages (autopilot
  player, 4 seeds) AI ships near the player spent **6–18% of their ticks aground**, merchants and patrols for
  up to **472 s** at a stretch — the player sees traders ground into islands and stay there.
- **Evidence:** probe `grounding` (baseline 9.1 / 5.7 / 18.1 / 18.2%; after the fix 2.6 / 9.2 / 6.4 / 1.5% — the
  rest is AI steering, see X4) and probe `stuck` (glued hulls at 0.01–0.07 m/s, rudder 0, full sail);
  `AGlancingBlowScrapesAlongTheCoastInsteadOfStickingToIt` (baseline: 0.12 m/s).
- **Fix:** Coulomb impulse friction — the scrape takes at most μ (the existing `CollisionFriction` 0.6) × the
  normal impulse off her sliding speed, and the swing is checked only in proportion to a real blow (≥ 2 m/s).
  A hard blow still stops her; within ~35° of head-on she is still pinned (`RunningStraightOntoACoastStillPinsHer`,
  the old grounding test and the self-test's grounding check all pass); a glancing one scrapes on (her keel still
  resists crabbing: ~1.7 m/s at 20° into a coast). `AShipPressedAgainstACoastCanTurnAwayAndSailOff` checks a
  lee-shore escape under the helm.
- **Status:** fixed (physics feel changed only for contacts with land; values unchanged).

### S18 — minor — straightened lanes cut across land
- **Where:** `src/Sim/Pathing.cs` `Smooth`/`Clear`, `src/Sim/NavGrid.cs`.
- **What was wrong:** string-pulling accepted a leg when points every 8–16 m fell in sea cells, but a 25 m cell
  is "sea" when its centre is, so a leg could clip a coast between samples: 74 legs over land on 37 of 60 maps,
  up to 5.7 m in (probe `mapcheck`). Merchants follow those legs straight into the land.
- **Evidence:** `LanesNeverCutAcrossLand` (baseline: 16 of 1,467 legs on six maps, up to 3.7 m in).
- **Fix:** `NavGrid` keeps its grown coastlines (coast + 10 m margin) and `SegmentClear` tests a leg against them
  exactly; `Smooth` requires it for every straightened leg. `Pathing.Clear` keeps its old meaning (the AI test uses
  it). A single step between two neighbouring sea cells — the A* grid itself — can still graze a spit by ~1 m
  (2 such steps in 4,936 legs). Lane finding: median 0.56 → 0.71 ms, p90 2.5 → 3.2 ms (probe `pathtime`); lanes are
  cached per port pair (and now saved, S2).
- **Status:** fixed.

### S19 — minor — the spyglass is not part of the logged input, so replays sail blind
- **Where:** `src/Sim/Ship.cs` (`ShipInput`), `src/Sim/World.cs` (`Tick`), `src/Sim/World.Weather.cs`; one local
  edit in `game/Main.cs` `_PhysicsProcess` (shared file, 3 lines).
- **What was wrong:** the view set `World.SetSpyglass` beside the input each frame. The glass inks the chart and
  marks ports discovered (which changes the candidates, and so the dice, of a tavern rumour, and counts coves),
  so a replay of the input and command logs did not rebuild the voyage.
- **Evidence:** baseline probe `glassreplay`: 23,077 cells inked vs 23,010 in the replay;
  `TheSpyglassIsReplayedWithTheHelm`.
- **Fix:** `ShipInput` gains `Spyglass` and `SpyglassDir` (optional, trailing: all 177 call sites unchanged);
  `Tick` raises/lowers the glass from its input. Main builds them into the input; the bearing is rounded to
  0.01 rad (0.6°, 9 m at the glass's reach) so a steady glass does not log a new entry every tick.
- **Status:** fixed. The input log in saves gains two fields per entry (older entries default to "lowered").
  Self-test spyglass checks still pass.

### S20 — minor — the determinism hash missed most of the state
- **Where:** `src/Sim/World.cs` `Hash()`.
- **What was wrong:** the hash covered the player, counts, AI positions/HP/crew and the sum of all market stock,
  but not AI helms, velocities or plans, the captains, the chart, discovered ports, individual stocks, reloads,
  the director's purse, storms in full, balls, flotsam, stats, ledger, maps or officers — so a load that lost half
  the fleet's captains (S1) compared equal, and only diverged later.
- **Fix:** all of that is folded in (plus the hull id as a string rather than its length). Every field it adds
  is restored exactly by `FromSave` and reproduced by `Replay`; checked per tick over 15-minute autopilot
  continuations after saves at sea and in port (probe `autocont`, 3 seeds × 2) and full-voyage replays
  (probe `autoreplay`: seeds 5/6/7 Rough, 8 Tempest; 10–14k inputs, 16–33 port commands each: equal).
  It immediately caught one more gap: `lastPos` (leagues sailed) was reset on load — now saved (`LastPos`).
- **Status:** fixed. Hashes are never stored, so no save or profile is affected.

### S21 — minor — (from sim-trade S-13) the lean-start route could be a hidden cove
- **Where:** `src/Sim/MapGen.cs` `Validate`.
- **What was wrong:** the half-day start-route guarantee accepted any port, including an uncharted secret cove;
  on 21/1000 seeds (7, 25, 63, 69, 125, …) the only market within 600 m was a hidden cove (seed 7: cove 570 m,
  nearest charted market 847 m).
- **Evidence:** `TheLeanStartNeverCountsAHiddenCove` (baseline: "seed 7: no charted market within 600 m").
- **Fix:** coves no longer count. Because this re-rolls those maps, the generator is now versioned:
  `MapGen.Version` = 3, `Map.Version`, `World(…, mapVersion)`, `Replay(…, mapVersion)`, and the suspend save
  records `MapVersion` (absent = 2, the old generator). A voyage saved before this change resumes on its own map:
  `AVoyageFromBeforeTheCoveRuleResumesOnItsOwnMap` (seed 7), and the three baseline-build saves (one is seed 7,
  docked) load with the baseline build's own hash (probe `oldsaves`, hash formula replicated).
- **Status:** fixed. New save field `MapVersion`.

### S22 — major — (from sim-trade R-03) the lantern part widened daytime vision
- **Where:** `src/Sim/World.Weather.cs` `VisionAt`; one local edit in `src/Sim/World.Progression.cs`
  `ApplyOfficers` (outside my files, as the lead asked).
- **What was wrong:** `VisionMult` was lookout × `LanternMult` and applied in every condition, so a grade-5 lantern
  gave 750 m at noon; the part is "+10% night radius and spyglass reach a grade" (GDD §19 M8).
- **Evidence:** `ALanternLightsTheNightNotTheNoon` (baseline: 750 at mid-morning).
- **Fix:** `VisionMult` is the lookout alone; `VisionAt` lights the night (and its dusk/dawn blend) with
  250 m × `LanternMult` when lit; doused stays 150 m; the spyglass already scales separately.
- **Status:** fixed.

### S23 — (from sim-trade R-02) no toast for a cove found down the glass
- Already fixed by S11: the spyglass and the plain sighting share `Discover(port)`, which posts `NOTICE_COVE` and
  counts the cove once. sim-trade's `max()` re-tally in `AchievementsTick` agrees with that count, so the two
  merged cannot double count.

### S24 — minor — how far hunters spot her changes at a stroke at 22:00 and 06:00
- **Where:** `src/Sim/World.Weather.cs` `DetectRangeOfPlayer`.
- **What was wrong:** her own sight blends through the dusk and dawn hours, but the range at which other ships spot
  her switched 500 → 650 m (lantern lit) or 500 → 200 m (doused) in the tick the clock struck 22:00, and back at
  06:00, so a hunter 300 m off lost or found her in one tick.
- **Evidence:** `HowFarSheCanBeSeenChangesWithTheLightNotAtAStroke` (baseline: 150 m in one tick at 22.00).
- **Fix:** the same dusk/dawn blend as `VisionAt`; the night values (650 / 200) are unchanged.
- **Status:** fixed.

### S25 — polish — a negative pinned wind turns the ship to NaN
- **Where:** `src/Sim/Wind.cs` `SetFixed` (`--wind=FROM,SPEED`, tests).
- **Evidence:** `APinnedWindCannotGoNegative` (baseline: position NaN after two seconds).
- **Fix:** the pinned speed is clamped at 0. **Status:** fixed. (A 20,000-run physics fuzz — random hulls, parts,
  states up to 60 m/s, winds 0–30 m/s, ships starting on a vertex or at an island's centre — produced no
  non-finite value, no runaway and no hull left inside land: probe `fuzz`.)

### S26 — major — (from game-core GC-36) the suspend save grows with every helm tap
- **Where:** `src/Sim/World.Save.cs` (`SaveData.Log`/`Commands`), `src/Sim/World.cs` (`Tick`, `Replay`).
- **What was wrong:** the suspend save carried the whole input and command logs, rewritten on every dock and
  synced to Steam Cloud: 106 KB after 1 minute → 1,438 KB after 11 minutes of helm work (10,025 inputs, baseline
  probe `savesize`); game-core measured 2.1 MB by day 12 of a voyage, ~5 MB for a 30-day one. Resuming needs only
  the state.
- **Evidence:** `TheSuspendSaveDoesNotGrowWithTheHelm` (fails on the baseline: it grows by 1.3 MB).
- **Fix:** the logs are no longer written (older saves' logs are read past, not carried on). A loaded world logs
  from its save — its first input is always logged (`logNext`) — and `World.Replay(suspendJson, log, commands,
  ticks)` replays a resumed voyage from its save; the old seed-based `Replay` is unchanged for whole voyages.
  Same scenario now: 84 → 85 KB, written in 1.3 ms. This also retires S5's partial `LastInput` field (the resumed
  log no longer depends on it); `DeterminismTests.SaveAndLoadContinueIdentically` now checks that the save has no
  log and that save + resumed log replay to the resumed voyage's hash, and `AReplayOfAResumedVoyageMatchesIt`
  replays from the save.
- **Status:** fixed. Save format: `Log`/`Commands` omitted; `LastInput` (added earlier on this branch) removed.

### S28 — minor — a corrupt suspend save loads, then crashes the resumed voyage
- **Where:** `src/Sim/World.Save.cs` (`FromSave`; new `Check`/`CheckAgainst`).
- **What was wrong:** a truncated file already failed cleanly (`JsonException`), but a save that parses and points
  past the tables loaded: on the baseline a merchant from port 999, a bottle map of treasure 999, preset 7 and a
  cove rumour of port 999 each loaded and crashed on the first tick (a merchant with no home port at tick 225),
  and cargo good 99, a barrel of good 99, beast 42 and a ledger price from port 999 loaded as latent crashes (baseline
  probe `corrupt`). `Main.ResumeVoyage` deletes the save as soon as `LoadJson` returns, so the voyage was lost with
  a crash.
- **Evidence:** `ACorruptSuspendSaveIsRefusedAtTheLoad` (15 corruptions; baseline: "a merchant from port 999: loaded").
- **Fix:** every index and enum the save carries is checked (against the tables, then against the regenerated map)
  before the world is built; a bad one throws `InvalidDataException("corrupt save: …")`, which `ResumeVoyage`
  catches while the file and the title are intact. A sound save loads unchanged.
- **Status:** fixed.

### S29 — minor — (from sim-combat R-13) the crew panel is not replayed
- **Where:** `game/CrewPanel.cs` wrote `world.Ship.Order` and called `world.SetStations` directly; the fix adds two
  `PortAction`s in `src/Sim/World.Port.cs` (enum + two cases before the docked check) and a packing helper in
  `src/Sim/World.cs`; `CrewPanel.cs` now applies them (2 lines). All three are small local edits outside my files
  except `World.cs`.
- **What was wrong:** neither change was in the input log or the command log, so `World.Replay` could not rebuild a
  voyage that used the panel (stations drive reloads, sail handling and pumping). The spyglass half of R-13 is S19.
- **Evidence:** baseline probe `crewreplay`: order Battle vs Balanced in the replay, hashes differ;
  `TheCrewPanelIsReplayed`.
- **Fix:** `PortAction.CrewOrder` (0–3) and `PortAction.CrewStations` (guns, sails, repair packed 10 bits each,
  `World.CrewStationsCommand`) work at sea and in port and are logged like any port action; the enum values are
  appended, so old command logs replay unchanged. `SetStations` stays for callers that want the raw change.
- **Status:** fixed.

### S30 — (from sim-combat R-05) logbook state not saved
- Already fixed by S8 (`LastMonsterHit`, `MonsterHitTime`, the player's `LastHitBy`, with a stand-in for an attacker
  that has since sunk; `TheCauseOfSinkingSurvivesASave`, fails on the baseline). Old saves load with the old
  defaults.

### S31 — minor — (from sim-combat R-12) "sea" cells of the nav grid hold land
- **Where:** `src/Sim/NavGrid.cs` (constructor, new `MarkCellsCrossed`), `src/Sim/MapGen.cs` (version).
- **What was wrong:** a 25 m cell was land only when its centre lay within 10 m of a coast, so the corners of
  "sea" cells held real land — ~630 such cells a map, up to 11 m into the land (probe `gridmiss`, 100 maps) —
  and lanes, `AvoidLand` look-aheads and `SeaPointNear` read open water over spits the hulls collide with
  (sim-combat worked round it in the AI, C-25).
- **Evidence:** `NoSeaCellOfTheGridHoldsLand` (baseline: seed 3, 4,441 sample points of land read as sea, 8.4 m in).
- **Fix:** every cell the coastline itself passes through is land too (exact grid traversal of each coast edge;
  a corner-to-corner crossing closes both neighbours). This closes 2.2% of sea cells (50 maps; rasterising the
  *grown* coast, as first tried, closed 5.5% and left sim-combat's 450 m open-water test helper no water to find)
  and leaves 0 land in sea cells over 100 maps. It is generator **v4** (`MapGen.Version`): the grid's
  reachability check re-rolls 10/300 maps (27, 41, 48, …; probe `mapdiff`); old saves keep their v2/v3 grid.
  Seed-pinned tests hardened, not weakened: `EconomyTests.TheTavernSellsRumorsHandsAndCannons` (relied on seed 4's
  first rumour roll being a price; the grid shifts the dice drawn while placing the fleet, so it marks the coves
  known first) and my own save-size test (keeps her pumped dry, not only her hull whole).
- **Status:** fixed.

### S32 — minor — (from art-ships R1) a tick that does nothing leaves the last tick's events standing
- **Where:** `src/Sim/World.cs` `Tick`: it returned (run over, or docked) before `Events.Clear()`.
- **What was wrong:** after the sinking (or a harbour rescue that docks her) every later frame read the same events
  again and re-fired their effects; game-core (Main) and art-ships (FleetView) had each guarded against it.
- **Evidence:** `ATickThatDoesNothingLeavesNoEvents` (baseline: the sinking event is still there a tick later).
- **Fix:** the early-return path clears the events; the Main/FleetView guards stay harmless.
- **Status:** fixed.

### S27 — major — ports big hulls can never dock at — **ready on branch `sim-sailing-v4`, lead's call**
- **Where:** `src/Sim/MapGen.cs` (`TryPlacePort`, `Validate`), `src/Sim/NavGrid.cs`.
- **What is wrong:** an island's region is the region of its centre, but its harbour can lie across a border.
  297/1000 maps have a port outside the Mangrove Maze whose harbour lies inside it (12/1000: the start port),
  and the maze admits only the five small hulls, so a fluyt or bigger can never dock there although the chart
  labels the port with another region; 129/1000 maps have 288 such ports cut off even if their harbour rings were
  exempt (probes `v3check`, `bigreach2`, `mapcheck`).
- **Evidence:** `EveryHarbourOutsideTheMazeTakesABigHull` (on `sim-sailing-v4`; fails on seed 4 without it).
- **Fix (one commit on top of this branch):** generator v4 — a harbour must lie in its island's own region (a
  vertex that puts it across a border is skipped; no dice are drawn in that loop), and validation requires a
  big-hull path from the start to every harbour outside the maze. Over 1,000 maps: 0 mismatched harbours, 0 cut
  off; 1.94 attempts per map (v3: 2.00); generation 4.9 s vs 2.6 s per 200 maps (the region lookups in the
  validation BFS are memoised). Old saves keep their generator (`MapVersion`).
- **Why a side branch:** it re-rolls ~30% of maps (seeds 4, 5, 6, 10, 13, 16, 19, 21–23, 25, 26, 31, 32, 35 among
  the test seeds), so every seed-pinned expectation in other branches can move. The full suite (145) passes on it
  after one test was hardened: `EconomyTests.TheTavernSellsRumorsHandsAndCannons` relied on seed 4's first
  rumour roll being a price (it now marks the coves known first).
- **Status:** proposed, implemented and green on `sim-sailing-v4`.

Save compatibility for S2–S9: all new `SaveData` fields are optional with defaults that reproduce the old load
behaviour. `ASaveFromBeforeTheAuditStillLoads` strips them and loads; three suspend saves written by the
**baseline build** (autopilot voyages, at sea and docked, 1.5–2.6 MB) load in the fixed build with the baseline's
own hash and tick on (probe `oldsaves`).

## Checked and clean

- **Determinism sources:** no `string.GetHashCode`, `DateTime`, unseeded `Random` or `Stopwatch` feeds sim state
  (`Stopwatch` only times the optional profiler; the one `new Random()` seeds a new voyage in `Main`). Hash sets
  and dictionaries of the sim are only probed or counted, never iterated into state (`Captains`, `fortClocks`,
  `routeCache`, the string sets); LINQ `OrderBy` is stable; all sim arithmetic is `double`.
- **Save graph (GC-19):** the only `Vec2`-typed member reachable from `SaveData` is `Pin.Pos` (the object-cycle
  crash game-core fixed); nothing added on this branch carries a `Vec2` (saved points are `double[]`).
- **Physics:** a 20,000-run fuzz found no NaN/∞, runaway or hull left inside land (S25); no tunnelling is possible
  at the speeds reachable (≤ ~22 m/s = 0.73 m a tick against islands ≥ 10 m across); a ship pressed on a lee shore
  claws off under the helm (`AShipPressedAgainstACoastCanTurnAwayAndSailOff`); in irons, rudder/sail clamps, torn
  sails and hull changes behave as the tests and GDD say.
- **Map generation (1,000 seeds):** every port reachable, the start route holds (now ignoring coves, S21), no two
  islands overlap, no port sits in another island, harbour rings wet (11 of ~11,300 rings more than 6/16 dry),
  every treasure X on its island with a reachable dig ring, every wreck at sea in the Sargasso and reachable, no
  island past the chart edge. The one gap is S27.
- **Clock:** the day turns at dawn and the watches follow the hours; the clock is frozen in port; `--time` ticks
  the world normally, so every midnight's upkeep is charged (S10).
- **Reveal mask:** painting clips at the chart edges; the home region is inked whole; the mask round-trips a save
  exactly (and now inks the same afterwards, S6).
- **Performance:** populated tick 0.3–0.5 ms, 0.8–1.1 ms with ~46 ships near (both builds, within noise); ~14 KB
  allocated per tick; lanes are the only spike (X11).

## Outside my files: reported, not fixed

- **X1 — minor — `AiTests.ThePopulatedSeaTradesAndKeepsItsBudget` is a wall-clock assertion** (240 s of sim in
  < 12 s). It passes alone in 3 s, but failed twice in the full suite on this box under load average ~20
  (other agents, RL training): 26–28 s with test classes running in parallel. Same on the baseline code
  (`scratch/probe fleet40`/`profile`: tick cost unchanged within noise, 0.8–1.1 ms with ~46 near ships on both).
  Proposal: time ticks, not wall clock, or run it in a non-parallel collection.
- **X2 — minor — `MerchantCaptain` flees from `ai.Waypoint` after the threat is gone** (`Captains.cs`): merchants
  never set `Waypoint`, so for 12 s after a threat drops out of range they "flee" from (0, 0), the chart's centre.
- **X3 — the crew panel bypasses the logs** — fixed after all at the lead's request: S29.
- **X4 — major — AI ships steer into coasts and never turn away** (`Ai.cs` `Seamanship.SteerTo`/`AvoidLand`):
  when the waypoint lies behind land, `AvoidLand` only tries ±66° and, with land ahead on every bearing, keeps the
  bow on the coast (rudder 0, full sail). S17 stopped grazing hulls from gluing, but head-on ones stay pinned:
  after S17 AI ships near the player were still aground 1.5–9% of ticks, some for 3–5 minutes (probe `stuck`:
  "coast at rel −1°, target bearing 145 = heading, speed 0.01"). Proposal: when `Aground` for > 2 s, steer to
  the coast normal (away) for a few seconds, or let `AvoidLand` consider bearings to ±180°, or re-path.
- **X5 — minor — ram separation ignores land and identical positions** (`World.Combat.cs` `Ram`): the push-apart is
  applied after `KeepOnTheChart`, so a ram can shove the player across the mangrove border (the rule only blocks
  *entering*, so a big hull shoved in then roams the maze) or a few metres into land (the next tick's collision
  pushes her out). Two hulls at exactly the same spot (a director pack whose `SeaPointNear` falls back to its
  centre) have a zero normal and are never separated.
- **X6 — minor — big hulls bought at a Mangrove Maze port roam the maze** (`World.Progression.cs` `BuyHull`): no
  region check, and `KeepOnTheChart` only stops a big hull *entering* the region. Proposal: the shipwrights of
  mangrove ports sell only the five mangrove hulls.
- **X7 — minor — an unreadable suspend save fails silently** (`Main.cs` `ResumeVoyage`): the exception is logged
  with `GD.PushError` and the title keeps offering "Resume voyage", which does nothing, forever. `LoadJson` itself
  fails cleanly (a truncated/corrupt file throws before any world is returned; nothing is half-loaded).
- **X8 — polish — far (coarse-tick) ships ignore the Sargasso's drag** (`World.Fleet.cs` `CoarseStep` uses the
  polar speed without `SpeedMult`), so distant traffic crosses the weed sea 1.7× faster than near traffic.
- **X9 — polish — Threat-scaled hunters count leaks against the unscaled hull** (`World.Fleet.cs` `SpawnHunter`
  sets `HullHp = Hull.HullHp × scale` while `MaxHp` stays unscaled): above 100% nominal they open a leak every
  10% of the *nominal* hull, and carpenters patch only to 70% of nominal. A Threat-4 hunter takes one extra leak
  over its life; proposal: include the spawn scale in `MaxHp` (`Ai.Scale`, which is saved).
- **X10 — polish — `--fleet=N` debug ships would not survive a save**: `SpawnHunter` gives them merchant/patrol/
  raider roles with no home port and a `HunterCaptain`; a load re-captains them by role (`MerchantCaptain` with
  `HomePort` −1 → index out of range). Debug runs never write the suspend save, so only a debug hazard.
- **X11 — perf note — first-time lanes spike a tick**: `PlanVoyage` runs A* + smoothing inside the tick the first
  time a port pair is used (median 0.7 ms, p90 3.2 ms, worst seen 27–55 ms incl. JIT; probe `pathtime`). Lanes
  are cached and now saved (S2). Tick cost otherwise: 0.3–0.5 ms populated, 0.8–1.1 ms with ~46 ships near;
  ~14 KB allocated per tick (0.4 MB/s, 7 gen-0 GCs per 100 s): no hot allocation path worth changing.
- **X12 — perf note — the suspend save grows with the voyage**: the input log is written in full on every dock;
  autopilot voyages reach 1.5–3.2 MB after 9–20 minutes (a human's log is smaller: it logs changes only), taking
  20–60 ms to write. Proposal: store the log compactly (arrays, not objects) or keep only what a replay needs.

## Tests run

- **Merged tree at the tip** (this branch + the lead's `merged` @ 5f012ef, merge commit 4631308; tip f117130+):
  `dotnet test tests/Sim.Tests` **202/202** (serial: `-- xUnit.ParallelizeTestCollections=false`, 5 m 25 s under
  load); `dotnet build LastTide.csproj` 0 errors; self-test **PASS, 178 ok** (via `vrun.sh`, absolute user-data dir,
  after `--headless --import` of the merged art).
  Merge notes: `game/CrewPanel.cs` conflict resolved keeping the new UI, with its order buttons and order hotkeys
  applying the logged `PortAction.CrewOrder` (S29); `AuditTradeTests.ACoveSightedThroughTheSpyglassCounts` raises the
  glass through the tick's input (S19's contract; it called `SetSpyglass` beside the input); my trend-arrow test
  re-docks on a later day (sim-trade's visit rule makes a same-day return the same visit).
- **This branch alone before the merge** (670d906): 146/146, self-test PASS 106 ok (baseline count).
- **Baseline:** 118/118, self-test PASS 106 ok.
- **Side branch `sim-sailing-v4`** (S27, pre-merge base f8d5893): 145/145 there. Its rule predates S31 and also calls
  itself generator version 4; adopting it now means renumbering it 5 and cherry-picking dc01a6e (conflicts only in
  `MapGen.cs`'s version lines, the end of `AuditSailingTests.cs`, and the rumour test already hardened here).
- **Probes** (`scratch/probe`, `scratch/probebase` against the baseline worktree `scratch/basewt`): `autocont`
  (per-tick hash over 15 min after saves at sea and in port), `autoreplay` (whole autopilot voyages replay exactly),
  `oldsaves` (baseline-build saves load with the baseline's hash), `fuzz` (20,000 physics runs), `grounding`/`stuck`,
  `wxjumps`/`stormring`/`stormlife`, `mapcheck`/`bigreach2`/`v3check`/`gridmiss`/`mapdiff` (map validation),
  `pathtime`/`profile`/`fleet40`/`alloc` (performance), `savesize`, `corrupt`, `crewreplay`, `glassreplay`.
