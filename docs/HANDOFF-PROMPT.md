# Handoff: build Last Tide

You are taking over **Last Tide**, Nolan Krieger's roguelite sailing game. The goal is the **whole game, complete and polished, ready for a Steam release**: every system in the design doc, balanced and bug-free, with finished art, audio, UI and performance. Work until it is done. This will span many sessions, so keep a written record of progress as you go (see "Continuity").

## Read first, in this order

1. `~/last-tide/docs/GDD.md`: the design. It is the source of truth, and §19 is the decisions log. Every design question is already answered. The remaining **(proposal)** tags are v1 tuning values, not open questions.
2. `~/.claude/workspace/tools/languages.md` → "Godot 4.7.2 .NET": toolchain setup, the C# project shape that works, headless build/test, scripted screenshots, synthetic input.
3. `~/.claude/workspace/tools/codex.md` → "Codex built-in image generation": the art route, including the transparent-PNG flow.

## Where things stand (2026-09-23)

- **Design only. No code exists.** `~/last-tide/` contains just `docs/GDD.md` and this file.
- **Toolchain is installed:** `godot` (wrapper `~/.local/bin/godot`, Godot 4.7.2-stable mono) and the .NET 8 SDK at `~/.dotnet`. Run godot and dotnet commands **non-sandboxed**. Screenshots need the real display: the window pops up briefly and Nolan's KWin tiler resizes it.
- **The game is original IP.** It's inspired by the sailing part of Poptropica's Skullduggery Island, but no Poptropica names, characters, art, music or UI may appear anywhere.

## How to work

- **Follow the GDD roadmap (§18) M0→M11 in order.** Each milestone's "done when" is its definition of done. Don't cut scope: Nolan chose "everything".
- **M1 (sailing feel) is gated on Nolan.** When it's built, give him the exact launch command and what to try. Iterate on his feedback until he says it feels right, and don't start M2 before that.
- **Tuning values:** tune the **(proposal)** values freely and record any meaningful change in GDD §19. Don't change a decided rule without asking him.
- **Architecture (GDD §17), keep it:**
  - All rules live in `src/Sim`. It is plain C# with **no Godot types**: fixed 30 Hz tick, seeded RNG per run, deterministic.
  - Ship physics is a custom 2D rigid body in the sim, not Godot physics.
  - The Godot layer (`game/`) only reads state and issues commands.
  - Content is data tables in `src/Sim`, never switch statements: 27 goods, 14 hulls, 9 parts × 5 grades, officers, 9 regions, 7 monsters, spawn cards, achievements.
- **Tests:**
  - Every sim feature gets xUnit tests in `tests/Sim.Tests`.
  - Every control gets a `--selftest` check through the real input path (`PushInput`).
  - A determinism test must replay a command log to the same state hash.
  - Map generation must validate across 1,000 seeds: every port reachable, and a profitable start route.
- **Balance with evidence, not feel alone.** Build a headless **autopilot captain** that plays full runs in the sim (trading, fighting or fleeing, docking to repair). Run thousands of runs per difficulty preset and check that:
  - the days-survived distribution matches GDD §11: good runs on Calm Day 25+, Rough Day 15–20, Tempest Day 10–12;
  - no single trade route or hull dominates;
  - the lean start (200 gold, 4 crew) is survivable.

  Nolan's playtests have the final say on feel.
- **Verify before claiming anything:**
  - Build and test green.
  - Self-test PASS.
  - A screenshot from `-- --screenshot=… --frames=N`, looked at with the Read tool.
  - Measured FPS for performance claims.
  - Never report done, fixed or working without that evidence.
- **Performance target (GDD §17):** 60 fps with ~40 ships active near the player plus coarse-tick traffic across a 35–40-port map, on this PC (i7-9700K, RTX 2070 SUPER). Profile before optimizing.

## Polish bar (what "complete" means beyond the GDD systems)

- **Onboarding.** The GDD has no tutorial, but sail trim and tacking aren't obvious. Design a light first-voyage onboarding that fits the ink-and-parchment style, such as hints inked onto the chart. Record it in §19.
- **Options:**
  - key rebinding (keyboard + mouse only; no gamepad by decision)
  - master/ambience/SFX volume
  - resolution, fullscreen, UI scale
  - colorblind-safe faction and threat colors
- **Game feel:** juice on every hit, splash, sail change, dock and sinking.
- **Readability at speed:** wind, sail level, hull, water and Threat always legible.
- **Recap:** the logbook recap page looks like a real ship's log.
- **Suspend save** survives a crash mid-voyage (it autosaves on dock).
- **Zero known bugs** at release. Keep a bug list in PROGRESS.md and close it out.
- **Builds:** export Linux and Windows builds. Verify the Windows build runs under Proton/Wine here.

## Art, audio, text

- **Art: Codex `image_gen`** (Nolan's choice). Drive Codex non-interactively from your session, and prove one generation works before planning around it.
  - Pipeline: style bible → fixed prompt template → transparent PNGs → normalized scale → contact-sheet review.
  - Look (GDD §15): parchment sea with ink wave strokes, top-down ink-and-watercolor ships rotated in-engine, sails as a separate layer with 4 states, hachure-shaded chart islands, and monsters drawn as sea-chart marginalia.
  - **Nolan must approve the style bible before mass generation.**
  - Log every AI-generated asset in `docs/AI-ASSETS.md` for Steam's AI-content disclosure.
- **Audio: no music anywhere** (decided). The game is pure ambience plus SFX:
  - wind scaled to wind speed, waves, timber creak, rigging slap
  - rain and thunder, gulls near ports, harbor bustle and bells
  - beat-to-quarters drums, cannon fire, splintering, water rushing in, pumps
  - monster cues

  Sources must be CC0 or self-made/synthesized. Log each source's licence in `docs/LICENCES.md`.
- **Fonts:** SIL OFL only, with the licence file beside each font.
- **Text:** English only. All player-facing text lives in string tables.

## Needs Nolan (stop and ask, one line each)

- M1 sailing-feel sign-off (see above).
- Style-bible approval, and any change to how the game looks or feels that he hasn't seen.
- Anything that costs money: the Steamworks account/app fee, paid assets, paid compute.
- The Steam price point (not in the GDD).
- **Git:** the project is not under version control. Ask whether he wants `git init` plus a private GitHub repo. **Never commit, push or publish without his explicit yes.**
- Steam store page, builds or uploads: anything outward-facing.

## Nolan's rules (these override defaults)

- **Replies: no fluff.** Give the answer or action first, in 1–5 short lines, with exact paths, numbers and commands. No preamble, recaps or offered alternatives. Reasoning only when asked.
- **Keep to this project.** Don't bring up, compare with, or schedule against his other projects.
- He has **no Godot experience**. When he has to do something in Godot, give the exact clicks.
- **Never** run `pkill -f` or `pgrep -f` on a pattern that is in your own command line; it kills your own shell. Collect PIDs and `kill` them.
- Don't restart or kill processes you didn't start.
- Honesty over optimism: when a tool errors, report the actual error.

## Continuity

- **First action:** create `~/last-tide/CLAUDE.md` with the project rules above (short), so every future session in this folder loads them automatically. Also create `README.md` covering how to run, test and navigate the code.
- Keep `~/last-tide/docs/PROGRESS.md` current. Record the milestone, what is done with its evidence, what is next, blockers, the bug list, and decisions. Update it at every milestone and before stopping. A new session must be able to resume from it alone.
- Record durable decisions in GDD §19, and toolchain discoveries in `~/.claude/workspace/tools/languages.md`.
