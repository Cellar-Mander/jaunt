# In-game smoke test

Run before merging anything that changes what the plugin does in-game. Each feature adds its cases here.

Setup: deploy with `dotnet buildingway dev-plugin deploy <task-ref>` (see "Testing in-game" in [CONTRIBUTING.md](../CONTRIBUTING.md)), log in, open `/xllog`. Release the dev folder with `dotnet buildingway dev-plugin release` when done.

## Self-tests

To run the checks that need no look at the screen and no click, open Dalamud's self-test window (`/xldev`, Dalamud menu, Open Self-Test), pick the `Jaunt` group and press play. Each check is its own step, so the table shows which passed or failed, and Jump runs any one on its own. Each step logs `Self-test PASS: <step>` or `Self-test FAIL: <step> (<detail>)`; on Linux, `grep 'Self-test' ~/.xlcore/logs/dalamud.log | tail` shows the result.

The steps work on scratch copies: the cache steps use a `self-test` folder in the plugin's config folder, deleted afterwards, and never your real cache files; the FFXIV Venues, xivpf, Partake and RP Event Calendar steps use a stand-in for the site and a clock they move, so they make no request. The settings step saves a test object through Dalamud, then saves your settings again. Three warnings in `/xllog` are expected, announced by `Self-test: … expected` lines just before them.

The drawing step ("draw without allocating") watches the real windows, so before running it open the listing browser with a listing selected and Settings with every group expanded; with either closed it fails and says so.

Checks marked *(self-test)* need nothing more; those marked *(self-test: …)* are covered only for the part named, so finish them by hand. Unmarked checks stay manual: how things look, clicks, reloading the plugin and the real network.

## Loading

1. `/xllog` shows `Jaunt <version>+<commit> loaded`, with this checkout's commit.
2. `/jaunt` opens the listing browser; running it again closes it. `/jaunt config` opens the settings window and leaves it open when run again. *(self-test)*
3. `/xlplugins` → Installed Plugins shows a settings button for the plugin, which opens the same window.
4. Change a setting, disable and re-enable the plugin: the setting is kept. *(self-test: Dalamud's save and load; the reload stays manual)*
5. With the listing browser and Settings open, a listing selected and every Settings group expanded, neither window allocates on a frame that works nothing out. *(self-test)*

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
5. **Sources**: Data ends with "Listings come from" and a bulleted list in the same colour:
   In-game Party Finder, xivpf, FFXIV Venues, Partake "(Jaunt is not affiliated with or endorsed by
   Partake.)" on the same line, and FFXIV RP Event Calendar · CC BY-SA 4.0. Hovering each name but the in-game Party Finder shows its site with a hand
   cursor and clicking opens it (`xivpf.com`, `ffxivvenues.com`, `partake.gg`, `ffxiv-rp.org`);
   "CC BY-SA 4.0" opens the licence page.
6. `/xllog` shows no errors from Jaunt throughout.

### With the sample cache

[`smoke-test-cache`](../Jaunt.Core.Tests/Fixtures/smoke-test-cache) holds eight listings: two unmarked, one
favourite and one visited, two hidden, plus an old unmarked listing and an old hidden one-off that
expire on load. Its dates are 2000 or 2099, so the results below hold until 2099.
`SmokeTestListingsTests` keeps the files and this list in step.

Loading the plugin rewrites the cache files, so copy the sample in again before each run:

