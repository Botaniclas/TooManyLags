using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;

namespace TooManyLags.PlayerSimulation
{
    // Observe actual target writes, not UpdateAI/FixedUpdate. Delivery waits for
    // Unity's next synchronization-context dispatch: changing owners inside
    // UpdateTarget would interrupt unfinished AI work.
    internal static class TargetEvents
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
        private static ConditionalWeakTable<BaseAI, Pending> pending = new ConditionalWeakTable<BaseAI, Pending>();
        private static readonly SendOrPostCallback dispatch = Dispatch;
        private static readonly Dictionary<MethodBase, int> expected = new Dictionary<MethodBase, int>();
        internal static int PatchedMethods, ObservedWrites;

        internal static void Install(Harmony harmony, Func<BaseAI, bool> isWatched,
            Action<BaseAI> onChange, Action<Exception> onFailure)
        {
            if (harmony == null || isWatched == null || onChange == null || onFailure == null)
                throw new ArgumentNullException("Native target-event installation input.");
            SynchronizationContext unity = SynchronizationContext.Current;
            if (unity == null || unity.GetType().FullName != "UnityEngine.UnitySynchronizationContext")
                throw new NotSupportedException("Native target events require Unity's main-thread synchronization context.");

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

            readTarget = FieldReader.Create<MonsterAI, Character>(targetField);
            DynamicMethod setter = new DynamicMethod("TooManyLagsAssignMonsterTarget", typeof(void),
                new[] { typeof(MonsterAI), typeof(Character) }, typeof(TargetEvents).Module, true);
            ILGenerator il = setter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, targetField); il.Emit(OpCodes.Ret);
            writeTarget = (Action<MonsterAI, Character>)setter.CreateDelegate(typeof(Action<MonsterAI, Character>));
            watched = isWatched; notify = onChange; fail = onFailure; context = unity;
            Reset(); PatchedMethods = ObservedWrites = 0;

            HarmonyMethod transpiler = new HarmonyMethod(AccessTools.Method(typeof(TargetEvents), "RewriteAssignments"));
            foreach (KeyValuePair<MethodBase, int> entry in expected)
            {
                harmony.Patch(entry.Key, transpiler: transpiler);
                PatchedMethods++; ObservedWrites += entry.Value;
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
            MethodInfo replacement = AccessTools.Method(typeof(TargetEvents), "AssignTarget");
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
            Schedule(ai);
        }

        internal static void Schedule(BaseAI ai)
        {
            try
            {
                if (ReferenceEquals(ai, null) || context == null || watched == null || !watched(ai)) return;
                Pending item = pending.GetOrCreateValue(ai);
                if (item.Queued) return;
                item.Ai = ai; item.Epoch = epoch; item.Queued = true;
                try { context.Post(dispatch, item); }
                catch { item.Queued = false; throw; }
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Dispatch(object state)
        {
            Pending item = (Pending)state;
            if (item.Epoch != epoch) return;
            item.Queued = false;
            try
            {
                // The shared-area policy may have disarmed this mob since enqueueing.
                // Read its final current target in notify, never a transient store.
                if (watched != null && watched(item.Ai) && notify != null) notify(item.Ai);
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Fail(Exception error)
        {
            // Diagnostics/feature shutdown must never escape into native AI work
            // or Unity's synchronization-context dispatcher.
            try { if (fail != null) fail(error); } catch { }
        }

        internal static Character Read(MonsterAI ai) { return readTarget(ai); }
        internal static void Adopt(MonsterAI ai, Character target) { writeTarget(ai, target); }
        internal static void Reset()
        {
            unchecked { epoch++; }
            pending = new ConditionalWeakTable<BaseAI, Pending>();
        }
    }
}
