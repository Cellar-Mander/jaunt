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
district shorthand (`LB`, `Lav`, `Gob`, `Shiro`, `Emp`, `Em`, ラベ, ゴブ, エンピ…) and data-centre
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

## Limits

Caps on user-configurable lists and text, and how input from other players or the game is bounded.

- Address parsing reads a bounded number of words around each address, plus one pass over the ad for
  a full world name when an address names none. Ads are at most a few hundred characters, so this stays small.
