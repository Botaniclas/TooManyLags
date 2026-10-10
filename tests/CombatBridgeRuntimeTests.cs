using System;
using System.Collections.Generic;
using TooManyLags;

// Run the production bridge with a modern Dictionary API. The ordinary .NET
// Framework fixture has only Remove(key) and cannot reproduce Unity's overload.
internal static class FgnBridgeRuntimeTests
{
    private static int checks;
    private static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    public static void Main()
    {
        try
        {
            bool ambiguous = false;
            try { typeof(Dictionary<ZDOID, int>).GetMethod("Remove"); }
            catch (System.Reflection.AmbiguousMatchException) { ambiguous = true; }
            Check(ambiguous, "host must reproduce the ambiguous name-only Remove lookup");
            FgnCombatBridge bridge = new FgnCombatBridge(typeof(FiresGhettoNetworkMod.CreatureOwnership).Assembly);
            Check(bridge.ClearStreak != null && bridge.ClearOwned != null && bridge.TargetReader != null,
                "complete production bridge initializes under overloaded Dictionary API");
            ZDOID first = new ZDOID(91, 1), other = new ZDOID(91, 2);
            FiresGhettoNetworkMod.CreatureOwnership.Clear();
            FiresGhettoNetworkMod.CreatureOwnership.Streak(first);
            FiresGhettoNetworkMod.CreatureOwnership.Streak(other);
            bridge.ClearStreak(first);
            Check(!FiresGhettoNetworkMod.CreatureOwnership.HaveStreak(first), "selected streak removed");
            Check(FiresGhettoNetworkMod.CreatureOwnership.HaveStreak(other), "unrelated streak retained");
            bridge.ClearStreak(first);
            Check(FiresGhettoNetworkMod.CreatureOwnership.HaveStreak(other), "absent-key removal harmless");
            FiresGhettoNetworkMod.CreatureOwnership.TrackOwned(first, 1, 10);
            FiresGhettoNetworkMod.CreatureOwnership.TrackOwned(other, 2, 12);
            bridge.ClearOwned(first);
            Check(Double.IsNaN(FiresGhettoNetworkMod.CreatureOwnership.OwnedSince(first)), "selected hold removed");
            Check(FiresGhettoNetworkMod.CreatureOwnership.OwnedSince(other) == 12, "unrelated hold retained");
            bridge.MarkPending(first, 1, 20);
            Check(bridge.Pending(first, 24), "existing pending reader/writer works after binding");
            Check(!bridge.Pending(first, 25), "native five-second pending expiry preserved");
            Check(!bridge.Pending(other, 24), "unrelated pending key absent");
            Console.WriteLine("PASS: " + checks + " production bridge assertions with ambiguous Dictionary.Remove overloads; managed doubles, not Unity.");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
}

namespace TooManyLags
{
    // Ownership is outside this bridge-binding test. No Harmony/static Unity
    // initializers are invoked; the normal combat suite tests its real service.
    internal static class FgnCombatOwnership
    {
        internal static bool ShouldOverride(ZDOID id) { return false; }
        internal static bool TryTargetPeer(ZDOID id, out long peer) { peer = 0; return false; }
    }
}
