using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    internal sealed partial class Controller
    {
        private readonly Dictionary<ZDO, Node> mobile = new Dictionary<ZDO, Node>();
        private readonly HashSet<Node> changed = new HashSet<Node>(), defaults = new HashSet<Node>();
        private readonly List<Node> changedWork = new List<Node>(), defaultWork = new List<Node>();
        private readonly Dictionary<Cell, bool> readyAreas = new Dictionary<Cell, bool>();
        private float readyAt = Single.NegativeInfinity;
        private bool MobileAreaReady(Node node)
        {
            if (readyAt != Time.unscaledTime) { readyAt = Time.unscaledTime; readyAreas.Clear(); }
            Cell location = node.Cell;
            if (node.Ship)
            {
                Vector3 root = node.Data.GetPosition();
                if (!Cell.At(root.x, root.z, options.CellSize, out location)) return false;
            }
            Cell zone = Cell.Zone(location, options.CellSize); bool ready;
            if (!readyAreas.TryGetValue(zone, out ready)) { ready = ZNetScene.instance.IsAreaReady(node.Data.GetPosition()); readyAreas.Add(zone, ready); }
            return ready;
        }
        private static byte ActorState(ZDO data)
        {
            byte state = data.GetBool(ZDOVars.s_tamed) ? (byte)1 : (byte)0;
            if (data.GetLong(ZDOVars.s_user, 0) != 0 || !String.IsNullOrEmpty(data.GetString(ZDOVars.s_follow, "")) ||
                !String.IsNullOrEmpty(data.GetString(ZDOVars.s_attachJointHash, "")) || !data.GetConnectionZDOID(ZDOExtraData.ConnectionType.SyncTransform).IsNone()) state |= 2;
            if (data.GetBool(ZDOVars.s_alert) || data.GetBool(ZDOVars.s_haveTargetHash)) state |= 4;
            return state;
        }
        private static byte State(Node node)
        {
            if (node.Group == Groups.None) return 0;
            if (node.Group == Groups.HostileMobs) return (ActorState(node.Data) & 3) == 0 ? (byte)1 : (byte)2;
            if (node.Group == Groups.Ships) return 1;
            if (node.Group == Groups.Carts) return node.Data.GetBool(ZDOVars.s_attachJointHash) ? (byte)2 : (byte)1;
            if (node.Group == Groups.TamedAnimals) return ActorState(node.Data);
            if (node.Group == Groups.LooseItems) return node.Data.GetBool(ZDOVars.s_piece) ? (byte)2 : (byte)1;
            return node.Data.GetInt(ZDOVars.s_hooked) != 0 ? (byte)2 : (byte)1;
        }
        private bool UsesGrid(Node node)
        {
            if (node.Group == Groups.None || node.Released || node.Pinned || node.ControlPinned || Catalog.LocalOnly(node.Group)) return false;
            if (node.Group == Groups.StaticBuildings) return options.StaticGrid;
            return !Catalog.Mobile(node.Group) || node.ActorState == 1;
        }
        private void Assign(Node node, long target)
        {
            assignCalls++;
            if (node.Ship && target == server && node.Data.GetOwner() != server) ShipOwnership.Prepare(node.Ship);
            node.Writing = true;
            try { node.Data.SetOwner(target); }
            finally { node.Writing = false; node.LastOwner = node.Data.GetOwner(); }
            if (node.Group == Groups.Fish && target != 0 && target != server) { node.Pinned = true; changed.Add(node); }
        }
        internal void OwnerAssigned(ZDO data, long owner)
        {
            Node node; if (!nodes.TryGetValue(data, out node) || node.Writing || node.LastOwner == owner) return;
            node.LastOwner = owner; node.Released = owner == 0;
            MobOwnerChanged(node);
            if (node.Ship && owner == server) ShipOwnership.Prepare(node.Ship);
            if (node.Group == Groups.Carts) CartAttached(node, owner);
            if (node.Group == Groups.Fish && owner != 0 && owner != server) node.Pinned = true;
            if (!Catalog.LocalOnly(node.Group)) changed.Add(node);
        }
        internal void PositionChanged(ZDO data)
        {
            Node node; if (mobile.Count == 0 || !mobile.TryGetValue(data, out node) || node.Pinned || node.Released || node.ActorState != 1) return;
            MobPoseChanged(node);
            Cell cell; Vector3 position = GridPosition(node);
            if (Cell.At(position.x, position.z, options.CellSize, out cell) && !node.Cell.Equals(cell)) changed.Add(node);
        }
        internal void DataReceived(ZDO data, long source)
        {
            SourceData(data, source);
            RefillChanged(data);
            StateChanged(data);
            Node node; if (mobs.Count != 0 && mobs.TryGetValue(data, out node)) MobPoseChanged(node);
        }
        internal void StateChanged(ZDO data)
        {
            Node node; if (mobile.Count == 0 || !mobile.TryGetValue(data, out node)) return;
            Groups group = CreatureGroup(data, node.PrefabGroups);
            if (group != node.Group) { CancelMob(node); sharedMobs.Remove(node); node.Group = group; changed.Add(node); }
            if (node.Mob != null)
            {
                bool combat = data.GetBool(ZDOVars.s_alert) || data.GetBool(ZDOVars.s_haveTargetHash);
                if (combat != node.Mob.Combat) { node.Mob.Combat = combat; WakeMob(node); }
            }
            if (node.Group == Groups.Ships) { PositionChanged(data); return; }
            if (node.Group == Groups.Carts && node.NativeUseRequests != null && node.NativeUseRequests.Count != 0) CartAttached(node, data.GetOwner());
            if (node.ControlPinned && node.Group == Groups.TamedAnimals)
            {
                if (data.GetLong(ZDOVars.s_user, 0) != 0) node.RiderSeen = true;
                else if (node.RiderSeen)
                {
                    node.RiderSeen = false; node.AcceptedRider = 0;
                    node.ControlPinned = node.ControlRequests != null && node.ControlRequests.Count != 0;
                    changed.Add(node);
                }
            }
            byte state = State(node); if (state == node.ActorState) return;
            node.ActorState = state;
            if (node.Group == Groups.Fish && state != 1) node.Pinned = true;
            changed.Add(node);
        }
        // Native ItemDrop pickup may finish locally without a server request or
        // completion flag. Keep a nearby client owner, but not an unloaded-world
        // lifetime pin. Active footprints already cover native pickup range.
        private bool ItemPickupProtected(Node node, long owner)
        {
            return (owner != 0 && owner != server && world.Grid.Covers(owner, node.Cell)) ||
                (node.NativeUseRequests != null && node.NativeUseRequests.Count != 0);
        }
        private readonly List<long> expiredUseRequests = new List<long>();
        private readonly List<KeyValuePair<long, float>> boundUseRequests = new List<KeyValuePair<long, float>>();
        private void PruneUseRequests(Node node, bool moved = false)
        {
            if (node.NativeUseRequests == null || node.NativeUseRequests.Count == 0) return;
            expiredUseRequests.Clear(); boundUseRequests.Clear();
            foreach (var request in node.NativeUseRequests)
            {
                float since;
                // Use the grid's existing contribution start, not a new clock
                // or cache. Leaving and re-entering must not revive an old
                // failed pickup/hitch, even if no handoff ran while the cell was empty.
                if (!world.Grid.CoverageStart(request.Key, node.Cell, out since) ||
                    (!moved && !Single.IsNaN(request.Value) && request.Value != since)) expiredUseRequests.Add(request.Key);
                else if (moved || Single.IsNaN(request.Value)) boundUseRequests.Add(new KeyValuePair<long, float>(request.Key, since));
            }
            foreach (long uid in expiredUseRequests) node.NativeUseRequests.Remove(uid);
            foreach (var request in boundUseRequests) node.NativeUseRequests[request.Key] = request.Value;
            expiredUseRequests.Clear(); boundUseRequests.Clear();
        }
        internal void PinPickup(ZDO data, long requester = 0)
        {
            Node node; if (data == null || !nodes.TryGetValue(data, out node) || (!Catalog.Pickup(node.Group) && node.Group != Groups.Carts)) return;
            if ((node.Group == Groups.LooseItems || node.Group == Groups.Carts) && requester != 0)
            {
                if (node.NativeUseRequests == null) node.NativeUseRequests = new Dictionary<long, float>();
                PruneUseRequests(node);
                float since;
                node.NativeUseRequests[requester] = world.Grid.CoverageStart(requester, node.Cell, out since) ? since : Single.NaN;
                grace.Remove(node); Unwait(node);
                // Keep idle items/carts indexed so departure, disconnect and
                // movement can release failed requests without a timer or scan.
                if (node.InGrid) { world.Dismiss(node); Wait(node); }
                return;
            }
            node.Pinned = true; grace.Remove(node); Unwait(node);
            if (node.InGrid) { world.Remove(node); node.InGrid = false; }
        }
        internal void PinControl(ZDO data, long requester = 0)
        {
            Node node; if (data == null || !mobile.TryGetValue(data, out node) || node.Group != Groups.TamedAnimals) return;
            if (node.ControlRequests == null) node.ControlRequests = new HashSet<long>();
            node.ControlRequests.Add(requester);
            node.ControlPinned = true; node.RiderSeen |= data.GetLong(ZDOVars.s_user, 0) != 0;
            grace.Remove(node); Unwait(node);
            if (node.InGrid) { world.Remove(node); node.InGrid = false; }
        }
        internal void ControlReply(ZDO data, bool allowed, long recipient = 0)
        {
            Node node; if (data == null || !mobile.TryGetValue(data, out node) || !node.ControlPinned || node.ControlRequests == null || !node.ControlRequests.Remove(recipient)) return;
            if (allowed) node.AcceptedRider = recipient;
            if (!allowed && node.AcceptedRider == 0 && !node.RiderSeen && node.ControlRequests.Count == 0)
            { node.ControlPinned = false; changed.Add(node); }
        }
        internal bool ExpectsControlReply(ZDO data, long recipient)
        {
            Node node;
            return data != null && mobile.TryGetValue(data, out node) && node.ControlPinned &&
                node.ControlRequests != null && node.ControlRequests.Contains(recipient);
        }
        private void Refresh(Node node, float now)
        {
            if (node.Removed && !nodes.ContainsKey(node.Data)) return;
            bool useGrid = UsesGrid(node); Cell cell; Vector3 position = GridPosition(node);
            if (node.Group == Groups.Fish) { Fish fish = node.View.GetComponent<Fish>(); if (fish && fish.IsHooked()) { node.Pinned = true; useGrid = false; } }
            if (node.Group == Groups.LooseItems) { ItemDrop item = node.View.GetComponent<ItemDrop>(); if (item && item.IsPiece()) useGrid = false; }
            if (!Cell.At(position.x, position.z, options.CellSize, out cell)) useGrid = false;
            if (!useGrid)
            {
                CancelMob(node); sharedMobs.Remove(node); mobDirty.Remove(node);
                if (node.InGrid) { world.Remove(node); node.InGrid = false; }
                grace.Remove(node); Unwait(node);
                if (node.Group == Groups.StaticBuildings && !node.Released) { node.Removed = false; defaults.Add(node); }
                return;
            }
            if (!node.InGrid)
            {
                bool movedCart = node.Group == Groups.Carts && !node.Cell.Equals(cell);
                node.Cell = cell; node.InGrid = true; world.Add(node, now);
                if (movedCart) PruneUseRequests(node, true);
            }
            else if (!node.Cell.Equals(cell))
            {
                grace.Remove(node); Unwait(node);
                if (node.Group == Groups.LooseItems || node.Group == Groups.Carts) PruneUseRequests(node);
                world.Move(node, cell, now);
                if (node.Group == Groups.LooseItems || node.Group == Groups.Carts) PruneUseRequests(node, true);
            }
            else { grace.Remove(node); world.Received(node, now); }
        }
        private void RefreshChanges(float now)
        {
            if (changed.Count == 0) return;
            changedWork.Clear(); changedWork.AddRange(changed); changed.Clear();
            foreach (Node node in changedWork) Refresh(node, now);
            changedWork.Clear();
        }
        private void ServiceDefaults(float now)
        {
            if (peers.Count == 0 || defaults.Count == 0) return;
            defaultWork.Clear(); defaultWork.AddRange(defaults); defaults.Clear();
            foreach (Node node in defaultWork) if (!node.Removed && !node.Released) Apply(node, server);
            defaultWork.Clear();
        }
        private long Desired(Node node)
        { return node.Group == Groups.StaticBuildings && !options.StaticGrid ? server : world.Grid.Owner(node.Cell); }
    }
}
