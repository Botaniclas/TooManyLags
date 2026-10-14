param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'test-integration.ps1') -BepInExDirectory $BepInExDirectory -GridProtocol
