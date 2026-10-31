using System;
using System.Reflection;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static GameObject PhysicsRoot(string name = "log", bool sleeping = false)
    {
        var root = new GameObject { name = name };
        var view = root.Add(new ZNetView());
        root.Add(new Rigidbody { isKinematic = false, useGravity = true, Sleeping = sleeping });
        var sync = root.Add(new ZSyncTransform()); sync.SetView(view);
        root.Add(new TreeLog()); return root;
    }
    private static ZDO PhysicsObject(bool sleeping = false, float x = 1, long owner = 99)
    {
        var root = PhysicsRoot("log" + next, sleeping); root.transform.position = new Vector3(x, 0, 1);
        var data = new ZDO { m_uid = new ZDOID { ID = next++ }, Hash = 30000 + next, Position = root.transform.position };
        data.SetOwner(owner); data.Writes = 0; root.GetComponent<ZNetView>().Data = data;
        ZDOMan.instance.Objects.Add(data); ZNetScene.instance.Views.Add(data, root.GetComponent<ZNetView>());
        ZNetScene.instance.Prefabs.Add(data.Hash, root); controller.Register(data, root.GetComponent<ZNetView>()); return data;
    }
    private static void PhysicsWorld()
    {
        int before = checks;
        var log = PhysicsRoot();
        Check("a native synchronized log has a physics category", ObjectCatalog.Classify(log) == Kinds.PhysicsObjects);
        var tree = new GameObject { name = "tree" }; tree.Add(new ZNetView()); tree.Add(new TreeBase { m_logPrefab = log });
        Check("a tree inherits interactive classification from its physical successor", ObjectCatalog.Classify(tree) == Kinds.PhysicsObjects);
        Check("the legacy static-only path cannot take over a physical source", !StaticBuildings.Safe(tree));
        tree.GetComponent<ZNetView>().m_distant = true;
        Check("distant standing tree keeps its physical-source classification", ObjectCatalog.Classify(tree) == Kinds.PhysicsObjects);
        var broken = new GameObject { name = "breakable" }; broken.Add(new ZNetView()); broken.Add(new Destructible { m_spawnWhenDestroyed = log });
        Check("native destructible successor metadata is supported", ObjectCatalog.Classify(broken) == Kinds.PhysicsObjects);
        var loot = new GameObject { name = "loot" }; loot.Add(new ZNetView()); loot.Add(new ItemDrop()); loot.Add(new Rigidbody());
        var ordinary = new GameObject { name = "ordinary" }; ordinary.Add(new ZNetView()); ordinary.Add(new Destructible { m_spawnWhenDestroyed = loot });
        Check("ordinary item drops do not make the source interactive", ObjectCatalog.Classify(ordinary) != Kinds.PhysicsObjects);
        var cosmetic = PhysicsRoot("visual"); cosmetic.GetComponent<ZNetView>().m_persistent = false;
        var visualSource = new GameObject(); visualSource.Add(new ZNetView()); visualSource.Add(new Destructible { m_spawnWhenDestroyed = cosmetic });
        Check("visual nonpersistent debris has no grid ownership", ObjectCatalog.Classify(cosmetic) == Kinds.None && ObjectCatalog.Classify(visualSource) != Kinds.PhysicsObjects);
        var projectile = PhysicsRoot("persistent projectile"); projectile.Add(new Projectile());
        Check("persistent projectiles are explicitly excluded", ObjectCatalog.Classify(projectile) == Kinds.None);
        var chained = new GameObject(); chained.Add(new ZNetView()); chained.Add(new Destructible { m_spawnWhenDestroyed = broken });
        Check("a short explicit successor chain is supported", ObjectCatalog.Classify(chained) == Kinds.PhysicsObjects);
        var cycle = new GameObject(); cycle.Add(new ZNetView()); cycle.Add(new Destructible { m_spawnWhenDestroyed = cycle });
        Check("cyclic successor metadata terminates and stays outside physics", ObjectCatalog.Classify(cycle) != Kinds.PhysicsObjects);
        var jointed = PhysicsRoot(); jointed.Add(new Joint());
        Check("joint systems are not mistaken for simple physics props", ObjectCatalog.Classify(jointed) == Kinds.None);
        var unsynced = PhysicsRoot(); unsynced.GetComponent<ZSyncTransform>().m_syncBodyVelocity = false;
        Check("incomplete native motion synchronization is not a safe physics root", ObjectCatalog.Classify(unsynced) == Kinds.None);
        var disabled = new ObjectCatalog(Kinds.Doors);
        var live = new ZDO { m_uid = new ZDOID { ID = 9001 } }; log.GetComponent<ZNetView>().Data = live;
        Check("disabled physics never falls through into static ownership", disabled.Instance(99, log.GetComponent<ZNetView>()) == Kinds.None);

        Fresh(Kinds.PhysicsObjects | Kinds.Doors, defaultInteractives: true);
        var player = Peer(1); var moving = PhysicsObject(); var door = Object(typeof(Door), 1, 1, 99);
        var body = ZNetScene.instance.FindInstance(moving).GetComponent<Rigidbody>();
        Tick(0); Tick(2);
        Check("moving server-owned body rejects a normal player takeover", moving.GetOwner() == 99);
        Check("one moving object does not block another object in its cell", door.GetOwner() == 1);
        int reads = body.SleepReads, scans = GameObject.Scans, writes = moving.Writes;
        for (int i = 0; i < 100; i++) Tick(3 + i);
        Check("standing does not poll sleeping state, scan roots or write ownership", body.SleepReads == reads && GameObject.Scans == scans && moving.Writes == writes);
        body.Sleeping = true; MovePeer(player, 48); Tick(104); MovePeer(player, 0); Tick(105); Tick(107);
        Check("fresh coverage can claim the now sleeping body", moving.GetOwner() == 1);

        Fresh(Kinds.PhysicsObjects, defaultInteractives: true, keepOnExit: false);
        player = Peer(1); moving = PhysicsObject(owner: 1); body = ZNetScene.instance.FindInstance(moving).GetComponent<Rigidbody>();
        Tick(0); Tick(2);
        Check("unchanged owner needs no sleeping check", moving.GetOwner() == 1 && body.SleepReads == 0);
        MovePeer(player, 48); Tick(3); Tick(6);
        Check("vacant exit may return an awake body to server", moving.GetOwner() == 99 && body.SleepReads == 0);

        Fresh(Kinds.PhysicsObjects, defaultInteractives: true, keepOnExit: true);
        player = Peer(1); moving = PhysicsObject(owner: 1); Tick(0); Tick(2); MovePeer(player, 48); Tick(3); Tick(6);
        Check("retained exit creates no physical return", moving.GetOwner() == 1);

        Fresh(Kinds.PhysicsObjects, defaultInteractives: true, keepOnExit: false);
        Peer(1); moving = PhysicsObject(x: 48, owner: 7);
        bool sleepRequired;
        Check("a newly spawned uncovered object is not mistaken for an exit return", controller.PhysicsTransfer(moving, out sleepRequired) && sleepRequired);

        foreach (bool defaults in new[] { true, false })
        {
            Fresh(Kinds.PhysicsObjects, defaultInteractives: defaults);
            player = Peer(1); AuthorityController.PhysicsClient = delegate { return true; };
            try
            {
                Tick(0); Tick(2);
                var loading = PhysicsObject(owner: 0); controller.InitialMobLoad(loading);
                Check("first loaded body follows the settled player grid without sleep", loading.GetOwner() == 1 && ZNetScene.instance.FindInstance(loading).GetComponent<Rigidbody>().SleepReads == 0);
                var outside = PhysicsObject(x: 48, owner: 0); controller.InitialMobLoad(outside);
                Check("uncovered first load follows selected server/vanilla default", outside.GetOwner() == (defaults ? 99 : 0));
                var established = PhysicsObject(x: 48, owner: 7); controller.InitialMobLoad(established);
                Check("loading a replica never steals an established owner", established.GetOwner() == 7);
            }
            finally { AuthorityController.PhysicsClient = null; }
        }
#if GRID_PROTOCOL
        PhysicsSourceProtocol();
        PhysicsSourceVetoAndExit();
#endif
        Console.WriteLine("PASS: " + (checks - before) + " physics-world assertions: native successors, explicit exclusions, sleeping-only transfers, exits, initial load and no idle sleep work.");
    }
