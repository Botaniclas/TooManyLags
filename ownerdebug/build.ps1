param(
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx'
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$managed = Join-Path $ValheimDirectory 'valheim_Data\Managed'
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll', 'UnityEngine.dll',
    'UnityEngine.CoreModule.dll', 'UnityEngine.PhysicsModule.dll', 'UnityEngine.IMGUIModule.dll', 'UnityEngine.InputLegacyModule.dll', 'assembly_valheim.dll', 'assembly_utils.dll',
    'SoftReferenceableAssets.dll', 'Splatform.dll') | ForEach-Object { Join-Path $managed $_ }
$references += Join-Path $BepInExDirectory 'core\BepInEx.dll'
$references += Join-Path $BepInExDirectory 'core\0Harmony.dll'
foreach ($path in @($compiler) + $references) { if (!(Test-Path -LiteralPath $path)) { throw "Missing build input: $path" } }
$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/target:library', '/optimize+', '/warnaserror+', '/langversion:5',
    ('/out:' + (Join-Path $output 'BotanicOwnerDebug.dll')))
$arguments += $references | ForEach-Object { '/reference:' + $_ }
$arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Get-Item -LiteralPath (Join-Path $output 'BotanicOwnerDebug.dll') | Select-Object FullName,Length
