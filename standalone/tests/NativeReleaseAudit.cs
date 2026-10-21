using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mono.Cecil;

internal static class NativeReleaseAudit
{
    private static int checks, patches;
    private static string[] directories;
    private const string Prefix = "TooManyLagsStandalone.";
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    { foreach (string folder in directories) { string file = Path.Combine(folder, new AssemblyName(args.Name).Name + ".dll"); if (File.Exists(file)) return Assembly.LoadFrom(file); } return null; }
    private static MethodDefinition Method(TypeDefinition type, string name)
    { foreach (MethodDefinition method in type.Methods) if (method.Name == name) return method; throw new Exception(type.Name + "." + name + " missing"); }
    public static void Main(string[] args)
    {
        try
        {
            directories = new[] { args[1], args[2], Path.GetDirectoryName(args[0]) };
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            using (var resolver = new DefaultAssemblyResolver())
            {
                foreach (string dir in directories) resolver.AddSearchDirectory(dir);
                var reader = new ReaderParameters { AssemblyResolver = resolver };
                using (var plugin = AssemblyDefinition.ReadAssembly(args[0], reader))
                using (var native = AssemblyDefinition.ReadAssembly(Path.Combine(args[1], "assembly_valheim.dll"), reader))
                {
                    foreach (AssemblyNameReference reference in plugin.MainModule.AssemblyReferences)
                        Check(reference.Name.IndexOf("FGN", StringComparison.OrdinalIgnoreCase) < 0 && reference.Name.IndexOf("Ghetto", StringComparison.OrdinalIgnoreCase) < 0 && reference.Name.IndexOf("Pup", StringComparison.OrdinalIgnoreCase) < 0 && reference.Name.IndexOf("Jotunn", StringComparison.OrdinalIgnoreCase) < 0, "Extra mod assembly reference: " + reference.Name);
                    TypeDefinition entry = plugin.MainModule.GetType(Prefix + "Plugin");
                    bool metadata = false;
                    foreach (CustomAttribute attr in entry.CustomAttributes)
                    {
                        Check(attr.AttributeType.Name != "BepInDependency", "Dependency attribute present");
                        if (attr.AttributeType.Name == "BepInPlugin") metadata = (string)attr.ConstructorArguments[0].Value == "botanic.toomanylags.standalone" && (string)attr.ConstructorArguments[2].Value == "1.1.3";
                    }
                    Check(metadata, "Release metadata");
                    var expectedHooks = new HashSet<string>(new[] { "GameCamera.LateUpdate","GameCamera.OnDestroy","Container.RPC_RequestOpen","Container.RPC_RequestStack","Container.RPC_RequestTakeAll","ZNet.OnNewConnection","ZNet.Shutdown","ZDO.Deserialize","ZDO.InternalSetPosition","ZDO.SetPrefab","ZDO.SetOwner","ZDO.SetOwnerInternal","ZDOMan.RPC_ZDOData","ZDOMan.HandleDestroyedZDO","ZNetScene.AddInstance","ZNetView.ResetZDO","ZNetView.OnDestroy","ZRoutedRpc.RouteRPC","ZRoutedRpc.HandleRoutedRPC","ZRoutedRpc.InvokeRoutedRPC","Container.RPC_OpenResponse","Container.RPC_StackResponse","Container.RPC_TakeAllResponse","Container.SetInUse","InventoryGui.Hide","InventoryGui.CloseContainer","ZDOMan.ReleaseZDOS","TextInput.RequestText" });
                    foreach (TypeDefinition type in plugin.MainModule.Types) foreach (CustomAttribute attr in type.CustomAttributes)
                    {
                        if (attr.AttributeType.Name != "HarmonyPatch") continue;
                        string owner = null, name = null; string[] signature = null;
                        foreach (CustomAttributeArgument argument in attr.ConstructorArguments)
                        {
                            if (argument.Value is TypeReference) owner = ((TypeReference)argument.Value).FullName;
                            if (argument.Value is string) name = (string)argument.Value;
                            var array = argument.Value as CustomAttributeArgument[];
                            if (array != null) { signature = new string[array.Length]; for (int i = 0; i < array.Length; i++) signature[i] = ((TypeReference)array[i].Value).FullName; }
                        }
                        TypeDefinition nativeType = native.MainModule.GetType(owner); MethodDefinition target = null; int matches = 0;
                        if (nativeType != null) foreach (MethodDefinition method in nativeType.Methods)
                        {
                            if (method.Name != name) continue;
                            bool same = signature == null || method.Parameters.Count == signature.Length;
                            if (same && signature != null) for (int i = 0; i < signature.Length; i++) same &= method.Parameters[i].ParameterType.FullName == signature[i];
                            if (same) { matches++; target = method; }
                        }
                        Check(matches == 1, "Missing/ambiguous native hook: " + owner + "." + name);
                        Check(expectedHooks.Remove(owner + "." + name), "Unexpected/duplicate native hook: " + owner + "." + name);
                        foreach (MethodDefinition hook in type.Methods) if (hook.Name == "Prefix" || hook.Name == "Postfix") foreach (ParameterDefinition parameter in hook.Parameters)
                        {
                            string key = parameter.Name, typeName = parameter.ParameterType.FullName;
                            if (key == "__state") continue;
                            if (key == "__runOriginal") { Check(typeName == "System.Boolean", "Harmony execution flag binding: " + type.Name); continue; }
                            if (key == "__instance") { Check(typeName == owner, "Instance binding: " + type.Name); continue; }
                            int slot;
                            if (key.StartsWith("__") && Int32.TryParse(key.Substring(2), out slot)) { Check(slot >= 0 && slot < target.Parameters.Count && typeName == target.Parameters[slot].ParameterType.FullName, "Positional hook binding: " + type.Name); continue; }
                            bool found = false; foreach (ParameterDefinition original in target.Parameters) found |= original.Name == key && original.ParameterType.FullName == typeName;
                            Check(found, "Named hook binding: " + type.Name + "." + key);
                        }
                        patches++;
                    }
                    Check(patches == 28 && expectedHooks.Count == 0, "Expected complete 1.1.3 native hook set; actual: " + patches);
                    string localType = Prefix + "LocalAgent";
                    MethodDefinition guard = Method(plugin.MainModule.GetType(localType), "Check");
                    foreach (string call in new[] { "Container::IsInUse", "InventoryGui", "InAttack", "IsStaggering", "HaveRider", "IsAttached", "IsRiding", "IsSummoned" })
                    { bool found = false; foreach (var code in guard.Body.Instructions) found |= code.Operand != null && code.Operand.ToString().Contains(call); Check(found, "Source guard missing: " + call); }
                    foreach (string call in new[] { "inventoryLoading", "IncineratorProtection::Busy", "IncineratorProtection::get_Supported", "DelayedTap", "DelayedSpawnBoss", "currentAttack", "IsDone", "IsCharging", "cartRequester", "ShipTransferGuard::Allows" })
                    { bool found = false; foreach (var code in guard.Body.Instructions) found |= code.Operand != null && code.Operand.ToString().Contains(call); Check(found, "New source inventory/burn guard missing: " + call); }
                    MethodDefinition receive = Method(plugin.MainModule.GetType(localType), "Receive"); int checkedAt = -1, ownerAt = -1;
                    bool capturesTick = false;
                    foreach (var instruction in Method(plugin.MainModule.GetType(localType), "Tick").Body.Instructions)
                    {
                        var allocation = instruction.Operand as MethodReference;
                        capturesTick |= instruction.OpCode == Mono.Cecil.Cil.OpCodes.Newobj && allocation != null && allocation.DeclaringType.Name.Contains("DisplayClass");
                    }
                    Check(!capturesTick, "Local action maintenance must not allocate a captured time value each frame");
                    for (int i = 0; i < receive.Body.Instructions.Count; i++)
                    { var call = receive.Body.Instructions[i].Operand as MethodReference; if (call == null) continue; if (call.Name == "Check") checkedAt = i; if (call.DeclaringType.FullName == "ZDO" && call.Name == "SetOwner") ownerAt = i; }
                    Check(checkedAt >= 0 && ownerAt > checkedAt, "Source-side guard must precede owner commit");
                    MethodDefinition nativeHandoff = Method(plugin.MainModule.GetType(Prefix + "Handoffs"), "Tick");
                    int nativeOwnerAt = -1;
                    foreach (string check in new[] { "MatchesSource", "NativeTransferSafe", "Idle" })
                    {
                        int guardAt = -1;
                        for (int i = 0; i < nativeHandoff.Body.Instructions.Count; i++)
                        { var call = nativeHandoff.Body.Instructions[i].Operand as MethodReference; if (call == null) continue; if (call.Name == check) guardAt = i; if (call.DeclaringType.FullName == "ZDO" && call.Name == "SetOwner") nativeOwnerAt = i; }
                        Check(guardAt >= 0 && nativeOwnerAt > guardAt, "Native fallback guard must precede owner write: " + check);
                    }
                    Check(Method(native.MainModule.GetType("ZDOMan"), "ReleaseNearbyZDOS").HasBody, "Native host reassignment contract");
                    Check(Method(native.MainModule.GetType("ISocket"), "GetConnectionQuality").Parameters.Count == 5, "Native socket telemetry contract");
                    TypeDefinition node = null;
                    foreach (TypeDefinition nested in plugin.MainModule.GetType(Prefix + "AuthorityController").NestedTypes) if (nested.Name == "Node") node = nested;
                    Check(node != null, "Production grid node exists");
                    bool pendingRetry = false; MethodDefinition apply = Method(node, "Apply");
                    for (int i = 0; i + 2 < apply.Body.Instructions.Count; i++)
                    {
                        var call = apply.Body.Instructions[i].Operand as MethodReference;
                        if (call != null && call.DeclaringType.FullName == Prefix + "Handoffs" && call.Name == "Request")
                            pendingRetry = apply.Body.Instructions[i + 1].OpCode == Mono.Cecil.Cil.OpCodes.Pop && apply.Body.Instructions[i + 2].OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_1;
                    }
                    Check(pendingRetry, "Temporary handoff saturation must retain the production node's pending retry");
                    foreach (TypeDefinition type in plugin.MainModule.Types) foreach (MethodDefinition method in type.Methods) if (method.HasBody) foreach (var instruction in method.Body.Instructions)
                    {
                        var call = instruction.Operand as MethodReference;
                        if (call != null && call.DeclaringType.FullName == "ZDO" && call.Name == "SetOwner") Check((type.FullName == localType && method.Name == "Receive") || (type.FullName == Prefix + "Handoffs" && method.Name == "Tick"), "Uncoordinated owner writer: " + type.Name);
                        if (call != null && call.Name == "RPC_RequestOpen") Check(false, "Fake inventory-open probe present");
                    }
                    Check(Method(native.MainModule.GetType("Container"), "RPC_RequestOpen").HasBody, "Native inventory protocol");
                    Check(Method(native.MainModule.GetType("ZNetScene"), "IsAreaReady").HasBody, "Native receiver readiness");
                }
            }
            using (var plugin = AssemblyDefinition.ReadAssembly(args[0]))
            using (var game = AssemblyDefinition.ReadAssembly(Path.Combine(args[1], "assembly_valheim.dll")))
            { Bindings(game); TargetWrites(plugin, game); }
            Console.WriteLine("PASS: " + checks + " native release assertions; " + patches + " exact Harmony targets; native private-field/delegate signatures and target-write IL. No Unity gameplay, delegate execution or live patch installation.");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    private static void Bindings(AssemblyDefinition native)
    {
        foreach (string contract in new[] { "BaseAI:m_nview:ZNetView", "MonsterAI:m_targetCreature:Character", "MonsterAI:m_timeSinceSensedTargetCreature:System.Single", "InventoryGui:m_currentContainer:Container", "Container:m_loading:System.Boolean", "Incinerator:isInUse:System.Boolean", "TextInput:m_queuedSign:TextReceiver", "Humanoid:m_currentAttack:Attack", "Vagon:m_useRequester:Humanoid", "Ship:m_players:System.Collections.Generic.List`1<Player>" })
        {
            string[] bits = contract.Split(':'); bool found = false;
            foreach (FieldDefinition field in native.MainModule.GetType(bits[0]).Fields) found |= field.Name == bits[1] && field.FieldType.FullName == bits[2] && !field.IsStatic;
            Check(found, "Native private-field contract: " + contract);
        }
        Check(Method(native.MainModule.GetType("BaseAI"), "SetAlerted").Parameters.Count == 1 && Method(native.MainModule.GetType("BaseAI"), "SetAlerted").Parameters[0].ParameterType.FullName == "System.Boolean", "Native alert setter");
        foreach (string call in new[] { "IsPlayerInBoat", "GetNewOwnerID", "SetOwner" })
        {
            bool found = false;
            foreach (var instruction in Method(native.MainModule.GetType("Ship"), "UpdateOwner").Body.Instructions)
            {
                var reference = instruction.Operand as MethodReference;
                found |= reference != null && reference.Name == call;
            }
            Check(found, "Native occupied-ship owner policy: " + call);
        }
        foreach (string contract in new[] { "Fermenter:RPC_Tap:DelayedTap", "OfferingBowl:SpawnBoss:DelayedSpawnBoss" })
        {
            string[] bits = contract.Split(':'); TypeDefinition owner = native.MainModule.GetType(bits[0]);
            Check(Method(owner, bits[2]).Parameters.Count == 0, "Native delayed callback contract: " + contract);
            bool schedules = false; foreach (var instruction in Method(owner, bits[1]).Body.Instructions)
                schedules |= instruction.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && Equals(instruction.Operand, bits[2]);
            Check(schedules, "Native action schedules the guarded callback: " + contract);
        }
        MethodDefinition lookup = Method(native.MainModule.GetType("ZDOMan"), "FindObjects");
        Check(lookup.Parameters.Count == 3 && lookup.Parameters[0].ParameterType.FullName == "Vector2s" && lookup.Parameters[1].ParameterType.FullName == "System.Collections.Generic.List`1<ZDO>" && lookup.Parameters[2].ParameterType.FullName == "System.Collections.Generic.HashSet`1<ZoneSystem/SectorIndex>", "Cached sector-lookup delegate contract");
    }
    private static void TargetWrites(AssemblyDefinition plugin, AssemblyDefinition game)
    {
        TypeDefinition events = plugin.MainModule.GetType(Prefix + "TargetEvents"), monster = game.MainModule.GetType("MonsterAI"); int methods = 0, writes = 0;
        foreach (MethodDefinition method in monster.Methods)
        {
            if (!method.HasBody || method.HasGenericParameters || method.IsConstructor) continue; int count = 0;
            foreach (var code in method.Body.Instructions) { var field = code.Operand as FieldReference; if (code.OpCode == Mono.Cecil.Cil.OpCodes.Stfld && field != null && field.DeclaringType.FullName == "MonsterAI" && field.Name == "m_targetCreature") count++; }
            if (count != 0) { methods++; writes += count; }
        }
        Check(methods > 0 && writes > 0, "Actual native target writes absent");
        MethodDefinition assign = Method(events, "AssignTarget"); int store = -1, notify = -1;
        for (int i = 0; i < assign.Body.Instructions.Count; i++)
        {
            var field = assign.Body.Instructions[i].Operand as FieldReference; var call = assign.Body.Instructions[i].Operand as MethodReference;
            if (field != null && field.Name == "writeTarget") store = i;
            if (call != null && call.Name == "Schedule") notify = i;
        }
        Check(store >= 0 && notify > store, "Optional callback must follow native target store");
        bool strict = false; foreach (var code in Method(events, "RewriteAssignments").Body.Instructions) if (code.Operand != null && code.Operand.ToString().Contains("NotSupportedException")) strict = true;
        Check(strict, "Unexpected target-store rewrites must fail preflight");
        Console.WriteLine("PASS: inspected " + writes + " actual MonsterAI target stores across " + methods + " native methods; production store-before-callback and strict rewrite validation.");
    }
}
