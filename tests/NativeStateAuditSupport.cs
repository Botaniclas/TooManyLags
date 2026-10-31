using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;

// Executes the actual native RPC_ZDOData IL, not a reimplementation.
// Unity clock, missing-object creation, portal registration and server role
// are environment seams; receiver decision/Deserialize IL stays original.
internal static class NativeOwnerReceiveAudit
{
    private const BindingFlags Fields=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static string[] search;
    private static Assembly game;
    private static Type worldType,dataType,idType,rpcType,pkgType,peerType;
    private static int checks;
    private sealed class Instruction { internal int Offset; internal OpCode opcode; internal object operand; }
    private static List<Instruction> Read(MethodInfo original)
    {
        var opcodes=new Dictionary<ushort,OpCode>();foreach(FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static))
            if(field.FieldType==typeof(OpCode)){OpCode code=(OpCode)field.GetValue(null);opcodes[(ushort)code.Value]=code;}
        var result=new List<Instruction>();byte[] bytes=original.GetMethodBody().GetILAsByteArray();Module module=original.Module;
        using(var reader=new BinaryReader(new MemoryStream(bytes)))while(reader.BaseStream.Position<bytes.Length)
        {
            int offset=(int)reader.BaseStream.Position;ushort raw=reader.ReadByte();if(raw==254)raw=(ushort)(0xfe00|reader.ReadByte());OpCode op=opcodes[raw];object value=null;
            switch(op.OperandType)
            {
                case OperandType.InlineNone:break;
                case OperandType.ShortInlineI:value=reader.ReadSByte();break;
                case OperandType.InlineI:value=reader.ReadInt32();break;
                case OperandType.InlineI8:value=reader.ReadInt64();break;
                case OperandType.ShortInlineR:value=reader.ReadSingle();break;
                case OperandType.InlineR:value=reader.ReadDouble();break;
                case OperandType.ShortInlineVar:value=reader.ReadByte();break;
                case OperandType.InlineVar:value=reader.ReadInt16();break;
                case OperandType.ShortInlineBrTarget:{int delta=reader.ReadSByte();value=(int)reader.BaseStream.Position+delta;break;}
                case OperandType.InlineBrTarget:{int delta=reader.ReadInt32();value=(int)reader.BaseStream.Position+delta;break;}
                case OperandType.InlineMethod:value=module.ResolveMethod(reader.ReadInt32());break;
                case OperandType.InlineField:value=module.ResolveField(reader.ReadInt32());break;
                case OperandType.InlineType:value=module.ResolveType(reader.ReadInt32());break;
                case OperandType.InlineString:value=module.ResolveString(reader.ReadInt32());break;
                case OperandType.InlineTok:value=module.ResolveMember(reader.ReadInt32());break;
                case OperandType.InlineSwitch:{int count=reader.ReadInt32();int[] targets=new int[count];for(int i=0;i<count;i++)targets[i]=reader.ReadInt32();int end=(int)reader.BaseStream.Position;for(int i=0;i<count;i++)targets[i]+=end;value=targets;break;}
                default:throw new Exception("Unsupported IL operand "+op.OperandType);
            }
            result.Add(new Instruction{Offset=offset,opcode=op,operand=value});
        }
        return result;
    }
    private static Assembly Resolve(object sender,ResolveEventArgs args)
    {foreach(string directory in search){string file=Path.Combine(directory,new AssemblyName(args.Name).Name+".dll");if(File.Exists(file))return Assembly.LoadFrom(file);}return null;}
    private static float Clock(){return 123f;}
    private static DynamicMethod Stub(MethodInfo original,bool returnsFalse)
    {
        var parameters=new List<Type>();if(!original.IsStatic)parameters.Add(original.DeclaringType);
        foreach(ParameterInfo parameter in original.GetParameters())parameters.Add(parameter.ParameterType);
        var stub=new DynamicMethod("Environment_"+original.Name,original.ReturnType,parameters.ToArray(),typeof(NativeOwnerReceiveAudit).Module,true);
        ILGenerator il=stub.GetILGenerator();if(returnsFalse)il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);return stub;
    }
    private static void Set(object target,string field,object value){target.GetType().GetField(field,Fields).SetValue(target,value);}
    private static object Get(object target,string field){return target.GetType().GetField(field,Fields).GetValue(target);}
    private static void Property(object target,string name,object value){target.GetType().GetProperty(name,Fields).SetValue(target,value,null);}
    private static object Property(object target,string name){return target.GetType().GetProperty(name,Fields).GetValue(target,null);}
    private static object New(Type type){return FormatterServices.GetUninitializedObject(type);}
    private static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
    private static DynamicMethod NativeReceiver()
    {
        MethodInfo original=worldType.GetMethod("RPC_ZDOData",Fields);
        Check(original.GetMethodBody().ExceptionHandlingClauses.Count==0,"native receiver has no exception clauses requiring reconstruction");
        var copy=new DynamicMethod("OriginalNativeOwnerReceive",typeof(void),new[]{worldType,rpcType,pkgType},typeof(NativeOwnerReceiveAudit).Module,true);
        ILGenerator il=copy.GetILGenerator();
        var create=new DynamicMethod("MissingObjectBoundary",dataType,new[]{worldType,idType,game.GetType("UnityEngine.Vector3")??Type.GetType("UnityEngine.Vector3, UnityEngine.CoreModule",true),typeof(int)},typeof(NativeOwnerReceiveAudit).Module,true);
        ILGenerator ci=create.GetILGenerator();ci.Emit(OpCodes.Ldstr,"native needs CreateNewZDO");ci.Emit(OpCodes.Newobj,typeof(InvalidOperationException).GetConstructor(new[]{typeof(string)}));ci.Emit(OpCodes.Throw);
        foreach(LocalVariableInfo local in original.GetMethodBody().LocalVariables)il.DeclareLocal(local.LocalType,local.IsPinned);
        List<Instruction> instructions=Read(original);var labels=new Dictionary<int,Label>();
        foreach(Instruction instruction in instructions)
        {
            if(instruction.opcode.OperandType==OperandType.InlineBrTarget||instruction.opcode.OperandType==OperandType.ShortInlineBrTarget)
            {int offset=(int)instruction.operand;if(!labels.ContainsKey(offset))labels.Add(offset,il.DefineLabel());}
            if(instruction.opcode.OperandType==OperandType.InlineSwitch)foreach(int offset in (int[])instruction.operand)if(!labels.ContainsKey(offset))labels.Add(offset,il.DefineLabel());
        }
        int clock=0,creation=0,portals=0,server=0;
        foreach(Instruction instruction in instructions)
        {
            Label label;if(labels.TryGetValue(instruction.Offset,out label))il.MarkLabel(label);
            if(instruction.opcode.OperandType==OperandType.InlineBrTarget||instruction.opcode.OperandType==OperandType.ShortInlineBrTarget)instruction.operand=labels[(int)instruction.operand];
            if(instruction.opcode.OperandType==OperandType.InlineSwitch){int[] offsets=(int[])instruction.operand;Label[] targets=new Label[offsets.Length];for(int i=0;i<offsets.Length;i++)targets[i]=labels[offsets[i]];instruction.operand=targets;}
            MethodInfo method=instruction.operand as MethodInfo;
            if(method!=null&&method.DeclaringType.FullName=="UnityEngine.Time"&&method.Name=="get_time")
            {il.Emit(OpCodes.Call,typeof(NativeOwnerReceiveAudit).GetMethod("Clock",Fields));clock++;continue;}
            if(method!=null&&method.DeclaringType==worldType&&method.Name=="CreateNewZDO")
            {il.Emit(OpCodes.Call,create);creation++;continue;}
            if(method!=null&&method.DeclaringType==worldType&&method.Name=="AddIfPortal")
            {il.Emit(OpCodes.Call,Stub(method,false));portals++;continue;}
            if(method!=null&&method.DeclaringType.FullName=="ZNet"&&method.Name=="IsServer")
            {il.Emit(OpCodes.Call,Stub(method,true));server++;continue;}
            Emit(il,instruction);
        }
        Check(clock==1&&creation==1&&portals==1&&server==1,"only clock, missing-object creation, portal registration and server-role environment boundaries substituted");
        return copy;
    }
    private static object ClosedSocket()
    {
        Type socket=game.GetType("ISocket",true);
        var assembly=(AssemblyBuilder)typeof(AssemblyBuilder).GetMethod("DefineDynamicAssembly",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(AssemblyName),typeof(AssemblyBuilderAccess)},null).Invoke(null,new object[]{new AssemblyName("OwnerAuditSocket"),AssemblyBuilderAccess.Run});
        TypeBuilder type=assembly.DefineDynamicModule("Main").DefineType("ClosedSocket",TypeAttributes.Public);type.AddInterfaceImplementation(socket);
        var interfaces=new List<Type>{socket};interfaces.AddRange(socket.GetInterfaces());var names=new HashSet<string>();
        foreach(Type contract in interfaces)foreach(MethodInfo member in contract.GetMethods())
        {
            string key=member.ToString();if(!names.Add(key))continue;var args=new List<Type>();foreach(ParameterInfo arg in member.GetParameters())args.Add(arg.ParameterType);
            MethodBuilder method=type.DefineMethod(member.Name,MethodAttributes.Public|MethodAttributes.Virtual|MethodAttributes.Final|MethodAttributes.NewSlot,member.ReturnType,args.ToArray());
            ILGenerator il=method.GetILGenerator();for(int i=0;i<args.Count;i++)if(args[i].IsByRef){il.Emit(OpCodes.Ldarg,i+1);il.Emit(OpCodes.Initobj,args[i].GetElementType());}
            if(member.ReturnType!=typeof(void)){if(member.ReturnType.IsValueType){LocalBuilder local=il.DeclareLocal(member.ReturnType);il.Emit(OpCodes.Ldloca,local);il.Emit(OpCodes.Initobj,member.ReturnType);il.Emit(OpCodes.Ldloc,local);}else il.Emit(OpCodes.Ldnull);}
            il.Emit(OpCodes.Ret);type.DefineMethodOverride(method,member);
        }
        return Activator.CreateInstance(type.CreateType());
    }
    private static DynamicMethod NativeSendRecord()
    {
        MethodInfo original=worldType.GetMethod("SendZDOs",Fields);List<Instruction> source=Read(original);int begin=-1,end=-1,current=-1;var locals=original.GetMethodBody().LocalVariables;
        for(int i=0;i<source.Count;i++)
        {
            MethodInfo method=source[i].operand as MethodInfo;
            if(method!=null&&method.Name=="get_Current"&&method.ReturnType==dataType){current=i;}
            FieldInfo field=source[i].operand as FieldInfo;if(current>=0&&begin<0&&field!=null&&field.Name=="m_forceSend")begin=i-1;
            if(begin>=0&&method!=null&&method.Name=="set_Item"&&method.DeclaringType.IsGenericType&&method.DeclaringType.GetGenericArguments()[1].Name=="PeerZDOInfo"){end=i;break;}
        }
        Check(begin>=0&&end>begin,"native send-record block ends after actual peer revision-table assignment");
        var copy=new DynamicMethod("OriginalNativeSendRecord",typeof(void),new[]{worldType,peerType,typeof(bool),dataType},typeof(NativeOwnerReceiveAudit).Module,true);
        ILGenerator il=copy.GetILGenerator();foreach(LocalVariableInfo local in locals)il.DeclareLocal(local.LocalType,local.IsPinned);
        int packages=0;foreach(LocalVariableInfo local in locals)
        {
            if(local.LocalType==dataType){il.Emit(OpCodes.Ldarg_3);il.Emit(OpCodes.Stloc,local.LocalIndex);}
            if(local.LocalType==pkgType){il.Emit(OpCodes.Newobj,pkgType.GetConstructor(Type.EmptyTypes));il.Emit(OpCodes.Stloc,local.LocalIndex);packages++;}
        }
        Check(packages==2,"native send-record locals contain exactly outer/body packages");
        var labels=new Dictionary<int,Label>();for(int i=begin;i<=end;i++)if(source[i].opcode.OperandType==OperandType.ShortInlineBrTarget||source[i].opcode.OperandType==OperandType.InlineBrTarget)
        {int target=(int)source[i].operand;if(target<source[begin].Offset||target>source[end].Offset)throw new Exception("Record branch escapes extracted block");if(!labels.ContainsKey(target))labels.Add(target,il.DefineLabel());}
        for(int i=begin;i<=end;i++)
        {
            Instruction instruction=source[i];Label label;if(labels.TryGetValue(instruction.Offset,out label))il.MarkLabel(label);
            if(instruction.opcode.OperandType==OperandType.InlineBrTarget||instruction.opcode.OperandType==OperandType.ShortInlineBrTarget)instruction.operand=labels[(int)instruction.operand];
            MethodInfo method=instruction.operand as MethodInfo;
            if(method!=null&&method.DeclaringType.FullName=="ZNet"&&method.Name=="IsServer")
            {
                var role=new DynamicMethod("ServerRole",typeof(bool),new[]{method.DeclaringType},typeof(NativeOwnerReceiveAudit).Module,true);
                ILGenerator ri=role.GetILGenerator();ri.Emit(OpCodes.Ldc_I4_1);ri.Emit(OpCodes.Ret);il.Emit(OpCodes.Call,role);continue;
            }
            if(method!=null&&method.DeclaringType==dataType&&method.Name=="Serialize"){il.Emit(OpCodes.Call,Stub(method,false));continue;}
            Emit(il,instruction);
        }
        // The original final call happens only after every record is built.
        // Invoke the native method on a deliberately closed managed socket.
        il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldfld,peerType.GetField("m_peer",Fields));il.Emit(OpCodes.Ldfld,game.GetType("ZNetPeer",true).GetField("m_rpc",Fields));
        il.Emit(OpCodes.Ldstr,"ZDOData");il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Newarr,typeof(object));il.Emit(OpCodes.Callvirt,rpcType.GetMethod("Invoke",new[]{typeof(string),typeof(object[])}));il.Emit(OpCodes.Ret);return copy;
    }
    private static void Emit(ILGenerator il,Instruction code)
    {
        object operand=code.operand;OpCode op=code.opcode;
        if(operand==null)il.Emit(op);
        else if(operand is LocalBuilder)il.Emit(op,(LocalBuilder)operand);
        else if(operand is Label)il.Emit(op,(Label)operand);
        else if(operand is Label[])il.Emit(op,(Label[])operand);
        else if(operand is MethodInfo)il.Emit(op,(MethodInfo)operand);
        else if(operand is ConstructorInfo)il.Emit(op,(ConstructorInfo)operand);
        else if(operand is FieldInfo)il.Emit(op,(FieldInfo)operand);
        else if(operand is Type)il.Emit(op,(Type)operand);
        else if(operand is string)il.Emit(op,(string)operand);
        else if(operand is byte)il.Emit(op,(byte)operand);
        else if(operand is sbyte)il.Emit(op,(sbyte)operand);
        else if(operand is short)il.Emit(op,(short)operand);
        else if(operand is int)il.Emit(op,(int)operand);
        else if(operand is long)il.Emit(op,(long)operand);
        else if(operand is float)il.Emit(op,(float)operand);
        else if(operand is double)il.Emit(op,(double)operand);
        else throw new Exception("Unsupported operand "+operand.GetType());
    }
    private static object World(out object rpc,out object id,out object data,uint revision,ushort ownerRevision,bool missing)
    {
        object world=New(worldType);worldType.GetField("s_instance",Fields).SetValue(null,world);Set(world,"m_sessionID",99L);
        rpc=New(rpcType);id=Activator.CreateInstance(idType,new object[]{770000001L,1U});data=New(dataType);
        Set(data,"m_uid",id);Set(data,"m_prefab",1230001);Property(data,"DataRevision",revision);Property(data,"OwnerRevision",ownerRevision);
        Type extra=game.GetType("ZDOExtraData",true),tableType=extra.GetNestedType("Type",Fields);
        extra.GetMethod("Reserve",Fields).Invoke(null,new object[]{id,Enum.Parse(tableType,"Float"),1});
        extra.GetMethod("Add",Fields,null,new[]{idType,typeof(int),typeof(float)},null).Invoke(null,new object[]{id,4321,-1f});
        dataType.GetMethod("SetOwnerInternal",Fields).Invoke(data,new object[]{1L});
        var objects=(IDictionary)Activator.CreateInstance(worldType.GetField("m_objectsByID",Fields).FieldType);if(!missing)objects.Add(id,data);Set(world,"m_objectsByID",objects);
        var peers=(IList)Activator.CreateInstance(worldType.GetField("m_peers",Fields).FieldType);
        object peer=Activator.CreateInstance(peerType,true);object netPeer=New(game.GetType("ZNetPeer",true));Set(netPeer,"m_rpc",rpc);Set(peer,"m_peer",netPeer);peers.Add(peer);Set(world,"m_peers",peers);
        return world;
    }
    private static byte[] Packet(uint dataRevision,ushort ownerRevision,byte[] body,long owner=99)
    {
        using(var bytes=new MemoryStream())using(var writer=new BinaryWriter(bytes))
        {writer.Write(0);writer.Write(770000001L);writer.Write(1U);writer.Write(ownerRevision);writer.Write(dataRevision);writer.Write(owner);writer.Write(0f);writer.Write(0f);writer.Write(0f);writer.Write(body.Length);writer.Write(body);writer.Write(0L);writer.Write(0U);return bytes.ToArray();}
    }
    private static byte[] Baseline()
    {using(var bytes=new MemoryStream())using(var writer=new BinaryWriter(bytes)){writer.Write((ushort)(256|2));writer.Write(1230001);writer.Write((byte)1);writer.Write(4321);writer.Write(13f);return bytes.ToArray();}}
    private static float Health(object data)
    {return (float)dataType.GetMethod("GetFloat",Fields,null,new[]{typeof(int),typeof(float)},null).Invoke(data,new object[]{4321,-1f});}
    private static Exception Run(DynamicMethod receive,object world,object rpc,byte[] packet)
    {
        object pkg=Activator.CreateInstance(pkgType,new object[]{packet});
        try{receive.Invoke(null,new[]{world,rpc,pkg});return null;}
        catch(TargetInvocationException error){return error.InnerException;}
    }
    private static void Main(string[] args)
    {
        try
        {
            search=new[]{args[0],args[1]};AppDomain.CurrentDomain.AssemblyResolve+=Resolve;game=Assembly.LoadFrom(Path.Combine(args[0],"assembly_valheim.dll"));
            worldType=game.GetType("ZDOMan",true);dataType=game.GetType("ZDO",true);idType=game.GetType("ZDOID",true);rpcType=game.GetType("ZRpc",true);pkgType=game.GetType("ZPackage",true);peerType=worldType.GetNestedType("ZDOPeer",Fields);
            game.GetType("ZDOExtraData",true).GetMethod("Init",Fields).Invoke(null,null);
            DynamicMethod receive=NativeReceiver();object rpc,id,data;
            object world=World(out rpc,out id,out data,7,1,false);
            Check(Run(receive,world,rpc,Packet(7,2,new byte[0]))==null,"equal data revision consumes empty body in original native receiver");
            Check((long)dataType.GetMethod("GetOwner").Invoke(data,null)==99L&&(ushort)Property(data,"OwnerRevision")==2&&(uint)Property(data,"DataRevision")==7,"native owner-only path updates owner/revision and retains data revision");
            Check(Run(receive,world,rpc,Packet(7,1,new byte[0]))==null&&(ushort)Property(data,"OwnerRevision")==2,"stale owner revision does not replace newer owner tuple");
            world=World(out rpc,out id,out data,8,1,false);
            Check(Run(receive,world,rpc,Packet(7,2,new byte[0]))==null&&(uint)Property(data,"DataRevision")==8,"native receiver also retains newer local data on older incoming state revision");
            world=World(out rpc,out id,out data,6,1,false);Exception behind=Run(receive,world,rpc,Packet(7,2,new byte[0]));
            Check(behind is EndOfStreamException,"receiver behind baseline calls actual native Deserialize and rejects empty body");
            Check((uint)Property(data,"DataRevision")==7&&(ushort)Property(data,"OwnerRevision")==2,"failed Deserialize already changed native revision tuple before failure");
            world=World(out rpc,out id,out data,7,1,true);Exception absent=Run(receive,world,rpc,Packet(7,2,new byte[0]));
            Check(absent is InvalidOperationException&&absent.Message=="native needs CreateNewZDO","missing instance enters native creation branch rather than owner-only branch");
            world=World(out rpc,out id,out data,6,1,false);
            Check(Run(receive,world,rpc,Packet(7,2,Baseline(),1))==null&&Health(data)==13f,"actual native Deserialize installs full baseline before subsequent owner-only message");
            Check(Run(receive,world,rpc,Packet(7,3,new byte[0]))==null&&Health(data)==13f&&(long)dataType.GetMethod("GetOwner").Invoke(data,null)==99L,"ordered full baseline then owner-only keeps instance state without extra acknowledgement");
            world=World(out rpc,out id,out data,6,1,false);
            Check(Run(receive,world,rpc,Packet(7,3,new byte[0])) is EndOfStreamException,"owner-only before baseline fails through actual native body reader");
            Check(Run(receive,world,rpc,Packet(7,2,Baseline(),1))==null&&Health(data)==-1f,"late full body with same data revision does not repair the missing state after failed owner-only update");
            world=World(out rpc,out id,out data,7,2,false);Set(rpc,"m_socket",ClosedSocket());object peer=((IList)Get(world,"m_peers"))[0];
            NativeSendRecord().Invoke(null,new[]{world,peer,false,data});IDictionary sent=(IDictionary)Get(peer,"m_zdos");
            Check(sent.Contains(id)&&(uint)Get(sent[id],"m_dataRevision")==7&&(ushort)Get(sent[id],"m_ownerRevision")==2,"native record construction advances sent tuple before delivery");
            Check((int)Get(rpc,"m_sentPackages")==0,"actual native ZRpc.Invoke sends nothing on closed socket despite advanced sent tuple");
            Console.WriteLine("PASS: "+checks+" actual native receiver checks. No Unity creation, native clock, sockets, live mod patches or gameplay executed.");
        }
        catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
    }
}
