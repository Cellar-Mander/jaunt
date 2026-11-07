#!/usr/bin/env python3
"""Spike d41391d0: compare captured xivpf listings with FFXIV Venues.

Usage: python3 -I analyse.py [captures-dir]
Writes a summary to stdout and per-venue detail to <captures>/analysis/ (gitignored: it holds
player names and ad text).

Rough by design: the address parser here is a measuring stick for the spike, not the parser
Jaunt will ship (that is task eceb260e).
"""
import calendar
import collections
import csv
import glob
import gzip
import json
import os
import re
import sys
import unicodedata
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo

CAP = sys.argv[1] if len(sys.argv) > 1 else "captures"
OUT = os.path.join(CAP, "analysis")
POLL_GAP = timedelta(minutes=30)  # polls further apart than this start a new capture window

DCS = {  # data centre: (region, worlds)
    "Aether": ("NA", "Adamantoise Cactuar Faerie Gilgamesh Jenova Midgardsormr Sargatanas Siren"),
    "Crystal": ("NA", "Balmung Brynhildr Coeurl Diabolos Goblin Malboro Mateus Zalera"),
    "Primal": ("NA", "Behemoth Excalibur Exodus Famfrit Hyperion Lamia Leviathan Ultros"),
    "Dynamis": ("NA", "Cuchulainn Golem Halicarnassus Kraken Maduin Marilith Rafflesia Seraph"),
    "Chaos": ("EU", "Cerberus Louisoix Moogle Omega Phantom Ragnarok Sagittarius Spriggan"),
    "Light": ("EU", "Alpha Lich Odin Phoenix Raiden Shiva Twintania Zodiark"),
    "Materia": ("OCE", "Bismarck Ravana Sephirot Sophia Zurvan"),
    "Elemental": ("JP", "Aegis Atomos Carbuncle Garuda Gungnir Kujata Tonberry Typhon"),
    "Gaia": ("JP", "Alexander Bahamut Durandal Fenrir Ifrit Ridill Tiamat Ultima"),
    "Mana": ("JP", "Anima Asura Chocobo Hades Ixion Masamune Pandaemonium Titan"),
    "Meteor": ("JP", "Belias Mandragora Ramuh Shinryu Unicorn Valefor Yojimbo Zeromus"),
}
REGION = {dc: r for dc, (r, _) in DCS.items()}
WORLD_DC = {w: dc for dc, (_, ws) in DCS.items() for w in ws.split()}
WORLDS_LC = {w.lower(): w for w in WORLD_DC}
PREFIXES = collections.defaultdict(list)  # every 3+ letter prefix -> worlds it could abbreviate
for _w in WORLD_DC:
    for _n in range(3, len(_w) + 1):
        PREFIXES[_w.lower()[:_n]].append(_w)
JP_WORLD_KANA = {"槍": "Gungnir", "タイタン": "Titan", "ティアマト": "Tiamat", "パンデモ": "Pandaemonium",
                 "アトモス": "Atomos", "ベリアス": "Belias", "ラムウ": "Ramuh", "マンドラ": "Mandragora"}

_DISTRICT_ALIASES = {
    "Mist": ["mist", "mists", "ミスト"],
    "Lavender Beds": ["lb", "lav", "lavender", "lavender beds", "lavenderbeds", "lavbed", "lavbeds",
                      "ラベ", "ラベンダーベッド"],
    "Goblet": ["gob", "goblet", "ゴブ", "ゴブレット"],
    "Shirogane": ["shiro", "shirogane", "シロガネ", "白銀"],
    "Empyreum": ["emp", "empy", "empyreum", "empyr", "エンピ", "エンピレアム"],
}
DISTRICT_OF = {a: d for d, aliases in _DISTRICT_ALIASES.items() for a in aliases}
_ascii = sorted((a for a in DISTRICT_OF if a.isascii()), key=len, reverse=True)
_kana = sorted((a for a in DISTRICT_OF if not a.isascii()), key=len, reverse=True)
RE_DISTRICT = re.compile("(?<![a-z])(" + "|".join(map(re.escape, _ascii)) + ")(?![a-z])|("
                         + "|".join(map(re.escape, _kana)) + ")")
