using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    internal sealed partial class Controller
    {
        private struct Latency { internal float Until; internal int Ping; }
        private readonly Dictionary<long, Latency> latency = new Dictionary<long, Latency>();
        private readonly DueQueue<Node> grace = new DueQueue<Node>();
        internal int PendingTransfers { get { return grace.Count; } }
        private bool Grace(long uid, float now, out float value)
        {
            value = options.MinimumGrace;
            if (options.GraceMultiplier == 0) return true;
            int ping = Ping(uid, now); if (ping <= 0) return false;
            value = Math.Max(value, ping * .001f * options.GraceMultiplier); return true;
        }
        private int Ping(long uid, float now)
        {
            Latency saved;
            if (latency.TryGetValue(uid, out saved) && now < saved.Until) return saved.Ping;
            ZNetPeer peer = ZNet.instance.GetPeer(uid);
            if (peer == null || !peer.IsReady() || peer.m_socket == null) return 0;
            int ping;
            try { ping = SocketLatency.Read(peer.m_socket); }
            catch (Exception) { ping = 0; }
            // Cache unavailable measurements too: no exception/retry on every
            // movement check. Unknown RTT keeps adaptive state and vetoes only
            // those protected source transfers that already require a valid RTT.
            latency[uid] = new Latency { Until = now + 1f, Ping = ping }; return ping;
        }
        private bool LargeGrid(long uid, float now)
        {
            if (!options.Directional || options.AdaptivePing <= 0) return false;
            int ping = Ping(uid, now); bool large = world.Grid.LargeCoverage(uid);
            return ping <= 0 ? large : ping >= options.AdaptivePing * (large ? .75f : 1f);
        }
        internal void SourceData(ZDO data, long source)
        {
            if (grace.Count == 0 || source == 0) return;
            Node node;
            if (nodes.TryGetValue(data, out node) && node.GraceSource == source && grace.Remove(node))
            { node.Managed = false; if (node.InGrid) world.Dismiss(node); Wait(node); }
        }
        internal void SourceRequest(ZDO data, long source)
        { SourceData(data, source); }
        internal void Service(float now)
        {
            ServiceMobs(now);
            Node node;
            while (grace.Take(now, out node))
            {
                if (node.Removed || !nodes.ContainsKey(node.Data) || !node.Data.IsValid()) continue;
                if (node.Data.GetOwner() != node.GraceSource || node.Data.OwnerRevision != node.GraceRevision)
                { Wait(node); continue; }
                long target = Desired(node);
                if (target == 0 && !options.Keep) target = server;
                if (target != server) continue;
                Apply(node, server, true);
            }
            // At most one small pass per second while actual transfer work exists.
            if (now >= nextLatencyPrune && latency.Count != 0)
            {
                nextLatencyPrune = now + 1f; stalePeers.Clear();
                foreach (var pair in latency) if (ZNet.instance.GetPeer(pair.Key) == null) stalePeers.Add(pair.Key);
                foreach (long uid in stalePeers) latency.Remove(uid);
            }
        }
        private float nextLatencyPrune;
        private readonly List<long> stalePeers = new List<long>();
        private void ClearTransfers()
        { grace.Clear(); latency.Clear(); inventory.Clear(); ClearMobs(); }
        internal bool FinishedRequest(ZDO data, int method)
        {
            if (!Catalog.PickRequest(method)) return false;
            Node node;
            return nodes.TryGetValue(data, out node) && node.View && Catalog.FinishedPick(node.View);
        }
    }
}
