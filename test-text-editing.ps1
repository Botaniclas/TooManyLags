param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim')
$ErrorActionPreference = 'Stop'
$editCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$editOutput = Join-Path $PSScriptRoot 'bin/text-editing-tests'
New-Item -ItemType Directory -Force -Path $editOutput | Out-Null
$editCore = Join-Path $BepInExDirectory 'core'
foreach ($editDependency in @('0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) {
    Copy-Item -LiteralPath (Join-Path $editCore $editDependency) -Destination $editOutput -Force
}
$editExe = Join-Path $editOutput 'ProtectionTests.exe'
$editSources = @('src/TextEditing/Protection.cs','tests/TextEditing/Fixtures.cs','tests/TextEditing/ProtectionTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $editCompiler /nologo /target:exe /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 ('/reference:'+(Join-Path $editCore '0Harmony.dll')) ('/out:'+$editExe) $editSources
if ($LASTEXITCODE -ne 0) { throw 'Text editing fixture compilation failed.' }
& $editExe
if ($LASTEXITCODE -ne 0) { throw 'Text editing protection tests failed.' }
$editNativeAudit = Join-Path $editOutput 'NativeAudit.exe'
& $editCompiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/reference:'+(Join-Path $editCore 'Mono.Cecil.dll')) ('/out:'+$editNativeAudit) (Join-Path $PSScriptRoot 'tests/TextEditing/NativeAudit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native text editing audit compilation failed.' }
& $editNativeAudit (Join-Path $ValheimDirectory 'valheim_Data/Managed/assembly_valheim.dll') (Join-Path $PSScriptRoot 'bin/TooManyLags.dll')
if ($LASTEXITCODE -ne 0) { throw 'Native text editing contracts failed.' }
