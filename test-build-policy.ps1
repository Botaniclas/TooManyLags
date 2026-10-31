param([string]$BepInExDirectory='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/profiles/Client/BepInEx')
$ErrorActionPreference='Stop'
$taskBuild=Join-Path $PSScriptRoot 'build.ps1'
$taskParameters=(Get-Command $taskBuild).ScriptBlock.Ast.ParamBlock.Parameters
$taskDefault=($taskParameters|Where-Object {$_.Name.VariablePath.UserPath -eq 'DebugSystem'}).DefaultValue.SafeGetValue()
if(!$taskDefault){throw 'Build must default to debug.'}
if(!($taskParameters|Where-Object {$_.Name.VariablePath.UserPath -eq 'Release'})){throw 'Explicit release switch absent.'}
foreach($taskArgs in @(@{DebugSystem=$false},@{DebugSystem=$true;Release=$true})){
    $taskRejected=$false
    try { & $taskBuild @taskArgs | Out-Null } catch {
        $taskRejected=$_.Exception.Message -match 'Use -Release explicitly|Choose -Release or -DebugSystem'
    }
    if(!$taskRejected){throw 'Implicit/ambiguous non-debug build was accepted.'}
}
$taskReceipt=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bin/debug-system/build-mode.json') -Raw|ConvertFrom-Json
$taskDll=Join-Path $PSScriptRoot 'bin/debug-system/TooManyLags.dll'
if(!$taskReceipt.debugSystem -or $taskReceipt.dllSha256 -ne (Get-FileHash -LiteralPath $taskDll -Algorithm SHA256).Hash){throw 'Default debug receipt mismatch.'}
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskOutput=Join-Path $PSScriptRoot 'bin/build-policy-tests'
New-Item -ItemType Directory -Force -Path $taskOutput|Out-Null
$taskCecil=Join-Path $BepInExDirectory 'core/Mono.Cecil.dll'
Copy-Item -LiteralPath $taskCecil -Destination $taskOutput -Force
$taskExe=Join-Path $taskOutput 'BuildPolicyAudit.exe'
& $taskCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/reference:'+$taskCecil) ('/out:'+$taskExe) (Join-Path $PSScriptRoot 'tests/BuildPolicyAudit.cs') (Join-Path $PSScriptRoot 'tests/DebugIdleAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Build policy audit compilation failed.'}
& $taskExe $taskDll
if($LASTEXITCODE -ne 0){throw 'Debug default failed emitted-DLL audit.'}
Write-Output 'PASS: default debug policy, explicit release switch, conflicting/implicit release rejection and matching build receipt.'
