param([string]$BepInExDirectory='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/profiles/Client/BepInEx')
$ErrorActionPreference='Stop'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskDirectory=Join-Path $PSScriptRoot 'bin/debug-build-audit'
New-Item -ItemType Directory -Force -Path $taskDirectory|Out-Null
$taskCecil=Join-Path $BepInExDirectory 'core/Mono.Cecil.dll'
Copy-Item -LiteralPath $taskCecil -Destination $taskDirectory -Force
$taskExe=Join-Path $taskDirectory 'DebugBuildAudit.exe'
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+$taskCecil) ('/out:'+$taskExe) (Join-Path $PSScriptRoot 'tests/DebugBuildAudit.cs') (Join-Path $PSScriptRoot 'tests/DebugIdleAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Build boundary audit failed to compile.'}
& $taskExe (Join-Path $PSScriptRoot 'bin/TooManyLags.dll') (Join-Path $PSScriptRoot 'bin/debug-system/TooManyLags.dll') (Join-Path (Split-Path $PSScriptRoot) 'TooManyLags-1.0.94-debug-idle/bin/debug-system/TooManyLags.dll')
if($LASTEXITCODE -ne 0){throw 'Compiled build boundary failed its audit.'}
foreach($taskMode in @('bin','bin/debug-system')){
    $taskDirectory=Join-Path $PSScriptRoot $taskMode
    $taskReceipt=Get-Content -LiteralPath (Join-Path $taskDirectory 'build-mode.json') -Raw|ConvertFrom-Json
    if($taskReceipt.debugSystem -ne ($taskMode -eq 'bin/debug-system') -or $taskReceipt.dllSha256 -ne (Get-FileHash -LiteralPath (Join-Path $taskDirectory 'TooManyLags.dll') -Algorithm SHA256).Hash){throw 'Build mode receipt mismatch.'}
}
Write-Output 'PASS: build receipts match separate normal/debug DLLs; release and debug outputs stay separate.'