# Tokens that look like world prefixes but mean something else in venue ads.
NOT_WORLD = {"gob", "lav", "emp", "empy", "mist", "shiro", "est", "pst", "cst", "the", "and",
             "open", "now", "night", "all", "bar", "club", "live", "fri", "sat", "sun", "mon",
             "tue", "wed", "thu", "rp", "nsfw", "sfw", "dj", "djs", "plot", "ward", "apt",
             "come", "join", "free", "with", "for", "new", "art", "sea", "ali", "alex",
             "raffle", "raffles", "bis", "mates", "cry", "sin", "mate", "hope", "sir", "shi"}


# SeIconChar.Number0..9, BoxedNumber0..9 and BoxedLetterA..Z; other private-use glyphs become spaces.
ICONS = {**{0xE060 + i: str(i) for i in range(10)}, **{0xE08F + i: str(i) for i in range(10)},
         **{0xE071 + i: chr(ord("a") + i) for i in range(26)}}
ICONS.update({c: " " for c in range(0xE000, 0xF900) if c not in ICONS})


def normalise(text):
    return unicodedata.normalize("NFKC", text.translate(ICONS))


NUM = r"(\d{1,2})"
SEP = r"\s*[-ー－−/,.|•・:]?\s*"
RE_WARD_PLOT = [
    re.compile(r"(?<![a-z])w(?:ard)?\s*#?\.?\s*" + NUM + SEP + r"(?:p(?:lot)?|p\.)\s*#?\.?\s*" + NUM + r"(?!\d)", re.I),
    re.compile(r"(?<![a-z])p(?:lot)?\s*#?\.?\s*" + NUM + SEP + r"w(?:ard)?\s*#?\.?\s*" + NUM + r"(?!\d)", re.I),
    re.compile(NUM + r"(?:st|nd|rd|th)\s*w(?:ard|\.)?" + SEP + r"(?:p(?:lot)?\.?\s*)?" + NUM + r"\s*p?(?!\d)", re.I),
    re.compile(r"ward\s*" + NUM + SEP + r"plot\s*" + NUM, re.I),
    re.compile(NUM + r"\s*区\s*" + NUM + r"\s*番?"),
]
RE_PLOT_THEN_WARD_ORD = re.compile(r"plot\s*" + NUM + r".{0,6}?" + NUM + r"(?:st|nd|rd|th)\s*ward", re.I)
RE_APT = re.compile(r"(?:apt|apartment|apa|アパ(?:ルトメント)?|room)\s*#?\.?\s*(\d{1,3})", re.I)
RE_DASH = re.compile(NUM + r"\s*[-ーｰ－−]\s*" + NUM + r"(?!\d)")
RE_URL = re.compile(r"(?:https?://)?((?:[a-z0-9-]+\.)+(?:com|co|gg|net|org|io|me|fun|xyz|club|site|art|link|app))", re.I)
RE_DISCORD = re.compile(r"discord(?:\.gg|app\.com/invite|\.com/invite)/([A-Za-z0-9-]+)", re.I)


def find_district(t):
    m = RE_DISTRICT.search(t.lower())  # earliest mention wins
    return DISTRICT_OF[m.group(1) or m.group(2)] if m else None


def world_candidates(t, region=None):
    """(position, world) for each token naming a world: an exact name, or an abbreviation that
    is a prefix of exactly one world (within the poster's region when one is given)."""
    out = []
    for m in re.finditer(r"[A-Za-z]+", t):
        tl = m.group().lower()
        if tl in WORLDS_LC:
            out.append((m.start(), WORLDS_LC[tl]))
            continue
        if len(tl) < 3 or tl in NOT_WORLD:
            continue
        hits = PREFIXES.get(tl, [])
        if len(hits) > 1 and region:
            hits = [w for w in hits if REGION.get(WORLD_DC[w]) == region]
        if len(hits) == 1:
            out.append((m.start(), hits[0]))
    for k, w in JP_WORLD_KANA.items():
        i = t.find(k)
        if i >= 0:
            out.append((i, w))
    return out


def find_world(t, near=None, region=None):
    """The world named closest to the address (venue names often contain world names)."""
    c = world_candidates(t, region)
    return min(c, key=lambda pw: abs(pw[0] - near))[1] if c else None


