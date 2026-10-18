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
    }
}
