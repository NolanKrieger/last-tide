# AUDIT — game-core (run lifecycle, input, settings, profile, audio, strings)

Auditor: game-core · copy `~/.cache/lt-audit/game-core` · branch `game-core`.
Scope owned: `game/Main.cs`, `game/Main.SelfTest.cs`, `game/Settings.cs`, `game/Profile.cs`, `game/Audio.cs`,
`game/Platform.cs`, `game/Text.cs`, `game/Cosmetic.cs`, `assets/text/en.csv`, `project.godot`, `export_presets.cfg`,
`LastTide.csproj`, `tools/audio/build.py`, `assets/audio/*`.

Status: DONE (2026-09-23). 38 entries: 2 critical, 3 major, 17 minor, 8 polish, 6 test-harness, 1 new tool
(`--playtest`), 1 proposal — plus 9 routed to other owners (R-1…R-9; R-7 fixed here). Every fix has a self-test check (`game/Main.Audit.cs`, ids GC-n) or an xUnit test, proven to
fail on the baseline. New: `--playtest` integration mode (GC-24). Commits: `git log --oneline main..game-core`.

## Summary (most important first)
1. GC-19 critical — a voyage with any chart pin could never be saved (dock autosave, Save and quit threw).
2. GC-1 critical — a harbour rescue, and resuming any dock autosave, left the game frozen with no port screen.
3. GC-4 major — quitting mid-voyage (pause Quit, Q, window close) threw the voyage away; closing during the sinking
   left the old save to "resume" a sunk ship and lost the best.
4. GC-5 major — after the loss the last tick's events replayed every frame (75 stacked sink whirls, repeated sink sound).
5. GC-21 major (routed) — UI scale 125–150 % pushes the title, logbook page and port screen off-screen.
6. R-1 major (routed) — "New voyage" silently destroys a suspended voyage.
7. Input: keys released under a screen or while unfocused stayed held (GC-2, GC-17), broadsides fired after a pause
   (GC-3), held keys carried into the next voyage (GC-6), a focus trap in the chart's pin note (GC-26).
8. Rebinding could leave Dock unbound (GC-10); 22 strings named the default keys after a rebind (GC-11).
9. Robustness: corrupt/unwritable profile, settings and suspend files (GC-8, GC-9, GC-35); debug runs wrote into the
   player's profile (GC-15); no ObjectDB leak at exit any more (GC-13).
10. The PROGRESS open bug: the flaky self-test is reproduced and fixed (GC-22, GC-30), the self-test ignores real input
    (GC-18), and every developer-run pause now logs its source.

