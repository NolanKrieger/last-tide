# AUDIT — sim-combat (Last Tide)

Auditor: sim-combat. Scope: `src/Sim/Combat.cs, World.Combat.cs, Ai.cs, Captain.cs, Captains.cs, Director.cs,
World.Fleet.cs, Monsters.cs, World.Monsters.cs, Autopilot.cs`; tests `CombatTests, AiTests, MonsterTests,
AutopilotTests`; regressions in `tests/Sim.Tests/AuditCombatTests.cs`.

Baseline (2026-09-23, branch `sim-combat` @ e39910d): `dotnet test` 118/118; self-test PASS (106 ok);
`scratch/tune profile 3000` 0.428 ms/tick (combat+fleet 0.346).

Status: DONE (2026-09-23). 24 fixed (C-01…C-26 except C-14, a proposal, and C-20, a report); 23 of them have a regression test that fails on the baseline (C-26 is itself a test fix). 8 proposals, 13 reports outside my files.

Diagnostic harnesses written for this audit (git-ignored, in `scratch/tune/`): `fleet <seed> <min> [roam]` (stuck ships,
merchant throughput, ships on land), `stuck`/`rtrace` (one ship's helm trace), `perf <ticks> <crowd>` (the `--fleet=40`
crowd headless), `astar`, `econ`, `pengage`, `flee`, `ptrace`.

## Findings

### C-01 — major (critical for the economy) — AI ships froze against coasts and on nav-grid corners
- Where: `src/Sim/Ai.cs` `AvoidLand` (probes started 60 m + length ahead), `src/Sim/World.Fleet.cs` `CoarseStep`
  (all-or-nothing move onto a land cell), `Seamanship.FollowPath` (no way back to a lane), patrol/raider waypoints behind islands.
- What: a distant (coarse-ticked) ship whose next 0.3 m step fell in a land cell of the 25 m nav grid never moved again
  (the look-ahead 78 m out was clear, so the helm held the course); near ships pressed into a bay kept steering into the
  coast; merchants blown off their lane by a tack or a flight steered straight at the next waypoint through an island.
- Evidence: `scratch/tune fleet 7 20` (player parked, all at peace): **28 of 32 ships** made no 30 m of way for 120 s
  within 20 minutes (seed 11: 29 of 32); merchant arrivals 38 / 29. Trace `stuck 7 5`: a patrol cutter at
  (−587.08, −1475.04) held heading 132° with 0 rudder for the rest of the run. Tests `ADistantPatrolAgainstAShoalCellTurnsAwayInsteadOfFreezing`
  (0 m in 60 s) and `TheFleetKeepsMoving` (10 ships stuck in 6 min) fail on the baseline.
- Fix: `AvoidLand` probes from 2 m off the bow (from 20 m when already inside the coastal margin), keeps to ±66° while
  that is clear and turns further (all the way round) when land is close; it never dodges into the no-go zone and
  looks at the nearest bearings first (open water: one look). `CoarseStep` slides along a land cell's edge and lets a
  ship already on a land cell sail off it, and reports zero velocity when held. `FollowPath` checks the line to the
  next waypoint every 3 s (`AiState.Repath`, already saved) and splices in an A* detour back to it when land is in the
  way; lanes are sailed at full sail to their end (⅓ sail only for the last 90 m). Patrol/raider waypoints must be in a
  clear line (`ClearWaypoint`) and are dropped when land comes between (`WaypointSpent`).
- After: `fleet` 20 min, seeds 7/11/12/13 parked and 7/21/33 roaming: **0–1 stuck ships** (the one left is a
  foundering corvette); merchant arrivals 60 / 88 / 73 / 40 (2–4 per merchant per 20 min, was ~1.5).
- Status: fixed (commit 7d776e1 + follow-ups). `EconomyTests.AScriptedTraderProfitsAndPricesReactAndRecover` measured the
  market's own drift in a populated sea; now that merchants actually deliver, one delivery swamped it, so that test
  clears the sea for its three days (edit outside my files, strictly needed).

