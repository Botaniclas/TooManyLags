#if TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // Temporary application-layer latency on one authorized solo client's RPC
    // connection. Native socket types/compression stay intact. No sleeping,
    // dropped/reordered gameplay or persistent/global networking settings.
    internal static class SoloNetworkLatency
    {
        private sealed class Packet { internal ZPackage Data; internal double Due; internal int Bytes; }
        private sealed class State
        {
            internal ZNet Network; internal ZDOMan World; internal ZNetPeer Peer; internal ISocket Socket;
            internal readonly Queue<Packet> Outgoing = new Queue<Packet>(), Incoming = new Queue<Packet>();
            internal double Delay, End; internal long Bytes; internal int BasePing, Added;
            internal bool Holding = true;
        }
        private static State state;
        private static Harmony hooks;
        private const int PacketLimit = 8192;
        private const long ByteLimit = 32L * 1024 * 1024;
        internal static bool Active { get { return state != null && state.Holding; } }
        internal static float RoundtripSeconds
        {
            get
            {
                State item = state;
                return item == null || !item.Holding ? 0f :
                    Math.Max(0, item.BasePing + item.Added) / 1000f;
            }
        }

        internal static bool Start(ZNetPeer peer, int target, out int baseline, out int added, out string error)
        {
            baseline = added = 0; error = "";
            if (state != null || !ZNet.instance || ZNet.instance.IsServer() || peer == null || !peer.IsReady() ||
                !peer.m_server || !ReferenceEquals(peer, ZNet.instance.GetServerPeer()) || target < 1 || target > 1000)
            { error = "Latency test requires the current server connection and no active/draining test."; return false; }
            ISocket socket = peer.m_rpc.GetSocket();
            if (socket == null || !socket.IsConnected() || !ReferenceEquals(socket, peer.m_socket))
            { error = "Unsupported RPC/socket binding."; return false; }
            try
            {
                float local, remote, sent, received;
                socket.GetConnectionQuality(out local, out remote, out baseline, out sent, out received);
                if (baseline < 0 || baseline > 60000) throw new NotSupportedException("Transport RTT unavailable.");
                added = Math.Max(0, target - baseline);
                MethodInfo send = AccessTools.Method(typeof(ZRpc), "SendPackage", new[] { typeof(ZPackage) });
                MethodInfo update = AccessTools.Method(typeof(ZRpc), "Update", new[] { typeof(float) });
                MethodInfo quality = socket.GetType().GetMethod("GetConnectionQuality", new[] {
                    typeof(float).MakeByRefType(), typeof(float).MakeByRefType(), typeof(int).MakeByRefType(),
                    typeof(float).MakeByRefType(), typeof(float).MakeByRefType() });
                if (send == null || update == null || quality == null) throw new NotSupportedException("Native latency test sites unavailable.");
                RewriteSend(PatchProcessor.GetOriginalInstructions(send));
                RewriteReceive(PatchProcessor.GetOriginalInstructions(update));
                hooks = new Harmony(Plugin.Id + ".solo-latency");
                hooks.Patch(send, transpiler: new HarmonyMethod(typeof(SoloNetworkLatency), "RewriteSend"));
                hooks.Patch(update, transpiler: new HarmonyMethod(typeof(SoloNetworkLatency), "RewriteReceive"));
                hooks.Patch(quality, postfix: new HarmonyMethod(typeof(SoloNetworkLatency), "Quality"));
                state = new State { Network = ZNet.instance, World = ZDOMan.instance, Peer = peer, Socket = socket,
                    BasePing = baseline, Added = added, Delay = added / 2000d, End = Time.realtimeSinceStartupAsDouble + 300 };
                return true;
            }
            catch (Exception exception) { error = exception.GetBaseException().Message; Reset(); return false; }
        }
        private static bool Current(State item)
        {
            return ReferenceEquals(item.Network, ZNet.instance) && ReferenceEquals(item.World, ZDOMan.instance) &&
                ZNet.instance && ReferenceEquals(item.Peer, ZNet.instance.GetServerPeer()) && item.Peer.IsReady() &&
                ReferenceEquals(item.Socket, item.Peer.m_rpc.GetSocket()) && item.Socket.IsConnected();
        }
        internal static string Describe()
        {
            State item = state;
            return item == null ? "network latency test inactive" :
                "network latency " + (item.Holding ? "active" : "draining") + "; base RTT=" + item.BasePing +
                "ms; added=" + item.Added + "ms round trip (half each direction); outgoing=" + item.Outgoing.Count +
                "; incoming=" + item.Incoming.Count + ". Frame scheduling and real RTT variation still apply.";
        }
        internal static void Poll()
        {
            State item = state; if (item == null) return;
            if (!Current(item)) { Reset(); return; }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= item.End) item.Holding = false;
            Flush(item, now); Finish(item);
        }
        internal static void End()
        {
            State item = state; if (item == null) return;
            if (!Current(item)) { Reset(); return; }
            item.Holding = false; Flush(item, Time.realtimeSinceStartupAsDouble); Finish(item);
        }
        internal static void Reset()
        {
            state = null;
            if (hooks != null) { try { hooks.UnpatchSelf(); } finally { hooks = null; } }
        }
        private static void Finish(State item)
        { if (!item.Holding && item.Outgoing.Count == 0 && item.Incoming.Count == 0 && ReferenceEquals(state, item)) Reset(); }
        private static void Flush(State item, double now)
        {
            while (item.Outgoing.Count != 0 && (!item.Holding || item.Outgoing.Peek().Due <= now))
            {
                Packet packet = item.Outgoing.Dequeue(); item.Bytes -= packet.Bytes;
                // Call the untouched concrete socket/compression chain once.
                // Never replay an arbitrary gameplay action if a third-party
                // socket hook throws after already sending it.
                item.Socket.Send(packet.Data);
            }
        }
        private static Packet Copy(ZPackage data, double due)
        {
            ZPackage copy = new ZPackage(data.GetArray()); copy.SetPos(data.GetPos());
            return new Packet { Data = copy, Due = due, Bytes = copy.Size() };
        }
        private static void Bound(State item)
        {
            if (item.Bytes <= ByteLimit && item.Outgoing.Count + item.Incoming.Count <= PacketLimit) return;
            item.Holding = false;
            Plugin.Warn("Artificial latency ended at its buffer limit; queued messages drain in order. " + Describe());
        }
        private static void Send(ISocket socket, ZPackage data)
        {
            State item = state;
            if (item == null || !ReferenceEquals(item.Socket, socket)) { socket.Send(data); return; }
            if (!Current(item)) { Reset(); socket.Send(data); return; }
            double now = Time.realtimeSinceStartupAsDouble;
            if (!item.Holding || item.Delay <= 0) { Flush(item, now); socket.Send(data); Finish(item); return; }
            Packet packet = Copy(data, now + item.Delay);
            item.Outgoing.Enqueue(packet); item.Bytes += packet.Bytes; Bound(item); Flush(item, now);
        }
        private static ZPackage Receive(ISocket socket)
        {
            State item = state;
            if (item == null || !ReferenceEquals(item.Socket, socket)) return socket.Recv();
            if (!Current(item)) { Reset(); return socket.Recv(); }
            double now = Time.realtimeSinceStartupAsDouble;
            if (item.Incoming.Count != 0 && (!item.Holding || item.Incoming.Peek().Due <= now))
            {
                Packet ready = item.Incoming.Dequeue(); item.Bytes -= ready.Bytes; Finish(item); return ready.Data;
            }
            if (!item.Holding || item.Delay <= 0) { Finish(item); return socket.Recv(); }
            // Read/decompress each native packet once. Timestamp at this read,
            // not at dequeue; never decompress a queued packet for a second time.
            for (int i = 0; i < 64; i++)
            {
                ZPackage received = socket.Recv(); if (received == null) break;
                Packet packet = Copy(received, now + item.Delay);
                item.Incoming.Enqueue(packet); item.Bytes += packet.Bytes; Bound(item);
                if (!item.Holding) break;
            }
            return null;
        }
        private static void Quality(object __instance, ref int __2)
        {
            State item = state;
            if (item != null && item.Holding && ReferenceEquals(item.Socket, __instance)) __2 = Math.Max(0, __2) + item.Added;
        }
        private static List<CodeInstruction> RewriteSend(IEnumerable<CodeInstruction> source)
        { return Rewrite(source, "Send", new[] { typeof(ZPackage) }, "Send", 1); }
        private static List<CodeInstruction> RewriteReceive(IEnumerable<CodeInstruction> source)
        { return Rewrite(source, "Recv", Type.EmptyTypes, "Receive", 2); }
        private static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source, string native, Type[] arguments, string replacement, int expected)
        {
            MethodInfo method = typeof(ISocket).GetMethod(native, arguments); int count = 0;
            List<CodeInstruction> result = new List<CodeInstruction>();
            foreach (CodeInstruction original in source)
            {
                CodeInstruction item = new CodeInstruction(original);
                if (method.Equals(item.operand)) { item.opcode = OpCodes.Call; item.operand = AccessTools.Method(typeof(SoloNetworkLatency), replacement); count++; }
                result.Add(item);
            }
            if (count != expected) throw new NotSupportedException("Native RPC " + native + " contract changed; artificial latency unavailable.");
            return result;
        }
    }
}
#endif
