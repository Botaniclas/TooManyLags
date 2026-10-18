using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Configuration;
using TooManyLags;
using UnityEngine;

internal static partial class FgnCombatTests
{
    private static int count;
    private static readonly Type service=typeof(FgnCombatOwnership);
    private static readonly BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
    private static ZDO mob; private static MonsterAI ai; private static ZNetPeer old,target,other; private static Player targetPlayer;
    private static void Assert(bool condition,string name){count++;if(!condition)throw new Exception(name);}
    private static object Call(string name,params object[] args){return service.GetMethod(name,flags).Invoke(null,args);}
    private static bool CancelFixtureStop(){return false;}
    private static void PreferredPolicyPostfix(){}
    private static void Set(string name,object value){service.GetField(name,flags).SetValue(null,value);}
    private static void Ack()
    {var map=(System.Collections.IDictionary)service.GetField("watching",flags).GetValue(null);object watch=map[mob.m_uid];watch.GetType().GetField("Acknowledged",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(watch,true);}
    private static void Setup()
    {
        ZRpc.LegacySend=FgnMobHandoff.Reset; FgnBossPolicy.Configure(false,30f);
        Set("defaultMobs",new ConfigEntry<bool>(false));Set("serverDefaultsConfigured",false);
        Time.AutoAdvance=0;Call("Reset"); FiresGhettoNetworkMod.CreatureOwnership.Clear();
        ZNet.instance=new ZNet();ZDOMan.instance=new ZDOMan();ZNetScene.instance=new ZNetScene();Time.realtimeSinceStartupAsDouble=20;
        Character.All.Clear();Player.m_localPlayer=null;
        ZDOMan.Session=100;old=Peer(1,50,new Vector3(102,0,100));target=Peer(2,150,new Vector3(100,0,100));other=Peer(3,40,new Vector3(106,0,100));
        FiresGhettoNetworkMod.CreatureOwnership.Candidates(ZNet.instance.Peers);
        mob=new ZDO(new ZDOID(99,1),1){Position=new Vector3(100,0,100)};ZDOMan.instance.Data.Add(mob.m_uid,mob);
        GameObject obj=new GameObject();ZNetView view=obj.Add(new ZNetView{Data=mob});obj.Add(new Humanoid());ai=obj.Add(new MonsterAI());ai.SetView(view);obj.Add(new ZSyncTransform());ZNetScene.instance.Views.Add(mob,view);
        targetPlayer=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(target.m_characterID)).GetComponent<Player>();ai.Target=targetPlayer;
        FgnCombatOwnership.Fgn=new FgnCombatBridge(typeof(FiresGhettoNetworkMod.CreatureOwnership).Assembly);FgnCombatOwnership.Enabled=true;
        Set("maximumPing",new ConfigEntry<int>(200));Set("nearbyRadius",new ConfigEntry<float>(6));
        FiresGhettoNetworkMod.CreatureOwnership.Enabled=true;FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive=true;
        NativePup.Ready=NativePup.Available=NativePup.DestinationReady=true;NativePup.Checks=0;NativePup.Destination=mob.GetPosition();NativePup.OnReady=null;Plugin.Native=new NativePup();
        object[] args={false};Call("BalancePrefix",args);Assert((bool)args[0],"existing FGN pass becomes due");
        ZNet.instance.ServerPeer=new ZNetPeer{m_uid=100,m_server=true};
        ZNet.instance.Dedicated=false;Set("serverConfirmed",true);
        foreach(ZNetPeer p in ZNet.instance.Peers){ZDOMan.Session=p.m_uid;Call("PublishCapability",p.m_characterID);}ZDOMan.Session=100;ZNet.instance.Dedicated=true;Set("serverConfirmed",false);
        Call("MarkWatch",mob);
        // Simulate the authoritative client reporting through its normal AI hook.
        ZDOMan.Session=1;ZNet.instance.Dedicated=false;Set("serverConfirmed",true);FgnCombatOwnership.Observe(ai);ZDOMan.Session=100;ZNet.instance.Dedicated=true;Set("serverConfirmed",false);Ack();
    }
    private static ZNetPeer Peer(long id,float ping,Vector3 point)
    {
        ZNetPeer p=new ZNetPeer{m_uid=id,m_characterID=new ZDOID(id,10),Ping=ping};ZNet.instance.Peers.Add(p);
        ZDO data=new ZDO(p.m_characterID,id){Position=point};ZDOMan.instance.Data.Add(data.m_uid,data);
        GameObject obj=new GameObject();ZNetView view=obj.Add(new ZNetView{Data=data});obj.Add(new Player());ZNetScene.instance.Views.Add(data,view);return p;
    }
    private static bool Handle(out bool moved)
    {object[] args={mob,Time.realtimeSinceStartupAsDouble,false};bool original=(bool)Call("TryPrefix",args);moved=(bool)args[2];if(moved)Call("TryPostfix",true);return original;}
    private static void ServerAccept()
    {mob.SetOwner(100);Call("MarkWatch",mob);FgnCombatOwnership.Observe(ai);Time.realtimeSinceStartupAsDouble+=6;object[] args={false};Call("BalancePrefix",args);}
    public static void Main()
    {
        try { Run(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode=1; }
    }
    private static void Run()
    {
        bool moved;ZDOID id;Setup();
        Assert(FgnCombatOwnership.ReadTarget(mob,out id)&&id==target.m_characterID,"actual target recorded");
        int writes=mob.Writes;ZDOMan.Session=1;for(int i=0;i<10000;i++)FgnCombatOwnership.Observe(ai);ZDOMan.Session=100;
        Assert(mob.Writes==writes,"stable AI updates do not rewrite/transmit ZDO data");
        Assert(ZDOExtraData.Session.Count==5,"target and capability fields excluded from saved world");
        Assert(Handle(out moved)&&!moved,"normal RTT uses original FGN path");
        mob.SetOwner(3);Assert(!FgnCombatOwnership.ReadTarget(mob,out id),"old-owner report invalid after revision change");
        Assert(!Handle(out moved)&&!moved,"missing new-owner report waits without damage fallback");
        ZDOMan.Session=3;FgnCombatOwnership.Observe(ai);ZDOMan.Session=100;Assert(FgnCombatOwnership.ReadTarget(mob,out id),"new owner renews same target once");

        Setup();target.Ping=400;Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==100,"high RTT with close other player requests server");
        Call("NativeSetOwner",mob,3L);Assert(mob.GetOwner()==1,"pending old-owner handoff not raced by native reassignment");
        Time.realtimeSinceStartupAsDouble+=5;Call("NativeSetOwner",mob,3L);Assert(mob.GetOwner()==3,"pending protection expires with FGN timeout");
        Setup();target.Ping=400;Handle(out moved);old.Ready=false;Call("NativeSetOwner",mob,3L);Assert(mob.GetOwner()==3,"disconnected/unready old owner never pinned");
        Setup();target.Ping=400;Handle(out moved);ZDOMan.instance.GetZDO(old.m_characterID).Set("toomanylags.fgn.ai.protocol".GetStableHashCode(),0);
        Assert(!FgnCombatOwnership.ShouldOverride(mob.m_uid),"pending lease cannot override a revoked current-owner capability");
        Assert(Handle(out moved)&&!moved,"revoked pending owner immediately returns to original FGN selection");
        Call("NativeSetOwner",mob,3L);Assert(mob.GetOwner()==3,"revoked pending source no longer pins native ownership");
        Setup();target.Ping=400;Handle(out moved);mob.SetOwner(3);ZDOMan.instance.GetZDO(other.m_characterID).Set("toomanylags.fgn.ai.protocol".GetStableHashCode(),0);
        Assert(!FgnCombatOwnership.ShouldOverride(mob.m_uid),"pending lease cannot inherit a different unparticipating client owner");
        Setup();target.Ping=400;FiresGhettoNetworkMod.CreatureOwnership.Streak(mob.m_uid);Handle(out moved);
        Assert(!FiresGhettoNetworkMod.CreatureOwnership.HaveStreak(mob.m_uid),"custom request resets stock lower-ping streak");
        FiresGhettoNetworkMod.CreatureOwnership.Decline(mob.m_uid);Call("NativeSetOwner",mob,3L);Assert(mob.GetOwner()==3,"rejected pending request immediately releases native writes");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();Call("NativeSetOwner",mob,3L);Assert(mob.GetOwner()==100,"active server lease survives vanilla player-area ownership");
        Assert(!Handle(out moved)&&!moved,"server retains high-ping target while another player close");
        Assert(FgnCombatOwnership.ShouldOverride(mob.m_uid),"confirmed server lease needs no client-owner capability");
        ZDOMan.instance.GetZDO(other.m_characterID).Position=new Vector3(107,0,100);
        ZDOMan.instance.GetZDO(old.m_characterID).Position=new Vector3(107,0,100);
        Assert(!Handle(out moved)&&moved&&mob.GetOwner()==2,"last nearby second player leaves: returns to high-ping AI target");
        ZNetView mobView=ZNetScene.instance.FindInstance(mob);HitData hit=new HitData();
        Assert(!(bool)Call("DamagePrefix",mobView.GetComponent<Character>(),hit)&&mobView.Forwarded==1&&mobView.ForwardOwner==2&&ReferenceEquals(mobView.ForwardHit,hit),"late server hit forwarded exactly once to confirmed return owner");
        Time.realtimeSinceStartupAsDouble+=5.1;Assert((bool)Call("DamagePrefix",mobView.GetComponent<Character>(),hit)&&mobView.Forwarded==1,"late forwarding expires");

        Setup();target.Ping=400;ZDOMan.instance.GetZDO(old.m_characterID).Position=new Vector3(120,0,100);ZDOMan.instance.GetZDO(other.m_characterID).Position=new Vector3(120,0,100);
        Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==2,"no close other player: high-ping target receives owner, not previous attacker");
        Setup();target.Ping=400;FiresGhettoNetworkMod.CreatureOwnership.Candidates(new[]{target});
        Assert(Handle(out moved)&&!moved,"one original FGN candidate does not arm AI target ownership at all");
        Setup();target.Ping=400;ZDOMan.instance.GetZDO(old.m_characterID).Position=new Vector3(120,0,100);other.Ref=new Vector3(230,0,100);
        Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==2,"close actual player outside original 3x3 sector coverage cannot trigger server fallback");
        Setup();target.Ping=400;ZDOMan.instance.GetZDO(old.m_characterID).Position=new Vector3(120,0,100);other.Ref=new Vector3(191,0,100);
        Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==100,"outer included 3x3 sector plus exact six-metre boundary remains eligible");
        Setup();target.Ping=400;ZDOMan.instance.GetZDO(old.m_characterID).Position=new Vector3(120,0,100);ZDOMan.instance.GetZDO(other.m_characterID).Position=new Vector3(106.01f,0,100);
        Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==2,"outside six-metre boundary returns high-ping target");
        Setup();target.Ping=400;Set("nearbyRadius",new ConfigEntry<float>(1));ZDOMan.instance.GetZDO(old.m_characterID).Position=new Vector3(120,0,100);
        Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==2,"configured proximity used");
        Setup();target.Ping=400;FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive=false;
        Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==2,"without SSS the high-ping target receives owner");
        Setup();target.Ping=400;NativePup.Ready=false;
        Assert(!Handle(out moved)&&!moved&&old.m_rpc.Sent==0,"pending collision denies server takeover");
        Setup();target.Ping=400;NativePup.DestinationReady=false;
        Assert(!Handle(out moved)&&!moved,"pending canonical destination denies takeover");
        Setup();target.Ping=400;ZNetScene.instance.Ready=false;
        Assert(!Handle(out moved)&&!moved,"unloaded area denies takeover");
        Setup();target.Ping=400;target.Measured=false;
        Assert(Handle(out moved)&&!moved,"unknown RTT not misclassified as measured high RTT");
        Setup();target.Ping=200;Assert(Handle(out moved),"exact ping boundary stays stock FGN");
        Setup();Set("maximumPing",new ConfigEntry<int>(0));target.Ping=301;Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==100,"relative 250 ms mode supported");
        Setup();Set("maximumPing",new ConfigEntry<int>(0));target.Ping=290;Assert(Handle(out moved),"relative boundary preserved");
        Setup();target.Matched=false;Assert(!Handle(out moved)&&!moved,"mismatched target rejected");
        Setup();target.Ping=400;old.Matched=false;Assert(Handle(out moved)&&!moved,"FGN compatibility gate preserved for old owner");
        Setup();target.Ping=400;mob.Set(ZDOVars.s_tamed,1);Assert(!Handle(out moved)&&!moved,"tamed excluded");
        Setup();target.Ping=400;mob.Set(ZDOVars.s_user,3);Assert(!Handle(out moved)&&!moved,"controlled excluded");
        Setup();target.Ping=400;mob.Held=true;Assert(!Handle(out moved)&&!moved,"FGN hold respected");
        Setup();target.Ping=400;ZNetScene.instance.FindInstance(mob).GetComponent<Character>().Boss=true;Assert(!Handle(out moved)&&moved,"boss uses normal high-ping exception with special rule disabled");
        Setup();target.Ping=400;FgnCombatOwnership.Fgn.PerOwner[1]=6;Assert(!Handle(out moved)&&!moved,"six handoffs per owner cap preserved");
        Setup();target.Ping=400;Set("passMoves",16);Assert(!Handle(out moved)&&!moved,"sixteen handoffs per pass cap preserved");
        Setup();target.Ping=400;FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]=30;Assert(!Handle(out moved)&&!moved,"FGN declined cooldown preserved");
        Setup();target.Ping=400;FgnCombatOwnership.Fgn.MarkPending(mob.m_uid,2,Time.realtimeSinceStartupAsDouble);Assert(!Handle(out moved)&&!moved,"FGN pending window preserved");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();target.Ping=100;ZNetScene.instance.FindInstance(mob).GetComponent<Character>().Attack=true;
        Assert(!Handle(out moved)&&!moved&&mob.GetOwner()==100,"return waits for ongoing server attack");
        ZNetScene.instance.FindInstance(mob).GetComponent<Character>().Attack=false;Assert(!Handle(out moved)&&moved&&mob.GetOwner()==2,"return after attack keeps target");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();target.Ref=new Vector3(1000,0,1000);
        Assert(!Handle(out moved)&&moved&&mob.GetOwner()==3,"target leaves active area: temporary server lease returns to eligible player");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();ZNet.instance.Peers.Remove(old);ZNet.instance.Peers.Remove(other);
        FiresGhettoNetworkMod.CreatureOwnership.Candidates(ZNet.instance.Peers);
        Call("BalancePostfix",true);Assert(mob.GetOwner()==2,"solo player receives lease return outside FGN two-player scan");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();ZNetScene.instance.Views.Remove(mob);Call("BalancePostfix",true);Call("NativeSetOwner",mob,2L);
        Assert(mob.GetOwner()==2,"unloaded actor lease removed; no permanent pin");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();ZNet.instance.Peers.Clear();ai.Target=null;FgnCombatOwnership.Observe(ai);Call("BalancePostfix",true);
        Assert(mob.GetOwner()==0,"no players: native unowned state restored");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();FiresGhettoNetworkMod.CreatureOwnership.TrackOwned(mob.m_uid,1,0);ZNet.instance.Peers.Clear();ai.Target=null;FgnCombatOwnership.Observe(ai);Call("BalancePostfix",true);
        Assert(mob.GetOwner()==0&&Double.IsNaN(FiresGhettoNetworkMod.CreatureOwnership.OwnedSince(mob.m_uid)),"no-player server release clears only committed stale ownership hold");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();FiresGhettoNetworkMod.CreatureOwnership.TrackOwned(mob.m_uid,1,0);old.Ref=target.Ref=other.Ref=new Vector3(1000,0,1000);
        Assert(!Handle(out moved)&&mob.GetOwner()==0&&Double.IsNaN(FiresGhettoNetworkMod.CreatureOwnership.OwnedSince(mob.m_uid)),"ineligible-target server release clears stale hold");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();FiresGhettoNetworkMod.CreatureOwnership.TrackOwned(mob.m_uid,1,0);ZDOID unrelated=new ZDOID(88,9);FiresGhettoNetworkMod.CreatureOwnership.TrackOwned(unrelated,3,11);
        FiresGhettoNetworkMod.CreatureOwnership.Enabled=false;object[] released={false};Call("BalancePrefix",released);
        Assert(mob.GetOwner()==0&&Double.IsNaN(FiresGhettoNetworkMod.CreatureOwnership.OwnedSince(mob.m_uid)),"disabled-balancing committed server release clears stale hold");
        Assert(FiresGhettoNetworkMod.CreatureOwnership.OwnedSince(unrelated)==11,"server release never clears unrelated native ownership records");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();FiresGhettoNetworkMod.CreatureOwnership.Enabled=false;object[] due={false};Call("BalancePrefix",due);
        Assert(mob.GetOwner()==0,"FGN balancing disabled releases own server exception only");

        Setup();ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(old.m_characterID)).GetComponent<Player>();old.Ping=400;
        ZDOMan.Session=1;ZNet.instance.Dedicated=false;Set("serverConfirmed",true);FgnCombatOwnership.Observe(ai);ZDOMan.Session=100;ZNet.instance.Dedicated=true;
        FiresGhettoNetworkMod.CreatureOwnership.TrackOwned(mob.m_uid,1,0);Handle(out moved);
        Assert(moved&&old.m_rpc.Destination==100&&FiresGhettoNetworkMod.CreatureOwnership.OwnedSince(mob.m_uid)==0,"requested client handoff retains original hold until actual server return");
        ServerAccept();ZDOMan.instance.GetZDO(target.m_characterID).Position=new Vector3(120,0,100);ZDOMan.instance.GetZDO(other.m_characterID).Position=new Vector3(120,0,100);
        Assert(!Handle(out moved)&&moved&&mob.GetOwner()==1&&Double.IsNaN(FiresGhettoNetworkMod.CreatureOwnership.OwnedSince(mob.m_uid)),"confirmed same-player server return clears stale FGN hold timestamp");
        Time.realtimeSinceStartupAsDouble+=2;
        FgnCombatOwnership.Fgn.TryHandOff.Invoke(null,new object[]{mob,Time.realtimeSinceStartupAsDouble});
        Assert(FiresGhettoNetworkMod.CreatureOwnership.OwnedSince(mob.m_uid)==Time.realtimeSinceStartupAsDouble,"next native FGN pass starts a fresh hold after same-player return");

        // Execute the production transpilers against independent foreign private stamp types.
        Setup();FiresGhettoNetworkMod.CreatureOwnership.Hit(mob.m_uid,3);Harmony h=new Harmony("combat.fixture");
        h.Patch(FgnCombatOwnership.Fgn.TryHandOff,transpiler:new HarmonyMethod(service.GetMethod("RewriteTarget",flags)));
        Assert((bool)FgnCombatOwnership.Fgn.TryHandOff.Invoke(null,new object[]{mob,20.0})&&old.m_rpc.Destination==2,"actual transpiled FGN uses AI target instead of last attacker");
        Assert(FiresGhettoNetworkMod.CreatureOwnership.RawHits==1,"original damage dictionary never overwritten");
        FgnCombatOwnership.Enabled=false;
        FiresGhettoNetworkMod.CreatureOwnership.ResetTransfers();
        Assert((bool)FgnCombatOwnership.Fgn.TryHandOff.Invoke(null,new object[]{mob,20.0})&&old.m_rpc.Destination==3,"disabled/faulted hook restores original FGN read");
        FgnCombatOwnership.Enabled=true;
        h.Patch(FgnCombatOwnership.Fgn.Veto,transpiler:new HarmonyMethod(service.GetMethod("RewriteVeto",flags)));
        Assert(FgnCombatOwnership.Fgn.Veto.Invoke(null,new object[]{mob,2L})==null,"damage-based combat veto removed with valid AI report");
        ZNetScene.instance.FindInstance(mob).GetComponent<Character>().Attack=true;
        Assert((string)FgnCombatOwnership.Fgn.Veto.Invoke(null,new object[]{mob,2L})=="attacking","veto retains later attack protection");
        FgnCombatOwnership.Enabled=false;Assert((string)FgnCombatOwnership.Fgn.Veto.Invoke(null,new object[]{mob,2L})=="owner fighting","disabled hook restores original damage veto");
        h.UnpatchSelf();
        Setup();FgnCombatOwnership.Enabled=false;writes=mob.Writes;FgnCombatOwnership.Observe(ai);Assert(mob.Writes==writes,"disabled observer writes nothing");
        Setup();ZDOMan.instance.GetZDO(old.m_characterID).Set("toomanylags.fgn.ai.protocol".GetStableHashCode(),0);
        Assert(!FgnCombatOwnership.ShouldOverride(mob.m_uid)&&Handle(out moved),"missing/disabled owner capability retains original FGN behavior");
        Setup();ZNet.instance.Dedicated=false;ZDOMan.Session=1;FiresGhettoNetworkMod.CreatureOwnership.Hit(mob.m_uid,3);
        Assert(!FgnCombatOwnership.ShouldOverride(mob.m_uid),"client alone cannot override unconfirmed server mode");
        MethodInfo localHit=service.GetMethod("LocalHit",flags);Dictionary<ZDOID,double> hits=new Dictionary<ZDOID,double>();hits[mob.m_uid]=20;object[] hitArgs={hits,mob.m_uid,0.0};
        Assert((bool)localHit.Invoke(null,hitArgs),"server mode OFF keeps original client damage veto");
        ZNetPeer server=new ZNetPeer{m_uid=100,m_server=true};ZNet.instance.ServerPeer=server;ZNet.instance.LocalPlayerCharacterID=old.m_characterID;Call("SessionConnection",server);
        server.m_rpc.Receive(4);Assert(!FgnCombatOwnership.ShouldOverride(mob.m_uid),"old server without attack-ready protocol retains stock FGN behavior");
        server.m_rpc.Receive(7);Call("ReceiveWatch",server,mob.m_uid,1L,(long)mob.OwnerRevision);
        Assert(FgnCombatOwnership.ShouldOverride(mob.m_uid),"authoritative new-server mode and renewed watch permit override");
        Call("Fail",new Exception("fixture"));Assert(ZDOMan.instance.GetZDO(old.m_characterID).GetLong("toomanylags.fgn.ai.protocol".GetStableHashCode(),0)==0,"feature failure revokes owned player capability");
        Setup();Call("Fail",new Exception("server fixture"));Assert(!FgnCombatOwnership.Enabled&&old.m_rpc.LastMode==3&&target.m_rpc.LastMode==3&&other.m_rpc.LastMode==3,"server feature failure sends OFF to every ready client");
        Setup();other.Ready=false;FgnCombatOwnership.Stop();Assert(!FgnCombatOwnership.Enabled&&old.m_rpc.LastMode==3&&target.m_rpc.LastMode==3&&other.m_rpc.ModeSent==0,"live server Stop notifies ready clients and skips unready peers");
        int sent=old.m_rpc.ModeSent;FgnCombatOwnership.Stop();Assert(old.m_rpc.ModeSent==sent,"repeated inactive Stop sends no extra notifications");
        Setup();ZNet.instance.Dedicated=false;ZDOMan.Session=1;server=new ZNetPeer{m_uid=100,m_server=true};ZNet.instance.ServerPeer=server;ZNet.instance.LocalPlayerCharacterID=old.m_characterID;Call("SessionConnection",server);
        server.m_rpc.Receive(3);writes=mob.Writes;FgnCombatOwnership.Observe(ai);Assert(mob.Writes==writes,"server OFF acknowledgement stops client target reports");
        hitArgs=new object[]{hits,mob.m_uid,0.0};Assert((bool)localHit.Invoke(null,hitArgs),"server failure OFF notification restores original client damage veto");
        Setup();ZDOMan.instance=new ZDOMan();Call("NativeSetOwner",mob,3L);Assert(mob.GetOwner()==3,"old-world lease cannot pin new world");
        Setup();ai.Target=null;ai.Sleeping=true;mob.SetOwner(3);ZDOMan.Session=3;ZNet.instance.Dedicated=false;Set("serverConfirmed",true);
        // Initial watch after an owner change includes sleeping actors without a per-AI update hook.
        ZNet.instance.Dedicated=true;Call("MarkWatch",mob);ZNet.instance.Dedicated=false;Set("serverConfirmed",true);
        FgnCombatOwnership.Observe(ai);Assert(FgnCombatOwnership.ReadTarget(mob,out id)&&id.IsNone(),"initial watch publishes no-target report for sleeping new owner");
        Setup();FgnCombatOwnership.Stop();MethodInfo fgnTry=typeof(FiresGhettoNetworkMod.CreatureOwnership).GetMethod("TryHandOffCreature",flags);
        ConfigFile defaultConfig=new ConfigFile();FgnCombatOwnership.Install(defaultConfig,null,null);
        Assert((bool)defaultConfig.Defaults["EnableAIOwnership"]&&!FgnCombatOwnership.Enabled,"default ON cannot install without required FGN");FgnCombatOwnership.Stop();
        ConfigFile disabledConfig=new ConfigFile{EnableOverride=false};
        FgnCombatOwnership.Install(disabledConfig,typeof(FiresGhettoNetworkMod.CreatureOwnership).Assembly,new Version(1,5,36));
        Assert((bool)disabledConfig.Defaults["EnableAIOwnership"],"with FGN feature defaults ON while explicit OFF remains respected");
        Patches defaultPatches=Harmony.GetPatchInfo(fgnTry);
        Assert(!FgnCombatOwnership.Enabled&&(defaultPatches==null||defaultPatches.Owners.Count==0),"explicit OFF installs no FGN combat hooks");
        Patches resultPatches=Harmony.GetPatchInfo(typeof(FiresGhettoNetworkMod.CreatureOwnership).GetMethod("OnResult",flags));
        Assert(resultPatches==null||resultPatches.Owners.Count==0,"explicit OFF installs no attack-cooldown hooks");
        Call("SessionConnection",old);old.m_rpc.Receive(1);Assert(old.m_rpc.LastMode==3,"passive disabled-server endpoint confirms OFF");FgnCombatOwnership.Stop();
        FgnCombatOwnership.Install(new ConfigFile(),typeof(FiresGhettoNetworkMod.CreatureOwnership).Assembly,new Version(1,5,41),false);
        defaultPatches=Harmony.GetPatchInfo(fgnTry);
        Assert(!FgnCombatOwnership.Enabled&&FgnCombatOwnership.StartupError==null&&FgnCombatOwnership.InstallationStatus.Contains("another AI ownership policy")&&
            (defaultPatches==null||defaultPatches.Owners.Count==0),"duplicate policy quietly binds settings without FGN combat hooks or a startup error");
        Call("SessionConnection",old);old.m_rpc.Receive(1);Assert(old.m_rpc.LastMode==3,"duplicate policy passive endpoint confirms OFF");FgnCombatOwnership.Stop();
        Harmony preferredPolicy=new Harmony("botanic.pup.patch.fgn.aiownership");
        try
        {
            preferredPolicy.Patch(fgnTry,postfix:new HarmonyMethod(typeof(FgnCombatTests).GetMethod("PreferredPolicyPostfix",flags)));
            FgnCombatOwnership.Install(new ConfigFile(),typeof(FiresGhettoNetworkMod.CreatureOwnership).Assembly,new Version(1,5,41),false);
            defaultPatches=Harmony.GetPatchInfo(fgnTry);
            Assert(!FgnCombatOwnership.Enabled&&FgnCombatOwnership.StartupError==null&&defaultPatches.Owners.Count==1&&
                defaultPatches.Owners.Contains(preferredPolicy.Id),"preferred ownership policy remains the only active FGN handoff policy");
            Assert(!FgnCombatOwnership.ShouldOverride(mob.m_uid),"quietly inactive addon cannot override the preferred policy's target");
            FgnCombatOwnership.Stop();
            Assert(Harmony.GetPatchInfo(fgnTry).Owners.Contains(preferredPolicy.Id),"addon cleanup never removes preferred policy hooks");
        }
        finally{FgnCombatOwnership.Stop();preferredPolicy.UnpatchSelf();}
        EventTests();
        ClientBalanceTests();
        DelayedHandoffTests();
        AttackCooldownTests();
        TargetCooldownTests();
        DiagnosticTests();
        UpdateCompatibilityTests();
        AttackEndTests();
        NativeReadinessTests();
        FriendlyCreatureTests();
        DefaultMobTests();
        IdentifiedHandoffTests(); BossTargetTests();
        Console.WriteLine("PASS: "+count+" production FGN combat/target/lease/runtime-transpiler assertions; managed doubles, not live Unity/transport.");
    }

    private static void NoPlayerTarget()
    {
        ai.Target=null;ZDOMan.Session=1;ZNet.instance.Dedicated=false;Set("serverConfirmed",true);
        FgnCombatOwnership.Observe(ai);ZDOMan.Session=100;ZNet.instance.Dedicated=true;Set("serverConfirmed",false);
        Call("MarkWatch",mob);Ack();
    }
    private static void DefaultMobTests()
    {
        bool moved;
        Setup();Set("serverDefaultsConfigured",true);NoPlayerTarget();
        Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==100,"idle hostile default requests guarded server handoff");
        int sent=old.m_rpc.Sent, pendingArea=ZNetScene.instance.AreaCalls;Assert(!Handle(out moved)&&!moved&&old.m_rpc.Sent==sent,"pending idle handoff is not duplicated");
        Assert(ZNetScene.instance.AreaCalls==pendingArea,"pending handoff skips repeated loading proof");
        ServerAccept();int area=ZNetScene.instance.AreaCalls,collision=NativePup.Checks,writes=mob.OwnerWrites;
        for(int i=0;i<1000;i++)Assert(!Handle(out moved)&&!moved,"settled idle default remains server-owned");
        Assert(mob.GetOwner()==100&&mob.OwnerWrites==writes&&ZNetScene.instance.AreaCalls==area&&NativePup.Checks==collision,"settled defaults do no repeated collider proof or owner writes");
        ai.Target=targetPlayer;FgnCombatOwnership.Observe(ai);
        Assert(!Handle(out moved)&&moved&&mob.GetOwner()==2,"server default returns to actual player target");

        Setup();Set("serverDefaultsConfigured",true);NoPlayerTarget();ZNetScene.instance.Ready=false;
        Assert(!Handle(out moved)&&!moved&&old.m_rpc.Sent==0,"idle default preserves native loading readiness");
        ZNetScene.instance.Ready=true;Plugin.Native.Active=false;
        Assert(!Handle(out moved)&&!moved&&old.m_rpc.Sent==0,"idle default preserves invalid PUP block");
        Plugin.Native.Active=true;NativePup.DestinationReady=false;
        Assert(!Handle(out moved)&&!moved&&old.m_rpc.Sent==0,"idle default preserves cluster collision proof");

        Setup();Set("serverDefaultsConfigured",true);NoPlayerTarget();
        ZNet.instance.Peers.Remove(target);ZNet.instance.Peers.Remove(other);FiresGhettoNetworkMod.CreatureOwnership.Candidates(ZNet.instance.Peers);
        Assert((bool)Call("MarkWatch",mob),"solo idle hostile can be watched when defaults enabled");Ack();
        Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==100,"solo idle hostile gets server default");
        ServerAccept();ZNet.instance.Peers.Clear();FiresGhettoNetworkMod.CreatureOwnership.Candidates(ZNet.instance.Peers);
        Assert(!Handle(out moved)&&!moved&&mob.GetOwner()==0,"no active player allows native release instead of permanent server pin");

        Setup();Set("serverDefaultsConfigured",true);
        Assert(Handle(out moved)&&!moved&&old.m_rpc.Sent==0,"valid player target retains existing target-based path");
        NoPlayerTarget();FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive=false;
        Assert(Handle(out moved)&&!moved&&old.m_rpc.Sent==0,"server defaults ignored without Server-Side Simulation");

        foreach(int protection in new[]{0,1,2,3,4,5})
        {
            Setup();Set("serverDefaultsConfigured",true);NoPlayerTarget();var actor=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();
            if(protection==0)actor.Team=Character.Faction.Players;
            if(protection==1){actor.Tamed=true;mob.Set(ZDOVars.s_tamed,1L);}
            if(protection==2)actor.Boss=true;
            if(protection==3)actor.Riding=true;
            if(protection==4)mob.Held=true;
            if(protection==5)mob.Set(ZDOVars.s_user,1L);
            Handle(out moved);Assert(!moved&&old.m_rpc.Sent==0,"idle default retains exclusion "+protection);
        }

        Setup();Set("serverDefaultsConfigured",true);NoPlayerTarget();var view=ZNetScene.instance.FindInstance(mob);
        Call("DefaultLoadedPostfix",mob,view);var registry=(System.Collections.IDictionary)service.GetField("defaultLoaded",flags).GetValue(null);
        Assert(registry.Count==1,"optional registry tracks loaded actor once");Call("DefaultLoadedPostfix",mob,view);Assert(registry.Count==1,"duplicate add event has no extra record");
        Call("DefaultUnloadedPrefix",view);Assert(registry.Count==0&&!FgnCombatOwnership.ShouldOverride(mob.m_uid),"unload forgets default actor and watch");
        Call("DefaultLoadedPostfix",mob,view);ZNetScene.instance.Views.Remove(mob);Call("ProcessDefaults");
        Assert(registry.Count==0,"existing balance pass prunes missing scene instance");
        ZNetScene.instance.Views.Add(mob,view);Call("DefaultLoadedPostfix",mob,view);Call("ClearDefaultLoaded");Assert(registry.Count==0,"scene shutdown clears optional registry");

        Setup();view=ZNetScene.instance.FindInstance(mob);Call("DefaultLoadedPostfix",mob,view);
        registry=(System.Collections.IDictionary)service.GetField("defaultLoaded",flags).GetValue(null);Assert(registry.Count==0,"default off allocates no actor record");
        Set("serverDefaultsConfigured",true);ZDOMan.Session=1;ZNet.instance.Dedicated=false;Call("DefaultLoadedPostfix",mob,view);
        Assert(registry.Count==0,"client does not run server default tracking");
        Setup();FgnCombatOwnership.Stop();
    }
    private static void NativeReadinessTests()
    {
        Setup();Plugin.Native=null;
        Assert(!(bool)Call("ServerReady",mob)&&NativePup.Checks==0,"missing native bridge denies server ownership without collision work");
        Setup();Plugin.Native.Present=false;Plugin.Native.Active=false;
        Assert((bool)Call("ServerReady",mob)&&NativePup.Checks==0&&ZNetScene.instance.AreaCalls==2,"absent PUP requires native readiness at canonical and live poses without PUP work");
        Setup();Plugin.Native.Present=false;ZNetView absentView=ZNetScene.instance.FindInstance(mob);absentView.transform.position=mob.GetPosition();
        Assert((bool)Call("ServerReady",mob)&&ZNetScene.instance.AreaCalls==1,"absent PUP reuses native area proof for equal poses");
        Setup();Plugin.Native.Present=false;ZNetScene.instance.AreaReady=p=>p.Equals(mob.GetPosition());
        Assert(!(bool)Call("ServerReady",mob)&&NativePup.Checks==0,"absent PUP cannot bypass unloaded live-pose area");
        Setup();Plugin.Native.Present=false;ZNetScene.instance.FindInstance(mob).m_distant=true;
        Assert(!(bool)Call("ServerReady",mob)&&ZNetScene.instance.AreaCalls==0,"absent PUP still requires an actual active mob instance");
        Setup();Plugin.Native.Present=false;mob.Position=new Vector3(float.NaN,0,100);
        Assert(!(bool)Call("ServerReady",mob)&&ZNetScene.instance.AreaCalls==0,"absent PUP denies nonfinite canonical position before native sector queries");
        Setup();Plugin.Native.Present=false;ZNetScene.instance.FindInstance(mob).transform.position=new Vector3(float.PositiveInfinity,0,100);
        Assert(!(bool)Call("ServerReady",mob)&&ZNetScene.instance.AreaCalls==1,"absent PUP denies nonfinite live pose before its native sector query");
        Setup();Plugin.Native.Active=false;
        Assert(!(bool)Call("ServerReady",mob)&&NativePup.Checks==0,"inactive PUP cannot authorize server fallback");
        Setup();ZNetScene.instance.Ready=false;
        Assert(!(bool)Call("ServerReady",mob)&&NativePup.Checks==0,"unloaded native area denies fallback before readiness calls");
        Setup();ZNetView view=ZNetScene.instance.FindInstance(mob);view.transform.position=mob.GetPosition();
        Assert((bool)Call("ServerReady",mob)&&NativePup.Checks==1,"exact-equal live and canonical positions reuse one native collision proof");
        Setup();view=ZNetScene.instance.FindInstance(mob);view.transform.position=new Vector3(101,0,100);
        Assert((bool)Call("ServerReady",mob)&&NativePup.Checks==2,"different live and canonical positions both require readiness");
        Setup();NativePup.DestinationReady=false;
        Assert(!(bool)Call("ServerReady",mob)&&NativePup.Checks==1,"missing canonical collision stops before testing old live pose");
        Setup();NativePup.Ready=false;
        Assert(!(bool)Call("ServerReady",mob)&&NativePup.Checks==2,"ready destination cannot bypass missing live-pose collision");
        Setup();NativePup.OnReady=()=>mob.Position=new Vector3(200,0,100);
        Assert(!(bool)Call("ServerReady",mob),"readiness cannot certify a ZDO destination changed while preparing collision");
        Setup();NativePup.OnReady=()=>mob.Set(ZDOVars.s_tamed,1);
        Assert(!(bool)Call("ServerReady",mob),"readiness rechecks tame eligibility after preparing collision");
        Setup();FgnCombatOwnership.Enabled=false;bool moved;
        Assert(Handle(out moved)&&!moved&&NativePup.Checks==0,"disabled target policy leaves native FGN without readiness work");
        Setup();int before=NativePup.Checks;Harmony hooks=new Harmony("combat.native.readiness.fixture");
        try
        {
            hooks.Patch(FgnCombatOwnership.Fgn.TryHandOff,prefix:new HarmonyMethod(service.GetMethod("TryPrefix",flags)),
                postfix:new HarmonyMethod(service.GetMethod("TryPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteTarget",flags)));
            Assert(FgnCombatOwnership.Fgn.TryTransfer(mob,20)&&old.m_rpc.Destination==2&&NativePup.Checks==before,"ordinary player-target transfer adds no server-fallback collider queries");
        }
        finally{hooks.UnpatchSelf();}
    }

    private static void UpdateCompatibilityTests()
    {
        System.Threading.SynchronizationContext previous=System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(new UnitySynchronizationContext());
        try
        {
            foreach(Version version in new[]{new Version(1,5,36),new Version(1,5,41),new Version(99,0,1)})
            {
                Setup();FgnCombatOwnership.Stop();
                FgnCombatOwnership.Install(new ConfigFile(),typeof(FiresGhettoNetworkMod.CreatureOwnership).Assembly,version);
                Assert(FgnCombatOwnership.Enabled&&FgnCombatOwnership.InstallationStatus=="installed",
                    "full installation uses real contracts, not assembly hash or version "+version+": "+FgnCombatOwnership.InstallationStatus);
                FgnCombatOwnership.Stop();
            }
            Setup();FgnCombatOwnership.Stop();
            FgnCombatOwnership.Install(new ConfigFile{MobDefaultOverride=true},typeof(FiresGhettoNetworkMod.CreatureOwnership).Assembly,new Version(1,5,41));
            Assert(FgnCombatOwnership.Enabled&&FgnCombatOwnership.InstallationStatus=="installed","enabled defaults install their real optional scene lifecycle hooks");
            ((ConfigEntry<bool>)service.GetField("defaultMobs",flags).GetValue(null)).Value=false;
            Assert((bool)service.GetProperty("ServerDefaults",flags).GetValue(null,null),"saved restart-only default setting cannot change active session");
            var view=ZNetScene.instance.FindInstance(mob);ZNetScene.instance.AddInstance(mob,view);
            var loaded=(System.Collections.IDictionary)service.GetField("defaultLoaded",flags).GetValue(null);
            Assert(loaded.Count==1,"patched scene add tracks mob when enabled");view.ResetZDO();Assert(loaded.Count==0,"patched view reset removes mob before losing ZDO");
            FgnCombatOwnership.Stop();
            Setup();FgnCombatOwnership.Stop();
            FgnCombatOwnership.Install(new ConfigFile{EnableOverride=false},typeof(FiresGhettoNetworkMod.CreatureOwnership).Assembly,new Version(99,0,1));
            Assert(!FgnCombatOwnership.Enabled,"future compatible version respects explicit feature OFF");
            Call("SessionConnection",old);old.m_rpc.Receive(1);
            Assert(old.m_rpc.LastMode==3,"future-version passive endpoint confirms server OFF");
            FgnCombatOwnership.Stop();
        }
        finally{FgnCombatOwnership.Stop();System.Threading.SynchronizationContext.SetSynchronizationContext(previous);}

        MethodInfo constant=typeof(FgnCombatBridge).GetMethod("RequireConstant",flags);
        bool rejected=false;
        try{constant.Invoke(null,new object[]{typeof(FiresGhettoNetworkMod.CreatureOwnership),"PendingSeconds",10.0});}
        catch(TargetInvocationException error){rejected=error.InnerException is NotSupportedException;}
        Assert(rejected,"actual changed pending contract remains rejected independent of version");
        rejected=false;
        try{constant.Invoke(null,new object[]{typeof(FiresGhettoNetworkMod.CreatureOwnership),"MissingContract",5.0});}
        catch(TargetInvocationException error){rejected=error.InnerException is NotSupportedException;}
        Assert(rejected,"missing actual timing contract is rejected before hooks");
        Setup();bool moved;target.Ping=400;
        int possessed="valcast_possessed".GetStableHashCode();mob.Set(possessed,42L);
        Assert(!FgnCombatOwnership.ShouldOverride(mob.m_uid),"possessed watched client mob keeps native damage and ownership veto");
        Assert(!Handle(out moved)&&!moved&&old.m_rpc.Sent==0,"possessed client mob never enters high-ping server takeover");
        Assert(FgnCombatOwnership.DescribeMob(mob,ai).Contains("Valcast possessed"),"manual status explains possessed exclusion");
        mob.Set(possessed,0L);Call("MarkWatch",mob);
        Assert(!Handle(out moved)&&!moved,"released possession waits for renewed initial authoritative report");
        ZDOMan.Session=1;ZNet.instance.Dedicated=false;Set("serverConfirmed",true);FgnCombatOwnership.Observe(ai);
        ZDOMan.Session=100;ZNet.instance.Dedicated=true;Set("serverConfirmed",false);Ack();
        Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==100,"released possession resumes server exception after renewed owner report");

        Setup();target.Ping=400;Handle(out moved);ServerAccept();mob.Set(possessed,42L);target.Ping=100;
        Assert(!FgnCombatOwnership.ShouldOverride(mob.m_uid),"possessed server lease cannot override FGN native ownership rules");
        Call("NativeSetOwner",mob,3L);Assert(mob.GetOwner()==3,"possessed lease cannot pin native ownership assignment");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();mob.Set(possessed,42L);
        FgnCombatOwnership.Stop();Assert(mob.GetOwner()==100,"shutdown cannot release a lease newly controlled by another mod");

        Setup();target.Ping=400;Handle(out moved);ServerAccept();mob.Set(possessed,42L);target.Ping=100;
        Assert(!Handle(out moved)&&!moved&&mob.GetOwner()==100,"possessed server lease never makes a direct player return");
    }

    private static void AttackCooldownTests()
    {
        Setup();Harmony hooks=new Harmony("combat.attack.retry.fixture");
        MethodInfo result=typeof(FiresGhettoNetworkMod.CreatureOwnership).GetMethod("OnResult",flags);
        MethodInfo prefix=service.GetMethod("ResultPrefix",flags),postfix=service.GetMethod("ResultPostfix",flags);
        hooks.Patch(result,prefix:new HarmonyMethod(prefix),postfix:new HarmonyMethod(postfix));
        try
        {
            FgnCombatOwnership.Fgn.MarkPending(mob.m_uid,2,20);
            result.Invoke(null,new object[]{old.m_rpc,mob.m_uid,false,"attacking"});
            Assert(!FgnCombatOwnership.Fgn.Cooldowns.ContainsKey(mob.m_uid),"active AI attack rejection leaves no retry cooldown");
            Assert(!FgnCombatOwnership.Fgn.Pending(mob.m_uid,20),"native rejected-result pending cleanup preserved");
            Type native=typeof(FiresGhettoNetworkMod.CreatureOwnership);
            Assert((int)native.GetField("s_declined",flags).GetValue(null)==1&&
                ((Dictionary<string,int>)native.GetField("s_declineReasons",flags).GetValue(null))["attacking"]==1,"native rejection statistics preserved");
            ZDOID reported;Assert(FgnCombatOwnership.ReadTarget(mob,out reported)&&reported==target.m_characterID,"attack rejection retains current player target");

            hooks.Patch(FgnCombatOwnership.Fgn.TryHandOff,transpiler:new HarmonyMethod(service.GetMethod("RewriteTarget",flags)));
            hooks.Patch(FgnCombatOwnership.Fgn.Veto,postfix:new HarmonyMethod(service.GetMethod("VetoPostfix",flags)),
                transpiler:new HarmonyMethod(service.GetMethod("RewriteVeto",flags)));
            Assert(FgnCombatOwnership.Fgn.TryTransfer(mob,20)&&old.m_rpc.Destination==2,"same unchanged target may retry without an extra wait");
            Client(1);Character body=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();body.Attack=true;
            MethodInfo receive=native.GetMethod("OnHandOff",flags);int forced=ZDOMan.instance.Forced;
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==1&&ZNet.instance.ServerPeer.m_rpc.HandoffReason=="attacking"&&ZDOMan.instance.Forced==forced,"actual ongoing attack still rejects retry before ownership or ForceSend");
            Server();result.Invoke(null,new object[]{old.m_rpc,mob.m_uid,false,"attacking"});
            Assert(!FgnCombatOwnership.Fgn.Cooldowns.ContainsKey(mob.m_uid),"continued attack also leaves no retry cooldown");
            Time.realtimeSinceStartupAsDouble=22;body.Attack=false;
            Assert(FgnCombatOwnership.Fgn.TryTransfer(mob,22),"next existing FGN pass can retry unchanged target after attack ends");
            Client(1);receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==2&&ZNet.instance.ServerPeer.m_rpc.HandoffAccepted,"ended attack allows native transfer to current AI target");
            Server();result.Invoke(null,new object[]{old.m_rpc,mob.m_uid,true,""});
            Assert((int)native.GetField("s_accepted",flags).GetValue(null)==1,"native accepted-result statistics preserved");

            foreach(string reason in new[]{"staggering","AI target changed","held","owner fighting","tamed","gone","not owner","bad target","dead","boss","ridden","",null})
            {
                Setup();FgnCombatOwnership.Fgn.MarkPending(mob.m_uid,2,20);
                result.Invoke(null,new object[]{old.m_rpc,mob.m_uid,false,reason});
                Assert(FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]==26,"other native rejection retains six seconds: "+(reason??"null"));
            }
            Action<Action,string> unchanged=(alter,name)=>{
                Setup();FgnCombatOwnership.Fgn.MarkPending(mob.m_uid,2,20);alter();
                result.Invoke(null,new object[]{old.m_rpc,mob.m_uid,false,"attacking"});
                Assert(FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]==Time.realtimeSinceStartupAsDouble+6,name);
            };
            unchanged(()=>FgnCombatOwnership.Enabled=false,"disabled optional feature retains stock attack cooldown");
            unchanged(()=>FiresGhettoNetworkMod.CreatureOwnership.Enabled=false,"inactive FGN balance retains stock attack cooldown");
            unchanged(()=>Call("RemoveWatch",mob.m_uid),"unmarked mob retains stock attack cooldown");
            unchanged(()=>{var map=(System.Collections.IDictionary)service.GetField("watching",flags).GetValue(null);object watch=map[mob.m_uid];watch.GetType().GetField("Acknowledged",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(watch,false);},"unacknowledged activation retains stock attack cooldown");
            unchanged(()=>FiresGhettoNetworkMod.CreatureOwnership.ResetTransfers(),"unsolicited result without pending request retains stock cooldown");
            unchanged(()=>Time.realtimeSinceStartupAsDouble=25,"expired pending request retains stock cooldown");
            unchanged(()=>old.Ready=false,"unready source retains stock attack cooldown");
            unchanged(()=>old.Matched=false,"FGN-incompatible source retains stock attack cooldown");
            unchanged(()=>ZDOMan.instance.GetZDO(old.m_characterID).Set("toomanylags.fgn.ai.protocol".GetStableHashCode(),0),"missing client capability retains stock attack cooldown");
            unchanged(()=>mob.Set(ZDOVars.s_tamed,1),"tamed mob retains stock attack cooldown");
            unchanged(()=>mob.Set(ZDOVars.s_user,1),"controlled mob retains stock attack cooldown");
            unchanged(()=>{Client(1);Set("serverConfirmed",false);},"client receive cannot remove a cooldown");
            unchanged(()=>Set("network",new ZNet()),"old network identity retains stock attack cooldown");
            unchanged(()=>Set("world",new ZDOMan()),"old world identity retains stock attack cooldown");
            unchanged(()=>{Client(1);ai.Target=null;FgnCombatOwnership.Observe(ai);Server();},"noncombat report retains stock attack cooldown");
            unchanged(()=>mob.SetOwner(3),"changed source ownership/revision retains stock attack cooldown");
            Setup();FgnCombatOwnership.Fgn.MarkPending(mob.m_uid,2,20);
            result.Invoke(null,new object[]{other.m_rpc,mob.m_uid,false,"attacking"});
            Assert(FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]==26,"foreign source response cannot remove current owner's cooldown");
            Setup();FgnCombatOwnership.Fgn.MarkPending(mob.m_uid,2,20);
            object[] state={old.m_rpc,mob.m_uid,false,"attacking",0.0};prefix.Invoke(null,state);
            FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]=30;postfix.Invoke(null,new object[]{mob.m_uid,(double)state[4]});
            Assert(FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]==30,"different longer cooldown from another patch preserved");
            FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]=21;postfix.Invoke(null,new object[]{mob.m_uid,(double)state[4]});
            Assert(FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]==21,"different shorter cooldown from another patch preserved");
            FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]=26;postfix.Invoke(null,new object[]{mob.m_uid,0.0});
            Assert(FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]==26,"default Harmony state from skipped prefix cannot remove cooldown");
            Setup();FgnCombatOwnership.Fgn.MarkPending(mob.m_uid,2,20);Time.AutoAdvance=0.001;
            result.Invoke(null,new object[]{old.m_rpc,mob.m_uid,false,"attacking"});Time.AutoAdvance=0;
            Assert(!FgnCombatOwnership.Fgn.Cooldowns.ContainsKey(mob.m_uid),"advancing realtime within an RPC still removes native attack cooldown");
            Assert(old.m_rpc.Sent==0&&mob.OwnerWrites==0,"result hook adds no retry RPC or forced ownership");
        }
        finally{Time.AutoAdvance=0;hooks.UnpatchSelf();}
    }

    private static void AttackEndTests()
    {
        System.Threading.SynchronizationContext previous=System.Threading.SynchronizationContext.Current;
        UnitySynchronizationContext context=new UnitySynchronizationContext();
        System.Threading.SynchronizationContext.SetSynchronizationContext(context);
        Harmony hooks=new Harmony("combat.attack.end.fixture");
        Type native=typeof(FiresGhettoNetworkMod.CreatureOwnership);
        MethodInfo receive=native.GetMethod("OnHandOff",flags),result=native.GetMethod("OnResult",flags);
        try
        {
            FgnAttackRetry.Install(hooks,(body,data)=>Call("OnAttackEnded",body,data),error=>Call("Fail",error));
            hooks.Patch(FgnCombatOwnership.Fgn.TryHandOff,prefix:new HarmonyMethod(service.GetMethod("TryPrefix",flags)),
                postfix:new HarmonyMethod(service.GetMethod("TryPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteTarget",flags)));
            hooks.Patch(FgnCombatOwnership.Fgn.Veto,postfix:new HarmonyMethod(service.GetMethod("VetoPostfix",flags)),
                transpiler:new HarmonyMethod(service.GetMethod("RewriteVeto",flags)));
            hooks.Patch(result,prefix:new HarmonyMethod(service.GetMethod("ResultPrefix",flags)),postfix:new HarmonyMethod(service.GetMethod("ResultPostfix",flags)));
            Func<Attack> reject=()=>
            {
                Setup();Assert(FgnCombatOwnership.Fgn.TryTransfer(mob,20),"attack-end fixture starts a real FGN handoff");
                Client(1);Humanoid body=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();Attack attack=body.BeginAttack();
                receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
                Assert(mob.GetOwner()==1&&ZNet.instance.ServerPeer.m_rpc.HandoffReason=="attacking","actual attack veto arms without transferring");
                Server();result.Invoke(null,new object[]{old.m_rpc,mob.m_uid,false,"attacking"});Client(1);return attack;
            };
            Action<Attack> end=attack=>
            {
                ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>().Attack=false;
                attack.Stop();context.Flush();
            };
            Action ready=()=>Call("ReceiveAttackReady",old,mob.m_uid,(long)mob.OwnerRevision);

            Attack current=reject();Humanoid actor=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();
            int reports=ZNet.instance.ServerPeer.m_rpc.TargetSent,posts=context.Posts,owners=mob.OwnerWrites;
            actor.Attack=false;current.Stop();
            Assert(context.Posts==posts+1&&ZNet.instance.ServerPeer.m_rpc.AttackReadySent==0&&mob.OwnerWrites==owners,"attack stop defers one notification beyond unfinished native work");
            current.Stop();Assert(context.Posts==posts+1,"repeated Stop on same attack cannot queue duplicates");
            context.Flush();ZRpc client=ZNet.instance.ServerPeer.m_rpc;
            Assert(client.AttackReadySent==1&&client.TargetSent==reports,"unchanged player target sends exactly one ready event without a fresh target report");
            Assert(client.AttackReadyMob==mob.m_uid&&client.AttackReadyRevision==(long)mob.OwnerRevision,"ready event carries exact mob and Int64 owner revision");
            Server();ready();Assert(old.m_rpc.Sent==2&&old.m_rpc.Destination==2,"unchanged target immediately retries before next two-second pass");
            Client(1);receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==2&&ZNet.instance.ServerPeer.m_rpc.HandoffAccepted,"ended attack transfers to unchanged actual AI target");

            current=reject();actor=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();
            ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();end(current);
            Assert(ZNet.instance.ServerPeer.m_rpc.AttackReadySent==1&&ZNet.instance.ServerPeer.m_rpc.TargetId==other.m_characterID,"changed current target sends target event plus readiness for a still-pending veto reply");
            Server();Call("ReceiveTarget",old,mob.m_uid,other.m_characterID,(long)mob.OwnerRevision);
            Assert(old.m_rpc.Destination==3,"attack-end retry uses latest player target instead of capturing old destination");

            // End can reach the server before the decline reply; do not race Pending.
            Setup();FgnCombatOwnership.Fgn.TryTransfer(mob,20);Client(1);actor=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();current=actor.BeginAttack();
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});end(current);
            Server();ready();Assert(old.m_rpc.Sent==1&&FgnCombatOwnership.Fgn.Pending(mob.m_uid,20),"early attack-ready event waits for original in-flight result");
            result.Invoke(null,new object[]{old.m_rpc,mob.m_uid,false,"attacking"});
            Assert(old.m_rpc.Sent==2&&old.m_rpc.Destination==2,"late confirmed attack decline consumes early readiness immediately after pending cleanup");

            Action<Action,string> ignore=(alter,label)=>
            {
                Attack attack=reject();alter();end(attack);
                Assert(ZNet.instance.ServerPeer.m_rpc.AttackReadySent==0,label);
            };
            ignore(()=>mob.SetOwner(3),"lost ownership cancels queued attack notification");
            ignore(()=>{mob.SetOwner(3);mob.SetOwner(1);},"A-B-A owner revision invalidates old attack ticket");
            ignore(()=>FgnAttackRetry.Reset(),"reset epoch cancels old attack ticket");
            ignore(()=>FgnCombatOwnership.Enabled=false,"disabled feature cannot emit attack-ready events");
            ignore(()=>mob.Set(ZDOVars.s_tamed,1),"taming excludes pending attack-ready event");
            ignore(()=>mob.Set(ZDOVars.s_user,2),"new rider/controller excludes pending attack-ready event");
            ignore(()=>mob.Set("valcast_possessed".GetStableHashCode(),42),"direct possession excludes pending attack-ready event");
            ignore(()=>mob.Held=true,"FGN combat hold excludes pending attack-ready event");
            ignore(()=>ai.Target=ZNetScene.instance.FindInstance(mob).GetComponent<Character>(),"non-player current target emits no attack-ready event");
            ignore(()=>targetPlayer.Dead=true,"dead current player target emits no attack-ready event");
            ignore(()=>Set("serverConfirmed",false),"unconfirmed server cannot receive attack-ready events");
            ignore(()=>ZNet.instance.ServerPeer.Ready=false,"unready server cannot receive attack-ready events");
            ignore(()=>Call("RemoveWatch",mob.m_uid),"removed watch cannot emit attack-ready events");

            Action<Action,string> blocked=(alter,label)=>
            {
                Attack attack=reject();end(attack);Server();alter();ready();Assert(old.m_rpc.Sent==1,label);
            };
            blocked(()=>old.Ready=false,"unready source cannot trigger retry");
            blocked(()=>old.Matched=false,"FGN mismatch cannot trigger retry");
            blocked(()=>ZDOMan.instance.GetZDO(old.m_characterID).Set("toomanylags.fgn.ai.protocol".GetStableHashCode(),2),"old capability cannot trigger new retry endpoint");
            blocked(()=>FgnCombatOwnership.Fgn.PerOwner[1]=6,"attack-ready retries preserve per-owner budget");
            blocked(()=>Set("passMoves",16),"attack-ready retries preserve shared pass budget");
            blocked(()=>FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]=30,"attack-ready retries preserve unrelated cooldowns");
            blocked(()=>Set("world",new ZDOMan()),"old-world source cannot trigger retry");
            blocked(()=>Set("network",new ZNet()),"old-network source cannot trigger retry");
            current=reject();end(current);Server();
            Call("ReceiveAttackReady",target,mob.m_uid,(long)mob.OwnerRevision);
            Call("ReceiveAttackReady",old,mob.m_uid,(long)mob.OwnerRevision+1);
            ZNetPeer replacement=new ZNetPeer{m_uid=old.m_uid,m_characterID=old.m_characterID};
            Call("ReceiveAttackReady",replacement,mob.m_uid,(long)mob.OwnerRevision);
            Assert(old.m_rpc.Sent==1,"foreign owner, stale revision and replaced peer cannot trigger retry");
            Setup();ready();Assert(old.m_rpc.Sent==0,"unsolicited ready event without pending/attack rejection does nothing");

            current=reject();actor=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();
            Attack next=actor.BeginAttack();current.Stop();context.Flush();
            Assert(ZNet.instance.ServerPeer.m_rpc.AttackReadySent==0,"attack chain still in attack sends no unsafe retry");
            actor.Attack=false;next.Stop();context.Flush();
            Assert(ZNet.instance.ServerPeer.m_rpc.AttackReadySent==1,"chained active attack rearms exactly once for its real end");

            current=reject();actor=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();actor.Attack=false;
            Harmony cancel=new Harmony("combat.attack.cancel.stop.fixture");
            try
            {
                cancel.Patch(typeof(Attack).GetMethod("Stop"),prefix:new HarmonyMethod(typeof(FgnCombatTests).GetMethod("CancelFixtureStop",flags)));
                posts=context.Posts;current.Stop();context.Flush();
                Assert(!current.IsDone()&&context.Posts==posts&&ZNet.instance.ServerPeer.m_rpc.AttackReadySent==0,"another mod cancelling native Stop cannot dispatch a false attack-end event");
            }
            finally{cancel.UnpatchSelf();}
            current.Stop();context.Flush();
            Assert(ZNet.instance.ServerPeer.m_rpc.AttackReadySent==1,"ticket survives cancelled Stop for its eventual genuine end");

            current=reject();actor=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();
            mob.SetOwner(3);mob.SetOwner(1);
            Call("ReceiveWatch",ZNet.instance.ServerPeer,mob.m_uid,1L,(long)mob.OwnerRevision);Call("Observe",ai);
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});end(current);
            Assert(ZNet.instance.ServerPeer.m_rpc.AttackReadySent==1,"fresh real veto after A-B-A replaces stale ticket with current revision");

            current=reject();actor=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();actor.Attack=false;
            client=ZNet.instance.ServerPeer.m_rpc;current.Stop();ZDOMan.instance=new ZDOMan();context.Flush();
            Assert(client.AttackReadySent==0,"queued end from replaced world cannot touch new session");
            current=reject();actor=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();actor.Attack=false;
            client=ZNet.instance.ServerPeer.m_rpc;current.Stop();ZNet.instance=new ZNet();context.Flush();
            Assert(client.AttackReadySent==0,"queued end from replaced network cannot touch new session");

            current=reject();actor=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();actor.Attack=false;
            context.ThrowPost=true;
            try{current.Stop();Assert(!FgnCombatOwnership.Enabled,"failed Unity dispatch disables only optional module without escaping native Stop");}
            finally{context.ThrowPost=false;}

            current=reject();end(current);Server();target.Ping=400;NativePup.Ready=false;ready();
            Assert(old.m_rpc.Sent==1,"high-ping server retry still waits for real collider readiness");
            current=reject();end(current);Server();target.Ping=400;ready();
            Assert(old.m_rpc.Destination==100,"close-player high-ping exception still chooses server after attack");

            Setup();target.Ping=400;bool moved;Handle(out moved);ServerAccept();target.Ping=100;
            actor=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();current=actor.BeginAttack();
            Handle(out moved);Assert(!moved&&mob.GetOwner()==100,"server lease return records its local attack veto");
            actor.Attack=false;current.Stop();context.Flush();
            Assert(mob.GetOwner()==2,"server-owned lease returns immediately after native attack end without client RPC");

            Setup();posts=context.Posts;Attack unarmed=new Attack();int reads=NativePup.Checks;
            for(int i=0;i<10000;i++)unarmed.Stop();
            Assert(context.Posts==posts&&NativePup.Checks==reads&&old.m_rpc.Sent==0,"ten thousand unarmed stop calls do no scheduling, collider checks or transfer work");
            current=reject();actor=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();actor.Attack=false;current.Stop();FgnAttackRetry.Reset();context.Flush();
            Assert(ZNet.instance.ServerPeer.m_rpc.AttackReadySent==0,"already queued callback is cancelled by reset epoch");
        }
        finally{hooks.UnpatchSelf();FgnAttackRetry.Reset();System.Threading.SynchronizationContext.SetSynchronizationContext(previous);}
    }

    private static void TargetCooldownTests()
    {
        Setup();Harmony hooks=new Harmony("combat.target.during.attack.fixture");
        Type native=typeof(FiresGhettoNetworkMod.CreatureOwnership);
        MethodInfo result=native.GetMethod("OnResult",flags),receive=native.GetMethod("OnHandOff",flags);
        hooks.Patch(result,prefix:new HarmonyMethod(service.GetMethod("ResultPrefix",flags)),postfix:new HarmonyMethod(service.GetMethod("ResultPostfix",flags)));
        hooks.Patch(FgnCombatOwnership.Fgn.TryHandOff,prefix:new HarmonyMethod(service.GetMethod("TryPrefix",flags)),
            postfix:new HarmonyMethod(service.GetMethod("TryPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteTarget",flags)));
        hooks.Patch(FgnCombatOwnership.Fgn.Veto,postfix:new HarmonyMethod(service.GetMethod("VetoPostfix",flags)),
            transpiler:new HarmonyMethod(service.GetMethod("RewriteVeto",flags)));
        Action<ZDOID> change=id=>Call("ReceiveTarget",old,mob.m_uid,id,(long)mob.OwnerRevision);
        Action arm=()=>{change(target.m_characterID);
            Assert(old.m_rpc.Sent==1&&old.m_rpc.Destination==2,"initial owner report requests current player target");
            result.Invoke(null,new object[]{old.m_rpc,mob.m_uid,false,"attacking"});};
        try
        {
            arm();Time.realtimeSinceStartupAsDouble=20.5;change(other.m_characterID);
            Assert(!FgnCombatOwnership.Fgn.Cooldowns.ContainsKey(mob.m_uid)&&old.m_rpc.Sent==2&&old.m_rpc.Destination==3,"new player target immediately requests new owner without a cooldown");
            Assert(mob.OwnerWrites==0,"target event requests native transfer without forcing ownership");
            Client(1);ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();
            Character body=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();body.Attack=true;
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,3L});
            Assert(mob.GetOwner()==1&&ZNet.instance.ServerPeer.m_rpc.HandoffReason=="attacking","new target retains actual ongoing-attack veto");
            Server();result.Invoke(null,new object[]{old.m_rpc,mob.m_uid,false,"attacking"});
            ZDOID report;Assert(FgnCombatOwnership.ReadTarget(mob,out report)&&report==other.m_characterID,"new target during attack remains recorded despite rejected handoff");
            Time.realtimeSinceStartupAsDouble=22.5;body.Attack=false;
            Assert(FgnCombatOwnership.Fgn.TryTransfer(mob,22.5)&&old.m_rpc.Destination==3,"next existing scan retries latest target without another target event");
            Client(1);receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,3L});
            Assert(mob.GetOwner()==3&&ZNet.instance.ServerPeer.m_rpc.HandoffAccepted,"ended attack receives latest target owner through native handoff");

            Setup();arm();change(target.m_characterID);
            Assert(old.m_rpc.Sent==1,"duplicate player target report does not create another transfer");
            Setup();arm();change(ZDOID.None);
            Assert(old.m_rpc.Sent==1&&FgnCombatOwnership.ReadTarget(mob,out report)&&report.IsNone(),"player target loss invalidates report without immediate handoff");
            change(target.m_characterID);Assert(old.m_rpc.Sent==2&&old.m_rpc.Destination==2,"reacquisition after target loss triggers normal player-target attempt");
            Setup();arm();change(mob.m_uid);Assert(old.m_rpc.Sent==1,"non-player ZDO cannot become a handoff destination");
            Setup();arm();change(new ZDOID(9876,1));Assert(old.m_rpc.Sent==1,"unknown target cannot become a handoff destination");
            Setup();arm();other.Ready=false;change(other.m_characterID);Assert(old.m_rpc.Sent==1,"unready player target cannot receive ownership");
            Setup();arm();other.Matched=false;change(other.m_characterID);Assert(old.m_rpc.Sent==1,"FGN-incompatible player target cannot receive ownership");
            foreach(string reason in new[]{"staggering","AI target changed","held","owner fighting","tamed","gone"})
            {
                Setup();arm();result.Invoke(null,new object[]{old.m_rpc,mob.m_uid,false,reason});change(other.m_characterID);
                Assert(FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]==26&&old.m_rpc.Sent==1,"new target preserves unrelated decline cooldown: "+reason);
            }
            Setup();arm();FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]=30;change(other.m_characterID);
            Assert(FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]==30&&old.m_rpc.Sent==1,"new target leaves another hook's cooldown unchanged");
            Setup();arm();FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]=22;change(other.m_characterID);
            Assert(FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]==22&&old.m_rpc.Sent==1,"new target cannot delete an untracked cooldown");
            Action<Action,string> ignored=(alter,name)=>{
                Setup();arm();FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]=30;alter();change(other.m_characterID);
                Assert(FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]==30&&old.m_rpc.Sent==1,name);
            };
            ignored(()=>FgnCombatOwnership.Enabled=false,"disabled optional feature ignores new target report");
            ignored(()=>Set("network",new ZNet()),"old network ignores new target report");
            ignored(()=>Set("world",new ZDOMan()),"old world ignores new target report");
            ignored(()=>Call("RemoveWatch",mob.m_uid),"unmarked mob ignores new target report");
            ignored(()=>mob.SetOwner(3),"new owner/revision ignores former owner's target report");
            Setup();arm();Set("world",null);ZDOMan.instance=null;change(other.m_characterID);
            Assert(FgnCombatOwnership.Enabled&&old.m_rpc.Sent==1,"missing world ignores stale report without failing optional feature");
            Setup();arm();ZNetPeer replaced=new ZNetPeer{m_uid=old.m_uid,m_characterID=old.m_characterID};
            Call("ReceiveTarget",replaced,mob.m_uid,other.m_characterID,(long)mob.OwnerRevision);
            Assert(old.m_rpc.Sent==1,"replaced same-UID source cannot update target");
            Call("ReceiveTarget",target,mob.m_uid,other.m_characterID,(long)mob.OwnerRevision);
            Assert(old.m_rpc.Sent==1,"non-owner source cannot update target");
            Call("ReceiveTarget",old,mob.m_uid,other.m_characterID,(long)mob.OwnerRevision+1);
            Assert(old.m_rpc.Sent==1,"stale revision cannot update target");

            Setup();arm();FgnCombatOwnership.Fgn.PerOwner[1]=6;change(other.m_characterID);
            Assert(old.m_rpc.Sent==1,"unthrottled attack retry still obeys native per-owner cap");
            Setup();arm();Set("passMoves",16);change(other.m_characterID);
            Assert(old.m_rpc.Sent==1,"unthrottled attack retry still obeys shared sixteen-move cap");
            Setup();arm();FgnCombatOwnership.Fgn.MarkPending(mob.m_uid,2,20);change(other.m_characterID);
            Assert(old.m_rpc.Sent==1&&FgnCombatOwnership.Fgn.Pending(mob.m_uid,20),"new target does not cancel or race another pending request");
            Setup();arm();other.Ping=400;change(other.m_characterID);
            Assert(old.m_rpc.Sent==2&&old.m_rpc.Destination==100,"new target preserves close-player high-ping server fallback");
        }
        finally{hooks.UnpatchSelf();}
    }

    private static void DelayedHandoffTests()
    {
        Setup();Harmony handoff=new Harmony("combat.delayed.handoff.fixture");
        MethodInfo guard=service.GetMethod("VetoPostfix",flags);
        handoff.Patch(FgnCombatOwnership.Fgn.Veto,postfix:guard==null?null:new HarmonyMethod(guard),
            transpiler:new HarmonyMethod(service.GetMethod("RewriteVeto",flags)));
        handoff.Patch(FgnCombatOwnership.Fgn.TryHandOff,transpiler:new HarmonyMethod(service.GetMethod("RewriteTarget",flags)));
        MethodInfo receive=typeof(FiresGhettoNetworkMod.CreatureOwnership).GetMethod("OnHandOff",flags);
        try
        {
            Assert((bool)FgnCombatOwnership.Fgn.TryHandOff.Invoke(null,new object[]{mob,20.0})&&old.m_rpc.Destination==2,"server initially requests the reported player target");
            Client(1);Player local=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(old.m_characterID)).GetComponent<Player>();
            ai.Target=local;ZDOID reported;Assert(FgnCombatOwnership.ReadTarget(mob,out reported)&&reported==target.m_characterID,"queued job can outlive the cached target report before next event dispatch");
            int writes=mob.Writes,owners=mob.OwnerWrites,forced=ZDOMan.instance.Forced;
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==1&&!ZNet.instance.ServerPeer.m_rpc.HandoffAccepted,"delayed handoff cannot assign another player while actual AI pursues current owner");
            Assert(mob.Writes==writes&&mob.OwnerWrites==owners&&ZDOMan.instance.Forced==forced,"obsolete handoff rejected before ForceSend and SetOwner without report mutation");
            Assert(ZNet.instance.ServerPeer.m_rpc.HandoffReason=="AI target changed","obsolete handoff returns a normal FGN decline reason");

            Setup();Client(1);receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==2&&ZNet.instance.ServerPeer.m_rpc.HandoffAccepted,"unchanged actual player target receives normal native handoff");
            Setup();Client(1);ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==1&&!ZNet.instance.ServerPeer.m_rpc.HandoffAccepted,"actual third player target rejects previously requested second player");
            Setup();Client(1);ai.Target=local=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(old.m_characterID)).GetComponent<Player>();
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,100L});
            Assert(mob.GetOwner()==100&&ZNet.instance.ServerPeer.m_rpc.HandoffAccepted,"explicit dedicated server fallback remains allowed");
            Setup();Client(1);ai.Target=null;FgnCombatOwnership.Observe(ai);
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,3L});
            Assert(mob.GetOwner()==3&&ZNet.instance.ServerPeer.m_rpc.HandoffAccepted,"no player target retains FGN noncombat lower-ping transfers");
            Setup();Client(1);ai.Target=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();FgnCombatOwnership.Observe(ai);
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,3L});
            Assert(mob.GetOwner()==3,"non-player target does not gain a player-target handoff veto");
            Setup();Client(1);ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(old.m_characterID)).GetComponent<Player>();Set("serverConfirmed",false);
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==2,"unconfirmed server mode retains original FGN receive behavior");
            Setup();Client(1);ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(old.m_characterID)).GetComponent<Player>();Call("RemoveWatch",mob.m_uid);
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==2,"unmarked mob retains original FGN receive behavior");
            Setup();Client(1);ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(old.m_characterID)).GetComponent<Player>();FgnCombatOwnership.Enabled=false;
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==2,"disabled feature retains original FGN receive behavior");
            Setup();Client(1);Character body=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();body.Attack=true;
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==1&&ZNet.instance.ServerPeer.m_rpc.HandoffReason=="attacking","native attack veto kept even for a correct player target");
            Setup();Client(1);body=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();body.Stagger=true;
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==1&&ZNet.instance.ServerPeer.m_rpc.HandoffReason=="staggering","native stagger veto kept");
            Setup();Client(1);body=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();body.Tamed=true;
            receive.Invoke(null,new object[]{ZNet.instance.ServerPeer.m_rpc,mob.m_uid,2L});
            Assert(mob.GetOwner()==1&&ZNet.instance.ServerPeer.m_rpc.HandoffReason=="tamed","native tamed veto kept");
        }
        finally { handoff.UnpatchSelf(); }
    }

    private static void ClientBalanceTests()
    {
        Setup();Client(1);Harmony balance=new Harmony("combat.client.balance.fixture");
        balance.Patch(FgnCombatOwnership.Fgn.Balance,prefix:new HarmonyMethod(service.GetMethod("BalancePrefix",flags)),
            postfix:new HarmonyMethod(service.GetMethod("BalancePostfix",flags)));
        try
        {
            Assert(FgnCombatOwnership.ShouldOverride(mob.m_uid)&&FgnCombatOwnership.Watching(ai),"owner watch active before client FGN update");
            int writes=mob.Writes,owners=mob.OwnerWrites,watches=old.m_rpc.WatchSent,targets=ZNet.instance.ServerPeer.m_rpc.TargetSent;
            for(int i=0;i<100;i++)FgnCombatOwnership.Fgn.Balance.Invoke(null,new object[]{ZDOMan.instance});
            Assert(FgnCombatOwnership.ShouldOverride(mob.m_uid)&&FgnCombatOwnership.Watching(ai),"client FGN Balance updates must not erase server-supplied owner watches");
            Assert(mob.Writes==writes&&mob.OwnerWrites==owners&&old.m_rpc.WatchSent==watches&&ZNet.instance.ServerPeer.m_rpc.TargetSent==targets,"idle client balance hook does no watch/report/ownership work");
            ZNet.instance=null;FgnCombatOwnership.Fgn.Balance.Invoke(null,new object[]{ZDOMan.instance});
            Assert(FgnCombatOwnership.Enabled,"client balance without a network does not disable optional feature");
        }
        finally { balance.UnpatchSelf(); }
    }

    private static void DiagnosticTests()
    {
        Setup();FgnCombatOwnership.InstallationStatus="installed";
        Set("configured",new ConfigEntry<bool>(true));Set("detectedVersion",new Version(1,5,36));
        FiresGhettoNetworkMod.CreatureOwnership.Hit(mob.m_uid,3);
        string status=String.Join("\n",new List<string>(FgnCombatOwnership.DescribeStatus()).ToArray());
        Assert(status.Contains("server balance=True")&&status.Contains("protocol=5"),"diagnostics show actual balance and peer protocol rather than config alone");
        int writes=mob.Writes,owners=mob.OwnerWrites,sent=old.m_rpc.Sent,watchSent=old.m_rpc.WatchSent,targetSent=ZNet.instance.ServerPeer.m_rpc.TargetSent;
        string line=FgnCombatOwnership.DescribeMob(mob,ai);
        Assert(line.Contains("rule=AI target")&&line.Contains("last attacker=3")&&line.Contains("report="),"diagnostics expose AI rule and differing raw last attacker");
        Assert(line.Contains("not authoritative"),"server replica target never labelled authoritative while a client owns mob");
        Assert(mob.Writes==writes&&mob.OwnerWrites==owners&&old.m_rpc.Sent==sent&&old.m_rpc.WatchSent==watchSent&&ZNet.instance.ServerPeer.m_rpc.TargetSent==targetSent,"diagnostics do not write reports, ownership, watches or RPCs");
        Call("RemoveWatch",mob.m_uid);line=FgnCombatOwnership.DescribeMob(mob,ai);
        Assert(line.Contains("not marked by two-player scan"),"unwatched damage fallback is visible");
        Call("MarkWatch",mob);line=FgnCombatOwnership.DescribeMob(mob,ai);
        Assert(line.Contains("waiting for owner's initial target report"),"initial report wait not reported as an active fallback");
        ZDOMan.instance.GetZDO(old.m_characterID).Set("toomanylags.fgn.ai.protocol".GetStableHashCode(),0L);
        Assert(FgnCombatOwnership.DescribeMob(mob,ai).Contains("participation missing"),"missing client capability is directly diagnosable");
        Setup();Client(1);Set("serverConfirmed",false);
        Assert(FgnCombatOwnership.DescribeMob(mob,ai).Contains("server mode unconfirmed"),"unconfirmed server mode is distinct from installation failure");
        Set("serverConfirmed",true);
        Assert(FgnCombatOwnership.DescribeMob(mob,ai).Contains("(authoritative)"),"owner's local native AI target identified as authoritative");
        Set("world",null);writes=mob.Writes;
        status=String.Join("\n",new List<string>(FgnCombatOwnership.DescribeStatus()).ToArray());
        Assert(status.Contains("Current session=False")&&mob.Writes==writes&&(object)service.GetField("world",flags).GetValue(null)==null,"status query does not initialize a world or publish capabilities");
        Setup();Call("Fail",new Exception("diagnostic fixture"));
        Assert(FgnCombatOwnership.InstallationStatus=="runtime failure: diagnostic fixture"&&FgnCombatOwnership.DescribeMob(mob,ai).Contains("diagnostic fixture"),"optional runtime failure retained for manual query with logs disabled");
        RemoteDiagnosticTests();
    }

    private static void RemoteDiagnosticTests()
    {
        Setup();Call("RemoveWatch",mob.m_uid);
        Plugin.Diagnostics=false;Call("SessionConnection",old);
        Assert(!old.m_rpc.HasHandler("TooManyLags_AI_Status")&&!old.m_rpc.HasHandler("TooManyLags_AI_StatusReply"),"disabled logging installs no diagnostic RPC endpoints");
        Plugin.Diagnostics=true;
        string line=FgnCombatOwnership.DescribeServerMob(mob.m_uid);
        Assert(line.Contains("FGN candidates=3")&&line.Contains("eligible near mob=3")&&line.Contains("owner protocol=5")&&line.Contains("server watch=missing"),"server diagnostic distinguishes candidate count, owner capability and missing watch");
        Assert(line.Contains("prefab=True")&&line.Contains("type=True")&&line.Contains("scan pass="),"server diagnostic reads original FGN prefab/type filters and actual addon pass");
        Assert(line.Contains("reported target peer=2")&&line.Contains("transfer pending=False")&&line.Contains("cooldown=0.0s"),"server diagnostic maps reported target to peer without reading a replica's local AI");
        FgnCombatOwnership.Fgn.MarkPending(mob.m_uid,2,Time.realtimeSinceStartupAsDouble);FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]=26;
        line=FgnCombatOwnership.DescribeServerMob(mob.m_uid);
        Assert(line.Contains("transfer pending=True")&&line.Contains("cooldown=6.0s"),"native pending and decline cooldown shown for deferred spawn/attack handoffs");
        int writes=mob.Writes,owners=mob.OwnerWrites,sent=old.m_rpc.Sent,watchSent=old.m_rpc.WatchSent;
        Call("SessionConnection",old);for(int i=0;i<20;i++)old.m_rpc.ReceiveStatus(mob.m_uid,1);
        Assert(old.m_rpc.StatusReplies==6&&old.m_rpc.StatusLine.Contains("SERVER"),"manual remote diagnostics bounded to six replies per two seconds");
        Plugin.Diagnostics=false;old.m_rpc.ReceiveStatus(mob.m_uid,9);
        Assert(old.m_rpc.StatusReplies==6,"disabling live logs immediately stops existing diagnostic handlers");
        Plugin.Diagnostics=true;
        Assert(mob.Writes==writes&&mob.OwnerWrites==owners&&old.m_rpc.Sent==sent&&old.m_rpc.WatchSent==watchSent,"server diagnostic request never changes report, watch or ownership");
        Time.realtimeSinceStartupAsDouble+=2;old.m_rpc.ReceiveStatus(mob.m_uid,2);Assert(old.m_rpc.StatusReplies==7,"rate limit renews without timer");
        int replies=old.m_rpc.StatusReplies;old.Ready=false;old.m_rpc.ReceiveStatus(mob.m_uid,3);old.Ready=true;
        old.m_rpc.ReceiveStatus(ZDOID.None,3);old.m_rpc.ReceiveStatus(mob.m_uid,0);
        Assert(old.m_rpc.StatusReplies==replies,"unready peers and invalid requests rejected");
        Client(1);old.m_rpc.ReceiveStatus(mob.m_uid,4);Assert(old.m_rpc.StatusReplies==replies,"client cannot act as diagnostic server");
        mob.Set(ZDOVars.s_tamed,1);mob.SetOwner(100);line=FgnCombatOwnership.DescribeMob(mob,ai);
        Assert(line.Contains("tamed (ownership unchanged)"),"excluded tamed mob has explicit rule even when owned by another policy");
        mob.Set(ZDOVars.s_tamed,0);Assert(FgnCombatOwnership.DescribeMob(mob,ai).Contains("observer: only the current owner"),"non-owner watch absence is not labelled failed two-player scan");
        Setup();Client(1);Terminal terminal=new Terminal();Player.m_localPlayer=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(old.m_characterID)).GetComponent<Player>();
        Character body=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();body.Tamed=true;Character.All.Add(body);
        MethodInfo print=typeof(FgnCombatStatus).GetMethod("Print",flags);print.Invoke(null,new object[]{new Terminal.ConsoleEventArgs{Context=terminal}});
        Assert(ZNet.instance.ServerPeer.m_rpc.StatusRequests==0&&String.Join("\n",terminal.Lines.ToArray()).Contains("tamed/boss mobs"),"tamed animals do not occupy wildcard diagnostic slots or cause server requests");
        body.Tamed=false;ZNetPeer server=ZNet.instance.ServerPeer;Call("SessionConnection",server);Call("SessionConnection",old);
        print.Invoke(null,new object[]{new Terminal.ConsoleEventArgs{Context=terminal}});int ticket=server.m_rpc.StatusTicket;
        Assert(server.m_rpc.StatusRequests==1,"one server request per selected loaded wild mob on explicit command");
        int lines=terminal.Lines.Count;server.m_rpc.ReceiveStatusReply(ticket-1,"stale");old.m_rpc.ReceiveStatusReply(ticket,"foreign");
        Assert(terminal.Lines.Count==lines,"stale tickets and foreign-peer responses ignored");
        server.m_rpc.ReceiveStatusReply(ticket,"SERVER verified fixture");Assert(terminal.Lines.Count==lines+1,"only current server reply is displayed in original terminal");
        Time.realtimeSinceStartupAsDouble+=11;server.m_rpc.ReceiveStatusReply(ticket,"expired");Assert(terminal.Lines.Count==lines+1,"late status responses expire without timer");
        Time.realtimeSinceStartupAsDouble=20;FgnCombatStatus.Reset();server.m_rpc.ReceiveStatusReply(ticket,"reset");Assert(terminal.Lines.Count==lines+1,"world reset clears pending status request");
        print.Invoke(null,new object[]{new Terminal.ConsoleEventArgs{Context=terminal}});ticket=server.m_rpc.StatusTicket;lines=terminal.Lines.Count;
        ZNet.instance=new ZNet{Dedicated=false,ServerPeer=server};server.m_rpc.ReceiveStatusReply(ticket,"old connection");
        Assert(terminal.Lines.Count==lines,"old network identity cannot deliver response into new session");
        Plugin.Diagnostics=false;
    }

    private static void Client(long owner){ZNet.instance.Dedicated=false;ZDOMan.Session=owner;Set("serverConfirmed",true);}
    private static void Server(){ZNet.instance.Dedicated=true;ZDOMan.Session=100;Set("serverConfirmed",false);}
    private static bool IsAcknowledged(ZDO data)
    {var map=(System.Collections.IDictionary)service.GetField("watching",flags).GetValue(null);object watch=map[data.m_uid];return watch!=null&&(bool)watch.GetType().GetField("Acknowledged",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(watch);}
    private static void EventTests()
    {
        Setup();Set("collecting",false);
        var context=new UnitySynchronizationContext();System.Threading.SynchronizationContext.SetSynchronizationContext(context);
        Harmony events=new Harmony("combat.events.fixture");
        FgnTargetEvents.Install(events,typeof(FiresGhettoNetworkMod.CreatureOwnership).Assembly,FgnCombatOwnership.Watching,FgnCombatOwnership.Observe,
            error=>Call("Fail",error));
        events.Patch(FgnCombatOwnership.Fgn.TryHandOff,prefix:new HarmonyMethod(service.GetMethod("TryPrefix",flags)),
            postfix:new HarmonyMethod(service.GetMethod("TryPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteTarget",flags)));
        MethodInfo despawnHook=service.GetMethod("PlayerDestroyPrefix",flags);
        if(despawnHook!=null)events.Patch(AccessTools.Method(typeof(Player),"OnDestroy"),prefix:new HarmonyMethod(despawnHook));
        Assert(FgnTargetEvents.PatchedMethods==2&&FgnTargetEvents.ObservedWrites==2,"runtime event rewrite covers fixture native store and separate FGN adoption store");
        Client(1);int posts=context.Posts,writes=mob.Writes,transmissions=ZNet.instance.ServerPeer.m_rpc.TargetSent;
        for(int i=0;i<10000;i++){ai.UpdateAI(0.02f);ai.Target=targetPlayer;}
        Assert(context.Posts==posts&&mob.Writes==writes&&ZNet.instance.ServerPeer.m_rpc.TargetSent==transmissions,"unchanged AI updates and repeated target stores cause no observation, queueing or report writes");
        Player third=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();
        ai.Target=third;ai.Target=targetPlayer;
        Assert(context.Posts==posts+1&&mob.Writes==writes,"target events are coalesced and never processed in unfinished native AI work");
        context.Flush();Assert(mob.Writes==writes,"transient target changes ending at the same target produce no report");
        ai.Target=third;context.Flush();
        Assert(ZNet.instance.ServerPeer.m_rpc.TargetId==other.m_characterID&&ZNet.instance.ServerPeer.m_rpc.TargetSent==transmissions+1,"changed player target sends one direct report after native AI completes");
        Server();Call("ReceiveTarget",old,mob.m_uid,other.m_characterID,(long)mob.OwnerRevision);
        Assert(old.m_rpc.Sent==1&&old.m_rpc.Destination==3&&Time.realtimeSinceStartupAsDouble==20,"validated target event starts ordinary FGN transfer immediately without waiting two seconds");
        int sent=old.m_rpc.Sent;Call("ReceiveTarget",old,mob.m_uid,other.m_characterID,(long)mob.OwnerRevision);
        Assert(old.m_rpc.Sent==sent,"duplicate report never rechecks or repeats the same target transfer");
        Call("ReceiveTarget",target,mob.m_uid,target.m_characterID,(long)mob.OwnerRevision);
        ZDOID actual;Assert(FgnCombatOwnership.ReadTarget(mob,out actual)&&actual==other.m_characterID,"nonowner connection cannot inject another mob's target");
        Call("ReceiveTarget",old,mob.m_uid,target.m_characterID,(long)mob.OwnerRevision+1);
        Assert(FgnCombatOwnership.ReadTarget(mob,out actual)&&actual==other.m_characterID,"wrong ownership revision cannot replace a current event report");
        Call("ReceiveTarget",old,mob.m_uid,target.m_characterID,65536L);
        Assert(FgnCombatOwnership.ReadTarget(mob,out actual)&&actual==other.m_characterID,"out-of-range native ushort revision rejected");
        Call("ReceiveTarget",old,mob.m_uid,ZDOID.None,(long)mob.OwnerRevision);
        Assert(FgnCombatOwnership.ReadTarget(mob,out actual)&&actual.IsNone()&&old.m_rpc.Sent==sent,"non-player or lost target invalidates snapshot without directly requesting ownership");

        Setup();Set("collecting",false);Client(1);posts=context.Posts;
        third=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();
        FiresGhettoNetworkMod.TargetSync.Next=third;FiresGhettoNetworkMod.TargetSync.Adopt(ai);
        Assert(context.Posts==posts+1,"successful FGN target adoption emits the same change event");context.Flush();
        Assert(ZNet.instance.ServerPeer.m_rpc.TargetId==other.m_characterID,"FGN-adopted final target appears in the event report");
        posts=context.Posts;FiresGhettoNetworkMod.TargetSync.Adopt(ai);Assert(context.Posts==posts,"repeated same FGN adoption causes no event");
        ai.Target.Alive=false;ai.Target=null;Assert(context.Posts==posts+1,"destroyed Unity target followed by null is still a reference change");context.Flush();
        Assert(ZNet.instance.ServerPeer.m_rpc.TargetId.IsNone(),"destroyed target clears previous player report");

        Setup();Set("collecting",false);Client(1);ai.Target=new Character();context.Flush();
        posts=context.Posts;writes=mob.Writes;transmissions=ZNet.instance.ServerPeer.m_rpc.TargetSent;
        for(int i=0;i<100;i++){ai.Target=new Character();ai.Target=null;}
        Assert(context.Posts==posts&&mob.Writes==writes&&ZNet.instance.ServerPeer.m_rpc.TargetSent==transmissions,"non-player to non-player or null changes never queue or transmit target reports");
        ai.Target=targetPlayer;context.Flush();
        Assert(ZNet.instance.ServerPeer.m_rpc.TargetId==target.m_characterID,"player acquisition after ignored non-player changes still reports normally");

        Setup();Set("collecting",false);Client(1);posts=context.Posts;
        targetPlayer.Destroy();
        Assert(ReferenceEquals(ai.Target,targetPlayer)&&context.Posts==posts+1,"despawn without a target-field store queues invalidation only for the watched local owner");
        context.Flush();Assert(ZNet.instance.ServerPeer.m_rpc.TargetId.IsNone(),"destroyed player retained in the native target field clears its report through the despawn event");
        Server();sent=old.m_rpc.Sent;Call("ReceiveTarget",old,mob.m_uid,ZDOID.None,(long)mob.OwnerRevision);
        Assert(FgnCombatOwnership.ReadTarget(mob,out actual)&&actual.IsNone()&&old.m_rpc.Sent==sent,"despawn invalidation never directly requests a transfer");

        Setup();Set("collecting",false);Client(1);posts=context.Posts;
        third=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();third.Destroy();
        Assert(context.Posts==posts,"unrelated player despawn creates no callback for an unchanged target");
        Client(3);targetPlayer.Destroy();Assert(context.Posts==posts,"non-owner replicas never report despawn invalidation");

        Setup();Set("collecting",false);Client(1);Call("SessionConnection",ZNet.instance.ServerPeer);
        ZDOMan.instance.Data.Remove(mob.m_uid);ZNet.instance.ServerPeer.m_rpc.ReceiveWatch(mob.m_uid,1,(long)mob.OwnerRevision);
        ZDOMan.instance.Data.Add(mob.m_uid,mob);Call("AiAwakePostfix",ai);context.Flush();
        var watchMap=(System.Collections.IDictionary)service.GetField("watching",flags).GetValue(null);object lateWatch=watchMap[mob.m_uid];
        Assert(ReferenceEquals(lateWatch.GetType().GetField("Data",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(lateWatch),mob),"watch arriving before its ZDO binds the exact reference after late AI Awake");
        posts=context.Posts;targetPlayer.Alive=false;Call("PlayerDestroyPrefix",targetPlayer);
        Assert(context.Posts==posts+1,"already fake-null player still invalidates a late-bootstrapped watch without using its detached ZDO");
        context.Flush();Assert(ZNet.instance.ServerPeer.m_rpc.TargetId.IsNone(),"late watch reports destroyed target with no target-field assignment");

        Setup();Set("collecting",false);Client(1);Call("ReceiveWatch",ZNet.instance.ServerPeer,mob.m_uid,0L,(long)mob.OwnerRevision);posts=context.Posts;
        targetPlayer.Destroy();Assert(context.Posts==posts,"unmarked mob is not inspected through player despawn notifications");

        Setup();Set("collecting",false);Client(1);third=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();
        ai.Target=third;writes=mob.Writes;Server();FiresGhettoNetworkMod.CreatureOwnership.Candidates(new[]{old});Call("MarkWatch",mob);
        Client(1);context.Flush();Assert(mob.Writes==writes,"deactivation before queued delivery suppresses the obsolete callback");
        posts=context.Posts;ai.Target=targetPlayer;Assert(context.Posts==posts,"unmarked actual target changes do not schedule reports");
        Server();Assert(!(bool)Call("MarkWatch",mob),"one eligible candidate never arms target events");
        FiresGhettoNetworkMod.CreatureOwnership.Candidates(new[]{old,target});target.Matched=false;
        Assert(!(bool)Call("MarkWatch",mob),"unmatched candidate does not count as second potential participant");
        target.Matched=true;Assert((bool)Call("MarkWatch",mob),"two compatible active candidates arm the mob");
        int watchSent=old.m_rpc.WatchSent;Call("MarkWatch",mob);
        Assert(old.m_rpc.WatchSent==watchSent+1,"only missing initial acknowledgement retries watch in original scan");
        Call("ReceiveTarget",old,mob.m_uid,target.m_characterID,(long)mob.OwnerRevision);watchSent=old.m_rpc.WatchSent;Call("MarkWatch",mob);
        Assert(old.m_rpc.WatchSent==watchSent,"acknowledged unchanged watch sends nothing in later scans");
        var serverPeer=ZNet.instance.ServerPeer;Client(1);Call("SessionConnection",serverPeer);serverPeer.m_rpc.Receive(2);
        Assert(!FgnCombatOwnership.Watching(ai),"old protocol acknowledgement cannot enable new event client");

        Setup();Set("collecting",false);mob.SetOwner(3);Call("MarkWatch",mob);
        var pendingPeer=other;long expected=mob.OwnerRevision;serverPeer=ZNet.instance.ServerPeer;
        // Simulate Watch reaching the client before the independent native ownership packet.
        mob.SetOwner(1);mob.OwnerRevision=0;Client(3);Call("SessionConnection",serverPeer);posts=context.Posts;
        serverPeer.m_rpc.ReceiveWatch(mob.m_uid,3,expected);
        Assert(context.Posts==posts,"early watch does not observe a wrong local owner/revision");
        mob.SetOwner(3);mob.OwnerRevision=(uint)expected;Server();watchSent=pendingPeer.m_rpc.WatchSent;Call("MarkWatch",mob);
        Assert(pendingPeer.m_rpc.WatchSent==watchSent+1,"initial unacknowledged watch is retried after ownership data becomes ready");
        Client(3);int initialReports=serverPeer.m_rpc.TargetSent;serverPeer.m_rpc.ReceiveWatch(mob.m_uid,3,expected);context.Flush();
        Assert(serverPeer.m_rpc.TargetSent==initialReports+1&&serverPeer.m_rpc.TargetRevision==expected,"unchanged initial target is reported after delayed native data without an AI polling hook");
        Server();Call("ReceiveTarget",pendingPeer,mob.m_uid,serverPeer.m_rpc.TargetId,serverPeer.m_rpc.TargetRevision);
        Assert(IsAcknowledged(mob),"initial ACK only accepted from actual current compatible owner and exact revision");

        Setup();target.Ping=400;bool moved;Handle(out moved);ServerAccept();Set("collecting",false);
        ZNet.instance.Peers.Remove(old);ZNet.instance.Peers.Remove(other);FiresGhettoNetworkMod.CreatureOwnership.Candidates(ZNet.instance.Peers);
        var body=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();body.Attack=true;
        Call("MarkWatch",mob);Call("BalancePostfix",true);
        Assert(mob.GetOwner()==100&&!FgnCombatOwnership.Watching(ai),"solo unwatch keeps a server lease safe while its ongoing attack blocks return");
        ai.Target=null;body.Attack=false;Time.realtimeSinceStartupAsDouble+=2;object[] retry={false};Call("BalancePrefix",retry);Call("BalancePostfix",true);
        Assert(mob.GetOwner()==2,"unwatched own lease captures changed authoritative target only during existing balance cleanup and returns safely");
        Assert(((System.Collections.IDictionary)service.GetField("receivedTargets",flags).GetValue(null)).Count==0,"completed unmonitored server return releases its received-report cache");
        Setup();target.Ping=400;Handle(out moved);ServerAccept();Set("collecting",true);mob.Set(ZDOVars.s_tamed,1);Handle(out moved);
        Assert(((System.Collections.IDictionary)service.GetField("receivedTargets",flags).GetValue(null)).Count==0,"becoming ineligible removes exception report instead of retaining cache indefinitely");

        Setup();Set("collecting",false);Client(1);ai.Target=null;writes=mob.Writes;FgnTargetEvents.Reset();context.Flush();
        Assert(mob.Writes==writes,"world epoch reset invalidates queued target notification");
        Setup();Set("collecting",false);Client(1);context.ThrowPost=true;ai.Target=null;context.ThrowPost=false;
        Assert(ai.Target==null&&!FgnCombatOwnership.Enabled,"failed queue preserves original target store and disables only optional feature");
        Setup();System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Threading.SynchronizationContext());
        bool refused=false;try{FgnTargetEvents.Install(events,typeof(FiresGhettoNetworkMod.CreatureOwnership).Assembly,FgnCombatOwnership.Watching,FgnCombatOwnership.Observe,error=>Call("Fail",error));}
        catch(NotSupportedException){refused=true;}Assert(refused,"no generic ThreadPool synchronization-context fallback allowed");
        System.Threading.SynchronizationContext.SetSynchronizationContext(context);

        Setup();Set("collecting",false);old.Ping=400;ai.Target=null;Client(1);FgnCombatOwnership.Observe(ai);Server();
        Call("ReceiveTarget",old,mob.m_uid,ZDOID.None,0L);
        Assert(!FgnCombatOwnership.Fgn.TryTransfer(mob,20)&&FiresGhettoNetworkMod.CreatureOwnership.OwnedSince(mob.m_uid)==20,"no-player target retains original FGN lower-ping ownership hold");
        Time.realtimeSinceStartupAsDouble=29.9;Assert(!FgnCombatOwnership.Fgn.TryTransfer(mob,29.9)&&old.m_rpc.Sent==0,"original lower-ping path does not move before ten-second hold");
        Time.realtimeSinceStartupAsDouble=30;Assert(!FgnCombatOwnership.Fgn.TryTransfer(mob,30)&&old.m_rpc.Sent==0,"first lower-ping win does not move a noncombat mob");
        Time.realtimeSinceStartupAsDouble=32;Assert(FgnCombatOwnership.Fgn.TryTransfer(mob,32)&&old.m_rpc.Destination==3,"second lower-ping win preserves original noncombat selection");

        // Actual event calls share stock per-owner counters and one global 16-request budget.
        Setup();Set("collecting",false);var fourth=Peer(4,50,new Vector3(103,0,100));
        ZDOMan.instance.GetZDO(fourth.m_characterID).Set("toomanylags.fgn.ai.protocol".GetStableHashCode(),5L);
        FiresGhettoNetworkMod.CreatureOwnership.Candidates(ZNet.instance.Peers);
        for(int i=0;i<20;i++)
        {
            long source=i<6?1L:(i<12?3L:4L);ZNetPeer sender=ZNet.instance.GetPeer(source);
            ZDO data=new ZDO(new ZDOID(79,(uint)(i+1)),source){Position=new Vector3(100,0,100)};ZDOMan.instance.Data.Add(data.m_uid,data);
            var obj=new GameObject();var view=obj.Add(new ZNetView{Data=data});obj.Add(new Character());var creature=obj.Add(new MonsterAI());creature.SetView(view);obj.Add(new ZSyncTransform());ZNetScene.instance.Views.Add(data,view);
            creature.Target=targetPlayer;Server();Call("MarkWatch",data);Client(source);FgnCombatOwnership.Observe(creature);Server();
            Call("ReceiveTarget",sender,data.m_uid,target.m_characterID,0L);
        }
        Assert(old.m_rpc.Sent==6&&other.m_rpc.Sent==6&&fourth.m_rpc.Sent==4,"normal-ping immediate events preserve six-per-source and global sixteen limits across multiple owners");
        Assert((int)service.GetField("passMoves",flags).GetValue(null)==16,"immediate and original scan use the same sixteen-move counter");
        // A still-eligible watched mob skipped by stock scan's cap stays watched without reactivation traffic.
        watchSent=old.m_rpc.WatchSent;Call("BalancePostfix",true);
        Assert(FgnCombatOwnership.ShouldOverride(mob.m_uid)&&old.m_rpc.WatchSent==watchSent,"cap-unvisited acknowledged watch retains valid subscription without network churn");
        events.UnpatchSelf();FgnCombatOwnership.Stop();System.Threading.SynchronizationContext.SetSynchronizationContext(null);
    }
}
