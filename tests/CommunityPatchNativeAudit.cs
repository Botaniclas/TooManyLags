using System;
using System.Linq;
using Mono.Cecil;

internal static class CommunityPatchNativeAudit
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static MethodDefinition Method(TypeDefinition type, string name)
    {
        MethodDefinition result = type.Methods.SingleOrDefault(m => m.Name == name);
        Check(result != null, type.FullName + "." + name + " exists"); return result;
    }
    private static bool Calls(MethodDefinition method, string type, string name)
    {
        return method.Body.Instructions.Any(i => i.Operand is MethodReference &&
            ((MethodReference)i.Operand).DeclaringType.FullName == type && ((MethodReference)i.Operand).Name == name);
    }
    private static void Signature(MethodDefinition method, string returns, params string[] parameters)
    {
        Check(method.IsStatic && method.ReturnType.FullName == returns && method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters),
            method.FullName + " exact receiver/patch signature");
    }
    private static void Run(string[] args)
    {
        using (var resolver = new DefaultAssemblyResolver())
        {
            resolver.AddSearchDirectory(args[3]);
            var reader = new ReaderParameters { AssemblyResolver = resolver };
            using (var vcp = AssemblyDefinition.ReadAssembly(args[0], reader))
            using (var fgn = AssemblyDefinition.ReadAssembly(args[1], reader))
            using (var tml = AssemblyDefinition.ReadAssembly(args[2], reader))
            {
            const string perf = "ValheimCommunityPatch.Patches.Performance.";
            const string correctness = "ValheimCommunityPatch.Patches.Correctness.RemoveObjectsNrePatch";
            const string zdos = "System.Collections.Generic.List`1<ZDO>";
            TypeDefinition api = vcp.MainModule.GetType("ValheimCommunityPatch.APIReceiver");
            Check(api != null, "official optional API receiver available");
            Signature(Method(api, "GetApiVersion"), "System.Int32");
            Signature(Method(api, "GetFixState"), "System.Int32", "System.String");
            Signature(Method(api, "CanDisableFix"), "System.Boolean", "System.String");
            Signature(Method(api, "DisableFix"), "System.Boolean", "System.String", "System.String");
            string[] fixes = { "SceneIdleSkipPatch", "SpawnQueueCachePatch", "ZoneDiffRemovalPatch", "RemoveSweepPacingPatch" };
            string[] prefixes = { "CreateDestroyObjectsPrefix", "CreateObjectsSortedPrefix", "RemoveObjectsPrefix", "RemoveObjectsPrefix" };
            for (int i = 0; i < fixes.Length; i++)
            {
                TypeDefinition type = vcp.MainModule.GetType(perf + fixes[i]);
                Check(type != null && type.CustomAttributes.Any(a => a.AttributeType.Name == "ModDisableableAttribute"), fixes[i] + " supports session stand-down");
                MethodDefinition prefix = Method(type, prefixes[i]);
                MethodDefinition switchUser = fixes[i] == "ZoneDiffRemovalPatch" ? Method(type, "get_Engaged") : prefix;
                Check(Calls(switchUser, "ValheimCommunityPatch.FixSwitch", "get_Off"), fixes[i] + " actually honours its session switch");
                if (i == 0) Signature(prefix, "System.Boolean", "ZNetScene");
                if (i == 1) Signature(prefix, "System.Boolean", "ZNetScene", zdos, "System.Int32", "System.Int32&", "System.Boolean");
                if (i == 2) Signature(prefix, "System.Boolean", "ZNetScene", zdos, zdos, "System.Boolean");
                if (i == 3) Signature(prefix, "System.Boolean");
            }
            TypeDefinition recovery = vcp.MainModule.GetType(correctness);
            MethodDefinition recoverPrefix = Method(recovery, "RemoveObjectsPrefix");
            Signature(recoverPrefix, "System.Boolean", "ZNetScene", zdos, zdos, "System.Boolean");
            Check(Calls(recoverPrefix, perf + "ZoneDiffRemovalPatch", "get_Engaged"), "recovery stands down only while competing removal engages");
            Check(Calls(recoverPrefix, correctness, "FastPass") && Calls(recoverPrefix, correctness, "GuardedSweep"), "retained native-list removal has fast and recovery paths");
            Check(recoverPrefix.Body.ExceptionHandlers.Count > 0, "orphan recovery catch retained");
            foreach (MethodDefinition method in recovery.Methods.Where(m => m.HasBody))
            {
                Check(!method.Body.Instructions.Any(i => i.Operand is MethodReference &&
                    new[] { "ZNet", "ZoneSystem" }.Contains(((MethodReference)i.Operand).DeclaringType.FullName)),
                    "retained recovery does not rebuild a reference-position ring: " + method.Name);
            }
            foreach (string name in new[] { "FastPass", "GuardedSweep" })
                Check(Method(recovery, name).Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "m_instances"), "recovery handles actual instance table: " + name);
            TypeDefinition compat = fgn.MainModule.GetType("FiresGhettoNetworkMod.ValheimCommunityPatchCompat");
            Signature(Method(compat, "HasPrefixFrom"), "System.Boolean", "System.Reflection.MethodBase", "System.String");
            Check(Calls(Method(compat, "Detect"), compat.FullName, "HasPrefixFrom"), "native FGN safety detection uses the bound predicate");
            TypeDefinition sync = fgn.MainModule.GetType("FiresGhettoNetworkMod.ZSyncHookGate");
            Signature(Method(sync, "Refresh"), "System.Void", "System.String");
            TypeDefinition backendPlugin = fgn.MainModule.GetType("FiresGhettoNetworkMod.FiresGhettoNetworkMod");
            Check(Calls(Method(backendPlugin, "Awake"), backendPlugin.FullName, "AwakeCore"), "native Awake invokes its startup core");
            Check(Calls(Method(backendPlugin, "AwakeCore"), sync.FullName, "Refresh"), "native startup completes simulation with sync-gate refresh");
            MethodDefinition simulation = Method(fgn.MainModule.GetType("FiresGhettoNetworkMod.ServerAuthorityPatches"), "CreateDestroyObjects_Prefix");
            Check(Calls(simulation, "ZNetScene", "CreateObjects") && Calls(simulation, "ZNetScene", "RemoveObjects"), "FGN supplies native create/remove lists");
            foreach (string field in new[] { "_cdoNearFiltered", "_cdoDistantFiltered" })
                Check(simulation.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == field), "FGN retains all-player list: " + field);

            TypeDefinition bridge = tml.MainModule.GetType("TooManyLags.CommunityPatchSceneCompatibility");
            Check(bridge != null, "compiled production integration exists");
            Check(!bridge.Methods.Any(m => m.Name == "Update" || m.Name == "FixedUpdate"), "no scene/physics polling loop");
            Check(!tml.MainModule.AssemblyReferences.Any(a => a.Name == vcp.Name.Name), "no new hard VCP assembly dependency");
            TypeDefinition plugin = tml.MainModule.GetType("TooManyLags.Plugin");
            Check(plugin.CustomAttributes.Any(a => a.AttributeType.FullName == "BepInEx.BepInDependency" && a.ConstructorArguments[0].Value.Equals("MidnightsFX.ValheimCommunityPatch")), "explicit optional startup ordering");
            Check(Calls(Method(plugin, "Awake"), bridge.FullName, "Start"), "compatibility runs during startup");
            Check(!Calls(Method(plugin, "Update"), bridge.FullName, "Start"), "normal runtime never repeats setup");
            }
        }
        Console.WriteLine("PASS: " + checks + " actual CommunityPatch/FGN/production metadata and IL contracts; no Unity world.");
    }
    public static int Main(string[] args)
    { try { Run(args); return 0; } catch (Exception error) { Console.Error.WriteLine(error); return 1; } }
}
