using System;
using System.Collections;
using System.IO;
using System.Reflection;
using HarmonyLib;

internal static class NativeValidationProbe
{
    private static int Main(string[] args)
    {
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
            {
                string name = new AssemblyName(request.Name).Name + ".dll";
                foreach (string root in new[] { args[1], args[2] })
                { string path = Path.Combine(root, name); if (File.Exists(path)) return Assembly.LoadFrom(path); }
                return null;
            };
            Type senderType = Assembly.LoadFrom(args[0]).GetType("TooManyLagsDedicatedAddon.NativeSnapshot", true);
            senderType.GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            senderType.Assembly.GetType("TooManyLagsDedicatedAddon.MobAttackAccess", true).GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            // Native detouring invokes Unity static constructors (Animator
            // hashes/Quaternion conversions), which need a running Unity host.
            // Check actual state types here; fixture detours run separately.
            foreach (string observer in new[] { "ObjectMoved", "ShipRotationChanged", "SourceObject" })
            {
                Type contract = senderType.Assembly.GetType("TooManyLagsDedicatedAddon." + observer, true);
                ParameterInfo sourceState = Array.Find(AccessTools.Method(contract, "Prefix").GetParameters(), p => p.Name == "__state");
                ParameterInfo targetState = Array.Find(AccessTools.Method(contract, "Postfix").GetParameters(), p => p.Name == "__state");
                if (sourceState == null || targetState == null || !sourceState.IsOut || sourceState.ParameterType.GetElementType() != targetState.ParameterType)
                    throw new Exception("Native observer capture contract differs: " + observer);
            }
            Type fireType = Assembly.LoadFrom(args[3]).GetType("Valheim_Serverside.Features.FireControl", true);
            senderType.Assembly.GetType("TooManyLagsDedicatedAddon.ShipOwnership", true).GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Type shipBridge = senderType.Assembly.GetType("TooManyLagsDedicatedAddon.ForkShipOwnership", true);
            Type backendReply = fireType.Assembly.GetType("Valheim_Serverside.Features.Core+ZRoutedRpc_RouteRPC_Patch", true);
            Type processor = Assembly.Load("0Harmony").GetType("HarmonyLib.PatchProcessor", true);
            MethodInfo originalInstructions = null;
            foreach (MethodInfo method in processor.GetMethods(BindingFlags.Static | BindingFlags.Public))
                if (method.Name == "GetOriginalInstructions") { originalInstructions = method; break; }
            if (originalInstructions == null) throw new Exception("Harmony instruction reader absent.");
            var instructionArgs = new object[originalInstructions.GetParameters().Length];
            instructionArgs[0] = backendReply.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            for (int i = 1; i < instructionArgs.Length; i++) instructionArgs[i] = null;
            object shipInstructions = originalInstructions.Invoke(null, instructionArgs);
            shipBridge.GetMethod("RewriteReply", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { shipInstructions });
            Type filterType = senderType.Assembly.GetType("TooManyLagsDedicatedAddon.ForkFireOwnership", true);
            filterType.GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { fireType.GetMethod("TakeOver", BindingFlags.Static | BindingFlags.NonPublic), fireType.GetMethod("IsSource", BindingFlags.Static | BindingFlags.NonPublic) });
            Type refill = senderType.Assembly.GetType("TooManyLagsDedicatedAddon.NativeRefill", true);
            int targets = 0;
            foreach (MethodBase target in (IEnumerable)refill.GetMethod("Targets", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null))
            { if (target.DeclaringType.Assembly.GetName().Name != "assembly_valheim" || target.GetMethodBody() == null) throw new Exception("Unexpected native refill target."); targets++; }
            if (targets != 7) throw new Exception("Unexpected native refill hook count.");
            Type socket = senderType.Assembly.GetType("TooManyLagsDedicatedAddon.SocketLatency", true);
            if (!(bool)socket.GetProperty("HasSteamReader", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null, null)) throw new Exception("Real native Steam connection reader could not be bound.");
            Console.WriteLine("PASS: production startup packet/completion, native observer state-type contracts, boat impact field and backend helm rewrite, optional FireControl rewrite, seven refill targets and real Steam reader checked against installed game/released dependency. Native Unity detours remain fixture-only; no Unity world or Steam session started.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
