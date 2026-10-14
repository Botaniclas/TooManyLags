using System;
using System.Collections.Generic;
using System.Reflection;
using TooManyLags;
using HarmonyLib;
using UnityEngine;

public sealed class ZRpc
{
    public int Calls;
    public Action<string,object[]> OnInvoke;
    public void Invoke(string name,params object[] values){Calls++;if(OnInvoke!=null)OnInvoke(name,values);}
    private readonly Dictionary<string,Delegate> handlers=new Dictionary<string,Delegate>();
    public void Register<T>(string name,Action<ZRpc,T> handler){handlers[name]=handler;}
    public void Receive<T>(string name,T value){((Action<ZRpc,T>)handlers[name])(this,value);}
}
internal static class StringHash { public static int GetStableHashCode(this string value){return value.GetHashCode();} }
namespace TooManyLags
{
    // Camera settings transport is exercised by its dedicated fixture.
    internal static class ConfigConsole { internal static void RequestCamera() { } }
    internal static class Plugin
    {
        internal const string Id="botanic.toomanylags.fgnaddon",FgnId="com.Fire.FiresGhettoNetworkMod";
        internal static AuthorityController Controller;
        internal static void SampleCamera() { }
        internal static void InvalidateCamera() { }
        internal static readonly List<string> Warnings = new List<string>();
        internal static void Warn(string value) { Warnings.Add(value); }
    }
}
namespace FiresGhettoNetworkMod
{
    internal static class FgnVersionGate { internal static bool Match; internal static bool IsMatched(ZNetPeer peer){return Match;} }
    internal static class CreatureOwnership
    {
        private static void OnNewConnection(ZNet network,ZNetPeer peer){}
        private static void OnHandOff(ZRpc rpc,ZDOID id,long target){}
        private static void OnResult(ZRpc rpc,ZDOID id,bool accepted,string reason){}
        private static string Veto(ZDO data,long target){return null;}
    }
}
internal static partial class IntegrationTests
{
    private static int checks, next, failures;
    private static AuthorityController controller;
    private static bool loadingObserver;
    private static ZNetPeer observer;
    private static void Check(string label,bool result){checks++;if(!result)throw new Exception(label);}
    private static void Fresh(Kinds kinds,bool statics=false,bool defaultInteractives=false,bool refillStations=false,float cellSize=8,GridCoveragePattern pattern=GridCoveragePattern.Square3x3,bool keepOnExit=true,bool directional=false,float innerDelay=0,float outerDelay=3,float checkIntervalSeconds=1,float forwardDelay=.2f,bool nativeSolo=false,float overlapCooldown=0,bool defaultMobs=false)
    {
        if(controller!=null)controller.Reset();
        next=1;failures=0;Time.time=Time.unscaledTime=0;
        loadingObserver=!nativeSolo&&!defaultInteractives;observer=null;
        ZNet.instance=new ZNet();ZDOMan.instance=new ZDOMan();ZNetScene.instance=new ZNetScene();ZoneSystem.instance=new ZoneSystem();ZRoutedRpc.instance=new ZRoutedRpc();
        ZNet.IsSinglePlayer=false;
        FiresGhettoNetworkMod.FgnVersionGate.Match=false;
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority.Value=true;
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership.Value=false;
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective.Value=false;
        FiresGhettoNetworkMod.StationRouter.ConfigEnabled.Value=true;
        FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive=true;
        FiresGhettoNetworkMod.HelmOwnership.ServerOwnsShips=false;
        var fgn=new FiresGhettoNetworkMod.FiresGhettoNetworkMod();
        controller=new AuthorityController(kinds,2,new CompatibilityBridge(fgn),delegate{failures++;},statics,delegate{failures++;},checkIntervalSeconds,defaultInteractives,refillStations,cellSize,pattern,keepOnExit,directional,innerDelay,outerDelay,forwardDelay,overlapCooldown,defaultMobs);
        Plugin.Controller=controller;
    }
    private static void Tick(float now){Time.time=Time.unscaledTime=now;controller.Tick();Check("controller active",controller.IsActive&&failures==0);}
    private static ZNetPeer Peer(long uid,float x=0,float z=0)
    {
        var data=new ZDO{m_uid=new ZDOID{ID=next++},PersistentValue=false,Position=new Vector3(x,0,z)};
        ZDOMan.instance.Objects.Add(data);var peer=new ZNetPeer{Uid=uid,Position=data.Position,m_characterID=data.m_uid};ZNet.instance.Peers.Add(peer);
        // Legacy fine-grid cases explicitly keep a second ready connection
        // loading nearby. It cannot contribute a footprint without a character.
        // The new nativeSolo cases below exercise actual solo bypass instead.
        if(loadingObserver&&(observer==null||!ZNet.instance.Peers.Contains(observer)))
        {observer=new ZNetPeer{Uid=123456,Position=data.Position};ZNet.instance.Peers.Add(observer);}
        return peer;
    }
    private static void MovePeer(ZNetPeer peer,float x,float z=0)
    {peer.Position=new Vector3(x,0,z);ZDOMan.instance.GetZDO(peer.m_characterID).Position=peer.Position;if(loadingObserver&&observer!=null&&ZNet.instance.Peers[0]==peer)observer.Position=peer.Position;}
    private static ZDO Object(Type type,float x=1,float z=1,long owner=7)
    {
        var root=new GameObject{name="piece"+next};root.Add(new Piece());root.Add(new WearNTear());
        var view=root.Add(new ZNetView());
        if(type!=null)root.Add((Component)Activator.CreateInstance(type));
        if(type==typeof(Character))root.Add(new Tameable());
        if(type==typeof(Smelter))root.Add(new Switch());
        root.transform.position=new Vector3(x,0,z);
        var data=new ZDO{m_uid=new ZDOID{ID=next++},Hash=100+next,Position=root.transform.position};data.SetOwner(owner);data.Writes=0;view.Data=data;
        ZDOMan.instance.Objects.Add(data);ZNetScene.instance.Views.Add(data,view);ZNetScene.instance.Prefabs.Add(data.Hash,root);controller.Register(data,view);return data;
    }
    private static ZRoutedRpc.RoutedRPCData Request(ZDO data,string name,long target)
    {return new ZRoutedRpc.RoutedRPCData{m_msgID=123,m_senderPeerID=1,m_targetPeerID=target,m_targetZDO=data.m_uid,m_methodHash=name.GetStableHashCode()};}
    private static void Import(ZDO data,long target,bool revisionFirst)
    {
        ushort revision=(ushort)(data.OwnerRevision+1);
        if(revisionFirst)data.OwnerRevision=revision;else data.SetOwnerInternal(target);
        controller.OwnerChanged(data,!revisionFirst);
        Check("half packet does not become a receipt",!controller.Inventory.Received(data)&&!controller.Cooperative.Received(data));
        if(revisionFirst)data.SetOwnerInternal(target);else data.OwnerRevision=revision;
        controller.OwnerChanged(data,revisionFirst);
    }
    private static void Fields()
    {
        Fresh(Kinds.Doors);Peer(1);var pieces=new List<ZDO>();
        for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)pieces.Add(Object(typeof(Door),x*8+1,z*8+1));
        Tick(0);Tick(2);foreach(var data in pieces)Check("all nine cells transfer",data.GetOwner()==1);
        int writes=0;foreach(var data in pieces)writes+=data.Writes;
        int scans=GameObject.Scans;int reads=ZNetScene.instance.AreaQueries;
        for(int i=3;i<23;i++)Tick(i);
        int after=0;foreach(var data in pieces)after+=data.Writes;
        Check("stable cells do no object rescan or owner writes",after==writes&&GameObject.Scans==scans&&ZNetScene.instance.AreaQueries==reads);
        var second=Peer(2,16,0);Tick(23);Tick(25);
        foreach(var data in pieces)Check("only cell intersections server owned",data.GetOwner()==(data.Position.x>=8?99:1));
        ZNet.instance.Peers.Remove(second);Tick(26);Tick(28);
        foreach(var data in pieces)Check("single covering peer regains settled candidate",data.GetOwner()==1);
        ZNet.instance.Peers.Clear();Tick(29);Tick(31);
        foreach(var data in pieces)Check("exit does not restore old owner",data.GetOwner()==1);
    }
    private static void Inventories()
    {
        Fresh(Kinds.Containers);Peer(1);Peer(7,64,0);var data=Object(typeof(Container));var view=ZNetScene.instance.FindInstance(data);var chest=view.GetComponent<Container>();int requests=0;
        view.OnInvoke=delegate(long target,string method,object[] args){Check("native public query",target==7&&method=="RPC_RequestOpen"&&(long)args[0]==0);requests++;};
        Tick(0);Tick(2);Check("no overwrite while source decides",requests==1&&data.GetOwner()==7&&data.Writes==0);
        controller.InventoryResponse(chest,7,false);Tick(3);Check("denied native request does not steal",data.GetOwner()==7);
        Tick(4);Check("native denial retries after backoff",requests==2);
        data.SetOwner(99);controller.OwnerChanged(data);Tick(4.5f);Check("owner data alone cannot authorize onward transfer",data.GetOwner()==99);
        controller.InventoryResponse(chest,7,true);Tick(5);Check("ack and revision permit onward transfer",data.GetOwner()==1);
        data.Use=1;Peer(2,1,1);Tick(6);Tick(8);Check("replicated open flag protects already-open chest",data.GetOwner()==1);
        data.Use=0;view.OnInvoke=delegate(long target,string method,object[] args){requests++;};Tick(9);
        Check("closing re-enables native source request",requests==3&&data.GetOwner()==1);
        controller.Unregister(view);Check("unload cancels reservations",!controller.Inventory.Pending(data));
        Fresh(Kinds.Containers);Peer(1);Peer(7,64,0);data=Object(typeof(Container));view=ZNetScene.instance.FindInstance(data);view.GetComponent<Container>().m_privacy=Container.PrivacySetting.Private;
        Tick(0);Tick(2);Check("private container stays native",data.GetOwner()==7&&data.Writes==0);
        Fresh(Kinds.Containers);Peer(1);data=Object(typeof(Container),1,1,1);view=ZNetScene.instance.FindInstance(data);requests=0;
        view.OnInvoke=delegate(long target,string method,object[] args){requests++;};Tick(0);Tick(2);Tick(3);
        Check("already desired client chest never queried or moved",requests==0&&data.GetOwner()==1&&data.Writes==0);
        Fresh(Kinds.Containers);Peer(1);Peer(7,64,0);data=Object(typeof(Container));view=ZNetScene.instance.FindInstance(data);chest=view.GetComponent<Container>();
        Tick(0);Tick(2);data.SetOwner(99);controller.OwnerChanged(data);Tick(8);
        Check("expired reply for complete idle server receipt resumes onward transfer",data.GetOwner()==1&&!controller.Inventory.Pending(data));
        controller.InventoryResponse(chest,7,true);Tick(9);
        Check("late old grant does not alter recovered player ownership",data.GetOwner()==1);
        foreach(bool reverse in new[]{false,true})
        {
            Fresh(Kinds.Containers);Peer(1);Peer(7,64,0);data=Object(typeof(Container));view=ZNetScene.instance.FindInstance(data);chest=view.GetComponent<Container>();Tick(0);Tick(2);
            Import(data,99,reverse);Check("completed native receipt pinned before ack",!controller.Persistent(data));
            controller.InventoryResponse(chest,7,true);Tick(3);Check("either native field order completes without extra delay",data.GetOwner()==1);
        }
    }
    private static void StationsAndActors()
    {
        Fresh(Kinds.Smelters|Kinds.TamedAnimals,refillStations:true);Peer(1);var station=Object(typeof(Smelter));var tame=Object(typeof(Character));tame.Tamed=true;controller.DataChanged(tame);var wild=Object(typeof(Character));wild.Tamed=false;controller.DataChanged(wild);
        Tick(0);Tick(2);Check("refill station permanent server",station.GetOwner()==99);Check("tame follows single field",tame.GetOwner()==1);Check("wild stays native",wild.GetOwner()==7);
        var peer2=Peer(2,1,1);var saddle=ZNetScene.instance.FindInstance(tame).GetComponent<Tameable>();saddle.m_saddle=saddle.gameObject.Add(new Sadle{User=true});
        Tick(3);Tick(5);Check("ridden animal is not transferred",tame.GetOwner()==1);
        saddle.m_saddle.User=false;Tick(6);Check("dismounted animal uses server overlap",tame.GetOwner()==99);
        ZNet.instance.Peers.Remove(peer2);Tick(7);Tick(9);Check("station never moves back to peer",station.GetOwner()==99);
        Check("tame returns to current unique covering peer",tame.GetOwner()==1);
        station.OwnerRevision=UInt16.MaxValue;station.SetOwner(3);station.OwnerRevision=UInt16.MaxValue;controller.OwnerChanged(station);Tick(10);Tick(12);Check("addon never causes owner revision wrap",station.GetOwner()==3);
    }
    private static void ExperimentalProtectionDisabled()
    {
        Check("disabled extra protection installs no capability or recipient delegates",FgnOwnerHandoff.CooperativeClient==null&&FgnOwnerHandoff.IdentifiedSend==null&&FgnOwnerHandoff.IdentifiedStatus==null&&RequestRouter.RecipientReady==null);
        Fresh(Kinds.Doors|Kinds.Containers|Kinds.Smelters,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        Peer(1,40);var source=Peer(7,140);FiresGhettoNetworkMod.FgnVersionGate.Match=true;Tick(0);
        var door=Object(typeof(Door),41);var chest=Object(typeof(Container),41);var station=Object(typeof(Smelter),41);
        chest.Use=1;var stationNode=controller.RequestNode(station.m_uid,"RPC_AddFuel".GetStableHashCode());
        Check("station has a loaded refill node",stationNode!=null);stationNode.Refilled("RPC_AddFuel".GetStableHashCode());
        int handoffs=0,inventoryQueries=0;
        source.m_rpc.OnInvoke=delegate(string name,object[] values){
            Check("disabled mode uses the native two-argument FGN handoff",name=="FGN_HandOff"&&values.Length==2&&((ZDOID)values[0]).Equals(door.m_uid)&&(long)values[1]==1);handoffs++;
        };
        ZNetScene.instance.FindInstance(chest).OnInvoke=delegate(long target,string method,object[] args){
            Check("disabled mode retains native inventory acquisition",target==7&&method=="RPC_RequestOpen"&&(long)args[0]==0);inventoryQueries++;
        };
        Tick(.1f);Tick(1);
        Check("distant source can hand off without a TML capability greeting",handoffs==1&&controller.Cooperative.Pending(door)&&door.GetOwner()==7);
        Check("open inventory and refill lease remain protected with the extra guard off",chest.GetOwner()==7&&station.GetOwner()==7&&inventoryQueries==0);
        Import(door,1,false);Tick(2);uint state;
        Check("native receipt completes without adding recipient-readiness work",door.GetOwner()==1&&!controller.Cooperative.Pending(door)&&!controller.TryRecipientState(door,1,out state));
        ZDOMan.instance.MarkSent(1,door);
        Check("stable interaction stays on the native fast path",!controller.Router.TryHandle(ZRoutedRpc.instance,Request(door,"UseDoor",1),false)&&ZRoutedRpc.instance.Remote.Count==0);
        chest.Use=0;Tick(3);
        Check("closed inventory requests native consent instead of a forced owner write",inventoryQueries==1&&chest.GetOwner()==7&&chest.Writes==0);
        Check("ten-second refill lease is independent of the experimental mode",station.GetOwner()==7&&station.Writes==0);
    }
    private static void CooperativeAndRouting()
    {
        Fresh(Kinds.Doors);Peer(1);var source=Peer(7,64,0);FiresGhettoNetworkMod.FgnVersionGate.Match=true;var door=Object(typeof(Door));
        Tick(0);Tick(2);Check("matched FGN client gets one native handoff request",source.m_rpc.Calls==1&&door.GetOwner()==7);
        Tick(3);Check("pending FGN request is deduplicated",source.m_rpc.Calls==1);
        Import(door,1,false);Check("source receipt immediately pinned",!controller.Persistent(door));Tick(4);Check("source receipt settles owner",door.GetOwner()==1&&door.Writes==0);
        ZDOMan.instance.MarkSent(1,door);var packet=Request(door,"UseDoor",99);
        Check("stale server target corrected to actual client",controller.Router.TryHandle(ZRoutedRpc.instance,packet,true)&&ZRoutedRpc.instance.Remote.Count==1);
        Check("original packet preserved",packet.m_targetPeerID==99&&packet.m_msgID==123);
        var reply=Request(door,"RPC_OpenResponse",99);Check("responses never rerouted",!controller.Router.TryHandle(ZRoutedRpc.instance,reply,true));
        var broadcast=Request(door,"UseDoor",0);Check("broadcasts never rerouted",!controller.Router.TryHandle(ZRoutedRpc.instance,broadcast,false));
        Fresh(Kinds.Doors);Peer(1);Peer(2,1,1);door=Object(typeof(Door));Tick(0);Tick(2);
        packet=Request(door,"UseDoor",1);Check("stale client target corrected to actual server",controller.Router.TryHandle(ZRoutedRpc.instance,packet,false)&&ZRoutedRpc.instance.Local.Count==1);
        ZRoutedRpc.instance.OnHandle=delegate{throw new InvalidOperationException("native handler failure");};bool thrown=false;
        try{controller.Router.TryHandle(ZRoutedRpc.instance,packet,false);}catch(InvalidOperationException){thrown=true;}
        Check("dispatch failure never replays or disables ownership",thrown&&controller.IsActive&&ZRoutedRpc.instance.Local.Count==2);
        Fresh(Kinds.Doors);Peer(1);source=Peer(7,64,0);FiresGhettoNetworkMod.FgnVersionGate.Match=true;door=Object(typeof(Door));Tick(0);Tick(2);
        packet=Request(door,"UseDoor",7);Check("in-flight source transfer holds gameplay",controller.Router.TryHandle(ZRoutedRpc.instance,packet,false)&&ZRoutedRpc.instance.Remote.Count==0);
        controller.Cooperative.Result(new ZRpc(),door.m_uid,false,"held");controller.Router.Flush();Check("unrelated result cannot release route",ZRoutedRpc.instance.Remote.Count==0);
        controller.Cooperative.Result(source.m_rpc,door.m_uid,false,"held");ZDOMan.instance.MarkSent(7,door);controller.Router.Flush();
        Check("known source veto releases native call",ZRoutedRpc.instance.Remote.Count==1&&!controller.Cooperative.Pending(door));
        Check("veto cooldown still pins native source ownership",!controller.Persistent(door));Tick(3);Check("veto cooldown sends no duplicate handoff",source.m_rpc.Calls==1);
        Tick(8);Check("after veto cooldown new request enters flight",source.m_rpc.Calls==2&&controller.Cooperative.Pending(door));
        controller.Cooperative.Result(source.m_rpc,door.m_uid,true,"");Check("accepted reply cannot substitute for revision",controller.Cooperative.Pending(door)&&!controller.Cooperative.Received(door));
        Import(door,1,true);Tick(9);Check("revision-first source commit settles after accepted result",door.GetOwner()==1&&!controller.Cooperative.Pending(door));
    }
    private static void BakedAndCompatibility()
    {
        Fresh(Kinds.Doors,true);Peer(1);var baked=Object(null);baked.Parked=true;
        Tick(0);Tick(2);Check("parked original excluded during save pose",controller.Persistent(baked)&&baked.GetOwner()==7);
        var staticPiece=Object(null);Check("ordinary static server ownership retained",!controller.Persistent(staticPiece)&&staticPiece.GetOwner()==99);
        var drop=Object(typeof(ItemDrop));Check("loose items excluded from grid",controller.RequestNode(drop.m_uid,"RPC_RequestOwn".GetStableHashCode())==null&&drop.GetOwner()==7);
        Fresh(Kinds.Incinerators);Peer(1);Peer(7,64,0);var burner=Object(typeof(Incinerator));Tick(0);Tick(2);
        Check("even explicitly enabled incinerator is never background transferred",burner.GetOwner()==7&&burner.Writes==0);
        var bridge=new CompatibilityBridge(new FiresGhettoNetworkMod.FiresGhettoNetworkMod());Check("works without any Pup plugin",bridge.Enabled());
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective.Value=true;Check("FGN replacing owner loop rejected",!bridge.Enabled());
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective.Value=false;
        FiresGhettoNetworkMod.StationRouter.ConfigEnabled.Value=false;Check("FGN insert protection required",!bridge.Enabled());
        FiresGhettoNetworkMod.StationRouter.ConfigEnabled.Value=true;
        controller.Reset();Check("scene shutdown clears native/Fgn reservations",!controller.IsActive&&controller.Inventory==null&&controller.Cooperative==null);
    }
    private static void OwnershipOptions()
    {
        // Both defaults false: stations use the same field policy as doors.
        Fresh(Kinds.Doors|Kinds.Smelters|Kinds.TamedAnimals);var player=Peer(1);Peer(7,64,0);
        var station=Object(typeof(Smelter));var far=Object(typeof(Door),40,1);var defaultOffTame=Object(typeof(Character),40,1);defaultOffTame.Tamed=true;controller.DataChanged(defaultOffTame);Tick(0);Tick(2);
        Check("default-off refill station belongs to nearby player",station.GetOwner()==1);
        Check("default-off uncovered interactive retains ownership",far.GetOwner()==7);
        Check("default-off uncovered tame retains ownership",defaultOffTame.GetOwner()==7);
        Peer(2,1,1);Tick(3);Tick(5);Check("default-off refill follows overlap server rule",station.GetOwner()==99);

        Fresh(Kinds.Doors|Kinds.Smelters|Kinds.TamedAnimals,defaultInteractives:true,keepOnExit:false);player=Peer(1);Peer(7,64,0);
        var near=Object(typeof(Door),1,1,1);far=Object(typeof(Door),40,1);station=Object(typeof(Smelter));
        var tame=Object(typeof(Character),40,1);tame.Tamed=true;controller.DataChanged(tame);
        Tick(0);Check("pending player coverage causes no pointless fallback transfer",near.GetOwner()==1&&near.Writes==0);
        Check("uncovered interactive receives server default",far.GetOwner()==99);
        Check("server default applies equally to uncovered idle tames",tame.GetOwner()==99);
        Tick(2);Check("server default still gives covered station to one player",station.GetOwner()==1);
        int scans=GameObject.Scans, area=ZNetScene.instance.AreaQueries, writes=far.Writes+near.Writes+station.Writes+tame.Writes;
        for(int i=3;i<13;i++)Tick(i);
        Check("stable server defaults cause no repeated scans area checks or writes",GameObject.Scans==scans&&ZNetScene.instance.AreaQueries==area&&far.Writes+near.Writes+station.Writes+tame.Writes==writes);
        MovePeer(player,40);Tick(13);Check("leaving retains prior owner during field delay",near.GetOwner()==1);
        Tick(15);Check("default server takes vacated field",near.GetOwner()==99);
        Check("grid overrides default for newly nearby interactive",far.GetOwner()==1);
        Check("tame remains subject to existing grid rules",tame.GetOwner()==1);
        ZNet.instance.Peers.Clear();Tick(16);Tick(18);Check("no peer coverage keeps default server for loaded interactives",far.GetOwner()==99);
        Check("explicit exit-return option applies to tames like interactives",tame.GetOwner()==99);

        foreach(bool fallback in new[]{false,true})
        {
            Fresh(Kinds.Doors|Kinds.Smelters,defaultInteractives:fallback,refillStations:true);player=Peer(1);
            station=Object(typeof(Smelter));near=Object(typeof(Door));Tick(0);Tick(2);
            Check("permanent refill ownership overrides unique-player coverage",station.GetOwner()==99);
            Check("permanent refill option does not pin unrelated door",near.GetOwner()==1);
            MovePeer(player,40);Tick(3);Tick(5);Check("permanent refill remains server after leaving",station.GetOwner()==99);
        }
        Fresh(Kinds.Doors,defaultInteractives:true,refillStations:true);Peer(1);station=Object(typeof(Smelter));Tick(0);Tick(2);
        Check("server options do not override a disabled category",station.GetOwner()==7&&station.Writes==0);

        Fresh(Kinds.Containers,defaultInteractives:true);player=Peer(1);Peer(7,64,0);
        var chestData=Object(typeof(Container),40,1);var chestView=ZNetScene.instance.FindInstance(chestData);var chest=chestView.GetComponent<Container>();int requests=0;
        chestView.OnInvoke=delegate(long target,string method,object[] args){requests++;};chestData.Use=1;
        Tick(0);Tick(2);Check("fallback server does not take open inventory",requests==0&&chestData.GetOwner()==7);
        chestData.Use=0;controller.DataChanged(chestData);Tick(3);Check("fallback public inventory uses native source request",requests==1&&chestData.GetOwner()==7);
        Import(chestData,99,false);controller.InventoryResponse(chest,7,true);Tick(4);
        Check("fallback inventory confirms native source revision",chestData.GetOwner()==99&&!controller.Inventory.Pending(chestData));
        MovePeer(player,40);Tick(5);Tick(7);Check("native-confirmed fallback chest passes to nearby player",chestData.GetOwner()==1);

        Fresh(Kinds.Smelters);Peer(1);station=Object(typeof(Smelter));Tick(0);Tick(2);ZDOMan.instance.MarkSent(1,station);
        var refill=Request(station,"RPC_AddOre",1);Tick(3);controller.Router.TryHandle(ZRoutedRpc.instance,refill,false);
        Peer(2,1,1);Tick(4);Tick(6);Check("grid refill mode protects observed active inserts",station.GetOwner()==1);
        Tick(12);Check("observed refill lease lasts ten seconds",station.GetOwner()==1);
        Tick(13);Check("refill lease expiry permits intended grid change",station.GetOwner()==99);

        var refillKinds=Kinds.Smelters|Kinds.CookingStations|Kinds.Fireplaces|Kinds.Fermenters|Kinds.ShieldGenerators|Kinds.Defenses;
        foreach(bool permanent in new[]{false,true})
        {
            Fresh(refillKinds,refillStations:permanent);Peer(1);var refillObjects=new List<ZDO>();
            foreach(Type type in new[]{typeof(Smelter),typeof(CookingStation),typeof(Fireplace),typeof(Fermenter),typeof(ShieldGenerator),typeof(Turret)})refillObjects.Add(Object(type));
            var trap=Object(typeof(Trap));Tick(0);Tick(2);
            foreach(var data in refillObjects)Check("all supported refill types honor optional pin",data.GetOwner()==(permanent?99:1));
            Check("refill option does not include non-refillable trap",trap.GetOwner()==1);
        }
        Fresh(Kinds.Vehicles,defaultInteractives:true,refillStations:true);Peer(1);Peer(7,64,0);var cartData=Object(typeof(Vagon),40,1);
        var cart=ZNetScene.instance.FindInstance(cartData).GetComponent<Vagon>();cart.Using=true;Tick(0);Tick(2);
        Check("server default retains active cart user's owner",cartData.GetOwner()==7&&cartData.Writes==0);
        cart.Using=false;Tick(3);Check("idle uncovered cart can receive optional server default",cartData.GetOwner()==99);
    }
    private static void CameraPublication()
    {
        Check("initial north has one raster",CameraHeading.Coverage(0,1)==0);
        Check("invalid camera directions rejected",CameraHeading.Coverage(0,0)==-1&&CameraHeading.Coverage(float.NaN,1)==-1);
        Fresh(Kinds.Doors);ZNet.instance.Dedicated=false;ZNet.instance.ServerPeer=new ZNetPeer{Uid=99};
        var root=new GameObject();var player=root.Add(new Player());var view=root.Add(new ZNetView());
        var data=new ZDO{m_uid=new ZDOID{ID=next++},PersistentValue=false};data.SetOwner(ZDOMan.GetSessionID());view.Data=data;ZDOMan.instance.Objects.Add(data);
        Player.m_localPlayer=player;var cameraRoot=new GameObject();var camera=cameraRoot.Add(new GameCamera());GameCamera.instance=camera;GameCamera.FreeFly=false;
        var publisher=new CameraPublisher(.1f);publisher.Sample(0);
        Check("first owned camera coverage publishes through prioritized ZDO",data.GetInt(CameraHeading.Key,-1)==0&&data.IntWrites==1&&ZDOMan.instance.Forced==1&&ZDOExtraData.SessionHashes.Contains(CameraHeading.Key));
        for(int i=1;i<=100;i++)publisher.Sample(i*.01f);
        Check("unchanged camera writes no repeated network state or send priority",data.IntWrites==1&&ZDOMan.instance.Forced==1);
        camera.transform.forward=new Vector3(1,0,0);publisher.Sample(2);
        Check("stationary camera turn changes coverage without body rotation",data.GetInt(CameraHeading.Key,-1)==2&&data.Yaw==0&&data.IntWrites==2);
        GameCamera.FreeFly=true;publisher.Sample(3);publisher.Sample(4);
        Check("freefly publishes one fallback sentinel",data.GetInt(CameraHeading.Key,-2)==-1&&data.IntWrites==3);
        GameCamera.FreeFly=false;camera.transform.forward=new Vector3(-1,0,0);publisher.Sample(5);
        Check("leaving freefly restores current camera coverage",data.GetInt(CameraHeading.Key,-1)==6&&data.IntWrites==4);
        publisher.Reset(true);Check("camera/plugin destruction clears current owned heading",data.GetInt(CameraHeading.Key,-2)==-1&&data.IntWrites==5);
        publisher.Sample(6);data.SetOwner(7);int writes=data.IntWrites;publisher.Reset(true);
        Check("cleanup does not write after ownership changed",data.IntWrites==writes);
        publisher.Sample(7);Check("non-owner cannot publish",data.IntWrites==writes);
        data.SetOwner(ZDOMan.GetSessionID());publisher.Sample(8);camera.transform.forward=new Vector3(0,1,0);publisher.Sample(9);
        Check("vertical camera invalidates stale horizontal sector",data.GetInt(CameraHeading.Key,-2)==-1);
        camera.transform.forward=new Vector3(1,0,0);publisher.Sample(10);GameCamera.instance=null;publisher.Sample(11);
        Check("missing camera restores character fallback",data.GetInt(CameraHeading.Key,-2)==-1);
        GameCamera.instance=camera;publisher.Sample(12);writes=data.IntWrites;ZNet.instance.Dedicated=true;publisher.Sample(13);
        Check("dedicated role never publishes",data.IntWrites==writes);
        ZNet.instance.Dedicated=false;ZNet.IsSinglePlayer=true;publisher.Sample(14);
        Check("singleplayer never publishes",data.IntWrites==writes);
        ZNet.IsSinglePlayer=false;publisher.Sample(15);var oldWorld=ZDOMan.instance;ZDOMan.instance=new ZDOMan();publisher.Reset(true);
        Check("cleanup never writes to a previous world",data.IntWrites==writes);
        publisher.Sample(16);Check("stale player view absent from current world cannot publish",data.IntWrites==writes);
        var replacement=new ZDO{m_uid=new ZDOID{ID=next++},PersistentValue=false};replacement.SetOwner(ZDOMan.GetSessionID());view.Data=replacement;ZDOMan.instance.Objects.Add(replacement);publisher.Sample(17);
        Check("new player ZDO republishes immediately",replacement.GetInt(CameraHeading.Key,-1)==2&&replacement.IntWrites==1&&data.IntWrites==writes);
        publisher.Reset();camera.transform.forward=new Vector3(0,0,1);publisher.Sample(18);
        int initialWrites=replacement.IntWrites,initialSends=ZDOMan.instance.Forced;
        float[] turns={7.4f,7.6f,22.5f,37.4f,37.6f};int[] coverage={0,8,8,8,1};int[] changed={0,1,1,1,2};
        for(int step=0;step<turns.Length;step++) {
            double angle=turns[step]*Math.PI/180;camera.transform.forward=new Vector3((float)Math.Sin(angle),0,(float)Math.Cos(angle));publisher.Sample(19+step);
            Check("real camera turn publishes only changed widened masks",replacement.GetInt(CameraHeading.Key,-1)==coverage[step]&&replacement.IntWrites==initialWrites+changed[step]&&ZDOMan.instance.Forced==initialSends+changed[step]);
        }
        int sendsBeforeLoss=ZDOMan.instance.Forced;publisher.Reset(true);publisher.Reset(true);
        Check("camera destruction prioritizes fallback once",replacement.GetInt(CameraHeading.Key,-2)==-1&&ZDOMan.instance.Forced==sendsBeforeLoss+1);
        writes=replacement.IntWrites;ZNet.instance=new ZNet{Dedicated=false};publisher.Sample(25);Check("disconnected client cannot publish",replacement.IntWrites==writes);
        publisher.Reset();Player.m_localPlayer=null;GameCamera.instance=null;GameCamera.FreeFly=false;
    }
    private static void CameraFastCadence()
    {
        Check("camera cadence accepts twentieth-second minimum",new OwnershipCadence(.05f,.05f).Interval==.05f);
        Check("camera cadence clamps supported bounds",new OwnershipCadence(.01f,.05f).Interval==.05f&&new OwnershipCadence(30,.05f).Interval==5f);
        Check("invalid camera cadence retains twentieth-second default",new OwnershipCadence(float.NaN,.05f).Interval==.05f&&new OwnershipCadence(float.PositiveInfinity,.05f).Interval==.05f);
        Check("server movement cadence accepts twentieth-second minimum",new OwnershipCadence(.05f).Interval==.05f);
        Fresh(Kinds.Doors);ZNet.instance.Dedicated=false;ZNet.instance.ServerPeer=new ZNetPeer{Uid=99};
        var root=new GameObject();var player=root.Add(new Player());var view=root.Add(new ZNetView());
        var data=new ZDO{m_uid=new ZDOID{ID=next++},PersistentValue=false};data.SetOwner(ZDOMan.GetSessionID());view.Data=data;ZDOMan.instance.Objects.Add(data);
        Player.m_localPlayer=player;var cameraRoot=new GameObject();var camera=cameraRoot.Add(new GameCamera());GameCamera.instance=camera;GameCamera.FreeFly=false;
        var publisher=new CameraPublisher(.05f);publisher.Sample(0);float now=0;
        for(int tick=1;tick<=20;tick++) {
            now+=.05f;camera.transform.forward=tick%2==0?new Vector3(0,0,1):new Vector3(1,0,0);
            publisher.Sample(now-.001f);Check("fast camera cannot publish before its interval",data.IntWrites==tick);
            publisher.Sample(now);Check("fast camera publishes every twentieth second",data.IntWrites==tick+1&&data.GetInt(CameraHeading.Key,-1)==(tick%2==0?0:2));
        }
        for(int sample=1;sample<=1000;sample++)publisher.Sample(now+sample*.001f);
        Check("fast stable camera adds no repeated network writes or send priority",data.IntWrites==21&&ZDOMan.instance.Forced==21);
        publisher.Reset();Player.m_localPlayer=null;GameCamera.instance=null;GameCamera.FreeFly=false;
    }
    private static void CameraDirectedController()
    {
        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var peer=Peer(1);var player=ZDOMan.instance.GetZDO(peer.m_characterID);player.SetOwner(1);player.Yaw=90;player.Set(CameraHeading.Key,0);
        var front=Object(typeof(Door),1,5);var rear=Object(typeof(Door),1,-3);Tick(0);
        Check("camera north overrides unchanged east-facing body",front.GetOwner()==1&&rear.GetOwner()==99&&player.RotationReads==0);
        int updates=controller.FieldUpdates;player.Set(CameraHeading.Key,2);Tick(.1f);
        Check("camera-only turn updates real coverage while standing",controller.FieldUpdates==updates+1&&rear.GetOwner()==1&&player.Yaw==90&&player.RotationReads==0);
        for(int i=2;i<=20;i++)Tick(i*.1f);
        Check("stable camera sector repeats no field request",controller.FieldUpdates==updates+1);
        player.Set(CameraHeading.Key,-1);player.Yaw=180;Tick(3);
        Check("camera sentinel follows current body heading",controller.FieldUpdates==updates+2&&player.RotationReads>0);
        player.Set(CameraHeading.Key,16);Check("predictive triple advisory is accepted",CameraHeading.Read(player,1)==16);
        foreach(int invalid in new[]{-1,56,int.MaxValue}){player.Set(CameraHeading.Key,invalid);Check("invalid advisory falls back",CameraHeading.Read(player,1)==4);}
        player.Set(CameraHeading.Key,0);player.SetOwner(2);Check("foreign owner advisory is ignored",CameraHeading.Read(player,1)==4);

        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true,checkIntervalSeconds:.05f);
        peer=Peer(1);player=ZDOMan.instance.GetZDO(peer.m_characterID);player.SetOwner(1);player.Set(CameraHeading.Key,0);
        var northOnly=Object(typeof(Door),-3,1);var diagonalOnly=Object(typeof(Door),5,-3);
        controller.Poll(0);Check("one initial raster with instant inner fields",northOnly.GetOwner()==1&&diagonalOnly.GetOwner()==99);
        player.Set(CameraHeading.Key,8);Time.time=Time.unscaledTime=.001f;controller.DataChanged(player);controller.Poll(.001f);
        Check("received changed camera bypasses remaining server interval for zero-delay fields",northOnly.GetOwner()==1&&diagonalOnly.GetOwner()==1&&failures==0);
        updates=controller.FieldUpdates;Time.time=Time.unscaledTime=.1f;controller.Poll(.1f);
        Check("unchanged overlap starts no duplicate field work",controller.FieldUpdates==updates);
        player.Set(CameraHeading.Key,1);Time.time=Time.unscaledTime=.101f;controller.DataChanged(player);controller.DataChanged(player);controller.Poll(.101f);Cell northCell;
        Check("valid physical field",Cell.At(-3,1,4,out northCell));
        Check("old raster leaves coverage while sticky owner stays",Index().Grid.Candidate(northCell)==0&&northOnly.GetOwner()==1&&diagonalOnly.GetOwner()==1);

        Fresh(Kinds.Doors,nativeSolo:true,directional:true);peer=Peer(1);player=ZDOMan.instance.GetZDO(peer.m_characterID);player.SetOwner(1);player.Set(CameraHeading.Key,2);Tick(0);
        Check("solo vanilla bypass still skips heading work",controller.FieldUpdates==0&&player.RotationReads==0);
    }
    private static void CameraOverlapController()
    {
        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true,overlapCooldown:1);
        var a=Peer(1);var b=Peer(2,0,8);
        var first=ZDOMan.instance.GetZDO(a.m_characterID);var second=ZDOMan.instance.GetZDO(b.m_characterID);
        first.SetOwner(1);second.SetOwner(2);first.Set(CameraHeading.Key,0);second.Set(CameraHeading.Key,4);
        var door=Object(typeof(Door),1,5);Tick(0);
        Check("controller overlap settles on server",door.GetOwner()==99);
        int writes=door.Writes;
        second.Set(CameraHeading.Key,0);Tick(.1f);
        Check("real camera advisory cannot change a field during acquisition cooldown",door.GetOwner()==99&&door.Writes==writes);
        second.Set(CameraHeading.Key,4);Tick(.2f);second.Set(CameraHeading.Key,0);Tick(.3f);Tick(10);
        Check("cooldown expiry never replays ignored door ownership request",door.GetOwner()==99&&door.Writes==writes);
        first.Set(CameraHeading.Key,8);Tick(11);
        Check("next real camera update detects expired same-tier field",door.GetOwner()==1&&door.Writes==writes+1);
        controller.ConfigureOverlapCooldown(0);
        second.Set(CameraHeading.Key,4);Tick(12);second.Set(CameraHeading.Key,0);Tick(12.1f);
        Check("live cooldown disable reaches controller grid",door.GetOwner()==1&&door.Writes==writes+3);
    }
    private static void SummonedCompanions()
    {
        Check("missing Tameable is not a summon marker",!ObjectCatalog.IsSummoned(null));
        Check("starting tamed alone does not exclude ordinary hens",!ObjectCatalog.IsSummoned(new Tameable{m_startsTamed=true}));
        foreach(bool defaults in new[]{false,true})foreach(int marker in new[]{1,2,3})foreach(bool following in new[]{false,true})
        {
            Fresh(Kinds.TamedAnimals,defaultInteractives:defaults,statics:true);Peer(1);var second=Peer(2);var source=Peer(7,128);
            var summon=Object(typeof(Character));var view=ZNetScene.instance.FindInstance(summon);var animal=view.GetComponent<Tameable>();
            animal.m_startsTamed=true;animal.m_commandable=true;
            if((marker&1)!=0)animal.m_unsummonDistance=150;
            if((marker&2)!=0)animal.m_unsummonOnOwnerLogoutSeconds=120;
            summon.Tamed=true;summon.Follow=following?"caster":"";controller.DataChanged(summon);
            // Per-instance settings must not poison the shared prefab cache.
            var ordinary=Object(typeof(Character));ordinary.Hash=summon.Hash;
            ZNetScene.instance.FindInstance(ordinary).GetComponent<Tameable>().m_startsTamed=true;
            ordinary.Tamed=true;controller.DataChanged(ordinary);Tick(0);Tick(2);
            Check("summon keeps existing owner even in server overlap",summon.GetOwner()==7&&summon.Writes==0&&source.m_rpc.Calls==0);
            Check("summon has no Authority node or native ownership pin",controller.RequestNode(summon.m_uid,"Command".GetStableHashCode())==null&&controller.Persistent(summon));
            Check("ordinary animal sharing prefab hash still follows grid",ordinary.GetOwner()==99&&controller.RequestNode(ordinary.m_uid,"Command".GetStableHashCode())!=null);
            MovePeer(second,128);Tick(3);Tick(5);
            Check("unique player's field changes ordinary animal only",ordinary.GetOwner()==1&&summon.GetOwner()==7&&summon.Writes==0);
            int attempts=controller.ScheduledAttempts,scans=GameObject.Scans,lookups=GameObject.Lookups;
            for(int i=6;i<26;i++)Tick(i);
            Check("excluded summons create no retry jobs or repeated component scans",controller.ScheduledAttempts==attempts&&GameObject.Scans==scans&&GameObject.Lookups==lookups);
            controller.Unregister(view);controller.Register(summon,view);Tick(26);
            Check("reload/registration does not reintroduce excluded summon",controller.RequestNode(summon.m_uid,"Command".GetStableHashCode())==null&&summon.Writes==0);
        }
        // A mod can enable native summon rules after registration. The existing
        // event/write paths must release that instance rather than retry forever.
        Fresh(Kinds.TamedAnimals,defaultInteractives:true);Peer(1);
        var changed=Object(typeof(Character));changed.Tamed=true;controller.DataChanged(changed);Tick(0);
        var changedView=ZNetScene.instance.FindInstance(changed);changedView.GetComponent<Tameable>().m_unsummonDistance=150;
        var node=controller.RequestNode(changed.m_uid,"Command".GetStableHashCode());
        int previousWrites=changed.Writes;
        Check("late summon marker releases native ownership pin",controller.Persistent(changed));
        Check("late summon marker is removed without a pending retry",node!=null&&node.Apply(99)==ApplyResult.Dropped&&controller.RequestNode(changed.m_uid,"Command".GetStableHashCode())==null&&changed.Writes==previousWrites);
        Fresh(Kinds.TamedAnimals,defaultInteractives:true);Peer(1);
        changed=Object(typeof(Character));changed.Tamed=true;controller.DataChanged(changed);
        ZNetScene.instance.FindInstance(changed).GetComponent<Tameable>().m_unsummonOnOwnerLogoutSeconds=120;controller.DataChanged(changed);Tick(0);Tick(2);
        Check("late marker event removes animal before first ownership write",changed.Writes==0&&controller.RequestNode(changed.m_uid,"Command".GetStableHashCode())==null&&controller.Persistent(changed));
    }
    private static void FriendlyMagicCreatures()
    {
        Check("missing creature is not classified as friendly",!FriendlyCreatures.IsFriendly(null));
        Check("actual player is outside summon predicate",!FriendlyCreatures.IsFriendly(new Player{m_faction=Character.Faction.Players}));
        Check("friendly root needs no Tameable marker",FriendlyCreatures.IsFriendly(new Character{m_faction=Character.Faction.Players,Tamed=false}));
        Check("destroyed friendly actor is not inspected",!FriendlyCreatures.IsFriendly(new Character{m_faction=Character.Faction.Players,Alive=false}));
        foreach(Character.Faction faction in new[]{Character.Faction.ForestMonsters,Character.Faction.Demon,Character.Faction.MountainMonsters,Character.Faction.PlainsMonsters,Character.Faction.DeepNorth,Character.Faction.PlayerSpawned})
            Check("taming does not make ordinary/hostile faction a magic companion",!FriendlyCreatures.IsFriendly(new Character{m_faction=faction,Tamed=true}));
        foreach(bool defaults in new[]{false,true})foreach(bool tamed in new[]{false,true})foreach(bool marker in new[]{false,true})
        {
            Fresh(Kinds.TamedAnimals,defaultInteractives:defaults,statics:true);Peer(1);Peer(2);Peer(7,128);
            var summoned=Object(typeof(Character));var view=ZNetScene.instance.FindInstance(summoned);var actor=view.GetComponent<Character>();
            actor.m_faction=Character.Faction.Players;actor.Tamed=tamed;summoned.Tamed=tamed;
            if(!marker)view.gameObject.Components.Remove(view.GetComponent<Tameable>());
            else view.GetComponent<Tameable>().m_unsummonDistance=150;
            controller.Unregister(view);controller.Register(summoned,view);controller.DataChanged(summoned);Tick(0);Tick(2);
            Check("friendly summon never enters grid, with or without taming/despawn markers",summoned.GetOwner()==7&&summoned.Writes==0&&controller.Persistent(summoned)&&controller.RequestNode(summoned.m_uid,"Command".GetStableHashCode())==null);
            var ordinary=Object(typeof(Character));ordinary.Hash=summoned.Hash;ordinary.Tamed=true;controller.DataChanged(ordinary);Tick(3);Tick(5);
            Check("friendly instance does not poison same-prefab ordinary animal",ordinary.GetOwner()==99&&controller.RequestNode(ordinary.m_uid,"Command".GetStableHashCode())!=null);
            int scans=GameObject.Scans,lookups=GameObject.Lookups,attempts=controller.ScheduledAttempts;
            for(int i=6;i<20;i++)Tick(i);
            Check("stable exclusion adds no object scans or retry jobs",GameObject.Scans==scans&&GameObject.Lookups==lookups&&controller.ScheduledAttempts==attempts);
        }
        Fresh(Kinds.TamedAnimals,defaultInteractives:true);Peer(1);
        var changed=Object(typeof(Character));changed.Tamed=true;controller.DataChanged(changed);Tick(0);
        var changedView=ZNetScene.instance.FindInstance(changed);var node=controller.RequestNode(changed.m_uid,"Command".GetStableHashCode());
        changedView.GetComponent<Character>().m_faction=Character.Faction.Players;int writes=changed.Writes;
        Check("late friendly faction drops existing animal job using cached actor",node!=null&&node.Apply(99)==ApplyResult.Dropped&&changed.Writes==writes&&controller.Persistent(changed));
    }
    private static void TamedDefaultProtection()
    {
        // Default ownership changes policy only; eligibility stays strict.
        foreach(int protection in new[]{0,1,2,3,4,5,6})
        {
            Fresh(Kinds.TamedAnimals,defaultInteractives:true);Peer(1);var source=Peer(7,64);
            FiresGhettoNetworkMod.FgnVersionGate.Match=true;
            var data=Object(typeof(Character),40,1);data.Tamed=true;controller.DataChanged(data);
            var view=ZNetScene.instance.FindInstance(data);var actor=view.GetComponent<Character>();var tameable=view.GetComponent<Tameable>();
            if(protection==0)data.User=7;
            if(protection==1)actor.Attached=true;
            if(protection==2)actor.Riding=true;
            if(protection==3)tameable.m_saddle=view.gameObject.Add(new Sadle{User=true});
            if(protection==4){tameable.m_commandable=true;data.Follow="owner";}
            if(protection==5)actor.Dead=true;
            if(protection==6)actor.isActiveAndEnabled=false;
            Tick(0);Tick(2);Check("server default never steals an ineligible tame: "+protection,data.GetOwner()==7&&data.Writes==0&&source.m_rpc.Calls==0);
            if(protection==5)continue;
            data.User=0;actor.Attached=actor.Riding=false;actor.isActiveAndEnabled=true;data.Follow="";
            if(tameable.m_saddle)tameable.m_saddle.User=false;
            Tick(3);Check("idle tame receives default through existing checked assignment",data.GetOwner()==99&&source.m_rpc.Calls==0);
        }
        Fresh(Kinds.TamedAnimals,defaultInteractives:true);Peer(1);
        var wild=Object(typeof(Character),40,1);Tick(0);Tick(2);
        Check("server default still excludes actual wild ZDO",wild.GetOwner()==7&&wild.Writes==0);
        wild.Tamed=true;controller.DataChanged(wild);Tick(3);
        Check("newly tamed animal is indexed by the real ZDO flag",wild.GetOwner()==99);
        wild.Tamed=false;controller.DataChanged(wild);wild.SetOwner(7);Tick(4);
        Check("losing tame flag removes managed ownership protection",controller.Persistent(wild)&&wild.GetOwner()==7);
        Fresh(Kinds.Doors,defaultInteractives:true);Peer(1);var disabled=Object(typeof(Character),40,1);disabled.Tamed=true;controller.DataChanged(disabled);Tick(0);Tick(2);
        Check("disabled tame category stays native despite server default",disabled.GetOwner()==7&&disabled.Writes==0);

        // Both policies use the same fallback and rounded grid, including all
        // selected cell sizes and overlap shapes. Never bounce through server
        // while a unique player's field is still becoming stable.
        foreach(int size in new[]{4,8,16})foreach(GridCoveragePattern shape in new[]{GridCoveragePattern.Square3x3,GridCoveragePattern.Rounded5x5,GridCoveragePattern.Rounded7x7})
        {
            if(size==4&&shape==GridCoveragePattern.Square3x3)continue;
            Fresh(Kinds.Doors|Kinds.TamedAnimals,defaultInteractives:true,cellSize:size,pattern:shape);Peer(1);
            var door=Object(typeof(Door),1,1,1);var animal=Object(typeof(Character),1,1,1);animal.Tamed=true;controller.DataChanged(animal);
            Tick(0);Check("pending tame coverage does not transfer to fallback",door.GetOwner()==1&&animal.GetOwner()==1&&animal.Writes==0);
            Tick(2);Peer(2);Tick(3);Tick(5);
            Check("tame and door share configured overlap ownership",door.GetOwner()==99&&animal.GetOwner()==99);
            ZNet.instance.Peers.RemoveAt(1);Tick(6);Tick(8);
            Check("remaining player owns tame and door equally",door.GetOwner()==1&&animal.GetOwner()==1);
            MovePeer(ZNet.instance.Peers[0],512);Tick(9);Tick(11);
            Check("whole-field exit keeps both owners without fallback",door.GetOwner()==1&&animal.GetOwner()==1);
        }
    }
    private static void LoginGates()
    {
        Fresh(Kinds.Doors|Kinds.TamedAnimals,defaultInteractives:true);Peer(1);
        var near=Object(typeof(Door));var far=Object(typeof(Door),40,1);var tame=Object(typeof(Character),40,1);tame.Tamed=true;controller.DataChanged(tame);
        FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive=false;
        ZNetScene.instance.Ready=delegate{return false;};
        controller.Poll(0);Check("login retains native owners until FGN simulation is active",!controller.IsActive&&near.Writes==0&&far.Writes==0&&tame.Writes==0);
        FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive=true;
        Time.time=Time.unscaledTime=.5f;controller.Poll(.5f);Check("FGN activation waits only the normal configured poll",!controller.IsActive);
        Time.time=Time.unscaledTime=1;controller.Poll(1);
        Check("unready local zone blocks default ownership for door and tame",controller.IsActive&&far.GetOwner()==7&&tame.GetOwner()==7);
        ZNetScene.instance.Ready=delegate{return true;};
        Time.time=Time.unscaledTime=1.5f;controller.Poll(1.5f);Check("area completion does not bypass poll interval",far.GetOwner()==7&&tame.GetOwner()==7);
        Time.time=Time.unscaledTime=2;controller.Poll(2);
        Check("uncovered default has no independent two-second delay",far.GetOwner()==99&&tame.GetOwner()==99);
        Check("unique player field remains native until stable decision",near.GetOwner()==7);
        Time.time=Time.unscaledTime=3;controller.Poll(3);Check("stable unique player field overrides server default",near.GetOwner()==1);
        Check("startup controller remained healthy",failures==0);

        Fresh(Kinds.Doors,defaultInteractives:true);Peer(1);Peer(2);near=Object(typeof(Door));Tick(0);
        Time.time=Time.unscaledTime=1;near.SetOwner(1);controller.OwnerChanged(near);Tick(2);
        Check("ordinary native owner event retains its stability barrier",near.GetOwner()==1);
        Tick(3);Check("owner event barrier expires for settled overlap",near.GetOwner()==99);
    }
    private static void GridSettings()
    {
        foreach(float size in new[]{4f,6.4f,8f,16f})foreach(GridCoveragePattern shape in new[]{GridCoveragePattern.Square3x3,GridCoveragePattern.Rounded5x5,GridCoveragePattern.Rounded7x7})
        {
            if(size==4&&shape==GridCoveragePattern.Square3x3)continue;
            Fresh(Kinds.Doors,cellSize:size,pattern:shape);Peer(1);var pieces=new List<ZDO>();int radius=shape==GridCoveragePattern.Rounded7x7?3:shape==GridCoveragePattern.Rounded5x5?2:1;
            for(int x=-radius;x<=radius;x++)for(int z=-radius;z<=radius;z++)pieces.Add(Object(typeof(Door),x*size+1,z*size+1));
            Tick(0);Tick(2);
            foreach(var data in pieces)
            {
                int x=(int)Math.Floor(data.Position.x/size),z=(int)Math.Floor(data.Position.z/size);
                bool inside=Covered(x,z,radius);
                Check("configured physical footprint transfers only selected fields",data.GetOwner()==(inside?1:7));
            }
            Peer(2,2*radius*size,0);Tick(3);Tick(5);
            foreach(var data in pieces)
            {
                int x=(int)Math.Floor(data.Position.x/size),z=(int)Math.Floor(data.Position.z/size);
                bool a=Covered(x,z,radius);
                int bx=x-2*radius;
                bool b=Covered(bx,z,radius);
                Check("configured physical overlap retains exclusive-player fields",data.GetOwner()==(a&&b?99:a?1:b?2:7));
            }
            int writes=0;foreach(var data in pieces)writes+=data.Writes;int scans=GameObject.Scans,queries=ZNetScene.instance.AreaQueries;
            for(int i=6;i<16;i++)Tick(i);int after=0;foreach(var data in pieces)after+=data.Writes;
            Check("all geometry combinations avoid stable object jobs",writes==after&&scans==GameObject.Scans&&queries==ZNetScene.instance.AreaQueries);

            Fresh(Kinds.TamedAnimals,cellSize:size,pattern:shape);Peer(1);Peer(2,2*radius*size,0);var animal=Object(typeof(Character));animal.Tamed=true;controller.DataChanged(animal);Tick(0);Tick(2);
            var node=controller.RequestNode(animal.m_uid,"Command".GetStableHashCode());Cell before=node.Cell;
            animal.Position=new Vector3(size-.01f,0,1);controller.Position(animal);Check("movement inside selected cell stays indexed there",node.Cell.Equals(before));
            animal.Position=new Vector3(size+.01f,0,1);controller.Position(animal);Check("crossing selected cell updates bucket",node.Cell.X==before.X+1);
            animal.Position=new Vector3(radius*size+.01f,0,1);controller.Position(animal);Tick(3.99f);Check("new animal cell retains residence guard",animal.GetOwner()==1);
            Tick(4);Check("animal ownership follows configured overlap",animal.GetOwner()==99);
            Check("active-area certificate requires full selected cell",!controller.AvailableOwner(1,new Vector3(96,0,0)));
        }
        foreach(GridCoveragePattern shape in new[]{GridCoveragePattern.Rounded5x5,GridCoveragePattern.Rounded7x7})
        {
            int radius=shape==GridCoveragePattern.Rounded7x7?3:2;
            Fresh(Kinds.Doors,defaultInteractives:true,cellSize:16,pattern:shape);Peer(1);
            var edge=Object(typeof(Door),radius*16+1,1);var corner=Object(typeof(Door),radius*16+1,radius*16+1);Tick(0);Tick(2);
            Check("rounded covered edge overrides server default",edge.GetOwner()==1);
            Check("rounded excluded corner gets optional server default",corner.GetOwner()==99);
        }
    }
    private static bool Covered(int x,int z,int radius)
    {
        if(Math.Abs(x)>radius||Math.Abs(z)>radius)return false;
        if(radius==1)return true;
        int[] widths=radius==3?new[]{3,3,2,1}:new[]{2,2,1};
        return Math.Abs(x)<=widths[Math.Abs(z)];
    }
    private static void ExitRetention()
    {
        foreach(int size in new[]{4,8,16})foreach(GridCoveragePattern shape in new[]{GridCoveragePattern.Square3x3,GridCoveragePattern.Rounded5x5,GridCoveragePattern.Rounded7x7})
        {
            if(size==4&&shape==GridCoveragePattern.Square3x3)continue;
            int radius=shape==GridCoveragePattern.Rounded7x7?3:shape==GridCoveragePattern.Rounded5x5?2:1;
            Fresh(Kinds.Doors|Kinds.TamedAnimals,defaultInteractives:true,cellSize:size,pattern:shape);var player=Peer(1);
            var near=Object(typeof(Door));var far=Object(typeof(Door),(radius+2)*size+1,1);var animal=Object(typeof(Character));animal.Tamed=true;controller.DataChanged(animal);
            Tick(0);Tick(2);Check("server default still initializes never-covered loaded objects",far.GetOwner()==99);
            Check("player initially owns interactive and tame",near.GetOwner()==1&&animal.GetOwner()==1);int writes=near.Writes;
            MovePeer(player,(radius+2)*size);Tick(3);Tick(5);
            Check("whole-field exit preserves player despite enabled server default",near.GetOwner()==1&&near.Writes==writes&&animal.GetOwner()==1);
            Check("newly covered objects still override server default",far.GetOwner()==1);
            controller.DataChanged(near);Tick(7);
            Check("later data event cannot recreate vacated-field fallback",near.GetOwner()==1&&near.Writes==writes);
            var node=controller.RequestNode(near.m_uid,"UseDoor".GetStableHashCode());controller.RequestFinished(node);Tick(8);
            Check("later router maintenance cannot recreate fallback",near.GetOwner()==1&&near.Writes==writes);
            Peer(2);Tick(9);Tick(10.99f);Check("new covering player retains full transition delay",near.GetOwner()==1);
            Tick(11);Check("new covering player can acquire sticky objects and tames",near.GetOwner()==2&&animal.GetOwner()==2);
        }

        // Leaving one covering player is not a whole-field exit.
        Fresh(Kinds.Doors,defaultInteractives:true);Peer(1);var second=Peer(2);var door=Object(typeof(Door));Tick(0);Tick(2);
        Check("overlap initially belongs to server",door.GetOwner()==99);ZNet.instance.Peers.Remove(second);Tick(3);Tick(5);
        Check("remaining covering player still receives normal field ownership",door.GetOwner()==1);

        // Permanent server-refill ownership has its explicit priority.
        Fresh(Kinds.Smelters,defaultInteractives:true,refillStations:true);var player1=Peer(1);var station=Object(typeof(Smelter));Tick(0);Tick(2);
        MovePeer(player1,40);Tick(3);Tick(5);controller.DataChanged(station);Tick(7);
        Check("permanent refill ownership survives empty-field retention",station.GetOwner()==99);

        // Native inventory and already-sent handoffs are not cancelled on exit.
        Fresh(Kinds.Containers,defaultInteractives:true);player1=Peer(1);Peer(7,64);var inventoryData=Object(typeof(Container));var inventoryView=ZNetScene.instance.FindInstance(inventoryData);var inventory=inventoryView.GetComponent<Container>();int requests=0;
        inventoryView.OnInvoke=delegate{requests++;};Tick(0);Tick(2);Check("initial native inventory transfer sent once",requests==1);
        MovePeer(player1,40);Tick(3);Tick(5);Import(inventoryData,99,false);Tick(6);
        Check("late exact source commit remains guarded without new return request",inventoryData.GetOwner()==99&&requests==1&&controller.Inventory.Pending(inventoryData));
        controller.InventoryResponse(inventory,7,true);Tick(7);Check("late native reply completes without departure transfer",inventoryData.GetOwner()==99&&requests==1&&!controller.Inventory.Pending(inventoryData));
        Peer(2);inventoryData.Use=1;Tick(8);Tick(10);Check("new entrant cannot take open inventory",inventoryData.GetOwner()==99);
        inventoryData.Use=0;controller.DataChanged(inventoryData);Tick(12);Check("closed inventory still reaches new covering player",inventoryData.GetOwner()==2);

        Fresh(Kinds.Doors,defaultInteractives:true);player1=Peer(1);var source=Peer(7,64);FiresGhettoNetworkMod.FgnVersionGate.Match=true;door=Object(typeof(Door));Tick(0);Tick(2);
        controller.Cooperative.Result(source.m_rpc,door.m_uid,false,"held");MovePeer(player1,40);Tick(3);Tick(5);Tick(20);
        Check("vacated field sends no FGN retry after veto",source.m_rpc.Calls==1&&door.GetOwner()==7);
    }
    private static void NativeExitRelease()
    {
        var patcher=new Harmony("botanic.authority.test.exit-release");
        patcher.Patch(AccessTools.Method(typeof(ZDOMan),"ReleaseNearbyZDOS"),transpiler:new HarmonyMethod(AccessTools.Method(typeof(OwnershipPatch),"Transpiler")));
        try
        {
            Fresh(Kinds.Doors,defaultInteractives:true);var player=Peer(1);var door=Object(typeof(Door));Tick(0);Tick(2);
            MovePeer(player,40);Tick(3);Tick(5);Check("nearby native region still protects retained managed owner",!controller.Persistent(door));
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);Check("native release does not undo an ordinary cell exit",door.GetOwner()==1);
            MovePeer(player,512);Tick(6);Check("unavailable old owner passes through native persistence",controller.Persistent(door));
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);Check("native out-of-area branch can clear owner",door.GetOwner()==0);
            controller.OwnerChanged(door);Tick(8);Check("later owner-zero event is not reclaimed by server-default fallback",door.GetOwner()==0);
            var visitor=Peer(2);Tick(9);Tick(11);Check("new entrant still owns natively released object",door.GetOwner()==2);
            ZNet.instance.Peers.Remove(visitor);Tick(12);Tick(14);Check("disconnected owner is not pinned by exit retention",controller.Persistent(door));
            var other=Peer(3,64);Tick(15);ZDOMan.instance.ReleaseNearbyZDOS(other.Position,3);
            Check("native orphan reassignment chooses server default outside active grid",door.GetOwner()==99);
        }
        finally{patcher.UnpatchSelf();}
    }
    private static void NativeDefaultAssignments()
    {
        var patcher = new Harmony("botanic.authority.test.native-default");
        patcher.Patch(AccessTools.Method(typeof(ZDOMan),"ReleaseNearbyZDOS"),transpiler:new HarmonyMethod(AccessTools.Method(typeof(OwnershipPatch),"Transpiler")));
        try
        {
            Cell initialCell;
            Check("native default fixture maps its actual object cell",Cell.At(1,1,out initialCell));
            Fresh(Kinds.Doors,defaultInteractives:true);var player=Peer(1);var door=Object(typeof(Door),40,1,0);
            ZNetScene.instance.Ready=delegate{return false;};Tick(0);int queries=ZNetScene.instance.AreaQueries;
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);
            Check("native first assignment uses server before area readiness",door.GetOwner()==99&&door.Writes==1);
            Check("native selector does not perform a loading scan or takeover RPC",ZNetScene.instance.AreaQueries==queries&&player.m_rpc.Calls==0);
            controller.OwnerChanged(door);Tick(1);Tick(2);int writes=door.Writes,attempts=controller.ScheduledAttempts;
            for(int i=3;i<=65;i++){Tick(i);ZDOMan.instance.ReleaseNearbyZDOS(new Vector3(512,0,512),99);ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);}
            Check("server reference point does not cause repeated release/reclaim",door.GetOwner()==99&&door.Writes==writes);
            Check("settled initial server ownership produces no further object jobs",controller.ScheduledAttempts==attempts);
            ZNetScene.instance.Ready=delegate{return true;};Tick(66);
            Check("area readiness does not cause another ownership change",door.Writes==writes&&door.GetOwner()==99);
            controller.Unregister(ZNetScene.instance.FindInstance(door));ZNet.instance.Peers.Clear();Tick(67);
            ZDOMan.instance.ReleaseNearbyZDOS(new Vector3(512,0,512),99);
            Check("native release survives region unload and no players",door.GetOwner()==0);

            Fresh(Kinds.Doors,defaultInteractives:false);player=Peer(1);door=Object(typeof(Door),40,1,0);Tick(0);
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);Check("default false preserves native player assignment",door.GetOwner()==1);

            Fresh(Kinds.Doors,defaultInteractives:true);player=Peer(1);var old=Peer(7,64);door=Object(typeof(Door),40,1,7);
            ZNetScene.instance.Ready=delegate{return false;};Tick(0);ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);
            Check("active native owner is not stolen before area readiness",door.GetOwner()==7&&door.Writes==0&&old.m_rpc.Calls==0);
            MovePeer(old,512);Tick(1);ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);
            Check("inactive native owner may be reassigned directly to server",door.GetOwner()==99&&door.Writes==1);

            Fresh(Kinds.Doors,defaultInteractives:true);player=Peer(1);var other=Peer(2,64);door=Object(typeof(Door),1,1,0);
            ZNetScene.instance.Ready=delegate{return false;};Tick(0);
            ZDOMan.instance.ReleaseNearbyZDOS(other.Position,2);
            Check("unsettled grid initializes server default without settling its timer",door.GetOwner()==99&&Index().Grid.Owner(initialCell)==0);
            var unowned=Object(typeof(Door),2,1,0);Tick(2);ZDOMan.instance.ReleaseNearbyZDOS(other.Position,2);
            Check("settled active player field overrides native peer pass",unowned.GetOwner()==1);
            var staticPiece=Object(null,3,1,0);ZDOMan.instance.ReleaseNearbyZDOS(other.Position,2);
            Check("noninteractive structures are not mapped to active grid owner",staticPiece.GetOwner()==2);

            Fresh(Kinds.Doors,defaultInteractives:true);player=Peer(1);Peer(2);Tick(0);Tick(2);
            door=Object(typeof(Door),1,1,0);ZNetScene.instance.Ready=delegate{return false;};
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);Check("settled overlap retains server target",door.GetOwner()==99);

            Fresh(Kinds.Doors|Kinds.Containers|Kinds.TamedAnimals|Kinds.Vehicles,defaultInteractives:true);player=Peer(1);
            var chest=Object(typeof(Container),40,1,0);var privateChest=Object(typeof(Container),41,1,0);
            ZNetScene.instance.FindInstance(privateChest).GetComponent<Container>().m_privacy=Container.PrivacySetting.Private;
            var tame=Object(typeof(Character),42,1,0);tame.Tamed=true;controller.DataChanged(tame);
            var vehicle=Object(typeof(Vagon),43,1,0);var physics=Object(typeof(Door),44,1,0);ZNetScene.instance.FindInstance(physics).gameObject.Add(new Rigidbody());
            ZNetScene.instance.Ready=delegate{return false;};
            Tick(0);ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);
            Check("public unowned inventory can get its initial server owner",chest.GetOwner()==99);
            Check("private inventory retains native initial owner",privateChest.GetOwner()==1);
            Check("tames vehicles and rigid bodies retain existing loading path",tame.GetOwner()==1&&vehicle.GetOwner()==1&&physics.GetOwner()==1);

            // A slow scene must never turn the first pending field into a
            // player->server takeover. Initial ownership is not a field grant.
            foreach (bool shared in new[] { false, true })
            {
                Fresh(Kinds.Doors|Kinds.Containers|Kinds.Workstations,defaultInteractives:true);
                player=Peer(1);if(shared)Peer(2);
                var initial=new[]{Object(typeof(Door),1,1,0),Object(typeof(Container),2,1,0),Object(typeof(CraftingStation),3,1,0)};
                ZNetScene.instance.Ready=delegate{return false;};Tick(0);
                int areaQueries=ZNetScene.instance.AreaQueries;
                ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);
                foreach(var data in initial)Check("pending login starts directly at server: "+shared,data.GetOwner()==99&&data.Writes==1);
                Check("pending login neither settles timer nor requests a source handoff",Index().Grid.Owner(initialCell)==0&&player.m_rpc.Calls==0&&ZNetScene.instance.AreaQueries==areaQueries);
                Tick(1);
                foreach(var data in initial)Check("initial default does not advance field timer",data.GetOwner()==99&&data.Writes==1);
                Tick(2);
                Check("field timer still settles at its configured deadline",Index().Grid.Owner(initialCell)==(shared?99:1));
                for(int i=3;i<=65;i++){Tick(i);ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);}
                foreach(var data in initial)Check("slow scene never creates a temporary native player owner",data.GetOwner()==99&&data.Writes==1);
                ZNetScene.instance.Ready=delegate{return true;};Tick(66);
                foreach(var data in initial)Check("loaded field retains intended settled owner",data.GetOwner()==(shared?99:1)&&data.Writes==(shared?1:2));
            }

            // Native assignment can precede the server instance entirely.
            // The registered prefab, not a populated live-node table, decides.
            Fresh(Kinds.Smelters|Kinds.CookingStations|Kinds.Fireplaces|Kinds.Fermenters|Kinds.ShieldGenerators|Kinds.Defenses|Kinds.Incinerators,
                defaultInteractives:true,refillStations:true);
            player=Peer(1);Tick(0);Tick(2);
            var earlyStations=new List<ZDO>();
            foreach(Type type in new[]{typeof(Smelter),typeof(CookingStation),typeof(Fireplace),typeof(Fermenter),typeof(ShieldGenerator),typeof(Turret)})
            {
                var data=Object(type,1,1,0);var instance=ZNetScene.instance.FindInstance(data);
                controller.Unregister(instance);ZNetScene.instance.Views.Remove(data);earlyStations.Add(data);
            }
            var nativeIncinerator=Object(typeof(Incinerator),2,1,0);var nativeView=ZNetScene.instance.FindInstance(nativeIncinerator);
            controller.Unregister(nativeView);ZNetScene.instance.Views.Remove(nativeIncinerator);
            var nativeTrap=Object(typeof(Trap),3,1,0);Tick(3);
            ZNetScene.instance.Ready=delegate{return false;};int prefabAreaQueries=ZNetScene.instance.AreaQueries;
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);
            foreach(var data in earlyStations)Check("permanent station starts at server even before its instance exists",data.GetOwner()==99&&data.Writes==1);
            Check("non-refillable trap retains settled player grid rule",nativeTrap.GetOwner()==1);
            Check("excluded incinerator retains native initial ownership before registration",nativeIncinerator.GetOwner()==1);
            Check("prefab-only assignment adds no loading scan or takeover RPC",ZNetScene.instance.AreaQueries==prefabAreaQueries&&player.m_rpc.Calls==0);
        }
        finally {patcher.UnpatchSelf();}
    }
    private static void SeparateMobDefaults()
    {
        Fresh(Kinds.Doors|Kinds.TamedAnimals,defaultMobs:true,nativeSolo:true);Peer(1);
        var near=Object(typeof(Character),1,1,7);near.Tamed=true;controller.DataChanged(near);
        var far=Object(typeof(Character),40,1,7);far.Tamed=true;controller.DataChanged(far);
        var door=Object(typeof(Door),1,1,7);
        ZNetScene.instance.Ready=delegate{return false;};Tick(0);Tick(2);
        Check("mob defaults preserve loading guard",near.GetOwner()==7&&far.GetOwner()==7);
        ZNetScene.instance.Ready=delegate{return true;};Tick(3);
        Check("solo tamed grid works with interactive default off",near.GetOwner()==1);
        Check("tamed server default is independent of interactive default",far.GetOwner()==99);
        Check("solo interactions retain vanilla with only mob default enabled",door.GetOwner()==7);
        int writes=far.Writes;Tick(4);Tick(5);Check("settled default tame is not repeatedly rewritten",far.Writes==writes);

        Fresh(Kinds.TamedAnimals,defaultMobs:true,nativeSolo:true);Peer(1);
        var used=Object(typeof(Character),40,1,7);used.Tamed=true;used.User=7;controller.DataChanged(used);Tick(0);Tick(2);
        Check("mob default does not take actively used animal",used.GetOwner()==7);
        var wild=Object(typeof(Character),41,1,7);Tick(3);
        Check("hostile actors stay outside tame grid",wild.GetOwner()==7);

        Fresh(Kinds.Doors,defaultMobs:true,nativeSolo:true);Peer(1);door=Object(typeof(Door),1,1,7);Tick(0);Tick(2);
        Check("disabled tamed category keeps solo shortcut and vanilla interactions",controller.FieldUpdates==0&&door.GetOwner()==7);
    }
    private static void InnerOnlyDepartureController()
    {
        Fresh(Kinds.Doors|Kinds.TamedAnimals,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true,nativeSolo:true);
        var first=Peer(1,1,1);var second=Peer(2,1,1);
        var red=Object(typeof(Door),1,1);var orange=Object(typeof(Door),1,9);var yellow=Object(typeof(Door),9,1);
        var innerAnimal=Object(typeof(Character),1,5);innerAnimal.Tamed=true;controller.DataChanged(innerAnimal);
        var outerAnimal=Object(typeof(Character),9,1);outerAnimal.Tamed=true;controller.DataChanged(outerAnimal);
        Tick(0);Tick(3);
        Check("actual registry assigns all covering object and tame tiers to server",red.GetOwner()==99&&orange.GetOwner()==99&&yellow.GetOwner()==99&&innerAnimal.GetOwner()==99&&outerAnimal.GetOwner()==99);
        MovePeer(second,49,1);Tick(4);
        Check("coarse shared-region gate remains active after fine overlap ends",controller.RegionGate.SharedPlayers==2);
        Check("unmoving sole player receives red objects and red tame only",red.GetOwner()==1&&innerAnimal.GetOwner()==1&&orange.GetOwner()==99&&yellow.GetOwner()==99&&outerAnimal.GetOwner()==99);
        int attempts=controller.ScheduledAttempts,queries=ZNetScene.instance.AreaQueries,scans=GameObject.Scans;
        for(int sample=5;sample<=20;sample++)Tick(sample);
        Check("held orange yellow and tamed state performs no object jobs or readiness scans",controller.ScheduledAttempts==attempts&&ZNetScene.instance.AreaQueries==queries&&GameObject.Scans==scans&&outerAnimal.GetOwner()==99);
        MovePeer(first,1,5);Tick(21);
        Check("real cell movement releases promoted forward object immediately",orange.GetOwner()==1&&yellow.GetOwner()==99&&outerAnimal.GetOwner()==99);
        Tick(24);
        Check("same-tier retained object and tame release after normal outer timer",yellow.GetOwner()==1&&outerAnimal.GetOwner()==1);
    }
    private static void DirectionalController()
    {
        foreach(int size in new[]{4,8,16})
        {
            Fresh(Kinds.Doors|Kinds.TamedAnimals,cellSize:size,pattern:GridCoveragePattern.Rounded7x7,directional:true);var player=Peer(1);
            var data=new List<ZDO>();
            for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++)data.Add(Object(typeof(Door),x*size+1,z*size+1));
            var tame=Object(typeof(Character));tame.Tamed=true;controller.DataChanged(tame);Tick(0);
            foreach(var door in data)
            {
                int x=(int)Math.Floor(door.Position.x/size),z=(int)Math.Floor(door.Position.z/size);
                bool red=Math.Abs(x)<=1&&(z==0||z==1);
                Check("production registry uses selected size for six instant fields",door.GetOwner()==(red?1:7));
            }
            Check("tame uses same zero inner timer",tame.GetOwner()==1);
            Tick(3);
            foreach(var door in data)
            {
                int x=(int)Math.Floor(door.Position.x/size),z=(int)Math.Floor(door.Position.z/size);
                int[] widths={3,3,2,1};bool inside=Math.Abs(x)<=widths[Math.Abs(z)]&&(z>=0||z==-1&&Math.Abs(x)>1);
                Check("production registry follows the second sketch including white exclusions",door.GetOwner()==(inside?1:7));
            }
            int writes=0;foreach(var door in data)writes+=door.Writes;int scans=GameObject.Scans,queries=ZNetScene.instance.AreaQueries;
            for(int i=4;i<14;i++)Tick(i);int after=0;foreach(var door in data)after+=door.Writes;
            Check("standing production state causes no scans area queries or owner writes",writes==after&&scans==GameObject.Scans&&queries==ZNetScene.instance.AreaQueries);
            var character=ZDOMan.instance.GetZDO(player.m_characterID);character.Yaw=22.4f;Tick(14);
            var west=Object(typeof(Door),-size+1,size+1);Tick(15);Check("same sector retains north red shape",west.GetOwner()==1);
            character.Yaw=90;Tick(16);
            var east=Object(typeof(Door),size+1,1);Tick(17);Check("replicated yaw changes orientation to east",east.GetOwner()==1);
            var behind=Object(typeof(Door),-size+1,1);Tick(18);Check("new east-white object has no grid takeover",behind.GetOwner()==7);
            tame.Position=new Vector3(size+1,0,1);controller.Position(tame);tame.SetOwner(7);controller.OwnerChanged(tame);Tick(19);
            Check("mobile tame and ordinary owner events add no hidden legacy wait in red field",tame.GetOwner()==1);
        }

        Fresh(Kinds.Doors,directional:true,pattern:GridCoveragePattern.Rounded7x7);Peer(1);var source=Peer(7,64);FiresGhettoNetworkMod.FgnVersionGate.Match=true;
        var cooperative=Object(typeof(Door));Tick(0);Tick(1);
        Check("instant grid still waits for real FGN source consent",cooperative.GetOwner()==7&&cooperative.Writes==0&&source.m_rpc.Calls==1);
        Import(cooperative,1,false);Tick(2);Check("FGN receipt settles fast field without an extra timer",cooperative.GetOwner()==1&&cooperative.Writes==0);

        Fresh(Kinds.Containers,directional:true,pattern:GridCoveragePattern.Rounded7x7);Peer(1);Peer(7,64);
        var chestData=Object(typeof(Container));var view=ZNetScene.instance.FindInstance(chestData);var chest=view.GetComponent<Container>();int requests=0;
        view.OnInvoke=delegate{requests++;};chestData.Use=1;Tick(0);Tick(1);
        Check("instant field never takes open inventory",chestData.GetOwner()==7&&requests==0);
        chestData.Use=0;controller.DataChanged(chestData);Tick(2);Check("closed fast-field chest uses native source handoff",chestData.GetOwner()==7&&requests==1);
        Import(chestData,99,true);Tick(3);Check("native ownership receipt alone cannot authorize final handoff",chestData.GetOwner()==99);
        controller.InventoryResponse(chest,7,true);Tick(4);Check("native grant and exact receipt finish fast-field chest",chestData.GetOwner()==1);
        chestData.Use=1;Peer(2);Tick(5);Tick(6);Check("new overlapping red peer cannot steal open chest",chestData.GetOwner()==1&&requests==1);

        Fresh(Kinds.Smelters,directional:true,pattern:GridCoveragePattern.Rounded7x7);Peer(1);var station=Object(typeof(Smelter));Tick(0);ZDOMan.instance.MarkSent(1,station);
        var refill=Request(station,"RPC_AddOre",1);Tick(1);controller.Router.TryHandle(ZRoutedRpc.instance,refill,false);
        Peer(2);Tick(2);Tick(10.99f);Check("red overlap retains full observed refill lease",station.GetOwner()==1);
        Tick(11);Check("refill expiry permits zero-timer settled overlap",station.GetOwner()==99);

        Fresh(Kinds.TamedAnimals,directional:true,pattern:GridCoveragePattern.Rounded7x7);Peer(1);var animal=Object(typeof(Character));animal.Tamed=true;controller.DataChanged(animal);Tick(0);
        var tameable=ZNetScene.instance.FindInstance(animal).GetComponent<Tameable>();tameable.m_commandable=true;animal.Follow="player";Peer(2);Tick(1);
        Check("zero timer preserves followed animal's owner",animal.GetOwner()==1);
        animal.Follow="";tameable.m_saddle=tameable.gameObject.Add(new Sadle{User=true});Tick(2);Check("zero timer preserves ridden animal's owner",animal.GetOwner()==1);
        tameable.m_saddle.User=false;Tick(3);Check("idle animal immediately follows settled overlap",animal.GetOwner()==99);

        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);Peer(1);
        var white=Object(typeof(Door),1,-3);var redDoor=Object(typeof(Door));Tick(0);
        Check("independent server default remains explicit outside directional coverage",white.GetOwner()==99&&redDoor.GetOwner()==1);
        Fresh(Kinds.Doors,directional:true);Peer(1);cooperative=Object(typeof(Door));ZNetScene.instance.Ready=delegate{return false;};Tick(0);Tick(1);
        Check("zero timer never bypasses local area readiness",cooperative.GetOwner()==7&&cooperative.Writes==0);
        ZNetScene.instance.Ready=delegate{return true;};Tick(2);Check("ready local area permits immediate pending field change",cooperative.GetOwner()==1);
    }
    private static AuthorityWorld Index()
    { return (AuthorityWorld)typeof(AuthorityController).GetField("indexed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller); }
    private static void FieldCacheController()
    {
        Fresh(Kinds.Doors,directional:true,pattern:GridCoveragePattern.Rounded7x7);var player=Peer(1);var door=Object(typeof(Door),1,17);
        Tick(0);Tick(3);var index=Index();int attempts=index.Attempts,queries=ZNetScene.instance.AreaQueries,scans=GameObject.Scans;
        Check("production successful outer field certified",index.Grid.RememberedCells==1&&door.GetOwner()==1);
        var character=ZDOMan.instance.GetZDO(player.m_characterID);character.Yaw=180;Tick(4);Tick(7);character.Yaw=0;Tick(8);
        Check("production reentry skips bucket and outer timer",index.Attempts==attempts&&index.SkippedCells==1&&door.GetOwner()==1);
        Check("production cache reentry adds no Unity scan/readiness query",queries==ZNetScene.instance.AreaQueries&&scans==GameObject.Scans);
        door.SetOwner(7);controller.OwnerChanged(door);
        Check("native owner hook invalidates certificate",index.Grid.RememberedCells==0);
        Tick(11);Check("owner drift reconciles and recertifies",door.GetOwner()==1&&index.Grid.RememberedCells==1);
        controller.Unregister(ZNetScene.instance.FindInstance(door));Check("production unload clears remembered cell",index.Grid.RememberedCells==0&&index.BucketCount==0);

        Fresh(Kinds.Doors,directional:true,pattern:GridCoveragePattern.Rounded7x7);Peer(1);var source=Peer(7,64);door=Object(typeof(Door),1,17);
        FiresGhettoNetworkMod.FgnVersionGate.Match=true;var destinations=new List<long>();
        source.m_rpc.OnInvoke=delegate(string name,object[] values){if(name=="FGN_HandOff")destinations.Add((long)values[1]);};
        Tick(0);Time.unscaledTime=2;Peer(2);Tick(2);Tick(3);
        Check("overlap sends only a direct server request at the first timer deadline",destinations.Count==1&&destinations[0]==99&&door.GetOwner()==7);
        Import(door,99,false);Tick(4);Check("source commit settles overlap without certifying server field",door.GetOwner()==99&&Index().Grid.RememberedCells==0);

        Fresh(Kinds.Containers,directional:true,pattern:GridCoveragePattern.Rounded7x7);Peer(1);var chestData=Object(typeof(Container),1,17,1);
        var view=ZNetScene.instance.FindInstance(chestData);var chest=view.GetComponent<Container>();int requests=0;view.OnInvoke=delegate{requests++;};
        Tick(0);Tick(3);Check("already-owned public container can certify",Index().Grid.RememberedCells==1);
        chestData.Use=1;Peer(2);Tick(4);Tick(7);
        Check("foreign overlap invalidates cache and still cannot take open inventory",Index().Grid.RememberedCells==0&&chestData.GetOwner()==1&&requests==0);
        chestData.Use=0;controller.DataChanged(chestData);Tick(8);
        Check("closed former cached chest still uses native handoff consent",requests==1&&chestData.GetOwner()==1);
        Import(chestData,99,true);controller.InventoryResponse(chest,1,true);Tick(9);
        Check("native grant/revision settles uncached server ownership",chestData.GetOwner()==99&&Index().Grid.RememberedCells==0);
    }
    private static void DestroyedViewCleanup()
    {
        Fresh(Kinds.Doors,directional:true);Peer(1);var data=Object(typeof(Door));Tick(0);
        var view=ZNetScene.instance.FindInstance(data);var index=Index();
        Check("loaded object has a certified field",index.Grid.RememberedCells==1&&index.BucketCount==1);
        view.Alive=false;
        Check("destroyed Unity wrapper compares null but still has CLR identity",view==null&&!System.Object.ReferenceEquals(view,null));
        controller.Unregister(view);
        Check("destroyed wrapper still unregisters all field and object references",index.Grid.RememberedCells==0&&index.BucketCount==0&&index.PendingCount==0);
        var registry=(Dictionary<ZDOID,AuthorityController.NativeNode>)typeof(AuthorityController).GetField("nodes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
        Check("destroyed wrapper is absent from controller's strong registry",registry.Count==0);
        controller.Unregister(view);controller.Unregister(null);
        Check("duplicate/null destruction is harmless",registry.Count==0&&index.BucketCount==0&&failures==0);

        Fresh(Kinds.Doors,directional:true);Peer(1);Object(typeof(Door));Tick(0);index=Index();
        var scene=ZNetScene.instance;controller.SceneDestroyed(new ZNetScene());
        Check("unrelated scene destruction cannot reset live authority",controller.IsActive&&System.Object.ReferenceEquals(Index(),index));
        scene.Alive=false;ZNetScene.instance=null;controller.SceneDestroyed(scene);
        registry=(Dictionary<ZDOID,AuthorityController.NativeNode>)typeof(AuthorityController).GetField("nodes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
        Check("scene destruction clears all strong state without Shutdown",!controller.IsActive&&Index()==null&&registry.Count==0&&controller.Inventory==null&&controller.Cooperative==null);
        controller.Tick();Check("missing scene remains harmless after cleanup",failures==0&&!controller.IsActive);
        ZNetScene.instance=new ZNetScene();Object(typeof(Door),1,1,1);Tick(1);index=Index();controller.SceneDestroyed(scene);
        Check("late old-scene callback cannot clear newly loaded state",controller.IsActive&&System.Object.ReferenceEquals(Index(),index)&&registry.Count==1);
    }
    private static void QuarterSecondCadence()
    {
        Check("invalid cadence falls back to configured twentieth-second default",new OwnershipCadence(Single.NaN).Interval==.05f&&new OwnershipCadence(Single.PositiveInfinity).Interval==.05f);
        Check("supported cadence bounds",new OwnershipCadence(0).Interval==.05f&&new OwnershipCadence(30).Interval==5f);
        var cadence=new OwnershipCadence(.25f);
        Check("first cadence sample is immediate",cadence.Due(0));
        Check("no early movement check",!cadence.Due(.249f));
        Check("quarter-second deadline is due",cadence.Due(.25f));
        Check("native per-frame calls do not repeatedly run grid",!cadence.Due(.251f)&&!cadence.Due(.499f));
        Check("second quarter-second deadline is due",cadence.Due(.5f));
        Check("invalid clock does not poison cadence",!cadence.Due(Single.NaN)&&!cadence.Due(Single.PositiveInfinity)&&!cadence.Due(.6f));
        Check("time rollback resumes immediately",cadence.Due(0)&&!cadence.Due(.1f)&&cadence.Due(.25f));

        Fresh(Kinds.Doors,directional:true,checkIntervalSeconds:.25f);Peer(1);var door=Object(typeof(Door));
        Time.time=Time.unscaledTime=0;controller.Poll(0);
        Check("red field executes on first poll",controller.IsActive&&door.GetOwner()==1&&failures==0);
        Peer(2,1,1);Time.time=Time.unscaledTime=.1f;controller.Poll(.1f);
        Check("second player does not bypass configured cadence",door.GetOwner()==1);
        Time.time=Time.unscaledTime=.25f;controller.Poll(.25f);
        Check("standing peer overlap is picked up at quarter second",door.GetOwner()==99&&failures==0);

        Fresh(Kinds.Doors,pattern:GridCoveragePattern.Rounded5x5,directional:true,checkIntervalSeconds:.25f);Peer(1);door=Object(typeof(Door),1,17);
        Time.time=Time.unscaledTime=0;controller.Poll(0);
        for(int i=1;i<12;i++){Time.time=Time.unscaledTime=i*.25f;controller.Poll(Time.unscaledTime);Check("standing outer timer remains protected until deadline",door.GetOwner()==7&&failures==0);}
        Time.time=Time.unscaledTime=3;controller.Poll(3);
        Check("standing player outer timer completes without movement event",door.GetOwner()==1&&failures==0);
    }
    private static int ExpectedDirectionalTier(int x,int z,int heading,int radius,bool forward)
    {
        for(int turn=0;turn<heading/2;turn++){int old=x;x=-z;z=old;}
        if(forward&&((heading&1)==0?z==2&&Math.Abs(x)<=1:(x==1&&z==2)||(x==2&&z==1)||(x==2&&z==2)))return 3;
        int[] widths=radius==2?new[]{2,2,1}:new[]{3,3,2,1};
        if(Math.Abs(z)>radius||Math.Abs(x)>widths[Math.Abs(z)])return 0;
        if((heading&1)==0){if(z<-1||z==-1&&Math.Abs(x)<=1)return 0;return z>=0&&z<=1&&Math.Abs(x)<=1?1:2;}
        int[] low={9,1,1,0,-2,-2,-1},high={-9,2,3,3,3,2,1};
        if(x<low[z+3]||x>high[z+3])return 0;
        return (x==0&&z==0)||(x==1&&z==-1)||(x==-1&&z==1)||(x==1&&z==0)||(x==1&&z==1)||(x==0&&z==1)?1:2;
    }
    private static void ForwardController()
    {
        foreach(int size in new[]{4,8,16})foreach(int radius in new[]{2,3})for(int heading=0;heading<8;heading++)
        {
            var shape=radius==2?GridCoveragePattern.Rounded5x5:GridCoveragePattern.Rounded7x7;
            Fresh(Kinds.Doors,cellSize:size,pattern:shape,directional:true);var player=Peer(1);ZDOMan.instance.GetZDO(player.m_characterID).Yaw=heading*45;
            var objects=new List<ZDO>();for(int x=-radius;x<=radius;x++)for(int z=-radius;z<=radius;z++)objects.Add(Object(typeof(Door),x*size+1,z*size+1));
            Tick(0);foreach(var data in objects){int tier=ExpectedDirectionalTier((int)Math.Floor(data.Position.x/size),(int)Math.Floor(data.Position.z/size),heading,radius,size==4);Check("only inner fields are immediate before forward deadline",data.GetOwner()==(tier==1?1:7));}
            Tick(.199f);foreach(var data in objects){int tier=ExpectedDirectionalTier((int)Math.Floor(data.Position.x/size),(int)Math.Floor(data.Position.z/size),heading,radius,size==4);Check("production forward deadline is not early",data.GetOwner()==(tier==1?1:7));}
            Tick(.2f);foreach(var data in objects){int tier=ExpectedDirectionalTier((int)Math.Floor(data.Position.x/size),(int)Math.Floor(data.Position.z/size),heading,radius,size==4);Check("4m auto forward tier transfers exact fields in all headings",data.GetOwner()==(tier==1||tier==3?1:7));}
            Tick(3);foreach(var data in objects){int tier=ExpectedDirectionalTier((int)Math.Floor(data.Position.x/size),(int)Math.Floor(data.Position.z/size),heading,radius,size==4);Check("5x5 and 7x7 exclusions and other sizes remain correct",data.GetOwner()==(tier!=0?1:7));}
        }
        bool rejected=false;try{Fresh(Kinds.Doors,cellSize:4);}catch(ArgumentException){rejected=true;}
        Check("controller cannot run unsupported 4m 3x3",rejected);
        Fresh(Kinds.Doors,cellSize:4,pattern:GridCoveragePattern.Rounded5x5,directional:true,forwardDelay:0);Peer(1);var door=Object(typeof(Door),1,9);Tick(0);
        Check("zero configured forward delay is supported",door.GetOwner()==1);
        Fresh(Kinds.Doors,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true,forwardDelay:.8f);Peer(1);door=Object(typeof(Door),1,9);Tick(0);Tick(.799f);
        Check("production custom forward timer remains independent",door.GetOwner()==7);Tick(.8f);Check("production custom forward timer completes",door.GetOwner()==1);
        Fresh(Kinds.Doors,cellSize:4,pattern:GridCoveragePattern.Rounded5x5,directional:true,forwardDelay:0);Peer(1);door=Object(typeof(Door),1,9);ZNetScene.instance.Ready=delegate{return false;};Tick(0);
        Check("zero forward timer does not bypass area readiness",door.GetOwner()==7&&door.Writes==0);ZNetScene.instance.Ready=delegate{return true;};Tick(.1f);Check("pending forward collider readiness resumes normally",door.GetOwner()==1);

        Fresh(Kinds.Doors,cellSize:4,pattern:GridCoveragePattern.Rounded5x5,directional:true,forwardDelay:0);Peer(1);var source=Peer(7,64);FiresGhettoNetworkMod.FgnVersionGate.Match=true;door=Object(typeof(Door),1,9);Tick(0);
        Check("zero forward timer respects actual FGN source handoff",door.GetOwner()==7&&source.m_rpc.Calls==1&&door.Writes==0);Import(door,1,false);Tick(.1f);Check("forward FGN receipt completes without extra timer",door.GetOwner()==1&&door.Writes==0);
        Fresh(Kinds.Containers,cellSize:4,pattern:GridCoveragePattern.Rounded5x5,directional:true,forwardDelay:0);Peer(1);Peer(7,64);var dataChest=Object(typeof(Container),1,9);var view=ZNetScene.instance.FindInstance(dataChest);var chest=view.GetComponent<Container>();int requests=0;view.OnInvoke=delegate{requests++;};dataChest.Use=1;Tick(0);Tick(.2f);
        Check("forward zero timer cannot steal an open chest",dataChest.GetOwner()==7&&requests==0);dataChest.Use=0;controller.DataChanged(dataChest);Tick(.3f);
        Check("closed forward chest starts native consent request",requests==1&&dataChest.GetOwner()==7);Import(dataChest,99,false);Tick(.4f);Check("forward inventory still waits for exact native grant",dataChest.GetOwner()==99);controller.InventoryResponse(chest,7,true);Tick(.5f);Check("forward chest finishes after actual receipt and grant",dataChest.GetOwner()==1);

        Fresh(Kinds.Smelters,cellSize:4,pattern:GridCoveragePattern.Rounded5x5,directional:true);Peer(1);var station=Object(typeof(Smelter),1,9);Tick(0);Tick(.2f);ZDOMan.instance.MarkSent(1,station);Tick(1);controller.Router.TryHandle(ZRoutedRpc.instance,Request(station,"RPC_AddOre",1),false);Peer(2);Tick(1.2f);Tick(10.99f);
        Check("forward overlap preserves observed refill lease",station.GetOwner()==1);Tick(11);Check("forward refill lease expires normally",station.GetOwner()==99);
        Fresh(Kinds.TamedAnimals,cellSize:4,pattern:GridCoveragePattern.Rounded5x5,directional:true);Peer(1);var tame=Object(typeof(Character),1,9);tame.Tamed=true;controller.DataChanged(tame);Tick(0);Tick(.2f);
        Check("idle tame uses same forward timer",tame.GetOwner()==1);var tameable=ZNetScene.instance.FindInstance(tame).GetComponent<Tameable>();tameable.m_commandable=true;tame.Follow="player";Peer(2);Tick(.3f);Tick(.6f);
        Check("forward timer leaves following tame protected",tame.GetOwner()==1);tame.Follow="";tameable.m_saddle=tameable.gameObject.Add(new Sadle{User=true});Tick(.7f);Check("forward timer leaves ridden tame protected",tame.GetOwner()==1);tameable.m_saddle.User=false;Tick(.8f);Check("idle tame follows settled forward overlap",tame.GetOwner()==99);
    }
    private static void DistantInteractiveOwner()
    {
        Fresh(Kinds.Doors|Kinds.Beehives|Kinds.Containers|Kinds.Smelters|Kinds.Workstations,
            defaultInteractives:true, cellSize:4, pattern:GridCoveragePattern.Rounded7x7, directional:true);
        var approaching = Peer(411,-32,0);
        var distant = Peer(822,100,0);
        var objects = new List<ZDO>();
        foreach (Type type in new[]{typeof(Door),typeof(Beehive),typeof(Container),typeof(Smelter),typeof(CraftingStation)})
            objects.Add(Object(type,1,1,99));
        Tick(0);
        foreach (ZDO data in objects) Check("uncovered interactive begins server owned",data.GetOwner()==99);
        MovePeer(approaching,0,0);
        Tick(.05f);
        Check("distant player never requests approaching player's inner cell",Index().Grid.Owner(new Cell(8,8))==411 && Index().Grid.Candidate(new Cell(8,8))==411);
        foreach (ZDO data in objects)
        {
            Check("server to nearby UID across interactive categories",data.GetOwner()==411);
            Check("settled interactive blocks native owner reassignment",!controller.Persistent(data));
        }
        MovePeer(distant,1024,0);
        Tick(.1f);
        foreach (ZDO data in objects) Check("other player's later teleport cannot change local owner",data.GetOwner()==411);
        // A different loaded area retains its exclusive owner on full field exit.
        var remote = Object(typeof(Door),1025,1,99);
        Tick(.15f);
        Check("distant player owns their own inner cell",remote.GetOwner()==822);
        MovePeer(distant,2048,0);
        Tick(.2f);
        Check("fully vacated remote cell retains owner until native release",remote.GetOwner()==822 && controller.Persistent(remote));
    }
    private static void LateGridCapability()
    {
        var capable = new HashSet<long>();
        var commands = new Dictionary<ZDOID,long>();
        int sends = 0;
        FgnOwnerHandoff.CooperativeClient = delegate(ZNetPeer peer) { return peer != null && capable.Contains(peer.m_uid); };
        FgnOwnerHandoff.IdentifiedSend = delegate(ZNetPeer peer,ZDOID id,long target,uint revision,long token) {
            Check("grid sends only to confirmed current source", capable.Contains(peer.m_uid));
            Check("far player's native owner does not become local grid target", target == 1);
            commands[id] = token; sends++;
        };
        FgnOwnerHandoff.IdentifiedStatus = delegate { };
        try
        {
            Fresh(Kinds.Doors|Kinds.Beehives|Kinds.Smelters|Kinds.Workstations|Kinds.TamedAnimals,
                defaultInteractives:true, cellSize:4, pattern:GridCoveragePattern.Rounded7x7,
                directional:true, checkIntervalSeconds:.05f, overlapCooldown:1);
            Peer(1,40);ZNetPeer source=Peer(7,140);
            var objects=new List<ZDO>();
            foreach(Type type in new[]{typeof(Door),typeof(Beehive),typeof(Smelter),typeof(CraftingStation)})
                objects.Add(Object(type,41,1,7));
            ZDO animal=Object(typeof(Character),41,1,7);animal.Tamed=true;controller.DataChanged(animal);
            Tick(0);
            Check("unconfirmed endpoints keep native ownership with no speculative commit",sends==0);
            Check("idle tamed animal still transfers through its separate protected path",animal.GetOwner()==1);
            capable.Add(7);controller.CapabilityChanged(7);Tick(.05f);
            Check("source alone cannot transfer to an unconfirmed recipient",sends==0);
            capable.Add(1);controller.CapabilityChanged(1);Tick(.1f);
            Check("late capability wakes all nearby object categories without player movement",sends==objects.Count);
            Check("identified TML endpoint works before independent FGN version greeting",!FiresGhettoNetworkMod.FgnVersionGate.Match);
            foreach(ZDO data in objects)
            {
                Check("identified source commit waits rather than forcing the far player's owner",data.GetOwner()==7&&controller.TransferPending(data));
                Check("source result accepted for its own fenced command",controller.Cooperative.Observed(source.m_rpc,data.m_uid,commands[data.m_uid],true,true,1,(uint)data.OwnerRevision+1));
                Import(data,1,false);
            }
            Tick(.15f);
            foreach(ZDO data in objects)
                Check("far owner relinquished and native redistribution cannot undo local result",data.GetOwner()==1&&!controller.TransferPending(data)&&!controller.Persistent(data));
            for(int i=1;i<=100;i++)Tick(.15f+i*.05f);
            Check("stable ownership adds no repeated commits or greetings",sends==objects.Count);
        }
        finally { FgnOwnerHandoff.CooperativeClient=null;FgnOwnerHandoff.IdentifiedSend=null;FgnOwnerHandoff.IdentifiedStatus=null; }
    }
    private static void TeleportFootprint()
    {
        Fresh(Kinds.Doors, defaultInteractives:true, cellSize:4, pattern:GridCoveragePattern.Rounded7x7, directional:true);
        Peer(1); var departing = Peer(2);
        var door = Object(typeof(Door), 1, 1, 99);
        Tick(0);
        Check("two nearby players hold server field", Index().Grid.Owner(new Cell(8,8)) == 99);
        // Reference-area update has arrived, character ZDO still has old position.
        departing.Position = new Vector3(1024,0,0);
        Tick(.1f);
        Check("teleported peer cannot retain stale old-area footprint", Index().Grid.Owner(new Cell(8,8)) == 1 && door.GetOwner() == 1);
        // Normal replication skew within a loaded area must stay eligible.
        departing.Position = new Vector3(20,0,0);
        Tick(.2f);
        Check("in-area character/ref skew retains valid overlap", Index().Grid.Owner(new Cell(8,8)) == 99 && door.GetOwner() == 99);
        // Character position catches up before a new reference update.
        ZDOMan.instance.GetZDO(departing.m_characterID).Position = new Vector3(1024,0,0);
        Tick(.3f);
        Check("far character with old reference adds no unreachable field", Index().Grid.Owner(new Cell(8,8)) == 1);
        departing.Position = new Vector3(1024,0,0);
        Tick(.4f);
        Check("new teleport area becomes eligible once both inputs arrive", Index().Grid.HasPeers && Index().Grid.Candidate(new Cell(264,8)) == 2);
    }
    private static void SoloRegions()
    {
        Fresh(Kinds.Doors|Kinds.Containers|Kinds.TamedAnimals,nativeSolo:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var first=Peer(1,512);var door=Object(typeof(Door),513,1,0);var animal=Object(typeof(Character),514,1,1);animal.Tamed=true;controller.DataChanged(animal);
        var chest=Object(typeof(Container),515,1,1);var character=ZDOMan.instance.GetZDO(first.m_characterID);
        Tick(0);Check("alone leaves actual ownerless object to native",door.GetOwner()==0&&controller.FieldUpdates==0&&character.RotationReads==0);
        ZDOMan.instance.ReleaseNearbyZDOS(first.Position,1);controller.OwnerChanged(door);Tick(.25f);
        Check("native assignment still acquires solo object",door.GetOwner()==1&&controller.Persistent(door));
        int attempts=controller.ScheduledAttempts,rebuilds=controller.RegionGate.Rebuilds,positions=character.PositionReads;
        for(int i=1;i<=200;i++){MovePeer(first,512+(i%6)*4);character.Yaw=(i%8)*45;Tick(.25f+i*.25f);}
        Check("solo movement and turning never update fine footprint",controller.FieldUpdates==0&&character.RotationReads==0);
        Check("single connection never samples character position",character.PositionReads==positions);
        Check("solo stable region has no new object jobs or coarse rebuilds",controller.ScheduledAttempts==attempts&&controller.RegionGate.Rebuilds==rebuilds);
        var far=Peer(2,2048);Tick(51);Check("separate player regions stay native",controller.RegionGate.SharedPlayers==0&&controller.FieldUpdates==0);
        MovePeer(far,576);Tick(51.25f);
        Check("adjacent native regions activate both real footprints",controller.RegionGate.SharedPlayers==2&&controller.FieldUpdates==2);
        MovePeer(first,512);MovePeer(far,512);Tick(52);Tick(56);
        Check("fine overlap selects server",door.GetOwner()==99&&animal.GetOwner()==99);
        ZNet.instance.Peers.Remove(far);Tick(56.25f);Check("becoming solo releases managed owner pin",controller.Persistent(door));
        ZDOMan.instance.ReleaseNearbyZDOS(first.Position,1);controller.OwnerChanged(door);Tick(56.5f);
        Check("native ownership resumes after shared coverage",door.GetOwner()==1);
        chest.Use=1;Tick(57);Check("open inventory remains protected in solo mode",!controller.Persistent(chest));
        chest.Use=0;controller.DataChanged(chest);Check("closed solo inventory is native",controller.Persistent(chest));
        // Owner belongs to another still-active player: no invented region-owner
        // certificate may conceal it when a real second player arrives.
        far=Peer(7,576);door.SetOwner(7);controller.OwnerChanged(door);Tick(58);Tick(62);
        Check("shared entry examines actual foreign object owner",door.GetOwner()==1);
        ZNet.instance.Peers.Clear();Tick(63);Check("disconnect drops all coarse region references",controller.RegionGate.CachedPeers==0&&controller.RegionGate.CachedZones==0);
        controller.Reset();Check("world reset clears coarse state",controller.RegionGate.CachedPeers==0&&controller.RegionGate.CachedZones==0);

        Fresh(Kinds.Doors,nativeSolo:true,defaultInteractives:true,directional:true);first=Peer(1);door=Object(typeof(Door));Tick(0);
        Check("server default bypasses the optimization entirely",door.GetOwner()==1&&controller.FieldUpdates==1&&controller.RegionGate.Rebuilds==0&&ZDOMan.instance.GetZDO(first.m_characterID).RotationReads==1);
        Fresh(Kinds.Doors|Kinds.Smelters,nativeSolo:true,refillStations:true,directional:true);Peer(1);var station=Object(typeof(Smelter));door=Object(typeof(Door));Tick(0);
        Check("permanent refill ownership works while solo grid pauses",station.GetOwner()==99&&door.GetOwner()==7&&!controller.Persistent(station));

        Fresh(Kinds.Smelters,nativeSolo:true,directional:true);first=Peer(1,512);station=Object(typeof(Smelter),513,1,1);Tick(0);ZDOMan.instance.MarkSent(1,station);
        controller.Router.TryHandle(ZRoutedRpc.instance,Request(station,"RPC_AddOre",1),false);
        Check("observed refill lease protects native solo owner",!controller.Persistent(station));Time.unscaledTime=10;
        Check("refill lease expires without forcing an ownership change",controller.Persistent(station));

        Fresh(Kinds.Doors,nativeSolo:true,directional:true);first=Peer(1,512);far=Peer(2,512);var source=Peer(7,576);FiresGhettoNetworkMod.FgnVersionGate.Match=true;door=Object(typeof(Door),513);
        Tick(0);Check("shared mode starts source-authorized handoff",source.m_rpc.Calls==1&&controller.Cooperative.Pending(door));
        ZNet.instance.Peers.Remove(first);ZNet.instance.Peers.Remove(far);Tick(.25f);
        Check("isolating source does not cancel unanswered handoff",controller.Cooperative.Pending(door)&&!controller.Persistent(door)&&source.m_rpc.Calls==1);
        controller.Cooperative.Result(source.m_rpc,door.m_uid,false,"held");Tick(.5f);Tick(20);
        Check("known veto releases paused reservation without retry",!controller.Cooperative.Reserved(door)&&controller.Persistent(door)&&source.m_rpc.Calls==1);

        Fresh(Kinds.Containers,nativeSolo:true,directional:true);first=Peer(1,512);far=Peer(2,512);source=Peer(7,576);chest=Object(typeof(Container),513);var view=ZNetScene.instance.FindInstance(chest);var inventory=view.GetComponent<Container>();int requests=0;view.OnInvoke=delegate{requests++;};Tick(0);
        Check("shared inventory uses native request",requests==1&&controller.Inventory.Pending(chest));
        ZNet.instance.Peers.Remove(first);ZNet.instance.Peers.Remove(far);Tick(.25f);Check("solo transition retains unanswered inventory request",controller.Inventory.Pending(chest)&&!controller.Persistent(chest));
        Import(chest,99,false);controller.InventoryResponse(inventory,7,true);Tick(.5f);
        Check("solo transition accepts completed native commit without forced onward write",chest.GetOwner()==99&&!controller.Inventory.Pending(chest)&&controller.Persistent(chest));
        ZDOMan.instance.ReleaseNearbyZDOS(source.Position,7);controller.OwnerChanged(chest);Tick(.75f);
        Check("native assignment after receipt is not blocked by stale authority pin",chest.GetOwner()==7&&controller.Persistent(chest)&&requests==1);

        Fresh(Kinds.Doors,nativeSolo:true,statics:true);first=Peer(1,512);var wall=Object(null,513);Tick(0);
        Check("separate static building option remains effective while solo",!controller.Persistent(wall)&&wall.GetOwner()==99&&controller.FieldUpdates==0);
        Fresh(Kinds.Doors,nativeSolo:true,directional:true);ZoneSystem.instance.m_zoneSize=128;Peer(1);door=Object(typeof(Door));Tick(0);
        Check("nonstandard native zone size falls back to old grid policy",door.GetOwner()==1&&controller.FieldUpdates==1&&controller.RegionGate.CachedZones==0);
        ZoneSystem.instance.m_zoneSize=64;Tick(.25f);Check("restoring native size resumes safe solo bypass",controller.Persistent(door)&&controller.RegionGate.SharedPlayers==0);

        Fresh(Kinds.Containers,nativeSolo:true,directional:true);first=Peer(1,512);far=Peer(2,512);source=Peer(7,576);chest=Object(typeof(Container),513);Tick(0);
        ZNet.instance.Peers.Remove(first);ZNet.instance.Peers.Remove(far);chest.SetOwnerInternal(99);controller.OwnerChanged(chest);Tick(6);
        Check("an expired query still protects an incomplete owner/revision packet",controller.Inventory.Transitioning(chest)&&!controller.Persistent(chest));
        chest.OwnerRevision++;controller.OwnerChanged(chest,false);Tick(6.25f);
        Check("completed packet releases paused query without forced onward ownership",!controller.Inventory.Transitioning(chest)&&controller.Persistent(chest));

        Fresh(Kinds.Doors,nativeSolo:true,directional:true);first=Peer(1,512);var loading=new ZNetPeer{Uid=2,Position=new Vector3(576,0,0)};ZNet.instance.Peers.Add(loading);door=Object(typeof(Door),513);Tick(0);
        Check("ready joining peer activates stationary real player before character exists",controller.RegionGate.SharedPlayers==2&&controller.FieldUpdates==1&&door.GetOwner()==1);
        loading.Ready=false;Tick(.25f);Check("non-ready peer cannot keep solo grid active",controller.RegionGate.SharedPlayers==0&&controller.Persistent(door));

        Fresh(Kinds.Doors,nativeSolo:true,directional:true);var all=new List<ZNetPeer>();for(int i=0;i<12;i++)all.Add(Peer(i+1,i*768));Tick(0);
        int pairs=controller.RegionGate.PairTests,builds=controller.RegionGate.Rebuilds;
        for(int frame=1;frame<=100;frame++)for(int i=0;i<12;i++){MovePeer(all[i],i*768+frame%6*4);ZDOMan.instance.GetZDO(all[i].m_characterID).Yaw=frame%8*45;if(i==11)Tick(frame*.25f);}
        Check("12 distant players add zero rotations or fine requests",controller.FieldUpdates==0);
        foreach(var peer in all)Check("distant peers skip rotation sampling",ZDOMan.instance.GetZDO(peer.m_characterID).RotationReads==0);
        Check("far peers inside stable native zones repeat no pair tests",controller.RegionGate.PairTests==pairs&&controller.RegionGate.Rebuilds==builds&&controller.RegionGate.CachedZones==0);
    }
    private static void IdentifiedInteractionDelivery()
    {
        // The receiver's cheap filter must retain every supported routed action.
        var actions = new Dictionary<Kinds,string> {
            { Kinds.Doors, "UseDoor" }, { Kinds.Beehives, "RPC_Extract" },
            { Kinds.Portals, "RPC_SetTag" }, { Kinds.Containers, "RPC_RequestOpen" },
            { Kinds.ItemStands, "RPC_RequestOwn" }, { Kinds.Smelters, "RPC_AddOre" }
        };
        foreach (var action in actions)
        {
            int hash = action.Value.GetStableHashCode();
            Check("receiver filter retains native action " + action.Value,
                ObjectCatalog.OwnerRequest(action.Key, hash) && ObjectCatalog.AnyOwnerRequest(hash));
        }
        Check("receiver filter retains building damage", ObjectCatalog.AnyOwnerRequest("RPC_Damage".GetStableHashCode()));
        Check("receiver filter leaves native inventory replies alone", !ObjectCatalog.AnyOwnerRequest("RPC_OpenResponse".GetStableHashCode()));
        long token = 0; bool recipientReady = false;
        FgnOwnerHandoff.CooperativeClient = delegate { return true; };
        FgnOwnerHandoff.IdentifiedSend = delegate(ZNetPeer p, ZDOID id, long target, uint revision, long t) { token = t; };
        FgnOwnerHandoff.IdentifiedStatus = delegate { };
        RequestRouter.RecipientReady = delegate { return recipientReady; };
        try
        {
            Fresh(Kinds.Doors); Peer(1); ZNetPeer source = Peer(7,64,0); FiresGhettoNetworkMod.FgnVersionGate.Match = true;
            ZDO door = Object(typeof(Door)); Tick(0); Tick(2);
            Check("identified grid handoff pending", token > 0 && controller.TransferPending(door));
            ZDOMan.instance.MarkSent(7,door);
            Check("interaction held during actual commit", controller.Router.TryHandle(ZRoutedRpc.instance,Request(door,"UseDoor",7),false) && ZRoutedRpc.instance.Remote.Count == 0);
            controller.Router.Flush(door.m_uid);
            Check("scoped reply cannot bypass pending source consent",ZRoutedRpc.instance.Remote.Count==0 && controller.TransferPending(door));
            Check("source rejects definitively", controller.Cooperative.Observed(source.m_rpc,door.m_uid,token,true,true,7,door.OwnerRevision));
            controller.Router.Flush(door.m_uid);
            Check("rejection ends commit but sent data is not recipient readiness", !controller.TransferPending(door) && ZRoutedRpc.instance.Remote.Count == 0);
            recipientReady = true; controller.Router.Flush(door.m_uid);
            Check("rejected handoff delivers interaction at valid old owner", ZRoutedRpc.instance.Remote.Count == 1 && door.GetOwner() == 7);
            controller.Router.Flush(); Check("interaction never replayed", ZRoutedRpc.instance.Remote.Count == 1);
            recipientReady = false; controller.Router.Forwarded(Request(door,"UseDoor",7));
            Check("late receiver relay waits rather than bouncing", ZRoutedRpc.instance.Remote.Count == 1);
            recipientReady = true; controller.Router.Flush();
            Check("late receiver relay dispatched once after readiness", ZRoutedRpc.instance.Remote.Count == 2);
            controller.Router.Flush(); Check("relay is not an action retry", ZRoutedRpc.instance.Remote.Count == 2);
        }
        finally { FgnOwnerHandoff.CooperativeClient = null; FgnOwnerHandoff.IdentifiedSend = null; FgnOwnerHandoff.IdentifiedStatus = null; RequestRouter.RecipientReady = null; }
    }
    private static void DelayedReadinessInteractions()
    {
        foreach(bool accept in new[]{false,true})
        foreach(Type type in new[]{typeof(Door),typeof(CraftingStation)})
        {
            long token=0,sequence=0;var probe=new TooManyLags.GridHandoffs.ReadinessProbe();
            FgnOwnerHandoff.CooperativeClient=delegate{return true;};
            FgnOwnerHandoff.IdentifiedSend=delegate(ZNetPeer p,ZDOID id,long target,uint revision,long t){token=t;};
            FgnOwnerHandoff.IdentifiedStatus=delegate{};
            RequestRouter.RecipientReady=delegate(ZDOMan world,long owner,ZDO data){probe.Query(Time.unscaledTime,ref sequence);return probe.Ready;};
            try
            {
                Fresh(Kinds.Doors|Kinds.Workstations);Peer(1);var source=Peer(7,64);FiresGhettoNetworkMod.FgnVersionGate.Match=true;
                var data=Object(type);Tick(0);Tick(2);string method=type==typeof(Door)?"UseDoor":"RPC_Repair";
                Check("door/station handoff starts",token>0&&controller.TransferPending(data));ZDOMan.instance.MarkSent(7,data);
                Check("interaction waits only while actual handoff pending",controller.Router.TryHandle(ZRoutedRpc.instance,Request(data,method,7),false));
                long actual=accept?1:7;uint revision=data.OwnerRevision+(accept?1U:0U);
                Check("definitive source response accepted",controller.Cooperative.Observed(source.m_rpc,data.m_uid,token,true,true,actual,revision));
                if(accept){data.SetOwner(1);controller.OwnerChanged(data);}
                controller.RecordRecipient(data,actual,revision,data.DataRevision);ZDOMan.instance.MarkSent(actual,data);
                controller.Router.Flush();long readinessToken=probe.Token;
                Check("source consent is settled before recipient wait",!controller.TransferPending(data)&&ZRoutedRpc.instance.Remote.Count==0&&readinessToken>0);
                Tick(2.6f);Tick(3.2f);
                Check("slow network retries retain outstanding identity",probe.Token==readinessToken&&sequence==1&&ZRoutedRpc.instance.Remote.Count==0);
                Check("delayed recipient answer completes",probe.Response(readinessToken,true,3.3f));controller.Router.Flush();
                Check("door/station action dispatched once at actual owner",ZRoutedRpc.instance.Remote.Count==1&&data.GetOwner()==actual);
                controller.Router.Flush();Check("late readiness cannot replay action",ZRoutedRpc.instance.Remote.Count==1);
                var next=Request(data,method,actual);next.m_senderPeerID=2;
                Check("another sender can use ready owner's native path",!controller.Router.TryHandle(ZRoutedRpc.instance,next,false));
            }
            finally{FgnOwnerHandoff.CooperativeClient=null;FgnOwnerHandoff.IdentifiedSend=null;FgnOwnerHandoff.IdentifiedStatus=null;RequestRouter.RecipientReady=null;}
        }
    }
    private static void ScopedInteractionDelivery()
    {
        var ready = new HashSet<ZDOID>(); int reads = 0;
        RequestRouter.RecipientReady = delegate(ZDOMan world,long owner,ZDO data) { reads++; return ready.Contains(data.m_uid); };
        try
        {
            Fresh(Kinds.Doors,defaultInteractives:true); Peer(1);
            var doors = new List<ZDO>();
            for (int i=0;i<100;i++) doors.Add(Object(typeof(Door),1,1,1));
            Tick(0); Tick(2);
            foreach (ZDO door in doors)
            {
                ZDOMan.instance.MarkSent(1,door);
                Check("scoped fixture holds unavailable recipient",controller.Router.TryHandle(ZRoutedRpc.instance,Request(door,"UseDoor",1),false));
            }
            int forced = ZDOMan.instance.Forced; reads=0;
            for(int i=0;i<100;i++) controller.Router.Flush();
            Check("unscoped 100-reply equivalent inspects 10000 recipients",reads==10000 && ZRoutedRpc.instance.Remote.Count==0);
            reads=0;
            foreach (ZDO door in doors) controller.Router.Flush(door.m_uid);
            Check("100 scoped replies inspect 100 waiting recipients",reads==100 && ZRoutedRpc.instance.Remote.Count==0);
            Check("scoped replies preserve force-send deduplication",ZDOMan.instance.Forced==forced);
            reads=0; controller.Router.Flush();
            Check("full fallback still checks all waiting recipients",reads==100);
            reads=0; controller.Router.Flush(new ZDOID{ID=999999});
            Check("unknown reply inspects no unrelated recipient",reads==0);
            ready.Add(doors[0].m_uid); controller.Router.Flush(doors[0].m_uid);
            Check("one ready result dispatches only its object",reads==2 && ZRoutedRpc.instance.Remote.Count==1);
            reads=0; controller.Router.Flush(doors[0].m_uid);
            Check("duplicate result has no work or replay",reads==0 && ZRoutedRpc.instance.Remote.Count==1);
            ready.Remove(doors[0].m_uid);
            var first = Request(doors[0],"UseDoor",1); first.m_msgID=201;
            controller.Router.Forwarded(first);
            Check("relay wakes only its own queue",reads==1 && ZRoutedRpc.instance.Remote.Count==1);
            var second = Request(doors[0],"UseDoor",1); second.m_msgID=202;
            Check("second action joins existing queue",controller.Router.TryHandle(ZRoutedRpc.instance,second,false));
            ready.Add(doors[0].m_uid); reads=0; controller.Router.Flush(doors[0].m_uid);
            Check("targeted delivery retains FIFO and per-dispatch readiness",reads==3 && ZRoutedRpc.instance.Remote.Count==3 &&
                ZRoutedRpc.instance.Remote[1].m_msgID==201 && ZRoutedRpc.instance.Remote[2].m_msgID==202);
            foreach (ZDO door in doors) ready.Add(door.m_uid);
            reads=0; controller.Router.Flush();
            Check("full pass delivers the remaining objects exactly once",reads==198 && ZRoutedRpc.instance.Remote.Count==102);
            reads=0; controller.Router.Flush();
            Check("empty full fallback performs no recipient work",reads==0 && ZRoutedRpc.instance.Remote.Count==102);

            Fresh(Kinds.Containers); Peer(1); ready.Clear();
            ZDO chest=Object(typeof(Container),1,1,1); Tick(0); Tick(2); ZDOMan.instance.MarkSent(1,chest);
            int denied=0; ZRoutedRpc.instance.OnInvoke=delegate(long owner,ZDOID id,string method,object[] args) {
                Check("expiry retains native negative inventory reply",owner==1 && id.Equals(chest.m_uid) && method=="RPC_OpenResponse" && !(bool)args[0]); denied++;
            };
            Check("unready inventory request held",controller.Router.TryHandle(ZRoutedRpc.instance,Request(chest,"RPC_RequestOpen",1),false));
            Time.unscaledTime=8; controller.Router.Flush(new ZDOID{ID=999999});
            Check("unrelated reply does not dispatch or cancel inventory",denied==0);
            controller.Router.Flush(); controller.Router.Flush(chest.m_uid);
            Check("periodic fallback still expires inventory once",denied==1 && ZRoutedRpc.instance.Remote.Count==0);

            foreach (bool scoped in new[]{false,true})
            {
                Fresh(Kinds.Doors); Peer(1); ready.Clear();
                ZDO a=Object(typeof(Door),1,1,1), b=Object(typeof(Door),1,1,1); Tick(0); Tick(2);
                ZDOMan.instance.MarkSent(1,a); ZDOMan.instance.MarkSent(1,b);
                Check("first reentrant action waits",controller.Router.TryHandle(ZRoutedRpc.instance,Request(a,"UseDoor",1),false));
                Check("second reentrant action waits",controller.Router.TryHandle(ZRoutedRpc.instance,Request(b,"UseDoor",1),false));
                a.SetOwner(99); b.SetOwner(99);
                ZRoutedRpc.instance.OnHandle=delegate {
                    controller.Router.Flush(b.m_uid); controller.Router.Flush();
                    controller.Reset();
                };
                if(scoped)controller.Router.Flush(a.m_uid);else controller.Router.Flush();
                Check("nested flush and scene reset never dispatch retired work",ZRoutedRpc.instance.Local.Count==1 && failures==0 && !controller.IsActive);
                controller.Router.Flush(a.m_uid); controller.Router.Flush();
                Check("cleared queues never replay after scoped/full reset",ZRoutedRpc.instance.Local.Count==1 && failures==0);

                Fresh(Kinds.Doors); Peer(1); ready.Clear();
                a=Object(typeof(Door),1,1,1); Tick(0); Tick(2); ZDOMan.instance.MarkSent(1,a);
                var view=ZNetScene.instance.FindInstance(a);
                Check("first removable action waits",controller.Router.TryHandle(ZRoutedRpc.instance,Request(a,"UseDoor",1),false));
                Check("second removable action waits",controller.Router.TryHandle(ZRoutedRpc.instance,Request(a,"UseDoor",1),false));
                a.SetOwner(99); ZRoutedRpc.instance.OnHandle=delegate { controller.Unregister(view); };
                if(scoped)controller.Router.Flush(a.m_uid);else controller.Router.Flush();
                Check("native removal cancels remaining actions in either path",ZRoutedRpc.instance.Local.Count==1 && failures==0 && controller.IsActive);

                Fresh(Kinds.Doors); Peer(1); ready.Clear();
                a=Object(typeof(Door),1,1,1); b=Object(typeof(Door),1,1,1); Tick(0); Tick(2);
                ZDOMan.instance.MarkSent(1,a); ZDOMan.instance.MarkSent(1,b);
                Check("first replacement action waits",controller.Router.TryHandle(ZRoutedRpc.instance,Request(a,"UseDoor",1),false));
                Check("old replacement action waits",controller.Router.TryHandle(ZRoutedRpc.instance,Request(b,"UseDoor",1),false));
                var successorView=ZNetScene.instance.FindInstance(b);
                a.SetOwner(99);
                ZRoutedRpc.instance.OnHandle=delegate {
                    controller.Unregister(successorView); controller.Register(b,successorView);
                    controller.Router.Forwarded(Request(b,"UseDoor",1));
                };
                if(scoped)controller.Router.Flush(a.m_uid);else controller.Router.Flush();
                Check("old snapshot leaves a reloaded object's new queue intact",ZRoutedRpc.instance.Local.Count==1 && ZRoutedRpc.instance.Remote.Count==0 && failures==0);
                ready.Add(b.m_uid); controller.Router.Flush(b.m_uid);
                Check("reloaded object's new request is delivered once",ZRoutedRpc.instance.Remote.Count==1 && failures==0);

                Fresh(Kinds.Doors); Peer(1); ready.Clear();
                a=Object(typeof(Door),1,1,1); Tick(0); Tick(2); ZDOMan.instance.MarkSent(1,a);
                Check("owner-change first action waits",controller.Router.TryHandle(ZRoutedRpc.instance,Request(a,"UseDoor",1),false));
                Check("owner-change second action waits",controller.Router.TryHandle(ZRoutedRpc.instance,Request(a,"UseDoor",1),false));
                a.SetOwner(99); ZRoutedRpc.instance.OnHandle=delegate { a.SetOwner(1); };
                if(scoped)controller.Router.Flush(a.m_uid);else controller.Router.Flush();
                Check("native owner change stops same-batch delivery",ZRoutedRpc.instance.Local.Count==1 && ZRoutedRpc.instance.Remote.Count==0);
                ZDOMan.instance.MarkSent(1,a); ready.Add(a.m_uid); controller.Router.Flush(a.m_uid);
                Check("remaining action follows the newly ready owner",ZRoutedRpc.instance.Local.Count==1 && ZRoutedRpc.instance.Remote.Count==1 && failures==0);
            }
            Console.WriteLine("PASS: 100-object reply fixture: 100 recipient checks instead of 10000; unchanged full fallback and delivery safeguards.");
        }
        finally { RequestRouter.RecipientReady=null; }
    }
    private static void RecipientScopeController()
    {
        var ready=new HashSet<ZDOID>(); int recipientChecks=0;
        AuthorityController.ReadinessRemoved=delegate(ZDO data) {ready.Remove(data.m_uid);};
        AuthorityController.ReadinessReset=delegate {ready.Clear();};
        RequestRouter.RecipientReady=delegate(ZDOMan world,long owner,ZDO data) {
            uint state;
            if(!controller.TryRecipientState(data,owner,out state))return true;
            recipientChecks++;return ready.Contains(data.m_uid);
        };
        try
        {
            Fresh(Kinds.Doors,defaultInteractives:true);Peer(1);recipientChecks=0;uint minimum;
            ZDO door=Object(typeof(Door),1,1,1);Tick(0);Tick(2);ZDOMan.instance.MarkSent(1,door);
            for(int i=0;i<100;i++)Check("unchanged ownership retains native interaction fast path",!controller.Router.TryHandle(ZRoutedRpc.instance,Request(door,"UseDoor",1),false));
            Check("100 stable interactions add no readiness requirement or recipient checks",!controller.TryRecipientState(door,1,out minimum)&&recipientChecks==0&&ZRoutedRpc.instance.Remote.Count==0);
            Tick(3);Check("no-op grid completion does not arm readiness",!controller.TryRecipientState(door,1,out minimum));

            Fresh(Kinds.Doors,defaultInteractives:true,directional:true);Peer(1);recipientChecks=0;
            var frequent=new List<ZDO>();
            for(int i=0;i<100;i++)frequent.Add(Object(typeof(Door),1,1,99));
            Tick(0);Tick(2);
            for(int cycle=1;cycle<10;cycle++)
            {
                foreach(ZDO data in frequent){data.SetOwner(99);controller.OwnerChanged(data);}
                Tick(cycle+2);
            }
            foreach(ZDO data in frequent)Check("frequent no-interaction writes retain inline readiness for the intended owner",data.GetOwner()==1&&data.Writes==19&&controller.TryRecipientState(data,1,out minimum));
            Check("1000 transfers without interactions never query recipient readiness",recipientChecks==0&&ZRoutedRpc.instance.Remote.Count==0);

            Fresh(Kinds.Doors,defaultInteractives:true);Peer(1);recipientChecks=0;
            door=Object(typeof(Door),1,1,99);door.DataRevision=55;Tick(0);Tick(2);
            Check("real server-to-client write arms inline recipient state",door.GetOwner()==1&&controller.TryRecipientState(door,1,out minimum)&&minimum==55);
            ZDOMan.instance.MarkSent(1,door);
            Check("post-write interaction waits for actual recipient readiness",controller.Router.TryHandle(ZRoutedRpc.instance,Request(door,"UseDoor",1),false)&&ZRoutedRpc.instance.Remote.Count==0&&recipientChecks>0);
            ready.Add(door.m_uid);controller.Router.Flush(door.m_uid);
            Check("ready post-write request executes once",ZRoutedRpc.instance.Remote.Count==1);
            uint savedMinimum=minimum;
            Tick(3);Check("already-owned grid result preserves readiness state",controller.TryRecipientState(door,1,out minimum)&&minimum==savedMinimum);
            var view=ZNetScene.instance.FindInstance(door);controller.Unregister(view);
            Check("unload forgets recipient scope",!controller.TryRecipientState(door,1,out minimum));
            controller.RecordRecipient(door,1,door.OwnerRevision,999);
            Check("late source state cannot recreate an unloaded record",!controller.TryRecipientState(door,1,out minimum));

            foreach(bool reverse in new[]{false,true})
            {
                Fresh(Kinds.Doors);Peer(1);Peer(7,64);recipientChecks=0;
                FiresGhettoNetworkMod.FgnVersionGate.Match=true;
                door=Object(typeof(Door));Tick(0);Tick(2);Check("source transfer actually pending",controller.TransferPending(door)&&!controller.TryRecipientState(door,1,out minimum));
                Import(door,1,reverse);
                Check("complete cooperative receipt arms inline scope before next policy tick",controller.TryRecipientState(door,1,out minimum));
                controller.RecordRecipient(door,1,door.OwnerRevision,100);
                controller.RecordRecipient(door,1,door.OwnerRevision,80);
                controller.RecordRecipient(door,7,(uint)door.OwnerRevision-1U,999);
                Check("old source revision cannot replace current recipient readiness",controller.TryRecipientState(door,1,out minimum)&&minimum==100);
                door.DataRevision=70;
                controller.RequireRecipient(controller.RequestNode(door.m_uid,"UseDoor".GetStableHashCode()));
                Check("native source floor survives later lower snapshot",controller.TryRecipientState(door,1,out minimum)&&minimum==100);
                ZDOMan.instance.MarkSent(1,door);
                Check("immediate interaction cannot outrun post-receipt readiness",controller.Router.TryHandle(ZRoutedRpc.instance,Request(door,"UseDoor",1),false)&&ZRoutedRpc.instance.Remote.Count==0);
                ready.Add(door.m_uid);controller.Router.Flush(door.m_uid);
                Check("both import orders deliver exactly once",ZRoutedRpc.instance.Remote.Count==1);
            }
            Fresh(Kinds.Containers);Peer(1);Peer(7,64);recipientChecks=0;
            ZDO chest=Object(typeof(Container));view=ZNetScene.instance.FindInstance(chest);
            Tick(0);Tick(2);Import(chest,99,false);
            Check("native inventory acquisition on server needs no remote readiness",!controller.TryRecipientState(chest,99,out minimum));
            controller.InventoryResponse(view.GetComponent<Container>(),7,true);Tick(3);
            Check("final native inventory client assignment still arms inline readiness",chest.GetOwner()==1&&controller.TryRecipientState(chest,1,out minimum));

            Fresh(Kinds.Doors);Peer(1);recipientChecks=0;
            door=Object(typeof(Door),1,1,1);Tick(0);Tick(2);ZDOMan.instance.MarkSent(1,door);
            controller.Router.Forwarded(Request(door,"UseDoor",1));
            Check("a receiver failure arms readiness even without TML transfer",controller.TryRecipientState(door,1,out minimum)&&recipientChecks>0&&ZRoutedRpc.instance.Remote.Count==0);
            ready.Add(door.m_uid);controller.Router.Flush(door.m_uid);
            Check("receiver failure recovery retains one delivery",ZRoutedRpc.instance.Remote.Count==1);
            controller.Reset();Check("world reset removes inline scope records",!controller.TryRecipientState(door,1,out minimum));
            Console.WriteLine("PASS: 100 unchanged-owner interactions and 1000 no-interaction transfers perform zero additional recipient-readiness checks; real writes, both source receipt orders, inventory assignments and receiver failures retain readiness gates.");
        }
        finally {
            RequestRouter.RecipientReady=null;
            AuthorityController.ReadinessRemoved=null;AuthorityController.ReadinessReset=null;
        }
    }
    private static void FractionalCellGeometry()
    {
        foreach (float size in new[] { 4f, 6.4f, 8f, 16f })
        {
            Fresh(Kinds.Doors,defaultInteractives:true,cellSize:size,pattern:GridCoveragePattern.Rounded7x7);
            Peer(1);Tick(0);var door=Object(typeof(Door),1,1,99);Tick(2);
            var nodes=(Dictionary<ZDOID,AuthorityController.NativeNode>)typeof(AuthorityController).GetField("nodes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
            var node=nodes[door.m_uid];int writes=door.Writes;
            foreach(float x in new[]{-96.01f,-96f,-32.01f,-32f,-25.6001f,-25.6f,-25.5999f,-6.4f,0f,6.4f,12.8f,31.99f,32f,96f})
            {
                door.Position=new Vector3(x,0,1);controller.Position(door);Cell expected;
                Check("position event keeps fractional node index consistent",Cell.At(x,1,size,out expected)&&node.Cell.Equals(expected));
                Check("cached native zone follows actual cell changes",node.NativeZone.Equals(Cell.Zone(expected,size)));
                foreach(float sample in new[]{x-.01f,x,x+.01f,x-size,x+size})
                {
                    Cell other;bool inside=Cell.At(sample,1,size,out other)&&other.Equals(expected);
                    Check("cached precise bounds agree with fractional cell assignment",node.ContainsPosition(new Vector3(sample,0,1))==inside);
                }
                controller.Position(door);
                Check("unchanged position events do not cause owner writes",door.Writes==writes);
            }
            controller.Unregister(ZNetScene.instance.FindInstance(door));
            Check("unload removes the node holding cached cell geometry",nodes.Count==0&&node.Removed);
            controller.Reset();Check("world reset keeps the geometry cache empty",nodes.Count==0);
        }
    }
    private static void OwnerStatusSnapshot()
    {
        Fresh(Kinds.Doors|Kinds.Containers,defaultInteractives:true);var player=Peer(1);Peer(7,64);
        var door=Object(typeof(Door),20,1,7);var chest=Object(typeof(Container),1,1,7);
        var privateChest=Object(typeof(Container),21,1,7);ZNetScene.instance.FindInstance(privateChest).GetComponent<Container>().m_privacy=Container.PrivacySetting.Private;
        var unowned=Object(typeof(Door),24,1,0);var unloaded=Object(typeof(Door),28,1,99);
        var view=ZNetScene.instance.FindInstance(unloaded);controller.Unregister(view);ZNetScene.instance.Views.Remove(unloaded);
        ZNetScene.instance.Ready=delegate{return false;};Tick(0);
        int writes=door.Writes+chest.Writes+privateChest.Writes+unowned.Writes+unloaded.Writes,forced=ZDOMan.instance.Forced,area=ZNetScene.instance.AreaQueries,updates=controller.FieldUpdates;
        string status=controller.OwnerStatus(1);
        Check("snapshot reports actual server mode and blocking readiness",status.Contains("active=True")&&status.Contains("defaultInteractives=True")&&status.Contains("serverAreaReady=False"));
        Check("snapshot includes unloaded native data and distinguishes initial eligibility",status.Contains("initialEligible=4")&&status.Contains("liveOnly=1")&&status.Contains("serverOwned=1")&&status.Contains("playerOwned=3")&&status.Contains("unowned=1")&&status.Contains("withoutNode=1"));
        Check("snapshot shows current owner and field decision separately",status.Contains("owner=7; activePeer=True")&&status.Contains("candidate=")&&status.Contains("no server node"));
        Check("snapshot reports instance and private-container eligibility without invoking handoffs",status.Contains("identity=True; controllable=False")&&status.Contains("controllable=True"));
        Check("snapshot does not transfer, send RPCs/data, poll fields or alter use",writes==door.Writes+chest.Writes+privateChest.Writes+unowned.Writes+unloaded.Writes&&forced==ZDOMan.instance.Forced&&player.m_rpc.Calls==0&&controller.FieldUpdates==updates);
        Check("snapshot performs only its one explicit native readiness query",ZNetScene.instance.AreaQueries==area+1&&status.Length<6000);
        controller.Reset();Check("snapshot safely reports world teardown",controller.OwnerStatus(1).Contains("No initialized server world"));
    }
    public static void Main()
    {
        try {
        Fields();Inventories();StationsAndActors();ExperimentalProtectionDisabled();CooperativeAndRouting();BakedAndCompatibility();OwnershipOptions();TamedDefaultProtection();LoginGates();GridSettings();ExitRetention();NativeExitRelease();NativeDefaultAssignments();DirectionalController();FieldCacheController();DestroyedViewCleanup();QuarterSecondCadence();ForwardController();
        FractionalCellGeometry();DistantInteractiveOwner();LateGridCapability();TeleportFootprint();SoloRegions();SummonedCompanions();FriendlyMagicCreatures();CameraPublication();CameraFastCadence();CameraDirectedController();CameraOverlapController();InnerOnlyDepartureController();IdentifiedInteractionDelivery();ScopedInteractionDelivery();RecipientScopeController();
        SeparateMobDefaults();DelayedReadinessInteractions();OwnerStatusSnapshot();
#if GRID_PROTOCOL
        DestinationLoadProtocol();
#endif
        Console.WriteLine("PASS: "+checks+" full-controller/grid/router fixture assertions; no PUP/PupPatch plugin in fixture.");
        Console.WriteLine("LIMIT: simulated transport and Unity registry; no live multiplayer test.");
        } catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
}
