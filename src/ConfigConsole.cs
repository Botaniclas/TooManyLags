using System;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    internal static class ConfigConsole
    {
        private const string RequestRpc = "TooManyLags_Config_1", ReplyRpc = "TooManyLags_ConfigReply_1";
        private const string CameraRpc = "TooManyLags_CameraInterval_1";
        private static bool registered;
        private static WeakReference terminal;
        private static ZNet network;
        private static int ticket;
        private static double until;
        private static ZNet cameraNetwork;
        internal static bool CameraConfigured { get { return ZNet.instance && ReferenceEquals(cameraNetwork, ZNet.instance); } }
        internal static void Register()
        {
            if (registered) return;
            new Terminal.ConsoleCommand("tml_config", "tml_config list | <Key> [value]. Server settings require admin; logging is local. Live settings are marked in the list.", new Terminal.ConsoleEvent(Run));
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
            if (peer.m_server && ZNet.instance && !ZNet.instance.IsDedicated()) cameraNetwork = null;
            double window = -100; int requests = 0;
            peer.m_rpc.Register<string, int>(RequestRpc, delegate(ZRpc rpc, string command, int id)
            {
                ZNet current = ZNet.instance;
                if (!current || !current.IsDedicated() || !peer.IsReady() || !ReferenceEquals(current.GetPeer(peer.m_uid), peer) || !ReferenceEquals(rpc, peer.m_rpc)) return;
                double now = Time.realtimeSinceStartupAsDouble;
                if (now - window >= 1) { window = now; requests = 0; }
                if (++requests > 4 || id <= 0 || command == null || command.Length > 256) return;
                // Same authenticated socket identity/admin list as vanilla's
                // remote admin commands; never trust a player name or ZDO owner.
                string reply = peer.m_socket != null && current.IsAdmin(peer.m_socket.GetHostName()) ?
                    Plugin.ConfigCommand(command, true) : "Server configuration requires admin rights (adminlist.txt).";
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
                if (!ZNet.instance || ZNet.instance.IsDedicated() || !peer.m_server || !peer.IsReady() ||
                    !ReferenceEquals(ZNet.instance.GetServerPeer(), peer) || !ReferenceEquals(rpc, peer.m_rpc) ||
                    Single.IsNaN(interval) || Single.IsInfinity(interval) || interval < .05f || interval > 5f) return;
                Plugin.ApplyCameraInterval(interval); cameraNetwork = ZNet.instance;
            });
        }
        internal static void Ready(ZRpc rpc)
        {
            if (!ZNet.instance || !ZNet.instance.IsDedicated()) return;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                if (peer != null && peer.IsReady() && ReferenceEquals(peer.m_rpc, rpc))
                { SendCamera(peer, Plugin.CameraInterval()); return; }
        }
        internal static void BroadcastCamera(float interval)
        {
            if (!ZNet.instance || !ZNet.instance.IsDedicated()) return;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) if (peer != null && peer.IsReady()) SendCamera(peer, interval);
        }
        private static void SendCamera(ZNetPeer peer, float interval)
        { try { if (peer.m_rpc != null) peer.m_rpc.Invoke(CameraRpc, interval); } catch (Exception) { } }
        private static void Print(Terminal context, string lines)
        { foreach (string line in lines.Split('\n')) context.AddString(line.TrimEnd('\r')); }
        internal static void Reset() { terminal = null; network = null; cameraNetwork = null; until = 0; }
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
