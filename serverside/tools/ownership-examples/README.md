# Server Side ownership animations

Documentation-only visualisations generated from the unchanged `src/AuthorityGrid.cs` used by Server Side. No Unity, installed profiles, configuration values or game saves are modified.

- Two server-default scenarios: return on exit (default) and retained ownership.
- Identical five-minute routes in a 50 x 50-cell area; each cell is 4 metres.
- Character rotation selects the nearest of eight directions. No camera data, angular overlap or rotation prediction.
- Both modes keep the full rear 3x3. Yellow cells are off; the three-cell forward row uses 0.2 seconds. Red uses 0; retirement uses 3 seconds; overlap hold uses 1 second.
- Synthetic native horizontal speeds follow the routes. The same controller thresholds activate immediate forward cells at 4 m/s and release that boost below 3 m/s.
- All example connections remain below the 200-ms larger-grid threshold; the high-ping expansion is not shown.
- One idle, ready schematic object per cell; all areas remain loaded. Native action guards, network delivery, server stalls and mob-specific decisions are outside this illustration.
- Snapshots every simulated second; 20x playback, infinite looping and an exact 3-second final hold.
- Displayed outlines are read from the actual production selector's selected masks, including speed variants. No separate hand-drawn grid approximations.

From this project root, compile the driver against the production grid using the .NET Framework compiler:

```powershell
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$exe = Join-Path (Get-Location) 'bin\ownership-examples\OwnershipFrames.exe'
New-Item -ItemType Directory -Force -Path (Split-Path $exe) | Out-Null
& $compiler /nologo /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 /reference:System.Web.Extensions.dll ('/out:' + $exe) (Join-Path (Get-Location) 'src\AuthorityGrid.cs') (Join-Path (Get-Location) 'tools\ownership-examples\OwnershipFrames.cs')
& $exe (Join-Path (Get-Location) 'bin\ownership-examples\frames.json')
python tools/ownership-examples/render.py bin/ownership-examples/frames.json
```

Use a real Python executable with Pillow if the Windows Store `python` alias is unavailable. Outputs are large GIFs, compact README GIFs, decoded final PNGs, an animated comparison and `render-checks.json`. Checks cover all eight default masks, speed-selected timer tiers, server overlaps, settled returns, retention, matching routes, final workplaces, GIF decoding and final-frame duration.

This does not publish or push media. README URLs point to `serverside/docs/ownership-examples/` in the shared repository and require a later authorized Git push.
