# Steam: what is wired, what Nolan owns

## In the build today
- `game/Platform.cs`: `IPlatform` seam with `NoPlatform` (achievements stay in `user://profile.json`) and a
  `SteamPlatform` behind `#if STEAM`. `Profile.RecordRun` calls `Platform.Current.Unlock(key)` for every new
  achievement, so the store mirrors the profile with no other code touched.
- Achievement keys = `Achievements.All` in `src/Sim/Achievements.cs` (17). Steam API names are the same keys
  upper-cased: `FAIR_WINDS`, `HEAVY_WEATHER`, `EYE_OF_THE_STORM`, `SERPENT_SLAYER`, `UNKRAKENED`, `LAY_THE_GHOST`,
  `HANDBAGS`, `WEEDED_OUT`, `DEAF_EARS`, `NINE_SEAS`, `CARTOGRAPHER`, `SMUGGLERS_WELCOME`, `X_MARKS`,
  `GALLEON_CAPTAIN`, `SHIP_OF_THE_LINE`, `BY_A_HAIR`, `LAST_TIDE`. Names and descriptions for the store are the
  `ACH_<key>_NAME` / `ACH_<key>_DESC` rows of `assets/text/en.csv`.
- Cloud saves: everything persistent is two files under Godot's `user://` (`profile.json`, `suspend.json`,
  plus `settings.json` which should NOT sync). Steam Auto-Cloud handles that with no code: root
  `Linux: ~/.local/share/godot/app_userdata/Last Tide`, `Windows: %APPDATA%/Godot/app_userdata/Last Tide`,
  pattern `profile.json` and `suspend.json`.
- Exports: `export_presets.cfg` has `Linux` and `Windows` presets (x86_64, GL Compatibility, PCK beside the
  binary, `assets/audio/*.wav` + `assets/text/*.csv` force-included because they are read at runtime).
  `godot --headless --path . --export-release "Linux" build/linux/LastTide.x86_64`, same for `"Windows"`.

## Nolan's steps (money and accounts; nothing here is done by the agent)
1. Steamworks partner account + Steam Direct fee → App ID.
2. Store page: name, price (open decision), capsule art, screenshots, AI-content disclosure from `docs/AI-ASSETS.md`.
3. Achievements: create the 17 above in Steamworks → Stats & Achievements with the API names listed.
4. Auto-Cloud: add the two file patterns above in Steamworks → Application → Steam Cloud.
5. Turn on the binding: add the Steamworks.NET package (`dotnet add LastTide.csproj package Steamworks.NET`),
   drop `libsteam_api.so` / `steam_api64.dll` beside the exported binary, write `steam_appid.txt` with the App ID
   for local tests, and export with `-p:DefineConstants=STEAM`. Verify with `--selftest` (achievements still land
   in the profile) and one manual unlock visible in the Steam overlay.
6. Upload builds with SteamPipe (Linux and Windows depots), set the launch options, and ship.
