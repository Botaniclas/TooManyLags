param(
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx'
)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'generate-config.ps1') -BepInExDirectory $BepInExDirectory
& (Join-Path $PSScriptRoot 'build.ps1') -ValheimDirectory $ValheimDirectory -BepInExDirectory $BepInExDirectory
$authorityDll=Join-Path $PSScriptRoot 'bin\TooManyLags.dll'
$authorityVerifiedHash=(Get-FileHash -LiteralPath $authorityDll -Algorithm SHA256).Hash
$authorityResults=@()
foreach($authorityTest in @('test-inventory.ps1','test-fgn-handoff.ps1','test-integration.ps1','test-directional.ps1','test-field-cache.ps1','test-regions.ps1','test-native.ps1','test-combat.ps1','test-combat-mono.ps1','test-combat-bridge.ps1','test-combat-readiness.ps1')){
    $authorityArguments=@{}
    if($authorityTest -in @('test-inventory.ps1','test-fgn-handoff.ps1','test-native.ps1','test-combat.ps1','test-combat-mono.ps1')){$authorityArguments.ValheimDirectory=$ValheimDirectory}
    if($authorityTest -in @('test-fgn-handoff.ps1','test-integration.ps1','test-native.ps1','test-combat.ps1','test-combat-bridge.ps1')){$authorityArguments.BepInExDirectory=$BepInExDirectory}
    $authorityLines=@(& (Join-Path $PSScriptRoot $authorityTest) @authorityArguments)
    $authorityLines | ForEach-Object {Write-Output $_}
    $authorityResults += [ordered]@{test=$authorityTest;passed=$true;output=($authorityLines -join [Environment]::NewLine)}
}
$authorityCombatAudit=Join-Path $PSScriptRoot 'bin\combat-tests\NativeCombatAudit.exe'
$authorityModCache=Join-Path (Split-Path (Split-Path (Split-Path $BepInExDirectory))) 'cache'
$authorityFgnOlder=Join-Path $authorityModCache 'VerdantsAscent-FiresGhettoNetworking\1.5.36\VAGhettoNetworking.dll'
$authorityPup=Join-Path $authorityModCache 'PUP82-PUP_FPS\1.0.34\PUP_FPS.dll'
$authorityCombatLines=@(& $authorityCombatAudit $authorityFgnOlder (Join-Path $ValheimDirectory 'valheim_Data\Managed') $authorityDll $authorityPup)
if($LASTEXITCODE -ne 0){throw 'Imported AI actual native contracts failed with FGN 1.5.36.'}
$authorityCombatLines | ForEach-Object {Write-Output $_}
$authorityResults += [ordered]@{test='combat-native-fgn-1.5.36';passed=$true;output=($authorityCombatLines -join [Environment]::NewLine)}
if((Get-FileHash -LiteralPath $authorityDll -Algorithm SHA256).Hash -ne $authorityVerifiedHash){throw 'DLL changed during validation.'}
$authorityNative=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bin\native-audit\verification.json') -Raw | ConvertFrom-Json
if($authorityNative.authority_sha256 -ne $authorityVerifiedHash -or $authorityNative.production_changed_during_audit){throw 'Native audit does not match final build.'}
$authorityImport=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tests\combat-import-equivalence.json') -Raw | ConvertFrom-Json
foreach($authoritySource in $authorityImport.files){
    if($authoritySource.import_sha256 -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('src\'+$authoritySource.file)) -Algorithm SHA256).Hash){throw ('Imported source changed since equivalence review: '+$authoritySource.file)}
}
$authorityHashes=[ordered]@{}
foreach($authorityItem in @('botanic.toomanylags.fgnaddon.cfg','README.md','manifest.json','tests\combat-import-equivalence.json')){
    $authorityHashes[$authorityItem]=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $authorityItem) -Algorithm SHA256).Hash
}
$authorityBenchmark=$null
$authorityBenchmarkPath=Join-Path $PSScriptRoot 'bin\benchmark\results.json'
if(Test-Path -LiteralPath $authorityBenchmarkPath){
    $authorityBenchmark=Get-Content -LiteralPath $authorityBenchmarkPath -Raw | ConvertFrom-Json
    if($authorityBenchmark.gridSha256 -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'src\AuthorityGrid.cs') -Algorithm SHA256).Hash){throw 'Benchmark source differs from verified grid.'}
}
$authorityRegionBenchmark=$null
$authorityRegionBenchmarkPath=Join-Path $PSScriptRoot 'bin\region-benchmark\results.json'
if(Test-Path -LiteralPath $authorityRegionBenchmarkPath){
    $authorityRegionBenchmark=Get-Content -LiteralPath $authorityRegionBenchmarkPath -Raw | ConvertFrom-Json
    foreach($authorityEntry in $authorityRegionBenchmark.sourceHashes.PSObject.Properties){
        if($authorityEntry.Value -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('src\'+$authorityEntry.Name)) -Algorithm SHA256).Hash){throw ('Region benchmark source changed: '+$authorityEntry.Name)}
    }
}
[ordered]@{
    version='1.0.5';dllSha256=$authorityVerifiedHash;validatedAt=(Get-Date).ToString('o');tests=$authorityResults;native=$authorityNative
    benchmark=$authorityBenchmark;regionBenchmark=$authorityRegionBenchmark;import=$authorityImport;artifactHashes=$authorityHashes;liveMultiplayerTested=$false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Encoding UTF8
Write-Output ('Verified release SHA256: '+$authorityVerifiedHash)
