param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$diagramRoot = Split-Path $PSScriptRoot
$diagramProbe = Join-Path $diagramRoot 'bin/grid-coverage-diagram'
New-Item -ItemType Directory -Force -Path $diagramProbe,$OutputDirectory | Out-Null
$diagramProbeSource = @'
using System;
using System.Collections.Generic;
using TooManyLagsDedicatedAddon;
internal static class DiagramMask
{
    public static void Main()
    {
        Console.WriteLine("Case,X,Z,Tier");
        for (int scenario = 0; scenario < 4; scenario++)
        {
            var grid = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7,
                true, 0, 3, false, true, .2f, null, 30, 0, true, false);
            grid.Update(new List<GridPeer> {
                new GridPeer(1, new Cell(0, 0), 0, -1, (scenario & 1) != 0, scenario >= 2)
            }, 0);
            for (int z = 3; z >= -1; z--) for (int x = -2; x <= 2; x++)
            {
                var cell = new Cell(x, z);
                if (grid.Candidate(cell) == 0) continue;
                Console.WriteLine(scenario + "," + x + "," + z + "," +
                    (grid.Wait(cell) == 0 ? "Inner" : "Forward"));
            }
        }
    }
}
'@
$diagramCs = Join-Path $diagramProbe 'DiagramMask.cs'
$diagramExe = Join-Path $diagramProbe 'DiagramMask.exe'
[IO.File]::WriteAllText($diagramCs,$diagramProbeSource)
$diagramCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
& $diagramCompiler /nologo /optimize+ /langversion:5 /warnaserror+ /nowarn:0649 ('/out:'+$diagramExe) (Join-Path $diagramRoot 'src/AuthorityGrid.cs') $diagramCs
if ($LASTEXITCODE -ne 0) { throw 'Grid diagram probe did not compile.' }
$diagramCells = @(& $diagramExe | ConvertFrom-Csv)
if ($LASTEXITCODE -ne 0) { throw 'Grid diagram probe failed.' }
$diagramCounts = @(@(9,3),@(12,0),@(16,3),@(19,0))
for ($scenario=0;$scenario -lt 4;$scenario++) {
    $cells = @($diagramCells | Where-Object { [int]$_.Case -eq $scenario })
    if (@($cells | Where-Object Tier -eq 'Inner').Count -ne $diagramCounts[$scenario][0] -or
        @($cells | Where-Object Tier -eq 'Forward').Count -ne $diagramCounts[$scenario][1]) {
        throw ('Unexpected production grid mask: scenario '+$scenario)
    }
}

Add-Type -AssemblyName System.Drawing
$script:diagramSvg = [Text.StringBuilder]::new()
[void]$script:diagramSvg.AppendLine('<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1350" viewBox="0 0 1600 1350" role="img" aria-labelledby="title desc">')
[void]$script:diagramSvg.AppendLine('<title id="title">TooManyLags Server Side: grid coverage and transition delays</title><desc id="desc">Four north-facing examples using production grid masks. Normal: nine immediate red cells plus three orange cells with 0.2 seconds delay. Fast movement: the same twelve cells are immediate. Poor connection: sixteen immediate red cells plus three orange cells. Poor connection and fast movement: all nineteen cells are immediate. White cells make no player-grid request. Yellow outer cells are disabled by default. Green marks the player and character heading.</desc>')
$diagramBitmap = [Drawing.Bitmap]::new(1600,1350)
$script:diagramGraphics = [Drawing.Graphics]::FromImage($diagramBitmap)
$script:diagramGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$script:diagramGraphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$script:diagramGraphics.Clear([Drawing.Color]::White)

