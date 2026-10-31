#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.LowLevel;

namespace TooManyLags
{
    internal sealed class MobBridgeEffects : IDisposable
    {
        internal static Action<ZSyncTransform> OwnershipSynced = null;
        internal static Action<ZSyncTransform> AfterOwnerSync = null;
        private sealed class Anchor { internal Transform Source, Value; }
        private sealed class Epoch { internal ZDO Data; internal uint Revision, Token; }
        private sealed class NativeFxScope
        {
            internal readonly List<MobBridgeEffectAttachment> Values = new List<MobBridgeEffectAttachment>();
            private NativeFxScope next;
            private static NativeFxScope spare;
            private static int retained;
            internal static NativeFxScope Take()
            {
                if (spare == null) return new NativeFxScope();
                NativeFxScope value = spare; spare = value.next;
                value.next = null; --retained; return value;
            }
            internal void Release()
            {
                // Clear Unity references before retaining storage. An in-flight
                // scope never enters the pool, including nested callbacks.
                Values.Clear();
                if (retained >= 8) return;
                next = spare; spare = this; ++retained;
            }
            internal static void Clear() { spare = null; retained = 0; }
        }
        private sealed class ParticleAnchorUpdate { }
        private static readonly Dictionary<ZDOID, MobBridgeEffects> active = new Dictionary<ZDOID, MobBridgeEffects>();
        private static readonly Dictionary<Transform, MobBridgeEffects> roots = new Dictionary<Transform, MobBridgeEffects>();
        private static readonly Dictionary<ZDOID, MobBridgeEffects> knownFx = new Dictionary<ZDOID, MobBridgeEffects>();
        private static readonly Dictionary<Transform, MobBridgeEffectAttachment> bindings = new Dictionary<Transform, MobBridgeEffectAttachment>();
        private static readonly Dictionary<ZSyncTransform, MobBridgeEffectAttachment> syncBindings = new Dictionary<ZSyncTransform, MobBridgeEffectAttachment>();
        private static readonly Dictionary<Transform, ZSyncTransform> effectSync = new Dictionary<Transform, ZSyncTransform>();
        private static readonly HashSet<ZNetView> pending = new HashSet<ZNetView>();
        private static readonly List<ZNetView> pendingDone = new List<ZNetView>();
        private static readonly List<ZNetView> pendingSnapshot = new List<ZNetView>();
        private static readonly Dictionary<ZDOID, Epoch> expected = new Dictionary<ZDOID, Epoch>();
        private static readonly Func<StatusEffect, GameObject[]> statusFx = FieldReader.Create<StatusEffect, GameObject[]>(
            AccessTools.Field(typeof(StatusEffect), "m_startEffectInstances"));
        private static readonly Func<ZSyncTransform, ZNetView> syncView = FieldReader.Create<ZSyncTransform, ZNetView>(
            AccessTools.Field(typeof(ZSyncTransform), "m_nview"));
        private static readonly Func<Character, GameObject[]>[] continuousFx = ContinuousAccessors();
        private static readonly int parentUser = "tml_fx_parent_user".GetStableHashCode(), parentId = "tml_fx_parent_id".GetStableHashCode(),
            parentPath = "tml_fx_parent_path".GetStableHashCode(), offsetPosition = "tml_fx_position".GetStableHashCode(),
            offsetRotation = "tml_fx_rotation".GetStableHashCode(), offsetScale = "tml_fx_scale".GetStableHashCode();
        private static Harmony hooks;
        private static bool loopInstalled;
        private readonly Character actor;
        private readonly Transform actorRoot, visual;
        private readonly ZDOID id;
        private readonly List<Anchor> anchors = new List<Anchor>();
        private readonly List<MobBridgeEffectAttachment> effects = new List<MobBridgeEffectAttachment>();
        private readonly List<ZDOID> referencedFx = new List<ZDOID>();
        private bool disposed;
        internal int Attached { get; private set; }

