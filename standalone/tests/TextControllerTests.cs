using System;
using System.Collections;
using System.Reflection;
using TooManyLagsStandalone;
using UnityEngine;
internal static class TextControllerTests
{
    private static int checks;private static Settings settings;private static AuthorityController controller;private static ZDO actor,other,sign;
    private static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    private static ZDO Add(long id,long owner,int prefab,float x=0,float z=0)
    {var value=new ZDO {m_uid=new ZDOID(id),Owner=owner,Prefab=prefab,Position=new Vector3 {x=x,z=z}};ZDOMan.instance.Objects.Add(value.m_uid,value);return value;}
    private static int Count(string field){return ((IDictionary)typeof(AuthorityController).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller)).Count;}
    private static void Setup(bool solo=false)
    {
        AuthorityController.TextEditTarget=null;AuthorityController.TextEditLocked=null;AuthorityController.TextEditMovement=null;
        Time.unscaledTime=0;ZDOMan.instance=new ZDOMan();ZNet.instance=new ZNet {Server=true};Plugin.Network=new Network();Plugin.Local=new LocalAgent();
        actor=Add(100,11,100);other=Add(200,22,100,2);sign=Add(1,11,10,1,1);
        Plugin.Network.Current.Add(new PlayerPeer {Uid=11,Character=actor,Reference=actor.Position});
        if(!solo)Plugin.Network.Current.Add(new PlayerPeer {Uid=22,Character=other,Reference=other.Position});
        Plugin.Network.Modded.Add(11);Plugin.Network.Modded.Add(22);
        settings=new Settings {Interval=.1f};controller=new AuthorityController(settings);Plugin.Controller=controller;controller.Poll(0);
    }
    private static void Step(float now){Time.unscaledTime=now;controller.Poll(now);}
    private static void ForcedEditorAndFreshRequests()
    {
        Setup();Check(controller.ManagesText(sign),"configured native sign is recognized");
        AuthorityController.TextEditTarget=delegate(ZDO data){return ReferenceEquals(data,sign)?22:0;};
        Check(controller.PrepareTextEdit(sign),"real controller accepts exact text object");Step(.01f);
        Check(sign.Owner==22&&controller.Handoffs.Requested==22,"existing guarded handoff targets actual editor despite grid ping winner");
        AuthorityController.TextEditTarget=null;AuthorityController.TextEditLocked=delegate(ZDO data){return ReferenceEquals(data,sign);};controller.EndTextEdit(sign);
        int requests=controller.Handoffs.Requests;
        for(int i=0;i<1000;i++)Step(1+i*.11f);
        Check(controller.Handoffs.Requests==requests&&sign.Owner==22,"standing lock does not cause handoff work");
        var door=Add(2,22,1,1,1);controller.Changed(door);Step(112);
        Check(door.Owner==11&&sign.Owner==22,"neighbouring interactive object remains independently managed");
        AuthorityController.TextEditLocked=null;controller.EndTextEdit(sign);requests=controller.Handoffs.Requests;
        controller.Changed(sign);Step(113);Step(114);Check(controller.Handoffs.Requests==requests,"unlock and delayed object notifications do not replay a discarded grid decision");
        actor.Rotation=new Quaternion {eulerAngles=new Vector3 {y=90}};Step(115);
        Check(sign.Owner==11&&controller.Handoffs.Requests>requests,"fresh covering rotation permits normal ownership again");
        controller.Stop();Check(Count("textWaiting")==0,"controller shutdown clears wait buckets");
    }
    private static void LegacyAndSoloCleanup()
    {
        Setup();Plugin.Network.LegacyText.Add(22);sign.Owner=22;controller.Changed(sign);Step(1);
        Check(sign.Owner==22,"older text owner keeps vanilla ownership");
        controller.Stop();Setup(true);AuthorityController.TextEditTarget=delegate(ZDO data){return ReferenceEquals(data,sign)?11:0;};
        Check(controller.PrepareTextEdit(sign),"solo edit can register outside contention interest");Step(.01f);Check(Count("nodes")==1,"solo forced node survives normal broad phase");
        AuthorityController.TextEditTarget=null;AuthorityController.TextEditLocked=delegate(ZDO data){return ReferenceEquals(data,sign);};controller.EndTextEdit(sign);
        Check(Count("nodes")==1,"solo active edit retains its node");
        AuthorityController.TextEditLocked=null;controller.EndTextEdit(sign);
        Check(Count("nodes")==0&&Count("textWaiting")==0,"unshared completed edit retains no unreachable field/cache entry");
        controller.Stop();Setup();AuthorityController.TextEditLocked=delegate(ZDO data){return ReferenceEquals(data,sign);};controller.EndTextEdit(sign);
        actor.Position=new Vector3 {x=10000};other.Position=new Vector3 {x=-10000};
        for(int i=0;i<Plugin.Network.Current.Count;i++){var peer=Plugin.Network.Current[i];peer.Reference=peer.Character.Position;Plugin.Network.Current[i]=peer;}
        AuthorityController.TextEditLocked=null;controller.EndTextEdit(sign);Step(3);
        Check(Count("textWaiting")==0,"retired native zones remove per-object wait entries");
    }
    public static int Main()
    {
        try{ForcedEditorAndFreshRequests();LegacyAndSoloCleanup();Console.WriteLine("PASS: "+checks+" production controller/grid text-edit assertions: targeted handoff, independent neighbours, fresh requests, optional-client fallback and cache cleanup.");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{AuthorityController.TextEditTarget=null;AuthorityController.TextEditLocked=null;AuthorityController.TextEditMovement=null;}
    }
}
