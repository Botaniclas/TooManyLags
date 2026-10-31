using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MobVisualNativeAudit
{
    private const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static string[] paths;
    private static Assembly Resolve(object sender,ResolveEventArgs args)
    { foreach(string path in paths){string file=Path.Combine(path,new AssemblyName(args.Name).Name+".dll");if(File.Exists(file))return Assembly.LoadFrom(file);}return null; }
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    public static void Main(string[] args)
    {
        try
        {
            paths=new[]{args[1],args[2],Path.GetDirectoryName(args[0]),Path.GetDirectoryName(args[3])};AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
            Assembly plugin=Assembly.LoadFrom(args[0]),game=Assembly.LoadFrom(Path.Combine(args[1],"assembly_valheim.dll")),fgn=Assembly.LoadFrom(args[3]);
            Type bridge=plugin.GetType("TooManyLags.MobHandoffVisual",true);
            MethodInfo native=fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership",true).GetMethod("OnHandOff",All);
            Harmony patch=new Harmony("tml.visual.native.audit");
            try
            {
                patch.Patch(native,prefix:new HarmonyMethod(bridge.GetMethod("BeforeCommit",All)),postfix:new HarmonyMethod(bridge.GetMethod("AfterCommit",All)));
                Patches info=Harmony.GetPatchInfo(native);
                Check(info.Prefixes.Count(p=>p.owner==patch.Id)==1&&info.Postfixes.Count(p=>p.owner==patch.Id)==1,"actual FGN hook installation/JIT failed");
            }
            finally { patch.UnpatchSelf(); }
            Check(!Harmony.GetPatchInfo(native).Owners.Contains(patch.Id),"native visual hooks not removed");
            Check(game.GetType("Character").GetMethod("GetVisual",All).ReturnType.Name=="GameObject","native visual accessor changed");
            bool observer9=false,observer10=false;
            foreach(MethodInfo method in bridge.GetMethods(All))if(method.Name=="CaptureObserver"){ParameterInfo[] parameters=method.GetParameters();observer9|=parameters.Length==9;observer10|=parameters.Length==10&&parameters[9].ParameterType==typeof(float);}
            Check(observer9&&observer10&&bridge.GetMethod("CommitObserver",All)!=null,"observer checkpoint presentation API unavailable");
            using(AssemblyDefinition source=AssemblyDefinition.ReadAssembly(args[0]))
            using(AssemblyDefinition networking=AssemblyDefinition.ReadAssembly(args[3]))
            using(AssemblyDefinition valheim=AssemblyDefinition.ReadAssembly(Path.Combine(args[1],"assembly_valheim.dll")))
            {
                TypeDefinition module=source.MainModule.Types.Single(t=>t.FullName=="TooManyLags.MobHandoffVisual");
                string[] forbidden={"SetOwner","ForceSendZDO","SyncNow","StartAttack","UpdateAI","UpdateAttack","Simulate","SyncTransforms","set_speed","set_isKinematic"};
                foreach(MethodDefinition method in module.Methods.Where(m=>m.HasBody))
                    foreach(Instruction instruction in method.Body.Instructions)
                    {
                        MethodReference call=instruction.Operand as MethodReference;if(call==null)continue;
                        Check(!forbidden.Contains(call.Name),"visual playback writes authority/physics/animation or sends gameplay: "+call.FullName);
                        Check(!(call.Name=="Invoke"&&(call.DeclaringType.Name=="ZRpc"||call.DeclaringType.Name=="ZNetView"||call.DeclaringType.Name=="ZRoutedRpc")),"visual playback sends a new gameplay RPC");
                    }
                MethodDefinition capture=module.Methods.Single(m=>m.Name=="Capture");
                Check(!capture.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="SyncNow"),"render capture must not trigger native owner-gain pose correction");
                Check(!module.Fields.Any(f=>f.Name=="observedWait"),"global observed delay can contaminate unrelated handoffs");
                MethodDefinition commit=networking.MainModule.Types.Single(t=>t.FullName=="FiresGhettoNetworkMod.CreatureOwnership").Methods.Single(m=>m.Name=="OnHandOff");
                Check(commit.Body.Instructions.Count(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="SetOwner")==1,"native ownership commit contract changed");
                Check(commit.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="ForceSendZDO"),"native state send absent");
                MethodDefinition awake=valheim.MainModule.Types.Single(t=>t.Name=="Character").Methods.Single(m=>m.Name=="Awake");
                Check(awake.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="Visual"),"native child visual contract changed");
                Check(module.Fields.Any(f=>f.Name=="Limit"&&(int)f.Constant==32),"render state has no explicit upper bound");
                MethodDefinition velocity=valheim.MainModule.Types.Single(t=>t.Name=="Character").Methods.Single(m=>m.Name=="GetVelocity");
                Check(velocity.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="GetVec3"),"nonlocal velocity no longer reads native synchronized body velocity");
                Check(valheim.MainModule.Types.Single(t=>t.Name=="Character").Fields.Any(f=>f.Name=="currentRotSpeedFactor"&&f.FieldType.FullName=="System.Single"),"native smooth-flight turn factor changed");
                TypeDefinition curve=source.MainModule.Types.Single(t=>t.FullName=="TooManyLags.MobVisualCurve");
                foreach(MethodDefinition method in curve.Methods.Where(m=>m.HasBody))foreach(Instruction instruction in method.Body.Instructions)
                {MethodReference call=instruction.Operand as MethodReference;if(call!=null)Check(!new[]{"SetOwner","UpdateAI","StartAttack","SetVelocity","Simulate"}.Contains(call.Name),"visual curve performs gameplay: "+call.FullName);}
            }
            Console.WriteLine("PASS: "+checks+" actual addon/game/FGN visual contracts and native Harmony install/uninstall under Valheim Mono.");
            Console.WriteLine("LIMIT: native bodies installed, not executed in a Unity world; no camera, Animator, physics or live transport validation.");
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
}
