using System;
using System.Collections;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

internal static partial class FgnCombatTests
{
    private static GameObject FactionPrefab(int hash,Character.Faction faction,bool tameable)
    {
        GameObject prefab=new GameObject{activeInHierarchy=false};
        prefab.Add(new Character{Team=faction});
        if(tameable)prefab.Add(new Tameable{m_unsummonDistance=50f});
        ZNetScene.instance.Prefabs[hash]=prefab;return prefab;
    }
    private static IDictionary FactionCache()
    {return (IDictionary)service.GetField("prefabFactionActors",flags).GetValue(null);}
    private static void FriendlyCreatureTests()
    {
        Setup();Character actor=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();
        Assert(!FriendlyCreatures.IsFriendly(null)&&!FriendlyCreatures.IsFriendly(new Character{Alive=false,Team=Character.Faction.Players}),"null/destroyed creatures are safe for pure friendly filter");
        Assert(!FriendlyCreatures.IsFriendly(new Player{Team=Character.Faction.Players}),"actual players never enter friendly-creature exclusion");
        actor.Team=Character.Faction.AnimalsVeg;actor.Tamed=true;
        Assert(!FriendlyCreatures.IsFriendly(actor),"ordinary tamed animal keeps native raw faction and is not a friendly summon");
        mob.Set(ZDOVars.s_tamed,1L);
        Assert(!(bool)Call("Eligible",mob),"ordinary tamed creature keeps original AI policy exclusion");

        Setup();actor=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();
        actor.Team=Character.Faction.PlayerSpawned;actor.gameObject.Add(new Tameable{m_unsummonDistance=50f});
        bool moved;
        Assert(!FriendlyCreatures.IsFriendly(actor)&&(bool)Call("Eligible",mob),"hostile PlayerSpawned summon remains eligible despite an unsummon marker");
        Assert(Handle(out moved)&&!moved&&FgnCombatOwnership.ShouldOverride(mob.m_uid),"hostile summon retains ordinary current-player-target FGN path");
        target.Ping=400;
        Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==100,"hostile summon retains guarded high-ping server fallback");

        foreach(bool tameable in new[]{false,true})
        {
            Setup();ZNetView view=ZNetScene.instance.FindInstance(mob);actor=view.GetComponent<Character>();
            actor.Team=Character.Faction.Players;actor.name=tameable?"Skeleton_Friendly":"staff_greenroots_tentaroot";
            if(tameable)actor.gameObject.Add(new Tameable{m_unsummonDistance=50f});
            Assert(FriendlyCreatures.IsFriendly(actor)&&!actor.IsTamed(),"native friendly faction covers "+actor.name+" without requiring tame state");
            Assert(!(bool)Call("MarkWatch",mob)&&!FgnCombatOwnership.ShouldOverride(mob.m_uid),"friendly "+actor.name+" is unmarked and cannot override last-attacker policy");
            ZDOMan.Session=1;ZNet.instance.Dedicated=false;Set("serverConfirmed",true);
            int writes=mob.Writes;FgnCombatOwnership.Observe(ai);
            Assert(!FgnCombatOwnership.Watching(ai)&&mob.Writes==writes,"friendly "+actor.name+" never publishes an owner target report");
            Call("ReceiveWatch",ZNet.instance.ServerPeer,mob.m_uid,1L,(long)mob.OwnerRevision);
            Assert(!FgnCombatOwnership.Watching(ai)&&((IDictionary)service.GetField("watching",flags).GetValue(null)).Count==0,"client ignores a friendly watch including roots without Tameable");
            ZDOMan.Session=100;ZNet.instance.Dedicated=true;target.Ping=400;
            Assert(!Handle(out moved)&&!moved&&old.m_rpc.Sent==0&&mob.OwnerWrites==0&&NativePup.Checks==0,"friendly "+actor.name+" is excluded before FGN handoff and readiness work");
            Assert(Call("LoadedCharacter",mob)==null,"server takeover cannot load a friendly creature into its policy");
            object[] veto={mob,2L,null};Call("VetoPostfix",veto);
            Assert((string)veto[2]=="friendly creature","queued stock FGN handoff to a friendly creature is vetoed");
            veto=new object[]{mob,2L,"another mod's veto"};Call("VetoPostfix",veto);
            Assert((string)veto[2]=="another mod's veto","friendly exclusion preserves earlier native/mod vetoes");
            FgnCombatOwnership.Enabled=false;veto=new object[]{mob,2L,null};Call("VetoPostfix",veto);
            Assert(Handle(out moved)&&veto[2]==null,"disabled AI module leaves native FGN selection and veto intact");
        }

        Setup();actor=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();
        GameObject loadedObject=actor.gameObject;FactionPrefab(mob.Prefab,Character.Faction.Players,false);
        Assert(!(bool)Call("IsFriendlyCreature",mob),"live hostile faction wins over friendly prefab classification");
        int loadedReads=loadedObject.ComponentQueries;actor.Team=Character.Faction.Players;
        Assert((bool)Call("IsFriendlyCreature",mob),"cached live actor reference observes a changed friendly faction");
        for(int i=0;i<10000;i++)Call("IsFriendlyCreature",mob);
        Assert(loadedObject.ComponentQueries==loadedReads,"ten thousand loaded friendly checks repeat no GetComponent");
        actor.Team=Character.Faction.PlayerSpawned;
        Assert(!(bool)Call("IsFriendlyCreature",mob),"cached live actor can become hostile without prefab cache poisoning");

