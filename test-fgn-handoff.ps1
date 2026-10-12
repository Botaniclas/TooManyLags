param(
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$FgnCache = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\cache\VerdantsAscent-FiresGhettoNetworking'
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$managed = Join-Path $ValheimDirectory 'valheim_Data\Managed'
$output = Join-Path $PSScriptRoot 'bin\handoff-tests'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$source = Join-Path $PSScriptRoot 'src\FgnOwnerHandoff.cs'
$tests = Join-Path $PSScriptRoot 'tests\FgnOwnerHandoffTests.cs'
$executable = Join-Path $output 'FgnOwnerHandoffTests.exe'
& $compiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/out:' + $executable) $source $tests
if ($LASTEXITCODE -ne 0) { throw 'FGN handoff state test build failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'FGN handoff state tests failed.' }

$identified = Join-Path $output 'IdentifiedGridHandoffTests.exe'
& $compiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 /main:IdentifiedGridHandoffTests ('/out:' + $identified) $source $tests (Join-Path $PSScriptRoot 'src\GridCommitFence.cs') (Join-Path $PSScriptRoot 'tests\IdentifiedGridHandoffTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Identified grid handoff build failed.' }
& $identified
if ($LASTEXITCODE -ne 0) { throw 'Identified grid handoff tests failed.' }

$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll',
    'UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'assembly_valheim.dll', 'assembly_utils.dll',
    'SoftReferenceableAssets.dll') | ForEach-Object { Join-Path $managed $_ }
$production = Join-Path $output 'FgnOwnerHandoff-production.dll'
$options = @('/nologo', '/noconfig', '/nostdlib+', '/target:library', '/optimize+', '/warnaserror+',
    '/langversion:5', ('/out:' + $production))
$options += $references | ForEach-Object { '/reference:' + $_ }
$options += $source
& $compiler @options
if ($LASTEXITCODE -ne 0) { throw 'FGN handoff actual-game source build failed.' }

$auditSource = Join-Path $PSScriptRoot 'tests\FgnOwnerHandoffContractAudit.cs'
$audit = Join-Path $output 'FgnOwnerHandoffContractAudit.exe'
& $compiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/out:' + $audit) $auditSource
if ($LASTEXITCODE -ne 0) { throw 'FGN handoff contract audit build failed.' }
foreach ($version in @('1.5.36', '1.5.41')) {
    $fgn = Join-Path $FgnCache ($version + '\VAGhettoNetworking.dll')
    & $audit $production $managed (Join-Path $BepInExDirectory 'core') $fgn
    if ($LASTEXITCODE -ne 0) { throw ('Actual FGN handoff contract failed: ' + $version) }
}
