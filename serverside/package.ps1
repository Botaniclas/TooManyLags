param([string]$OutputDirectory = (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'outputs'))
$ErrorActionPreference='Stop'
$manifest=Get-Content (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$proof=Get-Content (Join-Path $PSScriptRoot 'verification.json') -Raw | ConvertFrom-Json
$dll=Join-Path $PSScriptRoot 'bin/TooManyLagsServerSide.dll'
if ((Get-FileHash $dll).Hash -ne $proof.dllSha256 -or [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3) -ne $manifest.version_number) {throw 'DLL is not the verified version'}
foreach ($entry in $proof.sourceHashes.PSObject.Properties) {if ((Get-FileHash (Join-Path $PSScriptRoot $entry.Name)).Hash -ne $entry.Value) {throw ('Source changed: '+$entry.Name)}}
if (@($proof.tests.PSObject.Properties).Count -lt 32) {throw 'Verification groups missing'}
$stage=Join-Path $PSScriptRoot 'release-thunderstore'
New-Item -ItemType Directory -Force -Path $stage,$OutputDirectory | Out-Null
$files=@('README.md','CHANGELOG.md','manifest.json','LICENSE','THIRD_PARTY_NOTICES.md','icon.png')
foreach ($file in $files) {Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $stage -Force}
Copy-Item -LiteralPath $dll -Destination $stage -Force
$stem='TooManyLags-ServerSide-'+$manifest.version_number
$zip=Join-Path $OutputDirectory ($stem+'.zip')
Compress-Archive -LiteralPath @((@($files)+@('TooManyLagsServerSide.dll')) | ForEach-Object {Join-Path $stage $_}) -DestinationPath $zip -Force
$sources=@('src','tests','tools','docs','.gitignore','.gitattributes','README.md','CHANGELOG.md','manifest.json','thunderstore.toml','LICENSE','THIRD_PARTY_NOTICES.md','icon.png','AGENTS.md','VALIDATION.md','build.ps1','test.ps1','generate-config.ps1','package.ps1','validate-release.ps1','verification.json','botanic.toomanylags.dedicatedaddon.cfg') | ForEach-Object {Join-Path $PSScriptRoot $_}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceZip=Join-Path $OutputDirectory ($stem+'-source.zip')
if(Test-Path -LiteralPath $sourceZip -PathType Leaf){Remove-Item -LiteralPath $sourceZip -Force}
$sourceArchive=[IO.Compression.ZipFile]::Open($sourceZip,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($source in $sources){
        $item=Get-Item -LiteralPath $source
        $sourceFiles=if($item.PSIsContainer){Get-ChildItem -LiteralPath $source -File -Recurse}else{@($item)}
        foreach($sourceFile in $sourceFiles){
            $relative=$sourceFile.FullName.Substring($PSScriptRoot.Length+1).Replace('\','/')
            if($relative -match '(^|/)(bin|obj|\.git|__pycache__)/|\.(dll|exe|pdb|zip|pyc)$|^docs/audits/fgn-[^/]+/'){continue}
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceArchive,$sourceFile.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
}finally{$sourceArchive.Dispose()}
$configDir=Join-Path $PSScriptRoot 'release-config/BepInEx/config'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'botanic.toomanylags.dedicatedaddon.cfg') -Destination $configDir -Force
Compress-Archive -LiteralPath (Join-Path $PSScriptRoot 'release-config/BepInEx') -DestinationPath (Join-Path $OutputDirectory ($stem+'-config.zip')) -Force
foreach ($pair in @(@('README.md','-README.md'),@('VALIDATION.md','-Validation.md'),@('docs/COMPARISON-AND-IDEAS.md','-Comparison.md'))) {Copy-Item -LiteralPath (Join-Path $PSScriptRoot $pair[0]) -Destination (Join-Path $OutputDirectory ($stem+$pair[1])) -Force}
Copy-Item -LiteralPath $dll -Destination (Join-Path $OutputDirectory ($stem+'.dll')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'botanic.toomanylags.dedicatedaddon.cfg') -Destination (Join-Path $OutputDirectory ($stem+'.cfg')) -Force
$wiki=Join-Path $PSScriptRoot 'docs/wiki'
$wikiOutput=Join-Path $OutputDirectory ($stem+'-Wiki')
New-Item -ItemType Directory -Force -Path $wikiOutput | Out-Null
$wikiFiles=@(Get-ChildItem -LiteralPath $wiki -Filter '*.md' -File | Select-Object -ExpandProperty FullName)
Copy-Item -LiteralPath $wikiFiles -Destination $wikiOutput -Force
Compress-Archive -LiteralPath $wikiFiles -DestinationPath (Join-Path $OutputDirectory ($stem+'-wiki.zip')) -Force
& (Join-Path $PSScriptRoot 'validate-release.ps1') -OutputDirectory $OutputDirectory
& (Join-Path (Split-Path $PSScriptRoot) 'organize-outputs.ps1') -OutputDirectory $OutputDirectory -Project 'TooManyLags-ServerSide'
Get-ChildItem -LiteralPath $OutputDirectory -Filter ($stem+'*') | Select-Object Name,Length
