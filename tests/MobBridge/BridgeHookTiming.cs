using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
internal static class BridgeHookTiming
{
    private const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static string[] paths;
    private static Assembly Resolve(object sender,ResolveEventArgs args)
    {
        foreach(string path in paths) { string file=Path.Combine(path,new AssemblyName(args.Name).Name+".dll"); if(File.Exists(file))return Assembly.LoadFrom(file); }
        return null;
    }
    private static void Measure(string name,Action action)
    { Stopwatch watch=Stopwatch.StartNew(); action(); watch.Stop(); Console.WriteLine("TIMING: "+name+"="+watch.Elapsed.TotalMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"ms"); }
    public static int Main(string[] args)
    {
        try
        {
            string seam=Path.Combine(Path.GetDirectoryName(typeof(BridgeHookTiming).Assembly.Location),"hook-timing-seam");Directory.CreateDirectory(seam);
            using(AssemblyDefinition copy=AssemblyDefinition.ReadAssembly(Path.Combine(args[1],"assembly_valheim.dll")))
            {
                foreach(string name in new[]{"Character","Player","MonsterAI","Humanoid","ZSyncAnimation"})
                { MethodDefinition init=copy.MainModule.Types.Single(t=>t.Name==name).Methods.Single(m=>m.Name==".cctor");init.Body.Instructions.Clear();init.Body.ExceptionHandlers.Clear();init.Body.Variables.Clear();init.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); }
                copy.Write(Path.Combine(seam,"assembly_valheim.dll"));
            }
            paths=new[]{seam,args[1],args[2],Path.GetDirectoryName(args[0])};AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
            Assembly game=Assembly.LoadFrom(Path.Combine(seam,"assembly_valheim.dll")),addon=Assembly.LoadFrom(args[0]);
            Type native=addon.GetType("TooManyLags.MobBridgeNativeActor",true),visual=addon.GetType("TooManyLags.MobBridgeVisual",true),protocol=addon.GetType("TooManyLags.MobBridgeProtocol",true),test=addon.GetType("TooManyLags.FgnSoloCombatTest",true);
            for(int pass=0;pass<3;pass++)
            {
                Console.WriteLine("PASS INDEX: "+pass);
                Harmony solo=new Harmony("TooManyLags.timing.solo");
                Measure("test.install",delegate{solo.Patch(game.GetType("MonsterAI").GetMethod("UpdateTarget",All),postfix:new HarmonyMethod(test.GetMethod("Evaluated",All)));solo.Patch(game.GetType("MonsterAI").GetMethod("UpdateAI",All),finalizer:new HarmonyMethod(test.GetMethod("Completed",All)));});
                Measure("destination.install",delegate{protocol.GetMethod("EnsureAiHook",All).Invoke(null,null);});
                Measure("native.install",delegate{native.GetMethod("EnsureHooks",All).Invoke(null,null);});
                Measure("presentation.install",delegate{if(!(bool)visual.GetMethod("EnsurePresentationHooks",All).Invoke(null,null))throw new Exception("presentation hooks failed");});
                Measure("presentation.remove",delegate{visual.GetMethod("DisablePresentationHooks",All).Invoke(null,null);});
                Measure("native.remove",delegate{native.GetMethod("ReleaseHooks",All).Invoke(null,null);});
                Measure("destination.remove",delegate{protocol.GetMethod("DisableAiHook",All).Invoke(null,null);});
                Measure("test.remove",delegate{solo.UnpatchSelf();});
            }
            Measure("retained.sessionStart",delegate{
                protocol.GetMethod("EnsureReceiveHook",All).Invoke(null,null);
                protocol.GetMethod("EnsureAiHook",All).Invoke(null,null);
                visual.GetMethod("PrepareSession",All).Invoke(null,null);
                test.GetMethod("EnsureLocalHooks",All).Invoke(null,null);
            });
            for(int pass=0;pass<3;pass++)
                Measure("retained.edge"+pass,delegate{
                    test.GetMethod("EnsureLocalHooks",All).Invoke(null,null);
                    protocol.GetMethod("EnsureReceiveHook",All).Invoke(null,null);
                    protocol.GetMethod("EnsureAiHook",All).Invoke(null,null);
                    native.GetMethod("EnsureHooks",All).Invoke(null,null);
                    visual.GetMethod("EnsurePresentationHooks",All).Invoke(null,null);
                    test.GetMethod("ClearLocal",All).Invoke(null,new object[]{false});
                    native.GetMethod("ReleaseHooks",All).Invoke(null,null);
                    visual.GetMethod("Stop",All).Invoke(null,null);
                });
            Measure("retained.sessionEnd",delegate{
                test.GetMethod("ClearLocal",All).Invoke(null,new object[]{true});
                visual.GetMethod("EndSession",All).Invoke(null,null);
                protocol.GetMethod("DisableAiHook",All).Invoke(null,null);
                protocol.GetMethod("DisableReceiveHook",All).Invoke(null,null);
            });
            Console.WriteLine("LIMIT: actual game methods/Harmony under embedded Mono; Unity-only static initializers omitted, no third-party patch stack, no scene/rig/native physics execution.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
