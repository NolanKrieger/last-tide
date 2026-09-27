# AUDIT — sim-trade (economy, ports, progression, exploration, achievements)

Auditor: sim-trade. Copy `~/.cache/lt-audit/sim-trade/`, branch `sim-trade`.
Scope: `src/Sim/Market.cs, Goods.cs, Player.cs, World.Port.cs, World.Progression.cs, Parts.cs,
Exploration.cs, World.Exploration.cs, Achievements.cs, Cosmetics.cs`, produce/consume tables in `Regions.cs`;
tests `EconomyTests, ProgressionTests, ExplorationTests, RunWrapperTests`, new `tests/Sim.Tests/AuditTradeTests.cs`.

Baseline: `dotnet test` 118/118 green (52 s); self-test PASS, 106 `ok`.
Probe used for numbers: `scratch/audit-trade/` (git-ignored console app; `dotnet run -c Release -- roundtrip|repfarm|cannons|downgrade|start 1 1000|nearest …|digoverlap 1000`).

Status: DONE. 14 fixed (T-01…T-12, T-14, T-15: 2 critical, 3 major, 7 minor, 2 polish), 1 proposed outside my files
(S-13), 11 reported outside my files (R-01…R-11), 6 design/tuning proposals (P-01…P-06).

## Findings

Severity: critical / major / minor / polish. Status: fixed / proposed / suspected / reported (outside my files).
Every "fixed" item has a regression test in `AuditTradeTests.cs` that fails on the baseline: 19 of the 23 new test
cases fail there; the other four are a guard (the friendly premium still paid on goods from elsewhere), the lean-start
measurement, and two tests of the new saved state (quotes are pure; lots and visits ride in the save).

### T-01 — critical — same-port buy→sell round trips print gold — FIXED
- Where: `World.Progression.cs` `BuyPriceMult`/`SellPriceMult` (both sides ±10% friendly, ±3/6/10% quartermaster)
  applied on top of a 5% bid/ask spread (`Market.Spread` 0.95) in `World.Port.cs` Buy/Sell.
- Wrong: with friendly standing the port sells at 0.90·P and buys back at 0.95·1.10·P = 1.045·P; with a legendary
  quartermaster too, 0.81·P vs 1.15·P. The clock is frozen in port, so this is an instant, risk-free, unlimited
  gold press. Probe (`roundtrip`, 5 seeds, 1/5/10 units of every good at the start port): best round trip
  neutral 0, friendly **+270** (10 silk), green QM +26, seasoned +131, legendary +270, friendly+legendary **+621**.
  Even a green quartermaster alone (+0.9%) profits on dear goods.
- Fix: a port buys its own goods back at no more than it was paid. `Player` keeps one lot per good (port it was last
  bought at, units still aboard, gold paid; clamped to the hold as goods are eaten/fired/sold elsewhere);
  `World.SellQuote` caps the share of a sale that comes out of this port's lot at what was paid. All the drafted
  numbers stay: goods carried from any other port still get the full friendly / quartermaster premium, and buying
  still gets the discount. New `World.BuyQuote`/`SellQuote` are what Buy/Sell charge and pay.
- Tests: `ASamePortRoundTripNeverProfits` (5 standing/QM combos × 2 seeds × every good × 1 and 10 units, incl. a
  cast-off/re-dock in between), `GoodsBoughtElsewhereStillSellAtTheFriendlyPrice` (guard: premium intact elsewhere).
- Residual (design note, not a bug by the drafted numbers): carrying the SAME good back and forth between two
  friendly ports with similar prices still pays the bonus each leg (+16%/leg friendly only, up to +42%/leg with a
  legendary quartermaster), because the bonuses (up to 21% a side) are wider than the 5% spread. It needs sailing
  time and is well below a real producer→consumer route (+190% friendly, +254% max bonuses), so it is not dominant.
  If Nolan wants "better prices" to never pay without a real price difference, see proposal P-01.

