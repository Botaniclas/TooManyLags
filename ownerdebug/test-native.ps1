param(
 [string]$ValheimDirectory='D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
 [string]$BepInExDirectory='C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
 [string]$TmlAssembly=(Join-Path $PSScriptRoot '..\TooManyLags-1.0.94-handoff-sim\bin\debug-system\TooManyLags.dll')
)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$output=Join-Path $PSScriptRoot 'bin\native-tests'
New-Item -ItemType Directory -Force -Path $output|Out-Null
$cecil=Join-Path $BepInExDirectory 'core\Mono.Cecil.dll'
Copy-Item -LiteralPath $cecil -Destination $output -Force
$exe=Join-Path $output 'NativeAudit.exe'
& $compiler '/nologo' '/optimize+' '/warnaserror+' '/langversion:5' ('/reference:'+$cecil) ('/out:'+$exe) (Join-Path $PSScriptRoot 'tests\NativeAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Native audit compilation failed.'}
& $exe (Join-Path $PSScriptRoot 'bin\BotanicOwnerDebug.dll') (Join-Path $ValheimDirectory 'valheim_Data\Managed\assembly_valheim.dll') (Join-Path $ValheimDirectory 'valheim_Data\Managed\UnityEngine.CoreModule.dll')
if($LASTEXITCODE -ne 0){throw 'Native audit failed.'}
$core=Join-Path $BepInExDirectory 'core'
Get-ChildItem -LiteralPath $core -File | Where-Object Name -Match '^(0Harmony|Mono).*\.dll$' |
 Copy-Item -Destination $output -Force
$render=Join-Path $output 'RenderNativeAudit.exe'
& $compiler /nologo /optimize+ /warnaserror+ /langversion:5 ('/reference:'+(Join-Path $core '0Harmony.dll')) `
 ('/out:'+$render) (Join-Path $PSScriptRoot 'tests\RenderNativeAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Native render-hook audit compilation failed.'}
$hostExe=Join-Path $output 'RenderMonoHost.exe'
& $compiler /nologo /platform:x64 /optimize+ /warnaserror+ /langversion:5 ('/out:'+$hostExe) `
 (Join-Path $PSScriptRoot 'tests\RenderMonoHost.cs')
if($LASTEXITCODE -ne 0){throw 'Native render-hook host compilation failed.'}
$plugin=Join-Path $PSScriptRoot 'bin\BotanicOwnerDebug.dll'
$renderOutput=@(& $hostExe $render (Join-Path $ValheimDirectory 'valheim_Data\Managed') `
 (Join-Path $ValheimDirectory 'MonoBleedingEdge\EmbedRuntime') `
 (Join-Path $ValheimDirectory 'MonoBleedingEdge\etc') $plugin $core $TmlAssembly 2>&1)
$renderOutput | ForEach-Object { Write-Output $_ }
if($LASTEXITCODE -ne 0 -or !($renderOutput -match '^PASS: \d+ actual Unity render-dispatcher')){
 throw 'Actual Unity render-dispatcher hook audit failed.'
}
