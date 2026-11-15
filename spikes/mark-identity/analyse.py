#!/usr/bin/env python3
"""How well each piece of listing data identifies a venue, for marks that outlive a listing (deb3b4be).

Usage: python3 -I analyse.py [captures-dir] [corpus.jsonl]
  captures-dir: spikes/pf-vs-venues/captures (gitignored): FFXIV Venues snapshots and that spike's
                per-address CSV
  corpus.jsonl: Jaunt.Core.Tests/Corpus/pf-listings.jsonl (hand-labelled PF ads)

Prints counts only; no names, IDs, links or ad text.
"""
import collections
import csv
import glob
import gzip
import json
import os
import re
import sys
import unicodedata

CAP = sys.argv[1] if len(sys.argv) > 1 else "../pf-vs-venues/captures"
CORPUS = sys.argv[2] if len(sys.argv) > 2 else "../../Jaunt.Core.Tests/Corpus/pf-listings.jsonl"


def norm_name(s):
    """As the corpus's `normalised`: NFKC, case-folded, & as "and", punctuation as spaces, no leading "the"."""
    s = unicodedata.normalize("NFKC", s or "").casefold().replace("&", " and ")
    s = "".join(c if c.isalnum() or c.isspace() else " " for c in s)
    s = " ".join(s.split())
    return s[4:] if s.startswith("the ") else s


def addr_key(world, district, ward, plot, apartment, subdivision):
    """Jaunt's AddressKey: world (case ignored), district, ward, plot or apartment; no room."""
    if not world or not district or not ward:
        return None
    if plot:
        return (world.lower(), district, ward, "p", plot)
    if apartment:
        return (world.lower(), district, ward, "a", apartment, bool(subdivision))
    return (world.lower(), district, ward, "w")


INVITE = re.compile(r"(?:discord(?:app)?\.(?:gg|com/invite)/)([A-Za-z0-9-]+)", re.I)


def link_keys(urls):
    """Discord invites by code, other links by host and path (no scheme, www, query or trailing slash)."""
    keys = set()
    for u in urls:
        if not u:
            continue
        m = INVITE.search(u)
        if m:
            keys.add(("discord", m.group(1)))
            continue
        u = re.sub(r"^[a-z]+://", "", u.strip(), flags=re.I)
        u = re.sub(r"^www\.", "", u, flags=re.I).split("?")[0].split("#")[0].rstrip("/")
        host, _, path = u.partition("/")
        keys.add(("web", host.lower() + ("/" + path if path else "")))
    return keys


def host_of(key):
    return key[1].split("/")[0] if key[0] == "web" else "discord"


def pct(n, d):
    return f"{n}/{d} ({100 * n / d:.0f}%)" if d else f"{n}/0"


def load_venues(path):
    with gzip.open(path) as f:
        return json.load(f)


def venue_key(v):
    loc = v.get("location") or {}
    return addr_key(loc.get("world"), loc.get("district"), loc.get("ward"), loc.get("plot"), loc.get("apartment"), loc.get("subdivision"))


def venue_links(v):
    return link_keys([v.get("website"), v.get("discord")])


def shared(index, label, of):
    """How many keys of a kind point at more than one venue address."""
    multi = sum(1 for addrs in index.values() if len(addrs) > 1)
    print(f"  {label}: {pct(multi, len(index))} shared by venues at different {of}")


