param(
    [string]$PackagePath,
    [string]$CliDll = (Join-Path (Split-Path $PSScriptRoot) 'tools\thunderstore-cli\package\tools\net7.0\any\tcli.dll')
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
if (-not $PackagePath) {
    $PackagePath = Join-Path (Split-Path (Split-Path $PSScriptRoot)) ('outputs\TooManyLags-Standalone-' + $manifest.version_number + '.zip')
}
foreach ($inputFile in @($PackagePath, $CliDll)) {
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw ('Missing publish input: ' + $inputFile) }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    foreach ($artifact in @('manifest.json','README.md','CHANGELOG.md')) {
        $entry = $archive.GetEntry($artifact)
        if (-not $entry) { throw ('Package entry missing: ' + $artifact) }
        $reader = New-Object IO.StreamReader($entry.Open())
        try {
            if ($reader.ReadToEnd() -cne [IO.File]::ReadAllText((Join-Path $PSScriptRoot $artifact))) { throw ('Packaged artifact differs: ' + $artifact) }
        } finally { $reader.Dispose() }
    }
    $verification = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Raw | ConvertFrom-Json
    $dllStream = $archive.GetEntry('TooManyLagsStandalone.dll').Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $dllHash = [BitConverter]::ToString($sha.ComputeHash($dllStream)).Replace('-', '') } finally { $sha.Dispose(); $dllStream.Dispose() }
    if ($verification.version -ne $manifest.version_number -or $dllHash -ne $verification.dllSha256) { throw 'Package DLL is not the verified release.' }
    foreach ($suite in $verification.tests.PSObject.Properties) { if (-not $suite.Value.passed) { throw ('Verification suite failed: ' + $suite.Name) } }
} finally { $archive.Dispose() }
$remotePackage = Invoke-RestMethod -Uri ('https://thunderstore.io/api/experimental/package/Botaniclas/' + $manifest.name + '/?releaseCheck=' + [DateTime]::UtcNow.Ticks)
if ([Version]$remotePackage.latest.version_number -ge [Version]$manifest.version_number) {
    throw ('Thunderstore already has version ' + $remotePackage.latest.version_number + '. No upload performed.')
}
$helper = Join-Path (Split-Path $PSScriptRoot) 'tools\thunderstore-cli\ThunderstoreCredentials.ps1'
if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) { throw 'Shared credential helper is missing.' }
. $helper
Invoke-WithBotanicThunderstoreToken -Action {
    & dotnet $CliDll publish --file $PackagePath --config-path (Join-Path $PSScriptRoot 'thunderstore.toml') --tcli-directory (Join-Path (Split-Path $CliDll) 'local-data')
    if ($LASTEXITCODE -ne 0) { throw 'Thunderstore CLI publishing failed.' }
}
