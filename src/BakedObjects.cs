namespace TooManyLags
{
    // Optional stored-data compatibility, not a plugin/API dependency. Parked
    // originals remain enabled and briefly return to their real pose on saves.
    internal static class BakedObjects
    {
        private static readonly int multiplayer = "PUP_SAFE_Baked_multiplayer".GetStableHashCode();
        private static readonly int solo = "PUP_SAFE_Baked_solo".GetStableHashCode();
        internal static bool IsBaked(ZDO data) { return data.GetBool(multiplayer) || data.GetBool(solo); }
    }
}
