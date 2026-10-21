# Wiki ownership animations

Documentation-only diagrams. Production runtime sources are read and compiled unchanged; no installed mods, game files, saves or configuration values are modified.

`OwnershipFrames.cs` runs the current `AuthorityGrid` and `PlayerSimulation.AuthorityGrid` selectors against identical scripted paths. Footprints are read from the compiled selectors. One always-ready, idle schematic object represents each cell: a settled request changes its colour; an empty field retains or returns ownership according to the scenario. Object-specific safeguards and transport latency are intentionally outside this illustration.

- 50 x 50 cells, each 4 metres; all remain loaded for the illustration.
- Rounded7x7; camera held along the route, using the normal 30-degree angular overlap. Additional rotation prediction is not illustrated.
- Inner / forward / outer timers: 0 / 0.2 / 3 seconds; server overlap cooldown: 1 second.
- Production ticks: 0.05 seconds; image snapshots: 1 second.
- Simulated duration: five minutes; playback: twenty times normal speed, then an exact 3-second final hold. Identical final snapshots are merged, so each loop is slightly shorter than 18 seconds.
- Separate fixed random seeds add different detours to each player's route. Players keep moving along different paths during the meeting; those paths remain identical across ownership modes.
- No Server-Side Simulation pings: Player 1 = 80 ms, Player 2 = 25 ms, Player 3 = 140 ms. Differences exceed the 30 ms stability margin.
- Player paths and timings are identical in all four scenarios. Return-on-exit deliberately uses its production full-rear footprint.
- Static pieces not enrolled in the static grid are not shown.

From the project root, with .NET Framework's compiler and a Python environment containing Pillow:

```powershell
New-Item -ItemType Directory -Path bin/wiki-ownership -Force | Out-Null
& "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe" /nologo /optimize+ /r:System.Web.Extensions.dll /out:bin\wiki-ownership\OwnershipFrames.exe src\AuthorityGrid.cs src\PlayerSimulation\AuthorityGrid.cs tools\wiki-ownership\OwnershipFrames.cs
& .\bin\wiki-ownership\OwnershipFrames.exe bin/wiki-ownership/frames.json
python tools/wiki-ownership/render.py bin/wiki-ownership/frames.json
```

Use a real Python executable if `python` resolves to an unusable Windows Store alias.

The driver checks final workplace ownership, server overlaps, retained trails, settled returns and absence of server ownership in player-only mode. The renderer checks matching paths, decodable animation dimensions and duration. `docs/ownership-examples/comparison-preview.png` contains initial, overlapping and final states for visual review.

`python tools/wiki-ownership/package.py <absolute outputs path>` verifies decoded final GIF frames against their PNGs, creates `final-comparison.png`, refreshes the current documentation-only ZIP and writes a standalone media bundle under `outputs/Others/`. It leaves DLL, install, source and historical archives unchanged.

Images are prepared locally. Wiki links use the project's raw GitHub URLs and become available after the media are pushed. Rendering does not publish or push anything.

`python tools/wiki-ownership/compact.py bin/wiki-ownership/frames.json` creates 380 x 438-pixel README variants from the same ownership snapshots. They keep 20x playback and the exact 3-second final hold, with simplified readable labels. Both READMEs show a two-column gallery linked to the full-size GIFs; no HTML sizing attributes are needed. Compact media must be pushed before those new README image links work online.

`python tools/wiki-ownership/preview.py bin/wiki-ownership/frames.json` creates a synchronized animated 2x2 overview at `docs/ownership-examples/compact/readme-preview.gif`, including the CPU headings. The PNG overview remains a separate still image. The animation retains 20x playback and an exact 3-second final hold.
