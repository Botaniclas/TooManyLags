param([string]$Version='1.0.10')
$ErrorActionPreference='Stop'
$output=Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'outputs'
$release=Join-Path $PSScriptRoot 'release-thunderstore'
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw|ConvertFrom-Json
$dll=Join-Path $PSScriptRoot 'bin\BotanicOwnerDebug.dll'
$verification=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verification.json') -Raw|ConvertFrom-Json
if($manifest.version_number -ne $Version -or [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3) -ne $Version){throw 'Version mismatch.'}
if($verification.dll_sha256 -ne (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash){throw 'DLL differs from verified build.'}
New-Item -ItemType Directory -Force -Path $release,$output|Out-Null
Copy-Item -LiteralPath $dll -Destination $release -Force
foreach($name in @('README.md','CHANGELOG.md','manifest.json','icon.png')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $release -Force}
Copy-Item -LiteralPath $dll -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $output 'BotanicOwnerDebug-README.md') -Force
$reports=Join-Path $output 'Others'
New-Item -ItemType Directory -Force -Path $reports|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VALIDATION.md') -Destination (Join-Path $reports ('BotanicOwnerDebug-'+$Version+'-validation.md')) -Force
$files=@('BotanicOwnerDebug.dll','README.md','CHANGELOG.md','manifest.json','icon.png')|ForEach-Object{Join-Path $release $_}
$zip=Join-Path $output ('BotanicOwnerDebug-'+$Version+'.zip')
Compress-Archive -LiteralPath $files -DestinationPath $zip -Force
$sources=@('src','tests','README.md','CHANGELOG.md','VALIDATION.md','manifest.json','icon.png','build.ps1','test.ps1','test-native.ps1','package.ps1','thunderstore.toml','verification.json')|ForEach-Object{Join-Path $PSScriptRoot $_}
Compress-Archive -LiteralPath $sources -DestinationPath (Join-Path $output ('BotanicOwnerDebug-'+$Version+'-source.zip')) -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
 $expected=@('BotanicOwnerDebug.dll','README.md','CHANGELOG.md','manifest.json','icon.png')|Sort-Object
 if(Compare-Object @($archive.Entries.FullName|Sort-Object) $expected){throw 'Release must contain exactly five files at root.'}
 $stream=$archive.GetEntry('BotanicOwnerDebug.dll').Open()
 try{$sha=[Security.Cryptography.SHA256]::Create();$packed=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','');$sha.Dispose()}finally{$stream.Dispose()}
 if($packed -ne $verification.dll_sha256){throw 'Packaged DLL differs from validated build.'}
}finally{$archive.Dispose()}
Get-ChildItem -LiteralPath $output -Filter 'BotanicOwnerDebug*'|Select-Object Name,Length
