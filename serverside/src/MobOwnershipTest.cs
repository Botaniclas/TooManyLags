using System;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    // Explicit solo diagnostic. Only the desired owner is overridden; ApplyMob,
    // actual attack observation, forecast, source revision and area checks stay native.
    internal sealed partial class Controller
    {
        private Node mobTestNode;
        private long mobTestPlayer, mobTestTarget;
        private float mobTestStarted, mobTestNext, mobTestReport;
        private bool mobTestStopped, mobTestPending;
        private int mobTestRequests, mobTestTransfers;
        internal Action<string> MobTestLog = null;
        internal bool MobTestActive { get { return mobTestNode != null; } }
        internal int MobTestTransfers { get { return mobTestTransfers; } }
        private void MobTestNote(string message)
        { if (MobTestLog != null) MobTestLog("MOB TEST: " + message); }
        internal string DescribeMobTest()
        {
            return "MOB TEST: enabled=" + options.MobOwnershipTest + "; active=" + (mobTestNode != null) + "; stopped=" + mobTestStopped +
                "; requests=" + mobTestRequests + "; guardedTransfers=" + mobTestTransfers +
                (mobTestNode == null ? "" : "; prefab=" + mobTestNode.Data.GetPrefab() + "; owner=" + mobTestNode.Data.GetOwner() +
                "; desired=" + mobTestTarget + "; pending=" + mobTestPending + "; forecast=" + forecastWaiting.Contains(mobTestNode) +
                "; inAttack=" + (mobTestNode.Mob.Actor && mobTestNode.Mob.Actor.InAttack()) +
                "; armed=" + mobTestNode.Mob.Armed + "; combat=" + mobTestNode.Mob.Combat + "; idleHandoffObserved=" + mobTestNode.Mob.IdleHandoffObserved +
                "; observedAttack=" + mobTestNode.Mob.SawAttack + "; looping=" + mobTestNode.Mob.Looping);
        }
        internal void StopMobTest(string reason)
        { StopMobTest(reason, true); }
        private void StopMobTest(string reason, bool restore)
        {
            Node previous = mobTestNode;
            if (mobTestStopped) return;
            mobTestStopped = true; mobTestNode = null; mobTestPending = false;
            if (previous != null)
            {
                CancelMob(previous);
                if (restore && !previous.Removed && previous.InGrid) world.Received(previous, Time.unscaledTime);
            }
            MobTestNote("stopped: " + reason + "; guardedTransfers=" + mobTestTransfers + ". Normal ownership policy resumes; no forced reset.");
        }
        private void TickMobTest(float now)
        {
            if (mobTestStopped || peers.Count == 0) return;
            if (peers.Count != 1) { StopMobTest("requires exactly one online player"); return; }
            ZNetPeer peer = ZNet.instance.GetPeer(peers[0].Uid);
            ZDO player = peer == null ? null : ZDOMan.instance.GetZDO(peer.m_characterID);
            if (player == null || player.GetFloat(ZDOVars.s_health, 1f) <= 0f) { if (mobTestNode != null) StopMobTest("player unavailable or dead"); return; }
            if (mobTestNode == null)
            {
                if (now < mobTestNext) return;
                mobTestNext = now + 1f; float nearest = 20f * 20f;
                // Selection only while this explicit diagnostic awaits its first mob.
                foreach (Node candidate in mobs.Values)
                {
                    if (candidate.Removed || candidate.Released || !candidate.InGrid || candidate.Group != Groups.HostileMobs ||
                        !candidate.Mob.Actor || candidate.Mob.Actor.GetFaction() == Character.Faction.Players || !candidate.View || !candidate.View.IsValid() || !candidate.View.gameObject.activeInHierarchy ||
                        candidate.Data.GetFloat(ZDOVars.s_health, 1f) <= 0f || State(candidate) != 1 || world.Grid.Owner(candidate.Cell) == 0) continue;
                    long owner = candidate.Data.GetOwner(); if (owner != server && owner != peer.m_uid) continue;
                    Vector3 a = candidate.Data.GetPosition(), b = player.GetPosition();
                    if (!Finite(a) || !Finite(b)) continue;
                    float x = a.x-b.x, y = a.y-b.y, z = a.z-b.z, distance = x*x+y*y+z*z;
                    if (distance <= nearest) { nearest = distance; mobTestNode = candidate; }
                }
                if (mobTestNode == null) return;
                mobTestPlayer = peer.m_uid; mobTestStarted = now; mobTestNext = now;
                mobTestTarget = mobTestNode.Data.GetOwner();
                MobTestNote("selected one Hostile Mob; prefab=" + mobTestNode.Data.GetPrefab() + "; position=" + Coordinates(mobTestNode.Data.GetPosition()) +
                    "; player=" + mobTestPlayer + ". Alternates server/player only after completed handoffs; normal guards remain active. Stops after five minutes.");
            }
            Node node = mobTestNode;
            if (peer.m_uid != mobTestPlayer || node.Removed || node.Released || !node.InGrid || !node.View || !node.View.IsValid() ||
                node.Group != Groups.HostileMobs || !node.Mob.Actor || node.Mob.Actor.GetFaction() == Character.Faction.Players || node.Data.GetFloat(ZDOVars.s_health, 1f) <= 0f || State(node) != 1 ||
                !Near(node.Data.GetPosition(), player.GetPosition(), 64f))
            { StopMobTest("mob/player unavailable, changed state or too far apart"); return; }
            if (now < mobTestStarted || now - mobTestStarted >= 300f) { StopMobTest("time limit"); return; }
            long current = node.Data.GetOwner();
            if (current != server && current != mobTestPlayer) { StopMobTest("ownership changed to an unexpected peer"); return; }
            if (mobTestPending)
            {
                if (current == mobTestTarget)
                {
                    mobTestPending = false; mobTestNext = now + options.MobTestInterval;
                    MobTestNote("target reached outside a test commit; not counted as a guarded transfer.");
                }
                else if (now >= mobTestReport) { mobTestReport = now + 2f; if (MobTestLog != null) MobTestLog(DescribeMobTest()); }
                return; // Never replace a pending request with its opposite.
            }
            if (now < mobTestNext) return;
            mobTestTarget = current == server ? mobTestPlayer : server;
            mobTestPending = true; mobTestRequests++; mobTestReport = now + 2f;
            MobTestNote("request " + mobTestRequests + ": " + current + " -> " + mobTestTarget);
            node.Mob.Pending = true; pendingMobs.Add(node);
            if (!mobDue.Contains(node)) mobDue.Add(node, now);
        }
        private void MobTestCommitted(Node node, long previous, long target)
        {
            if (!ReferenceEquals(node, mobTestNode) || !mobTestPending || target != mobTestTarget) return;
            mobTestTransfers++; mobTestPending = false; mobTestNext = Time.unscaledTime + options.MobTestInterval;
            MobTestNote("completed " + mobTestTransfers + ": " + previous + " -> " + target + "; next request after " + options.MobTestInterval + " seconds.");
        }
    }
}
