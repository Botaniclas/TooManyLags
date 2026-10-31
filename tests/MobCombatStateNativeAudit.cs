using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MobCombatStateNativeAudit
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
            // Harmony's Mono detour asks for a Character function pointer, which
            // runs Unity-only static Animator hash initializers. A disposable
            // test copy omits Character/Player/MonsterAI initializers. Every patched native method
            // body, field and signature remains byte-for-byte native.
            string sandbox=Path.Combine(Path.GetDirectoryName(typeof(MobCombatStateNativeAudit).Assembly.Location),"native-static-seam");Directory.CreateDirectory(sandbox);
            string gameCopy=Path.Combine(sandbox,"assembly_valheim.dll");
            using(var copy=AssemblyDefinition.ReadAssembly(Path.Combine(args[1],"assembly_valheim.dll")))
            {
                foreach(string name in new[]{"Character","Player","MonsterAI"})
                {
                    var init=copy.MainModule.Types.Single(t=>t.Name==name).Methods.Single(m=>m.Name==".cctor");
                    init.Body.Instructions.Clear();init.Body.ExceptionHandlers.Clear();init.Body.Variables.Clear();init.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                }
                copy.Write(gameCopy);
            }
            paths=new[]{sandbox,args[1],args[2],Path.GetDirectoryName(args[0]),Path.GetDirectoryName(args[3])};AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
            Assembly game=Assembly.LoadFrom(gameCopy),addon=Assembly.LoadFrom(args[0]),fgn=Assembly.LoadFrom(args[3]);
            Type module=addon.GetType("TooManyLags.MobCombatState",true);
            try
            {
                module.GetMethod("Start",All).Invoke(null,new object[]{fgn});Check(module.GetField("hooks",All).GetValue(null)!=null,"actual native fields/RPC/Harmony installation failed");
                foreach(string name in new[]{"m_pushForce","m_staggerDamage","m_backstabTime","m_lastHit","m_nview","m_currentVel","m_currentTurnVel","m_currentTurnVelChange","currentRotSpeedFactor"})Check(game.GetType("Character").GetField(name,All)!=null,"native transient field missing: "+name);
                var damage=game.GetType("Character").GetMethod("RPC_Damage",All);var effect=game.GetType("SEMan").GetMethod("RPC_AddStatusEffect",All);
                var stagger=game.GetType("Character").GetMethod("RPC_Stagger",All);var heal=game.GetType("Character").GetMethod("RPC_Heal",All);
                foreach(MethodInfo rpc in new[]{damage,effect,stagger,heal})
                {var patch=Harmony.GetPatchInfo(rpc).Prefixes.Single(p=>p.PatchMethod.DeclaringType==module);Check(patch.priority==Priority.First&&patch.before.Contains("com.Fire.FiresGhettoNetworkMod"),"stale RPC prefix does not precede FGN duplicate-forward hook");}
                Check(Harmony.GetPatchInfo(game.GetType("Character").GetMethod("CustomFixedUpdate",All)).Prefixes.Any(p=>p.PatchMethod.DeclaringType==module),"first destination native physics step unprotected");
                Check(Harmony.GetPatchInfo(game.GetType("MonsterAI").GetMethod("UpdateAI",All)).Prefixes.Any(p=>p.PatchMethod.DeclaringType==module),"first destination AI step unprotected");
                foreach(string name in new[]{"m_targetCreature","m_targetStatic","m_updateTargetTimer"})Check(game.GetType("MonsterAI").GetField(name,All)!=null,"native target field missing: "+name);
                Check(Harmony.GetPatchInfo(game.GetType("ZNetView").GetMethod("HandleRoutedRPC",All)).Finalizers.Any(p=>p.PatchMethod.DeclaringType==module),"broadcast/owner-addressed stagger scope not exception safe");
                Check(Harmony.GetPatchInfo(fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership").GetMethod("OnDamage",All)).Prefixes.Any(p=>p.PatchMethod.DeclaringType==module),"FGN secondary damage-forward suppression absent");
                var veto=Harmony.GetPatchInfo(fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership").GetMethod("Veto",All));
                Check(veto==null||!veto.Postfixes.Any(p=>p.PatchMethod.DeclaringType==module),"combat state still installs an active-effect ownership veto");
                Check(game.GetType("SEMan").GetMethod("GetStatusEffects",All).ReturnType.GetGenericTypeDefinition()==typeof(System.Collections.Generic.List<>),"active effect collection contract changed");
            }
            finally{module.GetMethod("Stop",All).Invoke(null,null);}
            using(var image=AssemblyDefinition.ReadAssembly(args[0]))using(var native=AssemblyDefinition.ReadAssembly(Path.Combine(args[1],"assembly_valheim.dll")))
            {
                var type=image.MainModule.Types.Single(t=>t.FullName==module.FullName);
                var apply=type.Methods.Single(m=>m.Name=="Apply");
                Check(apply.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.FullName=="TooManyLags.MobStatusState"&&((MethodReference)i.Operand).Name=="Restore"),"effect checkpoint not restored before destination gameplay");
                foreach(var ins in apply.Body.Instructions)
                {
                    var call=ins.Operand as MethodReference;if(call==null)continue;
                    Check(!new[]{"RPC_Damage","Damage","SetHealth","AddStatusEffect","SetOwner","TakeInput","UpdateAI"}.Contains(call.Name),"checkpoint replays damage or performs unrelated gameplay: "+call.FullName);
                }
                foreach(var method in type.Methods.Where(m=>m.HasBody))foreach(var ins in method.Body.Instructions)
                {var call=ins.Operand as MethodReference;if(call==null)continue;Check(!(call.DeclaringType.Name=="ZDO"&&call.Name=="Set"),"combat state expanded every steady-state ZDO update");}
                var character=native.MainModule.Types.Single(t=>t.Name=="Character");var nativeDamage=character.Methods.Single(m=>m.Name=="RPC_Damage");
                Check(nativeDamage.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldc_R4&&(float)i.Operand==300f),"native backstab period changed");
                Check(nativeDamage.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsOwner"),"native damage owner gate changed");
                Check(!character.Methods.Single(m=>m.Name=="RPC_Stagger").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsOwner"),"native stagger contract changed; re-audit addressed-only routing");
                Check(character.Methods.Single(m=>m.Name=="RPC_Heal").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsOwner"),"native healing owner gate changed");
                Check(native.MainModule.Types.Single(t=>t.Name=="SEMan").Methods.Single(m=>m.Name=="RPC_AddStatusEffect").Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="IsOwner"),"native effect owner gate changed");
                Check(!type.Methods.Any(m=>m.Name=="Update"||m.Name=="Tick"),"combat continuity adds permanent timer polling");
            }
            Console.WriteLine("PASS: "+checks+" actual combat-state native contracts: private fields, four RPCs, routed-address scope, FGN duplicate suppression, backstab period and no health replay.");
            Console.WriteLine("LIMIT: disposable native copy omits Character/Player/MonsterAI Unity-only static initializers; all patched method bodies remain native. No live Unity transport/physics claims.");
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
}
