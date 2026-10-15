using System;
using BepInEx.Configuration;

namespace TooManyLags.PlayerSimulation
{
    // Read the addon's existing settings, not a second standalone config.
    internal sealed class Settings
    {
        internal bool Logs;
        internal readonly bool Directional, Keep;
        internal bool Mobs;
        internal readonly float CellSize;
        internal int HighPing;
        internal float Interval, Inner, Forward, Outer, Full, MobRadius;
        internal readonly float PingInterval, Margin;
        internal readonly GridCoveragePattern Pattern;
        internal readonly Kinds Categories;
        internal Settings(ConfigFile config, bool mobs)
        {
            Logs = Read(config, "01 - General", "EnableLogs", false);
            CellSize = Read(config, "04 - Grid Settings", "GridCellSizeMeters", 4f);
            string shape = Read(config, "04 - Grid Settings", "GridCoveragePattern", "Rounded7x7");
            Pattern = shape == "Rounded7x7" ? GridCoveragePattern.Rounded7x7 : shape == "Rounded5x5" ? GridCoveragePattern.Rounded5x5 : GridCoveragePattern.Square3x3;
            if (CellSize == 4 && Pattern == GridCoveragePattern.Square3x3) throw new ArgumentException("4-metre cells require a rounded grid.");
            Interval = Number(config, "04 - Grid Settings", "MovementRotationCheckIntervalSeconds", .05f);
            Keep = Read(config, "01 - General", "KeepOwnershipOnCellExit", true);
            Directional = Read(config, "05 - Directional Grid Mode", "DirectionalGridMode", true);
            Inner = Number(config, "05 - Directional Grid Mode", "InnerTransitionDelaySeconds", 0);
            Forward = Number(config, "05 - Directional Grid Mode", "ForwardTransitionDelaySeconds", .2f);
            Outer = Number(config, "05 - Directional Grid Mode", "OuterTransitionDelaySeconds", 3);
            Full = Number(config, "06 - Full Grid Mode", "TransitionDelaySeconds", 2);
            PingInterval = 2f; Margin = 30f; Mobs = mobs;
            HighPing = Read(config, "09 - AI Mob Ownership", "ServerFallbackPingMs", 200);
            MobRadius = Number(config, "09 - AI Mob Ownership", "ServerFallbackRadius", 6);
            Kinds kinds = Kinds.None;
            foreach (Kinds kind in Enum.GetValues(typeof(Kinds)))
                if (kind != Kinds.None && kind != Kinds.Incinerators && Read(config, "10 - Object Categories", kind.ToString(), true)) kinds |= kind;
            Categories = kinds;
        }
        private static T Read<T>(ConfigFile config, string section, string key, T fallback)
        {
            ConfigEntry<T> entry;
            return config.TryGetEntry<T>(new ConfigDefinition(section, key), out entry) ? entry.Value : fallback;
        }
        private static float Number(ConfigFile config, string section, string key, float fallback)
        {
            float result = Read(config, section, key, fallback);
            return Single.IsNaN(result) || Single.IsInfinity(result) || result < 0 ? fallback : result;
        }
    }
}
