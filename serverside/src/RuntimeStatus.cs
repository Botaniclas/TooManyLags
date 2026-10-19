using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    internal sealed partial class Controller
    {
        private int passes, registrationCalls, validRegistrations, assignCalls;
        internal IEnumerable<string> DescribeRuntime()
        {
            if (options.MobOwnershipTest) yield return DescribeMobTest();
            yield return "GRID runtime: ticks=" + passes + "; connectedPeers=" + ZNet.instance.GetPeers().Count +
                "; acceptedPlayers=" + peers.Count + "; sceneInstances=" + ZNetScene.instance.NrOfInstances() +
                "; registrations=" + registrationCalls + "; validRegistrations=" + validRegistrations + "; tracked=" + nodes.Count +
                "; buckets=" + world.BucketCount + "; cells=" + world.Grid.CachedCells + "; attempts=" + world.Attempts +
                "; assignCalls=" + assignCalls + "; waitingCells=" + waiting.Count + "; pendingGrace=" + grace.Count +
                "; pendingMobs=" + pendingMobs.Count + "; sharedMobs=" + sharedMobs.Count;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                ZDO player = peer.m_characterID.IsNone() ? null : ZDOMan.instance.GetZDO(peer.m_characterID);
                bool accepted = peers.Exists(delegate(GridPeer item) { return item.Uid == peer.m_uid; });
                Latency measured; int ping = latency.TryGetValue(peer.m_uid, out measured) ? measured.Ping : 0;
                yield return "GRID peer: uid=" + peer.m_uid + "; ready=" + peer.IsReady() + "; hasCharacterId=" + !peer.m_characterID.IsNone() +
                    "; characterFound=" + (player != null) + "; valid=" + (player != null && player.IsValid()) +
                    "; characterOwner=" + (player != null ? player.GetOwner() : 0) + "; accepted=" + accepted +
                    "; reference=" + Coordinates(peer.GetRefPos()) + "; character=" + (player != null ? Coordinates(player.GetPosition()) : "missing") +
                    "; cachedPingMs=" + ping;
            }
            int samples = 0;
            foreach (Node node in nodes.Values)
            {
                if (samples++ == 4) break;
                yield return "GRID node: prefab=" + node.Data.GetPrefab() + "; group=" + node.Group + "; owner=" + node.Data.GetOwner() +
                    "; field=" + world.Grid.Owner(node.Cell) + "; candidate=" + world.Grid.Candidate(node.Cell) +
                    "; participants=" + world.Grid.Participants(node.Cell) + "; inGrid=" + node.InGrid + "; managed=" + node.Managed +
                    "; released=" + node.Released + "; idle=" + Catalog.Idle(node.Data) + "; position=" + Coordinates(node.Data.GetPosition());
            }
            foreach (string name in new[] { "Pickable_Stone", "Pickable_Branch", "RaspberryBush", "BlueberryBush", "wood_door", "piece_chest_wood" })
                yield return catalog.DescribePrefab(name);
        }
        private static string Coordinates(Vector3 value)
        { return String.Format(CultureInfo.InvariantCulture, "({0:F1},{1:F1})", value.x, value.z); }
    }
}
