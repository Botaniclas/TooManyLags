param([string]$OutputDirectory = (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'outputs'))
$ErrorActionPreference = 'Stop'
$packageVersion = '1.1.2'
$packageVerification = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Raw | ConvertFrom-Json
$packageManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$packageDll = Join-Path $PSScriptRoot 'bin\TooManyLagsStandalone.dll'
$packageHash = (Get-FileHash -LiteralPath $packageDll).Hash
if ($packageVerification.version -ne $packageVersion -or $packageVerification.dllSha256 -ne $packageHash -or [Reflection.AssemblyName]::GetAssemblyName($packageDll).Version.ToString(3) -ne $packageVersion) { throw 'DLL differs from verified release.' }
if ($packageManifest.version_number -ne $packageVersion) { throw 'Manifest version differs from verified release.' }
foreach ($packageArtifact in $packageVerification.artifactHashes.PSObject.Properties) { if ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $packageArtifact.Name)).Hash -ne $packageArtifact.Value) { throw ('Artifact changed: ' + $packageArtifact.Name) } }
foreach ($packageSource in $packageVerification.sourceHashes.PSObject.Properties) { if ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $packageSource.Name)).Hash -ne $packageSource.Value) { throw ('Source changed after verification: ' + $packageSource.Name) } }
foreach ($packageTest in $packageVerification.tests.PSObject.Properties) { if (!$packageTest.Value.passed) { throw 'Tests failed.' } }
$packageStage = Join-Path $PSScriptRoot 'release-thunderstore'
$packageConfig = Join-Path $PSScriptRoot 'release-config\BepInEx\config'
New-Item -ItemType Directory -Force -Path $OutputDirectory,$packageStage,$packageConfig | Out-Null
$packageNames = @('TooManyLagsStandalone.dll','README.md','CHANGELOG.md','manifest.json','icon.png','LICENSE','THIRD_PARTY_NOTICES.md')
Copy-Item -LiteralPath $packageDll -Destination $packageStage -Force
foreach ($packageName in $packageNames | Where-Object { $_ -ne 'TooManyLagsStandalone.dll' }) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $packageName) -Destination $packageStage -Force }
$packageStem = 'TooManyLags-Standalone-' + $packageVersion
$packageZip = Join-Path $OutputDirectory ($packageStem + '.zip')
Compress-Archive -LiteralPath @($packageNames | ForEach-Object { Join-Path $packageStage $_ }) -DestinationPath $packageZip -Force
$packageSources = @('src','tests','tools','docs','AGENTS.md','README.md','CHANGELOG.md','VALIDATION.md','manifest.json','icon.png','LICENSE','THIRD_PARTY_NOTICES.md','botanic.toomanylags.standalone.cfg','build.ps1','generate-config.ps1','verify.ps1','package.ps1','publish.ps1','thunderstore.toml','organize-outputs.ps1','verification.json') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$packageSources += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'test-*.ps1' -File | Sort-Object Name | Select-Object -ExpandProperty FullName)
Compress-Archive -LiteralPath $packageSources -DestinationPath (Join-Path $OutputDirectory ($packageStem + '-source.zip')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'botanic.toomanylags.standalone.cfg') -Destination $packageConfig -Force
Compress-Archive -LiteralPath (Join-Path $PSScriptRoot 'release-config\BepInEx') -DestinationPath (Join-Path $OutputDirectory ($packageStem + '-config.zip')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'botanic.toomanylags.standalone.cfg') -Destination $OutputDirectory -Force
Copy-Item -LiteralPath $packageDll -Destination (Join-Path $OutputDirectory ($packageStem + '.dll')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $OutputDirectory ($packageStem + '-README.md')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VALIDATION.md') -Destination (Join-Path $OutputDirectory ($packageStem + '-Verification.md')) -Force
$packageWiki = Join-Path $PSScriptRoot 'docs\wiki'
if (Test-Path -LiteralPath $packageWiki -PathType Container) {
    $packageWikiOutput = Join-Path $OutputDirectory ($packageStem + '-Wiki')
    New-Item -ItemType Directory -Force -Path $packageWikiOutput | Out-Null
    $packageWikiFiles = @(Get-ChildItem -LiteralPath $packageWiki -Filter '*.md' -File | Sort-Object Name | Select-Object -ExpandProperty FullName)
    Copy-Item -LiteralPath $packageWikiFiles -Destination $packageWikiOutput -Force
    Compress-Archive -LiteralPath $packageWikiFiles -DestinationPath (Join-Path $OutputDirectory ($packageStem + '-wiki.zip')) -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageArchive = [IO.Compression.ZipFile]::OpenRead($packageZip)
try {
    if (Compare-Object @($packageArchive.Entries.FullName | Sort-Object) @($packageNames | Sort-Object)) { throw 'Unexpected package contents.' }
    $packageStream = $packageArchive.GetEntry('TooManyLagsStandalone.dll').Open()
    try { $packageSha = [Security.Cryptography.SHA256]::Create(); try { $packagePacked = [BitConverter]::ToString($packageSha.ComputeHash($packageStream)).Replace('-','') } finally { $packageSha.Dispose() } } finally { $packageStream.Dispose() }
    if ($packagePacked -ne $packageHash) { throw 'Packaged DLL hash differs.' }
    foreach ($packageText in @('README.md','CHANGELOG.md','manifest.json')) {
        $packageReader = [IO.StreamReader]::new($packageArchive.GetEntry($packageText).Open())
        try {
            if ($packageReader.ReadToEnd() -cne [IO.File]::ReadAllText((Join-Path $PSScriptRoot $packageText))) { throw ('Packaged text differs: ' + $packageText) }
        } finally { $packageReader.Dispose() }
    }
} finally { $packageArchive.Dispose() }
& (Join-Path $PSScriptRoot 'organize-outputs.ps1') -OutputDirectory $OutputDirectory -Project 'TooManyLags-Standalone'
Get-ChildItem -LiteralPath $OutputDirectory -Filter ($packageStem + '*') | Select-Object Name,Length
