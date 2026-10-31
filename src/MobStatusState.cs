using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // Native effects are shallow template clones. Checkpoint their real runtime
    // fields without Setup/ResetTime/SetLevel, which can pay rewards or spawn FX.
    internal static class MobStatusState
    {
        private const int Version = 1, Budget = 16384, MaxEffects = 64, MaxFields = 256, MaxReferences = 32, ReferenceLimit = 2048;
        private sealed class Field
        {
            internal FieldInfo Info;
            internal Func<object, object> Get;
            internal Action<object, object> Set;
        }
        private sealed class Schema
        {
            internal Type Type;
            internal uint Fingerprint;
            internal readonly List<Field> Values = new List<Field>(), References = new List<Field>();
        }
        private sealed class Snapshot
        {
            internal StatusEffect Effect;
            internal Schema Schema;
            internal ZDOID[] Fx;
            internal ZDOID Attacker, Line, Ball;
        }
        private sealed class Binding
        {
            internal Character Actor;
            internal SEMan Manager;
            internal Snapshot State;
            internal ZDO Data;
            internal uint Revision;
        }
        internal sealed class Shadow
        {
            private readonly Character actor;
            private readonly SEMan manager;
            private readonly List<StatusEffect> effects;
            private readonly List<StatusEffect> saved = new List<StatusEffect>();
            private readonly List<int> savedHashes = new List<int>();
            private bool active;
            private int attributesValue, savedAttributes, savedPreviousAttributes;

            internal int Count { get { return effects.Count; } }
            internal ZDOID[] VisualFxIds = new ZDOID[0];

            internal Shadow(Character actor, List<StatusEffect> effects)
            {
                this.actor = actor;
                manager = actor.GetSEMan();
                this.effects = effects;
                foreach (StatusEffect effect in effects)
                {
                    effect.m_character = actor;
                    attributesValue |= (int)effect.m_attributes;
                }
            }

            internal void Enter()
            {
                if (active) return;
                List<StatusEffect> current = manager.GetStatusEffects();
                HashSet<int> hashesValue = hashes(manager);
                saved.Clear(); savedHashes.Clear();
                saved.AddRange(current);
                foreach (int hash in hashesValue) savedHashes.Add(hash);
                savedAttributes = attributesRead(manager);
                savedPreviousAttributes = previousAttributesRead(manager);
                current.Clear(); hashesValue.Clear();
                foreach (StatusEffect effect in effects)
                {
                    current.Add(effect);
                    hashesValue.Add(effect.NameHash());
                }
                attributes(manager, attributesValue);
                previousAttributes(manager, -1);
                active = true;
            }

            internal void Exit()
            {
                if (!active) return;
                List<StatusEffect> current = manager.GetStatusEffects();
                HashSet<int> hashesValue = hashes(manager);
                current.Clear(); hashesValue.Clear();
                current.AddRange(saved);
                foreach (int hash in savedHashes) hashesValue.Add(hash);
                attributes(manager, savedAttributes);
                previousAttributes(manager, savedPreviousAttributes);
                active = false;
            }

            internal void RetainCurrent()
            {
                if (!active) throw new InvalidOperationException("Status shadow is not installed.");
                effects.Clear(); effects.AddRange(manager.GetStatusEffects());
                attributesValue = 0;
                foreach (StatusEffect effect in effects) attributesValue |= (int)effect.m_attributes;
            }

            internal void Advance(float dt)
            {
                if (effects.Count == 0) return;
                int nextAttributes = 0;
                for (int i = effects.Count - 1; i >= 0; --i)
                {
                    StatusEffect effect = effects[i];
                    statusTimeWrite(effect, Math.Max(0f, statusTimeRead(effect) + dt));
                    if (effect.m_ttl > 0f && effect.GetDuration() > effect.m_ttl) effects.RemoveAt(i);
                    else nextAttributes |= (int)effect.m_attributes;
                }
                attributesValue = nextAttributes;
            }

            internal void ApplySpeedMods(ref float speed, Vector3 direction)
            {
                Enter();
                try { manager.ApplyStatusEffectSpeedMods(ref speed, direction); }
                finally { Exit(); }
            }
        }
        private sealed class Tombstone { internal int Prefab; internal bool Known; }
        private struct Death { internal ZDOID Id; internal double Until; }
        private sealed class EffectIdentity : IEqualityComparer<StatusEffect>
        {
            public bool Equals(StatusEffect a, StatusEffect b) { return ReferenceEquals(a, b); }
            public int GetHashCode(StatusEffect value) { return RuntimeHelpers.GetHashCode(value); }
        }
        private static readonly Dictionary<Type, Schema> schemas = new Dictionary<Type, Schema>();
        private static readonly Dictionary<StatusEffect, Binding> deferred = new Dictionary<StatusEffect, Binding>(new EffectIdentity());
        private static readonly Dictionary<ZDOID, Tombstone> cleanup = new Dictionary<ZDOID, Tombstone>();
        private static readonly Dictionary<ZDOID, double> destroyed = new Dictionary<ZDOID, double>();
        private static readonly Queue<Death> deaths = new Queue<Death>();
        private static readonly List<StatusEffect> scratch = new List<StatusEffect>();
        private static Func<SEMan, HashSet<int>> hashes;
        private static Func<SEMan, List<StatusEffect>> removals;
        private static Func<SEMan, int> attributesRead, previousAttributesRead;
        private static Action<SEMan, int> attributes, previousAttributes;
        private static Field startFx, harpoonAttacker, harpoonLine, demisterBall, finderBeacon;
        private static Action<StatusEffect, float> baseTick;
        private static Func<StatusEffect, float> statusTimeRead;
        private static Action<StatusEffect, float> statusTimeWrite;
        private static Func<SE_Harpooned, bool> harpoonBroken;
        private static Harmony hooks;
        private static ZDOMan world;
        private static bool refreshing, deathCapacityReported;

        internal static void Initialize()
        {
            if (hooks != null) return;
            hashes = FieldReader.Create<SEMan, HashSet<int>>(Required(typeof(SEMan), "m_statusEffectsHashSet", typeof(HashSet<int>)));
            removals = FieldReader.Create<SEMan, List<StatusEffect>>(Required(typeof(SEMan), "m_removeStatusEffects", typeof(List<StatusEffect>)));
            attributesRead = FieldReader.Create<SEMan, int>(Required(typeof(SEMan), "m_statusEffectAttributes", typeof(int)));
            previousAttributesRead = FieldReader.Create<SEMan, int>(Required(typeof(SEMan), "m_statusEffectAttributesOld", typeof(int)));
            attributes = Setter<SEMan, int>(Required(typeof(SEMan), "m_statusEffectAttributes", typeof(int)));
            previousAttributes = Setter<SEMan, int>(Required(typeof(SEMan), "m_statusEffectAttributesOld", typeof(int)));
            startFx = Access(Required(typeof(StatusEffect), "m_startEffectInstances", typeof(GameObject[])));
            harpoonAttacker = Access(Required(typeof(SE_Harpooned), "m_attacker", typeof(Character)));
            harpoonLine = Access(Required(typeof(SE_Harpooned), "m_line", typeof(LineConnect)));
            harpoonBroken = FieldReader.Create<SE_Harpooned, bool>(Required(typeof(SE_Harpooned), "m_broken", typeof(bool)));
            demisterBall = Access(Required(typeof(SE_Demister), "m_ballInstance", typeof(GameObject)));
            finderBeacon = Access(AccessTools.Field(typeof(SE_Finder), "m_beacon"));
            FieldInfo statusTime = Required(typeof(StatusEffect), "m_time", typeof(float));
            statusTimeRead = FieldReader.Create<StatusEffect, float>(statusTime);
            statusTimeWrite = Setter<StatusEffect, float>(statusTime);
            DynamicMethod tick = new DynamicMethod("TooManyLagsStatusBaseTick", typeof(void), new[] { typeof(StatusEffect), typeof(float) }, typeof(MobStatusState).Module, true);
            ILGenerator il = tick.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, AccessTools.Method(typeof(StatusEffect), "UpdateStatusEffect")); il.Emit(OpCodes.Ret);
            baseTick = (Action<StatusEffect, float>)tick.CreateDelegate(typeof(Action<StatusEffect, float>));
            Harmony installed = new Harmony(Plugin.Id + ".mobstatusstate");
            try
            {
                installed.Patch(AccessTools.Method(typeof(StatusEffect), "RemoveStartEffects"), prefix: Hook("Removing"));
                installed.Patch(AccessTools.Method(typeof(SE_Demister), "RemoveEffects"), prefix: Hook("Removing"));
                installed.Patch(AccessTools.Method(typeof(SE_Demister), "UpdateStatusEffect"), prefix: Hook("BeforeDemister"));
                installed.Patch(AccessTools.Method(typeof(SE_Harpooned), "IsDone"), prefix: Hook("BeforeHarpoonDone"));
                installed.Patch(AccessTools.Method(typeof(ZNetScene), "AddInstance", new[] { typeof(ZDO), typeof(ZNetView) }), postfix: Hook("Added"));
                installed.Patch(AccessTools.Method(typeof(ZDOMan), "HandleDestroyedZDO", new[] { typeof(ZDOID) }), prefix: Hook("Destroyed"));
                hooks = installed;
            }
            catch { installed.UnpatchSelf(); throw; }
        }
        private static HarmonyMethod Hook(string name) { return new HarmonyMethod(typeof(MobStatusState), name); }
        internal static void Stop() { if (hooks != null) hooks.UnpatchSelf(); hooks = null; Reset(); }
        internal static void Reset() { deferred.Clear(); cleanup.Clear(); destroyed.Clear(); deaths.Clear(); scratch.Clear(); world = null; refreshing = false; deathCapacityReported = false; }
        private static void Context()
        { if (!ReferenceEquals(world, ZDOMan.instance)) { deferred.Clear(); cleanup.Clear(); destroyed.Clear(); deaths.Clear(); deathCapacityReported = false; world = ZDOMan.instance; } }
        private static FieldInfo Required(Type type, string name, Type expected)
        { FieldInfo field = AccessTools.Field(type, name); if (field == null || field.FieldType != expected) throw new NotSupportedException("Native status field changed: " + type.Name + "." + name); return field; }
        private static Action<T, V> Setter<T, V>(FieldInfo field)
        {
            DynamicMethod method = new DynamicMethod("TooManyLagsStatusSet_" + field.Name, typeof(void), new[] { typeof(T), typeof(V) }, typeof(MobStatusState).Module, true);
            ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
            return (Action<T, V>)method.CreateDelegate(typeof(Action<T, V>));
        }
        private static Field Access(FieldInfo field)
        {
            if (field == null) throw new NotSupportedException("Native status reference field absent.");
            DynamicMethod getter = new DynamicMethod("TooManyLagsStatusGet_" + field.Name, typeof(object), new[] { typeof(object) }, typeof(MobStatusState).Module, true);
            ILGenerator il = getter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType); il.Emit(OpCodes.Ldfld, field);
            if (field.FieldType.IsValueType) il.Emit(OpCodes.Box, field.FieldType); il.Emit(OpCodes.Ret);
            DynamicMethod setter = new DynamicMethod("TooManyLagsStatusWrite_" + field.Name, typeof(void), new[] { typeof(object), typeof(object) }, typeof(MobStatusState).Module, true);
            il = setter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType); il.Emit(OpCodes.Ldarg_1);
            il.Emit(field.FieldType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, field.FieldType); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
            return new Field { Info = field, Get = (Func<object, object>)getter.CreateDelegate(typeof(Func<object, object>)), Set = (Action<object, object>)setter.CreateDelegate(typeof(Action<object, object>)) };
        }
        private static bool ValueType(Type type, int depth)
        {
            if (type.IsEnum || type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) return type != typeof(IntPtr) && type != typeof(UIntPtr);
            if (!type.IsValueType || depth >= 4) return false;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) if (!ValueType(field.FieldType, depth + 1)) return false;
            return true;
        }
        private static string Key(FieldInfo field) { return field.DeclaringType.FullName + "/" + field.Name + ":" + field.FieldType.FullName; }
        private static Schema Describe(Type type)
        {
            Schema schema; if (schemas.TryGetValue(type, out schema)) return schema;
            if (schemas.Count >= 128) throw new NotSupportedException("Status type schema limit reached.");
            schema = new Schema { Type = type }; List<FieldInfo> fields = new List<FieldInfo>();
            for (Type current = type; current != null && typeof(StatusEffect).IsAssignableFrom(current); current = current.BaseType)
                fields.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            fields.Sort(delegate(FieldInfo a, FieldInfo b) { return String.CompareOrdinal(Key(a), Key(b)); });
            uint fingerprint = 2166136261U;
            foreach (char c in type.FullName) { fingerprint ^= c; fingerprint *= 16777619U; }
            foreach (FieldInfo field in fields)
            {
                if (field.IsStatic) continue;
                foreach (char c in Key(field)) { fingerprint ^= c; fingerprint *= 16777619U; }
                if (ValueType(field.FieldType, 0)) schema.Values.Add(Access(field)); else schema.References.Add(Access(field));
            }
            if (schema.Values.Count > MaxFields || schema.References.Count > MaxFields) throw new NotSupportedException("Status field schema exceeds bounded capacity.");
            schema.Fingerprint = fingerprint; schemas[type] = schema; return schema;
        }
        private static StatusEffect Template(StatusEffect effect)
        {
            if (!ObjectDB.instance) throw new InvalidOperationException("Status template database is not ready.");
            StatusEffect template = ObjectDB.instance.GetStatusEffect(effect.NameHash());
            if (!template || template.GetType() != effect.GetType()) throw new NotSupportedException("Status template/type unavailable: " + effect.GetType().FullName);
            return template;
        }
        private static bool Adapted(Field field)
        {
            return field.Info == startFx.Info || field.Info.Name == "m_character" && field.Info.DeclaringType == typeof(StatusEffect) ||
                field.Info == harpoonAttacker.Info || field.Info == harpoonLine.Info || field.Info == demisterBall.Info || field.Info == finderBeacon.Info;
        }
        private static ZDOID NetworkId(UnityEngine.Object value)
        {
            if (!value) return ZDOID.None;
            Component component = value as Component; GameObject obj = value as GameObject;
            ZNetView view = component ? component.GetComponentInParent<ZNetView>() : obj ? obj.GetComponent<ZNetView>() : null;
            if (!view || !view.IsValid()) throw new NotSupportedException("Runtime status reference has no network root: " + value.GetType().Name);
            return view.GetZDO().m_uid;
        }
        private static Snapshot Stored(StatusEffect effect, long committedDestination)
        {
            Binding binding;
            if (deferred.Count == 0 || !deferred.TryGetValue(effect, out binding)) return null;
            if (Current(binding)) return binding.State;
            // The final source cleanup runs after its owner write. Keep the
            // same unresolved IDs across that one successful revision change.
            return committedDestination != 0 && binding.Actor && ReferenceEquals(binding.Actor.GetSEMan(), binding.Manager) &&
                binding.Data.IsValid() && binding.Data.GetOwner() == committedDestination && binding.Data.OwnerRevision == binding.Revision + 1U &&
                Contains(binding.Manager, effect) ? binding.State : null;
        }
        private static ZDOID ExistingId(UnityEngine.Object value, ZDOID pending)
        { return value ? NetworkId(value) : pending; }
        private static ZDOID[] EffectIds(StatusEffect effect, Snapshot pending)
        {
            GameObject[] objects = (GameObject[])startFx.Get(effect);
            if (objects == null) return pending == null ? null : pending.Fx;
            if (objects.Length > MaxReferences) throw new NotSupportedException("Status FX reference count exceeded.");
            ZDOID[] ids = new ZDOID[objects.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = ExistingId(objects[i], pending != null && pending.Fx != null && i < pending.Fx.Length ? pending.Fx[i] : ZDOID.None);
            return ids;
        }
        internal static byte[] Capture(Character actor)
        {
            Initialize(); Context();
            if (!actor || actor is Player || actor.GetSEMan() == null) throw new InvalidOperationException("Mob status manager unavailable.");
            List<StatusEffect> effects = actor.GetSEMan().GetStatusEffects(); if (effects.Count == 0) return null;
            if (effects.Count > MaxEffects) throw new NotSupportedException("Active status count exceeds checkpoint capacity.");
            using (MemoryStream stream = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((byte)Version); writer.Write((byte)effects.Count);
                foreach (StatusEffect effect in effects)
                {
                    if (!effect) throw new InvalidOperationException("Null active status effect.");
                    Schema schema = Describe(effect.GetType()); StatusEffect template = Template(effect);
                    foreach (Field field in schema.References) if (!Adapted(field) && !ReferenceEquals(field.Get(effect), field.Get(template)))
                        throw new NotSupportedException("Unsupported mutable status reference: " + Key(field.Info));
                    writer.Write(effect.NameHash()); writer.Write(schema.Fingerprint);
                    // Include scalar defaults too: clients may have different
                    // template configuration, while runtime state must match.
                    writer.Write((ushort)schema.Values.Count);
                    for (int index = 0; index < schema.Values.Count; index++)
                    { Field field = schema.Values[index]; writer.Write((ushort)index); Write(writer, field.Info.FieldType, field.Get(effect)); }
                    Snapshot pending = Stored(effect, 0L);
                    WriteIds(writer, EffectIds(effect, pending));
                    WriteId(writer, effect is SE_Harpooned ? ExistingId((Character)harpoonAttacker.Get(effect), pending == null ? ZDOID.None : pending.Attacker) : ZDOID.None);
                    WriteId(writer, effect is SE_Harpooned ? ExistingId((LineConnect)harpoonLine.Get(effect), pending == null ? ZDOID.None : pending.Line) : ZDOID.None);
                    WriteId(writer, effect is SE_Demister ? ExistingId((GameObject)demisterBall.Get(effect), pending == null ? ZDOID.None : pending.Ball) : ZDOID.None);
                    if (stream.Length > Budget) throw new NotSupportedException("Status checkpoint exceeds 16 KiB.");
                }
                return stream.ToArray();
            }
        }
        internal static Shadow CreateShadow(Character actor, byte[] payload)
        {
            Initialize(); Context();
            if (!actor || actor is Player || actor.GetSEMan() == null) return null;
            List<Snapshot> decoded = Decode(payload);
            if (decoded.Count == 0) return null;
            List<StatusEffect> effects = new List<StatusEffect>(decoded.Count);
            foreach (Snapshot state in decoded) effects.Add(state.Effect);
            Shadow shadow = new Shadow(actor, effects);
            List<ZDOID> visualFx = new List<ZDOID>();
            foreach (Snapshot state in decoded)
                if (state.Fx != null) foreach (ZDOID id in state.Fx) if (!id.IsNone()) visualFx.Add(id);
            shadow.VisualFxIds = visualFx.ToArray();
            return shadow;
        }
        private static void Write(BinaryWriter writer, Type type, object value)
        {
            if (type.IsEnum) { Write(writer, Enum.GetUnderlyingType(type), Convert.ChangeType(value, Enum.GetUnderlyingType(type))); return; }
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: writer.Write((bool)value); return;
                case TypeCode.Byte: writer.Write((byte)value); return; case TypeCode.SByte: writer.Write((sbyte)value); return;
                case TypeCode.Int16: writer.Write((short)value); return; case TypeCode.UInt16: writer.Write((ushort)value); return;
                case TypeCode.Int32: writer.Write((int)value); return; case TypeCode.UInt32: writer.Write((uint)value); return;
                case TypeCode.Int64: writer.Write((long)value); return; case TypeCode.UInt64: writer.Write((ulong)value); return;
                case TypeCode.Char: writer.Write((ushort)(char)value); return;
                case TypeCode.Single: if (Single.IsNaN((float)value) || Single.IsInfinity((float)value)) throw new NotSupportedException("Non-finite status field."); writer.Write((float)value); return;
                case TypeCode.Double: if (Double.IsNaN((double)value) || Double.IsInfinity((double)value)) throw new NotSupportedException("Non-finite status field."); writer.Write((double)value); return;
                case TypeCode.Decimal: writer.Write((decimal)value); return;
                case TypeCode.String: writer.Write(value != null); if (value != null) { string text = (string)value; if (text.Length > 1024) throw new NotSupportedException("Status string too long."); writer.Write(text); } return;
            }
            foreach (FieldInfo field in StructFields(type)) Write(writer, field.FieldType, field.GetValue(value));
        }
        private static readonly Dictionary<Type, FieldInfo[]> structs = new Dictionary<Type, FieldInfo[]>();
        private static FieldInfo[] StructFields(Type type)
        {
            FieldInfo[] fields; if (structs.TryGetValue(type, out fields)) return fields;
            fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, delegate(FieldInfo a, FieldInfo b) { return String.CompareOrdinal(a.Name, b.Name); }); structs[type] = fields; return fields;
        }
        private static object Read(BinaryReader reader, Type type)
        {
            if (type.IsEnum) return Enum.ToObject(type, Read(reader, Enum.GetUnderlyingType(type)));
            object value;
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: return reader.ReadBoolean();
                case TypeCode.Byte: return reader.ReadByte(); case TypeCode.SByte: return reader.ReadSByte();
                case TypeCode.Int16: return reader.ReadInt16(); case TypeCode.UInt16: return reader.ReadUInt16();
                case TypeCode.Int32: return reader.ReadInt32(); case TypeCode.UInt32: return reader.ReadUInt32();
                case TypeCode.Int64: return reader.ReadInt64(); case TypeCode.UInt64: return reader.ReadUInt64();
                case TypeCode.Char: return (char)reader.ReadUInt16();
                case TypeCode.Single: value = reader.ReadSingle(); if (Single.IsNaN((float)value) || Single.IsInfinity((float)value)) throw new InvalidDataException("Non-finite status field."); return value;
                case TypeCode.Double: value = reader.ReadDouble(); if (Double.IsNaN((double)value) || Double.IsInfinity((double)value)) throw new InvalidDataException("Non-finite status field."); return value;
                case TypeCode.Decimal: return reader.ReadDecimal();
                case TypeCode.String: if (!reader.ReadBoolean()) return null; string text = reader.ReadString(); if (text.Length > 1024) throw new InvalidDataException("Status string too long."); return text;
            }
            value = Activator.CreateInstance(type); foreach (FieldInfo field in StructFields(type)) field.SetValue(value, Read(reader, field.FieldType)); return value;
        }
        private static void WriteId(BinaryWriter writer, ZDOID id) { writer.Write(id.UserID); writer.Write(id.ID); }
        private static ZDOID ReadId(BinaryReader reader) { return new ZDOID(reader.ReadInt64(), reader.ReadUInt32()); }
        private static void WriteIds(BinaryWriter writer, ZDOID[] ids)
        { writer.Write(ids == null ? (byte)0 : (byte)(ids.Length + 1)); if (ids != null) foreach (ZDOID id in ids) WriteId(writer, id); }
        private static ZDOID[] ReadIds(BinaryReader reader)
        { int count = reader.ReadByte(); if (count == 0) return null; if (--count > MaxReferences) throw new InvalidDataException("Too many status FX."); ZDOID[] ids = new ZDOID[count]; for (int i = 0; i < count; i++) ids[i] = ReadId(reader); return ids; }
        private static List<Snapshot> Decode(byte[] payload)
        {
            List<Snapshot> result = new List<Snapshot>(); if (payload == null) return result;
            if (payload.Length > Budget) throw new InvalidDataException("Status payload exceeds 16 KiB.");
            using (MemoryStream stream = new MemoryStream(payload, false)) using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadByte() != Version) throw new InvalidDataException("Unsupported status checkpoint.");
                int count = reader.ReadByte(); if (count > MaxEffects) throw new InvalidDataException("Too many statuses."); HashSet<int> names = new HashSet<int>();
                for (int i = 0; i < count; i++)
                {
                    int hash = reader.ReadInt32(); uint fingerprint = reader.ReadUInt32();
                    StatusEffect template = ObjectDB.instance ? ObjectDB.instance.GetStatusEffect(hash) : null;
                    if (!template || !names.Add(hash)) throw new InvalidDataException("Missing or duplicate status template: " + hash);
                    Schema schema = Describe(template.GetType()); if (schema.Fingerprint != fingerprint) throw new NotSupportedException("Status schema differs between peers: " + template.GetType().FullName);
                    StatusEffect effect = template.Clone(); int fields = reader.ReadUInt16(); if (fields > schema.Values.Count) throw new InvalidDataException("Invalid status field count.");
                    int previous = -1;
                    for (int n = 0; n < fields; n++)
                    { int index = reader.ReadUInt16(); if (index <= previous || index >= schema.Values.Count) throw new InvalidDataException("Invalid status field order."); previous = index; Field field = schema.Values[index]; field.Set(effect, Read(reader, field.Info.FieldType)); }
                    Snapshot state = new Snapshot { Effect = effect, Schema = schema, Fx = ReadIds(reader), Attacker = ReadId(reader), Line = ReadId(reader), Ball = ReadId(reader) };
                    if (!(effect is SE_Harpooned) && (!state.Attacker.IsNone() || !state.Line.IsNone()) || !(effect is SE_Demister) && !state.Ball.IsNone()) throw new InvalidDataException("Unexpected status reference adapter.");
                    effect.m_character = null; startFx.Set(effect, null);
                    if (effect is SE_Harpooned) { harpoonAttacker.Set(effect, null); harpoonLine.Set(effect, null); }
                    if (effect is SE_Demister) demisterBall.Set(effect, null);
                    if (effect is SE_Finder) finderBeacon.Set(effect, null);
                    result.Add(state);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing status checkpoint bytes.");
            }
            return result;
        }
        internal static void Restore(Character actor, byte[] payload)
        {
            Initialize(); Context();
            if (!actor || actor is Player || actor.GetSEMan() == null) throw new InvalidOperationException("Mob status destination unavailable.");
            List<Snapshot> decoded = Decode(payload); SEMan manager = actor.GetSEMan();
            if (deferred.Count + decoded.Count > ReferenceLimit) throw new InvalidOperationException("Deferred status reference capacity reached.");
            List<StatusEffect> next = new List<StatusEffect>(); int flags = 0;
            ZNetView view = actor.GetComponent<ZNetView>(); ZDO data = view && view.IsValid() ? view.GetZDO() : null;
            if (data == null || !data.IsOwner()) throw new InvalidOperationException("Status restore requires the new native owner.");
            foreach (Snapshot state in decoded) { state.Effect.m_character = actor; flags |= (int)state.Effect.m_attributes; next.Add(state.Effect); }
            // No virtual Stop/OnDestroy: this manager may still hold an obsolete
            // replica from a previous owner epoch. Retiring it must not destroy
            // the source's live FX or pay the effect's end/start gameplay twice.
            RetireLocalFx(manager, decoded);
            Detach(manager);
            List<StatusEffect> effects = manager.GetStatusEffects(); effects.AddRange(next); HashSet<int> set = hashes(manager); foreach (StatusEffect effect in next) set.Add(effect.NameHash());
            attributes(manager, flags); previousAttributes(manager, -1);
            foreach (Snapshot state in decoded)
            {
                Binding binding = new Binding { Actor = actor, Manager = manager, State = state, Data = data, Revision = data.OwnerRevision };
                if (!Resolve(binding)) deferred[state.Effect] = binding;
            }
        }
        private static void AddReference(HashSet<ZDOID> ids, ZDOID id) { if (!id.IsNone()) ids.Add(id); }
        private static void RetireLocalFx(SEMan manager, List<Snapshot> incoming)
        {
            if (manager.GetStatusEffects().Count == 0) return;
            HashSet<ZDOID> preserved = new HashSet<ZDOID>();
            foreach (Snapshot state in incoming)
            {
                if (state.Fx != null) foreach (ZDOID id in state.Fx) AddReference(preserved, id);
                AddReference(preserved, state.Ball); AddReference(preserved, state.Line);
            }
            // Native replica startup can create equipment FX too. Retire only
            // displaced local FX, never the authoritative incoming references.
            foreach (StatusEffect effect in manager.GetStatusEffects())
            {
                Binding binding; Snapshot pending = deferred.TryGetValue(effect, out binding) ? binding.State : null;
                GameObject[] objects = (GameObject[])startFx.Get(effect);
                if (objects != null) foreach (GameObject obj in objects) RetireObject(obj, preserved);
                if (effect is SE_Demister) RetireObject((GameObject)demisterBall.Get(effect), preserved);
                if (effect is SE_Harpooned) RetireObject((LineConnect)harpoonLine.Get(effect), preserved);
                if (pending == null) continue;
                if (pending.Fx != null) foreach (ZDOID id in pending.Fx) RetireId(id, preserved);
                RetireId(pending.Ball, preserved); RetireId(pending.Line, preserved);
            }
        }
        private static void RetireObject(UnityEngine.Object value, HashSet<ZDOID> preserved)
        {
            if (!value) return;
            Component component = value as Component; GameObject obj = component ? component.gameObject : value as GameObject;
            ZNetView view = component ? component.GetComponentInParent<ZNetView>() : obj ? obj.GetComponent<ZNetView>() : null;
            if (view)
            {
                if (view.IsValid() && view.IsOwner() && preserved.Add(view.GetZDO().m_uid)) view.Destroy();
            }
            else if (obj) UnityEngine.Object.Destroy(obj);
        }
        private static void RetireId(ZDOID id, HashSet<ZDOID> preserved)
        {
            if (id.IsNone() || world == null || preserved.Contains(id)) return;
            ZDO data = world.GetZDO(id);
            if (data != null && data.IsValid() && data.IsOwner() && preserved.Add(id)) world.DestroyZDO(data);
        }
        private static void Detach(SEMan manager)
        {
            foreach (StatusEffect effect in manager.GetStatusEffects())
            {
                deferred.Remove(effect); startFx.Set(effect, null); effect.m_character = null;
                if (effect is SE_Harpooned) { harpoonAttacker.Set(effect, null); harpoonLine.Set(effect, null); }
                if (effect is SE_Demister) demisterBall.Set(effect, null);
            }
            manager.GetStatusEffects().Clear(); hashes(manager).Clear(); removals(manager).Clear(); attributes(manager, 0); previousAttributes(manager, -1);
        }
        internal static void Relinquish(Character actor, byte[] payload, long destination)
        {
            if (!actor || actor is Player || actor.GetSEMan() == null) return; Initialize(); Context();
            // Snapshot is already decoded/validated before owner commit; transfer
            // only existing referenced FX, never instantiate replacement effects.
            List<StatusEffect> effects = actor.GetSEMan().GetStatusEffects();
            foreach (StatusEffect effect in effects)
            {
                Snapshot pending = Stored(effect, destination); ZDOID[] ids = EffectIds(effect, pending);
                if (ids != null) foreach (ZDOID id in ids) TransferReference(id, destination);
                if (effect is SE_Demister) TransferReference(ExistingId((GameObject)demisterBall.Get(effect), pending == null ? ZDOID.None : pending.Ball), destination);
                if (effect is SE_Harpooned) TransferReference(ExistingId((LineConnect)harpoonLine.Get(effect), pending == null ? ZDOID.None : pending.Line), destination);
            }
            Detach(actor.GetSEMan()); GC.KeepAlive(payload);
        }
        private static void TransferReference(ZDOID id, long destination)
        {
            ZDO data = !id.IsNone() && world != null ? world.GetZDO(id) : null;
            if (data != null && data.IsValid() && data.IsOwner()) data.SetOwner(destination);
        }
        private static GameObject Find(ZDOID id)
        { return id.IsNone() || !ZNetScene.instance ? null : ZNetScene.instance.FindInstance(id); }
        private static bool Resolve(Binding binding)
        {
            Snapshot state = binding.State; bool done = true;
            if (state.Fx != null)
            {
                GameObject[] objects = (GameObject[])startFx.Get(state.Effect);
                if (objects == null || objects.Length != state.Fx.Length) objects = new GameObject[state.Fx.Length];
                for (int i = 0; i < objects.Length; i++) { if (Dead(state.Fx[i])) state.Fx[i] = ZDOID.None; objects[i] = Adopt(state.Fx[i]); if (!state.Fx[i].IsNone() && !objects[i]) done = false; }
                startFx.Set(state.Effect, objects);
            }
            if (state.Effect is SE_Harpooned)
            {
                if (Dead(state.Attacker)) state.Attacker = ZDOID.None;
                if (Dead(state.Line)) state.Line = ZDOID.None;
                GameObject attacker = Find(state.Attacker), line = Adopt(state.Line);
                harpoonAttacker.Set(state.Effect, attacker ? attacker.GetComponent<Character>() : null);
                harpoonLine.Set(state.Effect, line ? line.GetComponent<LineConnect>() : null);
                if (!state.Attacker.IsNone() && !attacker || !state.Line.IsNone() && !line) done = false;
            }
            if (state.Effect is SE_Demister)
            {
                if (Dead(state.Ball)) state.Ball = ZDOID.None;
                GameObject ball = Adopt(state.Ball); demisterBall.Set(state.Effect, ball);
                if (!state.Ball.IsNone() && !ball) done = false;
            }
            return done;
        }
        private static bool Current(Binding binding)
        {
            if (!binding.Actor || !ReferenceEquals(binding.Actor.GetSEMan(), binding.Manager) || !binding.Data.IsValid() || !binding.Data.IsOwner() ||
                binding.Data.OwnerRevision != binding.Revision) return false;
            return Contains(binding.Manager, binding.State.Effect);
        }
        private static bool Contains(SEMan manager, StatusEffect expected)
        {
            foreach (StatusEffect effect in manager.GetStatusEffects()) if (ReferenceEquals(effect, expected)) return true;
            return false;
        }
        private static GameObject Adopt(ZDOID id)
        {
            GameObject obj = Find(id); ZNetView view = obj ? obj.GetComponent<ZNetView>() : null;
            // Late scene creation can follow a second mob handoff. Existing FX
            // still need their current simulator for native line/ball writes.
            if (view && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
            return obj;
        }
        internal static void RefreshReferences()
        {
            if (refreshing || deferred.Count == 0) return;
            Context(); if (world == null) return;
            try
            {
                refreshing = true; scratch.Clear(); foreach (StatusEffect effect in deferred.Keys) scratch.Add(effect);
                foreach (StatusEffect effect in scratch)
                {
                    Binding binding; if (!deferred.TryGetValue(effect, out binding)) continue;
                    if (!Current(binding)) { deferred.Remove(effect); continue; }
                    if (Resolve(binding)) deferred.Remove(effect);
                }
            }
            finally { scratch.Clear(); refreshing = false; }
        }
        private static void Removing(StatusEffect __instance)
        {
            Binding binding; if (!deferred.TryGetValue(__instance, out binding)) return;
            deferred.Remove(__instance); if (!Current(binding)) return;
            Snapshot state = binding.State;
            if (state.Fx != null) foreach (ZDOID id in state.Fx) if (!Find(id)) MarkCleanup(id);
            if (!state.Ball.IsNone() && !Find(state.Ball)) MarkCleanup(state.Ball);
            if (!state.Line.IsNone() && !Find(state.Line)) MarkCleanup(state.Line);
        }
        private static void MarkCleanup(ZDOID id)
        {
            if (id.IsNone() || world == null || Dead(id)) return; ZDO data = world.GetZDO(id); if (data != null && !data.IsValid()) return;
            if (cleanup.Count >= ReferenceLimit && !cleanup.ContainsKey(id)) { Plugin.Warn("Deferred status FX cleanup capacity exceeded."); return; }
            cleanup[id] = new Tombstone { Prefab = data == null ? 0 : data.GetPrefab(), Known = data != null }; Clean(id);
        }
        private static void Clean(ZDOID id)
        {
            Tombstone tombstone; if (!cleanup.TryGetValue(id, out tombstone)) return;
            ZDO data = world.GetZDO(id); if (data == null) return;
            if (!data.IsValid() || tombstone.Known && data.GetPrefab() != tombstone.Prefab) { cleanup.Remove(id); return; }
            if (!tombstone.Known) { tombstone.Known = true; tombstone.Prefab = data.GetPrefab(); }
            GameObject obj = Find(id); ZNetView view = obj ? obj.GetComponent<ZNetView>() : null;
            if (!view || !view.IsValid() || !ReferenceEquals(view.GetZDO(), data)) return;
            cleanup.Remove(id); view.ClaimOwnership(); view.Destroy();
        }
        private static void Added(ZDO __0, ZNetView __1)
        {
            if (deferred.Count == 0 && cleanup.Count == 0) return;
            try { Context(); if (__0 != null && cleanup.ContainsKey(__0.m_uid)) Clean(__0.m_uid); RefreshReferences(); }
            catch (Exception error) { Plugin.Warn("Status reference restore failed: " + error.GetBaseException().Message); }
            GC.KeepAlive(__1);
        }
        private static void PruneDeaths(double now)
        {
            while (deaths.Count != 0 && deaths.Peek().Until <= now)
            { Death death = deaths.Dequeue(); destroyed.Remove(death.Id); }
        }
        private static bool Dead(ZDOID id)
        {
            if (id.IsNone() || destroyed.Count == 0) return false;
            PruneDeaths(Time.realtimeSinceStartupAsDouble); return destroyed.ContainsKey(id);
        }
        private static bool References(Snapshot state, ZDOID id)
        {
            if (state.Attacker == id || state.Line == id || state.Ball == id) return true;
            if (state.Fx != null) foreach (ZDOID fx in state.Fx) if (fx == id) return true;
            return false;
        }
        private static void RefreshDestroyed(ZDOID id)
        {
            if (refreshing || deferred.Count == 0) return;
            try
            {
                refreshing = true; scratch.Clear();
                foreach (KeyValuePair<StatusEffect, Binding> pair in deferred)
                    if (References(pair.Value.State, id)) scratch.Add(pair.Key);
                foreach (StatusEffect effect in scratch)
                {
                    Binding binding; if (!deferred.TryGetValue(effect, out binding)) continue;
                    if (!Current(binding) || Resolve(binding)) deferred.Remove(effect);
                }
            }
            finally { scratch.Clear(); refreshing = false; }
        }
        private static void Destroyed(ZDOMan __instance, ZDOID __0)
        {
            try
            {
                if (__0.IsNone()) return;
                Context(); if (!ReferenceEquals(world, __instance)) return;
                double now = Time.realtimeSinceStartupAsDouble; PruneDeaths(now);
                // Real deletion may precede checkpoint/owner adoption. Retain a
                // bounded recent-ID history; ordinary scene unload never enters it.
                if (!destroyed.ContainsKey(__0))
                {
                    if (deaths.Count >= ReferenceLimit)
                    {
                        Death oldest = deaths.Dequeue(); destroyed.Remove(oldest.Id);
                        if (!deathCapacityReported) { deathCapacityReported = true; Plugin.Warn("Recent status reference deletion capacity exceeded."); }
                    }
                    double until = now + 10.0; destroyed.Add(__0, until); deaths.Enqueue(new Death { Id = __0, Until = until });
                }
                cleanup.Remove(__0); RefreshDestroyed(__0);
            }
            catch (Exception error) { Plugin.Warn("Status reference deletion handling failed: " + error.GetBaseException().Message); }
        }
        private static bool BeforeDemister(SE_Demister __instance, float dt)
        {
            Binding binding; if (deferred.Count == 0 || !deferred.TryGetValue(__instance, out binding) || binding.State.Ball.IsNone() || Find(binding.State.Ball)) return true;
            // Native Update would spawn a second network ball while its existing
            // reference is merely awaiting scene instantiation.
            baseTick(__instance, dt); return false;
        }
        private static bool BeforeHarpoonDone(SE_Harpooned __instance, ref bool __result)
        {
            Binding binding; if (deferred.Count == 0 || harpoonBroken(__instance) || !deferred.TryGetValue(__instance, out binding) || binding.State.Attacker.IsNone() || Find(binding.State.Attacker)) return true;
            // Missing local node is not a released harpoon. Native TTL still
            // expires normally while the existing attacker becomes available.
            __result = __instance.m_ttl > 0f && __instance.GetDuration() > __instance.m_ttl; return false;
        }
    }
}
