using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace TooManyLags.PupCompatibility
{
    internal static class Statics
    {
        private sealed class State { internal ZNet World; internal ZDO Data; internal long Owner; internal uint Revision; internal bool Pending; }
        internal struct Pass
        {
            internal WearNTear Wear; internal bool Checked, Ready; internal int Coverage, Zones;
            internal ZNet World; internal ZDO Data; internal long Owner; internal uint OwnerRevision;
        }
        private static ConditionalWeakTable<WearNTear, State> states = new ConditionalWeakTable<WearNTear, State>();
        private static readonly Func<WearNTear, ZNetView> view = FieldReader.Create<WearNTear, ZNetView>(AccessTools.Field(typeof(WearNTear), "m_nview"));
        private static readonly Action<WearNTear> clear = (Action<WearNTear>)Delegate.CreateDelegate(typeof(Action<WearNTear>), AccessTools.Method(typeof(WearNTear), "ClearCachedSupport"));
        private static readonly Func<WearNTear, bool> support = (Func<WearNTear, bool>)Delegate.CreateDelegate(typeof(Func<WearNTear, bool>), AccessTools.Method(typeof(WearNTear), "HaveSupport"));
        internal static SupportBounds Bounds;
        [ThreadStatic] private static Pass current;
        internal static void Reset() { current = default(Pass); states = new ConditionalWeakTable<WearNTear, State>(); }
        internal static bool Guarded(WearNTear wear)
        { ZNetView target = wear ? view(wear) : null; return Plugin.Native != null && Plugin.Native.Active && target && target.IsValid() && target.IsOwner(); }
        internal static bool Allow(WearNTear wear)
        {
            if (!Guarded(wear)) return true;
            NativePup native = Plugin.Native;
            if (native.Empty) return true;
            ZDO data = view(wear).GetZDO();
            if (SamePass(wear, data, native)) return current.Ready;
            State state = states.GetOrCreateValue(wear);
            bool changed = !ReferenceEquals(state.World, ZNet.instance) || !ReferenceEquals(state.Data, data) ||
                state.Owner != data.GetOwner() || state.Revision != data.OwnerRevision;
            bool ready = false;
            try { ready = native.SessionReady && Bounds.Ready(wear, native); }
            catch (Exception error) { Plugin.Warn("Structural readiness pending: " + error.Message); }
            if (ready && (state.Pending || changed)) clear(wear);
            state.World = ZNet.instance; state.Data = data; state.Owner = data.GetOwner(); state.Revision = data.OwnerRevision; state.Pending = !ready;
            if (current.Wear == wear)
            {
                current.Checked = true; current.Ready = ready; current.Coverage = native.Coverage; current.Zones = native.Zones;
                current.World = ZNet.instance; current.Data = data; current.Owner = data.GetOwner(); current.OwnerRevision = data.OwnerRevision;
            }
            return ready;
        }
        private static bool SamePass(WearNTear wear, ZDO data, NativePup native)
        {
            return current.Wear == wear && current.Checked && native != null &&
                current.Coverage == native.Coverage && current.Zones == native.Zones &&
                ReferenceEquals(current.World, ZNet.instance) && ReferenceEquals(current.Data, data) && data != null &&
                current.Owner == data.GetOwner() && current.OwnerRevision == data.OwnerRevision;
        }
        internal static bool Evaluate(WearNTear wear) { return wear.m_noSupportWear && Allow(wear); }
        internal static bool Supported(WearNTear wear) { return !Allow(wear) || support(wear); }

        [HarmonyPatch(typeof(WearNTear), "UpdateWear")]
        internal static class WearPatch
        {
            // A stack-local pass avoids repeating bounds/proxy work within this
            // exact native update. It is never reused on another tick or owner.
            [HarmonyPrefix] private static void Prefix(WearNTear __instance, out Pass __state)
            {
                __state = current;
                current = Guarded(__instance) && !Plugin.Native.Empty ? new Pass { Wear = __instance } : default(Pass);
            }
            [HarmonyFinalizer] private static Exception Finalizer(Pass __state, Exception __exception)
            { current = __state; return __exception; }
            [HarmonyTranspiler] private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            { return StructuralTranspiler.Rewrite(instructions, AccessTools.Field(typeof(WearNTear), "m_noSupportWear"), AccessTools.Method(typeof(Statics), "Evaluate"), AccessTools.Method(typeof(Statics), "Supported")); }
        }
        [HarmonyPatch(typeof(WearNTear), "UpdateSupport")]
        internal static class SupportPatch
        {
            [HarmonyPrefix, HarmonyPriority(Priority.Last), HarmonyAfter(Plugin.PupId)]
            private static bool Prefix(WearNTear __instance) { return Allow(__instance); }
        }
        [HarmonyPatch]
        internal static class PreservePatch
        {
            private static MethodBase TargetMethod() { return Plugin.Native.PreserveMethod; }
            [HarmonyPrefix] private static bool Prefix(WearNTear __0)
            { return !Guarded(__0); } // Loading is gated; ready unsupported pieces retain native collapse behavior.
        }
        [HarmonyPatch]
        internal static class RepairPatch
        {
            private static MethodBase TargetMethod() { return Plugin.Native.RepairMethod; }
            [HarmonyPrefix] private static bool Prefix(WearNTear __0)
            {
                ZNetView target = __0 ? view(__0) : null;
                return !current.Ready || !target || !SamePass(__0, target.GetZDO(), Plugin.Native);
            }
        }
    }
}