        internal static void PrepareSession()
        {
            if (hooks != null) return;
            Harmony next = new Harmony(Plugin.Id + ".mobbridge.effects");
            try
            {
                next.Patch(AccessTools.Method(typeof(EffectList), "Create"), postfix: new HarmonyMethod(typeof(MobBridgeEffects), "Created"));
                next.Patch(AccessTools.Method(typeof(ZNetScene), "AddInstance", new[] { typeof(ZDO), typeof(ZNetView) }),
                    postfix: new HarmonyMethod(typeof(MobBridgeEffects), "Added"));
                next.Patch(AccessTools.Method(typeof(Character), "SetupContinuousEffect"),
                    prefix: new HarmonyMethod(typeof(MobBridgeEffects), "BeforeContinuous"),
                    finalizer: new HarmonyMethod(typeof(MobBridgeEffects), "AfterContinuous"));
                foreach (string method in new[] { "CustomFixedUpdate", "CustomLateUpdate", "SyncNow" })
                    next.Patch(AccessTools.Method(typeof(ZSyncTransform), method),
                        prefix: new HarmonyMethod(typeof(MobBridgeEffects), method == "CustomFixedUpdate" ? "BeforeSync" : "BeforeOwnerSync"),
                        finalizer: new HarmonyMethod(typeof(MobBridgeEffects), "AfterSync"));
                hooks = next;
            }
            catch { next.UnpatchSelf(); throw; }
        }
        internal static void Expect(ZDO data, uint revision, uint token)
        {
            if (data == null) return;
            expected[data.m_uid] = new Epoch { Data = data, Revision = revision, Token = token };
        }
        internal static void Finished(ZDOID id, uint revision, uint token, byte stage)
        {
            Epoch item;
            if (stage == 2 && expected.TryGetValue(id, out item) && item.Revision == revision && item.Token == token)
                expected.Remove(id);
        }
        internal static void Cancel(ZDOID id) { expected.Remove(id); }
        private static bool Expected(ZDO data)
        {
            Epoch item;
            if (data == null || !expected.TryGetValue(data.m_uid, out item)) return false;
            if (item.Data.IsValid() && item.Data.IsOwner() && item.Data.OwnerRevision == item.Revision + 1U) return true;
            expected.Remove(data.m_uid); return false;
        }

