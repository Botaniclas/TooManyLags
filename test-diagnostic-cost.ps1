param(
    [string]$BepInExDirectory='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/profiles/Client/BepInEx',
    [string]$ValheimDirectory='D:/Steam-Bibliotheksordner/steamapps/common/Valheim'
)
$ErrorActionPreference='Stop'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskOutput=Join-Path $PSScriptRoot 'bin/diagnostic-cost-tests'
New-Item -ItemType Directory -Force -Path $taskOutput|Out-Null
$taskCecil=Join-Path $BepInExDirectory 'core/Mono.Cecil.dll'
Copy-Item -LiteralPath $taskCecil -Destination $taskOutput -Force
$taskExe=Join-Path $taskOutput 'DiagnosticLoggingAudit.exe'
& $taskCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/reference:'+$taskCecil) ('/out:'+$taskExe) (Join-Path $PSScriptRoot 'tests/DiagnosticLoggingAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Diagnostic audit compilation failed.'}
$taskDll=Join-Path $PSScriptRoot 'bin/TooManyLags.dll'
$taskPrior=Join-Path (Split-Path $PSScriptRoot) 'TooManyLags-1.0.80-flight-handoff/bin/TooManyLags.dll'
& $taskExe $taskDll $taskPrior $PSScriptRoot
if($LASTEXITCODE -ne 0){throw 'Diagnostic/logging regressions failed.'}
$taskHost=Join-Path $taskOutput 'DiagnosticMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ /langversion:5 ('/out:'+$taskHost) (Join-Path $PSScriptRoot 'tests/DiagnosticMonoHost.cs')
if($LASTEXITCODE -ne 0){throw 'Diagnostic Mono host compilation failed.'}
$taskLines=@(& $taskHost $taskExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') $taskDll $taskPrior $PSScriptRoot 2>&1)
if($LASTEXITCODE -ne 0 -or !($taskLines -match '^PASS: \d+ diagnostic-cost/logging')){$taskLines|Write-Output;throw 'Diagnostic Mono regression failed.'}
$taskLines|Write-Output
