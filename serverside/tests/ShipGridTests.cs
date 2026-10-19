using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class ShipGridTests
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    private static void Tick(Controller c, float now) { Time.unscaledTime = now; c.Tick(now); c.Service(now); }
    private static ZNetPeer Player(long uid, float x, float z = 0)
    {
        var peer = new ZNetPeer { m_uid = uid, m_characterID = new ZDOID(uid), Position = new Vector3(x, 0, z) };
        ZNet.instance.Peers.Add(peer);
        ZDOMan.instance.Data[peer.m_characterID] = new ZDO { Owner = uid, Position = peer.Position }; return peer;
    }
    private static void Move(ZNetPeer peer, float x, float z = 0)
    { peer.Position = new Vector3(x, 0, z); ZDOMan.instance.Data[peer.m_characterID].Position = peer.Position; }
    private static GameObject Boat(bool cargo)
    {
        var root = new GameObject(); var view = root.Add(new ZNetView()); var ship = root.Add(new Ship());
        ship.m_shipControlls = root.Add(new ShipControlls { m_ship = ship, OwnTransform = new Transform() });
        root.Add(new Rigidbody { isKinematic = false, useGravity = true }); root.Add(new ZSyncTransform());
        root.Add(new Chair()); root.Add(new Ladder());
        if (cargo) root.Add(new Container { m_rootObjectOverride = view }); return root;
    }
    private static Controller Reset(bool cargo = true, bool keep = false, bool enabled = true)
    {
        Time.unscaledTime = 0; ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene();
        ZNetScene.instance.Prefabs[10] = Boat(cargo);
        var config = new ConfigFile(); config.Values["KeepOwnershipOnCellExit"] = keep;
        config.Values["Ships"] = enabled; config.Values["ServerOverlapCooldownSeconds"] = 0f;
        var c = new Controller(new Settings(config), 99); Plugin.Current = c; return c;
    }
    private static ZNetView Loaded(Controller c, long owner, bool cargo = true, float x = 0, float helm = 0)
    {
        var root = Boat(cargo); var view = root.GetComponent<ZNetView>();
        view.Data = new ZDO { m_uid = new ZDOID(10000 + checks), Prefab = 10, Owner = owner, OwnerRevision = 7, Position = new Vector3(x, 0, 0) };
        root.transform.position = view.Data.Position;
        root.GetComponent<Ship>().m_shipControlls.transform.position = new Vector3(x + helm, 0, 0);
        ZDOMan.instance.Data[view.Data.m_uid] = view.Data; c.Add(view.Data, view); return view;
    }
    private static void Reply(Controller c, ZNetView view, long sender, bool allowed)
    {
        var payload = new ZPackage(); payload.Write(allowed);
        Check("inventory reply stays native", RequestRouter.Handle(new ZRoutedRpc(), new ZRoutedRpc.RoutedRPCData {
            m_targetPeerID = 99, m_senderPeerID = sender, m_targetZDO = view.Data.m_uid,
            m_methodHash = "RPC_OpenResponse".GetStableHashCode(), m_parameters = payload }));
    }
    private static void Run()
    {
        var c = Reset(); var first = Player(1, 0); var view = Loaded(c, 99); var data = view.Data;
        var catalog = new Catalog((Groups)(-1));
        Check("native ship with shared public cargo classified", catalog.Get(10) == Groups.Ships);
        ZNetScene.instance.Prefabs[11] = Boat(false); Check("raft without inventory classified", catalog.Get(11) == Groups.Ships);
        var custom = Boat(true); custom.Add(new ModdedControl()); ZNetScene.instance.Prefabs[12] = custom;
        Check("unknown custom ship action excluded", catalog.Get(12) == Groups.None);
        var privateCargo = Boat(true); privateCargo.GetComponent<Container>().m_privacy = Container.PrivacySetting.Private;
        ZNetScene.instance.Prefabs[13] = privateCargo; Check("private cargo excluded", catalog.Get(13) == Groups.None);
        var different = Boat(true); different.GetComponent<Container>().m_rootObjectOverride = new GameObject().Add(new ZNetView());
        ZNetScene.instance.Prefabs[14] = different; Check("independent cargo identity excluded", catalog.Get(14) == Groups.None);
        var missing = Boat(false); missing.GetComponent<Ship>().m_shipControlls = null;
        ZNetScene.instance.Prefabs[15] = missing; Check("missing helm excluded", catalog.Get(15) == Groups.None);
        var twice = Boat(true); twice.Add(new Container()); ZNetScene.instance.Prefabs[16] = twice;
        Check("multiple inventories excluded", catalog.Get(16) == Groups.None);
        Check("ship can be disabled independently", new Catalog(Groups.Containers).Get(10) == Groups.None);
        Tick(c, 0); Check("solo player acquires whole boat without inventory opening", data.Owner == 1 && view.Invocations == 0);
        Check("ship cargo and physics share one managed identity", c.TrackedObjects == 1 && c.ManagesShip(data));
        Check("native bootstrap cannot bypass existing boat owner", c.NativeDestination(data, 99) == 99);
        int reads = data.OwnerReads, writes = data.Writes, ready = ZNetScene.instance.AreaChecks;
        for (int i = 1; i <= 1000; i++) Tick(c, i * .1f);
        Check("stationary boat causes no repeated object reads writes requests or area checks", data.OwnerReads == reads && data.Writes == writes && view.Invocations == 0 && ZNetScene.instance.AreaChecks == ready);
        data.User = 1234; // Profile of helmsman, not network owner.
        var second = Player(2, 0); bool open = true;
        view.Invoke = delegate(long target, string name, object[] args)
        {
            Check("ship takeover asks source native inventory", target == 1 && name == "RPC_RequestOpen" && (long)args[0] == 0);
            if (open) Reply(c, view, 1, false);
            else { data.SetOwner(99); c.OwnerAssigned(data, 99); Reply(c, view, 1, true); }
        };
        Tick(c, 101); Check("open cargo vetoes overlap even while steering", data.Owner == 1 && view.Invocations == 1);
        int calls = view.Invocations; reads = data.OwnerReads;
        Tick(c, 110); Check("blocked inventory not polled or replayed", calls == view.Invocations && reads == data.OwnerReads);
        open = false; Move(second, 4); Tick(c, 111);
        Check("new overlap request commits boat to server while preserving driver", data.Owner == 99 && data.User == 1234 && view.Invocations == 2);
        Check("native server receipt preserves backend water-impact protection", view.GetComponent<Ship>().m_lastWaterImpactTime == 111);
        Move(second, 40); Tick(c, 112);
        Check("overlap departure returns boat to remaining player while steering", data.Owner == 1 && data.User == 1234);

        c = Reset(); first = Player(1, 0); view = Loaded(c, 99); data = view.Data;
        data.InUse = true; Tick(c, 0); Check("replicated cargo use blocks server delegation", data.Owner == 99 && data.Writes == 0);
        data.InUse = false; Tick(c, 1); Check("closing alone never replays blocked handoff", data.Owner == 99);
        Move(first, 4); Tick(c, 2); Check("fresh field activity delegates closed cargo", data.Owner == 1);
        c = Reset(); Player(1, 0); view = Loaded(c, 99); data = view.Data;
        view.GetComponent<Container>().InUse = true; Tick(c, 0);
        Check("server-local inventory flag also vetoes", data.Owner == 99 && data.Writes == 0);

        c = Reset(); first = Player(1, 0); Player(2, 0); view = Loaded(c, 1); data = view.Data;
        ZNetScene.instance.AreaReady = false; Tick(c, 0);
        Check("unready server physics blocks native acquisition before source request", data.Owner == 1 && view.Invocations == 0);
        ZNetScene.instance.AreaReady = true; Tick(c, 1);
        Check("readiness alone does not replay takeover", data.Owner == 1 && view.Invocations == 0);
        Move(first, 4); Tick(c, 2); Check("new ready overlap requests native source check", data.Owner == 1 && view.Invocations == 1);

        c = Reset(); first = Player(1, 0); view = Loaded(c, 1); data = view.Data;
        view.Invoke = delegate { Reply(c, view, 1, false); }; Tick(c, 0); Move(first, 40); Tick(c, 1); Tick(c, 5);
        Check("empty helm footprint still checks cargo because large boats can be used away from helm", data.Owner == 1 && view.Invocations == 1);
        c = Reset(true, true); first = Player(1, 0); view = Loaded(c, 1); data = view.Data;
        Tick(c, 0); Move(first, 40); Tick(c, 1); Tick(c, 5);
        Check("configured exit retention preserves vacant boat", data.Owner == 1 && view.Invocations == 0);

        c = Reset(false); first = Player(1, 0); view = Loaded(c, 99, false, 12, -12); data = view.Data;
        Tick(c, 0); Check("long boat uses helm anchor rather than distant hull centre", data.Owner == 1);
        data.Position = new Vector3(52, 0, 0); c.PositionChanged(data); Tick(c, 1); Tick(c, 5);
        Check("moving hull moves helm index and restores empty boat to server", data.Owner == 99);
        Check("direct server takeover preserves water-impact grace", view.GetComponent<Ship>().m_lastWaterImpactTime == 1 || view.GetComponent<Ship>().m_lastWaterImpactTime == 5);
        c = Reset(false); first = Player(1, 0, -12); view = Loaded(c, 99, false, 0, 12); data = view.Data;
        Tick(c, 0); Check("different helm location not in solo footprint", data.Owner == 99);
        data.Rotation = new Quaternion { Yaw = 90 }; c.ShipRotated(data); Tick(c, 1);
        Check("rotation without root movement reindexes helm", data.Owner == 1);

        c = Reset(false); Player(1, 0); Player(2, 0); view = Loaded(c, 1, false); data = view.Data;
        Tick(c, 0); Check("raft with two players uses server without inventory request", data.Owner == 99 && view.Invocations == 0);
        var rpc = new ZRoutedRpc(); var packet = new ZRoutedRpc.RoutedRPCData { m_targetPeerID = 1, m_senderPeerID = 1,
            m_targetZDO = data.m_uid, m_methodHash = "Forward".GetStableHashCode(), m_parameters = new ZPackage(), m_msgID = 88 };
        Check("stale helm command redirected once to server", !RequestRouter.Handle(rpc, packet) && rpc.Local.Count == 1 && rpc.Local[0].m_senderPeerID == 1);
        data.SetOwner(2); c.OwnerAssigned(data, 2); packet.m_methodHash = "Rudder".GetStableHashCode();
        packet.m_parameters.Write(.4f); FixtureWire.Order.Clear();
        Check("stale steering command redirected to current player", !RequestRouter.Handle(rpc, packet) && rpc.Remote.Count == 1 && rpc.Remote[0].m_targetPeerID == 2);
        Check("steering snapshot precedes original command", String.Join(",", FixtureWire.Order.ToArray()) == "ZDOData,RoutedRPC");
        foreach (string method in new[] { "RequestControl", "ReleaseControl", "Forward", "Backward", "Stop", "Rudder", "RPC_RequestOpen" })
            Check("native ship owner request recognized: " + method, c.Request(data, method.GetStableHashCode()));
        Check("cargo stack transaction remains native", !c.Request(data, "RPC_RequestStack".GetStableHashCode()));
        c.Remove(data); Check("unload removes ship ownership interception", !c.ManagesShip(data) && c.TrackedObjects == 0);

        Backend();
        Console.WriteLine("PASS: " + checks + " boat grid, native cargo guard, helm routing, backend coexistence and cleanup assertions. Controlled fixtures; no live sailing test.");
    }
    private static void Backend()
    {
        var backend = new Harmony("fixture.ship.backend"); var addon = new Harmony("fixture.ship.addon");
        try
        {
            backend.Patch(AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS"), prefix: new HarmonyMethod(typeof(Valheim_Serverside.Features.Core.ZDOMan_ReleaseNearbyZDOS_Patch), "Prefix"));
            backend.Patch(AccessTools.Method(typeof(Ship), "UpdateOwner"), prefix: new HarmonyMethod(typeof(Valheim_Serverside.Features.Core.Ship_UpdateOwner_Patch), "Prefix"));
            backend.Patch(AccessTools.Method(typeof(ZRoutedRpc), "RouteRPC"), prefix: new HarmonyMethod(typeof(Valheim_Serverside.Features.Core.ZRoutedRpc_RouteRPC_Patch), "Prefix"));
            var bridge = new ForkBridge(new Valheim_Serverside.ServersidePlugin()); bridge.Install(addon);
            var c = Reset(false); Player(1, 0); var view = Loaded(c, 99, false); var data = view.Data; Tick(c, 0);
            int writes = data.Writes, reads = data.OwnerReads;
            for (int i = 0; i < 1000; i++) view.GetComponent<Ship>().RunOwner();
            Check("backend timer cannot steal managed boat or repeatedly inspect owner", data.Owner == 1 && data.Writes == writes && data.OwnerReads == reads);
            Player(2, 0); Tick(c, 1); Check("overlap raft uses server under active backend", data.Owner == 99);
            var payload = new ZPackage(); payload.Write(true);
            var rpc = new ZRoutedRpc(); RequestRouter.Dispatch(rpc, new ZRoutedRpc.RoutedRPCData {
                m_targetPeerID = 1, m_senderPeerID = 99, m_targetZDO = data.m_uid,
                m_methodHash = "RequestRespons".GetStableHashCode(), m_parameters = payload }, false);
            Check("helm grant is delivered without stealing shared boat", data.Owner == 99 && rpc.Remote.Count == 1);
            c = Reset(); Player(1, 0); Player(2, 0); view = Loaded(c, 1); data = view.Data;
            bool open = true;
            view.Invoke = delegate
            {
                if (open) Reply(c, view, 1, false);
                else { data.SetOwner(99); c.OwnerAssigned(data, 99); Reply(c, view, 1, true); }
            };
            Tick(c, 0); Check("overlap cargo refusal starts blocked boat work", data.Owner == 1 && view.Invocations == 1);
            data.InUse = true; Time.unscaledTime = 2; view.GetComponent<Ship>().RunOwner(); Tick(c, 2);
            Check("existing timer does not query visibly open cargo", data.Owner == 1 && view.Invocations == 1);
            data.InUse = false; open = false; Tick(c, 3);
            Check("closing cargo alone does not invent a transfer", data.Owner == 1 && view.Invocations == 1);
            Time.unscaledTime = 4; view.GetComponent<Ship>().RunOwner(); Tick(c, 4); Tick(c, 4.1f);
            Check("existing boat timer resumes blocked overlap after close without player movement", data.Owner == 99 && view.Invocations == 2);
            writes = data.Writes; reads = data.OwnerReads; int requests = view.Invocations;
            for (int i = 0; i < 1000; i++) view.GetComponent<Ship>().RunOwner();
            Check("settled overlap timer creates no owner reads writes or cargo queries", data.Writes == writes && data.OwnerReads == reads && view.Invocations == requests);
            c.Remove(data); data.SetOwner(1); view.GetComponent<Ship>().RunOwner();
            Check("unmanaged ship keeps backend timer", data.Owner == 99);
            c = Reset(false, false, false); Player(1, 0); view = Loaded(c, 1, false); data = view.Data; Tick(c, 0);
            Check("disabled ships not intercepted", !c.ManagesShip(data)); view.GetComponent<Ship>().RunOwner();
            Check("disabled category retains backend ownership", data.Owner == 99);
            c = Reset(false); Player(1, 0); view = Loaded(c, 99, false); Tick(c, 0); addon.UnpatchSelf(); view.GetComponent<Ship>().RunOwner();
            Check("unpatch restores backend owner decisions", view.Data.Owner == 99);
        }
        finally { addon.UnpatchSelf(); backend.UnpatchSelf(); Plugin.Reset(); }
    }
    private static int Main() { try { Run(); return 0; } catch (Exception error) { Console.Error.WriteLine(error); return 1; } }
}
