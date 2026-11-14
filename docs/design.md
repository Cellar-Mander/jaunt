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
  after the ad was seen; with a day, one before 06:00 is after that day's midnight. An end at or
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
- **Last fetched**: the listing cache records when each fetched source was last requested,
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

- `marks.json`: each marked listing's source, ID, title and one-off flag, and its three marks.
  Written when a mark changes, when a marked listing's title, ad text or one-off flag changes, and
  when a marked listing expires. The file it replaces is kept as `marks.json.bak`.
- `fetch-state.json`: each source's last fetch time (`lastFetched`). Written on each fetch.
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
- Unknown source names, entries without an ID and, in fetch times, the in-game Party Finder are
  dropped. A saved opening that doesn't end after it starts is dropped, and a Partake event that
  had ended when last seen is dropped.
- A mark whose listing no source file holds still adds the listing, with the mark's title, so the
  hidden list shows it. It was last seen at no known time, so a marked one-off expires (see below).
  A listing whose title is empty takes the mark's title.
- With neither marks file, a `listings.json` from an earlier version (listings, marks and fetch
  times together) is read, written out as the files above and, once all are written, renamed to
  `listings.json.old`. An unreadable one is moved to `listings.json.bad` and the cache starts empty.

- **Clear cached listings** asks for confirmation, warning that rate-limited sources may not show
  in the browser again for hours, then removes every listing that is not favourited, visited or
  hidden and says how many it removed. Cancelling removes nothing.
- **Expiry**: on load, a listing that is not favourited, visited or hidden is dropped once one
  calendar month has passed since it was last seen (last seen 14 Oct 20:00 → dropped from 14 Nov
  20:00). Marked recurring listings are kept however old. Marked one-offs expire the same way as
  unmarked listings, since they can't reliably be matched to a later listing; the window doesn't
  mention this. Clear still keeps marked one-offs.
- **Hidden venues** lists hidden listings by title, case-insensitively, each with an Un-hide button.
  Titles are drawn as in "Text the font can't draw".

### FFXIV Venues

`FfxivVenues` reads `https://api.ffxivvenues.com/v1.0/venue?approved=true&withinWeek=true` in one
response: approved venues (the API's default, pinned) with an opening in the coming week, as the API
works out from each venue's schedule. A venue left out stays in the cache with its fields and marks,
and in the venue list, until it expires (see "Listing cache"). `FfxivVenues`
records each in the listing cache as `FfxivVenues`, keyed by the venue's ID, with the fields below as
the API sent them: its name as the title, and its location, website, Discord and schedule. The title,
address, links and 24/7 shown are derived from those fields when listed, once per change, so a change
to how they are read applies to cached venues without fetching again. A venue cached without them
shows no address until the next fetch. Opening the venue list fetches it, unless the source is within its
cooldown (8 hours, or the Settings cooldown if longer); the request is recorded before it is sent,
so a failed one counts too. Download and parsing run off the game thread.

- **Title**: the venue's name, first line only, trimmed and cut to 60 characters like an ad's.
- **Address**: from the venue's location: the world, a district (`Mist`, `Lavender Beds`, `Goblet`,
  `Shirogane`, `Empyreum`, with or without "The"), a ward of 1–30, then a plot of 1–60, or else an
  apartment over 0 with the location's subdivision flag, and a room over 0. A plot wins over an
  apartment. A world the game data doesn't know is corrected to the one world sharing its longest
  beginning of at least 5 letters ("Bismark" → Bismarck), and otherwise kept as written. A venue with
  no world, an unknown district or a ward outside 1–30 (such as one giving only a free-text location)
  has no address and isn't in the venue list.
- **Links**: the website then the Discord, http or https only. One without a scheme is read like a
  link in ad text (`discord.gg/abc` opens as https); any other scheme is dropped, including in a
  hand-edited cache. The same link twice is kept once.
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
listing ID, the same ID the game gives the listing. Opening the venue list fetches it, unless the
source is within its cooldown (the Settings cooldown, 10 minutes at least); the request is recorded
before it is sent, so a failed one counts too. Download and parsing run off the game thread.

- **Category**: only listings whose category is exactly `None`.
- **Text**: the ad's English text (`description.en`), glyphs included, so the in-game Party Finder's
  parsers read it unchanged. xivpf gives the text once per client language, differing only in
  auto-translate phrases; when the English is missing or blank, the first other language that isn't.
  An ad with no text in any language is skipped.
