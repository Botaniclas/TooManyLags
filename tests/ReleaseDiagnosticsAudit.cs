using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class ReleaseDiagnosticsAudit
{
    private static int Main(string[] args)
    {
        try
        {
            var counters = new HashSet<string>(new[] { "Rebuilds", "NeighborReads", "PlayerUpdates", "TimerVisits", "PolicyAllocations", "Attempts", "SkippedCells", "PendingVisits", "PairTests" });
            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                int checkedTypes = 0;
                foreach (string prefix in new[] { "TooManyLags.", "TooManyLags.PlayerSimulation." })
                foreach (string name in new[] { "AuthorityGrid", "AuthorityWorld", "RegionContentionGate" })
                {
                    TypeDefinition type = assembly.MainModule.GetType(prefix + name);
                    if (type == null) throw new Exception("Missing production grid type: " + prefix + name);
                    foreach (MethodDefinition method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        foreach (Instruction instruction in method.Body.Instructions)
                        {
                            MethodReference call = instruction.Operand as MethodReference;
                            if (call != null && call.DeclaringType.FullName == type.FullName && call.Name == "Count")
                                throw new Exception("Test instrumentation remains in release: " + method.FullName);
                            FieldReference field = instruction.Operand as FieldReference;
                            if (field != null && counters.Contains(field.Name) && (instruction.OpCode == OpCodes.Stfld || instruction.OpCode == OpCodes.Stsfld))
                                throw new Exception("Diagnostic counter write remains in release: " + method.FullName);
                        }
                    }
                    checkedTypes++;
                }
                if (checkedTypes != 6) throw new Exception("Not all production grid variants checked.");
                TypeDefinition status = assembly.MainModule.GetType("TooManyLags.FgnCombatStatus");
                MethodDefinition connect = null;
                foreach (MethodDefinition method in status.Methods) if (method.Name == "Connect") connect = method;
                if (connect == null || !connect.HasBody) throw new Exception("Diagnostic connection gate missing.");
                bool gate = false, enabled = false;
                foreach (Instruction instruction in connect.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Newobj || instruction.OpCode == OpCodes.Newarr)
                        throw new Exception("Disabled diagnostic connection still allocates a closure/array.");
                    FieldReference field = instruction.Operand as FieldReference;
                    if (field != null && field.Name == "Diagnostics") gate = true;
                    MethodReference call = instruction.Operand as MethodReference;
                    if (call != null && call.Name == "ConnectEnabled") { if (!gate) throw new Exception("Diagnostic connection precedes its gate."); enabled = true; }
                }
                if (!gate || !enabled) throw new Exception("Diagnostic opt-in gate not verified.");
                Console.WriteLine("PASS: six production grid/region types contain no calls or writes for test instrumentation. Test fixtures retain their counters.");
                Console.WriteLine("PASS: disabled diagnostic connection gate allocates no callback closure or array.");
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
