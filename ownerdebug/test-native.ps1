param(
 [string]$ValheimDirectory='D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
 [string]$BepInExDirectory='C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx'
)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$output=Join-Path $PSScriptRoot 'bin\tests'
New-Item -ItemType Directory -Force -Path $output|Out-Null
$cecil=Join-Path $BepInExDirectory 'core\Mono.Cecil.dll'
Copy-Item -LiteralPath $cecil -Destination $output -Force
$exe=Join-Path $output 'NativeAudit.exe'
& $compiler '/nologo' '/optimize+' '/warnaserror+' '/langversion:5' ('/reference:'+$cecil) ('/out:'+$exe) (Join-Path $PSScriptRoot 'tests\NativeAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Native audit compilation failed.'}
& $exe (Join-Path $PSScriptRoot 'bin\BotanicOwnerDebug.dll') (Join-Path $ValheimDirectory 'valheim_Data\Managed\assembly_valheim.dll')
if($LASTEXITCODE -ne 0){throw 'Native audit failed.'}
