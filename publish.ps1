param(
    [string]$PackagePath,
    [string]$CliDll = (Join-Path (Split-Path $PSScriptRoot) 'tools\thunderstore-cli\package\tools\net7.0\any\tcli.dll'),
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
if (-not $PackagePath) {
    $PackagePath = Join-Path (Split-Path (Split-Path $PSScriptRoot)) ('outputs\TooManyLags-FGNAddon-Combined-' + $manifest.version_number + '.zip')
}
$publishInputs = @($PackagePath)
if (!$ValidateOnly) { $publishInputs += $CliDll }
foreach ($inputFile in $publishInputs) {
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw ('Missing publish input: ' + $inputFile) }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    $reader = New-Object IO.StreamReader($archive.GetEntry('manifest.json').Open())
    try { $packedManifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if (($packedManifest | ConvertTo-Json -Depth 8 -Compress) -cne ($manifest | ConvertTo-Json -Depth 8 -Compress)) {
        throw 'Package manifest differs from the project manifest.'
    }
    $verification = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Raw | ConvertFrom-Json
    if ($verification.debugSystem -or !$verification.releaseVerified) { throw 'Publication requires verify.ps1 -Release; debug development builds cannot be published.' }
    $dllStream = $archive.GetEntry('TooManyLags.dll').Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $dllHash = [BitConverter]::ToString($sha.ComputeHash($dllStream)).Replace('-', '') } finally { $sha.Dispose(); $dllStream.Dispose() }
    if ($verification.version -ne $manifest.version_number -or $dllHash -ne $verification.dllSha256) { throw 'Package DLL is not the verified release.' }
} finally { $archive.Dispose() }
if ($ValidateOnly) { Write-Output 'Release package validated; no network access or publication performed.'; return }
$remotePackage = Invoke-RestMethod -Uri ('https://thunderstore.io/api/experimental/package/Botaniclas/' + $manifest.name + '/')
if ([Version]$remotePackage.latest.version_number -ge [Version]$manifest.version_number) {
    throw ('Thunderstore already has version ' + $remotePackage.latest.version_number + '. No upload performed.')
}
$sharedCredentialHelper = Join-Path (Split-Path $PSScriptRoot) 'tools\thunderstore-cli\ThunderstoreCredentials.ps1'
$publishAction = {
    & dotnet $CliDll publish --file $PackagePath --config-path (Join-Path $PSScriptRoot 'thunderstore.toml') --tcli-directory (Join-Path (Split-Path $CliDll) 'local-data')
    if ($LASTEXITCODE -ne 0) { throw 'Thunderstore CLI publishing failed.' }
}
$previousToken = $env:TCLI_AUTH_TOKEN
if ([string]::IsNullOrWhiteSpace($previousToken) -and (Test-Path -LiteralPath $sharedCredentialHelper -PathType Leaf)) {
    . $sharedCredentialHelper
    if (Test-Path -LiteralPath $script:BotanicThunderstoreTokenPath -PathType Leaf) {
        Invoke-WithBotanicThunderstoreToken -Action $publishAction
        return
    }
}
try {
    if ([string]::IsNullOrWhiteSpace($previousToken)) {
        $secureToken = Read-Host 'Thunderstore service-account token for Botaniclas (hidden; not saved)' -AsSecureString
        $tokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
        try { $env:TCLI_AUTH_TOKEN = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenPointer).Trim() }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenPointer); $secureToken.Dispose() }
        if ([string]::IsNullOrWhiteSpace($env:TCLI_AUTH_TOKEN)) { throw 'No token entered.' }
    }
    & $publishAction
} finally { $env:TCLI_AUTH_TOKEN = $previousToken }
