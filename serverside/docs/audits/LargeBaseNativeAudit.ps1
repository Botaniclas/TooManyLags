$ErrorActionPreference = 'Stop'
$auditDirectory = $PSScriptRoot
$workspace = 'C:/Users/Niclas/Documents/Codex/2026-09-19/ic'
$gamePath = 'D:/Steam-Bibliotheksordner/steamapps/common/Valheim/valheim_Data/Managed/assembly_valheim.dll'
Add-Type -Path (Join-Path $workspace 'work/AreaReadinessAudit-2026-10-05/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath)
$checks = [System.Collections.Generic.List[object]]::new()
$dump = [System.Collections.Generic.List[string]]::new()
function Get-Method([string]$type, [string]$name) {
    $methods = @($assembly.MainModule.GetType($type).Methods | Where-Object Name -EQ $name)
    if ($methods.Count -ne 1) { throw "Ambiguous or absent method: $type.$name" }
    return $methods[0]
}
function Assert-Contract([string]$name, [bool]$condition) {
    if (!$condition) { throw "Contract failed: $name" }
    $checks.Add([ordered]@{ name = $name; passed = $true })
}
function Has-Call($method, [string]$name) {
    return @($method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name
    }).Count -gt 0
}
try {
    foreach ($entry in @(
        @('ZNetScene','Update'), @('ZNetScene','CreateObjects'), @('ZNetScene','CreateObjectsSorted'),
        @('ZNetScene','RemoveObjects'), @('ZDOMan','FindObjects'), @('ZDOMan','FindDistantObjects'),
        @('ZoneSystem','Update'), @('ZoneSystem','PokeLocalZone'), @('ZoneSystem','SpawnZone'),
        @('ZoneSystem','UpdateTTL'), @('ZDO','InternalSetPosition'), @('ZDO','SetSector'),
        @('ZDO','Deserialize'), @('ZDO','SetPrefab'), @('ZDO','SetDistant')
    )) {
        $method = Get-Method $entry[0] $entry[1]
        $dump.Add($method.FullName)
        foreach ($instruction in $method.Body.Instructions) { $dump.Add($instruction.ToString()) }
        $dump.Add('')
    }
    $create = Get-Method 'ZNetScene' 'CreateObjectsSorted'
    Assert-Contract 'Creation still observes active-area loading' (Has-Call $create 'IsActiveAreaLoaded')
    Assert-Contract 'Creation still observes type-specific readiness' (Has-Call $create 'IsZoneReadyForType')
    Assert-Contract 'Creation reads live Created flag' (Has-Call $create 'get_Created')
    Assert-Contract 'Creation copies candidates to scratch before sorting' (Has-Call $create 'Sort')
    $find = Get-Method 'ZDOMan' 'FindObjects'
    Assert-Contract 'Native collection copies sector references with AddRange' (Has-Call $find 'AddRange')
    Assert-Contract 'Native collection includes portal storage' (@($find.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'm_portalObjects'
    }).Count -gt 0)
    Assert-Contract 'Far collection reads live Distant flag' (Has-Call (Get-Method 'ZDOMan' 'FindDistantObjects') 'get_Distant')
    $remove = Get-Method 'ZNetScene' 'RemoveObjects'
    Assert-Contract 'Removal marks current candidates' (Has-Call $remove 'set_TempRemoveEarmark')
    Assert-Contract 'Removal reads live instances' (@($remove.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'm_instances'
    }).Count -gt 0)
    $poke = Get-Method 'ZoneSystem' 'PokeLocalZone'
    Assert-Contract 'Loaded zones refreshed through live dictionary lookup' (Has-Call $poke 'TryGetValue')
    Assert-Contract 'Zone poke can spawn existing generated zone roots' (Has-Call $poke 'SpawnZone')
    Assert-Contract 'Zone poke chooses generated versus new mode' (Has-Call $poke 'IsZoneGenerated')
    $spawn = Get-Method 'ZoneSystem' 'SpawnZone'
    Assert-Contract 'Zone spawn has terrain readiness gate' (Has-Call $spawn 'IsTerrainReady')
    Assert-Contract 'Zone spawn places generated vegetation' (Has-Call $spawn 'PlaceVegetation')
    Assert-Contract 'Zone spawn does not call ZNetScene object creation' (!(Has-Call $spawn 'CreateObjects'))
    $sector = Get-Method 'ZDO' 'SetSector'
    Assert-Contract 'Native sector moves remove old membership' (Has-Call $sector 'RemoveFromSector')
    Assert-Contract 'Native sector moves add new membership' (Has-Call $sector 'AddToSector')
    $deserialize = Get-Method 'ZDO' 'Deserialize'
    Assert-Contract 'Network imports can directly replace prefab metadata' (@($deserialize.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'stfld' -and $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'm_prefab'
    }).Count -gt 0)
    Assert-Contract 'Network imports write Distant metadata' (Has-Call $deserialize 'set_Distant')
    $dump | Set-Content -LiteralPath (Join-Path $auditDirectory 'large-base-native-il.txt') -Encoding UTF8
    [ordered]@{
        gameSha256 = (Get-FileHash -LiteralPath $gamePath).Hash
        checks = $checks
        scope = 'Installed native metadata/IL only. Does not execute Unity, FGN or an optimisation.'
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $auditDirectory 'large-base-native-contracts.json') -Encoding UTF8
    Write-Output ("Native contracts passed: " + $checks.Count)
} finally { $assembly.Dispose() }
