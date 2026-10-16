using System;
using UnityEngine;

namespace TooManyLagsStandalone
{
    internal static class ConfigConsole
    {
        private static bool registered;
        private static WeakReference terminal;
        private static ZNet network;
        private static ZNetPeer requestedPeer;
        private static int ticket;
        private static float until;
        internal static void Register()
        {
            if (registered) return;
            new Terminal.ConsoleCommand("tml_config", "tml_config list | <Key> [value]. Host/server admin only; EnableLogs is local. Restart settings are saved for the next restart.", new Terminal.ConsoleEvent(Run));
            registered = true;
        }
        private static void Run(Terminal.ConsoleEventArgs args)
        {
            if (args == null || !args.Context) return;
            if (args.Args.Length < 2) { args.Context.AddString("tml_config list | <Key> [value]. Only EnableLogs is local; server changes require admin rights."); return; }
            string command = String.Join(" ", args.Args, 1, args.Args.Length - 1);
            if (command.Length > 256) { args.Context.AddString("Config command too long."); return; }
            if (ConfigCommands.Local(args.Args[1])) { Print(args.Context, Plugin.ConfigCommand(command, false)); return; }
            ZNet current = ZNet.instance;
            if (current && current.IsServer()) { Print(args.Context, Plugin.ConfigCommand(command, true)); return; }
            ZNetPeer server = current ? current.GetServerPeer() : null;
            if (server == null || !server.IsReady() || server.m_rpc == null || Plugin.Network == null || !Plugin.Network.Confirmed)
            { args.Context.AddString("Connect to a host/server running a compatible TooManyLags version first."); return; }
            ticket = ticket == Int32.MaxValue ? 1 : ticket + 1;
            terminal = new WeakReference(args.Context); network = current; requestedPeer = server; until = Time.unscaledTime + 10f;
            int id = ticket;
            Plugin.Network.ToServer(Message.ConfigRequest, delegate(ZPackage p) { p.Write(id); p.Write(command); });
            Plugin.Network.FlushAll();
        }
        internal static string Request(ZNetPeer peer, string command)
        {
            ZNet current = ZNet.instance;
            if (!current || !current.IsServer() || peer == null || !peer.IsReady() || peer.m_rpc == null ||
                !ReferenceEquals(current.GetPeer(peer.m_uid), peer) || command == null || command.Length > 256) return null;
            // Match vanilla remote-admin authentication: use the socket identity,
            // never the player name, announced UID or an owner-controlled ZDO.
            if (peer.m_socket == null || !current.IsAdmin(peer.m_socket.GetHostName())) return "Host/server configuration requires admin rights (adminlist.txt).";
            return Plugin.ConfigCommand(command, true);
        }
        internal static void Reply(ZNetPeer peer, int id, string reply)
        {
            ZNet current = ZNet.instance;
            if (!current || current.IsServer() || !ReferenceEquals(network, current) || !ReferenceEquals(requestedPeer, peer) ||
                !ReferenceEquals(current.GetServerPeer(), peer) || id != ticket || terminal == null || Time.unscaledTime > until || reply == null || reply.Length > 12000) return;
            Terminal context = terminal.Target as Terminal;
            Reset(); if (context) Print(context, reply);
        }
        private static void Print(Terminal context, string lines) { foreach (string line in lines.Split('\n')) context.AddString(line.TrimEnd('\r')); }
        internal static void Reset() { terminal = null; network = null; requestedPeer = null; until = 0; }
    }
}
