using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class CartGridTests
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    private static void Tick(Controller c, float now) { Time.unscaledTime = now; c.Tick(now); c.Service(now); }
    private static ZNetPeer Player(long uid, float x)
    {
        var peer = new ZNetPeer { m_uid = uid, m_characterID = new ZDOID(uid), Position = new Vector3(x, 0, 0) };
        ZNet.instance.Peers.Add(peer); ZDOMan.instance.Data[peer.m_characterID] = new ZDO { Owner = uid, Position = peer.Position }; return peer;
    }
    private static void Move(ZNetPeer peer, float x)
    { peer.Position = new Vector3(x, 0, 0); ZDOMan.instance.Data[peer.m_characterID].Position = peer.Position; }
    private static GameObject Cart()
    {
        var root = new GameObject(); var view = root.Add(new ZNetView()); var cart = root.Add(new Vagon());
        var storage = root.Add(new Container { m_rootObjectOverride = view, m_wagon = cart }); cart.m_container = storage;
        root.Add(new Rigidbody { isKinematic = false, useGravity = true }); root.Add(new ZSyncTransform()); return root;
    }
    private static Controller Reset(bool keep = false, bool enabled = true)
    {
        Time.unscaledTime = 0; ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene();
        ZNetScene.instance.Prefabs[10] = Cart(); var config = new ConfigFile();
        config.Values["KeepOwnershipOnCellExit"] = keep; config.Values["Carts"] = enabled; config.Values["ServerOverlapCooldownSeconds"] = 0f;
        var c = new Controller(new Settings(config), 99); Plugin.Current = c; return c;
    }
    private static ZNetView Loaded(Controller c, long owner, bool attached = false)
    {
        var root = Cart(); var view = root.GetComponent<ZNetView>();
        view.Data = new ZDO { m_uid = new ZDOID(20000 + checks), Prefab = 10, Owner = owner, OwnerRevision = 7, Attached = attached };
        ZDOMan.instance.Data[view.Data.m_uid] = view.Data; c.Add(view.Data, view); return view;
    }
    private static void Reply(ZNetView view, long sender, bool allowed)
    {
        var package = new ZPackage(); package.Write(allowed);
        Check("native inventory reply remains dispatched", RequestRouter.Handle(new ZRoutedRpc(), new ZRoutedRpc.RoutedRPCData {
            m_targetPeerID = 99, m_senderPeerID = sender, m_targetZDO = view.Data.m_uid,
            m_methodHash = "RPC_OpenResponse".GetStableHashCode(), m_parameters = package }));
    }
    private static void Handoff(ZNetView view, bool busy)
    {
        view.Invoke = delegate(long source, string method, object[] args)
        {
            Check("cart uses source native RequestOpen rather than fake cart attach", source == view.Data.Owner && method == "RPC_RequestOpen" && (long)args[0] == 0);
            if (!busy) view.Data.SetOwner(99);
            Reply(view, source, !busy);
        };
    }
    private static void Request(ZNetView view, long requester, int method)
    {
        Check("correctly addressed native cart request remains native", RequestRouter.Handle(new ZRoutedRpc(), new ZRoutedRpc.RoutedRPCData {
            m_targetPeerID = view.Data.Owner, m_senderPeerID = requester, m_targetZDO = view.Data.m_uid,
            m_methodHash = method, m_parameters = new ZPackage() }));
    }
    private static void Denied(ZNetView view, long requester)
    {
        Check("native hitch denial is not consumed", RequestRouter.Handle(new ZRoutedRpc(), new ZRoutedRpc.RoutedRPCData {
            m_targetPeerID = requester, m_senderPeerID = view.Data.Owner, m_targetZDO = view.Data.m_uid,
            m_methodHash = "RPC_RequestDenied".GetStableHashCode(), m_parameters = new ZPackage() }));
    }
    private static void Run()
    {
        var c = Reset(); var first = Player(1, 0); var view = Loaded(c, 99); var data = view.Data;
        var catalog = new Catalog((Groups)(-1)); Check("native cart classified independently", catalog.Get(10) == Groups.Carts);
        var wrong = Cart(); wrong.GetComponent<Container>().m_wagon = null; ZNetScene.instance.Prefabs[11] = wrong;
        Check("missing native source pull guard excluded", catalog.Get(11) == Groups.None);
        var detachedIdentity = Cart(); detachedIdentity.GetComponent<Container>().m_rootObjectOverride = new GameObject().Add(new ZNetView());
        ZNetScene.instance.Prefabs[12] = detachedIdentity; Check("separate cargo identity excluded", catalog.Get(12) == Groups.None);
        var privateCargo = Cart(); privateCargo.GetComponent<Container>().m_privacy = Container.PrivacySetting.Private;
        ZNetScene.instance.Prefabs[13] = privateCargo; Check("private cargo excluded", catalog.Get(13) == Groups.None);
        var custom = Cart(); custom.Add(new ModdedControl()); ZNetScene.instance.Prefabs[14] = custom;
        Check("unknown cart action excluded", catalog.Get(14) == Groups.None);
        var multiple = Cart(); multiple.Add(new Container()); ZNetScene.instance.Prefabs[15] = multiple;
        Check("multiple inventories excluded", catalog.Get(15) == Groups.None);
        Check("cart category independently disabled", new Catalog(Groups.Ships | Groups.Containers).Get(10) == Groups.None);
        Tick(c, 0); Check("idle server cart delegates to sole player without opening inventory", data.Owner == 1 && view.Invocations == 0);
        Check("initial backend route cannot bypass live cart checks", c.NativeDestination(data, 99) == 99 && c.NativeDestination(data, 0) == 0);
        int reads = data.OwnerReads, writes = data.Writes;
        for (int i = 1; i <= 1000; i++) Tick(c, i * .1f);
        Check("stationary idle cart has no repeated owner reads writes or native queries", data.OwnerReads == reads && data.Writes == writes && view.Invocations == 0);
        var second = Player(2, 0); Handoff(view, true); Tick(c, 101);
        Check("source-local cargo or joint vetoes overlap despite stale empty server flags", data.Owner == 1 && view.Invocations == 1);
        reads = data.OwnerReads; Tick(c, 110); Check("blocked stationary cart not polled", data.OwnerReads == reads && view.Invocations == 1);
        Handoff(view, false); Move(second, 4); Tick(c, 111); Tick(c, 111.1f);
        Check("fresh idle overlap acquires server through native source check", data.Owner == 99 && view.Invocations == 2);
        Move(second, 40); Tick(c, 112); Check("remaining solo player regains idle cart", data.Owner == 1);

        c = Reset(); first = Player(1, 0); view = Loaded(c, 99, true); data = view.Data; Tick(c, 0);
        Check("Boolean cart attachment excludes pulling cart from grid", data.Owner == 99 && data.Writes == 0);
        data.Set(ZDOVars.s_attachJointHash, false); Tick(c, 1);
        Check("native Boolean detach event resumes grid without player movement", data.Owner == 1);
        Handoff(view, false); second = Player(2, 0); data.Set(ZDOVars.s_attachJointHash, true); Tick(c, 2);
        Check("attached cart retains user despite overlap", data.Owner == 1 && view.Invocations == 0);
        data.Set(ZDOVars.s_attachJointHash, false); Tick(c, 3); Tick(c, 3.1f);
        Check("detach permits overlap server ownership via native inventory query", data.Owner == 99 && view.Invocations == 1);

        c = Reset(); Player(1, 0); view = Loaded(c, 99); data = view.Data; view.GetComponent<Vagon>().LocalJoint = true;
        Tick(c, 0); Check("native local joint vetoes despite unset replicated Boolean", data.Owner == 99 && data.Writes == 0);
        c = Reset(); first = Player(1, 0); view = Loaded(c, 99); data = view.Data; data.InUse = true; Tick(c, 0);
        Check("replicated open cargo vetoes delegation", data.Owner == 99 && data.Writes == 0);
        data.InUse = false; Tick(c, 1); Check("cargo close alone does not replay rejected handoff", data.Owner == 99);
        Move(first, 4); Tick(c, 2); Check("new field activity delegates closed cargo", data.Owner == 1);
        c = Reset(); Player(1, 0); view = Loaded(c, 99); data = view.Data; view.GetComponent<Container>().InUse = true;
        Tick(c, 0); Check("native local cargo vetoes when replicated use is false", data.Owner == 99 && data.Writes == 0);

        c = Reset(); first = Player(1, 0); Player(2, 0); view = Loaded(c, 1); data = view.Data;
        ZNetScene.instance.AreaReady = false; Tick(c, 0);
        Check("unready physics blocks before native source commit", data.Owner == 1 && view.Invocations == 0);
        ZNetScene.instance.AreaReady = true; Handoff(view, false); Move(first, 4); Tick(c, 1); Tick(c, 1.1f);
        Check("ready fresh overlap permits native cart takeover", data.Owner == 99 && view.Invocations == 1);

        c = Reset(); first = Player(1, 0); Player(2, 0); view = Loaded(c, 99); data = view.Data; Tick(c, 0);
        Request(view, 1, "RPC_RequestOwn".GetStableHashCode()); data.SetOwner(1); Tick(c, .1f);
        Check("native ownership grant cannot be immediately stolen before hitch attaches", data.Owner == 1 && view.Invocations == 0);
        int before = data.OwnerReads; Tick(c, 1); Tick(c, 10);
        Check("unknown hitch completion is not polled or timed out", data.Owner == 1 && data.OwnerReads == before);
        data.Set(ZDOVars.s_attachJointHash, true); Tick(c, 11); Handoff(view, false);
        data.Set(ZDOVars.s_attachJointHash, false); Tick(c, 12); Tick(c, 12.1f);
        Check("observed hitch completion then detach releases request protection", data.Owner == 99 && view.Invocations == 1);

        c = Reset(); Player(1, 0); Player(2, 0); view = Loaded(c, 99); data = view.Data; Tick(c, 0);
        Request(view, 1, "RPC_RequestOwn".GetStableHashCode());
        data.Set(ZDOVars.s_attachJointHash, true); Tick(c, .1f); data.SetOwner(1); Tick(c, .2f);
        Check("attachment arriving before ownership receipt preserves native user", data.Owner == 1 && view.Invocations == 0);
        Handoff(view, false); data.Set(ZDOVars.s_attachJointHash, false); Tick(c, 1); Tick(c, 1.1f);
        Check("reverse import order still releases completed hitch on detach", data.Owner == 99 && view.Invocations == 1);

        c = Reset(); first = Player(1, 0); Player(2, 0); view = Loaded(c, 99); data = view.Data; Tick(c, 0);
        Request(view, 1, "RPC_RequestOwn".GetStableHashCode()); Request(view, 2, "RPC_RequestOwn".GetStableHashCode());
        data.SetOwner(1); Denied(view, 2); Tick(c, .1f);
        Check("another request denial does not clear first pending hitch", data.Owner == 1 && view.Invocations == 0);
        Handoff(view, false); Denied(view, 1); Tick(c, 1); Tick(c, 1.1f);
        Check("native denial releases only matching pending requester", data.Owner == 99 && view.Invocations == 1);

        c = Reset(); first = Player(1, 0); view = Loaded(c, 1); data = view.Data; Tick(c, 0);
        Request(view, 1, "RPC_RequestOwn".GetStableHashCode()); Handoff(view, false);
        Move(first, 40); Tick(c, 1); Tick(c, 5); Tick(c, 5.1f);
        Check("failed native hitch request releases on footprint departure", data.Owner == 99 && view.Invocations == 1);
        Move(first, 0); Tick(c, 6); Check("reentry does not revive failed hitch protection", data.Owner == 1);

        c = Reset(); Player(1, 0); view = Loaded(c, 1); data = view.Data; Tick(c, 0);
        Request(view, 1, "RPC_RequestOwn".GetStableHashCode()); Handoff(view, false);
        data.InternalSetPosition(new Vector3(4, 0, 0)); Tick(c, .1f);
        Check("moving cart retains pending hitch within requester footprint", data.Owner == 1 && view.Invocations == 0);
        data.InternalSetPosition(new Vector3(40, 0, 0)); Tick(c, 1); Tick(c, 5); Tick(c, 5.1f);
        Check("moving cart leaves pending requester and can return to server", data.Owner == 99 && view.Invocations == 1);

        c = Reset(); first = Player(1, 0); view = Loaded(c, 1); data = view.Data; Tick(c, 0);
        Handoff(view, true); Move(first, 40); Tick(c, 1); Tick(c, 5);
        Check("even empty vehicle field retains native source-use check", data.Owner == 1 && view.Invocations == 1);
        c = Reset(true); first = Player(1, 0); view = Loaded(c, 1); data = view.Data; Tick(c, 0);
        Move(first, 40); Tick(c, 1); Tick(c, 5);
        Check("exit retention applies to idle carts", data.Owner == 1 && view.Invocations == 0);

        c = Reset(); first = Player(1, 0); view = Loaded(c, 99); data = view.Data; Tick(c, 0);
        Handoff(view, false); data.InternalSetPosition(new Vector3(40, 0, 0)); Tick(c, 1); Tick(c, 5); Tick(c, 5.1f);
        Check("native position event moves cart grid index", data.Owner == 99 && view.Invocations == 1);
        Check("native opening recognized", c.Request(data, "RPC_RequestOpen".GetStableHashCode()));
        Check("native hitch recognized", c.Request(data, "RPC_RequestOwn".GetStableHashCode()));
        Check("cargo transaction replies not converted to handoffs", !c.Request(data, "RPC_RequestTakeAll".GetStableHashCode()));
        var rpc = new ZRoutedRpc(); var packet = new ZRoutedRpc.RoutedRPCData { m_targetPeerID = 1, m_senderPeerID = 1,
            m_targetZDO = data.m_uid, m_methodHash = "RPC_RequestOwn".GetStableHashCode(), m_parameters = new ZPackage() };
        Check("stale native hitch routes to current owner without parameter changes", !RequestRouter.Handle(rpc, packet) && rpc.Local.Count == 1 && rpc.Local[0].m_senderPeerID == 1);
        c.Remove(data); Check("unload removes pending hitch and inventory state", c.TrackedObjects == 0 && !c.ExpectsInventoryReply(data));
        c = Reset(false, false); Player(1, 0); view = Loaded(c, 99); Tick(c, 0);
        Check("disabled cart category remains native", c.TrackedObjects == 0 && view.Data.Owner == 99);
        Console.WriteLine("PASS: " + checks + " cart grid, native inventory/pulling guards, hitch requests, Boolean hooks, movement and cleanup assertions. No live cart physics test.");
    }
    private static int Main()
    {
        var harmony = new Harmony("fixture.cart.events");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ZDO), "Set", new[] { typeof(int), typeof(bool) }), postfix: new HarmonyMethod(typeof(AnimalFlagChanged), "Postfix"));
            harmony.Patch(AccessTools.Method(typeof(ZDO), "SetOwnerInternal"), postfix: new HarmonyMethod(typeof(OwnershipChanged), "Postfix"));
            harmony.Patch(AccessTools.Method(typeof(ZDO), "InternalSetPosition"), postfix: new HarmonyMethod(typeof(ObjectMoved), "Postfix"));
            Run(); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { harmony.UnpatchSelf(); Plugin.Reset(); }
    }
}
