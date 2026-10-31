param(
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [switch]$DebugSystem = $true,
    [switch]$Release
)
$ErrorActionPreference = 'Stop'
if ($Release -and $PSBoundParameters.ContainsKey('DebugSystem')) { throw 'Choose -Release or -DebugSystem, not both.' }
if (!$Release -and !$DebugSystem) { throw 'Use -Release explicitly to build without the debug system.' }
$withDebug = !$Release

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$managed = Join-Path $ValheimDirectory 'valheim_Data\Managed'
$output = Join-Path $PSScriptRoot $(if ($withDebug) {'bin/debug-system'} else {'bin'})
$defines = if ($withDebug) {'TML_RELEASE;TML_MOB_BRIDGE;TML_DEBUG_SYSTEM;TML_BRIDGE_DIAGNOSTICS'} else {'TML_RELEASE;TML_MOB_BRIDGE'}
New-Item -ItemType Directory -Force -Path $output | Out-Null
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll', 'UnityEngine.dll',
    'UnityEngine.CoreModule.dll', 'UnityEngine.PhysicsModule.dll', 'UnityEngine.AnimationModule.dll',
    'UnityEngine.TerrainModule.dll', 'UnityEngine.TerrainPhysicsModule.dll', 'UnityEngine.ParticleSystemModule.dll',
    'UnityEngine.AudioModule.dll', 'assembly_valheim.dll', 'assembly_utils.dll',
    'SoftReferenceableAssets.dll') | ForEach-Object { Join-Path $managed $_ }
$references += Join-Path $BepInExDirectory 'core\BepInEx.dll'
$references += Join-Path $BepInExDirectory 'core\0Harmony.dll'
foreach ($path in @($compiler) + $references) { if (!(Test-Path -LiteralPath $path)) { throw "Missing build input: $path" } }
$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/target:library', '/optimize+', '/warnaserror+', '/langversion:5', ('/define:' + $defines),
    ('/out:' + (Join-Path $output 'TooManyLags.dll')))
$arguments += $references | ForEach-Object { '/reference:' + $_ }
$arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -Recurse | Select-Object -ExpandProperty FullName
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Get-Item -LiteralPath (Join-Path $output 'TooManyLags.dll') | Select-Object FullName,Length



[ordered]@{debugSystem=[bool]$withDebug;defines=$defines;dllSha256=(Get-FileHash -LiteralPath (Join-Path $output 'TooManyLags.dll') -Algorithm SHA256).Hash}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'build-mode.json') -Encoding utf8
