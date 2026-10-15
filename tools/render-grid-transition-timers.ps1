param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot) 'docs'))
$ErrorActionPreference = 'Stop'
$diagramRoot = Split-Path $PSScriptRoot
$diagramProbe = Join-Path $diagramRoot 'bin/grid-timer-diagram'
New-Item -ItemType Directory -Force -Path $diagramProbe,$OutputDirectory | Out-Null
$diagramProbeSource = @'
using System;
using System.Collections.Generic;
using TooManyLags;
internal static class DiagramMask
{
    public static void Main()
    {
        var active = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7,
            true, 0, 3, true, true, .2f, 1, false);
        var whole = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7,
            true, 0, 3, true, true, .2f, 1, true);
        var peer = new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), 0) };
        active.Update(peer, 0); whole.Update(peer, 0);
        Console.WriteLine("X,Z,Tier");
        for (int z = 3; z >= -3; z--) for (int x = -3; x <= 3; x++)
        {
            var cell = new Cell(x, z);
            if (whole.Candidate(cell) == 0) continue;
            string tier = active.Candidate(cell) == 0 ? "Inactive" :
                active.Wait(cell) == 0 ? "Inner" : active.Wait(cell) < 1 ? "Forward" : "Outer";
            Console.WriteLine(x + "," + z + "," + tier);
        }
    }
}
'@
$diagramCs = Join-Path $diagramProbe 'DiagramMask.cs'
$diagramExe = Join-Path $diagramProbe 'DiagramMask.exe'
[IO.File]::WriteAllText($diagramCs, $diagramProbeSource)
$diagramCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
& $diagramCompiler /nologo /optimize+ /langversion:5 /warnaserror+ /nowarn:0649 ('/out:'+$diagramExe) (Join-Path $diagramRoot 'src/AuthorityGrid.cs') $diagramCs
if ($LASTEXITCODE -ne 0) { throw 'Grid mask probe did not compile.' }
$diagramCells = @(& $diagramExe | ConvertFrom-Csv)
if ($LASTEXITCODE -ne 0) { throw 'Grid mask probe failed.' }
foreach ($expected in @{Inner=6;Forward=3;Outer=17;Inactive=11}.GetEnumerator()) {
    if (@($diagramCells | Where-Object Tier -eq $expected.Key).Count -ne $expected.Value) {
        throw ('Unexpected production grid mask: '+$expected.Key)
    }
}

Add-Type -AssemblyName System.Drawing
$script:diagramSvg = New-Object Text.StringBuilder
[void]$script:diagramSvg.AppendLine('<svg xmlns="http://www.w3.org/2000/svg" width="1320" height="940" viewBox="0 0 1320 940" role="img" aria-labelledby="title desc">')
[void]$script:diagramSvg.AppendLine('<title id="title">TooManyLags: the three transition timers</title><desc id="desc">Default north-facing 4-metre Rounded7x7 directional grid with ownership retention enabled: six inner red cells wait zero seconds, three forward orange cells wait 0.2 seconds, seventeen outer yellow cells wait three seconds. Eleven white rear cells make no new ownership request. A green marker shows the player and camera direction.</desc>')
$diagramBitmap = New-Object Drawing.Bitmap(1320,940)
$script:diagramGraphics = [Drawing.Graphics]::FromImage($diagramBitmap)
$script:diagramGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$script:diagramGraphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$script:diagramGraphics.Clear([Drawing.Color]::White)

