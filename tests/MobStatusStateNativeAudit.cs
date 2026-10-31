using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Mono.Cecil;

internal static class MobStatusStateNativeAudit
{
    private const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static int checks;private static string[] paths;
    private static void Check(bool value,string text){checks++;if(!value)throw new Exception(text);}
    private static Assembly Resolve(object sender,ResolveEventArgs args)
    {foreach(string path in paths){string file=Path.Combine(path,new AssemblyName(args.Name).Name+".dll");if(File.Exists(file))return Assembly.LoadFrom(file);}return null;}
    private static FieldInfo Field(Type type,string name){for(Type current=type;current!=null;current=current.BaseType){FieldInfo f=current.GetField(name,All|BindingFlags.DeclaredOnly);if(f!=null)return f;}throw new Exception("Missing "+type.Name+"."+name);}
    private static bool Called(MethodDefinition method,string type,string name)
    {return method.HasBody&&method.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name==type&&((MethodReference)i.Operand).Name==name);}
    public static void Main(string[] args)
    {
        try
        {
            paths=new[]{args[1],args[2],Path.GetDirectoryName(args[0])};AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
            Assembly game=Assembly.LoadFrom(Path.Combine(args[1],"assembly_valheim.dll")),addon=Assembly.LoadFrom(args[0]);Type module=addon.GetType("TooManyLags.MobStatusState",true);
            MethodInfo describe=module.GetMethod("Describe",All),write=module.GetMethod("Write",All),read=module.GetMethod("Read",All);
            try
            {
                module.GetMethod("Initialize",All).Invoke(null,null);Check(module.GetField("hooks",All).GetValue(null)!=null,"actual status native hook installation failed");
                foreach(string name in new[]{"StatusEffect","SE_Demister","SE_Harpooned"})Check(game.GetType(name)!=null,"native adapter type absent");
                Check(Harmony.GetPatchInfo(game.GetType("StatusEffect").GetMethod("RemoveStartEffects",All)).Prefixes.Any(p=>p.PatchMethod.DeclaringType==module),"delayed start FX expiry hook missing");
                Check(Harmony.GetPatchInfo(game.GetType("SE_Demister").GetMethod("UpdateStatusEffect",All)).Prefixes.Any(p=>p.PatchMethod.DeclaringType==module),"existing demister ball duplicate-spawn hook missing");
                Check(Harmony.GetPatchInfo(game.GetType("SE_Harpooned").GetMethod("IsDone",All)).Prefixes.Any(p=>p.PatchMethod.DeclaringType==module),"temporarily missing harpoon attacker expiry hook missing");
                Patches destroyed=Harmony.GetPatchInfo(game.GetType("ZDOMan").GetMethod("HandleDestroyedZDO",All));
                Check(destroyed!=null&&(destroyed.Prefixes.Any(p=>p.PatchMethod.DeclaringType==module)||destroyed.Postfixes.Any(p=>p.PatchMethod.DeclaringType==module)),"native destroyed-reference notification hook missing");
                Type status=game.GetType("StatusEffect");
                foreach(Type type in game.GetTypes().Where(t=>!t.IsAbstract&&status.IsAssignableFrom(t)))
                {
                    object schema=describe.Invoke(null,new object[]{type});IList values=(IList)schema.GetType().GetField("Values",All).GetValue(schema),references=(IList)schema.GetType().GetField("References",All).GetValue(schema);
                    int total=0;for(Type current=type;current!=null&&status.IsAssignableFrom(current);current=current.BaseType)total+=current.GetFields(All|BindingFlags.DeclaredOnly).Count(f=>!f.IsStatic);
                    Check(values.Count+references.Count==total,"native status field omitted from schema: "+type.Name);
                    object instance=FormatterServices.GetUninitializedObject(type);
                    foreach(object item in values)
                    {
                        Type fieldType=item.GetType();FieldInfo info=(FieldInfo)fieldType.GetField("Info",All).GetValue(item);Delegate get=(Delegate)fieldType.GetField("Get",All).GetValue(item),set=(Delegate)fieldType.GetField("Set",All).GetValue(item);
                        object value=info.FieldType==typeof(string)?"handoff":info.FieldType==typeof(float)?(object)1.25f:info.FieldType==typeof(int)?(object)7:info.FieldType.IsValueType?Activator.CreateInstance(info.FieldType):null;
                        set.DynamicInvoke(instance,value);Check(Object.Equals(get.DynamicInvoke(instance),value),"actual native field delegate mismatch: "+type.Name+"."+info.Name);
                        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
                        {write.Invoke(null,new[]{(object)writer,info.FieldType,value});stream.Position=0;using(var reader=new BinaryReader(stream))Check(Object.Equals(read.Invoke(null,new[]{(object)reader,info.FieldType}),value),"actual native scalar/struct codec mismatch: "+info.Name);}
                    }
                }
            }
            finally{module.GetMethod("Stop",All).Invoke(null,null);}
            using(var image=AssemblyDefinition.ReadAssembly(args[0]))using(var native=AssemblyDefinition.ReadAssembly(Path.Combine(args[1],"assembly_valheim.dll")))
            {
                var type=image.MainModule.Types.Single(t=>t.FullName==module.FullName);
                foreach(var method in type.Methods.Where(m=>m.HasBody))foreach(var ins in method.Body.Instructions)
                {
                    var call=ins.Operand as MethodReference;if(call==null)continue;
                    Check(!new[]{"Setup","ResetTime","SetLevel","AddStatusEffect","ApplyDamage","SetHealth","Heal","Instantiate","SetAttacker"}.Contains(call.Name),"status migration replays gameplay/spawns: "+call.FullName);
                }
                Check(!type.Methods.Any(m=>m.Name=="Update"||m.Name=="Tick"),"status migration added global polling");
                var restore=type.Methods.Single(m=>m.Name=="Restore");var calls=restore.Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>(MethodReference)i.Operand).ToList();
                Check(calls.FindIndex(c=>c.Name=="Decode")<calls.FindIndex(c=>c.Name=="Detach"),"decode occurs after old manager mutation");
                var stats=native.MainModule.Types.Single(t=>t.Name=="SE_Stats");Check(Called(stats.Methods.Single(m=>m.Name=="Setup"),"SE_Stats","StartupEffects"),"native stats upfront effect contract changed");
                foreach(string pair in new[]{"SE_Poison:m_damageLeft","SE_Poison:m_timer","SE_Burning:m_fireDamageLeft","SE_Burning:m_spiritDamageLeft","SE_Shield:m_damage","SE_Stats:m_healthOverTimeTicks","SE_Spawn:m_spawned"})
                {string[] parts=pair.Split(':');Check(native.MainModule.Types.Single(t=>t.Name==parts[0]).Fields.Any(f=>f.Name==parts[1]),"native runtime state absent: "+pair);}
                Check(Called(native.MainModule.Types.Single(t=>t.Name=="StatusEffect").Methods.Single(m=>m.Name=="Clone"),"Object","MemberwiseClone"),"native clone no longer shallow field-only clone");
            }
            Console.WriteLine("PASS: "+checks+" actual status-state contracts: all native effect fields classified, field delegates/struct codecs, six Harmony hooks, no Setup or reward replay.");
            Console.WriteLine("LIMIT: game method hooks installed/removed headlessly; actual Unity objects, FX, physics and multiplayer timing require in-game tests.");
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
}
