param()
$ErrorActionPreference='Stop'
$taskOutput=Join-Path $PSScriptRoot 'bin/bridge-performance'
New-Item -ItemType Directory -Force -Path $taskOutput|Out-Null
$taskReference=Join-Path (Split-Path $PSScriptRoot) 'TooManyLags-2.0.0-performance'
$taskReceipt=Get-Content -LiteralPath (Join-Path $taskReference 'verification.json') -Raw|ConvertFrom-Json
function Extract-Block([string]$source,[string]$marker) {
    $start=$source.IndexOf($marker)
    if($start -lt 0){throw ('Missing extraction marker: '+$marker)}
    $open=$source.IndexOf('{',$start);$depth=0
    for($index=$open;$index -lt $source.Length;$index++){
        if($source[$index] -eq '{'){$depth++}
        elseif($source[$index] -eq '}'){$depth--;if($depth -eq 0){return $source.Substring($start,$index-$start+1)}}
    }
    throw 'Unclosed production block.'
}
$taskSources=@()
foreach($taskVariant in @(@{Name='Reference';Root=$taskReference},@{Name='Optimized';Root=$PSScriptRoot})){
    $taskParts=@{}
    foreach($taskFile in @('MobBridgeAnimation.cs','MobBridgeEffects.cs')){
        $taskRelative='MobBridge\'+$taskFile
        $taskFilePath=Join-Path $taskVariant.Root ('src/'+$taskRelative)
        if($taskVariant.Name -eq 'Reference' -and (Get-FileHash -LiteralPath $taskFilePath).Hash -ne $taskReceipt.sourceHashes.$taskRelative){throw 'Verified reference changed.'}
        $taskParts[$taskFile]=[IO.File]::ReadAllText($taskFilePath)
    }
    $taskRig=Extract-Block $taskParts['MobBridgeAnimation.cs'] '        private static string[] RigPaths(Transform[] bones)'
    $taskScope=Extract-Block $taskParts['MobBridgeEffects.cs'] '        private sealed class NativeFxScope'
    # Add allocation counters only at the single construction site; body logic
    # and all callbacks remain actual production source in both variants.
    $taskScope=$taskScope.Replace('new NativeFxScope()', 'Allocated(new NativeFxScope())')
    $taskBefore=Extract-Block $taskParts['MobBridgeEffects.cs'] '        private static void BeforeContinuous('
    $taskBefore=$taskBefore.Replace('new NativeFxScope()', 'Allocated(new NativeFxScope())')
    $taskAfter=Extract-Block $taskParts['MobBridgeEffects.cs'] '        private static Exception AfterContinuous('
    $taskClear=if($taskVariant.Name -eq 'Optimized'){'NativeFxScope.Clear();'}else{''}
    $taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/MobBridge/PerformanceMethods.template.cs'))
    $taskText=$taskTemplate.Replace('@@NAME@@',$taskVariant.Name).Replace('@@RIG@@',$taskRig).Replace('@@SCOPE@@',$taskScope).Replace('@@BEFORE@@',$taskBefore).Replace('@@AFTER@@',$taskAfter).Replace('@@CLEAR@@',$taskClear)
    $taskPath=Join-Path $taskOutput ($taskVariant.Name+'Performance.cs')
    [IO.File]::WriteAllText($taskPath,$taskText);$taskSources+=$taskPath
}
$taskSources+=Join-Path $PSScriptRoot 'tests/MobBridge/PerformanceEquivalenceTests.cs'
$taskPublication=Extract-Block ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/MobBridge/MobBridgeProtocol.cs'))) '        private static void PublishPhysicsClock('
$taskPublicationTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/MobBridge/ClockPublication.template.cs'))
$taskPublicationPath=Join-Path $taskOutput 'ClockPublication.cs'
[IO.File]::WriteAllText($taskPublicationPath,$taskPublicationTemplate.Replace('@@PUBLICATION@@',$taskPublication))
$taskSources+=$taskPublicationPath
$taskSources+=@('src/MobBridge/MobBridgeClockCadence.cs','src/MobBridge/MobBridgeTimeline.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
$taskExe=Join-Path $taskOutput 'PerformanceEquivalenceTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe') /nologo /target:exe /define:TML_MOB_BRIDGE /optimize+ /warnaserror+ /langversion:5 ('/out:'+$taskExe) $taskSources
if($LASTEXITCODE -ne 0){throw 'Performance differential fixture failed compilation.'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Performance differential fixture failed.'}
