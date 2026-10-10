param([string]$BaselineDirectory = (Join-Path $PSScriptRoot '..\TooManyLags-3.5.2'))
$ErrorActionPreference='Stop'
$authorityCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$authorityOutput=Join-Path $PSScriptRoot 'bin\benchmark'
New-Item -ItemType Directory -Force -Path $authorityOutput | Out-Null
$authorityBaseline=Join-Path $authorityOutput 'BaselineGrid.cs'
[IO.File]::WriteAllText($authorityBaseline,[IO.File]::ReadAllText((Join-Path $BaselineDirectory 'src\AuthorityGrid.cs')).Replace('namespace TooManyLags','namespace AuthorityBaseline'))
$authorityExe=Join-Path $authorityOutput 'FieldCacheBenchmark.exe'
& $authorityCompiler /nologo /optimize+ /warnaserror+ /langversion:5 ('/out:'+$authorityExe) $authorityBaseline (Join-Path $PSScriptRoot 'src\AuthorityGrid.cs') (Join-Path $PSScriptRoot 'tests\FieldCacheBenchmark.cs')
if($LASTEXITCODE -ne 0){throw 'Benchmark compile failed.'}
& $authorityExe | Tee-Object -FilePath (Join-Path $authorityOutput 'results.txt')
if($LASTEXITCODE -ne 0){throw 'Benchmark failed.'}
[ordered]@{
    version='1.0.6';synthetic=$true;recordedAt=(Get-Date).ToString('o')
    baselineDirectory=$BaselineDirectory
    gridSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'src\AuthorityGrid.cs') -Algorithm SHA256).Hash
    baselineSha256=(Get-FileHash -LiteralPath (Join-Path $BaselineDirectory 'src\AuthorityGrid.cs') -Algorithm SHA256).Hash
    output=([IO.File]::ReadAllText((Join-Path $authorityOutput 'results.txt')))
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $authorityOutput 'results.json') -Encoding UTF8
