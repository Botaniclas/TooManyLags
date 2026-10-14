$ErrorActionPreference='Stop'
$authorityOutput=Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'outputs'
$authorityRelease=Join-Path $PSScriptRoot 'release-thunderstore'
$authorityConfigRelease=Join-Path $PSScriptRoot 'release-config\BepInEx\config'
$authorityDll=Join-Path $PSScriptRoot 'bin\TooManyLags.dll'
$authorityVerification=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Raw | ConvertFrom-Json
$authorityManifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$authorityVersion='1.0.40'
$authorityStem='TooManyLags-FGNAddon-'+$authorityVersion
$authorityHash=(Get-FileHash -LiteralPath $authorityDll -Algorithm SHA256).Hash
if($authorityVerification.version -ne $authorityVersion -or $authorityVerification.dllSha256 -ne $authorityHash){throw 'DLL differs from verified build.'}
if($authorityManifest.version_number -ne $authorityVersion -or [Reflection.AssemblyName]::GetAssemblyName($authorityDll).Version.ToString(3) -ne $authorityVersion){throw 'Version mismatch.'}
foreach($authorityItem in $authorityVerification.artifactHashes.PSObject.Properties){
    if($authorityItem.Value -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $authorityItem.Name) -Algorithm SHA256).Hash){throw ('Artifact changed after verification: '+$authorityItem.Name)}
}
New-Item -ItemType Directory -Force -Path $authorityRelease,$authorityConfigRelease,$authorityOutput | Out-Null
Copy-Item -LiteralPath $authorityDll -Destination $authorityRelease -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README-Thunderstore.md') -Destination (Join-Path $authorityRelease 'README.md') -Force
foreach($authorityFile in @('CHANGELOG.md','manifest.json','icon.png')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $authorityFile) -Destination $authorityRelease -Force}
$authorityFiles=@('TooManyLags.dll','README.md','CHANGELOG.md','manifest.json','icon.png') | ForEach-Object {Join-Path $authorityRelease $_}
$authorityZip=Join-Path $authorityOutput ($authorityStem+'.zip')
Compress-Archive -LiteralPath $authorityFiles -DestinationPath $authorityZip -Force
$authoritySources=@('src','tests','tools','README.md','VALIDATION.md','manifest.json','icon.png','botanic.toomanylags.fgnaddon.cfg','generate-config.ps1','build.ps1','verify.ps1','test-inventory.ps1','test-fgn-handoff.ps1','test-integration.ps1','test-directional.ps1','test-field-cache.ps1','test-regions.ps1','test-native.ps1','test-combat.ps1','test-combat-mono.ps1','test-combat-bridge.ps1','test-combat-readiness.ps1','benchmark.ps1','benchmark-regions.ps1','package.ps1','verification.json') | ForEach-Object {Join-Path $PSScriptRoot $_}
$authoritySources+=Join-Path $PSScriptRoot 'CHANGELOG.md'
$authoritySources+=Join-Path $PSScriptRoot 'test-config-console.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-camera-recovery.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-recipient-scope.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'TYPING_PROTECTION_DESIGN.md'
$authoritySources+=Join-Path $PSScriptRoot 'README-Thunderstore.md'
$authoritySources+=Join-Path $PSScriptRoot 'docs'
$authoritySources+=Join-Path $PSScriptRoot 'thunderstore.toml'
$authoritySources+=Join-Path $PSScriptRoot 'publish.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-fgn-settings.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-player-simulation.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-grid-load.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-grid-size.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-mob-precedence.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'organize-outputs.ps1'
$authorityRules=Join-Path $PSScriptRoot 'AGENTS.md'
if(-not (Test-Path -LiteralPath $authorityRules -PathType Leaf)){$authorityRules=Join-Path (Split-Path $PSScriptRoot) 'AGENTS.md'}
$authoritySources+=$authorityRules
Compress-Archive -LiteralPath $authoritySources -DestinationPath (Join-Path $authorityOutput ($authorityStem+'-source.zip')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'botanic.toomanylags.fgnaddon.cfg') -Destination $authorityConfigRelease -Force
Compress-Archive -LiteralPath (Join-Path $PSScriptRoot 'release-config\BepInEx') -DestinationPath (Join-Path $authorityOutput ($authorityStem+'-config.zip')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'botanic.toomanylags.fgnaddon.cfg') -Destination (Join-Path $authorityOutput 'botanic.toomanylags.fgnaddon.cfg') -Force
Copy-Item -LiteralPath $authorityDll -Destination (Join-Path $authorityOutput ($authorityStem+'.dll')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README-Thunderstore.md') -Destination (Join-Path $authorityOutput ($authorityStem+'-README.md')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VALIDATION.md') -Destination (Join-Path $authorityOutput ($authorityStem+'-Verification.md')) -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$authorityArchive=[IO.Compression.ZipFile]::OpenRead($authorityZip)
try{
    $authorityExpected=@('TooManyLags.dll','README.md','CHANGELOG.md','manifest.json','icon.png') | Sort-Object
    if(Compare-Object @($authorityArchive.Entries.FullName | Sort-Object) $authorityExpected){throw 'Release template entries differ.'}
    $authorityStream=$authorityArchive.GetEntry('TooManyLags.dll').Open()
    try{
        $authoritySha=[Security.Cryptography.SHA256]::Create()
        try{$authorityPacked=[BitConverter]::ToString($authoritySha.ComputeHash($authorityStream)).Replace('-','')}finally{$authoritySha.Dispose()}
    }finally{$authorityStream.Dispose()}
    if($authorityPacked -ne $authorityHash){throw 'Packaged DLL differs from verified DLL.'}
    foreach($authorityText in @('README.md','CHANGELOG.md','manifest.json')){
        $authorityReader=New-Object IO.StreamReader($authorityArchive.GetEntry($authorityText).Open())
        $authorityTextSource=if($authorityText -eq 'README.md'){'README-Thunderstore.md'}else{$authorityText}
        try{if($authorityReader.ReadToEnd() -cne [IO.File]::ReadAllText((Join-Path $PSScriptRoot $authorityTextSource))){throw ('Packaged content differs: '+$authorityText)}}finally{$authorityReader.Dispose()}
    }
}finally{$authorityArchive.Dispose()}
$authorityConfigArchive=[IO.Compression.ZipFile]::OpenRead((Join-Path $authorityOutput ($authorityStem+'-config.zip')))
try{
    if($authorityConfigArchive.Entries.Count -ne 1){throw 'Config ZIP has unexpected entries.'}
    $authorityConfigEntry=$authorityConfigArchive.Entries[0]
    if($authorityConfigEntry.FullName.Replace('\','/') -cne 'BepInEx/config/botanic.toomanylags.fgnaddon.cfg'){throw 'Config ZIP path differs.'}
    $authorityReader=New-Object IO.StreamReader($authorityConfigEntry.Open())
    try{if($authorityReader.ReadToEnd() -cne [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'botanic.toomanylags.fgnaddon.cfg'))){throw 'Packaged config differs.'}}finally{$authorityReader.Dispose()}
}finally{$authorityConfigArchive.Dispose()}
& (Join-Path $PSScriptRoot 'organize-outputs.ps1') -OutputDirectory $authorityOutput -Project 'TooManyLags-FGNAddon'
Get-ChildItem -LiteralPath $authorityOutput -Filter ($authorityStem+'*') | Select-Object Name,Length
