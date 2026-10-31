using System;
using TooManyLags;
public interface ISocket { bool IsConnected(); void GetConnectionQuality(out float local, out float remote, out int ping, out float sent, out float received); }
public sealed class TimingSocket : ISocket
{
    public bool Connected = true; public int Ping, Reads;
    public bool IsConnected() { return Connected; }
    public void GetConnectionQuality(out float local, out float remote, out int ping, out float sent, out float received)
    { Reads++; local = remote = sent = received = 0f; ping = Ping; }
}
public sealed class ZRpc { public ISocket Socket; public ISocket GetSocket() { return Socket; } }
public sealed class ZNetPeer { public ZRpc m_rpc = new ZRpc(); }
namespace TooManyLags
{
    internal sealed class FgnCombatBridge
    {
        internal bool Known; internal float Milliseconds; internal int Reads;
        internal bool TryMeasuredRtt(ZNetPeer peer, out float milliseconds)
        { Reads++; milliseconds = Milliseconds; return Known; }
    }
    internal static class MobBridgeTimeline
    { internal static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); } }
}
internal static class BridgeLatencyTests
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    public static int Main()
    {
        try
        {
            var socket = new TimingSocket(); var peer = new ZNetPeer(); peer.m_rpc.Socket = socket;
            var bridge = new FgnCombatBridge();
            Check(MobBridgeLatency.Measure(peer, bridge) == .001f && socket.Reads == 1, "Connected rounded-zero RTT became a 100/400ms recording");
            socket.Ping = 4; Check(MobBridgeLatency.Measure(peer, bridge) == .004f, "Fresh raw LAN timing unavailable during cold echo");
            bridge.Known = true; bridge.Milliseconds = 12.5f;
            int prior = socket.Reads; Check(MobBridgeLatency.Measure(peer, bridge) == .0125f && socket.Reads == prior, "Valid echo ignored or unnecessary socket work");
            bridge.Milliseconds = 500f; int reads = bridge.Reads;
            Check(MobBridgeLatency.Measure(peer, bridge, .05f) == .05f && bridge.Reads == reads && socket.Reads == prior, "50ms test inherited stale 500ms measurement");
            foreach(float latency in new[]{.001f,.01f,.02f,.05f,.1f,.5f,1f})
                Check(MobBridgeLatency.Measure(peer, bridge, latency) == latency, "Injected RTT changed before observed matching");
            bridge.Known = false; socket.Connected = false;
            Check(MobBridgeLatency.Measure(peer, bridge) == .1f, "Disconnected measurement treated as zero latency");
            Check(MobBridgeLatency.Measure(null, bridge) == .1f, "Missing route treated as measured LAN");
            socket.Connected = true; socket.Ping = -1;
            Check(MobBridgeLatency.Measure(peer, bridge) == .1f, "Invalid transport RTT accepted");
            bridge.Known = true; bridge.Milliseconds = Single.NaN; socket.Ping = 8;
            Check(MobBridgeLatency.Measure(peer, bridge) == .008f, "Invalid echo prevented raw transport fallback");
            Console.WriteLine("PASS: " + checks + " presentation RTT checks; cold LAN, valid echo, stale injected-test timing, unknown/disconnected routes and no extra polling."); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
