using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Inspect shipped IL: release removes diagnostics and their argument evaluation;
// debug retains test logging while inactive tests still return before work.
internal static class DebugIdleAudit
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
    { foreach (var type in types) { yield return type; foreach (var nested in Flatten(type.NestedTypes)) yield return nested; } }
    private static IEnumerable<MethodReference> Calls(MethodDefinition method)
    { return method.HasBody ? method.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand) : Enumerable.Empty<MethodReference>(); }
    private static bool Routine(MethodReference call)
    {
        return call.DeclaringType.FullName == "TooManyLags.MobBridgeCost" ||
            call.DeclaringType.FullName == "TooManyLags.Plugin" && call.Name == "TestLog" ||
            call.DeclaringType.FullName == "TooManyLags.MobBridgeProtocol" && call.Name == "Report" ||
            call.DeclaringType.FullName == "TooManyLags.MobBridgeVisual" && call.Name == "MeasureDraw" ||
            new[]{"LogInfo", "LogDebug", "LogMessage"}.Contains(call.Name) && call.DeclaringType.Namespace.StartsWith("BepInEx", StringComparison.Ordinal);
    }
    private static void IdleGuard(MethodDefinition method, string field)
    {
        var code = method.Body.Instructions;
        int read = code.ToList().FindIndex(i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference && ((FieldReference)i.Operand).Name == field);
        int ret = code.ToList().FindIndex(i => i.OpCode == OpCodes.Ret);
        int work = code.ToList().FindIndex(i => i.Operand is MethodReference || i.OpCode == OpCodes.Newobj);
        Check(read >= 0 && read < ret && (work < 0 || ret < work), method.FullName + " performs work before its inactive return");
        Check(code.Take(ret).All(i => i.OpCode != OpCodes.Stsfld && i.OpCode != OpCodes.Stfld), method.FullName + " writes idle state");
    }
    internal static void Run(AssemblyDefinition image)
    {
        var types = Flatten(image.MainModule.Types).ToDictionary(t => t.FullName);
        var methods = types.Values.SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
        foreach (var method in methods)
        {
            Check(!Calls(method).Any(Routine), "Routine logging/profiling remains in " + method.FullName);
            if (method.DeclaringType.FullName.StartsWith("TooManyLags.MobBridge", StringComparison.Ordinal))
                Check(!Calls(method).Any(c => c.DeclaringType.FullName == "System.Diagnostics.Stopwatch"), "Bridge profiling timer remains in " + method.FullName);
        }
        foreach (string name in new[]{"TooManyLags.FgnTargetBoundary", "TooManyLags.FgnBossPolicy", "TooManyLags.FgnTargetEvents", "TooManyLags.PlayerSimulation.TargetEvents"})
            Check(!types[name].Fields.Any(f => new[]{"Changes", "HeldStarts", "ExpiredHolds", "Evaluations", "PatchedMethods", "ObservedWrites"}.Contains(f.Name)), "Idle metric fields remain in " + name);
        var cost = types["TooManyLags.MobBridgeCost"];
        Check(cost.Fields.Count == 0 && cost.Methods.All(m => !Calls(m).Any(c => c.DeclaringType.FullName != "System.Object")), "Cost reporting still retains state or work");
        var visual = types["TooManyLags.MobBridgeVisual"];
        var track = visual.NestedTypes.Single(t => t.Name == "Track");
        Check(!track.Fields.Any(f => new[]{"CaptureTicks", "AiTicks", "MotorTicks", "MaxDivergence", "RootAngle", "MotionSamples", "StarvedFrames", "EndpointFrames", "BufferSlowFrames"}.Contains(f.Name)), "Per-frame diagnostic state remains");
        Check(!types["TooManyLags.MobBridgeAnimation"].Fields.Any(f => f.Name == "rootBone"), "Animation still probes root bone only for logging");
        foreach (string pair in new[]{"TooManyLags.Plugin:TestLog", "TooManyLags.MobBridgeProtocol:Report", "TooManyLags.MobBridgeProtocol:ReceiveDiagnostic", "TooManyLags.MobBridgeVisual:MeasureDraw"})
        {
            string[] names = pair.Split(':');
            Check(types[names[0]].Methods.Single(m => m.Name == names[1]).Body.Instructions.All(i => i.OpCode == OpCodes.Ret || i.OpCode == OpCodes.Nop), "Disabled sink does work: " + pair);
        }
        bool debug = types.ContainsKey("TooManyLags.FgnSoloCombatTest");
        if (debug)
        {
            IdleGuard(types["TooManyLags.FgnSoloCombatTest"].Methods.Single(m => m.Name == "Poll"), "session");
            IdleGuard(types["TooManyLags.SoloNetworkLatency"].Methods.Single(m => m.Name == "Poll"), "state");
            var after = types["TooManyLags.MobCollisionReadiness"].Methods.Single(m => m.Name == "AfterMob");
            Check(Calls(after).First().Name == "get_Active" && after.Body.Instructions.TakeWhile(i => i.OpCode != OpCodes.Ret).Count(i => i.Operand is MethodReference) == 1,
                "PUP diagnostic postfix touches Unity before active-test guard");
            Check(Calls(types["TooManyLags.FgnSoloCombatTest"].Methods.Single(m => m.Name == "Message")).Any(c => c.Name == "Invoke"), "Explicit test command no longer replies to requesting console");
            Check(Calls(types["TooManyLags.SoloNetworkLatency"].Methods.Single(m => m.Name == "Bound")).Any(c => c.Name == "Warn"), "Artificial-latency buffer failure became silent");
        }
        else Check(!types.ContainsKey("TooManyLags.SoloNetworkLatency") && !types["TooManyLags.MobCollisionReadiness"].Methods.Any(m => m.Name == "AfterMob"), "Release retains idle test features");
        var protocol = types["TooManyLags.MobBridgeProtocol"];
        Check(Calls(protocol.Methods.Single(m => m.Name == "PollRate")).Any(c => c.Name == "Invoke"), "Required pre-handoff simulation rate cache disabled");
        Check(Calls(protocol.Methods.Single(m => m.Name == "PollRate")).Any(c => c.Name == "Connected") &&
            Calls(protocol.Methods.Single(m => m.Name == "PollRate")).TakeWhile(c => c.Name != "Connected").All(c => c.Name != "Capable"),
            "Observer capability retry incorrectly depends on main bridge readiness");
        Check(Calls(types["TooManyLags.MobBridgeAnimation"].Methods.Single(m => m.Name == "Start")).Any(c => c.Name == "Warn"), "Missing native visual attack became silent");
        Check(Calls(visual.Methods.Single(m => m.Name == "Tick")).Any(c => c.Name == "Warn"), "Real bridge frame errors became silent");
        foreach (string sink in new[]{"Warn", "Report"})
            Check(Calls(types["TooManyLags.Plugin"].Methods.Single(m => m.Name == sink)).Any(c => c.Name == "LogWarning") && !types["TooManyLags.Plugin"].Methods.Single(m => m.Name == sink).Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "Diagnostics"), "Real errors gated by optional diagnostics");
        // Negative control: a reintroduced real diagnostic call must fail the
        // same assembly-wide scan, without loading Unity or touching profiles.
        var target = types["TooManyLags.Plugin"].Methods.Single(m => m.Name == "TestLog");
        var sample = new MethodDefinition("IdleAuditMutation", MethodAttributes.Static, image.MainModule.TypeSystem.Void);
        sample.Body.Instructions.Add(Instruction.Create(OpCodes.Call, target));
        Check(Calls(sample).Any(Routine), "Idle audit misses reintroduced diagnostics");
        Console.WriteLine("PASS: " + checks + " emitted idle/debug checks; no routine log calls, timer allocation, profiling RPC send, camera probes or combat counters; real errors and console feedback retained.");
    }

    internal static void RunDebug(AssemblyDefinition image)
    {
        var types = Flatten(image.MainModule.Types).ToDictionary(t => t.FullName);
        var plugin = types["TooManyLags.Plugin"];
        var log = plugin.Methods.Single(m => m.Name == "TestLog");
        Check(Calls(log).Any(c => c.Name == "LogInfo"), "Debug TestLog has no informational logger");
        Check(!log.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "Diagnostics"),
            "Debug test messages incorrectly depend on EnableLogs");
        var solo = types["TooManyLags.FgnSoloCombatTest"];
        Check(Calls(solo.Methods.Single(m => m.Name == "Message")).Any(c => c.Name == "TestLog"), "Debug AI-test messages removed");
        Check(Calls(solo.Methods.Single(m => m.Name == "Evaluated")).Any(c => c.Name == "TestLog"), "Debug source-wait messages removed");
        IdleGuard(solo.Methods.Single(m => m.Name == "Poll"), "session");
        IdleGuard(types["TooManyLags.SoloNetworkLatency"].Methods.Single(m => m.Name == "Poll"), "state");
        var after = types["TooManyLags.MobCollisionReadiness"].Methods.Single(m => m.Name == "AfterMob");
        Check(Calls(after).First().Name == "get_Active" && after.Body.Instructions.TakeWhile(i => i.OpCode != OpCodes.Ret).Count(i => i.Operand is MethodReference) == 1,
            "Inactive collision test touches Unity before active-test guard");
        var protocol = types["TooManyLags.MobBridgeProtocol"];
        Check(Calls(protocol.Methods.Single(m => m.Name == "Report")).Any(c => c.Name == "TestLog") &&
            Calls(protocol.Methods.Single(m => m.Name == "Report")).Any(c => c.Name == "Invoke"), "Debug bridge local logging/relay removed");
        Check(Calls(protocol.Methods.Single(m => m.Name == "ReceiveDiagnostic")).Any(c => c.Name == "TestLog"), "Debug server cannot log client bridge reports");
        var visual = types["TooManyLags.MobBridgeVisual"];
        Check(Calls(visual.Methods.Single(m => m.Name == "CloseRecording")).Any(c => c.Name == "Report"), "Debug recording summary removed");
        Check(types["TooManyLags.MobBridgeCost"].Fields.Count > 0, "Debug bridge cost diagnostics removed");
        foreach (string sink in new[]{"Warn", "Report"})
            Check(Calls(plugin.Methods.Single(m => m.Name == sink)).Any(c => c.Name == "LogWarning"), "Debug real error sink removed: " + sink);
        Console.WriteLine("PASS: " + checks + " emitted debug diagnostics/idle checks; AI-test and bridge logs/relay enabled, inactive test polls return before work.");
    }
}
