using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags.PlayerSimulation
{
    internal sealed class AuthorityController
    {
        private readonly Settings settings;
        private readonly OwnershipCadence cadence;
        private readonly TooManyLags.CameraWake cameras = new TooManyLags.CameraWake();
        private readonly ObjectCatalog catalog;
        private readonly AuthorityWorld index;
        private readonly RegionContentionGate regions = new RegionContentionGate();
        private readonly List<GridPeer> gridPeers = new List<GridPeer>(12);
        private readonly List<RegionPeer> regionPeers = new List<RegionPeer>(12);
        internal readonly List<PlayerPeer> Players = new List<PlayerPeer>(12);
        private readonly Dictionary<ZDOID, Node> nodes = new Dictionary<ZDOID, Node>();
        private readonly Dictionary<Cell, HashSet<Node>> sectors = new Dictionary<Cell, HashSet<Node>>();
        private readonly HashSet<Cell> interest = new HashSet<Cell>();
        private readonly List<Cell> retiring = new List<Cell>();
        private readonly List<ZDO> scan = new List<ZDO>();
        private readonly HashSet<ZoneSystem.SectorIndex> visited = new HashSet<ZoneSystem.SectorIndex>();
        private readonly Action<ZDOMan, Vector2s, List<ZDO>, HashSet<ZoneSystem.SectorIndex>> find;
        internal readonly Handoffs Handoffs;
        internal readonly MobPolicy Mobs;
        private int ranks;
        private int mobPeers;
        private bool failed;
        private bool interestDirty;
        internal AuthorityController(Settings settings)
        {
            this.settings = settings; cadence = new OwnershipCadence(settings.Interval); catalog = new ObjectCatalog(settings.Categories);
            // Zero is already the native no-owner value, never a real player.
            index = new AuthorityWorld(0, settings.Full, settings.Pattern, settings.Keep, settings.Directional, settings.Inner, settings.Outer,
                settings.CellSize == 4, settings.Forward, Plugin.Network.Latency, settings.Margin);
            find = AccessTools.MethodDelegate<Action<ZDOMan, Vector2s, List<ZDO>, HashSet<ZoneSystem.SectorIndex>>>(AccessTools.Method(typeof(ZDOMan), "FindObjects"));
            Handoffs = new Handoffs(this); Mobs = new MobPolicy(this); index.AfterGridUpdate = SyncInterest;
        }
        internal bool Active { get { return !failed && ZNet.instance && ZNet.instance.IsServer() && ZDOMan.instance != null; } }
        internal void ConfigureInterval(float interval) { cadence.Configure(interval); }
        internal void ConfigureDelays(float full, float inner, float forward, float outer)
        { index.Grid.ConfigureDelays(full, inner, forward, outer); }
        internal bool Shared(long uid) { return Active && regions.Shared(uid); }
        internal bool WatchMobs(long uid) { return mobPeers > 1 && Shared(uid); }
        internal bool NativeTransferSafe(ZDO data) { return catalog.NativeTransferSafe(data.GetPrefab()); }
        internal bool Available(long uid, Vector3 point)
        {
            if (!Active || uid == 0 || !Plugin.Network.Present(uid) || !Finite(point)) return false;
            foreach (PlayerPeer peer in Players) if (peer.Uid == uid && peer.Character != null && peer.Character.IsValid() &&
                peer.Character.GetFloat(ZDOVars.s_health, 1f) > 0 && Finite(peer.Reference) && ZNetScene.InActiveArea(point, peer.Reference)) return true;
            return false;
        }
        internal void Poll(float now)
        {
            if (!Active || !cadence.Due(now) || !ZoneSystem.instance || !ZNetScene.instance) return;
            try
            {
                Plugin.Network.Players(Players); Players.Sort(delegate(PlayerPeer a, PlayerPeer b) { return a.Uid.CompareTo(b.Uid); });
                regionPeers.Clear(); gridPeers.Clear(); cameras.Clear(); mobPeers = 0;
                foreach (PlayerPeer peer in Players)
                {
                    if (settings.Mobs && Plugin.Network.MobCapable(peer.Uid)) mobPeers++;
                    Vector3 point = peer.Character.GetPosition(); Vector2s zone = ZoneSystem.GetZone(peer.Reference), characterZone = ZoneSystem.GetZone(point);
                    regionPeers.Add(new RegionPeer(peer.Uid, new Cell(zone.x, zone.y), Finite(peer.Reference), new Cell(characterZone.x, characterZone.y), Finite(point)));
                }
                if (regions.Update(regionPeers)) { interestDirty = true; Mobs.RanksChanged(); }
                foreach (PlayerPeer peer in Players)
                {
                    if (!regions.Shared(peer.Uid)) continue;
                    Cell cell; Vector3 point = peer.Character.GetPosition();
                    if (Cell.At(point.x, point.z, settings.CellSize, out cell)) gridPeers.Add(GridPeer.Covered(peer.Uid, cell, settings.Directional ? CameraHeading.Read(peer.Character, peer.Uid, Plugin.Network.Capable(peer.Uid), cameras) : 0));
                }
                if (ranks != Plugin.Network.RankVersion) { ranks = Plugin.Network.RankVersion; index.Grid.RanksChanged(); Mobs.RanksChanged(); }
                index.Tick(gridPeers, now);
                Mobs.Tick(now); Handoffs.Tick(now);
                RequestRouter.Flush(now);
                if (Plugin.Local != null && !ZNet.instance.IsDedicated()) Plugin.Local.SetMobWatch(settings.Mobs && WatchMobs(ZDOMan.GetSessionID()));
                Plugin.Network.FlushAll();
            }
            catch (Exception error) { Stop(); Plugin.Fault(error); }
        }
        private void SyncInterest()
        {
            if (!interestDirty) return; interestDirty = false;
            retiring.Clear(); foreach (Cell cell in interest) if (!regions.ZoneKeys.Contains(cell)) retiring.Add(cell);
            foreach (Cell cell in retiring)
            {
                interest.Remove(cell); HashSet<Node> bucket;
                if (sectors.TryGetValue(cell, out bucket)) { var copy = new List<Node>(bucket); foreach (Node node in copy) Remove(node); }
            }
            foreach (Cell cell in regions.ZoneKeys) if (interest.Add(cell))
            {
                scan.Clear(); visited.Clear(); find(ZDOMan.instance, new Vector2s((short)cell.X, (short)cell.Z), scan, visited);
                foreach (ZDO data in scan) Changed(data);
            }
            scan.Clear(); visited.Clear(); retiring.Clear();
        }
        internal void CapabilityChanged(long uid)
        {
            // A newly modded owner enables previously excluded local-state
            // objects. Revisit loaded native sector buckets once, never per tick.
            if (!Active || !ZNetScene.instance || !ZoneSystem.instance) return;
            foreach (Cell cell in interest)
            {
                scan.Clear(); visited.Clear(); find(ZDOMan.instance, new Vector2s((short)cell.X, (short)cell.Z), scan, visited);
                foreach (ZDO data in scan) if (data.GetOwner() == uid) Changed(data);
            }
            scan.Clear(); visited.Clear();
            foreach (Node node in nodes.Values)
                if (node.Data.GetOwner() == uid || index.Grid.Owner(node.Cell) == uid) index.Received(node, Time.unscaledTime);
        }
        internal void Changed(ZDO data)
        {
            if (!Active || data == null || !data.IsValid()) return;
            if (cameras.Changed(data)) cadence.Request();
            Handoffs.Observe(data); Mobs.Changed(data);
            Node node; Vector2s zone = data.GetSector(); Cell sector = new Cell(zone.x, zone.y);
            if (!interest.Contains(sector)) { if (nodes.TryGetValue(data.m_uid, out node)) Remove(node); return; }
            Kinds kind = catalog.Prefab(data.GetPrefab());
            if (kind == Kinds.None || kind == Kinds.Incinerators || (kind == Kinds.TamedAnimals && (!data.GetBool(ZDOVars.s_tamed) || MobPolicy.FriendlyPrefab(data.GetPrefab()))))
            { if (nodes.TryGetValue(data.m_uid, out node)) Remove(node); return; }
            if (!Plugin.Network.Capable(data.GetOwner()) && !catalog.NativeTransferSafe(data.GetPrefab()))
            { if (nodes.TryGetValue(data.m_uid, out node)) Remove(node); return; }
            Cell cell; Vector3 point = data.GetPosition(); if (!Finite(point) || !Cell.At(point.x, point.z, settings.CellSize, out cell)) return;
            if (!nodes.TryGetValue(data.m_uid, out node))
            {
                node = new Node(this, data, kind, cell, sector); nodes.Add(data.m_uid, node); AddSector(node);
                index.Add(node, Time.unscaledTime); return;
            }
            if (!ReferenceEquals(node.Data, data) || node.Kind != kind) { Remove(node); Changed(data); return; }
            if (!node.Sector.Equals(sector)) { RemoveSector(node); node.Sector = sector; AddSector(node); }
            if (!node.Cell.Equals(cell)) index.Move(node, cell, Time.unscaledTime);
            long owner = data.GetOwner(); ushort revision = data.OwnerRevision;
            if (owner != node.SeenOwner || revision != node.SeenRevision)
            { node.SeenOwner = owner; node.SeenRevision = revision; index.Received(node, Time.unscaledTime); }
            bool used = data.GetLong(ZDOVars.s_user, 0) != 0 || data.GetBool(ZDOVars.s_attachJointHash) || (kind == Kinds.TamedAnimals && !String.IsNullOrEmpty(data.GetString(ZDOVars.s_follow)));
            if (used != node.SeenUse) { node.SeenUse = used; index.Received(node, Time.unscaledTime); }
        }
        internal void Destroyed(ZDOID id)
        { Node node; if (nodes.TryGetValue(id, out node)) Remove(node); Handoffs.Forget(id); Mobs.Forget(id); }
        private void AddSector(Node node)
        { HashSet<Node> bucket; if (!sectors.TryGetValue(node.Sector, out bucket)) { bucket = new HashSet<Node>(); sectors.Add(node.Sector, bucket); } bucket.Add(node); }
        private void RemoveSector(Node node)
        { HashSet<Node> bucket; if (sectors.TryGetValue(node.Sector, out bucket)) { bucket.Remove(node); if (bucket.Count == 0) sectors.Remove(node.Sector); } }
        private void Remove(Node node) { index.Remove(node); nodes.Remove(node.Data.m_uid); RemoveSector(node); Handoffs.Cancel(node.Data.m_uid); }
        internal long Desired(ZDO data, Kinds kind, ZDOID target)
        {
            if (!Active || data == null || !data.IsValid()) return 0;
            if (kind == Kinds.None) return Mobs.Desired(data, target);
            Node node; return nodes.TryGetValue(data.m_uid, out node) && ReferenceEquals(node.Data, data) ? index.Grid.Owner(node.Cell) : 0;
        }
        internal bool IsManaged(ZDO data, int method)
        {
            if (!Active || data == null) return false;
            Node node;
            return (nodes.TryGetValue(data.m_uid, out node) && ObjectCatalog.OwnerRequest(node.Kind, method)) || Mobs.Managed(data.m_uid, method);
        }
        internal void Wake(ZDOID id) { Node node; if (nodes.TryGetValue(id, out node)) index.Received(node, Time.unscaledTime); Mobs.Wake(id); }
        internal void Rejected(ZDOID id)
        { Node node; if (nodes.TryGetValue(id, out node)) { node.RejectedOwner = node.Data.GetOwner(); node.RejectedRevision = node.Data.OwnerRevision; } Mobs.Forget(id); }
        internal void Stop()
        { failed = true; Handoffs.Clear(); Mobs.Clear(); nodes.Clear(); sectors.Clear(); interest.Clear(); regions.Clear(); cameras.Clear(); Players.Clear(); gridPeers.Clear(); regionPeers.Clear(); }
        internal static bool Finite(Vector3 value) { return !(Single.IsNaN(value.x) || Single.IsNaN(value.y) || Single.IsNaN(value.z) || Single.IsInfinity(value.x) || Single.IsInfinity(value.y) || Single.IsInfinity(value.z)); }
        private sealed class Node : AuthorityNode
        {
            private readonly AuthorityController owner;
            internal readonly ZDO Data;
            internal readonly Kinds Kind;
            internal Cell Sector;
            internal long SeenOwner;
            internal ushort SeenRevision;
            internal bool SeenUse;
            internal long RejectedOwner;
            internal ushort RejectedRevision;
            internal Node(AuthorityController owner, ZDO data, Kinds kind, Cell cell, Cell sector)
            { this.owner = owner; Data = data; Kind = kind; Cell = cell; Sector = sector; SeenOwner = data.GetOwner(); SeenRevision = data.OwnerRevision; }
            internal override long CurrentOwner { get { return Data.GetOwner(); } }
            internal override ApplyResult Apply(long target)
            {
                if (!Data.IsValid() || !ReferenceEquals(ZDOMan.instance.GetZDO(Data.m_uid), Data)) return ApplyResult.Dropped;
                if (target == 0) return ApplyResult.Done;
                if (Data.OwnerRevision == UInt16.MaxValue || (RejectedOwner == Data.GetOwner() && RejectedRevision == Data.OwnerRevision)) return ApplyResult.Dropped;
                if (!owner.Available(target, Data.GetPosition())) return ApplyResult.Done;
                if (Data.GetOwner() == target && !owner.Handoffs.Pending(Data.m_uid)) { Managed = true; ManagedOwner = target; return ApplyResult.Done; }
                if (!owner.Available(Data.GetOwner(), Data.GetPosition())) return ApplyResult.Done; // Native reassignment owns departures/disconnects.
                if (Data.GetLong(ZDOVars.s_user, 0) != 0 || Data.GetBool(ZDOVars.s_attachJointHash) ||
                    (Kind == Kinds.TamedAnimals && (!Data.GetBool(ZDOVars.s_tamed) || !String.IsNullOrEmpty(Data.GetString(ZDOVars.s_follow))))) return ApplyResult.Done;
                if ((!Plugin.Network.Capable(Data.GetOwner()) || !Plugin.Network.Capable(target)) && !owner.NativeTransferSafe(Data)) return ApplyResult.Done;
                // Queue/transport saturation is temporary. Preserve the node's
                // pending job until the requested handoff can actually start.
                owner.Handoffs.Request(Data, target, Kind, ZDOID.None); return ApplyResult.Pending;
            }
        }
    }
}
