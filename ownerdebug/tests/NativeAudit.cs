using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
class NativeAudit
{
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static IEnumerable<TypeDefinition> Types(TypeDefinition t) { yield return t; foreach (var n in t.NestedTypes) foreach (var v in Types(n)) yield return v; }
    static void Main(string[] args)
    {
        try
        {
            using (var addon = AssemblyDefinition.ReadAssembly(args[0]))
            using (var native = AssemblyDefinition.ReadAssembly(args[1]))
            {
                var types = addon.MainModule.Types.SelectMany(Types).ToArray();
                var methods = types.SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
                var calls = methods.SelectMany(m => m.Body.Instructions).Select(i => i.Operand as MethodReference).Where(m => m != null).ToArray();
                Check(addon.Name.Version.ToString(3) == "1.0.10", "Verified patch version");
                Check(!addon.MainModule.AssemblyReferences.Any(a => a.Name.Contains("PUP") || a.Name.Contains("Ghetto") || a.Name.Contains("BotanicServer") || a.Name.Contains("BotanicPup")), "Optional standalone client");
                foreach (var method in methods)
                foreach (var ins in method.Body.Instructions)
                {
                    var field = ins.Operand as FieldReference;
                    if (ins.OpCode.Code == Code.Stfld || ins.OpCode.Code == Code.Stsfld)
                        Check(field != null && field.DeclaringType.FullName.StartsWith("BotanicOwnerDebug."), "No external/native field write: " + field);
                    var call = ins.Operand as MethodReference; if (call == null) continue;
                    string t = call.DeclaringType.FullName, n = call.Name;
                    if (t == "ZDO") Check(n == "GetOwner" || n == "IsValid", "No ZDO write");
                    if (t == "ZNetView") Check(n == "IsValid" || n == "GetZDO", "No native-view write/RPC");
                    Check(t != "UnityEngine.Collider" && t != "UnityEngine.Rigidbody", "No physics/collider calls");
                    if (t == "UnityEngine.Transform") Check(!n.StartsWith("set_"), "No position/rotation/scale writes");
                    if (t == "UnityEngine.GameObject") Check(n.StartsWith("get_"), "No native GO mutation");
                    if (t == "UnityEngine.Renderer")
                    {
                        Check(n.StartsWith("get_") || n == "GetSharedMaterials", "No renderer enable/material/property block mutations");
                        if (n == "set_sharedMaterials") Check(method.DeclaringType.FullName == "BotanicOwnerDebug.NativeColours" && (method.Name == "Apply" || method.Name == "Restore"), "Only guarded material slot assignments");
                    }
                    Check(t != "UnityEngine.Graphics", "No regular Graphics submissions");
                    if (t == "UnityEngine.Rendering.CommandBuffer") Check(n == ".ctor" || n == "set_name" || n == "Clear" || n == "Release" || n == "DrawRenderer", "Only native renderer reference draw; no matrices/meshes/targets/blits");
                    Check(!(t == "UnityEngine.SkinnedMeshRenderer" && n == "BakeMesh"), "No pose baking");
                    Check(!(t == "UnityEngine.Mesh" && n == ".ctor"), "No duplicate meshes");
                    Check(!(t == "UnityEngine.Material" && n == "CopyPropertiesFromMaterial"), "No recurring native property copies");
                    Check(!(t == "UnityEngine.Object" && (n == "Instantiate" || n == "DontDestroyOnLoad")), "No object/prefab clones");
                    Check(!(n == "InvokeRPC" || t == "ZRpc" || t == "ZRoutedRpc"), "No network RPC");
                    if (t == "UnityEngine.Object" && n == "Destroy") Check(method.DeclaringType.FullName == "BotanicOwnerDebug.NativeColours" && (method.Name == "Clear" || method.Name == "Sweep"), "Only own material clone disposal");
                }
                var hooks = types.Single(t => t.Name == "DebugHooks");
                foreach (string name in new[] { "Before", "After" })
                {
                    var m = hooks.Methods.Single(x => x.Name == name);
                    Check(m.ReturnType.FullName == "System.Void" && m.Parameters.All(p => !p.ParameterType.IsByReference), "Hook cannot cancel native or modify args: " + name);
                    Check(!m.Body.Instructions.Any(i => i.OpCode.Name.StartsWith("stelem")), "Hook never writes Harmony arguments");
                }
                Check(calls.Any(m => m.DeclaringType.FullName == "ZDO" && m.Name == "GetOwner") && !calls.Any(m => m.Name.Contains("Creator")), "Actual current owner read");
                Check(calls.Any(m => m.DeclaringType.FullName == "UnityEngine.Material" && m.Name == ".ctor" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "UnityEngine.Shader"), "Only shared owned flat materials");
                Check(!calls.Any(m => m.DeclaringType.FullName == "UnityEngine.Material" && m.Name == ".ctor" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "UnityEngine.Material"), "No source material clones");
                Check(calls.Count(m => m.DeclaringType.FullName == "UnityEngine.Rendering.CommandBuffer" && m.Name == "DrawRenderer") == 1, "Single native renderer draw site");
                Check(!calls.Any(m => m.Name == "SetViewProjectionMatrices" || m.Name == "get_localToWorldMatrix" || m.Name == "BakeMesh" || m.Name == "DrawMesh"), "No raw geometry/pose/matrix path");
                Check(calls.Any(m => m.Name == "UnpatchSelf") && !calls.Any(m => m.Name == "UnpatchAll"), "Only own hooks removed");
                var strings = methods.SelectMany(m => m.Body.Instructions).Where(i => i.OpCode.Code == Code.Ldstr).Select(i => (string)i.Operand).ToArray();
                Check(!strings.Contains("_EmissionColor") && !strings.Contains("_EMISSION") && strings.Contains("Hidden/Internal-Colored") && !strings.Contains("Standard"), "PUP flat shader, no PBR/emission");
                Check(!calls.Any(m => m.Name == "GetPropertyBlock" || m.Name == "SetPropertyBlock"), "Foreign property blocks untouched");
                Check(!calls.Any(m => m.Name == "get_material" || m.Name == "get_materials"), "No auto-created renderer material instances");
                Check(calls.Where(m => m.DeclaringType.FullName == "UnityEngine.Material" && m.Name == "SetColor").All(m => m.Parameters[0].ParameterType.FullName == "System.Int32"), "Only cached colour property ID");
                var generic = methods.SelectMany(m => m.Body.Instructions).Select(i => i.Operand as GenericInstanceMethod).Where(m => m != null).SelectMany(m => m.GenericArguments).Select(t => t.FullName).ToArray();
                Check(generic.Contains("Piece") && generic.Contains("WearNTear") && generic.Contains("Character") && generic.Contains("Player"), "Native building/mob scope with explicit player exclusion");
                var playerType = native.MainModule.Types.Single(t => t.Name == "Player");
                bool isCharacter = false;
                for (var parent = playerType.BaseType; parent != null; )
                {
                    if (parent.FullName == "Character") { isCharacter = true; break; }
                    var definition = native.MainModule.Types.FirstOrDefault(t => t.FullName == parent.FullName);
                    if (definition == null) break;
                    parent = definition.BaseType;
                }
                Check(isCharacter, "Native Player hierarchy derives from Character");
                var reg = native.MainModule.Types.Single(t => t.Name == "ZNetScene").Fields.Single(f => f.Name == "m_instances");
                var dict = reg.FieldType as GenericInstanceType;
                Check(dict != null && dict.ElementType.FullName.StartsWith("System.Collections.Generic.Dictionary") && dict.GenericArguments.Count == 2 && dict.GenericArguments[0].FullName == "ZDO" && dict.GenericArguments[1].FullName == "ZNetView", "Native registry type");
                var add = native.MainModule.Types.Single(t => t.Name == "ZNetScene").Methods.Single(m => m.Name == "AddInstance");
                Check(add.Parameters.Count == 2 && add.Parameters[0].ParameterType.FullName == "ZDO" && add.Parameters[1].ParameterType.FullName == "ZNetView", "Native AddInstance arguments");
                foreach (string name in new[] { "ResetZDO", "OnDestroy" }) Check(native.MainModule.Types.Single(t => t.Name == "ZNetView").Methods.Count(m => m.Name == name && m.Parameters.Count == 0) == 1, "Native cleanup hook " + name);
                var zdo = native.MainModule.Types.Single(t => t.Name == "ZDO");
                Check(zdo.Methods.Count(m => m.Name == "SetOwnerInternal" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int64") == 1, "Native ownership hook");
                Check(zdo.Methods.Count(m => m.Name == "set_OwnerRevision" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.UInt16") == 1, "Native replicated revision hook");
                Check(native.MainModule.Types.Single(t => t.Name == "ZNet").Methods.Count(m => m.Name == "Shutdown" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Boolean") == 1, "Native shutdown hook");
                var net = native.MainModule.Types.Single(t => t.Name == "ZNet");
                var info = net.NestedTypes.Single(t => t.Name == "PlayerInfo");
                Check(info.Fields.Single(f => f.Name == "m_name").FieldType.FullName == "System.String" && info.Fields.Single(f => f.Name == "m_characterID").FieldType.FullName == "ZDOID", "Native player roster name and character ID contract");
                var roster = net.Methods.Single(m => m.Name == "GetPlayerList" && m.Parameters.Count == 0).ReturnType as GenericInstanceType;
                Check(roster != null && roster.ElementType.FullName == "System.Collections.Generic.List`1" && roster.GenericArguments.Single().FullName == info.FullName, "Read existing synchronised player roster, no player enumeration/RPC");
                Check(native.MainModule.Types.Single(t => t.Name == "Interactable").IsInterface, "Native interaction criterion is real Interactable interface");
                Check(native.MainModule.Types.Single(t => t.Name == "Container").Fields.Single(f => f.Name == "m_rootObjectOverride").FieldType.FullName == "ZNetView", "Native container root ownership override contract");
                Check(methods.SelectMany(m => m.Body.Instructions).Any(i => (i.Operand as TypeReference) != null && ((TypeReference)i.Operand).FullName == "Interactable"), "Filter detects real interaction capability");
                Check(!calls.Any(m => m.DeclaringType.FullName == "Interactable"), "Read-only capability detection never calls gameplay interaction");
                Check(calls.Any(m => m.DeclaringType.FullName == "BotanicOwnerDebug.DisplayModes" && m.Name == "Current") && calls.Any(m => m.DeclaringType.FullName == "BotanicOwnerDebug.DisplayModes" && m.Name == "Next"), "Production plugin uses tested four-state cycle");
                Check(strings.Contains("InteractiveObjectsOnly") && strings.Contains("MobsOnly") && strings.Contains("Mode: "), "Config filter and current mode display are present");
                var plugin = types.Single(t => t.Name == "Plugin");
                Check(!plugin.Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => (i.Operand as MethodReference) != null && ((MethodReference)i.Operand).Name == "GetComponentsInChildren"), "Permanent Update contains no model scans");
                System.Console.WriteLine("PASS: " + checks + " actual-DLL/native-contract assertions.");
            }
        }
        catch (Exception e) { System.Console.Error.WriteLine(e); Environment.Exit(1); }
    }
}

