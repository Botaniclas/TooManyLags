#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;

namespace TooManyLags
{
    // Presentation timing cannot use FGN's conservative ownership-policy RTT:
    // that helper substitutes 400ms when echo/link status has no sample yet.
    internal static class MobBridgeLatency
    {
        internal static float Measure(ZNetPeer peer, FgnCombatBridge bridge, float injected = 0f)
        {
            // The test transport's known current delay supersedes stale echo
            // history from an earlier test. Actual delivery still controls join.
            if (MobBridgeTimeline.Finite(injected) && injected > 0f) return injected;
            float milliseconds;
            if (peer != null && bridge != null && bridge.TryMeasuredRtt(peer, out milliseconds) &&
                MobBridgeTimeline.Finite(milliseconds) && milliseconds > 0f)
                return milliseconds / 1000f;
            ISocket socket = peer == null || peer.m_rpc == null ? null : peer.m_rpc.GetSocket();
            if (socket == null || !socket.IsConnected()) return .1f;
            float local, remote, sent, received; int ping;
            socket.GetConnectionQuality(out local, out remote, out ping, out sent, out received);
            // A connected socket can report a valid rounded 0ms on LAN. The
            // 1ms epsilon keeps recording arithmetic defined, not a slow window.
            return ping >= 0 && ping <= 60000 ? Math.Max(.001f, ping / 1000f) : .1f;
        }
    }
}
#endif
