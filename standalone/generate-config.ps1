param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference = 'Stop'
$configCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$configCore = Join-Path $BepInExDirectory 'core'
$configOutput = Join-Path $PSScriptRoot 'bin\config-generation'
New-Item -ItemType Directory -Force -Path $configOutput | Out-Null
$configKinds = [regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src\ObjectCatalog.cs')), '(?s)\[Flags\]\s+internal enum Kinds\s*\{[^}]+\}').Value
$configPattern = [regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src\AuthorityGrid.cs')), 'internal enum GridCoveragePattern\s*\{[^}]+\}').Value
if (!$configKinds -or !$configPattern) { throw 'Production config enum extraction failed.' }
$configSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tools\ConfigProbe.template.cs')).Replace('@@KINDS@@', $configKinds).Replace('@@PATTERN@@', $configPattern)
$configProbe = Join-Path $configOutput 'ConfigProbe.cs'
[IO.File]::WriteAllText($configProbe, $configSource)
$configBepInEx = Join-Path $configCore 'BepInEx.dll'
Copy-Item -LiteralPath $configBepInEx -Destination $configOutput -Force
$configExe = Join-Path $configOutput 'ConfigProbe.exe'
& $configCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/reference:' + $configBepInEx) ('/out:' + $configExe) $configProbe (Join-Path $PSScriptRoot 'src\Settings.cs') (Join-Path $PSScriptRoot 'src\ConfigLayout.cs') (Join-Path $PSScriptRoot 'src\ConfigTemplate.cs')
if ($LASTEXITCODE -ne 0) { throw 'Config probe compilation failed.' }
$configFresh = Join-Path $configOutput ('defaults-' + [Guid]::NewGuid().ToString('N') + '.cfg')
& $configExe $configFresh $configCore
if ($LASTEXITCODE -ne 0) { throw 'Production config probe failed.' }
Copy-Item -LiteralPath $configFresh -Destination (Join-Path $PSScriptRoot 'botanic.toomanylags.standalone.cfg') -Force
Remove-Item -LiteralPath $configFresh
