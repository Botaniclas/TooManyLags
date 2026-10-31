param(
    [string]$ValheimDirectory='D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory='C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$FgnDll='C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\cache\VerdantsAscent-FiresGhettoNetworking\1.5.72\plugins\FiresGhettoNetworking\VAGhettoNetworking.dll',
    [switch]$DebugSystem = $true
)
$ErrorActionPreference='Stop'
$taskAddon=Join-Path $PSScriptRoot $(if($DebugSystem){'bin/debug-system/TooManyLags.dll'}else{'bin/TooManyLags.dll'})
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$output=Join-Path $PSScriptRoot 'bin\state-transfer-tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$core=Join-Path $BepInExDirectory 'core'
$harmony=Join-Path $core '0Harmony.dll'
$hostExe=Join-Path $output 'NativeStateTransferTests.exe'
& $compiler /nologo /optimize+ /warnaserror+ /langversion:5 /main:NativeStateTransferTests ('/reference:'+$harmony) ('/out:'+$hostExe) (Join-Path $PSScriptRoot 'tests\NativeStateAuditSupport.cs') (Join-Path $PSScriptRoot 'tests\NativeWireAuditSupport.cs') (Join-Path $PSScriptRoot 'tests\NativeCompactCompressionTests.cs') (Join-Path $PSScriptRoot 'tests\NativeStateTransferTests.cs')
if($LASTEXITCODE -ne 0){throw 'State transfer audit compilation failed.'}
Copy-Item -LiteralPath $harmony -Destination $output -Force
Get-ChildItem -LiteralPath $core -Filter 'Mono*.dll' | Copy-Item -Destination $output -Force
$installExe=Join-Path $output 'NativeStateInstallAudit.exe'
& $compiler /nologo /optimize+ /warnaserror+ /langversion:5 ('/reference:'+$harmony) ('/out:'+$installExe) (Join-Path $PSScriptRoot 'tests\NativeStateInstallAudit.cs')
if($LASTEXITCODE -ne 0){throw 'State transfer installation audit compilation failed.'}
$monoHost=Join-Path $output 'StateMonoFixtureHost.exe'
& $compiler /nologo /platform:x64 /optimize+ /warnaserror+ /langversion:5 ('/out:'+$monoHost) (Join-Path $PSScriptRoot 'tests\StateMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'State transfer Mono host compilation failed.'}
$monoLines=@(& $monoHost $installExe (Join-Path $ValheimDirectory 'valheim_Data\Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge\EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge\etc') $taskAddon $core $FgnDll 2>&1)
$monoStatus=$LASTEXITCODE
$monoLines | Set-Content -LiteralPath (Join-Path $output 'native-monoinstall.log') -Encoding UTF8
if($monoStatus -ne 0){$monoLines | ForEach-Object {Write-Output $_}; throw 'Actual native Harmony installation failed.'}
$monoLines | Where-Object { $_ -match '^(PASS:|LIMIT:)' } | ForEach-Object {Write-Output $_}
Write-Output 'NOTE: the headless Mono host reports the expected unresolved Unity Time call; complete output is in native-monoinstall.log. No Unity world is started.'
$legacyCache=Join-Path (Split-Path (Split-Path (Split-Path $BepInExDirectory))) 'cache\VerdantsAscent-FiresGhettoNetworking\1.5.36\VAGhettoNetworking.dll'
if(Test-Path -LiteralPath $legacyCache){
    $legacyLines=@(& $monoHost $installExe (Join-Path $ValheimDirectory 'valheim_Data\Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge\EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge\etc') $taskAddon $core $legacyCache 2>&1)
    if($LASTEXITCODE -ne 0){$legacyLines | ForEach-Object {Write-Output $_}; throw 'Legacy FGN optional-feature fallback failed.'}
    $legacyLines | Set-Content -LiteralPath (Join-Path $output 'native-legacy-fallback.log') -Encoding UTF8
    $legacyLines | Where-Object { $_ -match '^PASS:' } | ForEach-Object {Write-Output $_}
}
'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestMinor"}}' | Set-Content -LiteralPath (Join-Path $output 'NativeStateTransferTests.runtimeconfig.json') -Encoding UTF8
$nativeLines=@(& 'C:\Program Files\dotnet\dotnet.exe' $hostExe $taskAddon (Join-Path $ValheimDirectory 'valheim_Data\Managed') $core $FgnDll 2>&1)
if($LASTEXITCODE -ne 0){$nativeLines | ForEach-Object {Write-Output $_}; throw 'Production state transfer/native receive tests failed.'}
$nativeLines | ForEach-Object {Write-Output $_}
$nativeLines | Set-Content -LiteralPath (Join-Path $output 'native-state-transfer.log') -Encoding UTF8
[ordered]@{
    dllSha256=(Get-FileHash -LiteralPath $taskAddon -Algorithm SHA256).Hash
    nativeGameSha256=(Get-FileHash -LiteralPath (Join-Path $ValheimDirectory 'valheim_Data\Managed\assembly_valheim.dll') -Algorithm SHA256).Hash
    fgnSha256=(Get-FileHash -LiteralPath $FgnDll -Algorithm SHA256).Hash
    monoInstall=$true;nativeExecution=$true;liveMultiplayer=$false
    output=($nativeLines -join [Environment]::NewLine)
    scope='Production sender helpers, guarded native receiver IL, actual ZPackage/ZRpc repair envelopes, actual FGN encoder/decoder, and headless Valheim Mono Harmony installation. Declared Unity seams; no live transport benchmark.'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'verification.json') -Encoding UTF8
