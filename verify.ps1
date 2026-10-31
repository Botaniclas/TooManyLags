param(
    [string]$ValheimDirectory='D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory='C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$FgnAssembly='C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\cache\VerdantsAscent-FiresGhettoNetworking\1.5.72\plugins\FiresGhettoNetworking\VAGhettoNetworking.dll',
    [string]$PupAssembly='C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\cache\PUP82-PUP_FPS\1.0.37\PUP_FPS.dll',
    [switch]$Release
)
$ErrorActionPreference='Stop'
$taskBaseline=Join-Path (Split-Path $PSScriptRoot) 'TooManyLags-1.0.86-source-handoff'
$taskPrior=Get-Content -LiteralPath (Join-Path $taskBaseline 'verification.json') -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath (Join-Path $taskBaseline 'bin/debug-system/TooManyLags.dll') -Algorithm SHA256).Hash -ne $taskPrior.debugDllSha256){throw 'Baseline DLL changed.'}
$taskSources=[ordered]@{};$taskChanges=@();$taskSourceRoot=Join-Path $PSScriptRoot 'src'
foreach($taskSource in Get-ChildItem -LiteralPath $taskSourceRoot -File -Recurse){
    $taskRelative=$taskSource.FullName.Substring($taskSourceRoot.Length+1)
    $taskHash=(Get-FileHash -LiteralPath $taskSource.FullName -Algorithm SHA256).Hash
    $taskSources[$taskRelative]=$taskHash
    $taskOld=Join-Path $taskBaseline ('src/'+$taskRelative)
    if(!(Test-Path -LiteralPath $taskOld)){$taskChanges+=$taskRelative;continue}
    $taskOldHash=(Get-FileHash -LiteralPath $taskOld -Algorithm SHA256).Hash
    if($taskPrior.sourceHashes.$taskRelative -ne $taskOldHash){throw 'Previously audited baseline changed.'}
    if($taskHash -ne $taskOldHash){$taskChanges+=$taskRelative}
}
$taskAllowedChanges=@(
    'ConfigTemplate.cs',
    'MobBridge\MobBridgeLatency.cs',
    'MobBridge\MobBridgeClockCadence.cs',
    'MobBridge\MobBridgeTiming.cs',
    'MobBridge\MobBridgeWarmup.cs',
    'FgnCombatOwnership.cs','FgnMobHandoff.cs',
    'FgnTargetBoundary.cs','FgnBossPolicy.cs','FgnTargetEvents.cs','PlayerSimulation\TargetEvents.cs','MobCollisionReadiness.cs',
    'OwnerDebugApi.cs','FgnSoloCombatTest.cs','MobFlightHandoff.cs','MobSourceHandoff.cs','MobHandoffVisual.cs','MobStatusState.cs','SoloNetworkLatency.cs','Plugin.cs',
    'MobObserverHandoff.cs','MobBridge\MobBridgeObserver.cs','MobBridge\MobBridgeObserverPacket.cs',
    'MobBridge\MobBridgeBody.cs','MobBridge\MobBridgeCheckpoint.cs','MobBridge\MobBridgeFieldCodec.cs',
    'MobBridge\MobBridgeNativeActor.cs','MobBridge\MobBridgeNativeWorld.cs','MobBridge\MobBridgeRecording.cs',
    'MobBridge\MobBridgeCaptureClock.cs',
    'MobBridge\MobBridgeCost.cs',
    'MobBridge\MobBridgeSkin.cs',
    'MobBridge\MobBridgeEffects.cs','MobBridge\MobBridgeEffectAttachment.cs',
    'MobBridge\MobBridgeAnimation.cs','MobBridge\MobBridgeAnimationHistory.cs',
    'MobBridge\MobBridgeProtocol.cs','MobBridge\MobBridgeRandom.cs','MobBridge\MobBridgeSeed.cs',
    'MobBridge\MobBridgeRateTracker.cs','MobBridge\MobBridgeFrame.cs',
    'MobBridge\MobBridgePoseHistory.cs',
    'MobBridge\MobBridgeAttackGate.cs',
    'MobBridge\MobBridgeTargetTimeline.cs',
    'MobBridge\MobBridgeSimulation.cs','MobBridge\MobBridgeTakeover.cs','MobBridge\MobBridgeTimeline.cs',
    'MobBridge\MobBridgeVisual.cs',
    'MobBridge\MobBridgeHit.cs','MobBridge\MobBridgeHitFilter.cs','MobBridge\MobBridgePlacement.cs',
    'MobBridge\MobBridgePresentationPoint.cs',
    'MobBridge\Native\MobBridgeSteeringContext.cs','MobBridge\Native\NativeAiFork.cs',
    'MobBridge\Native\NativeFlightTransition.cs','MobBridge\Native\NativeRandomLedger.cs',
    'MobBridge\Native\NativeSteeringPlan.cs','MobBridge\Native\NativeSteeringSandbox.cs',
    'MobBridge\Native\MobBridgeWeaponState.cs'
)
if(Compare-Object ($taskAllowedChanges|Sort-Object) @($taskChanges|Sort-Object)){throw 'Unexpected production scope.'}
& (Join-Path $PSScriptRoot 'generate-config.ps1') -BepInExDirectory $BepInExDirectory
if($Release){ & (Join-Path $PSScriptRoot 'build.ps1') -ValheimDirectory $ValheimDirectory -BepInExDirectory $BepInExDirectory -Release }
& (Join-Path $PSScriptRoot 'build.ps1') -ValheimDirectory $ValheimDirectory -BepInExDirectory $BepInExDirectory -DebugSystem
$taskDebugDll=Join-Path $PSScriptRoot 'bin/debug-system/TooManyLags.dll'
$taskDebugHash=(Get-FileHash -LiteralPath $taskDebugDll -Algorithm SHA256).Hash
$taskDll=if($Release){Join-Path $PSScriptRoot 'bin/TooManyLags.dll'}else{$taskDebugDll}
$taskDllHash=(Get-FileHash -LiteralPath $taskDll -Algorithm SHA256).Hash
if([Reflection.AssemblyName]::GetAssemblyName($taskDll).Version.ToString(3) -ne '2.0.0'){throw 'Assembly version mismatch.'}
$taskResults=@()
$taskSuites=@(
    @{test='test-build-policy.ps1';debug=!$Release},
    @{test='test-animation-passthrough.ps1';debug=$true},
    @{test='test-bridge-performance.ps1';debug=$true},
    @{test='test-bridge-timing.ps1';debug=$true},
    @{test='test-bridge-continuity.ps1';debug=$true},
    @{test='test-mob-combat-state.ps1';debug=!$Release},
    @{test='test-mob-status-state.ps1';debug=!$Release},
    @{test='test-mob-source.ps1';debug=!$Release},
    @{test='test-mob-observer.ps1';debug=!$Release},
    @{test='test-mob-bridge.ps1';debug=$true},
    @{test='inspect-mob-bridge-fields.ps1';debug=$true},
    @{test='test-debug-build.ps1';debug=$false},
    @{test='test-diagnostic-cost.ps1';debug=$false},
    @{test='test-combat.ps1';debug=$false},
    @{test='test-combat-mono.ps1';debug=$false},
    @{test='test-combat-readiness.ps1';debug=$false},
    @{test='test-combat.ps1';debug=$true},
    @{test='test-combat-mono.ps1';debug=$true},
    @{test='test-combat-readiness.ps1';debug=$true},
    @{test='test-solo-latency.ps1';debug=$true},
    @{test='test-mob-visual.ps1';debug=$false},
    @{test='test-flight-handoff.ps1';debug=$false},
    @{test='test-player-simulation.ps1';debug=$false},
    @{test='test-config-layout.ps1';debug=$false},
    @{test='test-camera-recovery.ps1';debug=$false},
    @{test='test-integration.ps1';debug=$false},
    @{test='test-integration.ps1';debug=$false;grid=$true},
    @{test='test-state-transfer.ps1';debug=$false}
)
if($Release){$taskSuites+=@{test='test-mob-bridge.ps1';debug=$false;bridgeRelease=$true}}

