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
import random
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

# terrain, water, road edge, road, path, building, building edge, tree
PALETTES = {
    "customs": ((58, 74, 50), (42, 86, 102), (48, 42, 34), (196, 176, 132), (154, 140, 108), (132, 118, 96), (236, 220, 184), (34, 50, 32)),
    "factory": ((46, 42, 38), (42, 86, 102), (28, 26, 24), (168, 156, 140), (130, 120, 108), (108, 96, 84), (220, 204, 176), (34, 50, 32)),
    "woods": ((42, 64, 40), (48, 96, 112), (52, 44, 32), (186, 164, 116), (146, 128, 92), (120, 108, 84), (230, 214, 176), (28, 46, 28)),
    "lighthouse": ((52, 70, 58), (38, 84, 104), (48, 42, 34), (190, 172, 128), (150, 136, 104), (140, 128, 104), (232, 218, 184), (30, 48, 36)),
    "shoreline": ((132, 124, 90), (40, 92, 118), (70, 60, 46), (188, 176, 140), (160, 148, 116), (168, 150, 118), (236, 222, 190), (70, 90, 52)),
    "reserve": ((58, 64, 66), (42, 86, 102), (28, 30, 32), (150, 150, 148), (110, 112, 112), (118, 122, 124), (214, 218, 216), (34, 50, 32)),
    "interchange": ((54, 52, 48), (42, 86, 102), (24, 24, 24), (176, 172, 160), (120, 116, 108), (142, 134, 120), (226, 218, 200), (34, 50, 32)),
    "streets-of-tarkov": ((62, 64, 66), (42, 86, 102), (32, 32, 34), (168, 164, 154), (120, 116, 110), (128, 120, 112), (220, 214, 200), (34, 50, 32)),
    "ground-zero": ((78, 72, 62), (42, 86, 102), (36, 32, 28), (176, 164, 140), (140, 128, 108), (150, 132, 108), (230, 214, 184), (34, 50, 32)),
    "the-lab": ((36, 48, 58), (42, 86, 102), (18, 24, 30), (140, 164, 176), (96, 116, 128), (78, 104, 120), (196, 216, 224), (34, 50, 32)),
    "the-labyrinth": ((40, 36, 48), (42, 86, 102), (24, 22, 30), (130, 120, 146), (96, 88, 110), (96, 86, 112), (210, 200, 220), (34, 50, 32)),
    "terminal": ((40, 48, 54), (42, 86, 102), (22, 26, 30), (150, 158, 162), (110, 118, 122), (100, 112, 120), (200, 210, 214), (34, 50, 32)),
}

