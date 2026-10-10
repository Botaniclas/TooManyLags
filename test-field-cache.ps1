$ErrorActionPreference='Stop'
$authorityCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$authorityOutput=Join-Path $PSScriptRoot 'bin\field-cache-tests'
New-Item -ItemType Directory -Force -Path $authorityOutput | Out-Null
$authorityExe=Join-Path $authorityOutput 'FieldCacheTests.exe'
& $authorityCompiler /nologo /optimize+ /warnaserror+ /langversion:5 ('/out:'+$authorityExe) (Join-Path $PSScriptRoot 'src\AuthorityGrid.cs') (Join-Path $PSScriptRoot 'src\IntervalConfig.cs') (Join-Path $PSScriptRoot 'tests\FieldCacheTests.cs')
if($LASTEXITCODE -ne 0){throw 'Field-cache compile failed.'}
& $authorityExe
if($LASTEXITCODE -ne 0){throw 'Field-cache tests failed.'}
