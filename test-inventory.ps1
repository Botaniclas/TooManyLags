param(
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim'
)
$ErrorActionPreference = 'Stop'
$inventoryCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$inventorySource = Join-Path $PSScriptRoot 'src\NativeInventoryHandoff.cs'
$inventoryTests = Join-Path $PSScriptRoot 'tests\NativeInventoryTests.cs'
$inventoryOutput = Join-Path $PSScriptRoot 'bin\inventory-tests'
New-Item -ItemType Directory -Force -Path $inventoryOutput | Out-Null
$inventoryExe = Join-Path $inventoryOutput 'NativeInventoryTests.exe'
& $inventoryCompiler '/nologo' '/optimize+' '/langversion:5' '/warnaserror+' ('/out:' + $inventoryExe) $inventorySource $inventoryTests
if ($LASTEXITCODE -ne 0) { throw 'Native inventory bridge fixture compilation failed.' }
& $inventoryExe
if ($LASTEXITCODE -ne 0) { throw 'Native inventory bridge fixture tests failed.' }

# Separately prove this production slice compiles against the real game APIs.
$inventoryManaged = Join-Path $ValheimDirectory 'valheim_Data\Managed'
$inventoryReferences = @('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','UnityEngine.dll',
    'UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','assembly_valheim.dll','assembly_utils.dll',
    'SoftReferenceableAssets.dll') | ForEach-Object { Join-Path $inventoryManaged $_ }
$inventoryArguments = @('/nologo','/noconfig','/nostdlib+','/target:library','/optimize+','/warnaserror+','/langversion:5',
    ('/out:' + (Join-Path $inventoryOutput 'NativeInventoryHandoff-contract.dll')))
$inventoryArguments += $inventoryReferences | ForEach-Object { '/reference:' + $_ }
$inventoryArguments += $inventorySource
& $inventoryCompiler @inventoryArguments
if ($LASTEXITCODE -ne 0) { throw 'Actual Valheim inventory API compilation failed.' }
Write-Output 'PASS: Production inventory bridge compiles against actual installed Valheim APIs.'
