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
    public void Invoke(string name, params object[] values) { Sends++; Last = name; Values = values; }
    public void Int(string name, int value, ZRpc source) { ((Action<ZRpc, int>)hooks[name])(source, value); }
    public void Float(string name, float value) { ((Action<ZRpc, float>)hooks[name])(this, value); }
}
namespace TooManyLags
{
    internal static class Plugin
    {
        internal static float Interval = .05f, Applied;
        internal static float CameraInterval() { return Interval; }
        internal static void ApplyCameraInterval(float value) { Applied = value; }
        internal static string ConfigCommand(string text, bool server) { return "fixture"; }
    }
}
internal static class CameraIntervalRecoveryTests
{
    private const string Request = "TooManyLags_CameraRequest_1", Reply = "TooManyLags_CameraInterval_1";
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
        UnityEngine.Time.realtimeSinceStartupAsDouble = 1.99; ConfigConsole.RequestCamera();
        Check(destination.m_rpc.Sends == 1, "pending settings request is throttled");
        UnityEngine.Time.realtimeSinceStartupAsDouble = 2; ConfigConsole.RequestCamera();
        Check(destination.m_rpc.Sends == 2, "missing reply allows a bounded retry");
        ZNet.instance = server; source.Ready = true;
        source.m_rpc.Int(Request, 2, source.m_rpc);
        Check(source.m_rpc.Sends == 0, "unknown request protocol rejected");
        source.m_rpc.Int(Request, 1, new ZRpc());
        Check(source.m_rpc.Sends == 0, "foreign socket cannot request this peer's settings");
        source.m_rpc.Int(Request, 1, source.m_rpc);
        Check(source.m_rpc.Sends == 1 && source.m_rpc.Last == Reply && (float)source.m_rpc.Values[0] == .05f, "server returns authoritative interval after lost join push");
        source.m_rpc.Int(Request, 1, source.m_rpc);
        Check(source.m_rpc.Sends == 1, "server rate limits duplicate requests");
        ZNet.instance = client; destination.m_rpc.Float(Reply, (float)source.m_rpc.Values[0]);
        Check(ConfigConsole.CameraConfigured && Plugin.Applied == .05f, "authenticated reply releases publication gate");
        for (int i = 0; i < 1000; i++) { UnityEngine.Time.realtimeSinceStartupAsDouble += .05; ConfigConsole.RequestCamera(); }
        Check(destination.m_rpc.Sends == 2, "no requests after successful configuration over 1000 frame checks");
        var newPeer = new ZNetPeer { Ready = true, m_server = true };
        client.Peer = newPeer; ConfigConsole.Connect(newPeer); ConfigConsole.RequestCamera();
        Check(!ConfigConsole.CameraConfigured && newPeer.m_rpc.Sends == 1, "reconnect requests immediately despite old retry deadline");
        destination.m_rpc.Float(Reply, .2f);
        Check(!ConfigConsole.CameraConfigured && Plugin.Applied == .05f, "reply from old connection cannot configure new session");
        newPeer.m_rpc.Float(Reply, .01f);
        Check(!ConfigConsole.CameraConfigured, "invalid interval leaves gate closed");
        newPeer.m_rpc.Float(Reply, .1f);
        Check(ConfigConsole.CameraConfigured && Plugin.Applied == .1f, "new server setting accepted");
        ConfigConsole.Reset(); Check(!ConfigConsole.CameraConfigured, "shutdown clears confirmation");
        ZNet.instance = server; ConfigConsole.RequestCamera();
        Check(source.m_rpc.Sends == 1, "dedicated server does not run the client request loop");
    }
}
