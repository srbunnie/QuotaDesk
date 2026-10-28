#!/usr/bin/env python3
"""Cut a character's two pose sheets into the 12 aligned sprites QuotaDesk draws.

Each sheet is a transparent 3x2 grid of the same character in the same chair, three-quarter view facing right:
  sheet 1: type-a, type-b, wait, coffee, sleep, chair (empty)
  sheet 2: blink, look, nosig-a, nosig-b, stretch, sip
Every sprite is scaled so the upright typing pose is --height pixels tall and placed on one shared canvas whose
anchor is the chair's left wheel on the floor, so the poses can swap without the character jumping.

    python skills/add-provider/scripts/cut_poses.py --id <id> --sheet <id>-sheet.png --sheet2 <id>-sheet2.png

Writes src/QuotaDesk/assets/providers/<id>/poses/*.png and prints the canvas size and the wheel anchor.
Requires: pillow, numpy, scipy.
"""
from pathlib import Path
import argparse
import json
import re

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[3]
PAGES = [["type-a", "type-b", "wait", "coffee", "sleep", "chair"], ["blink", "look", "nosig-a", "nosig-b", "stretch", "sip"]]


def cut(sheet_path, threshold, dilation):
    """The six sprites of one sheet, in reading order, each with the x of its left wheel."""
    sheet = Image.open(sheet_path).convert("RGBA")
    pixels = np.asarray(sheet)
    alpha = pixels[..., 3] > threshold
    labels, count = ndimage.label(ndimage.binary_dilation(alpha, iterations=dilation))
    if count < 6:
        raise SystemExit(f"{sheet_path}: found {count} separate sprites, expected 6 with transparent gaps between them")
    sizes = ndimage.sum(alpha, labels, range(1, count + 1))
    main = [int(i) + 1 for i in np.argsort(sizes)[-6:]]
    centers = {m: ndimage.center_of_mass(alpha, labels, m) for m in main}
    # Small detached bits (a phone, a "?") join the nearest sprite.
    for other in range(1, count + 1):
        if other in main or sizes[other - 1] < 30:
            continue
        cy, cx = ndimage.center_of_mass(alpha, labels, other)
        nearest = min(main, key=lambda m: (centers[m][0] - cy) ** 2 + (centers[m][1] - cx) ** 2)
        labels[labels == other] = nearest
    order = sorted(main, key=lambda m: (centers[m][0] > sheet.height / 2, centers[m][1]))
    cells = []
    for m in order:
        mask = (labels == m) & alpha
        ys, xs = np.where(mask)
        x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
        rgba = pixels.copy()
        rgba[labels != m] = 0
        cell = Image.fromarray(rgba, "RGBA").crop((x0, y0, x1, y1))
        mask = mask[y0:y1, x0:x1]
        band = mask[mask.shape[0] - int(mask.shape[0] * 0.14):]  # wheels and shoes
        cells.append((cell, int(np.where(band.any(axis=0))[0].min())))
    return cells


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--id", required=True)
    parser.add_argument("--sheet", required=True, type=Path)
    parser.add_argument("--sheet2", required=True, type=Path)
    parser.add_argument("--height", type=int, default=236, help="height of the upright typing pose in pixels (236)")
    parser.add_argument("--threshold", type=int, default=40, help="alpha threshold; raise to 80 for soft glows")
    parser.add_argument("--dilation", type=int, default=4, help="lower to 2 if neighbouring poses merge")
    parser.add_argument("--out", type=Path, help="default: src/QuotaDesk/assets/providers/<id>/poses")
    args = parser.parse_args()
    if not re.fullmatch(r"[a-z][a-z0-9-]*", args.id):
        raise SystemExit("--id must be lowercase letters, digits or '-' and start with a letter")
    out = args.out or ROOT / "src" / "QuotaDesk" / "assets" / "providers" / args.id / "poses"

    scaled = []
    for names, path in zip(PAGES, (args.sheet, args.sheet2)):
        cells = cut(path, args.threshold, args.dilation)
        k = args.height / cells[0][0].height  # the first cell of each sheet is the upright typing pose
        for name, (cell, ax) in zip(names, cells):
            size = (max(1, round(cell.width * k)), max(1, round(cell.height * k)))
            scaled.append((name, cell.resize(size, Image.LANCZOS), round(ax * k)))

    left = max(ax for _, _, ax in scaled)
    right = max(c.width - ax for _, c, ax in scaled)
    top = max(c.height for _, c, _ in scaled)
    out.mkdir(parents=True, exist_ok=True)
    for name, cell, ax in scaled:
        canvas = Image.new("RGBA", (left + right, top), (0, 0, 0, 0))
        canvas.alpha_composite(cell, (left - ax, top - cell.height))
        canvas.save(out / f"{name}.png", optimize=True)
    print(json.dumps({"id": args.id, "poses": len(scaled), "canvas": [left + right, top], "wheelAnchorX": left, "out": str(out)}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