### C-02 — major — AI captains stalled in irons
- Where: `src/Sim/Ai.cs` `Engage` (abeam course chosen by the smaller turn only), `SteerTo` (tack flipped every 14 s
  whatever the speed), all helms (`Pilot.Helm` has no notion of sternway or of the wind's eye).
- What: an abeam course or an avoidance dodge inside the no-go zone left a ship head to wind (a coarse ship cannot move
  at all in irons: raider and patrol sat facing each other, `fleet 7`); a ship lying still turned the short way
  through the eye and stalled; with sternway `Ship.Step` reverses the rudder, so the helm turned her the wrong way.
- Evidence: test `FromAStandstillACaptainWearsRoundInsteadOfStallingHeadToWind` — baseline closest approach 0° off the
  wind, 12 s later still wallowing; `scratch/tune flee` trace. Seed 7 fleet diag: raider xebec and patrol cutter both
  at 0.0 m/s targeting each other for the rest of the run.
- Fix: `Seamanship.Helm` wears round (turns away through downwind) when the short turn crosses the wind's eye and she has
  under 15% of top speed, and reverses the order with sternway; `SteerTo` keeps a slow ship on her tack until she has
  way enough to go about; `Engage` holds whichever beam is sailable.
- Status: fixed.

### C-03 — minor — a merchant ran from the map's origin after losing sight of a raider
- Where: `src/Sim/Captains.cs` `MerchantCaptain` (the 12 s of continued flight fled from `AiState.Waypoint`, never set).
- Evidence: `AMerchantThatLosesSightOfARaiderKeepsRunningFromWhereItWas` (baseline 326 → 352 m in 10 s, turning back).
- Fix: remember the threat's position in `Waypoint` (saved field). Status: fixed.

### C-04 — major — being rammed cost the player reputation and turned the rammer hostile
- Where: `src/Sim/World.Combat.cs` `Ram` (`if (a.IsPlayer) Draw(b)` on every collision).
- What: any AI hull that sailed into her (a merchant leaving harbour, a patrol) charged her −10 with its faction and set
  `PlayerHostile`, so a Crown patrol that bumped her then engaged her.
- Evidence: `BeingRammedIsNotDrawingBlood` (baseline Crown −10, patrol hostile).
- Fix: the player draws blood only when her own closing speed along the impact normal is at least the other ship's
  (she did the driving). Status: fixed. Player-rams-target behaviour unchanged (`RammingSplitsDamageByMassAndAngle`).

### C-05 — major — night multiplied the whole Threat for the director
- Where: `src/Sim/World.Fleet.cs` `FleetTick` passed `ThreatNow × 1.5` at night to `Director.Tick`.
- What: GDD §19 says hunters earn credits 1.5× at night; the multiplier also unlocked cards early (brig at real Threat
  1.34, hunter pack at 2.34, galleon at 3.0) and raised the hunter cap (1 + ⌊1.5·Threat⌋).
- Evidence: `NightBringsMoreCreditsButNotHigherCardsOrAHigherCap` (baseline: a third hunter at Threat 1.37, cap 2).
- Fix: `Director.Tick(..., creditMult)`; night multiplies credits only. `HuntersPressHarderAtNightAndWeatherSaves` still
  passes. Status: fixed.

### C-06 — minor — the Kraken and the Weed-Kraken chased her anywhere, forever
- Where: `src/Sim/World.Monsters.cs` `KrakenTick`/`WeedTick` Approach (no region check).
- What: a ship that outran the weed (13 m/s; a cutter makes 13.2 outside the Sargasso) or the kraken (20 m/s; storm
  gusts) was chased across the chart indefinitely, and no other beast can wake while one is in play.
- Evidence: `ABeastStillHuntingDoesNotFollowHerOutOfItsWaters(Kraken|WeedKraken)` (baseline still approaching after 20 s).
- Fix: while still approaching, both give up when she is outside their region (as the serpent and crocodile already do);
  a grip, once made, still holds. Status: fixed.

### C-07 — polish — AI crews ran up a plank debt
- Where: `src/Sim/World.Combat.cs` (`() => ship.Timber-- > 0` decremented on every failed patch attempt, −30/s).
- Evidence: `AnAiShipWithoutPlanksDoesNotRunUpATimberDebt` (baseline −60 after 2 s). Fix: `TakeTimber`. Status: fixed.

### C-08 — minor (major at night / with a Lookout) — AI lookouts used the player's own vision
- Where: `src/Sim/Captains.cs` (patrols and raiders searched `world.VisionRadius`), `World.Fleet.cs` `NearestEnemyOf`/`NearestThreatTo`.
- What: every patrol and raider on the chart saw exactly as far as the player: her Lookout officer (+50%) and lantern
  grades made all raiders spot prey farther, fog around her blinded ships 2 km away (and clear air around her let them
  see through their own fog), and the lantern rule (GDD §9/§19: a lit lantern at night is seen from 650 m, doused
  200 m) applied only to hunters — raiders and patrols capped it at her own 250 m.
- Evidence: `scratch/tune pengage` (the old AiTests arena lies in a fog bank: the patrol "saw" 220 m only because the
  player, 1.2 km away, was in clear air); code reading for the officer/lantern coupling.
- Fix: `World.SightAt(p)` — an AI lookout's own sight (day 500, night 250, fog/rain/ash as for the player, no officers);
  `NearestEnemyOf`/`NearestThreatTo` see the player as far as `DetectRangeOfPlayer()` (the decided beacon/shadow rule).
  `AiTests.MerchantsFleeRaidersAndPatrolsEngageThem` moved its patrol half out of the fog bank (the premise "a patrol
  sights a raider 220 m off" needs clear air). Status: fixed.

### C-09 — major — AI broadsides were fired where they could not hit
- Where: `src/Sim/Ai.cs` `Engage` (fire when the target lies within ±22° of the beam, at any range).
- What: balls fly square off the side (±3°). At 170 m, 21° off the beam, they pass ~60 m wide; a whole 12 s reload was
  wasted. The patrol in the AiTests arena fired five broadsides at 165–172 m and hit nothing.
- Evidence: `AnAiCaptainHoldsHisFireUntilTheBroadsideWouldCrossHerHull` (baseline fires 21° off the beam at 170 m).
- Fix: `Seamanship.Bears(ship, target)`: fire the side whose ripple will cross where she will be when the balls arrive
  (within the battery's spread along the keel plus her half length). Used by hunters, patrols, raiders and the autopilot.
  This makes AI gunnery as accurate as intended — see the balance note below.
- Status: fixed.

### C-10 — minor — two logbook causes were wrong or unreachable
- Where: `src/Sim/World.Combat.cs` ball hits / `Sink` cause, `src/Sim/Combat.cs`, `World.Monsters.cs` `GhostTick`.
- What: the ghost ship's balls and fort balls carry no shooter, so they set neither the monster clock nor `LastHitBy`:
  **"Sunk by the Ghost Ship" (SUNK_GHOSTSHIP) could never be written**, and a ship sunk by a fort read "Lost at sea".
- Evidence: `TheGhostShipCanSinkHerAndTheLogbookSaysSo`, `AFortThatSinksHerShotHerToPieces` (baseline: SUNK_SEA both).
- Fix: `Cannonball.From` names a beast's ball; a ball striking her records the time (`shotAt`) and, for a beast, the
  monster clock. Not saved (a hint for the recap only): after a resume within 60 s of such a hit the cause falls back.
- Status: fixed.

### C-11 — minor — the director dealt a hunter pack past the cap, and paid for cards that never came
- Where: `src/Sim/Director.cs` `Tick` (the cap was checked before the draw), `World.Fleet.cs` `SpawnCard`.
- What: at Threat 3.5 with 3 hunters out (cap 4) the three-ship pack — the likeliest card by weight — put 6 at sea; when
  no sea point beyond her sight was found the card's credits were already spent.
- Evidence: `AHunterPackIsNotDealtIntoAFullSea`, `ACardWithNowhereToSpawnIsNotPaidFor`.
- Fix: only cards that fit under the cap whole are dealt; an unplaceable card is refunded. Status: fixed.

### C-12 — minor — merchants never mended
- Where: `src/Sim/Captains.cs` `MerchantCaptain` — `World.Recover` ("in port an AI crew mends") ran once on arrival (0.7 HP).
- Evidence: `AMerchantMendsWhileSheLiesInPort`. Fix: mend while lying inside her port's ring. Status: fixed.

### C-13 — major (balance yardstick) — autopilot harness bugs
- Where: `src/Sim/Autopilot.cs`.
- What: (a) against the Kraken it ordered "repair & pump" (0 gunners → 120 s reloads) and aimed at the beast's centre
  (her own position in a grip), so it died pinned; (b) it sheltered under a fort whose guns were turned on her
  (open port, rep −20…−50); (c) after buying a bigger hull it checked officer berths on the old hull; (d) in its last
  stand it made for the nearest open harbour even when that harbour had already used its one rescue.
- Evidence: `TheAutopilotShootsTheKrakensTentaclesToBreakFree`, `TheAutopilotDoesNotShelterUnderAFortThatFiresOnHer`;
  `scratch/tune kpilot` trace (first volley hit all four tentacles, then no reload for 120 s; sunk at 63 s).
- Fix: battle stations and the nearest live tentacle as the aim when the Kraken has her; no hostile forts as sanctuary;
  `w.Ship` after the hull purchase; the rescue run skips spent harbours (`TheAutopilotRunsForAHarbourThatWillStillTakeHerIn`).
  It issues no command a player cannot. Days are measured correctly (`DaysSurvived` is tick-derived and frozen in port);
  no crash in the batches below.
- Tried and reverted: limiting its threat radius to what a player sees at night (sight + the HUD's 300 m chevrons) made
  its own lantern logic flicker every tick (doused → hunter out of range → relight → back in range): 320 "flights" per
  voyage. It keeps the 650 m radius (a slight night-time information edge, listed under proposals).
- Status: fixed.

### C-14 — (not a fix) — see P-04: big AI hulls in the Mangrove Maze

### C-15 — minor — hunters stood off from a fort that was firing on her
- Where: `src/Sim/Captains.cs` `HunterCaptain` (sanctuary = any other faction's fort whose port was open, rep > −50).
- What: GDD §8 "a friendly port is a real sanctuary"; at rep −20…−50 the fort fires on her yet hunters waited outside.
- Evidence: `AFortThatFiresOnHerIsNoSanctuaryFromHunters`. Fix: sanctuary needs a fort not hostile to her. Status: fixed.

### C-16 — minor — the ghost ship woke and fired in clear daylight
- Where: `src/Sim/World.Monsters.cs` (hourly roll; `GhostTick` gun clock).
- What: GDD §12 "only visible in fog and at night". The roll ignored the weather; a daylight ghost fired an invisible
  broadside on its first tick and kept firing while it faded, and the notice promised "a lantern in the fog".
- Evidence: `TheGhostShipDoesNotFireFromClearDaylight`, `TheGhostShipOnlyWakesInFogOrAtNight`.
- Fix: it wakes only in fog or at night, and does not fire while fading. Status: fixed.

### C-17 — polish — the crocodile jumped back to its bank
- Where: `src/Sim/World.Monsters.cs` `CrocodileTick` Retreat (after 5 s it was set onto its perch, up to ~200 m).
- Evidence: `TheCrocodileSlidesBackToItsBankRatherThanJumping` (baseline: 41 m in one tick).
- Fix: it slides home surfaced within the same 5 s (6 m/s, faster from farther off), so it lands on its bank when the
  timer runs out instead of jumping there. A first version swam home submerged after the 5 s; that hid it from the guns
  longer and raised crocodile deaths in 100 Calm voyages from 2 to 10 (A/B, `scratch/balance/out/ab-croc-*.txt`);
  the timing-preserving slide gives 4 (noise). Status: fixed.

### C-18 — minor — hunters homed on a doused ship
- Where: `src/Sim/Captains.cs` `HunterCaptain` search (every new waypoint within 900 m was her *current* position).
- What: GDD §9 lets a doused ship (seen at 200 m) slip past a blockade; the hunter tracked her anyway.
- Evidence: `AHunterThatLosesHerSearchesWhereSheWasNotWhereSheIs`.
- Fix: close on where she was last seen, then cast about there. Status: fixed.

### C-19 — polish — flotsam reach was measured from the ship's centre
- Where: `src/Sim/World.Combat.cs` flotsam pickup (`12 m + half the beam` from the centre).
- What: a long hull sailed barrels down her side without picking them up (a man-o'-war: only within 20 m of midships).
- Evidence: `ALongHullPicksUpABarrelAlongHerSide`. Fix: distance to the hull's keel line. Status: fixed.

### C-20 — minor (test infrastructure) — three self-test checks are timing-flaky under load (pre-existing)
- Where: `game/Main.SelfTest.cs` (not my file; reported, not changed).
- What: "the spyglass sees 700 m along the cone" (the camera is still easing after the serpent step's teleport; the cone
  test has a 10° budget and the baseline already reads 9°), "the first-voyage hint shows and is remembered" (`atSea`
  accumulates real time, so under load `first_sail` fires before the explicit `Bind` and the check sees `trade`),
  "a broadside booms" (the S tap's `sail_down` one-shot lands after the shot counter is sampled).
- Evidence: the baseline worktree failed 2 of 3 runs (`scratch/selftest-base-r1.log`, `-r2.log`; load average 10–27
  from parallel agents); this branch 3 of 6 on the same checks, and PASS 106/106 in `scratch/selftest-r1.log`.
- Proposal (for the self-test owner): after the serpent step set `prevPose = curPose = (pos, heading); camPos = Ink.V(pos)`
  as the test's start does; `profile.HintsSeen.Remove("first_sail")` before `hintsView.Bind(world)`; wait a few more
  frames after `Tap(S)` before sampling `OneShotsPlayed`.

### C-21 — major — a spent harbour let her dock by hand while foundering, and she sank with a sound hull
- Where: `src/Sim/World.Port.cs` `Dock` (not my file; one line, strictly needed for the §19 rescue rule in my scope).
- What: the second time she founders at a port its rescue is refused ("will not take her in twice"), but F still docked
  her: she could repair to 100/100 at the shipwright, cast off, and sink 20 s later "Lost at sea" — only the harbour's
  rescue ends a hull-zero last stand.
- Evidence: `scratch/tune spentdock` (dock Ok, repair to 100/100, water 0, run over 25 s later with hull 100/100);
  `AHarbourThatHasTakenHerInOnceDoesNotLetHerDockWhileFoundering`.
- Fix: `Dock` returns `PortClosed` while she founders at a port in `RescuedAt` (§19: "the second time she founders at
  its mouth"). Status: fixed.

### C-22 — minor — the sparring captain fired the same wasted broadsides
- Where: `src/Sim/Captain.cs` `SparringCaptain` (±25° off the beam at up to 0.9 × range; the `--sparring` opponent).
- Fix: `Seamanship.Bears`, as C-09. `CombatTests.TheSparringCaptainFightsBackAndReplaysExactly` still passes. Status: fixed.

### C-23 — minor — a rammed hull swung its bow into the rammer
- Where: `src/Sim/World.Combat.cs` `Ram` (the second hull's spin used `-=` on the torque of the push it takes).
- What: the push on b is −n at pb, so its spin is sign(r_b × −n); the flipped sign turned a struck bow toward the ship
  that hit it (up to 0.3 rad/s). The player is always the first of a pair, so the ships she rams (and AI pairs) swung wrong.
- Evidence: `ARammedBowIsPushedAwayNotIntoTheRammer` (baseline AngVel +0.174: bow to starboard, into A). Status: fixed.

### C-24 — polish — a negative shot count fired a negative broadside
- Where: `src/Sim/World.Combat.cs` `Fire` (`if (guns == 0)`): `min(guns, −3)` "fired" −3 guns, added 3 shot and spent the
  reload. Unreachable through play today (nothing sells shot below zero), guarded anyway since the brief asked.
- Evidence: `AHoldWithoutShotFiresNothingEvenIfTheLedgerRunsNegative`. Fix: `guns <= 0`. Status: fixed.

### C-25 — minor — ships sat on rocks the nav grid cannot see
- Where: `src/Sim/Ai.cs` `AvoidLand` (look-ahead on the 25 m nav grid only).
- What: `NavGrid` marks a cell as land only when its centre is within 10 m of a coast, so a thin spit can fall between
  cell centres; the look-ahead reads clear water while the hull is on the rocks (a fluyt sat 2 minutes on one,
  `fleet 7 roam`, after C-01).
- Evidence: `AShipAgroundOnRocksTheGridCannotSeeWorksHerWayOff` (a 20 m bar off the grid across a patrol's course;
  baseline: pinned 237 m short for the rest of the run).
- Fix: when she is aground or barely moving within half a length of a real coast, bearings into those rocks count as
  blocked, so she works along and off them. Status: fixed (she crawls round; the root cause is in `NavGrid`, see R-12).

### C-26 — minor (test infrastructure) — the populated-sea budget test failed under load (lead's R-05)
- Where: `tests/Sim.Tests/AiTests.cs` `ThePopulatedSeaTradesAndKeepsItsBudget` (240 s of sim in < 12 s of wall time).
- What: with eight agents loading the box (load average 12–27) it took 16–25 s in full parallel runs and failed, while
  the same world runs 240 s of sim in 0.6–1.6 s alone.
- Fix: the budget (1.67 ms a tick) is judged on the quickest of eight 30 s stretches, so a busy machine or a parallel
  test slows some stretches but a real cost cliff slows them all. Status: fixed (commit 3a2432b); green in every full
  run since, including at load average 29.

## Balance note
The fixes change what the balance runs measure: AI ships now sail, fight, trade and aim as designed (C-01, C-02, C-08,
C-09) and the autopilot no longer dies pinned by the Kraken or shelters under hostile guns (C-13). Same batch as
`docs/BALANCE.md` (`scratch/balance`, seeds 2000/3000/4000; output `scratch/balance/out/audit-final.txt`):

| Preset · runs · cap | Days p10 / p25 / p50 / p75 / p90, mean | Alive at cap | Causes | Economy |
|---|---|---|---|---|
| Rough · 200 · 30, baseline (BALANCE.md) | 3.1 / 9.0 / 15.5 / 30 / 30 | 50 | water 77, guns 35, serpent 14, kraken 12, eruption 8, croc 3 | trades 8.9, 11 bigger hulls |
| Rough · 200 · 30, now | 2.4 / 8.2 / 15.4 / 30 / 30, mean 16.8 | 52 | water 84, guns 25, eruption 10, kraken 10, serpent 7, croc 6, ghost 6 | trades 12.0, visits 17.3, 26 bigger hulls |
| Calm · 100 · 40, baseline | 3.9 / 8.9 / 15.7 / 39.4 / 40 | 24 | water 38, guns 14, kraken 8, croc/serpent/eruption 5 | 9 bigger hulls |
| Calm · 100 · 40, now | 2.6 / 6.5 / 13.9 / 29.9 / 40, mean 18.0 | 18 | water 45, guns 8, kraken 8, ghost 7, eruption 5, serpent 5, croc 4 | trades 10.8, 13 bigger hulls |
| Tempest · 100 · 20, baseline | 3.0 / 6.6 / 11.6 / 20 / 20 | 35 | water 37, guns 15, kraken 6, croc 3 | 2 bigger hulls |
| Tempest · 100 · 20, now | 3.4 / 9.1 / 14.3 / 20 / 20, mean 13.3 | 33 | water 42, ghost 7, kraken 6, guns 5, serpent 4 | trades 8.4, 2 bigger hulls |

Reading: medians move within the noise of 100–200 voyages (Rough unchanged, Calm −1.8, Tempest +2.7); the economy is
healthier (+35% trades on Rough, twice the hull upgrades) because merchants reach port and the autopilot survives
beasts it used to die to. "Sunk by the Ghost Ship" now appears (C-10: it was always the ghost, logged as water or guns).
The GDD §11 targets are still missed the way BALANCE.md describes (Calm's median far under Day 25; early water deaths
dominate on every sea) — no tuning was changed here; see P-08 for the Kraken's share.

## Proposals (design or balance — not changed)
- **P-01 Cannonballs fly through islands.** No land test in the ball loop: ships and forts shoot through islands and a
  player cannot take cover behind one. Outline: splash a ball whose step enters a land cell (`Map.Nav.IsLand(next)`);
  start fort balls on the harbour side of the coast.
- **P-02 The Siren has no teeth.** Grounding does no damage anywhere, so her pull toward the rocks only costs time and
  "Wrecked on the Siren's rocks" (SUNK_SIREN) can never be written. Outline: while she sings, a coast strike deals
  ~2 × `LastImpact` (+ a leak above 3 m/s) attributed to the Siren.
- **P-03 A Kraken grip with no shot is certain death.** With 0 munitions or 0 guns nothing frees her (the weed can be
  cut by carpenters, the Kraken cannot). Outline: carpenters with no leak to plug hack a tentacle at ~1 HP/s each, or
  the grip tires after ~60 s.
- **P-04 Big AI hulls in the Mangrove Maze.** Only small hulls fit (§5), but the rule binds the player alone: frigate
  and galleon hunters follow her in, and fluyt/barque merchants, corvette patrols and brig raiders spawned at mangrove
  ports sail there (fleet diag: 300–2,000 big-hull samples inside per 20 min). Enforcing it needs a region mask in
  `Pathing.Find` per hull class (and in the lane cache key), small hulls at mangrove ports, and blocking in
  `KeepShipOnTheChart`/`CoarseStep` — otherwise big merchants would stick at the border.
- **P-05 The autopilot's night-time information edge.** It notices hostiles to 650 m in any light; a player sees her
  sight plus the HUD's 300 m chevrons (450–550 m at night). Aligning it needs hysteresis in its lantern logic (C-13).
- **P-06 A sunk Crown hunter is worth +3 Crown.** `crown_cutter`/`crown_frigate` cards are Crown ships with the hunter
  role, so sinking one pays the Crown's bounty on herself. Decided rule (§19); flagged only.
- **P-07 A ram death reads "Shot to pieces".** There is no SUNK_RAM text (would need an en.csv row).
- **P-08 The Kraken is certain death for the lean sloop.** Traced (`scratch/tune kseed 2018`): 4 crew, one gun a side,
  12 shot. The grind (1.5 HP/s) crosses a 10% leak threshold every ~7 s on top of the smash's leak every 8 s, so with
  battle stations (no carpenter) she is at 100% water 28 s into the grip, before her third volley (36 s) can cut the
  third tentacle; "repair & pump" keeps her afloat longer but nobody serves the guns. Kraken deaths: 19 of 200 Rough
  voyages, median day 7.9. Options: the grind does not open the 10% threshold leaks (only the smash does), or one
  tentacle fewer to cut for small hulls, or the Kraken wakes only from Threat 1.5.

## Reports outside my files (evidence; not changed)
- **R-01 `Ship.cs` reload is fixed at the moment of firing.** `Reload[side] = ReloadTime` uses the manning then;
  "fire, then 1 (battle stations)" or casualties during the reload change nothing. §7: reload scales with manning.
  Suggest decrementing `Reload` by `dt × manning(now)` from a base reload.
- **R-02 `Ship.cs` the last-stand glass is set once.** `Hourglass = 20 + 3 × pumps` at the moment the stand begins;
  ordering "3 · repair & pump" after it starts does not extend it (§8: "extended by pumping up to ~35 s").
- **R-03 `Ship.cs` scaled hunters leak from their base hull.** `SpawnHunter` sets `HullHp = base × Threat scale` above
  `MaxHp`; `Hit` counts leak thresholds from `MaxHp`, so a 1.5× hunter opens 15 leaks over its life instead of 10.
- **R-04 `Ship.cs` perf: `Stations()` allocates** an `int[4]` per call (~5 calls per ship per tick: `RiggerFactor` ×3
  in `Step`, `ReloadTime`, `DamageTick`), i.e. ~250 small arrays a tick with the fleet.
- **R-05 `World.Save.cs` logbook state not saved:** `LastMonsterHit`/`MonsterHitTime` and the player's `LastHitBy` —
  a resumed voyage that sinks within 60 s of a hit gets "Lost at sea" instead of the beast or "Shot to pieces".
- **R-06 `Hud.cs` the Lookout's warnings are missing.** `World.WarningRange` (100/200/300 m, §7) is used nowhere;
  `EdgeMarkers` gives every captain chevrons to sight + 300 m regardless of officers.
- **R-07 `Audio.cs` the Siren has no cue** (the monster switch falls through to `monster_ghost`).
- **R-08 `Main.cs --fleet=N`** spawns its crowd through `SpawnHunter`, so every "merchant" and "patrol" gets a
  `HunterCaptain` and attacks the player: the perf scene is not real traffic.
- **R-09 First-time merchant lanes run A* inside a tick** (`scratch/tune astar`: 3.1 ms average, 24 ms worst on an
  unreachable goal); the early game sees occasional 10–25 ms sim ticks. Precompute lanes at load or spread them out.
- **R-10 (lead's note)** `World.Combat.cs` munitions (Fire) and timber (`TakePlayerTimber`) still subtract from
  `Player.Cargo` directly; the lead switches them to sim-trade's `Player.Remove` at merge.

- **R-11 `Ship.cs` a water-only stand survives the hull reaching 0.** `WaterOnlyStand` is set once; shot to 0 hull
  during it, pumping back under 80% still ends the stand (with 0 hull), and the next tick starts a fresh hull-zero stand
  with a new 20–35 s glass; the logbook then says "Foundered" for a ship shot to pieces.
- **R-12 `NavGrid.cs` thin coasts fall between cell centres** (a cell is land only if its centre lies within 10 m of
  the coast): lanes and look-aheads cross spits the physics collides with. Suggest marking every cell a coast edge
  crosses (rasterise the grown polygon's edges) as well as the centre test. C-25 works round it in the AI.
- **R-13 Replays miss two player actions.** The crew panel (`World.SetStations`) and the spyglass (`SetSpyglass`,
  which inks the chart and discovers ports) are applied between ticks and logged nowhere, so `World.Replay` cannot
  rebuild a voyage that used them (the suspend save is unaffected: it stores the state).
## Performance
Interleaved with the baseline worktree under the same load (Release, `scratch/tune`): `profile 3000` 0.40–0.50 ms/tick
(baseline 0.54–0.58); the `--fleet=40` crowd headless (`perf 3000 40`) 0.64–0.67 ms/tick (baseline 0.80–0.95). The look-
ahead checks the nearest bearing first and stops in open water, which more than pays for the new probes; lane
rejoins are cheap (A* from a ship back to its next waypoint, 0.2 ms typical, backed off to 30 s when no way exists).
240 s of the populated sea: 0.56–0.62 s (baseline 0.77–0.79 s).

## Checked, no change needed
Ball hits (segment vs capsule, the shooter and sunk ships skipped), splashes at range, ripple delays; munitions are
paid per gun fired; ram separation, momentum exchange (e 0.2), bow/flank multipliers and the 0.6 s cooldown; flotsam
drift, 60 s expiry and no double pickup (a chest is removed on pickup, a barrel keeps what did not fit); forts fire only
at hostile hulls in their ring (the player when rep ≤ −20, other factions' raiders/hunters); the hostility matrix;
merchant cargo is taken from the producer and delivered once (or spilled); coarse/near hand-off keeps pose and velocity;
Threat tiers, enemy scaling (+10%/Threat), hunter patience and the 150 s quiet after a hunter leaves; monster rolls only
in their regions, the 60 s rest, achievements unlocked once (HashSet), spawn/beaten/escaped notices and events;
eruptions. Monsters have no hull to ram (by design); a fort stands on land.

## Tests run
- `nice -n 10 dotnet test tests/Sim.Tests`: **147/147** (baseline 118 + 29 in `AuditCombatTests`; 26 facts, one theory
  ×2 — every one fails on the baseline source, checked in a baseline worktree: 25 of 25 at the time, and the three added
  later were checked against the code without their fix). Final run at d282082: 147/147.
- Self-test (`~/.cache/lt-audit/vrun.sh … --selftest`, final code): **PASS 106 ok** in 3 of 5 runs
  (`scratch/selftest-final-1/2/4.log`); the other two failed only the pre-existing timing-flaky checks of C-20
  (spyglass cone once; first-voyage hint + broadside boom once) at load average 8–14. The untouched baseline, same
  runner and load: PASS in 1 of 3 of my runs (`scratch/selftest-base-r1/2/3.log`), failing the same checks.
- Balance: `scratch/balance` Rough 200 / Calm 100 / Tempest 100 (table above), no errors in 400 voyages.
- Perf: `scratch/tune profile 3000` and `perf 3000 40` against the baseline worktree (section above).
- Saved state: no save format change. New behaviour reuses saved fields (`AiState.Repath` as the lane/waypoint check
  clock, `AiState.Waypoint` for a merchant's last threat); the crocodile state machine is unchanged in the enum.
  `AFleetUnderWaySavesAndContinuesIdentically` guards save → load → continue with the fleet under way; the old
  `SaveAndLoadContinueIdentically`/replay tests pass. `Cannonball.From` and `shotAt` are not saved (a ball in flight
  or a 60 s logbook hint across a resume; documented in C-10).

## Round 2 (lead's decisions, after merging `merged` at dcff1e4)

Merged tree before round 2: `dotnet test` 171/171.

### R2-01 — P-01 done — cannonballs stop at land; forts fire from the harbour side
- Where: `src/Sim/World.Combat.cs` (ball loop, new `LandAlong`), `src/Sim/Combat.cs` (`Geometry.Crossing`),
  `src/Sim/World.Fleet.cs` `FortsFire`, `src/Sim/Ai.cs` (`ClearShot`), `src/Sim/Captain.cs`.
- What: a ball whose step meets an island bursts on the shore (Splash event at the entry point); monsters on a coast
  (the Siren's perch, the crocodile's bank) are still struck first. Captains hold their fire when an island lies on the
  line to the target (tested only when a loaded side bears). Fort balls leave from 15 m off the coast toward the harbour
  (the islands are star-shaped about their centres, so that point is at sea).
- Evidence: `AnIslandStopsShot` (both balls burst on the islet's near shore, the sloop behind it untouched; baseline:
  the balls flew through), `AnIslandBetweenGivesCover` (baseline: fired through the island),
  `AFortsShotLeavesFromTheHarbourSideOfItsCoast` (guard: the fort still hits her in its ring with islands present).
- Cost: one bound-circle test per island per ball step, edges only for the islands under the step.

### R2-02 — P-02 done — the Siren's rocks bite
- Where: `src/Sim/World.Monsters.cs` `SirenTick` (+ constants `SirenRockDamage/MinImpact/LeakImpact`).
- What: while she sings (within 250 m of her perch), a coast strike of at least 1 m/s costs 2 HP per m/s of impact
  (`Ship.LastImpact`) and a leak from 3 m/s, attributed to her through `MonsterHits`, so "Wrecked on the Siren's rocks"
  (SUNK_SIREN) is written when she sinks within 60 s of it. Strikes are a second apart (the monster's saved `GunClock`,
  unused by the Siren, is the clock), so one crash counts once and grinding along a shore does not bleed her.
  Grounding without a Siren singing stays harmless.
- Evidence: `TheSirensRocksBite` (a 6 m/s run into an islet: 6–14 HP and a leak with her singing, 0 without;
  baseline 0 both), `WreckedOnTheSirensRocksIsReachable` (baseline: never SUNK_SIREN).

### R2-03 — P-03 + P-08 done — the Kraken can be beaten and escaped by a lean sloop
- Where: `src/Sim/World.Monsters.cs` (`TentacleHp`, `KrakenSmashEvery`, `KrakenHackRate`), `src/Sim/Ship.cs`
  (`Hit(..., thresholdLeaks)`, `Hacking`), GDD §19 row "Kraken retune (audit round 2, M7 tuning)".
- What: a tentacle has 4.5 HP, so one grade-0 ball (6 × 0.8 at the least) cuts it — §12 "each one hit frees a hold";
  still three to break free. The grind no longer opens the 10% threshold leaks and the smash (3 HP + one leak) comes
  every 12 s (was 8: a lone carpenter spent the whole grip plugging). Carpenters with no leak to plug hack at a
  tentacle at 1 HP/s each instead of patching, so a ship with no shot cuts herself free.
- Evidence: `OneGradeZeroBallCutsATentacle`, `WithNoShotHerCarpentersCutHerFree`, `TheKrakensGrindOpensNoSeams` (all
  fail on the old code); `MonsterTests.TheKrakenPins…` now runs without a carpenter (so the guns do the work) and past
  the first smash. Trace `scratch/tune kseed 2018` (the round-1 death): gripped at t 182 s, free by t 204 s with 79 HP
  (was: sunk at t 241). Rough 200 (seeds 2000–2199, cap 30), same tree before/after: **Kraken deaths 14 → 6**;
  p50 14.6 → 15.2, alive at cap 51 → 45 (`scratch/balance/out-r2/pre-kraken.txt`, `post-kraken.txt`; the "after" run
  also includes R2-04's ship-state fixes).

### R2-04 — R-01, R-02, R-03, R-04, R-11 done — the ship's combat state (`src/Sim/Ship.cs`, combat state only)
- R-01: a side reloads at the manning of each moment (`Reload` counts down `BaseReload` at `Manning`), so crewing the
  guns after a broadside speeds the reload under way; `ReloadTime` = `BaseReload / Manning` as before.
  `CrewingTheGunsSpeedsTheReloadUnderWay` (old: 24 s whatever happened after the shot).
- R-02: pumping hands sent during the stand add their 3 s each, up to 35 s in all; time granted is never taken back
  (`StandBonus`, saved in `SaveData`/`ShipSave`, absent in older saves = 0). `HandsSentToThePumpsDuringTheStandLengthenTheGlass`.
- R-03: `MaxHp` includes a director spawn's Threat scale (`DamageMult`, already saved), so a 1.5× hunter's leaks,
  patch cap and `Recover` count from 150, not 100 (15 leaks → 10). `AScaledHuntersLeaksCountFromItsScaledHull`.
- R-04: `Split(out guns, out sails, out repair, out pumps)` computes stations without an array; `RiggerFactor`,
  `Manning`, `DamageTick` and the monsters use it (`Stations()` still returns an array for the HUD/tests).
- R-11: shot to 0 hull during a stand for water, the stand becomes a hull stand (the same glass runs on; pumping out
  no longer ends it; the logbook names the guns). `AWaterStandShotToPiecesIsAHullStand` (old: stand ended, fresh glass).
- Save: `StandBonus` is the only new saved field (both JSON classes; missing → 0). Reload values keep their meaning
  closely enough (seconds at full manning) for older saves.

### R2-05 — P-07 done — "Rammed and sunk"
- `SUNK_RAM,"Rammed and sunk"` added to `assets/text/en.csv` (one row); the logbook's cause is SUNK_RAM when a hull
  struck her last (and more recently than any ball) within 60 s (`rammedAt`, not saved, like `shotAt`).
- Evidence: `ARamDeathSaysSo` (old: "Shot to pieces"), `EveryCauseOfSinkingHasItsLine` (every cause the sim writes has
  an en.csv row).

### R2-06 — R-09 done — no A* inside a tick for merchant lanes
- Where: `src/Sim/World.Fleet.cs` (`Lane`, `HarbourField`, `WarmLanes`, `Pull`).
- What: each harbour gets one breadth-first sea-distance field (`NavGrid.Distances`, as float), built one a tick from
  the start of any voyage with traffic (36 harbours ≈ 36 ticks, before the first merchant's 2 s wait ends); a lane is
  the walk down the destination's field from the other harbour, string-pulled by doubling-then-halving line checks.
  Lanes depend on the map alone (no wind of the moment), so a reloaded voyage plans the same lanes.
- Measured (load-robust: each operation timed five times, the quickest counts; `scratch/tune lanecost2`, seed 7, the
  200 merchant-range pairs): old A* per pair mean 0.27 ms, worst 2.79 ms, run up to ~520 times in a voyage inside
  ticks; new lane mean 0.052 ms, worst 0.13 ms; one harbour field mean 1.77 ms, worst 3.36 ms, once per harbour in the
  warm-up. (Raw per-tick spike counts were dominated by the machine's load, average 17–19: 23–63 ms ticks landed even
  in phases that do no path work, so they are not quoted as evidence.)
- Evidence: `LanesDependOnTheMapAloneAndKeepToTheSea`; fleet diag seeds 7/11 roaming: 0 stuck, arrivals 45/65.

### R2-07 — R-08 done — `--fleet=N` is real traffic
- `World.SpawnTraffic(role, pos)` gives a merchant/patrol/raider its own captain, a home port of its kind and the hulls
  `PopulateFleet` uses; `game/Main.cs` (`--fleet=N`, one small local edit) spawns raiders, patrols and merchants with it.
- Evidence: `TheFleetCrowdIsRealTraffic`.

### Round 2 — tests run
- `nice -n 10 dotnet test tests/Sim.Tests`: **187/187** (merged tree 171 + 16 round-2 regressions in
  `AuditCombatTests`; each behavioural one fails on the code before its change, checked by stashing `src/`).
- Self-test (`vrun.sh`, absolute `VRUN_USERDATA`): **PASS 142 ok**, twice (`scratch/selftest-r2-1.log`, `-2.log`).
- `--fleet=40` scene (`--seed=7 --fleet=40 --fps --frames=300`) runs with the real traffic: 179 draw calls (Mesa
  runner: FPS not quoted). Headless sim cost unchanged within noise: `profile 3000` 0.34 ms/tick, the 40-ship crowd
  (`perf 3000 40`) 0.56–0.71 ms/tick (round 1: 0.64–0.67).
- Saved state touched: `StandBonus` (new, optional). No other format change.
- GDD §19: three rows added (Kraken retune, shot stops at land, the Siren's rocks).
