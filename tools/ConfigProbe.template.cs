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
    internal static class CameraHeading { internal static void Configure(float degrees, float seconds = 0f) { } }

    internal sealed class ConfigProbe
    {
        private static ConfigProbe instance;
        private static bool Diagnostics;
        private ConfigFile Config;
        private bool logs;
        private ConfigEntry<bool> configured;
        private ConfigEntry<bool> defaultMobs; private ConfigEntry<bool> bossAlwaysServerOwned; private ConfigEntry<float> bossPlayerRadius;
        private bool serverDefaultsConfigured;
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
            // Keep generated numeric option comments readable in English,
            // including the fractional 6.4-metre choice.
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
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
            ConfigLayout.Prepare(probe.Config, "@@PLUGIN_VERSION@@"); probe.Bind(); VerifyDefaults(probe.Config); ConfigLayout.Save(probe.Config);
            string generated = File.ReadAllText(path);
            string[] sections = {
                "00 - General", "01 - Simulation Settings",
                "02 - Default Ownership and Overrides", "03 - AI Mob Ownership",
                "04 - Grid Settings", "05 - Directional Grid Mode", "06 - Full Grid Mode",
                "07 - Camera Heading", "08 - PUP FPS Compatibility", "09 - Logs", "10 - Object Categories"
            };
            int found = 0;
            foreach (string line in File.ReadAllLines(path)) if (line.StartsWith("[", StringComparison.Ordinal)) {
                if (found >= sections.Length || line != "[" + sections[found] + "]") throw new Exception("Unexpected section or order: " + line);
                found++;
            }
            if (found != 11) throw new Exception("Expected eleven ordered English feature sections.");
            ConfigProbe reloaded = new ConfigProbe { Config = new ConfigFile(path, false, metadata) };
            reloaded.Config.SaveOnConfigSet = false;
            ConfigLayout.Prepare(reloaded.Config, "@@PLUGIN_VERSION@@"); reloaded.Bind(); VerifyDefaults(reloaded.Config); ConfigLayout.Save(reloaded.Config);
            if (File.ReadAllText(path) != generated) throw new Exception("Generated config changed during actual BepInEx reload/save.");
            string legacyPath = path + ".legacy.cfg";
            try
            {
                File.WriteAllText(legacyPath, generated + "\n[07 - Experimental Interaction Protection]\nEnableExperimentalInteractionProtection = false\n");
                ConfigProbe legacy = new ConfigProbe { Config = new ConfigFile(legacyPath, false, metadata) };
                legacy.Config.SaveOnConfigSet = false;
                ConfigLayout.Prepare(legacy.Config, "@@PLUGIN_VERSION@@"); legacy.Bind(); VerifyDefaults(legacy.Config);
                ConfigEntry<bool> removed;
                if (legacy.Config.TryGetEntry<bool>(new ConfigDefinition("07 - Experimental Interaction Protection", "EnableExperimentalInteractionProtection"), out removed))
                    throw new Exception("Removed protection switch must not become an active config entry.");
            }
            finally { if (File.Exists(legacyPath)) File.Delete(legacyPath); }
            Console.WriteLine("PASS: actual BepInEx generation and identical reload/save; 57 settings in eleven ordered English feature sections; automatic FGN setup enabled; object categories last; legacy protection=false is ignored.");
            Console.WriteLine("PASS: movement defaults to 0.1, camera to 0.05; angular overlap defaults to 30 degrees (0-45); object categories stay last.");
            Console.WriteLine("PASS: actual BepInEx camera and server movement ranges accept 0.05 seconds and clamp to 0.05-5.");
        }
        private static void VerifyDefaults(ConfigFile config)
        {
            ConfigEntry<float> camera = (ConfigEntry<float>)config[new ConfigDefinition("07 - Camera Heading", "CameraHeadingCheckIntervalSeconds")];
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
            ConfigEntry<float> angle = (ConfigEntry<float>)config[new ConfigDefinition("07 - Camera Heading", "CameraDirectionOverlapDegrees")];
            var angleRange = angle.Description.AcceptableValues as AcceptableValueRange<float>;
            if (angleRange == null || angleRange.MinValue != 0f || angleRange.MaxValue != 45f) throw new Exception("Unexpected angular overlap bounds.");
            angle.Value = -1f; if (angle.Value != 0f) throw new Exception("Angular overlap minimum did not clamp.");
            angle.Value = 90f; if (angle.Value != 45f) throw new Exception("Angular overlap maximum did not clamp.");
            angle.Value = (float)angle.DefaultValue;
            ConfigEntry<float> prediction = (ConfigEntry<float>)config[new ConfigDefinition("07 - Camera Heading", "CameraRotationPredictionSeconds")];
            var predictionRange = prediction.Description.AcceptableValues as AcceptableValueRange<float>;
            if (predictionRange == null || predictionRange.MinValue != 0f || predictionRange.MaxValue != .5f) throw new Exception("Unexpected prediction bounds.");
            prediction.Value = -1f; if (prediction.Value != 0f) throw new Exception("Prediction cannot be disabled.");
            prediction.Value = 1f; if (prediction.Value != .5f) throw new Exception("Prediction maximum did not clamp.");
            prediction.Value = (float)prediction.DefaultValue;
            AcceptableValueRange<float> movementRange = movement.Description.AcceptableValues as AcceptableValueRange<float>;
            if (movementRange == null || movementRange.MinValue != .05f) throw new Exception("Unexpected server movement interval minimum.");
            ConfigEntry<float> overlap = (ConfigEntry<float>)config[new ConfigDefinition("05 - Directional Grid Mode", "ServerOverlapCooldownSeconds")];
            AcceptableValueRange<float> overlapRange = overlap.Description.AcceptableValues as AcceptableValueRange<float>;
            if (overlapRange == null || overlapRange.MinValue != 0f || overlapRange.MaxValue != 30f) throw new Exception("Unexpected camera overlap buffer bounds.");
            overlap.Value = 0f;
            if (overlap.Value != 0f) throw new Exception("Actual config rejected disabled overlap buffer.");
            overlap.Value = (float)overlap.DefaultValue;
            Dictionary<string, string> expected = new Dictionary<string, string> {
                {"00 - General/AutoConfigureFiresGhettoNetworking", "true"},
                {"00 - General/Enabled", "true"}, {"01 - Simulation Settings/NoServerSideSimulation", "false"}, {"09 - Logs/EnableLogs", "false"},
                {"01 - Simulation Settings/ServerOwnedInteractivesByDefault", "false"},
                {"01 - Simulation Settings/ServerOwnedStaticBuildingsByDefault", "false"},
                {"01 - Simulation Settings/UseGridForStaticBuildings", "false"},
                {"01 - Simulation Settings/KeepOwnershipOnCellExit", "true"},
                {"08 - PUP FPS Compatibility/EnablePupCompatibility", "true"},
                {"02 - Default Ownership and Overrides/ServerOwnedMobsByDefault", "false"},
                {"02 - Default Ownership and Overrides/ServerOwnedRefillStations", "false"},
                {"04 - Grid Settings/GridCellSizeMeters", "4"}, {"04 - Grid Settings/GridCoveragePattern", "Rounded7x7"},
                {"04 - Grid Settings/MovementRotationCheckIntervalSeconds", "0.1"},
                {"05 - Directional Grid Mode/DirectionalGridMode", "true"}, {"05 - Directional Grid Mode/InnerTransitionDelaySeconds", "0"},
                {"05 - Directional Grid Mode/ForwardTransitionDelaySeconds", "0.2"}, {"05 - Directional Grid Mode/OuterTransitionDelaySeconds", "3"},
                {"05 - Directional Grid Mode/ServerOverlapCooldownSeconds", "1"},
                {"06 - Full Grid Mode/TransitionDelaySeconds", "2"},
                {"07 - Camera Heading/CameraHeadingCheckIntervalSeconds", "0.05"},
                {"07 - Camera Heading/CameraDirectionOverlapDegrees", "30"},
                {"07 - Camera Heading/CameraRotationPredictionSeconds", "0.1"},
                {"03 - AI Mob Ownership/EnableAIOwnership", "true"}, {"03 - AI Mob Ownership/BossAlwaysServerOwned", "true"},
                {"03 - AI Mob Ownership/EnableMobHandoffVisualBridge", "true"},
                {"03 - AI Mob Ownership/BossPlayerRadius", "30"}, {"03 - AI Mob Ownership/ServerFallbackPingMs", "200"},
                {"03 - AI Mob Ownership/ServerFallbackRadius", "6"}
            };
            foreach (string category in new[] {
                "Doors", "Seats", "Beds", "Workstations", "Smelters", "CookingStations", "Fireplaces", "Beehives",
                "SapCollectors", "Wards", "MapTables", "Wisplights", "TamedAnimals", "Containers", "ItemStands", "Signs",
                "Portals", "Fermenters", "Incinerators", "Defenses", "ShieldGenerators", "Pickables", "Offerings", "Vehicles", "OtherInteractables", "WorldMarkers", "LooseItems", "PhysicsObjects"
            }) expected.Add("10 - Object Categories/" + category, "true");
            if (config.Count != 57) throw new Exception("Expected 57 config entries; found " + config.Count + ".");
            int categoryNotes = 0;
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> entry in config) {
                string key = entry.Key.Section + "/" + entry.Key.Key, value = Convert.ToString(entry.Value.BoxedValue, CultureInfo.InvariantCulture), wanted;
                if (entry.Value.BoxedValue is bool) value = value.ToLowerInvariant();
                if (!expected.TryGetValue(key, out wanted) || value != wanted) throw new Exception("Unexpected setting/default: " + key + "=" + value);
                string defaultValue = Convert.ToString(entry.Value.DefaultValue, CultureInfo.InvariantCulture);
                if (entry.Value.DefaultValue is bool) defaultValue = defaultValue.ToLowerInvariant();
                if (defaultValue != wanted) throw new Exception("Binding default differs: " + key);
                if (String.IsNullOrEmpty(entry.Value.Description.Description)) throw new Exception("Missing setting description: " + key);
                string scope = entry.Key.Key == "EnableLogs" || entry.Key.Key == "EnablePupCompatibility" ? "Local" : "Server only";
                if (entry.Key.Section == "10 - Object Categories") {
                    if (entry.Value.Description.Description.StartsWith("All categories below:", StringComparison.Ordinal)) categoryNotes++;
                    else if (entry.Value.Description.Description.Contains("restart required") || entry.Value.Description.Description.Contains("true:")) throw new Exception("Repeated category instructions: " + key);
                } else if (!entry.Value.Description.Description.StartsWith(scope, StringComparison.Ordinal)) throw new Exception("Incorrect/missing setting scope: " + key);
                expected.Remove(key);
            }
            if (expected.Count != 0) throw new Exception("Missing expected config settings.");
            if (categoryNotes != 1) throw new Exception("Expected exactly one shared category explanation.");
            ConfigEntry<float> geometry;
            if (!config.TryGetEntry<float>(new ConfigDefinition("04 - Grid Settings", "GridCellSizeMeters"), out geometry) ||
                !geometry.Description.AcceptableValues.IsValid(6.4f) || geometry.Description.AcceptableValues.IsValid(6f))
                throw new Exception("Actual cell-size binding must accept 6.4 metres and reject unsupported sizes.");
        }
    }
}
