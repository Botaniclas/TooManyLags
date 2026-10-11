namespace TooManyLags.PlayerSimulation
{
    internal static class CameraHeading
    {
        internal static int Read(ZDO player, long peer, bool available, TooManyLags.CameraWake wake)
        { return available ? TooManyLags.CameraHeading.Read(player, peer, wake) : TooManyLags.CameraHeading.Coverage(player.GetRotation().eulerAngles.y); }
    }
}
