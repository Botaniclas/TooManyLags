namespace BotanicOwnerDebug
{
    internal enum DisplayMode { Off, All, InteractiveAndMobs, MobsOnly }
    internal static class DisplayModes
    {
        internal static DisplayMode Current(bool enabled, bool interactiveOnly, bool mobsOnly = false)
        { return !enabled ? DisplayMode.Off : mobsOnly ? DisplayMode.MobsOnly : interactiveOnly ? DisplayMode.InteractiveAndMobs : DisplayMode.All; }
        internal static DisplayMode Next(DisplayMode mode)
        { return mode == DisplayMode.Off ? DisplayMode.MobsOnly : mode == DisplayMode.MobsOnly ? DisplayMode.InteractiveAndMobs : mode == DisplayMode.InteractiveAndMobs ? DisplayMode.All : DisplayMode.Off; }
    }
}
