param([Parameter(Mandatory=$true)][string]$BaselineProject)
$ErrorActionPreference='Stop'
$auditOutput=Join-Path $PSScriptRoot 'bin\field-batch-benchmark'
New-Item -ItemType Directory -Force -Path $auditOutput | Out-Null
$auditSources=@()
foreach($auditVariant in @('Baseline','Candidate')) {
    $auditProject=if($auditVariant -eq 'Baseline'){$BaselineProject}else{$PSScriptRoot}
    $auditGrid=Join-Path $auditOutput ($auditVariant+'Grid.cs')
    [IO.File]::WriteAllText($auditGrid,[IO.File]::ReadAllText((Join-Path $auditProject 'src\AuthorityGrid.cs')).Replace('namespace TooManyLags','namespace '+$auditVariant))
    $auditRunner=Join-Path $auditOutput ($auditVariant+'Runner.cs')
    [IO.File]::WriteAllText($auditRunner,[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tests\FieldBatchRunner.cs.in')).Replace('NAMESPACE',$auditVariant))
    $auditSources+=@($auditGrid,$auditRunner)
}
$auditExecutable=Join-Path $auditOutput 'FieldBatchBenchmark.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe') /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 /define:TML_RELEASE ('/out:'+$auditExecutable) $auditSources (Join-Path $PSScriptRoot 'tests\FieldBatchBenchmark.cs')
if($LASTEXITCODE -ne 0){throw 'Field batch benchmark compilation failed.'}
$auditLines=@(& $auditExecutable)
if($LASTEXITCODE -ne 0){throw 'Field batch benchmark failed.'}
$auditLines | Set-Content -LiteralPath (Join-Path $auditOutput 'results.txt') -Encoding UTF8
$auditLines | Write-Output
