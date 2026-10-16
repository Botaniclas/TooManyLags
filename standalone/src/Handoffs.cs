using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLagsStandalone
{
    internal sealed class Handoffs
    {
        private sealed class Job
        {
            internal ZDO Data;
            internal Kinds Kind;
            internal ZDOID MobTarget;
            internal HandoffTransaction State;
            internal bool Activated;
            internal bool NativeSource, NativeTarget;
            internal bool SentCommit, SnapshotKnown;
            internal uint MinimumState, ActivationState;
            internal ZNetPeer SourcePeer, TargetPeer;
        }
        private readonly AuthorityController controller;
        private readonly Dictionary<ZDOID, Job> jobs = new Dictionary<ZDOID, Job>();
        private readonly List<Job> scratch = new List<Job>();
        private sealed class Receipt { internal long Source, Target; internal ushort Revision; internal Kinds Kind; internal float Until, Next; internal Job Job; internal bool Activated; }
        private readonly Dictionary<ZDOID, Receipt> receipts = new Dictionary<ZDOID, Receipt>();
        private readonly HashSet<ZDOID> awaiting = new HashSet<ZDOID>();
        private struct Expiry { internal ZDOID Id; internal long Token; internal float Until; }
        private readonly Queue<Expiry> retirement = new Queue<Expiry>();
        private readonly List<Receipt> receiptScratch = new List<Receipt>();
        private float nextReceipt = Single.PositiveInfinity;
        private long sequence;
        internal Handoffs(AuthorityController controller) { this.controller = controller; }
        internal bool Pending(ZDOID id) { return jobs.ContainsKey(id); }
        internal bool Committing(ZDOID id)
        { Job job; Receipt receipt; return (jobs.TryGetValue(id, out job) && (job.State.Phase == HandoffPhase.Commit || job.State.Phase == HandoffPhase.Reconciling)) ||
            (receipts.TryGetValue(id, out receipt) && ReceiptPending(receipt)); }
        private static bool ReceiptPending(Receipt receipt)
        { return !receipt.Job.SnapshotKnown || !receipt.Activated || receipt.Job.ActivationState < receipt.Job.MinimumState; }
        internal bool AcceptsForward(ZDOID id, long source, long target, ushort revision, int method)
        {
            Kinds kind; Job job; Receipt receipt;
            if (jobs.TryGetValue(id, out job) && job.State.Source == source && job.State.Target == target && job.State.Revision + 1 == revision && ReferenceEquals(job.SourcePeer, ZNet.instance.GetPeer(source))) kind = job.Kind;
            else if (receipts.TryGetValue(id, out receipt) && receipt.Until >= Time.unscaledTime && receipt.Source == source && receipt.Target == target && receipt.Revision == revision && ReferenceEquals(receipt.Job.SourcePeer, ZNet.instance.GetPeer(source))) kind = receipt.Kind;
            else return false;
            return kind == Kinds.None ? method == "RPC_Damage".GetStableHashCode() : ObjectCatalog.OwnerRequest(kind, method);
        }
        internal bool Request(ZDO data, long target, Kinds kind, ZDOID mobTarget)
        {
            if (!controller.Active || data == null || !data.IsValid() || target == 0 || target == data.GetOwner() || data.GetOwner() == 0 || data.OwnerRevision == UInt16.MaxValue || Committing(data.m_uid)) return false;
            if ((kind == Kinds.Signs || kind == Kinds.Portals) && (!Plugin.Network.TextCapable(data.GetOwner()) || !Plugin.Network.TextCapable(target))) return false;
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
                SnapshotKnown = nativeSource, MinimumState = data.DataRevision, ActivationState = nativeTarget ? UInt32.MaxValue : 0,
                SourcePeer = ZNet.instance.GetPeer(data.GetOwner()), TargetPeer = ZNet.instance.GetPeer(target),
                State = new HandoffTransaction(++sequence, data.GetOwner(), target, data.OwnerRevision, Time.unscaledTime) };
            jobs.Add(data.m_uid, job); ZDOMan.instance.ForceSendZDO(target, data.m_uid);
            return true;
        }
        internal void Tick(float now)
        {
            if (now >= nextReceipt)
            {
                nextReceipt = Single.PositiveInfinity; receiptScratch.Clear();
                // Completed receipts need only chronological expiry. One slow
                // receiver never rechecks all already-completed handoffs.
                while (retirement.Count != 0 && now >= retirement.Peek().Until)
                {
                    Expiry expiry = retirement.Dequeue(); Receipt retired;
                    if (receipts.TryGetValue(expiry.Id, out retired) && retired.Job.State.Token == expiry.Token && !ReceiptPending(retired))
                        receipts.Remove(expiry.Id);
                }
                foreach (ZDOID id in awaiting) { Receipt receipt; if (receipts.TryGetValue(id, out receipt)) receiptScratch.Add(receipt); }
                // Listen-host replies can complete another transfer inline.
                // Traverse a reusable snapshot, never a live sending dictionary.
                foreach (Receipt receipt in receiptScratch)
                {
                    ZDOID id = receipt.Job.Data.m_uid; Receipt current;
                    if (!receipts.TryGetValue(id, out current) || !ReferenceEquals(current, receipt)) continue;
                    if (!receipt.Job.Data.IsValid() || !ReferenceEquals(ZDOMan.instance.GetZDO(id), receipt.Job.Data) ||
                        !ReferenceEquals(receipt.Job.TargetPeer, ZNet.instance.GetPeer(receipt.Target)) ||
                        !controller.Available(receipt.Target, receipt.Job.Data.GetPosition()) ||
                        receipt.Job.Data.GetOwner() != receipt.Target || receipt.Job.Data.OwnerRevision != receipt.Revision)
                    { receipts.Remove(id); awaiting.Remove(id); continue; }
                    if (!ReceiptPending(receipt)) { RefreshReceipt(id, receipt); continue; }
                    if (now >= receipt.Next)
                    {
                        receipt.Next = now + .5f; Job receiptJob = receipt.Job;
                        if (!receiptJob.SnapshotKnown)
                        {
                            if (controller.Available(receipt.Source, receiptJob.Data.GetPosition()) && ReferenceEquals(receiptJob.SourcePeer, ZNet.instance.GetPeer(receipt.Source)))
                                Plugin.Network.Send(receipt.Source, Message.Status, delegate(ZPackage p) { Write(p, receiptJob); });
                            else { receiptJob.SnapshotKnown = true; RecordState(receiptJob, receiptJob.Data.DataRevision); }
                        }
                        ZDOMan.instance.ForceSendZDO(receipt.Target, receiptJob.Data.m_uid);
                        Plugin.Network.Send(receipt.Target, Message.ActivationCheck, delegate(ZPackage p) { Write(p, receiptJob); });
                    }
                    nextReceipt = Math.Min(nextReceipt, ReceiptPending(receipt) ? receipt.Next : receipt.Until);
                    RefreshReceipt(id, receipt);
                }
                receiptScratch.Clear();
                if (retirement.Count != 0) nextReceipt = Math.Min(nextReceipt, retirement.Peek().Until);
            }
            if (jobs.Count == 0) return;
            scratch.Clear(); foreach (Job job in jobs.Values) scratch.Add(job);
            foreach (Job job in scratch)
            {
                if (!jobs.ContainsKey(job.Data.m_uid)) continue;
                HandoffTransaction s = job.State;
                if (!ReferenceEquals(job.SourcePeer, ZNet.instance.GetPeer(s.Source)) || !ReferenceEquals(job.TargetPeer, ZNet.instance.GetPeer(s.Target)))
                { Cancel(job.Data.m_uid, true); controller.Wake(job.Data.m_uid); continue; }
                if (!job.Data.IsValid() || !ReferenceEquals(ZDOMan.instance.GetZDO(job.Data.m_uid), job.Data)) { Cancel(job.Data.m_uid); continue; }
                if (s.Observe(job.Data.GetOwner(), job.Data.OwnerRevision)) { Finish(job); continue; }
                // A client can finish its handshake while a native-source job
                // awaits preparation. Restart through its now-available guards.
                if (job.NativeSource && Plugin.Network.Capable(s.Source)) { Cancel(job.Data.m_uid, true); controller.Wake(job.Data.m_uid); continue; }
                if (!controller.Available(s.Source, job.Data.GetPosition()) || !controller.Available(s.Target, job.Data.GetPosition())) { Cancel(job.Data.m_uid, true); continue; }
                if (s.Phase != HandoffPhase.Commit && s.Phase != HandoffPhase.Reconciling && controller.Desired(job.Data, job.Kind, job.MobTarget) != s.Target) { Cancel(job.Data.m_uid); continue; }
                if (now < s.Next) continue;
                if (s.Phase == HandoffPhase.Blocked) { Restart(job, now); s = job.State; }
                if (s.Phase == HandoffPhase.Prepare && job.NativeTarget)
                {
                    // Vanilla assigns persistent objects by native active area,
                    // without a client readiness RPC. Availability was rechecked.
                    s.Prepared(true, now);
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
                if (s.Phase == HandoffPhase.Prepare || s.Phase == HandoffPhase.Commit || s.Phase == HandoffPhase.Reconciling)
                {
                    Message message = s.Phase == HandoffPhase.Prepare ? Message.Prepare : job.SentCommit ? Message.Status : Message.Commit;
                    if (message == Message.Commit) job.SentCommit = true;
                    long recipient = s.Phase == HandoffPhase.Prepare ? s.Target : s.Source;
                    s.Next = now + (message == Message.Prepare ? .5f : 2f);
                    Plugin.Network.Send(recipient, message, delegate(ZPackage p) { Write(p, job); });
                }
            }
            scratch.Clear();
        }
        private static void Write(ZPackage p, Job job)
        { p.Write(job.State.Token); p.Write(job.Data.m_uid); p.Write(job.State.Source); p.Write(job.State.Target); p.Write(job.State.Revision); p.Write((int)job.Kind); p.Write(job.MobTarget); p.Write(job.MinimumState); }
        internal void Receive(long sender, Message message, ZPackage packet)
        {
            long token = packet.ReadLong(); ZDOID id = packet.ReadZDOID(); Job job;
            if (message == Message.Activated)
            {
                ushort revision = packet.ReadUShort(); uint state = packet.ReadUInt(); Receipt receipt;
                if (receipts.TryGetValue(id, out receipt) && receipt.Job.State.Token == token && receipt.Target == sender && receipt.Revision == revision && ReferenceEquals(receipt.Job.TargetPeer, ZNet.instance.GetPeer(sender)))
                { receipt.Activated = true; receipt.Job.ActivationState = state; RefreshReceipt(id, receipt); controller.Wake(id); RequestRouter.Flush(id, Time.unscaledTime); }
                else if (jobs.TryGetValue(id, out job) && job.State.Token == token && job.State.Target == sender && job.State.Revision + 1 == revision && ReferenceEquals(job.TargetPeer, ZNet.instance.GetPeer(sender))) { job.Activated = true; job.ActivationState = state; }
                return;
            }
            if (!jobs.TryGetValue(id, out job))
            {
                Receipt receipt;
                if (message == Message.Result && receipts.TryGetValue(id, out receipt) && receipt.Job.State.Token == token && receipt.Source == sender && ReferenceEquals(receipt.Job.SourcePeer, ZNet.instance.GetPeer(sender)))
                { ReadResult(receipt.Job, packet); RefreshReceipt(id, receipt); RequestRouter.Flush(id, Time.unscaledTime); }
                return;
            }
            if (job.State.Token != token) return;
            HandoffTransaction s = job.State; float now = Time.unscaledTime;
            if (sender == s.Source && !ReferenceEquals(job.SourcePeer, ZNet.instance.GetPeer(sender))) return;
            if (sender == s.Target && !ReferenceEquals(job.TargetPeer, ZNet.instance.GetPeer(sender))) return;
            if (message == Message.Prepared && sender == s.Target)
            { bool ready = packet.ReadBool(); s.Prepared(ready, now); return; }
            if (message == Message.Wake && sender == s.Source) { if (s.Phase == HandoffPhase.Blocked) s.Next = now; return; }
            if (message != Message.Result || sender != s.Source || (s.Phase != HandoffPhase.Commit && s.Phase != HandoffPhase.Reconciling)) return;
            int result = ReadResult(job, packet);
            if (result == 1) { s.Next = now + 2f; Observe(job.Data); }
            else if (result == 2) { s.Phase = HandoffPhase.Commit; s.Block(now); }
            else { Cancel(id, true); if (result == 3) controller.Rejected(id); controller.Wake(id); }
            RequestRouter.Flush(id, now);
        }
        private int ReadResult(Job job, ZPackage packet)
        {
            int result = packet.ReadInt(); bool present = packet.ReadBool(); long owner = packet.ReadLong(); ushort revision = packet.ReadUShort(); uint state = packet.ReadUInt();
            if (present && owner == job.State.Target && revision == job.State.Revision + 1)
            { job.SnapshotKnown = true; RecordState(job, state); }
            return result;
        }
        private void RecordState(Job job, uint state)
        { job.MinimumState = Math.Max(job.MinimumState, state); controller.RecordRecipient(job.Data, job.State.Target, (ushort)(job.State.Revision + 1), job.MinimumState); }
        private void Restart(Job job, float now)
        {
            Plugin.Network.Send(job.State.Source, Message.Cancel, delegate(ZPackage p) { p.Write(job.State.Token); p.Write(job.Data.m_uid); });
            Plugin.Network.Send(job.State.Target, Message.Cancel, delegate(ZPackage p) { p.Write(job.State.Token); p.Write(job.Data.m_uid); });
            job.State = new HandoffTransaction(++sequence, job.Data.GetOwner(), job.State.Target, job.Data.OwnerRevision, now);
            job.SentCommit = false; job.Activated = job.NativeTarget; job.ActivationState = job.NativeTarget ? UInt32.MaxValue : 0;
            job.SnapshotKnown = job.NativeSource; job.MinimumState = job.Data.DataRevision;
        }
        internal void Observe(ZDO data)
        { Job job; if (data != null && jobs.TryGetValue(data.m_uid, out job) && job.State.Observe(data.GetOwner(), data.OwnerRevision)) Finish(job); }
        private void Finish(Job job)
        {
            if (job.State.Phase == HandoffPhase.Complete) RecordState(job, job.Data.DataRevision);
            // Native-only peers cannot send forwarding/activation callbacks.
            // No receipt cache or maintenance sweep is needed for that path.
            if (job.State.Phase == HandoffPhase.Complete && (!job.NativeSource || !job.NativeTarget))
            {
                Receipt receipt = new Receipt { Source = job.State.Source, Target = job.State.Target, Revision = job.Data.OwnerRevision,
                    Kind = job.Kind, Until = Time.unscaledTime + 12f, Next = Time.unscaledTime + .5f, Job = job, Activated = job.Activated };
                receipts[job.Data.m_uid] = receipt;
                retirement.Enqueue(new Expiry { Id = job.Data.m_uid, Token = job.State.Token, Until = receipt.Until });
                if (ReceiptPending(receipt)) awaiting.Add(job.Data.m_uid); else awaiting.Remove(job.Data.m_uid);
                nextReceipt = Math.Min(nextReceipt, ReceiptPending(receipt) ? receipt.Next : receipt.Until);
            }
            jobs.Remove(job.Data.m_uid); controller.Wake(job.Data.m_uid); RequestRouter.Flush(job.Data.m_uid, Time.unscaledTime);
        }
        private void RefreshReceipt(ZDOID id, Receipt receipt)
        {
            Receipt current; if (!receipts.TryGetValue(id, out current) || !ReferenceEquals(current, receipt)) return;
            if (ReceiptPending(receipt))
            { if (awaiting.Add(id)) receipt.Next = Time.unscaledTime + .5f; nextReceipt = Math.Min(nextReceipt, receipt.Next); }
            else { awaiting.Remove(id); if (Time.unscaledTime >= receipt.Until) receipts.Remove(id); }
        }
        internal void Forget(ZDOID id) { jobs.Remove(id); receipts.Remove(id); awaiting.Remove(id); }
        internal void Cancel(ZDOID id, bool force = false)
        {
            Job job; if (!jobs.TryGetValue(id, out job)) return;
            if (!force && job.SentCommit && (job.State.Phase == HandoffPhase.Commit || job.State.Phase == HandoffPhase.Reconciling))
            { job.State.Phase = HandoffPhase.Reconciling; job.State.Next = Time.unscaledTime; return; }
            jobs.Remove(id);
            Plugin.Network.Send(job.State.Target, Message.Cancel, delegate(ZPackage p) { p.Write(job.State.Token); p.Write(id); });
            Plugin.Network.Send(job.State.Source, Message.Cancel, delegate(ZPackage p) { p.Write(job.State.Token); p.Write(id); });
        }
        internal void Clear() { jobs.Clear(); scratch.Clear(); receipts.Clear(); awaiting.Clear(); retirement.Clear(); receiptScratch.Clear(); nextReceipt = Single.PositiveInfinity; }
    }
}
