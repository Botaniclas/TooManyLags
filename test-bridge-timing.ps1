param()
$ErrorActionPreference='Stop'
$taskOutput=Join-Path $PSScriptRoot 'bin/bridge-timing'
New-Item -ItemType Directory -Force -Path $taskOutput|Out-Null
function Extract-TimingBlock([string]$source,[string]$marker){
    $start=$source.IndexOf($marker);if($start -lt 0){throw ('Missing timing extraction: '+$marker)}
    $open=$source.IndexOf('{',$start);$depth=0
    for($i=$open;$i -lt $source.Length;$i++){
        if($source[$i] -eq '{'){$depth++}elseif($source[$i] -eq '}'){$depth--;if($depth -eq 0){return $source.Substring($start,$i-$start+1)}}
    };throw 'Unclosed timing block.'
}
$taskProtocol=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/MobBridge/MobBridgeProtocol.cs'))
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests/MobBridge/TimingCache.template.cs'))
foreach($taskItem in @(@{Token='@@PEER@@';Marker='        private sealed class PeerRate'},
    @{Token='@@LOCAL@@';Marker='        private static PeerRate LocalTiming('},
    @{Token='@@FOR@@';Marker='        private static PeerRate TimingFor('},
    @{Token='@@POLL@@';Marker='        internal static void PollRate('},
    @{Token='@@READ@@';Marker='        private static void ReceiveRate('},
    @{Token='@@FRAME@@';Marker='        internal static float FrameSeconds'})){
    $taskTemplate=$taskTemplate.Replace($taskItem.Token,(Extract-TimingBlock $taskProtocol $taskItem.Marker))
}
$taskGenerated=Join-Path $taskOutput 'TimingCache.cs';[IO.File]::WriteAllText($taskGenerated,$taskTemplate)
$taskSources=@('src/MobBridge/MobBridgeTiming.cs','src/MobBridge/MobBridgeTimeline.cs',
    'src/MobBridge/MobBridgeClockCadence.cs','src/MobBridge/MobBridgeCaptureClock.cs',
    'src/MobBridge/MobBridgeCheckpoint.cs','tests/MobBridge/BridgeTimingFixtures.cs',
    'tests/MobBridge/BridgeTimingTests.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
$taskSources+=$taskGenerated
$taskExe=Join-Path $taskOutput 'BridgeTimingTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe') /nologo /target:exe /define:TML_MOB_BRIDGE /optimize+ /warnaserror+ /langversion:5 ('/out:'+$taskExe) $taskSources
if($LASTEXITCODE -ne 0){throw 'Bridge timing fixtures failed compilation.'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Bridge timing fixtures failed.'}
