using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TooManyLags
{
    internal static class FgnSimulationStartup
    {
        private static readonly HashSet<string> groups = new HashSet<string>(StringComparer.Ordinal)
        {
            "ShipFixesGroup", "ServerShipSimulationPatches", "ServerAuthorityPatches",
            "ServerStabilityPatches", "MonsterAIPatches", "ServerOwnershipPatches", "ServerOwnershipPatchesV3"
        };
        private static readonly OpCode[] shortCodes = new OpCode[256], longCodes = new OpCode[256];
        static FgnSimulationStartup()
        {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public))
            {
                OpCode code = (OpCode)field.GetValue(null); ushort value = (ushort)code.Value;
                if (value < 256) shortCodes[value] = code; else if ((value & 0xff00) == 0xfe00) longCodes[value & 255] = code;
            }
        }
        internal static void Validate(MethodInfo initialize)
        {
            if (!ReferenceEquals(ZNet.instance, null) || !ReferenceEquals(ZNetScene.instance, null))
                throw new NotSupportedException("Server-Side Simulation can only be selected before loading a world; restart the server.");
            var found = new HashSet<string>(StringComparer.Ordinal);
            MethodInfo patchAll = initialize.DeclaringType.GetMethod("TryPatchAll", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Type) }, null);
            if (patchAll == null || patchAll.ReturnType != typeof(void)) throw new NotSupportedException("FiresGhettoNetworking's simulation installer changed.");
            byte[] il = initialize.GetMethodBody().GetILAsByteArray();
            Type pending = null;
            // Read only patch type tokens and the installer call. Resolving
            // every log/helper operand is unnecessary and can load unrelated
            // optional interfaces on older CLR audit hosts.
            for (int offset = 0; offset < il.Length;)
            {
                byte first = il[offset++]; OpCode code = first == 0xfe ? longCodes[il[offset++]] : shortCodes[first];
                int size = OperandSize(code.OperandType, il, offset);
                int token = size == 4 ? BitConverter.ToInt32(il, offset) : 0;
                offset += size;
                if (code == OpCodes.Ldtoken && (token & 0xff000000) == 0x02000000) pending = initialize.Module.ResolveType(token);
                if (code != OpCodes.Call || token != patchAll.MetadataToken) continue;
                if (pending == null || pending.Assembly != initialize.DeclaringType.Assembly || !IsSimulationGroup(pending))
                    throw new NotSupportedException("FiresGhettoNetworking's simulation patch groups changed; no hooks were removed.");
                if (groups.Contains(pending.Name)) found.Add(pending.Name);
                pending = null;
            }
            if (!found.SetEquals(groups))
                throw new NotSupportedException("FiresGhettoNetworking's simulation startup contract changed; no hooks were removed.");
        }
        private static bool IsSimulationGroup(Type type)
        {
            // FGN 1.5.65 adds optional simulation-only sector bookkeeping.
            // Older installers still require exactly the original seven groups.
            return type.Namespace == "FiresGhettoNetworkMod" &&
                (groups.Contains(type.Name) || type.Name == "SssQuietPass");
        }
        private static int OperandSize(OperandType type, byte[] il, int offset)
        {
            switch (type)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineI8: case OperandType.InlineR: return 8;
                case OperandType.InlineSwitch: return checked(4 + BitConverter.ToInt32(il, offset) * 4);
                default: return 4;
            }
        }
        internal static void Remove(Assembly assembly)
        {
            Type plugin = assembly.GetType("FiresGhettoNetworkMod.FiresGhettoNetworkMod", true);
            PropertyInfo property = plugin.GetProperty("Harmony", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Harmony owner = property == null ? null : property.GetValue(null, null) as Harmony;
            if (owner == null) throw new NotSupportedException("FiresGhettoNetworking's patch owner is unavailable.");
            var removals = new List<KeyValuePair<MethodBase, MethodInfo>>();
            foreach (MethodBase original in Harmony.GetAllPatchedMethods())
            {
                Patches patches = Harmony.GetPatchInfo(original);
                foreach (Patch patch in All(patches))
                {
                    Type root = patch.PatchMethod.DeclaringType;
                    while (root != null && root.DeclaringType != null) root = root.DeclaringType;
                    if (patch.owner == owner.Id && root != null && root.Assembly == assembly && IsSimulationGroup(root))
                        removals.Add(new KeyValuePair<MethodBase, MethodInfo>(original, patch.PatchMethod));
                }
            }
            foreach (var item in removals) owner.Unpatch(item.Key, item.Value);
        }
        private static IEnumerable<Patch> All(Patches patches)
        {
            foreach (Patch p in patches.Prefixes) yield return p;
            foreach (Patch p in patches.Postfixes) yield return p;
            foreach (Patch p in patches.Transpilers) yield return p;
            foreach (Patch p in patches.Finalizers) yield return p;
        }

        // Newer FGN snapshots installed features into shared dispatchers at the
        // end of Awake. Starting its simulation groups later does not refresh
        // those flags or attach dispatchers whose Prepare originally declined.
        // Reconcile only simulation branches, once before a world is loaded.
        internal static void SynchronizeHooks(Assembly assembly, bool enabled)
        {
            Type hooks = assembly.GetType("FiresGhettoNetworkMod.FgnHooks", false);
            if (hooks == null) return; // Older FGN installs direct hooks.
            if (!ReferenceEquals(ZNet.instance, null) || !ReferenceEquals(ZNetScene.instance, null))
                throw new NotSupportedException("FiresGhettoNetworking's simulation hooks require a server restart.");
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            MethodInfo attached = hooks.GetMethod("On", flags, null, new[] { typeof(Type) }, null);
            if (attached == null || attached.ReturnType != typeof(bool))
                throw new NotSupportedException("FiresGhettoNetworking's feature dispatcher contract changed.");
            string[] names = { "s_serverAuthority", "s_stability", "s_shipSim", "s_shipFixes", "s_ownershipV2", "s_ownershipV3" };
            string[] features = { "ServerAuthorityPatches", "ServerStabilityPatches", "ServerShipSimulationPatches", "ShipFixesGroup" };
            var fields = new FieldInfo[names.Length];
            var previous = new bool[names.Length];
            var desired = new bool[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                fields[i] = hooks.GetField(names[i], flags);
                if (fields[i] == null || fields[i].FieldType != typeof(bool) || fields[i].IsInitOnly)
                    throw new NotSupportedException("FiresGhettoNetworking's simulation dispatch field changed: " + names[i]);
                previous[i] = (bool)fields[i].GetValue(null);
                // TML disables both native broad/selective ownership modes.
                desired[i] = enabled && i < features.Length && (bool)attached.Invoke(null,
                    new object[] { assembly.GetType("FiresGhettoNetworkMod." + features[i], true) });
            }
            string[] dispatcherNames = { "ZoneSystemUpdate", "ZDOManRemovePeer", "TombStoneAwake", "ZRoutedRpcRouteRPC",
                "WearNTearUpdateSupport", "ShipCustomFixedUpdate", "ZNetSceneShutdown" };
            var dispatchers = new List<Type>();
            var methods = new HashSet<MethodInfo>();
            if (enabled)
                foreach (string name in dispatcherNames)
                {
                    Type type = hooks.GetNestedType(name, BindingFlags.NonPublic);
                    if (type == null) throw new NotSupportedException("FiresGhettoNetworking's simulation dispatcher changed: " + name);
                    MethodInfo prepare = type.GetMethod("Prepare", flags, null, Type.EmptyTypes, null);
                    if (prepare == null || prepare.ReturnType != typeof(bool))
                        throw new NotSupportedException("FiresGhettoNetworking's dispatcher preparation changed: " + name);
                    int count = 0;
                    foreach (MethodInfo method in type.GetMethods(flags))
                        if (Attribute.IsDefined(method, typeof(HarmonyPrefix)) || Attribute.IsDefined(method, typeof(HarmonyPostfix)) ||
                            Attribute.IsDefined(method, typeof(HarmonyTranspiler)) || Attribute.IsDefined(method, typeof(HarmonyFinalizer)))
                        { methods.Add(method); count++; }
                    if (count == 0) throw new NotSupportedException("FiresGhettoNetworking's dispatcher patches are absent: " + name);
                    dispatchers.Add(type);
                }
            var added = new List<KeyValuePair<MethodBase, MethodInfo>>();
            Harmony owner = null;
            try
            {
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, desired[i]);
                if (!enabled) return; // Shared networking/profiling hooks remain installed.
                Type plugin = assembly.GetType("FiresGhettoNetworkMod.FiresGhettoNetworkMod", true);
                PropertyInfo property = plugin.GetProperty("Harmony", flags);
                owner = property == null ? null : property.GetValue(null, null) as Harmony;
                if (owner == null) throw new NotSupportedException("FiresGhettoNetworking's patch owner is unavailable.");
                var installed = new HashSet<MethodInfo>();
                foreach (MethodBase original in Harmony.GetAllPatchedMethods())
                    foreach (Patch patch in All(Harmony.GetPatchInfo(original)))
                        if (patch.owner == owner.Id && methods.Contains(patch.PatchMethod)) installed.Add(patch.PatchMethod);
                var missing = new List<Type>();
                foreach (Type type in dispatchers)
                {
                    int total = 0, present = 0;
                    foreach (MethodInfo method in methods)
                        if (method.DeclaringType == type) { total++; if (installed.Contains(method)) present++; }
                    if (present == total) continue; // Never duplicate existing native wrappers.
                    if (present != 0) throw new NotSupportedException("FiresGhettoNetworking's shared dispatcher is only partly installed: " + type.Name);
                    if ((bool)type.GetMethod("Prepare", flags).Invoke(null, null)) missing.Add(type);
                }
                try
                {
                    foreach (Type type in missing) owner.CreateClassProcessor(type).Patch();
                }
                finally
                {
                    foreach (MethodBase original in Harmony.GetAllPatchedMethods())
                        foreach (Patch patch in All(Harmony.GetPatchInfo(original)))
                            if (patch.owner == owner.Id && methods.Contains(patch.PatchMethod) && !installed.Contains(patch.PatchMethod))
                                added.Add(new KeyValuePair<MethodBase, MethodInfo>(original, patch.PatchMethod));
                }
            }
            catch
            {
                if (owner != null) foreach (var patch in added) owner.Unpatch(patch.Key, patch.Value);
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, previous[i]);
                throw;
            }
        }
    }
}
