using System;
using System.Collections.Generic;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static void PendingFresh(Kinds kinds)
    {
        Fresh(kinds, defaultInteractives: true, keepOnExit: false, directional: true,
            cellSize: 4, pattern: GridCoveragePattern.Rounded7x7, checkIntervalSeconds: .05f);
        Peer(1); Tick(0);
    }
    private static ZDO PendingTree(float x, long owner)
    {
        var root = new GameObject { name = "fixed_tree" };
        root.transform.position = new Vector3(x, 0, 1);
        var view = root.Add(new ZNetView { m_distant = true });
        root.Add(new TreeBase { m_logPrefab = PhysicsRoot() });
        var data = new ZDO { m_uid = new ZDOID { ID = next++ }, Hash = 50000 + next, Position = root.transform.position };
        data.SetOwner(owner); data.Writes = 0; view.Data = data;
        ZDOMan.instance.Objects.Add(data); ZNetScene.instance.Views.Add(data, view);
        ZNetScene.instance.Prefabs.Add(data.Hash, root); controller.Register(data, view);
        return data;
    }
    private static void PendingPolls(int count, float start = 0)
    { for (int i = 1; i <= count; i++) Tick(start + i * .05f); }
    private static void PendingWork()
    {
        int before = checks;
        PendingFresh(Kinds.PhysicsObjects);
        ZNetScene.instance.Ready = delegate { throw new Exception("No broad readiness scans expected."); };
        var trees = new List<ZDO>();
        for (int i=0;i<100;i++) trees.Add(PendingTree(512,0));
        int calls=controller.ScheduledAttempts, scans=GameObject.Scans;
        PendingPolls(100);
        Check("distant sources stay dormant without readiness scans",controller.ScheduledAttempts-calls==100&&ZNetScene.instance.AreaQueries==0&&GameObject.Scans==scans);
        foreach(var tree in trees)Check("uncovered distant sources stay unowned",tree.GetOwner()==0);
        MovePeer(ZNet.instance.Peers[0],512);Tick(5.05f);Tick(8.05f);
        foreach(var tree in trees)Check("approach still wakes dormant sources",tree.GetOwner()==1);
        PendingFresh(Kinds.Pickables);
        ZNetScene.instance.Ready=delegate{throw new Exception("Default ownership must not wait for unrelated objects.");};
        var objects=new List<ZDO>();for(int i=0;i<100;i++)objects.Add(Object(typeof(Pickable),40,1,0));
        calls=controller.ScheduledAttempts;PendingPolls(100);
        foreach(var data in objects)Check("initial default is assigned without area readiness",data.GetOwner()==99);
        Check("initial defaults need one attempt per object and zero area queries",controller.ScheduledAttempts==calls+100&&ZNetScene.instance.AreaQueries==0);
        ZNet.instance.Peers.Clear();calls=controller.ScheduledAttempts;PendingPolls(100,5f);
        Check("no peers leave no loading-poll work",controller.ScheduledAttempts==calls&&ZNetScene.instance.AreaQueries==0);
        PendingFresh(Kinds.Pickables);
        var outer = Object(typeof(Pickable), 9, 1, 99);
        calls = controller.ScheduledAttempts; PendingPolls(59);
        Check("yellow field only attempts once while waiting for its existing three-second deadline",
            controller.ScheduledAttempts == calls + 1 && outer.GetOwner() == 99);
        Tick(3f);
        Check("yellow deadline does not add another delay", outer.GetOwner() == 1 && controller.ScheduledAttempts == calls + 2);

        PendingFresh(Kinds.Pickables);
        outer = Object(typeof(Pickable), 9, 1, 99); Tick(.05f); calls = controller.ScheduledAttempts;
        MovePeer(ZNet.instance.Peers[0], 8); Tick(.1f);
        Check("yellow-to-red acceleration executes in the same pass", outer.GetOwner() == 1 && controller.ScheduledAttempts == calls + 1);

        PendingFresh(Kinds.Pickables);
        outer = Object(typeof(Pickable), 9, 1, 99); Tick(.05f);
        controller.ConfigureDelays(2, 0, .2f, .1f); Tick(.1f);
        Check("live settings retain the already running field deadline", outer.GetOwner() == 99);
        Tick(3f);
        Check("retained deadline does not add the new configured delay", outer.GetOwner() == 1);
        Console.WriteLine("PASS: " + (checks - before) + " Pending-work assertions: fixed-source dormancy, no area gates, single-pass defaults, field acceleration, live delays and zero-peer idle.");
    }
}
