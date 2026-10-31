using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // Only persistent world props and their explicit native successors. Loot
    // tables and visual EffectLists never make a source interactive.
    internal static class PhysicsObjects
    {
        private sealed class State
        {
            internal Rigidbody Body;
            internal ZSyncTransform Sync;
            internal bool Source;
        }
        private static ConditionalWeakTable<GameObject, State> states = new ConditionalWeakTable<GameObject, State>();
        private static readonly Func<ZSyncTransform, bool> wasOwner = FieldReader.Create<ZSyncTransform, bool>(AccessTools.Field(typeof(ZSyncTransform), "m_wasOwner"));
        internal static bool Enabled;

        private static bool BodyRoot(GameObject root, out Rigidbody body, out ZSyncTransform sync)
        {
            body = null; sync = null;
            if (!root || root.GetComponent<Projectile>() || root.GetComponent<ItemDrop>() || root.GetComponent<Character>() ||
                root.GetComponent<Ship>() || root.GetComponent<Vagon>()) return false;
            ZNetView view = root.GetComponent<ZNetView>();
            if (!view || !view.m_persistent || view.m_distant) return false;
            body = root.GetComponent<Rigidbody>(); sync = root.GetComponent<ZSyncTransform>();
            // Preserve native motion transfer instead of inventing a second
            // serializer or taking over joints/multiple independently moving bodies.
            if (!body || !sync || !sync.m_syncPosition || !sync.m_syncRotation || !sync.m_syncBodyVelocity ||
                root.GetComponentsInChildren<Rigidbody>(true).Length != 1 || root.GetComponentInChildren<Joint>(true)) return false;
            foreach (ZNetView child in root.GetComponentsInChildren<ZNetView>(true)) if (child != view) return false;
            return true;
        }
        private static bool Successor(GameObject root, int depth)
        {
            if (!root || depth > 8 || root.GetComponent<Projectile>() || root.GetComponent<ItemDrop>() || root.GetComponent<Character>()) return false;
            Rigidbody body; ZSyncTransform sync;
            if (BodyRoot(root, out body, out sync)) return true;
            TreeBase tree = root.GetComponent<TreeBase>();
            if (tree && tree.m_logPrefab && !ReferenceEquals(tree.m_logPrefab, root) && Successor(tree.m_logPrefab, depth + 1)) return true;
            Destructible destructible = root.GetComponent<Destructible>();
            return destructible && destructible.m_ttl <= 0f && destructible.m_spawnWhenDestroyed &&
                !ReferenceEquals(destructible.m_spawnWhenDestroyed, root) && Successor(destructible.m_spawnWhenDestroyed, depth + 1);
        }
        private static State Inspect(GameObject root)
        {
            State state;
            if (states.TryGetValue(root, out state)) return state;
            state = new State();
            Rigidbody body; ZSyncTransform sync;
            if (BodyRoot(root, out body, out sync)) { state.Body = body; state.Sync = sync; }
            else
            {
                ZNetView view = root.GetComponent<ZNetView>();
                if (view && view.m_persistent && !root.GetComponent<Projectile>() && !root.GetComponentInChildren<Rigidbody>(true))
                {
                    TreeBase tree = root.GetComponent<TreeBase>(); Destructible destructible = root.GetComponent<Destructible>();
                    state.Source = (tree && Successor(tree.m_logPrefab, 0)) ||
                        (destructible && destructible.m_ttl <= 0f && Successor(destructible.m_spawnWhenDestroyed, 0));
                }
            }
            states.Add(root, state); return state;
        }
        internal static bool Eligible(GameObject root) { if (!root) return false; State s = Inspect(root); return s.Body || s.Source; }
        internal static bool FixedSource(GameObject root) { return root && Inspect(root).Source; }
        internal static Rigidbody Body(GameObject root) { return root ? Inspect(root).Body : null; }
        internal static bool Sleeping(ZNetView view)
        { Rigidbody body = view && view.IsValid() && view.IsOwner() ? Body(view.gameObject) : null; return body && !body.isKinematic && body.IsSleeping(); }
        internal static void FlushMotion(ZNetView view)
        { if (view && view.IsValid() && view.IsOwner()) { State s = Inspect(view.gameObject); if (s.Body && !s.Body.isKinematic && s.Sync) s.Sync.SyncNow(); } }
        internal static void Reset() { Enabled = false; states = new ConditionalWeakTable<GameObject, State>(); }

        // Reuse the existing native sync tick. No component/timer per prop;
        // steady-owner ticks only read the cached native ownership flag.
        [HarmonyPatch(typeof(ZSyncTransform), "CustomFixedUpdate")]
        internal static class GainPatch
        {
            [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyBefore(Plugin.FgnId)]
            private static bool Prefix(ZSyncTransform __instance, ZNetView ___m_nview)
            {
                if (!Enabled || wasOwner(__instance) || !___m_nview || !___m_nview.IsValid() || !___m_nview.IsOwner()) return true;
                State state = Inspect(___m_nview.gameObject);
                if (!state.Body || state.Sync != __instance) return true;
                // An existing collision/body guard may be holding this body.
                // Do not advance m_wasOwner or unfreeze it ourselves.
                if (state.Body.isKinematic) return false;
                __instance.SyncNow(); return true;
            }
        }
    }
}
