#if GRID_PROTOCOL
using System;
using System.Collections.Generic;
using System.Reflection;
using TooManyLags;
using TooManyLags.GridHandoffs;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static ZPackage ControlBundle(params ZPackage[] parts)
    { return GridPacket(11, delegate(ZPackage p) { p.Write(parts.Length); foreach (ZPackage part in parts) p.Write(part); }); }
    private static List<ZPackage> ExpandControl(List<ZPackage> packets)
    {
        var result = new List<ZPackage>();
        foreach (ZPackage packet in packets)
        {
            packet.SetPos(0); int kind = packet.ReadInt();
            if (kind != 11) { packet.SetPos(0); result.Add(packet); continue; }
            Check("bounded control bundle", packet.Size() <= 4096);
            int count = packet.ReadInt(); Check("bounded control record count", count >= 2 && count <= 32);
            for (int i = 0; i < count; i++) result.Add(packet.ReadPackage());
        }
        return result;
    }
    private static void NetworkBatches()
    {
        int before = checks;
        Fresh(Kinds.Doors, defaultInteractives: true); var peer = Peer(1); GridStart();
        try
        {
            GridHello(peer, true); var sent = GridCapture(peer);
            Protocol.BeginBatch();
            for (int i = 0; i < 100; i++) FgnOwnerHandoff.IdentifiedStatus(peer, new ZDOID { ID = i + 1 }, i + 1);
            Protocol.EndBatch();
            Check("legacy peers retain individual compatible messages", sent.Count == 100);
            GridReceive(peer, GridPacket(0, delegate(ZPackage p) { p.Write(65); })); sent.Clear();
            float now = Time.unscaledTime;
            Protocol.BeginBatch(); Protocol.BeginBatch();
            for (int i = 0; i < 100; i++) FgnOwnerHandoff.IdentifiedStatus(peer, new ZDOID { ID = i + 1 }, i + 1);
            Check("full chunks send without a timing delay", sent.Count == 3);
            Protocol.EndBatch(); Check("nested scope does not flush outer tail", sent.Count == 3);
            Protocol.EndBatch();
            Check("100 modern requests need four RPC envelopes in the same operation", sent.Count == 4 && Time.unscaledTime == now);
            var records = ExpandControl(sent); Check("no control entry is discarded", records.Count == 100);
            for (int i = 0; i < records.Count; i++)
            {
                records[i].SetPos(0);
                Check("command stream order remains unchanged", records[i].ReadInt() == 3 && records[i].ReadLong() == i + 1 && records[i].ReadZDOID().ID == i + 1);
            }
            sent.Clear(); Protocol.BeginBatch(); FgnOwnerHandoff.IdentifiedStatus(peer, new ZDOID { ID = 1 }, 101); Protocol.EndBatch();
            sent[0].SetPos(0); Check("single records have no extra bundle header", sent.Count == 1 && sent[0].ReadInt() == 3);
            sent.Clear(); FgnOwnerHandoff.IdentifiedStatus(peer, new ZDOID { ID = 1 }, 102);
            Check("outside a grid operation the fast reply path stays immediate", sent.Count == 1);
            sent.Clear(); Protocol.BeginBatch();
            FgnOwnerHandoff.IdentifiedStatus(peer, new ZDOID { ID = 1 }, 103);
            Protocol.Offer(peer);
            Check("immediate greeting flushes preceding commands first", sent.Count == 2);
            sent[0].SetPos(0); sent[1].SetPos(0);
            Check("greeting cannot overtake an earlier token", sent[0].ReadInt() == 3 && sent[1].ReadInt() == 1);
            Protocol.EndBatch(); Check("operation does not resend already flushed packets", sent.Count == 2);
            sent.Clear(); Protocol.BeginBatch();
            FgnOwnerHandoff.IdentifiedStatus(peer, new ZDOID { ID = 1 }, 104); peer.Ready = false; Protocol.EndBatch();
            Check("disconnected peer receives no queued packet", sent.Count == 0); peer.Ready = true;
            Protocol.BeginBatch(); FgnOwnerHandoff.IdentifiedStatus(peer, new ZDOID { ID = 1 }, 105); Protocol.Stop(); Protocol.EndBatch();
            Check("shutdown retires queued buffers and batch scope", sent.Count == 0);
        }
        finally { Protocol.Stop(); }
        NetworkBatchOwnerProtection(); NetworkBatchGrid(); NetworkBatchSourceGrid(); NetworkBatchPriorityBoundary(); NetworkBatchFailures();
        Console.WriteLine("PASS: " + (checks - before) + " bounded control-batching assertions; 100 requests use 4 RPC envelopes, not a bandwidth or live transport benchmark.");
    }
    private static object BatchConnection(ZNetPeer peer)
    {
        var links = (System.Collections.IDictionary)typeof(Protocol).GetField("connections", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        return links[peer.m_rpc];
    }
    private static void BatchQueuesEmpty(params ZNetPeer[] peers)
    {
        var pending = (System.Collections.IList)typeof(Protocol).GetField("outgoing", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Check("no peer remains in the completed batch queue", pending.Count == 0);
        Check("completed failure scope has no outstanding nesting", (int)typeof(Protocol).GetField("batchDepth", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == 0);
        Check("completed flush releases its reentrancy guard", !(bool)typeof(Protocol).GetField("flushingBatches", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        foreach (ZNetPeer peer in peers)
        {
            object link = BatchConnection(peer); Type type = link.GetType();
            var records = (List<ZPackage>)type.GetField("Outgoing", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link);
            Check("connection queue marker is cleared", !(bool)type.GetField("Queued", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link));
            Check("connection records and byte counter are cleared", records.Count == 0 && (int)type.GetField("Bytes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link) == 0);
        }
    }
    private static void BatchStatus(ZNetPeer peer, int token)
    { FgnOwnerHandoff.IdentifiedStatus(peer, new ZDOID { ID = token }, token); }
    private static void NetworkBatchFailures()
    {
        Fresh(Kinds.Doors, defaultInteractives: true); var first = Peer(1); var second = Peer(2); GridStart();
        bool diagnostics = Plugin.Diagnostics;
        try
        {
            GridReceive(first, GridPacket(0, delegate(ZPackage p) { p.Write(65); }));
            GridReceive(second, GridPacket(0, delegate(ZPackage p) { p.Write(65); }));
            var firstSent = GridCapture(first); var secondSent = GridCapture(second);
            Protocol.BeginBatch(); BatchStatus(first, 1); BatchStatus(first, 2); BatchStatus(second, 3); BatchStatus(second, 4);
            int packFaults = 0;
            ZPackage.BeforePackageWrite = delegate { if (packFaults++ == 0) throw new InvalidOperationException("injected envelope failure"); };
            bool escaped = false;
            try { Protocol.EndBatch(); } catch (Exception) { escaped = true; }
            finally { ZPackage.BeforePackageWrite = null; }
            Check("packing failure never escapes the batch operation", !escaped);
            Check("packing failure falls back to both original individual messages", firstSent.Count == 2 && ExpandControl(firstSent).Count == 2);
            Check("packing failure does not block the next recipient", secondSent.Count == 1 && ExpandControl(secondSent).Count == 2);
            BatchQueuesEmpty(first, second);
            Protocol.BeginBatch(); BatchStatus(first, 5); BatchStatus(second, 6); Protocol.EndBatch();
            Check("both recipients are immediately usable after a packing failure", firstSent.Count == 3 && secondSent.Count == 2);
            BatchQueuesEmpty(first, second);

            firstSent.Clear(); secondSent.Clear(); int attempts = 0;
            first.m_rpc.OnInvoke = delegate { attempts++; throw new InvalidOperationException("injected transport failure after possible send"); };
            Protocol.BeginBatch(); BatchStatus(first, 7); BatchStatus(first, 8); BatchStatus(second, 9); BatchStatus(second, 10); Protocol.EndBatch();
            Check("transport exception never retries an ambiguously sent envelope", attempts == 1);
            Check("transport exception does not block another recipient", secondSent.Count == 1 && ExpandControl(secondSent).Count == 2);
            BatchQueuesEmpty(first, second);
            firstSent = GridCapture(first); Protocol.BeginBatch(); BatchStatus(first, 11); Protocol.EndBatch();
            Check("a new operation can use a transport that threw", firstSent.Count == 1);
            BatchQueuesEmpty(first, second);

            firstSent.Clear(); secondSent.Clear(); attempts = 0;
            first.m_rpc.OnInvoke = delegate(string name, object[] values)
            { attempts++; if (attempts == 1) throw new InvalidOperationException("injected first legacy send failure"); firstSent.Add(new ZPackage(((ZPackage)values[0]).GetArray())); };
            Protocol.BeginBatch(); BatchStatus(first, 12); BatchStatus(first, 13); BatchStatus(second, 14); BatchStatus(second, 15);
            object legacy = BatchConnection(first); legacy.GetType().GetField("Batching", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(legacy, false);
            Protocol.EndBatch();
            Check("legacy fallback continues after one individual transport error", attempts == 2 && firstSent.Count == 1);
            Check("legacy fallback error does not block the next recipient", secondSent.Count == 1 && ExpandControl(secondSent).Count == 2);
            BatchQueuesEmpty(first, second);
            legacy.GetType().GetField("Batching", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(legacy, true);

            firstSent = GridCapture(first); secondSent.Clear(); packFaults = 0;
            ZPackage.BeforePackageWrite = delegate { if (packFaults++ == 0) throw new InvalidOperationException("injected full-chunk failure"); };
            try
            { Protocol.BeginBatch(); for (int i = 0; i < 100; i++) BatchStatus(first, 100 + i); Protocol.EndBatch(); }
            finally { ZPackage.BeforePackageWrite = null; }
            var expanded = ExpandControl(firstSent);
            Check("full-chunk construction failure retains all 100 commands", expanded.Count == 100);
            for (int i = 0; i < expanded.Count; i++)
            { expanded[i].SetPos(0); Check("fallback and subsequent chunks preserve token order", expanded[i].ReadInt() == 3 && expanded[i].ReadLong() == 100 + i); }
            BatchQueuesEmpty(first, second);

            firstSent = new List<ZPackage>(); bool reentered = false;
            first.m_rpc.OnInvoke = delegate(string name, object[] values)
            {
                firstSent.Add(new ZPackage(((ZPackage)values[0]).GetArray()));
                if (reentered) return; reentered = true;
                Protocol.BeginBatch(); BatchStatus(first, 203); Protocol.EndBatch();
            };
            Protocol.BeginBatch(); BatchStatus(first, 201); BatchStatus(first, 202); Protocol.EndBatch();
            expanded = ExpandControl(firstSent);
            Check("reentrant sends retain the records queued by a callback", expanded.Count == 3);
            for (int i = 0; i < expanded.Count; i++)
            { expanded[i].SetPos(0); Check("reentrant send keeps command order", expanded[i].ReadInt() == 3 && expanded[i].ReadLong() == 201 + i); }
            BatchQueuesEmpty(first, second);

            firstSent = GridCapture(first); secondSent.Clear();
            Protocol.BeginBatch(); BatchStatus(first, 204); BatchStatus(first, 205); BatchStatus(second, 206); BatchStatus(second, 207);
            object corrupt = BatchConnection(first);
            var corruptRecords = (List<ZPackage>)corrupt.GetType().GetField("Outgoing", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(corrupt);
            corruptRecords[0] = null; Protocol.EndBatch();
            Check("an invalid queued record cannot discard the other valid record", firstSent.Count == 1);
            Check("an invalid queued record cannot poison another recipient", secondSent.Count == 1 && ExpandControl(secondSent).Count == 2);
            BatchQueuesEmpty(first, second);

            firstSent = GridCapture(first); Plugin.Diagnostics = true; int warnings = Plugin.Warnings.Count;
            Protocol.BeginBatch(); BatchStatus(first, 208); BatchStatus(first, 209);
            ZPackage.BeforePackageWrite = delegate { throw new InvalidOperationException("injected persistent packaging failure"); };
            try { Protocol.EndBatch(); } finally { ZPackage.BeforePackageWrite = null; }
            Check("diagnostics report a packaging failure without suppressing valid records", Plugin.Warnings.Count > warnings && firstSent.Count == 2);
            BatchQueuesEmpty(first, second);
        }
        finally { ZPackage.BeforePackageWrite = null; Plugin.Diagnostics = diagnostics; Protocol.Stop(); }
        NetworkBatchControllerFailure();
    }
    private static void NetworkBatchControllerFailure()
    {
        Fresh(Kinds.Pickables, defaultInteractives: true, directional: true, cellSize: 4, pattern: GridCoveragePattern.Rounded7x7);
        var first = Peer(1); var second = Peer(2, 64);
        GridStart();
        try
        {
            for (int i = 0; i < 40; i++) { Object(typeof(Pickable), 1, 1, 2); Object(typeof(Pickable), 65, 1, 1); }
            GridReceive(first, GridPacket(0, delegate(ZPackage p) { p.Write(65); }));
            GridReceive(second, GridPacket(0, delegate(ZPackage p) { p.Write(65); }));
            var firstSent = GridCapture(first); var secondSent = GridCapture(second);
            ZNetScene.instance.Ready = delegate { return false; }; int faults = 0;
            ZPackage.BeforePackageWrite = delegate { if (faults++ == 0) throw new InvalidOperationException("injected controller send failure"); };
            try { Tick(0); } finally { ZPackage.BeforePackageWrite = null; }
            Check("a packing fault cannot disable the actual object-grid controller", controller.IsActive);
            Check("controller failure fallback retains all first-peer source commits", ExpandControl(firstSent).Count == 40);
            Check("controller failure fallback still sends all second-peer source commits", ExpandControl(secondSent).Count == 40);
            BatchQueuesEmpty(first, second);
        }
        finally { ZPackage.BeforePackageWrite = null; Protocol.Stop(); }
    }
    private static void NetworkBatchOwnerProtection()
    {
        Fresh(Kinds.Doors, defaultInteractives: true); var peer = Peer(1);
        var serverPeer = new ZNetPeer { Uid = 99, m_server = true }; ZNet.instance.Peers.Add(serverPeer);
        var normal = Object(typeof(Door), 1, 1, 1); var busy = Object(typeof(Door), 1, 1, 1); busy.User = 1;
        var third = Object(typeof(Door), 1, 1, 1); GridStart();
        bool dedicated = ZNet.instance.Dedicated; long session = ZDOMan.SessionID;
        try
        {
            ZNet.instance.Dedicated = false; ZDOMan.SessionID = 1; ZNet.instance.ServerPeer = serverPeer;
            GridReceive(serverPeer, GridPacket(1, delegate(ZPackage p) { p.Write(true); p.Write(false); p.Write(false); p.Write(true); p.Write(true); p.Write(true); }));
            int commits = 0;
            typeof(Protocol).GetField("nativeCommit", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null,
                (Action<ZRpc, ZDOID, long>)delegate(ZRpc rpc, ZDOID id, long target) { commits++; ZDOMan.instance.GetZDO(id).SetOwner(target); });
            Func<ZDO, long, ZPackage> commit = delegate(ZDO data, long token)
            { return GridPacket(2, delegate(ZPackage p) { p.Write(token); p.Write(data.m_uid); p.Write(99L); p.Write((uint)data.OwnerRevision); }); };
            var sent = GridCapture(serverPeer); var bundle = ControlBundle(commit(normal, 1), commit(busy, 2));
            GridReceive(serverPeer, bundle);
            Check("bundled commits retain independent native-use protection", commits == 1 && normal.GetOwner() == 99 && busy.GetOwner() == 1);
            Check("two source replies share one envelope", sent.Count == 1 && ExpandControl(sent).Count == 2);
            GridReceive(serverPeer, bundle); Check("bundled token replay never repeats an owner write", commits == 1);
            int replies = sent.Count;
            var malformed = GridPacket(11, delegate(ZPackage p) { p.Write(2); p.Write(commit(third, 3)); p.Write(300); });
            GridReceive(serverPeer, malformed);
            Check("truncated or oversized child is rejected before any entry executes", third.GetOwner() == 1 && commits == 1 && sent.Count == replies);
            GridReceive(serverPeer, ControlBundle(commit(third, 3), ControlBundle(commit(third, 4), commit(third, 5))));
            Check("nested control bundles are rejected before execution", commits == 1 && third.GetOwner() == 1);
            var tooMany = new ZPackage[33]; for (int i = 0; i < tooMany.Length; i++) tooMany[i] = commit(third, i + 3);
            GridReceive(serverPeer, ControlBundle(tooMany)); Check("oversized record counts are rejected", commits == 1);
            GridReceive(peer, ControlBundle(commit(third, 3), commit(third, 4)));
            Check("other peers cannot authorize a source owner write", commits == 1 && third.GetOwner() == 1);
            GridReceive(serverPeer, GridPacket(1, delegate(ZPackage p) { p.Write(true); }));
            GridReceive(serverPeer, ControlBundle(commit(third, 3), commit(third, 4)));
            Check("unnegotiated bundles are ignored", commits == 1);
        }
        finally { ZNet.instance.Dedicated = dedicated; ZDOMan.SessionID = session; ZNet.instance.ServerPeer = null; Protocol.Stop(); }
    }
    private static void NetworkBatchGrid()
    {
        Fresh(Kinds.Pickables, defaultInteractives: true, directional: true, cellSize: 4, pattern: GridCoveragePattern.Rounded7x7);
        var peer = Peer(1); var objects = new Dictionary<ZDOID, ZDO>();
        var serverPeer = new ZNetPeer { Uid = 99, m_server = true }; ZNet.instance.Peers.Add(serverPeer);
        for (int i = 0; i < 100; i++) { ZDO data = Object(typeof(Pickable), 1, 1, 99); objects.Add(data.m_uid, data); }
        GridStart();
        try
        {
            GridReceive(peer, GridPacket(0, delegate(ZPackage p) { p.Write(65); })); var sent = GridCapture(peer);
            ZNetScene.instance.Ready = delegate { return false; }; Tick(0);
            Check("100 red-field acquisitions need no initial-load query envelopes", sent.Count == 0 && ZNetScene.instance.AreaQueries == 0);
            foreach (ZDO data in objects.Values) Check("each harvest object transfers without a preflight loading proof", data.GetOwner() == 1);
            Tick(.1f);Check("settled batch-capable owners add no readiness RPCs",sent.Count==0);
            Check("no pending batch is retained after a grid pass", ((System.Collections.ICollection)typeof(Protocol).GetField("outgoing", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Count == 0);
        }
        finally { Protocol.Stop(); }
    }
    private static void NetworkBatchSourceGrid()
    {
        Fresh(Kinds.Pickables, defaultInteractives: true, directional: true, cellSize: 4, pattern: GridCoveragePattern.Rounded7x7);
        var first = Peer(1); var second = Peer(2); var serverPeer = new ZNetPeer { Uid = 99, m_server = true };
        ZNet.instance.Peers.Add(serverPeer); GridStart();
        var objects = new List<ZDO>();
        for (int i = 0; i < 100; i++) objects.Add(Object(typeof(Pickable), 1, 1, 1));
        try
        {
            GridReceive(first, GridPacket(0, delegate(ZPackage p) { p.Write(65); }));
            GridReceive(second, GridPacket(0, delegate(ZPackage p) { p.Write(65); }));
            var requests = GridCapture(first); Tick(0);
            Check("two player footprints request only one handoff per object", requests.Count == 4 && ExpandControl(requests).Count == 100);
            ZNet.instance.Dedicated = false; ZDOMan.SessionID = 1; ZNet.instance.ServerPeer = serverPeer;
            List<ZPackage> replies;
            int commits = 0;
            try
            {
                GridReceive(serverPeer, GridPacket(1, delegate(ZPackage p) { p.Write(true); p.Write(false); p.Write(false); p.Write(true); p.Write(true); p.Write(true); }));
                typeof(Protocol).GetField("nativeCommit", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null,
                    (Action<ZRpc, ZDOID, long>)delegate(ZRpc rpc, ZDOID id, long target) { commits++; ZDOMan.instance.GetZDO(id).SetOwner(target); });
                replies = GridCapture(serverPeer);
                foreach (ZPackage packet in requests) GridReceive(serverPeer, packet);
            }
            finally { ZNet.instance.Dedicated = true; ZDOMan.SessionID = 99; ZNet.instance.ServerPeer = null; }
            Check("100 source commits return in four result envelopes", commits == 100 && replies.Count == 4 && ExpandControl(replies).Count == 100);
            foreach (ZPackage reply in replies) GridReceive(first, reply);
            Tick(.01f);
            foreach (ZDO data in objects) Check("each real source commit settles without a parked transaction", data.GetOwner() == 99 && !controller.Cooperative.Pending(data));
            Check("100 source handoffs complete using eight control RPCs", requests.Count + replies.Count == 8);
        }
        finally { Protocol.Stop(); }
    }
    private static void NetworkBatchPriorityBoundary()
    {
        Fresh(Kinds.Pickables, defaultInteractives: true, directional: true, outerDelay: 0, cellSize: 4, pattern: GridCoveragePattern.Rounded7x7);
        var first = Peer(1); var second = Peer(2); GridStart();
        try
        {
            GridReceive(first, GridPacket(0, delegate(ZPackage p) { p.Write(65); }));
            GridReceive(second, GridPacket(0, delegate(ZPackage p) { p.Write(65); }));
            ZDO red = Object(typeof(Pickable), 1, 1, 1);
            for (int i = 0; i < 40; i++) Object(typeof(Pickable), 9, 1, 1);
            var sent = GridCapture(first); Tick(0);
            Check("red tail is flushed separately before outer-field chunks", sent.Count == 3);
            sent[0].SetPos(0);
            Check("a single red request is not held to fill a yellow bundle", sent[0].ReadInt() == 2);
            sent[0].ReadLong(); Check("the first native command belongs to the red object", sent[0].ReadZDOID() == red.m_uid);
            Check("priority boundaries neither lose nor duplicate commands", ExpandControl(sent).Count == 41);
        }
        finally { Protocol.Stop(); }
    }
}
#endif
