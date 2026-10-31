using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MobObserverNativeAudit
{
    private const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static string[] paths;private static int checks;
    private static Assembly Resolve(object sender,ResolveEventArgs args)
    {foreach(string path in paths){string file=Path.Combine(path,new AssemblyName(args.Name).Name+".dll");if(File.Exists(file))return Assembly.LoadFrom(file);}return null;}
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    public static void Main(string[] args)
    {
        try
        {
            paths=new[]{args[1],args[2],Path.GetDirectoryName(args[0]),Path.GetDirectoryName(args[3])};AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
            Assembly plugin=Assembly.LoadFrom(args[0]),game=Assembly.LoadFrom(Path.Combine(args[1],"assembly_valheim.dll"));
            Type bridge=plugin.GetType("TooManyLags.MobObserverHandoff",true),common=plugin.GetType("TooManyLags.MobFlightHandoff",true);
            MethodInfo[] targets={common.GetMethod("Transfer",All),game.GetType("ZNet").GetMethod("OnNewConnection",All),
                game.GetType("ZNet").GetMethod("RPC_PeerInfo",All),game.GetType("ZNet").GetMethod("Shutdown",All),
                game.GetType("ZDOMan").GetMethod("RPC_ZDOData",All),game.GetType("BaseAI").GetMethod("Awake",All)};
            string[] hooks={"BeforeTransfer","Connected","PeerReady","Reset","Tick","Awoke"};
            Harmony patch=new Harmony("tml.observer.native.audit");
            try
            {
                for(int i=0;i<targets.Length;i++)
                {
                    HarmonyMethod method=new HarmonyMethod(bridge.GetMethod(hooks[i],All));
                    patch.Patch(targets[i],prefix:i==0||i==3?method:null,postfix:i==0?new HarmonyMethod(bridge.GetMethod("AfterTransfer",All)):i==3?null:method);
                    Patches info=Harmony.GetPatchInfo(targets[i]);Check(info.Owners.Contains(patch.Id),"native observer hook installation/JIT failed: "+hooks[i]);
                }
            }
            finally{patch.UnpatchSelf();}
            foreach(MethodInfo target in targets)Check(!Harmony.GetPatchInfo(target).Owners.Contains(patch.Id),"observer native hook was not removed");
            Type packetType=plugin.GetType("TooManyLags.MobBridgeObserverPacket");
            if(packetType!=null){RecordedPackets(packetType);RecordedIsolation(plugin);}
            Type point=game.GetType("ZNetPeer").GetMethod("GetRefPos",All).ReturnType;
            Check(game.GetType("ZNetScene").GetMethods(All).Any(m=>m.Name=="InActiveArea"&&m.IsStatic&&m.GetParameters().Length==2&&m.GetParameters().All(p=>p.ParameterType==point)),"native observer active-area contract changed");
            using(AssemblyDefinition addon=AssemblyDefinition.ReadAssembly(args[0]))
            {
                TypeDefinition module=addon.MainModule.Types.Single(t=>t.FullName=="TooManyLags.MobObserverHandoff");
                string[] forbidden={"SetOwner","ForceSendZDO","SyncNow","StartAttack","UpdateAI","UpdateAttack","Simulate","SyncTransforms","SetVelocity","set_position","set_rotation","set_linearVelocity","set_isKinematic"};
                foreach(MethodDefinition method in module.Methods.Where(m=>m.HasBody))foreach(Instruction instruction in method.Body.Instructions)
                {
                    MethodReference call=instruction.Operand as MethodReference;if(call==null)continue;
                    Check(!forbidden.Contains(call.Name),"observer descriptor writes native gameplay, physics or ownership: "+call.FullName);
                    Check(!(call.Name=="Invoke"&&(call.DeclaringType.Name=="ZNetView"||call.DeclaringType.Name=="ZRoutedRpc")),"observer sends a new gameplay routed RPC");
                }
                Check(module.Fields.Any(f=>f.Name=="Limit"&&(int)f.Constant==256),"observer pending references have no explicit bound");
                Check(module.Fields.Any(f=>f.Name=="Lifetime"&&(double)f.Constant==5.0),"observer pending references have no explicit five-second lifetime");
                TypeDefinition transition=module.NestedTypes.Single(t=>t.Name=="Transition");
                Check(transition.Fields.Any(f=>f.Name=="Cutoff"&&f.FieldType.FullName=="System.UInt32"),"native data cutoff absent from descriptor");
                Check(transition.Fields.Any(f=>f.Name=="RotationFactor"&&f.FieldType.FullName=="System.Single"),"source flight turn accumulator absent from descriptor");
                MethodDefinition capture=module.Methods.Single(m=>m.Name=="BeforeTransfer");
                Check(!capture.Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="m_randomFly"),"grounded random-flight species must not be treated as airborne");
            }
            Console.WriteLine("PASS: "+checks+" actual addon/game observer contracts and native Harmony install/uninstall under Valheim Mono.");
            Console.WriteLine("LIMIT: hooks installed but not executed in a Unity world; no live transport or render quality assertion.");
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
    private static void RecordedPackets(Type packetType)
    {
        object packet=Activator.CreateInstance(packetType,true);
        Action<string,object> set=delegate(string name,object value){packetType.GetField(name,All).SetValue(packet,value);};
        set("Source",1L);set("Destination",2L);set("Revision",7U);set("Token",42U);
        set("Bones",1);set("Paths",new[]{""});set("Window",.1f);set("Rate",1f);set("AiRate",1f);
        Type quaternion=packetType.GetField("Rotation",All).FieldType;
        object identity=Activator.CreateInstance(quaternion,new object[]{0f,0f,0f,1f});
        set("Rotation",identity);set("ModelRotation",identity);
        Type sampleType=packetType.GetNestedType("Sample",All);object sample=Activator.CreateInstance(sampleType,true);
        sampleType.GetField("Time",All).SetValue(sample,.02f);
        sampleType.GetField("Rotation",All).SetValue(sample,identity);sampleType.GetField("ModelRotation",All).SetValue(sample,identity);
        ((IList)packetType.GetField("Samples",All).GetValue(packet)).Add(sample);
        for(byte kind=0;kind<=5;kind++)
        {
            set("Kind",kind);set("End",.1f);
            object encoded=packetType.GetMethod("Encode",All).Invoke(packet,null);
            encoded.GetType().GetMethod("SetPos",All).Invoke(encoded,new object[]{0});
            object decoded=packetType.GetMethod("Decode",All).Invoke(null,new[]{encoded});
            Check((byte)packetType.GetField("Kind",All).GetValue(decoded)==kind,"native ZPackage recorded observer kind roundtrip");
            Check((bool)packetType.GetMethod("Same",All).Invoke(decoded,new object[]{1L,2L,7U,42U}),"native ZPackage recorded observer epoch roundtrip");
        }
    }
    private static void RecordedIsolation(Assembly plugin)
    {
        Type animation=plugin.GetType("TooManyLags.MobBridgeAnimation",true);
        var source=new[]{"","4:Body:0/","4:Body:0/4:Head:0/","6:Weapon:0/"};
        var local=new[]{"","2:FX:0/","4:Body:0/","4:Body:0/4:Head:0/","7:ExtraFX:0/"};
        int[] map=(int[])animation.GetMethod("MapRig",All).Invoke(null,new object[]{source,local});
        Check(map.SequenceEqual(new[]{0,2,3,-1}),"extra FX/absent equipment corrupt source-to-observer bone mapping");
        Type visual=plugin.GetType("TooManyLags.MobBridgeVisual",true),track=visual.GetNestedType("Track",All);
        IList tracks=(IList)visual.GetField("tracks",All).GetValue(null);
        Check(tracks.Count==0,"native isolation fixture requires an empty presentation session");
        try
        {
            for(int i=0;i<16;i++)
            {object item=Activator.CreateInstance(track,true);track.GetField("Observer",All).SetValue(item,true);tracks.Add(item);}
            MethodInfo capacity=visual.GetMethod("AtCapacity",All);
            Check((bool)capacity.Invoke(null,new object[]{true})&&!(bool)capacity.Invoke(null,new object[]{false}),"observer capacity blocks old-owner preview");
            for(int i=0;i<16;i++)tracks.Add(Activator.CreateInstance(track,true));
            Check((bool)capacity.Invoke(null,new object[]{false}),"source capacity safeguard lost");
            object candidate=tracks[0];Type zdo=track.GetField("Data",All).FieldType;
            object data=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(zdo);
            object id=zdo.GetField("m_uid",All).GetValue(data);
            track.GetField("Data",All).SetValue(candidate,data);track.GetField("Revision",All).SetValue(candidate,7U);track.GetField("Token",All).SetValue(candidate,42U);
            MethodInfo contains=visual.GetMethod("Contains",All);
            Check(!(bool)contains.Invoke(null,new[]{id,(object)7U,42U}),"observer masquerades as successful source preview");
            track.GetField("Observer",All).SetValue(candidate,false);
            Check((bool)contains.Invoke(null,new[]{id,(object)7U,42U})&&!(bool)contains.Invoke(null,new[]{id,(object)8U,42U})&&
                !(bool)contains.Invoke(null,new[]{id,(object)7U,43U}),"source presence ignores ownership epoch/token");
        }
        finally{tracks.Clear();}
    }
}
