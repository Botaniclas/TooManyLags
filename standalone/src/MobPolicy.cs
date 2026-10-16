using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLagsStandalone
{
    internal sealed class MobPolicy
    {
        internal static readonly KeyValuePair<int, int> TargetKey = ZDO.GetHashZDOID(Plugin.Id + ".target");
        internal static readonly int WriterKey = (Plugin.Id + ".targetWriter").GetStableHashCode(), RevisionKey = (Plugin.Id + ".targetRevision").GetStableHashCode();
        private sealed class Record { internal ZDO Data; internal ZDOID Target; internal long Writer; internal ushort Revision; internal float Next; internal RecipientRequirement Recipient; }
        private readonly Dictionary<ZDOID, Record> records = new Dictionary<ZDOID, Record>();
        private readonly List<ZDOID> removing = new List<ZDOID>();
        private static readonly Dictionary<int, int> types = new Dictionary<int, int>();
        private readonly AuthorityController controller;
        private float next = Single.PositiveInfinity;
        internal MobPolicy(AuthorityController controller) { this.controller = controller; }
        private static int PrefabType(int hash)
        {
            int type; if (types.TryGetValue(hash, out type)) return type;
            GameObject root = ZNetScene.instance ? ZNetScene.instance.GetPrefab(hash) : null;
            if (!root) return 0;
            Character actor = root.GetComponent<Character>();
            type = actor && !(actor is Player) && actor.GetFaction() == Character.Faction.Players ? 1 :
                actor && root.GetComponent<MonsterAI>() && !actor.IsBoss() ? 2 : 0;
            types.Add(hash, type); return type;
        }
        internal static bool FriendlyPrefab(int hash) { return PrefabType(hash) == 1; }
        internal static bool HostilePrefab(int hash) { return hash != 0 && PrefabType(hash) == 2; }
        internal static bool Eligible(ZDO data)
        { return data != null && data.IsValid() && data.GetOwner() != 0 && data.GetFloat(ZDOVars.s_health, 1f) > 0 && !data.GetBool(ZDOVars.s_tamed) && data.GetLong(ZDOVars.s_user, 0) == 0 && PrefabType(data.GetPrefab()) == 2; }
        internal void Report(long sender, ZDOID id, ushort revision, ZDOID target)
        {
            if (!Plugin.Options.Mobs || ZDOMan.instance == null) return;
            ZDO data = ZDOMan.instance.GetZDO(id);
            if (!Eligible(data) || data.GetOwner() != sender || data.OwnerRevision != revision || !Plugin.Network.MobCapable(sender)) return;
            if (target.IsNone()) { Forget(id); controller.Handoffs.Cancel(id); return; }
            bool valid = false; foreach (PlayerPeer player in controller.Players) if (player.Character.m_uid == target) { valid = true; break; }
            if (!valid) return;
            Record old;
            if (records.TryGetValue(id, out old) && ReferenceEquals(old.Data, data) && old.Target == target && old.Writer == sender && old.Revision == revision) return;
            if (old == null || !ReferenceEquals(old.Data, data)) old = new Record { Data = data };
            old.Target = target; old.Writer = sender; old.Revision = revision; old.Next = Time.unscaledTime;
            records[id] = old; next = Time.unscaledTime;
        }
        internal void Changed(ZDO data)
        {
            if (!Plugin.Options.Mobs || data == null || data.GetPrefab() == 0) return;
            Record record;
            if (!Eligible(data)) { if (records.ContainsKey(data.m_uid)) Forget(data.m_uid); return; }
            if (records.TryGetValue(data.m_uid, out record) && (record.Writer != data.GetOwner() || record.Revision != data.OwnerRevision)) Wake(data.m_uid);
            if (data.GetLong(WriterKey, 0) == data.GetOwner() && data.GetLong(RevisionKey, -1) == data.OwnerRevision)
                Report(data.GetOwner(), data.m_uid, data.OwnerRevision, data.GetZDOID(TargetKey));
        }
        internal long Desired(ZDO data, ZDOID target)
        {
            if (!Plugin.Options.Mobs || !Eligible(data) || target.IsNone() || !Plugin.Network.MobCapable(data.GetOwner())) return 0;
            PlayerPeer targeted = default(PlayerPeer); int candidates = 0;
            foreach (PlayerPeer peer in controller.Players)
            {
                if (peer.Character.m_uid == target) targeted = peer;
                if (Plugin.Network.MobCapable(peer.Uid) && controller.Available(peer.Uid, data.GetPosition())) candidates++;
            }
            if (candidates < 2 || targeted.Uid == 0 || !Plugin.Network.MobCapable(targeted.Uid) || !controller.Available(targeted.Uid, data.GetPosition())) return 0;
            float targetPing = Plugin.Network.Latency(targeted.Uid);
            if (Single.IsInfinity(targetPing) || targetPing <= Plugin.Options.HighPing) return targeted.Uid;
            long best = targeted.Uid; float bestPing = targetPing, currentPing = Single.PositiveInfinity;
            float radius = Plugin.Options.MobRadius, squared = radius * radius;
            foreach (PlayerPeer peer in controller.Players)
            {
                if (peer.Uid == targeted.Uid || !Plugin.Network.MobCapable(peer.Uid) || !controller.Available(peer.Uid, data.GetPosition()) || (peer.Character.GetPosition() - data.GetPosition()).sqrMagnitude > squared) continue;
                float ping = Plugin.Network.Latency(peer.Uid);
                if (peer.Uid == data.GetOwner()) currentPing = ping;
                if (ping < bestPing || (ping == bestPing && peer.Uid < best)) { best = peer.Uid; bestPing = ping; }
            }
            if (bestPing + Plugin.Options.Margin >= targetPing) return targeted.Uid;
            return currentPing <= bestPing + Plugin.Options.Margin && currentPing + Plugin.Options.Margin < targetPing ? data.GetOwner() : best;
        }
        internal void Tick(float now)
        {
            if (now < next) return;
            next = Single.PositiveInfinity; removing.Clear();
            foreach (KeyValuePair<ZDOID, Record> item in records)
            {
                Record record = item.Value;
                if (record.Next > now) { next = Math.Min(next, record.Next); continue; }
                if (!Eligible(record.Data) || !ReferenceEquals(ZDOMan.instance.GetZDO(item.Key), record.Data)) { removing.Add(item.Key); continue; }
                long desired = Desired(record.Data, record.Target);
                if (desired == 0) { removing.Add(item.Key); controller.Handoffs.Cancel(item.Key); continue; }
                if (record.Data.GetOwner() != desired) controller.Handoffs.Request(record.Data, desired, Kinds.None, record.Target);
                // An alternative owner is revisited; normal target-owned mobs
                // need only actual target/owner changes or cached ping changes.
                bool revisit = false; foreach (PlayerPeer peer in controller.Players) if (peer.Character.m_uid == record.Target)
                    revisit = desired != peer.Uid || Plugin.Network.Latency(peer.Uid) > Plugin.Options.HighPing;
                record.Next = revisit ? now + Plugin.Options.PingInterval : Single.PositiveInfinity;
                next = Math.Min(next, record.Next);
            }
            foreach (ZDOID id in removing) Forget(id); removing.Clear();
        }
        internal void RanksChanged() { foreach (Record record in records.Values) record.Next = Time.unscaledTime; if (records.Count != 0) next = Time.unscaledTime; }
        internal void Wake(ZDOID id) { Record record; if (records.TryGetValue(id, out record)) { record.Next = Time.unscaledTime; next = record.Next; } }
        internal bool Managed(ZDOID id, int method) { return records.ContainsKey(id) && method == "RPC_Damage".GetStableHashCode(); }
        internal void RecordRecipient(ZDO data, long owner, ushort revision, uint state)
        { Record record; if (records.TryGetValue(data.m_uid, out record) && ReferenceEquals(record.Data, data)) record.Recipient.Include(owner, revision, state); }
        internal bool TryRecipientState(ZDO data, long owner, out uint state)
        {
            state = 0; Record record;
            if (!records.TryGetValue(data.m_uid, out record) || !ReferenceEquals(record.Data, data) || !record.Recipient.Matches(owner, data.OwnerRevision)) return false;
            state = record.Recipient.State; return true;
        }
        internal void Forget(ZDOID id) { records.Remove(id); controller.Recipients.Forget(id); }
        internal void Clear() { records.Clear(); removing.Clear(); next = Single.PositiveInfinity; }
        internal static void ResetPrefabs() { types.Clear(); }
    }
}
