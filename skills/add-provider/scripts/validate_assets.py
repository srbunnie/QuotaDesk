#!/usr/bin/env python3
"""Check that the Loft plate, its manifest, the provider poses and the code all agree.

    python skills/add-provider/scripts/validate_assets.py

Checks: manifest v2; plate proportional to the world and RGBA; every window and screen transparent and inside its
room; rooms laid end to end; 12 transparent poses per provider; and the room order in the manifest equal to
SharedOfficeScene.Order and OfficeViewport.PhysicalOrder, with a ProviderModuleCatalog entry for every room.
Exit code 1 lists every problem. Requires: pillow.
"""
from pathlib import Path
import json
import re
import sys

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
SRC = ROOT / "src" / "QuotaDesk"
ASSETS = SRC / "assets"
POSES = ("type-a", "type-b", "blink", "look", "wait", "coffee", "sip", "sleep", "stretch", "nosig-a", "nosig-b", "chair")


def code_list(file, pattern):
    match = re.search(pattern, (SRC / file).read_text(encoding="utf-8"), re.S)
    return re.findall(r'"([a-z0-9-]+)"', match.group(1)) if match else None


def main() -> int:
    errors = []
    manifest = json.loads((ASSETS / "office" / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("version") != 2:
        errors.append("manifest must be version 2")
    width, height = manifest.get("width", 0), manifest.get("height", 0)
    providers = manifest.get("providers", [])
    ids = [p.get("id") for p in providers]

    plate_path = ASSETS / "office" / manifest.get("asset", "voxel-loft.png")
    with Image.open(plate_path) as plate:
        k = plate.width / width
        if abs(plate.height / height - k) > 0.01:
            errors.append(f"plate {plate.size} is not proportional to the {width}x{height} world")
        if plate.mode != "RGBA":
            errors.append("plate must be RGBA: window panes and monitor glass are transparent")
        else:
            alpha = plate.getchannel("A")
            regions = [("window", w) for w in manifest.get("windows", [])]
            regions += [(p["id"] + " screen", p["screen"]) for p in providers if p.get("screen")]
            for name, (x, y, w, h) in regions:
                if not (0 <= x and x + w <= width and 0 <= y and y + h <= height):
                    errors.append(f"{name} lies outside the world")
                elif alpha.getpixel((int((x + w / 4) * k), int((y + h / 4) * k))) != 0:
                    errors.append(f"{name} is not transparent in the plate")

    edge = 0
    for p in providers:
        left, room = p.get("room", [-1, 0])
        if left != edge or room <= 0:
            errors.append(f"{p.get('id')} room must start where the previous one ends ({edge})")
        if not (left < p.get("seat", -1) < left + room):
            errors.append(f"{p.get('id')} seat lies outside its room")
        if p.get("screen") and not (left <= p["screen"][0] and p["screen"][0] + p["screen"][2] <= left + room):
            errors.append(f"{p.get('id')} screen lies outside its room")
        if not any(left <= w[0] + w[2] / 2 < left + room for w in manifest.get("windows", [])):
            errors.append(f"{p.get('id')} room has no window")
        edge = left + room
    if edge != width:
        errors.append(f"rooms cover {edge} of the {width}-unit world")

    for pid in ids:
        for pose in POSES:
            path = ASSETS / "providers" / pid / "poses" / f"{pose}.png"
            if not path.is_file():
                errors.append(f"missing pose: {pid}/{pose}.png")
                continue
            with Image.open(path) as image:
                if image.mode != "RGBA" or image.getchannel("A").getextrema()[0] != 0:
                    errors.append(f"{pid}/{pose}.png must be RGBA with a transparent background")
                if image.width < 80 or image.height < 100:
                    errors.append(f"{pid}/{pose}.png unexpectedly small: {image.size}")

    scene = code_list("SharedOfficeScene.cs", r"string\[\]\s+Order\s*=\s*\[(.*?)\]")
    viewport = code_list("OfficeViewport.cs", r"PhysicalOrder\s*\{[^}]*\}\s*=\s*\[(.*?)\]")
    catalog = re.findall(r'Module\("([a-z0-9-]+)"', (SRC / "ProviderModule.cs").read_text(encoding="utf-8"))
    if scene != ids:
        errors.append(f"SharedOfficeScene.Order {scene} must equal the manifest order {ids}")
    if viewport != ids:
        errors.append(f"OfficeViewport.PhysicalOrder {viewport} must equal the manifest order {ids}")
    for pid in ids:
        if pid not in catalog:
            errors.append(f"'{pid}' has a room but no Module(\"{pid}\", ...) in ProviderModuleCatalog")

    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print(f"OK: {len(ids)} rooms ({', '.join(ids)}), {len(ids) * len(POSES)} poses, code order matches the manifest.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
