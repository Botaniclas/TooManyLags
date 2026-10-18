param(
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$FgnCache = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\cache\VerdantsAscent-FiresGhettoNetworking'
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$runtime = 'C:\Program Files\dotnet\dotnet.exe'
$managed = Join-Path $ValheimDirectory 'valheim_Data\Managed'
$core = Join-Path $BepInExDirectory 'core'
$dll = Join-Path $PSScriptRoot 'bin\TooManyLags.dll'
$output = Join-Path $PSScriptRoot 'bin\native-audit'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$harmony = Join-Path $core '0Harmony.dll'
$cecil = Join-Path $core 'Mono.Cecil.dll'
Copy-Item -LiteralPath $harmony -Destination (Join-Path $output '0Harmony.dll') -Force
Copy-Item -LiteralPath $cecil -Destination (Join-Path $output 'Mono.Cecil.dll') -Force
$source = Join-Path $PSScriptRoot 'tests\NativeReleaseAudit.cs'
$audit = Join-Path $output 'NativeReleaseAudit.exe'
& $compiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/reference:' + $harmony) ('/reference:' + $cecil) ('/out:' + $audit) $source
if ($LASTEXITCODE -ne 0) { throw 'Native release audit host failed to compile.' }
if (!(Test-Path -LiteralPath $runtime)) { throw 'Actual native interfaces require an installed .NET 8+ audit runtime.' }
'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestMinor"}}' |
    Set-Content -LiteralPath (Join-Path $output 'NativeReleaseAudit.runtimeconfig.json') -Encoding UTF8
$hash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
$results = @()
$actorResult = @()
$failed = $false
foreach ($version in @('1.5.41', '1.5.36', '1.5.65')) {
    $fgn = Join-Path $FgnCache ($version + '\VAGhettoNetworking.dll')
    if (!(Test-Path -LiteralPath $fgn)) { $fgn = Join-Path $FgnCache ($version + '\plugins\FiresGhettoNetworking\VAGhettoNetworking.dll') }
    $lines = @(& $audit $dll $managed $core $fgn $version 2>&1)
    $status = $LASTEXITCODE
    $lines | ForEach-Object { Write-Output $_ }
    $results += [ordered]@{ version = $version; sha256 = (Get-FileHash -LiteralPath $fgn -Algorithm SHA256).Hash; passed = ($status -eq 0); output = ($lines -join [Environment]::NewLine) }
    if ($status -ne 0) { $failed = $true; break }
    if ($actorResult.Count -eq 0) {
        $actorLines = @(& $runtime $audit $dll $managed $core $fgn $version 'pickable' 2>&1)
        $actorStatus = $LASTEXITCODE
        $actorLines | ForEach-Object { Write-Output $_ }
        $actorResult += [ordered]@{ version = $version; passed = ($actorStatus -eq 0); output = ($actorLines -join [Environment]::NewLine) }
        if ($actorStatus -ne 0) { $failed = $true; break }
    }
}
foreach ($providerVersion in @('1.5.53', '1.5.65')) {
    if ($failed) { break }
    # Latest provider: target the reported Pickable collision. Full networking
    # and simulation-mode contracts retain their separate validated baselines.
    $provider = Join-Path $FgnCache ($providerVersion + '\plugins\FiresGhettoNetworking\VAGhettoNetworking.dll')
    $actorLines = @(& $runtime $audit $dll $managed $core $provider $providerVersion 'pickable' 2>&1)
    $actorStatus = $LASTEXITCODE
    $actorLines | ForEach-Object { Write-Output $_ }
    $actorResult += [ordered]@{ version=$providerVersion; providerSha256=(Get-FileHash -LiteralPath $provider -Algorithm SHA256).Hash; passed=($actorStatus -eq 0); output=($actorLines -join [Environment]::NewLine) }
    if ($actorStatus -ne 0) { $failed = $true }
}
$changed = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -ne $hash
$verification = [ordered]@{
    authority_sha256 = $hash
    production_changed_during_audit = $changed
    native_game_sha256 = (Get-FileHash -LiteralPath (Join-Path $managed 'assembly_valheim.dll') -Algorithm SHA256).Hash
    fgn = $results
    pickable_actor_rewrite = $actorResult
    scope = 'Production rewrite methods on actual native IL plus metadata/delegate contracts; no Unity gameplay, networking or live Harmony installation.'
}
$verification | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'verification.json') -Encoding UTF8
if ($changed) { throw 'Production DLL changed during audit; rerun on final build.' }
if ($failed) { throw 'Native release audit failed; details saved in bin/native-audit/verification.json.' }
