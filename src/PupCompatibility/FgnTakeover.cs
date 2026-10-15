using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags.PupCompatibility
{
    internal static class FgnTakeover
    {
        private sealed class ActorSync { internal ZSyncTransform Sync; }
        internal struct Snapshot { internal Component Guard; }
        private static ConditionalWeakTable<Character, ActorSync> actors = new ConditionalWeakTable<Character, ActorSync>();
        private static readonly Func<ZSyncTransform, bool> wasOwner = FieldReader.Create<ZSyncTransform, bool>(AccessTools.Field(typeof(ZSyncTransform), "m_wasOwner"));
        private static readonly Func<Character, ZNetView> actorView = FieldReader.Create<Character, ZNetView>(AccessTools.Field(typeof(Character), "m_nview"));
        internal static void Reset() { actors = new ConditionalWeakTable<Character, ActorSync>(); }
        internal static void Install(Harmony harmony, Assembly fgn)
        {
            // Validate the known ownership-snap contract. Ordinary steady-owner
            // ticks return after one cached native boolean read.
            Type handoff = fgn.GetType("FiresGhettoNetworkMod.OwnershipHandoffPatches", false);
            MethodInfo snap = handoff == null ? null : handoff.GetMethod("ZSyncTransform_CustomFixedUpdate_Prefix",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ZSyncTransform), typeof(ZNetView), typeof(Rigidbody) }, null);
            if (snap == null || snap.ReturnType != typeof(bool))
                throw new NotSupportedException("FGN ownership handoff contract changed.");
            harmony.CreateClassProcessor(typeof(SyncPatch)).Patch();
            harmony.CreateClassProcessor(typeof(MobPatch)).Patch();
            Type echo = fgn.GetType("FiresGhettoNetworkMod.OwnedPositionEcho", false);
            if (echo != null)
            {
                MethodInfo receive = echo.GetMethod("Receive_Finalizer", BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                FieldInfo moved = echo.GetField("s_moved", BindingFlags.Static | BindingFlags.NonPublic);
                if (receive == null || receive.ReturnType != typeof(void) || moved == null || moved.FieldType != typeof(List<KeyValuePair<ZDO, ZNetView>>))
                    throw new NotSupportedException("FGN owned-position echo contract changed.");
                harmony.Patch(receive, prefix: new HarmonyMethod(AccessTools.Method(typeof(FgnTakeover), "BeforeEcho")));
            }
        }
        private static bool Needs(ZSyncTransform sync, ZNetView view)
        { return sync && !wasOwner(sync) && sync.m_syncPosition && view && view.IsValid() && view.IsOwner(); }
        internal static Component Prepare(ZSyncTransform sync, ZNetView view, out bool allowed)
        {
            allowed = true;
            NativePup pup = Plugin.Native;
            if (pup == null || !pup.Active || !Needs(sync, view)) return null;
            Character actor = view.GetComponent<Character>();
            // FGN vehicles have an independent terrain freeze; pre-freezing them
            // would change its isKinematic-based ownership-gain decision.
            if (!actor || actor is Player) return null;
            if (view.GetComponent<Projectile>()) return null; // FGN runs its own arrow simulation, not this ownership snap.
            Component guard = pup.Existing(view.gameObject);
            // PUP deliberately excludes players and objects without rigidbodies.
            if (!guard) return null;
            Vector3 destination = view.GetZDO().GetPosition();
            if (pup.SessionReady && !pup.Relevant(destination, 16f) && !pup.Relevant(view.transform.position, 16f)) return null;
            bool ready = pup.ReadyPoint(destination, 16f);
            allowed = pup.ForceStep(guard, !ready);
            return guard;
        }
        [HarmonyPatch(typeof(ZSyncTransform), "CustomFixedUpdate")]
        internal static class SyncPatch
        {
            [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyBefore(Plugin.FgnId)]
            internal static void Prefix(ZSyncTransform __instance, ZNetView ___m_nview, out Snapshot __state)
            {
                __state = default(Snapshot);
                if (wasOwner(__instance)) return;
                bool allowed;
                __state.Guard = Prepare(__instance, ___m_nview, out allowed);
            }
            [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyAfter(Plugin.FgnId)]
            internal static void Postfix(Snapshot __state)
            {
                if (!__state.Guard || Plugin.Native == null) return;
                // Runs even when another prefix bypasses vanilla. Force PUP to
                // inspect the corrected pose instead of reusing its 0.2s result.
                UnityEngine.Vector3 point = __state.Guard.transform.position;
                bool ready = Plugin.Native.ReadyPoint(point, 16f);
                Plugin.Native.ForceStep(__state.Guard, !ready);
            }
        }
        [HarmonyPatch]
        internal static class MobPatch
        {
            private static MethodBase TargetMethod() { return Plugin.Native.MobMethod; }
            [HarmonyPrefix]
            internal static bool Prefix(Character __0, ref bool __result)
            {
                if (!__0 || __0 is Player) return true;
                ZNetView view = actorView(__0);
                if (!view || !view.IsValid() || !view.IsOwner()) return true;
                ActorSync entry = actors.GetOrCreateValue(__0);
                if (!entry.Sync) entry.Sync = __0.GetComponent<ZSyncTransform>();
                if (!entry.Sync || wasOwner(entry.Sync)) return true;
                bool allowed;
                Component guard = Prepare(entry.Sync, view, out allowed);
                // Pending destination blocks native AI/motion before FGN snaps.
                if (guard && !allowed)
                { __result = false; return false; }
                return true;
            }
        }
        internal static void BeforeEcho(List<KeyValuePair<ZDO, ZNetView>> ___s_moved)
        {
            NativePup pup = Plugin.Native;
            if (pup == null || !pup.Active || ___s_moved == null || ___s_moved.Count == 0) return;
            int write = 0;
            for (int read = 0; read < ___s_moved.Count; read++)
            {
                KeyValuePair<ZDO, ZNetView> entry = ___s_moved[read]; ZNetView view = entry.Value;
                bool preserve = false;
                if (view && view.IsValid() && ReferenceEquals(view.GetZDO(), entry.Key) && view.IsOwner())
                {
                    ZSyncTransform sync = view.GetComponent<ZSyncTransform>();
                    if (Needs(sync, view))
                    {
                        Character actor = view.GetComponent<Character>();
                        if (actor && !(actor is Player) && !view.GetComponent<Projectile>() && pup.Existing(view.gameObject))
                            preserve = !pup.SessionReady || pup.Relevant(entry.Key.GetPosition(), 16f) || pup.Relevant(view.transform.position, 16f);
                    }
                }
                if (preserve) continue;
                if (write != read) ___s_moved[write] = entry;
                write++;
            }
            if (write < ___s_moved.Count) ___s_moved.RemoveRange(write, ___s_moved.Count - write);
        }
    }
}
