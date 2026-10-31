using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

internal static class MobCombatStateTests
{
    private const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private const string Cap="TooManyLags_CombatCapability",StateRpc="TooManyLags_CombatCheckpoint",EventRpc="TooManyLags_CombatLateEvent";
    private static int checks;private static ZDO data;private static Character actor;private static ZNetPeer peer2,peer3;
    private static HitData nestedOuter;
    private static List<byte[]> packets;private static List<long> destinations;
    private static object Call(string name,params object[] args){return typeof(MobCombatState).GetMethod(name,All).Invoke(null,args);}
    private static IDictionary Map(string name){return (IDictionary)typeof(MobCombatState).GetField(name,All).GetValue(null);}
    private static int Waiting(){object set=typeof(MobCombatState).GetField("serverWaiting",All).GetValue(null);return (int)set.GetType().GetProperty("Count").GetValue(set,null);}
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    private static ZPackage Packet(byte[] bytes){return new ZPackage(bytes);}
    private static void New(bool server,long local,long owner)
    {
        MobCombatState.Stop();Time.realtimeSinceStartupAsDouble=0;Time.time=1000;ZDOMan.Session=local;ZDOMan.instance=new ZDOMan();ZNet.instance=new ZNet{Server=server};ZNetScene.instance=new ZNetScene();
        FiresGhettoNetworkMod.CreatureOwnership.VetoReason=null;
        data=new ZDO{m_uid=new ZDOID{UserID=55,ID=9},Owner=owner};ZDOMan.instance.Values[data.m_uid]=data;
        actor=new Character{Push=new Vector3(2,3,4),Velocity=new Vector3(5,0,6),Turn=.7f,TurnChange=-.3f,Stagger=7,Backstab=990,LastHit=new HitData{Damage=12,Attacker=99}};
        actor.Effects=new SEMan(actor);ZNetView view=new ZNetView{Data=data};BaseAI ai=new BaseAI();
        var components=new Dictionary<Type,Component>{{typeof(ZNetView),view},{typeof(Character),actor},{typeof(BaseAI),ai}};
        actor.Components=view.Components=ai.Components=components;actor.View=view;ZNetScene.instance.Values[data]=view;
        peer2=new ZNetPeer{m_uid=2,m_server=!server};peer3=new ZNetPeer{m_uid=3};ZNet.instance.Peers.Add(peer2);ZNet.instance.Peers.Add(peer3);
        packets=new List<byte[]>();destinations=new List<long>();
        peer2.m_rpc.Send=delegate(string name,object[] args){if(name==StateRpc||name==EventRpc){packets.Add(((ZPackage)args[1]).Bytes());destinations.Add(2);}};
        peer3.m_rpc.Send=delegate(string name,object[] args){if(name==StateRpc||name==EventRpc){packets.Add(((ZPackage)args[1]).Bytes());destinations.Add(3);}};
        MobCombatState.Start(typeof(MobCombatStateTests).Assembly);Check(typeof(MobCombatState).GetField("hooks",All).GetValue(null)!=null,"hooks failed");
        peer2.m_rpc.Receive(Cap,-4);peer3.m_rpc.Receive(Cap,-4);
        Check(ZNet.instance.Peers.Count==2,"peer context changed");
    }
    private static byte[] State(long source,long destination,uint revision,int prefab,float backstabValue)
    {
        ZPackage p=new ZPackage();p.Write((byte)4);p.Write(source);p.Write(destination);p.Write(revision);p.Write(prefab);p.Write(new Vector3(2,3,4));p.Write(7f);p.Write(backstabValue);
        p.Write(new Vector3(5,0,6));p.Write(.7f);p.Write(-.3f);p.Write(true);
        new HitData{Damage=12,Attacker=99}.Serialize(ref p);p.Write(false);p.Write(.4f);p.Write(ZDOID.None);p.Write(0f);return p.Bytes();
    }
    private static byte[] Event(long issuer,long token,uint anchor,uint expected,int hops,int kind)
    {
        ZPackage p=new ZPackage();p.Write((byte)4);p.Write(issuer);p.Write(token);p.Write(anchor);p.Write(expected);p.Write(data.Prefab);p.Write(88L);p.Write((byte)kind);p.Write((byte)hops);p.Write(5f);
        if(kind==1)new HitData{Damage=6,Attacker=88,Push=new Vector3(1,0,0),Status=123}.Serialize(ref p);
        else if(kind==2){p.Write(123);p.Write(true);p.Write(2);p.Write(4f);p.Write(0);}else if(kind==3)p.Write(new Vector3(1,0,0));else{p.Write(4f);p.Write(true);}return p.Bytes();
    }
    private static void CheckpointBeforeGameplay()
    {
        New(false,3,2);byte[] checkpoint=State(2,3,4,data.Prefab,290);
        peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(checkpoint));Check(Map("pending").Count==1,"valid checkpoint not pending");
        actor.Push=new Vector3();actor.Velocity=new Vector3(-8,1,2);actor.Turn=-5;actor.TurnChange=8;actor.Stagger=0;actor.LastHit=null;data.SetOwner(3);actor.CustomFixedUpdate(.02f);
        Check(actor.SeenStagger==7&&actor.Push.y==3,"first fixed step ran before checkpoint");Check(actor.LastHit.Attacker==99&&actor.Hits==0&&actor.Health==100,"last hit replayed as damage");
        Check(actor.SeenVelocity.x==5&&actor.SeenVelocity.y==0&&actor.SeenVelocity.z==6,"first walking step retained stale acceleration or counted push twice");
        Check(actor.SeenTurn==.7f&&actor.SeenTurnChange==-.3f,"first turn used stale animation accumulators");
        Check(actor.RotationFactor==.4f,"first flying turn lost its native rotation acceleration factor");
        Check(Math.Abs(actor.Backstab-990)<.001f,"backstab remaining not clock-relative");
        actor.Stagger=11;ZDOMan.instance.RPC_ZDOData(delegate{});actor.CustomFixedUpdate(.02f);Check(actor.Stagger==11&&Map("pending").Count==0,"checkpoint applied more than once");
        peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(checkpoint));Check(Map("pending").Count==0,"late checkpoint rewinds current owner");
        actor.Effects.GetStatusEffects().Add(new StatusEffect());MobCombatState.BeforeTransfer(data,2);
        Check(packets.Count==1&&data.Owner==3,"active effect blocked source checkpoint/changed owner early");
        New(false,3,2);actor.Effects.GetStatusEffects().Add(new StatusEffect{Age=999});
        peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(checkpoint));data.SetOwner(3);actor.CustomFixedUpdate(.02f);
        Check(actor.Effects.GetStatusEffects().Count==0,"empty effect snapshot retained stale effects from an earlier owner epoch");
        New(false,3,2);data.SetOwner(3);actor.Velocity=new Vector3(12,4,8);actor.Stagger=19;actor.CustomFixedUpdate(.02f);
        peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(checkpoint));actor.CustomFixedUpdate(.02f);
        Check(Map("pending").Count==0&&actor.SeenVelocity.x==12&&actor.SeenVelocity.y==4&&actor.SeenStagger==19,"late first checkpoint overwrote already running destination simulation");
    }
    private static void SourceAndLegacy()
    {
        New(true,1,1);MobCombatState.BeforeTransfer(data,2);Check(packets.Count==1&&data.Owner==1&&data.OwnerRevision==4,"checkpoint did not precede owner write");
        object decoded=Call("DecodeState",data.m_uid,Packet(packets[0]));Check(decoded!=null,"own checkpoint fails parser");
        var legacyHooks=new Harmony("com.Fire.FiresGhettoNetworkMod");legacyHooks.Patch(AccessTools.Method(typeof(Character),"RPC_Damage"),prefix:new HarmonyMethod(typeof(FiresGhettoNetworkMod.CreatureOwnership),"OnDamage"));
        try
        {
            FiresGhettoNetworkMod.CreatureOwnership.DamagePrefixes=0;
            Check(!MobCombatState.HandlesLateDamage(data),"current owner cannot bypass native routing as an old source");
            data.SetOwner(2);Check(MobCombatState.HandlesLateDamage(data),"known updated outgoing source must bypass the legacy one-receipt damage relay");
            actor.Damage(88,new HitData{Damage=4,Attacker=1});
            Check(FiresGhettoNetworkMod.CreatureOwnership.DamagePrefixes==0&&actor.Hits==0,"stale native/FGN damage prefix ran twice");Check(packets.Count==2&&destinations[1]==2,"local-attacker damage not forwarded");
            actor.Effects.Add(88,123,true,1,1,0);Check(packets.Count==3&&actor.Effects.Adds==0,"separate status RPC lost at old owner");
            Time.realtimeSinceStartupAsDouble=6;Check(!MobCombatState.HandlesLateDamage(data),"expired combat route cannot suppress legacy fallback");
            actor.Damage(88,new HitData{Damage=1});Check(packets.Count==3&&FiresGhettoNetworkMod.CreatureOwnership.DamagePrefixes==1,"expired route forwarded");
        }
        finally{legacyHooks.UnpatchSelf();}
        New(true,1,1);peer2.Ready=false;MobCombatState.BeforeTransfer(data,2);Check(packets.Count==0,"legacy/unready endpoint got unknown checkpoint");
    }
    private static void EarlyRpcAndOrder()
    {
        New(false,3,2);peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(2,3,4,data.Prefab,290)));
        actor.Stagger=0;actor.Push=new Vector3();actor.Damage(88,new HitData{Damage=6,Attacker=88,Push=new Vector3(1,0,0)});
        Check(actor.Hits==0&&Map("awaiting").Count==1,"pre-owner event lost instead of queued");
        ZDOMan.instance.RPC_ZDOData(delegate{data.SetOwner(3);});
        Check(actor.Hits==1&&actor.Health==94&&actor.Stagger==13&&actor.Push.x==1,"checkpoint overwrote newly processed damage");
        actor.CustomFixedUpdate(.02f);ZDOMan.instance.RPC_ZDOData(delegate{});Check(actor.Hits==1,"queued event replayed");
        New(false,3,2);peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(2,3,4,data.Prefab,290)));
        actor.Effects.Add(88,123,true,1,2,0);Check(actor.Effects.Adds==0,"early effect ran against stale owner");
        ZDOMan.instance.RPC_ZDOData(delegate{data.SetOwner(3);});Check(actor.Effects.Adds==1,"forwarded effect missing");
        MobCombatState.BeforeTransfer(data,2);Check(packets.Count==1,"newly applied effect blocked next handoff");
    }
    private static void ChainAndDedup()
    {
        New(true,1,2);byte[] state=State(2,3,4,data.Prefab,290);peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(state));Check(packets.Count==1,"checkpoint not relayed");
        byte[] hit=Event(2,1,4,5,0,1);peer2.m_rpc.Receive(EventRpc,data.m_uid,Packet(hit));Check(packets.Count==1,"event sent before canonical owner commit");
        Check(Waiting()==1,"uncommitted event not tracked as unresolved server work");
        ZDOMan.instance.RPC_ZDOData(delegate{data.SetOwner(3);});Check(packets.Count==2&&destinations[1]==3,"canonical destination not selected");
        Check(Waiting()==0&&Map("events").Count==1,"sent dedup tombstone remained in world-packet work queue");
        byte[] sent=packets[1];peer3.m_rpc.Receive(EventRpc,data.m_uid,Packet(sent));Check(packets.Count==2,"unchanged owner revision created bounce");
        Check(Waiting()==1,"returned event did not rejoin unresolved ownership work");
        peer3.m_rpc.Receive(EventRpc,data.m_uid,Packet(sent));ZDOMan.instance.RPC_ZDOData(delegate{});Check(packets.Count==2,"duplicate return created second forward");
        // B -> A before the late request arrives. A is now canonical again;
        // the same event ID may return to A only with a strictly newer revision.
        peer3.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(3,2,5,data.Prefab,290)));
        ZDOMan.instance.RPC_ZDOData(delegate{data.SetOwner(2);});Check(packets.Count==4&&destinations[3]==2,"A->B->A chain event did not follow newer canonical revision");
        peer2.m_rpc.Receive(EventRpc,data.m_uid,Packet(hit));Check(packets.Count==4,"duplicate originating event resent");
        byte[] again=packets[3];peer2.m_rpc.Receive(EventRpc,data.m_uid,Packet(again));Check(packets.Count==4,"cycle bounced at same revision");
        // Finally transfer to the server. Canonical native replication resumes
        // the one saved event; no retransmission timer or damage checkpoint replay.
        peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(2,1,6,data.Prefab,290)));
        ZDOMan.instance.RPC_ZDOData(delegate{data.SetOwner(1);});Check(actor.Hits==1&&actor.Health==94&&actor.LastHit.Status==123,"chain hit/status payload not delivered exactly once");
        Check(Waiting()==0&&Map("events").Count==1,"delivered dedup tombstone remained in world-packet work queue");
        ZDOMan.instance.RPC_ZDOData(delegate{});peer2.m_rpc.Receive(EventRpc,data.m_uid,Packet(again));Check(actor.Hits==1,"duplicate chain packet reapplied damage");
    }
    private static void ValidationAndBounds()
    {
        New(false,3,2);peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(2,3,4,999,290)));Check(Map("pending").Count==0,"prefab mismatch accepted");
        peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(2,3,4,data.Prefab,Single.NaN)));Check(Map("pending").Count==0,"nonfinite timer accepted");
        peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(2,3,4,data.Prefab,290)));data.SetOwner(1);actor.CustomFixedUpdate(.02f);Check(Map("pending").Count==0,"wrong destination applied");
        New(false,3,2);peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(2,3,4,data.Prefab,290)));Time.realtimeSinceStartupAsDouble=6;
        data.SetOwner(3);actor.Stagger=0;actor.CustomFixedUpdate(.02f);Check(actor.Stagger==0&&Map("pending").Count==0,"expired state applied");
        New(false,3,2);peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(2,3,4,data.Prefab,290)));ZDOMan.instance=new ZDOMan();actor.CustomFixedUpdate(.02f);
        Check(Map("pending").Count==0&&Map("events").Count==0,"old world checkpoint retained");
        MobCombatState.Stop();Check(Map("history").Count==0&&Map("outgoing").Count==0,"stop retained transient state");
    }
    private static void StaggerAndHeal()
    {
        New(true,1,1);MobCombatState.BeforeTransfer(data,2);data.SetOwner(2);actor.AddressedStagger(88,new Vector3(1,0,0),false);actor.HealRpc(88,4,true);
        Check(actor.Staggers==0&&actor.Heals==0&&packets.Count==3,"old replica staggered or heal not forwarded");
        actor.AddressedStagger(88,new Vector3(1,0,0),true);Check(actor.Staggers==1&&packets.Count==3,"broadcast stagger rerouted from known old source");
        New(false,3,2);peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(2,3,4,data.Prefab,290)));
        actor.AddressedStagger(88,new Vector3(1,0,0),false);actor.HealRpc(88,4,true);Check(actor.Staggers==0&&actor.Heals==0,"early stagger/heal ran before ownership");
        ZDOMan.instance.RPC_ZDOData(delegate{data.SetOwner(3);});Check(actor.Staggers==1&&actor.Heals==1&&actor.Health==104,"early stagger/heal lost or duplicated");
        actor.CustomFixedUpdate(.02f);Check(actor.Staggers==1&&actor.Heals==1&&Map("awaiting").Count==0,"delivered events retained in per-actor hot path");
        New(false,3,2);actor.StaggerRpc(88,new Vector3(1,0,0));Check(actor.Staggers==1,"unrelated observer native stagger altered");
        MobCombatState.BeforeTransfer(data,1);actor.AddressedStagger(88,new Vector3(1,0,0),true);Check(actor.Staggers==2,"broadcast stagger altered");
        peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(2,3,4,data.Prefab,290)));data.SetOwner(3);
        peer2.m_rpc.Receive(EventRpc,data.m_uid,Packet(Event(1,71,4,5,1,3)));peer2.m_rpc.Receive(EventRpc,data.m_uid,Packet(Event(1,71,4,5,1,3)));
        peer2.m_rpc.Receive(EventRpc,data.m_uid,Packet(Event(1,72,4,5,1,4)));peer2.m_rpc.Receive(EventRpc,data.m_uid,Packet(Event(1,72,4,5,1,4)));
        Check(actor.Staggers==3&&actor.Heals==1,"duplicate envelope replayed stagger/heal");
    }
    private static void SuppressionAndExpiry()
    {
        New(true,1,1);MobCombatState.BeforeTransfer(data,2);data.SetOwner(2);
        var nestedHooks=new Harmony("test.nested.combat.prefix");var fgnHooks=new Harmony("com.Fire.FiresGhettoNetworkMod");
        try
        {
            var nested=new HarmonyMethod(typeof(MobCombatStateTests),"NestedDamage");nested.priority=Priority.High;
            nestedHooks.Patch(AccessTools.Method(typeof(Character),"RPC_Damage"),prefix:nested);
            fgnHooks.Patch(AccessTools.Method(typeof(Character),"RPC_Damage"),prefix:new HarmonyMethod(typeof(FiresGhettoNetworkMod.CreatureOwnership),"OnDamage"));
            FiresGhettoNetworkMod.CreatureOwnership.DamagePrefixes=0;nestedOuter=new HitData{Damage=1};actor.Damage(88,nestedOuter);
            Check(packets.Count==3&&actor.Hits==0,"nested late hit was lost or independently forwarded twice");
            Check(FiresGhettoNetworkMod.CreatureOwnership.DamagePrefixes==0,"inner damage erased suppression of outer FGN prefix");
            Check(typeof(MobCombatState).GetField("suppressedHit",All).GetValue(null)==null,"outer finalizer retained nested suppression");
            Check(FiresGhettoNetworkMod.CreatureOwnership.OnDamage(actor,new HitData())&&FiresGhettoNetworkMod.CreatureOwnership.DamagePrefixes==1,"finished scope suppressed an unrelated later hit");
        }
        finally{nestedOuter=null;nestedHooks.UnpatchSelf();fgnHooks.UnpatchSelf();}
        New(false,3,2);
        for(uint i=1;i<=1025;i++)peer2.m_rpc.Receive(StateRpc,new ZDOID{UserID=55,ID=i+100},Packet(State(2,3,4,data.Prefab,290)));
        Check(Map("pending").Count==1024,"unloaded-destination checkpoint queue is unbounded");
        // Held native events have no delivery acknowledgement yet: one warning
        // on expiry, then no repeated work. Already sent envelopes are quiet.
        New(false,3,2);peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(State(2,3,4,data.Prefab,290)));actor.Damage(88,new HitData{Damage=1});
        int warnings=Plugin.Warnings;Time.realtimeSinceStartupAsDouble=6;ZDOMan.instance.RPC_ZDOData(delegate{});
        Check(Plugin.Warnings==warnings+1&&Map("events").Count==0&&Map("awaiting").Count==0&&Waiting()==0,"undelivered expiry not reported/cleaned");
        ZDOMan.instance.RPC_ZDOData(delegate{});Check(Plugin.Warnings==warnings+1,"expiry warning repeated");
    }
    private static void NestedDamage(Character __instance,HitData hit)
    {if(ReferenceEquals(hit,nestedOuter))__instance.Damage(88,new HitData{Damage=2});}
    private static void ActiveEffectTransport()
    {
        New(true,1,1);actor.Effects.GetStatusEffects().Add(new StatusEffect{Age=4,Remaining=27,Next=.2f});
        Check(FiresGhettoNetworkMod.CreatureOwnership.Veto(data,2)==null,"active effects installed a native ownership veto");
        MobCombatState.BeforeTransfer(data,2);byte[] snapshot=packets[0];
        MobCombatState.CompleteTransfer(data,2);Check(actor.Effects.GetStatusEffects().Count==1,"source effects detached before successful owner write");
        data.SetOwner(2);MobCombatState.CompleteTransfer(data,2);
        Check(actor.Effects.GetStatusEffects().Count==0&&MobStatusState.Destination==2,"source effects retained after successful owner write");
        New(false,2,1);actor.Effects.GetStatusEffects().Add(new StatusEffect{Age=999});
        peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(snapshot));data.SetOwner(2);actor.Damage(88,new HitData{Damage=4});
        var effect=actor.Effects.GetStatusEffects()[0];
        Check(actor.Effects.GetStatusEffects().Count==1&&effect.Age==4&&effect.Remaining==27&&effect.Next==.2f,"effect state was restarted, duplicated or not restored before first destination hit");
        Check(actor.Health==96&&actor.Hits==1,"checkpoint replayed damage or upfront effect healing");
        effect.Remaining=19;actor.CustomFixedUpdate(.02f);peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(snapshot));
        Check(effect.Remaining==19&&actor.Effects.GetStatusEffects().Count==1,"late checkpoint overwrote running effect");
        New(true,1,1);actor.Effects.GetStatusEffects().Add(new StatusEffect());
        FiresGhettoNetworkMod.CreatureOwnership.VetoReason="existing native refusal";
        Check(!FiresGhettoNetworkMod.CreatureOwnership.Commit(data,2,MobCombatState.BeforeTransfer)&&actor.Effects.GetStatusEffects().Count==1,"refused native commit detached effects");
        FiresGhettoNetworkMod.CreatureOwnership.VetoReason=null;
        Check(FiresGhettoNetworkMod.CreatureOwnership.Commit(data,2,MobCombatState.BeforeTransfer)&&data.Owner==2&&packets.Count==1&&actor.Effects.GetStatusEffects().Count==0,"active effect prevented otherwise valid native commit");
    }
    private static void NativeTargetCountdown()
    {
        New(false,3,2);
        MonsterAI ai=new MonsterAI();ai.Components=actor.Components;actor.Components[typeof(MonsterAI)]=ai;
        Player victim=new Player();ZDO victimData=new ZDO{m_uid=new ZDOID{UserID=9,ID=99},Owner=3};ZNetView view=new ZNetView{Data=victimData};
        victim.View=view;victim.Components=new Dictionary<Type,Component>{{typeof(ZNetView),view},{typeof(Character),victim}};view.Components=victim.Components;
        ZDOMan.instance.Values[victimData.m_uid]=victimData;ZNetScene.instance.Values[victimData]=view;
        byte[] bytes=State(2,3,4,data.Prefab,290);byte[] changed=new byte[bytes.Length];Array.Copy(bytes,changed,bytes.Length);
        using(var stream=new System.IO.MemoryStream(changed))using(var writer=new System.IO.BinaryWriter(stream))
        {stream.Position=changed.Length-16;writer.Write(victimData.m_uid.UserID);writer.Write(victimData.m_uid.ID);writer.Write(1.8f);}
        peer2.m_rpc.Receive(StateRpc,data.m_uid,Packet(changed));data.SetOwner(3);ai.TargetDelay=0;ai.Static=new StaticTarget();ai.UpdateAI(.02f);
        Check(ReferenceEquals(ai.Target,victim)&&ai.Static==null&&Math.Abs(ai.TargetDelay-1.78f)<.001f,"new owner searched again or kept a stale static target before restoring the real target and native remaining search interval");
        ai.TargetDelay=.3f;ai.UpdateAI(.02f);Check(Math.Abs(ai.TargetDelay-.28f)<.001f,"target checkpoint reapplied a permanent retarget lock");
    }
    public static void Main()
    {
        try{CheckpointBeforeGameplay();SourceAndLegacy();EarlyRpcAndOrder();ChainAndDedup();ValidationAndBounds();StaggerAndHeal();SuppressionAndExpiry();ActiveEffectTransport();NativeTargetCountdown();Console.WriteLine("PASS: "+checks+" production combat-state checks: checkpoints, effects, early/late damage/stagger/heal, A-B-A revision fences and at-most-once routing.");}
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}finally{MobCombatState.Stop();}
    }
}
