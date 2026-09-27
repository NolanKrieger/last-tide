# Last Tide — project rules

A roguelite sailing/trading/combat game drawn as an ink-and-parchment sea chart. Godot 4.7.2 .NET, C#. Goal: the complete, polished game on Steam.

## Read first
1. `docs/GDD.md` — the design and source of truth; §19 is the decisions log. Every design question is answered; remaining **(proposal)** tags are v1 tuning values.
2. `docs/PROGRESS.md` — where the build stands, evidence, what is next, blockers, bug list. Update it at every milestone and before stopping.
3. `README.md` — run, test, layout.
4. `~/.claude/workspace/tools/languages.md` → "Godot 4.7.2 .NET" (toolchain gotchas); `tools/codex.md` → "Codex built-in image generation" (the art route).

## Architecture (keep it)
- All rules live in `src/Sim`: plain C#, **no Godot types**, fixed 30 Hz tick, seeded RNG per run, deterministic. Ship physics is a custom 2D rigid body in the sim, never Godot physics. The Godot layer (`game/`) only reads state and issues commands.
- Coordinates: metres, +X east, +Y south, angles clockwise from +X (Godot's convention, so the view needs no flips). Compass heading = math angle + 90°.
- Content is data tables in `src/Sim` (`Hulls`, `Polar`, goods, parts, officers, regions, monsters, spawn cards, achievements), never switch statements.
- Every sim feature gets xUnit tests in `tests/Sim.Tests`. Every control gets a `--selftest` check through the real input path (`PushInput`). The determinism/replay tests must keep passing. Map generation validates across 1,000 seeds.
- Player-facing text lives in `assets/text/en.csv` (`Text.Get("KEY")`). English only.
- Fonts SIL OFL only, licence beside each font. Audio: no music; ambience + SFX from CC0 or self-made sources, logged in `docs/LICENCES.md`. Every AI-generated asset goes in `docs/AI-ASSETS.md` (Steam disclosure). No Poptropica names, characters, art, music or UI anywhere.

## How to work
- Follow GDD §18 M0→M11 in order. Don't cut scope: Nolan chose "everything". **2026-09-23: Nolan lifted the M1 gate ("just finish the whole game perfectly and polished then we can iterate if things feel off"): build everything to a polished finish, then iterate on his feedback.**
- Tune **(proposal)** values freely and record meaningful changes in GDD §19. Never change a decided rule without asking.
- Verify before claiming: `dotnet test tests/Sim.Tests` green; `godot --path . -- --selftest` PASS; a screenshot via `-- --screenshot=... --frames=N` looked at with the Read tool; measured FPS for performance claims. Never report done without that evidence.
- Run `godot` and `dotnet` **non-sandboxed**. Screenshots need the real display (the window pops up briefly; KWin tiles it).
- Balance with evidence: the headless autopilot captain plays thousands of runs per preset (GDD §11 day targets). Nolan's playtests have the final say on feel.
- Performance target (GDD §17): 60 fps with ~40 ships near the player plus coarse-tick traffic on a 35–40-port map, on this PC. Profile before optimizing.

## Needs Nolan (stop and ask, one line each)
- M1 sailing-feel sign-off. Style-bible approval before mass art generation; any change to look or feel he hasn't seen.
- Anything that costs money (Steamworks fee, paid assets, paid compute). The Steam price point.
- Git: not under version control yet. Ask about `git init` + private GitHub repo. **Never commit, push or publish without his explicit yes.**
- Steam store page, builds, uploads: anything outward-facing.

## Nolan's rules
- Replies: no fluff. Answer or action first, 1–5 short lines, exact paths/numbers/commands. No preamble, recaps, offered alternatives. Reasoning only when asked.
- Keep to this project; don't bring up or compare his other projects.
- He has no Godot experience: give exact clicks when he must do something in the editor.
- Never `pkill -f` / `pgrep -f` a pattern in your own command line. Collect PIDs, then `kill`.
- Don't restart or kill processes you didn't start.
- Honesty over optimism: report the actual error.
