param(
    [string]$ValheimDirectory='D:/Steam-Bibliotheksordner/steamapps/common/Valheim',
    [string]$BepInExDirectory='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/profiles/Client/BepInEx'
)
$ErrorActionPreference='Stop'
$workspace='C:/Users/Niclas/Documents/Codex/2026-09-19/ic'
$out=Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
function Compile-Audit([string[]]$Arguments) {
    & $compiler @Arguments
    if ($LASTEXITCODE -ne 0) {throw 'Research fixture compilation failed.'}
}
function Run-Audit([string]$Name,[string]$Executable,[string[]]$Arguments) {
    $result=@(& $Executable @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {throw ($Name+' failed: '+($result -join [Environment]::NewLine))}
    $result | Set-Content -LiteralPath (Join-Path $PSScriptRoot ($Name+'.txt')) -Encoding UTF8
    $result | ForEach-Object {Write-Output $_}
}
$model=Join-Path $out 'SceneListCacheModelAudit.exe'
Compile-Audit @('/nologo','/target:exe','/optimize+','/warnaserror+','/langversion:5',('/out:'+$model),(Join-Path $PSScriptRoot 'SceneListCacheModelAudit.cs'))
$hostExe=Join-Path $out 'MonoFixtureHost.exe'
Compile-Audit @('/nologo','/target:exe','/platform:x64','/warnaserror+',('/out:'+$hostExe),(Join-Path $workspace 'work/TooManyLagsDedicatedAddon/tests/MonoFixtureHost.cs'))
$mono=@((Join-Path $ValheimDirectory 'valheim_Data/Managed'),(Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime'),(Join-Path $ValheimDirectory 'MonoBleedingEdge/etc'))
Run-Audit 'scene-cache-model-dotnet' $model @()
Run-Audit 'scene-cache-model-mono' $hostExe (@($model)+$mono)

# Baseline guard verification: these tests do not contain or integrate the model cache.
$core=Join-Path $BepInExDirectory 'core'
Copy-Item -LiteralPath (Join-Path $core '0Harmony.dll') -Destination $out -Force
Get-ChildItem -LiteralPath $core -Filter 'Mono*.dll' | Copy-Item -Destination $out -Force
$fixtureRoot=Join-Path $workspace 'work/TooManyLags/tests/PupCompatibility'
$foreign=Join-Path $out 'PupForeignFixtures.dll'
Compile-Audit @('/nologo','/target:library','/optimize+','/warnaserror+','/langversion:5',('/out:'+$foreign),(Join-Path $fixtureRoot 'RuntimeFixtures.cs'),(Join-Path $fixtureRoot 'ModernPupFixtures.cs'))
$sources=@(Get-ChildItem -LiteralPath (Join-Path $workspace 'work/TooManyLags/src/PupCompatibility') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources+=@((Join-Path $fixtureRoot 'RuntimeTests.cs'),(Join-Path $fixtureRoot 'ModuleTests.cs'))
$baseline=Join-Path $out 'PupCompatibilityBaseline.exe'
Compile-Audit (@('/nologo','/define:TESTING','/optimize+','/warnaserror+','/langversion:5','/main:PupCompatibilityRuntimeTests',('/reference:'+(Join-Path $out '0Harmony.dll')),('/reference:'+$foreign),('/out:'+$baseline))+$sources)
Run-Audit 'scene-cache-pup-baseline-dotnet' $baseline @()
Run-Audit 'scene-cache-pup-baseline-mono' $hostExe (@($baseline)+$mono)
& (Join-Path $PSScriptRoot 'SceneCacheContractAudit.ps1')
$contracts=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'scene-cache-contracts.json') -Raw | ConvertFrom-Json
$files=@((Join-Path $PSScriptRoot 'SceneListCacheModelAudit.cs'),(Join-Path $PSScriptRoot 'SceneCacheContractAudit.ps1'),$PSCommandPath)+$sources+@((Join-Path $fixtureRoot 'RuntimeFixtures.cs'),(Join-Path $fixtureRoot 'ModernPupFixtures.cs'))
[ordered]@{
    scope='Research model plus existing guard baseline and native/reference contracts. No cache integrated into production, no Unity/network/world test, no timing measurement.'
    sourceHashes=@($files | ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash}})
    nativeContracts=$contracts
    results=@('scene-cache-model-dotnet','scene-cache-model-mono','scene-cache-pup-baseline-dotnet','scene-cache-pup-baseline-mono') | ForEach-Object {[ordered]@{name=$_;output=(Get-Content -LiteralPath (Join-Path $PSScriptRoot ($_+'.txt')) -Raw)}}
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'scene-cache-verification.json') -Encoding UTF8
