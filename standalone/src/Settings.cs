using System;
using BepInEx.Configuration;

namespace TooManyLagsStandalone
{
    internal sealed class Settings
    {
        internal bool Enabled, Logs, Mobs;
        internal int HighPing;
        internal float Interval, CameraInterval, CameraOverlap, CameraPrediction, Inner, Forward, Outer, Full, MobRadius, OverlapCooldown;
        internal readonly bool Directional, Keep;
        internal readonly float CellSize, PingInterval, Margin;
        internal readonly GridCoveragePattern Pattern;
        internal readonly Kinds Categories;
        private readonly ConfigFile config;
        private readonly bool configuredEnabled, configuredMobs;
        private readonly float configuredCameraInterval, configuredCameraOverlap, configuredCameraPrediction;
        internal Settings(ConfigFile config)
        {
            this.config = config;
            Enabled = config.Bind("00 - General", "Enabled", true, "Host/server setting. All gameplay settings, including camera and mobs, follow the host/server; only EnableLogs is local. Clients are optional with reduced features. Restart after changes unless marked live.").Value;
            Logs = config.Bind("09 - Logs", "EnableLogs", false, "Local only; live console supported. Extra diagnostics; startup errors are always reported.").Value;
            Keep = config.Bind("01 - Simulation Settings", "KeepOwnershipOnCellExit", true, "Host/server; restart. Retain empty fields. Only inner fields automatically update when one of several players leaves; outer fields wait for movement/rotation. false uses full directional coverage with a red 3x3 centre. Vanilla region ownership release remains active.").Value;
            CellSize = config.Bind("04 - Grid Settings", "GridCellSizeMeters", 4f, new ConfigDescription("Host/server; restart. Cell size in metres; 6.4 divides a vanilla zone into 10x10. 4 requires a rounded pattern.", new AcceptableValueList<float>(4f, 6.4f, 8f, 16f))).Value;
            string shape = config.Bind("04 - Grid Settings", "GridCoveragePattern", "Rounded7x7", new ConfigDescription("Host/server; restart. Larger grids prepare ownership earlier for poor connections. Overlaps select the best eligible player ping.", new AcceptableValueList<string>("Square3x3", "Rounded5x5", "Rounded7x7"))).Value;
            Pattern = shape == "Rounded7x7" ? GridCoveragePattern.Rounded7x7 : shape == "Rounded5x5" ? GridCoveragePattern.Rounded5x5 : GridCoveragePattern.Square3x3;
            if (CellSize == 4f && Pattern == GridCoveragePattern.Square3x3) throw new NotSupportedException("4-metre cells require Rounded5x5 or Rounded7x7.");
            Interval = Float(config, "04 - Grid Settings", "MovementRotationCheckIntervalSeconds", .1f, .05f, 5f, "Host/server; live. Check movement/character direction. Only changed coverage creates requests; camera changes wake the next pass directly.");
            CameraInterval = Float(config, "07 - Camera Heading", "CameraHeadingCheckIntervalSeconds", .05f, .05f, 5f, "Host/server; live, sent to clients. Camera sample interval; only changed coverage is published. No client mod: use character direction.");
            CameraOverlap = Float(config, "07 - Camera Heading", "CameraDirectionOverlapDegrees", 30f, 0f, 45f, "Host/server; live, sent to clients. Angular overlap between adjacent directions. 30 is recommended; 0 disables base overlap. Prediction can add one further direction.");
            CameraPrediction = Float(config, "07 - Camera Heading", "CameraRotationPredictionSeconds", .1f, 0f, .5f, "Host/server; live, sent to clients. Predict rotation this far ahead; at most one extra direction uses the inner/red delay. 0 disables. Requires a compatible client.");
            Directional = config.Bind("05 - Directional Grid Mode", "DirectionalGridMode", true, "Host/server; restart. Recommended: leave enabled. Uses camera direction, or character rotation without a client mod. false uses section 06's full grid timer.").Value;
            Inner = Float(config, "05 - Directional Grid Mode", "InnerTransitionDelaySeconds", 0, 0, 30, "Host/server; live. Red: six inner cells; full 3x3 with KeepOwnershipOnCellExit=false. Safe handoffs can still wait.");
            Forward = Float(config, "05 - Directional Grid Mode", "ForwardTransitionDelaySeconds", .2f, 0, 30, "Host/server; live. Orange: extra three-cell row ahead with 4-metre cells and rounded coverage.");
            Outer = Float(config, "05 - Directional Grid Mode", "OuterTransitionDelaySeconds", 3, 0, 30, "Host/server; live. Yellow: outer cells. Avoids unnecessary handoffs at the edge; lower delays can help fast movement on poor connections.");
            Full = Float(config, "06 - Full Grid Mode", "TransitionDelaySeconds", 2, 0, 30, "Host/server; live. Uniform delay when DirectionalGridMode=false.");
            PingInterval = Float(config, "02 - Latency Selection", "PingMeasurementIntervalSeconds", 2, .5f, 10, "Host/server; restart. Smoothed ping interval; unmodded clients use existing native telemetry without an extra RPC.");
            Margin = Float(config, "02 - Latency Selection", "OwnershipPingMarginMs", 30, 0, 500, "Host/server; restart. Required improvement before replacing a candidate with a lower-ping player; prevents jitter-driven switches.");
            OverlapCooldown = Float(config, "05 - Directional Grid Mode", "OverlapOwnershipCooldownSeconds", 1, 0, 30, "Host/server; live. Hold an overlap decision for this long. Exclusive cells have no hold; expiry alone makes no transfer. 0 disables.");
            Mobs = config.Bind("03 - AI Mob Ownership", "EnableAIOwnership", true, "Host/server; restart. Hostile Mob target-based ownership requires compatible source/target clients. Friendly player summons and bosses stay vanilla; idle tamed animals use the grid.").Value;
            HighPing = config.Bind("03 - AI Mob Ownership", "HighTargetPingMs", 200, new ConfigDescription("Host/server; live. Above this ping, prefer a better-connected eligible player very close to the Hostile Mob. Does not change its attack target.", new AcceptableValueRange<int>(50, 2000))).Value;
            MobRadius = Float(config, "03 - AI Mob Ownership", "NearbyAlternativeOwnerRadiusMeters", 6, 1, 20, "Host/server; live. Maximum distance from the mob for a better-ping alternative owner.");
            Kinds kinds = Kinds.None;
            foreach (Kinds kind in Enum.GetValues(typeof(Kinds)))
                if (kind != Kinds.None && config.Bind("10 - Object Categories", kind.ToString(), true,
                    (kind == Kinds.Doors ? "All categories: host/server, restart. Manage compatible objects through the grid; active use and safe handoffs stay protected. Without compatible clients, only supported native-safe categories transfer. " : "") + Description(kind)).Value) kinds |= kind;
            Categories = kinds;
            ConfigLayout.Finish(config);
            configuredEnabled = Enabled; configuredMobs = Mobs;
            configuredCameraInterval = CameraInterval; configuredCameraOverlap = CameraOverlap; configuredCameraPrediction = CameraPrediction;
        }
        internal void RestoreSession()
        {
            Enabled = configuredEnabled; Mobs = configuredMobs;
            CameraInterval = configuredCameraInterval; CameraOverlap = configuredCameraOverlap; CameraPrediction = configuredCameraPrediction;
            ApplyLive("CameraHeadingCheckIntervalSeconds"); ApplyLive("CameraDirectionOverlapDegrees"); ApplyLive("CameraRotationPredictionSeconds");
        }
        internal void Session(bool enabled, bool mobs, float interval, float overlap, float prediction)
        { Enabled = enabled; Mobs = enabled && mobs; CameraInterval = interval; CameraOverlap = overlap; CameraPrediction = prediction; }
        internal void ApplyLive(string key)
        {
            foreach (var pair in config)
            {
                if (pair.Key.Key != key) continue;
                object value = pair.Value.BoxedValue;
                switch (key)
                {
                    case "EnableLogs": Logs = (bool)value; break;
                    case "MovementRotationCheckIntervalSeconds": Interval = (float)value; break;
                    case "CameraHeadingCheckIntervalSeconds": CameraInterval = (float)value; break;
                    case "CameraDirectionOverlapDegrees": CameraOverlap = (float)value; break;
                    case "CameraRotationPredictionSeconds": CameraPrediction = (float)value; break;
                    case "InnerTransitionDelaySeconds": Inner = (float)value; break;
                    case "ForwardTransitionDelaySeconds": Forward = (float)value; break;
                    case "OuterTransitionDelaySeconds": Outer = (float)value; break;
                    case "TransitionDelaySeconds": Full = (float)value; break;
                    case "OverlapOwnershipCooldownSeconds": OverlapCooldown = (float)value; break;
                    case "HighTargetPingMs": HighPing = (int)value; break;
                    case "NearbyAlternativeOwnerRadiusMeters": MobRadius = (float)value; break;
                }
                return;
            }
        }
        private static float Float(ConfigFile config, string section, string name, float value, float min, float max, string description)
        {
            float result = config.Bind(section, name, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max))).Value;
            return Single.IsNaN(result) || Single.IsInfinity(result) ? value : Math.Max(min, Math.Min(max, result));
        }
        private static string Description(Kinds kind)
        {
            switch (kind)
            {
                case Kinds.TamedAnimals: return "Idle tamed animals; riding, following and friendly player summons retain native control.";
                case Kinds.Vehicles: return "Carts, boats and vehicle controls; active drivers retain control.";
                case Kinds.Containers: return "Chests and inventories; open inventories and pending open requests block transfers.";
                case Kinds.Smelters: return "Smelters, kilns, windmills and similar refill stations; each insert renews a one-second hold.";
                case Kinds.Incinerators: return "Incinerators; open inventories, pending requests and burning block transfers.";
                case Kinds.Signs: return "Signs; native editing retains ownership until player movement.";
                case Kinds.Portals: return "Portals and dungeon entrances; native portal-name editing retains ownership until movement.";
                case Kinds.OtherInteractables: return "Other compatible modded interactables; unreplicated custom actions cannot be inferred.";
                default: return kind.ToString() + ".";
            }
        }
    }
}
