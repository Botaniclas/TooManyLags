using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

// Runs the production rewrite of the installed native receiver. Environment
// seams: Unity clock, missing-object creation, portal registration/server role.
// Actual native ZPackage, ZDO fields/extra-data and Deserialize execute.
internal static class NativeStateTransferTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Type module, helper, worldType, dataType, pkgType, rpcType, idType, peerType;
    private static string[] paths;
    private static int checks;
    private static object Get(object x, string n) { return x.GetType().GetField(n, All).GetValue(x); }
    private static void Set(object x, string n, object v) { x.GetType().GetField(n, All).SetValue(x, v); }
    private static object H(string n, params object[] a) { return helper.GetMethod(n, All).Invoke(null, a); }
    private static object M(string n, params object[] a) { return module.GetMethod(n, All).Invoke(null, a); }
    private static object P(object x, string n) { return x.GetType().GetProperty(n, All).GetValue(x, null); }
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        foreach (string dir in paths)
        { string file = Path.Combine(dir, new AssemblyName(args.Name).Name + ".dll"); if (File.Exists(file)) return Assembly.LoadFrom(file); }
        return null;
    }
    private static object World(out object rpc, out object id, out object data, uint revision, ushort owner, bool missing = false)
    {
        object[] args = { null, null, null, revision, owner, missing }; object result = H("World", args);
        rpc = args[0]; id = args[1]; data = args[2];
        return result;
    }
    private static DynamicMethod NoPortal()
    {
        DynamicMethod m = new DynamicMethod("NoPortalEnvironment", typeof(void), new[] { worldType, dataType, typeof(int) }, typeof(NativeStateTransferTests).Module, true);
        m.GetILGenerator().Emit(OpCodes.Ret); return m;
    }
    private static DynamicMethod Decode()
    {
        DynamicMethod m = new DynamicMethod("ObservedNativeDeserialize", typeof(void), new[] { dataType, pkgType }, typeof(NativeStateTransferTests).Module, true);
        ILGenerator il = m.GetILGenerator(); LocalBuilder error = il.DeclareLocal(typeof(Exception));
        il.BeginExceptionBlock(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, dataType.GetMethod("Deserialize", All));
        il.BeginCatchBlock(typeof(Exception)); il.Emit(OpCodes.Stloc, error); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, error);
        il.Emit(OpCodes.Call, module.GetMethod("Decoded", All)); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Rethrow); il.EndExceptionBlock();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, module.GetMethod("Decoded", All)); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret); return m;
    }
    private static DynamicMethod Receiver()
    {
        MethodInfo original = worldType.GetMethod("RPC_ZDOData", All);
        DynamicMethod copy = new DynamicMethod("ActualNativeGuardedReceive", typeof(void), new[] { worldType, rpcType, pkgType }, typeof(NativeStateTransferTests).Module, true);
        ILGenerator il = copy.GetILGenerator(); foreach (LocalVariableInfo l in original.GetMethodBody().LocalVariables) il.DeclareLocal(l.LocalType, l.IsPinned);
        IList source = (IList)H("Read", original); var labels = new Dictionary<int, Label>(); var codes = new List<CodeInstruction>();
        foreach (object c in source) labels.Add((int)Get(c, "Offset"), il.DefineLabel());
        foreach (object c in source)
        {
            OpCode op = (OpCode)Get(c, "opcode"); object value = Get(c, "operand");
            if (op.OperandType == OperandType.InlineBrTarget || op.OperandType == OperandType.ShortInlineBrTarget) value = labels[(int)value];
            CodeInstruction code = new CodeInstruction(op, value); code.labels.Add(labels[(int)Get(c, "Offset")]); codes.Add(code);
        }
        List<CodeInstruction> rewritten = (List<CodeInstruction>)M("RewriteReceive", codes, il);
        Check(rewritten.Count == codes.Count + 7, "receiver adds one local guard at the existing native object lookup");
        DynamicMethod observed = Decode(); int clock = 0, create = 0, portal = 0, role = 0, decode = 0;
        foreach (CodeInstruction code in rewritten)
        {
            foreach (Label label in code.labels) il.MarkLabel(label);
            MethodInfo called = code.operand as MethodInfo;
            if (called != null && called.DeclaringType.FullName == "UnityEngine.Time" && called.Name == "get_time")
            { il.Emit(OpCodes.Call, typeof(NativeStateTransferTests).GetMethod("Clock", All)); clock++; continue; }
            if (called != null && called.DeclaringType == worldType && called.Name == "CreateNewZDO")
            {
                var signature = new List<Type> { worldType }; foreach (ParameterInfo p in called.GetParameters()) signature.Add(p.ParameterType);
                DynamicMethod missing = new DynamicMethod("MissingObjectEnvironment", dataType, signature.ToArray(), typeof(NativeStateTransferTests).Module, true);
                ILGenerator mi = missing.GetILGenerator(); mi.Emit(OpCodes.Ldstr, "native needs CreateNewZDO"); mi.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })); mi.Emit(OpCodes.Throw);
                il.Emit(OpCodes.Call, missing); create++; continue;
            }
            if (called != null && called.DeclaringType == worldType && called.Name == "AddIfPortal")
            { il.Emit(OpCodes.Call, NoPortal()); portal++; continue; }
            if (called != null && called.DeclaringType.FullName == "ZNet" && called.Name == "IsServer")
            { il.Emit(OpCodes.Call, (DynamicMethod)H("Stub", called, true)); role++; continue; }
            if (called != null && called.DeclaringType == dataType && called.Name == "Deserialize")
            { il.Emit(OpCodes.Call, observed); decode++; continue; }
            Emit(il, code);
        }
        Check(clock == 1 && create == 1 && portal == 1 && role == 1 && decode == 1, "only explicit environment seams and real Deserialize observation substituted");
        return copy;
    }
    private static float Clock() { return 123; }
    private static void Emit(ILGenerator il, CodeInstruction c)
    {
        OpCode op = c.opcode; object value = c.operand;
        // Inserted guard can lengthen original short branches.
        if (op.OperandType == OperandType.ShortInlineBrTarget)
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            { OpCode longer = (OpCode)field.GetValue(null); if (longer.Name == op.Name.Substring(0, op.Name.Length - 2)) { op = longer; break; } }
        if (value != null && op.OperandType == OperandType.InlineVar && !(value is LocalBuilder)) il.Emit(op, Convert.ToInt16(value));
        else if (value == null) il.Emit(op);
        else if (value is Label) il.Emit(op, (Label)value);
        else if (value is LocalBuilder) il.Emit(op, (LocalBuilder)value);
        else if (value is MethodInfo) il.Emit(op, (MethodInfo)value);
        else if (value is ConstructorInfo) il.Emit(op, (ConstructorInfo)value);
        else if (value is FieldInfo) il.Emit(op, (FieldInfo)value);
        else if (value is Type) il.Emit(op, (Type)value);
        else if (value is string) il.Emit(op, (string)value);
        else if (value is byte) il.Emit(op, (byte)value);
        else if (value is sbyte) il.Emit(op, (sbyte)value);
        else if (value is short) il.Emit(op, (short)value);
        else if (value is int) il.Emit(op, (int)value);
        else if (value is float) il.Emit(op, (float)value);
        else throw new NotSupportedException("Emitter operand " + value.GetType());
    }
    private static Exception Run(DynamicMethod receiver, object world, object rpc, uint rev, ushort own, byte[] body)
    {
        byte[] bytes = (byte[])H("Packet", rev, own, body, 99L);
        object packet = Activator.CreateInstance(pkgType, new object[] { bytes }); object[] enter = { rpc, null }; M("EnterReceive", enter);
        Exception failure = null;
        try { receiver.Invoke(null, new[] { world, rpc, packet }); }
        catch (TargetInvocationException error) { failure = error.InnerException; }
        M("LeaveReceive", failure, enter[1]); return failure;
    }
    private static void Ledger(object peer, object id, uint revision, ushort owner)
    {
        IDictionary entries = (IDictionary)Get(peer, "m_zdos"); Type info = entries.GetType().GetGenericArguments()[1];
        entries[id] = Activator.CreateInstance(info, new object[] { revision, owner, 123f });
    }
    private static int Body(object data, object peer, bool enabled)
    {
        object previous = module.GetField("sending", All).GetValue(null);
        bool previousCompact = (bool)module.GetField("compact", All).GetValue(null);
        bool previousOmitted = (bool)module.GetField("omitted", All).GetValue(null);
        module.GetField("sending", All).SetValue(null, peer); module.GetField("compact", All).SetValue(null, enabled);
        object body = Activator.CreateInstance(pkgType);
        try { M("Serialize", data, body); }
        catch (TargetInvocationException error)
        {
            // The full serializer uses Unity's native quaternion conversion.
            // Reaching that original method verifies fallback selection, not
            // a headless serialization size or execution of Unity internals.
            if (error.InnerException is System.Security.SecurityException && error.InnerException.StackTrace.Contains("ZDO.Serialize")) return Int32.MaxValue;
            throw;
        }
        finally
        {
            module.GetField("sending", All).SetValue(null, previous); module.GetField("compact", All).SetValue(null, previousCompact);
            module.GetField("omitted", All).SetValue(null, previousOmitted);
        }
        return (int)pkgType.GetMethod("Size").Invoke(body, null);
    }
    public static void Main(string[] args)
    {
        try
        {
            paths = new[] { args[1], args[2], Path.GetDirectoryName(args[0]) }; AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            Assembly game = Assembly.LoadFrom(Path.Combine(args[1], "assembly_valheim.dll")); Assembly plugin = Assembly.LoadFrom(args[0]);
            module = plugin.GetType("TooManyLags.NetworkSavings.StateTransfer", true); helper = typeof(NativeOwnerReceiveAudit);
            worldType = game.GetType("ZDOMan", true); dataType = game.GetType("ZDO", true); pkgType = game.GetType("ZPackage", true);
            rpcType = game.GetType("ZRpc", true); idType = game.GetType("ZDOID", true); peerType = worldType.GetNestedType("ZDOPeer", All);
            foreach (var item in new[] { new KeyValuePair<string, Type>("worldType", worldType), new KeyValuePair<string, Type>("dataType", dataType), new KeyValuePair<string, Type>("idType", idType), new KeyValuePair<string, Type>("rpcType", rpcType), new KeyValuePair<string, Type>("pkgType", pkgType), new KeyValuePair<string, Type>("peerType", peerType) }) helper.GetField(item.Key, All).SetValue(null, item.Value);
            helper.GetField("game", All).SetValue(null, game); game.GetType("ZDOExtraData", true).GetMethod("Init", All).Invoke(null, null);
            M("Bind"); FieldInfo portal = module.GetField("addPortal", All); portal.SetValue(null, NoPortal().CreateDelegate(portal.FieldType));
            MethodInfo send = worldType.GetMethod("SendZDOs", All);
            var originalSend = new List<CodeInstruction>();
            foreach (object code in (IList)H("Read", send)) originalSend.Add(new CodeInstruction((OpCode)Get(code, "opcode"), Get(code, "operand")));
            List<CodeInstruction> sent = (List<CodeInstruction>)M("RewriteSend", originalSend);
            int wrappers = 0; foreach (CodeInstruction c in sent) if (c.operand is MethodInfo && ((MethodInfo)c.operand).DeclaringType == module) wrappers++;
            Check(wrappers == 2, "production sender rewrites one serializer and adds stable native-list filtering");
            DynamicMethod receiver = Receiver(); object rpc, id, data; object world = World(out rpc, out id, out data, 6, 1);
            byte[] baseline = (byte[])H("Baseline");
            Exception initial = Run(receiver, world, rpc, 7, 2, baseline); if (initial != null) throw new Exception("Initial native receive failed", initial);
            Check((float)H("Health", data) == 13f, "real full state is installed by original native Deserialize");
            for (int i = 0; i < 1000; i++)
            {
                Check(Run(receiver, world, rpc, 7, (ushort)(i + 3), new byte[0]) == null, "ordered owner-only record succeeds");
                Check((float)H("Health", data) == 13f && (long)dataType.GetMethod("GetOwner").Invoke(data, null) == 99, "owner-only retains actual native field data");
            }
            Check(((IList)module.GetField("repairs", All).GetValue(null)).Count == 0 && !(bool)module.GetField("hasInvalid", All).GetValue(null), "normal path creates no repair work or invalid-baseline entries");
            world = World(out rpc, out id, out data, 6, 1);
            Check(Run(receiver, world, rpc, 7, 2, new byte[0]) == null && (uint)P(data, "DataRevision") == 6 && (ushort)P(data, "OwnerRevision") == 1, "missing newer state rejected BEFORE any native tuple mutation");
            Check(Run(receiver, world, rpc, 7, 2, baseline) == null && (float)H("Health", data) == 13, "full retry remains decodable after rejected compact record");
            world = World(out rpc, out id, out data, 7, 1, true);
            Check(Run(receiver, world, rpc, 7, 2, new byte[0]) == null, "absent owner-only object is dropped before native creation");
            world = World(out rpc, out id, out data, 6, 1);
            Check(Run(receiver, world, rpc, 7, 2, new byte[] { 0 }) is EndOfStreamException, "actual malformed full-state Deserialize still reports native error");
            Check(Run(receiver, world, rpc, 7, 3, new byte[0]) == null && (ushort)P(data, "OwnerRevision") == 2, "known failed equal-revision state cannot accept compact ownership");
            Check(Run(receiver, world, rpc, 6, 3, baseline) == null && (ushort)P(data, "OwnerRevision") == 2, "stale full retry cannot authorize ownership while a newer decode is known invalid");
            Check(Run(receiver, world, rpc, 7, 3, baseline) == null && (float)H("Health", data) == 13 && (ushort)P(data, "OwnerRevision") == 3, "equal-revision full repair restores native data before ownership advances");
            world = World(out rpc, out id, out data, 8, 10);
            Check(Run(receiver, world, rpc, 7, 9, new byte[0]) == null && (ushort)P(data, "OwnerRevision") == 10 && (uint)P(data, "DataRevision") == 8, "native stale-owner/newer-local-state behavior unchanged");
            object peer = ((IList)Get(world, "m_peers"))[0]; Ledger(peer, id, 8, 9);
            Check(Body(data, peer, true) == 0, "unchanged state omits the actual native serialized body");
            Check(Body(data, peer, false) > 0, "legacy peer uses actual native full serializer");
            Ledger(peer, id, 7, 10); Check(Body(data, peer, true) > 0, "changed state retains native full serializer");
            ((IDictionary)Get(peer, "m_zdos")).Clear(); Check(Body(data, peer, true) > 0, "unknown recipient starts with full native baseline");
            Ledger(peer, id, 8, 10); M("Decoded", data, new EndOfStreamException()); Check(Body(data, peer, true) > 0, "invalid local baseline is never compacted");
            M("Decoded", data, null); Check(Body(data, peer, true) == 0, "successful decoding restores compact eligibility");
            Type wire = typeof(NativeWireRecheck); wire.GetField("game", All).SetValue(null, game); wire.GetField("pkgType", All).SetValue(null, pkgType);
            object socket = wire.GetMethod("Socket", All).Invoke(null, null);
            Set(rpc, "m_socket", socket); Set(rpc, "m_pkg", Activator.CreateInstance(pkgType));
            Set(rpc, "m_functions", Activator.CreateInstance(rpcType.GetField("m_functions", All).FieldType));
            module.GetField("<Active>k__BackingField", All).SetValue(null, true);
            M("Connect", Get(peer, "m_peer")); M("Enable", rpc, true);
            int before = (int)wire.GetField("captures", All).GetValue(null);
            module.GetField("receiving", All).SetValue(null, rpc);
            for (uint i = 1; i <= 300; i++) M("NeedRepair", rpc, Activator.CreateInstance(idType, new object[] { 770000001L, i }));
            M("LeaveReceive", null, null);
            Check((int)wire.GetField("captures", All).GetValue(null) - before == 3, "300 actual repair IDs form three bounded native RPC envelopes without dropping overflow");
            byte[] repair = (byte[])wire.GetField("captured", All).GetValue(null);
            using (BinaryReader reader = new BinaryReader(new MemoryStream(repair)))
            {
                reader.ReadInt32(); Check(reader.ReadInt32() == repair.Length - 8 && reader.ReadInt32() == 44, "actual native repair RPC carries the final 44 IDs");
            }
            object request = Activator.CreateInstance(pkgType); pkgType.GetMethod("Write", new[] { typeof(int) }).Invoke(request, new object[] { 1 });
            pkgType.GetMethod("Write", new[] { idType }).Invoke(request, new[] { id }); pkgType.GetMethod("SetPos").Invoke(request, new object[] { 0 });
            Type network = game.GetType("ZNet", true);
            foreach (FieldInfo field in network.GetFields(All)) if (field.IsStatic && field.FieldType == network) field.SetValue(null, H("New", network));
            M("Repair", rpc, request);
            Check(!((IDictionary)Get(peer, "m_zdos")).Contains(id), "actual repair clears the optimistic peer entry so native sends full data");
            Check((bool)Get(peer, "m_forceSend").GetType().GetMethod("Contains").Invoke(Get(peer, "m_forceSend"), new[] { id }), "actual repair marks exactly the requested native object for send");
            Check(Body(data, peer, true) != 0, "repair cannot be compacted again because the native baseline entry is absent");
            before = (int)wire.GetField("captures", All).GetValue(null);
            for (int i = 0; i < 100; i++) Check(Run(receiver, world, rpc, 8, (ushort)(20 + i), new byte[0]) == null, "normal negotiated compact update succeeds");
            Check((int)wire.GetField("captures", All).GetValue(null) == before, "successful negotiated ownership updates issue ZERO additional RPC confirmations");
            M("Decoded", data, new EndOfStreamException());
            Check(Run(receiver, world, rpc, 8, 120, new byte[] { 0 }) == null, "unreadable full repair is discarded safely");
            Check(Run(receiver, world, rpc, 7, 120, baseline) == null, "stale full repair is discarded safely");
            Check((int)wire.GetField("captures", All).GetValue(null) == before, "bad full repairs cannot create an automatic request/reply loop");
            M("Decoded", data, null);
            Ledger(peer, id, 8, 10); IList retryList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(dataType)); retryList.Add(data); Set(world, "m_tempToSync", retryList);
            object[] sendScope = { peer, null }; M("EnterSend", sendScope);
            Check((bool)module.GetField("compact", All).GetValue(null), "negotiated peer enables compact native send context");
            object[] nestedScope = { peer, null }; M("EnterSend", nestedScope);
            Check(!(bool)module.GetField("compact", All).GetValue(null), "nested send uses full state instead of borrowing the outer context");
            M("LeaveSend", null, nestedScope[1], peer, world);
            Check((bool)module.GetField("compact", All).GetValue(null), "nested completion restores the outer send context");
            Exception uncertain = new InvalidOperationException("synthetic native send failure");
            Check(ReferenceEquals(M("LeaveSend", uncertain, sendScope[1], peer, world), uncertain), "send recovery preserves the original exception");
            Check(((IDictionary)Get(peer, "m_zdos")).Count == 0, "failed compact send cannot retain optimistic sent-state entries");
            object link = ((IDictionary)module.GetField("links", All).GetValue(null))[rpc];
            Check((bool)Get(link, "SendFailed") && (bool)Get(link, "Supported"), "uncertain send disables compact output while retaining negotiated receive/repair support");
            object[] afterFailure = { peer, null }; M("EnterSend", afterFailure);
            Check(!(bool)module.GetField("compact", All).GetValue(null), "subsequent output safely falls back to complete native state"); M("LeaveSend", null, afterFailure[1], peer, world);
            Ledger(peer, id, 8, 10); M("Enable", rpc, false); object[] legacyScope = { peer, null }; M("EnterSend", legacyScope);
            M("LeaveSend", uncertain, legacyScope[1], peer, world);
            Check(((IDictionary)Get(peer, "m_zdos")).Contains(id), "legacy-only send failures keep unmodified native bookkeeping");
            M("Disconnected", Get(peer, "m_peer"));
            Check(((IDictionary)module.GetField("links", All).GetValue(null)).Count == 0, "disconnect immediately releases the native transport reference");
            Type listType = typeof(List<>).MakeGenericType(dataType); IList items = (IList)Activator.CreateInstance(listType);
            object other = H("New", dataType); for (int i = 0; i < 100; i++) { items.Add(data); items.Add(other); }
            module.GetField("forceAdded", All).SetValue(null, true); M("Unique", items);
            Check(items.Count == 2 && ReferenceEquals(items[0], data) && ReferenceEquals(items[1], other), "same-reference duplicates removed while keeping first/forced order");
            Check((int)P(module.GetField("distinct", All).GetValue(null), "Count") == 0, "send scratch releases every object reference");
            M("Reset"); Check(((IDictionary)module.GetField("links", All).GetValue(null)).Count == 0 && !(bool)module.GetField("hasInvalid", All).GetValue(null), "world reset clears transport/error state");
            if (args.Length > 3) NativeCompactCompressionTests.Run(plugin, args[3]);
            Console.WriteLine("PASS: " + checks + " production compact-state/native-receiver assertions.");
            Console.WriteLine("LIMIT: native managed execution with declared Unity seams; no live multiplayer/combined Harmony transport benchmark.");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
}
