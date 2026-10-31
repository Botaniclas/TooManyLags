using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;

namespace TooManyLags
{
    // Observe actual target writes. Deliver after the completed AI/damage call
    // when the negotiated boundary is active; other writers use Unity's context.
    // Never dispatch an ownership change from an unfinished target-field store.
    internal static class FgnTargetEvents
    {
        private sealed class Pending
        {
            internal BaseAI Ai;
            internal long Epoch;
            internal bool Queued;
        }

        private static FieldInfo targetField;
        private static Func<MonsterAI, Character> readTarget;
        private static Action<MonsterAI, Character> writeTarget;
        private static Func<BaseAI, bool> watched;
        private static Action<BaseAI> notify;
        private static Action<Exception> fail;
        private static SynchronizationContext context;
        private static long epoch;
        private static int queuedCount;
        private static ConditionalWeakTable<BaseAI, Pending> pending = new ConditionalWeakTable<BaseAI, Pending>();
        private static readonly SendOrPostCallback dispatch = Dispatch;
        private static readonly Dictionary<MethodBase, int> expected = new Dictionary<MethodBase, int>();
#if !TML_RELEASE || TML_TEST_METRICS
        internal static int PatchedMethods, ObservedWrites;
#endif
        internal static bool HasPending { get { return queuedCount != 0; } }

        internal static void Install(Harmony harmony, Assembly fgn, Func<BaseAI, bool> isWatched,
            Action<BaseAI> onChange, Action<Exception> onFailure)
        {
            if (harmony == null || fgn == null || isWatched == null || onChange == null || onFailure == null)
                throw new ArgumentNullException("FGN target-event installation input.");
            SynchronizationContext unity = SynchronizationContext.Current;
            if (unity == null || unity.GetType().FullName != "UnityEngine.UnitySynchronizationContext")
                throw new NotSupportedException("FGN target events require Unity's main-thread synchronization context.");

            targetField = AccessTools.Field(typeof(MonsterAI), "m_targetCreature");
            if (targetField == null || targetField.IsStatic || targetField.FieldType != typeof(Character))
                throw new NotSupportedException("Native MonsterAI target field unavailable.");

            // Decode and validate every method before modifying any of them.
            expected.Clear();
            int nativeWrites = 0;
            foreach (MethodInfo method in typeof(MonsterAI).GetMethods(BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.GetMethodBody() == null || method.ContainsGenericParameters) continue;
                int count = CountAssignments(PatchProcessor.GetOriginalInstructions(method));
                if (count == 0) continue;
                expected.Add(method, count); nativeWrites += count;
            }
            if (nativeWrites == 0) throw new NotSupportedException("Native MonsterAI target writes unavailable.");

            Type targetSync = fgn.GetType("FiresGhettoNetworkMod.TargetSync", false);
            MethodInfo adopt = targetSync == null ? null : targetSync.GetMethod("AdoptOnHandover",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (adopt == null || adopt.GetMethodBody() == null ||
                CountAssignments(PatchProcessor.GetOriginalInstructions(adopt)) != 1)
                throw new NotSupportedException("FGN target-handover assignment contract changed.");
            expected.Add(adopt, 1);

            readTarget = FieldReader.Create<MonsterAI, Character>(targetField);
            DynamicMethod setter = new DynamicMethod("TooManyLagsAssignMonsterTarget", typeof(void),
                new[] { typeof(MonsterAI), typeof(Character) }, typeof(FgnTargetEvents).Module, true);
            ILGenerator il = setter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, targetField); il.Emit(OpCodes.Ret);
            writeTarget = (Action<MonsterAI, Character>)setter.CreateDelegate(typeof(Action<MonsterAI, Character>));
            watched = isWatched; notify = onChange; fail = onFailure; context = unity;
            Reset();
#if !TML_RELEASE || TML_TEST_METRICS
            PatchedMethods = ObservedWrites = 0;
#endif

            HarmonyMethod transpiler = new HarmonyMethod(AccessTools.Method(typeof(FgnTargetEvents), "RewriteAssignments"));
            foreach (KeyValuePair<MethodBase, int> entry in expected)
            {
                harmony.Patch(entry.Key, transpiler: transpiler);
#if !TML_RELEASE || TML_TEST_METRICS
                PatchedMethods++; ObservedWrites += entry.Value;
#endif
            }
        }

