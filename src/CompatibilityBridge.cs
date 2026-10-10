using System;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;

namespace TooManyLags
{
    // FGN only. PUP collision protection remains the responsibility of PUP and
    // its local patch; Authority neither binds nor certifies their readiness.
    internal sealed class CompatibilityBridge
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private readonly BaseUnityPlugin fgn;
        private readonly ConfigEntry<bool> sss, broad, selective, inserts;
        private readonly Func<bool> simulation, serverShips;
        private readonly Func<ConfigEntry<bool>> helm;
        internal readonly Assembly Assembly;
        internal bool ShipsOwnedExternally { get; private set; }
        internal CompatibilityBridge(BaseUnityPlugin fgn)
        {
            this.fgn = fgn; Assembly = fgn.GetType().Assembly;
            BepInPlugin identity = (BepInPlugin)Attribute.GetCustomAttribute(fgn.GetType(), typeof(BepInPlugin));
            if (identity == null || identity.GUID != Plugin.FgnId) throw new NotSupportedException("FGN plugin identity changed.");
            sss = Config(fgn.GetType(), "ConfigEnableServerAuthority");
            broad = Config(fgn.GetType(), "ConfigEnableServerOwnership");
            selective = Config(fgn.GetType(), "ConfigEnableServerOwnershipSelective");
            simulation = Read<bool>(Assembly.GetType("FiresGhettoNetworkMod.ServerAuthorityPatches", true), "SimulationActive");
            inserts = Config(Assembly.GetType("FiresGhettoNetworkMod.StationRouter", true), "ConfigEnabled");
            Type ships = Assembly.GetType("FiresGhettoNetworkMod.HelmOwnership", false);
            if (ships != null) { serverShips = Read<bool>(ships, "ServerOwnsShips"); helm = Read<ConfigEntry<bool>>(ships, "ConfigEnabled"); }
        }
        internal bool Enabled()
        {
            ConfigEntry<bool> control = helm == null ? null : helm();
            ShipsOwnedExternally = serverShips != null && serverShips() && control != null && control.Value;
            return fgn && fgn.isActiveAndEnabled && sss.Value && simulation() && !broad.Value && !selective.Value && inserts.Value;
        }
        internal bool Configured() { return sss.Value && !broad.Value && !selective.Value && inserts.Value; }
        internal string State()
        { return "SSS=" + sss.Value + "; simulation=" + simulation() + "; broad=" + broad.Value + "; selective=" + selective.Value + "; stationRouter=" + inserts.Value; }
        private static ConfigEntry<bool> Config(Type type, string name)
        {
            FieldInfo field = type.GetField(name, Flags);
            if (field == null || field.FieldType != typeof(ConfigEntry<bool>)) throw new NotSupportedException("FGN configuration contract changed: " + name);
            ConfigEntry<bool> result = (ConfigEntry<bool>)field.GetValue(null);
            if (result == null) throw new NotSupportedException("FGN configuration is not initialized: " + name);
            return result;
        }
        private static Func<T> Read<T>(Type type, string name)
        {
            FieldInfo field = type.GetField(name, Flags);
            if (field == null || field.FieldType != typeof(T)) throw new NotSupportedException("FGN state contract changed: " + name);
            DynamicMethod method = new DynamicMethod("BotanicFgn_" + name, typeof(T), Type.EmptyTypes, typeof(CompatibilityBridge).Module, true);
            ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Ret);
            return (Func<T>)method.CreateDelegate(typeof(Func<T>));
        }
    }
}
