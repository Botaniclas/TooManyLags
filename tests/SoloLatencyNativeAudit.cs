using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;

internal static class SoloLatencyNativeAudit
{
    private static string[] paths;
    private static Assembly Resolve(object sender,ResolveEventArgs args)
    {foreach(string path in paths){string file=Path.Combine(path,new AssemblyName(args.Name).Name+".dll");if(File.Exists(file))return Assembly.LoadFrom(file);}return null;}
    public static void Main(string[] args)
    {
        try
        {
            paths=new[]{args[1],args[2],Path.GetDirectoryName(args[0])};AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
            Assembly addon=Assembly.LoadFrom(args[0]),game=Assembly.LoadFrom(Path.Combine(args[1],"assembly_valheim.dll"));
            Type module=addon.GetType("TooManyLags.SoloNetworkLatency",true);const BindingFlags all=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            MethodInfo send=game.GetType("ZRpc",true).GetMethod("SendPackage",all),update=game.GetType("ZRpc",true).GetMethod("Update",all),quality=game.GetType("ZSteamSocket",true).GetMethod("GetConnectionQuality",all);
            Harmony hooks=new Harmony("tml.solo.latency.native.audit");
            try
            {
                hooks.Patch(send,transpiler:new HarmonyMethod(module.GetMethod("RewriteSend",all)));
                hooks.Patch(update,transpiler:new HarmonyMethod(module.GetMethod("RewriteReceive",all)));
                hooks.Patch(quality,postfix:new HarmonyMethod(module.GetMethod("Quality",all)));
                if(!new[]{send,update,quality}.All(m=>Harmony.GetPatchInfo(m).Owners.Contains(hooks.Id)))throw new Exception("native latency hooks not installed");
            }
            finally{hooks.UnpatchSelf();}
            if(new[]{send,update,quality}.Any(m=>Harmony.GetPatchInfo(m).Owners.Contains(hooks.Id)))throw new Exception("native latency hooks not removed");
            using(AssemblyDefinition image=AssemblyDefinition.ReadAssembly(args[0]))
            {
                var type=image.MainModule.GetType("TooManyLags.SoloNetworkLatency");
                foreach(var method in type.Methods.Where(m=>m.HasBody))foreach(var instruction in method.Body.Instructions)
                {
                    var call=instruction.Operand as MethodReference;
                    if(call!=null&&new[]{"SetOwner","UpdateAI","StartAttack","Sleep","SetConfigValue"}.Contains(call.Name))throw new Exception("latency test changes gameplay/global settings or blocks: "+call.FullName);
                    var field=instruction.Operand as FieldReference;
                    if(field!=null&&instruction.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&field.Name=="m_socket")throw new Exception("native socket binding replaced");
                }
            }
            Console.WriteLine("PASS: actual game ZRpc send/receive and Steam quality hooks install/JIT/uninstall under Valheim Mono; native send/receive sites validated; no authority, blocking or global transport setting writes.");
            Console.WriteLine("LIMIT: native bodies installed, not executed in a Unity/Steam world; full third-party patch graph and live latency not measured.");
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
}
