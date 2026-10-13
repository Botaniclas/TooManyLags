using System;
using UnityEngine;

namespace TooManyLags
{
    // One authenticated startup choice per server connection. No object polling.
    internal static class ServerSession
    {
        private const string RpcName = "TooManyLags_ServerSession_1";
        private static ZNet current;
        private static ZNetPeer source;
        private static ZRpc socket;
        private static bool known;
        private static int attempts;
        private static double retryAt;

        internal static void Connect(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null) return;
            if (peer.m_server && !ReferenceEquals(source, peer)) Reset();
            double nextRequest = Double.NegativeInfinity;
            peer.m_rpc.Register<int, int>(RpcName, delegate(ZRpc rpc, int version, int flags)
            {
                ZNet net = ZNet.instance;
                if (version != 1 || !net || !peer.IsReady() || !ReferenceEquals(rpc, peer.m_rpc) ||
                    !ReferenceEquals(net.GetPeer(peer.m_uid), peer)) return;
                if (net.IsDedicated())
                {
                    if (flags != -1 || Time.realtimeSinceStartupAsDouble < nextRequest) return;
                    nextRequest = Time.realtimeSinceStartupAsDouble + 1;
                    Send(peer); return;
                }
                if (!peer.m_server || !ReferenceEquals(net.GetServerPeer(), peer) ||
                    (flags != 0 && flags != 1 && flags != 3)) return;
                bool same = ReferenceEquals(current, net) && ReferenceEquals(source, peer) && ReferenceEquals(socket, rpc);
                if (same && known) return; // Restart settings never change mid-session.
                if (!same) Reset();
                current = net; source = peer; socket = rpc; known = true;
                try { Plugin.ApplyServerSession((flags & 1) != 0, (flags & 2) != 0); }
                catch (Exception error)
                { Plugin.ClearClientSession(); Plugin.Report("Server-controlled startup failed; native ownership retained. " + error.GetBaseException().Message); }
            });
        }
        internal static void Ready(ZNetPeer peer)
        { if (ZNet.instance && ZNet.instance.IsDedicated() && peer != null && peer.IsReady()) Send(peer); }
        private static void Send(ZNetPeer peer)
        {
            try { if (peer.m_rpc != null) peer.m_rpc.Invoke(RpcName, 1, Plugin.SessionFlags()); }
            catch (Exception) { }
        }
        internal static void Poll()
        {
            if (known) return;
            ZNet net = ZNet.instance;
            if (!net || net.IsServer()) return;
            ZNetPeer peer = net.GetServerPeer();
            if (peer == null || !peer.IsReady() || peer.m_rpc == null)
            { if (current != null) Reset(); return; }
            if (!ReferenceEquals(current, net) || !ReferenceEquals(source, peer) || !ReferenceEquals(socket, peer.m_rpc))
            { Reset(); current = net; source = peer; socket = peer.m_rpc; retryAt = Double.NegativeInfinity; }
            if (attempts >= 15 || Time.realtimeSinceStartupAsDouble < retryAt) return;
            retryAt = Time.realtimeSinceStartupAsDouble + 2; attempts++;
            try { socket.Invoke(RpcName, 1, -1); } catch (Exception) { }
        }
        internal static void Reset()
        {
            Plugin.ClearClientSession();
            current = null; source = null; socket = null; known = false; attempts = 0; retryAt = 0;
        }
    }
}
