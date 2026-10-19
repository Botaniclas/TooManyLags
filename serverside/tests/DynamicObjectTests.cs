using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class DynamicObjectTests
{
    private static int checks;
    private static void Check(string text, bool ok) { checks++; if (!ok) throw new Exception(text); }
    private static void Prefab(int hash, params MonoBehaviour[] parts)
    { var root = new GameObject(); root.Add(new ZNetView()); foreach (var part in parts) root.Add(part); ZNetScene.instance.Prefabs.Add(hash, root); }
    private static Controller Setup(ConfigFile config = null)
    {
        Time.unscaledTime = 0; ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene();
        Prefab(1, new Door()); Prefab(2, new Piece(), new WearNTear()); Prefab(3, new Character(), new MonsterAI(), new Tameable());
        Prefab(4, new ItemDrop()); Prefab(5, new Fish()); Prefab(6, new Fish(), new ItemDrop());
        Prefab(7, new Character { NativeFaction = Character.Faction.Players }, new MonsterAI(), new Tameable());
        Prefab(8, new Piece(), new WearNTear(), new ModdedControl()); Prefab(9, new Player(), new MonsterAI(), new Tameable());
        Prefab(10, new Character(), new MonsterAI(), new Tameable(), new Sadle());
        AddPeer(1, 0); AddPeer(2, 80);
        var controller = new Controller(new Settings(config ?? new ConfigFile()), 99); Plugin.Current = controller; return controller;
    }
    private static void AddPeer(long uid, float x)
    {
        var peer = new ZNetPeer { m_uid = uid, m_characterID = new ZDOID(uid), Position = new Vector3(x, 0, 0) }; ZNet.instance.Peers.Add(peer);
        ZDOMan.instance.Data.Add(peer.m_characterID, new ZDO { Owner = uid, Position = peer.Position });
    }
    private static ZNetView Add(Controller controller, ZDO data, params MonoBehaviour[] parts)
    {
        var root = new GameObject(); var view = root.Add(new ZNetView { Data = data }); foreach (var part in parts) root.Add(part);
        ZDOMan.instance.Data.Add(data.m_uid, data); controller.Add(data, view); return view;
    }
    private static void Tick(Controller controller, float now) { Time.unscaledTime = now; controller.Tick(now); controller.Service(now); }
    private static void NativeHooks(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ZDO), "SetOwnerInternal"), postfix: new HarmonyMethod(typeof(OwnershipChanged), "Postfix"));
        harmony.Patch(AccessTools.Method(typeof(ZDO), "InternalSetPosition"), postfix: new HarmonyMethod(typeof(ObjectMoved), "Postfix"));
        harmony.Patch(AccessTools.Method(typeof(ZDO), "Set", new[] { typeof(int), typeof(bool) }), postfix: new HarmonyMethod(typeof(AnimalFlagChanged), "Postfix"));
        harmony.Patch(AccessTools.Method(typeof(ZDO), "Set", new[] { typeof(int), typeof(long) }), postfix: new HarmonyMethod(typeof(AnimalUserChanged), "Postfix"));
        harmony.Patch(AccessTools.Method(typeof(ZDO), "Set", new[] { typeof(int), typeof(string) }), postfix: new HarmonyMethod(typeof(AnimalFollowChanged), "Postfix"));
    }
    private static void StaticAndCache()
    {
        var controller = Setup(); var catalog = new Catalog(new Settings(new ConfigFile()).Groups);
        Check("static Piece/WearNTear qualifies", catalog.Get(2) == Groups.StaticBuildings);
        Check("unknown interactive static mix stays native", catalog.Get(8) == Groups.None);
        var building = new ZDO { Prefab = 2, Owner = 99 }; Add(controller, building, new Piece(), new WearNTear()); Tick(controller, 0);
        Check("static defaults remain server-owned inside red player field", building.Owner == 99);
        int reads = building.OwnerReads;
        for (int i = 1; i < 1000; i++) { ZDOMan.instance.Data[new ZDOID(1)].Rotation = new Quaternion { Yaw = (i % 8) * 45 }; Tick(controller, i * .1f); }
        Check("moving directional grid never enumerates fixed static objects", building.OwnerReads == reads);
        building.SetOwner(1); Tick(controller, 101); Check("external static drift starts protected server return", controller.PendingTransfers == 1);
        controller.Service(101.11f); Check("static default recovers through existing due scheduler", building.Owner == 99);

        var config = new ConfigFile(); config.Values["UseGridForStaticBuildings"] = true; controller = Setup(config);
        building = new ZDO { Prefab = 2, Owner = 99 }; Add(controller, building, new Piece(), new WearNTear()); Tick(controller, 0);
        Check("optional static grid delegates red object to nearby player", building.Owner == 1);
        building.SetOwnerInternal(2); Tick(controller, .1f);
        Check("native owner event repairs settled static cache without movement", building.Owner == 1);
        var door = new ZDO { Prefab = 1, Owner = 99 }; Add(controller, door, new Door()); Tick(controller, .2f);
        door.SetOwnerInternal(2); Tick(controller, .3f);
        Check("same event-driven repair closes confirmed door ownership drift", door.Owner == 1);
        door.SetOwnerInternal(0); Tick(controller, .4f); Tick(controller, 1);
        Check("native zero-owner region release is never immediately reclaimed", door.Owner == 0);
        door.SetOwnerInternal(99); Tick(controller, 2);
        Check("native region reactivation restores grid participation", door.Owner == 1);
    }
    private static void Animals()
    {
        var controller = Setup(); var settings = new Settings(new ConfigFile()); var catalog = new Catalog(settings.Groups);
        Check("friendly magic summons excluded", catalog.Get(7) == Groups.None);
        Check("players excluded", catalog.Get(9) == Groups.None);
        Check("native riding saddle does not exclude idle rideable animals", catalog.Get(10) == Groups.TamedAnimals);
        var animal = new ZDO { Prefab = 3, Owner = 99 }; Add(controller, animal, new Character(), new MonsterAI(), new Tameable()); Tick(controller, 0);
        Check("wild tameable candidate retains native server owner", animal.Owner == 99);
        animal.Set(ZDOVars.s_tamed, true); Tick(controller, .1f);
        Check("native tame flag activates grid without region reload", animal.Owner == 1);
        animal.InternalSetPosition(new Vector3(80, 0, 0)); Tick(controller, .2f);
        Check("movement alone reindexes animal to stationary second player's field", animal.Owner == 2);
        animal.Set(ZDOVars.s_follow, "Follower"); Tick(controller, .3f);
        animal.InternalSetPosition(new Vector3(0, 0, 0)); Tick(controller, .4f);
        Check("following preserves native owner outside requested grid", animal.Owner == 2);
        animal.Set(ZDOVars.s_follow, ""); Tick(controller, .5f);
        Check("ending native follow reactivates current cell", animal.Owner == 1);
        animal.Set(ZDOVars.s_alert, true); Tick(controller, .6f);
        animal.InternalSetPosition(new Vector3(80, 0, 0)); Tick(controller, .7f);
        Check("alerted animal cannot be transferred", animal.Owner == 1);
        animal.Set(ZDOVars.s_alert, false); Tick(controller, .8f);
        Check("ending alert reindexes current pose", animal.Owner == 2);
        animal.Parent = new ZDOID(123); controller.DataReceived(animal, 2); Tick(controller, .9f);
        animal.InternalSetPosition(new Vector3(0, 0, 0)); Tick(controller, 1);
        Check("transport parent connection protects animals even without attach string", animal.Owner == 2);
        animal.Parent = ZDOID.None; controller.DataReceived(animal, 2); Tick(controller, 1.1f);
        Check("unparenting reactivates eligible animal", animal.Owner == 1);
        animal.Tamed = false; controller.DataReceived(animal, 1); Tick(controller, 1.2f);
        animal.InternalSetPosition(new Vector3(80, 0, 0)); Tick(controller, 1.3f);
        Check("untaming withdraws dynamic management", animal.Owner == 1);

        controller = Setup(); animal = new ZDO { Prefab = 3, Owner = 1, Tamed = true, Position = new Vector3(0, 0, 20) };
        Add(controller, animal, new Character(), new MonsterAI(), new Tameable()); ZNetScene.instance.AreaReady = false; Tick(controller, 0);
        Check("server takeover waits for native loaded-area readiness", animal.Owner == 1 && controller.PendingTransfers == 0);
        int areaReads = ZNetScene.instance.AreaChecks; for (int i = 1; i < 100; i++) Tick(controller, i * .1f);
        Check("blocked animal never repeatedly scans native area", ZNetScene.instance.AreaChecks == areaReads);
        ZNetScene.instance.AreaReady = true; animal.InternalSetPosition(new Vector3(4, 0, 20)); Tick(controller, 11); Tick(controller, 14); controller.Service(14.11f);
        Check("fresh mobile cell event can resume safe server takeover", animal.Owner == 99);
    }
    private static ZRoutedRpc.RoutedRPCData Request(ZDO data, string method, long target = 99)
    { return new ZRoutedRpc.RoutedRPCData { m_msgID = 1, m_senderPeerID = 1, m_targetPeerID = target, m_targetZDO = data.m_uid, m_methodHash = method.GetStableHashCode(), m_parameters = new ZPackage() }; }
    private static void PickupsAndControl()
    {
        var controller = Setup();
        var item = new ZDO { Prefab = 4, Owner = 99 }; Add(controller, item, new ItemDrop()); Tick(controller, 0);
        Check("server item delegates early to near player", item.Owner == 1);
        item.InternalSetPosition(new Vector3(80, 0, 0)); Tick(controller, .1f); Tick(controller, 5);
        Check("item moving beyond its owner's footprint can enter another player's grid", item.Owner == 2);
        var catalog = new Catalog(new Settings(new ConfigFile()).Groups);
        Check("fish with ItemDrop has one loose-item policy", catalog.Get(6) == Groups.LooseItems);
        var fish = new ZDO { Prefab = 5, Owner = 99, Position = new Vector3(0, 0, 20) }; Add(controller, fish, new Fish()); Tick(controller, 6);
        var rpc = new ZRoutedRpc(); var request = Request(fish, "RequestPickup");
        Check("native fish pickup request retains native dispatch", RequestRouter.Handle(rpc, request));
        ZDOMan.instance.Data[new ZDOID(1)].Position = fish.Position; Tick(controller, 7);
        Check("server-owned fish with an in-flight grant stays pinned", fish.Owner == 99);
        request.m_methodHash = "Pickup".GetStableHashCode(); request.m_targetPeerID = 1;
        Check("fish pickup response never routes to object owner", RequestRouter.Handle(rpc, request) && rpc.Remote.Count == 0);
        var dropped = new ZDO { Prefab = 4, Owner = 99, Position = new Vector3(0, 0, 20) }; Add(controller, dropped, new ItemDrop());
        var own = Request(dropped, "RPC_RequestOwn"); RequestRouter.Handle(rpc, own); Tick(controller, 8);
        Check("native item request pin survives while owner still server", dropped.Owner == 99);
        var hooked = new ZDO { Prefab = 5, Owner = 99, Hooked = 1, Position = fish.Position }; Add(controller, hooked, new Fish()); Tick(controller, 9);
        Check("hooked fish remains on native ownership path", hooked.Owner == 99);
        var converted = new ZDO { Prefab = 4, Owner = 99, MadePiece = true, Position = fish.Position }; Add(controller, converted, new ItemDrop { Converted = true }); Tick(controller, 10);
        Check("converted item pieces are not delegated as drops", converted.Owner == 99);

        foreach (ZDO data in new[] { item, fish, dropped, hooked, converted }) controller.Remove(data);
        Check("unload clears pickup pins and all loaded pickup nodes", controller.TrackedObjects == 0 && controller.WaitingCells == 0 && controller.PendingTransfers == 0);
        item.InternalSetPosition(new Vector3(0, 0, 0)); fish.SetOwner(2); Tick(controller, 11);
        Check("events from unloaded pickups cannot recreate managed nodes", controller.TrackedObjects == 0 && item.Owner == 2 && fish.Owner == 2);

        controller = Setup(); var animal = new ZDO { Prefab = 3, Owner = 99, Tamed = true }; Add(controller, animal, new Character(), new MonsterAI(), new Tameable());
        var control = Request(animal, "RequestControl"); RequestRouter.Handle(rpc, control); Tick(controller, 0);
        Check("riding request withdraws animal before native grant completes", animal.Owner == 99);
        var unrelatedReply = Request(animal, "RequestRespons", 2);
        Check("unrelated recipient reply is never parsed or altered", RequestRouter.Handle(rpc, unrelatedReply));
        var accepted = Request(animal, "RequestRespons", 1); accepted.m_parameters.Write(true);
        Check("expected native rider reply keeps its recipient and dispatch", RequestRouter.Handle(rpc, accepted) && accepted.m_targetPeerID == 1);
        animal.Set(ZDOVars.s_user, 100L); animal.SetOwner(2); Tick(controller, .1f);
        Check("native rider grant always wins over player-one grid", animal.Owner == 2);
        animal.Set(ZDOVars.s_user, 0L); Tick(controller, .2f);
        Check("observed dismount reactivates normal grid", animal.Owner == 1);
        controller.PinControl(animal); controller.ControlReply(animal, false); animal.SetOwner(99); Tick(controller, .3f);
        Check("denied control request safely releases its pin", animal.Owner == 1);
        var response = Request(animal, "RequestRespons", 1); response.m_parameters.Write(true);
        Check("rider response target is never rewritten", RequestRouter.Handle(rpc, response) && response.m_targetPeerID == 1);
        controller.PinControl(animal, 1); controller.ControlReply(animal, true, 1);
        controller.PinControl(animal, 2); controller.ControlReply(animal, false, 2);
        Check("pending accepted rider also vetoes early native grid delegation", controller.NativeDestination(animal, 99) == 99);
        animal.SetOwner(99); Tick(controller, .4f);
        Check("another player's denial cannot clear an accepted in-flight rider pin", animal.Owner == 99);
        animal.Set(ZDOVars.s_user, 100L); Tick(controller, .5f); animal.Set(ZDOVars.s_user, 0L); Tick(controller, .6f);
        Check("accepted rider release clears only its completed control pin", animal.Owner == 1);
        controller.PinControl(animal, 1); controller.Remove(animal); animal.SetOwner(99); Tick(controller, .7f);
        Check("unload removes pending riding state without reassigning the animal", controller.TrackedObjects == 0 && controller.WaitingCells == 0 && controller.PendingTransfers == 0 && animal.Owner == 99);
    }
    private static void MovePeer(long uid, float x)
    {
        ZNetPeer peer = ZNet.instance.GetPeer(uid);
        peer.Position = new Vector3(x, 0, 0);
        ZDOMan.instance.Data[peer.m_characterID].Position = peer.Position;
    }
    private static void LooseItemExits()
    {
        var controller = Setup();
        var item = new ZDO { Prefab = 4, Owner = 1 }; Add(controller, item, new ItemDrop()); Tick(controller, 0);
        Check("already client-owned drop participates in its player's grid", item.Owner == 1);
        int reads = item.OwnerReads, writes = item.Writes;
        for (int i = 1; i <= 1000; i++) Tick(controller, i * .1f);
        Check("stationary client drop has no repeated object work", item.OwnerReads == reads && item.Writes == writes);
        MovePeer(1, 20); Tick(controller, 101); Tick(controller, 104.1f);
        Check("vacated player-owned drop uses existing protected server return", item.Owner == 1 && controller.PendingTransfers == 1);
        controller.Service(104.21f);
        Check("ordinary assigned drop no longer has a lifetime owner pin", item.Owner == 99 && controller.PendingTransfers == 0);

        controller = Setup(); item = new ZDO { Prefab = 4, Owner = 1 }; Add(controller, item, new ItemDrop()); Tick(controller, 0);
        var rpc = new ZRoutedRpc(); var request = Request(item, "RPC_RequestOwn", 1);
        Check("failed pickup keeps original native request dispatch", RequestRouter.Handle(rpc, request));
        reads = item.OwnerReads; writes = item.Writes;
        for (int i = 1; i <= 1000; i++) Tick(controller, i * .1f);
        Check("request protection neither polls nor expires while requester stays", item.Owner == 1 && item.OwnerReads == reads && item.Writes == writes && rpc.Remote.Count == 0);
        MovePeer(1, 20); Tick(controller, 101); Tick(controller, 104.1f); controller.Service(104.21f);
        Check("failed pickup protection ends on requester grid exit", item.Owner == 99 && controller.PendingTransfers == 0);
        Check("request exit never replays original pickup", rpc.Local.Count == 0 && rpc.Remote.Count == 0);

        controller = Setup(); item = new ZDO { Prefab = 4, Owner = 1 }; Add(controller, item, new ItemDrop()); Tick(controller, 0);
        MovePeer(2, 0); Tick(controller, .1f);
        Check("overlap cannot interrupt invisible pickup at nearby client owner", item.Owner == 1 && controller.PendingTransfers == 0);
        RequestRouter.Handle(new ZRoutedRpc(), Request(item, "RPC_RequestOwn", 1));
        MovePeer(1, 20); Tick(controller, .2f); Tick(controller, 1.3f);
        MovePeer(2, 4); Tick(controller, 1.4f);
        Check("remaining player can acquire drop after old pickup owner leaves", item.Owner == 2);

        controller = Setup(); item = new ZDO { Prefab = 4, Owner = 99 }; Add(controller, item, new ItemDrop());
        RequestRouter.Handle(new ZRoutedRpc(), Request(item, "RPC_RequestOwn")); Tick(controller, 0);
        Check("pending native request protects server-owned item from preemptive grant", item.Owner == 99 && controller.PendingTransfers == 0);
        MovePeer(1, 20); Tick(controller, .1f); Tick(controller, 3.2f);
        MovePeer(2, 0); Tick(controller, 4);
        Check("failed ungranted request cannot pin server item against next player", item.Owner == 2);

        controller = Setup(); MovePeer(2, 0); item = new ZDO { Prefab = 4, Owner = 99 }; Add(controller, item, new ItemDrop()); Tick(controller, 0);
        RequestRouter.Handle(new ZRoutedRpc(), Request(item, "RPC_RequestOwn"));
        request = Request(item, "RPC_RequestOwn"); request.m_senderPeerID = 2; RequestRouter.Handle(new ZRoutedRpc(), request);
        MovePeer(1, 20); Tick(controller, .1f); Tick(controller, 1.2f);
        Check("one departing requester cannot release another nearby request", item.Owner == 99 && controller.PendingTransfers == 0);
        MovePeer(2, 40); Tick(controller, 1.3f); Tick(controller, 4.4f);
        MovePeer(1, 0); Tick(controller, 5);
        Check("all departed requests release on later coverage without timer cache", item.Owner == 1);

        controller = Setup(); item = new ZDO { Prefab = 4, Owner = 99 }; Add(controller, item, new ItemDrop());
        RequestRouter.Handle(new ZRoutedRpc(), Request(item, "RPC_RequestOwn")); Tick(controller, 0);
        MovePeer(1, 20); Tick(controller, .1f); MovePeer(1, 0); Tick(controller, .2f);
        Check("rapid same-player re-entry cannot revive an old failed request", item.Owner == 1);

        controller = Setup(); item = new ZDO { Prefab = 4, Owner = 99 }; Add(controller, item, new ItemDrop());
        RequestRouter.Handle(new ZRoutedRpc(), Request(item, "RPC_RequestOwn")); Tick(controller, 0);
        item.InternalSetPosition(new Vector3(4, 0, 0)); Tick(controller, .1f);
        Check("moving item retains pending requester protection inside footprint", item.Owner == 99 && controller.PendingTransfers == 0);
        MovePeer(1, 20); Tick(controller, .2f); Tick(controller, 3.3f);
        MovePeer(2, 4); Tick(controller, 4);
        Check("moving item releases old request in its new cell on departure", item.Owner == 2);

        controller = Setup(); item = new ZDO { Prefab = 4, Owner = 1 }; Add(controller, item, new ItemDrop()); Tick(controller, 0);
        RequestRouter.Handle(new ZRoutedRpc(), Request(item, "RPC_RequestOwn", 1));
        ZNet.instance.Peers.Remove(ZNet.instance.GetPeer(1)); Tick(controller, .1f); Tick(controller, 3.2f);
        Check("disconnected pickup owner returns without requiring unavailable RTT", item.Owner == 99 && controller.PendingTransfers == 0);

        controller = Setup(); item = new ZDO { Prefab = 4, Owner = 1 }; Add(controller, item, new ItemDrop()); Tick(controller, 0);
        RequestRouter.Handle(new ZRoutedRpc(), Request(item, "RPC_RequestOwn", 1));
        MovePeer(1, 1000); Tick(controller, .1f); Tick(controller, 3.2f);
        Check("teleport releases failed pickup without source-area RTT job", item.Owner == 99 && controller.PendingTransfers == 0);

        var cfg = new ConfigFile(); cfg.Values["KeepOwnershipOnCellExit"] = true;
        controller = Setup(cfg); item = new ZDO { Prefab = 4, Owner = 1 }; Add(controller, item, new ItemDrop()); Tick(controller, 0);
        RequestRouter.Handle(new ZRoutedRpc(), Request(item, "RPC_RequestOwn", 1));
        MovePeer(1, 20); Tick(controller, .1f); Tick(controller, 4);
        Check("explicit exit retention still keeps a vacated drop's owner", item.Owner == 1);
        MovePeer(2, 0); Tick(controller, 5);
        Check("retained drop can still change owner on a different player's entry", item.Owner == 2);
        controller.Remove(item);
        Check("unload removes item grid and request state", controller.TrackedObjects == 0 && controller.WaitingCells == 0 && controller.PendingTransfers == 0);

        controller = Setup(); item = new ZDO { Prefab = 4, Owner = 99 }; Add(controller, item, new ItemDrop()); Tick(controller, 0);
        item.SetOwnerInternal(0); Tick(controller, .1f);
        Check("native zero-owner release still withdraws loose item", item.Owner == 0);
        item.SetOwnerInternal(99); Tick(controller, .2f);
        Check("native reactivation resumes loose-item grid without reload", item.Owner == 1);
    }
    private static int Main()
    {
        var harmony = new Harmony("fixture.dynamicobjects");
        try { NativeHooks(harmony); StaticAndCache(); Animals(); PickupsAndControl(); LooseItemExits(); Console.WriteLine("PASS: " + checks + " static-default/grid, owner-drift/native-release, tame/movement/use, pickup/grant/exit and stable-cost assertions. Real Harmony hooks with managed fixtures; no live gameplay claim."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { harmony.UnpatchSelf(); Plugin.Reset(); }
    }
}
