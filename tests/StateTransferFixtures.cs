namespace TooManyLags.NetworkSavings
{
    // Low-level native IL is executed separately against the installed game.
    // Controller fixtures do not emulate the serializer or its transport.
    internal static class StateTransfer
    {
        internal static bool Active { get { return false; } }
        internal static void Start(System.Reflection.Assembly fgn) { }
        internal static void Stop() { }
        internal static void Reset() { }
        internal static void Connect(ZNetPeer peer) { }
        internal static void Enable(ZRpc rpc, bool supported) { }
    }
}
