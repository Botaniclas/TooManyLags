using System;
using TooManyLags;

// Lightweight behavior fixtures for the production bridge. Native container
// method bodies are tested separately by the original-IL container probe.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static implicit operator bool(Object value) { return value != null && !value.Destroyed; }
    }
    public static class Time { public static float unscaledTime; }
}
public struct ZDOID : IEquatable<ZDOID>
{
    public int Id;
    public ZDOID(int id) { Id = id; }
    public bool IsNone() { return Id == 0; }
    public bool Equals(ZDOID value) { return Id == value.Id; }
    public override bool Equals(object value) { return value is ZDOID && Equals((ZDOID)value); }
    public override int GetHashCode() { return Id; }
}
public class ZDO
{
    public ZDOID m_uid;
    public ushort OwnerRevision;
    public long Owner;
    public bool Valid = true;
    public bool IsValid() { return Valid; }
    public long GetOwner() { return Owner; }
}
public class ZNetView : UnityEngine.Object
{
    public ZDO Data;
    public Action<long, string, object[]> Invoke;
    public bool IsValid() { return !Destroyed && Data != null && Data.Valid; }
    public ZDO GetZDO() { return Data; }
    public T GetComponent<T>() where T : class { return this as T; }
    public void InvokeRPC(long target, string method, params object[] arguments) { Invoke(target, method, arguments); }
}
public class Container : UnityEngine.Object
{
    public enum PrivacySetting { Private, Group, Public }
    public PrivacySetting m_privacy = PrivacySetting.Public;
    public ZNetView m_rootObjectOverride, View;
    public T GetComponent<T>() where T : class { return View as T; }
}
internal static class NativeInventoryTests
{
    private sealed class Case
    {
        internal readonly NativeInventoryHandoff Bridge = new NativeInventoryHandoff(1);
        internal readonly ZDO Data = new ZDO { m_uid = new ZDOID(10), Owner = 2, OwnerRevision = 7 };
        internal readonly ZNetView View = new ZNetView();
        internal readonly Container Container = new Container();
        internal int Sends;
        internal bool Busy, Throw;
        internal Action Sent;
        internal Case()
        {
            UnityEngine.Time.unscaledTime = 0; View.Data = Data; Container.View = View;
            View.Invoke = delegate(long target, string method, object[] args)
            {
                Sends++;
                Require(Bridge.Sending, "Sending active around outgoing RPC");
                Require(target == 2 && method == "RPC_RequestOpen" && args.Length == 1 && (long)args[0] == 0L, "native owner request with public profile argument");
                if (Throw) throw new InvalidOperationException("transport fixture");
                if (Busy) Bridge.Response(Container, 2, false);
                if (Sent != null) Sent();
            };
        }
        internal int Acquire() { return Bridge.Acquire(Data, View, Container); }
        internal void Commit() { Data.Owner = 1; Data.OwnerRevision = 8; }
    }
    private static int assertions;
    private static void Require(bool success, string name)
    { assertions++; if (!success) throw new Exception(name); }
    public static int Main()
    {
        try
        {
            Case c = new Case(); c.Busy = true;
            Require(c.Acquire() == 0 && c.Data.Owner == 2, "source busy denial retains native owner");
            Require(!c.Bridge.Pending(c.Data), "denial releases reservation");
            Require(c.Acquire() == 0 && c.Sends == 1, "denial retry backoff");
            UnityEngine.Time.unscaledTime = 2; c.Busy = false;
            Require(c.Acquire() == 1 && c.Sends == 2, "denied request retries only when due");
            Require(c.Acquire() == 1 && c.Sends == 2, "no duplicate request per active revision");

            c = new Case(); c.Sent = delegate { c.Commit(); c.Bridge.Response(c.Container, 2, true); };
            Require(c.Acquire() == 1, "send result remains pending until next evaluation");
            Require(c.Acquire() == 2 && c.Bridge.Received(c.Data) && !c.Bridge.Pending(c.Data), "idle native grant completes matching receipt");
            c.Bridge.Response(c.Container, 2, false);
            Require(c.Acquire() == 2, "late denial cannot revoke a completed grant");

            c = new Case(); Require(c.Acquire() == 1, "request starts");
            c.Bridge.Response(c.Container, 2, true);
            Require(c.Bridge.Pending(c.Data) && !c.Bridge.Received(c.Data) && c.Acquire() == 1, "response before data cannot authorize a write");
            c.Commit(); Require(c.Acquire() == 2, "matching later data completes native grant");

            c = new Case(); c.Acquire(); c.Bridge.Response(c.Container, 2, true);
            UnityEngine.Time.unscaledTime = 7;
            Require(c.Acquire() == 0 && c.Sends == 1 && !c.Bridge.Pending(c.Data), "accepted native grant is retained without resend while receipt is delayed");
            c.Commit(); Require(c.Acquire() == 2, "late owner receipt still pairs with previously accepted grant");

            c = new Case(); c.Acquire(); c.Commit();
            Require(c.Bridge.Pending(c.Data) && c.Bridge.Received(c.Data) && c.Acquire() == 1, "data before response stays pinned");
            c.Bridge.Response(c.Container, 99, true);
            Require(c.Acquire() == 1, "wrong response source ignored");
            c.Bridge.Response(new Container { View = c.View }, 2, true);
            Require(c.Acquire() == 1, "wrong container instance ignored");
            c.Bridge.Response(c.Container, 2, true); Require(c.Acquire() == 2, "exact later source response completes");

            c = new Case(); c.Acquire(); c.Bridge.Response(c.Container, 2, true);
            c.Data.OwnerRevision = 8;
            Require(c.Bridge.Transitioning(c.Data) && !c.Bridge.Received(c.Data) && c.Bridge.Pending(c.Data), "revision-first import retains exact source/committedRevision half tuple");
            Require(c.Acquire() == 1 && c.Sends == 1, "revision-first half tuple cannot resend or complete");
            c.Data.Owner = 1;
            Require(!c.Bridge.Transitioning(c.Data) && c.Bridge.Received(c.Data) && c.Acquire() == 2, "revision-first completed import still recognizes original request");

            c = new Case(); c.Acquire(); c.Bridge.Response(c.Container, 2, true);
            c.Data.Owner = 1;
            Require(c.Bridge.Transitioning(c.Data) && !c.Bridge.Received(c.Data) && c.Bridge.Pending(c.Data), "owner-first import retains exact server/sourceRevision half tuple");
            Require(c.Acquire() == 1 && c.Sends == 1, "owner-first half tuple cannot resend or complete");
            c.Data.OwnerRevision = 8;
            Require(!c.Bridge.Transitioning(c.Data) && c.Bridge.Received(c.Data) && c.Acquire() == 2, "owner-first completed import still recognizes original request");

            c = new Case(); c.Acquire(); c.Data.Owner = 1;
            Require(c.Bridge.Transitioning(c.Data) && !c.Bridge.Received(c.Data), "owner-first half tuple survives without response");
            c.Data.OwnerRevision = 8;
            Require(c.Bridge.Received(c.Data) && c.Bridge.Pending(c.Data) && c.Acquire() == 1, "complete import without response remains pinned");
            c.Bridge.Response(c.Container, 2, true);
            Require(c.Acquire() == 2, "response after half-observed import completes");

            c = new Case(); c.Acquire(); c.Bridge.Response(c.Container, 2, true); c.Data.Owner = 1;
            UnityEngine.Time.unscaledTime = 7;
            Require(c.Bridge.Transitioning(c.Data) && !c.Bridge.Pending(c.Data) && !c.Bridge.Received(c.Data) && c.Acquire() == 0 && c.Sends == 1, "even granted owner-first half tuple times out without resend or completion");
            c.Data.OwnerRevision = 8;
            Require(c.Acquire() == 2, "retained positive reply can complete a subsequently finished import");

            c = new Case(); c.Acquire(); c.Data.OwnerRevision = 8;
            UnityEngine.Time.unscaledTime = 7;
            Require(c.Bridge.Transitioning(c.Data) && !c.Bridge.Pending(c.Data) && c.Acquire() == 0 && c.Sends == 1, "revision-first half tuple timeout never retries former-source revision");
            c.Data.Owner = 1; c.Bridge.Response(c.Container, 2, true);
            Require(c.Acquire() == 2, "late exact receipt and response recover after half tuple timeout");

            c = new Case(); c.Acquire(); c.Data.Owner = 1; c.Data.OwnerRevision = 9;
            Require(!c.Bridge.Transitioning(c.Data) && !c.Bridge.Received(c.Data) && !c.Bridge.Pending(c.Data), "wrong server revision is not a tolerated half tuple");

            c = new Case(); c.Acquire(); c.Data.OwnerRevision = 9;
            c.Bridge.Response(c.Container, 2, true);
            Require(!c.Bridge.Pending(c.Data) && !c.Bridge.Received(c.Data) && c.Data.Owner == 2, "changed native revision cancels stale request without write");
            c = new Case(); c.Acquire(); c.Data.Owner = 3;
            c.Bridge.Response(c.Container, 2, true);
            Require(!c.Bridge.Pending(c.Data) && !c.Bridge.Received(c.Data), "changed owner cancels stale source");

            c = new Case(); c.Acquire();
            ZDO replacement = new ZDO { m_uid = c.Data.m_uid, Owner = 2, OwnerRevision = 7 };
            Require(!c.Bridge.Pending(replacement) && !c.Bridge.Received(replacement), "same id with different ZDO identity is not the request");
            c.View.Data = replacement; c.Bridge.Response(c.Container, 2, true);
            Require(!c.Bridge.Received(replacement), "replacement view cannot consume old response");

            c = new Case(); c.Acquire(); UnityEngine.Time.unscaledTime = 5;
            Require(!c.Bridge.Pending(c.Data) && c.Acquire() == 0 && c.Data.Owner == 2 && c.Sends == 1, "timeout releases reservation and does not steal");
            c.Bridge.Response(c.Container, 2, true);
            Require(c.Acquire() == 0, "expired positive reply ignored");
            UnityEngine.Time.unscaledTime = 7;
            Require(c.Acquire() == 1 && c.Sends == 2 && c.Data.Owner == 2, "lost request retry remains a native request");

            c = new Case(); c.Acquire(); c.Commit(); UnityEngine.Time.unscaledTime = 5;
            Require(!c.Bridge.Pending(c.Data) && c.Bridge.Received(c.Data) && c.Acquire() == 0, "lost response cannot complete solely from owner data");
            UnityEngine.Time.unscaledTime = 10;
            Require(c.Acquire() == 0 && c.Sends == 1, "no request resent to former source after unconfirmed native commit");
            c.Bridge.Response(c.Container, 99, true);
            Require(c.Acquire() == 0 && !c.Bridge.Pending(c.Data), "late grant from wrong sender cannot confirm server receipt");
            c.Bridge.Response(c.Container, 2, false);
            Require(c.Acquire() == 0 && !c.Bridge.Pending(c.Data), "late denial never confirms or re-pins server receipt");
            c.Bridge.Response(c.Container, 2, true);
            Require(c.Acquire() == 2 && !c.Bridge.Pending(c.Data) && c.Sends == 1, "late matching positive confirms exact server receipt without re-pin or resend");

            c = new Case(); c.Acquire(); c.Commit(); UnityEngine.Time.unscaledTime = 7;
            Require(!c.Bridge.Pending(c.Data) && c.Acquire() == 0, "missing reply expires reservation for a normal native player opening");
            // Ordinary native RequestOpen can now commit a real player's ownership.
            c.Data.Owner = 3; c.Data.OwnerRevision = 9;
            c.Bridge.Response(c.Container, 2, true);
            Require(c.Data.Owner == 3 && c.Data.OwnerRevision == 9 && !c.Bridge.Received(c.Data) && !c.Bridge.Pending(c.Data), "late old grant cannot override a subsequent native player opening");

            c = new Case(); c.Acquire(); c.Commit(); UnityEngine.Time.unscaledTime = 7;
            Require(!c.Bridge.Pending(c.Data), "receipt-only reservation expires at deadline");
            c.Data.OwnerRevision = 9; c.Bridge.Response(c.Container, 2, true);
            Require(!c.Bridge.Received(c.Data), "late positive rejected when intermediate server revision changed");

            c = new Case(); c.Container.m_privacy = Container.PrivacySetting.Private;
            Require(c.Acquire() == 0 && c.Sends == 0 && c.Data.Owner == 2, "private access remains native with no profile spoof");
            c.Container.m_privacy = Container.PrivacySetting.Group;
            Require(c.Acquire() == 0 && c.Sends == 0, "group privacy remains native");
            c = new Case(); c.Data.OwnerRevision = UInt16.MaxValue;
            Require(c.Acquire() == 0 && c.Sends == 0, "no native revision wrap request");
            c = new Case(); c.Data.Owner = 0;
            Require(c.Acquire() == 0 && c.Sends == 0, "ownerless has no remote source");

            c = new Case(); c.Container.m_rootObjectOverride = new ZNetView { Data = c.Data };
            Require(c.Acquire() == 0 && c.Sends == 0, "mismatched root override rejected");
            c.Container.m_rootObjectOverride = c.View;
            Require(c.Acquire() == 1, "matching root override supported");
            c.Container.Destroyed = true;
            Require(!c.Bridge.Pending(c.Data), "destroyed container cancels reservation");

            c = new Case(); c.Throw = true;
            try { c.Acquire(); throw new Exception("missing transport error"); }
            catch (InvalidOperationException) { }
            Require(!c.Bridge.Sending && !c.Bridge.Pending(c.Data), "send failure always releases Sending guard");
            c = new Case(); c.Acquire(); c.Bridge.Forget(c.Data);
            Require(!c.Bridge.Pending(c.Data) && !c.Bridge.Received(c.Data), "forget releases exact object");
            c.Acquire(); c.Bridge.Clear();
            Require(!c.Bridge.Pending(c.Data), "clear drops all world reservations");
            Console.WriteLine("PASS: " + assertions + " native inventory bridge fixture assertions.");
            Console.WriteLine("LIMIT: No Unity frame scheduling, live transport or other-mod behavior exercised.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