        Setup();ZNetScene.instance.Views.Remove(mob);
        GameObject rootPrefab=FactionPrefab(mob.Prefab,Character.Faction.Players,false);
        Assert((bool)Call("IsFriendlyCreature",mob)&&!(bool)Call("MarkWatch",mob),"unloaded friendly root is excluded through inactive prefab's native faction");
        int prefabReads=rootPrefab.ComponentQueries,prefabLookups=ZNetScene.instance.PrefabCalls;
        for(int i=0;i<10000;i++)Call("IsFriendlyCreature",mob);
        Assert(rootPrefab.ComponentQueries==prefabReads&&ZNetScene.instance.PrefabCalls==prefabLookups,"ten thousand cached prefab checks repeat no GetComponent or GetPrefab");
        Character template=rootPrefab.GetComponent<Character>();prefabReads=rootPrefab.ComponentQueries;
        template.Team=Character.Faction.PlayerSpawned;
        Assert(!(bool)Call("IsFriendlyCreature",mob)&&rootPrefab.ComponentQueries==prefabReads,"prefab cache re-reads native faction rather than storing an immutable friendly bool");
        template.Team=Character.Faction.Players;
        Assert((bool)Call("IsFriendlyCreature",mob),"prefab reference observes restored friendly faction");

        Setup();ZNetScene.instance.Views.Remove(mob);mob.Prefab=8001;
        Assert(!(bool)Call("IsFriendlyCreature",mob)&&FactionCache().Count==0,"missing prefab is never cached as a hostile classification");
        FactionPrefab(mob.Prefab,Character.Faction.Players,false);
        Assert((bool)Call("IsFriendlyCreature",mob)&&FactionCache().Count==1,"late registered friendly prefab is retried and recognized");
        ZNetScene.instance=new ZNetScene();FactionPrefab(mob.Prefab,Character.Faction.PlayerSpawned,true);
        Assert(!(bool)Call("IsFriendlyCreature",mob)&&FactionCache().Count==1,"scene replacement clears old prefab classification before same-hash lookup");
        Call("Reset");Assert(FactionCache().Count==0,"session reset clears bounded prefab cache");
        Call("IsFriendlyCreature",mob);ZNetScene.instance.Alive=false;
        Assert(!(bool)Call("IsFriendlyCreature",mob)&&FactionCache().Count==0,"destroyed scene releases cached prefab references");

        Setup();ZNetScene.instance.Views.Remove(mob);Call("ResetFactions");
        for(int i=0;i<1025;i++)
        {
            int hash=100000+i;FactionPrefab(hash,Character.Faction.Players,false);
            Call("IsFriendlyCreature",new ZDO(new ZDOID(90,(uint)(i+1)),1){Prefab=hash});
        }
        Assert(FactionCache().Count==1024&&!FactionCache().Contains(100000)&&FactionCache().Contains(101024),"prefab cache is capped at 1024 with oldest entry eviction");
        Call("IsFriendlyCreature",new ZDO(new ZDOID(90,2000),1){Prefab=100000});
        Assert(FactionCache().Count==1024&&FactionCache().Contains(100000),"evicted friendly prefab is lazily reclassified without exceeding the bound");

        Setup();ZNetView inactiveView=ZNetScene.instance.FindInstance(mob);inactiveView.GetComponent<Character>().Team=Character.Faction.Players;
        inactiveView.gameObject.activeInHierarchy=false;
        Assert(!(bool)Call("Eligible",mob)&&!Handle(out moved)&&!moved,"inactive loaded friendly actor cannot enter FGN handoff");
        Setup();ZNetView wrongView=ZNetScene.instance.FindInstance(mob);wrongView.GetComponent<Character>().Team=Character.Faction.Players;
        wrongView.Data=new ZDO(mob.m_uid,1);FactionPrefab(mob.Prefab,Character.Faction.PlayerSpawned,false);
        Assert(!(bool)Call("IsFriendlyCreature",mob),"mismatched live view does not apply another ZDO's friendly actor classification");

        Setup();target.Ping=400;Handle(out moved);ServerAccept();
        ZDOMan.instance.GetZDO(old.m_characterID).Position=ZDOMan.instance.GetZDO(other.m_characterID).Position=new Vector3(120,0,100);
        Handle(out moved);ZNetView forwardedView=ZNetScene.instance.FindInstance(mob);actor=forwardedView.GetComponent<Character>();actor.Team=Character.Faction.Players;
        Assert((bool)Call("DamagePrefix",actor,new HitData())&&forwardedView.Forwarded==0,"a newly friendly actor receives native damage handling without late-hit forwarding");

        Setup();target.Ping=400;Handle(out moved);ServerAccept();
        actor=ZNetScene.instance.FindInstance(mob).GetComponent<Character>();actor.Team=Character.Faction.Players;int ownerWrites=mob.OwnerWrites;
        Call("ReleaseManaged");
        Assert(mob.OwnerWrites==ownerWrites&&mob.GetOwner()==100,"policy cleanup does not rewrite ownership of a lease actor changed to friendly");

        Setup();ZNetScene.instance.Views.Remove(mob);FactionPrefab(mob.Prefab,Character.Faction.Players,false);Call("IsFriendlyCreature",mob);
        Harmony sceneHooks=new Harmony("combat.friendly.scene.fixture");
        try
        {
            sceneHooks.Patch(typeof(ZNetScene).GetMethod("Shutdown"),prefix:new HarmonyMethod(service.GetMethod("ResetFactions",flags)));
            ZNetScene.instance.Shutdown();Assert(FactionCache().Count==0,"native scene shutdown hook clears prefab cache without a new update loop");
        }
        finally{sceneHooks.UnpatchSelf();}
    }
}
