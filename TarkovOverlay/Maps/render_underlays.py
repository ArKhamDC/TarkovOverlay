#!/usr/bin/env python3
"""Regenerate Maps/underlays.json and Maps/images/*.png.

Bounds and place-name positions come from the-hideout/tarkov-dev src/data/maps.json
(MIT). The PNGs are original drawings aligned to those frames. This script does
not read or copy the tarkov-dev SVG map art.

Usage:
  python3 render_underlays.py --maps /path/to/maps.json
  python3 render_underlays.py   # downloads maps.json from GitHub
"""

from __future__ import annotations

import argparse
import json
import math
import re
import urllib.request
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
MAPS_URL = "https://raw.githubusercontent.com/the-hideout/tarkov-dev/main/src/data/maps.json"
PRESET = re.compile(r"_?preset$", re.IGNORECASE)

# Base location plus raid variants that share its world coordinates.
VARIANTS: dict[str, list[tuple[str, str, str]]] = {
    "customs": [("bigmap", "customs", "maps/customs_preset.bundle")],
    "factory": [
        ("factory4_day", "factory", "maps/factory_day_preset.bundle"),
        ("factory4_night", "night-factory", "maps/factory_night_preset.bundle"),
        ("factory_event", "infected-factory", "maps/factory_event_preset.bundle"),
    ],
    "woods": [("Woods", "woods", "maps/woods_preset.bundle")],
    "lighthouse": [("Lighthouse", "lighthouse", "maps/lighthouse_preset.bundle")],
    "shoreline": [("Shoreline", "shoreline", "maps/shoreline_preset.bundle")],
    "reserve": [("RezervBase", "reserve", "maps/rezerv_base_preset.bundle")],
    "interchange": [("Interchange", "interchange", "maps/shopping_mall.bundle")],
    "streets-of-tarkov": [("TarkovStreets", "streets-of-tarkov", "maps/city_preset.bundle")],
    "ground-zero": [
        ("Sandbox", "ground-zero", "maps/sandbox_preset.bundle"),
        ("Sandbox_high", "ground-zero-21", "maps/sandbox_high_preset.bundle"),
        ("Sandbox_start", "ground-zero-tutorial", "maps/sandbox_start_preset.bundle"),
    ],
    "the-lab": [
        ("laboratory", "the-lab", "maps/laboratory_preset.bundle"),
        ("laboratory_dark", "the-lab-dark", "maps/laboratory_dark_preset.bundle"),
        ("laboratory_event", "infected-lab", "maps/laboratory_event_preset.bundle"),
    ],
    "the-labyrinth": [("Labyrinth", "the-labyrinth", "maps/labyrinth_preset.bundle")],
    "terminal": [("Terminal", "terminal", "maps/terminal_preset.bundle")],
}

# json.tarkov.dev coordinateToCardinalRotation for the base location.
# Player X/Z uses coordinateRotation from maps.json; this value only orients the compass tick.
CARDINAL = {
    "customs": 180,
    "factory": 90,
    "woods": 180,
    "lighthouse": 180,
    "shoreline": 180,
    "reserve": 195.209,
    "interchange": 180,
    "streets-of-tarkov": 180,
    "ground-zero": 180,
    "the-lab": 270,
    "the-labyrinth": 180,
    "terminal": 180,
}

# bg, blob, grid, major grid, building, building edge
PALETTES = {
    "customs": ((34, 40, 32), (46, 56, 42), (58, 70, 54), (86, 104, 78), (118, 104, 78), (214, 196, 150)),
    "factory": ((32, 30, 28), (48, 42, 38), (64, 56, 50), (96, 84, 72), (132, 108, 84), (220, 196, 160)),
    "woods": ((26, 40, 30), (34, 58, 38), (48, 74, 52), (72, 110, 74), (150, 132, 90), (220, 206, 160)),
    "lighthouse": ((28, 40, 42), (36, 58, 58), (52, 78, 76), (78, 116, 112), (168, 150, 114), (226, 214, 180)),
    "shoreline": ((48, 44, 34), (70, 62, 44), (86, 78, 56), (120, 108, 74), (150, 124, 90), (230, 214, 176)),
    "reserve": ((32, 36, 40), (44, 50, 56), (58, 66, 74), (90, 102, 112), (108, 112, 118), (196, 204, 208)),
    "interchange": ((36, 34, 32), (52, 48, 44), (70, 64, 58), (104, 96, 86), (124, 116, 104), (214, 206, 188)),
    "streets-of-tarkov": ((30, 32, 36), (42, 46, 52), (58, 64, 72), (88, 96, 108), (120, 114, 104), (210, 204, 190)),
    "ground-zero": ((40, 36, 32), (58, 50, 42), (76, 66, 54), (112, 98, 78), (140, 118, 90), (222, 204, 170)),
    "the-lab": ((24, 32, 40), (32, 48, 60), (46, 66, 82), (70, 102, 124), (90, 120, 140), (190, 214, 226)),
    "the-labyrinth": ((28, 26, 34), (42, 38, 52), (58, 52, 70), (90, 80, 110), (110, 96, 120), (206, 196, 220)),
    "terminal": ((26, 32, 38), (36, 46, 54), (50, 64, 74), (76, 98, 112), (100, 114, 124), (196, 210, 216)),
}

