# Design

What Jaunt does, stated precisely enough to test against. Mutation triage checks
surviving mutants against this file (`spec=` in `.claude/checks`), so write down each behaviour the
tests should hold, including edge cases and limits.

## Behaviour

### Opening times in Party Finder ads

`OpeningTimes.Parse` reads normalised ad text. `OpeningTimesCorpusTests` checks every labelled corpus ad (`Corpus/README.md`, `times`).

- **Open now**: "open now", "now open", "is open", a bare "NOW", 営業中 and similar. A bare "open"
  doesn't count when a time, "from", "tonight" and the like follow it, or a word says what is open
  ("Open Stage", "Open Recruitment", "open to all"). "starts now" and "now hiring" don't count.
  A closing time with no start ("open until 1am") belongs to the now opening.
- **Always**: "24/7" and "always open". 24/7 is never a date.
- **Times** need AM/PM, a zone, minutes or 時 after the hour; a bare number ("Gob 14-35", "W30") is
  not a time. Bare hours 1–11 read as evening ("8-12" is 20:00–00:00); a leading zero, an hour over
  12 or Japanese text in the ad means the 24-hour clock. In a range, the side without AM/PM takes
  the reading within 12 hours of the other. A dot between hour and minutes needs AM/PM.
- **Zones** are kept as written, upper-cased; ST is server time. A lower-case "st" after 1 or 21
  is an ordinal.
- **Days**: today/tonight/tomorrow apply to the whole ad, the first one written wins. A weekday
  applies only to a time or date right after it ("FRI 17 ST"), so "Goth Friday" names an event. A
  date beats both. a/b dates take whichever of month/day and day/month is nearer the day seen,
  month/day on a tie; a year is never written, so the nearest year is used.
- Parsing is one pass over tokens per step, with no regex, so it stays linear in the ad's length.

## Limits

Caps on user-configurable lists and text, and how input from other players or the game is bounded.