def parse_address(raw):
    """Return dict(district, ward, plot|apartment) or None. World is resolved separately."""
    t = normalise(raw)
    district = find_district(t)
    ward = plot = apt = None
    pos = None
    for n, rx in enumerate(RE_WARD_PLOT):
        m = rx.search(t)
        if m:
            pos = m.start()
            a, b = int(m.group(1)), int(m.group(2))
            if n == 1:  # plot before ward
                a, b = b, a
            ward, plot = a, b
            break
    if ward is None:
        m = RE_PLOT_THEN_WARD_ORD.search(t)
        if m:
            plot, ward, pos = int(m.group(1)), int(m.group(2)), m.start()
    if ward is None and district:
        m = RE_DASH.search(t[t.lower().find(district.lower()[:3]):] if district.isascii() else t)
        if not m:
            m = RE_DASH.search(t)
        if m:
            ward, plot = int(m.group(1)), int(m.group(2))
            pos = t.find(m.group(0))
    ma = RE_APT.search(t)
    if ma and district:
        apt = int(ma.group(1))
        if ward is None:
            mw = re.search(r"(?<![a-z])w(?:ard)?\s*" + NUM, t, re.I) or re.search(NUM + r"(?:st|nd|rd|th)?\s*ward", t, re.I)
            if mw:
                ward = int(mw.group(1))
    if ward is None or not district or not 1 <= ward <= 30:
        return None
    if apt and (plot is None or not 1 <= plot <= 60):
        return {"district": district, "ward": ward, "plot": None, "apartment": apt, "pos": pos or ma.start()}
    if plot is None or not 1 <= plot <= 60:
        return None
    return {"district": district, "ward": ward, "plot": plot, "apartment": None, "pos": pos}


def canon_world(w):
    """FFXIV Venues has a few misspelt worlds (e.g. 'Bismark')."""
    hits = [x for x in WORLD_DC if x.lower()[:5] == w.lower()[:5]]
    return hits[0] if w not in WORLD_DC and len(hits) == 1 else w


def addr_key(world, a):
    unit = ("apt", a["apartment"]) if a["plot"] is None else ("plot", a["plot"])
    return (world, a["district"], a["ward"]) + unit


def links(raw):
    t = normalise(raw).lower()
    out = {"discord:" + m.group(1).lower() for m in RE_DISCORD.finditer(t)}
    return out | {m.group(1).removeprefix("www.") for m in RE_URL.finditer(t) if "discord" not in m.group(1)}


def norm_name(s):
    return re.sub(r"[^a-z0-9]", "", unicodedata.normalize("NFKC", s).lower())


def ts(s):
    return datetime.fromisoformat(re.sub(r"\.\d+", "", s.replace("Z", "+00:00")))  # 3.10 can't parse 5-digit fractions


