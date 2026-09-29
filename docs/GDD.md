# Last Tide — Game Design Document

*Working title. Status: v0.1 draft, 2026-09-23. Written from Nolan's brainstorm answers. Anything marked **(proposal)** fills a gap the brainstorm left open and needs a yes or no. The decisions log (§19) records every answer.*

---

## 1. Pitch & pillars

**Pitch.** You captain a small merchant sloop in a fictional golden-age Caribbean. Read the wind, trim your sails, buy low and sell high across a large archipelago that is new every run. Trade broadsides with pirates and navy hunters, and upgrade from a sloop to a man-o'-war. Every day the sea gets meaner: more hunters, worse storms, and monsters waking across nine regions. Sooner or later you go down. Your score is how many days you lasted.

**Inspiration.** The sailing part of Poptropica's Skullduggery Island, and specifically the four things Nolan loved about it: the trade-route grind, free sailing, ship upgrades and pirate fights. The run structure and difficulty curve come from Risk of Rain. **Nothing is copied:** no Poptropica names, characters, art, music or UI. Everything is original so it can ship on Steam.

**Pillars**

1. **The wind is the game.** Sailing takes skill: trim, tacking, and fighting with the wind at your back. It is the one system every other system runs through.
2. **The sea gets meaner every day.** Pressure rises with time, Risk-of-Rain style. Gold is a tool; days survived is the score.
3. **Buy low, sail far, sell high.** A deep supply-and-demand economy with 27 goods and 35–40 ports pays for everything else.
4. **The chart draws itself.** The world is an ink-and-parchment storybook sea chart, and exploring it literally inks it in.

---

## 2. Core loop

```
 cast off ──► read the wind, trim sails, pick a route ──► dock: sell high, buy low
    ▲                                                            │
    │                                                            ▼
 repair, hire, upgrade ◄── gold ◄── cargo, flotsam, treasure ◄── fight, flee or explore on the way
                                                                 │
   every in-game day: more hunters, worse storms, monsters wake ─┘
                                                                 ▼
                                   hull gives out ──► last stand ──► sunk ──► logbook: "DAY 17"
```

- **Minute to minute:** trim and tack, line up broadsides, ram a crippled pirate, dodge a storm cell, get the carpenters patching before she floods.
- **Day to day:** plan routes from your price ledger, keep the crew fed and paid, decide whether to upgrade now or keep the cash, and watch your faction standings.
- **Run to run:** beat your personal best on each difficulty, learn the regions and monsters, and unlock cosmetics.

---

## 3. Run structure & score

| Aspect | Rule |
|---|---|
| Run | One procedurally generated archipelago per run, with no seeds or sharing. A good run lasts 15–20 in-game days, about 30–40 minutes of sailing. |
| Day length | **1 in-game day = 2 real minutes** (1 in-game hour = 5 s). |
| Clock in port | The world clock **freezes while docked.** Because score is days survived, sitting in port must not farm days, and menus shouldn't cost reaction time. Days only accrue at sea. |
| Score | **Days survived**, shown as "Day 17, afternoon watch" and stored to 0.01 day. Gold is only a tool. There is no banking and no net-worth score. |
| End | Sinking ends the run. There are no extra lives. |
| Personal best | One best per difficulty preset, stored locally. No leaderboards or seeds. |
| Recap | A **logbook page** showing days survived, cause of sinking, gold earned, ships sunk, ports visited, leagues sailed, best single trade, and treasures dug. |
| Meta | **Cosmetics only** (§14). Every run starts equal. |
| Saves | **Suspend save.** Esc → *Save & Quit* writes the run, and resuming deletes the save. It also writes the suspend save automatically on every dock so a crash can't eat a run. |

**Start of a run:** pick a difficulty preset and a cosmetic loadout, and name your ship. You spawn at a random free port in the Trade Isles with a sloop, **200 gold, 4 crew** (lean start: the first trade matters), and **(proposal)** 6 provisions, 12 munitions and 3 timber. The chart shows the home region's coastlines and port names, but no prices. The start port is guaranteed a profitable trade within half a day's sail. **She opens alongside the home quay** on the port ledger (§19 "Start in port"); the clock does not run until she casts off.

---

## 4. Sailing

Top-down view with **WASD** controls. Sail trim is the core skill.

### Controls (keyboard + mouse only; no gamepad by decision)

| Input | Action |
|---|---|
| W / S | Raise / lower sail one step: **Furled → ⅓ → ⅔ → Full** |
| A / D | Rudder port / starboard |
| Q / E | **Hold** to show the port / starboard firing arc; **release** to fire that broadside |
| F | Context action: dock (inside a harbor ring), drop anchor and dig (inside a dig ring) |
| Hold RMB | Spyglass: extends a vision cone toward the cursor |
| L | Lantern on/off (night stealth, §9) |
| C | Crew panel: move hands between stations with − / + (the sim waits while it is open) |
| M | Chart |
| Wheel | Zoom |
| Esc | Pause menu (pausing is allowed; single-player) |

### Wind model

- **No prevailing wind.** The wind wanders freely: its direction drifts over hours and **(proposal)** varies gently across the map (a slow-moving noise field), so no direction is consistently easier. Local gusts ride on top, and storms override it. Reading today's wind decides which route is fast.
- **Point-of-sail speed curve (polar), as a fraction of hull top speed (proposal, tune in playtest):**

| Angle off the wind | Name | Speed |
|---|---|---|
| < hull's minimum pointing angle | In irons | drifts, loses way |
| min–60° | Close-hauled | 0.50 |
| 90° | Beam reach | 1.00 |
| 135° | Broad reach | 0.90 |
| 180° | Running | 0.75 (square rigs 0.90) |

- **Sail level** multiplies speed (⅓ = 0.4, ⅔ = 0.75, Full = 1.0). **Full sail turns slower** (turn rate ×0.6 at Full, ×1.0 at ⅓). Rudder torque scales with speed, so a stopped ship can't turn.
- **Tacking:** you can't sail straight upwind; you zig-zag. Riggers (§7) make sail changes and tacks faster.
- **Keel physics (proposal):** low forward drag and high sideways drag, so the ship carves rather than skids. A custom 2D rigid body handles this (§17), not Godot's built-in physics.
- **Storm penalty:** Full sail in a storm risks **torn sails** (−30% top speed until repaired in port), so you have to reef down.

### Camera & vision

- The camera follows the ship with a small lead in its heading. The wheel zooms between close combat view and wide sailing view.
- **Vision radius (decided as drafted):** 500 m by day, 250 m at night with the lantern lit, 150 m with it doused, and 180 m in fog. The spyglass extends vision to ~900 m in a 20° cone.

---

## 5. World & map generation