function Diagram-Rect([float]$X,[float]$Y,[float]$Width,[float]$Height,[string]$Fill,[float]$Radius=0,[string]$Stroke='') {
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($Fill))
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    try {
        if ($Radius -gt 0) {
            $diameter=2*$Radius
            $path.AddArc($X,$Y,$diameter,$diameter,180,90)
            $path.AddArc(($X+$Width-$diameter),$Y,$diameter,$diameter,270,90)
            $path.AddArc(($X+$Width-$diameter),($Y+$Height-$diameter),$diameter,$diameter,0,90)
            $path.AddArc($X,($Y+$Height-$diameter),$diameter,$diameter,90,90)
            $path.CloseFigure()
        } else { $path.AddRectangle([Drawing.RectangleF]::new($X,$Y,$Width,$Height)) }
        $script:diagramGraphics.FillPath($brush,$path)
        if ($Stroke) {
            $pen=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($Stroke),1.5)
            try { $script:diagramGraphics.DrawPath($pen,$path) } finally { $pen.Dispose() }
        }
    } finally { $brush.Dispose();$path.Dispose() }
    $strokeAttribute=if($Stroke){' stroke="'+$Stroke+'" stroke-width="1.5"'}else{''}
    [void]$script:diagramSvg.AppendLine("<rect x=`"$X`" y=`"$Y`" width=`"$Width`" height=`"$Height`" rx=`"$Radius`" fill=`"$Fill`"$strokeAttribute/>")
}
function Diagram-Text([float]$X,[float]$Y,[string]$Text,[float]$Size=18,[string]$Color='#253344',[bool]$Bold=$false) {
    $style=if($Bold){[Drawing.FontStyle]::Bold}else{[Drawing.FontStyle]::Regular}
    $font=[Drawing.Font]::new('Segoe UI',$Size,$style,[Drawing.GraphicsUnit]::Pixel)
    $brush=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($Color))
    try { $script:diagramGraphics.DrawString($Text,$font,$brush,[Drawing.PointF]::new($X,$Y)) }
    finally { $font.Dispose();$brush.Dispose() }
    $escaped=[Security.SecurityElement]::Escape($Text)
    $weight=if($Bold){700}else{400}
    $baseline=$Y+$Size
    [void]$script:diagramSvg.AppendLine("<text x=`"$X`" y=`"$baseline`" font-family=`"Segoe UI,Arial,sans-serif`" font-size=`"$Size`" font-weight=`"$weight`" fill=`"$Color`">$escaped</text>")
}
function Diagram-Line([float]$X1,[float]$Y1,[float]$X2,[float]$Y2,[string]$Color,[float]$Width=1) {
    $pen=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($Color),$Width)
    try { $script:diagramGraphics.DrawLine($pen,$X1,$Y1,$X2,$Y2) } finally { $pen.Dispose() }
    [void]$script:diagramSvg.AppendLine("<line x1=`"$X1`" y1=`"$Y1`" x2=`"$X2`" y2=`"$Y2`" stroke=`"$Color`" stroke-width=`"$Width`" stroke-linecap=`"round`"/>")
}
function Diagram-Player([float]$X,[float]$Y) {
    foreach($style in @(@('#FFFFFF',10),@('#158F59',6))) {
        Diagram-Line $X ($Y-13) $X ($Y-78) $style[0] $style[1]
        Diagram-Line $X ($Y-81) ($X-12) ($Y-63) $style[0] $style[1]
        Diagram-Line $X ($Y-81) ($X+12) ($Y-63) $style[0] $style[1]
    }
    $brush=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#158F59'))
    $pen=[Drawing.Pen]::new([Drawing.Color]::White,4)
    try {
        $script:diagramGraphics.FillEllipse($brush,($X-19),($Y-19),38,38)
        $script:diagramGraphics.DrawEllipse($pen,($X-19),($Y-19),38,38)
    } finally { $brush.Dispose();$pen.Dispose() }
    [void]$script:diagramSvg.AppendLine("<circle cx=`"$X`" cy=`"$Y`" r=`"19`" fill=`"#158F59`" stroke=`"white`" stroke-width=`"4`"/>")
    Diagram-Text ($X-12) ($Y-7) 'YOU' 10 '#FFFFFF' $true
}
function Diagram-Panel([int]$Case,[float]$X,[float]$Y,[string]$Title,[string]$Subtitle,[string]$Note1,[string]$Note2) {
    Diagram-Rect $X $Y 740 480 '#F5F7FA' 16
    Diagram-Text ($X+24) ($Y+20) $Title 26 '#172332' $true
    Diagram-Text ($X+24) ($Y+58) $Subtitle 18 '#526072'
    $mask=@{}
    foreach($cell in $diagramCells) { if([int]$cell.Case -eq $Case){$mask[([string]$cell.X+','+[string]$cell.Z)]=$cell.Tier} }
    for($cellZ=3;$cellZ -ge -1;$cellZ--) { for($cellX=-2;$cellX -le 2;$cellX++) {
        $tier=$mask[([string]$cellX+','+[string]$cellZ)]
        $color=if($tier -eq 'Inner'){'#F04A55'}elseif($tier -eq 'Forward'){'#F49B32'}else{'#FFFFFF'}
        $border=if($tier){''}else{'#DCE2EA'}
        $tx=$X+24+($cellX+2)*62; $ty=$Y+110+(3-$cellZ)*62
        Diagram-Rect $tx $ty 56 56 $color 5 $border
        if(-not $tier){Diagram-Line ($tx+20) ($ty+28) ($tx+36) ($ty+28) '#CBD2DC' 2}
    } }
    Diagram-Player ($X+176) ($Y+324)
    $immediate=$diagramCounts[$Case][0];$forward=$diagramCounts[$Case][1]
    Diagram-Text ($X+370) ($Y+123) ($immediate.ToString()+' immediate cells') 29 '#172332' $true
    Diagram-Text ($X+371) ($Y+165) 'Red: 0 seconds' 21 '#D63343'
    if($forward) {
        Diagram-Text ($X+370) ($Y+224) '3 forward cells' 29 '#172332' $true
        Diagram-Text ($X+371) ($Y+266) 'Orange: 0.2 seconds' 21 '#B56511'
    } else {
        Diagram-Text ($X+370) ($Y+224) 'Forward row: 0 s' 29 '#172332' $true
        Diagram-Text ($X+371) ($Y+266) 'Speed removes its wait.' 21 '#526072'
    }
    Diagram-Text ($X+370) ($Y+342) $Note1 18 '#526072'
    Diagram-Text ($X+370) ($Y+372) $Note2 18 '#526072'
    Diagram-Text ($X+25) ($Y+439) 'Character facing: up  /  4 x 4 m per cell' 17 '#526072'
}

try {
    Diagram-Rect 0 0 1600 1350 '#FFFFFF'
    Diagram-Text 54 30 'TOOMANYLAGS / SERVER SIDE' 15 '#5B6878' $true
    Diagram-Text 52 65 'Grid coverage and transition delays' 38 '#172332' $true
    Diagram-Text 55 121 'Default directional settings. Poor connections expand the grid; fast movement removes the forward wait.' 21 '#526072'
    Diagram-Rect 56 179 20 20 '#F04A55' 4
    Diagram-Text 88 174 'Immediate: 0 s' 19 '#253344' $true
    Diagram-Rect 322 179 20 20 '#F49B32' 4
    Diagram-Text 354 174 'Forward: 0.2 s' 19 '#253344' $true
    Diagram-Rect 594 179 20 20 '#FFFFFF' 4 '#CED5DE'
    Diagram-Text 626 174 'No player-grid request' 19 '#526072'
    Diagram-Rect 980 179 20 20 '#F4D54D' 4
    Diagram-Text 1012 174 'Optional outer: 3 s (off by default)' 19 '#526072'

    Diagram-Panel 0 48 230 'Normal connection' 'Default grid, normal forward delay' 'Full red 3 x 3 centre.' 'Rear cells stay active.'
    Diagram-Panel 1 812 230 'Fast movement' 'Forward row becomes immediate at 4 m/s' 'Same area, shorter wait.' 'Normal wait returns below 3 m/s.'
    Diagram-Panel 2 48 736 'Poor connection' 'Larger grid starts at 200 ms ping' 'Red rows: 3 / 5 / 5 / 3.' 'Normal size returns below 150 ms.'
    Diagram-Panel 3 812 736 'Poor connection + fast movement' 'Both adaptations active' 'Expanded grid; forward row is red.' 'No additional configured wait.'

    Diagram-Line 56 1242 1544 1242 '#DFE4EB' 1
    Diagram-Text 56 1258 'Thresholds and delays are configurable. White cells are not requested by the player grid; this does not mean unloaded.' 19 '#253344'
    Diagram-Text 56 1290 'Cardinal direction shown. Diagonal large grids use 20 red + 3 forward cells. Loading and active-use guards still apply.' 18 '#526072'
    [void]$script:diagramSvg.AppendLine('</svg>')
    $diagramSvgPath=Join-Path $OutputDirectory 'grid-coverage.svg'
    $diagramPngPath=Join-Path $OutputDirectory 'grid-coverage.png'
    [IO.File]::WriteAllText($diagramSvgPath,$script:diagramSvg.ToString(),[Text.UTF8Encoding]::new($false))
    $diagramBitmap.Save($diagramPngPath,[Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{PNG=$diagramPngPath;SVG=$diagramSvgPath;VerifiedMasks='9+3; 12+0; 16+3; 19+0'}
} finally { $script:diagramGraphics.Dispose();$diagramBitmap.Dispose() }
