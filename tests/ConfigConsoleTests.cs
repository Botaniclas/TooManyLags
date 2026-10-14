using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using TooManyLags;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object item) { return item != null; } }
    public static class Time { public static double realtimeSinceStartupAsDouble; }
}
namespace TooManyLags.GridHandoffs
{
    // Grid transport is tested separately; this fixture covers config commands.
    internal static class Protocol { internal static void Offer(ZNetPeer peer) { } }
}
public sealed class Terminal : UnityEngine.Object
{
    public readonly List<string> Lines = new List<string>();
    public void AddString(string text) { Lines.Add(text); }
    public delegate void ConsoleEvent(ConsoleEventArgs args);
    public sealed class ConsoleEventArgs { public Terminal Context; public string[] Args; }
    public sealed class ConsoleCommand
    {
        internal static ConsoleEvent Execute;
        public ConsoleCommand(string name, string help, ConsoleEvent action) { Execute = action; }
    }
}
public sealed class ZSocket { public string Host = "authenticated-host"; public string GetHostName() { return Host; } }
public sealed class ZNetPeer
{
    public ZRpc m_rpc = new ZRpc(); public ZSocket m_socket = new ZSocket(); public long m_uid = 7; public bool m_server;
    public bool Ready = true; public bool IsReady() { return Ready; }
}
public sealed class ZNet : UnityEngine.Object
{
    public static ZNet instance; public bool Dedicated, Admin; public string CheckedIdentity;
    public ZNetPeer Peer; public bool IsDedicated() { return Dedicated; }
    public bool IsServer() { return Dedicated; }
    public ZNetPeer GetPeer(long id) { return Peer != null && Peer.m_uid == id ? Peer : null; }
    public ZNetPeer GetServerPeer() { return Peer; }
    public List<ZNetPeer> GetPeers() { return new List<ZNetPeer> { Peer }; }
    public bool IsAdmin(string host) { CheckedIdentity = host; return Admin; }
}
public sealed class ZRpc
{
    private readonly Dictionary<string, Delegate> hooks = new Dictionary<string, Delegate>();
    public int Sends; public string LastMethod, Text; public int Ticket;
    public float Camera, Overlap;
    public void Register<A>(string name, Action<ZRpc, A> action) { hooks[name] = action; }
    public void Register<A, B>(string name, Action<ZRpc, A, B> action) { hooks[name] = action; }
    public void Register<A, B, C>(string name, Action<ZRpc, A, B, C> action) { hooks[name] = action; }
    public void Invoke(string name, params object[] values) { Sends++; LastMethod = name; if (values[0] is float) { Camera = (float)values[0]; if (values.Length > 1) Overlap = (float)values[1]; } else if (values[0] is string) { Text = (string)values[0]; Ticket = (int)values[1]; } }
    public void Receive(string name, string text, int ticket) { ((Action<ZRpc, string, int>)hooks[name])(this, text, ticket); }
    public void ReceiveCamera(float value, float overlap = 30f) { ((Action<ZRpc, float, float>)hooks["TooManyLags_CameraSettings_2"])(this, value, overlap); }
    public void RequestCamera(int protocol) { ((Action<ZRpc, int>)hooks["TooManyLags_CameraRequest_1"])(this, protocol); }
}
namespace TooManyLags
{
    internal static class Plugin
    {
        internal static int Calls, OwnerCalls; internal static long OwnerPeer; internal static string OwnerFilter; internal static bool Server;
        internal static float Interval = .1f, Applied, Overlap = 30f, AppliedOverlap;
        internal static float CameraInterval() { return Interval; }
        internal static void ApplyCameraInterval(float interval) { Applied = interval; }
        internal static float CameraOverlap() { return Overlap; }
        internal static float CameraPredictionSeconds() { return .1f; }
        internal static void ApplyCameraSettings(float interval, float overlap, float prediction = 0f) { Applied = interval; AppliedOverlap = overlap; }
        internal static string ConfigCommand(string text, bool server) { Calls++; Server = server; return "reply"; }
        internal static string OwnerStatus(long peer, string filter = null) { OwnerCalls++; OwnerPeer=peer; OwnerFilter=filter; return "SERVER owner status"; }
        internal static int SessionFlags() { return 3; }
        internal static void ApplyServerSession(bool enabled, bool mobs) { }
        internal static void ClearClientSession() { }
        internal static void Report(string message) { }
    }
}
internal static class ConfigConsoleTests
{
    private static int checks;
    private static void Check(bool test, string message) { checks++; if (!test) throw new Exception(message); }
    public static int Main(string[] args)
    {
        try
        {
            typeof(Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "fixture.exe"), null, null, null });
            Service(args[0]); Endpoint(); OwnerEndpoint();
            Console.WriteLine("PASS: " + checks + " live-config validation, persistence, scope, guard-state and authenticated endpoint checks."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void OwnerEndpoint()
    {
        var peer=new ZNetPeer();ZNet.instance=new ZNet{Dedicated=true,Peer=peer};
        ConfigConsole.Connect(peer);UnityEngine.Time.realtimeSinceStartupAsDouble=100;
        int calls=Plugin.Calls;
        peer.m_rpc.Receive("TooManyLags_Config_1","owner_status",1);
        Check(Plugin.OwnerCalls==0&&peer.m_rpc.Text.Contains("admin rights"),"ownership snapshot rejects non-admin before scanning");
        ZNet.instance.Admin=true;peer.m_rpc.Receive("TooManyLags_Config_1","owner_status",2);
        Check(Plugin.OwnerCalls==1&&Plugin.OwnerPeer==peer.m_uid&&Plugin.Calls==calls&&peer.m_rpc.Text=="SERVER owner status","legacy config RPC selects authenticated requesting player without changing config");
        peer.m_rpc.Receive("TooManyLags_Config_1","owner_status",3);
        Check(Plugin.OwnerCalls==1&&peer.m_rpc.Text.Contains("two seconds"),"ownership snapshot has separate on-demand rate limit");
        UnityEngine.Time.realtimeSinceStartupAsDouble=102;peer.m_rpc.Receive("TooManyLags_Config_1","owner_status",4);
        Check(Plugin.OwnerCalls==2,"explicit snapshot can run again after its rate limit");
        UnityEngine.Time.realtimeSinceStartupAsDouble=104;peer.m_rpc.Receive("TooManyLags_Config_1","OWNER_STATUS   torch  ",5);
        Check(Plugin.OwnerCalls==3&&Plugin.OwnerFilter=="torch"&&Plugin.OwnerPeer==peer.m_uid&&Plugin.Calls==calls,"legacy RPC accepts a trimmed prefab filter through the same admin diagnostic endpoint");
        peer.m_rpc.Receive("TooManyLags_Config_1","owner_status Pickable",6);
        Check(Plugin.OwnerCalls==3&&peer.m_rpc.Text.Contains("two seconds"),"filtered snapshots cannot bypass the shared diagnostic throttle");
        UnityEngine.Time.realtimeSinceStartupAsDouble=106;peer.m_rpc.Receive("TooManyLags_Config_1","owner_statusExtra torch",7);
        Check(Plugin.OwnerCalls==3&&Plugin.Calls==calls+1,"similar config keys do not execute diagnostics");
        ZNet.instance.Admin=false;UnityEngine.Time.realtimeSinceStartupAsDouble=108;peer.m_rpc.Receive("TooManyLags_Config_1","owner_status torch",8);
        Check(Plugin.OwnerCalls==3&&peer.m_rpc.Text.Contains("admin rights"),"filtered snapshots retain admin authorization");
        ZNet.instance.Peer=new ZNetPeer();UnityEngine.Time.realtimeSinceStartupAsDouble=110;
        peer.m_rpc.Receive("TooManyLags_Config_1","owner_status",5);
        Check(Plugin.OwnerCalls==3,"detached socket cannot inspect ownership");ConfigConsole.Reset();
    }
    private static void Service(string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "config.cfg");
        ConfigFile config = new ConfigFile(path, false); config.SaveOnConfigSet = false;
        ConfigEntry<float> outer = config.Bind("05", "OuterTransitionDelaySeconds", 3f, new ConfigDescription("Server only", new AcceptableValueRange<float>(0, 30)));
        ConfigEntry<float> camera = config.Bind("08", "CameraHeadingCheckIntervalSeconds", .1f, new ConfigDescription("Server only", new AcceptableValueRange<float>(.05f, 5)));
        ConfigEntry<float> angle = config.Bind("08", "CameraDirectionOverlapDegrees", 30f, new ConfigDescription("Server only", new AcceptableValueRange<float>(0, 45)));
        ConfigEntry<float> prediction = config.Bind("08", "CameraRotationPredictionSeconds", .1f, new ConfigDescription("Server only", new AcceptableValueRange<float>(0, .5f)));
        ConfigEntry<float> overlap = config.Bind("05", "ServerOverlapCooldownSeconds", 1f, new ConfigDescription("Server only", new AcceptableValueRange<float>(0, 30)));
        ConfigEntry<bool> logs = config.Bind("01", "EnableLogs", false, "Local");
        ConfigEntry<int> ping = config.Bind("09", "ServerFallbackPingMs", 200, new ConfigDescription("Server only", new AcceptableValueRange<int>(0, 2000)));
        ConfigEntry<float> size = config.Bind("04", "GridCellSizeMeters", 4f, new ConfigDescription("Server only; restart required", new AcceptableValueList<float>(4f, 6.4f, 8f, 16f)));
        ConfigEntry<string> shape = config.Bind("04", "GridCoveragePattern", "Rounded7x7", new ConfigDescription("Server only; restart required", new AcceptableValueList<string>("Rounded5x5", "Square3x3", "Rounded7x7")));
        ConfigEntry<bool> ai = config.Bind("09", "EnableAIOwnership", true, "Server only; restart required");
        ConfigEntry<bool> enabled = config.Bind("01", "Enabled", true, "Server only; restart required");
        ConfigEntry<bool> doors = config.Bind("07", "Doors", true, "Server only; restart required");
        ConfigEntry<bool> experimental = config.Bind("07 - Experimental Interaction Protection", "EnableExperimentalInteractionProtection", false, "Server only; restart required. Experimental.");
        outer.Value = 3; camera.Value = .1f; overlap.Value = 1f; logs.Value = false; ping.Value = 200;
        size.Value = 4; shape.Value = "Rounded7x7"; ai.Value = true; enabled.Value = true; doors.Value = true; experimental.Value = false; config.Save();
        int applies = 0; string changed = null;
        ConfigCommands commands = new ConfigCommands(config, delegate(string key) { applies++; changed = key; });
        Check(commands.Execute("outertransitiondelayseconds 0,2", true).Contains("applied") && outer.Value == .2f && changed == outer.Definition.Key && applies == 1, "canonical key, invariant/comma input and runtime callback");
        ConfigFile saved = new ConfigFile(path, false);
        Check(saved.Bind("05", outer.Definition.Key, 9f, "").Value == .2f, "persists to config file");
        Check(!config.SaveOnConfigSet, "preserves original autosave mode");
        foreach (string bad in new[] { "-1", "31", "NaN", "Infinity", "1e100", "invalid" })
            Check(commands.Execute("OuterTransitionDelaySeconds " + bad, true).Contains("Invalid value") && outer.Value == .2f && applies == 1, "rejects invalid/out-of-range float " + bad);
        Check(commands.Execute("ServerFallbackPingMs 0", true).Contains("applied") && ping.Value == 0, "integer config");
        Check(commands.Execute("ServerFallbackPingMs 0.2", true).Contains("Invalid value"), "rejects fractional integer");
        Check(commands.Execute("CameraHeadingCheckIntervalSeconds 0.05", false).Contains("Wrong setting scope") && camera.Value == .1f, "client cannot change server camera setting locally");
        Check(commands.Execute("OuterTransitionDelaySeconds 1", false).Contains("Wrong setting scope") && outer.Value == .2f, "client cannot change server setting locally");
        Check(commands.Execute("CameraHeadingCheckIntervalSeconds 0.05", true).Contains("applied") && camera.Value == .05f, "server camera interval range");
        Check(commands.Execute("EnableLogs true", false).Contains("applied") && logs.Value, "local logging");
        int before = applies;
        Check(!ConfigCommands.Live(experimental.Definition.Key), "experimental handoff mode is startup-only");
        string modeReply = commands.Execute("EnableExperimentalInteractionProtection true", true);
        Check(modeReply.Contains("Takes effect after server restart") && !modeReply.Contains("every client") && experimental.Value && applies == before, "experimental toggle saves server choice without changing in-flight safeguards");
        Check(commands.Execute("EnableExperimentalInteractionProtection", true).Contains("restart pending; active: False"), "experimental read distinguishes active and pending modes");
        Check(commands.Execute("EnableExperimentalInteractionProtection", false).Contains("Wrong setting scope"), "client-local console cannot override server guard choice");
        Check(new ConfigFile(path, false).Bind("07 - Experimental Interaction Protection", experimental.Definition.Key, false, "").Value, "experimental server choice survives restart");
        Check(commands.Execute("EnableExperimentalInteractionProtection false", true).Contains("no restart pending") && !experimental.Value && applies == before, "restore default without live mode teardown");
        Check(commands.Execute("EnableLogs true", false).Contains("already") && applies == before, "unchanged value has no writes/runtime work");
        Check(commands.Execute("GridCoveragePattern Square3x3", true).Contains("Invalid grid combination") && shape.Value == "Rounded7x7", "rejects square grid with 4-metre cells");
        Check(commands.Execute("GridCellSizeMeters 5", true).Contains("Invalid value") && size.Value == 4, "restart values obey allowed list");
        Check(commands.Execute("GridCellSizeMeters 6.4", true).Contains("Takes effect after server restart") && size.Value == 6.4f && applies == before, "fractional cell size is saved without live structural changes");
        Check(commands.Execute("GridCellSizeMeters 6,4", true).Contains("already") && size.Value == 6.4f, "comma input resolves to the same allowed fractional size");
        Check(commands.Execute("GridCoveragePattern Square3x3", true).Contains("restart") && shape.Value == "Square3x3", "6.4-metre cells support the square pattern");
        Check(commands.Execute("GridCellSizeMeters 4", true).Contains("Invalid grid combination") && size.Value == 6.4f, "fractional setting retains the existing 4-metre square restriction");
        Check(commands.Execute("GridCoveragePattern Rounded7x7", true).Contains("no restart pending"), "restore active pattern before existing restart tests");
        Check(commands.Execute("GridCellSizeMeters 16", true).Contains("Takes effect after server restart") && size.Value == 16 && applies == before, "structural value saved without live callback");
        Check(commands.Execute("GridCellSizeMeters", true).Contains("restart pending; active: 4"), "read distinguishes saved and active values");
        Check(commands.Execute("GridCellSizeMeters 16", true).Contains("already") && commands.Execute("GridCellSizeMeters 16", true).Contains("restart") && applies == before, "unchanged pending value retains restart feedback without callbacks");
        Check(commands.Execute("GridCoveragePattern Square3x3", true).Contains("restart") && shape.Value == "Square3x3" && applies == before, "string grid option uses saved cell size");
        Check(commands.Execute("GridCellSizeMeters 4", true).Contains("Invalid grid combination") && size.Value == 16, "cannot save an invalid grid combination");
        Check(commands.Execute("GridCoveragePattern invalid", true).Contains("Invalid value") && shape.Value == "Square3x3", "rejects unknown string option without clamping");
        string aiReply = commands.Execute("EnableAIOwnership false", true);
        Check(aiReply.Contains("restart") && !aiReply.Contains("every client") && !ai.Value && applies == before, "AI config saved for server restart without client config requirement or policy teardown");
        Check(commands.Execute("Enabled false", true).Contains("Takes effect after server restart") && !enabled.Value && applies == before, "global enable saves a server-only restart change");
        Check(commands.Execute("Enabled", false).Contains("Wrong setting scope") && commands.Execute("EnableAIOwnership false", false).Contains("Wrong setting scope"), "both enable flags reject local client overrides");
        Check(commands.Execute("Enabled true", true).Contains("no restart pending") && enabled.Value && applies == before, "restoring active server enable does not tear down a session");
        Check(commands.Execute("EnableAIOwnership true", true).Contains("no restart pending") && !commands.Execute("EnableAIOwnership", true).Contains("restart pending") && applies == before, "restoring active value cancels queued change without teardown");
        Check(commands.Execute("Doors false", true).Contains("restart") && !doors.Value && applies == before, "category change saved without rebuilding indexes");
        Check(commands.Execute("list", true).Contains("GridCellSizeMeters = 16 [restart pending; active: 4]"), "list marks pending restart and original active value");
        ConfigFile nextStart = new ConfigFile(path, false);
        Check(nextStart.Bind("04", "GridCellSizeMeters", 4f, "").Value == 16 && nextStart.Bind("04", "GridCoveragePattern", "Rounded7x7", "").Value == "Square3x3" && !nextStart.Bind("07", "Doors", true, "").Value, "next startup reads queued geometry/category values");
        Check(commands.Execute("missing 1", true).Contains("Unknown"), "unknown keys");
        Check(commands.Execute(new string('x', 257), true).Contains("Invalid"), "bounded input");
        Check(commands.Execute("list", true).Contains("OuterTransitionDelaySeconds") && commands.Execute("list", true).Contains("CameraHeading") && !commands.Execute("list", true).Contains("EnableLogs"), "server list scope");
        Check(commands.Execute("list", false).Contains("EnableLogs") && !commands.Execute("list", false).Contains("OuterTransition"), "local list scope");
        bool fail = true;
        ConfigCommands failure = new ConfigCommands(config, delegate(string key) { if (fail) { fail = false; throw new Exception("fixture"); } });
        Check(failure.Execute("OuterTransitionDelaySeconds 5", true).Contains("failed") && outer.Value == .2f, "apply failure restores value");
        Check(new ConfigFile(path, false).Bind("05", outer.Definition.Key, 9f, "").Value == .2f, "apply failure restores saved value");
        Check(new ConfigFile(path, false).Bind("04", "GridCellSizeMeters", 4f, "").Value == 16, "later live save/rollback preserves queued restart value");
        string blocked = Path.Combine(folder, "blocked.cfg"); Directory.CreateDirectory(blocked);
        ConfigFile cannotSave = new ConfigFile(blocked, false); cannotSave.SaveOnConfigSet = false;
        ConfigEntry<bool> toggle = cannotSave.Bind("07", "Doors", true, "Server only; restart required");
        ConfigCommands saveFailure = new ConfigCommands(cannotSave, delegate(string key) { throw new Exception("Restart setting must never apply."); });
        Check(saveFailure.Execute("Doors false", true).Contains("failed") && toggle.Value && !saveFailure.Execute("Doors", true).Contains("restart pending"), "failed restart save restores value and pending state without invoking live callback");
        Check(commands.Execute("ServerOverlapCooldownSeconds 0", true).Contains("applied") && overlap.Value == 0 && changed == overlap.Definition.Key,
            "server can disable new overlap cooldowns live");
        Check(commands.Execute("ServerOverlapCooldownSeconds 1", false).Contains("Wrong setting scope") && overlap.Value == 0,
            "client cannot change server overlap buffer locally");
        foreach (string bad in new[] { "-1", "31", "NaN", "Infinity" })
            Check(commands.Execute("ServerOverlapCooldownSeconds " + bad, true).Contains("Invalid value") && overlap.Value == 0,
                "invalid overlap buffer is rejected");
        Check(new ConfigFile(path, false).Bind("05", overlap.Definition.Key, 1f, "").Value == 0, "live overlap buffer persists");
        Check(commands.Execute("CameraDirectionOverlapDegrees 45", true).Contains("applied") && angle.Value == 45, "angular overlap supports live server tuning up to 45");
        Check(commands.Execute("CameraDirectionOverlapDegrees 0", true).Contains("applied") && angle.Value == 0, "zero angular overlap is supported");
        Check(commands.Execute("CameraDirectionOverlapDegrees 30", false).Contains("Wrong setting scope"), "client cannot set server angular overlap");
        Check(commands.Execute("CameraRotationPredictionSeconds 0.2", true).Contains("applied") && prediction.Value == .2f, "server can tune predictive time live");
        Check(commands.Execute("CameraRotationPredictionSeconds 0", true).Contains("applied") && prediction.Value == 0f, "zero disables predictive time");
        Check(commands.Execute("CameraRotationPredictionSeconds 0.1", false).Contains("Wrong setting scope"), "client cannot set prediction time");
        Check(commands.Execute("CameraRotationPredictionSeconds 0.6", true).Contains("Invalid value") && prediction.Value == 0f, "prediction range rejects oversized input");
        foreach (string bad in new[] { "-1", "46", "NaN", "Infinity" })
            Check(commands.Execute("CameraDirectionOverlapDegrees " + bad, true).Contains("Invalid value") && angle.Value == 0, "invalid angular overlap rejected");
    }
    private static void Endpoint()
    {
        ZNetPeer peer = new ZNetPeer(); ZNet.instance = new ZNet { Dedicated = true, Peer = peer };
        ConfigConsole.Connect(peer); UnityEngine.Time.realtimeSinceStartupAsDouble = 1;
        peer.m_rpc.Receive("TooManyLags_Config_1", "OuterTransitionDelaySeconds 1", 1);
        Check(Plugin.Calls == 0 && peer.m_rpc.Text.Contains("admin rights") && ZNet.instance.CheckedIdentity == peer.m_socket.Host, "native authenticated host/admin check before execution");
        ZNet.instance.Admin = true;
        peer.m_rpc.Receive("TooManyLags_Config_1", "list", 2);
        Check(Plugin.Calls == 1 && Plugin.Server && peer.m_rpc.Ticket == 2, "authorized server execution");
        Plugin.Interval = .05f; ConfigConsole.Ready(peer.m_rpc);
        Check(peer.m_rpc.LastMethod == "TooManyLags_CameraInterval_1" && peer.m_rpc.Camera == .05f, "join sync sends server camera interval");
        peer.m_rpc.RequestCamera(2);
        Check(peer.m_rpc.LastMethod == "TooManyLags_CameraSettings_2" && peer.m_rpc.Overlap == 30f, "new client negotiates authoritative angle without sending an unknown RPC to old clients");
        ConfigConsole.BroadcastCamera(.2f);
        Check(peer.m_rpc.Camera == .2f && peer.m_rpc.LastMethod == "TooManyLags_CameraSettings_2", "live camera change broadcasts timing and angle to negotiated clients");
        peer.m_rpc.Receive("TooManyLags_Config_1", "list", 3); peer.m_rpc.Receive("TooManyLags_Config_1", "list", 4);
        peer.m_rpc.Receive("TooManyLags_Config_1", "list", 5);
        Check(Plugin.Calls == 3, "four requests/second bound includes rejected requests");
        UnityEngine.Time.realtimeSinceStartupAsDouble = 2;
        peer.m_rpc.Receive("TooManyLags_Config_1", new string('x', 257), 6);
        Check(Plugin.Calls == 3, "rejects oversized remote payload");
        ZNet.instance.Peer = new ZNetPeer(); peer.m_rpc.Receive("TooManyLags_Config_1", "list", 7);
        Check(Plugin.Calls == 3, "rejects detached/stale peer");
        ConfigConsole.Register(); Terminal terminal = new Terminal();
        ZNet.instance = new ZNet { Peer = peer }; peer.m_server = true;
        Terminal.ConsoleCommand.Execute(new Terminal.ConsoleEventArgs { Context = terminal, Args = new[] { "tml_config", "EnableLogs", "true" } });
        Check(Plugin.Calls == 4 && !Plugin.Server, "local logging command does not send server setting");
        Terminal.ConsoleCommand.Execute(new Terminal.ConsoleEventArgs { Context = terminal, Args = new[] { "tml_config", "CameraHeadingCheckIntervalSeconds", "0.05" } });
        Check(Plugin.Calls == 4 && peer.m_rpc.LastMethod == "TooManyLags_Config_1" && peer.m_rpc.Text.Contains("CameraHeading"), "camera command requests server change");
        peer.m_rpc.ReceiveCamera(.05f);
        Check(Plugin.Applied == .05f && ConfigConsole.CameraConfigured, "client uses server camera setting");
        foreach (float angle in new[] { 0f, 30f, 45f }) { peer.m_rpc.ReceiveCamera(.05f, angle); Check(Plugin.AppliedOverlap == angle, "server angle is applied on client"); }
        foreach (float bad in new[] { -1f, 46f, Single.NaN, Single.PositiveInfinity }) peer.m_rpc.ReceiveCamera(.05f, bad);
        Check(Plugin.AppliedOverlap == 45f, "invalid server angle cannot replace current angle");
        foreach (float bad in new[] { Single.NaN, Single.PositiveInfinity, .01f, 6f }) peer.m_rpc.ReceiveCamera(bad);
        Check(Plugin.Applied == .05f, "rejects invalid server camera interval");
        ZNet.instance.Peer = new ZNetPeer(); peer.m_rpc.ReceiveCamera(.2f);
        Check(Plugin.Applied == .05f, "rejects camera update from stale server connection");
        ZNet.instance.Peer = peer;
        Terminal.ConsoleCommand.Execute(new Terminal.ConsoleEventArgs { Context = terminal, Args = new[] { "tml_config", "list" } });
        int ticket = peer.m_rpc.Ticket, lines = terminal.Lines.Count;
        peer.m_rpc.Receive("TooManyLags_ConfigReply_1", "stale", ticket + 1);
        Check(terminal.Lines.Count == lines, "rejects stale reply ticket");
        peer.m_rpc.Receive("TooManyLags_ConfigReply_1", "accepted", ticket);
        Check(terminal.Lines.Count == lines + 1 && terminal.Lines[lines] == "accepted", "accepts current server reply");
        peer.m_rpc.Receive("TooManyLags_ConfigReply_1", "duplicate", ticket);
        Check(terminal.Lines.Count == lines + 1, "single-use reply");
        ConfigConsole.Reset();
        Check(!ConfigConsole.CameraConfigured, "session shutdown clears camera confirmation");
    }
}
