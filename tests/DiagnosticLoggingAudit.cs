using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

public sealed class DiagnosticLog
{
    public readonly List<object> Messages=new List<object>();
    public void LogWarning(object value){Messages.Add(value);}
}
public sealed class DiagnosticRuntime
{
    public bool logs,Alive=true; public readonly DiagnosticLog Log=new DiagnosticLog();
    public static bool IsAlive(DiagnosticRuntime runtime){return runtime!=null&&runtime.Alive;}
    public static DiagnosticLog Logger(DiagnosticRuntime runtime){return runtime.Log;}
}
// Executes the actual compiled logging bodies. Only the Unity liveness check
// and BepInEx logger are replaced; branches and message formatting are copied.
internal static class DiagnosticLoggingAudit
{
    private static int checks;
    private static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    private static IEnumerable<TypeDefinition> Types(TypeDefinition t)
    {yield return t;foreach(var n in t.NestedTypes)foreach(var child in Types(n))yield return child;}
    private static Type Clone(AssemblyDefinition source,string name)
    {
        var original=source.MainModule.Types.Single(t=>t.FullName=="TooManyLags.Plugin");
        using(var image=AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name,new Version(1,0)),name,ModuleKind.Dll))
        {
            var module=image.MainModule;
            var type=new TypeDefinition("", "LoggerProbe",Mono.Cecil.TypeAttributes.Public|Mono.Cecil.TypeAttributes.Class,module.TypeSystem.Object);module.Types.Add(type);
            var instance=new FieldDefinition("instance",Mono.Cecil.FieldAttributes.Public|Mono.Cecil.FieldAttributes.Static,module.ImportReference(typeof(DiagnosticRuntime)));type.Fields.Add(instance);
            var diagnostics=new FieldDefinition("Diagnostics",Mono.Cecil.FieldAttributes.Public|Mono.Cecil.FieldAttributes.Static,module.TypeSystem.Boolean);type.Fields.Add(diagnostics);
            foreach(string methodName in new[]{"Warn","RuntimeFault","RequestFault"})
            {
                var input=original.Methods.Single(m=>m.Name==methodName);
                var output=new MethodDefinition(methodName,Mono.Cecil.MethodAttributes.Public|Mono.Cecil.MethodAttributes.Static,module.TypeSystem.Void);
                if(!input.IsStatic)output.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(DiagnosticRuntime))));
                foreach(var parameter in input.Parameters)output.Parameters.Add(new ParameterDefinition(module.ImportReference(parameter.ParameterType)));
                type.Methods.Add(output);output.Body.InitLocals=input.Body.InitLocals;
                foreach(var variable in input.Body.Variables)output.Body.Variables.Add(new VariableDefinition(module.ImportReference(variable.VariableType)));
                var mapped=new Dictionary<Instruction,Instruction>();
                foreach(var item in input.Body.Instructions)
                {
                    var copy=Instruction.Create(OpCodes.Nop);copy.OpCode=item.OpCode;copy.Operand=item.Operand;mapped.Add(item,copy);output.Body.Instructions.Add(copy);
                }
                foreach(var inputInstruction in input.Body.Instructions)
                {
                    var copy=mapped[inputInstruction];var operand=copy.Operand;
                    var branch=operand as Instruction;if(branch!=null){copy.Operand=mapped[branch];continue;}
                    var field=operand as FieldReference;
                    if(field!=null)
                    {
                        if(field.Name=="instance")copy.Operand=instance;
                        else if(field.Name=="Diagnostics")copy.Operand=diagnostics;
                        else if(field.Name=="logs")copy.Operand=module.ImportReference(typeof(DiagnosticRuntime).GetField("logs"));
                        else throw new Exception("Unexpected logging dependency: "+field.FullName);
                        continue;
                    }
                    var call=operand as MethodReference;
                    if(call!=null)
                    {
                        if(call.DeclaringType.FullName=="UnityEngine.Object"&&call.Name=="op_Implicit")
                        {copy.OpCode=OpCodes.Call;copy.Operand=module.ImportReference(typeof(DiagnosticRuntime).GetMethod("IsAlive"));}
                        else if(call.Name=="get_Logger")
                        {copy.OpCode=OpCodes.Call;copy.Operand=module.ImportReference(typeof(DiagnosticRuntime).GetMethod("Logger"));}
                        else if(call.Name=="LogWarning")copy.Operand=module.ImportReference(typeof(DiagnosticLog).GetMethod("LogWarning"));
                        else copy.Operand=module.ImportReference(call);
                        continue;
                    }
                    var local=operand as VariableDefinition;if(local!=null)copy.Operand=output.Body.Variables[local.Index];
                    var argument=operand as ParameterDefinition;if(argument!=null)copy.Operand=output.Parameters[argument.Index+(input.IsStatic?0:1)];
                }
                Check(input.Body.ExceptionHandlers.Count==0,"logger body gained exception regions: extend copier first");
            }
            using(var stream=new MemoryStream()){image.Write(stream);return Assembly.Load(stream.ToArray()).GetType("LoggerProbe",true);}
        }
    }
    private static void Runtime(Type logger,bool legacy)
    {
        var runtime=new DiagnosticRuntime();logger.GetField("instance").SetValue(null,runtime);
        foreach(bool enabled in new[]{false,true})
        {
            logger.GetField("Diagnostics").SetValue(null,enabled);runtime.logs=enabled;runtime.Log.Messages.Clear();
            logger.GetMethod("Warn").Invoke(null,new object[]{"deliberate warning"});
            logger.GetMethod("RuntimeFault").Invoke(null,new object[]{runtime,new InvalidOperationException("grid failure")});
            logger.GetMethod("RequestFault").Invoke(null,new object[]{runtime,new InvalidOperationException("request failure")});
            int expected=legacy&&!enabled?0:3;
            Check(runtime.Log.Messages.Count==expected,"compiled errors must reach log independently of diagnostics; enabled="+enabled+" legacy="+legacy);
            if(expected==3)Check(runtime.Log.Messages[0].ToString()=="deliberate warning"&&runtime.Log.Messages[1].ToString().Contains("grid failure")&&runtime.Log.Messages[2].ToString().Contains("request failure"),"actual error details retained");
        }
        runtime.Log.Messages.Clear();runtime.Alive=false;logger.GetMethod("Warn").Invoke(null,new object[]{"destroyed"});
        logger.GetField("instance").SetValue(null,null);logger.GetMethod("Warn").Invoke(null,new object[]{"absent"});
        Check(runtime.Log.Messages.Count==0,"absent/destroyed plugin remains safe");
    }
    public static int Main(string[] args)
    {
        try
        {
            using(var current=AssemblyDefinition.ReadAssembly(args[0]))using(var baseline=AssemblyDefinition.ReadAssembly(args[1]))
            {
                Runtime(Clone(current,"CurrentDiagnosticLogger"),false);Runtime(Clone(baseline,"BaselineDiagnosticLogger"),true);
                var types=current.MainModule.Types.SelectMany(Types).ToArray();
                string[] metrics={"Changes","HeldStarts","ExpiredHolds","Evaluations","PatchedMethods","ObservedWrites"};
                foreach(var type in types.Where(t=>new[]{"FgnTargetBoundary","FgnBossPolicy","FgnTargetEvents","TargetEvents"}.Contains(t.Name)))
                    Check(!type.Fields.Any(f=>metrics.Contains(f.Name)),"test statistic field survives normal build: "+type.FullName);
                var node=types.Single(t=>t.FullName=="TooManyLags.AuthorityController/NativeNode");
                Check(!node.Fields.Any(f=>f.Name=="Requests"||f.Name.Contains("<Requests>")),"duplicate per-object request field survives");
                Check(node.Methods.Single(m=>m.Name=="get_Requests").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="PendingRequests"),"status count does not use real queue");
                foreach(var method in types.SelectMany(t=>t.Methods).Where(m=>m.HasBody))foreach(var instruction in method.Body.Instructions)
                {
                    var field=instruction.Operand as FieldReference;
                    Check(field==null||field.DeclaringType.FullName!=node.FullName||field.Name!="Requests","request counter access survives");
                }
            }
            foreach(string source in Directory.GetFiles(Path.Combine(args[2],"src"),"*.cs",SearchOption.AllDirectories))
                Check(!Regex.IsMatch(File.ReadAllText(source),@"if\s*\((?:(?:TooManyLags\.)?Plugin\.Diagnostics|Plugin\.Options\.Logs)\)\s*(?:TooManyLags\.)?Plugin\.Warn\("),"error call is still gated by optional logs: "+source);
            Console.WriteLine("PASS: "+checks+" diagnostic-cost/logging assertions; actual normal DLL logging bodies run with logs off/on; .80 negative control remains gated. Unity/BepInEx sinks modeled.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