- **Scale (Nolan, 2026-09-27: "much bigger", 3× each way; 2026-09-28: islands farther apart, 1.4× again):** a **25.2 × 18.9 km chart** (v6's 18 × 13.5 km stretched 1.4× each way, `Map.Stretch`), 140–152 ports including 10–14 secret coves, some 900–1,200 islands of the same sizes as before with 1.4× wider channels between them. Every map has **all 12 regions.** Crossing the chart edge to edge takes about **15 in-game days** at a good point of sail. Neighbouring ports are **½–1 day** apart. **There is no edge:** the sea runs on past the chart (see "Beyond the chart" below).
- **Regions: all 12 in every run** (the nine below plus three added with the bigger chart). Each has its own port density, goods, weather table, landforms, and a monster or hazard (sizes and weather are proposals):

| Region | Feel | Monster | Weather |
|---|---|---|---|
| Trade Isles | Start region: busy lanes, many free ports | — | Fair, occasional squall |
| The Shoals | Turquoise shallows, many small islands, tight channels | **Reef Serpent** | Fair, doldrums patches |
| The Deep | Open ocean, few ports, fast crossings | **Kraken** | Strong wind, big swells |
| Fog Banks | Grey, slow, secret coves more likely | **Ghost Ship** | Fog most of the time |
| Storm Reach | Rich ports (luxuries), dangerous water | — | Frequent storms |
| Mangrove Maze | Tangled channels **only small hulls fit (Sloop, Cutter, Schooner, Xebec, Brigantine)**; secret coves hide here | **Giant Crocodile** | Humid, dawn fog |
| Volcanic Isles | Ash clouds cut vision; **eruptions hurl rocks** (telegraphed impact zones); cheap iron and munitions | — (eruptions) | Ash fall, hot calm |
| Sargasso Weed Sea | **Weed slows every ship** (−40% speed); derelict wrecks full of salvage | **Weed-Kraken** | Doldrums common |
| Siren's Ruins | Sunken temple ruins among rocks; treasure-dense (most dig sites) | **Siren** | Fair, eerie calm |
| Ice Reach | Fjords and skerries under snow; **drift ice**: floes that circle slowly and hole a hull met faster than a crawl | — (drift ice) | Fog, dawn fog, squalls |
| Maelstrom Straits | Long drowned ridges with tide races between: **whirlpools** turn in the open reaches, well clear of land, and one great maelstrom | — (whirlpools) | Strong wind, frequent squalls |
| Corsair Keys | Sand cays, barrier islands and lagoons; mostly Brethren havens (8% Crown, 30% free), coves likely, and the director's raiders press **1.8×** harder here | — (pirates) | Fair, doldrums patches |

- **Generation steps:**
  1. Place the 12 region seeds on a jittered 4 × 3 grid and split the sea into regions with a noise-warped Voronoi diagram.
  2. **Build the islands from real-world landforms** (Nolan, 2026-09-27: "not all the same size and shape … placement not completely random … lagoons, bays, etc."). Each region has a landform table (`RegionLayout`): **great islands** kilometres across with coves or fjord-like rias, long peninsulas and offshore islets; **high islands**; **groups** of mixed sizes round a larger one; **hotspot chains** that shrink along a gentle arc, sometimes ending in an atoll; **atolls** (reef islets round a lagoon, cut by navigable passes); **almost-atolls** (a high island inside a reef-ringed lagoon); a **caldera** (a crescent round a flooded crater with a new cone in it); **barrier islands** across a sound; **drowned ridges** (long parallel islands); a **delta** cut into a maze by winding creeks (the Mangrove Maze); **cays** on a shallow bank; and lone **stacks**. Each region builds its **signature** landform at its heart first, then fills round **island groups** (a few centres per region, so land clusters and wide lanes of open water run between), sizes leaning small with a few big ones, until it has its share of land. The whole archipelago shares a **tectonic trend** that ridges, chains and long islands line up with, bent a little region by region. Coastlines are traced from a noise-roughened shape field (`Coastlines`): channels under ~40 m close up, slivers under ~20 m go, enclosed lakes fill, so every island is a simple polygon and every lagoon opens to the sea.
  3. Place ports on coasts, **preferring sheltered water** (bays, sounds, lagoons) the way real harbours grew; a big island can hold several. Harbours, dig rings and wrecks lie only in open sea every ship can reach. Faction mix per region (default ~44% Crown colonies, ~39% free ports, the rest pirate havens; the Corsair Keys mostly havens), plus 10–14 secret coves weighted toward the Mangrove Maze, Fog Banks and Corsair Keys.
  4. Assign goods. Each port **produces 2–4 goods and consumes 3–5**, drawn from its region's table. Every good gets ≥2 producers and ≥3 consumers. Luxuries are not sold within 4.2 km of where they are made.
  5. Validate: every port reachable by sea, every harbour outside the Mangrove Maze reachable by a big hull without crossing it, and the start port has a profitable route within 840 m of sea (about ½ day). Otherwise re-roll the ports (the coastlines are kept), and after six failures redraw the sea.
  6. Scatter 26–40 **treasure sites** on islands, weighted toward Siren's Ruins (§10). The Sargasso gets 9–15 derelict wrecks to salvage. The Maelstrom Straits get 6–10 whirlpools in open water (every rim 120 m or more from any shore, 300 m of sea between any two) and one great maelstrom.
- **Beyond the chart (Nolan, 2026-09-27):** "There should be no edge of the map; the edge should just be an increasing amount of sea monsters the further out you go, and whirlpools." Past the chart's rectangle the sea goes on. Nothing out there can be charted (the fog shows only her live sight), there are no ports or islands, and the water is colder and darker the further she goes. **Beasts:** beyond the edge every beast of the open ocean hunts her (kraken 50%, reef serpent 30%, the ghost ship in fog or dark 20%), none gives up at a region border, and every 600 m further out the rolls come faster and land more often (at ~2 km a new one rises within seconds of the last). **Whirlpools:** none within 300 m of the edge, then more and bigger with distance until, some 3 km out, they crowd every 420 m cell and their pulls overlap: the sea's own end to the world. A whirlpool drags anything in its reach round clockwise and in toward its eye (an idle ship is drawn to the core in about a minute; one under sail heading out escapes), turns her with the swirl, and in the core grinds 3 HP/s and opens a leak every 4 s ("Dragged down by a whirlpool"). The sea letters HERE BE MONSTERS as she crosses out, and the HUD names the water "Uncharted waters". No other ship follows her past the edge and the director sends no hunters there: out there the sea is the only enemy.
- **The chart (M).** Unexplored sea is blank parchment. Whatever passes through your vision radius gets inked in permanently for the run. Ports show a faction mark once seen. Hovering a port shows your **ledger**: last-seen prices with their age ("seen Day 4"). **(proposal)** You can drop ink pins with a short note.

---

## 6. Economy

**Model: supply and demand.** No random market events. Contraband exists only as harbour-office work (below): no good is banned from any market. *(Round 1 left contraband out; Nolan added smuggling runs on 2026-09-27.)*

### Price model (proposal)

For each port *p* and good *g*:

- Stock **S**, target stock **S\***, base price **B**, and a role multiplier *m* (producer 0.6, neutral 1.0, consumer 1.5).
- **Price = B × m × (S\* / S)^0.5**, clamped to **[0.35B, 3B]**. **Soft elasticity (decided):** S\* is 100–200 units depending on port size and role, so selling 10 units drops the price only about 5%. You can mostly sell a hold in one place, and the challenge is the sailing.
- Buying removes stock and selling adds it, so **prices move while you trade.** The market shows the marginal unit price and the total for the quantity selected. Dumping a hold into one port crashes the price there.
- Stock drifts back toward S\* every day (**15%/day**) plus production/consumption noise. AI merchant ships also move stock between ports (§8), so the ledger goes stale.
- **No rumours (Nolan, 2026-09-28):** prices are only learned in port. Your purser records every price you see there, dated; a **merchant hailed at sea** tells you the prices at the ports she last traded in, dated her visit; the **harbour office** posts which nearby ports are short of what they want, in words, never a price (§19 "News without rumours").

### Goods (27 traded, decided; base prices as drafted; plus her own catch)

| Group | Goods (base price) |
|---|---|
| Consumables (you use these too) | **Provisions** 8, **Munitions** 12, **Timber** 12 |
| Ship stores | Hemp rope 15, Sailcloth 20, Pitch & tar 14, Iron 25 |
| Colonial produce | Sugar 18, Molasses 14, Tobacco 30, Cotton 22, Coffee 35, Cocoa 40, Indigo 55, Mahogany 45 |
| Manufactured | Rum 28, Cloth 32, Tools 40, Muskets 70, Wine 36 |
| Luxury | Spices 80, Silk 110, Porcelain 95, Pearls 150 |
| Rare (secret coves and treasure only) | Ambergris 220, Emeralds 260, Relics 300 |
| Her own catch (ports buy it, none sells it) | **Fish** 4 (4 a slot; see §10 "Fishing") |

- **Cargo: bulk matters (decided).** Timber and mahogany take **2 slots** per unit, and pearls, emeralds and spices pack **5 units per slot**. Everything else is 1 unit per slot. Small ships favor luxuries.
- **Upkeep, charged at each in-game midnight (decided as drafted):** wages by station (gun crew 3, riggers 2, carpenters 4 gold/day) and **1 provision per 4 crew per day.** If you can't pay, crew desert at the next dock. If provisions run out, you lose 1 crew per day.

### Harbour office: money without capital (Nolan, 2026-09-27)

Trading needs gold to buy cargo. The harbour office pays a broke captain too, so the lean start stays tight without stalling.

- **Delivery contracts.** Each port's board has 2 offers a day (3 at a large port). You can hold up to 3 contracts at once. A contract names a port 490 m–3.9 km away by sea and a deadline in sea days (the clock is frozen in port). You're paid on arrival, and signing marks the destination on your chart.
  - **Freight:** sealed crates that fill 20–45% of your hold (3–30 slots). Pays 3 + 10/km per crate, 20% in advance. Deadline: 1 day + 2× the passage.
  - **Dispatches:** letters that take no hold space. Pays 20 + 30/km, 20% in advance. Deadline: ½ day + 1.3× the passage (a cutter's trade).
  - Colonies and free ports never ship to a pirate haven. A haven ships freight to havens and free ports only.
  - **Late or given up:** the advance goes back (as much as your purse holds), the issuing faction takes −6 standing, and the crates are forfeit. Delivered: +2 with the issuer.
  - Pay rises with the Threat: ×(1 + 0.15 × (Threat − 1)).
- **Survey fee.** The first time you dock at any port except your home port and the coves, the harbour master buys your soundings: 15 + 12 gold per km from your home port, up to 75.
- **Crown bounties.** Every Brethren ship you sink (raider, hunter or privateer) earns 50 + 3% of its hull's cost, × the enemy scale (§11). The bounty is paid when you dock at any Crown port.
- **Smuggling.** Pirate havens (their second and third board slots) and secret coves (1–2 slots) offer contraband runs to Crown and free ports. They fill 10–25% of your hold (2–12 slots), pay 8 + 24/km per crate on delivery with nothing in advance, and give +2 with the Brethren when delivered.
  - **Customs** search you on docking at a Crown port (25%) or a free port (10%).
  - A **Crown patrol** that comes within 120 m searches you once (35%). A hostile patrol fights instead.
  - The black market's hidden hold halves every search chance.
  - **Caught:** the contraband is seized, you're fined half its fee, and the searching faction takes −15 standing.

---

## 7. Ships, upgrades & crew

### Hulls (14, decided; bought at shipwrights; trade-in = 60% of hull + parts value)

Prices are decided as drafted. Stats are v1 values, tuned in playtest. Speed and turn are relative to the sloop. "Point" is the closest angle to the wind the hull can still sail.

| Hull | Cost | Hull HP | Cargo | Crew max | Guns/side | Top speed | Turn | Point | Rig | Niche |
|---|---|---|---|---|---|---|---|---|---|---|
| Sloop | start | 100 | 20 | 10 | 2 | 1.00 | 1.00 | 40° | fore-and-aft | all-rounder |
| Cutter | 800 | 110 | 25 | 12 | 2 | 1.10 | 1.10 | 38° | fore-and-aft | courier, escapes |
| Schooner | 1,500 | 140 | 40 | 16 | 3 | 1.05 | 0.90 | 40° | fore-and-aft | all-rounder |
| Xebec | 3,000 | 150 | 30 | 30 | 4 | 1.15 | 1.00 | 45° | lateen | raider (fastest hull) |
| Brigantine | 4,000 | 200 | 70 | 24 | 5 | 0.95 | 0.75 | 50° | mixed | all-rounder |
| Fluyt | 6,000 | 220 | 150 | 20 | 2 | 0.85 | 0.60 | 55° | square | hauler |
| Brig | 9,000 | 280 | 100 | 36 | 7 | 0.90 | 0.65 | 55° | square | all-rounder |
| Barque | 11,000 | 260 | 180 | 30 | 5 | 0.90 | 0.60 | 50° | mixed | long-haul trader |
| Corvette | 14,000 | 300 | 80 | 45 | 9 | 1.10 | 0.70 | 55° | square | hunter |
| Frigate | 20,000 | 400 | 140 | 60 | 12 | 1.00 | 0.55 | 60° | square | warship |
| Indiaman | 28,000 | 450 | 300 | 70 | 10 | 0.80 | 0.45 | 65° | square | armed mega-hauler |
| Heavy frigate | 32,000 | 520 | 160 | 80 | 15 | 0.95 | 0.50 | 60° | square | fast heavy warship |
| Galleon | 40,000 | 600 | 260 | 90 | 16 | 0.80 | 0.40 | 65° | square | treasure ship |
| Man-o'-war | 60,000 | 900 | 120 | 140 | 24 | 0.75 | 0.35 | 65° | square | floating fortress |

Fore-and-aft rigs point higher into the wind, and square rigs run faster downwind. Bigger isn't strictly better. Gun slots are a maximum, and each cannon is bought separately.

### Part upgrades (8 parts × **5 grades** each, decided; price scales with hull size; per-grade values are v1)

| Part | Effect |
|---|---|
| Sails | +top speed, faster sail changes |
| Rigging | −3° pointing angle per grade (−15° at grade 5; better upwind) |
| Hull planking | +max HP, −1.5% speed per grade |
| Copper sheathing | +speed, fewer leaks per hit |
| Cannons (base 4-pdr → 6 → 9 → 12 → 18 → 24-pdr) | +damage and range, slower reload |
| Ram prow | +ram damage, −self-damage on bow impacts |
| Hold | +cargo |
| Lantern / Spyglass | +night radius / +spyglass range |

**Carry-over when you buy a new hull (decided: some transfer).** **Cannons** (grade, plus as many guns as the new hull has slots; extras are sold) and the **lantern/spyglass** move to the new hull. Hull-specific parts (**sails, rigging, planking, copper, hold**) stay with the old hull and count toward its trade-in value. **The ram prow stays** (decided).

### Crew (roles)

Crew are hands you **assign to stations**. **(proposal)** Moving between stations takes ~3 s.

| Station | Needed | Effect when short |
|---|---|---|
| **Guns** | 1 per cannon | Reload scales with manning (a half-crewed side reloads at half speed) |
| **Sails** (riggers) | Per hull: sloop 2 … man-o'-war 30 | Slower sail changes, tacks and turns, and lower top speed |
| **Repair** (carpenters) | Up to 1 per 100 max HP | Leaks stay open longer and there's no hull patching |
| **Spare hands** | Anyone left over | Nothing: they wait for a station to need them |

- **Crew by hand (Nolan, 2026-09-28: no order keys):** the crew starts on a balanced split (riggers, then gunners, then carpenters). The crew panel (C) moves hands one at a time with − / +; the sim waits while it is open. Hands hired onto a crew set by hand go where hands are still wanted (sails, guns, repair), and only the rest stand spare. AI captains keep their orders.
- **Casualties:** hits kill crew at the station facing the hit. You hire replacements at taverns (signing fee + wages).

### Officers (decided: Lookout, Marines, Quartermaster)

One slot per officer type. You hire them at taverns in three tiers: **green, seasoned, legendary**. Bigger hulls have more officer slots, so you can't carry all three early. Slots (decided): 1 for Sloop through Xebec, 2 for Brigantine through Corvette, 3 for Frigate and up. Officers draw higher wages and leave if unpaid. They don't die from hits.

| Officer | Green / Seasoned / Legendary (v1 values, tuned in playtest) |
|---|---|
| **Lookout** | Vision +15 / 30 / 50%. Edge-of-screen warnings for hunters and monsters 100 / 200 / 300 m beyond vision |
| **Marines** | An automatic musket volley at enemy ships within 120 m every 6 s, killing 1 / 2 / 3 crew (slows their reload) |
| **Quartermaster** | Prices 3 / 6 / 10% better, wages −10 / 20 / 30%, provisions last +15 / 30 / 50% |

---

## 8. Combat, damage & factions

### Broadsides

- **Q = port (left), E = starboard (right).** Hold to show the ink firing arc (perpendicular ±25°, out to cannon range). Release to fire every loaded gun on that side in a quick ripple (0.05 s apart) for spread. Each side reloads independently.
- **Range (proposal):** from 180 m (4-pdr) up to 320 m (24-pdr), in even steps by grade. Balls fly flat and splash at max range.
- Each gun fired uses **1 munition**. If you run out, you have to ram or run.
- A ball that hits deals hull damage, may open a **leak**, and may kill a crew member.

### Ramming

- Damage = mass ratio × closing speed along the impact normal. **Bow-on hits:** ×2 to the target, ×0.5 to you. **Side hits** hurt you more. The ram prow upgrade improves both.
- Ramming a much larger ship at low speed is a bad idea.
- There's no boarding or capture (not picked).

### Flotsam

A sunk ship spills **barrels (cargo) and chests (gold)** that you collect by sailing through them. They drift and sink after ~60 s. That's what makes fighting pay, along with the Crown's bounty on pirates (§6).

### Damage, leaks & water (proposal numbers)

- **Hull HP** takes damage. Every **10% of max HP lost opens a leak.** Copper sheathing gives each hit a chance to not open one.
- **Water follows the hull** (decided, Nolan, 2026-09-27). The **flood line is 60% hull**: below it the water rises, above it she drains, at **1% a second for every 10% of hull away from the line** (40% hull: +2%/s; 90%: −3%/s). **Water 0–100%** slows speed and turning by up to 50%. There are no pumps.
- **Carpenters** plug one leak per ~8 s each; an open leak lets in no water of its own but stops the patching. With no open leaks they **patch hull at 1 HP/s using 1 timber per 10 HP, up to a 70% cap at sea.** Full repair costs gold at a shipwright.

### Last stand

- **Triggered by** hull 0 HP or water 100%.
- The ship **founders.** Guns go silent, speed is capped at 50%, and an ink hourglass starts: **35 s**.
- **Survive** by reaching the harbor ring of any port open to you, which docks you automatically with 1 HP and empties the water. If the last stand came from water only, the carpenters keep patching through it, and draining back below 80% also ends it.
- **Sink** when the hourglass runs out, and the run is over.

### Factions & reputation

| Faction | Ports | Ships | Rival |
|---|---|---|---|
| **The Crown** (navy) | Colonies with forts | Cutters, frigates, squadrons | Brethren |
| **Free Traders** (merchants) | Free ports | Merchantmen (flee, answering with a few guns as they run) | — (lean Crown) |
| **The Brethren** (pirates) | Havens | Sloops, brigs, raiders | Crown |

- **Reputation −100…+100 per faction (thresholds decided as drafted):**
  - **≥ +20** friendly: 10% better prices.
  - **−20…+20** neutral.
  - **≤ −20** hostile at sea: their ships attack on sight.
  - **≤ −50** their **ports close to you**, which removes those ports as last-stand refuges.
- **Shifts:** hitting a ship −10 to its faction. Sinking one −30 to its faction and +10 to its rival. Each trade at a port gives that faction +1, capped per visit. All reputations decay toward 0 by 2/day.
- **Starting standings (decided):** **Brethren −25** (pirates are hostile from day one because you're a merchant), **Crown 0, Free Traders 0.**
- **Harbor forts** keep hostile ships outside the harbor ring, so a friendly port is a real sanctuary.
- **AI ships (proposal):** merchants trade between ports, which moves real stock. They never start a fight, but a merchantman carries up to 4 guns (2 a side) and runs from an attacker in a fighting retreat: she yaws up to 50° off her escape course to bring a loaded broadside to bear, fires, and runs on while it reloads. Crown ships patrol near colonies, Brethren raiders prowl near havens and merchant lanes, and **hunters** are spawned by the director (§11). AI steering is wind-aware and tries to bring a broadside to bear.

---

## 9. Weather, day & night

- **Wind:** wanders freely with no prevailing direction, plus local gusts (§4).
- **Storms:** moving storm cells drawn as ink swirls on the chart. Inside one you get strong shifting wind, rain (vision −40%), swells that push the ship around, and torn-sail risk at Full sail. Frequency and strength rise with Threat (§11).
- **Fog banks:** vision drops to 180 m. The ghost ship lives here.
- **Doldrums:** patches of near-zero wind where you drift. They're deadly if something is chasing you.
- **Day/night: 80 s of day, 40 s of night** per 2-minute day, with a dawn and dusk tint.
  - **Night:** vision shrinks to the lantern radius, hunters close in more aggressively, and monsters are twice as active.
  - **Lantern (L):** lit, you see 250 m but can be spotted from far away. Doused, you see 150 m, but hunters only detect you inside ~200 m. That lets you slip past a blockade.

---

## 10. Exploration

### Fishing (Nolan, 2026-09-28)

"You should be able to fish anywhere but some places have more fish", "the fishing grounds should be very subtle", and "the fish should be more common near land".

- **Anywhere at sea**, under ⅓ sail or furled, the context key (F) puts out lines; F again hauls them in, and making more sail hauls them in by itself. The gun crews and the spare hands fish, at most 6 lines; the riggers and carpenters stay at their work. No hands free, no lines: the crew panel (C) frees some.
- **The catch** is ½ fish a minute a hand on an ordinary sea (one a hand a day) **(proposal)**, times the water's richness: each region has its run (Shoals 1.4, Ice Reach 1.3, Fog Banks and Corsair Keys 1.2, Mangrove and Maelstrom 1.1, Trade Isles and Siren's Ruins 1.0, Storm Reach 0.9, Deep 0.8, Volcanic 0.6, Sargasso 0.35, past the chart 0.7), **richer toward land** (1.8× at the water's edge, easing to 1× 500 m out), and here and there a **ground** holds 2–3.5× that at its heart. Grounds (70–150 m across, one at most per 600 m cell) settle somewhere new each midnight and **gather by the coasts**: a cell holds one on 65% of days by a shore, easing to 20% 500 m out. Halved while she makes more than 2.5 m/s; doubled at night with the lantern lit (and the hunters see the lantern too).
- **Subtle grounds:** nothing marks them on the chart or the HUD, and the prompt never says how rich the water is. Within ~400 m of her a ground shows only as faint rings where fish rise and, rarely, a small fish arcing out of the water. The catch is the tell.
- **Fish is food first:** at midnight the crew eats fish before provisions, then a third of what is left (rounded up) spoils: about three days' keeping. Every port buys fish (4 gold, ×1.5 where provisions are wanted, its price sinking as you sell); none sells it. The HUD's food reads "provisions+fish".
- **Something else on the line:** per fish, 1.2% a pearl (Shoals, Siren's Ruins), 0.8% ambergris (the Deep), 0.4% a bottle map; in the serpent's, kraken's, crocodile's or weed-kraken's own water, 1.2% the beast itself takes the line and the lines come in.

### Secret coves (3–5 per map)

- They aren't on the chart. You find one by sailing within vision (with a cartographer aboard).
- **Always open to you**, whatever your reputation. That makes them a lifeline once factions have closed their ports.
- They have odd prices, sell **rare goods**, and carry 1–2 unique black-market parts each for example *ghost-grey sails* (hunters detect you 30% closer) or a *smuggler's keel* (+5% pointing).

### Treasure digs

- **Bottle maps** float as flotsam or are sold in taverns. Each one is a **torn chart fragment: an island's coastline sketched in ink with an X.**
- **The puzzle:** match the sketch to a real island. Sailing within range of the right coast pins the X on your chart.
- **Digging:** enter the dig ring, drop to Furled, and press F. It takes **3 in-game hours (15 s)** and can be interrupted. The clock and the threats keep running.
- **Reward:** gold, rare goods, and a chance at a cosmetic unlock.

---

## 11. Difficulty & escalation

**Presets plus time scaling, as in Risk of Rain.**

| Preset | Threat growth *k* | Target "good run" |
|---|---|---|
| Calm Seas | 0.10 / day | Day 25+ |
| Rough Seas | 0.15 / day | Day 15–20 |
| Tempest | 0.22 / day | Day 10–12 |

- **Threat coefficient = 1 + k × days** (decided: k as drafted). It drives spawn budget, enemy tiers, enemy stats (+10% HP and damage per +1.0 of Threat), storm frequency and monster activity. Spawn volume and enemy strength both scale with it, so pressure rises roughly quadratically.
- **The Threat bar** is always on the HUD, like Risk of Rain's difficulty bar, and names the current tier:

  **Flat Calm → Light Airs → Freshening → Choppy → Squall → Gale → Storm → Maelstrom → LAST TIDE**

  The tiers begin at Threat 1.0 / 1.5 / 2.0 / 2.75 / 3.5 / 4.5 / 6 / 8 / 10. Reaching LAST TIDE is an achievement, and runs rarely go far past it.
- **The director (cards decided as drafted):** it earns spawn credits per second × Threat and spends them on **spawn cards** near you, just outside vision:

| Card | Cost | Unlocks at Threat |
|---|---|---|
| Brethren sloop | 10 | 1.0 |
| Brethren brig | 30 | 2.0 |
| Crown cutter (only if the Crown is hostile) | 15 | 1.0 |
| Crown frigate (only if the Crown is hostile) | 60 | 2.75 |
| Hunter pack (always hostile; privateers with a bounty on you) | 80 | 3.5 |
| Brethren galleon | 120 | 4.5 |

Monsters are handled separately (§12). Their chance per in-game hour depends on region × Threat, doubled at night.

---

## 12. Sea monsters (regional roster)

| Monster | Region | Behavior | How to beat or escape it |
|---|---|---|---|
| **Reef Serpent** | The Shoals | Surfaces beside you, bites at the waterline (opens 2 leaks), dives, and circles back | Hit it with a broadside while it's surfaced, or break into deep water where it won't follow |
| **Kraken** | The Deep | Tentacles grab the hull and pin your speed to 0 while they smash | Shoot the tentacles (each one hit frees a hold). Carpenters work overtime |
| **Ghost Ship** | Fog Banks | Only visible in fog and at night. Cannonballs pass through it except while its lanterns flare (brief windows) | Fire into the lantern flares, or sail out of the fog bank |

| **Giant Crocodile** | Mangrove Maze | Lurks in narrow channels and lunges from the bank. A bite **jams the rudder for ~5 s** and damages the hull | Shoot it when it surfaces, keep to mid-channel, or leave the mangroves |
| **Weed-Kraken** | Sargasso Weed Sea | Wraps the hull in weed and **drags you down**: speed pinned, water rising fast | Shoot the weed mass or let carpenters cut it; she cannot drain while it holds her |
| **Siren** | Siren's Ruins | Her song **pulls your rudder toward the rocks** while you're in earshot. You fight it with A/D | Shoot her rock perch to silence her, or sail out of earshot |

**Volcanic Isles** has no monster. Its danger is eruptions: telegraphed rock showers that hit whatever is inside the marked zone. The **Ice Reach** has drift ice, the **Maelstrom Straits** whirlpools, and the **Corsair Keys** the Brethren themselves (§5). **Beyond the chart** the kraken, the serpent and the ghost ship all hunt, more often the further out (§5 "Beyond the chart").

Monsters are drawn as sea-chart marginalia come to life ("here be monsters" illustrations) that tear off the edge of the parchment.

---

## 13. Ports (menu screens)

Docking opens a parchment panel. **The clock is frozen while docked (proposal, §3).** Tabs:

- **Market:** the ledger of all goods, with this port's buy/sell prices, a trend arrow since your last visit, your hold on the side, and quantity sliders showing marginal and total price.
- **Shipwright:** repair (gold per HP), buy cannons, part upgrades, and trade in your hull.
- **Tavern:** hire crew and officers, buy bottle maps.
- **Harbour office:** the day's contracts, the ones you hold (with Give up), Crown bounties owed, and what was paid or seized when you arrived (§6).
- **Cast off.**

The port header shows the owning faction, your standing, and a warning if you're close to being shut out.

---

## 14. Cosmetics & achievements

- **Cosmetics only:** flags, sail paint, figureheads, hull colors and wake-ink colors. They're chosen at run start and have no stats.
- **Unlocked by achievements and treasure finds.** They mirror Steam achievements (list decided):
  - *Fair Winds*: reach Day 10 on Calm Seas.
  - *Heavy Weather*: reach Day 15 on Rough Seas.
  - *Eye of the Storm*: reach Day 12 on Tempest.
  - *Serpent Slayer*: kill a Reef Serpent.
  - *Unkrakened*: break free from the Kraken.
  - *Lay the Ghost*: sink the Ghost Ship.
  - *Handbags*: kill the Giant Crocodile.
  - *Weeded Out*: escape the Weed-Kraken.
  - *Deaf Ears*: silence the Siren.
  - *Nine Seas*: enter all 9 regions in one run.
  - *Cartographer*: ink 80% of a chart in one run.
  - *Smuggler's Welcome*: find 3 secret coves in one run.
  - *X Marks*: dig 5 treasures in one run.
  - *Galleon Captain*: own a galleon.
  - *Ship of the Line*: own a man-o'-war.
  - *By a Hair*: dock during a last stand.
  - *Last Tide*: reach the LAST TIDE threat tier.

---

## 15. Art direction

**Treasure-map storybook: ink and parchment.**

- **The sea is parchment** with animated ink wave strokes (a shader). Wave density and speed follow wind strength, so the water itself shows the wind.
- **Ships** are top-down ink illustrations with watercolor washes. **Sails are a separate layer** with 4 states matching the sail level, and they swing with the wind. Cannon smoke is ink blots.
- **Islands** look like chart islands: hachure-shaded hills, dotted shallows and hand-lettered port names.
- **Fog of war is blank parchment**, and the chart inks itself in as you sail.
- **Ship card (Nolan, 2026-09-27):** hovering over any ship in sight rings her in red and shows a card: her name, faction and role (merchantman, patrol, raider, hunter, privateer) and hull type, how she stands toward you, her hull with a gauge, guns and hands, a merchant's cargo and destination, the Crown bounty on a pirate, and her distance, speed and whether she is in reach of your guns. Hovering your own ship shows your own card, with the deliveries in hand and their days.
- **UI:** compass rose with a wind arrow, sail level shown as small furled/set sails, a hull gauge beside a water-in-hold gauge, clock and day, the Threat bar, gold, a hold summary, and crew station counts. Everything is lettered in ink on paper.
- **Night** is an indigo wash over the parchment, and the lantern is a warm pool of light.
- **Asset pipeline: Codex `image_gen`** (Nolan's call; see `~/.claude/workspace/tools/codex.md` for the transparent-PNG flow). One fixed style prompt block is used for every asset. Ships are generated top-down at a single heading and rotated in-engine. Sail states, monsters, island stamps and UI ornaments are generated separately and cleaned up.

---

## 16. Audio

**Pure ambience. No music.**

- **Layers:**
  - Wind, with intensity following wind speed.
  - Waves.
  - Creaking timber, following speed and turn rate.
  - Rigging slap on sail changes.
  - Rain and thunder in storms.
  - Gulls near ports.
  - Cannon fire, splintering, water rushing in.
  - Monster cues.
- **No music anywhere** (decided). Ports and battles rely on ambience too: harbor bustle and bells in port, and drums of the beat-to-quarters call plus cannon fire in battle **(proposal)**.

---

## 17. Technical architecture

- **Godot 4.7.2 .NET, C# on net8.0.** It's installed user-local, and `~/.claude/workspace/tools/languages.md` has the working project shape.
- **Layout (proposal):**
  - `LastTide.csproj`: `<Project Sdk="Godot.NET.Sdk/4.7.2">`, with `<Compile Remove="src/**;tests/**" />`.
  - `src/Sim/Sim.csproj`: a plain C# class library with no Godot dependency. It holds world generation, economy, factions, the director, weather, ship physics, combat resolution, crew and the save state.
  - `game/`: Godot scenes and nodes for rendering, input, UI and audio. They read the sim and send it commands.
  - `tests/Sim.Tests`: xUnit, run headless with `dotnet test`.
  - `.gdignore` in `src/`, `tests/` and `docs/`.
- **Sim:** fixed timestep (30 Hz) with a seeded RNG per run. Seeds aren't exposed to players, but seeding makes suspend saves and tests reproducible. Ship physics is a custom 2D rigid body (polar thrust, keel drag, rudder torque, capsule collisions against island polygons and other ships).
- **Level of detail:** ships near the player run at full tick. Distant AI ships move on a coarse tick along lane paths (A\* over a coarse grid with a wind-aware cost), so traffic and stock flow stay real across the whole map.
- **Chart reveal:** a persistent reveal mask texture, painted as the vision circle moves and saved with the run.
- **Save:** JSON (System.Text.Json) of the sim state plus the reveal mask. Suspend-save semantics as in §3.
- **Steam:** achievements and cloud saves (no leaderboards). Default library: Steamworks.NET, confirmed at the Steam milestone.
- **Performance target:** 60 fps with ~40 ships active near the player and a full map of coarse-tick traffic.
- **Renderer:** GL Compatibility (default) for the widest Steam hardware reach. The ink shaders are simple 2D.

---

## 18. Roadmap: build everything, in order

Nolan chose to build the whole design at once, so this is the order of work, with each step verifiable before the next.

| # | Milestone | Done when |
|---|---|---|
| 0 | Project skeleton: csproj, Sim library, tests, headless build, scripted screenshots | `dotnet build` + `dotnet test` green, and the window opens |
| 1 | **Sailing feel:** wind, polar, trim, rudder, keel physics, camera, placeholder art | Nolan playtests and says it feels right. **Everything else waits on this.** |
| 2 | World gen, regions, ports, factions on the map, chart ink reveal | Maps validate (reachability, start-route guarantee) across 1,000 seeds in tests |
| 3 | Ports and economy: menus, 27 goods, supply and demand, ledger, upkeep, crew hiring | Sim test: a scripted trader profits and prices react and recover |
| 4 | Combat: broadsides, ramming, leaks, flooding, carpenters, last stand, flotsam | Scripted fight tests and a playtest |
| 5 | AI ships, reputation, port closures, director, Threat bar, presets | Threat curve plotted per preset in tests |
| 6 | Weather and day/night: veering wind, storms, fog, doldrums, lantern stealth | Playtest |
| 7 | Monsters: serpent, kraken, ghost ship | Each beatable and escapable in a scripted test |
| 8 | Progression: hulls, part upgrades, crew stations and orders | Balance pass |
| 9 | Exploration: secret coves, bottle maps, the matching puzzle, digs | Playtest |
| 10 | Run wrapper: title, preset select, logbook recap, personal bests, suspend save, cosmetics | Full-run playtest |
| 11 | Codex art pass, audio pass, Steam achievements and cloud saves, balance | Release-candidate playtest |

---

## 19. Decisions log (brainstorm, 2026-09-23)

| Question | Answer |
|---|---|
| Platform | Godot desktop |
| Core loop | Even mix of trading, combat and exploration |
| View / controls | Top-down, WASD + wind |
| Length | Roguelite runs |
| Art style | Treasure-map storybook (ink and parchment) |
| Combat | Broadside cannons + ramming |
| Run goal | Survive / score |
| Meta progression | Cosmetics only |
| Economy | Supply and demand |
| Hazards | Weather, navy and hunters, sea monsters |
| Escalation | Day-based |
| Ports | Menu screens |
| In-run growth | Bigger hulls, part upgrades, crew |
| Exploration | Secret ports, treasure digs |
| Death | Last stand. Sinking ends the run. |
| Score | Days survived (no banking) |
| Audio | Ambient sea |
| Language | C# |
| First build | Everything |
| Map size | Large (20+ ports) |
| Release | Steam |
| Goods | 20+ |
| Setting | Golden-age Caribbean (fictional, original) |
| Day length | ~2 minutes |
| Seeds / leaderboards | Neither |
| Difficulty | Presets + Risk-of-Rain-style time scaling |
| Crew | Roles |
| Sailing | Sail trim |
| Hostility | Factions + reputation |
| Input | Keyboard + mouse only |
| What Nolan loved | All four: upgrades, fights, free sailing, trade grind |
| Repairs | Carpenter at sea + shipwright + leaks and the flood line |
| Monsters | Regional roster |
| Title | Last Tide |
| Art source | Codex image_gen |
| Night | Day/night cycle |
| Quitting mid-run | Suspend save |
| **Round 2 (2026-09-23)** | |
| Clock in port | Freezes while docked |
| Wind | No prevailing wind; it wanders freely |
| Munitions | Consumable (1 per gun fired) |
| Starting kit | Lean: sloop, 200 gold, 4 crew |
| Vision tools | Spyglass (RMB) and lantern stealth (L) |
| Regions | All 9, every run: the original 5 + Mangrove Maze, Volcanic Isles, Sargasso Weed Sea, Siren's Ruins |
| Crew | More roles: Lookout, Marines, Quartermaster as tiered officers |
| Pirates | Hostile from day 1 |
| Autosave | Suspend save on every dock |
| Treasure digs | Match the coastline sketch |
| Black market | Yes, 1–2 unique parts per secret cove |
| Day/night | 80 s day / 40 s night |
| Presets | Calm Seas / Rough Seas / Tempest |
| Threat tiers | Keep Flat Calm … LAST TIDE |
| Music | None |
| Numbers | Nolan wants to review the tables |
| Goods | Keep the 27 and the drafted price spread |
| Elasticity | Soft (~5% drop per 10 units sold) |
| Cargo size | Bulk matters |
| Hulls | 14: the original 6 + Cutter, Xebec, Fluyt, Corvette, Barque, Indiaman, Heavy frigate, Man-o'-war |
| Hull prices | As drafted (800 → 60,000) |
| Upgrades | 8 parts × 5 grades |
| Carry-over | Some: cannons and lantern/spyglass transfer; hull parts don't |
| Achievements | Keep the list |
| Threat pace | As drafted (k 0.10 / 0.15 / 0.22) |
| Upkeep | As drafted |
| Reputation | Thresholds as drafted; start Crown 0, Traders 0, Brethren −25 |
| Director | Spawn cards as drafted |
| Crew hotkeys | Yes, 1–4 (removed 2026-09-28: the crew is set by hand on C) |
| Vision | As drafted |
| Officer slots | As drafted (1 / 2 / 3 by hull size) |
| New monster counters | Keep |
| Ram carry-over | Ram stays with the hull |

| **M1 build (2026-09-23)** | |
| Coordinates | Sim uses Godot's convention: metres, +X east, +Y south, angles clockwise from +X; compass = angle + 90°. No flips between sim and view. |
| Speed scale | Sloop top speed 12 m/s (23 kn; **13.2 m/s ≈ 26 kn since 2026-09-28**, see "Every ship 10% faster") at polar 1.0, full sail, 8 m/s "standard" wind; target speed scales with √(wind/8), clamped ×0.45–×1.3. 1 m = 4 px at zoom 1. |
| Thrust/drag model | Thrust is set equal to drag at the polar target speed, so equilibrium speed is exactly the §4 table; drag is quadratic (0.018/m) + linear (0.03/s), scaled by √(hull HP/100). 0→76% of top speed in ~4 s; 12→6 m/s coast in ~4 s. |
| Turning | Rudder authority ramps with headway (full from 35% of top speed), floor 0.5 with any sail set so a slow ship still swings; turning scrubs speed (0.25 × |ω|); full sail turns at ×0.6, ⅓ at ×1.0 (lerp between). Sloop 40°/s at ⅓ sail. A close-hauled tack takes ≈7 s helm-to-settled, ≈2.4 s through the eye. |
| Rudder | A/D swing the rudder at 2.5/s and it centres itself at 3/s when released (proposal for playtest). |
| Sail changes | One W/S step ≈ 2.5 s of crew work (0.16 sail-fraction/s); the sim tracks the canvas actually set. |
| In irons | Below the pointing angle the polar fades to 0 over 8°; head to wind with sail set, the ship drifts astern slowly. Leeway pushes the hull to leeward on a reach (≈0.3 m/s at full sail, beam wind). |
| Wind wander | Base direction and speed are Ornstein–Uhlenbeck processes (dir-rate σ 0.035 rad/s τ 10 s; speed mean 8 m/s σ 2.5 τ 30 s, clamped 3–14); a slow spatial noise field adds ±25° and ±25% across the map; patchy gusts add up to +45%. The sea shader draws the same gust field. |
| Camera | Follows the ship with a lead along her heading (4 s of speed, max 120 m), eased; wheel zoom ×1.2 steps between 0.3 and 2.5, default 1.25. |
| Window | 1600×900 fixed-size window (options menu later adds resolution/fullscreen). |
| Pause | Esc pauses (sim stops, inputs ignored); Q while paused quits. |
| M1 practice sea | Four placeholder blob islands around the start until world generation (M2). |
| Stores pack tight (M3) | Provisions take ¼ slot and munitions ⅒ slot (a barrel a slot); timber stays 2 slots. Reason: at 1 slot each the 6/12/3 start kit was 24 slots in a 20-slot sloop. Start kit is 6 provisions, 12 munitions, 2 timber = 6.7 slots. Sell price = 95% of the buy price at the same port. Market targets: consumers 100/150/200 by port size, producers ×1.5, untraded goods ×0.6, rare goods 15 outside coves. Stock drift 15%/day, noise 1%/h. Repair 1.5·√(HP/100) gold per point; cannon 80 gold (sells for 40); hand 10 gold signing fee; rumor 15 gold. Unpaid crew: 30% desert at the next dock. No provisions: one hand lost per night, never the last. |
| Combat numbers (M4) | 4-pdr: 180 m, 6 dmg, 12 s reload; each grade +28 m, +2 dmg, +2 s. Spread ±3°. Ball speed 110 m/s. Casualty chance 25% per hit. Ram: damage = 1.2 × closing speed × mass ratio; bow-on ×2 to the target and ×0.5 to the rammer; flank-to-flank ×1.3 both. Last stand glass = 35 s. Reputation: −10 to a faction the first time you hit one of its ships, −30 for sinking it, +10 to its rival; sinking a Free Trader also costs −10 Crown. Flotsam sinks after 60 s, collected within 12 m. The lean start ships one 4-pdr a side. |
| AI numbers (M5) | Director: 0.06 credits/s × Threat, checks every 10 s, at most 6 hunters alive, spawns 150–400 m beyond vision. Coarse tick beyond 1.5 km from the player (no combat out there). Forts: 3 × 8-dmg balls every 10 s at hostile hulls in the ring. Merchant cargo: 30% of a producer's stock, ≤ 30 units, sold into the consumer's market on arrival. Patrols break off 1.4 km from home; raiders run home under 30% hull; hunters give up after 90 s beyond 2.5 km or while the player shelters in a fort's ring. Sinking a hunter/privateer gives +3 Crown, no faction penalty. |
| Weather numbers (M6) | Region wind factors: Deep 1.3, Storm Reach 1.1, Trade/Shoals 1.0, Fog Banks 0.85, Mangrove/Siren 0.8, Sargasso 0.75, Volcanic 0.7. Fog most of the time in the Fog Banks (75%), dawn fog 05–08 in the mangroves. Doldrums patches cut the wind by 92% (Sargasso 45% of the sea, Shoals 30%, Volcanic 25%, Siren 20%). Storms: one per ~3 min in Storm Reach at Threat 1 (rate 1.0; Deep 0.35, Trade 0.25, others 0.1–0.15), ×(0.5 + 0.5·Threat); torn sails 3%/s × strength at full sail, mended for 25 gold. Lantern: seen from 650 m lit, 200 m doused at night. Hunters earn credits 1.5× at night. |
| Monster numbers (M7) | Spawn roll 10% × Threat per in-game hour in the beast's region, ×2 at night, 60 s rest. Serpent 36 HP, bite 8 + 1 leak, up 9 s / down 12–17 s, alternating sides (a sloop with one carpenter cannot keep up with two leaks a bite). Kraken closes at 20 m/s (22 since 2026-09-28), tentacles 4 × 12 HP, cut three to break free, grind 1.5 HP/s + a leak per 8 s. Ghost 50 HP, flares 3 s in 6, two 4-dmg balls per 12 s, only solid in a flare. Crocodile 40 HP, bite 10 + 5 s rudder jam. Weed mass 50 HP, +3% water/s and no draining while it holds, carpenters cut 5 HP/s each; Sargasso slows every hull to 60%. Siren perch 50 HP, pull 0.6 → 0.36 over 250 m. Eruptions: 60 m zone, 5 s warning, 20 dmg + a leak. |
| Progression numbers (M8) | Part price = base × grade × (0.6 + hull cost/40000); bases: sails 120, rigging 120, planking 150, copper 180, cannons 60 per gun, ram 100, hold 120, lantern 90. Effects per grade: sails +3% speed/+15% handling; rigging −3°; planking +12% hull, −1.5% speed; copper +2% speed, 12% leak save; ram +25%/−12%; hold +10%; lantern +10% night radius and spyglass. Trade-in 60% of hull + staying parts. Officers: 60/150/400 gold, 4/8/15 a day; taverns roll one of each type per port per day (legendary never at small ports). Spyglass 900 m, 20° cone. Short riggers: handling ×fraction, top speed down to 80%, helm to 70%. Crew panel on C. |
| Build order | Nolan (2026-09-23): "just finish the whole game perfectly and polished then we can iterate if things feel off" — the M1 playtest gate is lifted; build M2–M11 and the polish bar, then iterate. |
| Exploration numbers (M9) | Black market: 5 uniques (ghost-grey sails 650, smuggler's keel 550, long nines 700, hidden hold 600, false colours 450), 1–2 per cove fixed by seed; they belong to the captain and survive a hull change. Bottle maps 60 gold at any tavern (one on offer per port), also from 30% of wreck salvages; a map matches within 150 m of the island's bound circle. Dig 15 s, Furled + F inside a 70 m ring, interrupted by any sail or leaving the ring; pays 150–400 gold × (1 + 0.2·(Threat−1)) + 1–3 rare units + a 20% cosmetic roll. Sargasso wreck salvage 10 s: 5–12 units of Sargasso produce + 30–80 gold. 30% of rumors are a "?" area (r = 400 m) instead of a price. Achievement checks once a second. |
| Run wrapper (M10) | Profile in `user://profile.json` (bests per preset, achievements, cosmetics, hints seen, last loadout). Suspend save written on every dock and on Save & Quit; resuming consumes it; a lost ship deletes it. The recap opens 2.5 s after the ship is gone. Fifteen of the 17 achievements each unlock one cosmetic (`Achievements.Reward`; By a Hair and Last Tide have none), and treasure digs roll a 20% cosmetic. A blank ship name draws a random one. |
| Onboarding (polish bar) | No tutorial. Thirteen one-line hint cards inked under the HUD, each shown once per profile at its moment: furled at the start, the wind rose, in irons, tacking, the harbour ring, prices, first hostile, first leak, first night, the Threat bar at tier 2, the chart after 45 s, the spyglass after 90 s, the first storm. 9 s each, 5 s apart; a hint retires early once its action is done. Off switch in Options (M11). Never shown in debug runs. |
| Options and audio (M11) | Keyboard actions are rebindable (14); the right mouse button, Esc and the wheel are fixed. Window sizes 1280×720 / 1600×900 / 1920×1080 / 2560×1440 or fullscreen (also F11 or Alt+Enter anywhere, fixed keys); UI scale 75–150%. Colorblind-safe palette = Okabe–Ito vermilion (Crown) and blue (Brethren) with the glyph shapes unchanged. Audio is entirely synthesized in-repo (`tools/audio/build.py`), no music, three buses (Master, Ambience, SFX). |
| Performance (M11) | The compatibility renderer's frame cost is draw calls, not CPU: the chart's hachures, dots and harbour rings drew as ~3,700 calls a frame (10 ms of render CPU, 54 fps). Islands and rings now draw as batched multilines (219 calls, 2 ms), the wake as one coloured multiline, merchant lanes are cached per port pair so A* never spikes a tick. Measured: 59 fps vsync-on alone, 57.5 fps with 40 extra ships within 1.2 km (tick 1.35 ms). |
| Director tuning, round 1 (M11 balance) | Early hunters are lone pirates: director spawns get guns and hands scaled by clamp((Threat−1)/1.5, 0.5, 1). Hunters alive are capped at 1 + ⌊Threat⌋ (6 from Threat 5). A hunter that cannot bring her within range for 180 s gives up. After any hunter gives up or sinks the director stays quiet for 150 s. Reason: the autopilot batches showed a lean sloop chased without pause from Day 1.4, pinned to fleeing and sheltering 60% of the time. |
| Autopilot (M11 balance) | `Autopilot` in the sim plays the player's ship headless: plans trades on remembered prices (score = (profit − upkeep)/days), speculates on goods cheap against their base price when nothing is known, carries the hold to the best known buyer, docks opportunistically, budgets repair and hiring, buys rumors/maps/parts/hulls/officers/uniques when rich, digs matched treasure, fights when it can sink the other ship well before being sunk (even fights only when sound), flees toward fort sanctuaries or downwind, shelters inside a fort ring, and avoids ramming. `scratch/balance` runs it in parallel per preset. It is a yardstick, not a player: Nolan's playtests have the final say. |
| Exports (M11) | Linux x86_64 and Windows x86_64 presets in `export_presets.cfg` (GL Compatibility, PCK beside the binary, .NET data folder). `LastTide.sln` exists because the .NET exporter refuses a project without one. Runtime-read files (`assets/text/en.csv`, `assets/audio/*.wav`) are packed as-is through `importer="keep"` .import files. Both builds pass `--selftest`: Linux natively, Windows under Proton 11 (Experimental). |
| Steam (M11) | `IPlatform` seam with `NoPlatform` shipping and `SteamPlatform` behind `#if STEAM`; achievement API names = upper-cased keys; cloud = Steam Auto-Cloud on `profile.json` + `suspend.json`. Everything that needs the App ID, the fee, the store page and the price is Nolan's (`docs/STEAM.md`). |
| Harbour rescue, once per port (M11 balance) | A foundering ship that reaches an open harbour is still taken in with 1 HP (§8), but each port does it only once per voyage; the second time she founders at its mouth (notice `NOTICE_RESCUE_SPENT`). Reason: the batches showed a broke 1-HP sloop parked at a port could not die and sat out the day cap. |
| Kraken retune (audit round 2, M7 tuning) | GDD §12 "each one hit frees a hold": a tentacle has 4.5 HP, so one grade-0 ball (6 × 0.8 at the least) cuts it; still three to break free. The grind (1.5 HP/s) no longer opens the 10% threshold leaks; the smash (3 HP and one leak) comes every 12 s instead of 8. Carpenters with no leak to plug hack at a tentacle (1 HP/s each, instead of patching), so a ship with no shot can still cut herself free. Reason: the lean sloop was certain to sink in a grip (100% water 28 s in, before her third volley), and a ship with no shot had no way out. |
| Shot stops at land (audit round 2) | Cannonballs burst where they meet a shore, so islands give cover; captains hold their fire through land; fort batteries fire from just off their coast toward the harbour. |
| The Siren's rocks (audit round 2) | While she sings, a coast strike of 1 m/s or more costs 2 HP per m/s and a leak from 3 m/s, laid at her door ("Wrecked on the Siren's rocks"); one strike a second at most. Grounding elsewhere stays harmless. |
| Map generator v5 (audit S27, 2026-09-26) | A harbour must lie in its island's own region, and validation requires a big-hull path from the start to every harbour outside the Mangrove Maze (on ~30% of v4 maps a port was unreachable by anything bigger than a brigantine). Suspend saves record `MapVersion`, so an older voyage resumes on its own map. |
| Mangrove yards (audit X6) | A Mangrove Maze shipwright builds only the five hulls the maze admits; a bigger hull bought there roamed channels the rule closes to it. |
| Weather at region borders (audit S13/S14) | Region wind factors, fog and doldrums blend over ~100 m at a border instead of stepping (wind jumped up to 15× in 0.4 m); a storm's swirl mixes with the wind as a vector, with a calm eye. |
| Hull against coast (audit S17) | Contacts use Coulomb friction, so a grazing hull slides along a coast instead of gluing to it. |
| Suspend save (audit S26) | The suspend save stores the state only, not the input/command logs (1.4 MB → 85 KB); replays of a resumed voyage start from the save. |
| The Lookout's warnings (audit HUD-02) | Off-screen markers show only what she can see, plus the Lookout's early-warning ring (100/200/300 m, §7), marked as the lookout's call. |
| The sea draws the sim's own wind (audit W-01) | The sea shader no longer mirrors the noise hash on its own clock; `SeaLayer` samples `World.WindAt` on a world-locked grid, so the strokes show regional wind, doldrums and storms exactly. |
| Ship card (Nolan, 2026-09-27) | "I should be able to hover over ships and see information about them." Any ship in sight, the player's own included, answers the pointer with a red ring and a card (§15). AI ships get names drawn from the run's seed and their id (`World.NameOf`), which stay the same across saves. Ships under a HUD plate, and ships while a page, pause or the recap is up, don't answer. |
| Harbour office (Nolan, 2026-09-27) | Nolan asked for more ways to make money, because the lean start rarely grows (27 of 200 Rough voyages got past 200 gold). He picked delivery contracts, survey fees, Crown bounties and smuggling from a brainstorm, and declined passengers and taking prizes. Smuggling reverses round 1's "no contraband": it exists only as office contracts, and markets are unchanged. Numbers are in §6. The office is the port's fourth tab, and contract ports are ringed in red on the chart with their deadline. The autopilot signs freight and dispatches bound where it was going anyway. When exploring, it takes a paid passage to a market it hasn't visited that's no farther than its own pick. It doesn't smuggle. |
| Merchants defend themselves (Nolan, 2026-09-27) | "merchant ships should be able to defend themselves but also still be trying to get away." Replaces "merchantmen flee, don't fight": they still never attack on sight, but carry up to 4 guns (was 2) and flee in a fighting retreat (`Seamanship.FightingRetreat`): within gun range they yaw up to 50° off the escape course to bring a loaded side abeam of the attacker, fire as it bears, then run on while it reloads. A ship dead astern is never answered (that would take a 90° turn); one that comes alongside is. |
| Fullscreen keys (Nolan, 2026-09-27) | He couldn't make the window fullscreen: the window was non-resizable, so the window manager refused. It is resizable now (the stretch mode already fits any size), and F11 or Alt+Enter toggles fullscreen anywhere, saved like the Options checkbox. |
| Water follows the hull (Nolan, 2026-09-27) | Nolan disliked the pumps and asked for the water to rise while the hull is below 60% and fall while it is above, in proportion to the distance from 60%. Water now changes by 1%/s for every 10% of hull from the 60% flood line; leaks no longer add water (they still block patching until plugged). Removed: the pump station (left-over hands are spare), the Pumps part (older saves drop its grade), the bilge engine unique (dropped from older saves), and the glass bonus for pumping hands (the glass is a flat 35 s, what a crewed ship used to get). Carpenters keep patching through a water-only stand, so she can patch over the line and drain below 80% to end it. The Weed-Kraken's grip stops her draining on top of its +3%/s. Order 3 is "Repair" (carpenters first). The hull gauge marks the flood line. |
| Cartographer (Nolan, 2026-09-27) | "You should have to hire a cartographer to use the map, and you should only be able to see what you have 'discovered' while you have a cartographer aboard." A fourth officer (`OfficerType.Cartographer`), hired at taverns in the same three tiers, who **has his own berth**: he fills no officer slot on any hull and never goes ashore on a hull change. Green / seasoned / legendary **(proposal)**: 40 / 120 / 320 gold, 3 / 6 / 12 gold a day (cheaper than the other officers because every voyage needs one), and he inks out to **1.0 / 1.25 / 1.5 × the vision radius** (lookout included). He walks off unpaid like any officer. **Without one:** nothing is inked (sight, spyglass fan, a matched bottle map's X), no port is marked by sight or by tying up there, the chart (M) stays shut and the HUD says why (`NOTICE_NO_CARTOGRAPHER`); the chart already drawn is kept and shows again with the next one. Coves are "found" when he marks them, so none is found without him. What others mark for her still goes on it: the harbour master's chart of the Trade Isles the voyage starts with, a tavern rumour's port, a contract's destination. The purser's ledger of prices is kept either way (it is not the chart). Signing one on inks the chart round the harbour at once. **The voyage starts without one;** the home port's tavern always offers a green one (the first purchase), other taverns one at a rolled tier; the tavern lists him first. The in-world fog clears a live circle of her current sight every frame, charted or not, and off the chart's edge too, so she always sees round her; only what he inks stays. Suspend saves from before the rule resume with a green cartographer aboard. The autopilot hires one at the first port where it can pay for him and keep 30 gold. A 14th onboarding hint says why nothing is kept, the chart hint waits for him, and the key legend reads "chart (no cartographer)". |
| Landform islands, map generator v6 (Nolan, 2026-09-27) | "The islands should be procedurally generated like they already are but with structure like real life: not all the same size and shape, placement not completely random, lagoons, bays, etc." Replaced lone noise blobs with the landform generator of §5 step 2 (`Landforms`, `Coastlines`, `RegionLayout`). Ports prefer sheltered harbours (weight = shelter⁴, where shelter is the share of 16 bearings with land within ~400 m) and a big island may hold up to four; every harbour, dig ring and wreck must lie in the one connected open sea (`NavGrid.IsOpenSea`). Old maps are no longer generated: **a suspend save from before v6 cannot resume** (the chart it was sailing no longer exists). The generator keeps a map's coastlines when only its ports fail, so a map costs ~1 s. Islands are indexed by 500 m cells (`Map.IslandsAround`) so collisions and line-of-sight don't scan all ~800. Separate landforms keep 30 m of sea or more between them (more in open regions); within one landform islets may lie a narrow gut apart, but a rock under 3,000 m² within 25 m of a bigger islet of its own landform is dropped (it read as a crack in the coast). |
| Bigger chart, twelve regions (Nolan, 2026-09-27) | Nolan chose **3× each way** (18 × 13.5 km) and "the same 9, just bigger, but also add more regions". Added the Ice Reach (drift ice), the Maelstrom Straits (whirlpools) and the Corsair Keys (a Brethren stronghold where raiders press 1.8×), each with goods, weather, colours, flora and a signature landform. Region seeds sit on a 4 × 3 grid; the border warp was scaled up with them. Ports per region scaled so the total is 140–152; treasure 26–40; wrecks 9–15. Merchant lanes trace each harbour's sea-distance field only within 3.6 km of it (a full-chart field per harbour would be ~1.5 MB each). The sea's SDF texel went from 6.25 m to 8 m to keep the texture at 3.8 M texels. |
| Islands farther apart, generator v7 (Nolan, 2026-09-28) | "Make the distance between the islands bigger and subsequently the size of the map bigger." Everything map-scale is multiplied by `Map.Stretch` = 1.4: the chart (25.2 × 18.9 km), region seeds and border warp, island-group spreads and spacing, every region's channel between landforms, the edge margin, the Maelstrom's and Sargasso's search boxes, the start-route range (840 m), the luxury distance (4.2 km), merchant runs (3.1 km) and lanes (5 km), contract distances (490 m–3.9 km) and the autopilot's port searches. Land shares are divided by 1.4², so each region aims for v6's land in its larger sea; the landforms keep their sizes. Measured over 12 seeds: nearest island of another landform 201 → 338 m (median), nearest harbour 581 → 684 m, ports 145 → 147, islands 983 → 1,083 (the Mangrove, Maelstrom, Trade Isles and Fog Banks now reach the land share that v6 had no room for), land 22 → 28 km², generation 0.5 → 0.85 s. Suspend saves from v6 are refused. The reveal mask, nav grid and coast SDF keep their cell sizes, so each is ~2× larger. |
| Chart monsters round the border (Nolan, 2026-09-28) | "The images of sea monsters on the map should be smaller and there should be six of them evenly spread out around the edge of the map, for some reason right now the three sea monsters are on the top left of the map." The chart screen drew three, in the uncharted cells farthest from the edge that fitted the opening view, so they bunched in one blank corner. Now six are drawn on the frame rather than the sea, so all six show at every zoom and pan (the chart opens zoomed onto the charted water, where monsters pinned to the sheet's edge would be out of view): equal steps round a ring just inside the neatline (two along the top, one at each side, two along the bottom), the four drawings shuffled by seed with no repeat side by side, those on the right turned to face inward. Width 11% of the frame's height (was 20% × √zoom), under the land and the lettering. |
| No crew orders (Nolan, 2026-09-28) | "The thing where I can choose battle stations, make sail, repair, and balanced, get rid of the switching stuff with 1234 because you should just have to click C and manually switch the crew." The four order keys, their key bindings (Options, settings files drop them), the crew panel's order buttons, the HUD's order name and 1–4 key cap and the `ShipInput.Order` field are gone; the last stand's patch prompt and the leak hint point at C. The crew starts balanced and is set by hand with − / +. New hires onto a hand-set crew fill places still wanting hands (sails, guns, repair) rather than standing spare, since nothing rebalances them now. AI ships still sail by orders; the autopilot sets its hands through the crew panel's command, as a player would. |
| Fishing (Nolan, 2026-09-28) | "I should be able to fish in this game." Proposal accepted with his changes: fish anywhere, some places richer, grounds very subtle. Rules and numbers in §10 "Fishing". Built as the context key rather than a crew station so it is a choice (lie still, spare the gun crews) and not background upkeep; the catch rides the existing Collect event (callout "+1 Fish", a small splash). Fish is good 28, appended so older per-good arrays keep their places; the market gives it no generator draw, so maps are unchanged. |
| Beyond the chart (Nolan, 2026-09-27) | See §5 "Beyond the chart". The wall at the chart's edge is gone for the player (other ships keep to the chart); the Mangrove rule counts only water on the chart; the Sargasso weed and the volcano stop at the edge. Tuning **(proposal)**: `EdgeMonsterScale` 600 m, rolls every max(6 s, 1 h / (1 + 3k)) with chance min(0.95, 0.3 × Threat × (1 + 4k) × night 2) at k = metres out / 600; the rest after a beast shrinks by 1 + 2k. Whirlpools: cells 420 m, start 300 m out, full at 3.2 km; radius 80–280 m, swirl 2.5–8.5 m/s at the core's rim. The sea shader drops the neatline and darkens the water out there. |
| Hull liveries (Nolan, 2026-09-27) | "Make visual improvements to each new ship hull, they should look really good." Each hull now wears its own paint (topsides and a stripe, after the ships it's drawn from) instead of one umber, shown on a band of hull side outside the rail. Faction paint is now a touch on top of the livery rather than a whole-hull tint: the Crown runs a red strake, the Brethren tar their topsides and run a sea-green stripe. A cosmetic hull colour repaints the player's topsides; plain is the hull's own livery. Ships, sails and spars cast shadows to the south-east (faded at night, in fog and rain), and a ship heels to leeward under sail. View only; no rule changed. |
| Whirlpools and drift ice (2026-09-27) | Both are pure functions of the seed, the place and (for ice) the clock, so they cost nothing to save and replay exactly; only the Maelstrom's are generated and kept with the map, and their cores are closed to the nav grid so lanes steer round. Ice: 0–3 floes per 200 m cell of the Ice Reach, 4–30 m across, each circling 20–80 m round its home every 5–13 minutes, kept 30 m off every shore and harbour ring; a blow above 1.2 m/s costs 5 HP per m/s beyond that and a leak from 3.5 m/s ("Stove in by the drift ice"). |
| Start in port (Nolan, 2026-09-28) | From the audit brainstorm (item 1, "start the voyage docked, with a first job in hand"), which Nolan chose: "add 1, 2". A new voyage (the title's Set sail, not a resume or a debug run) docks her at the home port at once (a logged `Dock` command, so the clock stays at zero and the dock autosave is written) with the ledger open. A harbour master's welcome card over the ledger (the goal, a first job and a cartographer, both ticked) came with it and was dropped the same day: "get rid of the welcome aboard card, its not needed". |
| Delivery marks (Nolan, 2026-09-28) | From the audit brainstorm (item 2, "a course marker at sea"), which Nolan chose. Each port she owes a delivery gets an HUD edge badge (ochre; red under a day left; a seal for dispatches, a crate for freight and contraband) on the screen's rim toward its harbour, or over the harbour once it is on screen, lettered "Port Elmore · 2.1 km · due Day 5" (metres under 1 km). One badge per port, soonest deadline; none while she lies in its ring; deliveries rank after the last stand's refuge and never merge with ship badges; a delivery's label is never dropped (it slides along its side and is lettered over a plate as a last resort). Contract ports are marked for her anyway (§19 "Cartographer"), so no cartographer is needed. |
| Flotsam adrift (Nolan, 2026-09-28) | "There should be floating barrels rarely." **(proposal)** A roll once an in-game hour while she makes way (≥ 1.5 m/s, not foundering) at `DriftChancePerHour` 2% (about one barrel every two days at sea, ~4 real minutes): a barrel 0.75–1 × clamp(0.6 × sight, 120, 240) m ahead and 25–70 m off one bow, never behind land, floating `DriftLife` 150 s (a spill: 60), holding 2–5 units of what the region produces or 2–4 of provisions, munitions or timber (30%). The lookout sings out ("a barrel adrift off the starboard bow"). The roll and the barrel are pure functions of the seed and the hour (`World.DriftRng`), so nothing new is saved and replays draw the same barrels; `DriftEnabled` (saved) is off in the physics fixtures. Flotsam in sight but off the screen gets a brown edge badge. |
| Hit feedback (Nolan, 2026-09-28) | "Make clear hit feedback." `CombatEvent` names the shooter (`By`) and a pickup's good (`Good`, with the units in `Strength`). The view (`game/Callouts.cs`, under the HUD plates): a figure pops where a ball strikes and rises away, black for her own hits and red for hits she takes, merged over a broadside's ripple; "Leak!" when a blow holes her; "Sunk!" over a ship her guns sank within the last minute; "+3 Rum" / "+40 gold" for pickups; a hull bar over any ship struck in her sight for 4.5 s whose lost planks hang pale for a beat and then drain. Her own HUD hull tube flashes red and shows the same pale chunk. Her shots striking home are heard at any range (a touch higher). `--broadside` (fires the starboard battery whenever it is loaded) and `--flotsam` (a barrel ahead) are for screenshots. |
| Drift ice is solid (Nolan, 2026-09-28) | "Why can i drive over and shoot through icebergs." Two faults. A hull met the ice as three circles along the keel, so a small floe slipped between them and a big hull sailed over it (a man-o'-war ended 13 m inside a 6 m floe); it now meets the ice with its whole keel capsule, as balls and rams test it, with up to four settling passes a tick for floes that crowd her; a blow costs the §19 "5 HP per m/s" once a floe a tick rather than up to three times. Probe (6 seeds × 12 floes × 6 approaches, weaving under full sail): worst overlap sloop 0.1 m, brig 0.2, frigate 0.3, man-o'-war 2.1 on a 27 m floe (before: up to 13 m, 35 runs over 1 m for the man-o'-war). And shot ignored ice: a ball now bursts on a floe's rim as it does on a shore (`World.IceAlong`), so floes give cover, and captains hold fire through them (`Seamanship.ClearShot`). |
| Whirlpools clear of everything; the sea's hazards thin near land (Nolan, 2026-09-28) | "I saw a whirlpool that overlapped an island … it should never overlap anything", then "whirlpools shouldnt happen near land, also icebergs and sea monsters and stuff like that should be less common closer to land". **Whirlpools:** the Straits' no longer turn in the narrows (they were sized to ¾ of the gap, so they lay over both shores): `MapGen.PlaceWhirlpools` puts them in open water, each sized to the sea round it with **120 m** (`WhirlpoolShoreMargin`) between rim and shore and 300 m between rims; radius 80–150, the great maelstrom 180–260 (7–10 a chart plus the great one on seeds 1–8, 11, 21). Beyond the chart each keeps inside its own 420 m cell and its rim 150 m past the edge (`Whirlpools.CellMargin`, `EdgeMargin`), so none overlap; they still grow further out. Floes keep off whirlpools and on the chart. **Floes** thin toward land: a floe survives by `Shore.Openness` of its home's distance from land, 30% at the shore rising smoothly to all of them 300 m out (seed 4: 4 per km² within 60 m of land, ~20 from 200 m out). **Beasts:** the serpent, the Kraken, the ghost ship and the weed-kraken roll at 30% of their rate by the shore, rising to full 400 m out (`World.ShoreFactor`); the crocodile and the siren, who live on the bank and the rock, are unchanged, as are the eruptions (a volcano is land) and the beasts beyond the chart (no land there). Chart version stays 7 (unreleased). |
| News without rumours (Nolan, 2026-09-28) | "Get rid of the rumour, you only know the price of ports while you are in that port, how do we make it so that you can have more information while keeping these mechanics." He kept the ledger ("keep the ledger"), chose the harbour office's wanted notices, and "i should also be able to interact with merchant ships to ask about the ports they have recently been to". **Rumours removed:** the tavern's rumour (15 gold: a random port's price, or 3 in 10 a "?" area round an undiscovered cove) and the cove "?" areas are gone; coves are found only by sight. **Merchant news:** every merchant remembers the prices (what the port makes and wants) at the last **3** ports she traded in, on loading and on selling (`AiState.News`, saved and hashed). Within **150 m** and in sight, F hails her (before the fishing lines; with the lines out F still hauls them in): her prices go into the ledger **dated her visit**, marked with her name (`LedgerEntry.Teller`), never over a fresher price; the ports she names go on the chart. A merchant she has fired on runs rather than answers. The HUD prompt reads "Hail the <name>"; her card says how many ports' news she carries. **Wanted notices:** the office lists up to **4** open ports within its contract reach whose stock of a good they consume is under 85% of target ("wanted"; under 50% "badly wanted"), shortest first, with the port, its region and the days' sail; the market's detail pane repeats it for the chosen good. No price is ever posted. The autopilot no longer buys rumours and does not hail. |

### Still open

Nothing blocking. Every other **(proposal)** tag marks a v1 value to tune in playtest, not an open design question.
