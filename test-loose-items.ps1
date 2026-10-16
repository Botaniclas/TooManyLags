param([string]$ValheimDirectory='D:\Steam-Bibliotheksordner\steamapps\common\Valheim',[string]$BepInExDirectory='C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference='Stop'
$itemCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$itemOutput=Join-Path $PSScriptRoot 'bin/loose-item-tests'
New-Item -ItemType Directory -Force $itemOutput | Out-Null
$itemCecil=Join-Path $BepInExDirectory 'core/Mono.Cecil.dll'
Copy-Item -LiteralPath $itemCecil -Destination $itemOutput -Force
$itemExe=Join-Path $itemOutput 'NativeAudit.exe'
& $itemCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/reference:'+$itemCecil) ('/out:'+$itemExe) (Join-Path $PSScriptRoot 'tests/LooseItemNativeAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Item native audit compilation failed.'}
& $itemExe (Join-Path $ValheimDirectory 'valheim_Data/Managed/assembly_valheim.dll') (Join-Path $PSScriptRoot 'bin/TooManyLags.dll')
if($LASTEXITCODE -ne 0){throw 'Item native contracts changed.'}
