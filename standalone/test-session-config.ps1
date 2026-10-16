param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference = 'Stop'
$sessionCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$sessionOutput = Join-Path $PSScriptRoot 'bin/session-config-tests'
$sessionCore = Join-Path $BepInExDirectory 'core'
New-Item -ItemType Directory -Force -Path $sessionOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $sessionCore 'BepInEx.dll') -Destination $sessionOutput -Force
$sessionExe = Join-Path $sessionOutput 'SessionConfigTests.exe'
$sessionSources = @('src/Settings.cs','src/ConfigLayout.cs','src/ConfigTemplate.cs','src/ConfigCommands.cs','src/ConfigConsole.cs','src/Network.cs','tests/SessionConfigFixtures.cs','tests/SessionConfigTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $sessionCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ /nowarn:0649 ('/reference:'+(Join-Path $sessionCore 'BepInEx.dll')) ('/out:'+$sessionExe) $sessionSources
if ($LASTEXITCODE -ne 0) { throw 'Session/config fixture compilation failed.' }
& $sessionExe $sessionOutput
if ($LASTEXITCODE -ne 0) { throw 'Session/config regression failed.' }