### T-02 — critical (enabler of T-01) — standing farmed by re-docking — FIXED
- Where: `World.Port.cs` Dock reset `tradedThisVisit` on every dock ("+1 per trading visit, capped per visit").
- Wrong: Esc (cast off) → F (dock) in the same harbour is a new "visit": probe `repfarm`: 30 cycles took 1.0 s of game
  clock and moved Free Traders standing 0 → **+30** (friendly), and with T-01 the purse grew while farming.
  A second Dock command while already docked was also accepted (and reset the cap).
- Fix: coming back to the same port on the same in-game day is the same visit (standing cap and trend arrows
  kept); a visit elsewhere or a new day starts a new one. Dock while docked → `Nothing`. Saved: `LastDockPort`,
  `LastDockDay` (old saves: a save made alongside counts as that visit).
- Test: `ReDockingIsTheSameVisitForStanding`.

### T-03 — major — graded guns bought at the flat 80 gold — FIXED
- Where: `World.Port.cs` BuyCannon (flat `CannonPrice` 80) vs `PartDef.Price` cannons priced per mounted gun.
- Wrong: sell every gun (40 each), buy the grades at the 1-gun price, buy the guns back at 80 → they come at the new
  grade. Probe `cannons`: sloop 4 guns to 24-pdr 2,160 → 700; frigate 24 guns 23,760 → **1,950**. The same hole makes
  a hull change a discount: grades bought on a sloop carry over, and the big hull's empty slots filled at 80 each.
- Fix: a new gun is cast at the battery's grade: `World.CannonCost` = 80 + that grade's price for one gun on this
  hull (sloop grade 1: 115). Selling still pays 40. Port panel shows and checks the real price.
- Tests: `NewGunsCostTheBatterysGrade(sloop|frigate)`; self-test "a new gun is quoted at the battery's grade".

### T-04 — major — trading down forfeits the trade-in surplus — FIXED
- Where: `World.Progression.cs` `HullPrice` = max(0, cost − trade-in); BuyHull charged that and paid nothing back.
- Wrong: brig (trade-in 5,400) → cutter (800): the 4,600 difference vanished (probe `downgrade`). Trading down is a
  real move (only small hulls fit the Mangrove Maze).
- Fix: BuyHull pays out `HullRefund` = max(0, trade-in − cost). No loop: every swap still loses 40% of hull+parts.
- Test: `TradingDownPaysOutTheTradeInSurplus`. UI note: the hull button still reads "Buy · 0 gold after trade-in"
  for a trade-down (see R-06).

### T-05 — minor — officers without a berth kept their effect — FIXED
- Where: `World.Progression.cs` BuyHull called `ApplyOfficers()` before dropping officers beyond the new hull's slots.
- Wrong: frigate with a legendary lookout hired third → buy a sloop: the lookout leaves but `VisionMult` stays ×1.5
  until something else re-applies officers. Officers also left silently.
- Fix: drop first, then apply; notice `NOTICE_OFFICERS_ASHORE` (new en.csv row).
- Test: `OfficersWithoutABerthTakeTheirEffectAshore`.

### T-06 — minor — "cargo must fit" ignored the hidden hold — FIXED
- Where: `World.Progression.cs` BuyHull (`SlotsUsed > hull.Cargo`) and `HullFits` (a formula multiplied by 0).
- Wrong: the hidden hold (+15%) moves with the captain, but a hull swap with 26–29 slots aboard into a cutter
  (29 with it) was refused.
- Fix: `HullCapacity(hull)` = round(cargo × hidden-hold mult); `HullFits` is the one rule, used by BuyHull and the
  port panel.
- Test: `TheHiddenHoldCountsWhenCargoMustFit`.

### T-07 — minor — a tavern sold every bottle map, not one — FIXED
- Where: `World.Exploration.cs` `TavernMap` drew from maps the player did not hold, so each sale put the next map
  on the counter (GDD §19 M9 "one on offer per port"; en.csv "No bottle maps for sale today").
- Wrong: with the lean start's 200 gold, three maps (each worth ~700–1,500 gold of treasure) at the first tavern;
  all 8–14 for 60 each.
- Fix: the day's map is drawn from all undug sites by port and day; once held, "none for sale today". No new state.
- Test: `ATavernHasOneBottleMapADay`.