1. Disable Jaunt in `/xlplugins`.
2. In the plugin's config folder (`~/.xlcore/pluginConfigs/Jaunt/` on Linux,
   `%AppData%\XIVLauncher\pluginConfigs\Jaunt\` on Windows), move `marks.json`, `marks.json.bak`,
   `fetch-state.json` and the `cache` folder somewhere safe if you want to keep them, then copy the
   contents of `Jaunt.Core.Tests/Fixtures/smoke-test-cache/` there.
3. Enable Jaunt and open Settings.

Then:

1. **Expiry on load**: Hidden venues lists "Another hidden" then "Hidden lounge". "Hidden gala
   (one-off)" is not listed: marked one-offs expire. *(self-test: which listings expire; look at the list yourself)*
2. **Clear**: confirm it. It reports "Removed 2 listings." Hidden venues is unchanged. *(self-test: the count)*
3. **Un-hide**: press Un-hide on "Another hidden". Only "Hidden lounge" is left. Disable and
   re-enable Jaunt: still only "Hidden lounge". *(self-test: the cache after a save and reload; press Un-hide yourself)*
4. **Clear again**: it reports "Removed 1 listing." (the venue just un-hidden). *(self-test: the count)*
5. **Cache files**: `fetch-state.json` still has `"FfxivVenues/Materia"` under `lastFetched`, `marks.json`
   lists only "Hidden lounge" and the favourite and visited listings, `marks.json.bak` holds the marks
   from before the last change, and `/xllog` shows no error saving them. *(self-test: the fetch time and the backup)*
6. **Unreadable files**: an unreadable `marks.json` is moved to `marks.json.bad` and the marks are read
   from `marks.json.bak`; an unreadable `cache/<source>.json` is moved to `cache/<source>.json.bad` and
   that source starts empty. *(self-test)*

## Listing browser

Checks the layout, drawing and selection; grouping, which openings are listed, the order and the
status text are covered by `DayGroupsTests`, `SchedulesTests` and `StatusTextTests`. Venues cached
before this version have no openings until FFXIV Venues is fetched again: clear the `FfxivVenues/…`
times as in FFXIV Venues 1 first.

1. **Layout**: `/jaunt` opens a window with the list on the left and a detail pane on the right. The
   title bar has a cog that opens Settings. Resize the window to its smallest: both panes stay usable.
   At Steam Deck size (1280×800) with a raised font scale (`/xlsettings` → Look & Feel → Global Font
   Scale), rows and the detail pane aren't clipped.
2. **Groups**: dividers read "Open now", then "Today" with the weekday dimmed (if anything opens
   later today), "Tomorrow" and the weekday, then dates in the Settings format with the weekday
   dimmed. 24/7 venues are at the end of "Open now". A venue open now also shows under its next
   opening's day; one not open shows once.
3. **Rows**: each has a rounded square icon level with the first line (a pictogram of its first tag until its image loads, and for one with none), a status line (green "Open now · until …" or the times), the
   title in a larger font and the world. Above the list, at the right, "N listings" counts the
   entries (a venue under both "Open now" and its next day counts once). Narrow the window: long titles end in "…" rather than running under the
   detail pane.
4. **Local time**: change Settings → Display between 12- and 24-hour and the date formats; rows,
   dividers and the detail follow at once. A venue's opening matches its FFXIV Venues page in your
   time zone.
5. **Detail**: select a Party Finder ad, a venue, a Partake event and a calendar event. Each shows a
   band with its splash image (its district's art, dimmed, for one with none), its title in a larger font, status, world, Travel and Open page where it has an address or link, Location,
   and a footer naming its source ("Sources: FFXIV Venues"), each source but Party Finder linking to its
   site, in blue and underlined. A venue with a Discord invite shows "Discord" (also blue and underlined) first in the footer, opening the invite, and
   not under Location; one whose only link is the invite has no Open page. A long ad shows "Read more", which expands it and becomes "Show less".
   Each shows Age / rating: an ad saying "SFW 18+" shows "18+ · SFW"; a Partake or calendar event shows its rating first ("Mature · SFW"); a venue
   shows only "SFW" or "NSFW". Tags shows its interests as outlined chips that wrap at the pane's edge; a listing with none has no Tags.
   An ad naming DJs ("DJs: KK, Moon Cat, and Sleepy") shows them under Lineup, separated by dots; a name or handle whose
   Twitch channel the ad links is blue and underlined and opens it, and that link isn't listed again under Location. An ad
   naming no performer, or only a birthday ("HAPPY BIRTHDAY OPHE"), has no Lineup.
6. **Selection is remembered**: select a listing, close and reopen `/jaunt`: it is still selected.
   Disable and re-enable Jaunt: still selected. Press Settings → Clear cached listings, so it drops
   out of the data: the detail pane shows "Select a listing on the left to see its details."
7. **Empty states**: with nothing selected, the detail pane shows the help text. With nothing listed
   (log in a character in a region with no cached listings, with the network blocked), the list shows
   a joke line such as "No one here but us chickens." and the detail pane is blank.
8. **Duplicates**: with the Party Finder's None category open for a while, no venue shows twice at
   the same time: a re-posted ad (same text, new listing), a venue's several ads, and an ad for an
   FFXIV Venues venue that is open then each show as one row. The merged row takes the FFXIV Venues
   name, Location lists every link from its listings once, About shows one ad's text, and the footer
   names each source once on one line ("Sources: FFXIV Venues, Party Finder, xivpf"). Two xivpf ads
   giving only the ward ("Fae / LB / W4") merge with the game's ad naming the plot in that ward. A
   venue's next opening still shows under its own day. FFXIV Venues venues cached before this version
   have no openings to merge with: clear the `FfxivVenues/…` times as in FFXIV Venues 1 first. *(self-test: none; the matching rules
   are covered by `DuplicatesTests`)*
9. **Zone from a venue**: an ad that gives a time with no zone, for an FFXIV Venues venue, shows at the
   venue's opening time in your time zone, not hours away from it.
10. **Icons and splash images**: with `images/` in the plugin's config folder deleted, open `/jaunt`.
    Rows in view get icons within a few seconds: a venue with a Discord invite shows its server icon,
    and one without shows its website's icon or a square from its FFXIV Venues banner. Select an FFXIV
    Venues venue: its banner fills the band, cropped to it, not stretched. Select a Partake event: its
    image shows. Only rows that have been in view have files in `images/` (scroll to the bottom and
    more appear). Block the network, then close and reopen `/jaunt` and disable and re-enable
    Jaunt: the images still show at once, from `images/`. Under
    Wine (XIVLauncher.Core), FFXIV Venues banners show, as they are WebP. *(self-test: none; fetching,
    fallbacks and keeping are covered by `ImageStoreTests`)*
11. **Placeholders**: find a row with no icon (most Party Finder ads): it shows a grey pictogram of its
    first tag in venue-type-first order, a mug for a cafe, a martini glass for a bar, headphones for a
    club, and a house for one with no tags. Select it: an ad with a Mist address shows Limsa Lominsa's
    loading screen, dimmed, cropped to the band with no dark fade at its bottom; one in the Lavender
    Beds, Goblet, Shirogane or Empyreum shows Gridania's, Ul'dah's, Kugane's or Ishgard's. An RP calendar
    event in the open world ("Central Shroud, The Bannock") shows its zone's loading screen. One whose
    location is only "See URL", and an ad with no address, shows the world map. *(self-test: none; the choice is covered by
    `PlaceholdersTests`)*

## xivpf

Checks the fetch, the cooldown and the list; the reading rules are covered by `XivpfTests`.

1. **First fetch**: with no `Xivpf` time in `fetch-state.json` (disable Jaunt,
   remove it, re-enable), open `/jaunt` without opening the Party Finder. Within a few seconds
   ads appear whose detail footer says "Sources: xivpf", and `/xllog` shows `Fetched … Party Finder ads from xivpf`.
   *(self-test: the request is recorded before it is sent and only None ads after it, against a stand-in; the real site stays manual)*
2. **Cooldown**: close and reopen `/jaunt` within 10 minutes: no second fetch is logged. After 10
   minutes, reopening fetches again. *(self-test: no fetch a minute before the 10 min are up)*
3. **Seen in both**: open the Party Finder's None category and browse a page, then reopen `/jaunt`.
   A venue ad listed both ways shows once, its footer saying "Party Finder", not "xivpf".
   This also checks that xivpf's listing IDs match the game's. *(self-test: the in-game copy wins, against a stand-in)*
4. **Offline**: as in FFXIV Venues 7, with the `Xivpf` fetch time: `/xllog` shows a warning
   "Couldn't fetch Party Finder ads from xivpf", and the cached ads stay listed.

## Partake

Checks the fetch, the cooldown and the list; the reading rules are covered by `PartakeTests`.

1. **First fetch**: with no `Partake` time in `fetch-state.json` (disable Jaunt,
   remove it, re-enable), open `/jaunt`. Within a few seconds Partake events appear, each row
   with its times and "World (Data centre)", and `/xllog` shows `Fetched … events from Partake`.
   *(self-test: one POST, the request recorded before it is sent, and only events that haven't ended and have a world, against a stand-in; the real site stays manual)*
2. **Times**: an event's times match its page on partake.gg in your local time, in the time and
   date formats chosen in Settings. One under way is under "Open now" and says "Open now · until …".
3. **Location**: selected, an event whose location names a district, ward and plot has Travel and
   Copy; one whose location Jaunt can't read shows the text under Location instead.
4. **Credit**: neither the list nor a selected event's footer says "Jaunt is not affiliated with or
   endorsed by Partake."; Settings → Data carries it (Settings 5).
5. **Cooldown**: close and reopen `/jaunt`: no second Partake fetch is logged within 8 hours.
   *(self-test: no fetch a minute before the 8 h are up)*
6. **Offline**: as in FFXIV Venues 7, with the `Partake` fetch time: `/xllog` shows a warning
   "Couldn't fetch events from Partake", and the cached events stay listed.

## FFXIV RP Event Calendar

Checks the fetch, the cooldown and the list; the reading rules are covered by `RpEventCalendarTests`.

1. **First fetch**: with no `RpEventCalendar` time in `fetch-state.json` (disable
   Jaunt, remove it, re-enable), open `/jaunt`. Within a few seconds calendar events appear, their
   detail footer crediting "FFXIV RP Event Calendar · CC BY-SA 4.0", and `/xllog` shows `Fetched … events from the FFXIV RP Event Calendar`.
   *(self-test: the request is recorded before it is sent and the events after it, against a stand-in; the real site stays manual)*
2. **Cooldown**: with the Settings cooldown at 10 minutes, close and reopen `/jaunt` after 10
   minutes: no second calendar fetch is logged. *(self-test: no fetch a minute before the 8 h are up)*
3. **Detail**: a selected event shows its date and times or "Open now · until …" in your time zone and
   the Settings formats, and no "Validated" line. An open-world
   event shows its location as text with no Travel button. An event with no upcoming date isn't listed. *(self-test: the address, location, dates and validation are recorded)*4. **Credit**: in the footer, hovering "FFXIV RP Event Calendar" shows `https://ffxiv-rp.org/` and a hand cursor;
   clicking it opens the site. "CC BY-SA 4.0" does the same with the licence page.
