using System;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static void KinematicDoorDefaults()
    {
        var patcher=new Harmony("botanic.authority.test.kinematic-door");
        patcher.Patch(AccessTools.Method(typeof(ZDOMan),"ReleaseNearbyZDOS"),transpiler:new HarmonyMethod(AccessTools.Method(typeof(OwnershipPatch),"Transpiler")));
        try
        {
            Fresh(Kinds.Doors,defaultInteractives:true);var player=Peer(1);Tick(0);
            var data=Object(typeof(Door),40,1,0);var view=ZNetScene.instance.FindInstance(data);var root=view.gameObject;
            var animated=new GameObject();animated.transform.parent=view.transform;root.Children.Add(animated);var body=animated.Add(new Rigidbody{isKinematic=true,useGravity=false});
            controller.Unregister(view);controller.Register(data,view);
            ZNetScene.instance.Ready=delegate{return false;};int queries=ZNetScene.instance.AreaQueries;
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);
            Check("native fixed door with animated kinematic child starts at server",data.GetOwner()==99&&data.Writes==1);
            Check("kinematic door initial default adds no loading scan or RPC",ZNetScene.instance.AreaQueries==queries&&player.m_rpc.Calls==0);
            root.activeInHierarchy=false;Check("inactive door template keeps fixed-body classification",ObjectCatalog.Stationary(root,Kinds.Doors));root.activeInHierarchy=true;
            body.isKinematic=false;Check("dynamic child body remains excluded",!ObjectCatalog.Stationary(root,Kinds.Doors));body.isKinematic=true;
            body.useGravity=true;Check("gravity child remains excluded",!ObjectCatalog.Stationary(root,Kinds.Doors));body.useGravity=false;
            var sync=animated.Add(new ZSyncTransform());Check("synchronized child physics remains excluded",!ObjectCatalog.Stationary(root,Kinds.Doors));animated.Components.Remove(sync);
            var rootBody=root.Add(new Rigidbody());Check("root body remains excluded even if initially kinematic",!ObjectCatalog.Stationary(root,Kinds.Doors));root.Components.Remove(rootBody);
            root.GetComponent<WearNTear>().m_staticPosition=false;Check("movable building root remains excluded",!ObjectCatalog.Stationary(root,Kinds.Doors));root.GetComponent<WearNTear>().m_staticPosition=true;
            Check("kinematic exception does not extend to other categories or mobs",!ObjectCatalog.Stationary(root,Kinds.Fireplaces)&&!ObjectCatalog.Stationary(root,Kinds.TamedAnimals)&&!ObjectCatalog.Stationary(root,Kinds.Vehicles));
            int scans=GameObject.Scans,lookups=GameObject.Lookups;
            for(int i=0;i<100;i++)Check("door initial eligibility remains cached",controller.NativeOwnerTarget(data,1)==99);
            Check("cached fixed-door assignments add no hierarchy work",scans==GameObject.Scans&&lookups==GameObject.Lookups);
            data.Created=false;data.Type=ZDO.ObjectType.Solid;ZoneSystem.instance.Loaded=false;ZoneSystem.instance.TypeReady=false;
            MovePeer(player,32);
            string status=controller.OwnerStatus(1,"piece");
            Check("manual snapshot identifies the actual native creation gate",status.Contains("type=Solid; created=False; zoneLoaded=False; typeReady=False")&&status.Contains("defaultMobs=False"));
            Check("creation diagnostics do not change owner or creation flags",data.GetOwner()==99&&data.Writes==1&&!data.Created);
        }
        finally{patcher.UnpatchSelf();}
    }
    private static void WorldDefaultAssignments()
    {
        var patcher=new Harmony("botanic.authority.test.world-default");
        patcher.Patch(AccessTools.Method(typeof(ZDOMan),"ReleaseNearbyZDOS"),transpiler:new HarmonyMethod(AccessTools.Method(typeof(OwnershipPatch),"Transpiler")));
        try
        {
            Fresh(Kinds.WorldMarkers,defaultInteractives:true);var player=Peer(1);Tick(0);Tick(2);
            var marker=Object(typeof(Vegvisir),1,1,0);var entrance=Object(typeof(Teleport),2,1,0);
            var distant=Object(typeof(Vegvisir),3,1,0);ZNetScene.instance.FindInstance(distant).m_distant=true;
            var original=Object(typeof(Vegvisir),4,1,1);
            ZNetScene.instance.Ready=delegate{return false;};int reads=ZNetScene.instance.AreaQueries;
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);
            Check("existing Vegvisir ZDO starts server-owned even inside a player field",marker.GetOwner()==99&&marker.Writes==1);
            Check("existing dungeon entrance ZDO starts server-owned",entrance.GetOwner()==99&&entrance.Writes==1);
            Check("distant marker ZDO can receive server default without a grid node",distant.GetOwner()==99&&distant.Writes==1);
            Check("first-assignment selector does not steal an active player marker",original.GetOwner()==1&&original.Writes==0);
            Check("world-marker defaults do not query loading readiness",ZNetScene.instance.AreaQueries==reads);
            Check("world-marker classification adds no interactive grid nodes",controller.RequestNode(marker.m_uid,"RPC_Remove".GetStableHashCode())==null);
            ZNetScene.instance.Prefabs[marker.Hash].activeInHierarchy=false;
            Check("inactive templates keep correct marker root",ObjectCatalog.Classify(ZNetScene.instance.Prefabs[marker.Hash])==Kinds.WorldMarkers);
            var local=new GameObject();local.Add(new Vegvisir());
            Check("local marker without a ZDO is left local",ObjectCatalog.Classify(local)==Kinds.None);
            var root=new GameObject();root.Add(new ZNetView());
            var child=new GameObject();child.transform.parent=root.transform;child.Add(new ZNetView());child.Add(new Vegvisir());
            Check("a child's independent marker ZDO cannot claim its parent",ObjectCatalog.Classify(root)==Kinds.None);
            ZNetScene.instance.Prefabs[marker.Hash].activeInHierarchy=true;
            int writes=marker.Writes+entrance.Writes+distant.Writes;int updates=controller.FieldUpdates;
            for(int i=3;i<12;i++){Tick(i);ZDOMan.instance.ReleaseNearbyZDOS(new Vector3(512,0,512),99);}
            Check("stable server markers add no ownership writes or grid activity",writes==marker.Writes+entrance.Writes+distant.Writes&&updates==controller.FieldUpdates);
            ZNet.instance.Peers.Clear();Tick(12);ZDOMan.instance.ReleaseNearbyZDOS(new Vector3(512,0,512),99);
            Check("native marker release survives an unloaded region",marker.GetOwner()==0&&entrance.GetOwner()==0&&distant.GetOwner()==0);
            Fresh(Kinds.WorldMarkers,defaultInteractives:false);player=Peer(1);marker=Object(typeof(Vegvisir),1,1,0);Tick(0);
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);Check("default off preserves native world-marker assignment",marker.GetOwner()==1);
            Fresh(Kinds.Doors,defaultInteractives:true);player=Peer(1);marker=Object(typeof(Vegvisir),1,1,0);Tick(0);
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);Check("disabled marker category preserves native assignment",marker.GetOwner()==1);
        }
        finally{patcher.UnpatchSelf();}
    }