### T-08 — polish — unknown part index threw — FIXED
- Where: `World.Port.cs` BuyPart cast `(Part)cmd.Amount` unchecked → `IndexOutOfRangeException` on a bad command.
- Fix: refused with `Nothing`. Test: `AnUnknownPartIsRefusedNotAnException`.

### T-09 — polish — repairs charged whole hit points — FIXED
- Where: `World.Port.cs` Repair rounded the missing HP up before pricing: 0.5 HP missing cost 2 gold where the
  shipwright line quoted 1 (10.2 HP: 17 vs 16).
- Fix: charge ⌈HP mended × gold/HP⌉. Test: `RepairsChargeForTheHullTheyMend`.

### T-10 — minor — cost basis kept the price of goods eaten or fired — FIXED (my files) / R-01 for the rest
- Where: `Player.CostBasis` only shrank on a sale; midnight provisions (`World.Port.cs` Midnight), broadside shot
  and patching planks (`World.Combat.cs`) removed cargo without it. The logbook's "best single trade" came out low
  (8 provisions bought, 1 eaten: basis 59 for 7 units instead of 51.6).
- Fix: `Player.Remove` shrinks cargo, basis and lot together (used by Sell and Midnight); buying into an empty hold
  forgets any stale basis (covers shot and planks spent to zero). Test: `TheCostBasisFollowsTheGoodsLeftAboard`.

### T-11 — minor — overlapping dig rings hid the matched site — FIXED
- Where: `World.Exploration.cs` `DigSiteHere` returned the first undug site in range.
- Wrong: dig rings overlap on 118 of 1,000 maps (probe `digoverlap`). Inside the overlap the HUD said "no map" and F
  did nothing when the held map was for the second site; a dig started on the matched site could finish on the other.
- Fix: prefer the site she holds a matched map for. Test: `OverlappingDigRingsWorkTheMatchedSite`.

### T-12 — minor — coves sighted through the spyglass never counted — FIXED (tally) / R-02 (notice)
- Where: `World.cs` Tick's spyglass pass marks ports discovered without the vision pass's `CovesFound++`/notice, so
  Smuggler's Welcome and the logbook's coves undercounted.
- Fix (my file): `AchievementsTick` tallies discovered coves (max with the running count) once a second.
- Test: `ACoveSightedThroughTheSpyglassCounts`.

### T-14 — minor — a treasure's rare good ignored the hold — FIXED
- Where: `World.Exploration.cs` `FinishDig` drew one of the three rare goods at random, then gave only what fitted.
- Wrong: with a nearly full hold only emeralds (five to a slot) fit, but ambergris/relics were drawn 2 times in 3 and
  the treasure's goods were lost (GDD §19 M9: "1–3 units of a rare good that fits").
- Fix: draw among the rare goods that fit at least one unit (any if none fits). Same RNG calls, so determinism holds.
- Test: `TreasureGivesARareGoodThatFits` (6 seeds, 0.5 slot free → 1–2 emeralds each; failed on the baseline).

### T-15 — major — a foundering ship could dock by hand and buy her way out of the last stand — FIXED
- Where: `World.Port.cs` Dock accepted a foundering ship. The harbour rescue (GDD §19: once per port per voyage)
  runs automatically; at a port whose rescue was spent the HUD still offers "F: dock", F docked her, and any hull
  bought there (even a cheaper one, now with the refund) is a new `Ship` with no stand, full hull, dry bilge.
- Fix: Dock refuses (`PortClosed`) while foundering; the rescue clears the stand before it docks her, so it is
  unaffected.
- Test: `ASpentHarbourLetsHerSinkAtItsMouth` (spent port refuses; a fresh port still hauls her in).

### Self-review items (my own changes)
- The lot query `Player.LotAt` first clamped (mutated) the lot; the port panel calls `SellQuote` every refresh, so a
  UI call could change later sale results vs a replay. Made it a pure read (`QuotesLeaveTheWorldAlone`).
- `LotsAndVisitsSurviveASaveAndOldSavesLoad`: lots and the visit rule round-trip through `SaveJson`; a save with the
  five new fields removed (a pre-branch save) loads, docked, with no lots, and counts as the current visit.

