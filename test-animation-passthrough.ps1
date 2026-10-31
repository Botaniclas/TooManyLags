param()
$ErrorActionPreference='Stop'
$taskOutput=Join-Path $PSScriptRoot 'bin/animation-passthrough'
New-Item -ItemType Directory -Force -Path $taskOutput|Out-Null
$taskReference=Join-Path (Split-Path $PSScriptRoot) 'TooManyLags-2.0.0-low-latency'
$taskReceipt=Get-Content -LiteralPath (Join-Path $taskReference 'verification.json') -Raw|ConvertFrom-Json
$taskReferenceSource=Join-Path $taskReference 'src/MobBridge/MobBridgeAnimation.cs'
if((Get-FileHash -LiteralPath $taskReferenceSource).Hash -ne $taskReceipt.sourceHashes.'MobBridge\MobBridgeAnimation.cs'){throw 'Reference animation source changed.'}
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/MobBridge/AnimationPreparedRig.template.cs'))
$taskSources=@()
foreach($taskVariant in @(@{Name='ReferenceRig';Path=$taskReferenceSource},@{Name='OptimizedRig';Path=(Join-Path $PSScriptRoot 'src/MobBridge/MobBridgeAnimation.cs')})){
    $taskSource=[IO.File]::ReadAllText($taskVariant.Path)
    $taskStart=$taskSource.IndexOf('        internal void Prepare(')
    $taskEnd=$taskSource.IndexOf('        public void Dispose()',$taskStart)
    if($taskStart -lt 0 -or $taskEnd -lt 0){throw 'Production method extraction boundary missing.'}
    $taskMethods=$taskSource.Substring($taskStart,$taskEnd-$taskStart).Replace('internal void Prepare','public void Prepare').Replace('internal bool TryPose','public bool TryPose').Replace('internal void Apply','public void Apply').Replace('internal void Restore','public void Restore').Replace('History.Sample(time, positions, rotations, scales);','CountSample(time);')
    $taskPath=Join-Path $taskOutput ($taskVariant.Name+'.cs')
    [IO.File]::WriteAllText($taskPath,$taskTemplate.Replace('@@NAME@@',$taskVariant.Name).Replace('@@METHODS@@',$taskMethods))
    $taskSources+=$taskPath
}
$taskSources+=@('tests/MobVisualFixtures.cs','tests/MobBridge/AnimationPassthroughTests.cs','src/MobBridge/MobBridgeAnimationHistory.cs','src/MobBridge/MobBridgeObserverPacket.cs','src/MobBridge/MobBridgeTimeline.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
$taskExe=Join-Path $taskOutput 'AnimationPassthroughTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe') /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 /define:TML_MOB_BRIDGE ('/out:'+$taskExe) $taskSources
if($LASTEXITCODE -ne 0){throw 'Production animation differential fixture failed compilation.'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Production animation differential fixture failed.'}
