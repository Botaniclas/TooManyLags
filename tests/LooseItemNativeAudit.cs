using System;
using System.Linq;
using Mono.Cecil;

internal static class LooseItemNativeAudit
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    private static MethodDefinition Method(ModuleDefinition module,string type,string name)
    { var result=module.GetType(type).Methods.FirstOrDefault(m=>m.Name==name);Check(result!=null,type+"."+name+" changed");return result; }
    private static bool Calls(MethodDefinition method,string type,string name)
    {return method.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.FullName==type&&((MethodReference)i.Operand).Name==name);}
    public static void Main(string[] args)
    {
        try
        {
            using(var native=AssemblyDefinition.ReadAssembly(args[0]))using(var mod=AssemblyDefinition.ReadAssembly(args[1]))
            {
                var n=native.MainModule;var p=mod.MainModule;var player=n.GetType("Player");
                Check(player.Fields.Any(f=>f.Name=="m_enableAutoPickup"&&f.IsStatic&&f.FieldType.FullName=="System.Boolean"),"native automatic pickup switch changed");
                Check(player.Fields.Any(f=>f.Name=="m_autoPickupRange"&&!f.IsStatic&&f.FieldType.FullName=="System.Single"),"native collection range changed");
                Check(n.GetType("ItemDrop").Fields.Any(f=>f.Name=="m_autoPickup"&&f.FieldType.FullName=="System.Boolean"),"native item pickup flag changed");
                Check(Calls(Method(n,"ItemDrop","RPC_RequestOwn"),"ZDO","SetOwner"),"native ownership request no longer commits ownership");
                Check(Calls(Method(n,"ItemDrop","PickupUpdate"),"ItemDrop","CanPickup"),"native pickup ownership gate changed");
                Check(Calls(Method(n,"Player","AutoPickup"),"ItemDrop","RequestOwn"),"native auto pickup request changed");
                Check(Calls(Method(n,"ItemDrop","AutoStackItems"),"ZNetView","IsOwner"),"native stack merging no longer checks ownership");
                var guard=p.GetType("TooManyLags.LooseItemGuard");Check(guard!=null,"item guard missing");
                foreach(var method in guard.Methods.Where(m=>m.HasBody))foreach(var instruction in method.Body.Instructions)
                {
                    var call=instruction.Operand as MethodReference;if(call==null)continue;
                    Check(call.Name!="SetOwner"&&call.Name!="SetOwnerInternal","guard must not write ownership");
                    Check(call.DeclaringType.FullName!="UnityEngine.Physics","guard must not scan physics");
                }
                Check(Calls(Method(p,"TooManyLags.PlayerSimulation.LocalAgent","Check"),"TooManyLags.LooseItemGuard","Busy"),"player source guard missing");
                Check(Calls(Method(p,"TooManyLags.GridHandoffs.Protocol","Receive"),"TooManyLags.LooseItemGuard","Busy"),"server simulation source guard missing");
                Check(!guard.Methods.Any(m=>m.Name=="Update"||m.Name=="FixedUpdate"||m.Name=="Tick"),"item guard introduced polling");
            }
            Console.WriteLine("PASS: "+checks+" actual item pickup/merging and compiled handoff contracts; no live Unity or inventory execution.");
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
}