# Main roads as world (x, z) polylines. Waypoints are the published landmark
# coordinates (or a few meters beside them), joined in the order those places
# are actually connected. Widths are meters and are drawn a little wide so the
# road is still visible in the small GPS window.
ROADS: dict[str, list[tuple[float, list[tuple[float, float]]]]] = {
    "customs": [
        (16, [(-250, -200), (-230, -150), (-190, -90), (-120, 20), (-20, 8), (70, -4), (180, -6), (320, 16), (430, 24), (580, 2)]),
        (12, [(-140, 48), (-20, 46), (50, 28)]),
        (12, [(-140, 8), (-140, 48)]),
        (12, [(170, 8), (150, 70), (190, 160)]),
        (11, [(60, -16), (70, -80), (120, -120), (210, -130), (331, -168)]),
        (11, [(210, -130), (90, -150), (70, -90)]),
        (12, [(560, 0), (540, -70), (470, -78), (340, -72), (250, -100), (200, -125)]),
        (10, [(-230, -150), (-280, -140), (-300, -180)]),
    ],
    "reserve": [
        (14, [(175, -215), (158, -155), (90, -125), (40, -90), (30, -30), (-60, -28), (-140, -12), (-125, 55), (-70, 100), (-15, 170)]),
        (11, [(40, -90), (66, -95), (60, -40), (50, 25), (55, 62)]),
        (10, [(-55, -20), (-50, 20), (-70, 70), (-110, 90)]),
        (10, [(150, -150), (130, -130), (70, -140), (0, -145)]),
    ],
    "woods": [
        (15, [(-240, 350), (-190, 220), (-80, 80), (10, -5), (0, -90), (-20, -300), (-8, -500), (-80, -670)]),
        (13, [(10, -5), (120, -40), (239, -65), (220, -250), (280, -470), (200, -600)]),
        (13, [(10, -5), (-180, -20), (-330, -40), (-510, -200), (-485, -385)]),
        (10, [(-190, 220), (-320, 40)]),
        (10, [(0, -90), (80, -150)]),
    ],
    "lighthouse": [
        (14, [(-115, 360), (-130, 110), (-100, 120), (-155, -230), (-75, -290), (-70, -610), (-45, -860)]),
        (11, [(-70, -620), (40, -618), (-90, -730), (-185, -665)]),
        (10, [(-80, -200), (5, -188), (125, -155)]),
        (10, [(-130, 110), (-70, 130)]),
    ],
    "shoreline": [
        (16, [(430, 400), (280, 430), (50, 450), (-190, 440), (-340, 500), (-500, 490), (-630, 475), (-520, 280)]),
        (13, [(-190, 430), (-210, 180), (-250, 40), (-250, -75)]),
        (12, [(-210, 180), (0, 150), (140, 130), (300, 140), (418, 118)]),
        (10, [(418, 118), (340, 0), (326, -110)]),
        (10, [(-510, 280), (-700, 100)]),
        (10, [(-250, -75), (-180, -85), (-140, -80)]),
    ],
    "interchange": [
        (18, [(520, -400), (370, -395), (80, -385), (-190, -350), (-380, -345)]),
        (16, [(155, 250), (-255, 250), (-255, -360), (160, -360), (160, 250)]),
        (11, [(155, -250), (200, -253)]),
        (11, [(-255, 150), (-300, 140)]),
    ],
    "ground-zero": [
        (14, [(85, -115), (95, 20), (115, 145), (105, 290)]),
        (12, [(-55, 8), (10, 35), (80, 28), (155, 8)]),
        (12, [(8, 190), (60, 220), (115, 210), (148, 150)]),
        (10, [(8, 200), (4, 330)]),
    ],
    "factory": [
        (5, [(-20, 50), (-4, 38), (18, 40), (40, 36), (58, 8), (67, -28)]),
        (4, [(-4, 38), (-1, 16), (16, 6), (30, -8), (41, -11)]),
        (4, [(-20, -28), (-4, -18), (16, -8)]),
        (4, [(20, 40), (24, 28), (34, 26)]),
    ],
    "the-lab": [
        (5, [(-262, -348), (-230, -345), (-172, -342), (-140, -370), (-130, -398)]),
        (4, [(-246, -282), (-220, -310), (-220, -345), (-220, -400)]),
        (4, [(-163, -270), (-170, -310), (-172, -342)]),
        (4, [(-250, -320), (-236, -302), (-207, -305)]),
    ],
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


def collect_floors(inter: dict) -> list[dict]:
    """Height bands from tarkov.dev map layers. A player Y inside one of these is on that floor."""
    floors = []
    for layer in inter.get("layers") or []:
        name = str(layer.get("name") or "").strip()
        if not name:
            continue
        for extent in layer.get("extents") or []:
            height = extent.get("height")
            if not isinstance(height, list) or len(height) < 2:
                continue
            min_y, max_y = float(height[0]), float(height[1])
            boxes = []
            for raw in extent.get("bounds") or []:
                if not isinstance(raw, list) or len(raw) < 2 or not isinstance(raw[0], list):
                    continue
                place = str(raw[2]).strip() if len(raw) > 2 else ""
                boxes.append({
                    "name": name,
                    "place": place,
                    "minY": min_y,
                    "maxY": max_y,
                    "x0": raw[0][0],
                    "z0": raw[0][1],
                    "x1": raw[1][0],
                    "z1": raw[1][1],
                })
            if boxes:
                floors.extend(boxes)
            else:
                floors.append({"name": name, "place": "", "minY": min_y, "maxY": max_y})
    return floors


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


def world_direction(map_rotation: float, label_rotation: float) -> tuple[float, float]:
    """Label rotation is the street angle in the projected plane, 0 along +projected X."""
    theta = math.radians(label_rotation)
    drx, drz = math.cos(theta), math.sin(theta)
    c, s = math.cos(math.radians(map_rotation)), math.sin(math.radians(map_rotation))
    return (drx * c + drz * s, -drx * s + drz * c)


def street_roads(inter: dict, map_rotation: float) -> list[tuple[float, list[tuple[float, float]]]]:
    grouped: dict[str, list[dict]] = {}
    for raw in inter.get("labels") or []:
        text = str(raw.get("text") or "")
        if not any(token in text for token in ("St.", "Ave", "Alley")):
            continue
        pos = raw.get("position")
        if not isinstance(pos, list) or len(pos) < 2:
            continue
        try:
            rotation = float(raw.get("rotation") or 0)
        except (TypeError, ValueError):
            rotation = 0
        grouped.setdefault(text, []).append({"x": pos[0], "z": pos[1], "rotation": rotation})

    roads = []
    for marks in grouped.values():
        dx, dz = world_direction(map_rotation, marks[0]["rotation"])
        marks.sort(key=lambda m: m["x"] * dx + m["z"] * dz)
        reach = 220 if len(marks) > 1 else 140
        start = (marks[0]["x"] - dx * reach, marks[0]["z"] - dz * reach)
        end = (marks[-1]["x"] + dx * reach, marks[-1]["z"] + dz * reach)
        middle = [(m["x"], m["z"]) for m in marks]
        roads.append((18, [start, *middle, end]))
    return roads


def water_shapes(map_id: str, bounds: list) -> tuple[list[list[tuple[float, float]]], list[tuple[float, float, float]]]:
    (x0, z0), (x1, z1) = bounds
    min_x, max_x = min(x0, x1), max(x0, x1)
    min_z, max_z = min(z0, z1), max(z0, z1)
    polys: list[list[tuple[float, float]]] = []
    ponds: list[tuple[float, float, float]] = []
    if map_id == "customs":
        polys.append([(-96, -110), (-46, -110), (-46, 200), (-96, 200)])
    elif map_id == "shoreline":
        polys.append([(min_x, 545), (max_x, 545), (max_x, max_z), (min_x, max_z)])
        ponds.append((326, -118, 70))
    elif map_id == "woods":
        ponds.append((-5, -515, 78))
        ponds.append((40, -470, 28))
    elif map_id == "lighthouse":
        polys.append([(210, min_z), (max_x, min_z), (max_x, max_z), (210, max_z)])
        ponds.append((-177, -356, 36))
    return polys, ponds


def inside_area(x: float, z: float, areas: list[dict]) -> bool:
    for area in areas:
        x0, x1 = sorted((area["x0"], area["x1"]))
        z0, z1 = sorted((area["z0"], area["z1"]))
        if x0 - 10 <= x <= x1 + 10 and z0 - 10 <= z <= z1 + 10:
            return True
    return False


def render(spec: dict, path: Path) -> tuple[int, int]:
    rotation = spec["coordinateRotation"]
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
    ppm = (width - 1) / span_x

    def pix(x: float, z: float) -> tuple[float, float]:
        rx, rz = project(x, z, rotation)
        return ((rx - min_rx) / span_x * (width - 1), (max_rz - rz) / span_y * (height - 1))

    if spec["id"] == "woods":
        rng = random.Random(spec["id"])
        for _ in range(420):
            wx = rng.uniform(min(spec["bounds"]["x0"], spec["bounds"]["x1"]), max(spec["bounds"]["x0"], spec["bounds"]["x1"]))
            wz = rng.uniform(min(spec["bounds"]["z0"], spec["bounds"]["z1"]), max(spec["bounds"]["z0"], spec["bounds"]["z1"]))
            px, py = pix(wx, wz)
            rad = rng.uniform(1.5, 3.5)
            draw.ellipse((px - rad, py - rad, px + rad, py + rad), fill=palette[7])

    for poly in spec.get("water") or []:
        draw.polygon([pix(x, z) for x, z in poly], fill=palette[1])
    for cx, cz, radius in spec.get("ponds") or []:
        px, py = pix(cx, cz)
        rad = radius * ppm
        draw.ellipse((px - rad, py - rad, px + rad, py + rad), fill=palette[1])
    if spec["id"] == "lighthouse":
        px, py = pix(382, 496)
        rad = 48 * ppm
        draw.ellipse((px - rad, py - rad * 0.7, px + rad, py + rad * 0.7), fill=palette[0])

    def paint(points: list[tuple[float, float]], width_m: float, fill: tuple[int, int, int], edge: tuple[int, int, int] | None) -> None:
        seq = [pix(x, z) for x, z in points]
        if len(seq) < 2:
            return
        if edge is not None:
            draw.line(seq, fill=edge, width=max(2, round(width_m * ppm * 1.45)), joint="curve")
        draw.line(seq, fill=fill, width=max(1, round(width_m * ppm)), joint="curve")

    for width_m, points in spec.get("roads") or []:
        paint(points, width_m, palette[3], palette[2])

    def lighten(color: tuple[int, int, int], amount: int) -> tuple[int, int, int]:
        return tuple(min(255, channel + amount) for channel in color)

    def draw_block(x0: float, z0: float, x1: float, z1: float, fill: tuple[int, int, int]) -> None:
        xs = sorted((x0, x1))
        zs = sorted((z0, z1))
        ring = [pix(xs[0], zs[0]), pix(xs[1], zs[0]), pix(xs[1], zs[1]), pix(xs[0], zs[1])]
        draw.polygon(ring, fill=fill)
        span_a, span_b = xs[1] - xs[0], zs[1] - zs[0]
        if span_a > 8 and span_b > 8:
            inset_x, inset_z = span_a * 0.16, span_b * 0.16
            roof = [
                pix(xs[0] + inset_x, zs[0] + inset_z),
                pix(xs[1] - inset_x, zs[0] + inset_z),
                pix(xs[1] - inset_x, zs[1] - inset_z),
                pix(xs[0] + inset_x, zs[1] - inset_z),
            ]
            draw.polygon(roof, fill=lighten(fill, 26))
            # A spine along the long side so a large footprint reads as a building, not a flat slab.
            if max(span_a, span_b) > 36:
                if span_a >= span_b:
                    mid = (zs[0] + zs[1]) / 2
                    half = span_b * 0.1
                    corridor = [pix(xs[0] + inset_x, mid - half), pix(xs[1] - inset_x, mid - half), pix(xs[1] - inset_x, mid + half), pix(xs[0] + inset_x, mid + half)]
                else:
                    mid = (xs[0] + xs[1]) / 2
                    half = span_a * 0.1
                    corridor = [pix(mid - half, zs[0] + inset_z), pix(mid + half, zs[0] + inset_z), pix(mid + half, zs[1] - inset_z), pix(mid - half, zs[1] - inset_z)]
                draw.polygon(corridor, fill=lighten(fill, 8))
        draw.line(ring + [ring[0]], fill=palette[6], width=max(1, round(ppm * 1.4)))

    # The published dorms footprint is one yard around both buildings.
    # On this compass frame the three-story block is the eastern one (lower X)
    # and the two-story block is the western one, with the courtyard between them.
    if spec["id"] == "customs":
        draw_block(170, 128, 193, 186, palette[5])
        draw_block(216, 138, 237, 180, palette[5])
        try:
            from PIL import ImageFont
            mark = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 16)
            for mx, mz, glyph in ((181.5, 157, "3"), (226.5, 159, "2")):
                px, py = pix(mx, mz)
                draw.text((px - 5, py - 8), glyph, fill=(48, 38, 28), font=mark)
        except OSError:
            pass

    for area in spec["areas"]:
        if str(area["name"]).lower() == "dorms":
            continue
        if str(area["name"]).lower() == "mall":
            plate = lighten(palette[0], 18)
            ring = [
                pix(area["x0"], area["z0"]),
                pix(area["x1"], area["z0"]),
                pix(area["x1"], area["z1"]),
                pix(area["x0"], area["z1"]),
            ]
            draw.polygon(ring, fill=plate)
            draw.line(ring + [ring[0]], fill=palette[6], width=max(2, round(ppm * 2)))
            continue
        draw_block(area["x0"], area["z0"], area["x1"], area["z1"], palette[5])

    def footprint(name: str) -> tuple[float, float]:
        lowered = name.lower()
        if spec["id"] in {"factory", "the-lab"}:
            return 12, 9
        if spec["id"] == "interchange":
            return 16, 12
        if any(word in lowered for word in ("dorm", "sawmill", "resort", "plant", "chalet", "depot", "warehouse", "bunker", "hotel", "construction", "knight", "bishop", "pawn", "king")):
            return 34, 24
        if any(word in lowered for word in ("shack", "tent", "tree", "rock", "nest")):
            return 12, 10
        return 20, 15

    blocking = [area for area in spec["areas"] if str(area["name"]).lower() != "mall"]
    for label in spec["labels"]:
        if any(token in label["text"] for token in ("St.", "Ave", "Alley")):
            continue
        if inside_area(label["x"], label["z"], blocking):
            continue
        half_x, half_z = footprint(label["text"])
        draw_block(label["x"] - half_x, label["z"] - half_z, label["x"] + half_x, label["z"] + half_z, palette[5])

    draw.rectangle((1, 1, width - 2, height - 2), outline=palette[6], width=2)
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=True)
    return width, height


