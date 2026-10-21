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
    1: ("Default settings", "default-settings.gif",
        "ServerOwnedInteractivesByDefault = false", "KeepOwnershipOnCellExit = true",
        "Unvisited cells stay with Player 1. Vacated cells keep their last owner."),
    2: ("Max server ownership / keep ownership", "max-server-ownership-retained.gif",
        "ServerOwnedInteractivesByDefault = true", "KeepOwnershipOnCellExit = true",
        "Unvisited cells stay with the server. Vacated cells keep their last owner."),
    3: ("Max server ownership / return on exit", "max-server-ownership-return.gif",
        "ServerOwnedInteractivesByDefault = true", "KeepOwnershipOnCellExit = false",
        "Vacated cells return to the server. The grid also covers the rear centre."),
    4: ("No Server-Side Simulation", "no-server-simulation.gif",
        "NoServerSideSimulation = true", "KeepOwnershipOnCellExit = true",
        "Shared cells prefer the better-connected player. Vacated cells keep their owner."),
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
    cells = {tuple(c) for c in cells}
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
    text(draw, (41, 1246), "50 x 50 cells   /   Cell colour = owner   /   Same five-minute routes in every example", "small", MUTED)
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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("snapshots", type=Path)
    args = parser.parse_args()
    data = json.loads(args.snapshots.read_text(encoding="utf-8-sig"))
    assert data["width"] == COLS and data["height"] == ROWS
    OUT.mkdir(parents=True, exist_ok=True)
    pal = palette()
    manifest = []
    for scenario in data["modes"]:
        mode = scenario["mode"]
        frames = scenario["frames"]
        # Source snapshots prove paths and headings identical across all modes.
        if mode > 1:
            assert len(frames) == len(data["modes"][0]["frames"])
            assert all(
                [{k: v for k, v in p.items() if k != "coverage"} for p in f["players"]] ==
                [{k: v for k, v in p.items() if k != "coverage"} for p in ref["players"]]
                for f, ref in zip(frames, data["modes"][0]["frames"]))
            if mode != 3:
                assert [f["players"] for f in frames] == [f["players"] for f in data["modes"][0]["frames"]]
        images = [render(mode, f).quantize(palette=pal, dither=Image.Dither.NONE) for f in frames]
        # One simulated second per 50 ms frame: 20x playback, followed by
        # a longer final comparison hold. Both fit GIF's 10 ms timing units.
        durations = [50]*len(images)
        durations[-1] = FINAL_HOLD_MS
        target = OUT / MODES[mode][1]
        images[0].save(target, save_all=True, append_images=images[1:], duration=durations, loop=0, optimize=True, disposal=1)
        set_final_hold(target)
        render(mode, frames[-1]).save(target.with_suffix(".png"))
        with Image.open(target) as check:
            assert check.size == SIZE and check.is_animated
            duration = sum((check.seek(i), check.info["duration"])[1] for i in range(check.n_frames))
            manifest.append({"file": target.name, "frames": check.n_frames, "size": SIZE, "bytes": target.stat().st_size,
                             "duration_ms": duration, "final_hold_ms": FINAL_HOLD_MS, "server_overlap_ticks": scenario["serverOverlapTicks"],
                             "retained_ticks": scenario["retainedTicks"]})
        print(f"{target.name}: {target.stat().st_size:,} bytes")
    # Contact sheet includes the initial state, overlap and settled work areas.
    thumb = (540, 634)
    sheet = Image.new("RGB", (thumb[0]*3, thumb[1]*4), BG)
    for row, scenario in enumerate(data["modes"]):
        for col, moment in enumerate((0, 155, 300)):
            frame = min(scenario["frames"], key=lambda f: abs(f["time"]-moment))
            sheet.paste(render(scenario["mode"], frame).resize(thumb, Image.Resampling.LANCZOS), (col*thumb[0], row*thumb[1]))
    sheet.save(OUT / "comparison-preview.png")
    (OUT / "render-checks.json").write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
    print("PASS: four GIFs reopen correctly; identical player paths; PNG final states and comparison preview exported.")


if __name__ == "__main__":
    main()
