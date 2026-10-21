"""Render deterministic documentation GIFs. Requires Pillow; never loads game DLLs."""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "docs" / "ownership-examples"
SIZE = (1080, 1268)
CELL, LEFT, TOP = 20, 40, 170
COLS, ROWS = 50, 50
BG, TEXT, MUTED = "#101923", "#f2f5f8", "#b6c3d2"
EDGE = "#101923"
COLOURS = {1: "#36b9ee", 2: "#ffad45", 3: "#c084fc", 99: "#e8edf1"}
FINAL_HOLD_MS = 3000
FONT_DIR = Path("C:/Windows/Fonts")


def font(size, bold=False):
    return ImageFont.truetype(str(FONT_DIR / ("segoeuib.ttf" if bold else "segoeui.ttf")), size)


FONTS = {"title": font(29, True), "config": font(17), "body": font(19),
         "small": font(16), "label": font(18, True), "number": font(12, True)}
MODES = {
    3: ("Server Side / return on exit (default)", "return-on-exit.gif",
        "4-metre cells / character heading", "KeepOwnershipOnCellExit = false",
        "Vacated cells return to the server. Shared cells also use the server."),
    2: ("Server Side / keep ownership", "retained.gif",
        "4-metre cells / character heading", "KeepOwnershipOnCellExit = true",
        "Vacated cells retain their last owner. Shared cells use the server."),
}


def text(draw, xy, value, style="body", colour=TEXT, anchor=None):
    draw.text(xy, value, font=FONTS[style], fill=colour, anchor=anchor)


def point(x, z):
    return LEFT + (x + 0.5) * CELL, TOP + (ROWS - z - 0.5) * CELL


def label(draw, x, z, caption):
    px, py = point(x, z)
    width = draw.textlength(caption, font=FONTS["label"]) + 24
    draw.rounded_rectangle((px-width/2, py-16, px+width/2, py+16), radius=5, fill=BG)
    text(draw, (px, py), caption, "label", anchor="mm")


def outline(draw, cells, colour):
    cells = {tuple(c[:2]) for c in cells}
    lines = []
    for x, z in cells:
        if not (0 <= x < COLS and 0 <= z < ROWS):
            continue
        l, t = LEFT+x*CELL, TOP+(ROWS-z-1)*CELL
        r, b = l+CELL, t+CELL
        if (x-1, z) not in cells: lines.append((l, t, l, b))
        if (x+1, z) not in cells: lines.append((r, t, r, b))
        if (x, z+1) not in cells: lines.append((l, t, r, t))
        if (x, z-1) not in cells: lines.append((l, b, r, b))
    # Dark underlay keeps each player's grid visible on their own cell colour.
    for line in lines: draw.line(line, fill=EDGE, width=3)
    for line in lines: draw.line(line, fill=colour, width=1)