### S-13 — minor — the lean-start guarantee counts hidden coves — PROPOSED (outside my files)
- Where: `MapGen.Validate` accepts a start route to any port within 600 m of sea, including an uncharted secret cove.
- Evidence: probe `start 1 1000`: with the real purse (200 gold) and free hold (13.3 slots), 979/1000 seeds have a
  profitable first trade to a charted port within 600 m (p10 81 gold, median 127, p90 227, never under 30); on
  **21/1000** the only port in range is a hidden cove (seeds 7, 25, 63, 69, 125, 150, 377, 387, 422, 449, 476, 487,
  501, 507, 513, 534, 624, 820, 848, 886, 991); the nearest charted market is then 601–1,025 m away (seed 7: cove
  570 m, Fort Ismouth 847 m). Including coves, 1000/1000 pass.
- Proposed fix (one condition in `MapGen.Validate`): `if (port == start || port.Secret || port.Produces.Contains(g)) continue;`
  **Side effect: re-rolls ~2% of maps including seed 7, the standard screenshot/gallery seed**, so it should land
  with a fresh BEFORE gallery. Not applied here for that reason and because MapGen is not mine.
- Test: `TheLeanStartHasAProfitableFirstTradeWithinHalfADay` (150 seeds): every seed has a profitable trade in
  range, the charted p10 profit ≥ 40, and at most 5% of seeds depend on a hidden cove (6/150 today). Tighten to 0
  once the MapGen fix lands.

## Reported (outside my files; evidence + proposed fix, not applied)

### R-01 — minor — shot and planks still leave their cost in the basis (`World.Combat.cs`)
- `World.Combat.cs:66` `Player.Cargo[(int)Good.Munitions] -= guns;` and `:222` `Player.Cargo[(int)Good.Timber]--;`
  bypass the new `Player.Remove`, so a partly spent stock keeps the price of what was fired/burnt (only the
  "best single trade" line is affected; spending to zero is already handled by T-10).
- Fix: `Player.Remove(Good.Munitions, guns);` and `Player.Remove(Good.Timber, 1);`.

