#!/usr/bin/env python3
"""Render the labelled PF corpus as Markdown for a person to check, with game glyphs made readable.

Usage: python3 -I render_review.py <pf-listings.jsonl> <out.md> [--sample N] [--seed S]

Boxed letters and numbers (SeIconChar BoxedLetter*/Number*/BoxedNumber*) become plain text inside
⟦ ⟧, so "⟦CLUB⟧ ⟦NOIR⟧" shows the name was written in boxed letters. Other game glyphs become
⟨Name⟩ using Dalamud's SeIconChar names; unknown ones show as ⟨U+E0xx⟩.
"""
import json
import random
import sys

GLYPHS = {  # Dalamud SeIconChar, the ones that matter for reading venue ads; "?" = not in SeIconChar
    0xE031: "Clock", 0xE032: "V-chevron?", 0xE033: "ItemLevel", 0xE034: "Sprout", 0xE035: "ArrowDown", 0xE03A: "EurekaLevel",
    0xE03C: "HQ", 0xE03E: "Dice", 0xE048: "Hyadelyn", 0xE049: "Gil", 0xE04A: "Circle", 0xE04B: "Square",
    0xE04C: "Cross", 0xE04D: "Triangle", 0xE04E: "GlamouredDyed (+)", 0xE05B: "Debuff", 0xE05C: "Buff",
    0xE05D: "CrossWorld", 0xE05A: "Dots?", 0xE05F: "BoxedX?", 0xE06A: "Lv", 0xE06B: "St (LevelDe)", 0xE06C: "Nv (LevelFr)", 0xE06D: "AM",
    0xE06E: "PM", 0xE06F: "ArrowRight", 0xE0AF: "BoxedPlus", 0xE0C0: "BoxedStar",
    0xE0D0: "LT", 0xE0D1: "ST", 0xE0D2: "ET (Eorzea)",
}


def boxed(o):
    """The text a boxed glyph stands for, or None."""
    if 0xE071 <= o <= 0xE08A:  # BoxedLetterA..Z
        return chr(ord("A") + o - 0xE071)
    if 0xE060 <= o <= 0xE069:  # Number0..9
        return str(o - 0xE060)
    if 0xE08F <= o <= 0xE0AE:  # BoxedNumber0..31
        return str(o - 0xE08F)
    if 0xE0E0 <= o <= 0xE0E9:  # BoxedOutlinedNumber0..9
        return str(o - 0xE0E0)
    if o == 0xE0B0:  # a boxed E, not in SeIconChar
        return "E"
    return None


def readable(text):
    out, in_box = [], False
    for ch in text:
        o = ord(ch)
        b = boxed(o)
        if b is not None:
            # Boxed numbers 10-31 are single glyphs; keep them apart from a neighbouring digit.
            if in_box and len(b) > 1:
                out.append("|")
            if not in_box:
                out.append("⟦")
                in_box = True
            out.append(b)
            continue
        if in_box:
            out.append("⟧")
            in_box = False
        if 0xE000 <= o < 0xF900:
            out.append(f"⟨{GLYPHS.get(o, f'U+{o:04X}')}⟩")
        else:
            out.append(ch)
    if in_box:
        out.append("⟧")
    return "".join(out).replace("\n", " ⏎ ")


def address(a):
    parts = [a["world"] or "(listing's world)", a["district"] or "(no district)"]
    if a["ward"] is not None:
        parts.append(f"W{a['ward']}")
    if a["plot"] is not None:
        parts.append(f"P{a['plot']}")
    if a["subdivision"] is not None:
        parts.append("subdivision" if a["subdivision"] else "main division")
    if a["apartment"] is not None:
        parts.append(f"apartment {a['apartment']}")
    if a["room"] is not None:
        parts.append(f"room {a['room']}")
    return ", ".join(parts)


def when(t):
    if t["now"] and not (t["start"] or t["end"]):
        return "open now"
    if t["always"]:
        return "24/7"
    bits = ["open now," if t["now"] else "", t["day"] or "", t["start"] or "?", "–", t["end"] or "?",
            t["zone"] or "(no zone)"]
    return " ".join(b for b in bits if b).replace(" – ", "–")


def main():
    src, out = sys.argv[1], sys.argv[2]
    args = sys.argv[3:]
    rows = [json.loads(line) for line in open(src, encoding="utf-8") if line.strip()]
    if "--sample" in args:
        n = int(args[args.index("--sample") + 1])
        seed = int(args[args.index("--seed") + 1]) if "--seed" in args else None
        rows = sorted(random.Random(seed).sample(rows, min(n, len(rows))), key=lambda r: r["id"])
    with open(out, "w", encoding="utf-8") as f:
        f.write(f"# PF corpus review ({len(rows)} entries)\n\n"
                "⟦ ⟧ = boxed letters or numbers; ⟨ ⟩ = other game glyphs; ⏎ = line break in the ad.\n\n")
        for r in rows:
            lab = r["labels"]
            f.write(f"## {r['id']} · {r['region']} · {lab['kind']}\n\n")
            f.write(f"> {readable(r['text'])}\n\n")
            f.write(f"- listed on: {', '.join(r['listedOn'])}\n")
            name = lab["name"]
            f.write(f"- name: {name['raw'] + '  (normalised: ' + name['normalised'] + ')' if name else '—'}\n")
            for a in lab["addresses"] or [None]:
                f.write(f"- address: {address(a) if a else '—'}\n")
            for t in lab["times"] or [None]:
                f.write(f"- time: {when(t) if t else '—'}\n")
            f.write(f"- urls: {', '.join(lab['urls']) or '—'}\n")
            if lab["notes"]:
                f.write(f"- notes: {lab['notes']}\n")
            f.write("\n")
    print(f"{len(rows)} entries -> {out}", file=sys.stderr)


if __name__ == "__main__":
    main()
