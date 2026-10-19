param(
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim'
)
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskCore = Join-Path $BepInExDirectory 'core'
$taskOutput = Join-Path $PSScriptRoot 'bin/communitypatch-tests'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
foreach ($taskDependency in @('BepInEx.dll','0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) {
    Copy-Item -LiteralPath (Join-Path $taskCore $taskDependency) -Destination $taskOutput -Force
}
$taskSources = @('src/CommunityPatchSceneCompatibility.cs','src/FgnSessionSettings.cs','src/FgnSimulationStartup.cs','tests/CommunityPatchSceneTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$taskExe = Join-Path $taskOutput 'CommunityPatchSceneTests.exe'
& $taskCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/reference:'+(Join-Path $taskCore 'BepInEx.dll')) ('/out:'+$taskExe) $taskSources
if ($LASTEXITCODE -ne 0) { throw 'CommunityPatch integration fixture compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'CommunityPatch integration fixtures failed.' }
$taskMonoHost = Join-Path $taskOutput 'CommunityPatchMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ /langversion:5 ('/out:'+$taskMonoHost) (Join-Path $PSScriptRoot 'tests/CombatMonoFixtureHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'CommunityPatch Mono host compilation failed.' }
& $taskMonoHost $taskExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc')
if ($LASTEXITCODE -ne 0) { throw 'CommunityPatch integration fixtures failed on Valheim Mono.' }
$taskCache = Join-Path (Split-Path (Split-Path (Split-Path $BepInExDirectory))) 'cache'
$taskVcp = Join-Path $taskCache 'MidnightMods-ValheimCommunityPatch/0.37.0/plugins/ValheimCommunityPatch.dll'
if (-not (Test-Path -LiteralPath $taskVcp)) { $taskVcp = Join-Path $taskCache 'MidnightMods-ValheimCommunityPatch/0.37.0/ValheimCommunityPatch.dll' }
$taskFgn = Join-Path $taskCache 'VerdantsAscent-FiresGhettoNetworking/1.5.65/plugins/FiresGhettoNetworking/VAGhettoNetworking.dll'
$taskAudit = Join-Path $taskOutput 'CommunityPatchNativeAudit.exe'
& $taskCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/reference:'+(Join-Path $taskCore 'Mono.Cecil.dll')) ('/out:'+$taskAudit) (Join-Path $PSScriptRoot 'tests/CommunityPatchNativeAudit.cs')
if ($LASTEXITCODE -ne 0) { throw 'CommunityPatch native contract audit compilation failed.' }
& $taskAudit $taskVcp $taskFgn (Join-Path $PSScriptRoot 'bin/TooManyLags.dll') $taskCore
if ($LASTEXITCODE -ne 0) { throw 'CommunityPatch native contracts failed.' }
