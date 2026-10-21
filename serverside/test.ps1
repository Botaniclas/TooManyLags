param(
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$ForkDll = (Join-Path (Split-Path $PSScriptRoot) 'ServersideSimulation-reference/bin/SarkasticGG_Dedicated_Simulation-1.18.0.dll')
)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -ValheimDirectory $ValheimDirectory -BepInExDirectory $BepInExDirectory | Out-Host
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$core=Join-Path $BepInExDirectory 'core'
$out=Join-Path $PSScriptRoot 'bin/verification-tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
foreach ($dependency in @('0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) {Copy-Item -LiteralPath (Join-Path $core $dependency) -Destination $out -Force}
$results=[ordered]@{}
foreach ($suite in @('GridPriorityBatchTests','GridBatchEquivalence','GridTests','FieldCacheTests','DirectionalGridTests','RuntimeTests','ForkRuntimeTests','HandoffTests','ServerGridTests','DynamicObjectTests','AdaptiveRefillTests','NativeInventoryTests','InventoryGridTests','ShipGridTests','CartGridTests','MobGridTests','RuntimeStatusTests','SocketLatencyTests')) {
    $inputs=@('src/AuthorityGrid.cs',('tests/'+$suite+'.cs'))
    $extra=@()
    if ($suite -in @('RuntimeTests','ForkRuntimeTests','HandoffTests','ServerGridTests','DynamicObjectTests','AdaptiveRefillTests','InventoryGridTests','ShipGridTests','CartGridTests','MobGridTests','RuntimeStatusTests','SocketLatencyTests')) {
        $inputs+=@('src/Settings.cs','src/Catalog.cs','src/Controller.cs','src/DynamicObjects.cs','src/RefillProtection.cs','src/Handoffs.cs','src/DueQueue.cs','src/NativeHooks.cs','src/NativeFlow.cs','src/NativeInventoryHandoff.cs','src/InventoryOwnership.cs','src/ShipOwnership.cs','src/CartOwnership.cs','src/MobOwnership.cs','src/MobAttackForecast.cs','src/MobOwnershipTest.cs','src/RuntimeStatus.cs','src/SocketLatency.cs','tests/RuntimeFixtures.cs','tests/SocketFixtures.cs')
        $extra+=('/reference:'+(Join-Path $core '0Harmony.dll'))
    }
    if ($suite -eq 'NativeInventoryTests') {$inputs=@('src/NativeInventoryHandoff.cs','tests/NativeInventoryTests.cs')}
    if ($suite -in @('ForkRuntimeTests','ShipGridTests')) {$inputs+=@('src/ForkBridge.cs','tests/ForkFixture.cs')}
    $sources=@($inputs | ForEach-Object {Join-Path $PSScriptRoot $_})
    $exe=Join-Path $out ($suite+'.exe')
    & $compiler /nologo /target:exe /warnaserror+ /optimize+ /langversion:5 ('/out:'+$exe) $extra $sources
    if ($LASTEXITCODE -ne 0) {throw ('Compile failed: '+$suite)}
    $result=@(& $exe 2>&1)
    $status=$LASTEXITCODE
    $result | ForEach-Object {Write-Host $_}
    if ($status -ne 0) {throw ('Failed: '+$suite)}
    $results[$suite]=$result -join [Environment]::NewLine
}
$audit=Join-Path $out 'NativeAudit.exe'
& $compiler /nologo /target:exe /warnaserror+ /optimize+ /langversion:5 ('/out:'+$audit) ('/reference:'+(Join-Path $core 'Mono.Cecil.dll')) (Join-Path $PSScriptRoot 'tests/NativeAudit.cs')
if ($LASTEXITCODE -ne 0) {throw 'Native audit compilation failed'}
$game=Join-Path $ValheimDirectory 'valheim_Data/Managed/assembly_valheim.dll'
$dll=Join-Path $PSScriptRoot 'bin/TooManyLagsServerSide.dll'
$result=@(& $audit $game $ForkDll $dll 2>&1)
$status=$LASTEXITCODE
$result | ForEach-Object {Write-Host $_}
if ($status -ne 0) {throw 'Native/released-fork audit failed'}
$results['NativeAudit']=$result -join [Environment]::NewLine
$validationExe=Join-Path $out 'NativeValidationProbe.exe'
& $compiler /nologo /target:exe /warnaserror+ /optimize+ /langversion:5 ('/out:'+$validationExe) ('/reference:'+(Join-Path $core '0Harmony.dll')) (Join-Path $PSScriptRoot 'tests/NativeValidationProbe.cs')
if ($LASTEXITCODE -ne 0) {throw 'Native startup probe compilation failed'}
$hostExe=Join-Path $out 'MonoFixtureHost.exe'
& $compiler /nologo /target:exe /platform:x64 /warnaserror+ /optimize+ /langversion:5 ('/out:'+$hostExe) (Join-Path $PSScriptRoot 'tests/MonoFixtureHost.cs')
if ($LASTEXITCODE -ne 0) {throw 'Mono host compilation failed'}
$result=@(& $hostExe $validationExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') $dll (Join-Path $ValheimDirectory 'valheim_Data/Managed') $core $ForkDll 2>&1)
$status=$LASTEXITCODE
$result | ForEach-Object {Write-Host $_}
if ($status -ne 0) {throw 'Native startup contract probe failed on Valheim Mono'}
$results['NativeStartup']=$result -join [Environment]::NewLine
foreach ($suite in @('RuntimeTests','ForkRuntimeTests','HandoffTests','ServerGridTests','DynamicObjectTests','AdaptiveRefillTests','NativeInventoryTests','InventoryGridTests','ShipGridTests','CartGridTests','MobGridTests','RuntimeStatusTests','SocketLatencyTests')) {
    $result=@(& $hostExe (Join-Path $out ($suite+'.exe')) (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') 2>&1)
    $status=$LASTEXITCODE
    $result | ForEach-Object {Write-Host $_}
    if ($status -ne 0) {throw ('Valheim Mono failed: '+$suite)}
    $results[$suite+'-Mono']=$result -join [Environment]::NewLine
}
$result=@(& (Join-Path $PSScriptRoot 'generate-config.ps1') -BepInExDirectory $BepInExDirectory)
$result | ForEach-Object {Write-Host $_}
$results['ProductionConfig']=$result -join [Environment]::NewLine
$hashes=[ordered]@{}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src'),(Join-Path $PSScriptRoot 'tests') -File -Recurse) {$hashes[$file.FullName.Substring($PSScriptRoot.Length+1)]=(Get-FileHash $file.FullName).Hash}
[ordered]@{dllSha256=(Get-FileHash $dll).Hash;gameSha256=(Get-FileHash $game).Hash;forkSha256=(Get-FileHash $ForkDll).Hash;tests=$results;sourceHashes=$hashes;liveMultiplayerTested=$false;testedAtUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $PSScriptRoot 'verification.json') -Encoding UTF8
