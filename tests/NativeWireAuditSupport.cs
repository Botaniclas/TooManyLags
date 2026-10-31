using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

// Runs installed native ZRpc/ZPackage serialization. Only ISocket is a capture.
// Does not emulate handlers, Steam/PlayFab or ownership gameplay.
public static class NativeWireRecheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static string[] paths;
    private static Assembly game;
    private static Type pkgType, idType, rpcType;
    private static object rpc;
    private static byte[] captured;
    private static int checks, captures;
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        foreach (string dir in paths)
        {
            string path = Path.Combine(dir, new AssemblyName(args.Name).Name + ".dll");
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }
    public static void Capture(object pkg)
    { captured = (byte[])pkgType.GetMethod("GetArray").Invoke(pkg, null); captures++; }
    private static object Socket()
    {
        Type contract = game.GetType("ISocket", true);
        var assembly = (AssemblyBuilder)typeof(AssemblyBuilder).GetMethod("DefineDynamicAssembly", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(AssemblyName), typeof(AssemblyBuilderAccess) }, null).Invoke(null,
            new object[] { new AssemblyName("NativeWireCapture"), AssemblyBuilderAccess.Run });
        TypeBuilder type = assembly.DefineDynamicModule("Main").DefineType("CapturedSocket", TypeAttributes.Public);
        type.AddInterfaceImplementation(contract);
        var interfaces = new List<Type> { contract }; interfaces.AddRange(contract.GetInterfaces());
        var names = new HashSet<string>();
        foreach (Type current in interfaces) foreach (MethodInfo member in current.GetMethods())
        {
            if (!names.Add(member.ToString())) continue;
            var args = new List<Type>(); foreach (ParameterInfo arg in member.GetParameters()) args.Add(arg.ParameterType);
            MethodBuilder method = type.DefineMethod(member.Name, MethodAttributes.Public | MethodAttributes.Virtual |
                MethodAttributes.Final | MethodAttributes.NewSlot, member.ReturnType, args.ToArray());
            ILGenerator il = method.GetILGenerator();
            for (int i = 0; i < args.Count; i++) if (args[i].IsByRef)
            { il.Emit(OpCodes.Ldarg, i + 1); il.Emit(OpCodes.Initobj, args[i].GetElementType()); }
            if (member.Name == "Send" && args.Count == 1 && args[0] == pkgType)
            { il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, typeof(NativeWireRecheck).GetMethod("Capture")); }
            if (member.Name == "IsConnected" && member.ReturnType == typeof(bool)) il.Emit(OpCodes.Ldc_I4_1);
            else if (member.ReturnType != typeof(void))
            {
                if (member.ReturnType.IsValueType)
                {
                    LocalBuilder local = il.DeclareLocal(member.ReturnType);
                    il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Initobj, member.ReturnType); il.Emit(OpCodes.Ldloc, local);
                }
                else il.Emit(OpCodes.Ldnull);
            }
            il.Emit(OpCodes.Ret); type.DefineMethodOverride(method, member);
        }
        return Activator.CreateInstance(type.CreateType());
    }
    private static object Package() { return Activator.CreateInstance(pkgType); }
    private static object Id(int i) { return Activator.CreateInstance(idType, new object[] { 770000001L, (uint)(i + 10001) }); }
    private static void Write(object pkg, object value)
    { pkgType.GetMethod("Write", new[] { value.GetType() }).Invoke(pkg, new[] { value }); }
    private static byte[] Send(string name, params object[] args)
    {
        captured = null; rpcType.GetMethod("Invoke").Invoke(rpc, new object[] { name, args });
        if (captured == null) throw new Exception("Actual native Invoke did not reach captured Send");
        return captured;
    }
    private static object Control(int i, int kind, bool fused)
    {
        object p = Package(); Write(p, kind); Write(p, (long)i + 1); Write(p, Id(i));
        if (kind == 2) { Write(p, 990000003L); Write(p, (uint)1); }
        else
        {
            Write(p, true); Write(p, true); Write(p, 990000003L);
            Write(p, (uint)2); Write(p, (uint)7); Write(p, false);
            if (fused) { Write(p, true); Write(p, true); Write(p, ""); }
        }
        return p;
    }
    private static List<byte[]> Controls(int n, bool batch, bool fused, bool stateSuccess)
    {
        var packets = new List<byte[]>();
        foreach (int kind in new[] { 2, 4 })
        {
            if (stateSuccess && kind == 4) continue;
            int step = batch ? 32 : 1;
            for (int start = 0; start < n; start += step)
            {
                int count = Math.Min(step, n - start); object p;
                if (count == 1) p = Control(start, kind, fused && kind == 4);
                else
                {
                    p = Package(); Write(p, 11); Write(p, count);
                    for (int i = start; i < start + count; i++) Write(p, Control(i, kind, fused && kind == 4));
                }
                packets.Add(Send("TooManyLags_GridHandoff_1", p));
            }
        }
        if (!fused && !stateSuccess) for (int i = 0; i < n; i++)
            packets.Add(Send("FGN_HandOffResult", Id(i), true, ""));
        return packets;
    }
    private static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    private static void Check(bool ok, string label)
    { checks++; if (!ok) throw new Exception(label); }
    private static int Hash(string value)
    {
        unchecked
        {
            int a = 5381, b = a;
            for (int i = 0; i < value.Length && value[i] != 0; i += 2)
            {
                a = ((a << 5) + a) ^ value[i];
                if (i == value.Length - 1 || value[i + 1] == 0) break;
                b = ((b << 5) + b) ^ value[i + 1];
            }
            return a + b * 1566083941;
        }
    }
    private static byte[] Routed(string name, object argument)
    {
        Type routed = game.GetType("ZRoutedRpc+RoutedRPCData", true);
        object data = Activator.CreateInstance(routed); object parameters = routed.GetField("m_parameters").GetValue(data);
        Write(parameters, argument);
        routed.GetField("m_msgID").SetValue(data, 3L);
        routed.GetField("m_senderPeerID").SetValue(data, 990000003L);
        routed.GetField("m_targetPeerID").SetValue(data, 880000004L);
        routed.GetField("m_targetZDO").SetValue(data, Id(0));
        routed.GetField("m_methodHash").SetValue(data, Hash(name));
        object p = Package(); routed.GetMethod("Serialize").Invoke(data, new[] { p });
        return Send("RoutedRPC", p);
    }
    public static void Main(string[] args)
    {
        paths = new[] { args[0], args[1] }; AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        game = Assembly.LoadFrom(Path.Combine(args[0], "assembly_valheim.dll"));
        pkgType = game.GetType("ZPackage", true); idType = game.GetType("ZDOID", true); rpcType = game.GetType("ZRpc", true);
        game.GetType("ZDOExtraData", true).GetMethod("Init", All).Invoke(null, null);
        rpcType.GetField("m_DEBUG", All).SetValue(null, false);
        rpc = Activator.CreateInstance(rpcType, new[] { Socket() });
        Assembly model = Assembly.LoadFrom(args[2]);
        MethodInfo expected = model.GetType("BandwidthOptionsAudit", true).GetMethod("Controls", All);
        foreach (int n in new[] { 1, 5, 10, 20, 30, 100 }) foreach (string variant in new[] { "single", "batch", "fused", "state" })
        {
            bool batch = variant != "single", fused = variant == "fused", state = variant == "state";
            List<byte[]> actual = Controls(n, batch, fused, state);
            var reference = (List<byte[]>)expected.Invoke(null, new object[] { n, batch, fused, state });
            Check(actual.Count == reference.Count, "Message count differs from native layout");
            for (int i = 0; i < actual.Count; i++) Check(Equal(actual[i], reference[i]), "Native packet differs: " + variant + " " + n + " " + i);
        }
        Check(Send("TooManyLags_GridHandoff_1", Control(0, 2, false)).Length == 44, "Native Commit byte count");
        Check(Send("TooManyLags_GridHandoff_1", Control(0, 4, false)).Length == 51, "Native TML Result byte count");
        Check(Send("FGN_HandOffResult", Id(0), true, "").Length == 18, "Native FGN Result byte count");
        int open = Routed("RPC_RequestOpen", 0L).Length;
        int response = Routed("RPC_OpenResponse", true).Length;
        Console.WriteLine("Native routed RequestOpen=" + open + " B; OpenResponse=" + response + " B; no transport framing.");
        Check(open == 60 && response == 53, "Routed inventory request/response byte count");
        Console.WriteLine("PASS: " + checks + " native wire assertions; " + captures + " actual native ZRpc.Invoke captures.");
        Console.WriteLine("LIMIT: No FGN/TML handler execution, live sockets, handshake correctness or gameplay tested. Proposed layouts remain proposals.");
    }
}