5. **No contacts**: `cache/RpEventCalendar.json` holds no `contacts` and no Discord handles from the calendar. *(self-test)*

## FFXIV Venues

Checks the fetch, the cooldown and the list; the reading rules are covered by `FfxivVenuesTests`.

1. **First fetch**: with no `FfxivVenues/…` times in `fetch-state.json` (disable Jaunt,
   remove them, re-enable), open `/jaunt`. Within a few seconds the list fills with venues (footer "Sources: FFXIV Venues"),
   only on your region's worlds, `/xllog` shows `Fetched … venues from FFXIV Venues`, and
   `fetch-state.json` has one `FfxivVenues/<data centre>` time per data centre in your region. The game doesn't stutter while it loads.
   *(self-test: Materia's request, with `dataCenter=Materia`, is recorded before it is sent and the venues after it, against a stand-in; the real site and the stutter stay manual)*
2. **Cooldown**: close and reopen `/jaunt`. No second fetch is logged; the venues are still listed.
   Disable and re-enable Jaunt, then open `/jaunt`: still no fetch, as the time is saved.
   *(self-test: no fetch a minute before the 8 h are up, one when they are; the reload stays manual)*
3. **24/7**: at least one row at the end of "Open now" shows "Open now · 24h" (Nox Arcana or Splash Bathhouse, as of 2026-11).
4. **Address**: on a selected venue, Copy copies its world, district, ward and plot; Travel works with
   Lifestream installed. A venue on Bismarck shows "Bismarck", not "Bismark". *(self-test: the Bismarck spelling)*
5. **Links**: Open page, and Open beside a further link under Location, open the browser at the URI in the tooltip.
   Copy link on a venue with a website copies that website; on one without, it copies
   `https://ffxivvenues.com/venue/<id>`, which opens the venue's page.
6. **Scrolling**: scroll quickly through the whole list: rows don't jump or overlap, and the scrollbar
   reaches the last row.
7. **Offline**: block the network (or set a bad DNS), clear the `FfxivVenues/…` fetch times as in 1, and
   open `/jaunt`. `/xllog` shows a warning "Couldn't fetch venues", the cached venues stay listed, and
   reopening doesn't retry until the cooldown passes. *(self-test: against a failing stand-in)*
8. **Symbols in names**: no title shows `=` boxes. "꧁Tʜᴇ Aᴜʀᴏʀᴀ Tʜᴇᴀᴛᴇʀ꧂" (Mateus, Shirogane W26 P12)
   shows as "«The Aurora Theater»", "✦✦✦ 🌴 DESERT ROSE 🌴 ✦✦✦" as "◆◆◆ DESERT ROSE ◆◆◆", and
   "♥ Crafty Cafe ♥" and "☆KITSU☆" are unchanged.

## Travel region

Checks that only the character's region is fetched, kept and listed; the matching rules are covered by
`TravelRegionTests` and each source's tests.

1. **Not logged in**: at the title screen with `/jaunt` open, `/xllog` shows no fetch.
2. **One region**: logged in, open `/jaunt` after clearing `fetch-state.json` as in each source's
   first fetch. `cache/Xivpf.json`, `cache/Partake.json` and `cache/RpEventCalendar.json` hold only
   listings on your region's worlds. *(self-test: an xivpf ad on Bismarck and a Partake event on Gilgamesh aren't recorded, against stand-ins)*
3. **Another region**: log in a character in another region and open `/jaunt`. FFXIV Venues fetches
   at once for that region's data centres (new `FfxivVenues/<data centre>` times); xivpf, Partake and
   the RP calendar don't fetch until their cooldowns pass, and then record only that region's listings.
   The first region's cached listings are still in the cache files.
4. **List follows the character**: with `/jaunt` open, log out: within a second the list shows only
   "Log in to a character to see the venues it can travel to." Log in a character in the first
   region: only its region's listings show again, though the other region's are still cached. Every
   row's world (or a Partake event's data centre) is in the logged-in character's region.
