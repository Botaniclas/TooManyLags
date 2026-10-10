using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;

namespace TooManyLags
{
    // Private FGN contracts are bound once. No reflection/boxing on a balance pass.
    internal sealed class FgnCombatBridge
    {
        internal readonly MethodInfo Balance, TryHandOff, Veto, OnResult;
        internal readonly Func<ZDO, double, bool> TryTransfer;
        internal readonly Func<bool> Active, Simulation;
        internal readonly Func<double> NextPass;
        internal readonly Func<ZNetPeer, bool> Matched;
        internal readonly Func<ZNetPeer, float> Rtt;
        internal delegate bool EchoRtt(ZNetPeer peer, out float milliseconds);
        internal readonly EchoRtt MeasuredEcho;
        internal readonly Func<ZNetPeer, int> SocketPing;
        internal readonly Func<ZDO, bool> Held, Possessed;
        internal readonly Func<ZDOID, double, bool> Pending;
        internal readonly Action<ZDOID, long, double> MarkPending;
        internal readonly Action<ZDOID> ClearStreak, ClearOwned;
        internal readonly Dictionary<long, int> PerOwner;
        internal readonly Dictionary<ZDOID, double> Cooldowns;
        internal readonly List<ZNetPeer> Candidates;
        internal readonly MethodInfo TargetReader;
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal FgnCombatBridge(Assembly assembly)
        {
            Type type = assembly.GetType("FiresGhettoNetworkMod.CreatureOwnership", true);
            // These constants are shared with our pending/lease and retry rules.
            // Check once at startup, independently of the plugin version/hash.
            RequireConstant(type, "PassSeconds", 2.0);
            RequireConstant(type, "PendingSeconds", 5.0);
            RequireConstant(type, "DeclinedCooldownSeconds", 6.0);
            RequireConstant(type, "ForwardHitsSeconds", 5.0);
            RequireConstant(type, "MaxHandOffsPerPass", 16);
            RequireConstant(type, "MaxHandOffsPerOwner", 6);
            Balance = Method(type, "Balance", typeof(void), typeof(ZDOMan));
            TryHandOff = Method(type, "TryHandOffCreature", typeof(bool), typeof(ZDO), typeof(double));
            TryTransfer = Bind<Func<ZDO, double, bool>>(TryHandOff);
            Veto = Method(type, "Veto", typeof(string), typeof(ZDO), typeof(long));
            OnResult = Method(type, "OnResult", typeof(void), typeof(ZRpc), typeof(ZDOID), typeof(bool), typeof(string));
            Active = Bind<Func<bool>>(Method(type, "Active", typeof(bool)));
            NextPass = Static<double>(Field(type, "s_nextPass", typeof(double)));
            Rtt = Bind<Func<ZNetPeer, float>>(Method(type, "Rtt", typeof(float), typeof(ZNetPeer)));
            Held = Bind<Func<ZDO, bool>>(Method(type, "IsHeld", typeof(bool), typeof(ZDO)));
            Type possession = assembly.GetType("FiresGhettoNetworkMod.ValcastPossession", false);
            Possessed = possession == null ? (Func<ZDO, bool>)NoPossession :
                Bind<Func<ZDO, bool>>(Method(possession, "IsPossessed", typeof(bool), typeof(ZDO)));
            Type gate = assembly.GetType("FiresGhettoNetworkMod.FgnVersionGate", true);
            Matched = Bind<Func<ZNetPeer, bool>>(Method(gate, "IsMatched", typeof(bool), typeof(ZNetPeer)));
            Type authority = assembly.GetType("FiresGhettoNetworkMod.ServerAuthorityPatches", true);
            Simulation = Static<bool>(Field(authority, "SimulationActive", typeof(bool)));
            Type plugin = assembly.GetType("FiresGhettoNetworkMod.FiresGhettoNetworkMod", true);
            ConfigEntry<bool> broad = (ConfigEntry<bool>)Field(plugin, "ConfigEnableServerOwnership", typeof(ConfigEntry<bool>)).GetValue(null);
            ConfigEntry<bool> selective = (ConfigEntry<bool>)Field(plugin, "ConfigEnableServerOwnershipSelective", typeof(ConfigEntry<bool>)).GetValue(null);
            if (broad == null || selective == null || broad.Value || selective.Value)
                throw new NotSupportedException("FGN AI ownership requires both FGN server ZDO ownership options OFF.");
            Type echo = assembly.GetType("FiresGhettoNetworkMod.ConnectionEcho", true);
            MeasuredEcho = Bind<EchoRtt>(Method(echo, "TryRtt", typeof(bool), typeof(ZNetPeer), typeof(float).MakeByRefType()));
            Type link = assembly.GetType("FiresGhettoNetworkMod.LinkController", true);
            SocketPing = Bind<Func<ZNetPeer, int>>(Method(link, "PingMs", typeof(int), typeof(ZNetPeer)));
            PerOwner = (Dictionary<long, int>)Field(type, "s_perOwner", typeof(Dictionary<long, int>)).GetValue(null);
            Cooldowns = (Dictionary<ZDOID, double>)Field(type, "s_cooldowns", typeof(Dictionary<ZDOID, double>)).GetValue(null);
            Candidates = (List<ZNetPeer>)Field(type, "s_candidates", typeof(List<ZNetPeer>)).GetValue(null);
            FieldInfo pending = type.GetField("s_pending", Flags), lastHit = type.GetField("s_lastHitBy", Flags);
            if (pending == null || lastHit == null || pending.FieldType != lastHit.FieldType || !pending.IsStatic)
                throw new NotSupportedException("FGN stamp dictionary contract changed.");
            Type stamp = pending.FieldType.GetGenericArguments()[1];
            if (!stamp.IsValueType || pending.FieldType != typeof(Dictionary<,>).MakeGenericType(typeof(ZDOID), stamp) ||
                stamp.GetField("Peer").FieldType != typeof(long) || stamp.GetField("At").FieldType != typeof(double))
                throw new NotSupportedException("FGN stamp contract changed.");
            Pending = PendingReader(pending, stamp);
            MarkPending = StampWriter(pending, stamp);
            FieldInfo streaks = type.GetField("s_streaks", Flags);
            if (streaks == null || !streaks.IsStatic || !streaks.FieldType.IsGenericType ||
                streaks.FieldType.GetGenericTypeDefinition() != typeof(Dictionary<,>) || streaks.FieldType.GetGenericArguments()[0] != typeof(ZDOID))
                throw new NotSupportedException("FGN ping streak contract changed.");
            DynamicMethod clear = new DynamicMethod("TooManyLagsCombatClearStreak", typeof(void), new[] { typeof(ZDOID) }, typeof(FgnCombatBridge).Module, true);
            ILGenerator clearIl = clear.GetILGenerator(); clearIl.Emit(OpCodes.Ldsfld, streaks); clearIl.Emit(OpCodes.Ldarg_0);
            clearIl.Emit(OpCodes.Callvirt, streaks.FieldType.GetMethod("Remove", new[] { typeof(ZDOID) })); clearIl.Emit(OpCodes.Pop); clearIl.Emit(OpCodes.Ret);
            ClearStreak = (Action<ZDOID>)clear.CreateDelegate(typeof(Action<ZDOID>));
            FieldInfo owned = type.GetField("s_owned", Flags);
            if (owned == null || !owned.IsStatic || !owned.FieldType.IsGenericType ||
                owned.FieldType.GetGenericTypeDefinition() != typeof(Dictionary<,>) || owned.FieldType.GetGenericArguments()[0] != typeof(ZDOID))
                throw new NotSupportedException("FGN ownership hold contract changed.");
            DynamicMethod reset = new DynamicMethod("TooManyLagsCombatClearOwned", typeof(void), new[] { typeof(ZDOID) }, typeof(FgnCombatBridge).Module, true);
            ILGenerator resetIl = reset.GetILGenerator(); resetIl.Emit(OpCodes.Ldsfld, owned); resetIl.Emit(OpCodes.Ldarg_0);
            resetIl.Emit(OpCodes.Callvirt, owned.FieldType.GetMethod("Remove", new[] { typeof(ZDOID) })); resetIl.Emit(OpCodes.Pop); resetIl.Emit(OpCodes.Ret);
            ClearOwned = (Action<ZDOID>)reset.CreateDelegate(typeof(Action<ZDOID>));
            TargetReader = typeof(FgnTargetReader<>).MakeGenericType(stamp).GetMethod("TryGetValue", Flags);
            // Force generic initialization now, so installation errors cannot occur mid-fight.
            TargetReader.DeclaringType.GetMethod("Initialize", Flags).Invoke(null, null);
        }