function Diagram-Rect([float]$X,[float]$Y,[float]$Width,[float]$Height,[string]$Fill,[float]$Radius=0,[string]$Stroke='') {
    $color = [Drawing.ColorTranslator]::FromHtml($Fill)
    $brush = New-Object Drawing.SolidBrush($color)
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    try {
        if ($Radius -gt 0) {
            $diameter = 2*$Radius
            $path.AddArc($X,$Y,$diameter,$diameter,180,90)
            $path.AddArc(($X+$Width-$diameter),$Y,$diameter,$diameter,270,90)
            $path.AddArc(($X+$Width-$diameter),($Y+$Height-$diameter),$diameter,$diameter,0,90)
            $path.AddArc($X,($Y+$Height-$diameter),$diameter,$diameter,90,90)
            $path.CloseFigure()
        } else { $path.AddRectangle([Drawing.RectangleF]::new($X,$Y,$Width,$Height)) }
        $script:diagramGraphics.FillPath($brush,$path)
        if ($Stroke) {
            $pen = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml($Stroke),1.5)
            try { $script:diagramGraphics.DrawPath($pen,$path) } finally { $pen.Dispose() }
        }
    } finally { $brush.Dispose();$path.Dispose() }
    $strokeAttribute = if($Stroke){' stroke="'+$Stroke+'" stroke-width="1.5"'}else{''}
    [void]$script:diagramSvg.AppendLine("<rect x=`"$X`" y=`"$Y`" width=`"$Width`" height=`"$Height`" rx=`"$Radius`" fill=`"$Fill`"$strokeAttribute/>")
}
function Diagram-Text([float]$X,[float]$Y,[string]$Text,[float]$Size=18,[string]$Color='#253344',[bool]$Bold=$false) {
    $style = if($Bold){[Drawing.FontStyle]::Bold}else{[Drawing.FontStyle]::Regular}
    $font = New-Object Drawing.Font('Segoe UI',$Size,$style,[Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml($Color))
    try { $script:diagramGraphics.DrawString($Text,$font,$brush,[Drawing.PointF]::new($X,$Y)) }
    finally { $font.Dispose();$brush.Dispose() }
    $escaped = [Security.SecurityElement]::Escape($Text)
    $weight = if($Bold){700}else{400}
    $baseline = $Y+$Size
    [void]$script:diagramSvg.AppendLine("<text x=`"$X`" y=`"$baseline`" font-family=`"Segoe UI,Arial,sans-serif`" font-size=`"$Size`" font-weight=`"$weight`" fill=`"$Color`">$escaped</text>")
}
function Diagram-Line([float]$X1,[float]$Y1,[float]$X2,[float]$Y2,[string]$Color,[float]$Width=1) {
    $pen = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml($Color),$Width)
    try { $script:diagramGraphics.DrawLine($pen,$X1,$Y1,$X2,$Y2) } finally { $pen.Dispose() }
    [void]$script:diagramSvg.AppendLine("<line x1=`"$X1`" y1=`"$Y1`" x2=`"$X2`" y2=`"$Y2`" stroke=`"$Color`" stroke-width=`"$Width`" stroke-linecap=`"round`"/>")
}
function Diagram-Legend([int]$Y,[string]$Color,[string]$Title,[string]$Time,[string]$Detail,[string]$Key) {
    Diagram-Rect 744 $Y 504 146 '#F5F7FA' 14
    Diagram-Rect 765 ($Y+22) 18 18 $Color 4
    Diagram-Text 795 ($Y+16) $Title 21 '#253344' $true
    Diagram-Text 762 ($Y+43) $Time 43 '#172332' $true
    Diagram-Text 765 ($Y+96) $Detail 17
    Diagram-Text 765 ($Y+120) $Key 14 '#526072'
}

try {
    Diagram-Rect 0 0 1320 940 '#FFFFFF'
    Diagram-Text 66 32 'TOOMANYLAGS' 14 '#5B6878' $true
    Diagram-Text 64 58 'The three transition timers' 34 '#172332' $true
    Diagram-Text 67 108 '4 m cells  /  Rounded7x7  /  KeepOwnershipOnCellExit = true' 18 '#526072'
    Diagram-Text 222 153 'Camera direction: up' 19 '#138453' $true
    Diagram-Text 744 153 'Delay after a new field request' 20 '#253344' $true

    $diagramColors = @{Inner='#F04A55';Forward='#F49B32';Outer='#F4D54D';Inactive='#FFFFFF'}
    foreach ($cell in $diagramCells) {
        $x = 68+(3+[int]$cell.X)*83
        $y = 197+(3-[int]$cell.Z)*83
        $border = if($cell.Tier -eq 'Inactive'){'#CED5DE'}else{''}
        Diagram-Rect $x $y 76 76 $diagramColors[$cell.Tier] 6 $border
        if($cell.Tier -eq 'Inactive') {
            Diagram-Line ($x+24) ($y+38) ($x+52) ($y+38) '#C4CBD5' 2
        }
    }
    # The player is in the centre cell; positive Z is up on this diagram.
    $cx = 68+3*83+38; $cy = 197+3*83+38
    Diagram-Line $cx ($cy-16) $cx ($cy-106) '#FFFFFF' 12
    Diagram-Line $cx ($cy-16) $cx ($cy-106) '#158F59' 7
    Diagram-Line $cx ($cy-109) ($cx-15) ($cy-87) '#FFFFFF' 12
    Diagram-Line $cx ($cy-109) ($cx+15) ($cy-87) '#FFFFFF' 12
    Diagram-Line $cx ($cy-109) ($cx-15) ($cy-87) '#158F59' 7
    Diagram-Line $cx ($cy-109) ($cx+15) ($cy-87) '#158F59' 7
    $playerBrush = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#158F59'))
    $playerPen = New-Object Drawing.Pen([Drawing.Color]::White,4)
    try {
        $script:diagramGraphics.FillEllipse($playerBrush,($cx-21),($cy-21),42,42)
        $script:diagramGraphics.DrawEllipse($playerPen,($cx-21),($cy-21),42,42)
    } finally { $playerBrush.Dispose();$playerPen.Dispose() }
    [void]$script:diagramSvg.AppendLine("<circle cx=`"$cx`" cy=`"$cy`" r=`"21`" fill=`"#158F59`" stroke=`"white`" stroke-width=`"4`"/>")
    Diagram-Text ($cx-13) ($cy-8) 'YOU' 11 '#FFFFFF' $true

    Diagram-Legend 195 '#F04A55' 'INNER / RED' '0 s' 'Nearby cells and the first row ahead' 'InnerTransitionDelaySeconds'
    Diagram-Legend 355 '#F49B32' 'FORWARD / ORANGE' '0.2 s' 'One extra three-cell row ahead' 'ForwardTransitionDelaySeconds'
    Diagram-Legend 515 '#F4D54D' 'OUTER / YELLOW' '3 s' 'The remaining active outer cells' 'OuterTransitionDelaySeconds'
    Diagram-Rect 744 683 504 94 '#F5F7FA' 14
    Diagram-Rect 766 705 28 28 '#FFFFFF' 4 '#CED5DE'
    Diagram-Line 773 719 787 719 '#C4CBD5' 2
    Diagram-Text 807 696 'REAR CUTOUT' 18 '#526072' $true
    Diagram-Text 807 723 'No new ownership request' 18 '#253344'
    Diagram-Text 807 748 'Existing ownership is retained.' 15 '#526072'

    Diagram-Line 68 808 1248 808 '#DFE4EB' 1
    Diagram-Text 68 828 'Each tile is 4 x 4 metres. Timings are configurable.' 18
    Diagram-Text 68 858 'Loading, open inventories and active use can still delay a handoff.' 17 '#526072'
    Diagram-Text 68 885 'Straight-ahead example; angular overlap and turning prediction can extend this layout.' 16 '#526072'
    [void]$script:diagramSvg.AppendLine('</svg>')
    $diagramSvgPath = Join-Path $OutputDirectory 'grid-transition-timers.svg'
    $diagramPngPath = Join-Path $OutputDirectory 'grid-transition-timers.png'
    [IO.File]::WriteAllText($diagramSvgPath, $script:diagramSvg.ToString(), (New-Object Text.UTF8Encoding($false)))
    $diagramBitmap.Save($diagramPngPath,[Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{PNG=$diagramPngPath;SVG=$diagramSvgPath;Inner=6;Forward=3;Outer=17;Inactive=11}
} finally { $script:diagramGraphics.Dispose();$diagramBitmap.Dispose() }
