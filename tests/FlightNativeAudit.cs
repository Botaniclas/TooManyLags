using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class FlightNativeAudit
{
    private const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static int checks;private static string[] paths;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    private static Assembly Resolve(object sender,ResolveEventArgs e)
    {foreach(string path in paths){string file=Path.Combine(path,new AssemblyName(e.Name).Name+".dll");if(File.Exists(file))return Assembly.LoadFrom(file);}return null;}
    public static void Main(string[] args)
    {
        try
        {
            paths=new[]{args[1],args[2],Path.GetDirectoryName(args[0]),Path.GetDirectoryName(args[3])};AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
            Assembly addon=Assembly.LoadFrom(args[0]),fgn=Assembly.LoadFrom(args[3]);Type module=addon.GetType("TooManyLags.MobFlightHandoff",true);
            try
            {
                module.GetMethod("Start",All).Invoke(null,new object[]{fgn});
                Check(module.GetField("hooks",All).GetValue(null)!=null,"native flight field delegates or hook installation failed");
                Type creature=fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership",true);
                Check(Harmony.GetPatchInfo(creature.GetMethod("OnHandOff",All)).Transpilers.Any(p=>p.PatchMethod.DeclaringType==module),"actual native final commit transpiler missing");
                Assembly game=Assembly.LoadFrom(Path.Combine(args[1],"assembly_valheim.dll"));
                Check(Harmony.GetPatchInfo(game.GetType("ZDOMan").GetMethod("RPC_ZDOData",All)).Postfixes.Any(p=>p.PatchMethod.DeclaringType==module),"receive hook absent");
                Check(Harmony.GetPatchInfo(game.GetType("BaseAI").GetMethod("Awake",All)).Postfixes.Any(p=>p.PatchMethod.DeclaringType==module),"late initialization hook absent");
            }
            finally{module.GetMethod("Stop",All).Invoke(null,null);}
            using(var image=AssemblyDefinition.ReadAssembly(args[0]))
            using(var native=AssemblyDefinition.ReadAssembly(Path.Combine(args[1],"assembly_valheim.dll")))
            {
                var type=image.MainModule.Types.Single(t=>t.FullName==module.FullName);
                foreach(var method in type.Methods.Where(m=>m.HasBody))foreach(var ins in method.Body.Instructions)
                {
                    var call=ins.Operand as MethodReference;if(call==null)continue;
                    Check(!new[]{"set_isKinematic","set_useGravity","TakeOff","Land","UpdateAI","StartAttack"}.Contains(call.Name),"flight restore overrides physics guard or performs new gameplay: "+call.FullName);
                    Check(!(call.DeclaringType.Name=="ZDO"&&call.Name=="Set"),"flight state leaks into ongoing ZDO payload");
                }
                Check(!type.Methods.Any(m=>m.Name=="Update"||m.Name=="Tick"),"checkpoint adds permanent polling");
                var transfer=type.Methods.Single(m=>m.Name=="Transfer").Body.Instructions;
                int send=transfer.ToList().FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="Invoke");
                int write=transfer.ToList().FindIndex(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="SetOwner");
                Check(send>=0&&write>send,"checkpoint follows ownership instead of preceding it");
                var receive=native.MainModule.Types.Single(t=>t.Name=="ZDOMan").Methods.Single(m=>m.Name=="RPC_ZDOData");
                Check(receive.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="Deserialize"),"full native deserialize contract changed");
                var flying=native.MainModule.Types.Single(t=>t.Name=="Character").Methods.Single(m=>m.Name=="UpdateFlying");
                Check(flying.Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="m_moveDir"),"native flight movement contract changed");
                foreach(string field in new[]{"m_randomFlyTimer","aroundPointUpdateTime","arroundPointTarget","m_randomMoveUpdateTimer","m_randomMoveTarget"})
                    Check(native.MainModule.Types.Single(t=>t.Name=="BaseAI").Fields.Any(f=>f.Name==field),"native timer/goal field absent: "+field);
            }
            Console.WriteLine("PASS: "+checks+" actual flight contracts; field delegates and four Harmony hooks installed/removed under Valheim Mono.");
            Console.WriteLine("LIMIT: native field/IL/Harmony contracts verified, not real Unity flight, physics, packet timing or camera playback.");
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
}
