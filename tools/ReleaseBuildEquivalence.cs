using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
internal static class ReleaseBuildEquivalence
{
    private static string Name(string value) { return Regex.Replace(value, @"\{[0-9A-Fa-f-]{36}\}", "{compiler-id}"); }
    private static IEnumerable<TypeDefinition> Types(TypeDefinition type)
    { yield return type; foreach (var child in type.NestedTypes) foreach (var value in Types(child)) yield return value; }
    private static string Arg(CustomAttributeArgument value)
    {
        var values = value.Value as CustomAttributeArgument[];
        return value.Type.FullName + "=" + (values == null ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) : String.Join(",", values.Select(Arg).ToArray()));
    }
    private static string Attributes(IEnumerable<CustomAttribute> values)
    { return String.Join("\n", values.Select(a => a.AttributeType.FullName + ":" + String.Join(",", a.ConstructorArguments.Select(Arg).ToArray()) + ":" + String.Join(",", a.Fields.Select(f => f.Name + "=" + Arg(f.Argument)).ToArray()) + ":" + String.Join(",", a.Properties.Select(p => p.Name + "=" + Arg(p.Argument)).ToArray())).OrderBy(a => a).ToArray()); }
    private static void Put(Dictionary<string,string> values, string key, string value)
    {
        string candidate=key;int occurrence=0;
        while(values.ContainsKey(candidate)) candidate=key+"#"+(++occurrence).ToString(CultureInfo.InvariantCulture);
        values.Add(candidate,value);
    }
    private static Dictionary<string,string> Image(AssemblyDefinition assembly)
    {
        var result = new Dictionary<string,string>();
        Put(result,"assembly", assembly.Name.FullName + "\n" + Attributes(assembly.CustomAttributes));
        foreach(var reference in assembly.MainModule.AssemblyReferences) Put(result,"reference:"+reference.Name, reference.FullName);
        foreach(var type in assembly.MainModule.Types.SelectMany(Types))
        {
            string name=Name(type.FullName);
            Put(result,"type:"+name, type.Attributes+":"+(type.BaseType==null?"":Name(type.BaseType.FullName))+":"+Attributes(type.CustomAttributes)+":"+String.Join(",",type.Interfaces.Select(i=>Name(i.InterfaceType.FullName)).ToArray()));
            foreach(var field in type.Fields) Put(result,"field:"+Name(field.FullName), field.Attributes+":"+Convert.ToString(field.Constant,CultureInfo.InvariantCulture)+":"+Convert.ToBase64String(field.InitialValue??new byte[0])+":"+Attributes(field.CustomAttributes));
            foreach(var method in type.Methods) Put(result,"method:"+Name(method.FullName), method.Attributes+":"+method.ImplAttributes+":"+Attributes(method.CustomAttributes)+":"+String.Join(",",method.Parameters.Select(p=>p.Name+":"+p.Attributes+":"+Convert.ToString(p.Constant,CultureInfo.InvariantCulture)+":"+Attributes(p.CustomAttributes)).ToArray())+"\n"+Body(method));
        }
        return result;
    }
    public static int Main(string[] args)
    {
        try
        {
            var resolver=new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(@"D:\Steam-Bibliotheksordner\steamapps\common\Valheim\valheim_Data\Managed");
            resolver.AddSearchDirectory(@"C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx\core");
            var parameters=new ReaderParameters{AssemblyResolver=resolver};
            using(var a=AssemblyDefinition.ReadAssembly(args[0],parameters))
            using(var b=AssemblyDefinition.ReadAssembly(args[1],parameters))
            {
                var left=Image(a);var right=Image(b);
                var different=left.Keys.Union(right.Keys).Where(k=>!left.ContainsKey(k)||!right.ContainsKey(k)||left[k]!=right[k]).ToArray();
                foreach(var key in different.Take(25)) Console.WriteLine("DIFF: "+key);
                if(different.Length!=0) throw new Exception(different.Length+" executable/metadata entries differ.");
                Console.WriteLine("PASS: "+left.Count+" assembly/reference/type/field/method entries match the supplied test build, including method bodies and attributes. Only PE timestamps/MVID/compiler GUID spelling excluded.");
                return 0;
            }
        }
        catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
    }
    private static string Body(MethodDefinition method)
    {
        if (!method.HasBody) return "no body";
        var body = method.Body;
        return body.InitLocals + ":" + String.Join(",", body.Variables.Select(v => v.VariableType.FullName).ToArray()) + "\n" +
            String.Join("\n", body.Instructions.Select(i => i.OpCode.Name + ":" + Operand(i.Operand, body)).ToArray()) + "\n" +
            String.Join("\n", body.ExceptionHandlers.Select(h => h.HandlerType + ":" +
                Operand(h.TryStart, body) + ":" + Operand(h.TryEnd, body) + ":" + Operand(h.HandlerStart, body) + ":" +
                Operand(h.HandlerEnd, body) + ":" + Operand(h.FilterStart, body) + ":" + (h.CatchType == null ? "" : h.CatchType.FullName)).ToArray());
    }
    private static string Operand(object value, MethodBody body)
    {
        if (value == null) return "";
        var branch = value as Instruction;
        if (branch != null) return "@" + body.Instructions.IndexOf(branch);
        var branches = value as Instruction[];
        if (branches != null) return String.Join(",", branches.Select(b => Operand(b, body)).ToArray());
        var arrayData = value as FieldReference;
        if (arrayData != null && arrayData.DeclaringType.Name.StartsWith("<PrivateImplementationDetails>", StringComparison.Ordinal))
            return "array-data:" + Convert.ToBase64String(arrayData.Resolve().InitialValue);
        var member = value as MemberReference;
        if (member != null) return Regex.Replace(member.FullName, @"\{[0-9A-Fa-f-]{36}\}", "{compiler-id}");
        var local = value as VariableDefinition;
        if (local != null) return "local:" + local.Index;
        var parameter = value as ParameterDefinition;
        if (parameter != null) return "arg:" + parameter.Index;
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

}