def occurrences(v, lo, hi):
    """Open intervals of venue v (UTC) overlapping [lo, hi), from its schedule and overrides."""
    out = []
    for s in v.get("schedule") or []:
        if not (tzname := (s.get("start") or {}).get("timeZone")):
            continue
        tz = ZoneInfo(tzname)
        it, arg = s["interval"]["intervalType"], s["interval"]["intervalArgument"]
        comm = ts(s["commencing"]).astimezone(tz).date() if s.get("commencing") else None
        d = (lo - timedelta(days=2)).astimezone(tz).date()
        last = (hi + timedelta(days=1)).astimezone(tz).date()
        while d <= last:
            if d.weekday() == s["day"] and (comm is None or d >= comm or it != 0):
                ok = True
                if it == 0 and arg > 1:
                    ok = comm is not None and ((d - comm).days // 7) % arg == 0
                elif it == 1:
                    if arg > 0:
                        ok = (d.day - 1) // 7 + 1 == arg
                    else:
                        ok = d.day + 7 > calendar.monthrange(d.year, d.month)[1]
                if ok:
                    st = datetime(d.year, d.month, d.day, s["start"]["hour"], s["start"]["minute"], tzinfo=tz)
                    e = s["end"]
                    if e is None:  # the API resolves a missing end as start + 3 h
                        en = st + timedelta(hours=3)
                    else:
                        ed = d + timedelta(days=1 if e.get("nextDay") else 0)
                        en = datetime(ed.year, ed.month, ed.day, e["hour"], e["minute"], tzinfo=tz)
                        if en <= st:
                            en += timedelta(days=1)
                    out.append((st.astimezone(timezone.utc), en.astimezone(timezone.utc)))
            d += timedelta(days=1)
    for o in v.get("scheduleOverrides") or []:
        st, en = ts(o["start"]), ts(o["end"])
        if o["open"]:
            out.append((st, en))
        else:
            out = [iv for iv in out if iv[1] <= st or iv[0] >= en]
    return [iv for iv in out if iv[1] > lo and iv[0] < hi]


def next_start(v, after):
    return min((st for st, en in occurrences(v, after, after + timedelta(days=150)) if en > after), default=None)


def sched_sig(v):
    """Schedule and overrides without the API's time-relative fields (resolution, isNow)."""
    sch = [{k: x for k, x in s.items() if k != "resolution"} for s in v.get("schedule") or []]
    ovr = [{k: x for k, x in o.items() if k != "isNow"} for o in v.get("scheduleOverrides") or []]
    return json.dumps([sch, ovr], sort_keys=True)


def poll_time(path):
    return datetime.strptime(os.path.basename(path).split(".")[0], "%Y%m%dT%H%M%SZ").replace(tzinfo=timezone.utc)


def main():
    os.makedirs(OUT, exist_ok=True)
    pf_files = sorted(glob.glob(os.path.join(CAP, "pf", "*.json.gz")))
    ven_files = sorted(glob.glob(os.path.join(CAP, "venues", "*.json.gz")))
    polls = [poll_time(f) for f in pf_files]

    windows = []
    for t in polls:
        if windows and t - windows[-1][1] <= POLL_GAP:
            windows[-1][1] = t
        else:
            windows.append([t, t])
    windows = [(a, b + timedelta(minutes=10)) for a, b in windows]

    # ----- PF: one pass over every poll -----
    listing_seen = collections.defaultdict(set)  # listing id -> poll indexes
    listing = {}
    for i, f in enumerate(pf_files):
        with gzip.open(f) as fh:
            for x in json.load(fh):
                lst = x["listing"]
                listing_seen[lst["id"]].add(i)
                listing[lst["id"]] = lst

    stats, clusters = collections.Counter(), {}  # clusters: address key -> listings at that address
    for lid, lst in listing.items():
        raw = (lst["description"] or {}).get("en") or ""
        stats["listings"] += 1
        cat_none = "None" if lst["category"] == "None" else "other"
        a = parse_address(raw)
        if not a:
            stats["none_unparsed"] += cat_none == "None"
            continue
        stats["addressed"] += 1
        stats["addressed_cat_" + cat_none] += 1
        created = lst["created_world"]["name"]
        world = find_world(normalise(raw), a["pos"], REGION.get(WORLD_DC.get(created, "")))
        world, world_src = (world, "text") if world else (created, "created_world")
        stats["world_from_" + world_src] += 1
        c = clusters.setdefault(addr_key(world, a), {"listings": set(), "polls": set(), "texts": [],
                                                     "links": set(), "world_src": set()})
        c["listings"].add(lid)
        c["polls"] |= listing_seen[lid]
        c["texts"].append(raw)
        c["links"] |= links(raw)
        c["world_src"].add(world_src)

    # ----- FFXIV Venues -----
    snaps = {poll_time(f): f for f in ven_files}
    # Per capture window: schedule from the snapshot at its start; changes checked at its end.
    win_snap = [tuple(min(snaps, key=lambda t: abs(t - edge)) for edge in w) for w in windows]

    snap_cache = {t: {v["id"]: v for v in json.load(gzip.open(snaps[t]))} for t in {x for p in win_snap for x in p}}

    # validate the schedule maths against the API's own next-opening ("resolution")
    t0 = min(snap_cache)
    bad = []
    agree = 0
    for v in snap_cache[t0].values():
        r = v.get("resolution")
        if not r or not v.get("schedule"):
            continue
        mine = next_start(v, t0)
        theirs = ts(r["start"]).astimezone(timezone.utc)
        if mine and (abs(mine - theirs) < timedelta(minutes=1) or (r.get("isNow") and mine <= theirs + timedelta(minutes=1))):
            agree += 1
        else:
            bad.append((v["name"], mine, theirs))

    open_at = collections.defaultdict(set)  # venue id -> poll indexes when scheduled open
    changed = set()
    for (a, b), (sf, sl) in zip(windows, win_snap):
        vs, ve = snap_cache[sf], snap_cache[sl]
        in_window = [(i, t) for i, t in enumerate(polls) if a <= t < b]
        for vid, v in vs.items():
            occ = occurrences(v, a, b)
            if vid in ve and sched_sig(ve[vid]) != sched_sig(v):
                changed.add(vid)
                occ += occurrences(ve[vid], a, b)
            open_at[vid].update(i for i, t in in_window if any(st <= t < en for st, en in occ))
    all_venues = {vid: v for t in sorted(snap_cache) for vid, v in snap_cache[t].items()}

    ven_key = {}  # address from the start-of-window snapshots, later windows winning
    for sf, _ in win_snap:
        for vid, v in snap_cache[sf].items():
            loc = v["location"] or {}
            if loc.get("world") and loc.get("district") and loc.get("ward") and (loc["plot"] or loc["apartment"]):
                ven_key[vid] = addr_key(canon_world(loc["world"]), {"district": loc["district"], "ward": loc["ward"],
                                        "plot": loc["plot"] or None, "apartment": loc["apartment"] or None})
    key_to_ven = collections.defaultdict(list)
    key_noworld = collections.defaultdict(list)  # same address on any world, to spot misread worlds
    for vid, k in ven_key.items():
        key_to_ven[k].append(vid)
        key_noworld[k[1:]].append(vid)
    name_index = collections.defaultdict(list)
    link_index = collections.defaultdict(set)
    for vid, v in all_venues.items():
        if len(n := norm_name(v["name"])) >= 6:
            name_index[n].append(vid)
        for lk in links(" ".join(u for u in (v.get("website"), v.get("discord")) if u)):
            link_index[lk].add(vid)

    def region_of(world):
        return REGION.get(WORLD_DC.get(world, ""), "?")

    rows = []
    matched_ven = set()
    cat = collections.Counter()
    by_region = collections.Counter()
    for k, c in clusters.items():
        world = k[0]
        reg = region_of(world)
        by_region[reg] += 1
        addr_hits = key_to_ven.get(k, [])
        texts_norm = " ".join(norm_name(normalise(x)) for x in c["texts"])
        name_any = {vid for n, vids in name_index.items() if n in texts_norm for vid in vids}
        name_hits = {vid for vid in name_any
                     if REGION.get(all_venues[vid]["location"].get("dataCenter") or "") == reg}
        link_hits = {vid for lk in c["links"] for vid in link_index.get(lk, ())}
        ident_hits = name_hits | link_hits
        # Same ward/plot on another world plus a name or link match: the world was misread or differs.
        other_world = [vid for vid in key_noworld.get(k[1:], []) if vid in (name_any | link_hits)]
        world_issue = ""
        if not addr_hits and other_world:
            addr_hits = other_world
            world_issue = "world_differs" if c["world_src"] == {"text"} else "world_fallback_wrong"
        if addr_hits:
            basis = "address" + (f" ({world_issue})" if world_issue else "")
        else:
            basis = "+".join(b for b, h in (("link", link_hits), ("name", name_hits)) if h)
        if world_issue == "world_differs":
            status = "both_world_differs"
        elif addr_hits:
            status = "both"
            matched_ven.update(addr_hits)
        elif ident_hits:
            status = "both_address_differs"
            matched_ven.update(ident_hits)
        elif reg == "JP":
            status = "pf_only_jp"
        else:
            status = "pf_only"
        cat[status] += 1
        hits = addr_hits or sorted(ident_hits, key=lambda v: all_venues[v]["name"])
        open_polls = set().union(*(open_at.get(v, set()) for v in hits))
        rows.append({
            "status": status, "basis": basis, "region": reg, "world": world, "district": k[1], "ward": k[2],
            "unit": f"{k[3]} {k[4]}", "world_src": "/".join(sorted(c["world_src"])),
            "polls_seen": len(c["polls"]), "listings": len(c["listings"]),
            "fv_match": "; ".join(all_venues[v]["name"] for v in hits),
            "fv_address": "; ".join("{}/{}/W{}/{} {}".format(*ven_key[v]) for v in hits if v in ven_key),
            "fv_scheduled_open_during_pf": bool(open_polls & c["polls"]),
            "same_addr_other_world": "; ".join(all_venues[v]["name"] + "@" + ven_key[v][0]
                                               for v in key_noworld.get(k[1:], []) if ven_key[v][0] != world),
            "sample_text": normalise(c["texts"][0])[:160],
        })

    fv_open = {vid for vid, s in open_at.items() if s}
    fv_only = fv_open - matched_ven
    fv_matched_open = fv_open & matched_ven
    for vid in fv_only:
        v, loc = all_venues[vid], all_venues[vid]["location"]
        rows.append({"status": "fv_only", "region": REGION.get(loc.get("dataCenter") or "", "?"),
                     "world": loc.get("world"), "district": loc.get("district"), "ward": loc.get("ward"),
                     "unit": f"plot {loc.get('plot')}" if loc.get("plot") else f"apt {loc.get('apartment')}",
                     "polls_seen": 0, "listings": 0, "fv_match": v["name"], "fv_scheduled_open_during_pf": True})

    with open(os.path.join(OUT, "venues.csv"), "w", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0]), restval="")
        w.writeheader()
        order = ("status", "region", "world", "district", "ward", "unit", "fv_match")
        w.writerows(sorted(rows, key=lambda r: tuple(str(r[f]) for f in order)))

    # ----- hourly vs 10-minute sampling -----
    na_eu_oce = {k: c for k, c in clusters.items() if region_of(k[0]) != "JP"}
    hourly = [sum(1 for c in na_eu_oce.values() if c["polls"] & set(range(off, len(polls), 6))) for off in range(6)]
    single_poll = sum(1 for c in na_eu_oce.values() if len(c["polls"]) == 1)

    # ----- listing lifetimes -----
    lifetimes = collections.Counter(
        "1 poll" if n == 1 else "2-3 polls" if n <= 3 else "4-6 polls" if n <= 6 else "7+ polls"
        for c in na_eu_oce.values() for n in (len(listing_seen[lid]) for lid in c["listings"]))

    print("Capture windows (UTC):")
    for (a, b), (sf, sl) in zip(windows, win_snap):
        n = sum(1 for t in polls if a <= t < b)
        print(f"  {a:%Y-%m-%d %H:%M} to {b:%Y-%m-%d %H:%M}  {n} polls  venues snapshots {sf:%H:%M} / {sl:%H:%M}")
    print(f"PF: {stats['listings']} unique listings; {stats['addressed']} with a parsed housing address "
          f"({stats['addressed_cat_None']} category None, {stats['addressed_cat_other']} other); "
          f"{stats['none_unparsed']} category-None listings without one")
    print(f"  world from ad text {stats['world_from_text']}, fallback to created_world {stats['world_from_created_world']}")
    print(f"PF addresses (distinct venues by address): {len(clusters)}  by region {dict(by_region)}")
    print(f"FFXIV Venues: {len(snap_cache[t0])} venues; schedule maths agrees with API next-opening for {agree}, "
          f"disagrees for {len(bad)}")
    print(f"  scheduled open at >=1 poll: {len(fv_open)}; schedule/override edited during a window: {len(changed)}")
    print("Overlap (NA/EU/OCE PF addresses vs FFXIV Venues):")
    for s in ("both", "both_world_differs", "both_address_differs", "pf_only", "pf_only_jp"):
        print(f"  {s}: {cat[s]}")
    print(f"  fv_only (scheduled open, never seen in PF): {len(fv_only)}; FV venues open and seen in PF: {len(fv_matched_open)}")
    both_rows = [r for r in rows if r["status"] == "both"]
    print(f"  of 'both': FV schedule says open while PF ad up: {sum(r['fv_scheduled_open_during_pf'] for r in both_rows)}/{len(both_rows)}")
    other = sum(1 for r in rows if r["status"] == "pf_only" and r["same_addr_other_world"])
    print(f"  PF-only addresses that match an FV venue on another world: {other}")
    print(f"Hourly sampling finds {min(hourly)}-{max(hourly)} of {len(na_eu_oce)} NA/EU/OCE PF addresses "
          f"(10-minute); {single_poll} addresses were up for only one 10-minute poll")
    print(f"NA/EU/OCE listing lifetimes in polls: {dict(lifetimes)}")
    print(f"Detail: {os.path.join(OUT, 'venues.csv')}")
    if bad:
        print("Schedule mismatches:", *[f"  {n}: mine {m} api {t}" for n, m, t in bad[:15]], sep="\n")
    print(f"Address-differs rows: {cat['both_address_differs']}")


if __name__ == "__main__":
    main()
