param(
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string[]]$PupPatchAssembly = @(
        (Join-Path $PSScriptRoot '../BotanicPupPatch-2.1.0/bin/BotanicPupPatch.dll'),
        (Join-Path $PSScriptRoot '../BotanicPupPatch-2.1.1/bin/BotanicPupPatch.dll')
    )
)
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskOutput = Join-Path $PSScriptRoot 'bin/mob-precedence-tests'
$taskCore = Join-Path $BepInExDirectory 'core'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
foreach ($dep in @('BepInEx.dll','0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) {
    Copy-Item -LiteralPath (Join-Path $taskCore $dep) -Destination $taskOutput -Force
}
$taskExe = Join-Path $taskOutput 'MobPolicyHandoffTests.exe'
$taskSources = @('src/MobPolicyHandoff.cs','tests/MobPolicyHandoffTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore 'BepInEx.dll')) ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/out:'+$taskExe) $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Combat precedence fixture compilation failed.' }
& $taskExe (Join-Path $taskOutput 'fixture.cfg') $taskCore
if ($LASTEXITCODE -ne 0) { throw 'Combat precedence regression failed.' }
Add-Type -Path (Join-Path $taskCore 'Mono.Cecil.dll')
foreach ($taskPatchPath in $PupPatchAssembly) {
$taskPatch = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $taskPatchPath).Path)
try {
    $taskPolicy = @($taskPatch.MainModule.Types | Where-Object FullName -eq 'BotanicPupPatch.FgnCombatOwnership')
    if ($taskPolicy.Count -ne 1) { throw 'Actual PupPatch combat module not found.' }
    $taskEnabled = @($taskPolicy.Fields | Where-Object { $_.Name -eq 'Enabled' -and $_.IsStatic -and $_.FieldType.FullName -eq 'System.Boolean' })
    $taskStop = @($taskPolicy.Methods | Where-Object { $_.Name -eq 'Stop' -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Void' -and $_.Parameters.Count -eq 0 })
    $taskInstall = @($taskPolicy.Methods | Where-Object { $_.Name -eq 'Install' -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Void' -and ($_.Parameters.ParameterType.FullName -join ',') -eq 'BepInEx.Configuration.ConfigFile,System.Reflection.Assembly,System.Version' })
    if ($taskEnabled.Count -ne 1 -or $taskStop.Count -ne 1 -or $taskInstall.Count -ne 1) { throw 'Actual PupPatch module has no supported lifecycle contract.' }
    foreach ($taskInstruction in $taskStop[0].Body.Instructions) {
        if ($taskInstruction.Operand -is [Mono.Cecil.MethodReference]) {
            $taskCalledType = $taskInstruction.Operand.DeclaringType.FullName
            if (($taskCalledType -eq 'BotanicPupPatch.Plugin' -and $taskInstruction.Operand.Name -ne 'Warn') -or $taskCalledType -in @('BotanicPupPatch.Statics','BotanicPupPatch.FgnTakeover','BotanicPupPatch.PupServerStreaming')) { throw 'Combat stop calls a core guard lifecycle.' }
        }
    }
    Write-Output ('PASS: Actual PupPatch ' + $taskPatch.Name.Version + ' optional combat lifecycle contract; no core guard lifecycle call in Stop.')
} finally { $taskPatch.Dispose() }
}
$taskMonoHost = Join-Path $taskOutput 'MonoFixtureHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$taskMonoHost) (Join-Path $PSScriptRoot 'tests/CombatMonoFixtureHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Precedence Mono host compilation failed.' }
& $taskMonoHost $taskExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc')
if ($LASTEXITCODE -ne 0) { throw 'Combat precedence failed on embedded Valheim Mono.' }
