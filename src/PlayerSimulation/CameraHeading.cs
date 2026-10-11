namespace TooManyLags.PlayerSimulation
{
    internal static class CameraHeading
    {
        internal static float Read(ZDO player, long peer, bool available)
        { return available ? TooManyLags.CameraHeading.Read(player, peer) : player.GetRotation().eulerAngles.y; }
    }
}
