$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskTests = Join-Path $PSScriptRoot 'bin\combat-tests'
New-Item -ItemType Directory -Force -Path $taskTests | Out-Null
$taskOutput = Join-Path $taskTests 'CombatPupReadinessTests.exe'
$taskSources = @('src\FieldReader.cs','src\NativePup.cs','src\CellReadiness.cs','tests\CombatPupReadinessTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $taskCompiler '/nologo' '/target:exe' '/optimize+' '/langversion:5' '/warnaserror+' '/define:TESTING' ('/out:' + $taskOutput) $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Combat optional PUP readiness fixture compilation failed.' }
& $taskOutput
if ($LASTEXITCODE -ne 0) { throw 'Combat optional PUP readiness fixtures failed.' }
