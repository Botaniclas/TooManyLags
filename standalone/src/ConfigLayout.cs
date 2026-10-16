using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;

namespace TooManyLagsStandalone
{
    // Startup/explicit config saves only. No gameplay timer, networking or
    // changes to other plugins' ConfigFile instances.
    internal static class ConfigLayout
    {
        private const string Marker = "## Configuration layout: 2";
        private sealed class Option
        { internal string Section, Key, Value; }
        private static ConfigFile managed;
        private static string version;
        private static bool autoSave;
        internal static bool Migrated { get; private set; }

        internal static void Prepare(ConfigFile config, string pluginVersion)
        {
            managed = config; version = pluginVersion; autoSave = config.SaveOnConfigSet;
            string original = File.Exists(config.ConfigFilePath) ? File.ReadAllText(config.ConfigFilePath) : "";
            Dictionary<string, Option> schema = Schema();
            Dictionary<string, string> values = ReadValues(original, schema);
            Migrated = original.Length != 0 && !original.Contains(Marker);
            if (Migrated)
            {
                string backup = config.ConfigFilePath + ".pre-layout.bak";
                if (!File.Exists(backup)) File.WriteAllText(backup, original, new UTF8Encoding(false));
            }
            config.SaveOnConfigSet = false;
            Write(config.ConfigFilePath, Render(values, null));
            config.Reload();
        }

        internal static void Finish(ConfigFile config)
        {
            if (!ReferenceEquals(managed, config)) return;
            Save(config);
            config.SaveOnConfigSet = autoSave;
        }

        // Installed explicitly by the entry plugin on ConfigFile.Save. A
        // reference check leaves every other plugin's normal save untouched.
        internal static bool BeforeSave(ConfigFile __instance)
        {
            if (!ReferenceEquals(managed, __instance)) return true;
            Save(__instance); return false;
        }

        internal static void Save(ConfigFile config)
        {
            List<Option> extra = new List<Option>();
            Dictionary<string, Option> schema = Schema();
            Dictionary<string, string> values = ReadValues(File.Exists(config.ConfigFilePath) ? File.ReadAllText(config.ConfigFilePath) : "", schema);
            foreach (var pair in config)
            {
                string serialized = pair.Value.GetSerializedValue();
                if (schema.ContainsKey(pair.Key.Key)) values[pair.Key.Key] = serialized;
                else extra.Add(new Option { Section = pair.Key.Section, Key = pair.Key.Key, Value = serialized });
            }
            Write(config.ConfigFilePath, Render(values, extra));
        }

        internal static void Reset()
        {
            if (managed != null) managed.SaveOnConfigSet = autoSave;
            managed = null; version = null; Migrated = false;
        }

        private static Dictionary<string, Option> Schema()
        {
            Dictionary<string, Option> result = new Dictionary<string, Option>(StringComparer.Ordinal);
            string section = "";
            foreach (string line in Lines(ConfigTemplate.Text))
            {
                string value = line.Trim();
                if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
                { section = value.Substring(1, value.Length - 2); continue; }
                string key, content;
                if (Setting(value, out key, out content)) result.Add(key, new Option { Section = section, Key = key, Value = content });
            }
            return result;
        }

        private static Dictionary<string, string> ReadValues(string text, Dictionary<string, Option> schema)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, int> priority = new Dictionary<string, int>(StringComparer.Ordinal);
            string section = "";
            foreach (string raw in Lines(text))
            {
                string line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                { section = line.Substring(1, line.Length - 2); continue; }
                string key, content;
                if (!Setting(line, out key, out content)) continue;
                string originalKey = key;
                if (key == "ServerOwnedStaticBuildings") key = "ServerOwnedStaticBuildingsByDefault";
                if (!schema.ContainsKey(key))
                {
                    if (key == "ServerFallbackPingMs") key = "HighTargetPingMs";
                    else if (key == "ServerFallbackRadius") key = "NearbyAlternativeOwnerRadiusMeters";
                    else if (key == "ServerOverlapCooldownSeconds") key = "OverlapOwnershipCooldownSeconds";
                }
                Option option;
                if (!schema.TryGetValue(key, out option)) continue;
                int rank = (section == option.Section ? 20 : section.Length == 0 && key == "Enabled" ? 30 : 10)
                    + (key == originalKey ? 1 : 0);
                int before;
                if (priority.TryGetValue(key, out before) && before > rank) continue;
                priority[key] = rank; result[key] = content;
            }
            return result;
        }

        private static string Render(Dictionary<string, string> values, List<Option> extra)
        {
            StringBuilder output = new StringBuilder();
            output.AppendLine(Marker);
            bool extrasWritten = false;
            foreach (string raw in Lines(ConfigTemplate.Text))
            {
                string line = raw.Replace("@VERSION@", version ?? "");
                if (!extrasWritten && line.Trim() == "[10 - Object Categories]")
                {
                    if (extra != null && extra.Count != 0)
                    {
                        output.AppendLine("[09 - Additional Settings]");
                        foreach (Option option in extra) output.AppendLine(option.Key + " = " + option.Value);
                        output.AppendLine();
                    }
                    extrasWritten = true;
                }
                string key, content, saved;
                if (Setting(line.Trim(), out key, out content) && values.TryGetValue(key, out saved)) line = key + " = " + saved;
                output.AppendLine(line);
            }
            return output.ToString();
        }

        private static bool Setting(string line, out string key, out string value)
        {
            key = value = null;
            if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') return false;
            int equal = line.IndexOf('=');
            if (equal <= 0) return false;
            key = line.Substring(0, equal).Trim(); value = line.Substring(equal + 1).Trim();
            return key.Length != 0;
        }
        private static IEnumerable<string> Lines(string text)
        {
            using (StringReader reader = new StringReader(text))
            { string line; while ((line = reader.ReadLine()) != null) yield return line; }
        }
        private static void Write(string path, string text)
        {
            if (File.Exists(path) && File.ReadAllText(path) == text) return;
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + ".layout-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
