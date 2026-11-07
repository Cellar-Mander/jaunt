#!/usr/bin/env python3
"""Select Party Finder ads for the labelled corpus (task 16dee061).

Usage: python3 -I select_pf.py <xivpf-captures-dir> <ingame-capture-dir> <out.jsonl>

Reads xivpf polls (*.json.gz, from spikes/pf-vs-venues/poll.sh) and the plugin's dev-build
captures (*.jsonl), keeps category `None`, merges ads with identical text, and writes an
unlabelled candidate file. Poster names, content IDs and home worlds are never read into the
output. The selection is seeded, so a rerun on the same captures gives the same candidates.

Duplicates: two sightings are one ad when the text is identical. Fields that change over time
(capture time, listing ID, seconds remaining) are dropped; the worlds it was listed on are kept
as `listedOn`, because an address with no world in its text is read against the listing's world.
"""
import glob
import gzip
import json
import random
import re
import sys
import unicodedata
from datetime import datetime, timezone

SEED = 0x16DEE061  # fixed: reruns must pick the same ads
QUOTA = {  # region: (venue-like ads, other ads); None = take all
    "NA": (110, 15),
    "EU": (None, 10),
    "OCE": (None, None),
    "JP": (60, 10),
}

DCS = {
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
REGION_OF_WORLD = {w: r for r, ws in DCS.values() for w in ws.split()}

# SeIconChar digits and boxed letters, so keyword matching sees "W14" in boxed text.
ICONS = {**{0xE060 + i: str(i) for i in range(10)}, **{0xE08F + i: str(i) for i in range(10)},
         **{0xE071 + i: chr(ord("a") + i) for i in range(26)}}

# Deliberately broad: a miss here only moves an ad to the "other" pool, which is also sampled.
VENUE_LIKE = re.compile(
    r"venue|bar\b|club|lounge|caf[eé]|tavern|\binn\b|ward|plot|\bw\s?\d|\bp\s?\d|apt|apartment|mist|"
    r"lavender|\blb\b|goblet|shiro|empy|\bopen|tonight|\bdjs?\b|bath|\bspa\b|"
    r"ラベ|ミスト|ゴブ|シロガネ|エンピ|区|番地|営業|オープン|バー|カフェ", re.I)
# Failure cases named in the task: a DC-world-district-ward-plot code, and names that carry a
# time or a generic descriptor. Every match is kept on top of the quotas.
FAILURE_CASE = re.compile(
    r"\b(dyn|crys|prim|aeth|chaos|light|mat|elem|gaia|mana|meteor)[a-z]*\s*[-/|]\s*[a-z]{3,}\s*[-/|]",
    re.I)


def keyword_text(text):
    return unicodedata.normalize("NFKC", text.translate(ICONS))


def utc(stamp):
    """ISO-8601 with any offset and up to 7 fraction digits -> 'YYYY-MM-DDTHH:MM:SSZ'."""
    stamp = re.sub(r"(\.\d+)", "", stamp).replace("Z", "+00:00")
    return datetime.fromisoformat(stamp).astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def load(xivpf_dir, ingame_dir):
    ads = {}  # text -> merged record

    def see(text, world, when, source):
        if not text.strip() or world not in REGION_OF_WORLD:
            return
        when = utc(when)
        ad = ads.setdefault(text, {"text": text, "listedOn": set(), "sources": set(), "firstSeen": when})
        ad["listedOn"].add(world)
        ad["sources"].add(source)
        ad["firstSeen"] = min(ad["firstSeen"], when)

    for path in sorted(glob.glob(f"{xivpf_dir}/*.json.gz")):
        with gzip.open(path) as f:
            for entry in json.load(f):
                listing = entry["listing"]
                if listing["category"] == "None":
                    see(listing["description"]["en"], listing["created_world"]["name"],
                        entry["created_at"], "xivpf")
    for path in sorted(glob.glob(f"{ingame_dir}/*.jsonl")):
        with open(path, encoding="utf-8") as f:
            for line in f:
                if line.strip():
                    row = json.loads(line)
                    if row["category"] == "None":
                        see(row["description"], row["worldName"], row["capturedAt"], "ingame")
    return ads


def region(ad):
    # An ad listed in two regions is rare; take the alphabetically first world's region.
    return REGION_OF_WORLD[sorted(ad["listedOn"])[0]]


def select(ads):
    rng = random.Random(SEED)
    chosen = {}
    for text, ad in ads.items():
        if FAILURE_CASE.search(keyword_text(text)):
            chosen[text] = "failure-case"
    for reg, (n_venue, n_other) in QUOTA.items():
        pool = sorted(t for t, ad in ads.items() if region(ad) == reg and t not in chosen)
        venue = [t for t in pool if VENUE_LIKE.search(keyword_text(t))]
        other = [t for t in pool if t not in set(venue)]
        for group, n, label in ((venue, n_venue, "venue-keyword"), (other, n_other, "other-random")):
            picked = group if n is None or n >= len(group) else rng.sample(group, n)
            chosen.update((t, label) for t in picked)
    return chosen


def main():
    xivpf_dir, ingame_dir, out = sys.argv[1:4]
    ads = load(xivpf_dir, ingame_dir)
    chosen = select(ads)
    order = sorted(chosen, key=lambda t: (region(ads[t]), ads[t]["firstSeen"], t))
    with open(out, "w", encoding="utf-8") as f:
        for i, text in enumerate(order, 1):
            ad = ads[text]
            f.write(json.dumps({
                "id": f"pf-{i:04d}",
                "region": region(ad),
                "listedOn": sorted(ad["listedOn"]),
                "firstSeen": ad["firstSeen"],
                "sources": sorted(ad["sources"]),
                "selection": chosen[text],
                "text": text,
            }, ensure_ascii=False) + "\n")
    print(f"{len(ads)} distinct ads, {len(order)} selected -> {out}", file=sys.stderr)


if __name__ == "__main__":
    main()
