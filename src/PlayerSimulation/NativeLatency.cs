using System;
using System.Reflection;
using HarmonyLib;

namespace TooManyLags.PlayerSimulation
{
    internal static class NativeLatency
    {
        private delegate bool Echo(ZNetPeer peer, out float milliseconds);
        private static Echo echo;
        private static Func<ZNetPeer, int> ping;
        private static Func<ZNetPeer, bool> matched;
        internal static void Bind(Assembly fgn)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            Type echoes = fgn.GetType("FiresGhettoNetworkMod.ConnectionEcho", true);
            Type links = fgn.GetType("FiresGhettoNetworkMod.LinkController", true);
            Type gate = fgn.GetType("FiresGhettoNetworkMod.FgnVersionGate", true);
            echo = (Echo)Delegate.CreateDelegate(typeof(Echo), echoes.GetMethod("TryRtt", flags, null, new[] { typeof(ZNetPeer), typeof(float).MakeByRefType() }, null));
            ping = AccessTools.MethodDelegate<Func<ZNetPeer, int>>(links.GetMethod("PingMs", flags, null, new[] { typeof(ZNetPeer) }, null));
            matched = AccessTools.MethodDelegate<Func<ZNetPeer, bool>>(gate.GetMethod("IsMatched", flags, null, new[] { typeof(ZNetPeer) }, null));
        }
        internal static bool Matched(ZNetPeer peer) { return matched != null && matched(peer); }
        internal static float Read(ZRpc rpc)
        {
            try
            {
                if (!ZNet.instance || rpc == null) return Single.PositiveInfinity;
                foreach (ZNetPeer peer in ZNet.instance.GetPeers()) if (ReferenceEquals(peer.m_rpc, rpc) && Matched(peer))
                {
                    float value;
                    if (echo != null && echo(peer, out value)) return value;
                    int socket = ping != null ? ping(peer) : -1;
                    // LinkController returns zero when no status exists.
                    return socket > 0 ? socket : Single.PositiveInfinity;
                }
            }
            catch { }
            return Single.PositiveInfinity;
        }
    }
}
