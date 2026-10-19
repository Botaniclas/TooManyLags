using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    internal sealed partial class Controller
    {
        private sealed class Node : AuthorityNode
        {
            internal readonly Controller Controller;
            internal readonly ZDO Data;
            internal readonly ZNetView View;
            internal readonly Groups PrefabGroups;
            internal Groups Group;
            internal MobState Mob;
            internal long GraceSource;
            internal ushort GraceRevision;
            internal long LastOwner;
            internal bool InGrid, Released, Pinned, Writing, ControlPinned, RiderSeen;
            internal long AcceptedRider;
            internal HashSet<long> ControlRequests;
            internal Dictionary<long, float> NativeUseRequests;
            internal byte ActorState;
            internal RefillState Refill;
            internal Container Container;
            internal Ship Ship;
            internal Vagon Cart;
            internal Vector3 ShipAnchor;
            internal Node(Controller owner, ZDO data, ZNetView view, Groups group, Cell cell)
            { Controller = owner; Data = data; View = view; PrefabGroups = group; Group = CreatureGroup(data, group); Cell = cell; }
            internal override long CurrentOwner { get { return Data.GetOwner(); } }
            internal override ApplyResult Apply(long target) { return Controller.Apply(this, target); }
        }
        private readonly Settings options;
        private readonly long server;
        private readonly Catalog catalog;
        private readonly AuthorityWorld world;
        private readonly NativeInventoryHandoff inventory;
        private readonly Dictionary<ZDO, Node> nodes = new Dictionary<ZDO, Node>();
        private readonly Dictionary<Cell, HashSet<Node>> waiting = new Dictionary<Cell, HashSet<Node>>();
        private readonly List<GridPeer> peers = new List<GridPeer>();
        private bool hadPeers;
        private int shipCount;
        internal Controller(Settings options, long server, Func<int, bool> reserved = null)
        {
            this.options = options; this.server = server; catalog = new Catalog(options.Groups, reserved);
            inventory = new NativeInventoryHandoff(server);
            world = new AuthorityWorld(server, options.FullDelay, options.Pattern, options.Keep, options.Directional,
                options.InnerDelay, options.OuterDelay, true, options.ForwardDelay, null, 30f, options.OverlapHold, true, options.IncludeOuter);
            world.Grid.Renewed = Renew;
            world.AfterGridUpdate = RefreshMobCones;
        }
        internal void Tick(float now)
        {
            passes++;
            peers.Clear();
            BeginMobPlayers();
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (!peer.IsReady() || peer.m_uid == 0 || peer.m_uid == server || peer.m_characterID.IsNone()) continue;
                ZDO player = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (player == null || !player.IsValid() || player.GetOwner() != peer.m_uid) continue;
                Vector3 position = player.GetPosition(); Cell cell;
                if (!Cell.At(position.x, position.z, options.CellSize, out cell)) continue;
                bool large = LargeGrid(peer.m_uid, now), fast = false;
                if (options.Directional && (options.Pattern != GridCoveragePattern.Square3x3 || large) && options.ForwardDelay > 0f && options.FastMovementSpeed > 0f)
                {
                    // Use native replicated velocity, not position deltas: packet
                    // gaps and teleports must not look like a sprint.
                    Vector3 velocity = player.GetVec3(ZDOVars.s_velHash, Vector3.zero);
                    float speedSquared = velocity.x * velocity.x + velocity.z * velocity.z;
                    float threshold = options.FastMovementSpeed * (world.Grid.FastForward(peer.m_uid) ? .75f : 1f);
                    fast = !Single.IsNaN(speedSquared) && !Single.IsInfinity(speedSquared) && speedSquared >= threshold * threshold;
                }
                peers.Add(new GridPeer(peer.m_uid, cell, player.GetRotation().eulerAngles.y, -1, fast, large));
                AddMobPlayer(peer.m_uid, player, position);
            }
            if (peers.Count == 0) ClearTransfers();
            if (peers.Count == 0 && !hadPeers && world.Grid.CachedCells == 0) return;
            hadPeers = peers.Count != 0;
            if (mobTestNode != null && peers.Count != 1) StopMobTest("requires exactly one online player");
            RefreshChanges(now);
            world.Tick(peers, now);
            if (options.MobOwnershipTest) TickMobTest(now);
            ServiceDefaults(now);
        }
        internal void Add(ZDO data, ZNetView view)
        {
            ForecastTargetChanged(data, null, true);
            registrationCalls++;
            if (data == null || !data.IsValid() || !view || !view.IsValid() || nodes.ContainsKey(data)) return;
            validRegistrations++;
            Groups group = catalog.Get(data.GetPrefab()); if (group == Groups.None) return;
            Vector3 position = data.GetPosition(); Cell cell;
            if (!Cell.At(position.x, position.z, options.CellSize, out cell)) return;
            Node node = new Node(this, data, view, group, cell) { LastOwner = data.GetOwner() }; nodes.Add(data, node);
            if (group == Groups.Containers || group == Groups.Ships || group == Groups.Carts)
            {
                foreach (Container container in view.GetComponentsInChildren<Container>(true))
                    if (((group == Groups.Ships || group == Groups.Carts) && container.m_rootObjectOverride == view) || container.GetComponentInParent<ZNetView>(true) == view)
                    { node.Container = container; break; }
            }
            if (group == Groups.Carts)
            {
                node.Cart = view.GetComponent<Vagon>();
                if (!node.Cart || !node.Container || node.Cart.m_container != node.Container || node.Container.m_wagon != node.Cart) { nodes.Remove(data); return; }
            }
            if (group == Groups.Ships)
            {
                node.Ship = view.GetComponent<Ship>();
                if (!node.Ship || !node.Ship.m_shipControlls) { nodes.Remove(data); return; }
                node.ShipAnchor = view.transform.InverseTransformPoint(node.Ship.m_shipControlls.transform.position);
                shipCount++;
            }
            if (Catalog.LocalOnly(group)) { if (Catalog.Idle(data) && ActiveArea(position)) Assign(node, server); return; }
            if ((group & Groups.RefillStations) != 0 && options.RefillHold > 0f) node.Refill = new RefillState(data, view);
            if (Catalog.Mobile(group))
            {
                mobile.Add(data, node); node.ActorState = State(node);
                if (group == Groups.Fish && node.LastOwner != 0 && node.LastOwner != server) node.Pinned = true;
            }
            if ((group & Groups.HostileMobs) != 0) AddMob(node);
            Refresh(node, Time.unscaledTime);
        }
        internal void Remove(ZDO data)
        {
            ForecastTargetChanged(data, null, true);
            Node node; if (data == null || !nodes.TryGetValue(data, out node)) return;
            if (node.Group == Groups.Ships) shipCount--;
            ForgetMob(node);
            grace.Remove(node); Unwait(node);
            inventory.Forget(data);
            if (node.InGrid) world.Remove(node);
            node.Removed = true; changed.Remove(node); defaults.Remove(node); mobile.Remove(data);
            nodes.Remove(data);
        }
        internal long NativeDestination(ZDO data, long proposed)
        {
            if (proposed != server || data == null || !data.Persistent || !Catalog.Idle(data)) return proposed;
            Groups group = catalog.Get(data.GetPrefab()); if (group == Groups.None) return proposed;
            if ((group & Groups.HostileMobs) != 0) return proposed; // Live attack gate, including first delegation.
            // Only an unowned chest may use initial grid assignment here.
            // Existing owners need the guarded live-container handoff path.
            if ((group == Groups.Containers || group == Groups.Ships || group == Groups.Carts) && data.GetOwner() != 0) return proposed;
            if (group == Groups.Ships || group == Groups.Carts) return proposed; // Live vehicle and cargo contracts are required.
            if (Catalog.LocalOnly(group) || (group == Groups.StaticBuildings && !options.StaticGrid)) return server;
            Node node; if (nodes.TryGetValue(data, out node) && (node.Pinned || node.Released || node.ControlPinned)) return proposed;
            if (node != null && node.Refill != null && Time.unscaledTime < node.Refill.Until) return proposed;
            if (group == Groups.TamedAnimals && (ActorState(data) != 1 || data.GetFloat(ZDOVars.s_health, 1f) <= 0f)) return proposed;
            if ((group == Groups.Fish && data.GetInt(ZDOVars.s_hooked) != 0) || (group == Groups.LooseItems && data.GetBool(ZDOVars.s_piece))) return proposed;
            // Existing remote pickups stay on their guarded live-node path.
            if (Catalog.Pickup(group) && data.GetOwner() != 0 && data.GetOwner() != server) return proposed;
            Vector3 position = data.GetPosition(); Cell cell;
            if (!Cell.At(position.x, position.z, options.CellSize, out cell)) return proposed;
            long target = world.Grid.Owner(cell);
            return target != 0 && target != server && Available(target, position) ? target : proposed;
        }
        private ApplyResult Apply(Node node, long target)
        { return Apply(node, target, false); }
        private ApplyResult Apply(Node node, long target, bool due)
        {
            Unwait(node);
            if (peers.Count == 0) return ApplyResult.Dropped;
            if (!node.Data.IsValid() || !node.View || !node.View.IsValid() || !ReferenceEquals(node.View.GetZDO(), node.Data)) return ApplyResult.Dropped;
            if (node.Released || node.Pinned || node.ControlPinned || (node.Group == Groups.Fish && node.Data.GetOwner() != 0 && node.Data.GetOwner() != server)) return ApplyResult.Dropped;
            if (node.Group == Groups.TamedAnimals && (ActorState(node.Data) != 1 || node.Data.GetFloat(ZDOVars.s_health, 1f) <= 0f))
            { grace.Remove(node); Wait(node); return ApplyResult.Dropped; }
            if (target == 0 && options.Keep) { grace.Remove(node); CancelMob(node); sharedMobs.Remove(node); return ApplyResult.Done; }
            if (target == 0) target = server;
            long owner = node.Data.GetOwner();
            if (node.Group == Groups.HostileMobs) return ApplyMob(node, target, owner);
            if (node.Group == Groups.Ships) return ApplyShip(node, target, owner);
            if (node.Group == Groups.Carts) return ApplyCart(node, target, owner);
            if (node.Group == Groups.Containers) return ApplyContainer(node, target, owner);
            if (node.Group == Groups.LooseItems) PruneUseRequests(node);
            if (owner == target) { grace.Remove(node); node.Managed = true; node.ManagedOwner = target; return ApplyResult.Done; }
            if (node.Group == Groups.LooseItems && ItemPickupProtected(node, owner))
            { grace.Remove(node); Wait(node); return ApplyResult.Dropped; }
            if (node.Refill != null && Time.unscaledTime < node.Refill.Until && (owner == server || Available(owner, node.Data.GetPosition())))
            { grace.Remove(node); Wait(node); return ApplyResult.Dropped; }
            if (Catalog.Terminal(node.View) || !Catalog.Idle(node.Data) || !Available(target, node.Data.GetPosition()) ||
                (Catalog.Mobile(node.Group) && target == server && !MobileAreaReady(node)) ||
                (target == server && !node.View.gameObject.activeInHierarchy))
            { grace.Remove(node); Wait(node); return ApplyResult.Dropped; }
            if (target == server && owner != 0 && Catalog.NeedsGrace(node.Group) && !due &&
                (node.Group != Groups.LooseItems || Available(owner, node.Data.GetPosition())))
            {
                if (!grace.Contains(node))
                {
                    float wait; if (!Grace(owner, Time.unscaledTime, out wait)) { Wait(node); return ApplyResult.Dropped; }
                    if (wait > 0)
                    {
                        node.GraceSource = owner; node.GraceRevision = node.Data.OwnerRevision;
                        grace.Add(node, Time.unscaledTime + wait); return ApplyResult.Dropped;
                    }
                }
                else return ApplyResult.Dropped;
            }
            grace.Remove(node);
            Assign(node, target);
            if (node.Data.GetOwner() != target) { Wait(node); return ApplyResult.Dropped; }
            node.Managed = true; node.ManagedOwner = target; return ApplyResult.Done;
        }
        internal bool Available(long uid, Vector3 position)
        {
            if (uid == server) return true;
            ZNetPeer peer = ZNet.instance.GetPeer(uid);
            return peer != null && peer.IsReady() && ZNetScene.InActiveArea(position, ZoneSystem.GetZone(peer.GetRefPos()));
        }
        private static bool ActiveArea(Vector3 position)
        {
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                if (peer.IsReady() && ZNetScene.InActiveArea(position, ZoneSystem.GetZone(peer.GetRefPos()))) return true;
            return false;
        }
        internal bool Request(ZDO data, int method)
        { if (data == null || !data.IsValid()) return false; Groups group = CreatureGroup(data, catalog.Get(data.GetPrefab())); return group != Groups.None && Catalog.OwnerRequest(group, method); }
        internal bool GridManagesFire(int prefab)
        { return !options.RespectFireOwnership && (catalog.Get(prefab) & Groups.RefillStations) != 0; }
        internal int TrackedObjects { get { return nodes.Count; } }
        internal int WaitingCells { get { return waiting.Count; } }
        internal int RememberedCells { get { return world.Grid.RememberedCells; } }
        private void Wait(Node node)
        { if (!node.InGrid) return; HashSet<Node> bucket; if (!waiting.TryGetValue(node.Cell, out bucket)) { bucket = new HashSet<Node>(); waiting.Add(node.Cell, bucket); } bucket.Add(node); }
        private void Unwait(Node node)
        { HashSet<Node> bucket; if (waiting.TryGetValue(node.Cell, out bucket) && bucket.Remove(node) && bucket.Count == 0) waiting.Remove(node.Cell); }
        private void Renew(Cell cell)
        { HashSet<Node> bucket; if (!waiting.TryGetValue(cell, out bucket)) return; foreach (Node node in bucket) world.Received(node, Time.unscaledTime); waiting.Remove(cell); }
    }
}
