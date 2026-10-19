using System;
using TooManyLagsStandalone;
using UnityEngine;

// Real MobPolicy, LocalAgent and Handoffs; controlled transport/scene/controller
// boundaries. The dedicated coordinator is forbidden to inspect live instances.
internal static class MobIntegrationTests
{
    private static int checks;
    private static bool dedicated;
    private static LocalAgent source, receiver;
    private static AuthorityController controller;
    private static void Check(bool value,string reason) { checks++;if(!value)throw new Exception(reason); }
    private static ZPackage Copy(ZPackage p) { return new ZPackage(p.GetArray()); }
    private static Network.Envelope Last(Message message)
    { for(int i=Plugin.Network.Sent.Count-1;i>=0;i--)if(Plugin.Network.Sent[i].Message==message)return Plugin.Network.Sent[i];throw new Exception("Missing "+message); }
    private static void Server()
    { ZNet.instance.Server=true;ZNet.instance.Dedicated=dedicated;ZDOMan.Session=dedicated?999:11;Plugin.Local=dedicated?null:source;ZNetScene.instance.ForbidLiveInstances=dedicated; }
    private static void Client(long uid,LocalAgent agent)
    { ZNet.instance.Server=false;ZNet.instance.Dedicated=false;ZDOMan.Session=uid;Plugin.Local=agent;ZNetScene.instance.ForbidLiveInstances=false; }
    private static ZNetView Object(long id,int prefab,long owner)
    {
        var root=new GameObject();var view=root.Add<ZNetView>();
        view.Data=new ZDO { m_uid=new ZDOID(id),Prefab=prefab,Owner=owner };
        ZDOMan.instance.Objects.Add(view.Data.m_uid,view.Data);ZNetScene.instance.Views.Add(view.Data.m_uid,view);return view;
    }
    private static void Report(long owner)
    {
        var report=Last(Message.Target);var packet=Copy(report.Data);Server();
        controller.Mobs.Report(owner,packet.ReadZDOID(),packet.ReadUShort(),packet.ReadZDOID());
    }
    private static void Run(bool headless)
    {
        dedicated=headless;Time.unscaledTime=0;Time.frameCount++;
        ZNet.IsSinglePlayer=false;ZNet.instance=new ZNet();ZDOMan.instance=new ZDOMan();ZNetScene.instance=new ZNetScene();
        ZoneSystem.instance=new ZoneSystem();InventoryGui.instance.Current=null;MobPolicy.ResetPrefabs();RequestRouter.Reset();
        Plugin.Network=new Network();Plugin.Options.Mobs=true;Plugin.Options.HighPing=200;Plugin.Options.MobRadius=6;
        controller=Plugin.Controller=new AuthorityController();source=new LocalAgent();receiver=new LocalAgent();
        var prefab=new GameObject();prefab.Add<Humanoid>();prefab.Add<MonsterAI>();ZNetScene.instance.Prefabs.Add(101,prefab);
        var a=Object(11,201,11);var playerA=a.gameObject.Add<Player>();playerA.Side=Character.Faction.Players;
        var b=Object(22,201,22);var playerB=b.gameObject.Add<Player>();playerB.Side=Character.Faction.Players;
        controller.Players.Add(new PlayerPeer { Uid=11,Character=a.Data });controller.Players.Add(new PlayerPeer { Uid=22,Character=b.Data });
        var view=Object(1,101,11);var actor=view.gameObject.Add<Humanoid>();var ai=view.gameObject.Add<MonsterAI>();ai.Init();ai.Target=playerB;
        Client(11,source);source.SetMobWatch(true);source.TargetChanged(ai);
        Check(view.Data.GetZDOID(MobPolicy.TargetKey)==b.Data.m_uid,"Source publishes the actual player target through the real policy key");
        Report(11);Check(controller.Mobs.Desired(view.Data,b.Data.m_uid)==22,"Real mob policy selects the target's player, without simulation ownership");
        controller.Mobs.Tick(0);Check(controller.Handoffs.Pending(view.Data.m_uid),"Real policy starts the real source-confirmed handoff");
        controller.Handoffs.Tick(0);var prepare=Last(Message.Prepare);
        Check(prepare.Uid==22,"Dedicated server/listen host prepares the target player, never a headless simulation endpoint");
        Client(22,receiver);receiver.SetMobWatch(true);receiver.Receive(Message.Prepare,Copy(prepare.Data));var prepared=Last(Message.Prepared);
        Server();controller.Handoffs.Receive(22,Message.Prepared,Copy(prepared.Data));controller.Handoffs.Tick(.1f);var commit=Last(Message.Commit);
        Check(commit.Uid==11,"Only the actual old owner receives the commit request");
        Client(11,source);actor.CurrentAttack=new Attack();source.Receive(Message.Commit,Copy(commit.Data));var busy=Last(Message.Result);
        Server();controller.Handoffs.Receive(11,Message.Result,Copy(busy.Data));Check(view.Data.Owner==11,"Actual native Attack veto reaches the coordinator's real handoff transaction");
        Client(11,source);actor.CurrentAttack.Done=true;Time.unscaledTime=.2f;source.Tick(.2f);var wake=Last(Message.Wake);
        Server();controller.Handoffs.Receive(11,Message.Wake,Copy(wake.Data));controller.Handoffs.Tick(.2f);prepare=Last(Message.Prepare);
        Client(22,receiver);receiver.Receive(Message.Prepare,Copy(prepare.Data));prepared=Last(Message.Prepared);
        Server();controller.Handoffs.Receive(22,Message.Prepared,Copy(prepared.Data));controller.Handoffs.Tick(.3f);commit=Last(Message.Commit);
        Client(11,source);source.Receive(Message.Commit,Copy(commit.Data));var result=Last(Message.Result);
        Server();controller.Handoffs.Receive(11,Message.Result,Copy(result.Data));
        Check(view.Data.Owner==22 && view.Data.OwnerRevision==1,"Idle mob commits exactly once from player to player");
        Check(controller.Handoffs.Committing(view.Data.m_uid),"Coordinator still waits for the real recipient activation");
        Client(22,receiver);receiver.Observe(view.Data);var active=Last(Message.Activated);
        Check(ai.Target==playerB && ai.Alerted,"New client owner adopts the same actual target without server AI");
        Server();controller.Handoffs.Receive(22,Message.Activated,Copy(active.Data));Check(!controller.Handoffs.Committing(view.Data.m_uid),"Recipient acknowledgement settles the real transaction");

        Plugin.Network.Latencies[22]=250;Plugin.Network.Latencies[11]=headless?30:0;
        Check(controller.Mobs.Desired(view.Data,b.Data.m_uid)==11,"High-ping target uses a better-connected nearby player");
        a.Data.Point=new Vector3 { x=7 };Check(controller.Mobs.Desired(view.Data,b.Data.m_uid)==22,"Out-of-radius player cannot replace the target owner");a.Data.Point=new Vector3();
        Plugin.Network.Latencies[22]=50;Check(controller.Mobs.Desired(view.Data,b.Data.m_uid)==22,"Ordinary target ping keeps target ownership, including on a local host");
        Client(22,receiver);ai.Target=playerA;receiver.TargetChanged(ai);Report(22);
        Check(controller.Mobs.Desired(view.Data,a.Data.m_uid)==11,"A real owner report changes the policy to the new player target");
        controller.Mobs.Report(11,view.Data.m_uid,0,b.Data.m_uid);
        Check(controller.Mobs.Desired(view.Data,a.Data.m_uid)==11,"Stale former-owner report cannot revert the new target");
        controller.Mobs.Report(22,view.Data.m_uid,view.Data.OwnerRevision,ZDOID.None);
        Check(!controller.Mobs.Managed(view.Data.m_uid,"RPC_Damage".GetStableHashCode()),"Target loss retires policy and forwarding state");
        view.Data.Set(ZDOVars.s_tamed,1);Check(!MobPolicy.Eligible(view.Data),"Tamed animals remain outside the hostile target policy");view.Data.Set(ZDOVars.s_tamed,0);
        var friendly=new GameObject();friendly.Add<Humanoid>().Side=Character.Faction.Players;friendly.Add<MonsterAI>();ZNetScene.instance.Prefabs.Add(102,friendly);
        view.Data.Prefab=102;Check(!MobPolicy.Eligible(view.Data),"Friendly summons remain native under the real prefab filter");
        var boss=new GameObject();boss.Add<Humanoid>().Boss=true;boss.Add<MonsterAI>();ZNetScene.instance.Prefabs.Add(103,boss);
        view.Data.Prefab=103;Check(!MobPolicy.Eligible(view.Data),"Boss ownership remains native as documented");
        Check(!headless || !controller.Available(999,new Vector3()),"Dedicated coordinator cannot become a simulation destination");
        controller.Handoffs.Clear();controller.Mobs.Clear();ZNetScene.instance.ForbidLiveInstances=false;
    }
    public static void Main()
    { try { Run(true);Run(false);Console.WriteLine("PASS: "+checks+" production mob policy/source/transaction integration checks; dedicated coordinator without live instances and local-host ownership, real target reports, native attack veto, player commit, activation, ping fallback, lifecycle and native exclusions. Controlled transport/Unity boundaries."); } catch(Exception error) { Console.Error.WriteLine(error);Environment.ExitCode=1; } }
}
