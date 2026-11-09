# Labelled Party Finder corpus

`pf-listings.jsonl` holds 391 Party Finder ads from category `None` (venues, events and everything
else posted there), each labelled by hand for kind, name, addresses, times and URLs. The parsers for
addresses (`eceb260e`), links (`368d1e40`) and opening times (`a7558238`) are tested against it.
`PfCorpusTests` keeps the labels consistent with the text.

## Where the ads come from

Captured 2026-11-05 to 2026-11-07 from two sources:
- xivpf polls every 10 minutes (`spikes/pf-vs-venues/poll.sh`), all regions
- the plugin's dev-build capture (`pf-capture/*.jsonl`), NA only

`tools/corpus/select_pf.py` merges them and picks the ads with a fixed seed:
- Two sightings are one ad when the text is identical. Capture time, listing ID and seconds
  remaining are dropped. The worlds it was seen on are kept in `listedOn`.
- Ads that differ in any character are kept, even when their labels come out the same.
- Every ad matching a `DC-World-District-` code is kept (the FFXIV Venues Directory failure case).
  The rest are sampled per region: venue-like ads by keyword (NA 110, JP 60, all of EU and OCE)
  plus ads that match no venue keyword (NA 15, EU 10, JP 10, all of OCE).

The keyword filter skews the sample towards ads that look like venues; the non-venue samples only
partly offset that.

Poster names, content IDs and home worlds are never read. Ads can still name people (DJs,
performers, characters); `privacy` lists what each entry contains. Every flagged entry was reviewed
on 2026-11-08. Public DJ, performer and venue names were kept. Other personal names and handles
were replaced in `text` and labels with invented ones of the same shape, and those entries say so in
`privacy`.

## Line format

```json
{"id": "pf-0002", "region": "EU", "listedOn": ["Twintania"], "firstSeen": "2026-11-05T16:14:21Z",
 "sources": ["xivpf"], "selection": "venue-keyword", "text": "…raw ad text…",
 "labels": {...}, "privacy": []}
```

`text` is the ad as the game sends it, with its private-use glyphs. Labels describe what a reader of
the rendered ad understands. That reading of the glyphs is `Jaunt.Core.Text.PartyFinderText`, and
`PartyFinderTextCorpusTests` checks every labelled name against it:
- Boxed letters and digits read as plain letters and digits, including BoxedNumber10–31
  (`U+E099`–`U+E0AE`) and the boxed E `U+E0B0`. A digit followed by a two-digit glyph is a time:
  "7" then boxed 30 reads 7:30.
- Some glyphs are letters inside a word and decoration elsewhere: Circle `U+E04A` and Buff `U+E05C`
  as O, `U+E05F` (an x in a box) as X, and `U+E032` (a V-shaped chevron) as V. GlamouredDyed
  `U+E04E` reads as +.
- Time glyphs read as words: `U+E06D`/`U+E06E` = AM/PM, `U+E0D0`–`U+E0D2` = LT/ST/ET.
  Some ads use `U+E06B` (LevelDe, which looks like "St") for ST, and `U+E0D2` (EorzeaTimeEn) for
  US Eastern.
- Other glyphs, stars, hearts, notes, arrows and bullets are decoration; ArrowDown `U+E035` is a
  line break.

| Field | Meaning |
|---|---|
| `kind` | `venue` (a venue or a night at one), `event` (a one-off not tied to a named venue), `housing` (a house to look round, no staffed opening), `shop`, `recruiting` (FC, staff, members), `other` |
| `name` | `{raw, normalised}` or null. `raw` is the name as written, with glyphs decoded as above. It is the span the ad marks as the name (brackets, quotes, boxed letters). Without such marks, it is the noun phrase, including a generic word written as part of it ("Cafe Canopy", "酒処桜い"). Event titles, DJ names and tags are not names. `normalised` is NFKC, case-folded, `&` read as "and", each punctuation mark turned into a space ("Warden's" → "warden s") and a leading "the" dropped (so "LEATHER&LACE" matches FFXIV Venues' "Leather and Lace"). |
| `addresses[]` | `world`, `worldFrom`, `district`, `ward`, `plot`, `subdivision`, `apartment`, `room`. `world` is null when the text names none; `worldFrom` is then `listing` and the world is the one the ad is listed on (`listedOn`). `subdivision` is true or false only when the text says so. `room` is a private chamber or room inside a house; `apartment` is a room number in an apartment building. |
| `times[]` | `now` (open now), `always` (24/7), `day` (`today`, `tomorrow`, `mon`–`sun` or an ISO date), `start`, `end` (24-hour `HH:MM`, as the ad means them; "8-12" in a venue ad is 20:00–00:00), `zone` (as written, e.g. `ST`, `EST`, `AEDT`; null when not stated, and the region default from `aca1be81` applies) |
| `urls[]` | each URL exactly as it appears in `text`, including broken ones |
| `notes` | anything ambiguous about the labels |

## Editing

If you obscure personal data in `text`, update any label that quoted it. `PfCorpusTests` fails when
a URL label no longer appears in the text.
