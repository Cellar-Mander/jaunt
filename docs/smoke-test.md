# In-game smoke test

Run before merging anything that changes what the plugin does in-game. Each feature adds its cases here.

Setup: deploy with `dotnet buildingway dev-plugin deploy <task-ref>` (see "Testing in-game" in [CONTRIBUTING.md](../CONTRIBUTING.md)), log in, open `/xllog`. Release the dev folder with `dotnet buildingway dev-plugin release` when done.

## Self-tests

To run the checks that need no look at the screen and no click, open Dalamud's self-test window (`/xldev`, Dalamud menu, Open Self-Test), pick the `Jaunt` group and press play. Each check is its own step, so the table shows which passed or failed, and Jump runs any one on its own. Each step logs `Self-test PASS: <step>` or `Self-test FAIL: <step> (<detail>)`; on Linux, `grep 'Self-test' ~/.xlcore/logs/dalamud.log | tail` shows the result.

The steps work on scratch copies: the cache steps use a `self-test` folder in the plugin's config folder, deleted afterwards, and never your real cache files; the FFXIV Venues, xivpf, Partake and RP Event Calendar steps use a stand-in for the site and a clock they move, so they make no request. The settings step saves a test object through Dalamud, then saves your settings again. Three warnings in `/xllog` are expected, announced by `Self-test: … expected` lines just before them.

Checks marked *(self-test)* need nothing more; those marked *(self-test: …)* are covered only for the part named, so finish them by hand. Unmarked checks stay manual: how things look, clicks, reloading the plugin and the real network.

## Loading

