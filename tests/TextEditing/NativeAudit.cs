using System;
using System.Linq;
using Mono.Cecil;

internal static class TextEditNativeAudit
{
    private static int checks;
    private static void Check(bool condition,string message) { checks++;if(!condition)throw new Exception(message); }
    private static MethodDefinition Method(ModuleDefinition module,string type,string name,params string[] args)
    {
        var owner=module.GetType(type);Check(owner!=null,"native type missing: "+type);
        var matches=owner.Methods.Where(m=>m.Name==name && m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(args)).ToArray();
        Check(matches.Length==1,"native target changed: "+type+"."+name);return matches[0];
    }
    private static bool Calls(MethodDefinition method,string type,string name)
    { return method.Body.Instructions.Any(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName==type && ((MethodReference)i.Operand).Name==name); }
    private static bool Field(MethodDefinition method,string type,string name)
    { return method.Body.Instructions.Any(i=>i.Operand is FieldReference && ((FieldReference)i.Operand).DeclaringType.FullName==type && ((FieldReference)i.Operand).Name==name); }
    public static void Main(string[] args)
    {
        try
        {
            using(var native=AssemblyDefinition.ReadAssembly(args[0]))
            using(var production=AssemblyDefinition.ReadAssembly(args[1]))
            {
                var n=native.MainModule;var p=production.MainModule;
                Method(n,"TextInput","RequestText","TextReceiver","System.String","System.Int32");
                Method(n,"TextInput","setText","System.String");
                Method(n,"ZNet","OnNewConnection","ZNetPeer");Method(n,"ZNet","Disconnect","ZNetPeer");Method(n,"ZNet","Shutdown","System.Boolean");
                Method(n,"ZNetView","ResetZDO");Method(n,"ZNetView","OnDestroy");
                Check(n.GetType("TextInput").Fields.Any(f=>f.Name=="m_queuedSign" && f.FieldType.FullName=="TextReceiver"),"native queued receiver contract changed");
                Check(Calls(Method(n,"Sign","Interact","Humanoid","System.Boolean","System.Boolean"),"TextInput","RequestText"),"native sign UI start changed");
                Check(Calls(Method(n,"TeleportWorld","Interact","Humanoid","System.Boolean","System.Boolean"),"TextInput","RequestText"),"native portal UI start changed");
                Check(Calls(Method(n,"Sign","SetText","System.String"),"ZNetView","ClaimOwnership"),"native sign ownership preparation contract changed");
                Check(Calls(Method(n,"ZNetView","ClaimOwnership"),"ZNetView","IsOwner"),"native sign claim no longer checks actual local ownership");
                var protection=p.GetType("TooManyLags.TextEditing.Protection");Check(protection!=null,"production text protection missing");
                Check(protection.BaseType.FullName=="System.Object" && !protection.Methods.Any(m=>m.Name=="Update" || m.Name=="FixedUpdate" || m.Name=="LateUpdate"),"text protection installs no independent Unity loop");
                Check(!protection.Methods.Single(m=>m.Name=="Start").Body.Instructions.Any(i=>Object.Equals(i.Operand,"OwnerChanged") || Object.Equals(i.Operand,"Destroyed")),"text protection adds no independent per-ZDO owner/reset Harmony hooks");
                Check(!protection.Methods.Where(m=>m.HasBody).Any(m=>m.Body.Instructions.Any(i=>i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName=="ZDO" && ((MethodReference)i.Operand).Name.StartsWith("SetOwner"))),"text protection must reuse guarded transfer, never write ownership directly");
                Check(Field(Method(p,"TooManyLags.AuthorityController/NativeNode","Apply","System.Int64"),"TooManyLags.AuthorityController/NativeNode","TextHeld"),"production grid text flag missing");
                Check(Field(Method(p,"TooManyLags.PlayerSimulation.AuthorityController/Node","Apply","System.Int64"),"TooManyLags.PlayerSimulation.AuthorityController/Node","TextHeld"),"player grid text flag missing");
                Check(!Field(Method(p,"TooManyLags.AuthorityController/NativeNode","Apply","System.Int64"),"TooManyLags.AuthorityController","TextEditLocked"),"steady grid decisions must use cached flags instead of session callbacks");
                Check(Field(Method(p,"TooManyLags.GridHandoffs.Protocol","Receive","TooManyLags.GridHandoffs.Protocol/Connection","ZRpc","ZPackage"),"TooManyLags.GridHandoffs.Protocol","TextEditBusy"),"source identified-commit text veto missing");
                Check(Field(Method(p,"TooManyLags.PlayerSimulation.LocalAgent","Check","TooManyLags.PlayerSimulation.LocalAgent/Ticket","ZDO","ZNetView","System.Single&"),"TooManyLags.PlayerSimulation.LocalAgent","TextEditBusy"),"player source text veto missing");
            }
            Console.WriteLine("PASS: "+checks+" actual Valheim text UI, lifecycle, source-veto and compiled module contracts. No live Unity session.");
        }
        catch(Exception error) { Console.Error.WriteLine(error);Environment.ExitCode=1; }
    }
}