NOTES = {
    "reserve": "Frame rotation is the tarkov.dev interactive map (180). json.tarkov.dev coordinateToCardinalRotation for Reserve is 195.209, so compass north is about 15 degrees clockwise of image up.",
    "ground-zero": "East Gate, UN Roadblock, and the co-op exit lie outside this published frame. Spawns and the other extracts sit inside it.",
    "factory": "A few spawns sit a few meters south of the published frame.",
    "the-labyrinth": "Frame rotation is 270, from tarkov.dev maps.json. json.tarkov.dev coordinateToCardinalRotation for Labyrinth is 180.",
    "customs": "Orientation matches the in-game compass: Big Red to the east, dorms to the south.",
}


def norm(value: str) -> str:
    return "".join(ch.lower() for ch in value if ch.isalnum())


def aliases_for(name_id: str, normalized: str, scene: str) -> set[str]:
    found: set[str] = set()

    def add(raw: str | None) -> None:
        n = norm(raw or "")
        if n:
            found.add(n)

    add(name_id)
    add(normalized)
    file = scene.replace("\\", "/").rsplit("/", 1)[-1]
    if file.lower().endswith(".bundle"):
        file = file[: -len(".bundle")]
    add(file)
    add(PRESET.sub("", file))
    return found


def project(x: float, z: float, rotation: float) -> tuple[float, float]:
    rad = math.radians(rotation)
    c, s = math.cos(rad), math.sin(rad)
    return (x * c - z * s, x * s + z * c)


def frame_of(bounds: list, rotation: float) -> tuple[float, float, float, float]:
    (x0, z0), (x1, z1) = bounds
    pts = [project(x, z, rotation) for x in (x0, x1) for z in (z0, z1)]
    xs = [p[0] for p in pts]
    zs = [p[1] for p in pts]
    return min(xs), max(xs), min(zs), max(zs)


def grid_step(span: float) -> int:
    if span < 220:
        return 25
    if span < 700:
        return 50
    return 100


def interactive_maps(doc: list) -> dict[str, dict]:
    found = {}
    for entry in doc:
        name = entry.get("normalizedName")
        inter = next((m for m in entry.get("maps", []) if m.get("projection") == "interactive"), None)
        if name and inter and inter.get("bounds") and "coordinateRotation" in inter:
            found[name] = inter
    return found


def collect_areas(inter: dict) -> list[dict]:
    best: dict[str, dict] = {}
    for layer in inter.get("layers") or []:
        # Underground bunker volumes are much larger than the surface buildings and
        # would paint Reserve as a few slabs. Surface floors carry the landmarks.
        if str(layer.get("name") or "").lower() == "bunkers":
            continue
        for extent in layer.get("extents") or []:
            for raw in extent.get("bounds") or []:
                if not isinstance(raw, list) or len(raw) < 3 or not isinstance(raw[0], list):
                    continue
                name = str(raw[2]).strip()
                if not name or "switch" in name.lower():
                    continue
                (x0, z0), (x1, z1) = raw[0], raw[1]
                side = min(abs(x1 - x0), abs(z1 - z0))
                if side < 15:
                    continue
                area = abs(x1 - x0) * abs(z1 - z0)
                prev = best.get(name.lower())
                if prev is None or area > prev["area"]:
                    best[name.lower()] = {
                        "name": name,
                        "x0": x0,
                        "z0": z0,
                        "x1": x1,
                        "z1": z1,
                        "area": area,
                    }
    areas = list(best.values())
    for area in areas:
        del area["area"]
    areas.sort(key=lambda a: a["name"].lower())
    return areas


def collect_labels(inter: dict) -> list[dict]:
    labels = []
    for raw in inter.get("labels") or []:
        text = str(raw.get("text") or "").strip()
        pos = raw.get("position")
        if not text or not isinstance(pos, list) or len(pos) < 2:
            continue
        size = raw.get("size")
        try:
            priority = float(size) if size is not None else 140
        except (TypeError, ValueError):
            priority = 140
        labels.append({"x": pos[0], "z": pos[1], "text": text, "priority": priority})
    labels.sort(key=lambda item: (-item["priority"], item["text"], item["x"], item["z"]))
    kept: list[dict] = []
    for label in labels:
        if any(
            label["text"] == prev["text"] and math.hypot(label["x"] - prev["x"], label["z"] - prev["z"]) < 8
            for prev in kept
        ):
            continue
        kept.append(label)
    return kept


