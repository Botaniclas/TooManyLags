using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace TooManyLagsStandalone
{
    internal enum Message { Hello, Welcome, Ping, Pong, Prepare, Prepared, Commit, Result, Wake, Target, Cancel, Policy, Forward, ActivationCheck, Activated, Status, Ready, ReadyResult, ReadyLost, Relay, ConfigRequest, ConfigReply, TextEdit }
    internal struct PlayerPeer
    {
        internal long Uid;
        internal ZDO Character;
        internal Vector3 Reference;
        internal bool Local;
    }
    internal sealed class Network
    {
        // Protocol 3 certifies the new active-use/incinerator safeguards. Older
        // clients remain connected, but take the native-safe ownership path.
        // Text editing is an optional trailing capability; older protocol-3
        // peers keep their existing handoffs and native text-object ownership.
        internal const int Protocol = 3;
        private const string RpcName = "TooManyLags_Standalone_1";
        private sealed class Connection
        {
            internal ZNetPeer Peer;
            internal bool Capable, SupportsMobs, SupportsCameraConfig, SupportsCameraPrediction, SupportsText; internal bool? MobWatch;
            internal float HelloAt, NextPing, PingAt, MeasuredAt, Rtt = Single.PositiveInfinity;
            internal long Stamp;
            internal float ConfigWindow;
            internal int ConfigRequests;
            internal readonly Queue<ZPackage> Out = new Queue<ZPackage>();
        }
        private readonly Dictionary<ZRpc, Connection> connections = new Dictionary<ZRpc, Connection>();
        private readonly List<ZRpc> gone = new List<ZRpc>();
        internal int RankVersion;
        internal bool Confirmed;
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
                if (!net.IsServer() && !c.Capable && now >= c.HelloAt)
                { c.HelloAt = now + 3f; Send(c.Peer.m_uid, Message.Hello, delegate(ZPackage p) { p.Write(Protocol); p.Write(Plugin.TargetSupport); p.Write(TextEditingProtection.Supported); }); }
                if (net.IsServer() && Plugin.HooksActive && c.Capable && now >= c.NextPing && (c.Stamp == 0 || now - c.PingAt >= 10f))
                {
                    c.NextPing = now + Plugin.Options.PingInterval; c.PingAt = now; c.Stamp = Stopwatch.GetTimestamp();
                    long stamp = c.Stamp; Send(c.Peer.m_uid, Message.Ping, delegate(ZPackage p) { p.Write(stamp); });
                }
                if (net.IsServer() && Plugin.HooksActive && !c.Capable && now >= c.NextPing)
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
            foreach (ZRpc rpc in gone) { TextEditingProtection.Disconnected(connections[rpc].Peer); connections.Remove(rpc); RankVersion++; }
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
            return peer != null && peer.IsReady() && peer.m_rpc != null;
        }
        internal bool TextCapable(long uid)
        {
            if (!TextEditingProtection.Supported || !Capable(uid)) return false;
            if (uid == ZDOMan.GetSessionID()) return true;
            ZNetPeer peer = ZNet.instance.GetPeer(uid); Connection c;
            return peer != null && connections.TryGetValue(peer.m_rpc, out c) && c.SupportsText;
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
            if (!ZNet.instance.IsServer() && message != Message.Hello && (!c.Capable || !ReferenceEquals(ZNet.instance.GetServerPeer(), peer))) return;
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
            catch (Exception error) { Plugin.Warn("Ignored invalid standalone packet: " + error.GetBaseException().Message); }
        }
        private void Handle(Connection c, long uid, ZPackage packet)
        {
            Message message = (Message)packet.ReadInt(); bool server = ZNet.instance.IsServer();
            if (message == Message.Hello && server && c != null)
            {
                if (packet.ReadInt() != Protocol) return;
                bool newCapability = !c.Capable;
                if (newCapability) { c.Rtt = Single.PositiveInfinity; c.Stamp = 0; c.NextPing = Time.unscaledTime; }
                c.SupportsMobs = packet.ReadBool();
                c.SupportsText = packet.GetPos() < packet.Size() && packet.ReadBool();
                c.SupportsCameraConfig = c.SupportsCameraPrediction = true;
                c.Capable = true; RankVersion++;
                if (newCapability && Plugin.Controller != null) Plugin.Controller.CapabilityChanged(uid);
                SendSettings(c); return;
            }
            if (!server && (c == null || ZNet.instance.GetServerPeer() != c.Peer)) return;
            if (message == Message.Welcome && !server && c != null)
            {
                if (packet.ReadInt() != Protocol) return;
                bool enabled = packet.ReadBool(), mobs = packet.ReadBool();
                float interval = packet.ReadSingle(), degrees = packet.ReadSingle(), prediction = packet.ReadSingle();
                if (Single.IsNaN(interval) || Single.IsInfinity(interval) || interval < .05f || interval > 5f ||
                    Single.IsNaN(degrees) || Single.IsInfinity(degrees) || degrees < 0f || degrees > 45f ||
                    Single.IsNaN(prediction) || Single.IsInfinity(prediction) || prediction < 0f || prediction > .5f) return;
                c.SupportsText = packet.GetPos() < packet.Size() && packet.ReadBool();
                Plugin.ApplySession(enabled, mobs, interval, degrees, prediction);
                c.Capable = Confirmed = true; return;
            }
            if (c != null && !c.Capable) return;
            if (message == Message.TextEdit)
            { if (Plugin.HooksActive && TextCapable(uid)) TextEditingProtection.Receive(c == null ? null : c.Peer, uid, packet); return; }
            if (message == Message.ConfigRequest && server && c != null)
            {
                float now = Time.unscaledTime;
                if (now < c.ConfigWindow || now - c.ConfigWindow >= 1f) { c.ConfigWindow = now; c.ConfigRequests = 0; }
                if (++c.ConfigRequests > 4) return;
                int id = packet.ReadInt(); string command = packet.ReadString();
                if (id <= 0 || command == null || command.Length > 256) return;
                string reply = ConfigConsole.Request(c.Peer, command);
                if (reply != null && reply.Length <= 12000) Send(uid, Message.ConfigReply, delegate(ZPackage p) { p.Write(id); p.Write(reply); });
                return;
            }
            if (message == Message.ConfigReply && !server && c != null)
            { int id = packet.ReadInt(); ConfigConsole.Reply(c.Peer, id, packet.ReadString()); return; }
            if (message == Message.Ping && !server)
            { long stamp = packet.ReadLong(); Send(uid, Message.Pong, delegate(ZPackage p) { p.Write(stamp); }); return; }
            if (message == Message.Pong && server && c != null)
            {
                long stamp = packet.ReadLong(); if (stamp == 0 || stamp != c.Stamp) return; c.Stamp = 0;
                float rtt = (float)((Stopwatch.GetTimestamp() - stamp) * 1000d / Stopwatch.Frequency);
                Measure(c, rtt, Time.unscaledTime); return;
            }
            if (!Plugin.HooksActive) return;
            if (message == Message.Policy && !server) { if (Plugin.Local != null) Plugin.Local.SetMobWatch(packet.ReadBool()); return; }
            if ((message == Message.Prepare || message == Message.Commit || message == Message.Cancel || message == Message.ActivationCheck || message == Message.Status || message == Message.Ready) && (!server || (c == null && uid == ZDOMan.GetSessionID())))
            { if (Plugin.Local != null) Plugin.Local.Receive(message, packet); return; }
            if (!server || Plugin.Controller == null) return;
            if (message == Message.ReadyResult || message == Message.ReadyLost) { Plugin.Controller.Recipients.Receive(uid, message, packet); return; }
            if (message == Message.Relay) { RequestRouter.Relayed(uid, packet); return; }
            if (message == Message.Forward) { RequestRouter.Forwarded(uid, packet); return; }
            if (message == Message.Target) Plugin.Controller.Mobs.Report(uid, packet.ReadZDOID(), packet.ReadUShort(), packet.ReadZDOID());
            else Plugin.Controller.Handoffs.Receive(uid, message, packet);
        }
        private void SendSettings(Connection c)
        {
            Send(c.Peer.m_uid, Message.Welcome, delegate(ZPackage p)
            {
                p.Write(Protocol); p.Write(Plugin.Options.Enabled); p.Write(Plugin.Options.Mobs);
                p.Write(Plugin.Options.CameraInterval); p.Write(Plugin.Options.CameraOverlap); p.Write(Plugin.Options.CameraPrediction);
                p.Write(TextEditingProtection.Supported);
            });
        }
        internal void BroadcastSettings()
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return;
            foreach (Connection c in connections.Values) if (c.Capable && c.Peer.IsReady()) SendSettings(c);
            FlushAll();
        }
        internal void Reset() { connections.Clear(); gone.Clear(); Confirmed = false; RankVersion++; }
    }
}
