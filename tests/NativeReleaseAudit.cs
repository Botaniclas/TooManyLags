using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mono.Cecil;

internal static class NativeReleaseAudit
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static string[] directories;
    private static int assertions, targets;
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }
    private static Assembly Resolve(object sender, ResolveEventArgs request)
    {
        string filename = new AssemblyName(request.Name).Name + ".dll";
        foreach (string folder in directories)
        { string path = Path.Combine(folder, filename); if (File.Exists(path)) return Assembly.LoadFrom(path); }
        return null;
    }
    public static void Main(string[] arguments)
    {
        try
        {
            if (arguments.Length < 5 || arguments.Length > 6) throw new ArgumentException("Authority DLL, native managed, BepInEx core, FGN DLL, version label, optional pickable mode required");
            bool actorOnly = arguments.Length == 6 && arguments[5] == "pickable";
            directories = new[] { arguments[1], arguments[2], Path.GetDirectoryName(arguments[0]), Path.GetDirectoryName(arguments[3]) };
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            Assembly authority = Assembly.LoadFrom(arguments[0]);
            Assembly game = Assembly.LoadFrom(Path.Combine(arguments[1], "assembly_valheim.dll"));
            Assembly fgn = Assembly.LoadFrom(arguments[3]);
            using (DefaultAssemblyResolver resolver = new DefaultAssemblyResolver())
            {
                foreach (string folder in directories) resolver.AddSearchDirectory(folder);
                ReaderParameters reader = new ReaderParameters { AssemblyResolver = resolver };
                using (AssemblyDefinition module = AssemblyDefinition.ReadAssembly(arguments[0], reader))
                using (AssemblyDefinition native = AssemblyDefinition.ReadAssembly(Path.Combine(arguments[1], "assembly_valheim.dll"), reader))
                {
                    if (actorOnly) Pickable(authority, game, native);
                    else
                    {
                        Metadata(module, native);
                        Targets(module, native, authority, game);
                        Lifecycle(module,native);
                        MovementCadence(module,native);
                        RegionBounds(native);
                        SummonedRules(native, module);
                        Ownership(authority, game);
                        ComposedOwnership(authority, game);
                    }
                }
            }
            if (actorOnly)
            {
                authority.GetType("TooManyLags.PlayerSimulation.NativeContracts", true).GetMethod("Validate", Static).Invoke(null, null);
                Console.WriteLine("PASS: Actual player-simulation inventory and sector contracts in modern interface-compatible metadata host.");
                Console.WriteLine("PASS: " + assertions + " actual native Pickable rewrite assertions in modern interface-compatible metadata host. No Unity harvest executed.");
                return;
            }
            Fgn(authority, fgn, game);
            GridChoiceClamp();
            authority.GetType("TooManyLags.RequestRouter", true).GetMethod("BindKnowledge", Static).Invoke(null, null);
            Check(true, "Native private sent-owner-revision binding");
            Console.WriteLine("PASS: " + assertions + " native release assertions; " + targets + " exact Harmony targets; actual ownership IL plus FGN " + arguments[4] + ". Pickable rewrite uses separate modern host. No Unity gameplay, client callback, packet transport or live Harmony installation executed.");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Metadata(AssemblyDefinition authority, AssemblyDefinition native)
    {
        TypeDefinition plugin = authority.MainModule.GetType("TooManyLags.Plugin");
        Check(plugin != null, "Plugin missing");
        MethodDefinition awake=null;foreach(MethodDefinition method in plugin.Methods)if(method.Name=="Awake")awake=method;
        Check(awake!=null&&awake.HasBody,"Actual plugin config setup missing");
        foreach(string key in new[]{"ServerOwnedStaticBuildings","ServerOwnedInteractivesByDefault","ServerOwnedRefillStations"})
        {
            int bound=0;
            for(int i=0;i+1<awake.Body.Instructions.Count;i++)
                if(Object.Equals(awake.Body.Instructions[i].Operand,key))
                {bound++;Check(awake.Body.Instructions[i+1].OpCode==Mono.Cecil.Cil.OpCodes.Ldc_I4_0,"Actual config default must be false: "+key);}
            Check(bound==1,"Actual config entry missing or duplicated: "+key);
        }
        int keepDefault=0;
        for(int i=0;i+1<awake.Body.Instructions.Count;i++)
            if(Object.Equals(awake.Body.Instructions[i].Operand,"KeepOwnershipOnCellExit"))
            {keepDefault++;Check(awake.Body.Instructions[i+1].OpCode==Mono.Cecil.Cil.OpCodes.Ldc_I4_1,"Exit retention must default to true");}
        Check(keepDefault==1,"Exit-retention config missing or duplicated");
        int movementInterval=0,oldInterval=0;
        foreach(var instruction in awake.Body.Instructions)
        {
            if(Object.Equals(instruction.Operand,"MovementRotationCheckIntervalSeconds"))movementInterval++;
            if(Object.Equals(instruction.Operand,"OwnershipCheckIntervalSeconds"))oldInterval++;
        }
        Check(movementInterval==1&&oldInterval==0,"Actual config must bind renamed movement/rotation interval only");
        TypeDefinition interval=authority.MainModule.GetType("TooManyLags.IntervalConfig");
        Check(interval!=null,"Movement interval constants missing");
        foreach(MethodDefinition method in interval.Methods)Check(method.Name!="LegacyDefault","Obsolete config migration retained");
        foreach(string section in new[]{"01 - General","02 - Static Buildings","03 - Default Ownership and Overrides","04 - Grid Settings","05 - Directional Grid Mode","06 - Full Grid Mode","10 - Object Categories"})
        {
            bool configured=false;foreach(var instruction in awake.Body.Instructions)if(Object.Equals(instruction.Operand,section))configured=true;
            Check(configured,"Generated config section missing: "+section);
        }
        foreach(var instruction in awake.Body.Instructions)
            Check(!Object.Equals(instruction.Operand,"General")&&!Object.Equals(instruction.Operand,"ObjectTypes")&&!Object.Equals(instruction.Operand,"DirectionalFieldTimers"),"Obsolete config binding retained");
        foreach(string key in new[]{"DirectionalGridMode","InnerTransitionDelaySeconds","ForwardTransitionDelaySeconds","OuterTransitionDelaySeconds"})
        {
            int bound=0;
            for(int i=0;i+1<awake.Body.Instructions.Count;i++)if(Object.Equals(awake.Body.Instructions[i].Operand,key))
            {
                bound++;var value=awake.Body.Instructions[i+1];
                Check(key=="DirectionalGridMode"?value.OpCode==Mono.Cecil.Cil.OpCodes.Ldc_I4_1:
                    value.OpCode==Mono.Cecil.Cil.OpCodes.Ldc_R4&&Convert.ToSingle(value.Operand)==(key=="InnerTransitionDelaySeconds"?0f:key=="ForwardTransitionDelaySeconds"?.2f:3f),"Directional default changed: "+key);
            }
            Check(bound==1,"Directional config missing or duplicated: "+key);
        }
        foreach(string key in new[]{"GridCellSizeMeters","GridCoveragePattern"})
        {
            int bound=0;
            for(int i=0;i+1<awake.Body.Instructions.Count;i++)
                if(Object.Equals(awake.Body.Instructions[i].Operand,key))
                {
                    bound++;var value=awake.Body.Instructions[i+1];
                    Check(key=="GridCellSizeMeters"?value.OpCode==Mono.Cecil.Cil.OpCodes.Ldc_I4_4:
                        value.OpCode==Mono.Cecil.Cil.OpCodes.Ldstr&&Object.Equals(value.Operand,"Rounded7x7"),"Actual grid config default changed: "+key);
                }
            Check(bound==1,"Actual grid config entry missing or duplicated: "+key);
        }
        TypeDefinition patterns=authority.MainModule.GetType("TooManyLags.GridCoveragePattern");
        Check(patterns!=null&&patterns.IsEnum,"Grid pattern enum missing");
        foreach(string name in new[]{"Square3x3","Rounded5x5","Rounded7x7"})
        {
            bool field=false,configured=false;
            foreach(FieldDefinition candidate in patterns.Fields)if(candidate.Name==name&&candidate.HasConstant)field=true;
            foreach(var instruction in awake.Body.Instructions)if(Object.Equals(instruction.Operand,name))configured=true;
            Check(field&&configured,"Pattern not exposed in actual config: "+name);
        }
        MethodDefinition tick=null;foreach(MethodDefinition method in authority.MainModule.GetType("TooManyLags.AuthorityController").Methods)if(method.Name=="Tick")tick=method;
        int yawReads=0;foreach(var instruction in tick.Body.Instructions)
        {
            MethodReference method=instruction.Operand as MethodReference;
            if(method!=null&&method.DeclaringType.FullName=="TooManyLags.CameraHeading"&&method.Name=="Read")yawReads++;
        }
        Check(yawReads==2,"Camera direction has separate server-default and contention branches; each samples a participating peer once");
        TypeDefinition sync=native.MainModule.GetType("ZSyncTransform"),zdo=native.MainModule.GetType("ZDO");
        bool writesRotation=false,readsRotation=false,serializedRotation=false;
        foreach(MethodDefinition method in sync.Methods)if(method.HasBody)foreach(var instruction in method.Body.Instructions)
        {
            MethodReference called=instruction.Operand as MethodReference;
            if(called!=null&&called.DeclaringType.FullName=="ZDO"&&called.Name=="SetRotation")writesRotation=true;
            if(called!=null&&called.DeclaringType.FullName=="ZDO"&&called.Name=="GetRotation")readsRotation=true;
        }
        foreach(MethodDefinition method in zdo.Methods)if(method.HasBody&&method.Name=="Serialize")foreach(var instruction in method.Body.Instructions)
        {
            FieldReference field=instruction.Operand as FieldReference;
            if(field!=null&&field.DeclaringType.FullName=="ZDO"&&field.Name=="m_rotation")serializedRotation=true;
        }
        Check(writesRotation&&readsRotation&&serializedRotation,"Actual native transform replication must carry object rotation");
        int dependencies = 0;
        foreach (CustomAttribute attribute in plugin.CustomAttributes)
        {
            if (attribute.AttributeType.FullName != "BepInEx.BepInDependency") continue;
            dependencies++;
            Check(attribute.ConstructorArguments.Count == 2 &&
                Convert.ToString(attribute.ConstructorArguments[0].Value) == "com.Fire.FiresGhettoNetworkMod" &&
                Convert.ToInt32(attribute.ConstructorArguments[1].Value) == 1,
                "Only unversioned hard FGN dependency permitted");
        }
        Check(dependencies == 1, "Exactly one plugin dependency required");
        foreach (AssemblyNameReference reference in authority.MainModule.AssemblyReferences)
            Check(reference.Name.IndexOf("pup", StringComparison.OrdinalIgnoreCase) < 0, "Unexpected Pup assembly dependency");
        int configRegistrations = 0, configAdminChecks = 0;
        foreach (TypeDefinition type in AllTypes(authority.MainModule.Types))
        {
            // Imported AI contracts and optional direct PUP readiness have their
            // own actual-IL audits; the original grid restrictions stay scoped.
            if(type.FullName.StartsWith("TooManyLags.FgnCombat",StringComparison.Ordinal)||
                type.FullName.StartsWith("TooManyLags.FgnTargetEvents",StringComparison.Ordinal)||
                type.FullName.StartsWith("TooManyLags.FgnAttackRetry",StringComparison.Ordinal)||
                type.FullName.StartsWith("TooManyLags.NativePup",StringComparison.Ordinal)||
                type.FullName.StartsWith("TooManyLags.CellReadiness",StringComparison.Ordinal)||
                type.FullName.StartsWith("TooManyLags.FieldReader",StringComparison.Ordinal))continue;
            foreach (MethodDefinition method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (Mono.Cecil.Cil.Instruction instruction in method.Body.Instructions)
                {
                    MethodReference called = instruction.Operand as MethodReference;
                    if (called != null)
                    {
                        Check(called.DeclaringType.FullName != "System.Version", "Runtime numeric version gate remains");
                        bool configEndpoint = type.FullName == "TooManyLags.ConfigConsole" && method.Name == "Connect";
                        bool gridEndpoint = type.FullName == "TooManyLags.GridHandoffs.Protocol" && method.Name == "Connect";
                        if (called.DeclaringType.FullName == "ZRpc" && called.Name == "Register" && gridEndpoint)
                        {
                            string endpoint = null;
                            for (int i = method.Body.Instructions.IndexOf(instruction) - 1; i >= 0; i--)
                                if (method.Body.Instructions[i].OpCode == Mono.Cecil.Cil.OpCodes.Ldstr) { endpoint = (string)method.Body.Instructions[i].Operand; break; }
                            Check(endpoint == "TooManyLags_GridHandoff_1", "Grid handoffs must register only their own identified endpoint, never native callbacks");
                        }
                        if (called.DeclaringType.FullName == "ZRpc" && called.Name == "Register" && configEndpoint)
                        {
                            string endpoint = null;
                            for (int i = method.Body.Instructions.IndexOf(instruction) - 1; i >= 0; i--)
                                if (method.Body.Instructions[i].OpCode == Mono.Cecil.Cil.OpCodes.Ldstr)
                                { endpoint = (string)method.Body.Instructions[i].Operand; break; }
                            Check(endpoint == "TooManyLags_Config_1" || endpoint == "TooManyLags_ConfigReply_1" || endpoint == "TooManyLags_CameraInterval_1" || endpoint == "TooManyLags_CameraRequest_1", "Only isolated config/camera RPC names may be registered");
                            configRegistrations++;
                        }
                        if (type.FullName.StartsWith("TooManyLags.ConfigConsole/", StringComparison.Ordinal) && called.DeclaringType.FullName == "ZNet" && called.Name == "IsAdmin")
                        {
                            bool executesAfterAuthorization = false;
                            for (int i = method.Body.Instructions.IndexOf(instruction) + 1; i < method.Body.Instructions.Count; i++)
                            {
                                MethodReference later = method.Body.Instructions[i].Operand as MethodReference;
                                if (later != null && later.DeclaringType.FullName == "TooManyLags.Plugin" && later.Name == "ConfigCommand") executesAfterAuthorization = true;
                            }
                            Check(executesAfterAuthorization, "Remote setting execution follows native admin identity check"); configAdminChecks++;
                        }
                        Check(!((called.DeclaringType.FullName == "ZRpc" || called.DeclaringType.FullName == "ZRoutedRpc") &&
                            called.Name == "Register") || configEndpoint || gridEndpoint || (type.FullName == "TooManyLags.PlayerSimulation.Network" && method.Name == "Register"), "Authority registers/replaces a native client RPC callback");
                        Check(!(called.DeclaringType.FullName == "System.Reflection.FieldInfo" && called.Name == "SetValue") ||
                            (type.FullName == "TooManyLags.FgnSessionSettings" && (method.Name == "Apply" || method.Name == "CreatureBalancing")),
                            "Reflection writes must be confined to approved FGN startup settings; native gameplay fields remain untouched");
                    }
                    string text = instruction.Operand as string;
                    if (text != null)
                        Check((text=="BotanicPupPatch.FgnCombatOwnership"||!text.StartsWith("BotanicPupPatch", StringComparison.Ordinal)) &&
                            !text.StartsWith("PUP.", StringComparison.Ordinal), "Reflected Pup contract remains");
                }
            }
        }
        Check(configRegistrations == 4 && configAdminChecks == 1, "Four private config/camera endpoints and one remote admin check required");
        TypeDefinition net = native.MainModule.GetType("ZNet"); MethodDefinition admin = null, ready = null;
        foreach (MethodDefinition method in net.Methods) { if (method.Name == "IsAdmin") admin = method; if (method.Name == "RPC_PeerInfo") ready = method; }
        Check(admin != null && admin.IsPublic && admin.Parameters.Count == 1 && admin.Parameters[0].ParameterType.FullName == "System.String" && admin.ReturnType.FullName == "System.Boolean", "Native public admin-list wrapper changed");
        bool nativeList = false;
        foreach (var instruction in admin.Body.Instructions) { MethodReference called = instruction.Operand as MethodReference; if (called != null && called.Name == "ListContainsId") nativeList = true; }
        Check(nativeList, "Config authorization uses vanilla admin list");
        Check(ready != null && ready.Parameters.Count == 2 && ready.Parameters[0].Name == "rpc" && ready.Parameters[0].ParameterType.FullName == "ZRpc" && ready.Parameters[1].ParameterType.FullName == "ZPackage", "Native peer-ready callback changed");
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> source)
    {
        foreach (TypeDefinition type in source)
        { yield return type; foreach (TypeDefinition nested in AllTypes(type.NestedTypes)) yield return nested; }
    }

    private static void SummonedRules(AssemblyDefinition native, AssemblyDefinition authority)
    {
        TypeDefinition tameable=native.MainModule.GetType("Tameable");
        Check(tameable!=null,"Native Tameable contract missing");
        string[] fields={"m_unsummonDistance","m_unsummonOnOwnerLogoutSeconds"};
        string[] methods={"UpdateSummon","UpdateSavedFollowTarget"};
        for(int i=0;i<fields.Length;i++)
        {
            FieldDefinition field=null;MethodDefinition method=null;
            foreach(var candidate in tameable.Fields)if(candidate.Name==fields[i])field=candidate;
            foreach(var candidate in tameable.Methods)if(candidate.Name==methods[i])method=candidate;
            Check(field!=null&&field.IsPublic&&field.FieldType.FullName=="System.Single","Native summon marker changed: "+fields[i]);
            bool reads=false,unsummons=false;
            if(method!=null&&method.HasBody)foreach(var instruction in method.Body.Instructions)
            {
                FieldReference read=instruction.Operand as FieldReference;MethodReference call=instruction.Operand as MethodReference;
                reads|=read!=null&&read.FullName==field.FullName&&instruction.OpCode==Mono.Cecil.Cil.OpCodes.Ldfld;
                unsummons|=call!=null&&call.DeclaringType.FullName=="Tameable"&&call.Name=="UnSummon";
            }
            Check(reads&&unsummons,"Summon marker no longer drives native unsummoning: "+fields[i]);
        }
        MethodDefinition filter=null;
        foreach(var method in authority.MainModule.GetType("TooManyLags.ObjectCatalog").Methods)if(method.Name=="IsSummoned")filter=method;
        Check(filter!=null&&filter.HasBody,"Summon filter missing");
        foreach(var instruction in filter.Body.Instructions)
        {
            MethodReference call=instruction.Operand as MethodReference;
            Check(call==null||(call.DeclaringType.FullName=="UnityEngine.Object"&&call.Name=="op_Implicit"),"Summon filter adds component/physics/network calls");
            Check(instruction.OpCode!=Mono.Cecil.Cil.OpCodes.Stfld&&instruction.OpCode!=Mono.Cecil.Cil.OpCodes.Stsfld,"Summon filter must not write native state");
        }
        TypeDefinition character=native.MainModule.GetType("Character");MethodDefinition getFaction=null;
        foreach(var method in character.Methods)if(method.Name=="GetFaction")getFaction=method;
        Check(getFaction!=null&&getFaction.IsPublic&&getFaction.ReturnType.FullName=="Character/Faction","Native faction getter contract changed");
        bool readsFaction=false;
        foreach(var instruction in getFaction.Body.Instructions)
        {
            FieldReference field=instruction.Operand as FieldReference;
            readsFaction|=field!=null&&field.Name=="m_faction"&&instruction.OpCode==Mono.Cecil.Cil.OpCodes.Ldfld;
            Check(!(instruction.Operand is MethodReference),"Native faction getter became an indirect classification");
        }
        Check(readsFaction,"Native faction getter no longer reads raw faction");
        foreach(var method in character.Methods)if(method.Name=="SetTamed"||method.Name=="RPC_SetTamed")foreach(var instruction in method.Body.Instructions)
        {
            FieldReference field=instruction.Operand as FieldReference;
            Check(!(field!=null&&field.Name=="m_faction"&&instruction.OpCode==Mono.Cecil.Cil.OpCodes.Stfld),"Taming changes raw faction and would affect companion exclusion");
        }
        TypeDefinition factionType=native.MainModule.GetType("Character/Faction");bool players=false,hostileSummon=false;
        foreach(var field in factionType.Fields){if(field.Name=="Players")players=Convert.ToInt32(field.Constant)==0;if(field.Name=="PlayerSpawned")hostileSummon=Convert.ToInt32(field.Constant)==11;}
        Check(players&&hostileSummon,"Friendly/hostile native summon faction values changed");
        MethodDefinition friendly=null;foreach(var method in authority.MainModule.GetType("TooManyLags.FriendlyCreatures").Methods)if(method.Name=="IsFriendly")friendly=method;
        Check(friendly!=null&&friendly.HasBody,"Friendly companion predicate missing");
        foreach(var instruction in friendly.Body.Instructions)
        {
            MethodReference call=instruction.Operand as MethodReference;
            Check(call==null||(call.DeclaringType.FullName=="UnityEngine.Object"&&call.Name=="op_Implicit")||(call.DeclaringType.FullName=="Character"&&(call.Name=="IsPlayer"||call.Name=="GetFaction")),"Friendly filter adds component/network/physics work");
            Check(instruction.OpCode!=Mono.Cecil.Cil.OpCodes.Stfld&&instruction.OpCode!=Mono.Cecil.Cil.OpCodes.Stsfld,"Friendly filter writes native state");
        }
        Console.WriteLine("PASS: native faction predicate excludes friendly magic companions including roots; normal taming retains raw faction; legacy despawn markers preserved.");
    }
    private static void RegionBounds(AssemblyDefinition native)
    {
        TypeDefinition scene=native.MainModule.GetType("ZNetScene"),zones=native.MainModule.GetType("ZoneSystem");
        MethodDefinition area=null,getZone=null;bool zoneSize=false,initialSize=false;
        foreach(FieldDefinition field in zones.Fields)if(field.Name=="m_zoneSize"&&field.FieldType.FullName=="System.Single")zoneSize=true;
        foreach(MethodDefinition method in scene.Methods)if(method.Name=="PointInsideActiveArea")area=method;
        foreach(MethodDefinition method in zones.Methods)
        {
            if(method.Name=="GetZone")getZone=method;
            if(method.Name==".ctor"&&method.HasBody)for(int i=1;i<method.Body.Instructions.Count;i++)
            {
                FieldReference field=method.Body.Instructions[i].Operand as FieldReference;
                if(field!=null&&field.Name=="m_zoneSize"&&method.Body.Instructions[i].OpCode==Mono.Cecil.Cil.OpCodes.Stfld&&
                    method.Body.Instructions[i-1].OpCode==Mono.Cecil.Cil.OpCodes.Ldc_R4&&Object.Equals(method.Body.Instructions[i-1].Operand,64f))initialSize=true;
            }
        }
        Check(zoneSize&&initialSize&&area!=null&&getZone!=null,"Actual native zone geometry contract missing");
        bool outer=false,small=false,circle=false,chebyshev=false,sizeRead=false,nearRead=false;
        foreach(var instruction in area.Body.Instructions)
        {
            if(instruction.OpCode==Mono.Cecil.Cil.OpCodes.Ldc_R4)
            {outer|=Object.Equals(instruction.Operand,1.5f);small|=Object.Equals(instruction.Operand,1f);circle|=Object.Equals(instruction.Operand,1.75f);}
            FieldReference field=instruction.Operand as FieldReference;if(field!=null&&field.Name=="m_zoneSize")sizeRead=true;
            MethodReference method=instruction.Operand as MethodReference;
            if(method!=null){chebyshev|=method.Name=="ChebyshevDistance";nearRead|=method.Name=="get_NearSimulationDistance";}
        }
        Check(outer&&small&&circle&&chebyshev&&sizeRead&&nearRead,"Actual native active-area bounds differ from audited +/-96m square / smaller circle");
        int origin=0,divisor=0;foreach(var instruction in getZone.Body.Instructions)if(instruction.OpCode==Mono.Cecil.Cil.OpCodes.Ldc_R8)
        {if(Object.Equals(instruction.Operand,32d))origin++;if(Object.Equals(instruction.Operand,64d))divisor++;}
        Check(origin==2&&divisor==2,"Actual native zones no longer align with fine-cell power-of-two subdivision");
        Console.WriteLine("PASS: actual native 64m/+32 origin and active-area constants inspected; +/-2-zone broad boxes cover the audited native bounds.");
    }
    private static void Targets(AssemblyDefinition authority, AssemblyDefinition native, Assembly runtime, Assembly game)
    {
        int configTargets = 0;
        foreach (TypeDefinition type in HandoffTargets(authority))
        {
            CustomAttribute patch = null;
            foreach (CustomAttribute attribute in type.CustomAttributes)
                if (attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch") patch = attribute;
            if (patch == null) continue;
            if (patch.ConstructorArguments.Count == 0)
            {
                MethodInfo enumerate = runtime.GetType(type.FullName.Replace('/', '+'), true).GetMethod("TargetMethods", Static);
                Check(enumerate != null, "Dynamic Harmony target enumerator missing: " + type.FullName);
                foreach (MethodBase target in (IEnumerable<MethodBase>)enumerate.Invoke(null, null))
                {
                    Check(target != null && target.Module.Assembly == game, "Dynamic Harmony native target missing");
                    TypeDefinition nativeType = native.MainModule.GetType(target.DeclaringType.FullName);
                    MethodDefinition definition = nativeType == null ? null : ByToken(nativeType, target.MetadataToken);
                    Check(definition != null, "Dynamic native method body missing");
                    PatchParameters(type, definition); targets++;
                }
            }
            else
            {
                string owner = null, name = null; string[] parameters = null;
                foreach (CustomAttributeArgument argument in patch.ConstructorArguments)
                {
                    TypeReference reference = argument.Value as TypeReference;
                    if (reference != null) owner = reference.FullName;
                    string member = argument.Value as string;
                    if (member != null) name = member;
                    CustomAttributeArgument[] values = argument.Value as CustomAttributeArgument[];
                    if (values != null)
                    {
                        parameters = new string[values.Length];
                        for (int i = 0; i < values.Length; i++) parameters[i] = ((TypeReference)values[i].Value).FullName;
                    }
                }
                Check(owner != null && name != null, "Unrecognized declarative Harmony target");
                if (type.FullName == "TooManyLags.ConfigConnectionPatch" || type.FullName == "TooManyLags.ConfigShutdownPatch" || type.FullName == "TooManyLags.ConfigReadyPatch")
                {
                    string expected = type.Name == "ConfigConnectionPatch" ? "OnNewConnection" : type.Name == "ConfigReadyPatch" ? "RPC_PeerInfo" : "Shutdown";
                    Check(owner == "ZNet" && name == expected, "Config hooks must remain connection/ready/shutdown events only"); configTargets++;
                }
                TypeDefinition nativeType = native.MainModule.GetType(owner);
                MethodDefinition target = null; int matches = 0;
                if (nativeType != null)
                    foreach (MethodDefinition candidate in nativeType.Methods)
                    {
                        if (candidate.Name != name || (parameters != null && !Parameters(candidate, parameters))) continue;
                        matches++; target = candidate;
                    }
                Check(matches == 1, "Missing/ambiguous actual Harmony target " + owner + "." + name);
                PatchParameters(type, target); targets++;
            }
        }
        Check(configTargets == 3 && targets == 50, "Unexpected Harmony target count: " + targets);
    }
    private static IEnumerable<TypeDefinition> HandoffTargets(AssemblyDefinition module)
    {
        foreach (TypeDefinition type in module.MainModule.Types)
        {
            yield return type;
            if (type.FullName == "TooManyLags.GridHandoffs.Protocol")
                foreach (TypeDefinition nested in type.NestedTypes) yield return nested;
        }
    }
    private static MethodDefinition ByToken(TypeDefinition type, int token)
    { foreach (MethodDefinition method in type.Methods) if (method.MetadataToken.ToInt32() == token) return method; return null; }
    private static void MovementCadence(AssemblyDefinition authority,AssemblyDefinition native)
    {
        var config=authority.MainModule.GetType("TooManyLags.IntervalConfig");FieldDefinition fallback=null;
        foreach(var field in config.Fields)if(field.Name=="Default")fallback=field;
        Check(fallback!=null&&fallback.HasConstant&&Convert.ToSingle(fallback.Constant)==.05f,"Actual movement cadence must default to 0.05 seconds");
        var manager=native.MainModule.GetType("ZDOMan");MethodDefinition update=null,release=null;
        foreach(var method in manager.Methods){if(method.Name=="Update")update=method;if(method.Name=="ReleaseZDOS")release=method;}
        Check(update!=null&&update.HasBody&&release!=null&&release.HasBody,"Native cadence dispatch bodies missing");
        bool eachUpdate=false,twoSecondNativeTimer=false;
        foreach(var instruction in update.Body.Instructions){var target=instruction.Operand as MethodReference;if(target!=null&&target.DeclaringType.FullName=="ZDOMan"&&target.Name=="ReleaseZDOS")eachUpdate=true;}
        foreach(var instruction in release.Body.Instructions)if(instruction.OpCode==Mono.Cecil.Cil.OpCodes.Ldc_R4&&Convert.ToSingle(instruction.Operand)==2f)twoSecondNativeTimer=true;
        Check(eachUpdate,"Native Update must invoke ReleaseZDOS independently of its internal timer");
        Check(twoSecondNativeTimer,"Native release timer must remain unchanged at two seconds");
        var zdo=native.MainModule.GetType("ZDO");MethodDefinition deserialize=null;
        foreach(var method in zdo.Methods)if(method.Name=="Deserialize")deserialize=method;
        Check(deserialize!=null&&deserialize.HasBody,"Native ZDO deserialize body missing");
        bool directRotation=false,callsSetRotation=false;
        foreach(var instruction in deserialize.Body.Instructions)
        {
            var field=instruction.Operand as FieldReference;
            if(field!=null&&field.Name=="m_rotation"&&instruction.OpCode==Mono.Cecil.Cil.OpCodes.Stfld)directRotation=true;
            var target=instruction.Operand as MethodReference;if(target!=null&&target.Name=="SetRotation")callsSetRotation=true;
        }
        Check(directRotation&&!callsSetRotation,"Incoming rotation is assigned directly; SetRotation alone is not a complete remote movement hook");
        Console.WriteLine("PASS: actual default 0.05; native per-update cadence hook keeps two-second vanilla release timer; received rotations bypass SetRotation.");
    }
    private static void GridChoiceClamp()
    {
        Type choices=Assembly.Load("BepInEx").GetType("BepInEx.Configuration.AcceptableValueList`1",true).MakeGenericType(typeof(string));
        object allowed=Activator.CreateInstance(choices,new object[]{new[]{"Rounded5x5","Rounded7x7"}});
        MethodInfo clamp=choices.GetMethod("Clamp",Instance);
        Check(clamp!=null,"Actual BepInEx acceptable-value clamp missing");
        Check(Object.Equals(clamp.Invoke(allowed,new object[]{"Square3x3"}),"Rounded5x5"),"Unsupported 4m pattern must clamp to first allowed pattern");
        Check(Object.Equals(clamp.Invoke(allowed,new object[]{"Rounded7x7"}),"Rounded7x7"),"Supported 4m pattern must remain unchanged");
        Console.WriteLine("PASS: actual BepInEx restricted pattern list clamps Square3x3 to Rounded5x5 and preserves Rounded7x7.");
    }
    private static void Lifecycle(AssemblyDefinition authority,AssemblyDefinition native)
    {
        TypeDefinition scene=native.MainModule.GetType("ZNetScene");
        foreach(string name in new[]{"RemoveObjects","Destroy","OnZDODestroyed","Shutdown"})
        {
            MethodDefinition method=null;foreach(var candidate in scene.Methods)if(candidate.Name==name)method=candidate;
            Check(method!=null&&method.HasBody,"Native unload/destroy body missing: "+name);
            bool reset=false;foreach(var instruction in method.Body.Instructions)
            {
                var called=instruction.Operand as MethodReference;
                if(called!=null&&called.DeclaringType.FullName=="ZNetView"&&called.Name=="ResetZDO")reset=true;
            }
            Check(reset,"Native unload/destroy must call hooked ResetZDO: "+name);
        }
        var controller=authority.MainModule.GetType("TooManyLags.AuthorityController");
        MethodDefinition unregister=null;foreach(var candidate in controller.Methods)if(candidate.Name=="Unregister")unregister=candidate;
        bool managedCheck=false;
        foreach(var instruction in unregister.Body.Instructions)
        {
            var called=instruction.Operand as MethodReference;if(called==null)continue;
            Check(called.DeclaringType.FullName!="UnityEngine.Object"||called.Name!="op_Equality","Unregister must not reject destroyed Unity wrappers by fake-null equality");
            if(called.DeclaringType.FullName=="System.Object"&&called.Name=="ReferenceEquals")managedCheck=true;
        }
        Check(managedCheck,"Unregister must check CLR identity for null");
        Console.WriteLine("PASS: actual native RemoveObjects/Destroy/OnZDODestroyed/Shutdown call ResetZDO; destroyed Unity wrappers use CLR null check; scene destruction has cleanup target.");
    }
    private static bool Parameters(MethodDefinition method, string[] arguments)
    {
        if (method.Parameters.Count != arguments.Length) return false;
        for (int i = 0; i < arguments.Length; i++) if (method.Parameters[i].ParameterType.FullName != arguments[i]) return false;
        return true;
    }
    private static void PatchParameters(TypeDefinition patch, MethodDefinition native)
    {
        foreach (MethodDefinition method in patch.Methods)
        {
            bool handler = false;
            foreach (CustomAttribute attribute in method.CustomAttributes)
                if (attribute.AttributeType.FullName == "HarmonyLib.HarmonyPrefix" || attribute.AttributeType.FullName == "HarmonyLib.HarmonyPostfix") handler = true;
            if (!handler) continue;
            foreach (ParameterDefinition parameter in method.Parameters)
            {
                string name = parameter.Name;
                if (name == "__state")
                {
                    string state = parameter.ParameterType.FullName.TrimEnd('&'); bool paired = false;
                    foreach (MethodDefinition other in patch.Methods) foreach (ParameterDefinition arg in other.Parameters)
                        if (!ReferenceEquals(other, method) && arg.Name == "__state" && arg.ParameterType.FullName.TrimEnd('&') == state) paired = true;
                    Check(paired, "Harmony state must have matching prefix/postfix types"); continue;
                }
                if (name == "__runOriginal") { Check(parameter.ParameterType.FullName == "System.Boolean", "runOriginal type"); continue; }
                if (name == "__originalMethod") { Check(parameter.ParameterType.FullName == "System.Reflection.MethodBase", "originalMethod type"); continue; }
                if (name == "__instance") { Check(parameter.ParameterType.FullName == native.DeclaringType.FullName, "instance type mismatch"); continue; }
                if (name == "__result") { Check(parameter.ParameterType.FullName == native.ReturnType.FullName + "&", "result type mismatch"); continue; }
                int index;
                if (name.StartsWith("__", StringComparison.Ordinal) && Int32.TryParse(name.Substring(2), out index))
                {
                    Check(index >= 0 && index < native.Parameters.Count &&
                        parameter.ParameterType.FullName == native.Parameters[index].ParameterType.FullName, "Positional Harmony parameter mismatch");
                    continue;
                }
                bool found = false;
                foreach (ParameterDefinition original in native.Parameters)
                    if (original.Name == name && original.ParameterType.FullName == parameter.ParameterType.FullName) found = true;
                Check(found, "Named Harmony parameter mismatch: " + patch.FullName + "." + name);
            }
        }
    }

    private static void Flow(List<CodeInstruction> before, List<CodeInstruction> after)
    {
        Check(before.Count == after.Count, "Native instruction count changed");
        for (int i = 0; i < before.Count; i++)
        {
            Check(before[i].labels.Count == after[i].labels.Count && before[i].blocks.Count == after[i].blocks.Count, "Control-flow metadata count changed");
            for (int j = 0; j < before[i].labels.Count; j++) Check(before[i].labels[j].Equals(after[i].labels[j]), "Branch label changed");
            for (int j = 0; j < before[i].blocks.Count; j++) Check(before[i].blocks[j].Equals(after[i].blocks[j]), "Exception block changed");
        }
    }
    private static void Rejected(MethodInfo rewrite, object[] arguments, string name)
    {
        try { rewrite.Invoke(null, arguments); }
        catch (TargetInvocationException error)
        { Check(error.InnerException is NotSupportedException, "Wrong preflight failure for " + name); return; }
        throw new Exception("Missing incompatible-IL rejection: " + name);
    }
    private static void Ownership(Assembly authority, Assembly game)
    {
        MethodInfo original = game.GetType("ZDOMan", true).GetMethod("ReleaseNearbyZDOS", Instance);
        MethodInfo replacement = authority.GetType("TooManyLags.OwnershipPatch", true).GetMethod("Persistent", Static);
        MethodInfo rewrite = authority.GetType("TooManyLags.OwnershipTranspiler", true).GetMethod("Rewrite", Static);
        List<CodeInstruction> before = PatchProcessor.GetOriginalInstructions(original);
        List<CodeInstruction> after = (List<CodeInstruction>)rewrite.Invoke(null, new object[] { before, replacement });
        Flow(before, after); int changes = 0;
        for (int i = 0; i < before.Count; i++)
            if (before[i].opcode != after[i].opcode || !Object.Equals(before[i].operand, after[i].operand))
            { changes++; Check(after[i].opcode == OpCodes.Call && replacement.Equals(after[i].operand), "Unexpected native ownership mutation"); }
        Check(changes == 1, "Exactly one persistent getter substitution required");
        Rejected(rewrite, new object[] { after, replacement }, "already rewritten ownership loop");
        Console.WriteLine("PASS: Actual ReleaseNearbyZDOS: exactly one getter substitution; native SetOwner/active-area branches untouched; changed preflight rejected.");
    }

    private static void ComposedOwnership(Assembly authority, Assembly game)
    {
        MethodInfo original=game.GetType("ZDOMan",true).GetMethod("ReleaseNearbyZDOS",Instance);
        MethodInfo persistent=authority.GetType("TooManyLags.OwnershipPatch",true).GetMethod("Persistent",Static);
        MethodInfo grid=authority.GetType("TooManyLags.OwnershipTranspiler",true).GetMethod("Rewrite",Static);
        Type policy=authority.GetType("TooManyLags.FgnCombatOwnership",true);
        MethodInfo mob=policy.GetMethod("RewriteNativeOwner",Static),setter=policy.GetMethod("NativeSetOwner",Static);
        foreach(bool gridFirst in new[]{true,false})
        {
            List<CodeInstruction> before=PatchProcessor.GetOriginalInstructions(original),after;
            if(gridFirst)after=(List<CodeInstruction>)mob.Invoke(null,new object[]{grid.Invoke(null,new object[]{before,persistent})});
            else after=(List<CodeInstruction>)grid.Invoke(null,new object[]{mob.Invoke(null,new object[]{before}),persistent});
            Flow(before,after);int getters=0,writes=0,changes=0;
            for(int i=0;i<after.Count;i++)
            {
                MethodInfo call=after[i].operand as MethodInfo;
                if(persistent.Equals(call))getters++;
                if(setter.Equals(call))writes++;
                if(before[i].opcode!=after[i].opcode||!Object.Equals(before[i].operand,after[i].operand))
                {changes++;Check(after[i].opcode==OpCodes.Call&&(persistent.Equals(call)||setter.Equals(call)),"Unexpected composed ownership mutation");}
            }
            Check(getters==1&&writes==2&&changes==3,"Grid+AI native ownership composition failed: "+gridFirst);
        }
        Console.WriteLine("PASS: actual native grid+AI ownership transpilers compose in either order; only one persistent getter and two owner calls change.");
    }
    private static void Pickable(Assembly authority, Assembly game, AssemblyDefinition native)
    {
        MethodInfo original = game.GetType("Pickable", true).GetMethod("RPC_Pick", Instance);
        Type patch = authority.GetType("TooManyLags.PickableServerPatch", true);
        MethodInfo rewrite = patch.GetMethod("Rewrite", Static), helper = patch.GetMethod("EffectActor", Static);
        MethodDefinition body = ByToken(native.MainModule.GetType("Pickable"), original.MetadataToken);
        var opcodes = new Dictionary<short, OpCode>();
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.FieldType == typeof(OpCode)) { OpCode value = (OpCode)field.GetValue(null); opcodes[value.Value] = value; }
        var labels = new Dictionary<Mono.Cecil.Cil.Instruction, Label>();
        var generator = new DynamicMethod("AuditNativePickLabels", typeof(void), Type.EmptyTypes).GetILGenerator();
        foreach (Mono.Cecil.Cil.Instruction instruction in body.Body.Instructions)
        {
            var target = instruction.Operand as Mono.Cecil.Cil.Instruction;
            if (target != null && !labels.ContainsKey(target)) labels.Add(target, generator.DefineLabel());
            var branchTargets = instruction.Operand as Mono.Cecil.Cil.Instruction[];
            if (branchTargets != null) foreach (var branch in branchTargets) if (!labels.ContainsKey(branch)) labels.Add(branch, generator.DefineLabel());
        }
        var before = new List<CodeInstruction>(); var copied = new Dictionary<Mono.Cecil.Cil.Instruction, CodeInstruction>();
        foreach (Mono.Cecil.Cil.Instruction instruction in body.Body.Instructions)
        {
            object operand = instruction.Operand;
            FieldReference field = operand as FieldReference; MethodReference method = operand as MethodReference;
            if (field != null && field.FullName == "Player Player::m_localPlayer") operand = original.Module.ResolveField(field.MetadataToken.ToInt32());
            if (method != null && method.FullName == "ZDOID Character::GetZDOID()") operand = original.Module.ResolveMethod(method.MetadataToken.ToInt32());
            var converted = new CodeInstruction(opcodes[instruction.OpCode.Value], operand); Label label;
            if (labels.TryGetValue(instruction, out label)) converted.labels.Add(label);
            before.Add(converted); copied.Add(instruction, converted);
        }
        foreach (Mono.Cecil.Cil.ExceptionHandler handler in body.Body.ExceptionHandlers)
        {
            Check(handler.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally, "Changed Pickable exception shape");
            copied[handler.TryStart].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            copied[handler.HandlerStart].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock));
            var end = handler.HandlerEnd ?? body.Body.Instructions[body.Body.Instructions.Count - 1];
            copied[end].blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        }
        var after = (List<CodeInstruction>)rewrite.Invoke(null, new object[] { before }); Flow(before, after); int changes = 0;
        for (int i = 0; i < before.Count; i++)
            if (before[i].opcode != after[i].opcode || !Object.Equals(before[i].operand, after[i].operand))
            {
                changes++;
                Check((after[i].opcode == OpCodes.Ldarg_1 && after[i].operand == null) ||
                    (after[i].opcode == OpCodes.Call && helper.Equals(after[i].operand)), "Unexpected harvest logic mutation");
            }
        Check(changes == 2, "Exactly two effect actor substitutions required");
        Rejected(rewrite, new object[] { after }, "already rewritten harvest actor");
        Console.WriteLine("PASS: Actual Pickable.RPC_Pick: only two effect-actor instructions changed; harvest/drop/bonus/ownership behavior untouched; changed preflight rejected.");
    }

    private static void Fgn(Assembly authority, Assembly fgn, Assembly game)
    {
        Type access = fgn.GetType("FiresGhettoNetworkMod.VanillaAccess", true);
        FieldInfo field = access.GetField("HandleRoutedRpc", Static);
        Check(field != null && field.IsInitOnly, "Expected captured readonly FGN native delegate");
        MethodInfo process = fgn.GetType("FiresGhettoNetworkMod.RoutedRpcManager", true).GetMethod("ProcessRoutedRPC", Static);
        CheckMethod(process, typeof(void), "ZRoutedRpc,ZRpc,ZPackage");
        Type integration = authority.GetType("TooManyLags.FgnRpcIntegration", true);
        MethodInfo rewrite = integration.GetMethod("Rewrite", Static), replacement = integration.GetMethod("DispatchLocal", Static);
        List<CodeInstruction> before = PatchProcessor.GetOriginalInstructions(process);
        var after = (List<CodeInstruction>)rewrite.Invoke(null, new object[] { before, field, replacement });
        Flow(before, after); int loads = 0, calls = 0, changes = 0;
        for (int i = 0; i < before.Count; i++)
        {
            if (before[i].opcode == OpCodes.Ldsfld && field.Equals(before[i].operand)) loads++;
            if (after[i].opcode == OpCodes.Call && replacement.Equals(after[i].operand)) calls++;
            if (before[i].opcode != after[i].opcode || !Object.Equals(before[i].operand, after[i].operand))
            {
                changes++;
                Check((before[i].opcode == OpCodes.Ldsfld && field.Equals(before[i].operand) && after[i].opcode == OpCodes.Nop && after[i].operand == null) ||
                    (before[i].opcode == OpCodes.Callvirt && after[i].opcode == OpCodes.Call && replacement.Equals(after[i].operand)), "Unexpected FGN packet dispatcher mutation");
            }
        }
        Check(loads == 3 && calls == loads && changes == 2 * loads, "All three actual captured native dispatches required");
        Rejected(rewrite, new object[] { after, field, replacement }, "already rewritten FGN captured dispatcher");
        Type stations = fgn.GetType("FiresGhettoNetworkMod.StationRouter", true);
        MethodInfo route = stations.GetMethod("TryRoute", Static), deliver = stations.GetMethod("Deliver", Static);
        CheckMethod(route, typeof(bool), "ZRoutedRpc,ZRoutedRpc+RoutedRPCData");
        CheckMethod(deliver, typeof(bool), "ZRoutedRpc,ZDO,ZRoutedRpc+RoutedRPCData,System.Int64");
        CheckMethod(integration.GetMethod("Station", Static), typeof(bool), "ZRoutedRpc,ZRoutedRpc+RoutedRPCData,System.Boolean,System.Boolean&");
        CheckMethod(integration.GetMethod("HeldStation", Static), typeof(bool), "ZRoutedRpc,ZRoutedRpc+RoutedRPCData,System.Int64,System.Boolean,System.Boolean&");
        CheckMethod(integration.GetMethod("Result", Static), typeof(void), "ZRpc,ZDOID,System.Boolean,System.String");
        CheckMethod(fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership", true).GetMethod("OnResult", Static), typeof(void), "ZRpc,ZDOID,System.Boolean,System.String");
        Type bridge = authority.GetType("TooManyLags.FgnOwnerHandoff", true);
        ConstructorInfo constructor = bridge.GetConstructor(Instance, null, new[] { typeof(Assembly), typeof(long) }, null);
        Check(constructor.Invoke(new object[] { fgn, 99L }) != null, "FGN protocol gate and native client handoff contract did not bind");
        Type gate = fgn.GetType("FiresGhettoNetworkMod.FgnVersionGate", true);
        CheckMethod(gate.GetMethod("IsMatched", Static), typeof(bool), "ZNetPeer");
        Type plugin = fgn.GetType("FiresGhettoNetworkMod.FiresGhettoNetworkMod", true);
        // Compare native field signatures by name; no FGN plugin config/static initializer is executed.
        foreach (string name in new[] { "ConfigEnableServerAuthority", "ConfigEnableServerOwnership", "ConfigEnableServerOwnershipSelective" })
            CheckConfig(plugin.GetField(name, Static), name);
        CheckConfig(stations.GetField("ConfigEnabled", Static), "Fix Lost Station Inserts");
        Check(fgn.GetType("FiresGhettoNetworkMod.ServerAuthorityPatches", true).GetField("SimulationActive", Static).FieldType == typeof(bool), "FGN runtime SSS state contract changed");
        SessionSettings(authority, fgn, plugin);
        // Keep compiler honest about the supplied actual game identity.
        Check(game.GetType("ZNetPeer", true) == gate.GetMethod("IsMatched", Static).GetParameters()[0].ParameterType, "FGN resolves a different native peer assembly");
        Console.WriteLine("PASS: Actual FGN ProcessRoutedRPC: three readonly captured delegates replaced without changing branches; Station/TryRoute/Deliver and handoff/protocol/SSS fields bind; incompatible dispatcher preflight rejected.");
    }
    private static void SessionSettings(Assembly authority, Assembly fgn, Type plugin)
    {
        MethodInfo initialize = plugin.GetMethod("ApplyServerSideSimulationPatches", Static);
        CheckMethod(initialize, typeof(void), "");
        Check(initialize.IsStatic, "Native simulation initializer must be static");
        Type compatibility = fgn.GetType("FiresGhettoNetworkMod.ValheimCommunityPatchCompat", true);
        CheckMethod(compatibility.GetMethod("Detect", Static), typeof(void), "");
        foreach (Type type in new[] { compatibility, fgn.GetType("FiresGhettoNetworkMod.ServerClientUtils", true) })
        {
            string propertyName = type == compatibility ? "SchedulesSceneObjects" : "IsDedicatedServerDetected";
            PropertyInfo property = type.GetProperty(propertyName, Static);
            Check(property != null && property.PropertyType == typeof(bool) && property.GetGetMethod(true).IsStatic, "Native startup safety property: " + propertyName);
        }
        Type creatures = fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership", true);
        CheckConfig(creatures.GetField("ConfigEnabled", Static), "Balance Creature Ownership");
        foreach (string typeName in new[] { "CreatureOwnership", "HelmOwnership" })
        {
            string fieldName = typeName == "CreatureOwnership" ? "ServerOwnsCreatures" : "ServerOwnsShips";
            FieldInfo mode = fgn.GetType("FiresGhettoNetworkMod." + typeName, true).GetField(fieldName, Static);
            Check(mode != null && mode.FieldType == typeof(bool) && !mode.IsInitOnly, "Native startup-only ownership mode: " + fieldName);
        }
        List<CodeInstruction> awake = PatchProcessor.GetOriginalInstructions(plugin.GetMethod("Awake", Instance));
        int nativeStart = -1, nativeReady = -1;
        for (int i = 0; i < awake.Count; i++)
        {
            if (initialize.Equals(awake[i].operand)) nativeStart = i;
            FieldInfo written = awake[i].operand as FieldInfo;
            if (awake[i].opcode == OpCodes.Stsfld && written != null && written.Name == "SimulationActive") nativeReady = i;
        }
        Check(nativeStart >= 0 && nativeReady > nativeStart, "FGN uses this initializer before its completion flag");
        foreach (string typeName in new[] { "ServerOwnershipPatches", "ServerOwnershipPatchesV3" })
        {
            Type ownership = fgn.GetType("FiresGhettoNetworkMod." + typeName, true);
            string flag = typeName == "ServerOwnershipPatches" ? "ConfigEnableServerOwnership" : "ConfigEnableServerOwnershipSelective";
            FieldInfo config = plugin.GetField(flag, Static);
            bool reads = false;
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(ownership.GetMethod("ReleaseNearbyZDOS_Prefix", Static)))
                if (config.Equals(instruction.operand)) reads = true;
            Check(reads, "Installed legacy ownership prefix reads the current config reference: " + typeName);
        }
        Check(authority.GetType("TooManyLags.FgnSessionSettings", true).GetMethod("Apply", Static) != null, "Production session setup included");
        authority.GetType("TooManyLags.FgnSimulationStartup", true).GetMethod("Validate", Static).Invoke(null, new object[] { initialize });
        Type latency = authority.GetType("TooManyLags.PlayerSimulation.NativeLatency", true);
        latency.GetMethod("Bind", Static).Invoke(null, new object[] { fgn });
        Check(plugin.GetProperty("Harmony", Static) != null, "Actual FiresGhettoNetworking patch owner available for startup removal");
        Console.WriteLine("PASS: Player-simulation mode validates actual simulation groups, connection RTT delegates and native inventory/sector contracts.");
        Console.WriteLine("PASS: Actual FGN session contracts: native simulation initializer/completion order, dedicated and scene-scheduler safety guards, five config fields, stale mode flags and live legacy ownership gates.");
    }
    private static void CheckConfig(FieldInfo field, string name)
    {
        Check(field != null && field.FieldType.IsGenericType &&
            field.FieldType.GetGenericTypeDefinition().FullName == "BepInEx.Configuration.ConfigEntry`1" &&
            field.FieldType.GetGenericArguments()[0] == typeof(bool), "FGN boolean config field changed: " + name);
    }
    private static void CheckMethod(MethodInfo method, Type result, string parameters)
    {
        Check(method != null && method.ReturnType == result, "Missing/wrong actual method return type");
        var names = new List<string>(); foreach (ParameterInfo argument in method.GetParameters()) names.Add(argument.ParameterType.FullName);
        Check(String.Join(",", names.ToArray()) == parameters, "Actual method parameters changed: " + method.Name);
    }
}
