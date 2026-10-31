using System;
using System.Linq;
using Mono.Cecil;

internal static class BuildPolicyAudit
{
    public static int Main(string[] args)
    {
        try
        {
            using(var image=AssemblyDefinition.ReadAssembly(args[0]))
            {
                foreach(string name in new[]{"TooManyLags.FgnSoloCombatTest","TooManyLags.SoloNetworkLatency"})
                    if(!image.MainModule.Types.Any(t=>t.FullName==name))throw new Exception("Default development DLL lacks "+name);
                var boundary=image.MainModule.Types.Single(t=>t.FullName=="TooManyLags.FgnTargetBoundary");
                if(boundary.Fields.Any(f=>f.Name=="HeldStarts"))throw new Exception("Default development DLL still gathers idle statistics");
                DebugIdleAudit.RunDebug(image);
            }
            Console.WriteLine("PASS: default development DLL retains solo test/artificial latency with AI-test and bridge diagnostics.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