def render(mode, frame):
    image = Image.new("RGB", SIZE, BG)
    draw = ImageDraw.Draw(image)
    title, _, config, retention, note = MODES[mode]
    text(draw, (41, 21), title, "title")
    text(draw, (41, 67), config, "config", MUTED)
    text(draw, (572, 67), retention, "config", MUTED)
    for i, (owner, caption) in enumerate(((1, "Player 1"), (2, "Player 2"), (3, "Player 3"), (99, "Server"))):
        if mode == 4 and owner == 99:
            continue
        x = 41+i*225
        draw.rectangle((x, 108, x+17, 125), fill=COLOURS[owner])
        if mode == 4:
            caption += {1: " / 80 ms", 2: " / 25 ms", 3: " / 140 ms"}[owner]
        text(draw, (x+27, 104), caption, "body")
    # Build a small indexed ownership map first. Nearest-neighbour scaling is
    # exact and avoids thousands of drawing calls per animation frame.
    owner_map = Image.frombytes("P", (COLS, ROWS), bytes(frame["owners"]))
    colours = [0]*768
    for owner, value in COLOURS.items():
        colours[owner*3:owner*3+3] = tuple(bytes.fromhex(value[1:]))
    owner_map.putpalette(colours)
    owner_map = owner_map.transpose(Image.Transpose.FLIP_TOP_BOTTOM).convert("RGB")
    image.paste(owner_map.resize((COLS*CELL, ROWS*CELL), Image.Resampling.NEAREST), (LEFT, TOP))
    for x in range(COLS+1):
        draw.line((LEFT+x*CELL, TOP, LEFT+x*CELL, TOP+ROWS*CELL), fill=EDGE)
    for z in range(ROWS+1):
        draw.line((LEFT, TOP+z*CELL, LEFT+COLS*CELL, TOP+z*CELL), fill=EDGE)
    for player in frame["players"]:
        outline(draw, player["coverage"], COLOURS[player["id"]])
    label(draw, 10, 46, "Forge")
    label(draw, 38, 46, "Storage")
    label(draw, 32, 3, "Farm")
    for player in frame["players"]:
        x, y = point(player["x"], player["z"])
        yaw = math.radians(player["yaw"])
        dx, dy = math.sin(yaw), -math.cos(yaw)
        # A small facing triangle, then a numbered player marker.
        tip = (x+16*dx, y+16*dy)
        side1 = (x+8*dx-4*dy, y+8*dy+4*dx)
        side2 = (x+8*dx+4*dy, y+8*dy-4*dx)
        draw.polygon((tip, side1, side2), fill=EDGE)
        draw.ellipse((x-10, y-10, x+10, y+10), fill=COLOURS[player["id"]], outline=EDGE, width=2)
        text(draw, (x, y-1), str(player["id"]), "number", EDGE, "mm")
    text(draw, (41, 1189), frame["phase"], "body")
    text(draw, (41, 1219), note, "small", MUTED)
    text(draw, (41, 1246), "50 x 50 cells   /   Cell colour = owner   /   Full rear 3x3 + forward row / yellow cells off / 20x playback", "small", MUTED)
    return image


def palette():
    # One shared palette avoids frame-to-frame dithering/colour shifts.
    image = Image.new("RGB", (256, 256), BG)
    draw = ImageDraw.Draw(image)
    colours = [BG, TEXT, MUTED, *COLOURS.values()]
    for i, colour in enumerate(colours):
        draw.rectangle((i*32, 0, min(255, i*32+31), 255), fill=colour)
    return image.quantize(colors=128, method=Image.Quantize.MEDIANCUT)