        private static bool IsAssignment(CodeInstruction code)
        {
            return code.opcode == OpCodes.Stfld && Equals(code.operand as FieldInfo, targetField);
        }

        private static int CountAssignments(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (CodeInstruction code in instructions) if (IsAssignment(code)) count++;
            return count;
        }

        private static IEnumerable<CodeInstruction> RewriteAssignments(IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            List<CodeInstruction> rewritten = new List<CodeInstruction>(); int count = 0;
            MethodInfo replacement = AccessTools.Method(typeof(FgnTargetEvents),
                __originalMethod.DeclaringType == typeof(MonsterAI) ? "AssignTarget" : "AssignAdoptedTarget");
            foreach (CodeInstruction code in instructions)
            {
                if (IsAssignment(code))
                {
                    // Keep branch labels and exception-block boundaries on the
                    // original instruction; the replacement has identical stack use.
                    code.opcode = OpCodes.Call; code.operand = replacement; count++;
                }
                rewritten.Add(code);
            }
            int required;
            if (!expected.TryGetValue(__originalMethod, out required) || count != required)
                throw new NotSupportedException("Monster target assignments changed while patching " + __originalMethod + ".");
            return rewritten;
        }

        private static void AssignTarget(MonsterAI ai, Character value)
        {
            Character previous = readTarget(ai);
            // Always perform the original store, including repeated assignments.
            // Everything optional runs afterwards and cannot cancel that store.
            writeTarget(ai, value);
            if (ReferenceEquals(previous, value)) return;
            // Only player acquisition/loss can change the Player/None report.
            // CLR type checks also include destroyed Unity player references.
            if (!(previous is Player) && !(value is Player)) return;
            try { FgnTargetBoundary.Changed(ai, value); }
            catch (Exception error) { Fail(error); }
            Schedule(ai);
        }

        internal static Action<BaseAI, Character> FlightAdopt = null;
        private static void AssignAdoptedTarget(MonsterAI ai, Character value)
        {
            Character previous = readTarget(ai);
            writeTarget(ai, value);
            if (FlightAdopt != null) FlightAdopt(ai, value);
            if (ReferenceEquals(previous, value) || (!(previous is Player) && !(value is Player))) return;
            // A new owner adopting the existing target is not choosing another
            // player. Report it normally without delaying its first attack.
            Schedule(ai);
        }

        internal static void Schedule(BaseAI ai)
        {
            try
            {
                if (ReferenceEquals(ai, null) || context == null || watched == null || !watched(ai)) return;
                Pending item = pending.GetOrCreateValue(ai);
                if (item.Queued) return;
                item.Ai = ai; item.Epoch = epoch; item.Queued = true; queuedCount++;
                try { context.Post(dispatch, item); }
                catch { item.Queued = false; queuedCount--; throw; }
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Dispatch(object state)
        {
            Pending item = (Pending)state;
            if (item.Epoch != epoch || !item.Queued) return;
            item.Queued = false; queuedCount--;
            try
            {
                // A two-second FGN scan may have disarmed this mob since enqueueing.
                // Read its final current target in notify, never a transient store.
                if (watched != null && watched(item.Ai) && notify != null) notify(item.Ai);
            }
            catch (Exception error) { Fail(error); }
        }

        // Only completed native AI/damage operations call this. No ownership
        // change can be dispatched from an unfinished target-field store.
        internal static void Flush(BaseAI ai)
        {
            Pending item;
            if (queuedCount == 0 || ReferenceEquals(ai, null) || !pending.TryGetValue(ai, out item) || !item.Queued) return;
            Dispatch(item);
        }

        private static void Fail(Exception error)
        {
            // Diagnostics/feature shutdown must never escape into native AI work
            // or Unity's synchronization-context dispatcher.
            try { if (fail != null) fail(error); } catch { }
        }

        internal static void Reset()
        {
            unchecked { epoch++; }
            queuedCount = 0;
            pending = new ConditionalWeakTable<BaseAI, Pending>();
        }
    }
}
