using System;
using System.IO;
using System.Reflection;

internal static class FgnOwnerHandoffContractAudit
{
    public static void Main(string[] arguments)
    {
        if (arguments.Length != 4) throw new ArgumentException("production DLL, game managed, BepInEx core, FGN DLL required");
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
        {
            string filename = new AssemblyName(request.Name).Name + ".dll";
            foreach (string folder in new[] { arguments[1], arguments[2], Path.GetDirectoryName(arguments[3]) })
            {
                string candidate = Path.Combine(folder, filename);
                if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
            }
            return null;
        };
        Assembly module = Assembly.LoadFrom(arguments[0]);
        Assembly fgn = Assembly.LoadFrom(arguments[3]);
        Type bridge = module.GetType("TooManyLags.FgnOwnerHandoff", true);
        ConstructorInfo constructor = bridge.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(Assembly), typeof(long) }, null);
        object prepared = constructor.Invoke(new object[] { fgn, 99L });
        if (prepared == null) throw new Exception("No production native binding");
        Type gate = fgn.GetType("FiresGhettoNetworkMod.FgnVersionGate", true);
        FieldInfo wire = gate.GetField("WireProtocol", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Console.WriteLine("PASS: actual FGN native handoff/IsMatched signatures bound; protocol metadata=" +
            (wire == null ? "unavailable (not a version gate)" : Convert.ToString(wire.GetRawConstantValue())) +
            "; " + Path.GetDirectoryName(arguments[3]));
    }
}
