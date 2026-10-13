using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TooManyLags
{
    internal sealed class AuthorityController
    {
        private struct Receipt { internal int Epoch; internal bool Ready; }
        private struct OwnerCell : IEquatable<OwnerCell>
        {
            internal long Owner; internal Cell Cell; internal Vector2s Center;
            public bool Equals(OwnerCell other) { return Owner == other.Owner && Cell.Equals(other.Cell) && Center.Equals(other.Center); }
            public override bool Equals(object other) { return other is OwnerCell && Equals((OwnerCell)other); }
            public override int GetHashCode() { return Owner.GetHashCode() ^ Cell.GetHashCode() ^ Center.GetHashCode(); }
        }
        private readonly Kinds kinds;
        private float delay;
        private readonly OwnershipCadence cadence;
        private readonly CompatibilityBridge compatibility;
        private readonly Action<Exception> fault, requestFault;
        private readonly bool staticEnabled, defaultInteractives, refillStations, keepOnExit;
        private readonly float cellSize;
        private readonly int subdivisions;
        private readonly GridCoveragePattern pattern;
        private readonly bool directional;
        private float innerDelay, outerDelay, forwardDelay, overlapCooldown;
        private StaticBuildings statics;
        private readonly Dictionary<ZDOID, NativeNode> nodes = new Dictionary<ZDOID, NativeNode>();
        private ConditionalWeakTable<ZNetView, NativeNode> views = new ConditionalWeakTable<ZNetView, NativeNode>();
        private readonly Dictionary<Vector2s, Receipt> readiness = new Dictionary<Vector2s, Receipt>();
        private readonly Dictionary<Vector2s, bool> defaultAreas = new Dictionary<Vector2s, bool>();
        private readonly Dictionary<OwnerCell, bool> available = new Dictionary<OwnerCell, bool>();
        private readonly List<GridPeer> peers = new List<GridPeer>();
        private readonly HashSet<long> peerIds = new HashSet<long>();
        private readonly CameraWake cameras = new CameraWake();
        private readonly RegionContentionGate regions = new RegionContentionGate();
        private readonly List<RegionPeer> regionPeers = new List<RegionPeer>();
        private readonly List<ZDO> characters = new List<ZDO>();
        private static readonly Comparison<GridPeer> peerOrder = delegate(GridPeer a, GridPeer b) { return a.Uid.CompareTo(b.Uid); };
        private readonly Func<ZNetScene, Dictionary<ZDO, ZNetView>> instances;
        private ObjectCatalog catalog;
        private AuthorityWorld indexed;
        private ZNet network;
        private ZDOMan world;
        private ZNetScene scene;
        private long server;
        private int epoch;
        private bool primed, active, failed, writing, coarseEnabled;
        internal readonly RequestRouter Router;
        internal NativeInventoryHandoff Inventory { get; private set; }
        internal FgnOwnerHandoff Cooperative { get; private set; }
        internal AuthorityController(Kinds kinds, float delay, CompatibilityBridge compatibility,
            Action<Exception> fault, bool staticEnabled, Action<Exception> requestFault, float checkIntervalSeconds,
            bool defaultInteractives, bool refillStations, float cellSize, GridCoveragePattern pattern, bool keepOnExit,
            bool directional = false, float innerDelay = 0f, float outerDelay = 3f, float forwardDelay = .2f, float overlapCooldown = 0f)
        {
            this.kinds = kinds; this.delay = delay; cadence = new OwnershipCadence(checkIntervalSeconds);
            this.compatibility = compatibility; this.fault = fault; this.requestFault = requestFault; this.staticEnabled = staticEnabled;
            this.defaultInteractives = defaultInteractives; this.refillStations = refillStations;
            this.keepOnExit = keepOnExit;
            this.cellSize = Cell.Subdivisions(cellSize) != 0 ? cellSize : 8f;
            subdivisions = Cell.Subdivisions(this.cellSize);
            this.directional = directional; this.innerDelay = innerDelay; this.outerDelay = outerDelay; this.forwardDelay = forwardDelay;
            this.overlapCooldown = overlapCooldown;
            this.pattern = pattern == GridCoveragePattern.Rounded5x5 || pattern == GridCoveragePattern.Rounded7x7 ? pattern : GridCoveragePattern.Square3x3;
            if (this.cellSize == 4 && this.pattern == GridCoveragePattern.Square3x3)
                throw new ArgumentException("4-metre cells require Rounded5x5 or Rounded7x7.");
            FieldInfo field = typeof(ZNetScene).GetField("m_instances", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(Dictionary<ZDO, ZNetView>)) throw new NotSupportedException("Native instance registry changed.");
            DynamicMethod read = new DynamicMethod("BotanicInstances", field.FieldType, new[] { typeof(ZNetScene) }, typeof(AuthorityController).Module, true);
            ILGenerator il = read.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
            instances = (Func<ZNetScene, Dictionary<ZDO, ZNetView>>)read.CreateDelegate(typeof(Func<ZNetScene, Dictionary<ZDO, ZNetView>>));
            Router = new RequestRouter(this);
        }
        private bool Context()
        {
            if (failed || !ZNet.instance || !ZNet.instance.IsDedicated() || ZNet.IsSinglePlayer || ZDOMan.instance == null || !ZNetScene.instance || !ZoneSystem.instance) return false;
            if (!ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance) || !ReferenceEquals(scene, ZNetScene.instance))
            {
                Reset(); world = ZDOMan.instance; network = ZNet.instance; scene = ZNetScene.instance; server = ZDOMan.GetSessionID();
                if (server == 0) return false;
                coarseEnabled = !defaultInteractives && ZoneSystem.instance.m_zoneSize == 64f;
                catalog = new ObjectCatalog(kinds); indexed = new AuthorityWorld(server, delay, pattern, keepOnExit, directional, innerDelay, outerDelay, cellSize == 4, forwardDelay, overlapCooldown);
                statics = staticEnabled ? new StaticBuildings(delegate { return active; }) : null;
                Inventory = new NativeInventoryHandoff(server); Cooperative = new FgnOwnerHandoff(compatibility.Assembly, server);
            }
            return indexed != null;
        }
        internal void Poll(float now) { if (cadence.Due(now)) Tick(); }
        internal void ConfigureInterval(float interval) { cadence.Configure(interval); }
        internal void ConfigureOverlapCooldown(float seconds)
        {
            overlapCooldown = seconds;
            if (indexed != null) indexed.Grid.ConfigureOverlapCooldown(seconds);
        }
        internal void ConfigureDelays(float full, float inner, float forward, float outer)
        {
            delay = full; innerDelay = inner; forwardDelay = forward; outerDelay = outer;
            if (indexed != null) indexed.Grid.ConfigureDelays(full, inner, forward, outer);
        }
        internal void Tick()
        {
            try
            {
                cameras.Clear();
                active = Context() && compatibility.Enabled();
                if (!active) { Router.Flush(); return; }
                bool useCoarse = !defaultInteractives && ZoneSystem.instance.m_zoneSize == 64f;
                if (coarseEnabled != useCoarse) { coarseEnabled = useCoarse; regions.Clear(); }
                readiness.Clear(); available.Clear(); defaultAreas.Clear();
                if (statics != null) statics.Begin(network, scene, server, 0, true);
                if (!primed)
                {
                    foreach (KeyValuePair<ZDO, ZNetView> pair in instances(scene)) Register(pair.Key, pair.Value);
                    primed = true;
                }
                peers.Clear(); peerIds.Clear(); regionPeers.Clear(); characters.Clear();
                List<ZNetPeer> nativePeers = network.GetPeers();
                if (coarseEnabled && nativePeers.Count <= 1)
                {
                    // With at most one connection no character lookup, position,
                    // fine-cell conversion or rotation can change contention.
                    // A constant sole-peer token also avoids region rebuilds as
                    // that player travels. A second connection uses full data.
                    if (nativePeers.Count == 1)
                    {
                        ZNetPeer sole = nativePeers[0];
                        if (sole != null && sole.IsReady() && sole.m_uid != 0 && sole.m_uid != server)
                            regionPeers.Add(new RegionPeer(sole.m_uid, default(Cell), true, default(Cell), false));
                    }
                    regions.Update(regionPeers); indexed.Tick(peers, Time.unscaledTime); Router.Flush(); return;
                }
                foreach (ZNetPeer peer in nativePeers)
                {
                    if (peer == null || !peer.IsReady() || peer.m_uid == 0 || peer.m_uid == server || !peerIds.Add(peer.m_uid)) continue;
                    ZDO player = peer.m_characterID.IsNone() ? null : world.GetZDO(peer.m_characterID); Cell cell;
                    Vector3 position = player != null && player.IsValid() ? player.GetPosition() : new Vector3(Single.NaN, 0, Single.NaN);
                    // A reference-area update can precede the character ZDO
                    // after teleporting. An obsolete character outside its own
                    // native loaded area must not contribute an old footprint.
                    bool valid = Cell.At(position.x, position.z, cellSize, out cell) &&
                        ZNetScene.InActiveArea(position, ZoneSystem.GetZone(peer.GetRefPos()));
                    if (valid)
                    {
                        peers.Add(GridPeer.Covered(peer.m_uid, cell, !coarseEnabled && directional ? CameraHeading.Read(player, peer.m_uid, cameras) : 0));
                        if (coarseEnabled) characters.Add(player);
                    }
                    if (coarseEnabled)
                    {
                        Vector3 reference = peer.GetRefPos(); Cell referenceCell;
                        bool referenceValid = Cell.At(reference.x, reference.z, cellSize, out referenceCell);
                        Vector2s refZone = referenceValid ? ZoneSystem.GetZone(reference) : default(Vector2s);
                        Vector2s charZone = valid ? ZoneSystem.GetZone(position) : default(Vector2s);
                        // Include ready peers whose character is still loading:
                        // native ownership already considers their reference.
                        regionPeers.Add(new RegionPeer(peer.m_uid, new Cell(refZone.x, refZone.y), referenceValid, new Cell(charZone.x, charZone.y), valid));
                    }
                }
                if (coarseEnabled)
                {
                    regions.Update(regionPeers);
                    int kept = 0;
                    for (int i = 0; i < peers.Count; i++)
                    {
                        GridPeer peer = peers[i];
                        if (!regions.Shared(peer.Uid)) continue;
                        if (directional) peer.Coverage = CameraHeading.Read(characters[i], peer.Uid, cameras);
                        peers[kept++] = peer;
                    }
                    if (kept != peers.Count) peers.RemoveRange(kept, peers.Count - kept);
                    // Do not hold character ZDOs after this sampling pass.
                    characters.Clear();
                }
                peers.Sort(peerOrder); indexed.Tick(peers, Time.unscaledTime); Router.Flush();
            }
            catch (Exception error) { Fail(error); }
        }
        internal void Register(ZDO data, ZNetView view)
        {
            try
            {
                if (!Context()) return;
                epoch++; if (statics != null) statics.InvalidateReadiness();
                if (data == null || !view || !view.IsValid() || !ReferenceEquals(view.GetZDO(), data) || data.m_uid.IsNone() || BakedObjects.IsBaked(data)) return;
                NativeNode old;
                if (nodes.TryGetValue(data.m_uid, out old))
                {
                    if (ReferenceEquals(old.Data, data) && ReferenceEquals(old.View, view)) return;
                    Remove(old);
                }
                Kinds kind = catalog.Instance(data.GetPrefab(), view);
                if (kind == Kinds.None || (kind == Kinds.TamedAnimals && !data.GetBool(ZDOVars.s_tamed))) return;
                Tameable animal = kind == Kinds.TamedAnimals ? view.GetComponent<Tameable>() : null;
                Character actor = kind == Kinds.TamedAnimals ? view.GetComponent<Character>() : null;
                if (kind == Kinds.TamedAnimals && (ObjectCatalog.IsSummoned(animal) || FriendlyCreatures.IsFriendly(actor))) return;
                Vector3 position = data.GetPosition(); Cell cell;
                if (!Finite(position) || !Cell.At(position.x, position.z, cellSize, out cell)) return;
                NativeNode node = new NativeNode(this, data, view, kind, cell, animal, actor);
                nodes.Add(node.Id, node); views.Remove(view); views.Add(view, node); indexed.Add(node, Time.unscaledTime, NeedsPolicy(node));
            }
            catch (Exception error) { Fail(error); }
        }
        internal void Unregister(ZNetView view)
        {
            // A destroyed Unity native object can compare equal to null while
            // its managed wrapper is still our CWT key and strong node value.
            if (ReferenceEquals(view, null)) return;
            try { epoch++; if (statics != null) { statics.InvalidateReadiness(); statics.Remove(view); } NativeNode node; if (views.TryGetValue(view, out node)) Remove(node); }
            catch (Exception error) { Fail(error); }
        }
        private void Remove(NativeNode node)
        {
            Inventory.Forget(node.Data); Cooperative.Forget(node.Data);
            if (ReadinessRemoved != null) ReadinessRemoved(node.Data);
            indexed.Remove(node); nodes.Remove(node.Id); views.Remove(node.View); Router.Remove(node);
        }
        internal void Position(ZDO data)
        {
            if (failed || indexed == null) return;
            try
            {
                NativeNode node; if (!nodes.TryGetValue(data.m_uid, out node) || !ReferenceEquals(node.Data, data)) return;
                Vector3 pos = data.GetPosition(); if (!Finite(pos)) { Remove(node); return; }
                if (node.ContainsPosition(pos)) return;
                Cell next; if (!Cell.At(pos.x, pos.z, cellSize, out next)) { Remove(node); return; }
                // The destination, not the former cell, decides whether this
                // movement needs policy work. The loaded-object index still
                // follows animals/carts so shared entry can find them cheaply.
                node.CacheCellGeometry(next);
                bool schedule = NeedsGrid(node) || (refillStations && node.ServerStation) || ProtectedTransfer(data);
                indexed.Move(node, next, Time.unscaledTime, schedule);
            }
            catch (Exception error) { Fail(error); }
        }
        internal void DataChanged(ZDO data)
        {
            if (failed || indexed == null) return;
            try
            {
                if (active && ReferenceEquals(world, ZDOMan.instance) && cameras.Changed(data)) cadence.Request();
                NativeNode node;
                if (nodes.TryGetValue(data.m_uid, out node) && ReferenceEquals(node.Data, data))
                {
                    node.ObserveUse();
                    if (node.Kind == Kinds.TamedAnimals && (!data.GetBool(ZDOVars.s_tamed) || node.Summoned)) Remove(node);
                    return;
                }
                if ((kinds & Kinds.TamedAnimals) != 0 && data.GetBool(ZDOVars.s_tamed)) { ZNetView view = scene.FindInstance(data); if (view) Register(data, view); }
            }
            catch (Exception error) { Fail(error); }
        }
        internal void OwnerChanged(ZDO data, bool ownershipField = true)
        {
            if (failed || writing || indexed == null) return;
            try
            {
                NativeNode node;
                if (!nodes.TryGetValue(data.m_uid, out node) || !ReferenceEquals(node.Data, data))
                {
                    long recipient = data.GetOwner();
                    if (active && ownershipField && catalog.IsLooseDrop(data.GetPrefab()) && recipient != 0 && recipient != server && data.IsValid() && ReferenceEquals(world.GetZDO(data.m_uid), data))
                    {
                        ZNetPeer peer = network.GetPeer(recipient); ZNetView drop = scene.FindInstance(data);
                        if (peer != null && peer.IsReady() && drop && drop.IsValid() && ReferenceEquals(drop.GetZDO(), data) && drop.GetComponent<ItemDrop>()) world.ForceSendZDO(recipient, data.m_uid);
                    }
                    return;
                }
                // Native packets assign owner and revision separately, in
                // either order. Only the complete tuple proves a handoff.
                if (Inventory.Transitioning(data) || Cooperative.Transitioning(data)) return;
                long recipientOwner = data.GetOwner(); uint revision = data.OwnerRevision;
                if (node.ObservedOwner == recipientOwner && node.Revision == revision) return;
                node.ObservedOwner = recipientOwner; node.Revision = revision;
                bool receipt = active && (Inventory.Received(data) || Cooperative.Received(data));
                if (receipt) RequireRecipient(node);
                if (active && receipt && node.Identity() && node.Controllable() && AvailableOwner(recipientOwner, data.GetPosition()))
                {
                    node.Managed = true; node.ManagedOwner = recipientOwner;
                    if (recipientOwner != server) world.ForceSendZDO(recipientOwner, data.m_uid);
                    indexed.Received(node, Time.unscaledTime);
                }
                else indexed.Changed(node, Time.unscaledTime, NeedsPolicy(node));
            }
            catch (Exception error) { Fail(error); }
        }
        internal bool Persistent(ZDO data)
        {
            bool persistent = data.Persistent;
            try
            {
                if (!active || failed || !persistent || !ReferenceEquals(world, ZDOMan.instance) || BakedObjects.IsBaked(data)) return persistent;
                NativeNode node; bool known = nodes.TryGetValue(data.m_uid, out node) && ReferenceEquals(node.Data, data);
                if (known && (node.UseProtected() || ProtectedTransfer(data) ||
                    (node.Managed && node.ManagedOwner == data.GetOwner() && (NeedsGrid(node) || (refillStations && node.ServerStation)))) &&
                    node.Identity() && node.Controllable() && AvailableOwner(data.GetOwner(), data.GetPosition()))
                {
                    return false;
                }
                // Keep the established managed-object fast path above. Only
                // defaults not yet managed need the native-zone cache below.
                // Vanilla checks the server UID against its own ref-pos; retain
                // these defaults while a ready peer loads the area instead.
                if (defaultInteractives && data.GetOwner() == server && (!known || (node.Identity() && node.Controllable())) &&
                    catalog.NativeDefault(data.GetPrefab(), scene) && DefaultArea(data.GetPosition())) return false;
                return statics != null && statics.Manage(data) ? false : persistent;
            }
            catch (Exception error) { Fail(error); return persistent; }
        }
        internal bool TransferPending(ZDO data) { return (Inventory != null && Inventory.Pending(data)) || (Cooperative != null && Cooperative.Pending(data)); }
        internal bool SendingInventory { get { return Inventory != null && Inventory.Sending; } }
        internal void InventoryResponse(Container container, long sender, bool granted)
        {
            if (!active || Inventory == null) return;
            try { Inventory.Response(container, sender, granted); } catch (Exception error) { Fail(error); }
        }
        internal bool AvailableOwner(long owner, Vector3 position)
        {
            if (owner == server) return true;
            ZNetPeer peer = network.GetPeer(owner); Cell cell;
            if (peer == null || !peer.IsReady() || !Cell.At(position.x, position.z, cellSize, out cell)) return false;
            OwnerCell key = new OwnerCell { Owner = owner, Cell = cell, Center = ZoneSystem.GetZone(peer.GetRefPos()) };
            bool result; if (available.TryGetValue(key, out result)) return result;
            float x = (float)Cell.Edge(cell.X, subdivisions), z = (float)Cell.Edge(cell.Z, subdivisions);
            float right = (float)Cell.Edge(cell.X + 1, subdivisions), top = (float)Cell.Edge(cell.Z + 1, subdivisions);
            result = ZNetScene.InActiveArea(new Vector3(x, 0, z), key.Center) && ZNetScene.InActiveArea(new Vector3(right, 0, z), key.Center) &&
                ZNetScene.InActiveArea(new Vector3(x, 0, top), key.Center) && ZNetScene.InActiveArea(new Vector3(right, 0, top), key.Center);
            available[key] = result; return result;
        }
        private bool AreaReady(Vector3 position)
        {
            Vector2s zone = ZoneSystem.GetZone(position); Receipt receipt;
            if (readiness.TryGetValue(zone, out receipt) && receipt.Epoch == epoch) return receipt.Ready;
            int before = epoch; bool ready = scene.IsAreaReady(position); if (before != epoch) return false;
            readiness[zone] = new Receipt { Epoch = epoch, Ready = ready }; return ready;
        }
        private bool DefaultArea(Vector3 position)
        {
            if (!Finite(position)) return false;
            Vector2s zone = ZoneSystem.GetZone(position); bool result;
            if (defaultAreas.TryGetValue(zone, out result)) return result;
            result = false;
            foreach (ZNetPeer peer in network.GetPeers())
                if (peer != null && peer.IsReady() && ZNetScene.InActiveArea(position, ZoneSystem.GetZone(peer.GetRefPos()))) { result = true; break; }
            defaultAreas.Add(zone, result); return result;
        }
        // This is only a target selector at vanilla's assignment write. Native
        // active-owner/area tests have already succeeded; no takeover RPC here.
        internal long NativeOwnerTarget(ZDO data, long nativeOwner)
        {
            if (!defaultInteractives || !active || failed || nativeOwner == 0 || !ReferenceEquals(world, ZDOMan.instance) ||
                !data.IsValid() || !data.Persistent || BakedObjects.IsBaked(data) || data.OwnerRevision == UInt16.MaxValue ||
                !catalog.NativeDefault(data.GetPrefab(), scene)) return nativeOwner;
            Vector3 position = data.GetPosition(); Cell cell;
            if (!Finite(position) || !Cell.At(position.x, position.z, cellSize, out cell)) return nativeOwner;
            NativeNode node;
            if (nodes.TryGetValue(data.m_uid, out node) && ReferenceEquals(node.Data, data) && (!node.Identity() || !node.Controllable() || node.UseProtected())) return nativeOwner;
            long candidate = indexed.Grid.Candidate(cell), target = indexed.Grid.Owner(cell);
            if (candidate != 0)
            {
                // Pending fields retain their timer. Settled fields choose their
                // existing owner, even if another native peer's pass found them.
                if (target == 0 || !AvailableOwner(target, position)) return nativeOwner;
                return target;
            }
            return server;
        }
        internal NativeNode RequestNode(ZDOID id, int method)
        {
            NativeNode node; return active && !failed && nodes.TryGetValue(id, out node) && ObjectCatalog.OwnerRequest(node.Kind, method) && node.Identity() ? node : null;
        }
        internal bool IsActive { get { return active && !failed; } }
        internal static Action<ZDO> ReadinessRemoved { get; set; }
        internal static Action ReadinessReset { get; set; }
        internal void RequireRecipient(NativeNode node)
        {
            if (RequestRouter.RecipientReady == null) return;
            long recipient = node.Data.GetOwner();
            if (recipient != 0 && recipient != server) node.Recipient.Include(recipient, node.Data.OwnerRevision, node.Data.DataRevision);
        }
        internal void RecordRecipient(ZDO data, long recipient, uint revision, uint state)
        {
            NativeNode node;
            if (nodes.TryGetValue(data.m_uid, out node) && ReferenceEquals(node.Data, data) && !node.Removed)
            {
                // Delayed source/status replies must not erase a requirement
                // belonging to the newer native owner tuple.
                if (revision >= data.OwnerRevision && revision >= node.Recipient.Revision)
                    node.Recipient.Include(recipient, revision, state);
            }
        }
        internal bool TryRecipientState(ZDO data, long recipient, out uint state)
        {
            NativeNode node; state = 0;
            if (!nodes.TryGetValue(data.m_uid, out node) || !ReferenceEquals(node.Data, data) || node.Removed ||
                !node.Recipient.Matches(recipient, data.OwnerRevision)) return false;
            state = node.Recipient.State; return true;
        }
        internal void CapabilityChanged(long uid)
        {
            if (indexed == null || !active) return;
            foreach (NativeNode node in nodes.Values)
                if (node.Data.GetOwner() == uid || indexed.Grid.Owner(node.Cell) == uid) indexed.Changed(node, Time.unscaledTime, NeedsPolicy(node));
            cadence.Request();
        }
        private bool NeedsGrid(NativeNode node) { return !coarseEnabled || regions.ContainsZone(node.NativeZone); }
        private bool ProtectedTransfer(ZDO data)
        { return Inventory.Transitioning(data) || Cooperative.Transitioning(data) || TransferPending(data) || Cooperative.Reserved(data); }
        private bool NeedsPolicy(NativeNode node)
        { return NeedsGrid(node) || (refillStations && node.ServerStation) || ProtectedTransfer(node.Data); }
        internal RegionContentionGate RegionGate { get { return regions; } }
        internal int ScheduledAttempts { get { return indexed == null ? 0 : indexed.Attempts; } }
        internal int FieldUpdates { get { return indexed == null ? 0 : indexed.Grid.PlayerUpdates; } }
        internal bool DiagnosticsEnabled { get { return false; } }
        internal long Server { get { return server; } }
        internal void RequestFinished(NativeNode node) { if (indexed != null && !node.Removed) indexed.Received(node, Time.unscaledTime); }
        internal void SceneDestroyed(ZNetScene destroyed)
        {
            // A stale scene's delayed callback must not clear a newer world.
            if (ReferenceEquals(scene, destroyed)) Reset();
        }
        internal void RouterFault(Exception error) { Fail(error); }
        internal void RequestFault(Exception error) { try { if (requestFault != null) requestFault(error); } catch { } }
        internal void Reset()
        {
            if (ReadinessReset != null) ReadinessReset();
            active = false; primed = false; coarseEnabled = false;
            if (Inventory != null) Inventory.Clear(); if (Cooperative != null) Cooperative.Clear();
            Inventory = null; Cooperative = null; nodes.Clear(); views = new ConditionalWeakTable<ZNetView, NativeNode>();
            readiness.Clear(); available.Clear(); defaultAreas.Clear(); peers.Clear(); peerIds.Clear(); cameras.Clear(); regionPeers.Clear(); characters.Clear(); regions.Clear(); Router.Clear();
            indexed = null; catalog = null; statics = null; scene = null; network = null; world = null; epoch = 0;
        }
        private void Fail(Exception error) { failed = true; Reset(); try { fault(error); } catch { } }
        private static bool Finite(Vector3 v) { return !(Single.IsNaN(v.x) || Single.IsNaN(v.y) || Single.IsNaN(v.z) || Single.IsInfinity(v.x) || Single.IsInfinity(v.y) || Single.IsInfinity(v.z)); }
        internal sealed class NativeNode : AuthorityNode
        {
            private readonly AuthorityController owner;
            internal readonly ZDO Data;
            internal readonly ZDOID Id;
            internal readonly ZNetView View;
            internal readonly Kinds Kind;
            private readonly WearNTear wear;
            private readonly Character actor;
            private readonly Tameable tameable;
            private readonly bool ship, serverStation, multipleInventories;
            private readonly Vagon cart;
            private readonly Container container;
            internal long ObservedOwner;
            internal uint Revision;
            internal GridHandoffs.RecipientRequirement Recipient;
            internal int Requests;
            internal Cell NativeZone;
            private double cellLeft, cellRight, cellBottom, cellTop;
            private float refillAt, refillUntil, inventoryAt, inventoryUntil;
            private long refillOwner, inventoryOwner;
            private uint refillRevision;
            private bool coveredBefore;
            internal bool ServerStation { get { return serverStation; } }
            internal bool Summoned { get { return Kind == Kinds.TamedAnimals && (ObjectCatalog.IsSummoned(tameable) || FriendlyCreatures.IsFriendly(actor)); } }
            internal NativeNode(AuthorityController owner, ZDO data, ZNetView view, Kinds kind, Cell cell, Tameable animal, Character character)
            {
                this.owner = owner; Data = data; Id = data.m_uid; View = view; Kind = kind; Cell = cell;
                CacheCellGeometry(cell);
                coveredBefore = owner.indexed.Grid.Candidate(cell) != 0;
                wear = view.GetComponent<WearNTear>(); ship = kind == Kinds.Vehicles && view.GetComponent<Ship>();
                if (kind == Kinds.Vehicles) cart = view.GetComponent<Vagon>();
                if (kind == Kinds.TamedAnimals) { actor = character; tameable = animal; }
                foreach (Container inventory in view.GetComponentsInChildren<Container>(true))
                {
                    ZNetView root = inventory.m_rootObjectOverride ? inventory.m_rootObjectOverride : inventory.GetComponentInParent<ZNetView>();
                    if (root != view) continue;
                    if (container) multipleInventories = true; else container = inventory;
                }
                serverStation = view.GetComponentInChildren<Smelter>(true) || view.GetComponentInChildren<CookingStation>(true) ||
                    view.GetComponentInChildren<Fireplace>(true) || view.GetComponentInChildren<Fermenter>(true) ||
                    view.GetComponentInChildren<ShieldGenerator>(true) || view.GetComponentInChildren<Turret>(true) || kind == Kinds.Incinerators;
                ObservedOwner = data.GetOwner(); Revision = data.OwnerRevision;
            }
            internal void CacheCellGeometry(Cell cell)
            {
                NativeZone = Cell.Zone(cell, owner.cellSize);
                cellLeft = Cell.Edge(cell.X, owner.subdivisions); cellRight = Cell.Edge(cell.X + 1, owner.subdivisions);
                cellBottom = Cell.Edge(cell.Z, owner.subdivisions); cellTop = Cell.Edge(cell.Z + 1, owner.subdivisions);
            }
            internal bool ContainsPosition(Vector3 position)
            { return position.x >= cellLeft && position.x < cellRight && position.z >= cellBottom && position.z < cellTop; }
            internal override long CurrentOwner { get { return Data.GetOwner(); } }
            internal override bool CanRemember(long fieldOwner)
            {
                // A configured permanent server station must not prevent its
                // otherwise completed player field from being remembered.
                long expected = owner.refillStations && serverStation ? owner.server : fieldOwner;
                return Managed && ManagedOwner == expected && Data.GetOwner() == expected;
            }
            internal void Refilled(int method)
            {
                if (ObjectCatalog.RefillRequest(Kind, method)) { refillOwner = Data.GetOwner(); refillRevision = Data.OwnerRevision; refillAt = Time.unscaledTime; refillUntil = refillAt + 10f; }
                if (method == "RPC_RequestOpen".GetStableHashCode() || method == "RPC_RequestStack".GetStableHashCode() || method == "RPC_RequestTakeAll".GetStableHashCode())
                { inventoryOwner = Data.GetOwner(); inventoryAt = Time.unscaledTime; inventoryUntil = inventoryAt + RequestRouter.InventoryRequestLifetime; }
            }
            internal bool UseProtected()
            {
                float now = Time.unscaledTime;
                return (refillOwner != 0 && now >= refillAt && now < refillUntil && Data.GetOwner() == refillOwner && Data.OwnerRevision == refillRevision) ||
                    (container && ((now >= inventoryAt && now < inventoryUntil && inventoryOwner != 0) || Data.GetInt(ZDOVars.s_inUse) != 0));
            }
            internal void ObserveUse()
            {
                // Once native open use has arrived, its flag protects the open
                // inventory. Closing it needs no arbitrary remaining lease.
                if (container && Data.GetInt(ZDOVars.s_inUse) != 0) { inventoryOwner = 0; inventoryUntil = 0; }
            }
            internal bool Identity()
            {
                return !Removed && Data.IsValid() && Data.m_uid == Id && ReferenceEquals(owner.world.GetZDO(Id), Data) && View && View.IsValid() &&
                    ReferenceEquals(View.GetZDO(), Data) && View.gameObject.activeInHierarchy && !View.m_distant && View.transform.parent == null && !BakedObjects.IsBaked(Data);
            }
            internal bool Controllable()
            {
                // Native RequestOpen does not veto an incinerator's private
                // burning coroutine. Leave its background ownership native.
                if (Kind == Kinds.Incinerators) return false;
                if (multipleInventories || (container && container.m_privacy != Container.PrivacySetting.Public)) return false;
                if (Kind != Kinds.TamedAnimals) return View.m_persistent && (!wear || wear.isActiveAndEnabled) && (!ship || !owner.compatibility.ShipsOwnedExternally) &&
                    Data.GetLong(ZDOVars.s_user, 0) == 0 && (!cart || !cart.InUse());
                return !Summoned && actor && !(actor is Player) && actor.isActiveAndEnabled && Data.GetBool(ZDOVars.s_tamed) && Data.GetLong(ZDOVars.s_user, 0) == 0 &&
                    !actor.IsDead() && !actor.IsAttached() && !actor.IsRiding() && (!tameable || !tameable.HaveRider()) &&
                    (!tameable || !tameable.m_commandable || String.IsNullOrEmpty(Data.GetString(ZDOVars.s_follow)));
            }
            private long DesiredOwner(long fieldOwner)
            {
                if (owner.refillStations && serverStation) return owner.server;
                if (!owner.NeedsGrid(this)) return 0;
                // Remember coverage only for this loaded instance. An unrelated
                // later event must not turn its vacated field into a return job.
                if (owner.indexed.Grid.Candidate(Cell) == 0)
                {
                    if (owner.keepOnExit && coveredBefore) return 0;
                    // A newly loaded white object must not inherit a retiring
                    // field's old cached grid owner during its exit grace.
                    if (owner.directional && !coveredBefore) fieldOwner = 0;
                }
                if (fieldOwner == 0 && owner.defaultInteractives) return owner.server;
                return fieldOwner;
            }
            internal override ApplyResult Apply(long target)
            {
                if (!Identity() || Summoned) { owner.Remove(this); return ApplyResult.Dropped; }
                if (!(owner.refillStations && serverStation) && !owner.NeedsGrid(this))
                {
                    // Stop policy work, not native in-flight transactions. Never
                    // resubmit a vetoed handoff or force an onward owner write
                    // merely to finish leaving shared coverage. RPC responses
                    // and replication still arrive through their existing hooks.
                    if (owner.Inventory.Transitioning(Data) || owner.Cooperative.Transitioning(Data) || owner.TransferPending(Data)) return ApplyResult.Pending;
                    owner.Inventory.Forget(Data); owner.Cooperative.Forget(Data); Managed = false;
                    return ApplyResult.Done;
                }
                if (!coveredBefore && owner.indexed.Grid.Candidate(Cell) != 0) coveredBefore = true;
                if (!Controllable())
                {
                    Managed = false;
                    if (Kind == Kinds.TamedAnimals && actor && !actor.IsDead() && Data.GetBool(ZDOVars.s_tamed)) return ApplyResult.Pending;
                    if (Kind == Kinds.Vehicles && !multipleInventories && (!ship || !owner.compatibility.ShipsOwnedExternally) && (!container || container.m_privacy == Container.PrivacySetting.Public)) return ApplyResult.Pending;
                    return ApplyResult.Dropped;
                }
                // A newly covered field has not settled yet. Do not send its
                // objects to the fallback server and immediately back to the
                // player two seconds later. Permanent refill ownership wins.
                if (!(owner.refillStations && serverStation) && target == 0 &&
                    owner.defaultInteractives && owner.indexed.Grid.Candidate(Cell) != 0)
                    return ApplyResult.Pending;
                target = DesiredOwner(target);
                if (target == 0) return ApplyResult.Done;
                Vector3 position = Data.GetPosition(); Cell current;
                if (!Finite(position) || !Cell.At(position.x, position.z, owner.cellSize, out current)) return ApplyResult.Dropped;
                if (!current.Equals(Cell)) { CacheCellGeometry(current); owner.indexed.Move(this, current, Time.unscaledTime); return ApplyResult.Pending; }
                if (!owner.AvailableOwner(target, position)) return ApplyResult.Pending;
                // A capability check replaces an exact-version lock. Connected
                // clients lacking identified commits/readiness retain native ownership.
                if (Kind != Kinds.TamedAnimals && FgnOwnerHandoff.CooperativeClient != null &&
                    ((target != owner.server && !FgnOwnerHandoff.CooperativeClient(owner.network.GetPeer(target))) ||
                    (Data.GetOwner() != 0 && Data.GetOwner() != owner.server && owner.AvailableOwner(Data.GetOwner(), position) &&
                     !FgnOwnerHandoff.CooperativeClient(owner.network.GetPeer(Data.GetOwner())))))
                { Managed = false; return ApplyResult.Done; }
                long old = Data.GetOwner(); uint revision = Data.OwnerRevision;
                bool transfer = owner.TransferPending(Data);
                if (old == target && !transfer && (!container || !owner.Inventory.Received(Data) || owner.Inventory.Acquire(Data, View, container) == 2))
                { Managed = true; ManagedOwner = target; owner.Inventory.Forget(Data); owner.Cooperative.Forget(Data); return ApplyResult.Done; }
                if (UseProtected() && owner.AvailableOwner(old, position)) return ApplyResult.Pending;
                if (!owner.AreaReady(position)) return ApplyResult.Pending;
                if (Kind != Kinds.TamedAnimals && (ObjectCatalog.Classify(View.gameObject) != Kind || (wear && wear.m_staticPosition && (View.transform.position - position).sqrMagnitude > .0025f))) return ApplyResult.Dropped;
                if (!Identity() || !Controllable() || Data.GetOwner() != old || Data.OwnerRevision != revision || Data.GetPosition() != position || !owner.AvailableOwner(target, position)) return ApplyResult.Pending;
                if (revision == UInt16.MaxValue) { Managed = false; return ApplyResult.Dropped; }
                if (container)
                {
                    // RequestOpen commits at the real source; its reply is not a
                    // free-standing permission for a later forced server steal.
                    if (old != 0 && owner.AvailableOwner(old, position))
                    {
                        if (owner.Inventory.Acquire(Data, View, container) != 2) return ApplyResult.Pending;
                        old = Data.GetOwner(); revision = Data.OwnerRevision;
                    }
                    else owner.Inventory.Forget(Data);
                }
                else if (Kind != Kinds.TamedAnimals && old != owner.server && old != 0 && owner.AvailableOwner(old, position))
                {
                    int receipt = owner.Cooperative.Request(Data, target);
                    if (receipt == 1) return ApplyResult.Pending;
                    if (receipt == 2)
                    {
                        owner.RequireRecipient(this);
                        Managed = true; ManagedOwner = Data.GetOwner(); ObservedOwner = ManagedOwner; Revision = Data.OwnerRevision;
                        owner.Cooperative.Forget(Data); if (ManagedOwner != owner.server) owner.world.ForceSendZDO(ManagedOwner, Id);
                        return ApplyResult.Done;
                    }
                    if (FgnOwnerHandoff.IdentifiedSend != null) { Managed = false; return ApplyResult.Done; }
                    // No compatible FGN callback: only non-inventory objects use
                    // vanilla assignment. A timeout never returns this fallback.
                }
                if (!Identity() || !Controllable() || Data.GetOwner() != old || Data.OwnerRevision != revision ||
                    DesiredOwner(owner.indexed.Grid.Owner(Cell)) != target || !owner.AvailableOwner(target, Data.GetPosition())) return ApplyResult.Pending;
                if (Data.GetOwner() != target)
                {
                    if (Data.OwnerRevision == UInt16.MaxValue) return ApplyResult.Dropped;
                    owner.writing = true;
                    try { if (target != owner.server) owner.world.ForceSendZDO(target, Id); Data.SetOwner(target); owner.RequireRecipient(this); }
                    finally { owner.writing = false; }
                }
                ObservedOwner = Data.GetOwner(); Revision = Data.OwnerRevision; Managed = ObservedOwner == target; ManagedOwner = ObservedOwner;
                if (Managed) { owner.Inventory.Forget(Data); owner.Cooperative.Forget(Data); if (target != owner.server) owner.world.ForceSendZDO(target, Id); }
                return Managed ? ApplyResult.Done : ApplyResult.Pending;
            }
        }
    }
}
