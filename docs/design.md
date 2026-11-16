# Design

What Jaunt does, stated precisely enough to test against. Mutation triage checks
surviving mutants against this file (`spec=` in `.claude/checks`), so write down each behaviour the
tests should hold, including edge cases and limits.

## Behaviour

### Opening times in Party Finder ads

`OpeningTimes.Parse` reads normalised ad text; `OpeningTimeResolver` turns each result into UTC
instants. `OpeningTimesCorpusTests` checks every labelled corpus ad (`Corpus/README.md`, `times`).

- **Open now**: "open now", "now open", "is open", a bare "NOW", 営業中 and similar. A bare "open"
  doesn't count when a time, "from", "tonight" and the like follow it, or a word says what is open
  ("Open Stage", "Open Recruitment", "open to all"). "starts now" and "now hiring" don't count.
  A closing time with no start ("open until 1am") belongs to the now opening.
- **Always**: "24/7" and "always open". 24/7 is never a date.
- **Times** need AM/PM, a zone, minutes or 時 after the hour; a bare number ("Gob 14-35", "W30") is
  not a time. Bare hours 1–11 read as evening ("8-12" is 20:00–00:00); a leading zero, an hour over
  12 or Japanese text in the ad means the 24-hour clock. In a range, the side without AM/PM takes
  the reading within 12 hours of the other. A dot between hour and minutes needs AM/PM.
- **Zones** as written: ST is server time (UTC). US and Australian names give the place, not the
  offset: "EST" in July is New York's daylight time, since ads write EST all year. Unknown or
  missing zones use the region default (`aca1be81`): NA New York, OCE Sydney, JP Tokyo, EU Berlin
  for German or French ads and London otherwise. A lower-case "st" after 1 or 21 is an ordinal.
- **Days**: today/tonight/tomorrow apply to the whole ad, the first one written wins. A weekday
  applies only to a time or date right after it ("FRI 17 ST"), so "Goth Friday" names an event. A
  date beats both. a/b dates take whichever of month/day and day/month is nearer the day seen,
  month/day on a tie; a year is never written, so the nearest year is used.
- **Resolving**: an undated start is today's in the ad's zone, unless yesterday's overnight opening
  is still running. A weekday is the next one, counting today. A closing time alone is the next one
  after the ad was first seen with its text, so it stays put while the ad stays listed, unless one
  passed at most 6 hours before that sighting: the ad went up before it closed, so it is over; with a day,
  one before 06:00 is after that day's midnight. An end at or
  before the start is the next day. A time skipped by a clock change reads as the hour after; a
  repeated one as the first.
- Parsing is one pass over tokens per step, with no regex, so it stays linear in the ad's length.

### Addresses in ad text

`AddressParser` reads every housing address in a Party Finder ad's text (after `PartyFinderText`
decodes its glyphs) and returns them in the order they appear. Each has a world, where the world came
from, a district, a ward and optionally a plot, subdivision, apartment and room. The labelled corpus
(`Jaunt.Core.Tests/Corpus`) holds the shapes below as real ads.

**Place names** come from a `Gazetteer`: every public world with its data centre and region, and each
district's name in English, German, French and Japanese, read from the game's data
(`Jaunt/Text/GazetteerLoader.cs`). A built-in community table adds Japanese world names and their
short forms (パンデモ, ゆに, 槍), the abbreviations `Exl` (Excalibur) and `Gobby` (Goblin or Goblet),
district shorthand (`LB`, `Lav`, `Gob`, `Shiro`, `Emp`, `Em`, the misspelling `Emperyum`, ラベ, ゴブ, エンピ…) and data-centre
shorthand (`Dyn`, `Cry`, `Pri`, マナ, メテオ…). Names are matched case-insensitively and may span
several words separated by spaces, `.` or `・`. Any word of three or more letters that starts exactly one
world's name in the listing world's region is that world ("Krak", "Sagi"). When no world in that region
starts with it, the one world anywhere that does. `gil` is never an abbreviation.

**Words.** Latin text splits where a lower-case letter meets an upper-case one ("HypMist", "BalGob")
and where letters meet digits ("W30"). Japanese text has no spaces, so it splits on known names,
longest first ("マナパンデモゴブ" → マナ, パンデモ, ゴブ). A long-vowel mark ー between two digits is a
dash.

**Ward and plot.** Wards are 1–30 and plots 1–60; a number outside those is not one. The shapes are:
- `W20 P28`, `Ward 27 Plot 44`, `W:27P:43`, `W 22`, with any separators between the ward and plot.
- A plot without a P after `/` or `|` only: `W2/30`, `W9|34`, not `W2 30`, and not when a time
  word follows.
- `P29/30` after a ward: two addresses, one per plot.
- Plot before ward: `Plot 40, Ward 5`, `P50 W1`, and ordinal wards `5th Ward` (the suffix touching the number).
- A district followed by bare numbers, joined by a dash or a space: `Mist 8-32`, `Empy 6 12`, `Gob28 P5`.
  Not with `/` (a date), and not when a time follows: `am`, `pm`, `a`, `p`, a zone (`ST`, `EST`, `AEDT`…),
  時, or `:` and a number.
- Japanese: `ゴブ第23区32番地`, `ラベ21区13`, `ゴブ27-10`.
- A world followed by a ward and plot joined by a dash, with no district: `Ifrit/6-51`.
- A ward with no plot is an address only when an apartment follows, the ward is marked (`W`, `Ward`,
  区, 第), or a district is found for it. "Ward 2" alone is not an address; "Mist 22" alone is not.
  A number followed by 号室 or 号 is never a plot.

**After the plot** (up to 8 words): 拡張 or `subdivision` sets subdivision true, and 表 or `main` sets it false,
also inside brackets ("(拡張街南東)"). アパ or `Apt` then a number is an apartment. In `アパ1- 24` the second
number is the room in building 1, so the apartment is 24. A room number (`room 3`, `Room#6`, `個室001`,
`4号室`) is a room after a plot, and an apartment after a ward with no plot or after an apartment building's
name (トップマスト, Lily Hills, `LHS`…). A bracket right after the plot holding at most 4 letters and
digits, touching and including a digit, is a room (`【B1】`). Other brackets are skipped. A bracket never
closed within 10 words ends the address.

**District and world.** Walking back from the ward, skipping up to 6 other words: the nearest district,
then, skipping up to 3 more, the nearest world. A word that names both ("Gob", "Gobby") is the district if none has been
found yet, and the world otherwise. A data centre where the world would be ("Materia-LB-W22-P36") means
the text names no world there. Walking back never goes into the previous address. If either is still
missing, look forward, skipping up to 2 other words ("W8 P45 Mist Sagi"). If no world is found, use the world of the
previous address in the same ad, then a world's full name anywhere in the ad. Abbreviations don't count
for this. Only then use the listing world, marked as lower confidence (`WorldSource.Listing`). An
unknown listing world is passed through as written.

### Links in ad text

`WebLinks.Find` reads every website link in a Party Finder ad's text (after `PartyFinderText`
normalises it), in the order written. `WebLinksTests` checks every labelled corpus ad
(`Corpus/README.md`, `urls`).

