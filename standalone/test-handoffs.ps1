param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference = 'Stop'
$handoffCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$handoffCore = Join-Path $BepInExDirectory 'core'
$handoffOutput = Join-Path $PSScriptRoot 'bin\handoff-tests'
New-Item -ItemType Directory -Force -Path $handoffOutput | Out-Null
foreach ($handoffDependency in @('0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) { Copy-Item -LiteralPath (Join-Path $handoffCore $handoffDependency) -Destination $handoffOutput -Force }
$handoffExe = Join-Path $handoffOutput 'HandoffRuntimeTests.exe'
$handoffSources = @('src\LocalAgent.cs','src\Handoffs.cs','src\NativeOwnership.cs','src\HandoffTransaction.cs','src\RecipientReadiness.cs','src\RecipientRequirement.cs','src\RequestRouter.cs','src\IncineratorProtection.cs','src\FieldReader.cs','src\FriendlyCreatures.cs','tests\HandoffFixtures.cs','tests\HandoffRuntimeTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $handoffCompiler /nologo /target:exe /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 ('/reference:' + (Join-Path $handoffCore '0Harmony.dll')) ('/out:' + $handoffExe) $handoffSources
if ($LASTEXITCODE -ne 0) { throw 'Handoff fixture compilation failed.' }
& $handoffExe
if ($LASTEXITCODE -ne 0) { throw 'Production handoff tests failed.' }
$networkExe = Join-Path $handoffOutput 'NetworkBoundaryTests.exe'
$networkSources = @('src\Network.cs','src\NativeLatency.cs','src\NativeOwnership.cs','src\CameraHeading.cs','src\AuthorityGrid.cs','src\LocalAgent.cs','src\Handoffs.cs','src\HandoffTransaction.cs','src\RecipientReadiness.cs','src\RecipientRequirement.cs','src\RequestRouter.cs','src\IncineratorProtection.cs','src\FieldReader.cs','src\FriendlyCreatures.cs','tests\HandoffFixtures.cs','tests\NetworkBoundaryTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $handoffCompiler /nologo /target:exe /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 /define:NETWORK_AUDIT ('/reference:' + (Join-Path $handoffCore '0Harmony.dll')) ('/out:' + $networkExe) $networkSources
if ($LASTEXITCODE -ne 0) { throw 'Network boundary fixture compilation failed.' }
& $networkExe
if ($LASTEXITCODE -ne 0) { throw 'Production network boundary tests failed.' }
