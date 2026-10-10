$ErrorActionPreference='Stop'
$authorityCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$authorityFolder=Join-Path $PSScriptRoot 'bin\region-tests'
New-Item -ItemType Directory -Force -Path $authorityFolder | Out-Null
$authorityExecutable=Join-Path $authorityFolder 'RegionGateTests.exe'
& $authorityCompiler /nologo /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 ('/out:'+$authorityExecutable) (Join-Path $PSScriptRoot 'src\AuthorityGrid.cs') (Join-Path $PSScriptRoot 'src\RegionContentionGate.cs') (Join-Path $PSScriptRoot 'tests\RegionGateTests.cs')
if($LASTEXITCODE -ne 0){throw 'Region gate compile failed.'}
& $authorityExecutable
if($LASTEXITCODE -ne 0){throw 'Region gate tests failed.'}
