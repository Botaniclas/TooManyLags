using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags.PlayerSimulation
{
    internal sealed class Handoffs
    {
        private sealed class Job
        {
            internal ZDO Data;
            internal Kinds Kind;
            internal ZDOID MobTarget;
            internal HandoffTransaction State;
            internal float LastSend;
            internal float CommitAt;
            internal bool Activated;
            internal bool NativeSource, NativeTarget;
        }
        private readonly AuthorityController controller;
        private readonly Dictionary<ZDOID, Job> jobs = new Dictionary<ZDOID, Job>();
        private readonly List<Job> scratch = new List<Job>();
        private sealed class Receipt { internal long Source, Target; internal ushort Revision; internal Kinds Kind; internal float Until, Next; internal Job Job; internal bool Activated; }
        private readonly Dictionary<ZDOID, Receipt> receipts = new Dictionary<ZDOID, Receipt>();
        private readonly List<ZDOID> expired = new List<ZDOID>();
        private float nextReceipt = Single.PositiveInfinity;
        private long sequence;
        internal Handoffs(AuthorityController controller) { this.controller = controller; }
        internal bool Pending(ZDOID id) { return jobs.ContainsKey(id); }
        internal bool Committing(ZDOID id)
        { Job job; Receipt receipt; return (jobs.TryGetValue(id, out job) && job.State.Phase == HandoffPhase.Commit) || (receipts.TryGetValue(id, out receipt) && !receipt.Activated && Time.unscaledTime < receipt.Until); }
        internal bool AcceptsForward(ZDOID id, long source, long target, ushort revision, int method)
        {
            Kinds kind; Job job; Receipt receipt;
            if (jobs.TryGetValue(id, out job) && job.State.Source == source && job.State.Target == target && job.State.Revision + 1 == revision) kind = job.Kind;
            else if (receipts.TryGetValue(id, out receipt) && receipt.Until >= Time.unscaledTime && receipt.Source == source && receipt.Target == target && receipt.Revision == revision) kind = receipt.Kind;
            else return false;
            return kind == Kinds.None ? method == "RPC_Damage".GetStableHashCode() : ObjectCatalog.OwnerRequest(kind, method);
        }
        internal bool Request(ZDO data, long target, Kinds kind, ZDOID mobTarget)
        {
            if (!controller.Active || data == null || !data.IsValid() || target == 0 || target == data.GetOwner() || data.GetOwner() == 0 || data.OwnerRevision == UInt16.MaxValue || Committing(data.m_uid)) return false;
            if (kind == Kinds.None && (!Plugin.Network.MobCapable(target) || !Plugin.Network.MobCapable(data.GetOwner()))) return false;
            bool nativeSource = !Plugin.Network.Capable(data.GetOwner()), nativeTarget = !Plugin.Network.Capable(target);
            // No spoofed inventory probe or forced transfer of local use state.
            if ((nativeSource || nativeTarget) && (kind == Kinds.None || !controller.NativeTransferSafe(data))) return false;
            Job old;
            if (jobs.TryGetValue(data.m_uid, out old))
            {
                if (old.State.Phase == HandoffPhase.Commit) return false; // Observe the outcome before any onward request.
                if (old.State.Target == target && old.MobTarget == mobTarget)
                { if (old.NativeSource && NativeOwnership.Idle(data)) old.State.Wake(Time.unscaledTime); return true; }
                Cancel(data.m_uid);
            }
            if (jobs.Count >= 4096 || RequestRouter.Saturated || !controller.Available(data.GetOwner(), data.GetPosition()) || !controller.Available(target, data.GetPosition())) return false;
            Job job = new Job { Data = data, Kind = kind, MobTarget = mobTarget, NativeSource = nativeSource, NativeTarget = nativeTarget, Activated = nativeTarget,
                State = new HandoffTransaction(++sequence, data.GetOwner(), target, data.OwnerRevision, Time.unscaledTime) };
            jobs.Add(data.m_uid, job); ZDOMan.instance.ForceSendZDO(target, data.m_uid);
            return true;
        }
        internal void Tick(float now)
        {
            if (now >= nextReceipt)
            {
                nextReceipt = Single.PositiveInfinity; expired.Clear();
                foreach (KeyValuePair<ZDOID, Receipt> item in receipts)
                {
                    Receipt receipt = item.Value;
                    if (now >= receipt.Until) { expired.Add(item.Key); continue; }
                    if (!receipt.Activated && now >= receipt.Next)
                    {
                        receipt.Next = now + 2f; Job receiptJob = receipt.Job;
                        ZDOMan.instance.ForceSendZDO(receipt.Target, receiptJob.Data.m_uid);
                        Plugin.Network.Send(receipt.Target, Message.ActivationCheck, delegate(ZPackage p) { Write(p, receiptJob); });
                    }
                    nextReceipt = Math.Min(nextReceipt, receipt.Activated ? receipt.Until : Math.Min(receipt.Until, receipt.Next));
                }
                foreach (ZDOID id in expired) receipts.Remove(id); expired.Clear();
            }
            if (jobs.Count == 0) return;
            scratch.Clear(); foreach (Job job in jobs.Values) scratch.Add(job);
            foreach (Job job in scratch)
            {
                if (!jobs.ContainsKey(job.Data.m_uid)) continue;
                HandoffTransaction s = job.State;
                if (!job.Data.IsValid() || !ReferenceEquals(ZDOMan.instance.GetZDO(job.Data.m_uid), job.Data)) { Cancel(job.Data.m_uid); continue; }
                if (s.Observe(job.Data.GetOwner(), job.Data.OwnerRevision)) { Finish(job); continue; }
                // A client can finish its handshake while a native-source job
                // awaits preparation. Restart through its now-available guards.
                if (job.NativeSource && Plugin.Network.Capable(s.Source)) { Cancel(job.Data.m_uid, true); controller.Wake(job.Data.m_uid); continue; }
                if (s.Phase == HandoffPhase.Commit && now - job.CommitAt > 12f) { Cancel(job.Data.m_uid, true); controller.Wake(job.Data.m_uid); continue; }
                if (!controller.Available(s.Source, job.Data.GetPosition()) || !controller.Available(s.Target, job.Data.GetPosition())) { Cancel(job.Data.m_uid); continue; }
                if (s.Phase != HandoffPhase.Commit && controller.Desired(job.Data, job.Kind, job.MobTarget) != s.Target) { Cancel(job.Data.m_uid); continue; }
                if (now < s.Next) continue;
                if (s.Phase == HandoffPhase.Blocked) s.Wake(now);
                if (s.Phase == HandoffPhase.Prepare && job.NativeTarget)
                {
                    // Vanilla assigns persistent objects by native active area,
                    // without a client readiness RPC. Availability was rechecked.
                    s.Prepared(true, now); job.CommitAt = now;
                }
                if (s.Phase == HandoffPhase.Commit && job.NativeSource)
                {
                    if (!s.MatchesSource(job.Data.GetOwner(), job.Data.OwnerRevision)) continue;
                    if (!controller.NativeTransferSafe(job.Data)) { Cancel(job.Data.m_uid, true); continue; }
                    if (!NativeOwnership.Idle(job.Data)) { s.Block(now); continue; }
                    // The host uses the same owner revision/replication as native
                    // reassignment. Inventories, stations, vehicles and AI never
                    // enter this fallback; capable sources retain all local guards.
                    job.Data.SetOwner(s.Target);
                    ZDOMan.instance.ForceSendZDO(s.Source, job.Data.m_uid);
                    ZDOMan.instance.ForceSendZDO(s.Target, job.Data.m_uid);
                    Observe(job.Data); continue;
                }
                if (s.Phase == HandoffPhase.Prepare || s.Phase == HandoffPhase.Commit)
                {
                    Message message = s.Phase == HandoffPhase.Prepare ? Message.Prepare : Message.Commit;
                    long recipient = s.Phase == HandoffPhase.Prepare ? s.Target : s.Source;
                    s.Next = now + (message == Message.Prepare ? .5f : 2f); job.LastSend = now;
                    Plugin.Network.Send(recipient, message, delegate(ZPackage p) { Write(p, job); });
                }
            }
            scratch.Clear();
        }
        private static void Write(ZPackage p, Job job)
        { p.Write(job.State.Token); p.Write(job.Data.m_uid); p.Write(job.State.Source); p.Write(job.State.Target); p.Write(job.State.Revision); p.Write((int)job.Kind); p.Write(job.MobTarget); }
        internal void Receive(long sender, Message message, ZPackage packet)
        {
            long token = packet.ReadLong(); ZDOID id = packet.ReadZDOID(); Job job;
            if (message == Message.Activated)
            {
                ushort revision = packet.ReadUShort(); Receipt receipt;
                if (receipts.TryGetValue(id, out receipt) && receipt.Job.State.Token == token && receipt.Target == sender && receipt.Revision == revision) { receipt.Activated = true; controller.Wake(id); }
                else if (jobs.TryGetValue(id, out job) && job.State.Token == token && job.State.Target == sender && job.State.Revision + 1 == revision) job.Activated = true;
                return;
            }
            if (!jobs.TryGetValue(id, out job) || job.State.Token != token) return;
            HandoffTransaction s = job.State; float now = Time.unscaledTime;
            if (message == Message.Prepared && sender == s.Target)
            { bool ready = packet.ReadBool(); s.Prepared(ready, now); if (ready) job.CommitAt = now; return; }
            if (message == Message.Wake && sender == s.Source) { s.Wake(now); return; }
            if (message != Message.Result || sender != s.Source || s.Phase != HandoffPhase.Commit) return;
            int result = packet.ReadInt();
            if (result == 1) { s.Next = now + 2f; Observe(job.Data); }
            else if (job.Kind != Kinds.None)
            {
                // Separate native replication can precede this reply. Retain
                // a real receipt/activation barrier or an expected half import.
                if (s.Observe(job.Data.GetOwner(), job.Data.OwnerRevision)) { Finish(job); return; }
                if (!s.MatchesSource(job.Data.GetOwner(), job.Data.OwnerRevision)) return;
                // A processed grid refusal is not a request to retry after
                // use ends. Keep only the existing sparse coverage listener.
                Cancel(id, true); controller.Rejected(id);
            }
            else if (result == 2) s.Block(now);
            else { Cancel(id, true); if (result == 4 && job.Kind == Kinds.LooseItems) { controller.PickupBlocked(id); return; } if (result == 3) controller.Rejected(id); controller.Wake(id); }
        }
        internal void Observe(ZDO data)
        { Job job; if (data != null && jobs.TryGetValue(data.m_uid, out job) && job.State.Observe(data.GetOwner(), data.OwnerRevision)) Finish(job); }
        private void Finish(Job job)
        {
            // Native-only peers cannot send forwarding/activation callbacks.
            // No receipt cache or maintenance sweep is needed for that path.
            if (job.State.Phase == HandoffPhase.Complete && (!job.NativeSource || !job.NativeTarget)) { receipts[job.Data.m_uid] = new Receipt { Source = job.State.Source, Target = job.State.Target, Revision = job.Data.OwnerRevision, Kind = job.Kind, Until = Time.unscaledTime + 12f, Job = job, Activated = job.Activated }; nextReceipt = Time.unscaledTime; }
            jobs.Remove(job.Data.m_uid); controller.Wake(job.Data.m_uid);
        }
        internal void Forget(ZDOID id) { jobs.Remove(id); receipts.Remove(id); }
        internal void Cancel(ZDOID id, bool force = false)
        {
            Job job; if (!jobs.TryGetValue(id, out job)) return;
            if (!force && job.State.Phase == HandoffPhase.Commit && Time.unscaledTime - job.State.Started < 12f) return;
            jobs.Remove(id);
            Plugin.Network.Send(job.State.Target, Message.Cancel, delegate(ZPackage p) { p.Write(job.State.Token); p.Write(id); });
            Plugin.Network.Send(job.State.Source, Message.Cancel, delegate(ZPackage p) { p.Write(job.State.Token); p.Write(id); });
        }
        internal void Clear() { jobs.Clear(); scratch.Clear(); receipts.Clear(); expired.Clear(); nextReceipt = Single.PositiveInfinity; }
    }
}
