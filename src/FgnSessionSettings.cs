using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace TooManyLags
{
    // Replace only FGN's runtime references. Its ConfigFile retains the user's
    // original entries, including if FGN or a config manager saves it later.
    // No getter patches, SettingChanged subscriptions or recurring checks.
    internal static class FgnSessionSettings
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool Apply(Assembly assembly, ConfigFile original, bool dedicated, bool enableAI, bool sceneConflict, bool noSimulation = false)
        {
            Type plugin = assembly.GetType("FiresGhettoNetworkMod.FiresGhettoNetworkMod", true);
            List<FieldInfo> fields = new List<FieldInfo>(5);
            List<bool> values = new List<bool>(5);
            Add(fields, values, plugin, "ConfigEnableServerOwnership", false);
            Add(fields, values, plugin, "ConfigEnableServerOwnershipSelective", false);
            Type creatures = assembly.GetType("FiresGhettoNetworkMod.CreatureOwnership", true);
            if (enableAI) Add(fields, values, creatures, "ConfigEnabled", true);

            FieldInfo simulation = null, creatureOwner = null, shipOwner = null;
            MethodInfo initialize = null;
            bool wasActive = false;
            if (dedicated)
            {
                Type detection = assembly.GetType("FiresGhettoNetworkMod.ServerClientUtils", true);
                if (!ReadBool(detection, "IsDedicatedServerDetected"))
                    throw new NotSupportedException("FGN did not detect a dedicated server; simulation settings were not overridden.");
                Type compatibility = assembly.GetType("FiresGhettoNetworkMod.ValheimCommunityPatchCompat", true);
                Method(compatibility, "Detect").Invoke(null, null);
                if (!noSimulation && (sceneConflict || ReadBool(compatibility, "SchedulesSceneObjects")))
                    throw new NotSupportedException("FGN Server-Side Simulation conflicts with the installed scene scheduler; its safety block remains active.");

                Add(fields, values, plugin, "ConfigEnableServerAuthority", !noSimulation);
                Add(fields, values, assembly.GetType("FiresGhettoNetworkMod.StationRouter", true), "ConfigEnabled", true);
                simulation = Field(assembly.GetType("FiresGhettoNetworkMod.ServerAuthorityPatches", true), "SimulationActive", typeof(bool));
                creatureOwner = Field(creatures, "ServerOwnsCreatures", typeof(bool));
                shipOwner = Field(assembly.GetType("FiresGhettoNetworkMod.HelmOwnership", true), "ServerOwnsShips", typeof(bool));
                initialize = Method(plugin, "ApplyServerSideSimulationPatches");
                wasActive = (bool)simulation.GetValue(null);
                if (noSimulation) FgnSimulationStartup.Validate(initialize);
            }

            // Binding against an isolated, non-saving ConfigFile preserves entry
            // metadata. No file is created or changed by these session values.
            ConfigFile session = new ConfigFile(original.ConfigFilePath, false);
            session.SaveOnConfigSet = false;
            ConfigEntry<bool>[] replacements = new ConfigEntry<bool>[fields.Count];
            for (int i = 0; i < fields.Count; i++)
            {
                ConfigEntry<bool> previous = (ConfigEntry<bool>)fields[i].GetValue(null);
                if (previous == null) throw new NotSupportedException("FGN configuration is not initialized: " + fields[i].Name);
                replacements[i] = session.Bind(previous.Definition, (bool)previous.DefaultValue, previous.Description);
                replacements[i].Value = values[i];
            }
            // Remove only simulation hooks, before a world exists. Changing the
            // flag alone would leave already-installed simulation patches active.
            if (dedicated && noSimulation) FgnSimulationStartup.Remove(assembly);
            for (int i = 0; i < fields.Count; i++) fields[i].SetValue(null, replacements[i]);
            if (!dedicated) return false;

            // Existing broad/selective prefixes read the now-disabled entries
            // and pass through. Clear their startup-only ownership mode flags.
            creatureOwner.SetValue(null, false);
            shipOwner.SetValue(null, false);
            if (noSimulation) { simulation.SetValue(null, false); return false; }
            if (!wasActive)
            {
                // Use the same initializer and completion flag as FGN's Awake.
                // Never announce simulation readiness by changing a flag alone.
                initialize.Invoke(null, null);
                simulation.SetValue(null, true);
            }
            return !wasActive;
        }

        internal static void CreatureBalancing(Assembly assembly, ConfigFile original, bool enabled)
        {
            FieldInfo field = Field(assembly.GetType("FiresGhettoNetworkMod.CreatureOwnership", true), "ConfigEnabled", typeof(ConfigEntry<bool>));
            ConfigEntry<bool> previous = (ConfigEntry<bool>)field.GetValue(null);
            ConfigFile session = new ConfigFile(original.ConfigFilePath, false) { SaveOnConfigSet = false };
            ConfigEntry<bool> replacement = session.Bind(previous.Definition, (bool)previous.DefaultValue, previous.Description);
            replacement.Value = enabled; field.SetValue(null, replacement);
        }

        private static void Add(List<FieldInfo> fields, List<bool> values, Type type, string name, bool value)
        { fields.Add(Field(type, name, typeof(ConfigEntry<bool>))); values.Add(value); }

        private static FieldInfo Field(Type type, string name, Type expected)
        {
            FieldInfo field = type.GetField(name, Static);
            if (field == null || field.FieldType != expected || field.IsInitOnly || field.IsLiteral)
                throw new NotSupportedException("FGN session configuration contract changed: " + type.Name + "." + name);
            return field;
        }

        private static MethodInfo Method(Type type, string name)
        {
            MethodInfo method = type.GetMethod(name, Static, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(void))
                throw new NotSupportedException("FGN startup contract changed: " + type.Name + "." + name);
            return method;
        }

        private static bool ReadBool(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name, Static);
            if (property == null || property.PropertyType != typeof(bool) || property.GetIndexParameters().Length != 0 || property.GetGetMethod(true) == null)
                throw new NotSupportedException("FGN startup guard changed: " + type.Name + "." + name);
            return (bool)property.GetValue(null, null);
        }
    }
}
