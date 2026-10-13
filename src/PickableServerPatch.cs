using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TooManyLags
{
    // Native Pickable.RPC_Pick uses a local player's ID for its effect. Dedicated
    // servers have no local player. Leave harvesting/drops/bonuses entirely native.
    [HarmonyPatch(typeof(Pickable), "RPC_Pick")]
    internal static class PickableServerPatch
    {
        internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source)
        {
            var result = new List<CodeInstruction>();
            foreach (CodeInstruction item in source) result.Add(new CodeInstruction(item));
            FieldInfo player = typeof(Player).GetField("m_localPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo id = typeof(Character).GetMethod("GetZDOID", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            MethodInfo actor = typeof(PickableServerPatch).GetMethod("EffectActor", BindingFlags.Static | BindingFlags.NonPublic);
            if (player == null || id == null || actor == null) throw new NotSupportedException("Native Pickable actor API changed.");
            int changed = 0, alreadySafe = 0;
            for (int i = 0; i + 1 < result.Count; i++)
            {
                MethodInfo safe = result[i].operand as MethodInfo;
                // Newer FGN already replaces this pair with its null-safe
                // helper and a nop. Keep its patch; reject unknown IL shapes.
                if (result[i].opcode == OpCodes.Call && safe != null && safe.IsStatic &&
                    safe.DeclaringType != null && safe.DeclaringType.FullName == "FiresGhettoNetworkMod.ServerSafePick" &&
                    safe.Name == "LocalPlayerZdoid" && safe.ReturnType == typeof(ZDOID) && safe.GetParameters().Length == 0 &&
                    result[i + 1].opcode == OpCodes.Nop && result[i + 1].operand == null)
                { alreadySafe++; continue; }
                if (result[i].opcode != OpCodes.Ldsfld || !player.Equals(result[i].operand) ||
                    result[i + 1].opcode != OpCodes.Callvirt || !id.Equals(result[i + 1].operand)) continue;
                result[i].opcode = OpCodes.Ldarg_1; result[i].operand = null;
                result[i + 1].opcode = OpCodes.Call; result[i + 1].operand = actor; changed++;
            }
            if (changed + alreadySafe != 1) throw new NotSupportedException("Native Pickable effect actor contract changed.");
            return result;
        }
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source) { return Rewrite(source); }
        internal static ZDOID EffectActor(long sender)
        {
            if (Player.m_localPlayer) return Player.m_localPlayer.GetZDOID();
            ZNetPeer peer = ZNet.instance ? ZNet.instance.GetPeer(sender) : null;
            // A departed peer has no current actor; the effect already supports None.
            return peer == null ? default(ZDOID) : peer.m_characterID;
        }
    }
}
