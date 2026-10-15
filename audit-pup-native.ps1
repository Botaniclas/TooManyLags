param(
    [string]$CacheDirectory='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/cache',
    [string[]]$PupVersions=@('1.0.34','1.0.36'),
    [string]$ValheimDirectory='D:/Steam-Bibliotheksordner/steamapps/common/Valheim',
    [string]$CecilPath=(Join-Path (Split-Path $PSScriptRoot) 'tools/ilspycmd/package/tools/net8.0/any/Mono.Cecil.dll')
)
$ErrorActionPreference='Stop'
Add-Type -LiteralPath $CecilPath
$script:checks=0
function Check($Condition,[string]$Message) { $script:checks++; if(!$Condition){throw $Message} }
function Types($Items) { foreach($type in $Items){$type; Types $type.NestedTypes} }
function Inspect([string]$Path,[scriptblock]$Body) {
    $assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path)
    try { & $Body $assembly } finally { $assembly.Dispose() }
}
function Field($Type,[string]$Name,[string]$Expected) {
    $fields=@($Type.Fields|Where-Object Name -eq $Name)
    Check ($fields.Count -eq 1 -and $fields[0].FieldType.FullName -eq $Expected -and !$fields[0].IsStatic) ('Field contract changed: '+$Type.FullName+'::'+$Name)
}
function Method($Type,[string]$Name,[string]$Return,[string]$Parameters='') {
    $methods=@($Type.Methods|Where-Object {$_.Name -eq $Name -and ($_.Parameters.ParameterType.FullName -join ',') -eq $Parameters})
    Check ($methods.Count -eq 1 -and $methods[0].ReturnType.FullName -eq $Return) ('Method contract changed: '+$Type.FullName+'::'+$Name)
    return $methods[0]
}
function Uses($Method,[string]$Name) { return @($Method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MemberReference] -and $_.Operand.Name -eq $Name}).Count -gt 0 }
$pupInputs=@()
foreach($pupVersion in $PupVersions){
$pupPath=Join-Path $CacheDirectory ('PUP82-PUP_FPS/'+$pupVersion+'/PUP_FPS.dll')
$nativeHash=(Get-FileHash -LiteralPath $pupPath -Algorithm SHA256).Hash

Inspect $pupPath {
    param($assembly)
    $pup=$assembly.MainModule.Types|Where-Object FullName -eq 'ValheimInstanceCombiner.InstanceCombinerMod'
    $metadata=@($pup.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInPlugin'})
    Check ($metadata.Count -eq 1 -and $metadata[0].ConstructorArguments[0].Value -eq 'com.pup.instancecombiner.safe' -and $metadata[0].ConstructorArguments[2].Value -eq $pupVersion) 'Native plugin identity changed.'
    $dict='System.Collections.Generic.Dictionary`2<UnityEngine.Vector2Int,'
    $set='System.Collections.Generic.HashSet`1<UnityEngine.Vector2Int>'
    $nested=$pup.FullName+'/'
    foreach($pair in @(
        @('multiplayerManifestReceived','System.Boolean'),@('multiplayerRejected','System.Boolean'),@('loadedZNet','ZNet'),
        @('collisionCoverageRevision','System.Int32'),@('zoneGuardRevision','System.Int32'),
        @('records',($dict+'System.Collections.Generic.List`1<'+$nested+'BakedRecord>>')),
        @('multiplayerRemoteCells',($dict+$nested+'RemoteCellManifest>')),@('fastColliderCells',($dict+$nested+'FastColliderCellState>')),
        @('restoreSupportCells',($dict+$nested+'RestoreSupportCell>')),@('zoneTransitions',($dict+'System.Int32>')),
        @('clusters',($dict+'UnityEngine.GameObject>')),@('cacheIds',($dict+'System.String>')),
        @('colliderCellActive',($dict+'System.Boolean>')),@('fastLoadedSubclusters',($dict+'System.Collections.Generic.HashSet`1<System.String>>')),
        @('validatedReplacementCacheIds','System.Collections.Generic.Dictionary`2<UnityEngine.GameObject,System.String>'),
        @('activeBodyCollisionCells',$set),@('multiplayerRemotePhysicsReady',$set),@('verifiedRemoteReplacements',$set),
        @('pendingWorldSaveRestores',$set),@('multiplayerPendingVanillaRestore',$set),@('loadingCells',$set),@('queuedColliderCount','System.Int32')
    )){Field $pup $pair[0] $pair[1]}
    foreach($pair in @(
        @('PrepareExactCollision','System.Boolean','UnityEngine.Vector3,System.Single'),
        @('IsZoneTransitionNear','System.Boolean','UnityEngine.Vector3,System.Single'),
        @('IsZoneReplacementReady','System.Boolean','UnityEngine.Vector3,System.Single'),
        @('AttachZoneBodyGuard',($nested+'ZoneBodyGuard'),'UnityEngine.GameObject'),
        @('PrepareMobCollision','System.Boolean','Character'),@('PreserveDependentSupport','System.Void','WearNTear,System.Single'),
        @('RepairDependentSupport','System.Void','WearNTear'),@('ShouldRetainActorCollisionCell','System.Boolean','UnityEngine.Vector2Int'),
        @('HasPendingCollidersForCell','System.Boolean','UnityEngine.Vector2Int')
    )){[void](Method $pup $pair[0] $pair[1] $pair[2])}
    $remote=$pup.NestedTypes|Where-Object Name -eq RemoteCellManifest
    $fast=$pup.NestedTypes|Where-Object Name -eq FastColliderCellState
    $body=$pup.NestedTypes|Where-Object Name -eq ZoneBodyGuard
    Field $remote 'cacheId' 'System.String'; Field $fast 'cacheId' 'System.String'
    Field $fast 'root' 'UnityEngine.GameObject'; Field $fast 'required' 'System.Collections.Generic.HashSet`1<System.String>'
    Field $body 'coverage' 'System.Int32'
    $step=Method $body 'Step' 'System.Boolean'
    Check ((Uses $step 'IsZoneReplacementReady') -and (Uses $step 'set_isKinematic')) 'Native body freeze implementation changed.'
    $ready=$pup.Methods|Where-Object Name -eq IsZoneReplacementReady
    Check (Uses $ready 'IsDedicated') 'Dedicated readiness branch missing.'
    Check ((Uses $ready 'HasPreparedDedicatedCollision') -eq ($pupVersion -eq '1.0.36')) 'Prepared dedicated collision proof changed.'
    if($pupVersion -eq '1.0.34'){
        $code=$ready.Body.Instructions
        $call=@($code | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'IsDedicated'})[0]
        $branch=$code[$code.IndexOf($call)+1]; $target=$branch.Operand
        Check ($branch.OpCode.Name -like 'brtrue*' -and $target -is [Mono.Cecil.Cil.Instruction] -and
            $target.OpCode.Name -like 'ldloc*' -and $target.Next.OpCode.Name -eq 'ldc.i4.1' -and $target.Next.Next.OpCode.Name -eq 'add') 'Legacy dedicated skip must match production startup recognizer.'
    }
    $retain=$pup.Methods|Where-Object Name -eq ShouldRetainActorCollisionCell
    Check ((Uses $retain 'activeBodyCollisionCells') -eq ($pupVersion -eq '1.0.36')) 'Native body-cell retention proof changed.'
    $visibility=$pup.Methods|Where-Object Name -eq UpdateVisibleClusters
    $activation=$pup.Methods|Where-Object Name -eq UpdateColliderActivation
    Check ((Uses $visibility 'ShouldRetainActorCollisionCell') -and (Uses $activation 'activeBodyCollisionCells')) 'Native retention/activation contracts changed.'
    $repair=$pup.Methods|Where-Object Name -eq RepairDependentSupport
    $instructions=$repair.Body.Instructions
    $gates=@(0..($instructions.Count-2)|Where-Object {$instructions[$_].Operand -is [Mono.Cecil.FieldReference] -and $instructions[$_].Operand.Name -eq 'm_noSupportWear'})
    Check ($gates.Count -eq 1 -and $instructions[$gates[0]+1].OpCode.Name -like 'brfalse*') 'Native support-wear gate is not already corrected.'
    $stream=$assembly.MainModule.Types|Where-Object FullName -eq ServerZoneStreamBlock
    Field $stream 'initialized' 'System.Boolean'; [void](Method $stream 'Initialize' 'System.Void')
}
$pupInputs+=@{version=$pupVersion;sha256=$nativeHash}
}
$fgnInputs=@()
foreach($version in @('1.5.36','1.5.41','1.5.53')){
    $path=Join-Path $CacheDirectory ('VerdantsAscent-FiresGhettoNetworking/'+$version+'/VAGhettoNetworking.dll')
    if($version -eq '1.5.53'){$path=Join-Path $CacheDirectory ('VerdantsAscent-FiresGhettoNetworking/'+$version+'/plugins/FiresGhettoNetworking/VAGhettoNetworking.dll')}
    Inspect $path {
        param($assembly)
        $handoff=$assembly.MainModule.Types|Where-Object FullName -eq 'FiresGhettoNetworkMod.OwnershipHandoffPatches'
        [void](Method $handoff 'ZSyncTransform_CustomFixedUpdate_Prefix' 'System.Boolean' 'ZSyncTransform,ZNetView,UnityEngine.Rigidbody')
        $echo=$assembly.MainModule.Types|Where-Object FullName -eq 'FiresGhettoNetworkMod.OwnedPositionEcho'
        $receive=Method $echo 'Receive_Finalizer' 'System.Void'
        Check ($receive.IsStatic -and $receive.IsPrivate -and (Uses $receive 'InternalSetPosition')) 'FGN echo implementation changed.'
        Check (@($echo.Fields|Where-Object {$_.Name -eq 's_moved' -and $_.IsStatic -and $_.FieldType.FullName -eq 'System.Collections.Generic.List`1<System.Collections.Generic.KeyValuePair`2<ZDO,ZNetView>>'}).Count -eq 1) 'FGN echo list contract changed.'
    }
    $fgnInputs+=@{version=$version;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
}
Inspect (Join-Path $ValheimDirectory 'valheim_Data/Managed/assembly_valheim.dll') {
    param($assembly)
    $wear=$assembly.MainModule.Types|Where-Object FullName -eq WearNTear
    $boundsField=$wear.Fields|Where-Object Name -eq m_bounds
    Check ($boundsField.FieldType -is [Mono.Cecil.GenericInstanceType]) 'Native bounds list changed.'
    $bounds=Types $assembly.MainModule.Types|Where-Object FullName -eq $boundsField.FieldType.GenericArguments[0].FullName
    Field $bounds 'm_pos' 'UnityEngine.Vector3'; Field $bounds 'm_size' 'UnityEngine.Vector3'; Field $bounds 'm_rot' 'UnityEngine.Quaternion'
    Field $wear 'm_nview' 'ZNetView'; Field $wear 'm_colliders' 'UnityEngine.Collider[]'
    foreach($pair in @(@('SetupColliders','System.Void'),@('ClearCachedSupport','System.Void'),@('HaveSupport','System.Boolean'),@('UpdateSupport','System.Void'))){[void](Method $wear $pair[0] $pair[1])}
    $wearUpdate=Method $wear 'UpdateWear' 'System.Void' 'System.Single'
    Check ((Uses $wearUpdate 'm_noSupportWear') -and (Uses $wearUpdate 'UpdateSupport') -and (Uses $wearUpdate 'HaveSupport')) 'Native structural wear branch changed.'
    $sync=$assembly.MainModule.Types|Where-Object FullName -eq ZSyncTransform
    Field $sync 'm_wasOwner' 'System.Boolean'; Field $sync 'm_nview' 'ZNetView'
    [void](Method $sync 'CustomFixedUpdate' 'System.Void' 'System.Single')
}
$dll=Join-Path $PSScriptRoot 'bin/TooManyLags.dll'
Inspect $dll {
    param($assembly)
    $types=@(Types $assembly.MainModule.Types | Where-Object {$_.FullName.StartsWith('TooManyLags.PupCompatibility.',[StringComparison]::Ordinal)})
    $instructions=@($types.Methods|Where-Object HasBody|ForEach-Object {$_.Body.Instructions})
    Check ($types.Count -gt 10) 'Embedded PUP guard types missing.'
    Check (@($types.Methods|Where-Object {$_.Name -in @('FixedUpdate','Update','LateUpdate')}).Count -eq 0) 'Optional module adds a permanent update loop.'
    Check (@($types|Where-Object {$_.BaseType.FullName -in @('BepInEx.BaseUnityPlugin','UnityEngine.MonoBehaviour')}).Count -eq 0) 'Optional module creates a Unity component.'
    Check (@($instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'UnityEngine.Rigidbody' -and $_.Operand.Name -like 'set_*'}).Count -eq 0) 'Embedded guard writes physics state instead of reusing native PUP.'
    Check (@($instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'UnityEngine.Transform' -and $_.Operand.Name -in @('set_position','set_rotation')}).Count -eq 0) 'Embedded guard adds position correction.'
    Check (@($instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'ZDO' -and $_.Operand.Name -eq 'SetOwner'}).Count -eq 0) 'Embedded guards install a second ownership writer.'
    Check (@($types|Where-Object {$_.Name -in @('FgnCombatOwnership','FgnTargetEvents','FgnAttackRetry','FgnCombatStatus')}).Count -eq 0) 'Imported guards duplicate existing TML combat policy.'
    $native=$types|Where-Object FullName -eq TooManyLags.PupCompatibility.NativePup
    $ctor=$native.Methods|Where-Object Name -eq .ctor
    Check (!(Uses $ctor 'ComputeHash') -and (Uses $ctor 'Calls') -and (Uses $ctor 'ReadsField') -and (Uses $ctor 'LegacyDedicatedSkip')) 'Embedded release must retain capability checks without a version lock.'
    $wait=$types|Where-Object FullName -eq TooManyLags.PupCompatibility.NativeWaitPatch
    Check (Uses ($wait.Methods|Where-Object Name -eq Postfix) 'PreparedPoint') 'New native readiness lost read-only restore/recipe checks.'
    $retention=$types|Where-Object FullName -eq TooManyLags.PupCompatibility.NativeRetentionPatch
    Check (Uses ($retention.Methods|Where-Object Name -eq Prepare) 'NativeBodyRetention') 'Native retention must suppress the redundant supplement.'
    $module=$types|Where-Object FullName -eq TooManyLags.PupCompatibility.Plugin
    $start=$module.Methods|Where-Object Name -eq Start
    Check ((Uses $start 'Rewrite') -and (Uses $start 'CreateClassProcessor') -and (Uses $start 'Install')) 'Embedded guard installation missing.'
    $plugin=$assembly.MainModule.Types|Where-Object FullName -eq TooManyLags.Plugin
    Check (Uses ($plugin.Methods|Where-Object Name -eq Awake) 'Start') 'Root startup does not install the optional module.'
    Check (Uses ($plugin.Methods|Where-Object Name -eq OnDestroy) 'Dispose') 'Root teardown does not dispose the module.'
    foreach($method in $plugin.Methods | Where-Object {$_.HasBody -and $_.Name -ne 'Awake' -and $_.Name -ne 'OnDestroy'}) {
        Check (@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName.StartsWith('TooManyLags.PupCompatibility.',[StringComparison]::Ordinal)}).Count -eq 0) ('Optional guard work leaked into normal runtime: '+$method.Name)
    }
    Check (@($assembly.MainModule.AssemblyReferences|Where-Object {$_.Name -in @('PUP_FPS','BotanicPupPatch')}).Count -eq 0) 'Embedded guards retain an external assembly dependency.'
}
$provenance=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'tests/pup-import-equivalence.json') -Raw | ConvertFrom-Json
foreach($file in $provenance.files) {
    Check ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('src/PupCompatibility/'+$file.file)) -Algorithm SHA256).Hash -eq $file.import_sha256) ('Imported PUP guard changed without equivalence review: '+$file.file)
}
@{
    native_contract_assertions=$script:checks;pup=$pupInputs;fgn=$fgnInputs;
    dll_sha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash;
    own_update_loop=$false;own_rigidbody_writes=$false;own_transform_correction=$false;
    scope='Actual native/release metadata and IL; no live Unity or network test.'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'bin/pup-compatibility-tests/native-verification.json') -Encoding UTF8
Write-Output ('PASS: '+$script:checks+' actual native/embedded PUP guard contracts; no extra background loop or physics-state writes.')
