#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
// Transaction and policy primitives shared by the private native steering fork.
// No Harmony hooks or native physics are installed here. The experimental .88
// runner is NativeSteeringTrack; complete Unity scene validation remains pending.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace NativeMobPreviewAudit
{
    public enum StepStatus
    {
        Produced, NotDue, Expired, Stopped, InvalidTime, WrongThread, NestedScope,
        OwnerChanged, ActionRequested, ExceptionThrown, MissingNativeGuards
    }

    [Flags]
    public enum PreviewSignal
    {
        None = 0, AttackRequested = 1, UnsupportedActionRequested = 2,
        NetworkWriteAttempted = 4, ExternalNotificationSuppressed = 8
    }

    public struct SteeringFrame
    {
        public Vector3 MoveDirection;
        public bool Run;
        public bool Walk;
        public bool Flying;
        public Vector3 LookDirection;
        public Quaternion LookYaw;
        public double NativeTime;
    }

    public struct StepResult
    {
        public StepStatus Status;
        public bool NativeRan;
        public bool CallbackRan;
        public PreviewSignal Signals;
        public SteeringFrame Frame;
        public Exception Error;
        public bool Produced { get { return Status == StepStatus.Produced; } }
    }

    internal static class NativeSteeringPolicy
    {
        internal static void HoldAttack(ref SteeringFrame frame)
        {
            frame.MoveDirection = new Vector3();
            frame.Run = false;
        }
    }

    // Legacy research frontgate deliberately remains closed. The .88 plugin uses
    // the separate, internal NativeSteeringTrack with the audited private IL plan.
    // This public surface cannot bypass the internal runner's admission rules.
    public static class NativeSteeringSandbox
    {
        public static bool CanExecuteNativeAI { get { return false; } }

        public static StepResult TryStep(MonsterAI actor, double nativeRealtime)
        {
            return new StepResult { Status = StepStatus.MissingNativeGuards };
        }

        public static string[] RequiredGuards()
        {
            return new[]
            {
                "Unity-main-thread admission; awake nonboss untamed ordinary MonsterAI; no unfinished attack, saddle or unsupported subclass",
                "Exact-view ZNetView.IsOwner scope; never ZDO.IsOwner; reject real owner/revision change before publishing",
                "BaseAI.UpdateRegeneration and UpdateTakeoffLanding",
                "Character.Jump, ForceJump, TakeOff, Land and all reachable physical/gameplay mutations",
                "MonsterAI.UpdateSleep, UpdateConsumeItem and DoAttack (report attack request)",
                "BaseAI.SetAlerted plus MonsterAI override (retain shadow alert semantics without effects/messages/global-key writes)",
                "BaseAI.SetTargetInfo and SetHuntPlayer (retain shadow semantics without ZDO writes)",
                "BaseAI.MoveAwayAndDespawn (report unsupported despawn request)",
                "MonsterAI.SelectBestAttack (no EquipBestWeapon mutation; report unsupported selection instead of fake steering)",
                "BaseAI.ChargeStart/ChargeStop (retain shadow charge semantics without animation writes)",
                "Every ZSyncAnimation mutating setter/trigger and Player.OnTargeted",
                "Every ZNetView.InvokeRPC and ZRpc.Invoke overload",
                "Every ZDO.Set/SetOwner mutation overload, including calls on foreign ZDOs",
                "TooManyLags.FgnTargetBoundary.AfterAi/Changed and FgnTargetEvents.Schedule/Flush/Changed dispatch paths",
                "FGN 1.5.72 MonsterAI/TargetSync target dispatch and patched target stores; preserve native shadow assignment only",
                "Full reachable-call mutation/collection audit, static scratch/cache policy, and compatible third-party Harmony prefix/postfix/finalizer ordering",
                "Complete BaseAI/MonsterAI mutable-field manifest, Character controls and UnityEngine.Random.state capture/restore",
                "Original native AI scheduling suspended for this actor; one native 0.05s pass maximum; bounded actor count/lifetime; no catch-up"
            };
        }

        // Metadata-only binding audit; finding a method is NOT guard coverage.
        // The real-game build must compile this against installed game + Harmony.
        public static bool InstalledAiSignatureExists()
        {
#if SANDBOX_FIXTURES
            MethodInfo method = typeof(MonsterAI).GetMethod("UpdateAI", new[] { typeof(float) });
#else
            MethodInfo method = HarmonyLib.AccessTools.Method(typeof(MonsterAI), "UpdateAI", new[] { typeof(float) });
#endif
            return method != null && method.ReturnType == typeof(bool);
        }
    }

    internal interface IShadowSlot
    {
        void CaptureReal();
        void InstallShadow();
        void CaptureShadow();
        void RestoreReal();
    }

    // Closed generic slots keep value types unboxed. Reflection/IL compilation
    // happens once at setup; stepping dispatches typed delegates through this
    // reference-type interface. No GetValue/SetValue, Invoke or LINQ per step.
    internal sealed class FieldSlot<A, V> : IShadowSlot where A : class
    {
        private readonly A target;
        private readonly Func<A, V> read;
        private readonly Action<A, V> write;
        private V real;
        private V shadow;

        public FieldSlot(A value, FieldInfo field)
        {
            target = value;
            read = FieldAccess.Reader<A, V>(field);
            write = FieldAccess.Writer<A, V>(field);
            shadow = read(target);
        }

        public void CaptureReal() { real = read(target); }
        public void InstallShadow() { write(target, shadow); }
        public void CaptureShadow() { shadow = read(target); }
        public void RestoreReal() { write(target, real); }
    }

    internal static class FieldAccess
    {
        internal static void Prepare(Type actorType, Type valueType, FieldInfo field)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo reader = typeof(FieldAccess).GetMethod("Reader", flags).MakeGenericMethod(actorType, valueType);
            MethodInfo writer = typeof(FieldAccess).GetMethod("Writer", flags).MakeGenericMethod(actorType, valueType);
            reader.Invoke(null, new object[] { field });
            writer.Invoke(null, new object[] { field });
        }
        internal static Func<A, V> Reader<A, V>(FieldInfo field)
        {
            lock (FieldCache<A, V>.Readers)
            {
                Func<A, V> read;
                if (!FieldCache<A, V>.Readers.TryGetValue(field, out read)) FieldCache<A, V>.Readers.Add(field, read = CreateReader<A, V>(field));
                return read;
            }
        }
        private static Func<A, V> CreateReader<A, V>(FieldInfo field)
        {
            DynamicMethod method = new DynamicMethod("SandboxRead_" + field.Name,
                typeof(V), new[] { typeof(A) }, typeof(FieldAccess).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
            return (Func<A, V>)method.CreateDelegate(typeof(Func<A, V>));
        }

        internal static Action<A, V> Writer<A, V>(FieldInfo field)
        {
            lock (FieldCache<A, V>.Writers)
            {
                Action<A, V> write;
                if (!FieldCache<A, V>.Writers.TryGetValue(field, out write)) FieldCache<A, V>.Writers.Add(field, write = CreateWriter<A, V>(field));
                return write;
            }
        }
        private static Action<A, V> CreateWriter<A, V>(FieldInfo field)
        {
            DynamicMethod method = new DynamicMethod("SandboxWrite_" + field.Name,
                typeof(void), new[] { typeof(A), typeof(V) }, typeof(FieldAccess).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
            return (Action<A, V>)method.CreateDelegate(typeof(Action<A, V>));
        }
    }

    // Bounded by the audited field/type set, not by actors or handoff count.
    // The lock is used during track construction only, never during a step.
    internal static class FieldCache<A, V>
    {
        internal static readonly Dictionary<FieldInfo, Func<A, V>> Readers = new Dictionary<FieldInfo, Func<A, V>>();
        internal static readonly Dictionary<FieldInfo, Action<A, V>> Writers = new Dictionary<FieldInfo, Action<A, V>>();
    }

    internal static class MethodReader<A, V> where A : class
    {
        private static readonly Dictionary<MethodInfo, Func<A, V>> Readers =
            new Dictionary<MethodInfo, Func<A, V>>();

        internal static Func<A, V> Bind(MethodInfo method)
        {
            if (method == null) throw new ArgumentNullException("method");
            lock (Readers)
            {
                Func<A, V> read;
                if (!Readers.TryGetValue(method, out read)) Readers.Add(method, read = Create(method));
                return read;
            }
        }

        private static Func<A, V> Create(MethodInfo method)
        {
            if (method.IsStatic || !method.DeclaringType.IsAssignableFrom(typeof(A)))
                throw new NotSupportedException("Method reader receiver mismatch.");
            DynamicMethod access = new DynamicMethod("SandboxReadMethod_" + method.Name,
                typeof(V), new[] { typeof(A) }, typeof(MethodReader<A, V>).Module, true);
            ILGenerator il = access.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, method); il.Emit(OpCodes.Ret);
            return (Func<A, V>)access.CreateDelegate(typeof(Func<A, V>));
        }
    }

    // Readonly m_path retains its original List identity. Real/shadow contents
    // have separate bounded arrays; neither aliases the mutable native list.
    internal sealed class VectorPathSlot<A> : IShadowSlot where A : class
    {
        private readonly A target;
        private readonly Func<A, List<Vector3>> read;
        private readonly List<Vector3> path;
        private readonly Vector3[] real;
        private readonly Vector3[] shadow;
        private int realCount, shadowCount, realCapacity;

        public VectorPathSlot(A value, FieldInfo field, int limit)
        {
            if (limit < 0) throw new ArgumentOutOfRangeException("limit");
            target = value; read = FieldAccess.Reader<A, List<Vector3>>(field);
            path = read(target);
            if (path == null) throw new NotSupportedException("A null path list is unsupported.");
            real = new Vector3[limit]; shadow = new Vector3[limit];
            shadowCount = CopyTo(shadow);
        }

        private int CopyTo(Vector3[] destination)
        {
            if (!ReferenceEquals(read(target), path)) throw new InvalidOperationException("Path list identity changed.");
            int count = path.Count;
            if (count > destination.Length) throw new NotSupportedException("Path point bound exceeded.");
            path.CopyTo(destination, 0);
            return count;
        }

        private void CopyFrom(Vector3[] source, int count)
        {
            path.Clear();
            for (int i = 0; i < count; ++i) path.Add(source[i]);
        }

        public void CaptureReal() { realCount = CopyTo(real); realCapacity = path.Capacity; }
        public void InstallShadow() { CopyFrom(shadow, shadowCount); }
        public void CaptureShadow() { shadowCount = CopyTo(shadow); }
        public void RestoreReal()
        {
            CopyFrom(real, realCount);
            // Also restore capacity if the tested callback grew the native list.
            // This exceptional/growing path can allocate; no claim is made that
            // real native pathfinding, or a shrinking restore, allocates nothing.
            if (path.Capacity != realCapacity) path.Capacity = realCapacity;
        }
    }

    internal sealed class ShadowSnapshot<A> where A : class
    {
        // Cache constructors by field type, never by actor or snapshot state.
        // Admission validation below still runs for every requested field set.
        private static readonly Dictionary<Type, Func<A, FieldInfo, IShadowSlot>> factories =
            new Dictionary<Type, Func<A, FieldInfo, IShadowSlot>>();
        private readonly IShadowSlot[] slots;
        private bool installed;
        private bool captured;

        private ShadowSnapshot(IShadowSlot[] value) { slots = value; }
#if SANDBOX_FIXTURES
        internal static ShadowSnapshot<A> FixtureSlots(params IShadowSlot[] value) { return new ShadowSnapshot<A>(value); }
#endif

        internal static ShadowSnapshot<A> Create(A actor, FieldInfo[] fields, int maxPathPoints)
        {
            if (actor == null || fields == null) throw new ArgumentNullException("snapshot input");
            IShadowSlot[] result = new IShadowSlot[fields.Length];
            HashSet<FieldInfo> seen = new HashSet<FieldInfo>();
            for (int i = 0; i < fields.Length; ++i)
            {
                FieldInfo field = fields[i];
                if (field == null || field.IsStatic || !field.DeclaringType.IsAssignableFrom(typeof(A)) || !seen.Add(field))
                    throw new NotSupportedException("Snapshot field is invalid or duplicated.");
                if (field.FieldType == typeof(List<Vector3>) && field.IsInitOnly)
                { result[i] = new VectorPathSlot<A>(actor, field, maxPathPoints); continue; }
                Type type = field.FieldType;
                bool scalar = type.IsPrimitive || type.IsEnum || type == typeof(decimal) ||
                    type == typeof(Vector3) || type == typeof(Quaternion) || type == typeof(string) ||
                    typeof(UnityEngine.Object).IsAssignableFrom(type);
                if (!scalar || field.IsInitOnly)
                    throw new NotSupportedException("Unsupported mutable/reference field: " + field.DeclaringType.FullName + "." + field.Name);
                result[i] = Factory(type)(actor, field);
            }
            return new ShadowSnapshot<A>(result);
        }

        private static Func<A, FieldInfo, IShadowSlot> Factory(Type type)
        {
            lock (factories)
            {
                Func<A, FieldInfo, IShadowSlot> factory;
                if (factories.TryGetValue(type, out factory)) return factory;
                Type closed = typeof(FieldSlot<,>).MakeGenericType(typeof(A), type);
                ConstructorInfo constructor = closed.GetConstructor(new[] { typeof(A), typeof(FieldInfo) });
                if (constructor == null) throw new NotSupportedException("Missing snapshot slot constructor.");
                try { RuntimeHelpers.PrepareMethod(constructor.MethodHandle); } catch (Exception) { }
                DynamicMethod method = new DynamicMethod("SandboxSlot_" + type.Name, typeof(IShadowSlot),
                    new[] { typeof(A), typeof(FieldInfo) }, typeof(ShadowSnapshot<A>).Module, true);
                ILGenerator il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Newobj, constructor); il.Emit(OpCodes.Ret);
                factory = (Func<A, FieldInfo, IShadowSlot>)method.CreateDelegate(typeof(Func<A, FieldInfo, IShadowSlot>));
                factories.Add(type, factory); return factory;
            }
        }

        internal static void PrepareType(Type type) { Factory(type); }

        internal void CaptureReal()
        {
            if (installed) throw new InvalidOperationException("Snapshot is already installed.");
            captured = false;
            for (int i = 0; i < slots.Length; ++i) slots[i].CaptureReal();
            captured = true;
        }
        internal void InstallShadow()
        {
            if (installed || !captured) throw new InvalidOperationException("Snapshot must capture real state before installation.");
            // Set before the first store: a partially installed state must restore.
            installed = true;
            for (int i = 0; i < slots.Length; ++i) slots[i].InstallShadow();
        }
        internal void CaptureShadow()
        {
            if (!installed) throw new InvalidOperationException("Shadow state is not installed.");
            for (int i = 0; i < slots.Length; ++i) slots[i].CaptureShadow();
        }
        internal void RestoreReal()
        {
            if (!installed) return;
            Exception first = null;
            for (int i = 0; i < slots.Length; ++i)
                try { slots[i].RestoreReal(); } catch (Exception error) { if (first == null) first = error; }
            installed = false;
            captured = false;
            if (first != null) throw first;
        }
    }

    internal struct OwnerStamp : IEquatable<OwnerStamp>
    {
        internal long Owner;
        internal uint Revision;
        internal OwnerStamp(long owner, uint revision) { Owner = owner; Revision = revision; }
        public bool Equals(OwnerStamp other) { return Owner == other.Owner && Revision == other.Revision; }
    }

    // Guard predicates only. They are NOT installed Harmony prefixes. A native
    // adapter must guard every escape path, not just call these from some paths.
    internal sealed class SandboxScope
    {
        [ThreadStatic] private static SandboxScope current;
        private readonly object actor, character, view, animator;
        private readonly int unityThread;
        internal PreviewSignal Signals;

        internal SandboxScope(object ai, object body, object netView, object animation)
        {
            if (ai == null || body == null || netView == null) throw new ArgumentNullException("scope identity");
            actor = ai; character = body; view = netView; animator = animation;
            unityThread = Thread.CurrentThread.ManagedThreadId;
        }
        internal static bool Active { get { return current != null; } }
        internal bool OnThread { get { return unityThread == Thread.CurrentThread.ManagedThreadId; } }
        internal bool Enter()
        {
            if (!OnThread || current != null) return false;
            Signals = PreviewSignal.None; current = this; return true;
        }
        internal void Exit() { if (ReferenceEquals(current, this)) current = null; }
        internal static bool TryViewOwner(object netView, ref bool result)
        {
            if (current == null || !ReferenceEquals(current.view, netView)) return false;
            result = true; return true;
        }
        internal static bool BlockOwnAction(object instance, PreviewSignal signal)
        {
            SandboxScope item = current;
            if (item == null || (!ReferenceEquals(item.actor, instance) && !ReferenceEquals(item.character, instance) &&
                !ReferenceEquals(item.view, instance) && !ReferenceEquals(item.animator, instance))) return false;
            item.Signals |= signal; return true;
        }
        internal static bool BlockNetworkWrite()
        {
            if (current == null) return false;
            // Block writes even to another actor's ZDO or a shared ZRpc while the
            // synchronous speculative stack is active. Owner spoof remains local.
            current.Signals |= PreviewSignal.NetworkWriteAttempted | PreviewSignal.UnsupportedActionRequested;
            return true;
        }
        internal static bool BlockTargetNotification()
        {
            if (current == null) return false;
            current.Signals |= PreviewSignal.ExternalNotificationSuppressed; return true;
        }
    }

    internal sealed class NativeCadence
    {
        internal const float Delta = 0.05f;
        private readonly double started, lifetime;
        private readonly bool continuous;
        private double next, previous;

        internal NativeCadence(double now, double maxLifetime, bool continuous = false)
        {
            if (!Finite(now) || maxLifetime <= 0 || !Finite(maxLifetime)) throw new ArgumentOutOfRangeException("cadence");
            this.continuous = continuous;
            started = previous = next = now; lifetime = continuous ? Double.PositiveInfinity : maxLifetime;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        internal StepStatus Take(double now)
        {
            if (!Finite(now) || now < previous) return StepStatus.InvalidTime;
            previous = now;
            if (now - started >= lifetime) return StepStatus.Expired;
            if (!continuous && now < next) return StepStatus.NotDue;
            // A late caller gets one pass; there is no backlog/catch-up loop.
            next = now + 0.05d;
            return StepStatus.Produced;
        }
    }

    internal sealed class ActorBudget
    {
        private readonly object[] actors;
        internal ActorBudget(int maxActors)
        {
            if (maxActors < 1) throw new ArgumentOutOfRangeException("maxActors");
            actors = new object[maxActors];
        }
        internal bool Acquire(object actor)
        {
            if (actor == null) return false;
            int free = -1;
            for (int i = 0; i < actors.Length; ++i)
            {
                if (ReferenceEquals(actors[i], actor)) return false;
                if (actors[i] == null) free = i;
            }
            if (free < 0) return false;
            actors[free] = actor; return true;
        }
        internal void Release(object actor)
        { for (int i = 0; i < actors.Length; ++i) if (ReferenceEquals(actors[i], actor)) actors[i] = null; }
    }

    // Save/restore transaction used by fixtures and the private native steering
    // runner. The callback contract excludes Transform/Rigidbody/gameplay writes;
    // the original AI is never globally patched or given genuine ownership.
    internal sealed class ShadowTransaction<A, C, R> where A : class where C : class
    {
        private readonly ShadowSnapshot<A> ai;
        private readonly ShadowSnapshot<C> controls;
        private readonly SandboxScope scope;
        private readonly NativeCadence cadence;
        private readonly Func<R> readRandom;
        private readonly Action<R> writeRandom;
        private readonly Func<OwnerStamp> owner;
        private readonly Func<float, bool> step;
        private readonly Func<SteeringFrame> output;
        private readonly OwnerStamp expectedOwner;
        private R shadowRandom;
        private bool stopped;
        internal int Calls { get; private set; }

        internal ShadowTransaction(ShadowSnapshot<A> state, ShadowSnapshot<C> inputs, SandboxScope activeScope,
            NativeCadence clock, Func<R> getRandom, Action<R> setRandom, Func<OwnerStamp> getOwner,
            Func<float, bool> isolatedStep, Func<SteeringFrame> readOutput)
            : this(state, inputs, activeScope, clock, getRandom, setRandom, getOwner, isolatedStep, readOutput, default(R), false) { }

        internal ShadowTransaction(ShadowSnapshot<A> state, ShadowSnapshot<C> inputs, SandboxScope activeScope,
            NativeCadence clock, Func<R> getRandom, Action<R> setRandom, Func<OwnerStamp> getOwner,
            Func<float, bool> isolatedStep, Func<SteeringFrame> readOutput, R initialRandom)
            : this(state, inputs, activeScope, clock, getRandom, setRandom, getOwner, isolatedStep, readOutput, initialRandom, true) { }

        private ShadowTransaction(ShadowSnapshot<A> state, ShadowSnapshot<C> inputs, SandboxScope activeScope,
            NativeCadence clock, Func<R> getRandom, Action<R> setRandom, Func<OwnerStamp> getOwner,
            Func<float, bool> isolatedStep, Func<SteeringFrame> readOutput, R initialRandom, bool useInitialRandom)
        {
            if (state == null || inputs == null || activeScope == null || clock == null || getRandom == null ||
                setRandom == null || getOwner == null || isolatedStep == null || readOutput == null)
                throw new ArgumentNullException("transaction input");
            ai = state; controls = inputs; scope = activeScope; cadence = clock;
            readRandom = getRandom; writeRandom = setRandom; owner = getOwner; step = isolatedStep; output = readOutput;
            expectedOwner = owner(); shadowRandom = useInitialRandom ? initialRandom : readRandom();
        }

        internal StepResult TryStep(double now)
        {
            StepResult result = new StepResult();
            if (stopped) { result.Status = StepStatus.Stopped; return result; }
            if (!scope.OnThread) { result.Status = StepStatus.WrongThread; return result; }
            if (SandboxScope.Active) { result.Status = StepStatus.NestedScope; return result; }
            R realRandom = default(R);
            bool haveRandom = false, entered = false;
            try
            {
                if (!owner().Equals(expectedOwner)) { stopped = true; result.Status = StepStatus.OwnerChanged; return result; }
                result.Status = cadence.Take(now);
                if (result.Status != StepStatus.Produced)
                {
                    if (result.Status == StepStatus.Expired || result.Status == StepStatus.InvalidTime) stopped = true;
                    return result;
                }
                ai.CaptureReal(); controls.CaptureReal();
                realRandom = readRandom(); haveRandom = true;
                if (!scope.Enter()) { result.Status = StepStatus.NestedScope; return result; }
                entered = true;
                ai.InstallShadow(); controls.InstallShadow(); writeRandom(shadowRandom);
                if (!owner().Equals(expectedOwner)) { stopped = true; result.Status = StepStatus.OwnerChanged; }
                else
                {
                    Calls++; result.CallbackRan = true;
                    bool accepted = step(NativeCadence.Delta);
                    result.Signals = scope.Signals;
                    if (!owner().Equals(expectedOwner)) { stopped = true; result.Status = StepStatus.OwnerChanged; }
                    else if (!accepted || (result.Signals & (PreviewSignal.AttackRequested | PreviewSignal.UnsupportedActionRequested)) != 0)
                    { stopped = true; result.Status = StepStatus.ActionRequested; }
                    else
                    {
                        result.Frame = output(); result.Frame.NativeTime = now;
                        ai.CaptureShadow(); controls.CaptureShadow(); shadowRandom = readRandom();
                    }
                }
            }
            catch (Exception error) { stopped = true; result.Status = StepStatus.ExceptionThrown; result.Error = error; }
            finally
            {
                // Each restoration is attempted even if another fails. Scope is
                // cleared last, so restoration cannot leak network side effects.
                try { if (haveRandom) writeRandom(realRandom); }
                catch (Exception error) { stopped = true; result.Status = StepStatus.ExceptionThrown; if (result.Error == null) result.Error = error; }
                try { controls.RestoreReal(); }
                catch (Exception error) { stopped = true; result.Status = StepStatus.ExceptionThrown; if (result.Error == null) result.Error = error; }
                try { ai.RestoreReal(); }
                catch (Exception error) { stopped = true; result.Status = StepStatus.ExceptionThrown; if (result.Error == null) result.Error = error; }
                if (entered) { result.Signals = scope.Signals; scope.Exit(); }
            }
            // Validate the genuine owner again with no spoof active. A failed
            // frame never publishes its captured steering.
            if (result.Status == StepStatus.Produced)
            {
                try { if (!owner().Equals(expectedOwner)) { stopped = true; result.Status = StepStatus.OwnerChanged; } }
                catch (Exception error) { stopped = true; result.Status = StepStatus.ExceptionThrown; result.Error = error; }
            }
            if (!result.Produced) result.Frame = default(SteeringFrame);
            return result;
        }
    }
}

#endif
