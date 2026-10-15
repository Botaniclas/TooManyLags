using System;
using System.Collections.Generic;
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
        private readonly Dictionary<string, object> active = new Dictionary<string, object>(StringComparer.Ordinal);
        internal ConfigCommands(ConfigFile config, Action<string> apply)
        {
            this.config = config; this.apply = apply;
            foreach (var pair in config)
                if (!Live(pair.Key.Key)) active.Add(pair.Key.Key, pair.Value.BoxedValue);
        }
        internal static bool Local(string key)
        { return String.Equals(key, "EnableLogs", StringComparison.OrdinalIgnoreCase); }
        internal static bool CanReturnOnExit(bool serverSimulation, bool defaultInteractives, bool defaultStatics)
        { return serverSimulation && (defaultInteractives || defaultStatics); }
        internal static bool Live(string key)
        {
            switch (key)
            {
                case "EnableLogs": case "CameraHeadingCheckIntervalSeconds": case "CameraDirectionOverlapDegrees": case "CameraRotationPredictionSeconds": case "MovementRotationCheckIntervalSeconds":
                case "InnerTransitionDelaySeconds": case "ForwardTransitionDelaySeconds": case "OuterTransitionDelaySeconds":
                case "ServerOverlapCooldownSeconds":
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
                    if (server != Local(pair.Key.Key)) output.Append('\n').Append(Display(pair.Value));
                return output.ToString();
            }
            if (args.Length < 1 || args.Length > 2) return "Usage: tml_config list | <Key> [value].";
            ConfigEntryBase entry = null;
            foreach (var pair in config)
                if (String.Equals(pair.Key.Key, args[0], StringComparison.OrdinalIgnoreCase)) { entry = pair.Value; break; }
            if (entry == null) return "Unknown setting. Use tml_config list.";
            string key = entry.Definition.Key;
            if (server == Local(key)) return "Wrong setting scope. Logging is local; camera timing and ownership settings belong on the server.";
            if (args.Length == 1) return Display(entry) + "\n" + entry.Description.Description;
            object value;
            if (!Parse(entry.SettingType, args[1], out value) ||
                (entry.Description.AcceptableValues != null && !entry.Description.AcceptableValues.IsValid(value)))
                return "Invalid value for " + key + ".\n" + entry.Description.Description + "\n" + (entry.Description.AcceptableValues == null ? "" : entry.Description.AcceptableValues.ToDescriptionString());
            if (InvalidGrid(key, value)) return "Invalid grid combination: 4-metre cells require Rounded5x5 or Rounded7x7. Change the other setting first.";
            if (InvalidReturn(key, value)) return "Invalid ownership combination: KeepOwnershipOnCellExit=false requires Server-Side Simulation and server-default ownership for interactive or static objects. Enable a server default or keep ownership on exit.";
            bool live = Live(key);
            if (Equals(entry.BoxedValue, value)) return key + " is already " + Value(value) + "." + (Pending(entry) ? RestartNotice(key) : "");
            object before = entry.BoxedValue; bool autoSave = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                entry.BoxedValue = value;
                config.Save();
                if (live) apply(key);
                string result = (server ? "SERVER: " : "LOCAL: ") + key + " = " + Value(value);
                if (live) return result + " (saved and applied; existing handoffs remain protected).";
                return result + (Pending(entry) ? " (saved)." + RestartNotice(key) : " (saved; matches the current session, no restart pending).");
            }
            catch (Exception error)
            {
                entry.BoxedValue = before;
                try { if (live) apply(key); config.Save(); } catch (Exception) { }
                return "Change failed: " + error.GetBaseException().Message;
            }
            finally { config.SaveOnConfigSet = autoSave; }
        }
        private bool Pending(ConfigEntryBase entry)
        {
            object current;
            return active.TryGetValue(entry.Definition.Key, out current) && !Equals(current, entry.BoxedValue);
        }
        private string Display(ConfigEntryBase entry)
        {
            string key = entry.Definition.Key, result = key + " = " + Value(entry.BoxedValue);
            if (Pending(entry)) return result + " [restart pending; active: " + Value(active[key]) + "]";
            return result + (Live(key) ? " [live]" : " [restart]");
        }
        private static string RestartNotice(string key)
        {
            return " Takes effect after server restart; the current session is unchanged.";
        }
        private bool InvalidGrid(string key, object value)
        {
            if (key != "GridCellSizeMeters" && key != "GridCoveragePattern") return false;
            float size = 0; string pattern = null;
            foreach (var pair in config)
                if (pair.Key.Key == "GridCellSizeMeters") size = Convert.ToSingle(key == pair.Key.Key ? value : pair.Value.BoxedValue, CultureInfo.InvariantCulture);
                else if (pair.Key.Key == "GridCoveragePattern") pattern = (string)(key == pair.Key.Key ? value : pair.Value.BoxedValue);
            return size == 4 && pattern == "Square3x3";
        }
        private static string Value(object value) { return Convert.ToString(value, CultureInfo.InvariantCulture); }
        private bool InvalidReturn(string key, object value)
        {
            if (key != "KeepOwnershipOnCellExit" && key != "NoServerSideSimulation" &&
                key != "ServerOwnedInteractivesByDefault" && key != "ServerOwnedStaticBuildingsByDefault") return false;
            bool keep = true, noSimulation = false, interactives = false, statics = false;
            foreach (var pair in config)
            {
                object selected = pair.Key.Key == key ? value : pair.Value.BoxedValue;
                switch (pair.Key.Key)
                {
                    case "KeepOwnershipOnCellExit": keep = (bool)selected; break;
                    case "NoServerSideSimulation": noSimulation = (bool)selected; break;
                    case "ServerOwnedInteractivesByDefault": interactives = (bool)selected; break;
                    case "ServerOwnedStaticBuildingsByDefault": statics = (bool)selected; break;
                }
            }
            return !keep && !CanReturnOnExit(!noSimulation, interactives, statics);
        }
        private static bool Parse(Type type, string text, out object value)
        {
            value = null;
            bool boolean; int integer; float number;
            if (type == typeof(bool) && Boolean.TryParse(text, out boolean)) value = boolean;
            else if (type == typeof(string)) value = text;
            else if (type == typeof(int) && Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) value = integer;
            else if (type == typeof(float) && Single.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !Single.IsNaN(number) && !Single.IsInfinity(number)) value = number;
            return value != null;
        }
    }
}
