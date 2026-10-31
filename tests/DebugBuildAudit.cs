using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class DebugBuildAudit
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static IEnumerable<TypeDefinition> Types(TypeDefinition type)
    { yield return type; foreach (var child in type.NestedTypes) foreach (var item in Types(child)) yield return item; }
    private static IEnumerable<TypeDefinition> Types(AssemblyDefinition image)
    { return image.MainModule.Types.SelectMany(Types); }
    private static bool TestType(string name)
    { return name.Contains("FgnSoloCombatTest") || name.Contains("SoloNetworkLatency"); }
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
        return Convert.ToString(value, CultureInfo.InvariantCulture).Replace("1.0.94", "2.0.0");
    }
    private static bool VariantMethod(MethodDefinition method)
    {
        string type = method.DeclaringType.FullName, name = method.Name;
        if (type == "TooManyLags.Plugin") return name == "InitializeOwnership" || name == "Update";
        if (type == "TooManyLags.OwnerDebugApi") return name == "Phase";
        if (type == "TooManyLags.MobBridgeProtocol") return new[]{"Start","Stop","BeforeTransfer","Receive"}.Contains(name);
        if (type == "TooManyLags.MobBridgeFrame") return name == "Observe";
        if (type == "TooManyLags.MobBridgeSimulation") return name == ".ctor";
        if (type == "TooManyLags.MobBridgeVisual") return name == "StartObserver" || name == "RenderFrameSeconds";
        if (type == "TooManyLags.MobBridgeObserver" || type.StartsWith("TooManyLags.MobBridgeObserver/", StringComparison.Ordinal))
            return name == "Start" || name == "Begin" || name == "Receive" || name == "ObserveClock" ||
                name == "ApplyClock" || name == "RelayClock" || name.Contains("<Start>");
        if (type == "TooManyLags.FgnTargetBoundary") return new[]{"Changed","Send","FreshTarget","Reset","BeforeAttack","Describe"}.Contains(name);
        if(type=="TooManyLags.FgnBossPolicy")return name=="Reset"||name=="TwoNearby";
        if(type=="TooManyLags.FgnTargetEvents"||type=="TooManyLags.PlayerSimulation.TargetEvents")return name=="Install";
        if (type == "TooManyLags.FgnCombatOwnership" && (name == "SoloEligiblePending" ||
            name == "BeforeOnHandOff" || name == "TargetAllowsOwnership" || name == "Install")) return true;
        if (type == "TooManyLags.MobCollisionReadiness") return name == "Start" || name == "Stop";
        if (type.StartsWith("TooManyLags.FgnCombatOwnership/", StringComparison.Ordinal)) return true; // Mode callback.
        return type == "TooManyLags.FgnCombatOwnership" && new[]{"Reset","Fail","ConnectCurrentServer","SessionConnection",
            "CapabilityPostfix","RevokeCapability","MarkWatch","Handle","SourceDestination","SourceLocalReady","SourceCommit"}.Contains(name);
    }
    private static bool DiagnosticField(FieldReference field)
    {
        string type = field.DeclaringType.FullName;
        if (type == "TooManyLags.MobBridgeCost") return true;
        if (type == "TooManyLags.FgnSoloCombatTest") return field.Name == "localStatus";
        if (type == "TooManyLags.MobBridgeAnimation") return field.Name == "rootBone";
        return type == "TooManyLags.MobBridgeVisual/Track" && new[]{
            "CaptureTicks","MaxCaptureTicks","AiTicks","MotorTicks","Decisions","MotorSteps","RotationSample",
            "LastRootRotation","LastBoneRotation","MaxRootStep","MaxBoneStep","MaxDrawModelGap","MaxDrawBoneGap",
            "MaxApplyBoneGap","DrawSamples","MaxDivergence","MotionSamples","StarvedFrames","EndpointFrames",
            "BufferSlowFrames","MinPlaybackRate","MaxPlaybackRateStep","MaxFrameDelta","ExpectedAttack",
            "EntryVelocity","FirstMotorPosition","SecondMotorPosition","FirstMotorTime","SecondMotorTime","EntryLogged"}.Contains(field.Name);
    }
    private static bool DiagnosticMethod(MethodDefinition method)
    {
        string type = method.DeclaringType.FullName;
        if (type == "TooManyLags.MobBridgeCost" || type == "TooManyLags.Plugin" && method.Name == "TestLog" ||
            type == "TooManyLags.MobBridgeProtocol" && new[]{"Report","ReceiveDiagnostic"}.Contains(method.Name) ||
            type == "TooManyLags.MobBridgeVisual" && method.Name == "MeasureDraw") return true;
        return method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is FieldReference && DiagnosticField((FieldReference)i.Operand) ||
            i.Operand is MethodReference && DiagnosticCall((MethodReference)i.Operand));
    }
    private static bool DiagnosticCall(MethodReference call)
    {
        string type = call.DeclaringType.FullName;
        return type == "TooManyLags.MobBridgeCost" ||
            type == "TooManyLags.Plugin" && call.Name == "TestLog" ||
            type == "TooManyLags.MobBridgeProtocol" && call.Name == "Report" ||
            type == "TooManyLags.MobBridgeVisual" && call.Name == "MeasureDraw";
    }
    private static bool Metric(FieldDefinition field)
    {
        string type=field.DeclaringType.FullName,name=field.Name;
        return (type=="TooManyLags.FgnTargetBoundary"&&new[]{"Changes","HeldStarts","ExpiredHolds"}.Contains(name))||
            (type=="TooManyLags.FgnBossPolicy"&&name=="Evaluations")||
            ((type=="TooManyLags.FgnTargetEvents"||type=="TooManyLags.PlayerSimulation.TargetEvents")&&new[]{"PatchedMethods","ObservedWrites"}.Contains(name));
    }
    private static bool DiagnosticChange(MethodDefinition method)
    {
        // These source-file types are the explicitly audited .86 continuity
        // delta. Normal/debug equality below is still checked independently.
        string type = method.DeclaringType.FullName;
        if (new[]{"TooManyLags.MobCombatState","TooManyLags.MobStatusState","TooManyLags.MobHandoffVisual","TooManyLags.MobVisualCurve",
            "TooManyLags.MobSourceHandoff","TooManyLags.MobObserverHandoff","TooManyLags.MobNativePublication","TooManyLags.FgnMobHandoff",
            "TooManyLags.MobFlightHandoff","TooManyLags.TamedMobHandoff","TooManyLags.FgnCombatOwnership",
            "TooManyLags.PlayerSimulation.LocalAgent","TooManyLags.Plugin","TooManyLags.ConfigTemplate"}
            .Any(t => type == t || type.StartsWith(t + "/", StringComparison.Ordinal))) return true;
        return new[]{
            "System.Int32 TooManyLags.AuthorityController/NativeNode::get_Requests()",
            "System.String TooManyLags.AuthorityController/NativeNode::OwnerStatus()",
            "System.Collections.Generic.Dictionary`2<System.String,TooManyLags.ConfigLayout/Option> TooManyLags.ConfigLayout::Schema()",
            "System.String TooManyLags.ConfigLayout::Render(System.Collections.Generic.Dictionary`2<System.String,System.String>,System.Collections.Generic.List`1<TooManyLags.ConfigLayout/Option>)",
            "System.Void TooManyLags.FgnCombatOwnership::Install(BepInEx.Configuration.ConfigFile,System.Reflection.Assembly,System.Version,System.Boolean)",
            "System.Void TooManyLags.FgnCombatOwnership::Stop()",
            "System.Void TooManyLags.FgnCombatOwnership::Fail(System.Exception)",
            "System.Void TooManyLags.FgnCombatOwnership::NotifyServerOff()",
            "System.Void TooManyLags.FgnCombatStatus::ConnectEnabled(ZNetPeer)",
            "System.Void TooManyLags.GridHandoffs.Protocol::BatchWarning(System.String,System.Exception)",
            "System.Void TooManyLags.GridHandoffs.Protocol::Receive(TooManyLags.GridHandoffs.Protocol/Connection,ZRpc,ZPackage)",
            "System.Void TooManyLags.GridHandoffs.Protocol::FlushDeferred()",
            "System.Boolean TooManyLags.GridHandoffs.Protocol::Ready(ZDOMan,System.Int64,ZDO)",
            "System.Boolean TooManyLags.GridHandoffs.Protocol/ReceivePatch::Prefix(ZRoutedRpc/RoutedRPCData,System.Boolean)",
            "System.Boolean TooManyLags.NativePup::ReadyPoint(UnityEngine.Vector3,System.Single)",
            "System.Void TooManyLags.Plugin::Awake()",
            "System.Void TooManyLags.Plugin::Warn(System.String)",
            "System.Void TooManyLags.Plugin::ApplyLiveSetting(System.String)",
            "System.Void TooManyLags.Plugin::RuntimeFault(System.Exception)",
            "System.Void TooManyLags.Plugin::RequestFault(System.Exception)",
            "System.Int32 TooManyLags.RequestRouter::PendingRequests(TooManyLags.AuthorityController/NativeNode)",
            "System.Void TooManyLags.RequestRouter::FlushEntry(TooManyLags.RequestRouter/Waiting)",
            "System.Void TooManyLags.RequestRouter::Hold(TooManyLags.RequestRouter/Waiting,ZRoutedRpc/RoutedRPCData)",
            "System.Void TooManyLags.RequestRouter::Expire(TooManyLags.RequestRouter/Waiting)",
            "System.Void TooManyLags.RequestRouter::Cancel(TooManyLags.RequestRouter/Waiting)",
            "System.Void TooManyLags.NetworkSavings.StateTransfer::FlushRepairs(ZRpc)",
            "System.Void TooManyLags.PlayerSimulation.Network::Receive(ZRpc,ZPackage)",
            "System.Void TooManyLags.PlayerSimulation.Settings::.ctor(BepInEx.Configuration.ConfigFile,System.Boolean)",
            "System.Boolean TooManyLags.PupCompatibility.NativePup::ReadyPoint(UnityEngine.Vector3,System.Single)",
            "System.Boolean TooManyLags.PupCompatibility.NativePup::PreparedPoint(UnityEngine.Vector3,System.Single,System.Boolean)",
            "System.Boolean TooManyLags.PupCompatibility.Statics::Allow(WearNTear)",
            "System.Boolean TooManyLags.TextEditing.Protection::Send(ZNetPeer,TooManyLags.TextEditing.Protection/Message,ZDOID,System.Int64,System.UInt32,System.UInt32)",
            "System.Void TooManyLags.TextEditing.Protection::Receive(TooManyLags.TextEditing.Protection/Link,ZRpc,ZPackage)",
            "System.Boolean TooManyLags.TextEditing.Protection::Open(TooManyLags.TextEditing.Protection/Entry)"
        }.Contains(method.FullName);
    }
    private static bool Calls(MethodDefinition method, string type, string name)
    { return method.HasBody && method.Body.Instructions.Any(i => i.Operand is MethodReference &&
        ((MethodReference)i.Operand).DeclaringType.FullName == type && ((MethodReference)i.Operand).Name == name); }
    public static void Main(string[] args)
    {
        using (var normal = AssemblyDefinition.ReadAssembly(args[0]))
        using (var debug = AssemblyDefinition.ReadAssembly(args[1]))
        using (var baseline = AssemblyDefinition.ReadAssembly(args[2]))
        {
            var normalTypes = Types(normal).Where(t => !t.FullName.StartsWith("<PrivateImplementationDetails>", StringComparison.Ordinal)).ToDictionary(t => t.FullName);
            var debugTypes = Types(debug).Where(t => !t.FullName.StartsWith("<PrivateImplementationDetails>", StringComparison.Ordinal)).ToDictionary(t => t.FullName);
            var oldMethods = Types(baseline).SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
            Check(!normalTypes.Keys.Any(TestType), "normal build contains test types");
            Check(debugTypes.ContainsKey("TooManyLags.FgnSoloCombatTest") && debugTypes.ContainsKey("TooManyLags.SoloNetworkLatency"), "debug modules missing");
            Check(!normal.MainModule.GetTypeReferences().Any(t => TestType(t.FullName)), "normal metadata references tests");
            Check(!normal.MainModule.GetMemberReferences().Any(m => TestType(m.FullName)), "normal member reference points to tests");
            var forbiddenMethods = new[]{"SoloEligible","SoloEligiblePending","SoloPrepare","HoldForSolo"};
            var forbiddenStrings = new[]{"ai_test","ai_test_v","ai_test_vanilla","tml_ai_test","TooManyLags_AI_Solo","MOB TEST:",".solo-latency",".solo-combat-test"};
            int debugCompared = 0, normalCompared = 0;
            foreach (var type in debugTypes.Values.Where(t => !TestType(t.FullName)))
            {
                TypeDefinition retained;
                Check(normalTypes.TryGetValue(type.FullName, out retained), "non-test type removed: " + type.FullName);
                foreach (var method in type.Methods)
                {
                    bool omitted = forbiddenMethods.Contains(method.Name) || (type.FullName == "TooManyLags.MobCollisionReadiness" &&
                        (method.Name == "AfterMob" || method.Name == "Describe"));
                    if (!DiagnosticMethod(method))
                        Check(retained.Methods.Any(m => m.FullName == method.FullName) != omitted, "unexpected method removal/retention: " + method.FullName);
                }
                foreach (var field in type.Fields)
                {
                    bool omitted = DiagnosticField(field) || Metric(field) || (type.FullName == "TooManyLags.FgnTargetBoundary/Intent" && field.Name == "Solo") ||
                        (type.FullName == "TooManyLags.MobCollisionReadiness" && new[]{"selectedCalls","selectedBlocked","traced","tracedOwner","tracedRevision","tracedAt","lastAllowed"}.Contains(field.Name));
                    Check(retained.Fields.Any(f => f.Name == field.Name && f.FieldType.FullName == field.FieldType.FullName) != omitted,
                        "unexpected field removal/retention: " + field.FullName);
                }
            }
            foreach (var type in normalTypes.Values)
            {
                Check(!type.Fields.Any(f => TestType(f.FieldType.FullName)), "test field signature: " + type.FullName);
                foreach (var method in type.Methods)
                {
                    Check(!forbiddenMethods.Contains(method.Name), "test helper in normal build: " + method.FullName);
                    Check(!TestType(method.ReturnType.FullName) && !method.Parameters.Any(p => TestType(p.ParameterType.FullName)), "test signature: " + method.FullName);
                    if (method.HasBody) foreach (var instruction in method.Body.Instructions)
                    {
                        var member = instruction.Operand as MemberReference;
                        Check(member == null || !TestType(member.FullName), "test instruction: " + method.FullName);
                        var text = instruction.Operand as string;
                        Check(text == null || !forbiddenStrings.Any(text.Contains), "test command/RPC/hook/log string: " + method.FullName);
                    }
                    if (VariantMethod(method)) continue;
                    var other = debugTypes[type.FullName].Methods.Single(m => m.FullName == method.FullName);
                    if (DiagnosticMethod(other)) continue;
                    Check(Body(method) == Body(other), "unrelated normal/debug code changed: " + method.FullName);
                    normalCompared++;
                }
            }
            foreach (var method in debugTypes.Values.SelectMany(t => t.Methods))
            {
                if (DiagnosticMethod(method)) continue;
                if (method.DeclaringType.FullName == "TooManyLags.MobBridgePlacement" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeBody" && method.Name == ".ctor" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeTakeover" && new[]{".ctor","AdvancePosition","ConstrainPosition","Settled","RebaseOwnership"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeTakeover/OwnershipCorrection" && new[]{".ctor","AdvancePosition"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeVisual" && new[]{"UpdatePresentation","OwnershipChanged","Valid","ComputePose"}.Contains(method.Name)) continue;
                if(method.DeclaringType.FullName == "TooManyLags.ConfigTemplate" || method.DeclaringType.FullName == "TooManyLags.ConfigLayout" ||
                    method.DeclaringType.FullName == "TooManyLags.Plugin" && method.Name == "Awake" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeLatency" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeWarmup" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeNativeActor" &&
                        (method.Name == "WarmFields" || method.Name == "WarmField") ||
                    method.DeclaringType.FullName == "NativeMobPreviewAudit.FieldAccess" && method.Name == "Prepare" ||
                    method.DeclaringType.FullName == "NativeMobPreviewAudit.ShadowSnapshot`1" &&
                        (method.Name == "PrepareType" || method.Name == "Factory") ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeClockCadence" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeTiming" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeCheckpoint" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeAnimation" && new[]{"Prepare","TryPose","RigPaths","BeginVisualAttack","TryAttackState","Remaining","AdvanceVisual","EndVisualAttack","PrepareLive"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeEffects/NativeFxScope" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeEffects" && new[]{".cctor","BeforeContinuous","AfterContinuous","AfterSync","EndSession"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeAnimationHistory" && method.Name == "Add" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeObserverPacket" && new[]{".ctor","Decode","DecodeCore","DecodeForRelay","Encode","get_RecordingSeconds","get_ExpectedArrival","get_NativeStep","get_MatchArrival"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeCaptureClock" && method.Name == "Take" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeTakeover" && new[]{"SettleForPose","SettleForPlayback","SettleForPlaybackCapped"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeTimeline" && new[]{"Integrate","ManageBuffer",".ctor","ExpectFirst","ExpectMatch","ExpectFrames","ObserveDelivery"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeProtocol" && new[]{"ApplyPending","ApplySourcePose","ArmSourcePose","OwnerSyncPost","ForgetOwnerPose","ClearOwnerPoses","Finite","SendClock","ReceiveClock","AfterRandom","Maintain","Prune","Cancel","Stop","Shutdown","Context","PublishPhysicsClock","AfterAttack",".cctor","BeforeTransfer","Transferred","PollRate","ReceiveRate","LocalTiming","TimingFor","get_FrameSeconds","Connected","ReceiveFinish"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeProtocol" && method.Name.StartsWith("<Connected>",StringComparison.Ordinal) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeFrame" && method.Name == "Observe" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeProtocol/ClockEpoch" && method.Name == ".ctor" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeVisual" && new[]{"Start","StartObserver","ObserveClock","Advance","TakeoverTarget","TakeoverSettled","OwnerReadySeconds","UpdateVisualAttack","PrimeOwnerAnimation","BeginOwnerGain","CancelOwnerGain","CancelOwnerGains","RestoreOwnerGain","ComputeOwnerGainPose","Cancel","Stop","ApplyRender","Restore"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeVisual" && method.Name == ".cctor" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeVisual" && method.Name == "RenderFrameSeconds" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeVisual/AttackEvent" && method.Name == ".ctor" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeVisual/OwnerGain" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgePresentationPoint" && new[]{"OwnerGainWeight","OwnerGainPosition","OwnerGainRotation"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.FgnSoloCombatTest" && new[]{"ClearLocal","Completed","Start","Stop","Poll","TryTransfer"}.Contains(method.Name) ||
                    method.DeclaringType.FullName.StartsWith("TooManyLags.FgnSoloCombatTest/<>c__DisplayClass",StringComparison.Ordinal) &&
                        method.Name == "<Connect>b__2" ||
                    method.DeclaringType.FullName == "TooManyLags.FgnCombatOwnership" &&
                    new[]{"BeforeOnHandOff","TargetAllowsOwnership","HandoffObserve","Install"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.FgnMobHandoff" &&
                    new[]{"Send","ReceiveCommit"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeSimulation" &&
                    method.Name == ".ctor" ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeProtocol" && method.Name == "BeforeTransfer" ||
                    method.DeclaringType.FullName == "TooManyLags.Plugin" && method.Name.Contains("<InitializeOwnership>") ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeObserver" && new[]{"Begin","Receive","ObserveClock","ApplyClock","RelayClock","Connect","Lifetime","ReceiveServer","ReceiveClient"}.Contains(method.Name) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeObserver" && method.Name.StartsWith("<Connect>",StringComparison.Ordinal) ||
                    method.DeclaringType.FullName.StartsWith("TooManyLags.MobBridgeObserver/",StringComparison.Ordinal) && method.Name.StartsWith("<Connect>",StringComparison.Ordinal) ||
                    method.DeclaringType.FullName.StartsWith("TooManyLags.MobBridgeProtocol/",StringComparison.Ordinal) && method.Name.StartsWith("<Connected>",StringComparison.Ordinal) ||
                    method.DeclaringType.FullName == "TooManyLags.MobBridgeVisual" && method.Name == "StartObserver")continue;
                if (method.DeclaringType.FullName == "TooManyLags.FgnCombatOwnership" && method.Name == "SoloEligiblePending") continue;
                MethodDefinition prior;
                Check(oldMethods.TryGetValue(method.FullName, out prior), "debug adds unexpected method: " + method.FullName);
                Check(Body(method) == Body(prior), "unrelated behavior differs from verified .80: " + method.FullName);
                debugCompared++;
            }
            foreach(var prior in oldMethods.Values.Where(m=>m.DeclaringType.FullName != "TooManyLags.ConfigTemplate" && m.DeclaringType.FullName != "TooManyLags.ConfigLayout" &&
                !(m.DeclaringType.FullName == "TooManyLags.MobBridgeProtocol" && m.Name == "SendClock") &&
                !(m.DeclaringType.FullName == "TooManyLags.MobBridgeTimeline" && m.Name == "ManageBuffer" && m.Parameters.Count == 6) &&
                m.DeclaringType.FullName != "TooManyLags.MobBridgeCheckpoint" &&
                !(m.DeclaringType.FullName == "TooManyLags.MobBridgeTakeover/OwnershipCorrection" && m.Name == ".ctor") &&
                !(m.DeclaringType.FullName == "TooManyLags.MobBridgeVisual" && m.Name == "BeginTakeover") &&
                !(m.DeclaringType.FullName == "TooManyLags.MobBridgeVisual" && m.Name == "Start") &&
                !(m.DeclaringType.FullName == "TooManyLags.MobBridgeFrame" && m.Name == "Observe" && m.Parameters.Count == 9) &&
                !(m.DeclaringType.FullName == "TooManyLags.MobBridgeObserver" && m.Name == "ObserveClock" && m.Parameters.Count == 15) &&
                !(m.DeclaringType.FullName == "TooManyLags.MobBridgeVisual" && m.Name == "ObserveClock" && m.Parameters.Count == 13) &&
                !(m.DeclaringType.FullName == "TooManyLags.MobBridgeObserver" && m.Name == "Begin")))
                Check(debugTypes.Values.SelectMany(t=>t.Methods).Any(m=>m.FullName==prior.FullName),"unrelated baseline method removed: "+prior.FullName);
            var collision = normalTypes["TooManyLags.MobCollisionReadiness"];
            Check(!collision.Methods.Any(m => m.Name == "AfterMob" || m.Name == "Describe"), "test-only PUP postfix remains");
            Check(!collision.Fields.Any(f => new[]{"selectedCalls","selectedBlocked","traced","tracedOwner","tracedRevision","tracedAt","lastAllowed"}.Contains(f.Name)), "PUP trace state remains");
            Check(collision.Methods.Any(m => m.Name == "BeforeMob") && collision.Methods.Any(m => m.Name == "Ready"), "real collision guard removed");
            Check(!normalTypes["TooManyLags.FgnTargetBoundary/Intent"].Fields.Any(f => f.Name == "Solo"), "test intent flag remains");
            foreach(var type in normalTypes.Values)Check(!type.Fields.Any(Metric),"test statistic retained in normal build: "+type.FullName);
            var pluginUpdate = normalTypes["TooManyLags.Plugin"].Methods.Single(m => m.Name == "Update");
            Check(!pluginUpdate.Body.Instructions.Any(i => i.Operand is MethodReference && TestType(((MethodReference)i.Operand).FullName)), "test polling remains");
            Check(Calls(normalTypes["TooManyLags.FgnTargetBoundary"].Methods.Single(m => m.Name == "BeforeAttack"), "TooManyLags.FgnTargetBoundary", "Busy"), "real attack safeguard removed");
            Check(normalTypes.ContainsKey("TooManyLags.MobHandoffVisual") && normalTypes.ContainsKey("TooManyLags.FgnCombatStatus"), "visual bridge/status diagnostics removed");
            foreach(string bridge in new[]{"MobBridgeProtocol","MobBridgeVisual","MobBridgeSimulation","MobBridgeNativeActor","MobBridgeObserver"})
                Check(normalTypes.ContainsKey("TooManyLags."+bridge), "Release lost native simulation bridge: "+bridge);
            DebugIdleAudit.Run(normal);
            DebugIdleAudit.RunDebug(debug);
            Console.WriteLine("PASS: " + checks + " compiled build-boundary assertions; " + debugCompared +
                " debug methods match verified .94 (version metadata normalized); " + normalCompared + " unrelated normal/debug methods match exactly. Native bridge retained; no release test types, references, commands, RPCs, polls or PUP tracing.");
        }
    }
}