def render(spec: dict, path: Path) -> tuple[int, int]:
    rotation = spec["coordinateRotation"]
    (x0, z0), (x1, z1) = spec["publishedBounds"]
    min_rx, max_rx, min_rz, max_rz = frame_of(spec["publishedBounds"], rotation)
    span_x = max(max_rx - min_rx, 1)
    span_y = max(max_rz - min_rz, 1)
    long_side = 1400
    if span_x >= span_y:
        width = long_side
        height = max(480, round(long_side * span_y / span_x))
    else:
        height = long_side
        width = max(480, round(long_side * span_x / span_y))

    palette = PALETTES[spec["id"]]
    image = Image.new("RGB", (width, height), palette[0])
    draw = ImageDraw.Draw(image)

    def pix(x: float, z: float) -> tuple[float, float]:
        rx, rz = project(x, z, rotation)
        u = (rx - min_rx) / span_x
        v = (max_rz - rz) / span_y
        return (u * (width - 1), v * (height - 1))

    step = grid_step(max(abs(x1 - x0), abs(z1 - z0)))
    min_x, max_x = min(x0, x1), max(x0, x1)
    min_z, max_z = min(z0, z1), max(z0, z1)
    start_x = math.ceil(min_x / step) * step
    x = start_x
    while x <= max_x:
        major = abs(x) % (step * 2) < 1e-6
        draw.line([pix(x, min_z), pix(x, max_z)], fill=palette[3 if major else 2], width=2 if major else 1)
        x += step
    start_z = math.ceil(min_z / step) * step
    z = start_z
    while z <= max_z:
        major = abs(z) % (step * 2) < 1e-6
        draw.line([pix(min_x, z), pix(max_x, z)], fill=palette[3 if major else 2], width=2 if major else 1)
        z += step

    for area in spec["areas"]:
        ax0, az0, ax1, az1 = area["x0"], area["z0"], area["x1"], area["z1"]
        poly = [pix(ax0, az0), pix(ax1, az0), pix(ax1, az1), pix(ax0, az1)]
        shift = sum(ord(ch) for ch in area["name"]) % 24 - 12
        fill = tuple(max(0, min(255, c + shift)) for c in palette[4])
        draw.polygon(poly, fill=fill, outline=palette[5])

    for label in spec["labels"]:
        px, py = pix(label["x"], label["z"])
        r = 4
        draw.polygon([(px, py - r), (px + r, py), (px, py + r), (px - r, py)], fill=palette[5])

    draw.rectangle((1, 1, width - 2, height - 2), outline=palette[5], width=2)
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=True)
    return width, height


def build(doc: list) -> list[dict]:
    source = interactive_maps(doc)
    specs = []
    used: dict[str, str] = {}
    for map_id, variants in VARIANTS.items():
        inter = source.get(map_id)
        if inter is None:
            raise SystemExit(f"missing interactive map {map_id}")
        alias: set[str] = set()
        for name_id, normalized, scene in variants:
            alias |= aliases_for(name_id, normalized, scene)
        overlap = sorted(a for a in alias if a in used)
        if overlap:
            raise SystemExit(f"{map_id} aliases collide with {used[overlap[0]]}: {overlap}")
        for a in alias:
            used[a] = map_id
        bounds = inter["bounds"]
        spec = {
            "id": map_id,
            "image": f"{map_id}.png",
            "coordinateRotation": inter["coordinateRotation"],
            "cardinalRotation": CARDINAL[map_id],
            "publishedBounds": bounds,
            "bounds": {"x0": bounds[0][0], "z0": bounds[0][1], "x1": bounds[1][0], "z1": bounds[1][1]},
            "aliases": sorted(alias),
            "labels": collect_labels(inter),
            "areas": collect_areas(inter),
        }
        if map_id in NOTES:
            spec["note"] = NOTES[map_id]
        specs.append(spec)
    return specs


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--maps", type=Path, help="tarkov-dev src/data/maps.json")
    args = parser.parse_args()
    if args.maps:
        doc = json.loads(args.maps.read_text())
    else:
        req = urllib.request.Request(MAPS_URL, headers={"User-Agent": "TarkovOverlay/2.0"})
        with urllib.request.urlopen(req, timeout=120) as response:
            doc = json.load(response)

    specs = build(doc)
    payload = {
        "version": 1,
        "source": (
            "World bounds and coordinateRotation are the interactive frames in "
            "the-hideout/tarkov-dev src/data/maps.json (MIT License). Place-name positions are the labels "
            "in that file. cardinalRotation is json.tarkov.dev coordinateToCardinalRotation for the base "
            "location and is used only for the compass tick. Images are original drawings aligned to those "
            "frames; they are not the tarkov-dev SVG maps (those assets forbid overlay use). Frames were "
            "checked against spawn and extract coordinates from json.tarkov.dev. Icebreaker is omitted: its "
            "published bounds match Factory and do not contain Icebreaker spawns."
        ),
        "maps": specs,
    }
    out_json = ROOT / "underlays.json"
    out_json.write_text(json.dumps(payload, indent=2) + "\n")
    for spec in specs:
        width, height = render(spec, ROOT / "images" / spec["image"])
        print(f"{spec['id']:20} {width:4}x{height:<4} labels={len(spec['labels']):3} areas={len(spec['areas']):2}")


if __name__ == "__main__":
    main()
