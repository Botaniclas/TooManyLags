using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

internal static class NativeCompactCompressionTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static object Get(object x, string name) { return x.GetType().GetField(name, All).GetValue(x); }
    private static object Read(string name, params object[] args) { return typeof(NativeOwnerReceiveAudit).GetMethod(name, All).Invoke(null, args); }
    private static byte[] Packet(int count, bool full)
    {
        using (MemoryStream stream = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(stream))
        {
            int hash = (int)typeof(NativeWireRecheck).GetMethod("Hash", All).Invoke(null, new object[] { "ZDOData" });
            writer.Write(hash); writer.Write(0); writer.Write(0);
            for (uint i = 0; i < count; i++)
            {
                writer.Write(770000001L); writer.Write(i + 1); writer.Write((ushort)3); writer.Write((uint)7); writer.Write(99L);
                writer.Write((float)i); writer.Write(0f); writer.Write(0f);
                writer.Write(full ? 45 : 0);
                if (full) for (int j = 0; j < 45; j++) writer.Write((byte)(j * 7));
            }
            writer.Write(0L); writer.Write(0U); writer.Flush();
            stream.Position = 4; writer.Write((int)stream.Length - 8); writer.Flush(); return stream.ToArray();
        }
    }
    private static DynamicMethod Policy(Type bridge, MethodInfo original)
    {
        Type[] args = { original.DeclaringType, typeof(byte[]), original.GetParameters()[1].ParameterType };
        DynamicMethod copy = new DynamicMethod("ActualFGNCompactPolicy", typeof(byte[]), args, typeof(NativeCompactCompressionTests).Module, true);
        ILGenerator il = copy.GetILGenerator(); foreach (LocalVariableInfo local in original.GetMethodBody().LocalVariables) il.DeclareLocal(local.LocalType, local.IsPinned);
        IList source = (IList)Read("Read", original); var labels = new Dictionary<int, Label>(); var codes = new List<CodeInstruction>();
        foreach (object c in source) labels.Add((int)Get(c, "Offset"), il.DefineLabel());
        foreach (object c in source)
        {
            int offset = (int)Get(c, "Offset"); OpCode op = (OpCode)Get(c, "opcode"); object value = Get(c, "operand");
            if (op.OperandType == OperandType.InlineBrTarget || op.OperandType == OperandType.ShortInlineBrTarget) value = labels[(int)value];
            else if (op.OperandType == OperandType.InlineSwitch)
            { int[] offsets = (int[])value; Label[] targets = new Label[offsets.Length]; for (int i = 0; i < targets.Length; i++) targets[i] = labels[offsets[i]]; value = targets; }
            CodeInstruction code = new CodeInstruction(op, value); code.labels.Add(labels[offset]);
            foreach (ExceptionHandlingClause h in original.GetMethodBody().ExceptionHandlingClauses)
            {
                if (offset == h.HandlerOffset + h.HandlerLength) code.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
                if (offset == h.TryOffset) code.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
                if (offset == h.HandlerOffset)
                {
                    if (h.Flags != ExceptionHandlingClauseOptions.Clause) throw new NotSupportedException("Unexpected FGN compression exception region");
                    code.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, h.CatchType));
                }
            }
            codes.Add(code);
        }
        List<CodeInstruction> rewritten = (List<CodeInstruction>)bridge.GetMethod("Rewrite", All).Invoke(null, new object[] { codes });
        foreach (CodeInstruction code in rewritten)
        {
            foreach (ExceptionBlock block in code.blocks)
            {
                if (block.blockType == ExceptionBlockType.BeginExceptionBlock) il.BeginExceptionBlock();
                if (block.blockType == ExceptionBlockType.BeginCatchBlock) il.BeginCatchBlock(block.catchType);
                if (block.blockType == ExceptionBlockType.EndExceptionBlock) il.EndExceptionBlock();
            }
            foreach (Label label in code.labels) il.MarkLabel(label);
            if (code.operand is Label[]) il.Emit(code.opcode, (Label[])code.operand);
            else typeof(NativeStateTransferTests).GetMethod("Emit", All).Invoke(null, new object[] { il, code });
        }
        return copy;
    }
    internal static void Run(Assembly plugin, string filename)
    {
        Assembly fgn = Assembly.LoadFrom(filename); Type policy = fgn.GetType("FiresGhettoNetworkMod.CompressionPolicy", true);
        Type bridge = plugin.GetType("TooManyLags.NetworkSavings.CompressionBridge", true), transfer = plugin.GetType("TooManyLags.NetworkSavings.StateTransfer", true);
        MethodInfo native = policy.GetMethod("TryCompress", All); DynamicMethod scoped = Policy(bridge, native);
        int hash = (int)typeof(NativeWireRecheck).GetMethod("Hash", All).Invoke(null, new object[] { "ZDOData" });
        Type outcome = native.GetParameters()[1].ParameterType.GetElementType(); long originalBytes = 0, compactBytes = 0; int checks = 0;
        transfer.GetField("compact", All).SetValue(null, true); transfer.GetField("omitted", All).SetValue(null, true); transfer.GetField("sending", All).SetValue(null, new object());
        for (int count = 1; count <= 100; count++)
        {
            object standard = Activator.CreateInstance(policy, All, null, new object[] { hash, 0, null }, null);
            object changed = Activator.CreateInstance(policy, All, null, new object[] { hash, 0, null }, null);
            byte[] full = Packet(count, true), small = Packet(count, false);
            object[] normalArgs = { full, Enum.ToObject(outcome, 0) }, compactArgs = { changed, small, Enum.ToObject(outcome, 0) };
            byte[] normal = (byte[])native.Invoke(standard, normalArgs); byte[] encoded = (byte[])scoped.Invoke(null, compactArgs);
            originalBytes += normal == null ? full.Length : normal.Length; compactBytes += encoded == null ? small.Length : encoded.Length;
            if ((encoded == null ? small.Length : encoded.Length) > (normal == null ? full.Length : normal.Length)) throw new Exception("Compact wire increased for synthetic object count " + count);
            if (count >= 3 && count <= 11 && encoded == null) throw new Exception("Small compact batch failed to use scoped FGN compression"); checks += 2;
            if (encoded != null)
            {
                Type frame = fgn.GetType("FiresGhettoNetworkMod.PacketFrame", true); object[] decode = { encoded, null };
                object status = frame.GetMethod("TryDecode", All).Invoke(null, decode);
                if (status.ToString() != "Decoded") throw new Exception("FGN cannot decode compact frame");
                byte[] roundtrip = (byte[])decode[1]; if (roundtrip.Length != small.Length) throw new Exception("FGN frame length mismatch");
                for (int i = 0; i < small.Length; i++) if (small[i] != roundtrip[i]) throw new Exception("FGN compact bytes differ after decoding"); checks++;
            }
        }
        transfer.GetField("omitted", All).SetValue(null, false);
        if ((int)transfer.GetMethod("CompressionMinimum", All).Invoke(null, new object[] { Packet(5, false) }) != 512) throw new Exception("Unrelated/native full packets changed threshold"); checks++;
        Console.WriteLine("PASS: " + checks + " actual FGN encoder/decoder checks; 100 synthetic 1-100-object packets: " + originalBytes + " native bytes versus " + compactBytes + " compact bytes.");
        Console.WriteLine("LIMIT: synthetic 45-byte repeated state bodies; no claim about real chest/onion sizes, physical transport bytes or live CPU.");
    }
}