def customs_dorm_labels(labels: list[dict]) -> list[dict]:
    kept = [label for label in labels if label["text"] != "Dorms"]
    kept.append({"x": 181.5, "z": 157.0, "text": "3-story dorms", "priority": 160})
    kept.append({"x": 226.5, "z": 159.0, "text": "2-story dorms", "priority": 160})
    return kept


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
            "labels": customs_dorm_labels(collect_labels(inter)) if map_id == "customs" else collect_labels(inter),
            "areas": collect_areas(inter),
            "floors": collect_floors(inter),
            "roads": [*ROADS.get(map_id, []), *street_roads(inter, inter["coordinateRotation"])],
            "water": water_shapes(map_id, bounds)[0],
            "ponds": water_shapes(map_id, bounds)[1],
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
            "location and is used only for the compass tick. "
            "The tarkov.dev SVG maps (and the raster tiles made from them) are CC BY-NC-SA 4.0 and their "
            "authors forbid using them in an in-raid overlay or radar, so they are not shipped. MapGenie "
            "tiles and the game's own map textures are not licensed for redistribution. "
            "The images here are original drawings on those same frames: roads join the published landmark "
            "coordinates in the order those places connect, with water and building footprints. "
            "Frames were checked against spawn and extract coordinates from json.tarkov.dev. "
            "Icebreaker is omitted: its published bounds match Factory and do not contain Icebreaker spawns. "
            "Terminal and Labyrinth have no published road or landmark geometry, so they stay a plain frame."
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
