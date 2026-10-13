param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskOutput = Join-Path $PSScriptRoot 'bin/camera-recovery-tests'
$taskCore = Join-Path $BepInExDirectory 'core'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
foreach ($dep in @('BepInEx.dll', '0Harmony.dll', 'MonoMod.Utils.dll', 'MonoMod.RuntimeDetour.dll', 'Mono.Cecil.dll')) {
    Copy-Item -LiteralPath (Join-Path $taskCore $dep) -Destination $taskOutput -Force
}
$taskExe = Join-Path $taskOutput 'CameraIntervalRecoveryTests.exe'
$taskSources = @('src/ConfigCommands.cs', 'src/ConfigConsole.cs', 'src/ServerSession.cs', 'tests/CameraIntervalRecoveryTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:' + (Join-Path $taskCore 'BepInEx.dll')) ('/reference:' + (Join-Path $taskCore '0Harmony.dll')) ('/out:' + $taskExe) $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Camera recovery fixture compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Camera recovery regression failed.' }
$taskSession = Join-Path $taskOutput 'ServerSessionTests.exe'
& $taskCompiler /nologo /target:exe /main:ServerSessionTests /optimize+ /langversion:5 /warnaserror+ ('/reference:' + (Join-Path $taskCore 'BepInEx.dll')) ('/reference:' + (Join-Path $taskCore '0Harmony.dll')) ('/out:' + $taskSession) $taskSources (Join-Path $PSScriptRoot 'tests/ServerSessionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Server session fixture compilation failed.' }
& $taskSession
if ($LASTEXITCODE -ne 0) { throw 'Server-controlled startup regression failed.' }
