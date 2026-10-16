param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference = 'Stop'
$layoutCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$layoutCore = Join-Path $BepInExDirectory 'core'
$layoutOutput = Join-Path $PSScriptRoot 'bin\config-layout-tests'
New-Item -ItemType Directory -Force -Path $layoutOutput | Out-Null
foreach ($layoutDependency in @('BepInEx.dll','0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) { Copy-Item -LiteralPath (Join-Path $layoutCore $layoutDependency) -Destination $layoutOutput -Force }
$layoutExe = Join-Path $layoutOutput 'ConfigLayoutTests.exe'
$layoutInputs = @('src\ConfigLayout.cs','src\ConfigTemplate.cs','tests\ConfigLayoutTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$layoutArguments = @('/nologo','/target:exe','/optimize+','/warnaserror+','/langversion:5',('/reference:' + (Join-Path $layoutCore 'BepInEx.dll')),('/reference:' + (Join-Path $layoutCore '0Harmony.dll')),('/out:' + $layoutExe))
if ($PSScriptRoot.EndsWith('TooManyLagsStandalone')) { $layoutArguments += '/define:STANDALONE' }
& $layoutCompiler @layoutArguments @layoutInputs
if ($LASTEXITCODE -ne 0) { throw 'Config layout test compilation failed.' }
& $layoutExe $layoutOutput
if ($LASTEXITCODE -ne 0) { throw 'Config layout regression failed.' }
