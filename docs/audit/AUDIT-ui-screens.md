# AUDIT — ui-screens (visual agent: theme, title, port, logbook, chart, pause, options, crew)

Branch `ui-screens` in `~/.cache/lt-audit/ui-screens` (merged `sim-trade` @ 6645db6 and `game-core` @ 7113d7b at the
lead's request; conflicts in PortScreen.cs, Main.cs, Main.SelfTest.cs and en.csv resolved in favour of both sides).
Kept current while working. Screens: BEFORE `~/.cache/lt-audit/gallery/before/*.png` +
`scratch/before/{base,s150}/*.png`; AFTER `scratch/shots/final/*.png` (1600×900, contact sheet `final-sheet.png`),
`scratch/shots/final-fs/*.png` (1920×1080), `scratch/shots/{s75,s125,s150}/*.png` (UI scale), and the lead's gallery
script output `scratch/gallery-after/*.png` (all paths relative to this copy).

## Incident (process, mine) — READ FIRST
- **INC-1 (major, data): my first capture runs wrote into Nolan's real `~/.local/share/godot/app_userdata/Last Tide/`.**
  My capture script passed a *relative* `VRUN_USERDATA`; Godot ignores a relative `XDG_DATA_HOME` and silently falls back
  to `~/.local/share`. Evidence: the title capture showed "5 voyages logged · best 0.00 days on Rough Seas" instead of the
  fixture profile; the real folder's files carry my run times; those runs' private `.ud-*` dirs hold no logs/shader cache
  (the lead's gallery runs, with absolute paths, do).
  - What was written: `profile.json` at 13:10:51 by my `--logbook` recap capture (`Profile.RecordRun`): `Voyages` +1 and,
    if they were absent, `Bests.RoughSeas = {0 days, "Last Tide", 2026-09-23, ""}` and achievement `by_a_hair`. Current
    content: Voyages 5, that one best, that one achievement — i.e. only debug-run records; prior content unknown (no
    backup). `logs/` rotated by ~17 runs 13:05–13:45 (Godot keeps 5 logs, older ones are gone). No suspend save,
    settings or hints were written by my runs.
  - Not mine: `debug/profile.json` at 13:28:18 (a `--hints` run; I never ran one) — another agent may have the same bug.
  - Fixed at the source: `~/.cache/lt-audit/vrun.sh` and `vcmd.sh` now `realpath` the workdir and the user-data path
    (atomic replace; original at `/tmp/claude-1000/vrun.sh.orig-ui-screens`); my scripts absolutise their paths too.
    Verified afterwards: my runs' `.ud-*` dirs hold `logs/`, `profile.json`, `shader_cache/`; the real `logs/` did not
    change after 13:45. I did **not** touch the real folder again. **Needs the lead/Nolan:** decide whether to reset the
    real `profile.json` (Voyages −1, drop the debug-only best/achievement).

## Findings (bugs in my files, found on the way) — all fixed
| ID | Sev | Where (before) | What was wrong | Evidence | Fix | Status |
|---|---|---|---|---|---|---|
| UI-01 | minor | `TitleScreen.cs:268,278` | `Hide()` and `SetName(string)` hid `CanvasLayer.Hide` / `Node.SetName` (CS0108); `title.Hide()` from Main silently meant the page, not the layer | build warnings (3 at baseline) | renamed `Close()` / `SetShipName()`; Main + self-test updated | fixed (0 warnings) |
| UI-02 | minor | `ChartScreen.cs:109` | `ChartCanvas.Scale` hid `Control.Scale` (CS0108) | build warning | renamed `MapScale` | fixed |
| UI-03 | minor | `TitleScreen.cs:197,215,242` | bests printed as "Best: Day 0.00" / "best Day 0.42" — days survived shown as a day number | `gallery/before/title-voyage.png`, fixture best 0.42 | "Best · 0.42 days", logbook "12.43 days" + "lost Day 13 · dog watch" | fixed |
| UI-04 | major | `Parchment.cs:48-53`, `PortScreen.cs:165` | the market's quantity slider rendered as a dot: the theme set the slider/grabber areas but no grabber icon (the `grabber` StyleBoxFlat was built and never used) | `gallery/before/port-market.png` | ruled track + fill + wax-seal grabber icons, ± buttons | fixed; self-test "+ raises the quantity…" |
| UI-05 | major | `ChartScreen.cs:329-346` | GDD §5 "hovering a port shows your ledger: last-seen prices with their age" — the card always said "No prices seen yet" | code: `CHART_NO_PRICES` unconditional | card lists what the port sells/buys from the player's own ledger (or rumours), with "seen Day N" / "rumour, Day N" | fixed; self-test "hovering a port…" |
| UI-06 | major (perf) | `ChartScreen.cs:115-118` | the chart redrew everything every frame (`_Process` → `QueueRedraw`): **800 draw calls** with the chart fully inked, one `DrawColoredPolygon`/`DrawPolyline` per island | `--chart --reveal --fps`: 800 calls | islands triangulated once into one draw, coasts/hachures/shallows batched in multilines, redraw only on change, world hidden under the page | fixed: **217** calls (267→107 for the start chart) |
| UI-07 | minor | `PortScreen.cs:380,386,500` | market prices ignored friendly standing (−10 %) and the quartermaster, so the ledger disagreed with the gold actually charged | (also sim-trade T-xx) | ledger/detail/buttons use `World.BuyQuote/SellQuote` and the price multipliers | fixed (merged sim-trade) |
| UI-08 | minor | `PortScreen.cs:506-507` | with 6 held and 20 affordable the slider could sit at 20 and **Sell** went disabled; the sell total quoted for units she did not hold | code | Sell sells min(n, held) and says so; "None in the hold" when empty | fixed |
| UI-09 | polish (perf) | `PortScreen.cs:494` | affordable count found by a linear loop of unit-by-unit quotes (O(n²) per refresh, every slider tick) | code | binary search over `BuyQuote` | fixed |
| UI-10 | minor | `OptionsScreen.cs:74` | dragging the UI-scale slider re-scaled the whole UI on every tick, so the slider jumped away under the pointer | code (`ValueChanged` → `Apply`) | the scale applies when the drag ends (keys still apply at once) | fixed |
| UI-11 | minor | `PortScreen.cs:473-480` | the tavern showed the last rumour heard **anywhere** (`World.LastRumor` persists), and after a cove rumour it said "The tavern talks for a price." with 15 gold spent (sim-trade R-08) | code | this visit's rumour only; cove rumours answered ("a ‘?’ is marked on your chart"); every rumour still in the ledger listed | fixed |
| UI-12 | minor | `PortScreen.cs:438` | trading down read "Buy · 0 gold after trade-in" though the yard now pays the surplus (sim-trade R-06) | sim-trade T-04 | "Trade down · the yard pays N gold" | fixed |
| UI-13 | polish | `PortScreen.cs:447` | "Hire 5" enabled with under 50 gold, and offered 5 when fewer berths were free (sim-trade R-07) | code | hires what the berths allow (≤ 5), disabled unless the fee for all of them is in the purse | fixed; self-test "Hire" paths unchanged |
| UI-14 | polish | `en.csv PORT_QTY` | "1 units: buy for 5 …" (sim-trade R-10) | text | "Quantity {0} · …" (the row is no longer on screen; kept correct) | fixed |
| UI-15 | minor | `CrewPanel.cs` / `Main.cs:640` | with the crew panel open, the 1–4 order keys did nothing although the panel's buttons are labelled 1–4 | code | Main forwards the order keys to `CrewPanel.PressOrder` | fixed; self-test "3 on the open crew panel…" |
| UI-16 | minor | `TitleScreen.cs:154` | the logbook's achievement list was a 330 px scroll box of "X / -" rows; long descriptions ran into the next column at 150 % UI scale | `before/s150/*` | medallion grid with locked (grey) / unlocked states and the reward | fixed |
| UI-17 | major | all pages | at UI scale 125 % (1280 × 720) and 150 % (1067 × 600) the voyage page overflowed sideways, the logbook page downwards, the shipwright/cove/tavern were cut right and bottom (also game-core R-2/GC-21) | `scratch/before/s150/*.png`, game-core `scratch/audit/shots/ui150-*`, `s125/` | every page lays itself out from its root size: compact title pages under 800 px, a tighter port rim, wrapping card/button rows, the hull catalogue moves under the parts on a narrow page, the ledger drops its optional columns when narrow, the detail particulars scroll with Buy/Sell always visible | fixed; `scratch/shots/{s125,s150}/*.png` |
| UI-18 | major | `TitleScreen.cs PressSetSail` | "New voyage" silently destroyed a suspended voyage (its first dock overwrites the one suspend slot; sinking deletes it) (game-core R-1) | game-core report | with a save present, Set sail asks "Abandon the saved voyage?" naming the ship and day; **Keep her** is focused and is the default, Esc keeps | fixed; 3 self-test checks |
| UI-19 | minor | `TitleScreen.cs:129`, all screens | Enter in the ship's-name field did nothing; no screen took the keyboard focus, so arrows/Enter did nothing until a click (game-core R-5) | code | Enter sets sail; every screen gives a default focus (title line, preset card, Back, Resume, Keep her, ledger, first live control on a turned port page, orders, New voyage); scrolls follow the focus; the logbook scrolls with the arrow/page keys | fixed; 6 self-test checks through the keys |
| UI-20 | polish | port/chart/crew (baseline) | port tabs, trades, hire, repair, crew and chart actions were silent while title/pause/options clicked (game-core R-4) | code | every button goes through `Parchment.B` (click); trades, repairs, hires and purchases ring the coins; a chart pin is inked with the quill | fixed |
| UI-21 | minor | `en.csv` key hints | key letters were hard-coded in my new hints (Q / E, M, C) | code | `[[FirePort]]`/`[[FireStarboard]]`/`[[Chart]]`/`[[Crew]]` tokens, re-read on open; the port pages turn on whatever the broadside keys are bound to | fixed |

## Reported, not fixed (outside my files or a decided rule)
| ID | Sev | Where | What | Evidence | Status |
|---|---|---|---|---|---|
| R-UI-1 | minor | `CrewPanel` order buttons + `World.SetStations` (`World.Progression.cs`) | crew-panel orders set `Ship.Order` directly and `SetStations` is not a logged command, so a replay from the logs diverges after the panel is used (the key orders go through `ShipInput.Order` and are logged). Saves are unaffected (they store Order and CustomStations). Needs a logged crew command in the sim. | code | proposed |
| R-UI-2 | minor | `Main.SelfTest.cs` | the "first-voyage hint" check flaked in slow runs because the dig stretch earlier in the test sits furled long enough to show `first_sail` first (sim-trade R-11). I made the check's premise explicit (`profile.HintsSeen.Remove("first_sail")` before the fresh voyage). "a broadside booms" can still flake when the box is loaded (another one-shot lands last) — not touched. | runs st1/st2 | partly fixed |
| R-UI-4 | minor (test infra) | `Main.SelfTest.cs` + game-core `RealInputFilter` | Godot's built-in `ui_*` actions (Enter, arrows, Tab — GUI focus navigation) are bound to device 0, so after game-core stamped the test's events with a synthetic device id no keyboard navigation could be driven by the self-test (37 checks failed after the merge). The self-test now sets those actions' events to match any device, for the test only. | self-test run after merge | fixed (self-test only) |
| R-UI-5 | minor | game-core `Main.Playtest.cs:86`, `Main.Audit.cs:257` | they called `title.SetName(...)`, which after my CS0108 rename resolves to **`Node.SetName`** and would silently rename the node instead of the ship; changed to `SetShipName`. | build + code | fixed |
| R-UI-3 | polish | `Main.cs` title mode | the title's backdrop world is a random new sea with the player's sloop in harbour under the menu; I pool paper behind the menu so it never crosses the lettering, but the camera framing is Main's. | `scratch/shots/fs/title.png` | noted |

## What changed (visual)
- **Theme** (`Parchment.cs`): a UI kit in `assets/art/ui` — tileable paper graded to `Ink.Paper` (+ aged), a 9-slice
  deckled/age-browned rim with a printed double rule, Codex corner flourishes, hand-ruled cards (normal/on/hover/well),
  inked buttons (normal/hover/pressed/disabled), **red corner-bracket focus on every focusable control**, ribbon tabs
  (bookmark ribbons, red when open, blue "Cast off"), key caps, inked check boxes ticked in red, ruled sliders with a
  wax-seal grabber (normal/hover/disabled), hairline scroll bars with ink thumbs, LineEdit as an italic ruled writing
  line with a red caret, italic tooltips, watercolour washes for menu hovers. Label variations Title/Big/Head/Caption/
  Flavour/Warn/Data (Display, SmallCaps, Italic, Body). Widgets: `UiSheet`, `UiCartouche` (5-slice Codex plate),
  `UiBanner`, `UiDivider`, `UiPips`, `UiGauge`, `UiFigures`; `Parchment.GoodIcon/PartIcon/AchievementIcon/Crest/Portrait`
  (atlases → one batch). Every piece falls back to plain drawing when its PNG is absent.
- **Title**: Codex key art (brig and kraken) composed with the live sea, "LAST TIDE" in DisplayCaps sized to the screen,
  a Codex divider flourish, the tagline on a Codex ribbon, a small-caps menu with watercolour hover and focus brackets,
  paper pooled behind the lettering. **Voyage page**: three illustrated preset cards (calm/rough/tempest vignettes,
  growth, target, best), cosmetic slots with colour swatches, a **live preview** of the sloop in the chosen colours
  (the real `ShipView`, her wake in the chosen ink, her name lettered under her, rocking), a ruled name line.
  **Logbook page**: bests as cards with vignette, days and cause; 17 medallions, grey when locked, with the reward.
- **Port**: full-page ledger on a dark table (world not drawn under it): faction crest (tinted, cove = skull and keys),
  standing meter with the shut-out band, the cartouche with the port's name (red at a cove), purse and hold gauge;
  ribbon tabs + Q/E paging; the market as a drawn ledger (27 goods icons, red money rules, buy/sell as charged, trend
  chevrons, stock word + gauge, made/wanted, held, notes: margin per unit on goods held, better price on record);
  a detail pane with the good's picture, group and bulk, charged prices, stock gauge, what she paid, the port's role,
  prices on record elsewhere, a seal slider with ± and live totals on Buy/Sell. Shipwright: repair card with hull
  gauge, guns card with gun ports and graded gun price, part cards (icon, grade pips, effect, price), black market
  first at coves, the 14 hulls as a to-scale catalogue (`Art.Tex("ships/<id>")`, inked outline fallback). Tavern: crew
  card with portrait and sailor figures, rumours with the ledger's rumours listed, the bottle map, officers with
  portraits and tier pips.
- **Recap**: a real log page — aged paper, blue ruling, red double margin, the ship's name in Display italic, entries
  in italic on the lines, dotted leaders to the figures, an ink blot, a sketch of her going down, the captain's red wax
  seal on a best, honours earned with medallions.
- **Chart (M)**: antique sheet with flourishes, the cartouche, a subtitle (ship · day · % charted), a 4:3 neatline with a
  chequered 250 m degree band, rhumb lines from two compass roses placed in open water, the charted sea washed in
  watercolour that pools at its edge, hachured coasts and dotted shallows, region names spaced in Display italic in
  open sea, Codex sea monsters in the blank margins (never over charted water, pins or rumours), a key to the glyphs,
  port names placed to avoid each other, haloed labels, a red ship, the ledger card on hover, bottle maps in cards
  that fit their coast, + / − / arrow keys.
- **Pause**: a small sheet with the cartouche, where she is ("the Kestrel · Day 3 · forenoon watch · Rough Seas") and
  the menu. **Options**: sections with rules, check boxes, seal sliders, window-size buttons, and a two-column orders
  card of key caps with dotted leaders; click or Enter to rebind. **Crew panel**: order buttons, station icons, a
  sailor per hand (faded for places still wanting one), wages, ± per station.

## Performance (draw calls after 600 frames, `scratch/dc.sh`; renderer-independent)
| Scene | Before | After |
|---|---|---|
| sea `--seed=7 --sail=3` | 220 | 220 |
| fleet `--fleet=40` | 237 | 240 |
| port market `--dock` | 226 | 74 (the world is not drawn under the page) |
| shipwright `--page=1` | 236 | 220 |
| tavern `--page=2` | 205 | 86 |
| chart `--chart` | 267 | 111 |
| chart fully inked `--chart --reveal` | **800** | **221** |
| title | 90 | 99 |
| voyage page | 179 | 208 |
| pause | 171 | 171 |
| crew panel | 187 | 207 |
| recap | 176 | 128 |

## Art (logged in `docs/ai-assets/ui-screens.md`)
19 Codex jobs (batched sheets cut by `tools/art/ui_cut.py`), kit built by `tools/art/ui_kit.py` (deterministic).
`assets/art/ui` 4.3 MB, `title` 2.5 MB, `goods` 0.5 MB, `people` 0.4 MB, `parts` 0.2 MB.

## Tests run (final, branch head)
- `dotnet build LastTide.csproj --no-incremental`: **0 errors, 0 warnings** (baseline 3 CS0108).
- `dotnet test tests/Sim.Tests`: **142/142** alone; in the full run under load (load average 24–27 from the other
  agents) `AiTests.ThePopulatedSeaTradesAndKeepsItsBudget` (a wall-clock budget, sim-trade R-05) failed once and passes
  alone — 141/142 + 1/1. I changed no sim code (`git diff main -- src/` holds only the merged sim-trade/game-core work).
- Self-test: **SELFTEST PASS, 171 `ok`** in two parallel runs (baseline 106; +33 from me through clicks and keys, the rest
  from the merged branches).
- Screens: 16 captures × {1600×900, fullscreen 1920×1080, UI 75 %, 125 %, 150 %}, all looked at; nothing overlaps or
  clips (hull sketches are the inked fallback until art-ships' `ships/<id>.png` land).
