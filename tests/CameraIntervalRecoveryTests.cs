using System;
using System.Collections.Generic;
using TooManyLags;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value != null; } }
    public static class Time { public static double realtimeSinceStartupAsDouble; }
}
public sealed class Terminal : UnityEngine.Object
{
    public void AddString(string text) { }
    public delegate void ConsoleEvent(ConsoleEventArgs args);
    public sealed class ConsoleEventArgs { public Terminal Context; public string[] Args; }
    public sealed class ConsoleCommand { public ConsoleCommand(string name, string help, ConsoleEvent action) { } }
}
public sealed class ZSocket { public string GetHostName() { return "fixture"; } }
public sealed class ZNetPeer
{
    public ZRpc m_rpc = new ZRpc(); public ZSocket m_socket = new ZSocket();
    public long m_uid = 7; public bool m_server; public bool Ready;
    public bool IsReady() { return Ready; }
}
public sealed class ZNet : UnityEngine.Object
{
    public static ZNet instance; public bool Dedicated; public ZNetPeer Peer;
    public bool IsDedicated() { return Dedicated; }
    public bool IsServer() { return Dedicated; }
    public bool IsAdmin(string name) { return false; }
    public ZNetPeer GetServerPeer() { return Peer; }
    public ZNetPeer GetPeer(long uid) { return Peer != null && Peer.m_uid == uid ? Peer : null; }
    public List<ZNetPeer> GetPeers() { return new List<ZNetPeer> { Peer }; }
}
public sealed class ZRpc
{
    private readonly Dictionary<string, Delegate> hooks = new Dictionary<string, Delegate>();
    public int Sends; public string Last; public object[] Values;
    public void Register<A>(string name, Action<ZRpc, A> callback) { hooks[name] = callback; }
    public void Register<A, B>(string name, Action<ZRpc, A, B> callback) { hooks[name] = callback; }
    public void Register<A, B, C>(string name, Action<ZRpc, A, B, C> callback) { hooks[name] = callback; }
    public void Invoke(string name, params object[] values) { Sends++; Last = name; Values = values; }
    public void Int(string name, int value, ZRpc source) { ((Action<ZRpc, int>)hooks[name])(source, value); }
    public void Pair(string name, int first, int second, ZRpc source) { ((Action<ZRpc, int, int>)hooks[name])(source, first, second); }
    public void Float(string name, float value, float overlap = 30f) { ((Action<ZRpc, float, float>)hooks[name])(this, value, overlap); }
    public void Predict(float interval, float overlap, float prediction, bool legacy = false) { ((Action<ZRpc, float, float, float>)hooks[legacy ? "TooManyLags_CameraSettings_3" : "TooManyLags_CameraSettings_4"])(this, interval, overlap, prediction); }
}
namespace TooManyLags
{
    internal static class Plugin
    {
        internal static float Interval = .05f, Applied, Overlap = 30f, AppliedOverlap, AppliedPrediction;
        internal static float CameraInterval() { return Interval; }
        internal static void ApplyCameraInterval(float value) { Applied = value; }
        internal static float CameraOverlap() { return Overlap; }
        internal static float CameraPredictionSeconds() { return .1f; }
        internal static void ApplyCameraSettings(float value, float overlap, float prediction = 0f) { Applied = value; AppliedOverlap = overlap; AppliedPrediction = prediction; }
        internal static string ConfigCommand(string text, bool server) { return "fixture"; }
        internal static string OwnerStatus(long peer, string filter = null) { return "fixture"; }
        internal static int Flags = 3, SessionApplies, SessionClears, SessionReports;
        internal static bool SessionEnabled, SessionMobs;
        internal static bool SessionFail = false;
        internal static int SessionFlags() { return Flags; }
        internal static void ApplyServerSession(bool enabled, bool mobs) { SessionApplies++; SessionEnabled = enabled; SessionMobs = mobs; if (SessionFail) throw new Exception("startup fixture"); }
        internal static void ClearClientSession() { SessionClears++; SessionEnabled = SessionMobs = false; }
        internal static void Report(string message) { SessionReports++; }
    }
}
internal static class CameraIntervalRecoveryTests
{
    private const string Request = "TooManyLags_CameraRequest_1", Reply = "TooManyLags_CameraSettings_2";
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    public static int Main()
    {
        try { Run(); Console.WriteLine("PASS: " + checks + " camera recovery/authentication/reconnect checks."); return 0; }
        catch (Exception error) { Console.WriteLine(error); return 1; }
    }
    private static void Run()
    {
        ConfigConsole.Reset();
        var source = new ZNetPeer { Ready = false };
        var server = new ZNet { Dedicated = true, Peer = source };
        ZNet.instance = server; ConfigConsole.Connect(source); ConfigConsole.Ready(source.m_rpc);
        Check(source.m_rpc.Sends == 0, "early join hook cannot push to an unready peer");
        var destination = new ZNetPeer { m_server = true, Ready = false };
        var client = new ZNet { Peer = destination };
        ZNet.instance = client; ConfigConsole.Connect(destination); ConfigConsole.RequestCamera();
        Check(destination.m_rpc.Sends == 0, "does not request before client peer is ready");
        destination.Ready = true; ConfigConsole.RequestCamera();
        Check(destination.m_rpc.Sends == 1 && destination.m_rpc.Last == Request, "ready client requests missing interval immediately");
        Check((int)destination.m_rpc.Values[0] == 4, "new client requests red-forecast capability first");
        UnityEngine.Time.realtimeSinceStartupAsDouble = 1.99; ConfigConsole.RequestCamera();
        Check(destination.m_rpc.Sends == 1, "pending settings request is throttled");
        UnityEngine.Time.realtimeSinceStartupAsDouble = 2; ConfigConsole.RequestCamera();
        Check(destination.m_rpc.Sends == 2, "missing reply allows a bounded retry");
        Check((int)destination.m_rpc.Values[0] == 2, "old server fallback requests only baseline angle settings");
        ZNet.instance = server; source.Ready = true;
        source.m_rpc.Int(Request, 5, source.m_rpc);
        Check(source.m_rpc.Sends == 0, "unknown request protocol rejected");
        Check(TooManyLags.GridHandoffs.Protocol.Offers == 0, "invalid protocol cannot advertise grid capability");
        source.m_rpc.Int(Request, 2, new ZRpc());
        Check(source.m_rpc.Sends == 0, "foreign socket cannot request this peer's settings");
        Check(TooManyLags.GridHandoffs.Protocol.Offers == 0, "foreign socket cannot advertise grid capability");
        source.m_rpc.Int(Request, 2, source.m_rpc);
        Check(source.m_rpc.Sends == 1 && source.m_rpc.Last == Reply && (float)source.m_rpc.Values[0] == .05f, "server returns authoritative interval after lost join push");
        Check(TooManyLags.GridHandoffs.Protocol.Offers == 1, "authenticated settings recovery also recovers the grid capability offer");
        source.m_rpc.Int(Request, 2, source.m_rpc);
        Check(source.m_rpc.Sends == 1, "server rate limits duplicate requests");
        Check(TooManyLags.GridHandoffs.Protocol.Offers == 1, "duplicate requests cannot bypass offer rate limit");
        ZNet.instance = client; destination.m_rpc.Float(Reply, (float)source.m_rpc.Values[0]);
        Check(ConfigConsole.CameraConfigured && Plugin.Applied == .05f && Plugin.AppliedOverlap == 30f, "authenticated settings release publication gate");
        for (int i = 0; i < 1000; i++) { UnityEngine.Time.realtimeSinceStartupAsDouble += .05; ConfigConsole.RequestCamera(); }
        Check(destination.m_rpc.Sends == 2, "no requests after successful configuration over 1000 frame checks");
        var newPeer = new ZNetPeer { Ready = true, m_server = true };
        client.Peer = newPeer; ConfigConsole.Connect(newPeer); ConfigConsole.RequestCamera();
        Check(!ConfigConsole.CameraConfigured && newPeer.m_rpc.Sends == 1, "reconnect requests immediately despite old retry deadline");
        destination.m_rpc.Float(Reply, .2f);
        Check(!ConfigConsole.CameraConfigured && Plugin.Applied == .05f, "reply from old connection cannot configure new session");
        newPeer.m_rpc.Float(Reply, .01f);
        Check(!ConfigConsole.CameraConfigured, "invalid interval leaves gate closed");
        newPeer.m_rpc.Float(Reply, .1f, 46f);
        Check(!ConfigConsole.CameraConfigured, "invalid angle also leaves gate closed");
        newPeer.m_rpc.Float(Reply, .1f);
        Check(ConfigConsole.CameraConfigured && Plugin.Applied == .1f, "new server setting accepted");
        Check(Plugin.AppliedPrediction == 0f, "old settings replies disable prediction");
        newPeer.m_rpc.Predict(.05f, 30f, .1f, true);
        Check(Plugin.AppliedPrediction == 0f, "old prediction server cannot activate red forecast masks");
        newPeer.m_rpc.Predict(.05f, 30f, .1f);
        Check(Plugin.Applied == .05f && Plugin.AppliedPrediction == .1f, "authenticated new reply enables prediction");
        newPeer.m_rpc.Float(Reply, .2f, 45f);
        Check(Plugin.Applied == .05f && Plugin.AppliedPrediction == .1f, "late old reply cannot downgrade predictive settings");
        newPeer.m_rpc.Predict(.2f, 45f, .2f, true);
        Check(Plugin.Applied == .05f && Plugin.AppliedPrediction == .1f, "late old prediction reply cannot downgrade red masks");
        foreach (float bad in new[] { Single.NaN, Single.PositiveInfinity, -.1f, .6f })
        { newPeer.m_rpc.Predict(.1f, 30f, bad); Check(Plugin.Applied == .05f && Plugin.AppliedPrediction == .1f, "invalid predictive settings rejected"); }
        ZNet.instance = server; source.m_rpc.Int(Request, 4, source.m_rpc);
        Check(source.m_rpc.Last == "TooManyLags_CameraSettings_4" && source.m_rpc.Values.Length == 3 && (float)source.m_rpc.Values[2] == .1f, "capable client receives authoritative predictive time");
        var oldClient = new ZNetPeer { Ready = true };
        server.Peer = oldClient; ConfigConsole.Connect(oldClient);
        oldClient.m_rpc.Int(Request, 3, oldClient.m_rpc);
        Check(oldClient.m_rpc.Last == Reply && oldClient.m_rpc.Values.Length == 2, "old predictive client receives only supported base coverage settings");
        server.Peer = source;
        ConfigConsole.Reset(); Check(!ConfigConsole.CameraConfigured, "shutdown clears confirmation");
        ZNet.instance = server; ConfigConsole.RequestCamera();
        Check(source.m_rpc.Sends == 2, "dedicated server does not run the client request loop");
    }
}
namespace TooManyLags.GridHandoffs
{
    // Only the camera-request hook is under test here; no transport simulated.
    internal static class Protocol { internal static int Offers; internal static void Offer(ZNetPeer peer) { Offers++; } }
}