### R-02 — polish — no "cove found" notice for a cove sighted through the spyglass (`World.cs`)
- `World.cs:133` marks the port discovered without the vision pass's `Stats.CovesFound++` / `NOTICE_COVE`. The tally is
  now right (T-12); the toast is still missing. Fix: `if (port.Secret) { Stats.CovesFound++; Notices.Enqueue("NOTICE_COVE"); }`
  in that block (T-12's max() keeps the count exact either way).

### R-03 — major — the lantern part widens daytime vision (`World.Weather.cs` + `ApplyOfficers`)
- `World.Progression.cs:183` folds `Ship.LanternMult` into `VisionMult`, which `World.Weather.cs:18` applies to every
  condition. Probe `lantern`: at noon, lantern grade 0 → 500 m, grade 5 → **750 m**; doused at night 150 → 225 m. The
  part says "+10% night radius and spyglass reach a grade" (en.csv, GDD §19 M8). Day vision +50% also inks the
  chart 50% wider (Cartographer) and spots hunters earlier.
- Fix (needs both files, so not applied): `ApplyOfficers`: `VisionMult = look >= 0 ? 1 + Officers.LookoutVision[look] : 1;`
  and in `VisionAt` use `double lit = 250 * Ship.LanternMult;` for the lit night radius (and its dusk blend). The
  spyglass already scales with the lantern separately.

### R-04 — polish — HUD says "digging 15 s" while salvaging a wreck inside an unmatched dig ring (`Hud.cs:311`)
- The label infers salvage from `WreckHere != null && DigSiteHere == null`; `ActionTick` salvages whenever no matched
  site is here. Fix: expose `World.Salvaging => Digging && digWreck` and key the label and countdown on it.

### R-05 — test flake — `AiTests.ThePopulatedSeaTradesAndKeepsItsBudget` wall-clock budget
- Asserts 240 s of populated sim runs in < 12 s of wall time. In full parallel runs with other agents loading the box
  (load average 12–20) it took 19–25 s and failed twice; alone it passes, and a standalone probe of the same world on
  this branch runs 240 s of sim in 0.7–1.6 s. Not caused by these changes; suggest a per-tick budget measured in a
  non-parallel collection or a looser bound.

### R-06 — minor (UI) — trading down reads "Buy · 0 gold after trade-in" (`PortScreen.cs:438`)
- With T-04 the shipwright now pays the surplus. Suggest a row `PORT_HULL_SWAP,"Trade down · the yard pays {0} gold"`
  and `b.Text = world.HullRefund(hull) > 0 ? Text.Get("PORT_HULL_SWAP", world.HullRefund(hull)) : …`.

### R-07 — polish (UI) — "Hire 5" enabled with under 50 gold (`PortScreen.cs:449`)
- Disabled only below one signing fee; pressing it with 10–49 gold returns "Not enough gold." Suggest
  `player.Gold < World.SigningFee * Math.Min(5, crewMax - crew)`.

### R-08 — minor (UI) — a cove rumor gives no feedback in the tavern (`PortScreen.cs:482`)
- 30% of rumors mark a "?" area; `World.LastRumorWasCove` is set but unused, so the tavern line says "The tavern talks
  for a price." after 15 gold was spent. Suggest a `PORT_RUMOR_COVE` line ("A hidden cove, they say: a ? is inked on
  your chart.") when `LastRumorWasCove`.

### R-09 — minor — a dig's 20% cosmetic roll can "unlock" a cosmetic already owned (`Main.cs:437`)
- `World.UnlockCosmetic` draws from cosmetics not in this voyage's `Player.Cosmetics`, which starts empty, so once the
  profile owns some, the roll toasts "unlocked" for one already owned and the roll is wasted (with 15 in all, most
  rolls are duds after a few voyages). Fix (game layer, one line in `StartVoyage`):
  `foreach (var c in profile.Cosmetics) w.Player.Cosmetics.Add(c);` — `Profile.RecordRun` already records only new
  ones, and the suspend save carries the set.

### R-10 — polish (text) — "1 units: buy for 5 · sell for 5" (`en.csv` `PORT_QTY`)
- The quantity line has no singular. Suggest "{0} × · buy for {1} · sell for {2}" or a singular row.

### R-11 — test flake — the self-test is timing-sensitive on a loaded machine (`Main.SelfTest.cs`)
- Checks that wait a fixed number of frames against real-time systems fail intermittently when the box is loaded
  (load average 9–17 from other agents): "the spyglass sees 700 m along the cone" (the camera is still easing after the
  teleport, so the cursor's world point drifts out of the 20° cone), "the first-voyage hint shows" (60 frames at a
  few fps is > 14 s of hint time, so `first_sail` has already retired and `trade`/`hostile` shows), "a broadside booms"
  (another one-shot lands last). Same failures on the **baseline** in alternating runs: base 105/106 ok (hint), base
  105/106 (spyglass), this branch 105/106 (spyglass + hint), 106/107 (broadside); logs `scratch/logs/alt-*.log`. When
  the box is quieter both pass (baseline 106/106 twice; this branch 107/107, `scratch/logs/selftest-3.log`). Suggest
  waiting on conditions (camera settled, `atSea` reached) rather than frame counts. Also: run the self-test with a
  fresh `VRUN_USERDATA` per run.

## Proposals (design/tuning; Nolan's call, with numbers)

### P-01 — "better prices" wider than the spread
- Friendly standing (±10%) and the quartermaster (±3/6/10%) apply to both sides of a 5% spread. T-01 stops the
  same-port loop; what remains is legitimate-looking but price-difference-free profit: carrying one good back and
  forth between two friendly ports with similar prices pays +16%/leg (friendly) to +42%/leg (friendly + legendary
  QM), and selling a hold at a friendly port then buying it back nets +14.5% (up to +34%) once per port visit.
  Neither beats a real producer→consumer route (+190% / +254%), so not urgent. If "buy low, sell high" should be the
  only source of profit: give the bonus as a share of the profit on a sale (bonus × max(0, revenue − cost basis),
  no buy discount), which can never pay without a real price difference.

### P-02 — four goods have no consuming region (`Regions.cs`)
- Hemp rope, pitch & tar, molasses and munitions appear in no region's consume list, so `MapGen` gives each its 3
  consumers at random ports anywhere. Natural homes: molasses → Trade Isles (rum is distilled there), hemp rope and
  pitch & tar → Storm Reach / Deep (shipyards), munitions → Storm Reach / Crown colonies. Data only; changes maps.

### P-03 — the quartermaster's provisions bonus vanishes under ⌈crew/4⌉ for small crews
- `DailyProvisions` = ⌈crew/4 ÷ (1 + bonus)⌉: with 4 hands every tier still eats 1/day; with 5 hands the green tier
  saves nothing (2 → 2). A fractional carry (eat 0.87/day) would honour "+15% longer" but needs one saved double.

### P-04 — wages follow the station split at the stroke of midnight
- `DailyWages` prices the current order: guns 3, sails 2, carpenters 4, pumps 2. Ordering every hand to the pumps
  (custom 0/0/0) just before midnight cuts a 10-hand sloop's bill 26 → 20 (−23%) and a 60-hand frigate's 152 → 120
  (−21%). Suggest pricing the hands the ship needs (the Balanced split) whatever the order.

### P-05 — a wreck trades in at the price of a sound hull
- `TradeInValue` ignores damage: a 1-HP brig trades in at 5,400 like a new one, so a captain trading down never
  repairs first. Suggest deducting the repair bill (missing HP × gold/HP).

### P-06 — standing decay makes pirates peaceful on day 4 (two decided rules collide)
- Brethren start at −25 "hostile from day one"; all standings decay toward 0 by 2/day (`World.Port.cs` Midnight), and
  `PlayerHostileTo` is rep ≤ −20, so after three midnights (−19) Brethren raiders no longer attack on sight
  (director hunters stay hostile). If pirates should stay hostile to a merchant: decay the Brethren toward −25
  (their starting stance) instead of 0.

### Balance check of this branch (autopilot, Rough, 120 voyages, seeds 1000–1119, cap 30 days)
- Baseline: days p10 2.9 / p25 8.0 / p50 14.0 / p75 28.4 / p90 30.0, mean 16.1, alive 27, grew past 200 gold 21,
  bigger hull 12. This branch: 2.8 / 7.8 / 14.0 / 26.6 / 30.0, mean 15.7, alive 26, grew 18, bigger hull 8. Same
  causes (water 52 both). No material shift (the autopilot never used the closed exploits); outputs in
  `scratch/bal-out/`.

## Changes outside my files (strictly needed by the fixes above)
- `src/Sim/World.Save.cs`: saves `LotPort/LotUnits/LotGold` and `LastDockPort/LastDockDay`. **Saved state touched,
  additively**: old suspend saves load (missing arrays → no lots; missing last dock → a save made alongside counts
  as the current visit). No field removed or renamed. Profile untouched.
- `game/PortScreen.cs` (6 lines): market rows and the detail pane show the player's prices (standing/quartermaster,
  and the buy-back cap on the quantity quote) via `BuyQuote`/`SellQuote`; the buy-cannon button shows and checks
  `CannonCost`; hull rows use `HullFits`; read-only `CannonOffer` for the self-test.
- `game/Main.SelfTest.cs`: +1 check ("a new gun is quoted at the battery's grade").
- `assets/text/en.csv`: +1 row `NOTICE_OFFICERS_ASHORE`.

## Tests run
- `dotnet test tests/Sim.Tests -- xUnit.ParallelizeTestCollections=false`: **141/141** (118 baseline + 23 new in
  `AuditTradeTests`), 3 m 35 s under load average ~27. The default parallel run fails only
  `AiTests.ThePopulatedSeaTradesAndKeepsItsBudget` on its 12 s wall-clock budget while other agents load the box
  (R-05); it passes alone.
- Self-test (`vrun.sh … -- --selftest`, fresh `VRUN_USERDATA`): `SELFTEST PASS`, **107 `ok`** (baseline 106 + 1 new
  check), twice in a row on the final commit (`scratch/logs/final-selftest-{1,2}.log`, load average 21–25). Under
  heavy load it can flake exactly like the baseline (R-11).
- Port screens re-shot after the UI edits (`scratch/shots-after/port-{market,shipwright,tavern}.png`): layout
  unchanged; at neutral standing the numbers match the baseline (multipliers 1.0).
