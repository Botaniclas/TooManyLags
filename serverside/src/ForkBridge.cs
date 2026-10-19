using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    internal sealed class ForkBridge
    {
        private readonly BaseUnityPlugin plugin;
        private readonly FieldInfo installed;
        private readonly MethodInfo assignment;
        private delegate bool FireSource(int prefab, out bool fire);
        private readonly FireSource fireSource;
        internal ForkBridge(BaseUnityPlugin plugin)
        {
            this.plugin = plugin;
            Type type = plugin.GetType();
            if (type.FullName != "Valheim_Serverside.ServersidePlugin") throw new NotSupportedException("Expected MistrCech/Sarkastic.gg Dedicated Simulation fork.");
            installed = type.GetField("installed", BindingFlags.NonPublic | BindingFlags.Static);
            Type patch = type.Assembly.GetType("Valheim_Serverside.Features.Core+ZDOMan_ReleaseNearbyZDOS_Patch", true);
            assignment = AccessTools.Method(patch, "Prefix", new[] { typeof(ZDOMan), typeof(Vector3), typeof(long) });
            if (installed == null || installed.FieldType != typeof(bool) || assignment == null || assignment.ReturnType != typeof(bool)) throw new NotSupportedException("Dedicated Simulation ownership contract changed.");
            Patches patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS"));
            bool found = false;
            // The released backend installs each feature under its own Harmony
            // ID. Authenticate the exact method from the dependency assembly;
            // the Harmony owner label is not the plugin's identity.
            if (patches != null) foreach (Patch prefix in patches.Prefixes) if (prefix.PatchMethod == assignment) found = true;
            if (!found || !Active) throw new NotSupportedException("Dedicated Simulation's core ownership patch is not active.");
            Patches firePatches = Harmony.GetPatchInfo(AccessTools.Method(typeof(ZDO), "SetOwnerInternal"));
            if (firePatches != null) foreach (Patch postfix in firePatches.Postfixes)
            {
                if (postfix.PatchMethod.DeclaringType.Assembly != type.Assembly || postfix.PatchMethod.DeclaringType.FullName != "Valheim_Serverside.Features.FireControl+ZDO_SetOwnerInternal_Patch") continue;
                Type fire = type.Assembly.GetType("Valheim_Serverside.Features.FireControl", true);
                MethodInfo source = AccessTools.Method(fire, "IsSource", new[] { typeof(int), typeof(bool).MakeByRefType() });
                if (source == null || source.ReturnType != typeof(bool)) throw new NotSupportedException("Dedicated Simulation fire ownership contract changed.");
                fireSource = AccessTools.MethodDelegate<FireSource>(source); break;
            }
        }
        internal bool Active { get { return plugin && plugin.isActiveAndEnabled && (bool)installed.GetValue(null); } }
        internal bool Reserved(int prefab) { bool fire; return fireSource != null && fireSource(prefab, out fire); }
        internal void Install(Harmony harmony, bool overrideFire = false, bool ships = true)
        {
            harmony.Patch(assignment, transpiler: new HarmonyMethod(typeof(ForkAssignment), "Rewrite"));
            if (ships) ForkShipOwnership.Install(harmony, plugin.GetType().Assembly);
            if (!overrideFire || fireSource == null) return;
            MethodInfo takeover = AccessTools.Method(fireSource.Method.DeclaringType, "TakeOver", Type.EmptyTypes);
            if (takeover == null || !takeover.IsStatic || takeover.ReturnType != typeof(void)) throw new NotSupportedException("Dedicated Simulation fire takeover contract changed.");
            ForkFireOwnership.Validate(takeover, fireSource.Method);
            harmony.Patch(takeover, transpiler: new HarmonyMethod(typeof(ForkFireOwnership), "Rewrite"));
        }
    }
    internal static class ForkShipOwnership
    {
        internal static void Install(Harmony harmony, Assembly backend)
        {
            Type core = backend.GetType("Valheim_Serverside.Features.Core", true);
            MethodInfo timer = AccessTools.Method(core.GetNestedType("Ship_UpdateOwner_Patch"), "Prefix");
            MethodInfo reply = AccessTools.Method(core.GetNestedType("ZRoutedRpc_RouteRPC_Patch"), "Prefix");
            if (timer == null || timer.ReturnType != typeof(bool) || timer.GetParameters().Length != 1 ||
                timer.GetParameters()[0].ParameterType != typeof(Ship).MakeByRefType() || reply == null ||
                reply.ReturnType != typeof(void) || reply.GetParameters().Length != 1 ||
                reply.GetParameters()[0].ParameterType != typeof(ZRoutedRpc.RoutedRPCData))
                throw new NotSupportedException("Dedicated Simulation ship ownership contract changed.");
            if (!Installed(AccessTools.Method(typeof(Ship), "UpdateOwner"), timer) ||
                !Installed(AccessTools.Method(typeof(ZRoutedRpc), "RouteRPC"), reply))
                throw new NotSupportedException("Dedicated Simulation ship ownership patches are not active.");
            ShipOwnership.Validate();
            RewriteReply(PatchProcessor.GetOriginalInstructions(reply));
            harmony.Patch(timer, prefix: new HarmonyMethod(typeof(ForkShipOwnership), "Timer"));
            harmony.Patch(reply, transpiler: new HarmonyMethod(typeof(ForkShipOwnership), "RewriteReply"));
        }
        private static bool Installed(MethodInfo original, MethodInfo method)
        {
            Patches patches = Harmony.GetPatchInfo(original);
            if (patches != null) foreach (Patch prefix in patches.Prefixes) if (prefix.PatchMethod == method) return true;
            return false;
        }
        private static bool Timer(ref Ship __0, ref bool __result)
        {
            if (Plugin.Current == null || !__0) return true;
            try
            {
                ZNetView view = __0.GetComponent<ZNetView>();
                if (!view || !view.IsValid() || !Plugin.Current.ShipTimer(view.GetZDO())) return true;
                __result = false; return false;
            }
            catch (Exception error) { Plugin.Fault(error); return true; }
        }
        internal static IEnumerable<CodeInstruction> RewriteReply(IEnumerable<CodeInstruction> source)
        {
            var code = new List<CodeInstruction>(source); int count = 0;
            MethodInfo owner = AccessTools.Method(typeof(ZDO), "SetOwner", new[] { typeof(long) });
            foreach (CodeInstruction instruction in code)
            {
                if (!instruction.Calls(owner)) continue;
                instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ForkShipOwnership), "ReplyOwner"); count++;
            }
            if (count != 1) throw new NotSupportedException("Dedicated Simulation helm response assignment changed.");
            return code;
        }
        private static void ReplyOwner(ZDO data, long target)
        {
            try { if (Plugin.Current != null && Plugin.Current.ManagesShip(data)) return; }
            catch (Exception error) { Plugin.Fault(error); }
            data.SetOwner(target);
        }
    }
    internal static class ForkFireOwnership
    {
        private delegate bool SourcePredicate(int prefab, out bool fire);
        private static SourcePredicate native;
        internal static void Validate(MethodInfo takeover, MethodInfo source)
        {
            native = AccessTools.MethodDelegate<SourcePredicate>(source);
            Rewrite(PatchProcessor.GetOriginalInstructions(takeover));
        }
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source)
        {
            var code = new List<CodeInstruction>(source); int count = 0;
            if (native == null) throw new NotSupportedException("Dedicated Simulation fire predicate unavailable.");
            MethodInfo filter = AccessTools.Method(typeof(ForkFireOwnership), "Filter");
            foreach (CodeInstruction instruction in code)
            {
                if (!instruction.Calls(native.Method)) continue;
                instruction.opcode = OpCodes.Call; instruction.operand = filter; count++;
            }
            if (count != 1) throw new NotSupportedException("Dedicated Simulation fire takeover predicate changed.");
            return code;
        }
        private static bool Filter(int prefab, out bool fire)
        {
            bool source = native(prefab, out fire);
            if (!source || fire || Plugin.Current == null) return source;
            try { return !Plugin.Current.GridManagesFire(prefab); }
            catch (Exception error) { Plugin.Fault(error); return source; }
        }
    }
    internal static class ForkAssignment
    {
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source)
        {
            var code = new List<CodeInstruction>(source);
            MethodInfo native = AccessTools.Method(typeof(ZDO), "SetOwner", new[] { typeof(long) });
            MethodInfo replacement = AccessTools.Method(typeof(ForkAssignment), "Assign");
            int count = 0;
            foreach (CodeInstruction instruction in code)
            {
                if (!instruction.Calls(native)) continue;
                instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++;
            }
            if (count != 2) throw new NotSupportedException("Dedicated Simulation native assignment sites changed; no addon assignment patch installed.");
            return code;
        }
        internal static void Assign(ZDO data, long destination)
        {
            try
            {
                if (Plugin.Current == null) Plugin.EnsureWorld();
                Controller controller = Plugin.Current;
                if (controller != null) destination = controller.NativeDestination(data, destination);
            }
            catch (Exception error) { Plugin.Fault(error); }
            data.SetOwner(destination);
        }
    }
}