- **Title** as for an in-game ad: the first line of the normalised text, cut to 60 characters.
- **Listed on**: the world the ad was put up on (`created_world.name`), trimmed, as the in-game
  Party Finder gives it; none when it is missing or blank.
- **Seen in both**: when the in-game Party Finder has sent a listing with the same ID, the list
  shows only the in-game copy, whatever its marks; the xivpf copy stays in the cache. Recording an
  xivpf ad moves the in-game copy's last seen time up to now (never back), since it was listed then.
- A listing without a whole-number ID of 0 or more, or whose fields have the wrong types, is skipped
  without failing the others. A response that isn't a JSON array fails the fetch, which is logged,
  and the cached ads stay.
- Only the fields above are read; others, such as the recruiter's name and the party's jobs, are
  never stored.

### FFXIV RP Event Calendar

`RpEventCalendar` reads `https://api.ffxiv-rp.org/api/Events/GetWeekTranslatableEvents`, about three
weeks of events with one row per occurrence, and records each event in the listing cache as
`RpEventCalendar`, keyed by its calendar ID (`uId`, trimmed), which its occurrences share. Opening
the venue list fetches it, unless the source is within its cooldown (8 hours, or the Settings
cooldown if longer, so at most 3 fetches a day); the request is recorded before it is sent, so a
failed one counts too. Download and parsing run off the game thread.

**Stored as sent**: the cache keeps `eventName` (as the listing's title), `server` (as the world
it is listed on), `location`, `esrbRating` and `eventURL` exactly as the calendar sent them, an empty title when
the name is missing. The title, address, location and rating below are derived from those when
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
- **Links**: `eventURL`, stored as sent; shown only if http or https, read like FFXIV Venues' links.
- **Openings**: each occurrence's `startTimeUTC` and `endTimeUTC`, earliest first, the same one
  kept once. An occurrence without both, or that doesn't end after it starts, is skipped; the
  event keeps its other occurrences. Each fetch replaces an event's openings.
- **One-off**: an event whose `isRecurring` is `false`.
- **Validated**: the day part of `lastValidated`, or none when it is missing or unreadable.
- A row without an ID, or whose fields have the wrong types, is skipped without failing the others.
  A response that isn't a JSON array fails the fetch, which is logged, and the cached events stay.
- Only the fields above are read. `contacts` and `description` (which repeats the contacts) hold
  Discord handles and are never stored or shown.
- **Credit**: the calendar's listings are CC BY-SA 4.0, so each of its rows shows "FFXIV RP Event
  Calendar", opening `https://ffxiv-rp.org/`, and "CC BY-SA 4.0", opening the licence.

### Partake

`Partake` reads FFXIV events from Partake's public GraphQL API, `POST https://api.partake.gg/` with
no authentication, and records each in the listing cache as `Partake`, keyed by the event's ID, as a
one-off. Opening the venue list fetches it, unless the source is within its cooldown (8 hours, or
the Settings cooldown if longer); the request is recorded before it is sent, so a failed one counts
too. Download and parsing run off the game thread.

- **Raw values**: the cache keeps the title, world, data centre, location and links exactly as
  Partake sent them (links in `linkTexts`). The rules below for showing them run each time they
  are shown, so a change to these rules or the address parser applies on the next plugin load,
  not after the next fetch.
- **Query**: `events(game: "final-fantasy-xiv", startsBetween: {start}, sortBy: STARTS_AT_ASC)`
  with `start` 24 hours before now, so events already under way are included. Pages of 200 (`limit`,
  `offset`) until a page comes back with fewer, at most 5 pages per fetch. About 150 events are
  listed at once, so one page is usual. Only the fields shown are asked for: `id`, `title`,
  `startsAt`, `endsAt`, `location`, `server.name`, `dataCenter.name` and the host team's
  `websiteUrl` and `discordUrl`. `server` and `dataCenter` work but aren't in Partake's docs.
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
- **Links**: the host's website then Discord, shown like FFXIV Venues links: http or https only. On
  load, the cache drops missing link texts.
