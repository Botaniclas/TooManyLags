using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TooManyLags;
using UnityEngine;

internal static class MobStatusStateTests
{
    private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private static int checks; private static uint nextId;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    private static FieldInfo Field(Type type,string name){for(Type current=type;current!=null;current=current.BaseType){FieldInfo f=current.GetField(name,All|BindingFlags.DeclaredOnly);if(f!=null)return f;}throw new Exception("Missing field "+name);}
    private static void Set(object value,string name,object item){Field(value.GetType(),name).SetValue(value,item);}
    private static T Get<T>(object value,string name){return (T)Field(value.GetType(),name).GetValue(value);}
    private static IDictionary Map(string name){return (IDictionary)typeof(MobStatusState).GetField(name,All).GetValue(null);}
    private static T Template<T>(string name)where T:StatusEffect,new(){T effect=new T{name=name,m_ttl=20};ObjectDB.instance.Templates.Add(effect.NameHash(),effect);return effect;}
    private static Character Actor(long owner)
    {
        GameObject obj=new GameObject();Character actor=obj.Add<Character>();actor.Manager=new SEMan();ZNetView view=obj.Add<ZNetView>();
        view.Data=new ZDO{m_uid=new ZDOID(51,++nextId),Owner=owner};ZDOMan.instance.All[view.Data.m_uid]=view.Data;ZNetScene.instance.AddInstance(view.Data,view);return actor;
    }
    private static ZNetView Fx(long owner,bool loaded)
    {
        GameObject obj=new GameObject();ZNetView view=obj.Add<ZNetView>();view.Data=new ZDO{m_uid=new ZDOID(52,++nextId),Owner=owner};ZDOMan.instance.All[view.Data.m_uid]=view.Data;
        if(loaded)ZNetScene.instance.AddInstance(view.Data,view);return view;
    }
    private static void New()
    {
        MobStatusState.Stop();Time.realtimeSinceStartupAsDouble=0;ZDOMan.Local=1;ZDOMan.instance=new ZDOMan();ZNetScene.instance=new ZNetScene();ObjectDB.instance=new ObjectDB();nextId=0;
        StatusEffect.Setups=StatusEffect.Resets=StatusEffect.Levels=StatusEffect.Stops=StatusEffect.OnDestroys=SE_Spawn.Spawns=SE_Demister.Spawned=UnityEngine.Object.DestroyCalls=0;MobStatusState.Initialize();
    }
    private static T Add<T>(Character actor,T template)where T:StatusEffect
    {T effect=(T)template.Clone();effect.Setup(actor);actor.GetSEMan().Add(effect);return effect;}
    private static Character Restore(Character source,byte[] payload)
    {Character target=Actor(1);MobStatusState.Restore(target,payload);GC.KeepAlive(source);return target;}
    private static T Effect<T>(Character actor)where T:StatusEffect
    {foreach(StatusEffect effect in actor.GetSEMan().GetStatusEffects())if(effect is T)return (T)effect;throw new Exception("No "+typeof(T).Name);}
    private static void DotAndScalarState()
    {
        New();Character source=Actor(1);SE_Poison poison=Add(source,Template<SE_Poison>("Poison"));SE_Burning burn=Add(source,Template<SE_Burning>("Burning"));
        poison.m_ttl=13.25f;Set(poison,"m_time",3.7f);Set(poison,"m_timer",.31f);Set(poison,"m_damageLeft",31f);Set(poison,"m_damagePerHit",4f);
        Set(burn,"m_time",6.2f);Set(burn,"m_timer",.61f);Set(burn,"m_fireDamageLeft",17f);Set(burn,"m_fireDamagePerHit",3f);Set(burn,"m_spiritDamageLeft",9f);Set(burn,"m_spiritDamagePerHit",2f);
        int setups=StatusEffect.Setups;byte[] bytes=MobStatusState.Capture(source);Character target=Restore(source,bytes);SE_Poison copied=Effect<SE_Poison>(target);SE_Burning copiedBurn=Effect<SE_Burning>(target);
        Check(StatusEffect.Setups==setups&&StatusEffect.Resets==0&&StatusEffect.Levels==0,"Restore replayed Setup/ResetTime/SetLevel");
        Check(copied.m_ttl==13.25f&&copied.GetDuration()==3.7f,"poison runtime TTL/duration not exact");
        Check(Get<float>(copied,"m_damageLeft")==31&&Get<float>(copied,"m_damagePerHit")==4&&Get<float>(copied,"m_timer")==.31f,"poison unpaid pool or phase lost");
        Check(Get<float>(copiedBurn,"m_fireDamageLeft")==17&&Get<float>(copiedBurn,"m_spiritDamageLeft")==9&&Get<float>(copiedBurn,"m_timer")==.61f,"burning unpaid pools/phase lost");
        target.Manager.Tick(.2f);Check(target.DamageTicks==0&&target.Health==100,"DoT tick replayed early");target.Manager.Tick(.12f);Check(target.DamageTicks==1&&target.Health==96,"poison next native tick timing changed");
        target.Manager.Tick(.3f);Check(target.DamageTicks==2&&target.Health==91,"burning next unpaid tick changed");
        Check(ReferenceEquals(copied.m_character,target)&&ReferenceEquals(copiedBurn.m_character,target),"destination actor reference stale");
        Check(ReferenceEquals(poison.m_tickEffect,copied.m_tickEffect),"template config reference not retained");Check(bytes.Length<1024,"ordinary two-effect snapshot unexpectedly large");
    }
    private static void FrostShieldStatsSpawn()
    {
        New();Character source=Actor(1);SE_Frost frost=Add(source,Template<SE_Frost>("Frost"));frost.m_ttl=7.3f;Set(frost,"m_time",2.4f);frost.m_attributes=StatusEffect.StatusAttribute.ColdResistance;
        SE_Shield shield=Add(source,Template<SE_Shield>("Shield"));Set(shield,"m_totalAbsorbDamage",233f);Set(shield,"m_damage",87f);
        SE_Stats stats=Add(source,Template<SE_Stats>("Stats"));stats.m_healthOverTimeDuration=19;stats.m_staminaOverTimeDuration=12;stats.m_eitrOverTimeDuration=15;stats.m_jumpModifier=new Vector3(1,2,3);
        Set(stats,"m_tickTimer",.43f);Set(stats,"m_healthOverTimeTimer",.15f);Set(stats,"m_healthOverTimeTicks",6f);Set(stats,"m_healthOverTimeTickHP",2.5f);
        SE_Spawn spawn=Add(source,Template<SE_Spawn>("Spawn"));Set(spawn,"m_spawned",true);int heal=source.Heals,setups=StatusEffect.Setups;
        Character target=Restore(source,MobStatusState.Capture(source));Check(source.Heals==heal&&target.Heals==0&&target.Health==100&&StatusEffect.Setups==setups,"upfront stats healing replayed");
        SE_Frost f=Effect<SE_Frost>(target);Check(f.m_ttl==7.3f&&f.GetDuration()==2.4f,"frost modified TTL lost");
        SE_Shield s=Effect<SE_Shield>(target);Check(Get<float>(s,"m_totalAbsorbDamage")==233&&Get<float>(s,"m_damage")==87,"remaining shield budget lost");
        SE_Stats st=Effect<SE_Stats>(target);Check(st.m_healthOverTimeDuration==19&&st.m_staminaOverTimeDuration==12&&st.m_eitrOverTimeDuration==15,"mutable public stats durations reset to template");
        Check(Get<float>(st,"m_tickTimer")==.43f&&Get<float>(st,"m_healthOverTimeTicks")==6&&Get<float>(st,"m_healthOverTimeTickHP")==2.5f&&st.m_jumpModifier.Equals(new Vector3(1,2,3)),"stats tick budget/value struct lost");
        target.Manager.Tick(.02f);Check(SE_Spawn.Spawns==0&&Get<bool>(Effect<SE_Spawn>(target),"m_spawned"),"already performed spawn repeated");
        Check(target.Manager.Has(f.NameHash())&&target.Manager.Attributes==(int)StatusEffect.StatusAttribute.ColdResistance&&target.Manager.Previous==-1,"SE manager hash/attribute caches not rebuilt");
    }
    private static void SourceDetachCyclesAndEmpty()
    {
        New();Character source=Actor(1);SE_Poison poison=Add(source,Template<SE_Poison>("Poison"));Set(poison,"m_damageLeft",19f);ZNetView fx=Fx(1,true);Set(poison,"m_startEffectInstances",new[]{fx.gameObject});
        byte[] payload=MobStatusState.Capture(source);Check(source.Manager.GetStatusEffects().Count==1&&fx.Data.Owner==1,"capture relinquished before successful commit");
        source.GetComponent<ZNetView>().Data.SetOwner(2);MobStatusState.Relinquish(source,payload,2);
        Check(source.Manager.GetStatusEffects().Count==0&&!source.Manager.Has(poison.NameHash())&&source.Manager.Attributes==0,"old authoritative effect manager retained");
        Check(StatusEffect.Stops==0&&StatusEffect.OnDestroys==0&&poison.m_character==null&&Get<GameObject[]>(poison,"m_startEffectInstances")==null,"relinquish called stop/destroy or retained source refs");
        Check(fx.Data.Owner==2&&fx.Destroys==0,"existing effect FX not transferred cleanly");
        Character target=Actor(1);MobStatusState.Restore(target,payload);Check(ReferenceEquals(Get<GameObject[]>(Effect<SE_Poison>(target),"m_startEffectInstances")[0],fx.gameObject),"existing FX not reused");
        byte[] again=MobStatusState.Capture(target);MobStatusState.Restore(target,again);Check(target.Manager.GetStatusEffects().Count==1&&Get<float>(Effect<SE_Poison>(target),"m_damageLeft")==19,"repeated epoch restore duplicated effect/lost budget");
        MobStatusState.Restore(target,null);Check(target.Manager.GetStatusEffects().Count==0&&target.Manager.Attributes==0&&StatusEffect.Stops==0&&fx.Destroys==1,"empty source snapshot did not clear stale epoch and retire its displaced local FX without Stop");
        Check(MobStatusState.Capture(target)==null,"empty manager serializes unnecessary payload");
    }
    private static void ShadowSurvivesSourceDetach()
    {
        New();Character source=Actor(1);SlowEffect slow=Add(source,Template<SlowEffect>("Slow"));slow.m_ttl=1f;
        SE_Poison poison=Add(source,Template<SE_Poison>("Poison"));Set(poison,"m_timer",0f);Set(poison,"m_damagePerHit",7f);Set(poison,"m_damageLeft",20f);
        ZNetView attachedFx=Fx(1,true);Set(poison,"m_startEffectInstances",new[]{attachedFx.gameObject});
        byte[] payload=MobStatusState.Capture(source);MobStatusState.Shadow shadow=MobStatusState.CreateShadow(source,payload);
        Check(shadow!=null,"captured fake status timeline was not created");
        source.GetComponent<ZNetView>().Data.SetOwner(2);MobStatusState.Relinquish(source,payload,2);
        Check(source.Manager.GetStatusEffects().Count==0,"source effects remained live after fake shadow capture");
        Check(shadow.VisualFxIds.Length==1&&shadow.VisualFxIds[0]==attachedFx.Data.m_uid,
            "visual FX references were lost when combat transfer detached live source effects");
        float speed=10f;shadow.ApplySpeedMods(ref speed,new Vector3(0,0,1));
        Check(Math.Abs(speed-5f)<.00001f&&source.Manager.GetStatusEffects().Count==0,"fake status speed modifier did not match native without mutating source");
        shadow.Advance(.2f);
        Check(source.DamageTicks==0&&source.Manager.GetStatusEffects().Count==0,"fake status tick paid damage or installed effects on old-owner actor");
        shadow.Advance(1f);speed=10f;shadow.ApplySpeedMods(ref speed,new Vector3(0,0,1));
        Check(Math.Abs(speed-10f)<.00001f&&StatusEffect.Stops==0,"expired fake status remained active or replayed Stop");
        shadow.Enter();
        try { Add(source,slow); shadow.RetainCurrent(); }
        finally { shadow.Exit(); }
        Check(source.Manager.GetStatusEffects().Count==0,"new native water status leaked into real manager");
        speed=10f;shadow.ApplySpeedMods(ref speed,new Vector3(0,0,1));
        Check(Math.Abs(speed-5f)<.00001f,"new native status was lost when private manager restored");
    }
    private static void GenericAndAtomicValidation()
    {
        New();Character source=Actor(1);SimpleModEffect effect=Add(source,Template<SimpleModEffect>("Simple"));Set(effect,"durationFactor",.875d);Set(effect,"direction",new Vector3(2,3,4));Set(effect,"orientation",new Quaternion{x=.1f,y=.2f,z=.3f,w=.4f});Set(effect,"epoch",123456789012L);Set(effect,"marker","runtime");
        byte[] bytes=MobStatusState.Capture(source);Character target=Restore(source,bytes);SimpleModEffect copy=Effect<SimpleModEffect>(target);
        Check(Get<double>(copy,"durationFactor")==.875&&Get<long>(copy,"epoch")==123456789012L&&Get<string>(copy,"marker")=="runtime"&&Get<Vector3>(copy,"direction").Equals(new Vector3(2,3,4)),"ordinary mod scalar/struct state not supported");
        Check(ReferenceEquals(effect.SharedTemplateConfig,copy.SharedTemplateConfig),"readonly shared template config changed");
        int before=target.Manager.GetStatusEffects().Count;bool failed=false;try{MobStatusState.Restore(target,new byte[]{1,2});}catch(EndOfStreamException){failed=true;}
        Check(failed&&target.Manager.GetStatusEffects().Count==before&&ReferenceEquals(Effect<SimpleModEffect>(target),copy),"malformed decode partially replaced manager");
        byte[] trailing=new byte[bytes.Length+1];Array.Copy(bytes,trailing,bytes.Length);failed=false;try{MobStatusState.Restore(target,trailing);}catch(InvalidDataException){failed=true;}Check(failed&&ReferenceEquals(Effect<SimpleModEffect>(target),copy),"trailing data partially replaced manager");
        Set(effect,"durationFactor",Double.NaN);failed=false;try{MobStatusState.Capture(source);}catch(NotSupportedException){failed=true;}Check(failed&&source.Manager.GetStatusEffects().Count==1,"invalid field accepted or changed source before owner write");
        New();source=Actor(1);ComplexModEffect complex=Add(source,Template<ComplexModEffect>("Complex"));complex.RuntimeGraph=new object();failed=false;
        try{MobStatusState.Capture(source);}catch(NotSupportedException){failed=true;}Check(failed&&source.GetComponent<ZNetView>().Data.Owner==1&&source.Manager.GetStatusEffects().Count==1,"unsupported runtime object graph silently dropped");
    }
    private static void DelayedFxAndAdapters()
    {
        New();Character source=Actor(1);SE_Poison effect=Add(source,Template<SE_Poison>("Poison"));ZNetView fx=Fx(1,false);Set(effect,"m_startEffectInstances",new[]{fx.gameObject});byte[] bytes=MobStatusState.Capture(source);
        Character target=Restore(source,bytes);StatusEffect copy=Effect<SE_Poison>(target);Check(Map("deferred").Count==1&&Get<GameObject[]>(copy,"m_startEffectInstances")[0]==null,"missing scene FX not deferred");
        ZNetScene.instance.AddInstance(fx.Data,fx);Check(Map("deferred").Count==0&&ReferenceEquals(Get<GameObject[]>(copy,"m_startEffectInstances")[0],fx.gameObject),"late existing FX not rebound");
        target.Manager.Remove(copy);Check(fx.Destroys==1,"resolved FX native cleanup lost");
        New();source=Actor(1);effect=Add(source,Template<SE_Poison>("Poison"));fx=Fx(1,false);Set(effect,"m_startEffectInstances",new[]{fx.gameObject});target=Restore(source,MobStatusState.Capture(source));copy=Effect<SE_Poison>(target);
        target.Manager.Remove(copy);Check(Map("cleanup").Count==1,"expiry before FX instantiation forgot cleanup");ZNetScene.instance.AddInstance(fx.Data,fx);Check(fx.Destroys==1&&Map("cleanup").Count==0,"late expired FX leaked");MobStatusState.RefreshReferences();Check(fx.Destroys==1,"late cleanup repeated");
        New();source=Actor(1);SE_Demister demister=Add(source,Template<SE_Demister>("Demister"));ZNetView ball=Fx(1,false);Set(demister,"m_ballInstance",ball.gameObject);Set(demister,"m_ballVel",new Vector3(1,2,3));Set(demister,"m_coverRayMask",987);target=Restore(source,MobStatusState.Capture(source));SE_Demister dest=Effect<SE_Demister>(target);
        dest.UpdateStatusEffect(.1f);Check(SE_Demister.Spawned==0&&dest.GetDuration()==.1f,"unloaded existing demister ball spawned duplicate or paused effect duration");ZNetScene.instance.AddInstance(ball.Data,ball);dest.UpdateStatusEffect(.1f);Check(SE_Demister.Spawned==0&&ReferenceEquals(Get<GameObject>(dest,"m_ballInstance"),ball.gameObject)&&Get<Vector3>(dest,"m_ballVel").Equals(new Vector3(1,2,3))&&Get<int>(dest,"m_coverRayMask")==987,"demister state/reference continuity lost");
        New();source=Actor(1);SE_Harpooned harpoon=Add(source,Template<SE_Harpooned>("Harpoon"));Character attacker=Actor(1);ZNetScene.instance.All.Remove(attacker.GetZDOID());ZNetView line=Fx(1,false);LineConnect link=line.gameObject.Add<LineConnect>();Set(harpoon,"m_attacker",attacker);Set(harpoon,"m_line",link);Set(harpoon,"m_baseDistance",8f);Set(harpoon,"m_drainStaminaTimer",.07f);target=Restore(source,MobStatusState.Capture(source));SE_Harpooned hp=Effect<SE_Harpooned>(target);
        Check(!hp.IsDone(),"temporarily missing attacker ended active harpoon");ZNetScene.instance.AddInstance(attacker.GetComponent<ZNetView>().Data,attacker.GetComponent<ZNetView>());ZNetScene.instance.AddInstance(line.Data,line);
        Check(ReferenceEquals(Get<Character>(hp,"m_attacker"),attacker)&&ReferenceEquals(Get<LineConnect>(hp,"m_line"),link)&&Get<float>(hp,"m_baseDistance")==8&&Get<float>(hp,"m_drainStaminaTimer")==.07f,"harpoon actor/line/distance/timer not preserved");
    }
    private static void DeferredReferenceSecondHandoff()
    {
        New();Character source=Actor(1);SE_Demister effect=Add(source,Template<SE_Demister>("Demister"));ZNetView ball=Fx(1,false);Set(effect,"m_ballInstance",ball.gameObject);
        Character first=Restore(source,MobStatusState.Capture(source));byte[] next=MobStatusState.Capture(first);Character second=Restore(first,next);
        SE_Demister current=Effect<SE_Demister>(second);current.UpdateStatusEffect(.02f);
        Check(SE_Demister.Spawned==0,"second handoff while existing ball is unloaded lost network reference and spawned duplicate");
        ZNetScene.instance.AddInstance(ball.Data,ball);Check(ReferenceEquals(Get<GameObject>(current,"m_ballInstance"),ball.gameObject),"second handoff did not retain deferred ball ID");
        New();source=Actor(1);SE_Harpooned harpoon=Add(source,Template<SE_Harpooned>("Harpoon"));Character attacker=Actor(1);ZNetScene.instance.All.Remove(attacker.GetZDOID());Set(harpoon,"m_attacker",attacker);
        first=Restore(source,MobStatusState.Capture(source));second=Restore(first,MobStatusState.Capture(first));Check(!Effect<SE_Harpooned>(second).IsDone(),"second handoff lost deferred attacker ID and prematurely ended harpoon");
        New();source=Actor(1);effect=Add(source,Template<SE_Demister>("Demister"));ball=Fx(1,false);Set(effect,"m_ballInstance",ball.gameObject);first=Restore(source,MobStatusState.Capture(source));next=MobStatusState.Capture(first);
        first.GetComponent<ZNetView>().Data.SetOwner(2);MobStatusState.Relinquish(first,next,2);Check(ball.Data.Owner==2,"existing but locally unloaded status FX owner was not transferred");
    }
    private static void UnknownFxWorldResetAndLateExpiry()
    {
        New();Character source=Actor(1);SE_Poison effect=Add(source,Template<SE_Poison>("Poison"));ZNetView fx=Fx(1,false);Set(effect,"m_startEffectInstances",new[]{fx.gameObject});byte[] payload=MobStatusState.Capture(source);
        ZDOMan.instance.All.Remove(fx.Data.m_uid);Character target=Restore(source,payload);target.Manager.Remove(Effect<SE_Poison>(target));
        Check(Map("cleanup").Count==1,"effect expired before FX ZDO arrived; unknown network ID tombstone dropped");
        ZDOMan.instance.All[fx.Data.m_uid]=fx.Data;ZNetScene.instance.AddInstance(fx.Data,fx);Check(fx.Destroys==1&&Map("cleanup").Count==0,"late FX ZDO+instance was not cleaned once");
        New();source=Actor(1);effect=Add(source,Template<SE_Poison>("Poison"));fx=Fx(1,false);Set(effect,"m_startEffectInstances",new[]{fx.gameObject});target=Restore(source,MobStatusState.Capture(source));
        ZDOMan.instance=new ZDOMan();ZNetScene.instance=new ZNetScene();MobStatusState.RefreshReferences();
        Check(Map("deferred").Count==0&&Map("cleanup").Count==0,"world swap retained stale reference/cleanup state");
        ZDOMan.instance.All[fx.Data.m_uid]=fx.Data;ZNetScene.instance.AddInstance(fx.Data,fx);Check(fx.Destroys==0,"old-world tombstone destroyed new-world object");
        New();source=Actor(1);SE_Demister demister=Add(source,Template<SE_Demister>("Demister"));ZNetView ball=Fx(1,false);Set(demister,"m_ballInstance",ball.gameObject);target=Restore(source,MobStatusState.Capture(source));
        target.Manager.Remove(Effect<SE_Demister>(target));Check(Map("cleanup").Count==1,"demister late ball cleanup state lost");MobStatusState.Reset();
        ZNetScene.instance.AddInstance(ball.Data,ball);Check(ball.Destroys==0&&Map("cleanup").Count==0,"Reset did not clear deferred cleanup");
        Check(typeof(MobStatusState).GetField("hooks",All).GetValue(null)!=null,"Reset removed bound lifetime hooks");
    }
    private static void BoundsAndTemplateValues()
    {
        New();Character source=Actor(1);SE_Poison template=Template<SE_Poison>("Poison");SE_Poison effect=Add(source,template);byte[] payload=MobStatusState.Capture(source);template.m_ttl=51;
        Character target=Restore(source,payload);Check(Effect<SE_Poison>(target).m_ttl==20,"unchanged source runtime scalar silently replaced by different destination template config");
        byte[] damaged=(byte[])payload.Clone();damaged[6]^=1;StatusEffect original=target.Manager.GetStatusEffects()[0];bool failed=false;
        try{MobStatusState.Restore(target,damaged);}catch(NotSupportedException){failed=true;}Check(failed&&ReferenceEquals(target.Manager.GetStatusEffects()[0],original),"schema mismatch changed existing manager");
        failed=false;try{MobStatusState.Restore(target,new byte[16385]);}catch(InvalidDataException){failed=true;}Check(failed&&ReferenceEquals(target.Manager.GetStatusEffects()[0],original),"oversized input accepted or mutated manager");
        New();source=Actor(1);for(int i=0;i<65;i++)Add(source,Template<SE_Poison>("P"+i));failed=false;try{MobStatusState.Capture(source);}catch(NotSupportedException){failed=true;}Check(failed&&source.Manager.GetStatusEffects().Count==65,"effect bound lost source state");
        New();source=Actor(1);effect=Add(source,Template<SE_Poison>("Poison"));Set(effect,"m_startEffectInstances",new GameObject[33]);failed=false;try{MobStatusState.Capture(source);}catch(NotSupportedException){failed=true;}Check(failed,"FX reference bound missing");
    }
    private static void DistinctMobBindingsShareUnityTemplateIdentity()
    {
        New();SE_Poison template=Template<SE_Poison>("Poison");Character sourceA=Actor(1),sourceB=Actor(1);SE_Poison a=Add(sourceA,template),b=Add(sourceB,template);ZNetView fxA=Fx(1,false),fxB=Fx(1,false);
        Set(a,"m_startEffectInstances",new[]{fxA.gameObject});Set(b,"m_startEffectInstances",new[]{fxB.gameObject});
        Check(a.Equals(b)&&!ReferenceEquals(a,b),"fixture lost native shared-template Unity equality");
        Character targetA=Restore(sourceA,MobStatusState.Capture(sourceA)),targetB=Restore(sourceB,MobStatusState.Capture(sourceB));
        Check(Map("deferred").Count==2,"same status on two mobs collapsed deferred CLR-instance bindings through Unity equality");
        ZNetScene.instance.AddInstance(fxA.Data,fxA);Check(ReferenceEquals(Get<GameObject[]>(Effect<SE_Poison>(targetA),"m_startEffectInstances")[0],fxA.gameObject)&&Map("deferred").Count==1,"first mob reference resolved wrong shared-template clone");
        ZNetScene.instance.AddInstance(fxB.Data,fxB);Check(ReferenceEquals(Get<GameObject[]>(Effect<SE_Poison>(targetB),"m_startEffectInstances")[0],fxB.gameObject)&&Map("deferred").Count==0,"second mob reference binding lost");
    }
    private static void ActiveReferenceOwnership()
    {
        New();Character source=Actor(1);SE_Demister demister=Add(source,Template<SE_Demister>("Demister"));ZNetView ball=Fx(2,true);Set(demister,"m_ballInstance",ball.gameObject);
        Character target=Restore(source,MobStatusState.Capture(source));Check(ball.Data.IsOwner()&&ball.Claims==1&&ReferenceEquals(Get<GameObject>(Effect<SE_Demister>(target),"m_ballInstance"),ball.gameObject),"restored mist ball is not owned by its active effect simulator");
        New();source=Actor(1);SE_Harpooned harpoon=Add(source,Template<SE_Harpooned>("Harpoon"));Character attacker=Actor(2);ZNetView line=Fx(2,true);LineConnect link=line.gameObject.Add<LineConnect>();Set(harpoon,"m_attacker",attacker);Set(harpoon,"m_line",link);
        target=Restore(source,MobStatusState.Capture(source));Check(line.Data.IsOwner()&&line.Claims==1&&attacker.GetComponent<ZNetView>().Data.Owner==2,"line owner not acquired or attacker ownership stolen");
    }
    private static void DestroyedPendingReferences()
    {
        New();Character source=Actor(1);SE_Harpooned effect=Add(source,Template<SE_Harpooned>("Harpoon"));Character attacker=Actor(1);ZNetScene.instance.All.Remove(attacker.GetZDOID());Set(effect,"m_attacker",attacker);
        Character target=Restore(source,MobStatusState.Capture(source));SE_Harpooned restored=Effect<SE_Harpooned>(target);Check(!restored.IsDone(),"unloaded but living attacker already treated as destroyed");
        ZDOMan.instance.HandleDestroyedZDO(attacker.GetZDOID());Check(restored.IsDone(),"destroyed pending harpoon attacker keeps effect alive forever");
        Check(Map("deferred").Count==0,"destroyed pending attacker retained unresolved binding");
        New();source=Actor(1);effect=Add(source,Template<SE_Harpooned>("Harpoon"));attacker=Actor(1);ZNetScene.instance.All.Remove(attacker.GetZDOID());Set(effect,"m_attacker",attacker);Set(effect,"m_broken",true);
        target=Restore(source,MobStatusState.Capture(source));Check(Effect<SE_Harpooned>(target).IsDone(),"restored broken harpoon ignores native broken flag while attacker is unloaded");
        New();source=Actor(1);effect=Add(source,Template<SE_Harpooned>("Harpoon"));attacker=Actor(1);ZNetScene.instance.All.Remove(attacker.GetZDOID());Set(effect,"m_attacker",attacker);byte[] payload=MobStatusState.Capture(source);ZDOMan.instance.HandleDestroyedZDO(attacker.GetZDOID());
        target=Restore(source,payload);Check(Effect<SE_Harpooned>(target).IsDone(),"attacker destroyed between checkpoint and adoption was resurrected as deferred reference");
        New();source=Actor(1);SE_Demister demister=Add(source,Template<SE_Demister>("Demister"));ZNetView ball=Fx(1,false);Set(demister,"m_ballInstance",ball.gameObject);target=Restore(source,MobStatusState.Capture(source));SE_Demister dest=Effect<SE_Demister>(target);dest.UpdateStatusEffect(.02f);
        Check(SE_Demister.Spawned==0,"pending existing ball incorrectly respawned before destruction");ZDOMan.instance.HandleDestroyedZDO(ball.Data.m_uid);dest.UpdateStatusEffect(.02f);
        Check(SE_Demister.Spawned==1,"destroyed pending mist ball never permits native replacement");
        dest.UpdateStatusEffect(.02f);Check(SE_Demister.Spawned==1,"native replacement mist ball spawned more than once");
        New();source=Actor(1);demister=Add(source,Template<SE_Demister>("Demister"));ball=Fx(1,false);Set(demister,"m_ballInstance",ball.gameObject);payload=MobStatusState.Capture(source);ZDOMan.instance.HandleDestroyedZDO(ball.Data.m_uid);
        target=Restore(source,payload);Effect<SE_Demister>(target).UpdateStatusEffect(.02f);Check(SE_Demister.Spawned==1,"ball destroyed in checkpoint/adoption gap stays deferred forever");
        New();source=Actor(1);SE_Poison poison=Add(source,Template<SE_Poison>("Poison"));ZNetView fx=Fx(1,false);Set(poison,"m_startEffectInstances",new[]{fx.gameObject});target=Restore(source,MobStatusState.Capture(source));ZDOMan.instance.HandleDestroyedZDO(fx.Data.m_uid);MobStatusState.RefreshReferences();
        Check(Map("deferred").Count==0,"destroyed cosmetic FX retained per-effect pending state");target.Manager.Remove(Effect<SE_Poison>(target));Check(Map("cleanup").Count==0,"known destroyed FX resurrected cleanup tombstone");
    }
    private static void RetireDisplacedDestinationReferences()
    {
        New();SE_Poison template=Template<SE_Poison>("Poison");Character source=Actor(1),target=Actor(1);Add(source,template);SE_Poison stale=Add(target,template);ZNetView old=Fx(1,true);Set(stale,"m_startEffectInstances",new[]{old.gameObject});byte[] payload=MobStatusState.Capture(source);
        MobStatusState.Restore(target,payload);Check(!old.Data.IsValid()&&old.Claims==0,"displaced local destination FX leaked or was unnecessarily claimed");Check(StatusEffect.Stops==0&&StatusEffect.OnDestroys==0,"destination retirement replayed native effect stop/destroy");
        int destroys=old.Destroys,requests=ZDOMan.instance.DestroyRequests;MobStatusState.Restore(target,payload);Check(old.Destroys==destroys&&ZDOMan.instance.DestroyRequests==requests,"repeated restore repeated displaced FX destruction");
        New();template=Template<SE_Poison>("Poison");source=Actor(1);target=Actor(1);SE_Poison incoming=Add(source,template);stale=Add(target,template);ZNetView same=Fx(1,true);Set(incoming,"m_startEffectInstances",new[]{same.gameObject});Set(stale,"m_startEffectInstances",new[]{same.gameObject});payload=MobStatusState.Capture(source);
        MobStatusState.Restore(target,payload);MobStatusState.Restore(target,payload);Check(same.Data.IsValid()&&same.Destroys==0&&same.Claims==0&&ReferenceEquals(Get<GameObject[]>(Effect<SE_Poison>(target),"m_startEffectInstances")[0],same.gameObject),"incoming identical FX was retired during epoch replacement");
        New();template=Template<SE_Poison>("Poison");source=Actor(1);target=Actor(1);Add(source,template);stale=Add(target,template);ZNetView foreign=Fx(2,true);Set(stale,"m_startEffectInstances",new[]{foreign.gameObject});MobStatusState.Restore(target,MobStatusState.Capture(source));
        Check(foreign.Data.IsValid()&&foreign.Data.Owner==2&&foreign.Destroys==0&&foreign.Claims==0,"retirement stole or destroyed foreign-owner FX");
        New();template=Template<SE_Poison>("Poison");source=Actor(1);target=Actor(1);Add(source,template);stale=Add(target,template);GameObject local=new GameObject();Set(stale,"m_startEffectInstances",new[]{local});MobStatusState.Restore(target,MobStatusState.Capture(source));
        Check(local.Destroyed&&UnityEngine.Object.DestroyCalls==1,"displaced local nonnetwork FX leaked");
        New();template=Template<SE_Poison>("Poison");source=Actor(1);target=Actor(1);Add(source,template);stale=Add(target,template);old=Fx(1,false);Set(stale,"m_startEffectInstances",new[]{old.gameObject});byte[] stalePayload=MobStatusState.Capture(target);MobStatusState.Restore(target,stalePayload);Check(Map("deferred").Count==1,"missing scene FX setup for retirement failed");
        MobStatusState.Restore(target,MobStatusState.Capture(source));Check(!old.Data.IsValid()&&ZDOMan.instance.DestroyRequests==1,"displaced owned FX ZDO without scene node was abandoned");
        New();SE_Demister demisterTemplate=Template<SE_Demister>("Demister");source=Actor(1);target=Actor(1);Add(source,demisterTemplate);SE_Demister staleMist=Add(target,demisterTemplate);old=Fx(1,true);Set(staleMist,"m_ballInstance",old.gameObject);MobStatusState.Restore(target,MobStatusState.Capture(source));Check(!old.Data.IsValid()&&StatusEffect.Stops==0,"displaced owned mist ball leaked/replayed Stop");
        New();SE_Harpooned harpoonTemplate=Template<SE_Harpooned>("Harpoon");source=Actor(1);target=Actor(1);Add(source,harpoonTemplate);SE_Harpooned staleHarpoon=Add(target,harpoonTemplate);old=Fx(1,true);LineConnect link=old.gameObject.Add<LineConnect>();Set(staleHarpoon,"m_line",link);MobStatusState.Restore(target,MobStatusState.Capture(source));Check(!old.Data.IsValid()&&StatusEffect.Stops==0,"displaced owned harpoon line leaked/replayed Stop");
    }
    public static void Main()
    {
        try{DotAndScalarState();FrostShieldStatsSpawn();SourceDetachCyclesAndEmpty();ShadowSurvivesSourceDetach();GenericAndAtomicValidation();DelayedFxAndAdapters();DeferredReferenceSecondHandoff();UnknownFxWorldResetAndLateExpiry();BoundsAndTemplateValues();DistinctMobBindingsShareUnityTemplateIdentity();ActiveReferenceOwnership();DestroyedPendingReferences();RetireDisplacedDestinationReferences();Console.WriteLine("PASS: "+checks+" production status-state checks: unpaid DoT budgets, timers, generic scalar state, side-effect-free epoch restore, fake shadow, existing and delayed FX.");}
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}finally{MobStatusState.Stop();}
    }
}
