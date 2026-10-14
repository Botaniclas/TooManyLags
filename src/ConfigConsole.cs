using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    internal static class ConfigConsole
    {
        private const string RequestRpc = "TooManyLags_Config_1", ReplyRpc = "TooManyLags_ConfigReply_1";
        private const string CameraRpc = "TooManyLags_CameraInterval_1";
        private const string CameraSettingsRpc = "TooManyLags_CameraSettings_2";
        private const string LegacyCameraPredictionRpc = "TooManyLags_CameraSettings_3";
        private const string CameraPredictionRpc = "TooManyLags_CameraSettings_4";
        private const string CameraRequestRpc = "TooManyLags_CameraRequest_1";
        private static bool registered;
        private static WeakReference terminal;
        private static ZNet network;
        private static int ticket;
        private static double until;
        private static ZNet cameraNetwork;
        private static ZRpc cameraRequestedRpc;
        private static ZNet cameraRequestedNetwork;
        private static double cameraRetryAt;
        private static int cameraRequests, cameraProtocol;
        private static readonly HashSet<ZRpc> cameraPeers = new HashSet<ZRpc>();
        private static readonly HashSet<ZRpc> predictivePeers = new HashSet<ZRpc>();
        internal static bool CameraConfigured { get { return ZNet.instance && ReferenceEquals(cameraNetwork, ZNet.instance); } }
        internal static void Register()
        {
            if (registered) return;
            new Terminal.ConsoleCommand("tml_config", "tml_config list | <Key> [value]. Server settings require admin; logging is local. Restart settings are saved for the next restart.", new Terminal.ConsoleEvent(Run));
            registered = true;
        }
        private static void Run(Terminal.ConsoleEventArgs args)
        {
            if (args == null || !args.Context) return;
            if (args.Args.Length < 2)
            { args.Context.AddString("tml_config list | <Key> [value]. EnableLogs applies locally. Camera timing and ownership settings require server admin rights."); return; }
            string command = String.Join(" ", args.Args, 1, args.Args.Length - 1);
            if (ConfigCommands.Local(args.Args[1])) { Print(args.Context, Plugin.ConfigCommand(command, false)); return; }
            if (ZNet.instance && ZNet.instance.IsDedicated()) { Print(args.Context, Plugin.ConfigCommand(command, true)); return; }
            ZNetPeer server = ZNet.instance ? ZNet.instance.GetServerPeer() : null;
            if (server == null || !server.IsReady() || server.m_rpc == null)
            { args.Context.AddString("Connect to a dedicated server running this TooManyLags version first."); return; }
            if (command.Length > 256) { args.Context.AddString("Config command too long."); return; }
            ticket = ticket == Int32.MaxValue ? 1 : ticket + 1;
            terminal = new WeakReference(args.Context); network = ZNet.instance; until = Time.realtimeSinceStartupAsDouble + 10;
            try { server.m_rpc.Invoke(RequestRpc, command, ticket); }
            catch (Exception error) { args.Context.AddString("Config request failed: " + error.GetBaseException().Message); }
        }
        internal static void Connect(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null) return;
            ServerSession.Connect(peer);
            if (ZNet.instance && ZNet.instance.IsDedicated())
            {
                cameraPeers.RemoveWhere(DisconnectedCameraPeer);
                predictivePeers.RemoveWhere(DisconnectedCameraPeer);
            }
            if (peer.m_server && ZNet.instance && !ZNet.instance.IsDedicated()) { cameraNetwork = null; cameraProtocol = 0; }
            double window = -100; int requests = 0;
            double nextCameraRequest = -100;
            double nextOwnerStatus = -100;
            peer.m_rpc.Register<int>(CameraRequestRpc, delegate(ZRpc rpc, int protocol)
            {
                ZNet current = ZNet.instance;
                if ((protocol < 1 || protocol > 4) || !current || !current.IsDedicated() || !peer.IsReady() ||
                    !ReferenceEquals(current.GetPeer(peer.m_uid), peer) || !ReferenceEquals(rpc, peer.m_rpc)) return;
                double now = Time.realtimeSinceStartupAsDouble;
                if (now < nextCameraRequest) return;
                nextCameraRequest = now + 1;
                if (protocol >= 2) cameraPeers.Add(rpc);
                if (protocol == 4) predictivePeers.Add(rpc);
                GridHandoffs.Protocol.Offer(peer);
                SendCamera(peer, Plugin.CameraInterval());
            });
            peer.m_rpc.Register<string, int>(RequestRpc, delegate(ZRpc rpc, string command, int id)
            {
                ZNet current = ZNet.instance;
                if (!current || !current.IsDedicated() || !peer.IsReady() || !ReferenceEquals(current.GetPeer(peer.m_uid), peer) || !ReferenceEquals(rpc, peer.m_rpc)) return;
                double now = Time.realtimeSinceStartupAsDouble;
                if (now - window >= 1) { window = now; requests = 0; }
                if (++requests > 4 || id <= 0 || command == null || command.Length > 256) return;
                // Same authenticated socket identity/admin list as vanilla's
                // remote admin commands; never trust a player name or ZDO owner.
                string reply;
                if (peer.m_socket == null || !current.IsAdmin(peer.m_socket.GetHostName()))
                    reply = "Server configuration requires admin rights (adminlist.txt).";
                else if (String.Equals(command, "owner_status", StringComparison.OrdinalIgnoreCase))
                {
                    if (now < nextOwnerStatus) reply = "Owner status is limited to one snapshot every two seconds.";
                    else { nextOwnerStatus = now + 2; reply = Plugin.OwnerStatus(peer.m_uid); }
                }
                else reply = Plugin.ConfigCommand(command, true);
                try { rpc.Invoke(ReplyRpc, reply, id); } catch (Exception) { }
            });
            peer.m_rpc.Register<string, int>(ReplyRpc, delegate(ZRpc rpc, string reply, int id)
            {
                if (!ZNet.instance || ZNet.instance.IsDedicated() || !peer.m_server || !ReferenceEquals(network, ZNet.instance) ||
                    !ReferenceEquals(ZNet.instance.GetServerPeer(), peer) || !ReferenceEquals(rpc, peer.m_rpc) || id != ticket || terminal == null ||
                    Time.realtimeSinceStartupAsDouble > until || reply == null || reply.Length > 6000) return;
                Terminal target = terminal.Target as Terminal;
                terminal = null; network = null;
                if (target) Print(target, reply);
            });
            peer.m_rpc.Register<float>(CameraRpc, delegate(ZRpc rpc, float interval)
            {
                if (cameraProtocol >= 2 || !ZNet.instance || ZNet.instance.IsDedicated() || !peer.m_server || !peer.IsReady() ||
                    !ReferenceEquals(ZNet.instance.GetServerPeer(), peer) || !ReferenceEquals(rpc, peer.m_rpc) ||
                    Single.IsNaN(interval) || Single.IsInfinity(interval) || interval < .05f || interval > 5f) return;
                // Legacy timing replies do not certify the configurable angle.
                Plugin.ApplyCameraInterval(interval);
            });
            peer.m_rpc.Register<float, float>(CameraSettingsRpc, delegate(ZRpc rpc, float interval, float overlap)
            {
                if (cameraProtocol >= 3 || !ZNet.instance || ZNet.instance.IsDedicated() || !peer.m_server || !peer.IsReady() ||
                    !ReferenceEquals(ZNet.instance.GetServerPeer(), peer) || !ReferenceEquals(rpc, peer.m_rpc) ||
                    Single.IsNaN(interval) || Single.IsInfinity(interval) || interval < .05f || interval > 5f ||
                    Single.IsNaN(overlap) || Single.IsInfinity(overlap) || overlap < 0f || overlap > 45f) return;
                Plugin.ApplyCameraSettings(interval, overlap, 0f); cameraNetwork = ZNet.instance; cameraProtocol = 2;
            });
            peer.m_rpc.Register<float, float, float>(CameraPredictionRpc, delegate(ZRpc rpc, float interval, float overlap, float prediction)
            {
                if (!ZNet.instance || ZNet.instance.IsDedicated() || !peer.m_server || !peer.IsReady() ||
                    !ReferenceEquals(ZNet.instance.GetServerPeer(), peer) || !ReferenceEquals(rpc, peer.m_rpc) ||
                    Single.IsNaN(interval) || Single.IsInfinity(interval) || interval < .05f || interval > 5f ||
                    Single.IsNaN(overlap) || Single.IsInfinity(overlap) || overlap < 0f || overlap > 45f ||
                    Single.IsNaN(prediction) || Single.IsInfinity(prediction) || prediction < 0f || prediction > .5f) return;
                Plugin.ApplyCameraSettings(interval, overlap, prediction); cameraNetwork = ZNet.instance; cameraProtocol = 4;
            });
            // Earlier prediction servers cannot decode red forecast fields.
            // Keep their base angle/timing, but never publish the new masks.
            peer.m_rpc.Register<float, float, float>(LegacyCameraPredictionRpc, delegate(ZRpc rpc, float interval, float overlap, float prediction)
            {
                if (cameraProtocol >= 4 || !ZNet.instance || ZNet.instance.IsDedicated() || !peer.m_server || !peer.IsReady() ||
                    !ReferenceEquals(ZNet.instance.GetServerPeer(), peer) || !ReferenceEquals(rpc, peer.m_rpc) ||
                    Single.IsNaN(interval) || Single.IsInfinity(interval) || interval < .05f || interval > 5f ||
                    Single.IsNaN(overlap) || Single.IsInfinity(overlap) || overlap < 0f || overlap > 45f ||
                    Single.IsNaN(prediction) || Single.IsInfinity(prediction) || prediction < 0f || prediction > .5f) return;
                Plugin.ApplyCameraSettings(interval, overlap, 0f); cameraNetwork = ZNet.instance; cameraProtocol = 3;
            });
        }
        internal static void Ready(ZRpc rpc)
        {
            if (!ZNet.instance || !ZNet.instance.IsDedicated()) return;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                if (peer != null && peer.IsReady() && ReferenceEquals(peer.m_rpc, rpc))
                { ServerSession.Ready(peer); SendCamera(peer, Plugin.CameraInterval()); return; }
        }
        internal static void BroadcastCamera(float interval)
        {
            if (!ZNet.instance || !ZNet.instance.IsDedicated()) return;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) if (peer != null && peer.IsReady()) SendCamera(peer, interval);
        }
        internal static void RequestCamera()
        {
            if (CameraConfigured) return;
            ZNet current = ZNet.instance;
            if (!current || current.IsDedicated()) return;
            ZNetPeer server = current.GetServerPeer();
            if (server == null || !server.IsReady() || server.m_rpc == null) return;
            if (!ReferenceEquals(cameraRequestedNetwork, current) || !ReferenceEquals(cameraRequestedRpc, server.m_rpc))
            { cameraRequestedNetwork = current; cameraRequestedRpc = server.m_rpc; cameraRetryAt = Double.NegativeInfinity; cameraRequests = 0; }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < cameraRetryAt) return;
            // Only retry settings acquisition; never repeat a gameplay action.
            // Receiving the authenticated interval stops these requests entirely.
            cameraRetryAt = now + 2;
            // Old servers understand protocol 2 and disable predictive masks.
            int protocol = cameraRequests == 0 ? 4 : 2;
            if (cameraRequests == 0) cameraRequests = 1;
            try { server.m_rpc.Invoke(CameraRequestRpc, protocol); } catch (Exception) { }
        }
        internal static void RefreshCamera()
        { cameraNetwork = null; cameraProtocol = cameraRequests = 0; cameraRequestedRpc = null; cameraRequestedNetwork = null; RequestCamera(); }
        private static void SendCamera(ZNetPeer peer, float interval)
        {
            try
            {
                if (peer.m_rpc == null) return;
                if (predictivePeers.Contains(peer.m_rpc)) peer.m_rpc.Invoke(CameraPredictionRpc, interval, Plugin.CameraOverlap(), Plugin.CameraPredictionSeconds());
                else if (cameraPeers.Contains(peer.m_rpc)) peer.m_rpc.Invoke(CameraSettingsRpc, interval, Plugin.CameraOverlap());
                else peer.m_rpc.Invoke(CameraRpc, interval);
            }
            catch (Exception) { }
        }
        private static void Print(Terminal context, string lines)
        { foreach (string line in lines.Split('\n')) context.AddString(line.TrimEnd('\r')); }
        private static bool DisconnectedCameraPeer(ZRpc saved)
        { foreach (ZNetPeer peer in ZNet.instance.GetPeers()) if (peer != null && ReferenceEquals(peer.m_rpc, saved)) return false; return true; }
        internal static void Reset()
        { ServerSession.Reset(); terminal = null; network = null; cameraNetwork = null; until = 0; cameraRequestedRpc = null; cameraRequestedNetwork = null; cameraRetryAt = 0; cameraProtocol = cameraRequests = 0; cameraPeers.Clear(); predictivePeers.Clear(); }
    }
    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    internal static class ConfigConnectionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ZNetPeer peer) { ConfigConsole.Connect(peer); }
    }
    [HarmonyPatch(typeof(ZNet), "Shutdown")]
    internal static class ConfigShutdownPatch
    {
        [HarmonyPrefix]
        private static void Prefix() { ConfigConsole.Reset(); }
    }
    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class ConfigReadyPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ZRpc rpc) { ConfigConsole.Ready(rpc); }
    }
}
