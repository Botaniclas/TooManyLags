using System;

namespace TooManyLags
{
    // One owned-player ZDO integer uses native replication; no extra RPC,
    // per-peer dictionary, timestamps or camera objects on the server.
    internal static class CameraHeading
    {
        internal static readonly int Key = (Plugin.Id + ".cameraSector").GetStableHashCode();
        internal static int Sector(float yaw)
        {
            if (Single.IsNaN(yaw) || Single.IsInfinity(yaw)) return -1;
            yaw %= 360f; if (yaw < 0f) yaw += 360f;
            return (int)((yaw + 22.5f) / 45f) & 7;
        }
        internal static int Sector(float x, float z)
        {
            if (Single.IsNaN(x) || Single.IsNaN(z) || Single.IsInfinity(x) || Single.IsInfinity(z) ||
                (double)x * x + (double)z * z < .000001d) return -1;
            return Sector((float)(Math.Atan2(x, z) * (180d / Math.PI)));
        }
        internal static float Read(ZDO player, long peer)
        {
            int heading = player.GetOwner() == peer ? player.GetInt(Key, -1) : -1;
            return heading >= 0 && heading < 8 ? heading * 45f : player.GetRotation().eulerAngles.y;
        }
    }
}