- An event is skipped, without failing the others, when it lacks an ID or either time, its title
  or world is missing or blank, its end is not after its start, or its fields have the wrong types.
  A response with GraphQL `errors` (Partake answers those with HTTP 200), without `data.events`, or
  that isn't JSON fails the fetch, which is logged, and the cached events stay.
- Events that have ended by the time a fetch is recorded aren't recorded, and the list leaves out
  cached events once they end.
- **Credit**: while the list shows any Partake event it ends with "Events from Partake (partake.gg).
  Jaunt is not affiliated with or endorsed by Partake.", as Partake's terms ask apps not to imply
  endorsement. Each event's source reads "Partake". Hidden venues shows a hidden event's title as
  the list does.

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

### Venue list (MVP)

`/jaunt` and the installer's Open button toggle the venue list; `/jaunt config` opens Settings. The
two-pane browser (`cdd9e95f`) replaces this list.

- **Ingest**: every Party Finder ad in the None category the game sends is recorded in the listing
  cache as `InGamePartyFinder`, keyed by the PF listing ID, with its text (`description`, glyphs
  included) and the world it is listed on (`listedOn`). Its title is the first line of the
  normalised text, cut to 60 characters with "…" when longer. The cache is saved 5 seconds after
  the first change since the last save, and on unload, not once per listing.
- **List**: in-game and xivpf Party Finder listings with at least one parsed address, FFXIV
  Venues venues with an address, Partake events that haven't ended and FFXIV RP Event Calendar
  events with an address or a location, not hidden, most recently seen first and then by title,
  each with its source ("Party Finder", "Party Finder (xivpf)", "FFXIV Venues", "Partake", or the
  calendar's credit). Partake events show their times and world on a second line (see "Partake").
  An xivpf ad the game has
  also sent shows once, as the in-game copy (see "xivpf"). Each row shows "open 24/7" when it is,
  and "seen … ago". A calendar event also shows, in local time and the Settings formats, "On now
  until 23:00" for an opening in progress, else "Next Sat 15 Nov 20:00–23:00" for the next one,
  else "No upcoming dates"; then "validated 30 Aug 2025" (the date with its year) or "never
  validated"; then its location when it has no address. Titles are drawn as in "Text the font can't draw". Each ad is parsed once per text, not per frame. Rows out of view
  are spaced by their last drawn height and not drawn.
- **Copy address** copies "World, District, W20, P28": a missing world or district is left out; an
  apartment is "W5, Subdivision, Apartment 12" (Subdivision only when the ad says so); a room adds
  ", Room B1". Districts use their English names. This is the shape Lifestream's address book reads.
  A world taken from the listing is marked "(world the ad is listed on)".
- **Travel** calls Lifestream's IPC: `Lifestream.IsBusy`, then `Lifestream.BuildAddressBookEntry`
  (world, district, ward, plot or apartment, is apartment, is subdivision) and
  `Lifestream.GoToHousingAddress` with the entry it returns. It is disabled, with a tooltip saying
  why, when Lifestream isn't loaded or the address lacks a world, district, or both plot and
  apartment. A plot wins over an apartment; a room is not sent. If Lifestream is busy or fails, a
  notification says so and suggests copying the address (risk `de04e687`).
- **Open** opens the link's URI in the browser. `WebLinks` only ever returns http or https URIs,
  so another player's ad can't make it open anything else; the button's tooltip shows the full URI.

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
- The refresh cooldown is entered as a number and clamped to 10–1440 minutes, in the window and in
  `Normalize`.
- Link finding is one pass over the normalised ad with no regex, so it stays linear in the ad's length.
- An xivpf response (about 700 listings, 1.6 MB) is read in one pass over its listings, off the
  game thread, keeping about 150 ads; their text is at most a few hundred characters. Checking
  whether the game also sent an ad is one cache lookup.
- A Partake fetch is at most 5 pages of 200 events, read off the game thread. Each event's location
  is parsed once per text, like an ad; its title, world and links are worked out once per rebuild
  of the list (once a second), about 150 events.
- An FFXIV Venues response is read in one pass over its venues, off the game thread; a venue's links
  are at most two, and titles are cut to 60 characters when shown. The list (about 750 of 1,400 venues,
  1.6 MB) keeps `listings.json` under about 800 KB. The list derives a venue's title, address, links and
  24/7 once per change to its cached fields, not per frame.
