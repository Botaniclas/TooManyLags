param(
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx'
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $BepInExDirectory 'core\Mono.Cecil.dll')
$priorityGamePath = Join-Path $ValheimDirectory 'valheim_Data\Managed\assembly_valheim.dll'
$priorityDllPath = Join-Path $PSScriptRoot 'bin\TooManyLags.dll'
$priorityGame = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($priorityGamePath)
$priorityMod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($priorityDllPath)
try {
    $priorityType = @($priorityGame.MainModule.Types | Where-Object Name -eq 'ZDOMan')[0]
    $priorityMethods = @($priorityType.Methods | Where-Object Name -eq 'AddForceSendZdos')
    if ($priorityMethods.Count -ne 1) { throw 'Native forced-send target is missing or overloaded' }
    $priorityMethod = $priorityMethods[0]
    if ($priorityMethod.Parameters.Count -ne 2 -or $priorityMethod.Parameters[1].ParameterType.FullName -cne 'System.Collections.Generic.List`1<ZDO>' -or $priorityMethod.ReturnType.FullName -cne 'System.Void') { throw 'Native forced-send signature changed' }
    $priorityInsert = @($priorityMethod.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Insert' -and $_.Operand.DeclaringType.FullName -ceq 'System.Collections.Generic.List`1<ZDO>' })
    if ($priorityInsert.Count -ne 1 -or $priorityInsert[0].Previous.Previous.OpCode.Code.ToString() -ne 'Ldc_I4_0') { throw 'Native forced-send no longer only prepends entries' }
    $priorityOtherWrites = @($priorityMethod.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -ceq 'System.Collections.Generic.List`1<ZDO>' -and $_.Operand.Name -notin @('Insert', 'get_Count', 'get_Item') })
    if ($priorityOtherWrites.Count) { throw 'Native forced-send has an unexpected list mutation' }
    $priorityCreate = @($priorityType.Methods | Where-Object Name -eq 'CreateSyncList')[0]
    $priorityCallsites = @($priorityCreate.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'AddForceSendZdos' })
    if ($priorityCallsites.Count -ne 2) { throw 'Native server/client forced-send seams changed' }
    $priorityPatch = @($priorityMod.MainModule.Types | Where-Object FullName -ceq 'TooManyLags.HandoffSendPatch')[0]
    $priorityAfter = @($priorityPatch.Methods | Where-Object Name -eq 'After')[0]
    if (-not @($priorityAfter.Parameters | Where-Object { $_.Name -ceq '__runOriginal' -and $_.ParameterType.FullName -ceq 'System.Boolean' }).Count) { throw 'Skipped originals are not excluded by the hook contract' }
    if (@($priorityAfter.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('SetOwner', 'SetOwnerInternal', 'Serialize', 'SendZDOs') }).Count) { throw 'Priority hook must not assign ownership or send packets itself' }
    [ordered]@{
        passed = $true
        dllSha256 = (Get-FileHash -LiteralPath $priorityDllPath -Algorithm SHA256).Hash
        gameSha256 = (Get-FileHash -LiteralPath $priorityGamePath -Algorithm SHA256).Hash
        target = $priorityMethod.FullName
        nativeCreateSyncListCallsites = $priorityCallsites.Count
        skippedOriginalGuard = $true
        changesOwnerOrSerializer = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'bin\priority-audit\native-contract.json') -Encoding UTF8
    Write-Output 'PASS: actual native forced-prefix mutation/callsite and original-skip hook contracts; no owner or serializer replacement.'
}
finally { $priorityGame.Dispose(); $priorityMod.Dispose() }
