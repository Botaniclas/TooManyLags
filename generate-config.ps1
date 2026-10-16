param(
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx'
)
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskCore = Join-Path $BepInExDirectory 'core'
$taskBepInEx = Join-Path $taskCore 'BepInEx.dll'
$taskOutput = Join-Path $PSScriptRoot 'bin\config-generation'
foreach ($taskInput in @($taskCompiler, $taskBepInEx)) {
    if (!(Test-Path -LiteralPath $taskInput)) { throw "Missing config generation input: $taskInput" }
}
function Read-Source([string]$Path) { return [IO.File]::ReadAllText($Path).Replace("`r`n", "`n") }
function Extract-Region([string]$Source, [string]$Start, [string]$End) {
    $taskStart = $Source.IndexOf($Start, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Config source boundary missing: $Start" }
    $taskStart += $Start.Length
    $taskEnd = $Source.IndexOf($End, $taskStart, [StringComparison]::Ordinal)
    if ($taskEnd -lt 0) { throw "Config source boundary missing: $End" }
    return $Source.Substring($taskStart, $taskEnd - $taskStart).TrimEnd()
}
$taskPlugin = Read-Source (Join-Path $PSScriptRoot 'src\Plugin.cs')
$taskCombat = Read-Source (Join-Path $PSScriptRoot 'src\FgnCombatOwnership.cs')
$taskCatalog = Read-Source (Join-Path $PSScriptRoot 'src\ObjectCatalog.cs')
$taskGrid = Read-Source (Join-Path $PSScriptRoot 'src\AuthorityGrid.cs')
$taskPluginBindings = Extract-Region $taskPlugin "        private void Awake()`n        {`n" '            // Configuration ends here.'
$taskPluginBindings = $taskPluginBindings.Replace("            PrepareConfiguration();`n", "")
$taskCombatBindings = Extract-Region $taskCombat "        internal static void Install(ConfigFile config, Assembly assembly, System.Version version, bool allowInstall = true)`n        {`n" '            StartupError = null;'
$taskDescription = '        private static string ObjectDescription' + (Extract-Region $taskPlugin '        private static string ObjectDescription' '        private void RuntimeFault')
$taskKinds = [regex]::Match($taskCatalog, '(?s)\[Flags\]\s+internal enum Kinds\s*\{[^}]+\}').Value
$taskPattern = [regex]::Match($taskGrid, 'internal enum GridCoveragePattern\s*\{[^}]+\}').Value
$taskMetadata = [regex]::Match($taskPlugin, '\[BepInPlugin\(Id, "(?<name>[^"]+)", "(?<version>[^"]+)"\)\]')
$taskId = [regex]::Match($taskPlugin, 'internal const string Id = "(?<id>[^"]+)"').Groups['id'].Value
if (!$taskKinds -or !$taskPattern -or !$taskMetadata.Success -or $taskId -ne 'botanic.toomanylags.fgnaddon') {
    throw 'Config generator could not extract current enums or plugin metadata.'
}
if ($taskPluginBindings.Contains('Chainloader') -or $taskCombatBindings.Contains('sessionHarmony')) {
    throw 'Config extraction unexpectedly includes runtime startup hooks.'
}
$taskTemplate = Read-Source (Join-Path $PSScriptRoot 'tools\ConfigProbe.template.cs')
$taskProbe = $taskTemplate.Replace('@@KINDS@@', $taskKinds).Replace('@@GRID_PATTERN@@', $taskPattern).
    Replace('@@PLUGIN_BINDINGS@@', $taskPluginBindings).Replace('@@COMBAT_BINDINGS@@', $taskCombatBindings).
    Replace('@@OBJECT_DESCRIPTION@@', $taskDescription).Replace('@@PLUGIN_ID@@', $taskId).
    Replace('@@PLUGIN_NAME@@', $taskMetadata.Groups['name'].Value).Replace('@@PLUGIN_VERSION@@', $taskMetadata.Groups['version'].Value)
if ($taskProbe.Contains('@@')) { throw 'Unresolved config generator template token.' }
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskSource = Join-Path $taskOutput 'ConfigProbe.cs'
$taskExecutable = Join-Path $taskOutput 'ConfigProbe.exe'
[IO.File]::WriteAllText($taskSource, $taskProbe, (New-Object Text.UTF8Encoding($false)))
Copy-Item -LiteralPath $taskBepInEx -Destination $taskOutput -Force
& $taskCompiler '/nologo' '/target:exe' '/optimize+' '/langversion:5' '/warnaserror+' ('/reference:' + $taskBepInEx) ('/out:' + $taskExecutable) $taskSource (Join-Path $PSScriptRoot 'src\IntervalConfig.cs') (Join-Path $PSScriptRoot 'src\ConfigLayout.cs') (Join-Path $PSScriptRoot 'src\ConfigTemplate.cs')
if ($LASTEXITCODE -ne 0) { throw 'Actual BepInEx config generator compilation failed.' }
$taskFreshConfig = Join-Path $taskOutput ('defaults-' + [Guid]::NewGuid().ToString('N') + '.cfg')
& $taskExecutable $taskFreshConfig $taskCore
if ($LASTEXITCODE -ne 0) { throw 'Actual BepInEx config generation or default verification failed.' }
$taskFinalConfig = Join-Path $PSScriptRoot ($taskId + '.cfg')
Copy-Item -LiteralPath $taskFreshConfig -Destination $taskFinalConfig -Force
Remove-Item -LiteralPath $taskFreshConfig
Get-Item -LiteralPath $taskFinalConfig | Select-Object FullName, Length
