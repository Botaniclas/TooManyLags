using System;
using BepInEx.Configuration;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class InventoryGridTests
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    private static void Tick(Controller c, float now) { Time.unscaledTime = now; c.Tick(now); c.Service(now); }
    private static ZNetPeer Player(long uid, float x)
    {
        var peer = new ZNetPeer { m_uid = uid, m_characterID = new ZDOID(uid), Position = new Vector3(x, 0, 0) };
        ZNet.instance.Peers.Add(peer);
        ZDOMan.instance.Data[peer.m_characterID] = new ZDO { Owner = uid, Position = peer.Position };
        return peer;
    }
    private static void Move(ZNetPeer peer, float x)
    { peer.Position = new Vector3(x, 0, 0); ZDOMan.instance.Data[peer.m_characterID].Position = peer.Position; }
    private static GameObject Chest(int hash, params MonoBehaviour[] extra)
    {
        var root = new GameObject(); root.Add(new ZNetView()); root.Add(new Container());
        foreach (MonoBehaviour part in extra) root.Add(part);
        ZNetScene.instance.Prefabs[hash] = root; return root;
    }
    private static ZNetView Loaded(Controller c, long owner)
    {
        var root = new GameObject(); var data = new ZDO { m_uid = new ZDOID(10000 + checks), Prefab = 10, Owner = owner, OwnerRevision = 7 };
        var view = root.Add(new ZNetView { Data = data }); root.Add(new Container());
        ZDOMan.instance.Data[data.m_uid] = data; c.Add(data, view); return view;
    }
    private static Controller Reset(bool keep, float cooldown = 0f)
    {
        Time.unscaledTime = 0; ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene();
        Chest(10); var cfg = new ConfigFile(); cfg.Values["KeepOwnershipOnCellExit"] = keep;
        cfg.Values["ServerOverlapCooldownSeconds"] = cooldown;
        var c = new Controller(new Settings(cfg), 99); Plugin.Current = c; return c;
    }
    private static void Reply(Controller c, ZNetView view, long sender, bool granted)
    {
        var payload = new ZPackage(); payload.Write(granted);
        var rpc = new ZRoutedRpc();
        Check("native response remains dispatched", RequestRouter.Handle(rpc, new ZRoutedRpc.RoutedRPCData {
            m_targetPeerID = 99, m_senderPeerID = sender, m_targetZDO = view.Data.m_uid,
            m_methodHash = "RPC_OpenResponse".GetStableHashCode(), m_parameters = payload }));
    }
    private static void Run()
    {
        var c = Reset(false); Player(1, 0);
        var catalog = new Catalog((Groups)(-1));
        Check("native public chest classified", catalog.Get(10) == Groups.Containers);
        Chest(11).GetComponent<Container>().m_privacy = Container.PrivacySetting.Private;
        Check("private chest native", catalog.Get(11) == Groups.None);
        Chest(12, new Incinerator()); Check("incinerator remains native", catalog.Get(12) == Groups.None);
        Chest(13, new TombStone()); Check("grave remains native", catalog.Get(13) == Groups.None);
        Chest(14, new ModdedControl()); Check("custom inventory actions remain native", catalog.Get(14) == Groups.None);
        Chest(15, new Door()); Check("mixed transaction identity native", catalog.Get(15) == Groups.None);
        var moving = Chest(17); moving.Add(new ZSyncTransform()); Check("mobile inventory stays native", catalog.Get(17) == Groups.None);
        Check("disabled chest category excluded", new Catalog(Groups.Doors).Get(10) == Groups.None);
        var shared = Chest(20); shared.GetComponent<Container>().m_rootObjectOverride = shared.GetComponent<ZNetView>();
        Check("native matching network-root override is accepted", catalog.Get(20) == Groups.Containers);
        Chest(21).GetComponent<Container>().m_rootObjectOverride = new GameObject().Add(new ZNetView());
        Check("different network-root override remains excluded", catalog.Get(21) == Groups.None);
        var view = Loaded(c, 99); var data = view.Data; Tick(c, 0);
        Check("sole nearby player acquires chest without opening", data.Owner == 1 && view.Invocations == 0);
        Check("new player owner receives native state scheduling", ZDOMan.instance.ForcedSends == 1 && ZDOMan.instance.LastForcedPeer == 1);
        Check("backend cannot bypass guarded existing inventory owner", c.NativeDestination(data, 99) == 99);
        var initial = new ZDO { Prefab = 10, Owner = 0 };
        Check("initially unowned chest can use mature grid", c.NativeDestination(initial, 99) == 1);
        Check("native release destination stays unchanged", c.NativeDestination(data, 0) == 0);
        data.SetOwner(1); c.OwnerAssigned(data, 1); Tick(c, .1f);
        Check("sole native opener retained", data.Owner == 1 && view.Invocations == 0);
        var second = Player(2, 0); bool busy = true;
        view.Invoke = delegate(long target, string method, object[] args)
        {
            Check("overlap requests native current owner", target == 1 && method == "RPC_RequestOpen" && args.Length == 1 && (long)args[0] == 0L);
            if (busy) Reply(c, view, 1, false);
            else { data.SetOwner(99); c.OwnerAssigned(data, 99); Reply(c, view, 1, true); }
        };
        Tick(c, .2f);
        Check("busy overlap refuses without owner write", data.Owner == 1 && view.Invocations == 1 && c.PendingTransfers == 0);
        int reads = data.OwnerReads, writes = data.Writes;
        for (int i = 1; i <= 1000; i++) Tick(c, .2f + i * .1f);
        Check("stationary denied chest has no repeated reads requests or writes", view.Invocations == 1 && data.OwnerReads == reads && data.Writes == writes);
        busy = false; Move(second, 4); Tick(c, 101);
        Check("fresh movement retries natively after refusal", view.Invocations == 2 && data.Owner == 99 && data.Writes == writes + 1);
        Tick(c, 101.1f);
        Check("source native grant not followed by forced owner write", data.Writes == writes + 1 && view.Invocations == 2);
        Check("no synthetic server-local inventory use", !view.GetComponent<Container>().IsInUse());
        c.Remove(data); Check("unload forgets native request state", !c.ExpectsInventoryReply(data));

        c = Reset(false); Player(1, 0); var distant = Player(2, 40); view = Loaded(c, 2); data = view.Data;
        Tick(c, 0);
        Check("different connected owner checked before player-to-player transfer", data.Owner == 2 && view.Invocations == 1 && c.ExpectsInventoryReply(data));
        int beforeCommit = data.Writes;
        Reply(c, view, 2, true); Tick(c, .1f);
        Check("grant without native owner receipt never forces onward player", data.Owner == 2 && data.Writes == beforeCommit);
        data.Owner = 99; c.OwnerAssigned(data, 99); Tick(c, .2f);
        Check("half-imported native commit waits for revision", data.Owner == 99 && data.Writes == beforeCommit);
        data.OwnerRevision = 8; Tick(c, .3f);
        Check("settling revision without a new event does not poll chest", data.Owner == 99 && data.Writes == beforeCommit);
        Move(ZNet.instance.GetPeer(1), 4); Tick(c, .4f);
        Check("matching native grant and receipt allows onward grid owner", data.Owner == 1 && data.Writes == beforeCommit + 1 && view.Invocations == 1);
        Check("onward delegation uses native state scheduling without GUI request", ZDOMan.instance.ForcedSends == 1 && ZDOMan.instance.LastForcedPeer == 1 && !view.GetComponent<Container>().IsInUse());

        // In the real receiver SetOwnerInternal runs before/after the revision,
        // and the queued owner event is evaluated after the packet completes.
        c = Reset(false); Player(1, 0); Player(2, 40); view = Loaded(c, 2); data = view.Data; Tick(c, 0);
        data.SetOwner(99); c.OwnerAssigned(data, 99); Tick(c, .1f);
        Check("owner receipt before reply waits without onward write", data.Owner == 99 && data.Writes == 1);
        Reply(c, view, 2, true); Tick(c, .2f);
        Check("late matching reply wakes only affected chest", data.Owner == 1 && data.Writes == 2 && view.Invocations == 1);
        int stationaryReads = data.OwnerReads, stationaryWrites = data.Writes;
        for (int i = 1; i <= 100; i++) Tick(c, .2f + i * .1f);
        Check("settled player chest performs no repeated object work", data.OwnerReads == stationaryReads && data.Writes == stationaryWrites && view.Invocations == 1);

        c = Reset(false); Player(1, 0); view = Loaded(c, 99); data = view.Data;
        view.GetComponent<Container>().InUse = true; Tick(c, 0);
        Check("server-local open inventory vetoes delegation", data.Owner == 99 && view.Invocations == 0);
        view.GetComponent<Container>().InUse = false; Tick(c, .1f);
        Check("local close alone never replays rejected transfer", data.Owner == 99);
        Move(ZNet.instance.GetPeer(1), 4); Tick(c, .2f);
        Check("fresh field activity delegates idle server chest", data.Owner == 1 && view.Invocations == 0);

        c = Reset(false); Player(1, 0); distant = Player(2, 40); view = Loaded(c, 2); data = view.Data;
        view.Invoke = delegate { Reply(c, view, 2, false); }; Tick(c, 0);
        Check("busy old owner refuses player-to-player handoff", data.Owner == 2 && view.Invocations == 1 && data.Writes == 0);
        Move(distant, 44); Tick(c, .1f);
        Check("unrelated distant coverage does not retry refused chest", view.Invocations == 1 && data.Owner == 2);

        c = Reset(false); Player(1, 0); view = Loaded(c, 99); data = view.Data; Tick(c, 0);
        var payload = new ZPackage(); payload.Write(123456L);
        var packet = new ZRoutedRpc.RoutedRPCData { m_msgID = 88, m_senderPeerID = 1, m_targetPeerID = 99,
            m_targetZDO = data.m_uid, m_methodHash = "RPC_RequestOpen".GetStableHashCode(), m_parameters = payload };
        var routed = new ZRoutedRpc(); FixtureWire.Order.Clear();
        Check("stale opening request follows actual player owner", !RequestRouter.Handle(routed, packet) && routed.Remote.Count == 1 && routed.Remote[0].m_targetPeerID == 1);
        Check("forwarded native opening preserves sender profile and request id", routed.Remote[0].m_senderPeerID == 1 && routed.Remote[0].m_msgID == 88 && new ZPackage(routed.Remote[0].m_parameters.GetArray()).ReadLong() == 123456L);
        Check("opening gets current owner state before native request", String.Join(",", FixtureWire.Order.ToArray()) == "ZDOData,RoutedRPC" && data.Owner == 1);
        packet.m_targetPeerID = 1;
        Check("correctly addressed opening stays on native path", RequestRouter.Handle(routed, packet));
        data.SetOwner(99); c.OwnerAssigned(data, 99); packet.m_targetPeerID = 1;
        Check("stale opening request can return to native server handler", !RequestRouter.Handle(routed, packet) && routed.Local.Count == 1);
        packet.m_methodHash = "RPC_OpenResponse".GetStableHashCode();
        Check("actual player open response is never synthesized or redirected", RequestRouter.Handle(routed, packet));

        c = Reset(false); Player(1, 0); second = Player(2, 0); view = Loaded(c, 1); data = view.Data; Tick(c, 0);
        data.SetOwner(99); c.OwnerAssigned(data, 99); Tick(c, .1f);
        Check("same server owner does not certify an unacknowledged acquisition", c.ExpectsInventoryReply(data) && data.Owner == 99 && data.Writes == 1);
        Move(second, 40); Tick(c, .2f);
        Check("changing desired field cannot bypass pending native reply", data.Owner == 99 && data.Writes == 1);
        Reply(c, view, 1, true); Tick(c, .3f);
        Check("reply completes to current field player rather than stale server target", data.Owner == 1 && data.Writes == 2 && view.Invocations == 1);

        c = Reset(false); Player(1, 0); view = Loaded(c, 99); data = view.Data;
        data.OwnerRevision = UInt16.MaxValue; Tick(c, 0);
        Check("grid never overflows native inventory ownership revision", data.Owner == 99 && data.Writes == 0 && view.Invocations == 0);
        packet.m_targetZDO = data.m_uid; packet.m_targetPeerID = 1;
        packet.m_methodHash = "RPC_RequestStack".GetStableHashCode();
        Check("native stack transaction is not routed by opening support", RequestRouter.Handle(new ZRoutedRpc(), packet));
        packet.m_methodHash = "RPC_RequestTakeAll".GetStableHashCode();
        Check("native take-all transaction is not routed by opening support", RequestRouter.Handle(new ZRoutedRpc(), packet));

        c = Reset(false); var only = Player(1, 0); view = Loaded(c, 1); data = view.Data; Tick(c, 0);
        Move(only, 40); Tick(c, .1f); Tick(c, 3.2f);
        Check("empty field direct return has no native open request or grace", data.Owner == 99 && view.Invocations == 0 && c.PendingTransfers == 0);

        c = Reset(true); only = Player(1, 0); view = Loaded(c, 1); data = view.Data; Tick(c, 0);
        Move(only, 40); Tick(c, .1f); Tick(c, 10);
        Check("exit retention preserves chest without request", data.Owner == 1 && view.Invocations == 0);

        c = Reset(false); Player(1, 0); Player(2, 0); view = Loaded(c, 1); data = view.Data; Tick(c, 0);
        Check("outstanding request does not force a transfer", view.Invocations == 1 && data.Owner == 1 && c.ExpectsInventoryReply(data));
        reads = data.OwnerReads;
        Tick(c, 1); Tick(c, 6); Tick(c, 10);
        Check("timeout alone never retries or changes owner", view.Invocations == 1 && data.Owner == 1 && data.OwnerReads == reads);
        Check("expired request no longer blocks", !c.ExpectsInventoryReply(data));
        Move(ZNet.instance.GetPeer(2), 4); Tick(c, 11);
        Check("fresh overlap activity may retry expired native request", view.Invocations == 2 && data.Owner == 1);
        c.Remove(data); Check("unload clears outstanding request", !c.ExpectsInventoryReply(data));

        c = Reset(false); Player(1, 0); Player(2, 0); view = Loaded(c, 7); data = view.Data; Tick(c, 0);
        Check("disconnected owner reclaimed without request", data.Owner == 99 && view.Invocations == 0);

        c = Reset(false, 2f); Player(1, 0); second = Player(2, 0); view = Loaded(c, 99); data = view.Data; Tick(c, 0);
        data.SetOwner(1); c.OwnerAssigned(data, 1); Move(second, 40); Tick(c, .1f);
        Check("remaining server cooldown is not a real overlap takeover", data.Owner == 1 && view.Invocations == 0);
        Console.WriteLine("PASS: " + checks + " inventory grid and event-driven native handoff assertions. No live multiplayer claim.");
    }
    private static void Main() { try { Run(); } catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; } }
}