#if GRID_PROTOCOL
    private static void IncineratorStateProtection()
    {
        Fresh(Kinds.Incinerators,defaultInteractives:true);var player=Peer(1);var old=Peer(7,64);
        var data=Object(typeof(Incinerator),1,1,99);var view=ZNetScene.instance.FindInstance(data);
        var station=view.GetComponent<Incinerator>();var inventory=station.m_container;
        GridStart();
        try
        {
            GridReceive(player,GridPacket(0,delegate(ZPackage p){p.Write(3);}));GridHello(old,true);
            Check("incinerator guard binds the exact native boolean",IncineratorProtection.Supported&&IncineratorProtection.Active);
            Check("old clients remain capable for existing features but not burning protection",TooManyLags.GridHandoffs.Protocol.Capable(old)&&!IncineratorProtection.Client(old));
            Check("updated client advertises incinerator protection",IncineratorProtection.Client(player));
            station.Burning(true);Tick(0);Tick(2);
            Check("burning server incinerator cannot leave server ownership",data.GetOwner()==99&&data.Writes==0);
            int denies=0;view.OnInvoke=delegate(long target,string method,object[] args){Check("burn refusal uses native negative response",target==1&&!(bool)args[0]);denies++;};
            inventory.RPC_RequestOpen(1,0);inventory.RPC_RequestStack(1,123);inventory.RPC_RequestTakeAll(1,123);
            Check("open, stack and take-all cannot bypass a burning source",denies==3&&inventory.NativeRequests==0&&data.GetOwner()==99);
            inventory.RPC_RequestOpen(99,0);Check("current owner's native inventory path remains intact",inventory.NativeRequests==1);
            Check("state reader never mutates native container-use state",station.Using()&&!inventory.IsInUse());
            station.Burning(false);inventory.RPC_RequestOpen(1,0);Check("completion or abort releases native requests immediately",inventory.NativeRequests==2&&denies==3);
            int heldAttempts=controller.ScheduledAttempts;Tick(3);Tick(4);
            Check("completion alone does not replay the blocked ownership request",data.GetOwner()==99&&controller.ScheduledAttempts==heldAttempts);
            MovePeer(player,8);Tick(5);Check("a new covering player update retries the idle incinerator",data.GetOwner()==1);
            int writes=data.Writes,scans=GameObject.Scans,queries=ZNetScene.instance.AreaQueries,attempts=controller.ScheduledAttempts;
            for(int i=6;i<15;i++)Tick(i);
            Check("settled incinerator adds no scans, loading queries or ownership jobs",data.Writes==writes&&GameObject.Scans==scans&&ZNetScene.instance.AreaQueries==queries&&controller.ScheduledAttempts==attempts);
            // The same local state must veto at a CLIENT owner, not at its
            // server replica. Native source consent is the decisive check.
            long session=ZDOMan.SessionID;ZDOMan.SessionID=1;station.Burning(true);
            view.OnInvoke=delegate(long target,string method,object[] args){Check("client owner rejects server acquisition",target==99&&method=="RPC_OpenResponse"&&!(bool)args[0]);denies++;};
            inventory.RPC_RequestOpen(99,0);Check("client-local burn vetoes a server takeover",denies==4&&inventory.NativeRequests==2);
            station.Burning(false);inventory.RPC_RequestOpen(99,0);Check("client-local completion permits vanilla consent again",inventory.NativeRequests==3);ZDOMan.SessionID=session;
            data.SetOwner(7);controller.OwnerChanged(data);Tick(15);Tick(17);
            Check("an old unprotected client owner is never forcibly acquired",data.GetOwner()==7);
            int acquisitions=0;
            view.OnInvoke=delegate(long target,string method,object[] args)
            {
                Check("guarded incinerator acquisition still uses native source consent",target==7&&method=="RPC_RequestOpen"&&(long)args[0]==0);acquisitions++;
                controller.InventoryResponse(inventory,7,false);
            };
            GridReceive(old,GridPacket(0,delegate(ZPackage p){p.Write(3);}));Tick(18);Tick(20);
            Check("source refusal retires the automatic request",acquisitions==1&&data.GetOwner()==7);
            heldAttempts=controller.ScheduledAttempts;Tick(22);Tick(25);
            Check("a source refusal causes no repeated job or source query while standing",acquisitions==1&&controller.ScheduledAttempts==heldAttempts);
            view.OnInvoke=delegate(long target,string method,object[] args)
            {
                Check("fresh source request targets the current owner",target==7&&method=="RPC_RequestOpen");acquisitions++;
                data.SetOwner(99);controller.OwnerChanged(data);controller.InventoryResponse(inventory,7,true);
            };
            MovePeer(player,0);Tick(26);Tick(28);
            Check("new covering movement can obtain fresh native consent",acquisitions==2&&data.GetOwner()==1);
            data.SetOwner(99);controller.OwnerChanged(data);inventory.InUse=true;Tick(29);Tick(30);
            heldAttempts=controller.ScheduledAttempts;inventory.InUse=false;Tick(31);Tick(32);
            Check("closing an incinerator inventory alone does not replay a blocked transfer",data.GetOwner()==99&&controller.ScheduledAttempts==heldAttempts);
            MovePeer(player,8);Tick(33);Check("new request after inventory close can transfer",data.GetOwner()==1);
            var plain=Object(typeof(Container),2,1,99);var chest=ZNetScene.instance.FindInstance(plain).GetComponent<Container>();
            chest.RPC_RequestOpen(1,0);Check("ordinary chest opening remains native",chest.NativeRequests==1);
            data.SetOwner(99);controller.OwnerChanged(data);station.Burning(true);Tick(34);Tick(35);
            Check("busy incinerator enters sparse request wait",((System.Collections.IDictionary)typeof(AuthorityController).GetField("waitingIncinerators",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(controller)).Count==1);
            controller.Unregister(view);int remaining=controller.ScheduledAttempts;Tick(36);
            Check("unloaded incinerator has no pending ownership job",controller.ScheduledAttempts==remaining);
            Check("unloading removes sparse request wait entries",((System.Collections.IDictionary)typeof(AuthorityController).GetField("waitingIncinerators",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(controller)).Count==0);
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();ZDOMan.SessionID=99;}
        Check("protocol shutdown clears incinerator capability and activation",!IncineratorProtection.Active&&IncineratorProtection.Client==null);
    }
#endif
}
