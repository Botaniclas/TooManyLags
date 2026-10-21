using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
internal static class ConfigProbe
{
    private static int Main(string[] args)
    {
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            string core = args[2];
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
            { string path = Path.Combine(core, new AssemblyName(request.Name).Name + ".dll"); return File.Exists(path) ? Assembly.LoadFrom(path) : null; };
            var config = new ConfigFile(args[1], false, new BepInPlugin("botanic.toomanylags.dedicatedaddon", "TooManyLags - Server Side", "0.13.2"));
            config.SaveOnConfigSet = false;
            Type settings = Assembly.LoadFrom(args[0]).GetType("TooManyLagsDedicatedAddon.Settings", true);
            object values = Activator.CreateInstance(settings, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { config }, null);
            if (config.Count != 45) throw new Exception("Unexpected settings count.");
            Func<string, object> read = name => settings.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(values);
            if ((float)read("CellSize") != 4 || (float)read("CheckInterval") != .1f || (float)read("InnerDelay") != 0 || (float)read("ForwardDelay") != .2f || (float)read("OuterDelay") != 3 || (float)read("OverlapHold") != 1 || (bool)read("Keep") || (bool)read("IncludeOuter") || (float)read("FastMovementSpeed") != 4) throw new Exception("Defaults differ.");
            var interval = config[new ConfigDefinition("01 - Grid", "MovementRotationCheckIntervalSeconds")];
            interval.BoxedValue = .001f; if ((float)interval.BoxedValue != .05f) throw new Exception("Minimum interval broken.");
            interval.BoxedValue = 10f; if ((float)interval.BoxedValue != 5f) throw new Exception("Maximum interval broken.");
            interval.BoxedValue = interval.DefaultValue;
            config.Save();
            var reload = new ConfigFile(args[1], false);
            reload.SaveOnConfigSet = false;
            Activator.CreateInstance(settings, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { reload }, null);
            foreach (var pair in config) if (pair.Value.GetSerializedValue() != reload[pair.Key].GetSerializedValue()) throw new Exception("Config reload differs: " + pair.Key);
            string lastSection = null;
            foreach (string line in File.ReadAllLines(args[1])) if (line.StartsWith("[")) lastSection = line;
            if (lastSection != "[10 - Object Categories]") throw new Exception("Categories are not last.");
            if ((float)read("GraceMultiplier") != 2 || (float)read("MinimumGrace") != .1f) throw new Exception("Handoff defaults differ.");
            if (!(bool)read("RespectFireOwnership")) throw new Exception("Backend fire ownership must be respected by default.");
            if ((bool)read("StaticGrid")) throw new Exception("Static grid must default off.");
            if ((int)read("AdaptivePing") != 200 || (float)read("RefillHold") != 1f) throw new Exception("Adaptive/refill defaults differ.");
            if ((float)read("MobConeRange") != 8f) throw new Exception("Mob cone must default to eight metres.");
            if ((float)read("BossMobConeRange") != 10f) throw new Exception("Boss cone must default to ten metres.");
            if (!(bool)read("MobConeServerWhenMultiple")) throw new Exception("Multiple cone players must default to server ownership.");
            if (!(bool)read("MobAttackForecast")) throw new Exception("Additional native ability forecast must default on.");
            if ((bool)read("MobOwnershipTest") || (float)read("MobTestInterval") != 1f) throw new Exception("Solo diagnostic must default off with a one-second completed-transfer pause.");
            var testInterval = config[new ConfigDefinition("09 - Solo Mob Test", "MobOwnershipTestIntervalSeconds")];
            testInterval.BoxedValue = 0f; if ((float)testInterval.BoxedValue != .1f) throw new Exception("Test interval minimum differs.");
            testInterval.BoxedValue = 100f; if ((float)testInterval.BoxedValue != 10f) throw new Exception("Test interval maximum differs.");
            testInterval.BoxedValue = testInterval.DefaultValue;
            if (Convert.ToInt32(read("Groups")) >> 17 != 7) throw new Exception("Ship/cart categories must default on.");
            Console.WriteLine("PASS: 45 actual production BepInEx settings, solo diagnostic disabled, native ability forecast enabled, default 8m/10m mob/boss cones, enabled multiple-cone-player server ownership, existing defaults, interval bounds and save/reload; categories last."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

