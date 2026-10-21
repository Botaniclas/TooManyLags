param(
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx'
)
$ErrorActionPreference = 'Stop'
$verifyDll = Join-Path $PSScriptRoot 'bin\TooManyLagsStandalone.dll'
& (Join-Path $PSScriptRoot 'build.ps1') -ValheimDirectory $ValheimDirectory -BepInExDirectory $BepInExDirectory | Out-Host
$verifyHash = (Get-FileHash -LiteralPath $verifyDll).Hash
$verifySources = [ordered]@{}
foreach ($verifyInput in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src'),(Join-Path $PSScriptRoot 'tests'),(Join-Path $PSScriptRoot 'tools') -Recurse -File) { $verifySources[$verifyInput.FullName.Substring($PSScriptRoot.Length + 1)] = (Get-FileHash -LiteralPath $verifyInput.FullName).Hash }
$verifyResults = [ordered]@{}
foreach ($verifySuite in @('test-policy.ps1','test-camera.ps1','test-handoffs.ps1','test-mob-integration.ps1','test-session-config.ps1','test-text-editing.ps1','test-native.ps1','generate-config.ps1','test-config-layout.ps1')) {
    $verifyArgs = @{}
    if ($verifySuite -ne 'test-policy.ps1') { $verifyArgs.BepInExDirectory = $BepInExDirectory }
    if ($verifySuite -in @('test-native.ps1','test-text-editing.ps1','test-handoffs.ps1','test-mob-integration.ps1')) { $verifyArgs.ValheimDirectory = $ValheimDirectory }
    $verifyLines = @(& (Join-Path $PSScriptRoot $verifySuite) @verifyArgs)
    $verifyLines | ForEach-Object { Write-Host $_ }
    $verifyResults[$verifySuite] = [ordered]@{ passed = $true; output = ($verifyLines -join [Environment]::NewLine) }
}
if ((Get-FileHash -LiteralPath $verifyDll).Hash -ne $verifyHash) { throw 'Production DLL changed during verification.' }
foreach ($verifyInput in $verifySources.Keys) { if ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $verifyInput)).Hash -ne $verifySources[$verifyInput]) { throw ('Source changed during verification: ' + $verifyInput) } }
$verifyManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($verifyManifest.version_number -ne '1.1.3' -or $verifyManifest.name -ne 'TooManyLags_Standalone' -or $verifyManifest.description.Length -gt 250 -or $verifyManifest.dependencies.Count -ne 1 -or $verifyManifest.dependencies[0] -ne 'denikson-BepInExPack_Valheim-5.4.2351') { throw 'Release manifest mismatch.' }
$verifyArtifacts = [ordered]@{}
foreach ($verifyFile in @('README.md','CHANGELOG.md','manifest.json','icon.png','LICENSE','THIRD_PARTY_NOTICES.md','VALIDATION.md','botanic.toomanylags.standalone.cfg')) { $verifyArtifacts[$verifyFile] = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $verifyFile)).Hash }
[ordered]@{ version = '1.1.3'; dllSha256 = $verifyHash; nativeGameSha256 = (Get-FileHash -LiteralPath (Join-Path $ValheimDirectory 'valheim_Data\Managed\assembly_valheim.dll')).Hash; tests = $verifyResults; sourceHashes = $verifySources; artifactHashes = $verifyArtifacts; liveMultiplayerTested = $false; testedAtUtc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Encoding UTF8
Write-Host 'PASS: final DLL, automated suites, native contracts, generated config and release metadata verified.'
