param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskOutput = Join-Path $PSScriptRoot 'bin/config-console-tests'
$taskCore = Join-Path $BepInExDirectory 'core'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
foreach ($dep in @('BepInEx.dll','0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) {
    Copy-Item -LiteralPath (Join-Path $taskCore $dep) -Destination $taskOutput -Force
}
$taskExe = Join-Path $taskOutput 'ConfigConsoleTests.exe'
$taskSources = @('src/ConfigCommands.cs','src/ConfigConsole.cs','src/ServerSession.cs','tests/ConfigConsoleTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore 'BepInEx.dll')) ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/out:'+$taskExe) $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Live-config fixture compilation failed.' }
& $taskExe $taskOutput
if ($LASTEXITCODE -ne 0) { throw 'Live-config endpoint regression failed.' }
foreach ($mode in @('ServerSimulation','PlayerSimulation')) {
    $taskGrid = 'src/AuthorityGrid.cs'
    $taskDefine = '/define:SERVER_SIMULATION'
    if ($mode -eq 'PlayerSimulation') { $taskGrid = 'src/PlayerSimulation/AuthorityGrid.cs'; $taskDefine = '/define:PLAYER_SIMULATION' }
    $taskTiming = Join-Path $taskOutput ($mode + 'TimingTests.exe')
    & $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ /nowarn:0649 $taskDefine ('/out:'+$taskTiming) (Join-Path $PSScriptRoot $taskGrid) (Join-Path $PSScriptRoot 'tests/LiveGridTimingTests.cs')
    if ($LASTEXITCODE -ne 0) { throw ('Live timing compile failed: ' + $mode) }
    & $taskTiming
    if ($LASTEXITCODE -ne 0) { throw ('Live timing preservation failed: ' + $mode) }
    $taskCadence = 'src/OwnershipCadence.cs'
    if ($mode -eq 'PlayerSimulation') { $taskCadence = 'src/PlayerSimulation/OwnershipCadence.cs' }
    $taskWake = Join-Path $taskOutput ($mode + 'CameraWakeTests.exe')
    $taskWakeSources = @('src/AuthorityGrid.cs','src/CameraHeading.cs','src/IntervalConfig.cs',$taskCadence,'tests/CameraWakeTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    & $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ /nowarn:0649 $taskDefine ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/out:'+$taskWake) $taskWakeSources
    if ($LASTEXITCODE -ne 0) { throw ('Camera wake compile failed: ' + $mode) }
    & $taskWake
    if ($LASTEXITCODE -ne 0) { throw ('Camera wake regression failed: ' + $mode) }
}
