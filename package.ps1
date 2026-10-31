param([string]$OutputDirectory = (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'outputs'),[switch]$Release)
$ErrorActionPreference='Stop'
$authorityOutput=[IO.Path]::GetFullPath($OutputDirectory)
$authorityRelease=Join-Path $PSScriptRoot 'release-thunderstore'
$authorityConfigRelease=Join-Path $PSScriptRoot 'release-config\BepInEx\config'
$authorityModeFolder=if($Release){'bin'}else{'bin/debug-system'}
$authorityDll=Join-Path $PSScriptRoot ($authorityModeFolder+'/TooManyLags.dll')
$authorityReadme=if($Release){'README-Thunderstore.md'}else{'README-TEST.md'}
$authorityVerification=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Raw | ConvertFrom-Json
$authorityManifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$authorityVersion='2.0.0'
$authorityStem='TooManyLags-FGNAddon-Combined-'+$authorityVersion
foreach($authoritySuffix in @('.zip','-debug-system.zip','-source.zip','-config.zip','-wiki.zip')){
    if(Test-Path -LiteralPath (Join-Path $authorityOutput ($authorityStem+$authoritySuffix))){
        throw 'Versioned output already exists. Preserve the previous candidate before packaging.'
    }
}
$authorityHash=(Get-FileHash -LiteralPath $authorityDll -Algorithm SHA256).Hash
if($Release -and (!$authorityVerification.releaseVerified -or $authorityVerification.debugSystem)){throw 'Run verify.ps1 -Release before packaging a release.'}
$authorityExpectedHash=if($Release){$authorityVerification.dllSha256}else{$authorityVerification.debugDllSha256}
if($authorityVerification.version -ne $authorityVersion -or $authorityExpectedHash -ne $authorityHash){throw 'DLL differs from verified build.'}
$authorityMode=Get-Content -LiteralPath (Join-Path $PSScriptRoot ($authorityModeFolder+'/build-mode.json')) -Raw|ConvertFrom-Json
if($authorityMode.debugSystem -ne (!$Release) -or $authorityMode.dllSha256 -ne $authorityHash){throw 'Requested package mode differs from verified build.'}
$authorityDebugDll=Join-Path $PSScriptRoot 'bin/debug-system/TooManyLags.dll'
$authorityDebugHash=(Get-FileHash -LiteralPath $authorityDebugDll -Algorithm SHA256).Hash
$authorityDebugMode=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bin/debug-system/build-mode.json') -Raw|ConvertFrom-Json
if(!$authorityDebugMode.debugSystem -or $authorityDebugMode.dllSha256 -ne $authorityDebugHash -or $authorityVerification.debugDllSha256 -ne $authorityDebugHash){throw 'Debug DLL differs from verified debug build.'}
if($authorityManifest.version_number -ne $authorityVersion -or [Reflection.AssemblyName]::GetAssemblyName($authorityDll).Version.ToString(3) -ne $authorityVersion){throw 'Version mismatch.'}
foreach($authorityItem in $authorityVerification.artifactHashes.PSObject.Properties){
    if($authorityItem.Value -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $authorityItem.Name) -Algorithm SHA256).Hash){throw ('Artifact changed after verification: '+$authorityItem.Name)}
}
foreach($authorityItem in $authorityVerification.sourceHashes.PSObject.Properties){
    if($authorityItem.Value -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('src\'+$authorityItem.Name)) -Algorithm SHA256).Hash){throw ('Source changed after verification: '+$authorityItem.Name)}
}
New-Item -ItemType Directory -Force -Path $authorityRelease,$authorityConfigRelease,$authorityOutput | Out-Null
Copy-Item -LiteralPath $authorityDll -Destination $authorityRelease -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot $authorityReadme) -Destination (Join-Path $authorityRelease 'README.md') -Force
foreach($authorityFile in @('CHANGELOG.md','manifest.json','icon.png')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $authorityFile) -Destination $authorityRelease -Force}
$authorityFiles=@('TooManyLags.dll','README.md','CHANGELOG.md','manifest.json','icon.png') | ForEach-Object {Join-Path $authorityRelease $_}
$authorityZip=Join-Path $authorityOutput ($authorityStem+'.zip')
Compress-Archive -LiteralPath $authorityFiles -DestinationPath $authorityZip -Force
$authorityDebugRelease=Join-Path $PSScriptRoot 'release-debug-system'
New-Item -ItemType Directory -Force -Path $authorityDebugRelease|Out-Null
Copy-Item -LiteralPath $authorityDebugDll -Destination $authorityDebugRelease -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README-TEST.md') -Destination (Join-Path $authorityDebugRelease 'README.md') -Force
foreach($authorityFile in @('CHANGELOG.md','manifest.json','icon.png')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $authorityFile) -Destination $authorityDebugRelease -Force}
$authorityDebugFiles=@('TooManyLags.dll','README.md','CHANGELOG.md','manifest.json','icon.png')|ForEach-Object{Join-Path $authorityDebugRelease $_}
$authorityDebugZip=Join-Path $authorityOutput ($authorityStem+'-debug-system.zip')
Compress-Archive -LiteralPath $authorityDebugFiles -DestinationPath $authorityDebugZip -Force
$authoritySources=@('src','tests','tools','README.md','VALIDATION.md','manifest.json','icon.png','botanic.toomanylags.fgnaddon.cfg','generate-config.ps1','build.ps1','verify.ps1','test-inventory.ps1','test-fgn-handoff.ps1','test-integration.ps1','test-directional.ps1','test-field-cache.ps1','test-regions.ps1','test-native.ps1','test-combat.ps1','test-combat-mono.ps1','test-combat-bridge.ps1','test-combat-readiness.ps1','test-mob-visual.ps1','benchmark.ps1','benchmark-regions.ps1','package.ps1','verification.json') | ForEach-Object {Join-Path $PSScriptRoot $_}
$authoritySources+=Join-Path $PSScriptRoot 'CHANGELOG.md'
$authoritySources+=Join-Path $PSScriptRoot 'SIDE-PHYSICS.md'
$authoritySources+=Join-Path $PSScriptRoot 'NETWORK-BATCHING.md'
$authoritySources+=Join-Path $PSScriptRoot 'NETWORK-STATE.md'
$authoritySources+=Join-Path $PSScriptRoot 'bandwidth-provenance.json'
$authoritySources+=Join-Path $PSScriptRoot 'pending-provenance.json'
$authoritySources+=Join-Path $PSScriptRoot 'OBJECT-LIFECYCLE-AUDIT.md'
$authoritySources+=Join-Path $PSScriptRoot 'OWNERSHIP-GUARDS.md'
$authoritySources+=Join-Path $PSScriptRoot 'test-state-transfer.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'README-TEST.md'
$authoritySources+=Join-Path $PSScriptRoot 'merge-provenance.json'
$authoritySources+=Join-Path $PSScriptRoot 'merge-71-provenance.json'
$authoritySources+=Join-Path $PSScriptRoot 'visual-76-provenance.json'
$authoritySources+=Join-Path $PSScriptRoot 'visual-77-provenance.json'
$authoritySources+=Join-Path $PSScriptRoot 'latency-78-provenance.json'
$authoritySources+=Join-Path $PSScriptRoot 'test-solo-latency.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-debug-build.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-build-policy.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-mob-combat-state.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-mob-status-state.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-mob-source.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-mob-observer.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-mob-bridge.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-animation-passthrough.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-bridge-performance.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-bridge-timing.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-bridge-continuity.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'release-input.json'
$authoritySources+=Join-Path $PSScriptRoot 'release-plan.json'
$authoritySources+=Join-Path $PSScriptRoot 'test-bridge-continuity.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'inspect-mob-bridge-fields.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'MOB-CONTINUITY.md'
$authoritySources+=Join-Path $PSScriptRoot 'debug-build-79-provenance.json'
$authoritySources+=Join-Path $PSScriptRoot 'flight-80-provenance.json'
$authoritySources+=Join-Path $PSScriptRoot 'diagnostic-81-provenance.json'
$authoritySources+=Join-Path $PSScriptRoot 'test-diagnostic-cost.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-flight-handoff.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'merge-71-files.json'
$authoritySources+=Join-Path $PSScriptRoot 'test-pup-compatibility.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'audit-pup-native.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-config-console.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-text-editing.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-loose-items.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-config-layout.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-camera-recovery.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-recipient-scope.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'TYPING_PROTECTION_DESIGN.md'
$authoritySources+=Join-Path $PSScriptRoot 'README-Thunderstore.md'
$authoritySources+=Join-Path $PSScriptRoot 'docs'
$authoritySources+=Join-Path $PSScriptRoot 'thunderstore.toml'
$authoritySources+=Join-Path $PSScriptRoot 'publish.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-fgn-settings.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-communitypatch.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-player-simulation.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-grid-load.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-grid-size.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-grid-scheduling.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'audit-priority-native.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'audit-field-batch.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'benchmark-field-batch.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'test-mob-precedence.ps1'
$authoritySources+=Join-Path $PSScriptRoot 'organize-outputs.ps1'
$authorityRules=Join-Path $PSScriptRoot 'AGENTS.md'
if(-not (Test-Path -LiteralPath $authorityRules -PathType Leaf)){$authorityRules=Join-Path (Split-Path $PSScriptRoot) 'AGENTS.md'}
$authoritySources+=$authorityRules
$authoritySources+=Join-Path $PSScriptRoot 'compatibility-provenance.json'
# Stream the source archive: Compress-Archive can retain large media in memory.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$authoritySourceZip=Join-Path $authorityOutput ($authorityStem+'-source.zip')
$authoritySourceTemp=Join-Path $PSScriptRoot 'release-source.pending.zip'
$authoritySourceStream=[IO.File]::Open($authoritySourceTemp,[IO.FileMode]::Create,[IO.FileAccess]::Write)
$authoritySourceArchive=New-Object IO.Compression.ZipArchive($authoritySourceStream,[IO.Compression.ZipArchiveMode]::Create,$false)
try{
    foreach($authoritySource in $authoritySources){
        $authoritySourceItem=Get-Item -LiteralPath $authoritySource
        $authoritySourceFiles=if($authoritySourceItem.PSIsContainer){@(Get-ChildItem -LiteralPath $authoritySource -Recurse -File)}else{@($authoritySourceItem)}
        foreach($authoritySourceFile in $authoritySourceFiles){
            $authorityEntryName=$authoritySourceFile.FullName.Substring($PSScriptRoot.Length+1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($authoritySourceArchive,$authoritySourceFile.FullName,$authorityEntryName,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
        }
    }
}finally{$authoritySourceArchive.Dispose();$authoritySourceStream.Dispose()}
Move-Item -LiteralPath $authoritySourceTemp -Destination $authoritySourceZip -Force
$authoritySourceArchive=[IO.Compression.ZipFile]::OpenRead($authoritySourceZip)
try{
    foreach($authorityItem in $authorityVerification.sourceHashes.PSObject.Properties){
        $authorityEntry=$authoritySourceArchive.GetEntry('src/'+$authorityItem.Name.Replace('\','/'))
        if($null -eq $authorityEntry){throw ('Missing packaged source: '+$authorityItem.Name)}
        $authorityEntryStream=$authorityEntry.Open();$authorityEntrySha=[Security.Cryptography.SHA256]::Create()
        try{$authorityEntryHash=[BitConverter]::ToString($authorityEntrySha.ComputeHash($authorityEntryStream)).Replace('-','')}
        finally{$authorityEntrySha.Dispose();$authorityEntryStream.Dispose()}
        if($authorityEntryHash -ne $authorityItem.Value){throw ('Packaged source differs: '+$authorityItem.Name)}
    }
}finally{$authoritySourceArchive.Dispose()}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'botanic.toomanylags.fgnaddon.cfg') -Destination $authorityConfigRelease -Force
Compress-Archive -LiteralPath (Join-Path $PSScriptRoot 'release-config\BepInEx') -DestinationPath (Join-Path $authorityOutput ($authorityStem+'-config.zip')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'botanic.toomanylags.fgnaddon.cfg') -Destination (Join-Path $authorityOutput ($authorityStem+'.cfg')) -Force
Copy-Item -LiteralPath $authorityDll -Destination (Join-Path $authorityOutput ($authorityStem+'.dll')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot $authorityReadme) -Destination (Join-Path $authorityOutput ($authorityStem+'-README.md')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VALIDATION.md') -Destination (Join-Path $authorityOutput ($authorityStem+'-Verification.md')) -Force
$authorityWiki=Join-Path $PSScriptRoot 'docs\wiki'
if(Test-Path -LiteralPath $authorityWiki -PathType Container){
    $authorityWikiOutput=Join-Path $authorityOutput ($authorityStem+'-Wiki')
    New-Item -ItemType Directory -Force -Path $authorityWikiOutput | Out-Null
    $authorityWikiFiles=@(Get-ChildItem -LiteralPath $authorityWiki -Filter '*.md' -File | Select-Object -ExpandProperty FullName)
    Copy-Item -LiteralPath $authorityWikiFiles -Destination $authorityWikiOutput -Force
    Compress-Archive -LiteralPath $authorityWikiFiles -DestinationPath (Join-Path $authorityOutput ($authorityStem+'-wiki.zip')) -Force
}
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
        $authorityTextSource=if($authorityText -eq 'README.md'){$authorityReadme}else{$authorityText}
        try{if($authorityReader.ReadToEnd() -cne [IO.File]::ReadAllText((Join-Path $PSScriptRoot $authorityTextSource))){throw ('Packaged content differs: '+$authorityText)}}finally{$authorityReader.Dispose()}
    }
}finally{$authorityArchive.Dispose()}
$authorityDebugArchive=[IO.Compression.ZipFile]::OpenRead($authorityDebugZip)
try{
    if(Compare-Object @($authorityDebugArchive.Entries.FullName|Sort-Object) $authorityExpected){throw 'Debug package entries differ.'}
    $authorityStream=$authorityDebugArchive.GetEntry('TooManyLags.dll').Open()
    $authoritySha=[Security.Cryptography.SHA256]::Create()
    try{$authorityPacked=[BitConverter]::ToString($authoritySha.ComputeHash($authorityStream)).Replace('-','')}
    finally{$authoritySha.Dispose();$authorityStream.Dispose()}
    if($authorityPacked -ne $authorityDebugHash){throw 'Packaged debug DLL differs.'}
}finally{$authorityDebugArchive.Dispose()}
$authorityConfigArchive=[IO.Compression.ZipFile]::OpenRead((Join-Path $authorityOutput ($authorityStem+'-config.zip')))
try{
    if($authorityConfigArchive.Entries.Count -ne 1){throw 'Config ZIP has unexpected entries.'}
    $authorityConfigEntry=$authorityConfigArchive.Entries[0]
    if($authorityConfigEntry.FullName.Replace('\','/') -cne 'BepInEx/config/botanic.toomanylags.fgnaddon.cfg'){throw 'Config ZIP path differs.'}
    $authorityReader=New-Object IO.StreamReader($authorityConfigEntry.Open())
    try{if($authorityReader.ReadToEnd() -cne [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'botanic.toomanylags.fgnaddon.cfg'))){throw 'Packaged config differs.'}}finally{$authorityReader.Dispose()}
}finally{$authorityConfigArchive.Dispose()}
& (Join-Path $PSScriptRoot 'organize-outputs.ps1') -OutputDirectory $authorityOutput -Project 'TooManyLags-FGNAddon-Combined'
Get-ChildItem -LiteralPath $authorityOutput -Filter ($authorityStem+'*') | Select-Object Name,Length

