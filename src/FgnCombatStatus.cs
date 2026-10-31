using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags
{
    internal static class FgnCombatStatus
    {
        private static bool registered;
        private static WeakReference responseTerminal;
        private static ZNet responseNetwork;
        private static int request;
        private static double responseUntil;
        internal static void Reset() { responseTerminal = null; responseNetwork = null; responseUntil = 0; }
        internal static void Connect(ZNetPeer peer)
        {
            if (!Plugin.Diagnostics || peer == null || peer.m_rpc == null) return;
            ConnectEnabled(peer);
        }
        private static void ConnectEnabled(ZNetPeer peer)
        {
            // Separate method keeps compiler-generated callback closures behind
            // the log gate; C# otherwise allocates them before an early return.
            // Idle sessions do no diagnostic work. Requests are bounded to the
            // command's six mobs; a remote peer cannot trigger an unbounded scan.
            double window = -100; int replies = 0;
            try
            {
                peer.m_rpc.Register<ZDOID, int>("TooManyLags_AI_Status", delegate(ZRpc rpc, ZDOID id, int ticket)
                {
                    if (!Plugin.Diagnostics || !ZNet.instance || !ZNet.instance.IsDedicated() || !peer.IsReady() || ticket <= 0 || id.IsNone()) return;
                    double now = Time.realtimeSinceStartupAsDouble;
                    if (now - window >= 2) { window = now; replies = 0; }
                    if (replies >= 6) return;
                    replies++;
                    string line;
                    try { line = FgnCombatOwnership.DescribeServerMob(id); }
                    catch (Exception error) { line = "SERVER status unavailable: " + error.GetBaseException().Message; }
                    try { peer.m_rpc.Invoke("TooManyLags_AI_StatusReply", ticket, line); }
                    catch (Exception) { } // A diagnostic send cannot disable gameplay hooks.
                });
                peer.m_rpc.Register<int, string>("TooManyLags_AI_StatusReply", delegate(ZRpc rpc, int ticket, string line)
                {
                    if (!Plugin.Diagnostics || !ZNet.instance || ZNet.instance.IsDedicated() || !peer.m_server ||
                        !ReferenceEquals(ZNet.instance.GetServerPeer(), peer) || !ReferenceEquals(responseNetwork, ZNet.instance) ||
                        ticket != request || responseTerminal == null || Time.realtimeSinceStartupAsDouble > responseUntil) return;
                    Terminal terminal = responseTerminal.Target as Terminal;
                    if (terminal && line != null && line.Length <= 3000) terminal.AddString(line);
                });
            }
            catch (Exception error) { Plugin.Warn("Optional AI status endpoint: " + error.Message); }
        }
        private static void RequestServer(Terminal terminal, List<ZDOID> mobs)
        {
            if (!ZNet.instance || ZNet.instance.IsDedicated() || mobs.Count == 0) return;
            ZNetPeer server = ZNet.instance.GetServerPeer();
            if (server == null || !server.IsReady() || server.m_rpc == null) return;
            request = request == Int32.MaxValue ? 1 : request + 1;
            responseTerminal = new WeakReference(terminal); responseNetwork = ZNet.instance;
            responseUntil = Time.realtimeSinceStartupAsDouble + 10;
            terminal.AddString("Requesting SERVER scan/watch status. Use TooManyLags " + Plugin.Version + " on server and clients. Only on this command.");
            foreach (ZDOID id in mobs) server.m_rpc.Invoke("TooManyLags_AI_Status", id, request);
        }
        internal static void Register()
        {
            if (!Plugin.Diagnostics || registered) return;
            new Terminal.ConsoleCommand("toomanylags_ai_status", "Show local and dedicated-server FGN AI ownership status for up to six mobs within 30 metres. Read-only; no continuous logging.", new Terminal.ConsoleEvent(Print));
            registered = true;
        }
        private static void Print(Terminal.ConsoleEventArgs args)
        {
            if (args == null || !args.Context) return;
            if (!Plugin.Diagnostics) { args.Context.AddString("EnableLogs must be enabled locally and on the server before requesting AI diagnostics."); return; }
            try
            {
                foreach (string line in FgnCombatOwnership.DescribeStatus()) args.Context.AddString(line);
                if (!Player.m_localPlayer) return;
                Vector3 point = Player.m_localPlayer.transform.position;
                List<KeyValuePair<float, Character>> nearby = new List<KeyValuePair<float, Character>>();
                int excluded = 0;
                foreach (Character mob in Character.GetAllCharacters())
                {
                    if (!mob || mob is Player) continue;
                    float distance = (mob.transform.position - point).sqrMagnitude;
                    if (distance > 900f) continue;
                    // These cannot join FGN's combat ownership policy. Keep them
                    // from occupying all six diagnostic slots in an animal pen.
                    if (mob.IsTamed() || mob.IsBoss()) { excluded++; continue; }
                    nearby.Add(new KeyValuePair<float, Character>(distance, mob));
                }
                nearby.Sort(delegate(KeyValuePair<float, Character> a, KeyValuePair<float, Character> b) { return a.Key.CompareTo(b.Key); });
                if (excluded != 0) args.Context.AddString("Excluded from AI ownership status: " + excluded + " tamed/boss mobs; their ownership policy is unchanged.");
                if (nearby.Count == 0) args.Context.AddString("No eligible loaded wild mobs within 30 metres.");
                List<ZDOID> ids = new List<ZDOID>();
                for (int i = 0; i < nearby.Count && i < 6; i++)
                {
                    Character mob = nearby[i].Value;
                    ZNetView view = mob.GetComponent<ZNetView>();
                    args.Context.AddString(mob.name + ": " + FgnCombatOwnership.DescribeMob(view && view.IsValid() ? view.GetZDO() : null, mob.GetComponent<BaseAI>()));
                    if (view && view.IsValid()) ids.Add(view.GetZDO().m_uid);
                }
                RequestServer(args.Context, ids);
            }
            catch (Exception error) { args.Context.AddString("Status query failed: " + error.GetBaseException().Message); }
        }
    }
}
