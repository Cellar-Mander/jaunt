# In-game smoke test

Run before merging anything that changes what the plugin does in-game. Each feature adds its cases here.

Setup: deploy with `dotnet buildingway dev-plugin deploy <task-ref>` (see "Testing in-game" in [CONTRIBUTING.md](../CONTRIBUTING.md)), log in, open `/xllog`. Release the dev folder with `dotnet buildingway dev-plugin release` when done.

## Loading

1. `/xllog` shows `Jaunt <version>+<commit> loaded`, with this checkout's commit.
2. `/jaunt` opens the settings window; running it again closes it. `/jaunt config` opens it and leaves it open when run again.
3. `/xlplugins` → Installed Plugins shows a settings button for the plugin, which opens the same window.
4. Change a setting, disable and re-enable the plugin: the setting is kept.

## Settings

Checks only what the Core tests can't reach: Dalamud wiring, ImGui behaviour, saving in-game and the
cache file. The rules themselves (cooldown range and per-source minimums, date and time formats,
expiry) are covered by `Jaunt.Core.Tests`.

1. **Layout**: Data, Display and Listings are collapsible headers, in that order. Each (?) shows a
   tooltip on hover. At a larger font scale (`/xlsettings` → Look & Feel → Global Font Scale),
   nothing is clipped.
2. **Cooldown controls**: − [number] +, with the label above. − and + step 5 minutes, or 60 with
   Ctrl held. Typing 5 gives 10.
3. **Settings survive a reload**: change the cooldown, both formats (the "Now:" line changes at
   once) and collapse a section. Disable and re-enable the plugin: all four are kept.
4. **Clear confirmation**: move the Settings window off-centre, then press Clear cached listings.
   The confirmation opens centred on the Settings window. Cancel and the title bar's close button
   remove nothing; Clear reports how many it removed.
5. `/xllog` shows no errors from Jaunt throughout.

### With the sample cache

[`smoke-test-listings.json`](smoke-test-listings.json) holds eight listings: two unmarked, one
favourite and one visited, two hidden, plus an old unmarked listing and an old hidden one-off that
expire on load. Its dates are 2000 or 2099, so the results below hold until 2099.
`SmokeTestListingsTests` keeps the file and this list in step.

Loading the plugin rewrites `listings.json`, so copy the sample in again before each run:

1. Disable Jaunt in `/xlplugins`.
2. Copy `docs/smoke-test-listings.json` to the plugin's config folder as `listings.json`
   (`~/.xlcore/pluginConfigs/Jaunt/` on Linux, `%AppData%\XIVLauncher\pluginConfigs\Jaunt\` on Windows).
3. Enable Jaunt and open Settings.

Then:

1. **Expiry on load**: Hidden venues lists "Another hidden" then "Hidden lounge". "Hidden gala
   (one-off)" is not listed: marked one-offs expire.
2. **Clear**: confirm it. It reports "Removed 2 listings." Hidden venues is unchanged.
3. **Un-hide**: press Un-hide on "Another hidden". Only "Hidden lounge" is left. Disable and
   re-enable Jaunt: still only "Hidden lounge".
4. **Clear again**: it reports "Removed 1 listing." (the venue just un-hidden).
5. **Cache file**: `listings.json` still has `"FfxivVenues"` under `lastFetched`, and `/xllog`
   shows no error saving it.
