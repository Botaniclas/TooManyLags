using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
namespace TooManyLagsStandalone
{
    @@KINDS@@
    @@PATTERN@@
    internal static class ConfigProbe
    {
        private static string core;
        public static void Main(string[] args)
        {
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                core = args[1]; AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request) { string path = Path.Combine(core, new AssemblyName(request.Name).Name + ".dll"); return File.Exists(path) ? Assembly.LoadFrom(path) : null; };
                ConfigFile config = new ConfigFile(args[0], false, new BepInPlugin("botanic.toomanylags.standalone", "TooManyLags - Standalone", "1.1.0"));
                config.SaveOnConfigSet = false;
                ConfigLayout.Prepare(config, "1.1.0");
                Settings defaults = new Settings(config);
                if (defaults.CellSize != 4 || defaults.Pattern != GridCoveragePattern.Rounded7x7 || defaults.Interval != .1f || defaults.CameraInterval != .05f || defaults.CameraOverlap != 30f || !defaults.Keep || !defaults.Mobs || defaults.Inner != 0 || defaults.Forward != .2f || defaults.Outer != 3f || defaults.Full != 2f || defaults.PingInterval != 2f || defaults.Margin != 30f || defaults.OverlapCooldown != 1f || defaults.HighPing != 200 || defaults.MobRadius != 6f) throw new Exception("Production defaults changed unexpectedly.");
                if (config.Count != 45) throw new Exception("Config entry count changed unexpectedly.");
                foreach (var pair in config) if (pair.Value.GetSerializedValue() != Convert.ToString(pair.Value.DefaultValue, System.Globalization.CultureInfo.InvariantCulture) && !(pair.Value.BoxedValue is bool)) throw new Exception("Fresh config/template differs from actual binding default: " + pair.Key);
                foreach (string key in new[] { "MovementRotationCheckIntervalSeconds", "CameraHeadingCheckIntervalSeconds" })
                {
                    var entry = config[new ConfigDefinition(key.StartsWith("Camera") ? "07 - Camera Heading" : "04 - Grid Settings", key)];
                    entry.BoxedValue = .01f; if ((float)entry.BoxedValue != .05f) throw new Exception("Minimum interval did not clamp: " + key);
                    entry.BoxedValue = 6f; if ((float)entry.BoxedValue != 5f) throw new Exception("Maximum interval did not clamp: " + key);
                    entry.BoxedValue = entry.DefaultValue;
                }
                var cooldown = config[new ConfigDefinition("05 - Directional Grid Mode", "OverlapOwnershipCooldownSeconds")];
                cooldown.BoxedValue = 0f; if ((float)cooldown.BoxedValue != 0f) throw new Exception("Overlap cooldown cannot be disabled.");
                cooldown.BoxedValue = cooldown.DefaultValue;
                var angle = config[new ConfigDefinition("07 - Camera Heading", "CameraDirectionOverlapDegrees")];
                angle.BoxedValue = -1f; if ((float)angle.BoxedValue != 0f) throw new Exception("Angular overlap minimum did not clamp.");
                angle.BoxedValue = 90f; if ((float)angle.BoxedValue != 45f) throw new Exception("Angular overlap maximum did not clamp.");
                angle.BoxedValue = angle.DefaultValue;
                var prediction = config[new ConfigDefinition("07 - Camera Heading", "CameraRotationPredictionSeconds")];
                prediction.BoxedValue = -1f; if ((float)prediction.BoxedValue != 0f) throw new Exception("Prediction cannot be disabled.");
                prediction.BoxedValue = 1f; if ((float)prediction.BoxedValue != .5f) throw new Exception("Prediction maximum did not clamp.");
                prediction.BoxedValue = prediction.DefaultValue;
                ConfigLayout.Save(config);
                string generated = File.ReadAllText(args[0]);
                string[] sections = { "00 - General", "01 - Simulation Settings", "02 - Latency Selection", "03 - AI Mob Ownership", "04 - Grid Settings", "05 - Directional Grid Mode", "06 - Full Grid Mode", "07 - Camera Heading", "09 - Logs", "10 - Object Categories" };
                int index = 0;
                foreach (string line in File.ReadAllLines(args[0])) if (line.StartsWith("[")) {
                    if (index >= sections.Length || line != "[" + sections[index++] + "]") throw new Exception("Unexpected section/order: " + line);
                }
                if (index != sections.Length) throw new Exception("A feature section is missing.");
                var reloaded = new ConfigFile(args[0], false); reloaded.SaveOnConfigSet = false;
                ConfigLayout.Prepare(reloaded, "1.1.0"); new Settings(reloaded); ConfigLayout.Save(reloaded);
                if (File.ReadAllText(args[0]) != generated) throw new Exception("Reload/save changed the config.");
                Console.WriteLine("PASS: 45 production settings/defaults; ten ordered feature sections, categories last; movement 0.1, camera 0.05; identical real BepInEx reload/save.");
                Console.WriteLine("PASS: camera/movement bounds 0.05-5; overlap 0-45 and prediction 0-0.5 enforced; overlap hold can be disabled.");
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { ConfigLayout.Reset(); }
        }
    }
}