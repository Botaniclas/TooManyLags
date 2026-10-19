using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class HandoffTests
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    private static GameObject Prefab(int hash, params MonoBehaviour[] controls)
    { var root = new GameObject(); root.Add(new ZNetView()); foreach (var c in controls) root.Add(c); ZNetScene.instance.Prefabs.Add(hash, root); return root; }
    private static ZNetPeer Player(long uid)
    {
        var peer = new ZNetPeer { m_uid = uid, m_characterID = new ZDOID(uid) };
        ZNet.instance.Peers.Add(peer); ZDOMan.instance.Data.Add(peer.m_characterID, new ZDO { Owner = uid }); return peer;
    }
    private static ZNetView View(ZDO data, MonoBehaviour control)
    { var root = new GameObject(); var view = root.Add(new ZNetView { Data = data }); root.Add(control); ZDOMan.instance.Data[data.m_uid] = data; return view; }
    private static void Tick(Controller controller, float now)
    { Time.unscaledTime = now; controller.Tick(now); controller.Service(now); }
    private static int Main()
    { try { Run(); return 0; } catch (Exception error) { Console.Error.WriteLine(error); return 1; } }
    private static void Run()
    {
        Heap();
        ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene();
        Prefab(1, new Beehive()); Prefab(2, new Pickable()); Prefab(3, new CraftingStation()); Prefab(4, new Sign());
        Prefab(5, new TeleportWorld()); Prefab(6, new MapTable(), new Switch()); Prefab(7, new ShieldGenerator(), new Switch());
        Prefab(8, new Switch()); Prefab(9, new PickableItem()); Prefab(10, new Smelter(), new Switch());
        var config = new ConfigFile(); config.Values["LargeGridPingThresholdMs"] = 0;
        var options = new Settings(config); var catalog = new Catalog(options.Groups);
        Check("standard collector enabled", catalog.Get(1) == Groups.Collectors);
        Check("map switch tied to known native host", catalog.Get(6) == Groups.MapTables);
        Check("shield refill switch tied to known native host", catalog.Get(7) == Groups.RefillStations);
        Check("unknown switch callback stays native", catalog.Get(8) == Groups.None);
        Check("sign and portal editing stay entirely outside addon management", catalog.Get(4) == Groups.None && catalog.Get(5) == Groups.None);
        Check("local UI category is separate from grid interactions", Catalog.LocalOnly(catalog.Get(3)) && !Catalog.NeedsGrace(catalog.Get(3)));
        Check("smelter controls recognized", catalog.Get(10) == Groups.RefillStations);
        Check("dependency-reserved controls remain outside management", new Catalog(options.Groups, hash => hash == 10).Get(10) == Groups.None);
        var a = Player(1); var b = Player(2);
        var controller = new Controller(options, 99); Plugin.Current = controller;
        var hive = new ZDO { Prefab = 1, Owner = 1 }; var hiveView = View(hive, new Beehive()); controller.Add(hive, hiveView);
        Tick(controller, 0);
        Check("A retains immediate local use while takeover is pending", hive.Owner == 1 && controller.PendingTransfers == 1);
        Tick(controller, .02f); Check("stationary tick does not restart pending deadline", controller.PendingTransfers == 1 && hive.Owner == 1);
        var hooks = new Harmony("fixture.serveronly.receive");
        try
        {
            hooks.Patch(AccessTools.Method(typeof(ZDOMan), "RPC_ZDOData"), prefix: new HarmonyMethod(typeof(SourceReceive), "Prefix"), finalizer: new HarmonyMethod(typeof(SourceReceive), "Finalizer"));
            hooks.Patch(AccessTools.Method(typeof(ZDO), "Deserialize"), postfix: new HarmonyMethod(typeof(SourceObject), "Postfix"));
            ZDOMan.instance.Receiving = hive;
            ZDOMan.instance.Receive(b.m_rpc, new ZPackage());
            Check("another peer cannot cancel A's pending source transfer", controller.PendingTransfers == 1);
            ZDOMan.instance.Receive(a.m_rpc, new ZPackage());
            Check("actual native source-data hook cancels takeover", controller.PendingTransfers == 0 && hive.Owner == 1 && controller.WaitingCells == 1);
            Tick(controller, 1); Tick(controller, 2);
            Check("cancelled takeover never executes just because time passed", controller.PendingTransfers == 0 && hive.Owner == 1);
            ZDOMan.instance.Data[a.m_characterID].Rotation = new Quaternion { Yaw = 45 }; Tick(controller, 2.1f);
            Check("fresh heading activity can request a cancelled transfer again", controller.PendingTransfers == 1);
            controller.Service(2.21f); Check("due takeover runs without another movement check", controller.PendingTransfers == 0 && hive.Owner == 99);
            // Incoming parsing errors must not leak the source identity to later operations.
            ZDOMan.instance.Data[a.m_characterID].Rotation = new Quaternion { Yaw = 90 }; hive.Owner = 1; Tick(controller, 3);
            hive.ThrowDeserialize = true;
            try { ZDOMan.instance.Receive(a.m_rpc, new ZPackage()); } catch (Exception error) { if (error.Message != "receive fixture") throw; }
            Check("receive source context restored after parsing failure", SourceReceive.Peer == 0);
            hive.ThrowDeserialize = false;
        }
        finally { hooks.UnpatchSelf(); }
        controller.Remove(hive); Check("unload removes pending grace immediately", controller.PendingTransfers == 0 && controller.WaitingCells == 0);
        var berry = new ZDO { Prefab = 2, Owner = 1 }; controller.Add(berry, View(berry, new Pickable())); Tick(controller, 4);
        Check("native picked-completion group does not allocate grace", berry.Owner == 99 && controller.PendingTransfers == 0);
        var station = new ZDO { Prefab = 3, Owner = 1 }; controller.Add(station, View(station, new CraftingStation())); Tick(controller, 4.1f);
        Check("local station goes to server even inside player fields", station.Owner == 99 && controller.PendingTransfers == 0);
        int reads = station.OwnerReads, writes = station.Writes;
        ZDOMan.instance.Data[a.m_characterID].Rotation = new Quaternion { Yaw = 135 }; Tick(controller, 5);
        Check("grid movement never enumerates local-only station", station.OwnerReads == reads && station.Writes == writes);
        var sign = new ZDO { Prefab = 4, Owner = 1 }; controller.Add(sign, View(sign, new Sign()));
        var portal = new ZDO { Prefab = 5, Owner = 1 }; controller.Add(portal, View(portal, new TeleportWorld())); Tick(controller, 6);
        Check("no takeover introduced during sign or portal input", sign.Owner == 1 && sign.Writes == 0 && portal.Owner == 1 && portal.Writes == 0);
        // A single stale server-addressed command receives native state first, with no queue or timer.
        berry.Owner = 1; berry.OwnerRevision = 12; berry.DataRevision = 34;
        var rpc = new ZRoutedRpc(); var request = new ZRoutedRpc.RoutedRPCData { m_msgID = 999, m_senderPeerID = 1, m_targetPeerID = 99, m_targetZDO = berry.m_uid, m_methodHash = "RPC_Pick".GetStableHashCode(), m_parameters = new ZPackage(new byte[] { 7 }) };
        FixtureWire.Order.Clear(); Check("stale harvest routes without grace", !RequestRouter.Handle(rpc, request) && controller.PendingTransfers == 0);
        Check("state and original action share ordered submission", FixtureWire.Order.SequenceEqual(new[] { "ZDOData", "RoutedRPC" }));
        var state = new ZPackage(a.m_rpc.Sent.Last().GetArray());
        Check("snapshot native header preserves object and revision types", state.ReadInt() == 0 && state.ReadLong() == berry.m_uid.Id && state.ReadUShort() == 12 && state.ReadUInt() == 34 && state.ReadLong() == 1);
        Check("original command metadata and bonus untouched", rpc.Remote[0].m_msgID == 999 && rpc.Remote[0].m_senderPeerID == 1 && rpc.Remote[0].m_parameters.GetArray()[0] == 7 && request.m_targetPeerID == 99);
        var oneShot = new ZDO { Prefab = 9, Owner = 1 }; var pickup = new PickableItem(); controller.Add(oneShot, View(oneShot, pickup)); pickup.Complete();
        request.m_targetZDO = oneShot.m_uid; request.m_methodHash = "Pick".GetStableHashCode(); int commands = rpc.Remote.Count;
        Check("completed one-shot drop is never forwarded a second time", !RequestRouter.Handle(rpc, request) && rpc.Remote.Count == commands);
        Check("normal source RTT is cached across nearby jobs", ((FixtureSocket)a.m_socket).Reads <= 4);
        ZNet.instance.Peers.Clear(); Tick(controller, 7);
        Check("last disconnect clears all pending transfers", controller.PendingTransfers == 0);
        Console.WriteLine("PASS: " + checks + " heap, ordered-state routing, native Harmony cancellation, text/local exclusions and cleanup assertions. Managed fixtures; no live gameplay claim.");
    }
    private static void Heap()
    {
        var queue = new DueQueue<object>(); var reference = new Dictionary<object, float>(); var rng = new Random(710);
        var keys = Enumerable.Range(0, 100).Select(i => new object()).ToArray();
        for (int i = 0; i < 5000; i++)
        {
            object key = keys[rng.Next(keys.Length)]; float due = rng.Next(100);
            if (rng.Next(2) == 0) { queue.Add(key, due); if (!reference.ContainsKey(key)) reference.Add(key, due); }
            else { Check("indexed cancellation matches reference", queue.Remove(key) == reference.Remove(key)); }
            Check("heap never retains cancelled historical entries", queue.Count == reference.Count);
        }
        float previous = -1; object value;
        while (queue.Take(100, out value)) { Check("due order is monotonic", reference[value] >= previous); previous = reference[value]; reference.Remove(value); }
        Check("drained heap releases all keys", queue.Count == 0 && reference.Count == 0);
    }
}
