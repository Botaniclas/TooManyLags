using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;

internal static class NativeCombatAudit
{
    private static int assertions;
    private static void Check(bool value,string label){assertions++;if(!value)throw new Exception(label);}
    private static MethodDefinition Method(TypeDefinition type,string name){return type.Methods.Single(m=>m.Name==name);}
    private static void Field(TypeDefinition type,string name,string expected)
    {
        FieldDefinition field=type.Fields.Single(f=>f.Name==name);
        Check(!field.IsStatic&&field.FieldType.FullName==expected,"optional PUP field contract "+type.Name+"."+name);
    }
    private static void AuditPup(string path,TypeDefinition adapter)
    {
        string hash;
        using(SHA256 sha=SHA256.Create())using(Stream input=File.OpenRead(path))
            hash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
        bool modern=hash=="C685B43D90C99BA21A8C9477D095BE76CFAF703C9F3430FD8D678302CE8308F9";
        Check(modern||hash=="47DEADE1F80117821462E69D95752F666AFF6AA8752988852D0315B0E4ABA04B","PUP1.0.34/1.0.36 audit provenance; no runtime version lock");
        Check(!adapter.Methods.Where(m=>m.HasBody).Any(m=>m.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="ComputeHash"))&&
            !adapter.Fields.Any(f=>f.Name=="Hash"),"release readiness adapter has no DLL hash lock");
        Check(Method(adapter,".ctor").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="ValidateDedicatedReadiness")&&
            Method(adapter,"ValidateDedicatedReadiness").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="GetOriginalInstructions"),"release constructor validates dedicated capability once at startup");
        Check(!Method(adapter,"ReadyPointCore").Body.Instructions.Any(i=>i.Operand is MethodReference&&new[]{"GetMethod","GetOriginalInstructions","ValidateDedicatedReadiness"}.Contains(((MethodReference)i.Operand).Name)),"runtime readiness performs no reflection or startup IL validation");
        using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(path))
        {
            TypeDefinition pup=assembly.MainModule.Types.Single(t=>t.FullName=="ValheimInstanceCombiner.InstanceCombinerMod");
            CustomAttribute metadata=pup.CustomAttributes.Single(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin");
            Check((string)metadata.ConstructorArguments[0].Value=="com.pup.instancecombiner.safe"&&
                (string)metadata.ConstructorArguments[2].Value==(modern?"1.0.36":"1.0.34"),"verified optional PUP audit identity");
            MethodDefinition ready=Method(pup,"IsZoneReplacementReady");
            Check(ready.ReturnType.FullName=="System.Boolean"&&String.Join(",",ready.Parameters.Select(p=>p.ParameterType.FullName).ToArray())=="UnityEngine.Vector3,System.Single","actual native replacement readiness signature");
            if(modern)
            {
                MethodDefinition prepared=Method(pup,"HasPreparedDedicatedCollision");
                Check(!prepared.IsStatic&&prepared.ReturnType.FullName=="System.Boolean"&&String.Join(",",prepared.Parameters.Select(p=>p.ParameterType.FullName).ToArray())=="UnityEngine.Vector2Int,UnityEngine.Vector3,System.Single","actual dedicated proxy query signature");
                Check(ready.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).FullName==prepared.FullName),"native replacement readiness calls dedicated proxy query");
                Check(prepared.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="PlantSupportCellCurrent")&&
                    prepared.Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="incomplete")&&
                    prepared.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="get_enabled")&&
                    prepared.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="get_activeInHierarchy"),"native proxy proof retains revision/completion/active collider gates");
            }
            else
            {
                Check(!pup.Methods.Any(m=>m.Name=="HasPreparedDedicatedCollision"),"legacy PUP has no dedicated proxy query");
                var body=ready.Body.Instructions.ToList();
                int dedicated=body.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name=="ZNet"&&((MethodReference)i.Operand).Name=="IsDedicated");
                Check(dedicated>=0&&(body[dedicated+1].OpCode.Name=="brtrue"||body[dedicated+1].OpCode.Name=="brtrue.s"),"legacy native dedicated branch jumps when dedicated");
                var target=body[dedicated+1].Operand as Mono.Cecil.Cil.Instruction;
                int at=body.IndexOf(target);
                Check(at>=0&&body[at].OpCode.Name.StartsWith("ldloc",StringComparison.Ordinal)&&body[at+1].OpCode.Name=="ldc.i4.1"&&body[at+2].OpCode.Name=="add","legacy dedicated skip lands directly at the loop increment");
            }
            TypeDefinition cells=adapter.Module.Types.Single(t=>t.FullName.StartsWith("TooManyLags.TypedCellReadiness`",StringComparison.Ordinal));
            Check(cells.Fields.Any(f=>f.Name=="dedicatedPrepared"&&f.FieldType.FullName=="System.Func`4<UnityEngine.Vector2Int,UnityEngine.Vector3,System.Single,System.Boolean>"),"adapter stores typed dedicated proxy query");
            Check(Method(cells,".ctor").Body.Instructions.Any(i=>i.OpCode.Name=="ldstr"&&(string)i.Operand=="HasPreparedDedicatedCollision")&&
                Method(cells,"Ready").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="Invoke"&&((MethodReference)i.Operand).DeclaringType.FullName.StartsWith("System.Func`4<",StringComparison.Ordinal)),"release cell readiness binds and invokes optional native dedicated proof");
            string dict="System.Collections.Generic.Dictionary`2<UnityEngine.Vector2Int,",nested=pup.FullName+"/";
            string set="System.Collections.Generic.HashSet`1<UnityEngine.Vector2Int>";
            foreach(string[] pair in new[]{
                new[]{"multiplayerManifestReceived","System.Boolean"},new[]{"multiplayerRejected","System.Boolean"},new[]{"loadedZNet","ZNet"},
                new[]{"records",dict+"System.Collections.Generic.List`1<"+nested+"BakedRecord>>"},
                new[]{"multiplayerRemoteCells",dict+nested+"RemoteCellManifest>"},new[]{"restoreSupportCells",dict+nested+"RestoreSupportCell>"},
                new[]{"zoneTransitions",dict+"System.Int32>"},new[]{"fastColliderCells",dict+nested+"FastColliderCellState>"},
                new[]{"clusters",dict+"UnityEngine.GameObject>"},new[]{"cacheIds",dict+"System.String>"},
                new[]{"validatedReplacementCacheIds","System.Collections.Generic.Dictionary`2<UnityEngine.GameObject,System.String>"},
                new[]{"colliderCellActive",dict+"System.Boolean>"},new[]{"fastLoadedSubclusters",dict+"System.Collections.Generic.HashSet`1<System.String>>"},
                new[]{"multiplayerRemotePhysicsReady",set},new[]{"verifiedRemoteReplacements",set},new[]{"pendingWorldSaveRestores",set},
                new[]{"multiplayerPendingVanillaRestore",set},new[]{"loadingCells",set},new[]{"queuedColliderCount","System.Int32"}
            })Field(pup,pair[0],pair[1]);
            foreach(string[] pair in new[]{new[]{"PrepareExactCollision","UnityEngine.Vector3,System.Single"},
                new[]{"IsZoneTransitionNear","UnityEngine.Vector3,System.Single"},new[]{"HasPendingCollidersForCell","UnityEngine.Vector2Int"}})
            {
                MethodDefinition method=Method(pup,pair[0]);
                Check(!method.IsStatic&&method.ReturnType.FullName=="System.Boolean"&&
                    String.Join(",",method.Parameters.Select(p=>p.ParameterType.FullName).ToArray())==pair[1],"optional PUP readiness method "+pair[0]);
            }
            Field(pup.NestedTypes.Single(t=>t.Name=="RemoteCellManifest"),"cacheId","System.String");
            TypeDefinition fast=pup.NestedTypes.Single(t=>t.Name=="FastColliderCellState");
            Field(fast,"cacheId","System.String");Field(fast,"root","UnityEngine.GameObject");Field(fast,"required","System.Collections.Generic.HashSet`1<System.String>");
        }
    }
    public static void Main(string[] args)
    {
        string inputHash;
        using(SHA256 sha=SHA256.Create())using(Stream stream=File.OpenRead(args[0]))
            inputHash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
        bool possessionRelease=inputHash=="E43F8E7B7614D0C6B7C8437CD235D837851955A41ACE1238858EC5FCEF75B2CB"||
            inputHash=="C11FF8A96B1A7AE4BB76A433BC26447BD914C31797C6E1EBA557217C2FB9399D"||
            inputHash=="41B18BF36F837620DC5A76DE3BEE0569555B699AAA10722B8D95C07A70A638B2";
        Check(possessionRelease||inputHash=="75F5446AD4E43529F57D2C56F8CE34E061A78B289AE88DE6DE6B3CE69506E292",
            "audit input is exact original FGN1.5.36,1.5.41,1.5.53 or1.5.65; hash is test provenance, not runtime gating");
        using(AssemblyDefinition fgn=AssemblyDefinition.ReadAssembly(args[0]))
        using(AssemblyDefinition native=AssemblyDefinition.ReadAssembly(Path.Combine(args[1],"assembly_valheim.dll")))
        using(AssemblyDefinition core=AssemblyDefinition.ReadAssembly(Path.Combine(args[1],"mscorlib.dll")))
        using(AssemblyDefinition addon=AssemblyDefinition.ReadAssembly(args[2]))
        {
            TypeDefinition dictionary=core.MainModule.Types.Single(t=>t.FullName=="System.Collections.Generic.Dictionary`2");
            Check(dictionary.Methods.Count(m=>m.Name=="Remove"&&m.IsPublic)==2,"actual Unity Dictionary has two public Remove overloads");
            Check(dictionary.Methods.Single(m=>m.Name=="Remove"&&m.IsPublic&&m.Parameters.Count==1).Parameters[0].ParameterType.Name=="TKey","actual one-argument Remove contract");
            Check(dictionary.Methods.Count(m=>m.Name=="TryGetValue"&&m.IsPublic)==1,"remaining name-only Dictionary read lookup is unambiguous in actual Unity corelib");
            MethodDefinition bridgeCtor=Method(addon.MainModule.Types.Single(t=>t.Name=="FgnCombatBridge"),".ctor");
            Check(!bridgeCtor.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.FullName=="System.Type"&&((MethodReference)i.Operand).Name=="GetMethod"&&((MethodReference)i.Operand).Parameters.Count==1),"bridge constructor has no ambiguous name-only method lookup");
            Check(bridgeCtor.Body.Instructions.Count(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="GetMethod"&&((MethodReference)i.Operand).Parameters.Count==2&&((MethodReference)i.Operand).Parameters[1].ParameterType.FullName=="System.Type[]")==2,"existing dictionary removal delegates bind explicit key-only overload");
            TypeDefinition ownership=fgn.MainModule.Types.Single(t=>t.FullName=="FiresGhettoNetworkMod.CreatureOwnership");
            foreach(var expected in new[]{Tuple.Create("PassSeconds",(object)2.0),Tuple.Create("PendingSeconds",(object)5.0),
                Tuple.Create("DeclinedCooldownSeconds",(object)6.0),Tuple.Create("ForwardHitsSeconds",(object)5.0),
                Tuple.Create("MaxHandOffsPerPass",(object)16),Tuple.Create("MaxHandOffsPerOwner",(object)6)})
                Check(ownership.Fields.Any(f=>f.Name==expected.Item1&&f.IsLiteral&&Equals(f.Constant,expected.Item2)),
                    "actual startup timing/budget contract "+expected.Item1);
            Check(bridgeCtor.Body.Instructions.Count(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="RequireConstant")==6,
                "bridge validates shared timings/budgets once during binding");
            MethodDefinition updateCaller=Method(fgn.MainModule.Types.Single(t=>t.Name=="FgnHooks").NestedTypes.Single(t=>t.Name=="ZDOManUpdate"),"Postfix");
            Check(updateCaller.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="Balance")&&
                !updateCaller.Body.Instructions.Any(i=>i.Operand is MethodReference&&new[]{"IsDedicated","IsServer"}.Contains(((MethodReference)i.Operand).Name)),"actual FGN ZDO update caller invokes Balance without server/client gate");
            Check(Method(ownership,"Active").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsDedicated"),"original FGN Active is false on ordinary clients");
            MethodDefinition handoff=Method(ownership,"TryHandOffCreature");FieldReference context=null;int reads=0;
            foreach(var instruction in handoff.Body.Instructions)
            {
                if(instruction.OpCode.Name=="ldsfld")context=instruction.Operand as FieldReference;
                MethodReference call=instruction.Operand as MethodReference;
                if(context!=null&&context.Name=="s_lastHitBy"&&call!=null&&call.Name=="TryGetValue"&&call.DeclaringType.FullName==context.FieldType.FullName)reads++;
            }
            Check(reads==1,"exact single FGN combat read site");
            Check(handoff.Body.Instructions.Count(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="InActiveArea")==2,"FGN eligibility and candidate-area gates retained");
            Check(handoff.Body.Instructions.Any(i=>i.OpCode.Name=="ldc.r4"&&(float)i.Operand==250f),"FGN fighter250ms allowance exists");
            Check(handoff.Body.Instructions.Any(i=>i.OpCode.Name=="ldc.r8"&&(double)i.Operand==10.0),"FGN ten-second lower-ping hold exists");
            Check(ownership.Fields.Single(f=>f.Name=="s_owned").FieldType.FullName.Contains("System.Collections.Generic.Dictionary`2<ZDOID,FiresGhettoNetworkMod.CreatureOwnership/Owned>"),"FGN private owned-cache binding contract");
            Check(handoff.Body.Instructions.Any(i=>i.OpCode.Name=="ldc.i4.2"),"FGN two-win gate exists");
            Check(handoff.Body.Instructions.Any(i=>i.OpCode.Name=="ldc.i4.6"),"FGN six moves per owner exists");
            MethodDefinition balance=Method(ownership,"BalanceCreaturesNearPlayers");
            Check(ownership.Fields.Single(f=>f.Name=="s_candidates").FieldType.FullName=="System.Collections.Generic.List`1<ZNetPeer>","FGN original candidate-list binding contract");
            Check(balance.Body.Instructions.Any(i=>i.OpCode.Name=="ldc.i4.s"&&Convert.ToInt32(i.Operand)==16),"FGN sixteen moves per pass exists");
            MethodDefinition veto=Method(ownership,"Veto");
            var receive=Method(ownership,"OnHandOff").Body.Instructions.ToList();
            int vetoAt=receive.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="Veto"),
                sendAt=receive.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="ForceSendZDO"),
                ownerAt=receive.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="SetOwner");
            Check(vetoAt>=0&&sendAt>vetoAt&&ownerAt>sendAt,"actual FGN receive final veto precedes ForceSend and owner assignment");
            foreach(var method in new[]{handoff,Method(ownership,"OnHandOff")})
                Check(method.Body.Instructions.Count(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name=="ZRpc"&&((MethodReference)i.Operand).Name=="Invoke")==1,
                    "exact single native mob request/result RPC site "+method.Name);
            TypeDefinition identified=addon.MainModule.Types.Single(t=>t.Name=="FgnMobHandoff");
            Check(Method(identified,"ReceiveCommit").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="Commit")&&
                !Method(identified,"ReceiveCommit").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="SetOwner"),
                "identified receiver delegates source commit to original FGN without new ownership writes");
            Check(Method(combatType(addon),"Install").Body.Instructions.Any(i=>i.OpCode.Name=="ldstr"&&(string)i.Operand=="RPC_ZDOData"),
                "owner replication uses native packet event rather than new physics polling");
            Check(!veto.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="GetTargetCreature"),"original final FGN veto does not revalidate current AI player target");
            Check(veto.Body.Instructions.Count(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="TryGetValue")==1,"exact single damage-memory veto read");
            foreach(string guard in new[]{"IsDead","IsBoss","IsTamed","InAttack","IsStaggering","HaveValidUser"})
                Check(veto.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name==guard),"FGN veto remains "+guard);
            var scan=Method(ownership,"ScanPrefabs");
            Check(scan.IsStatic&&scan.Parameters.Count==0&&scan.Body.Instructions.Count(i=>i.Operand is MethodReference&&
                ((MethodReference)i.Operand).DeclaringType.Name=="Character"&&((MethodReference)i.Operand).Name=="IsBoss")==1,
                "actual FGN prefab scan has exactly one boss exclusion for narrow replacement");
            TypeDefinition boss=addon.MainModule.Types.Single(t=>t.Name=="FgnBossPolicy");
            Check(Method(boss,"VetoBoss").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="AuthorizesBoss"),
                "boss veto exception requires an identified managed source commit");
            Check(Method(boss,"Record").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="TwoNearby")&&
                !Method(boss,"WantsServer").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="TwoNearby"),
                "boss radius evaluates on target report and handoff retries reuse cached decision");
            Check(!boss.Methods.Any(m=>m.Name=="Update"||m.Name=="FixedUpdate"||m.Name.Contains("Movement")||m.Name.Contains("Position")),
                "boss module adds no physics/frame/movement polling");
            Check(Method(ownership,"OnHandOff").Body.Instructions.All(i=>!(i.Operand is MethodReference)||
                !new[]{"Sleep","ThreadSleep","SetSleeping","SetActive"}.Contains(((MethodReference)i.Operand).Name)),
                "native FGN handoff imposes no timed AI suspension");
            if(possessionRelease)
            {
                Check(veto.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsPossessed"),"FGN1.5.41 native possession veto retained");
                TypeDefinition possession=fgn.MainModule.Types.Single(t=>t.Name=="ValcastPossession");
                Check(Method(possession,"IsPossessed").IsStatic&&Method(possession,"IsPossessed").ReturnType.FullName=="System.Boolean"&&
                    Method(possession,"IsPossessed").Parameters.Single().ParameterType.FullName=="ZDO","actual optional possession delegate contract");
            }
            MethodDefinition result=Method(ownership,"OnResult");
            Check(result.IsPrivate&&result.IsStatic&&result.ReturnType.FullName=="System.Void"&&
                result.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"ZRpc","ZDOID","System.Boolean","System.String"}),"actual private FGN result signature matches exact bridge binding");
            Check(result.Body.Instructions.Any(i=>i.OpCode.Name=="ldc.r8"&&(double)i.Operand==6.0)&&
                result.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="get_realtimeSinceStartupAsDouble"),"native rejection cooldown uses realtime plus six seconds");
            TypeDefinition gameAI=native.MainModule.Types.Single(t=>t.Name=="MonsterAI");MethodDefinition update=Method(gameAI,"UpdateTarget");
            var aiUpdate=Method(gameAI,"UpdateAI").Body.Instructions.ToList();
            int targetAt=aiUpdate.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="UpdateTarget"),
                attackAt=aiUpdate.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="DoAttack");
            Check(targetAt>=0&&attackAt>targetAt,"native AI can select a target then start attack in the same update");
            TypeDefinition human=native.MainModule.Types.Single(t=>t.Name=="Humanoid");
            var humanUpdate=Method(human,"CustomFixedUpdate").Body.Instructions.ToList();
            int ownAt=humanUpdate.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsOwner"),
                tickAt=humanUpdate.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="UpdateAttack");
            Check(ownAt>=0&&tickAt>ownAt&&humanUpdate.Skip(ownAt+1).Take(tickAt-ownAt-1).Any(i=>i.OpCode.Name.StartsWith("brfalse")),"native attack ticking is gated by current network ownership");
            Check(Method(human,"OnAttackTrigger").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsOwner"),"native attack damage trigger checks current owner");
            Check(update.IsPrivate&&update.Parameters.Count==4&&update.Parameters[2].IsOut&&update.Parameters[3].IsOut,"native MonsterAI target-update contract");
            Check(Method(gameAI,"GetTargetCreature").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="m_targetCreature"),"native target getter is real local AI target");
            TypeDefinition targetSync=fgn.MainModule.Types.Single(t=>t.Name=="TargetSync");
            MethodDefinition syncPrefix=Method(targetSync,"SetTargetInfo_Prefix"), adopt=Method(targetSync,"AdoptOnHandover");
            Check(syncPrefix.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="AdoptOnHandover"),"FGN target sync retains handover adoption inside target-info prefix");
            Check(adopt.Body.Instructions.Any(i=>i.OpCode.Name=="stfld"&&i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="m_targetCreature"),"FGN adoption updates the actual AI target read by our event notification");
            Check(adopt.Body.Instructions.Any(i=>i.OpCode.Name=="ldc.r4"&&(float)i.Operand==60f),"FGN adoption retains its sixty-metre range limit");
            Check(new[]{"IsTamed","IsDead","IsEnemy"}.All(name=>adopt.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name==name)),"FGN adoption retains tamed, dead and enemy validation");
            TypeDefinition baseAI=native.MainModule.Types.Single(t=>t.Name=="BaseAI");
            Check(Method(baseAI,"SetTargetInfo").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="s_haveTargetHash"),"native network target info contains boolean");
            Check(!Method(baseAI,"SetTargetInfo").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).FullName.Contains("ZDOID)")),"native target ID is not replicated by SetTargetInfo");
            TypeDefinition manager=native.MainModule.Types.Single(t=>t.Name=="ZDOMan");
            Check(Method(manager,"ReleaseNearbyZDOS").Body.Instructions.Count(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="SetOwner")==2,"native two ownership writes match wrapper");
            TypeDefinition net=native.MainModule.Types.Single(t=>t.Name=="ZNet");
            Check(Method(net,"SetCharacterID").Parameters.Single().ParameterType.Name=="ZDOID","spawn capability publication contract");
            TypeDefinition data=native.MainModule.Types.Single(t=>t.Name=="ZDOExtraData");
            Check(Method(data,"GetSaveData").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="s_sessionOnly"),"session-only fields excluded during save");
            TypeDefinition combat=addon.MainModule.Types.Single(t=>t.Name=="FgnCombatOwnership");
            TypeDefinition plugin=addon.MainModule.Types.Single(t=>t.FullName=="TooManyLags.Plugin");
            var startPolicy=Method(plugin,"InitializeOwnership").Body.Instructions.ToList();
            Check(new[]{"Start","ApplyServerSession"}.All(name=>Method(plugin,name).Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="InitializeOwnership")),
                "server startup and authenticated client startup use the same guarded policy initializer");
            int policyCheck=startPolicy.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name=="MobPolicyHandoff"&&((MethodReference)i.Operand).Name=="Begin"),
                policyInstall=startPolicy.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name=="FgnCombatOwnership"&&((MethodReference)i.Operand).Name=="Install");
            Check(policyCheck>=0&&policyInstall>policyCheck,"actual startup suspends previous combat before installing addon hooks");
            TypeDefinition policyHandoff=addon.MainModule.Types.Single(t=>t.FullName=="TooManyLags.MobPolicyHandoff");
            var begin=Method(policyHandoff,"Begin").Body.Instructions;
            Check(new[]{"Enabled","Stop","Install"}.All(value=>begin.Any(i=>i.OpCode.Name=="ldstr"&&(string)i.Operand==value))&&
                (string)policyHandoff.Fields.Single(f=>f.Name=="Owner").Constant=="botanic.pup.patch.fgn.aiownership",
                "actual bridge binds only the optional combat contract and exact combat Harmony owner");
            Check(new[]{"GetField","GetValue","GetMethod","CreateDelegate"}.All(name=>begin.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name==name))&&
                !begin.Any(i=>i.Operand is MethodReference&&new[]{"SetValue","UnpatchSelf"}.Contains(((MethodReference)i.Operand).Name)),
                "actual bridge uses module lifecycle methods without editing config or removing guards itself");
            int commit=startPolicy.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name=="MobPolicyHandoff"&&((MethodReference)i.Operand).Name=="Commit"),
                dispose=startPolicy.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name=="MobPolicyHandoff"&&((MethodReference)i.Operand).Name=="Dispose");
            Check(commit>policyInstall&&dispose>commit&&Method(plugin,"InitializeOwnership").Body.ExceptionHandlers.Any(h=>h.HandlerType==Mono.Cecil.Cil.ExceptionHandlerType.Finally),
                "policy startup commits server takeover or retains client rollback and always disposes failed transactions");
            Check(startPolicy.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="clientTakeover")&&
                Method(plugin,"ClearClientSession").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name=="MobPolicyHandoff"&&((MethodReference)i.Operand).Name=="Dispose"),
                "client session retains and restores its suspended prior policy");
            Check(startPolicy.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).DeclaringType.Name=="FgnCombatOwnership"&&((FieldReference)i.Operand).Name=="Enabled")&&
                startPolicy.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).DeclaringType.Name=="LocalAgent"&&((FieldReference)i.Operand).Name=="MobsEnabled"),
                "actual startup checks installed normal combat or ready player-mode mob hooks before committing");
            TypeDefinition nativeCharacter=native.MainModule.Types.Single(t=>t.Name=="Character");
            MethodDefinition faction=Method(nativeCharacter,"GetFaction");
            Check(faction.ReturnType.FullName=="Character/Faction"&&faction.Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="m_faction")&&
                !faction.Body.Instructions.Any(i=>i.Operand is MethodReference),"native GetFaction reads raw faction without a taming conversion");
            TypeDefinition factions=nativeCharacter.NestedTypes.Single(t=>t.Name=="Faction");
            Check(Convert.ToInt32(factions.Fields.Single(f=>f.Name=="Players").Constant)==0&&
                Convert.ToInt32(factions.Fields.Single(f=>f.Name=="PlayerSpawned").Constant)==11,"native friendly Players and hostile PlayerSpawned factions remain distinct");
            TypeDefinition friendly=addon.MainModule.Types.Single(t=>t.FullName=="TooManyLags.FriendlyCreatures");
            MethodDefinition friendlyPredicate=Method(friendly,"IsFriendly");
            Check(friendly.Fields.Count==0&&new[]{"IsPlayer","GetFaction"}.All(name=>friendlyPredicate.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name==name))&&
                !friendlyPredicate.Body.Instructions.Any(i=>i.Operand is MethodReference&&new[]{"IsTamed","GetComponent","GetPrefab"}.Contains(((MethodReference)i.Operand).Name)),
                "shared friendly predicate is pure native faction plus non-player check without Tameable/prefab heuristics");
            Check(Convert.ToInt32(combat.Fields.Single(f=>f.Name=="PrefabFactionLimit").Constant)==1024&&
                combat.Fields.Single(f=>f.Name=="prefabFactionActors").FieldType.FullName=="System.Collections.Generic.Dictionary`2<System.Int32,Character>",
                "AI prefab classification cache is bounded and keeps actor references instead of immutable faction booleans");
            Check(new[]{"Eligible","ShouldOverride","Watching","ReceiveWatch","VetoPostfix","ReleaseManaged"}.All(name=>Method(combat,name).Body.Instructions.Any(i=>
                i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsFriendlyCreature")),"friendly exclusion covers eligibility, owner watches/reports, pending veto and lease cleanup");
            Check(Method(combat,"LoadedCharacter").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name=="FriendlyCreatures")&&
                Method(combat,"DamagePrefix").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name=="FriendlyCreatures"),
                "loaded server takeover and late-hit forwarding reject current friendly actors");
            Check(Method(combat,"Reset").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="ResetFactions")&&
                new[]{"Shutdown","OnDestroy","ResetFactions"}.All(name=>Method(combat,"Install").Body.Instructions.Any(i=>i.OpCode.Name=="ldstr"&&(string)i.Operand==name)),
                "session and native scene lifecycle clear faction cache without adding a timer");
            Check(!combat.Methods.Any(m=>m.Name=="VerifiedAssembly")&&!Method(combat,"Install").Body.Instructions.Any(i=>
                i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.FullName=="System.Version"),
                "runtime installation never gates on FGN version or DLL hash");
            Check(Method(combat,"Install").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="RewriteTarget")&&
                Method(combat,"Install").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="RewriteVeto"),
                "version-free installation still preflights the actual transpiler contracts");
            Check(new[]{"Eligible","ShouldOverride","ReleaseManaged"}.All(name=>Method(combat,name).Body.Instructions.Any(i=>
                i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="Possessed")),
                "possession exclusion covers client selection, server lease pinning and cleanup");
            var prefix=Method(combat,"BalancePrefix").Body.Instructions.ToList();
            int dedicated=prefix.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsDedicated"),
                ensure=prefix.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="EnsureWorld");
            Check(dedicated>=0&&ensure>dedicated&&prefix.Skip(dedicated+1).Take(ensure-dedicated-1).Any(i=>i.OpCode.Name=="ret"),"compiled addon client gate returns before server session/watch cleanup");
            Check(ownership.Fields.Single(f=>f.Name=="s_creaturePrefabs").FieldType.FullName=="System.Collections.Generic.HashSet`1<System.Int32>"&&
                ownership.Fields.Single(f=>f.Name=="s_visited").FieldType.FullName=="System.Collections.Generic.HashSet`1<ZDOID>"&&
                ownership.Fields.Single(f=>f.Name=="s_creatureTypeMask").FieldType.FullName=="System.Int32"&&
                ownership.Fields.Single(f=>f.Name=="s_prefabsScanned").FieldType.FullName=="System.Boolean","remote diagnostic reads actual original FGN scan field contracts");
            Check(!Method(combat,"Install").Body.Instructions.Any(i=>i.OpCode.Name=="ldstr"&&(string)i.Operand=="SetTargetInfo"),"optional feature does not replace FGN target-info prefix");
            Check(Method(combat,"Install").Body.Instructions.Any(i=>i.OpCode.Name=="ldstr"&&(string)i.Operand=="VetoPostfix"),"compiled addon installs final target validation at native veto");
            Check(new[]{"ResultPrefix","ResultPostfix"}.All(name=>Method(combat,"Install").Body.Instructions.Any(i=>i.OpCode.Name=="ldstr"&&(string)i.Operand==name)),"compiled addon installs scoped native result hooks");
            MethodDefinition retryGate=Method(combat,"ResultPrefix"),retry=Method(combat,"ResultPostfix");
            Check(retryGate.Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="Pending")&&
                new[]{"ShouldOverride","ReadTarget"}.All(name=>retryGate.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name==name))&&
                retryGate.Body.Instructions.Any(i=>i.OpCode.Name=="ldstr"&&(string)i.Operand=="attacking"),"attack retry scope checks existing pending request and active AI watch/report");
            Check(!new[]{retryGate,retry}.SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference&&new[]{"SetOwner","ForceSendZDO","GetPeers","InAttack"}.Contains(((MethodReference)i.Operand).Name)),"result hooks never directly force ownership, scan players or alter attack state");
            Check(retry.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="Remove")&&
                !retry.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="set_Item"),"result postfix removes new attack cooldown without replacing it with another delay");
            Check(!combat.Methods.Any(m=>m.Name=="ClearAttackCooldown")&&
                !combat.NestedTypes.Single(t=>t.Name=="Watch").Fields.Any(f=>f.Name=="AttackCooldown"),"old target-specific cooldown tracking and watch field removed");
            MethodDefinition targetEvent=Method(combat,"ProcessTargetEvent");
            Check(targetEvent.Parameters.Count==2&&targetEvent.Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="TryTransfer")&&
                !targetEvent.Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="Cooldowns"),"target events use existing FGN transfer path without cooldown management");
            Check(Method(ownership,"Balance").Body.Instructions.Any(i=>i.OpCode.Name=="ldc.r8"&&(double)i.Operand==2.0)&&
                Method(ownership,"Balance").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="s_nextPass"),"original FGN supplies existing two-second retry cadence without a new timer");
            MethodDefinition finalTarget=Method(combat,"VetoPostfix");
            Check(finalTarget.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="GetTargetCreature")&&
                !finalTarget.Body.Instructions.Any(i=>i.Operand is MethodReference&&new[]{"SetOwner","ForceSendZDO","Observe","GetPeers"}.Contains(((MethodReference)i.Operand).Name)),"final target guard reads actual owner AI without writes, sends or player scan");
            Check(Method(combat,"Observe").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="GetTargetCreature"),"optional feature reads final actual target including FGN adoption");
            Check(!addon.MainModule.AssemblyReferences.Any(a=>a.Name=="VAGhettoNetworking"),"no hard FGN assembly reference");
            Check(!combat.Methods.Any(m=>m.Name=="Update"||m.Name=="FixedUpdate"||m.Name.Contains("Coroutine")),"no added polling behavior");
            TypeDefinition attack=native.MainModule.Types.Single(t=>t.Name=="Attack");
            Check(human.Fields.Single(f=>f.Name=="m_currentAttack").FieldType.FullName=="Attack","native Humanoid active attack field matches cached typed getter");
            MethodDefinition attackStop=Method(attack,"Stop"),attackUpdate=Method(attack,"Update");
            Check(attackStop.IsPublic&&!attackStop.IsStatic&&attackStop.Parameters.Count==0&&attackStop.ReturnType.FullName=="System.Void","native attack stop event signature");
            Check(attackStop.Body.Instructions.Any(i=>i.OpCode.Name=="stfld"&&i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="m_attackDone"),"native Stop sets terminal attack flag");
            Check(Method(attack,"IsDone").ReturnType.FullName=="System.Boolean"&&Method(attack,"IsDone").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="m_attackDone"),"native IsDone reads exact terminal flag");
            Check(new[]{"InAttack","Stop"}.All(name=>attackUpdate.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name==name)),"native attack update ends animation through Stop");
            TypeDefinition attackRetry=addon.MainModule.Types.Single(t=>t.Name=="FgnAttackRetry");
            Check(Method(combat,"Install").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name=="FgnAttackRetry"&&((MethodReference)i.Operand).Name=="Install"),"optional AI feature installs end-event hook");
            Check(attackRetry.Fields.Any(f=>f.Name=="waiting"&&f.FieldType.FullName.Contains("ConditionalWeakTable`2<Attack,")),"pending attack tickets have weak Attack keys");
            Check(Method(attackRetry,"Stopped").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsDone")&&
                Method(attackRetry,"Stopped").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="Post"),"attack-end hook validates terminal state and defers beyond native stop");
            Check(!attackRetry.Methods.Any(m=>m.Name=="Update"||m.Name=="FixedUpdate"||m.Name.Contains("Coroutine")),"attack retry adds no recurring frame or physics callback");
            Check(Method(combat,"VetoPostfix").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="RememberAttack")&&
                Method(combat,"Request").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="RememberAttack"),"client attack rejection and server lease return arm the same event");
            Check(Method(combat,"OnAttackEnded").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="GetTargetCreature")&&
                !Method(combat,"OnAttackEnded").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="SetOwner"),"end callback rechecks actual current target without forcing ownership");
            Check(Method(combat,"ReceiveAttackReady").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="Pending")&&
                Method(combat,"HandoffAttackRejected").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="AttackReady")&&
                Method(combat,"ResultPostfix").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="HandoffAttackRejected"),"early ready events wait for pending result and are consumed after its cleanup");
            Check(Method(combat,"ServerReady").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.FullName=="TooManyLags.NativePup"&&((MethodReference)i.Operand).Name=="ReadyPoint"),"server takeover reuses optional PUP collision readiness");
            Check(!addon.MainModule.Types.Any(t=>t.Namespace=="TooManyLags"&&(t.Name=="CollisionGuard"||t.Name=="FgnTakeover"||t.Name=="Statics")),"combat policy remains separate from the optional PUP body/support module");
            TypeDefinition nativeAdapter=addon.MainModule.Types.Single(t=>t.FullName=="TooManyLags.NativePup");
            Check(!nativeAdapter.Methods.Any(m=>new[]{"Attach","Existing","ForceStep","ReadyBounds","NeededByNativeBody"}.Contains(m.Name))&&
                !nativeAdapter.Fields.Any(f=>new[]{"GuardType","ReadyMethod","MobMethod","PreserveMethod","RepairMethod","RetainMethod","ForceWait"}.Contains(f.Name)),
                "optional PUP adapter contains only readiness and no physics/support hook surface");
            Check(Method(combat,"ServerReady").Body.Instructions.Count(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsAreaReady")==2&&
                Method(combat,"ServerReady").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="get_Present"),
                "server fallback distinguishes absent PUP and verifies destination plus differing live-pose native area");
            MethodDefinition nativeArea=Method(native.MainModule.Types.Single(t=>t.Name=="ZNetScene"),"IsAreaReady");
            Check(new[]{"IsZoneLoaded","FindInstance"}.All(name=>nativeArea.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name==name)),
                "actual native area readiness requires zone loading and instantiated nearby valid prefabs");
            Check(!addon.MainModule.AssemblyReferences.Any(a=>a.Name=="PUP_FPS"||a.Name=="BotanicPupPatch"),"combat import has no PUP or PupPatch assembly dependency");
            if(args.Length>3)AuditPup(args[3],nativeAdapter);
            Check(Method(combat,"Request").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="ClearOwned"),"actual direct server return invalidates FGN hold cache");
            Check(Method(combat,"ReleaseManaged").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="ClearOwned"),"committed server-to-unowned cleanup invalidates FGN hold cache");
            Check(Method(combat,"Stop").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="NotifyServerOff"),"live stop sends server mode OFF before hook cleanup");
            Check(Method(combat,"OtherPlayerNearby").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="Candidates"),"nearby filter reads existing FGN candidate list");
            Check(!Method(combat,"OtherPlayerNearby").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="GetPeers"),"nearby filter does not independently collect connected peers");
            Check(Method(combat,"OtherPlayerNearby").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="GetSector")&&Method(combat,"OtherPlayerNearby").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="GetZone"),"nearby filter checks native mob sector and candidate scan centre");
            FieldDefinition actualTarget=gameAI.Fields.Single(f=>f.Name=="m_targetCreature");
            var targetWriters=gameAI.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Any(i=>i.OpCode.Name=="stfld"&&i.Operand is FieldReference&&((FieldReference)i.Operand).FullName==actualTarget.FullName)).ToArray();
            Check(targetWriters.Length==5&&targetWriters.Sum(m=>m.Body.Instructions.Count(i=>i.OpCode.Name=="stfld"&&i.Operand is FieldReference&&((FieldReference)i.Operand).FullName==actualTarget.FullName))==14,"all fourteen native target stores in five methods enumerated");
            Check(adopt.Body.Instructions.Count(i=>i.OpCode.Name=="stfld"&&i.Operand is FieldReference&&((FieldReference)i.Operand).FullName==actualTarget.FullName)==1,"exact one FGN adoption store observed separately");
            TypeDefinition eventSource=addon.MainModule.Types.Single(t=>t.Name=="FgnTargetEvents");
            Check(Method(eventSource,"Install").Body.Instructions.Any(i=>i.OpCode.Name=="ldstr"&&(string)i.Operand=="UnityEngine.UnitySynchronizationContext"),"event installation requires actual Unity main-thread context");
            Check(Method(eventSource,"Schedule").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="Post"),"notifications queued only through captured context Post");
            var assignInstructions=Method(eventSource,"AssignTarget").Body.Instructions.ToList();
            int writeIndex=assignInstructions.FindIndex(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="writeTarget"), compareIndex=assignInstructions.FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="ReferenceEquals");
            Check(writeIndex>=0&&compareIndex>writeIndex,"original target write precedes optional reference-change event");
            Check(assignInstructions.Count(i=>i.OpCode.Name=="isinst"&&i.Operand is TypeReference&&((TypeReference)i.Operand).Name=="Player")==2,"target events filter non-player to non-player stores with CLR type checks");
            MethodDefinition playerDestroy=Method(native.MainModule.Types.Single(t=>t.Name=="Player"),"OnDestroy");
            Check(playerDestroy.IsFamily&&playerDestroy.IsVirtual&&playerDestroy.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="OnDestroy"&&((MethodReference)i.Operand).DeclaringType.Name=="Humanoid"),"native player despawn invokes protected lifecycle cleanup");
            Check(Method(combat,"Install").Body.Instructions.Any(i=>i.OpCode.Name=="ldstr"&&(string)i.Operand=="PlayerDestroyPrefix"),"optional feature installs player-despawn invalidation hook");
            MethodDefinition invalidate=Method(combat,"PlayerDestroyPrefix");
            Check(invalidate.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="ReferenceEquals")&&
                !invalidate.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="GetZDOID"),"despawn invalidation matches actual reference without depending on a detached player ZDO");
            Check(!combat.Methods.Any(m=>m.Name=="MonsterAiPostfix"||m.Name=="OtherAiPostfix"),"old per-AI-update target observations removed");
            Check(Method(combat,"MarkWatch").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="TwoCandidates")&&Method(combat,"TwoCandidates").Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="Candidates"),"watch marking uses existing FGN candidate list and two-player filter");
            Check(Method(combat,"TryPrefix").Body.Instructions.Any(i=>i.OpCode.Name=="ldc.i4.s"&&Convert.ToInt32(i.Operand)==16),"shared event and scan cap checked before ordinary native handoff");
            Check(native.MainModule.Types.Single(t=>t.Name=="ZDO").Properties.Single(p=>p.Name=="OwnerRevision").PropertyType.FullName=="System.UInt16","actual owner revision is unsigned sixteen-bit");
            TypeDefinition rpc=native.MainModule.Types.Single(t=>t.Name=="ZRpc");
            Check(rpc.Methods.Any(m=>m.Name=="Register"&&m.GenericParameters.Count==3),"native three-argument connection RPC registration exists");
            Check(rpc.Methods.Any(m=>m.Name=="Register"&&m.GenericParameters.Count==2),"native two-argument diagnostic RPC registration exists");
            MethodDefinition serializer=Method(rpc,"Serialize");
            Check(new[]{"System.Int64","ZDOID"}.All(name=>serializer.Body.Instructions.Any(i=>i.OpCode.Name=="isinst"&&i.Operand is TypeReference&&((TypeReference)i.Operand).FullName==name))&&
                !serializer.Body.Instructions.Any(i=>i.OpCode.Name=="isinst"&&i.Operand is TypeReference&&((TypeReference)i.Operand).FullName=="System.UInt16"),"native RPC supports long and ZDOID but not boxed ushort");
            Check(new[]{"Observe","MarkWatch","RemoveWatch"}.All(name=>Method(combat,name).Body.Instructions.Any(i=>i.OpCode.Name=="box"&&i.Operand is TypeReference&&((TypeReference)i.Operand).FullName=="System.Int64")),"target and watch messages explicitly box revision as supported Int64");
            Console.WriteLine("PASS: "+assertions+" actual FGN/native/addon IL contracts; no native Unity methods executed.");
        }
    }
    private static TypeDefinition combatType(AssemblyDefinition assembly)
    { return assembly.MainModule.Types.Single(t=>t.Name=="FgnCombatOwnership"); }
}
