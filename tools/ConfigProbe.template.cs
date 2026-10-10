// Filled from the current Plugin/FgnCombatOwnership binding prefixes by
// generate-config.ps1. Uses real installed BepInEx.ConfigFile without Unity
// startup, plugin hooks, game instances or a dependency on another project.
#pragma warning disable 0219, 0414
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;

namespace TooManyLags
{
    @@KINDS@@
    @@GRID_PATTERN@@

    internal sealed class ConfigProbe
    {
        private static ConfigProbe instance;
        private static bool Diagnostics;
        private ConfigFile Config;
        private bool logs;
        private ConfigEntry<bool> configured;
        private ConfigEntry<int> maximumPing;
        private ConfigEntry<float> nearbyRadius;

        private void BindPlugin()
        {
@@PLUGIN_BINDINGS@@
        }
        private void BindCombat()
        {
            ConfigFile config = Config;
@@COMBAT_BINDINGS@@
        }
        private void Bind() { BindPlugin(); BindCombat(); }
@@OBJECT_DESCRIPTION@@

        public static int Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request) {
                string name = new AssemblyName(request.Name).Name + ".dll";
                for (int i = 1; i < args.Length; i++) {
                    string candidate = Path.Combine(args[i], name);
                    if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
                }
                return null;
            };
            try { Run(args[0]); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        private static void Run(string path)
        {
            if (File.Exists(path)) throw new Exception("Use a fresh config generation path.");
            BepInPlugin metadata = new BepInPlugin("@@PLUGIN_ID@@", "@@PLUGIN_NAME@@", "@@PLUGIN_VERSION@@");
            ConfigProbe probe = new ConfigProbe { Config = new ConfigFile(path, false, metadata) };
            probe.Config.SaveOnConfigSet = false;
            probe.Bind(); VerifyDefaults(probe.Config); probe.Config.Save();
            string generated = File.ReadAllText(path);
            string[] sections = {
                "01 - General", "02 - Static Buildings", "03 - Default Ownership and Overrides",
                "04 - Grid Settings", "05 - Directional Grid Mode", "06 - Full Grid Mode",
                "07 - Object Categories", "08 - Camera Heading", "09 - AI Mob Ownership"
            };
            int found = 0;
            foreach (string line in File.ReadAllLines(path)) if (line.StartsWith("[", StringComparison.Ordinal)) {
                if (found >= sections.Length || line != "[" + sections[found] + "]") throw new Exception("Unexpected section or order: " + line);
                found++;
            }
            if (found != 9) throw new Exception("Expected nine ordered English feature sections.");
            ConfigProbe reloaded = new ConfigProbe { Config = new ConfigFile(path, false, metadata) };
            reloaded.Config.SaveOnConfigSet = false;
            reloaded.Bind(); VerifyDefaults(reloaded.Config); reloaded.Config.Save();
            if (File.ReadAllText(path) != generated) throw new Exception("Generated config changed during actual BepInEx reload/save.");
            Console.WriteLine("PASS: actual BepInEx generation and identical reload/save; 42 settings in nine ordered English feature sections.");
            Console.WriteLine("PASS: original 38 defaults unchanged; camera interval=0.1, EnableAIOwnership=true, ServerFallbackPingMs=200, ServerFallbackRadius=6.");
            Console.WriteLine("PASS: actual BepInEx camera range accepts 0.05 seconds and clamps to 0.05-5; server movement minimum remains 0.1.");
        }
        private static void VerifyDefaults(ConfigFile config)
        {
            ConfigEntry<float> camera = (ConfigEntry<float>)config[new ConfigDefinition("08 - Camera Heading", "CameraHeadingCheckIntervalSeconds")];
            AcceptableValueRange<float> cameraRange = camera.Description.AcceptableValues as AcceptableValueRange<float>;
            if (cameraRange == null || cameraRange.MinValue != .05f || cameraRange.MaxValue != 5f) throw new Exception("Unexpected camera interval bounds.");
            camera.Value = .05f;
            if (camera.Value != .05f) throw new Exception("Actual BepInEx config rejected the fast camera interval.");
            camera.Value = .01f;
            if (camera.Value != .05f) throw new Exception("Camera interval lower bound was not enforced.");
            camera.Value = 6f;
            if (camera.Value != 5f) throw new Exception("Camera interval upper bound was not enforced.");
            camera.Value = (float)camera.DefaultValue;
            ConfigEntry<float> movement = (ConfigEntry<float>)config[new ConfigDefinition("04 - Grid Settings", "MovementRotationCheckIntervalSeconds")];
            AcceptableValueRange<float> movementRange = movement.Description.AcceptableValues as AcceptableValueRange<float>;
            if (movementRange == null || movementRange.MinValue != .1f) throw new Exception("Server movement interval minimum changed.");
            Dictionary<string, string> expected = new Dictionary<string, string> {
                {"01 - General/Enabled", "true"}, {"01 - General/EnableLogs", "false"},
                {"02 - Static Buildings/ServerOwnedStaticBuildings", "false"},
                {"03 - Default Ownership and Overrides/ServerOwnedInteractivesByDefault", "false"},
                {"03 - Default Ownership and Overrides/ServerOwnedRefillStations", "false"},
                {"04 - Grid Settings/GridCellSizeMeters", "4"}, {"04 - Grid Settings/GridCoveragePattern", "Rounded7x7"},
                {"04 - Grid Settings/MovementRotationCheckIntervalSeconds", "0.1"}, {"04 - Grid Settings/KeepOwnershipOnCellExit", "true"},
                {"05 - Directional Grid Mode/DirectionalGridMode", "true"}, {"05 - Directional Grid Mode/InnerTransitionDelaySeconds", "0"},
                {"05 - Directional Grid Mode/ForwardTransitionDelaySeconds", "0.2"}, {"05 - Directional Grid Mode/OuterTransitionDelaySeconds", "3"},
                {"06 - Full Grid Mode/TransitionDelaySeconds", "2"},
                {"08 - Camera Heading/CameraHeadingCheckIntervalSeconds", "0.1"},
                {"09 - AI Mob Ownership/EnableAIOwnership", "true"}, {"09 - AI Mob Ownership/ServerFallbackPingMs", "200"},
                {"09 - AI Mob Ownership/ServerFallbackRadius", "6"}
            };
            foreach (string category in new[] {
                "Doors", "Seats", "Beds", "Workstations", "Smelters", "CookingStations", "Fireplaces", "Beehives",
                "SapCollectors", "Wards", "MapTables", "Wisplights", "TamedAnimals", "Containers", "ItemStands", "Signs",
                "Portals", "Fermenters", "Defenses", "ShieldGenerators", "Pickables", "Offerings", "Vehicles", "OtherInteractables"
            }) expected.Add("07 - Object Categories/" + category, "true");
            if (config.Count != 42) throw new Exception("Expected 42 config entries; found " + config.Count + ".");
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> entry in config) {
                string key = entry.Key.Section + "/" + entry.Key.Key, value = Convert.ToString(entry.Value.BoxedValue, CultureInfo.InvariantCulture), wanted;
                if (entry.Value.BoxedValue is bool) value = value.ToLowerInvariant();
                if (!expected.TryGetValue(key, out wanted) || value != wanted) throw new Exception("Unexpected setting/default: " + key + "=" + value);
                string defaultValue = Convert.ToString(entry.Value.DefaultValue, CultureInfo.InvariantCulture);
                if (entry.Value.DefaultValue is bool) defaultValue = defaultValue.ToLowerInvariant();
                if (defaultValue != wanted) throw new Exception("Binding default differs: " + key);
                if (String.IsNullOrEmpty(entry.Value.Description.Description)) throw new Exception("Missing setting description: " + key);
                expected.Remove(key);
            }
            if (expected.Count != 0) throw new Exception("Missing expected config settings.");
        }
    }
}
