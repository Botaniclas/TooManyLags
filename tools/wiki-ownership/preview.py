"""Synchronized animated 2x2 README preview; no changes to the individual GIFs."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw
from compact import BG, BOLD, TEXT, OUT, PREVIEW_LABELS, render
from render import palette, set_final_hold


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("snapshots", type=Path)
    args = parser.parse_args()
    data = json.loads(args.snapshots.read_text(encoding="utf-8-sig"))
    assert data["width"] == data["height"] == 50
    modes = sorted(data["modes"], key=lambda mode: mode["mode"])
    assert [mode["mode"] for mode in modes] == [1, 2, 3, 4]
    times = [frame["time"] for frame in modes[0]["frames"]]
    assert all([frame["time"] for frame in mode["frames"]] == times for mode in modes)
    base = Image.new("RGB", (760, 924), BG)
    draw = ImageDraw.Draw(base)
    for mode in range(1, 5):
        x, y = ((mode-1) % 2)*380, ((mode-1)//2)*462
        assert draw.textlength(PREVIEW_LABELS[mode-1], font=BOLD) <= 350
        draw.text((x+15, y+3), PREVIEW_LABELS[mode-1], font=BOLD, fill=TEXT)
    pal = palette()
    images = []
    for index in range(len(times)):
        image = base.copy()
        for mode in modes:
            number = mode["mode"]
            x, y = ((number-1) % 2)*380, ((number-1)//2)*462
            image.paste(render(number, mode["frames"][index]), (x, y+24))
        images.append(image.quantize(palette=pal, dither=Image.Dither.NONE))
    target = OUT / "compact" / "readme-preview.gif"
    durations = [50]*len(images)
    durations[-1] = 3000
    images[0].save(target, save_all=True, append_images=images[1:], duration=durations,
                   loop=0, disposal=1, optimize=True)
    set_final_hold(target)
    with Image.open(target) as gif:
        assert gif.size == (760, 924) and gif.is_animated and gif.n_frames > 200
        elapsed = 0
        for index in range(gif.n_frames):
            gif.seek(index)
            elapsed += gif.info["duration"]
        assert gif.info["duration"] == 3000 and elapsed <= 18000
        assert ImageChops.difference(gif.convert("RGB"), images[-1].convert("RGB")).getbbox() is None
        report = {"file": target.name, "frames": gif.n_frames, "size": gif.size,
                  "duration_ms": elapsed, "final_hold_ms": 3000, "synchronized_panels": True,
                  "final_frame_matches": True, "bytes": target.stat().st_size,
                  "sha256": hashlib.sha256(target.read_bytes()).hexdigest()}
    (target.parent / "animated-preview-checks.json").write_text(json.dumps(report, indent=2)+"\n", encoding="utf-8")
    print("PASS: synchronized animated 2x2 preview; CPU headings, all four modes, 20x playback, exact 3-second final hold.")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
