param(
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx'
)
$ErrorActionPreference = 'Stop'
$nativeCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$nativeManaged = Join-Path $ValheimDirectory 'valheim_Data\Managed'
$nativeCore = Join-Path $BepInExDirectory 'core'
$nativeOutput = Join-Path $PSScriptRoot 'bin\native-audit'
New-Item -ItemType Directory -Force -Path $nativeOutput | Out-Null
$nativeHarmony = Join-Path $nativeCore '0Harmony.dll'
$nativeCecil = Join-Path $nativeCore 'Mono.Cecil.dll'
Copy-Item -LiteralPath $nativeHarmony,$nativeCecil -Destination $nativeOutput -Force
$nativeExe = Join-Path $nativeOutput 'NativeReleaseAudit.exe'
& $nativeCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/reference:' + $nativeHarmony) ('/reference:' + $nativeCecil) ('/out:' + $nativeExe) (Join-Path $PSScriptRoot 'tests\NativeReleaseAudit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native audit compilation failed.' }
$nativeDll = Join-Path $PSScriptRoot 'bin\TooManyLagsStandalone.dll'
$nativeResult = @(& $nativeExe $nativeDll $nativeManaged $nativeCore 2>&1)
$nativeStatus = $LASTEXITCODE
$nativeResult | ForEach-Object { Write-Output $_ }
[ordered]@{ production_sha256 = (Get-FileHash -LiteralPath $nativeDll).Hash; native_game_sha256 = (Get-FileHash -LiteralPath (Join-Path $nativeManaged 'assembly_valheim.dll')).Hash; passed = ($nativeStatus -eq 0); output = ($nativeResult -join [Environment]::NewLine); scope = 'Actual native IL and bindings; no Unity gameplay, transport or live patch installation.' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $nativeOutput 'verification.json') -Encoding UTF8
if ($nativeStatus -ne 0) { throw 'Native audit failed.' }
