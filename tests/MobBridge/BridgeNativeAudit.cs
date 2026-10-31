using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BridgeNativeAudit
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static IEnumerable<TypeDefinition> All(TypeDefinition type)
    {
        yield return type;
        foreach (TypeDefinition nested in type.NestedTypes)
            foreach (TypeDefinition item in All(nested)) yield return item;
    }
    private static IEnumerable<MethodReference> Calls(MethodDefinition method)
    {
        return method.HasBody ? method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>() : Enumerable.Empty<MethodReference>();
    }
    public static int Main(string[] args)
    {
        try
        {
            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(args[0]))
            {
                Dictionary<string, TypeDefinition> types = assembly.MainModule.Types.SelectMany(All).ToDictionary(t => t.FullName);
                bool release = args.Length > 1 && args[1] == "release";
                if (release)
                {
                    Check(!types.Keys.Any(t => t.Contains("FgnSoloCombatTest") || t.Contains("SoloNetworkLatency")),
                        "release retains solo-test modules");
                }
                foreach (string name in new[] { "MobBridgeTimeline", "MobBridgeTakeover", "MobBridgeAttackGate", "MobBridgeProtocol",
                    "MobBridgeVisual", "MobBridgeSimulation", "MobBridgeBody", "MobBridgeFieldCodec", "MobBridgeCheckpoint",
                    "MobBridgeHit", "MobBridgeHitFilter", "MobBridgePresentationPoint" })
                    Check(types.ContainsKey("TooManyLags." + name), "missing bridge type: " + name);
                TypeDefinition transfer = types["TooManyLags.MobFlightHandoff"];
                MethodDefinition body = transfer.Methods.Single(m => m.Name == "Transfer");
                List<Instruction> instructions = body.Body.Instructions.ToList();
                int simBefore = instructions.FindIndex(i => i.Operand is FieldReference &&
                    ((FieldReference)i.Operand).Name == "SimulationTransfer");
                int owner = instructions.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "SetOwner");
                int simAfter = instructions.FindIndex(i => i.Operand is FieldReference &&
                    ((FieldReference)i.Operand).Name == "SimulationTransferred");
                Check(simBefore >= 0 && owner > simBefore && simAfter > owner, "simulation callbacks do not straddle owner write");

                foreach (string name in new[] { "MobBridgeSimulation", "MobBridgeBody" })
                {
                    TypeDefinition type = types["TooManyLags." + name];
                    foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
                    foreach (MethodReference called in Calls(method))
                    {
                        string ownerType = called.DeclaringType.FullName, call = called.Name;
                        bool forbidden =
                            ((ownerType == "ZDO" || ownerType == "ZNetView") && (call.StartsWith("Set", StringComparison.Ordinal) || call == "ClaimOwnership")) ||
                            ((ownerType == "UnityEngine.Transform" || ownerType == "UnityEngine.Rigidbody") && call.StartsWith("set_", StringComparison.Ordinal)) ||
                            (ownerType == "Character" && (call == "Damage" || call == "RPC_Damage" || call == "SetHealth")) ||
                            (ownerType == "Humanoid" && call == "StartAttack");
                        Check(!forbidden, type.FullName + "." + method.Name + " mutates live gameplay: " + called.FullName);
                    }
                }
                TypeDefinition bridgeBody = types["TooManyLags.MobBridgeBody"];
                TypeDefinition snapshotType = types["NativeMobPreviewAudit.ShadowSnapshot`1"];
                Check(!Calls(snapshotType.Methods.Single(m => m.Name == "Create"))
                    .Any(c => c.Name == "MakeGenericType" || c.Name == "CreateInstance") &&
                    Calls(snapshotType.Methods.Single(m => m.Name == "Create")).Any(c => c.Name == "Factory"),
                    "snapshot construction still binds/invokes generic constructors per field");
                TypeDefinition native = types["TooManyLags.MobBridgeNativeActor"];
                TypeDefinition world = types["TooManyLags.MobBridgeNativeWorld"];
                MethodDefinition nativeStep = native.Methods.Single(m => m.Name == "Step");
                foreach (string name in new[] { "Step", "ApplyAnimationEvents", "BeforeAnimHashes", "CaptureCallbacks", "ClearCallbacks", "RestoreCallbacks" })
                    Check(!Calls(native.Methods.Single(m => m.Name == name)).Any(c => c.DeclaringType.Namespace == "System.Reflection" || c.Name == "Field"),
                        "native motor tick still reflects or looks up field names: " + name);
                Check(Calls(nativeStep).Any(c => c.Name == "CaptureCallbacks") && Calls(nativeStep).Any(c => c.Name == "ClearCallbacks"),
                    "native motor no longer suspends gameplay callbacks");
                Check(nativeStep.Body.ExceptionHandlers.Where(h => h.HandlerType == ExceptionHandlerType.Finally).Any(h =>
                    nativeStep.Body.Instructions.SkipWhile(i => i != h.HandlerStart).TakeWhile(i => i != h.HandlerEnd)
                    .Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "RestoreCallbacks")),
                    "typed native callbacks are not restored in finally");
                Check(Calls(world.Methods.Single(m => m.Name == "Mirror")).Any(c => c.Name == "TryGetValue") &&
                    !Calls(world.Methods.Single(m => m.Name == "Mirror")).Any(c => c.Name == "GetEnumerator"),
                    "native collider remapping scans the full collision scene");
                Check(Calls(bridgeBody.Methods.Single(m => m.Name == "Move")).Any(c => c.DeclaringType.Name == "MobBridgeNativeActor" && c.Name == "Step"),
                    "buffer producer bypasses original native motor");
                foreach (string name in new[] { "UpdateMotion", "UpdateWater", "UpdateGroundTilt", "UpdateGroundContact", "OnCollisionStay", "CalculateLiquidDepth", "UpdateLayer", "UpdateLookTransition" })
                    Check(native.Methods.Any(m => m.HasBody && m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == name)),
                        "original native locomotion delegate missing: " + name);
                Check(Calls(nativeStep).Any(c => c.Name == "GetLiquidLevel" && c.DeclaringType.Name == "Floating"), "native motor misses live water inputs");
                Check(Calls(nativeStep).Any(c => c.Name == "Simulate" && c.DeclaringType.Name == "MobBridgeNativeWorld") &&
                    Calls(world.Methods.Single(m => m.Name == "Simulate")).Any(c => c.Name == "Simulate" && c.DeclaringType.Name == "PhysicsScene"),
                    "native motor does not use Unity scene physics");
                Check(Calls(nativeStep).Any(c => c.Name == "set_rotation" && c.DeclaringType.Name == "Rigidbody"),
                    "native Character transform turning is not synchronized into private physics body");
                Check(world.Methods.Any(m => Calls(m).Any(c => c.Name == "CreateScene")) &&
                    !native.Methods.Any(m => Calls(m).Any(c => c.Name == "Instantiate")), "preview duplicates a registered native actor instead of private physics");
                foreach (string name in new[] { "RestoreReal", "Exit", "set_state", "set_position", "set_rotation", "SetPositionAndRotation", "set_layer" })
                    Check(nativeStep.Body.ExceptionHandlers.Where(h => h.HandlerType == ExceptionHandlerType.Finally).Any(h =>
                    nativeStep.Body.Instructions.SkipWhile(i => i != h.HandlerStart).TakeWhile(i => i != h.HandlerEnd).Any(i =>
                        i.Operand is MethodReference && ((MethodReference)i.Operand).Name == name)), "source restoration missing in finally: " + name);
                Check(native.Methods.Any(m => m.Name == "BeforeDataWrite") && native.Methods.Any(m => m.Name == "BeforeRpc") &&
                    native.Methods.Any(m => m.Name == "BeforeAnimation") && native.Methods.Any(m => m.Name == "BeforeOwnAction"),
                    "native simulation side effects are not scoped");
                Check(Calls(nativeStep).Any(c => c.Name == "Enter") && Calls(nativeStep).Any(c => c.Name == "RetainCurrent"),
                    "native liquid status additions do not stay in private status timeline");
                Check(!types.ContainsKey("TooManyLags.MobBridgeCollision"), "obsolete custom collision motor remains in runtime");
                TypeDefinition statusState = types["TooManyLags.MobStatusState"];
                TypeDefinition shadow = All(statusState).SingleOrDefault(t => t.Name == "Shadow");
                Check(shadow != null && shadow.Methods.Any(m => m.Name == "Advance") &&
                    shadow.Methods.Any(m => m.Name == "ApplySpeedMods"),
                    "fake status timeline is missing or incomplete");
                Check(Calls(types["TooManyLags.MobBridgeSimulation"].Methods.Single(m => m.Name == "Step"))
                    .Count(c => c.Name == "Enter" || c.Name == "Exit") >= 2,
                    "fake AI does not enter and leave the captured status timeline transactionally");
                TypeDefinition hit = types["TooManyLags.MobBridgeHit"];
                Check(hit.Methods.Any(m => m.HasBody && m.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Ldstr && (string)i.Operand == "hitbox")),
                    "fake pose collider does not use the dedicated hitbox layer");
                Check(Calls(hit.Methods.Single(m => m.Name == "Create")).Any(c => c.Name == "GetComponentsInChildren") &&
                    Calls(hit.Methods.Single(m => m.Name == "RestorePhysics")).Any(c => c.Name == "set_enabled"),
                    "fake pose colliders do not replace and restore all native mob shapes");
                Check(!Calls(types["TooManyLags.MobBridgeVisual"].Methods.Single(m => m.Name == "Start"))
                    .Any(c => c.Name == "GetComponentsInChildren"), "visual still rejects multi-collider mobs");
                Check(!Calls(types["TooManyLags.MobBridgeVisual"].Methods.Single(m => m.Name == "FollowHit"))
                    .Any(c => c.Name == "ApplyPose"), "bone queries rewrite the visible rig outside rendering");
                Check(!Calls(types["NativeMobPreviewAudit.NativeSteeringAdapters"].Methods.Single(m => m.Name == "CenterPoint"))
                    .Any(c => c.Name == "get_bounds"), "fake AI center still reads empty disabled-collider bounds");
                Check(Calls(types["NativeMobPreviewAudit.NativeSteeringAdapters"].Methods.Single(m => m.Name == "CenterPoint"))
                    .Any(c => c.Name == "TransformPoint"), "fake collider center is not derived from configured geometry");
                Check(!types["TooManyLags.MobBridgeSimulation"].Methods.Single(m => m.Name == "TryStep")
                    .Body.Instructions.Any(i => i.OpCode == OpCodes.Ldarg_2), "fake cooldown clock still adds wall-time route latency");
                Check(Calls(hit.Methods.Single(m => m.Name == "Follow")).Any(c => c.Name == "set_position" && c.DeclaringType.Name == "Rigidbody"),
                    "hit query proxy depends on deferred Transform-to-physics synchronization");
                Check(!Calls(hit.Methods.Single(m => m.Name == "Create")).Any(c => c.Name == "SetParent"),
                    "hit proxy is still parented to replicated root or attached to its physics body");
                TypeDefinition protocol = types["TooManyLags.MobBridgeProtocol"];
                TypeDefinition timeline = types["TooManyLags.MobBridgeTimeline"];
                Check(!timeline.Methods.Single(m => m.Name == "FakeTime").Body.Instructions.Any(i =>
                        i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "latency"),
                    "fake simulation still caps at the initial latency horizon");
                TypeDefinition historyType = types["TooManyLags.MobBridgePoseHistory"];
                Check(Calls(historyType.Methods.Single(m => m.Name == "Add")).Any(c => c.Name == "Index") &&
                    historyType.Methods.Single(m => m.Name == "Index").Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Rem),
                    "pose history is not rolling after bounded capacity");
                Check(protocol.Methods.Any(m => m.HasBody && m.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Ldstr && (string)i.Operand == "TooManyLags_MobBridgeState")), "bridge state RPC missing");
                Check(protocol.Methods.Any(m => m.HasBody && m.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Ldstr && (string)i.Operand == "TooManyLags_MobBridgeClock")), "bridge clock RPC missing");
                Check(protocol.Methods.Any(m => m.HasBody && m.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Ldstr && (string)i.Operand == "TooManyLags_MobBridgeRate")), "pre-handoff rate cache RPC missing");
                Check(protocol.Methods.Any(m => m.HasBody && m.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Ldstr && (string)i.Operand == "TooManyLags_MobBridgeDiagnostic")), "client bridge diagnostic relay missing");
                Check(protocol.Fields.Any(f => f.Name == "random"), "deterministic destination random epoch missing");
                Check(protocol.Fields.Any(f => f.Name == "targets"), "destination target lock epoch missing");
                Check(protocol.Fields.Any(f => f.Name == "rates") && protocol.Fields.Any(f => f.Name == "localRate"),
                    "player simulation-rate cache missing");
                Check(protocol.Fields.Any(f => f.Name == "clocks") && protocol.Fields.Any(f => f.Name == "aiHooks") &&
                    protocol.Fields.Any(f => f.Name == "receiveHooks"), "bridged hooks are not transient");
                MethodDefinition start = protocol.Methods.Single(m => m.Name == "Start");
                Check(!start.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "UpdateAI"),
                    "AI hook is installed permanently at default startup");
                List<Instruction> aiHook = protocol.Methods.Single(m => m.Name == "EnsureAiHook").Body.Instructions.ToList();
                Check(aiHook.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "UpdateAI"),
                    "event-triggered AI hook is missing");
                Check(aiHook.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "UpdateTarget") &&
                    aiHook.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "BeforeTarget"),
                    "destination AI target lock hook is missing");
                MethodDefinition beforeTarget = protocol.Methods.Single(m => m.Name == "BeforeTarget");
                Check(beforeTarget.Parameters.Select(p => p.Name).SequenceEqual(
                    new[] { "__instance", "humanoid", "dt", "canHearTarget", "canSeeTarget" }),
                    "target-lock prefix parameter names drifted from native UpdateTarget");
                TypeDefinition visual = types["TooManyLags.MobBridgeVisual"];
                Check(types["NativeMobPreviewAudit.NativeSteeringAdapters"].Methods.Single(m => m.Name == "InAttack")
                    .Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "ShadowInAttack"),
                    "fake AI still follows future replicated attack animation state");
                Check(native.Methods.Any(m => m.Name == "BeforeAttack"), "future replicated attacks can affect earlier native recording");
                TypeDefinition recording = types["TooManyLags.MobBridgeRecording"];
                Check(!recording.Fields.Any(f => f.FieldType.FullName.Contains("Animator") || f.FieldType.FullName.Contains("Animation")),
                    "recording unexpectedly buffers animation states");
                Check(!Calls(recording.Methods.Single(m => m.Name == "Sample")).Any(c => c.Name == "Add"), "playback rewrites native recording");
                Check(Calls(visual.Methods.Single(m => m.Name == "AdvanceSimulation")).Any(c => c.Name == "Take" && c.DeclaringType.Name == "MobBridgeCaptureClock"),
                    "capture cadence is still coupled to render frames");
                Check(Calls(visual.Methods.Single(m => m.Name == "ComputePose")).Any(c => c.Name == "Sample" && c.DeclaringType.Name == "MobBridgeRecording"),
                    "visual pose bypasses buffered native movement");
                Check(!Calls(visual.Methods.Single(m => m.Name == "Valid")).Any(c => c.Name == "get_Latency"),
                    "visual validity still expires an unsettled correction from initial latency");
                Check(!Calls(visual.Methods.Single(m => m.Name == "CurrentPose")).Any(c => c.Name == "FakeTime" || c.Name == "Sample"),
                    "HUD/camera consumers still advance fake presentation outside update");
                Check(protocol.Methods.Any(m => m.Name == "ReceiveFinish") && protocol.Methods.Any(m => m.Name == "Matching"),
                    "dynamic matching window lacks event-driven completion");
                Check(Calls(protocol.Methods.Single(m => m.Name == "SendClock")).Any(c => c.Name == "get_position") &&
                    Calls(protocol.Methods.Single(m => m.Name == "SendClock")).Any(c => c.Name == "get_rotation"),
                    "destination clock is not paired with owner pose");
                Check(protocol.Methods.Single(m => m.Name == "SendClock").Body.Instructions.Any(i =>
                    i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "AttackTime"),
                    "destination attack decision is not carried with owner clock");
                Check(protocol.Methods.Single(m => m.Name == "SendClock").Body.Instructions.Any(i =>
                    i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "AttackActive"),
                    "owner attack lifecycle is not carried with owner clock");
                Check(Calls(protocol.Methods.Single(m => m.Name == "PublishPhysicsClock")).Any(c => c.Name == "InAttack"),
                    "owner attack end cannot trigger an immediate clock publication");
                Check(Calls(visual.Methods.Single(m => m.Name == "BeforeHud")).Any(c => c.Name == "Tick") &&
                    Calls(visual.Methods.Single(m => m.Name == "Tick")).Any(c => c.Name == "get_frameCount"),
                    "HUD does not prepare the same once-per-frame pose before LateUpdate/render");
                Check(types["NativeMobPreviewAudit.NativeSteeringAdapters"].Methods.Single(m => m.Name == "Swimming")
                    .Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "ShadowSwimming"),
                    "fake AI does not use its isolated water state");
                var pendingInstructions = protocol.Methods.Single(m => m.Name == "ApplyPending").Body.Instructions.ToList();
                int applyCheckpoint = pendingInstructions.FindIndex(i => i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).Name == "Apply" && ((MethodReference)i.Operand).DeclaringType.Name == "MobBridgeCheckpoint");
                int releaseStageRead = pendingInstructions.FindIndex(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "ReleaseStage");
                Check(applyCheckpoint >= 0 && releaseStageRead > applyCheckpoint,
                    "early preview completion discards the native checkpoint before applying it");
                Check(Calls(protocol.Methods.Single(m => m.Name == "Maintain")).Any(c => c.Name == "FlushFinishes"),
                    "thrown completion send has no event-scoped retry path");
                Check(!Calls(protocol.Methods.Single(m => m.Name == "Maintain"))
                    .Any(c => c.Name == "DisableAiHook" || c.Name == "DisableReceiveHook"),
                    "idle gaps still rebuild protocol Harmony wrappers");
                Check(Calls(protocol.Methods.Single(m => m.Name == "Context")).Any(c => c.Name == "PrepareSession") &&
                    Calls(protocol.Methods.Single(m => m.Name == "Context")).Any(c => c.Name == "EnsureAiHook"),
                    "world connection does not warm preview/protocol hooks before combat");
                foreach (string method in new[] { "Shutdown", "Stop" })
                    Check(Calls(protocol.Methods.Single(m => m.Name == method)).Any(c => c.Name == "EndSession") &&
                        Calls(protocol.Methods.Single(m => m.Name == method)).Any(c => c.Name == "DisableAiHook"),
                        "hook lifetime exceeds enabled world session: " + method);
                Check(protocol.Methods.Single(m => m.Name == "PublishPhysicsClock").Body.Instructions.Any(i =>
                    i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "GameStarted") &&
                    protocol.Methods.Single(m => m.Name == "AfterRandom").Body.Instructions.Any(i =>
                    i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "AiSimulation"),
                    "owner body clock is still an AI-dt accumulator under CPU load");
                Check(Calls(protocol.Methods.Single(m => m.Name == "SendClock")).Any(c => c.Name == "Sent" && c.DeclaringType.Name == "MobBridgeClockCadence") &&
                    Calls(protocol.Methods.Single(m => m.Name == "PublishPhysicsClock")).Any(c => c.Name == "Due" && c.DeclaringType.Name == "MobBridgeClockCadence"),
                    "initial clock and physics clock use separate send scheduling");
                Check(Calls(protocol.Methods.Single(m => m.Name == "Maintain")).Any(c => c.Name == "PublishPhysicsClock") &&
                    Calls(protocol.Methods.Single(m => m.Name == "PublishPhysicsClock")).Any(c => c.Name == "get_fixedTime") &&
                    !Calls(protocol.Methods.Single(m => m.Name == "AfterRandom")).Any(c => c.Name == "SendClock"),
                    "paired physics frontier still waits for an AI callback or is sent before physics completes");
                Check(Calls(visual.Methods.Single(m => m.Name == "ObserveClock")).Any(c => c.Name == "ObserveInterval" && c.DeclaringType.Name == "MobBridgeClockCadence"),
                    "initial/attack packet bursts still replace periodic destination cadence");
                Check(Calls(visual.Methods.Single(m => m.Name == "AdvanceSimulation")).Any(c => c.Name == "get_Estimate"),
                    "fake decision cadence does not follow the cached/measured native AI rate");
                MethodDefinition beforeHit = visual.Methods.Single(m => m.Name == "BeforeHit");
                Check(beforeHit.Parameters.Count == 2, "fake-pose hit prefix signature drifted from Character.Damage");
                Check(visual.Methods.Any(m => Calls(m).Any(c => c.DeclaringType.FullName == "HarmonyLib.Harmony" &&
                    c.Name == "Patch")) && visual.Methods.Any(m => m.HasBody && m.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Ldstr && (string)i.Operand == "Damage")),
                    "fake-pose hit routing is not installed on Character.Damage");
                Check(visual.Methods.Any(m => m.HasBody && m.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Ldstr && (string)i.Operand == "GetTopPoint")) &&
                    visual.Methods.Any(m => m.HasBody && m.Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Ldstr && (string)i.Operand == "UpdateHuds")) &&
                    visual.Methods.Any(m => m.Name == "BeforeTopPoint" && m.Parameters.Count == 2),
                    "vanilla EnemyHud anchor is not redirected to the fake presentation top point");
                MethodDefinition advance = visual.Methods.Single(m => m.Name == "Advance");
                TypeDefinition takeover = types["TooManyLags.MobBridgeTakeover"];
                MethodDefinition poseRead = takeover.Methods.Single(m => m.Name == "get_Position");
                Check(!poseRead.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld) &&
                    !Calls(poseRead).Any(), "presentation pose getter mutates takeover state");
                Check(Calls(advance).Any(c => c.Name == "AdvancePosition"),
                    "takeover correction is not advanced in the update path");
                var advanceCalls = Calls(advance).ToList();
                Check(advanceCalls.FindIndex(c => c.Name == "BeginTakeover") < advanceCalls.FindIndex(c => c.Name == "AdvanceFrame") &&
                    advanceCalls.FindIndex(c => c.Name == "AdvancePosition") > advanceCalls.FindIndex(c => c.Name == "BeginTakeover"),
                    "already received matching pose is missed or its transition does not advance current motion");
                Check(advanceCalls.FindIndex(c => c.Name == "ManageBuffer") >= 0 &&
                    advanceCalls.FindIndex(c => c.Name == "ManageBuffer") < advanceCalls.FindIndex(c => c.Name == "AdvanceFrame"),
                    "finite-buffer evidence is not considered before advancing display time");
                Check(!Calls(timeline.Methods.Single(m => m.Name == "ManageBuffer")).Any(c =>
                    c.Name == "ObserveReal" || c.Name == "CompleteAtDestination") &&
                    Calls(advance).Any(c => c.Name == "ObservedDestination"),
                    "buffer prediction can replace observed-frontier takeover eligibility");
                Check(Calls(visual.Methods.Single(m => m.Name == "TakeoverTarget")).Any(c => c.Name == "GetVelocity" && c.DeclaringType.Name == "Character") &&
                    Calls(advance).Any(c => c.Name == "TakeoverTarget") &&
                    Calls(advance).Any(c => c.Name == "AdvancePosition" && c.Parameters.Count == 3),
                    "replica root correction can redefine movement direction without authoritative velocity");
                Check(!Calls(visual.Methods.Single(m => m.Name == "Valid")).Any(c => c.Name == "Settled") &&
                    Calls(visual.Methods.Single(m => m.Name == "Tick")).Any(c => c.Name == "TakeoverSettled"),
                    "retirement compares new native root with previous frame's presentation");
                MethodDefinition visualAttack = visual.Methods.Single(m => m.Name == "UpdateVisualAttack");
                Check(visualAttack.Body.Instructions.Any(i => i.Operand is FieldReference &&
                    ((FieldReference)i.Operand).Name == "AttackActive") &&
                    visualAttack.Body.Instructions.Any(i => i.Operand is FieldReference &&
                    ((FieldReference)i.Operand).Name == "AttackActiveObserved"),
                    "visual attack end ignores observed owner lifecycle");
                Check(!All(visual).Any(t => t.Fields.Any(f => f.Name == "InheritedDuration")) &&
                    Calls(visual.Methods.Single(m => m.Name == "ComputeModelPose")).Any(c => c.Name == "SampleRoot"),
                    "overlap still decays during playback or recorded model-local tilt is discarded");
                Check(Calls(visual.Methods.Single(m => m.Name == "ApplyPose")).Any(c => c.Name == "ModelPosition") &&
                    Calls(visual.Methods.Single(m => m.Name == "UpdatePresentation")).Any(c => c.Name == "ModelPosition"),
                    "drawn model and hitbox rig do not share scaled model position");
                Check(!timeline.Methods.Single(m => m.Name == "ObserveRealRate").Body.Instructions.Any(i =>
                    i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).Name == "playbackRate") &&
                    Calls(timeline.Methods.Single(m => m.Name == "AdvanceFrame")).Any(c => c.Name == "Integrate"),
                    "owner sample still steps rendered playback speed instead of integrating continuous rate");
                Check(Calls(visual.Methods.Single(m => m.Name == "ObserveClock"))
                    .Any(c => c.Name == "ObserveRealRate" && c.Parameters.Count == 2),
                    "playback transition ignores measured destination sample interval");
                MethodDefinition join = visual.Methods.Single(m => m.Name == "BeginTakeover");
                Check(Calls(join).Any(c => c.Name == "SettleForPlaybackCapped") &&
                    Calls(join).Any(c => c.Name == "OwnerReadySeconds") &&
                    !join.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_R8 && (double)i.Operand == .5d),
                    "final correction omits owner readiness or still halves playback before its base");
                var cappedSettle = takeover.Methods.Single(m => m.Name == "SettleForPlaybackCapped").Body.Instructions.ToList();
                Check(cappedSettle.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 2f) &&
                    cappedSettle.Any(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == .5f),
                    "final correction lacks the two-horizon or 500ms cap");
                Check(Calls(join).Any(c => c.Name == "TakeoverTarget") &&
                    Calls(join).Any(c => c.Name == ".ctor" && c.DeclaringType.Name == "MobBridgeTakeover" && c.Parameters.Count == 8),
                    "takeover discards accepted owner velocity before the first root delta");
                foreach (string reader in new[] { "ApplyRender", "CurrentPose", "BeforeTopPoint", "FollowHit" })
                    Check(!Calls(visual.Methods.Single(m => m.Name == reader)).Any(c => c.Name == "AdvancePosition"),
                        reader + " advances correction while reading presentation pose");
                Check(!Calls(advance).Any(c => c.DeclaringType.Name == "MobBridgeAttackGate"),
                    "visual attacks still wait at the former attack barrier");
                TypeDefinition animation = types["TooManyLags.MobBridgeAnimation"];
                var beforeCheckpoint = Calls(protocol.Methods.Single(m => m.Name == "BeforeTransfer")).ToList();
                Check(beforeCheckpoint.Any(c => c.DeclaringType.Name == "MobBridgeTiming" && c.Name == "Create") &&
                    Calls(protocol.Methods.Single(m => m.Name == "TimingFor")).Any(c => c.Name == "TryGetValue"),
                    "handoff ignores cached startup timing or derives no reserved window");
                Check(!Calls(visual.Methods.Single(m => m.Name == "Advance")).Any(c => c.DeclaringType.Name == "MobBridgeTiming") &&
                    !Calls(protocol.Methods.Single(m => m.Name == "AfterRandom")).Any(c => c.Name == "TimingFor" || c.Name == "LocalTiming"),
                    "arrival prediction/cache work was added to every fake frame or AI pass");
                TypeDefinition checkpoint = types["TooManyLags.MobBridgeCheckpoint"];
                foreach(string timingField in new[]{"Latency","FirstArrival","RecordingWindow","Horizon","PhysicsStep"})
                    Check(checkpoint.Fields.Any(f => f.Name == timingField),"checkpoint dropped distinct timing quantity: "+timingField);
                var bridgeTransfer = protocol.Methods.Single(m => m.Name == "BeforeTransfer").Body.Instructions.ToList();
                Check(bridgeTransfer.Any(i => i.Operand is MemberReference && ((MemberReference)i.Operand).Name.Contains("OwnerRevision")) &&
                    !bridgeTransfer.Any(i => i.OpCode == OpCodes.Isinst && i.Operand is TypeReference &&
                        ((TypeReference)i.Operand).Name == "Player"),
                    "non-initial bridge eligibility still depends on the target being a player");
                Check(!Calls(types["TooManyLags.MobBridgeSimulation"].Methods.Single(m => m.Name == ".ctor"))
                    .Any(c => c.Name == "Selected" || c.Name == "Isinst"),
                    "fake simulation still rejects non-player Character targets");
                Check(!Calls(types["TooManyLags.FgnCombatOwnership"].Methods.Single(m => m.Name == "HandoffObserve"))
                    .Any(c => c.Name == "TargetAllowsOwnership"),
                    "observer bridge readiness still depends on player-target ownership policy");
                Check(protocol.Methods.Single(m => m.Name == "ApplyPending").Body.Instructions.Count(i =>
                    i.Operand is FieldReference && ((FieldReference)i.Operand).DeclaringType.Name == "MobBridgeCheckpoint" &&
                    ((FieldReference)i.Operand).Name == "Horizon")>=3,
                    "destination target/RNG/clock scopes do not cover source reserve horizon");
                var applyPendingCalls = Calls(protocol.Methods.Single(m => m.Name == "ApplyPending")).ToList();
                Check(applyPendingCalls.Any(c => c.Name == "IsServer"),
                    "owner-gain basis does not distinguish server and client");
                Check(applyPendingCalls.Any(c => c.Name == "get_position" &&
                    c.DeclaringType.FullName == "UnityEngine.Transform"),
                    "owner-gain basis does not capture the displayed replica root");
                Check(Calls(protocol.Methods.Single(m => m.Name == "ApplyPending")).Any(c =>
                    c.Name == "PrimeOwnerAnimation" && c.DeclaringType.Name == "MobBridgeVisual") &&
                    Calls(visual.Methods.Single(m => m.Name == "PrimeOwnerAnimation")).Any(c =>
                        c.Name == "SetFloat" && c.DeclaringType.Name == "ZSyncAnimation"),
                    "owner gain does not prime native locomotion animation");
                var sourcePose = protocol.Methods.Single(m => m.Name == "ApplySourcePose").Body.Instructions.ToList();
                int sourceVelocity = sourcePose.FindIndex(i => i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).Name == "set_linearVelocity" &&
                    ((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.Rigidbody");
                int sourceTransform = sourcePose.FindIndex(i => i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).Name == "SetPositionAndRotation" &&
                    ((MethodReference)i.Operand).DeclaringType.FullName == "UnityEngine.Transform");
                Check(sourceVelocity >= 0 && sourceTransform > sourceVelocity,
                    "equal replicated pose skips checkpoint velocity before ownership gain");
                Check(Calls(protocol.Methods.Single(m => m.Name == "PollRate")).Count(c => c.Name == "Write" && c.Parameters.Count == 1 &&
                    c.Parameters[0].ParameterType.FullName == "System.Single")==7,
                    "existing timing RPC lost CPU rates or startup/cadence fields");
                Check(beforeCheckpoint.Any(c => c.DeclaringType.Name == "MobBridgeLatency" && c.Name == "Measure") &&
                    !beforeCheckpoint.Any(c => c.Name == "Invoke" && c.DeclaringType.FullName.Contains("Func`2<ZNetPeer,System.Single>")),
                    "Bridge timing still consumes conservative policy RTT fallback");
                Check(release ? !beforeCheckpoint.Any(c => c.Name == "Vanilla") : beforeCheckpoint.FindIndex(c => c.Name == "Vanilla") >= 0 &&
                    beforeCheckpoint.FindIndex(c => c.Name == "Vanilla") < beforeCheckpoint.FindIndex(c => c.Name == "Context"),
                    "vanilla transfer can prepare bridge state before scoped bypass");
                var receiveCheckpoint = Calls(protocol.Methods.Single(m => m.Name == "Receive")).ToList();
                Check(release ? !receiveCheckpoint.Any(c => c.Name == "Vanilla") : receiveCheckpoint.FindIndex(c => c.Name == "Vanilla") >= 0 &&
                    receiveCheckpoint.FindIndex(c => c.Name == "Vanilla") < receiveCheckpoint.FindIndex(c => c.Name == "Decode"),
                    "vanilla comparison can import late bridge checkpoint before bypass");
                Check(Calls(protocol.Methods.Single(m => m.Name == "Cancel")).Any(c => c.DeclaringType.Name == "MobBridgeVisual" && c.Name == "Cancel") &&
                    Calls(protocol.Methods.Single(m => m.Name == "Cancel")).Any(c => c.DeclaringType.Name == "MobBridgeEffects" && c.Name == "Cancel"),
                    "entering vanilla comparison leaves prior visual/effect epoch active");
                MethodDefinition producer = visual.Methods.Single(m => m.Name == "AdvanceSimulation");
                var captureInstructions = producer.Body.Instructions.ToList();
                int request = captureInstructions.FindIndex(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "get_AttackRequested");
                int command = captureInstructions.FindIndex(request, i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "Command");
                var attackBlock = captureInstructions.Skip(request).Take(command - request).ToList();
                Check(request >= 0 && command > request &&
                    attackBlock.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "CaptureAttack") &&
                    !attackBlock.Any(i => i.Operand is MethodReference &&
                        ((MethodReference)i.Operand).Name == "BeginVisualAttack" && ((MethodReference)i.Operand).DeclaringType.Name == "MobBridgeAnimation"),
                    "capture-time attack event still triggers the private visual animation");
                MethodDefinition beginVisualAttack = animation.Methods.Single(m => m.Name == "BeginVisualAttack");
                Check(Calls(beginVisualAttack).Any(c => c.Name == "TryAttackState") &&
                    Calls(beginVisualAttack).Any(c => c.Name == "Play") &&
                    Calls(beginVisualAttack).Any(c => c.Name == "SetTrigger"),
                    "visual attack retriggers without checking the active native attack phase");
                Check(!attackBlock.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "CloseRecording") &&
                    !attackBlock.Any(i => i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).Name == "Frozen"),
                    "predicted attack still closes or freezes recording");
                foreach (MethodDefinition method in animation.Methods.Concat(types["TooManyLags.MobBridgeAnimationRoot"].Methods))
                foreach (MethodReference call in Calls(method))
                    Check(!(call.DeclaringType.Name == "Attack" && (call.Name == "Start" || call.Name == "Update" || call.Name == "OnAttackTrigger")) &&
                        call.Name != "Damage" && call.Name != "ApplyDamage" && call.Name != "InvokeRPC" && call.Name != "SendMessage" && call.Name != "Instantiate",
                        "visual animation can execute gameplay or clone gameplay components: " + call.FullName);
                Check(animation.Methods.Any(m => Calls(m).Any(c => c.Name == "set_fireEvents")) &&
                    animation.Methods.Any(m => Calls(m).Any(c => c.Name == "SetTimeUpdateMode")), "preview animator is not manually driven with isolated events");
                Check(!animation.Fields.Any(f => f.FieldType.Name == "CharacterAnimEvent" || f.FieldType.Name == "ZNetView" || f.FieldType.Name == "Character"),
                    "visual attack receiver retains gameplay components");
                Check(Calls(animation.Methods.Single(m => m.Name == "Prepare")).Any(c => c.Name == "Sample") &&
                    !Calls(animation.Methods.Single(m => m.Name == "Apply")).Any(c => c.Name == "Sample") &&
                    !Calls(animation.Methods.Single(m => m.Name == "Apply")).Any(c => c.Name == "Evaluate" || c.Name == "SetTrigger"),
                    "rendering reruns attack logic instead of reading pose data");
                Check(Calls(visual.Methods.Single(m => m.Name == "UpdatePresentation"))
                    .Count(c => c.Name == "Prepare" && c.DeclaringType.Name == "MobBridgeAnimation") == 1,
                    "animation/root/hitboxes do not share one prepared presentation sample");
                Check(Calls(visual.Methods.Single(m => m.Name == "UpdatePresentation"))
                    .Any(c => c.DeclaringType.Name == "MobBridgeAnimationBlend" && c.Name == "NativeWeight"),
                    "animation entry/immutable endpoint does not fade through live native bones");
                Check(Calls(visual.Methods.Single(m => m.Name == "UpdatePresentation"))
                    .Any(c => c.Name == "AttackWeight" && c.DeclaringType.Name == "MobBridgeAnimationHistory"),
                    "native locomotion policy is not selected by buffered attack metadata");
                Check(!Calls(animation.Methods.Single(m => m.Name == "Prepare"))
                    .Any(c => c.Name == "get_localPosition" || c.Name == "get_localRotation"),
                    "early preparation still freezes the live native bone blend before culling/IK");
                Check(Calls(types["TooManyLags.MobBridgeObserver"].Methods.Single(m => m.Name == "Receive"))
                    .Any(c => c.Name == "DecodeForRelay" && c.DeclaringType.Name == "MobBridgeObserverPacket"),
                    "server relay still builds unused observer skeleton arrays");
                Check(Calls(animation.Methods.Single(m => m.Name == "Apply"))
                    .Any(c => c.Name == "get_localRotation") &&
                    Calls(animation.Methods.Single(m => m.Name == "Apply"))
                    .Any(c => c.Name == "Slerp"), "late bone application does not blend the fresh native pose");
                Check(Calls(visual.Methods.Single(m => m.Name == "BeforeDraw"))
                    .Any(c => c.Name == "Follow" && c.DeclaringType.Name == "MobBridgeHit"),
                    "late native bone blend is not synchronized with displayed hitboxes");
                foreach (string name in new[] { "ApplyRender", "ApplyPose", "FollowHit" })
                    Check(!Calls(visual.Methods.Single(m => m.Name == name)).Any(c => c.Name == "FakeTime" ||
                        c.Name == "AnimationTime" || c.Name == "AnimationWeight" || c.Name == "get_realtimeSinceStartupAsDouble"),
                        "presentation reader advances the animation blend clock: " + name);
                Check(new[] { "Start", "Unsubscribe" }.All(name => visual.Methods.Single(m => m.Name == name)
                    .Body.Instructions.Any(i => i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference &&
                        ((FieldReference)i.Operand).DeclaringType.Name == "Camera" && ((FieldReference)i.Operand).Name == "onPreRender")) &&
                    Calls(visual.Methods.Single(m => m.Name == "Start")).Any(c => c.Name == "Combine") &&
                    Calls(visual.Methods.Single(m => m.Name == "Unsubscribe")).Any(c => c.Name == "Remove"),
                    "draw-time rotation diagnostic callback is unbalanced");
                Check(new[]{"BeforeDraw","AfterRender"}.All(name => Calls(visual.Methods.Single(m => m.Name == name))
                    .Any(c => c.Name == "MeasureDraw") == !release), "draw diagnostic callbacks do not match build mode");
                Check(Calls(visual.Methods.Single(m => m.Name == "MeasureDraw")).Any() == !release,
                    "draw diagnostic body does not match build mode");
                TypeDefinition fx = types["TooManyLags.MobBridgeEffects"];
                TypeDefinition fxScope = types["TooManyLags.MobBridgeEffects/NativeFxScope"];
                Check(fx.Fields.Any(f => f.Name == "AfterOwnerSync") &&
                    protocol.Methods.Any(m => m.Name == "OwnerSyncPost") &&
                    Calls(protocol.Methods.Single(m => m.Name == "ApplyPending"))
                        .Any(c => c.Name == "ArmSourcePose"),
                    "checkpoint pose cannot be restored after native OwnerSync");
                var afterSync = fx.Methods.Single(m => m.Name == "AfterSync").Body.Instructions.ToList();
                int restorePose = afterSync.FindIndex(i => i.Operand is FieldReference &&
                    ((FieldReference)i.Operand).Name == "AfterOwnerSync");
                int presentationSync = afterSync.FindIndex(i => i.Operand is FieldReference &&
                    ((FieldReference)i.Operand).Name == "OwnershipSynced");
                Check(restorePose >= 0 && presentationSync > restorePose,
                    "visual rebase can read the stale root before checkpoint pose is restored");
                Check(Calls(protocol.Methods.Single(m => m.Name == "OwnerSyncPost"))
                    .Any(c => c.Name == "ApplySourcePose"),
                    "post-OwnerSync callback does not restore the checkpoint source pose");
                Check(Calls(protocol.Methods.Single(m => m.Name == "OwnerSyncPost"))
                        .Any(c => c.Name == "BeginOwnerGain") &&
                    Calls(protocol.Methods.Single(m => m.Name == "ArmSourcePose"))
                        .Any(c => c.Name == "BeginOwnerGain"),
                    "owner-gain visual smoothing is not armed after both ownership orderings");
                MethodDefinition ownerGain = visual.Methods.Single(m => m.Name == "BeginOwnerGain");
                Check(Calls(ownerGain).Any(c => c.Name == "GetVisual") &&
                    !Calls(ownerGain).Any(c => c.Name == "SetPositionAndRotation"),
                    "owner-gain smoothing moved gameplay root instead of visual presentation only");
                TypeDefinition ownerGainState = types["TooManyLags.MobBridgeVisual/OwnerGain"];
                MethodDefinition ownerGainPose = visual.Methods.Single(m => m.Name == "ComputeOwnerGainPose");
                Check(ownerGainState.Fields.Any(f => f.Name == "Motion" && f.FieldType.Name == "MobBridgeTakeover") &&
                    ownerGain.Parameters.Any(p => p.Name == "priorVelocity") &&
                    Calls(ownerGain).Any(c => c.Name == ".ctor" &&
                        c.DeclaringType.Name == "MobBridgeTakeover" && c.Parameters.Count == 8) &&
                    Calls(ownerGainPose).Any(c => c.Name == "AdvancePosition" &&
                        c.DeclaringType.Name == "MobBridgeTakeover"),
                    "owner gain does not preserve replica velocity through the takeover curve");
                Check(Calls(fx.Methods.Single(m => m.Name == "BeforeContinuous")).Any(c => c.DeclaringType.Name == "NativeFxScope" && c.Name == "Take") &&
                    !fx.Methods.Single(m => m.Name == "BeforeContinuous").Body.Instructions.Any(i => i.OpCode == OpCodes.Newobj &&
                        ((MethodReference)i.Operand).DeclaringType.Name == "NativeFxScope"),
                    "continuous FX still constructs scopes directly on every callback");
                MethodDefinition releaseScope = fxScope.Methods.Single(m => m.Name == "Release");
                Check(Calls(releaseScope).Any(c => c.Name == "Clear") &&
                    !fxScope.Fields.Any(f => f.FieldType.FullName.StartsWith("UnityEngine.", StringComparison.Ordinal)),
                    "FX scope pool retains direct Unity references or fails to clear values");
                Check(Calls(fx.Methods.Single(m => m.Name == "EndSession")).Any(c => c.DeclaringType.Name == "NativeFxScope" && c.Name == "Clear"),
                    "FX scope pool survives session teardown");
                TypeDefinition attachment = types["TooManyLags.MobBridgeEffectAttachment"];
                Check(Calls(fx.Methods.Single(m => m.Name == "BeforeParticles")).Any(c => c.Name == "PrepareEffectAnchors") &&
                    Calls(visual.Methods.Single(m => m.Name == "UpdatePresentation")).Any(c => c.Name == "Update" && c.DeclaringType.Name == "MobBridgeEffects"),
                    "attached FX emitter anchors do not share the prepared mob presentation");
                Check(visual.Methods.Single(m => m.Name == "Start").Body.Instructions.Any(i => i.Operand is FieldReference &&
                    ((FieldReference)i.Operand).Name == "VisualFxIds"),
                    "visual FX startup reads only the SEMan already cleared by combat relinquishment");
                Check(fx.Methods.Single(m => m.Name == "Created").Parameters.Any(p => p.Name == "__2" && p.ParameterType.Name == "Transform"),
                    "EffectList parent hook uses incorrect native parameter index");
                Check(Calls(fx.Methods.Single(m => m.Name == "GetAnchor")).Any(c => c.Name == "SetParent") &&
                    Calls(visual.Methods.Single(m => m.Name == "ApplyRender")).Any(c => c.Name == "Update" && c.DeclaringType.Name == "MobBridgeEffects") &&
                    Calls(visual.Methods.Single(m => m.Name == "RestorePose")).Any(c => c.Name == "Update" && c.DeclaringType.Name == "MobBridgeEffects"),
                    "effect ancestry or camera apply/restore double-offset correction is missing");
                Check(Calls(fx.Methods.Single(m => m.Name == "AfterContinuous")).Any(c => c.Name == "ExitNative"),
                    "continuous water/flight/slide FX overwrite attachment presentation without a native scope");
                Check(Calls(fx.Methods.Single(m => m.Name == "OnCreated")).Any(c => c.Name == "Expected") &&
                    Calls(fx.Methods.Single(m => m.Name == "OnCreated")).Any(c => c.Name == "IsOwner"),
                    "FX attachment metadata is written outside handoff ownership scope");
                Check(Calls(protocol.Methods.Single(m => m.Name == "ReceiveFinish")).Any(c => c.Name == "Finished" && c.DeclaringType.Name == "MobBridgeEffects"),
                    "effect attachment tagging lacks source visual-completion fencing");
                foreach (MethodDefinition method in fx.Methods.Concat(attachment.Methods))
                foreach (MethodReference call in Calls(method))
                    Check(call.Name != "Instantiate" && call.Name != "Simulate" && call.Name != "Emit" &&
                        call.Name != "ClaimOwnership" && call.Name != "Damage" && call.Name != "InvokeRPC",
                        "FX presentation clones/replays particles or changes gameplay authority: " + call.FullName);
                foreach (string method in new[] { "Stop", "RemoveAt" })
                    Check(Calls(visual.Methods.Single(m => m.Name == method)).Any(c => c.Name == "Dispose" && c.DeclaringType.Name == "MobBridgeEffects"),
                        "effect parent restoration missing on " + method);
                Check(!Calls(visual.Methods.Single(m => m.Name == "ApplyPose"))
                    .Any(c => c.DeclaringType.Name == "MobBridgeAnimation" && c.Name == "Apply") &&
                    Calls(visual.Methods.Single(m => m.Name == "BeforeDraw"))
                    .Count(c => c.DeclaringType.Name == "MobBridgeAnimation" && c.Name == "Apply") == 1,
                    "cached bones are not applied once at the late draw boundary");
                Check(Calls(hit.Methods.Single(m => m.Name == "Follow")).Any(c => c.Name == "TryPose") &&
                    !Calls(animation.Methods.Single(m => m.Name == "TryPose")).Any(c => c.DeclaringType.Name == "Transform" && c.Name.StartsWith("set_")),
                    "bone query does not read cached pose math or changes visible transforms");
                TypeDefinition skin = types["TooManyLags.MobBridgeSkin"];
                Check(Calls(skin.Methods.Single(m => m.IsConstructor && !m.IsStatic))
                    .Any(c => c.Name == "set_forceMatrixRecalculationPerRender"), "render overrides reuse stale skinning matrices");
                foreach (string name in new[] { "Stop", "RemoveAt" })
                    Check(Calls(visual.Methods.Single(m => m.Name == name))
                        .Any(c => c.Name == "Dispose" && c.DeclaringType.Name == "MobBridgeSkin"), "skin renderer settings leak at " + name);
                Check(Calls(visual.Methods.Single(m => m.Name == "Restore")).Any(c => c.Name == "RestorePose") &&
                    Calls(visual.Methods.Single(m => m.Name == "RestorePose")).Any(c => c.DeclaringType.Name == "MobBridgeAnimation" && c.Name == "Restore"),
                    "rendered attack bones leak into live animation/gameplay transforms");
                Check(advance.Body.Instructions.Any(i => i.Operand is MethodReference &&
                    ((MethodReference)i.Operand).Name == "MatchHorizon" &&
                    ((MethodReference)i.Operand).DeclaringType.Name == "MobBridgeTimeline"),
                    "near-horizon fake hold is not driven by observed destination sample age");
                Check(Calls(advance).Any(c => c.Name == "ObservedDestination") && Calls(advance).Any(c => c.Name == "get_End"),
                    "completion does not use observed destination and recorded playback horizon");
                TypeDefinition plugin = types["TooManyLags.Plugin"];
                MethodDefinition valid = visual.Methods.Single(m => m.Name == "Valid");
                Check(Calls(valid).Any(c => c.Name == "OwnershipChanged") && valid.Body.Instructions.Any(i =>
                    i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "CorrectionRevision"),
                    "active correction ignores a subsequent ownership epoch");
                Check(Calls(visual.Methods.Single(m => m.Name == "RemoveAt")).Any(c => c.Name == "RemoveOwnershipWatch") &&
                    visual.Methods.Single(m => m.Name == "Stop").Body.Instructions.Any(i => i.Operand is FieldReference &&
                    ((FieldReference)i.Operand).Name == "ownershipSync"), "ownership watcher leaks after track/world teardown");
                foreach (string method in new[] { "OwnershipChanged", "OwnershipSynced", "RebaseOwnership" })
                foreach (MethodReference call in Calls(visual.Methods.Single(m => m.Name == method)))
                    Check(!new[] { "Patch", "UnpatchSelf", "SetOwner", "set_position", "set_rotation", "set_linearVelocity", "Simulate",
                        "TryStep", "Evaluate", "Damage", "Finish" }.Contains(call.Name), "rare ownership correction changes gameplay/session matching: " + call.FullName);
                Check(!Calls(visual.Methods.Single(m => m.Name == "RebaseOwnership")).Any(c => c.Name == "Prepare" || c.Name == "Start"),
                    "ownership correction creates another simulation or animation rig");
                Check(!Calls(takeover.Methods.Single(m => m.Name == "AdvancePosition")).Any(c => c.Name == ".ctor"),
                    "ordinary correction frame allocates ownership state");
                Check(!Calls(visual.Methods.Single(m => m.Name == ".cctor")).Any(c => c.DeclaringType.Name.StartsWith("Dictionary", StringComparison.Ordinal)),
                    "ownership sync dictionary is allocated in ordinary sessions");
                TypeDefinition observer = types["TooManyLags.MobBridgeObserver"];
                foreach (MethodDefinition method in new[] { observer }.Concat(observer.NestedTypes).SelectMany(t => t.Methods))
                foreach (MethodReference call in Calls(method))
                    Check(!new[] { "SetOwner", "ForceSendZDO", "SyncNow", "UpdateAI", "StartAttack", "Damage", "Simulate", "SyncTransforms",
                        "set_position", "set_rotation", "set_linearVelocity" }.Contains(call.Name),
                        "observer stream changes native gameplay: " + call.FullName);
                var observerStart = visual.Methods.Single(m => m.Name == "StartObserver");
                Check(!Calls(observerStart).Any(c => c.DeclaringType.Name == "MobBridgeBody" || c.DeclaringType.Name == "MobBridgeSimulation" ||
                    c.DeclaringType.Name == "MobBridgeNativeWorld"), "observer creates another simulation/private physics world");
                foreach (string component in new[] { "MobBridgeHit", "MobBridgeSkin", "MobBridgeEffects", "MobBridgeAnimation" })
                    Check(Calls(observerStart).Any(c => c.DeclaringType.Name == component), "observer bypasses shared presentation: " + component);
                var observerAnimation = animation.Methods.Single(m => m.IsConstructor && m.Parameters.Any(p => p.ParameterType.FullName == "System.String[]"));
                Check(!Calls(observerAnimation).Any(c => c.Name == "AddComponent" || c.DeclaringType.Name == "PlayableGraph" ||
                    c.DeclaringType.Name == "AnimatorControllerPlayable"), "observer adapter builds/evaluates an animation graph");
                Check(Calls(observer.Methods.Single(m => m.Name == "Relevant")).Any(c => c.DeclaringType.Name == "ZNetScene" && c.Name == "InActiveArea"),
                    "observer uses invented radius instead of native active-area relevance");
                Check(Calls(observer.Methods.Single(m => m.Name == "Publish")).Any(c => c.Name == "Export") &&
                    !Calls(observer.Methods.Single(m => m.Name == "Publish")).Any(c => c.Name == "Evaluate" || c.Name == "Step"),
                    "observer export re-simulates/animates source recording");
                Check(Calls(advance).Any(c => c.Name == "ManageInputBuffer") && Calls(advance).Any(c => c.Name == "BeginTakeover"),
                    "observer lacks input-buffer budget/shared end correction");
                Check(Calls(protocol.Methods.Single(m => m.Name == "ReceiveFinish")).Any(c => c.DeclaringType.Name == "MobBridgeObserver" && c.Name == "KeepClock"),
                    "old-owner finish drops observer frontier clocks");
                Check(plugin.Methods.Single(m => m.Name == "LateUpdate").Body.Instructions.Any(i =>
                    i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Maintain"),
                    "transient hook maintenance is not drained from the main update");
            }
            Console.WriteLine("PASS: " + checks + " emitted mob bridge wiring and no-live-mutation assertions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
