using System;
using HarmonyLib;
using Steamworks;

namespace TooManyLagsStandalone
{
    // Read existing transport telemetry; vanilla clients need no custom ping RPC.
    internal static class NativeLatency
    {
        private static readonly Func<ZSteamSocket, HSteamNetConnection> connection = SteamConnection();
        private static Func<ZSteamSocket, HSteamNetConnection> SteamConnection()
        {
            try { return FieldReader.Create<ZSteamSocket, HSteamNetConnection>(AccessTools.Field(typeof(ZSteamSocket), "m_con")); }
            catch { return null; } // Optional telemetry must never disable ownership.
        }
        private static float DedicatedRead(ZSteamSocket steam)
        {
            try
            {
                HSteamNetConnection handle = connection(steam);
                if (handle == HSteamNetConnection.Invalid) return Single.PositiveInfinity;
                SteamNetConnectionRealTimeStatus_t status = default(SteamNetConnectionRealTimeStatus_t);
                SteamNetConnectionRealTimeLaneStatus_t lane = default(SteamNetConnectionRealTimeLaneStatus_t);
                return SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(handle, ref status, 0, ref lane) == EResult.k_EResultOK &&
                    status.m_flConnectionQualityLocal > 0f && status.m_nPing >= 0 ? status.m_nPing : Single.PositiveInfinity;
            }
            catch { return Single.PositiveInfinity; }
        }
        internal static float Read(ZRpc rpc)
        {
            try
            {
                ISocket socket = rpc != null ? rpc.GetSocket() : null;
                if (socket == null || !socket.IsConnected()) return Single.PositiveInfinity;
                ZSteamSocket steam = socket as ZSteamSocket;
                if (ZNet.instance && ZNet.instance.IsDedicated() && steam != null && connection != null)
                {
                    float dedicatedPing = DedicatedRead(steam);
                    if (!Single.IsInfinity(dedicatedPing)) return dedicatedPing;
                }
                float local, remote, outgoing, incoming; int ping;
                socket.GetConnectionQuality(out local, out remote, out ping, out outgoing, out incoming);
                return local > 0f && ping >= 0 ? ping : Single.PositiveInfinity;
            }
            catch { return Single.PositiveInfinity; }
        }
    }
}
