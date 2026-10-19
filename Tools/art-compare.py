"""Comparison of the arena with the target concept (A.3d, brief Docs/Briefs/A-EnvTarget.md section 4).

Usage:  python Tools/art-compare.py [step_dir] [--before other_step_dir]
        (default step_dir: Tools/out/art/A.3d; run after ArtCapture.CaptureAll)

Writes into step_dir:
  compare_env_close.png  ours (env_close.png; before | after with --before) next to the same share of concept 002
  compare_arena.png      arena.png next to concept 002 (and arena_fov50 / arena_fov30 if present: camera_ab.png)
  env_report.txt         env_stats.txt against the target values: CIE76 delta E and the brief's rules
Needs Python 3 + Pillow, no network. Screenshots are not committed (Tools/out is ignored).
"""
import colorsys
import math
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONCEPT = os.path.join(ROOT, "Art", "Source", "Flow", "2026-10-19_arena-target", "002.png")
# Concept 002 (896 x 1200): a piece with a 2 x 2 block, a bush mass, blocks and barrels, about 9 x 7 cells like env_close.
CONCEPT_CROP = (200, 320, 690, 720)
PANEL_HEIGHT = 640
LABEL_HEIGHT = 28

# name -> (target hex or None, delta E tolerance)
TARGETS = {
    "sand_open": ("#F9D28A", 8.0),
    "block_top": ("#FBE6B8", 8.0),
    "block_side": (("#D2AA74", "#E0BC86"), 12.0),   # A.3f (D-132): one stone material, lit cream-tan side (was wood #A84A14)
    "meadow": ("#5BB56B", 12.0),
}


def srgb_to_lab(hex_or_rgb):
    if isinstance(hex_or_rgb, str):
        h = hex_or_rgb.lstrip("#")
        rgb = tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    else:
        rgb = hex_or_rgb

    def lin(c):
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

    r, g, b = (lin(c) for c in rgb)
    x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047
    y = 0.2126 * r + 0.7152 * g + 0.0722 * b
    z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883

    def f(t):
        return t ** (1.0 / 3.0) if t > 216.0 / 24389.0 else (24389.0 / 27.0 * t + 16.0) / 116.0

    fx, fy, fz = f(x), f(y), f(z)
    return 116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz)


def delta_e(a, b):
    la, lb = srgb_to_lab(a), srgb_to_lab(b)
    return math.sqrt(sum((p - q) ** 2 for p, q in zip(la, lb)))


def read_stats(path):
    stats = {}
    if not os.path.exists(path):
        return stats
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            if line.startswith("#") or not line.strip():
                continue
            name, hx, lum, hue, sat, _ = line.split()
            stats[name] = {"hex": hx, "lum": float(lum), "hue": float(hue), "sat": float(sat)}
    return stats


def report(stats):
    lines = []
    ok_all = True

    def check(label, ok, detail):
        nonlocal ok_all
        ok_all &= ok
        lines.append(("OK   " if ok else "MISS ") + label + ": " + detail)

    for name, (target, tol) in TARGETS.items():
        if name not in stats:
            continue
        ours = stats[name]["hex"]
        targets = target if isinstance(target, tuple) else (target,)
        de = min(delta_e(ours, t) for t in targets)
        check(name, de <= tol, "%s vs %s  dE %.1f (<= %.0f)" % (ours, "/".join(targets), de, tol))
    if "sand_open" in stats and "sand_shadow" in stats:
        ratio = stats["sand_shadow"]["lum"] / max(stats["sand_open"]["lum"], 1e-3)
        hue = stats["sand_shadow"]["hue"]
        check("sand_shadow", 0.55 <= ratio <= 0.70 and 15 <= hue <= 35,
              "%s  %.2f of open sand (0.55-0.70), hue %.0f (15-35)  [A.3e]" % (stats["sand_shadow"]["hex"], ratio, hue))
    if "bush_tips" in stats:
        t = stats["bush_tips"]
        check("bush_tips", 150 <= t["hue"] <= 165 and t["sat"] >= 0.75,
              "%s  hue %.0f (150-165), sat %.2f (>= 0.75), concept #1CD48E" % (t["hex"], t["hue"], t["sat"]))
    if "bush_body" in stats and "sand_open" in stats:
        ratio = stats["bush_body"]["lum"] / max(stats["sand_open"]["lum"], 1e-3)
        check("bush_body", ratio <= 0.55, "%s  %.2f of open sand (<= 0.55)" % (stats["bush_body"]["hex"], ratio))
    if "shadow_edge" in stats:
        w = stats["shadow_edge"]["lum"]
        check("shadow_edge", w >= 0.25, "%.2f cells from 10%% to 90%% (>= 0.25)  [A.3e]" % w)
    if "water" in stats:
        lines.append("INFO water: %s (concept #69BCDF)" % stats["water"]["hex"])
    lines.append("ALL OK" if ok_all else "SOME MISSES")
    return lines


def labelled(img, text, height):
    w = int(img.width * height / img.height)
    panel = Image.new("RGB", (w, height + LABEL_HEIGHT), (30, 30, 30))
    panel.paste(img.convert("RGB").resize((w, height), Image.LANCZOS), (0, LABEL_HEIGHT))
    ImageDraw.Draw(panel).text((8, 7), text, fill=(255, 255, 255))
    return panel


def side_by_side(panels, path):
    w = sum(p.width for p in panels) + 8 * (len(panels) - 1)
    h = max(p.height for p in panels)
    out = Image.new("RGB", (w, h), (30, 30, 30))
    x = 0
    for p in panels:
        out.paste(p, (x, 0))
        x += p.width + 8
    out.save(path)
    print("wrote " + path)


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    before = None
    if "--before" in argv:
        before = argv[argv.index("--before") + 1]
        args = [a for a in args if a != before]
    step_dir = args[0] if args else os.path.join(ROOT, "Tools", "out", "art", "A.3d")
    concept = Image.open(CONCEPT)

    env = os.path.join(step_dir, "env_close.png")
    if os.path.exists(env):
        panels = []
        if before and os.path.exists(os.path.join(before, "env_close.png")):
            panels.append(labelled(Image.open(os.path.join(before, "env_close.png")), "before (" + os.path.basename(before) + ")", PANEL_HEIGHT))
        panels.append(labelled(Image.open(env), "ours (" + os.path.basename(step_dir) + ", gameplay camera)", PANEL_HEIGHT))
        panels.append(labelled(concept.crop(CONCEPT_CROP), "concept 002 (same share of cells)", PANEL_HEIGHT))
        side_by_side(panels, os.path.join(step_dir, "compare_env_close.png"))

    arena = os.path.join(step_dir, "arena.png")
    if os.path.exists(arena):
        side_by_side([labelled(Image.open(arena), "ours arena.png", 720), labelled(concept, "concept 002", 720)],
                     os.path.join(step_dir, "compare_arena.png"))
        lenses = [n for n in ("arena_fov50.png", "arena.png", "arena_fov30.png") if os.path.exists(os.path.join(step_dir, n))]
        if len(lenses) > 1:
            side_by_side([labelled(Image.open(os.path.join(step_dir, n)), n, 400) for n in lenses],
                         os.path.join(step_dir, "camera_ab.png"))

    lines = report(read_stats(os.path.join(step_dir, "env_stats.txt")))
    with open(os.path.join(step_dir, "env_report.txt"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")
    print("\n".join(lines))


if __name__ == "__main__":
    main(sys.argv[1:])
