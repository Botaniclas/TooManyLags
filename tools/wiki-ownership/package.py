"""Check and package local wiki media; no publication or runtime packaging."""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import zipfile
from pathlib import Path

from PIL import Image, ImageChops
from render import MODES, OUT, ROOT, SIZE, BG, FINAL_HOLD_MS, palette


def make_zip(directory, target):
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(directory.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(directory).as_posix())


def remove_legacy_images(directory):
    # Remove only the six explicitly renamed media files, after the new GIFs
    # have passed validation. Preserve unrelated files and user edits.
    for stem in ("vanilla-default-retained", "server-default-retained", "server-default-return"):
        for suffix in (".gif", ".png"):
            old = directory / (stem+suffix)
            if old.is_file():
                old.unlink()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("outputs", type=Path)
    args = parser.parse_args()
    outputs = args.outputs.resolve()
    checks = json.loads((OUT / "render-checks.json").read_text())
    pal = palette()
    comparison = Image.new("RGB", (1080, 1268), BG)
    for i, check in enumerate(checks):
        path = OUT / check["file"]
        with Image.open(path) as gif:
            assert gif.is_animated and gif.size == SIZE
            total = 0
            for frame in range(gif.n_frames):
                gif.seek(frame)
                total += gif.info["duration"]
            assert total == check["duration_ms"] and total <= 18000
            assert gif.info["duration"] == FINAL_HOLD_MS == check["final_hold_ms"]
            with Image.open(path.with_suffix(".png")) as original:
                final = original.quantize(palette=pal, dither=Image.Dither.NONE).convert("RGB")
                assert ImageChops.difference(gif.convert("RGB"), final).getbbox() is None, "GIF disposal damaged final ownership map"
                comparison.paste(original.resize((540, 634), Image.Resampling.LANCZOS), ((i%2)*540, (i//2)*634))
        check["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
        check["final_frame_matches_png"] = True
    comparison.save(OUT / "final-comparison.png")
    (OUT / "render-checks.json").write_text(json.dumps(checks, indent=2)+"\n", encoding="utf-8")
    remove_legacy_images(OUT)

    bundle = outputs / "Others" / "TooManyLags-Wiki-Ownership-Examples"
    bundle.mkdir(parents=True, exist_ok=True)
    remove_legacy_images(bundle / "ownership-examples")
    shutil.copytree(OUT, bundle / "ownership-examples", dirs_exist_ok=True)
    shutil.copytree(ROOT / "docs" / "wiki", bundle / "wiki", dirs_exist_ok=True)
    (bundle / "README.md").write_text(
        "# TooManyLags ownership examples\n\n"
        "Four English GIFs: 50 x 50 cells, identical five-minute routes with independent player detours, 20x playback and a 3-second final hold.\n\n"
        "- [View all four examples](ownership-examples/README.md)\n"
        "- [Final ownership comparison](ownership-examples/final-comparison.png)\n"
        "- [Updated configuration wiki page](wiki/01-Configuration.md)\n\n"
        "Only coloured cells, small player/grid markers and location labels are drawn. "
        "Wiki image links use raw GitHub URLs; media must be pushed before those online links work. "
        "These files are prepared locally; nothing is published.\n", encoding="utf-8")
    make_zip(bundle, bundle.with_suffix(".zip"))

    # Refresh only the current local documentation delivery, never DLL/install
    # archives, source archives, installed profiles or old release archives.
    wiki = outputs / "TooManyLags-FGNAddon-1.0.57-Wiki"
    wiki.mkdir(parents=True, exist_ok=True)
    remove_legacy_images(wiki / "ownership-examples")
    for source in (ROOT / "docs" / "wiki").glob("*.md"):
        shutil.copy2(source, wiki / source.name)
    shutil.copytree(OUT, wiki / "ownership-examples", dirs_exist_ok=True)
    make_zip(wiki, outputs / "TooManyLags-FGNAddon-1.0.57-wiki.zip")
    print("PASS: four GIFs, exact decoded final frames, 3-second final holds and local wiki/media bundles verified.")
    print(bundle.with_suffix(".zip"))


if __name__ == "__main__":
    main()
