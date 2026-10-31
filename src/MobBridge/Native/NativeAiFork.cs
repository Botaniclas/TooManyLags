#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
// Isolated original-IL copier. Never patches or invokes an original game method.
// A complete, explicit policy is required before any delegate is returned.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace NativeMobPreviewAudit
{
    internal sealed class ForkPolicy
    {
        internal readonly HashSet<MethodInfo> Copies = new HashSet<MethodInfo>();
        internal readonly HashSet<MethodBase> Reads = new HashSet<MethodBase>();
        internal readonly Dictionary<MethodInfo, MethodInfo> Replacements = new Dictionary<MethodInfo, MethodInfo>();
        internal readonly Dictionary<FieldInfo, MethodInfo> FieldReads = new Dictionary<FieldInfo, MethodInfo>();
        internal readonly Dictionary<FieldInfo, MethodInfo> StaticReads = new Dictionary<FieldInfo, MethodInfo>();
        internal readonly HashSet<FieldInfo> Writes = new HashSet<FieldInfo>();
        internal readonly HashSet<MethodInfo> IndirectWrites = new HashSet<MethodInfo>();
    }

    // DynamicMethod uses skipVisibility to access original private runtime fields.
    // The copied bodies call only copies, reviewed adapters or explicit reads.
    internal sealed class NativeAiFork
    {
        private readonly ForkPolicy policy;
        private readonly Dictionary<MethodInfo, DynamicMethod> methods = new Dictionary<MethodInfo, DynamicMethod>();
        private readonly Dictionary<MethodInfo, List<CodeInstruction>> bodies = new Dictionary<MethodInfo, List<CodeInstruction>>();
        internal int MethodCount { get { return methods.Count; } }

        internal NativeAiFork(ForkPolicy specification)
        {
            if (specification == null) throw new ArgumentNullException("specification");
            policy = specification;
            // Allocate all destinations before rewriting, including recursive calls.
            foreach (MethodInfo method in policy.Copies)
            {
                if (method == null || method.ContainsGenericParameters || method.DeclaringType.IsValueType || method.ReturnType.IsByRef || method.GetMethodBody() == null)
                    throw new NotSupportedException("Unsupported fork method.");
                MethodBody body = method.GetMethodBody();
                foreach (ExceptionHandlingClause clause in body.ExceptionHandlingClauses)
                    if (clause.Flags != ExceptionHandlingClauseOptions.Finally)
                        throw new NotSupportedException("Only audited try/finally regions are supported: " + method);
                DynamicMethod copy = new DynamicMethod("TmlPreview_" + method.DeclaringType.Name + "_" + method.Name,
                    method.ReturnType, Parameters(method), typeof(NativeAiFork).Module, true);
                copy.InitLocals = body.InitLocals;
                methods.Add(method, copy);
                bodies.Add(method, PatchProcessor.GetOriginalInstructions(method, copy.GetILGenerator()));
            }
            // Validate the complete graph before emitting or exposing a delegate.
            foreach (KeyValuePair<MethodInfo, List<CodeInstruction>> body in bodies)
                foreach (CodeInstruction instruction in body.Value) Validate(body.Key, instruction);
            foreach (KeyValuePair<MethodInfo, List<CodeInstruction>> body in bodies) Emit(body.Key, body.Value);
        }

        internal T Bind<T>(MethodInfo root) where T : class
        {
            DynamicMethod method;
            if (!methods.TryGetValue(root, out method)) throw new NotSupportedException("Root not included.");
            return (T)(object)method.CreateDelegate(typeof(T));
        }

        private static Type[] Parameters(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            Type[] result = new Type[parameters.Length + (method.IsStatic ? 0 : 1)];
            int index = 0;
            if (!method.IsStatic) result[index++] = method.DeclaringType;
            foreach (ParameterInfo parameter in parameters) result[index++] = parameter.ParameterType;
            return result;
        }

        private static void Match(MethodInfo original, MethodInfo replacement)
        {
            Type[] expected = Parameters(original);
            ParameterInfo[] actual = replacement.GetParameters();
            if (!replacement.IsStatic || replacement.ReturnType != original.ReturnType || expected.Length != actual.Length)
                throw new NotSupportedException("Adapter signature mismatch: " + original);
            for (int i = 0; i < expected.Length; ++i)
                if (expected[i] != actual[i].ParameterType) throw new NotSupportedException("Adapter argument mismatch: " + original);
        }

        private void Validate(MethodInfo source, CodeInstruction instruction)
        {
            OpCode op = instruction.opcode;
            foreach (ExceptionBlock block in instruction.blocks)
                if (block.blockType != ExceptionBlockType.BeginExceptionBlock &&
                    block.blockType != ExceptionBlockType.BeginFinallyBlock &&
                    block.blockType != ExceptionBlockType.EndExceptionBlock)
                    throw new NotSupportedException("Unsupported exception region in " + source);
            if (op == OpCodes.Calli || op == OpCodes.Jmp || op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn || op == OpCodes.Localloc || op == OpCodes.Cpblk || op == OpCodes.Initblk)
                throw new NotSupportedException("Unsupported instruction in " + source + ": " + op);
            MethodBase call = instruction.operand as MethodBase;
            if (call != null)
            {
                MethodInfo target = call as MethodInfo, replacement;
                if (target != null && policy.Replacements.TryGetValue(target, out replacement)) { Match(target, replacement); return; }
                if (target != null && methods.ContainsKey(target))
                {
                    if (op == OpCodes.Callvirt && target.IsVirtual && !target.IsFinal)
                        throw new NotSupportedException("Virtual dispatch needs an explicit adapter: " + target);
                    return;
                }
                if (!policy.Reads.Contains(call)) throw new NotSupportedException("Unreviewed call in " + source + ": " + call.DeclaringType.FullName + "." + call);
            }
            FieldInfo field = instruction.operand as FieldInfo;
            MethodInfo fieldReader;
            if (op == OpCodes.Ldfld && field != null && policy.FieldReads.TryGetValue(field, out fieldReader))
            {
                if (!fieldReader.IsStatic || fieldReader.ReturnType != field.FieldType ||
                    fieldReader.GetParameters().Length != 1 || fieldReader.GetParameters()[0].ParameterType != field.DeclaringType)
                    throw new NotSupportedException("Field-read adapter mismatch: " + field);
            }
            if (op == OpCodes.Stsfld || op == OpCodes.Ldsflda)
                throw new NotSupportedException("Static mutation/address forbidden: " + source);
            if (op == OpCodes.Stfld && !policy.Writes.Contains(field))
                throw new NotSupportedException("Unreviewed field store in " + source + ": " + field);
            if (op == OpCodes.Ldsfld && field != null && policy.StaticReads.ContainsKey(field))
            {
                MethodInfo reader = policy.StaticReads[field];
                if (!reader.IsStatic || reader.ReturnType != field.FieldType || reader.GetParameters().Length != 0)
                    throw new NotSupportedException("Static-buffer adapter mismatch.");
            }
            string name = op.Name;
            if ((name.StartsWith("stind.", StringComparison.Ordinal) || name.StartsWith("stelem", StringComparison.Ordinal) || op == OpCodes.Stobj || op == OpCodes.Cpobj || op == OpCodes.Initobj) && !policy.IndirectWrites.Contains(source))
                throw new NotSupportedException("Indirect mutation requires method audit: " + source);
        }

        private void Emit(MethodInfo source, List<CodeInstruction> body)
        {
            ILGenerator il = methods[source].GetILGenerator();
            foreach (CodeInstruction instruction in body)
            {
                foreach (ExceptionBlock block in instruction.blocks)
                {
                    if (block.blockType == ExceptionBlockType.BeginExceptionBlock) il.BeginExceptionBlock();
                    else if (block.blockType == ExceptionBlockType.BeginFinallyBlock) il.BeginFinallyBlock();
                }
                foreach (Label label in instruction.labels) il.MarkLabel(label);
                OpCode op = LongBranch(instruction.opcode); object operand = instruction.operand;
                bool endsRegion = instruction.blocks.Exists(b => b.blockType == ExceptionBlockType.EndExceptionBlock);
                if (endsRegion)
                {
                    if (op != OpCodes.Endfinally) throw new NotSupportedException("Unexpected finally boundary in " + source);
                    il.EndExceptionBlock(); // Emits endfinally; do not duplicate the native terminator.
                    continue;
                }
                MethodInfo call = operand as MethodInfo, replacement;
                DynamicMethod fork;
                if (call != null && policy.Replacements.TryGetValue(call, out replacement)) { il.Emit(OpCodes.Call, replacement); continue; }
                if (call != null && methods.TryGetValue(call, out fork)) { il.Emit(OpCodes.Call, fork); continue; }
                FieldInfo field = operand as FieldInfo;
                if (op == OpCodes.Ldfld && field != null && policy.FieldReads.TryGetValue(field, out replacement))
                { il.Emit(OpCodes.Call, replacement); continue; }
                if (op == OpCodes.Ldsfld && field != null && policy.StaticReads.TryGetValue(field, out replacement)) { il.Emit(OpCodes.Call, replacement); continue; }
                if (operand == null) il.Emit(op);
                else if (operand is LocalBuilder) il.Emit(op, (LocalBuilder)operand);
                else if (operand is Label) il.Emit(op, (Label)operand);
                else if (operand is Label[]) il.Emit(op, (Label[])operand);
                else if (operand is MethodInfo) il.Emit(op, (MethodInfo)operand);
                else if (operand is ConstructorInfo) il.Emit(op, (ConstructorInfo)operand);
                else if (operand is FieldInfo) il.Emit(op, (FieldInfo)operand);
                else if (operand is Type) il.Emit(op, (Type)operand);
                else if (operand is string) il.Emit(op, (string)operand);
                else if (operand is int) il.Emit(op, (int)operand);
                else if (operand is byte) il.Emit(op, (byte)operand);
                else if (operand is sbyte) il.Emit(op, (sbyte)operand);
                else if (operand is short) il.Emit(op, (short)operand);
                else if (operand is long) il.Emit(op, (long)operand);
                else if (operand is float) il.Emit(op, (float)operand);
                else if (operand is double) il.Emit(op, (double)operand);
                else throw new NotSupportedException("Unsupported IL operand: " + operand.GetType());
            }
        }

        private static OpCode LongBranch(OpCode op)
        {
            if (op.OperandType != OperandType.ShortInlineBrTarget) return op;
            if (op == OpCodes.Br_S) return OpCodes.Br;
            if (op == OpCodes.Brfalse_S) return OpCodes.Brfalse;
            if (op == OpCodes.Brtrue_S) return OpCodes.Brtrue;
            if (op == OpCodes.Beq_S) return OpCodes.Beq;
            if (op == OpCodes.Bge_S) return OpCodes.Bge;
            if (op == OpCodes.Bge_Un_S) return OpCodes.Bge_Un;
            if (op == OpCodes.Bgt_S) return OpCodes.Bgt;
            if (op == OpCodes.Bgt_Un_S) return OpCodes.Bgt_Un;
            if (op == OpCodes.Ble_S) return OpCodes.Ble;
            if (op == OpCodes.Ble_Un_S) return OpCodes.Ble_Un;
            if (op == OpCodes.Blt_S) return OpCodes.Blt;
            if (op == OpCodes.Blt_Un_S) return OpCodes.Blt_Un;
            if (op == OpCodes.Bne_Un_S) return OpCodes.Bne_Un;
            if (op == OpCodes.Leave_S) return OpCodes.Leave;
            throw new NotSupportedException("Unknown branch.");
        }
    }
}

#endif