- A link is a run of ASCII letters, digits and `-._~:/?#@%&=+`, so brackets, parentheses, `|`,
  glyphs and line breaks around it are not part of it. Leading punctuation and a trailing `.`, `:`
  or `?` belong to the sentence; a trailing `/` or `#` stays.
- It counts when it starts with `http://` or `https://` (also `https:` without the slashes), or with
  `www.`, or when its host ends in a common web ending (`.com`, `.co`, `.gg`, `.tv`, `.club`,
  `.site`, `.link`, `.gd`, ...). So `discord.gg/abc` and `venue.carrd.co` are links, and
  "Lav.Beds", "p.m." and "Emp.W15" are not.
- A host with `@` or `:` is an email address, handle or time, not a link. Other schemes
  (`javascript:`, `ftp:`) are never links.
- Each link keeps the text as written and gets a URI to open: always http or https, https when the
  ad gives no scheme. A link that can't form a valid URI ("canvas..site") is left out.

### Settings

`ConfigurationBase` holds the settings; `Normalize` runs after every load. The window groups them
in collapsible sections: Data, Display, Calendar (with the calendar feature), then Listings, all
expanded until collapsed; which are expanded is saved too. Each change saves at once.

- **Refresh cooldown** (`RefreshPolicy`), default 10 minutes, kept within 10 minutes to 24 hours.
  Opening the listing browser fetches each source unless that source was fetched within its
  cooldown; Refresh obeys it too, and inside it the browser shows cached listings. A source's
  cooldown is the setting or its own minimum, whichever is longer: xivpf 10 minutes (so the setting
  alone), FFXIV Venues, Partake and the FFXIV RP Event Calendar 8 hours. The in-game Party Finder is
  never fetched, so it has no cooldown: its listings are recorded as the game sends them. A cooldown
  has passed once exactly that long has gone by. A source never fetched is fetched; one whose last
  fetch time is after now (the clock moved back) is fetched too, rather than left stuck.
- **Travel region**: listings are fetched and kept only for worlds the logged-in character can
  travel to: every public world in its home world's region (`IPlayerState.HomeWorld`, then the
  `World` sheet's data centre and its region), as data-centre travel doesn't cross regions. Nothing
  is fetched while no character is logged in. A source that can be asked for one data centre
  (`RefreshPolicy.ByDataCentre`: FFXIV Venues) gets one request per data centre in the region, each
  with its own cooldown, so a character in another region fetches its data centres at once. Sources
  that can't be asked for less than every region (xivpf, Partake, the RP calendar; their APIs had no
  region, data-centre or server filter as of 2026-11) are fetched whole with one cooldown for all
  regions, and listings off the region's worlds are left out when recorded; after a switch to another
  region their listings arrive once that cooldown passes. Listings cached for another region are
  never purged for it; they expire as usual, and the list doesn't show them (see "List").
- **Sources**: Data ends with "Listings come from" and a bulleted list of the sources, in normal
  text colour: the in-game Party Finder, then xivpf, FFXIV Venues, Partake and the FFXIV RP Event
  Calendar, each opening its site (`SiteUrl`) when clicked. Partake's carries its disclaimer
  (`Partake.Disclaimer`) in brackets on the same line, and the calendar's its CC BY-SA 4.0 licence link. These are the app-wide credits; per-listing credits are
  in the detail footer (see "Detail pane").
  An in-game Party Finder ad on a world outside the region isn't recorded either; the game sends only
  the character's data centre, so this rarely drops one.
- **Last fetched**: the listing cache records when each fetched source (each data centre, for a
  source fetched by data centre) was last requested,
  whether or not the request succeeded, and saves it with the listings. It is used only for the
  cooldown, never shown, and survives Clear cached listings, expiry and reloads.
- **Time format**: System, 12-hour (`8:05 PM`) or 24-hour (`20:05`). System is 12-hour when the
  locale's short time pattern has AM/PM.
- **Date format**: System, `Sat 14 Nov`, `Sat Nov 14` or `2026-11-14`, with English names. System
  is ISO when the locale writes the year first, month-day when it writes the month before the day,
  and day-month otherwise. Days are not zero-padded.
- An unknown time or date format in a hand-edited config becomes System.

### Listing cache

`ListingCache` keeps every listing seen, keyed by source and the source's ID (the same ID in two
sources is two listings), with when it was last seen, whether it is a one-off event (as the source
last said) and the favourited, visited and hidden marks. It is saved in the plugin's config folder
as separate files, each with a `version` (1); sources are named, not numbered:

- `marks.json`: each marked listing's source, ID, title and one-off flag, and its three marks; and
  each marked place (see "Marks" under "Listing browser"): its address (the district by name), title
  and address as last shown, three marks, and the FFXIV Venues IDs, websites and Discord invites seen
  there, each sorted so saves are stable. A place with no mark left isn't written. Written when a
  mark changes, when a marked listing's title, ad text or one-off flag changes, when a marked listing
  expires, and when a place moves or learns something. On load a place without a world, a known
  district, a ward from 1 to 30, or exactly one of a plot (1 to 60) or an apartment is dropped, and
  so is one with no mark. The file it replaces is kept as `marks.json.bak`.
- `fetch-state.json`: each source's last fetch time (`lastFetched`), keyed by the source's name,
  or `<source>/<data centre>` (`FfxivVenues/Aether`) for a source fetched by data centre. Written on each fetch.
- `cache/<source>.json` (e.g. `cache/FfxivVenues.json`): each listing as the source sent it, in
  the source's own shape (a Party Finder ad's text and world; a venue's name and fields; an event's
  values), with when it was last seen. Written only when that source records, re-sees or loses a
  listing. An xivpf ad the game also sent writes both ad files when it moves the in-game copy's
  last-seen time.

Nothing derived is saved: titles shown, an ad's title, addresses, opening times, 24/7 and
whether a listing is marked are worked out again from what the source sent. An ad's title is
worked out the first time it is read after its text changes, not on load. A save snapshots
the changed files' contents on the game thread and serialises and writes them on a background
task, after any earlier save; each file is written to a temporary file and renamed into place.

On load:

- Each file is read on its own. One that is unreadable or has a later `version` is moved aside
  with `.bad` added to its name and its part starts empty. If `marks.json` is missing or
  unreadable, `marks.json.bak` is read instead.
- Unknown source names, entries without an ID and, in fetch times, the in-game Party Finder and a
  time with a data centre for a source fetched for every region (or without one for a source fetched
  by data centre) are dropped. Listings in the source files are loaded whatever their region. A saved opening that doesn't end after it starts is dropped, and a Partake event that
  had ended when last seen is dropped.
- A mark whose listing no source file holds still adds the listing, with the mark's title, so the
  hidden list shows it. It was last seen at no known time, so a marked one-off expires (see below).
  A listing whose title is empty takes the mark's title.

- **Clear cached listings** asks for confirmation, warning that rate-limited sources may not show
  in the browser again for hours, then removes every listing that is not favourited, visited or
  hidden and says how many it removed. Cancelling removes nothing.