#if GRID_PROTOCOL
    private static ZPackage PhysicsCommit(ZDO data, long token, long target, bool sleep)
    { return GridPacket(10, delegate(ZPackage p) { p.Write(token); p.Write(data.m_uid); p.Write(target); p.Write((uint)data.OwnerRevision); p.Write(sleep); }); }
    private static void PhysicsSourceProtocol()
    {
        Fresh(Kinds.PhysicsObjects, defaultInteractives: true); Peer(1);
        var serverPeer = new ZNetPeer { Uid = 99, m_server = true }; ZNet.instance.Peers.Add(serverPeer);
        var data = PhysicsObject(owner: 1); var view = ZNetScene.instance.FindInstance(data); var body = view.GetComponent<Rigidbody>(); var sync = view.GetComponent<ZSyncTransform>();
        GridStart(); long session = ZDOMan.SessionID;
        try
        {
            ZNet.instance.Dedicated = false; ZDOMan.SessionID = 1; ZNet.instance.ServerPeer = serverPeer;
            GridReceive(serverPeer, GridPacket(1, delegate(ZPackage p) { p.Write(true); p.Write(false); p.Write(false); p.Write(true); p.Write(true); }));
            int commits = 0;
            typeof(TooManyLags.GridHandoffs.Protocol).GetField("nativeCommit", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null,
                (Action<ZRpc, ZDOID, long>)delegate(ZRpc rpc, ZDOID id, long target) { commits++; data.SetOwner(target); });
            var packet = PhysicsCommit(data, 1, 99, true); GridReceive(serverPeer, packet);
            Check("owner rejects an awake body's ordinary overlap transfer", commits == 0 && data.GetOwner() == 1 && sync.Syncs == 0);
            body.Sleeping = true; packet = PhysicsCommit(data, 2, 99, true); GridReceive(serverPeer, packet);
            Check("owner accepts sleeping overlap and flushes motion once", commits == 1 && data.GetOwner() == 99 && sync.Syncs == 1);
            GridReceive(serverPeer, packet);
            Check("replaying an accepted physics token never repeats the write", commits == 1 && sync.Syncs == 1);
            data.SetOwner(1); body.Sleeping = false;
            GridReceive(serverPeer, PhysicsCommit(data, 3, 99, false));
            Check("owner accepts awake vacant-field server return", commits == 2 && data.GetOwner() == 99 && sync.Syncs == 2);
            data.SetOwner(1); GridReceive(serverPeer, PhysicsCommit(data, 4, 2, false));
            Check("sleep bypass cannot be used for a player-to-player transfer", commits == 2 && data.GetOwner() == 1);
            var projectile = PhysicsRoot(); projectile.Add(new Projectile());
            var projectileData = new ZDO { m_uid = new ZDOID { ID = next++ }, Hash = next }; projectileData.SetOwner(1); projectile.GetComponent<ZNetView>().Data = projectileData;
            ZDOMan.instance.Objects.Add(projectileData); ZNetScene.instance.Views.Add(projectileData, projectile.GetComponent<ZNetView>());
            GridReceive(serverPeer, PhysicsCommit(projectileData, 5, 99, false));
            Check("explicit projectile cannot be handed off by the physics endpoint", commits == 2 && projectileData.GetOwner() == 1);
            data.SetOwner(1); sync.SetWasOwner(false); int syncs = sync.Syncs;
            body.isKinematic = true; sync.CustomFixedUpdate(.02f);
            Check("collision hold is not released by ownership gain", sync.Syncs == syncs && body.isKinematic);
            body.isKinematic = false; sync.CustomFixedUpdate(.02f); sync.CustomFixedUpdate(.02f);
            Check("native gain snapshot is restored once before physics", sync.Syncs == syncs + 1);
        }
        finally
        { ZNet.instance.Dedicated = true; ZDOMan.SessionID = session; ZNet.instance.ServerPeer = null; TooManyLags.GridHandoffs.Protocol.Stop(); }
    }
    private static void PhysicsSourceVetoAndExit()
    {
        Fresh(Kinds.PhysicsObjects, defaultInteractives: true, keepOnExit: false);
        var first = Peer(1); var second = Peer(2); var serverPeer = new ZNetPeer { Uid = 99, m_server = true };
        ZNet.instance.Peers.Add(serverPeer); GridStart();
        var data = PhysicsObject(sleeping: true, owner: 1);
        var body = ZNetScene.instance.FindInstance(data).GetComponent<Rigidbody>();
        try
        {
            GridReceive(first, GridPacket(0, delegate(ZPackage p) { p.Write(33); }));
            GridReceive(second, GridPacket(0, delegate(ZPackage p) { p.Write(33); }));
            var sent = GridCapture(first); Tick(0); Tick(2);
            Check("overlap never reads the non-owner server body's sleep state", body.SleepReads == 0);
            ZPackage request = null;
            foreach (var packet in sent) { packet.SetPos(0); if (packet.ReadInt() == 10) request = packet; }
            Check("overlap queries the real physics owner", request != null && controller.Cooperative.Pending(data));
            var replies = GridCapture(serverPeer);
            ZNet.instance.Dedicated = false; ZDOMan.SessionID = 1; ZNet.instance.ServerPeer = serverPeer;
            try
            {
                GridReceive(serverPeer, GridPacket(1, delegate(ZPackage p) { p.Write(true); p.Write(false); p.Write(false); p.Write(true); p.Write(true); }));
                body.Sleeping = false; GridReceive(serverPeer, request);
            }
            finally { ZNet.instance.Dedicated = true; ZDOMan.SessionID = 99; ZNet.instance.ServerPeer = null; }
            ZPackage result = null;
            foreach (var packet in replies) { packet.SetPos(0); if (packet.ReadInt() == 4) result = packet; }
            Check("source returns its movement veto", result != null && body.SleepReads == 1);
            GridReceive(first, result);
            Check("movement veto leaves no parked cooperative commit", data.GetOwner() == 1 && !controller.Cooperative.Pending(data) && !controller.Cooperative.Reserved(data));
            int queries = sent.Count; for (int i = 3; i <= 10; i++) Tick(i);
            Check("a movement veto is not retried by the old six-second timer", sent.Count == queries);
            MovePeer(first, 48); MovePeer(second, 48); Tick(11); Tick(14);
            ZPackage exit = null;
            foreach (var packet in sent) { packet.SetPos(0); if (packet.ReadInt() == 10) exit = packet; }
            Check("a later empty-field return replaces the discarded overlap request", exit != null && !ReferenceEquals(request, exit));
            exit.SetPos(0); exit.ReadInt(); exit.ReadLong(); exit.ReadZDOID();
            Check("empty-field return targets server and explicitly bypasses sleep", exit.ReadLong() == 99 && exit.ReadUInt() == data.OwnerRevision && !exit.ReadBool());
        }
        finally { TooManyLags.GridHandoffs.Protocol.Stop(); }
    }
#endif
}