def set_final_hold(path, milliseconds=FINAL_HOLD_MS):
    """Set the final displayed frame's exact hold without re-encoding pixels.

    Pillow merges identical tail frames. Adjust the real final graphic-control
    delay after encoding so that this hold is exactly the requested duration.
    Parse block boundaries; compressed image bytes must never be searched.
    """
    data = bytearray(path.read_bytes())
    assert data[:6] in (b"GIF87a", b"GIF89a")
    offset = 13 + (3 * (2 << (data[10] & 7)) if data[10] & 128 else 0)
    delay = None
    final_delay = None

    def blocks(start):
        while data[start]:
            start += 1+data[start]
        return start+1

    while offset < len(data):
        marker = data[offset]
        if marker == 0x3B:
            break
        if marker == 0x21:
            if data[offset+1] == 0xF9:
                assert data[offset+2] == 4 and data[offset+7] == 0
                delay = offset+4
            offset = blocks(offset+2)
        elif marker == 0x2C:
            packed = data[offset+9]
            offset += 10 + (3*(2 << (packed & 7)) if packed & 128 else 0)
            offset = blocks(offset+1)
            final_delay = delay
            delay = None
        else:
            raise ValueError(f"Unexpected GIF block at {offset}")
    assert final_delay is not None and milliseconds % 10 == 0
    data[final_delay:final_delay+2] = (milliseconds//10).to_bytes(2, "little")
    path.write_bytes(data)


def validate(path, final, pal):
    import hashlib
    from PIL import ImageChops
    with Image.open(path) as gif:
        assert gif.is_animated and gif.info.get("loop") == 0
        duration = 0
        for frame in range(gif.n_frames):
            gif.seek(frame)
            duration += gif.info["duration"]
        assert gif.info["duration"] == 3000
        assert duration <= 18000
        expected = final.quantize(palette=pal, dither=Image.Dither.NONE).convert("RGB")
        assert ImageChops.difference(gif.convert("RGB"), expected).getbbox() is None
        return {"file": path.relative_to(OUT).as_posix(), "size": gif.size,
                "frames": gif.n_frames, "duration_ms": duration,
                "final_hold_ms": 3000, "final_frame_verified": True,
                "bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}


def save_animation(images, path):
    durations = [50] * len(images)
    durations[-1] = FINAL_HOLD_MS
    images[0].save(path, save_all=True, append_images=images[1:],
                   duration=durations, loop=0, disposal=1, optimize=True)
    set_final_hold(path)


def main():
    import hashlib
    import compact
    parser = argparse.ArgumentParser()
    parser.add_argument("snapshots", type=Path)
    args = parser.parse_args()
    data = json.loads(args.snapshots.read_text(encoding="utf-8-sig"))
    assert data["width"] == COLS and data["height"] == ROWS
    assert [s["mode"] for s in data["modes"]] == [3, 2]
    assert all(a["players"] == b["players"] for a, b in zip(data["modes"][0]["frames"], data["modes"][1]["frames"]))
    OUT.mkdir(parents=True, exist_ok=True)
    small = OUT / "compact"
    small.mkdir(exist_ok=True)
    pal = palette()
    checks, previews = [], []
    for scenario in data["modes"]:
        mode = scenario["mode"]
        frames = scenario["frames"]
        images = [render(mode, f).quantize(palette=pal, dither=Image.Dither.NONE) for f in frames]
        target = OUT / MODES[mode][1]
        save_animation(images, target)
        final = render(mode, frames[-1])
        final.save(target.with_suffix(".png"))
        checks.append(validate(target, final, pal))
        del images
        images = [compact.render(mode, f).quantize(palette=pal, dither=Image.Dither.NONE) for f in frames]
        target = small / MODES[mode][1]
        save_animation(images, target)
        final = compact.render(mode, frames[-1])
        final.save(target.with_suffix(".png"))
        checks.append(validate(target, final, pal))
        previews.append(images)
    combined = []
    captions = ["Default: return / CPU: mostly server", "Retain ownership / CPU: shared"]
    for frame in range(len(previews[0])):
        image = Image.new("RGB", (760, 462), BG)
        draw = ImageDraw.Draw(image)
        for col in range(2):
            draw.text((col * 380 + 15, 3), captions[col], font=compact.BOLD, fill=TEXT)
            image.paste(previews[col][frame].convert("RGB"), (col * 380, 24))
        combined.append(image.quantize(palette=pal, dither=Image.Dither.NONE))
    target = OUT / "comparison-preview.gif"
    save_animation(combined, target)
    final = combined[-1].convert("RGB")
    final.save(OUT / "comparison-preview.png")
    checks.append(validate(target, final, pal))
    report = {"source_grid_sha256": hashlib.sha256((ROOT / "src/AuthorityGrid.cs").read_bytes()).hexdigest(),
              "normal_cells": 12, "normal_inner": 9, "normal_forward": 3,
              "yellow_enabled": False, "full_rear_in_both_modes": True,
              "character_heading_only": True, "camera_overlap": False,
              "speed_threshold_mps": 4, "speed_release_mps": 3,
              "snapshot_seconds": 1, "playback_factor": 20,
              "high_ping_expansion_illustrated": False,
              "server_overlap_ticks": [s["serverOverlapTicks"] for s in data["modes"]],
              "speed_boost_ticks": [s["fastTicks"] for s in data["modes"]], "files": checks}
    (OUT / "render-checks.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    print("PASS: two real Server Side masks, matching routes, decoded final frames and exact 3-second final holds.")


if __name__ == "__main__":
    main()