- **Expiry**: on load, a listing that is not favourited, visited or hidden is dropped once one
  calendar month has passed since it was last seen (last seen 14 Oct 20:00 → dropped from 14 Nov
  20:00). An unmarked Party Finder ad (in-game or xivpf) is dropped sooner: once two days have
  passed since it was last seen and its text states no time ending, or with no end starting, later
  than two days before now (last seen 12 Nov 20:00 → dropped from 14 Nov 20:00, unless a time it
  states is still ahead or ended after 12 Nov 20:00). The two days cover how long an ad may still
  count as listed at the longest cooldown (25 hours) and how far the zone its times are read in may
  move them. Marked recurring listings are kept however old. Marked one-offs expire the same way as
  unmarked listings, since they can't reliably be matched to a later listing; the window doesn't
  mention this. Clear still keeps marked one-offs.
- **Hidden venues** lists hidden places by title, case-insensitively, each once with its address
  dimmed, then hidden listings by title; each has an Un-hide button. Titles are drawn as in "Text the
  font can't draw".

### FFXIV Venues

`FfxivVenues` reads `https://api.ffxivvenues.com/v1.0/venue?approved=true&withinWeek=true&dataCenter=<name>`
once for each data centre in the travel region (see "Settings"; the API matches the name exactly,
ignoring case, and has no region filter), in one fetch: approved venues (the API's default, pinned) with an opening in the coming week, as the API
works out from each venue's schedule. A venue left out stays in the cache with its fields and marks,
and in the browser while it has openings to list, until it expires (see "Listing cache"). `FfxivVenues`
records each in the listing cache as `FfxivVenues`, keyed by the venue's ID, with the fields below as
the API sent them: its name as the title, and its location, website, Discord, schedule, `sfw` and
tags, each schedule entry with its time zone (the start's `timeZone`, an IANA ID), and its
`bannerUri` (see "Icons and splash images"); and its `description`, cleaned when read (see "Descriptions"). The title,
address, links and 24/7 shown are derived from those fields when listed, once per change, so a change
to how they are read applies to cached venues without fetching again. A venue cached without them
shows no address until the next fetch. Opening the browser asks for each data centre in the region
whose cooldown (8 hours, or the Settings cooldown if longer) has passed, one request after another;
each request is recorded before it is sent, so a failed one counts too. Download and parsing run off the game thread.

- **Title**: the venue's name, first line only, trimmed and cut to 60 characters like an ad's.
- **Address**: from the venue's location: the world, a district (`Mist`, `Lavender Beds`, `Goblet`,
  `Shirogane`, `Empyreum`, with or without "The"), a ward of 1–30, then a plot of 1–60, or else an
  apartment over 0 with the location's subdivision flag, and a room over 0. A plot wins over an
  apartment. A world the game data doesn't know is corrected to the one world sharing its longest
  beginning of at least 5 letters ("Bismark" → Bismarck), and otherwise kept as written. A venue with
  no world, an unknown district or a ward outside 1–30 (such as one giving only a free-text location)
  has no address and isn't in the browser.
- **Links**: the website then the Discord, then the Discord invites the description links to, http
  or https only. One without a scheme is read like a link in ad text (`discord.gg/abc` opens as
  https); any other scheme is dropped, including in a hand-edited cache. The same link twice is kept once.
- **Description**: the `description` paragraphs that aren't blank, joined with a blank line, then
  cleaned. 1,340 of 1,365 venues had one in the spike capture.
- **Open 24/7**: every day of the week (days 0–6) has an opening that repeats every week (interval
  type 0, every 1 week) and lasts at least 23 h 59 min, end minus start plus a day when the end is the
  next day. The site's form can't end at 24:00, so venues open around the clock enter 00:00–23:59.
  An opening with no end (the API reads it as 3 hours), every other week or monthly doesn't count.
  Schedule overrides don't change it.
- A venue without an ID, or marked not approved, is skipped; one whose fields have the wrong types
  is skipped without failing the others. A response that isn't a JSON array fails the fetch, which is
  logged, and the cached venues stay.
- Only the fields above are read and stored; others, such as managers' Discord IDs, never are.

### xivpf

`Xivpf` reads `https://xivpf.com/api/listings`, every Party Finder listing xivpf holds in one
response, and records each ad in the None category in the listing cache as `Xivpf`, keyed by its PF
listing ID, the same ID the game gives the listing. Opening the browser fetches it, unless the
source is within its cooldown (the Settings cooldown, 10 minutes at least); the request is recorded
before it is sent, so a failed one counts too. Download and parsing run off the game thread.

- **Category**: only listings whose category is exactly `None`.
- **Region**: an ad put up on a world outside the travel region, or on none xivpf names, isn't
  recorded. The API has no filter, so every region is downloaded.
- **Text**: the ad's English text (`description.en`), glyphs included, so the in-game Party Finder's
  parsers read it unchanged. xivpf gives the text once per client language, differing only in
  auto-translate phrases; when the English is missing or blank, the first other language that isn't.
  An ad with no text in any language is skipped.
- **Title** as for an in-game ad: the first line of the normalised text, cut to 60 characters.
- **Listed on**: the world the ad was put up on (`created_world.name`), trimmed, as the in-game
  Party Finder gives it; none when it is missing or blank.
- **Seen in both**: when the in-game Party Finder has sent a listing with the same ID, the list
  shows only one copy, whatever its marks; the other stays in the cache. That is the in-game copy,
  unless xivpf has seen the ad with other text since the game last sent it, as after the recruiter
  edits it; a sighting at the same time goes to the game. Recording an xivpf ad with the in-game
  copy's text moves that copy's last seen time up to now (never back), since it was listed then.
  Texts can differ without an edit, as xivpf leaves out auto-translate phrases.
- A listing without a whole-number ID of 0 or more, or whose fields have the wrong types, is skipped
  without failing the others. A response that isn't a JSON array fails the fetch, which is logged,
  and the cached ads stay.
- Only the fields above are read; others, such as the recruiter's name and the party's jobs, are
  never stored.

### FFXIV RP Event Calendar

`RpEventCalendar` reads `https://api.ffxiv-rp.org/api/Events/GetWeekTranslatableEvents`, about three
weeks of events with one row per occurrence, and records each event in the listing cache as
`RpEventCalendar`, keyed by its calendar ID (`uId`, trimmed), which its occurrences share. Opening
the browser fetches it, unless the source is within its cooldown (8 hours, or the Settings
cooldown if longer, so at most 3 fetches a day); the request is recorded before it is sent, so a
failed one counts too. Download and parsing run off the game thread. An event whose `server` isn't
a world in the travel region, or is missing, isn't recorded; the week endpoint has no region filter.

**Stored as sent**: the cache keeps `eventName` (as the listing's title), `server` (as the world
it is listed on), `location`, `esrbRating` and `eventURL` exactly as the calendar sent them, an empty title when
the name is missing. `shortDescription` is kept cleaned instead (see "Descriptions"). The title, address, location and rating below are derived from those when
shown, never stored, so a change to how they are read reaches cached events without fetching
again. The list derives them once per change to those fields, not every frame.

- **Title**: the event's name without a leading rating tag (`[T]`, `[M]` or `[AO]`, any case),
  which is metadata rather than part of the name; then first line only, trimmed and cut to 60
  characters like a venue's. A tag anywhere else, or another bracketed word, is kept. Hidden
  venues shows and sorts by this title too.
- **Rating**: `esrbRating`, trimmed; when it is missing or blank, the rating the removed tag stands
  for (Teen, Mature, Adults Only); otherwise none. For filtering; not shown.
- **Address**: the first housing address the ad-text parser (see "Addresses in ad text") reads from
  the trimmed location, so typos (`Mists`, `Emperyum`) and a plot written before the ward still
  parse. A location that names no world is on the event's server (trimmed), which counts as
  written, not as a listing world. Without a server, the address has no world.
- **Location**: a location with no housing address ("North Shroud, Hyrstmill", "See URL") is shown
  as text, trimmed; an empty one is neither.
- **Links**: `eventURL`, stored as sent; shown only if http or https, read like FFXIV Venues' links;
  then the Discord invites the short description links to.
- **Description**: `shortDescription`, the organiser's own text, which every event had in November
  2026. It is cleaned with Discord handles removed, so a contact named in it is never stored.
- **Openings**: each occurrence's `startTimeUTC` and `endTimeUTC`, earliest first, the same one
  kept once. An occurrence without both, or that doesn't end after it starts, is skipped; the
  event keeps its other occurrences. Each fetch replaces an event's openings.
- **One-off**: an event whose `isRecurring` is `false`.
- **Validated**: the day part of `lastValidated`, or none when it is missing or unreadable. Kept,
  but not shown.
- A row without an ID, or whose fields have the wrong types, is skipped without failing the others.
  A response that isn't a JSON array fails the fetch, which is logged, and the cached events stay.
- Only the fields above are read. `contacts` and `description` (the short description wrapped in
  a template with the contacts) hold Discord handles and are never read, stored or shown.
- **Credit**: the calendar's listings are CC BY-SA 4.0, so each of its events' detail footers shows
  "FFXIV RP Event Calendar", opening `https://ffxiv-rp.org/`, and "CC BY-SA 4.0", opening the
  licence. Settings → Data credits it the same way (see "Settings").

### Partake

`Partake` reads FFXIV events from Partake's public GraphQL API, `POST https://api.partake.gg/` with
no authentication, and records each in the listing cache as `Partake`, keyed by the event's ID, as a
one-off. Opening the browser fetches it, unless the source is within its cooldown (8 hours, or
the Settings cooldown if longer); the request is recorded before it is sent, so a failed one counts
too. Download and parsing run off the game thread. An event is recorded only when its world or its
data centre is in the travel region; the API has no region filter.

- **Raw values**: the cache keeps the title, world, data centre, location, links, age rating and
  tags exactly as Partake sent them (links in `linkTexts`; the rating and tags are for "Tags and
  SFW"), and the description cleaned (see "Descriptions"). The rules below for showing them run each time they
  are shown, so a change to these rules or the address parser applies on the next plugin load,
  not after the next fetch.
- **Query**: `events(game: "final-fantasy-xiv", startsBetween: {start}, sortBy: STARTS_AT_ASC)`
  with `start` 24 hours before now, so events already under way are included. Pages of 200 (`limit`,
  `offset`) until a page comes back with fewer, at most 5 pages per fetch. About 150 events are
  listed at once, so one page is usual. Only the fields shown or filtered on are asked for: `id`,
  `title`, `description`, `attachments`, `startsAt`, `endsAt`, `location`, `ageRating`, `tags`, `server.name`, `dataCenter.name`
  and the host team's `websiteUrl` and `discordUrl`. `attachments` isn't in Partake's docs either. `server` and `dataCenter` work but aren't in Partake's docs.
- **Title**: shown trimmed and cut to 60 characters like an ad's.
- **Times**: `startsAt` and `endsAt` (UTC), shown in local time in the chosen time and date formats,
  "Sat 14 Nov 20:00–23:00", with the end's date too when it ends on a later day, and "on now" once
  it has started.
- **World**: `server.name`, shown trimmed and corrected like an FFXIV Venues world, with the data
  centre trimmed (none when blank): "Lich (Light)". It is the world for addresses in the location
  that name none.
- **Location**: free text ("LB W19 P58", "57 plot, ward15 , lavender beds"), trimmed, for the same
  address parser as ad text. When no address is read from it, the text is shown instead, without
  Travel.
- **Links**: the host's website then Discord, then the Discord invites the description links to,
  shown like FFXIV Venues links: http or https only. On load, the cache drops missing link texts.
- **Description**: Markdown, often with images; 88 of 100 events had text left once cleaned. About
  leaves images out; their addresses, as written and each once, are kept for the splash image.
- **Images**: `attachments`, the event's own images as file names on `https://cdn.partake.gg/assets/`
  (57 of 60 events had one), kept as sent. A name sent without an extension is a WebP file there
  ("205b…" is at "205b….webp"). A name that isn't letters, digits, "-", "_" and "." (or starts with
  ".") is never used. On load, the cache drops missing names.
- An event is skipped, without failing the others, when it lacks an ID or either time, its title
  or world is missing or blank, its end is not after its start, or its fields have the wrong types.
  A response with GraphQL `errors` (Partake answers those with HTTP 200), without `data.events`, or
  that isn't JSON fails the fetch, which is logged, and the cached events stay. A missing tag is dropped.
- Events that have ended by the time a fetch is recorded aren't recorded, and the list leaves out
  cached events once they end.
- **Credit**: Partake's API terms (https://partake.gg/developers) bar apps from presenting
  themselves as endorsed, and set no placement for a disclaimer. A selected event's detail footer
  and Settings → Data (see "Settings") say "Jaunt is not affiliated with or endorsed by Partake.";
  the list itself carries no credit line, which overflowed its narrow pane. Each event's source reads "Partake". Hidden venues shows a hidden event's title as
  the list does.

### Descriptions

FFXIV Venues, Partake and the RP calendar give a description of their own (see each source), which
About shows when no ad gives text. `ListingAbout.Clean` cleans it once, when the source is read, and
the cache keeps only the cleaned text, so a change to these rules applies after the next fetch. One
pass over each line:

- **Lines**: line endings become `\n`; each line is trimmed, with runs of spaces, tabs and no-break
  spaces as one space. Runs of blank lines become one blank line, and blank lines at the start and
  end go.
- **Blocks**: a heading's `#` to `######` and a quote's `>` go; a list item's `-`, `*` or `+` becomes
  "•". A line of only three or more `-`, `*` or `_` (a rule) becomes a blank line.
- **Inline**: images (`![alt](url)`) go; a link (`[text](url)`) becomes its text, also inside an image
  in the link's text. `**`, `__` and `~~` go; a lone `*` goes unless it stands between spaces
  ("5 * 3"), and a lone `_` unless it is inside a word ("snake_case"). A backslash before ASCII
  punctuation goes, keeping the punctuation. An HTML tag (`<` then a letter or `/`, up to `>`)
  becomes a space; an autolink (`<https://…>`) keeps its address. "<3" and "a < b" stay.
- **Handles** (RP calendar only): "@" then a letter (an "_" after "@" has gone as emphasis) and up to 31 more letters, digits, "_" or
  "." goes, unless after a letter, digit or "." (so e-mail addresses stay); so does a name ending in
  "#" and four digits ("Enhasa#1319"). "@ 8pm" and "#12345" stay.
- **Invites**: Discord invites among the Markdown links' targets, then in the text, each once. They
  join the listing's links, so the footer's Discord link and Open page work as for other links.
- **Empty**: text with no letter or digit outside its links (only images, links and symbols) is
  none, so About stays empty; the invites are still kept.
- **Limit**: past 1,500 characters the text is cut at the last space or line break after 1,000,
  else at 1,500 (never inside a surrogate pair), and ends with "…". A cut description shown in full
  is followed by "Read the rest on the website", opening the listing's first link that isn't a
  Discord invite, when it has one. In November 2026 this cut 5 of 1,365 venues and 4 of 100 Partake events.
- Characters the font lacks, such as emoji, are left to "Text the font can't draw" when shown.
- **Filters**: a description shows with its listing; the SFW filter (see "Tags and SFW") decides
  whether the listing shows at all, so there is no separate check on the text.

### Text the font can't draw

Venue names and ad titles use characters the plugin's font lacks (ImGui draws those as a box). The
font is Dalamud's default: the game font, Noto Sans CJK and FontAwesome; no other font Dalamud ships
has these characters. `FontFallback.Fold` rewrites a title for display only (the cache, Copy and
Hidden venues' matching keep it as written), in one pass:

- **Small capitals** (ᴀ ʙ ᴄ … ʜ ʀ ᴛ ᴜ) are always the lower-case letter, even when the font has a
  glyph: ʜ and ʀ are FontAwesome icons in it. "Tʜᴇ Aᴜʀᴏʀᴀ" shows as "The Aurora".
- Any other character the font has is kept.
- **Look-alikes**, only when the font lacks the character and has the stand-in: Canadian syllabics
  used as letters (ᗩ A, ᗷ B, ᑕ C, ᗪ D, ᗴ E, ᖴ F, ᕼ H, ᒍ J, ᒪ L, ᗰ M, ᑎ N, ᑭ P, ᑫ Q, ᖇ R, ᔕ S,
  ᑌ U, ᐯ V, ᗯ W), ⅃ L, Ǝ E, ✦ ◆, ✧ ◇, ✩ ✰ ☆, ✪–✯ ★, ❤ ♥, and the Javanese ornaments ꧁ ꧂ as « ».
- **Compatibility forms** whose plain form the font has: the bold, italic and Fraktur "maths" letters
  (𝗛𝗢𝗪𝗟 → HOWL, 𝟙 → 1) and full-width letters, by Unicode's NFKC decomposition.
- Anything else the font lacks, such as emoji, joiners and variation selectors, is left out. When
  something is left out, runs of spaces close up and trailing spaces go ("✦ 🌴 ROSE" → "◆ ROSE").
- Whitespace is always kept. ImGui's characters are 16-bit, so nothing beyond U+FFFF counts as in the font.
- Text that needs no change is returned as it is. Each title is folded once per font, not per frame.

### Tags and SFW

`ListingTags` gives each cached listing a set of interests and decides whether it counts as NSFW,
for the browser's filters (`d68d407e`) and detail pane (`5d36be20`). Both are worked out from the
fields the source sent, so a change to the keyword lists applies to cached listings without fetching
again.

- **Interests**: Clubs (and DJs), Bars (and taverns), Cafes (and food), Live music (and open stage),
  Games (contests and giveaways too), Gambling, Roleplay, Spa (and baths), Art, Photography, Tarot,
  and Chill (AFK, open house, 24/7). A listing has any number of them; the median venue ad has two.
- **Keywords**: developer-maintained lists in `ListingTags`, read from an ad's text, an event's
  title or a venue's name after Party Finder glyphs are decoded (boxed letters read as letters) and
  case is folded. A word is a run of letters and digits, keeping `/` between digits (`24/7`,
  `50/50`) and `+` next to a digit (`18+`); other characters split words, so `Bar/DJs` and
  `twitch.tv/name` give their words. Keywords match whole words, or a phrase's consecutive words,
  and a word ending in "s" also matches without it (`bars`, `hot springs`); "starts" is not "art"
  and "team" is not "tea". Japanese keywords (喫茶, 雑談, 読書, 見学, 珈琲, ...) match anywhere in the
  text. Gambling shorthand (`DRT`, `BJ`, `50/50`, raffle) counts as Gambling, and bingo, trivia,
  prizes and giveaways as Games; a Twitch link or "party" counts as Clubs. On the labelled corpus's 274 venue ads, 254 get at least one interest; the
  lists were tuned on those ads.
- **Source tags**: FFXIV Venues' owner-chosen tags and Partake's event tags map to interests,
  ignoring case and surrounding spaces ("Bath house" → Spa, "Live DJs" → Clubs, "Cafes, Taverns &
  Inns" → Cafes and Bars). Tags with no matching interest (Courtesans, VIP, LGBTQIA+) add none. A
  venue's or event's interests are its tags' and its name's or title's keywords together.
  FFXIV RP Event Calendar events are always Roleplay.
- **SFW or NSFW**: the browser's safety filter is SFW only. A listing that marks itself neither
  SFW nor NSFW but has an age restriction counts as NSFW. An explicit word in the text means the
  word `NSFW` (NSFW, even if it also says SFW) or `SFW`; an age restriction is `18+`, `21+`, `+18`,
  `R18`, `R-18`, "adults only", 18禁 or 成人向け, not a bare number ("W18"). Per source:
  - FFXIV Venues: its `sfw` field decides; a venue cached before it was kept counts as SFW until
    the next fetch.
  - Partake: an explicit word in the title wins; otherwise `ageRating` `ADULT` (any case) is NSFW,
    and `MATURE`, `EVERYONE` or none is SFW.
  - FFXIV RP Event Calendar: an explicit word in the name wins; otherwise Adults Only (the rating,
    or the `[AO]` tag when there is none) is NSFW, and Mature and Teen are SFW: Mature's 16+ is a
    content rating, not an age restriction.
  - Party Finder ads: an explicit word wins; otherwise an age restriction is NSFW.
- **Listings shown as one**: every member's interests, and NSFW when any member is: NSFW always
  wins over SFW, even over an FFXIV Venues venue marked SFW.

### Listing browser

`/jaunt` and the installer's Open button toggle the browser; `/jaunt config` and the cog in its title
bar open Settings. The window has the list on the left and the selected listing's details on the
right, under a line of search and filters. The narrow single-column layout (`2dce5769`)
and copy link (`6e2b5199`) are separate tasks.

- **Ingest**: every Party Finder ad in the None category the game sends is recorded in the listing
  cache as `InGamePartyFinder`, keyed by the PF listing ID, with its text (`description`, glyphs
  included) and the world it is listed on (`listedOn`). Its title is the first line of the
  normalised text, cut to 60 characters with "…" when longer. The cache is saved 5 seconds after
  the first change since the last save, and on unload, not once per listing.
- **Which listings**: in-game and xivpf Party Finder ads with at least one parsed address, FFXIV
  Venues venues with an address, Partake events, and FFXIV RP Event Calendar events with an address
  or a location, in the travel region. An entry with a hidden member (below) isn't shown. An xivpf ad the game has also sent shows once,
  usually as the in-game copy (see "xivpf"). A listing with no opening to list (below) isn't shown.
  Duplicates show as one entry (below).
- **Openings** (`Schedules`), all instants in UTC:
  - FFXIV Venues: 24/7 (see "FFXIV Venues") is always open. Otherwise each schedule entry's
    `resolution`, the occurrence the API worked out when fetched, is carried forward by whole weeks of
    UTC for an entry repeating every N weeks (interval type 0) until it hasn't ended, and gives that
    occurrence and the one N weeks later. An entry repeating by week of the month keeps only its
    resolution, until it ends. An entry without a resolution (a venue cached before it was kept)
    gives none until the next fetch. Recurring. Schedule overrides are ignored.
  - Partake: its start and end. One-off.
  - FFXIV RP Event Calendar: its occurrences; one-off when the calendar says it doesn't recur.
  - Party Finder ads: the times read from the text (see "Opening times in Party Finder ads"), in the
    zone it writes, else the zone of a venue or event at its address (see "Duplicates"), else the
    region default for the character's region (a European ad's language isn't
    known, so London). An ad is still listed for 1 hour after it was last seen (a PF ad's longest
    life), plus the source's cooldown for xivpf. "24/7" is always open while still listed. "Open
    now", or a closing time alone, is open until that time while still listed; once that time has
    passed it is closed, even if the ad is still up. A start is listed as
    stated, with its end if given; with no end it counts as open from its start only while still
    listed. An ad with no time is open now while still listed (decision `ab014655`). One-off.
- **Listed openings**: a recurring listing shows its current opening and the next one, or only the
  next when it isn't open. A one-off shows each opening that hasn't ended and starts within 30 days.
  A 24/7 listing shows once, as open now. An opening ending exactly now is over.
- **Duplicates** (`Duplicates`): listings at the same address, in one source or several, whose listed
  openings overlap show as one entry, so a re-posted ad, a venue's several ads and an ad for an FFXIV
  Venues venue each show once. Names never decide a match (decision `25e33e0a`).
  - The address is the first one a listing gives: world (case ignored), district, ward, and plot or
    apartment (with whether it is in the subdivision), or neither for one naming only the ward. A room
    is left out. One with no world or no district never merges.
  - Two openings overlap when each starts before the other ends; an opening with no end counts as the
    moment it starts, and one with no start as starting now. A 24/7 listing overlaps every opening.
    Listings merge when any pair of their listed openings overlap, and merging chains: if A overlaps B
    and B overlaps C, all three are one entry. The same address at times that don't overlap stays
    separate.
  - A listing naming only the ward ("Fae / LB / W4") merges with others naming only that ward, and
    also with the plot or apartment in the ward whose listings it overlaps, when there is exactly one.
    With two or more, it joins none of them, as a busy ward can have several venues open at once.
  - The entry lists every member's listed openings, overlapping ones combined into one from the
    earliest start to the latest end; it is 24/7 when any member is, and one-off only when every member
    is.
  - Its title is FFXIV Venues' name, else a Partake event's, else an RP calendar event's, else an ad's.
    Its addresses and world are those of a member naming a plot or apartment rather than only the
    ward: a Party Finder ad's, as an ad can name the room, else FFXIV Venues', else the calendar's, else
    Partake's. Its links are every member's, the title's source first, each once; so different pages
    at one address become links of one entry. About is the text of the ad or event that stops being
    listed soonest (for an ad, last seen plus 1 hour, plus the cooldown for xivpf), as it is about the
    opening listed; failing that, the FFXIV Venues description. The footer names every member's
    source, plus the calendar's licence and the Partake line when a member is from them.
  - Selecting any member selects the entry, and a saved selection of any member still finds it.
  - An ad's times that state no zone take the zone of an FFXIV Venues venue at its address; failing
    that (no venue, or one cached before zones were kept), the zone among those ads write in which
    one of its start times falls exactly on a start a venue or Partake event lists there (Partake
    sends no zone). Otherwise the region default stands. This is worked out before the overlap, so
    an ad on Twintania for a US venue merges with it: the Orchid House ad's "7pm" with no zone is New
    York's 7pm, as FFXIV Venues says, not London's (decision `aca1be81`).
- **Groups**, by the local day an opening starts on: "Open now" for openings in progress, then
  "Today" with the weekday dimmed for later today, "Tomorrow" and the weekday ("Tomorrow Monday"), then
  one group per date, the date in the Settings date format without its weekday ("17 Nov", "Nov 17"
  or "2026-11-17") and the weekday dimmed. Within a group, earliest start first, then title (case
  ignored), then source and ID, so the order is stable. In "Open now", openings with a known start
  come first, then those without one, then 24/7 listings. Times show in the local time zone in the
  Settings time format.
- **Count**: above the panes, at the right, "14 listings" ("1 listing") counts the entries listed,
  each once however many of its openings are listed.
- **Rows**: the icon (see "Icons and splash images"), a rounded square about 32 px at the default
  scale (no taller than the row) level with the first line, then three lines, each cut with "…" to the pane's
  width: the status, the title in a font a quarter larger than the default, and the world dimmed. The status is "Open now · until 23:00" in green
  ("until Mon 16 Nov 23:00" when it closes more than a day ahead), "Open now" when the end isn't
  known, "Open now · 24h", or for a later opening its times ("20:00–23:00", or "20:00" without an end).
  Rows out of view are spaced, not drawn. The list is rebuilt every second. An opening that ends
  before then shows "Closed" in the row and detail pane, and is left out when the list is next
  grouped (on the rebuild or a filter change).
- **Detail pane**, top to bottom, each part only when the listing has it: the splash image band
  (see "Icons and splash images"); the title in the larger font; the status as in the rows, but a later opening with its date ("Sat 21 Nov
  20:00–23:00"); the world;
  Travel and Open page (the first link that isn't a Discord invite); Location: each address with Copy,
  a location given only as text, and any further links; About: an ad's normalised text, or else the
  description (see "Descriptions"), cut with "…" to 6 lines with "Read more" (then "Show
  less"), with "Read the rest on the website" after a description cut at the limit; Age / rating: an RP calendar event's rating; then a footer line: "Discord" linking to the
  first Discord invite among the links (`discord.gg/…`, or `discord.com/invite/…` and
  `discordapp.com/invite/…`), which Location then leaves out, then its sources, comma separated
  ("Discord · Sources: FFXIV Venues, xivpf"; the in-game Party Finder is "Party Finder"), each source
  but Party Finder linking to its site, and the calendar's licence link after them; for a Partake
  event, a line that Jaunt is not affiliated with or endorsed by Partake. Links in the footer, as
  in Settings, are in a link colour and underlined, show their address and a hand cursor on hover,
  and open in the browser when clicked. Who listed an ad
  ("Listed by …") is `d12eb2c4`.
- **Marks** (`EntryMarks`, `PlaceMarks`): three square icon buttons at the right of the detail pane's
  title, which wraps short of them: a star (Favourite), a tick (Visited) and a crossed-out eye (Hide).
  Icons are drawn a little over half the button's height, centred by their drawn shape.
  - **What a mark goes on**: a Party Finder ad's listing ID changes with every post (61% of venue
    addresses showed 2+ IDs over 31 hours of xivpf polls), so marks go on the venue's housing address
    (a *place*: world, district, ward, and plot or apartment, as `AddressKey`), and every listing there
    from any source carries them, now or later. Visited on a Partake or RP calendar event goes on that
    event instead, so one event doesn't mark the host venue's regular nights. An entry naming no plot
    or apartment marks its event, else each of its ads. Names never decide (decision `25e33e0a`), so
    two venues sharing a house share marks.
  - **Tooltips** say what will carry the mark: "Favourite Siren, Empyreum, W16, P20: every listing
    there, from any source, now or later", "Mark this event visited", "Hide these ads"; a lit button
    says "Remove from favourites", "Mark not visited" or "Un-hide".
  - **Matching**, from the surest evidence down (measured in `spikes/mark-identity`):
    1. The same FFXIV Venues ID as one seen at a place: the entry carries it. A place at one of the
       entry's addresses stays put; otherwise it moves to the entry's first address, as the venue's
       owner has said where it is now, unless an entry still at its address shows another of its IDs.
       Two venues sharing a house are both seen there, so the one that moves leaves the marks with
       the one that stays.
    2. The same address: any of the entry's plot or apartment addresses, the one it shows first first.
    3. A website the place has seen, shown by an entry with no place at another address: the place moves
       there, unless an entry still at the place's address shows that website or shows none (an ad
       without links may be the venue's own). Websites are compared by host without "www." and path,
       ignoring case. Links to people and single events (Twitch, Kick, YouTube, X, Instagram, TikTok,
       Bluesky, SoundCloud, Mixcloud, Partake) are never a place's website: about half the
       non-Discord links in venue ads are DJs' Twitch channels.
    - Discord invites never move a place on their own: 5% of FFXIV Venues' invites are shared across
      venues (brand and community servers). Names, managers and opening times aren't used; opening
      patterns are spike `e5a21c9f`.
  - **Learning**: a matched place keeps every FFXIV Venues ID, website and Discord invite its entries
    show. Its title and address as shown (for the hidden list) are taken when it is marked or moves, and
    otherwise only filled in, so two entries at one place don't take turns. A move onto a place with
    marks makes them one place with every mark and everything either saw.
  - **Showing**: an entry shows a mark when its place or any of its listings carries it. Its button is
    lit (the star yellow, the tick green), and its row shows the star and tick in the top right of the
    row's highlight, inset from the top and right by the same gap; the status line is cut short of them.
  - **Clearing**: clicking a lit button clears that mark from the place and every listing, so a mark
    set before the entry merged with another still comes off.
  - **Hide**: listings are merged before hidden ones are left out, and an entry whose place or any
    listing is hidden isn't listed, so hiding a venue also hides the ads listed as it. Hide takes the
    entry out of the list at once and says where to un-hide it (Settings → Hidden venues), unless
    Show hidden is on.
  - **Earlier marks**: favourite and hidden marks on the listings of an entry with a plot or apartment,
    and visited on all but its event, move to its place when it is next listed.
  - Marks are saved to `marks.json` (see "Listing cache") as soon as one changes, and when matching
    moves a place or learns something.
- **Selection**: clicking a row selects its listing (both rows of a listing listed twice). The
  selection is saved in the settings (`SelectedSource`, `SelectedId`), so it shows again when the
  window is reopened or the game restarted, and is forgotten once the listing is no longer listed. A
  saved selection with an unknown source, or only one of the two, is cleared on load. A listing the
  filters leave out stays selected, and its details show again once they let it through.
- **Filters and search** (`ListingFilters`): above the list, a search box, then chips that wrap onto
  more lines when the window is narrow, with the count of entries listed at the right. Every change
  applies at once and is saved in the settings (`Filters`), so it stays set when the window is
  reopened; the search text is saved once the box loses focus. An entry is listed when all of these let it through:
  - **SFW only** (on by default) leaves out entries counted as NSFW (see "Tags and SFW").
  - **Favourites** lists only entries showing the favourite mark; **Hide visited** leaves out those
    showing the visited mark.
  - **Show hidden** lists hidden entries too; otherwise they are left out (see "Hide").
  - **Tags** opens a menu of interests to tick; the entry must have every one ticked. Each ticked
    interest also shows as a lit chip ("Bars ×") that unticks it when clicked.
  - **Search**: space-separated terms must all match, ignoring case, anywhere in the entry's
    titles as shown (every member's), its About text or its interests' labels. `-term` leaves out
    entries containing it; `"two words"` matches the phrase, and `-"two words"` leaves it out. A
    phrase must be within one of those texts. An unclosed quote runs to the end of the box; a lone
    `-` or `""` is ignored. Hovering the box shows the syntax.
- **Empty states**: with nothing selected the detail pane says "Select a listing on the left to see
  its details." With nothing to list, the list shows a line picked at random from a few ("No one here
  but us chickens."), chosen again each time the list becomes empty, and the detail pane is blank.
  When there are entries but the filters let none through (hidden ones count as entries), the list
  says "No listings match these filters." and suggests removing some or clearing the search, and the
  detail pane is blank.
- **Region in the list**: a listing is in the travel region by the same fields it is filtered on
  when recorded (`TravelRegion.Includes`): the world it is listed on, or for a Partake event that
  world or its data centre; an FFXIV Venues venue by its address's world as shown, misspellings
  corrected. The list follows a character switch within a second.
  While no character is logged in (or its home world is unknown) the window shows only "Log in to a
  character to see the venues it can travel to." Nothing else in the window mentions the region.
  Hidden venues in Settings lists every hidden listing, whatever its region.
- **Copy** beside an address copies "World, District, W20, P28": a missing world or district is left out; an
  apartment is "W5, Subdivision, Apartment 12" (Subdivision only when the ad says so); a room adds
  ", Room B1". Districts use their English names. This is the shape Lifestream's address book reads.
  A world taken from the listing is marked "(world the ad is listed on)".
- **Travel** calls Lifestream's IPC: `Lifestream.IsBusy`, then `Lifestream.BuildAddressBookEntry`
  (world, district, ward, plot or apartment, is apartment, is subdivision) and
  `Lifestream.GoToHousingAddress` with the entry it returns. It is disabled, with a tooltip saying
  why, when Lifestream isn't loaded or the address lacks a world, district, or both plot and
  apartment. A plot wins over an apartment; a room is not sent. If Lifestream is busy or fails, a
  notification says so and suggests copying the address (risk `de04e687`).
- **Open page** opens the first link's URI (any further links are listed under Location, each with Open) in the browser. `WebLinks` only ever returns http or https URIs,
  so another player's ad can't make it open anything else; the button's tooltip shows the full URI.

### Icons and splash images

Each entry in the browser shows an icon in its row and a splash image at the top of its detail pane.
`ImageSources` picks where each may come from, in order, and `ImageStore` fetches, converts and keeps
them; the browser draws them.

- **Sources**, from the entry's members and links, at most two of each kind, each once:
  - Icon: the server icon of each Discord invite among its links, then the icon of each website FFXIV
    Venues (the venue's `website`) or Partake (the host's `websiteUrl`) gives, then a square from the
    FFXIV Venues banner. Server icons are made to read as small squares;
    a banner's middle square is often a dark scene, so it comes last (decision with the user, `185b66ad`).
  - Splash: the FFXIV Venues banner, then each invite's server banner, else its invite splash, then
    the Partake event's attachments and then its description's images.
  - Websites in Party Finder ads and RP calendar pages aren't used: any player writes an ad's links,
    and fetching their icon would tell the ad's author the address of everyone who scrolls past it
    (decision with the user, `185b66ad`). Invites from any member are, as they are looked up only on
    Discord. Of the websites used, only those places compare count (see "Matching" under "Marks"):
    links to people and single events (Twitch, Partake) would show their icon, not the venue's. An
    invite code that isn't letters, digits and "-", at most 32, is skipped.
- **Discord**: `https://discord.com/api/v10/invites/<code>`, which needs no sign-in, gives the server's
  ID and its icon, banner and splash hashes; the images are `cdn.discordapp.com` PNGs (icon at
  128 px, banner and splash at 1024 px). An invite to a group DM, or one Discord doesn't know, gives
  none. Each invite is asked once per session for both kinds.
- **Website icons** (`Favicons`): the site's home page (scheme and host, path "/"), read up to 512 KB,
  and the icons its `<link>` tags name: `apple-touch-icon` first, then `icon` by the largest of its
  `sizes`, then `/favicon.ico`, at most three, each once. SVG icons are skipped, as they can't be
  decoded. Relative addresses are read against where redirects led. A home page that isn't there
  still tries `/favicon.ico`.
- **Fetching**: only when the row or detail pane is drawn (rows out of view are spaced, not drawn), so
  opening the browser doesn't fetch every entry's images. Three fetches run at once, off the game
  thread. A source tries the next one in order when it has no image: not found or another refusal, a
  host that is unknown or refuses the connection, data that isn't an image Skia can decode, or more
  than 8 MB (Partake has sent 60 MB GIFs). A timeout, a 429 or a 5xx stops there and tries the same
  sources again 10 minutes later, while the game runs, showing any kept image meanwhile.
- **Only the public internet**: image and website addresses come from other players' ads, so every
  connection, redirects included, goes only to a public address: never loopback, private (10/8,
  172.16/12, 192.168/16, fc00::/7), shared (100.64/10), link-local, multicast or reserved ranges,
  checked on the address a name resolves to (`PublicNetwork`). Image requests use no proxy, as the
  check is on the address connected to. Requests send Jaunt's version and repository as their
  User-Agent, nothing about the player.
- **Converting** (`ImageProcessing`): decoded with SkiaSharp (PNG, JPEG, WebP, GIF, ICO, BMP; the first
  frame of an animation), as every FFXIV Venues banner and most Partake images are WebP, which the
  game's own decoder can't read under Wine. An image over 16 million pixels isn't decoded. An icon is
  its middle square at 64 px; a splash image is at most as tall as it is wide (its middle part) and at
  most 960 px wide, keeping its shape. Both are kept as PNG.
- **Keeping**: in the plugin's config folder under `images/`: one PNG per image address and kind, named
  by a hash of both, so the same picture is downloaded once for any entry; and `index.json`
  (`version` 1), recording for each entry's kind and sources the file they gave, or none, and when.
  Reopening the window or restarting the game shows kept images without fetching. A kept image's
  sources are tried again when shown 7 days after (a server's new icon has a new address), showing
  the old one meanwhile; an address already kept isn't downloaded again. Sources that gave none are
  tried again when shown a day after. On load, entries not worked out for 30 days are dropped, and
  so is any file no entry names. An unreadable index starts empty, and a file name in it that isn't
  one the store makes is ignored.
- **Drawing**: a plain rounded box in the frame colour shows until the image is kept, and stays when
  there is none. The image covers the box, cropped to its shape about the middle: the row's square,
  or the detail pane's band (as wide as the pane, 0.3 of its width tall, at most 140 px at the
  default scale). Textures are loaded by Dalamud from the kept PNGs.
- The plugin ships only SkiaSharp's 64-bit Windows native library: the game is always 64-bit Windows,
  also under Wine and XIV on Mac.

## Limits

Caps on user-configurable lists and text, and how input from other players or the game is bounded.

- Address parsing reads a bounded number of words around each address, plus one pass over the ad for
  a full world name when an address names none. Ads are at most a few hundred characters, so this stays small.
- The listing cache is a dictionary per source keyed by ID, so recording a listing doesn't grow
  with the cache. Expiry bounds its size to the listings seen in the last month plus the marked recurring ones.
  A save copies only the changed sources' listings on the game thread (about 0.1 ms for 400 in-game
  ads, 0.5 ms for 1,400 venues) and serialises them off it. Loading every file takes about 20 ms for
  2,700 listings; working out every listing's address and schedule once takes about 130–280 ms, on the
  list's first rebuild, as before.
- Finding duplicates groups listings by address with one dictionary pass per rebuild, then compares
  the listings at each address pairwise. An address has a handful of listings at most, so this stays
  linear in the number of listings.
- The refresh cooldown is entered as a number and clamped to 10–1440 minutes, in the window and in
  `Normalize`.
- Link finding is one pass over the normalised ad with no regex, so it stays linear in the ad's length.
- Tagging a listing is one pass over its normalised text into words, a dictionary lookup per word
  (and per word of a phrase starting there), and one substring search per Japanese keyword (about
  40), with no regex, so it stays linear in the text's length. The browser tags each listing once per
  change to the fields tagging reads, not once per rebuild.
- Filtering is one pass over the entries, with search as a case-insensitive substring check per term
  and text (a few titles, About of at most 1,501 characters, a few labels). It runs on each rebuild
  and each filter or search change, without working out schedules, merges or marks again. The search
  text is parsed once per change.
- An xivpf response (about 700 listings, 1.6 MB) is read in one pass over its listings, off the
  game thread, keeping about 150 ads; their text is at most a few hundred characters. Checking
  whether the game also sent an ad is one cache lookup.
- A Partake fetch is at most 5 pages of 200 events, read off the game thread. Each event's location
  is parsed once per text, like an ad; its title, world and links are worked out once per rebuild
  of the list (once a second), about 150 events.
- An FFXIV Venues fetch is at most one request per data centre in the region (4 for North America and
  Japan, 2 for Europe, 1 for Oceania), each read in one pass over its venues, off the game thread; a venue's links
  are two plus any invites in its description, and titles are cut to 60 characters when shown. The list (about 750 of 1,400 venues,
  1.6 MB) keeps `cache/FfxivVenues.json` under about 1.1 MB, of which descriptions are about 260 KB.
- Images are fetched only for entries drawn, three at a time, each source a handful of requests at
  most (an invite lookup or home page, then an image); downloads stop at 8 MB and home pages at
  512 KB. Decoding refuses images over 16 million pixels. Finding an entry's sources happens once per
  rebuild of the list for each row drawn; asking for a kept image each frame is a field check that
  allocates nothing. Kept files are about 8 KB per icon and up to about 1 MB per splash image, and
  those not shown for 30 days are deleted on load.
- Cleaning a description is one pass over its lines plus a link search, once per fetch, off the game
  thread; the cleaned text is at most 1,501 characters. The list derives a venue's title, address, links and
  24/7 once per change to its cached fields, not per frame.
