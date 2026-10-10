param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskOutput = Join-Path $PSScriptRoot 'bin\session-settings-tests'
$taskCore = Join-Path $BepInExDirectory 'core'
$taskBepInEx = Join-Path $taskCore 'BepInEx.dll'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
Copy-Item -LiteralPath $taskBepInEx -Destination $taskOutput -Force
$taskExe = Join-Path $taskOutput 'FgnSessionSettingsTests.exe'
$taskSources = @('src\FgnSessionSettings.cs', 'tests\FgnSessionSettingsTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $taskCompiler '/nologo' '/target:exe' '/optimize+' '/langversion:5' '/warnaserror+' ('/reference:' + $taskBepInEx) ('/out:' + $taskExe) $taskSources
if ($LASTEXITCODE -ne 0) { throw 'FGN session settings fixture compilation failed.' }
& $taskExe (Join-Path $taskOutput 'fixture.cfg') $taskCore
if ($LASTEXITCODE -ne 0) { throw 'FGN session settings fixture failed.' }
