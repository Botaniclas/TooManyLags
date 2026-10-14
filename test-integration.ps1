param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',[switch]$GridProtocol)
$ErrorActionPreference='Stop'
$authorityCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$authorityTestOutput=Join-Path $PSScriptRoot 'bin\integration-tests'
New-Item -ItemType Directory -Force -Path $authorityTestOutput | Out-Null
$authorityHarmony=Join-Path $BepInExDirectory 'core\0Harmony.dll'
Copy-Item -LiteralPath $authorityHarmony -Destination $authorityTestOutput -Force
Get-ChildItem -LiteralPath (Join-Path $BepInExDirectory 'core') -Filter 'Mono*.dll' | Copy-Item -Destination $authorityTestOutput -Force
$authorityGridExe=Join-Path $authorityTestOutput 'GridTests.exe'
& $authorityCompiler /nologo /optimize+ /warnaserror+ /langversion:5 ('/out:'+$authorityGridExe) (Join-Path $PSScriptRoot 'src\AuthorityGrid.cs') (Join-Path $PSScriptRoot 'tests\GridTests.cs')
if($LASTEXITCODE -ne 0){throw 'Grid compile failed.'}
& $authorityGridExe
if($LASTEXITCODE -ne 0){throw 'Grid tests failed.'}
$authoritySources=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Where-Object { $_.Name -notin @('Plugin.cs','GridHandoffProtocol.cs','ConfigCommands.cs','ConfigConsole.cs','ServerSession.cs','FgnSessionSettings.cs','MobPolicyHandoff.cs','FgnCombatOwnership.cs','FgnMobHandoff.cs','FgnBossPolicy.cs','FgnCombatBridge.cs','FgnCombatStatus.cs','FgnTargetEvents.cs','FgnAttackRetry.cs','FieldReader.cs','NativePup.cs','CellReadiness.cs') } | Select-Object -ExpandProperty FullName
$authorityFixtures=@('RuntimeFixtures.cs','ExtraFixtures.cs','InteractionFixtures.cs','FgnRpcFixtures.cs','IntegrationTests.cs') | ForEach-Object {Join-Path $PSScriptRoot ('tests\'+$_)}
if($GridProtocol){$authoritySources+=Join-Path $PSScriptRoot 'src\GridHandoffProtocol.cs';$authorityFixtures+=Join-Path $PSScriptRoot 'tests\GridLoadProtocolTests.cs'}
$authorityIntegrationExe=Join-Path $authorityTestOutput 'IntegrationTests.exe'
$authorityDefines=@();if($GridProtocol){$authorityDefines+='/define:GRID_PROTOCOL'}
& $authorityCompiler /nologo /optimize+ /warnaserror+ /langversion:5 @authorityDefines ('/reference:'+$authorityHarmony) ('/out:'+$authorityIntegrationExe) @authoritySources @authorityFixtures
if($LASTEXITCODE -ne 0){throw 'Integration compile failed.'}
& $authorityIntegrationExe
if($LASTEXITCODE -ne 0){throw 'Integration tests failed.'}
