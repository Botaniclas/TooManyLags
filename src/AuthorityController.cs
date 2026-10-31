using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Globalization;
using UnityEngine;

namespace TooManyLags
{
    internal sealed class AuthorityController
    {
        internal static Func<ZNetPeer, bool> LooseItemClient = null;
        internal static Func<ZNetPeer, bool> PhysicsClient = null;
        internal static Func<ZNetPeer, bool> TamedClient = null;
        internal static Func<Vector3, bool> MobServerReady = null;
        internal static Action BeginNetworkBatch, EndNetworkBatch, FlushNetworkBatch;
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
        private readonly bool staticEnabled, staticGrid, defaultCoverage, defaultInteractives, defaultMobs, defaultSpawners, refillStations, keepOnExit;
        private readonly float cellSize;
        private readonly int subdivisions;
        private readonly GridCoveragePattern pattern;
        private readonly bool directional;
        private float innerDelay, outerDelay, forwardDelay, overlapCooldown;
        private StaticBuildings statics;
        private readonly Dictionary<ZDOID, NativeNode> nodes = new Dictionary<ZDOID, NativeNode>();
        private readonly HashSet<NativeNode> loadingObjects = new HashSet<NativeNode>();
        private readonly HandoffSendPriority<ZDO> sendPriority = new HandoffSendPriority<ZDO>();
        private Func<ZDO, int> sendTier;
        private readonly List<NativeNode> retiringInitialObjects = new List<NativeNode>();
        private readonly HashSet<Vector2s> loadingCenters = new HashSet<Vector2s>(), nextLoadingCenters = new HashSet<Vector2s>();
        private readonly Dictionary<Cell, HashSet<NativeNode>> waitingFieldRequests = new Dictionary<Cell, HashSet<NativeNode>>();
        private Predicate<NativeNode> renewFieldRequest;
        private float renewalTime;
        private ConditionalWeakTable<ZNetView, NativeNode> views = new ConditionalWeakTable<ZNetView, NativeNode>();
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
        private bool primed, active, failed, writing, coarseEnabled;
        internal readonly RequestRouter Router;
        internal NativeInventoryHandoff Inventory { get; private set; }
        internal FgnOwnerHandoff Cooperative { get; private set; }
        internal AuthorityController(Kinds kinds, float delay, CompatibilityBridge compatibility,
            Action<Exception> fault, bool staticEnabled, Action<Exception> requestFault, float checkIntervalSeconds,
            bool defaultInteractives, bool refillStations, float cellSize, GridCoveragePattern pattern, bool keepOnExit,
            bool directional = false, float innerDelay = 0f, float outerDelay = 3f, float forwardDelay = .2f, float overlapCooldown = 0f, bool defaultMobs = false, bool staticGrid = false)
        {
            this.kinds = kinds | (staticEnabled || staticGrid ? Kinds.StaticBuildings : Kinds.None); this.delay = delay; cadence = new OwnershipCadence(checkIntervalSeconds);
            this.compatibility = compatibility; this.fault = fault; this.requestFault = requestFault; this.staticEnabled = staticEnabled;
            this.defaultInteractives = defaultInteractives; this.refillStations = refillStations;
            this.defaultMobs = defaultMobs && (kinds & Kinds.TamedAnimals) != 0;
            defaultSpawners = defaultMobs;
            this.staticGrid = staticGrid; defaultCoverage = this.defaultMobs || (staticEnabled && staticGrid);
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
                PhysicsObjects.Enabled = (kinds & Kinds.PhysicsObjects) != 0;
                statics = staticEnabled && !staticGrid ? new StaticBuildings(delegate { return active; }) : null;
                Inventory = new NativeInventoryHandoff(server); Cooperative = new FgnOwnerHandoff(compatibility.Assembly, server);
                Cooperative.Rejected = CooperativeRejected;
            }
            return indexed != null;
        }
        internal void Poll(float now) { if (cadence.Due(now)) Tick(); }
        internal void PrioritizeForced(ZDOMan source, List<ZDO> items, int inserted)
        {
            if (!active || failed || indexed == null || inserted < 2 || !ReferenceEquals(world, source)) return;
            if (sendTier == null) sendTier = SendPriorityOf;
            indexed.ClearPriorityBatch();
            try { sendPriority.Apply(items, inserted, sendTier); }
            finally { if (indexed != null) indexed.ClearPriorityBatch(); }
        }
        private int SendPriorityOf(ZDO data)
        {
            NativeNode node;
            if (data == null || !nodes.TryGetValue(data.m_uid, out node) || node.Removed || !ReferenceEquals(node.Data, data)) return -1;
            return indexed.BatchPriority(node);
        }
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
            Action finishBatch = EndNetworkBatch;
            if (finishBatch != null && BeginNetworkBatch != null) BeginNetworkBatch();
            else finishBatch = null;
            try
            {
                cameras.Clear();
                active = Context() && compatibility.Enabled();
                if (!active) { Router.Flush(); return; }
                indexed.PriorityLaneCompleted = FlushNetworkBatch;
                bool useCoarse = !defaultInteractives && ZoneSystem.instance.m_zoneSize == 64f;
                if (coarseEnabled != useCoarse) { coarseEnabled = useCoarse; regions.Clear(); }
                available.Clear(); defaultAreas.Clear();
                if (statics != null) statics.Begin(network, scene, server, 0, true);
                if (!primed)
                {
                    foreach (KeyValuePair<ZDO, ZNetView> pair in instances(scene)) Register(pair.Key, pair.Value);
                    primed = true;
                }
                peers.Clear(); peerIds.Clear(); regionPeers.Clear(); characters.Clear();
                List<ZNetPeer> nativePeers = network.GetPeers();
                if (coarseEnabled && !defaultCoverage && nativePeers.Count <= 1)
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
                    regions.Update(regionPeers);
                    indexed.Tick(peers, Time.unscaledTime); Router.Flush(); return;
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
                        peers.Add(GridPeer.Covered(peer.m_uid, cell, (!coarseEnabled || defaultCoverage) && directional ? CameraHeading.Read(player, peer.m_uid, cameras) : 0));
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
                    for (int i = 0; !defaultCoverage && i < peers.Count; i++)
                    {
                        GridPeer peer = peers[i];
                        if (!regions.Shared(peer.Uid)) continue;
                        if (directional) peer.Coverage = CameraHeading.Read(characters[i], peer.Uid, cameras);
                        peers[kept++] = peer;
                    }
                    if (!defaultCoverage && kept != peers.Count) peers.RemoveRange(kept, peers.Count - kept);
                    // Do not hold character ZDOs after this sampling pass.
                    characters.Clear();
                }
                PruneInitialObjects(nativePeers);
                peers.Sort(peerOrder); indexed.Tick(peers, Time.unscaledTime); Router.Flush();
            }
            catch (Exception error) { Fail(error); }
            finally { if (finishBatch != null) finishBatch(); }
        }
        internal void Register(ZDO data, ZNetView view)
        {
            try
            {
                if (!Context()) return;
                if (data == null || !view || !view.IsValid() || !ReferenceEquals(view.GetZDO(), data) || data.m_uid.IsNone() || BakedObjects.IsBaked(data)) return;
                NativeNode old;
                if (nodes.TryGetValue(data.m_uid, out old))
                {
                    if (ReferenceEquals(old.Data, data) && ReferenceEquals(old.View, view)) return;
                    if (ReferenceEquals(old.Data, data) && old.AwaitingInstance &&
                        (ObjectCatalog.InitialRoot(view.gameObject, old.Kind) ||
                         (old.Kind == Kinds.LooseItems && view.transform.parent == null &&
                          ObjectCatalog.Classify(view.gameObject) == Kinds.LooseItems && !view.GetComponentInChildren<Container>(true))))
                    {
                        // Keep queued picks, exact recipient state and the field
                        // request when the normal server scene catches up.
                        old.Attach(view); loadingObjects.Remove(old);
                        views.Remove(view); views.Add(view, old);
                        indexed.Received(old, Time.unscaledTime); cadence.Request(); return;
                    }
                    Remove(old);
                }
                Kinds kind = catalog.Instance(data.GetPrefab(), view);
                if (kind == Kinds.None || (kind == Kinds.StaticBuildings && !staticGrid) || (kind == Kinds.TamedAnimals && !data.GetBool(ZDOVars.s_tamed))) return;
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
            try { if (statics != null) statics.Remove(view); NativeNode node; if (views.TryGetValue(view, out node)) Remove(node); }
            catch (Exception error) { Fail(error); }
        }
        private void Remove(NativeNode node)
        {
            if ((node.TextHeld || node.TextOwner != 0) && TextEditNodeRemoved != null) TextEditNodeRemoved(node.Id);
            ForgetFieldWait(node);
            Inventory.Forget(node.Data); Cooperative.Forget(node.Data);
            if (ReadinessRemoved != null) ReadinessRemoved(node.Data);
            indexed.Remove(node); nodes.Remove(node.Id); loadingObjects.Remove(node);
            if (!ReferenceEquals(node.View, null)) views.Remove(node.View);
            Router.Remove(node);
        }
        private void TrackInitialObject(ZDO data)
        {
            if (!(defaultInteractives || staticEnabled) || !active || InitialClient == null || data.GetOwner() == 0 ||
                !data.IsValid() || !data.Persistent || data.Created || data.m_uid.IsNone() ||
                !ReferenceEquals(world.GetZDO(data.m_uid), data) || BakedObjects.IsBaked(data)) return;
            Kinds kind = catalog.NativeDefaultKind(data.GetPrefab(), scene);
            if (kind == Kinds.None || kind == Kinds.WorldMarkers ||
                (kind == Kinds.LooseItems && catalog.NativeInventoryDefault(data.GetPrefab(), scene)) ||
                (kind == Kinds.PhysicsObjects && PhysicsObjects.Body(scene.GetPrefab(data.GetPrefab()))) ||
                (kind == Kinds.StaticBuildings ? !staticEnabled || !staticGrid : !defaultInteractives) || scene.FindInstance(data)) return;
            Vector3 position = data.GetPosition(); Cell cell;
            if (!Finite(position) || !Cell.At(position.x, position.z, cellSize, out cell) || !DefaultArea(position)) return;
            NativeNode node = new NativeNode(this, data, null, kind, cell, null, null);
            nodes.Add(node.Id, node); loadingObjects.Add(node);
            indexed.Add(node, Time.unscaledTime); cadence.Request();
        }
        private void PruneInitialObjects(List<ZNetPeer> nativePeers)
        {
            if (loadingObjects.Count == 0) { loadingCenters.Clear(); return; }
            nextLoadingCenters.Clear();
            foreach (ZNetPeer peer in nativePeers)
                if (peer != null && peer.IsReady() && Finite(peer.GetRefPos())) nextLoadingCenters.Add(ZoneSystem.GetZone(peer.GetRefPos()));
            // Native loaded coverage changes only with a peer's reference zone
            // or membership. Stationary peers never rescan loading objects.
            if (loadingCenters.Count != 0 && loadingCenters.SetEquals(nextLoadingCenters)) return;
            loadingCenters.Clear(); loadingCenters.UnionWith(nextLoadingCenters);
            retiringInitialObjects.Clear();
            foreach (NativeNode node in loadingObjects)
                if (!node.Identity()) retiringInitialObjects.Add(node);
            foreach (NativeNode node in retiringInitialObjects) Remove(node);
            retiringInitialObjects.Clear();
        }
        internal void DataDestroyed(ZDO data)
        {
            if (indexed == null || data == null) return;
            try
            {
                if (TextEditDestroyed != null) TextEditDestroyed(data);
                NativeNode node;
                if (nodes.TryGetValue(data.m_uid, out node) && ReferenceEquals(node.Data, data) && node.AwaitingInstance) Remove(node);
            }
            catch (Exception error) { Fail(error); }
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
                ForgetFieldWait(node);
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
                    if (active && ownershipField && node == null && recipient != 0) TrackInitialObject(data);
                    if (active && ownershipField && catalog.IsLooseDrop(data.GetPrefab()) && recipient != 0 && recipient != server && data.IsValid() && ReferenceEquals(world.GetZDO(data.m_uid), data))
                    {
                        ZNetPeer peer = network.GetPeer(recipient); ZNetView drop = scene.FindInstance(data);
                        if (peer != null && peer.IsReady() && drop && drop.IsValid() && ReferenceEquals(drop.GetZDO(), data) && drop.GetComponent<ItemDrop>()) world.ForceSendZDO(recipient, data.m_uid);
                    }
                    return;
                }
                // Native packets assign owner and revision separately, in
                // either order. Only the complete tuple proves a handoff.
                if (node.TextHeld && TextEditOwnerObserved != null) TextEditOwnerObserved(data);
                if (Inventory.Transitioning(data) || Cooperative.Transitioning(data)) return;
                long recipientOwner = data.GetOwner(); uint revision = data.OwnerRevision;
                if (node.ObservedOwner == recipientOwner && node.Revision == revision) return;
                node.InventoryRetryUntil = 0;
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
                if ((defaultInteractives || defaultMobs || defaultSpawners || staticEnabled) && data.GetOwner() == server && (!known || (node.Identity() && node.Controllable())) &&
                    NativeServerDefault(data) && DefaultArea(data.GetPosition()))
                {
                    // Retaining the same owner performs no SetOwnerInternal
                    // write. Recover a missing initial record here as well,
                    // before waiting for the server scene to instantiate it.
                    if (node == null) TrackInitialObject(data);
                    return false;
                }
                return statics != null && statics.Manage(data) ? false : persistent;
            }
            catch (Exception error) { Fail(error); return persistent; }
        }
        internal bool TransferPending(ZDO data) { return (Inventory != null && Inventory.Pending(data)) || (Cooperative != null && Cooperative.Pending(data)); }
        internal bool SendingInventory { get { return Inventory != null && Inventory.Sending; } }
        internal void InventoryResponse(Container container, long sender, bool granted)
        {
            if (!active || Inventory == null) return;
            try
            {
                if (!Inventory.Response(container, sender, granted)) return;
                ZNetView view = container.m_rootObjectOverride ? container.m_rootObjectOverride : container.GetComponentInParent<ZNetView>(true);
                NativeNode node;
                if (view && views.TryGetValue(view, out node))
                { node.RejectInventoryRequest(); indexed.Dismiss(node); }
            }
            catch (Exception error) { Fail(error); }
        }
        private void WaitForFieldRequest(NativeNode node)
        {
            if (node.WaitingForRequest) return;
            node.WaitingForRequest = true; node.WaitingCell = node.Cell;
            HashSet<NativeNode> waiting;
            if (!waitingFieldRequests.TryGetValue(node.Cell, out waiting))
            { waiting = new HashSet<NativeNode>(); waitingFieldRequests.Add(node.Cell, waiting); }
            waiting.Add(node); Inventory.Forget(node.Data);
            indexed.Changed(node, Time.unscaledTime, false);
            indexed.Grid.Renewed = RenewFieldRequests;
        }
        private void CooperativeRejected(ZDO data)
        {
            NativeNode node;
            if (!active || data == null || !nodes.TryGetValue(data.m_uid, out node) || !ReferenceEquals(node.Data, data)) return;
            node.RejectAutomaticRequest(); indexed.Dismiss(node);
        }
        internal void PickupBlocked(ZDO data, long source, uint revision)
        {
            NativeNode node;
            if (data == null || data.GetOwner() != source || data.OwnerRevision != revision ||
                !nodes.TryGetValue(data.m_uid, out node) || !ReferenceEquals(node.Data, data) || node.Kind != Kinds.LooseItems) return;
            Cooperative.Forget(data); node.RejectAutomaticRequest(); indexed.Dismiss(node);
        }
        internal void PhysicsBlocked(ZDO data, long source, uint revision)
        {
            NativeNode node;
            if (data == null || data.GetOwner() != source || data.OwnerRevision != revision ||
                !nodes.TryGetValue(data.m_uid, out node) || !ReferenceEquals(node.Data, data) || node.Kind != Kinds.PhysicsObjects) return;
            Cooperative.Forget(data); node.RejectAutomaticRequest(); indexed.Dismiss(node);
        }
        private void ForgetFieldWait(NativeNode node)
        {
            if (!node.WaitingForRequest) return;
            node.WaitingForRequest = false;
            HashSet<NativeNode> waiting;
            if (waitingFieldRequests.TryGetValue(node.WaitingCell, out waiting))
            { waiting.Remove(node); if (waiting.Count == 0) waitingFieldRequests.Remove(node.WaitingCell); }
            if (waitingFieldRequests.Count == 0 && indexed != null) indexed.Grid.Renewed = null;
        }
        private void RenewFieldRequests(Cell cell)
        {
            HashSet<NativeNode> waiting;
            if (!waitingFieldRequests.TryGetValue(cell, out waiting)) return;
            // Keep suppressed inventory requests in their existing sparse set.
            // No allocation, scheduled retry or wake occurs when the gate ends.
            renewalTime = Time.unscaledTime;
            if (renewFieldRequest == null) renewFieldRequest = RenewFieldRequest;
            waiting.RemoveWhere(renewFieldRequest);
            if (waiting.Count == 0) waitingFieldRequests.Remove(cell);
            if (waitingFieldRequests.Count == 0) indexed.Grid.Renewed = null;
        }
        private bool RenewFieldRequest(NativeNode node)
        {
            if (!node.Removed && renewalTime < node.InventoryRetryUntil) return false;
            node.InventoryRetryUntil = 0;
            node.WaitingForRequest = false;
            if (!node.Removed)
            {
                if (NeedsPolicy(node)) indexed.RenewRequest(node, renewalTime);
                else indexed.Changed(node, renewalTime, false);
            }
            return true;
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
        internal void InitialMobLoad(ZDO data)
        {
            if (!(defaultInteractives || defaultMobs || defaultSpawners || staticEnabled || PhysicsEnabled) || !active || failed || data == null ||
                !ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(scene, ZNetScene.instance)) return;
            try
            {
                if (!data.IsValid() || !data.Persistent || data.GetOwner() != 0L || data.OwnerRevision == UInt16.MaxValue ||
                    BakedObjects.IsBaked(data) || !Finite(data.GetPosition())) return;
                long physicsTarget = InitialPhysicsTarget(data);
                if (physicsTarget != 0) { data.SetOwner(physicsTarget); return; }
                bool spawner = defaultSpawners && catalog.NativeSpawnerDefault(data.GetPrefab(), scene);
                bool fixedObject = staticEnabled && catalog.NativeDefaultKind(data.GetPrefab(), scene) == Kinds.StaticBuildings;
                if (!spawner && !fixedObject && catalog.NativeTamedDefault(data, scene) && MobServerReady != null && !MobServerReady(data.GetPosition())) return;
                if ((!spawner && !fixedObject && (!(defaultInteractives || defaultMobs) || !catalog.NativeTamedDefault(data, scene))) ||
                    !DefaultArea(data.GetPosition())) return;
                // At CreateObject entry no instance has been initialized. A settled
                // player field keeps precedence; only the server default is seeded.
                if (spawner || NativeOwnerTarget(data, server) == server) data.SetOwner(server);
            }
            catch (Exception error) { Fail(error); }
        }
        // This is only a target selector at vanilla's assignment write. Native
        // active-owner/area tests have already succeeded; no takeover RPC here.
        internal long NativeOwnerTarget(ZDO data, long nativeOwner)
        {
            if (PhysicsEnabled && active && !failed && nativeOwner != 0 && data != null && data.IsValid() && data.Persistent &&
                data.GetOwner() == 0 && data.OwnerRevision != UInt16.MaxValue && !BakedObjects.IsBaked(data) && ReferenceEquals(world, ZDOMan.instance))
            {
                long physicsTarget = InitialPhysicsTarget(data);
                if (physicsTarget != 0) return physicsTarget;
            }
            if (!(defaultInteractives || defaultMobs || defaultSpawners || staticEnabled) || !active || failed || nativeOwner == 0 || !ReferenceEquals(world, ZDOMan.instance) ||
                !data.IsValid() || !data.Persistent || BakedObjects.IsBaked(data) || data.OwnerRevision == UInt16.MaxValue ||
                !NativeServerDefault(data)) return nativeOwner;
            Vector3 position = data.GetPosition(); Cell cell;
            if (!Finite(position) || !Cell.At(position.x, position.z, cellSize, out cell)) return nativeOwner;
            NativeNode node;
            bool known = nodes.TryGetValue(data.m_uid, out node) && ReferenceEquals(node.Data, data);
            if (known && (!node.Identity() || !node.Controllable() || node.UseProtected())) return nativeOwner;
            // Fixed mob spawners follow the mob default, independently of animal
            // categories and player fields. Native active-owner tests and the
            // existing identity/position/use checks remain ahead of this selector.
            if (defaultSpawners && catalog.NativeSpawnerDefault(data.GetPrefab(), scene)) return server;
            // Mobile actors keep their normal guarded handoff path. Only an
            // unowned animal may use this native first-assignment selector.
            if (data.GetOwner() != 0L && catalog.NativeTamedDefault(data, scene)) return nativeOwner;
            if (catalog.NativeTamedDefault(data, scene) && MobServerReady != null && !MobServerReady(position)) return nativeOwner;
            if (refillStations && catalog.NativeRefillDefault(data.GetPrefab(), scene)) return server;
            if (catalog.NativeWorldDefault(data.GetPrefab(), scene)) return server;
            if (!staticGrid && catalog.NativeDefaultKind(data.GetPrefab(), scene) == Kinds.StaticBuildings) return server;
            long candidate = indexed.Grid.Candidate(cell), target = indexed.Grid.Owner(cell);
            if (candidate != 0)
            {
                // A first native assignment uses the configured server default
                // until a field settles. Never give an arbitrary native player
                // this temporary owner and require a loading-gated takeover.
                // This selects the initial owner; it does not settle the timer
                // or steal a still-active native owner.
                if (target == 0 || !AvailableOwner(target, position)) return server;
                if (target != server && catalog.NativeIncineratorDefault(data.GetPrefab(), scene) &&
                    (IncineratorProtection.Client == null || !IncineratorProtection.Client(network.GetPeer(target)))) return server;
                return target;
            }
            return server;
        }
        private long InitialPhysicsTarget(ZDO data)
        {
            if (!PhysicsEnabled || catalog.NativeDefaultKind(data.GetPrefab(), scene) != Kinds.PhysicsObjects || !DefaultArea(data.GetPosition())) return 0;
            Cell cell; Vector3 point = data.GetPosition();
            if (!Finite(point) || !Cell.At(point.x, point.z, cellSize, out cell)) return 0;
            long target = indexed.Grid.Owner(cell);
            if (indexed.Grid.Candidate(cell) != 0 && target != 0 && AvailableOwner(target, point) &&
                (target == server || (PhysicsClient != null && PhysicsClient(network.GetPeer(target))))) return target;
            return defaultInteractives ? server : 0;
        }
        internal NativeNode RequestNode(ZDOID id, int method)
        {
            NativeNode node; return active && !failed && nodes.TryGetValue(id, out node) &&
                ObjectCatalog.OwnerRequest(node.Kind, method) && node.Identity() ? node : null;
        }
        internal bool IsActive { get { return active && !failed; } }
        internal bool StaticGridEnabled { get { return staticGrid; } }
        internal bool PhysicsEnabled { get { return (kinds & Kinds.PhysicsObjects) != 0; } }
        // Explicit admin request only. No polling/counters in ownership hot paths.
        // Inspect one native sector, including data without a server instance.
        private struct OwnerSample
        {
            internal ZDO Data;
            internal int Rank;
            internal float Distance;
        }
        private static void AddOwnerSample(List<OwnerSample> samples, ZDO data, int rank, Vector3 point)
        {
            float distance = (data.GetPosition() - point).sqrMagnitude; int index = 0;
            while (index < samples.Count && (samples[index].Rank < rank || (samples[index].Rank == rank && samples[index].Distance <= distance))) index++;
            if (index >= 8) return;
            if (samples.Count == 8) samples.RemoveAt(7);
            samples.Insert(index, new OwnerSample { Data = data, Rank = rank, Distance = distance });
        }
        internal string OwnerStatus(long requester, string prefabFilter = null)
        {
            var output = new StringBuilder("SERVER owner status: active=" + IsActive + "; defaultInteractives=" + defaultInteractives +
                "; defaultMobs=" + defaultMobs + "; defaultSpawners=" + defaultSpawners + "; keepOnExit=" + keepOnExit + "; permanentRefills=" + refillStations + "; destinationProtocol=" + (InitialClient != null) + "; " + compatibility.State());
            bool filtered = !String.IsNullOrWhiteSpace(prefabFilter);
            if (filtered && prefabFilter.Length > 100) return output.Append("\nPrefab filter must be at most 100 characters.").ToString();
            if (!ReferenceEquals(world, ZDOMan.instance) || !network || !scene || indexed == null || catalog == null)
                return output.Append("\nNo initialized server world.").ToString();
            ZNetPeer peer = network.GetPeer(requester);
            if (peer == null || !peer.IsReady()) return output.Append("\nRequesting player unavailable.").ToString();
            ZDO character = peer.m_characterID.IsNone() ? null : world.GetZDO(peer.m_characterID);
            Vector3 point = character != null && character.IsValid() ? character.GetPosition() : peer.GetRefPos();
            if (!Finite(point)) return output.Append("\nPlayer position unavailable.").ToString();
            Vector2s zone = ZoneSystem.GetZone(point);
            var data = new List<ZDO>(); world.FindSectorObjects(zone, new SimulationDistance(0, 0), data);
            int eligible = 0, serverOwned = 0, playerOwned = 0, unowned = 0, unloaded = 0, liveOnly = 0;
            var samples = new List<OwnerSample>(8);
            foreach (ZDO item in data)
            {
                if (item == null || !item.IsValid() || !item.Persistent) continue;
                bool baked = BakedObjects.IsBaked(item), matches = false;
                if (filtered)
                {
                    GameObject template = scene.GetPrefab(item.GetPrefab());
                    matches = template && template.name.IndexOf(prefabFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (baked && !matches) continue;
                bool initial = catalog.NativeDefault(item.GetPrefab(), scene);
                NativeNode node; bool live = nodes.TryGetValue(item.m_uid, out node) && ReferenceEquals(node.Data, item);
                bool tracked = !baked && (initial || (live && node.Kind != Kinds.TamedAnimals && node.Kind != Kinds.Vehicles));
                if (!tracked && !matches) continue;
                long current = item.GetOwner();
                if (tracked)
                {
                    if (initial) eligible++; else liveOnly++;
                    if (current == server) serverOwned++; else if (current == 0) unowned++; else playerOwned++;
                    if (!live) unloaded++;
                }
                if (filtered && !matches) continue;
                Cell cell; if (!Cell.At(item.GetPosition().x, item.GetPosition().z, cellSize, out cell)) continue;
                long candidate = indexed.Grid.Candidate(cell), target = indexed.Grid.Owner(cell);
                int rank = candidate != 0 && target == requester && current != requester ? 0 :
                    candidate == requester && current != requester ? 1 : !initial ? 2 : 3;
                AddOwnerSample(samples, item, rank, point);
            }
            output.Append("\nzone=(").Append(zone.x).Append(',').Append(zone.y).Append("); server=").Append(server).Append("; player=").Append(requester)
                .Append("; serverAreaReady=").Append(scene.IsAreaReady(point)).Append("; loadedNodes=").Append(nodes.Count - loadingObjects.Count)
                .Append("; loadingObjects=").Append(loadingObjects.Count)
                .Append("\nsector ZDOs=").Append(data.Count).Append("; initialEligible=").Append(eligible).Append("; liveOnly=").Append(liveOnly)
                .Append("; serverOwned=").Append(serverOwned).Append("; playerOwned=").Append(playerOwned).Append("; unowned=").Append(unowned).Append("; withoutNode=").Append(unloaded)
                .Append("; filteredSamples=").Append(filtered).Append("; samples=").Append(samples.Count);
            foreach (OwnerSample sample in samples)
            {
                ZDO item = sample.Data;
                Vector3 p = item.GetPosition(); Cell cell; Cell.At(p.x, p.z, cellSize, out cell);
                GameObject prefab = scene.GetPrefab(item.GetPrefab()); string name = prefab ? prefab.name : item.GetPrefab().ToString();
                if (name.Length > 100) name = name.Substring(0, 100);
                ZNetPeer current = network.GetPeer(item.GetOwner());
                bool ownerActive = current != null && current.IsReady() && ZNetScene.InActiveArea(p, ZoneSystem.GetZone(current.GetRefPos()));
                output.Append("\n").Append(name).Append(" @(").Append(p.x.ToString("0.0", CultureInfo.InvariantCulture)).Append(',').Append(p.z.ToString("0.0", CultureInfo.InvariantCulture))
                    .Append("): owner=").Append(item.GetOwner()).Append("; activePeer=").Append(ownerActive).Append("; initialEligible=").Append(catalog.NativeDefault(item.GetPrefab(), scene))
                    .Append("; grid=").Append(indexed.Grid.Owner(cell)).Append("; candidate=").Append(indexed.Grid.Candidate(cell)).Append("; timer=").Append(indexed.Grid.Remaining(cell, Time.unscaledTime).ToString("0.0", CultureInfo.InvariantCulture));
                NativeNode node; if (nodes.TryGetValue(item.m_uid, out node) && ReferenceEquals(node.Data, item)) output.Append(node.OwnerStatus());
                else
                {
                    ZNetView view = scene.FindInstance(item);
                    output.Append("; no server node; serverInstance=").Append(view && view.IsValid() && ReferenceEquals(view.GetZDO(), item) && view.gameObject.activeInHierarchy);
                }
                Vector2s itemZone = item.GetSector();
                output.Append("; type=").Append(item.Type).Append("; created=").Append(item.Created)
                    .Append("; zoneLoaded=").Append(ZoneSystem.instance.IsZoneLoaded(itemZone))
                    .Append("; typeReady=").Append(ZoneSystem.instance.IsZoneReadyForType(itemZone, item.Type));
                output.Append("; baked=").Append(BakedObjects.IsBaked(item));
            }
            if (output.Length > 5600) { output.Length = 5600; output.Append("\n[truncated]"); }
            return output.ToString();
        }
        internal static Action<ZDO> ReadinessRemoved { get; set; }
        internal static Func<ZDO, long> TextEditTarget = null;
        internal static Func<ZDO, bool> TextEditLocked = null;
        internal static Action<ZDO> TextEditOwnerObserved = null, TextEditDestroyed = null;
        internal static Action<ZDOID> TextEditNodeRemoved = null;
        internal bool ManagesText(ZDO data)
        {
            if (!IsActive || data == null) return false;
            GameObject prefab = scene.GetPrefab(data.GetPrefab());
            if (!prefab) return true; // Missing metadata is not permission to bypass protection.
            Kinds kind = ObjectCatalog.Classify(prefab);
            return (kind == Kinds.Signs || kind == Kinds.Portals) && (kind & kinds) != 0;
        }
        internal bool PrepareTextEdit(ZDO data)
        {
            if (!IsActive || data == null || !data.IsValid()) return false;
            NativeNode node;
            if (!nodes.TryGetValue(data.m_uid, out node))
            {
                ZNetView view = scene.FindInstance(data);
                if (view) Register(data, view);
                if (!nodes.TryGetValue(data.m_uid, out node))
                {
                    Kinds kind = catalog.NativeDefaultKind(data.GetPrefab(), scene); Cell cell;
                    Vector3 point = data.GetPosition();
                    if ((kind != Kinds.Signs && kind != Kinds.Portals) || !data.Persistent ||
                        BakedObjects.IsBaked(data) || !Cell.At(point.x, point.z, cellSize, out cell) || !DefaultArea(point)) return false;
                    node = new NativeNode(this, data, null, kind, cell, null, null);
                    nodes.Add(node.Id, node); loadingObjects.Add(node); indexed.Add(node, Time.unscaledTime);
                }
            }
            if (!ReferenceEquals(node.Data, data) || (node.Kind != Kinds.Signs && node.Kind != Kinds.Portals)) return false;
            node.TextOwner = TextEditTarget == null ? 0 : TextEditTarget(data); node.TextHeld = false;
            ForgetFieldWait(node); indexed.RenewRequest(node, Time.unscaledTime); cadence.Request(); return true;
        }
        internal void EndTextEdit(ZDO data)
        {
            NativeNode node;
            if (data != null && nodes.TryGetValue(data.m_uid, out node) && ReferenceEquals(node.Data, data))
            { node.TextOwner = 0; node.TextHeld = TextEditLocked != null && TextEditLocked(data); node.RejectAutomaticRequest(); indexed.Dismiss(node); }
        }
        internal static Action ReadinessReset { get; set; }
        internal static Func<ZNetPeer, bool> InitialClient { get; set; }
        internal bool InitialObject(ZDO data)
        { NativeNode node; return nodes.TryGetValue(data.m_uid, out node) && ReferenceEquals(node.Data, data) && node.AwaitingInstance; }
        internal Kinds InitialKind(ZDO data)
        { NativeNode node; return nodes.TryGetValue(data.m_uid, out node) && ReferenceEquals(node.Data, data) && node.AwaitingInstance ? node.Kind : Kinds.None; }
        internal Kinds CommitKind(ZDO data)
        { NativeNode node; return nodes.TryGetValue(data.m_uid, out node) && ReferenceEquals(node.Data, data) && ((node.AwaitingInstance && node.Kind != Kinds.LooseItems) || node.InventoryBacked) ? node.Kind : Kinds.None; }
        internal void Wake() { cadence.Request(); }
        internal bool PhysicsTransfer(ZDO data, out bool sleepRequired)
        {
            NativeNode node; sleepRequired = true;
            if (!nodes.TryGetValue(data.m_uid, out node) || !ReferenceEquals(node.Data, data) || !node.PhysicsBody) return false;
            sleepRequired = !ReturnPhysicsOnExit(node); return true;
        }
        private bool ReturnPhysicsOnExit(NativeNode node)
        { return node.Kind == Kinds.PhysicsObjects && node.HadCoverage && !keepOnExit && defaultInteractives && !indexed.Grid.Covered(node.Cell); }
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
                if (node.Data.GetOwner() == uid || indexed.Grid.Owner(node.Cell) == uid)
                { indexed.Changed(node, Time.unscaledTime, NeedsPolicy(node)); }
            cadence.Request();
        }
        private bool NativeServerDefault(ZDO data)
        {
            if (defaultSpawners && catalog.NativeSpawnerDefault(data.GetPrefab(), scene)) return true;
            Kinds kind = catalog.NativeDefaultKind(data.GetPrefab(), scene);
            return kind != Kinds.None ? (kind == Kinds.StaticBuildings ? staticEnabled : defaultInteractives) :
                (defaultInteractives || defaultMobs) && catalog.NativeTamedDefault(data, scene);
        }
        private bool NeedsGrid(NativeNode node) { return node.TextOwner != 0 || (staticEnabled && node.Kind == Kinds.StaticBuildings) || (defaultMobs && node.Kind == Kinds.TamedAnimals) || !coarseEnabled || regions.ContainsZone(node.NativeZone); }
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
            loadingObjects.Clear(); retiringInitialObjects.Clear(); loadingCenters.Clear(); nextLoadingCenters.Clear();
            waitingFieldRequests.Clear(); if (indexed != null) indexed.Grid.Renewed = null;
            available.Clear(); defaultAreas.Clear(); peers.Clear(); peerIds.Clear(); cameras.Clear(); regionPeers.Clear(); characters.Clear(); regions.Clear(); Router.Clear();
            indexed = null; catalog = null; statics = null; scene = null; network = null; world = null;
        }
        private void Fail(Exception error) { failed = true; Reset(); try { fault(error); } catch { } }
        private static bool Finite(Vector3 v) { return !(Single.IsNaN(v.x) || Single.IsNaN(v.y) || Single.IsNaN(v.z) || Single.IsInfinity(v.x) || Single.IsInfinity(v.y) || Single.IsInfinity(v.z)); }
        internal sealed class NativeNode : AuthorityNode
        {
            internal long TextOwner;
            internal bool TextHeld;
            private readonly AuthorityController owner;
            internal readonly ZDO Data;
            internal readonly ZDOID Id;
            internal ZNetView View;
            internal bool AwaitingInstance { get { return ReferenceEquals(View, null); } }
            internal readonly Kinds Kind;
            internal bool WaitingForRequest;
            internal float InventoryRetryUntil;
            internal Cell WaitingCell;
            private WearNTear wear;
            private Character actor;
            private Tameable tameable;
            private bool ship, serverStation, multipleInventories, stationary, hasInventory;
            private Vagon cart;
            private Container container;
            private Incinerator incinerator;
            internal long ObservedOwner;
            internal uint Revision;
            internal GridHandoffs.RecipientRequirement Recipient;
            internal int Requests { get { return owner.Router.PendingRequests(this); } }
            internal Cell NativeZone;
            private double cellLeft, cellRight, cellBottom, cellTop;
            private float refillAt, refillUntil, inventoryAt, inventoryUntil;
            private long refillOwner, inventoryOwner;
            private uint refillRevision;
            private bool coveredBefore;
            internal bool HadCoverage { get { return coveredBefore; } }
            internal bool ServerStation { get { return serverStation; } }
            internal bool InventoryBacked { get { return hasInventory; } }
            internal Rigidbody PhysicsBody;
            internal string OwnerStatus()
            {
                float now = Time.unscaledTime;
                return "; kind=" + Kind + "; identity=" + Identity() + "; controllable=" + Controllable() + "; managed=" + Managed + "; retainedExit=" + (owner.keepOnExit && coveredBefore && owner.indexed.Grid.Candidate(Cell) == 0) +
                    "; inUse=" + (Data.GetInt(ZDOVars.s_inUse) == 1 || (container && container.IsInUse())) +
                    "; refillLease=" + (now < refillUntil) + "; openingLease=" + (now < inventoryUntil) +
                    "; requests=" + Requests + "; awaitRequest=" + WaitingForRequest + "; jobDelay=" + Math.Max(0, NotBefore - now).ToString("0.0", CultureInfo.InvariantCulture) +
                    "; stationary=" + stationary + "; serverStation=" + serverStation + "; awaitingInstance=" + AwaitingInstance;
            }
            internal bool Summoned { get { return Kind == Kinds.TamedAnimals && (ObjectCatalog.IsSummoned(tameable) || FriendlyCreatures.IsFriendly(actor)); } }
            internal NativeNode(AuthorityController owner, ZDO data, ZNetView view, Kinds kind, Cell cell, Tameable animal, Character character)
            {
                this.owner = owner; Data = data; Id = data.m_uid; View = view; Kind = kind; Cell = cell;
                TextOwner = TextEditTarget == null ? 0 : TextEditTarget(data); TextHeld = TextEditLocked != null && TextEditLocked(data);
                CacheCellGeometry(cell);
                coveredBefore = owner.indexed.Grid.Candidate(cell) != 0;
                ObservedOwner = data.GetOwner(); Revision = data.OwnerRevision;
                if (ReferenceEquals(view, null))
                {
                    // An unloaded drop has no server-local pickup operation.
                    // Keep its grid record without treating it as a fixed root;
                    // active player sources retain the ordinary pickup veto.
                    stationary = kind != Kinds.LooseItems; serverStation = owner.catalog.NativeRefillDefault(data.GetPrefab(), owner.scene);
                    hasInventory = owner.catalog.NativeInventoryDefault(data.GetPrefab(), owner.scene); return;
                }
                if (kind == Kinds.TamedAnimals) { actor = character; tameable = animal; }
                Attach(view);
            }
            internal void Attach(ZNetView view)
            {
                View = view;
                wear = view.GetComponent<WearNTear>(); ship = Kind == Kinds.Vehicles && view.GetComponent<Ship>();
                stationary = ObjectCatalog.Stationary(view.gameObject, Kind);
                if (Kind == Kinds.PhysicsObjects) PhysicsBody = PhysicsObjects.Body(view.gameObject);
                if (Kind == Kinds.Vehicles) cart = view.GetComponent<Vagon>();
                if (Kind == Kinds.Incinerators) incinerator = view.GetComponent<Incinerator>();
                foreach (Container inventory in view.GetComponentsInChildren<Container>(true))
                {
                    ZNetView root = inventory.m_rootObjectOverride ? inventory.m_rootObjectOverride : inventory.GetComponentInParent<ZNetView>(true);
                    if (root != view) continue;
                    if (container) multipleInventories = true; else container = inventory;
                }
                serverStation = view.GetComponentInChildren<Smelter>(true) || view.GetComponentInChildren<CookingStation>(true) ||
                    view.GetComponentInChildren<Fireplace>(true) || view.GetComponentInChildren<Fermenter>(true) ||
                    view.GetComponentInChildren<ShieldGenerator>(true) || view.GetComponentInChildren<Turret>(true);
                hasInventory = container;
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
                if (ObjectCatalog.RefillRequest(Kind, method)) { refillOwner = Data.GetOwner(); refillRevision = Data.OwnerRevision; refillAt = Time.unscaledTime; refillUntil = refillAt + 1f; }
                if (method == "RPC_RequestOpen".GetStableHashCode() || method == "RPC_RequestStack".GetStableHashCode() || method == "RPC_RequestTakeAll".GetStableHashCode())
                { inventoryOwner = Data.GetOwner(); inventoryAt = Time.unscaledTime; inventoryUntil = inventoryAt + RequestRouter.InventoryRequestLifetime; }
            }
            internal bool UseProtected()
            {
                if (TextHeld) return true;
                if (Kind == Kinds.LooseItems && View && View.IsOwner() && LooseItemGuard.Busy(View)) return true;
                float now = Time.unscaledTime;
                // No server instance means no server-side interaction has run.
                // Queued requests must not delay the first client acquisition.
                if (AwaitingInstance && Data.GetOwner() == owner.server) return false;
                return (incinerator && View.IsOwner() && IncineratorProtection.Busy(incinerator)) ||
                    (refillOwner != 0 && now >= refillAt && now < refillUntil && Data.GetOwner() == refillOwner && Data.OwnerRevision == refillRevision) ||
                    (hasInventory && ((container && container.IsInUse()) || (now >= inventoryAt && now < inventoryUntil && inventoryOwner != 0) || Data.GetInt(ZDOVars.s_inUse) != 0));
            }
            internal void ObserveUse()
            {
                // Once native open use has arrived, its flag protects the open
                // inventory. Closing it needs no arbitrary remaining lease.
                if (hasInventory && Data.GetInt(ZDOVars.s_inUse) != 0) { inventoryOwner = 0; inventoryUntil = 0; }
            }
            internal bool Identity()
            {
                if (Removed || !Data.IsValid() || Data.m_uid != Id || !ReferenceEquals(owner.world.GetZDO(Id), Data) || BakedObjects.IsBaked(Data)) return false;
                if (AwaitingInstance) return !Data.Created && !owner.scene.FindInstance(Data) && Data.Persistent &&
                    owner.catalog.NativeDefaultKind(Data.GetPrefab(), owner.scene) == Kind && owner.DefaultArea(Data.GetPosition());
                return View && View.IsValid() && ReferenceEquals(View.GetZDO(), Data) && View.gameObject.activeInHierarchy &&
                    (!View.m_distant || Kind == Kinds.StaticBuildings || (Kind == Kinds.PhysicsObjects && stationary)) && View.transform.parent == null;
            }
            internal bool Controllable()
            {
                if (AwaitingInstance) return Data.Persistent && (Data.GetOwner() == owner.server || Data.GetLong(ZDOVars.s_user, 0) == 0);
                if (Kind == Kinds.Incinerators && (!IncineratorProtection.Active || !IncineratorProtection.Supported ||
                    !incinerator || !container || incinerator.m_container != container)) return false;
                if (multipleInventories || (container && container.m_privacy != Container.PrivacySetting.Public)) return false;
                if (Kind != Kinds.TamedAnimals) return View.m_persistent && (Kind == Kinds.LooseItems || !wear || wear.isActiveAndEnabled) && (!ship || !owner.compatibility.ShipsOwnedExternally) &&
                    Data.GetLong(ZDOVars.s_user, 0) == 0 && (!cart || !cart.InUse());
                return !Summoned && actor && !(actor is Player) && actor.isActiveAndEnabled && Data.GetBool(ZDOVars.s_tamed) && Data.GetLong(ZDOVars.s_user, 0) == 0 &&
                    !actor.IsDead() && !actor.IsAttached() && !actor.IsRiding() && (!tameable || !tameable.HaveRider()) &&
                    (!tameable || !tameable.m_commandable || String.IsNullOrEmpty(Data.GetString(ZDOVars.s_follow)));
            }
            private long DesiredOwner(long fieldOwner)
            {
                if (TextOwner != 0) return TextOwner;
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
                if (fieldOwner == 0 && HasServerDefault) return owner.server;
                return fieldOwner;
            }
            private bool HasServerDefault { get { return Kind == Kinds.StaticBuildings ? owner.staticEnabled : owner.defaultInteractives || (owner.defaultMobs && Kind == Kinds.TamedAnimals); } }
            internal void RejectAutomaticRequest()
            { Managed = false; owner.WaitForFieldRequest(this); }
            internal void RejectInventoryRequest()
            { InventoryRetryUntil = Time.unscaledTime + 1f; RejectAutomaticRequest(); }
            internal override ApplyResult Apply(long target)
            {
                if (!Identity() || Summoned) { owner.Remove(this); return ApplyResult.Dropped; }
                if (TextHeld)
                { RejectAutomaticRequest(); return ApplyResult.Done; }
                if (WaitingForRequest && owner.ReturnPhysicsOnExit(this)) owner.ForgetFieldWait(this);
                if (WaitingForRequest) return ApplyResult.Done;
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
                    HasServerDefault && owner.indexed.Grid.Candidate(Cell) != 0 && TextOwner == 0)
                { owner.indexed.WaitForFieldTimer(this, Time.unscaledTime); return ApplyResult.Pending; }
                target = DesiredOwner(target);
                if (target == 0) return ApplyResult.Done;
                Vector3 position = Data.GetPosition(); Cell current;
                if (!Finite(position) || !Cell.At(position.x, position.z, owner.cellSize, out current)) return ApplyResult.Dropped;
                if (!current.Equals(Cell)) { CacheCellGeometry(current); owner.indexed.Move(this, current, Time.unscaledTime); return ApplyResult.Pending; }
                if (!owner.AvailableOwner(target, position))
                { return ApplyResult.Pending; }
                // A capability check replaces an exact-version lock. Connected
                // clients lacking identified commits retain native ownership.
                if (FgnOwnerHandoff.CooperativeClient != null &&
                    ((target != owner.server && !FgnOwnerHandoff.CooperativeClient(owner.network.GetPeer(target))) ||
                    (Data.GetOwner() != 0 && Data.GetOwner() != owner.server && owner.AvailableOwner(Data.GetOwner(), position) &&
                     !FgnOwnerHandoff.CooperativeClient(owner.network.GetPeer(Data.GetOwner())))))
                { Managed = false; return ApplyResult.Done; }
                long old = Data.GetOwner(); uint revision = Data.OwnerRevision;
                if (Kind == Kinds.TamedAnimals && old != target)
                {
                    if ((old != 0 && old != owner.server && owner.AvailableOwner(old, position) &&
                        (TamedClient == null || !TamedClient(owner.network.GetPeer(old)))) ||
                        (target != owner.server && (TamedClient == null || !TamedClient(owner.network.GetPeer(target)))))
                    { Managed = false; return ApplyResult.Done; }
                    if (target == owner.server && MobServerReady != null && !MobServerReady(position)) return ApplyResult.Pending;
                    if (old == owner.server && !TamedMobHandoff.Ready(View)) return ApplyResult.Pending;
                }
                if (Kind == Kinds.PhysicsObjects && PhysicsClient != null &&
                    ((target != owner.server && !PhysicsClient(owner.network.GetPeer(target))) ||
                     (old != 0 && old != owner.server && owner.AvailableOwner(old, position) && !PhysicsClient(owner.network.GetPeer(old)))))
                { Managed = false; return ApplyResult.Done; }
                // Native distant scenery can exist beyond every active area.
                // Leave unowned render-only roots idle; the normal first-owner
                // event wakes their indexed node when simulation reaches them.
                if ((Kind == Kinds.StaticBuildings || (Kind == Kinds.PhysicsObjects && stationary)) && View && View.m_distant && old == 0 && !owner.DefaultArea(position))
                { Managed = false; return ApplyResult.Done; }
                // Older sources/destinations cannot protect native pickup.
                if (Kind == Kinds.LooseItems &&
                    ((old != 0 && old != owner.server && owner.AvailableOwner(old, position) &&
                      (LooseItemClient == null || !LooseItemClient(owner.network.GetPeer(old)))) ||
                     (target != owner.server && (LooseItemClient == null || !LooseItemClient(owner.network.GetPeer(target))))))
                { Managed = false; return ApplyResult.Done; }
                if (AwaitingInstance && old != owner.server && old != 0 && owner.AvailableOwner(old, position) &&
                    (InitialClient == null || !InitialClient(owner.network.GetPeer(old))))
                { Managed = false; return ApplyResult.Done; }
                // Older clients cannot expose the private burning state. They
                // keep native incinerator ownership until guarded support exists.
                if (Kind == Kinds.Incinerators &&
                    ((target != owner.server && (IncineratorProtection.Client == null || !IncineratorProtection.Client(owner.network.GetPeer(target)))) ||
                    (old != 0 && old != owner.server && owner.AvailableOwner(old, position) &&
                     (IncineratorProtection.Client == null || !IncineratorProtection.Client(owner.network.GetPeer(old))))))
                { Managed = false; return ApplyResult.Done; }
                bool transfer = owner.TransferPending(Data);
                if (WaitingForRequest) return ApplyResult.Done;
                if (old == target && !transfer && (!container || !owner.Inventory.Received(Data) || owner.Inventory.Acquire(Data, View, container) == 2))
                { Managed = true; ManagedOwner = target; owner.Inventory.Forget(Data); owner.Cooperative.Forget(Data); return ApplyResult.Done; }
                if (InventoryRetryUntil > 0f)
                {
                    // Position/index changes can queue work without renewing
                    // coverage. They must not bypass the same object's gate.
                    if (Time.unscaledTime < InventoryRetryUntil) { RejectAutomaticRequest(); return ApplyResult.Done; }
                    InventoryRetryUntil = 0;
                }
                if (PhysicsBody && old == owner.server && !owner.ReturnPhysicsOnExit(this) && !PhysicsObjects.Sleeping(View))
                { RejectAutomaticRequest(); return ApplyResult.Done; }
                if (UseProtected() && owner.AvailableOwner(old, position))
                {
                    if (Kind == Kinds.Incinerators || Kind == Kinds.LooseItems) { RejectAutomaticRequest(); return ApplyResult.Done; }
                    // A known busy chest needs a fresh field request, not an
                    // idle poll. Preserve any source transaction which may have
                    // committed already, including a late/partial state import.
                    if (container && !transfer && !owner.Inventory.Reserved(Data) && !owner.Cooperative.Reserved(Data))
                    { RejectInventoryRequest(); return ApplyResult.Done; }
                    return ApplyResult.Pending;
                }
                if (!AwaitingInstance && Kind != Kinds.TamedAnimals && (ObjectCatalog.Classify(View.gameObject) != Kind || (Kind != Kinds.LooseItems && wear && wear.m_staticPosition && (View.transform.position - position).sqrMagnitude > .0025f))) return ApplyResult.Dropped;
                if (!Identity() || !Controllable() || Data.GetOwner() != old || Data.OwnerRevision != revision || Data.GetPosition() != position || !owner.AvailableOwner(target, position)) return ApplyResult.Pending;
                if (revision == UInt16.MaxValue) { Managed = false; return ApplyResult.Dropped; }
                bool completingCooperative = container && (owner.Cooperative.Pending(Data) || owner.Cooperative.Received(Data));
                if (container && !completingCooperative)
                {
                    // RequestOpen commits at the real source; its reply is not a
                    // free-standing permission for a later forced server steal.
                    if (old != 0 && owner.AvailableOwner(old, position))
                    {
                        int acquired = owner.Inventory.Acquire(Data, View, container);
                        if (WaitingForRequest) return ApplyResult.Done;
                        if (acquired != 2) return ApplyResult.Pending;
                        old = Data.GetOwner(); revision = Data.OwnerRevision;
                    }
                    else owner.Inventory.Forget(Data);
                }
                else if (old != owner.server && old != 0 && owner.AvailableOwner(old, position))
                {
                    int receipt = owner.Cooperative.Request(Data, target);
                    if (WaitingForRequest) return ApplyResult.Done;
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
                if (PhysicsBody && old == owner.server) PhysicsObjects.FlushMotion(View);
                if (Kind == Kinds.TamedAnimals && old == owner.server && old != target) TamedMobHandoff.Flush(View);
                if (!Identity() || !Controllable() || Data.GetOwner() != old || Data.OwnerRevision != revision ||
                    Data.GetPosition() != position || (UseProtected() && owner.AvailableOwner(old, position)) ||
                    DesiredOwner(owner.indexed.Grid.Owner(Cell)) != target || !owner.AvailableOwner(target, Data.GetPosition())) return ApplyResult.Pending;
                if (Data.GetOwner() != target)
                {
                    if (Data.OwnerRevision == UInt16.MaxValue) return ApplyResult.Dropped;
                    owner.writing = true;
                    try
                    {
                        if (target != owner.server) owner.world.ForceSendZDO(target, Id);
                        if (Kind == Kinds.TamedAnimals && TamedMobHandoff.FlightTransfer != null) TamedMobHandoff.FlightTransfer(Data, target); else Data.SetOwner(target);
                        owner.RequireRecipient(this);
                    }
                    finally { owner.writing = false; }
                }
                ObservedOwner = Data.GetOwner(); Revision = Data.OwnerRevision; Managed = ObservedOwner == target; ManagedOwner = ObservedOwner;
                if (Managed) { owner.Inventory.Forget(Data); owner.Cooperative.Forget(Data); if (target != owner.server) owner.world.ForceSendZDO(target, Id); }
                return Managed ? ApplyResult.Done : ApplyResult.Pending;
            }
        }
    }
}