## Baseline (before any change)
- `dotnet build LastTide.csproj`: 0 errors, 3 × CS0108 (ChartScreen.cs:109 `ChartCanvas.Scale`, TitleScreen.cs:268 `Hide()`, TitleScreen.cs:278 `SetName(string)`).
- `dotnet test tests/Sim.Tests`: 118/118.
- `--selftest`: PASS, 106 `ok`; exit prints `WARNING: 14 ObjectDB instances were leaked at exit` (16 in the lead's run).

## Findings
(ID · severity · file:line (baseline) · what · evidence · fix · status)

Evidence convention: every GC-n self-test check lives in `game/Main.Audit.cs` (called once from `RunSelfTest`). The
first 19 checks were run against the baseline code (test hooks only) → `scratch/audit/selftest-audit-baseline.log`
(19 FAIL), and pass after the fixes → `scratch/audit/selftest-f2.log` (130 ok, PASS). The later checks (GC-1b, GC-12,
GC-17, GC-25, GC-26) were re-written against the baseline API in a throwaway probe on a baseline worktree →
`scratch/audit/selftest-probe-baseline.log`: all five FAIL on the baseline (and the baseline's own spyglass check
failed in that run too — GC-30). Current: `scratch/audit/selftest-f10.log`, 138 ok, PASS.

### GC-1 · critical · game/Main.cs:591 (TryDock is the only path that opens the port screen)
**A harbour rescue freezes the game, and so does resuming any dock autosave.** The sim docks a foundering ship itself
(`World.Combat.cs:175` `Execute(Dock)`), and a suspend save written at a dock (the crash-protection autosave, GDD §3)
restores `Docked`. In both cases `world.IsDocked` pauses the sim but nothing opens the port screen: the HUD's dock
prompt hides (`!world.IsDocked`), no key but a blind F (which re-runs Dock, incl. desertion) recovers, and the rescue
wrote no autosave. `World.OnDocked` exists but nobody subscribes.
Evidence: baseline `FAIL GC-1 a harbour rescue docks her and opens the port (docked True, port screen False)`,
`FAIL GC-1 the rescue dock writes the suspend save`, `FAIL GC-1 Esc casts off after a rescue` (Esc opened the pause menu).
Fix: `ProcessFrame` opens the port whenever the world is docked in a run and the screen is shut (`OnDocked()`: port
screen, bell, autosave; `TryDock` shares it). New check GC-1b resumes that dock save and finds the port screen up.
Status: fixed (fb5aff6).

### GC-4 · major · game/Main.cs:177, :666, (no WM close handler)
**Quitting mid-voyage threw the voyage away**, against GDD §19 "Quitting mid-run: suspend save". The pause menu's
"Quit to the desktop", Q while paused (the documented key: "Paused · Esc resumes · Q quits") and the window's close
button all quit without saving. Everything since the last dock was lost — and after a resume (which consumes the
save) the whole voyage, until the next dock. Worse, closing the window in the 2.5 s between the sinking and the recap
left the last dock's save on disk: the next launch offered "Resume voyage" for a sunk ship and the best was never logged.
Evidence: baseline `FAIL GC-4 closing the window mid-voyage writes the suspend save`, `FAIL GC-4 closing the window
while she sinks logs the voyage and drops the old save`.
Fix: `SuspendOnQuit()` (writes the save + folds achievements, or logs a lost voyage once via `RecordFinished()`) runs
from `QuitGame()` (pause Quit, Q while paused, title Quit) and from `_Notification(NotificationWMCloseRequest)`;
`FinishRun` reuses the recorded result so the voyage is logged once (`GC-4 … logged once (1)`).
Status: fixed (fb5aff6).

### GC-5 · major · game/Main.cs:495-499 (+ Audio.cs:182)
**After the ship is lost the last tick's events replay every frame.** `World.Tick` returns early once `RunOver`
(and while docked) without clearing `World.Events`, but `_PhysicsProcess` kept calling `effects.Consume()` and the hull
flash loop every physics frame, and `Audio.Update` read the list every render frame. The sinking whirl + 20 splinters
were re-added 30×/s until the recap (≈75 stacked whirls), the sink sound replayed every 0.5 s, and a pre-dock event
(e.g. the rescue bell) could replay on a no-tick frame after casting off.
Evidence: baseline `FAIL GC-5 the sinking sounds once … (3 plays)`, `FAIL GC-5 the sinking whirl is drawn once … (921
particles)`; after: 1 play, 21 particles.
Fix: effects, flashes and the new `Audio.Consume()` run only when `world.Ticks` advanced.
Status: fixed (fb5aff6).

### GC-2 · minor · game/Main.cs:615-645
**A key released while a screen is up stays held.** `_UnhandledInput` returns early for the port, chart, crew panel,
logbook, title and options, so an A/D/Q/E/RMB release there is lost: the rudder stays hard over after closing the chart,
the firing arc stays up (and the next release fires).
Evidence: baseline `FAIL GC-2 A released under the chart lets the rudder centre (-1.00)`, `FAIL GC-2 E released under
the crew panel neither sticks the arc nor fires`.
Fix: `_Input` lets go of held helm/aim/spyglass while any screen is up (before the GUI can eat it); an aim released
there is stood down, never fired. Status: fixed (fb5aff6).

### GC-6 · minor · game/Main.cs:414 (BeginRun)
**Held keys and queued orders carried into the next voyage** (e.g. D held as she sank, released under the recap: the
new voyage started with the helm hard over). Evidence: baseline `FAIL GC-6 … does not steer the next (1.00)`.
Fix: `ReleaseHeld()` in `BeginRun`, `ShowTitle`, `FinishRun`, `OnDocked`. Status: fixed (fb5aff6).

### GC-3 · minor · game/Main.cs:673-680
**A broadside key pressed and released in the pause menu fired on resume** (GDD §19: "inputs ignored" while paused);
and a Q held into the pause lost its release to the quit branch (arc stuck). Evidence: baseline `FAIL GC-3 E pressed
and released while paused does not fire on resume`.
Fix: no aim starts while paused, a release while paused stands the aim down unfired; only a Q *press* quits.
Status: fixed (fb5aff6).

### GC-7 · minor · game/Settings.cs:110-120 via Main.ApplySettings
**`--mute`, the self-test and title-mode debug runs were audible**: `Audio.Init(mute)` muted Master, then
`ApplySettings()` → `Settings.Apply` → `SetBusMute(Master, volume <= 0)` unmuted it (the self-test calls it at start).
Evidence: baseline `FAIL GC-7 a muted run stays muted after the settings apply`.
Fix: `Audio.ApplyMute()` re-applies a forced mute after every settings apply. Status: fixed (fb5aff6).

### GC-8 · minor · game/Main.cs:441-450
**A corrupt suspend save haunted the title**: "Resume voyage" pushed an ERROR and did nothing, forever (the file was
never cleared). Evidence: baseline `FAIL GC-8 …` + `ERROR: suspend save unreadable`.
Fix: `Profile.SetAsideSuspend()` renames it to `suspend.json.corrupt` (kept for diagnosis), the title refreshes
without the button; logged as a warning. Status: fixed (fb5aff6).

### GC-9 · minor · game/Profile.cs:159-176, game/Settings.cs:35-52
**Corrupt data files**: truncated/empty/garbage JSON was already caught, but JSON with nulls (`"LastLoadout": null`,
`"Keys": null`, `[null,…]`, a null best) threw `NullReferenceException` inside `_Ready` (outside the try) → the game
could not start; a width/height of 0 or −5 reached `WindowSetSize`; and an unreadable profile was silently replaced by
a fresh one on the next save, destroying the only copy of the bests.
Evidence: baseline `FAIL GC-9 … (nulls: NullReferenceException; null slot: NullReferenceException)`, `FAIL GC-9 the
unreadable profile is kept beside it`, `FAIL GC-9 … settings (nulls: NullReferenceException; silly window: 0x-5)`.
Fix: `Sanitize()` on both after load (null lists/strings/keys → defaults, unknown presets dropped, sizes/volumes/scale
clamped); an unreadable profile is copied to `profile.json.corrupt-<stamp>` before anything can overwrite it.
Status: fixed (fb5aff6). Saved-state format unchanged.

### GC-10 · minor · game/Settings.cs:74-80
**Rebinding could leave an action with no key**: binding Dock's F to another action set Dock to "None" — the ship
could no longer dock, dig or salvage, and nothing but a small "—" in Options said so. Evidence: the old self-test check
asserted exactly that (`KeyFor("Dock") == None`).
Fix: the action that held the key takes the rebound action's old key (swap). One key still holds one action (GDD §19
unchanged). The existing self-test check now asserts the swap. Status: fixed (fb5aff6).

