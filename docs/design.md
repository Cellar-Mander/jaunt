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

## Limits

Caps on user-configurable lists and text, and how input from other players or the game is bounded.