        internal MobBridgeEffects(Character actor, ZDOID[] statusReferences)
        {
            this.actor = actor; actorRoot = actor.transform; visual = actor.GetVisual().transform; id = actor.GetZDOID();
            InstallLoop(); active.Add(id, this); roots.Add(actorRoot, this);
            try
            {
                // Combat relinquishment clears the live SEMan before visual
                // startup. Use IDs from the already captured status snapshot,
                // including FX whose network object has not arrived locally.
                if (statusReferences != null)
                    foreach (ZDOID reference in statusReferences)
                    {
                        if (reference.IsNone()) continue;
                        knownFx[reference] = this; referencedFx.Add(reference);
                        GameObject item = ZNetScene.instance ? ZNetScene.instance.FindInstance(reference) : null;
                        if (item) Existing(item, actor.transform);
                    }
                // Existing status FX may be world-rooted on a remote client.
                foreach (StatusEffect effect in actor.GetSEMan().GetStatusEffects())
                {
                    GameObject[] objects = statusFx(effect);
                    if (objects != null) foreach (GameObject item in objects) Existing(item, actor.transform);
                }
                foreach (Func<Character, GameObject[]> read in continuousFx)
                {
                    GameObject[] objects = read(actor);
                    if (objects != null) foreach (GameObject item in objects) Existing(item, actor.transform);
                }
                // Scan once per handoff. New vanilla FX are event-discovered.
                foreach (ParticleSystem item in actor.GetComponentsInChildren<ParticleSystem>(true)) Existing(item.gameObject, item.transform.parent);
                foreach (Light item in actor.GetComponentsInChildren<Light>(true)) Existing(item.gameObject, item.transform.parent);
                foreach (AudioSource item in actor.GetComponentsInChildren<AudioSource>(true)) Existing(item.gameObject, item.transform.parent);
                foreach (Renderer item in actor.GetComponentsInChildren<Renderer>(true))
                    if (!item.transform.IsChildOf(visual)) Existing(item.gameObject, item.transform.parent);
            }
            catch { Dispose(); throw; }
        }
        private static bool VisualOnly(GameObject item)
        {
            return item && !item.GetComponent<Character>() && !item.GetComponent<Projectile>() && !item.GetComponent<Aoe>() &&
                !item.GetComponent<ItemDrop>() && item.GetComponentsInChildren<Collider>(true).Length == 0 &&
                item.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                (item.GetComponentInChildren<Renderer>(true) || item.GetComponentInChildren<Light>(true) ||
                 item.GetComponentInChildren<AudioSource>(true));
        }
        private static Func<Character, GameObject[]>[] ContinuousAccessors()
        {
            List<Func<Character, GameObject[]>> result = new List<Func<Character, GameObject[]>>();
            foreach (string name in new[] { "m_waterEffects_instances", "m_slideEffects_instances", "m_flyingEffects_instances" })
                result.Add(FieldReader.Create<Character, GameObject[]>(AccessTools.Field(typeof(Character), name)));
            return result.ToArray();
        }
        private void Existing(GameObject item, Transform source)
        {
            if (!item || item == actor.gameObject || item == visual.gameObject) return;
            ZNetView view = item.GetComponentInParent<ZNetView>();
            if (view && view.gameObject != actor.gameObject && view.transform.IsChildOf(actor.transform)) item = view.gameObject;
            if (!item || Covered(item.transform)) return;
            ZNetView ownView = item.GetComponent<ZNetView>();
            if (ownView && ownView.IsValid() && ownView.GetZDO().GetString(parentPath, "").Length != 0)
            {
                Discover(ownView.GetZDO(), ownView);
                if (Covered(item.transform)) return;
            }
            // An existing vanilla constraint is a more precise attachment than
            // the fallback actor root (for example a named head/hand bone).
            ParentConstraint constraint = item.GetComponent<ParentConstraint>();
            if (constraint)
                for (int i = 0; i < constraint.sourceCount; ++i)
                {
                    Transform candidate = constraint.GetSource(i).sourceTransform;
                    if (candidate && candidate.IsChildOf(actor.transform)) { source = candidate; break; }
                }
            if (item.transform.parent && item.transform.parent.IsChildOf(actor.transform)) source = item.transform.parent;
            if (!source || !source.IsChildOf(actor.transform)) source = actor.transform;
            Vector3 localPosition = source.InverseTransformPoint(item.transform.position);
            Quaternion localRotation = Quaternion.Inverse(source.rotation) * item.transform.rotation;
            if (constraint && constraint.sourceCount == 1 && constraint.GetSource(0).sourceTransform == source)
            { localPosition = constraint.GetTranslationOffset(0); localRotation = Quaternion.Euler(constraint.GetRotationOffset(0)); }
            Add(item, source, localPosition, localRotation, Divide(item.transform.lossyScale, source.lossyScale));
        }
        private bool Covered(Transform item)
        {
            foreach (MobBridgeEffectAttachment effect in effects)
                if (effect.Alive && (item == effect.Root || item.IsChildOf(effect.Root))) return true;
            return false;
        }
        private Anchor GetAnchor(Transform source)
        {
            foreach (Anchor value in anchors) if (value.Source == source) return value;
            Transform root = new GameObject("TooManyLags_EffectAnchor").transform;
            // Preserve GetComponentInParent<Character> and normal hierarchy
            // visibility for visual scripts while separating the pose offset.
            root.SetParent(source, false);
            root.SetPositionAndRotation(source.position, source.rotation); root.localScale = Vector3.one;
            Anchor next = new Anchor { Source = source, Value = root }; anchors.Add(next); return next;
        }
        private void Add(GameObject item, Transform source, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (disposed || !source || !VisualOnly(item) || Covered(item.transform) || bindings.ContainsKey(item.transform)) return;
            MobBridgeEffectAttachment binding = new MobBridgeEffectAttachment(item.transform, source,
                GetAnchor(source).Value, position, rotation, scale);
            effects.Add(binding); bindings.Add(item.transform, binding);
            ++Attached;
            ZSyncTransform sync = item.GetComponent<ZSyncTransform>();
            if (sync) { syncBindings.Add(sync, binding); effectSync.Add(item.transform, sync); }
        }
        internal void Update(Vector3 position, Quaternion rotation, MobBridgeAnimation animation)
        {
            if (disposed) return;
            Quaternion inverse = Quaternion.Inverse(actorRoot.rotation);
            for (int i = effects.Count - 1; i >= 0; --i)
                if (!effects[i].Alive)
                { Forget(effects[i]); effects[i].Dispose(); effects.RemoveAt(i); }
            foreach (Anchor anchor in anchors)
            {
                if (!anchor.Source || !anchor.Value) continue;
                bool enabled = anchor.Source.gameObject.activeInHierarchy;
                if (anchor.Value.gameObject.activeSelf != enabled) anchor.Value.gameObject.SetActive(enabled);
                Vector3 p, scale; Quaternion q;
                if (!animation.TryPose(anchor.Source, out p, out q, out scale))
                {
                    p = position + rotation * (inverse * (anchor.Source.position - actorRoot.position));
                    q = rotation * inverse * anchor.Source.rotation; scale = anchor.Source.lossyScale;
                }
                if (!anchor.Value.position.Equals(p) || !anchor.Value.rotation.Equals(q)) anchor.Value.SetPositionAndRotation(p, q);
                Vector3 localScale = Divide(scale, anchor.Source.lossyScale);
                if (!anchor.Value.localScale.Equals(localScale)) anchor.Value.localScale = localScale;
            }
        }
        private static Vector3 Divide(Vector3 value, Vector3 by)
        { return new Vector3(by.x == 0f ? 0f : value.x / by.x, by.y == 0f ? 0f : value.y / by.y, by.z == 0f ? 0f : value.z / by.z); }
        private static string Path(Transform root, Transform child)
        {
            if (root == child) return ".";
            string path = child.name;
            for (Transform parent = child.parent; parent && parent != root; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }
        private static void Created(EffectList __instance, Transform __2, int __4, GameObject[] __result)
        {
            try { OnCreated(__instance, __2, __4, __result); }
            catch (Exception error) { Plugin.Warn("Attached FX presentation retained native effect: " + error.GetBaseException().Message); }
        }
        private static void OnCreated(EffectList list, Transform parent, int variant, GameObject[] objects)
        {
            if ((active.Count == 0 && expected.Count == 0) || !parent || objects == null || objects.Length == 0) return;
            Character owner = parent.GetComponentInParent<Character>();
            if (!owner || owner is Player) return;
            ZNetView actorView = owner.GetComponent<ZNetView>();
            ZDO data = actorView && actorView.IsValid() ? actorView.GetZDO() : null;
            MobBridgeEffects local = null;
            if (data != null) active.TryGetValue(data.m_uid, out local);
            bool tag = Expected(data);
            if (local == null && !tag) return;
            int index = 0;
            foreach (EffectList.EffectData template in list.m_effectPrefabs)
            {
                // Match vanilla's enabled/variant output order. Two entries
                // may use the same prefab on different bones; names alone
                // cannot identify their actual attachment.
                if (!template.m_enabled || variant >= 0 && template.m_variant >= 0 && template.m_variant != variant) continue;
                if (index >= objects.Length) break;
                GameObject item = objects[index++];
                if (!VisualOnly(item) || (!template.m_attach && !template.m_follow)) continue;
                Transform source = String.IsNullOrEmpty(template.m_childTransform) ? parent : Utils.FindChild(parent, template.m_childTransform);
                if (!source) source = parent;
                if (!source.IsChildOf(owner.transform)) continue;
                Vector3 p = source.InverseTransformPoint(item.transform.position);
                Quaternion q = Quaternion.Inverse(source.rotation) * item.transform.rotation;
                Vector3 scale = Divide(item.transform.lossyScale, source.lossyScale);
                ParentConstraint follow = item.GetComponent<ParentConstraint>();
                if (template.m_follow && follow && follow.sourceCount == 1)
                { p = follow.GetTranslationOffset(0); q = Quaternion.Euler(follow.GetRotationOffset(0)); }
                ZNetView view = item.GetComponent<ZNetView>();
                if (tag && view && view.IsValid() && view.IsOwner())
                {
                    ZDO fx = view.GetZDO(); fx.Set(parentUser, data.m_uid.UserID); fx.Set(parentId, (long)data.m_uid.ID);
                    fx.Set(offsetPosition, p); fx.Set(offsetRotation, q); fx.Set(offsetScale, scale);
                    fx.Set(parentPath, Path(owner.transform, source));
                }
                if (local != null) local.Add(item, source, p, q, scale);
            }
        }
        private static void Added(ZDO __0, ZNetView __1)
        {
            if (active.Count == 0) return;
            try
            {
                MobBridgeEffects captured;
                if (__0 != null && __1 && knownFx.TryGetValue(__0.m_uid, out captured))
                    captured.Existing(__1.gameObject, captured.actor.transform);
                Discover(__0, __1);
                // Creation can precede its attachment-tag delta. Retry only
                // newly created visual FX, never every world object's sync.
                if (__0 != null && __1 && !bindings.ContainsKey(__1.transform))
                {
                    long user = __0.GetLong(parentUser, __0.GetLong(ZDOVars.s_ownerZDOUserId, 0L));
                    uint identity = (uint)__0.GetLong(parentId, __0.GetLong(ZDOVars.s_ownerZDOId, 0L));
                    if (active.ContainsKey(new ZDOID(user, identity)) && VisualOnly(__1.gameObject)) pending.Add(__1);
                }
            }
            catch (Exception error) { Plugin.Warn("Late attached FX retained native presentation: " + error.GetBaseException().Message); }
        }
        private static void Discover(ZDO data, ZNetView view)
        {
            if (data == null || !view || !data.IsValid()) return;
            string path = data.GetString(parentPath, "");
            if (path.Length == 0 || path.Length > 1024) return;
            MobBridgeEffects owner;
            if (!active.TryGetValue(new ZDOID(data.GetLong(parentUser, 0L), (uint)data.GetLong(parentId, 0L)), out owner)) return;
            Transform source = path == "." ? owner.actor.transform : owner.actor.transform.Find(path);
            if (source) owner.Add(view.gameObject, source, data.GetVec3(offsetPosition, Vector3.zero),
                data.GetQuaternion(offsetRotation, Quaternion.identity), data.GetVec3(offsetScale, Vector3.one));
        }
        private static void BeforeSync(ZSyncTransform __instance, out MobBridgeEffectAttachment __state)
        { BeginSync(__instance, false, out __state); }
        private static void BeforeOwnerSync(ZSyncTransform __instance, out MobBridgeEffectAttachment __state)
        { BeginSync(__instance, true, out __state); }
        private static void BeginSync(ZSyncTransform sync, bool ownerOnly, out MobBridgeEffectAttachment state)
        {
            state = null;
            // Idle and unrelated objects do not call Unity GetComponent or
            // Transform APIs; they only take this dictionary fast path.
            if (active.Count == 0 || !syncBindings.TryGetValue(sync, out state)) return;
            try
            {
                ZNetView view = syncView(sync);
                if (!view || !view.IsValid() || view.IsOwner() != ownerOnly) { state = null; return; }
                state.EnterNative();
            }
            catch (Exception error)
            {
                if (state != null) { state.Dispose(); state = null; }
                Plugin.Warn("FX network sync retains native pose: " + error.GetBaseException().Message);
            }
        }
        private static Exception AfterSync(ZSyncTransform __instance, MobBridgeEffectAttachment __state, Exception __exception)
        {
            try { if (__state != null) __state.ExitNative(); }
            catch (Exception error) { if (__state != null) __state.Dispose(); Plugin.Warn("FX presentation restore failed: " + error.GetBaseException().Message); }
            if (__exception == null && AfterOwnerSync != null)
            {
                try { AfterOwnerSync(__instance); }
                catch (Exception error) { Plugin.Warn("Owner checkpoint pose reapply failed: " + error.GetBaseException().Message); }
            }
            if (__exception == null && OwnershipSynced != null)
            {
                try { OwnershipSynced(__instance); }
                catch (Exception error) { Plugin.Warn("Ownership presentation retained native sync: " + error.GetBaseException().Message); }
            }
            return __exception;
        }
        private static void BeforeContinuous(Transform __0, GameObject[] __4, out NativeFxScope __state)
        {
            __state = null;
            if (active.Count == 0 || __4 == null || !roots.ContainsKey(__0)) return;
            try
            {
                foreach (GameObject item in __4)
                {
                    MobBridgeEffectAttachment effect;
                    if (!item || !bindings.TryGetValue(item.transform, out effect) || !effect.Alive) continue;
                    if (__state == null) __state = NativeFxScope.Take();
                    __state.Values.Add(effect); effect.EnterNative();
                }
            }
            catch (Exception error)
            { AfterContinuous(__0, null, __state, error); __state = null; Plugin.Warn("Continuous FX retained native update: " + error.GetBaseException().Message); }
        }
        private static Exception AfterContinuous(Transform __0, GameObject[] __4, NativeFxScope __state, Exception __exception)
        {
            if (__state != null)
            {
                foreach (MobBridgeEffectAttachment effect in __state.Values)
                    try { effect.ExitNative(__exception == null); }
                    catch (Exception error) { effect.Dispose(); Plugin.Warn("Continuous FX retains native pose: " + error.GetBaseException().Message); }
                // Retain only successfully unwound scopes. If cleanup throws,
                // Harmony can still hold/retry its original untouched state.
                __state.Release();
            }
            if (__exception == null && active.Count != 0 && __0 && __4 != null)
            {
                MobBridgeEffects owner;
                if (roots.TryGetValue(__0, out owner))
                    try { foreach (GameObject item in __4) owner.Existing(item, __0); }
                    catch (Exception error) { Plugin.Warn("New continuous FX retained native presentation: " + error.GetBaseException().Message); }
            }
            return __exception;
        }

        private static void BeforeParticles()
        {
            if (active.Count == 0) return;
            try
            {
                pendingDone.Clear();
                pendingSnapshot.Clear(); pendingSnapshot.AddRange(pending);
                foreach (ZNetView view in pendingSnapshot)
                {
                    if (!view || !view.IsValid()) { pendingDone.Add(view); continue; }
                    Discover(view.GetZDO(), view);
                    if (bindings.ContainsKey(view.transform)) pendingDone.Add(view);
                }
                foreach (ZNetView view in pendingDone) pending.Remove(view);
                pendingDone.Clear();
                pendingSnapshot.Clear();
                MobBridgeVisual.PrepareEffectAnchors();
            }
            catch (Exception error) { Plugin.Warn("Mob effect presentation stopped: " + error.GetBaseException().Message); EndBindings(); }
        }
        private static void InstallLoop()
        {
            if (loopInstalled) return;
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!Insert(ref loop)) throw new NotSupportedException("Native particle/constraint update boundary unavailable.");
            PlayerLoop.SetPlayerLoop(loop); loopInstalled = true;
        }
        private static bool Insert(ref PlayerLoopSystem loop)
        {
            PlayerLoopSystem[] systems = loop.subSystemList;
            if (systems == null) return false;
            foreach (PlayerLoopSystem child in systems) if (child.type == typeof(ParticleAnchorUpdate)) return true;
            for (int i = 0; i < systems.Length; ++i)
            {
                Type type = systems[i].type;
                if (type == typeof(UnityEngine.PlayerLoop.PreLateUpdate.ParticleSystemBeginUpdateAll) ||
                    type == typeof(UnityEngine.PlayerLoop.PreLateUpdate.ConstraintManagerUpdate))
                {
                    PlayerLoopSystem[] next = new PlayerLoopSystem[systems.Length + 1];
                    Array.Copy(systems, 0, next, 0, i); Array.Copy(systems, i, next, i + 1, systems.Length - i);
                    next[i] = new PlayerLoopSystem { type = typeof(ParticleAnchorUpdate), updateDelegate = BeforeParticles };
                    loop.subSystemList = next; return true;
                }
                if (Insert(ref systems[i])) { loop.subSystemList = systems; return true; }
            }
            return false;
        }
        private static void RemoveLoop(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null) return;
            List<PlayerLoopSystem> next = new List<PlayerLoopSystem>();
            foreach (PlayerLoopSystem child in loop.subSystemList)
            {
                if (child.type == typeof(ParticleAnchorUpdate)) continue;
                PlayerLoopSystem kept = child; RemoveLoop(ref kept); next.Add(kept);
            }
            loop.subSystemList = next.ToArray();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            active.Remove(id);
            roots.Remove(actorRoot);
            foreach (ZDOID reference in referencedFx)
            {
                MobBridgeEffects current;
                if (knownFx.TryGetValue(reference, out current) && ReferenceEquals(current, this)) knownFx.Remove(reference);
            }
            foreach (MobBridgeEffectAttachment item in effects) { Forget(item); item.Dispose(); }
            foreach (Anchor anchor in anchors) if (anchor.Value) UnityEngine.Object.Destroy(anchor.Value.gameObject);
            effects.Clear(); anchors.Clear();
            referencedFx.Clear();
            if (active.Count == 0) pending.Clear();
        }
        private static void Forget(MobBridgeEffectAttachment item)
        {
            ZSyncTransform sync;
            if (effectSync.TryGetValue(item.Root, out sync))
            { syncBindings.Remove(sync); effectSync.Remove(item.Root); }
            bindings.Remove(item.Root);
        }
        private static void EndBindings()
        {
            List<MobBridgeEffects> current = new List<MobBridgeEffects>(active.Values);
            foreach (MobBridgeEffects item in current) item.Dispose();
        }
        internal static void EndSession()
        {
            EndBindings(); expected.Clear();
            NativeFxScope.Clear();
            pending.Clear(); pendingDone.Clear(); pendingSnapshot.Clear();
            if (loopInstalled)
            { PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop(); RemoveLoop(ref loop); PlayerLoop.SetPlayerLoop(loop); loopInstalled = false; }
            if (hooks != null) { hooks.UnpatchSelf(); hooks = null; }
        }
    }
}
#endif
