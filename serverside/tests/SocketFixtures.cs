using System;

namespace Steamworks
{
    public enum EResult { k_EResultOK = 1, k_EResultFail = 2 }
    public struct HSteamNetConnection : IEquatable<HSteamNetConnection>
    {
        public uint Value;
        public HSteamNetConnection(uint value) { Value = value; }
        public static readonly HSteamNetConnection Invalid = new HSteamNetConnection(0);
        public bool Equals(HSteamNetConnection other) { return Value == other.Value; }
        public override bool Equals(object other) { return other is HSteamNetConnection && Equals((HSteamNetConnection)other); }
        public override int GetHashCode() { return (int)Value; }
        public static bool operator ==(HSteamNetConnection a, HSteamNetConnection b) { return a.Equals(b); }
        public static bool operator !=(HSteamNetConnection a, HSteamNetConnection b) { return !a.Equals(b); }
    }
    public struct SteamNetConnectionRealTimeStatus_t { public int m_nPing; }
    public struct SteamNetConnectionRealTimeLaneStatus_t { }
    public static class SteamNetworkingSockets
    {
        public static int Calls;
        public static EResult GetConnectionRealTimeStatus(HSteamNetConnection handle, ref SteamNetConnectionRealTimeStatus_t status, int lanes, ref SteamNetConnectionRealTimeLaneStatus_t lane)
        { Calls++; throw new InvalidOperationException("Steamworks is not initialized."); }
    }
    public static class SteamGameServerNetworkingSockets
    {
        public static int Calls, Ping = 50;
        public static bool Throw, Fail;
        public static HSteamNetConnection LastHandle;
        public static EResult GetConnectionRealTimeStatus(HSteamNetConnection handle, ref SteamNetConnectionRealTimeStatus_t status, int lanes, ref SteamNetConnectionRealTimeLaneStatus_t lane)
        {
            Calls++; LastHandle = handle;
            if (Throw) throw new InvalidOperationException("Steamworks GameServer is not initialized.");
            status.m_nPing = Ping; return Fail ? EResult.k_EResultFail : EResult.k_EResultOK;
        }
    }
}
public sealed class ZSteamSocket : ISocket
{
    private Steamworks.HSteamNetConnection m_con;
    public ZSteamSocket(Steamworks.HSteamNetConnection handle) { m_con = handle; }
    public Steamworks.HSteamNetConnection ActualHandle { get { return m_con; } }
    public void GetConnectionQuality(out float local, out float remote, out int ping, out float outgoing, out float incoming)
    {
        var status = new Steamworks.SteamNetConnectionRealTimeStatus_t();
        var lane = new Steamworks.SteamNetConnectionRealTimeLaneStatus_t();
        Steamworks.SteamNetworkingSockets.GetConnectionRealTimeStatus(m_con, ref status, 0, ref lane);
        local = remote = outgoing = incoming = 0; ping = status.m_nPing;
    }
}
