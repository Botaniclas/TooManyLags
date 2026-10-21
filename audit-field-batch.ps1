param([Parameter(Mandatory=$true)][string]$BaselineProject)
$ErrorActionPreference='Stop'
$auditOutput=Join-Path $PSScriptRoot 'bin\field-batch-audit'
New-Item -ItemType Directory -Force -Path $auditOutput | Out-Null
$auditCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
foreach($auditMode in @('Server','Player')) {
    $auditHashes=@()
    foreach($auditVariant in @('Baseline','Candidate')) {
        $auditProject=if($auditVariant -eq 'Baseline'){$BaselineProject}else{$PSScriptRoot}
        $auditRelative=if($auditMode -eq 'Server'){'src\AuthorityGrid.cs'}else{'src\PlayerSimulation\AuthorityGrid.cs'}
        $auditDefines=if($auditMode -eq 'Server'){'/define:BEFORE_SCHEDULER'}else{'/define:BEFORE_SCHEDULER,PLAYER_SIMULATION'}
        $auditExe=Join-Path $auditOutput ($auditMode+$auditVariant+'.exe')
        & $auditCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 $auditDefines ('/out:'+$auditExe) (Join-Path $auditProject $auditRelative) (Join-Path $PSScriptRoot 'tests\FieldBatchTrace.cs')
        if($LASTEXITCODE -ne 0){throw 'Differential trace compilation failed.'}
        $auditTrace=Join-Path $auditOutput ($auditMode+$auditVariant+'.trace')
        & $auditExe $auditTrace
        if($LASTEXITCODE -ne 0){throw 'Differential trace failed.'}
        $auditHashes+=(Get-FileHash -LiteralPath $auditTrace -Algorithm SHA256).Hash
    }
    if($auditHashes[0] -cne $auditHashes[1]){throw ('Scheduling changed in '+$auditMode+' mode.')}
    Write-Output ('PASS: '+$auditMode+' mode; 40000 event steps; byte-identical object owners, writes, callbacks, deadlines and lifecycle; SHA256='+$auditHashes[0])
}
