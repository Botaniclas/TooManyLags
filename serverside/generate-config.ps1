param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$core=Join-Path $BepInExDirectory 'core'
$out=Join-Path $PSScriptRoot 'bin/config-probe'
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item -LiteralPath (Join-Path $core 'BepInEx.dll') -Destination $out -Force
$probe=Join-Path $out 'ConfigProbe.exe'
& $compiler /nologo /target:exe /warnaserror+ /optimize+ /langversion:5 ('/out:'+$probe) ('/reference:'+(Join-Path $core 'BepInEx.dll')) (Join-Path $PSScriptRoot 'tests/ConfigProbe.cs')
if ($LASTEXITCODE -ne 0) {throw 'Config probe compilation failed'}
$config=Join-Path $PSScriptRoot 'botanic.toomanylags.dedicatedaddon.cfg'
& $probe (Join-Path $PSScriptRoot 'bin/TooManyLagsServerSide.dll') $config $core
if ($LASTEXITCODE -ne 0) {throw 'Production config probe failed'}