if(!$Release){
    $releaseOnly=@('test-debug-build.ps1','test-diagnostic-cost.ps1','test-combat.ps1','test-combat-mono.ps1','test-combat-readiness.ps1')
    $taskSuites=@($taskSuites|Where-Object { $_.debug -or $_.test -notin $releaseOnly })
    foreach($suite in $taskSuites){if($suite.test -in @('test-mob-visual.ps1','test-flight-handoff.ps1','test-state-transfer.ps1')){$suite.debug=$true}}
}
foreach($taskSuite in $taskSuites){
    $taskTest=$taskSuite.test
    Write-Output ('Checking '+$taskTest)
    $taskCommand=Get-Command (Join-Path $PSScriptRoot $taskTest);$taskArgs=@{}
    if($taskCommand.Parameters.ContainsKey('ValheimDirectory')){$taskArgs.ValheimDirectory=$ValheimDirectory}
    if($taskCommand.Parameters.ContainsKey('BepInExDirectory')){$taskArgs.BepInExDirectory=$BepInExDirectory}
    if($taskCommand.Parameters.ContainsKey('FgnAssembly')){$taskArgs.FgnAssembly=$FgnAssembly}
    if($taskCommand.Parameters.ContainsKey('PupAssembly')){$taskArgs.PupAssembly=$PupAssembly}
    if($taskCommand.Parameters.ContainsKey('DebugSystem')){$taskArgs.DebugSystem=[bool]$taskSuite.debug}
    if($taskSuite.bridgeRelease){$taskArgs.Release=$true}
    if($taskSuite.grid -and $taskCommand.Parameters.ContainsKey('GridProtocol')){$taskArgs.GridProtocol=$true}
    $taskLabel=$taskTest+$(if($taskSuite.debug){'-debug'}else{'-normal'})+$(if($taskSuite.grid){'-grid-protocol'}else{''})
    $taskLines=@(& $taskCommand @taskArgs | Tee-Object -FilePath (Join-Path $PSScriptRoot ('bin/'+$taskLabel+'.log')));$taskLines|ForEach-Object{Write-Output $_}
    $taskResults+=[ordered]@{test=$taskTest;debugSystem=[bool]$taskSuite.debug;gridProtocol=[bool]$taskSuite.grid;passed=$true;output=($taskLines -join [Environment]::NewLine)}
}
if((Get-FileHash -LiteralPath $taskDll -Algorithm SHA256).Hash -ne $taskDllHash){throw 'DLL changed during verification.'}
if((Get-FileHash -LiteralPath $taskDebugDll -Algorithm SHA256).Hash -ne $taskDebugHash){throw 'Debug DLL changed during verification.'}
foreach($taskItem in $taskSources.GetEnumerator()){
    if((Get-FileHash -LiteralPath (Join-Path $taskSourceRoot $taskItem.Key) -Algorithm SHA256).Hash -ne $taskItem.Value){throw 'Source changed during verification; rerun before packaging.'}
}
$taskArtifacts=[ordered]@{}
$taskArtifactFiles=@(Get-ChildItem -LiteralPath $PSScriptRoot -File|Where-Object {$_.Extension -in @('.md','.ps1','.json','.toml','.cfg') -and $_.Name -ne 'verification.json'})
$taskArtifactFiles+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -File -Recurse)
$taskArtifactFiles+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tools') -File -Recurse)
$taskArtifactFiles+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'docs/wiki') -File -Filter '*.md')
foreach($taskFile in $taskArtifactFiles){
    $taskRelative=$taskFile.FullName.Substring($PSScriptRoot.Length+1).Replace('\','/')
    $taskArtifacts[$taskRelative]=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash
}
[ordered]@{
    version='2.0.0';debugSystem=!$Release;releaseVerified=[bool]$Release;dllSha256=$taskDllHash;debugDllSha256=$taskDebugHash;validatedAt=(Get-Date).ToString('o');tests=$taskResults
    changedProductionSources=$taskChanges;sourceHashes=$taskSources;artifactHashes=$taskArtifacts
    unchangedProductionValidation=[ordered]@{version='1.0.86';dllSha256=$taskPrior.debugDllSha256;priorSuites=$taskPrior.tests.Count;sourceHashesVerified=$true;rerun=$false;scope='The tests list records fresh checks for the selected mode. Unchanged production files inherit the hash-verified .86 debug baseline. Explicit release verification additionally runs normal/debug omission and logging checks. Live Unity multiplayer remains untested.'}
    network=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bin/state-transfer-tests/verification.json') -Raw|ConvertFrom-Json)
    liveMultiplayerTested=$false;published=$false;installedProfilesChanged=$false
}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Encoding utf8
Write-Output ('Verified '+$(if($Release){'release and debug'}else{'debug development'})+' build 2.0.0: '+$taskDllHash)
