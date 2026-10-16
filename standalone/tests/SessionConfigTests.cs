using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using TooManyLagsStandalone;
using UnityEngine;
internal static class SessionConfigTests
{
    private static int checks;
    private static void Check(bool value,string message) { checks++;if(!value)throw new Exception(message); }
    private static void Incoming(ZNetPeer peer,Message message,Action<ZPackage> write)
    { var part=new ZPackage();part.Write((int)message);write(part);var batch=new ZPackage();batch.Write(1);batch.Write(part);batch.SetPos(0);peer.m_rpc.Handler(peer.m_rpc,batch); }
    private static void Hello(ZNetPeer peer,bool mobs=true,int protocol=Network.Protocol,bool? text=null)
    { Incoming(peer,Message.Hello,delegate(ZPackage p) { p.Write(protocol);p.Write(mobs);if(text.HasValue)p.Write(text.Value); }); }
    private static void Welcome(ZNetPeer peer,bool enabled=true,bool mobs=true,float interval=.05f,float angle=30f,float prediction=.1f,int protocol=Network.Protocol,bool? text=null)
    { Incoming(peer,Message.Welcome,delegate(ZPackage p) { p.Write(protocol);p.Write(enabled);p.Write(mobs);p.Write(interval);p.Write(angle);p.Write(prediction);if(text.HasValue)p.Write(text.Value); }); }
    private static List<ZPackage> Messages(ZNetPeer peer,Message wanted)
    {
        var result=new List<ZPackage>();foreach(var source in peer.m_rpc.Sent)
        { var batch=new ZPackage(source.GetArray());int count=batch.ReadInt();for(int i=0;i<count;i++) { var part=new ZPackage();batch.ReadPackage(ref part);if((Message)part.ReadInt()==wanted)result.Add(part); } }return result;
    }
    private static ConfigEntryBase Entry(ConfigFile config,string key)
    { foreach(var pair in config)if(pair.Key.Key==key)return pair.Value;throw new Exception(key); }
    public static void Main(string[] args)
    {
        try
        {
            string path=Path.Combine(args[0],"SessionConfig-"+Guid.NewGuid().ToString("N")+".cfg");
            var config=new ConfigFile(path,false);var settings=new Settings(config);Plugin.Options=settings;
            var applied=new List<string>();
            Plugin.Commands=new ConfigCommands(config,delegate(string key) { settings.ApplyLive(key);applied.Add(key);if(Plugin.Network!=null && key.StartsWith("Camera",StringComparison.Ordinal))Plugin.Network.BroadcastSettings(); });
            Check(settings.CellSize==4f && settings.Pattern==GridCoveragePattern.Rounded7x7,"default rounded four-metre grid retained");
            Check((settings.Categories&Kinds.Incinerators)!=0,"incinerator category enabled with new handoff contract");
            Check(ConfigCommands.Local("enablelogs") && !ConfigCommands.Local("Enabled") && !ConfigCommands.Local("EnableAIOwnership"),"only logging is local");
            Check(Plugin.Commands.Execute("CameraHeadingCheckIntervalSeconds 0.05",true).Contains("already"),"20Hz camera sampling allowed");
            Check(Plugin.Commands.Execute("CameraHeadingCheckIntervalSeconds 0.049",true).Contains("Invalid"),"camera interval lower bound enforced");
            Check(Plugin.Commands.Execute("CameraDirectionOverlapDegrees NaN",true).Contains("Invalid") && Plugin.Commands.Execute("CameraRotationPredictionSeconds Infinity",true).Contains("Invalid"),"nonfinite live settings rejected");
            Check(Plugin.Commands.Execute("GridCoveragePattern Square3x3",true).Contains("Invalid grid"),"invalid4m square combination rejected");
            Check(Plugin.Commands.Execute("GridCellSizeMeters 6.4",true).Contains("restart"),"6.4m cells saved for restart");
            Check(settings.CellSize==4f && applied.Count==0,"pending grid setting cannot mutate active cell size");
            Check(Plugin.Commands.Execute("GridCoveragePattern Square3x3",true).Contains("restart"),"valid pending6.4m square accepted");
            Check(Plugin.Commands.Execute("Enabled false",true).Contains("current session is unchanged") && settings.Enabled,"disable is saved but does not tear down active ownership");
            Check(Plugin.Commands.Execute("Enabled",true).Contains("restart pending; active: True"),"pending active value visible");
            Check(Plugin.Commands.Execute("Enabled true",true).Contains("no restart pending"),"restoring active flag clears pending status");
            Check(Plugin.Commands.Execute("OuterTransitionDelaySeconds 1,5",true).Contains("applied") && settings.Outer==1.5f,"finite decimal comma live delay applied");
            Check(Plugin.Commands.Execute("EnableLogs true",false).Contains("applied") && settings.Logs,"local logs apply without host privileges");
            Check(Plugin.Commands.Execute("EnableLogs false",true).Contains("Wrong setting") && Plugin.Commands.Execute("Enabled",false).Contains("Wrong setting"),"scope checks prevent mixing local/host values");
            Check(!Plugin.Commands.Execute("list",true).Contains("EnableLogs =") && Plugin.Commands.Execute("list",false).Contains("EnableLogs ="),"config list follows scope");
            Check(!Plugin.Commands.Execute("NoServerSideSimulation false",true).Contains("applied"),"no FiresGhettoNetworking/Server-Side Simulation setting exposed");
            Entry(config,"Enabled").BoxedValue=false;Entry(config,"EnableAIOwnership").BoxedValue=false;
            var local=new Settings(config);Plugin.Options=local;
            local.Session(true,true,.2f,42f,.3f);
            Check(local.Enabled && local.Mobs && local.CameraInterval==.2f && local.CameraOverlap==42f && local.CameraPrediction==.3f,"host session overrides disabled local gameplay/camera preferences");
            Check(!(bool)Entry(config,"Enabled").BoxedValue && !(bool)Entry(config,"EnableAIOwnership").BoxedValue,"received host session never overwrites local config file");
            local.RestoreSession();Check(!local.Enabled && !local.Mobs && local.CameraInterval==.05f && local.CameraOverlap==30f,"disconnect restores local startup mode and camera values");
            local.Session(false,true,.05f,30f,.1f);Check(!local.Enabled && !local.Mobs,"disabled host never enables mobs");
            Plugin.Options=settings;Plugin.Local=new LocalAgent();Plugin.Controller=new AuthorityController();Plugin.Network=new Network();
            ZNet.instance=new ZNet { Server=true };ZDOMan.Session=11;
            Player.m_localPlayer=new Player { View=new ZNetView { Data=new ZDO { m_uid=new ZDOID(1) } } };
            var remote=new ZNetPeer { m_uid=22,m_characterID=new ZDOID(2) };ZNet.instance.Peers.Add(remote);Plugin.Network.Register(remote);
            ZDOMan.instance.Objects[remote.m_characterID]=new ZDO { m_uid=remote.m_characterID,Owner=22 };
            var players=new List<PlayerPeer>();Plugin.Network.Players(players);
            Check(players.Count==2 && Plugin.Network.Present(22) && !Plugin.Network.Capable(22),"listen host and unmodded client participate before handshake");
            Plugin.Network.Send(22,Message.Prepare,delegate(ZPackage p) { p.Write(1); });Plugin.Network.FlushAll();
            Check(remote.m_rpc.Sent.Count==0,"unmodded peers receive no custom gameplay RPC");
            Hello(remote,true,2);Check(!Plugin.Network.Capable(22) && remote.m_rpc.Sent.Count==0,"old1.0.5 protocol remains native fallback without disconnection or unsupported messages");
            Hello(remote);Check(Plugin.Network.Capable(22) && Plugin.Network.MobCapable(22),"new protocol advertises full handoff support independent of local config");
            Check(!Plugin.Network.TextCapable(22),"older protocol3 client remains connected without claiming new text safeguards");
            int textPackets=TextEditingProtection.Packets;Incoming(remote,Message.TextEdit,delegate(ZPackage p){p.Write(0);});Check(TextEditingProtection.Packets==textPackets,"older endpoint cannot send text-control messages");
            Hello(remote,true,Network.Protocol,true);Check(Plugin.Network.TextCapable(22),"optional text capability negotiates on existing protocol");
            Incoming(remote,Message.TextEdit,delegate(ZPackage p){p.Write(0);});Check(TextEditingProtection.Packets==textPackets+1,"authenticated capable endpoint reaches text control");
            var welcomes=Messages(remote,Message.Welcome);var welcome=welcomes[welcomes.Count-1];
            Check(welcome.ReadInt()==3 && welcome.ReadBool() && welcome.ReadBool() && welcome.ReadSingle()==settings.CameraInterval && welcome.ReadSingle()==settings.CameraOverlap && welcome.ReadSingle()==settings.CameraPrediction,"welcome carries authoritative Enabled/Mobs and complete camera settings");
            int prior=Messages(remote,Message.Welcome).Count;var unmodded=new ZNetPeer { m_uid=33 };ZNet.instance.Peers.Add(unmodded);Plugin.Network.Register(unmodded);
            Plugin.Commands.Execute("CameraHeadingCheckIntervalSeconds 0.1",true);
            Check(Messages(remote,Message.Welcome).Count==prior+1 && unmodded.m_rpc.Sent.Count==0,"live camera changes broadcast only to compatible announced peers");
            ZNet.instance.Admin=false;int commands=Plugin.CommandsExecuted;
            Incoming(remote,Message.ConfigRequest,delegate(ZPackage p) { p.Write(1);p.Write("OuterTransitionDelaySeconds 4"); });
            var denied=Messages(remote,Message.ConfigReply);denied[0].ReadInt();
            Check(denied[0].ReadString().Contains("admin rights") && Plugin.CommandsExecuted==commands,"nonadmin remote requests cannot run config commands");
            Check(ZNet.instance.AdminIdentity==remote.m_socket.GetHostName(),"remote admin checks authenticated socket identity");
            ZNet.instance.Admin=true;Time.unscaledTime=2;
            Incoming(remote,Message.ConfigRequest,delegate(ZPackage p) { p.Write(2);p.Write("OuterTransitionDelaySeconds 4"); });
            Check(settings.Outer==4,"authenticated remote admin can change live settings");
            for(int i=0;i<20;i++)Incoming(remote,Message.ConfigRequest,delegate(ZPackage p) { p.Write(3);p.Write("Enabled"); });
            Check(Plugin.CommandsExecuted==commands+4,"per-connection remote config requests limited to four per second");
            Time.unscaledTime=3;var replaced=new ZNetPeer { m_uid=22 };ZNet.instance.Peers[0]=replaced;
            Incoming(remote,Message.ConfigRequest,delegate(ZPackage p) { p.Write(4);p.Write("OuterTransitionDelaySeconds 8"); });
            Check(settings.Outer==4,"stale connection cannot administer replacement peer");ZNet.instance.Peers[0]=remote;
            settings.Enabled=false;Plugin.Local=null;Hello(remote);
            welcomes=Messages(remote,Message.Welcome);welcome=welcomes[welcomes.Count-1];welcome.ReadInt();Check(!welcome.ReadBool(),"disabled host still advertises disabled mode");
            Plugin.Network.Reset();Check(!Plugin.Network.Confirmed && !Plugin.Network.Capable(22),"world reset clears capability/session confirmation");
            ZNet.instance.Server=false;ZNet.instance.ServerPeer=remote;Plugin.Options=local;Plugin.Network.Register(remote);
            Plugin.Network.Send(22,Message.Target,delegate(ZPackage p) { p.Write(0); });Plugin.Network.FlushAll();
            int changes=Plugin.SessionChanges;Welcome(unmodded);Check(Plugin.SessionChanges==changes,"unregistered/nonserver welcome ignored");
            Welcome(remote,true,true,.1f,37f,.25f);
            Check(Plugin.Network.Confirmed && Plugin.HooksActive && local.Enabled && local.Mobs,"disabled local client activates only after authenticated enabled welcome");
            Check(local.CameraInterval==.1f && local.CameraOverlap==37f && local.CameraPrediction==.25f,"camera timing/overlap/prediction are session authoritative");
            Check(!Plugin.Network.TextCapable(22),"older host without trailing text flag keeps native editing");
            Welcome(remote,true,true,.1f,37f,.25f,Network.Protocol,true);Check(Plugin.Network.TextCapable(22),"new host advertises text protection without changing the base protocol");
            changes=Plugin.SessionChanges;Welcome(remote,true,true,.049f);Welcome(remote,true,true,.05f,46f);Welcome(remote,true,true,.05f,30f,.6f);
            Check(Plugin.SessionChanges==changes,"invalid host camera payload leaves prior session untouched");
            Welcome(remote,false,true);Check(Plugin.Network.Confirmed && !Plugin.HooksActive && !local.Enabled && !local.Mobs,"disabled host keeps optional client hooks dormant after successful handshake");
            Welcome(remote,true,false);Check(Plugin.HooksActive && !Plugin.Local.MobsEnabled,"host controls mob mode independently of general session mode");
            Plugin.TargetSupport=false;Welcome(remote,true,true);Check(!Plugin.Local.MobsEnabled,"unsupported native target contract cannot be enabled by network settings");Plugin.TargetSupport=true;
            ConfigConsole.Register();var terminal=new Terminal();Terminal.ConsoleCommand.Execute(new Terminal.ConsoleEventArgs { Context=terminal,Args=new[] { "tml_config","OuterTransitionDelaySeconds" } });
            Plugin.Network.FlushAll();var requests=Messages(remote,Message.ConfigRequest);Check(requests.Count>0,"remote admin command uses negotiated existing channel");
            var request=requests[requests.Count-1];int ticket=request.ReadInt();
            ConfigConsole.Reply(unmodded,ticket,"spoof");Check(terminal.Lines.Count==0,"config reply cannot come from a different peer");
            ConfigConsole.Reply(remote,ticket+1,"wrong ticket");Check(terminal.Lines.Count==0,"config reply ticket must match current request");
            ConfigConsole.Reply(remote,ticket,"received");Check(terminal.Lines.Count==1 && terminal.Lines[0]=="received","authenticated matching reply reaches originating console");
            ZNet.instance.Server=true;Plugin.Options=settings;Terminal.ConsoleCommand.Execute(new Terminal.ConsoleEventArgs { Context=terminal,Args=new[] { "tml_config","Enabled" } });
            Check(terminal.Lines.Count>1,"local listen host console can read host configuration");ZNet.instance.Dedicated=true;
            Terminal.ConsoleCommand.Execute(new Terminal.ConsoleEventArgs { Context=terminal,Args=new[] { "tml_config","OuterTransitionDelaySeconds","2" } });Check(settings.Outer==2,"dedicated local console can apply configuration");
            Plugin.Network.Reset();Plugin.Network.Register(remote);Hello(remote);remote.m_rpc.Sent.Clear();
            for(int i=0;i<5000;i++)Plugin.Network.Send(22,Message.Status,delegate(ZPackage p) { p.Write(1); });
            var dictionary=(IDictionary)typeof(Network).GetField("connections",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Plugin.Network);var connection=dictionary[remote.m_rpc];
            var queue=(System.Collections.ICollection)connection.GetType().GetField("Out",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(connection);
            Check(queue.Count<=2048,"session/config changes preserve bounded existing outbound queues");
            ZNet.instance.Peers.Clear();Plugin.Network.Poll(10);Check(dictionary.Count==0,"disconnect removes queued state and per-peer admin limiter");
            File.Delete(path);
            Console.WriteLine("PASS: "+checks+" production session/config/network boundary assertions (managed fixtures; no live multiplayer claim).");
        }
        catch(Exception error) { Console.Error.WriteLine(error);Environment.ExitCode=1; }
    }
}
