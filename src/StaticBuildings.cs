using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TooManyLags
{
    // Shared static policy for fixed building pieces and native world objects.
    // Extra visual/mod components do not by themselves make a root interactive.
    internal sealed class StaticBuildings
    {
        private sealed class State
        {
            internal ZDO Data; internal ZDOID Id; internal WearNTear Wear;
            internal bool Eligible, Ready; internal Vector3 ReadyPosition;
        }
        private readonly Dictionary<int, bool> prefabs = new Dictionary<int, bool>();
        private readonly List<Vector2s> centers = new List<Vector2s>();
        private readonly ConditionalWeakTable<ZNetView, State> states = new ConditionalWeakTable<ZNetView, State>();
        private ZNetScene scene; private long server; private int baked;
        private bool takeovers;
        private readonly Func<bool> takeoversReady;
        internal StaticBuildings(Func<bool> takeoversReady) { this.takeoversReady = takeoversReady; }
        internal void Begin(ZNet network, ZNetScene scene, long server, int baked, bool takeovers)
        {
            this.scene = scene; this.server = server; this.baked = baked; this.takeovers = takeovers;
            centers.Clear();
            foreach (ZNetPeer peer in network.GetPeers())
            {
                if (peer == null || !peer.IsReady() || !Finite(peer.GetRefPos())) continue;
                Vector2s center = ZoneSystem.GetZone(peer.GetRefPos());
                if (!centers.Contains(center)) centers.Add(center);
            }
        }
        internal bool Manage(ZDO data)
        {
            if (centers.Count == 0 || !data.IsValid() || data.m_uid.IsNone() || BakedObjects.IsBaked(data) || (baked != 0 && data.GetBool(baked))) return false;
            bool eligible; int hash = data.GetPrefab();
            if (!prefabs.TryGetValue(hash, out eligible))
            {
                GameObject prefab = scene.GetPrefab(hash); if (!prefab) return false;
                eligible = Safe(prefab); prefabs.Add(hash, eligible);
            }
            if (!eligible) return false;
            Vector3 position = data.GetPosition(); if (!Finite(position)) return false;
            bool covered = false; foreach (Vector2s center in centers) if (ZNetScene.InActiveArea(position, center)) { covered = true; break; }
            if (!covered) return false;
            ZNetView view = scene.FindInstance(data);
            if (!view || !view.IsValid() || !view.gameObject.activeInHierarchy || !ReferenceEquals(view.GetZDO(), data)) return false;
            Transform pose = view.transform; Vector3 live = pose.position;
            if (pose.parent != null || !Finite(live) || (live - position).sqrMagnitude > 0.0025f) return false;
            State state;
            if (!states.TryGetValue(view, out state))
            {
                state = new State { Data = data, Id = data.m_uid, Wear = view.GetComponent<WearNTear>(), Eligible = Safe(view.gameObject) };
                states.Add(view, state);
            }
            if (!ReferenceEquals(state.Data, data) || state.Id != data.m_uid || !state.Eligible ||
                (state.Wear && (!state.Wear.enabled || !state.Wear.m_staticPosition))) return false;
            if (data.GetOwner() == server && state.Ready && state.ReadyPosition == position) return true;
            if (!takeovers || !data.IsValid() || state.Id != data.m_uid || !view || !view.IsValid() ||
                !ReferenceEquals(view.GetZDO(), data) || data.GetPosition() != position) return false;
            if (data.GetOwner() != server)
            {
                // Unlike the legacy addon, never cause the native revision wrap.
                if (data.OwnerRevision == UInt16.MaxValue) return false;
                if (!takeoversReady() || !Safe(view.gameObject)) return false;
                data.SetOwner(server);
            }
            state.Ready = true; state.ReadyPosition = position; return true;
        }
        internal void Remove(ZNetView view) { states.Remove(view); }
        internal static bool Safe(GameObject root, MonoBehaviour[] components = null)
        {
            WearNTear wear = root.GetComponent<WearNTear>(); ZNetView view = root.GetComponent<ZNetView>();
            // Native standing trees can be distant roots. Their fixed network
            // identity is safe; active-area and recipient checks still apply.
            if (!view || !view.m_persistent || root.GetComponentsInChildren<Rigidbody>(true).Length != 0) return false;
            // These roots belong to the separate interactive category even
            // when that category is disabled; the legacy static-only path
            // must not silently take them over through its own prefab cache.
            if (root.GetComponent<Projectile>() || PhysicsObjects.FixedSource(root)) return false;
            Destructible destructible = root.GetComponent<Destructible>();
            // Native decorative vines have fixed WearNTear but no Piece.
            // Build-menu membership is not part of the network/safety contract.
            bool building = wear && wear.m_staticPosition;
            bool world = root.GetComponent<TreeBase>() || root.GetComponent<MineRock>() || root.GetComponent<MineRock5>() ||
                (destructible && destructible.m_ttl <= 0f);
            if ((!building && !world) || (wear && !wear.m_staticPosition)) return false;
            foreach (MonoBehaviour component in components ?? root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!component || component is Interactable || component is Character || component is BaseAI ||
                    component is ItemDrop || component is Container || component is ZSyncTransform || component is TreeLog ||
                    component is SpawnArea || component is CreatureSpawner || component is SpawnSystem ||
                    (component is Destructible && ((Destructible)component).m_ttl > 0f) ||
                    (component is ZNetView && component != view) || component.GetComponentInParent<ZNetView>(true) != view) return false;
            }
            return true;
        }
        private static bool Finite(Vector3 p)
        { return !(Single.IsNaN(p.x) || Single.IsNaN(p.y) || Single.IsNaN(p.z) || Single.IsInfinity(p.x) || Single.IsInfinity(p.y) || Single.IsInfinity(p.z)); }
    }
}
