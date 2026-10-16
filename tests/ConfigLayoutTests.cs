using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
#if STANDALONE
using TooManyLagsStandalone;
#else
using TooManyLags;
#endif

internal static class ConfigLayoutTests
{
    private static int assertions;
    private static void Check(bool value, string why) { assertions++; if (!value) throw new Exception(why); }
    private static string Read(ConfigFile config, string key)
    { foreach (var entry in config) if (entry.Key.Key == key) return entry.Value.GetSerializedValue(); throw new Exception("Missing " + key); }
    private static ConfigEntryBase Entry(ConfigFile config, string key)
    { foreach (var entry in config) if (entry.Key.Key == key) return entry.Value; throw new Exception("Missing " + key); }
    private static int Bind(ConfigFile config, bool deferCombat)
    {
        string section = ""; int count = 0;
        using (var reader = new StringReader(ConfigTemplate.Text))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.StartsWith("[")) { section = line.Substring(1, line.Length - 2); continue; }
                int split = line.IndexOf('=');
                if (split <= 0 || line.StartsWith("#")) continue;
                string key = line.Substring(0, split).Trim(), value = line.Substring(split + 1).Trim();
                if (deferCombat && (key == "ServerFallbackPingMs" || key == "ServerFallbackRadius" || key == "HighTargetPingMs" || key == "NearbyAlternativeOwnerRadiusMeters")) continue;
                bool boolean; float number;
                if (Boolean.TryParse(value, out boolean)) config.Bind(section, key, boolean);
                else if (Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) config.Bind(section, key, number);
                else config.Bind(section, key, value);
                count++;
            }
        }
        return count;
    }
    public static int Main(string[] args)
    {
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        // The game bootstraps BepInEx Paths before plugins. Reproduce that in
        // the isolated Mono host before ConfigFile's core-config initializer.
        typeof(BepInEx.Paths).GetMethod("SetExecutablePath", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { System.Reflection.Assembly.GetExecutingAssembly().Location,
                AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.BaseDirectory, new string[0] });
        var harmony = new Harmony("botanic.config.layout.tests");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ConfigFile), "Save"), prefix: new HarmonyMethod(typeof(ConfigLayout), "BeforeSave"));
            string path = Path.Combine(args[0], Guid.NewGuid().ToString("N") + ".cfg");
            string fresh = path + ".fresh", foreign = path + ".foreign";
            try
            {
                string original = "Enabled = false\n[01 - General]\nKeepOwnershipOnCellExit = false\nEnableLogs = true\nNoServerSideSimulation = true\n[00 - Simulation Settings - Static Objects]\nServerOwnedStaticBuildings = true\n[04 - Grid Settings]\nGridCellSizeMeters = 6.4\nMovementRotationCheckIntervalSeconds = 0.075\n[08 - Camera Heading]\nCameraHeadingCheckIntervalSeconds = 0.11\n[09 - AI Mob Ownership]\nServerFallbackPingMs = 777\nServerFallbackRadius = 12\n[99 - Object Categories]\nDoors = false\nContainers = false\n";
                File.WriteAllText(path, original);
                var migrated = new ConfigFile(path, false);
                ConfigLayout.Prepare(migrated, "test"); Check(ConfigLayout.Migrated, "Old layout was not recognized.");
                Check(File.ReadAllText(path + ".pre-layout.bak") == original, "Original config backup changed values.");
                Bind(migrated, true); ConfigLayout.Finish(migrated);
#if STANDALONE
                string ping = "HighTargetPingMs", radius = "NearbyAlternativeOwnerRadiusMeters";
#else
                string ping = "ServerFallbackPingMs", radius = "ServerFallbackRadius";
                Check(Read(migrated, "ServerOwnedStaticBuildingsByDefault") == "true", "Static ownership alias lost.");
                Check(Read(migrated, "NoServerSideSimulation") == "true", "Simulation mode lost.");
#endif
                Check(File.ReadAllText(path).Contains(ping + " = 777"), "Deferred combat setting overwritten by defaults.");
                Bind(migrated, false); ConfigLayout.Save(migrated);
                foreach (var item in new Dictionary<string, string> {
                    {"Enabled","false"},{"KeepOwnershipOnCellExit","false"},{"EnableLogs","true"},
                    {"GridCellSizeMeters","6.4"},{"MovementRotationCheckIntervalSeconds","0.075"},
                    {"CameraHeadingCheckIntervalSeconds","0.11"},{ping,"777"},{radius,"12"},{"Doors","false"},{"Containers","false"}
                }) Check(Read(migrated, item.Key) == item.Value, "Migration lost " + item.Key);
                Entry(migrated, "OuterTransitionDelaySeconds").BoxedValue = 7f;
                migrated.Save();
                string saved = File.ReadAllText(path);
                Check(saved.Contains("OuterTransitionDelaySeconds = 7"), "Save hook did not preserve live edit.");
                Check(saved.LastIndexOf("[", StringComparison.Ordinal) == saved.IndexOf("[10 - Object Categories]", StringComparison.Ordinal), "Object categories no longer last.");
                Check(!saved.Contains("[01 - General]"), "Obsolete section retained in active config.");
                var other = new ConfigFile(foreign, false); other.Bind("Unrelated", "ForeignValue", 123); other.Save();
                Check(!File.ReadAllText(foreign).Contains("Configuration layout"), "Another plugin config was rewritten.");
                var again = new ConfigFile(path, false); ConfigLayout.Prepare(again, "test"); Bind(again, false); ConfigLayout.Finish(again);
                Check(!ConfigLayout.Migrated, "New layout repeatedly migrated.");
                Check(File.ReadAllText(path) == saved, "Reload/save was not stable.");
                Check(File.ReadAllText(path + ".pre-layout.bak") == original, "Backup overwritten on restart.");
                ConfigLayout.Prepare(new ConfigFile(fresh, false), "test");
                var defaults = new ConfigFile(fresh, false); ConfigLayout.Prepare(defaults, "test");
                int count = Bind(defaults, false); ConfigLayout.Finish(defaults);
                Check(count ==
#if STANDALONE
                    45
#else
                    53
#endif
                    , "Template key count changed.");
                Check(Read(defaults, "MovementRotationCheckIntervalSeconds") == "0.1", "Fresh movement interval differs.");
                Check(Read(defaults, "CameraHeadingCheckIntervalSeconds") == "0.05", "Fresh camera interval differs.");
                string duplicate = File.ReadAllText(fresh) + "\n[legacy]\nOuterTransitionDelaySeconds = 21\nServerOwnedStaticBuildings = true\n";
                File.WriteAllText(fresh, duplicate);
                defaults = new ConfigFile(fresh, false); ConfigLayout.Prepare(defaults, "test"); Bind(defaults, false); ConfigLayout.Finish(defaults);
                Check(Read(defaults, "OuterTransitionDelaySeconds") == "3", "Stale orphan overrode canonical value.");
#if !STANDALONE
                Check(Read(defaults, "ServerOwnedStaticBuildingsByDefault") == "false", "Deprecated alias overrode canonical value.");
#endif
                Entry(defaults, "MovementRotationCheckIntervalSeconds").BoxedValue = .05f; defaults.Save();
                var retained = new ConfigFile(fresh, false); ConfigLayout.Prepare(retained, "test"); Bind(retained, false); ConfigLayout.Finish(retained);
                Check(Read(retained, "MovementRotationCheckIntervalSeconds") == "0.05", "Existing previous default overwritten.");
            }
            finally
            {
                ConfigLayout.Reset();
                foreach (string item in new[] {path, path + ".pre-layout.bak", fresh, fresh + ".pre-layout.bak", foreign}) if (File.Exists(item)) File.Delete(item);
            }
            Console.WriteLine("PASS: " + assertions + " real BepInEx/Harmony config migration checks: values, deferred bindings, backup, stable ordering, live saves and unrelated config isolation.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { ConfigLayout.Reset(); harmony.UnpatchSelf(); }
    }
}
