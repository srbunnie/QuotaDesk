#!/usr/bin/env python3
"""Add (or replace) one provider room in the QuotaDesk Loft plate and manifest.

The approved room is a 1452x896 image whose window panes are flat #00FF00 and whose monitor glass is flat #FF00FF
(omit the monitor for tools without a screen). This script keys both to transparency, neutralises the green and pink
they cast on frames, walls and floor, halves the room to 726x448 and writes it into
src/QuotaDesk/assets/office/voxel-loft.png: appended at the right end for a new id, or in place for an existing one.
The manifest gets the room span, the window and screen measured from the keyed glass, and the seat.

    python skills/add-provider/scripts/add_room.py --id <id> --room approved/<id>.png --seat 182 [--keep-ceiling]

The plate is the only copy of the other rooms in the repository, so it is backed up next to itself first
(voxel-loft.png.bak, manifest.json.bak). A preview with a night sky and code screens goes to --preview.
Requires: pillow, numpy, scipy.
"""
from pathlib import Path
import argparse
import json
import re
import shutil

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[3]
OFFICE = ROOT / "src" / "QuotaDesk" / "assets" / "office"
ROOM = (1452, 896)           # approved room, four pixels per world unit
HALF = (ROOM[0] // 2, ROOM[1] // 2)
FLOOR = 0.825                # rows below this fraction of the height are floor
CEILING = 0.07               # rows above this are the ceiling band, where the windows cast a glow
EDGE = 0.05                  # columns this close to either side are the pilasters


def components(mask, min_px):
    labels, _ = ndimage.label(mask)
    return [box for index, box in enumerate(ndimage.find_objects(labels), start=1)
            if box is not None and (labels[box] == index).sum() >= min_px]


def clean(path, keep_ceiling):
    """Returns the keyed RGBA room and the mask of its monitor glass, both at ROOM size."""
    source = Image.open(path).convert("RGB")
    if source.size != ROOM:
        raise SystemExit(f"{path}: the room must be {ROOM[0]}x{ROOM[1]}, got {source.size[0]}x{source.size[1]}")
    image = np.asarray(source).astype(float)
    h, w, _ = image.shape
    r, g, b = image[..., 0], image[..., 1], image[..., 2]
    green = (g > 140) & (g - np.maximum(r, b) > 60)
    magenta = (r > 150) & (b > 150) & (np.minimum(r, b) - g > 70)

    keyed = np.zeros((h, w), bool)
    screen = np.zeros((h, w), bool)
    near = np.zeros((h, w), bool)
    for box, is_screen in [(b_, False) for b_ in components(green, 4000)] + [(b_, True) for b_ in components(magenta, 8000)]:
        y, x = box
        if y.start > h * FLOOR * 0.9:
            continue  # a bright reflection on the floor, not glass
        region = (slice(max(y.start - 4, 0), y.stop + 4), slice(max(x.start - 4, 0), x.stop + 4))
        local = green[region] | magenta[region]
        filled = ndimage.binary_dilation(ndimage.binary_fill_holes(local), iterations=2)
        keyed[region] |= filled
        if is_screen:
            screen[region] |= filled
        near[max(y.start - 18, 0):y.stop + 18, max(x.start - 18, 0):x.stop + 18] = True
    if not (keyed & ~screen).any():
        raise SystemExit(f"{path}: no #00FF00 window pane found; the room needs a window keyed in flat green")

    out = image.copy()
    r, g, b = out[..., 0], out[..., 1], out[..., 2]
    rows = np.arange(h, dtype=float)[:, None]
    weight = np.clip((rows - h * FLOOR) / 30, 0, 1) * np.ones((1, w))
    weight = np.maximum(weight, near * 1.0)
    if not keep_ceiling:
        weight[: int(h * CEILING)] = 1
    cols = np.arange(w)
    weight[:, (cols < w * EDGE) | (cols > w * (1 - EDGE))] = 1
    weight = np.maximum(weight, 0.35)
    weight = ndimage.gaussian_filter(weight, 6) * ~keyed

    lime = np.clip(g - np.maximum(r, b), 0, None) * weight
    pink = np.clip(np.minimum(r, b) - g - 25, 0, None) * weight
    out[..., 1] = g - lime * 0.9
    out[..., 0] = r - pink * 0.6
    out[..., 2] = b - pink * 0.5
    # Floor reflections go back to the wood's warm hue at the same brightness; violet props and LEDs keep theirs.
    floor = np.clip((rows - h * FLOOR) / 30, 0, 1) * ~keyed
    r, g, b = out[..., 0], out[..., 1], out[..., 2]
    cast = np.clip((g / (r + 1) - 0.8) / 0.12, 0, 1) + np.clip((b - g) / 18, 0, 1) * (r >= b)
    cast *= np.clip((200 - out.max(axis=2)) / 50, 0, 1)
    luma = out.mean(axis=2, keepdims=True)
    wood = np.clip(luma * np.array([1.3, 0.95, 0.68]), 0, 255)
    k = ndimage.gaussian_filter(floor * np.clip(cast, 0, 1), 2)[..., None] * 0.95
    out = out * (1 - k) + wood * k

    alpha = np.where(keyed, 0, 255).astype(np.uint8)
    return np.dstack([np.clip(out, 0, 255).astype(np.uint8), alpha]), screen


def bbox(mask):
    rows, cols = np.nonzero(mask)
    return (cols.min(), rows.min(), cols.max() + 1, rows.max() + 1) if len(rows) else None


def preview(plate, screen):
    """Night sky behind the glass and code on the screens, only for reviewing the result."""
    rgba = plate.astype(float)
    h, w, _ = rgba.shape
    y = np.linspace(0, 1, h)[:, None, None]
    sky = np.repeat(np.array([8, 14, 40]) + np.array([32, 48, 80]) * np.clip(y * 2.2, 0, 1), w, axis=1)
    code = np.zeros_like(sky)
    code[:] = (14, 22, 34)
    code[(np.arange(h) // 3) % 2 == 0] = (110, 200, 255)
    sky[screen] = code[screen]
    a = rgba[..., 3:] / 255
    return (rgba[..., :3] * a + sky * (1 - a)).clip(0, 255).astype(np.uint8)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--id", required=True, help="provider id, lowercase (same as IAgentSource.Id)")
    parser.add_argument("--room", required=True, type=Path, help="approved 1452x896 room with keyed glass")
    parser.add_argument("--seat", type=float, default=182, help="seat x in the room's own units, 0-363 (default 182)")
    parser.add_argument("--keep-ceiling", action="store_true", help="keep green/pink decoration in the ceiling band")
    parser.add_argument("--office", type=Path, default=OFFICE, help="folder with voxel-loft.png and manifest.json")
    parser.add_argument("--preview", type=Path, default=Path("loft-preview.png"))
    args = parser.parse_args()
    if not re.fullmatch(r"[a-z][a-z0-9-]*", args.id):
        raise SystemExit("--id must be lowercase letters, digits or '-' and start with a letter")

    manifest_path = args.office / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    plate_path = args.office / manifest["asset"]
    plate = Image.open(plate_path).convert("RGBA")
    providers = manifest["providers"]
    if not providers:
        raise SystemExit("the manifest has no rooms to take the layout from")
    reference = providers[0]
    room_units = reference["room"][1]
    unit = ROOM[0] / room_units                       # approved-room pixels per world unit (4)
    scale = plate.width / manifest["width"]           # plate pixels per world unit (2)
    head_dx = reference["head"][0] - reference["seat"]
    if not 0 < args.seat < room_units:
        raise SystemExit(f"--seat must lie inside the room (0-{room_units})")

    room, screen = clean(args.room, args.keep_ceiling)
    window_box, screen_box = bbox((room[..., 3] == 0) & ~screen), bbox(screen)
    half_room = Image.fromarray(room, "RGBA").resize(HALF, Image.LANCZOS)

    existing = next((i for i, p in enumerate(providers) if p["id"] == args.id), None)
    left = providers[existing]["room"][0] if existing is not None else manifest["width"]

    def world(box):
        x0, y0, x1, y1 = box
        return [round(left + x0 / unit, 1), round(y0 / unit, 1), round((x1 - x0) / unit, 1), round((y1 - y0) / unit, 1)]

    shutil.copy2(plate_path, plate_path.with_suffix(plate_path.suffix + ".bak"))
    shutil.copy2(manifest_path, manifest_path.with_suffix(".json.bak"))

    if existing is None:
        grown = Image.new("RGBA", (plate.width + HALF[0], plate.height))
        grown.paste(plate, (0, 0))
        plate = grown
        manifest["width"] += room_units
    else:
        # Drop the old room's windows before measuring the new ones.
        manifest["windows"] = [w for w in manifest["windows"] if not (left <= w[0] + w[2] / 2 < left + room_units)]
    plate.paste(half_room, (round(left * scale), 0))

    seat = round(left + args.seat, 1)
    entry = {
        "id": args.id,
        "seat": seat,
        "head": [round(seat + head_dx, 1), reference["head"][1]],
        "spriteScale": reference["spriteScale"],
        "screen": world(screen_box) if screen_box else None,
        "room": [left, room_units],
    }
    if existing is None:
        providers.append(entry)
    else:
        providers[existing] = entry
    manifest["windows"].append(world(window_box))
    manifest["windows"].sort(key=lambda w: w[0])

    plate.save(plate_path, optimize=True)
    manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    full_screen = np.zeros((plate.height, plate.width), bool)
    small_screen = np.asarray(Image.fromarray(screen).resize(HALF, Image.NEAREST))
    x = round(left * scale)
    full_screen[:, x:x + HALF[0]] = small_screen
    Image.fromarray(preview(np.asarray(plate), full_screen)).save(args.preview)

    order = [p["id"] for p in providers]
    print(f"{'Replaced' if existing is not None else 'Added'} room '{args.id}' at x={left} (seat {seat}).")
    print(f"Plate {plate.width}x{plate.height} -> {plate_path}; preview -> {args.preview}")
    print("Room order in the manifest (copy it into SharedOfficeScene.Order and OfficeViewport.PhysicalOrder):")
    print("  [" + ", ".join(f'"{i}"' for i in order) + "]")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
