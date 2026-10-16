param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$output=Join-Path $PSScriptRoot 'bin/player-simulation-tests'
$core=Join-Path $BepInExDirectory 'core'
New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach($dep in @('0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) { Copy-Item -LiteralPath (Join-Path $core $dep) -Destination $output -Force }
foreach($suite in @('GridTests','DirectionalGridTests','FieldCacheTests','RegionGateTests','StandalonePolicyTests')) {
    $sources=@('src/PlayerSimulation/AuthorityGrid.cs','tests/PlayerSimulation/IntervalConfigFixture.cs','src/PlayerSimulation/RegionContentionGate.cs','src/PlayerSimulation/HandoffTransaction.cs',('tests/PlayerSimulation/'+$suite+'.cs')) | ForEach-Object { Join-Path $PSScriptRoot $_ }
    $exe=Join-Path $output ($suite+'.exe')
    & $compiler /nologo /target:exe /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 ('/out:'+$exe) $sources
    if($LASTEXITCODE -ne 0){throw ('Player-simulation policy compilation failed: '+$suite)}
    & $exe
    if($LASTEXITCODE -ne 0){throw ('Player-simulation policy test failed: '+$suite)}
}
foreach($suite in @('HandoffRuntimeTests','NetworkModeTests')) {
    $itemSources=@('src/LooseItemGuard.cs','src/FieldReader.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
    $sources=@('src/PlayerSimulation/LocalAgent.cs','src/PlayerSimulation/Handoffs.cs','src/PlayerSimulation/NativeOwnership.cs','src/PlayerSimulation/HandoffTransaction.cs','src/PlayerSimulation/RequestRouter.cs','src/PlayerSimulation/FieldReader.cs','src/PlayerSimulation/FriendlyCreatures.cs','tests/PlayerSimulation/HandoffFixtures.cs',('tests/PlayerSimulation/'+$suite+'.cs')) | ForEach-Object { Join-Path $PSScriptRoot $_ }
    $sources += $itemSources
    $define='/define:HANDOFF_AUDIT'
    if($suite -eq 'NetworkModeTests') {
        $define='/define:NETWORK_AUDIT'
        $sources += @('src/PlayerSimulation/Network.cs','src/PlayerSimulation/NativeLatency.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    }
    $exe=Join-Path $output ($suite+'.exe')
    & $compiler /nologo /target:exe /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 $define ('/reference:'+(Join-Path $core '0Harmony.dll')) ('/out:'+$exe) $sources
    if($LASTEXITCODE -ne 0){throw ('Player-simulation runtime compilation failed: '+$suite)}
    & $exe
    if($LASTEXITCODE -ne 0){throw ('Player-simulation runtime test failed: '+$suite)}
}
