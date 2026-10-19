using System;
using System.Reflection;
using System.Reflection.Emit;
using Steamworks;

namespace TooManyLagsDedicatedAddon
{
    // Used only by this dedicated-server edition. Native Steam quality reads
    // enter the client API, which is unavailable on a headless Steam server.
    internal static class SocketLatency
    {
        private static readonly Func<ZSteamSocket, HSteamNetConnection> steamConnection = BindConnection();
        internal static bool HasSteamReader { get { return steamConnection != null; } }
        internal static int Read(ISocket socket)
        {
            ZSteamSocket steam = socket as ZSteamSocket;
            if (steam != null)
            {
                if (steamConnection == null) return 0;
                HSteamNetConnection handle = steamConnection(steam);
                if (handle == HSteamNetConnection.Invalid) return 0;
                var status = new SteamNetConnectionRealTimeStatus_t();
                var lane = new SteamNetConnectionRealTimeLaneStatus_t();
                return SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(handle, ref status, 0, ref lane) == EResult.k_EResultOK ? Math.Max(0, status.m_nPing) : 0;
            }
            float local, remote, outgoing, incoming; int ping;
            socket.GetConnectionQuality(out local, out remote, out ping, out outgoing, out incoming);
            return Math.Max(0, ping);
        }
        private static Func<ZSteamSocket, HSteamNetConnection> BindConnection()
        {
            try
            {
                FieldInfo field = typeof(ZSteamSocket).GetField("m_con", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null || field.FieldType != typeof(HSteamNetConnection)) return null;
                var method = new DynamicMethod("TooManyLagsServerSteamConnection", typeof(HSteamNetConnection), new[] { typeof(ZSteamSocket) }, typeof(SocketLatency).Module, true);
                ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
                return (Func<ZSteamSocket, HSteamNetConnection>)method.CreateDelegate(typeof(Func<ZSteamSocket, HSteamNetConnection>));
            }
            catch (Exception) { return null; } // Optional telemetry must not disable ownership.
        }
    }
}
