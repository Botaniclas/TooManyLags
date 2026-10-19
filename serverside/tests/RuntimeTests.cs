using System;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class RuntimeTests
{
    private static int checks;
    private static void Check(string label, bool condition) { checks++; if (!condition) throw new Exception(label); }
    private static ZNetPeer Player(long uid, float x, float z, float yaw)
    {
        var peer = new ZNetPeer { m_uid = uid, m_characterID = new ZDOID(uid), Position = new Vector3(x, 0, z) };
        ZNet.instance.Peers.Add(peer);
        ZDOMan.instance.Data.Add(peer.m_characterID, new ZDO { Owner = uid, Position = peer.Position, Rotation = new Quaternion { Yaw = yaw } });
        return peer;
    }
    private static ZNetView View(int prefab, ZDO data)
    { return new GameObject().Add(new ZNetView { Data = data }); }
    private static void Tick(Controller controller, float now) { Time.unscaledTime = now; controller.Tick(now); controller.Service(now); }
    private static void Main()
    { try { Run(); } catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; } }
    private static void Run()
    {
        ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene();
        var door = new GameObject(); door.Add(new ZNetView()); door.Add(new Door()); ZNetScene.instance.Prefabs.Add(1, door);
        var chest = new GameObject(); chest.Add(new ZNetView()); chest.Add(new Container()); ZNetScene.instance.Prefabs.Add(2, chest);
        var custom = new GameObject(); custom.Add(new ZNetView()); custom.Add(new Door()); custom.Add(new ModdedControl()); ZNetScene.instance.Prefabs.Add(3, custom);
        var sign = new GameObject(); sign.Add(new ZNetView()); sign.Add(new Sign()); ZNetScene.instance.Prefabs.Add(4, sign);
        var movable = new GameObject(); movable.Add(new ZNetView()); movable.Add(new Door()); movable.Add(new ZSyncTransform()); ZNetScene.instance.Prefabs.Add(5, movable);
        var catalog = new Catalog((Groups)31);
        Check("exact native door qualifies", catalog.Get(1) == Groups.Doors);
        Check("disabled inventory category excluded", catalog.Get(2) == Groups.None);
        Check("unknown actions excluded even on native door", catalog.Get(3) == Groups.None);
        Check("native text excluded", catalog.Get(4) == Groups.None);
        Check("moving roots excluded", catalog.Get(5) == Groups.None);
        Check("missing prefab not cached permanently", catalog.Get(6) == Groups.None);
        ZNetScene.instance.Prefabs.Add(6, door); Check("late prefab registration retried", catalog.Get(6) == Groups.Doors);
        var mixed = new GameObject(); mixed.Add(new ZNetView()); mixed.Add(new Door()); mixed.Add(new CraftingStation()); ZNetScene.instance.Prefabs.Add(7, mixed);
        Check("disabled category on same identity prevents partial management", new Catalog(Groups.Doors).Get(7) == Groups.None);
        var config = new ConfigFile(); var options = new Settings(config); var controller = new Controller(options, 99); Plugin.Current = controller;
        Player(1, 0, 0, 0);
        var data = new ZDO { Owner = 99, Prefab = 1, Position = new Vector3(0, 0, 0) };
        var view = View(1, data); controller.Add(data, view); Tick(controller, 0);
        Check("initial server-owned door enters zero-delay inner grid", data.Owner == 1);
        int reads = data.OwnerReads, writes = data.Writes;
        for (int i = 0; i < 10000; i++) Tick(controller, .1f * i);
        Check("stable coverage has zero repeated object reads or writes", data.OwnerReads == reads && data.Writes == writes);
        Check("native zero release preserved", controller.NativeDestination(data, 0) == 0);
        Check("unrelated native destination preserved", controller.NativeDestination(data, 7) == 7);
        Check("native server destination can use mature player field", controller.NativeDestination(data, 99) == 1);
        data.User = 1; Check("active user excludes initial delegation", controller.NativeDestination(data, 99) == 99); data.User = 0;
        Player(2, 0, 0, 0); data.InUse = true; Tick(controller, 1001);
        Check("busy object does not move to overlapping server", data.Owner == 1 && controller.WaitingCells == 1);
        data.InUse = false; writes = data.Writes;
        for (int i = 0; i < 100; i++) Tick(controller, 1002 + i);
        Check("unblocked alone never retries or replays old takeover", data.Owner == 1 && data.Writes == writes);
        ZDOMan.instance.Data[new ZDOID(1)].Rotation = new Quaternion { Yaw = 45 }; Tick(controller, 1103);
        Check("new genuine heading request schedules shared server decision", data.Owner == 1 && controller.PendingTransfers == 1 && controller.WaitingCells == 0);
        Time.unscaledTime = 1103.11f; controller.Service(Time.unscaledTime);
        Check("due shared decision runs without another grid update", data.Owner == 99 && controller.PendingTransfers == 0);
        Check("cached object remains bounded", controller.TrackedObjects == 1);
        controller.Remove(data); Check("unload removes node, waiting and completed-field cache", controller.TrackedObjects == 0 && controller.WaitingCells == 0 && controller.RememberedCells == 0);
        controller.Add(data, view); Check("reload reindexes independently", controller.TrackedObjects == 1);
        var id = new ZDOID(77); data.m_uid = id; ZDOMan.instance.Data.Add(id, data); data.Owner = 1;
        var rpc = new ZRoutedRpc(); var call = new ZRoutedRpc.RoutedRPCData { m_msgID = 123, m_senderPeerID = 2, m_targetPeerID = 99, m_targetZDO = id, m_methodHash = "UseDoor".GetStableHashCode(), m_parameters = new ZPackage(new byte[] { 1, 2 }) };
        Check("stale owner request rerouted once", !RequestRouter.Handle(rpc, call) && rpc.Remote.Count == 1);
        Check("native state is submitted before the routed action", FixtureWire.Order.SequenceEqual(new[] { "ZDOData", "RoutedRPC" }));
        Check("routing preserves sender, id, payload and original header", rpc.Remote[0].m_senderPeerID == 2 && rpc.Remote[0].m_msgID == 123 && rpc.Remote[0].m_targetPeerID == 1 && rpc.Remote[0].m_parameters.GetArray()[1] == 2 && call.m_targetPeerID == 99);
        data.Owner = 99; call.m_targetPeerID = 1; Check("stale remote destination can execute server-side", !RequestRouter.Handle(rpc, call) && rpc.Local.Count == 1);
        call.m_methodHash = "RPC_OpenResponse".GetStableHashCode(); Check("responses untouched", RequestRouter.Handle(rpc, call));
        call.m_methodHash = "UseDoor".GetStableHashCode(); call.m_targetPeerID = 0; Check("broadcast untouched", RequestRouter.Handle(rpc, call));
        call.m_targetPeerID = 99; data.Owner = 1; rpc.Throw = true;
        try { RequestRouter.Handle(rpc, call); throw new Exception("expected transport failure"); } catch (Exception e) { if (e.Message != "transport fixture") throw; }
        rpc.Throw = false; Check("forwarding fence released after exception", !RequestRouter.Handle(rpc, call));
        ZNet.instance.GetPeer(1).Ready = false; Check("unready recipient retains native route", RequestRouter.Handle(rpc, call));
        controller.Remove(data); Check("all node state freed", controller.TrackedObjects == 0 && controller.WaitingCells == 0);
        config.Values["KeepOwnershipOnCellExit"] = false; controller = new Controller(new Settings(config), 99);
        ZNet.instance.GetPeer(1).Ready = true; controller.Add(data, view); Tick(controller, 0);
        ZNet.instance.Peers.Clear(); data.Owner = 0; writes = data.Writes; Tick(controller, 1);
        Check("no-player exit never reclaims a native release", data.Owner == 0 && data.Writes == writes);
        Console.WriteLine("PASS: " + checks + " production controller/catalog/routing assertions, including 10000 stable iterations and unload cleanup. Managed fixtures; no Unity gameplay claim.");
    }
}