### GC-11 · minor · assets/text/en.csv (22 rows)
**Text named the default keys after rebinding**: the HUD dock/dig/salvage prompts ("F · …", "(S)"), the gun gauge
labels (Q/E), crew orders ("1 Battle stations"…), the chart hint ("M or Esc closes"), the crew note and ten hint
cards hard-coded W/S/A/D/Q/E/F/M/L/1–4. Evidence: baseline `FAIL GC-11 the dock prompt names the rebound key
(F · Dock at …)` with Dock bound to G.
Fix: `[[Action]]` tokens in en.csv, substituted by `Text.Get` from `Settings.Current` (no caller changes; strings drawn
every frame follow a mid-voyage rebind at once; the crew panel's order buttons from the next voyage).
Status: fixed (fb5aff6).

### GC-12 · polish · game/Main.cs:385 (ShowTitle)
**A hint card showing at Save and quit stayed on screen under the title** (Hints.Update returns early while paused, so
the card never hid). Evidence: check `GC-12 a hint card showing at Save and quit is not left on the title`.
Fix: `ShowTitle` disables the hints (BeginRun re-enables). Status: fixed (fb5aff6).

### GC-13 · polish · game/Audio.cs (no exit handling)
**`WARNING: N ObjectDB instances were leaked at exit`** (8 on a title run, 14–16 on the self-test): `--verbose` lists
AudioStreamWAV + AudioStreamPlaybackWAV — the loops still playing when the tree is torn down
(`scratch/audit/title-verbose.log`). Fix: `Audio._ExitTree()` stops and detaches every player. Title run after: no
leak line (`scratch/audit/title-verbose2.log`). Status: fixed (fb5aff6 for the title; 8ca75b2 for every exit). The self-test and playtest still leaked 7–14
(one-shots mid-play at quit): the playback objects stay queued in the audio server until a mix step. Every exit now
goes through `ExitGame` (audio stopped, 4 frames for the mixer, world held still, then `Quit`), and the window's close
button is handled by Main (`AutoAcceptQuit = false`) so it takes the same path. Self-test, `--playtest` and
`--screenshot` runs now end with no leak line (`scratch/audit/selftest-f5.log`, `playtest-3.log` with `--verbose`).

### GC-14 · minor · game/Audio.cs:203
**The notice quill almost never played**: Audio compared `Notices.Count` with last frame's count, but the HUD dequeues
a new notice the same frame it arrives, so the count never dropped below what Audio had seen. Evidence: baseline
`FAIL GC-14 a notice plays the quill when the HUD shows it`. Fix: `ProcessFrame` plays it when `hud.Refresh` dequeues.
Status: fixed (fb5aff6).

### GC-15 · minor · game/Main.cs:144-147 (debug-run profile roots)
**`--pause` and `--logbook` wrote into the player's real profile** (README: debug args never touch it): `--pause` turned
Save & quit on against `user://`, `--logbook` recorded a fake 0-day voyage (Voyages+1, maybe a "best"). Debug runs
also read the player's saved key bindings (README/PROGRESS say they keep default keys).
Fix: every debug run (any argument but `--title`) uses `user://debug/` for profile and settings. Evidence: code; the
gallery runs still render (shots use a private user://). Status: fixed (fb5aff6).

### GC-16 · polish · game/Settings.cs:93-107
Window size larger than the screen (2560×1440 on 1080p) put the title bar off-screen. Fix: fit the largest listed size
inside `ScreenGetUsableRect`. Status: fixed (fb5aff6). (Profile `Best.Date` now formats with the invariant culture.)

### GC-19 · critical · src/Sim/World.cs:7-11 (Pin) — outside my files, fixed because the suspend save needs it
**A voyage with any chart pin could never be saved.** `SaveData.Pins` serialised the runtime `Pin` whose `Vec2 Pos`
System.Text.Json walks through `Normalized` (a Vec2 whose Normalized is itself) → `JsonException: A possible object
cycle was detected … Path: $.Pins.Pos.Normalized.Normalized…`. So after a player dropped one pin: every dock autosave
threw (inside `_UnhandledInput`, no save written), and "Save and quit" threw and did nothing — the voyage could only be
lost. Found when a self-test pin was left in place.
Evidence: `scratch/audit/pinsave/` console (no pins: saves 50,277 chars; with a pin: JsonException); xUnit
`AuditGameCoreTests.AVoyageWithChartPinsSavesAndLoads` fails on the baseline sim with that exception and passes after;
self-test `GC-19 a voyage with a chart pin autosaves at the dock (1 pins, save True)`.
Fix: `Pin.Pos` is `[JsonIgnore]`, saved as `X`/`Y` properties (runtime API unchanged). **Saved state:** pins now save
as `{"X":…,"Y":…,"Note":…}`; every save written by the current build has `"Pins":[]` (a save with a pin could not be
written), so all existing saves load (asserted in the test). Main also guards every suspend-save write (logged, never
thrown into input handling; Save and quit stays in the menu if the write fails). Lead: sim-sailing also edits
World.Save.cs (not World.cs); no overlap seen in their branch. Status: fixed (9bb2a25).

### GC-17 · minor · game/Main.cs (no focus handler)
**A key held while the window loses focus stays held** (alt-tab mid-turn: the ship keeps circling, an aimed broadside
fires on the next release). Godot does not deliver key-ups to an unfocused window. Evidence: `GC-17 alt-tabbing away
with D held lets the rudder centre` (fails without the handler: rudder stays +1). Fix: `NotificationApplicationFocusOut`
/ `NotificationWMWindowFocusOut` → `LetGoAll()`. Status: fixed (9bb2a25).

### GC-18 · open bug from PROGRESS · "spontaneous pause" and "flaky self-test cascade"
Findings: the only code paths that pause are an Escape *press* in `OnKey` and `--pause`; there was no focus or
notification handler, key echoes are filtered, and no mouse button pauses. In the invisible runner (no real input)
repeated self-tests right after a forced rebuild (`scratch/audit/fresh-selftests.sh`) never paused on their own
— 3 of 3 runs showed only the test's own pauses, all logged as synthetic (`scratch/audit/fresh-summary.txt`).
Run 3 did fail once: `FAIL the first-voyage hint shows and is remembered (trade)` — see GC-22, a separate timing
flake that one long frame triggers. The self-test used to run in a real window on Nolan's desktop, and Godot 4.7 gives
*every* new InputEvent the real keyboard/mouse device id, so a stray key press or mouse move while that window had focus
was indistinguishable from the test's own input — an Esc paused it, a mouse move re-aimed the spyglass check, and one
early failure cascades through the linear script. That is the most likely source of both reports; it cannot be proven
after the fact.
Fix (hardening, not a claim of root cause): the self-test stamps its events with a synthetic device id and a
`RealInputFilter` (the root's last child, so it sees `_Input` first) swallows everything else (check GC-18: a real Esc
is swallowed); every developer-run pause now logs its source (`SetPaused(True) tick=… by Escape (keyboard|synthetic)`)
instead of a stack trace, so a future unexplained pause names what caused it. Status: closed as hardened (9bb2a25).

### GC-20 · minor · game/Main.cs:273-280 (`--logbook` debug path)
The recap screenshot sank her inside the start harbour, where the rescue took her in: the gallery recap showed
"By a hair! …" on the HUD, "Earned this voyage: By a Hair", a ship still afloat (and, with GC-1, the port opening
behind it). Evidence: `~/.cache/lt-audit/gallery/before/recap.png`. Fix: sink her in open water first
(`scratch/audit/shots-after/recap.png`). Status: fixed (9bb2a25).

### GC-21 · major (for the visual agents) · UI scale 150 % (and 125 %) — screens overflow
`Window.ContentScaleFactor` scales the whole canvas: at 150 % every 16:9 resolution gives a 1067×600 logical canvas
(1280×720 logical at 125 %). Captured with the new `--uiscale=` debug argument and title-mode settings.json:
- 150 %: **title "New voyage" page overflows sideways** (Difficulty, preset cards, slot names, ship's name and version
  line cut: `scratch/audit/shots/ui150-voyage.png`); **title logbook page overflows vertically** (title and Back cut:
  `ui150-logbook.png`); **port screen** shipwright/cove cut right and bottom, tavern officer rows cut
  (`ui1.5-shipwright.png`, `ui1.5-cove.png`, `ui1.5-tavern.png`). Options (tight), home, pause, recap, chart, HUD fit.
- 125 %: port shipwright/cove still cut on the right and bottom (`scratch/audit/shots/s125/ui1.25-*.png`).
Sheets: `scratch/audit/shots/ui150-sheet.png`, `ui125-sheet.png`. Route to the visual agents owning TitleScreen /
PortScreen / LogbookScreen: every screen must fit 1067×600 logical (scroll containers for long lists). Alternative
(lead's call, changes a decided option range): cap the effective scale in `Settings.Apply`. Status: reported.

### GC-22 · minor (test) · game/Main.SelfTest.cs:342 — the flaky self-test after a fresh build, reproduced
`FAIL the first-voyage hint shows and is remembered (trade)` in 1 of 3 fresh-build runs
(`scratch/audit/fresh/run-3.log`). Hints accumulate `atSea` from the render `delta`; one long frame (first-run JIT /
shader compile) between the resume and the check pushes it past 1.5 s, so `first_sail` fires early and is marked seen
before the check's `hintsView.Bind`, and the next unseen hint (`trade`) shows instead. Fix: the check removes
`first_sail` from HintsSeen before re-binding (its precondition, now explicit). The game behaviour is fine (a hint
showing a frame early is harmless). Status: fixed (8ca75b2).

### GC-23 · minor (test) · self-test ignored logged errors
An exception thrown inside an input or frame callback (e.g. the GC-19 save exception in `_UnhandledInput`) printed
`ERROR` and the self-test still passed. `ErrorCounter` (a `Godot.Logger` registered with `OS.AddLogger` in the self-test
and playtest) counts engine/script errors; the audit section ends with `GC-23 no engine or script errors were logged`.
Status: fixed (8ca75b2).

### GC-24 · integration mode · `--playtest=DAYS[,VOYAGES]` (new, game/Main.Playtest.cs)
Plays whole voyages through the real game layer: each starts from the title's Set sail (preset cycles, blank name every
other voyage); `src/Sim/Autopilot` chooses, and its choices reach the ship only as synthetic key events through
`_UnhandledInput` (A/D held via sigma-delta, W/S taps, Q/E press-release, 1–4, L, F); docks open the port screen and
every tab is shown before the autopilot trades and casts off; the chart (a pin dropped by click), crew panel and pause
menu open along the way; at DAYS/2 it saves and quits from the pause menu and resumes from the title (hash checked);
at DAYS she goes down; the recap's New voyage starts the next. Fast-forwarded ×8 (physics 240 Hz; `Engine.TimeScale`
does not change the tick count in Godot 4). Fails on any logged error or broken expectation. Example:
`vrun.sh <copy> --path . -- --playtest=6,3 --seed=21`. Results in the Work log.

### GC-25 · minor · game/Main.cs ProcessFrame + game/Hud.cs:327 (routing note)
**Notices raised on docking were never seen.** The HUD dequeues a notice the frame it arrives and shows it for ~5 s at
the top centre (layer 10); the port screen (layer 12) covers y≈40–860 (`gallery/before/port-market.png`). So "Unpaid
hands deserted.", "The officers walked off unpaid." and the rescue's "By a hair!" timed out under the port panel.
Evidence: check `GC-25 … (held True, shown True)`; before the fix the notice was already dequeued in port
(`world.Notices` empty the frame after docking).
Fix (Main only): while the title, logbook, options, port or chart covers the HUD, pending notices stay queued; the
HUD shows them in order once she is at sea. Better (for the PortScreen owner): show dock-time notices in the port
screen's message line. Status: fixed (27ce812) + proposal.

### GC-26 · minor · game/Main.cs:634 + ChartScreen note field
**A focus trap in the chart's pin note**: clicking the chart opens a note field with focus; Esc and M were then
ignored (`!chartScreen.NoteHasFocus`), so the only ways out were Enter (keeping a pin) or clicking elsewhere.
Evidence: check `GC-26 Esc abandons the pin note, a second Esc closes the chart`. Fix: `_Input` (before the LineEdit)
turns Esc into "abandon this pin". Status: fixed (27ce812).

### GC-27 · polish · tools/audio/build.py → assets/audio (21 one-shots)
Five cues stopped mid-waveform: last sample kraken −0.050, ghost +0.028, weed −0.085, eruption −0.028, gust −0.042 of
full scale (a click at the end of every play); the crocodile had a −0.023 DC offset. Evidence: the analysis in
`scratch/audit/` (seam/tail table in the Work log). Fix: `finish()` for every one-shot (remove DC, 3 ms fade in,
40 ms fade out) before normalising; loops untouched — the 11 loop files are byte-identical after regenerating (seeded
RNG), the 21 one-shots now start and end at 0.0000. Status: fixed (27ce812).

### GC-28 · polish · project.godot
The boot splash was Godot's default grey screen with the Godot logo. Now plain parchment
(`boot_splash/bg_color` = the clear colour, `show_image=false`). Visual agents may want a logo there later.
Status: fixed (27ce812).

### GC-29 · test · self-test string coverage
New check: every table row the game names (goods, hulls, regions, factions, port results, stock words, parts and
effects, uniques, officers × tiers, tiers, rigs, monsters + their notices + causes, achievements ×3, cosmetic slots and
options + unlock notices, presets, crew orders, key actions, hint cards, logbook rows: 279 keys) has a string, and
every `[[Action]]` token resolves. Passes today; guards the next content addition. Status: added (08f57b5).

### GC-30 · minor (test) · game/Main.SelfTest.cs:278 — flaky "the spyglass sees 700 m along the cone"
The check aims the cursor 700 m ahead (3,500 px off-screen) right after the ship was teleported to open water, while
the camera is still easing after it (it covers ~5% of the gap per frame): the world point under the cursor drifts by
the camera's travel and can leave the 20° cone. Failed once in my runs and once on the untouched baseline
(`selftest-f9.log`, `selftest-probe-baseline.log`). Fix: snap the camera to the ship and wait two frames before
aiming. Status: fixed (08f57b5).

### GC-31 · polish · assets/text/en.csv — spelling
The text is British (harbour ×6, colours ×4, grey) with three Americanisms: "rumor/rumors", "crueler",
"Colorblind-safe". Now rumour/rumours, crueller, "Colour-blind-safe colours". Status: fixed (08f57b5).

### GC-32 · minor · assets/text/en.csv HINT_leak
"Water in the hold: 4 puts hands on the pumps, 3 on repairs." — wrong for the sloop the hint fires on: Balanced (4)
fills sails then guns then repair, so a crew of 4 with 2 guns has **no** hand at the pumps (`Ship.Stations`,
`src/Sim/Ship.cs:145-151`; the HUD reads "4 Balanced · guns 2 · sails 2 · repair 0 · pumps 0"). Order 3 is
"Repair & pump". Now: "[[Order3]] sends hands to repairs and the pumps." Status: fixed (08f57b5).

### GC-33 · polish · assets/text/en.csv — unused rows
Never read by any code: `PORT_MARKET_HEAD` (replaced by PORT_COL_*), `PORT_SHIPWRIGHT_NOTE` ("…full ledger (later)",
stale since M8), and lowercase duplicates `PRESET_calmseas/roughseas/tempest` (code uses the upper-case rows).
Removed (507 rows). Placeholders vs arguments: all 157 static `Text.Get("KEY", …)` calls pass exactly the
placeholder count; no malformed CSV rows (every row 2 cells). Status: fixed (08f57b5).

### GC-34 · polish · game/Audio.cs:211-219 + tools/audio/build.py
The Siren fell to the switch's default and arrived with the Ghost Ship's moan. build.py now synthesises
`monster_siren` (a sung call gliding C5→G5 with a breath under it), generated last so the 32 existing files stay
byte-identical (checked with `cmp`); `assets/audio/monster_siren.wav.import` = keep (exports). Evidence: check
`GC-34 the Siren's arrival plays monster_siren, not the Ghost Ship's cue`. docs/LICENCES.md says "32 loops and
one-shots" (now 33) — outside my files, lead to adjust. Status: fixed (cce3f06).

### GC-35 · minor · game/Profile.cs:236-243, game/Settings.cs:54-62
Profile and settings writes threw on an unwritable user folder (read-only or full disk; on Windows a file held open
by a sync client makes `File.Move` throw): `TitleScreen.PressSetSail` saves the profile before starting the voyage, so
Set sail would silently do nothing; a hint showing (`MarkHint`) or the recap would throw mid-frame; an options change
would not apply. Evidence: `GC-35 an unwritable user folder is reported, not thrown` (chmod'ed folder; .NET throws
UnauthorizedAccessException there on the baseline code). Fix: `WriteAtomic` catches, logs a warning, removes its
temp file and returns false; `Settings.Save` uses it; `Main.WriteSuspend` uses the result. Status: fixed (cce3f06).

### GC-36 · proposal (sim, for sim-sailing / lead) · src/Sim/World.Save.cs:119-120, 263-264
The suspend save carries the whole input and command logs (for replays), so it grows with every helm tap: measured in
`playtest-6.log` 76 KB at day 0 → 1.36 MB at day 7 → 2.1 MB at day 12 (15,211 input entries). Write time stays small
(≤ 18 ms), so nothing breaks, but a 30-day voyage writes ~5 MB on every dock (and Steam Cloud syncs it). Proposal: keep
the logs out of the suspend save (resuming needs only the state; the replay tests build their own logs), or cap them.
Status: proposed.

### GC-37 · test · long / non-Latin ship names
"Ψυχή 海の星 🚢 «Wren» of the Very Long Name" through the real name field: cut to the field's 24 characters (an
emoji counts once; C# `Length` would say 25), kept through Set sail, Save and quit, Resume and the recap title. The
IM Fell font has no Greek/CJK/emoji glyphs; Godot's system-font fallback draws them (runner and desktops have one).
Status: check added (b57ccc4).

### GC-38 · minor · game/Main.cs StartVoyage (from sim-trade R-09, routed by the lead)
`World.UnlockCosmetic` draws from cosmetics not in the voyage's `Player.Cosmetics`, which started empty, so a dig's
20 % cosmetic roll could "unlock" (and toast) a colour the profile already owned — after a few voyages most rolls were
duds. Fix: `StartVoyage` seeds `Player.Cosmetics` from `profile.Cosmetics` (`RecordRun` still records only new ones;
the suspend save carries the set). Evidence: `GC-38 a dig's cosmetic roll unlocks one the profile lacks` (profile owns
14 of 15; the roll must pick the 15th). **Saved state:** a new voyage's save now lists the profile's cosmetics
(same field, same format). Status: fixed (b57ccc4).

### GC-30 (extended, lead's R-11) · the self-test under load
Other flaky checks waited a fixed number of frames against real-time clocks: "the first-voyage hint shows" (at a few
fps 60 frames outlast the 9 s hint), "setting sail slaps the rigging" / "a broadside booms" (another one-shot lands
last), "the ship abeam takes the broadside" (under load the ball lands during `Frames(2)`, before the hull was read —
failed once in `selftest-load-b.log`), and my own sinking checks (the recap could open inside `Frames(45)`). Now: an
`Until(condition, maxFrames)` helper; the camera settles before the spyglass aims; the hint is awaited; one-shots are
counted by name (`Audio.Plays`); the target's hull is read before the shot; the sinking checks wait on RunOver and the
recap. Evidence: three concurrent self-tests at load average ~19: 3/3 PASS (`scratch/audit/selftest-load2-*.log`).
Status: fixed (b57ccc4).

## Bugs in other agents' files (not edited; routed to the lead with evidence)

- **R-1 · major · game/TitleScreen.cs:65, :279 (+ Main.StartVoyage) — "New voyage" silently destroys a suspended
  voyage.** With a save on disk the title offers both Resume and New voyage. A new voyage's first dock overwrites the
  one suspend slot, and if the new ship sinks first `FinishRun` deletes it — the saved voyage is gone without a word.
  Repro: Save and quit → New voyage → Set sail → dock (or sink) → the title no longer offers the old voyage.
  Proposal: when `profile.HasSuspend`, Set sail asks "Abandon the saved voyage?" (or New voyage is disabled while a
  save exists). Needs the title owner + a one-line design decision.
- **R-2 · major (visual) · TitleScreen / PortScreen / LogbookScreen — UI scale 125–150 % overflows** (GC-21 above, with
  screenshots). Every screen must fit a 1067×600 logical canvas at 150 %.
- **R-3 · minor · game/Hud.cs:335 — notice timer counts frames, not seconds**: `noticeTime -= 1.0 / 60` per
  `Refresh` call (once per render frame): a notice lasts 5 s at 60 fps, 2.1 s at 144 Hz, 10 s at 30 fps (and the
  queue waits on the same clock). Fix: pass `delta` into `Hud.Refresh` (Main already has it).
- **R-4 · polish · game/PortScreen.cs, ChartScreen.cs, CrewPanel.cs — silent buttons**: title, pause, options and
  logbook buttons click (`Audio.Ui()`), the port's tabs/buy/sell/hire/repair, the crew panel's orders and the chart
  don't; a trade makes no sound. Proposal: `Audio.Ui()` on every button, and `Audio.Instance?.Play("coins", 0.6)` on
  a successful Buy/Sell/BuyPart/BuyHull in `PortScreen.Do`.
- **R-5 · polish · TitleScreen.cs:129 — Enter in the ship's-name field does nothing** (`TextSubmitted` not connected);
  and no menu grabs focus, so arrow keys/Enter never work until the mouse clicks a button.
- **R-6 · minor · compiler warnings CS0108** (baseline, unchanged): `ChartScreen.cs:109 ChartCanvas.Scale` hides
  `Control.Scale` (a tween or layout code touching `Scale` on the canvas gets a float, not the Control's Vector2);
  `TitleScreen.cs:268 Hide()` hides `CanvasLayer.Hide()` (a `CanvasLayer`-typed call hides the whole layer, not the
  root — two different behaviours behind one name); `TitleScreen.cs:278 SetName(string)` hides `Node.SetName`.
  Proposal: rename (`MapScale`, `Close`, `SetShipName`).
- **R-7 · minor · the Siren's appearance played the Ghost Ship's cue** (Audio default branch). Fixed on my side with a
  dedicated `monster_siren` cue (see GC-34).
- **R-8 · polish · project.godot / export_presets.cfg — no application icon**: `config/icon` is unset (window and
  taskbar show Godot's icon) and the Windows preset has `application/icon=""`. Needs an icon asset (visual agents /
  art pass); then set both.
- **R-9 · test flake (sim) · tests/Sim.Tests/AiTests.cs:196 `ThePopulatedSeaTradesAndKeepsItsBudget`** asserts 240 s
  of sim in < 12 s of wall clock: failed with "240 s of sim took 15.8 s" while a Godot run shared the box (load ~19),
  passed alone and in the next full runs (`scratch/audit/dotnet-test-final.log` vs `-final3.log`). Proposal: measure the
  thread's CPU time, or give the budget headroom for a loaded CI box.

## Notes for the merge (read before applying this branch)
- Shared files: `game/Main.cs` (most changes), `game/Main.SelfTest.cs` (small local edits only: `Push` stamps the
  synthetic device id, an `Until` helper, the rebinding check asserts the swap, the `first_sail` precondition, four flaky
  checks wait on conditions, one `await AuditChecks(...)` line, `ExitGame` at the end), `assets/text/en.csv` (values of
  ~27 rows, 5 unused rows removed, none added), `src/Sim/World.cs` (the `Pin` class only), `README.md` (two lines).
- **Self-test input must go through `Push`/`Key`/`Tap`** (they stamp `RealInputFilter.Synthetic`); Godot 4.7 gives a
  new InputEvent the real keyboard/mouse device id, so a raw `GetViewport().PushInput(...)` in a new check is
  swallowed as stray real input. No copy has such a call today.
- **Quit through `QuitGame()` / `ExitGame(code)`**, never `GetTree().Quit()`: `AutoAcceptQuit` is off so the window's
  close button writes the suspend save, and the exit stops the audio first.
- Text: name keys with `[[Action]]` tokens (e.g. `[[Dock]]`), resolved by `Text.Get` from the current bindings.
- The port screen follows the world (Main opens it when docked, closes it when not); notices wait while the port,
  chart, title, logbook or options cover the HUD.
- Saved state: `Pin` saves as X/Y/Note (every existing save has `"Pins":[]`, so all load; xUnit asserts it); a new
  voyage's save lists the profile's cosmetics. Profile and settings formats unchanged.

## Tests run (final, 2026-09-23 ~14:45)
- `dotnet build LastTide.csproj`: 0 errors, 3 warnings (the baseline CS0108s, R-6).
- `dotnet test tests/Sim.Tests`: **119/119** (118 + `AuditGameCoreTests.AVoyageWithChartPinsSavesAndLoads`)
  (`scratch/audit/dotnet-test-final3.log`; one earlier run hit the wall-clock flake R-9 under load).
- `--selftest` (editor build, invisible runner): **PASS, 141 `ok`**, no ERROR, no leak line
  (`scratch/audit/selftest-final.log`); ×3 concurrently at load ~19: 3/3 PASS.
- Exported Linux build `--selftest`: PASS 141 ok; Windows build under Proton 11: PASS 141 ok.
- `--playtest`: `playtest-4.log` (3 × 6 days), `playtest-6.log` (2 × 12 days, a real loss to the crocodile),
  `playtest-7.log` (2 × 3 days, final build): all PASS, 0 errors, 0 warnings.
- Gallery (28 standard screens) re-captured with the current build: 28/28, no errors
  (`scratch/audit/gallery-after-sheet.png`).

## Work log
- baseline recorded.
- batch 1 committed (fb5aff6): GC-1…GC-16; self-test 130 ok PASS (`scratch/audit/selftest-f2.log`).
- batch 2 (9bb2a25): GC-17…GC-21; batch 3 (8ca75b2): GC-22…GC-24 + clean exit; self-test 134 ok PASS, no leak line;
  `dotnet test` 119/119 (118 + AVoyageWithChartPinsSavesAndLoads).
- batch 4 (27ce812): GC-25…GC-28; self-test 136 ok PASS.
- batch 5 (08f57b5): GC-29…GC-33; self-test 138 ok PASS; full 28-shot gallery re-captured with the current build:
  28/28, no ERROR/leak in any log (`scratch/audit/gallery-after/`, sheet `gallery-after-sheet.png`).
- batch 6 (cce3f06): GC-34, GC-35; self-test 140 ok PASS.
- batch 7 (b57ccc4): GC-37, GC-38, R-11 flakes; self-test 141 ok PASS ×3 concurrently under load.
- GC-35 made portable (no chmod: `File.SetUnixFileMode` throws on Windows). Exports from my copy: Linux
  (`scratch/audit/build/linux`, self-test inside the binary: PASS 141 ok, `selftest-exported-linux.log`) and Windows
  under Proton 11 with a private prefix (`scratch/audit/vwin-gc.sh`: PASS 141 ok, log in
  `scratch/audit/protonprefix/…/Last Tide/logs/godot.log`). Export templates are found through a symlink from the
  private XDG_DATA_HOME to `~/.local/share/godot/export_templates/4.7.2.stable.mono`.
- `playtest-6.log` (2 voyages × 12 days, seed 31): PASS, 0 errors/warnings. Voyage 1 was really lost to the Giant
  Crocodile on day 8.51 (11 docks, 8 trades, 6 upgrades, 6 flights); voyage 2 reached day 12 (10 docks, Threat tier 3).
- a saved 2560×1440 window on the 1920×1080 runner opens at 1920×1080 (`shots/big-window.png`).
- playtests (all PASS, 0 errors): `playtest-1.log` 2 voyages × 1 day; `playtest-4.log` 3 voyages × 6 days (seed 21:
  docks 5/5/3, resumes 1 each, pins, monsters 5 in voyage 2); `playtest-5.log` 1 × 4 days with the per-dock log
  (trades happen; the "trades 0" in earlier summaries was the counter resetting with the captain on resume).