1. `/xllog` shows `Jaunt <version>+<commit> loaded`, with this checkout's commit.
2. `/jaunt` opens the venue list; running it again closes it. `/jaunt config` opens the settings window and leaves it open when run again. *(self-test)*
3. `/xlplugins` → Installed Plugins shows a settings button for the plugin, which opens the same window.
4. Change a setting, disable and re-enable the plugin: the setting is kept. *(self-test: Dalamud's save and load; the reload stays manual)*

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

[`smoke-test-listings.json`](../Jaunt.Core.Tests/Fixtures/smoke-test-listings.json) holds eight listings: two unmarked, one
favourite and one visited, two hidden, plus an old unmarked listing and an old hidden one-off that
expire on load. Its dates are 2000 or 2099, so the results below hold until 2099.
`SmokeTestListingsTests` keeps the file and this list in step.

The sample is in the single `listings.json` earlier versions saved, which loading moves to the
current files once, so copy it in again before each run:

1. Disable Jaunt in `/xlplugins`.
2. In the plugin's config folder (`~/.xlcore/pluginConfigs/Jaunt/` on Linux,
   `%AppData%\XIVLauncher\pluginConfigs\Jaunt\` on Windows), move `marks.json`, `marks.json.bak`,
   `fetch-state.json` and the `cache` folder somewhere safe if you want to keep them, then copy
   `Jaunt.Core.Tests/Fixtures/smoke-test-listings.json` there as `listings.json`.
3. Enable Jaunt and open Settings.

Then:

1. **Expiry on load**: Hidden venues lists "Another hidden" then "Hidden lounge". "Hidden gala
   (one-off)" is not listed: marked one-offs expire. *(self-test: which listings expire; look at the list yourself)*
2. **Clear**: confirm it. It reports "Removed 2 listings." Hidden venues is unchanged. *(self-test: the count)*
3. **Un-hide**: press Un-hide on "Another hidden". Only "Hidden lounge" is left. Disable and
   re-enable Jaunt: still only "Hidden lounge". *(self-test: the cache after a save and reload; press Un-hide yourself)*
4. **Clear again**: it reports "Removed 1 listing." (the venue just un-hidden). *(self-test: the count)*
5. **Cache files**: `listings.json` is now `listings.json.old`; `fetch-state.json` still has
   `"FfxivVenues"` under `lastFetched`, `marks.json` lists only "Hidden lounge" and the favourite and
   visited listings, and `/xllog` shows no error saving them. *(self-test: the move and the fetch time)*
6. **Unreadable files**: an unreadable `marks.json` is moved to `marks.json.bad` and the marks are read
   from `marks.json.bak`; an unreadable `cache/<source>.json` is moved to `cache/<source>.json.bad` and
   that source starts empty. *(self-test)*

## xivpf

Checks the fetch, the cooldown and the list; the reading rules are covered by `XivpfTests`.

1. **First fetch**: with no `Xivpf` time in `fetch-state.json` (disable Jaunt,
   remove it, re-enable), open `/jaunt` without opening the Party Finder. Within a few seconds
   "Party Finder (xivpf)" rows appear, and `/xllog` shows `Fetched … Party Finder ads from xivpf`.
   *(self-test: the request is recorded before it is sent and only None ads after it, against a stand-in; the real site stays manual)*
2. **Cooldown**: close and reopen `/jaunt` within 10 minutes: no second fetch is logged. After 10
   minutes, reopening fetches again. *(self-test: no fetch a minute before the 10 min are up)*
3. **Seen in both**: open the Party Finder's None category and browse a page, then reopen `/jaunt`.
   A venue ad listed both ways shows once, as "Party Finder", not also as "Party Finder (xivpf)".
   This also checks that xivpf's listing IDs match the game's. *(self-test: the in-game copy wins, against a stand-in)*
4. **Offline**: as in FFXIV Venues 7, with the `Xivpf` fetch time: `/xllog` shows a warning
   "Couldn't fetch Party Finder ads from xivpf", and the cached ads stay listed.

## Partake

Checks the fetch, the cooldown and the list; the reading rules are covered by `PartakeTests`.

1. **First fetch**: with no `Partake` time in `fetch-state.json` (disable Jaunt,
   remove it, re-enable), open `/jaunt`. Within a few seconds "Partake" rows appear, each with a
   second line of times and "World (Data centre)", and `/xllog` shows `Fetched … events from Partake`.
   *(self-test: one POST, the request recorded before it is sent, and only events that haven't ended and have a world, against a stand-in; the real site stays manual)*
2. **Times**: an event's times match its page on partake.gg in your local time, in the time and
   date formats chosen in Settings. One under way says "on now".
3. **Location**: an event whose location names a district, ward and plot has Travel and Copy
   address; one whose location Jaunt can't read shows the text instead.
4. **Credit**: the list ends with "Events from Partake (partake.gg). Jaunt is not affiliated with or
   endorsed by Partake."
5. **Cooldown**: close and reopen `/jaunt`: no second Partake fetch is logged within 8 hours.
   *(self-test: no fetch a minute before the 8 h are up)*
6. **Offline**: as in FFXIV Venues 7, with the `Partake` fetch time: `/xllog` shows a warning
   "Couldn't fetch events from Partake", and the cached events stay listed.

## FFXIV RP Event Calendar

Checks the fetch, the cooldown and the list; the reading rules are covered by `RpEventCalendarTests`.

1. **First fetch**: with no `RpEventCalendar` time in `fetch-state.json` (disable
   Jaunt, remove it, re-enable), open `/jaunt`. Within a few seconds rows credited "FFXIV RP Event
   Calendar · CC BY-SA 4.0" appear, and `/xllog` shows `Fetched … events from the FFXIV RP Event Calendar`.
   *(self-test: the request is recorded before it is sent and the events after it, against a stand-in; the real site stays manual)*
2. **Cooldown**: with the Settings cooldown at 10 minutes, close and reopen `/jaunt` after 10
   minutes: no second calendar fetch is logged. *(self-test: no fetch a minute before the 8 h are up)*
3. **Rows**: an event shows "Next …" or "On now until …" in your time zone and the Settings formats,
   then "validated …" with a year or "never validated". An open-world event shows its location as
   text with no Travel button. *(self-test: the address, location, dates and validation are recorded)*
4. **Credit**: hovering "FFXIV RP Event Calendar" shows `https://ffxiv-rp.org/` and a hand cursor;
   clicking it opens the site. "CC BY-SA 4.0" does the same with the licence page.
5. **No contacts**: `cache/RpEventCalendar.json` holds no `contacts` and no Discord handles from the calendar. *(self-test)*

## FFXIV Venues

Checks the fetch, the cooldown and the list; the reading rules are covered by `FfxivVenuesTests`.

1. **First fetch**: with no `FfxivVenues` time in `fetch-state.json` (disable Jaunt,
   remove it, re-enable), open `/jaunt`. Within a few seconds the list fills with "FFXIV Venues" rows,
   and `/xllog` shows `Fetched … venues from FFXIV Venues`. The game doesn't stutter while it loads.
   *(self-test: the request is recorded before it is sent and the venues after it, against a stand-in; the real site and the stutter stay manual)*
2. **Cooldown**: close and reopen `/jaunt`. No second fetch is logged; the venues are still listed.
   Disable and re-enable Jaunt, then open `/jaunt`: still no fetch, as the time is saved.
   *(self-test: no fetch a minute before the 8 h are up, one when they are; the reload stays manual)*
3. **24/7**: at least one row shows "· open 24/7" (Nox Arcana or Splash Bathhouse, as of 2026-11).
4. **Address**: on a venue, Copy address copies its world, district, ward and plot; Travel works with
   Lifestream installed. A venue on Bismarck shows "Bismarck", not "Bismark". *(self-test: the Bismarck spelling)*
5. **Links**: Open on a website and a Discord link opens the browser at the URI in the tooltip.
6. **Scrolling**: scroll quickly through the whole list: rows don't jump or overlap, and the scrollbar
   reaches the last row.
7. **Offline**: block the network (or set a bad DNS), clear the `FfxivVenues` fetch time as in 1, and
   open `/jaunt`. `/xllog` shows a warning "Couldn't fetch venues", the cached venues stay listed, and
   reopening doesn't retry until the cooldown passes. *(self-test: against a failing stand-in)*
8. **Symbols in names**: no title shows `=` boxes. "꧁Tʜᴇ Aᴜʀᴏʀᴀ Tʜᴇᴀᴛᴇʀ꧂" (Mateus, Shirogane W26 P12)
   shows as "«The Aurora Theater»", "✦✦✦ 🌴 DESERT ROSE 🌴 ✦✦✦" as "◆◆◆ DESERT ROSE ◆◆◆", and
   "♥ Crafty Cafe ♥" and "☆KITSU☆" are unchanged.
