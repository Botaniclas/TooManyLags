using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MobSourceNativeAudit
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
            paths=new[]{args[1],args[2],Path.GetDirectoryName(args[0])};AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
            Assembly game=Assembly.LoadFrom(Path.Combine(args[1],"assembly_valheim.dll")),addon=Assembly.LoadFrom(args[0]);
            Type module=addon.GetType("TooManyLags.MobSourceHandoff",true);
            try
            {
                module.GetMethod("Start",All).Invoke(null,null);Check(module.GetField("hooks",All).GetValue(null)!=null,"actual source handoff hooks unavailable");
                foreach(var tuple in new[]{new[]{"ZDOMan","RPC_ZDOData"},new[]{"ZNet","Shutdown"}})
                {
                    MethodInfo native=game.GetType(tuple[0]).GetMethod(tuple[1],All);
                    var info=Harmony.GetPatchInfo(native);Check(info!=null&&(info.Prefixes.Concat(info.Postfixes).Any(p=>p.PatchMethod.DeclaringType==module)),"actual native lifecycle/replication hook absent");
                }
            }
            finally{module.GetMethod("Stop",All).Invoke(null,null);}
            using(var image=AssemblyDefinition.ReadAssembly(args[0]))using(var native=AssemblyDefinition.ReadAssembly(Path.Combine(args[1],"assembly_valheim.dll")))
            {
                var type=image.MainModule.Types.Single(t=>t.FullName==module.FullName);
                Check(type.Methods.Any(m=>m.Name=="Waiting")&&!type.Methods.Any(m=>m.Name=="Grant"||m.Name=="NeedsGrant"||m.Name=="ReceiveGrant"),"source still requires grants or lacks pending fence");
                foreach(var method in type.Methods.Where(m=>m.HasBody))foreach(var instruction in method.Body.Instructions)
                {
                    var call=instruction.Operand as MethodReference;if(call==null)continue;
                    Check(!(call.DeclaringType.Name=="ZDO"&&(call.Name=="SetOwner"||call.Name=="SetOwnerInternal")),"permission module directly changes ownership");
                    Check(!(call.DeclaringType.FullName=="System.Threading.Thread"&&call.Name=="Sleep"),"source cutoff blocks its thread");
                }
                Check(!type.Methods.Any(m=>m.Name=="Tick"||m.Name=="Update"),"source permissions add an actor polling loop");
                var ownership=image.MainModule.Types.Single(t=>t.FullName=="TooManyLags.FgnCombatOwnership");
                var observe=ownership.Methods.Single(m=>m.Name=="Observe");
                Check(CallAt(observe,"MobSourceHandoff","TryCommit")>=0&&CallAt(observe,"MobSourceHandoff","TryCommit")<CallAt(observe,"FgnTargetBoundary","Send"),"source target report waits on legacy boundary path before local commit");
                var attackEnd=ownership.Methods.Single(m=>m.Name=="OnAttackEnded");
                Check(CallAt(attackEnd,"MobSourceHandoff","TryCommit")>=0&&attackEnd.Body.Instructions.Skip(CallAt(attackEnd,"MobSourceHandoff","TryCommit")).Any(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="TooManyLags_AI_AttackReady"),"attack-end source retry lacks fast commit before legacy readiness packet");
                var ready=ownership.Methods.Single(m=>m.Name=="SourceLocalReady");
                foreach(string required in new[]{"Watching","Busy","CanCheckpoint"})
                    Check(ready.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name==required),"source local safety gate missing: "+required);
                foreach(string gate in new[]{"TryPrefix","Request","NativeSetOwner"})
                    Check(CallAt(ownership.Methods.Single(m=>m.Name==gate),"MobSourceHandoff","Waiting")>=0,"source publication gap not protected in "+gate);
                var initialize=image.MainModule.Types.Single(t=>t.FullName=="TooManyLags.Plugin").Methods.Single(m=>m.Name=="InitializeOwnership");
                foreach(string field in new[]{"CommitLocal","ValidateLocal","ResolveDestination","AcceptedServer","PublishFinal"})
                    Check(initialize.Body.Instructions.Any(i=>i.OpCode==OpCodes.Stsfld&&((FieldReference)i.Operand).DeclaringType.Name=="MobSourceHandoff"&&((FieldReference)i.Operand).Name==field),"source callback not wired by real plugin: "+field);
                Check(CallAt(initialize,"MobSourceHandoff","Start")>=0,"real plugin does not start source protocol");
                var result=image.MainModule.Types.Single(t=>t.FullName=="TooManyLags.FgnMobHandoff").Methods.Single(m=>m.Name=="SendResult");
                int scope=CallAt(result,"MobSourceHandoff","IsLocalCommit");
                Check(scope>=0&&result.Body.Instructions.Skip(scope+1).Take(3).Any(i=>i.OpCode==OpCodes.Ret),"native result suppression not scoped to source-local commit");
                Check(result.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="FGN_HandOffResult")&&CallAt(result,"ZRpc","Invoke")>scope,"legacy native result path removed");
                var publication=image.MainModule.Types.Single(t=>t.FullName=="TooManyLags.MobNativePublication").Methods.Single(m=>m.Name=="Send");
                var calls=publication.Body.Instructions.Select(i=>i.Operand as MethodReference).Where(m=>m!=null).ToArray();
                Check(calls.Count(m=>m.DeclaringType.Name=="ZDO"&&m.Name=="Serialize")==1,"native cutoff serializer not used exactly once");
                Check(calls.Count(m=>m.DeclaringType.Name=="ZRpc"&&m.Name=="Invoke")==1,"native cutoff has extra send");
                Check(!calls.Any(m=>m.Name=="ForceSendZDO"||m.Name=="SetOwner"||m.Name=="SetOwnerInternal"),"immediate packet drains unrelated objects or rewrites ownership");
                var writes=calls.Where(m=>m.DeclaringType.Name=="ZPackage"&&m.Name=="Write").Select(m=>m.Parameters[0].ParameterType.Name).ToArray();
                Check(writes.SequenceEqual(new[]{"Int32","ZDOID","UInt16","UInt32","Int64","Vector3","ZPackage","ZDOID"}),"native ZDOData field encoding changed");
                var receiver=native.MainModule.Types.Single(t=>t.Name=="ZDOMan").Methods.Single(m=>m.Name=="RPC_ZDOData");
                var reads=receiver.Body.Instructions.Select(i=>i.Operand as MethodReference).Where(m=>m!=null&&m.DeclaringType.Name=="ZPackage").Select(m=>m.Name).ToArray();
                int index=Array.IndexOf(reads,"ReadUShort");
                Check(index>=0&&reads.Skip(index).Take(5).SequenceEqual(new[]{"ReadUShort","ReadUInt","ReadLong","ReadVector3","ReadPackage"}),"native receiver owner/data/pose/body contract changed");
            }
            Console.WriteLine("PASS: "+checks+" actual source handoff/native publication contracts: lifecycle hooks, scoped permission-only ownership, single native serializer and exact ZDOData encoding.");
            Console.WriteLine("LIMIT: actual native hooks installed/uninstalled under Valheim Mono; no world, sockets or Unity physics executed.");
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
    private static int CallAt(MethodDefinition method,string type,string name)
    {
        for(int i=0;i<method.Body.Instructions.Count;i++)
        {var call=method.Body.Instructions[i].Operand as MethodReference;if(call!=null&&call.DeclaringType.Name==type&&call.Name==name)return i;}
        return -1;
    }
}
