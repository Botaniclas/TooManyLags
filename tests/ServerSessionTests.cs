using System;
using TooManyLags;
using UnityEngine;

internal static class ServerSessionTests
{
    private const string Rpc = "TooManyLags_ServerSession_1";
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static ZNetPeer Client(long id = 7)
    {
        ServerSession.Reset(); Time.realtimeSinceStartupAsDouble = 0;
        var peer = new ZNetPeer { m_uid = id, m_server = true, Ready = true };
        ZNet.instance = new ZNet { Peer = peer }; ServerSession.Connect(peer); return peer;
    }
    public static int Main()
    {
        try
        {
            var peer = Client(); int applied = Plugin.SessionApplies;
            peer.Ready = false; ServerSession.Poll(); Check(peer.m_rpc.Sends == 0, "no request before authenticated peer is ready");
            peer.Ready = true; ServerSession.Poll();
            Check(peer.m_rpc.Sends == 1 && peer.m_rpc.Last == Rpc && (int)peer.m_rpc.Values[1] == -1, "ready client requests startup choice");
            for (int i = 0; i < 1000; i++) ServerSession.Poll();
            Check(peer.m_rpc.Sends == 1 && Plugin.SessionApplies == applied, "waiting does not initialize features or repeat traffic each frame");
            peer.m_rpc.Pair(Rpc, 2, 3, peer.m_rpc); peer.m_rpc.Pair(Rpc, 1, 2, peer.m_rpc); peer.m_rpc.Pair(Rpc, 1, 4, peer.m_rpc);
            peer.m_rpc.Pair(Rpc, 1, 3, new ZRpc());
            Check(Plugin.SessionApplies == applied, "unknown protocols, invalid combinations and foreign sockets rejected");
            peer.m_server = false; peer.m_rpc.Pair(Rpc, 1, 3, peer.m_rpc); peer.m_server = true;
            Check(Plugin.SessionApplies == applied, "non-server peers cannot enable features");
            peer.m_rpc.Pair(Rpc, 1, 0, peer.m_rpc);
            Check(Plugin.SessionApplies == applied + 1 && !Plugin.SessionEnabled && !Plugin.SessionMobs, "server OFF overrides both flags");
            for (int i = 0; i < 1000; i++) { Time.realtimeSinceStartupAsDouble = i; ServerSession.Poll(); }
            peer.m_rpc.Pair(Rpc, 1, 3, peer.m_rpc);
            Check(peer.m_rpc.Sends == 1 && Plugin.SessionApplies == applied + 1, "confirmed OFF has no polling traffic and cannot change mid-session");

            var old = peer; peer = Client(8); applied = Plugin.SessionApplies;
            old.m_rpc.Pair(Rpc, 1, 3, old.m_rpc);
            Check(Plugin.SessionApplies == applied, "old server replies cannot activate a new connection");
            peer.m_rpc.Pair(Rpc, 1, 1, peer.m_rpc);
            Check(Plugin.SessionEnabled && !Plugin.SessionMobs, "server enables grid with AI disabled independently");
            peer = Client(9); peer.m_rpc.Pair(Rpc, 1, 3, peer.m_rpc);
            Check(Plugin.SessionEnabled && Plugin.SessionMobs && Plugin.SessionAutoFgn, "new and legacy auto-setup server flags enable both modules after prior OFF");
            peer = Client(12); peer.m_rpc.Pair(Rpc, 1, 7, peer.m_rpc);
            Check(Plugin.SessionEnabled && Plugin.SessionMobs && !Plugin.SessionAutoFgn, "server disables automatic FGN setup without disabling grid or AI");
            peer = Client(13); peer.m_rpc.Pair(Rpc, 1, 5, peer.m_rpc);
            Check(Plugin.SessionEnabled && !Plugin.SessionMobs && !Plugin.SessionAutoFgn, "manual FGN setup also supports grid-only mode");
            int clears = Plugin.SessionClears; ServerSession.Reset();
            Check(Plugin.SessionClears == clears + 1 && !Plugin.SessionEnabled && !Plugin.SessionMobs, "shutdown removes the old runtime decision");

            peer = Client(10);
            for (int i = 0; i < 40; i++) { Time.realtimeSinceStartupAsDouble = i * 2; ServerSession.Poll(); }
            Check(peer.m_rpc.Sends == 15, "missing/older server endpoint produces only bounded join requests");
            peer = Client(11); Plugin.SessionFail = true; int reports = Plugin.SessionReports;
            peer.m_rpc.Pair(Rpc, 1, 3, peer.m_rpc); Plugin.SessionFail = false;
            Check(!Plugin.SessionEnabled && !Plugin.SessionMobs && Plugin.SessionReports == reports + 1, "failed feature initialization clears runtime state and reports once");

            ServerSession.Reset(); Time.realtimeSinceStartupAsDouble = 0;
            peer = new ZNetPeer { Ready = true }; ZNet.instance = new ZNet { Dedicated = true, Peer = peer }; ServerSession.Connect(peer);
            Plugin.Flags = 1; peer.m_rpc.Pair(Rpc, 1, -1, peer.m_rpc);
            Check(peer.m_rpc.Sends == 1 && (int)peer.m_rpc.Values[1] == 1, "server replies with its startup snapshot");
            peer.m_rpc.Pair(Rpc, 1, -1, peer.m_rpc);
            Check(peer.m_rpc.Sends == 1, "join requests rate-limited per connection");
            Time.realtimeSinceStartupAsDouble = 1; peer.m_rpc.Pair(Rpc, 1, -1, new ZRpc());
            Check(peer.m_rpc.Sends == 1, "server rejects an unrelated request socket");
            Plugin.Flags = 0; ServerSession.Ready(peer);
            Check((int)peer.m_rpc.Values[1] == 0, "disabled server still communicates OFF at join");
            Plugin.Flags = 7; ServerSession.Ready(peer);
            Check((int)peer.m_rpc.Values[1] == 7, "server transmits the manual-setup bit in its startup snapshot");
            ServerSession.Reset();
            Console.WriteLine("PASS: " + checks + " server-controlled startup, authentication, retries, OFF/AI modes, idle and reconnect assertions. Simulated transport; no Unity session."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
