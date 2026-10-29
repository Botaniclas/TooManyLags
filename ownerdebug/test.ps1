param([string]$BepInExDirectory='C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$output=Join-Path $PSScriptRoot 'bin\tests'
New-Item -ItemType Directory -Force -Path $output|Out-Null
$harmony=Join-Path $BepInExDirectory 'core\0Harmony.dll'
Copy-Item -LiteralPath $harmony -Destination $output -Force
Get-ChildItem -LiteralPath (Join-Path $BepInExDirectory 'core') -Filter 'Mono*.dll'|Copy-Item -Destination $output -Force
$inputs=@('tests\Fixtures.cs','tests\DebugTests.cs','src\OwnerColour.cs','src\DisplayModes.cs','src\DebugView.cs','src\BuildingVisuals.cs','src\NativeColours.cs','src\DebugCategories.cs','src\TmlDebug.cs','src\PresentationPaint.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
$exe=Join-Path $output 'DebugTests.exe'
& $compiler '/nologo' '/optimize+' '/warnaserror+' '/langversion:5' ('/reference:'+$harmony) ('/out:'+$exe) @inputs
if($LASTEXITCODE -ne 0){throw 'Debug test compilation failed.'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Debug tests failed.'}
