using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags
{
    // RequestOpen is a source-side native commit, not permission for SetOwner.
    // Its public-container path checks the source's local in-use/wagon state.
    // The dedicated server has no local inventory GUI. Native's pre-inUse GUI
    // phase and other mods' unreplicated local actions are not extra-protected.
    internal sealed class NativeInventoryHandoff
    {
        internal const float RequestTimeoutSeconds = 2f;
        internal const float RetryDelaySeconds = 2f;

        private sealed class Request
        {
            internal ZDO Data;
            internal ZNetView View;
            internal Container Container;
            internal long Source;
            internal ushort Revision, CommittedRevision;
            internal float Deadline, RetryAt;
            internal bool Awaiting, Granted;
        }

        private readonly long server;
        private readonly Dictionary<ZDOID, Request> requests = new Dictionary<ZDOID, Request>();
        private bool sending;
        internal bool Sending { get { return sending; } }

        internal NativeInventoryHandoff(long server) { this.server = server; }

        // 0 unavailable/unsupported/denied; 1 awaiting native source commit;
        // 2 already server-owned, or matching native response AND owner receipt.
        internal int Acquire(ZDO data, ZNetView view, Container container)
        {
            if (server == 0 || !Identity(data, view, container) || container.m_privacy != Container.PrivacySetting.Public)
            { Forget(data); return 0; }

            float now = Time.unscaledTime;
            Request request;
            if (requests.TryGetValue(data.m_uid, out request))
            {
                if (!ReferenceEquals(request.Data, data) || !ReferenceEquals(request.View, view) ||
                    !ReferenceEquals(request.Container, container) || !Current(request))
                { requests.Remove(data.m_uid); }
                else
                {
                    Expire(request, now);
                    // Native ZDO import assigns owner/revision separately, in
                    // either order. Hooks can observe the exact expected half
                    // tuple; preserve it without sending or completing a request.
                    if (Transitioning(request)) return request.Awaiting ? 1 : 0;
                    if (Committed(request))
                    {
                        if (request.Granted) return 2;
                        if (request.Awaiting) return 1;
                        if (container.IsInUse()) return 0;
                        // The exact source->server owner receipt is complete.
                        // After the reply window, use the current server-owned
                        // container contract, as a fresh/reloaded node does.
                        // Never resend to its former owner or assign ownership
                        // from a timeout. Caller keeps opening/use safeguards.
                        requests.Remove(data.m_uid); return 2;
                    }
                    if (request.Awaiting) return 1;
                    // A positive source response means it already committed.
                    // Preserve that reply while its owner data is still delayed;
                    // sending again cannot make the former source the owner.
                    if (request.Granted) return 0;
                    if (now < request.RetryAt) return 0;
                    Send(request, now); return request.Awaiting ? 1 : 0;
                }
            }

            long owner = data.GetOwner();
            if (owner == server) return 2;
            // Native OwnerRevision is UInt16. Do not cause its overflow.
            if (owner == 0 || data.OwnerRevision == UInt16.MaxValue) return 0;
            request = new Request { Data = data, View = view, Container = container,
                Source = owner, Revision = data.OwnerRevision,
                CommittedRevision = (ushort)(data.OwnerRevision + 1) };
            requests.Add(data.m_uid, request);
            Send(request, now); return request.Awaiting ? 1 : 0;
        }

        private void Send(Request request, float now)
        {
            request.Awaiting = true; request.Granted = false;
            request.Deadline = now + RequestTimeoutSeconds;
            sending = true;
            try { request.View.InvokeRPC(request.Source, "RPC_RequestOpen", 0L); }
            catch { request.Awaiting = false; request.RetryAt = now + RetryDelaySeconds; throw; }
            finally { sending = false; }
        }

        internal bool Pending(ZDO data)
        {
            Request request;
            if (!Find(data, out request)) return false;
            Expire(request, Time.unscaledTime);
            return request.Awaiting && !(request.Granted && Committed(request));
        }

        // Expiry is not a refusal: the source may have committed while its
        // reply/state is still in transit. Local-use parking cannot forget it.
        internal bool Reserved(ZDO data)
        {
            Request request;
            return Find(data, out request) && !(request.Granted && Committed(request));
        }

        // A receipt identifies this exact source->server native revision, even
        // when its OpenResponse has not arrived. Preserve the live response
        // window; only a settled, idle server receipt can retire after expiry.
        internal bool Received(ZDO data)
        {
            Request request;
            return Find(data, out request) && Committed(request);
        }

        internal bool Transitioning(ZDO data)
        {
            Request request;
            return Find(data, out request) && Transitioning(request);
        }

        private bool Transitioning(Request request)
        {
            long owner = request.Data.GetOwner();
            return (owner == request.Source && request.Data.OwnerRevision == request.CommittedRevision) ||
                (owner == server && request.Data.OwnerRevision == request.Revision);
        }

        // True only for a matching native refusal without an owner receipt.
        // The caller parks this object until a fresh coverage request.
        internal bool Response(Container container, long sender, bool granted)
        {
            if (!container) return false;
            ZNetView view = ContainerView(container);
            if (!view || !view.IsValid()) return false;
            ZDO data = view.GetZDO(); Request request;
            if (!Find(data, out request) || !ReferenceEquals(request.Container, container) ||
                !ReferenceEquals(request.View, view) || sender != request.Source) return false;
            Expire(request, Time.unscaledTime);
            // Native replies carry no request token. A denial cannot revoke a
            // previously accepted matching grant. A late positive reply is only
            // useful when its exact native owner receipt is still current; this
            // does not re-pin the chest or authorize an unobserved ownership write.
            if (request.Granted) return false;
            if (!granted)
            {
                // A delayed refusal cannot cancel an observed native commit
                // or its half-import. No timer may resurrect a clear refusal.
                if (Committed(request) || Transitioning(request)) return false;
                request.Awaiting = false; requests.Remove(data.m_uid); return true;
            }
            if (!request.Awaiting)
            {
                if (Committed(request)) request.Granted = true;
                return false;
            }
            request.Granted = true;
            return false;
        }

        private bool Find(ZDO data, out Request request)
        {
            request = null;
            if (data == null || !requests.TryGetValue(data.m_uid, out request)) return false;
            if (!ReferenceEquals(request.Data, data)) { request = null; return false; }
            if (!Current(request)) { requests.Remove(data.m_uid); request = null; return false; }
            return true;
        }

        private bool Current(Request request)
        {
            if (!Identity(request.Data, request.View, request.Container) ||
                request.Container.m_privacy != Container.PrivacySetting.Public) return false;
            long owner = request.Data.GetOwner();
            return (owner == request.Source && request.Data.OwnerRevision == request.Revision) ||
                (owner == server && request.Data.OwnerRevision == request.CommittedRevision) ||
                Transitioning(request);
        }

        private bool Committed(Request request)
        { return request.Data.GetOwner() == server && request.Data.OwnerRevision == request.CommittedRevision; }

        private void Expire(Request request, float now)
        {
            if (request.Awaiting && !(request.Granted && Committed(request)) && now >= request.Deadline)
            {
                // No answer: the next existing policy evaluation may retry.
                // Explicit denials are discarded; uncertain send errors retain
                // their separate delay.
                request.Awaiting = false; request.RetryAt = now;
            }
        }

        private static bool Identity(ZDO data, ZNetView view, Container container)
        {
            return data != null && data.IsValid() && !data.m_uid.IsNone() && view && view.IsValid() &&
                container && ReferenceEquals(view.GetZDO(), data) && ReferenceEquals(ContainerView(container), view);
        }

        private static ZNetView ContainerView(Container container)
        { return container.m_rootObjectOverride ? container.m_rootObjectOverride.GetComponent<ZNetView>() : container.GetComponent<ZNetView>(); }

        internal void Forget(ZDO data)
        {
            Request request;
            if (data != null && requests.TryGetValue(data.m_uid, out request) && ReferenceEquals(request.Data, data)) requests.Remove(data.m_uid);
        }
        internal void Clear() { requests.Clear(); }
    }
}
