param()
$ErrorActionPreference='Stop'
$testOutput=Join-Path $PSScriptRoot 'bin/bridge-continuity'
New-Item -ItemType Directory -Force -Path $testOutput|Out-Null
$visualText=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src/MobBridge/MobBridgeVisual.cs') -Raw
$advanceStart=$visualText.IndexOf('        private static void Advance(Track item, double now)')
if($advanceStart -lt 0){throw 'Actual Advance not found.'}
$advanceOpen=$visualText.IndexOf('{',$advanceStart);$advanceDepth=1;$advanceEnd=$advanceOpen+1
while($advanceEnd -lt $visualText.Length -and $advanceDepth -gt 0){
    if($visualText[$advanceEnd] -eq '{'){$advanceDepth++}
    elseif($visualText[$advanceEnd] -eq '}'){$advanceDepth--}
    $advanceEnd++
}
$advanceTemplate=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tests/MobBridge/BridgeAdvance.template.cs') -Raw
$advanceFile=Join-Path $testOutput 'ActualAdvance.cs'
$advanceTemplate.Replace('@@ADVANCE@@',$visualText.Substring($advanceStart,$advanceEnd-$advanceStart))|
    Set-Content -LiteralPath $advanceFile -Encoding utf8
$testSources=@('src/MobBridge/MobBridgeTimeline.cs','src/MobBridge/MobBridgeTiming.cs',
    'src/MobBridge/MobBridgeClockCadence.cs','src/MobBridge/MobBridgeTakeover.cs','src/MobBridge/MobBridgeRecording.cs',
    'src/MobBridge/MobBridgePlacement.cs','tests/MobVisualFixtures.cs',
    'tests/MobBridge/BridgePlacementFixtures.cs','tests/MobBridge/BridgeContinuityTests.cs')|
    ForEach-Object{Join-Path $PSScriptRoot $_}
$testSources+=$advanceFile
$testExe=Join-Path $testOutput 'BridgeContinuityTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe') /nologo /target:exe /define:TML_MOB_BRIDGE /optimize+ /warnaserror+ /langversion:5 ('/out:'+$testExe) $testSources
if($LASTEXITCODE -ne 0){throw 'Bridge continuity fixtures failed compilation.'}
& $testExe
if($LASTEXITCODE -ne 0){throw 'Bridge continuity fixtures failed.'}
