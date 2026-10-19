using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
internal static class NativeAudit
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    private static int Main(string[] args)
    {
        try
        {
            using (var game = AssemblyDefinition.ReadAssembly(args[0]))
            using (var fork = AssemblyDefinition.ReadAssembly(args[1]))
            using (var addon = AssemblyDefinition.ReadAssembly(args[2]))
            {
                Func<string, TypeDefinition> type = name => game.MainModule.GetType(name);
                foreach (var entry in new[] { new[] { "ZNetScene", "AddInstance", "ZDO", "ZNetView" }, new[] { "ZNetView", "ResetZDO" }, new[] { "ZNetView", "OnDestroy" }, new[] { "ZNet", "Shutdown", "System.Boolean" }, new[] { "ZRoutedRpc", "RouteRPC", "ZRoutedRpc/RoutedRPCData" }, new[] { "ZRoutedRpc", "HandleRoutedRPC", "ZRoutedRpc/RoutedRPCData" }, new[] { "ZDOMan", "RPC_ZDOData", "ZRpc", "ZPackage" }, new[] { "ZDO", "Deserialize", "ZPackage" }, new[] { "ZDO", "SetOwnerInternal", "System.Int64" }, new[] { "ZDO", "InternalSetPosition", "UnityEngine.Vector3" }, new[] { "ZDO", "Set", "System.Int32", "System.Boolean" }, new[] { "ZDO", "Set", "System.Int32", "System.Int64" }, new[] { "ZDO", "Set", "System.Int32", "System.String" }, new[] { "Fish", "OnHooked", "FishingFloat" } })
                    Check("native Harmony target " + entry[0] + "." + entry[1], type(entry[0]).Methods.Any(m => m.Name == entry[1] && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(entry.Skip(2))));
                Check("PickableItem completion field is boolean", type("PickableItem").Fields.Any(f => f.Name == "m_picked" && f.FieldType.FullName == "System.Boolean"));
                Check("Pickable completion remains available", type("Pickable").Methods.Any(m => m.Name == "GetPicked" && m.ReturnType.FullName == "System.Boolean"));
                Check("native velocity field available", type("ZDOVars").Fields.Any(f => f.Name == "s_velHash" && f.FieldType.FullName == "System.Int32"));
                Check("native owner sync publishes velocity", type("ZSyncTransform").Methods.Single(m => m.Name == "OwnerSync").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).FullName == "System.Int32 ZDOVars::s_velHash"));
                Check("native velocity read has expected shape", type("ZDO").Methods.Any(m => m.Name == "GetVec3" && m.ReturnType.FullName == "UnityEngine.Vector3" && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.Int32", "UnityEngine.Vector3" })));
                foreach (string key in new[] { "s_tamed", "s_user", "s_follow", "s_alert", "s_haveTargetHash", "s_attachJointHash", "s_piece", "s_hooked" })
                    Check("replicated dynamic-state key " + key, type("ZDOVars").Fields.Any(f => f.Name == key && f.FieldType.FullName == "System.Int32"));
                Check("native attachment uses string data", type("ZSyncTransform").Methods.Single(m => m.Name == "OwnerSync").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "Set" && ((MethodReference)i.Operand).Parameters.Last().ParameterType.FullName == "System.String"));
                Check("native item grant writes ownership", type("ItemDrop").Methods.Single(m => m.Name == "RPC_RequestOwn").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "SetOwner"));
                Check("native fish grant is directed Pickup response", type("Fish").Methods.Single(m => m.Name == "RPC_RequestPickup").Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "Pickup"));
                Check("native fish successful grant claims ownership", type("Fish").Methods.Single(m => m.Name == "RPC_Pickup").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "ClaimOwnership"));
                Check("native loaded-area predicate available", type("ZNetScene").Methods.Any(m => m.Name == "IsAreaReady" && m.Parameters.Count == 1 && m.ReturnType.FullName == "System.Boolean"));
                Check("static-position field available", type("WearNTear").Fields.Any(f => f.Name == "m_staticPosition" && f.FieldType.FullName == "System.Boolean"));
                Check("native AI replicates only target-present boolean", type("BaseAI").Methods.Single(m => m.Name == "SetTargetInfo").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName == "System.Void ZDO::Set(System.Int32,System.Boolean)"));
                Check("native monster target getter reads owner-local reference", type("MonsterAI").Methods.Single(m => m.Name == "GetTargetCreature").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "m_targetCreature"));
                Check("native unfinished attack instance has expected type", type("Humanoid").Fields.Any(f => f.Name == "m_currentAttack" && f.FieldType.FullName == "Attack" && !f.IsStatic));
                Check("native unarmed fallback definition", type("Humanoid").Fields.Any(f => f.Name == "m_unarmedWeapon" && f.FieldType.FullName == "ItemDrop" && f.IsPublic));
                Check("native Attack exposes completion", type("Attack").Methods.Any(m => m.Name == "IsDone" && m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 0));
                Check("native animation observer signature", type("ZSyncAnimation").Methods.Any(m => m.Name == "RPC_SetTrigger" && m.ReturnType.FullName == "System.Void" && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.Int64", "System.String" })));
                Check("native trigger broadcasts do not contain concrete targets", type("ZSyncAnimation").Methods.Single(m => m.Name == "SetTrigger").Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.String" }));
                Check("native AI charging predicate available", type("BaseAI").Methods.Any(m => m.Name == "IsCharging" && m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 0));
                Check("native boss predicate reads the actual boss flag", type("Character").Methods.Single(m => m.Name == "IsBoss").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "m_boss"));
                var itemData = type("ItemDrop").NestedTypes.Single(t => t.Name == "ItemData");
                var attackShared = itemData.NestedTypes.Single(t => t.Name == "SharedData");
                foreach (string key in new[] { "m_aiAttackInterval", "m_aiAttackRange", "m_aiAttackRangeMin", "m_aiAttackMaxAngle" })
                    Check("native forecast metadata " + key, attackShared.Fields.Any(f => f.Name == key && f.FieldType.FullName == "System.Single"));
                Check("native inverted attack-angle metadata", attackShared.Fields.Any(f => f.Name == "m_aiInvertAngleCheck" && f.FieldType.FullName == "System.Boolean"));
                Check("native attack-target metadata", attackShared.Fields.Any(f => f.Name == "m_aiTargetType" && f.FieldType.Name == "AiTarget"));
                Check("native weapon classifier", itemData.Methods.Any(m => m.Name == "IsWeapon" && m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 0));
                Check("native forecast faction predicate", type("BaseAI").Methods.Any(m => m.Name == "IsEnemy" && m.IsStatic && m.Parameters.Count == 2));
                Check("native forecast target radius", type("Character").Methods.Any(m => m.Name == "GetRadius" && m.ReturnType.FullName == "System.Single" && m.Parameters.Count == 0));
                Check("native loaded character list is reused", type("Character").Methods.Single(m => m.Name == "GetAllCharacters").Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "s_characters"));
                Check("native forecast death predicate", type("Character").Methods.Any(m => m.Name == "IsDead" && m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 0));
                Check("native building attack policy", type("MonsterAI").Fields.Any(f => f.Name == "m_attackPlayerObjects" && f.FieldType.FullName == "System.Boolean"));
                Check("native direct loaded-view lookup", type("ZNetScene").Methods.Any(m => m.Name == "FindInstance" && m.ReturnType.FullName == "ZNetView" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "ZDO"));
                Check("native hostility state key", type("ZDOVars").Fields.Any(f => f.Name == "s_aggravated" && f.FieldType.FullName == "System.Int32"));
                foreach (string method in new[] { "IsPriorityTarget", "IsRandomTarget", "FindClosestPoint", "GetCenter" })
                    Check("native static forecast target " + method, type("StaticTarget").Methods.Any(m => m.Name == method && m.IsPublic));
                Check("mob guard accesses private lifecycle without native field writes", !addon.MainModule.GetType("TooManyLagsDedicatedAddon.MobAttackAccess").Methods.SelectMany(m => m.HasBody ? m.Body.Instructions : new Mono.Collections.Generic.Collection<Instruction>()).Any(i => i.OpCode == OpCodes.Stfld));
                foreach (var entry in new[] { new[] { "Smelter", "RPC_AddOre", "System.Int64", "System.String", "System.Boolean" }, new[] { "Smelter", "RPC_AddFuel", "System.Int64" }, new[] { "CookingStation", "RPC_AddItem", "System.Int64", "System.String", "System.Boolean" }, new[] { "CookingStation", "RPC_AddFuel", "System.Int64" }, new[] { "Fireplace", "RPC_AddFuel", "System.Int64" }, new[] { "ShieldGenerator", "RPC_AddFuel", "System.Int64" }, new[] { "ShieldGenerator", "RPC_SetFuel", "System.Int64", "System.Single" }, new[] { "Turret", "RPC_AddAmmo", "System.Int64", "System.String" } })
                    Check("native refill contract " + entry[0] + "." + entry[1], type(entry[0]).Methods.Any(m => m.Name == entry[1] && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(entry.Skip(2))));
                Check("native cooking slot field has expected shape", type("CookingStation").Fields.Any(f => f.Name == "m_slots" && f.FieldType.FullName == "UnityEngine.Transform[]"));
                foreach (string key in new[] { "s_fuel", "s_queued", "s_ammo" }) Check("native refill-state key " + key, type("ZDOVars").Fields.Any(f => f.Name == key));
                Func<MethodDefinition, string, bool> calls = (method, name) => method.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == name);
                Func<MethodDefinition, string, bool> field = (method, name) => method.Body.Instructions.Any(i => i.Operand is FieldReference && ((FieldReference)i.Operand).Name == name);
                var open = type("Container").Methods.Single(m => m.Name == "RPC_RequestOpen");
                Check("native inventory opening checks local use", calls(open, "IsInUse"));
                Check("native inventory opening grants owner explicitly", calls(open, "SetOwner"));
                Check("native cart cargo field available", type("Vagon").Fields.Any(f => f.Name == "m_container" && f.IsPublic && f.FieldType.FullName == "Container"));
                Check("native cargo links back to pull guard", type("Container").Fields.Any(f => f.Name == "m_wagon" && f.IsPublic && f.FieldType.FullName == "Vagon"));
                Check("native open query checks source cart use", open.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "Vagon" && ((MethodReference)i.Operand).Name == "InUse"));
                var cartUse = type("Vagon").Methods.Single(m => m.Name == "InUse");
                Check("native cart use checks cargo", calls(cartUse, "IsInUse"));
                Check("native cart use checks actual joint", calls(cartUse, "IsAttached"));
                var attached = type("Vagon").Methods.Single(m => m.Name == "IsAttached" && m.Parameters.Count == 0);
                Check("cart joint marker is Boolean rather than transform string", attached.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "ZDO" && ((MethodReference)i.Operand).Name == "GetBool"));
                var cartIdle = addon.MainModule.GetType("TooManyLagsDedicatedAddon.Controller").Methods.Single(m => m.Name == "CartIdle");
                Check("production cart guard uses Boolean marker without transform string fallback", calls(cartIdle, "GetBool") && !calls(cartIdle, "GetString"));
                foreach (string method in new[] { "AttachTo", "Detach" })
                    Check("native cart Boolean state event " + method, type("Vagon").Methods.Single(m => m.Name == method).Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName == "System.Void ZDO::Set(System.Int32,System.Boolean)"));
                var hitch = type("Vagon").Methods.Single(m => m.Name == "RPC_RequestOwn");
                Check("native hitch request commits ownership", calls(hitch, "SetOwner"));
                Check("native hitch denial name retained", hitch.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "RPC_RequestDenied"));
                Check("hitch start has private pending requester", type("Vagon").Fields.Any(f => f.Name == "m_useRequester" && f.IsPrivate && f.FieldType.FullName == "Humanoid"));
                Check("native hitch completion deferred to owner update", field(type("Vagon").Methods.Single(m => m.Name == "Update"), "m_useRequester") && calls(type("Vagon").Methods.Single(m => m.Name == "Update"), "AttachTo"));
                Check("ship driver request keeps native user without directly assigning owner", calls(type("ShipControlls").Methods.Single(m => m.Name == "RPC_RequestControl"), "Set") && !calls(type("ShipControlls").Methods.Single(m => m.Name == "RPC_RequestControl"), "SetOwner"));
                Check("native helm controller identifies ship", type("ShipControlls").Fields.Any(f => f.Name == "m_ship" && f.FieldType.FullName == "Ship"));
                Check("native ship helm field available", type("Ship").Fields.Any(f => f.Name == "m_shipControlls" && f.FieldType.FullName == "ShipControlls"));
                Check("native ship impact protection is local float", type("Ship").Fields.Any(f => f.Name == "m_lastWaterImpactTime" && !f.IsStatic && f.FieldType.FullName == "System.Single"));
                Check("native quaternion setter supports helm reindex hook", type("ZDO").Methods.Any(m => m.Name == "SetRotation" && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "UnityEngine.Quaternion" })));
                foreach (string method in new[] { "RPC_Forward", "RPC_Backward", "RPC_Stop", "RPC_Rudder" })
                    Check("native ship steering handler retained " + method, type("Ship").Methods.Any(m => m.Name == method && m.Parameters[0].ParameterType.FullName == "System.Int64"));
                foreach (string patch in new[] { "Ship_UpdateOwner_Patch", "ZRoutedRpc_RouteRPC_Patch" })
                    Check("released backend ship ownership patch available " + patch, fork.MainModule.GetType("Valheim_Serverside.Features.Core").NestedTypes.Any(t => t.Name == patch && t.Methods.Any(m => m.Name == "Prefix")));
                Check("released backend helm reply contains one owner assignment", fork.MainModule.GetType("Valheim_Serverside.Features.Core").NestedTypes.Single(t => t.Name == "ZRoutedRpc_RouteRPC_Patch").Methods.Single(m => m.Name == "Prefix").Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName == "System.Void ZDO::SetOwner(System.Int64)") == 1);
                Check("native inventory in-use getter reads local flag", field(type("Container").Methods.Single(m => m.Name == "IsInUse"), "m_inUse"));
                Check("native inventory root override is a network view", type("Container").Fields.Any(f => f.Name == "m_rootObjectOverride" && f.FieldType.FullName == "ZNetView"));
                Check("native loaded instance count has constant-time API", type("ZNetScene").Methods.Any(m => m.Name == "NrOfInstances" && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Int32"));
                Check("native character announcement hook shape", type("ZNet").Methods.Any(m => m.Name == "RPC_CharacterID" && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "ZRpc", "ZDOID" })));
                Check("native inventory response really opens GUI", calls(type("Container").Methods.Single(m => m.Name == "RPC_OpenResponse"), "Show"));
                Check("native open request takes sender and profile", open.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.Int64", "System.Int64" }));
                var openReply = type("Container").Methods.Single(m => m.Name == "RPC_OpenResponse");
                Check("native open reply takes sender and grant", openReply.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.Int64", "System.Boolean" }));
                Check("native open reply requires local player", field(openReply, "m_localPlayer"));
                Check("native opening schedules object synchronization", calls(open, "ForceSendZDO"));
                Check("native public privacy enum available", type("Container").NestedTypes.Any(t => t.Name == "PrivacySetting" && t.Fields.Any(f => f.Name == "Public")));
                Check("source native inventory bridge never forces ownership", !addon.MainModule.GetType("TooManyLagsDedicatedAddon.NativeInventoryHandoff").Methods.Where(m => m.HasBody).Any(m => calls(m, "SetOwner")));
                Check("direct native owner assignment has no inventory-use check", !calls(type("ZDO").Methods.Single(m => m.Name == "SetOwner"), "IsInUse"));
                Check("incinerator running flag is private and local", type("Incinerator").Fields.Any(f => f.Name == "isInUse" && f.IsPrivate && f.FieldType.FullName == "System.Boolean"));
                Check("incinerator completion clears actual flag", field(type("Incinerator").Methods.Single(m => m.Name == "StopAOE"), "isInUse"));
                Check("incinerator coroutine writes actual running flag", type("Incinerator").NestedTypes.SelectMany(t => t.Methods).Where(m => m.HasBody).Any(m => field(m, "isInUse") && calls(m, "IsOwner")));
                var tap = type("Fermenter").Methods.Single(m => m.Name == "RPC_Tap");
                Check("fermenter tap queues local delayed output", field(tap, "m_delayedTapItem") && calls(tap, "Invoke"));
                Check("fermenter pending item is not cleared on output", !type("Fermenter").Methods.Single(m => m.Name == "DelayedTap").Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference && ((FieldReference)i.Operand).Name == "m_delayedTapItem"));
                Check("boss queue predicate uses local Invoke state", calls(type("OfferingBowl").Methods.Single(m => m.Name == "IsBossSpawnQueued"), "IsInvoking"));
                foreach (string stand in new[] { "ItemStand", "ArmorStand" })
                    Check("stand attachment depends on requester-local queued item " + stand, type(stand).Fields.Any(f => f.Name == "m_queuedItem" && f.IsPrivate) && field(type(stand).Methods.Single(m => m.Name == "UpdateAttach"), "m_queuedItem"));
                var snapshot = addon.MainModule.GetType("TooManyLagsDedicatedAddon.NativeSnapshot").Methods.Single(m => m.Name == "Send");
                var writes = snapshot.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand).Where(m => m.DeclaringType.FullName == "ZPackage" && m.Name == "Write").Select(m => m.Parameters[0].ParameterType.FullName).ToArray();
                Check("targeted snapshot uses native typed envelope", writes.SequenceEqual(new[] { "System.Int32", "ZDOID", "System.UInt16", "System.UInt32", "System.Int64", "UnityEngine.Vector3", "ZPackage", "ZDOID" }));
                Check("snapshot uses native object serializer", snapshot.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName == "System.Void ZDO::Serialize(ZPackage)"));
                var readNames = type("ZDOMan").Methods.Single(m => m.Name == "RPC_ZDOData").Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand).Where(m => m.DeclaringType.FullName == "ZPackage" && m.Name.StartsWith("Read")).Select(m => m.Name).ToArray();
                Check("installed native receiver matches targeted envelope", readNames.SequenceEqual(new[] { "ReadInt", "ReadZDOID", "ReadZDOID", "ReadUShort", "ReadUInt", "ReadLong", "ReadVector3", "ReadPackage" }));
                foreach (string name in new[] { "Sign", "TeleportWorld", "Container", "Incinerator", "Ship", "Vagon" })
                    Check("no addon patch replaces native " + name + " transactions", !addon.MainModule.Types.SelectMany(t => t.CustomAttributes).Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").SelectMany(a => a.ConstructorArguments).Any(a => a.Value is TypeReference && ((TypeReference)a.Value).FullName == name));
                Check("native sign submit still claims ownership", type("Sign").Methods.Single(m => m.Name == "SetText").Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "ClaimOwnership"));
                Check("native portal submit still uses SetTag RPC", type("TeleportWorld").Methods.Single(m => m.Name == "SetText").Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "RPC_SetTag"));
                var plugin = fork.MainModule.GetType("Valheim_Serverside.ServersidePlugin");
                Check("released backend uses per-feature Harmony identities", plugin.Methods.Single(m => m.Name == "PatchFeatures").Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "MVP.Valheim_Serverside_Simulations."));
                Check("private installed flag available", plugin.Fields.Any(f => f.Name == "installed" && f.IsStatic && f.FieldType.FullName == "System.Boolean"));
                var owner = fork.MainModule.GetType("Valheim_Serverside.Features.Core").NestedTypes.Single(t => t.Name == "ZDOMan_ReleaseNearbyZDOS_Patch").Methods.Single(m => m.Name == "Prefix");
                Check("fork prefix exact shape", owner.IsStatic && owner.ReturnType.FullName == "System.Boolean" && owner.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "ZDOMan", "UnityEngine.Vector3", "System.Int64" }));
                Check("exactly two native assignment sites", owner.Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName == "System.Void ZDO::SetOwner(System.Int64)") == 2);
                foreach (string call in new[] { "get_Persistent", "IsInPeerActiveArea", "InActiveArea", "GetOwner" }) Check("native dependency rule retained " + call, owner.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == call));
                Check("no FGN reference", !addon.MainModule.AssemblyReferences.Any(a => a.Name.IndexOf("Ghetto", StringComparison.OrdinalIgnoreCase) >= 0));
                Check("no fork binary reference", !addon.MainModule.AssemblyReferences.Any(a => a.Name.IndexOf("Sarkastic", StringComparison.OrdinalIgnoreCase) >= 0));
                Check("no client networking implementation", !addon.MainModule.Types.Any(t => t.Name == "Network" || t.Name == "LocalAgent" || t.Name == "CameraPublisher"));
                var assignment = addon.MainModule.GetType("TooManyLagsDedicatedAddon.ForkAssignment").Methods.Single(m => m.Name == "Assign");
                Check("native assignment still writes once", assignment.Body.Instructions.Count(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "SetOwner") == 1);
                var write = assignment.Body.Instructions.Single(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "SetOwner");
                Check("native write remains outside addon exception handler", assignment.Body.HasExceptionHandlers && assignment.Body.ExceptionHandlers.All(h => write.Offset >= h.HandlerEnd.Offset));
                Check("native Steam connection field has the expected handle", type("ZSteamSocket").Fields.Any(f => f.Name == "m_con" && f.FieldType.FullName == "Steamworks.HSteamNetConnection"));
                var latency = addon.MainModule.GetType("TooManyLagsDedicatedAddon.SocketLatency").Methods.Single(m => m.Name == "Read");
                Check("dedicated Steam telemetry calls GameServer API", latency.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "Steamworks.SteamGameServerNetworkingSockets" && ((MethodReference)i.Operand).Name == "GetConnectionRealTimeStatus"));
                Check("dedicated adapter never calls Steam client API", !latency.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.FullName == "Steamworks.SteamNetworkingSockets"));
                using (var steam = AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(args[0]), "com.rlabrecque.steamworks.net.dll")))
                {
                    var serverStatus = steam.MainModule.GetType("Steamworks.SteamGameServerNetworkingSockets").Methods.Single(m => m.Name == "GetConnectionRealTimeStatus");
                    Check("real Steam GameServer status signature", serverStatus.ReturnType.FullName == "Steamworks.EResult" && serverStatus.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "Steamworks.HSteamNetConnection", "Steamworks.SteamNetConnectionRealTimeStatus_t&", "System.Int32", "Steamworks.SteamNetConnectionRealTimeLaneStatus_t&" }));
                    Check("real server API validates GameServer availability", calls(serverStatus, "TestIfAvailableGameServer"));
                    Check("real server API uses GameServer networking context", serverStatus.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name == "CSteamGameServerAPIContext"));
                    Check("Steam status publishes integer ping", steam.MainModule.GetType("Steamworks.SteamNetConnectionRealTimeStatus_t").Fields.Any(f => f.Name == "m_nPing" && f.FieldType.FullName == "System.Int32"));
                }
            }
            Console.WriteLine("PASS: " + checks + " real native/released-fork metadata and ownership contract assertions. No live world claim."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