        internal bool TryMeasuredRtt(ZNetPeer peer, out float value)
        {
            if (MeasuredEcho(peer, out value) && value > 0f && !Single.IsNaN(value) && !Single.IsInfinity(value)) return true;
            int ping = SocketPing(peer); value = ping; return ping > 0;
        }

        private static T Bind<T>(MethodInfo method) where T : class { return (T)(object)Delegate.CreateDelegate(typeof(T), method); }
        private static bool NoPossession(ZDO data) { return false; }
        private static void RequireConstant(Type type, string name, object expected)
        {
            FieldInfo field = type.GetField(name, Flags);
            if (field == null || !field.IsLiteral || !Equals(field.GetRawConstantValue(), expected))
                throw new NotSupportedException("FGN ownership contract changed: " + name);
        }
        private static MethodInfo Method(Type type, string name, Type returns, params Type[] args)
        {
            MethodInfo result = type.GetMethod(name, Flags, null, args, null);
            if (result == null || result.ReturnType != returns) throw new NotSupportedException("FGN method changed: " + name);
            return result;
        }
        private static FieldInfo Field(Type type, string name, Type value)
        {
            FieldInfo field = type.GetField(name, Flags);
            if (field == null || field.FieldType != value || !field.IsStatic) throw new NotSupportedException("FGN field changed: " + name);
            return field;
        }
        private static Func<T> Static<T>(FieldInfo field)
        {
            DynamicMethod method = new DynamicMethod("TooManyLagsCombatRead_" + field.Name, typeof(T), Type.EmptyTypes, typeof(FgnCombatBridge).Module, true);
            ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Ret);
            return (Func<T>)method.CreateDelegate(typeof(Func<T>));
        }
        private static Func<ZDOID, double, bool> PendingReader(FieldInfo field, Type stamp)
        {
            DynamicMethod method = new DynamicMethod("TooManyLagsCombatPending", typeof(bool), new[] { typeof(ZDOID), typeof(double) }, typeof(FgnCombatBridge).Module, true);
            ILGenerator il = method.GetILGenerator(); LocalBuilder value = il.DeclareLocal(stamp); Label absent = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Callvirt, field.FieldType.GetMethod("TryGetValue")); il.Emit(OpCodes.Brfalse, absent);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Ldfld, stamp.GetField("At"));
            il.Emit(OpCodes.Sub); il.Emit(OpCodes.Ldc_R8, 5.0); il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ret);
            il.MarkLabel(absent); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            return (Func<ZDOID, double, bool>)method.CreateDelegate(typeof(Func<ZDOID, double, bool>));
        }
        private static Action<ZDOID, long, double> StampWriter(FieldInfo field, Type stamp)
        {
            DynamicMethod method = new DynamicMethod("TooManyLagsCombatPendingWrite", typeof(void), new[] { typeof(ZDOID), typeof(long), typeof(double) }, typeof(FgnCombatBridge).Module, true);
            ILGenerator il = method.GetILGenerator(); LocalBuilder value = il.DeclareLocal(stamp);
            il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Initobj, stamp);
            il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, stamp.GetField("Peer"));
            il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stfld, stamp.GetField("At"));
            il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Callvirt, field.FieldType.GetProperty("Item").GetSetMethod()); il.Emit(OpCodes.Ret);
            return (Action<ZDOID, long, double>)method.CreateDelegate(typeof(Action<ZDOID, long, double>));
        }
    }

    internal static class FgnTargetReader<TStamp>
    {
        private static readonly Func<long, double, TStamp> create = Create();
        internal static void Initialize() { if (create == null) throw new NotSupportedException(); }
        internal static bool TryGetValue(Dictionary<ZDOID, TStamp> unused, ZDOID id, out TStamp value)
        {
            if (!FgnCombatOwnership.ShouldOverride(id)) return unused.TryGetValue(id, out value);
            long peer;
            if (!FgnCombatOwnership.TryTargetPeer(id, out peer) || peer == 0) { value = default(TStamp); return false; }
            value = create(peer, UnityEngine.Time.realtimeSinceStartupAsDouble); return true;
        }
        private static Func<long, double, TStamp> Create()
        {
            DynamicMethod method = new DynamicMethod("TooManyLagsCombatTargetStamp", typeof(TStamp), new[] { typeof(long), typeof(double) }, typeof(FgnCombatBridge).Module, true);
            ILGenerator il = method.GetILGenerator(); LocalBuilder value = il.DeclareLocal(typeof(TStamp));
            il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Initobj, typeof(TStamp));
            il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stfld, typeof(TStamp).GetField("Peer"));
            il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, typeof(TStamp).GetField("At"));
            il.Emit(OpCodes.Ldloc, value); il.Emit(OpCodes.Ret);
            return (Func<long, double, TStamp>)method.CreateDelegate(typeof(Func<long, double, TStamp>));
        }
    }
}
