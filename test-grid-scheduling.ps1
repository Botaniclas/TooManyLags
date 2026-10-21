$ErrorActionPreference = 'Stop'
$gridCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$gridOutput = Join-Path $PSScriptRoot 'bin\grid-scheduling-tests'
New-Item -ItemType Directory -Force -Path $gridOutput | Out-Null
$sendExecutable = Join-Path $gridOutput 'HandoffSendPriorityTests.exe'
& $gridCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/out:' + $sendExecutable) (Join-Path $PSScriptRoot 'src\HandoffSendPriority.cs') (Join-Path $PSScriptRoot 'tests\HandoffSendPriorityTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Send priority compilation failed.' }
& $sendExecutable
if ($LASTEXITCODE -ne 0) { throw 'Send priority tests failed.' }
foreach ($gridMode in @('ServerSimulation', 'PlayerSimulation')) {
    $gridSource = 'src\AuthorityGrid.cs'; $gridDefine = '/define:SERVER_SIMULATION'
    if ($gridMode -eq 'PlayerSimulation') { $gridSource = 'src\PlayerSimulation\AuthorityGrid.cs'; $gridDefine = '/define:PLAYER_SIMULATION' }
    $gridExecutable = Join-Path $gridOutput ($gridMode + 'SchedulingTests.exe')
    & $gridCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 $gridDefine ('/out:' + $gridExecutable) (Join-Path $PSScriptRoot $gridSource) (Join-Path $PSScriptRoot 'tests\LegacyGridSchedulingTests.cs')
    if ($LASTEXITCODE -ne 0) { throw ('Grid scheduling compilation failed: ' + $gridMode) }
    & $gridExecutable
    if ($LASTEXITCODE -ne 0) { throw ('Grid scheduling test failed: ' + $gridMode) }
    $priorityExecutable = Join-Path $gridOutput ($gridMode + 'PriorityTests.exe')
    & $gridCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 $gridDefine ('/out:' + $priorityExecutable) (Join-Path $PSScriptRoot $gridSource) (Join-Path $PSScriptRoot 'tests\GridPriorityTests.cs')
    if ($LASTEXITCODE -ne 0) { throw ('Grid priority compilation failed: ' + $gridMode) }
    & $priorityExecutable
    if ($LASTEXITCODE -ne 0) { throw ('Grid priority test failed: ' + $gridMode) }
}
