using System;
using System.Collections.Generic;

namespace TooManyLagsStandalone
{
    // One owned-player ZDO integer uses native replication; no extra RPC,
    // timestamps or camera objects on the server.
    internal static class CameraHeading
    {
        internal static readonly int Key = (Plugin.Id + ".cameraCoverageConfigured").GetStableHashCode();
        private static float overlap = 30f;
        private static float prediction;
        internal static float Overlap { get { return overlap; } }
        internal static float Prediction { get { return prediction; } }
        internal static void Configure(float degrees, float seconds = 0f)
        {
            overlap = Single.IsNaN(degrees) || Single.IsInfinity(degrees) ? 30f : Math.Max(0f, Math.Min(45f, degrees));
            prediction = Single.IsNaN(seconds) || Single.IsInfinity(seconds) ? 0f : Math.Max(0f, Math.Min(.5f, seconds));
        }
        internal static int Coverage(float yaw) { return AuthorityGrid.HeadingCoverage(yaw, overlap); }
        internal static int Coverage(float x, float z)
        {
            if (Single.IsNaN(x) || Single.IsNaN(z) || Single.IsInfinity(x) || Single.IsInfinity(z) ||
                (double)x * x + (double)z * z < .000001d) return -1;
            return Coverage((float)(Math.Atan2(x, z) * (180d / Math.PI)));
        }
        internal static int Read(ZDO player, long peer, bool cameraAvailable, CameraWake wake = null)
        {
            bool owned = cameraAvailable && player.GetOwner() == peer;
            int heading = owned ? player.GetInt(Key, -1) : -1;
            if (wake != null && owned) wake.Track(player.m_uid, peer, heading);
            return heading >= 0 && heading < AuthorityGrid.CoverageCount ? heading : Coverage(player.GetRotation().eulerAngles.y);
        }
    }

    // At most one value record per currently sampled player. Packet events do
    // one ID lookup; only changed owned-player camera data wakes a grid pass.
    internal sealed class CameraWake
    {
        private struct Stamp { internal long Peer; internal int Coverage; }
        private readonly Dictionary<ZDOID, Stamp> players = new Dictionary<ZDOID, Stamp>();
        private static int Valid(int value) { return value >= 0 && value < AuthorityGrid.CoverageCount ? value : -1; }
        internal void Clear() { players.Clear(); }
        internal void Track(ZDOID id, long peer, int coverage) { players[id] = new Stamp { Peer = peer, Coverage = Valid(coverage) }; }
        internal bool Changed(ZDO data)
        {
            Stamp old;
            if (data == null || !players.TryGetValue(data.m_uid, out old) || data.GetOwner() != old.Peer || !data.IsValid() || ZDOMan.instance == null || !ReferenceEquals(ZDOMan.instance.GetZDO(data.m_uid), data)) return false;
            int coverage = Valid(data.GetInt(CameraHeading.Key, -1));
            if (coverage == old.Coverage) return false;
            old.Coverage = coverage; players[data.m_uid] = old; return true;
        }
        internal int Count { get { return players.Count; } }
    }
}
