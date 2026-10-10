using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;

namespace TooManyLags
{
    // Only actual attack vetoes arm a ticket. No Update/physics polling.
    internal static class FgnAttackRetry
    {
        private sealed class Ticket
        {
            internal Humanoid Mob;
            internal ZDO Data;
            internal ZDOMan World;
            internal ZNet Network;
            internal long Owner, Epoch;
            internal uint Revision;
        }
        private static ConditionalWeakTable<Attack, Ticket> waiting = new ConditionalWeakTable<Attack, Ticket>();
        private static Func<Humanoid, Attack> currentAttack;
        private static SynchronizationContext context;
        private static Action<Humanoid, ZDO> retry;
        private static Action<Exception> fail;
        private static readonly SendOrPostCallback dispatch = Dispatch;
        private static long epoch;
        private static int armedCount;

        internal static void Install(Harmony harmony, Action<Humanoid, ZDO> onEnd, Action<Exception> onFailure)
        {
            SynchronizationContext unity = SynchronizationContext.Current;
            FieldInfo field = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
            MethodInfo stop = AccessTools.Method(typeof(Attack), "Stop", Type.EmptyTypes);
            if (unity == null || unity.GetType().FullName != "UnityEngine.UnitySynchronizationContext" ||
                field == null || field.IsStatic || field.FieldType != typeof(Attack) ||
                stop == null || stop.IsStatic || stop.ReturnType != typeof(void))
                throw new NotSupportedException("Native attack-end retry contract unavailable.");
            currentAttack = FieldReader.Create<Humanoid, Attack>(field);
            context = unity; retry = onEnd; fail = onFailure; Reset();
            harmony.Patch(stop, postfix: new HarmonyMethod(AccessTools.Method(typeof(FgnAttackRetry), "Stopped")));
        }

        internal static bool Arm(Character mob, ZDO data)
        {
            Humanoid human = mob as Humanoid;
            if (!human || data == null || currentAttack == null || context == null) return false;
            Attack attack = currentAttack(human);
            if (attack == null || attack.IsDone()) return false;
            Ticket ticket;
            if (waiting.TryGetValue(attack, out ticket))
            {
                if (ticket.Epoch == epoch && ReferenceEquals(ticket.Data, data) &&
                    ReferenceEquals(ticket.World, ZDOMan.instance) && ReferenceEquals(ticket.Network, ZNet.instance) &&
                    ticket.Owner == data.GetOwner() && ticket.Revision == data.OwnerRevision) return true;
                waiting.Remove(attack); armedCount--;
            }
            // Weak keys cannot retain an unloaded actor's abandoned Attack clone.
            waiting.Add(attack, new Ticket { Mob = human, Data = data, World = ZDOMan.instance,
                Network = ZNet.instance, Owner = data.GetOwner(), Revision = data.OwnerRevision, Epoch = epoch });
            armedCount++; return true;
        }

        private static void Stopped(Attack __instance)
        {
            // Native Stop may be called again on an already finished attack.
            // Remove before dispatch, so this event remains strictly one-shot.
            if (armedCount == 0) return;
            try
            {
                if (!__instance.IsDone()) return; // Another prefix may cancel native Stop.
                Ticket ticket;
                if (!waiting.TryGetValue(__instance, out ticket)) return;
                waiting.Remove(__instance); armedCount--;
                context.Post(dispatch, ticket);
            }
            catch (Exception error) { Failed(error); }
        }

        private static void Dispatch(object state)
        {
            try
            {
                Ticket ticket = (Ticket)state;
                if (ticket.Epoch != epoch || !ReferenceEquals(ticket.World, ZDOMan.instance) ||
                    !ReferenceEquals(ticket.Network, ZNet.instance) || ticket.World == null ||
                    !ticket.Mob || !ticket.Data.IsValid() ||
                    !ReferenceEquals(ticket.World.GetZDO(ticket.Data.m_uid), ticket.Data) ||
                    ticket.Data.GetOwner() != ticket.Owner || ticket.Data.OwnerRevision != ticket.Revision ||
                    ticket.Owner != ZDOMan.GetSessionID()) return;
                retry(ticket.Mob, ticket.Data);
            }
            catch (Exception error) { Failed(error); }
        }

        private static void Failed(Exception error) { try { if (fail != null) fail(error); } catch { } }
        internal static void Reset()
        {
            unchecked { epoch++; }
            waiting = new ConditionalWeakTable<Attack, Ticket>(); armedCount = 0;
        }
    }
}
