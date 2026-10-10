param(
    [string]$BaselineDirectory=(Join-Path $PSScriptRoot '..\TooManyLags-3.6.0'),
    [string]$BepInExDirectory='C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx'
)
$ErrorActionPreference='Stop'
$authorityCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$authorityOutput=Join-Path $PSScriptRoot 'bin\region-benchmark'
$authorityBaselineOutput=Join-Path $authorityOutput 'baseline'
New-Item -ItemType Directory -Force -Path $authorityBaselineOutput | Out-Null
$authorityHarmony=Join-Path $BepInExDirectory 'core\0Harmony.dll'
Copy-Item -LiteralPath $authorityHarmony -Destination $authorityOutput -Force
Get-ChildItem -LiteralPath (Join-Path $BepInExDirectory 'core') -Filter 'Mono*.dll' | Copy-Item -Destination $authorityOutput -Force
$authoritySources=@()
foreach($authorityFile in (Get-ChildItem -LiteralPath (Join-Path $BaselineDirectory 'src') -Filter '*.cs' | Where-Object Name -ne 'Plugin.cs')){
    $authorityCopy=Join-Path $authorityBaselineOutput $authorityFile.Name
    [IO.File]::WriteAllText($authorityCopy,[IO.File]::ReadAllText($authorityFile.FullName).Replace('namespace TooManyLags','namespace AuthorityBaseline'))
    $authoritySources+=$authorityCopy
}
$authoritySources+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Where-Object Name -ne 'Plugin.cs' | Select-Object -ExpandProperty FullName
$authoritySources+=@('RuntimeFixtures.cs','ExtraFixtures.cs','InteractionFixtures.cs','FgnRpcFixtures.cs','IntegrationTests.cs','RegionControllerBenchmark.cs') | ForEach-Object {Join-Path $PSScriptRoot ('tests\'+$_)}
$authorityExecutable=Join-Path $authorityOutput 'RegionControllerBenchmark.exe'
& $authorityCompiler /nologo /optimize+ /warnaserror+ /langversion:5 /main:RegionControllerBenchmark ('/reference:'+$authorityHarmony) ('/out:'+$authorityExecutable) @authoritySources
if($LASTEXITCODE -ne 0){throw 'Region benchmark compile failed.'}
& $authorityExecutable | Tee-Object -FilePath (Join-Path $authorityOutput 'results.txt')
if($LASTEXITCODE -ne 0){throw 'Region benchmark failed.'}
$authorityHashes=@{};foreach($authorityFile in (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs')){$authorityHashes[$authorityFile.Name]=(Get-FileHash -LiteralPath $authorityFile.FullName -Algorithm SHA256).Hash}
[ordered]@{version='1.0.7';synthetic=$true;baseline='3.6.0';recordedAt=(Get-Date).ToString('o');sourceHashes=$authorityHashes;output=[IO.File]::ReadAllText((Join-Path $authorityOutput 'results.txt'))} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $authorityOutput 'results.json') -Encoding UTF8
