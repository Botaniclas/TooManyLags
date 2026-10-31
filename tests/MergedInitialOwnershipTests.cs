using System;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static ZDO UnloadedMergedLog(float x)
    {
        ZDO data = PhysicsObject(x: x, owner: 0);
        ZNetView view = ZNetScene.instance.FindInstance(data);
        controller.Unregister(view); ZNetScene.instance.Views.Remove(data); return data;
    }
    private static void MergedInitialOwnership()
    {
        int before = checks;
        var patcher = new Harmony("botanic.authority.test.merged-initial");
        patcher.Patch(AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS"),
            transpiler: new HarmonyMethod(AccessTools.Method(typeof(OwnershipPatch), "Transpiler")));
        var physicsClient = AuthorityController.PhysicsClient;
        AuthorityController.PhysicsClient = null;
        try
        {
            Fresh(Kinds.LooseItems | Kinds.PhysicsObjects, defaultInteractives: true);
            var player = Peer(1); var egg = LooseInitialTemplate("ChickenEgg", 40); var log = UnloadedMergedLog(44);
            var eggRoot = ZNetScene.instance.Prefabs[egg.Hash]; var logRoot = ZNetScene.instance.Prefabs[log.Hash];
            Tick(0); ZNetScene.instance.Ready = delegate { return false; };
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position, 1);
            Check("combined categories preserve the 1.0.60 egg server default", egg.GetOwner() == 99);
            Check("combined categories apply the physics prop server default", log.GetOwner() == 99);
            Check("ordinary eggs never enter the physics category", ObjectCatalog.Classify(eggRoot) == Kinds.LooseItems);
            Check("physical logs never enter the loose-item category", ObjectCatalog.Classify(logRoot) == Kinds.PhysicsObjects);
            
            controller.OwnerChanged(egg); controller.OwnerChanged(log);
            Check("neither dynamic category enters the stationary initial-proof path", !controller.InitialObject(egg) && !controller.InitialObject(log));
            Check("combined initial defaults add no control RPC", player.m_rpc.Calls == 0);

            Fresh(Kinds.LooseItems | Kinds.PhysicsObjects, defaultInteractives: true, directional: true,
                cellSize: 4, pattern: GridCoveragePattern.Rounded7x7);
            player = Peer(1); Tick(0);
            egg = LooseInitialTemplate("ChickenEgg", 1); log = UnloadedMergedLog(1);
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position, 1);
            Check("settled red field owns eggs during native initial assignment", egg.GetOwner() == 1);
            Check("unsupported physics client retains server default despite a settled field", log.GetOwner() == 99);
            AuthorityController.PhysicsClient = delegate { return true; };
            var supportedLog = UnloadedMergedLog(1);
            ZDOMan.instance.ReleaseNearbyZDOS(player.Position, 1);
            Check("settled red field owns logs with a physics-capable client", supportedLog.GetOwner() == 1);
            Check("existing server-owned log is not stolen by a new native assignment", log.GetOwner() == 99);
            AuthorityController.PhysicsClient = null;

            foreach (bool enablePhysics in new[] { false, true })
            {
                Fresh(enablePhysics ? Kinds.PhysicsObjects : Kinds.LooseItems, defaultInteractives: true);
                player = Peer(1); egg = LooseInitialTemplate("ChickenEgg", 40); log = UnloadedMergedLog(44); Tick(0);
                ZDOMan.instance.ReleaseNearbyZDOS(player.Position, 1);
                Check("loose-item category toggle remains independent of physics", egg.GetOwner() == (enablePhysics ? 1 : 99));
                Check("physics category toggle remains independent of loose items", log.GetOwner() == (enablePhysics ? 99 : 1));
            }
        }
        finally { patcher.UnpatchSelf();  AuthorityController.PhysicsClient = physicsClient; }
        Console.WriteLine("PASS: " + (checks - before) + " combined 1.0.60 loose-item / physics initial-owner regressions.");
    }
}
