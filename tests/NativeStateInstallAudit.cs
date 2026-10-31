using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;

internal static class NativeStateInstallAudit
{
    private static string[] paths;
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        foreach (string dir in paths)
        { string file = Path.Combine(dir, new AssemblyName(args.Name).Name + ".dll"); if (File.Exists(file)) return Assembly.LoadFrom(file); }
        return null;
    }
    public static void Main(string[] args)
    {
        try
        {
            paths = new[] { args[1], args[2], Path.GetDirectoryName(args[0]), Path.GetDirectoryName(args[3]) };
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            Assembly plugin = Assembly.LoadFrom(args[0]), game = Assembly.LoadFrom(Path.Combine(args[1], "assembly_valheim.dll")), fgn = Assembly.LoadFrom(args[3]);
            Type transfer = plugin.GetType("TooManyLags.NetworkSavings.StateTransfer", true);
            if (fgn.GetType("FiresGhettoNetworkMod.CompressionPolicy", false) == null)
            {
                bool retainedNative = false;
                try { transfer.GetMethod("Start", All).Invoke(null, new object[] { fgn }); }
                catch (TargetInvocationException error) { retainedNative = error.InnerException is NotSupportedException; }
                if (!retainedNative || (bool)transfer.GetProperty("Active", All).GetValue(null, null)) throw new Exception("Unknown compression contract did not retain the native path");
                foreach (MethodBase method in Harmony.GetAllPatchedMethods())
                    if (Harmony.GetPatchInfo(method).Owners.Contains("botanic.toomanylags.fgnaddon.state-transfer")) throw new Exception("Fallback left partially installed native hooks");
                Console.WriteLine("PASS: older FGN without the optional compression contract retains full native payloads and installs no optimization hooks; no numeric version gate.");
                return;
            }
            transfer.GetMethod("Start", All).Invoke(null, new object[] { fgn });
            if (!(bool)transfer.GetProperty("Active", All).GetValue(null, null)) throw new Exception("Native optimization did not activate");
            string owner = "botanic.toomanylags.fgnaddon.state-transfer";
            int targets = 0;
            foreach (MethodBase method in Harmony.GetAllPatchedMethods())
            { Patches info = Harmony.GetPatchInfo(method); if (info != null && info.Owners.Contains(owner)) targets++; }
            if (targets != 6) throw new Exception("Unexpected production patch target count: " + targets);
            Type dataType = game.GetType("ZDO", true), packageType = game.GetType("ZPackage", true), idType = game.GetType("ZDOID", true);
            game.GetType("ZDOExtraData", true).GetMethod("Init", All).Invoke(null, null);
            object data = FormatterServices.GetUninitializedObject(dataType);
            dataType.GetField("m_uid", All).SetValue(data, Activator.CreateInstance(idType, new object[] { 770000001L, 1U }));
            bool failed = false;
            try { dataType.GetMethod("Deserialize", All).Invoke(data, new[] { Activator.CreateInstance(packageType, new object[] { new byte[] { 0 } }) }); }
            catch (TargetInvocationException error) { failed = error.InnerException is EndOfStreamException; }
            object weak = transfer.GetField("invalid", All).GetValue(null); object[] lookup = { data, null };
            if (!failed || !(bool)weak.GetType().GetMethod("TryGetValue").Invoke(weak, lookup)) throw new Exception("Real native decode failure did not mark its baseline invalid");
            byte[] full;
            using (MemoryStream bytes = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(bytes))
            { writer.Write((ushort)0); writer.Write(1230001); writer.Flush(); full = bytes.ToArray(); }
            dataType.GetMethod("Deserialize", All).Invoke(data, new[] { Activator.CreateInstance(packageType, new object[] { full }) });
            lookup = new object[] { data, null };
            if ((bool)weak.GetType().GetMethod("TryGetValue").Invoke(weak, lookup)) throw new Exception("Real native successful decode did not clear its invalid marker");
            Console.WriteLine("PASS: actual patched native Deserialize under Valheim Mono marks decode failure and clears it after successful full decoding.");
            transfer.GetMethod("Stop", All).Invoke(null, null);
            if ((bool)transfer.GetProperty("Active", All).GetValue(null, null)) throw new Exception("Stop left send capability active");
            MethodInfo receive = game.GetType("ZDOMan", true).GetMethod("RPC_ZDOData", All);
            if (!Harmony.GetPatchInfo(receive).Owners.Contains(owner)) throw new Exception("Stop removed receive guard while messages may already be queued");
            transfer.GetMethod("Start", All).Invoke(null, new object[] { fgn });
            if (!(bool)transfer.GetProperty("Active", All).GetValue(null, null)) throw new Exception("Restart failed");
            new Harmony(owner).UnpatchSelf();
            Console.WriteLine("PASS: real Harmony install/JIT on native send, receive, Deserialize, forced-list, disconnect and FGN compression methods; stop retains queued-message protection; restart is idempotent.");
            Console.WriteLine("LIMIT: no Unity world or active third-party Harmony patch graph executed.");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
}