def main():
    snaps = sorted(glob.glob(os.path.join(CAP, "venues", "*.json.gz")))
    first, last = load_venues(snaps[0]), load_venues(snaps[-1])
    venues = [v for v in last if venue_key(v)]
    print(f"FFXIV Venues snapshot {os.path.basename(snaps[-1])}: {len(venues)} venues with a housing address\n")

    print("1. Venue -> address (does an address name one venue?)")
    by_addr = collections.defaultdict(set)
    by_addr_room = collections.defaultdict(set)
    for v in venues:
        by_addr[venue_key(v)].add(v["id"])
        by_addr_room[(venue_key(v), (v.get("location") or {}).get("room") or 0)].add(v["id"])
    multi = [a for a, ids in by_addr.items() if len(ids) > 1]
    print(f"  addresses holding 2+ venues: {pct(len(multi), len(by_addr))}; venues at such an address: "
          f"{pct(sum(len(by_addr[a]) for a in multi), len(venues))}")
    print(f"  counting the room too: {pct(sum(1 for ids in by_addr_room.values() if len(ids) > 1), len(by_addr_room))}\n")

    print("2. Links, names and managers across venues (could they move a mark to the wrong venue?)")
    links_addr = collections.defaultdict(set)
    names_addr = collections.defaultdict(set)
    mgr_addr = collections.defaultdict(set)
    has_link = 0
    for v in venues:
        keys = venue_links(v)
        has_link += bool(keys)
        for k in keys:
            links_addr[k].add(venue_key(v))
        names_addr[norm_name(v.get("name"))].add(venue_key(v))
        for m in v.get("managers") or []:
            mgr_addr[m].add(venue_key(v))
    print(f"  venues with a website or Discord: {pct(has_link, len(venues))}")
    shared({k: a for k, a in links_addr.items() if k[0] == "discord"}, "Discord invites", "addresses")
    shared({k: a for k, a in links_addr.items() if k[0] == "web"}, "websites (host and path)", "addresses")
    hosts = collections.Counter(host_of(k) for k, a in links_addr.items() if k[0] == "web" and len(a) > 1)
    print(f"    shared websites by host: {dict(hosts.most_common(6))}")
    shared(names_addr, "names", "addresses")
    shared(mgr_addr, "managers", "addresses")
    print()

    print(f"3. Changes for the same venue ID between {os.path.basename(snaps[0])} and {os.path.basename(snaps[-1])}")
    before = {v["id"]: v for v in first}
    both = [v for v in venues if v["id"] in before and venue_key(before[v["id"]])]
    moved = [v for v in both if venue_key(v) != venue_key(before[v["id"]])]
    print(f"  venues in both: {len(both)}; address changed: {len(moved)}; "
          f"website or Discord changed: {sum(1 for v in both if venue_links(v) != venue_links(before[v['id']]))}; "
          f"name changed: {sum(1 for v in both if norm_name(v.get('name')) != norm_name(before[v['id']].get('name')))}")
    for v in moved:
        old = before[v["id"]]
        print(f"    moved: links kept {bool(venue_links(v) & venue_links(old))}, name kept "
              f"{norm_name(v.get('name')) == norm_name(old.get('name'))}, same world {venue_key(v)[0] == venue_key(old)[0]}")
    print()

    corpus = [json.loads(line) for line in open(CORPUS, encoding="utf-8")]
    ads = []
    for ad in corpus:
        lab = ad["labels"]
        if lab["kind"] != "venue":
            continue
        keys = []
        for a in lab["addresses"]:
            world = a["world"] or (ad["listedOn"][0] if ad["listedOn"] else None)
            keys.append(addr_key(world, a["district"], a["ward"], a["plot"], a["apartment"], a["subdivision"]))
        ads.append({
            "addr": next((k for k in keys if k), None),
            "name": (lab.get("name") or {}).get("normalised"),
            "links": link_keys(lab["urls"]),
        })
    print(f"4. Labelled corpus: {len(ads)} venue ads")
    print(f"  with an address: {pct(sum(1 for a in ads if a['addr']), len(ads))}; naming a plot or apartment: "
          f"{pct(sum(1 for a in ads if a['addr'] and a['addr'][3] != 'w'), len(ads))}")
    print(f"  with a name: {pct(sum(1 for a in ads if a['name']), len(ads))}; with a link: "
          f"{pct(sum(1 for a in ads if a['links']), len(ads))}; with a Discord invite: "
          f"{pct(sum(1 for a in ads if any(k[0] == 'discord' for k in a['links'])), len(ads))}")
    names = collections.defaultdict(set)
    addrs = collections.defaultdict(set)
    for a in ads:
        if a["name"] and a["addr"]:
            names[a["name"]].add(a["addr"])
            addrs[a["addr"]].add(a["name"])
    print(f"  names seen at 2+ addresses: {pct(sum(1 for s in names.values() if len(s) > 1), len(names))}")
    print(f"  addresses seen with 2+ names: {pct(sum(1 for s in addrs.values() if len(s) > 1), len(addrs))}\n")

    print("5. Corpus ads against FFXIV Venues (is a signal right when it matches?)")
    venue_by_addr = collections.defaultdict(list)
    venue_by_link = collections.defaultdict(list)
    venue_by_name = collections.defaultdict(list)
    for v in venues:
        venue_by_addr[venue_key(v)].append(v)
        for k in venue_links(v):
            venue_by_link[k].append(v)
        venue_by_name[norm_name(v.get("name"))].append(v)

    at_venue = [a for a in ads if a["addr"] in venue_by_addr]
    named = [a for a in at_venue if a["name"]]
    name_agrees = [a for a in named if any(norm_name(v.get("name")) == a["name"] or a["name"] in norm_name(v.get("name"))
                                           or norm_name(v.get("name")) in a["name"] for v in venue_by_addr[a["addr"]])]
    linked = [a for a in at_venue if a["links"]]
    link_agrees = [a for a in linked if any(a["links"] & venue_links(v) for v in venue_by_addr[a["addr"]])]
    print(f"  ads at an FFXIV Venues address: {pct(len(at_venue), len(ads))}")
    print(f"    their name matches the venue's (equal or contained): {pct(len(name_agrees), len(named))}")
    print(f"    a link matches the venue's: {pct(len(link_agrees), len(linked))}")

    by_link = [a for a in ads if any(k in venue_by_link for k in a["links"])]
    link_addr_agrees = [a for a in by_link if a["addr"] and any(venue_key(v) == a["addr"] for k in a["links"] for v in venue_by_link.get(k, []))]
    print(f"  ads sharing a link with a venue: {len(by_link)}; at that venue's address: {pct(len(link_addr_agrees), len(by_link))}")
    for kind in ("discord", "web"):
        sub = [a for a in ads if any(k in venue_by_link and k[0] == kind for k in a["links"])]
        ok = [a for a in sub if a["addr"] and any(venue_key(v) == a["addr"] for k in a["links"] if k[0] == kind for v in venue_by_link.get(k, []))]
        print(f"    by {kind}: {pct(len(ok), len(sub))}")

    by_name = [a for a in ads if a["name"] and a["name"] in venue_by_name]
    name_addr_agrees = [a for a in by_name if any(venue_key(v) == a["addr"] for v in venue_by_name[a["name"]])]
    print(f"  ads whose name equals a venue's: {len(by_name)}; at that venue's address: {pct(len(name_addr_agrees), len(by_name))}")
    print()

    path = os.path.join(CAP, "analysis", "venues.csv")
    if os.path.exists(path):
        with open(path, newline="", encoding="utf-8") as f:
            rows = [r for r in csv.DictReader(f) if int(r["polls_seen"] or 0) > 0]
        listings = [int(r["listings"]) for r in rows]
        print(f"6. xivpf, 139 polls over ~31 h: distinct listing IDs per address ({len(rows)} addresses)")
        print(f"  1 ID: {pct(sum(1 for n in listings if n == 1), len(rows))}; 2+: {pct(sum(1 for n in listings if n > 1), len(rows))}; "
              f"max {max(listings)}")


if __name__ == "__main__":
    main()
