using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // Optional PUP boundary. Never releases a freeze, changes physics state or
    // substitutes a global area-loaded flag for PUP's local collision proof.
    internal static class MobCollisionReadiness
    {
        private static NativePup native;
        private static Type guardType;
        private static Func<Component, bool> held;
        private static Action<Component> prepare;
        private static Harmony hooks;
        private static bool present;
        private static int frame = -1;
        private static long spent;
#if TML_DEBUG_SYSTEM
        private static long selectedCalls, selectedBlocked;
        private static ZDO traced;
        private static long tracedOwner;
        private static uint tracedRevision;
        private static double tracedAt;
        private static bool lastAllowed;

#endif
        internal static void Start(MonoBehaviour pup, bool prioritize = true)
        {
            present = pup;
            FgnCombatOwnership.ServerCollisionReady = Ready;
            AuthorityController.MobServerReady = Ready;
#if TML_DEBUG_SYSTEM
            FgnSoloCombatTest.CollisionStatus = Describe;
#endif
            if (!present) return;
            try
            {
                native = new NativePup(pup);
                if (!prioritize) return;
                guardType = pup.GetType().GetNestedType("ZoneBodyGuard", BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo preparation = guardType == null ? null : AccessTools.Method(guardType, "PrepareDedicatedCollision");
                FieldInfo heldField = guardType == null ? null : AccessTools.Field(guardType, "held");
                if (preparation == null || preparation.ReturnType != typeof(void) || preparation.GetParameters().Length != 0 ||
                    heldField == null || heldField.FieldType != typeof(bool))
                    throw new NotSupportedException("PUP dedicated collision preparation unavailable.");
                held = FieldReader.Create<Component, bool>(heldField);
                DynamicMethod call = new DynamicMethod("TooManyLagsPrepareNativeMobCollision", typeof(void),
                    new[] { typeof(Component) }, typeof(MobCollisionReadiness).Module, true);
                ILGenerator il = call.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, guardType);
                il.Emit(OpCodes.Call, preparation); il.Emit(OpCodes.Ret);
                prepare = (Action<Component>)call.CreateDelegate(typeof(Action<Component>));
                hooks = new Harmony(Plugin.Id + ".mobcollision");
                hooks.Patch(AccessTools.Method(pup.GetType(), "PrepareMobCollision"),
                    prefix: new HarmonyMethod(typeof(MobCollisionReadiness), "BeforeMob")
#if TML_DEBUG_SYSTEM
                    , postfix: new HarmonyMethod(typeof(MobCollisionReadiness), "AfterMob")
#endif
                );
            }
            catch (Exception error)
            {
                if (hooks != null) hooks.UnpatchSelf(); hooks = null;
                Plugin.Report((native == null ? "PUP mob readiness unavailable; server handoffs wait for a supported collision contract. " :
                    "PUP priority preparation unavailable; native guards and local handoff readiness remain active. ") + error.GetBaseException().Message);
            }
        }
        internal static bool Ready(Vector3 point)
        { return !present || (native != null && native.ReadyPoint(point, 16f)); }

        private static void BeforeMob(Character __0)
        {
            // Native PUP already owns the guard and its preparation cadence.
            // Prioritize only an existing held server mob close to a player;
            // ordinary actors and clients do no queue/preparation work here.
            if (!__0 || __0 is Player || !ZNet.instance || !ZNet.instance.IsDedicated() || native == null || !native.Active || prepare == null) return;
            ZNetView view = __0.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.IsOwner()) return;
            Component guard = __0.GetComponent(guardType);
            if (!guard || !held(guard)) return;
            int current = Time.frameCount;
            if (frame != current) { frame = current; spent = 0; }
            if (spent >= Stopwatch.Frequency / 500L) return;
            Vector3 point = view.GetZDO().GetPosition(); bool near = false;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                if (peer != null && peer.IsReady() && (peer.GetRefPos() - point).sqrMagnitude <= 1600f) { near = true; break; }
            if (!near) return;
            long start = Stopwatch.GetTimestamp();
            try { prepare(guard); }
            catch (Exception error) { Plugin.Warn("Local PUP mob preparation deferred: " + error.GetBaseException().Message); }
            finally { spent += Stopwatch.GetTimestamp() - start; }
        }
#if TML_DEBUG_SYSTEM
        private static void AfterMob(Character __0, bool __result)
        {
            if (!FgnSoloCombatTest.Active) return;
            if (!__0 || !ZNet.instance || !ZNet.instance.IsDedicated()) return;
            ZNetView view = __0.GetComponent<ZNetView>(); ZDO data = view && view.IsValid() ? view.GetZDO() : null;
            if (data == null || !FgnSoloCombatTest.Selected(data)) return;
            if (!ReferenceEquals(traced, data) || tracedOwner != data.GetOwner() || tracedRevision != data.OwnerRevision)
            { traced = data; tracedOwner = data.GetOwner(); tracedRevision = data.OwnerRevision; selectedCalls = selectedBlocked = 0; }
            tracedAt = Time.realtimeSinceStartupAsDouble;
            selectedCalls++; if (!__result) selectedBlocked++; lastAllowed = __result;
        }
        private static string Describe(ZDO data)
        {
            if (!present) return "; PUP=absent";
            if (native == null) return "; PUP=unsupported readiness";
            return "; PUP session=" + native.SessionReady + "; PUP local=" + native.Relevant(data.GetPosition(), 16f) +
                (ReferenceEquals(traced, data) && tracedOwner == data.GetOwner() && tracedRevision == data.OwnerRevision ?
                    "; PUP allowed=" + lastAllowed + "; PUP calls=" + selectedCalls + "; PUP blocked=" + selectedBlocked +
                    "; PUP sample age=" + Math.Max(0, Time.realtimeSinceStartupAsDouble - tracedAt).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s" : "; PUP=no current-owner sample");
        }
#endif
        internal static void Stop()
        {
            if (hooks != null) hooks.UnpatchSelf(); hooks = null; native = null; present = false;
            FgnCombatOwnership.ServerCollisionReady = null; AuthorityController.MobServerReady = null;
#if TML_DEBUG_SYSTEM
            FgnSoloCombatTest.CollisionStatus = null; traced = null;
#endif
            prepare = null; held = null; guardType = null;
        }
    }
}
