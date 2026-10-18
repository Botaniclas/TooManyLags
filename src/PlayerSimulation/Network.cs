using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags.PlayerSimulation
{
    internal enum Message { Hello, Welcome, Ping, Pong, Prepare, Prepared, Commit, Result, Wake, Target, Cancel, Policy, Forward, ActivationCheck, Activated }
    internal struct PlayerPeer
    {
        internal long Uid;
        internal ZDO Character;
        internal Vector3 Reference;
        internal bool Local;
    }
    internal sealed class Network
    {
        internal const int Protocol = 1;
        private const string RpcName = "TooManyLags_FGN_PlayerSimulation_1";
        private sealed class Connection
        {
            internal ZNetPeer Peer;
            internal bool Capable, SupportsMobs, ModeKnown, TextEditing, LooseItems; internal bool? MobWatch;
            internal float HelloAt, NextPing, MeasuredAt, Rtt = Single.PositiveInfinity;
            internal readonly Queue<ZPackage> Out = new Queue<ZPackage>();
        }
        private readonly Dictionary<ZRpc, Connection> connections = new Dictionary<ZRpc, Connection>();
        private readonly List<ZRpc> gone = new List<ZRpc>();
        internal int RankVersion;
        internal bool Confirmed;
        internal static bool TextEditingAvailable = false;
        internal bool SupportsTextEditing(ZNetPeer peer)
        { Connection c; return peer != null && peer.IsReady() && peer.m_rpc != null && connections.TryGetValue(peer.m_rpc, out c) && ReferenceEquals(c.Peer, peer) && c.Capable && c.TextEditing; }
        internal bool SupportsLooseItems(long uid)
        {
            if (ZNet.instance && uid == ZDOMan.GetSessionID()) return true;
            ZNetPeer peer = ZNet.instance ? ZNet.instance.GetPeer(uid) : null; Connection c;
            return peer != null && peer.IsReady() && peer.m_rpc != null && connections.TryGetValue(peer.m_rpc, out c) && ReferenceEquals(c.Peer, peer) && c.Capable && c.LooseItems;
        }
        internal bool NeedsPoll
        {
            get
            {
                if (Confirmed) return true;
                ZNetPeer peer = ZNet.instance ? ZNet.instance.GetServerPeer() : null; Connection c;
                return peer == null || peer.m_rpc == null || !connections.TryGetValue(peer.m_rpc, out c) || !c.ModeKnown;
            }
        }
        internal void Register(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null || connections.ContainsKey(peer.m_rpc)) return;
            connections.Add(peer.m_rpc, new Connection { Peer = peer, NextPing = Time.unscaledTime });
            peer.m_rpc.Register<ZPackage>(RpcName, Receive);
        }
        internal void Poll(float now)
        {
            ZNet net = ZNet.instance;
            if (!net) return;
            List<ZNetPeer> peers = net.GetPeers();
            foreach (ZNetPeer peer in peers) Register(peer);
            gone.Clear();
            foreach (KeyValuePair<ZRpc, Connection> item in connections)
            {
                Connection c = item.Value;
                if (!peers.Contains(c.Peer)) { gone.Add(item.Key); continue; }
                if (!c.Peer.IsReady()) continue;
                if (net.IsServer() && !Single.IsInfinity(c.Rtt) && now - c.MeasuredAt >= Math.Max(10f, Plugin.Options.PingInterval * 3f))
                { c.Rtt = Single.PositiveInfinity; RankVersion++; }
                if (!net.IsServer() && !c.ModeKnown && now >= c.HelloAt)
                { c.HelloAt = now + 3f; Send(c.Peer.m_uid, Message.Hello, delegate(ZPackage p) { p.Write(Protocol); p.Write(Plugin.Options.Mobs); p.Write(TextEditingAvailable); p.Write(true); }); }
                // Reuse FGN's connection measurements; no second ping protocol.
                if (net.IsServer() && now >= c.NextPing)
                {
                    c.NextPing = now + Plugin.Options.PingInterval;
                    Measure(c, NativeLatency.Read(c.Peer.m_rpc), now);
                }
                if (net.IsServer() && c.Capable && Plugin.Controller != null)
                {
                    bool watch = Plugin.Options.Mobs && c.SupportsMobs && Plugin.Controller.WatchMobs(c.Peer.m_uid);
                    if (c.MobWatch != watch) { c.MobWatch = watch; Send(c.Peer.m_uid, Message.Policy, delegate(ZPackage p) { p.Write(watch); }); }
                }
                Flush(c);
            }
            foreach (ZRpc rpc in gone) { connections.Remove(rpc); RankVersion++; }
            gone.Clear();
        }
        internal void FlushAll() { foreach (Connection c in connections.Values) if (c.Peer.IsReady()) Flush(c); }
        private static void Flush(Connection c)
        {
            if (c.Out.Count == 0) return;
            // Bound each dispatch, including transitions across dense bases.
            int count = Math.Min(64, c.Out.Count); ZPackage batch = new ZPackage(); batch.Write(count);
            for (int i = 0; i < count; i++) batch.Write(c.Out.Dequeue());
            c.Peer.m_rpc.Invoke(RpcName, batch);
        }
        internal bool Capable(long uid)
        {
            if (uid == 0 || !ZNet.instance || ZDOMan.instance == null) return false;
            if (uid == ZDOMan.GetSessionID()) return ZNet.instance.IsServer() && !ZNet.instance.IsDedicated() && Player.m_localPlayer;
            ZNetPeer peer = ZNet.instance.GetPeer(uid); Connection c;
            return peer != null && peer.IsReady() && peer.m_rpc != null && connections.TryGetValue(peer.m_rpc, out c) && c.Capable;
        }
        internal bool Present(long uid)
        {
            if (uid == 0 || !ZNet.instance || ZDOMan.instance == null) return false;
            if (uid == ZDOMan.GetSessionID()) return ZNet.instance.IsServer() && !ZNet.instance.IsDedicated() && Player.m_localPlayer;
            ZNetPeer peer = ZNet.instance.GetPeer(uid);
            return peer != null && peer.IsReady() && peer.m_rpc != null && NativeLatency.Matched(peer);
        }
        private void Measure(Connection c, float rtt, float now)
        {
            if (Single.IsNaN(rtt) || Single.IsInfinity(rtt) || rtt < 0 || rtt > 10000) return;
            float before = c.Rtt; c.Rtt = Single.IsInfinity(before) ? rtt : before + (rtt - before) * .3f; c.MeasuredAt = now;
            if (Single.IsInfinity(before) || (int)(before / 10f) != (int)(c.Rtt / 10f)) RankVersion++;
        }
        internal float Latency(long uid)
        {
            if (ZNet.instance && ZNet.instance.IsServer() && !ZNet.instance.IsDedicated() && ZDOMan.instance != null && uid == ZDOMan.GetSessionID()) return 0;
            ZNetPeer peer = ZNet.instance ? ZNet.instance.GetPeer(uid) : null; Connection c;
            return peer != null && peer.IsReady() && peer.m_rpc != null && connections.TryGetValue(peer.m_rpc, out c) && Time.unscaledTime - c.MeasuredAt < Math.Max(10f, Plugin.Options.PingInterval * 3f) ? c.Rtt : Single.PositiveInfinity;
        }
        internal bool MobCapable(long uid)
        {
            if (!Capable(uid)) return false;
            if (uid == ZDOMan.GetSessionID()) return Plugin.Local != null && Plugin.Local.MobsEnabled;
            ZNetPeer peer = ZNet.instance.GetPeer(uid); Connection c;
            return peer != null && connections.TryGetValue(peer.m_rpc, out c) && c.SupportsMobs;
        }
        internal void Players(List<PlayerPeer> output)
        {
            output.Clear();
            if (!ZNet.instance || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            if (!ZNet.instance.IsDedicated() && Player.m_localPlayer)
            {
                ZNetView view = Player.m_localPlayer.GetComponent<ZNetView>();
                if (view && view.IsValid() && view.GetZDO().GetFloat(ZDOVars.s_health, 1f) > 0) output.Add(new PlayerPeer { Uid = ZDOMan.GetSessionID(), Character = view.GetZDO(), Reference = ZNet.instance.GetReferencePosition(), Local = true });
            }
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) if (Present(peer.m_uid) && !peer.m_characterID.IsNone())
            {
                ZDO data = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (data != null && data.IsValid() && data.GetOwner() == peer.m_uid && data.GetFloat(ZDOVars.s_health, 1f) > 0)
                    output.Add(new PlayerPeer { Uid = peer.m_uid, Character = data, Reference = peer.GetRefPos() });
            }
        }
        internal void ToServer(Message message, Action<ZPackage> write)
        {
            if (!ZNet.instance || ZDOMan.instance == null) return;
            long uid = ZNet.instance.IsServer() ? ZDOMan.GetSessionID() : ZNet.instance.GetServerPeer() != null ? ZNet.instance.GetServerPeer().m_uid : 0;
            Send(uid, message, write);
        }
        internal void Send(long uid, Message message, Action<ZPackage> write)
        {
            if (uid == 0 || !ZNet.instance || ZDOMan.instance == null) return;
            ZPackage packet = new ZPackage(); packet.Write((int)message); write(packet);
            if (ZNet.instance.IsServer() && uid == ZDOMan.GetSessionID()) { packet.SetPos(0); Handle(null, uid, packet); return; }
            ZNetPeer peer = ZNet.instance.GetPeer(uid); Connection c;
            if (peer == null || !peer.IsReady() || peer.m_rpc == null || !connections.TryGetValue(peer.m_rpc, out c)) return;
            if (ZNet.instance.IsServer() && !c.Capable) return; // Vanilla clients never receive unknown custom RPCs.
            // There is one active transfer per object; a saturated connection
            // defers attempts rather than retaining an unbounded send queue.
            if (message == Message.Forward && c.Out.Count >= 2048) Flush(c);
            if (message == Message.Forward || c.Out.Count < 2048) c.Out.Enqueue(packet);
        }
        private void Receive(ZRpc rpc, ZPackage batch)
        {
            Connection c;
            if (batch == null || !connections.TryGetValue(rpc, out c) || !c.Peer.IsReady()) return;
            try
            {
                int count = batch.ReadInt(); if (count < 0 || count > 64) return;
                ZPackage packet = new ZPackage();
                for (int i = 0; i < count; i++) { batch.ReadPackage(ref packet); if (packet.Size() > 16384) return; Handle(c, c.Peer.m_uid, packet); }
                Flush(c);
            }
            catch (Exception error) { if (Plugin.Options.Logs) Plugin.Warn("Ignored invalid standalone packet: " + error.GetBaseException().Message); }
        }
        private void Handle(Connection c, long uid, ZPackage packet)
        {
            Message message = (Message)packet.ReadInt(); bool server = ZNet.instance.IsServer();
            if (message == Message.Hello && server && c != null)
            {
                if (packet.ReadInt() != Protocol) return;
                bool newCapability = !c.Capable;
                if (newCapability) { c.Rtt = Single.PositiveInfinity; c.NextPing = Time.unscaledTime; }
                c.SupportsMobs = packet.ReadBool(); c.Capable = true; RankVersion++;
                c.TextEditing = packet.GetPos() < packet.Size() && packet.ReadBool();
                c.LooseItems = packet.GetPos() < packet.Size() && packet.ReadBool();
                if (newCapability && Plugin.Controller != null) Plugin.Controller.CapabilityChanged(uid);
                Send(uid, Message.Welcome, delegate(ZPackage p) { p.Write(Protocol); p.Write(Plugin.Options.Mobs); p.Write(Plugin.ServerMode); p.Write(TextEditingAvailable); p.Write(true); }); return;
            }
            if (!server && (c == null || ZNet.instance.GetServerPeer() != c.Peer)) return;
            if (message == Message.Welcome && !server && c != null)
            {
                if (packet.ReadInt() != Protocol) return;
                bool mobs = packet.ReadBool(), mode = packet.ReadBool(); c.ModeKnown = true;
                c.TextEditing = packet.GetPos() < packet.Size() && packet.ReadBool();
                c.LooseItems = packet.GetPos() < packet.Size() && packet.ReadBool();
                c.Capable = true;
                if (mode) { Plugin.Activate(); Plugin.Local.MobsEnabled = Plugin.Options.Mobs && mobs; }
                else Plugin.Deactivate();
                Confirmed = mode;
                return;
            }
            if (c != null && !c.Capable) return;
            if ((server && !Plugin.ServerMode) || (!server && !Confirmed)) return;
            if (message == Message.Policy && !server && Plugin.Local != null) { Plugin.Local.SetMobWatch(packet.ReadBool()); return; }
            if ((message == Message.Prepare || message == Message.Commit || message == Message.Cancel || message == Message.ActivationCheck) && (!server || (c == null && uid == ZDOMan.GetSessionID())))
            { if (Plugin.Local != null) Plugin.Local.Receive(message, packet); return; }
            if (!server || Plugin.Controller == null) return;
            if (message == Message.Forward) { RequestRouter.Forwarded(uid, packet); return; }
            if (message == Message.Target) Plugin.Controller.Mobs.Report(uid, packet.ReadZDOID(), packet.ReadUShort(), packet.ReadZDOID());
            else Plugin.Controller.Handoffs.Receive(uid, message, packet);
        }
        internal void Reset() { connections.Clear(); gone.Clear(); Confirmed = false; RankVersion++; }
    }
}
