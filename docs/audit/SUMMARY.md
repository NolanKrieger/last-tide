# Audit and visual overhaul, 2026-09-23 → 2026-09-26

Nolan: *"do a full audit on the game and make sure there arent any bugs, keep making visual and ui improvements. i
want this game to look amazing."*

Eight agents worked in isolated copies (scratch git branches under `~/.cache/lt-audit/`), four auditing the rules and
the game layer and four redesigning the look; the lead verified each auditor's regression tests against the untouched
build (they fail there), merged the branches, reconciled overlaps and fixed what the merges exposed. Per-area reports,
with every finding, its evidence and its fix, are in this folder.

## Bugs fixed, by area

| Area | Report | Fixed | Worst finds |
|---|---|---|---|
| Economy, progression, exploration | `AUDIT-sim-trade.md` | 14 | Same-port buy→sell round trips printed 16–42 % (friendly standing + quartermaster); standing farmed by re-docking; graded guns sold at the flat price; trading down lost the surplus; a sinking ship could dock by hand and buy a new hull |
| Game layer: run lifecycle, input, settings, audio, text | `AUDIT-game-core.md` | 36 | Any chart pin made every save throw (voyage unsavable); harbour rescue / resuming a dock autosave left her docked with no port screen; quitting mid-voyage threw the voyage away; the last tick's events replayed every frame after the loss |
| Combat, AI, director, monsters (two rounds) | `AUDIT-sim-combat.md` | 37 | AI ships froze on coasts (28 of 32 within 20 min — the economy stalled); captains stalled in irons; AI broadsides fired where they could not hit; being rammed made the rammer hostile; night multiplied the whole Threat; cannonballs flew through islands; the Kraken was certain death for the starting sloop |
| Sailing, wind, weather, saves, map generation | `AUDIT-sim-sailing.md` | 29 | Resumed voyages lost their AI captains; wind jumped up to 1,567 % at region borders and storms flipped 180° across a line; hulls glued to coasts; the lantern widened daytime vision; the suspend save grew to MBs with the input log |
| Sea, islands, ports, fog, weather views | `AUDIT-world.md` | 10 | The sea's gust patches ran on the render clock, not the sim's; strokes jumped and swam with the wind |
| Ships, sails, monsters, effects | `AUDIT-art-ships.md` | 4 | Sunk ships vanished instead of sinking; only the player had a wake |
| HUD and hints | `AUDIT-hud.md` | 17 | Off-screen markers printed over the HUD and each other; the Lookout's early warnings never existed; notices timed in frames |
| Menus, port, logbook, chart, options | `AUDIT-ui-screens.md` | 21 | The market slider rendered as a dot; screens overflowed at UI scale 125–150 %; "New voyage" silently destroyed a saved voyage; no keyboard focus anywhere |
| Lead (merge) | below | 10 | Map generator v5 (S27: on ~30 % of maps a port could never be reached by a big hull); merchant lanes grazed land corners; mangrove yards sold hulls the maze forbids; two hulls on one spot never separated; far traffic ignored the Sargasso; a malformed launch argument broke startup; stack allocations in draw loops |

Total: **178 fixes**. Every auditor fix carries a regression test that fails on the untouched build; the visual
agents' and the lead's fixes carry a test, a self-test check or captured evidence (see each report).

## Lead's merge fixes
- **S27 → map generator v5:** a harbour must lie in its island's own region, and validation requires a big-hull path
  from the start to every harbour outside the Mangrove Maze. Old suspend saves keep their own generator version.
- **Lanes never graze land:** a diagonal step past one land cell now goes round through the sea cell beside it
  (found when v5 maps put a merchant lane across a coast corner; `AuditLeadTests.NoLaneLegGrazesLand`).
- **Mangrove yards** sell only the five hulls the maze takes (X6); **co-located hulls** part across the beam (X5);
  **far traffic** feels the Sargasso drag (X8); **launch arguments** that do not parse are ignored with a warning
  instead of throwing out of `_Ready`; **stackalloc** hoisted out of per-ship draw loops (CA2014); shot and planks
  leave the hold through `Player.Remove` so the cost basis follows (R-01); the populated-sea budget test is judged at
  5 ms a tick (X1); the sea-vs-`WindAt` self-test compares inside one region.
- **App icon and boot splash** (window, taskbar, Windows exe).

## The new look
Hand-coloured antique chart: rag paper, quill wave strokes that follow the sim's own wind, portolan rhumb lines,
coastal watercolour, watercolour islands with stamped palms/jungle/volcanoes/ruins, illustrated harbour towns,
blooming fog of war, storm swirls with forked lightning, sea life; 14 plan-view hulls with in-engine sails and
ensigns; monsters as living marginalia; ink-smoke broadsides and full sinkings; a chart-ornament HUD; parchment
ledgers for the port; a ship's-log recap with a wax seal; an antique chart screen; title key art. Draw calls at sea
fell from 219 to about 30–100 depending on the scene.

## Needs Nolan
See `docs/PROGRESS.md` → "Blockers / needs Nolan".
