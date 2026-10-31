#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TooManyLags
{
    // Reliable presentation stream. Server validates the real ownership epoch
    // and relays only within vanilla's active area; it never simulates a mob.
    internal static class MobBridgeObserver
    {
        private const string CapabilityRpc = "TooManyLags_RecordedObserverCapability";
        private const string StreamRpc = "TooManyLags_RecordedObserver";
        private const string ClockRpc = "TooManyLags_MobBridgeClock";
        private const int Protocol = 4, Limit = 256, MaxSamples = 4096;
        private sealed class Capability { internal bool Ready, Registered; internal int Clients; }
        private sealed class Entry
        {
            internal MobBridgeObserverPacket Descriptor;
            internal readonly List<MobBridgeObserverPacket.Sample> Samples = new List<MobBridgeObserverPacket.Sample>();
            internal readonly HashSet<long> Recipients = new HashSet<long>();
            internal uint Sequence;
            internal int Batches, Received;
            internal bool Closed, Started, Matched;
            internal double Until, NextSend, NextCheck;
            internal float End;
            internal Clock LastClock;
            internal ZPackage ClockPacket;
            internal bool ClockRetry;
        }
        private sealed class Clock
        {
            internal long Tick;
            internal float Simulation, Sample, Ai, AttackTime;
            internal bool AttackActive;
            internal Vector3 Position, Velocity;
            internal Quaternion Rotation;
            internal string Attack;
            internal double Received;
        }
        private sealed class Demand
        { internal MobBridgeObserverPacket Epoch; internal float End, Sent = -1f; internal double Until; }
        private static ConditionalWeakTable<ZRpc, Capability> capabilities = new ConditionalWeakTable<ZRpc, Capability>();
        private static readonly Dictionary<ZDOID, Entry> entries = new Dictionary<ZDOID, Entry>();
        private static readonly Dictionary<ZDOID, Demand> demands = new Dictionary<ZDOID, Demand>();
        private static readonly List<ZDOID> expired = new List<ZDOID>();
        private static ZDOMan world;
        private static ZNet network;
        private static bool started;
        internal static bool Active { get { return entries.Count != 0 || demands.Count != 0; } }
        internal static void Start()
        {
            Stop(); started = true; Context();
            MobObserverHandoff.RecordedActive = delegate(ZDO data)
            {
                Entry entry;
                return
#if TML_DEBUG_SYSTEM
                    FgnSoloCombatTest.Vanilla(data) ||
#endif
                    data != null && entries.TryGetValue(data.m_uid, out entry) &&
                    data.OwnerRevision == entry.Descriptor.Revision + 1U && data.GetOwner() == entry.Descriptor.Destination;
            };
        }
        internal static void Stop()
        {
            started = false; entries.Clear(); demands.Clear(); expired.Clear(); world = null; network = null;
            capabilities = new ConditionalWeakTable<ZRpc, Capability>();
            MobObserverHandoff.RecordedActive = null;
        }
        private static void Context()
        {
            if (ReferenceEquals(world, ZDOMan.instance) && ReferenceEquals(network, ZNet.instance)) return;
            entries.Clear(); demands.Clear(); world = ZDOMan.instance; network = ZNet.instance;
        }
        private static bool Capable(ZNetPeer peer)
        { Capability state; return peer != null && peer.IsReady() && peer.m_rpc != null && capabilities.TryGetValue(peer.m_rpc, out state) && state.Ready; }
        private static bool Trusted(ZNetPeer peer, ZRpc rpc)
        {
            return started && network && peer != null && peer.IsReady() && ReferenceEquals(peer.m_rpc, rpc) &&
                (network.IsServer() ? ReferenceEquals(network.GetPeer(peer.m_uid), peer) :
                peer.m_server && ReferenceEquals(network.GetServerPeer(), peer));
        }
        internal static void Connect(ZNetPeer peer)
        {
            if (!started || peer == null || peer.m_rpc == null) return;
            Context(); Capability state = capabilities.GetOrCreateValue(peer.m_rpc);
            if (!state.Registered)
            {
                state.Registered = true;
                peer.m_rpc.Register<int>(CapabilityRpc, delegate(ZRpc rpc, int value)
                {
                    Context(); if (!Trusted(peer, rpc) || value != Protocol && (value > -1 || value < -Limit)) return;
                    Capability remote = capabilities.GetOrCreateValue(rpc); remote.Ready = true;
                    if (network.IsServer()) AnnounceInterest();
                    else
                    {
                        if (value < 0) remote.Clients = -value;
                        if (value == Protocol) rpc.Invoke(CapabilityRpc, -Protocol);
                    }
                });
                peer.m_rpc.Register<ZDOID, ZPackage>(StreamRpc, delegate(ZRpc rpc, ZDOID id, ZPackage packet)
                {
                    try { Receive(peer, rpc, id, packet); }
                    catch (Exception error) { Plugin.Warn("Observer recording packet ignored: " + error.GetBaseException().Message); }
                });
            }
            if (!state.Ready && peer.IsReady()) peer.m_rpc.Invoke(CapabilityRpc, Protocol);
        }
        private static void AnnounceInterest()
        {
            int count = 0;
            foreach (ZNetPeer peer in network.GetPeers()) if (Capable(peer)) ++count;
            foreach (ZNetPeer peer in network.GetPeers())
                if (Capable(peer)) peer.m_rpc.Invoke(CapabilityRpc, -Math.Max(1, count));
        }
        private static bool Epoch(ZDO data, MobBridgeObserverPacket packet)
        {
            return data != null && data.IsValid() &&
                (data.OwnerRevision == packet.Revision && data.GetOwner() == packet.Source ||
                 data.OwnerRevision == packet.Revision + 1U && data.GetOwner() == packet.Destination);
        }
        private static bool Matches(Entry entry, MobBridgeObserverPacket packet)
        { return entry.Descriptor.Same(packet.Source, packet.Destination, packet.Revision, packet.Token); }
        private static double Lifetime(MobBridgeObserverPacket packet)
        { return Math.Max(5d, packet.RecordingSeconds * 8d); }
        private static MobBridgeObserverPacket Header(Entry entry, byte kind)
        {
            var d = entry.Descriptor;
            return new MobBridgeObserverPacket { Kind = kind, Source = d.Source, Destination = d.Destination,
                Revision = d.Revision, Token = d.Token, Sequence = entry.Sequence };
        }
        internal static void Begin(ZDO data, long destination, uint revision, uint token, float window, float rate,
            float aiRate, Vector3 origin, Quaternion rotation, MobBridgeAnimation animation, ZDOID[] effects,
            float recordingWindow = 0f, float firstArrival = 0f, float physicsStep = 0f, float expectedMatch = 0f, float startup = 0f, int physicsLayer = -1)
        {
            try
            {
                if (!started || !ZNet.instance || ZNet.instance.IsServer()) return;
#if TML_DEBUG_SYSTEM
                if (FgnSoloCombatTest.Vanilla(data)) return;
#endif
                Context(); ZNetPeer server = network.GetServerPeer();
                Capability capability;
                if (!Capable(server) || !capabilities.TryGetValue(server.m_rpc, out capability) ||
                    capability.Clients <= (destination == server.m_uid ? 1 : 2) ||
                    entries.Count >= Limit || animation.BoneCount > MobBridgeObserverPacket.MaxBones) return;
                Vector3 model; Quaternion tilt; animation.History.SampleRoot(0f, out model, out tilt);
                var descriptor = new MobBridgeObserverPacket { Kind = 0, Source = ZDOMan.GetSessionID(), Destination = destination,
                    Revision = revision, Token = token, Prefab = data.GetPrefab(), Window = window, Rate = rate, AiRate = aiRate,
                    RecordingWindow = recordingWindow, FirstArrival = firstArrival, PhysicsStep = physicsStep,
                    ExpectedMatch = expectedMatch, Startup = startup, PhysicsLayer = physicsLayer,
                    Origin = origin, Rotation = rotation, ModelPosition = model, ModelRotation = tilt,
                    Bones = animation.BoneCount, Paths = animation.RigPaths(),
                    Effects = effects ?? new ZDOID[0] };
                var entry = new Entry { Descriptor = descriptor, Until = Time.realtimeSinceStartupAsDouble + Lifetime(descriptor) };
                server.m_rpc.Invoke(StreamRpc, data.m_uid, descriptor.Encode());
                entries[data.m_uid] = entry;
            }
            catch (Exception error) { Plugin.Warn("Observer descriptor retained local playback: " + error.GetBaseException().Message); }
        }
        internal static bool Publishing(ZDOID id, uint revision, uint token)
        {
            Entry entry;
            return started && entries.TryGetValue(id, out entry) && !entry.Closed &&
                entry.Descriptor.Source == ZDOMan.GetSessionID() && entry.Descriptor.Revision == revision && entry.Descriptor.Token == token;
        }
        internal static void Publish(ZDO data, MobBridgeRecording recording, MobBridgeAnimation animation, ref int cursor, bool closed)
        {
            Entry entry;
            if (!entries.TryGetValue(data.m_uid, out entry) || entry.Closed) return;
            double now = Time.realtimeSinceStartupAsDouble;
            // Two initial native endpoints prime interpolation. Thereafter use
            // the same 50ms batching target as native ZDO send rounds.
            if (!closed && entry.Batches >= 2 && now < entry.NextSend) return;
            ZNetPeer server = network.GetServerPeer(); if (!Capable(server)) return;
            var packet = Header(entry, 1); int bytes = 40, next = cursor;
            // Retain one look-ahead endpoint after initial priming so the frame
            // before a predicted attack carries its exact native bone pose.
            int available = closed ? animation.History.Count : Math.Max(2, animation.History.Count - 1);
            available = Math.Min(available, animation.History.Count);
            while (next < available && animation.History.TimeAt(next) <= recording.End)
            {
                var sample = animation.History.Export(next);
                recording.Sample(sample.Time, out sample.Position, out sample.Rotation);
                int size = 80 + (sample.Positions == null ? 0 : sample.Positions.Length * 41);
                if (packet.Samples.Count > 0 && (bytes + size > 60000 || packet.Samples.Count == MobBridgeObserverPacket.MaxFrames))
                { SendBatch(server, data.m_uid, entry, packet); cursor = next; packet = Header(entry, 1); bytes = 40; }
                packet.Samples.Add(sample); bytes += size; ++next;
            }
            if (packet.Samples.Count > 0) { SendBatch(server, data.m_uid, entry, packet); cursor = next; }
            entry.NextSend = now + MobBridgeProtocol.ClockInterval;
            if (closed)
            {
                var seal = Header(entry, 2); seal.Sequence = entry.Sequence + 1U; seal.End = recording.End;
                server.m_rpc.Invoke(StreamRpc, data.m_uid, seal.Encode()); entry.Sequence = seal.Sequence; entry.Closed = true;
            }
        }
        private static void SendBatch(ZNetPeer server, ZDOID id, Entry entry, MobBridgeObserverPacket packet)
        { packet.Sequence = entry.Sequence + 1U; server.m_rpc.Invoke(StreamRpc, id, packet.Encode()); entry.Sequence = packet.Sequence; ++entry.Batches; }

        private static void Receive(ZNetPeer peer, ZRpc rpc, ZDOID id, ZPackage raw)
        {
            if (!started || id.IsNone()) return;
            Context(); if (world == null || !Trusted(peer, rpc) || !Capable(peer)) return;
            MobBridgeObserverPacket packet = network.IsServer() ? MobBridgeObserverPacket.DecodeForRelay(raw) : MobBridgeObserverPacket.Decode(raw);
            ZDO data = world.GetZDO(id);
            if (!Epoch(data, packet)) return;
#if TML_DEBUG_SYSTEM
            if (FgnSoloCombatTest.Vanilla(data)) return;
#endif
            if (network.IsServer()) ReceiveServer(peer, id, data, packet, raw);
            else ReceiveClient(id, data, packet);
        }
        private static void ReceiveServer(ZNetPeer peer, ZDOID id, ZDO data, MobBridgeObserverPacket packet, ZPackage raw)
        {
            Entry entry;
            if (packet.Kind == 4)
            {
                if (!entries.TryGetValue(id, out entry) || !Matches(entry, packet) || !entry.Recipients.Remove(peer.m_uid)) return;
                if (entry.Recipients.Count == 0) { SendDemand(id, entry, 0f); StopSource(id, entry); }
                return;
            }
            if (peer.m_uid != packet.Source || packet.Kind > 2) return;
            if (packet.Kind == 0)
            {
                if (data.GetPrefab() != packet.Prefab) return;
                if (entries.TryGetValue(id, out entry))
                { if (entry.Descriptor.Revision >= packet.Revision) return; }
                else if (entries.Count >= Limit) return;
                entry = new Entry { Descriptor = packet, Until = Time.realtimeSinceStartupAsDouble + Lifetime(packet) };
                foreach (ZNetPeer recipient in network.GetPeers())
                    if (Relevant(recipient, data, packet)) entry.Recipients.Add(recipient.m_uid);
                if (entry.Recipients.Count == 0)
                { var decline = Header(entry, 5); peer.m_rpc.Invoke(StreamRpc, id, decline.Encode()); return; }
                entries[id] = entry; Relay(id, data, entry, raw);
                SendDemand(id, entry, packet.RecordingSeconds * packet.Rate);
                return;
            }
            if (!entries.TryGetValue(id, out entry) || !Matches(entry, packet) || entry.Closed || packet.Sequence != entry.Sequence + 1U) return;
            if (packet.Kind == 1)
            {
                if (entry.Received + packet.Samples.Count > MaxSamples || packet.Samples[0].Time < entry.End ||
                    packet.Samples[packet.Samples.Count - 1].Time > entry.Descriptor.RecordingSeconds * entry.Descriptor.Rate + .00001f) return;
                entry.Received += packet.Samples.Count;
                entry.End = packet.Samples[packet.Samples.Count - 1].Time;
            }
            else
            {
                if (Math.Abs(packet.End - entry.End) > .00001f) return;
                entry.Closed = true; SendDemand(id, entry, packet.End);
            }
            entry.Sequence = packet.Sequence; Relay(id, data, entry, raw);
        }
        private static bool Relevant(ZNetPeer peer, ZDO data, MobBridgeObserverPacket descriptor)
        {
            return peer != null && peer.m_uid != descriptor.Source && peer.m_uid != descriptor.Destination && Capable(peer) &&
                ZNetScene.InActiveArea(data.GetPosition(), peer.GetRefPos());
        }
        private static void StopSource(ZDOID id, Entry entry)
        {
            ZNetPeer source = network.GetPeer(entry.Descriptor.Source);
            if (Capable(source)) source.m_rpc.Invoke(StreamRpc, id, Header(entry, 5).Encode());
        }
        private static void Relay(ZDOID id, ZDO data, Entry entry, ZPackage packet)
        {
            entry.Recipients.RemoveWhere(delegate(long uid) { return !Relevant(network.GetPeer(uid), data, entry.Descriptor); });
            foreach (long uid in entry.Recipients)
            { ZNetPeer peer = network.GetPeer(uid); if (peer != null && Relevant(peer, data, entry.Descriptor)) peer.m_rpc.Invoke(StreamRpc, id, packet); }
            if (entry.Recipients.Count == 0) { SendDemand(id, entry, 0f); StopSource(id, entry); }
        }
        private static void SendDemand(ZDOID id, Entry entry, float end)
        {
            var request = Header(entry, 3); request.End = end;
            if (entry.Descriptor.Destination == ZDOMan.GetSessionID()) DemandClock(id, request);
            else
            { ZNetPeer destination = network.GetPeer(entry.Descriptor.Destination); if (Capable(destination)) destination.m_rpc.Invoke(StreamRpc, id, request.Encode()); }
        }
        private static void DemandClock(ZDOID id, MobBridgeObserverPacket request)
        {
            if (request.End <= 0f) { demands.Remove(id); return; }
            Demand previous;
            if (demands.TryGetValue(id, out previous) && previous.Epoch.Same(request.Source, request.Destination, request.Revision, request.Token))
            { previous.End = request.End; return; }
            if (demands.Count < Limit)
                demands[id] = new Demand { Epoch = request, End = request.End, Until = Time.realtimeSinceStartupAsDouble + 30d };
        }
        private static void ReceiveClient(ZDOID id, ZDO data, MobBridgeObserverPacket packet)
        {
            if (packet.Kind == 3)
            { if (packet.Destination == ZDOMan.GetSessionID()) DemandClock(id, packet); return; }
            Entry entry;
            if (packet.Kind == 5)
            { if (entries.TryGetValue(id, out entry) && Matches(entry, packet) && packet.Source == ZDOMan.GetSessionID()) entries.Remove(id); return; }
            if (packet.Source == ZDOMan.GetSessionID() || packet.Destination == ZDOMan.GetSessionID()) return;
            if (packet.Kind == 0)
            {
                if (packet.Prefab != data.GetPrefab()) return;
                if (entries.TryGetValue(id, out entry)) { if (entry.Descriptor.Revision >= packet.Revision) return; }
                else if (entries.Count >= Limit) return;
                entry = new Entry { Descriptor = packet, Until = Time.realtimeSinceStartupAsDouble + Lifetime(packet) };
                entry.Samples.Add(new MobBridgeObserverPacket.Sample { Time = 0f, Position = packet.Origin, Rotation = packet.Rotation,
                    ModelPosition = packet.ModelPosition, ModelRotation = packet.ModelRotation });
                entries[id] = entry;
                MobHandoffVisual.Cancel(id);
                return;
            }
            if (!entries.TryGetValue(id, out entry) || !Matches(entry, packet) || entry.Closed || packet.Sequence != entry.Sequence + 1U || entry.Matched) return;
            if (packet.Kind == 1)
            {
                if (entry.Received + packet.Samples.Count > MaxSamples || packet.Samples[0].Time < entry.End ||
                    packet.Samples[packet.Samples.Count - 1].Time > entry.Descriptor.RecordingSeconds * entry.Descriptor.Rate + .00001f) return;
                entry.Received += packet.Samples.Count;
                foreach (var sample in packet.Samples)
                {
                    if (!entry.Started && sample.Time == 0f && entry.End == 0f) entry.Samples[0] = sample;
                    if (sample.Time > entry.End)
                    { if (!entry.Started) entry.Samples.Add(sample); entry.End = sample.Time; }
                }
                if (entry.Started) MobBridgeVisual.AppendObserver(id, packet.Revision, packet.Token, packet.Samples, false);
            }
            else if (packet.Kind == 2)
            {
                if (Math.Abs(packet.End - entry.End) > .00001f) return;
                entry.Closed = true;
                if (entry.Started) MobBridgeVisual.AppendObserver(id, packet.Revision, packet.Token, new MobBridgeObserverPacket.Sample[0], true);
            }
            entry.Sequence = packet.Sequence; TryStart(id, data, entry);
        }
        private static void TryStart(ZDOID id, ZDO data, Entry entry)
        {
            if (entry.Started || entry.Matched || entry.Samples.Count < 2 || data.GetOwner() != entry.Descriptor.Destination) return;
            entry.Started = MobBridgeVisual.StartObserver(data, entry.Descriptor, entry.Samples, entry.Closed);
            if (entry.Started)
            { entry.Samples.Clear(); if (entry.LastClock != null) ApplyClock(id, entry); }
        }
        internal static void ObserveClock(ZDOID id, long source, long destination, uint revision, uint token,
            long tick, float simulation, float sample, Vector3 position, Quaternion rotation, Vector3 velocity,
            float attackTime, string attack, float ai, ZPackage raw, bool attackActive = false)
        {
            Entry entry;
            if (!started || !entries.TryGetValue(id, out entry) || !entry.Descriptor.Same(source, destination, revision, token)) return;
            ZDO data = world.GetZDO(id); if (!Epoch(data, entry.Descriptor)) return;
            if (network.IsServer())
            {
                entry.ClockPacket = raw;
                RelayClock(id, data, entry);
            }
            else if (!entry.Matched && source != ZDOMan.GetSessionID() && destination != ZDOMan.GetSessionID())
            {
                if (entry.LastClock != null && (tick < entry.LastClock.Tick ||
                    tick == entry.LastClock.Tick && attackActive == entry.LastClock.AttackActive &&
                    (simulation <= entry.LastClock.Simulation || sample <= entry.LastClock.Sample || ai != entry.LastClock.Ai) ||
                    simulation < entry.LastClock.Simulation ||
                    sample < entry.LastClock.Sample || ai < entry.LastClock.Ai)) return;
                entry.LastClock = new Clock { Tick = tick, Simulation = simulation, Sample = sample, Position = position,
                    Rotation = rotation, Velocity = velocity, AttackTime = attackTime, Attack = attack, Ai = ai,
                    AttackActive = attackActive, Received = Time.realtimeSinceStartupAsDouble };
                if (entry.Started) ApplyClock(id, entry);
            }
        }
        private static void ApplyClock(ZDOID id, Entry entry)
        {
            var d = entry.Descriptor; var c = entry.LastClock;
            MobBridgeVisual.ObserveClock(id, d.Revision, d.Token, c.Tick, c.Simulation, c.Sample, c.Position,
                c.Rotation, c.Velocity, c.AttackTime, c.Attack, c.Ai, c.Received, c.AttackActive);
        }
        private static void RelayClock(ZDOID id, ZDO data, Entry entry)
        {
            entry.ClockRetry = false;
            foreach (long uid in entry.Recipients)
            {
                ZNetPeer peer = network.GetPeer(uid);
                if (!Relevant(peer, data, entry.Descriptor)) continue;
                // This can run from the destination's native AI finalizer.
                // Observer transport must never throw through that operation.
                try { peer.m_rpc.Invoke(ClockRpc, id, entry.ClockPacket); }
                catch (Exception) { entry.ClockRetry = true; }
            }
        }
        internal static bool KeepClock(ZDOID id, long source, long destination, uint revision, uint token, float simulation)
        {
            Demand demand;
            return started && demands.TryGetValue(id, out demand) && demand.Epoch.Same(source, destination, revision, token) &&
                demand.Sent + .000001f < demand.End;
        }
        internal static void ClockSent(ZDOID id, long source, long destination, uint revision, uint token, float simulation)
        {
            Demand demand;
            if (demands.TryGetValue(id, out demand) && demand.Epoch.Same(source, destination, revision, token))
                demand.Sent = Math.Max(demand.Sent, simulation);
        }
        internal static void Finish(ZDOID id, uint revision, uint token, bool final)
        {
            Entry entry;
            if (!entries.TryGetValue(id, out entry) || entry.Descriptor.Revision != revision || entry.Descriptor.Token != token) return;
            if (!entry.Matched)
            {
                entry.Matched = true;
                var done = Header(entry, 4); ZNetPeer server = network ? network.GetServerPeer() : null;
                try { if (Capable(server)) server.m_rpc.Invoke(StreamRpc, id, done.Encode()); } catch (Exception) { }
            }
            if (final) entries.Remove(id);
        }
        internal static void Cancel(ZDOID id) { entries.Remove(id); demands.Remove(id); }
        internal static void Tick()
        {
            if (!started || !Active) return;
            Context(); if (!network || world == null) return;
            expired.Clear(); double now = Time.realtimeSinceStartupAsDouble;
            foreach (var pair in entries)
            {
                Entry entry = pair.Value; ZDO data = world.GetZDO(pair.Key);
                if (!Epoch(data, entry.Descriptor)) { expired.Add(pair.Key); continue; }
                if (network.IsServer())
                {
                    ZNetPeer source = network.GetPeer(entry.Descriptor.Source);
                    if (!entry.Closed && (source == null || !source.IsReady()))
                    {
                        // The accepted prefix remains immutable if its producer
                        // disconnects. Seal that prefix instead of inventing AI.
                        entry.Closed = true; var seal = Header(entry, 2); seal.Sequence = ++entry.Sequence; seal.End = entry.End;
                        Relay(pair.Key, data, entry, seal.Encode()); SendDemand(pair.Key, entry, entry.End);
                    }
                    if (now >= entry.NextCheck)
                    {
                        entry.Recipients.RemoveWhere(delegate(long uid) { return !Relevant(network.GetPeer(uid), data, entry.Descriptor); });
                        if (entry.ClockRetry) RelayClock(pair.Key, data, entry);
                        entry.NextCheck = now + MobBridgeProtocol.ClockInterval;
                    }
                    if (entry.Recipients.Count == 0) { SendDemand(pair.Key, entry, 0f); StopSource(pair.Key, entry); expired.Add(pair.Key); continue; }
                }
                else if (entry.Descriptor.Source == ZDOMan.GetSessionID())
                { if (entry.Until < now) expired.Add(pair.Key); continue; }
                else if (entry.Until < now && !entry.Started)
                { Finish(pair.Key, entry.Descriptor.Revision, entry.Descriptor.Token, false); expired.Add(pair.Key); continue; }
                if (!network.IsServer() && entry.Descriptor.Source != ZDOMan.GetSessionID() && !entry.Started && !entry.Matched)
                    TryStart(pair.Key, data, entry);
            }
            foreach (var id in expired) entries.Remove(id);
            expired.Clear();
            foreach (var pair in demands)
            {
                ZDO data = world.GetZDO(pair.Key); Demand demand = pair.Value;
                if (!Epoch(data, demand.Epoch) || demand.Sent + .000001f >= demand.End ||
                    demand.Until < now && data.GetOwner() != demand.Epoch.Destination) expired.Add(pair.Key);
            }
            foreach (var id in expired) demands.Remove(id);
            expired.Clear();
        }
    }
}
#endif
