using System;
using System.Reflection;
using TooManyLags.TextEditing;
using UnityEngine;
using Normal=TooManyLags;
using Mode=TooManyLags.PlayerSimulation;

internal static class ProtectionTests
{
    private static int checks;private static bool playerMode;
    private static ZNet host,client;private static ZDOMan serverWorld,clientWorld;private static ZNetPeer editor,hostPeer;
    private static ZDO serverData,clientData,character;private static TextInput input;private static Sign sign;
    private static Normal.AuthorityController normal;private static Mode.AuthorityController alternate;
    private static void Check(bool value,string message) { checks++;if(!value)throw new Exception(message); }
    private static object Call(string method,params object[] args) { return typeof(Protection).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args); }
    private static void Server() { ZNet.instance=host;ZDOMan.instance=serverWorld;ZDOMan.Session=99;Normal.Plugin.Controller=playerMode?null:normal;Mode.Plugin.Controller=playerMode?alternate:null; }
    private static void Client() { ZNet.instance=client;ZDOMan.instance=clientWorld;ZDOMan.Session=2;Normal.Plugin.Controller=null;Mode.Plugin.Controller=null; }
    private static void Setup(bool mode=false)
    {
        Protection.Stop();playerMode=mode;Time.unscaledTime=1;PrivateArea.Allowed=true;
        host=new ZNet {Server=true};client=new ZNet();serverWorld=new ZDOMan();clientWorld=new ZDOMan();
        normal=new Normal.AuthorityController();alternate=new Mode.AuthorityController();Mode.Plugin.Network=null;
        editor=new ZNetPeer {m_uid=2,m_characterID=new ZDOID(200)};hostPeer=new ZNetPeer {m_uid=99,m_server=true};
        host.Peers.Add(editor);client.Peers.Add(hostPeer);character=new ZDO {m_uid=editor.m_characterID,Owner=2};serverWorld.Objects.Add(character.m_uid,character);
        serverData=new ZDO {m_uid=new ZDOID(1),Owner=99};clientData=new ZDO {m_uid=serverData.m_uid,Owner=99};
        serverWorld.Objects.Add(serverData.m_uid,serverData);clientWorld.Objects.Add(clientData.m_uid,clientData);
        var root=new GameObject();sign=root.Add<Sign>();root.Add<ZNetView>().Data=clientData;input=new TextInput();Player.m_localPlayer=new GameObject().Add<Player>();
        Server();Protection.Start();Client();client.OnNewConnection(hostPeer);
    }
    private static ZPackage Last(ZNetPeer peer) { return peer.m_rpc.Sent[peer.m_rpc.Sent.Count-1]; }
    private static int Kind(ZPackage p) { return new ZPackage(p.GetArray()).ReadInt(); }
    private static void Begin(TextReceiver receiver=null)
    {
        Client();input.RequestText(receiver ?? sign,"native topic",123);Check(input.Opened==0,"native UI waits for actual safe acquisition");
        Check(Normal.GridHandoffs.Protocol.TextEditBusy(clientData),"local source is guarded before begin crosses network");
        Check(Mode.LocalAgent.TextEditBusy(clientData),"player-simulation source uses same local guard");
        ZPackage begin=Last(hostPeer);Server();editor.m_rpc.Deliver(begin);
        Check(Normal.AuthorityController.TextEditTarget(serverData)==2,"only edited object requests its editor");
        Check(Normal.AuthorityController.TextEditTarget(character)==0,"no neighbouring-object or player pin");
    }
    private static void Grant()
    {
        Server();Protection.Poll();Check(editor.m_rpc.Sent.Count==0,"old owner never grants editing");
        serverData.OwnerRevision=1;serverData.DataRevision=7;normal.Floor=9;serverData.SetOwnerInternal(2);normal.Pending=true;alternate.Handoffs.Busy=true;
        Time.unscaledTime+=.1f;Protection.Poll();Check(editor.m_rpc.Sent.Count==0,"in-flight existing handoff must settle first");
        normal.Pending=false;alternate.Handoffs.Busy=false;Time.unscaledTime+=.1f;Protection.Poll();
        Check(Kind(Last(editor))==1,"actual editor owner receives grant");Check(Normal.AuthorityController.TextEditLocked(serverData),"server pins only granted text object");
        ZPackage grant=Last(editor);Client();hostPeer.m_rpc.Deliver(grant);Time.unscaledTime+=.1f;Protection.Poll();Check(input.Opened==0,"grant cannot beat native ownership receipt");
        clientData.OwnerRevision=1;clientData.DataRevision=6;clientData.SetOwnerInternal(2);Time.unscaledTime+=.1f;Protection.Poll();
        Check(input.Opened==0,"ownership receipt alone cannot expose stale sign or portal text");
        clientData.DataRevision=9;Time.unscaledTime+=.1f;Protection.Poll();
        Check(input.Opened==1 && input.Topic=="native topic" && input.Limit==123,"native receiver, topic and character limit preserved");
    }
    private static void Lifetime(bool mode)
    {
        Setup(mode);Begin();Grant();input.Confirm();Check(sign.Saves==1,"text saving remains native");
        Server();Time.unscaledTime+=500;Protection.Poll();Check(Normal.AuthorityController.TextEditLocked(serverData),"confirming and elapsed time do not release typing pin");
        int sends=serverWorld.Sends,packets=editor.m_rpc.Sent.Count;
        for(int i=0;i<10000;i++) { Time.unscaledTime+=.1f;Protection.Poll(); }
        Check(serverWorld.Sends==sends && editor.m_rpc.Sent.Count==packets,"standing edits create no ownership jobs or protocol traffic");
        Client();Player.m_localPlayer.transform.position=new Vector3(.02f,0,0);Time.unscaledTime+=.1f;Protection.Poll();Check(Normal.AuthorityController.TextEditLocked(serverData),"tiny position noise retains pin");
        Player.m_localPlayer.transform.position=new Vector3(.2f,0,0);Time.unscaledTime+=.1f;Protection.Poll();ZPackage released=Last(hostPeer);
        Check(Normal.AuthorityController.TextEditLocked(serverData),"server retains safe pin until actual editor release arrives");
        Server();editor.m_rpc.Deliver(released);
        Check(Normal.AuthorityController.TextEditTarget==null && Normal.AuthorityController.TextEditLocked==null && Normal.AuthorityController.TextEditOwnerObserved==null,"movement within same grid cell clears coordinator callbacks without a server movement scan");
        Check((mode?alternate.Prepares:normal.Prepares)==1,"release never replays or resubmits discarded ownership requests");
        Client();Check(Normal.GridHandoffs.Protocol.TextEditBusy==null && Mode.LocalAgent.TextEditBusy==null,"movement clears both local source vetoes without waiting for an acknowledgement");
    }
    private static void Races()
    {
        Setup();Begin();ZPackage original=Last(hostPeer);Server();editor.m_rpc.Deliver(original);Check(normal.Prepares==1,"duplicate request does not restart handoff");
        Client();Player.m_localPlayer.transform.position=new Vector3(.2f,0,0);Time.unscaledTime+=.1f;Protection.Poll();ZPackage end=Last(hostPeer);
        Server();editor.m_rpc.Deliver(end);editor.m_rpc.Deliver(original);Check(Normal.AuthorityController.TextEditTarget==null,"late begin after release cannot resurrect session");
        Client();hostPeer.m_rpc.Deliver(new ZPackage(new byte[]{0}));Check(input.Opened==0,"malformed response cannot open UI");
        Setup();Begin();Grant();Server();normal.Ends=0;serverData.OwnerRevision++;serverData.SetOwnerInternal(3);
        Check(Normal.AuthorityController.TextEditLocked==null && normal.Ends==1,"external owner change invalidates server pin");
        Client();clientData.OwnerRevision++;clientData.SetOwnerInternal(3);input.Confirm();Check(!input.Visible && sign.Saves==0 && Normal.GridHandoffs.Protocol.TextEditBusy==null,"save guard closes stale UI without a global ownership hook or native write");
        Setup();Begin();Server();Time.unscaledTime+=9;Protection.Poll();Check(Normal.AuthorityController.TextEditTarget==null,"incomplete opening is bounded by timeout");
        Client();hostPeer.m_rpc.Deliver(Last(editor));Check(input.Opened==0 && Normal.GridHandoffs.Protocol.TextEditBusy==null,"timeout does not expose unprotected native editor");
    }
    private static void NativeAndCleanup()
    {
        Setup();hostPeer.TextSupported=false;input.RequestText(sign,"old host",50);Check(input.Opened==1 && hostPeer.m_rpc.Sent.Count==0,"older host retains vanilla UI without unknown custom RPC");
        Setup();var custom=new GameObject().Add<CustomSign>();input.RequestText(custom,"mod UI",99);Check(input.Opened==1 && hostPeer.m_rpc.Sent.Count==0,"unrelated text receivers remain untouched");
        Setup();Begin();Grant();Server();host.Disconnect(editor);Check(Normal.AuthorityController.TextEditLocked==null,"disconnect cannot strand server ownership pin");
        Client();client.Disconnect(hostPeer);Check(Normal.GridHandoffs.Protocol.TextEditBusy==null && !input.Visible,"disconnect cleans client session and text UI");
        Setup();Begin();Grant();Client();sign.GetComponent<ZNetView>().ResetZDO();Check(!input.Visible && Normal.GridHandoffs.Protocol.TextEditBusy==null,"actual view unloading cancels native editing safely");
        Server();serverData.Reset();Check(Normal.AuthorityController.TextEditLocked==null,"destroying exact server data retires edit");
        Setup();serverData.Text=false;Client();input.RequestText(sign,"invalid",12);Server();editor.m_rpc.Deliver(Last(hostPeer));Check(Normal.AuthorityController.TextEditTarget==null && Kind(Last(editor))==3,"unsupported server object rejects without pinning");
        Setup();Begin();Grant();Client();PrivateArea.Allowed=false;input.Hide(); // Existing lock does not bypass subsequent native access checks.
        Server();host.Shutdown();Check(Normal.AuthorityController.TextEditLocked==null && Normal.GridHandoffs.Protocol.TextEditBusy==null,"world shutdown clears all callbacks");
    }
    private static void PortalsAndContention()
    {
        foreach(bool mode in new[]{false,true})
        {
            Setup(mode);var root=new GameObject();var portal=root.Add<TeleportWorld>();root.Add<ZNetView>().Data=clientData;
            Begin(portal);Grant();input.Confirm();Server();Time.unscaledTime+=1000;Protection.Poll();
            Check(Normal.AuthorityController.TextEditLocked(serverData),"confirmed portal remains pinned without time limit");
            Client();Player.m_localPlayer.transform.position=new Vector3(0,0,.2f);Time.unscaledTime+=.1f;Protection.Poll();ZPackage released=Last(hostPeer);Server();editor.m_rpc.Deliver(released);
            Check(Normal.AuthorityController.TextEditLocked==null,"portal pin ends on actual movement in both modes");
        }
        Setup();Begin();Server();var competitor=new ZNetPeer {m_uid=3,m_characterID=new ZDOID(300)};
        host.Peers.Add(competitor);serverWorld.Objects.Add(competitor.m_characterID,new ZDO {m_uid=competitor.m_characterID});host.OnNewConnection(competitor);
        var request=new ZPackage();request.Write(0);request.Write(serverData.m_uid);request.Write(1L);request.Write(0U);request.Write(0U);competitor.m_rpc.Deliver(request);
        Check(Kind(Last(competitor))==3 && Normal.AuthorityController.TextEditTarget(serverData)==2 && normal.Prepares==1,"second editor cannot replace pending first editor");
        Setup();normal.Managed=false;Client();input.RequestText(sign,"disabled category",50);Server();editor.m_rpc.Deliver(Last(hostPeer));
        Check(Kind(Last(editor))==4 && normal.Prepares==0,"disabled categories require no ownership acquisition");
        Client();hostPeer.m_rpc.Deliver(Last(editor));Check(input.Opened==1 && Normal.GridHandoffs.Protocol.TextEditBusy==null,"unmanaged objects keep their normal native UI");
        Setup();Client();input.RequestText(sign,"old edit",50);var custom=new GameObject().Add<CustomSign>();input.RequestText(custom,"new UI",20);
        Check(input.Opened==1 && input.Topic=="new UI" && Normal.GridHandoffs.Protocol.TextEditBusy==null,"another native text request cancels pending UI rather than being overwritten by late grant");
        Setup();Begin();Client();Player.m_localPlayer.Dead=true;Time.unscaledTime+=.1f;Protection.Poll();ZPackage died=Last(hostPeer);Server();editor.m_rpc.Deliver(died);
        Check(Normal.AuthorityController.TextEditTarget==null,"death releases an incomplete edit without rescheduling acquisition");
    }
    private static ZPackage Intent(ZDOID id,long token)
    { var p=new ZPackage();p.Write(0);p.Write(id);p.Write(token);p.Write(0U);p.Write(0U);return p; }
    private static void PooledDataAndBounds()
    {
        Setup();Begin();Grant();Client();clientData.m_uid=new ZDOID(999);input.Confirm();
        Check(sign.Saves==0 && Normal.GridHandoffs.Protocol.TextEditBusy==null,"reused client data cannot save into a different object or leak old edit key");
        ZPackage end=Last(hostPeer);var endCopy=new ZPackage(end.GetArray());endCopy.ReadInt();
        Check(endCopy.ReadZDOID().Equals(new ZDOID(1)),"release retains immutable original object identity after pooling");
        Server();editor.m_rpc.Deliver(end);Check(Normal.AuthorityController.TextEditLocked==null,"original server edit is retired after client pooling");
        Setup();Begin();Grant();Server();serverData.m_uid=new ZDOID(999);Normal.AuthorityController.TextEditNodeRemoved(new ZDOID(1));
        Check(Normal.AuthorityController.TextEditLocked==null,"existing node lifecycle retires pooled data using its immutable identity");
        Setup();Server();
        for(int i=0;i<65;i++)
        {
            var data=new ZDO {m_uid=new ZDOID(500+i),Owner=99};serverWorld.Objects.Add(data.m_uid,data);
            editor.m_rpc.Deliver(Intent(data.m_uid,100+i));
        }
        Check(normal.Prepares==64 && Kind(Last(editor))==3,"per-player active edit cache is bounded before scheduling more work");
        Time.unscaledTime+=9;Protection.Poll();Check(Normal.AuthorityController.TextEditTarget==null && Normal.AuthorityController.TextEditOwnerObserved==null,"expired incomplete batch leaves no edit cache or ownership callback");
        Setup();Begin();ZPackage begin=Last(hostPeer);Server();int prepares=normal.Prepares;Protection.Stop();editor.m_rpc.Deliver(begin);
        Check(normal.Prepares==prepares && Normal.AuthorityController.TextEditTarget==null,"old registered endpoint cannot resurrect a disposed module");
    }
    public static void Main()
    {
        try { Lifetime(false);Lifetime(true);Races();NativeAndCleanup();PortalsAndContention();PooledDataAndBounds();Protection.Stop();Console.WriteLine("PASS: "+checks+" production text-edit lifecycle and Harmony fixture assertions; both modes, late messages, movement, exact ownership receipt and bounded idle work. No live Unity session."); }
        catch(Exception error) { Console.Error.WriteLine(error);Environment.ExitCode=1; }
    }
}
