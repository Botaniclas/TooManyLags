using System;
using System.Reflection;
using HarmonyLib;
using TooManyLagsStandalone;
using UnityEngine;
internal static class TextEditingTests
{
    private static int checks;
    private static ZNet host,client;private static ZDOMan serverWorld,clientWorld;private static ZNetPeer editor,hostPeer;
    private static ZDO data,copy,actor;private static Sign sign;private static TextInput input;private static Player player;private static AuthorityController controller;
    private static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    private static void Server(){ZNet.instance=host;ZDOMan.instance=serverWorld;ZDOMan.Session=99;Plugin.Controller=controller;Player.m_localPlayer=player;}
    private static void Client(){ZNet.instance=client;ZDOMan.instance=clientWorld;ZDOMan.Session=2;Plugin.Controller=null;Player.m_localPlayer=player;}
    private static ZPackage Last(ZNetPeer peer){return new ZPackage(peer.Out[peer.Out.Count-1].GetArray());}
    private static int Kind(ZNetPeer peer){return Last(peer).ReadInt();}
    private static void Incoming(ZNetPeer peer,long uid,ZPackage packet){TextEditingProtection.Receive(peer,uid,packet);}
    private static void Setup(bool localHost=false)
    {
        TextEditingProtection.Reset();Time.unscaledTime=1;PrivateArea.Allowed=true;Plugin.HooksActive=true;Plugin.Network=new Network();
        host=new ZNet {Server=true,Dedicated=!localHost};client=new ZNet();controller=new AuthorityController();serverWorld=new ZDOMan();clientWorld=new ZDOMan();
        editor=new ZNetPeer {m_uid=2,m_characterID=new ZDOID(200)};hostPeer=new ZNetPeer {m_uid=99,m_server=true};host.Peers.Add(editor);client.Peers.Add(hostPeer);
        actor=new ZDO {m_uid=editor.m_characterID,Owner=localHost?99:2};serverWorld.Objects.Add(actor.m_uid,actor);
        data=new ZDO {m_uid=new ZDOID(1),Owner=localHost?99:3};serverWorld.Objects.Add(data.m_uid,data);
        host.Peers.Add(new ZNetPeer {m_uid=3});
        copy=localHost?data:new ZDO {m_uid=data.m_uid,Owner=3};clientWorld.Objects.Add(copy.m_uid,copy);
        var root=new GameObject();sign=root.Add<Sign>();root.Add<ZNetView>().Data=copy;input=new TextInput();
        var avatar=new GameObject();player=avatar.Add<Player>();avatar.Add<ZNetView>().Data=actor;
        if(localHost)Server();else Client();
    }
    private static void Begin()
    {
        Client();input.RequestText(sign,"native topic",123);Check(input.Opened==0,"UI waits for guarded acquisition");
        Check(LocalAgent.TextEditBusy(copy),"source veto exists before begin leaves client");
        ZPackage packet=Last(hostPeer);Server();Incoming(editor,2,packet);
        Check(AuthorityController.TextEditTarget(data)==2&&AuthorityController.TextEditTarget(actor)==0,"only edited object requests editor");
    }
    private static void Grant()
    {
        Server();TextEditingProtection.Poll();Check(editor.Out.Count==0,"old owner cannot grant editing");
        data.Owner=2;data.OwnerRevision=1;controller.Handoffs.InFlight=true;Time.unscaledTime+=.1f;TextEditingProtection.Poll();
        Check(editor.Out.Count==0,"final-state receipt blocks grant even after pending job retired");
        controller.Handoffs.InFlight=false;Time.unscaledTime+=.1f;TextEditingProtection.Poll();Check(Kind(editor)==1,"matching owner receives grant");
        Check(AuthorityController.TextEditLocked(data),"grant pins edited object only");
        Client();Incoming(hostPeer,99,Last(editor));Time.unscaledTime+=.1f;TextEditingProtection.Poll();Check(input.Opened==0,"grant cannot overtake owner replication");
        copy.Owner=2;copy.OwnerRevision=1;Time.unscaledTime+=.1f;TextEditingProtection.Poll();
        Check(input.Opened==1&&input.Topic=="native topic"&&input.Limit==123,"native receiver/topic/limit retained");
    }
    private static void Lifetime()
    {
        Setup();Begin();Grant();input.Confirm();Check(sign.Saved==1,"saving remains native");
        Server();int packets=editor.Out.Count,prepares=controller.Prepares;
        for(int i=0;i<10000;i++){Time.unscaledTime+=.1f;TextEditingProtection.Poll();AuthorityController.TextEditMovement(2,actor,new Vector3());}
        Check(AuthorityController.TextEditLocked(data),"confirmation and elapsed time retain lock");
        Check(editor.Out.Count==packets&&controller.Prepares==prepares,"stationary edit produces no RPCs or handoffs");
        AuthorityController.TextEditMovement(2,actor,new Vector3(.02f,0,0));Check(AuthorityController.TextEditLocked(data),"position noise retains lock");
        int ends=controller.Ends;AuthorityController.TextEditMovement(2,actor,new Vector3(.2f,0,0));
        Check(AuthorityController.TextEditTarget==null&&AuthorityController.TextEditLocked==null&&AuthorityController.TextEditMovement==null,"real movement releases callbacks in same grid cell");
        Check(controller.Prepares==prepares&&controller.Ends==ends+1,"release never replays an old takeover");
        Client();Incoming(hostPeer,99,Last(editor));Check(LocalAgent.TextEditBusy==null,"release acknowledgement retires source veto");
    }
    private static void ListenHost()
    {
        Setup(true);input.RequestText(sign,"host",40);Check(input.Opened==0&&controller.Prepares==1,"listen host participates through local coordinator");
        TextEditingProtection.Poll();Check(input.Opened==1&&AuthorityController.TextEditLocked(data),"listen host opens only after its local grant");
        Check(serverWorld.Sends==0&&editor.Out.Count==0&&hostPeer.Out.Count==0,"local host requires no network hop or snapshot send");
        input.Confirm();Time.unscaledTime+=200;TextEditingProtection.Poll();Check(AuthorityController.TextEditLocked(data),"host lock survives confirmation and long pause");
        player.transform.position=new Vector3(.2f,0,0);Time.unscaledTime+=.1f;TextEditingProtection.Poll();
        Check(AuthorityController.TextEditLocked==null&&LocalAgent.TextEditBusy==null,"listen-host movement releases both halves synchronously");
    }
    private static void Races()
    {
        Setup();Begin();ZPackage duplicate=Last(hostPeer);Server();Incoming(editor,2,new ZPackage(duplicate.GetArray()));Check(controller.Prepares==1,"duplicate begin cannot resubmit");
        Client();player.transform.position=new Vector3(.2f,0,0);Time.unscaledTime+=.1f;TextEditingProtection.Poll();Server();Incoming(editor,2,Last(hostPeer));Incoming(editor,2,duplicate);
        Check(AuthorityController.TextEditTarget==null,"end fences a delayed begin");
        Setup();Begin();Server();Time.unscaledTime+=9;TextEditingProtection.Poll();Client();Incoming(hostPeer,99,Last(editor));
        Check(input.Opened==0&&LocalAgent.TextEditBusy==null,"opening timeout cannot expose unprotected editor");
        Setup();Begin();Grant();Client();copy.Owner=3;copy.OwnerRevision++;TextEditingProtection.Observed(copy);Check(!input.Visible&&LocalAgent.TextEditBusy==null,"external owner loss cancels exact local editor");
        Server();data.Owner=3;data.OwnerRevision++;TextEditingProtection.Observed(data);Check(AuthorityController.TextEditLocked==null,"external owner loss retires server lock");
        Setup();Begin();Grant();Client();TextEditingProtection.Removed(copy);Check(!input.Visible&&LocalAgent.TextEditBusy==null,"view unloading clears exact UI");
        Server();TextEditingProtection.Disconnected(editor);Check(AuthorityController.TextEditLocked==null,"disconnect cannot strand lock");
        Setup();Begin();Grant();Server();actor.Health=0;TextEditingProtection.Observed(actor);Check(AuthorityController.TextEditLocked==null,"replicated death releases lock even when dead player is absent from grid peer list");
        Setup();Begin();Grant();Server();TextEditingProtection.Removed(actor);Check(AuthorityController.TextEditLocked==null,"destroyed character cannot leave a pinned object");
        Setup();Begin();Server();var fake=new ZNetPeer {m_uid=2};Incoming(fake,2,Last(hostPeer));Check(controller.Prepares==1,"replacement identity cannot replay pending request");
        Client();Incoming(hostPeer,99,new ZPackage(new byte[]{0}));Check(input.Opened==0,"malformed packet cannot open native UI");
        Setup();Begin();Server();TextEditingProtection.Disconnected(editor);host.Peers.Remove(editor);var replacement=new ZNetPeer {m_uid=2,m_characterID=actor.m_uid};host.Peers.Add(replacement);
        Incoming(replacement,2,Last(hostPeer));Check(controller.Prepares==2,"new connection may begin with its own token generation");
        TextEditingProtection.Disconnected(editor);Check(AuthorityController.TextEditTarget(data)==2,"late disconnect of old peer does not clear replacement session");
    }
    private static void NativeFallback()
    {
        Setup();hostPeer.TextSupported=false;input.RequestText(sign,"older host",50);Check(input.Opened==1&&hostPeer.Out.Count==0,"older host keeps native UI without unknown messages");
        Setup();var custom=new GameObject().Add<CustomSign>();input.RequestText(custom,"custom",20);Check(input.Opened==1&&hostPeer.Out.Count==0,"mod-specific receiver remains untouched");
        Setup();host.GetPeer(3).TextSupported=false;input.RequestText(sign,"native",20);Server();Incoming(editor,2,Last(hostPeer));Client();Incoming(hostPeer,99,Last(editor));Check(input.Opened==1&&AuthorityController.TextEditTarget==null&&LocalAgent.TextEditBusy==null,"older source uses native editing rather than unsupported locking");
        Setup();Plugin.HooksActive=false;input.RequestText(sign,"disabled",30);Check(input.Opened==1&&hostPeer.Out.Count==0,"disabled session stays native");
        Setup();Begin();Grant();Client();PrivateArea.Allowed=false;input.Hide();TextEditingProtection.Reset();Check(AuthorityController.TextEditLocked==null&&LocalAgent.TextEditBusy==null&&!TextEditingProtection.HasWork,"world reset clears caches/callbacks/UI");
    }
    private static void RespawnIdentity()
    {
        Setup();Begin();Grant();Client();player=new GameObject().Add<Player>();Player.m_localPlayer=player;Time.unscaledTime+=.1f;TextEditingProtection.Poll();
        Check(LocalAgent.TextEditBusy==null&&!input.Visible,"replacement player releases old local edit even at identical position");
        Setup();Begin();Grant();Server();var replacement=new ZDO {m_uid=new ZDOID(300),Owner=2};serverWorld.Objects.Add(replacement.m_uid,replacement);editor.m_characterID=replacement.m_uid;
        var second=new ZDO {m_uid=new ZDOID(2),Owner=3};serverWorld.Objects.Add(second.m_uid,second);
        var begin=Last(hostPeer);begin.ReadInt();begin.ReadZDOID();long newToken=begin.ReadLong()+1;
        var packet=new ZPackage();packet.Write(0);packet.Write(second.m_uid);packet.Write(newToken);packet.Write((ushort)0);
        Incoming(editor,2,new ZPackage(packet.GetArray()));
        Check(AuthorityController.TextEditTarget(second)==2&&(AuthorityController.TextEditLocked==null||!AuthorityController.TextEditLocked(data)),"new actor retires old pin and starts its own request");
        var characters=(System.Collections.IDictionary)typeof(TextEditingProtection).GetField("characters",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        Check(characters.Count==1&&characters.Contains(replacement.m_uid)&&!characters.Contains(actor.m_uid),"character index contains only the current generation");
        replacement.Health=0;TextEditingProtection.Observed(replacement);Check(characters.Count==0&&AuthorityController.TextEditTarget==null,"last replacement edit removes its actor index");
    }
    public static int Main()
    {
        var harmony=new Harmony("standalone.text.tests");
        try {harmony.CreateClassProcessor(typeof(TextRequestPatch)).Patch();Lifetime();ListenHost();Races();NativeFallback();RespawnIdentity();Console.WriteLine("PASS: "+checks+" production text-edit assertions; dedicated/listen host, ownership receipt, action lifetime, native fallback, cleanup and zero standing traffic. Managed Unity/transport doubles.");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally {TextEditingProtection.Reset();harmony.UnpatchSelf();}
    }
}
