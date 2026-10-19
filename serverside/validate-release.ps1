param([string]$OutputDirectory = (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'outputs'))
$ErrorActionPreference='Stop'
$manifest=Get-Content (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$proof=Get-Content (Join-Path $PSScriptRoot 'verification.json') -Raw | ConvertFrom-Json
$contracts=Get-Content (Join-Path $PSScriptRoot ('docs/audits/backend-contracts-'+$manifest.version_number+'.json')) -Raw | ConvertFrom-Json
$toml=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'thunderstore.toml'))
foreach($pair in @(@('name',$manifest.name),@('versionNumber',$manifest.version_number),@('description',$manifest.description),@('websiteUrl',$manifest.website_url))) {
    $line='(?m)^'+[regex]::Escape($pair[0])+' = "'+[regex]::Escape($pair[1])+'"\r?$'
    if($toml -notmatch $line){throw ('CLI metadata differs: '+$pair[0])}
}
if($manifest.description.Length -gt 250){throw 'Thunderstore description exceeds 250 characters'}
$expectedDependencies=@('denikson-BepInExPack_Valheim-5.4.2351','Sarkastic_gg-Dedicated_Simulation-1.18.0')
if(Compare-Object @($manifest.dependencies | Sort-Object) @($expectedDependencies | Sort-Object)){throw 'Package dependencies differ'}
foreach($dependency in $expectedDependencies){
    $parts=$dependency -split '-'
    $line='(?m)^'+[regex]::Escape($parts[0]+'-'+$parts[1])+' = "'+[regex]::Escape($parts[2])+'"\r?$'
    if($toml -notmatch $line){throw ('CLI dependency differs: '+$dependency)}
}
$stem='TooManyLags-ServerSide-'+$manifest.version_number
$pages=@('01-Configuration.md','02-Compatibility.md','03-Report-bugs-and-ideas.md','README.md')
$actualPages=@(Get-ChildItem (Join-Path $PSScriptRoot 'docs/wiki') -File -Filter '*.md' | Select-Object -ExpandProperty Name)
if(Compare-Object @($pages | Sort-Object) @($actualPages | Sort-Object)){throw 'Expected exactly three Wiki pages plus author index'}
if($contracts.version -ne $manifest.version_number -or $contracts.dllSha256 -ne $proof.dllSha256 -or $contracts.current.dllSha256 -ne $proof.forkSha256){throw 'Backend contract proof differs from verified beta'}
$config=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'botanic.toomanylags.dedicatedaddon.cfg'))
$documented=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'docs/wiki/01-Configuration.md'))
foreach($key in @('Enabled','UseGridForStaticBuildings','KeepOwnershipOnCellExit','GridCellSizeMeters','GridCoveragePattern','MovementRotationCheckIntervalSeconds','LargeGridPingThresholdMs','DirectionalGridMode','EnableOuterCells','FastMovementThresholdMetersPerSecond','InnerTransitionDelaySeconds','ForwardTransitionDelaySeconds','OuterTransitionDelaySeconds','TransitionDelaySeconds','ServerOverlapCooldownSeconds','ClientToServerGracePingMultiplier','MinimumClientToServerGraceSeconds','RefillOwnershipProtectionSeconds','RespectBackendFireOwnership','HostileMobConeRangeMeters','BossMobConeRangeMeters','ServerOwnedWhenMultiplePlayersInMobCone','EnableMobAttackForecast','EnableMobOwnershipTest','MobOwnershipTestIntervalSeconds')){
    if($config -notmatch ('(?m)^'+[regex]::Escape($key)+' = ') -or -not $documented.Contains($key)){throw ('Missing config/documentation key: '+$key)}
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing
$icon=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'icon.png'))
try{if($icon.Width -ne 256 -or $icon.Height -ne 256){throw 'Thunderstore icon must be 256x256'}}finally{$icon.Dispose()}
function Read-Entry($entry){
    if($null -eq $entry){throw 'Missing ZIP entry'}
    $reader=[IO.StreamReader]::new($entry.Open())
    try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $OutputDirectory ($stem+'.zip')))
try{
    $expected=@('README.md','CHANGELOG.md','manifest.json','LICENSE','THIRD_PARTY_NOTICES.md','icon.png','TooManyLagsServerSide.dll')
    if(Compare-Object @($zip.Entries.FullName | Sort-Object) @($expected | Sort-Object)){throw 'Unexpected install ZIP contents'}
    foreach($file in @('README.md','CHANGELOG.md','manifest.json','LICENSE','THIRD_PARTY_NOTICES.md')){
        if((Read-Entry $zip.GetEntry($file)) -cne [IO.File]::ReadAllText((Join-Path $PSScriptRoot $file))){throw ('Packaged text differs: '+$file)}
    }
    $stream=$zip.GetEntry('TooManyLagsServerSide.dll').Open()
    try{$sha=[Security.Cryptography.SHA256]::Create();try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$sha.Dispose()}}finally{$stream.Dispose()}
    if($hash -ne $proof.dllSha256){throw 'Packaged DLL differs from verified build'}
}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $OutputDirectory ($stem+'-config.zip')))
try{
    if($zip.Entries.Count -ne 1 -or $zip.Entries[0].FullName.Replace('\','/') -ne 'BepInEx/config/botanic.toomanylags.dedicatedaddon.cfg'){throw 'Unexpected config ZIP layout'}
    if((Read-Entry $zip.Entries[0]) -cne $config){throw 'Packaged config differs'}
}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $OutputDirectory ($stem+'-wiki.zip')))
try{
    if(Compare-Object @($zip.Entries.FullName | Sort-Object) @($pages | Sort-Object)){throw 'Unexpected Wiki ZIP contents'}
    foreach($page in $pages){if((Read-Entry $zip.GetEntry($page)) -cne [IO.File]::ReadAllText((Join-Path $PSScriptRoot ('docs/wiki/'+$page)))){throw ('Packaged Wiki differs: '+$page)}}
}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $OutputDirectory ($stem+'-source.zip')))
try{
    $names=@($zip.Entries | ForEach-Object {$_.FullName.Replace('\','/')})
    foreach($file in @('thunderstore.toml','validate-release.ps1','src/Plugin.cs',('docs/audits/backend-contracts-'+$manifest.version_number+'.json'))+@($pages | ForEach-Object {'docs/wiki/'+$_})){
        if($names -notcontains $file){throw ('Source ZIP omits: '+$file)}
    }
    if(@($zip.Entries | Where-Object {$_.Name -match '\.(dll|exe)$'}).Count -ne 0){throw 'Source ZIP contains external binaries'}
}finally{$zip.Dispose()}
Write-Host ('PASS: beta '+$manifest.version_number+'; metadata, dependency, icon, 25 documented settings, verified DLL, config, source and three Wiki pages match. No upload performed.')
