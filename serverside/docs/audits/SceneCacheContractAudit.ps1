$ErrorActionPreference = 'Stop'
$workspace = 'C:/Users/Niclas/Documents/Codex/2026-09-19/ic'
$gamePath = 'D:/Steam-Bibliotheksordner/steamapps/common/Valheim/valheim_Data/Managed/assembly_valheim.dll'
$fgnRoot = Join-Path $workspace 'work/KinematicVelocityAudit-2026-10-14/fgn-1.5.53'
$pupPath = Join-Path $workspace 'work/PupUpdateAudit-2026-10-14/pup-1.0.36/ValheimInstanceCombiner/InstanceCombinerMod.cs'
$forkPath = Join-Path $workspace 'work/ServersideSimulation-reference/src/Valheim_Serverside/Features/Core.cs'
Add-Type -Path (Join-Path $workspace 'work/AreaReadinessAudit-2026-10-05/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath)
$checks = [System.Collections.Generic.List[object]]::new()
function Assert-Contract([string]$name, [bool]$value) {
    if (!$value) { throw "Contract failed: $name" }
    $checks.Add([ordered]@{ name=$name; passed=$true })
}
function Get-Method([string]$type, [string]$name) {
    $found = @($assembly.MainModule.GetType($type).Methods | Where-Object Name -EQ $name)
    if ($found.Count -ne 1) { throw "Ambiguous or missing $type.$name" }
    return $found[0]
}
function Call-Index($method, [string]$name) {
    for ($i=0; $i -lt $method.Body.Instructions.Count; $i++) {
        $instruction=$method.Body.Instructions[$i]
        if ($instruction.Operand -is [Mono.Cecil.MethodReference] -and $instruction.Operand.Name -eq $name) { return $i }
    }
    return -1
}
function Source-Between([string]$source, [string]$start, [string]$end) {
    $a=$source.IndexOf($start,[StringComparison]::Ordinal)
    if ($a -lt 0) { throw "Missing source marker $start" }
    $b=$source.IndexOf($end,$a+$start.Length,[StringComparison]::Ordinal)
    if ($b -lt 0) { throw "Missing source end marker $end" }
    return $source.Substring($a,$b-$a)
}
try {
    $position=Get-Method 'ZDO' 'InternalSetPosition'
    $sectorCall=Call-Index $position 'SetSector'
    $positionWrites=@(for ($i=0; $i -lt $position.Body.Instructions.Count; $i++) {
        $instruction=$position.Body.Instructions[$i]
        if ($instruction.OpCode.Name -eq 'stfld' -and $instruction.Operand -is [Mono.Cecil.FieldReference] -and $instruction.Operand.Name -eq 'm_position') { $i }
    })
    Assert-Contract 'Native position field is written after sector callbacks' ($sectorCall -ge 0 -and $positionWrites.Count -eq 1 -and $sectorCall -lt $positionWrites[0])
    $hash=Get-Method 'ZDO' 'GetHashCode'
    Assert-Contract 'Native ZDO hash depends on mutable UID' (@($hash.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'm_uid'
    }).Count -gt 0)
    $hasPrefab=Get-Method 'ZNetScene' 'HasPrefab'
    Assert-Contract 'Native prefab availability is dictionary key membership' ((Call-Index $hasPrefab 'ContainsKey') -ge 0)
    $add=Get-Method 'ZNetScene' 'AddInstance'
    Assert-Contract 'Scene registration writes Created independently of prefab filtering' ((Call-Index $add 'set_Created') -ge 0)
    $sector=Get-Method 'ZDO' 'SetSector'
    Assert-Contract 'Sector callbacks carry explicit SectorIndex arguments' ((Call-Index $sector 'AddToSector') -ge 0 -and (Call-Index $sector 'RemoveFromSector') -ge 0)
    $chunks=Get-Method 'ZDOMan' 'LoadChunks'
    Assert-Contract 'Chunk loading has a separate InitialAddToSector path' ((Call-Index $chunks 'InitialAddToSector') -ge 0)
    $deserialize=Get-Method 'ZDO' 'Deserialize'
    Assert-Contract 'Deserialize bypasses SetPrefab' ((Call-Index $deserialize 'SetPrefab') -lt 0)
    Assert-Contract 'Deserialize sets Distant directly' ((Call-Index $deserialize 'set_Distant') -ge 0)
    Assert-Contract 'Native portal storage must be read in collection' (@((Get-Method 'ZDOMan' 'FindObjects').Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'm_portalObjects'
    }).Count -gt 0)
    $fgn=Get-Content -LiteralPath (Join-Path $fgnRoot 'FiresGhettoNetworkMod/ServerAuthorityPatches.cs') -Raw
    $filter=Source-Between $fgn 'private static void FilterAndDedupeZdos' 'internal static int ExtendedZoneRadius'
    Assert-Contract 'FGN filter observes native prefab availability' ($filter.Contains('instance.HasPrefab(prefab)'))
    Assert-Contract 'FGN filter observes current zone identity membership' ($filter.Contains('dictionary.ContainsKey(sector)'))
    Assert-Contract 'FGN candidate filter does not filter Created or ownership' (!$filter.Contains('.Created') -and !$filter.Contains('GetOwner('))
    $fork=Get-Content -LiteralPath $forkPath -Raw
    $collection=Source-Between $fork 'public class CreateDestroyObjects_Patch' '[HarmonyPatch(typeof(ZNetScene), "CreateObjectsSorted")]'
    Assert-Contract 'Fork clears reusable buffers every pass' ($collection.Contains('currentObjects.Clear();') -and $collection.Contains('currentDistantObjects.Clear();'))
    Assert-Contract 'Fork recollects objects in its scene pass' ($collection.Contains('FindSectorObjects(zone, distance, currentObjects, currentDistantObjects)'))
    $pup=Get-Content -LiteralPath $pupPath -Raw
    $suppress=Source-Between $pup 'private void SuppressRemoteZdo' 'private void ReleaseSuppressedRemoteZdos'
    Assert-Contract 'PUP uses Created true for suppressed originals' ($suppress.Contains('zdo.Created = true;'))
    $release=Source-Between $pup 'private void ReleaseSuppressedRemoteZdos' 'private bool TrySuppressRemoteOriginalCreation'
    Assert-Contract 'PUP restore releases Created without guaranteed membership changes' ($release.Contains('value.Created = false;') -and $release.Contains('zDO.Created = false;'))
    $unload=Source-Between $pup 'private bool UnloadRemoteOriginalInstance' 'private void SuppressRemoteZdo'
    Assert-Contract 'PUP removes a live instance separately from ZDO membership' ($unload.Contains('view.ResetZDO();') -and $unload.Contains('dictionary.Remove(zdo);') -and $unload.Contains('SuppressRemoteZdo('))
    $repair=Source-Between $pup 'private void RepairRestoredZdoSector' 'private List<KeyValuePair<ZDOID, string>> CaptureRestoreSectorCandidates'
    Assert-Contract 'PUP can repair a sector without changing position' ($repair.Contains('SetRestoreSector(zdo, sectorIndex);') -and !$repair.Contains('SetPosition('))
    $cell=Source-Between $pup 'private static Vector2Int GetCell(Vector3 position)' 'private '
    Assert-Contract 'PUP cell origin differs from native centred zones' ($cell.Contains('Mathf.FloorToInt(position.x / 64f)') -and $cell.Contains('Mathf.FloorToInt(position.z / 64f)'))
    $tml=Get-Content -LiteralPath (Join-Path $workspace 'work/TooManyLags/src/AuthorityController.cs') -Raw
    Assert-Contract 'TML excludes baked originals separately in ownership registration' ($tml.Contains('BakedObjects.IsBaked(data)'))
    $staticGuard=Get-Content -LiteralPath (Join-Path $workspace 'work/TooManyLags/src/PupCompatibility/Statics.cs') -Raw
    Assert-Contract 'TML structural guard uses owner and live PUP revisions' ($staticGuard.Contains('native.Coverage') -and $staticGuard.Contains('native.Zones') -and $staticGuard.Contains('data.OwnerRevision'))
    $cacheRoot='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/cache'
    $binaryInputs=[System.Collections.Generic.List[string]]::new()
    foreach ($version in @('1.5.53','1.5.65')) {
        $binary=Join-Path $cacheRoot ('VerdantsAscent-FiresGhettoNetworking/'+$version+'/plugins/FiresGhettoNetworking/VAGhettoNetworking.dll')
        $binaryInputs.Add($binary)
        $fgnAssembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($binary)
        try {
            $fgnType=$fgnAssembly.MainModule.GetType('FiresGhettoNetworkMod.ServerAuthorityPatches')
            $pass=$fgnType.Methods | Where-Object Name -EQ 'CreateDestroyObjects_Prefix'
            $filterMethod=$fgnType.Methods | Where-Object Name -EQ 'FilterAndDedupeZdos'
            Assert-Contract ("FGN $version still collects and filters in each served scene pass") ((Call-Index $pass 'CollectZdosFromAllPeerActiveAreas') -ge 0 -and (Call-Index $pass 'FilterAndDedupeZdos') -ge 0)
            Assert-Contract ("FGN $version still services creation and removal") ((Call-Index $pass 'CreateObjects') -ge 0 -and (Call-Index $pass 'RemoveObjects') -ge 0)
            Assert-Contract ("FGN $version candidate filter does not consult Created/owner") ((Call-Index $filterMethod 'get_Created') -lt 0 -and (Call-Index $filterMethod 'GetOwner') -lt 0)
            $serverCreate=$fgnAssembly.MainModule.GetType('FiresGhettoNetworkMod.AutoTune.ZoneLoadPatches').Methods | Where-Object Name -EQ 'ServerCreate'
            Assert-Contract ("FGN $version keeps live creation/readiness/nearest-player checks") ((Call-Index $serverCreate 'get_Created') -ge 0 -and (Call-Index $serverCreate 'IsZoneReadyForType') -ge 0 -and (Call-Index $serverCreate 'NearestPeerSqr') -ge 0)
        } finally { $fgnAssembly.Dispose() }
    }
    $pupBinary=Join-Path $cacheRoot 'PUP82-PUP_FPS/1.0.36/PUP_FPS.dll'
    $binaryInputs.Add($pupBinary)
    $pupAssembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($pupBinary)
    try {
        $pupType=$pupAssembly.MainModule.GetType('ValheimInstanceCombiner.InstanceCombinerMod')
        $suppressNative=$pupType.Methods | Where-Object Name -EQ 'SuppressRemoteZdo'
        $releaseNative=$pupType.Methods | Where-Object Name -EQ 'ReleaseSuppressedRemoteZdos'
        $unloadNative=$pupType.Methods | Where-Object Name -EQ 'UnloadRemoteOriginalInstance'
        Assert-Contract 'Released PUP suppression writes Created' ((Call-Index $suppressNative 'set_Created') -ge 0)
        Assert-Contract 'Released PUP suppression release also writes Created' ((Call-Index $releaseNative 'set_Created') -ge 0)
        Assert-Contract 'Released PUP unload preserves ZDO while resetting/removing its view' ((Call-Index $unloadNative 'ResetZDO') -ge 0 -and (Call-Index $unloadNative 'Remove') -ge 0 -and (Call-Index $unloadNative 'SuppressRemoteZdo') -ge 0)
        $repairNative=$pupType.Methods | Where-Object Name -EQ 'RepairRestoredZdoSector'
        Assert-Contract 'Released PUP repairs sectors through a separately bound delegate' (@($repairNative.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'SetRestoreSector'
        }).Count -gt 0 -and (Call-Index $repairNative 'SetPosition') -lt 0)
    } finally { $pupAssembly.Dispose() }
    $files=@($gamePath,$pupPath,$forkPath,
        (Join-Path $fgnRoot 'FiresGhettoNetworkMod/ServerAuthorityPatches.cs'),
        (Join-Path $fgnRoot 'FiresGhettoNetworkMod.AutoTune/ZoneLoadPatches.cs'),
        (Join-Path $workspace 'work/TooManyLags/src/AuthorityController.cs'),
        (Join-Path $workspace 'work/TooManyLags/src/PupCompatibility/Statics.cs')) + @($binaryInputs)
    [ordered]@{
        checks=$checks
        inputs=@($files | ForEach-Object { [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash} })
        scope='Native IL plus version-scoped reference source checks; no production cache or live Unity integration executed.'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'scene-cache-contracts.json') -Encoding UTF8
    Write-Output ("Scene-cache native/reference contracts passed: " + $checks.Count)
} finally { $assembly.Dispose() }
