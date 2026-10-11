using System;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;

namespace TooManyLags
{
    // Runs only on a console request. Startup-only settings cannot tear down
    // guards or the grid while an inventory/owner handoff is in flight.
    internal sealed class ConfigCommands
    {
        private readonly ConfigFile config;
        private readonly Action<string> apply;
        internal ConfigCommands(ConfigFile config, Action<string> apply) { this.config = config; this.apply = apply; }
        internal static bool Local(string key)
        { return String.Equals(key, "EnableLogs", StringComparison.OrdinalIgnoreCase); }
        internal static bool Live(string key)
        {
            switch (key)
            {
                case "EnableLogs": case "CameraHeadingCheckIntervalSeconds": case "MovementRotationCheckIntervalSeconds":
                case "InnerTransitionDelaySeconds": case "ForwardTransitionDelaySeconds": case "OuterTransitionDelaySeconds":
                case "TransitionDelaySeconds": case "ServerFallbackPingMs": case "ServerFallbackRadius": return true;
                default: return false;
            }
        }
        internal string Execute(string command, bool server)
        {
            if (command == null || command.Length > 256) return "Invalid config command.";
            string[] args = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 1 && String.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
            {
                StringBuilder output = new StringBuilder(server ? "SERVER settings (live/restart):" : "LOCAL settings (live):");
                foreach (var pair in config)
                    if (server != Local(pair.Key.Key)) output.Append('\n').Append(pair.Key.Key).Append(" = ").Append(Value(pair.Value.BoxedValue)).Append(Live(pair.Key.Key) ? " [live]" : " [restart]");
                return output.ToString();
            }
            if (args.Length < 1 || args.Length > 2) return "Usage: tml_config list | <Key> [value].";
            ConfigEntryBase entry = null;
            foreach (var pair in config)
                if (String.Equals(pair.Key.Key, args[0], StringComparison.OrdinalIgnoreCase)) { entry = pair.Value; break; }
            if (entry == null) return "Unknown setting. Use tml_config list.";
            string key = entry.Definition.Key;
            if (server == Local(key)) return "Wrong setting scope. Logging is local; camera timing and ownership settings belong on the server.";
            if (args.Length == 1) return key + " = " + Value(entry.BoxedValue) + "\n" + entry.Description.Description;
            if (!Live(key)) return key + " requires a restart. Edit the config on the indicated installation; no live change was made.";
            object value;
            if (!Parse(entry.SettingType, args[1], out value) ||
                (entry.Description.AcceptableValues != null && !entry.Description.AcceptableValues.IsValid(value)))
                return "Invalid value for " + key + ".\n" + entry.Description.Description + "\n" + (entry.Description.AcceptableValues == null ? "" : entry.Description.AcceptableValues.ToDescriptionString());
            if (Equals(entry.BoxedValue, value)) return key + " is already " + Value(value) + ".";
            object before = entry.BoxedValue; bool autoSave = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                entry.BoxedValue = value;
                config.Save();
                apply(key);
                return (server ? "SERVER: " : "LOCAL: ") + key + " = " + Value(value) + " (saved and applied; existing handoffs remain protected).";
            }
            catch (Exception error)
            {
                entry.BoxedValue = before;
                try { apply(key); config.Save(); } catch (Exception) { }
                return "Change failed: " + error.GetBaseException().Message;
            }
            finally { config.SaveOnConfigSet = autoSave; }
        }
        private static string Value(object value) { return Convert.ToString(value, CultureInfo.InvariantCulture); }
        private static bool Parse(Type type, string text, out object value)
        {
            value = null;
            bool boolean; int integer; float number;
            if (type == typeof(bool) && Boolean.TryParse(text, out boolean)) value = boolean;
            else if (type == typeof(int) && Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) value = integer;
            else if (type == typeof(float) && Single.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !Single.IsNaN(number) && !Single.IsInfinity(number)) value = number;
            return value != null;
        }
    }
}
